using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Conditions that common scroll behaviors check: an infinite scroll that loads more items at the end
    /// (Extent == Offset + Bounds.Height on every property change, or the last item on the screen on ScrollChanged),
    /// a floating button that hides while scrolling down (ScrollChanged.OffsetDelta), and two Offset writes before
    /// one layout.
    /// </summary>
    public class LazyStackPanelTests_Behaviors : LazyStackPanelTestBase
    {
        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_Behaviors(ITestOutputHelper output) => _output = output;

        // Heights in device pixels at a render scaling of 2.625 (Android), so not exact in binary.
        private static double Fractional(int i) => Math.Round((40 + (i * 37) % 121) * 2.625) / 2.625;

        [Theory]
        [InlineData(false, "wheel", 1000)]
        [InlineData(true, "wheel", 1000)]
        [InlineData(false, "ScrollToEnd", 1000)]
        [InlineData(true, "ScrollToEnd", 1000)]
        [InlineData(false, "ScrollIntoView", 1000)]
        [InlineData(true, "ScrollIntoView", 1000)]
        [InlineData(true, "wheel", 12)]
        [InlineData(true, "wheel", 20)]
        [InlineData(true, "wheel", 30)]
        [InlineData(true, "ScrollToEnd", 12)]
        [InlineData(true, "ScrollToEnd", 20)]
        [InlineData(true, "ScrollToEnd", 30)]
        public void Last_Item_Condition_Holds_At_The_End(bool fractional, string how, int count)
        {
            var (window, sv, panel, rows) = CreateTarget(fractional ? Fractional : null, count);

            if (how == "wheel")
            {
                for (var i = 0; i < 10000 && !AtEnd(sv); ++i)
                {
                    sv.LineDown();
                    RunLayout(sv);
                }
            }
            else if (how == "ScrollToEnd")
            {
                sv.ScrollToEnd();
                RunLayout(sv);
            }
            else
            {
                ((ListBox)window.Content!).ScrollIntoView(rows.Count - 1);
                RunLayout(sv);
            }

            var bottom = RowTop(panel, rows.Count - 1) + rows[^1].Height;
            _output.WriteLine($"Offset {sv.Offset.Y:R}, Viewport {sv.Viewport.Height:R}, Bounds {sv.Bounds.Height:R}, Extent {sv.Extent.Height:R}, " +
                $"Extent - (Offset + Bounds) = {sv.Extent.Height - (sv.Offset.Y + sv.Bounds.Height):R}, last row bottom {bottom:R}");

            Assert.Equal(panel.Viewport.Height, bottom, 0.5);
            Assert.True(LastItemVisible(sv));
            _output.WriteLine($"Exact Extent check: {InfiniteScrollCondition(sv)}");

            // Offset and Extent are estimates and may be 1 ulp off at the end. A further step does not move the content.
            for (var i = 0; i < 3; ++i)
            {
                sv.LineDown();
                RunLayout(sv);
                Assert.Equal(panel.Viewport.Height, RowTop(panel, rows.Count - 1) + rows[^1].Height, 0.001);
            }
        }

        [Fact]
        public void Infinite_Scroll_Condition_Is_Not_True_Far_From_The_End_When_Rows_Shrink()
        {
            // 100 rows of 250 px, scrolled to row 85. The realized rows shrink to 80 px, so the average drops and
            // Extent - Viewport goes below the old Offset in one layout. The old Offset must not be clamped to the
            // new maximum, not even for a moment, or the end condition fires.
            var (window, sv, panel, rows) = CreateTarget(_ => 250, count: 100);
            ((ListBox)window.Content!).ScrollIntoView(85);
            RunLayout(sv);
            LazyStackPanelTestTarget.ScrollBy(sv, 1);
            RunLayout(sv);

            var fired = Fired(sv);
            var lastItemFired = 0;
            sv.ScrollChanged += (_, _) => lastItemFired += LastItemVisible(sv) ? 1 : 0;
            var anchor = panel.FirstVisibleIndex;
            var top = RowTop(panel, anchor);

            foreach (var r in rows.Where(r => panel.Children.Any(c => c.IsVisible && c.DataContext == r)))
                r.Height = 80;
            RunLayout(sv);

            var distance = sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height;
            _output.WriteLine($"After: anchor {panel.FirstVisibleIndex}, Offset {sv.Offset.Y}, Extent {sv.Extent.Height}, distance to the end {distance}");
            foreach (var f in fired)
                _output.WriteLine("   fired: " + f);

            Assert.Equal(top, RowTop(panel, anchor));
            Assert.True(distance > sv.Viewport.Height);
            Assert.Empty(fired);
            Assert.Equal(0, lastItemFired);
        }

        [Fact]
        public void No_Load_More_Far_From_The_End_When_Rows_Shrink()
        {
            var (window, sv, panel, rows) = CreateTarget(_ => 250, count: 100);
            ((ListBox)window.Content!).ScrollIntoView(85);
            RunLayout(sv);
            LazyStackPanelTestTarget.ScrollBy(sv, 1);
            RunLayout(sv);

            var loads = 0;
            sv.PropertyChanged += (_, e) =>
            {
                if ((e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.OffsetProperty) &&
                    InfiniteScrollCondition(sv) && loads++ == 0)
                {
                    // Load more: append synchronously, inside the panel's ScrollInvalidated.
                    for (var i = 0; i < 100; ++i)
                        rows.Add(new Row(rows.Count, 250));
                }
            };

            var anchor = panel.FirstVisibleIndex;
            var top = RowTop(panel, anchor);

            foreach (var r in rows.Where(r => panel.Children.Any(c => c.IsVisible && c.DataContext == r)))
                r.Height = 80;
            RunLayout(sv);

            _output.WriteLine($"loads {loads}, rows {rows.Count}, anchor {panel.FirstVisibleIndex}, Offset {sv.Offset.Y}, Extent {sv.Extent.Height}");
            Assert.Equal(0, loads);
            Assert.Equal(100, rows.Count);
            Assert.Equal(top, RowTop(panel, anchor));

            sv.LineDown();
            RunLayout(sv);
            Assert.Equal(top - 50, RowTop(panel, anchor), 0.5);
        }

        [Fact]
        public void Insert_Above_Changes_Offset_And_Extent_Without_Moving_The_Content()
        {
            var (_, sv, panel, rows) = CreateTarget();
            sv.Offset = new Vector(0, 50000);
            RunLayout(sv);

            var events = Record(sv);
            var top = RowTop(panel, 500);

            for (var i = 0; i < 5; ++i)
                rows.Insert(0, new Row(-1, 100));
            RunLayout(sv);

            _output.WriteLine(string.Join("\n", events.Select(e => $"OffsetDelta {e.OffsetDelta.Y}, ExtentDelta {e.ExtentDelta.Y}")));
            Assert.Equal(top, RowTop(panel, 500));
            Assert.Equal(500, events.Sum(e => e.OffsetDelta.Y));
            Assert.All(events.Where(e => e.OffsetDelta.Y != 0), e => Assert.NotEqual(0, e.ExtentDelta.Y));
        }

        [Fact]
        public void Average_Change_Leaks_Into_OffsetDelta_While_Scrolling()
        {
            // Regions: 40 px rows 0..299, 160 px after. Scroll down over the boundary in wheel steps.
            var (window, sv, panel, rows) = CreateTarget(i => i < 300 ? 40 : 160);
            ((ListBox)window.Content!).ScrollIntoView(280);
            RunLayout(sv);

            var events = Record(sv);
            var wrong = 0;
            var withoutExtentDelta = 0;

            for (var i = 0; i < 60; ++i)
            {
                events.Clear();
                sv.LineDown();
                RunLayout(sv);

                var offsetDelta = events.Sum(e => e.OffsetDelta.Y);
                var extentDelta = events.Sum(e => e.ExtentDelta.Y);
                if (Math.Abs(offsetDelta - 50) > 0.5)
                {
                    ++wrong;
                    if (extentDelta == 0)
                        ++withoutExtentDelta;
                    _output.WriteLine($"step {i}: anchor {panel.FirstVisibleIndex}, OffsetDelta {offsetDelta:F1} (content moved 50), ExtentDelta {extentDelta:F1}");
                }
            }

            _output.WriteLine($"{wrong} of 60 steps report a wrong OffsetDelta; {withoutExtentDelta} of them without an ExtentDelta");
            // ItemsScroll.Scrolled reports these steps correctly. The reported Extent follows only changes of more
            // than 1 %, so a ScrollChanged consumer cannot use ExtentDelta to find them.
            Assert.True(wrong > 0);
        }

        [Fact]
        public void Two_Offset_Writes_Before_One_Layout_Jump_To_The_Last_One()
        {
            // Each Offset write is an absolute ScrollToOffset. Two wheel or inertia steps are ScrollBy commands and
            // add up (LazyStackPanelTests_ScrollCommands).
            var (_, sv, panel, _) = CreateTarget();

            sv.Offset = new Vector(0, 100);
            sv.Offset = new Vector(0, 200);
            RunLayout(sv);

            _output.WriteLine($"Anchor {panel.FirstVisibleIndex} + {panel.FirstVisibleOffset}, Offset {panel.Offset.Y}");
            Assert.Equal(200, panel.Offset.Y);
            Assert.Equal(2, panel.FirstVisibleIndex);
        }

        // End condition of an infinite scroll: the last item's container is on the screen.
        private static bool LastItemVisible(ScrollViewer sv) =>
            sv.FindAncestorOfType<ItemsControl>() is { } ic &&
            ic.ContainerFromIndex(ic.ItemCount - 1) is { IsVisible: true } c &&
            c.TranslatePoint(default, sv) is { } p && p.Y < sv.Viewport.Height;

        // End condition of an infinite scroll that compares Extent with Offset + Bounds.Height exactly.
        private static bool InfiniteScrollCondition(ScrollViewer sv) =>
            sv.Offset.Y > double.Epsilon && sv.Extent.Height > 0 &&
            Math.Abs(sv.Extent.Height - (sv.Offset.Y + sv.Bounds.Height)) <= double.Epsilon;

        private static bool AtEnd(ScrollViewer sv) => sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 0.001;

        private static List<string> Fired(ScrollViewer sv)
        {
            var fired = new List<string>();
            sv.PropertyChanged += (_, e) =>
            {
                if ((e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.OffsetProperty ||
                     e.Property == Visual.BoundsProperty) && InfiniteScrollCondition(sv))
                    fired.Add($"{e.Property.Name}: Offset {sv.Offset.Y}, Extent {sv.Extent.Height}");
            };
            return fired;
        }

        private static List<ScrollChangedEventArgs> Record(ScrollViewer sv)
        {
            var events = new List<ScrollChangedEventArgs>();
            sv.ScrollChanged += (_, e) => events.Add(e);
            return events;
        }

        private static (Window, ScrollViewer, LazyStackPanel, ObservableCollection<Row>) CreateTarget(
            Func<int, double>? height = null, int count = 1000)
        {
            var rows = new ObservableCollection<Row>(Enumerable.Range(0, count).Select(i => new Row(i, height?.Invoke(i) ?? 100)));
            var listBox = new ListBox
            {
                ItemsSource = rows,
                ItemsPanel = new FuncTemplate<Panel?>(() => new LazyStackPanel()),
                ItemTemplate = new FuncDataTemplate<Row>((_, _) => new Border
                {
                    UseLayoutRounding = false,
                    [!Layoutable.HeightProperty] = new Binding(nameof(Row.Height)),
                }),
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
                            new Setter(Layoutable.UseLayoutRoundingProperty, false),
                        },
                    },
                },
            };

            window.Show();
            Dispatcher.UIThread.RunJobs();
            var sv = listBox.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var panel = (LazyStackPanel)listBox.ItemsPanelRoot!;
            RunLayout(sv);
            Assert.Equal(500, panel.Viewport.Height);
            return (window, sv, panel, rows);
        }

        private static void RunLayout(Control c)
        {
            Dispatcher.UIThread.RunJobs();
            c.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static double RowTop(LazyStackPanel panel, int index) =>
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
