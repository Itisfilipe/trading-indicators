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
        private int activeBar = -1;
        private double delta;

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
                BuyColor = Brushes.Green;
                SellColor = Brushes.Red;

                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Bar, "Delta");
                Plots[0].AutoWidth = true;
                AddLine(Brushes.DarkGray, 0, "Zero");
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
                Value[0] = 0;
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last || CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;

            if (CurrentBar != activeBar)
            {
                activeBar = CurrentBar;
                delta = 0;
            }

            if (e.Price >= e.Ask)
                delta += e.Volume;
            else if (e.Price <= e.Bid)
                delta -= e.Volume;

            Value[0] = AbsoluteBars ? Math.Abs(delta) : delta;
            PlotBrushes[0][0] = delta >= 0 ? BuyColor : SellColor;
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Bars from zero up", Description = "On: every bar grows up from zero and the color tells the side. Off: selling bars grow down.", Order = 1, GroupName = "1. Display")]
        public bool AbsoluteBars { get; set; }

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
