using System.Linq;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Animation;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Harfbuzz;
using Avalonia.Headless;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Transformation;
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

                // It magnifies the TextBox itself, not a copy - the box, because the presenter has
                // no background of its own and a loupe over it comes out transparent.
                Assert.Same(textBox, brush.Visual);

                var caret = presenter.GetCursorRectangle();
                var caretInBox = presenter.TranslatePoint(caret.Center, textBox)!.Value;
                var source = brush.SourceRect.Rect;

                Assert.Equal(RelativeUnit.Absolute, brush.SourceRect.Unit);

                // The window is the visible loupe - control minus shadow gutter - shrunk by the zoom.
                var glass = magnifier.DesiredSize.Deflate(magnifier.FrameMargin);
                Assert.Equal(new Size(120, 48), glass);
                Assert.Equal(glass.Width / magnifier.Zoom, source.Width, 3);
                Assert.Equal(glass.Height / magnifier.Zoom, source.Height, 3);

                // It never magnifies blank space past the edge of the field...
                Assert.True(new Rect(textBox.Bounds.Size).Contains(source),
                    $"source {source} escaped the text box {textBox.Bounds}");

                // ...and the caret is always inside what the user sees.
                Assert.True(source.Contains(caretInBox),
                    $"caret {caretInBox} is outside the magnified window {source}");
            }
        }

        [Fact]
        public void Magnifier_Is_Placed_By_RenderTransform_Not_By_Canvas_Left()
        {
            // Placement has to go through RenderTransform: that is what the theme's transition
            // animates, and what the compositor can animate off the UI thread.
            using (UnitTestApplication.Start(Services))
            {
                var (topLevel, _, _, canvas) = CreateDraggableSelection();
                var magnifier = canvas.Children.OfType<TextSelectionMagnifier>().Single();
                GiveMagnifierATemplate(magnifier, topLevel);

                var handle = canvas.Children.OfType<TextSelectionHandle>().First(h => h.IsVisible);
                var touch = new TouchTestHelper();
                touch.Down(handle);
                touch.Move(handle, new Point(4, 0));

                Assert.IsType<TransformOperations>(magnifier.RenderTransform);
                Assert.True(double.IsNaN(Canvas.GetLeft(magnifier)) || Canvas.GetLeft(magnifier) == 0,
                    "Canvas.Left must stay untouched, or it would fight the transform");
            }
        }

        [Fact]
        public void Magnifier_Follows_The_Finger_Between_Character_Positions()
        {
            // The caret snaps to whole characters. If the loupe were pinned to it, it would sit
            // still for several pointer moves and then jump - which is what a transition was
            // covering up, at the cost of lagging badly on a fast drag. Following the finger is
            // smooth without anything being animated.
            using (UnitTestApplication.Start(Services))
            {
                // Wide, with the selection well into the text: near an edge the loupe is clamped to
                // stay on screen, which would hide the movement this test is about.
                var (topLevel, textBox, _, canvas) = CreateDraggableSelection(
                    text: "The quick brown fox jumps over the lazy dog", width: 600, selectionEnd: 20);
                var magnifier = canvas.Children.OfType<TextSelectionMagnifier>().Single();
                GiveMagnifierATemplate(magnifier, topLevel);

                var handle = canvas.Children.OfType<TextSelectionHandle>().ToList()[2];
                var touch = new TouchTestHelper();
                touch.Down(handle);

                var offsets = new List<double>();
                var selections = new List<int>();

                // One-pixel steps: far finer than a character, so the caret cannot keep up.
                for (var dx = 1; dx <= 6; dx++)
                {
                    touch.Move(handle, new Point(dx, 0));
                    var transform = Assert.IsType<TransformOperations>(magnifier.RenderTransform);
                    offsets.Add(transform.Value.M31);
                    selections.Add(textBox.SelectionEnd);
                }

                // The caret stalls on a character for at least one of these steps...
                Assert.True(selections.Distinct().Count() < offsets.Count,
                    $"precondition: the caret should lag the finger, got [{string.Join(",", selections)}]");

                // ...but the loupe moved on every single one.
                Assert.Equal(offsets.Count, offsets.Distinct().Count());
            }
        }

        [Fact]
        public void ShowTouchSelectionMenu_Brings_The_Handles_Back_When_In_Touch_Mode()
        {
            // Running a command from the touch context menu closes that menu, so the caller needs a
            // way to put the selection UI back for the selection the command just made.
            using (UnitTestApplication.Start(Services))
            {
                var (topLevel, textBox, _, canvas) = CreateDraggableSelection();

                canvas.ShowHandles = false;
                topLevel.LayoutManager.ExecuteLayoutPass();
                Assert.DoesNotContain(canvas.Children.OfType<TextSelectionHandle>(), h => h.IsVisible);

                textBox.ShowTouchSelectionMenu();
                topLevel.LayoutManager.ExecuteLayoutPass();

                Assert.Contains(canvas.Children.OfType<TextSelectionHandle>(), h => h.IsVisible);
            }
        }

        [Fact]
        public void ShowTouchSelectionMenu_Does_Nothing_For_A_Mouse_User()
        {
            // A menu appearing on its own would be wrong with a mouse; there the flyout is opened by
            // the right-click that ran the command in the first place.
            using (UnitTestApplication.Start(Services))
            {
                var (topLevel, textBox, _, canvas) = CreateDraggableSelection(forceTouchMode: false);

                textBox.ShowTouchSelectionMenu();
                topLevel.LayoutManager.ExecuteLayoutPass();

                Assert.False(canvas.ShowHandles);
                Assert.DoesNotContain(canvas.Children.OfType<TextSelectionHandle>(), h => h.IsVisible);
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
                Assert.NotEqual(default, presenter.Bounds.Size);
            }
        }

        [Fact]
        public void Handles_Work_When_The_TextBox_Was_Focused_Before_It_Had_A_Template()
        {
            // The "impostor" pattern: a read-only placeholder is swapped for a real TextBox, which is
            // focused the instant it becomes visible - before it has ever been measured, so before it
            // has a template. TextBox.OnApplyTemplate then calls ShowCaret because IsFocused is
            // already true, and the selection canvas is handed a presenter that is not attached yet.
            using (UnitTestApplication.Start(Services))
            {
                var textBox = new TextBox
                {
                    Template = CreateTextBoxTemplateWithSelectionBindings(),
                    Text = "Hello world",
                    Width = 150,
                    IsVisible = false,
                };

                var topLevel = new TestTopLevel(CreateMockTopLevelImpl().Object)
                {
                    Template = CreateTopLevelTemplate(),
                    Content = new VisualLayerManager
                    {
                        Child = new Border { Width = 200, Height = 200, Child = textBox },
                        EnableTextSelectorLayer = true
                    }
                };
                topLevel.LayoutManager.ExecuteInitialLayoutPass();

                // Hidden controls are never measured, so no template and no presenter yet.
                Assert.Null(textBox.FindDescendantOfType<TextPresenter>());

                textBox.IsVisible = true;
                textBox.Focus();
                textBox.SelectAll();

                topLevel.LayoutManager.ExecuteLayoutPass();

                var presenter = textBox.FindDescendantOfType<TextPresenter>()!;
                presenter.EnsureTextSelectionLayer();
                var canvas = presenter.TextSelectionHandleCanvas!;
                canvas.Show(forceTouchMode: true);
                topLevel.LayoutManager.ExecuteLayoutPass();

                var endHandle = canvas.Children.OfType<TextSelectionHandle>().ToList()[2];

                var touch = new TouchTestHelper();
                touch.Down(endHandle);
                touch.Move(endHandle, new Point(4, 0));
                touch.Move(endHandle, new Point(-40, 0));

                // Without the retry in SetPresenter the canvas keeps a null _textBox, both drag
                // handlers bail, and the selection sits exactly where SelectAll left it.
                Assert.True(textBox.SelectionEnd < 11,
                    $"dragging the end handle left should have shrunk the selection, got " +
                    $"[{textBox.SelectionStart}..{textBox.SelectionEnd}]");
            }
        }

        [Fact]
        public void Dragging_The_End_Handle_Extends_The_Selection()
        {
            using (UnitTestApplication.Start(Services))
            {
                var (_, textBox, _, canvas) = CreateDraggableSelection();

                // Canvas children are caret, start, end - with a selection the caret one is hidden.
                var handles = canvas.Children.OfType<TextSelectionHandle>().ToList();
                Assert.False(handles[0].IsVisible);
                var endHandle = handles[2];

                var touch = new TouchTestHelper();
                touch.Down(endHandle);

                var seen = new List<int>();
                for (var dx = 4; dx <= 28; dx += 8)
                {
                    touch.Move(endHandle, new Point(dx, 0));
                    seen.Add(textBox.SelectionEnd);
                }

                // The anchor stays put and the far end follows the finger, growing monotonically.
                Assert.Equal(0, textBox.SelectionStart);
                Assert.Equal(seen.OrderBy(x => x), seen);
                Assert.True(seen[^1] > 5, $"selection should have grown past 5, got [{string.Join(",", seen)}]");
            }
        }

        /// <summary>
        /// A focused text box with a selection, its handles forced into touch mode so they are live
        /// and draggable.
        /// </summary>
        private static (TopLevel topLevel, TextBox textBox, TextPresenter presenter, TextSelectionHandleCanvas canvas)
            CreateDraggableSelection(string text = "Hello world", double width = 150, int selectionEnd = 5,
                bool forceTouchMode = true)
        {
            var textBox = new TextBox
            {
                Template = CreateTextBoxTemplateWithSelectionBindings(),
                Text = text,
                Width = width
            };

            var visualLayerManager = new VisualLayerManager
            {
                Child = new Border { Width = width + 50, Height = 200, Child = textBox },
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
            textBox.SelectionEnd = selectionEnd;

            var presenter = textBox.FindDescendantOfType<TextPresenter>()!;
            presenter.EnsureTextSelectionLayer();

            var canvas = presenter.TextSelectionHandleCanvas!;
            canvas.Show(forceTouchMode);

            topLevel.LayoutManager.ExecuteLayoutPass();

            return (topLevel, textBox, presenter, canvas);
        }

        /// <summary>
        /// These tests run without the Fluent theme, so the magnifier has no template and therefore
        /// no size - and it refuses to show at zero size. Give it the shape the theme would.
        /// </summary>
        private static void GiveMagnifierATemplate(TextSelectionMagnifier magnifier, TopLevel topLevel)
        {
            // Mirrors the shipped themes, gutter included: the frame is inset so its drop shadow
            // falls inside the control, and the magnified image fills the frame, not the control.
            magnifier.Template = new FuncControlTemplate<TextSelectionMagnifier>((_, scope) =>
                new Border
                {
                    Name = "PART_Frame",
                    Margin = new Thickness(12),
                    Child = new Border { Name = "PART_Viewport" }.RegisterInNameScope(scope)
                }.RegisterInNameScope(scope));
            magnifier.Width = 144;
            magnifier.Height = 72;

            magnifier.ApplyTemplate();
            topLevel.LayoutManager.ExecuteLayoutPass();
        }

        /// <summary>
        /// Mirrors the real Fluent/Simple TextBox template: Text two-way, and CaretIndex,
        /// SelectionStart and SelectionEnd as plain one-way template bindings.
        /// <see cref="TextBoxTests.CreateTemplate"/> cannot be used here - it omits the selection
        /// entirely and binds CaretIndex <b>two-way</b>, which no shipped theme does. That matters:
        /// two-way would let a presenter caret move round-trip into TextBox.OnCaretIndexChanged,
        /// which collapses the selection onto the caret, so a handle drag would appear broken purely
        /// because of the harness.
        /// </summary>
        private static IControlTemplate CreateTextBoxTemplateWithSelectionBindings()
        {
            return new FuncControlTemplate<TextBox>((control, scope) =>
                new ScrollViewer
                {
                    Name = "PART_ScrollViewer",
                    Template = new FuncControlTemplate<ScrollViewer>(ScrollViewerTests.CreateTemplate),
                    Content = new TextPresenter
                    {
                        Name = "PART_TextPresenter",
                        [!!TextPresenter.TextProperty] = TemplateBinding(nameof(TextPresenter.Text), BindingMode.TwoWay),
                        [!TextPresenter.CaretIndexProperty] = TemplateBinding(nameof(TextPresenter.CaretIndex), BindingMode.OneWay),
                        [!TextPresenter.SelectionStartProperty] = TemplateBinding(nameof(TextPresenter.SelectionStart), BindingMode.OneWay),
                        [!TextPresenter.SelectionEndProperty] = TemplateBinding(nameof(TextPresenter.SelectionEnd), BindingMode.OneWay),
                    }.RegisterInNameScope(scope)
                }.RegisterInNameScope(scope));

            static Binding TemplateBinding(string path, BindingMode mode) => new()
            {
                Path = path,
                Mode = mode,
                Priority = BindingPriority.Template,
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            };
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
