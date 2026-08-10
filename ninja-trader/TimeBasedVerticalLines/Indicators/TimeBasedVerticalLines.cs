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
    public enum TimeMarkTimeZone
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

    public enum TimeMarkLabelPosition
    {
        Top,
        Middle,
        Bottom
    }

    /// <summary>
    /// Vertical lines at the clock times that matter during a session -- New York
    /// midnight, the 09:30 open, a news release -- drawn for the whole day, so
    /// the ones still ahead are already on the chart with a countdown to them.
    /// Port of the TradingView "Time-Based Vertical Lines"; the macro windows
    /// that tool carries live in the ICTMacros indicator instead.
    /// </summary>
    public class TimeBasedVerticalLines : Indicator
    {
        // Distinct days kept in the log: comfortably more than the five the
        // history cutoff can ask for, so the oldest day still wanted is always
        // still in there.
        private const int MaxTrackedDays = 20;

        private class TimeMarker
        {
            public int MinutesOfDay;
            public string Caption;
            public Stroke Stroke;
        }

        private readonly List<TimeMarker> markers = new List<TimeMarker>();

        // Mutated on the data thread, read on the render thread.
        private readonly object sync = new object();
        private readonly List<DateTime> tradingDays = new List<DateTime>();

        private TimeZoneInfo displayZone;
        private System.Windows.Threading.DispatcherTimer refreshTimer;

        #region Properties

        [Display(Name = "Time Zone", GroupName = "General", Order = 1,
                 Description = "Zone every time below is read in. Exchange means the instrument's own trading-hours zone.")]
        public TimeMarkTimeZone TimeZoneSelection { get; set; }

        [Range(0, 5)]
        [Display(Name = "Days of History to Show", GroupName = "General", Order = 2,
                 Description = "How far back to keep drawing lines, counted from the last day the chart has bars for. 0 = that day and today.")]
        public int HistoryDays { get; set; }

        [Display(Name = "Include Tomorrow", GroupName = "General", Order = 3,
                 Description = "Also draw tomorrow's lines. Useful late in the session, when everything for today has already happened.")]
        public bool IncludeTomorrow { get; set; }

        [Display(Name = "Show Labels", GroupName = "Labels", Order = 1)]
        public bool ShowLabels { get; set; }

        [Display(Name = "Label Position", GroupName = "Labels", Order = 2,
                 Description = "Where on the line the label sits: near the top or bottom edge of the viewport, or the middle of it.")]
        public TimeMarkLabelPosition LabelPosition { get; set; }

        [Range(0.0, 40.0)]
        [Display(Name = "Edge Gap (%)", GroupName = "Labels", Order = 3,
                 Description = "Clear space between the panel edge and the captions, as a share of the panel height.")]
        public double EdgeGapPercent { get; set; }

        [Display(Name = "Countdown On Future Lines", GroupName = "Labels", Order = 4,
                 Description = "Add the time left to the label of a line whose time has not come yet.")]
        public bool ShowCountdown { get; set; }

        [Display(Name = "Line 1", GroupName = "Lines", Order = 10)]
        public bool Line1Enabled { get; set; }
        [Display(Name = "Line 1 Time", GroupName = "Lines", Order = 11)]
        public string Line1Time { get; set; }
        [Display(Name = "Line 1 Label", GroupName = "Lines", Order = 12)]
        public string Line1Text { get; set; }
        [Display(Name = "Line 1 Style", GroupName = "Lines", Order = 13)]
        public Stroke Line1Stroke { get; set; }

        [Display(Name = "Line 2", GroupName = "Lines", Order = 20)]
        public bool Line2Enabled { get; set; }
        [Display(Name = "Line 2 Time", GroupName = "Lines", Order = 21)]
        public string Line2Time { get; set; }
        [Display(Name = "Line 2 Label", GroupName = "Lines", Order = 22)]
        public string Line2Text { get; set; }
        [Display(Name = "Line 2 Style", GroupName = "Lines", Order = 23)]
        public Stroke Line2Stroke { get; set; }

        [Display(Name = "Line 3", GroupName = "Lines", Order = 30)]
        public bool Line3Enabled { get; set; }
        [Display(Name = "Line 3 Time", GroupName = "Lines", Order = 31)]
        public string Line3Time { get; set; }
        [Display(Name = "Line 3 Label", GroupName = "Lines", Order = 32)]
        public string Line3Text { get; set; }
        [Display(Name = "Line 3 Style", GroupName = "Lines", Order = 33)]
        public Stroke Line3Stroke { get; set; }

        [Display(Name = "Line 4", GroupName = "Lines", Order = 40)]
        public bool Line4Enabled { get; set; }
        [Display(Name = "Line 4 Time", GroupName = "Lines", Order = 41)]
        public string Line4Time { get; set; }
        [Display(Name = "Line 4 Label", GroupName = "Lines", Order = 42)]
        public string Line4Text { get; set; }
        [Display(Name = "Line 4 Style", GroupName = "Lines", Order = 43)]
        public Stroke Line4Stroke { get; set; }

        [Display(Name = "Line 5", GroupName = "Lines", Order = 50)]
        public bool Line5Enabled { get; set; }
        [Display(Name = "Line 5 Time", GroupName = "Lines", Order = 51)]
        public string Line5Time { get; set; }
        [Display(Name = "Line 5 Label", GroupName = "Lines", Order = 52)]
        public string Line5Text { get; set; }
        [Display(Name = "Line 5 Style", GroupName = "Lines", Order = 53)]
        public Stroke Line5Stroke { get; set; }

        [Display(Name = "Line 6", GroupName = "Lines", Order = 60)]
        public bool Line6Enabled { get; set; }
        [Display(Name = "Line 6 Time", GroupName = "Lines", Order = 61)]
        public string Line6Time { get; set; }
        [Display(Name = "Line 6 Label", GroupName = "Lines", Order = 62)]
        public string Line6Text { get; set; }
        [Display(Name = "Line 6 Style", GroupName = "Lines", Order = 63)]
        public Stroke Line6Stroke { get; set; }

        [Display(Name = "Line 7", GroupName = "Lines", Order = 70)]
        public bool Line7Enabled { get; set; }
        [Display(Name = "Line 7 Time", GroupName = "Lines", Order = 71)]
        public string Line7Time { get; set; }
        [Display(Name = "Line 7 Label", GroupName = "Lines", Order = 72)]
        public string Line7Text { get; set; }
        [Display(Name = "Line 7 Style", GroupName = "Lines", Order = 73)]
        public Stroke Line7Stroke { get; set; }

        [Display(Name = "Line 8", GroupName = "Lines", Order = 80)]
        public bool Line8Enabled { get; set; }
        [Display(Name = "Line 8 Time", GroupName = "Lines", Order = 81)]
        public string Line8Time { get; set; }
        [Display(Name = "Line 8 Label", GroupName = "Lines", Order = 82)]
        public string Line8Text { get; set; }
        [Display(Name = "Line 8 Style", GroupName = "Lines", Order = 83)]
        public Stroke Line8Stroke { get; set; }

        [Display(Name = "Line 9", GroupName = "Lines", Order = 90)]
        public bool Line9Enabled { get; set; }
        [Display(Name = "Line 9 Time", GroupName = "Lines", Order = 91)]
        public string Line9Time { get; set; }
        [Display(Name = "Line 9 Label", GroupName = "Lines", Order = 92)]
        public string Line9Text { get; set; }
        [Display(Name = "Line 9 Style", GroupName = "Lines", Order = 93)]
        public Stroke Line9Stroke { get; set; }

        [Display(Name = "Line 10", GroupName = "Lines", Order = 100)]
        public bool Line10Enabled { get; set; }
        [Display(Name = "Line 10 Time", GroupName = "Lines", Order = 101)]
        public string Line10Time { get; set; }
        [Display(Name = "Line 10 Label", GroupName = "Lines", Order = 102)]
        public string Line10Text { get; set; }
        [Display(Name = "Line 10 Style", GroupName = "Lines", Order = 103)]
        public Stroke Line10Stroke { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Vertical lines at the clock times that matter, drawn for the whole day with countdowns on the ones still ahead.";
                Name = "Time-Based Vertical Lines";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                TimeZoneSelection = TimeMarkTimeZone.NewYork;
                HistoryDays = 1;
                IncludeTomorrow = false;
                ShowLabels = true;
                LabelPosition = TimeMarkLabelPosition.Top;
                EdgeGapPercent = 2.0;
                ShowCountdown = true;

                Line1Enabled = true; Line1Time = "00:00"; Line1Text = "NY Midnight";
                Line2Enabled = true; Line2Time = "08:30"; Line2Text = "8:30 News";
                Line3Enabled = true; Line3Time = "09:30"; Line3Text = "NY Open";
                Line4Enabled = true; Line4Time = "16:00"; Line4Text = "NY Close";
                Line5Enabled = true; Line5Time = "18:00"; Line5Text = "Futures Open";
                Line6Enabled = false; Line6Time = "03:00"; Line6Text = "London Open";
                Line7Enabled = false; Line7Time = "11:30"; Line7Text = "London Close";
                Line8Enabled = false; Line8Time = "14:00"; Line8Text = "FOMC";
                Line9Enabled = false; Line9Time = "17:00"; Line9Text = "Session Break";
                Line10Enabled = false; Line10Time = "12:00"; Line10Text = "";

                Line1Stroke = new Stroke(Rgb(0x78, 0x7B, 0x86), DashStyleHelper.Dash, 1f);
                Line2Stroke = new Stroke(Rgb(0xEF, 0x53, 0x50), DashStyleHelper.Dot, 1f);
                Line3Stroke = new Stroke(Rgb(0x29, 0x62, 0xFF), DashStyleHelper.Solid, 1f);
                Line4Stroke = new Stroke(Rgb(0x29, 0x62, 0xFF), DashStyleHelper.Solid, 1f);
                Line5Stroke = new Stroke(Rgb(0x78, 0x7B, 0x86), DashStyleHelper.Dash, 1f);
                Line6Stroke = new Stroke(Rgb(0x26, 0xA6, 0x9A), DashStyleHelper.Dash, 1f);
                Line7Stroke = new Stroke(Rgb(0x26, 0xA6, 0x9A), DashStyleHelper.Dash, 1f);
                Line8Stroke = new Stroke(Rgb(0xEF, 0x53, 0x50), DashStyleHelper.Dot, 1f);
                Line9Stroke = new Stroke(Rgb(0x78, 0x7B, 0x86), DashStyleHelper.Dot, 1f);
                Line10Stroke = new Stroke(Rgb(0x78, 0x7B, 0x86), DashStyleHelper.Solid, 1f);
            }
            else if (State == State.DataLoaded)
            {
                displayZone = ResolveDisplayZone();
                markers.Clear();
                AddLine(Line1Enabled, Line1Time, Line1Text, Line1Stroke);
                AddLine(Line2Enabled, Line2Time, Line2Text, Line2Stroke);
                AddLine(Line3Enabled, Line3Time, Line3Text, Line3Stroke);
                AddLine(Line4Enabled, Line4Time, Line4Text, Line4Stroke);
                AddLine(Line5Enabled, Line5Time, Line5Text, Line5Stroke);
                AddLine(Line6Enabled, Line6Time, Line6Text, Line6Stroke);
                AddLine(Line7Enabled, Line7Time, Line7Text, Line7Stroke);
                AddLine(Line8Enabled, Line8Time, Line8Text, Line8Stroke);
                AddLine(Line9Enabled, Line9Time, Line9Text, Line9Stroke);
                AddLine(Line10Enabled, Line10Time, Line10Text, Line10Stroke);
            }
            else if (State == State.Realtime)
            {
                // Countdowns move with wall time, not with ticks, so a
                // once-a-second repaint keeps them honest through quiet tape.
                if (refreshTimer == null && ChartControl != null && ShowCountdown)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        // The indicator can be removed while this callback is
                        // still queued; a timer born after teardown would tick
                        // for a dead indicator until the chart closes.
                        if (State >= State.Terminated || refreshTimer != null)
                            return;
                        refreshTimer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(1),
                            IsEnabled = true
                        };
                        refreshTimer.Tick += (o, e) => ForceRefresh();
                    });
                }
            }
            else if (State == State.Terminated)
            {
                if (refreshTimer != null)
                {
                    refreshTimer.IsEnabled = false;
                    refreshTimer = null;
                }
            }
        }

        // Frozen because the render thread reads the brush's color; an unfrozen
        // WPF brush is bound to its creating thread. The colors are the
        // TradingView defaults, so both platforms draw the same chart.
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
                    case TimeMarkTimeZone.Utc: return TimeZoneInfo.Utc;
                    case TimeMarkTimeZone.NewYork: return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    case TimeMarkTimeZone.Chicago: return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                    case TimeMarkTimeZone.London: return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                    case TimeMarkTimeZone.Berlin: return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
                    case TimeMarkTimeZone.Tokyo: return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                    case TimeMarkTimeZone.Sydney: return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
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

        private void AddLine(bool enabled, string timeText, string caption, Stroke stroke)
        {
            if (!enabled)
                return;
            int minutes = ParseClockMinutes(timeText);
            if (minutes < 0)
            {
                Log("Time-Based Vertical Lines: skipping \"" + timeText + "\", expected a time as HH:MM.", LogLevel.Warning);
                return;
            }
            markers.Add(new TimeMarker { MinutesOfDay = minutes, Caption = caption ?? string.Empty, Stroke = stroke });
        }

        // mm:ss while under an hour, then "Xh MMm" -- the seconds stop being
        // useful once the line is that far out.
        private static string CountdownText(TimeSpan left)
        {
            if (left < TimeSpan.Zero)
                left = TimeSpan.Zero;
            return left.TotalHours >= 1
                ? string.Format("{0}h {1:00}m", (int)left.TotalHours, left.Minutes)
                : string.Format("{0:00}:{1:00}", left.Minutes, left.Seconds);
        }

        // Days are logged as the bars pass through them rather than counted back
        // from today, so a weekend or a holiday never collects a set of lines
        // nothing traded under.
        protected override void OnBarUpdate()
        {
            if (displayZone == null)
                return;
            DateTime tzDate = TimeZoneInfo.ConvertTime(Time[0], Core.Globals.GeneralOptions.TimeZoneInfo, displayZone).Date;
            lock (sync)
            {
                if (tradingDays.Count == 0 || tradingDays[tradingDays.Count - 1] != tzDate)
                {
                    tradingDays.Add(tzDate);
                    if (tradingDays.Count > MaxTrackedDays)
                        tradingDays.RemoveAt(0);
                }
            }
        }

        private DateTime PlatformNow()
        {
            return Connection.PlaybackConnection != null ? Connection.PlaybackConnection.Now : Core.Globals.Now;
        }

        // A clock time that does not exist on a daylight-saving switch day is
        // nudged forward an hour instead of throwing inside ConvertTime.
        private DateTime ToChartZone(DateTime tzUnspecified)
        {
            if (displayZone.IsInvalidTime(tzUnspecified))
                tzUnspecified = tzUnspecified.AddHours(1);
            return TimeZoneInfo.ConvertTime(tzUnspecified, displayZone, Core.Globals.GeneralOptions.TimeZoneInfo);
        }

        // X for any chart-zone time, including times past the last bar, where the
        // bar-to-pixel mapping runs out and the recent bars' own pace extends it.
        // On non-time bars (Renko, tick) that pace is an estimate, but so is any
        // other way of placing a future time on such a chart.
        private double XForTime(ChartControl chartControl, DateTime chartZoneTime, DateTime lastBarTime, double lastBarX, double pixelsPerMs)
        {
            if (chartZoneTime <= lastBarTime)
                return chartControl.GetXByTime(chartZoneTime);
            if (pixelsPerMs <= 0)
                return double.MaxValue; // no pace to extend with; lands off-panel and is skipped
            return lastBarX + (chartZoneTime - lastBarTime).TotalMilliseconds * pixelsPerMs;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartControl == null || ChartPanel == null || RenderTarget == null || displayZone == null)
                return;
            if (Bars.Count == 0 || markers.Count == 0)
                return;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

            DateTime lastBarTime = Bars.GetTime(Bars.Count - 1);
            double lastBarX = chartControl.GetXByTime(lastBarTime);

            // Pixels per millisecond from the last stretch of bars, for placing
            // times that lie past the last bar.
            int paceBackIndex = Math.Max(0, Bars.Count - 1 - 20);
            DateTime paceBackTime = Bars.GetTime(paceBackIndex);
            double paceMs = (lastBarTime - paceBackTime).TotalMilliseconds;
            double pixelsPerMs = paceMs > 0 ? (lastBarX - chartControl.GetXByTime(paceBackTime)) / paceMs : 0;

            DateTime now = PlatformNow();
            DateTime todayTzDate = TimeZoneInfo.ConvertTime(now, Core.Globals.GeneralOptions.TimeZoneInfo, displayZone).Date;

            // History counts back from the newest day the chart has bars for, not
            // from today: counting from today would, over a weekend, push the last
            // day that actually traded out of the window.
            List<DateTime> daysToDraw = new List<DateTime>();
            lock (sync)
            {
                if (tradingDays.Count > 0)
                {
                    DateTime cutoff = tradingDays[tradingDays.Count - 1].AddDays(-HistoryDays);
                    foreach (DateTime trackedDay in tradingDays)
                        if (trackedDay >= cutoff && trackedDay < todayTzDate)
                            daysToDraw.Add(trackedDay);
                }
            }
            // Today and tomorrow go in whether or not they have bars yet: their
            // lines are the ones still ahead, which is the point of drawing them.
            daysToDraw.Add(todayTzDate);
            if (IncludeTomorrow)
                daysToDraw.Add(todayTzDate.AddDays(1));

            float panelLeft = ChartPanel.X;
            float panelRight = ChartPanel.X + ChartPanel.W;
            float panelTop = ChartPanel.Y;
            float panelBottom = ChartPanel.Y + ChartPanel.H;

            // Captions are pinned to the viewport, a gap in from the chosen edge
            // (or the middle of it), and ride there through scroll and zoom.
            float edgeGap = (float)(ChartPanel.H * EdgeGapPercent / 100);
            float captionY;
            switch (LabelPosition)
            {
                case TimeMarkLabelPosition.Middle: captionY = panelTop + ChartPanel.H / 2f; break;
                case TimeMarkLabelPosition.Bottom: captionY = panelBottom - edgeGap; break;
                default: captionY = panelTop + edgeGap; break;
            }

            SimpleFont wpfFont = chartControl.Properties.LabelFont ?? new SimpleFont();
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
            try
            {
                textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

                foreach (TimeMarker marker in markers)
                    marker.Stroke.RenderTarget = RenderTarget;

                foreach (DateTime dayStart in daysToDraw)
                {
                    foreach (TimeMarker marker in markers)
                    {
                        DateTime eventChartTime = ToChartZone(dayStart.AddMinutes(marker.MinutesOfDay));
                        double x = XForTime(chartControl, eventChartTime, lastBarTime, lastBarX, pixelsPerMs);
                        if (x < panelLeft || x > panelRight)
                            continue;

                        RenderTarget.DrawLine(
                            new SharpDX.Vector2((float)x, panelTop),
                            new SharpDX.Vector2((float)x, panelBottom),
                            marker.Stroke.BrushDX, marker.Stroke.Width, marker.Stroke.StrokeStyle);

                        if (!ShowLabels || marker.Caption.Length == 0)
                            continue;
                        string caption = marker.Caption;
                        if (ShowCountdown && eventChartTime > now)
                            caption += "\n" + CountdownText(eventChartTime - now);
                        DrawCaption(caption, (float)x + 4, captionY, marker.Stroke.BrushDX, textFormat);
                    }
                }
            }
            finally
            {
                textFormat.Dispose();
            }
        }

        private void DrawCaption(string caption, float x, float centreY, SharpDX.Direct2D1.Brush brush,
            SharpDX.DirectWrite.TextFormat textFormat)
        {
            SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, caption, textFormat, 600, textFormat.FontSize);
            try
            {
                RenderTarget.DrawTextLayout(
                    new SharpDX.Vector2(x, centreY - layout.Metrics.Height / 2),
                    layout, brush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
            finally
            {
                layout.Dispose();
            }
        }
    }
}
