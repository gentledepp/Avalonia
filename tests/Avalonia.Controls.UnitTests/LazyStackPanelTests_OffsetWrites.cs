using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Application code writes an out-of-range <see cref="ScrollViewer.Offset"/>: after the layout the
    /// <see cref="ScrollViewer"/> and its presenter show the offset of the panel. 100 x 100 window, rows of 10 px,
    /// no cache.
    /// </summary>
    public class LazyStackPanelTests_OffsetWrites : LazyStackPanelTestBase
    {
        private static ItemsControl CreateTarget() =>
            new() { ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }) };

        private static ObservableCollection<string> Rows(int count = 100) =>
            new(Enumerable.Range(0, count).Select(i => $"Item {i}"));

        private static void AssertOffset(ScrollViewer sv, double expected)
        {
            var presenter = sv.FindDescendantOfType<ScrollContentPresenter>()!;
            if (sv.FindDescendantOfType<ItemsPresenter>()!.Panel is ILogicalScrollable panel)
                Assert.Equal(expected, panel.Offset.Y, 1);
            Assert.Equal(expected, presenter.Offset.Y, 1);
            Assert.Equal(expected, sv.Offset.Y, 1);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Offset_Above_The_Top_Is_Clamped(bool lazy)
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy);

            sv.Offset = new Vector(0, -100);
            RunLayout(ic);

            AssertOffset(sv, 0);
            window.Close();
        }

        [Theory]
        [InlineData(false, 500)]
        [InlineData(true, 500)]
        [InlineData(false, double.PositiveInfinity)]
        [InlineData(true, double.PositiveInfinity)]
        public void Offset_Past_The_End_Is_Clamped(bool lazy, double past)
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy);
            sv.ScrollToEnd();
            RunLayout(ic);
            var end = sv.Offset.Y;
            Assert.Equal(900, end, 1);

            sv.Offset = new Vector(0, end + past);
            RunLayout(ic);

            AssertOffset(sv, end);
            window.Close();
        }

        // The second write of the same value is a change of the property again, so it is a request too.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_Same_Out_Of_Range_Write_Twice_Is_Two_Changes(bool lazy)
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy);
            var changes = 0;
            sv.ScrollChanged += (_, _) => ++changes;

            sv.Offset = new Vector(0, -100);
            RunLayout(ic);
            var first = changes;
            sv.Offset = new Vector(0, -100);
            RunLayout(ic);

            Assert.Equal(first * 2, changes);
            AssertOffset(sv, 0);
            window.Close();
        }
    }
}
