using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// BringIntoView of a descendant, ScrollIntoView of a buffer row and of a selection, against both panels.
    /// 100 x 100 window, rows of 30 px with an inner element.
    /// </summary>
    public class LazyStackPanelTests_ScrollIntoView : LazyStackPanelTestBase
    {
        private const string NoAutoScrollToMovedSelection =
            "SelectingItemsControl does not auto-scroll when a collection change only moves the selected item.";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BringIntoView_Of_A_Descendant_Below_Aligns_Its_Rect_With_The_Bottom(bool lazy)
        {
            var (window, ic, sv) = ShowRows(lazy, cacheLength: 0);
            var inner = Inner(ic, 3);   // row 3 is at 90-120, its inner element at 100-110

            inner.BringIntoView();
            RunLayout(ic);

            Assert.Equal(sv.Viewport.Height, Top(inner, sv) + inner.Bounds.Height, 1);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BringIntoView_Of_A_Descendant_Above_Aligns_Its_Rect_With_The_Top(bool lazy)
        {
            var (window, ic, sv) = ShowRows(lazy, cacheLength: 0);
            sv.Offset = new Vector(0, 45);
            RunLayout(ic);
            var inner = Inner(ic, 1);   // row 1 is at -15-15, its inner element at -5-5

            inner.BringIntoView();
            RunLayout(ic);

            Assert.Equal(0, Top(inner, sv), 1);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScrollIntoView_Of_A_Buffer_Row_Below_Shows_It(bool lazy)
        {
            var (window, ic, sv) = ShowRows(lazy, cacheLength: 0.5);
            Assert.NotNull(ic.ContainerFromIndex(4));   // in the buffer: 120-150

            ic.ScrollIntoView(4);
            RunLayout(ic);

            var row = ic.ContainerFromIndex(4)!;
            Assert.Equal(sv.Viewport.Height, Top(row, sv) + row.Bounds.Height, 1);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScrollIntoView_Of_A_Buffer_Row_Above_Shows_It(bool lazy)
        {
            var (window, ic, sv) = ShowRows(lazy, cacheLength: 0.5);
            sv.Offset = new Vector(0, 300);
            RunLayout(ic);
            Assert.NotNull(ic.ContainerFromIndex(8));   // in the buffer: -60 to -30

            ic.ScrollIntoView(8);
            RunLayout(ic);

            Assert.Equal(0, Top(ic.ContainerFromIndex(8)!, sv), 1);
            window.Close();
        }

        // The VSP's ScrollIntoView raises RequestBringIntoView, so a handler can stop it. The LazyStackPanel
        // scrolls by itself (by index, the smallest move) and does not raise it.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScrollIntoView_Raises_RequestBringIntoView_Only_With_The_Vsp(bool lazy)
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 30 }),
            };
            var (window, sv) = Show(ic, new ObservableCollection<string>(
                Enumerable.Range(0, 100).Select(x => $"Item {x}")), lazy);
            var raised = 0;
            ic.FindDescendantOfType<ItemsPresenter>()!.AddHandler(Control.RequestBringIntoViewEvent, (_, e) =>
            {
                ++raised;
                e.Handled = true;
            });
            var row0 = ic.ContainerFromIndex(0)!;

            ic.ScrollIntoView(3);   // partly visible: 90-120
            RunLayout(ic);

            // VSP: the handler stops it, nothing moves. Lazy: row 3's bottom goes to the viewport bottom.
            Assert.Equal(lazy ? 0 : 1, raised);
            Assert.Equal(lazy ? -20 : 0, Top(row0, sv), 0.5);
            window.Close();
        }

        [Theory]
        [InlineData(false, true, Skip = NoAutoScrollToMovedSelection)]
        [InlineData(true, true, Skip = NoAutoScrollToMovedSelection)]
        [InlineData(false, false)]
        [InlineData(true, false)]
        public void ListBox_Insert_Above_A_Selection_That_Is_Not_On_The_Screen(bool lazy, bool autoScroll)
        {
            // Upstream behaviour: a moved selection anchor makes the ListBox scroll to it
            // (SelectingItemsControl.OnSelectionModelPropertyChanged), with any panel.
            var items = new ObservableCollection<LazyStackPanelTests_BlackBoxRecycling.Row>(
                Enumerable.Range(0, 100).Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x)));
            var lb = new ListBox
            {
                AutoScrollToSelectedItem = autoScroll,
                ItemTemplate = new FuncDataTemplate<LazyStackPanelTests_BlackBoxRecycling.Row>((_, _) => new Border { Height = 10 }),
            };
            var (window, sv) = Show(lb, items, lazy);
            lb.SelectedIndex = 50;
            RunLayout(lb);
            sv.Offset = new Vector(0, 200);     // the selection (row 50) is below the screen
            RunLayout(lb);
            var first = lb.GetRealizedContainers().First(c => Top(c, sv) + c.Bounds.Height > 0).DataContext!;
            var firstTop = Top(lb.ContainerFromItem(first)!, sv);

            items.Insert(0, new LazyStackPanelTests_BlackBoxRecycling.Row(1000));
            RunLayout(lb);

            Assert.Equal(51, lb.SelectedIndex);
            if (autoScroll)
            {
                // The list jumps to the selection.
                var selected = lb.ContainerFromIndex(51)!;
                Assert.InRange(Top(selected, sv), 0, sv.Viewport.Height - selected.Bounds.Height);
            }
            else if (lazy)
            {
                // The content does not move.
                Assert.Equal(firstTop, Top(lb.ContainerFromItem(first)!, sv), 1);
            }
            window.Close();
        }

        [Theory]
        [InlineData(false, false, false)]   // the VSP drops a request while the list is not visible
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public void Pending_ScrollIntoView_Follows_The_Item(bool lazy, bool reset, bool expectShown)
        {
            var items = new ResettingCollection<LazyStackPanelTests_BlackBoxRecycling.Row>(
                Enumerable.Range(0, 100).Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x)));
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<LazyStackPanelTests_BlackBoxRecycling.Row>((_, _) => new Border { Height = 30 }),
            };
            var (window, sv) = Show(ic, items, lazy);
            var target = items[40];
            ic.IsVisible = false;
            RunLayout(ic);

            ic.ScrollIntoView(40);
            var inserted = Enumerable.Range(1000, 5).Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x)).ToList();
            if (reset)
                items.Reset(inserted.Concat(items).ToList());
            else
                for (var i = 0; i < inserted.Count; i++)
                    items.Insert(i, inserted[i]);
            ic.IsVisible = true;
            RunLayout(ic);

            var row = ic.ContainerFromItem(target);
            Assert.Equal(expectShown, row is not null && Top(row, sv) >= 0 && Top(row, sv) + row.Bounds.Height <= sv.Viewport.Height + 0.5);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScrollIntoView_During_Layout_Does_Nothing(bool lazy)
        {
            // As the VSP: a call while the panel measures does not realize the row and does not scroll.
            var items = new ObservableCollection<LazyStackPanelTests_BlackBoxRecycling.Row>(
                Enumerable.Range(0, 100).Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x)));
            var ic = new ItemsControl();
            var called = false;
            Control? result = new Border();
            ic.ItemTemplate = new FuncDataTemplate<LazyStackPanelTests_BlackBoxRecycling.Row>((r, _) => r?.Id == 1000 ?
                new MeasureCallback(() =>
                {
                    if (called)
                        return;
                    called = true;
                    ic.ScrollIntoView(80);
                    result = ic.ContainerFromIndex(80);
                }) { Height = 10 } :
                new Border { Height = 10 });
            var (window, sv) = Show(ic, items, lazy);

            items.Insert(2, new LazyStackPanelTests_BlackBoxRecycling.Row(1000));
            RunLayout(ic);

            Assert.True(called);
            Assert.Null(result);
            Assert.Equal(0, Top(ic.ContainerFromIndex(0)!, sv), 1);
            window.Close();
        }

        private class MeasureCallback : Border
        {
            private readonly System.Action _callback;

            public MeasureCallback(System.Action callback) => _callback = callback;

            protected override Size MeasureOverride(Size availableSize)
            {
                _callback();
                return base.MeasureOverride(availableSize);
            }
        }

        private static (Window, ItemsControl, ScrollViewer) ShowRows(bool lazy, double cacheLength)
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<LazyStackPanelTests_BlackBoxRecycling.Row>((_, _) => new Border
                {
                    Height = 30,
                    Child = new Border { Name = "Inner", Height = 10, Margin = new Thickness(0, 10) },
                }),
            };
            var (window, sv) = Show(ic, new ObservableCollection<LazyStackPanelTests_BlackBoxRecycling.Row>(
                Enumerable.Range(0, 100).Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x))), lazy, cacheLength);
            return (window, ic, sv);
        }

        private static Control Inner(ItemsControl ic, int index) =>
            ((Border)((ContentPresenter)ic.ContainerFromIndex(index)!).Child!).Child!;

        private static double Top(Control c, ScrollViewer sv) => c.TranslatePoint(default, sv)!.Value.Y;
    }
}
