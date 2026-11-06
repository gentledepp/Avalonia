using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Utilities;
using Avalonia.VisualTree;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Gets an <see cref="IItemsScrollInfo"/> for the tests that compare panels: the panel itself if it
    /// implements the interface, else a <see cref="ContainerScrollInfo"/> that reads the realized containers.
    /// </summary>
    internal static class TestScrollInfo
    {
        private static readonly ConditionalWeakTable<Panel, ContainerScrollInfo> s_fallbacks = new();

        public static IItemsScrollInfo Get(ItemsControl itemsControl)
        {
            var panel = itemsControl.ItemsPanelRoot ??
                throw new InvalidOperationException("The items control has no panel yet: run a layout first.");

            return panel as IItemsScrollInfo ?? s_fallbacks.GetValue(panel, _ => new ContainerScrollInfo(itemsControl));
        }
    }

    /// <summary>
    /// A test reference for panels that do not implement <see cref="IItemsScrollInfo"/>: reads the realized
    /// containers and their position in the viewport. Slow (walks all containers on each call and each layout).
    /// </summary>
    internal sealed class ContainerScrollInfo : IItemsScrollInfo
    {
        private readonly ItemsControl _itemsControl;
        private readonly ScrolledTracker _tracker;
        private EventHandler? _visibleRangeChanged;
        private (int First, int Last, int Count) _visibleRange;

        public ContainerScrollInfo(ItemsControl itemsControl)
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

        /// <summary>
        /// Raises <see cref="Scrolled"/> after a layout that moved the content: compares the screen position of a
        /// row that is on the screen before and after the layout; without such a row, the Offset change.
        /// </summary>
        private sealed class ScrolledTracker
        {
            private readonly Layoutable _owner;
            private readonly object _sender;
            private readonly Func<List<(object? Item, double Top)>> _visibleRows;
            private readonly Func<double> _offset;
            private List<(object? Item, double Top)>? _rows;
            private double _lastOffset;
            private EventHandler<ItemsScrolledEventArgs>? _scrolled;

            public ScrolledTracker(Layoutable owner, object sender, Func<List<(object? Item, double Top)>> visibleRows, Func<double> offset)
            {
                _owner = owner;
                _sender = sender;
                _visibleRows = visibleRows;
                _offset = offset;
            }

            public event EventHandler<ItemsScrolledEventArgs>? Scrolled
            {
                add
                {
                    if (_scrolled is null)
                    {
                        _rows = _visibleRows();
                        _lastOffset = _offset();
                        _owner.LayoutUpdated += OnLayoutUpdated;
                    }

                    _scrolled += value;
                }
                remove
                {
                    _scrolled -= value;

                    if (_scrolled is null)
                        _owner.LayoutUpdated -= OnLayoutUpdated;
                }
            }

            private void OnLayoutUpdated(object? sender, EventArgs e)
            {
                var rows = _visibleRows();
                var offset = _offset();
                var delta = Delta(_rows!, rows) ?? (_rows!.Count > 0 && rows.Count > 0 ? offset - _lastOffset : 0);

                _rows = rows;
                _lastOffset = offset;

                if (delta != 0)
                    _scrolled?.Invoke(_sender, new ItemsScrolledEventArgs(delta));
            }

            private static double? Delta(List<(object? Item, double Top)> before, List<(object? Item, double Top)> after)
            {
                foreach (var (item, top) in before)
                {
                    foreach (var (afterItem, afterTop) in after)
                    {
                        if (Equals(item, afterItem))
                            return top - afterTop;
                    }
                }

                return null;
            }
        }
    }
}
