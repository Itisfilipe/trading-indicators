#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    public enum FlowDeltaSource
    {
        BidAsk,
        HeikinAshiVolume,
        StructureVolume
    }

    /// <summary>
    /// Cumulative delta drawn as Heikin-Ashi candles, laid out like the
    /// reference trader's CVD: breakout circles, pivot levels, the previous
    /// candle's high/low dashes and effort-versus-result divergence stripes.
    /// </summary>
    public class FlowCVD : Indicator
    {
        private class PivotLevel
        {
            public int BarIndex;
            public double Value;
            public bool IsHigh;
            public int BrokenAt = -1;
        }

        private readonly List<PivotLevel> pivots = new List<PivotLevel>();
        private readonly List<PivotLevel> unbrokenPivots = new List<PivotLevel>();
        private readonly List<PivotLevel> provisionalBreaks = new List<PivotLevel>();
        private SessionIterator sessionIterator;
        private int activeBar = -1;
        private double cumulativeDelta;
        private TimeSpan resetTimeOfDay;
        private bool hasResetTime;
        private TimeZoneInfo newYorkZone;

        private Series<double> cvdOpen;
        private Series<bool> resetBars;
        private DateTime lastResetMoment = DateTime.MinValue;
        private Series<double> cvdHigh;
        private Series<double> cvdLow;
        private Series<double> cvdClose;
        private Series<double> priceHaOpen;
        private Series<double> priceHaClose;
        private Series<int> structureSign;
        private Series<double> shownOpen;
        private Series<double> shownHigh;
        private Series<double> shownLow;
        private Series<double> shownClose;
        private bool activeBarIsReset;
        private Series<int> divergence;

        private static System.Windows.Media.Brush Rgb(byte red, byte green, byte blue)
        {
            System.Windows.Media.SolidColorBrush brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Cumulative delta as Heikin-Ashi candles with breakout circles, pivot levels, last-candle levels and divergence stripes.";
                Name = "Flow CVD";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                DrawOnPricePanel = false;
                BarsRequiredToPlot = 0;

                ResetEachSession = false;
                ResetAtTime = false;
                ResetTime = "09:30";
                DeltaSource = FlowDeltaSource.HeikinAshiVolume;
                SignEachTrade = false;

                ShowBreakoutCircles = true;
                CircleRadius = 12;
                ColorLookback = 3;
                BullFill = Rgb(0x00, 0xFF, 0x00);
                BullFillOpacity = 0.3;
                BullBorder = Rgb(0x00, 0x66, 0x00);
                BearFill = Rgb(0xFF, 0x00, 0x00);
                BearFillOpacity = 0.3;
                BearBorder = Rgb(0xFF, 0x00, 0x00);

                MatchChartCandleWidth = true;
                FixedCandleWidth = 8;
                WidthScale = 0.7;

                BodyUp = Rgb(0x37, 0xF0, 0x9C);
                BodyDown = Rgb(0x1A, 0x1C, 0x26);
                WickUp = Rgb(0x1A, 0x1C, 0x26);
                WickDown = Rgb(0x1A, 0x1C, 0x26);

                UseLookbackScaling = false;
                ScaleLookback = 300;

                ShowDivergence = true;
                DivergenceLookback = 3;
                MinEffortThreshold = 0.1;
                MaxResultRatio = 0.3;
                BullDivergence = Rgb(0xFF, 0xA5, 0x00);
                BullDivergenceOpacity = 0.15;
                BearDivergence = Rgb(0x93, 0x70, 0xDB);
                BearDivergenceOpacity = 0.15;

                LastLevelsColor = Rgb(0x00, 0x00, 0x00);
                LevelsUpCross = Rgb(0xFF, 0x00, 0x00);
                LevelsDownCross = Rgb(0xFF, 0x00, 0x00);
                CurrentPad = 0.15;

                ShowPivotLevels = true;
                PivotStrength = 1;
                LastLevelsLength = 30;
                PivotUp = Rgb(0x00, 0x4D, 0x00);
                PivotDown = Rgb(0xFF, 0x00, 0x00);
                PivotLineWidth = 2;
                PivotLineOpacity = 0.8;

                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "CVD");
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
                cvdOpen = new Series<double>(this, MaximumBarsLookBack.Infinite);
                resetBars = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                cvdHigh = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvdLow = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvdClose = new Series<double>(this, MaximumBarsLookBack.Infinite);
                priceHaOpen = new Series<double>(this);
                priceHaClose = new Series<double>(this);
                structureSign = new Series<int>(this);
                shownOpen = new Series<double>(this, MaximumBarsLookBack.Infinite);
                shownHigh = new Series<double>(this, MaximumBarsLookBack.Infinite);
                shownLow = new Series<double>(this, MaximumBarsLookBack.Infinite);
                shownClose = new Series<double>(this, MaximumBarsLookBack.Infinite);
                divergence = new Series<int>(this, MaximumBarsLookBack.Infinite);
            }
            else if (State == State.Historical)
            {
                newYorkZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                hasResetTime = ResetAtTime && TimeSpan.TryParse(ResetTime, CultureInfo.InvariantCulture, out resetTimeOfDay);
                if (ResetAtTime && !hasResetTime)
                    Draw.TextFixed(this, "FlowCvdResetTime", "Flow CVD: reset time must be HH:MM", TextPosition.TopLeft);
                if (Calculate == Calculate.OnBarClose && UsesTrades)
                    Draw.TextFixed(this, "FlowCvdCalculate", "Flow CVD: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay && UsesTrades)
                    Draw.TextFixed(this, "FlowCvdTickReplay", "Flow CVD: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last || CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;
            if (State == State.Realtime && !sessionIterator.IsInSession(e.Time, true, true))
                return;
            if (!UsesTrades)
                return;

            StartBarIfNew();
            int side = DeltaSource == FlowDeltaSource.BidAsk ? TradeSide(e) : PriceSign();
            cumulativeDelta += side * e.Volume;

            cvdHigh[0] = Math.Max(cvdHigh[0], cumulativeDelta);
            cvdLow[0] = Math.Min(cvdLow[0], cumulativeDelta);
            cvdClose[0] = cumulativeDelta;
            Value[0] = cumulativeDelta;
            UpdateShownCandle();
        }

        private bool UsesTrades
        {
            get { return DeltaSource == FlowDeltaSource.BidAsk || SignEachTrade; }
        }

        private static int TradeSide(MarketDataEventArgs e)
        {
            return e.Price >= e.Ask ? 1 : e.Price <= e.Bid ? -1 : 0;
        }

        private void StartBarIfNew()
        {
            if (CurrentBar == activeBar)
                return;

            activeBar = CurrentBar;
            activeBarIsReset = IsResetBar(CurrentBar);
            resetBars[0] = activeBarIsReset;
            lock (pivots)
                provisionalBreaks.Clear();
            if (activeBarIsReset)
            {
                cumulativeDelta = 0;
                lock (pivots)
                {
                    foreach (PivotLevel level in unbrokenPivots)
                        level.BrokenAt = CurrentBar - 1;
                    unbrokenPivots.Clear();
                }
            }
            cvdOpen[0] = cumulativeDelta;
            cvdHigh[0] = cumulativeDelta;
            cvdLow[0] = cumulativeDelta;
        }

        private int PriceHeikinAshiSign()
        {
            double haClose = (Open[0] + High[0] + Low[0] + Close[0]) / 4;
            double haOpen = CurrentBar > 0 ? (priceHaOpen[1] + priceHaClose[1]) / 2 : (Open[0] + Close[0]) / 2;
            priceHaOpen[0] = haOpen;
            priceHaClose[0] = haClose;
            return haClose >= haOpen ? 1 : -1;
        }

        private int PriceSign()
        {
            return DeltaSource == FlowDeltaSource.StructureVolume ? PriceStructureSign() : PriceHeikinAshiSign();
        }

        // The sign holds until a price candle closes past the previous candle's high or low.
        private int PriceStructureSign()
        {
            int sign;
            if (CurrentBar == 0)
                sign = Close[0] >= Open[0] ? 1 : -1;
            else if (Close[0] > High[1])
                sign = 1;
            else if (Close[0] < Low[1])
                sign = -1;
            else
                sign = structureSign[1];
            structureSign[0] = sign;
            return sign;
        }

        private void UpdateFromCandle()
        {
            StartBarIfNew();
            double close = cvdOpen[0] + PriceSign() * Volume[0];
            // History sees only the final sign, so live must not keep extremes from earlier ones.
            cvdHigh[0] = Math.Max(cvdOpen[0], close);
            cvdLow[0] = Math.Min(cvdOpen[0], close);
            cvdClose[0] = close;
            Value[0] = close;
            cumulativeDelta = close;
            UpdateShownCandle();
        }

        private void UpdateShownCandle()
        {
            double open = cvdOpen[0];
            double high = cvdHigh[0];
            double low = cvdLow[0];
            double close = cvdClose[0];
            bool continuesPrevious = !activeBarIsReset && HasCvd(CurrentBar - 1);
            double haOpen = continuesPrevious
                ? (shownOpen.GetValueAt(CurrentBar - 1) + shownClose.GetValueAt(CurrentBar - 1)) / 2
                : (open + close) / 2;
            double haClose = (open + high + low + close) / 4;
            shownOpen[0] = haOpen;
            shownClose[0] = haClose;
            shownHigh[0] = Math.Max(high, Math.Max(haOpen, haClose));
            shownLow[0] = Math.Min(low, Math.Min(haOpen, haClose));

            // A pivot line stops at the first candle that trades through it, the forming one included.
            lock (pivots)
            {
                // A whole-candle CVD can retreat within the bar, and history sees only its final shape.
                if (!UsesTrades)
                {
                    foreach (PivotLevel level in provisionalBreaks)
                    {
                        level.BrokenAt = -1;
                        unbrokenPivots.Add(level);
                    }
                    provisionalBreaks.Clear();
                }
                for (int i = unbrokenPivots.Count - 1; i >= 0; i--)
                {
                    PivotLevel level = unbrokenPivots[i];
                    if (level.BarIndex < CurrentBar && (level.IsHigh ? shownHigh[0] > level.Value : shownLow[0] < level.Value))
                    {
                        level.BrokenAt = CurrentBar;
                        unbrokenPivots.RemoveAt(i);
                        if (!UsesTrades)
                            provisionalBreaks.Add(level);
                    }
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // Pivots confirmed by the closed candle go in before this candle's break check.
            if (IsFirstTickOfBar && HasCvd(CurrentBar - 1))
            {
                lock (pivots)
                    AddPivotsAt(CurrentBar - 1 - PivotStrength);
                MarkDivergence(CurrentBar - 1);
            }

            if (DeltaSource != FlowDeltaSource.BidAsk)
            {
                if (SignEachTrade)
                    PriceSign();
                else
                    UpdateFromCandle();
            }
        }

        private bool HasCvd(int barIndex)
        {
            return barIndex >= 0 && shownOpen.IsValidDataPointAt(barIndex) && shownClose.IsValidDataPointAt(barIndex);
        }

        private DateTime NewYorkTime(int barIndex)
        {
            return TimeZoneInfo.ConvertTime(Bars.GetTime(barIndex), Core.Globals.GeneralOptions.TimeZoneInfo, newYorkZone);
        }

        private bool IsResetBar(int barIndex)
        {
            if (barIndex <= 0)
                return false;
            if (ResetEachSession && Bars.IsFirstBarOfSessionByIndex(barIndex))
                return true;
            if (!hasResetTime)
                return false;

            // Called once, on the bar's first tick: a tick, volume or range bar is stamped with
            // that tick then. The first bar stamped past today's reset resets, even when the
            // bar before it straddled the reset time.
            DateTime currentStamp = NewYorkTime(barIndex);
            DateTime resetMoment = currentStamp.Date + resetTimeOfDay;
            // A time bar's stamp is its end, so one ending exactly at the reset is still before it.
            bool pastReset = Bars.BarsType.IsTimeBased ? currentStamp > resetMoment : currentStamp >= resetMoment;
            if (!pastReset || resetMoment == lastResetMoment)
                return false;
            lastResetMoment = resetMoment;
            return true;
        }

        private bool SpansReset(int firstBar, int lastBar)
        {
            for (int barIndex = firstBar; barIndex <= lastBar; barIndex++)
                if (barIndex >= 0 && barIndex <= CurrentBar && resetBars.GetValueAt(barIndex))
                    return true;
            return false;
        }

        private void AddPivotsAt(int pivotBar)
        {
            if (!HasCvd(pivotBar) || SpansReset(pivotBar - PivotStrength + 1, pivotBar + PivotStrength))
                return;

            double high = shownHigh.GetValueAt(pivotBar);
            double low = shownLow.GetValueAt(pivotBar);
            bool isHigh = true;
            bool isLow = true;
            for (int offset = 1; offset <= PivotStrength; offset++)
            {
                if (!HasCvd(pivotBar - offset) || !HasCvd(pivotBar + offset))
                    return;
                // Adjacent candles can share an extreme, so only the left side must be beaten strictly.
                if (high <= shownHigh.GetValueAt(pivotBar - offset) || high < shownHigh.GetValueAt(pivotBar + offset))
                    isHigh = false;
                if (low >= shownLow.GetValueAt(pivotBar - offset) || low > shownLow.GetValueAt(pivotBar + offset))
                    isLow = false;
            }

            if (isHigh)
                AddPivot(new PivotLevel { BarIndex = pivotBar, Value = high, IsHigh = true });
            if (isLow)
                AddPivot(new PivotLevel { BarIndex = pivotBar, Value = low, IsHigh = false });
        }

        private void AddPivot(PivotLevel level)
        {
            pivots.Add(level);
            unbrokenPivots.Add(level);
        }

        // Effort (the CVD's move over the lookback) that price did not follow, both scaled by
        // their largest move of that length in the last Scale Lookback candles.
        private void MarkDivergence(int closedBar)
        {
            int lookback = DivergenceLookback;
            int firstBar = closedBar - ScaleLookback - lookback + 1;
            if (firstBar < 0 || SpansReset(firstBar + 1, closedBar))
                return;

            double maxEffort = 0;
            double maxResult = 0;
            for (int barIndex = firstBar + lookback; barIndex <= closedBar; barIndex++)
            {
                if (!HasCvd(barIndex) || !HasCvd(barIndex - lookback))
                    return;
                maxEffort = Math.Max(maxEffort, Math.Abs(cvdClose.GetValueAt(barIndex) - cvdClose.GetValueAt(barIndex - lookback)));
                maxResult = Math.Max(maxResult, Math.Abs(Bars.GetClose(barIndex) - Bars.GetClose(barIndex - lookback)));
            }
            if (maxEffort <= 0)
                return;

            double effort = cvdClose.GetValueAt(closedBar) - cvdClose.GetValueAt(closedBar - lookback);
            double result = Bars.GetClose(closedBar) - Bars.GetClose(closedBar - lookback);
            double effortScaled = Math.Abs(effort) / maxEffort;
            if (effortScaled < MinEffortThreshold)
                return;
            double progress = maxResult > 0 ? Math.Sign(effort) * result / maxResult : 0;
            if (progress / effortScaled <= MaxResultRatio)
                divergence[1] = effort > 0 ? 1 : -1;
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;
            if (ChartBars == null)
                return;

            int firstBar = UseLookbackScaling ? Math.Max(0, ChartBars.ToIndex - ScaleLookback + 1) : ChartBars.FromIndex;
            for (int barIndex = firstBar; barIndex <= ChartBars.ToIndex; barIndex++)
            {
                if (!HasCvd(barIndex))
                    continue;
                MinValue = Math.Min(MinValue, shownLow.GetValueAt(barIndex));
                MaxValue = Math.Max(MaxValue, shownHigh.GetValueAt(barIndex));
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.Direct2D1.Brush bodyUpBrush = BodyUp.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush bodyDownBrush = BodyDown.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush wickUpBrush = WickUp.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush wickDownBrush = WickDown.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush bullFillBrush = BullFill.ToDxBrush(RenderTarget, (float)BullFillOpacity);
            SharpDX.Direct2D1.Brush bullBorderBrush = BullBorder.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush bearFillBrush = BearFill.ToDxBrush(RenderTarget, (float)BearFillOpacity);
            SharpDX.Direct2D1.Brush bearBorderBrush = BearBorder.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush bullStripeBrush = BullDivergence.ToDxBrush(RenderTarget, (float)BullDivergenceOpacity);
            SharpDX.Direct2D1.Brush bearStripeBrush = BearDivergence.ToDxBrush(RenderTarget, (float)BearDivergenceOpacity);
            SharpDX.Direct2D1.Brush lastLevelBrush = LastLevelsColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush upCrossBrush = LevelsUpCross.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush downCrossBrush = LevelsDownCross.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush pivotUpBrush = PivotUp.ToDxBrush(RenderTarget, (float)PivotLineOpacity);
            SharpDX.Direct2D1.Brush pivotDownBrush = PivotDown.ToDxBrush(RenderTarget, (float)PivotLineOpacity);

            try
            {
                float barDistance = chartControl.Properties.BarDistance;
                float bodyWidth = MatchChartCandleWidth
                    ? Math.Max(1f, (float)(chartControl.GetBarPaintWidth(ChartBars) * WidthScale))
                    : FixedCandleWidth;
                int lastBar = Bars.Count - 1;
                // On bar close the forming candle has no CVD yet; recent marks count from the last one that does.
                int lastCvdBar = HasCvd(lastBar) ? lastBar : lastBar - 1;
                float levelsEndX = chartControl.GetXByBarIndex(ChartBars, lastBar) + (float)(CurrentPad * barDistance);

                if (ShowDivergence)
                    for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
                    {
                        if (!divergence.IsValidDataPointAt(barIndex))
                            continue;
                        float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(x - barDistance / 2, ChartPanel.Y, barDistance, ChartPanel.H),
                            divergence.GetValueAt(barIndex) > 0 ? bullStripeBrush : bearStripeBrush);
                    }

                if (ShowPivotLevels)
                    lock (pivots)
                        foreach (PivotLevel level in pivots)
                        {
                            // INFERRED from his chart: a broken level disappears, and only recent pivots get a line.
                            if (level.BrokenAt >= 0 || level.BarIndex > ChartBars.ToIndex || level.BarIndex < lastCvdBar - LastLevelsLength + 1)
                                continue;
                            float x1 = chartControl.GetXByBarIndex(ChartBars, level.BarIndex);
                            float x2 = levelsEndX;
                            float y = chartScale.GetYByValue(level.Value);
                            RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(x2, y),
                                level.IsHigh ? pivotDownBrush : pivotUpBrush, PivotLineWidth);
                        }

                for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
                {
                    if (!HasCvd(barIndex))
                        continue;

                    float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
                    double open = shownOpen.GetValueAt(barIndex);
                    double close = shownClose.GetValueAt(barIndex);
                    bool rising = close >= open;
                    SharpDX.Direct2D1.Brush wick = rising ? wickUpBrush : wickDownBrush;

                    RenderTarget.DrawLine(new SharpDX.Vector2(x, chartScale.GetYByValue(shownHigh.GetValueAt(barIndex))),
                        new SharpDX.Vector2(x, chartScale.GetYByValue(shownLow.GetValueAt(barIndex))), wick, 1f);

                    float bodyTop = chartScale.GetYByValue(Math.Max(open, close));
                    float bodyBottom = chartScale.GetYByValue(Math.Min(open, close));
                    SharpDX.RectangleF body = new SharpDX.RectangleF(x - bodyWidth / 2, bodyTop, bodyWidth, Math.Max(1f, bodyBottom - bodyTop));
                    RenderTarget.FillRectangle(body, rising ? bodyUpBrush : bodyDownBrush);
                    RenderTarget.DrawRectangle(body, wick, 1f);
                }

                int dashBar = lastCvdBar;
                if (dashBar <= ChartBars.ToIndex && HasCvd(dashBar) && HasCvd(dashBar - 1))
                {
                    double previousHigh = shownHigh.GetValueAt(dashBar - 1);
                    double previousLow = shownLow.GetValueAt(dashBar - 1);
                    float x1 = chartControl.GetXByBarIndex(ChartBars, dashBar - 1);
                    float x2 = levelsEndX;
                    float highY = chartScale.GetYByValue(previousHigh);
                    float lowY = chartScale.GetYByValue(previousLow);
                    RenderTarget.DrawLine(new SharpDX.Vector2(x1, highY), new SharpDX.Vector2(x2, highY),
                        shownHigh.GetValueAt(dashBar) > previousHigh ? upCrossBrush : lastLevelBrush, 1f);
                    RenderTarget.DrawLine(new SharpDX.Vector2(x1, lowY), new SharpDX.Vector2(x2, lowY),
                        shownLow.GetValueAt(dashBar) < previousLow ? downCrossBrush : lastLevelBrush, 1f);
                }

                if (ShowBreakoutCircles)
                    for (int barIndex = Math.Max(Math.Max(1, ChartBars.FromIndex), lastCvdBar - ColorLookback + 1); barIndex <= ChartBars.ToIndex; barIndex++)
                    {
                        if (!HasCvd(barIndex) || !HasCvd(barIndex - 1) || resetBars.GetValueAt(barIndex))
                            continue;
                        double close = shownClose.GetValueAt(barIndex);
                        double previousHigh = shownHigh.GetValueAt(barIndex - 1);
                        double previousLow = shownLow.GetValueAt(barIndex - 1);
                        bool previousRising = shownClose.GetValueAt(barIndex - 1) >= shownOpen.GetValueAt(barIndex - 1);
                        bool bull = !previousRising && close > previousHigh;
                        bool bear = previousRising && close < previousLow;
                        if (!bull && !bear)
                            continue;

                        double level = bull ? previousHigh : previousLow;
                        SharpDX.Direct2D1.Ellipse circle = new SharpDX.Direct2D1.Ellipse(
                            new SharpDX.Vector2(chartControl.GetXByBarIndex(ChartBars, barIndex), chartScale.GetYByValue(level)),
                            CircleRadius, CircleRadius);
                        RenderTarget.FillEllipse(circle, bull ? bullFillBrush : bearFillBrush);
                        RenderTarget.DrawEllipse(circle, bull ? bullBorderBrush : bearBorderBrush, 1f);
                    }
            }
            finally
            {
                bodyUpBrush.Dispose();
                bodyDownBrush.Dispose();
                wickUpBrush.Dispose();
                wickDownBrush.Dispose();
                bullFillBrush.Dispose();
                bullBorderBrush.Dispose();
                bearFillBrush.Dispose();
                bearBorderBrush.Dispose();
                bullStripeBrush.Dispose();
                bearStripeBrush.Dispose();
                lastLevelBrush.Dispose();
                upCrossBrush.Dispose();
                downCrossBrush.Dispose();
                pivotUpBrush.Dispose();
                pivotDownBrush.Dispose();
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Reset each session", Description = "Start the cumulative delta from zero on the first candle of each session. Off by default: the reference CVD keeps accumulating.", Order = 1, GroupName = "1. CVD")]
        public bool ResetEachSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reset at time", Description = "Start the cumulative delta from zero on the first candle after Reset time each day.", Order = 2, GroupName = "1. CVD")]
        public bool ResetAtTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reset time", Description = "HH:MM, New York time. 09:30 is the cash open.", Order = 3, GroupName = "1. CVD")]
        public string ResetTime { get; set; }

        [Display(Name = "Delta source", Description = "HeikinAshiVolume: volume signed by the Heikin-Ashi color of price, as the reference CVD appears to be built; needs no Tick Replay. StructureVolume: volume signed by the last structure break of the price candles (+ after a close above the previous candle's high, - after a close below its low, unchanged otherwise). BidAsk: real aggression from each trade's bid and ask (needs Tick Replay).", Order = 4, GroupName = "1. CVD")]
        public FlowDeltaSource DeltaSource { get; set; }

        [Display(Name = "Sign each trade", Description = "HeikinAshiVolume and StructureVolume. On: each trade's volume takes the price sign at that moment (needs Tick Replay). Off: the candle's whole volume takes its current sign.", Order = 5, GroupName = "1. CVD")]
        public bool SignEachTrade { get; set; }

        [Display(Name = "Show breakout circles", Order = 1, GroupName = "2. Breakout Circles")]
        public bool ShowBreakoutCircles { get; set; }

        [Range(1, 60)]
        [Display(Name = "Circle radius (px)", Order = 2, GroupName = "2. Breakout Circles")]
        public int CircleRadius { get; set; }

        [Range(1, 1000)]
        [Display(Name = "Color lookback (bars)", Description = "Circles show only on the last this many candles.", Order = 9, GroupName = "2. Breakout Circles")]
        public int ColorLookback { get; set; }

        [XmlIgnore]
        [Display(Name = "Bull fill", Order = 3, GroupName = "2. Breakout Circles")]
        public System.Windows.Media.Brush BullFill { get; set; }

        [Browsable(false)]
        public string BullFillSerializable
        {
            get { return Serialize.BrushToString(BullFill); }
            set { BullFill = Serialize.StringToBrush(value); }
        }

        [Range(0, 1)]
        [Display(Name = "Bull fill opacity", Order = 4, GroupName = "2. Breakout Circles")]
        public double BullFillOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Bull border", Order = 5, GroupName = "2. Breakout Circles")]
        public System.Windows.Media.Brush BullBorder { get; set; }

        [Browsable(false)]
        public string BullBorderSerializable
        {
            get { return Serialize.BrushToString(BullBorder); }
            set { BullBorder = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bear fill", Order = 6, GroupName = "2. Breakout Circles")]
        public System.Windows.Media.Brush BearFill { get; set; }

        [Browsable(false)]
        public string BearFillSerializable
        {
            get { return Serialize.BrushToString(BearFill); }
            set { BearFill = Serialize.StringToBrush(value); }
        }

        [Range(0, 1)]
        [Display(Name = "Bear fill opacity", Order = 7, GroupName = "2. Breakout Circles")]
        public double BearFillOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Bear border", Order = 8, GroupName = "2. Breakout Circles")]
        public System.Windows.Media.Brush BearBorder { get; set; }

        [Browsable(false)]
        public string BearBorderSerializable
        {
            get { return Serialize.BrushToString(BearBorder); }
            set { BearBorder = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Match chart candle width", Order = 1, GroupName = "3. Candle Width")]
        public bool MatchChartCandleWidth { get; set; }

        [Range(1, 50)]
        [Display(Name = "Fixed candle width (px)", Order = 2, GroupName = "3. Candle Width")]
        public int FixedCandleWidth { get; set; }

        [Range(0.1, 2)]
        [Display(Name = "Width scale (match mode)", Order = 3, GroupName = "3. Candle Width")]
        public double WidthScale { get; set; }

        [XmlIgnore]
        [Display(Name = "Body up", Order = 1, GroupName = "4. Colors")]
        public System.Windows.Media.Brush BodyUp { get; set; }

        [Browsable(false)]
        public string BodyUpSerializable
        {
            get { return Serialize.BrushToString(BodyUp); }
            set { BodyUp = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Body down", Order = 2, GroupName = "4. Colors")]
        public System.Windows.Media.Brush BodyDown { get; set; }

        [Browsable(false)]
        public string BodyDownSerializable
        {
            get { return Serialize.BrushToString(BodyDown); }
            set { BodyDown = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Wick up", Order = 3, GroupName = "4. Colors")]
        public System.Windows.Media.Brush WickUp { get; set; }

        [Browsable(false)]
        public string WickUpSerializable
        {
            get { return Serialize.BrushToString(WickUp); }
            set { WickUp = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Wick down", Order = 4, GroupName = "4. Colors")]
        public System.Windows.Media.Brush WickDown { get; set; }

        [Browsable(false)]
        public string WickDownSerializable
        {
            get { return Serialize.BrushToString(WickDown); }
            set { WickDown = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Use lookback scaling", Description = "Scale the panel to the CVD range of the last Scale lookback candles, not only the visible ones.", Order = 1, GroupName = "5. Display")]
        public bool UseLookbackScaling { get; set; }

        [Range(10, 5000)]
        [Display(Name = "Scale lookback (bars)", Description = "Candles used for the panel scale and to normalize divergence effort and result.", Order = 2, GroupName = "5. Display")]
        public int ScaleLookback { get; set; }

        [Display(Name = "Show divergence", Order = 1, GroupName = "6. Divergence")]
        public bool ShowDivergence { get; set; }

        [Range(1, 50)]
        [Display(Name = "Divergence lookback (bars)", Order = 2, GroupName = "6. Divergence")]
        public int DivergenceLookback { get; set; }

        [Range(0, 1)]
        [Display(Name = "Min effort threshold (normalized)", Order = 3, GroupName = "6. Divergence")]
        public double MinEffortThreshold { get; set; }

        [Range(-1, 1)]
        [Display(Name = "Max result ratio", Order = 4, GroupName = "6. Divergence")]
        public double MaxResultRatio { get; set; }

        [XmlIgnore]
        [Display(Name = "Bull divergence", Description = "Buying flow while price does not rise.", Order = 5, GroupName = "6. Divergence")]
        public System.Windows.Media.Brush BullDivergence { get; set; }

        [Browsable(false)]
        public string BullDivergenceSerializable
        {
            get { return Serialize.BrushToString(BullDivergence); }
            set { BullDivergence = Serialize.StringToBrush(value); }
        }

        [Range(0, 1)]
        [Display(Name = "Bull divergence opacity", Order = 6, GroupName = "6. Divergence")]
        public double BullDivergenceOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Bear divergence", Description = "Selling flow while price does not fall.", Order = 7, GroupName = "6. Divergence")]
        public System.Windows.Media.Brush BearDivergence { get; set; }

        [Browsable(false)]
        public string BearDivergenceSerializable
        {
            get { return Serialize.BrushToString(BearDivergence); }
            set { BearDivergence = Serialize.StringToBrush(value); }
        }

        [Range(0, 1)]
        [Display(Name = "Bear divergence opacity", Order = 8, GroupName = "6. Divergence")]
        public double BearDivergenceOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Last levels color", Description = "The previous candle's high and low, while the forming candle has not crossed them.", Order = 1, GroupName = "7. Levels")]
        public System.Windows.Media.Brush LastLevelsColor { get; set; }

        [Browsable(false)]
        public string LastLevelsColorSerializable
        {
            get { return Serialize.BrushToString(LastLevelsColor); }
            set { LastLevelsColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Levels up-cross", Order = 2, GroupName = "7. Levels")]
        public System.Windows.Media.Brush LevelsUpCross { get; set; }

        [Browsable(false)]
        public string LevelsUpCrossSerializable
        {
            get { return Serialize.BrushToString(LevelsUpCross); }
            set { LevelsUpCross = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Levels down-cross", Order = 3, GroupName = "7. Levels")]
        public System.Windows.Media.Brush LevelsDownCross { get; set; }

        [Browsable(false)]
        public string LevelsDownCrossSerializable
        {
            get { return Serialize.BrushToString(LevelsDownCross); }
            set { LevelsDownCross = Serialize.StringToBrush(value); }
        }

        [Range(0, 5)]
        [Display(Name = "Current pad (bar fraction)", Description = "How far past the current candle the levels reach.", Order = 4, GroupName = "7. Levels")]
        public double CurrentPad { get; set; }

        [Display(Name = "Show pivot levels", Order = 1, GroupName = "8. Pivot Levels")]
        public bool ShowPivotLevels { get; set; }

        [Range(1, 20), NinjaScriptProperty]
        [Display(Name = "Pivot strength (bars each side)", Order = 2, GroupName = "8. Pivot Levels")]
        public int PivotStrength { get; set; }

        [Range(1, 5000)]
        [Display(Name = "Last levels length (bars)", Description = "Only pivots from the last this many candles get a line; a broken line disappears.", Order = 9, GroupName = "8. Pivot Levels")]
        public int LastLevelsLength { get; set; }

        [XmlIgnore]
        [Display(Name = "Pivot up", Description = "Line from a CVD low.", Order = 3, GroupName = "8. Pivot Levels")]
        public System.Windows.Media.Brush PivotUp { get; set; }

        [Browsable(false)]
        public string PivotUpSerializable
        {
            get { return Serialize.BrushToString(PivotUp); }
            set { PivotUp = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Pivot down", Description = "Line from a CVD high.", Order = 4, GroupName = "8. Pivot Levels")]
        public System.Windows.Media.Brush PivotDown { get; set; }

        [Browsable(false)]
        public string PivotDownSerializable
        {
            get { return Serialize.BrushToString(PivotDown); }
            set { PivotDown = Serialize.StringToBrush(value); }
        }

        [Range(1, 10)]
        [Display(Name = "Pivot line width", Order = 5, GroupName = "8. Pivot Levels")]
        public int PivotLineWidth { get; set; }

        [Range(0, 1)]
        [Display(Name = "Pivot line opacity", Order = 6, GroupName = "8. Pivot Levels")]
        public double PivotLineOpacity { get; set; }

        #endregion
    }
}
