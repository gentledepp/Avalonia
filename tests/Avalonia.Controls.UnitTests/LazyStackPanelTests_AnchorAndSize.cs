using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

#nullable enable

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Anchor and size changes, against both panels. Window 600 x 600, CacheLength 0.5, no layout rounding (so the
    /// fractional heights stay fractional). Every layout runs the consistency checks of <see cref="Harness.Check"/>.
    /// Where the VSP fails by design, its own result is the expectation.
    /// </summary>
    public class LazyStackPanelTests_AnchorAndSize : LazyStackPanelTestBase
    {
        private const double Tolerance = 0.5;
        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_AnchorAndSize(ITestOutputHelper output) => _output = output;

        public enum Heights
        {
            /// <summary>Every row 100 px: the estimate is exact.</summary>
            Uniform,
            /// <summary>40/70/100/130/160 px repeating: the average is exact over 5 rows.</summary>
            Periodic,
            /// <summary>Rows 0-299 are 40 px, the rest 160 px: the realized-window average is wrong above.</summary>
            Regions,
            /// <summary>Periodic plus 1 device pixel at render scaling 2.625: heights that are not exact in binary.</summary>
            Fractional,
        }

        public enum Change
        {
            InsertFarAbove,
            RemoveFarAbove,
            MoveFarAbove,
            ReplaceFarAbove,
            InsertFarBelow,
            InsertInBufferAbove,
            RemoveBufferRows,
            ResetSameItems,
            ResetPlusInsertFarAbove,
        }

        public static IEnumerable<object[]> CollectionChangeCases() =>
            from lazy in new[] { false, true }
            from c in Enum.GetValues<Change>()
            from h in Enum.GetValues<Heights>()
            select new object[] { lazy, c, h };

        // Scrolled to row 400 + 30 px; a change far above, far below, in the buffer, or a
        // Reset. The first fully visible row stays.
        [Theory]
        [MemberData(nameof(CollectionChangeCases))]
        public void Collection_Change_Keeps_First_Visible_Row_In_Place(bool lazy, Change change, Heights heights)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            var tracked = h.FirstFullyVisible();
            var before = h.ScreenY(tracked)!.Value;
            var firstRealized = h.FirstRealizedIndex;
            var offsetBefore = h.Scroll.Offset.Y;

            switch (change)
            {
                case Change.InsertFarAbove:
                    for (var i = 0; i < 5; ++i)
                        h.Items.Insert(100, new Row(10000 + i, 70));
                    break;
                case Change.RemoveFarAbove:
                    for (var i = 0; i < 5; ++i)
                        h.Items.RemoveAt(100);
                    break;
                case Change.MoveFarAbove:
                    h.Items.Move(100, 150);
                    break;
                case Change.ReplaceFarAbove:
                    h.Items[100] = new Row(10000, h.Items[100].Height + 50);
                    break;
                case Change.InsertFarBelow:
                    for (var i = 0; i < 5; ++i)
                        h.Items.Insert(800, new Row(10000 + i, 70));
                    break;
                case Change.InsertInBufferAbove:
                    for (var i = 0; i < 5; ++i)
                        h.Items.Insert(firstRealized + 1, new Row(10000 + i, 70));
                    break;
                case Change.RemoveBufferRows:
                    var count = h.Items.IndexOf(tracked) - firstRealized - 1;
                    for (var i = 0; i < count; ++i)
                        h.Items.RemoveAt(firstRealized);
                    break;
                case Change.ResetSameItems:
                    h.Items.Reset(h.Items.ToList());
                    break;
                case Change.ResetPlusInsertFarAbove:
                    var list = h.Items.ToList();
                    list.InsertRange(100, Enumerable.Range(0, 30).Select(i => new Row(10000 + i, 70)));
                    h.Items.Reset(list);
                    break;
            }

            var expected = lazy ? Outcome.Kept : Expectations.VspCollectionChange(change, heights);
            h.Strict = expected != Outcome.Gap;
            h.Layout();
            var after = h.ScreenY(tracked);
            _output.WriteLine($"{h.Name} {change}/{heights}: row {tracked.Id} {before:0.##} -> {Format(after)}, " +
                $"Offset {offsetBefore:0.#} -> {h.Scroll.Offset.Y:0.#}, realized before from {firstRealized}; " +
                $"{string.Join("; ", h.Problems)}");

            if (expected == Outcome.Gap)
                Assert.NotEmpty(h.Problems);
            else
                AssertKept(h, tracked, before, after, expected == Outcome.Kept);
        }

        public enum Outcome
        {
            /// <summary>The tracked row stays.</summary>
            Kept,
            /// <summary>The tracked row moves (or leaves the screen); the harness checks pass.</summary>
            Moved,
            /// <summary>A harness check fails (a gap in the viewport).</summary>
            Gap,
        }

        // The jump of the VSP on an insert at 0 depends on the error of its estimate at that depth.
        [Theory]
        [InlineData(false, Heights.Uniform, 20)]
        [InlineData(false, Heights.Uniform, 400)]
        [InlineData(false, Heights.Periodic, 20)]
        [InlineData(false, Heights.Periodic, 400)]
        [InlineData(false, Heights.Periodic, 900)]
        [InlineData(false, Heights.Regions, 400)]
        [InlineData(false, Heights.Regions, 900)]
        [InlineData(true, Heights.Uniform, 20)]
        [InlineData(true, Heights.Uniform, 400)]
        [InlineData(true, Heights.Periodic, 20)]
        [InlineData(true, Heights.Periodic, 400)]
        [InlineData(true, Heights.Periodic, 900)]
        [InlineData(true, Heights.Regions, 400)]
        [InlineData(true, Heights.Regions, 900)]
        public void Insert_Above_Jump_Depends_On_Depth(bool lazy, Heights heights, int scrollTo)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));

            h.ScrollToTop(scrollTo);
            h.ScrollBy(30);
            var tracked = h.FirstFullyVisible();
            var before = h.ScreenY(tracked)!.Value;

            // One row with exactly the average height of the rows above, at index 0.
            h.Items.Insert(0, new Row(10000, h.Items.Take(scrollTo).Average(x => x.Height)));
            h.Layout();

            var after = h.ScreenY(tracked);
            _output.WriteLine($"{h.Name} {heights} @ {scrollTo}: row {tracked.Id} {before:0.##} -> {Format(after)}");
            // The VSP moves the content at every depth and height set (+1 row, or off the screen).
            AssertKept(h, tracked, before, after, expectKept: lazy);
        }

        // A buffer row above the anchor (relative -2) or the anchor row itself (0, 40 px, 30 px scrolled out) changes
        // size. A row above changes: nothing on the screen moves. The anchor row changes: its top stays and the rows
        // below move (a shrink below the 30 px: the next row becomes the anchor with the rest).
        [Theory]
        [InlineData(false, -2, 200)]
        [InlineData(false, -2, -30)]
        [InlineData(false, 0, 200)]
        [InlineData(false, 0, -20)]
        [InlineData(true, -2, 200)]
        [InlineData(true, -2, -30)]
        [InlineData(true, 0, 200)]
        [InlineData(true, 0, -20)]
        public void Realized_Row_Size_Change_Keeps_Content(bool lazy, int relative, double delta)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            var anchor = h.Items[400];
            Assert.Equal(-30, h.ScreenY(anchor)!.Value, 1);
            var changing = h.Items[400 + relative];
            var tracked = h.Items[401];
            var before = h.ScreenY(tracked)!.Value;
            var expected = before + (relative == 0 ? delta : 0);

            changing.Height += delta;
            h.Layout();

            var after = h.ScreenY(tracked);
            _output.WriteLine($"{h.Name} row {changing.Id} {delta:+0;-0}: row {tracked.Id} {before:0.#} -> {Format(after)}, " +
                $"spec {expected:0.#}, row 400 at {Format(h.ScreenY(anchor))}");

            // The VSP's presenter keeps the next row (the anchor candidate nearest to the top), also when the first
            // (partly) visible row changes.
            var vspExpected = before;
            Assert.True(after is { } y && Math.Abs(y - (lazy ? expected : vspExpected)) <= Tolerance,
                $"{h.Name}: expected {(lazy ? expected : vspExpected)}, was {Format(after)}");
        }

        // The anchor row grows twice in one layout pass, the second time from its own SizeChanged.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Two_Stage_Growth_Of_First_Visible_Row_Keeps_Its_Top(bool lazy)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            var row = h.Items[400];
            var topBefore = h.ScreenY(row)!.Value;
            GrowTwice(h, row);

            row.Height += 100;
            h.Layout();
            h.Layout();

            var top = h.ScreenY(row);
            _output.WriteLine($"{h.Name} row 400 top {topBefore:0.#} -> {Format(top)}, height {row.Height}");
            Assert.Equal(360, row.Height);
            // The VSP keeps the next row instead, so the grown row's top moves up by the growth (320 px).
            var expected = lazy ? topBefore : topBefore - 320;
            Assert.True(top is { } y && Math.Abs(y - expected) <= Tolerance, $"{h.Name}: top moved from {topBefore} to {Format(top)}");
        }

        // At the end the last row grows, ScrollToEnd, then it shrinks back: no empty space below it.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Last_Row_Shrinks_At_End_Leaves_No_Empty_Space(bool lazy)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000)) { Strict = lazy };
            var last = h.Items[^1];
            var vh = h.ViewportHeight;

            h.ScrollToEnd();
            h.ScrollToEnd();
            var bottom0 = h.ScreenY(last) + last.Height;
            last.Height += 300;
            h.Layout();
            h.ScrollToEnd();
            var bottom1 = h.ScreenY(last) + last.Height;
            last.Height -= 300;
            h.Layout();
            h.Layout();
            var bottom2 = h.ScreenY(last) + last.Height;

            _output.WriteLine($"{h.Name} last row bottom: {Format(bottom0)}, after grow + ScrollToEnd {Format(bottom1)}, " +
                $"after shrink {Format(bottom2)} (Offset {h.Scroll.Offset.Y:0.#}, Extent {h.Scroll.Extent.Height:0.#}); " +
                string.Join("; ", h.Problems));
            Assert.True(bottom0 is { } b0 && Math.Abs(b0 - vh) <= Tolerance, $"after the first ScrollToEnd: {Format(bottom0)}");
            Assert.True(bottom1 is { } b1 && Math.Abs(b1 - vh) <= Tolerance, $"after grow + ScrollToEnd: {Format(bottom1)}");
            // The VSP leaves the 300 px of the shrink empty below the last row.
            var expected = lazy ? vh : vh - 300;
            Assert.True(bottom2 is { } b2 && Math.Abs(b2 - expected) <= Tolerance, $"after the shrink: {Format(bottom2)}");
            Assert.Equal(lazy, h.Problems.Count == 0);
        }

        // The estimate above the viewport is too large. Scrolling up in steps of at most 300 px moves
        // the content by exactly the step, until row 0 is at 0.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Scrolling_Up_With_Too_Large_Estimate_Reaches_Every_Row(bool lazy)
        {
            using var h = new Harness(lazy, Enumerable.Range(0, 1000).Select(i => new Row(i, i < 900 ? 30 : 300)));

            h.ScrollToTop(950);
            var steps = 0;
            string? wrong = null;

            while (h.Scroll.Offset.Y > 0 && steps < 5000)
            {
                var tracked = h.FirstFullyVisible();
                var before = h.ScreenY(tracked)!.Value;
                var step = Math.Min(h.Scroll.Offset.Y, 300);
                h.ScrollBy(-step);
                ++steps;
                var after = h.ScreenY(tracked);

                if (wrong is null && (after is null || Math.Abs(after.Value - (before + step)) > Tolerance))
                    wrong = $"step {steps}: row {tracked.Id} {before:0.#} -> {Format(after)} (expected {before + step:0.#}), " +
                        $"Offset now {h.Scroll.Offset.Y:0.#}";
            }

            _output.WriteLine($"{h.Name}: {steps} steps, first wrong: {wrong ?? "none"}; first visible row {h.FirstFullyVisible().Id}");
            Assert.Equal(0, h.FirstFullyVisible().Id);
            Assert.Equal(0, h.ScreenY(h.Items[0])!.Value, 1);

            if (lazy)
                Assert.Null(wrong);
            else
                Assert.NotNull(wrong); // By design, the VSP's estimate moves the content during the steps.
        }

        // A row far above (not realized) gets taller. Nothing moves now; scrolling up later shows it with its real
        // size and every step moves the content by exactly the step.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Row_Far_Above_Grows_While_Not_Realized_Then_Scrolling_Up_Shows_It_Without_A_Jump(bool lazy)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));

            h.ScrollToTop(30);
            h.ScrollBy(30);
            var tracked = h.FirstFullyVisible();
            var before = h.ScreenY(tracked)!.Value;
            var far = h.Items[10];
            Assert.Null(h.ScreenY(far));

            far.Height = 400;
            h.Layout();
            AssertKept(h, tracked, before, h.ScreenY(tracked), true);

            string? wrong = null;
            var steps = 0;

            while (h.ScreenY(far) is null && steps < 100)
            {
                var t = h.FirstFullyVisible();
                var y = h.ScreenY(t)!.Value;
                h.ScrollBy(-50);
                ++steps;
                var y2 = h.ScreenY(t);
                if (wrong is null && (y2 is null || Math.Abs(y2.Value - (y + 50)) > Tolerance))
                    wrong = $"step {steps}: row {t.Id} {y:0.#} -> {Format(y2)}";
            }

            var container = h.ContainerOf(far)!;
            _output.WriteLine($"{h.Name}: {steps} steps, first wrong {wrong ?? "none"}, row 10 height {container.Bounds.Height}");
            Assert.Null(wrong);
            Assert.Equal(400, container.Bounds.Height, 1);
        }

        // The anchor row (100 px, 30 px scrolled out) shrinks to 20 px. The next row becomes the anchor
        // with the rest of the offset (10 px); the content below moves up by the shrink.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Anchor_Row_Shrinks_Below_Its_Offset_Next_Row_Becomes_The_Anchor(bool lazy)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            var next = h.Items[401];
            Assert.Equal(70, h.ScreenY(next)!.Value, 1);

            h.Items[400].Height = 20;
            h.Layout();

            var info = h.Info;
            _output.WriteLine($"{h.Name}: row 401 at {Format(h.ScreenY(next))}, FirstVisibleIndex {info.FirstVisibleIndex}, " +
                $"FirstVisibleOffset {info.FirstVisibleOffset:0.#}");
            Assert.Equal(-10, h.ScreenY(next)!.Value, 1);
            Assert.Equal(401, info.FirstVisibleIndex);
            Assert.Equal(10, info.FirstVisibleOffset, 1);
        }

        // Two-stage growth (+100, then +220 from its own SizeChanged) of a buffer row above: nothing on the
        // screen moves; of a row below the anchor on the screen: the rows below it move once, by the sum.
        [Theory]
        [InlineData(false, -2)]
        [InlineData(false, 2)]
        [InlineData(true, -2)]
        [InlineData(true, 2)]
        public void Two_Stage_Growth_Of_A_Row_Above_Or_Below_The_Anchor(bool lazy, int relative)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            var row = h.Items[400 + relative];
            var above = h.Items[401];
            var below = h.Items[404];
            var aboveBefore = h.ScreenY(above)!.Value;
            var belowBefore = h.ScreenY(below)!.Value;
            var height = row.Height;
            GrowTwice(h, row);

            row.Height += 100;
            h.Layout();
            h.Layout();

            var aboveAfter = h.ScreenY(above);
            var belowAfter = h.ScreenY(below);
            _output.WriteLine($"{h.Name} row {row.Id}: row 401 {aboveBefore:0.#} -> {Format(aboveAfter)}, " +
                $"row 404 {belowBefore:0.#} -> {Format(belowAfter)}");
            Assert.Equal(height + 320, row.Height);
            Assert.Equal(aboveBefore, aboveAfter!.Value, 1);
            Assert.Equal(belowBefore + (relative > 0 ? 320 : 0), belowAfter!.Value, 1);
        }

        [Theory]
        [InlineData(false, 100)]
        [InlineData(false, -30)]
        [InlineData(true, 100)]
        [InlineData(true, -30)]
        public void Row_Below_The_Anchor_Changes_Only_Rows_Below_Move(bool lazy, double delta)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            var positions = Enumerable.Range(400, 6).ToDictionary(i => i, i => h.ScreenY(h.Items[i])!.Value);

            h.Items[402].Height += delta;
            h.Layout();

            foreach (var (i, y) in positions)
            {
                var expected = y + (i > 402 ? delta : 0);
                Assert.True(Math.Abs(h.ScreenY(h.Items[i])!.Value - expected) <= Tolerance,
                    $"{h.Name}: row {i} at {h.ScreenY(h.Items[i])}, expected {expected}");
            }
        }

        // Rows that load late (pictures): each row grows by 80 px one layout after it is realized, while the list
        // scrolls down and up in steps of 50 px. No step and no late growth moves the first fully visible row except
        // by the step; at the end the Extent is stable.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Rows_That_Grow_Late_During_A_Scroll_Do_Not_Move_The_Content(bool lazy)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            foreach (var r in h.RealizedRows())
                r.Loaded = true;

            var wrong = new List<string>();

            void Step(double delta)
            {
                var tracked = h.FirstFullyVisible();
                var y = h.ScreenY(tracked)!.Value;
                h.ScrollBy(delta);
                var y1 = h.ScreenY(tracked);
                if (y1 is null || Math.Abs(y1.Value - (y - delta)) > Tolerance)
                    wrong.Add($"scroll {delta}: row {tracked.Id} {y:0.#} -> {Format(y1)}");

                // The late growth of the rows that were realized by this step.
                tracked = h.FirstFullyVisible();
                y = h.ScreenY(tracked)!.Value;
                var grown = 0;
                foreach (var r in h.RealizedRows().Where(r => !r.Loaded).ToList())
                {
                    r.Loaded = true;
                    r.Height += 80;
                    ++grown;
                }

                if (grown == 0)
                    return;

                h.Layout();
                var y2 = h.ScreenY(tracked);
                if (y2 is null || Math.Abs(y2.Value - y) > Tolerance)
                    wrong.Add($"growth of {grown} rows after scroll {delta}: row {tracked.Id} {y:0.#} -> {Format(y2)}");
            }

            for (var i = 0; i < 40; ++i)
                Step(50);
            for (var i = 0; i < 80; ++i)
                Step(-50);

            h.Layout();
            var extent = h.Scroll.Extent.Height;
            h.Layout();
            _output.WriteLine($"{h.Name}: {wrong.Count} wrong steps{(wrong.Count > 0 ? ", first: " + wrong[0] : "")}; Extent {extent:0.#}");
            Assert.Equal(extent, h.Scroll.Extent.Height);

            if (lazy)
                Assert.Empty(wrong);
            else
                Assert.NotEmpty(wrong); // The VSP moves the content when buffer rows above grow while scrolling up.
        }

        // A buffer row above grows by 0.3 px, 100 times. Each change is compensated; no drift.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Sub_Pixel_Resize_Of_A_Row_Above_Does_Not_Drift(bool lazy)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            var tracked = h.Items[401];
            var before = h.ScreenY(tracked)!.Value;
            var row = h.Items[398];
            var maxMove = 0.0;

            for (var i = 0; i < 100; ++i)
            {
                row.Height += 0.3;
                h.Layout();
                maxMove = Math.Max(maxMove, Math.Abs(h.ScreenY(tracked)!.Value - before));
            }

            _output.WriteLine($"{h.Name}: row 401 {before:0.###} -> {h.ScreenY(tracked):0.###}, max move {maxMove:0.###}, " +
                $"row 398 {h.ContainerOf(row)?.Bounds.Height:0.###}");
            Assert.Equal(130, h.ContainerOf(row)!.Bounds.Height, 1);
            Assert.True(maxMove <= 0.01, $"{h.Name}: moved by up to {maxMove}");
        }

        // A row of 2000 px (more than 3 viewports) is the anchor; scrolling through it in steps of 100 px reaches
        // every pixel, and FirstVisibleOffset grows past the viewport height.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Row_Taller_Than_The_Viewport_Can_Be_Scrolled_Through(bool lazy)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));
            var tall = h.Items[400];
            tall.Height = 2000;

            h.ScrollToTop(400);
            Assert.Equal(0, h.ScreenY(tall)!.Value, 1);
            var offsets = new List<double>();

            for (var i = 1; i <= 19; ++i)
            {
                h.ScrollBy(100);
                Assert.True(Math.Abs(h.ScreenY(tall)!.Value + 100 * i) <= Tolerance,
                    $"{h.Name} step {i}: row 400 at {h.ScreenY(tall)}, expected {-100 * i}");
                offsets.Add(h.Info.FirstVisibleOffset);
                Assert.Equal(400, h.Info.FirstVisibleIndex);
            }

            h.ScrollBy(100);
            _output.WriteLine($"{h.Name}: FirstVisibleOffset {string.Join(" ", offsets)}; then index {h.Info.FirstVisibleIndex}");
            Assert.Equal(1900, offsets[^1], 1);
            Assert.Equal(401, h.Info.FirstVisibleIndex);
            Assert.Equal(0, h.ScreenY(h.Items[401])!.Value, 1);
        }

        public enum Zero
        {
            /// <summary>The template measures 0 px.</summary>
            Height,
            /// <summary>The container is hidden (<c>IsVisible=false</c> bound to the item).</summary>
            Invisible,
        }

        // Rows of height 0 at the anchor and around it. Scrolling through them moves the content by exactly the
        // step; the anchor is never a row of height 0 while a row with height follows; the anchor row itself
        // becoming 0 makes the next row the anchor at the same place.
        [Theory]
        [InlineData(false, Zero.Height)]
        [InlineData(false, Zero.Invisible)]
        [InlineData(true, Zero.Height)]
        [InlineData(true, Zero.Invisible)]
        public void Rows_Of_Height_Zero_Around_The_Anchor(bool lazy, Zero zero)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));

            for (var i = 395; i <= 405; i += i % 2 == 0 ? 1 : 2)
                MakeZero(h.Items[i], zero);

            h.ScrollToTop(390);
            var wrong = new List<string>();

            for (var i = 0; i < 40; ++i)
            {
                var tracked = h.FirstFullyVisible();
                var y = h.ScreenY(tracked)!.Value;
                h.ScrollBy(30);
                var y1 = h.ScreenY(tracked);
                if (y1 is null || Math.Abs(y1.Value - (y - 30)) > Tolerance)
                    wrong.Add($"step {i}: row {tracked.Id} {y:0.#} -> {Format(y1)}");
                var anchor = h.Info.FirstVisibleIndex;
                if (lazy && h.Items[anchor].DesiredHeight() == 0)
                    wrong.Add($"step {i}: anchor {anchor} has height 0");
            }

            // The anchor row itself becomes 0.
            h.ScrollToTop(420);
            var next = h.Items[421];
            var nextY = h.ScreenY(next)!.Value;
            MakeZero(h.Items[420], zero);
            h.Layout();

            _output.WriteLine($"{h.Name} {zero}: {wrong.Count} wrong{(wrong.Count > 0 ? ", first: " + wrong[0] : "")}; " +
                $"row 421 {nextY} -> {Format(h.ScreenY(next))}, FirstVisibleIndex {h.Info.FirstVisibleIndex}, " +
                $"Extent {h.Scroll.Extent.Height}");
            Assert.Empty(wrong);

            if (lazy)
            {
                Assert.Equal(0, h.ScreenY(next)!.Value, 1);
                Assert.Equal(421, h.Info.FirstVisibleIndex);
            }
            else
            {
                // The VSP keeps row 421 on the screen (its presenter anchor) and moves the rows above down.
                Assert.Equal(nextY, h.ScreenY(next)!.Value, 1);
                Assert.Equal(419, h.Info.FirstVisibleIndex);
            }
            Assert.True(double.IsFinite(h.Scroll.Extent.Height) && double.IsFinite(h.Scroll.Offset.Y));
        }

        // Every row has height 0 / every row is hidden; from the start and after a scroll to the middle. The
        // layout ends (the panel stops realizing at the end of the list), Extent is finite.
        [Theory]
        [InlineData(false, Zero.Height)]
        [InlineData(false, Zero.Invisible)]
        [InlineData(true, Zero.Height)]
        [InlineData(true, Zero.Invisible)]
        public void All_Rows_Of_Height_Zero_End_The_Layout(bool lazy, Zero zero)
        {
            var rows = Rows(Heights.Uniform, 1000).ToList();
            using (var h = new Harness(lazy, rows.Select(r => MakeZero(r, zero))))
            {
                _output.WriteLine($"{h.Name} {zero} from the start: realized {h.Realized().Count}, Extent {h.Scroll.Extent.Height}, " +
                    $"Offset {h.Scroll.Offset.Y}");
                Assert.True(h.Realized().Count <= h.Items.Count);
                Assert.True(double.IsFinite(h.Scroll.Extent.Height));
                h.ScrollBy(100);
                Assert.Equal(0, h.Scroll.Offset.Y);
            }

            using (var h = new Harness(lazy, Rows(Heights.Uniform, 1000)))
            {
                h.ScrollToTop(500);
                foreach (var r in h.Items)
                    MakeZero(r, zero);
                h.Strict = lazy;
                h.Layout();
                _output.WriteLine($"{h.Name} {zero} after a scroll: realized {h.Realized().Count}, Extent {h.Scroll.Extent.Height}, " +
                    $"Offset {h.Scroll.Offset.Y}; {string.Join("; ", h.Problems)}");
                Assert.True(double.IsFinite(h.Scroll.Extent.Height));
                Assert.True(h.Realized().Count <= h.Items.Count);

                // The VSP does not finish the layout: the layout manager stops after its pass limit (~3 s here).
                if (!lazy)
                    Assert.Contains(h.Problems, x => x.Contains("did not finish"));
            }
        }

        // A row whose measure gives another height each time (100 / 150 alternating). The layout ends in every
        // step of a scroll through it.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Non_Deterministic_Measure_Converges(bool lazy)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));
            var n = 0;
            var row = h.Items[405];
            row.Measure = () => ++n % 2 == 0 ? 150 : 100;

            h.ScrollToTop(400);

            for (var i = 0; i < 20; ++i)
            {
                h.ScrollBy(i < 10 ? 20 : -20);

                // Something makes the row measure again: it gets the other height. The rows above it stay.
                var tracked = h.FirstFullyVisible();
                var y = h.ScreenY(tracked)!.Value;
                var count = n;
                h.ContainerOf(row)?.GetVisualDescendants().OfType<RowView>().Single().InvalidateMeasure();
                h.Layout();
                Assert.True(n > count, "the row was not measured again");
                if (tracked != row && h.Items.IndexOf(tracked) < 405)
                    Assert.Equal(y, h.ScreenY(tracked)!.Value, 1);
            }

            _output.WriteLine($"{h.Name}: row 405 measured {n} times, max measures in one layout {h.MaxMeasures}");
            Assert.InRange(n, 21, 200);
        }

        public enum Bad
        {
            NaN,
            Infinity,
            Negative,
        }

        // A template that returns NaN, infinity or a negative height. Avalonia's Measure throws for NaN and infinity
        // before a panel sees the value; a negative height is clamped to 0. The panel never hangs.
        [Theory]
        [InlineData(false, Bad.NaN)]
        [InlineData(false, Bad.Infinity)]
        [InlineData(false, Bad.Negative)]
        [InlineData(true, Bad.NaN)]
        [InlineData(true, Bad.Infinity)]
        [InlineData(true, Bad.Negative)]
        public void Bad_Desired_Height_Throws_Or_Is_Clamped(bool lazy, Bad bad)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));

            h.ScrollToTop(400);
            h.Items[402].Measure = () => bad switch
            {
                Bad.NaN => double.NaN,
                Bad.Infinity => double.PositiveInfinity,
                _ => -50,
            };

            var e = Record.Exception(() => h.Layout());
            _output.WriteLine($"{h.Name} {bad}: {e?.GetType().Name}: {e?.Message}; row 402 height {h.ContainerOf(h.Items[402])?.Bounds.Height}");

            if (bad == Bad.Negative)
            {
                Assert.Null(e);
                Assert.Equal(0, h.ContainerOf(h.Items[402])!.Bounds.Height);
                Assert.Equal(200, h.ScreenY(h.Items[403])!.Value, 1);
            }
            else
            {
                Assert.IsType<InvalidOperationException>(e);
                h.Items[402].Measure = null;
                h.Items[402].Height = 50;
                h.Layout();
                Assert.Equal(250, h.ScreenY(h.Items[403])!.Value, 1);
            }
        }

        public enum Away
        {
            Invisible,
            Detached,
        }

        // #21903, #21905: rows change size while the list is hidden or detached (a tab switch). When it is shown
        // again the next layout applies the sizes; the first fully visible row stays (the changed rows are above it
        // and below it).
        [Theory]
        [InlineData(false, Away.Invisible)]
        [InlineData(false, Away.Detached)]
        [InlineData(true, Away.Invisible)]
        [InlineData(true, Away.Detached)]
        public void Size_Change_While_Hidden_Is_Applied_When_Shown(bool lazy, Away away)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));

            h.ScrollToTop(400);
            h.ScrollBy(30);
            var tracked = h.Items[401];
            var before = h.ScreenY(tracked)!.Value;

            if (away == Away.Invisible)
                h.ItemsControl.IsVisible = false;
            else
                h.Window.Content = null;
            RunLayout(h.Window);

            h.Items[399].Height = 300;
            h.Items[403].Height = 250;

            if (away == Away.Invisible)
                h.ItemsControl.IsVisible = true;
            else
                h.Window.Content = h.ItemsControl;
            h.Layout();

            _output.WriteLine($"{h.Name} {away}: row 401 {before} -> {Format(h.ScreenY(tracked))}, row 399 " +
                $"{h.ContainerOf(h.Items[399])?.Bounds.Height}, row 403 {h.ContainerOf(h.Items[403])?.Bounds.Height}");
            Assert.Equal(300, h.ContainerOf(h.Items[399])!.Bounds.Height, 1);
            Assert.Equal(250, h.ContainerOf(h.Items[403])!.Bounds.Height, 1);
            Assert.Equal(before, h.ScreenY(tracked)!.Value, 1);
            Assert.Equal(before + 200, h.ScreenY(h.Items[403])!.Value, 1);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------------------

        private static string Format(double? y) => y is { } v ? v.ToString("0.##") : "gone";

        private void AssertKept(Harness h, Row tracked, double before, double? after, bool expectKept)
        {
            var kept = after is { } y && Math.Abs(y - before) <= Tolerance;
            Assert.True(kept == expectKept,
                $"{h.Name}: row {tracked.Id} {before} -> {Format(after)}, expected {(expectKept ? "kept" : "moved (the VSP's result)")}");
        }

        // +100 from the model, then +220 more from the row's own SizeChanged in the same layout pass.
        private static void GrowTwice(Harness h, Row row)
        {
            var container = h.ContainerOf(row)!;
            var grownOnce = false;
            var height = row.Height;
            container.SizeChanged += (_, _) =>
            {
                if (!grownOnce && row.Height >= height + 100)
                {
                    grownOnce = true;
                    row.Height += 220;
                }
            };
        }

        private static Row MakeZero(Row row, Zero zero)
        {
            if (zero == Zero.Height)
                row.Height = 0;
            else
                row.Visible = false;
            return row;
        }

        /// <summary>The height of row <paramref name="index"/> in the given set.</summary>
        public static double HeightOf(Heights set, int index) => set switch
        {
            Heights.Uniform => 100,
            Heights.Periodic => 40 + index * 37 % 5 * 30,
            Heights.Regions => index < 300 ? 40 : 160,
            Heights.Fractional => (Math.Round((40 + index * 37 % 5 * 30) * 2.625) + 1) / 2.625,
            _ => throw new ArgumentOutOfRangeException(nameof(set)),
        };

        private static IEnumerable<Row> Rows(Heights set, int count) =>
            Enumerable.Range(0, count).Select(i => new Row(i, HeightOf(set, i)));

        /// <summary>The VSP's own results where it fails by design.</summary>
        private static class Expectations
        {
            // An insert above (also in a Reset) always moves the content; a remove or move above only with heights
            // that are not uniform; an insert into the buffer with uniform heights leaves 30 px empty at the bottom.
            // A replace above, an insert below and a Reset with the same items keep it.
            public static Outcome VspCollectionChange(Change change, Heights heights) => (change, heights) switch
            {
                (Change.InsertFarAbove, _) => Outcome.Moved,
                (Change.ResetPlusInsertFarAbove, _) => Outcome.Moved,
                (Change.RemoveFarAbove or Change.MoveFarAbove or Change.RemoveBufferRows, not Heights.Uniform) => Outcome.Moved,
                (Change.InsertInBufferAbove, Heights.Uniform) => Outcome.Gap,
                _ => Outcome.Kept,
            };
        }

        public class Row : INotifyPropertyChanged
        {
            private double _height;
            private bool _visible = true;
            private Func<double>? _measure;

            public Row(int id, double height) => (Id, _height) = (id, height);

            public event PropertyChangedEventHandler? PropertyChanged;

            public int Id { get; }

            public double Height { get => _height; set => Set(ref _height, value); }

            public bool Visible { get => _visible; set => Set(ref _visible, value); }

            /// <summary>Replaces <see cref="Height"/> in the row's measure.</summary>
            public Func<double>? Measure { get => _measure; set => Set(ref _measure, value); }

            /// <summary>Set once the row has had its late growth.</summary>
            public bool Loaded { get; set; }

            public double DesiredHeight() => !_visible ? 0 : _measure?.Invoke() ?? _height;

            public override string ToString() => $"Row {Id}";

            private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        /// <summary>The row template: measures to the row's height and counts its measures.</summary>
        private sealed class RowView : Control
        {
            private readonly Harness _harness;
            private Row? _row;

            public RowView(Harness harness)
            {
                _harness = harness;
                UseLayoutRounding = false;
            }

            protected override void OnDataContextChanged(EventArgs e)
            {
                if (_row is not null)
                    _row.PropertyChanged -= OnRowChanged;
                _row = DataContext as Row;
                if (_row is not null)
                    _row.PropertyChanged += OnRowChanged;
                InvalidateMeasure();
                base.OnDataContextChanged(e);
            }

            private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => InvalidateMeasure();

            protected override Size MeasureOverride(Size availableSize)
            {
                ++_harness.Measures;
                return new Size(100, _row?.Measure?.Invoke() ?? _row?.Height ?? 0);
            }
        }

        /// <summary>
        /// An items control with the panel in a 600 x 600 window, and the consistency checks after each layout.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            public Harness(bool lazy, IEnumerable<Row> rows, double cacheLength = 0.5)
            {
                Lazy = lazy;
                Items = new ResettingCollection<Row>(rows);
                ItemsControl = new ItemsControl { ItemTemplate = new FuncDataTemplate<Row>((_, _) => new RowView(this)) };
                var style = new Style(x => x.OfType<ContentPresenter>())
                {
                    Setters =
                    {
                        new Setter(Layoutable.UseLayoutRoundingProperty, false),
                        new Setter(Visual.IsVisibleProperty, new Binding(nameof(Row.Visible))),
                    },
                };
                (Window, Scroll) = Show(ItemsControl, Items, lazy, cacheLength, 600, style);
                Check("after the first layout");
            }

            public bool Lazy { get; }
            public string Name => Lazy ? "Lazy" : "VSP";
            public ResettingCollection<Row> Items { get; }
            public ItemsControl ItemsControl { get; }
            public Window Window { get; }
            public ScrollViewer Scroll { get; }
            public Panel Panel => ItemsControl.ItemsPanelRoot!;
            public IItemsScrollInfo Info => ItemsScroll.Get(ItemsControl);
            public double ViewportHeight => Scroll.Viewport.Height;

            /// <summary>Row measures since the start of the last <see cref="Layout"/>.</summary>
            public int Measures { get; set; }
            public int MaxMeasures { get; private set; }

            public int FirstRealizedIndex => ItemsControl.IndexFromContainer(Realized()[0]);

            public List<Control> Realized() => ItemsControl.GetRealizedContainers()?.ToList() ?? new();

            public IEnumerable<Row> RealizedRows() => Realized().Select(c => (Row)c.DataContext!);

            public void Layout()
            {
                Measures = 0;
                RunLayout(ItemsControl);
                MaxMeasures = Math.Max(MaxMeasures, Measures);
                Check("after a layout");
            }

            public void ScrollBy(double delta)
            {
                LazyStackPanelTestTarget.ScrollBy(Scroll, delta);
                Layout();
            }

            /// <summary>Brings row <paramref name="index"/> into view, then scrolls it to the top.</summary>
            public void ScrollToTop(int index)
            {
                ItemsControl.ScrollIntoView(index);
                Layout();
                var y = ScreenY(Items[index]);
                Assert.NotNull(y);
                if (Math.Abs(y!.Value) > 0.001)
                    ScrollBy(y.Value);
            }

            public void ScrollToEnd()
            {
                Scroll.ScrollToEnd();
                Layout();
            }

            public Control? ContainerOf(Row row) =>
                Realized().FirstOrDefault(c => ReferenceEquals(c.DataContext, row));

            /// <summary>The top of the row relative to the viewport top; null if not realized or hidden.</summary>
            public double? ScreenY(Row row) =>
                ContainerOf(row) is { IsVisible: true } c ? c.TranslatePoint(default, Scroll)!.Value.Y : null;

            public Row FirstFullyVisible() => Realized()
                .Where(c => c.IsVisible && c.Bounds.Height > 0)
                .Select(c => (Row: (Row)c.DataContext!, Y: c.TranslatePoint(default, Scroll)!.Value.Y))
                .Where(x => x.Y >= -Tolerance)
                .OrderBy(x => x.Y)
                .First().Row;

            /// <summary>
            /// The realized rows are contiguous, in collection order, and cover the viewport; no pooled container is
            /// reported as realized; 0 &lt;= Offset &lt;= max(0, Extent - Viewport); the layout pass finished.
            /// </summary>
            public void Check(string when)
            {
                var problem = FindProblem();
                if (problem is null)
                    return;
                Problems.Add($"{when}: {problem}");
                if (Strict)
                    Assert.Fail($"{Name} {when}: {problem}");
            }

            /// <summary>False: a failed check is only added to <see cref="Problems"/> (a VSP result by design).</summary>
            public bool Strict { get; set; } = true;

            public List<string> Problems { get; } = new();

            private string? FindProblem()
            {
                var panel = Panel;
                if (!panel.IsMeasureValid || !panel.IsArrangeValid)
                    return "the layout pass did not finish (layout cycle?)";

                var realized = Realized();
                var vh = ViewportHeight;
                double? prevBottom = null;
                int? prevIndex = null;
                double? firstTop = null;
                var lastIndex = -1;

                foreach (var c in realized)
                {
                    var index = ItemsControl.IndexFromContainer(c);
                    if (index < 0 || index >= Items.Count)
                        return $"realized container with index {index}";
                    if (!ReferenceEquals(c.DataContext, Items[index]))
                        return $"container at index {index} shows {c.DataContext}";
                    if (c.GetVisualParent() != panel)
                        return $"container at index {index} is not a child of the panel";
                    if (c.IsVisible != Items[index].Visible)
                        return $"container at index {index}: IsVisible {c.IsVisible} (pooled?)";
                    if (prevIndex is { } p && index != p + 1)
                        return $"realized indexes {p} and {index} are not consecutive";
                    prevIndex = index;
                    lastIndex = index;

                    if (!c.IsVisible)
                        continue;

                    var top = c.TranslatePoint(default, Scroll)!.Value.Y;
                    var bottom = top + c.Bounds.Height;
                    if (prevBottom is { } b && Math.Abs(top - b) > Tolerance)
                        return $"{top - b:0.##} px between row {index} and the row before it";
                    firstTop ??= top;
                    prevBottom = bottom;
                }

                if (Items.Count > 0 && realized.Count == 0)
                    return "no rows realized";

                if (firstTop is { } ft && ft > Tolerance)
                    return $"{ft:0.##} px empty above the first realized row";

                if (prevBottom is { } pb && pb < vh - Tolerance)
                {
                    if (lastIndex != Items.Count - 1)
                        return $"{vh - pb:0.##} px empty below row {lastIndex}";
                    if (realized.Count > 0 && (ItemsControl.IndexFromContainer(realized[0]) != 0 || firstTop < -Tolerance))
                        return $"{vh - pb:0.##} px empty below the last row while rows above are scrolled out";
                }

                var offset = Scroll.Offset.Y;
                var max = Math.Max(0, Scroll.Extent.Height - Scroll.Viewport.Height);
                if (offset < -0.01 || offset > max + 0.01)
                    return $"Offset {offset} outside 0 … {max} (Extent {Scroll.Extent.Height}, Viewport {Scroll.Viewport.Height})";

                if (Measures > 4 * (realized.Count + 10))
                    return $"{Measures} row measures in one layout for {realized.Count} realized rows";

                return null;
            }

            public void Dispose() => Window.Close();
        }
    }
}
