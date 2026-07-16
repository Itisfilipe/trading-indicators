#region Using declarations
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using SharpDX;
using SharpDX.Direct2D1;
using System;
using System.ComponentModel.DataAnnotations;
#endregion

namespace NinjaTrader.NinjaScript.ChartStyles
{
    /// <summary>
    /// Custom chart style for rendering Renko bars with price wicks.
    /// This style displays both the Renko brick body and the actual price extremes as wicks,
    /// similar to candlestick charts but adapted for Renko bar structure.
    /// </summary>
    public class RenkoWickStyle : ChartStyle
    {
        #region Constants
        /// <summary>
        /// Minimum bar width in pixels
        /// </summary>
        private const int MIN_BAR_WIDTH = 1;

        /// <summary>
        /// Maximum bar width in pixels
        /// </summary>
        private const int MAX_BAR_WIDTH = 100;

        /// <summary>
        /// Default bar width in pixels
        /// </summary>
        private const int DEFAULT_BAR_WIDTH = 3;

        /// <summary>
        /// Padding for bar rendering
        /// </summary>
        private const float BAR_PADDING = 0.5f;

        /// <summary>
        /// Unique id registering this chart style. RenkoWicksBarsType declares the same
        /// value as its BarsPeriodType and DefaultChartStyle. Note NinjaTrader only
        /// enumerates chart styles at startup: after compiling a new one, restart the
        /// platform or it will not appear in the Chart Styles list.
        /// </summary>
        private const int TYPE_ID = 2588;

        /// <summary>
        /// How faded a gap brick is drawn, by default.
        /// </summary>
        private const double DEFAULT_GAP_OPACITY = 0.4;

        /// <summary>
        /// Bricks the bars type synthesises to span a price jump carry no volume. Real
        /// bricks always close on a tick, which carries volume, so this separates them.
        /// </summary>
        private const long GAP_BRICK_VOLUME = 0;
        #endregion

        #region Properties
        /// <summary>
        /// Opacity applied to bricks that only exist to fill a price jump.
        /// </summary>
        /// <remarks>
        /// Fading rather than recolouring keeps the up/down direction of a gap readable,
        /// and reuses the brushes the base class already owns, so no extra device
        /// resource has to be created and released alongside the render target.
        /// </remarks>
        [Range(0.05, 1.0)]
        [Display(Name = "Gap brick opacity", Order = 10, GroupName = "NinjaScriptGeneral",
                 Description = "Opacity of the bricks drawn to fill a price jump. 1 draws them like any other brick.")]
        public double GapOpacity { get; set; }
        #endregion

        #region Fields
        /// <summary>
        /// Lock object for thread-safe operations
        /// </summary>
        private readonly object renderLock = new object();
        #endregion

        #region ChartStyle Override Methods
        /// <summary>
        /// Calculates the painted width of the chart bar including stroke width.
        /// </summary>
        /// <param name="barWidth">The base bar width</param>
        /// <returns>The total painted width including stroke</returns>
        public override int GetBarPaintWidth(int barWidth)
        {
            if (barWidth < MIN_BAR_WIDTH)
                barWidth = MIN_BAR_WIDTH;
            else if (barWidth > MAX_BAR_WIDTH)
                barWidth = MAX_BAR_WIDTH;

            return 1 + 2 * (barWidth - 1) + 2 * (int)Math.Round(Stroke?.Width ?? 1);
        }

        /// <summary>
        /// Renders the Renko bars with wicks on the chart.
        /// </summary>
        /// <param name="chartControl">The chart control object</param>
        /// <param name="chartScale">The chart scale for price-to-pixel conversion</param>
        /// <param name="chartBars">The chart bars data</param>
        public override void OnRender(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars)
        {
            if (chartControl == null || chartScale == null || chartBars == null)
                return;

            Bars bars = chartBars.Bars;
            if (bars == null || bars.Count == 0)
                return;

            // Thread synchronization for rendering
            lock (renderLock)
            {
                try
                {
                    float barWidth = GetBarPaintWidth(BarWidthUI);

                    for (int idx = chartBars.FromIndex; idx <= chartBars.ToIndex; idx++)
                    {
                        // Validate bar index
                        if (idx < 0 || idx >= bars.Count)
                            continue;

                        RenderSingleBar(chartControl, chartScale, chartBars, bars, idx, barWidth);
                    }
                }
                catch (Exception ex)
                {
                    // Logged in full and swallowed on purpose: a render fault must not
                    // tear down the chart, and the next frame recomputes from scratch.
                    NinjaTrader.Code.Output.Process($"RenkoWickStyle.OnRender error: {ex}", PrintTo.OutputTab1);
                }
            }
        }

        /// <summary>
        /// Handles state changes for initialization and resource cleanup.
        /// </summary>
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Renko with Wicks";
                Description = "ChartStyle to be used with Renko Wicks bars displaying actual price extremes";
                ChartStyleType = (ChartStyleType)TYPE_ID;
                BarWidth = DEFAULT_BAR_WIDTH;
                GapOpacity = DEFAULT_GAP_OPACITY;

