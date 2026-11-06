using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// <see cref="IItemsScrollInfo.Scrolled"/> (relative scrolls only, the applied distance and its source) and
    /// <see cref="IItemsScrollInfo.VisibleRangeChanged"/>: 100 x 100 window, rows of 10 px, no cache.
    /// </summary>
    public class LazyStackPanelTests_ScrolledEvents : LazyStackPanelTestBase
    {
        private static ItemsControl CreateTarget() =>
            new() { ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }) };

        private static ObservableCollection<string> Rows(int count = 100) =>
            new(Enumerable.Range(0, count).Select(i => $"Item {i}"));

        private sealed class Recorder
        {
            public readonly List<(double Delta, ScrollSource Source)> Scrolled = new();
            public int RangeChanged;

            public Recorder(IItemsScrollInfo info)
            {
                info.Scrolled += (_, e) => Scrolled.Add((e.Delta, e.Source));
                info.VisibleRangeChanged += (_, _) => ++RangeChanged;
            }

            public void Clear()
            {
                Scrolled.Clear();
                RangeChanged = 0;
            }
        }

        private static void Wheel(ScrollViewer sv, double lines)
        {
            var presenter = (Control)sv.Presenter!;
            presenter.RaiseEvent(new PointerWheelEventArgs(
                presenter,
                new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true),
                TopLevel.GetTopLevel(sv)!,
                new Point(50, 50),
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None,
                new Vector(0, -lines))
            {
                RoutedEvent = InputElement.PointerWheelChangedEvent
            });
        }

        private static void Touch(ScrollViewer sv, double delta)
        {
            var presenter = (Control)sv.Presenter!;
            presenter.RaiseEvent(new ScrollGestureEventArgs(ScrollGestureEventArgs.GetNextFreeId(), new Vector(0, delta)));
        }

        [Fact]
        public void Scrolled_Source_Is_What_Started_The_Scroll()
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy: true);
            var info = ItemsScroll.Get(ic);
            var r = new Recorder(info);

            Wheel(sv, 1);
            RunLayout(ic);
            Touch(sv, 7);
            RunLayout(ic);
            sv.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
            RunLayout(ic);
            sv.ScrollBarScrollBy(Orientation.Vertical, 3);
            RunLayout(ic);
            info.ScrollBy(4);
            RunLayout(ic);
            sv.LineDown();
            RunLayout(ic);

            Assert.Equal(
                new[] { ScrollSource.Wheel, ScrollSource.Touch, ScrollSource.Keyboard, ScrollSource.ScrollBar, ScrollSource.Programmatic, ScrollSource.Programmatic },
                r.Scrolled.Select(x => x.Source));
            Assert.Equal(new[] { 50.0, 7, 100, 3, 4, sv.SmallChange.Height }, r.Scrolled.Select(x => x.Delta));
            window.Close();
        }

        // The applied distance: the stop at row 0 and at the end is not part of it.
        [Fact]
        public void Scrolled_Delta_Is_The_Distance_That_Was_Scrolled()
        {
            var ic = CreateTarget();
            var (window, _) = Show(ic, Rows(), lazy: true);
            var info = ItemsScroll.Get(ic);
            info.ScrollBy(10);
            RunLayout(ic);
            var r = new Recorder(info);

            info.ScrollBy(-30);
            RunLayout(ic);
            info.ScrollBy(-30);
            RunLayout(ic);
            info.ScrollToEnd();
            RunLayout(ic);
            info.ScrollBy(30);
            RunLayout(ic);

            Assert.Equal(new[] { -10.0 }, r.Scrolled.Select(x => x.Delta));
            window.Close();
        }

        [Fact]
        public void Two_Scrolls_Before_One_Layout_Raise_One_Event()
        {
            var ic = CreateTarget();
            var (window, _) = Show(ic, Rows(), lazy: true);
            var info = ItemsScroll.Get(ic);
            var r = new Recorder(info);

            info.ScrollBy(5);
            info.ScrollBy(7);
            RunLayout(ic);

            Assert.Equal(new[] { 12.0 }, r.Scrolled.Select(x => x.Delta));
            window.Close();
        }

        [Fact]
        public void Jumps_And_Changes_Raise_VisibleRangeChanged_But_Not_Scrolled()
        {
            var ic = CreateTarget();
            var items = Rows();
            var (window, _) = Show(ic, items, lazy: true);
            var info = ItemsScroll.Get(ic);
            var r = new Recorder(info);

            info.ScrollToEnd();
            RunLayout(ic);
            Assert.Equal(1, r.RangeChanged);

            info.ScrollToItem(30);
            RunLayout(ic);
            Assert.Equal(2, r.RangeChanged);

            items.Insert(0, "New");
            RunLayout(ic);
            Assert.Equal(3, r.RangeChanged);

            Assert.Empty(r.Scrolled);
            window.Close();
        }

        // For an infinite scroll: rows removed below the screen can make the last visible row the last row.
        [Fact]
        public void VisibleRangeChanged_Is_Raised_When_Only_The_Count_Changes()
        {
            var ic = CreateTarget();
            var items = Rows();
            var (window, _) = Show(ic, items, lazy: true);
            var r = new Recorder(ItemsScroll.Get(ic));

            while (items.Count > 10)
                items.RemoveAt(items.Count - 1);
            RunLayout(ic);

            Assert.Equal(1, r.RangeChanged);
            Assert.Equal(9, ItemsScroll.Get(ic).LastVisibleIndex);
            window.Close();
        }

        [Fact]
        public void A_Layout_Without_A_Change_Raises_Nothing()
        {
            var ic = CreateTarget();
            var (window, _) = Show(ic, Rows(), lazy: true);
            var r = new Recorder(ItemsScroll.Get(ic));

            ic.ItemsPanelRoot!.InvalidateMeasure();
            RunLayout(ic);

            Assert.Empty(r.Scrolled);
            Assert.Equal(0, r.RangeChanged);
            window.Close();
        }

        // The fallback cannot see the source; it compares the rows on the screen.
        [Fact]
        public void Fallback_Raises_Both_Events_With_An_Unknown_Source()
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy: false);
            var r = new Recorder(ItemsScroll.Get(ic));

            Wheel(sv, 1);
            RunLayout(ic);

            Assert.Equal(new[] { (50.0, ScrollSource.Unknown) }, r.Scrolled);
            Assert.Equal(1, r.RangeChanged);
            window.Close();
        }
    }
}
