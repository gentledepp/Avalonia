using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Containers stay on their own item after an edit, black box against both panels: 100 px viewport, 10 px rows, no cache.
    /// </summary>
    public class LazyStackPanelTests_ContainerReuse : LazyStackPanelTestBase
    {
        private readonly ITestOutputHelper _output;

        public LazyStackPanelTests_ContainerReuse(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Inserting_Item_Before_Viewport_Reuses_Matching_Containers(bool lazy)
        {
            var items = new ObservableCollection<Row>(Enumerable.Range(0, 100).Select(x => new Row(x)));
            var (window, ic, sv, prepares) = CreateTarget(items, lazy);

            sv.Offset = new Vector(0, 200);
            LazyStackPanelTestTarget.RunLayout(sv);
            Assert.Equal(20, FirstRealizedIndex(ic));

            var before = ContainerByItem(ic);
            var containers = ic.GetRealizedContainers().ToHashSet();
            prepares.Count = 0;

            items.Insert(0, new Row(-1));
            LazyStackPanelTestTarget.RunLayout(sv);

            _output.WriteLine($"{Name(lazy)}: reused {Reused(ic, before)} of {before.Count}, prepares {prepares.Count}");
            Assert.True(Reused(ic, before) >= 8, $"reused {Reused(ic, before)} of {before.Count}");
            Assert.True(ic.GetRealizedContainers().Count(x => !containers.Contains(x)) <= 3, "new containers");
            AssertIndexes(ic, items);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Collection_Reset_With_Reorder_Reuses_Matching_Containers(bool lazy)
        {
            var rows = Enumerable.Range(0, 100).Select(x => new Row(x)).ToList();
            var items = new ResettingCollection<Row>(rows);
            var (window, ic, sv, prepares) = CreateTarget(items, lazy);

            Assert.Equal(0, FirstRealizedIndex(ic));

            var before = ContainerByItem(ic);
            prepares.Count = 0;

            var shuffled = rows.ToList();
            // Rows 1-9 only: the anchor item (row 0) stays at index 0, so the same rows stay on the screen.
            shuffled.Reverse(1, 9);
            items.Reset(shuffled);
            LazyStackPanelTestTarget.RunLayout(sv);

            _output.WriteLine($"{Name(lazy)}: reused {Reused(ic, before)} of {before.Count}, prepares {prepares.Count}");
            Assert.True(Reused(ic, before) >= 8, $"reused {Reused(ic, before)} of {before.Count}");
            AssertIndexes(ic, items);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reset_With_MidList_Insert_Realizes_Shifted_Items_At_Correct_Index(bool lazy)
        {
            var rows = Enumerable.Range(0, 20).Select(x => new Row(x)).ToList();
            var items = new ResettingCollection<Row>(rows);
            var (window, ic, sv, _) = CreateTarget(items, lazy, virtualizingTemplate: true);

            Assert.Equal(0, FirstRealizedIndex(ic));

            items.Reset(rows.Take(6).Concat(Enumerable.Range(0, 3).Select(x => new Row(100 + x))).Concat(rows.Skip(6)).ToList());
            LazyStackPanelTestTarget.RunLayout(sv);

            AssertIndexes(ic, items);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Disjunct_Scroll_Reuses_Containers_With_Matching_DataContext(bool lazy)
        {
            // The same 10 instances at 0-9 and at 50-59.
            var shared = Enumerable.Range(0, 10).Select(x => new Row(x)).ToList();
            var items = new ObservableCollection<Row>(shared
                .Concat(Enumerable.Range(10, 40).Select(x => new Row(x)))
                .Concat(shared)
                .Concat(Enumerable.Range(60, 40).Select(x => new Row(x))));
            var (window, ic, sv, prepares) = CreateTarget(items, lazy);

            Assert.Equal(0, FirstRealizedIndex(ic));

            var before = ContainerByItem(ic);
            var containers = ic.GetRealizedContainers().ToHashSet();
            prepares.Count = 0;

            sv.Offset = new Vector(0, 500);
            LazyStackPanelTestTarget.RunLayout(sv);

            _output.WriteLine($"{Name(lazy)}: reused {Reused(ic, before)} of {before.Count}, prepares {prepares.Count}");
            Assert.True(FirstRealizedIndex(ic) >= 40, $"first realized {FirstRealizedIndex(ic)}");

            // The VSP keeps a container by item; the lazy panel keys a jump by index, so the same instance at
            // another index gets a container from the pool, with a prepare but without a new container.
            if (lazy)
                Assert.Empty(ic.GetRealizedContainers().Where(x => !containers.Contains(x)));
            else
                Assert.True(Reused(ic, before) >= 8, $"reused {Reused(ic, before)} of {before.Count}");

            AssertIndexes(ic, items);
            window.Close();
        }

        private static string Name(bool lazy) => lazy ? "Lazy" : "VSP";

        private static int FirstRealizedIndex(ItemsControl ic) =>
            ic.GetRealizedContainers().Select(ic.IndexFromContainer).Where(x => x >= 0).DefaultIfEmpty(-1).Min();

        private static Dictionary<object, Control> ContainerByItem(ItemsControl ic) =>
            ic.GetRealizedContainers().Where(x => ic.IndexFromContainer(x) >= 0 && x.DataContext is not null)
                .ToDictionary(x => x.DataContext!, x => x);

        // How many items are still shown by the container that showed them before.
        private static int Reused(ItemsControl ic, Dictionary<object, Control> before) =>
            ic.GetRealizedContainers().Count(x =>
                ic.IndexFromContainer(x) >= 0 &&
                x.DataContext is { } item &&
                before.TryGetValue(item, out var old) &&
                ReferenceEquals(old, x));

        // Every realized container shows the item at its index, at the item's position.
        private static void AssertIndexes(ItemsControl ic, IList<Row> items)
        {
            var realized = ic.GetRealizedContainers().Where(x => x.IsVisible).ToList();
            Assert.NotEmpty(realized);

            foreach (var c in realized)
            {
                var index = ic.IndexFromContainer(c);
                Assert.True(index >= 0, "realized container without an index");
                Assert.Same(items[index], c.DataContext);
            }

            var byTop = realized.OrderBy(x => x.Bounds.Top).Select(ic.IndexFromContainer).ToList();
            Assert.Equal(Enumerable.Range(byTop[0], byTop.Count), byTop);
        }

        private static (Window, ItemsControl, ScrollViewer, PrepareCounter) CreateTarget(
            IList<Row> items, bool lazy, bool virtualizingTemplate = false)
        {
            var prepares = new PrepareCounter();
            var ic = new ItemsControl { ItemTemplate = virtualizingTemplate ? new RowVirtualizingTemplate() : RowTemplate };

            ic.ContainerPrepared += (_, _) => prepares.Count++;

            var (window, sv) = LazyStackPanelTestTarget.Show(ic, items, lazy);
            return (window, ic, sv, prepares);
        }

        private static readonly FuncDataTemplate<Row> RowTemplate = new((_, _) =>
            new Canvas { [!Layoutable.HeightProperty] = new Binding(nameof(Row.Height)) });

        private class PrepareCounter
        {
            public int Count;
        }

        private class RowVirtualizingTemplate : IVirtualizingDataTemplate
        {
            public int MaxPoolSizePerKey => 5;

            public int MinPoolSizePerKey => 0;

            public object? GetKey(object? data) => data?.GetType();

            public bool Match(object? data) => data is Row;

            public Control? Build(object? data) => Build(data, null);

            public Control? Build(object? data, Control? existing) => existing ?? RowTemplate.Build(data);
        }

        public class Row
        {
            public Row(int id) => Id = id;

            public int Id { get; }

            public double Height => 10;

            public override string ToString() => $"Row {Id}";
        }
    }
}
