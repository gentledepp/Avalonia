using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Scroll requests reach the panel as commands, through <see cref="IScrollCommandTarget"/>.
    /// </summary>
    public class LazyStackPanelTests_ScrollCommands : LazyStackPanelTestBase
    {
        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_ScrollCommands(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Panel_Is_A_Command_Target()
        {
            var (_, sv, panel, _) = CreateTarget();

            Assert.Same(panel, ((ScrollContentPresenter)sv.Presenter!).Child is ItemsPresenter p ? p.Panel : null);
            Assert.IsAssignableFrom<IScrollCommandTarget>(panel);
        }

        [Fact]
        public void ScrollBy_Is_Exact_And_Returns_The_Used_Distance()
        {
            var (_, sv, panel, _) = CreateTarget();
            var target = Assert.IsAssignableFrom<IScrollCommandTarget>(panel);

            Assert.Equal(0, target.ScrollBy(new Vector(0, -50), ScrollSource.Programmatic).Y);

            Assert.Equal(120, target.ScrollBy(new Vector(0, 120), ScrollSource.Programmatic).Y);
            RunLayout(sv);
            Assert.Equal(1, panel.FirstVisibleIndex);
            Assert.Equal(20, panel.FirstVisibleOffset);

            // 120 px above the viewport top to row 0.
            Assert.Equal(-120, target.ScrollBy(new Vector(0, -500), ScrollSource.Programmatic).Y);
            RunLayout(sv);
            Assert.Equal(0, panel.FirstVisibleIndex);
            Assert.Equal(0, panel.FirstVisibleOffset);

            sv.ScrollToEnd();
            RunLayout(sv);
            Assert.Equal(0, target.ScrollBy(new Vector(0, 50), ScrollSource.Programmatic).Y);
        }

        [Fact]
        public void Two_LineDown_Before_One_Layout_Add_Up()
        {
            var (_, sv, panel, _) = CreateTarget();

            sv.LineDown();
            sv.LineDown();
            RunLayout(sv);

            Assert.Equal(1, panel.FirstVisibleIndex);
            Assert.Equal(0, panel.FirstVisibleOffset);
        }

        [Fact]
        public void Wheel_Step_Near_The_Top_Has_Its_Full_Length()
        {
            // Row 0 is 2000 px and not realized (CacheLength 0); the realized rows are 10 px, so
            // Offset = avg x 1 = 10 while the real distance to the top is 2000.
            var (window, sv, panel, _) = CreateTarget(i => i == 0 ? 2000 : 10);
            panel.CacheLength = 0;
            ((ListBox)window.Content!).ScrollIntoView(500);
            RunLayout(sv);
            ((ListBox)window.Content!).ScrollIntoView(1);
            RunLayout(sv);
            Assert.Equal(1, panel.FirstVisibleIndex);
            Assert.True(panel.Offset.Y < 50);

            var presenter = (Control)sv.Presenter!;
            presenter.RaiseEvent(new PointerWheelEventArgs(
                presenter,
                new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true),
                window,
                new Point(50, 50),
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None,
                new Vector(0, 1))
            {
                RoutedEvent = InputElement.PointerWheelChangedEvent
            });
            RunLayout(sv);

            Assert.Equal(50, RowTop(panel, 1));
        }

        [Fact]
        public void ScrollToEnd_One_Viewport_From_The_End_Is_Exact()
        {
            // Rows 998 and 999 are 2000 px and not realized; the estimate says 2 x 100 px.
            var (_, sv, panel, rows) = CreateTarget(i => i >= 998 ? 2000 : 100);

            sv.Offset = new Vector(0, 99000);
            RunLayout(sv);
            var max = panel.Extent.Height - panel.Viewport.Height;
            Assert.True(max - panel.Offset.Y <= panel.Viewport.Height);

            sv.ScrollToEnd();
            RunLayout(sv);

            Assert.Equal(panel.Viewport.Height, RowTop(panel, 999) + rows[999].Height);
            Assert.Equal(panel.Extent.Height - panel.Viewport.Height, panel.Offset.Y, 0.001);
        }

        [Fact]
        public void ScrollToHome_One_Viewport_From_The_Top_Is_Exact()
        {
            // Rows 0..2 are 2000 px. With CacheLength 0.2 only row 4 is realized above the anchor.
            var (window, sv, panel, _) = CreateTarget(i => i <= 2 ? 2000 : 100);
            panel.CacheLength = 0.2;
            ((ListBox)window.Content!).ScrollIntoView(30);
            RunLayout(sv);
            ((ListBox)window.Content!).ScrollIntoView(5);
            RunLayout(sv);
            Assert.True(panel.Offset.Y <= panel.Viewport.Height);

            sv.ScrollToHome();
            RunLayout(sv);

            Assert.Equal(0, panel.FirstVisibleIndex);
            Assert.Equal(0, panel.FirstVisibleOffset);
            Assert.Equal(0, panel.Offset.Y);
        }

        [Fact]
        public void ScrollToFraction_Within_One_Viewport_Is_Exact()
        {
            var (_, sv, panel, _) = CreateTarget(i => i % 2 == 0 ? 40 : 160);
            var target = (IScrollCommandTarget)panel;
            sv.Offset = new Vector(0, 50000);
            RunLayout(sv);
            var index = panel.FirstVisibleIndex;
            var top = RowTop(panel, index);
            var max = panel.Extent.Height - panel.Viewport.Height;

            target.ScrollToFraction(new Vector(0, (panel.Offset.Y + 30) / max));
            RunLayout(sv);

            Assert.Equal(top - 30, RowTop(panel, index), 0.001);
        }

        [Fact]
        public void ScrollToFraction_Far_Away_Is_Absolute_And_Stable()
        {
            var (_, sv, panel, _) = CreateTarget(i => i % 2 == 0 ? 40 : 160);
            var target = (IScrollCommandTarget)panel;

            target.ScrollToFraction(new Vector(0, 0.7));
            RunLayout(sv);
            var index = panel.FirstVisibleIndex;
            var top = RowTop(panel, index);
            _output.WriteLine($"Anchor {index} + {panel.FirstVisibleOffset}, Offset {panel.Offset.Y}, fraction {panel.Offset.Y / (panel.Extent.Height - panel.Viewport.Height)}");

            Assert.InRange(index, 600, 800);
            RunLayout(sv);
            Assert.Equal(top, RowTop(panel, index));
        }

        [Fact]
        public void ScrollToFraction_Of_0_And_1_Are_The_Edges()
        {
            var (_, sv, panel, rows) = CreateTarget(i => i >= 998 ? 2000 : 100);
            var target = (IScrollCommandTarget)panel;

            target.ScrollToFraction(new Vector(0, 1));
            RunLayout(sv);
            Assert.Equal(panel.Viewport.Height, RowTop(panel, 999) + rows[999].Height);

            target.ScrollToFraction(new Vector(0, 0));
            RunLayout(sv);
            Assert.Equal(0, panel.FirstVisibleIndex);
            Assert.Equal(0, panel.FirstVisibleOffset);
        }

        [Fact]
        public void Small_Thumb_Drag_Moves_The_Content_In_The_Drag_Direction()
        {
            // Regions: 40 px rows 0..299, 160 px after; the average changes on every step over the boundary.
            var (window, sv, panel, _) = CreateTarget(i => i < 300 ? 40 : 160);
            var scrollBar = sv.GetVisualDescendants().OfType<ScrollBar>().Single(x => x.Orientation == Orientation.Vertical);
            ((ListBox)window.Content!).ScrollIntoView(280);
            RunLayout(sv);

            for (var i = 0; i < 40; ++i)
            {
                var index = panel.FirstVisibleIndex;
                var top = RowTop(panel, index);
                scrollBar.Value += 20;
                ((IScrollCommandTarget)panel).ScrollToFraction(new Vector(0, scrollBar.Value / scrollBar.Maximum));
                RunLayout(sv);
                Assert.True(RowTop(panel, index) < top, $"step {i}: row {index} moved from {top} to {RowTop(panel, index)}");
            }
        }

        [Fact]
        public void ScrollToOffset_Is_Absolute_And_Stable()
        {
            var (_, sv, panel, _) = CreateTarget();

            sv.Offset = new Vector(0, 300);
            RunLayout(sv);
            Assert.Equal(3, panel.FirstVisibleIndex);
            Assert.Equal(0, panel.FirstVisibleOffset);
            Assert.Equal(300, panel.Offset.Y);

            sv.Offset = new Vector(0, 70000);
            RunLayout(sv);
            Assert.Equal(700, panel.FirstVisibleIndex);
            RunLayout(sv);
            Assert.Equal(700, panel.FirstVisibleIndex);
            Assert.Equal(0, RowTop(panel, 700));
        }

        [Fact]
        public void Extent_Below_The_Old_Offset_Does_Not_Move_The_Content()
        {
            var (_, sv, panel, rows) = CreateTarget();
            sv.Offset = new Vector(0, 90000);
            RunLayout(sv);
            Assert.Equal(900, panel.FirstVisibleIndex);

            // All realized rows shrink to 10 px: avg 100 -> 10, so Offset 90000 -> 9000 and
            // Extent - Viewport 99500 -> 9500 in one layout.
            foreach (var r in rows.Skip(800).Take(200))
                r.Height = 10;
            RunLayout(sv);

            Assert.Equal(900, panel.FirstVisibleIndex);
            Assert.Equal(0, panel.FirstVisibleOffset);

            var presenter = sv.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
            var scrollBar = sv.GetVisualDescendants().OfType<ScrollBar>().Single(x => x.Orientation == Orientation.Vertical);
            Assert.Equal(panel.Offset.Y, presenter.Offset.Y);
            Assert.Equal(panel.Offset.Y, sv.Offset.Y);
            Assert.Equal(panel.Offset.Y, scrollBar.Value);
        }

        [Fact]
        public void VirtualizingStackPanel_Keeps_The_Offset_Path()
        {
            var (_, sv, _, _) = CreateTarget(lazy: false);
            var panel = ((ListBox)sv.FindAncestorOfType<ListBox>()!).ItemsPanelRoot!;

            Assert.IsNotAssignableFrom<IScrollCommandTarget>(panel);
            sv.Offset = new Vector(0, 300);
            RunLayout(sv);
            Assert.Equal(300, sv.Offset.Y);
            var row = panel.Children.Single(x => x.DataContext is Row { Index: 3 });
            Assert.Equal(0, row.TranslatePoint(default, sv.Presenter!)!.Value.Y);
        }

        private static (Window, ScrollViewer, LazyStackPanel, ObservableCollection<Row>) CreateTarget(
            System.Func<int, double>? height = null, bool lazy = true)
        {
            var rows = new ObservableCollection<Row>(Enumerable.Range(0, 1000).Select(i => new Row(i, height?.Invoke(i) ?? 100)));
            var listBox = new ListBox
            {
                ItemsSource = rows,
                ItemsPanel = new FuncTemplate<Panel?>(() => lazy ? new LazyStackPanel() : new VirtualizingStackPanel()),
                ItemTemplate = new FuncDataTemplate<Row>((_, _) =>
                    new Border { [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(Row.Height)) }),
                BorderThickness = default,
                Padding = default,
            };
            var window = new Window
            {
                Width = 400,
                Height = 500,
                Content = listBox,
                Styles =
                {
                    new Style(x => x.OfType<ListBoxItem>())
                    {
                        Setters =
                        {
                            new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                            new Setter(Layoutable.MinHeightProperty, 0.0),
                        },
                    },
                },
            };

            window.Show();
            Dispatcher.UIThread.RunJobs();
            var sv = listBox.GetVisualDescendants().OfType<ScrollViewer>().Single();
            RunLayout(sv);
            return (window, sv, listBox.ItemsPanelRoot as LazyStackPanel ?? null!, rows);
        }

        private static void RunLayout(Control c)
        {
            Dispatcher.UIThread.RunJobs();
            c.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static double RowTop(Panel panel, int index) =>
            panel.Children.Single(x => x.IsVisible && x.DataContext is Row r && r.Index == index).Bounds.Top;

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
