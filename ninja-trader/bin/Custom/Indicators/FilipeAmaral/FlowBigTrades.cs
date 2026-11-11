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
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    public enum FlowBigTradesFilter { Manual, Automatic }

    public enum FlowBigTradesIntensity { Low, Medium, Strong }

    /// <summary>
    /// Large aggressive market orders as bubbles. The exchange reports one big
    /// market order as many fills; consecutive fills on the same side arriving
    /// within the aggregation window are put back together into one order, drawn
    /// at its volume-weighted price and sized by its lots.
    /// </summary>
    public class FlowBigTrades : Indicator
    {
        private class BigTrade
        {
            public int BarIndex;
            public double Price;
            public double Lots;
            public bool IsBuy;
        }

        private const int HistogramCap = 10000;
        private const int StoredFloor = 5;
        private const int MinimumOrdersForAutomatic = 500;

        private readonly List<BigTrade> trades = new List<BigTrade>();
        private readonly long[] sizeHistogram = new long[HistogramCap + 1];
        private long orderCount;
        private SessionIterator sessionIterator;

        private bool hasOrder;
        private bool orderIsBuy;
        private int orderBar;
        private DateTime orderStartTime;
        private double orderLots;
        private double orderNotional;

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
                Description = "Large aggressive market orders as bubbles, rebuilt from their fills. Needs Tick Replay for history.";
                Name = "Flow Big Trades";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                FilterMode = FlowBigTradesFilter.Manual;
                MinimumLots = 30;
                MaximumLots = 0;
                Intensity = FlowBigTradesIntensity.Medium;
                AggregationMilliseconds = 5;
                MinimumSize = 18;
                MaximumSize = 50;
                FillOpacity = 50;
                HollowFill = false;
                ShowValue = true;

                BuyColor = Brushes.DodgerBlue;
                SellColor = Brushes.Crimson;
                TextColor = Rgb(0x37, 0x41, 0x51);
                LabelFont = new SimpleFont("Arial", 9) { Bold = true };
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
            }
            else if (State == State.Realtime)
            {
                FlushOrder();
            }
            else if (State == State.Historical)
            {
                if (Calculate == Calculate.OnBarClose)
                    Draw.TextFixed(this, "FlowBigTradesCalculate", "Flow Big Trades: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay)
                    Draw.TextFixed(this, "FlowBigTradesTickReplay", "Flow Big Trades: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
            }
        }

        protected override void OnBarUpdate()
        {
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;
            // Bid and ask updates arrive constantly, so a finished order shows without waiting for the next trade.
            if (hasOrder && (e.Time - orderStartTime).TotalMilliseconds > AggregationMilliseconds)
                FlushOrder();
            if (e.MarketDataType != MarketDataType.Last)
                return;
            if (State == State.Realtime && !sessionIterator.IsInSession(e.Time, true, true))
                return;

            bool isBuy = e.Price >= e.Ask;
            bool isSell = !isBuy && e.Price <= e.Bid;
            if (!isBuy && !isSell)
                return;

            bool continuesOrder = hasOrder
                && orderIsBuy == isBuy
                && (e.Time - orderStartTime).TotalMilliseconds <= AggregationMilliseconds;
            if (!continuesOrder)
            {
                FlushOrder();
                hasOrder = true;
                orderIsBuy = isBuy;
                orderBar = CurrentBar;
                orderStartTime = e.Time;
                orderLots = 0;
                orderNotional = 0;
            }
            orderLots += e.Volume;
            orderNotional += e.Price * e.Volume;
        }

        private void FlushOrder()
        {
            if (!hasOrder)
                return;
            hasOrder = false;

            lock (trades)
            {
                sizeHistogram[(int)Math.Min(HistogramCap, orderLots)]++;
                orderCount++;
                // Manual never draws below Minimum lots; Automatic may lower its threshold to the floor.
                int storedFloor = FilterMode == FlowBigTradesFilter.Manual ? MinimumLots : Math.Min(StoredFloor, MinimumLots);
                if (orderLots < storedFloor)
                    return;
                trades.Add(new BigTrade
                {
                    BarIndex = orderBar,
                    Price = Instrument.MasterInstrument.RoundToTickSize(orderNotional / orderLots),
                    Lots = orderLots,
                    IsBuy = orderIsBuy,
                });
            }
        }

        // Smallest order size in the top share of all orders seen, by intensity.
        private double AutomaticThreshold()
        {
            if (orderCount < MinimumOrdersForAutomatic)
                return MinimumLots;
            double topShare = Intensity == FlowBigTradesIntensity.Low ? 0.01 : Intensity == FlowBigTradesIntensity.Medium ? 0.005 : 0.001;
            long wanted = (long)Math.Ceiling(orderCount * topShare);
            long counted = 0;
            for (int size = HistogramCap; size >= 1; size--)
            {
                counted += sizeHistogram[size];
                if (counted >= wanted)
                    return Math.Max(StoredFloor, size);
            }
            return MinimumLots;
        }

        private static int FirstTradeAtOrAfter(List<BigTrade> sorted, int barIndex)
        {
            int low = 0;
            int high = sorted.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (sorted[middle].BarIndex < barIndex)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.Direct2D1.Brush buyFill = BuyColor.ToDxBrush(RenderTarget, FillOpacity / 100f);
            SharpDX.Direct2D1.Brush sellFill = SellColor.ToDxBrush(RenderTarget, FillOpacity / 100f);
            SharpDX.Direct2D1.Brush buyLine = BuyColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush sellLine = SellColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush textBrush = TextColor.ToDxBrush(RenderTarget);
            SharpDX.DirectWrite.TextFormat textFormat = LabelFont.ToDirectWriteTextFormat();
            textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
            textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
            textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

            try
            {
                lock (trades)
                {
                    double threshold = FilterMode == FlowBigTradesFilter.Automatic ? AutomaticThreshold() : MinimumLots;
                    for (int i = FirstTradeAtOrAfter(trades, ChartBars.FromIndex); i < trades.Count; i++)
                    {
                        BigTrade trade = trades[i];
                        if (trade.BarIndex > ChartBars.ToIndex)
                            break;
                        if (trade.Lots < threshold || (MaximumLots > 0 && trade.Lots > MaximumLots))
                            continue;

                        float radius = (float)Math.Min(MaximumSize, MinimumSize * Math.Sqrt(trade.Lots / threshold)) / 2f;
                        float x = chartControl.GetXByBarIndex(ChartBars, trade.BarIndex);
                        float y = chartScale.GetYByValue(trade.Price);
                        SharpDX.Direct2D1.Ellipse bubble = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), radius, radius);
                        if (!HollowFill)
                            RenderTarget.FillEllipse(bubble, trade.IsBuy ? buyFill : sellFill);
                        RenderTarget.DrawEllipse(bubble, trade.IsBuy ? buyLine : sellLine, 1.5f);

                        if (ShowValue && radius * 2 >= LabelFont.Size * 1.6)
                        {
                            const float boxSize = 80;
                            using (SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, trade.Lots.ToString("0"), textFormat, boxSize, boxSize))
                                RenderTarget.DrawTextLayout(new SharpDX.Vector2(x - boxSize / 2, y - boxSize / 2), layout, textBrush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
                        }
                    }
                }
            }
            finally
            {
                buyFill.Dispose();
                sellFill.Dispose();
                buyLine.Dispose();
                sellLine.Dispose();
                textBrush.Dispose();
                textFormat.Dispose();
            }
        }

        #region Properties

        [Display(Name = "Filter mode", Description = "Manual: show orders of at least Minimum lots. Automatic: show the largest orders seen on this chart, by Intensity.", Order = 1, GroupName = "1. Big trades")]
        public FlowBigTradesFilter FilterMode { get; set; }

        [Range(1, 10000), NinjaScriptProperty]
        [Display(Name = "Minimum lots", Description = "Manual filter: smallest order shown. Also used by Automatic until enough orders have been seen.", Order = 2, GroupName = "1. Big trades")]
        public int MinimumLots { get; set; }

        [Range(0, 100000), NinjaScriptProperty]
        [Display(Name = "Maximum lots", Description = "Largest order shown, to split size bands across two copies. 0 turns it off.", Order = 3, GroupName = "1. Big trades")]
        public int MaximumLots { get; set; }

        [Display(Name = "Intensity", Description = "Automatic filter: Low shows the top 1% of orders by size, Medium the top 0.5%, Strong the top 0.1%.", Order = 4, GroupName = "1. Big trades")]
        public FlowBigTradesIntensity Intensity { get; set; }

        [Range(0, 1000), NinjaScriptProperty]
        [Display(Name = "Aggregation (ms)", Description = "Fills on the same side arriving within this many milliseconds of the order's first fill count as one order. 0 joins only fills with the same timestamp.", Order = 5, GroupName = "1. Big trades")]
        public int AggregationMilliseconds { get; set; }

        [Range(2, 200)]
        [Display(Name = "Minimum size", Description = "Bubble diameter, in pixels, of an order at the threshold.", Order = 1, GroupName = "2. Appearance")]
        public int MinimumSize { get; set; }

        [Range(2, 400)]
        [Display(Name = "Maximum size", Description = "Largest bubble diameter, in pixels; bubbles grow with the square root of lots.", Order = 2, GroupName = "2. Appearance")]
        public int MaximumSize { get; set; }

        [Range(0, 100)]
        [Display(Name = "Fill opacity (%)", Order = 3, GroupName = "2. Appearance")]
        public int FillOpacity { get; set; }

        [Display(Name = "Hollow", Description = "Outline only, no fill.", Order = 4, GroupName = "2. Appearance")]
        public bool HollowFill { get; set; }

        [Display(Name = "Show lots", Description = "Print the order's lots inside bubbles big enough to hold them.", Order = 5, GroupName = "2. Appearance")]
        public bool ShowValue { get; set; }

        [Display(Name = "Font", Order = 6, GroupName = "2. Appearance")]
        public SimpleFont LabelFont { get; set; }

        [XmlIgnore]
        [Display(Name = "Buy color", Description = "Orders lifting the ask.", Order = 1, GroupName = "3. Colors")]
        public System.Windows.Media.Brush BuyColor { get; set; }

        [Browsable(false)]
        public string BuyColorSerializable
        {
            get { return Serialize.BrushToString(BuyColor); }
            set { BuyColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Sell color", Description = "Orders hitting the bid.", Order = 2, GroupName = "3. Colors")]
        public System.Windows.Media.Brush SellColor { get; set; }

        [Browsable(false)]
        public string SellColorSerializable
        {
            get { return Serialize.BrushToString(SellColor); }
            set { SellColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Text color", Order = 3, GroupName = "3. Colors")]
        public System.Windows.Media.Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        #endregion
    }
}
