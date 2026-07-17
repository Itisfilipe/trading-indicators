#region Using declarations
using System;
using System.Collections.Generic;
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
        private int[] timeframeDays;
        private double[] currentATR;
        private int[] currentHalfATRTicks;

        // Manual-EMA state per timeframe. The ATR period is days x bars-per-day, and
        // bars-per-day is only learned by watching a session roll over, so the period
        // is not known when an EMA() instance would have to be constructed. A manual
        // EMA whose smoothing constant is recomputed each bar handles the moving
        // target (ported from the NTSL renko-size-calculator, which works the same way).
        private double[] emaAtr;
        private bool[] emaSeeded;
        private int[] barsInCurrentDay;
        private int[] barsPerDay;
        private int[] lastProcessedBar;
        private bool[] trackedBarFirstOfSession;
        private List<double>[] firstSessionTrueRanges;

        // Median-of-daily-ATRs state per timeframe: the EMA column is the live read,
        // this one is the "typical day" read. Each completed session contributes its
        // mean true range; the median over the row's day window rejects outlier days
        // (one hot CPI session out of ten moves it not at all), so the suggested
        // brick size stays put unless volatility genuinely changes regime.
        private double[] sessionTrSum;
        private int[] sessionTrCount;
        private bool[] sessionOpenObserved;
        private List<double>[] dailyAtrs;
        private int[] currentMedianTicks;

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

        // Each timeframe row pairs its bar interval with its own ATR lookback in
        // days; the day count converts to bars from how many bars that timeframe's
        // sessions actually hold.
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe 1 (minutes)", Order = 0, GroupName = "Timeframes")]
        public int Timeframe1Minutes { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR 1 (days)", Order = 1, GroupName = "Timeframes")]
        public int Atr1Days { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe 2 (minutes)", Order = 2, GroupName = "Timeframes")]
        public int Timeframe2Minutes { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR 2 (days)", Order = 3, GroupName = "Timeframes")]
        public int Atr2Days { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe 3 (minutes)", Order = 4, GroupName = "Timeframes")]
        public int Timeframe3Minutes { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR 3 (days)", Order = 5, GroupName = "Timeframes")]
        public int Atr3Days { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe 4 (minutes)", Order = 6, GroupName = "Timeframes")]
        public int Timeframe4Minutes { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR 4 (days)", Order = 7, GroupName = "Timeframes")]
        public int Atr4Days { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ignore Gaps", Order = 5, GroupName = "Parameters")]
        public bool IgnoreGaps { get; set; }

        [Range(0, 8), NinjaScriptProperty]
        [Display(Name = "Decimal Places", Order = 6, GroupName = "Parameters")]
        public int DecimalPlaces { get; set; }

        [Range(0, 2000), NinjaScriptProperty]
        [Display(Name = "Top Margin (pixels)", Order = 7, GroupName = "Parameters",
                 Description = "Distance between the panel's top edge and the table, so the table " +
                               "clears the chart toolbar icons in the top-right corner.")]
        public int TopMarginPixels { get; set; }

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
                Atr1Days = 3;
                Timeframe2Minutes = 5;
                Atr2Days = 5;
                Timeframe3Minutes = 15;
                Atr3Days = 10;
                Timeframe4Minutes = 60;
                Atr4Days = 20;
                IgnoreGaps = true;
                DecimalPlaces = 1;
                TopMarginPixels = 40;
            }
            else if (State == State.Configure)
            {
                // Four fixed slots, so AddDataSeries always runs exactly 4 times regardless of the
                // configured minute values: NinjaTrader only guarantees reliable series loading when
                // the NUMBER of AddDataSeries calls is fixed, not derived from parsed user input.
                timeframeMinutes = new[] { Timeframe1Minutes, Timeframe2Minutes, Timeframe3Minutes, Timeframe4Minutes };
                timeframeDays = new[] { Atr1Days, Atr2Days, Atr3Days, Atr4Days };
                foreach (int minutes in timeframeMinutes)
                    AddDataSeries(BarsPeriodType.Minute, minutes);
            }
            else if (State == State.DataLoaded)
            {
                int count = timeframeMinutes.Length;
                currentATR = new double[count];
                currentHalfATRTicks = new int[count];
                emaAtr = new double[count];
                emaSeeded = new bool[count];
                barsInCurrentDay = new int[count];
                barsPerDay = new int[count];
                lastProcessedBar = new int[count];
                trackedBarFirstOfSession = new bool[count];
                firstSessionTrueRanges = new List<double>[count];
                sessionTrSum = new double[count];
                sessionTrCount = new int[count];
                sessionOpenObserved = new bool[count];
                dailyAtrs = new List<double>[count];
                currentMedianTicks = new int[count];
                for (int i = 0; i < count; i++)
                {
                    lastProcessedBar[i] = -1;
                    firstSessionTrueRanges[i] = new List<double>();
                    dailyAtrs[i] = new List<double>();
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

            // Only an index advance means a bar finished. The forming bar is never
            // folded into the EMA: under tick-based Calculate modes its high/low are
            // not final until it closes, and a stateful EMA cannot take back a
            // contribution. Each completed bar is folded exactly once, at the first
            // update of its successor, whatever the Calculate mode.
            if (CurrentBars[idx] <= lastProcessedBar[seriesIndex])
                return;

            bool formingIsFirstOfSession = BarsArray[idx].IsFirstBarOfSession;
            bool firstTrackedBar = lastProcessedBar[seriesIndex] < 0;
            if (!firstTrackedBar)
            {
                FoldCompletedBar(seriesIndex, idx, trackedBarFirstOfSession[seriesIndex]);

                // The forming bar opening a new session means the old session's bars
                // are all folded -- its daily mean is final right now, not one bar
                // later when this forming bar itself gets folded. The flag is only
                // trusted on a bar with a predecessor: NinjaTrader also flags the
                // very first loaded bar even when the data starts mid-session, and a
                // partial day's mean would skew the median for up to N sessions, so
                // the session that contains bar 0 is always discarded.
                if (formingIsFirstOfSession)
                    FinalizeSessionDay(seriesIndex);
            }

            // Start tracking the new forming bar; its session flag is consumed when
            // it completes and gets folded on the next advance.
            trackedBarFirstOfSession[seriesIndex] = formingIsFirstOfSession;
            lastProcessedBar[seriesIndex] = CurrentBars[idx];
        }

        /// <summary>
        /// Folds the just-completed bar (one bar behind the forming one) into this
        /// timeframe's ATR. During the first session the day-derived period is still
        /// unknown, so true ranges are parked and replayed the moment the first
        /// session roll reveals it -- folding them early with a stand-in period would
        /// leave a residue the real period never washes out.
        /// </summary>
        private void FoldCompletedBar(int seriesIndex, int idx, bool isFirstOfSession)
        {
            // Bars-per-day census: each session roll, the day just finished says how
            // many bars one day holds for this timeframe.
            if (isFirstOfSession)
            {
                if (barsInCurrentDay[seriesIndex] > 0)
                    barsPerDay[seriesIndex] = barsInCurrentDay[seriesIndex];
                barsInCurrentDay[seriesIndex] = 1;
            }
            else
                barsInCurrentDay[seriesIndex]++;

            double high = Highs[idx][1];
            double low = Lows[idx][1];
            double trueRange;

            bool hasPriorClose = CurrentBars[idx] >= 2;
            if (!hasPriorClose || (IgnoreGaps && isFirstOfSession))
                trueRange = high - low;
            else
            {
                double priorClose = Closes[idx][2];
                trueRange = Math.Max(high - low, Math.Max(Math.Abs(high - priorClose), Math.Abs(low - priorClose)));
            }

            // Median-of-daily-ATRs accumulation. The gate opens in FinalizeSessionDay
            // when a genuine session roll is observed, so a partial session at the
            // left edge of the data never accumulates. This runs before the
            // parked-EMA early return: accumulating bars belong in the daily stat
            // even while the EMA period is still unknown.
            if (sessionOpenObserved[seriesIndex])
            {
                sessionTrSum[seriesIndex] += trueRange;
                sessionTrCount[seriesIndex]++;
            }

            if (barsPerDay[seriesIndex] == 0)
            {
                firstSessionTrueRanges[seriesIndex].Add(trueRange);
                return;
            }

            // ATR period in bars = this timeframe's own day count x bars per day,
            // refreshed on every fold so the smoothing follows the census as it settles.
            int period = Math.Max(1, timeframeDays[seriesIndex] * barsPerDay[seriesIndex]);
            double k = 2.0 / (period + 1);

            if (firstSessionTrueRanges[seriesIndex] != null)
            {
                foreach (double parked in firstSessionTrueRanges[seriesIndex])
                    FoldTrueRange(seriesIndex, parked, k);
                firstSessionTrueRanges[seriesIndex] = null;
            }

            FoldTrueRange(seriesIndex, trueRange, k);

            currentATR[seriesIndex] = emaAtr[seriesIndex];
            currentHalfATRTicks[seriesIndex] = (int)Math.Round((emaAtr[seriesIndex] / 2.0) / TickSize);
        }

        private void FoldTrueRange(int seriesIndex, double trueRange, double k)
        {
            if (!emaSeeded[seriesIndex])
            {
                emaAtr[seriesIndex] = trueRange;
                emaSeeded[seriesIndex] = true;
            }
            else
                emaAtr[seriesIndex] += k * (trueRange - emaAtr[seriesIndex]);
        }

        /// <summary>
        /// Called when the forming bar opens a new session: pushes the finished
        /// session's mean true range into the day window (when one was accumulating)
        /// and opens the accumulation gate for the session now starting -- a roll
        /// observed inside the data proves this session begins at its true start.
        /// </summary>
        private void FinalizeSessionDay(int seriesIndex)
        {
            if (sessionOpenObserved[seriesIndex] && sessionTrCount[seriesIndex] > 0)
                PushDailyAtr(seriesIndex, sessionTrSum[seriesIndex] / sessionTrCount[seriesIndex]);
            sessionOpenObserved[seriesIndex] = true;
            sessionTrSum[seriesIndex] = 0;
            sessionTrCount[seriesIndex] = 0;
        }

        /// <summary>
        /// Adds a finished session's mean true range to this timeframe's day window
        /// (capped at the row's configured day count) and refreshes the median.
        /// </summary>
        private void PushDailyAtr(int seriesIndex, double dayAtr)
        {
            List<double> days = dailyAtrs[seriesIndex];
            days.Add(dayAtr);
            while (days.Count > Math.Max(1, timeframeDays[seriesIndex]))
                days.RemoveAt(0);

            List<double> sorted = new List<double>(days);
            sorted.Sort();
            int mid = sorted.Count / 2;
            double median = sorted.Count % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;

            currentMedianTicks[seriesIndex] = (int)Math.Round((median / 2.0) / TickSize);
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

        // "Ticks" is the live EMA read; "Med Ticks" is the median-of-days read that
        // holds steady through outlier sessions.
        private static readonly float[] ColumnWidths = { 55, 70, 80, 60, 75 };
        private static readonly string[] ColumnHeaders = { "TF", "ATR", "Half ATR", "Ticks", "Med Ticks" };

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
            // table inside the panel instead of clipped or drawn over a neighboring one. The
            // top margin drops the table below the platform's own top-right toolbar icons.
            float tableX = ChartPanel.X + ChartPanel.W - tableWidth - 10;
            float tableY = ChartPanel.Y + TopMarginPixels;

            RectangleF tableRect = new RectangleF(tableX, tableY, tableWidth, tableHeight);
            RenderTarget.FillRectangle(tableRect, tableBgBrush);
            RenderTarget.DrawRectangle(tableRect, borderBrush, 1);

            RectangleF headerRect = new RectangleF(tableX, tableY, tableWidth, headerHeight);
            RenderTarget.FillRectangle(headerRect, headerBgBrush);
            DrawTableRow(tableY, tableX, headerHeight, ColumnHeaders, headerFormat, textBrushWhite, textBrushWhite, textBrushWhite, textBrushWhite, textBrushWhite);

            for (int i = 0; i < rowCount; i++)
            {
                float rowY = tableY + headerHeight + rowHeight * i;
                string[] cells =
                {
                    timeframeMinutes[i] + "m",
                    FormatValue(currentATR[i]),
                    FormatValue(currentATR[i] / 2.0),
                    currentHalfATRTicks[i].ToString(),
                    currentMedianTicks[i].ToString()
                };
                RenderTarget.DrawLine(new SharpDX.Vector2(tableX, rowY), new SharpDX.Vector2(tableX + tableWidth, rowY), borderBrush, 1);
                DrawTableRow(rowY, tableX, rowHeight, cells, textFormat, textBrushWhite, textBrushBlue, textBrushGreen, textBrushGreen, textBrushGreen);
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
