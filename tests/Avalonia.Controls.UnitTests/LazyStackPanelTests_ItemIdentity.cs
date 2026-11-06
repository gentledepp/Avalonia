using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Items that are not the container's DataContext: controls as items (a container's DataContext is
    /// inherited) and null items. 100 x 100 window with a DataContext, rows of 10 px, no cache.
    /// </summary>
    public class LazyStackPanelTests_ItemIdentity : LazyStackPanelTestBase
    {
        private static ObservableCollection<object?> Buttons(int count) =>
            new(Enumerable.Range(0, count).Select(i => (object?)new Button { Height = 10, Content = i }));

        private static (Window, ScrollViewer) ShowWithDataContext(ItemsControl ic, IEnumerable<object?> items)
        {
            var (window, sv) = Show(ic, items, lazy: true);
            window.DataContext = new object();
            RunLayout(window);
            return (window, sv);
        }

        // Every realized row is a child once, and shows the item at its index.
        private static void AssertRows(ItemsControl ic)
        {
            var panel = (Panel)ic.ItemsPanelRoot!;
            var realized = ic.GetRealizedContainers().ToList();

            Assert.Equal(realized.Count, realized.Distinct().Count());
            Assert.All(realized, c => Assert.Single(panel.Children, x => x == c));
            Assert.All(realized, c =>
            {
                var index = ic.IndexFromContainer(c);
                var item = ic.Items[index];
                Assert.True(ReferenceEquals(c, item) || ReferenceEquals(ContainerContent(c), item),
                    $"row {index} shows the wrong item");
            });
        }

        private static object? ContainerContent(Control c) => c switch
        {
            ContentControl cc => cc.Content,
            Avalonia.Controls.Presenters.ContentPresenter p => p.Content,
            _ => null,
        };

        // A jump keeps the old rows by DataContext, but all control items inherit the window's one: each row must
        // still be a child once and show the item at its index.
        [Fact]
        public void ItemsControl_With_Control_Items_Jump_Into_An_Overlapping_Range()
        {
            var items = Buttons(100);
            var ic = new ItemsControl();
            var (window, sv) = ShowWithDataContext(ic, items);

            sv.Offset = new Vector(0, 5);
            RunLayout(ic);
            AssertRows(ic);

            sv.Offset = new Vector(0, 500);
            RunLayout(ic);
            AssertRows(ic);

            ItemsScroll.Get(ic).ScrollToStart();
            RunLayout(ic);
            AssertRows(ic);
            Assert.Equal(0, ItemsScroll.Get(ic).FirstVisibleIndex);
            window.Close();
        }

        [Fact]
        public void ListBox_With_ListBoxItems_Jump_Into_An_Overlapping_Range()
        {
            var items = new ObservableCollection<object?>(
                Enumerable.Range(0, 100).Select(i => (object?)new ListBoxItem { Height = 10, MinHeight = 0, Padding = default, Content = i }));
            var ic = new ListBox();
            var (window, sv) = ShowWithDataContext(ic, items);

            sv.Offset = new Vector(0, 5);
            RunLayout(ic);
            AssertRows(ic);

            ItemsScroll.Get(ic).ScrollToEnd();
            RunLayout(ic);
            AssertRows(ic);

            ItemsScroll.Get(ic).ScrollToStart();
            RunLayout(ic);
            AssertRows(ic);
            window.Close();
        }

        // Control items all inherit the same DataContext, so it cannot tell the rows apart.
        [Fact]
        public void Scrolled_Delta_With_Control_Items_Is_The_Real_Movement()
        {
            var ic = new ItemsControl();
            var (window, sv) = ShowWithDataContext(ic, Buttons(100));
            var deltas = new List<double>();
            ItemsScroll.Get(ic).Scrolled += (_, e) => deltas.Add(e.Delta);

            ScrollBy(sv, 25);
            RunLayout(ic);

            Assert.Equal(25, deltas.Sum());
            window.Close();
        }

        [Fact]
        public void Scrolled_Delta_With_Null_Items_Is_The_Real_Movement()
        {
            var ic = new ItemsControl { ItemTemplate = new FuncDataTemplate<object?>((_, _) => new Border { Height = 10 }) };
            var (window, sv) = ShowWithDataContext(ic, new ObservableCollection<object?>(Enumerable.Repeat<object?>(null, 100)));
            var deltas = new List<double>();
            ItemsScroll.Get(ic).Scrolled += (_, e) => deltas.Add(e.Delta);

            ScrollBy(sv, 25);
            RunLayout(ic);

            Assert.Equal(25, deltas.Sum());
            window.Close();
        }
    }
}
