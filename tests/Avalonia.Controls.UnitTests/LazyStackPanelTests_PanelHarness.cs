using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Xunit;

#nullable enable

namespace Avalonia.Controls.UnitTests
{
    /// <summary>
    /// A row of <see cref="PanelHarness"/>: a height, a visibility, and optionally a height that depends on the
    /// available width (wrapping text) or a measure function.
    /// </summary>
    public class HarnessRow : INotifyPropertyChanged
    {
        private double _height;
        private bool _visible = true;

        public HarnessRow(int id, double height) => (Id, _height) = (id, height);

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Id { get; }

        public double Height { get => _height; set => Set(ref _height, value); }

        public bool Visible { get => _visible; set => Set(ref _visible, value); }

        /// <summary>
        /// When set, the row wraps like text: its height is <see cref="Height"/> × WrapWidth / available width
        /// (rounded up to whole lines of 20 px).
        /// </summary>
        public double? WrapWidth { get; set; }

        /// <summary>Replaces <see cref="Height"/> in the row's measure (non-deterministic rows).</summary>
        public Func<double>? Measure { get; set; }

        public double DesiredHeight(double availableWidth)
        {
            if (Measure is { } m)
                return m();
            if (WrapWidth is { } w && !double.IsInfinity(availableWidth) && availableWidth > 0)
                return Math.Ceiling(_height * w / availableWidth / 20) * 20;
            return _height;
        }

        public override string ToString() => $"Row {Id}";

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// An <see cref="ItemsControl"/> (or a <see cref="ListBox"/>) with the <see cref="LazyStackPanel"/> or the
    /// VSP, in a host of a given size inside a window. After each <see cref="Layout"/> it checks the realized rows
    /// (<see cref="Check"/>).
    /// </summary>
    internal sealed class PanelHarness : IDisposable
    {
        public const double Tolerance = 0.5;

        public PanelHarness(
            bool lazy,
            IEnumerable<HarnessRow> rows,
            double width = 200,
            double height = 200,
            double cacheLength = 0.5,
            ItemsControl? itemsControl = null,
            IWindowImpl? windowImpl = null,
            bool layoutRounding = false,
            ScrollBarVisibility horizontal = ScrollBarVisibility.Disabled,
            Thickness presenterMargin = default,
            Func<Control, Control>? wrap = null,
            bool strict = true,
            Func<Panel>? panel = null,
            IDataTemplate? template = null,
            params IStyle[] styles)
        {
            Lazy = lazy;
            Strict = strict;
            Items = new HarnessRowCollection(rows);
            ItemsControl = itemsControl ?? new ItemsControl();
            ItemsControl.ItemTemplate = template ?? new FuncDataTemplate<HarnessRow>((_, _) => new HarnessRowView(this, layoutRounding));
            ItemsControl.Template = new FuncControlTemplate<ItemsControl>((_, ns) => new ScrollViewer
            {
                HorizontalScrollBarVisibility = horizontal,
                Content = new ItemsPresenter
                {
                    Name = "PART_ItemsPresenter",
                    Margin = presenterMargin,
                    [~ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty),
                }.RegisterInNameScope(ns),
            });
            ItemsControl.ItemsSource = Items;
            ItemsControl.ItemsPanel = new FuncTemplate<Panel?>(() =>
            {
                var p = panel?.Invoke() ?? (lazy ? new LazyStackPanel() : (Panel)new VirtualizingStackPanel());
                if (p is LazyStackPanel l)
                    l.CacheLength = cacheLength;
                else if (p is VirtualizingStackPanel v)
                    v.CacheLength = cacheLength;
                return p;
            });

            Host = new Border
            {
                Width = width,
                Height = height,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = ItemsControl,
            };

            var style = new Style(x => x.OfType<ContentPresenter>())
            {
                Setters =
                {
                    new Setter(Layoutable.UseLayoutRoundingProperty, layoutRounding),
                    new Setter(Visual.IsVisibleProperty, new Binding(nameof(HarnessRow.Visible))),
                },
            };

            Window = windowImpl is null ? new Window() : new Window(windowImpl);
            Window.Width = 600;
            Window.Height = 600;
            Window.UseLayoutRounding = layoutRounding;
            Window.Content = wrap is null ? Host : wrap(Host);
            Window.Styles.Add(style);
            Window.Styles.AddRange(styles);
            Window.Show();
            LazyStackPanelTestTarget.RunLayout(Window);
            Scroll = ItemsControl.GetVisualDescendants().OfType<ScrollViewer>().First();
            Check("after the first layout");
        }

        public bool Lazy { get; }
        public string Name => Lazy ? "Lazy" : "VSP";
        public HarnessRowCollection Items { get; }
        public ItemsControl ItemsControl { get; }
        public Border Host { get; }
        public Window Window { get; }
        public ScrollViewer Scroll { get; }
        public Panel Panel => ItemsControl.ItemsPanelRoot!;
        public IItemsScrollInfo Info => ItemsScroll.Get(ItemsControl);
        public IScrollCommandTarget? Target => Panel as IScrollCommandTarget;
        public double ViewportHeight => Scroll.Viewport.Height;

