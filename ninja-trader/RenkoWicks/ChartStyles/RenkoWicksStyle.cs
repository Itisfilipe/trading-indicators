#region Using declarations
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using SharpDX;
using SharpDX.Direct2D1;
using System;
using System.Collections.Generic;
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
        #endregion

        #region Fields
        /// <summary>
        /// Cached bar width calculation
        /// </summary>
        private float cachedBarWidth = -1;

        /// <summary>
        /// Cached bar paint width
        /// </summary>
        private int cachedBarPaintWidth = -1;

        /// <summary>
        /// Object pool for rectangles to reduce allocations
        /// </summary>
        private readonly Queue<RectangleF> rectanglePool = new Queue<RectangleF>();

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
            // Validate input
            if (barWidth < MIN_BAR_WIDTH)
                barWidth = MIN_BAR_WIDTH;
            else if (barWidth > MAX_BAR_WIDTH)
                barWidth = MAX_BAR_WIDTH;

            // Cache the result for performance
            if (cachedBarPaintWidth < 0 || barWidth != BarWidthUI)
            {
                cachedBarPaintWidth = 1 + 2 * (barWidth - 1) + 2 * (int)Math.Round(Stroke?.Width ?? 1);
            }

            return cachedBarPaintWidth;
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
                    // Calculate bar width once
                    float barWidth = GetBarPaintWidth(BarWidthUI);
                    if (cachedBarWidth != barWidth)
                    {
                        cachedBarWidth = barWidth;
                    }

                    // Get or create a rectangle for rendering
                    RectangleF rect = GetPooledRectangle();

                    // Render each visible bar
                    for (int idx = chartBars.FromIndex; idx <= chartBars.ToIndex; idx++)
                    {
                        // Validate bar index
                        if (idx < 0 || idx >= bars.Count)
                            continue;

                        RenderSingleBar(chartControl, chartScale, chartBars, bars, idx, rect, cachedBarWidth);
                    }

                    // Return rectangle to pool
                    ReturnRectangleToPool(rect);
                }
                catch (Exception ex)
                {
                    // Log error and continue
                    NinjaTrader.Code.Output.Process($"RenkoWickStyle.OnRender error: {ex.Message}", PrintTo.OutputTab1);
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
                ChartStyleType = (ChartStyleType)2588;
                BarWidth = DEFAULT_BAR_WIDTH;
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
            else if (State == State.Terminated)
            {
                // Clean up resources
                ClearCaches();
            }
        }
        #endregion

        #region Private Helper Methods
        /// <summary>
        /// Renders a single Renko bar with wicks
        /// </summary>
        private void RenderSingleBar(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars,
            Bars bars, int idx, RectangleF rect, float barWidth)
        {
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

            // Choose outline stroke based on bar direction
            Gui.Stroke outlineStroke = isUpBar ? Stroke : Stroke2;
            if (outlineStroke == null)
                outlineStroke = new Gui.Stroke(System.Windows.Media.Brushes.Gray, 1);

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

                // Fill the bar body
                RenderTarget.FillRectangle(rect, fillBrush);
            }

            // Get or determine the outline brush
            Brush outlineBrush = overriddenOutlineBrush ?? outlineStroke.BrushDX;
            if (outlineBrush != null)
            {
                // Transform brush if it's not a solid color brush
                if (!(outlineBrush is SolidColorBrush))
                    TransformBrush(outlineBrush, rect);

                // Draw the bar outline
                RenderTarget.DrawRectangle(rect, outlineBrush, outlineStroke.Width, outlineStroke.StrokeStyle);

                // Draw the upper wick if present
                DrawWick(x, highY, Math.Min(openY, closeY), highValue, Math.Max(openValue, closeValue),
                    outlineBrush, outlineStroke.Width, true);

                // Draw the lower wick if present
                DrawWick(x, Math.Max(openY, closeY), lowY, Math.Min(openValue, closeValue), lowValue,
                    outlineBrush, outlineStroke.Width, false);
            }
        }

        /// <summary>
        /// Draws a wick line (upper or lower) if the wick extends beyond the bar body
        /// </summary>
        private void DrawWick(float x, float wickStartY, float wickEndY, double wickValue, double bodyValue,
            Brush brush, float strokeWidth, bool isUpperWick)
        {
            // Only draw wick if it extends beyond the body
            bool shouldDrawWick = isUpperWick
                ? wickValue > bodyValue
                : wickValue < bodyValue;

            if (shouldDrawWick && brush != null)
            {
                // Ensure we have valid coordinates
                if (float.IsNaN(wickStartY) || float.IsNaN(wickEndY))
                    return;

                Vector2 startPoint = new Vector2(x, wickStartY);
                Vector2 endPoint = new Vector2(x, wickEndY);

                RenderTarget.DrawLine(startPoint, endPoint, brush, strokeWidth);
            }
        }

        /// <summary>
        /// Gets a rectangle from the pool or creates a new one
        /// </summary>
        private RectangleF GetPooledRectangle()
        {
            if (rectanglePool.Count > 0)
                return rectanglePool.Dequeue();

            return new RectangleF();
        }

        /// <summary>
        /// Returns a rectangle to the pool for reuse
        /// </summary>
        private void ReturnRectangleToPool(RectangleF rect)
        {
            if (rectanglePool.Count < 10) // Keep pool size reasonable
                rectanglePool.Enqueue(rect);
        }

        /// <summary>
        /// Clears all cached values
        /// </summary>
        private void ClearCaches()
        {
            cachedBarWidth = -1;
            cachedBarPaintWidth = -1;
            rectanglePool.Clear();
        }
        #endregion
    }
}
