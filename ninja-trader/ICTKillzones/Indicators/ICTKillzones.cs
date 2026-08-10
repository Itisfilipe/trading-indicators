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
    public enum KillzoneTimeZone
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

    /// <summary>
    /// A translucent box hugging each killzone's candles: from the window's
    /// first bar to its last, from its running high to its running low, growing
    /// with the session while the window is open. Eight zone slots; the defaults
    /// are the playbook's killzones for index futures, in New York time, with
    /// the session blocks carried but off.
    /// </summary>
    public class ICTKillzones : Indicator
    {
        private class ZoneRecord
        {
            public DateTime Day;
            public double High;
            public double Low;
            public DateTime StartChartTime;
            public DateTime LastBarChartTime;
        }

        // One parsed zone. EndMinutes may wrap past midnight (Asia 20:00-00:00);
        // a window is keyed to the day it STARTS on.
        private class ZoneSlot
        {
            public int StartMinutes;
            public int EndMinutes;
            public bool Wraps;
            public System.Windows.Media.Brush Color;
            public SharpDX.Direct2D1.Brush BrushDx;
            public List<ZoneRecord> Records = new List<ZoneRecord>();
        }

        // Mutated on the data thread, read on the render thread.
        private readonly object sync = new object();
        private readonly List<ZoneSlot> slots = new List<ZoneSlot>();
        private TimeZoneInfo displayZone;
        private DateTime lastSeenTzDate = DateTime.MinValue;

        #region Properties

        [Display(Name = "Time Zone", GroupName = "General", Order = 1,
                 Description = "Zone every window time below is read in. Exchange means the instrument's own trading-hours zone.")]
        public KillzoneTimeZone TimeZoneSelection { get; set; }

        [Range(0, 20)]
        [Display(Name = "Days of History to Show", GroupName = "General", Order = 2,
                 Description = "How many previous days keep their boxes. 0 = current day only.")]
        public int DaysToShow { get; set; }

        [Range(0, 100)]
        [Display(Name = "Fill Opacity (%)", GroupName = "General", Order = 3)]
        public int FillOpacity { get; set; }

        [Display(Name = "Zone 1", GroupName = "Zones", Order = 10)]
        public bool Zone1Enabled { get; set; }
        [Display(Name = "Zone 1 Name", GroupName = "Zones", Order = 11,
                 Description = "Reminder of what this slot marks; not drawn on the chart.")]
        public string Zone1Name { get; set; }
        [Display(Name = "Zone 1 Start", GroupName = "Zones", Order = 12)]
        public string Zone1Start { get; set; }
        [Display(Name = "Zone 1 End", GroupName = "Zones", Order = 13)]
        public string Zone1End { get; set; }
        [XmlIgnore]
        [Display(Name = "Zone 1 Color", GroupName = "Zones", Order = 14)]
        public Brush Zone1Color { get; set; }
        [Browsable(false)]
        public string Zone1ColorSerializable
        {
            get { return Serialize.BrushToString(Zone1Color); }
            set { Zone1Color = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Zone 2", GroupName = "Zones", Order = 20)]
        public bool Zone2Enabled { get; set; }
        [Display(Name = "Zone 2 Name", GroupName = "Zones", Order = 21)]
        public string Zone2Name { get; set; }
        [Display(Name = "Zone 2 Start", GroupName = "Zones", Order = 22)]
        public string Zone2Start { get; set; }
        [Display(Name = "Zone 2 End", GroupName = "Zones", Order = 23)]
        public string Zone2End { get; set; }
        [XmlIgnore]
        [Display(Name = "Zone 2 Color", GroupName = "Zones", Order = 24)]
        public Brush Zone2Color { get; set; }
        [Browsable(false)]
        public string Zone2ColorSerializable
        {
            get { return Serialize.BrushToString(Zone2Color); }
            set { Zone2Color = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Zone 3", GroupName = "Zones", Order = 30)]
        public bool Zone3Enabled { get; set; }
        [Display(Name = "Zone 3 Name", GroupName = "Zones", Order = 31)]
        public string Zone3Name { get; set; }
        [Display(Name = "Zone 3 Start", GroupName = "Zones", Order = 32)]
        public string Zone3Start { get; set; }
        [Display(Name = "Zone 3 End", GroupName = "Zones", Order = 33)]
        public string Zone3End { get; set; }
        [XmlIgnore]
        [Display(Name = "Zone 3 Color", GroupName = "Zones", Order = 34)]
        public Brush Zone3Color { get; set; }
        [Browsable(false)]
        public string Zone3ColorSerializable
        {
            get { return Serialize.BrushToString(Zone3Color); }
            set { Zone3Color = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Zone 4", GroupName = "Zones", Order = 40)]
        public bool Zone4Enabled { get; set; }
        [Display(Name = "Zone 4 Name", GroupName = "Zones", Order = 41)]
        public string Zone4Name { get; set; }
        [Display(Name = "Zone 4 Start", GroupName = "Zones", Order = 42)]
        public string Zone4Start { get; set; }
        [Display(Name = "Zone 4 End", GroupName = "Zones", Order = 43)]
        public string Zone4End { get; set; }
        [XmlIgnore]
        [Display(Name = "Zone 4 Color", GroupName = "Zones", Order = 44)]
        public Brush Zone4Color { get; set; }
        [Browsable(false)]
        public string Zone4ColorSerializable
        {
            get { return Serialize.BrushToString(Zone4Color); }
            set { Zone4Color = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Zone 5", GroupName = "Zones", Order = 50)]
        public bool Zone5Enabled { get; set; }
        [Display(Name = "Zone 5 Name", GroupName = "Zones", Order = 51)]
        public string Zone5Name { get; set; }
        [Display(Name = "Zone 5 Start", GroupName = "Zones", Order = 52)]
        public string Zone5Start { get; set; }
        [Display(Name = "Zone 5 End", GroupName = "Zones", Order = 53)]
        public string Zone5End { get; set; }
        [XmlIgnore]
        [Display(Name = "Zone 5 Color", GroupName = "Zones", Order = 54)]
        public Brush Zone5Color { get; set; }
        [Browsable(false)]
        public string Zone5ColorSerializable
        {
            get { return Serialize.BrushToString(Zone5Color); }
            set { Zone5Color = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Zone 6", GroupName = "Zones", Order = 60)]
        public bool Zone6Enabled { get; set; }
        [Display(Name = "Zone 6 Name", GroupName = "Zones", Order = 61)]
        public string Zone6Name { get; set; }
        [Display(Name = "Zone 6 Start", GroupName = "Zones", Order = 62)]
        public string Zone6Start { get; set; }
        [Display(Name = "Zone 6 End", GroupName = "Zones", Order = 63)]
        public string Zone6End { get; set; }
        [XmlIgnore]
        [Display(Name = "Zone 6 Color", GroupName = "Zones", Order = 64)]
        public Brush Zone6Color { get; set; }
        [Browsable(false)]
        public string Zone6ColorSerializable
        {
            get { return Serialize.BrushToString(Zone6Color); }
            set { Zone6Color = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Zone 7", GroupName = "Zones", Order = 70)]
        public bool Zone7Enabled { get; set; }
        [Display(Name = "Zone 7 Name", GroupName = "Zones", Order = 71)]
        public string Zone7Name { get; set; }
        [Display(Name = "Zone 7 Start", GroupName = "Zones", Order = 72)]
        public string Zone7Start { get; set; }
        [Display(Name = "Zone 7 End", GroupName = "Zones", Order = 73)]
        public string Zone7End { get; set; }
        [XmlIgnore]
        [Display(Name = "Zone 7 Color", GroupName = "Zones", Order = 74)]
        public Brush Zone7Color { get; set; }
        [Browsable(false)]
        public string Zone7ColorSerializable
        {
            get { return Serialize.BrushToString(Zone7Color); }
            set { Zone7Color = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Zone 8", GroupName = "Zones", Order = 80)]
        public bool Zone8Enabled { get; set; }
        [Display(Name = "Zone 8 Name", GroupName = "Zones", Order = 81)]
        public string Zone8Name { get; set; }
        [Display(Name = "Zone 8 Start", GroupName = "Zones", Order = 82)]
        public string Zone8Start { get; set; }
        [Display(Name = "Zone 8 End", GroupName = "Zones", Order = 83)]
        public string Zone8End { get; set; }
        [XmlIgnore]
        [Display(Name = "Zone 8 Color", GroupName = "Zones", Order = 84)]
        public Brush Zone8Color { get; set; }
        [Browsable(false)]
        public string Zone8ColorSerializable
        {
            get { return Serialize.BrushToString(Zone8Color); }
            set { Zone8Color = Serialize.StringToBrush(value); }
        }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "A translucent box around each killzone's candles, from the window's high to its low, growing while the window is open.";
                Name = "ICT Killzones";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                TimeZoneSelection = KillzoneTimeZone.NewYork;
                DaysToShow = 5;
                FillOpacity = 15;

                // The playbook's killzones for index futures, New York time; the
                // session blocks fill the remaining slots but start off.
                Zone1Enabled = true; Zone1Name = "Asia Range"; Zone1Start = "20:00"; Zone1End = "00:00";
                Zone2Enabled = true; Zone2Name = "London Open KZ"; Zone2Start = "02:00"; Zone2End = "04:00";
                Zone3Enabled = true; Zone3Name = "NY Open KZ"; Zone3Start = "07:00"; Zone3End = "09:00";
                Zone4Enabled = true; Zone4Name = "London Close KZ"; Zone4Start = "10:00"; Zone4End = "12:00";
                Zone5Enabled = false; Zone5Name = "AM Session"; Zone5Start = "09:30"; Zone5End = "11:30";
                Zone6Enabled = false; Zone6Name = "Lunch"; Zone6Start = "11:30"; Zone6End = "13:30";
                Zone7Enabled = false; Zone7Name = "PM Session"; Zone7Start = "13:30"; Zone7End = "16:00";
                Zone8Enabled = false; Zone8Name = "CBDR"; Zone8Start = "16:00"; Zone8End = "20:00";

                Zone1Color = Rgb(0x90, 0xCA, 0xF9); // soft blue
                Zone2Color = Rgb(0xA5, 0xD6, 0xA7); // soft green
                Zone3Color = Rgb(0xFF, 0xCC, 0x80); // soft orange
                Zone4Color = Rgb(0xEF, 0x9A, 0x9A); // soft red
                Zone5Color = Rgb(0x80, 0xCB, 0xC4); // soft teal
                Zone6Color = Rgb(0xB0, 0xBE, 0xC5); // soft gray
                Zone7Color = Rgb(0xB3, 0x9D, 0xDB); // soft purple
                Zone8Color = Rgb(0xFF, 0xF5, 0x9D); // soft yellow
            }
            else if (State == State.DataLoaded)
            {
                displayZone = ResolveDisplayZone();
                slots.Clear();
                AddZone(Zone1Enabled, Zone1Start, Zone1End, Zone1Color);
                AddZone(Zone2Enabled, Zone2Start, Zone2End, Zone2Color);
                AddZone(Zone3Enabled, Zone3Start, Zone3End, Zone3Color);
                AddZone(Zone4Enabled, Zone4Start, Zone4End, Zone4Color);
                AddZone(Zone5Enabled, Zone5Start, Zone5End, Zone5Color);
                AddZone(Zone6Enabled, Zone6Start, Zone6End, Zone6Color);
                AddZone(Zone7Enabled, Zone7Start, Zone7End, Zone7Color);
                AddZone(Zone8Enabled, Zone8Start, Zone8End, Zone8Color);
            }
            else if (State == State.Terminated)
            {
                DisposeDxBrushes();
            }
        }

        // Frozen because the render thread reads the brush's color; an unfrozen
        // WPF brush is bound to its creating thread.
        private static System.Windows.Media.Brush Rgb(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private TimeZoneInfo ResolveDisplayZone()
        {
            try
            {
                switch (TimeZoneSelection)
                {
                    case KillzoneTimeZone.Utc: return TimeZoneInfo.Utc;
                    case KillzoneTimeZone.NewYork: return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    case KillzoneTimeZone.Chicago: return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                    case KillzoneTimeZone.London: return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                    case KillzoneTimeZone.Berlin: return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
                    case KillzoneTimeZone.Tokyo: return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                    case KillzoneTimeZone.Sydney: return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
                    default: return Bars.TradingHours.TimeZoneInfo;
                }
            }
            catch (TimeZoneNotFoundException)
            {
                return Core.Globals.GeneralOptions.TimeZoneInfo;
            }
        }

        // Reads HH:MM into minutes since midnight, or -1 when the entry is not a
        // valid clock time. Guarding here keeps a typo in one slot from taking
        // the whole indicator down.
        private static int ParseClockMinutes(string timeText)
        {
            string[] parts = (timeText ?? string.Empty).Trim().Split(':');
            int hour, minute;
            if (parts.Length != 2 || !int.TryParse(parts[0], out hour) || !int.TryParse(parts[1], out minute))
                return -1;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
                return -1;
            return hour * 60 + minute;
        }

        private void AddZone(bool enabled, string startText, string endText, System.Windows.Media.Brush color)
        {
            if (!enabled)
                return;
            int startMinutes = ParseClockMinutes(startText);
            int endMinutes = ParseClockMinutes(endText);
            if (startMinutes < 0 || endMinutes < 0)
            {
                Log("ICT Killzones: skipping zone \"" + startText + "\" to \"" + endText + "\", expected times as HH:MM.", LogLevel.Warning);
                return;
            }
            // An end at or before the start means the window runs into the next
            // day (Asia 20:00-00:00). It stays keyed to the day it starts on.
            slots.Add(new ZoneSlot
            {
                StartMinutes = startMinutes,
                EndMinutes = endMinutes,
                Wraps = endMinutes <= startMinutes,
                Color = color
            });
        }

        protected override void OnBarUpdate()
        {
            if (displayZone == null)
                return;

            DateTime chartZoneTime = Time[0];
            DateTime tzTime = TimeZoneInfo.ConvertTime(chartZoneTime, Core.Globals.GeneralOptions.TimeZoneInfo, displayZone);
            // The capture bar's left edge is its open, which is the previous
            // bar's end; the first bar of the stream has no previous.
            DateTime leftEdgeChartTime = CurrentBar > 0 ? Time[1] : chartZoneTime;

            lock (sync)
            {
                if (tzTime.Date != lastSeenTzDate)
                {
                    lastSeenTzDate = tzTime.Date;
                    foreach (ZoneSlot slot in slots)
                        slot.Records.RemoveAll(r => (tzTime.Date - r.Day).TotalDays > DaysToShow);
                }

                foreach (ZoneSlot slot in slots)
                {
                    // A window is under 24 hours, so a bar can sit in at most one
                    // day's instance of it: today's, or -- when the window wraps
                    // past midnight -- the one that started yesterday.
                    if (!TryUpdateZoneDay(slot, tzTime.Date, tzTime, chartZoneTime, leftEdgeChartTime) && slot.Wraps)
                        TryUpdateZoneDay(slot, tzTime.Date.AddDays(-1), tzTime, chartZoneTime, leftEdgeChartTime);
                }
            }
        }

        // Folds the current bar into the given day's window instance if the bar
        // ends inside it, creating the record on the window's first bar. Returns
        // whether the bar belonged to that day's window.
        private bool TryUpdateZoneDay(ZoneSlot slot, DateTime day, DateTime tzTime, DateTime chartZoneTime, DateTime leftEdgeChartTime)
        {
            DateTime windowStart = day.AddMinutes(slot.StartMinutes);
            DateTime windowEnd = day.AddMinutes(slot.Wraps ? slot.EndMinutes + 1440 : slot.EndMinutes);
            if (tzTime <= windowStart || tzTime > windowEnd)
                return false;

            ZoneRecord record = slot.Records.Count > 0 ? slot.Records[slot.Records.Count - 1] : null;
            if (record == null || record.Day != day)
            {
                record = new ZoneRecord
                {
                    Day = day,
                    High = High[0],
                    Low = Low[0],
                    StartChartTime = leftEdgeChartTime,
                    LastBarChartTime = chartZoneTime
                };
                slot.Records.Add(record);
            }
            else
            {
                record.High = Math.Max(record.High, High[0]);
                record.Low = Math.Min(record.Low, Low[0]);
                record.LastBarChartTime = chartZoneTime;
            }
            return true;
        }

        public override void OnRenderTargetChanged()
        {
            DisposeDxBrushes();
        }

        private void DisposeDxBrushes()
        {
            lock (sync)
                foreach (ZoneSlot slot in slots)
                {
                    if (slot.BrushDx != null)
                    {
                        slot.BrushDx.Dispose();
                        slot.BrushDx = null;
                    }
                }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || Bars.Count == 0 || ChartControl == null || ChartPanel == null || RenderTarget == null)
                return;

            float panelLeft = ChartPanel.X;
            float panelRight = ChartPanel.X + ChartPanel.W;
            float panelTop = ChartPanel.Y;
            float panelBottom = ChartPanel.Y + ChartPanel.H;

            lock (sync)
                foreach (ZoneSlot slot in slots)
                {
                    // Fill brushes are built lazily: the render target can change
                    // before the slots exist, and the slots before the target.
                    if (slot.BrushDx == null)
                    {
                        System.Windows.Media.SolidColorBrush solid = slot.Color as System.Windows.Media.SolidColorBrush;
                        System.Windows.Media.Color c = solid != null ? solid.Color : Colors.Gray;
                        slot.BrushDx = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                            new SharpDX.Color(c.R, c.G, c.B, (byte)(255 * FillOpacity / 100)));
                    }

                    foreach (ZoneRecord record in slot.Records)
                    {
                        float x1 = Math.Max(chartControl.GetXByTime(record.StartChartTime), panelLeft);
                        float x2 = Math.Min(chartControl.GetXByTime(record.LastBarChartTime), panelRight);
                        // The box hugs its candles: highest high to lowest low of
                        // the bars inside the window, clamped to the panel so a
                        // zoomed-in chart does not paint over its neighbors.
                        float y1 = Math.Max(chartScale.GetYByValue(record.High), panelTop);
                        float y2 = Math.Min(chartScale.GetYByValue(record.Low), panelBottom);
                        if (x2 <= x1 || y2 <= y1)
                            continue;

                        RenderTarget.FillRectangle(new SharpDX.RectangleF(x1, y1, x2 - x1, y2 - y1), slot.BrushDx);
                    }
                }
        }
    }
}
