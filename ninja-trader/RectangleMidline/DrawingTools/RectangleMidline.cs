#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.DrawingTools
{
    /// <summary>
    /// The platform's rectangle with a line across its vertical centre -- the
    /// level a pullback into a zone is normally measured against.
    ///
    /// Everything else (anchors, corner resizing, fill, outline, alerts, hit
    /// testing) is inherited from the built-in Rectangle, so the tool behaves
    /// exactly like the one already in the Draw menu.
    /// </summary>
    public class RectangleMidline : Rectangle
    {
        [Display(Name = "Show midline", GroupName = "Midline", Order = 1,
                 Description = "Draws a horizontal line across the middle of the rectangle.")]
        public bool IsMidlineVisible { get; set; }

        [Display(Name = "Midline", GroupName = "Midline", Order = 2)]
        public Stroke MidlineStroke { get; set; }

        protected override void OnStateChange()
        {
            base.OnStateChange();

            if (State == State.SetDefaults)
            {
                Name = "Rectangle Midline";
                Description = "Rectangle with a line across its vertical centre.";
                IsMidlineVisible = true;
                MidlineStroke = new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dash, 1f);
            }
        }

        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            // The hit-test pass only needs the rectangle: the midline runs inside
            // it, so it can add nothing to what is already clickable.
            if (!IsMidlineVisible || IsInHitTest)
                return;

            ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];
            Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

            // Pixel centre rather than the mid price rounded to a tick: rounding
            // would push the line visibly off centre whenever the box spans an
            // odd number of ticks. The half-pixel shift on even stroke widths is
            // what the platform's own shapes do to keep edges from blurring.
            double strokePixelAdjust = MidlineStroke.Width % 2 == 0 ? 0.5d : 0d;
            float midY = (float)((startPoint.Y + endPoint.Y) / 2 + strokePixelAdjust);

            MidlineStroke.RenderTarget = RenderTarget;
            RenderTarget.DrawLine(
                new SharpDX.Vector2((float)Math.Min(startPoint.X, endPoint.X), midY),
                new SharpDX.Vector2((float)Math.Max(startPoint.X, endPoint.X), midY),
                MidlineStroke.BrushDX, MidlineStroke.Width, MidlineStroke.StrokeStyle);
        }
    }
}
