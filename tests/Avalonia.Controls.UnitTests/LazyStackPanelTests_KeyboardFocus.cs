using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.UnitTests;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// The key and focus retention tests of VirtualizingStackPanelTests and ListBoxTests, against both panels:
    /// 100 x 100 window, rows of 10 px, no cache.
    /// </summary>
    public class LazyStackPanelTests_KeyboardFocus : LazyStackPanelTestBase
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Arrow_Keys_Move_Selection_And_Focus(bool lazy)
        {
            var (window, lb, _) = ShowListBox(lazy, 100);
            Focus(lb, 0);

            Press(lb, Key.Down);
            Press(lb, Key.Down);
            Press(lb, Key.Up);

            Assert.Equal(1, lb.SelectedIndex);
            Assert.True(lb.ContainerFromIndex(1)!.IsFocused);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Down_Key_Brings_Unrealized_Row_Into_View(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            Focus(lb, 0);

            ScrollBy(sv, 100);
            ScrollBy(sv, 100);
            Assert.True(FirstVisibleIndex(lb, sv) > 10);

            Press(lb, Key.Down);

            Assert.Equal(1, lb.SelectedIndex);
            Assert.True(lb.ContainerFromIndex(1)!.IsFocused);
            AssertFullyVisible(lb.ContainerFromIndex(1)!, sv);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void End_And_Home_Keys_Select_And_Show_The_Last_And_First_Row(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            Focus(lb, 0);

            Press(lb, Key.End);

            Assert.Equal(99, lb.SelectedIndex);
            var last = lb.ContainerFromIndex(99)!;
            Assert.True(last.IsFocused);
            Assert.Equal(sv.Viewport.Height, Top(last, sv) + last.Bounds.Height, 1);

            Press(lb, Key.Home);

            Assert.Equal(0, lb.SelectedIndex);
            var first = lb.ContainerFromIndex(0)!;
            Assert.True(first.IsFocused);
            Assert.Equal(0, Top(first, sv), 1);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PageDown_Scrolls_One_Viewport_And_Keeps_Selection_And_Focus(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            var focused = Focus(lb, 0);
            var belowIndex = FirstVisibleIndex(lb, sv) + 10;

            Press(lb, Key.PageDown);

            // The row that was just below the viewport is now at the top.
            Assert.Equal(belowIndex, FirstVisibleIndex(lb, sv));
            Assert.Equal(0, Top(lb.ContainerFromIndex(belowIndex)!, sv), 1);
            Assert.Equal(0, lb.SelectedIndex);
            Assert.True(focused.IsFocused);

            Press(lb, Key.PageUp);

            Assert.Equal(0, Top(lb.ContainerFromIndex(0)!, sv), 1);
            Assert.Same(focused, lb.ContainerFromIndex(0));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Focused_Row_Stays_Realized_When_It_Scrolls_Out(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            var focused = Focus(lb, 0);

            ScrollBy(sv, 100);
            ScrollBy(sv, 100);

            var panel = lb.ItemsPanelRoot!;
            Assert.Contains(focused, panel.Children);
            Assert.True(focused.IsVisible);
            Assert.True(focused.IsFocused);
            Assert.Same(focused, lb.ContainerFromIndex(0));
            Assert.Equal(0, lb.IndexFromContainer(focused));
            Assert.DoesNotContain(focused, lb.GetRealizedContainers());
            Assert.True(Top(focused, sv) + focused.Bounds.Height <= 0, $"focused row at {Top(focused, sv)}");

            // Scrolling back uses the same container.
            ScrollBy(sv, -100);
            ScrollBy(sv, -100);
            Assert.Same(focused, lb.ContainerFromIndex(0));
            Assert.Contains(focused, lb.GetRealizedContainers());
            Assert.Equal(0, Top(focused, sv), 1);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Focusing_Another_Row_Recycles_The_Kept_One(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            var original = Focus(lb, 0);

            ScrollBy(sv, 100);
            ScrollBy(sv, 100);
            Focus(lb, FirstVisibleIndex(lb, sv));
            RunLayout(sv);

            Assert.False(original.IsVisible && lb.ItemsPanelRoot!.Children.Contains(original));
            Assert.Null(lb.ContainerFromIndex(0));
            Assert.Equal(lb.GetRealizedContainers().Count(), lb.ItemsPanelRoot!.Children.Count(x => x.IsVisible));
            window.Close();
        }

        /// <summary>
        /// The VSP loses the kept row and the focus: the selection anchor moves first, and
        /// SelectingItemsControl sets TabOnceActiveElement to ContainerFromIndex(6) before the panel has
        /// moved its focused index. That is null, so the VSP recycles the row.
        /// <see cref="SelectingItemsControl.AutoScrollToSelectedItem"/> is off: a moved selection anchor scrolls
        /// the list to the selected row, with any panel.
        /// </summary>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void Kept_Focused_Row_Follows_Inserts_And_Removes_Above(bool lazy, bool expectKept)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            var items = (ObservableCollection<LazyStackPanelTests_BlackBoxRecycling.Row>)lb.ItemsSource!;
            lb.AutoScrollToSelectedItem = false;
            var focused = Focus(lb, 5);

            ScrollBy(sv, 100);
            ScrollBy(sv, 100);

            items.Insert(0, new LazyStackPanelTests_BlackBoxRecycling.Row(-1));
            if (!expectKept)
            {
                Assert.Null(lb.ContainerFromIndex(6));
                Assert.False(focused.IsFocused);
                window.Close();
                return;
            }

            items.Insert(0, new LazyStackPanelTests_BlackBoxRecycling.Row(-2));
            Assert.Same(focused, lb.ContainerFromIndex(7));
            Assert.Equal(7, lb.IndexFromContainer(focused));
            RunLayout(sv);
            Assert.Same(focused, lb.ContainerFromIndex(7));

            items.RemoveAt(0);
            Assert.Same(focused, lb.ContainerFromIndex(6));
            RunLayout(sv);
            Assert.Same(focused, lb.ContainerFromIndex(6));
            Assert.True(focused.IsFocused);
            Assert.Equal(lb.GetRealizedContainers().Count() + 1, lb.ItemsPanelRoot!.Children.Count(x => x.IsVisible));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Removing_The_Kept_Focused_Row_Clears_Focus(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            var items = (ObservableCollection<LazyStackPanelTests_BlackBoxRecycling.Row>)lb.ItemsSource!;
            var focused = Focus(lb, 0);

            ScrollBy(sv, 100);
            ScrollBy(sv, 100);
            items.RemoveAt(0);
            RunLayout(sv);

            Assert.False(focused.IsFocused);
            Assert.Equal(-1, lb.IndexFromContainer(focused));
            Assert.DoesNotContain(lb.GetRealizedContainers(), x => x == focused);
            Assert.Equal(lb.GetRealizedContainers().Count(), lb.ItemsPanelRoot!.Children.Count(x => x.IsVisible));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Kept_Focused_Row_Is_Not_Part_Of_The_Estimate(bool lazy)
        {
            // Row 0 is 1000 px; the others 10 px. With row 0 kept, the extent must not change.
            var rows = Enumerable.Range(0, 100).Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x, x == 0 ? 1000 : 10));
            var (window, lb, sv) = ShowListBox(lazy, rows);
            var (window2, lb2, sv2) = ShowListBox(lazy, rows.Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x.Id, x.Height)));
            Focus(lb, 0);

            foreach (var s in new[] { sv, sv2 })
            {
                s.Offset = new Vector(0, 1200);
                RunLayout(s);
                ScrollBy(s, 50);
            }

            Assert.True(FirstVisibleIndex(lb, sv) > 0);
            Assert.Equal(FirstVisibleIndex(lb2, sv2), FirstVisibleIndex(lb, sv));
            Assert.Equal(sv2.Extent.Height, sv.Extent.Height, 3);
            window.Close();
            window2.Close();
        }

        /// <summary>
        /// An <see cref="ItemsControl"/> whose rows contain a <see cref="TextBox"/>. The focus is in the text box, so
        /// <see cref="KeyboardNavigation.GetTabOnceActiveElement"/> is the text box, not the container, and the VSP
        /// recycles the row. The text box loses the focus: with a plain template it leaves the tree, with an
        /// <see cref="IVirtualizingDataTemplate"/> its container is hidden.
        /// </summary>
        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(false, true, false, false)]
        [InlineData(true, false, true, true)]
        [InlineData(true, true, true, true)]
        public void Row_With_A_Focused_TextBox_When_It_Scrolls_Out(bool lazy, bool ivdt, bool expectKept, bool expectFocused)
        {
            Control Build(LazyStackPanelTests_BlackBoxRecycling.Row? r) => new TextBox
            {
                Height = 30,
                MinHeight = 0,
                Padding = new Thickness(0),
                [!TextBox.TextProperty] = new Avalonia.Data.Binding(nameof(LazyStackPanelTests_BlackBoxRecycling.Row.Id)),
            };
            var ic = new ItemsControl
            {
                ItemTemplate = ivdt ?
                    new TextBoxTemplate(() => Build(null)) :
                    new FuncDataTemplate<LazyStackPanelTests_BlackBoxRecycling.Row>((r, _) => Build(r)),
            };
            var (window, sv) = Show(ic, new ObservableCollection<LazyStackPanelTests_BlackBoxRecycling.Row>(
                Enumerable.Range(0, 100).Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x))), lazy);
            var container = ic.ContainerFromIndex(1)!;
            var textBox = container.GetVisualDescendants().OfType<TextBox>().First();
            textBox.Focus();
            Assert.True(textBox.IsFocused);
            Assert.Same(textBox, KeyboardNavigation.GetTabOnceActiveElement(ic));

            ScrollBy(sv, 100);
            ScrollBy(sv, 100);

            Assert.Equal(expectKept, ic.ContainerFromIndex(1) == container && container.IsVisible);
            Assert.Equal(expectFocused, textBox.IsFocused);
            if (expectFocused)
                Assert.Equal("1", textBox.Text);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Down_Key_On_The_Last_Visible_Row_Scrolls_By_One_Row(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            Focus(lb, 9);

            Press(lb, Key.Down);

            Assert.Equal(10, lb.SelectedIndex);
            var row = lb.ContainerFromIndex(10)!;
            Assert.True(row.IsFocused);
            Assert.Equal(sv.Viewport.Height, Top(row, sv) + row.Bounds.Height, 1);
            Assert.Equal(1, FirstVisibleIndex(lb, sv));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Typing_In_A_Kept_Row_Brings_It_Back(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, 100);
            var focused = Focus(lb, 3);

            ScrollBy(sv, 100);
            ScrollBy(sv, 100);
            focused.BringIntoView();
            RunLayout(sv);

            AssertFullyVisible(focused, sv);
            Assert.Same(focused, lb.ContainerFromIndex(3));
            window.Close();
        }

        private class TextBoxTemplate : IVirtualizingDataTemplate
        {
            private readonly System.Func<Control> _build;

            public TextBoxTemplate(System.Func<Control> build) => _build = build;

            public int MaxPoolSizePerKey => 5;

            public int MinPoolSizePerKey => 0;

            public object? GetKey(object? data) => data?.GetType();

            public bool Match(object? data) => data is LazyStackPanelTests_BlackBoxRecycling.Row;

            public Control? Build(object? data) => Build(data, null);

            public Control? Build(object? data, Control? existing) => existing ?? _build();
        }

        private static (Window, ListBox, ScrollViewer) ShowListBox(bool lazy, int count) =>
            ShowListBox(lazy, Enumerable.Range(0, count).Select(x => new LazyStackPanelTests_BlackBoxRecycling.Row(x)));

        private static (Window, ListBox, ScrollViewer) ShowListBox(bool lazy, System.Collections.Generic.IEnumerable<LazyStackPanelTests_BlackBoxRecycling.Row> rows)
        {
            var lb = new ListBox
            {
                ItemTemplate = new FuncDataTemplate<LazyStackPanelTests_BlackBoxRecycling.Row>((r, _) => new Border { Height = r?.Height ?? 10 }),
                ItemContainerTheme = new Avalonia.Styling.ControlTheme(typeof(ListBoxItem))
                {
                    BasedOn = null,
                    Setters =
                    {
                        new Avalonia.Styling.Setter(ListBoxItem.TemplateProperty, new FuncControlTemplate<ListBoxItem>((_, _) =>
                            new Avalonia.Controls.Presenters.ContentPresenter
                            {
                                Name = "PART_ContentPresenter",
                                [~Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] =
                                    new Avalonia.Data.TemplateBinding(ContentControl.ContentProperty),
                                [~Avalonia.Controls.Presenters.ContentPresenter.ContentTemplateProperty] =
                                    new Avalonia.Data.TemplateBinding(ContentControl.ContentTemplateProperty),
                            })),
                        new Avalonia.Styling.Setter(InputElement.FocusableProperty, true),
                    },
                },
            };
            var (window, sv) = Show(lb, new ObservableCollection<LazyStackPanelTests_BlackBoxRecycling.Row>(rows), lazy);
            return (window, lb, sv);
        }

        private static Control Focus(ListBox lb, int index)
        {
            // Layout first: the ListBox scrolls to a new selection in the next layout.
            lb.SelectedIndex = index;
            RunLayout(lb);
            var c = lb.ContainerFromIndex(index)!;
            c.Focus();
            Assert.True(c.IsFocused);
            return c;
        }

        private static void Press(Control target, Key key)
        {
            var source = TopLevel.GetTopLevel(target)!.FocusManager!.GetFocusedElement() as Interactive ?? target;
            source.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = source });
            RunLayout(target);
        }

        private static void ScrollBy(ScrollViewer sv, double delta)
        {
            LazyStackPanelTestTarget.ScrollBy(sv, delta);
            RunLayout(sv);
        }

        private static double Top(Control c, ScrollViewer sv) => c.TranslatePoint(default, sv)!.Value.Y;

        private static int FirstVisibleIndex(ItemsControl ic, ScrollViewer sv) =>
            ic.GetRealizedContainers()
                .Where(c => c.IsVisible && Top(c, sv) + c.Bounds.Height > 0)
                .Select(ic.IndexFromContainer)
                .Min();

        private static void AssertFullyVisible(Control c, ScrollViewer sv)
        {
            var top = Top(c, sv);
            Assert.InRange(top, -0.5, sv.Viewport.Height);
            Assert.InRange(top + c.Bounds.Height, 0, sv.Viewport.Height + 0.5);
        }
    }
}