        /// <summary>Row measures since the last reset (each <see cref="Layout"/> resets it).</summary>
        public int Measures { get; set; }

        /// <summary>Row measures over the whole test.</summary>
        public int TotalMeasures { get; set; }

        /// <summary>Row measures per row id over the whole test.</summary>
        public Dictionary<int, int> MeasuresById { get; } = new();

        /// <summary>Row arranges over the whole test.</summary>
        public int TotalArranges { get; set; }

        public int FirstRealizedIndex => ItemsControl.IndexFromContainer(Realized()[0]);
        public int LastRealizedIndex => ItemsControl.IndexFromContainer(Realized()[^1]);

        public List<Control> Realized() => ItemsControl.GetRealizedContainers()?.ToList() ?? new();

        /// <summary>The indexes of the realized rows, in index order.</summary>
        public List<int> RealizedIndexes() =>
            Realized().Select(c => ItemsControl.IndexFromContainer(c)).Where(i => i >= 0).OrderBy(i => i).ToList();

        public void Layout(bool check = true)
        {
            Measures = 0;
            LazyStackPanelTestTarget.RunLayout(ItemsControl);
            if (check)
                Check("after a layout");
        }

        public void ScrollBy(double delta)
        {
            LazyStackPanelTestTarget.ScrollBy(Scroll, delta);
            Layout();
        }

        /// <summary>Brings row <paramref name="index"/> into view, then scrolls it to the top.</summary>
        public void ScrollToTop(int index)
        {
            ItemsControl.ScrollIntoView(index);
            Layout();
            var y = ScreenY(Items[index]);
            Assert.NotNull(y);
            if (Math.Abs(y!.Value) > 0.001)
                ScrollBy(y.Value);
        }

        public Control? ContainerOf(HarnessRow row) =>
            Realized().FirstOrDefault(c => ReferenceEquals(c.DataContext, row));

        /// <summary>The top of the row relative to the viewport top; null if not realized or hidden.</summary>
        public double? ScreenY(HarnessRow row) =>
            ContainerOf(row) is { IsVisible: true } c ? c.TranslatePoint(default, Scroll)!.Value.Y : null;

        public double? ScreenY(int index) => ScreenY(Items[index]);

        /// <summary>The first row whose top is in the viewport.</summary>
        public HarnessRow FirstFullyVisible() => Visible()
            .Where(x => x.Top >= -Tolerance)
            .OrderBy(x => x.Top)
            .First().Row;

        /// <summary>The row at the top edge of the viewport (it can be cut).</summary>
        public HarnessRow TopRow() => Visible()
            .Where(x => x.Top + x.Height > Tolerance)
            .OrderBy(x => x.Top)
            .First().Row;

        /// <summary>The rows that are on the screen (at least partly), with their positions.</summary>
        public List<(HarnessRow Row, double Top, double Height)> OnScreen() => Visible()
            .Where(x => x.Top + x.Height > Tolerance && x.Top < ViewportHeight - Tolerance)
            .OrderBy(x => x.Top)
            .ToList();

        private IEnumerable<(HarnessRow Row, double Top, double Height)> Visible() => Realized()
            .Where(c => c.IsVisible && c.Bounds.Height > 0)
            .Select(c => ((HarnessRow)c.DataContext!, c.TranslatePoint(default, Scroll)!.Value.Y, c.Bounds.Height));

        /// <summary>
        /// Checks that the realized rows are contiguous, in collection order, and cover the viewport; no pooled
        /// container is reported as realized; 0 ≤ Offset ≤ max(0, Extent − Viewport); the layout pass finished.
        /// </summary>
        public void Check(string when)
        {
            var problem = FindProblem();
            if (problem is null)
                return;
            Problems.Add($"{when}: {problem}");
            if (Strict)
                Assert.Fail($"{Name} {when}: {problem}");
        }

        /// <summary>False: a failed check only goes to <see cref="Problems"/> (where the VSP fails by design).</summary>
        public bool Strict { get; set; } = true;

        /// <summary>The space between two rows that counts as contiguous (container margins).</summary>
        public double RowGap { get; set; }

        public List<string> Problems { get; } = new();

