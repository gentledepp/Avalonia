using System;

namespace Avalonia.Controls
{
    /// <summary>
    /// The object that <see cref="ItemsScroll.Get"/> returns: one for each items control, also before its panel
    /// exists (a hidden list, no template yet) and after the panel is replaced. It forwards to the current target:
    /// the panel if it implements <see cref="IItemsScrollInfo"/>, else a <see cref="RealizedItemsScrollInfo"/>,
    /// else nothing ("no rows").
    /// </summary>
    internal sealed class ItemsScrollState : IItemsScrollInfo
    {
        private readonly ItemsControl _itemsControl;
        private Panel? _panel;
        private IItemsScrollInfo? _target;
        private EventHandler<ItemsScrolledEventArgs>? _scrolled;
        private EventHandler? _visibleRangeChanged;
        private (int First, int Last, int Count) _visibleRange;
        // Commands given while there is no target, or before the first layout of a fallback target.
        private Action<IItemsScrollInfo>? _pending;
        private bool _waitingForLayout;

        public ItemsScrollState(ItemsControl itemsControl)
        {
            _itemsControl = itemsControl;
            _itemsControl.ItemsPanelRootChanged += (_, _) => UpdateTarget();
            UpdateTarget();
        }

        public int FirstVisibleIndex => _target?.FirstVisibleIndex ?? -1;

        public double FirstVisibleOffset => _target?.FirstVisibleOffset ?? 0;

        public int LastVisibleIndex => _target?.LastVisibleIndex ?? -1;

        public bool CanScrollBackward => _target?.CanScrollBackward ?? false;

        public bool CanScrollForward => _target?.CanScrollForward ?? false;

        public double ScrollFraction => _target?.ScrollFraction ?? 0;

        public event EventHandler<ItemsScrolledEventArgs>? Scrolled
        {
            add
            {
                if (_scrolled is null && _target is not null)
                    _target.Scrolled += OnTargetScrolled;
                _scrolled += value;
            }
            remove
            {
                _scrolled -= value;
                if (_scrolled is null && _target is not null)
                    _target.Scrolled -= OnTargetScrolled;
            }
        }

        public event EventHandler? VisibleRangeChanged
        {
            add
            {
                if (_visibleRangeChanged is null)
                {
                    _visibleRange = VisibleRange;
                    if (_target is not null)
                        _target.VisibleRangeChanged += OnTargetVisibleRangeChanged;
                }

                _visibleRangeChanged += value;
            }
            remove
            {
                _visibleRangeChanged -= value;
                if (_visibleRangeChanged is null && _target is not null)
                    _target.VisibleRangeChanged -= OnTargetVisibleRangeChanged;
            }
        }

        public void ScrollBy(double delta)
        {
            // Without rows there is nothing to scroll; after a pending command it is added to that command.
            if (_pending is { } pending)
                _pending = t => { pending(t); t.ScrollBy(delta); };
            else
                _target?.ScrollBy(delta);
        }

        public void ScrollToItem(int index, ItemAlignment alignment = ItemAlignment.Start, double offset = 0) =>
            Command(t => t.ScrollToItem(index, alignment, offset));

        public void ScrollToStart() => Command(t => t.ScrollToStart());

        public void ScrollToEnd() => Command(t => t.ScrollToEnd());

        // A command replaces the pending ones, as in the panel's request queue.
        private void Command(Action<IItemsScrollInfo> command)
        {
            if (_target is null || _waitingForLayout)
                _pending = command;
            else
                command(_target);
        }

        private (int, int, int) VisibleRange => (FirstVisibleIndex, LastVisibleIndex, _itemsControl.ItemCount);

        private void UpdateTarget()
        {
            var panel = _itemsControl.ItemsPanelRoot;

            if (panel == _panel)
                return;

            if (_target is not null)
            {
                if (_scrolled is not null)
                    _target.Scrolled -= OnTargetScrolled;
                if (_visibleRangeChanged is not null)
                    _target.VisibleRangeChanged -= OnTargetVisibleRangeChanged;
            }

            if (_waitingForLayout)
            {
                _itemsControl.LayoutUpdated -= OnFirstLayout;
                _waitingForLayout = false;
            }

            _panel = panel;
            _target = panel is null ? null : panel as IItemsScrollInfo ?? new RealizedItemsScrollInfo(_itemsControl);

            if (_target is null)
            {
                RaiseVisibleRangeChanged();
                return;
            }

            if (_scrolled is not null)
                _target.Scrolled += OnTargetScrolled;
            if (_visibleRangeChanged is not null)
                _target.VisibleRangeChanged += OnTargetVisibleRangeChanged;

            if (_pending is null)
                return;

            if (panel is IItemsScrollInfo)
            {
                // The panel queues the command for its first layout.
                var pending = _pending;
                _pending = null;
                pending(_target);
            }
            else
            {
                // The fallback scrolls through realized containers: after the first layout.
                _waitingForLayout = true;
                _itemsControl.LayoutUpdated += OnFirstLayout;
            }
        }

        private void OnFirstLayout(object? sender, EventArgs e)
        {
            _itemsControl.LayoutUpdated -= OnFirstLayout;
            _waitingForLayout = false;

            if (_pending is { } pending && _target is not null)
            {
                _pending = null;
                pending(_target);
            }
        }

        private void OnTargetScrolled(object? sender, ItemsScrolledEventArgs e) => _scrolled?.Invoke(this, e);

        private void OnTargetVisibleRangeChanged(object? sender, EventArgs e) => RaiseVisibleRangeChanged();

        // Only when the range is different from the one reported last: a new panel gives one event.
        private void RaiseVisibleRangeChanged()
        {
            if (_visibleRangeChanged is null)
                return;

            var range = VisibleRange;
            if (range != _visibleRange)
            {
                _visibleRange = range;
                _visibleRangeChanged.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
