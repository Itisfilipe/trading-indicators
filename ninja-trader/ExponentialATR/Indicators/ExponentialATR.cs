#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// ATR Renko Size Calculator - Calculates ATR with EMA smoothing, gap handling, and displays values in points and ticks for Renko sizing.
    /// </summary>
    public class ATRRenkoSizeCalculator : Indicator
    {
        private EMA emaATR;
        private Series<double> trSeries;

        // SharpDX brushes for table rendering
        private SharpDX.Direct2D1.Brush tableBgBrush;
        private SharpDX.Direct2D1.Brush headerBgBrush;
        private SharpDX.Direct2D1.Brush borderBrush;
        private SharpDX.Direct2D1.Brush textBrushWhite;
        private SharpDX.Direct2D1.Brush textBrushBlue;
        private SharpDX.Direct2D1.Brush textBrushGreen;
        private SharpDX.DirectWrite.TextFormat textFormat;
        private SharpDX.DirectWrite.TextFormat headerFormat;

        // Store calculated values for table display
        private double currentATR;
        private double currentHalfATR;
        private int currentHalfATRTicks;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description               = "ATR Renko Size Calculator - Calculates ATR with EMA smoothing, gap handling, and displays values in points and ticks for Renko sizing.";
                Name                      = "ATR Renko Size Calculator";
                IsSuspendedWhileInactive  = true;
                IsOverlay                 = false;

                Period                    = 14;
                IgnoreGaps                = true;
                ShowHalfATR               = true;
                ShowTable                 = true;
                DecimalPlaces             = 1;

                AddPlot(System.Windows.Media.Brushes.Blue, "ATR");
                AddPlot(System.Windows.Media.Brushes.Green, "HalfATR");
            }
            else if (State == State.DataLoaded)
            {
                trSeries = new Series<double>(this);
                emaATR = EMA(trSeries, Period);
            }
            else if (State == State.Terminated)
            {
                // Dispose of SharpDX resources
                if (tableBgBrush != null) tableBgBrush.Dispose();
                if (headerBgBrush != null) headerBgBrush.Dispose();
                if (borderBrush != null) borderBrush.Dispose();
                if (textBrushWhite != null) textBrushWhite.Dispose();
                if (textBrushBlue != null) textBrushBlue.Dispose();
                if (textBrushGreen != null) textBrushGreen.Dispose();
                if (textFormat != null) textFormat.Dispose();
                if (headerFormat != null) headerFormat.Dispose();
            }
        }

        protected override void OnBarUpdate()
        {
            double high0 = High[0];
            double low0  = Low[0];

            // Calculate True Range with optional gap handling
            double trueRange;

            if (CurrentBar == 0)
            {
                // First bar ever: only High - Low
                trueRange = high0 - low0;
            }
            else if (IgnoreGaps && Bars.IsFirstBarOfSession)
            {
                // First bar of new session with gap handling: only High - Low
                trueRange = high0 - low0;
            }
            else
            {
                // Standard True Range: max of (H-L, |H-PC|, |L-PC|)
                trueRange = Math.Max(high0 - low0, Math.Max(Math.Abs(high0 - Close[1]), Math.Abs(low0 - Close[1])));
            }

            // Store True Range in series - EMA will automatically process this
            trSeries[0] = trueRange;

            // Wait for minimum bars before using EMA
            if (CurrentBar < Period)
            {
                Value[0] = 0;
                Values[1][0] = 0;
                currentATR = 0;
                currentHalfATR = 0;
                currentHalfATRTicks = 0;
                return;
            }

            // Read the EMA-smoothed ATR value
            double atr = emaATR[0];

            // Set ATR value
            Value[0] = atr;

            // Calculate and set Half ATR
            double halfATR = atr / 2.0;
            Values[1][0] = ShowHalfATR ? halfATR : double.NaN;

            // Store values for table display
            currentATR = atr;
            currentHalfATR = halfATR;
            currentHalfATRTicks = (int)Math.Round(halfATR / TickSize);
        }

        protected override void OnRenderTargetChanged()
        {
            // Dispose of old resources and set to null
            if (tableBgBrush != null)
            {
                tableBgBrush.Dispose();
                tableBgBrush = null;
            }
            if (headerBgBrush != null)
            {
                headerBgBrush.Dispose();
                headerBgBrush = null;
            }
            if (borderBrush != null)
            {
                borderBrush.Dispose();
                borderBrush = null;
            }
            if (textBrushWhite != null)
            {
                textBrushWhite.Dispose();
                textBrushWhite = null;
            }
            if (textBrushBlue != null)
            {
                textBrushBlue.Dispose();
                textBrushBlue = null;
            }
            if (textBrushGreen != null)
            {
                textBrushGreen.Dispose();
                textBrushGreen = null;
            }
            if (textFormat != null)
            {
                textFormat.Dispose();
                textFormat = null;
            }
            if (headerFormat != null)
            {
                headerFormat.Dispose();
                headerFormat = null;
            }

            // Create new resources if RenderTarget is available
            if (RenderTarget != null)
            {
                tableBgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(0, 0, 0, 200));
                headerBgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(70, 70, 70, 180));
                borderBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(128, 128, 128, 255));
                textBrushWhite = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(255, 255, 255, 255));
                textBrushBlue = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(0, 150, 255, 255));
                textBrushGreen = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(0, 200, 0, 255));

                textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 12);
                headerFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 12);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (!ShowTable || Bars == null || ChartControl == null || RenderTarget == null)
                return;

            if (tableBgBrush == null || textFormat == null)
                return;

            // Table dimensions
            float tableWidth = 200;
            float rowHeight = 25;
            float headerHeight = 30;
            float cellPadding = 8;

            // Position table at top right with margin
            float tableX = ChartPanel.W - tableWidth - 10;
            float tableY = 10;

            // Draw table background
            RectangleF tableRect = new RectangleF(tableX, tableY, tableWidth, headerHeight + (rowHeight * 3));
            RenderTarget.FillRectangle(tableRect, tableBgBrush);
            RenderTarget.DrawRectangle(tableRect, borderBrush, 1);

            // Draw header row
            RectangleF headerRect = new RectangleF(tableX, tableY, tableWidth, headerHeight);
            RenderTarget.FillRectangle(headerRect, headerBgBrush);
            RenderTarget.DrawRectangle(headerRect, borderBrush, 1);

            // Draw header text
            RectangleF headerMetricRect = new RectangleF(tableX + cellPadding, tableY, tableWidth / 2 - cellPadding, headerHeight);
            RectangleF headerValueRect = new RectangleF(tableX + tableWidth / 2, tableY, tableWidth / 2 - cellPadding, headerHeight);

            SharpDX.DirectWrite.TextLayout headerMetricLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, "Metric", headerFormat, headerMetricRect.Width, headerMetricRect.Height);
            SharpDX.DirectWrite.TextLayout headerValueLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, "Value", headerFormat, headerValueRect.Width, headerValueRect.Height);

            RenderTarget.DrawTextLayout(new SharpDX.Vector2(headerMetricRect.X, headerMetricRect.Y + 8), headerMetricLayout, textBrushWhite);
            RenderTarget.DrawTextLayout(new SharpDX.Vector2(headerValueRect.X, headerValueRect.Y + 8), headerValueLayout, textBrushWhite);

            headerMetricLayout.Dispose();
            headerValueLayout.Dispose();

            // Draw vertical line between columns
            float columnDividerX = tableX + tableWidth / 2;
            RenderTarget.DrawLine(new SharpDX.Vector2(columnDividerX, tableY), new SharpDX.Vector2(columnDividerX, tableY + headerHeight + (rowHeight * 3)), borderBrush, 1);

            // Format values
            string atrValueStr = FormatValue(currentATR);
            string halfATRValueStr = FormatValue(currentHalfATR);
            string halfATRTicksStr = currentHalfATRTicks.ToString();

            // Row 1: ATR
            float row1Y = tableY + headerHeight;
            DrawTableRow(row1Y, tableX, tableWidth, rowHeight, cellPadding, "ATR", atrValueStr, textBrushBlue, textBrushWhite, borderBrush);

            // Row 2: Half ATR
            float row2Y = row1Y + rowHeight;
            DrawTableRow(row2Y, tableX, tableWidth, rowHeight, cellPadding, "Half ATR", halfATRValueStr, textBrushGreen, textBrushWhite, borderBrush);

            // Row 3: Half ATR (Ticks)
            float row3Y = row2Y + rowHeight;
            DrawTableRow(row3Y, tableX, tableWidth, rowHeight, cellPadding, "Half ATR (Ticks)", halfATRTicksStr, textBrushGreen, textBrushWhite, borderBrush);
        }

        private void DrawTableRow(float y, float tableX, float tableWidth, float rowHeight, float cellPadding, string label, string value, SharpDX.Direct2D1.Brush labelBrush, SharpDX.Direct2D1.Brush valueBrush, SharpDX.Direct2D1.Brush borderBrush)
        {
            // Draw horizontal border
            RenderTarget.DrawLine(new SharpDX.Vector2(tableX, y), new SharpDX.Vector2(tableX + tableWidth, y), borderBrush, 1);

            // Draw label
            RectangleF labelRect = new RectangleF(tableX + cellPadding, y, tableWidth / 2 - cellPadding, rowHeight);
            SharpDX.DirectWrite.TextLayout labelLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, label, textFormat, labelRect.Width, labelRect.Height);
            RenderTarget.DrawTextLayout(new SharpDX.Vector2(labelRect.X, labelRect.Y + 6), labelLayout, labelBrush);
            labelLayout.Dispose();

            // Draw value
            RectangleF valueRect = new RectangleF(tableX + tableWidth / 2 + cellPadding, y, tableWidth / 2 - cellPadding, rowHeight);
            SharpDX.DirectWrite.TextLayout valueLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, value, textFormat, valueRect.Width, valueRect.Height);
            RenderTarget.DrawTextLayout(new SharpDX.Vector2(valueRect.X, valueRect.Y + 6), valueLayout, valueBrush);
            valueLayout.Dispose();
        }

        private string FormatValue(double value)
        {
            if (DecimalPlaces == 0)
                return Math.Round(value).ToString();
            else
                return value.ToString("F" + DecimalPlaces);
        }

        #region Properties
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR Length", GroupName = "Parameters", Order = 1)]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ignore Gaps", GroupName = "Parameters", Order = 2)]
        public bool IgnoreGaps { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Half ATR", GroupName = "Parameters", Order = 3)]
        public bool ShowHalfATR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Values Table", GroupName = "Parameters", Order = 4)]
        public bool ShowTable { get; set; }

        [Range(0, 8), NinjaScriptProperty]
        [Display(Name = "Decimal Places", GroupName = "Parameters", Order = 5)]
        public int DecimalPlaces { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> HalfATR
        {
            get { return Values[1]; }
        }
        #endregion
    }
}



