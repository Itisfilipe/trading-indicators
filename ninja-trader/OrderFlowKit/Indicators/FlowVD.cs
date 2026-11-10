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
    /// Volume delta per candle: contracts bought at the ask minus contracts sold
    /// at the bid, updated tick by tick while the candle forms.
    /// </summary>
    public class FlowVD : Indicator
    {
        private SessionIterator sessionIterator;
        private int activeBar = -1;
        private double delta;
        private double windowMean;
        private double windowDeviation;
        private Series<double> rawDelta;

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
                Description = "Volume delta per candle (ask aggression minus bid aggression). Needs Tick Replay for history.";
                Name = "Flow VD";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                DrawOnPricePanel = false;
                BarsRequiredToPlot = 0;

                AbsoluteBars = true;
                ShowZScore = false;
                ZScoreLookback = 60;
                BuyColor = Brushes.SeaGreen;
                SellColor = Brushes.Crimson;

                AddPlot(new Stroke(BuyColor, 2), PlotStyle.Bar, "Delta");
                Plots[0].AutoWidth = true;
                AddLine(Rgb(0xD1, 0xD5, 0xDB), 0, "Zero");
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
                rawDelta = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
            else if (State == State.Historical)
            {
                if (Calculate == Calculate.OnBarClose)
                    Draw.TextFixed(this, "FlowVdCalculate", "Flow VD: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay)
                    Draw.TextFixed(this, "FlowVdTickReplay", "Flow VD: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar != activeBar)
            {
                Value[0] = 0;
                rawDelta[0] = 0;
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last || CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;
            if (State == State.Realtime && !sessionIterator.IsInSession(Core.Globals.Now, true, true))
                return;

            if (CurrentBar != activeBar)
            {
                activeBar = CurrentBar;
                delta = 0;
                if (ShowZScore)
                    MeasurePreviousDeltas();
            }

            if (e.Price >= e.Ask)
                delta += e.Volume;
            else if (e.Price <= e.Bid)
                delta -= e.Volume;

            rawDelta[0] = delta;
            double shown = delta;
            if (ShowZScore)
                shown = windowDeviation > 0 ? (delta - windowMean) / windowDeviation : 0;

            Value[0] = AbsoluteBars ? Math.Abs(shown) : shown;
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
