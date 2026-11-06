using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;
using Row = Avalonia.Controls.UnitTests.LazyStackPanelTests_BlackBoxRecycling.Row;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// The anchor row and the realized rows after a collection change: 100 x 100 window, rows of 10 px,
    /// no cache. Unless a test says otherwise, the list is scrolled by 505 px, so row 50 is the anchor with 5 px
    /// above the top.
    /// </summary>
    public class LazyStackPanelTests_AnchorAfterChange : LazyStackPanelTestBase
    {
        private static ItemsControl CreateTarget() =>
            new() { ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }) };

        private static ItemsControl CreateTargetForObjects() =>
            new() { ItemTemplate = new FuncDataTemplate<object>((_, _) => new Border { Height = 10 }) };

        private static string[] Rows(int count, string prefix = "Item") =>
            Enumerable.Range(0, count).Select(i => $"{prefix} {i}").ToArray();

        // The screen position of the container of item.
        private static double TopOf(ItemsControl ic, string item)
        {
            var container = ic.ContainerFromItem(item);
            Assert.NotNull(container);
            var panel = ic.ItemsPanelRoot!;
            return container!.TranslatePoint(default, panel)!.Value.Y;
        }

        private static double Top(Control c, ScrollViewer sv) => c.TranslatePoint(default, sv)!.Value.Y;

        private static Control ContainerOf(ItemsControl ic, object item) =>
            ic.GetRealizedContainers()!.First(c => c.IsVisible && ReferenceEquals(c.DataContext, item));

        // DynamicData sends a Reset for more than 26 changes; here 30 new rows are inserted above the screen.
        [Fact]
        public void Reset_With_Rows_Inserted_Above_Keeps_The_Anchor_Row()
        {
            var old = Rows(100);
            var items = new ResettingCollection<string>(old);
            var ic = CreateTarget();
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 505);
            RunLayout(ic);
            Assert.Equal(-5, TopOf(ic, "Item 50"));

            items.Reset(old.Take(20).Concat(Rows(30, "New")).Concat(old.Skip(20)));
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(80, info.FirstVisibleIndex);
            Assert.Equal(5, info.FirstVisibleOffset);
            Assert.Equal(-5, TopOf(ic, "Item 50"));
            window.Close();
        }

        [Fact]
        public void Reset_With_Rows_Removed_Above_Keeps_The_Anchor_Row()
        {
            var old = Rows(100);
            var items = new ResettingCollection<string>(old);
            var ic = CreateTarget();
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 505);
            RunLayout(ic);

            items.Reset(old.Take(10).Concat(old.Skip(40)));
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(20, info.FirstVisibleIndex);
            Assert.Equal(-5, TopOf(ic, "Item 50"));
            window.Close();
        }

        // The anchor item is gone; the first row on the screen that still exists becomes the anchor, at its old
        // screen position.
        [Fact]
        public void Reset_Without_The_Anchor_Item_Keeps_The_Next_Row_On_The_Screen()
        {
            var old = Rows(100);
            var items = new ResettingCollection<string>(old);
            var ic = CreateTarget();
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 505);
            RunLayout(ic);

            items.Reset(Rows(30, "New").Concat(old.Where(x => x != "Item 50")));
            RunLayout(ic);

            Assert.Equal(5, TopOf(ic, "Item 51"));
            window.Close();
        }

        // The anchor follows its item, also at the top of the list.
        [Fact]
        public void Reset_That_Moves_The_Anchor_Item_Follows_It()
        {
            var old = Rows(100);
            var items = new ResettingCollection<string>(old);
            var ic = CreateTarget();
            var (window, _) = Show(ic, items, lazy: true);

            items.Reset(old.Take(10).Reverse().Concat(old.Skip(10)));
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(9, info.FirstVisibleIndex);
            Assert.Equal(0, TopOf(ic, "Item 0"));
            window.Close();
        }

        // A Move computes the new anchor index from the event.
        [Fact]
        public void Move_From_Below_To_Above_The_Anchor_Keeps_The_Anchor_Row()
        {
            var items = new ObservableCollection<string>(Rows(100));
            var ic = CreateTarget();
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 505);
            RunLayout(ic);

            items.Move(80, 10);
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(51, info.FirstVisibleIndex);
            Assert.Equal(-5, TopOf(ic, "Item 50"));
            window.Close();
        }

        // Scrolled by 203 px: row 20 is the anchor with 3 px above the top.
        [Fact]
        public void Move_Of_The_Anchor_Item_Follows_It()
        {
            var rows = Enumerable.Range(0, 100).Select(x => new Row(x)).ToList();
            var items = new ObservableCollection<Row>(rows);
            var ic = CreateTargetForObjects();
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 203);
            RunLayout(ic);
            var anchor = rows[20];
            Assert.Equal(-3, Top(ContainerOf(ic, anchor), sv), 1);

            items.Move(20, 50);
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(50, info.FirstVisibleIndex);
            Assert.Equal(-3, Top(ContainerOf(ic, anchor), sv), 1);
            window.Close();
        }

        // No old item exists any more after the Reset.
        [Fact]
        public void Reset_With_All_New_Instances_Keeps_The_Old_Index_And_Offset()
        {
            var items = new ResettingCollection<Row>(Enumerable.Range(0, 100).Select(x => new Row(x)));
            var ic = CreateTargetForObjects();
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 203);
            RunLayout(ic);

            items.Reset(Enumerable.Range(0, 100).Select(x => new Row(x)));
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(20, info.FirstVisibleIndex);
            Assert.Equal(3, info.FirstVisibleOffset, 1);
            window.Close();
        }

        private sealed record Item(int Id);

        // New instances that are Equals to the old ones: each row gets its new instance.
        [Fact]
        public void Reset_With_Equal_New_Instances_Prepares_The_New_Instances()
        {
            var items = new ResettingCollection<Item>(Enumerable.Range(0, 100).Select(x => new Item(x)));
            var ic = CreateTargetForObjects();
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 203);
            RunLayout(ic);

            items.Reset(Enumerable.Range(0, 100).Select(x => new Item(x)));
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(20, info.FirstVisibleIndex);
            Assert.Equal(3, info.FirstVisibleOffset, 1);
            foreach (var c in ic.GetRealizedContainers()!.Where(c => c.IsVisible))
                Assert.Same(items[ic.IndexFromContainer(c)], c.DataContext);
            window.Close();
        }

        // Between a change and the next layout, the rows on the screen still count as realized.
        [Fact]
        public void ScrollIntoView_Of_A_Row_On_The_Screen_Right_After_A_Change_Does_Not_Scroll()
        {
            var items = new ObservableCollection<string>(Rows(100));
            var ic = CreateTarget();
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 505);
            RunLayout(ic);

            items.Add("Last");
            ic.ScrollIntoView(52);
            RunLayout(ic);

            Assert.Equal(-5, TopOf(ic, "Item 50"));
            window.Close();
        }

        // As above with the Down key: a change, then a key press, then one layout. The next row is on the screen.
        [Fact]
        public void Down_Key_Right_After_A_Change_Does_Not_Move_Rows_On_The_Screen()
        {
            var items = new ObservableCollection<Row>(Enumerable.Range(0, 100).Select(x => new Row(x)));
            var lb = CreateListBox();
            var (window, sv) = Show(lb, items, lazy: true);
            ScrollBy(sv, 205);
            RunLayout(lb);
            lb.SelectedIndex = 22;
            RunLayout(lb);
            var row = lb.ContainerFromIndex(22)!;
            row.Focus();
            Assert.Equal(-5, Top(lb.ContainerFromIndex(20)!, sv), 1);

            items.Add(new Row(100));
            row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, Source = row });
            RunLayout(lb);

            Assert.Equal(23, lb.SelectedIndex);
            Assert.True(lb.ContainerFromIndex(23)!.IsFocused);
            Assert.Equal(-5, Top(lb.ContainerFromIndex(20)!, sv), 1);
            window.Close();
        }

        // The anchor index is clamped only in the next measure; the visible indexes are in range before it.
        [Fact]
        public void Visible_Indexes_Are_Below_The_Count_Right_After_A_Remove_At_The_End()
        {
            var items = new ObservableCollection<string>(Rows(100));
            var ic = CreateTarget();
            var (window, _) = Show(ic, items, lazy: true);
            ItemsScroll.Get(ic).ScrollToEnd();
            RunLayout(ic);

            for (var i = 0; i < 15; ++i)
                items.RemoveAt(items.Count - 1);

            var info = ItemsScroll.Get(ic);
            Assert.InRange(info.FirstVisibleIndex, 0, items.Count - 1);
            Assert.InRange(info.LastVisibleIndex, 0, items.Count - 1);
            window.Close();
        }

        private static ListBox CreateListBox() => new()
        {
            ItemTemplate = new FuncDataTemplate<Row>((_, _) => new Border { Height = 10 }),
            ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
            {
                Setters =
                {
                    new Setter(ListBoxItem.TemplateProperty, new FuncControlTemplate<ListBoxItem>((_, _) =>
                        new ContentPresenter
                        {
                            Name = "PART_ContentPresenter",
                            [~ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty),
                            [~ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentControl.ContentTemplateProperty),
                        })),
                    new Setter(InputElement.FocusableProperty, true),
                },
            },
        };
    }
}
