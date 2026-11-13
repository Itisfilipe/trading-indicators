#region Using declarations
using System;
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
    /// Volume delta per candle, either each candle's whole volume signed by a
    /// Heikin-Ashi candle of price or bid/ask aggression from each trade.
    /// </summary>
    public class FlowVD : Indicator
    {
        private SessionIterator sessionIterator;
        private int activeBar = -1;
        private double delta;
        private double windowMean;
        private double windowDeviation;
        private Series<double> rawDelta;
        private Series<double> priceHaOpen;
        private Series<double> priceHaClose;
        private Series<int> structureSign;
        private Series<double> smoothedSize;
        private double previousSmoothedSize = double.NaN;

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
                Description = "Volume delta per candle: whole volume signed by Heikin-Ashi (default) or bid/ask aggression (needs Tick Replay for history).";
                Name = "Flow VD";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                DrawOnPricePanel = false;
                BarsRequiredToPlot = 0;

                AbsoluteBars = true;
                ShowZScore = false;
                ZScoreLookback = 60;
                SmoothingPeriod = 0;
                DeltaSource = FlowDeltaSource.HeikinAshiVolume;
                BuyColor = Rgb(0x37, 0xF0, 0x9C);
                SellColor = Rgb(0x1A, 0x1C, 0x26);

                AddPlot(new Stroke(BuyColor, 2), PlotStyle.Bar, "Delta");
                Plots[0].AutoWidth = true;
                AddLine(Rgb(0xD1, 0xD5, 0xDB), 0, "Zero");
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
                rawDelta = new Series<double>(this, MaximumBarsLookBack.Infinite);
                priceHaOpen = new Series<double>(this);
                priceHaClose = new Series<double>(this);
                structureSign = new Series<int>(this);
                smoothedSize = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
            else if (State == State.Historical)
            {
                if (Calculate == Calculate.OnBarClose && UsesTrades)
                    Draw.TextFixed(this, "FlowVdCalculate", "Flow VD: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay && UsesTrades)
                    Draw.TextFixed(this, "FlowVdTickReplay", "Flow VD: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar != activeBar)
            {
                Value[0] = 0;
            }

            if (!UsesTrades)
            {
                StartBarIfNew();
                delta = DeltaSource == FlowDeltaSource.StructureVolume ? PriceStructureSign() * Volume[0] : HeikinAshiVolumeDelta();
                ShowDelta();
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
            delta += TradeSide(e) * e.Volume;
            ShowDelta();
        }

        private bool UsesTrades
        {
            get { return DeltaSource == FlowDeltaSource.BidAsk; }
        }

        private static int TradeSide(MarketDataEventArgs e)
        {
            return e.Price >= e.Ask ? 1 : e.Price <= e.Bid ? -1 : 0;
        }

        private double HeikinAshiVolumeDelta()
        {
            double haClose = (Open[0] + High[0] + Low[0] + Close[0]) / 4;
            double haOpen = CurrentBar > 0 ? (priceHaOpen[1] + priceHaClose[1]) / 2 : (Open[0] + Close[0]) / 2;
            priceHaOpen[0] = haOpen;
            priceHaClose[0] = haClose;
            return haClose >= haOpen ? Volume[0] : -Volume[0];
        }

        // The sign holds until a price candle closes past the previous candle's high or low.
        private int PriceStructureSign()
        {
            int sign;
            if (CurrentBar == 0)
                sign = Close[0] >= Open[0] ? 1 : -1;
            else
                sign = Close[0] > High[1] ? 1 : Close[0] < Low[1] ? -1 : structureSign[1];
            structureSign[0] = sign;
            return sign;
        }

        private void StartBarIfNew()
        {
            if (CurrentBar == activeBar)
                return;

            activeBar = CurrentBar;
            delta = 0;
            previousSmoothedSize = CurrentBar > 0 && smoothedSize.IsValidDataPointAt(CurrentBar - 1)
                ? smoothedSize.GetValueAt(CurrentBar - 1)
                : double.NaN;
            if (ShowZScore)
                MeasurePreviousDeltas();
        }

        private void ShowDelta()
        {
            rawDelta[0] = delta;
            double shown = delta;
            if (ShowZScore)
                shown = windowDeviation > 0 ? (delta - windowMean) / windowDeviation : 0;

            double size = Math.Abs(shown);
            if (SmoothingPeriod > 0 && !double.IsNaN(previousSmoothedSize))
                size = previousSmoothedSize + 2.0 / (SmoothingPeriod + 1) * (size - previousSmoothedSize);
            smoothedSize[0] = size;

            Value[0] = AbsoluteBars ? size : Math.Sign(shown) * size;
            PlotBrushes[0][0] = shown >= 0 ? BuyColor : SellColor;
        }

        private void MeasurePreviousDeltas()
        {
            windowDeviation = 0;
            if (CurrentBar < ZScoreLookback)
                return;

            double sum = 0;
            double sumOfSquares = 0;
            for (int barsAgo = 1; barsAgo <= ZScoreLookback; barsAgo++)
            {
                if (!rawDelta.IsValidDataPoint(barsAgo))
                    return;
                sum += rawDelta[barsAgo];
                sumOfSquares += rawDelta[barsAgo] * rawDelta[barsAgo];
            }

            windowMean = sum / ZScoreLookback;
            double variance = sumOfSquares / ZScoreLookback - windowMean * windowMean;
            if (variance > 0)
                windowDeviation = Math.Sqrt(variance);
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Bars from zero up", Description = "On: every bar grows up from zero and the color tells the side. Off: selling bars grow down.", Order = 1, GroupName = "1. Display")]
        public bool AbsoluteBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show as z-score", Description = "Plot how many deviations the candle's delta sits from the average of the previous candles, so different timeframes and sessions read on one scale.", Order = 2, GroupName = "1. Display")]
        public bool ShowZScore { get; set; }

        [Range(5, 250), NinjaScriptProperty]
        [Display(Name = "Z-score lookback", Description = "Previous candles the z-score is measured against.", Order = 3, GroupName = "1. Display")]
        public int ZScoreLookback { get; set; }

        [Range(0, 50), NinjaScriptProperty]
        [Display(Name = "Smoothing period", Description = "Bar height is an EMA of this many candles' delta size; the color still follows each candle's own delta. 0 = raw delta.", Order = 4, GroupName = "1. Display")]
        public int SmoothingPeriod { get; set; }

        [Display(Name = "Delta source", Description = "BidAsk: real aggression from each trade's bid and ask (needs Tick Replay). HeikinAshiVolume: each candle's whole volume, positive while a Heikin-Ashi candle built from price is rising and negative while it falls; the reference trader's 1-minute VD and CVD are built this way. StructureVolume: the whole volume signed by the last structure break of the price candles (+ after a close above the previous candle's high, - after a close below its low, unchanged otherwise). Both follow price and need no Tick Replay.", Order = 5, GroupName = "1. Display")]
        public FlowDeltaSource DeltaSource { get; set; }

        [XmlIgnore]
        [Display(Name = "Buyer color", Order = 1, GroupName = "2. Colors")]
        public System.Windows.Media.Brush BuyColor { get; set; }

        [Browsable(false)]
        public string BuyColorSerializable
        {
            get { return Serialize.BrushToString(BuyColor); }
            set { BuyColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Seller color", Order = 2, GroupName = "2. Colors")]
        public System.Windows.Media.Brush SellColor { get; set; }

        [Browsable(false)]
        public string SellColorSerializable
        {
            get { return Serialize.BrushToString(SellColor); }
            set { SellColor = Serialize.StringToBrush(value); }
        }

        #endregion
    }
}
