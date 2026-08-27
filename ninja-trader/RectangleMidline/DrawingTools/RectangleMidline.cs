#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.DrawingTools
{
    public enum RectangleMidlineLevels
    {
        Midline,
        Quadrants,
        Octets,
        None
    }

    /// <summary>
    /// The platform's rectangle with two additions for zone drawing:
    ///
    /// Levels: horizontal lines inside the box at 50% (midline), quadrants
    /// (25/50/75%) or octets (every 12.5%).
    ///
    /// Extend handle: a grip on the middle of the right edge. Dragging it moves
    /// the rectangle's end in time only, ignoring price snap entirely, so a zone
    /// whose two placement clicks snapped onto a candle can be stretched forward
    /// freely without the box growing or shrinking vertically. Corner grips keep
    /// the platform's normal resize-with-snap behaviour.
    /// </summary>
    public class RectangleMidline : Rectangle
    {
        private const double GripSensitivity = 10;
        // ShapeBase's private cursorSensitivity: the capture radius its corner
        // grips already own, which the extend grip must not shadow.
        private const double CornerSensitivity = 15;
        private const float GripRadius = 4f;

        private static readonly double[] MidlineFractions = { 0.5 };
        private static readonly double[] QuadrantFractions = { 0.25, 0.5, 0.75 };
        private static readonly double[] OctetFractions = { 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875 };

        private ChartAnchor extendingAnchor;
        private readonly DeviceBrush gripFillBrush = new DeviceBrush { Brush = Brushes.White };

        [Display(Name = "Levels", GroupName = "Levels", Order = 1,
                 Description = "Lines drawn inside the rectangle: the 50% midline, quadrants (25/50/75%) or octets (every 12.5%).")]
        public RectangleMidlineLevels Levels { get; set; }

        [Display(Name = "Midline", GroupName = "Levels", Order = 2)]
        public Stroke MidlineStroke { get; set; }

        [Display(Name = "Other levels", GroupName = "Levels", Order = 3)]
        public Stroke QuarterStroke { get; set; }

        // Drawings saved before the Levels enum serialized the midline as this
        // bool; keep mapping it so an explicitly hidden midline stays hidden.
        [Browsable(false)]
        public bool IsMidlineVisible
        {
            get { return Levels != RectangleMidlineLevels.None; }
            set { if (!value) Levels = RectangleMidlineLevels.None; }
        }

        protected override void OnStateChange()
        {
            base.OnStateChange();

            if (State == State.SetDefaults)
            {
                Name = "Rectangle Midline";
                Description = "Rectangle with midline/quadrant/octet levels and a snap-free horizontal extend handle.";
                Levels = RectangleMidlineLevels.Midline;
                MidlineStroke = new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dash, 1f);
                QuarterStroke = new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dot, 1f);
            }
        }

        private bool IsOnExtendGrip(Point point, ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
        {
            Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point gripPoint = new Point(Math.Max(startPoint.X, endPoint.X), (startPoint.Y + endPoint.Y) / 2);
            if ((gripPoint - point).Length > GripSensitivity)
                return false;

            // On a box shorter than ~50 pixels the grip circle overlaps the
            // corners' capture radius; a click that close to a corner belongs
            // to the corner resize, not to the extend grip.
            Point[] corners =
            {
                new Point(startPoint.X, startPoint.Y), new Point(endPoint.X, startPoint.Y),
                new Point(startPoint.X, endPoint.Y), new Point(endPoint.X, endPoint.Y)
            };
            foreach (Point corner in corners)
                if ((corner - point).Length <= CornerSensitivity)
                    return false;

            return true;
        }

        public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
        {
            if (DrawingState == DrawingState.Editing && extendingAnchor != null)
                return Cursors.SizeWE;

            if (IsSelected && DrawingState == DrawingState.Normal && !IsLocked
                && IsOnExtendGrip(point, chartControl, chartPanel, chartScale))
                return Cursors.SizeWE;

            return base.GetCursor(chartControl, chartPanel, chartScale, point);
        }

        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Normal && IsSelected && !IsLocked)
            {
                Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
                if (IsOnExtendGrip(point, chartControl, chartPanel, chartScale))
                {
                    Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
                    Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
                    extendingAnchor = startPoint.X <= endPoint.X ? EndAnchor : StartAnchor;
                    DrawingState = DrawingState.Editing;
                    return;
                }
            }

            base.OnMouseDown(chartControl, chartPanel, chartScale, dataPoint);
        }

        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (extendingAnchor != null && DrawingState == DrawingState.Editing)
            {
                extendingAnchor.Time = dataPoint.Time;
                extendingAnchor.SlotIndex = dataPoint.SlotIndex;
                return;
            }

            base.OnMouseMove(chartControl, chartPanel, chartScale, dataPoint);
        }

        public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            extendingAnchor = null;
            base.OnMouseUp(chartControl, chartPanel, chartScale, dataPoint);
        }

        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];
            Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
            float left = (float)Math.Min(startPoint.X, endPoint.X);
            float right = (float)Math.Max(startPoint.X, endPoint.X);
            double top = Math.Min(startPoint.Y, endPoint.Y);
            double bottom = Math.Max(startPoint.Y, endPoint.Y);

            // The grip draws in the hit-test pass too: that widens the clickable
            // area beyond the thin outline the base rectangle registers there.
            if (IsSelected && DrawingState != DrawingState.Building)
                RenderExtendGrip(right, (float)((top + bottom) / 2));

            if (IsInHitTest)
                return;

            double[] fractions;
            switch (Levels)
            {
                case RectangleMidlineLevels.Midline: fractions = MidlineFractions; break;
                case RectangleMidlineLevels.Quadrants: fractions = QuadrantFractions; break;
                case RectangleMidlineLevels.Octets: fractions = OctetFractions; break;
                default: return;
            }

            foreach (double fraction in fractions)
            {
                Stroke stroke = fraction == 0.5 ? MidlineStroke : QuarterStroke;

                // Pixel positions rather than prices rounded to a tick: rounding
                // would push a line visibly off its fraction whenever the box
                // spans an awkward number of ticks. The half-pixel shift on even
                // stroke widths is what the platform's own shapes do to keep
                // edges from blurring.
                double strokePixelAdjust = stroke.Width % 2 == 0 ? 0.5d : 0d;
                float y = (float)(top + fraction * (bottom - top) + strokePixelAdjust);

                stroke.RenderTarget = RenderTarget;
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(left, y), new SharpDX.Vector2(right, y),
                    stroke.BrushDX, stroke.Width, stroke.StrokeStyle);
            }
        }

        private void RenderExtendGrip(float x, float y)
        {
            SharpDX.Direct2D1.Ellipse grip = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), GripRadius, GripRadius);
            gripFillBrush.RenderTarget = RenderTarget;
            if (gripFillBrush.BrushDX != null)
                RenderTarget.FillEllipse(grip, gripFillBrush.BrushDX);
            OutlineStroke.RenderTarget = RenderTarget;
            RenderTarget.DrawEllipse(grip, OutlineStroke.BrushDX, 1f);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            gripFillBrush.RenderTarget = null;
        }
    }
}
