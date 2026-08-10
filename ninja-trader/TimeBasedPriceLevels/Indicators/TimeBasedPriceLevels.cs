#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    public enum PriceLevelTimeZone
    {
        Exchange,
        Utc,
        NewYork,
        Chicago,
        London,
        Berlin,
        Tokyo,
        Sydney
    }

    public enum PriceLevelPriceType
    {
        Open,
        High,
        Low,
        Close
    }

    /// <summary>
    /// Horizontal lines at up to 10 key price levels, each anchored to either an
    /// intraday clock time (the O/H/L/C of the bar at that time, one line per day)
    /// or a higher-timeframe period (the developing D/W/M/3M/6M/12M O/H/L/C).
    /// Port of the TradingView "Time-Based Price Levels".
    /// </summary>
    public class TimeBasedPriceLevels : Indicator
    {
        private enum HtfPeriod
        {
            Day,
            Week,
            Month,
            Quarter,
            HalfYear,
            Year
        }

        // One parsed slot. Intraday slots carry MinutesOfDay; HTF slots carry Period.
        private class LevelSlot
        {
            public bool IsHtf;
            public int MinutesOfDay;
            public HtfPeriod Period;
            public PriceLevelPriceType PriceType;
            public Stroke Stroke;
            public string LabelText;

            // HTF running state: the developing period's OHLC, reset on period change.
            public DateTime PeriodKey = DateTime.MinValue;
            public double Open;
            public double High;
            public double Low;
            public double Close;
            public DateTime PeriodStartChartTime;

            // Intraday capture bookkeeping.
            public DateTime LastCapturedDate = DateTime.MinValue;
            public List<LevelRecord> Records = new List<LevelRecord>();
        }

        private class LevelRecord
        {
            public DateTime TzDate;
            public double Price;
            public DateTime StartChartTime;
            public DateTime EndChartTime;
            public int CaptureBar;
        }

        // Mutated on the data thread, read on the render thread.
        private readonly object sync = new object();
        private readonly List<LevelSlot> slots = new List<LevelSlot>();
        private TimeZoneInfo displayZone;
        private DateTime lastSeenTzDate = DateTime.MinValue;
        private SessionIterator sessionIterator;

        #region Properties

        [Display(Name = "Time Zone", GroupName = "General", Order = 1,
                 Description = "Zone every slot time below is read in. Exchange means the instrument's own trading-hours zone.")]
        public PriceLevelTimeZone TimeZoneSelection { get; set; }

        [Range(0, 30)]
        [Display(Name = "Days of History to Show", GroupName = "General", Order = 2,
                 Description = "How many previous days keep their intraday lines. 0 = current day only.")]
        public int DaysToShow { get; set; }

        [Display(Name = "Show Labels", GroupName = "General", Order = 3)]
        public bool ShowLabels { get; set; }

        [Display(Name = "Level 1", GroupName = "Levels", Order = 10)]
        public bool Level1Enabled { get; set; }
        [Display(Name = "Time/Period 1", GroupName = "Levels", Order = 11, Description = "HH:MM, or D, W, M, 3M, 6M, 12M.")]
        public string Level1Time { get; set; }
        [Display(Name = "Price 1", GroupName = "Levels", Order = 12)]
        public PriceLevelPriceType Level1Price { get; set; }
        [Display(Name = "Style 1", GroupName = "Levels", Order = 13)]
        public Stroke Level1Stroke { get; set; }

        [Display(Name = "Level 2", GroupName = "Levels", Order = 20)]
        public bool Level2Enabled { get; set; }
        [Display(Name = "Time/Period 2", GroupName = "Levels", Order = 21)]
        public string Level2Time { get; set; }
        [Display(Name = "Price 2", GroupName = "Levels", Order = 22)]
        public PriceLevelPriceType Level2Price { get; set; }
        [Display(Name = "Style 2", GroupName = "Levels", Order = 23)]
        public Stroke Level2Stroke { get; set; }

        [Display(Name = "Level 3", GroupName = "Levels", Order = 30)]
        public bool Level3Enabled { get; set; }
        [Display(Name = "Time/Period 3", GroupName = "Levels", Order = 31)]
        public string Level3Time { get; set; }
        [Display(Name = "Price 3", GroupName = "Levels", Order = 32)]
        public PriceLevelPriceType Level3Price { get; set; }
        [Display(Name = "Style 3", GroupName = "Levels", Order = 33)]
        public Stroke Level3Stroke { get; set; }

        [Display(Name = "Level 4", GroupName = "Levels", Order = 40)]
        public bool Level4Enabled { get; set; }
        [Display(Name = "Time/Period 4", GroupName = "Levels", Order = 41)]
        public string Level4Time { get; set; }
        [Display(Name = "Price 4", GroupName = "Levels", Order = 42)]
        public PriceLevelPriceType Level4Price { get; set; }
        [Display(Name = "Style 4", GroupName = "Levels", Order = 43)]
        public Stroke Level4Stroke { get; set; }

        [Display(Name = "Level 5", GroupName = "Levels", Order = 50)]
        public bool Level5Enabled { get; set; }
        [Display(Name = "Time/Period 5", GroupName = "Levels", Order = 51)]
        public string Level5Time { get; set; }
        [Display(Name = "Price 5", GroupName = "Levels", Order = 52)]
        public PriceLevelPriceType Level5Price { get; set; }
        [Display(Name = "Style 5", GroupName = "Levels", Order = 53)]
        public Stroke Level5Stroke { get; set; }

        [Display(Name = "Level 6", GroupName = "Levels", Order = 60)]
        public bool Level6Enabled { get; set; }
        [Display(Name = "Time/Period 6", GroupName = "Levels", Order = 61)]
        public string Level6Time { get; set; }
        [Display(Name = "Price 6", GroupName = "Levels", Order = 62)]
        public PriceLevelPriceType Level6Price { get; set; }
        [Display(Name = "Style 6", GroupName = "Levels", Order = 63)]
        public Stroke Level6Stroke { get; set; }

        [Display(Name = "Level 7", GroupName = "Levels", Order = 70)]
        public bool Level7Enabled { get; set; }
        [Display(Name = "Time/Period 7", GroupName = "Levels", Order = 71)]
        public string Level7Time { get; set; }
        [Display(Name = "Price 7", GroupName = "Levels", Order = 72)]
        public PriceLevelPriceType Level7Price { get; set; }
        [Display(Name = "Style 7", GroupName = "Levels", Order = 73)]
        public Stroke Level7Stroke { get; set; }

        [Display(Name = "Level 8", GroupName = "Levels", Order = 80)]
        public bool Level8Enabled { get; set; }
        [Display(Name = "Time/Period 8", GroupName = "Levels", Order = 81)]
        public string Level8Time { get; set; }
        [Display(Name = "Price 8", GroupName = "Levels", Order = 82)]
        public PriceLevelPriceType Level8Price { get; set; }
        [Display(Name = "Style 8", GroupName = "Levels", Order = 83)]
        public Stroke Level8Stroke { get; set; }

        [Display(Name = "Level 9", GroupName = "Levels", Order = 90)]
        public bool Level9Enabled { get; set; }
        [Display(Name = "Time/Period 9", GroupName = "Levels", Order = 91)]
        public string Level9Time { get; set; }
        [Display(Name = "Price 9", GroupName = "Levels", Order = 92)]
        public PriceLevelPriceType Level9Price { get; set; }
        [Display(Name = "Style 9", GroupName = "Levels", Order = 93)]
        public Stroke Level9Stroke { get; set; }

        [Display(Name = "Level 10", GroupName = "Levels", Order = 100)]
        public bool Level10Enabled { get; set; }
        [Display(Name = "Time/Period 10", GroupName = "Levels", Order = 101)]
        public string Level10Time { get; set; }
        [Display(Name = "Price 10", GroupName = "Levels", Order = 102)]
        public PriceLevelPriceType Level10Price { get; set; }
        [Display(Name = "Style 10", GroupName = "Levels", Order = 103)]
        public Stroke Level10Stroke { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Horizontal lines at key price levels: intraday clock times or developing D/W/M/3M/6M/12M values.";
                Name = "Time-Based Price Levels";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                TimeZoneSelection = PriceLevelTimeZone.NewYork;
                DaysToShow = 0;
                ShowLabels = true;

                Level1Enabled = true;
                Level1Time = "00:00";
                Level2Enabled = true;
                Level2Time = "08:30";
                Level3Enabled = true;
                Level3Time = "09:30";
                Level4Enabled = false;
                Level4Time = "13:30";
                Level5Enabled = false;
                Level5Time = "16:00";
                Level6Enabled = false;
                Level6Time = "D";
                Level7Enabled = true;
                Level7Time = "W";
                Level8Enabled = true;
                Level8Time = "M";
                Level9Enabled = false;
                Level9Time = "3M";
                Level10Enabled = false;
                Level10Time = "12M";

                Level1Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level2Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level3Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level4Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level5Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level6Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level7Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level8Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level9Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
                Level10Stroke = new Stroke(Brushes.Black, DashStyleHelper.Dash, 1f);
            }
            else if (State == State.DataLoaded)
            {
                displayZone = ResolveDisplayZone();
                sessionIterator = new SessionIterator(Bars);
                slots.Clear();
                AddSlot(Level1Enabled, Level1Time, Level1Price, Level1Stroke);
                AddSlot(Level2Enabled, Level2Time, Level2Price, Level2Stroke);
                AddSlot(Level3Enabled, Level3Time, Level3Price, Level3Stroke);
                AddSlot(Level4Enabled, Level4Time, Level4Price, Level4Stroke);
                AddSlot(Level5Enabled, Level5Time, Level5Price, Level5Stroke);
                AddSlot(Level6Enabled, Level6Time, Level6Price, Level6Stroke);
                AddSlot(Level7Enabled, Level7Time, Level7Price, Level7Stroke);
                AddSlot(Level8Enabled, Level8Time, Level8Price, Level8Stroke);
                AddSlot(Level9Enabled, Level9Time, Level9Price, Level9Stroke);
                AddSlot(Level10Enabled, Level10Time, Level10Price, Level10Stroke);
            }
        }

        private TimeZoneInfo ResolveDisplayZone()
        {
            try
            {
                switch (TimeZoneSelection)
                {
                    case PriceLevelTimeZone.Utc: return TimeZoneInfo.Utc;
                    case PriceLevelTimeZone.NewYork: return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    case PriceLevelTimeZone.Chicago: return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                    case PriceLevelTimeZone.London: return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                    case PriceLevelTimeZone.Berlin: return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
                    case PriceLevelTimeZone.Tokyo: return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                    case PriceLevelTimeZone.Sydney: return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
                    default: return Bars.TradingHours.TimeZoneInfo;
                }
            }
            catch (TimeZoneNotFoundException)
            {
                return Core.Globals.GeneralOptions.TimeZoneInfo;
            }
        }

        private void AddSlot(bool enabled, string timeText, PriceLevelPriceType priceType, Stroke stroke)
        {
            if (!enabled)
                return;

            string trimmed = (timeText ?? string.Empty).Trim();
            string priceLetter = priceType.ToString().Substring(0, 1);
            LevelSlot slot = new LevelSlot { PriceType = priceType, Stroke = stroke };

            switch (trimmed.ToUpperInvariant())
            {
                case "D": slot.IsHtf = true; slot.Period = HtfPeriod.Day; slot.LabelText = "D." + priceLetter; break;
                case "W": slot.IsHtf = true; slot.Period = HtfPeriod.Week; slot.LabelText = "W." + priceLetter; break;
                case "M": slot.IsHtf = true; slot.Period = HtfPeriod.Month; slot.LabelText = "M." + priceLetter; break;
                case "3M": slot.IsHtf = true; slot.Period = HtfPeriod.Quarter; slot.LabelText = "Q." + priceLetter; break;
                case "6M": slot.IsHtf = true; slot.Period = HtfPeriod.HalfYear; slot.LabelText = "S." + priceLetter; break;
                case "12M": slot.IsHtf = true; slot.Period = HtfPeriod.Year; slot.LabelText = "Y." + priceLetter; break;
                default:
                    int minutes = ParseClockMinutes(trimmed);
                    if (minutes < 0)
                    {
                        Log("Time-Based Price Levels: skipping \"" + timeText + "\", expected HH:MM or one of D, W, M, 3M, 6M, 12M.", LogLevel.Warning);
                        return;
                    }
                    slot.MinutesOfDay = minutes;
                    slot.LabelText = priceType == PriceLevelPriceType.Open ? trimmed : trimmed + " " + priceLetter;
                    break;
            }
            slots.Add(slot);
        }

        // Reads HH:MM into minutes since midnight, or -1 when the entry is not a
        // valid clock time. Guarding here keeps a typo in one slot from taking the
        // whole indicator down.
        private static int ParseClockMinutes(string timeText)
        {
            string[] parts = timeText.Split(':');
            int hour, minute;
            if (parts.Length != 2 || !int.TryParse(parts[0], out hour) || !int.TryParse(parts[1], out minute))
                return -1;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
                return -1;
            return hour * 60 + minute;
        }

        // A clock time that does not exist on a daylight-saving switch day is
        // nudged forward an hour instead of throwing inside ConvertTime.
        private static DateTime SafeConvert(DateTime unspecified, TimeZoneInfo from, TimeZoneInfo to)
        {
            if (from.IsInvalidTime(unspecified))
                unspecified = unspecified.AddHours(1);
            return TimeZoneInfo.ConvertTime(unspecified, from, to);
        }

        // Keys are derived from the instrument's TRADING day, not the calendar
        // date of the bar: on an overnight instrument the Sunday 18:00 session
        // already belongs to Monday's trading day, so the daily level rolls at
        // the session open and the weekly level opens with the Sunday session --
        // matching how the exchange (and Pine's D/W series) cut the periods.
        private static DateTime PeriodKeyFor(DateTime tradingDay, HtfPeriod period)
        {
            switch (period)
            {
                case HtfPeriod.Week:
                    return tradingDay.AddDays(-(((int)tradingDay.DayOfWeek + 6) % 7));
                case HtfPeriod.Month: return new DateTime(tradingDay.Year, tradingDay.Month, 1);
                case HtfPeriod.Quarter: return new DateTime(tradingDay.Year, ((tradingDay.Month - 1) / 3) * 3 + 1, 1);
                case HtfPeriod.HalfYear: return new DateTime(tradingDay.Year, tradingDay.Month <= 6 ? 1 : 7, 1);
                case HtfPeriod.Year: return new DateTime(tradingDay.Year, 1, 1);
                default: return tradingDay;
            }
        }

        protected override void OnBarUpdate()
        {
            if (displayZone == null)
                return;

            DateTime chartZoneTime = Time[0];
            DateTime tzTime = TimeZoneInfo.ConvertTime(chartZoneTime, Core.Globals.GeneralOptions.TimeZoneInfo, displayZone);
            DateTime tzDate = tzTime.Date;
            // The previous bar's end, for the span test below; the first bar of
            // the stream has nothing before it and can prove no span.
            DateTime previousTzTime = CurrentBar > 0
                ? TimeZoneInfo.ConvertTime(Time[1], Core.Globals.GeneralOptions.TimeZoneInfo, displayZone)
                : DateTime.MaxValue;

            lock (sync)
            {
                if (tzDate != lastSeenTzDate)
                {
                    lastSeenTzDate = tzDate;
                    DropExpiredRecords(tzDate);
                }

                DateTime tradingDay = sessionIterator.GetTradingDay(Time[0]);
                foreach (LevelSlot slot in slots)
                {
                    if (slot.IsHtf)
                        UpdateHtfSlot(slot, tradingDay, chartZoneTime);
                    else
                        UpdateIntradaySlot(slot, tzTime, previousTzTime, tzDate, chartZoneTime);
                }
            }
        }

        private void DropExpiredRecords(DateTime currentTzDate)
        {
            foreach (LevelSlot slot in slots)
                slot.Records.RemoveAll(r => (currentTzDate - r.TzDate).TotalDays > DaysToShow);
        }

        private void UpdateHtfSlot(LevelSlot slot, DateTime tradingDay, DateTime chartZoneTime)
        {
            DateTime key = PeriodKeyFor(tradingDay, slot.Period);
            if (key != slot.PeriodKey)
            {
                slot.PeriodKey = key;
                slot.Open = Open[0];
                slot.High = High[0];
                slot.Low = Low[0];
                slot.Close = Close[0];
                slot.PeriodStartChartTime = chartZoneTime;
            }
            else
            {
                slot.High = Math.Max(slot.High, High[0]);
                slot.Low = Math.Min(slot.Low, Low[0]);
                slot.Close = Close[0];
            }
        }

        // Captures the bar that spans the slot's clock time each day (previous
        // bar ended at or before it, this bar ends after it), one line per day.
        // Unlike the Pine original, which required a bar whose stamp matched the
        // minute exactly (and so drew nothing on coarse timeframes), any
        // timeframe catches its level here; on a 1-minute chart the captured bar
        // is exactly the one opening at the slot time. A bar that spans a session
        // gap still captures -- its open is the first traded price after the
        // slot moment.
        private void UpdateIntradaySlot(LevelSlot slot, DateTime tzTime, DateTime previousTzTime, DateTime tzDate, DateTime chartZoneTime)
        {
            // A bar that crosses midnight ends on the next date, but a late slot
            // it spans still belongs to the day it started on: a 4-hour bar
            // ending 02:00 is the one that spans yesterday's 23:00. Both
            // candidate days are tested; at most one of the two slot moments can
            // fall inside any one bar.
            if (previousTzTime != DateTime.MaxValue && previousTzTime.Date != tzDate)
                TryCapture(slot, previousTzTime.Date, tzTime, previousTzTime, chartZoneTime);
            TryCapture(slot, tzDate, tzTime, previousTzTime, chartZoneTime);

            // High/Low/Close of the capture bar keep forming until it closes.
            if (slot.Records.Count > 0)
            {
                LevelRecord last = slot.Records[slot.Records.Count - 1];
                if (last.CaptureBar == CurrentBar)
                    last.Price = PriceForType(slot.PriceType);
            }
        }

        private void TryCapture(LevelSlot slot, DateTime day, DateTime tzTime, DateTime previousTzTime, DateTime chartZoneTime)
        {
            DateTime slotMoment = day.AddMinutes(slot.MinutesOfDay);
            if (slot.LastCapturedDate == day || tzTime <= slotMoment || previousTzTime > slotMoment)
                return;

            slot.LastCapturedDate = day;
            slot.Records.Add(new LevelRecord
            {
                TzDate = day,
                Price = PriceForType(slot.PriceType),
                StartChartTime = chartZoneTime,
                EndChartTime = SafeConvert(day.AddDays(1), displayZone, Core.Globals.GeneralOptions.TimeZoneInfo),
                CaptureBar = CurrentBar
            });
        }

        private double PriceForType(PriceLevelPriceType priceType)
        {
            switch (priceType)
            {
                case PriceLevelPriceType.High: return High[0];
                case PriceLevelPriceType.Low: return Low[0];
                case PriceLevelPriceType.Close: return Close[0];
                default: return Open[0];
            }
        }

        private double HtfPrice(LevelSlot slot)
        {
            switch (slot.PriceType)
            {
                case PriceLevelPriceType.High: return slot.High;
                case PriceLevelPriceType.Low: return slot.Low;
                case PriceLevelPriceType.Close: return slot.Close;
                default: return slot.Open;
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || Bars.Count == 0 || ChartControl == null || ChartPanel == null || RenderTarget == null || displayZone == null)
                return;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
            DateTime lastBarTime = Bars.GetTime(Bars.Count - 1);
            int lastBarX = chartControl.GetXByTime(lastBarTime);

            SimpleFont wpfFont = chartControl.Properties.LabelFont ?? new SimpleFont();
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
            try
            {
                textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
                lock (sync)
                {
                    foreach (LevelSlot slot in slots)
                    {
                        slot.Stroke.RenderTarget = RenderTarget;
                        if (slot.IsHtf)
                        {
                            if (slot.PeriodKey == DateTime.MinValue)
                                continue;
                            DrawLevel(chartControl, chartScale, textFormat, slot,
                                HtfPrice(slot), slot.PeriodStartChartTime, lastBarTime, lastBarX, true);
                        }
                        else
                            foreach (LevelRecord record in slot.Records)
                            {
                                bool reachesLastBar = record.EndChartTime >= lastBarTime;
                                DrawLevel(chartControl, chartScale, textFormat, slot,
                                    record.Price, record.StartChartTime,
                                    reachesLastBar ? lastBarTime : record.EndChartTime,
                                    lastBarX, reachesLastBar);
                            }
                    }
                }
            }
            finally
            {
                textFormat.Dispose();
            }
        }

        private void DrawLevel(ChartControl chartControl, ChartScale chartScale, SharpDX.DirectWrite.TextFormat textFormat,
            LevelSlot slot, double price, DateTime startTime, DateTime endTime, int lastBarX, bool extendPastLastBar)
        {
            float y = chartScale.GetYByValue(price);
            if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
                return;

            float startX = Math.Max(chartControl.GetXByTime(startTime), ChartPanel.X);
            // A still-running line pokes a little past the last bar so its label
            // clears the price action; a finished day ends at its own midnight.
            float endX = extendPastLastBar ? lastBarX + 8 : chartControl.GetXByTime(endTime);
            endX = Math.Min(endX, ChartPanel.X + ChartPanel.W);
            if (endX <= startX)
                return;

            RenderTarget.DrawLine(
                new SharpDX.Vector2(startX, y),
                new SharpDX.Vector2(endX, y),
                slot.Stroke.BrushDX, slot.Stroke.Width, slot.Stroke.StrokeStyle);

            if (!ShowLabels)
                return;

            SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, slot.LabelText, textFormat, 600, textFormat.FontSize);
            try
            {
                RenderTarget.DrawTextLayout(
                    new SharpDX.Vector2(endX + 4, y - layout.Metrics.Height / 2),
                    layout, slot.Stroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
            finally
            {
                layout.Dispose();
            }
        }
    }
}
