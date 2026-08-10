#region Using declarations
using System;
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
    public enum CountdownTimeZone
    {
        Exchange,
        Utc,
        NewYork,
        London,
        Tokyo,
        Sydney
    }

    public enum CountdownRiskMode
    {
        FixedDollar,
        PercentOfAccount
    }

    public enum CountdownAtrSmoothing
    {
        Exponential,
        Wilder
    }

    public enum CountdownTableCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// On-chart info table: a clock, countdowns to the next candle on up to three
    /// timeframes, and an ATR-based position size -- the lot count that keeps the
    /// loss at the configured risk if a stop stopAtrMult ATRs away is hit.
    /// Port of the TradingView "Candle Countdown &amp; Position Sizer".
    /// </summary>
    public class CandleCountdownPositionSizer : Indicator
    {
        private const float CellPaddingX = 8f;
        private const float CellPaddingY = 4f;
        private const float TableMargin = 10f;

        // ATR as a series so the recursion stays correct under OnPriceChange:
        // the forming bar overwrites its own slot on every tick while [1] keeps
        // holding the completed prior bar. The render pass reads the snapshot
        // field instead of the series: a barsAgo indexer is only guaranteed to
        // be in sync inside the market-data callbacks.
        private Series<double> atrSeries;
        private double currentAtr;

        private System.Windows.Threading.DispatcherTimer refreshTimer;
        private TimeZoneInfo displayZone;

        private SharpDX.Direct2D1.Brush textBrushDx;
        private SharpDX.Direct2D1.Brush backgroundBrushDx;
        private SharpDX.Direct2D1.Brush alertTextBrushDx;
        private SharpDX.Direct2D1.Brush alertBackgroundBrushDx;
        private SharpDX.Direct2D1.Brush borderBrushDx;

        private struct TableRow
        {
            public string Label;
            public string Value;
            public bool IsAlert;
        }

        #region Properties

        [Display(Name = "Timezone", GroupName = "Display", Order = 1,
                 Description = "Timezone of the clock row. Exchange means the instrument's own trading-hours zone.")]
        public CountdownTimeZone ClockTimeZone { get; set; }

        [Display(Name = "Position", GroupName = "Display", Order = 2)]
        public CountdownTableCorner TablePosition { get; set; }

        [XmlIgnore]
        [Display(Name = "Text Color", GroupName = "Display", Order = 3)]
        public Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Background Color", GroupName = "Display", Order = 4)]
        public Brush BackgroundColor { get; set; }

        [Browsable(false)]
        public string BackgroundColorSerializable
        {
            get { return Serialize.BrushToString(BackgroundColor); }
            set { BackgroundColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Alert Text Color", GroupName = "Display", Order = 5)]
        public Brush AlertTextColor { get; set; }

        [Browsable(false)]
        public string AlertTextColorSerializable
        {
            get { return Serialize.BrushToString(AlertTextColor); }
            set { AlertTextColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Alert Background Color", GroupName = "Display", Order = 6)]
        public Brush AlertBackgroundColor { get; set; }

        [Browsable(false)]
        public string AlertBackgroundColorSerializable
        {
            get { return Serialize.BrushToString(AlertBackgroundColor); }
            set { AlertBackgroundColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Show Countdowns", GroupName = "Countdown", Order = 1)]
        public bool ShowCountdowns { get; set; }

        [Display(Name = "Timeframe 1", GroupName = "Countdown", Order = 2)]
        public bool Countdown1Enabled { get; set; }

        [Range(1, 1440)]
        [Display(Name = "Timeframe 1 (minutes)", GroupName = "Countdown", Order = 3)]
        public int Countdown1Minutes { get; set; }

        [Display(Name = "Timeframe 2", GroupName = "Countdown", Order = 4)]
        public bool Countdown2Enabled { get; set; }

        [Range(1, 1440)]
        [Display(Name = "Timeframe 2 (minutes)", GroupName = "Countdown", Order = 5)]
        public int Countdown2Minutes { get; set; }

        [Display(Name = "Timeframe 3", GroupName = "Countdown", Order = 6)]
        public bool Countdown3Enabled { get; set; }

        [Range(1, 1440)]
        [Display(Name = "Timeframe 3 (minutes)", GroupName = "Countdown", Order = 7)]
        public int Countdown3Minutes { get; set; }

        [Display(Name = "Highlight Countdown Before New Candle", GroupName = "Candle Alert", Order = 1)]
        public bool AlertEnabled { get; set; }

        [Range(1, 600)]
        [Display(Name = "Alert Lead Time (seconds)", GroupName = "Candle Alert", Order = 2,
                 Description = "A countdown at or under this many seconds switches to the alert colors.")]
        public int AlertLeadSeconds { get; set; }

        [Display(Name = "Risk Mode", GroupName = "Position Sizing", Order = 1,
                 Description = "Fixed $: risk a set dollar amount per trade. Percent of account: risk a share of the account size.")]
        public CountdownRiskMode RiskMode { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Risk Per Trade ($)", GroupName = "Position Sizing", Order = 2,
                 Description = "Used in Fixed $ mode. Maximum loss if the stop is hit.")]
        public double RiskDollars { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Account Size ($)", GroupName = "Position Sizing", Order = 3)]
        public double AccountSize { get; set; }

        [Range(0.0, 100.0)]
        [Display(Name = "Risk Per Trade (%)", GroupName = "Position Sizing", Order = 4,
                 Description = "Used in Percent of Account mode.")]
        public double RiskPercent { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Stop Size (x ATR)", GroupName = "Position Sizing", Order = 5,
                 Description = "Stop distance as a multiple of ATR. 1.5 puts the stop 1.5 ATRs from entry.")]
        public double StopAtrMultiple { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Length", GroupName = "Position Sizing", Order = 6)]
        public int AtrLength { get; set; }

        [Display(Name = "ATR Smoothing", GroupName = "Position Sizing", Order = 7,
                 Description = "Wilder is the classic ATR. Exponential reacts faster to recent volatility.")]
        public CountdownAtrSmoothing AtrSmoothing { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Clock, countdowns to the next candle on up to three timeframes, and an ATR-based position size.";
                Name = "Candle Countdown & Position Sizer";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                ClockTimeZone = CountdownTimeZone.Exchange;
                TablePosition = CountdownTableCorner.TopRight;
                TextColor = Brushes.White;
                // Frozen because the render thread reads the brush's color; an
                // unfrozen WPF brush is bound to its creating thread.
                SolidColorBrush translucentBlack = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
                translucentBlack.Freeze();
                BackgroundColor = translucentBlack;
                AlertTextColor = Brushes.Black;
                AlertBackgroundColor = Brushes.Red;

                ShowCountdowns = true;
                Countdown1Enabled = true;
                Countdown1Minutes = 1;
                Countdown2Enabled = true;
                Countdown2Minutes = 5;
                Countdown3Enabled = true;
                Countdown3Minutes = 15;

                AlertEnabled = true;
                AlertLeadSeconds = 10;

                RiskMode = CountdownRiskMode.FixedDollar;
                RiskDollars = 100.0;
                AccountSize = 10000.0;
                RiskPercent = 1.0;
                StopAtrMultiple = 1.5;
                AtrLength = 5;
                AtrSmoothing = CountdownAtrSmoothing.Exponential;
            }
            else if (State == State.DataLoaded)
            {
                atrSeries = new Series<double>(this);
                displayZone = ResolveDisplayZone();
            }
            else if (State == State.Realtime)
            {
                // The clock and countdowns move with wall time, not with ticks, so
                // a once-a-second repaint keeps them honest through quiet tape.
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
                DisposeDxBrushes();
            }
        }

        private TimeZoneInfo ResolveDisplayZone()
        {
            try
            {
                switch (ClockTimeZone)
                {
                    case CountdownTimeZone.Utc: return TimeZoneInfo.Utc;
                    case CountdownTimeZone.NewYork: return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    case CountdownTimeZone.London: return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                    case CountdownTimeZone.Tokyo: return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                    case CountdownTimeZone.Sydney: return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
                    default: return Bars.TradingHours.TimeZoneInfo;
                }
            }
            catch (TimeZoneNotFoundException)
            {
                // A machine without the requested Windows zone still gets a clock,
                // just in the platform's own display zone.
                return Core.Globals.GeneralOptions.TimeZoneInfo;
            }
        }

        protected override void OnBarUpdate()
        {
            double trueRange = CurrentBar == 0
                ? High[0] - Low[0]
                : Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));

            // Both smoothings are one recursion apart: k * tr + (1 - k) * prev,
            // with k = 2/(n+1) (EMA) or 1/n (Wilder). The first AtrLength bars
            // carry the running mean of the true ranges instead, which is the
            // simple-average seed Pine's ta.ema and ta.rma both start from --
            // seeding from the first bar alone would skew the ATR for a long
            // stretch on short histories.
            if (CurrentBar == 0)
                atrSeries[0] = trueRange;
            else if (CurrentBar < AtrLength)
                atrSeries[0] = (atrSeries[1] * CurrentBar + trueRange) / (CurrentBar + 1);
            else
            {
                double k = AtrSmoothing == CountdownAtrSmoothing.Exponential
                    ? 2.0 / (AtrLength + 1)
                    : 1.0 / AtrLength;
                atrSeries[0] = k * trueRange + (1 - k) * atrSeries[1];
            }
            currentAtr = atrSeries[0];
        }

        // Platform time is already in the platform display zone; Playback charts
        // must read the replay clock or every countdown pins at its wrap point.
        private DateTime PlatformNow()
        {
            return Connection.PlaybackConnection != null ? Connection.PlaybackConnection.Now : Core.Globals.Now;
        }

        // Seconds until the next candle of a tf minutes timeframe. Boundaries are
        // anchored to the Unix epoch (UTC), exactly like the TradingView original,
        // so both platforms flip their candles on the same second.
        private double SecondsUntilCandle(int tfMinutes)
        {
            DateTime nowUtc = TimeZoneInfo.ConvertTime(PlatformNow(), Core.Globals.GeneralOptions.TimeZoneInfo, TimeZoneInfo.Utc);
            double sinceEpoch = (nowUtc - new DateTime(1970, 1, 1)).TotalSeconds;
            double tfSeconds = tfMinutes * 60.0;
            return tfSeconds - sinceEpoch % tfSeconds;
        }

        private static string FormatCountdown(double secondsLeft)
        {
            int total = (int)Math.Floor(secondsLeft);
            return string.Format("{0}:{1:00}", total / 60, total % 60);
        }

        private static string TimeframeLabel(int minutes)
        {
            return minutes % 60 == 0 ? (minutes / 60) + "h" : minutes + "m";
        }

        public override void OnRenderTargetChanged()
        {
            DisposeDxBrushes();
            if (RenderTarget == null)
                return;
            textBrushDx = ToDxBrush(TextColor);
            backgroundBrushDx = ToDxBrush(BackgroundColor);
            alertTextBrushDx = ToDxBrush(AlertTextColor);
            alertBackgroundBrushDx = ToDxBrush(AlertBackgroundColor);
            borderBrushDx = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(128, 128, 128, 100));
        }

        private void DisposeDxBrushes()
        {
            if (textBrushDx != null) { textBrushDx.Dispose(); textBrushDx = null; }
            if (backgroundBrushDx != null) { backgroundBrushDx.Dispose(); backgroundBrushDx = null; }
            if (alertTextBrushDx != null) { alertTextBrushDx.Dispose(); alertTextBrushDx = null; }
            if (alertBackgroundBrushDx != null) { alertBackgroundBrushDx.Dispose(); alertBackgroundBrushDx = null; }
            if (borderBrushDx != null) { borderBrushDx.Dispose(); borderBrushDx = null; }
        }

        private SharpDX.Direct2D1.Brush ToDxBrush(System.Windows.Media.Brush wpfBrush)
        {
            // Fully qualified on both sides: System.Windows.Media and SharpDX.Direct2D1
            // each define SolidColorBrush, and the unqualified name is CS0104.
            System.Windows.Media.SolidColorBrush solid = wpfBrush as System.Windows.Media.SolidColorBrush;
            System.Windows.Media.Color c = solid != null ? solid.Color : Colors.Gray;
            return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(c.R, c.G, c.B, c.A));
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartControl == null || ChartPanel == null || RenderTarget == null || textBrushDx == null)
                return;

            MasterInstrument master = Instrument?.MasterInstrument;
            if (master == null)
                return;
            if (displayZone == null)
                displayZone = ResolveDisplayZone();

            double atr = currentAtr;
            double stopDistance = StopAtrMultiple * atr;
            double riskPerLot = stopDistance * master.PointValue;
            double riskAmount = RiskMode == CountdownRiskMode.FixedDollar
                ? RiskDollars
                : AccountSize * RiskPercent / 100.0;
            double lotSize = riskPerLot > 0 ? riskAmount / riskPerLot : double.NaN;

            DateTime now = TimeZoneInfo.ConvertTime(PlatformNow(), Core.Globals.GeneralOptions.TimeZoneInfo, displayZone);

            var rows = new System.Collections.Generic.List<TableRow>();
            rows.Add(new TableRow { Label = "Time", Value = now.ToString("HH:mm:ss") });

            if (ShowCountdowns)
            {
                AddCountdownRow(rows, Countdown1Enabled, Countdown1Minutes);
                AddCountdownRow(rows, Countdown2Enabled, Countdown2Minutes);
                AddCountdownRow(rows, Countdown3Enabled, Countdown3Minutes);
            }

            // Away-from-zero to match Pine's math.round; .NET's default banker's
            // rounding would show 0 contracts where the original shows 1.
            rows.Add(new TableRow { Label = "Lot", Value = double.IsNaN(lotSize) ? "-" : Math.Round(lotSize, MidpointRounding.AwayFromZero).ToString("0") });
            rows.Add(new TableRow { Label = "ATR", Value = master.FormatPrice(atr) });
            // Invariant culture so the multiple reads "1.5" everywhere; on a
            // comma-decimal Windows the default would print "Stop ATR (1,5)".
            rows.Add(new TableRow { Label = "Stop ATR (" + StopAtrMultiple.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", Value = master.FormatPrice(stopDistance) });

            // Cash at risk at the rounded lot count is only informative in percent
            // mode; in Fixed $ mode it would echo the number the user typed in.
            if (RiskMode == CountdownRiskMode.PercentOfAccount && !double.IsNaN(lotSize))
                rows.Add(new TableRow
                {
                    Label = "Stop Cash (" + RiskPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%)",
                    Value = "$" + (Math.Round(lotSize, MidpointRounding.AwayFromZero) * riskPerLot).ToString("0.##")
                });

            DrawTable(chartControl, rows);
        }

        private void AddCountdownRow(System.Collections.Generic.List<TableRow> rows, bool enabled, int minutes)
        {
            if (!enabled)
                return;
            double secondsLeft = SecondsUntilCandle(minutes);
            rows.Add(new TableRow
            {
                Label = TimeframeLabel(minutes),
                Value = FormatCountdown(secondsLeft),
                IsAlert = AlertEnabled && secondsLeft <= AlertLeadSeconds
            });
        }

        private void DrawTable(ChartControl chartControl, System.Collections.Generic.List<TableRow> rows)
        {
            SimpleFont wpfFont = chartControl.Properties.LabelFont ?? new SimpleFont();
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
            var layouts = new System.Collections.Generic.List<SharpDX.DirectWrite.TextLayout>();
            try
            {
                textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

                // Both columns size to their widest text so a long label ("Stop
                // Cash (1.5%)") never collides with its value.
                float labelWidth = 0;
                float valueWidth = 0;
                float rowHeight = textFormat.FontSize + 2 * CellPaddingY;
                foreach (TableRow row in rows)
                {
                    var labelLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, row.Label, textFormat, 600, textFormat.FontSize);
                    var valueLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, row.Value, textFormat, 600, textFormat.FontSize);
                    layouts.Add(labelLayout);
                    layouts.Add(valueLayout);
                    labelWidth = Math.Max(labelWidth, labelLayout.Metrics.Width);
                    valueWidth = Math.Max(valueWidth, valueLayout.Metrics.Width);
                }

                float tableWidth = labelWidth + valueWidth + 4 * CellPaddingX;
                float tableHeight = rows.Count * rowHeight;
                bool onRight = TablePosition == CountdownTableCorner.TopRight || TablePosition == CountdownTableCorner.BottomRight;
                bool onTop = TablePosition == CountdownTableCorner.TopLeft || TablePosition == CountdownTableCorner.TopRight;
                float tableX = onRight ? ChartPanel.X + ChartPanel.W - tableWidth - TableMargin : ChartPanel.X + TableMargin;
                float tableY = onTop ? ChartPanel.Y + TableMargin : ChartPanel.Y + ChartPanel.H - tableHeight - TableMargin;

                RenderTarget.FillRectangle(new SharpDX.RectangleF(tableX, tableY, tableWidth, tableHeight), backgroundBrushDx);

                for (int i = 0; i < rows.Count; i++)
                {
                    float rowY = tableY + i * rowHeight;
                    if (rows[i].IsAlert)
                        RenderTarget.FillRectangle(
                            new SharpDX.RectangleF(tableX + labelWidth + 2 * CellPaddingX, rowY, valueWidth + 2 * CellPaddingX, rowHeight),
                            alertBackgroundBrushDx);

                    SharpDX.Direct2D1.Brush valueBrush = rows[i].IsAlert ? alertTextBrushDx : textBrushDx;
                    SharpDX.DirectWrite.TextLayout labelLayout = layouts[2 * i];
                    SharpDX.DirectWrite.TextLayout valueLayout = layouts[2 * i + 1];
                    RenderTarget.DrawTextLayout(
                        new SharpDX.Vector2(tableX + CellPaddingX, rowY + CellPaddingY),
                        labelLayout, textBrushDx, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
                    // Values right-align like the TradingView table.
                    RenderTarget.DrawTextLayout(
                        new SharpDX.Vector2(tableX + tableWidth - CellPaddingX - valueLayout.Metrics.Width, rowY + CellPaddingY),
                        valueLayout, valueBrush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
                }

                RenderTarget.DrawRectangle(new SharpDX.RectangleF(tableX, tableY, tableWidth, tableHeight), borderBrushDx, 1);
            }
            finally
            {
                foreach (SharpDX.DirectWrite.TextLayout layout in layouts)
                    layout.Dispose();
                textFormat.Dispose();
            }
        }
    }
}
