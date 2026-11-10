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
    public enum FlowPocMarker
    {
        Circle,
        Square
    }

    public enum FlowPocLabel
    {
        Delta,
        Volume
    }

    public enum FlowPocMeasure
    {
        Volume,
        Delta
    }

    public enum FlowPocStrength
    {
        RecentCandles,
        SameTimeOfDay
    }

    /// <summary>
    /// Marks each candle's point of control (the most traded price) colored by
    /// the aggressor that dominated it, labels the strong ones with their
    /// contract count, and prints each candle's volume and delta.
    /// </summary>
    public class FlowPOC : Indicator
    {
        private class LevelVolume
        {
            public double Buys;
            public double Sells;
            public double Total;
        }

        private readonly Dictionary<double, LevelVolume> levels = new Dictionary<double, LevelVolume>();
        private int levelsBar = -1;
        private double pocKey;
        private double runningVolume;
        private double runningDelta;

        private SessionIterator sessionIterator;
        private Series<double> pocPrice;
        private Series<double> pocVolume;
        private Series<double> pocBuys;
        private Series<double> pocSells;
        private Series<double> barVolume;
        private Series<double> barDelta;
        private Series<double> slotBaseline;

        private const int MinimumSessions = 3;
        private bool useTimeOfDay;
        private readonly Dictionary<TimeSpan, List<double>> slotHistory = new Dictionary<TimeSpan, List<double>>();

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
                Description = "Each candle's point of control colored by aggressor, with contract counts on the strong ones. Needs Tick Replay for history.";
                Name = "Flow POC";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                PriceGrouping = 1;
                AverageBars = 30;
                AverageMultiplier = 1.0;
                NeutralBandPercent = 0;
                StrengthMode = FlowPocStrength.RecentCandles;
                PocLabel = FlowPocLabel.Delta;
                StrengthMeasure = FlowPocMeasure.Volume;
                SessionsBack = 20;
                HighlightAbsorption = false;
                AbsorptionEdgePercent = 25;
                Marker = FlowPocMarker.Circle;
                MarkerSize = 18;
                FillOpacity = 40;
                ShowBarStats = true;

                BuyColor = Brushes.SeaGreen;
                SellColor = Brushes.Crimson;
                NeutralColor = Rgb(0x5F, 0x67, 0x73);
                AbsorptionColor = Rgb(0xB8, 0x86, 0x2B);
                TextColor = Rgb(0x37, 0x41, 0x51);
                LabelFont = new SimpleFont("Arial", 10);
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
                pocPrice = new Series<double>(this, MaximumBarsLookBack.Infinite);
                pocVolume = new Series<double>(this, MaximumBarsLookBack.Infinite);
                pocBuys = new Series<double>(this, MaximumBarsLookBack.Infinite);
                pocSells = new Series<double>(this, MaximumBarsLookBack.Infinite);
                barVolume = new Series<double>(this, MaximumBarsLookBack.Infinite);
                barDelta = new Series<double>(this, MaximumBarsLookBack.Infinite);
                slotBaseline = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
            else if (State == State.Historical)
            {
                useTimeOfDay = StrengthMode == FlowPocStrength.SameTimeOfDay && Bars.BarsType.IsTimeBased;
                if (StrengthMode == FlowPocStrength.SameTimeOfDay && !useTimeOfDay)
                    Draw.TextFixed(this, "FlowPocStrength", "Flow POC: same time of day needs time-based bars; using recent candles", TextPosition.TopLeft);
                if (Calculate == Calculate.OnBarClose)
                    Draw.TextFixed(this, "FlowPocCalculate", "Flow POC: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay)
                    Draw.TextFixed(this, "FlowPocTickReplay", "Flow POC: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
            }
        }

        protected override void OnBarUpdate()
        {
            if (!useTimeOfDay || !IsFirstTickOfBar)
                return;

            if (CurrentBar > 0 && pocVolume.IsValidDataPoint(1))
                SlotVolumes(ExchangeTimeOfDay(1)).Add(PocStrength(CurrentBar - 1));

            List<double> history = SlotVolumes(ExchangeTimeOfDay(0));
            if (history.Count >= MinimumSessions)
                slotBaseline[0] = MedianOfLast(history, SessionsBack);
        }

        private TimeSpan ExchangeTimeOfDay(int barsAgo)
        {
            return TimeZoneInfo.ConvertTime(Time[barsAgo], Core.Globals.GeneralOptions.TimeZoneInfo, Bars.TradingHours.TimeZoneInfo).TimeOfDay;
        }

        private List<double> SlotVolumes(TimeSpan timeOfDay)
        {
            List<double> volumes;
            if (!slotHistory.TryGetValue(timeOfDay, out volumes))
            {
                volumes = new List<double>();
                slotHistory.Add(timeOfDay, volumes);
            }
            return volumes;
        }

        private static double MedianOfLast(List<double> values, int count)
        {
            int taken = Math.Min(count, values.Count);
            List<double> recent = values.GetRange(values.Count - taken, taken);
            recent.Sort();
            int middle = taken / 2;
            return taken % 2 == 1 ? recent[middle] : (recent[middle - 1] + recent[middle]) / 2;
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last || CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;
            if (State == State.Realtime && !sessionIterator.IsInSession(Core.Globals.Now, true, true))
                return;

            if (CurrentBar != levelsBar)
            {
                levels.Clear();
                levelsBar = CurrentBar;
                pocKey = double.NaN;
                runningVolume = 0;
                runningDelta = 0;
            }

            double bucketSize = TickSize * PriceGrouping;
            double key = Instrument.MasterInstrument.RoundToTickSize(Math.Floor(e.Price / bucketSize + 1e-9) * bucketSize);

            LevelVolume level;
            if (!levels.TryGetValue(key, out level))
            {
                level = new LevelVolume();
                levels.Add(key, level);
            }

            double volume = e.Volume;
            level.Total += volume;
            runningVolume += volume;
            if (e.Price >= e.Ask)
            {
                level.Buys += volume;
                runningDelta += volume;
            }
            else if (e.Price <= e.Bid)
            {
                level.Sells += volume;
                runningDelta -= volume;
            }

            if (double.IsNaN(pocKey) || level.Total > levels[pocKey].Total)
                pocKey = key;

            LevelVolume poc = levels[pocKey];
            pocPrice[0] = pocKey;
            pocVolume[0] = poc.Total;
            pocBuys[0] = poc.Buys;
            pocSells[0] = poc.Sells;
            barVolume[0] = runningVolume;
            barDelta[0] = runningDelta;
        }

        private double PocStrength(int barIndex)
        {
            if (StrengthMeasure == FlowPocMeasure.Volume)
                return pocVolume.GetValueAt(barIndex);
            return Math.Abs(pocBuys.GetValueAt(barIndex) - pocSells.GetValueAt(barIndex));
        }

        private double AveragePocStrength(int barIndex)
        {
            if (barIndex < AverageBars)
                return 0;

            double sum = 0;
            for (int index = barIndex - AverageBars; index < barIndex; index++)
            {
                if (!pocVolume.IsValidDataPointAt(index))
                    return 0;
                sum += PocStrength(index);
            }
            return sum / AverageBars;
        }

        private double StrengthBaseline(int barIndex)
        {
            if (!useTimeOfDay)
                return AveragePocStrength(barIndex);
            return slotBaseline.IsValidDataPointAt(barIndex) ? slotBaseline.GetValueAt(barIndex) : 0;
        }

        private bool IsAbsorptionPoc(int barIndex, int aggressorSide)
        {
            bool isClosed = barIndex < Bars.Count - 1 || State != State.Realtime;
            double high = Bars.GetHigh(barIndex);
            double low = Bars.GetLow(barIndex);
            if (!isClosed || high <= low)
                return false;

            double pocLow = pocPrice.GetValueAt(barIndex);
            double pocHigh = pocLow + (PriceGrouping - 1) * TickSize;
            double positionPercent = 100 * ((pocLow + pocHigh) / 2 - low) / (high - low);
            double close = Bars.GetClose(barIndex);

            if (aggressorSide < 0 && positionPercent <= AbsorptionEdgePercent && close > pocHigh)
                return true;
            return aggressorSide > 0 && positionPercent >= 100 - AbsorptionEdgePercent && close < pocLow;
        }

        private int AggressorSide(double buys, double sells)
        {
            double traded = buys + sells;
            if (traded <= 0)
                return 0;

            double imbalancePercent = 100.0 * (buys - sells) / traded;
            if (imbalancePercent > NeutralBandPercent)
                return 1;
            if (imbalancePercent < -NeutralBandPercent)
                return -1;
            return 0;
        }

        private System.Windows.Media.Brush SideColor(int aggressorSide)
        {
            if (aggressorSide > 0)
                return BuyColor;
            if (aggressorSide < 0)
                return SellColor;
            return NeutralColor;
        }

        private static string FormatVolume(double volume)
        {
            return volume >= 1000 ? (volume / 1000).ToString("0.0") + "K" : volume.ToString("0");
        }

        private void DrawCenteredText(string text, SharpDX.DirectWrite.TextFormat textFormat, float centerX, float centerY, SharpDX.Direct2D1.Brush brush)
        {
            const float boxWidth = 200;
            const float boxHeight = 60;
            SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, text, textFormat, boxWidth, boxHeight);
            try
            {
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(centerX - boxWidth / 2, centerY - boxHeight / 2), layout, brush,
                    SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
            finally
            {
                layout.Dispose();
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.DirectWrite.TextFormat textFormat = LabelFont.ToDirectWriteTextFormat();
            textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
            textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
            textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

            Dictionary<System.Windows.Media.Brush, SharpDX.Direct2D1.Brush> fills = new Dictionary<System.Windows.Media.Brush, SharpDX.Direct2D1.Brush>();
            Dictionary<System.Windows.Media.Brush, SharpDX.Direct2D1.Brush> lines = new Dictionary<System.Windows.Media.Brush, SharpDX.Direct2D1.Brush>();
            foreach (System.Windows.Media.Brush color in new[] { BuyColor, SellColor, NeutralColor })
            {
                if (fills.ContainsKey(color))
                    continue;
                fills.Add(color, color.ToDxBrush(RenderTarget, FillOpacity / 100f));
                lines.Add(color, color.ToDxBrush(RenderTarget));
            }
            SharpDX.Direct2D1.Brush textBrush = TextColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush absorptionBrush = AbsorptionColor.ToDxBrush(RenderTarget);

            try
            {
                float barWidth = (float)chartControl.GetBarPaintWidth(ChartBars);
                float rowHeight = (float)LabelFont.Size * 1.4f;
                double groupOffset = (PriceGrouping - 1) * TickSize / 2;

                for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
                {
                    if (!pocVolume.IsValidDataPointAt(barIndex))
                        continue;

                    float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
                    float y = chartScale.GetYByValue(pocPrice.GetValueAt(barIndex) + groupOffset);
                    double volume = pocVolume.GetValueAt(barIndex);
                    double strength = PocStrength(barIndex);
                    int aggressorSide = AggressorSide(pocBuys.GetValueAt(barIndex), pocSells.GetValueAt(barIndex));
                    System.Windows.Media.Brush color = SideColor(aggressorSide);
                    double baseline = StrengthBaseline(barIndex);

                    if (baseline > 0 && strength > baseline * AverageMultiplier)
                    {
                        float growth = (float)Math.Min(1.25, Math.Max(0.9, Math.Sqrt(strength / baseline)));
                        float radius = MarkerSize / 2f * growth;
                        float ringRadius = radius + 4;
                        bool isAbsorption = HighlightAbsorption && IsAbsorptionPoc(barIndex, aggressorSide);
                        if (Marker == FlowPocMarker.Circle)
                        {
                            SharpDX.Direct2D1.Ellipse ellipse = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), radius, radius);
                            RenderTarget.FillEllipse(ellipse, fills[color]);
                            RenderTarget.DrawEllipse(ellipse, lines[color], 1.5f);
                            if (isAbsorption)
                                RenderTarget.DrawEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), ringRadius, ringRadius), absorptionBrush, 2.5f);
                        }
                        else
                        {
                            SharpDX.RectangleF square = new SharpDX.RectangleF(x - radius, y - radius, radius * 2, radius * 2);
                            RenderTarget.FillRectangle(square, fills[color]);
                            RenderTarget.DrawRectangle(square, lines[color], 1.5f);
                            if (isAbsorption)
                                RenderTarget.DrawRectangle(new SharpDX.RectangleF(x - ringRadius, y - ringRadius, ringRadius * 2, ringRadius * 2), absorptionBrush, 2.5f);
                        }
                        double labelValue = PocLabel == FlowPocLabel.Delta
                            ? Math.Abs(pocBuys.GetValueAt(barIndex) - pocSells.GetValueAt(barIndex))
                            : volume;
                        DrawCenteredText(labelValue.ToString("0"), textFormat, x, y, textBrush);
                    }
                    else
                    {
                        RenderTarget.DrawLine(new SharpDX.Vector2(x - barWidth / 2 - 2, y), new SharpDX.Vector2(x + barWidth / 2 + 2, y), lines[color], 3f);
                    }

                    if (ShowBarStats)
                    {
                        double delta = barDelta.GetValueAt(barIndex);
                        bool closedDown = Bars.GetClose(barIndex) < Bars.GetOpen(barIndex);
                        float volumeY = closedDown
                            ? chartScale.GetYByValue(Bars.GetHigh(barIndex)) - 4 - rowHeight / 2
                            : chartScale.GetYByValue(Bars.GetLow(barIndex)) + 4 + rowHeight / 2;
                        float deltaY = closedDown ? volumeY - rowHeight : volumeY + rowHeight;
                        DrawCenteredText(FormatVolume(barVolume.GetValueAt(barIndex)), textFormat, x, volumeY, textBrush);
                        DrawCenteredText((delta > 0 ? "+" : "") + delta.ToString("0"), textFormat, x, deltaY,
                            lines[delta >= 0 ? BuyColor : SellColor]);
                    }
                }
            }
            finally
            {
                textFormat.Dispose();
                textBrush.Dispose();
                absorptionBrush.Dispose();
                foreach (SharpDX.Direct2D1.Brush brush in fills.Values)
                    brush.Dispose();
                foreach (SharpDX.Direct2D1.Brush brush in lines.Values)
                    brush.Dispose();
            }
        }

        #region Properties

        [Range(1, 100), NinjaScriptProperty]
        [Display(Name = "Price grouping (ticks)", Description = "Ticks merged into one price level before finding the POC.", Order = 1, GroupName = "1. POC")]
        public int PriceGrouping { get; set; }

        [Range(1, 500), NinjaScriptProperty]
        [Display(Name = "Average candles", Description = "Candles in the moving average of POC volume.", Order = 2, GroupName = "1. POC")]
        public int AverageBars { get; set; }

        [Range(0.1, 20), NinjaScriptProperty]
        [Display(Name = "Average multiplier", Description = "A POC is strong when its volume beats the baseline (average or same-time median) times this.", Order = 3, GroupName = "1. POC")]
        public double AverageMultiplier { get; set; }

        [Range(0, 100), NinjaScriptProperty]
        [Display(Name = "Neutral band (%)", Description = "POCs whose buy/sell imbalance is inside this band are drawn neutral.", Order = 4, GroupName = "1. POC")]
        public int NeutralBandPercent { get; set; }

        [Display(Name = "Strength baseline", Description = "RecentCandles: average of the previous candles' POCs. SameTimeOfDay: median POC volume at the same time in past sessions.", Order = 5, GroupName = "1. POC")]
        public FlowPocStrength StrengthMode { get; set; }

        [Display(Name = "Label", Description = "Delta: the lots left over at the POC price (buys minus sells, side shown by the color). Volume: all contracts traded at the POC price.", Order = 9, GroupName = "1. POC")]
        public FlowPocLabel PocLabel { get; set; }

        [Display(Name = "Strength measure", Description = "What is compared with the baseline: the POC's volume, or its leftover lots (buys minus sells, either side).", Order = 10, GroupName = "1. POC")]
        public FlowPocMeasure StrengthMeasure { get; set; }

        [Range(1, 250), NinjaScriptProperty]
        [Display(Name = "Sessions back", Description = "Past sessions in the same-time median. Needs that many days loaded.", Order = 6, GroupName = "1. POC")]
        public int SessionsBack { get; set; }

        [Display(Name = "Highlight absorption POCs", Description = "Ring a strong closed POC sitting at the candle's edge with the aggressor that lost: sellers at the bottom of a candle closing above it, or buyers at the top of one closing below it.", Order = 7, GroupName = "1. POC")]
        public bool HighlightAbsorption { get; set; }

        [Range(1, 50)]
        [Display(Name = "Absorption edge (%)", Description = "How close to the candle's low (or high) the POC must sit, as a share of the candle's range.", Order = 8, GroupName = "1. POC")]
        public int AbsorptionEdgePercent { get; set; }

        [Display(Name = "Marker", Order = 1, GroupName = "2. Appearance")]
        public FlowPocMarker Marker { get; set; }

        [Range(6, 60)]
        [Display(Name = "Marker size", Order = 2, GroupName = "2. Appearance")]
        public int MarkerSize { get; set; }

        [Range(0, 100)]
        [Display(Name = "Fill opacity (%)", Order = 3, GroupName = "2. Appearance")]
        public int FillOpacity { get; set; }

        [Display(Name = "Show volume and delta", Description = "Print each candle's volume and delta at its opening end: above a falling candle, below a rising one.", Order = 4, GroupName = "2. Appearance")]
        public bool ShowBarStats { get; set; }

        [Display(Name = "Font", Order = 5, GroupName = "2. Appearance")]
        public SimpleFont LabelFont { get; set; }

        [XmlIgnore]
        [Display(Name = "Buyer color", Order = 1, GroupName = "3. Colors")]
        public System.Windows.Media.Brush BuyColor { get; set; }

        [Browsable(false)]
        public string BuyColorSerializable
        {
            get { return Serialize.BrushToString(BuyColor); }
            set { BuyColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Seller color", Order = 2, GroupName = "3. Colors")]
        public System.Windows.Media.Brush SellColor { get; set; }

        [Browsable(false)]
        public string SellColorSerializable
        {
            get { return Serialize.BrushToString(SellColor); }
            set { SellColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Neutral color", Order = 3, GroupName = "3. Colors")]
        public System.Windows.Media.Brush NeutralColor { get; set; }

        [Browsable(false)]
        public string NeutralColorSerializable
        {
            get { return Serialize.BrushToString(NeutralColor); }
            set { NeutralColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Absorption ring", Order = 5, GroupName = "3. Colors")]
        public System.Windows.Media.Brush AbsorptionColor { get; set; }

        [Browsable(false)]
        public string AbsorptionColorSerializable
        {
            get { return Serialize.BrushToString(AbsorptionColor); }
            set { AbsorptionColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Text color", Order = 4, GroupName = "3. Colors")]
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
