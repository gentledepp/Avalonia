using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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
    /// Compares container recycling of <see cref="LazyStackPanel"/> with the VSP of the same
    /// Avalonia version, on the same items control, templates and 1000 rows of 3 types.
    /// </summary>
    public class LazyStackPanelTests_Recycling : LazyStackPanelTestBase
    {
        private const int ViewportHeight = 500;
        private const int Step = 250;
        private const int Steps = 20; // 10 viewports

        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_Recycling(ITestOutputHelper output) => _output = output;

        public enum Setup
        {
            // ItemsControl (ContentPresenter containers), one typed template per item type in DataTemplates.
            TypedTemplates,
            // ItemsControl with one IVirtualizingDataTemplate as ItemTemplate.
            VirtualizingTemplate,
            // ListBox (ListBoxItem containers) with typed templates.
            ListBoxTypedTemplates,
            // ListBox with one IVirtualizingDataTemplate.
            ListBoxVirtualizingTemplate,
        }

        public static TheoryData<Setup, bool> Cases()
        {
            return new TheoryData<Setup, bool>
            {
                { Setup.TypedTemplates, false },
                { Setup.ListBoxTypedTemplates, false },
                { Setup.TypedTemplates, true },
                { Setup.VirtualizingTemplate, false },
                { Setup.VirtualizingTemplate, true },
                { Setup.ListBoxVirtualizingTemplate, false },
            };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Recycling_Is_At_Least_As_Good_As_The_Vsp(Setup setup, bool warmup)
        {
            var vsp = Run(setup, warmup, lazy: false);
            var lazy = Run(setup, warmup, lazy: true);

            _output.WriteLine($"{setup}, warmup {warmup}, Avalonia {typeof(Control).Assembly.GetName().Version}");
            _output.WriteLine(Stats.Header);
            _output.WriteLine(vsp.ToString("VSP"));
            _output.WriteLine(lazy.ToString("Lazy"));

            // Both panels show the right content for every realized row (checked inside Run).
            // The lazy panel keeps the number of children bounded and the pools below the cap.
            Assert.True(lazy.MaxChildren <= lazy.MaxVisible + lazy.MaxPooled, "children not bounded");
            Assert.True(lazy.Containers <= vsp.Containers + 2, $"containers: lazy {lazy.Containers}, VSP {vsp.Containers}");
            // Totals: the lazy panel realizes fewer rows in the first layout, so it builds more of them during the scroll.
            Assert.True(lazy.Builds <= vsp.Builds + 2, $"builds: lazy {lazy.Builds}, VSP {vsp.Builds}");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Lazy_Recycled_Containers_Keep_Their_Child(Setup setup, bool warmup)
        {
            var lazy = Run(setup, warmup, lazy: true);

            Assert.Equal(0, lazy.ChildSwaps);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ListBox_Selection_Survives_Recycling(bool lazy)
        {
            var (window, ic, sv, panel, rows, counter) = CreateTarget(Setup.ListBoxTypedTemplates, warmup: false, lazy);
            var listBox = (ListBox)ic;

            listBox.SelectedIndex = 3;
            RunLayout(sv);
            ScrollDownAndBack(sv, panel);

            Assert.Equal(3, listBox.SelectedIndex);
            Assert.Single(listBox.SelectedItems!);
            var selected = panel.Children.OfType<ListBoxItem>().Where(x => x.IsVisible && x.IsSelected).ToList();
            Assert.Equal(3, ((Item)Assert.Single(selected).DataContext!).Index);

            // Selecting a row that reuses the old selected container's pool does not bring back the old selection.
            listBox.SelectedIndex = 900;
            sv.Offset = new Vector(0, 50000);
            RunLayout(sv);
            listBox.ScrollIntoView(900);
            RunLayout(sv);
            Assert.Single(listBox.SelectedItems!);
            Assert.All(panel.Children.OfType<ListBoxItem>().Where(x => x.IsVisible && x.IsSelected),
                x => Assert.Equal(900, ((Item)x.DataContext!).Index));
            window.Close();
        }

        [Fact]
        public void Warmup_Fills_The_Pools_After_The_First_Layout()
        {
            // The realized rows already hold MinPoolSizePerKey 2 of each type, so warmup needs a larger minimum.
            var vsp = Run(Setup.VirtualizingTemplate, warmup: true, lazy: false, minPool: 8);
            var lazy = Run(Setup.VirtualizingTemplate, warmup: true, lazy: true, minPool: 8);

            _output.WriteLine(Stats.Header);
            _output.WriteLine(vsp.ToString("VSP"));
            _output.WriteLine(lazy.ToString("Lazy"));
            _output.WriteLine($"pooled keys after warmup: VSP {vsp.PooledKeysAfterWarmup}, Lazy {lazy.PooledKeysAfterWarmup}");

            Assert.True(lazy.PooledKeysAfterWarmup > 0, "no pooled keys after warmup");
            Assert.True(lazy.PooledKeysAfterWarmup >= vsp.PooledKeysAfterWarmup,
                $"pooled keys: lazy {lazy.PooledKeysAfterWarmup}, VSP {vsp.PooledKeysAfterWarmup}");
            Assert.True(lazy.ScrollBuilds <= vsp.ScrollBuilds, $"scroll builds: lazy {lazy.ScrollBuilds}, VSP {vsp.ScrollBuilds}");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Pool_Respects_MaxPoolSizePerKey(bool lazy)
        {
            var (window, ic, sv, panel, rows, counter) = CreateTarget(Setup.VirtualizingTemplate, warmup: false, lazy);

            // Jump far, twice: all realized rows go to the pools at once; at most 5 per key may stay.
            for (var i = 0; i < 2; ++i)
            {
                sv.Offset = new Vector(0, 20000 + i * 20000);
                RunLayout(sv);
                AssertContent(panel);
            }

            var hidden = panel.Children.Where(x => !x.IsVisible).GroupBy(x => x.DataContext?.GetType()).ToList();
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")}: hidden children per type: " +
                string.Join(", ", hidden.Select(g => $"{g.Key?.Name} {g.Count()}")) + $"; children {panel.Children.Count}");

            Assert.All(hidden, g => Assert.True(g.Count() <= CountingSelector.MaxPool, $"{g.Key}: {g.Count()}"));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Jump_Far_Shows_The_Right_Content(bool lazy)
        {
            var (window, ic, sv, panel, rows, counter) = CreateTarget(Setup.TypedTemplates, warmup: false, lazy);

            foreach (var offset in new[] { 20000, 40000, 5000, 0 })
            {
                sv.Offset = new Vector(0, offset);
                RunLayout(sv);
                AssertContent(panel);
            }

            window.Close();
        }

        [Fact]
        public void Lazy_Reset_And_Clear_Do_Not_Leave_Pooled_Children()
        {
            var (window, ic, sv, panel, rows, counter) = CreateTarget(Setup.TypedTemplates, warmup: false, lazy: true);

            ScrollDownAndBack(sv, panel);
            rows.Clear();
            RunLayout(sv);

            Assert.Empty(panel.Children);
            window.Close();
        }

        // A jump far away (thumb drag, ScrollToEnd/Home, search) must reuse the old rows' containers.
        [Theory]
        [InlineData(Setup.VirtualizingTemplate)]
        [InlineData(Setup.ListBoxVirtualizingTemplate)]
        public void Jumps_Reuse_Containers_As_The_Vsp(Setup setup)
        {
            var vsp = Jump(setup, lazy: false);
            var lazy = Jump(setup, lazy: true);

            _output.WriteLine($"{setup}: new containers during 20 jumps: VSP {vsp}, Lazy {lazy}");
            Assert.True(lazy <= vsp + 2, $"new containers: lazy {lazy}, VSP {vsp}");
        }

        private int Jump(Setup setup, bool lazy)
        {
            var (window, ic, sv, panel, rows, _) = CreateTarget(setup, warmup: false, lazy);
            var containers = new HashSet<Control>(panel.Children);
            var created = 0;

            ic.ContainerPrepared += (_, e) =>
            {
                if (containers.Add(e.Container))
                    ++created;
            };

            // Each jump lands far from the last one; the realized rows never overlap.
            for (var i = 0; i < 20; ++i)
            {
                sv.Offset = new Vector(0, (i % 2 == 0 ? 20000 : 5000) + i * 1000);
                RunLayout(sv);
                AssertContent(panel);
            }

            window.Close();
            return created;
        }

        private Stats Run(Setup setup, bool warmup, bool lazy, int minPool = 2)
        {
            var (window, ic, sv, panel, rows, counter) = CreateTarget(setup, warmup, lazy, minPool);
            var stats = new Stats();
            var containers = new HashSet<Control>();
            var lastChild = new Dictionary<Control, Control?>();

            ic.ContainerPrepared += (_, e) =>
            {
                containers.Add(e.Container);
                stats.Prepares++;
            };

            void Observe()
            {
                AssertContent(panel);
                stats.MaxChildren = Math.Max(stats.MaxChildren, panel.Children.Count);
                stats.MaxVisible = Math.Max(stats.MaxVisible, panel.Children.Count(x => x.IsVisible));
                stats.MaxPooled = Math.Max(stats.MaxPooled, panel.Children.Count(x => !x.IsVisible));

                foreach (var c in panel.Children.Where(x => x.IsVisible))
                {
                    var child = ContentChild(c);
                    if (lastChild.TryGetValue(c, out var old) && old != child)
                        stats.ChildSwaps++;
                    lastChild[c] = child;
                }
            }

            // Containers created in CreateTarget (first layout, warmup) are counted through the children.
            foreach (var c in panel.Children)
                containers.Add(c);
            stats.FirstContainers = containers.Count;
            stats.FirstBuilds = counter.Builds;
            stats.PooledKeysAfterWarmup = panel.Children.Where(x => !x.IsVisible)
                .Select(x => x.DataContext?.GetType()).Distinct().Count();
            stats.Prepares = 0;
            Observe();

            ScrollDownAndBack(sv, panel, Observe);

            stats.Containers = containers.Count;
            stats.Builds = counter.Builds;
            window.Close();
            return stats;
        }

        private static void ScrollDownAndBack(ScrollViewer sv, Panel panel, Action? observe = null)
        {
            for (var i = 0; i < Steps; ++i)
            {
                LazyStackPanelTestTarget.ScrollBy(sv, Step);
                RunLayout(sv);
                observe?.Invoke();
            }

            for (var i = 0; i < Steps; ++i)
            {
                LazyStackPanelTestTarget.ScrollBy(sv, -Step);
                RunLayout(sv);
                observe?.Invoke();
            }
        }

        private static Control? ContentChild(Control container) => container switch
        {
            ContentPresenter p => p.Child,
            ContentControl cc => cc.Presenter?.Child,
            _ => null,
        };

        // Every visible container shows its item, with a child built for the item's type and the item's height.
        private static void AssertContent(Panel panel)
        {
            foreach (var c in panel.Children.Where(x => x.IsVisible))
            {
                var item = Assert.IsAssignableFrom<Item>(c.DataContext);
                var child = ContentChild(c);
                Assert.NotNull(child);
                Assert.Same(item, child!.DataContext);
                Assert.Equal(item.GetType(), child.Tag);
                Assert.Equal(item.Height, c.Bounds.Height);
            }
        }

        private static (Window, ItemsControl, ScrollViewer, Panel, ObservableCollection<Item>, BuildCounter) CreateTarget(
            Setup setup, bool warmup, bool lazy, int minPool = 2)
        {
            var counter = new BuildCounter();
            var rows = new ObservableCollection<Item>(Enumerable.Range(0, 1000).Select(Item.Create));
            ItemsControl ic = setup is Setup.ListBoxTypedTemplates or Setup.ListBoxVirtualizingTemplate ? new ListBox() : new ItemsControl
            {
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

            ic.ItemsSource = rows;
            ic.ItemsPanel = new FuncTemplate<Panel?>(() => CreatePanel(lazy, warmup));

            if (setup is Setup.VirtualizingTemplate or Setup.ListBoxVirtualizingTemplate)
                ic.ItemTemplate = new CountingSelector(counter, minPool);
            else
            {
                ic.DataTemplates.Add(new CountingTemplate<ItemA>(counter));
                ic.DataTemplates.Add(new CountingTemplate<ItemB>(counter));
                ic.DataTemplates.Add(new CountingTemplate<ItemC>(counter));
            }

            var window = new Window
            {
                Width = 400,
                Height = ViewportHeight,
                Content = ic,
                Styles =
                {
                    new Style(x => x.OfType<ListBoxItem>())
                    {
                        Setters =
                        {
                            new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                            new Setter(Layoutable.MinHeightProperty, 0.0),
                        },
                    },
                    new Style(x => x.OfType<ListBox>())
                    {
                        Setters =
                        {
                            new Setter(Border.BorderThicknessProperty, new Thickness(0)),
                            new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                        },
                    },
                },
            };

            window.Show();
            RunLayout(window);
            var sv = ic.GetVisualDescendants().OfType<ScrollViewer>().First();
            return (window, ic, sv, (Panel)ic.ItemsPanelRoot!, rows, counter);
        }

        private static Panel CreatePanel(bool lazy, bool warmup)
        {
            if (lazy)
                return new LazyStackPanel { CacheLength = 0.5, EnableWarmup = warmup };

            return new VirtualizingStackPanel { CacheLength = 0.5, EnableWarmup = warmup };
        }

        private static void RunLayout(Control c)
        {
            Dispatcher.UIThread.RunJobs();
            c.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            c.UpdateLayout();
        }

        private class Stats
        {
            public const string Header =
                "panel | first: containers, builds | after scroll: containers, builds, prepares | child swaps | max children (visible + pooled)";

            public int FirstContainers;
            public int FirstBuilds;
            public int PooledKeysAfterWarmup;
            public int Containers;
            public int Builds;
            public int Prepares;
            public int ChildSwaps;
            public int MaxChildren;
            public int MaxVisible;
            public int MaxPooled;

            public int ScrollBuilds => Builds - FirstBuilds;

            public string ToString(string name) =>
                $"{name} | {FirstContainers}, {FirstBuilds} | {Containers}, {Builds}, {Prepares} | {ChildSwaps} | {MaxChildren} ({MaxVisible} + {MaxPooled})";
        }

        public class BuildCounter
        {
            public int Builds;

            public Control Build(object? data)
            {
                Builds++;
                return new Border
                {
                    Tag = data?.GetType(),
                    [!Layoutable.HeightProperty] = new Binding(nameof(Item.Height)),
                    Child = new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(Item.Index)) },
                };
            }
        }

        private class CountingTemplate<T> : IRecyclingDataTemplate, ITypedDataTemplate
        {
            private readonly BuildCounter _counter;

            public CountingTemplate(BuildCounter counter) => _counter = counter;

            public Type DataType => typeof(T);

            public bool Match(object? data) => data is T;

            public Control? Build(object? data) => Build(data, null);

            public Control? Build(object? data, Control? existing) => existing ?? _counter.Build(data);
        }

        // One template for all item types: key = item type, pool 2-5 per key.
        private class CountingSelector : IVirtualizingDataTemplate
        {
            public const int MaxPool = 5;
            private readonly BuildCounter _counter;

            public CountingSelector(BuildCounter counter, int minPool) => (_counter, MinPoolSizePerKey) = (counter, minPool);

            public int MaxPoolSizePerKey => Math.Max(MaxPool, MinPoolSizePerKey);

            public int MinPoolSizePerKey { get; }

            public object? GetKey(object? data) => data?.GetType();

            public bool Match(object? data) => data is Item;

            public Control? Build(object? data) => Build(data, null);

            public Control? Build(object? data, Control? existing) => existing ?? _counter.Build(data);
        }

        public abstract class Item
        {
            protected Item(int index, double height) => (Index, Height) = (index, height);

            public int Index { get; }

            public double Height { get; }

            public static Item Create(int i) => ((i * 7) % 10) switch
            {
                < 5 => new ItemA(i),
                < 8 => new ItemB(i),
                _ => new ItemC(i),
            };
        }

        // Different heights within one key: a reused child must be measured again.
        public class ItemA : Item { public ItemA(int i) : base(i, 30 + i % 3 * 10) { } }

        public class ItemB : Item { public ItemB(int i) : base(i, 80) { } }

        public class ItemC : Item { public ItemC(int i) : base(i, 120) { } }
    }
}
