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

    public enum FlowBigTradesFilter { Manual, Automatic }

    public enum FlowBigTradesIntensity { Low, Medium, Strong }

    /// <summary>
    /// Marks each candle's point of control (the most traded price) colored by
    /// the aggressor that dominated it, labels the strong ones with their
    /// contract count, and prints each candle's volume and delta. With a POC
    /// period set, the candles are grouped into clock-aligned windows of that
    /// many minutes and each window gets one POC spanning its candles, and one
    /// volume and delta.
    /// Large aggressive market orders show as circles, one per price. The
    /// exchange reports one big market order as many fills; consecutive fills
    /// on the same side at the same price within the aggregation window are put
    /// back together, so a sweep through several prices shows one circle per
    /// price, sized by its lots.
    /// </summary>
    public class OrderFlowToolKit : Indicator
    {
        private class LevelVolume
        {
            public double Buys;
            public double Sells;
            public double Total;
        }

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
        private bool hasOrder;
        private bool orderIsBuy;
        private int orderBar;
        private DateTime orderStartTime;
        private double orderLots;
        private double orderPrice;

        private class StrongPoc
        {
            public int GroupNumber;
            public int BarIndex;
            public double Price;
        }

        private class NakedPoc
        {
            public int BarIndex;
            public double Price;
            public int AggressorSide;
            public int TouchedAt = -1;
        }

        private class PocZone
        {
            public int StartBar;
            public double Low;
            public double High;
            public int LastSide;
            public int BrokenAt = -1;
        }

        private readonly List<StrongPoc> recentStrongPocs = new List<StrongPoc>();
        private readonly List<NakedPoc> nakedPocs = new List<NakedPoc>();
        private readonly List<PocZone> zones = new List<PocZone>();
        private readonly List<NakedPoc> untouchedPocs = new List<NakedPoc>();
        private readonly List<PocZone> openZones = new List<PocZone>();
        private int closedGroups;
        private int lastTrackedBar = -1;

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
                Description = "Each candle's point of control colored by aggressor, with contract counts on the strong ones, and large aggressive orders as circles. Needs Tick Replay for history.";
                Name = "OrderFlowToolKit";
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
                PocLabel = FlowPocLabel.Volume;
                StrengthMeasure = FlowPocMeasure.Volume;
                SessionsBack = 4;
                HighlightAbsorption = true;
                AbsorptionEdgePercent = 25;
                OutlierMultiplier = 0;
                OutlierSizePercent = 130;
                OutlierSolidFill = true;
                OutlierBorderWidth = 3;
                Marker = FlowPocMarker.Square;
                MarkerSize = 20;
                FillOpacity = 40;
                ShowBarStats = false;
                ShowNumbers = true;
                MarkerMatchesCandle = false;
                PocPeriod = FlowPocPeriod.Chart;

                ShowBigTrades = true;
                BigTradesFilter = FlowBigTradesFilter.Manual;
                MinimumLots = 30;
                MaximumLots = 0;
                BigTradesIntensity = FlowBigTradesIntensity.Medium;
                AggregationMilliseconds = 5;
                BubbleMinimumSize = 18;
                BubbleMaximumSize = 70;
                BubbleFillOpacity = 50;
                HollowBubbles = false;
                BigBuyColor = Brushes.DodgerBlue;
                BigSellColor = Brushes.Crimson;

                ShowNakedPocs = false;
                ShowPocZones = false;
                ZoneLookback = 30;
                ZoneMinimumPocs = 3;
                ZoneTicks = 10;
                ZoneBreakTicks = 4;
                ZoneOpacity = 20;
                ZoneColor = Rgb(0x64, 0x74, 0x8B);

                BuyColor = Rgb(0x00, 0x4D, 0x00);
                SellColor = Rgb(0xFF, 0x00, 0x00);
                NeutralColor = Rgb(0x80, 0x80, 0x80);
                AbsorptionColor = Rgb(0xB8, 0x86, 0x2B);
                TextColor = Brushes.Black;
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
                groupStarts = new Series<int>(this, MaximumBarsLookBack.Infinite);
            }
            else if (State == State.Realtime)
            {
                FlushOrder();
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
                    Draw.TextFixed(this, "FlowPocStrength", "OrderFlowToolKit: same time of day needs time-based bars; using recent candles", TextPosition.TopLeft);
                if (Calculate == Calculate.OnBarClose)
                    Draw.TextFixed(this, "FlowPocCalculate", "OrderFlowToolKit: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay)
                    Draw.TextFixed(this, "FlowPocTickReplay", "OrderFlowToolKit: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
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

            // Remove-last-bar charts reopen the same candle, so each closed candle is tracked once.
            while (lastTrackedBar < CurrentBar - 1 && (ShowNakedPocs || ShowPocZones))
            {
                lastTrackedBar++;
                TrackLevels(lastTrackedBar);
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
            if (CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;
            // Bid and ask updates arrive constantly, so a finished order shows without waiting for the next trade.
            if (hasOrder && (e.Time - orderStartTime).TotalMilliseconds > AggregationMilliseconds)
                FlushOrder();
            if (e.MarketDataType != MarketDataType.Last)
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

            if (ShowBigTrades)
                TrackOrder(e);
        }

        private void TrackOrder(MarketDataEventArgs e)
        {
            bool isBuy = e.Price >= e.Ask;
            bool isSell = !isBuy && e.Price <= e.Bid;
            if (!isBuy && !isSell)
                return;

            bool continuesOrder = hasOrder
                && orderIsBuy == isBuy
                && e.Price == orderPrice
                && (e.Time - orderStartTime).TotalMilliseconds <= AggregationMilliseconds;
            if (!continuesOrder)
            {
                FlushOrder();
                hasOrder = true;
                orderIsBuy = isBuy;
                orderBar = CurrentBar;
                orderStartTime = e.Time;
                orderLots = 0;
                orderPrice = e.Price;
            }
            orderLots += e.Volume;
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
                int storedFloor = BigTradesFilter == FlowBigTradesFilter.Manual ? MinimumLots : Math.Min(StoredFloor, MinimumLots);
                if (orderLots < storedFloor)
                    return;
                trades.Add(new BigTrade
                {
                    BarIndex = orderBar,
                    Price = orderPrice,
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
            double topShare;
            if (BigTradesIntensity == FlowBigTradesIntensity.Low)
                topShare = 0.01;
            else if (BigTradesIntensity == FlowBigTradesIntensity.Medium)
                topShare = 0.005;
            else
                topShare = 0.001;
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

        private void TrackLevels(int closedBar)
        {
            double close = Bars.GetClose(closedBar);
            double low = Bars.GetLow(closedBar);
            double high = Bars.GetHigh(closedBar);
            double groupTop = (PriceGrouping - 1) * TickSize;
            double halfTick = TickSize / 2;

            lock (zones)
            {
                foreach (NakedPoc naked in untouchedPocs)
                {
                    if (low <= naked.Price + groupTop + halfTick && high >= naked.Price - halfTick)
                        naked.TouchedAt = closedBar;
                }
                untouchedPocs.RemoveAll(naked => naked.TouchedAt >= 0);

                foreach (PocZone zone in openZones)
                {
                    int side = SideOfZone(zone, close);
                    if (side == 0)
                        continue;
                    if (zone.LastSide != 0 && side != zone.LastSide)
                    {
                        zone.BrokenAt = closedBar;
                        recentStrongPocs.RemoveAll(poc => poc.Price >= zone.Low && poc.Price <= zone.High);
                    }
                    zone.LastSide = side;
                }
                openZones.RemoveAll(zone => zone.BrokenAt >= 0);

                if (!IsGroupEnd(closedBar))
                    return;
                closedGroups++;
                if (!pocVolume.IsValidDataPointAt(closedBar) || !IsStrongPoc(closedBar))
                    return;

                double price = pocPrice.GetValueAt(closedBar);
                int groupStart = GroupStart(closedBar);
                int aggressorSide = AggressorSide(pocBuys.GetValueAt(closedBar), pocSells.GetValueAt(closedBar));
                if (ShowNakedPocs)
                {
                    NakedPoc formed = new NakedPoc { BarIndex = groupStart, Price = price, AggressorSide = aggressorSide };
                    nakedPocs.Add(formed);
                    untouchedPocs.Add(formed);
                }

                recentStrongPocs.RemoveAll(poc => poc.GroupNumber <= closedGroups - ZoneLookback);
                recentStrongPocs.Add(new StrongPoc { GroupNumber = closedGroups, BarIndex = groupStart, Price = price });
                List<StrongPoc> cluster = recentStrongPocs.FindAll(poc => Math.Abs(poc.Price - price) <= ZoneTicks * TickSize + halfTick);
                if (cluster.Count < ZoneMinimumPocs)
                    return;

                int startBar = closedBar;
                double zoneLow = price;
                double zoneHigh = price;
                foreach (StrongPoc poc in cluster)
                {
                    startBar = Math.Min(startBar, poc.BarIndex);
                    zoneLow = Math.Min(zoneLow, poc.Price);
                    zoneHigh = Math.Max(zoneHigh, poc.Price);
                }
                zoneHigh += groupTop;

                List<PocZone> overlapping = openZones.FindAll(zone => zone.Low <= zoneHigh && zone.High >= zoneLow);
                if (overlapping.Count == 0)
                {
                    PocZone created = new PocZone { StartBar = startBar, Low = zoneLow, High = zoneHigh };
                    created.LastSide = SideOfZone(created, close);
                    zones.Add(created);
                    openZones.Add(created);
                    return;
                }
                PocZone merged = overlapping[0];
                double previousLow = merged.Low;
                double previousHigh = merged.High;
                merged.StartBar = Math.Min(merged.StartBar, startBar);
                merged.Low = Math.Min(merged.Low, zoneLow);
                merged.High = Math.Max(merged.High, zoneHigh);
                for (int i = 1; i < overlapping.Count; i++)
                {
                    PocZone absorbed = overlapping[i];
                    merged.StartBar = Math.Min(merged.StartBar, absorbed.StartBar);
                    merged.Low = Math.Min(merged.Low, absorbed.Low);
                    merged.High = Math.Max(merged.High, absorbed.High);
                    zones.Remove(absorbed);
                    openZones.Remove(absorbed);
                }
                int closeSide = SideOfZone(merged, close);
                bool grewTowardLastSide = (merged.LastSide == 1 && merged.High > previousHigh) || (merged.LastSide == -1 && merged.Low < previousLow);
                if (closeSide != 0)
                    merged.LastSide = closeSide;
                else if (grewTowardLastSide)
                    merged.LastSide = 0;
            }
        }

        private int SideOfZone(PocZone zone, double close)
        {
            double margin = ZoneBreakTicks * TickSize + TickSize / 2;
            if (close > zone.High + margin)
                return 1;
            if (close < zone.Low - margin)
                return -1;
            return 0;
        }

        private bool IsStrongPoc(int barIndex)
        {
            double baseline = StrengthBaseline(barIndex);
            return baseline > 0 && PocStrength(barIndex) > baseline * AverageMultiplier;
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

                if (ShowPocZones)
                    DrawZones(chartControl, chartScale, barWidth);
                if (ShowNakedPocs)
                    DrawNakedPocs(chartControl, chartScale, lines);

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
                        float baseSize = MarkerMatchesCandle ? barWidth : MarkerSize;
                        float radius = baseSize / 2f * growth * (isOutlier ? OutlierSizePercent / 100f : 1f);
                        float ringRadius = radius + 4;
                        bool isAbsorption = HighlightAbsorption && IsAbsorptionPoc(startIndex, barIndex, aggressorSide);
                        bool solid = isOutlier && OutlierSolidFill;
                        SharpDX.Direct2D1.Brush markerFill = solid ? lines[color] : fills[color];
                        float borderWidth = isOutlier ? OutlierBorderWidth : 1f;
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
                        if (ShowNumbers)
                            DrawCenteredText(labelValue.ToString("0"), textFormat, x, y, solid ? backgroundBrush : textBrush);
                    }
                    else
                    {
                        RenderTarget.DrawLine(new SharpDX.Vector2(left - 2, y), new SharpDX.Vector2(right + 2, y), lines[color], 4f);
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

                if (ShowBigTrades)
                    DrawBigTrades(chartControl, chartScale, textFormat, textBrush);
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

        private void DrawBigTrades(ChartControl chartControl, ChartScale chartScale, SharpDX.DirectWrite.TextFormat textFormat, SharpDX.Direct2D1.Brush textBrush)
        {
            SharpDX.Direct2D1.Brush buyFill = BigBuyColor.ToDxBrush(RenderTarget, BubbleFillOpacity / 100f);
            SharpDX.Direct2D1.Brush sellFill = BigSellColor.ToDxBrush(RenderTarget, BubbleFillOpacity / 100f);
            SharpDX.Direct2D1.Brush buyLine = BigBuyColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush sellLine = BigSellColor.ToDxBrush(RenderTarget);
            try
            {
                lock (trades)
                {
                    double threshold = BigTradesFilter == FlowBigTradesFilter.Automatic ? AutomaticThreshold() : MinimumLots;
                    for (int i = FirstTradeAtOrAfter(trades, ChartBars.FromIndex); i < trades.Count; i++)
                    {
                        BigTrade trade = trades[i];
                        if (trade.BarIndex > ChartBars.ToIndex)
                            break;
                        if (trade.Lots < threshold || (MaximumLots > 0 && trade.Lots > MaximumLots))
                            continue;

                        float radius = (float)Math.Min(BubbleMaximumSize, BubbleMinimumSize * trade.Lots / threshold) / 2f;
                        float x = chartControl.GetXByBarIndex(ChartBars, trade.BarIndex);
                        float y = chartScale.GetYByValue(trade.Price);
                        SharpDX.Direct2D1.Ellipse bubble = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), radius, radius);
                        if (!HollowBubbles)
                            RenderTarget.FillEllipse(bubble, trade.IsBuy ? buyFill : sellFill);
                        RenderTarget.DrawEllipse(bubble, trade.IsBuy ? buyLine : sellLine, 1.5f);

                        if (ShowNumbers && radius * 2 >= LabelFont.Size * 1.6)
                            DrawCenteredText(trade.Lots.ToString("0"), textFormat, x, y, textBrush);
                    }
                }
            }
            finally
            {
                buyFill.Dispose();
                sellFill.Dispose();
                buyLine.Dispose();
                sellLine.Dispose();
            }
        }

        private float RightEdgeX(ChartControl chartControl, int endBar)
        {
            return endBar >= 0 ? chartControl.GetXByBarIndex(ChartBars, endBar) : ChartPanel.X + ChartPanel.W;
        }

        private void DrawNakedPocs(ChartControl chartControl, ChartScale chartScale, Dictionary<System.Windows.Media.Brush, SharpDX.Direct2D1.Brush> lines)
        {
            double groupOffset = (PriceGrouping - 1) * TickSize / 2;
            lock (zones)
            {
                foreach (NakedPoc naked in nakedPocs)
                {
                    if (naked.BarIndex > ChartBars.ToIndex || (naked.TouchedAt >= 0 && naked.TouchedAt < ChartBars.FromIndex))
                        continue;
                    float y = chartScale.GetYByValue(naked.Price + groupOffset);
                    float x1 = chartControl.GetXByBarIndex(ChartBars, naked.BarIndex);
                    RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(RightEdgeX(chartControl, naked.TouchedAt), y), lines[SideColor(naked.AggressorSide)], 1f);
                }
            }
        }

        private void DrawZones(ChartControl chartControl, ChartScale chartScale, float barWidth)
        {
            SharpDX.Direct2D1.Brush fill = ZoneColor.ToDxBrush(RenderTarget, ZoneOpacity / 100f);
            SharpDX.Direct2D1.Brush border = ZoneColor.ToDxBrush(RenderTarget);
            try
            {
                double threshold;
                lock (trades)
                    threshold = BigTradesFilter == FlowBigTradesFilter.Automatic ? AutomaticThreshold() : MinimumLots;
                lock (zones)
                {
                    foreach (PocZone zone in zones)
                    {
                        if (zone.StartBar > ChartBars.ToIndex || (zone.BrokenAt >= 0 && zone.BrokenAt < ChartBars.FromIndex))
                            continue;
                        float left = chartControl.GetXByBarIndex(ChartBars, zone.StartBar) - barWidth / 2;
                        float right = RightEdgeX(chartControl, zone.BrokenAt);
                        float top = chartScale.GetYByValue(zone.High + TickSize / 2);
                        float bottom = chartScale.GetYByValue(zone.Low - TickSize / 2);
                        if (bottom < ChartPanel.Y || top > ChartPanel.Y + ChartPanel.H)
                            continue;
                        SharpDX.RectangleF band = new SharpDX.RectangleF(left, top, right - left, bottom - top);
                        RenderTarget.FillRectangle(band, fill);
                        RenderTarget.DrawRectangle(band, border, HadBigTrade(zone, threshold) ? 2.5f : 1f);
                    }
                }
            }
            finally
            {
                fill.Dispose();
                border.Dispose();
            }
        }

        private bool HadBigTrade(PocZone zone, double threshold)
        {
            if (!ShowBigTrades)
                return false;
            lock (trades)
            {
                int lastBar = zone.BrokenAt >= 0 ? Math.Min(zone.BrokenAt, ChartBars.ToIndex) : ChartBars.ToIndex;
                for (int i = FirstTradeAtOrAfter(trades, Math.Max(zone.StartBar, ChartBars.FromIndex)); i < trades.Count; i++)
                {
                    BigTrade trade = trades[i];
                    if (trade.BarIndex > lastBar)
                        break;
                    bool qualifies = trade.Lots >= threshold && (MaximumLots == 0 || trade.Lots <= MaximumLots);
                    if (qualifies && trade.Price >= zone.Low && trade.Price <= zone.High)
                        return true;
                }
            }
            return false;
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

        [Range(2, 60)]
        [Display(Name = "Marker size", Description = "Square or circle size, in pixels. Ignored when the marker matches the candle width.", Order = 2, GroupName = "3. Markers")]
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

        [Display(Name = "Show numbers", Description = "Print contract counts inside POC markers and big-trade circles. Off leaves only the shapes.", Order = 9, GroupName = "3. Markers")]
        public bool ShowNumbers { get; set; }

        [Display(Name = "Marker matches candle width", Description = "Size strong-POC markers to the candle body's width, so they follow the chart's zoom.", Order = 10, GroupName = "3. Markers")]
        public bool MarkerMatchesCandle { get; set; }

        [Display(Name = "Show big trades", Description = "Circles at the prices where large aggressive market orders traded.", Order = 1, GroupName = "6. Big trades")]
        public bool ShowBigTrades { get; set; }

        [Display(Name = "Filter mode", Description = "Manual: show orders of at least Minimum lots. Automatic: show the largest orders seen on this chart, by Intensity.", Order = 2, GroupName = "6. Big trades")]
        public FlowBigTradesFilter BigTradesFilter { get; set; }

        [Range(1, 10000), NinjaScriptProperty]
        [Display(Name = "Minimum lots", Description = "Manual filter: smallest order shown. Also used by Automatic until enough orders have been seen.", Order = 3, GroupName = "6. Big trades")]
        public int MinimumLots { get; set; }

        [Display(Name = "Intensity", Description = "Automatic filter: Low shows the top 1% of orders by size, Medium the top 0.5%, Strong the top 0.1%.", Order = 4, GroupName = "6. Big trades")]
        public FlowBigTradesIntensity BigTradesIntensity { get; set; }

        [Range(0, 100000), NinjaScriptProperty]
        [Display(Name = "Maximum lots", Description = "Largest order shown, to split size bands across two copies. 0 turns it off.", Order = 5, GroupName = "6. Big trades")]
        public int MaximumLots { get; set; }

        [Range(0, 1000), NinjaScriptProperty]
        [Display(Name = "Aggregation (ms)", Description = "Fills on the same side at the same price arriving within this many milliseconds of the first one count as one order. 0 joins only fills with the same timestamp.", Order = 6, GroupName = "6. Big trades")]
        public int AggregationMilliseconds { get; set; }

        [Range(2, 200)]
        [Display(Name = "Circle minimum size", Description = "Circle diameter, in pixels, of an order at the threshold.", Order = 7, GroupName = "6. Big trades")]
        public int BubbleMinimumSize { get; set; }

        [Range(2, 400)]
        [Display(Name = "Circle maximum size", Description = "Largest circle diameter, in pixels; circles grow in proportion to lots.", Order = 8, GroupName = "6. Big trades")]
        public int BubbleMaximumSize { get; set; }

        [Range(0, 100)]
        [Display(Name = "Circle fill opacity (%)", Order = 9, GroupName = "6. Big trades")]
        public int BubbleFillOpacity { get; set; }

        [Display(Name = "Hollow circles", Description = "Outline only, no fill.", Order = 10, GroupName = "6. Big trades")]
        public bool HollowBubbles { get; set; }

        [Display(Name = "Show naked POCs", Description = "A thin line from each strong POC to the right, ending where price first trades back to it.", Order = 1, GroupName = "7. Levels")]
        public bool ShowNakedPocs { get; set; }

        [Display(Name = "Show POC zones", Description = "A band where strong POCs cluster, running right until price closes through it to the other side. The border thickens when a big trade printed inside it.", Order = 2, GroupName = "7. Levels")]
        public bool ShowPocZones { get; set; }

        [Range(2, 500)]
        [Display(Name = "Zone lookback (POCs)", Description = "Strong POCs among this many recent POCs (one per candle, or per window with a POC period) can join a zone.", Order = 3, GroupName = "7. Levels")]
        public int ZoneLookback { get; set; }

        [Range(2, 20)]
        [Display(Name = "Zone minimum POCs", Description = "Strong POCs needed within the zone width to start a zone.", Order = 4, GroupName = "7. Levels")]
        public int ZoneMinimumPocs { get; set; }

        [Range(1, 200)]
        [Display(Name = "Zone width (ticks)", Description = "How far a strong POC may sit from the newest one and still join its zone.", Order = 5, GroupName = "7. Levels")]
        public int ZoneTicks { get; set; }

        [Range(0, 100)]
        [Display(Name = "Zone break buffer (ticks)", Description = "A close must be this far beyond the zone to count as a break.", Order = 6, GroupName = "7. Levels")]
        public int ZoneBreakTicks { get; set; }

        [Range(0, 100)]
        [Display(Name = "Zone opacity (%)", Order = 7, GroupName = "7. Levels")]
        public int ZoneOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Zone color", Order = 7, GroupName = "5. Colors")]
        public System.Windows.Media.Brush ZoneColor { get; set; }

        [Browsable(false)]
        public string ZoneColorSerializable
        {
            get { return Serialize.BrushToString(ZoneColor); }
            set { ZoneColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Big buy color", Description = "Orders lifting the ask.", Order = 5, GroupName = "5. Colors")]
        public System.Windows.Media.Brush BigBuyColor { get; set; }

        [Browsable(false)]
        public string BigBuyColorSerializable
        {
            get { return Serialize.BrushToString(BigBuyColor); }
            set { BigBuyColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Big sell color", Description = "Orders hitting the bid.", Order = 6, GroupName = "5. Colors")]
        public System.Windows.Media.Brush BigSellColor { get; set; }

        [Browsable(false)]
        public string BigSellColorSerializable
        {
            get { return Serialize.BrushToString(BigSellColor); }
            set { BigSellColor = Serialize.StringToBrush(value); }
        }

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
