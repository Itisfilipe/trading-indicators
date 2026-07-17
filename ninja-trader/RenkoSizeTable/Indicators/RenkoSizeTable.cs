#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// Table of suggested Renko box sizes (half ATR, in points and ticks) across several minute
    /// timeframes at once, each computed on its own secondary bar series so no chart switching or
    /// separate indicator instance per timeframe is needed.
    /// </summary>
    public class RenkoSizeTable : Indicator
    {
        private int[] timeframeMinutes;
        private Series<double>[] trSeries;
        private EMA[] emaATR;
        private double[] currentATR;
        private int[] currentHalfATRTicks;

        // SharpDX resources for table rendering
        private SharpDX.Direct2D1.Brush tableBgBrush;
        private SharpDX.Direct2D1.Brush headerBgBrush;
        private SharpDX.Direct2D1.Brush borderBrush;
        private SharpDX.Direct2D1.Brush textBrushWhite;
        private SharpDX.Direct2D1.Brush textBrushBlue;
        private SharpDX.Direct2D1.Brush textBrushGreen;
        private SharpDX.DirectWrite.TextFormat textFormat;
        private SharpDX.DirectWrite.TextFormat headerFormat;

        #region Properties

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe 1 (minutes)", Order = 0, GroupName = "Timeframes")]
        public int Timeframe1Minutes { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe 2 (minutes)", Order = 1, GroupName = "Timeframes")]
        public int Timeframe2Minutes { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe 3 (minutes)", Order = 2, GroupName = "Timeframes")]
        public int Timeframe3Minutes { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe 4 (minutes)", Order = 3, GroupName = "Timeframes")]
        public int Timeframe4Minutes { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR Length", Order = 4, GroupName = "Parameters")]
        public int ATRLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ignore Gaps", Order = 5, GroupName = "Parameters")]
        public bool IgnoreGaps { get; set; }

        [Range(0, 8), NinjaScriptProperty]
        [Display(Name = "Decimal Places", Order = 6, GroupName = "Parameters")]
        public int DecimalPlaces { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Table of suggested Renko box sizes (half ATR) across several minute timeframes at once.";
                Name = "Renko Size Table";
                IsSuspendedWhileInactive = true;
                IsOverlay = true;

                Timeframe1Minutes = 2;
                Timeframe2Minutes = 5;
                Timeframe3Minutes = 15;
                Timeframe4Minutes = 60;
                ATRLength = 14;
                IgnoreGaps = true;
                DecimalPlaces = 1;
            }
            else if (State == State.Configure)
            {
                // Four fixed slots, so AddDataSeries always runs exactly 4 times regardless of the
                // configured minute values: NinjaTrader only guarantees reliable series loading when
                // the NUMBER of AddDataSeries calls is fixed, not derived from parsed user input.
                timeframeMinutes = new[] { Timeframe1Minutes, Timeframe2Minutes, Timeframe3Minutes, Timeframe4Minutes };
                foreach (int minutes in timeframeMinutes)
                    AddDataSeries(BarsPeriodType.Minute, minutes);
            }
            else if (State == State.DataLoaded)
            {
                int count = timeframeMinutes.Length;
                trSeries = new Series<double>[count];
                emaATR = new EMA[count];
                currentATR = new double[count];
                currentHalfATRTicks = new int[count];

                for (int i = 0; i < count; i++)
                {
                    // Series<double>(this) would sync to the PRIMARY series' bar count; each
                    // timeframe's True Range series must instead sync to its own secondary Bars
                    // object (BarsArray[i + 1]) or every row would misalign against the chart's bars.
                    trSeries[i] = new Series<double>(BarsArray[i + 1]);
                    emaATR[i] = EMA(trSeries[i], ATRLength);
                }
            }
            else if (State == State.Terminated)
            {
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
            if (BarsInProgress == 0)
                return;

            int seriesIndex = BarsInProgress - 1;
            int idx = BarsInProgress;

            double high0 = Highs[idx][0];
            double low0 = Lows[idx][0];
            double trueRange;

            if (CurrentBars[idx] == 0)
                trueRange = high0 - low0;
            else if (IgnoreGaps && BarsArray[idx].IsFirstBarOfSession)
                trueRange = high0 - low0;
            else
                trueRange = Math.Max(high0 - low0, Math.Max(Math.Abs(high0 - Closes[idx][1]), Math.Abs(low0 - Closes[idx][1])));

            trSeries[seriesIndex][0] = trueRange;

            if (CurrentBars[idx] < ATRLength)
            {
                currentATR[seriesIndex] = 0;
                currentHalfATRTicks[seriesIndex] = 0;
                return;
            }

            double atr = emaATR[seriesIndex][0];
            currentATR[seriesIndex] = atr;
            currentHalfATRTicks[seriesIndex] = (int)Math.Round((atr / 2.0) / TickSize);
        }

        // Public, not protected: the base member is public and CS0507 rejects
        // narrowing it (same platform trap as ChartStyle.OnRender).
        public override void OnRenderTargetChanged()
        {
            if (tableBgBrush != null) { tableBgBrush.Dispose(); tableBgBrush = null; }
            if (headerBgBrush != null) { headerBgBrush.Dispose(); headerBgBrush = null; }
            if (borderBrush != null) { borderBrush.Dispose(); borderBrush = null; }
            if (textBrushWhite != null) { textBrushWhite.Dispose(); textBrushWhite = null; }
            if (textBrushBlue != null) { textBrushBlue.Dispose(); textBrushBlue = null; }
            if (textBrushGreen != null) { textBrushGreen.Dispose(); textBrushGreen = null; }
            if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
            if (headerFormat != null) { headerFormat.Dispose(); headerFormat = null; }

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

        private static readonly float[] ColumnWidths = { 55, 70, 80, 60 };
        private static readonly string[] ColumnHeaders = { "TF", "ATR", "Half ATR", "Ticks" };

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartControl == null || RenderTarget == null || tableBgBrush == null || textFormat == null || timeframeMinutes == null)
                return;

            float tableWidth = 0;
            foreach (float w in ColumnWidths)
                tableWidth += w;

            float rowHeight = 24;
            float headerHeight = 28;
            int rowCount = timeframeMinutes.Length;
            float tableHeight = headerHeight + rowHeight * rowCount;

            // ChartPanel.X/.Y are nonzero with a left-justified scale or when this isn't the
            // topmost panel; offsetting from the panel origin (not the chart canvas) keeps the
            // table inside the panel instead of clipped or drawn over a neighboring one.
            float tableX = ChartPanel.X + ChartPanel.W - tableWidth - 10;
            float tableY = ChartPanel.Y + 10;

            RectangleF tableRect = new RectangleF(tableX, tableY, tableWidth, tableHeight);
            RenderTarget.FillRectangle(tableRect, tableBgBrush);
            RenderTarget.DrawRectangle(tableRect, borderBrush, 1);

            RectangleF headerRect = new RectangleF(tableX, tableY, tableWidth, headerHeight);
            RenderTarget.FillRectangle(headerRect, headerBgBrush);
            DrawTableRow(tableY, tableX, headerHeight, ColumnHeaders, headerFormat, textBrushWhite, textBrushWhite, textBrushWhite, textBrushWhite);

            for (int i = 0; i < rowCount; i++)
            {
                float rowY = tableY + headerHeight + rowHeight * i;
                string[] cells =
                {
                    timeframeMinutes[i] + "m",
                    FormatValue(currentATR[i]),
                    FormatValue(currentATR[i] / 2.0),
                    currentHalfATRTicks[i].ToString()
                };
                RenderTarget.DrawLine(new SharpDX.Vector2(tableX, rowY), new SharpDX.Vector2(tableX + tableWidth, rowY), borderBrush, 1);
                DrawTableRow(rowY, tableX, rowHeight, cells, textFormat, textBrushWhite, textBrushBlue, textBrushGreen, textBrushGreen);
            }

            float columnX = tableX;
            for (int c = 0; c < ColumnWidths.Length - 1; c++)
            {
                columnX += ColumnWidths[c];
                RenderTarget.DrawLine(new SharpDX.Vector2(columnX, tableY), new SharpDX.Vector2(columnX, tableY + tableHeight), borderBrush, 1);
            }
        }

        private void DrawTableRow(float y, float tableX, float rowHeight, string[] cells, SharpDX.DirectWrite.TextFormat format, params SharpDX.Direct2D1.Brush[] brushes)
        {
            float x = tableX;
            for (int c = 0; c < cells.Length; c++)
            {
                RectangleF cellRect = new RectangleF(x + 6, y, ColumnWidths[c] - 6, rowHeight);
                SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, cells[c], format, cellRect.Width, cellRect.Height);
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(cellRect.X, cellRect.Y + 6), layout, brushes[c]);
                layout.Dispose();
                x += ColumnWidths[c];
            }
        }

        private string FormatValue(double value)
        {
            return DecimalPlaces == 0 ? Math.Round(value).ToString() : value.ToString("F" + DecimalPlaces);
        }
    }
}
