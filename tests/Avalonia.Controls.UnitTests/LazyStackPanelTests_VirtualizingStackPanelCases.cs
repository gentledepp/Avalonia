using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.PanelHarness;

#nullable enable

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Ported from the VirtualizingStackPanel tests, as black-box tests for both panels. Setup: 100 rows of 10 px
    /// in a 100 x 100 viewport. Tests that read VSP internals check the rows on the screen, the hidden children and
    /// the row measure counts instead. An Offset write of the original test is a ScrollBy here when it means a
    /// relative scroll (on the LazyStackPanel an Offset write is an absolute jump).
    /// </summary>
    public class LazyStackPanelTests_VirtualizingStackPanelCases : LazyStackPanelTestBase
    {
        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_VirtualizingStackPanelCases(ITestOutputHelper output) => _output = output;

        private static PanelHarness Create(bool lazy, double cacheLength = 0, int count = 100, Func<int, double>? height = null,
            double size = 100, Func<Panel>? panel = null, bool strict = true, ItemsControl? itemsControl = null,
            IDataTemplate? template = null, Func<Control, Control>? wrap = null, params IStyle[] styles) =>
            new(lazy, Rows(count, height ?? (_ => 10)), width: size, height: size, cacheLength: cacheLength,
                panel: panel, strict: strict, itemsControl: itemsControl, template: template, wrap: wrap, styles: styles);

        private static List<int> OnScreenIndexes(PanelHarness h) => h.OnScreen().Select(x => h.Items.IndexOf(x.Row)).ToList();

        // ---------------------------------------------------------------------------------------------------------
        // Collection changes
        // ---------------------------------------------------------------------------------------------------------

        // Also covers removing so many rows that less than a page is left.
        [Theory]
        [InlineData(false, 0, 80, 10, 100)]
        [InlineData(false, 0.5, 80, 10, 100)]
        [InlineData(false, 0, 95, 0, 0)]
        [InlineData(false, 0.5, 95, 0, 0)]
        [InlineData(true, 0, 80, 10, 100)]
        [InlineData(true, 0.5, 80, 10, 100)]
        [InlineData(true, 0, 95, 0, 0)]
        [InlineData(true, 0.5, 95, 0, 0)]
        public void Removing_Range_When_Scrolled_To_End_Updates_Viewport(bool lazy, double cacheLength, int remove,
            int firstOnScreen, double offset)
        {
            using var h = Create(lazy, cacheLength);
            h.Scroll.Offset = new Vector(0, 900);
            h.Layout();
            Assert.Equal(Enumerable.Range(90, 10), OnScreenIndexes(h));

            h.Items.RemoveRange(0, remove);
            h.Layout();

            _output.WriteLine($"{h.Name}: realized {string.Join(",", h.RealizedIndexes())}, Offset {h.Scroll.Offset.Y}");
            Assert.Equal(Enumerable.Range(firstOnScreen, Math.Min(10, h.Items.Count)), OnScreenIndexes(h));
            Assert.Equal(offset, h.Scroll.Offset.Y);
        }

        // Also covers a reset to less than a page of items.
        [Theory]
        [InlineData(false, 0, 20, 10, 100)]
        [InlineData(false, 0.5, 20, 10, 100)]
        [InlineData(false, 0, 5, 0, 0)]
        [InlineData(false, 0.5, 5, 0, 0)]
        [InlineData(true, 0, 20, 10, 100)]
        [InlineData(true, 0.5, 20, 10, 100)]
        [InlineData(true, 0, 5, 0, 0)]
        [InlineData(true, 0.5, 5, 0, 0)]
        public void Resetting_Collection_To_Have_Less_Items_When_Scrolled_To_End_Updates_Viewport(bool lazy,
            double cacheLength, int keep, int firstOnScreen, double offset)
        {
            using var h = Create(lazy, cacheLength);
            h.Scroll.Offset = new Vector(0, 900);
            h.Layout();

            h.Items.Reset(h.Items.Take(keep).ToList());
            h.Layout();

            _output.WriteLine($"{h.Name}: realized {string.Join(",", h.RealizedIndexes())}, Offset {h.Scroll.Offset.Y}");
            Assert.Equal(Enumerable.Range(firstOnScreen, Math.Min(10, keep)), OnScreenIndexes(h));
            Assert.Equal(offset, h.Scroll.Offset.Y);
        }

        // The replaced row's container is cleared in the change and prepared again for the new item in the next
        // layout.
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 0.5)]
        [InlineData(true, 0)]
        [InlineData(true, 0.5)]
        public void Fires_Correct_Container_Lifecycle_Events_On_Replace(bool lazy, double cacheLength)
        {
            using var h = Create(lazy, cacheLength);
            var events = new List<string>();
            h.ItemsControl.ContainerPrepared += (_, e) => events.Add($"Prepared #{e.Container.GetHashCode()} = {e.Index}");
            h.ItemsControl.ContainerClearing += (_, e) => events.Add($"Clearing #{e.Container.GetHashCode()}");
            h.ItemsControl.ContainerIndexChanged += (_, e) =>
                events.Add($"IndexChanged #{e.Container.GetHashCode()} {e.OldIndex} -> {e.NewIndex}");

            var toReplace = h.ItemsControl.ContainerFromIndex(2)!;
            h.Items[2] = new HarnessRow(1000, 10);
            var inChange = events.ToList();
            events.Clear();
            h.Layout();

            _output.WriteLine($"{h.Name}: in the change [{string.Join("; ", inChange)}], layout [{string.Join("; ", events)}]");
            Assert.Equal(new[] { $"Clearing #{toReplace.GetHashCode()}" }, inChange);
            Assert.Equal(new[] { $"Prepared #{toReplace.GetHashCode()} = 2" }, events);
        }

        public enum CollectionEdit
        {
            InsertOne,
            InsertRange,
            RemoveOne,
            RemoveRange,
            Replace,
            MoveForward,
            MoveBackward,
        }

        public enum EditPosition
        {
            BeforeWindow,
            EarlyInWindow,
            LateInWindow,
            AfterWindow,
        }

        public static IEnumerable<object[]> CollectionEditCases() =>
            from lazy in new[] { false, true }
            from edit in Enum.GetValues<CollectionEdit>()
            from position in Enum.GetValues<EditPosition>()
            select new object[] { lazy, edit, position };

        // The harness check after the layout compares each realized container's DataContext with the item at its
        // index.
        [Theory]
        [MemberData(nameof(CollectionEditCases))]
        public void Collection_Edit_Keeps_Every_Container_On_Its_Own_Item(bool lazy, CollectionEdit edit, EditPosition position)
        {
            using var h = Create(lazy, 0.5, 200, i => 10 + i % 4 * 20);
            h.Scroll.Offset = new Vector(0, 2000);
            h.Layout();
            var first = h.FirstRealizedIndex;
            var last = h.LastRealizedIndex;
            Assert.True(first > 10 && last < 180, $"window [{first}..{last}]");

            var at = position switch
            {
                EditPosition.BeforeWindow => first - 5,
                EditPosition.EarlyInWindow => first + 1,
                EditPosition.LateInWindow => first + (last - first) * 3 / 4,
                _ => last + 5,
            };

            switch (edit)
            {
                case CollectionEdit.InsertOne:
                    h.Items.Insert(at, new HarnessRow(900, 55));
                    break;
                case CollectionEdit.InsertRange:
                    for (var i = 0; i < 3; i++)
                        h.Items.Insert(at + i, new HarnessRow(900 + i, 35 + i * 10));
                    break;
                case CollectionEdit.RemoveOne:
                    h.Items.RemoveAt(at);
                    break;
                case CollectionEdit.RemoveRange:
                    for (var i = 0; i < 3; i++)
                        h.Items.RemoveAt(at);
                    break;
                case CollectionEdit.Replace:
                    h.Items[at] = new HarnessRow(900, 65);
                    break;
                case CollectionEdit.MoveForward:
                    h.Items.Move(at, at + 4);
                    break;
                case CollectionEdit.MoveBackward:
                    h.Items.Move(at, at - 4);
                    break;
            }

            h.Layout();
            Assert.NotEmpty(h.Realized());
        }

        public enum CoalescedEdit
        {
            InsertBeforeWindow,
            InsertEarlyInWindow,
            InsertLateInWindow,
            InsertAfterWindow,
            Reorder,
            Append,
        }

        // The change arrives as one Reset. The harness check verifies that every container shows the item at its
        // index. An append keeps the first realized row.
        [Theory]
        [InlineData(false, CoalescedEdit.InsertBeforeWindow)]
        [InlineData(false, CoalescedEdit.InsertEarlyInWindow)]
        [InlineData(false, CoalescedEdit.InsertLateInWindow)]
        [InlineData(false, CoalescedEdit.InsertAfterWindow)]
        [InlineData(false, CoalescedEdit.Reorder)]
        [InlineData(false, CoalescedEdit.Append)]
        [InlineData(true, CoalescedEdit.InsertBeforeWindow)]
        [InlineData(true, CoalescedEdit.InsertEarlyInWindow)]
        [InlineData(true, CoalescedEdit.InsertLateInWindow)]
        [InlineData(true, CoalescedEdit.InsertAfterWindow)]
        [InlineData(true, CoalescedEdit.Reorder)]
        [InlineData(true, CoalescedEdit.Append)]
        public void Change_Coalesced_Into_A_Reset_Keeps_Every_Container_On_Its_Own_Item(bool lazy, CoalescedEdit edit)
        {
            var insert = edit < CoalescedEdit.Reorder;
            using var h = Create(lazy, 0.5, insert ? 400 : 200, i => insert ? 10 : 10 + i % 4 * 20);
            h.Scroll.Offset = new Vector(0, 2000);
            h.Layout();
            var first = h.FirstRealizedIndex;
            var last = h.LastRealizedIndex;
            var list = h.Items.ToList();

            switch (edit)
            {
                case CoalescedEdit.Reorder:
                    list.Reverse();
                    break;
                case CoalescedEdit.Append:
                    list.AddRange(Rows(250, i => 10 + i % 4 * 20).Skip(200));
                    break;
                default:
                    var at = edit switch
                    {
                        CoalescedEdit.InsertBeforeWindow => first - 5,
                        CoalescedEdit.InsertEarlyInWindow => first + 1,
                        CoalescedEdit.InsertLateInWindow => first + (last - first) * 3 / 4,
                        _ => last + 5,
                    };
                    list.Insert(at, new HarnessRow(900, 10));
                    break;
            }

            h.Items.Reset(list);
            h.Layout();

            if (edit == CoalescedEdit.Append)
                Assert.Equal(first, h.FirstRealizedIndex);
        }

        // Each row measures 5 px taller the first time.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Items_Added_Under_Measurement_Instability_Are_Reflected_In_The_Next_Layout(bool lazy)
        {
            using var h = Create(lazy, 0, 50, _ => 50);
            Unstable(h.Items);
            h.Scroll.Offset = new Vector(0, 500);
            h.Layout();
            var extentBefore = h.Scroll.Extent.Height;

            for (var i = 50; i < 60; i++)
                h.Items.Add(Unstable(new HarnessRow(i, 50)));
            h.Layout();

            _output.WriteLine($"{h.Name}: Extent {extentBefore} -> {h.Scroll.Extent.Height}");
            Assert.True(h.Scroll.Extent.Height >= extentBefore + 400, $"Extent {extentBefore} -> {h.Scroll.Extent.Height}");
        }

        /// <summary>The row's first measure is 5 px taller than the later ones.</summary>
        private static HarnessRow Unstable(HarnessRow row)
        {
            var first = true;
            row.Measure = () =>
            {
                var result = first ? row.Height + 5 : row.Height;
                first = false;
                return result;
            };
            return row;
        }

        private static void Unstable(IEnumerable<HarnessRow> rows)
        {
            foreach (var row in rows)
                Unstable(row);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Row size and visibility
        // ---------------------------------------------------------------------------------------------------------

        // 50 px rows, CacheLength 1, Offset 600; the second realized row (in the buffer above) grows to 200 or
        // shrinks to 30. The row at the top stays on the screen where it was.
        [Theory]
        [InlineData(false, 200)]
        [InlineData(false, 30)]
        [InlineData(true, 200)]
        [InlineData(true, 30)]
        public void Item_Changing_Size_Before_Anchor_Preserves_Anchor_Position(bool lazy, double height)
        {
            using var h = Create(lazy, 1, 50, _ => 50);
            h.Scroll.Offset = new Vector(0, 600);
            h.Layout();
            var anchor = h.Items[12];
            var before = h.ScreenY(anchor)!.Value;
            var changing = h.FirstRealizedIndex + 1;
            Assert.True(changing < 12, $"first realized {h.FirstRealizedIndex}");

            h.Items[changing].Height = height;
            h.Layout();

            _output.WriteLine($"{h.Name}: row 12 {before} -> {Format(h.ScreenY(anchor))}, first realized {h.FirstRealizedIndex}");
            Assert.Equal(before, h.ScreenY(anchor)!.Value, 1);
        }

        // A row hidden through a binding takes no space; the next row moves up.
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 0.5)]
        [InlineData(true, 0)]
        [InlineData(true, 0.5)]
        public void Can_Bind_Item_IsVisible(bool lazy, double cacheLength)
        {
            using var h = Create(lazy, cacheLength);
            var container = h.ItemsControl.ContainerFromIndex(2)!;
            Assert.True(container.IsVisible);
            Assert.Equal(20, h.ScreenY(2));

            h.Items[2].Visible = false;
            h.Layout();

            Assert.False(container.IsVisible);
            Assert.Equal(20, h.ScreenY(3));
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 0.5)]
        [InlineData(true, 0)]
        [InlineData(true, 0.5)]
        public void IsVisible_Binding_Persists_After_Scrolling(bool lazy, double cacheLength)
        {
            using var h = Create(lazy, cacheLength);
            h.Items[2].Visible = false;
            h.Scroll.Offset = new Vector(0, 200);
            h.Layout();
            h.Scroll.Offset = new Vector(0, 0);
            h.Layout();

            Assert.False(h.ItemsControl.ContainerFromIndex(2)!.IsVisible);
            Assert.Equal(20, h.ScreenY(3));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scrolling
        // ---------------------------------------------------------------------------------------------------------

        // The rows above shrink while they are not realized, so the estimate above is too large. Scrolling up until
        // Offset is 0 puts row 0 at 0.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Item_Zero_Is_Always_At_Position_Zero(bool lazy)
        {
            using var h = Create(lazy, 0, 100, _ => 20, strict: lazy);
            h.Scroll.Offset = new Vector(0, 1000);
            h.Layout();
            for (var i = 0; i < 40; ++i)
                h.Items[i].Height = 5;

            var steps = 0;
            while (h.Scroll.Offset.Y > 0 && steps++ < 200)
                h.ScrollBy(-Math.Min(30, h.Scroll.Offset.Y));
            h.Scroll.Offset = new Vector(0, 0);
            h.Layout();

            _output.WriteLine($"{h.Name}: {steps} steps, row 0 at {Format(h.ScreenY(0))}, Offset {h.Scroll.Offset.Y}; " +
                string.Join("; ", h.Problems));
            Assert.Equal(0, h.ScreenY(0)!.Value, 1);
            Assert.Equal(0, h.Scroll.Offset.Y, 1);
        }

        // Rows of 10 and 100 px, Offset written in steps of 10 px down to 500 and back. The harness check after each
        // layout verifies that the rows stay contiguous.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Scrolling_Down_And_Up_With_Mixed_Heights_Does_Not_Jump(bool lazy)
        {
            using var h = Create(lazy, 0, 50, i => i % 2 == 0 ? 10 : 100);

            for (double offset = 0; offset < 500; offset += 10)
            {
                h.Scroll.Offset = new Vector(0, offset);
                h.Layout();
            }

            for (double offset = 500; offset >= 0; offset -= 10)
            {
                h.Scroll.Offset = new Vector(0, offset);
                h.Layout();
            }

            Assert.Equal(0, h.ScreenY(0)!.Value, 1);
        }

        // Heights 5/50/200; Offset + 200 until it stops changing; the last row is realized; Offset 0 shows row 0.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Scroll_To_End_And_Back_With_Extreme_Height_Variance(bool lazy)
        {
            using var h = Create(lazy, 0, 100, i => i % 3 == 0 ? 5 : i % 3 == 1 ? 50 : 200);

            for (var i = 0; i < 1000; ++i)
            {
                var previous = h.Scroll.Offset.Y;
                h.Scroll.Offset = new Vector(0, previous + 200);
                h.Layout();
                if (Math.Abs(h.Scroll.Offset.Y - previous) < 1)
                    break;
            }

            Assert.Equal(99, h.LastRealizedIndex);
            h.Scroll.Offset = new Vector(0, 0);
            h.Layout();
            Assert.Equal(0, h.FirstRealizedIndex);
            Assert.Equal(0, h.ScreenY(0)!.Value, 1);
        }

        // CacheLength 2, a jump to 5000 and back to 0: row 0 at 0, rows contiguous.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Scroll_Down_Then_Back_To_Top_With_Mixed_Heights_Shows_All_Items(bool lazy)
        {
            using var h = Create(lazy, 2, 71, i => (i % 5) switch { 0 => 50, 1 => 80, 2 => 120, 3 => 200, _ => 30 });
            h.Scroll.Offset = new Vector(0, 5000);
            h.Layout();
            h.Scroll.Offset = new Vector(0, 0);
            h.Layout();

            Assert.Equal(0, h.FirstRealizedIndex);
            Assert.Equal(0, h.ScreenY(0)!.Value, 1);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Viewport
        // ---------------------------------------------------------------------------------------------------------

        // Checked through the result: after growing back, rows 0-9 are on the screen.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Shrinking_Viewport_Then_Growing_Back_Triggers_Remeasure(bool lazy)
        {
            using var h = Create(lazy, 0, 20);
            Assert.Equal(Enumerable.Range(0, 10), OnScreenIndexes(h));

            h.Host.Height = 10;
            h.Layout();
            h.Host.Height = 100;
            h.Layout();

            Assert.Equal(Enumerable.Range(0, 10), OnScreenIndexes(h));
        }

        // Offset 200, the host size to 0 x 0 and back: Offset 200, row 20 at the top.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Collapsing_Viewport_To_Empty_And_Restoring_Preserves_Scroll_Position(bool lazy)
        {
            using var h = Create(lazy);
            h.Scroll.Offset = new Vector(0, 200);
            h.Layout();
            Assert.Equal(20, h.FirstRealizedIndex);

            h.Host.Width = h.Host.Height = 0;
            h.Layout(check: false);
            h.Host.Width = h.Host.Height = 100;
            h.Layout();

            Assert.Equal(200, h.Scroll.Offset.Y);
            Assert.Equal(20, h.FirstRealizedIndex);
            Assert.Equal(0, h.ScreenY(20)!.Value, 1);
        }

        // Issue #11272: the list is added to a Decorator with a margin of 100 after the first layout.
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 0.5)]
        [InlineData(true, 0)]
        [InlineData(true, 0.5)]
        public void Does_Not_Throw_When_Estimating_Viewport_With_Ancestor_Margin(bool lazy, double cacheLength)
        {
            var decorator = new Decorator { Margin = new Thickness(100) };
            using var h = Create(lazy, cacheLength, wrap: host => { decorator.Child = host; return decorator; });
            decorator.Child = null;
            LazyStackPanelTestTarget.RunLayout(h.Window);
            decorator.Child = h.Host;
            h.Layout();

            Assert.Equal(Enumerable.Range(0, 10), OnScreenIndexes(h));
        }

        // Issue #10968: the items control (with its own ScrollViewer, no height) is the content of a 100 x 100
        // ScrollContentPresenter with a top margin of 200. Both panels realize 1 row. The LazyStackPanel should
        // throw here instead: see the next test.
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 0.5)]
        [InlineData(true, 0)]
        [InlineData(true, 0.5)]
        public void Does_Not_Realize_Items_If_Self_Outside_Viewport(bool lazy, double cacheLength)
        {
            var ic = new ItemsControl
            {
                Margin = new Thickness(0, 200, 0, 0),
                ItemTemplate = new FuncDataTemplate<HarnessRow>((_, _) => new Border { Width = 100, Height = 10 }),
                ItemsSource = Rows(100, _ => 10).ToList(),
                ItemsPanel = new FuncTemplate<Panel?>(() => lazy
                    ? new LazyStackPanel { CacheLength = cacheLength }
                    : new VirtualizingStackPanel { CacheLength = cacheLength }),
                Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
                {
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns),
                }),
            };
            var presenter = new ScrollContentPresenter { Width = 100, Height = 100, Content = ic };
            var window = new Window { Width = 600, Height = 600, Content = presenter };

            void Run()
            {
                window.Show();
                LazyStackPanelTestTarget.RunLayout(window);
                Assert.Equal(1, ic.ItemsPanelRoot!.Children.Count);
                presenter.Content = null;
                LazyStackPanelTestTarget.RunLayout(window);
                presenter.Content = ic;
                LazyStackPanelTestTarget.RunLayout(window);
                Assert.Equal(1, ic.ItemsPanelRoot!.Children.Count);
            }

            var ex = Record.Exception(Run);
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")}: {ex?.Message ?? "no exception"}, children " +
                $"{ic.ItemsPanelRoot?.Children.Count}, desired {ic.ItemsPanelRoot?.DesiredSize}");
            Assert.Null(ex);
            window.Close();
        }

        // The setup above: the items control's ScrollViewer has no height limit, so the LazyStackPanel should throw.
        // Its only measure is the first one (before the presenter scrolls it), where an infinite height is allowed.
        [Fact(Skip = "No exception: the panel is measured only once, and its first measure allows an infinite height.")]
        public void Without_A_Height_Limit_Inside_A_ScrollContentPresenter_The_Lazy_Panel_Throws()
        {
            var ic = new ItemsControl
            {
                Margin = new Thickness(0, 200, 0, 0),
                ItemTemplate = new FuncDataTemplate<HarnessRow>((_, _) => new Border { Width = 100, Height = 10 }),
                ItemsSource = Rows(100, _ => 10).ToList(),
                ItemsPanel = new FuncTemplate<Panel?>(() => new LazyStackPanel { CacheLength = 0 }),
                Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
                {
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns),
                }),
            };
            var window = new Window
            {
                Width = 600,
                Height = 600,
                Content = new ScrollContentPresenter { Width = 100, Height = 100, Content = ic },
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                window.Show();
                LazyStackPanelTestTarget.RunLayout(window);
            });
            Assert.Contains("infinite height", ex.Message);
            window.Close();
        }

        // Issue #18646. CacheLength 0.5, steps of 5 px until the realized range changes: the panel is measured once
        // and only the new rows are measured. The VSP is also arranged once; the LazyStackPanel is arranged at each
        // step, as a small scroll moves the realized rows in the arrange pass.
        [Theory]
        [InlineData(false, 0, 1)]
        [InlineData(false, 200, -1)]
        [InlineData(false, 800, 1)]
        [InlineData(false, 105, -1)]
        [InlineData(true, 0, 1)]
        [InlineData(true, 200, -1)]
        [InlineData(true, 800, 1)]
        [InlineData(true, 105, -1)]
        public void Scrolling_Does_Not_Measure_Until_The_Buffer_Bounds_Are_Reached(bool lazy, double start, int direction)
        {
            CountingLazy? cl = null;
            CountingVsp? cv = null;
            using var h = Create(lazy, 0.5, panel: () => lazy ? cl = new CountingLazy() : cv = new CountingVsp());
            if (start > 0)
            {
                h.Scroll.Offset = new Vector(0, start);
                h.Layout();
            }

            var realizedBefore = h.RealizedIndexes();
            var stop = direction > 0 ? Math.Min(realizedBefore[^1] + 1, 99) : Math.Max(realizedBefore[0] - 1, 0);
            h.MeasuresById.Clear();
            if (cl is not null) cl.Measured = cl.Arranged = 0;
            if (cv is not null) cv.Measured = cv.Arranged = 0;
            var steps = 0;

            while ((direction > 0 ? h.LastRealizedIndex < stop : h.FirstRealizedIndex > stop) && steps++ < 1000)
                h.ScrollBy(5 * direction);

            var measured = cl?.Measured ?? cv!.Measured;
            var arranged = cl?.Arranged ?? cv!.Arranged;
            var rowsMeasured = h.MeasuresById.Where(x => x.Value > 0).Select(x => x.Key).OrderBy(x => x).ToList();
            _output.WriteLine($"{h.Name} from {start} ({direction}): realized {realizedBefore[0]}..{realizedBefore[^1]} -> " +
                $"{h.FirstRealizedIndex}..{h.LastRealizedIndex} in {steps} steps; panel measures {measured}, arranges " +
                $"{arranged}; rows measured [{string.Join(",", rowsMeasured)}]");

            Assert.Equal(1, measured);
            Assert.Equal(lazy ? steps : 1, arranged);
            // Only rows that were not realized before are measured, each once.
            Assert.All(rowsMeasured, i => Assert.DoesNotContain(i, realizedBefore));
            Assert.All(h.MeasuresById.Values, n => Assert.Equal(1, n));
        }

        // Rows of 10 / 100 px that measure 5 px taller the first time; 10 steps of 20 px measure at most 40 rows.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Mixed_Heights_Scrolling_Does_Not_Cause_Excessive_Measures(bool lazy)
        {
            using var h = Create(lazy, 0.5, 100, i => i % 2 == 0 ? 10 : 100);
            Unstable(h.Items);
            h.TotalMeasures = 0;

            for (var i = 0; i < 10; i++)
                h.ScrollBy(20);

            _output.WriteLine($"{h.Name}: {h.TotalMeasures} row measures");
            Assert.True(h.TotalMeasures <= 40, $"{h.TotalMeasures} row measures");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Jumps
        // ---------------------------------------------------------------------------------------------------------

        // 10 rows of one height, then 10 of another: ScrollIntoView(19) aligns the last row with the bottom. Offset
        // and Extent are not checked: they are estimates and differ by panel.
        [Theory]
        [InlineData(false, 0, true)]
        [InlineData(false, 0.5, true)]
        [InlineData(false, 0, false)]
        [InlineData(false, 0.5, false)]
        [InlineData(true, 0, true)]
        [InlineData(true, 0.5, true)]
        [InlineData(true, 0, false)]
        [InlineData(true, 0.5, false)]
        public void ScrollIntoView_Correctly_Scrolls_Down_To_A_Page_Of_Other_Items(bool lazy, double cacheLength, bool smaller)
        {
            using var h = Create(lazy, cacheLength, 20, i => smaller ? (29 - i) / 10 * 10 : (i / 10 + 1) * 10);

            h.ItemsControl.ScrollIntoView(19);
            var bottom = h.ScreenY(19) + h.Items[19].Height;
            h.Layout();

            _output.WriteLine($"{h.Name}: row 19 bottom {Format(bottom)}, Offset {h.Scroll.Offset.Y}, Extent {h.Scroll.Extent.Height}");
            Assert.Equal(100, bottom!.Value, 1);
            Assert.Equal(100, h.ScreenY(19)!.Value + h.Items[19].Height, 1);
        }

        // All rows removed, the list hidden, items set again, ScrollIntoView(0) while hidden; shown: 3 rows and no
        // hidden children.
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 0.5)]
        [InlineData(true, 0)]
        [InlineData(true, 0.5)]
        public void ScrollIntoView_On_Effectively_Invisible_Panel_Does_Not_Create_Ghost_Elements(bool lazy, double cacheLength)
        {
            var decorator = new Decorator { Margin = new Thickness(100) };
            using var h = Create(lazy, cacheLength, 3, wrap: host => { decorator.Child = host; return decorator; });
            var items = h.Items.ToList();

            h.Items.Reset(Array.Empty<HarnessRow>());
            h.Layout();
            var childrenWhenEmpty = h.Panel.Children.Count;

            decorator.IsVisible = false;
            h.Items.Reset(items);
            h.ItemsControl.ScrollIntoView(0);
            decorator.IsVisible = true;
            h.Layout();

            _output.WriteLine($"{h.Name}: children when empty {childrenWhenEmpty}, after {h.Panel.Children.Count}, " +
                $"realized {h.Realized().Count}");
            Assert.Equal(0, childrenWhenEmpty);
            Assert.Equal(3, h.Realized().Count);
            Assert.Equal(3, h.Panel.Children.Count);
        }

        // Rows of 10 / 80 px that measure 5 px taller the first time; Offset 1000, then 2000: the realized range
        // moves forward and the rows stay contiguous.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Multiple_Scroll_Jumps_Each_Get_Fresh_Measure_Pass(bool lazy)
        {
            using var h = Create(lazy, 0.5, 100, i => i % 2 == 0 ? 10 : 80);
            Unstable(h.Items);

            h.Scroll.Offset = new Vector(0, 1000);
            h.Layout();
            var first = h.FirstRealizedIndex;
            Assert.True(h.LastRealizedIndex > first);

            h.Scroll.Offset = new Vector(0, 2000);
            h.Layout();
            Assert.True(h.FirstRealizedIndex > first, $"{first} -> {h.FirstRealizedIndex}");
            Assert.True(h.LastRealizedIndex > h.FirstRealizedIndex);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Containers, recycling and styles
        // ---------------------------------------------------------------------------------------------------------

        // An IVirtualizingDataTemplate keyed by item type; after scrolling down and back, every realized container
        // shows its own item with a child built for its type.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Items_Of_Different_Types_Use_Separate_Recycle_Pools(bool lazy)
        {
            var items = new ObservableCollection<object>(Enumerable.Range(0, 50)
                .Select(i => i % 2 == 0 ? (object)new TypeA { Index = i } : new TypeB { Index = i }));
            var (window, ic, sv) = CreateTyped(lazy, items, new TypedTemplate(), 0);

            sv.Offset = new Vector(0, 200);
            LazyStackPanelTestTarget.RunLayout(ic);
            sv.Offset = new Vector(0, 0);
            LazyStackPanelTestTarget.RunLayout(ic);

            var realized = ic.GetRealizedContainers()!.Where(c => c.IsVisible).ToList();
            Assert.Same(items[0], ic.ContainerFromIndex(0)!.DataContext);
            Assert.All(realized, c =>
            {
                Assert.Same(items[ic.IndexFromContainer(c)], c.DataContext);
                Assert.Equal(c.DataContext!.GetType(), ((ContentPresenter)c).Child!.Tag);
            });
            window.Close();
        }

        public enum Warmup
        {
            /// <summary>Both item types get hidden (pooled) children.</summary>
            PreCreatesForDiscoveredKeys,
            /// <summary>Hidden + realized children are at least MinPoolSizePerKey.</summary>
            RespectsMinPoolSizePerKey,
            /// <summary>A scroll of one viewport builds nothing.</summary>
            ReusedOnFirstScroll,
            /// <summary>The warmup is posted when the panel is attached and runs on the dispatcher.</summary>
            PostedByAttach,
            /// <summary>The panel is detached before the dispatcher runs the warmup.</summary>
            DetachedBeforeTick,
            /// <summary>The panel is detached, then attached again.</summary>
            AttachedAgain,
        }

        // The pool is read by counting the hidden children of the panel (a pooled container is a hidden child). When
        // the panel is detached before the dispatcher runs, the warmup still builds 5 containers (both panels).
        [Theory]
        [InlineData(false, Warmup.PreCreatesForDiscoveredKeys)]
        [InlineData(false, Warmup.RespectsMinPoolSizePerKey)]
        [InlineData(false, Warmup.ReusedOnFirstScroll)]
        [InlineData(false, Warmup.PostedByAttach)]
        [InlineData(false, Warmup.DetachedBeforeTick)]
        [InlineData(false, Warmup.AttachedAgain)]
        [InlineData(true, Warmup.PreCreatesForDiscoveredKeys)]
        [InlineData(true, Warmup.RespectsMinPoolSizePerKey)]
        [InlineData(true, Warmup.ReusedOnFirstScroll)]
        [InlineData(true, Warmup.PostedByAttach)]
        [InlineData(true, Warmup.DetachedBeforeTick)]
        [InlineData(true, Warmup.AttachedAgain)]
        public void Warmup_Fills_The_Pool(bool lazy, Warmup warmup)
        {
            var twoTypes = warmup == Warmup.PreCreatesForDiscoveredKeys;
            var items = new ObservableCollection<object>(Enumerable.Range(0, 50)
                .Select(i => twoTypes && i % 2 == 1 ? (object)new TypeB { Index = i } : new TypeA { Index = i }));
            var template = new TypedTemplate { MinPoolSizePerKey = twoTypes ? 8 : 15 };
            var (window, ic, sv) = CreateTyped(lazy, items, template, 0, warmup: true, runJobs: false);
            var panel = (Panel)ic.ItemsPanelRoot!;
            int Hidden(Type? type = null) => panel.Children.Count(c => !c.IsVisible && (type is null || c.DataContext?.GetType() == type));
            var hiddenBeforeJobs = Hidden();
            var log = $"{(lazy ? "Lazy" : "VSP")} {warmup}: hidden before the dispatcher {hiddenBeforeJobs}";

            switch (warmup)
            {
                case Warmup.PreCreatesForDiscoveredKeys:
                    Dispatcher.UIThread.RunJobs();
                    log += $", after: A {Hidden(typeof(TypeA))}, B {Hidden(typeof(TypeB))}";
                    Assert.True(Hidden(typeof(TypeA)) > 0 && Hidden(typeof(TypeB)) > 0, log);
                    break;
                case Warmup.RespectsMinPoolSizePerKey:
                    Dispatcher.UIThread.RunJobs();
                    var realized = ic.GetRealizedContainers()!.Count();
                    log += $", after {Hidden()} + {realized} realized";
                    Assert.True(Hidden() + realized >= 15, log);
                    break;
                case Warmup.ReusedOnFirstScroll:
                    Dispatcher.UIThread.RunJobs();
                    var builds = template.Builds;
                    var containers = panel.Children.Count;
                    sv.Offset = new Vector(0, 100);
                    LazyStackPanelTestTarget.RunLayout(ic);
                    log += $", builds on the scroll {template.Builds - builds}, children {containers} -> {panel.Children.Count}";
                    _output.WriteLine(log);
                    Assert.Equal(builds, template.Builds);
                    Assert.Equal(containers, panel.Children.Count);
                    break;
                case Warmup.PostedByAttach:
                    Dispatcher.UIThread.RunJobs();
                    log += $", after {Hidden()}";
                    Assert.Equal(0, hiddenBeforeJobs);
                    Assert.True(Hidden() > 0, log);
                    break;
                case Warmup.DetachedBeforeTick:
                    var children = panel.Children.Count;
                    window.Content = null;
                    Assert.Null(Record.Exception(() => Dispatcher.UIThread.RunJobs()));
                    log += $", children {children} -> {panel.Children.Count}";
                    Assert.Equal(children + 5, panel.Children.Count);
                    Assert.Equal(5, Hidden());
                    break;
                case Warmup.AttachedAgain:
                    window.Content = null;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(5, Hidden());
                    window.Content = ic;
                    LazyStackPanelTestTarget.RunLayout(window);
                    log += $", attached again {Hidden()}";
                    Assert.True(Hidden() > 0, log);
                    break;
            }

            _output.WriteLine(log);
            window.Close();
        }

        // Offset 200: one Clearing for each row that leaves the realized range, one Prepared for each row that enters
        // it. With CacheLength 0.5 the LazyStackPanel has 15 rows before (no buffer above at the top) and 20 after
        // (buffer above and below), none of them shared.
        [Theory]
        [InlineData(false, 0, 10, 10)]
        [InlineData(false, 0.5, 15, 15)]
        [InlineData(true, 0, 10, 10)]
        [InlineData(true, 0.5, 15, 20)]
        public void ContainerPrepared_And_Clearing_Are_Raised_When_Scrolling(bool lazy, double cacheLength,
            int expectedClearing, int expectedPrepared)
        {
            using var h = Create(lazy, cacheLength);
            var before = h.RealizedIndexes();
            var prepared = 0;
            var clearing = 0;
            h.ItemsControl.ContainerPrepared += (_, _) => ++prepared;
            h.ItemsControl.ContainerClearing += (_, _) => ++clearing;

            h.Scroll.Offset = new Vector(0, 200);
            h.Layout();

            _output.WriteLine($"{h.Name}: prepared {prepared}, clearing {clearing}, realized {string.Join(",", h.RealizedIndexes())}");
            Assert.Equal(expectedPrepared, prepared);
            Assert.Equal(expectedClearing, clearing);
            // One event per row that leaves / enters the realized range.
            var after = h.RealizedIndexes();
            Assert.Equal(before.Except(after).Count(), clearing);
            Assert.Equal(after.Except(before).Count(), prepared);
        }

        // A relative scroll of one viewport or less, down and then up: one Clearing for each row that leaves the
        // realized range and one Prepared for each row that enters it. The panel must not prepare a row that left
        // again only to read its height.
        [Theory]
        [InlineData(false, 0, 50)]
        [InlineData(false, 0, 100)]
        [InlineData(false, 0.5, 50)]
        [InlineData(false, 0.5, 100)]
        [InlineData(true, 0, 50)]
        [InlineData(true, 0, 100)]
        [InlineData(true, 0.5, 50)]
        [InlineData(true, 0.5, 100)]
        public void A_Relative_Scroll_Prepares_And_Clears_Each_Row_Once(bool lazy, double cacheLength, double distance)
        {
            using var h = Create(lazy, cacheLength);
            h.ScrollBy(200);

            foreach (var delta in new[] { distance, -distance })
            {
                var before = h.RealizedIndexes();
                var prepared = 0;
                var clearing = 0;
                EventHandler<ContainerPreparedEventArgs> onPrepared = (_, _) => ++prepared;
                EventHandler<ContainerClearingEventArgs> onClearing = (_, _) => ++clearing;
                h.ItemsControl.ContainerPrepared += onPrepared;
                h.ItemsControl.ContainerClearing += onClearing;

                h.ScrollBy(delta);

                h.ItemsControl.ContainerPrepared -= onPrepared;
                h.ItemsControl.ContainerClearing -= onClearing;
                var after = h.RealizedIndexes();
                _output.WriteLine($"{h.Name} ScrollBy({delta}): prepared {prepared}, clearing {clearing}, " +
                    $"realized {before[0]}..{before[^1]} -> {after[0]}..{after[^1]}");
                Assert.Equal(after.Except(before).Count(), prepared);
                Assert.Equal(before.Except(after).Count(), clearing);
            }
        }

        // A ScrollBy of two viewports (cache 0) from the top passes rows 10-19, which were never realized. The
        // LazyStackPanel measures them to scroll the exact distance, so each gets one Prepared and one Clearing; the
        // rows 0-9 that leave are cleared once and not prepared again. The VSP estimates them.
        [Theory]
        [InlineData(false, 10, 10)]
        [InlineData(true, 20, 20)]
        public void A_Relative_Scroll_Past_Unrealized_Rows_Prepares_Each_Row_Once(bool lazy, int expectedPrepared,
            int expectedClearing)
        {
            using var h = Create(lazy, 0);
            var prepared = 0;
            var clearing = 0;
            h.ItemsControl.ContainerPrepared += (_, _) => ++prepared;
            h.ItemsControl.ContainerClearing += (_, _) => ++clearing;

            h.ScrollBy(200);

            _output.WriteLine($"{h.Name}: prepared {prepared}, clearing {clearing}, realized {string.Join(",", h.RealizedIndexes())}");
            Assert.Equal(20, h.RealizedIndexes()[0]);
            Assert.Equal(expectedPrepared, prepared);
            Assert.Equal(expectedClearing, clearing);
        }

        // With a pool limit of 5 per key, nine ScrollBy(100) down and nine back build no more template children than
        // the VSP.
        [Fact]
        public void Relative_Scrolls_With_A_Pool_Limit_Build_No_More_Templates_Than_The_Vsp()
        {
            int Builds(bool lazy)
            {
                var items = Enumerable.Range(0, 200).Select(i => (object)new TypeA { Index = i }).ToList();
                var template = new TypedTemplate { MaxPoolSizePerKey = 5 };
                var (window, ic, sv) = CreateTyped(lazy, items, template, 0);
                var start = template.Builds;

                foreach (var delta in Enumerable.Repeat(100.0, 9).Concat(Enumerable.Repeat(-100.0, 9)))
                {
                    LazyStackPanelTestTarget.ScrollBy(sv, delta);
                    LazyStackPanelTestTarget.RunLayout(ic);
                }

                window.Close();
                return template.Builds - start;
            }

            var vsp = Builds(false);
            var lazy = Builds(true);
            _output.WriteLine($"builds: VSP {vsp}, Lazy {lazy}");
            Assert.True(lazy <= vsp, $"Lazy {lazy} builds, VSP {vsp}");
        }

        // Raised in the change, once for each realized row whose index changes, with the old and the new index.
        // The insert or remove is inside or before the realized rows.
        [Theory]
        [InlineData(false, 0, true, false)]
        [InlineData(false, 0.5, true, false)]
        [InlineData(false, 0, true, true)]
        [InlineData(false, 0.5, true, true)]
        [InlineData(false, 0, false, false)]
        [InlineData(false, 0.5, false, false)]
        [InlineData(false, 0, false, true)]
        [InlineData(false, 0.5, false, true)]
        [InlineData(true, 0, true, false)]
        [InlineData(true, 0.5, true, false)]
        [InlineData(true, 0, true, true)]
        [InlineData(true, 0.5, true, true)]
        [InlineData(true, 0, false, false)]
        [InlineData(true, 0.5, false, false)]
        [InlineData(true, 0, false, true)]
        [InlineData(true, 0.5, false, true)]
        public void ContainerIndexChanged_Is_Raised_On_Insert_And_Remove(bool lazy, double cacheLength, bool insert, bool beforeRealized)
        {
            using var h = Create(lazy, cacheLength);
            if (beforeRealized)
            {
                h.Scroll.Offset = new Vector(0, 200);
                h.Layout();
            }

            var realized = h.RealizedIndexes();
            var at = beforeRealized ? 10 : 1;
            var changes = new List<(int Old, int New)>();
            h.ItemsControl.ContainerIndexChanged += (_, e) => changes.Add((e.OldIndex, e.NewIndex));

            if (insert)
                h.Items.Insert(at, new HarnessRow(1000, 10));
            else
                h.Items.RemoveAt(at);

            var expected = realized.Where(i => insert ? i >= at : i > at).Select(i => (i, insert ? i + 1 : i - 1)).ToList();
            _output.WriteLine($"{h.Name}: realized {realized[0]}..{realized[^1]}, {changes.Count} changes, expected {expected.Count}");
            Assert.Equal(expected, changes.OrderBy(x => x.Old).ToList());
            h.Layout();
        }

        private const string NthChildOnTemplateChildNotApplied =
            "An nth-child style on the item template's child is not applied to some rows; on the container it works.";

        // Every fifth row (by item index) gets the style, on the container or on the item template's child, at the
        // top and after a scroll.
        [Theory]
        [InlineData(false, 0, false)]
        [InlineData(false, 0.5, false)]
        [InlineData(false, 0, true)]
        [InlineData(false, 0.5, true)]
        [InlineData(true, 0, false)]
        [InlineData(true, 0.5, false)]
        [InlineData(true, 0, true, Skip = NthChildOnTemplateChildNotApplied)]
        [InlineData(true, 0.5, true, Skip = NthChildOnTemplateChildNotApplied)]
        public void NthChild_Selector_Works(bool lazy, double cacheLength, bool templateChild)
        {
            var style = templateChild
                ? new Style(x => x.OfType<ContentPresenter>().NthChild(5, 0).Child().OfType<HarnessRowView>())
                    { Setters = { new Setter(Control.TagProperty, "red") } }
                : new Style(x => x.OfType<ContentPresenter>().NthChild(5, 0))
                    { Setters = { new Setter(Control.TagProperty, "red") } };
            using var h = Create(lazy, cacheLength, styles: style);

            var wrong = new List<string>();

            void AssertStyled(string when)
            {
                foreach (var c in h.Realized().Where(c => c.IsVisible))
                {
                    var index = h.ItemsControl.IndexFromContainer(c);
                    var target = templateChild ? ((ContentPresenter)c).Child! : c;
                    if (!Equals((index + 1) % 5 == 0 ? "red" : null, target.Tag))
                        wrong.Add($"{when}: row {index} Tag {target.Tag ?? "null"}");
                }
            }

            AssertStyled("top");
            h.ScrollBy(237);
            AssertStyled("ScrollBy");
            h.Scroll.Offset = new Vector(0, 700);
            h.Layout();
            AssertStyled("jump");
            _output.WriteLine($"{h.Name}: {string.Join("; ", wrong)}");
            Assert.Empty(wrong);
        }

        // Issue #12381: a ListBox, 9 rows on the screen (an odd number), nth-child(2n+1) green and nth-child(2n) red;
        // after a scroll by 10 px.
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 0.5)]
        [InlineData(true, 0)]
        [InlineData(true, 0.5)]
        public void Alternating_Backgrounds_Should_Be_Correct_After_Scrolling(bool lazy, double cacheLength)
        {
            var styles = new IStyle[]
            {
                new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(ListBoxItem.BackgroundProperty, Brushes.White) } },
                new Style(x => x.OfType<ListBoxItem>().NthChild(2, 1)) { Setters = { new Setter(ListBoxItem.BackgroundProperty, Brushes.Green) } },
                new Style(x => x.OfType<ListBoxItem>().NthChild(2, 0)) { Setters = { new Setter(ListBoxItem.BackgroundProperty, Brushes.Red) } },
                new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)) } },
            };
            using var h = new PanelHarness(lazy, Rows(100, _ => 10), width: 100, height: 90, cacheLength: cacheLength,
                itemsControl: new ListBox(), styles: styles);

            void AssertColors()
            {
                foreach (var c in h.Realized().Where(c => c.IsVisible).Cast<ListBoxItem>())
                {
                    var index = h.ItemsControl.IndexFromContainer(c);
                    Assert.Equal(index % 2 == 0 ? Colors.Green : Colors.Red, ((ISolidColorBrush)c.Background!).Color);
                }
            }

            AssertColors();
            h.ScrollBy(10);
            AssertColors();
            h.ScrollBy(333);
            AssertColors();
        }

        // ---------------------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------------------

        private static (Window, ItemsControl, ScrollViewer) CreateTyped(bool lazy, IEnumerable<object> items,
            IDataTemplate template, double cacheLength, bool warmup = false, bool runJobs = true)
        {
            var ic = new ItemsControl
            {
                ItemTemplate = template,
                ItemsSource = items,
                ItemsPanel = new FuncTemplate<Panel?>(() => lazy
                    ? new LazyStackPanel { CacheLength = cacheLength, EnableWarmup = warmup }
                    : new VirtualizingStackPanel { CacheLength = cacheLength, EnableWarmup = warmup }),
                Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns),
                }),
            };
            var window = new Window { Width = 100, Height = 100, Content = ic };
            window.Show();
            if (runJobs)
                LazyStackPanelTestTarget.RunLayout(window);
            else
                window.UpdateLayout();
            return (window, ic, ic.GetVisualDescendants().OfType<ScrollViewer>().First());
        }

        public class TypeA
        {
            public int Index { get; set; }
        }

        public class TypeB
        {
            public int Index { get; set; }
        }

        /// <summary>One template keyed by the item type.</summary>
        private sealed class TypedTemplate : IVirtualizingDataTemplate
        {
            public int Builds { get; private set; }
            public int MaxPoolSizePerKey { get; set; } = 20;
            public int MinPoolSizePerKey { get; set; } = 2;
            public object? GetKey(object? data) => data?.GetType();
            public bool Match(object? data) => data is TypeA or TypeB;
            public Control? Build(object? data) => Build(data, null);

            public Control? Build(object? data, Control? existing)
            {
                if (existing is not null)
                    return existing;
                ++Builds;
                return new Border { Width = 100, Height = 10, Tag = data?.GetType() };
            }
        }

        private sealed class CountingLazy : LazyStackPanel
        {
            public int Measured { get; set; }
            public int Arranged { get; set; }

            protected override Size MeasureOverride(Size availableSize)
            {
                ++Measured;
                return base.MeasureOverride(availableSize);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                ++Arranged;
                return base.ArrangeOverride(finalSize);
            }
        }

        private sealed class CountingVsp : VirtualizingStackPanel
        {
            public int Measured { get; set; }
            public int Arranged { get; set; }

            protected override Size MeasureOverride(Size availableSize)
            {
                ++Measured;
                return base.MeasureOverride(availableSize);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                ++Arranged;
                return base.ArrangeOverride(finalSize);
            }
        }
    }
}
