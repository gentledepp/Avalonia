using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Small scroll steps give the same result as one large step, and the reported Extent does not follow every
    /// row that enters or leaves the realized range.
    /// </summary>
    public class LazyStackPanelTests_ScrollPerformance : LazyStackPanelTestBase
    {
        // Periodic heights of 40-160 px.
        private static double Height(int i) => 40 + (i * 37 % 4) * 40;

        // Scroll steps before each layout add up to one scroll.
        [Fact]
        public void Small_Steps_Give_The_Same_Rows_As_One_Large_Step()
        {
            var heights = Enumerable.Range(0, 300).Select(Height).ToList();
            var (window1, sv1, ic1) = ShowRows(heights, size: 400);
            var (window2, sv2, ic2) = ShowRows(heights, size: 400);

            for (var i = 0; i < 150; ++i)
            {
                ScrollBy(sv1, i % 3 == 2 ? -7 : 13);
                RunLayout(ic1);
            }

            ScrollBy(sv2, 100 * 13 - 50 * 7);
            RunLayout(ic2);

            var scroll1 = TestScrollInfo.Get(ic1);
            var scroll2 = TestScrollInfo.Get(ic2);
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
            var (window, sv, ic) = ShowRows(Enumerable.Range(0, 2000).Select(Height), size: 400);
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
            // each row that enters or leaves the realized range. 78 changes with the 1 % threshold, 102 without it.
            Assert.Equal(0, small);
            Assert.True(changes <= 90, $"{changes} Extent changes for 400 steps");
            window.Close();
        }

        [Fact]
        public void At_The_End_Offset_Is_Extent_Minus_Viewport()
        {
            var (window, sv, ic) = ShowRows(Enumerable.Range(0, 200).Select(Height), size: 400);

            for (var i = 0; i < 2000; ++i)
            {
                ScrollBy(sv, 10);
                RunLayout(ic);
            }

            Assert.Equal(199, TestScrollInfo.Get(ic).LastVisibleIndex);
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

        private static (Window, ScrollViewer, ItemsControl) ShowRows(System.Collections.Generic.IEnumerable<double> heights,
            double size)
        {
            var ic = new ItemsControl
            {
                ItemTemplate = new FuncDataTemplate<double>((h, _) => new Border { Height = h }),
            };
            var (window, sv) = Show(ic, new ObservableCollection<double>(heights), lazy: true, cacheLength: 0.5, size: size);
            return (window, sv, ic);
        }
    }
}
