using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Harfbuzz;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Moq;
using Xunit;

namespace Avalonia.Controls.UnitTests
{
    public class TextSelectionCanvasTests : ScopedTestBase
    {
        [Fact]
        public void Text_Selection_Handle_Moves_With_Scrolling()
        {
            using (UnitTestApplication.Start(Services))
            {
                var touchHelper = new TouchTestHelper();
                Border rootBorder = new Border()
                {
                    Width = 200,
                    Height = 600
                };
                var visualLayerManager = new VisualLayerManager()
                {
                    Child = rootBorder,
                    EnableTextSelectorLayer = true
                };
                var impl = CreateMockTopLevelImpl();
                var topLevel = new TestTopLevel(impl.Object)
                {
                    Template = CreateTopLevelTemplate(),
                    Content = visualLayerManager
                };

                var focusedTextBox = CreateTextBox();

                var panel = new StackPanel()
                {
                    Orientation = Layout.Orientation.Vertical,
                    Spacing = 20,
                    Children =
                    {
                        focusedTextBox,
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                        CreateTextBox(),
                    }
                };

                var scrollViewer = new ScrollViewer
                {
                    Template = new FuncControlTemplate<ScrollViewer>(ScrollViewerTests.CreateTemplate),
                    Content = panel
                };

                rootBorder.Child = scrollViewer;

                topLevel.LayoutManager.ExecuteInitialLayoutPass();

                touchHelper.Tap(focusedTextBox);

                topLevel.LayoutManager.ExecuteLayoutPass();

                var presenter = focusedTextBox.FindDescendantOfType<TextPresenter>()!;
                var canvas = presenter.TextSelectionHandleCanvas;

                Assert.NotNull(canvas);

                var handle = canvas.Children.FirstOrDefault() as TextSelectionHandle;

                Assert.NotNull(handle);

                Assert.Equal(new Point(25.5, 14.5), handle.GetTopLeft());

                scrollViewer.Offset = new Vector(0, 50);

                topLevel.LayoutManager.ExecuteLayoutPass();

                Assert.Equal(new Point(25.5, -35.5), handle.GetTopLeft());
            }

            TextBox CreateTextBox()
            {
                return new TextBox
                {
                    Template = TextBoxTests.CreateTemplate(),
                    Text = "Test",
                    Width = 150
                };
            }
        }

        [Theory]
        // fits: slides just far enough to sit inside
        [InlineData(-5, 0, 10, 10, 0, 0)]
        [InlineData(95, 0, 10, 10, 90, 0)]
        [InlineData(20, 20, 10, 10, 20, 20)]
        // wider than the bounds: centred instead, so the caret stays mid-loupe
        [InlineData(0, 0, 200, 10, -50, 0)]
        public void ClampInto_Keeps_The_Window_Inside_The_Presenter(
            double x, double y, double w, double h, double expectedX, double expectedY)
        {
            var result = TextSelectionMagnifier.ClampInto(
                new Rect(x, y, w, h), new Rect(0, 0, 100, 100));

            Assert.Equal(expectedX, result.X, 3);
            Assert.Equal(expectedY, result.Y, 3);
            Assert.Equal(w, result.Width, 3);
        }

        [Fact]
        public void Magnifier_Is_Shown_While_A_Handle_Is_Dragged_And_Hidden_On_Release()
        {
            using (UnitTestApplication.Start(Services))
            {
                var (topLevel, textBox, _, canvas) = CreateDraggableSelection();
                var magnifier = canvas.Children.OfType<TextSelectionMagnifier>().Single();
                GiveMagnifierATemplate(magnifier, topLevel);

                var handle = canvas.Children.OfType<TextSelectionHandle>()
                    .First(h => h.IsVisible);

                var touch = new TouchTestHelper();
                touch.Down(handle);
                Assert.False(magnifier.IsVisible, "not until the drag actually starts");

                touch.Move(handle, new Point(4, 0));   // raises DragStarted
                Assert.True(magnifier.IsVisible, "the loupe follows a handle drag");

                touch.Move(handle, new Point(20, 0));  // raises DragDelta
                Assert.True(magnifier.IsVisible);

                touch.Up(handle, new Point(20, 0));
                Assert.False(magnifier.IsVisible, "the loupe goes away when the finger lifts");
            }
        }

