using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Primitives;

namespace Avalonia.Controls
{
    /// <summary>
    /// Where <see cref="IItemsScrollInfo.ScrollToItem"/> puts the item.
    /// </summary>
    public enum ItemAlignment
    {
        /// <summary>
        /// The top of the item on the top of the viewport.
        /// </summary>
        Start,

        /// <summary>
        /// The center of the item on the center of the viewport.
        /// </summary>
        Center,

        /// <summary>
        /// The bottom of the item on the bottom of the viewport.
        /// </summary>
        End,
    }

    /// <summary>
    /// Provides data for the <see cref="IItemsScrollInfo.Scrolled"/> event.
    /// </summary>
    public class ItemsScrolledEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ItemsScrolledEventArgs"/> class.
        /// </summary>
        /// <param name="delta">How far the content moved on the screen.</param>
        /// <param name="source">What started the scroll.</param>
        public ItemsScrolledEventArgs(double delta, ScrollSource source = ScrollSource.Unknown)
        {
            Delta = delta;
            Source = source;
        }

        /// <summary>
        /// How far the content moved on the screen; positive when it moved up (a scroll forward).
        /// </summary>
        public double Delta { get; }

        /// <summary>
        /// What started the scroll. <see cref="ScrollSource.Unknown"/> on a panel that cannot tell.
        /// </summary>
        public ScrollSource Source { get; }
    }

    /// <summary>
    /// The scroll state of an items control, the same for every panel. Get it with
    /// <see cref="ItemsScroll.Get"/>.
    /// </summary>
    public interface IItemsScrollInfo
    {
        /// <summary>
        /// The index of the first row on the screen, or -1 if there is none.
        /// </summary>
        int FirstVisibleIndex { get; }

        /// <summary>
        /// The pixels of the first visible row that are above the viewport top.
        /// </summary>
        double FirstVisibleOffset { get; }

        /// <summary>
        /// The index of the last row on the screen, or -1 if there is none.
        /// </summary>
        int LastVisibleIndex { get; }

        /// <summary>
        /// True when there is content above the viewport top.
        /// </summary>
        bool CanScrollBackward { get; }

        /// <summary>
        /// True when there is content below the viewport bottom.
        /// </summary>
        bool CanScrollForward { get; }

        /// <summary>
        /// Raised after a layout in which a relative scroll moved the content on the screen. Not raised for a
        /// jump, a collection change or a size change.
        /// </summary>
        event EventHandler<ItemsScrolledEventArgs>? Scrolled;

        /// <summary>
        /// Raised after a layout that changed <see cref="FirstVisibleIndex"/>, <see cref="LastVisibleIndex"/>
        /// or the item count, for any reason (scroll, jump, collection change, size change).
        /// </summary>
        event EventHandler? VisibleRangeChanged;

        /// <summary>
        /// An estimate between 0 and 1, only for a scrollbar or a position indicator.
        /// </summary>
        double ScrollFraction { get; }

        /// <summary>
        /// Scrolls by <paramref name="delta"/> pixels; positive scrolls forward.
        /// </summary>
        void ScrollBy(double delta);

        /// <summary>
        /// Scrolls the item at <paramref name="index"/> to <paramref name="alignment"/>, then by
        /// <paramref name="offset"/> pixels forward. The index wins over a collection change before the next layout.
        /// </summary>
        void ScrollToItem(int index, ItemAlignment alignment = ItemAlignment.Start, double offset = 0);

        /// <summary>
        /// Scrolls to the top of the first item.
        /// </summary>
        void ScrollToStart();

        /// <summary>
        /// Scrolls to the bottom of the last item.
        /// </summary>
        void ScrollToEnd();
    }

    /// <summary>
    /// Gets the <see cref="IItemsScrollInfo"/> of an <see cref="ItemsControl"/>.
    /// </summary>
    public static class ItemsScroll
    {
        private static readonly ConditionalWeakTable<ItemsControl, ItemsScrollState> s_states = new();

        /// <summary>
        /// The same object for each call with <paramref name="itemsControl"/>, also before its panel exists and
        /// after the panel is replaced. It uses the panel if it implements <see cref="IItemsScrollInfo"/>, else a
        /// fallback that reads the realized containers; without a panel it reports no rows, and keeps a command
        /// until the panel exists.
        /// </summary>
        public static IItemsScrollInfo Get(ItemsControl itemsControl) =>
            s_states.GetValue(itemsControl, x => new ItemsScrollState(x));
    }
}
