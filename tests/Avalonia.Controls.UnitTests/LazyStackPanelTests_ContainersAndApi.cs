using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

#nullable enable

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Containers, selection, keyboard and the <see cref="ItemsScroll"/> API, against both panels; the ItemsScroll
    /// tests also against a StackPanel (the ItemsScroll fallback). The container tests check the layout after every
    /// step (<see cref="Check"/>). Where the VSP fails by design, its own result is the expectation.
    /// </summary>
    public class LazyStackPanelTests_ContainersAndApi : LazyStackPanelTestBase
    {
        private const double Tolerance = 0.5;
        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_ContainersAndApi(ITestOutputHelper output) => _output = output;

        public const string Lazy = "Lazy";
        public const string Vsp = "VSP";
        public const string Stack = "StackPanel";

        public static TheoryData<string> ThreePanels => new() { Lazy, Vsp, Stack };

        // =========================================================================================================
        // ItemTemplate and ItemContainerTheme change at runtime
        // =========================================================================================================

        // A change of ItemTemplate or ItemContainerTheme is a Reset with the same items. Both panels keep the
        // realized rows by item without a new prepare, so they keep the old template or theme until they are
        // recycled. For the VSP that is the expectation (upstream ItemContainerTheme_Can_Be_Changed_Virtualizing
        // fails on it).
        public const string RowsKeepOldTemplateAfterReset =
            "Rows kept by item after a Reset are not prepared again, so they keep the old template or theme";

        [Theory]
        [InlineData(false)]
        [InlineData(true, Skip = RowsKeepOldTemplateAfterReset)]
        public void ItemTemplate_Change_Is_Used_By_Every_Row(bool lazy)
        {
            var ic = new ItemsControl { ItemTemplate = TaggedTemplate("A") };
            var items = Items(100);
            var (window, sv) = Show(ic, items, lazy, 0.5);
            ScrollAndBack(ic, sv); // fill the pool with "A" rows

            ic.ItemTemplate = TaggedTemplate("B");
            RunLayout(ic);
            Check(ic, sv, "after the template change");
            var oldAfterChange = RealizedTags(ic).Count(t => t != "B");

            var oldWhileScrolling = new List<int>();
            for (var i = 0; i < 9; ++i)
            {
                ScrollBy(sv, 100);
                RunLayout(ic);
                Check(ic, sv, $"step {i}");
                oldWhileScrolling.Add(RealizedTags(ic).Count(t => t != "B"));
            }

            // "Old pools dropped": hidden children that still hold an "A" row.
            var oldPooled = ic.ItemsPanelRoot!.Children.OfType<ContentPresenter>()
                .Count(c => !c.IsVisible && (c.Child?.Tag as string) == "A");
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")}: old template on {oldAfterChange} of {ic.GetRealizedContainers().Count()} rows after the change, " +
                $"while scrolling [{string.Join(", ", oldWhileScrolling)}], pooled {oldPooled}");
            var expected = lazy ? (0, 0, 0) : Expectations.VspItemTemplateChange;
            Assert.Equal(expected, (oldAfterChange, oldWhileScrolling.Max(), oldPooled));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true, Skip = RowsKeepOldTemplateAfterReset)]
        public void ItemContainerTheme_Change_Is_Used_By_Every_Row(bool lazy)
        {
            var theme1 = new ControlTheme(typeof(ContentPresenter))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Red) },
            };
            var theme2 = new ControlTheme(typeof(ContentPresenter))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Green) },
            };
            var ic = new ItemsControl { ItemTemplate = RowTemplate(), ItemContainerTheme = theme1 };
            var (window, sv) = Show(ic, Items(100), lazy, 0.5);
            ScrollAndBack(ic, sv);

            ic.ItemContainerTheme = theme2;
            RunLayout(ic);
            Check(ic, sv, "after the theme change");
            var oldAfterChange = ic.GetRealizedContainers().Count(c => c.Theme != theme2 || ((ContentPresenter)c).Background != Brushes.Green);

            var oldWhileScrolling = new List<int>();
            for (var i = 0; i < 9; ++i)
            {
                ScrollBy(sv, 100);
                RunLayout(ic);
                oldWhileScrolling.Add(ic.GetRealizedContainers().Count(c => c.Theme != theme2 || ((ContentPresenter)c).Background != Brushes.Green));
            }
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")}: old theme on {oldAfterChange} of {ic.GetRealizedContainers().Count()} rows after the change, " +
                $"while scrolling [{string.Join(", ", oldWhileScrolling)}]");
            var expected = lazy ? (0, 0) : Expectations.VspThemeChange;
            Assert.Equal(expected, (oldAfterChange, oldWhileScrolling.Max()));
            window.Close();
        }

        // =========================================================================================================
        // GetRealizedContainers order and content
        // =========================================================================================================

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GetRealizedContainers_Has_No_Pooled_And_No_Kept_Focused_Row(bool lazy)
        {
            var items = Items(200);
            var (window, lb, sv) = ShowListBox(lazy, items, 0.5);
            var focused = FocusRow(lb, 0);
            var info = ItemsScroll.Get(lb);

            void AssertRealized(string when)
            {
                Check(lb, sv, when);
                Assert.DoesNotContain(focused, lb.GetRealizedContainers());
                Assert.All(lb.GetRealizedContainers(), c => Assert.True(c.IsVisible, $"{when}: hidden container"));
            }

            ScrollBy(sv, 400);
            RunLayout(lb);
            AssertRealized("after a scroll");
            Assert.Same(focused, lb.ContainerFromIndex(0));
            items.Insert(30, new Item(-1, 10));
            RunLayout(lb);
            AssertRealized("after an insert");
            info.ScrollToItem(150);
            RunLayout(lb);
            AssertRealized("after a jump");
            ScrollBy(sv, -55);
            RunLayout(lb);
            AssertRealized("after a scroll up");
            items.RemoveAt(lb.IndexFromContainer(lb.GetRealizedContainers().First()) + 2);
            RunLayout(lb);
            AssertRealized("after a remove");
            Assert.True(focused.IsFocused);
            window.Close();
        }

        // =========================================================================================================
        // Loaded / Unloaded with an IVirtualizingDataTemplate
        // =========================================================================================================

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Loaded_And_Unloaded_Of_Recycled_Template_Children(bool lazy)
        {
            var loaded = new Dictionary<Control, int>();
            var unloaded = new Dictionary<Control, int>();
            var template = new VirtualizingTemplate(() =>
            {
                var b = new Border { [!Layoutable.HeightProperty] = new Binding(nameof(Item.Height)) };
                loaded[b] = unloaded[b] = 0;
                b.Loaded += (_, _) => ++loaded[b];
                b.Unloaded += (_, _) => ++unloaded[b];
                return b;
            }, maxPool: 100); // no container over the pool limit: those leave the panel and are unloaded
            var ic = new ItemsControl { ItemTemplate = template };
            var (window, sv) = Show(ic, Items(100), lazy, 0);

            for (var i = 0; i < 9; ++i)
            {
                ScrollBy(sv, 100);
                RunLayout(ic);
            }
            for (var i = 0; i < 9; ++i)
            {
                ScrollBy(sv, -100);
                RunLayout(ic);
            }
            Check(ic, sv, "after down and back");

            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")}: {loaded.Count} children built, Loaded {loaded.Values.Sum()}, Unloaded {unloaded.Values.Sum()}");
            // The documented limit (#20993): a template child stays loaded while its container is pooled.
            Assert.All(loaded.Values, n => Assert.Equal(1, n));
            Assert.All(unloaded.Values, n => Assert.Equal(0, n));
            window.Close();
        }

        // =========================================================================================================
        // The panel is replaced (re-templated items control, new ItemsPanel)
        // =========================================================================================================

        public enum Replace { Retemplate, NewItemsPanel }

        [Theory]
        [InlineData(false, Replace.Retemplate)]
        [InlineData(false, Replace.NewItemsPanel)]
        [InlineData(true, Replace.Retemplate)]
        [InlineData(true, Replace.NewItemsPanel)]
        public void A_Replaced_Panel_Leaves_No_Container_In_The_Visual_Tree(bool lazy, Replace replace)
        {
            var (window, ic, oldPanel, oldChildren) = ReplacePanel(lazy, replace);
            var newPanel = ic.ItemsPanelRoot!;

            Assert.NotSame(oldPanel, newPanel);
            Assert.Empty(oldPanel.Children);
            Assert.All(oldChildren, c => Assert.Null(c.GetVisualParent()));
            Assert.DoesNotContain(newPanel.Children, oldChildren.Contains);
            Check(ic, ic.GetVisualDescendants().OfType<ScrollViewer>().First(), "with the new panel");
            window.Close();
        }

        // VirtualizingPanel.Detach clears Children but does not call ItemsControl.RemoveLogicalChild, so every old
        // container (realized and pooled) stays a logical child of the items control: a leak for both panels. For
        // the VSP that is the expectation.
        public const string DetachLeavesLogicalChildren =
            "VirtualizingPanel.Detach does not call RemoveLogicalChild, so old containers stay logical children";

        [Theory]
        [InlineData(false, Replace.Retemplate)]
        [InlineData(false, Replace.NewItemsPanel)]
        [InlineData(true, Replace.Retemplate, Skip = DetachLeavesLogicalChildren)]
        [InlineData(true, Replace.NewItemsPanel, Skip = DetachLeavesLogicalChildren)]
        public void A_Replaced_Panel_Leaves_No_Logical_Child_Behind(bool lazy, Replace replace)
        {
            var (window, ic, _, oldChildren) = ReplacePanel(lazy, replace);
            var leaked = oldChildren.Count(c => c.GetLogicalParent() == ic || ic.GetLogicalChildren().Contains(c));

            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")} {replace}: {leaked} of {oldChildren.Count} old containers are still logical children");
            Assert.Equal(lazy ? 0 : oldChildren.Count, leaked);
            window.Close();
        }

        private static (Window, ItemsControl, Panel, List<Control>) ReplacePanel(bool lazy, Replace replace)
        {
            var ic = new ItemsControl { ItemTemplate = RowTemplate() };
            var (window, sv) = Show(ic, Items(100), lazy, 0.5);
            ScrollAndBack(ic, sv);
            ScrollBy(sv, 250);
            RunLayout(ic);
            var oldPanel = ic.ItemsPanelRoot!;
            var oldChildren = oldPanel.Children.ToList();
            Assert.Contains(oldChildren, c => !c.IsVisible); // pooled rows exist

            if (replace == Replace.Retemplate)
            {
                ic.Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns),
                });
            }
            else
            {
                ic.ItemsPanel = new FuncTemplate<Panel?>(() => lazy
                    ? new LazyStackPanel { CacheLength = 0.5 }
                    : new VirtualizingStackPanel { CacheLength = 0.5 });
            }
            RunLayout(window);
            return (window, ic, oldPanel, oldChildren);
        }

        // =========================================================================================================
        // The children stay bounded after 100 random jumps and edits
        // =========================================================================================================

        [Theory]
        [InlineData(false, 1)]
        [InlineData(false, 2)]
        [InlineData(true, 1)]
        [InlineData(true, 2)]
        public void Children_Stay_Bounded_After_Random_Jumps_And_Edits(bool lazy, int seed)
        {
            const int maxPool = 5;
            var random = new Random(seed);
            var next = 1000;
            var items = new ResettingCollection<Item>(Enumerable.Range(0, 300).Select(i => new Item(i, 10 + 10 * (i % 3))));
            var (window, lb, sv) = ShowListBox(lazy, items, 0.5, template: new VirtualizingTemplate(
                () => new Border { [!Layoutable.HeightProperty] = new Binding(nameof(Item.Height)) }, maxPool));
            var info = ItemsScroll.Get(lb);
            var focused = FocusRow(lb, 3);
            var maxChildren = 0;
            var vspProblems = new List<string>();

            for (var step = 0; step < 100; ++step)
            {
                var what = random.Next(7);
                switch (what)
                {
                    case 0: info.ScrollToItem(random.Next(items.Count)); break;
                    case 1: info.ScrollBy(random.Next(-300, 300)); break;
                    case 2: items.Insert(random.Next(items.Count), new Item(next++, 10 + random.Next(3) * 10)); break;
                    case 3: if (items.Count > 50) items.RemoveAt(random.Next(items.Count)); break;
                    case 4: items.Reset(items.OrderBy(_ => random.Next()).Take(items.Count - random.Next(5)).ToList()); break;
                    case 5: items[random.Next(items.Count)].Height = 10 + random.Next(4) * 10; break;
                    case 6: lb.ScrollIntoView(random.Next(items.Count)); break;
                }
                RunLayout(lb);
                if (Problem(lb, sv) is { } problem)
                {
                    // The VSP leaves gaps after some edits (by design): only recorded.
                    Assert.False(lazy, $"step {step} ({what}): {problem}");
                    vspProblems.Add($"step {step} ({what}): {problem}");
                }

                var children = lb.ItemsPanelRoot!.Children.Count;
                var realized = lb.GetRealizedContainers().Count();
                maxChildren = Math.Max(maxChildren, children);
                Assert.True(children <= realized + maxPool + 1,
                    $"step {step} ({what}): {children} children for {realized} realized rows");
            }
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")} seed {seed}: at most {maxChildren} children; {string.Join("; ", vspProblems)}");
            window.Close();
        }

        // =========================================================================================================
        // Recycle keys, templates and nested virtualized lists inside rows
        // =========================================================================================================

        // The pool is read as the hidden children of the panel.

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Opted_In_Xaml_Template_Keeps_Child_Attached_Across_Recycling(bool lazy)
        {
            var template = CanvasTemplate(enableVirtualization: true, typeof(TypeAItem));
            var (ic, sv, _) = ShowItemsControl(lazy, ObjectItems<TypeAItem>(200), template, 100);
            var container = Assert.IsType<ContentPresenter>(ic.ContainerFromIndex(0));
            var child = container.Child;
            Assert.NotNull(child);

            sv.Offset = new Vector(0, 1000);
            RunLayout(ic);
            Assert.True(ic.IndexFromContainer(container) > 0, "The container was not reused for a different item.");
            Assert.Same(child, container.Child);

            sv.Offset = new Vector(0, 0);
            RunLayout(ic);
            Check(ic, sv, "back at the top");
            Assert.True(ic.IndexFromContainer(container) >= 0, "The container was not reused.");
            Assert.Same(child, container.Child);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Typed_FuncDataTemplate_Keys_By_DataType(bool lazy)
        {
            var template = new FuncDataTemplate<TypeAItem>((_, _) => new Canvas { Width = 100, Height = 10 });
            var items = ObjectItems<TypeAItem>(200);
            var (ic, _, _) = ShowItemsControl(lazy, items, template, 400);

            Assert.True(ic.ItemContainerGenerator.NeedsContainer(items[0], 0, out var key));
            Assert.Equal(typeof(TypeAItem), key);
            var recycled = RecycleByShrinking(ic, items, keep: 10);
            Assert.True(recycled > 0);
            // The key is the DataType, but a FuncDataTemplate without supportsRecycling does not keep its child:
            // the pooled containers are empty (both panels).
            var pooled = Pooled(ic).Cast<ContentPresenter>().ToList();
            Assert.Equal(recycled, pooled.Count);
            Assert.All(pooled, c => Assert.Null(c.Child));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Virtualizing_Template_Keys_The_Pool_And_Keeps_The_Child(bool lazy)
        {
            var template = new FuncVirtualizingDataTemplate<TypeAItem>((_, _) => new Canvas { Width = 100, Height = 10 });
            var items = ObjectItems<TypeAItem>(200);
            var (ic, sv, _) = ShowItemsControl(lazy, items, template, 100);

            Assert.True(ic.ItemContainerGenerator.NeedsContainer(items[0], 0, out var key));
            Assert.Equal(typeof(TypeAItem), key);
            var container = Assert.IsType<ContentPresenter>(ic.ContainerFromIndex(0));
            var child = container.Child;

            sv.Offset = new Vector(0, 1000);
            RunLayout(ic);
            Assert.True(ic.IndexFromContainer(container) > 0, "The container was not reused for a different item.");
            Assert.Same(child, container.Child);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Recycle_Key_Can_Be_Something_Other_Than_The_Type(bool lazy)
        {
            var template = new KindTemplate(d => ((KindedItem)d!).Kind);
            var items = new ObservableCollection<object>(Enumerable.Range(0, 200).Select(i => (object)new KindedItem(i % 2 == 0 ? "tall" : "short")));
            var (ic, _, _) = ShowItemsControl(lazy, items, template, 400);

            Assert.True(ic.ItemContainerGenerator.NeedsContainer(items[0], 0, out var tall));
            Assert.True(ic.ItemContainerGenerator.NeedsContainer(items[1], 1, out var shortKey));
            Assert.Equal("tall", tall);
            Assert.Equal("short", shortKey);

            RecycleByShrinking(ic, items, keep: 10);
            // Two pools: a pooled container keeps the child of its own kind.
            Assert.Equal(new[] { 10.0, 20.0 }, Pooled(ic).Cast<ContentPresenter>().Select(c => c.Child!.Height).Distinct().OrderBy(h => h));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Null_Recycle_Key_Falls_Back_To_The_Item_Type(bool lazy)
        {
            var template = new KindTemplate(d => ((KindedItem)d!).Kind == "tall" ? "tall" : null);
            var items = new ObservableCollection<object>(Enumerable.Range(0, 200).Select(i => (object)new KindedItem(i % 2 == 0 ? "tall" : "short")));
            var (ic, sv, _) = ShowItemsControl(lazy, items, template, 100);

            Assert.True(ic.ItemContainerGenerator.NeedsContainer(items[0], 0, out var tall));
            Assert.True(ic.ItemContainerGenerator.NeedsContainer(items[1], 1, out var other));
            Assert.Equal("tall", tall);
            Assert.Equal(typeof(KindedItem), other);
            sv.Offset = new Vector(0, 1000);
            RunLayout(ic);
            Check(ic, sv, "after the jump");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MaxPoolSizePerKey_Is_Respected(bool lazy)
        {
            var template = new FuncVirtualizingDataTemplate<TypeAItem>((_, _) => new Canvas { Width = 100, Height = 10 }) { MaxPool = 2 };
            var items = ObjectItems<TypeAItem>(200);
            var (ic, _, _) = ShowItemsControl(lazy, items, template, 400);

            var recycled = RecycleByShrinking(ic, items, keep: 10);
            Assert.True(recycled > 2, $"only {recycled} recycled");
            Assert.Equal(2, Pooled(ic).Count);
            Assert.Equal(10 + 2, ic.ItemsPanelRoot!.Children.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Item_That_Is_Its_Own_Container_Is_Not_Wrapped(bool lazy)
        {
            var template = new FuncVirtualizingDataTemplate<object>((_, _) => new Canvas { Width = 100, Height = 10 });
            var items = new ObservableCollection<object>(Enumerable.Range(0, 20).Select(_ => (object)new Canvas { Width = 100, Height = 10 }));
            var (ic, _, _) = ShowItemsControl(lazy, items, template, 100);

            Assert.False(ic.ItemContainerGenerator.NeedsContainer(items[0], 0, out var key));
            Assert.Null(key);
            Assert.Same(items[0], ic.ContainerFromIndex(0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Recycled_Container_Reused_For_Different_Item_Gets_New_Content(bool lazy)
        {
            var template = CanvasTemplate(enableVirtualization: true, typeof(TypeAItem));
            var items = ObjectItems<TypeAItem>(200);
            var (ic, sv, _) = ShowItemsControl(lazy, items, template, 100);
            var container = Assert.IsType<ContentPresenter>(ic.ContainerFromIndex(0));
            Assert.Same(items[0], container.Content);

            sv.Offset = new Vector(0, 1000);
            RunLayout(ic);

            var index = ic.IndexFromContainer(container);
            Assert.True(index > 0, "The container was not reused for a different item.");
            Assert.Same(items[index], container.Content);
        }

        // The ItemsControl resolves the template from DataTemplates (GetCachedDataTemplate) and sets it as the
        // container's ContentTemplate.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DataTemplates_Collection_Template_Is_Set_On_The_Container(bool lazy)
        {
            var canvasTemplate = new FuncDataTemplate<TypeAItem>((_, _) => new Canvas { Width = 100, Height = 10 });
            var (ic, sv, _) = ShowItemsControl(lazy, ObjectItems<TypeAItem>(200), null, 100, ic => ic.DataTemplates.Add(canvasTemplate));

            ic.DataTemplates.Clear();
            ic.DataTemplates.Add(canvasTemplate);
            sv.Offset = new Vector(0, 500);
            RunLayout(ic);
            Check(ic, sv, "after the jump");

            var realized = ic.GetRealizedContainers().Cast<ContentPresenter>().ToList();
            Assert.NotEmpty(realized);
            Assert.All(realized, c =>
            {
                Assert.Same(canvasTemplate, c.ContentTemplate);
                Assert.IsType<Canvas>(c.Child);
            });
        }

        // The ItemsControl caches the template per item type (cleared only on an ItemTemplate change), so both
        // panels keep the old template after a swap in DataTemplates.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Template_Swapped_In_DataTemplates_At_Runtime(bool lazy)
        {
            var (ic, sv, _) = ShowItemsControl(lazy, ObjectItems<TypeAItem>(200), null, 100,
                ic => ic.DataTemplates.Add(new FuncDataTemplate<TypeAItem>((_, _) => new Canvas { Width = 100, Height = 10 })));
            Assert.All(ic.GetRealizedContainers().Cast<ContentPresenter>(), c => Assert.IsType<Canvas>(c.Child));

            ic.DataTemplates.Clear();
            ic.DataTemplates.Add(new FuncDataTemplate<TypeAItem>((_, _) => new Border { Width = 100, Height = 10 }));
            sv.Offset = new Vector(0, 1000);
            RunLayout(ic);

            var children = ic.GetRealizedContainers().Cast<ContentPresenter>().Select(c => c.Child?.GetType().Name).Distinct().ToList();
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")}: children after the swap: {string.Join(", ", children)}");
            Assert.Equal(new[] { Expectations.SwappedTemplateChild }, children);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ItemTemplate_Is_Applied_To_The_Container(bool lazy)
        {
            var template = CanvasTemplate(enableVirtualization: true, typeof(TypeAItem));
            var (ic, _, _) = ShowItemsControl(lazy, ObjectItems<TypeAItem>(200), template, 100);

            var realized = ic.GetRealizedContainers().Cast<ContentPresenter>().ToList();
            Assert.NotEmpty(realized);
            Assert.All(realized, c => Assert.Same(template, c.ContentTemplate));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DataTemplates_Collection_Does_Not_Cause_Repeated_Measures(bool lazy)
        {
            var measures = new int[1];
            var (ic, sv, _) = ShowItemsControl(lazy, ObjectItems<TypeAItem>(200), null, 100,
                ic => ic.DataTemplates.Add(new FuncDataTemplate<TypeAItem>((_, _) => new CountingCanvas(measures))));
            var realizedCount = ic.GetRealizedContainers().Count();
            Assert.True(realizedCount > 0);

            var afterInitial = measures[0];
            RunLayout(ic);
            Assert.Equal(afterInitial, measures[0]);

            measures[0] = 0;
            sv.Offset = new Vector(0, 500);
            RunLayout(ic);
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")}: {measures[0]} child measures for {realizedCount} realized rows");
            Assert.True(measures[0] <= realizedCount * 3, $"{measures[0]} child measures for {realizedCount} rows");

            var afterScroll = measures[0];
            RunLayout(ic);
            Assert.Equal(afterScroll, measures[0]);
        }

        // DisplayMemberBinding keys by the item type, without a pool cap: the pool holds every recycled container.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DisplayMemberBinding_Keys_And_Caps_Consistently(bool lazy)
        {
            var items = new ObservableCollection<object>(Enumerable.Range(0, 200).Select(i => (object)new TypeAItem { Name = $"Item {i}" }));
            var theme = new ControlTheme(typeof(ContentPresenter)) { Setters = { new Setter(Layoutable.HeightProperty, 10.0) } };
            var (ic, _, _) = ShowItemsControl(lazy, items, null, 400, ic =>
            {
                ic.DisplayMemberBinding = new Binding(nameof(TypeAItem.Name));
                ic.ItemContainerTheme = theme;
            });

            Assert.True(ic.ItemContainerGenerator.NeedsContainer(items[0], 0, out var key));
            Assert.Equal(typeof(TypeAItem), key);
            var recycled = RecycleByShrinking(ic, items, keep: 10);
            Assert.True(recycled > 5, $"only {recycled} recycled");
            Assert.Equal(recycled, Pooled(ic).Count);
        }

        public static TheoryData<bool, bool> NestedPanels => new() { { false, false }, { true, true }, { true, false }, { false, true } };

        [Theory]
        [MemberData(nameof(NestedPanels))]
        public void Nested_Virtualized_List_Retargets_When_Outer_Container_Is_Recycled(bool outerLazy, bool innerLazy)
        {
            var (ic, sv, items) = CreateNested(outerLazy, innerLazy);
            var container = Assert.IsType<ContentPresenter>(ic.ContainerFromIndex(0));
            var inner = Assert.IsType<ListBox>(container.Child);
            var first = items[0];
            Assert.Same(first.Children, inner.ItemsSource);
            var innerItems = RealizedInnerItems(inner);
            Assert.NotEmpty(innerItems);
            Assert.True(innerItems.Count < first.Children.Count, $"{innerItems.Count} of {first.Children.Count} realized");
            Assert.All(innerItems, x => Assert.Contains(x, first.Children));

            sv.Offset = new Vector(0, 1000);
            RunLayout(ic);
            Check(ic, sv, "after the jump");

            var index = ic.IndexFromContainer(container);
            Assert.True(index > 0, "The outer container was not reused for a different item.");
            Assert.Same(inner, container.Child);
            var reused = items[index];
            Assert.Same(reused.Children, inner.ItemsSource);
            var reusedInnerItems = RealizedInnerItems(inner);
            Assert.NotEmpty(reusedInnerItems);
            Assert.All(reusedInnerItems, x => Assert.Contains(x, reused.Children));
            Assert.Empty(reusedInnerItems.Intersect(first.Children));
            Assert.Equal(reusedInnerItems.Count, reusedInnerItems.Distinct().Count());
            var containers = inner.GetRealizedContainers().ToList();
            Assert.Equal(containers.Count, containers.Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(NestedPanels))]
        public void Nested_Inner_Lists_Are_Never_Shared_Between_Outer_Containers(bool outerLazy, bool innerLazy)
        {
            var (ic, sv, items) = CreateNested(outerLazy, innerLazy);
            sv.Offset = new Vector(0, 1000);
            RunLayout(ic);

            var outer = ic.GetRealizedContainers().Cast<ContentPresenter>().ToList();
            Assert.True(outer.Count > 1);
            var innerLists = new List<ListBox>();
            var innerContainers = new List<Control>();
            foreach (var c in outer)
            {
                var index = ic.IndexFromContainer(c);
                var inner = Assert.IsType<ListBox>(c.Child);
                innerLists.Add(inner);
                innerContainers.AddRange(inner.GetRealizedContainers());
                Assert.Same(items[index].Children, inner.ItemsSource);
                Assert.All(RealizedInnerItems(inner), x => Assert.Contains(x, items[index].Children));
            }
            Assert.Equal(innerLists.Count, innerLists.Distinct().Count());
            Assert.Equal(innerContainers.Count, innerContainers.Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(NestedPanels))]
        public void Nested_Virtualization_Survives_An_Outer_Scroll_Roundtrip(bool outerLazy, bool innerLazy)
        {
            var (ic, sv, items) = CreateNested(outerLazy, innerLazy);
            var container = Assert.IsType<ContentPresenter>(ic.ContainerFromIndex(0));
            var inner = Assert.IsType<ListBox>(container.Child);
            var first = items[0];

            sv.Offset = new Vector(0, 1000);
            RunLayout(ic);
            sv.Offset = new Vector(0, 0);
            RunLayout(ic);
            Check(ic, sv, "back at the top");

            var back = Assert.IsType<ContentPresenter>(ic.ContainerFromIndex(0));
            var innerBack = Assert.IsType<ListBox>(back.Child);
            Assert.Same(first, back.Content);
            Assert.Same(first.Children, innerBack.ItemsSource);
            var innerItems = RealizedInnerItems(innerBack);
            Assert.NotEmpty(innerItems);
            Assert.All(innerItems, x => Assert.Contains(x, first.Children));
            Assert.Equal(innerItems.Count, innerItems.Distinct().Count());
            Assert.Same(inner, container.Child);
        }

        // An inner list scrolled by the user, then its outer row scrolled out and back: the inner list shows the rows
        // of its item from a valid offset. The inner offset is not part of the item, so it is not kept.
        [Theory]
        [MemberData(nameof(NestedPanels))]
        public void Scrolled_Inner_List_Shows_Valid_Rows_After_The_Outer_Row_Comes_Back(bool outerLazy, bool innerLazy)
        {
            var (ic, sv, items) = CreateNested(outerLazy, innerLazy);
            var inner = (ListBox)((ContentPresenter)ic.ContainerFromIndex(1)!).Child!;
            var innerSv = inner.GetVisualDescendants().OfType<ScrollViewer>().First();
            ScrollBy(innerSv, 55);
            RunLayout(ic);
            Assert.DoesNotContain(items[1].Children[0], RealizedInnerItems(inner));

            for (var i = 0; i < 10; ++i)
            {
                ScrollBy(sv, 100);
                RunLayout(ic);
            }
            for (var i = 0; i < 10; ++i)
            {
                ScrollBy(sv, -100);
                RunLayout(ic);
            }
            Check(ic, sv, "after the round trip");

            var back = (ListBox)((ContentPresenter)ic.ContainerFromIndex(1)!).Child!;
            var backSv = back.GetVisualDescendants().OfType<ScrollViewer>().First();
            var shown = RealizedInnerItems(back);
            Assert.All(shown, x => Assert.Contains(x, items[1].Children));
            Assert.InRange(backSv.Offset.Y, 0, Math.Max(0, backSv.Extent.Height - backSv.Viewport.Height) + 0.01);
            _output.WriteLine($"outer {(outerLazy ? "Lazy" : "VSP")}, inner {(innerLazy ? "Lazy" : "VSP")}: inner offset after the round trip {backSv.Offset.Y}, same list {ReferenceEquals(back, inner)}");
        }

        // =========================================================================================================
        // SelectedIndex / SelectedItem from code to a row that is not on the screen
        // =========================================================================================================

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public void Selecting_An_Off_Screen_Row_From_Code_Scrolls_It_Into_View(bool lazy, bool periodic, bool byItem)
        {
            var items = Items(200, i => periodic ? 10 + 10 * (i % 3) : 10);
            var (window, lb, sv) = ShowListBox(lazy, items, 0);

            void Select(int index)
            {
                if (byItem)
                    lb.SelectedItem = items[index];
                else
                    lb.SelectedIndex = index;
                RunLayout(lb);
                Check(lb, sv, $"after selecting {index}");
            }

            // Below: aligned with the bottom.
            Select(120);
            var c = lb.ContainerFromIndex(120)!;
            Assert.True(((ListBoxItem)c).IsSelected);
            Assert.Equal(sv.Viewport.Height, Top(c, sv) + c.Bounds.Height, 1);

            // Above: aligned with the top.
            Select(40);
            c = lb.ContainerFromIndex(40)!;
            Assert.Equal(0, Top(c, sv), 1);

            // On the screen: no move.
            var top = Top(lb.ContainerFromIndex(42)!, sv);
            Select(42);
            Assert.Equal(top, Top(lb.ContainerFromIndex(42)!, sv), 1);
            window.Close();
        }

        // =========================================================================================================
        // Shift ranges (as ListBoxTests_Multiple), also across recycled rows
        // =========================================================================================================

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Shift_Arrow_Key_Selects_Range(bool lazy)
        {
            var (window, lb, _) = ShowListBox(lazy, Items(100), 0, multiple: true);
            FocusRow(lb, 0);

            Press(lb, Key.Down, KeyModifiers.Shift);
            Assert.Equal(new[] { 0, 1 }, SelectedIndexes(lb));
            Assert.Equal(new[] { 0, 1 }, SelectedContainers(lb));
            Assert.True(lb.ContainerFromIndex(1)!.IsFocused);

            Press(lb, Key.Down, KeyModifiers.Shift);
            Assert.Equal(new[] { 0, 1, 2 }, SelectedIndexes(lb));
            Assert.Equal(new[] { 0, 1, 2 }, SelectedContainers(lb));
            Assert.True(lb.ContainerFromIndex(2)!.IsFocused);

            Press(lb, Key.Up, KeyModifiers.Shift);
            Assert.Equal(new[] { 0, 1 }, SelectedIndexes(lb));
            Assert.Equal(new[] { 0, 1 }, SelectedContainers(lb));
            Assert.True(lb.ContainerFromIndex(1)!.IsFocused);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Shift_Down_Key_Selects_Range_End_From_Focus(bool lazy)
        {
            var (window, lb, _) = ShowListBox(lazy, Items(100), 0, multiple: true);
            lb.SelectedIndex = 0;
            RunLayout(lb);

            lb.ContainerFromIndex(1)!.Focus();
            Press(lb, Key.Down, KeyModifiers.Shift);

            Assert.Equal(new[] { 0, 1, 2 }, SelectedIndexes(lb));
            Assert.Equal(new[] { 0, 1, 2 }, SelectedContainers(lb));
            Assert.True(lb.ContainerFromIndex(2)!.IsFocused);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Shift_Down_Key_Selects_Range_End_From_Focus_Moved_With_Ctrl_Key(bool lazy)
        {
            var (window, lb, _) = ShowListBox(lazy, Items(100), 0, multiple: true);
            FocusRow(lb, 0);

            Press(lb, Key.Down, KeyModifiers.Shift);
            Assert.Equal(new[] { 0, 1 }, SelectedIndexes(lb));
            Assert.True(lb.ContainerFromIndex(1)!.IsFocused);

            Press(lb, Key.Down, KeyModifiers.Control);
            Assert.Equal(new[] { 0, 1 }, SelectedIndexes(lb));
            Assert.Equal(new[] { 0, 1 }, SelectedContainers(lb));
            Assert.True(lb.ContainerFromIndex(2)!.IsFocused);

            Press(lb, Key.Down, KeyModifiers.Shift);
            Assert.Equal(new[] { 0, 1, 2, 3 }, SelectedIndexes(lb));
            Assert.Equal(new[] { 0, 1, 2, 3 }, SelectedContainers(lb));
            Assert.True(lb.ContainerFromIndex(3)!.IsFocused);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Shift_Down_Across_Recycled_Rows_Selects_The_Whole_Range(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, Items(100), 0, multiple: true);
            FocusRow(lb, 0);

            for (var i = 0; i < 25; ++i)
                Press(lb, Key.Down, KeyModifiers.Shift);

            Assert.Equal(Enumerable.Range(0, 26), SelectedIndexes(lb));
            Assert.True(lb.ContainerFromIndex(25)!.IsFocused);
            AssertSelectedContainersMatch(lb);

            ScrollBy(sv, -1000);
            RunLayout(lb);
            Check(lb, sv, "back at the top");
            AssertSelectedContainersMatch(lb);
            Assert.Equal(Enumerable.Range(0, 10), SelectedContainers(lb));

            // Shift+Up from the focused row (now scrolled out) shrinks the range. The VSP keeps the row, but the key
            // does nothing: its result is the expectation.
            Press(lb, Key.Up, KeyModifiers.Shift);
            if (!lazy)
            {
                Assert.Equal(Enumerable.Range(0, 26), SelectedIndexes(lb));
                window.Close();
                return;
            }
            Assert.Equal(Enumerable.Range(0, 25), SelectedIndexes(lb));
            Assert.True(lb.ContainerFromIndex(24)!.IsFocused);
            AssertSelectedContainersMatch(lb);
            window.Close();
        }

        // =========================================================================================================
        // WrapSelection, SelectionMode Toggle
        // =========================================================================================================

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WrapSelection_Should_Wrap(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, Items(100), 0);
            lb.WrapSelection = true;
            FocusRow(lb, 0);

            Press(lb, Key.Up);
            Assert.Equal(99, lb.SelectedIndex);
            Assert.True(lb.ContainerFromIndex(99)!.IsFocused);
            AssertFullyVisible(lb.ContainerFromIndex(99)!, sv);

            Press(lb, Key.Up);
            Assert.Equal(98, lb.SelectedIndex);

            Press(lb, Key.Down);
            Assert.Equal(99, lb.SelectedIndex);

            Press(lb, Key.Down);
            Assert.Equal(0, lb.SelectedIndex);
            Assert.True(lb.ContainerFromIndex(0)!.IsFocused);
            AssertFullyVisible(lb.ContainerFromIndex(0)!, sv);

            lb.WrapSelection = false;
            Press(lb, Key.Up);
            Assert.Equal(0, lb.SelectedIndex);
            Check(lb, sv, "at the end");
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Toggle_Selection_Survives_Recycling(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, Items(100), 0);
            lb.SelectionMode = SelectionMode.Toggle;
            var mouse = new MouseTestHelper();

            void Away_And_Back()
            {
                ScrollBy(sv, 500);
                RunLayout(lb);
                AssertSelectedContainersMatch(lb);
                ScrollBy(sv, -500);
                RunLayout(lb);
                Check(lb, sv, "back at the top");
                AssertSelectedContainersMatch(lb);
            }

            var row2 = (ListBoxItem)lb.ContainerFromIndex(2)!;
            mouse.Click(row2, row2);
            Assert.Equal(2, lb.SelectedIndex);
            Away_And_Back();
            Assert.Equal(new[] { 2 }, SelectedContainers(lb));

            row2 = (ListBoxItem)lb.ContainerFromIndex(2)!;
            mouse.Click(row2, row2);
            Assert.Equal(-1, lb.SelectedIndex);
            Away_And_Back();
            Assert.Empty(SelectedContainers(lb));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SelectionMode_Change_From_Multiple_To_Single_Updates_Recycled_Rows(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, Items(100), 0, multiple: true);
            lb.Selection.Select(1);
            lb.Selection.Select(3);
            lb.Selection.Select(55);
            RunLayout(lb);
            ScrollBy(sv, 500);
            RunLayout(lb);
            AssertSelectedContainersMatch(lb);

            lb.SelectionMode = SelectionMode.Single;
            RunLayout(lb);
            AssertSelectedContainersMatch(lb);
            ScrollBy(sv, -500);
            RunLayout(lb);
            Check(lb, sv, "back at the top");
            AssertSelectedContainersMatch(lb);
            Assert.True(lb.Selection.SelectedIndexes.Count <= 1);
            window.Close();
        }

        // =========================================================================================================
        // Arrow keys without a selection, Ctrl+arrow, Tab into the list
        // =========================================================================================================

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Down_Key_From_No_Selection_And_No_Focus_Selects_From_Start(bool lazy)
        {
            var (window, lb, _) = ShowListBox(lazy, Items(100), 0);

            Press(lb, Key.Down);

            Assert.Equal(0, lb.SelectedIndex);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Down_Key_From_No_Selection_Selects_From_Focus(bool lazy)
        {
            var (window, lb, _) = ShowListBox(lazy, Items(100), 0);

            lb.ContainerFromIndex(1)!.Focus();
            Press(lb, Key.Down);

            Assert.Equal(2, lb.SelectedIndex);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Ctrl_Down_Key_Moves_Focus_But_Not_Selection(bool lazy)
        {
            var (window, lb, sv) = ShowListBox(lazy, Items(100), 0);
            FocusRow(lb, 0);

            Press(lb, Key.Down, KeyModifiers.Control);

            Assert.Equal(0, lb.SelectedIndex);
            Assert.True(lb.ContainerFromIndex(1)!.IsFocused);

            // Past the last visible row: the focused row is brought into view, the selection stays.
            for (var i = 0; i < 12; ++i)
                Press(lb, Key.Down, KeyModifiers.Control);
            Assert.Equal(0, lb.SelectedIndex);
            Assert.True(lb.ContainerFromIndex(13)!.IsFocused);
            AssertFullyVisible(lb.ContainerFromIndex(13)!, sv);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Tab_Moves_To_First_Item_When_No_Anchor_Element_Selected(bool lazy)
        {
            var (window, lb, button) = ShowListBoxWithButton(lazy, Items(100));

            button.Focus();
            Press(button, Key.Tab);

            Assert.Same(lb.ContainerFromIndex(0), FocusedElement(lb));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Tab_Moves_To_Anchor_Element(bool lazy)
        {
            var (window, lb, button) = ShowListBoxWithButton(lazy, Items(100));

            button.Focus();
            lb.Selection.AnchorIndex = 1;
            Press(button, Key.Tab);

            var item = lb.ContainerFromIndex(1);
            Assert.NotNull(item);
            Assert.Same(item, FocusedElement(lb));

            Press(item!, Key.Tab);
            Assert.Same(button, FocusedElement(lb));

            lb.Selection.AnchorIndex = 2;
            Press(button, Key.Tab);
            Assert.Same(lb.ContainerFromIndex(2), FocusedElement(lb));
            window.Close();
        }

        [Theory]
        [InlineData(false, 0d)]
        [InlineData(false, 0.5d)]
        [InlineData(true, 0d)]
        [InlineData(true, 0.5d)]
        public void Focused_Element_Losing_Focus_Does_Not_Reset_Selection(bool lazy, double cache)
        {
            var (window, lb, sv) = ShowListBox(lazy, Items(100), cache);
            FocusRow(lb, 0);

            ScrollBy(sv, 500);
            RunLayout(lb);
            var newFocused = lb.GetRealizedContainers().First();
            newFocused.Focus();
            RunLayout(lb);

            Assert.Equal(0, lb.SelectedIndex);
            Assert.True(newFocused.IsFocused);
            Check(lb, sv, "after the focus change");
            window.Close();
        }

        // =========================================================================================================
        // Tab into the list after an insert moved the anchor row
        // =========================================================================================================

        // SelectingItemsControl moves the anchor index in its own CollectionChanged handler, before the panel gives
        // its rows the new indexes, and sets TabOnceActiveElement = ContainerFromIndex(new anchor): the row that
        // still has the old index, i.e. the row below the selected one. Tab then lands on Item 24 (index 26) instead
        // of Item 23 (index 25), also with a layout between the insert and the Tab.
        public const string TabAfterInsertUsesOldIndexes =
            "After an insert above, TabOnceActiveElement is set from old indexes: Tab lands below the selected row";

        [Theory]
        [InlineData(true, false, Skip = TabAfterInsertUsesOldIndexes)]
        [InlineData(true, true, Skip = TabAfterInsertUsesOldIndexes)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void Tab_Into_The_List_After_An_Insert_Lands_On_The_Selected_Row(bool lazy, bool layoutBeforeTab)
        {
            var items = Items(100);
            var (window, lb, button) = ShowListBoxWithButton(lazy, items);
            var sv = lb.GetVisualDescendants().OfType<ScrollViewer>().First();
            ScrollBy(sv, 200);
            RunLayout(lb);
            var selected = items[23];
            FocusRow(lb, 23);
            button.Focus();
            RunLayout(lb);

            items.Insert(0, new Item(-1, 10));
            items.Insert(21, new Item(-2, 10));
            if (layoutBeforeTab)
                RunLayout(lb);
            Press(button, Key.Tab);

            var focused = FocusedElement(lb) as Control;
            Assert.NotNull(focused);
            _output.WriteLine($"{(lazy ? "Lazy" : "VSP")} layout {layoutBeforeTab}: Tab lands on {focused!.DataContext} at index {lb.IndexFromContainer(focused)}");
            Assert.Same(lazy ? selected : Expectations.VspTabAfterInsert(items, layoutBeforeTab), focused!.DataContext);
            Assert.Equal(lazy ? 25 : 26, lb.IndexFromContainer(focused));
            AssertFullyVisible(focused, sv);
            window.Close();
        }

        // =========================================================================================================
        // The ItemsScroll API on Lazy, the VSP fallback and the StackPanel fallback
        // =========================================================================================================

        [Theory]
        [MemberData(nameof(ThreePanels))]
        public void Visible_Indexes_Match_The_Screen_At_Each_Position(string panel)
        {
            var (window, ic, sv, _) = CreateApiTarget(panel, 100, i => 40 + 30 * (i % 5));
            var info = ItemsScroll.Get(ic);

            AssertVisibleIndexes(ic, sv, info, "top");
            foreach (var step in new[] { 37.0, 250, 13, 1234, -400 })
            {
                info.ScrollBy(step);
                RunLayout(ic);
                AssertVisibleIndexes(ic, sv, info, $"after ScrollBy({step})");
            }
            info.ScrollToItem(60, ItemAlignment.Center);
            RunLayout(ic);
            AssertVisibleIndexes(ic, sv, info, "after ScrollToItem(60)");
            info.ScrollToEnd();
            RunLayout(ic);
            AssertVisibleIndexes(ic, sv, info, "at the end");
            Assert.Equal(99, info.LastVisibleIndex);
            window.Close();
        }

        [Theory]
        [InlineData(Lazy)]
        [InlineData(Vsp)]
        [InlineData(Stack)]
        public void Visible_Offset_Matches_The_Screen_After_A_Fractional_Scroll(string panel)
        {
            var (window, ic, sv, _) = CreateApiTarget(panel, 100, i => 40 + 30 * (i % 5));
            var info = ItemsScroll.Get(ic);

            foreach (var step in new[] { 37.0, 0.5, 0.25, 100.5 })
            {
                info.ScrollBy(step);
                RunLayout(ic);
                AssertVisibleIndexes(ic, sv, info, $"after ScrollBy({step})");
            }
            window.Close();
        }

        // Lazy and the StackPanel are at the same pixel position after the same relative scrolls.
        [Fact]
        public void Lazy_And_StackPanel_Report_The_Same_Numbers()
        {
            var results = new List<List<(int, double, int)>>();
            foreach (var panel in new[] { Lazy, Stack, Vsp })
            {
                var (window, ic, sv, _) = CreateApiTarget(panel, 100, i => 40 + 30 * (i % 5));
                var info = ItemsScroll.Get(ic);
                var list = new List<(int, double, int)>();
                foreach (var step in new[] { 37.0, 250, 13, 333, -400 })
                {
                    info.ScrollBy(step);
                    RunLayout(ic);
                    list.Add((info.FirstVisibleIndex, Math.Round(info.FirstVisibleOffset, 3), info.LastVisibleIndex));
                }
                results.Add(list);
                _output.WriteLine($"{panel}: {string.Join(" ", list)}");
                window.Close();
            }
            Assert.Equal(results[0], results[1]);
            Assert.Equal(results[0], results[2]);
        }

        [Theory]
        [InlineData(Lazy, 3)]
        [InlineData(Lazy, 5)]
        [InlineData(Lazy, 6)]
        [InlineData(Vsp, 3)]
        [InlineData(Vsp, 5)]
        [InlineData(Vsp, 6)]
        [InlineData(Stack, 3)]
        [InlineData(Stack, 5)]
        [InlineData(Stack, 6)]
        public void CanScroll_With_A_Short_List(string panel, int count)
        {
            // Viewport 500, rows of 100: 3 rows are shorter than the viewport, 5 fill it, 6 are one row longer.
            var (window, ic, sv, _) = CreateApiTarget(panel, count, _ => 100);
            var info = ItemsScroll.Get(ic);

            Assert.False(info.CanScrollBackward);
            Assert.Equal(count > 5, info.CanScrollForward);
            Assert.Equal(0, info.FirstVisibleIndex);
            Assert.Equal(Math.Min(count, 5) - 1, info.LastVisibleIndex);

            info.ScrollToEnd();
            RunLayout(ic);
            Assert.Equal(count > 5, info.CanScrollBackward);
            Assert.False(info.CanScrollForward);
            Assert.Equal(count - 1, info.LastVisibleIndex);
            window.Close();
        }

        [Theory]
        [MemberData(nameof(ThreePanels))]
        public void CanScroll_In_The_Middle_And_At_The_End_With_Mixed_Heights(string panel)
        {
            var (window, ic, sv, _) = CreateApiTarget(panel, 100, i => 40 + 30 * (i % 5));
            var info = ItemsScroll.Get(ic);

            info.ScrollToItem(50);
            RunLayout(ic);
            Assert.True(info.CanScrollBackward);
            Assert.True(info.CanScrollForward);

            info.ScrollToEnd();
            RunLayout(ic);
            Assert.True(info.CanScrollBackward);
            Assert.False(info.CanScrollForward);
            var last = ic.ContainerFromIndex(99)!;
            Assert.Equal(sv.Viewport.Height, Top(last, sv) + last.Bounds.Height, 1);

            // One pixel up from the end: forward again.
            info.ScrollBy(-1);
            RunLayout(ic);
            Assert.True(info.CanScrollForward);
            info.ScrollToStart();
            RunLayout(ic);
            Assert.False(info.CanScrollBackward);
            Assert.True(info.CanScrollForward);
            window.Close();
        }

        [Theory]
        [MemberData(nameof(ThreePanels))]
        public void Scrolled_Delta_For_A_Wheel_Step(string panel)
        {
            var (window, ic, sv, _) = CreateApiTarget(panel, 100, i => 40 + 30 * (i % 5));
            var info = ItemsScroll.Get(ic);
            var deltas = Record(info);

            Wheel(sv, 1);
            RunLayout(ic);
            Wheel(sv, -1);
            RunLayout(ic);

            Assert.Equal(new[] { 50.0, -50.0 }, deltas.Select(x => Math.Round(x, 3)));
            Assert.Equal(0, info.FirstVisibleIndex);
            window.Close();
        }

        public enum NoMove { InsertAbove, RemoveAbove, AverageChange }

        [Theory]
        [InlineData(Lazy, NoMove.InsertAbove)]
        [InlineData(Lazy, NoMove.RemoveAbove)]
        [InlineData(Lazy, NoMove.AverageChange)]
        [InlineData(Vsp, NoMove.InsertAbove)]
        [InlineData(Vsp, NoMove.RemoveAbove)]
        [InlineData(Vsp, NoMove.AverageChange)]
        [InlineData(Stack, NoMove.InsertAbove)]
        [InlineData(Stack, NoMove.RemoveAbove)]
        [InlineData(Stack, NoMove.AverageChange)]
        public void Scrolled_Is_Not_Raised_When_The_Content_Does_Not_Move(string panel, NoMove change)
        {
            var (window, ic, sv, rows) = CreateApiTarget(panel, 200, _ => 100);
            var info = ItemsScroll.Get(ic);
            info.ScrollToItem(100);
            RunLayout(ic);
            var row = rows[100];
            var deltas = Record(info);

            switch (change)
            {
                case NoMove.InsertAbove: rows.Insert(10, new Item(-1, 100)); break;
                case NoMove.RemoveAbove: rows.RemoveAt(10); break;
                case NoMove.AverageChange:
                    // Rows far below the screen shrink (the realized average changes for Lazy and the VSP).
                    foreach (var r in rows.Skip(103).Take(5))
                        r.Height = 20;
                    break;
            }
            RunLayout(ic);

            var top = Top(ContainerOf(ic, row)!, sv);
            _output.WriteLine($"{panel} {change}: deltas [{string.Join(", ", deltas)}], row top {top}");
            var expected = Expectations.NoMoveScrolled(panel, change);
            Assert.Equal(expected.Top, top, 1);
            Assert.Equal(expected.Deltas, deltas.Select(x => Math.Round(x, 1)));
            window.Close();
        }

        // A jump (ScrollToItem far away): Lazy raises no Scrolled; the fallback has no row on the screen before and
        // after, so it reports the Offset change.
        [Theory]
        [MemberData(nameof(ThreePanels))]
        public void Scrolled_Delta_For_A_Jump(string panel)
        {
            var (window, ic, sv, _) = CreateApiTarget(panel, 200, _ => 100);
            var info = ItemsScroll.Get(ic);
            var deltas = Record(info);

            info.ScrollToItem(150);
            RunLayout(ic);
            var forward = deltas.ToList();
            deltas.Clear();
            info.ScrollToItem(20);
            RunLayout(ic);
            var backward = deltas.ToList();

            _output.WriteLine($"{panel}: jump forward [{string.Join(", ", forward)}], jump back [{string.Join(", ", backward)}]");
            var expected = Expectations.JumpScrolled(panel);
            Assert.Equal(expected.Forward, forward);
            Assert.Equal(expected.Backward, backward);
            Assert.Equal(20, info.FirstVisibleIndex);
            window.Close();
        }

        [Theory]
        [MemberData(nameof(ThreePanels))]
        public void ScrollFraction_Grows_While_Scrolling_Down(string panel)
        {
            var (window, ic, sv, _) = CreateApiTarget(panel, 200, i => i < 100 ? 40 : 40 + 30 * (i % 5));
            var info = ItemsScroll.Get(ic);
            Assert.Equal(0, info.ScrollFraction);

            var fractions = new List<double> { info.ScrollFraction };
            for (var i = 0; i < 2000 && info.CanScrollForward; ++i)
            {
                info.ScrollBy(97);
                RunLayout(ic);
                fractions.Add(info.ScrollFraction);
            }

            Assert.False(info.CanScrollForward);
            Assert.Equal(1, fractions[^1], 3);
            var drops = fractions.Zip(fractions.Skip(1), (a, b) => b - a).Where(d => d < -1e-9).ToList();
            _output.WriteLine($"{panel}: {fractions.Count} steps, drops [{string.Join(", ", drops)}]");
            if (panel == Vsp)
            {
                // The VSP's Offset/Extent estimate changes when the 40-160 px region comes in: the fraction drops
                // 9 times, at most 0.037 (by design; ScrollFraction is an estimate).
                Assert.NotEmpty(drops);
                Assert.All(drops, d => Assert.True(d > -0.05));
            }
            else
                Assert.Empty(drops);
            Assert.All(fractions, f => Assert.InRange(f, 0, 1));
            window.Close();
        }

        [Theory]
        [MemberData(nameof(ThreePanels))]
        public void ItemsScroll_Get_Before_The_Template_And_After_Each_ItemsPanel_Change(string panel)
        {
            var rows = Items(100, _ => 100);
            var ic = CreateApiItemsControl(panel, rows);
            var info = ItemsScroll.Get(ic);

            // No template, no presenter, no panel.
            Assert.Equal(-1, info.FirstVisibleIndex);
            Assert.Equal(-1, info.LastVisibleIndex);
            Assert.Equal(0, info.FirstVisibleOffset);
            Assert.Equal(0, info.ScrollFraction);
            Assert.False(info.CanScrollBackward);
            Assert.False(info.CanScrollForward);
            info.ScrollBy(50); // dropped: no rows

            var window = new Window { Width = 400, Height = 500, Content = ic };
            window.Show();
            RunLayout(window);
            Assert.Same(info, ItemsScroll.Get(ic));
            Assert.Equal((0, 0.0, 4), (info.FirstVisibleIndex, info.FirstVisibleOffset, info.LastVisibleIndex));

            foreach (var next in new[] { Lazy, Vsp, Stack, panel }.Where(p => p != panel).Append(panel))
            {
                ic.ItemsPanel = PanelTemplate(next);
                RunLayout(ic);
                Assert.Same(info, ItemsScroll.Get(ic));
                var sv = ic.GetVisualDescendants().OfType<ScrollViewer>().Single();
                AssertVisibleIndexes(ic, sv, info, $"{panel} -> {next}");
                info.ScrollToStart();
                RunLayout(ic);
                info.ScrollBy(130);
                RunLayout(ic);
                Assert.Equal((1, 30.0, 6), (info.FirstVisibleIndex, Math.Round(info.FirstVisibleOffset, 3), info.LastVisibleIndex));
                Assert.True(info.CanScrollBackward);
            }
            window.Close();
        }

        // =========================================================================================================
        // Expectations where the panels differ by design
        // =========================================================================================================

        private static class Expectations
        {
            // The VSP keeps its Offset for an insert above (the content moves one row down; by design) and
            // the StackPanel has no anchor at all: the fallback reports the real movement.
            public static (double Top, double[] Deltas) NoMoveScrolled(string panel, NoMove change) => (panel, change) switch
            {
                (Vsp, NoMove.InsertAbove) => (100, new[] { -100.0 }),
                (Stack, NoMove.InsertAbove) => (100, new[] { -100.0 }),
                (Stack, NoMove.RemoveAbove) => (-100, new[] { 100.0 }),
                _ => (0, Array.Empty<double>()),
            };

            // Lazy raises no Scrolled for a jump. The fallback's ScrollToItem is ScrollIntoView (bottom-aligned,
            // 14600 px), a layout, then ScrollBy to the top (400 px): two events; back up, ScrollIntoView is already
            // top-aligned: one event (-13000).
            public static (double[] Forward, double[] Backward) JumpScrolled(string panel) => panel == Lazy
                ? (Array.Empty<double>(), Array.Empty<double>())
                : (new[] { 14600.0, 400 }, new[] { -13000.0 });

            // 20 realized rows (CacheLength 0.5) keep "A" after the change; the first ScrollBy(100) still shows 20,
            // the second 5, then 0; no pooled "A" row is left.
            public static (int AfterChange, int WhileScrolling, int Pooled) VspItemTemplateChange => (20, 20, 0);

            public static (int AfterChange, int WhileScrolling) VspThemeChange => (20, 20);

            // Item 24, the row below the selected one: the same as Lazy.
            public static Item VspTabAfterInsert(IList<Item> items, bool layoutBeforeTab) => items[26];

            public const string SwappedTemplateChild = "Canvas";
        }

        // =========================================================================================================
        // Helpers
        // =========================================================================================================

        public class Item : INotifyPropertyChanged
        {
            private double _height;

            public Item(int id, double height) => (Id, _height) = (id, height);

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

            public override string ToString() => $"Item {Id}";
        }

        private static ObservableCollection<Item> Items(int count, Func<int, double>? height = null) =>
            new(Enumerable.Range(0, count).Select(i => new Item(i, height?.Invoke(i) ?? 10)));

        private static IDataTemplate RowTemplate() =>
            new FuncDataTemplate<Item>((_, _) => new Border { [!Layoutable.HeightProperty] = new Binding(nameof(Item.Height)) });

        private static IDataTemplate TaggedTemplate(string tag) =>
            new FuncDataTemplate<Item>((_, _) => new Border { Tag = tag, [!Layoutable.HeightProperty] = new Binding(nameof(Item.Height)) });

        private static List<string?> RealizedTags(ItemsControl ic) =>
            ic.GetRealizedContainers().Cast<ContentPresenter>().Select(c => c.Child?.Tag as string).ToList();

        private static List<Item> RealizedItems(ItemsControl ic) =>
            ic.GetRealizedContainers().Select(c => (Item)c.DataContext!).ToList();

        private static void ScrollAndBack(ItemsControl ic, ScrollViewer sv)
        {
            for (var i = 0; i < 3; ++i)
            {
                ScrollBy(sv, 100);
                RunLayout(ic);
            }
            for (var i = 0; i < 3; ++i)
            {
                ScrollBy(sv, -100);
                RunLayout(ic);
            }
        }

        private static double Top(Control c, ScrollViewer sv) => c.TranslatePoint(default, sv)!.Value.Y;

        private static Control? ContainerOf(ItemsControl ic, object item) =>
            ic.GetRealizedContainers().FirstOrDefault(c => ReferenceEquals(c.DataContext, item));

        private static void AssertFullyVisible(Control c, ScrollViewer sv)
        {
            var top = Top(c, sv);
            Assert.InRange(top, -Tolerance, sv.Viewport.Height);
            Assert.InRange(top + c.Bounds.Height, 0, sv.Viewport.Height + Tolerance);
        }

        /// <summary>
        /// After a layout: the realized rows are contiguous, in collection order, all visible (no pooled container),
        /// show their item, and cover the viewport; 0 &lt;= Offset &lt;= max(0, Extent - Viewport); the layout pass
        /// finished.
        /// </summary>
        private static void Check(ItemsControl ic, ScrollViewer sv, string when)
        {
            if (Problem(ic, sv) is { } problem)
                Assert.Fail($"{when}: {problem}");
        }

        private static string? Problem(ItemsControl ic, ScrollViewer sv)
        {
            var panel = ic.ItemsPanelRoot!;
            if (!panel.IsMeasureValid || !panel.IsArrangeValid)
                return "the layout pass did not finish";

            var realized = ic.GetRealizedContainers().ToList();
            var count = ic.ItemCount;
            var vh = sv.Viewport.Height;
            int? prevIndex = null;
            double? prevBottom = null;
            double? firstTop = null;
            var lastIndex = -1;

            foreach (var c in realized)
            {
                var index = ic.IndexFromContainer(c);
                if (index < 0 || index >= count)
                    return $"realized container with index {index}";
                if (!ReferenceEquals(c.DataContext, ic.ItemsView[index]))
                    return $"container {index} shows {c.DataContext}";
                if (c.GetVisualParent() != panel)
                    return $"container {index} is not a child of the panel";
                if (!c.IsVisible)
                    return $"container {index} is hidden (pooled?)";
                if (prevIndex is { } p && index != p + 1)
                    return $"realized indexes {p} and {index} are not consecutive";
                prevIndex = lastIndex = index;

                var top = Top(c, sv);
                if (prevBottom is { } b && Math.Abs(top - b) > Tolerance)
                    return $"{top - b:0.##} px between row {index} and the row before";
                firstTop ??= top;
                prevBottom = top + c.Bounds.Height;
            }

            if (count > 0 && realized.Count == 0)
                return "no rows realized";
            if (firstTop is { } ft && ft > Tolerance)
                return $"{ft:0.##} px empty above the first realized row";
            if (prevBottom is { } pb && pb < vh - Tolerance &&
                !(lastIndex == count - 1 && ic.IndexFromContainer(realized[0]) == 0 && firstTop >= -Tolerance))
                return $"{vh - pb:0.##} px empty below row {lastIndex}";

            var max = Math.Max(0, sv.Extent.Height - vh);
            if (sv.Offset.Y < -0.01 || sv.Offset.Y > max + 0.01)
                return $"Offset {sv.Offset.Y} outside 0 ... {max}";
            return null;
        }

        private sealed class VirtualizingTemplate : IVirtualizingDataTemplate
        {
            private readonly Func<Control> _build;

            public VirtualizingTemplate(Func<Control> build, int maxPool = 5) => (_build, MaxPoolSizePerKey) = (build, maxPool);

            public int MaxPoolSizePerKey { get; }

            public int MinPoolSizePerKey => 0;

            public object? GetKey(object? data) => data?.GetType();

            public bool Match(object? data) => data is Item;

            public Control? Build(object? data) => Build(data, null);

            public Control? Build(object? data, Control? existing) => existing ?? _build();
        }

        // ---------------------------------------------------------------------------------------------------------
        // ListBox
        // ---------------------------------------------------------------------------------------------------------

        private static ControlTheme ListBoxItemTheme() => new(typeof(ListBoxItem))
        {
            Setters =
            {
                new Setter(ListBoxItem.TemplateProperty, new FuncControlTemplate<ListBoxItem>((_, _) => new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [~ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty),
                    [~ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentControl.ContentTemplateProperty),
                })),
                new Setter(InputElement.FocusableProperty, true),
            },
        };

        private static ListBox CreateListBox(bool multiple, IDataTemplate? template) => new()
        {
            ItemTemplate = template ?? RowTemplate(),
            ItemContainerTheme = ListBoxItemTheme(),
            SelectionMode = multiple ? SelectionMode.Multiple : SelectionMode.Single,
        };

        private static (Window, ListBox, ScrollViewer) ShowListBox(
            bool lazy, IEnumerable<Item> items, double cache, IStyle[]? styles = null, bool multiple = false, IDataTemplate? template = null)
        {
            var lb = CreateListBox(multiple, template);
            var source = items as ObservableCollection<Item> ?? new ObservableCollection<Item>(items);
            var (window, sv) = Show(lb, source, lazy, cache, 100, styles ?? Array.Empty<IStyle>());
            return (window, lb, sv);
        }

        /// <summary>A button above a 100 x 100 list, for the Tab tests.</summary>
        private static (Window, ListBox, Button) ShowListBoxWithButton(bool lazy, ObservableCollection<Item> items)
        {
            var lb = CreateListBox(false, null);
            lb.Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new ItemsPresenter
                {
                    Name = "PART_ItemsPresenter",
                    [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                }.RegisterInNameScope(ns),
            });
            lb.ItemsSource = items;
            lb.ItemsPanel = new FuncTemplate<Panel?>(() => lazy
                ? new LazyStackPanel { CacheLength = 0 }
                : new VirtualizingStackPanel { CacheLength = 0 });
            var button = new Button { Content = "Button", Height = 20, [DockPanel.DockProperty] = Dock.Top };
            var window = new Window { Width = 100, Height = 120, Content = new DockPanel { Children = { button, lb } } };
            window.Show();
            RunLayout(window);
            return (window, lb, button);
        }

        private static Control FocusRow(ListBox lb, int index)
        {
            lb.SelectedIndex = index;
            RunLayout(lb);
            var c = lb.ContainerFromIndex(index)!;
            c.Focus();
            Assert.True(c.IsFocused);
            return c;
        }

        private static IInputElement? FocusedElement(Control c) => TopLevel.GetTopLevel(c)!.FocusManager!.GetFocusedElement();

        private static void Press(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            var source = FocusedElement(target) as Interactive ?? target;
            source.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = source });
            RunLayout(target);
        }

        private static IEnumerable<int> SelectedIndexes(ListBox lb) => lb.Selection.SelectedIndexes.OrderBy(x => x).ToList();

        private static IEnumerable<int> SelectedContainers(ListBox lb) =>
            lb.GetRealizedContainers().Where(c => ((ListBoxItem)c).IsSelected).Select(lb.IndexFromContainer).OrderBy(x => x).ToList();

        private static void AssertSelectedContainersMatch(ListBox lb)
        {
            foreach (var c in lb.GetRealizedContainers())
            {
                var i = lb.IndexFromContainer(c);
                Assert.True(lb.Selection.IsSelected(i) == ((ListBoxItem)c).IsSelected, $"row {i}: IsSelected {((ListBoxItem)c).IsSelected}");
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Recycle keys, templates and nested lists
        // ---------------------------------------------------------------------------------------------------------

        private const int InnerItemHeight = 10;
        private const int OuterItemHeight = 20;

        public class OuterItem
        {
            public OuterItem(int index, int childCount)
            {
                Name = $"Outer {index}";
                Children = new ObservableCollection<InnerItem>(
                    Enumerable.Range(0, childCount).Select(i => new InnerItem($"Outer {index} / Inner {i}")));
            }

            public string Name { get; }

            public ObservableCollection<InnerItem> Children { get; }

            public override string ToString() => Name;
        }

        public class InnerItem
        {
            public InnerItem(string name) => Name = name;

            public string Name { get; }

            public override string ToString() => Name;
        }

        private sealed class FuncVirtualizingDataTemplate<T> : FuncDataTemplate<T>, IVirtualizingDataTemplate
        {
            public FuncVirtualizingDataTemplate(Func<T, INameScope, Control?> build) : base(build, supportsRecycling: true) { }

            public object? GetKey(object? data) => data?.GetType();

            public int MaxPool { get; set; } = 5;

            public int MaxPoolSizePerKey => MaxPool;

            public int MinPoolSizePerKey => 2;
        }

        private static (ItemsControl, ScrollViewer, ObservableCollection<OuterItem>) CreateNested(bool outerLazy, bool innerLazy)
        {
            var innerTemplate = new FuncVirtualizingDataTemplate<InnerItem>((_, _) => new Canvas { Width = 50, Height = InnerItemHeight });
            var outerTemplate = new FuncVirtualizingDataTemplate<OuterItem>((_, _) => new ListBox
            {
                Height = OuterItemHeight,
                ItemTemplate = innerTemplate,
                ItemsPanel = new FuncTemplate<Panel?>(() => innerLazy
                    ? new LazyStackPanel { CacheLength = 0 }
                    : new VirtualizingStackPanel { CacheLength = 0 }),
                Template = new FuncControlTemplate<ListBox>((_, ns) => new ScrollViewer
                {
                    Name = "PART_ScrollViewer",
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                    }.RegisterInNameScope(ns),
                }.RegisterInNameScope(ns)),
                [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(OuterItem.Children)),
            });
            var items = new ObservableCollection<OuterItem>(Enumerable.Range(0, 200).Select(i => new OuterItem(i, 20)));
            var ic = new ItemsControl { ItemTemplate = outerTemplate };
            var style = new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.TemplateProperty, new FuncControlTemplate<ListBoxItem>((_, ns) => new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        Height = InnerItemHeight,
                        [~ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty),
                        [~ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentControl.ContentTemplateProperty),
                    }.RegisterInNameScope(ns))),
                },
            };
            var (_, sv) = Show(ic, items, outerLazy, 0, 100, style);
            return (ic, sv, items);
        }

        private static List<object?> RealizedInnerItems(ListBox inner) =>
            inner.GetRealizedContainers().Cast<ListBoxItem>().Select(x => x.Content).ToList();

        public class TypeAItem
        {
            public string Name { get; set; } = "";

            public override string ToString() => Name;
        }

        public class KindedItem
        {
            public KindedItem(string kind) => Kind = kind;

            public string Kind { get; }
        }

        private sealed class KindTemplate : IVirtualizingDataTemplate
        {
            private readonly Func<object?, object?> _key;

            public KindTemplate(Func<object?, object?> key) => _key = key;

            public int MaxPoolSizePerKey => 100;

            public int MinPoolSizePerKey => 0;

            public object? GetKey(object? data) => _key(data);

            public bool Match(object? data) => data is KindedItem;

            public Control? Build(object? data) => Build(data, null);

            public Control? Build(object? data, Control? existing) =>
                existing ?? new Canvas { Width = 100, Height = ((KindedItem)data!).Kind == "tall" ? 20 : 10 };
        }

        private sealed class CountingCanvas : Canvas
        {
            private readonly int[] _count;

            public CountingCanvas(int[] count)
            {
                _count = count;
                Width = 100;
                Height = 10;
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                ++_count[0];
                return base.MeasureOverride(availableSize);
            }
        }

        private static Avalonia.Markup.Xaml.Templates.DataTemplate CanvasTemplate(bool enableVirtualization, Type dataType) => new()
        {
            DataType = dataType,
            EnableVirtualization = enableVirtualization,
            Content = (Func<IServiceProvider?, object?>)(_ => new TemplateResult<Control>(new Canvas { Width = 100, Height = 10 }, new NameScope())),
        };

        private static ObservableCollection<object> ObjectItems<T>(int count) where T : new() =>
            new(Enumerable.Range(0, count).Select(_ => (object)new T()));

        private static (ItemsControl, ScrollViewer, Window) ShowItemsControl(bool lazy, ObservableCollection<object> items, IDataTemplate? template,
            double size, Action<ItemsControl>? configure = null, params IStyle[] styles)
        {
            var ic = new ItemsControl { ItemTemplate = template };
            configure?.Invoke(ic);
            var (window, sv) = Show(ic, items, lazy, 0, size, styles);
            return (ic, sv, window);
        }

        private static List<Control> Pooled(ItemsControl ic) => ic.ItemsPanelRoot!.Children.Where(c => !c.IsVisible).ToList();

        /// <summary>Removes the items after <paramref name="keep"/>; returns how many realized containers left.</summary>
        private static int RecycleByShrinking(ItemsControl ic, ObservableCollection<object> items, int keep)
        {
            var before = ic.GetRealizedContainers().Count();
            Assert.True(before > keep, $"only {before} realized");
            for (var i = items.Count - 1; i >= keep; i--)
                items.RemoveAt(i);
            RunLayout(ic);
            return before - ic.GetRealizedContainers().Count();
        }

        // ---------------------------------------------------------------------------------------------------------
        // ItemsScroll API: a 400 x 500 window, as ItemsScrollTests
        // ---------------------------------------------------------------------------------------------------------

        private static ITemplate<Panel?> PanelTemplate(string panel) => new FuncTemplate<Panel?>(() => panel switch
        {
            Lazy => new LazyStackPanel(),
            Vsp => new VirtualizingStackPanel(),
            _ => new StackPanel(),
        });

        private static ItemsControl CreateApiItemsControl(string panel, ObservableCollection<Item> rows) => new()
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
            ItemsSource = rows,
            ItemsPanel = PanelTemplate(panel),
            ItemTemplate = RowTemplate(),
        };

        private static (Window, ItemsControl, ScrollViewer, ObservableCollection<Item>) CreateApiTarget(
            string panel, int count, Func<int, double> height)
        {
            var rows = Items(count, height);
            var ic = CreateApiItemsControl(panel, rows);
            var window = new Window { Width = 400, Height = 500, Content = ic };
            window.Show();
            RunLayout(window);
            return (window, ic, ic.GetVisualDescendants().OfType<ScrollViewer>().Single(), rows);
        }

        /// <summary>The visible indexes and the offset of the first row, from the containers on the screen.</summary>
        private static void AssertVisibleIndexes(ItemsControl ic, ScrollViewer sv, IItemsScrollInfo info, string when)
        {
            var vh = sv.Viewport.Height;
            var visible = ic.GetRealizedContainers()
                .Where(c => c.IsVisible)
                .Select(c => (Index: ic.IndexFromContainer(c), Top: Top(c, sv), Bottom: Top(c, sv) + c.Bounds.Height))
                .Where(x => x.Bottom > 0.001 && x.Top < vh - 0.001)
                .OrderBy(x => x.Index)
                .ToList();
            Assert.True(visible.Count > 0, $"{when}: nothing on the screen");
            Assert.True(visible[0].Index == info.FirstVisibleIndex, $"{when}: FirstVisibleIndex {info.FirstVisibleIndex}, screen {visible[0].Index}");
            Assert.True(Math.Abs(-visible[0].Top - info.FirstVisibleOffset) < 0.01,
                $"{when}: FirstVisibleOffset {info.FirstVisibleOffset}, screen {-visible[0].Top}");
            Assert.True(visible[^1].Index == info.LastVisibleIndex, $"{when}: LastVisibleIndex {info.LastVisibleIndex}, screen {visible[^1].Index}");
        }

        private static List<double> Record(IItemsScrollInfo info)
        {
            var deltas = new List<double>();
            info.Scrolled += (_, e) => deltas.Add(e.Delta);
            return deltas;
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
    }
}
