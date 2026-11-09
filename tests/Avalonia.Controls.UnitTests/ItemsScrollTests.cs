using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// <see cref="IItemsScrollInfo"/> gives the same answers for every panel.
    /// </summary>
    public class ItemsScrollTests : LazyStackPanelTestBase
    {
        public const string Lazy = "LazyStackPanel";
        public const string Vsp = "VirtualizingStackPanel";
        public const string Stack = "StackPanel";

        public static TheoryData<string> Panels => new() { Lazy, Vsp, Stack };

        [Fact]
        public void Get_Returns_One_Object_That_Uses_The_LazyStackPanel()
        {
            var (ic, _, _) = CreateTarget(Lazy);
            var panel = (IItemsScrollInfo)ic.ItemsPanelRoot!;
            var info = ItemsScroll.Get(ic);

            // Not the panel itself, so that it exists before the panel and follows a new panel.
            Assert.Same(info, ItemsScroll.Get(ic));
            Assert.NotSame(panel, info);
            panel.ScrollBy(25);
            ic.UpdateLayout();
            Assert.Equal(panel.FirstVisibleIndex, info.FirstVisibleIndex);
            Assert.Equal(panel.FirstVisibleOffset, info.FirstVisibleOffset);
        }

        [Theory]
        [MemberData(nameof(Panels))]
        public void Visible_Indexes(string panel)
        {
            var (ic, sv, _) = CreateTarget(panel);
            var info = ItemsScroll.Get(ic);

            Assert.Equal(0, info.FirstVisibleIndex);
            Assert.Equal(0, info.FirstVisibleOffset);
            Assert.Equal(4, info.LastVisibleIndex);

            sv.Offset = new Vector(0, 1050);
            RunLayout(sv);

            Assert.Equal(10, info.FirstVisibleIndex);
            Assert.Equal(50, info.FirstVisibleOffset);
            Assert.Equal(15, info.LastVisibleIndex);
        }

        [Theory]
        [MemberData(nameof(Panels))]
        public void Empty_List_Has_No_Visible_Index(string panel)
        {
            var (ic, _, rows) = CreateTarget(panel);
            var info = ItemsScroll.Get(ic);

            rows.Clear();
            RunLayout(ic);

            Assert.Equal(-1, info.FirstVisibleIndex);
            Assert.Equal(-1, info.LastVisibleIndex);
            Assert.False(info.CanScrollBackward);
            Assert.False(info.CanScrollForward);
        }

        [Theory]
        [MemberData(nameof(Panels))]
        public void CanScroll_At_The_Edges(string panel)
        {
            var (ic, sv, _) = CreateTarget(panel);
            var info = ItemsScroll.Get(ic);

            Assert.False(info.CanScrollBackward);
            Assert.True(info.CanScrollForward);
            Assert.Equal(0, info.ScrollFraction);

            info.ScrollToEnd();
            RunLayout(sv);

            Assert.True(info.CanScrollBackward);
            Assert.False(info.CanScrollForward);
            Assert.Equal(99, info.LastVisibleIndex);
            Assert.Equal(1, info.ScrollFraction, 0.001);

            info.ScrollToStart();
            RunLayout(sv);

            Assert.False(info.CanScrollBackward);
            Assert.Equal(0, info.FirstVisibleIndex);
        }

        [Theory]
        [MemberData(nameof(Panels))]
        public void Scrolled_Delta_Is_The_Movement_On_The_Screen(string panel)
        {
            var (ic, sv, _) = CreateTarget(panel);
            var info = ItemsScroll.Get(ic);
            var deltas = Record(info);

            info.ScrollBy(130);
            RunLayout(sv);
            info.ScrollBy(-30);
            RunLayout(sv);

            Assert.Equal([130.0, -30.0], deltas);
            Assert.Equal(1, info.FirstVisibleIndex);
            Assert.Equal(0, info.FirstVisibleOffset);
        }

        [Fact]
        public void Scrolled_Is_Not_Raised_For_An_Insert_Above_Or_A_New_Average()
        {
            var (ic, sv, rows) = CreateTarget(Lazy, count: 1000);
            var info = ItemsScroll.Get(ic);
            info.ScrollToItem(500);
            RunLayout(sv);
            var deltas = Record(info);
            var offset = sv.Offset.Y;

            rows.Insert(0, new Row(-1, 100));
            RunLayout(sv);
            // Rows above the anchor grow: the average changes, the content does not move.
            foreach (var r in rows.Skip(490).Take(10))
                r.Height = 300;
            RunLayout(sv);

            Assert.NotEqual(offset, sv.Offset.Y);
            Assert.Empty(deltas);
            Assert.Equal(501, info.FirstVisibleIndex);
            Assert.Equal(0, info.FirstVisibleOffset);
        }

        [Theory]
        [MemberData(nameof(Panels))]
        public void ScrollToItem_With_Each_Alignment(string panel)
        {
            var (ic, sv, _) = CreateTarget(panel);
            var info = ItemsScroll.Get(ic);

            info.ScrollToItem(60);
            RunLayout(sv);
            Assert.Equal(0, RowTop(ic, sv, 60));

            info.ScrollToItem(30, ItemAlignment.End);
            RunLayout(sv);
            Assert.Equal(400, RowTop(ic, sv, 30));

            info.ScrollToItem(50, ItemAlignment.Center);
            RunLayout(sv);
            Assert.Equal(200, RowTop(ic, sv, 50));

            info.ScrollToItem(70, ItemAlignment.Start, 20);
            RunLayout(sv);
            Assert.Equal(-20, RowTop(ic, sv, 70));
            Assert.Equal(70, info.FirstVisibleIndex);
            Assert.Equal(20, info.FirstVisibleOffset);
        }

        [Fact]
        public void ScrollToItem_Index_Wins_Over_An_Insert_In_The_Same_Frame()
        {
            var (ic, sv, rows) = CreateTarget(Lazy);
            var info = ItemsScroll.Get(ic);
            info.ScrollToItem(40);
            RunLayout(sv);

            info.ScrollToItem(0);
            rows.Insert(0, new Row(-1, 100));
            RunLayout(sv);

            Assert.Equal(0, info.FirstVisibleIndex);
            Assert.Equal(0, RowTop(ic, sv, -1));
        }

        private static List<double> Record(IItemsScrollInfo info)
        {
            var deltas = new List<double>();
            info.Scrolled += (_, e) => deltas.Add(e.Delta);
            return deltas;
        }

        private static (ItemsControl, ScrollViewer, ObservableCollection<Row>) CreateTarget(string panel, int count = 100)
        {
            var rows = new ObservableCollection<Row>(Enumerable.Range(0, count).Select(i => new Row(i, 100)));
            var ic = new ItemsControl
            {
                Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new Avalonia.Data.TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns),
                }),
                ItemsSource = rows,
                ItemsPanel = new FuncTemplate<Panel?>(() => panel switch
                {
                    Lazy => new LazyStackPanel(),
                    Vsp => new VirtualizingStackPanel(),
                    _ => new StackPanel(),
                }),
                ItemTemplate = new FuncDataTemplate<Row>((_, _) =>
                    new Border { [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(Row.Height)) }),
            };
            var window = new Window { Width = 400, Height = 500, Content = ic };
            window.Show();
            RunLayout(window);
            return (ic, ic.GetVisualDescendants().OfType<ScrollViewer>().Single(), rows);
        }

        private static void RunLayout(Control c)
        {
            Dispatcher.UIThread.RunJobs();
            c.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            c.UpdateLayout();
        }

        private static double RowTop(ItemsControl ic, ScrollViewer sv, int index)
        {
            var c = ic.GetRealizedContainers().Single(x => x.IsVisible && x.DataContext is Row r && r.Index == index);
            return c.TranslatePoint(default, sv.Presenter!)!.Value.Y;
        }

        public class Row : INotifyPropertyChanged
        {
            private double _height;

            public Row(int index, double height) => (Index, _height) = (index, height);

            public int Index { get; }

            public double Height
            {
                get => _height;
                set
                {
                    _height = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Height)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
