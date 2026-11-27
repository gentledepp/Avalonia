using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Xunit;

#nullable enable

namespace Avalonia.Controls.UnitTests;

public class ScrollViewerTests_IScrollCommandTarget : ScopedTestBase
{
    private readonly MouseTestHelper _mouse = new();

    [Fact]
    public void LineDown_Sends_ScrollBy()
    {
        var (target, content) = CreateTarget();

        target.LineDown();

        Assert.Equal([Command.By(0, 50)], content.Commands);
        Assert.Empty(content.OffsetWrites);
    }

    [Fact]
    public void LineUp_Near_The_Start_Sends_The_Full_Step()
    {
        var (target, content) = CreateTarget(offset: 20);

        target.LineUp();

        Assert.Equal([Command.By(0, -50)], content.Commands);
        Assert.Empty(content.OffsetWrites);
    }

    [Fact]
    public void PageDown_And_PageUp_Send_ScrollBy_Viewport()
    {
        var (target, content) = CreateTarget(offset: 500);

        target.PageDown();
        target.PageUp();

        Assert.Equal([Command.By(0, 1000), Command.By(0, -1000)], content.Commands);
        Assert.Empty(content.OffsetWrites);
    }

    [Fact]
    public void ScrollToEnd_And_ScrollToHome_Send_ScrollToEdge()
    {
        var (target, content) = CreateTarget(offset: 500);

        target.ScrollToEnd();
        target.ScrollToHome();

        Assert.Equal([Command.Edge(ScrollEdge.End), Command.Edge(ScrollEdge.Start)], content.Commands);
        Assert.Empty(content.OffsetWrites);
    }

    [Fact]
    public void Setting_Offset_Sends_ScrollToOffset_Without_Clamp()
    {
        var (target, content) = CreateTarget();

        target.Offset = new Vector(0, 99999);

        Assert.Equal([Command.To(0, 99999)], content.Commands);
        Assert.Empty(content.OffsetWrites);
    }

    [Fact]
    public void Reported_Offset_Is_Not_Written_Back()
    {
        var (target, content) = CreateTarget(offset: 9000);
        var scrollBar = GetVerticalScrollBar(target);

        // The new maximum (4000) is below the old offset: a clamp of the old offset must not reach the content.
        content.Report(new Size(100, 5000), new Size(100, 1000), new Vector(0, 3000));

        Assert.Empty(content.Commands);
        Assert.Empty(content.OffsetWrites);
        Assert.Equal(new Vector(0, 3000), target.Offset);
        Assert.Equal(3000, scrollBar.Value);
    }

    [Fact]
    public void Wheel_Sends_ScrollBy_Without_Clamp()
    {
        var (target, content) = CreateTarget(offset: 20);

        var handled = Wheel(target, new Vector(0, 1));

        Assert.Equal([Command.By(0, -50)], content.Commands);
        Assert.Empty(content.OffsetWrites);
        Assert.True(handled);
    }

    [Fact]
    public void Wheel_Is_Not_Handled_When_Nothing_Was_Scrolled()
    {
        var (target, content) = CreateTarget();
        content.UsedDelta = Vector.Zero;

        var handled = Wheel(target, new Vector(0, 1));

        Assert.Equal([Command.By(0, -50)], content.Commands);
        Assert.False(handled);
    }

    [Fact]
    public void ScrollBar_Buttons_Send_ScrollBy()
    {
        var (target, content) = CreateTarget(offset: 20);
        var scrollBar = GetVerticalScrollBar(target);

        scrollBar.LineUp();
        scrollBar.PageDown();

        Assert.Equal([Command.By(0, -50), Command.By(0, 1000)], content.Commands);
        Assert.Empty(content.OffsetWrites);
    }

    [Fact]
    public void ScrollBar_ScrollToEnd_Sends_ScrollToEdge()
    {
        var (target, content) = CreateTarget(offset: 500);
        var scrollBar = GetVerticalScrollBar(target);

        scrollBar.ScrollToEnd();
        scrollBar.ScrollToHome();

        Assert.Equal([Command.Edge(ScrollEdge.End), Command.Edge(ScrollEdge.Start)], content.Commands);
        Assert.Empty(content.OffsetWrites);
    }

    [Fact]
    public void ScrollBar_Thumb_Drag_Sends_ScrollToFraction()
    {
        var (target, content) = CreateTarget();
        var scrollBar = GetVerticalScrollBar(target);
        var thumb = GetVerticalThumb(target);
        var p = GetRootPoint(thumb, thumb.Bounds.Center);

        _mouse.Down(thumb, position: p);
        _mouse.Move(thumb, p + new Vector(0, 100));

        var command = Assert.Single(content.Commands);
        Assert.Equal(nameof(IScrollCommandTarget.ScrollToFraction), command.Name);
        Assert.Equal(scrollBar.Value / scrollBar.Maximum, command.Value.Y, 6);
        Assert.True(command.Value.Y > 0);
        Assert.Empty(content.OffsetWrites);
    }

    [Fact]
    public void ScrollBar_Value_Follows_The_Reported_Offset_After_Layout()
    {
        var (target, content) = CreateTarget();
        var scrollBar = GetVerticalScrollBar(target);
        var thumb = GetVerticalThumb(target);
        var p = GetRootPoint(thumb, thumb.Bounds.Center);

        _mouse.Down(thumb, position: p);
        _mouse.Move(thumb, p + new Vector(0, 100));
        Assert.True(scrollBar.Value > 0);

        // The content did not scroll, so the thumb goes back.
        ((TestRoot)target.GetVisualRoot()!).LayoutManager.ExecuteLayoutPass();

        Assert.Equal(0, scrollBar.Value);
        Assert.Equal(default, target.Offset);
    }