#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private FilipeAmaral.ATRRenkoSizeCalculator[] cacheATRRenkoSizeCalculator;
		public FilipeAmaral.ATRRenkoSizeCalculator ATRRenkoSizeCalculator(int period, bool ignoreGaps, bool showHalfATR, bool showTable, int decimalPlaces)
		{
			return ATRRenkoSizeCalculator(Input, period, ignoreGaps, showHalfATR, showTable, decimalPlaces);
		}

		public FilipeAmaral.ATRRenkoSizeCalculator ATRRenkoSizeCalculator(ISeries<double> input, int period, bool ignoreGaps, bool showHalfATR, bool showTable, int decimalPlaces)
		{
			if (cacheATRRenkoSizeCalculator != null)
				for (int idx = 0; idx < cacheATRRenkoSizeCalculator.Length; idx++)
					if (cacheATRRenkoSizeCalculator[idx] != null && cacheATRRenkoSizeCalculator[idx].Period == period && cacheATRRenkoSizeCalculator[idx].IgnoreGaps == ignoreGaps && cacheATRRenkoSizeCalculator[idx].ShowHalfATR == showHalfATR && cacheATRRenkoSizeCalculator[idx].ShowTable == showTable && cacheATRRenkoSizeCalculator[idx].DecimalPlaces == decimalPlaces && cacheATRRenkoSizeCalculator[idx].EqualsInput(input))
						return cacheATRRenkoSizeCalculator[idx];
			return CacheIndicator<FilipeAmaral.ATRRenkoSizeCalculator>(new FilipeAmaral.ATRRenkoSizeCalculator(){ Period = period, IgnoreGaps = ignoreGaps, ShowHalfATR = showHalfATR, ShowTable = showTable, DecimalPlaces = decimalPlaces }, input, ref cacheATRRenkoSizeCalculator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.FilipeAmaral.ATRRenkoSizeCalculator ATRRenkoSizeCalculator(int period, bool ignoreGaps, bool showHalfATR, bool showTable, int decimalPlaces)
		{
			return indicator.ATRRenkoSizeCalculator(Input, period, ignoreGaps, showHalfATR, showTable, decimalPlaces);
		}

		public Indicators.FilipeAmaral.ATRRenkoSizeCalculator ATRRenkoSizeCalculator(ISeries<double> input , int period, bool ignoreGaps, bool showHalfATR, bool showTable, int decimalPlaces)
		{
			return indicator.ATRRenkoSizeCalculator(input, period, ignoreGaps, showHalfATR, showTable, decimalPlaces);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.FilipeAmaral.ATRRenkoSizeCalculator ATRRenkoSizeCalculator(int period, bool ignoreGaps, bool showHalfATR, bool showTable, int decimalPlaces)
		{
			return indicator.ATRRenkoSizeCalculator(Input, period, ignoreGaps, showHalfATR, showTable, decimalPlaces);
		}

		public Indicators.FilipeAmaral.ATRRenkoSizeCalculator ATRRenkoSizeCalculator(ISeries<double> input , int period, bool ignoreGaps, bool showHalfATR, bool showTable, int decimalPlaces)
		{
			return indicator.ATRRenkoSizeCalculator(input, period, ignoreGaps, showHalfATR, showTable, decimalPlaces);
		}
	}
}

#endregion
