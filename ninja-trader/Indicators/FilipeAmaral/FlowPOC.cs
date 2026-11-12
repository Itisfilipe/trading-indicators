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

    public enum FlowPocPeriod
    {
        Chart,
        Minutes2,
        Minutes3,
        Minutes5,
        Minutes10,
        Minutes15,
        Minutes30,
        Hours1,
        Hours2,
        Hours4,
        Hours12,
        Session
    }

    /// <summary>
    /// Marks each candle's point of control (the most traded price) colored by
    /// the aggressor that dominated it, labels the strong ones with their
    /// contract count, and prints each candle's volume and delta. With a POC
    /// period set, the candles are grouped into clock-aligned windows of that
    /// many minutes and each window gets one POC spanning its candles, and one
    /// volume and delta.
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
        private int levelsKey = -1;
        private int periodMinutes;
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
        private Series<int> groupStarts;

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
                AverageBars = 12;
                AverageMultiplier = 1.0;
                NeutralBandPercent = 0;
                StrengthMode = FlowPocStrength.RecentCandles;
                PocLabel = FlowPocLabel.Volume;
                StrengthMeasure = FlowPocMeasure.Volume;
                SessionsBack = 4;
                HighlightAbsorption = false;
                AbsorptionEdgePercent = 25;
                OutlierMultiplier = 3;
                OutlierSizePercent = 130;
                OutlierSolidFill = true;
                OutlierBorderWidth = 3;
                Marker = FlowPocMarker.Square;
                MarkerSize = 18;
                FillOpacity = 40;
                ShowBarStats = false;
                PocPeriod = FlowPocPeriod.Chart;

                BuyColor = Brushes.DodgerBlue;
                SellColor = Brushes.Crimson;
                NeutralColor = Rgb(0x5F, 0x67, 0x73);
                AbsorptionColor = Rgb(0xB8, 0x86, 0x2B);
                TextColor = Rgb(0x37, 0x41, 0x51);
                LabelFont = new SimpleFont("Arial", 9);
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
                groupStarts = new Series<int>(this, MaximumBarsLookBack.Infinite);
            }
            else if (State == State.Historical)
            {
                periodMinutes = PeriodMinutes(PocPeriod);
                // Clock windows need candles with fixed time stamps that tile the window evenly.
                bool chartIsMinutes = Bars.BarsPeriod.BarsPeriodType == BarsPeriodType.Minute;
                bool tilesWindow = chartIsMinutes && Bars.BarsPeriod.Value < periodMinutes && periodMinutes % Bars.BarsPeriod.Value == 0;
                bool isSession = PocPeriod == FlowPocPeriod.Session;
                if (!Bars.BarsType.IsIntraday || (!isSession && !tilesWindow))
                    periodMinutes = 0;
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
            if (!IsFirstTickOfBar)
                return;

            groupStarts[0] = NewGroupStart();

            // A candle with no trades yet still belongs to its window's POC.
            bool continuesGroup = CurrentBar > 0 && !IsGroupEnd(CurrentBar - 1);
            if (continuesGroup && pocVolume.IsValidDataPoint(1) && !pocVolume.IsValidDataPoint(0))
            {
                pocPrice[0] = pocPrice[1];
                pocVolume[0] = pocVolume[1];
                pocBuys[0] = pocBuys[1];
                pocSells[0] = pocSells[1];
                barVolume[0] = barVolume[1];
                barDelta[0] = barDelta[1];
            }

            if (!useTimeOfDay)
                return;

            if (CurrentBar > 0 && !continuesGroup && pocVolume.IsValidDataPoint(1))
                SlotVolumes(GroupTimeOfDay(CurrentBar - 1)).Add(PocStrength(CurrentBar - 1));

            List<double> history = SlotVolumes(GroupTimeOfDay(CurrentBar));
            if (history.Count >= MinimumSessions)
                slotBaseline[0] = MedianOfLast(history, SessionsBack);
        }

        private static int PeriodMinutes(FlowPocPeriod period)
        {
            switch (period)
            {
                case FlowPocPeriod.Minutes2: return 2;
                case FlowPocPeriod.Minutes3: return 3;
                case FlowPocPeriod.Minutes5: return 5;
                case FlowPocPeriod.Minutes10: return 10;
                case FlowPocPeriod.Minutes15: return 15;
                case FlowPocPeriod.Minutes30: return 30;
                case FlowPocPeriod.Hours1: return 60;
                case FlowPocPeriod.Hours2: return 120;
                case FlowPocPeriod.Hours4: return 240;
                case FlowPocPeriod.Hours12: return 720;
                case FlowPocPeriod.Session: return 1440;
                default: return 0;
            }
        }

        // Bars are stamped with their close time, so the tick before it places
        // a candle closing on the boundary in the window it actually traded in.
        // Windows count from exchange midnight so they hold still across the
        // user's and the exchange's daylight-saving changes.
        private DateTime WindowStart(int barIndex)
        {
            DateTime exchangeTime = TimeZoneInfo.ConvertTime(Bars.GetTime(barIndex), Core.Globals.GeneralOptions.TimeZoneInfo, Bars.TradingHours.TimeZoneInfo);
            long barTicks = exchangeTime.AddTicks(-1).Ticks;
            long periodTicks = TimeSpan.FromMinutes(periodMinutes).Ticks;
            return new DateTime(barTicks - barTicks % periodTicks);
        }

        /// <summary>
        /// Index of the first candle of the current candle's group. A group is
        /// the candle itself on Chart; otherwise a new one starts with each
        /// session and, for a minute period, with each new window.
        /// </summary>
        private int NewGroupStart()
        {
            if (periodMinutes == 0 || CurrentBar == 0 || Bars.IsFirstBarOfSession)
                return CurrentBar;
            if (PocPeriod != FlowPocPeriod.Session && WindowStart(CurrentBar) != WindowStart(CurrentBar - 1))
                return CurrentBar;
            return groupStarts[1];
        }

        private int GroupStart(int barIndex)
        {
            return groupStarts.GetValueAt(barIndex);
        }

        private bool IsGroupEnd(int barIndex)
        {
            return periodMinutes == 0 || barIndex + 1 >= Bars.Count || GroupStart(barIndex + 1) != GroupStart(barIndex);
        }

        private TimeSpan GroupTimeOfDay(int barIndex)
        {
            if (periodMinutes == 0)
                return TimeZoneInfo.ConvertTime(Bars.GetTime(barIndex), Core.Globals.GeneralOptions.TimeZoneInfo, Bars.TradingHours.TimeZoneInfo).TimeOfDay;
            if (PocPeriod == FlowPocPeriod.Session)
                return TimeSpan.Zero;
            return WindowStart(barIndex).TimeOfDay;
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
            if (State == State.Realtime && !sessionIterator.IsInSession(e.Time, true, true))
                return;

            if (!groupStarts.IsValidDataPoint(0))
                groupStarts[0] = NewGroupStart();
            int groupKey = GroupStart(CurrentBar);
            if (groupKey != levelsKey)
            {
                levels.Clear();
                levelsKey = groupKey;
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
            double sum = 0;
            int previousGroupEnd = GroupStart(barIndex) - 1;
            for (int counted = 0; counted < AverageBars; counted++)
            {
                if (previousGroupEnd < 0 || !pocVolume.IsValidDataPointAt(previousGroupEnd))
                    return 0;
                sum += PocStrength(previousGroupEnd);
                previousGroupEnd = GroupStart(previousGroupEnd) - 1;
            }
            return sum / AverageBars;
        }

        private double StrengthBaseline(int barIndex)
        {
            if (!useTimeOfDay)
                return AveragePocStrength(barIndex);
            return slotBaseline.IsValidDataPointAt(barIndex) ? slotBaseline.GetValueAt(barIndex) : 0;
        }

        private double GroupHigh(int startIndex, int endIndex)
        {
            double high = Bars.GetHigh(startIndex);
            for (int index = startIndex + 1; index <= endIndex; index++)
                high = Math.Max(high, Bars.GetHigh(index));
            return high;
        }

        private double GroupLow(int startIndex, int endIndex)
        {
            double low = Bars.GetLow(startIndex);
            for (int index = startIndex + 1; index <= endIndex; index++)
                low = Math.Min(low, Bars.GetLow(index));
            return low;
        }

        private bool IsAbsorptionPoc(int startIndex, int barIndex, int aggressorSide)
        {
            bool isClosed = barIndex < Bars.Count - 1 || State != State.Realtime;
            double high = GroupHigh(startIndex, barIndex);
            double low = GroupLow(startIndex, barIndex);
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
            SharpDX.Direct2D1.Brush backgroundBrush = chartControl.Properties.ChartBackground.ToDxBrush(RenderTarget);

            try
            {
                float barWidth = (float)chartControl.GetBarPaintWidth(ChartBars);
                float rowHeight = (float)LabelFont.Size * 1.4f;
                double groupOffset = (PriceGrouping - 1) * TickSize / 2;

                int lastIndex = ChartBars.ToIndex;
                while (!IsGroupEnd(lastIndex))
                    lastIndex++;

                for (int barIndex = ChartBars.FromIndex; barIndex <= lastIndex; barIndex++)
                {
                    if (!pocVolume.IsValidDataPointAt(barIndex) || !IsGroupEnd(barIndex))
                        continue;

                    int startIndex = GroupStart(barIndex);
                    float left = chartControl.GetXByBarIndex(ChartBars, startIndex) - barWidth / 2;
                    float right = chartControl.GetXByBarIndex(ChartBars, barIndex) + barWidth / 2;
                    float x = (left + right) / 2;
                    float y = chartScale.GetYByValue(pocPrice.GetValueAt(barIndex) + groupOffset);
                    double volume = pocVolume.GetValueAt(barIndex);
                    double strength = PocStrength(barIndex);
                    int aggressorSide = AggressorSide(pocBuys.GetValueAt(barIndex), pocSells.GetValueAt(barIndex));
                    System.Windows.Media.Brush color = SideColor(aggressorSide);
                    double baseline = StrengthBaseline(barIndex);

                    if (baseline > 0 && strength > baseline * AverageMultiplier)
                    {
                        float growth = (float)Math.Min(1.1, Math.Max(1.0, Math.Sqrt(strength / baseline)));
                        bool isOutlier = OutlierMultiplier > 0 && strength >= baseline * OutlierMultiplier;
                        float radius = MarkerSize / 2f * growth * (isOutlier ? OutlierSizePercent / 100f : 1f);
                        float ringRadius = radius + 4;
                        bool isAbsorption = HighlightAbsorption && IsAbsorptionPoc(startIndex, barIndex, aggressorSide);
                        bool solid = isOutlier && OutlierSolidFill;
                        SharpDX.Direct2D1.Brush markerFill = solid ? lines[color] : fills[color];
                        float borderWidth = isOutlier ? OutlierBorderWidth : 1.5f;
                        if (Marker == FlowPocMarker.Circle)
                        {
                            SharpDX.Direct2D1.Ellipse ellipse = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), radius, radius);
                            RenderTarget.FillEllipse(ellipse, backgroundBrush);
                            RenderTarget.FillEllipse(ellipse, markerFill);
                            if (borderWidth > 0)
                                RenderTarget.DrawEllipse(ellipse, isOutlier ? textBrush : lines[color], borderWidth);
                            if (isAbsorption)
                                RenderTarget.DrawEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), ringRadius, ringRadius), absorptionBrush, 2.5f);
                        }
                        else
                        {
                            SharpDX.RectangleF square = new SharpDX.RectangleF(x - radius, y - radius, radius * 2, radius * 2);
                            RenderTarget.FillRectangle(square, backgroundBrush);
                            RenderTarget.FillRectangle(square, markerFill);
                            if (borderWidth > 0)
                                RenderTarget.DrawRectangle(square, isOutlier ? textBrush : lines[color], borderWidth);
                            if (isAbsorption)
                                RenderTarget.DrawRectangle(new SharpDX.RectangleF(x - ringRadius, y - ringRadius, ringRadius * 2, ringRadius * 2), absorptionBrush, 2.5f);
                        }
                        double labelValue = PocLabel == FlowPocLabel.Delta
                            ? Math.Abs(pocBuys.GetValueAt(barIndex) - pocSells.GetValueAt(barIndex))
                            : volume;
                        DrawCenteredText(labelValue.ToString("0"), textFormat, x, y, solid ? backgroundBrush : textBrush);
                    }
                    else
                    {
                        RenderTarget.DrawLine(new SharpDX.Vector2(left - 2, y), new SharpDX.Vector2(right + 2, y), lines[color], 3f);
                    }

                    if (ShowBarStats)
                    {
                        double delta = barDelta.GetValueAt(barIndex);
                        bool closedDown = Bars.GetClose(barIndex) < Bars.GetOpen(startIndex);
                        float volumeY = closedDown
                            ? chartScale.GetYByValue(GroupHigh(startIndex, barIndex)) - 4 - rowHeight / 2
                            : chartScale.GetYByValue(GroupLow(startIndex, barIndex)) + 4 + rowHeight / 2;
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
                backgroundBrush.Dispose();
                foreach (SharpDX.Direct2D1.Brush brush in fills.Values)
                    brush.Dispose();
                foreach (SharpDX.Direct2D1.Brush brush in lines.Values)
                    brush.Dispose();
            }
        }

        #region Properties

        [Display(Name = "POC period", Description = "Chart: one POC per candle. A longer period: one POC per window of that length on the exchange clock, spanning its candles (a 5-minute POC on a 1-minute chart). Session: one POC per trading session. Minute periods need a minute chart whose candle divides the period; otherwise it behaves as Chart.", Order = 0, GroupName = "2. POC")]
        public FlowPocPeriod PocPeriod { get; set; }

        [Range(1, 100), NinjaScriptProperty]
        [Display(Name = "Price grouping (ticks)", Description = "Ticks merged into one price level before finding the POC.", Order = 1, GroupName = "2. POC")]
        public int PriceGrouping { get; set; }

        [Range(1, 500), NinjaScriptProperty]
        [Display(Name = "Average candles", Description = "Candles in the moving average of POC volume.", Order = 2, GroupName = "1. When to mark")]
        public int AverageBars { get; set; }

        [Range(0.1, 20), NinjaScriptProperty]
        [Display(Name = "Square at (x baseline)", Description = "A POC gets a square when its strength beats the baseline times this; below it, a dash.", Order = 5, GroupName = "1. When to mark")]
        public double AverageMultiplier { get; set; }

        [Range(0, 100), NinjaScriptProperty]
        [Display(Name = "Neutral band (%)", Description = "POCs whose buy/sell imbalance is inside this band are drawn neutral.", Order = 2, GroupName = "2. POC")]
        public int NeutralBandPercent { get; set; }

        [Display(Name = "Strength baseline", Description = "RecentCandles: average of the previous candles' POCs. SameTimeOfDay: median POC volume at the same time in past sessions.", Order = 1, GroupName = "1. When to mark")]
        public FlowPocStrength StrengthMode { get; set; }

        [Display(Name = "Label", Description = "Delta: the lots left over at the POC price (buys minus sells, side shown by the color). Volume: all contracts traded at the POC price.", Order = 3, GroupName = "2. POC")]
        public FlowPocLabel PocLabel { get; set; }

        [Display(Name = "Strength measure", Description = "What is compared with the baseline: the POC's volume, or its leftover lots (buys minus sells, either side).", Order = 4, GroupName = "1. When to mark")]
        public FlowPocMeasure StrengthMeasure { get; set; }

        [Range(1, 250), NinjaScriptProperty]
        [Display(Name = "Sessions back", Description = "Past sessions in the same-time median. Needs that many days loaded.", Order = 3, GroupName = "1. When to mark")]
        public int SessionsBack { get; set; }

        [Display(Name = "Highlight absorption POCs", Description = "Ring a strong closed POC sitting at the candle's edge with the aggressor that lost: sellers at the bottom of a candle closing above it, or buyers at the top of one closing below it.", Order = 1, GroupName = "4. Absorption ring")]
        public bool HighlightAbsorption { get; set; }

        [Range(1, 50)]
        [Display(Name = "Absorption edge (%)", Description = "How close to the candle's low (or high) the POC must sit, as a share of the candle's range.", Order = 2, GroupName = "4. Absorption ring")]
        public int AbsorptionEdgePercent { get; set; }

        [Range(0, 50)]
        [Display(Name = "Outlier at (x baseline)", Description = "A POC at least this many times the baseline is an outlier: larger, solid, thick border. 0 turns it off.", Order = 6, GroupName = "1. When to mark")]
        public double OutlierMultiplier { get; set; }

        [Display(Name = "Marker", Order = 1, GroupName = "3. Markers")]
        public FlowPocMarker Marker { get; set; }

        [Range(6, 60)]
        [Display(Name = "Marker size", Order = 2, GroupName = "3. Markers")]
        public int MarkerSize { get; set; }

        [Range(0, 100)]
        [Display(Name = "Fill opacity (%)", Order = 3, GroupName = "3. Markers")]
        public int FillOpacity { get; set; }

        [Display(Name = "Show volume and delta", Description = "Print each candle's volume and delta (each window's, with a POC period) at its opening end: above a falling candle, below a rising one.", Order = 8, GroupName = "3. Markers")]
        public bool ShowBarStats { get; set; }

        [Range(100, 300)]
        [Display(Name = "Outlier size (%)", Description = "Outlier markers relative to the normal marker size.", Order = 4, GroupName = "3. Markers")]
        public int OutlierSizePercent { get; set; }

        [Display(Name = "Outlier solid fill", Description = "Fill outlier markers with the full side color instead of the fill opacity.", Order = 5, GroupName = "3. Markers")]
        public bool OutlierSolidFill { get; set; }

        [Range(0, 10)]
        [Display(Name = "Outlier border width", Description = "Border of outlier markers, in the text color. 0 hides it.", Order = 6, GroupName = "3. Markers")]
        public int OutlierBorderWidth { get; set; }

        [Display(Name = "Font", Order = 7, GroupName = "3. Markers")]
        public SimpleFont LabelFont { get; set; }

        [XmlIgnore]
        [Display(Name = "Buyer color", Order = 1, GroupName = "5. Colors")]
        public System.Windows.Media.Brush BuyColor { get; set; }

        [Browsable(false)]
        public string BuyColorSerializable
        {
            get { return Serialize.BrushToString(BuyColor); }
            set { BuyColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Seller color", Order = 2, GroupName = "5. Colors")]
        public System.Windows.Media.Brush SellColor { get; set; }

        [Browsable(false)]
        public string SellColorSerializable
        {
            get { return Serialize.BrushToString(SellColor); }
            set { SellColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Neutral color", Order = 3, GroupName = "5. Colors")]
        public System.Windows.Media.Brush NeutralColor { get; set; }

        [Browsable(false)]
        public string NeutralColorSerializable
        {
            get { return Serialize.BrushToString(NeutralColor); }
            set { NeutralColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Ring color", Order = 3, GroupName = "4. Absorption ring")]
        public System.Windows.Media.Brush AbsorptionColor { get; set; }

        [Browsable(false)]
        public string AbsorptionColorSerializable
        {
            get { return Serialize.BrushToString(AbsorptionColor); }
            set { AbsorptionColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Text color", Order = 4, GroupName = "5. Colors")]
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
