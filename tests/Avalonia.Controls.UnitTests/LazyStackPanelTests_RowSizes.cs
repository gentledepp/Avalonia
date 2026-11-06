using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Rows with a margin, a short list that does not stretch, and rows taller than the viewport.
    /// </summary>
    public class LazyStackPanelTests_RowSizes : LazyStackPanelTestBase
    {
        // Containers with a vertical margin: after a Reset that removed the anchor item, the first old row that
        // still exists stays at its old screen position, although its Bounds.Top is inside the margin.
        [Fact]
        public void Reset_Without_The_Anchor_Item_Keeps_Rows_With_A_Margin_In_Place()
        {
            var items = Enumerable.Range(0, 100).Select(x => $"Item {x}").ToList();
            var source = new ResettingCollection<string>(items);
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 30 }),
            };
            ic.ContainerPrepared += (_, e) => e.Container.Margin = new Thickness(0, 4);
            var (window, sv) = Show(ic, source, lazy: true);

            ScrollBy(sv, 50);   // anchor: row 1 (slots of 38 px), 12 px above the top
            RunLayout(ic);
            var row2 = ic.ContainerFromIndex(2)!;
            var before = Top(row2, sv);

            source.Reset(items.Where(x => x != "Item 1"));
            RunLayout(ic);

            Assert.Same(row2, ic.ContainerFromIndex(1));
            Assert.Equal(before, Top(row2, sv), 0.5);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_Short_List_That_Does_Not_Stretch_Has_The_Height_Of_Its_Rows(bool lazy)
        {
            var ic = new ItemsControl
            {
                VerticalAlignment = VerticalAlignment.Top,
                ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 30 }),
            };
            var (window, _) = Show(ic, new ObservableCollection<string>(new[] { "a", "b" }), lazy);

            Assert.Equal(60, ic.Bounds.Height, 0.5);
            window.Close();
        }

        // Rows of 40 px, row 5 is 1300 px, viewport 600; scroll down in steps of 20 px. No row on the screen may
        // move by anything else than the step (the bottom of the tall row must not come back).
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void Scrolling_Past_A_Row_Taller_Than_The_Viewport_Moves_The_Content_By_Each_Step(bool lazy, bool focusInTallRow)
        {
            var items = Enumerable.Range(0, 40).Select(x => $"Item {x}").ToList();
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<string>((x, _) => new Border
                {
                    Height = x == "Item 5" ? 1300 : 40,
                    Child = new Button { Name = "Button", Height = 30, VerticalAlignment = VerticalAlignment.Bottom },
                }),
            };
            var (window, sv) = Show(ic, new ObservableCollection<string>(items), lazy, cacheLength: 0.5, size: 600);
            Button? focused = null;

            if (focusInTallRow)
            {
                ScrollBy(sv, 900);   // the tall row's button (1460-1490) on the screen
                RunLayout(ic);
                focused = ((Border)((ContentPresenter)ic.ContainerFromIndex(5)!).Child!).Child as Button;
                Assert.True(focused!.Focus());
                RunLayout(ic);
            }

            var jumps = new System.Collections.Generic.List<string>();

            // Not into the end (2860 - 600 = 2260).
            for (var step = 0; step < (focusInTallRow ? 60 : 100); ++step)
            {
                var before = VisibleTops(ic, sv, items);
                ScrollBy(sv, 20);
                RunLayout(ic);
                var after = VisibleTops(ic, sv, items);

                foreach (var (item, top) in before)
                {
                    if (after.TryGetValue(item, out var newTop) && System.Math.Abs(newTop - top + 20) > 0.5)
                    {
                        jumps.Add($"step {step}: {item} moved {newTop - top:0.#} (expected -20)");
                        break;
                    }
                }
            }

            Assert.True(jumps.Count == 0, string.Join("\n", jumps.Take(10)) + $"\n({jumps.Count} jumps)");
            // The VSP recycles the focused row when it scrolls out and loses the focus.
            if (focused is not null)
                Assert.True(focused.IsFocused == lazy, $"focused: {focused.IsFocused}, expected {lazy}");
            window.Close();
        }

        // A row that covers the whole viewport is not scrolled to its top (e.g. on a focus change inside it).
        [Fact]
        public void BringIntoView_Of_A_Row_Taller_Than_The_Viewport_That_Covers_It_Does_Not_Scroll()
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<string>((x, _) => new Border { Height = x == "Item 5" ? 1300 : 40 }),
            };
            var (window, sv) = Show(ic, new ObservableCollection<string>(
                Enumerable.Range(0, 40).Select(x => $"Item {x}")), lazy: true, size: 600);
            ScrollBy(sv, 400);   // row 5: -200 to 1100
            RunLayout(ic);
            var row5 = ic.ContainerFromIndex(5)!;
            Assert.Equal(-200, Top(row5, sv), 0.5);

            row5.BringIntoView();
            RunLayout(ic);

            Assert.Equal(-200, Top(row5, sv), 0.5);
            window.Close();
        }

        // A tall row partly below the viewport: its top is the nearer edge, so it goes to the viewport top.
        [Fact]
        public void BringIntoView_Of_A_Tall_Row_Below_Aligns_The_Nearer_Edge()
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<string>((x, _) => new Border { Height = x == "Item 5" ? 1300 : 40 }),
            };
            var (window, sv) = Show(ic, new ObservableCollection<string>(
                Enumerable.Range(0, 40).Select(x => $"Item {x}")), lazy: true, size: 600);
            var row5 = ic.ContainerFromIndex(5)!;   // 200 to 1500

            row5.BringIntoView();
            RunLayout(ic);

            Assert.Equal(0, Top(row5, sv), 0.5);
            window.Close();
        }

        // A tall row partly above the viewport: its bottom is the nearer edge, so it goes to the viewport bottom
        // (a move of 100 px, not 800 px).
        [Fact]
        public void BringIntoView_Of_A_Tall_Row_Above_Aligns_The_Nearer_Edge()
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<string>((x, _) => new Border { Height = x == "Item 5" ? 1300 : 40 }),
            };
            var (window, sv) = Show(ic, new ObservableCollection<string>(
                Enumerable.Range(0, 40).Select(x => $"Item {x}")), lazy: true, size: 600);
            ScrollBy(sv, 1000);   // row 5: -800 to 500
            RunLayout(ic);
            var row5 = ic.ContainerFromIndex(5)!;
            Assert.Equal(-800, Top(row5, sv), 0.5);

            row5.BringIntoView();
            RunLayout(ic);

            Assert.Equal(-700, Top(row5, sv), 0.5);
            window.Close();
        }

        private static System.Collections.Generic.Dictionary<string, double> VisibleTops(
            ItemsControl ic, ScrollViewer sv, System.Collections.Generic.List<string> items)
        {
            var result = new System.Collections.Generic.Dictionary<string, double>();

            for (var i = 0; i < items.Count; ++i)
            {
                if (ic.ContainerFromIndex(i) is { IsVisible: true } c && c.DataContext is string item &&
                    c.TranslatePoint(default, sv) is { } p && p.Y < sv.Viewport.Height && p.Y + c.Bounds.Height > 0)
                    result[item] = p.Y;
            }

            return result;
        }

        private static double Top(Control c, ScrollViewer sv) => c.TranslatePoint(default, sv)!.Value.Y;
    }
}
