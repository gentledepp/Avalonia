using System;
using System.Collections.Generic;
using Avalonia.Layout;

namespace Avalonia.Controls
{
    /// <summary>
    /// Raises <see cref="IItemsScrollInfo.Scrolled"/> after a layout that moved the content: compares the screen
    /// position of a row that is on the screen before and after the layout.
    /// </summary>
    internal sealed class ScrolledTracker
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
