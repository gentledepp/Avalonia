using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    /// Collection changes, against both panels. Window 600 x 600, CacheLength 0.5, no layout rounding. Unless a test
    /// says otherwise, the anchor is row 402 (100 px uniform, 160 px in the other height sets), 30 px scrolled out.
    /// Every layout runs the consistency checks of <see cref="Harness.Check"/>. Where the VSP fails by design, its own
    /// result is the expectation (<see cref="Vsp"/>).
    /// </summary>
    public class LazyStackPanelTests_CollectionChanges : LazyStackPanelTestBase
    {
        private const double Tolerance = 0.5;
        private const int AnchorIndex = 402;
        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_CollectionChanges(ITestOutputHelper output) => _output = output;

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

        public enum Outcome
        {
            /// <summary>The tracked row is at the expected place; the harness checks pass.</summary>
            Kept,
            /// <summary>The tracked row is somewhere else (or not realized); the harness checks pass.</summary>
            Moved,
            /// <summary>A harness check fails (a gap, a wrong container, Offset out of range).</summary>
            Gap,
        }

        private static IEnumerable<object[]> Panels(IEnumerable<object[]> cases) =>
            from lazy in new[] { false, true } from c in cases select new object[] { lazy }.Concat(c).ToArray();

        private static IEnumerable<object[]> Cross<T1, T2>(IEnumerable<T1> a, IEnumerable<T2> b) =>
            Panels(from x in a from y in b select new object[] { x!, y! });

        // ---------------------------------------------------------------------------------------------------------
        // Insert / remove in the buffer above
        // ---------------------------------------------------------------------------------------------------------

        public enum BufferChange
        {
            /// <summary>1 row of 70 px before the first realized row's successor (top of the buffer).</summary>
            InsertAtTopOfBuffer,
            /// <summary>3 rows of 70 px directly above the anchor row.</summary>
            InsertJustAboveTheAnchor,
            /// <summary>The first realized row.</summary>
            RemoveFirstRealized,
            /// <summary>The row directly above the anchor row.</summary>
            RemoveJustAboveTheAnchor,
        }

        public static IEnumerable<object[]> BufferCases() =>
            Cross(Enum.GetValues<BufferChange>(), Enum.GetValues<Heights>());

        // A change in the buffer above the screen. The content does not move, and the
        // viewport stays filled to the bottom.
        [Theory]
        [MemberData(nameof(BufferCases))]
        public void Change_In_The_Buffer_Above_Keeps_The_Content(bool lazy, BufferChange change, Heights heights)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));
            h.ScrollToAnchor(30);
            var anchor = h.Items[AnchorIndex];
            var firstRealized = h.FirstRealizedIndex;
            Assert.True(firstRealized < AnchorIndex - 1, $"no buffer above: first realized {firstRealized}");
            var before = h.ScreenY(anchor)!.Value;

            switch (change)
            {
                case BufferChange.InsertAtTopOfBuffer:
                    h.Items.Insert(firstRealized + 1, new Row(10000, 70));
                    break;
                case BufferChange.InsertJustAboveTheAnchor:
                    for (var i = 0; i < 3; ++i)
                        h.Items.Insert(AnchorIndex, new Row(10000 + i, 70));
                    break;
                case BufferChange.RemoveFirstRealized:
                    h.Items.RemoveAt(firstRealized);
                    break;
                case BufferChange.RemoveJustAboveTheAnchor:
                    h.Items.RemoveAt(AnchorIndex - 1);
                    break;
            }

            h.Strict = false;
            h.Layout();
            AssertOutcome(h, lazy ? Outcome.Kept : Vsp.BufferChange(change, heights), anchor, before, before,
                $"{change}/{heights}, first realized was {firstRealized}");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Insert / remove just above the screen, 5 times
        // ---------------------------------------------------------------------------------------------------------

        public static IEnumerable<object[]> RepeatedCases() =>
            Panels(from insert in new[] { true, false }
                   from cut in new[] { 0.0, 20, 45, 60 }
                   from heights in new[] { Heights.Uniform, Heights.Periodic, Heights.Fractional }
                   select new object[] { insert, cut, heights });

        // One row inserted directly above the anchor row (or the row directly above it removed), 5 times, with a
        // layout after each change and no scroll. The anchor row is at the top or cut by 20-60 px. No drift after any
        // step, and no late jump in two more layouts.
        [Theory]
        [MemberData(nameof(RepeatedCases))]
        public void Repeated_Change_Just_Above_The_Screen_Does_Not_Drift(bool lazy, bool insert, double cut, Heights heights)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));
            h.ScrollToAnchor(cut);
            var anchor = h.Items[AnchorIndex];
            var before = h.ScreenY(anchor)!.Value;
            Assert.Equal(-cut, before, 1);
            var ys = new List<double?>();
            h.Strict = false;

            for (var i = 0; i < 5; ++i)
            {
                var index = h.Items.IndexOf(anchor);
                if (insert)
                    h.Items.Insert(index, new Row(10000 + i, 70));
                else
                    h.Items.RemoveAt(index - 1);
                h.Layout();
                ys.Add(h.ScreenY(anchor));
            }

            h.Layout();
            h.Layout();
            ys.Add(h.ScreenY(anchor));

            var steps = string.Join(", ", ys.Select(Format));
            var drifted = ys.Any(y => y is not { } v || Math.Abs(v - before) > Tolerance);
            var actual = h.Problems.Count > 0 ? Outcome.Gap : drifted ? Outcome.Moved : Outcome.Kept;
            var expected = lazy ? Outcome.Kept : Vsp.Repeated(insert, cut, heights);
            _output.WriteLine($"{h.Name} {(insert ? "insert" : "remove")} cut {cut} {heights}: anchor {before:0.##} -> {steps}; " +
                string.Join("; ", h.Problems));
            Assert.True(expected == actual, $"{h.Name}: expected {expected}, was {actual}: anchor {before:0.##} -> {steps}; " +
                string.Join("; ", h.Problems));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Insert at 0 while row 20 is the anchor
        // ---------------------------------------------------------------------------------------------------------

        public static IEnumerable<object[]> InsertAtZeroCases() =>
            Cross(Enum.GetValues<Heights>(), new[] { 0.0, 30 });

        // Rows 0-19 are not realized (row 20 is more than one buffer below the top).
        [Theory]
        [MemberData(nameof(InsertAtZeroCases))]
        public void Insert_At_Index_0_While_Row_20_Is_The_Anchor_Keeps_The_Content(bool lazy, Heights heights, double cut)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));
            h.ScrollToTop(20);
            if (cut > 0)
                h.ScrollBy(cut);
            var anchor = h.Items[20];
            var before = h.ScreenY(anchor)!.Value;

            h.Items.Insert(0, new Row(10000, 70));
            h.Strict = false;
            h.Layout();

            AssertOutcome(h, lazy ? Outcome.Kept : Vsp.InsertAtZero(heights, cut), anchor, before, before,
                $"{heights} cut {cut}, first realized {h.FirstRealizedIndex}");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Insert / remove at or below the anchor, on the screen
        // ---------------------------------------------------------------------------------------------------------

        public static IEnumerable<object[]> BelowCases() =>
            Panels(from heights in new[] { Heights.Uniform, Heights.Periodic }
                   from c in new (bool Insert, int Relative)[] { (true, 0), (true, 1), (true, 3), (false, 1), (false, 3) }
                   select new object[] { c.Insert, c.Relative, heights });

        // A row inserted at the anchor's index or below it, or a row below it removed, on the screen. The anchor item
        // stays; the rows from the change down move by the inserted / removed height. An insert at the anchor's index
        // puts the new row above the anchor item (the anchor follows its item), so it is above the screen.
        [Theory]
        [MemberData(nameof(BelowCases))]
        public void Insert_Or_Remove_At_Or_Below_The_Anchor_Moves_Only_The_Rows_Below(bool lazy, bool insert, int relative, Heights heights)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));
            h.ScrollToAnchor(30);
            var anchor = h.Items[AnchorIndex];
            var index = AnchorIndex + relative;
            var anchorBefore = h.ScreenY(anchor)!.Value;
            var slotTop = h.ScreenY(h.Items[index])!.Value;
            Row tracked;
            double expected;

            if (insert)
            {
                tracked = h.Items[index];
                expected = slotTop + (relative == 0 ? 0 : 70);
                h.Items.Insert(index, new Row(10000, 70));
            }
            else
            {
                tracked = h.Items[index + 1];
                expected = slotTop;
                h.Items.RemoveAt(index);
            }

            h.Strict = false;
            h.Layout();

            var anchorAfter = h.ScreenY(anchor);
            var trackedAfter = h.ScreenY(tracked);
            var ok = h.Problems.Count == 0 && Near(anchorAfter, anchorBefore) && Near(trackedAfter, expected);
            var actual = h.Problems.Count > 0 ? Outcome.Gap : ok ? Outcome.Kept : Outcome.Moved;
            var want = lazy ? Outcome.Kept : Vsp.Below(insert, relative, heights);
            var text = $"{h.Name} {(insert ? "insert" : "remove")} at anchor+{relative} {heights}: anchor {anchorBefore:0.##} -> " +
                $"{Format(anchorAfter)}, row {tracked.Id} -> {Format(trackedAfter)} (spec {expected:0.##}); {string.Join("; ", h.Problems)}";
            _output.WriteLine(text);
            Assert.True(want == actual, $"expected {want}, was {actual}: {text}");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Remove the anchor row
        // ---------------------------------------------------------------------------------------------------------

        public static IEnumerable<object[]> RemoveAnchorCases() =>
            Cross(new[] { Heights.Uniform, Heights.Periodic, Heights.Fractional }, new[] { 0.0, 30 });

        // The next row takes the removed anchor's place with the same offset (its top at the old
        // anchor top; FirstVisibleIndex unchanged).
        [Theory]
        [MemberData(nameof(RemoveAnchorCases))]
        public void Remove_The_Anchor_Row_The_Next_Row_Takes_Its_Place(bool lazy, Heights heights, double cut)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));
            h.ScrollToAnchor(cut);
            var next = h.Items[AnchorIndex + 1];
            var anchorTop = h.ScreenY(h.Items[AnchorIndex])!.Value;
            var nextBefore = h.ScreenY(next)!.Value;

            h.Items.RemoveAt(AnchorIndex);
            h.Strict = false;
            h.Layout();

            AssertOutcome(h, lazy ? Outcome.Kept : Vsp.RemoveAnchor(heights, cut), next, nextBefore, anchorTop,
                $"{heights} cut {cut}, FirstVisibleIndex {h.Info.FirstVisibleIndex}, offset {h.Info.FirstVisibleOffset:0.##}");
            if (lazy)
            {
                Assert.Equal(AnchorIndex, h.Info.FirstVisibleIndex);
                Assert.Equal(cut, h.Info.FirstVisibleOffset, 1);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Remove a range so the anchor index is past the end
        // ---------------------------------------------------------------------------------------------------------

        public enum RangeRemove
        {
            /// <summary>Scrolled to row 900 + 30 px; rows 500-999 are removed.</summary>
            AnchorPastTheEnd,
            /// <summary>Scrolled to the end; the last 50 rows are removed.</summary>
            LastRowsAtTheEnd,
            /// <summary>Scrolled to row 900 + 30 px; rows 3-999 are removed, fewer rows than one page remain.</summary>
            FewerThanOnePage,
        }

        public static IEnumerable<object[]> RangeCases() =>
            Cross(Enum.GetValues<RangeRemove>(), new[] { true, false });

        // Rows are removed so the anchor index is >= the new count (one Remove event with all rows, or one event per
        // row). The last rows fill the viewport (or row 0 is at the top when less than a page remains); no empty
        // space; Offset valid.
        [Theory]
        [MemberData(nameof(RangeCases))]
        public void Remove_A_Range_So_The_Anchor_Is_Past_The_End_Fills_The_Viewport(bool lazy, RangeRemove kind, bool oneEvent)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000));
            if (kind == RangeRemove.LastRowsAtTheEnd)
                h.ScrollToEnd();
            else
            {
                h.ScrollToTop(900);
                h.ScrollBy(30);
            }

            var (start, count) = kind switch
            {
                RangeRemove.AnchorPastTheEnd => (500, 500),
                RangeRemove.LastRowsAtTheEnd => (950, 50),
                _ => (3, 997),
            };

            if (oneEvent)
                h.Items.RemoveRange(start, count);
            else
                for (var i = 0; i < count; ++i)
                    h.Items.RemoveAt(start + count - 1 - i);

            h.Strict = false;
            h.Layout();

            var last = h.Items[^1];
            var lastBottom = h.ScreenY(last) + last.Height;
            var firstTop = h.ScreenY(h.Items[0]);
            var text = $"{h.Name} {kind} oneEvent {oneEvent}: count {h.Items.Count}, last row bottom {Format(lastBottom)}, " +
                $"row 0 at {Format(firstTop)}, Offset {h.Scroll.Offset.Y:0.##}, Extent {h.Scroll.Extent.Height:0.##}; " +
                string.Join("; ", h.Problems);
            _output.WriteLine(text);
            Assert.True(h.Problems.Count == 0, text);
            if (kind == RangeRemove.FewerThanOnePage)
            {
                Assert.True(Near(firstTop, 0), text);
                Assert.Equal(0, h.Scroll.Offset.Y, 1);
            }
            else
                Assert.True(Near(lastBottom, h.ViewportHeight), text);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Replace the anchor item or a row above
        // ---------------------------------------------------------------------------------------------------------

        public enum ReplaceKind
        {
            AnchorSameHeight,
            AnchorTaller,
            AnchorShorter,
            RowAbove,
        }

        public static IEnumerable<object[]> ReplaceCases() =>
            Cross(Enum.GetValues<ReplaceKind>(), new[] { Heights.Uniform, Heights.Periodic });

        // The anchor item is replaced (same height, +50, -20): the new item is the anchor at the same index, with
        // the same offset. The row above the anchor is replaced with a taller row: nothing on the screen moves.
        [Theory]
        [MemberData(nameof(ReplaceCases))]
        public void Replace_The_Anchor_Or_A_Row_Above_Keeps_The_Anchor_Slot(bool lazy, ReplaceKind kind, Heights heights)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));
            h.ScrollToAnchor(30);
            var anchorTop = h.ScreenY(h.Items[AnchorIndex])!.Value;
            var old = h.Items[AnchorIndex];
            Row tracked;

            if (kind == ReplaceKind.RowAbove)
            {
                tracked = old;
                h.Items[AnchorIndex - 1] = new Row(10000, h.Items[AnchorIndex - 1].Height + 50);
            }
            else
            {
                var height = old.Height + kind switch { ReplaceKind.AnchorTaller => 50, ReplaceKind.AnchorShorter => -20, _ => 0 };
                tracked = new Row(10000, height);
                h.Items[AnchorIndex] = tracked;
            }

            h.Strict = false;
            h.Layout();

            AssertOutcome(h, lazy ? Outcome.Kept : Vsp.Replace(kind, heights), tracked, anchorTop, anchorTop,
                $"{kind}/{heights}, FirstVisibleIndex {h.Info.FirstVisibleIndex}, offset {h.Info.FirstVisibleOffset:0.##}");
            if (lazy)
                Assert.Equal(AnchorIndex, h.Info.FirstVisibleIndex);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Reset with value types / duplicate strings
        // ---------------------------------------------------------------------------------------------------------

        public enum Duplicates
        {
            /// <summary>Ints 0-99; 5 new ints at 0. The anchor value is found at 25.</summary>
            DistinctIntsInsert5,
            /// <summary>Ints i % 5; 2 new ints at 0. The nearest equal value is at 22 (the anchor item itself).</summary>
            RepeatingIntsInsert2,
            /// <summary>Strings "a".."e" repeating; 3 new at 0. The nearest "a" is at 18 (not the old item, at 23).</summary>
            RepeatingStringsInsert3,
            /// <summary>Strings "a".."e" repeating; 5 new at the end. The item at the old index is equal.</summary>
            RepeatingStringsAppend5,
        }

        public static IEnumerable<object[]> DuplicateCases() => Panels(Enum.GetValues<Duplicates>().Select(x => new object[] { x }));

        // A Reset of a list of value types or interned strings. 100 x 100 window, rows of
        // 10 px, anchor row 20 with 3 px scrolled out. The anchor is the nearest equal item, with the old offset.
        [Theory]
        [MemberData(nameof(DuplicateCases))]
        public void Reset_With_Value_Types_Or_Duplicate_Strings_Finds_The_Nearest_Equal_Item(bool lazy, Duplicates kind)
        {
            static string Letter(int i) => ((char)('a' + i % 5)).ToString();
            var (items, reset, expected) = kind switch
            {
                Duplicates.DistinctIntsInsert5 => (Enumerable.Range(0, 100).Cast<object>().ToList(),
                    Enumerable.Range(1000, 5).Concat(Enumerable.Range(0, 100)).Cast<object>().ToList(), 25),
                Duplicates.RepeatingIntsInsert2 => (Enumerable.Range(0, 100).Select(i => (object)(i % 5)).ToList(),
                    new object[] { 7, 8 }.Concat(Enumerable.Range(0, 100).Select(i => (object)(i % 5))).ToList(), 22),
                Duplicates.RepeatingStringsInsert3 => (Enumerable.Range(0, 100).Select(i => (object)Letter(i)).ToList(),
                    new object[] { "x", "y", "z" }.Concat(Enumerable.Range(0, 100).Select(i => (object)Letter(i))).ToList(), 18),
                _ => (Enumerable.Range(0, 100).Select(i => (object)Letter(i)).ToList(),
                    Enumerable.Range(0, 105).Select(i => (object)Letter(i)).ToList(), 20),
            };

            var collection = new ResettingCollection<object>(items);
            var ic = new ItemsControl { ItemTemplate = new FuncDataTemplate<object>((_, _) => new Border { Height = 10 }) };
            var (window, sv) = Show(ic, collection, lazy);
            ScrollBy(sv, 203);
            RunLayout(ic);
            var info = ItemsScroll.Get(ic);
            Assert.Equal(20, info.FirstVisibleIndex);

            collection.Reset(reset);
            RunLayout(ic);

            var top = ic.ContainerFromIndex(info.FirstVisibleIndex)?.TranslatePoint(default, sv)?.Y;
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")} {kind}: FirstVisibleIndex {info.FirstVisibleIndex}, offset " +
                $"{info.FirstVisibleOffset:0.##}, its top {Format(top)}, Offset {sv.Offset.Y:0.##}");
            var (index, offset) = lazy ? (expected, 3.0) : Vsp.Duplicates(kind);
            Assert.Equal(index, info.FirstVisibleIndex);
            Assert.Equal(offset, info.FirstVisibleOffset, 1);
            window.Close();
        }

        // ---------------------------------------------------------------------------------------------------------
        // More than 26 rows in one change, as a Reset
        // ---------------------------------------------------------------------------------------------------------

        public enum BigReset
        {
            /// <summary>30 rows inserted directly above the anchor row.</summary>
            InsertJustAbove,
            /// <summary>The 30 rows directly above the anchor row removed.</summary>
            RemoveJustAbove,
            /// <summary>30 rows inserted after the row below the anchor (headline expand).</summary>
            InsertBelowOnTheScreen,
            /// <summary>The 30 rows after the row below the anchor removed (headline collapse).</summary>
            RemoveBelowOnTheScreen,
        }

        public static IEnumerable<object[]> BigResetCases() =>
            Cross(Enum.GetValues<BigReset>(), new[] { Heights.Uniform, Heights.Periodic });

        // DynamicData sends a change of more than 26 rows as one Reset. Near above the anchor (in
        // the buffer and beyond) or below it on the screen. The anchor row does not move; the row below it stays
        // too (the change is after it).
        [Theory]
        [MemberData(nameof(BigResetCases))]
        public void More_Than_26_Rows_As_One_Reset_Do_Not_Move_The_Anchor(bool lazy, BigReset kind, Heights heights)
        {
            using var h = new Harness(lazy, Rows(heights, 1000));
            h.ScrollToAnchor(30);
            var anchor = h.Items[AnchorIndex];
            var before = h.ScreenY(anchor)!.Value;
            var list = h.Items.ToList();
            var added = Enumerable.Range(0, 30).Select(i => new Row(10000 + i, 70));

            switch (kind)
            {
                case BigReset.InsertJustAbove:
                    list.InsertRange(AnchorIndex, added);
                    break;
                case BigReset.RemoveJustAbove:
                    list.RemoveRange(AnchorIndex - 30, 30);
                    break;
                case BigReset.InsertBelowOnTheScreen:
                    list.InsertRange(AnchorIndex + 2, added);
                    break;
                case BigReset.RemoveBelowOnTheScreen:
                    list.RemoveRange(AnchorIndex + 2, 30);
                    break;
            }

            h.Items.Reset(list);
            h.Strict = false;
            h.Layout();

            AssertOutcome(h, lazy ? Outcome.Kept : Vsp.BigReset(kind, heights), anchor, before, before,
                $"{kind}/{heights}, FirstVisibleIndex {h.Info.FirstVisibleIndex}");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Reset to fewer items than one page while scrolled to the end
        // ---------------------------------------------------------------------------------------------------------

        public static IEnumerable<object[]> FewerCases() =>
            Cross(new[] { 1, 3 }, new[] { false, true });

        // Scrolled to the end, a Reset leaves 1 or 3 rows (the old instances or new ones). Row 0 is at the top,
        // Offset 0, no empty space above, no pooled container realized.
        [Theory]
        [MemberData(nameof(FewerCases))]
        public void Reset_To_Fewer_Items_Than_One_Page_At_The_End_Shows_Them_From_The_Top(bool lazy, int count, bool newInstances)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000));
            h.ScrollToEnd();
            h.Strict = false;

            h.Items.Reset(newInstances
                ? Enumerable.Range(0, count).Select(i => new Row(20000 + i, 100)).ToList()
                : h.Items.Take(count).ToList());
            h.Layout();

            var top = h.ScreenY(h.Items[0]);
            var text = $"{h.Name} {count} rows, new {newInstances}: row 0 at {Format(top)}, realized {h.Realized().Count}, " +
                $"Offset {h.Scroll.Offset.Y:0.##}, Extent {h.Scroll.Extent.Height:0.##}; {string.Join("; ", h.Problems)}";
            _output.WriteLine(text);
            Assert.True(h.Problems.Count == 0, text);
            Assert.True(Near(top, 0), text);
            Assert.Equal(count, h.Realized().Count);
            Assert.Equal(0, h.Scroll.Offset.Y, 1);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Several changes before one layout
        // ---------------------------------------------------------------------------------------------------------

        public static IEnumerable<object[]> SeveralCases() =>
            Panels(new[] { Heights.Uniform, Heights.Periodic, Heights.Regions }.Select(x => new object[] { x }));

        // An insert above (in the buffer), a remove below (on the screen) and a replace of the anchor item before
        // one layout give the same screen as the same changes with a layout after each one.
        [Theory]
        [MemberData(nameof(SeveralCases))]
        public void Several_Changes_Before_One_Layout_Equal_The_Changes_One_By_One(bool lazy, Heights heights)
        {
            using var one = new Harness(lazy, Rows(heights, 1000));
            using var each = new Harness(lazy, Rows(heights, 1000));

            foreach (var h in new[] { one, each })
            {
                h.ScrollToAnchor(30);
                h.Strict = false;
                h.Items.Insert(AnchorIndex - 1, new Row(10000, 70));
                if (h == each)
                    h.Layout();
                h.Items.RemoveAt(AnchorIndex + 4);
                if (h == each)
                    h.Layout();
                h.Items[AnchorIndex + 1] = new Row(10001, 120);
                h.Layout();
            }

            var a = Screen(one);
            var b = Screen(each);
            var diff = a.Keys.Intersect(b.Keys).Where(id => Math.Abs(a[id] - b[id]) > Tolerance)
                .Select(id => $"row {id}: {a[id]:0.##} / {b[id]:0.##}").ToList();
            var text = $"{one.Name} {heights}: one layout: anchor {Format(one.ScreenY(one.Items[AnchorIndex + 1]))}, " +
                $"FirstVisibleIndex {one.Info.FirstVisibleIndex}; one by one: anchor {Format(each.ScreenY(each.Items[AnchorIndex + 1]))}, " +
                $"FirstVisibleIndex {each.Info.FirstVisibleIndex}; {diff.Count} differences {string.Join(", ", diff.Take(3))}; " +
                string.Join("; ", one.Problems.Concat(each.Problems));
            _output.WriteLine(text);

            Assert.True(one.Problems.Count == 0 && each.Problems.Count == 0, text);
            var same = diff.Count == 0 && one.Info.FirstVisibleIndex == each.Info.FirstVisibleIndex;
            var anchorKept = Near(one.ScreenY(one.Items[AnchorIndex + 1]), -30);
            if (lazy)
            {
                Assert.True(same, text);
                Assert.True(anchorKept, text); // the new anchor item (10001) in the old anchor's slot
            }
            else
                Assert.True(same == Vsp.SeveralSame(heights), text);
        }

        private static Dictionary<int, double> Screen(Harness h) =>
            h.Realized().Where(c => c.IsVisible)
                .Select(c => (Id: ((Row)c.DataContext!).Id, Y: c.TranslatePoint(default, h.Scroll)!.Value.Y))
                .Where(x => x.Y > -h.ViewportHeight && x.Y < h.ViewportHeight)
                .ToDictionary(x => x.Id, x => x.Y);

        // ---------------------------------------------------------------------------------------------------------
        // A change from a ScrollChanged handler
        // ---------------------------------------------------------------------------------------------------------

        public static IEnumerable<object[]> ScrollChangedCases() =>
            Panels(new[] { false, true }.Select(x => new object[] { x }));

        // A ScrollChanged handler changes the collection during a scroll (load more: 50 rows appended; or 5 rows
        // inserted at 0). The scroll moves the content by the scrolled distance only; the next wheel step is normal.
        [Theory]
        [MemberData(nameof(ScrollChangedCases))]
        public void Change_From_A_ScrollChanged_Handler_Does_Not_Move_The_Content(bool lazy, bool insertAtZero)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 200));
            h.ScrollToTop(150);
            var armed = true;
            h.Scroll.ScrollChanged += (_, _) =>
            {
                if (!armed)
                    return;
                armed = false;
                if (insertAtZero)
                    for (var i = 0; i < 5; ++i)
                        h.Items.Insert(0, new Row(10000 + i, 70));
                else
                    for (var i = 0; i < 50; ++i)
                        h.Items.Add(new Row(10000 + i, 70));
            };
            var tracked = h.FirstFullyVisible();
            var y0 = h.ScreenY(tracked)!.Value;
            h.Strict = false;

            h.ScrollBy(50);
            h.Layout();
            var y1 = h.ScreenY(tracked);
            h.ScrollBy(50);
            var y2 = h.ScreenY(tracked);

            var ok = !armed && Near(y1, y0 - 50) && Near(y2, y0 - 100);
            var actual = h.Problems.Count > 0 ? Outcome.Gap : ok ? Outcome.Kept : Outcome.Moved;
            var expected = lazy ? Outcome.Kept : Vsp.ScrollChanged(insertAtZero);
            var text = $"{h.Name} {(insertAtZero ? "insert at 0" : "append")}: handler ran {!armed}, row {tracked.Id} {y0:0.##} -> " +
                $"{Format(y1)} -> {Format(y2)} (spec {y0 - 50:0.##}, {y0 - 100:0.##}), count {h.Items.Count}; {string.Join("; ", h.Problems)}";
            _output.WriteLine(text);
            Assert.True(expected == actual, $"expected {expected}, was {actual}: {text}");
        }

        // ---------------------------------------------------------------------------------------------------------
        // A change inside the panel's measure
        // ---------------------------------------------------------------------------------------------------------

        public enum InMeasure
        {
            /// <summary>A row's MeasureOverride inserts a row below the anchor (on the screen).</summary>
            MeasureInsertsBelow,
            /// <summary>A row's MeasureOverride inserts a row directly above the anchor.</summary>
            MeasureInsertsAbove,
            /// <summary>A row's SizeChanged handler inserts a row below the anchor.</summary>
            SizeChangedInsertsBelow,
        }

        public static IEnumerable<object[]> InMeasureCases() => Panels(Enum.GetValues<InMeasure>().Select(x => new object[] { x }));

        // A row's measure (or its SizeChanged handler) inserts a row while the panel lays out. No exception; the
        // insert is applied at the latest in the next layout (the new row is realized at its index); the anchor item
        // does not move.
        [Theory]
        [MemberData(nameof(InMeasureCases))]
        public void Change_Inside_The_Layout_Is_Applied_In_The_Next_Layout(bool lazy, InMeasure kind)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));
            h.ScrollToAnchor(30);
            var anchor = h.Items[AnchorIndex];
            var before = h.ScreenY(anchor)!.Value;
            var trigger = h.Items[AnchorIndex + 1];
            var inserted = new Row(10000, 70);
            var done = false;

            void Insert()
            {
                if (done)
                    return;
                done = true;
                var index = h.Items.IndexOf(anchor);
                h.Items.Insert(kind == InMeasure.MeasureInsertsAbove ? index : index + 2, inserted);
            }

            if (kind == InMeasure.SizeChangedInsertsBelow)
                h.ContainerOf(trigger)!.SizeChanged += (_, _) => Insert();
            else
                trigger.OnMeasure = Insert;

            h.Strict = false;
            trigger.Height += 10;
            var error = Record.Exception(() => { h.Layout(); h.Layout(); });

            var expectedIndex = h.Items.IndexOf(anchor) + (kind == InMeasure.MeasureInsertsAbove ? -1 : 2);
            var container = h.ContainerOf(inserted);
            var text = $"{h.Name} {kind}: inserted {done}, error {error?.GetType().Name} {error?.Message}, new row realized " +
                $"{container is not null} at index {(container is null ? -1 : h.ItemsControl.IndexFromContainer(container))} " +
                $"(expected {expectedIndex}), anchor {before:0.##} -> {Format(h.ScreenY(anchor))}; {string.Join("; ", h.Problems)}";
            _output.WriteLine(text);
            Assert.True(error is null, text);
            Assert.True(done, text);
            Assert.True(h.Problems.Count == 0, text);
            Assert.Equal(expectedIndex, h.Items.IndexOf(inserted));
            if (kind != InMeasure.MeasureInsertsAbove)
                Assert.True(container is not null && h.ItemsControl.IndexFromContainer(container) == expectedIndex, text);
            var kept = Near(h.ScreenY(anchor), before);
            Assert.True(kept == (lazy || Vsp.InMeasureKept(kind)), text);
        }

        // ---------------------------------------------------------------------------------------------------------
        // A change while the list is hidden or detached
        // ---------------------------------------------------------------------------------------------------------

        public enum Away
        {
            Invisible,
            Detached,
        }

        public enum HiddenChange
        {
            Insert5Above,
            Remove5Above,
            Insert5Below,
        }

        public static IEnumerable<object[]> HiddenCases() => Cross(Enum.GetValues<Away>(), Enum.GetValues<HiddenChange>());

        // The list is hidden (IsVisible) or detached (a tab switch); 5 rows are inserted directly above the
        // anchor, removed directly above it, or inserted below it; then the list is shown. The anchor follows the
        // event index (its item stays at its place); the rows are contiguous.
        [Theory]
        [MemberData(nameof(HiddenCases))]
        public void Change_While_Hidden_Keeps_The_Anchor_When_Shown(bool lazy, Away away, HiddenChange change)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000));
            h.ScrollToAnchor(30);
            var anchor = h.Items[AnchorIndex];
            var before = h.ScreenY(anchor)!.Value;

            if (away == Away.Invisible)
                h.ItemsControl.IsVisible = false;
            else
                h.Window.Content = null;
            RunLayout(h.Window);

            for (var i = 0; i < 5; ++i)
            {
                switch (change)
                {
                    case HiddenChange.Insert5Above:
                        h.Items.Insert(h.Items.IndexOf(anchor), new Row(10000 + i, 70));
                        break;
                    case HiddenChange.Remove5Above:
                        h.Items.RemoveAt(h.Items.IndexOf(anchor) - 1);
                        break;
                    case HiddenChange.Insert5Below:
                        h.Items.Insert(h.Items.IndexOf(anchor) + 1, new Row(10000 + i, 70));
                        break;
                }
            }

            if (away == Away.Invisible)
                h.ItemsControl.IsVisible = true;
            else
                h.Window.Content = h.ItemsControl;
            h.Strict = false;
            h.Layout();

            AssertOutcome(h, lazy ? Outcome.Kept : Vsp.Hidden(away, change), anchor, before, before,
                $"{away}/{change}, FirstVisibleIndex {h.Info.FirstVisibleIndex}");
        }

        // ---------------------------------------------------------------------------------------------------------
        // ItemsSource replaced, set to null, set again
        // ---------------------------------------------------------------------------------------------------------

        public enum SourceChange
        {
            /// <summary>Another collection with 1000 new rows.</summary>
            NewCollection,
            /// <summary>ItemsSource = null.</summary>
            Null,
            /// <summary>ItemsSource = null, layout, then the old collection again.</summary>
            NullThenTheSameCollection,
            /// <summary>The same collection assigned again (no property change).</summary>
            SameCollectionAgain,
        }

        public static IEnumerable<object[]> SourceCases() => Panels(Enum.GetValues<SourceChange>().Select(x => new object[] { x }));

        // Scrolled to row 402 + 30 px, then ItemsSource changes. No harness check fails, no visible child of the panel
        // is not realized (ghost), at most the old count of children. The resulting first visible row is not
        // specified; the test pins what each panel does now (Lazy.Source, Vsp.Source).
        [Theory]
        [MemberData(nameof(SourceCases))]
        public void ItemsSource_Replaced_Or_Null_Leaves_No_Ghost_Children(bool lazy, SourceChange kind)
        {
            using var h = new Harness(lazy, Rows(Heights.Uniform, 1000));
            h.ScrollToAnchor(30);
            var oldItems = h.Items;
            var childrenBefore = h.Panel.Children.Count;

            switch (kind)
            {
                case SourceChange.NewCollection:
                    h.SetSource(new RowList(Enumerable.Range(0, 1000).Select(i => new Row(20000 + i, 100))));
                    break;
                case SourceChange.Null:
                    h.SetSource(null);
                    break;
                case SourceChange.NullThenTheSameCollection:
                    h.SetSource(null);
                    h.Layout();
                    h.SetSource(oldItems);
                    break;
                case SourceChange.SameCollectionAgain:
                    h.SetSource(oldItems);
                    break;
            }

            h.Strict = false;
            h.Layout();

            var realized = h.Realized();
            var ghosts = h.Panel.Children.Count(c => c.IsVisible && !realized.Contains(c));
            var first = h.Items.Count > 0 ? h.Info.FirstVisibleIndex : -1;
            var offset = h.Items.Count > 0 ? h.Info.FirstVisibleOffset : 0;
            var text = $"{h.Name} {kind}: FirstVisibleIndex {first}, offset {offset:0.##}, Offset {h.Scroll.Offset.Y:0.##}, " +
                $"realized {realized.Count}, children {childrenBefore} -> {h.Panel.Children.Count}, ghosts {ghosts}; " +
                string.Join("; ", h.Problems);
            _output.WriteLine(text);
            Assert.True(h.Problems.Count == 0, text);
            Assert.True(ghosts == 0, text);
            Assert.True(h.Panel.Children.Count <= Math.Max(childrenBefore, realized.Count), text);
            var (index, off) = lazy ? Lazy.Source(kind) : Vsp.Source(kind);
            Assert.True(first == index && Math.Abs(offset - off) <= Tolerance, $"expected first visible {index} + {off}: {text}");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Every container stays on its own item after any edit
        // ---------------------------------------------------------------------------------------------------------

        public enum Edit
        {
            InsertInTheBufferAbove,
            InsertOnTheScreen,
            RemoveOnTheScreen,
            MoveOnTheScreen,
            MoveOutFarBelow,
            MoveInFromFarBelow,
            ReplaceOnTheScreen,
            ReorderingReset,
            CoalescedReset,
        }

        public static IEnumerable<object[]> EditCases() => Panels(Enum.GetValues<Edit>().Select(x => new object[] { x }));

        // After any edit, before the next layout (indexes change at once) and after it, every realized container's
        // DataContext is the item at its index, and ContainerFromIndex gives the same container.
        [Theory]
        [MemberData(nameof(EditCases))]
        public void Every_Container_Stays_On_Its_Own_Item_After_Any_Edit(bool lazy, Edit edit)
        {
            using var h = new Harness(lazy, Rows(Heights.Periodic, 1000));
            h.ScrollToAnchor(30);
            var a = AnchorIndex;
            var first = h.FirstRealizedIndex;
            var last = h.ItemsControl.IndexFromContainer(h.Realized()[^1]);

            switch (edit)
            {
                case Edit.InsertInTheBufferAbove:
                    h.Items.Insert(first + 1, new Row(10000, 70));
                    break;
                case Edit.InsertOnTheScreen:
                    h.Items.Insert(a + 2, new Row(10000, 70));
                    break;
                case Edit.RemoveOnTheScreen:
                    h.Items.RemoveAt(a + 2);
                    break;
                case Edit.MoveOnTheScreen:
                    h.Items.Move(a + 3, a - 1);
                    break;
                case Edit.MoveOutFarBelow:
                    h.Items.Move(a + 2, 900);
                    break;
                case Edit.MoveInFromFarBelow:
                    h.Items.Move(900, a + 2);
                    break;
                case Edit.ReplaceOnTheScreen:
                    h.Items[a + 1] = new Row(10000, 70);
                    break;
                case Edit.ReorderingReset:
                {
                    var list = h.Items.ToList();
                    list.Reverse(first, last - first + 1);
                    h.Items.Reset(list);
                    break;
                }
                case Edit.CoalescedReset:
                {
                    var list = h.Items.ToList();
                    list.InsertRange(a + 1, Enumerable.Range(0, 3).Select(i => new Row(10000 + i, 70)));
                    list.RemoveRange(a - 3, 2);
                    list.RemoveAt(a + 5);
                    h.Items.Reset(list);
                    break;
                }
            }

            var wrong = new List<string>();
            foreach (var c in h.Realized())
            {
                var index = h.ItemsControl.IndexFromContainer(c);
                if (index < 0)
                    continue;
                if (index >= h.Items.Count || !ReferenceEquals(c.DataContext, h.Items[index]))
                    wrong.Add($"index {index} shows {c.DataContext}");
                else if (h.ItemsControl.ContainerFromIndex(index) != c)
                    wrong.Add($"ContainerFromIndex({index}) is another container");
            }

            _output.WriteLine($"{h.Name} {edit}: before the layout {wrong.Count} wrong {string.Join(", ", wrong.Take(3))}");
            Assert.True(wrong.Count == 0, $"{h.Name} {edit} before the layout: {string.Join(", ", wrong.Take(5))}");
            h.Layout(); // the harness check asserts DataContext == Items[index] for every realized container
        }

        // ---------------------------------------------------------------------------------------------------------
        // Expectations
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>What Lazy does now where the behaviour is not specified.</summary>
        private static class Lazy
        {
            // ItemsSource changes: a new collection, or null and then the same collection again: row 0 at offset 0.
            // The same collection assigned again is no property change: nothing moves.
            public static (int Index, double Offset) Source(SourceChange kind) => kind switch
            {
                SourceChange.NewCollection => (0, 0),
                SourceChange.Null => (-1, 0),
                SourceChange.NullThenTheSameCollection => (0, 0),
                _ => (AnchorIndex, 30),
            };
        }

        /// <summary>The VSP's own results where it fails by design.</summary>
        private static class Vsp
        {
            // Removing the first realized row moves the content (the anchor row leaves the screen) when the heights
            // are not uniform; the other buffer changes keep it.
            public static Outcome BufferChange(BufferChange change, Heights heights) =>
                change == LazyStackPanelTests_CollectionChanges.BufferChange.RemoveFirstRealized && heights != Heights.Uniform
                    ? Outcome.Moved : Outcome.Kept;

            // 5 inserts directly above the anchor are kept; 5 removes directly above it move the content at the 4th
            // remove (the anchor row leaves the screen) when the heights are not uniform, at every cut.
            public static Outcome Repeated(bool insert, double cut, Heights heights) =>
                !insert && heights != Heights.Uniform ? Outcome.Moved : Outcome.Kept;

            // The insert at 0 always moves the content down (uniform +100, periodic +157, regions +40).
            public static Outcome InsertAtZero(Heights heights, double cut) => Outcome.Moved;

            // Same as Lazy.
            public static Outcome Below(bool insert, int relative, Heights heights) => Outcome.Kept;

            // The VSP keeps the next row where it was (it does not take the removed anchor row's slot), except
            // fractional heights with no cut.
            public static Outcome RemoveAnchor(Heights heights, double cut) =>
                heights == Heights.Fractional && cut == 0 ? Outcome.Kept : Outcome.Moved;

            // A replaced anchor with another height keeps the old offset by pixels (taller: top -80, shorter: -10);
            // with periodic heights even the same height moves it (-190). A replace above keeps the content.
            public static Outcome Replace(ReplaceKind kind, Heights heights) => kind switch
            {
                ReplaceKind.RowAbove => Outcome.Kept,
                ReplaceKind.AnchorSameHeight when heights == Heights.Uniform => Outcome.Kept,
                _ => Outcome.Moved,
            };

            // The VSP keeps its Offset after a Reset, so it shows index 20 + 3 px whatever item is there.
            public static (int, double) Duplicates(Duplicates kind) => (20, 3);

            // Uniform heights: a Reset above moves the content, below keeps it; periodic heights: every case moves.
            public static Outcome BigReset(BigReset kind, Heights heights) =>
                heights == Heights.Uniform && kind is LazyStackPanelTests_CollectionChanges.BigReset.InsertBelowOnTheScreen
                    or LazyStackPanelTests_CollectionChanges.BigReset.RemoveBelowOnTheScreen
                    ? Outcome.Kept : Outcome.Moved;

            // Same as Lazy.
            public static bool SeveralSame(Heights heights) => true;

            // An insert at 0 inside the handler moves the content (the tracked row leaves the screen); append is fine.
            public static Outcome ScrollChanged(bool insertAtZero) => insertAtZero ? Outcome.Moved : Outcome.Kept;

            // Same as Lazy.
            public static bool InMeasureKept(InMeasure kind) => true;

            // Inserts above kept; removes above move the content (anchor row gone); detached + insert below moves the
            // anchor row up by 350 px (-30 -> -380).
            public static Outcome Hidden(Away away, HiddenChange change) => (away, change) switch
            {
                (_, HiddenChange.Remove5Above) => Outcome.Moved,
                (Away.Detached, HiddenChange.Insert5Below) => Outcome.Moved,
                _ => Outcome.Kept,
            };

            // A new collection keeps the old Offset (row 402 + 30 px); null then the same collection: row 0.
            public static (int, double) Source(SourceChange kind) => kind switch
            {
                SourceChange.Null => (-1, 0),
                SourceChange.NullThenTheSameCollection => (0, 0),
                _ => (AnchorIndex, 30),
            };
        }

        // ---------------------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------------------

        private static string Format(double? y) => y is { } v ? v.ToString("0.##") : "gone";

        private static bool Near(double? y, double expected) => y is { } v && Math.Abs(v - expected) <= Tolerance;

        /// <summary>
        /// The tracked row is at <paramref name="expected"/> and the harness checks pass (Kept), it is elsewhere
        /// (Moved), or a check failed (Gap). Asserts that this equals <paramref name="want"/>.
        /// </summary>
        private void AssertOutcome(Harness h, Outcome want, Row tracked, double before, double expected, string what)
        {
            var after = h.ScreenY(tracked);
            var actual = h.Problems.Count > 0 ? Outcome.Gap : Near(after, expected) ? Outcome.Kept : Outcome.Moved;
            var text = $"{h.Name} {what}: row {tracked.Id} {before:0.##} -> {Format(after)} (spec {expected:0.##}), " +
                $"Offset {h.Scroll.Offset.Y:0.##}; {string.Join("; ", h.Problems)}";
            _output.WriteLine(text);
            Assert.True(want == actual, $"expected {want}, was {actual}: {text}");
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

        public class Row : INotifyPropertyChanged
        {
            private double _height;

            public Row(int id, double height) => (Id, _height) = (id, height);

            public event PropertyChangedEventHandler? PropertyChanged;

            public int Id { get; }

            public double Height
            {
                get => _height;
                set
                {
                    _height = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Height)));
                }
            }

            /// <summary>Called from the row's MeasureOverride.</summary>
            public Action? OnMeasure { get; set; }

            public override string ToString() => $"Row {Id}";
        }

        /// <summary>An observable list with one-event range removal and a Reset.</summary>
        private sealed class RowList : ResettingCollection<Row>
        {
            public RowList(IEnumerable<Row> items) : base(items) { }

            public void RemoveRange(int index, int count)
            {
                var removed = Items.Skip(index).Take(count).ToList();
                for (var i = 0; i < count; ++i)
                    Items.RemoveAt(index);
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, (IList)removed, index));
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
                _row?.OnMeasure?.Invoke();
                return new Size(100, _row?.Height ?? 0);
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
                Items = new RowList(rows);
                ItemsControl = new ItemsControl { ItemTemplate = new FuncDataTemplate<Row>((_, _) => new RowView(this)) };
                var style = new Style(x => x.OfType<ContentPresenter>())
                {
                    Setters = { new Setter(Layoutable.UseLayoutRoundingProperty, false) },
                };
                (Window, Scroll) = Show(ItemsControl, Items, lazy, cacheLength, 600, style);
                Check("after the first layout");
            }

            public bool Lazy { get; }
            public string Name => Lazy ? "Lazy" : "VSP";
            public RowList Items { get; private set; }
            public ItemsControl ItemsControl { get; }
            public Window Window { get; }
            public ScrollViewer Scroll { get; }
            public Panel Panel => ItemsControl.ItemsPanelRoot!;
            public IItemsScrollInfo Info => ItemsScroll.Get(ItemsControl);
            public double ViewportHeight => Scroll.Viewport.Height;

            /// <summary>Row measures since the start of the last <see cref="Layout"/>.</summary>
            public int Measures { get; set; }

            public int FirstRealizedIndex => ItemsControl.IndexFromContainer(Realized()[0]);

            public List<Control> Realized() => ItemsControl.GetRealizedContainers()?.ToList() ?? new();

            /// <summary>Sets ItemsSource; null leaves <see cref="Items"/> an empty list for the checks.</summary>
            public void SetSource(RowList? items)
            {
                Items = items ?? new RowList(Array.Empty<Row>());
                ItemsControl.ItemsSource = items;
            }

            public void Layout()
            {
                Measures = 0;
                RunLayout(ItemsControl);
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

            /// <summary>Row <see cref="AnchorIndex"/> at the top, <paramref name="cut"/> px of it scrolled out.</summary>
            public void ScrollToAnchor(double cut)
            {
                ScrollToTop(AnchorIndex);
                if (cut > 0)
                    ScrollBy(cut);
                Assert.Equal(-cut, ScreenY(Items[AnchorIndex])!.Value, 1);
            }

            public void ScrollToEnd()
            {
                Scroll.ScrollToEnd();
                Layout();
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

            /// <summary>False: a failed check is only added to <see cref="Problems"/>.</summary>
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
                    if (!c.IsVisible)
                        return $"container at index {index} is not visible (pooled?)";
                    if (prevIndex is { } p && index != p + 1)
                        return $"realized indexes {p} and {index} are not consecutive";
                    prevIndex = index;
                    lastIndex = index;

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
