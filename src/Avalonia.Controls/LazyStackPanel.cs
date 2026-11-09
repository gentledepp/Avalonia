using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Utilities;
using Avalonia.VisualTree;

namespace Avalonia.Controls
{
    /// <summary>
    /// A vertical virtualizing panel that lays out from an anchor row (an index and the offset in
    /// that row) instead of a pixel offset. Rows on the screen do not move when rows above them
    /// change. <see cref="Offset"/> and <see cref="Extent"/> are estimates for the scrollbar; scroll
    /// requests arrive as commands (<see cref="IScrollCommandTarget"/>).
    /// A row with the keyboard focus stays realized when it leaves the realized range.
    /// </summary>
    public class LazyStackPanel : VirtualizingPanel, IScrollCommandTarget, IContentRecyclingPanel, IItemsScrollInfo
    {
        /// <summary>
        /// Defines the <see cref="CacheLength"/> property.
        /// </summary>
        public static readonly StyledProperty<double> CacheLengthProperty =
            AvaloniaProperty.Register<LazyStackPanel, double>(nameof(CacheLength), 0.5);

        /// <summary>
        /// Defines the <see cref="EnableWarmup"/> property.
        /// </summary>
        public static readonly StyledProperty<bool> EnableWarmupProperty =
            ContainerRecyclePool.EnableWarmupProperty.AddOwner<LazyStackPanel>();

        /// <summary>
        /// Defines the <see cref="WarmupSampleSize"/> property.
        /// </summary>
        public static readonly StyledProperty<int> WarmupSampleSizeProperty =
            ContainerRecyclePool.WarmupSampleSizeProperty.AddOwner<LazyStackPanel>();

        private static readonly object s_itemIsItsOwnContainer = new();
        private readonly ContainerRecyclePool _recyclePool;
        private Dictionary<int, Control> _realized = new();
        // The item of each realized row, so that a collection change can find the rows in the new list.
        private Dictionary<int, object?> _realizedItems = new();
        private Control? _focusedElement;
        private object? _focusedItem;
        private int _focusedIndex = -1;
        private int _firstRealizedIndex = -1;
        private double _firstRealizedTop;
        private int _lastRealizedIndex = -1;
        private double _lastRealizedBottom;
        private double _average;
        private int _anchorIndex;
        private double _anchorOffset;
        private ScrollRequest _request;
        private double _requestOffset;
        // A ScrollBy after a request that is not relative, applied after it.
        private double _pendingDelta;
        private int _requestIndex;
        private object? _requestItem;
        private bool _isInLayout;
        private Size _extent;
        // The item count of the reported Extent; a new count reports the exact Extent.
        private int _extentCount = -1;
        // The relative change of the estimate before the reported Extent follows it.
        private const double ExtentTolerance = 0.01;
        private readonly Func<int, double> _realizedHeight;
        private Size _viewport;
        private Vector _offset;
        private int _lastVisibleIndex = -1;
        private double _lastVisibleBottom;
        private EventHandler? _scrollInvalidated;
        // The source of the queued relative scroll; the distance it moved the content, not raised yet.
        private ScrollSource _requestSource;
        private double _scrolledDelta;
        private ScrollSource _scrolledSource;
        private EventHandler<ItemsScrolledEventArgs>? _scrolled;
        private EventHandler? _visibleRangeChanged;
        private (int First, int Last, int Count) _visibleRange;

        /// <summary>
        /// Initializes a new instance of the <see cref="LazyStackPanel"/> class.
        /// </summary>
        public LazyStackPanel()
        {
            _recyclePool = new ContainerRecyclePool(this);
            _realizedHeight = i => _realized[i].DesiredSize.Height;
            AddHandler(GotFocusEvent, (_, _) => OnFocusChanged(), handledEventsToo: true);
            AddHandler(LostFocusEvent, (_, _) => OnFocusChanged(), handledEventsToo: true);
        }

        /// <summary>
        /// Gets or sets the size of the buffer of realized rows above and below the viewport, as a multiple of the
        /// viewport height. The default is 0.5.
        /// </summary>
        public double CacheLength
        {
            get => GetValue(CacheLengthProperty);
            set => SetValue(CacheLengthProperty, value);
        }

        /// <summary>
        /// Gets or sets whether container warmup is enabled.
        /// When enabled, containers are pre-created during initialization to improve first-scroll performance.
        /// </summary>
        public bool EnableWarmup
        {
            get => GetValue(EnableWarmupProperty);
            set => SetValue(EnableWarmupProperty, value);
        }

        /// <summary>
        /// Gets or sets the number of items to sample for template discovery during warmup.
        /// </summary>
        public int WarmupSampleSize
        {
            get => GetValue(WarmupSampleSizeProperty);
            set => SetValue(WarmupSampleSizeProperty, value);
        }

        /// <summary>
        /// The index of the first visible row: the source of truth for the scroll position. Use it (or the
        /// realized containers) to find out which rows are on the screen, not <see cref="Offset"/>.
        /// </summary>
        public int FirstVisibleIndex => Items.Count > 0 ? _anchorIndex : -1;

        /// <summary>
        /// The pixels of the first visible row that are above the viewport top.
        /// </summary>
        /// <remarks>Rounded as the arrange rounds the row's position, so the value matches the screen.</remarks>
        public double FirstVisibleOffset =>
            (_realized.TryGetValue(_anchorIndex, out var c) ? c.UseLayoutRounding : UseLayoutRounding) ?
                -LayoutHelper.RoundLayoutValue(-_anchorOffset, LayoutHelper.GetLayoutScale(this)) :
                _anchorOffset;

        /// <inheritdoc/>
        public int LastVisibleIndex => Items.Count > 0 ? _lastVisibleIndex : -1;

        /// <inheritdoc/>
        public bool CanScrollBackward => Items.Count > 0 && (_anchorIndex > 0 || _anchorOffset > 0);

        /// <inheritdoc/>
        public bool CanScrollForward => Items.Count > 0 &&
            (_lastVisibleIndex < Items.Count - 1 || MathUtilities.GreaterThan(_lastVisibleBottom, _viewport.Height));

