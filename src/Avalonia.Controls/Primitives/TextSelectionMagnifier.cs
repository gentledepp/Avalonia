using Avalonia.Controls.Metadata;
using Avalonia.Media;
using Avalonia.Utilities;

namespace Avalonia.Controls.Primitives
{
    /// <summary>
    /// The loupe shown above the text while a <see cref="TextSelectionHandle"/> is dragged, so the
    /// caret stays visible under the finger. Matches the Android <c>Magnifier</c> widget and the
    /// iOS text loupe.
    /// </summary>
    /// <remarks>
    /// The magnified image is a <see cref="VisualBrush"/> over the live <c>TextPresenter</c>. That is
    /// safe on an attached visual: <c>Control.EnsureInitialized</c> only measures/arranges when
    /// <c>VisualRoot</c> is null, and <c>ImmediateRenderer</c> is a pure traversal, so nothing here
    /// disturbs the presenter's layout.
    /// </remarks>
    [TemplatePart("PART_Frame", typeof(Border))]
    [TemplatePart("PART_Viewport", typeof(Border))]
    public class TextSelectionMagnifier : TemplatedControl
    {
        /// <summary>
        /// Defines the <see cref="Zoom"/> property.
        /// </summary>
        public static readonly StyledProperty<double> ZoomProperty =
            AvaloniaProperty.Register<TextSelectionMagnifier, double>(nameof(Zoom), 1.25);

        /// <summary>
        /// Defines the <see cref="VerticalOffset"/> property.
        /// </summary>
        public static readonly StyledProperty<double> VerticalOffsetProperty =
            AvaloniaProperty.Register<TextSelectionMagnifier, double>(nameof(VerticalOffset), 8);

        private readonly VisualBrush _brush = new()
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
        };

        private Border? _viewport;
        private Border? _frame;

        /// <summary>
        /// The inset of the visible frame inside the control. The frame's drop shadow has to fall
        /// inside the control's own bounds, because a shadow drawn outside them is clipped away, so
        /// the control measures larger than the loupe and the caller has to allow for the gap.
        /// </summary>
        internal Thickness FrameMargin => _frame?.Margin ?? default;

        /// <summary>The brush painting the magnified image. Exposed for tests.</summary>
        internal VisualBrush SourceBrush => _brush;

        /// <summary>
        /// Gets or sets how much the magnified text is enlarged. Android's Magnifier uses 1.25.
        /// </summary>
        public double Zoom
        {
            get => GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        /// <summary>
        /// Gets or sets the gap between the bottom of the magnifier and the top of the magnified line.
        /// </summary>
        public double VerticalOffset
        {
            get => GetValue(VerticalOffsetProperty);
            set => SetValue(VerticalOffsetProperty, value);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            _frame = e.NameScope.Find<Border>("PART_Frame");
            _viewport = e.NameScope.Find<Border>("PART_Viewport");

            // Painting the brush as the viewport's Background gets the rounded corners for free -
            // a Border fills its corner radius with Background, so no extra clip is needed.
            if (_viewport != null)
                _viewport.Background = _brush;
        }

        /// <summary>
        /// Points the magnifier at <paramref name="focus"/>, given in <paramref name="source"/>'s
        /// coordinates. Placement is the caller's job - only it knows the layer the magnifier is in.
        /// </summary>
        internal void Update(Visual source, Point focus)
        {
            _brush.Visual = source;

            var zoom = Zoom <= 0 ? 1 : Zoom;
            // DesiredSize, not Bounds: the loupe is only arranged on the layout pass after it is
            // shown, so Bounds is still zero on the frame the drag starts. Deflate by the frame's
            // margin, because the magnified image fills the frame, not the whole control - the rest
            // is the transparent gutter the drop shadow needs.
            var outer = DesiredSize.Width > 0 && DesiredSize.Height > 0 ? DesiredSize : Bounds.Size;
            var size = outer.Deflate(FrameMargin);

            if (size.Width <= 0 || size.Height <= 0)
                return;

            // The window into the presenter that gets blown up to fill the viewport.
            var sourceSize = new Size(size.Width / zoom, size.Height / zoom);
            var sourceRect = new Rect(
                focus.X - sourceSize.Width / 2,
                focus.Y - sourceSize.Height / 2,
                sourceSize.Width,
                sourceSize.Height);

            // Keep the window inside the presenter so the loupe never magnifies blank space beyond
            // the text. Near an edge the caret sits off-centre, which is what the platforms do too.
            sourceRect = ClampInto(sourceRect, new Rect(source.Bounds.Size));
            _brush.SourceRect = new RelativeRect(sourceRect, RelativeUnit.Absolute);

            // The brush snapshots the presenter when the render data is built, so the loupe only
            // picks up caret/selection changes if we ask for a repaint.
            InvalidateVisual();
        }

        /// <summary>
        /// Slides <paramref name="rect"/> so it lies inside <paramref name="bounds"/> where it fits,
        /// leaving it centred on the axes where it does not.
        /// </summary>
        internal static Rect ClampInto(Rect rect, Rect bounds)
        {
            var x = rect.Width >= bounds.Width
                ? bounds.X - (rect.Width - bounds.Width) / 2
                : MathUtilities.Clamp(rect.X, bounds.X, bounds.Right - rect.Width);

            var y = rect.Height >= bounds.Height
                ? bounds.Y - (rect.Height - bounds.Height) / 2
                : MathUtilities.Clamp(rect.Y, bounds.Y, bounds.Bottom - rect.Height);

            return new Rect(x, y, rect.Width, rect.Height);
        }
    }
}
