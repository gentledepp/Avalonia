namespace Avalonia.Controls.Primitives
{
    /// <summary>
    /// An edge for <see cref="IScrollCommandTarget.ScrollToEdge"/>.
    /// </summary>
    public enum ScrollEdge
    {
        /// <summary>
        /// The start, as <see cref="ScrollViewer.ScrollToHome"/>.
        /// </summary>
        Start,

        /// <summary>
        /// The end, as <see cref="ScrollViewer.ScrollToEnd"/>.
        /// </summary>
        End,
    }

    /// <summary>
    /// What started a scroll request, for <see cref="IScrollCommandTarget.ScrollBy"/>.
    /// </summary>
    public enum ScrollSource
    {
        /// <summary>
        /// Not known: an Offset write, or a panel that cannot tell.
        /// </summary>
        Unknown,

        /// <summary>
        /// Code: the public scroll methods, <c>BringIntoView</c> and <c>ScrollIntoView</c>.
        /// </summary>
        Programmatic,

        /// <summary>
        /// The mouse wheel or a touchpad.
        /// </summary>
        Wheel,

        /// <summary>
        /// A touch or pen gesture, with its inertia.
        /// </summary>
        Touch,

        /// <summary>
        /// A key of the <see cref="ScrollViewer"/> (PageUp, PageDown).
        /// </summary>
        Keyboard,

        /// <summary>
        /// The buttons, the track or the thumb of a scrollbar.
        /// </summary>
        ScrollBar,
    }

    /// <summary>
    /// An <see cref="ILogicalScrollable"/> that receives scroll requests as commands instead of
    /// writes to <see cref="IScrollable.Offset"/>.
    /// </summary>
    /// <remarks>
    /// When the content of a <see cref="Presenters.ScrollContentPresenter"/> (or the panel of an
    /// <see cref="ItemsControl"/> in it) implements this interface, the presenter, the
    /// <see cref="ScrollViewer"/> and its scrollbars send these commands, do not write
    /// <see cref="IScrollable.Offset"/> and do not clamp it. The offset reported by the target is
    /// copied to the <see cref="ScrollViewer"/> only; the target must keep it between 0 and
    /// <c>Extent - Viewport</c>.
    /// </remarks>
    public interface IScrollCommandTarget : ILogicalScrollable
    {
        /// <summary>
        /// Scrolls by a distance: mouse wheel, touch gesture, line and page scrolls, scrollbar buttons.
        /// </summary>
        /// <param name="delta">The distance.</param>
        /// <param name="source">What started the request.</param>
        /// <returns>The part of <paramref name="delta"/> that the target scrolls.</returns>
        Vector ScrollBy(Vector delta, ScrollSource source);

        /// <summary>
        /// Scrolls to an edge: <see cref="ScrollViewer.ScrollToHome"/> and <see cref="ScrollViewer.ScrollToEnd"/>.
        /// </summary>
        void ScrollToEdge(ScrollEdge edge);

        /// <summary>
        /// Scrolls to a fraction of the scrollable range on each axis: the scrollbar thumb.
        /// </summary>
        /// <param name="fraction">0 is the start and 1 is the end.</param>
        void ScrollToFraction(Vector fraction);

        /// <summary>
        /// Scrolls to an offset: code that sets <see cref="ScrollViewer.Offset"/>. The value is not clamped.
        /// </summary>
        void ScrollToOffset(Vector offset);
    }
}
