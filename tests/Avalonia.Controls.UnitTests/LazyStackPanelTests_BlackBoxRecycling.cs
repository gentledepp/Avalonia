using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.UnitTests;
using Xunit;
using static Avalonia.Controls.UnitTests.LazyStackPanelTestTarget;

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// Recycling tests of VirtualizingStackPanelTests, against both panels: 100 x 100 window, no cache unless the test
    /// says so.
    /// </summary>
    public class LazyStackPanelTests_BlackBoxRecycling : LazyStackPanelTestBase
    {
        [Theory]
        [InlineData(false, 0d, 20)]
        [InlineData(false, 0.5d, 200)]
        [InlineData(true, 0d, 20)]
        [InlineData(true, 0.5d, 200)]
        public void Supports_Null_Recycle_Key_When_Scrolling(bool lazy, double cacheLength, int offset)
        {
            var ic = new NonRecyclingItemsControl { ItemTemplate = RowTemplate };
            var (window, sv) = Show(ic, Rows(100), lazy, cacheLength);
            var firstItem = ic.ContainerFromIndex(0)!;

            sv.Offset = new Vector(0, offset);
            RunLayout(sv);

            Assert.Null(firstItem.Parent);
            Assert.Null(firstItem.GetVisualParent());
            Assert.DoesNotContain(firstItem, ic.ItemsPanelRoot!.Children);
            window.Close();
        }

        [Theory]
        [InlineData(false, 0d)]
        [InlineData(false, 0.5d)]
        [InlineData(true, 0d)]
        [InlineData(true, 0.5d)]
        public void Supports_Null_Recycle_Key_When_Clearing_Items(bool lazy, double cacheLength)
        {
            var ic = new NonRecyclingItemsControl { ItemTemplate = RowTemplate };
            var (window, sv) = Show(ic, Rows(100), lazy, cacheLength);
            var firstItem = ic.ContainerFromIndex(0)!;

            ic.ItemsSource = null;
            RunLayout(sv);

            Assert.Null(firstItem.Parent);
            Assert.Null(firstItem.GetVisualParent());
            Assert.Empty(ic.ItemsPanelRoot!.Children);
            window.Close();
        }

        [Theory]
        [InlineData(false, 0d)]
        [InlineData(false, 0.5d)]
        [InlineData(true, 0d)]
        [InlineData(true, 0.5d)]
        public void Recycling_A_Hidden_Control_Shows_It(bool lazy, double cacheLength)
        {
            var items = new ObservableCollection<Row>(Rows(3));
            var style = new Style(x => x.OfType<ContentPresenter>())
            {
                Setters = { new Setter(Visual.IsVisibleProperty, new Binding(nameof(Row.IsVisible))) },
            };
            var ic = new ItemsControl { ItemTemplate = RowTemplate };
            var (window, sv) = Show(ic, items, lazy, cacheLength, styles: style);
            var container = ic.ContainerFromIndex(2)!;

            Assert.True(container.IsVisible);
            Assert.Equal(20, container.Bounds.Top);

            items[2].IsVisible = false;
            RunLayout(sv);

            Assert.False(container.IsVisible);

            items.RemoveAt(2);
            items.Add(new Row(3));
            RunLayout(sv);

            Assert.True(container.IsVisible);
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Recycled_Container_Gets_New_DataContext_When_Type_Matches(bool lazy)
        {
            var items = new ObservableCollection<Row>(Rows(20));
            var ic = new ItemsControl { ItemTemplate = new RowVirtualizingTemplate(() => new Canvas { Width = 100, Height = 10 }) };
            var (window, sv) = Show(ic, items, lazy);

            sv.Offset = new Vector(0, 100);
            RunLayout(sv);
            sv.Offset = new Vector(0, 0);
            RunLayout(sv);

            var realized = ic.GetRealizedContainers().ToList();
            Assert.NotEmpty(realized);
            Assert.All(realized, c => Assert.Same(items[ic.IndexFromContainer(c)], c.DataContext));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reused_Container_Reflects_New_Item_Height_After_Scroll(bool lazy)
        {
            // Short and tall rows alternate, so a reused child shows an item of another height. The
            // height is read in MeasureOverride, not bound: only a re-measure of the reused child
            // gives the new height.
            var items = Enumerable.Range(0, 50).Select(i => new Row(i, i % 2 == 0 ? 20 : 80)).ToList();
            var ic = new ItemsControl { ItemTemplate = new RowVirtualizingTemplate(() => new DataDrivenHeightControl(), minPool: 2) };
            var (window, sv) = Show(ic, items, lazy);

            for (double offset = 0; offset <= 400; offset += 40)
            {
                sv.Offset = new Vector(0, offset);
                RunLayout(sv);
            }

            for (double offset = 400; offset >= 0; offset -= 40)
            {
                sv.Offset = new Vector(0, offset);
                RunLayout(sv);
            }

            var realized = ic.GetRealizedContainers().ToList();
            Assert.NotEmpty(realized);
            Assert.All(realized, c => Assert.Equal(((Row)c.DataContext!).Height, c.Bounds.Height));
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reset_Reuses_Container_With_Matching_DataContext(bool lazy)
        {
            var rows = Rows(20);
            var items = new ResettingCollection<Row>(rows);
            var ic = new ItemsControl { ItemTemplate = new RowVirtualizingTemplate(() => new Canvas { Width = 100, Height = 10 }) };
            var (window, sv) = Show(ic, items, lazy);
            var before = ic.GetRealizedContainers().ToDictionary(x => x.DataContext!);

            items.Reset(rows);
            RunLayout(sv);

            var realized = ic.GetRealizedContainers().ToList();
            Assert.NotEmpty(realized);
            Assert.All(realized, c =>
            {
                Assert.Same(rows[ic.IndexFromContainer(c)], c.DataContext);
                Assert.Same(before[c.DataContext!], c);
            });
            window.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reset_With_Completely_New_Items_Recycles_Everything(bool lazy)
        {
            var items = new ResettingCollection<Row>(Rows(20));
            var ic = new ItemsControl { ItemTemplate = new RowVirtualizingTemplate(() => new Canvas { Width = 100, Height = 10 }) };
            var (window, sv) = Show(ic, items, lazy);

            sv.Offset = new Vector(0, 50);
            RunLayout(sv);

            var newRows = Enumerable.Range(100, 20).Select(i => new Row(i)).ToList();
            items.Reset(newRows);
            RunLayout(sv);

            var realized = ic.GetRealizedContainers().ToList();
            Assert.NotEmpty(realized);
            Assert.All(realized, c => Assert.Same(newRows[ic.IndexFromContainer(c)], c.DataContext));
            Assert.DoesNotContain(ic.ItemsPanelRoot!.Children, c => c.IsVisible && !realized.Contains(c));
            window.Close();
        }

        private static Row[] Rows(int count) => Enumerable.Range(0, count).Select(i => new Row(i)).ToArray();

        private static readonly FuncDataTemplate<Row> RowTemplate = new((_, _) =>
            new Canvas { [!Layoutable.HeightProperty] = new Binding(nameof(Row.Height)) });

        private class NonRecyclingItemsControl : ItemsControl
        {
            protected override Type StyleKeyOverride => typeof(ItemsControl);

            protected internal override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
            {
                recycleKey = null;
                return true;
            }
        }

        private class DataDrivenHeightControl : Control
        {
            protected override Size MeasureOverride(Size availableSize) =>
                new(50, (DataContext as Row)?.Height ?? 10);
        }

        private class RowVirtualizingTemplate : IVirtualizingDataTemplate
        {
            private readonly Func<Control> _build;

            public RowVirtualizingTemplate(Func<Control> build, int minPool = 0) => (_build, MinPoolSizePerKey) = (build, minPool);

            public int MaxPoolSizePerKey => 5;

            public int MinPoolSizePerKey { get; }

            public object? GetKey(object? data) => data?.GetType();

            public bool Match(object? data) => data is Row;

            public Control? Build(object? data) => Build(data, null);

            public Control? Build(object? data, Control? existing) => existing ?? _build();
        }

        public class Row : INotifyPropertyChanged
        {
            private bool _isVisible = true;

            public Row(int id, double height = 10) => (Id, Height) = (id, height);

            public event PropertyChangedEventHandler? PropertyChanged;

            public int Id { get; }

            public double Height { get; }

            public bool IsVisible
            {
                get => _isVisible;
                set
                {
                    _isVisible = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
                }
            }

            public override string ToString() => $"Row {Id}";
        }
    }
}
