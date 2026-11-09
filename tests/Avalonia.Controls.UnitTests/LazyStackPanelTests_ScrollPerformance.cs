using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// A scroll step that keeps the realized rows does not measure the panel (as with the VSP), and the reported
    /// Extent does not follow every row that enters or leaves the realized range.
    /// </summary>
    public class LazyStackPanelTests_ScrollPerformance : LazyStackPanelTestBase
    {
        // Periodic heights of 40-160 px.
        private static double Height(int i) => 40 + (i * 37 % 4) * 40;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_Small_Scroll_Inside_The_Realized_Rows_Does_Not_Measure_The_Panel(bool lazy)
        {
            var (window, sv, ic, counts) = ShowCounting(lazy, Enumerable.Range(0, 100).Select(_ => 40.0), size: 100);
            var row1 = ic.ContainerFromIndex(1)!;
            var before = row1.TranslatePoint(default, sv)!.Value.Y;

            counts.Reset();
            ScrollBy(sv, 5);
            RunLayout(ic);

            Assert.Equal(0, counts.Measures);
            Assert.Equal(before - 5, row1.TranslatePoint(default, sv)!.Value.Y, 3);
            window.Close();
        }

        [Fact]
        public void Many_Small_Scroll_Steps_Measure_The_Panel_Rarely()
        {
            var (window, sv, ic, counts) = ShowCounting(lazy: true, Enumerable.Range(0, 2000).Select(Height), size: 400);
            const int steps = 400;

            counts.Reset();
            for (var i = 0; i < steps; ++i)
            {
                ScrollBy(sv, 10);
                RunLayout(ic);
            }

            // A measure is only needed when a row enters or leaves the buffer.
            Assert.True(counts.Measures < steps / 4, $"{counts.Measures} panel measures for {steps} steps");
            window.Close();
        }

        // The fast path must give the same rows as the full layout.
        [Fact]
        public void Small_Steps_Give_The_Same_Rows_As_One_Large_Step()
        {
            var heights = Enumerable.Range(0, 300).Select(Height).ToList();
            var (window1, sv1, ic1, _) = ShowCounting(lazy: true, heights, size: 400);
            var (window2, sv2, ic2, _) = ShowCounting(lazy: true, heights, size: 400);

            for (var i = 0; i < 150; ++i)
            {
                ScrollBy(sv1, i % 3 == 2 ? -7 : 13);
                RunLayout(ic1);
            }

            ScrollBy(sv2, 100 * 13 - 50 * 7);
            RunLayout(ic2);

            var scroll1 = ItemsScroll.Get(ic1);
            var scroll2 = ItemsScroll.Get(ic2);
            Assert.Equal(scroll2.FirstVisibleIndex, scroll1.FirstVisibleIndex);
            Assert.Equal(scroll2.FirstVisibleOffset, scroll1.FirstVisibleOffset, 6);
            Assert.Equal(scroll2.LastVisibleIndex, scroll1.LastVisibleIndex);
            Assert.Equal(Realized(ic2), Realized(ic1));
            foreach (var i in Realized(ic1))
                Assert.Equal(ic2.ContainerFromIndex(i)!.Bounds.Top, ic1.ContainerFromIndex(i)!.Bounds.Top, 6);
            Assert.Equal(sv2.Offset.Y, sv1.Offset.Y, 6);
            window1.Close();
            window2.Close();
        }

        [Fact]
        public void Extent_Changes_Rarely_While_Scrolling_Mixed_Heights()
        {
            var (window, sv, ic, _) = ShowCounting(lazy: true, Enumerable.Range(0, 2000).Select(Height), size: 400);
            var changes = 0;
            var small = 0;
            sv.PropertyChanged += (_, e) =>
            {
                if (e.Property != ScrollViewer.ExtentProperty)
                    return;
                ++changes;
                var (oldExtent, newExtent) = (((Size)e.OldValue!).Height, ((Size)e.NewValue!).Height);
                if (System.Math.Abs(newExtent - oldExtent) <= oldExtent * 0.01)
                    ++small;
            };

            for (var i = 0; i < 400; ++i)
            {
                ScrollBy(sv, 10);
                RunLayout(ic);
                Assert.InRange(sv.Offset.Y, 0, System.Math.Max(0, sv.Extent.Height - sv.Viewport.Height) + 1e-6);
            }

            // Extent changes when the average row height changes (these heights change it by up to 6 %), not for
            // each row that enters or leaves the realized range.
            Assert.Equal(0, small);
            Assert.True(changes <= 50, $"{changes} Extent changes for 400 steps");
            window.Close();
        }

        [Fact]
        public void At_The_End_Offset_Is_Extent_Minus_Viewport()
        {
            var (window, sv, ic, _) = ShowCounting(lazy: true, Enumerable.Range(0, 200).Select(Height), size: 400);

            for (var i = 0; i < 2000; ++i)
            {
                ScrollBy(sv, 10);
                RunLayout(ic);
            }

            Assert.Equal(199, ItemsScroll.Get(ic).LastVisibleIndex);
            Assert.Equal(sv.Extent.Height - sv.Viewport.Height, sv.Offset.Y, 6);

            // And back at the top: Offset 0.
            for (var i = 0; i < 2000; ++i)
            {
                ScrollBy(sv, -10);
                RunLayout(ic);
            }

            Assert.Equal(0, sv.Offset.Y);
            window.Close();
        }

        private static int[] Realized(ItemsControl ic) =>
            ic.ItemsPanelRoot!.Children.Where(c => c.IsVisible).Select(ic.IndexFromContainer).Where(i => i >= 0)
                .OrderBy(i => i).ToArray();

        private static (Window, ScrollViewer, ItemsControl, Counts) ShowCounting(
            bool lazy, System.Collections.Generic.IEnumerable<double> heights, double size)
        {
            var counts = new Counts();
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<double>((h, _) => new Border { Height = h }),
            };
            var (window, sv) = Show(ic, new ObservableCollection<double>(heights), lazy, cacheLength: 0.5, size: size);
            ic.ItemsPanel = new FuncTemplate<Panel?>(() => lazy
                ? new CountingLazyStackPanel(counts) { CacheLength = 0.5 }
                : new CountingVirtualizingStackPanel(counts) { CacheLength = 0.5 });
            RunLayout(window);
            return (window, sv, ic, counts);
        }

        private class Counts
        {
            public int Measures;
            public int Arranges;
            public void Reset() => Measures = Arranges = 0;
        }

        private class CountingLazyStackPanel : LazyStackPanel
        {
            private readonly Counts _counts;
            public CountingLazyStackPanel(Counts counts) => _counts = counts;

            protected override Size MeasureOverride(Size availableSize)
            {
                ++_counts.Measures;
                return base.MeasureOverride(availableSize);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                ++_counts.Arranges;
                return base.ArrangeOverride(finalSize);
            }
        }

        private class CountingVirtualizingStackPanel : VirtualizingStackPanel
        {
            private readonly Counts _counts;
            public CountingVirtualizingStackPanel(Counts counts) => _counts = counts;

            protected override Size MeasureOverride(Size availableSize)
            {
                ++_counts.Measures;
                return base.MeasureOverride(availableSize);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                ++_counts.Arranges;
                return base.ArrangeOverride(finalSize);
            }
        }
    }
}
