using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.PanelHarness;

#nullable enable

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Scrolling, viewport and jumps. Host 200 x 200 unless a test says otherwise, CacheLength 0.5, no layout
    /// rounding. Every layout runs <see cref="PanelHarness.Check"/>. Where the VSP fails by design, its own result is
    /// the expectation.
    /// </summary>
    public class LazyStackPanelTests_ScrollViewportJumps : LazyStackPanelTestBase
    {
        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_ScrollViewportJumps(ITestOutputHelper output) => _output = output;

        // ---------------------------------------------------------------------------------------------------------
        // Scrolling and scroll commands
        // ---------------------------------------------------------------------------------------------------------

        // A ScrollBy of many viewports (a fling) from the top. The panel scrolls exactly through the real rows
        // (periodic heights, so the target row and its offset are known); it measures only the rows on the path and
        // keeps only the rows around the new position.
        [Theory]
        [InlineData(5000)]
        [InlineData(20000)]
        public void ScrollBy_Of_Many_Viewports_Is_Exact_And_Keeps_Only_The_New_Rows(double distance)
        {
            using var h = new PanelHarness(true, Rows(1000, Periodic));
            h.TotalMeasures = 0;

            var used = h.Target!.ScrollBy(new Vector(0, distance), ScrollSource.Programmatic);
            // The rows on the path are measured in this one layout: more than the harness check allows.
            h.Layout(check: false);
            h.Measures = 0;
            h.Check("after the fling");

            var (index, top) = RowAt(h.Items, distance);
            var pathRows = index + 1;
            _output.WriteLine($"used {used.Y}, expected row {index} at {top}, top row {h.TopRow().Id} at " +
                $"{Format(h.ScreenY(h.TopRow()))}, measures {h.TotalMeasures} (rows on the path {pathRows}), " +
                $"realized {h.Realized().Count}, children {h.Panel.Children.Count}");

            Assert.Equal(distance, used.Y);
            Assert.Equal(index, h.TopRow().Id);
            Assert.Equal(top, h.ScreenY(index)!.Value, 3);
            // Rows on the path plus the realized rows around the target.
            Assert.True(h.TotalMeasures <= pathRows + h.Realized().Count + 5,
                $"{h.TotalMeasures} measures for {pathRows} rows on the path");
        }

        // The rows on the path are not kept: the children are the realized rows and a small pool.
        [Theory]
        [InlineData(5000)]
        [InlineData(20000)]
        public void A_Fling_Does_Not_Keep_A_Container_For_Each_Row_On_The_Path(double distance)
        {
            using var h = new PanelHarness(true, Rows(1000, Periodic));

            h.Target!.ScrollBy(new Vector(0, distance), ScrollSource.Programmatic);
            h.Layout(check: false);

            _output.WriteLine($"realized {h.Realized().Count}, children {h.Panel.Children.Count}");
            Assert.True(h.Panel.Children.Count <= 2 * h.Realized().Count + 5,
                $"{h.Panel.Children.Count} children for {h.Realized().Count} realized rows");
        }

        public enum HeightSet
        {
            /// <summary>40/70/100/130/160 px.</summary>
            Periodic,
            /// <summary>5/50/200 px.</summary>
            Extreme,
        }

        private static double HeightOf(HeightSet set, int i) =>
            set == HeightSet.Periodic ? Periodic(i) : i % 3 == 0 ? 5 : i % 3 == 1 ? 50 : 200;

        // Scroll down to the end in steps of 50 px, then back to the top. Each step moves the content by exactly
        // 50 px, the last row ends at the viewport bottom, row 0 at 0, every row is on the screen at least once.
        [Theory]
        [InlineData(false, HeightSet.Periodic)]
        [InlineData(false, HeightSet.Extreme)]
        [InlineData(true, HeightSet.Periodic)]
        [InlineData(true, HeightSet.Extreme)]
        public void Scroll_To_The_End_And_Back_With_Mixed_Heights(bool lazy, HeightSet set)
        {
            using var h = new PanelHarness(lazy, Rows(100, i => HeightOf(set, i)));
            var seen = new HashSet<int>();
            var wrongDown = 0;
            var wrongUp = 0;

            void Record() => seen.UnionWith(h.OnScreen().Select(x => x.Row.Id));

            Record();
            for (var steps = 0; steps < 2000; ++steps)
            {
                var last = h.ScreenY(h.Items.Count - 1);
                if (last is { } y && y + h.Items[^1].Height <= h.ViewportHeight + Tolerance)
                    break;
                var tracked = h.FirstFullyVisible();
                var before = h.ScreenY(tracked)!.Value;
                // The last step stops at the end.
                var step = last is { } l ? Math.Min(50, l + h.Items[^1].Height - h.ViewportHeight) : 50;
                h.ScrollBy(step);
                Record();
                if (h.ScreenY(tracked) is not { } after || Math.Abs(after - (before - step)) > Tolerance)
                    ++wrongDown;
            }

            var lastBottom = h.ScreenY(h.Items.Count - 1) + h.Items[^1].Height;
            var seenDown = seen.Count;

            for (var steps = 0; steps < 2000; ++steps)
            {
                if (h.ScreenY(0) is { } y0 && y0 >= -Tolerance)
                    break;
                var tracked = h.FirstFullyVisible();
                var before = h.ScreenY(tracked)!.Value;
                var step = h.ScreenY(0) is { } t ? Math.Min(50, -t) : 50;
                h.ScrollBy(-step);
                Record();
                if (h.ScreenY(tracked) is not { } after || Math.Abs(after - (before + step)) > Tolerance)
                    ++wrongUp;
            }

            _output.WriteLine($"{h.Name} {set}: last row bottom {Format(lastBottom)} (viewport {h.ViewportHeight}), " +
                $"rows seen down {seenDown}, total {seen.Count}, wrong steps down {wrongDown}, up {wrongUp}, " +
                $"row 0 at {Format(h.ScreenY(0))}, Offset {h.Scroll.Offset.Y:0.#}");

            Assert.Equal(h.ViewportHeight, lastBottom!.Value, 1);
            Assert.Equal(0, h.ScreenY(0)!.Value, 1);
            Assert.Equal(0, h.Scroll.Offset.Y, 1);
            Assert.Equal(h.Items.Count, seen.Count);
            Assert.Equal(0, wrongDown);
            Assert.Equal(0, wrongUp);
        }

        public enum Absolute
        {
            EdgeStart,
            EdgeEnd,
            Offset,
            FarFraction,
        }

        // An absolute command followed by a ScrollBy before one layout: the ScrollBy is
        // added to the absolute target. Uniform 50 px rows, so the absolute target does not depend on the average.
        [Theory]
        [InlineData(Absolute.EdgeStart)]
        [InlineData(Absolute.EdgeEnd)]
        [InlineData(Absolute.Offset)]
        [InlineData(Absolute.FarFraction)]
        public void A_ScrollBy_After_An_Absolute_Command_Is_Added_To_It(Absolute command)
        {
            using var h = new PanelHarness(true, Rows(1000, _ => 50));
            h.ScrollToTop(400);
            var t = h.Target!;

            void Send()
            {
                switch (command)
                {
                    case Absolute.EdgeStart: t.ScrollToEdge(ScrollEdge.Start); break;
                    case Absolute.EdgeEnd: t.ScrollToEdge(ScrollEdge.End); break;
                    case Absolute.Offset: t.ScrollToOffset(new Vector(0, 20025)); break;
                    case Absolute.FarFraction: t.ScrollToFraction(new Vector(0, 0.25)); break;
                }
            }

            // The command alone.
            Send();
            h.Layout();
            var reference = h.TopRow();
            var referenceY = h.ScreenY(reference)!.Value;

            // Back to row 400, then the command and a ScrollBy before one layout.
            h.ScrollToTop(400);
            var delta = command == Absolute.EdgeEnd ? -30 : 30;
            Send();
            t.ScrollBy(new Vector(0, delta), ScrollSource.Programmatic);
            h.Layout();

            _output.WriteLine($"{command}: alone row {reference.Id} at {referenceY:0.##}; with ScrollBy({delta}) " +
                $"row {reference.Id} at {Format(h.ScreenY(reference))}");
            Assert.Equal(referenceY - delta, h.ScreenY(reference)!.Value, 1);
        }

        // The panel does not support snap points: the wheel step is the full ScrollBy, no snap, no exception.
        [Theory]
        [InlineData(SnapPointsType.Mandatory)]
        [InlineData(SnapPointsType.MandatorySingle)]
        public void Snap_Points_Are_Ignored_By_The_Panel(SnapPointsType snap)
        {
            using var h = new PanelHarness(true, Rows(1000, Periodic));
            h.Scroll.VerticalSnapPointsType = snap;
            h.Scroll.VerticalSnapPointsAlignment = SnapPointsAlignment.Near;
            h.Layout();
            h.ScrollBy(25);
            var tracked = h.FirstFullyVisible();
            var before = h.ScreenY(tracked)!.Value;

            var handled = Wheel(h.Scroll, new Vector(0, -1));
            h.Layout();
            h.Scroll.LineDown();
            h.Layout();

            _output.WriteLine($"{snap}: row {tracked.Id} {before} -> {Format(h.ScreenY(tracked))}, handled {handled}");
            Assert.True(handled);
            // Wheel 50 px (ScrollSize) + LineDown 50 px; no snap to a row edge.
            Assert.Equal(before - 100, h.ScreenY(tracked)!.Value, 1);
        }

        // ScrollChanged is raised once per layout with the net deltas, never between the commands.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScrollChanged_Is_Raised_Once_Per_Layout_With_The_Net_Delta(bool lazy)
        {
            using var h = new PanelHarness(lazy, Rows(1000, _ => 50));
            h.ScrollToTop(100);
            var events = new List<Vector>();
            h.Scroll.ScrollChanged += (_, e) => events.Add(e.OffsetDelta);

            LazyStackPanelTestTarget.ScrollBy(h.Scroll, 30);
            LazyStackPanelTestTarget.ScrollBy(h.Scroll, 40);
            var beforeLayout = events.Count;
            h.Layout();

            _output.WriteLine($"{h.Name}: {beforeLayout} events before the layout, after: " +
                string.Join(", ", events.Select(e => e.Y.ToString("0.##"))));
            Assert.Equal(0, beforeLayout);
            Assert.Equal(70, Assert.Single(events).Y, 1);

            // A layout without a scroll raises nothing.
            events.Clear();
            h.ItemsControl.InvalidateMeasure();
            h.Layout();
            Assert.Empty(events);
        }

        // The panel does not scroll horizontally: the horizontal part of a ScrollBy is not used (the presenter can
        // forward it), the vertical part is; a Shift+wheel does not move the rows and is not handled.
        [Fact]
        public void Horizontal_Scroll_Is_Ignored_And_Not_Handled()
        {
            using var h = new PanelHarness(true, Rows(1000, Periodic), horizontal: ScrollBarVisibility.Auto);
            h.ScrollToTop(100);
            var tracked = h.FirstFullyVisible();
            var before = h.ScreenY(tracked)!.Value;

            var used = h.Target!.ScrollBy(new Vector(50, 30), ScrollSource.Programmatic);
            h.Layout();
            Assert.Equal(new Vector(0, 30), used);
            Assert.Equal(before - 30, h.ScreenY(tracked)!.Value, 1);
            Assert.Equal(0, h.Scroll.Offset.X);

            before = h.ScreenY(tracked)!.Value;
            var handled = Wheel(h.Scroll, new Vector(-1, 0));
            var shiftHandled = Wheel(h.Scroll, new Vector(0, -1), KeyModifiers.Shift);
            h.Layout();

            _output.WriteLine($"CanHorizontallyScroll {((ILogicalScrollable)h.Panel).CanHorizontallyScroll}, " +
                $"Extent {h.Scroll.Extent}, Viewport {h.Scroll.Viewport}, handled {handled}, Shift {shiftHandled}, " +
                $"row {tracked.Id} {before} -> {Format(h.ScreenY(tracked))}");
            Assert.False(handled);
            Assert.False(shiftHandled);
            Assert.Equal(before, h.ScreenY(tracked)!.Value, 1);
            Assert.Equal(0, h.Scroll.Offset.X);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Viewport, size and placement
        // ---------------------------------------------------------------------------------------------------------

        // Width 420 → 260 → 420 with rows that wrap. The top row stays the top row; every realized row is
        // measured again (its height fits the new width).
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Width_Change_Keeps_The_Top_Row_And_Remeasures_Every_Row(bool lazy)
        {
            using var h = new PanelHarness(lazy, Rows(500, i => 20 + i % 4 * 20).Select(r => { r.WrapWidth = 420; return r; }),
                width: 420, height: 400);
            h.ScrollToTop(200);
            h.ScrollBy(10);
            var top = h.TopRow();
            var log = $"{h.Name}: top row {top.Id} at {Format(h.ScreenY(top))}";

            foreach (var width in new[] { 260.0, 420.0 })
            {
                h.Host.Width = width;
                h.Layout();
                var stale = h.Realized().Where(c => c.IsVisible &&
                    Math.Abs(c.Bounds.Height - ((HarnessRow)c.DataContext!).DesiredHeight(c.Bounds.Width)) > 0.01).ToList();
                log += $"; width {width}: top row {h.TopRow().Id} at {Format(h.ScreenY(h.TopRow()))}, stale {stale.Count}";
                Assert.Equal(top, h.TopRow());
                Assert.Empty(stale);
            }

            _output.WriteLine(log);
        }

        // The viewport height shrinks (a soft keyboard opens) and grows back. The top row stays; after growing the
        // rows fill the viewport again.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Height_Shrinks_And_Grows_Back(bool lazy)
        {
            using var h = new PanelHarness(lazy, Rows(500, Periodic), height: 400);
            h.ScrollToTop(200);
            h.ScrollBy(20);
            var top = h.TopRow();
            var y = h.ScreenY(top)!.Value;

            h.Host.Height = 150;
            h.Layout();
            var yShrunk = h.ScreenY(top);
            h.Host.Height = 400;
            h.Layout();

            _output.WriteLine($"{h.Name}: row {top.Id} {y} -> {Format(yShrunk)} -> {Format(h.ScreenY(top))}, " +
                $"last on screen {h.OnScreen()[^1].Row.Id}");
            Assert.Equal(y, yShrunk!.Value, 1);
            Assert.Equal(y, h.ScreenY(top)!.Value, 1);
            Assert.True(h.OnScreen().Sum(x => x.Height) >= 400);
        }

        // The viewport collapses to 0 (e.g. a mobile app goes to the background) and comes back. The scroll position
        // is kept.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Viewport_To_Zero_And_Back_Keeps_The_Position(bool lazy)
        {
            using var h = new PanelHarness(lazy, Rows(500, Periodic));
            h.ScrollToTop(200);
            h.ScrollBy(20);
            var top = h.TopRow();
            var y = h.ScreenY(top)!.Value;
            var offset = h.Scroll.Offset.Y;

            h.Host.Height = 0;
            h.Host.Width = 0;
            h.Layout(check: false);
            h.Host.Height = 200;
            h.Host.Width = 200;
            h.Layout();

            _output.WriteLine($"{h.Name}: row {top.Id} {y} -> {Format(h.ScreenY(top))}, Offset {offset} -> {h.Scroll.Offset.Y}");
            Assert.Equal(y, h.ScreenY(top)!.Value, 1);
            Assert.Equal(top, h.TopRow());
        }

        // In a StackPanel (infinite height) the VSP realizes a bounded number of rows and the layout ends. The
        // LazyStackPanel throws there (see LazyStackPanelTests_EdgeCases).
        [Fact]
        public void Vsp_With_An_Infinite_Height_Realizes_A_Bounded_Number_Of_Rows()
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<HarnessRow>((_, _) => new Border { Height = 10 }),
                ItemsSource = Rows(1000, _ => 10).ToList(),
                ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 0 }),
                Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
                {
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns),
                }),
            };
            var window = new Window { Width = 100, Height = 100, Content = new StackPanel { Children = { ic } } };
            window.Show();
            LazyStackPanelTestTarget.RunLayout(window);

            var realized = ic.GetRealizedContainers()!.Count();
            var sv = ic.GetVisualDescendants().OfType<ScrollViewer>().First();
            _output.WriteLine($"VSP: realized {realized}, desired {ic.ItemsPanelRoot!.DesiredSize}, Extent {sv.Extent}, " +
                $"Viewport {sv.Viewport}");
            Assert.True(ic.ItemsPanelRoot!.IsArrangeValid);
            Assert.InRange(realized, 1, 20);
            window.Close();
        }

        // The list is laid out off the screen under a render transform (a drawer that slides in), then shown.
        // Once shown, the rows fill the viewport. The panel uses its ScrollViewer's viewport, so it realizes the
        // rows while off the screen too; the VSP uses the effective viewport.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void List_Under_A_Render_Transform_Off_The_Screen_Then_Slid_In(bool lazy)
        {
            // The VSP realizes 1 row while off the screen: the harness check fails there by design.
            using var h = new PanelHarness(lazy, Rows(500, Periodic), strict: lazy, wrap: host => new Border
            {
                Child = host,
                RenderTransform = new TranslateTransform(2000, 0),
            });
            var transformed = (Border)h.Window.Content!;
            h.Items.Reset(Rows(400, Periodic).ToList());
            h.Layout(check: false);
            var offScreen = h.Realized().Count(c => c.IsVisible);

            transformed.RenderTransform = null;
            h.Strict = true;
            h.Layout();

            _output.WriteLine($"{h.Name}: realized off the screen {offScreen}, after slide-in {h.Realized().Count} " +
                $"(on the screen {h.OnScreen().Count})");
            Assert.True(h.OnScreen().Sum(x => x.Height) >= h.ViewportHeight);
            if (lazy)
                Assert.True(offScreen >= 2, $"{offScreen} rows realized off the screen");
        }

        public enum Nesting
        {
            AncestorMargin,
            OuterScrollViewer,
        }

        // The list inside an ancestor with a margin, and inside another ScrollViewer (with a limited height of its
        // own). No exception, the viewport is the list's height, the rows fill it.
        [Theory]
        [InlineData(false, Nesting.AncestorMargin)]
        [InlineData(false, Nesting.OuterScrollViewer)]
        [InlineData(true, Nesting.AncestorMargin)]
        [InlineData(true, Nesting.OuterScrollViewer)]
        public void Nested_Or_With_An_Ancestor_Margin(bool lazy, Nesting nesting)
        {
            using var h = new PanelHarness(lazy, Rows(500, Periodic), wrap: host => nesting == Nesting.AncestorMargin
                ? new Decorator { Margin = new Thickness(100), Child = host }
                : new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Children = { new Border { Height = 150 }, host, new Border { Height = 700 } },
                    },
                });

            h.ScrollToTop(100);
            h.ScrollBy(500);

            _output.WriteLine($"{h.Name} {nesting}: viewport {h.Scroll.Viewport}, on screen {h.OnScreen().Count}");
            Assert.Equal(200, h.ViewportHeight);
            Assert.True(h.OnScreen().Sum(x => x.Height) >= 200);
        }

        // In the middle of a list of 20 px rows (viewport 200 px), the realized rows reach CacheLength x viewport
        // above and below the viewport (the VSP only in total: after a ScrollIntoView most of its buffer is on one
        // side). A CacheLength change does not measure the panel; it applies at the next measure, here a scroll of
        // more than the buffer.
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 0.5)]
        [InlineData(false, 1)]
        [InlineData(true, 0)]
        [InlineData(true, 0.5)]
        [InlineData(true, 1)]
        public void Buffer_Follows_CacheLength(bool lazy, double cacheLength)
        {
            using var h = new PanelHarness(lazy, Rows(1000, _ => 20), cacheLength: cacheLength);
            h.ScrollToTop(500);
            var (above, below) = Buffer(h);
            var log = $"{h.Name} {cacheLength}: above {above}, below {below}";
            AssertBuffer(lazy, cacheLength, above, below);

            var changed = cacheLength == 1 ? 0 : 1;
            if (lazy)
                ((LazyStackPanel)h.Panel).CacheLength = changed;
            else
                ((VirtualizingStackPanel)h.Panel).CacheLength = changed;
            h.Layout();
            var (above2, below2) = Buffer(h);
            log += $"; CacheLength {changed}, a layout: above {above2}, below {below2}";
            Assert.Equal((above, below), (above2, below2));

            h.ScrollBy(250);
            var (above3, below3) = Buffer(h);
            log += $"; ScrollBy(250): above {above3}, below {below3}";
            _output.WriteLine(log);
            AssertBuffer(lazy, changed, above3, below3);
        }

        private static (int Above, int Below) Buffer(PanelHarness h)
        {
            var screen = h.OnScreen();
            var indexes = h.RealizedIndexes();
            return (h.Items.IndexOf(screen[0].Row) - indexes[0], indexes[^1] - h.Items.IndexOf(screen[^1].Row));
        }

        private static void AssertBuffer(bool lazy, double cacheLength, int above, int below)
        {
            var expected = cacheLength * 200 / 20;
            if (lazy)
            {
                Assert.InRange(above, expected - 1, expected + 1);
                Assert.InRange(below, expected - 1, expected + 1);
            }
            else
                Assert.InRange(above + below, 2 * expected - 1, 2 * expected + 1);
        }

        // Layout rounding on, render scaling 1, 1.25 and 2.625, rows of fractional heights. At the end the last row
        // ends at the viewport bottom; no gaps or overlaps (the harness check, 0.5 px).
        [Theory]
        [InlineData(false, 1)]
        [InlineData(false, 1.25)]
        [InlineData(false, 2.625)]
        [InlineData(true, 1)]
        [InlineData(true, 1.25)]
        [InlineData(true, 2.625)]
        public void Layout_Rounding_At_Render_Scaling(bool lazy, double scaling)
        {
            var impl = MockWindowingPlatform.CreateWindowMock();
            impl.Setup(x => x.RenderScaling).Returns(scaling);
            // The VSP leaves 0.8 px empty below the last row at 1.25: the harness check fails there by design.
            var vspGap = !lazy && scaling == 1.25;
            using var h = new PanelHarness(lazy, Rows(300, i => Periodic(i) / 3.0 + 0.37), windowImpl: impl.Object,
                layoutRounding: true, strict: !vspGap);

            for (var i = 0; i < 20; ++i)
                h.ScrollBy(37.3);
            h.Scroll.ScrollToEnd();
            h.Layout();
            var lastBottom = h.ScreenY(h.Items.Count - 1) + h.ContainerOf(h.Items[^1])!.Bounds.Height;
            var offsetAtEnd = h.Scroll.Offset.Y;
            var maxAtEnd = h.Scroll.Extent.Height - h.ViewportHeight;
            h.Scroll.ScrollToHome();
            h.Layout();

            _output.WriteLine($"{h.Name} x{scaling} (render scaling {h.Window.RenderScaling}): last row bottom " +
                $"{Format(lastBottom)} of {h.ViewportHeight}, Offset {offsetAtEnd:0.###} of {maxAtEnd:0.###}, " +
                $"row 0 at {Format(h.ScreenY(0))}; {string.Join("; ", h.Problems)}");
            Assert.Equal(vspGap, h.Problems.Count > 0);
            Assert.Equal(h.ViewportHeight, lastBottom!.Value, vspGap ? 1 : 1 / scaling);
            Assert.Equal(maxAtEnd, offsetAtEnd, 1 / scaling);
            Assert.Equal(0, h.ScreenY(0)!.Value, 1 / scaling);
        }

        // 1,000,000 rows (Offset about 1e8 px). A jump to the middle measures about the visible rows and the buffer,
        // and the rows are arranged near the viewport.
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Jump_In_A_Million_Rows_Measures_About_The_Visible_Rows_And_The_Buffer(bool lazy, bool periodic)
        {
            using var h = new PanelHarness(lazy, Rows(1_000_000, i => periodic ? Periodic(i) : 100));
            var realizedBefore = h.Realized().Count;
            var log = $"{h.Name} {(periodic ? "periodic" : "uniform")}: first layout {h.TotalMeasures} measures";

            foreach (var jump in new Action[]
            {
                () => h.Scroll.Offset = new Vector(0, h.Scroll.Extent.Height / 2),
                () => h.ItemsControl.ScrollIntoView(900_000),
                () => h.Scroll.ScrollToEnd(),
            })
            {
                h.TotalMeasures = 0;
                jump();
                h.Layout();
                var tops = h.Realized().Where(c => c.IsVisible).Select(c => c.Bounds.Top).ToList();
                log += $"; jump: {h.TotalMeasures} measures, {h.Realized().Count} realized, top row {h.TopRow().Id}, " +
                    $"Offset {h.Scroll.Offset.Y:0}, Bounds.Top {tops.Min():0.#} … {tops.Max():0.#}";

                Assert.True(h.TotalMeasures <= 2 * h.Realized().Count + 5,
                    $"{h.TotalMeasures} measures for {h.Realized().Count} realized rows");
                if (lazy)
                    Assert.True(tops.All(t => Math.Abs(t) < 10 * h.ViewportHeight), "rows not arranged near the viewport");
            }

            _output.WriteLine(log);
            Assert.True(realizedBefore > 0);
        }

        public enum Spacing
        {
            ContainerMargin,
            PresenterMargin,
        }

        // A container margin (ListBoxItem-like) and a margin around the ItemsPresenter (ItemsControl padding).
        // At the top the first row starts after the margins; at the end the last row ends before them; rows are
        // contiguous slots (the harness check includes the margins).
        [Theory]
        [InlineData(false, Spacing.ContainerMargin)]
        [InlineData(false, Spacing.PresenterMargin)]
        [InlineData(true, Spacing.ContainerMargin)]
        [InlineData(true, Spacing.PresenterMargin)]
        public void Margins_Give_Exact_Positions_At_The_Top_And_The_End(bool lazy, Spacing spacing)
        {
            var margin = spacing == Spacing.ContainerMargin ? new Thickness(0, 5, 0, 7) : default;
            var presenter = spacing == Spacing.PresenterMargin ? new Thickness(0, 10, 0, 20) : default;
            using var h = new PanelHarness(lazy, Rows(300, Periodic), presenterMargin: presenter, styles: new Style(
                x => x.OfType<ContentPresenter>()) { Setters = { new Setter(Layoutable.MarginProperty, margin) } });

            var top0 = h.ScreenY(0);
            for (var i = 0; i < 10; ++i)
                h.ScrollBy(97);
            h.Scroll.ScrollToEnd();
            h.Layout();
            var lastBottom = h.ScreenY(h.Items.Count - 1) + h.Items[^1].Height;
            h.Scroll.ScrollToHome();
            h.Layout();

            _output.WriteLine($"{h.Name} {spacing}: row 0 at {Format(top0)}, last row bottom {Format(lastBottom)}, " +
                $"viewport {h.ViewportHeight}, row 0 after ScrollToHome {Format(h.ScreenY(0))}");
            Assert.Equal(presenter.Top + margin.Top, top0!.Value, 1);
            // Positions are relative to the ScrollViewer; the panel's viewport is inside the presenter margin. The
            // VSP's ScrollToEnd with container margins stops 60 px before the end (its Extent is an estimate).
            var expectedBottom = h.Host.Height - presenter.Bottom - margin.Bottom;
            Assert.Equal(!lazy && spacing == Spacing.ContainerMargin ? expectedBottom + 60 : expectedBottom, lastBottom!.Value, 1);
            Assert.Equal(presenter.Top + margin.Top, h.ScreenY(0)!.Value, 1);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Jumps
        // ---------------------------------------------------------------------------------------------------------

        // ScrollIntoView of a row taller than the viewport, from above and from below: its top is aligned with the
        // viewport top.
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ScrollIntoView_Of_A_Row_Taller_Than_The_Viewport_Aligns_Its_Top(bool lazy, bool fromBelow)
        {
            using var h = new PanelHarness(lazy, Rows(300, i => i == 150 ? 500 : Periodic(i)));
            if (fromBelow)
                h.ScrollToTop(250);

            h.ItemsControl.ScrollIntoView(150);
            h.Layout();

            _output.WriteLine($"{h.Name} from {(fromBelow ? "below" : "above")}: row 150 at {Format(h.ScreenY(150))}");
            // The VSP aligns a row above with the bottom (its top is 300 px above the viewport).
            var expected = lazy || !fromBelow ? 0 : 200 - 500;
            Assert.Equal(expected, h.ScreenY(150)!.Value, 1);
        }

        // ScrollIntoView, then ContainerFromItem in the same call: the container is realized synchronously.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScrollIntoView_Realizes_The_Container_Synchronously(bool lazy)
        {
            using var h = new PanelHarness(lazy, Rows(1000, Periodic));

            foreach (var index in new[] { 600, 20, 999, 0 })
            {
                h.ItemsControl.ScrollIntoView(index);
                var container = h.ItemsControl.ContainerFromItem(h.Items[index]);
                Assert.NotNull(container);
                Assert.Same(container, h.ItemsControl.ContainerFromIndex(index));
                Assert.True(container!.IsVisible);
                h.Layout();
                Assert.NotNull(h.ScreenY(index));
            }
        }

        // Several jumps in a row each land; a jump and a ScrollBy before one layout add up; the content is stable
        // afterwards.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Several_Jumps_Each_Land_And_A_Jump_Plus_A_Scroll_Adds_Up(bool lazy)
        {
            using var h = new PanelHarness(lazy, Rows(1000, Periodic));

            foreach (var index in new[] { 200, 600, 100, 900, 450 })
            {
                h.Info.ScrollToItem(index);
                h.Layout();
                Assert.Equal(0, h.ScreenY(index)!.Value, 1);
            }

            h.Info.ScrollToItem(700);
            LazyStackPanelTestTarget.ScrollBy(h.Scroll, 30);
            h.Layout();
            var y = h.ScreenY(700);
            h.ItemsControl.InvalidateMeasure();
            h.Layout();

            _output.WriteLine($"{h.Name}: ScrollToItem(700) + ScrollBy(30): row 700 at {Format(y)}, " +
                $"after another layout {Format(h.ScreenY(700))}");
            Assert.Equal(-30, y!.Value, 1);
            Assert.Equal(-30, h.ScreenY(700)!.Value, 1);
        }

        // Index -1, index = count, and an empty list: no exception, nothing moves.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Out_Of_Range_Index_And_Empty_List_Do_Nothing(bool lazy)
        {
            using var h = new PanelHarness(lazy, Rows(100, Periodic));
            h.ScrollToTop(40);
            h.ScrollBy(10);
            var top = h.TopRow();
            var y = h.ScreenY(top)!.Value;

            foreach (var index in new[] { -1, h.Items.Count, int.MaxValue })
            {
                h.ItemsControl.ScrollIntoView(index);
                h.Info.ScrollToItem(index);
                h.Layout();
                Assert.Equal(y, h.ScreenY(top)!.Value, 1);
            }

            h.Items.Reset(Array.Empty<HarnessRow>());
            h.Layout();
            h.ItemsControl.ScrollIntoView(0);
            h.Info.ScrollToItem(0);
            h.Layout();
            Assert.Empty(h.Realized());
            Assert.Equal(-1, h.Info.FirstVisibleIndex);
        }

        // A jump far down, then scrolling up through rows taller than the estimate (rows 0–899 are 160 px, the
        // rows at the target 30 px). Each step moves the content by exactly the step; row 0 ends at 0.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Jump_Far_Then_Scroll_Up_Through_Rows_Taller_Than_The_Estimate(bool lazy)
        {
            using var h = new PanelHarness(lazy, Rows(1000, i => i < 900 ? 160 : 30));
            h.ScrollToTop(950);
            var steps = 0;
            string? wrong = null;

            while ((h.ScreenY(0) is not { } y0 || y0 < -Tolerance) && steps < 5000)
            {
                // The row cut by the top edge stays on the screen after a step of 150 px up.
                var tracked = h.TopRow();
                var before = h.ScreenY(tracked)!.Value;
                var step = h.ScreenY(0) is { } t ? Math.Min(150, -t) : 150;
                h.ScrollBy(-step);
                ++steps;
                var after = h.ScreenY(tracked);
                if (wrong is null && (after is null || Math.Abs(after.Value - (before + step)) > Tolerance))
                    wrong = $"step {steps}: row {tracked.Id} {before:0.#} -> {Format(after)} (expected {before + step:0.#})";
            }

            _output.WriteLine($"{h.Name}: {steps} steps, first wrong: {wrong ?? "none"}, row 0 at {Format(h.ScreenY(0))}, " +
                $"Offset {h.Scroll.Offset.Y:0.#}");
            Assert.Equal(0, h.ScreenY(0)!.Value, 1);
            Assert.Null(wrong);
            // The VSP shows row 0 at the top with Offset 6400 (its estimate of the rows above, by design).
            Assert.Equal(lazy ? 0 : 6400, h.Scroll.Offset.Y, 1);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>The row at <paramref name="y"/> px from the top of the list, and its top relative to y.</summary>
        private static (int Index, double Top) RowAt(IReadOnlyList<HarnessRow> rows, double y)
        {
            var top = 0.0;
            for (var i = 0; i < rows.Count; ++i)
            {
                if (top + rows[i].Height > y)
                    return (i, top - y);
                top += rows[i].Height;
            }
            return (rows.Count - 1, top - y);
        }

        internal static bool Wheel(ScrollViewer target, Vector delta, KeyModifiers modifiers = KeyModifiers.None)
        {
            var presenter = (ScrollContentPresenter)target.Presenter!;
            var root = (Visual)target.GetVisualRoot()!;
            var e = new PointerWheelEventArgs(
                presenter,
                new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true),
                root,
                default,
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                modifiers,
                delta)
            {
                RoutedEvent = InputElement.PointerWheelChangedEvent
            };
            presenter.RaiseEvent(e);
            return e.Handled;
        }
    }
}
