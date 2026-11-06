using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;
using Row = Avalonia.Controls.UnitTests.LazyStackPanelTests_BlackBoxRecycling.Row;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Control items, the hosting requirements of the panel, the container order, and scroll requests made during
    /// layout. Where the expected result is "as the VSP", the test runs the same steps on both panels and compares.
    /// 100 x 100 window, rows of 10 px, no cache.
    /// </summary>
    public class LazyStackPanelTests_EdgeCases : LazyStackPanelTestBase
    {
        private static ItemsControl CreateTarget() =>
            new() { ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }) };

        private static ObservableCollection<string> Rows(int count = 100) =>
            new(Enumerable.Range(0, count).Select(i => $"Item {i}"));

        private static double Top(Control c, ScrollViewer sv) => c.TranslatePoint(default, sv)!.Value.Y;

        // Control items scrolled out and back stay hidden children, as with the VSP.
        [Fact]
        public void Control_Items_Scrolled_Out_And_Back_Behave_As_With_The_Vsp()
        {
            var (vsp, lazy) = (ControlItemsScrolledOutAndBack(lazy: false), ControlItemsScrolledOutAndBack(lazy: true));
            Assert.True(vsp == lazy, $"VSP:  {vsp}{Environment.NewLine}Lazy: {lazy}");
        }

        private static string ControlItemsScrolledOutAndBack(bool lazy)
        {
            var ic = new ItemsControl();
            var buttons = Enumerable.Range(0, 100).Select(_ => new Button { Height = 10 }).ToList();
            var (window, sv) = Show(ic, buttons, lazy);
            var first = buttons[0];
            var (loaded, unloaded) = (0, 0);
            first.Loaded += (_, _) => ++loaded;
            first.Unloaded += (_, _) => ++unloaded;

            ScrollBy(sv, 200);
            RunLayout(ic);
            var away = $"away: loaded {loaded}, unloaded {unloaded}, visual parent is the panel {first.GetVisualParent() == ic.ItemsPanelRoot}, " +
                $"logical parent {first.GetLogicalParent()?.GetType().Name}";
            ScrollBy(sv, -200);
            RunLayout(ic);
            var back = $"back: loaded {loaded}, unloaded {unloaded}, visible {first.IsEffectivelyVisible}, " +
                $"logical parent {first.GetLogicalParent()?.GetType().Name}, top {Top(first, sv)}";
            window.Close();
            return away + "; " + back;
        }

        // A hidden Control item that leaves the collection (Remove, Reset) leaves the panel.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_Scrolled_Out_Control_Item_That_Is_Removed_Leaves_The_Panel(bool reset)
        {
            var ic = new ItemsControl();
            var buttons = Enumerable.Range(0, 100).Select(_ => new Button { Height = 10 }).ToList();
            var items = new ResettingCollection<Button>(buttons);
            var (window, sv) = Show(ic, items, lazy: true);
            ScrollBy(sv, 200);
            RunLayout(ic);
            var first = buttons[0];
            Assert.Equal(ic.ItemsPanelRoot, first.GetVisualParent());

            if (reset)
                items.Reset(buttons.Skip(1));
            else
                items.RemoveAt(0);
            RunLayout(ic);

            Assert.Null(first.GetVisualParent());
            Assert.Null(first.GetLogicalParent());
            var other = new StackPanel { Children = { first } };
            Assert.Equal(other, first.GetVisualParent());
            window.Close();
        }

        [Theory(Skip = "SelectingItemsControl does not scroll to a selection that comes back after a Reset " +
            "(VirtualizingStackPanel too).")]
        [InlineData(false)]
        [InlineData(true)]
        public void Selecting_An_Item_Added_Back_After_A_Reset_Scrolls_To_It(bool lazy)
        {
            var rows = Enumerable.Range(0, 100).Select(x => new Row(x)).ToList();
            var items = new ResettingCollection<Row>(rows);
            var lb = CreateListBox();
            var (window, sv) = Show(lb, items, lazy);
            var a = rows[0];
            lb.SelectedItem = a;
            RunLayout(lb);

            items.Reset(rows.Skip(1));
            RunLayout(lb);
            items.Add(a);
            RunLayout(lb);
            lb.SelectedItem = a;
            RunLayout(lb);

            var container = lb.ContainerFromIndex(items.Count - 1);
            Assert.NotNull(container);
            Assert.InRange(Top(container!, sv), -0.5, sv.Viewport.Height - 10 + 0.5);
            window.Close();
        }

        // The anchor follows its item also at the very top: the old row 0 stays at the top and the new row is
        // above it. The VSP shows the new row.
        [Fact]
        public void Insert_At_Index_0_At_The_Very_Top_Keeps_The_Old_Row_0_On_The_Screen()
        {
            var ic = CreateTarget();
            var items = Rows();
            var (window, sv) = Show(ic, items, lazy: true);

            items.Insert(0, "New");
            RunLayout(ic);

            var info = ItemsScroll.Get(ic);
            Assert.Equal(1, info.FirstVisibleIndex);
            Assert.Equal(0, Top(ic.ContainerFromIndex(1)!, sv), 1);
            Assert.True(info.CanScrollBackward);
            window.Close();
        }

        // The panel throws when it is not the panel of an ItemsPresenter directly in a ScrollViewer, and when its
        // ScrollViewer has no height limit (in a StackPanel).
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void Without_A_Limited_ScrollViewer_The_Panel_Throws(bool scrollViewer, bool inStackPanel)
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }),
                ItemsSource = Rows(20),
                ItemsPanel = new FuncTemplate<Panel?>(() => new LazyStackPanel { CacheLength = 0 }),
                Template = new FuncControlTemplate<ItemsControl>((_, ns) =>
                {
                    var presenter = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns);
                    return scrollViewer ? new ScrollViewer { Content = presenter } : presenter;
                }),
            };
            var window = new Window
            {
                Width = 100,
                Height = 100,
                Content = inStackPanel ? new StackPanel { Children = { ic } } : ic,
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                window.Show();
                RunLayout(window);
            });
            Assert.Contains(scrollViewer ? "infinite height" : "direct content of a ScrollViewer", ex.Message);
        }

        // Scrolling up realizes rows in front of the realized ones.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GetRealizedContainers_Is_In_Index_Order_After_Scrolling_Up(bool lazy)
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy);
            ScrollBy(sv, 200);
            RunLayout(ic);
            ScrollBy(sv, -50);
            RunLayout(ic);

            var indexes = ic.GetRealizedContainers().Where(c => c.IsVisible).Select(ic.IndexFromContainer).ToList();
            Assert.Equal(indexes.OrderBy(x => x), indexes);
            window.Close();
        }

        // BringIntoView inside a running layout pass (an EffectiveViewportChanged handler) while a scroll is queued:
        // the queued layout cannot run, the row must still end fully visible.
        [Fact]
        public void BringIntoView_Inside_A_Layout_Pass_With_A_Queued_Scroll_Shows_The_Row()
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy: true);
            var info = ItemsScroll.Get(ic);
            var done = false;
            ic.ContainerFromIndex(3)!.EffectiveViewportChanged += (_, _) =>
            {
                if (done)
                    return;
                done = true;
                info.ScrollBy(-5);
                ic.ContainerFromIndex(10)!.BringIntoView();
            };

            info.ScrollBy(5);
            RunLayout(ic);

            Assert.True(done);
            var row = ic.ContainerFromIndex(10);
            Assert.NotNull(row);
            Assert.InRange(Top(row!, sv), -0.5, sv.Viewport.Height - 10 + 0.5);
            window.Close();
        }

        // A row that scrolls the list while it is measured.
        [Fact]
        public void A_Scroll_Request_From_Inside_The_Panels_Measure_Is_Applied()
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<string>((_, _) => new ScrollingRow { Height = 10 }),
            };
            var (window, _) = Show(ic, Rows(), lazy: true);
            var info = ItemsScroll.Get(ic);
            ScrollingRow.Action = () => info.ScrollBy(10);

            info.ScrollBy(20);
            RunLayout(ic);
            ScrollingRow.Action = null;

            Assert.Equal(3, info.FirstVisibleIndex);
            Assert.Equal(0, info.FirstVisibleOffset, 1);
            window.Close();
        }

        private sealed class ScrollingRow : Border
        {
            [ThreadStatic] public static Action? Action;

            protected override Size MeasureOverride(Size availableSize)
            {
                // Once: the first row measured while the action is set.
                var action = Action;
                Action = null;
                action?.Invoke();
                return base.MeasureOverride(availableSize);
            }
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
                },
            },
        };
    }
}