        [Fact]
        public void Magnifier_Magnifies_The_Caret_From_The_Live_Presenter()
        {
            using (UnitTestApplication.Start(Services))
            {
                var (topLevel, textBox, presenter, canvas) = CreateDraggableSelection();
                var magnifier = canvas.Children.OfType<TextSelectionMagnifier>().Single();
                GiveMagnifierATemplate(magnifier, topLevel);

                var handle = canvas.Children.OfType<TextSelectionHandle>()
                    .First(h => h.IsVisible);

                var touch = new TouchTestHelper();
                touch.Down(handle);
                touch.Move(handle, new Point(4, 0));

                var brush = magnifier.SourceBrush;

                // It magnifies the presenter itself, not a copy.
                Assert.Same(presenter, brush.Visual);

                // The window is the viewport shrunk by the zoom, centred on the caret.
                var caret = presenter.GetCursorRectangle();
                var source = brush.SourceRect.Rect;

                Assert.Equal(RelativeUnit.Absolute, brush.SourceRect.Unit);

                // The window is the viewport shrunk by the zoom.
                Assert.Equal(magnifier.DesiredSize.Width / magnifier.Zoom, source.Width, 3);
                Assert.Equal(magnifier.DesiredSize.Height / magnifier.Zoom, source.Height, 3);

                // It never magnifies blank space past the edge of the text...
                Assert.True(new Rect(presenter.Bounds.Size).Contains(source),
                    $"source {source} escaped the presenter {presenter.Bounds}");

                // ...and the caret is always inside what the user sees.
                Assert.True(source.Contains(caret.Center),
                    $"caret {caret.Center} is outside the magnified window {source}");
            }
        }

        [Fact]
        public void Magnifier_Brush_Renders_An_Attached_Presenter()
        {
            // The whole feature rests on VisualBrush being able to draw a visual that is live in the
            // tree; every VisualBrush render test upstream uses a detached one.
            using (UnitTestApplication.Start(Services))
            {
                var (topLevel, _, presenter, canvas) = CreateDraggableSelection();
                var magnifier = canvas.Children.OfType<TextSelectionMagnifier>().Single();
                GiveMagnifierATemplate(magnifier, topLevel);

                Assert.NotNull(presenter.GetVisualRoot());

                magnifier.Update(presenter, presenter.GetCursorRectangle().Center);

                var content = ((ISceneBrush)magnifier.SourceBrush).CreateContent();

                Assert.NotNull(content);
                Assert.Equal(presenter.Bounds.Size, content!.Rect.Size);
            }
        }

        /// <summary>
        /// A focused text box with a selection, its handles forced into touch mode so they are live
        /// and draggable.
        /// </summary>
        private static (TopLevel topLevel, TextBox textBox, TextPresenter presenter, TextSelectionHandleCanvas canvas)
            CreateDraggableSelection()
        {
            var textBox = new TextBox
            {
                Template = TextBoxTests.CreateTemplate(),
                Text = "Hello world",
                Width = 150
            };

            var visualLayerManager = new VisualLayerManager
            {
                Child = new Border { Width = 200, Height = 200, Child = textBox },
                EnableTextSelectorLayer = true
            };

            var topLevel = new TestTopLevel(CreateMockTopLevelImpl().Object)
            {
                Template = CreateTopLevelTemplate(),
                Content = visualLayerManager
            };

            topLevel.LayoutManager.ExecuteInitialLayoutPass();

            textBox.Focus();
            textBox.SelectionStart = 0;
            textBox.SelectionEnd = 5;

            var presenter = textBox.FindDescendantOfType<TextPresenter>()!;
            presenter.EnsureTextSelectionLayer();

            var canvas = presenter.TextSelectionHandleCanvas!;
            canvas.Show(forceTouchMode: true);

            topLevel.LayoutManager.ExecuteLayoutPass();

            return (topLevel, textBox, presenter, canvas);
        }

        /// <summary>
        /// These tests run without the Fluent theme, so the magnifier has no template and therefore
        /// no size - and it refuses to show at zero size. Give it the shape the theme would.
        /// </summary>
        private static void GiveMagnifierATemplate(TextSelectionMagnifier magnifier, TopLevel topLevel)
        {
            magnifier.Template = new FuncControlTemplate<TextSelectionMagnifier>((_, scope) =>
                new Border { Name = "PART_Viewport" }.RegisterInNameScope(scope));
            magnifier.Width = 120;
            magnifier.Height = 48;

            magnifier.ApplyTemplate();
            topLevel.LayoutManager.ExecuteLayoutPass();
        }

        private class TestTopLevel(ITopLevelImpl impl) : TopLevel(impl)
        {

        }

        static Mock<ITopLevelImpl> CreateMockTopLevelImpl()
        {
            var topLevel = new Mock<ITopLevelImpl>();
            topLevel.Setup(x => x.RenderScaling).Returns(1);
            topLevel.Setup(x => x.Compositor).Returns(RendererMocks.CreateDummyCompositor());
            return topLevel;
        }

        private static FuncControlTemplate<TestTopLevel> CreateTopLevelTemplate()
        {
            return new FuncControlTemplate<TestTopLevel>((x, scope) =>
                new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = x[!ContentControl.ContentProperty],
                }.RegisterInNameScope(scope));
        }

        private static TestServices Services => TestServices.MockThreadingInterface.With(
            standardCursorFactory: Mock.Of<ICursorFactory>(),
            renderInterface: new HeadlessPlatformRenderInterface(),
            textShaperImpl: new HarfBuzzTextShaper(),
            fontManagerImpl: new TestFontManager(),
            keyboardDevice: () => new KeyboardDevice(),
            keyboardNavigation: () => new KeyboardNavigationHandler(),
            inputManager: new InputManager(),
            assetLoader: new StandardAssetLoader());
    }
}
