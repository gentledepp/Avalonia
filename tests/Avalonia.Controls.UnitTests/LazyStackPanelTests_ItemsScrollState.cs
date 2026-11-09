using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// <see cref="ItemsScroll.Get"/> returns one object for each items control, also before its panel exists
    /// (a hidden list) and after the panel is replaced. 100 x 100 window, rows of 10 px, no cache.
    /// </summary>
    public class LazyStackPanelTests_ItemsScrollState : LazyStackPanelTestBase
    {
        private static ItemsControl CreateTarget() =>
            new() { ItemTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 10 }) };

        private static ObservableCollection<string> Rows(int count = 100) =>
            new(Enumerable.Range(0, count).Select(i => $"Item {i}"));

        private static ITemplate<Panel?> PanelTemplate(bool lazy) =>
            new FuncTemplate<Panel?>(() => lazy ? new LazyStackPanel { CacheLength = 0 } : new VirtualizingStackPanel { CacheLength = 0 });

        private static Window ShowHidden(ItemsControl ic, bool lazy)
        {
            ic.IsVisible = false;
            return Show(ic, Rows(), lazy).Window;
        }

        private static ScrollViewer ScrollViewerOf(ItemsControl ic) =>
            ic.GetVisualDescendants().OfType<ScrollViewer>().First();

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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Get_On_A_Hidden_List_Reports_The_Rows_When_It_Is_Shown(bool lazy)
        {
            var ic = CreateTarget();
            var window = ShowHidden(ic, lazy);
            Assert.Null(ic.ItemsPanelRoot);
            var info = ItemsScroll.Get(ic);
            var changes = 0;
            info.VisibleRangeChanged += (_, _) => ++changes;
            Assert.Equal(-1, info.LastVisibleIndex);

            ic.IsVisible = true;
            RunLayout(ic);

            Assert.Equal(1, changes);
            Assert.Equal(0, info.FirstVisibleIndex);
            Assert.Equal(9, info.LastVisibleIndex);
            Assert.Same(info, ItemsScroll.Get(ic));
            window.Close();
        }

        // The object from before the panel existed uses the panel, so the source of a scroll is known.
        [Fact]
        public void Get_On_A_Hidden_List_Reports_The_Scroll_Source_Of_The_Panel()
        {
            var ic = CreateTarget();
            var window = ShowHidden(ic, lazy: true);
            var info = ItemsScroll.Get(ic);
            var scrolled = new List<(double Delta, ScrollSource Source)>();
            info.Scrolled += (_, e) => scrolled.Add((e.Delta, e.Source));
            ic.IsVisible = true;
            RunLayout(ic);

            Wheel(ScrollViewerOf(ic), 1);
            RunLayout(ic);

            Assert.Equal(new[] { (50.0, ScrollSource.Wheel) }, scrolled);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Events_Follow_A_Replaced_Panel(bool lazy)
        {
            var ic = CreateTarget();
            var (window, sv) = Show(ic, Rows(), lazy);
            var info = ItemsScroll.Get(ic);
            var changes = 0;
            var scrolled = 0;
            info.VisibleRangeChanged += (_, _) => ++changes;
            info.Scrolled += (_, _) => ++scrolled;

            ic.ItemsPanel = PanelTemplate(!lazy);
            RunLayout(ic);
            ic.ItemsPanel = PanelTemplate(lazy);
            RunLayout(ic);
            changes = 0;
            info.ScrollBy(25);
            RunLayout(ic);

            Assert.Same(info, ItemsScroll.Get(ic));
            Assert.Equal((1, 1, 2), (changes, scrolled, info.FirstVisibleIndex));
            window.Close();
        }

        // A command before the panel exists runs in the panel's first layout.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScrollToItem_On_A_Hidden_List_Runs_When_It_Is_Shown(bool lazy)
        {
            var ic = CreateTarget();
            var window = ShowHidden(ic, lazy);
            var info = ItemsScroll.Get(ic);

            info.ScrollToItem(50);
            ic.IsVisible = true;
            RunLayout(ic);

            Assert.Equal(50, info.FirstVisibleIndex);
            Assert.Equal(0, info.FirstVisibleOffset, 1);
            window.Close();
        }
    }
}
