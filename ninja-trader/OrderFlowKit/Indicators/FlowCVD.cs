#region Using declarations
using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Cumulative delta drawn as candles, with its swing pivots, the pivot
    /// levels until the delta closes through them, and absorption bands where
    /// an outsized delta failed to move price its way.
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
        private int activeBar = -1;
        private double cumulativeDelta;

        private Series<double> cvdOpen;
        private Series<double> cvdHigh;
        private Series<double> cvdLow;
        private Series<double> cvdClose;
        private Series<double> absorption;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Cumulative delta as candles with pivot levels and absorption bands. Needs Tick Replay for history.";
                Name = "Flow CVD";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                DrawOnPricePanel = false;
                BarsRequiredToPlot = 0;

                ResetEachSession = false;
                PivotStrength = 2;
                AbsorptionLookback = 60;
                AbsorptionZScore = 1.5;
                BandOpacity = 25;

                UpColor = Brushes.LimeGreen;
                DownColor = Brushes.LightCoral;
                PivotHighColor = Brushes.Red;
                PivotLowColor = Brushes.Green;
                BuyAbsorptionColor = Brushes.RoyalBlue;
                SellAbsorptionColor = Brushes.Red;

                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "CVD");
            }
            else if (State == State.DataLoaded)
            {
                cvdOpen = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvdHigh = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvdLow = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvdClose = new Series<double>(this, MaximumBarsLookBack.Infinite);
                absorption = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
            else if (State == State.Historical)
            {
                if (Calculate == Calculate.OnBarClose)
                    Draw.TextFixed(this, "FlowCvdCalculate", "Flow CVD: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay)
                    Draw.TextFixed(this, "FlowCvdTickReplay", "Flow CVD: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last || CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;

            if (CurrentBar != activeBar)
            {
                activeBar = CurrentBar;
                if (ResetEachSession && Bars.IsFirstBarOfSessionByIndex(CurrentBar))
                {
                    cumulativeDelta = 0;
                    lock (pivots)
                        foreach (PivotLevel level in pivots)
                            if (level.BrokenAt < 0)
                                level.BrokenAt = CurrentBar - 1;
                }
                cvdOpen[0] = cumulativeDelta;
                cvdHigh[0] = cumulativeDelta;
                cvdLow[0] = cumulativeDelta;
            }

            if (e.Price >= e.Ask)
                cumulativeDelta += e.Volume;
            else if (e.Price <= e.Bid)
                cumulativeDelta -= e.Volume;

            cvdHigh[0] = Math.Max(cvdHigh[0], cumulativeDelta);
            cvdLow[0] = Math.Min(cvdLow[0], cumulativeDelta);
            cvdClose[0] = cumulativeDelta;
            Value[0] = cumulativeDelta;
        }

        protected override void OnBarUpdate()
        {
            if (!IsFirstTickOfBar || CurrentBar < 1)
                return;

            int closedBar = CurrentBar - 1;
            if (!HasCvd(closedBar))
                return;

            lock (pivots)
            {
                double close = cvdClose.GetValueAt(closedBar);
                foreach (PivotLevel level in pivots)
                    if (level.BrokenAt < 0 && (level.IsHigh ? close > level.Value : close < level.Value))
                        level.BrokenAt = closedBar;

                AddPivotsAt(closedBar - PivotStrength);
            }

            MarkAbsorption(closedBar);
        }

        private bool HasCvd(int barIndex)
        {
            return barIndex >= 0 && cvdOpen.IsValidDataPointAt(barIndex) && cvdClose.IsValidDataPointAt(barIndex);
        }

        private double BarDelta(int barIndex)
        {
            return cvdClose.GetValueAt(barIndex) - cvdOpen.GetValueAt(barIndex);
        }

        private bool SpansSessionReset(int pivotBar)
        {
            if (!ResetEachSession)
                return false;
            for (int barIndex = pivotBar - PivotStrength + 1; barIndex <= pivotBar + PivotStrength; barIndex++)
                if (barIndex >= 0 && Bars.IsFirstBarOfSessionByIndex(barIndex))
                    return true;
            return false;
        }

        private void AddPivotsAt(int pivotBar)
        {
            if (!HasCvd(pivotBar) || SpansSessionReset(pivotBar))
                return;

            double high = cvdHigh.GetValueAt(pivotBar);
            double low = cvdLow.GetValueAt(pivotBar);
            bool isHigh = true;
            bool isLow = true;
            for (int offset = 1; offset <= PivotStrength; offset++)
            {
                if (!HasCvd(pivotBar - offset) || !HasCvd(pivotBar + offset))
                    return;
                if (high <= cvdHigh.GetValueAt(pivotBar - offset) || high < cvdHigh.GetValueAt(pivotBar + offset))
                    isHigh = false;
                if (low >= cvdLow.GetValueAt(pivotBar - offset) || low > cvdLow.GetValueAt(pivotBar + offset))
                    isLow = false;
            }

            if (isHigh)
                pivots.Add(new PivotLevel { BarIndex = pivotBar, Value = high, IsHigh = true });
            if (isLow)
                pivots.Add(new PivotLevel { BarIndex = pivotBar, Value = low, IsHigh = false });
        }

        private void MarkAbsorption(int closedBar)
        {
            int firstBar = closedBar - AbsorptionLookback + 1;
            if (firstBar < 0)
                return;

            double sum = 0;
            double sumOfSquares = 0;
            for (int barIndex = firstBar; barIndex <= closedBar; barIndex++)
            {
                if (!HasCvd(barIndex))
                    return;
                double delta = BarDelta(barIndex);
                sum += delta;
                sumOfSquares += delta * delta;
            }

            double mean = sum / AbsorptionLookback;
            double variance = sumOfSquares / AbsorptionLookback - mean * mean;
            if (variance <= 0)
                return;

            double zScore = (BarDelta(closedBar) - mean) / Math.Sqrt(variance);
            double open = Bars.GetOpen(closedBar);
            double close = Bars.GetClose(closedBar);
            if (zScore <= -AbsorptionZScore && close >= open)
                absorption[1] = 1;
            else if (zScore >= AbsorptionZScore && close <= open)
                absorption[1] = -1;
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;
            if (ChartBars == null)
                return;

            for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
            {
                if (!HasCvd(barIndex))
                    continue;
                MinValue = Math.Min(MinValue, cvdLow.GetValueAt(barIndex));
                MaxValue = Math.Max(MaxValue, cvdHigh.GetValueAt(barIndex));
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.Direct2D1.Brush upBrush = UpColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush downBrush = DownColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush highBrush = PivotHighColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush lowBrush = PivotLowColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush highFill = PivotHighColor.ToDxBrush(RenderTarget, 0.35f);
            SharpDX.Direct2D1.Brush lowFill = PivotLowColor.ToDxBrush(RenderTarget, 0.35f);
            SharpDX.Direct2D1.Brush buyBand = BuyAbsorptionColor.ToDxBrush(RenderTarget, BandOpacity / 100f);
            SharpDX.Direct2D1.Brush sellBand = SellAbsorptionColor.ToDxBrush(RenderTarget, BandOpacity / 100f);

            try
            {
                float barWidth = Math.Max(1f, (float)chartControl.GetBarPaintWidth(ChartBars));
                float panelTop = ChartPanel.Y;
                float panelRight = ChartPanel.X + ChartPanel.W;
                float bandWidth = barWidth + 4;
                float pivotRadius = Math.Max(5f, barWidth);

                for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
                {
                    if (!absorption.IsValidDataPointAt(barIndex))
                        continue;
                    float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x - bandWidth / 2, panelTop, bandWidth, ChartPanel.H),
                        absorption.GetValueAt(barIndex) > 0 ? buyBand : sellBand);
                }

                for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
                {
                    if (!HasCvd(barIndex))
                        continue;

                    float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
                    double open = cvdOpen.GetValueAt(barIndex);
                    double close = cvdClose.GetValueAt(barIndex);
                    SharpDX.Direct2D1.Brush brush = close >= open ? upBrush : downBrush;

                    RenderTarget.DrawLine(new SharpDX.Vector2(x, chartScale.GetYByValue(cvdHigh.GetValueAt(barIndex))),
                        new SharpDX.Vector2(x, chartScale.GetYByValue(cvdLow.GetValueAt(barIndex))), brush, 1f);

                    float bodyTop = chartScale.GetYByValue(Math.Max(open, close));
                    float bodyBottom = chartScale.GetYByValue(Math.Min(open, close));
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x - barWidth / 2, bodyTop, barWidth, Math.Max(1f, bodyBottom - bodyTop)), brush);
                }

                lock (pivots)
                    foreach (PivotLevel level in pivots)
                    {
                        if (level.BarIndex > ChartBars.ToIndex || (level.BrokenAt >= 0 && level.BrokenAt < ChartBars.FromIndex))
                            continue;

                        float x1 = chartControl.GetXByBarIndex(ChartBars, level.BarIndex);
                        float x2 = level.BrokenAt >= 0 ? chartControl.GetXByBarIndex(ChartBars, level.BrokenAt) : panelRight;
                        float y = chartScale.GetYByValue(level.Value);
                        SharpDX.Direct2D1.Brush lineBrush = level.IsHigh ? highBrush : lowBrush;
                        RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(x2, y), lineBrush, 1.5f);

                        SharpDX.Direct2D1.Ellipse circle = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x1, y), pivotRadius, pivotRadius);
                        RenderTarget.FillEllipse(circle, level.IsHigh ? highFill : lowFill);
                        RenderTarget.DrawEllipse(circle, lineBrush, 1f);
                    }
            }
            finally
            {
                upBrush.Dispose();
                downBrush.Dispose();
                highBrush.Dispose();
                lowBrush.Dispose();
                highFill.Dispose();
                lowFill.Dispose();
                buyBand.Dispose();
                sellBand.Dispose();
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Reset each session", Description = "Start the cumulative delta from zero on the first candle of each session.", Order = 1, GroupName = "1. CVD")]
        public bool ResetEachSession { get; set; }

        [Range(1, 20), NinjaScriptProperty]
        [Display(Name = "Pivot strength", Description = "Candles on each side a CVD high or low must beat to become a pivot.", Order = 2, GroupName = "1. CVD")]
        public int PivotStrength { get; set; }

        [Range(5, 500), NinjaScriptProperty]
        [Display(Name = "Absorption lookback", Description = "Candles used to measure how large a candle's delta is.", Order = 3, GroupName = "1. CVD")]
        public int AbsorptionLookback { get; set; }

        [Range(0.5, 10), NinjaScriptProperty]
        [Display(Name = "Absorption z-score", Description = "How many deviations a candle's delta must reach, against a candle that closes the other way, to mark absorption.", Order = 4, GroupName = "1. CVD")]
        public double AbsorptionZScore { get; set; }

        [Range(0, 100)]
        [Display(Name = "Band opacity (%)", Order = 5, GroupName = "1. CVD")]
        public int BandOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Rising candle", Order = 1, GroupName = "2. Colors")]
        public System.Windows.Media.Brush UpColor { get; set; }

        [Browsable(false)]
        public string UpColorSerializable
        {
            get { return Serialize.BrushToString(UpColor); }
            set { UpColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Falling candle", Order = 2, GroupName = "2. Colors")]
        public System.Windows.Media.Brush DownColor { get; set; }

        [Browsable(false)]
        public string DownColorSerializable
        {
            get { return Serialize.BrushToString(DownColor); }
            set { DownColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Pivot high", Order = 3, GroupName = "2. Colors")]
        public System.Windows.Media.Brush PivotHighColor { get; set; }

        [Browsable(false)]
        public string PivotHighColorSerializable
        {
            get { return Serialize.BrushToString(PivotHighColor); }
            set { PivotHighColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Pivot low", Order = 4, GroupName = "2. Colors")]
        public System.Windows.Media.Brush PivotLowColor { get; set; }

        [Browsable(false)]
        public string PivotLowColorSerializable
        {
            get { return Serialize.BrushToString(PivotLowColor); }
            set { PivotLowColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Buyers absorbing (selling failed)", Order = 5, GroupName = "2. Colors")]
        public System.Windows.Media.Brush BuyAbsorptionColor { get; set; }

        [Browsable(false)]
        public string BuyAbsorptionColorSerializable
        {
            get { return Serialize.BrushToString(BuyAbsorptionColor); }
            set { BuyAbsorptionColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Sellers absorbing (buying failed)", Order = 6, GroupName = "2. Colors")]
        public System.Windows.Media.Brush SellAbsorptionColor { get; set; }

        [Browsable(false)]
        public string SellAbsorptionColorSerializable
        {
            get { return Serialize.BrushToString(SellAbsorptionColor); }
            set { SellAbsorptionColor = Serialize.StringToBrush(value); }
        }

        #endregion
    }
}
