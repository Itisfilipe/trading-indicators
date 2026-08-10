#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http;
using System.Web.Script.Serialization;
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
    public enum NewsImpact
    {
        Low,
        Medium,
        High,
        Holiday
    }

    public enum NewsLineTime
    {
        Future,
        PastAndFuture
    }

    public enum NewsHistory
    {
        Today,
        ThisWeek,
        ManualDays
    }

    public enum NewsTableMode
    {
        Today,
        ThisWeek
    }

    public enum NewsTableCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// The week's economic-calendar events on the chart: a vertical line at each
    /// release (future ones placed ahead of the last bar), a caption naming it,
    /// and a corner table of the day's or week's list with the past releases
    /// grayed out. Events come straight from Forex Factory's public weekly feed
    /// over HTTPS -- the TradingView original this ports (toodegrees' Live
    /// Economic Calendar) had to smuggle the same data through request.seed()
    /// because Pine cannot make network calls.
    /// </summary>
    public class EconomicCalendar : Indicator
    {
        private const string FeedUrl = "https://nfs.faireconomy.media/ff_calendar_thisweek.json";
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

        private class NewsEvent
        {
            public DateTime ChartZoneTime;
            public string Title;
            public string Currency;
            public NewsImpact Impact;
        }

        // Swapped whole under the lock by the fetch worker, read by both the
        // data and render threads.
        private readonly object sync = new object();
        private List<NewsEvent> events = new List<NewsEvent>();

        private System.Windows.Threading.DispatcherTimer refreshTimer;
        private DateTime nextFetchUtc = DateTime.MinValue;
        private bool fetchRunning;

        private SharpDX.Direct2D1.Brush[] impactLineBrushes;
        private SharpDX.Direct2D1.Brush[] impactTextBrushes;
        private SharpDX.Direct2D1.Brush headerTextBrushDx;
        private SharpDX.Direct2D1.Brush headerBackBrushDx;
        private SharpDX.Direct2D1.Brush rowTextBrushDx;
        private SharpDX.Direct2D1.Brush rowPastTextBrushDx;
        private SharpDX.Direct2D1.Brush rowBackBrushDx;
        private SharpDX.Direct2D1.Brush borderBrushDx;

        // The event lines share one dash pattern; the impact brushes carry the
        // color. This Stroke exists only to lend its StrokeStyle to DrawLine.
        private Stroke lineDashCarrier;

        #region Properties

        [Display(Name = "Show Lines", GroupName = "On Chart", Order = 1)]
        public bool ShowLines { get; set; }

        [Display(Name = "Lines For", GroupName = "On Chart", Order = 2,
                 Description = "Future draws only the releases still ahead; Past+Future also keeps the ones already out.")]
        public NewsLineTime LineTime { get; set; }

        [Display(Name = "Line Style", GroupName = "On Chart", Order = 3)]
        public DashStyleHelper LineDashStyle { get; set; }

        [Range(1, 5)]
        [Display(Name = "Line Width", GroupName = "On Chart", Order = 4)]
        public int LineWidth { get; set; }

        [Range(0, 100)]
        [Display(Name = "Line Opacity (%)", GroupName = "On Chart", Order = 5)]
        public int LineOpacity { get; set; }

        [Display(Name = "Show Labels", GroupName = "On Chart", Order = 6,
                 Description = "Name each release at the top of its line: impact letter, currency, title.")]
        public bool ShowLabels { get; set; }

        [Display(Name = "Chart History", GroupName = "On Chart", Order = 7,
                 Description = "How far back past releases stay on the chart. Today, this week, or a manual number of days.")]
        public NewsHistory ChartHistory { get; set; }

        [Range(1, 30)]
        [Display(Name = "Manual Days", GroupName = "On Chart", Order = 8)]
        public int ManualDays { get; set; }

        [Display(Name = "High", GroupName = "Expected Impact", Order = 1)]
        public bool ShowHighImpact { get; set; }

        [Display(Name = "Medium", GroupName = "Expected Impact", Order = 2)]
        public bool ShowMediumImpact { get; set; }

        [Display(Name = "Low", GroupName = "Expected Impact", Order = 3)]
        public bool ShowLowImpact { get; set; }

        [Display(Name = "Holidays", GroupName = "Expected Impact", Order = 4)]
        public bool ShowHolidays { get; set; }

        [Display(Name = "Automatic", GroupName = "Currencies", Order = 1,
                 Description = "Follow the chart's instrument: its denomination currency, or both sides of a forex pair. The manual toggles below are ignored while this is on.")]
        public bool AutoCurrency { get; set; }

        [Display(Name = "AUD", GroupName = "Currencies", Order = 2)]
        public bool ShowAud { get; set; }
        [Display(Name = "CAD", GroupName = "Currencies", Order = 3)]
        public bool ShowCad { get; set; }
        [Display(Name = "CHF", GroupName = "Currencies", Order = 4)]
        public bool ShowChf { get; set; }
        [Display(Name = "CNY", GroupName = "Currencies", Order = 5)]
        public bool ShowCny { get; set; }
        [Display(Name = "EUR", GroupName = "Currencies", Order = 6)]
        public bool ShowEur { get; set; }
        [Display(Name = "GBP", GroupName = "Currencies", Order = 7)]
        public bool ShowGbp { get; set; }
        [Display(Name = "JPY", GroupName = "Currencies", Order = 8)]
        public bool ShowJpy { get; set; }
        [Display(Name = "NZD", GroupName = "Currencies", Order = 9)]
        public bool ShowNzd { get; set; }
        [Display(Name = "USD", GroupName = "Currencies", Order = 10)]
        public bool ShowUsd { get; set; }

        [Display(Name = "Show Table", GroupName = "Table", Order = 1)]
        public bool ShowTable { get; set; }

        [Display(Name = "Table Scope", GroupName = "Table", Order = 2)]
        public NewsTableMode TableMode { get; set; }

        [Display(Name = "Position", GroupName = "Table", Order = 3)]
        public NewsTableCorner TablePosition { get; set; }

        [XmlIgnore]
        [Display(Name = "Header Text Color", GroupName = "Table", Order = 4)]
        public Brush HeaderTextColor { get; set; }
        [Browsable(false)]
        public string HeaderTextColorSerializable
        {
            get { return Serialize.BrushToString(HeaderTextColor); }
            set { HeaderTextColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Header Background Color", GroupName = "Table", Order = 5)]
        public Brush HeaderBackColor { get; set; }
        [Browsable(false)]
        public string HeaderBackColorSerializable
        {
            get { return Serialize.BrushToString(HeaderBackColor); }
            set { HeaderBackColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Row Text Color", GroupName = "Table", Order = 6)]
        public Brush RowTextColor { get; set; }
        [Browsable(false)]
        public string RowTextColorSerializable
        {
            get { return Serialize.BrushToString(RowTextColor); }
            set { RowTextColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Past Row Text Color", GroupName = "Table", Order = 7)]
        public Brush RowPastTextColor { get; set; }
        [Browsable(false)]
        public string RowPastTextColorSerializable
        {
            get { return Serialize.BrushToString(RowPastTextColor); }
            set { RowPastTextColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Row Background Color", GroupName = "Table", Order = 8)]
        public Brush RowBackColor { get; set; }
        [Browsable(false)]
        public string RowBackColorSerializable
        {
            get { return Serialize.BrushToString(RowBackColor); }
            set { RowBackColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Border", GroupName = "Table", Order = 9)]
        public bool ShowTableBorder { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "The week's economic-calendar releases from Forex Factory: lines on the chart, a caption per release, and a corner table.";
                Name = "Economic Calendar";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                ShowLines = true;
                LineTime = NewsLineTime.Future;
                LineDashStyle = DashStyleHelper.Solid;
                LineWidth = 1;
                LineOpacity = 60;
                ShowLabels = true;
                ChartHistory = NewsHistory.ThisWeek;
                ManualDays = 30;

                ShowHighImpact = true;
                ShowMediumImpact = true;
                ShowLowImpact = true;
                ShowHolidays = true;

                AutoCurrency = true;

                ShowTable = true;
                TableMode = NewsTableMode.ThisWeek;
                TablePosition = NewsTableCorner.BottomRight;
                HeaderTextColor = Rgb(0xDE, 0xE1, 0xE9);
                HeaderBackColor = Rgb(0x28, 0x3C, 0x70);
                RowTextColor = Rgb(0x00, 0x00, 0x00);
                RowPastTextColor = Rgb(0x78, 0x7B, 0x86);
                RowBackColor = Rgb(0xDE, 0xE1, 0xE9);
                ShowTableBorder = true;
            }
            else if (State == State.DataLoaded)
            {
                nextFetchUtc = DateTime.MinValue; // fetch immediately, then hourly
            }
            else if (State == State.Realtime)
            {
                // The one-minute tick re-grays table rows as releases pass, and
                // is when the feed is re-pulled once it is stale.
                if (refreshTimer == null && ChartControl != null)
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
                            Interval = TimeSpan.FromMinutes(1),
                            IsEnabled = true
                        };
                        refreshTimer.Tick += (o, e) => { StartFetchIfDue(); ForceRefresh(); };
                    });
                }
                StartFetchIfDue();
            }
            else if (State == State.Terminated)
            {
                if (refreshTimer != null)
                {
                    refreshTimer.IsEnabled = false;
                    refreshTimer = null;
                }
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

        // The feed is pulled on a worker thread so a slow network never stalls
        // the data or UI threads; a failed pull keeps the previous list and
        // retries sooner than the regular refresh.
        private void StartFetchIfDue()
        {
            lock (sync)
            {
                if (fetchRunning || DateTime.UtcNow < nextFetchUtc)
                    return;
                fetchRunning = true;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                bool succeeded = false;
                try
                {
                    string json;
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(20);
                        json = client.GetStringAsync(FeedUrl).Result;
                    }
                    List<NewsEvent> parsed = ParseFeed(json);
                    lock (sync)
                        events = parsed;
                    succeeded = true;
                    ChartControl?.Dispatcher.InvokeAsync(() =>
                    {
                        if (State < State.Terminated)
                            ForceRefresh();
                    });
                }
                catch (Exception error)
                {
                    Log("Economic Calendar: calendar fetch failed (" + error.Message + "); keeping the previous list.", LogLevel.Warning);
                }
                finally
                {
                    lock (sync)
                    {
                        fetchRunning = false;
                        nextFetchUtc = DateTime.UtcNow + (succeeded ? RefreshInterval : RetryInterval);
                    }
                }
            });
        }

        // Feed entries are flat objects: title, country (a currency code), date
        // (ISO-8601 with offset), impact. Anything malformed is skipped -- one
        // odd entry must not cost the rest of the week.
        private List<NewsEvent> ParseFeed(string json)
        {
            List<NewsEvent> parsed = new List<NewsEvent>();
            object[] entries = new JavaScriptSerializer().DeserializeObject(json) as object[];
            if (entries == null)
                return parsed;

            foreach (object entry in entries)
            {
                Dictionary<string, object> fields = entry as Dictionary<string, object>;
                if (fields == null)
                    continue;
                string title = FieldText(fields, "title");
                string currency = FieldText(fields, "country").ToUpperInvariant();
                string dateText = FieldText(fields, "date");
                DateTimeOffset when;
                if (title.Length == 0 || currency.Length == 0
                    || !DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                    continue;

                NewsImpact impact;
                switch (FieldText(fields, "impact").ToUpperInvariant())
                {
                    case "HIGH": impact = NewsImpact.High; break;
                    case "MEDIUM": impact = NewsImpact.Medium; break;
                    case "HOLIDAY": impact = NewsImpact.Holiday; break;
                    default: impact = NewsImpact.Low; break; // includes Non-Economic
                }

                parsed.Add(new NewsEvent
                {
                    // Chart-zone times compare directly against bar stamps and
                    // print in the same clock the time axis shows.
                    ChartZoneTime = TimeZoneInfo.ConvertTimeFromUtc(when.UtcDateTime, Core.Globals.GeneralOptions.TimeZoneInfo),
                    Title = title,
                    Currency = currency,
                    Impact = impact
                });
            }
            parsed.Sort((a, b) => a.ChartZoneTime.CompareTo(b.ChartZoneTime));
            return parsed;
        }

        private static string FieldText(Dictionary<string, object> fields, string key)
        {
            object value;
            return fields.TryGetValue(key, out value) && value is string ? (string)value : string.Empty;
        }

        private bool ImpactVisible(NewsImpact impact)
        {
            switch (impact)
            {
                case NewsImpact.High: return ShowHighImpact;
                case NewsImpact.Medium: return ShowMediumImpact;
                case NewsImpact.Holiday: return ShowHolidays;
                default: return ShowLowImpact;
            }
        }

        // The currencies whose releases show. Automatic follows the chart's
        // instrument: both sides of a forex pair by name, otherwise the
        // instrument's denomination currency (string-matched so an unmapped
        // denomination degrades to USD instead of failing to compile against
        // an unverified enum surface).
        private HashSet<string> ActiveCurrencies()
        {
            HashSet<string> active = new HashSet<string>();
            if (!AutoCurrency)
            {
                if (ShowAud) active.Add("AUD");
                if (ShowCad) active.Add("CAD");
                if (ShowChf) active.Add("CHF");
                if (ShowCny) active.Add("CNY");
                if (ShowEur) active.Add("EUR");
                if (ShowGbp) active.Add("GBP");
                if (ShowJpy) active.Add("JPY");
                if (ShowNzd) active.Add("NZD");
                if (ShowUsd) active.Add("USD");
                return active;
            }

            MasterInstrument master = Instrument?.MasterInstrument;
            if (master == null)
            {
                active.Add("USD");
                return active;
            }
            string name = (master.Name ?? string.Empty).ToUpperInvariant();
            if (master.InstrumentType == InstrumentType.Forex && name.Length >= 6)
            {
                active.Add(name.Substring(0, 3));
                active.Add(name.Substring(3, 3));
                return active;
            }
            switch (master.Currency.ToString())
            {
                case "Euro": active.Add("EUR"); break;
                case "BritishPound": active.Add("GBP"); break;
                case "JapaneseYen": active.Add("JPY"); break;
                case "AustralianDollar": active.Add("AUD"); break;
                case "CanadianDollar": active.Add("CAD"); break;
                case "SwissFranc": active.Add("CHF"); break;
                case "NewZealandDollar": active.Add("NZD"); break;
                default: active.Add("USD"); break;
            }
            return active;
        }

        // Render-only tool: everything reads live state in OnRender.
        protected override void OnBarUpdate() { }

        private DateTime PlatformNow()
        {
            return Connection.PlaybackConnection != null ? Connection.PlaybackConnection.Now : Core.Globals.Now;
        }

        // X for any chart-zone time, including times past the last bar, where the
        // bar-to-pixel mapping runs out and the recent bars' own pace extends it.
        private double XForTime(ChartControl chartControl, DateTime chartZoneTime, DateTime lastBarTime, double lastBarX, double pixelsPerMs)
        {
            if (chartZoneTime <= lastBarTime)
                return chartControl.GetXByTime(chartZoneTime);
            if (pixelsPerMs <= 0)
                return double.MaxValue; // no pace to extend with; lands off-panel and is skipped
            return lastBarX + (chartZoneTime - lastBarTime).TotalMilliseconds * pixelsPerMs;
        }

        public override void OnRenderTargetChanged()
        {
            DisposeDxBrushes();
        }

        private void DisposeDxBrushes()
        {
            if (impactLineBrushes != null)
                foreach (SharpDX.Direct2D1.Brush brush in impactLineBrushes)
                    brush?.Dispose();
            if (impactTextBrushes != null)
                foreach (SharpDX.Direct2D1.Brush brush in impactTextBrushes)
                    brush?.Dispose();
            impactLineBrushes = null;
            impactTextBrushes = null;
            lineDashCarrier = null;
            headerTextBrushDx?.Dispose(); headerTextBrushDx = null;
            headerBackBrushDx?.Dispose(); headerBackBrushDx = null;
            rowTextBrushDx?.Dispose(); rowTextBrushDx = null;
            rowPastTextBrushDx?.Dispose(); rowPastTextBrushDx = null;
            rowBackBrushDx?.Dispose(); rowBackBrushDx = null;
            borderBrushDx?.Dispose(); borderBrushDx = null;
        }

        private SharpDX.Direct2D1.Brush SolidDx(byte r, byte g, byte b, byte a)
        {
            return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(r, g, b, a));
        }

        private SharpDX.Direct2D1.Brush WpfToDx(System.Windows.Media.Brush wpfBrush, byte alpha)
        {
            System.Windows.Media.SolidColorBrush solid = wpfBrush as System.Windows.Media.SolidColorBrush;
            System.Windows.Media.Color c = solid != null ? solid.Color : Colors.Gray;
            return SolidDx(c.R, c.G, c.B, alpha);
        }

        // Built lazily: the render target can change before any state exists.
        // Impact colors are fixed (red, orange, amber, gray) -- they are the
        // calendar's own language and match the TradingView original's glyphs.
        private void EnsureDxBrushes()
        {
            if (impactLineBrushes != null)
                return;
            byte lineAlpha = (byte)(255 * LineOpacity / 100);
            impactLineBrushes = new SharpDX.Direct2D1.Brush[4];
            impactTextBrushes = new SharpDX.Direct2D1.Brush[4];
            impactLineBrushes[(int)NewsImpact.Low] = SolidDx(0xFF, 0xC1, 0x07, lineAlpha);
            impactLineBrushes[(int)NewsImpact.Medium] = SolidDx(0xF5, 0x7C, 0x00, lineAlpha);
            impactLineBrushes[(int)NewsImpact.High] = SolidDx(0xEF, 0x53, 0x50, lineAlpha);
            impactLineBrushes[(int)NewsImpact.Holiday] = SolidDx(0x9E, 0x9E, 0x9E, lineAlpha);
            impactTextBrushes[(int)NewsImpact.Low] = SolidDx(0xB2, 0x86, 0x04, 255);
            impactTextBrushes[(int)NewsImpact.Medium] = SolidDx(0xF5, 0x7C, 0x00, 255);
            impactTextBrushes[(int)NewsImpact.High] = SolidDx(0xEF, 0x53, 0x50, 255);
            impactTextBrushes[(int)NewsImpact.Holiday] = SolidDx(0x9E, 0x9E, 0x9E, 255);
            lineDashCarrier = new Stroke(Brushes.Black, LineDashStyle, LineWidth) { RenderTarget = RenderTarget };
            headerTextBrushDx = WpfToDx(HeaderTextColor, 255);
            headerBackBrushDx = WpfToDx(HeaderBackColor, 255);
            rowTextBrushDx = WpfToDx(RowTextColor, 255);
            rowPastTextBrushDx = WpfToDx(RowPastTextColor, 255);
            rowBackBrushDx = WpfToDx(RowBackColor, 230);
            borderBrushDx = SolidDx(0, 0, 0, 255);
        }

        private static string ImpactLetter(NewsImpact impact)
        {
            switch (impact)
            {
                case NewsImpact.High: return "H";
                case NewsImpact.Medium: return "M";
                case NewsImpact.Holiday: return "Hol";
                default: return "L";
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || Bars.Count == 0 || ChartControl == null || ChartPanel == null || RenderTarget == null)
                return;

            // Kicked from the render pass as well as the timer so a chart that
            // never goes realtime (weekend review) still loads the feed.
            StartFetchIfDue();

            List<NewsEvent> snapshot;
            lock (sync)
                snapshot = events;
            if (snapshot.Count == 0)
                return;

            EnsureDxBrushes();
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

            DateTime now = PlatformNow();
            HashSet<string> currencies = ActiveCurrencies();

            // Impact and currency filter both surfaces; the chart-history cutoff
            // belongs to the lines alone, or a short history would also starve
            // the week table of its earlier rows.
            List<NewsEvent> visible = new List<NewsEvent>();
            foreach (NewsEvent newsEvent in snapshot)
                if (ImpactVisible(newsEvent.Impact) && currencies.Contains(newsEvent.Currency))
                    visible.Add(newsEvent);
            if (visible.Count == 0)
                return;

            SimpleFont wpfFont = chartControl.Properties.LabelFont ?? new SimpleFont();
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
            try
            {
                textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
                if (ShowLines || ShowLabels)
                    DrawEventLines(chartControl, visible, now, textFormat);
                if (ShowTable)
                    DrawEventTable(visible, now, textFormat);
            }
            finally
            {
                textFormat.Dispose();
            }
        }

        private void DrawEventLines(ChartControl chartControl, List<NewsEvent> visible, DateTime now,
            SharpDX.DirectWrite.TextFormat textFormat)
        {
            float panelLeft = ChartPanel.X;
            float panelRight = ChartPanel.X + ChartPanel.W;
            float panelTop = ChartPanel.Y;
            float panelBottom = ChartPanel.Y + ChartPanel.H;

            // How far back past releases keep their lines; future ones always draw.
            DateTime historyCutoff;
            switch (ChartHistory)
            {
                case NewsHistory.Today: historyCutoff = now.Date; break;
                case NewsHistory.ManualDays: historyCutoff = now.Date.AddDays(-ManualDays); break;
                default: // this week, back to Monday
                    historyCutoff = now.Date.AddDays(-(((int)now.Date.DayOfWeek + 6) % 7));
                    break;
            }

            DateTime lastBarTime = Bars.GetTime(Bars.Count - 1);
            double lastBarX = chartControl.GetXByTime(lastBarTime);
            int paceBackIndex = Math.Max(0, Bars.Count - 1 - 20);
            DateTime paceBackTime = Bars.GetTime(paceBackIndex);
            double paceMs = (lastBarTime - paceBackTime).TotalMilliseconds;
            double pixelsPerMs = paceMs > 0 ? (lastBarX - chartControl.GetXByTime(paceBackTime)) / paceMs : 0;

            // Releases sharing a minute (a data drop) would print their captions
            // on top of one another; same-X captions stack downward instead.
            double previousX = double.MinValue;
            float captionY = panelTop + 4;

            foreach (NewsEvent newsEvent in visible)
            {
                if (newsEvent.ChartZoneTime < historyCutoff)
                    continue;
                if (newsEvent.ChartZoneTime <= now && LineTime == NewsLineTime.Future)
                    continue;
                double x = XForTime(chartControl, newsEvent.ChartZoneTime, lastBarTime, lastBarX, pixelsPerMs);
                if (x < panelLeft || x > panelRight)
                    continue;

                if (ShowLines)
                    RenderTarget.DrawLine(
                        new SharpDX.Vector2((float)x, panelTop),
                        new SharpDX.Vector2((float)x, panelBottom),
                        impactLineBrushes[(int)newsEvent.Impact], LineWidth, lineDashCarrier.StrokeStyle);

                if (!ShowLabels)
                    continue;
                string caption = ImpactLetter(newsEvent.Impact) + " " + newsEvent.Currency + " " + newsEvent.Title;
                SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(
                    Core.Globals.DirectWriteFactory, caption, textFormat, 600, textFormat.FontSize);
                try
                {
                    captionY = Math.Abs(x - previousX) < 2 ? captionY + layout.Metrics.Height + 2 : panelTop + 4;
                    RenderTarget.DrawTextLayout(
                        new SharpDX.Vector2((float)x + 4, captionY),
                        layout, impactTextBrushes[(int)newsEvent.Impact], SharpDX.Direct2D1.DrawTextOptions.NoSnap);
                }
                finally
                {
                    layout.Dispose();
                }
                previousX = x;
            }
        }

        private void DrawEventTable(List<NewsEvent> visible, DateTime now, SharpDX.DirectWrite.TextFormat textFormat)
        {
            const float CellPadX = 6f;
            const float CellPadY = 3f;
            const float Margin = 10f;

            List<NewsEvent> rows = new List<NewsEvent>();
            foreach (NewsEvent newsEvent in visible)
                if (TableMode == NewsTableMode.ThisWeek || newsEvent.ChartZoneTime.Date == now.Date)
                    rows.Add(newsEvent);
            if (rows.Count == 0)
                return;

            float rowHeight = textFormat.FontSize + 2 * CellPadY;

            // Cap the list to the panel: keep the still-ahead releases in view by
            // dropping from the top (the oldest) first, and say what was cut.
            int maxRows = (int)((ChartPanel.H - 2 * Margin) / rowHeight) - 1;
            int dropped = 0;
            if (maxRows < 1)
                return;
            if (rows.Count > maxRows)
            {
                dropped = rows.Count - maxRows;
                rows.RemoveRange(0, dropped);
            }

            List<string[]> cells = new List<string[]>();
            foreach (NewsEvent newsEvent in rows)
                cells.Add(new[]
                {
                    newsEvent.ChartZoneTime.ToString("ddd HH:mm", CultureInfo.InvariantCulture),
                    newsEvent.Currency,
                    ImpactLetter(newsEvent.Impact),
                    newsEvent.Title
                });
            string[] header = { "Time", "Cur", "Imp", dropped > 0 ? "Event (+" + dropped + " past)" : "Event" };

            var layouts = new List<SharpDX.DirectWrite.TextLayout>();
            try
            {
                float[] columnWidths = new float[4];
                Func<string, SharpDX.DirectWrite.TextLayout> layoutOf = text =>
                {
                    var layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, text, textFormat, 600, textFormat.FontSize);
                    layouts.Add(layout);
                    return layout;
                };
                for (int c = 0; c < 4; c++)
                    columnWidths[c] = layoutOf(header[c]).Metrics.Width;
                foreach (string[] row in cells)
                    for (int c = 0; c < 4; c++)
                        columnWidths[c] = Math.Max(columnWidths[c], layoutOf(row[c]).Metrics.Width);

                float tableWidth = 2 * CellPadX * 4;
                foreach (float width in columnWidths)
                    tableWidth += width;
                float tableHeight = (rows.Count + 1) * rowHeight;
                bool onRight = TablePosition == NewsTableCorner.TopRight || TablePosition == NewsTableCorner.BottomRight;
                bool onTop = TablePosition == NewsTableCorner.TopLeft || TablePosition == NewsTableCorner.TopRight;
                float tableX = onRight ? ChartPanel.X + ChartPanel.W - tableWidth - Margin : ChartPanel.X + Margin;
                float tableY = onTop ? ChartPanel.Y + Margin : ChartPanel.Y + ChartPanel.H - tableHeight - Margin;

                RenderTarget.FillRectangle(new SharpDX.RectangleF(tableX, tableY, tableWidth, rowHeight), headerBackBrushDx);
                RenderTarget.FillRectangle(new SharpDX.RectangleF(tableX, tableY + rowHeight, tableWidth, tableHeight - rowHeight), rowBackBrushDx);

                // layouts: 4 header entries first, then 4 per row.
                for (int rowIndex = -1; rowIndex < rows.Count; rowIndex++)
                {
                    float y = tableY + (rowIndex + 1) * rowHeight + CellPadY;
                    float xCursor = tableX;
                    for (int c = 0; c < 4; c++)
                    {
                        SharpDX.DirectWrite.TextLayout layout = layouts[(rowIndex + 1) * 4 + c];
                        SharpDX.Direct2D1.Brush brush;
                        if (rowIndex < 0)
                            brush = headerTextBrushDx;
                        else if (c == 2)
                            brush = impactTextBrushes[(int)rows[rowIndex].Impact];
                        else
                            brush = rows[rowIndex].ChartZoneTime <= now ? rowPastTextBrushDx : rowTextBrushDx;
                        RenderTarget.DrawTextLayout(new SharpDX.Vector2(xCursor + CellPadX, y), layout, brush,
                            SharpDX.Direct2D1.DrawTextOptions.NoSnap);
                        xCursor += columnWidths[c] + 2 * CellPadX;
                    }
                }

                if (ShowTableBorder)
                    RenderTarget.DrawRectangle(new SharpDX.RectangleF(tableX, tableY, tableWidth, tableHeight), borderBrushDx, 1);
            }
            finally
            {
                foreach (SharpDX.DirectWrite.TextLayout layout in layouts)
                    layout.Dispose();
            }
        }
    }
}
