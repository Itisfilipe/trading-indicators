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
    /// <summary>
    /// Absorption levels: a price inside a closed candle that took heavy
    /// one-sided aggression and held, drawn as a band to the right with the
    /// lots still defending it, until the later candles' net aggression into
    /// that price eats them.
    /// </summary>
    public class FlowAbsorption : Indicator
    {
        private class LevelVolume
        {
            public double Buys;
            public double Sells;
        }

        private class AbsorptionLevel
        {
            public int StartBar;
            public double Price;
            public bool BuyersAbsorbing;
            public double InitialLots;
            public double RemainingLots;
            public int EndBar = -1;
        }

        private readonly Dictionary<double, LevelVolume> barLevels = new Dictionary<double, LevelVolume>();
        private readonly List<AbsorptionLevel> absorptionLevels = new List<AbsorptionLevel>();
        private SessionIterator sessionIterator;
        private int levelsBar = -1;
        private int finalizedBar = -1;

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
                Description = "Prices that took heavy one-sided aggression and held, with the lots still defending them. Needs Tick Replay for history.";
                Name = "Flow Absorption";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                PriceGrouping = 4;
                MinimumLots = 100;
                LevelMultiplier = 2.0;
                EdgePercent = 50;
                FillOpacity = 40;
                MinimumBandHeight = 8;
                ShowConsumed = false;

                BuyersAbsorbingColor = Brushes.DodgerBlue;
                SellersAbsorbingColor = Brushes.Crimson;
                TextColor = Brushes.White;
                LabelFont = new SimpleFont("Arial", 9) { Bold = true };
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
            }
            else if (State == State.Historical)
            {
                if (Calculate == Calculate.OnBarClose)
                    Draw.TextFixed(this, "FlowAbsorptionCalculate", "Flow Absorption: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay)
                    Draw.TextFixed(this, "FlowAbsorptionTickReplay", "Flow Absorption: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
            }
            else if (State == State.Realtime)
            {
                if (levelsBar >= 0 && !sessionIterator.IsInSession(Core.Globals.Now, true, true))
                {
                    CloseBar(levelsBar);
                    finalizedBar = levelsBar;
                }
            }
        }

        protected override void OnBarUpdate()
        {
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last || CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;
            if (State == State.Realtime && !sessionIterator.IsInSession(Core.Globals.Now, true, true))
                return;

            if (CurrentBar != levelsBar)
            {
                if (levelsBar >= 0 && levelsBar != finalizedBar)
                    CloseBar(levelsBar);
                barLevels.Clear();
                levelsBar = CurrentBar;
            }

            double key = LevelKey(e.Price);
            LevelVolume level;
            if (!barLevels.TryGetValue(key, out level))
            {
                level = new LevelVolume();
                barLevels.Add(key, level);
            }

            bool isBuy = e.Price >= e.Ask;
            bool isSell = !isBuy && e.Price <= e.Bid;
            if (isBuy)
                level.Buys += e.Volume;
            else if (isSell)
                level.Sells += e.Volume;
        }

        private double LevelKey(double price)
        {
            double bucketSize = TickSize * PriceGrouping;
            return Instrument.MasterInstrument.RoundToTickSize(Math.Floor(price / bucketSize + 1e-9) * bucketSize);
        }

        private void CloseBar(int closedBar)
        {
            double high = Bars.GetHigh(closedBar);
            double low = Bars.GetLow(closedBar);
            double close = Bars.GetClose(closedBar);
            double groupTop = (PriceGrouping - 1) * TickSize;

            lock (absorptionLevels)
            {
                foreach (AbsorptionLevel level in absorptionLevels)
                {
                    LevelVolume traded;
                    if (level.EndBar >= 0 || level.StartBar >= closedBar || !barLevels.TryGetValue(level.Price, out traded))
                        continue;
                    double netAgainst = level.BuyersAbsorbing ? traded.Sells - traded.Buys : traded.Buys - traded.Sells;
                    if (netAgainst <= 0)
                        continue;
                    level.RemainingLots -= netAgainst;
                    if (level.RemainingLots <= 0)
                    {
                        level.RemainingLots = 0;
                        level.EndBar = closedBar;
                    }
                }

                double range = high - low;
                if (range <= 0 || barLevels.Count == 0)
                    return;

                double aggression = 0;
                foreach (LevelVolume volume in barLevels.Values)
                    aggression += volume.Buys + volume.Sells;
                double perLevelAverage = aggression / barLevels.Count;
                double threshold = Math.Max(MinimumLots, perLevelAverage * LevelMultiplier);
                double edge = range * EdgePercent / 100.0;

                foreach (KeyValuePair<double, LevelVolume> entry in barLevels)
                {
                    double price = entry.Key;
                    double levelTop = price + groupTop;
                    double sells = entry.Value.Sells;
                    double buys = entry.Value.Buys;
                    if (sells > buys && sells >= threshold && price <= low + edge && close > levelTop)
                        absorptionLevels.Add(new AbsorptionLevel { StartBar = closedBar, Price = price, BuyersAbsorbing = true, InitialLots = sells, RemainingLots = sells });
                    else if (buys > sells && buys >= threshold && levelTop >= high - edge && close < price)
                        absorptionLevels.Add(new AbsorptionLevel { StartBar = closedBar, Price = price, BuyersAbsorbing = false, InitialLots = buys, RemainingLots = buys });
                }
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.Direct2D1.Brush buyersFill = BuyersAbsorbingColor.ToDxBrush(RenderTarget, FillOpacity / 100f);
            SharpDX.Direct2D1.Brush sellersFill = SellersAbsorbingColor.ToDxBrush(RenderTarget, FillOpacity / 100f);
            SharpDX.Direct2D1.Brush buyersConsumed = BuyersAbsorbingColor.ToDxBrush(RenderTarget, FillOpacity / 300f);
            SharpDX.Direct2D1.Brush sellersConsumed = SellersAbsorbingColor.ToDxBrush(RenderTarget, FillOpacity / 300f);
            SharpDX.Direct2D1.Brush textBrush = TextColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush buyersBox = BuyersAbsorbingColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush sellersBox = SellersAbsorbingColor.ToDxBrush(RenderTarget);
            SharpDX.DirectWrite.TextFormat textFormat = LabelFont.ToDirectWriteTextFormat();
            textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

            try
            {
                float panelRight = ChartPanel.X + ChartPanel.W;
                double groupTop = (PriceGrouping - 1) * TickSize;

                lock (absorptionLevels)
                    foreach (AbsorptionLevel level in absorptionLevels)
                    {
                        bool consumed = level.EndBar >= 0;
                        if (consumed && !ShowConsumed)
                            continue;
                        if (level.StartBar > ChartBars.ToIndex || (consumed && level.EndBar < ChartBars.FromIndex))
                            continue;

                        float x1 = chartControl.GetXByBarIndex(ChartBars, level.StartBar);
                        float x2 = consumed ? chartControl.GetXByBarIndex(ChartBars, level.EndBar) : panelRight;
                        float top = chartScale.GetYByValue(level.Price + groupTop + TickSize / 2);
                        float bottom = chartScale.GetYByValue(level.Price - TickSize / 2);
                        float height = Math.Max(MinimumBandHeight, bottom - top);
                        if (bottom - top < MinimumBandHeight)
                            top = (top + bottom - height) / 2;
                        if (x2 <= x1)
                            continue;

                        SharpDX.Direct2D1.Brush fill = level.BuyersAbsorbing
                            ? (consumed ? buyersConsumed : buyersFill)
                            : (consumed ? sellersConsumed : sellersFill);
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(x1, top, x2 - x1, height), fill);

                        if (consumed)
                            continue;

                        SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory,
                            level.RemainingLots.ToString("0"), textFormat, 200, 40);
                        try
                        {
                            float labelX = panelRight - layout.Metrics.Width - 4;
                            float labelY = top + height / 2 - layout.Metrics.Height / 2;
                            RenderTarget.FillRectangle(new SharpDX.RectangleF(labelX - 3, labelY - 1, layout.Metrics.Width + 6, layout.Metrics.Height + 2),
                                level.BuyersAbsorbing ? buyersBox : sellersBox);
                            RenderTarget.DrawTextLayout(new SharpDX.Vector2(labelX, labelY), layout, textBrush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
                        }
                        finally
                        {
                            layout.Dispose();
                        }
                    }
            }
            finally
            {
                buyersFill.Dispose();
                sellersFill.Dispose();
                buyersConsumed.Dispose();
                sellersConsumed.Dispose();
                textBrush.Dispose();
                buyersBox.Dispose();
                sellersBox.Dispose();
                textFormat.Dispose();
            }
        }

        #region Properties

        [Range(1, 100), NinjaScriptProperty]
        [Display(Name = "Price grouping (ticks)", Description = "Ticks merged into one price level.", Order = 1, GroupName = "1. Absorption")]
        public int PriceGrouping { get; set; }

        [Range(1, 100000), NinjaScriptProperty]
        [Display(Name = "Minimum lots", Description = "One-sided aggression (sells into a low zone, or buys into a high one) a price zone needs before it can become a level.", Order = 2, GroupName = "1. Absorption")]
        public int MinimumLots { get; set; }

        [Range(1, 50), NinjaScriptProperty]
        [Display(Name = "Level multiplier", Description = "The zone's one-sided aggression must also reach the candle's average aggression per zone times this.", Order = 3, GroupName = "1. Absorption")]
        public double LevelMultiplier { get; set; }

        [Range(1, 100), NinjaScriptProperty]
        [Display(Name = "Edge (%)", Description = "How close to the candle's low (selling absorbed) or high (buying absorbed) the price must be, as a share of the candle's range.", Order = 4, GroupName = "1. Absorption")]
        public int EdgePercent { get; set; }

        [Display(Name = "Show consumed levels", Description = "Keep faded bands for levels whose lots were already eaten.", Order = 5, GroupName = "1. Absorption")]
        public bool ShowConsumed { get; set; }

        [Range(0, 100)]
        [Display(Name = "Fill opacity (%)", Order = 1, GroupName = "2. Appearance")]
        public int FillOpacity { get; set; }

        [Range(1, 40)]
        [Display(Name = "Minimum band height (px)", Description = "Bands thinner than this on screen are drawn this tall, centered on the level.", Order = 2, GroupName = "2. Appearance")]
        public int MinimumBandHeight { get; set; }

        [Display(Name = "Font", Order = 3, GroupName = "2. Appearance")]
        public SimpleFont LabelFont { get; set; }

        [XmlIgnore]
        [Display(Name = "Buyers absorbing (selling held)", Order = 1, GroupName = "3. Colors")]
        public System.Windows.Media.Brush BuyersAbsorbingColor { get; set; }

        [Browsable(false)]
        public string BuyersAbsorbingColorSerializable
        {
            get { return Serialize.BrushToString(BuyersAbsorbingColor); }
            set { BuyersAbsorbingColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Sellers absorbing (buying held)", Order = 2, GroupName = "3. Colors")]
        public System.Windows.Media.Brush SellersAbsorbingColor { get; set; }

        [Browsable(false)]
        public string SellersAbsorbingColorSerializable
        {
            get { return Serialize.BrushToString(SellersAbsorbingColor); }
            set { SellersAbsorbingColor = Serialize.StringToBrush(value); }
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