        private string? FindProblem()
        {
            var panel = Panel;
            if (!panel.IsMeasureValid || !panel.IsArrangeValid)
                return "the layout pass did not finish (layout cycle?)";

            var realized = Realized();
            var vh = ViewportHeight;
            double? prevBottom = null;
            int? prevIndex = null;
            double? firstTop = null;
            var lastIndex = -1;

            foreach (var c in realized)
            {
                var index = ItemsControl.IndexFromContainer(c);
                if (index < 0 || index >= Items.Count)
                    return $"realized container with index {index}";
                if (!ReferenceEquals(c.DataContext, Items[index]))
                    return $"container at index {index} shows {c.DataContext}";
                if (c.GetVisualParent() != panel)
                    return $"container at index {index} is not a child of the panel";
                if (c.IsVisible != Items[index].Visible)
                    return $"container at index {index}: IsVisible {c.IsVisible} (pooled?)";
                if (prevIndex is { } p && index != p + 1)
                    return $"realized indexes {p} and {index} are not consecutive";
                prevIndex = index;
                lastIndex = index;

                if (!c.IsVisible)
                    continue;

                var top = c.TranslatePoint(default, Scroll)!.Value.Y - c.Margin.Top;
                var bottom = top + c.Margin.Top + c.Bounds.Height + c.Margin.Bottom;
                if (prevBottom is { } b && Math.Abs(top - b) > Tolerance)
                    return $"{top - b:0.##} px between row {index} and the row before it";
                firstTop ??= top;
                prevBottom = bottom;
            }

            if (Items.Count > 0 && realized.Count == 0 && vh > 0)
                return "no rows realized";

            var pm = ItemsPresenterMargin;
            if (firstTop is { } ft && ft > pm.Top + Tolerance)
                return $"{ft:0.##} px empty above the first realized row";

            if (prevBottom is { } pb && pb < vh - pm.Bottom - Tolerance)
            {
                if (lastIndex != Items.Count - 1)
                    return $"{vh - pb:0.##} px empty below row {lastIndex}";
                if (realized.Count > 0 && (ItemsControl.IndexFromContainer(realized[0]) != 0 || firstTop < pm.Top - Tolerance))
                    return $"{vh - pb:0.##} px empty below the last row while rows above are scrolled out";
            }

            var offset = Scroll.Offset.Y;
            var max = Math.Max(0, Scroll.Extent.Height - Scroll.Viewport.Height);
            if (offset < -0.01 || offset > max + 0.01)
                return $"Offset {offset} outside 0 … {max} (Extent {Scroll.Extent.Height}, Viewport {Scroll.Viewport.Height})";

            if (Measures > 4 * (realized.Count + 10))
                return $"{Measures} row measures in one layout for {realized.Count} realized rows";

            return null;
        }

        private Thickness ItemsPresenterMargin =>
            ItemsControl.GetVisualDescendants().OfType<ItemsPresenter>().FirstOrDefault()?.Margin ?? default;

        public void Dispose() => Window.Close();

        public static string Format(double? y) => y is { } v ? v.ToString("0.##") : "null";

        public static IEnumerable<HarnessRow> Rows(int count, Func<int, double> height) =>
            Enumerable.Range(0, count).Select(i => new HarnessRow(i, height(i)));

        /// <summary>40/70/100/130/160 px, repeating.</summary>
        public static double Periodic(int index) => 40 + index * 37 % 5 * 30;
    }

    /// <summary>
    /// An observable collection of rows with one-event range changes (as <c>AvaloniaList</c>) and a
    /// <see cref="NotifyCollectionChangedAction.Reset"/> with new contents.
    /// </summary>
    public class HarnessRowCollection : ObservableCollection<HarnessRow>
    {
        public HarnessRowCollection(IEnumerable<HarnessRow> items) : base(items) { }

        public void Reset(IEnumerable<HarnessRow> items)
        {
            var list = items.ToList();
            Items.Clear();
            foreach (var item in list)
                Items.Add(item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>Removes <paramref name="count"/> rows with one Remove event.</summary>
        public void RemoveRange(int index, int count)
        {
            var removed = Items.Skip(index).Take(count).ToList();
            for (var i = 0; i < count; ++i)
                Items.RemoveAt(index);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
        }
    }

    /// <summary>The row template: measures to the row's height and counts its measures and arranges.</summary>
    internal sealed class HarnessRowView : Control
    {
        private readonly PanelHarness _harness;
        private HarnessRow? _row;

        public HarnessRowView(PanelHarness harness, bool layoutRounding)
        {
            _harness = harness;
            UseLayoutRounding = layoutRounding;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_row is not null)
                _row.PropertyChanged -= OnRowChanged;
            _row = DataContext as HarnessRow;
            if (_row is not null)
                _row.PropertyChanged += OnRowChanged;
            InvalidateMeasure();
            base.OnDataContextChanged(e);
        }

        private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => InvalidateMeasure();

        protected override Size MeasureOverride(Size availableSize)
        {
            ++_harness.Measures;
            ++_harness.TotalMeasures;
            if (_row is not null)
                _harness.MeasuresById[_row.Id] = _harness.MeasuresById.GetValueOrDefault(_row.Id) + 1;
            return new Size(100, _row?.DesiredHeight(availableSize.Width) ?? 0);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ++_harness.TotalArranges;
            return base.ArrangeOverride(finalSize);
        }
    }
}