        /// <summary>
        /// Raised after a layout in which a relative scroll (<see cref="ScrollBy(Vector, ScrollSource)"/>, a thumb
        /// drag of one viewport or less, the smallest move of <c>BringIntoView</c>) moved the content. The delta is
        /// the distance that the content moved; the stop at row 0 and at the end is not part of it.
        /// </summary>
        public event EventHandler<ItemsScrolledEventArgs>? Scrolled
        {
            add
            {
                if (_scrolled is null)
                    _scrolledDelta = 0;
                HookLayoutUpdated(() => _scrolled += value);
            }
            remove => UnhookLayoutUpdated(() => _scrolled -= value);
        }

        /// <inheritdoc/>
        public event EventHandler? VisibleRangeChanged
        {
            add
            {
                if (_visibleRangeChanged is null)
                    _visibleRange = VisibleRange;
                HookLayoutUpdated(() => _visibleRangeChanged += value);
            }
            remove => UnhookLayoutUpdated(() => _visibleRangeChanged -= value);
        }

        private (int, int, int) VisibleRange => (FirstVisibleIndex, LastVisibleIndex, Items.Count);

        private void HookLayoutUpdated(Action add)
        {
            var hooked = _scrolled is not null || _visibleRangeChanged is not null;
            add();
            if (!hooked)
                LayoutUpdated += OnLayoutUpdated;
        }

