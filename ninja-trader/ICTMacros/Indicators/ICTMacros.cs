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
    public enum MacroTimeZone
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

    public enum MacroStripPosition
    {
        Bottom,
        Top
    }

    /// <summary>
    /// The ICT macro windows: each one bracketed by a vertical line at its start
    /// and end with a captioned band joining the pair, the caption counting down
    /// to the window and then counting out what is left of it. Windows are drawn
    /// for the whole day, the ones still ahead included.
    /// Extracted from the TradingView "Time-Based Vertical Lines" port so the
    /// macro concept stands on its own; TimeBasedVerticalLines keeps the plain
    /// line slots.
    /// </summary>
    public class ICTMacros : Indicator
    {
        // Distinct days kept in the log: comfortably more than the five the
        // history cutoff can ask for, so the oldest day still wanted is always
        // still in there.
        private const int MaxTrackedDays = 20;

        private class MacroSpan
        {
            public int StartMinutes;
            public int EndMinutes;
            public string Caption;
        }

        private readonly List<MacroSpan> spans = new List<MacroSpan>();

        // Mutated on the data thread, read on the render thread.
        private readonly object sync = new object();
        private readonly List<DateTime> tradingDays = new List<DateTime>();

        private TimeZoneInfo displayZone;
        private System.Windows.Threading.DispatcherTimer refreshTimer;

        #region Properties

        [Display(Name = "Time Zone", GroupName = "General", Order = 1,
                 Description = "Zone every window time below is read in. Exchange means the instrument's own trading-hours zone.")]
        public MacroTimeZone TimeZoneSelection { get; set; }

        [Range(0, 5)]
        [Display(Name = "Days of History to Show", GroupName = "General", Order = 2,
                 Description = "How far back to keep drawing windows, counted from the last day the chart has bars for. 0 = that day and today.")]
        public int HistoryDays { get; set; }

        [Display(Name = "Include Tomorrow", GroupName = "General", Order = 3,
                 Description = "Also draw tomorrow's windows. Useful late in the session, when everything for today has already happened.")]
        public bool IncludeTomorrow { get; set; }

        [Display(Name = "Show Captions", GroupName = "General", Order = 4)]
        public bool ShowLabels { get; set; }

        [Display(Name = "Countdown In Captions", GroupName = "General", Order = 5,
                 Description = "Count down to a window still ahead, and count out what is left of one already running.")]
        public bool ShowCountdown { get; set; }

        [Display(Name = "Style", GroupName = "Band", Order = 1,
                 Description = "Color, width and dash shared by every window's two lines and its band border.")]
        public Stroke MacroStroke { get; set; }

        [Display(Name = "Strip Position", GroupName = "Band", Order = 2,
                 Description = "Which edge of the viewport the bands ride. Bottom by default, leaving the top free for the Time-Based Vertical Lines captions.")]
        public MacroStripPosition StripPosition { get; set; }

        [Range(0.0, 40.0)]
        [Display(Name = "Edge Gap (%)", GroupName = "Band", Order = 3,
                 Description = "Clear space between the panel edge and the bands, as a share of the panel height.")]
        public double EdgeGapPercent { get; set; }

        [Range(0.0, 40.0)]
        [Display(Name = "Band Height (%)", GroupName = "Band", Order = 4,
                 Description = "Thickness of the band joining a window's two lines, as a share of the panel height. 0 draws no band.")]
        public double BandHeightPercent { get; set; }

        [Range(0, 100)]
        [Display(Name = "Band Opacity (%)", GroupName = "Band", Order = 5)]
        public int BandOpacity { get; set; }

        [Display(Name = "Macro 1", GroupName = "Macros", Order = 10)]
        public bool Macro1Enabled { get; set; }
        [Display(Name = "Macro 1 Start", GroupName = "Macros", Order = 11)]
        public string Macro1Start { get; set; }
        [Display(Name = "Macro 1 End", GroupName = "Macros", Order = 12)]
        public string Macro1End { get; set; }

        [Display(Name = "Macro 2", GroupName = "Macros", Order = 20)]
        public bool Macro2Enabled { get; set; }
        [Display(Name = "Macro 2 Start", GroupName = "Macros", Order = 21)]
        public string Macro2Start { get; set; }
        [Display(Name = "Macro 2 End", GroupName = "Macros", Order = 22)]
        public string Macro2End { get; set; }

        [Display(Name = "Macro 3", GroupName = "Macros", Order = 30)]
        public bool Macro3Enabled { get; set; }
        [Display(Name = "Macro 3 Start", GroupName = "Macros", Order = 31)]
        public string Macro3Start { get; set; }
        [Display(Name = "Macro 3 End", GroupName = "Macros", Order = 32)]
        public string Macro3End { get; set; }

        [Display(Name = "Macro 4", GroupName = "Macros", Order = 40)]
        public bool Macro4Enabled { get; set; }
        [Display(Name = "Macro 4 Start", GroupName = "Macros", Order = 41)]
        public string Macro4Start { get; set; }
        [Display(Name = "Macro 4 End", GroupName = "Macros", Order = 42)]
        public string Macro4End { get; set; }

        [Display(Name = "Macro 5", GroupName = "Macros", Order = 50)]
        public bool Macro5Enabled { get; set; }
        [Display(Name = "Macro 5 Start", GroupName = "Macros", Order = 51)]
        public string Macro5Start { get; set; }
        [Display(Name = "Macro 5 End", GroupName = "Macros", Order = 52)]
        public string Macro5End { get; set; }

        [Display(Name = "Macro 6", GroupName = "Macros", Order = 60)]
        public bool Macro6Enabled { get; set; }
        [Display(Name = "Macro 6 Start", GroupName = "Macros", Order = 61)]
        public string Macro6Start { get; set; }
        [Display(Name = "Macro 6 End", GroupName = "Macros", Order = 62)]
        public string Macro6End { get; set; }

        [Display(Name = "Macro 7", GroupName = "Macros", Order = 70)]
        public bool Macro7Enabled { get; set; }
        [Display(Name = "Macro 7 Start", GroupName = "Macros", Order = 71)]
        public string Macro7Start { get; set; }
        [Display(Name = "Macro 7 End", GroupName = "Macros", Order = 72)]
        public string Macro7End { get; set; }

        [Display(Name = "Macro 8", GroupName = "Macros", Order = 80)]
        public bool Macro8Enabled { get; set; }
        [Display(Name = "Macro 8 Start", GroupName = "Macros", Order = 81)]
        public string Macro8Start { get; set; }
        [Display(Name = "Macro 8 End", GroupName = "Macros", Order = 82)]
        public string Macro8End { get; set; }

        [Display(Name = "Macro 9", GroupName = "Macros", Order = 90)]
        public bool Macro9Enabled { get; set; }
        [Display(Name = "Macro 9 Start", GroupName = "Macros", Order = 91)]
        public string Macro9Start { get; set; }
        [Display(Name = "Macro 9 End", GroupName = "Macros", Order = 92)]
        public string Macro9End { get; set; }

        [Display(Name = "Macro 10", GroupName = "Macros", Order = 100)]
        public bool Macro10Enabled { get; set; }
        [Display(Name = "Macro 10 Start", GroupName = "Macros", Order = 101)]
        public string Macro10Start { get; set; }
        [Display(Name = "Macro 10 End", GroupName = "Macros", Order = 102)]
        public string Macro10End { get; set; }

        [Display(Name = "Macro 11", GroupName = "Macros", Order = 110)]
        public bool Macro11Enabled { get; set; }
        [Display(Name = "Macro 11 Start", GroupName = "Macros", Order = 111)]
        public string Macro11Start { get; set; }
        [Display(Name = "Macro 11 End", GroupName = "Macros", Order = 112)]
        public string Macro11End { get; set; }

        [Display(Name = "Macro 12", GroupName = "Macros", Order = 120)]
        public bool Macro12Enabled { get; set; }
        [Display(Name = "Macro 12 Start", GroupName = "Macros", Order = 121)]
        public string Macro12Start { get; set; }
        [Display(Name = "Macro 12 End", GroupName = "Macros", Order = 122)]
        public string Macro12End { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "The ICT macro windows, each bracketed by a line at both ends with a captioned band joining the pair, countdowns included.";
                Name = "ICT Macros";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                TimeZoneSelection = MacroTimeZone.NewYork;
                HistoryDays = 1;
                IncludeTomorrow = false;
                ShowLabels = true;
                ShowCountdown = true;

                MacroStroke = new Stroke(Rgb(0xF5, 0x7C, 0x00), DashStyleHelper.Dot, 1f);
                StripPosition = MacroStripPosition.Bottom;
                EdgeGapPercent = 2.0;
                BandHeightPercent = 4.0;
                BandOpacity = 15;

                // The macro rule is :50 to :10 around every top of the hour, with
                // the last RTH hour split into the Final Hour and Market On Close
                // windows instead. The playbook trades the six RTH windows plus
                // those two, so exactly that set is on; the pre-market pair and
                // the London pair are carried for convenience but start off.
                Macro1Enabled = false; Macro1Start = "07:50"; Macro1End = "08:10";
                Macro2Enabled = false; Macro2Start = "08:50"; Macro2End = "09:10";
                Macro3Enabled = true; Macro3Start = "09:50"; Macro3End = "10:10";
                Macro4Enabled = true; Macro4Start = "10:50"; Macro4End = "11:10";
                Macro5Enabled = true; Macro5Start = "11:50"; Macro5End = "12:10";
                Macro6Enabled = true; Macro6Start = "12:50"; Macro6End = "13:10";
                Macro7Enabled = true; Macro7Start = "13:50"; Macro7End = "14:10";
                Macro8Enabled = true; Macro8Start = "14:50"; Macro8End = "15:10";
                Macro9Enabled = true; Macro9Start = "15:15"; Macro9End = "15:45";
                Macro10Enabled = true; Macro10Start = "15:45"; Macro10End = "16:00";
                Macro11Enabled = false; Macro11Start = "02:33"; Macro11End = "03:00";
                Macro12Enabled = false; Macro12Start = "04:03"; Macro12End = "04:30";
            }
            else if (State == State.DataLoaded)
            {
                displayZone = ResolveDisplayZone();
                spans.Clear();
                AddMacro(Macro1Enabled, Macro1Start, Macro1End);
                AddMacro(Macro2Enabled, Macro2Start, Macro2End);
                AddMacro(Macro3Enabled, Macro3Start, Macro3End);
                AddMacro(Macro4Enabled, Macro4Start, Macro4End);
                AddMacro(Macro5Enabled, Macro5Start, Macro5End);
                AddMacro(Macro6Enabled, Macro6Start, Macro6End);
                AddMacro(Macro7Enabled, Macro7Start, Macro7End);
                AddMacro(Macro8Enabled, Macro8Start, Macro8End);
                AddMacro(Macro9Enabled, Macro9Start, Macro9End);
                AddMacro(Macro10Enabled, Macro10Start, Macro10End);
                AddMacro(Macro11Enabled, Macro11Start, Macro11End);
                AddMacro(Macro12Enabled, Macro12Start, Macro12End);
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
                    case MacroTimeZone.Utc: return TimeZoneInfo.Utc;
                    case MacroTimeZone.NewYork: return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    case MacroTimeZone.Chicago: return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                    case MacroTimeZone.London: return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                    case MacroTimeZone.Berlin: return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
                    case MacroTimeZone.Tokyo: return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                    case MacroTimeZone.Sydney: return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
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

        private void AddMacro(bool enabled, string startText, string endText)
        {
            if (!enabled)
                return;
            int startMinutes = ParseClockMinutes(startText);
            int endMinutes = ParseClockMinutes(endText);
            if (startMinutes < 0 || endMinutes < 0)
            {
                Log("ICT Macros: skipping macro \"" + startText + "\" to \"" + endText + "\", expected times as HH:MM.", LogLevel.Warning);
                return;
            }
            if (endMinutes <= startMinutes)
            {
                Log("ICT Macros: skipping macro \"" + startText + "\" to \"" + endText + "\", a window has to end after it starts on the same day.", LogLevel.Warning);
                return;
            }
            spans.Add(new MacroSpan
            {
                StartMinutes = startMinutes,
                EndMinutes = endMinutes,
                Caption = "Macro " + ClockText(startMinutes) + "-" + ClockText(endMinutes)
            });
        }

        private static string ClockText(int minutesOfDay)
        {
            return string.Format("{0:00}:{1:00}", minutesOfDay / 60, minutesOfDay % 60);
        }

        // mm:ss while under an hour, then "Xh MMm" -- the seconds stop being
        // useful once the window is that far out.
        private static string CountdownText(TimeSpan left)
        {
            if (left < TimeSpan.Zero)
                left = TimeSpan.Zero;
            return left.TotalHours >= 1
                ? string.Format("{0}h {1:00}m", (int)left.TotalHours, left.Minutes)
                : string.Format("{0:00}:{1:00}", left.Minutes, left.Seconds);
        }

        // Days are logged as the bars pass through them rather than counted back
        // from today, so a weekend or a holiday never collects a set of windows
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
            if (Bars.Count == 0 || spans.Count == 0)
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
            // windows are the ones still ahead, which is the point of drawing them.
            daysToDraw.Add(todayTzDate);
            if (IncludeTomorrow)
                daysToDraw.Add(todayTzDate.AddDays(1));

            float panelLeft = ChartPanel.X;
            float panelRight = ChartPanel.X + ChartPanel.W;
            float panelTop = ChartPanel.Y;
            float panelBottom = ChartPanel.Y + ChartPanel.H;

            // The band strip is pinned to the viewport, a gap in from the chosen
            // edge, and rides there through scroll and zoom -- it costs no chart
            // scale, unlike the TradingView original, which had to give up price
            // range for the room.
            float edgeGap = (float)(ChartPanel.H * EdgeGapPercent / 100);
            float bandHeight = (float)(ChartPanel.H * BandHeightPercent / 100);
            float bandTop = StripPosition == MacroStripPosition.Top
                ? panelTop + edgeGap
                : panelBottom - edgeGap - bandHeight;

            SimpleFont wpfFont = chartControl.Properties.LabelFont ?? new SimpleFont();
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
            SharpDX.Direct2D1.SolidColorBrush bandFillBrush = null;
            try
            {
                textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

                System.Windows.Media.SolidColorBrush macroSolid = MacroStroke.Brush as System.Windows.Media.SolidColorBrush;
                System.Windows.Media.Color macroColor = macroSolid != null ? macroSolid.Color : Colors.Orange;
                bandFillBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    new SharpDX.Color(macroColor.R, macroColor.G, macroColor.B, (byte)(255 * BandOpacity / 100)));
                MacroStroke.RenderTarget = RenderTarget;

                foreach (DateTime dayStart in daysToDraw)
                    foreach (MacroSpan span in spans)
                    {
                        DateTime spanStart = ToChartZone(dayStart.AddMinutes(span.StartMinutes));
                        DateTime spanEnd = ToChartZone(dayStart.AddMinutes(span.EndMinutes));
                        double startX = XForTime(chartControl, spanStart, lastBarTime, lastBarX, pixelsPerMs);
                        double endX = XForTime(chartControl, spanEnd, lastBarTime, lastBarX, pixelsPerMs);
                        if (endX < panelLeft || startX > panelRight)
                            continue;

                        // Both edges, always: a macro is a span, and one line can
                        // only say when it opens. The band between them carries
                        // the name, which is what makes the pair read as one
                        // window instead of two loose lines.
                        if (startX >= panelLeft)
                            RenderTarget.DrawLine(
                                new SharpDX.Vector2((float)startX, panelTop),
                                new SharpDX.Vector2((float)startX, panelBottom),
                                MacroStroke.BrushDX, MacroStroke.Width, MacroStroke.StrokeStyle);
                        if (endX <= panelRight)
                            RenderTarget.DrawLine(
                                new SharpDX.Vector2((float)endX, panelTop),
                                new SharpDX.Vector2((float)endX, panelBottom),
                                MacroStroke.BrushDX, MacroStroke.Width, MacroStroke.StrokeStyle);

                        if (bandHeight > 0)
                        {
                            SharpDX.RectangleF band = new SharpDX.RectangleF(
                                (float)Math.Max(startX, panelLeft), bandTop,
                                (float)(Math.Min(endX, panelRight) - Math.Max(startX, panelLeft)), bandHeight);
                            RenderTarget.FillRectangle(band, bandFillBrush);
                            RenderTarget.DrawRectangle(band, MacroStroke.BrushDX, MacroStroke.Width, MacroStroke.StrokeStyle);
                        }

                        if (!ShowLabels)
                            continue;
                        // One caption for the whole window, changing tense with
                        // it: counting down to the open while the window is
                        // ahead, counting out what is left of it once inside.
                        string caption = span.Caption;
                        if (ShowCountdown)
                        {
                            if (now < spanStart)
                                caption += "   in " + CountdownText(spanStart - now);
                            else if (now < spanEnd)
                                caption += "   " + CountdownText(spanEnd - now) + " left";
                        }
                        DrawCaption(caption, (float)(startX + endX) / 2, bandTop + bandHeight / 2, textFormat);
                    }
            }
            finally
            {
                if (bandFillBrush != null)
                    bandFillBrush.Dispose();
                textFormat.Dispose();
            }
        }

        private void DrawCaption(string caption, float centreX, float centreY, SharpDX.DirectWrite.TextFormat textFormat)
        {
            SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, caption, textFormat, 600, textFormat.FontSize);
            try
            {
                RenderTarget.DrawTextLayout(
                    new SharpDX.Vector2(centreX - layout.Metrics.Width / 2, centreY - layout.Metrics.Height / 2),
                    layout, MacroStroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
            finally
            {
                layout.Dispose();
            }
        }
    }
}
