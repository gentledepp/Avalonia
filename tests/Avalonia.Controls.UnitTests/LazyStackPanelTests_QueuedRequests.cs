using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Several scroll requests before one layout: the result is the same as when each request had its own layout.
    /// The VSP fails these: its presenter computes BringIntoView from the positions of the last layout.
    /// 100 x 100 window, rows of 10 px, no cache.
    /// </summary>
    public class LazyStackPanelTests_QueuedRequests : LazyStackPanelTestBase
    {
        private static ItemsControl CreateTarget() =>
            new() { ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }) };

        private static ObservableCollection<string> Rows(int count = 100) =>
            new(Enumerable.Range(0, count).Select(i => $"Item {i}"));

        private static double Top(ItemsControl ic, int index, ScrollViewer sv)
        {
            var c = ic.ContainerFromIndex(index);
            Assert.True(c is not null, $"row {index} not realized");
            var p = c!.TranslatePoint(default, sv);
            Assert.True(p is not null, $"row {index}: visible {c.IsVisible}, parent {c.GetVisualParent()}, offset {sv.Offset}");
            return p!.Value.Y;
        }

        // An index request replaces a relative scroll that was queued after a jump.
        [Fact]
        public void ScrollIntoView_After_A_Jump_And_A_ScrollBy_Aligns_The_Row()
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy: true);

            sv.ScrollToEnd();
            ScrollBy(sv, 10);
            ic.ScrollIntoView(50);
            RunLayout(ic);

            // From the end, row 50 is above: it is aligned with the top.
            Assert.Equal(0, Top(ic, 50, sv), 1);
            window.Close();
        }

        // BringIntoView starts from the position after the queued relative scroll.
        [Fact]
        public void ScrollIntoView_After_A_ScrollBy_Shows_The_Row()
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy: true);

            ScrollBy(sv, 10);
            ic.ScrollIntoView(0);
            RunLayout(ic);

            Assert.Equal(0, Top(ic, 0, sv), 1);
            window.Close();
        }

        // A wheel step up and a focus change (Tab) to the row cut by the bottom.
        [Fact]
        public void BringIntoView_After_A_ScrollBy_Shows_The_Row()
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy: true);
            ScrollBy(sv, 5);
            RunLayout(ic);
            Assert.Equal(95, Top(ic, 10, sv), 1);

            ScrollBy(sv, -5);
            ic.ContainerFromIndex(10)!.BringIntoView();
            RunLayout(ic);

            Assert.Equal(sv.Viewport.Height, Top(ic, 10, sv) + 10, 1);
            window.Close();
        }

        // BringIntoView of a realized row after a jump that has no layout yet.
        [Fact]
        public void BringIntoView_After_A_Jump_Shows_The_Row()
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy: true);
            var row = ic.ContainerFromIndex(3)!;

            sv.ScrollToEnd();
            row.BringIntoView();
            RunLayout(ic);

            Assert.Equal(0, Top(ic, 3, sv), 1);
            window.Close();
        }
    }
}