        private void UnhookLayoutUpdated(Action remove)
        {
            remove();
            if (_scrolled is null && _visibleRangeChanged is null)
                LayoutUpdated -= OnLayoutUpdated;
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            var delta = _scrolledDelta;
            _scrolledDelta = 0;

            if (!MathUtilities.IsZero(delta))
                _scrolled?.Invoke(this, new ItemsScrolledEventArgs(delta, _scrolledSource));

            var range = VisibleRange;
            if (range != _visibleRange)
            {
                _visibleRange = range;
                _visibleRangeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// An estimate from <see cref="Offset"/> and <see cref="Extent"/>.
        /// </summary>
        public double ScrollFraction => _extent.Height > _viewport.Height ?
            Math.Clamp(_offset.Y / (_extent.Height - _viewport.Height), 0, 1) :
            0;

        /// <inheritdoc/>
        public void ScrollBy(double delta) => ScrollBy(new Vector(0, delta), ScrollSource.Programmatic);

        /// <summary>
        /// Sets the anchor to <paramref name="index"/> at the next layout; the index wins over a collection change
        /// before it.
        /// </summary>
        public void ScrollToItem(int index, ItemAlignment alignment = ItemAlignment.Start, double offset = 0)
        {
            if (index < 0 || index >= Items.Count)
                return;

            SetRequest(alignment switch
            {
                ItemAlignment.Center => ScrollRequest.ItemCenter,
                ItemAlignment.End => ScrollRequest.ItemEnd,
                _ => ScrollRequest.ItemStart,
            }, offset);
            _requestIndex = index;
        }

        /// <inheritdoc/>
        public void ScrollToStart() => ScrollToEdge(ScrollEdge.Start);

        /// <inheritdoc/>
        public void ScrollToEnd() => ScrollToEdge(ScrollEdge.End);

        /// <inheritdoc/>
        public bool CanHorizontallyScroll { get; set; }
        /// <inheritdoc/>
        public bool CanVerticallyScroll { get; set; }
        /// <inheritdoc/>
        public bool IsLogicalScrollEnabled => true;
        /// <inheritdoc/>
        public Size ScrollSize => new(16, 50);
        /// <inheritdoc/>
        public Size PageScrollSize => _viewport;
        /// <summary>
        /// An estimate for the scrollbar, from the anchor and the average row height. It changes
        /// without a scroll (new average, insert above) and is not exact at the end. See <see cref="FirstVisibleIndex"/>.
        /// </summary>
        public Size Extent => _extent;

        /// <inheritdoc/>
        public Size Viewport => _viewport;

        /// <summary>
        /// An estimate, like <see cref="Extent"/>: reported by the panel after each layout, never read back to
        /// place rows. The presenter does not write it; a write is a <see cref="ScrollToOffset"/>.
        /// </summary>
        public Vector Offset
        {
            get => _offset;
            set => ScrollToOffset(value);
        }

        event EventHandler? ILogicalScrollable.ScrollInvalidated
        {
            add => _scrollInvalidated += value;
            remove => _scrollInvalidated -= value;
        }

        /// <inheritdoc/>
        public void RaiseScrollInvalidated(EventArgs e) => _scrollInvalidated?.Invoke(this, e);

        /// <summary>
        /// Scrolls by <paramref name="delta"/> through the real rows. Returns the distance that the
        /// realized rows allow; the full delta when the edge row in that direction is not realized.
        /// </summary>
        public Vector ScrollBy(Vector delta, ScrollSource source)
        {
            var used = UsedDistance(delta.Y);
            AddDelta(used, source);
            return new Vector(0, used);
        }

        /// <inheritdoc/>
        public void ScrollToEdge(ScrollEdge edge)
        {
            SetRequest(edge == ScrollEdge.Start ? ScrollRequest.Top : ScrollRequest.End, 0);
        }

        /// <summary>
        /// The scrollbar thumb: within one viewport of the reported <see cref="Offset"/> a relative scroll, else
        /// absolute.
        /// </summary>
        public void ScrollToFraction(Vector fraction)
        {
            var f = fraction.Y;

            if (double.IsNaN(f))
                return;

            if (f <= 0 || f >= 1)
            {
                ScrollToEdge(f <= 0 ? ScrollEdge.Start : ScrollEdge.End);
                return;
            }

            var delta = f * Math.Max(0, _extent.Height - _viewport.Height) - _offset.Y;

            if (Math.Abs(delta) <= _viewport.Height)
            {
                SetRequest(ScrollRequest.Relative, delta);
                _requestSource = ScrollSource.ScrollBar;
            }
            else
                ScrollToOffset(new Vector(0, _offset.Y + delta));
        }

        /// <summary>
        /// An absolute jump to an estimated position.
        /// </summary>
        public void ScrollToOffset(Vector offset)
        {
            var y = double.IsNaN(offset.Y) ? 0 : Math.Clamp(offset.Y, 0, Math.Max(0, _extent.Height - _viewport.Height));
            SetRequest(ScrollRequest.Absolute, y);
        }

        // A command that replaces the requests before it in the same layout.
        private void SetRequest(ScrollRequest request, double offset)
        {
            _request = request;
            _requestOffset = offset;
            _requestSource = ScrollSource.Unknown;
            _pendingDelta = 0;
            InvalidateForRequest();
        }

        // A relative scroll that keeps the realized rows only moves them, in the arrange. Everything else needs
        // the full layout.
        private void InvalidateForRequest()
        {
            if (!_isInLayout && _request == ScrollRequest.Relative && _pendingDelta == 0 && IsMeasureValid &&
                CanMoveRealizedRows(_requestOffset))
                InvalidateArrange();
            else
                InvalidateMeasure();
        }

        private void AddDelta(double delta, ScrollSource source)
        {
            if (delta == 0)
                return;

            if (_request == ScrollRequest.None)
            {
                _request = ScrollRequest.Relative;
                _requestSource = source;
            }

            if (_request == ScrollRequest.Relative)
            {
                _requestOffset += delta;

                // Several sources before one layout raise one event: user input wins over code.
                if (source is not (ScrollSource.Programmatic or ScrollSource.Unknown) ||
                    _requestSource is ScrollSource.Programmatic or ScrollSource.Unknown && source == ScrollSource.Programmatic)
                    _requestSource = source;
            }
            else
                _pendingDelta += delta;

            InvalidateForRequest();
        }

        private double UsedDistance(double delta)
        {
            if (_firstRealizedIndex < 0 || _request is not (ScrollRequest.None or ScrollRequest.Relative))
                return delta;

            var pending = _request == ScrollRequest.Relative ? _requestOffset : 0;

            if (delta < 0 && _firstRealizedIndex == 0)
                return Math.Max(delta, Math.Min(0, _firstRealizedTop - pending));
            if (delta > 0 && _lastRealizedIndex == Items.Count - 1)
                return Math.Min(delta, Math.Max(0, _lastRealizedBottom - _viewport.Height - pending));
            return delta;
        }

        /// <inheritdoc/>
        public bool BringIntoView(Control target, Rect targetRect)
        {
            // The presenter passes the descendant that asked, not its container.
            var container = target;
            while (container is not null && container.GetVisualParent() != this)
                container = container.GetVisualParent() as Control;

            if (container is null)
                return false;

            // The smallest move starts from the position after the queued request.
            var index = IndexFromContainer(container);

            if (index >= 0 && ApplyQueuedRequest() && ContainerFromIndex(index) != container)
            {
                // The queued request recycled the row: bring its index into view.
                RequestIndex(index < _anchorIndex ? ScrollRequest.IndexTop : ScrollRequest.IndexBottom, index);
                return true;
            }

            if (container == _focusedElement || index >= 0 && IsRequestQueued)
            {
                // The focused row outside the realized range; or a layout that could not run (the positions
                // are old), as the index of the row.
                RequestIndex(index < _anchorIndex ? ScrollRequest.IndexTop : ScrollRequest.IndexBottom, index);
                return true;
            }

            if (!_realized.ContainsValue(container) || target.TransformToVisual(this) is not { } transform)
                return false;

            // Nothing when it is fully visible, or larger than the viewport and covers it; else the smaller move
            // that puts one of its edges on the viewport edge.
            var rect = targetRect.TransformToAABB(transform);
            var height = _viewport.Height;
            var delta = rect.Top >= 0 && rect.Bottom <= height || rect.Top < 0 && rect.Bottom > height ? 0 :
                Math.Abs(rect.Top) < Math.Abs(rect.Bottom - height) ? rect.Top :
                rect.Bottom - height;

            if (delta == 0)
                return false;

            AddDelta(delta, ScrollSource.Programmatic);
            return true;
        }

        /// <inheritdoc/>
        public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

        protected override Size MeasureOverride(Size availableSize)
        {
            // The panel always scrolls: it cannot take the height of all its rows.
            if (!(this.GetVisualParent() is Presenters.ItemsPresenter itemsPresenter &&
                  itemsPresenter.GetVisualParent() is Presenters.ScrollContentPresenter scrollPresenter))
                throw new InvalidOperationException(
                    "LazyStackPanel must be the items panel of an ItemsControl whose ItemsPresenter is the direct " +
                    "content of a ScrollViewer.");

            // Before the ScrollContentPresenter scrolls the panel (its first layout) it measures with an infinite
            // height; after that, an infinite height means a ScrollViewer without a limit.
            if (double.IsInfinity(availableSize.Height) && scrollPresenter.ScrollCommandTarget == this)
                throw new InvalidOperationException(
                    "LazyStackPanel was measured with an infinite height: its ScrollViewer must have a limited height " +
                    "(not in a StackPanel, an Auto row or other vertically scrolling content).");

            _isInLayout = true;
            try
            {
                return MeasureRows(availableSize);
            }
            finally
            {
                _isInLayout = false;
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _isInLayout = true;
            try
            {
                // A relative scroll that the measure did not take: the fast path (InvalidateForRequest).
                if (_request == ScrollRequest.Relative && _pendingDelta == 0 && !MoveRealizedRows())
                    InvalidateMeasure();

                return ArrangeRows(finalSize);
            }
            finally
            {
                _isInLayout = false;
            }
        }

        /// <summary>
        /// True when the full layout after a relative scroll by <paramref name="delta"/> would keep the realized
        /// rows as they are: no row leaves the buffer (<see cref="RecycleLeavingContainers"/>), no row is added
        /// (the buffer loops of <see cref="MeasureRows"/>), no stop at row 0 and no end gap, and no row needs a
        /// measure. Then only the anchor and the row positions change.
        /// </summary>
        private bool CanMoveRealizedRows(double delta)
        {
            var count = Items.Count;
            var first = _firstRealizedIndex;
            var last = _lastRealizedIndex;

            if (first < 0 || last >= count || _realized.Count != last - first + 1)
                return false;

            foreach (var c in _realized.Values)
            {
                if (!c.IsMeasureValid)
                    return false;
            }

            if (_focusedElement is { IsMeasureValid: false })
                return false;

            var viewportHeight = _viewport.Height;
            var cache = viewportHeight * CacheLength;
            var top = _firstRealizedTop - delta;
            var bottom = _lastRealizedBottom - delta;

            // No row leaves.
            if (top + _realized[first].DesiredSize.Height < -cache ||
                bottom - _realized[last].DesiredSize.Height > viewportHeight + cache)
                return false;

            // No row is added.
            if (bottom < viewportHeight + cache && last < count - 1 || top > -cache && first > 0)
                return false;

            // No stop at row 0, no end gap.
            return top <= 0 && bottom >= viewportHeight;
        }

        /// <summary>
        /// The fast path of a relative scroll: moves the anchor through the realized rows, with the same result as
        /// the full layout (<see cref="CanMoveRealizedRows"/>). Returns false, and changes nothing, when the
        /// full layout is necessary.
        /// </summary>
        private bool MoveRealizedRows()
        {
            var delta = _requestOffset;

            if (!CanMoveRealizedRows(delta))
                return false;

            var count = Items.Count;
            var viewportHeight = _viewport.Height;
            var source = _requestSource;
            var oldIndex = _anchorIndex;
            var oldOffset = _anchorOffset;

            ApplyRequest(count, viewportHeight, _realizedHeight);
            NormalizeAnchor(count, _realizedHeight);
            var bottom = LayoutDown(count, viewportHeight, _realizedHeight, out var lastVisible);

            _scrolledDelta += Distance(oldIndex, oldOffset, _realizedHeight);
            _scrolledSource = source;
            _lastVisibleIndex = lastVisible;
            _lastVisibleBottom = bottom;

            // The same sums as MeasureRows.
            for (var i = lastVisible + 1; i <= _lastRealizedIndex; ++i)
                bottom += _realized[i].DesiredSize.Height;

            _lastRealizedBottom = bottom;
            _firstRealizedTop = -_anchorOffset;

            for (var i = _firstRealizedIndex; i < _anchorIndex; ++i)
                _firstRealizedTop -= _realized[i].DesiredSize.Height;

            UpdateScrollValues(count, _lastRealizedIndex, bottom, _extent.Width, _viewport, hadRequest: true,
                isRelative: true);
            return true;
        }

        /// <summary>
        /// Offset and extent from the anchor, the realized rows and the average row height. The reported Extent
        /// follows the estimate only when it changes by more than <see cref="ExtentTolerance"/>, so the scrollbar
        /// does not change for each row that enters or leaves the realized range. It is exact at the end, after a
        /// jump, after a count or viewport change, and when Offset would be beyond Extent - Viewport.
        /// </summary>
        private void UpdateScrollValues(int count, int lastIndex, double bottom, double extentWidth, Size viewport,
            bool hadRequest, bool isRelative)
        {
            var avg = _average;
            var offset = avg * _anchorIndex + _anchorOffset;
            var extent = offset + bottom + avg * Math.Max(0, count - 1 - lastIndex);
            var reported = _extent.Height;
            var exact = lastIndex >= count - 1 ||
                hadRequest && !isRelative ||
                count != _extentCount ||
                viewport != _viewport ||
                offset + viewport.Height > reported ||
                Math.Abs(extent - reported) > reported * ExtentTolerance;

            var newExtent = new Size(extentWidth, exact ? extent : reported);
            var newOffset = new Vector(0, offset);
            _extentCount = count;

            if (hadRequest || newExtent != _extent || viewport != _viewport || newOffset != _offset)
            {
                _extent = newExtent;
                _viewport = viewport;
                _offset = newOffset;
                RaiseScrollInvalidated(EventArgs.Empty);
            }
        }

        private Size MeasureRows(Size availableSize)
        {
            var items = Items;
            var count = items.Count;
            var width = availableSize.Width;
            var viewportHeight = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
            var old = _realized;
            var realized = new Dictionary<int, Control>();
            // Heights of rows that this measure recycled: the anchor walk reads them and does not realize the rows
            // again.
            var known = new Dictionary<int, double>();
            var cache = viewportHeight * CacheLength;

            RecycleLeavingContainers(old, viewportHeight, known);

            if (_focusedElement is not null && !IsFocusedElement(_focusedElement))
                RecycleFocusedElement();

            double RowHeight(int index)
            {
                if (!realized.TryGetValue(index, out var c))
                {
                    if (!old.Remove(index, out c))
                        c = GetContainer(items[index], index);
                    c.Measure(new Size(width, double.PositiveInfinity));
                    realized[index] = c;
                }

                return c.DesiredSize.Height;
            }

            // The anchor walk: a recycled row gives its known height; any other row is realized.
            double WalkHeight(int index) =>
                !realized.ContainsKey(index) && !old.ContainsKey(index) && known.TryGetValue(index, out var h) ?
                    h :
                    RowHeight(index);

            // A row that the walk passed and that is outside the new range (the buffer loops below) is recycled
            // at once, so the next row on the path can reuse its container. Its position is final:
            // the viewport top does not move during the walk.
            void Passed(int index, double top, double height)
            {
                if (top + height > -cache && top < viewportHeight + cache)
                    return;

                known[index] = height;

                if (realized.Remove(index, out var c) && !TryKeepFocused(c, index))
                    RecycleContainer(c);
            }

            // A relative scroll raises Scrolled with the distance the content really moved (after the stops).
            var isRelative = _request == ScrollRequest.Relative;
            var source = _requestSource;
            var oldIndex = Math.Min(_anchorIndex, count - 1);
            var oldOffset = _anchorOffset;
            // A request is reported also when the offset does not change: an Offset write out of range must
            // come back as the clamped value.
            var hadRequest = IsRequestQueued;

            if (count == 0)
            {
                _anchorIndex = 0;
                _anchorOffset = 0;
            }
            else
            {
                ApplyRequest(count, viewportHeight, RowHeight);
                _anchorIndex = Math.Clamp(_anchorIndex, 0, count - 1);
                NormalizeAnchor(count, WalkHeight, Passed);
            }

            var bottom = 0.0;
            var lastIndex = -1;
            var firstIndex = 0;

            if (count > 0)
            {
                // Down from the anchor to the viewport bottom; then close an end gap.
                bottom = LayoutDown(count, viewportHeight, RowHeight, out lastIndex);

                if (bottom < viewportHeight && (_anchorIndex > 0 || _anchorOffset > 0))
                {
                    _anchorOffset -= viewportHeight - bottom;
                    NormalizeAnchor(count, RowHeight);
                    bottom = LayoutDown(count, viewportHeight, RowHeight, out lastIndex);
                }

                if (isRelative)
                {
                    _scrolledDelta += Distance(oldIndex, oldOffset, WalkHeight);
                    _scrolledSource = source;
                }

                _lastVisibleIndex = lastIndex;
                _lastVisibleBottom = bottom;

                while (bottom < viewportHeight + cache && lastIndex < count - 1)
                    bottom += RowHeight(++lastIndex);

                var top = -_anchorOffset;
                firstIndex = _anchorIndex;

                while (top > -cache && firstIndex > 0)
                    top -= RowHeight(--firstIndex);
            }

            // Rows measured while moving the anchor that ended outside the realized range.
            foreach (var index in realized.Keys.Where(x => x < firstIndex || x > lastIndex).ToList())
            {
                old.Add(index, realized[index]);
                realized.Remove(index);
            }

            foreach (var (index, c) in old)
            {
                if (!TryKeepFocused(c, index))
                    RecycleContainer(c);
            }

            _realized = realized;
            _realizedItems.Clear();

            foreach (var index in realized.Keys)
                _realizedItems[index] = items[index];

            // The focused row stays realized outside the range; it is not part of the estimate.
            _focusedElement?.Measure(new Size(width, double.PositiveInfinity));
            _firstRealizedIndex = count > 0 ? firstIndex : -1;
            _lastRealizedIndex = lastIndex;
            _lastRealizedBottom = bottom;
            _firstRealizedTop = -_anchorOffset;

            for (var i = firstIndex; i < _anchorIndex; ++i)
                _firstRealizedTop -= realized[i].DesiredSize.Height;

            _average = realized.Count > 0 ? realized.Values.Average(x => x.DesiredSize.Height) : 0;
            var maxWidth = realized.Count > 0 ? realized.Values.Max(x => x.DesiredSize.Width) : 0;
            UpdateScrollValues(count, lastIndex, bottom, Math.Max(width, maxWidth), new Size(width, viewportHeight),
                hadRequest, isRelative);

            // As the VSP: rows shorter than the viewport give their own height.
            return new Size(maxWidth, count > 0 ? Math.Min(viewportHeight, _lastVisibleBottom) : 0);
        }

        private Size ArrangeRows(Size finalSize)
        {
            if (_firstRealizedIndex < 0)
                return finalSize;

            var y = -_anchorOffset;

            for (var i = _anchorIndex - 1; i >= _firstRealizedIndex; --i)
                y -= _realized[i].DesiredSize.Height;

            var top = y;
            var lastIndex = _firstRealizedIndex - 1;

            for (var i = _firstRealizedIndex; _realized.TryGetValue(i, out var c); ++i)
            {
                c.Arrange(new Rect(0, y, finalSize.Width, c.DesiredSize.Height));
                y += c.DesiredSize.Height;
                lastIndex = i;
            }

            // Outside the realized range, so never on the screen.
            if (_focusedElement is { } f)
            {
                var h = f.DesiredSize.Height;
                var fy = _focusedIndex < _firstRealizedIndex ?
                    top - (_firstRealizedIndex - _focusedIndex - 1) * _average - h :
                    y + (_focusedIndex - lastIndex - 1) * _average;
                f.Arrange(new Rect(0, fy, finalSize.Width, h));
            }

            return finalSize;
        }

        protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(items, e);

            // The anchor first: a Reset that removed the anchor item uses the old rows' screen positions.
            _recyclePool.OnItemsChanged(items, e);
            UpdateAnchor(items, e);
            UpdateRealized(items, e);
            RemoveOwnContainers(items, e);
            UpdateFocusedIndex(items, e);
            UpdateRequestIndex(items, e);

            // The realized rows can have gaps until the next layout.
            _firstRealizedIndex = -1;

            if (items.Count == 0)
                _recyclePool.Clear();
            else if (e.Action == NotifyCollectionChangedAction.Reset)
                _recyclePool.OnItemsReset(items);

            InvalidateMeasure();
        }

        // Finds the anchor row in the new list (an insert or a remove above it, a move, a Reset).
        private void UpdateAnchor(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
        {
            var count = items.Count;

            if (count == 0)
            {
                _anchorIndex = 0;
                _anchorOffset = 0;
                _lastVisibleIndex = -1;
                return;
            }

            var hasItem = _realizedItems.TryGetValue(_anchorIndex, out var anchorItem);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                var index = hasItem ? IndexNear(items, anchorItem, _anchorIndex) : -1;

                if (index >= 0)
                    _anchorIndex = index;
                else if (FindOldRow(items, out var rowIndex, out var rowTop))
                {
                    // The item is gone: the first old row that still exists, at its old screen position.
                    _anchorIndex = rowIndex;
                    _anchorOffset = -rowTop;
                }

                // Else keep the old index and offset; never jump to row 0 because the item is lost.
            }
            else if (e.Action == NotifyCollectionChangedAction.Add && _anchorIndex >= count - e.NewItems!.Count)
            {
                // The list was empty: there is no row to keep.
            }
            else
            {
                var index = NewIndex(_anchorIndex, anchorItem, items, e);

                // A removed anchor row: the next row takes its index, its top where the removed row's top was.
                if (index >= 0)
                    _anchorIndex = index;
                else if (e.Action == NotifyCollectionChangedAction.Remove)
                    _anchorIndex = e.OldStartingIndex;
            }

            if (_anchorIndex >= count)
            {
                _anchorIndex = count - 1;
                _anchorOffset = 0;
            }

            _lastVisibleIndex = Math.Min(_lastVisibleIndex, count - 1);
        }

        // The first old realized row whose item is in the new list (the rows on the screen first) and the top of
        // its slot. The slot includes the container's margin; Bounds does not.
        private bool FindOldRow(IReadOnlyList<object?> items, out int index, out double top)
        {
            var height = _viewport.Height;
            var rows = _realized
                .Select(x => (Index: x.Key, Top: x.Value.Bounds.Top - x.Value.Margin.Top,
                    Bottom: x.Value.Bounds.Bottom + x.Value.Margin.Bottom))
                .OrderBy(x => x.Bottom > 0 && x.Top < height ? 0 : 1)
                .ThenBy(x => Math.Abs(x.Top));

            foreach (var row in rows)
            {
                if (_realizedItems.TryGetValue(row.Index, out var item) &&
                    IndexNear(items, item, row.Index) is var i and >= 0 &&
                    ReferenceEquals(items[i], item))
                {
                    index = i;
                    top = row.Top;
                    return true;
                }
            }

            index = -1;
            top = 0;
            return false;
        }

        // Gives each realized row its new index at once, so that ScrollIntoView, ContainerFromIndex and the
        // selection see the right rows before the next layout. A row whose item is gone goes to the pool.
        private void UpdateRealized(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
        {
            if (_realized.Count == 0)
                return;

            var realized = new Dictionary<int, Control>(_realized.Count);
            var realizedItems = new Dictionary<int, object?>(_realized.Count);

            foreach (var (index, c) in _realized)
            {
                var item = _realizedItems.GetValueOrDefault(index);
                var newIndex = items.Count == 0 ? -1 :
                    e.Action == NotifyCollectionChangedAction.Reset ? IndexNear(items, item, index) :
                    NewIndex(index, item, items, e);

                // An equal but different instance (a Reset with new objects) needs its own prepare.
                if (newIndex < 0 || realized.ContainsKey(newIndex) || !ReferenceEquals(items[newIndex], item))
                {
                    RecycleContainer(c);
                    continue;
                }

                if (newIndex != index)
                    ItemContainerGenerator?.ItemContainerIndexChanged(c, index, newIndex);

                realized[newIndex] = c;
                realizedItems[newIndex] = item;
            }

            _realized = realized;
            _realizedItems = realizedItems;
        }

        protected override void OnItemsControlChanged(ItemsControl? oldValue)
        {
            base.OnItemsControlChanged(oldValue);

            if (oldValue is not null)
                oldValue.PropertyChanged -= OnItemsControlPropertyChanged;
            if (ItemsControl is not null)
                ItemsControl.PropertyChanged += OnItemsControlPropertyChanged;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _recyclePool.OnAttachedToVisualTree();
        }

        protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
        {
            var count = Items.Count;
            var fromControl = from as Control;

            if (count == 0 ||
                (fromControl is null && direction is not NavigationDirection.First and not NavigationDirection.Last))
                return null;

            var fromIndex = fromControl is not null ? IndexFromContainer(fromControl) : -1;
            int toIndex;

            switch (direction)
            {
                case NavigationDirection.First:
                    toIndex = 0;
                    break;
                case NavigationDirection.Last:
                    toIndex = count - 1;
                    break;
                case NavigationDirection.Next:
                case NavigationDirection.Down:
                    toIndex = fromIndex + 1;
                    break;
                case NavigationDirection.Previous:
                case NavigationDirection.Up:
                    toIndex = fromIndex - 1;
                    break;
                case NavigationDirection.Left:
                case NavigationDirection.Right:
                    toIndex = fromIndex;
                    break;
                default:
                    return null;
            }

            if (fromIndex == toIndex)
                return from;

            if (wrap)
            {
                if (toIndex < 0)
                    toIndex = count - 1;
                else if (toIndex >= count)
                    toIndex = 0;
            }

            return ScrollIntoView(toIndex);
        }

        // In index order, as the VSP.
        protected internal override IEnumerable<Control>? GetRealizedContainers() =>
            _realized.OrderBy(x => x.Key).Select(x => x.Value);

        protected internal override Control? ContainerFromIndex(int index)
        {
            if (index == _focusedIndex)
                return _focusedElement;
            return _realized.GetValueOrDefault(index);
        }

        protected internal override int IndexFromContainer(Control container)
        {
            if (container == _focusedElement)
                return _focusedIndex;

            foreach (var (index, c) in _realized)
            {
                if (c == container)
                    return index;
            }

            return -1;
        }

        protected internal override Control? ScrollIntoView(int index)
        {
            // As the VirtualizingStackPanel: not from inside the panel's own layout.
            if (_isInLayout || index < 0 || index >= Items.Count)
                return null;

            ApplyQueuedRequest();

            if (_realized.TryGetValue(index, out var container))
            {
                // It can be in the buffer, outside the viewport.
                if (BringIntoView(container, new Rect(container.Bounds.Size)))
                    UpdateLayout();
            }
            else
            {
                // A row above becomes the anchor; a row below is aligned with the bottom.
                RequestIndex(index < _anchorIndex ? ScrollRequest.IndexTop : ScrollRequest.IndexBottom, index);
                UpdateLayout();
            }

            return ContainerFromIndex(index);
        }

        private void RequestIndex(ScrollRequest request, int index)
        {
            // Replaces the requests before it, as SetRequest.
            _request = request;
            _requestIndex = index;
            _requestItem = Items[index];
            _requestOffset = 0;
            _requestSource = ScrollSource.Unknown;
            _pendingDelta = 0;
            InvalidateMeasure();
        }

        private bool IsRequestQueued => _request != ScrollRequest.None || _pendingDelta != 0;

        /// <summary>
        /// Runs the layout of a queued scroll request, so that a smallest move (BringIntoView, ScrollIntoView)
        /// starts from the real positions. Returns true when a layout ran. Does nothing inside a layout pass.
        /// </summary>
        private bool ApplyQueuedRequest()
        {
            if (_isInLayout || !IsRequestQueued)
                return false;

            UpdateLayout();
            return !IsRequestQueued;
        }

        private void ApplyRequest(int count, double viewportHeight, Func<int, double> rowHeight)
        {
            switch (_request)
            {
                case ScrollRequest.ItemStart:
                    _anchorIndex = Math.Clamp(_requestIndex, 0, count - 1);
                    _anchorOffset = _requestOffset;
                    break;
                case ScrollRequest.ItemCenter:
                    _anchorIndex = Math.Clamp(_requestIndex, 0, count - 1);
                    _anchorOffset = (rowHeight(_anchorIndex) - viewportHeight) / 2 + _requestOffset;
                    break;
                case ScrollRequest.ItemEnd:
                    _anchorIndex = Math.Clamp(_requestIndex, 0, count - 1);
                    _anchorOffset = rowHeight(_anchorIndex) - viewportHeight + _requestOffset;
                    break;
                case ScrollRequest.IndexTop:
                    _anchorIndex = Math.Clamp(_requestIndex, 0, count - 1);
                    _anchorOffset = 0;
                    break;
                case ScrollRequest.IndexBottom:
                    // A row taller than the viewport is aligned with the top.
                    _anchorIndex = Math.Clamp(_requestIndex, 0, count - 1);
                    _anchorOffset = Math.Min(0, rowHeight(_anchorIndex) - viewportHeight);
                    break;
                case ScrollRequest.Relative:
                    _anchorOffset += _requestOffset;
                    break;
                case ScrollRequest.Top:
                    _anchorIndex = 0;
                    _anchorOffset = 0;
                    break;
                case ScrollRequest.End:
                    // The last row's bottom on the viewport bottom; NormalizeAnchor walks up through real rows.
                    _anchorIndex = count - 1;
                    _anchorOffset = rowHeight(_anchorIndex) - viewportHeight;
                    break;
                case ScrollRequest.Absolute:
                    var avg = _average;
                    var index = avg > 0 ? (int)Math.Floor(_requestOffset / avg) : 0;
                    _anchorIndex = Math.Clamp(index, 0, count - 1);
                    _anchorOffset = _requestOffset - _anchorIndex * avg;
                    break;
            }

            _anchorOffset += _pendingDelta;
            _request = ScrollRequest.None;
            _requestOffset = 0;
            _pendingDelta = 0;
        }

        // How far the content moved: from the old anchor position to the new one, through the real rows.
        private double Distance(int oldIndex, double oldOffset, Func<int, double> rowHeight)
        {
            var distance = _anchorOffset - oldOffset;

            for (var i = oldIndex; i < _anchorIndex; ++i)
                distance += rowHeight(i);
            for (var i = _anchorIndex; i < oldIndex; ++i)
                distance -= rowHeight(i);

            return distance;
        }

        /// <param name="count">The number of items.</param>
        /// <param name="rowHeight">Returns the height of the row at an index.</param>
        /// <param name="passed">Called for each row that the anchor moves past, with its top in the viewport and its
        /// height. The position is final: only the anchor changes, the viewport top stays.</param>
        private void NormalizeAnchor(int count, Func<int, double> rowHeight, Action<int, double, double>? passed = null)
        {
            while (_anchorOffset < 0 && _anchorIndex > 0)
            {
                var below = _anchorIndex;
                var h = rowHeight(--_anchorIndex);
                _anchorOffset += h;
                passed?.Invoke(below, h - _anchorOffset, rowHeight(below));
            }

            if (_anchorOffset < 0)
                _anchorOffset = 0;

            while (_anchorIndex < count - 1)
            {
                var h = rowHeight(_anchorIndex);
                if (_anchorOffset < h)
                    break;
                _anchorOffset -= h;
                passed?.Invoke(_anchorIndex, -_anchorOffset - h, h);
                ++_anchorIndex;
            }
        }

        private double LayoutDown(int count, double viewportHeight, Func<int, double> rowHeight, out int lastIndex)
        {
            var y = -_anchorOffset;
            var i = _anchorIndex;

            while (i < count && (y < viewportHeight || i == _anchorIndex))
                y += rowHeight(i++);

            lastIndex = i - 1;
            return y;
        }

        private Control GetContainer(object? item, int index)
        {
            var generator = ItemContainerGenerator!;

            if (index == _focusedIndex && _focusedElement is { } focused)
            {
                _focusedElement = null;
                _focusedIndex = -1;
                _focusedItem = null;
                return focused;
            }

            if (!generator.NeedsContainer(item, index, out var recycleKey))
                return GetItemAsOwnContainer((Control)item!, index);

            return _recyclePool.GetRecycledContainer(item, index, recycleKey) ??
                _recyclePool.CreateContainer(item, index, recycleKey);
        }

        // As the VSP: a Control item stays a hidden child while it is scrolled out (no Unloaded/Loaded).
        private Control GetItemAsOwnContainer(Control item, int index)
        {
            var generator = ItemContainerGenerator!;

            if (item.GetVisualParent() != this)
            {
                generator.PrepareItemContainer(item, item, index);
                AddInternalChild(item);
                item.SetValue(ContainerRecyclePool.RecycleKeyProperty, s_itemIsItsOwnContainer);
                generator.ItemContainerPrepared(item, item, index);
            }

            item.SetCurrentValue(IsVisibleProperty, true);
            return item;
        }

        private void RecycleContainer(Control container)
        {
            if (ItemContainerGenerator is null)
                RemoveInternalChild(container);
            else if (container.GetValue(ContainerRecyclePool.RecycleKeyProperty) == s_itemIsItsOwnContainer)
                container.SetCurrentValue(IsVisibleProperty, false);
            else
                _recyclePool.RecycleContainer(container);
        }

        // A Control item that left the collection leaves the panel too.
        private void RemoveOwnContainers(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
        {
            List<Control>? remove = null;

            if (e.Action == NotifyCollectionChangedAction.Reset || items.Count == 0)
            {
                HashSet<object>? present = null;

                foreach (var child in Children)
                {
                    if (child.GetValue(ContainerRecyclePool.RecycleKeyProperty) != s_itemIsItsOwnContainer)
                        continue;

                    present ??= new HashSet<object>(items.OfType<Control>(), ReferenceEqualityComparer.Instance);

                    if (!present.Contains(child))
                        (remove ??= new()).Add(child);
                }
            }
            else if (e.OldItems is not null)
            {
                foreach (var old in e.OldItems)
                {
                    if (old is Control c && c.GetVisualParent() == this &&
                        c.GetValue(ContainerRecyclePool.RecycleKeyProperty) == s_itemIsItsOwnContainer &&
                        !items.Contains(c))
                        (remove ??= new()).Add(c);
                }
            }

            if (remove is null)
                return;

            foreach (var c in remove)
            {
                RemoveInternalChild(c);
                c.ClearValue(ContainerRecyclePool.RecycleKeyProperty);
            }
        }

        // Recycles the rows that a scroll request moves out of the realized range before new
        // rows are realized, so that the new rows can reuse them.
        private void RecycleLeavingContainers(Dictionary<int, Control> realized, double viewportHeight,
            Dictionary<int, double> known)
        {
            void Recycle(int i)
            {
                var c = realized[i];

                if (c.IsMeasureValid)
                    known[i] = c.DesiredSize.Height;
                if (!TryKeepFocused(c, i))
                    RecycleContainer(c);
                realized.Remove(i);
            }

            if (realized.Count == 0)
                return;

            if (_request is not (ScrollRequest.None or ScrollRequest.Relative))
            {
                // A jump (as the VirtualizingStackPanel): estimate the new range around the target index and recycle
                // the old rows outside it. The rest stay in the index map, so a row in both ranges keeps its container.
                var avg = _average;
                if (avg <= 0)
                    return;

                var target = _request switch
                {
                    ScrollRequest.Top => 0,
                    ScrollRequest.End => Items.Count - 1,
                    ScrollRequest.Absolute => (int)Math.Min(Math.Floor(_requestOffset / avg), int.MaxValue),
                    _ => _requestIndex,
                };
                var rows = (int)Math.Min(Math.Ceiling(viewportHeight * (1 + 2 * CacheLength) / avg) + 1, int.MaxValue / 2);
                var cacheRows = (int)Math.Min(Math.Ceiling(viewportHeight * CacheLength / avg) + 1, int.MaxValue / 2);

                // The target row at the top: the new range is below it; at the bottom: above it. A request
                // with its own offset or a center alignment keeps the window on both sides.
                var (from, to) = _request switch
                {
                    ScrollRequest.Top or ScrollRequest.Absolute or ScrollRequest.IndexTop => (target - cacheRows, target + rows),
                    ScrollRequest.End or ScrollRequest.IndexBottom => (target - rows, target + cacheRows),
                    _ => (target - rows, target + rows),
                };
                var leaving = realized.Keys.Where(i => i < from || i > to).ToList();

                foreach (var i in leaving)
                    Recycle(i);
            }
            else if (_request == ScrollRequest.Relative)
            {
                var cache = viewportHeight * CacheLength;
                var top = _firstRealizedTop - _requestOffset;
                var leaving = new List<int>();

                for (var i = _firstRealizedIndex; realized.TryGetValue(i, out var c); ++i)
                {
                    var bottom = top + c.DesiredSize.Height;
                    if (bottom < -cache || top > viewportHeight + cache)
                        leaving.Add(i);
                    top = bottom;
                }

                foreach (var i in leaving)
                    Recycle(i);
            }
        }

        private void OnItemsControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ItemsControl.ItemTemplateProperty)
                _recyclePool.OnItemTemplateChanged();
            else if (e.Property == ItemsControl.ItemsSourceProperty)
            {
                // Another list: start at row 0, offset 0. The Reset of the new list came first and kept the
                // old position, as for a Reset of the same list.
                _anchorIndex = 0;
                _anchorOffset = 0;
                InvalidateMeasure();
            }
            else if (e.Property == KeyboardNavigation.TabOnceActiveElementProperty)
                OnFocusChanged();
        }

        // The focus is inside the row, or the row is where Tab comes back to (as the VSP).
        private bool IsFocusedElement(Control container) =>
            container.IsKeyboardFocusWithin ||
            (ItemsControl is { } ic && KeyboardNavigation.GetTabOnceActiveElement(ic) == container);

        private void OnFocusChanged()
        {
            if (_focusedElement is not null && !IsFocusedElement(_focusedElement))
                InvalidateMeasure();
        }

        // Keeps a container that leaves the realized range when it has the focus.
        private bool TryKeepFocused(Control container, int index)
        {
            if (container == _focusedElement)
                return true;
            if (!IsFocusedElement(container))
                return false;

            RecycleFocusedElement();
            _focusedElement = container;
            _focusedIndex = index;
            // Called in the measure, before any change: the index is valid in Items.
            _focusedItem = Items[index];
            return true;
        }

        private void RecycleFocusedElement()
        {
            if (_focusedElement is { } f)
            {
                _focusedElement = null;
                _focusedIndex = -1;
                _focusedItem = null;
                RecycleContainer(f);
            }
        }

        private void UpdateFocusedIndex(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
        {
            if (_focusedElement is not { } focused)
                return;

            var index = NewIndex(_focusedIndex, _focusedItem, items, e);

            if (index < 0)
            {
                // The item is gone. The pool hides its container, which clears the focus.
                RecycleFocusedElement();
            }
            else if (index != _focusedIndex)
            {
                ItemContainerGenerator?.ItemContainerIndexChanged(focused, _focusedIndex, index);
                _focusedIndex = index;
            }
        }

        private void UpdateRequestIndex(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
        {
            // A ScrollIntoView that has not run yet (not visible, not attached) follows its item.
            if (_request is not (ScrollRequest.IndexTop or ScrollRequest.IndexBottom))
                return;

            _requestIndex = NewIndex(_requestIndex, _requestItem, items, e);

            if (_requestIndex < 0)
                _request = ScrollRequest.None;
        }

        /// <summary>
        /// The index of <paramref name="item"/> after a collection change, or -1 if it was removed.
        /// </summary>
        private static int NewIndex(int index, object? item, IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
        {
            var isOld = e.OldItems is not null && e.OldStartingIndex <= index && index < e.OldStartingIndex + e.OldItems.Count;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    return e.NewStartingIndex <= index ? index + e.NewItems!.Count : index;
                case NotifyCollectionChangedAction.Remove:
                    return isOld ? -1 : e.OldStartingIndex < index ? index - e.OldItems!.Count : index;
                case NotifyCollectionChangedAction.Replace:
                    return isOld ? -1 : index;
                case NotifyCollectionChangedAction.Move when isOld:
                    return e.NewStartingIndex + index - e.OldStartingIndex;
                case NotifyCollectionChangedAction.Move:
                    if (e.OldStartingIndex < index)
                        index -= e.OldItems!.Count;
                    return e.NewStartingIndex <= index ? index + e.NewItems!.Count : index;
                default:
                    // Reset: found again by item.
                    return IndexNear(items, item, index);
            }
        }

        /// <summary>
        /// The index of <paramref name="item"/>, searched from <paramref name="index"/> outwards, so that a
        /// repeated item finds its nearest copy.
        /// </summary>
        private static int IndexNear(IReadOnlyList<object?> items, object? item, int index)
        {
            var count = items.Count;

            for (var d = 0; index - d >= 0 || index + d < count; ++d)
            {
                if (index + d < count && index + d >= 0 && Equals(items[index + d], item))
                    return index + d;
                if (d > 0 && index - d >= 0 && index - d < count && Equals(items[index - d], item))
                    return index - d;
            }

            return -1;
        }

        private enum ScrollRequest
        {
            None,
            Relative,
            Absolute,
            Top,
            End,
            IndexTop,
            IndexBottom,
            ItemStart,
            ItemCenter,
            ItemEnd,
        }
    }
}