                // Defaults let the base class bind these to the render target. Without
                // them, OnRender would have to build a Stroke per bar and read BrushDX
                // off a stroke that was never bound to a target. Black suits the white
                // chart background in use; both are user-configurable in the chart
                // properties ("Candle Outline" / "Candle Wick") for dark themes.
                Stroke = new Gui.Stroke(System.Windows.Media.Brushes.Black, 1);
                Stroke2 = new Gui.Stroke(System.Windows.Media.Brushes.Black, 1);
            }
            else if (State == State.Configure)
            {
                // Set property display names for UI
                SetPropertyName("BarWidth", Custom.Resource.NinjaScriptChartStyleBarWidth);
                SetPropertyName("DownBrush", Custom.Resource.NinjaScriptChartStyleCandleDownBarsColor);
                SetPropertyName("UpBrush", Custom.Resource.NinjaScriptChartStyleCandleUpBarsColor);
                SetPropertyName("Stroke", Custom.Resource.NinjaScriptChartStyleCandleOutline);
                SetPropertyName("Stroke2", Custom.Resource.NinjaScriptChartStyleCandleWick);

                // Remove the Name property from UI as it's fixed
                Properties.Remove(Properties.Find("Name", true));
            }
        }
        #endregion

        #region Private Helper Methods
        /// <summary>
        /// Renders a single Renko bar with wicks
        /// </summary>
        private void RenderSingleBar(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars,
            Bars bars, int idx, float barWidth)
        {
            RectangleF rect = new RectangleF();

            // Retrieve any override brushes (if set)
            Brush overriddenBrush = chartControl.GetBarOverrideBrush(chartBars, idx);
            Brush overriddenOutlineBrush = chartControl.GetCandleOutlineOverrideBrush(chartBars, idx);

            // Get price values from the bar (which now include wick extremes)
            double closeValue = bars.GetClose(idx);
            double openValue = bars.GetOpen(idx);
            double highValue = bars.GetHigh(idx);
            double lowValue = bars.GetLow(idx);

            // Validate price values
            if (double.IsNaN(closeValue) || double.IsNaN(openValue) ||
                double.IsNaN(highValue) || double.IsNaN(lowValue))
                return;

            // Convert prices to pixel coordinates
            float closeY = chartScale.GetYByValue(closeValue);
            float openY = chartScale.GetYByValue(openValue);
            float highY = chartScale.GetYByValue(highValue);
            float lowY = chartScale.GetYByValue(lowValue);
            float x = chartControl.GetXByBarIndex(chartBars, idx);

            // Determine if this is an up or down bar
            bool isUpBar = closeValue >= openValue;

            // Direction selects the fill only. The strokes are chosen by role, matching
            // the names the UI shows for them: Stroke is the outline, Stroke2 the wick.
            Gui.Stroke outlineStroke = Stroke;
            Gui.Stroke wickStroke = Stroke2;

            // Fade the bricks that only exist to span a price jump. The opacity is set on
            // every bar rather than set-and-restored around the gap ones, so a fault
            // part way through a frame cannot leave the whole chart dimmed.
            bool isGapBrick = bars.GetVolume(idx) == GAP_BRICK_VOLUME;
            float opacity = isGapBrick ? (float)GapOpacity : 1f;

            // Setup the rectangle for the bar body
            rect.X = x - barWidth * 0.5f + BAR_PADDING;
            rect.Y = Math.Min(openY, closeY);
            rect.Width = Math.Max(1, barWidth - 1);
            rect.Height = Math.Max(1, Math.Abs(openY - closeY));

            // Get or determine the fill brush
            Brush fillBrush = overriddenBrush ?? (isUpBar ? UpBrushDX : DownBrushDX);
            if (fillBrush != null)
            {
                // Transform brush if it's not a solid color brush
                if (!(fillBrush is SolidColorBrush))
                    TransformBrush(fillBrush, rect);

                // Fade only our own brush: an override brush (bar coloring from another
                // script or the user) carries its own opacity, which is not ours to stomp.
                if (overriddenBrush == null)
                    fillBrush.Opacity = opacity;
                RenderTarget.FillRectangle(rect, fillBrush);
            }

            // Draw the bar outline
            Brush outlineBrush = overriddenOutlineBrush ?? outlineStroke?.BrushDX;
            if (outlineBrush != null)
            {
                // Transform brush if it's not a solid color brush
                if (!(outlineBrush is SolidColorBrush))
                    TransformBrush(outlineBrush, rect);

                if (overriddenOutlineBrush == null)
                    outlineBrush.Opacity = opacity;
                RenderTarget.DrawRectangle(rect, outlineBrush, outlineStroke.Width, outlineStroke.StrokeStyle);
            }

            // Draw the wicks. The extends-beyond-the-body test lives here, next to the
            // prices it compares: the old helper took (wickValue, bodyValue) and the
            // lower-wick call passed them swapped, so its condition read "body bottom
            // below the low" -- never true -- and down wicks were never drawn at all.
            Brush wickBrush = overriddenOutlineBrush ?? wickStroke?.BrushDX;
            if (wickBrush != null)
            {
                if (overriddenOutlineBrush == null)
                    wickBrush.Opacity = opacity;

                double bodyTop = Math.Max(openValue, closeValue);
                double bodyBottom = Math.Min(openValue, closeValue);

                // Upper wick: from the high down to the top of the body
                if (highValue > bodyTop)
                    DrawWickLine(x, highY, Math.Min(openY, closeY), wickBrush, wickStroke.Width);

                // Lower wick: from the bottom of the body down to the low
                if (lowValue < bodyBottom)
                    DrawWickLine(x, Math.Max(openY, closeY), lowY, wickBrush, wickStroke.Width);
            }
        }

        /// <summary>
        /// Draws one vertical wick line; the caller decides whether the wick exists.
        /// </summary>
        private void DrawWickLine(float x, float startY, float endY, Brush brush, float strokeWidth)
        {
            if (float.IsNaN(startY) || float.IsNaN(endY))
                return;

            RenderTarget.DrawLine(new Vector2(x, startY), new Vector2(x, endY), brush, strokeWidth);
        }

        #endregion
    }
}
