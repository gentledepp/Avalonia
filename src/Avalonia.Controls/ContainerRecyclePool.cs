using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace Avalonia.Controls
{
    /// <summary>
    /// Recycle pools per recycle key for the containers of a <see cref="VirtualizingPanel"/>,
    /// and the warmup that fills them.
    /// </summary>
    /// <remarks>
    /// A pooled container stays a child of the panel with <see cref="Visual.IsVisible"/> set to
    /// false. Use it with a panel that implements <see cref="IContentRecyclingPanel"/>.
    /// </remarks>
    public class ContainerRecyclePool
    {
        /// <summary>
        /// Defines the EnableWarmup attached property.
        /// </summary>
        public static readonly AttachedProperty<bool> EnableWarmupProperty =
            AvaloniaProperty.RegisterAttached<ContainerRecyclePool, VirtualizingPanel, bool>(
                "EnableWarmup",
                defaultValue: false);

        /// <summary>
        /// Defines the WarmupSampleSize attached property.
        /// </summary>
        public static readonly AttachedProperty<int> WarmupSampleSizeProperty =
            AvaloniaProperty.RegisterAttached<ContainerRecyclePool, VirtualizingPanel, int>(
                "WarmupSampleSize",
                defaultValue: 50,
                validate: v => v > 0 && v <= 1000);

        internal static readonly AttachedProperty<object?> RecycleKeyProperty =
            AvaloniaProperty.RegisterAttached<ContainerRecyclePool, Control, object?>("RecycleKey");

        private readonly VirtualizingPanel _owner;
        private readonly Func<Size>? _getWarmupAvailableSize;
        private Dictionary<object, List<Control>>? _recyclePool;
        private bool _isWarmupComplete;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerRecyclePool"/> class.
        /// </summary>
        /// <param name="owner">The panel whose containers are pooled.</param>
        /// <param name="getWarmupAvailableSize">
        /// The size to measure warmup containers with. Default: the panel's width and an infinite height.
        /// </param>
        public ContainerRecyclePool(VirtualizingPanel owner, Func<Size>? getWarmupAvailableSize = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _getWarmupAvailableSize = getWarmupAvailableSize;
        }

        internal IReadOnlyDictionary<object, List<Control>>? RecyclePoolForTesting => _recyclePool;

        private IReadOnlyList<object?> Items => _owner.GetItems();

        private ItemsControl? ItemsControl => _owner.GetItemsControl();

        /// <summary>
        /// Gets the value of the <see cref="EnableWarmupProperty"/> attached property.
        /// </summary>
        public static bool GetEnableWarmup(VirtualizingPanel panel) => panel.GetValue(EnableWarmupProperty);

        /// <summary>
        /// Sets the value of the <see cref="EnableWarmupProperty"/> attached property.
        /// </summary>
        public static void SetEnableWarmup(VirtualizingPanel panel, bool value) => panel.SetValue(EnableWarmupProperty, value);

        /// <summary>
        /// Gets the value of the <see cref="WarmupSampleSizeProperty"/> attached property.
        /// </summary>
        public static int GetWarmupSampleSize(VirtualizingPanel panel) => panel.GetValue(WarmupSampleSizeProperty);

        /// <summary>
        /// Sets the value of the <see cref="WarmupSampleSizeProperty"/> attached property.
        /// </summary>
        public static void SetWarmupSampleSize(VirtualizingPanel panel, int value) => panel.SetValue(WarmupSampleSizeProperty, value);

        /// <summary>
        /// Creates and prepares a new container and adds it to the panel's children.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="index">The index of the item.</param>
        /// <param name="recycleKey">The recycle key from <c>ItemContainerGenerator.NeedsContainer</c>.</param>
        public Control CreateContainer(object? item, int index, object? recycleKey)
        {
            var generator = _owner.ItemContainerGenerator!;
            var container = generator.CreateContainer(item, index, recycleKey);

            container.SetValue(RecycleKeyProperty, recycleKey);
            generator.PrepareItemContainer(container, item, index);
            _owner.AddPooledChild(container);
            generator.ItemContainerPrepared(container, item, index);

            return container;
        }

        /// <summary>
        /// Takes a container from the pool for <paramref name="recycleKey"/>, makes it visible and
        /// prepares it for the item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="index">The index of the item.</param>
        /// <param name="recycleKey">The recycle key from <c>ItemContainerGenerator.NeedsContainer</c>.</param>
        /// <returns>The container, or null if the pool is empty.</returns>
        /// <remarks>
        /// A pooled container whose <see cref="StyledElement.DataContext"/> is the item is preferred.
        /// </remarks>
        public Control? GetRecycledContainer(object? item, int index, object? recycleKey)
        {
            if (recycleKey is null)
                return null;

            var generator = _owner.ItemContainerGenerator!;

            if (_recyclePool?.TryGetValue(recycleKey, out var recyclePool) == true && recyclePool.Count > 0)
            {
                // edge case: The item is already datacontext of a recyclable item
                var recycleIndex = recyclePool.Count - 1;
                for (int i = 0; i < recyclePool.Count; i++)
                {
                    if (recyclePool[i].DataContext == item)
                    {
                        recycleIndex = i;
                        break;
                    }
                }

                var recycled = recyclePool[recycleIndex];
                recyclePool.RemoveAt(recycleIndex);
                recycled.SetCurrentValue(Visual.IsVisibleProperty, true);

                // Detect whether this pooled container is being reused for a *different* item.
                // For IVirtualizingDataTemplate the container is not cleared on recycle and the
                // same child instance is reused, so a container reused for a new item keeps its
                // previous content's cached layout. Making the container visible invalidates the
                // container's own measure, but its content subtree is still IsMeasureValid at the
                // same available size and would short-circuit re-measure — leaving the new item
                // arranged with the previous item's size (content rendered blank / clipped, or
                // text not re-wrapped). Force the reused subtree to re-measure in that case.
                // When reused for the SAME item, the cached layout is still correct — skip the work.
                var dataContextChanged = !ReferenceEquals(recycled.DataContext, item);

                generator.PrepareItemContainer(recycled, item, index);
                generator.ItemContainerPrepared(recycled, item, index);

                if (dataContextChanged)
                    InvalidateMeasureRecursive(recycled);

                return recycled;
            }

            return null;
        }

        /// <summary>
        /// Clears a container and puts it into the pool for its recycle key.
        /// </summary>
        /// <param name="container">A container created by <see cref="CreateContainer"/>.</param>
        /// <remarks>
        /// A container without a recycle key, or whose pool is full, is removed from the panel's
        /// children.
        /// </remarks>
        public void RecycleContainer(Control container)
        {
            var recycleKey = container.GetValue(RecycleKeyProperty);

            if (recycleKey is null)
            {
                _owner.RemovePooledChild(container);
                return;
            }

            _owner.ItemContainerGenerator!.ClearItemContainer(container);

            if (PushToRecyclePool(recycleKey, container))
                container.SetCurrentValue(Visual.IsVisibleProperty, false);
            else
                _owner.RemovePooledChild(container);
        }

        /// <summary>
        /// Removes all pooled containers from the panel's children and clears the pools.
        /// </summary>
        public void Clear()
        {
            if (_recyclePool is null || _recyclePool.Count == 0)
                return;

            foreach (var pool in _recyclePool.Values)
            {
                for (var i = pool.Count - 1; i >= 0; i--)
                    _owner.RemovePooledChild(pool[i]);
                pool.Clear();
            }

            _recyclePool.Clear();
        }

        /// <summary>
        /// Schedules the warmup when the panel is attached to the visual tree.
        /// </summary>
        public void OnAttachedToVisualTree()
        {
            // Schedule warmup after initial render if enabled
            if (GetEnableWarmup(_owner) && !_isWarmupComplete)
            {
                Threading.Dispatcher.UIThread.Post(PerformWarmup, Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Schedules the warmup when the first items are added.
        /// </summary>
        /// <param name="items">The items.</param>
        /// <param name="e">The collection change.</param>
        public void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
        {
            // Handle async collection loading - trigger warmup when first items become available
            if (GetEnableWarmup(_owner) && !_isWarmupComplete && items.Count > 0 && e.Action == NotifyCollectionChangedAction.Add)
            {
                if (_recyclePool == null || _recyclePool.Count == 0)
                {

                    Threading.Dispatcher.UIThread.Post(PerformWarmup, Threading.DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// Removes pooled warmup containers of keys that are no longer used and schedules the
        /// warmup again if a pool is below its minimum size. Call it after the panel recycled its
        /// containers for a reset.
        /// </summary>
        /// <param name="items">The items.</param>
        public void OnItemsReset(IReadOnlyList<object?> items)
        {
            // WARMUP OPTIMIZATION: After reset, clear only obsolete keys and top-up if needed
            if (GetEnableWarmup(_owner) && _isWarmupComplete && items.Count > 0)
            {
                // Clear only containers whose keys are no longer in the new collection
                ClearObsoleteWarmupContainers();

                // Discover what keys we need now
                var currentKeys = DiscoverTemplateKeys();

                // Check if we need to warm up any new keys or top-up existing ones
                bool needsWarmup = false;
                foreach (var kvp in currentKeys)
                {
                    var existingCount = _recyclePool?.TryGetValue(kvp.Key, out var pool) == true
                        ? pool.Count
                        : 0;

                    if (existingCount < kvp.Value)
                    {
                        needsWarmup = true;
                        break;
                    }
                }

                if (needsWarmup)
                {

                    _isWarmupComplete = false;
                    Threading.Dispatcher.UIThread.Post(PerformWarmup, Threading.DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// Removes the unused warmup containers and schedules the warmup again. Call it when the
        /// <see cref="ItemsControl.ItemTemplate"/> changes.
        /// </summary>
        public void OnItemTemplateChanged()
        {
            if (GetEnableWarmup(_owner) && _isWarmupComplete)
            {

                ClearWarmupContainers();
                _isWarmupComplete = false;

                Threading.Dispatcher.UIThread.Post(PerformWarmup, Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Discovers unique template types/keys by sampling items from the collection.
        /// Returns a dictionary mapping recycle keys to target warmup counts.
        /// </summary>
        internal Dictionary<object, int> DiscoverTemplateKeys()
        {
            var templateKeys = new Dictionary<object, int>();
            var items = Items;
            var generator = _owner.ItemContainerGenerator;

            if (items == null || items.Count == 0 || generator == null)
                return templateKeys;

            // Sample first N items to discover template types
            int sampleSize = Math.Min(GetWarmupSampleSize(_owner), items.Count);

            for (int i = 0; i < sampleSize; i++)
            {
                var item = items[i];

                // Use ItemContainerGenerator to determine recycle key without creating container
                if (generator.NeedsContainer(item, i, out var recycleKey) && recycleKey != null)
                    CollectionsMarshal.GetValueRefOrAddDefault(templateKeys, recycleKey, out _)++;
            }

            // Query MaxPoolSizePerKey from IVirtualizingDataTemplate if available
            if (ItemsControl?.ItemTemplate is IVirtualizingDataTemplate vdt)
            {
                foreach (var key in templateKeys.Keys.ToList())
                {
                    templateKeys[key] = vdt.MinPoolSizePerKey;
                }

            }
            else
            {
                // Default to 3 containers per type if no MaxPoolSizePerKey available
                foreach (var key in templateKeys.Keys.ToList())
                {
                    templateKeys[key] = 3;
                }
            }

            return templateKeys;
        }

        /// <summary>
        /// Pre-creates containers with their content for each discovered template type.
        /// Containers are stored in the recycle pool with their Child controls already attached,
        /// ready to be reused during scrolling. This eliminates the expensive template instantiation
        /// cost during the first scroll.
        /// </summary>
        internal void PerformWarmup()
        {
            if (_isWarmupComplete || Items == null || Items.Count == 0)
                return;

            var templateKeys = DiscoverTemplateKeys();

            if (templateKeys.Count == 0)
            {
                _isWarmupComplete = true;
                return;
            }

            _recyclePool ??= new Dictionary<object, List<Control>>();

            var availableSize = _getWarmupAvailableSize?.Invoke() ??
                new Size(_owner.Bounds.Width > 0 ? _owner.Bounds.Width : double.PositiveInfinity, double.PositiveInfinity);

            var realizedElements = _owner.GetRealizedContainers()?.ToList();
            int alreadyRealized = realizedElements?.Count ?? 0;
            Dictionary<object, List<Control>> realizedElementsLookup = new();
            if (realizedElements is not null)
            {
                realizedElementsLookup = realizedElements
                    .GroupBy(re => re.GetValue(RecycleKeyProperty))
                    .ToDictionary(g => g.Key??new object(), g => g.ToList());

            }

            foreach (var kvp in templateKeys)
            {
                var recycleKey = kvp.Key;
                var targetCount = kvp.Value;

                // OPTIMIZATION: Check existing pool size
                var existingCount = _recyclePool.TryGetValue(recycleKey, out var existingPool)
                    ? existingPool.Count
                    : 0;
                // OPTIMIZATION 2: Check realized elements
                if (realizedElementsLookup.TryGetValue(recycleKey, out var realized))
                    existingCount += realized.Count;

                var neededCount = Math.Max(0, targetCount - existingCount);

                if (neededCount == 0)
                {
                    continue;
                }

                // Collect actual items from the ItemsSource that match this recycle key
                // CHANGED: Only collect neededCount items, not targetCount
                var matchingItems = new List<(object item, int index)>();
                var startIndex = Math.Max(alreadyRealized - 1, 0);
                for (int i = startIndex; i < Math.Min(GetWarmupSampleSize(_owner)+alreadyRealized, Items.Count); i++)
                {
                    var item = Items[i];
                    if (_owner.ItemContainerGenerator!.NeedsContainer(item, i, out var key) &&
                        Equals(key, recycleKey) && item is not null)
                    {
                        matchingItems.Add((item, i));
                        if (matchingItems.Count >= neededCount)  // CHANGED: from targetCount
                            break;
                    }
                }

                if (matchingItems.Count == 0)
                    continue;

                // Create containers for real items (but only those not yet realized)
                for (int i = 0; i < matchingItems.Count; i++)
                {
                    var (item, index) = matchingItems[i];

                    try
                    {
                        // Create container with real item - this creates the Container + Child together
                        // PrepareContainerForItemOverride is called, which creates the Child control
                        var container = CreateContainer(item, index, recycleKey);

                        // Pre-measure with typical available size to cache layout
                        container.Measure(availableSize);

                        // IMPORTANT: Do NOT clear the container!
                        // The Child control should stay attached with its template instantiated.
                        // When reused, only the data binding will update (cheap operation).

                        // Push to recycle pool - container + child are pooled together
                        if (PushToRecyclePool(recycleKey, container))
                            container.SetCurrentValue(Visual.IsVisibleProperty, false);
                        else
                            _owner.RemovePooledChild(container);
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            _isWarmupComplete = true;
        }

        private bool PushToRecyclePool(object recycleKey, Control element)
        {
            _recyclePool ??= new();

            if (!_recyclePool.TryGetValue(recycleKey, out var pool))
            {
                pool = new();
                _recyclePool.Add(recycleKey, pool);
            }

            // respect max poolsize per key of IVirtualizingDataTemplate
            if (ItemsControl?.ItemTemplate is IVirtualizingDataTemplate vdt &&
                pool.Count >= vdt.MaxPoolSizePerKey)
                return false;

            pool.Add(element);
            return true;
        }

        /// <summary>
        /// Clears unused warmup containers from the recycle pool.
        /// Only removes containers that haven't been used yet (null DataContext and invisible).
        /// </summary>
        private void ClearWarmupContainers()
        {
            if (_recyclePool == null)
                return;

            foreach (var pool in _recyclePool.Values)
            {
                for (int i = pool.Count - 1; i >= 0; i--)
                {
                    var container = pool[i];
                    if (container.DataContext == null && !container.IsVisible)
                    {
                        _owner.RemovePooledChild(container);
                        pool.RemoveAt(i);
                    }
                }
            }

        }

        /// <summary>
        /// Clears only obsolete warmup containers from the recycle pool.
        /// Preserves containers whose recycleKey is still active in the current collection.
        /// </summary>
        private void ClearObsoleteWarmupContainers()
        {
            if (_recyclePool == null)
                return;

            // Get currently needed keys from the new collection
            var activeKeys = new HashSet<object>(DiscoverTemplateKeys().Keys);

            var keysToRemove = new List<object>();

            foreach (var kvp in _recyclePool)
            {
                var recycleKey = kvp.Key;
                var pool = kvp.Value;

                // Only clear pools for obsolete keys (not in new collection)
                if (!activeKeys.Contains(recycleKey))
                {
                    for (int i = pool.Count - 1; i >= 0; i--)
                    {
                        var container = pool[i];
                        if (container.DataContext == null && !container.IsVisible)
                        {
                            _owner.RemovePooledChild(container);
                            pool.RemoveAt(i);
                        }
                    }

                    if (pool.Count == 0)
                        keysToRemove.Add(recycleKey);
                }
            }

            // Remove empty pools
            foreach (var key in keysToRemove)
                _recyclePool.Remove(key);

        }

        /// <summary>
        /// Invalidates the measure of <paramref name="visual"/> and its entire visual subtree.
        /// Needed when a recycled container is reused for a different item: the container's content
        /// is not re-created (IVirtualizingDataTemplate reuses the child), so descendants would
        /// otherwise short-circuit re-measure at the unchanged available size and keep the previous
        /// item's layout. This forces the new item's data to be measured before the arrange pass.
        /// </summary>
        private static void InvalidateMeasureRecursive(Visual visual)
        {
            if (visual is Layout.Layoutable layoutable)
                layoutable.InvalidateMeasure();

            foreach (var child in visual.GetVisualChildren())
                InvalidateMeasureRecursive(child);
        }
    }
}
