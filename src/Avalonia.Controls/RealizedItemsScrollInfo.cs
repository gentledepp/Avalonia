using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Utilities;
using Avalonia.VisualTree;

namespace Avalonia.Controls
{
    /// <summary>
    /// <see cref="IItemsScrollInfo"/> for any panel, from the realized containers and their position in the
    /// viewport. The visible rows are always realized, so the visible indexes are exact.
    /// </summary>
    internal sealed class RealizedItemsScrollInfo : IItemsScrollInfo
    {
        private readonly ItemsControl _itemsControl;
        private readonly ScrolledTracker _tracker;
        private EventHandler? _visibleRangeChanged;
        private (int First, int Last, int Count) _visibleRange;

        public RealizedItemsScrollInfo(ItemsControl itemsControl)
        {
            _itemsControl = itemsControl;
            _tracker = new ScrolledTracker(
                itemsControl,
                this,
                () => VisibleRows().Select(x => (x.Item, x.Top)).ToList(),
                () => ScrollViewer?.Offset.Y ?? 0);
        }

        public int FirstVisibleIndex => VisibleRows() is { Count: > 0 } rows ? rows[0].Index : -1;

        public double FirstVisibleOffset => VisibleRows() is { Count: > 0 } rows ? Math.Max(0, -rows[0].Top) : 0;

        public int LastVisibleIndex => VisibleRows() is { Count: > 0 } rows ? rows[^1].Index : -1;

        public bool CanScrollBackward => VisibleRows() is { Count: > 0 } rows && (rows[0].Index > 0 || MathUtilities.LessThan(rows[0].Top, 0));

        public bool CanScrollForward => VisibleRows() is { Count: > 0 } rows &&
            (rows[^1].Index < _itemsControl.ItemCount - 1 || MathUtilities.GreaterThan(rows[^1].Bottom, ViewportHeight));

        public event EventHandler<ItemsScrolledEventArgs>? Scrolled
        {
            add => _tracker.Scrolled += value;
            remove => _tracker.Scrolled -= value;
        }

        public event EventHandler? VisibleRangeChanged
        {
            add
            {
                if (_visibleRangeChanged is null)
                {
                    _visibleRange = VisibleRange;
                    _itemsControl.LayoutUpdated += OnLayoutUpdated;
                }

                _visibleRangeChanged += value;
            }
            remove
            {
                _visibleRangeChanged -= value;
                if (_visibleRangeChanged is null)
                    _itemsControl.LayoutUpdated -= OnLayoutUpdated;
            }
        }

        private (int, int, int) VisibleRange => (FirstVisibleIndex, LastVisibleIndex, _itemsControl.ItemCount);

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            var range = VisibleRange;
            if (range != _visibleRange)
            {
                _visibleRange = range;
                _visibleRangeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double ScrollFraction => ScrollViewer is { } sv && sv.ScrollBarMaximum.Y > 0 ?
            Math.Clamp(sv.Offset.Y / sv.ScrollBarMaximum.Y, 0, 1) :
            0;

        private ScrollViewer? ScrollViewer => _itemsControl.ItemsPanelRoot?.FindAncestorOfType<ScrollViewer>();

        private Visual? Viewport => ScrollViewer?.Presenter as Visual;

        private double ViewportHeight => Viewport?.Bounds.Height ?? 0;

        public void ScrollBy(double delta)
        {
            if (_itemsControl.ItemsPanelRoot is IScrollCommandTarget target)
                target.ScrollBy(new Vector(0, delta), ScrollSource.Programmatic);
            else if (ScrollViewer is { } sv)
                sv.Offset = new Vector(sv.Offset.X, sv.Offset.Y + delta);
        }

        public void ScrollToItem(int index, ItemAlignment alignment = ItemAlignment.Start, double offset = 0)
        {
            if (index < 0 || index >= _itemsControl.ItemCount || ScrollViewer is not { } sv)
                return;

            _itemsControl.ScrollIntoView(index);
            sv.UpdateLayout();

            if (_itemsControl.ContainerFromIndex(index) is not { } container ||
                container.TranslatePoint(default, Viewport!) is not { } p)
                return;

            var height = container.Bounds.Height;
            var target = alignment switch
            {
                ItemAlignment.Center => (ViewportHeight - height) / 2,
                ItemAlignment.End => ViewportHeight - height,
                _ => 0,
            };

            ScrollBy(p.Y - target + offset);
        }

        public void ScrollToStart() => ScrollViewer?.ScrollToHome();

        public void ScrollToEnd() => ScrollViewer?.ScrollToEnd();

        private List<(int Index, object? Item, double Top, double Bottom)> VisibleRows()
        {
            var result = new List<(int, object?, double, double)>();

            if (Viewport is not { } viewport)
                return result;

            var height = viewport.Bounds.Height;

            foreach (var c in _itemsControl.GetRealizedContainers())
            {
                if (!c.IsEffectivelyVisible || c.TranslatePoint(default, viewport) is not { } p)
                    continue;

                var bottom = p.Y + c.Bounds.Height;

                if (bottom > 0 && p.Y < height && _itemsControl.IndexFromContainer(c) is var index and >= 0)
                    result.Add((index, _itemsControl.ItemFromContainer(c), p.Y, bottom));
            }

            result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return result;
        }
    }
}
