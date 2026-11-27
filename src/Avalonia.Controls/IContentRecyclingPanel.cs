namespace Avalonia.Controls
{
    /// <summary>
    /// Marks a <see cref="VirtualizingPanel"/> that recycles containers together with their
    /// content.
    /// </summary>
    /// <remarks>
    /// When the items panel implements this interface, <see cref="ItemsControl"/> uses a recycle
    /// key per data template or item type and keeps the content of a container that is recycled
    /// under such a key. The panel must only reuse a container for an item with the same key.
    /// </remarks>
    public interface IContentRecyclingPanel
    {
    }
}