    [Fact]
    public void Panel_Of_ItemsControl_Receives_The_Commands()
    {
        var itemsControl = new ItemsControl
        {
            Template = new FuncControlTemplate<ItemsControl>((parent, scope) => new ScrollViewer
            {
                Name = "PART_ScrollViewer",
                Template = new FuncControlTemplate<ScrollViewer>(ScrollViewerTests.CreateTemplate),
                Content = new ItemsPresenter
                {
                    Name = "PART_ItemsPresenter",
                    [~ItemsPresenter.ItemsPanelProperty] = parent[~ItemsControl.ItemsPanelProperty],
                }.RegisterInNameScope(scope),
            }.RegisterInNameScope(scope)),
            ItemsPanel = new FuncTemplate<Panel?>(() => new TestCommandTarget()),
            ItemsSource = new[] { new Border(), new Border() },
        };

        var root = new TestRoot(itemsControl);
        root.LayoutManager.ExecuteInitialLayoutPass();

        var panel = Assert.IsType<TestCommandTarget>(itemsControl.ItemsPanelRoot);
        panel.Report(new Size(100, 10000), new Size(100, 1000), default);
        var scrollViewer = itemsControl.GetVisualDescendants().OfType<ScrollViewer>().Single();

        scrollViewer.LineDown();
        scrollViewer.Offset = new Vector(0, 300);

        Assert.Equal([Command.By(0, 50), Command.To(0, 300)], panel.Commands);
        Assert.Empty(panel.OffsetWrites);
    }

    private static (ScrollViewer, TestCommandTarget) CreateTarget(double offset = 0)
    {
        var content = new TestCommandTarget();
        var target = new ScrollViewer
        {
            Template = new FuncControlTemplate<ScrollViewer>(ScrollViewerTests.CreateTemplate),
            Content = content,
        };

        content.Report(new Size(100, 10000), new Size(100, 1000), new Vector(0, offset));
        var root = new TestRoot(target);
        root.LayoutManager.ExecuteInitialLayoutPass();
        return (target, content);
    }

    private static bool Wheel(ScrollViewer target, Vector delta)
    {
        var presenter = (ScrollContentPresenter)target.Presenter!;
        var root = (Visual)target.GetVisualRoot()!;
        var e = new PointerWheelEventArgs(
            presenter,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true),
            root,
            default,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            delta)
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent
        };
        presenter.RaiseEvent(e);
        return e.Handled;
    }

    private static ScrollBar GetVerticalScrollBar(ScrollViewer target) =>
        Assert.IsType<ScrollBar>(target.GetTemplateDescendants().FirstOrDefault(x => x.Name == "PART_VerticalScrollBar"));

    private static Thumb GetVerticalThumb(ScrollViewer target)
    {
        var track = Assert.IsType<Track>(
            GetVerticalScrollBar(target).GetTemplateDescendants().FirstOrDefault(x => x.Name == "track"));
        return Assert.IsType<Thumb>(track.Thumb);
    }

    private static Point GetRootPoint(Visual control, Point p) =>
        p.Transform(control.TransformToVisual((Visual)control.GetVisualRoot()!)!.Value);

    private record Command(string Name, Vector Value)
    {
        public static Command By(double x, double y) => new(nameof(IScrollCommandTarget.ScrollBy), new Vector(x, y));
        public static Command To(double x, double y) => new(nameof(IScrollCommandTarget.ScrollToOffset), new Vector(x, y));
        public static Command Edge(ScrollEdge edge) => new(nameof(IScrollCommandTarget.ScrollToEdge), new Vector(0, (int)edge));
    }

    private class TestCommandTarget : Panel, IScrollCommandTarget
    {
        private EventHandler? _scrollInvalidated;
        private Vector _offset;

        public List<Command> Commands { get; } = new();
        public List<Vector> OffsetWrites { get; } = new();
        public Vector? UsedDelta { get; set; }

        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public bool IsLogicalScrollEnabled => true;
        public Size ScrollSize => new(16, 50);
        public Size PageScrollSize => Viewport;
        public Size Extent { get; private set; }
        public Size Viewport { get; private set; }

        public Vector Offset
        {
            get => _offset;
            set => OffsetWrites.Add(value);
        }

        public event EventHandler? ScrollInvalidated
        {
            add => _scrollInvalidated += value;
            remove => _scrollInvalidated -= value;
        }

        public void Report(Size extent, Size viewport, Vector offset)
        {
            Extent = extent;
            Viewport = viewport;
            _offset = offset;
            RaiseScrollInvalidated(EventArgs.Empty);
        }

        public void RaiseScrollInvalidated(EventArgs e) => _scrollInvalidated?.Invoke(this, e);

        public Vector ScrollBy(Vector delta, ScrollSource source)
        {
            Commands.Add(Command.By(delta.X, delta.Y));
            return UsedDelta ?? delta;
        }

        public void ScrollToEdge(ScrollEdge edge) => Commands.Add(Command.Edge(edge));

        public void ScrollToFraction(Vector fraction) =>
            Commands.Add(new Command(nameof(ScrollToFraction), fraction));

        public void ScrollToOffset(Vector offset) => Commands.Add(Command.To(offset.X, offset.Y));

        public bool BringIntoView(Control target, Rect targetRect) => false;

        protected override Size MeasureOverride(Size availableSize) => new(100, 1000);

        public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;
    }
}
