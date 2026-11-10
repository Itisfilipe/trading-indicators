#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
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
    /// Heikin-Ashi, CVD and volume-delta direction on the chart's timeframe and
    /// two larger ones built from it, the larger timeframe's lot balance, and a
    /// replay journal that trades every new alignment by fixed rules.
    /// </summary>
    public class FlowAlignment : Indicator
    {
        private class Timeframe
        {
            public int Minutes;
            public string Label;
            public double Key = double.NaN;
            public bool HasPreviousCandle;
            public double PreviousHaOpen, PreviousHaClose, PreviousHaHigh, PreviousHaLow;
            public double HaOpen, HaClose, HaHigh, HaLow;
            public bool HasPreviousCvd;
            public double PreviousCvdHigh, PreviousCvdLow;
            public double CvdHigh, CvdLow;
            public double Delta;
            public int HaSide, CvdSide, VdSide;
        }

        private class JournalTrade
        {
            public int Side;
            public int EntryBar;
            public DateTime EntryTime;
            public double Entry, Stop, InitialStop, Target;
            public double Balance;
            public bool MovedToBreakeven;
            public double MfeTicks, MaeTicks;
            public DateTime ExitTime;
            public double Exit;
            public string Reason;

            public double StopTicks(double tickSize) { return Math.Abs(Entry - InitialStop) / tickSize; }
            public double PnlTicks(double tickSize) { return Side * (Exit - Entry) / tickSize; }
        }

        private Timeframe[] timeframes;
        private Series<double>[] bucketKeys;
        private Series<double> longStop;
        private Series<double> shortStop;
        private Series<double> slowBalance;
        private Series<double> fastVdSide;
        private Series<double> middleVdSide;
        private double cvd;
        private string setupError;
        private TimeZoneInfo newYorkZone;
        private SessionIterator sessionIterator;
        private TimeSpan windowStart, windowEnd;
        private bool hasWindow;
        private System.Windows.Media.Brush buyShade, sellShade;
        private readonly List<JournalTrade> trades = new List<JournalTrade>();
        private JournalTrade openTrade;
        private int skippedSignals;
        private int currentSide;

        private static System.Windows.Media.Brush Rgb(byte red, byte green, byte blue)
        {
            System.Windows.Media.SolidColorBrush brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static System.Windows.Media.Brush Faded(System.Windows.Media.Brush color, int opacityPercent)
        {
            System.Windows.Media.Brush brush = color.Clone();
            brush.Opacity = opacityPercent / 100.0;
            brush.Freeze();
            return brush;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "HA, CVD and VD direction on three timeframes, the larger one's lot balance, and a replay journal of every alignment. Needs Tick Replay for history.";
                Name = "Flow Alignment";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                IsAutoScale = false;
                DrawOnPricePanel = true;
                DisplayInDataBox = true;
                PaintPriceMarkers = false;

                MiddleMinutes = 5;
                SlowMinutes = 15;
                RequireSlowTimeframe = true;
                MinimumBalance = 500;
                ShadeAlignedBars = true;
                ShadeOpacity = 10;

                JournalEnabled = true;
                WindowStart = "09:30";
                WindowEnd = "11:00";
                TargetTicks = 60;
                MinimumStopTicks = 20;
                MaximumStopTicks = 250;
                BreakevenTicks = 0;
                ExitOnFlowFlip = true;
                MaximumHoldMinutes = 20;
                MaximumTradesPerDay = 3;
                ShowTradesOnChart = true;

                BuyColor = Brushes.DodgerBlue;
                SellColor = Brushes.Crimson;
                NeutralColor = Rgb(0xD1, 0xD5, 0xDB);
                TextColor = Rgb(0x37, 0x41, 0x51);
                LabelFont = new SimpleFont("Arial", 10);

                AddPlot(Brushes.Transparent, "State");
            }
            else if (State == State.DataLoaded)
            {
                newYorkZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                sessionIterator = new SessionIterator(Bars);
                hasWindow = TimeSpan.TryParse(WindowStart, CultureInfo.InvariantCulture, out windowStart)
                    && TimeSpan.TryParse(WindowEnd, CultureInfo.InvariantCulture, out windowEnd)
                    && windowEnd.TotalHours < 24
                    && windowStart < windowEnd;

                int fastMinutes = BarsPeriod.Value;
                if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute)
                    setupError = "Flow Alignment: use a minute chart";
                else if (MiddleMinutes % fastMinutes != 0 || SlowMinutes % fastMinutes != 0 || MiddleMinutes <= fastMinutes || SlowMinutes <= MiddleMinutes)
                    setupError = "Flow Alignment: the larger timeframes must be growing multiples of the chart's minutes";
                else if (1440 % MiddleMinutes != 0 || 1440 % SlowMinutes != 0)
                    setupError = "Flow Alignment: the larger timeframes must divide the day evenly (5, 10, 15, 30, 60...)";

                timeframes = new[]
                {
                    new Timeframe { Minutes = fastMinutes, Label = fastMinutes + "m" },
                    new Timeframe { Minutes = MiddleMinutes, Label = MiddleMinutes + "m" },
                    new Timeframe { Minutes = SlowMinutes, Label = SlowMinutes + "m" },
                };
                bucketKeys = new Series<double>[timeframes.Length];
                for (int i = 0; i < timeframes.Length; i++)
                    bucketKeys[i] = new Series<double>(this, MaximumBarsLookBack.Infinite);
                longStop = new Series<double>(this, MaximumBarsLookBack.Infinite);
                shortStop = new Series<double>(this, MaximumBarsLookBack.Infinite);
                slowBalance = new Series<double>(this, MaximumBarsLookBack.Infinite);
                fastVdSide = new Series<double>(this, MaximumBarsLookBack.Infinite);
                middleVdSide = new Series<double>(this, MaximumBarsLookBack.Infinite);

                buyShade = Faded(BuyColor, ShadeOpacity);
                sellShade = Faded(SellColor, ShadeOpacity);
            }
            else if (State == State.Historical)
            {
                if (setupError != null)
                    Draw.TextFixed(this, "FlowAlignmentSetup", setupError, TextPosition.BottomLeft);
                else if (Calculate == Calculate.OnBarClose)
                    Draw.TextFixed(this, "FlowAlignmentCalculate", "Flow Alignment: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay)
                    Draw.TextFixed(this, "FlowAlignmentTickReplay", "Flow Alignment: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
                if (JournalEnabled && !hasWindow)
                    Draw.TextFixed(this, "FlowAlignmentWindow", "Flow Alignment: window times must be HH:MM, start before end", TextPosition.TopLeft);
            }
            else if (State == State.Realtime)
            {
                WriteJournal();
            }
            else if (State == State.Terminated && newYorkZone != null)
            {
                WriteJournal();
            }
        }

        protected override void OnBarUpdate()
        {
            if (setupError != null)
                return;

            if (IsFirstTickOfBar)
            {
                DateTime newYork = NewYorkTime(0);
                long minute = newYork.Ticks / TimeSpan.TicksPerMinute;
                for (int i = 0; i < timeframes.Length; i++)
                    bucketKeys[i][0] = (minute + timeframes[i].Minutes - 1) / timeframes[i].Minutes;

                if (CurrentBar >= 2 && JournalEnabled && hasWindow)
                    StepJournal();
            }

            for (int i = 0; i < timeframes.Length; i++)
                UpdateCandle(timeframes[i], i);

            Timeframe middle = timeframes[1];
            longStop[0] = middle.HasPreviousCandle ? Math.Floor(middle.PreviousHaLow / TickSize + 1e-9) * TickSize - TickSize : double.NaN;
            shortStop[0] = middle.HasPreviousCandle ? Math.Ceiling(middle.PreviousHaHigh / TickSize - 1e-9) * TickSize + TickSize : double.NaN;
            Evaluate();
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (setupError != null || e.MarketDataType != MarketDataType.Last || CurrentBar < 0 || Calculate == Calculate.OnBarClose)
                return;
            if (State == State.Realtime && !sessionIterator.IsInSession(Core.Globals.Now, true, true))
                return;

            int sign = e.Price >= e.Ask ? 1 : e.Price <= e.Bid ? -1 : 0;
            if (sign == 0)
                return;

            cvd += sign * e.Volume;
            foreach (Timeframe timeframe in timeframes)
            {
                timeframe.Delta += sign * e.Volume;
                timeframe.CvdHigh = Math.Max(timeframe.CvdHigh, cvd);
                timeframe.CvdLow = Math.Min(timeframe.CvdLow, cvd);
            }
            Evaluate();
        }

        private DateTime NewYorkTime(int barsAgo)
        {
            return TimeZoneInfo.ConvertTime(Time[barsAgo], Core.Globals.GeneralOptions.TimeZoneInfo, newYorkZone);
        }

        private void UpdateCandle(Timeframe timeframe, int index)
        {
            double key = bucketKeys[index][0];
            if (key != timeframe.Key)
            {
                if (!double.IsNaN(timeframe.Key))
                {
                    timeframe.PreviousHaOpen = timeframe.HaOpen;
                    timeframe.PreviousHaClose = timeframe.HaClose;
                    timeframe.PreviousHaHigh = timeframe.HaHigh;
                    timeframe.PreviousHaLow = timeframe.HaLow;
                    timeframe.HasPreviousCandle = true;
                    timeframe.PreviousCvdHigh = timeframe.CvdHigh;
                    timeframe.PreviousCvdLow = timeframe.CvdLow;
                    timeframe.HasPreviousCvd = true;
                }
                timeframe.Key = key;
                timeframe.Delta = 0;
                timeframe.CvdHigh = cvd;
                timeframe.CvdLow = cvd;
            }

            int firstBarsAgo = 0;
            double high = High[0];
            double low = Low[0];
            while (firstBarsAgo < CurrentBar && bucketKeys[index][firstBarsAgo + 1] == key)
            {
                firstBarsAgo++;
                high = Math.Max(high, High[firstBarsAgo]);
                low = Math.Min(low, Low[firstBarsAgo]);
            }
            double open = Open[firstBarsAgo];
            double close = Close[0];

            timeframe.HaClose = (open + high + low + close) / 4;
            timeframe.HaOpen = timeframe.HasPreviousCandle
                ? (timeframe.PreviousHaOpen + timeframe.PreviousHaClose) / 2
                : (open + close) / 2;
            timeframe.HaHigh = Math.Max(high, Math.Max(timeframe.HaOpen, timeframe.HaClose));
            timeframe.HaLow = Math.Min(low, Math.Min(timeframe.HaOpen, timeframe.HaClose));

            timeframe.HaSide = 0;
            if (timeframe.HasPreviousCandle)
            {
                bool brokeHigh = timeframe.HaHigh > timeframe.PreviousHaHigh;
                bool brokeLow = timeframe.HaLow < timeframe.PreviousHaLow;
                if (brokeHigh != brokeLow)
                    timeframe.HaSide = brokeHigh ? 1 : -1;
            }
        }

        private void Evaluate()
        {
            foreach (Timeframe timeframe in timeframes)
            {
                timeframe.VdSide = Math.Sign(timeframe.Delta);
                timeframe.CvdSide = 0;
                if (timeframe.HasPreviousCvd && cvd > timeframe.PreviousCvdHigh)
                    timeframe.CvdSide = 1;
                else if (timeframe.HasPreviousCvd && cvd < timeframe.PreviousCvdLow)
                    timeframe.CvdSide = -1;
            }

            Timeframe slow = timeframes[2];
            int alignedSide = 0;
            foreach (int side in new[] { 1, -1 })
            {
                bool aligned = MinimumBalance == 0 || side * slow.Delta >= MinimumBalance;
                for (int i = 0; i < timeframes.Length && aligned; i++)
                {
                    if (i == 2 && !RequireSlowTimeframe)
                        continue;
                    Timeframe timeframe = timeframes[i];
                    aligned = timeframe.HaSide == side && timeframe.CvdSide == side && timeframe.VdSide == side;
                }
                if (aligned)
                    alignedSide = side;
            }

            Value[0] = alignedSide;
            currentSide = alignedSide;
            slowBalance[0] = slow.Delta;
            fastVdSide[0] = timeframes[0].VdSide;
            middleVdSide[0] = timeframes[1].VdSide;
            if (ShadeAlignedBars)
                BackBrush = alignedSide > 0 ? buyShade : alignedSide < 0 ? sellShade : null;
        }

        private bool InWindow(DateTime newYork)
        {
            TimeSpan timeOfDay = newYork.TimeOfDay;
            return timeOfDay > windowStart && timeOfDay <= windowEnd;
        }

        private void StepJournal()
        {
            DateTime closedTime = Time[1];
            if (openTrade != null)
                ManageTrade(closedTime);
            if (openTrade != null)
                return;

            int side = (int)Value[1];
            if (side == 0 || (int)Value[2] == side || !InWindow(NewYorkTime(1)))
                return;

            DateTime tradingDay = NewYorkTime(1).Date;
            int tradesToday = 0;
            foreach (JournalTrade trade in trades)
                if (TimeZoneInfo.ConvertTime(trade.EntryTime, Core.Globals.GeneralOptions.TimeZoneInfo, newYorkZone).Date == tradingDay)
                    tradesToday++;
            if (tradesToday >= MaximumTradesPerDay)
                return;

            double entry = Close[1];
            double stop = side > 0 ? longStop[1] : shortStop[1];
            double stopTicks = side * (entry - stop) / TickSize;
            if (double.IsNaN(stop) || stopTicks < MinimumStopTicks || stopTicks > MaximumStopTicks)
            {
                skippedSignals++;
                return;
            }

            openTrade = new JournalTrade
            {
                Side = side,
                EntryBar = CurrentBar - 1,
                EntryTime = closedTime,
                Entry = entry,
                Stop = stop,
                InitialStop = stop,
                Target = entry + side * TargetTicks * TickSize,
                Balance = slowBalance[1],
            };
        }

        private void ManageTrade(DateTime closedTime)
        {
            JournalTrade trade = openTrade;
            if (CurrentBar - 1 <= trade.EntryBar)
                return;

            double high = High[1];
            double low = Low[1];
            int side = trade.Side;
            double favorableTicks = (side > 0 ? high - trade.Entry : trade.Entry - low) / TickSize;
            double adverseTicks = (side > 0 ? trade.Entry - low : high - trade.Entry) / TickSize;
            double stopDistanceTicks = side * (trade.Entry - trade.Stop) / TickSize;

            bool stopHit = side > 0 ? low <= trade.Stop : high >= trade.Stop;
            bool targetHit = side > 0 ? high >= trade.Target : low <= trade.Target;
            if (stopHit)
            {
                trade.MaeTicks = Math.Max(trade.MaeTicks, Math.Min(adverseTicks, stopDistanceTicks));
                CloseTrade(closedTime, trade.Stop, trade.MovedToBreakeven ? "Breakeven" : "Stop");
                return;
            }
            if (targetHit)
            {
                trade.MfeTicks = Math.Max(trade.MfeTicks, TargetTicks);
                trade.MaeTicks = Math.Max(trade.MaeTicks, adverseTicks);
                CloseTrade(closedTime, trade.Target, "Target");
                return;
            }

            trade.MfeTicks = Math.Max(trade.MfeTicks, favorableTicks);
            trade.MaeTicks = Math.Max(trade.MaeTicks, adverseTicks);
            if (BreakevenTicks > 0 && !trade.MovedToBreakeven && trade.MfeTicks >= BreakevenTicks)
            {
                trade.Stop = trade.Entry;
                trade.MovedToBreakeven = true;
                if (adverseTicks >= 0)
                {
                    CloseTrade(closedTime, trade.Stop, "Breakeven");
                    return;
                }
            }

            bool flowFlipped = (int)fastVdSide[1] == -side || (int)middleVdSide[1] == -side;
            if (ExitOnFlowFlip && flowFlipped)
                CloseTrade(closedTime, Close[1], "Flow flip");
            else if (MaximumHoldMinutes > 0 && (closedTime - trade.EntryTime).TotalMinutes >= MaximumHoldMinutes)
                CloseTrade(closedTime, Close[1], "Time");
            else if (Bars.IsFirstBarOfSession)
                CloseTrade(closedTime, Close[1], "Session end");
        }

        private void CloseTrade(DateTime exitTime, double exit, string reason)
        {
            JournalTrade trade = openTrade;
            openTrade = null;
            trade.ExitTime = exitTime;
            trade.Exit = exit;
            trade.Reason = reason;
            lock (trades)
                trades.Add(trade);

            if (ShowTradesOnChart)
            {
                double r = trade.PnlTicks(TickSize) / trade.StopTicks(TickSize);
                System.Windows.Media.Brush color = r > 0 ? BuyColor : SellColor;
                string tag = "FlowAlignmentTrade" + trades.Count;
                Draw.Line(this, tag, false, trade.EntryTime, trade.Entry, exitTime, exit, color, DashStyleHelper.Dash, 2);
                Draw.Text(this, tag + "R", false, r.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "R", exitTime, exit, 0, color,
                    LabelFont, System.Windows.TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }

            if (State == State.Realtime)
                WriteJournal();
        }

        private void WriteJournal()
        {
            if (!JournalEnabled || trades.Count == 0)
                return;

            List<JournalTrade> snapshot;
            lock (trades)
                snapshot = new List<JournalTrade>(trades);

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("date,entry_time_ny,side,entry,stop,target,stop_ticks,balance_slow,exit_time_ny,exit,reason,pnl_ticks,r,mfe_ticks,mae_ticks");
            foreach (JournalTrade trade in snapshot)
            {
                DateTime entryNewYork = TimeZoneInfo.ConvertTime(trade.EntryTime, Core.Globals.GeneralOptions.TimeZoneInfo, newYorkZone);
                DateTime exitNewYork = TimeZoneInfo.ConvertTime(trade.ExitTime, Core.Globals.GeneralOptions.TimeZoneInfo, newYorkZone);
                double stopTicks = trade.StopTicks(TickSize);
                double pnlTicks = trade.PnlTicks(TickSize);
                csv.AppendLine(string.Join(",", new[]
                {
                    entryNewYork.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    entryNewYork.ToString("HH:mm", CultureInfo.InvariantCulture),
                    trade.Side > 0 ? "long" : "short",
                    trade.Entry.ToString(CultureInfo.InvariantCulture),
                    trade.InitialStop.ToString(CultureInfo.InvariantCulture),
                    trade.Target.ToString(CultureInfo.InvariantCulture),
                    stopTicks.ToString("0", CultureInfo.InvariantCulture),
                    trade.Balance.ToString("0", CultureInfo.InvariantCulture),
                    exitNewYork.ToString("HH:mm", CultureInfo.InvariantCulture),
                    trade.Exit.ToString(CultureInfo.InvariantCulture),
                    trade.Reason,
                    pnlTicks.ToString("0", CultureInfo.InvariantCulture),
                    (pnlTicks / stopTicks).ToString("0.00", CultureInfo.InvariantCulture),
                    trade.MfeTicks.ToString("0", CultureInfo.InvariantCulture),
                    trade.MaeTicks.ToString("0", CultureInfo.InvariantCulture),
                }));
            }

            try
            {
                string folder = Path.Combine(Core.Globals.UserDataDir, "FlowAlignment");
                Directory.CreateDirectory(folder);
                string fileName = Instrument.FullName + " " + BarsPeriod.Value + "min.csv";
                foreach (char invalid in Path.GetInvalidFileNameChars())
                    fileName = fileName.Replace(invalid, '_');
                File.WriteAllText(Path.Combine(folder, fileName), csv.ToString());
            }
            catch (Exception exception)
            {
                Print("Flow Alignment: could not write the journal: " + exception.Message);
            }
        }

        private string JournalSummary()
        {
            List<JournalTrade> snapshot;
            lock (trades)
                snapshot = new List<JournalTrade>(trades);

            if (snapshot.Count == 0)
                return "Journal: no trades yet" + (skippedSignals > 0 ? " (" + skippedSignals + " skipped by stop size)" : "");

            int wins = 0;
            double totalR = 0, grossWin = 0, grossLoss = 0;
            foreach (JournalTrade trade in snapshot)
            {
                double r = trade.PnlTicks(TickSize) / trade.StopTicks(TickSize);
                totalR += r;
                if (r > 0)
                {
                    wins++;
                    grossWin += r;
                }
                else
                    grossLoss -= r;
            }
            string profitFactor = grossLoss > 0 ? (grossWin / grossLoss).ToString("0.00", CultureInfo.InvariantCulture) : "-";
            return string.Format(CultureInfo.InvariantCulture, "Journal: {0} trades, {1:0}% win, {2:+0.0;-0.0}R, avg {3:+0.00;-0.00}R, PF {4}",
                snapshot.Count, 100.0 * wins / snapshot.Count, totalR, totalR / snapshot.Count, profitFactor);
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (setupError != null || timeframes == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.DirectWrite.TextFormat textFormat = LabelFont.ToDirectWriteTextFormat();
            textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
            SharpDX.Direct2D1.Brush textBrush = TextColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush buyBrush = BuyColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush sellBrush = SellColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush neutralBrush = NeutralColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush backgroundBrush = Brushes.White.ToDxBrush(RenderTarget, 0.85f);
            List<SharpDX.DirectWrite.TextLayout> layouts = new List<SharpDX.DirectWrite.TextLayout>();

            try
            {
                Func<string, SharpDX.DirectWrite.TextLayout> measure = text =>
                {
                    SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, text, textFormat, 600, 60);
                    layouts.Add(layout);
                    return layout;
                };

                float rowHeight = (float)LabelFont.Size * 1.7f;
                float labelWidth = (float)LabelFont.Size * 3.4f;
                float cellWidth = (float)LabelFont.Size * 3.2f;
                string[] headers = { "HA", "CVD", "VD" };

                int alignedSide = currentSide;
                double balance = timeframes[2].Delta;
                string balanceLine = string.Format(CultureInfo.InvariantCulture, "{0} lots: {1:+0;-0;0}", timeframes[2].Label, balance);
                string stateLine = alignedSide > 0 ? "BUY aligned" : alignedSide < 0 ? "SELL aligned" : "Not aligned";
                List<string> lines = new List<string> { balanceLine, stateLine };
                if (JournalEnabled)
                    lines.Add(JournalSummary());

                float width = labelWidth + cellWidth * headers.Length;
                List<SharpDX.DirectWrite.TextLayout> lineLayouts = new List<SharpDX.DirectWrite.TextLayout>();
                foreach (string line in lines)
                {
                    SharpDX.DirectWrite.TextLayout layout = measure(line);
                    lineLayouts.Add(layout);
                    width = Math.Max(width, layout.Metrics.Width);
                }

                const float padding = 6;
                float height = rowHeight * (timeframes.Length + 1 + lines.Count);
                float left = ChartPanel.X + ChartPanel.W - width - padding * 2 - 8;
                float top = ChartPanel.Y + 8;
                RenderTarget.FillRectangle(new SharpDX.RectangleF(left, top, width + padding * 2, height + padding * 2), backgroundBrush);

                float x = left + padding;
                float y = top + padding;
                for (int column = 0; column < headers.Length; column++)
                {
                    SharpDX.DirectWrite.TextLayout header = measure(headers[column]);
                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(x + labelWidth + cellWidth * column + (cellWidth - header.Metrics.Width) / 2, y), header, textBrush);
                }

                for (int row = 0; row < timeframes.Length; row++)
                {
                    Timeframe timeframe = timeframes[timeframes.Length - 1 - row];
                    float rowTop = y + rowHeight * (row + 1);
                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, rowTop), measure(timeframe.Label), textBrush);
                    int[] sides = { timeframe.HaSide, timeframe.CvdSide, timeframe.VdSide };
                    for (int column = 0; column < sides.Length; column++)
                    {
                        SharpDX.Direct2D1.Brush cell = sides[column] > 0 ? buyBrush : sides[column] < 0 ? sellBrush : neutralBrush;
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(x + labelWidth + cellWidth * column + 2, rowTop + 2, cellWidth - 4, rowHeight - 4), cell);
                    }
                }

                for (int i = 0; i < lineLayouts.Count; i++)
                {
                    float lineTop = y + rowHeight * (timeframes.Length + 1 + i);
                    SharpDX.Direct2D1.Brush brush = i == 1 && alignedSide != 0 ? (alignedSide > 0 ? buyBrush : sellBrush) : textBrush;
                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, lineTop), lineLayouts[i], brush);
                }
            }
            finally
            {
                foreach (SharpDX.DirectWrite.TextLayout layout in layouts)
                    layout.Dispose();
                textFormat.Dispose();
                textBrush.Dispose();
                buyBrush.Dispose();
                sellBrush.Dispose();
                neutralBrush.Dispose();
                backgroundBrush.Dispose();
            }
        }

        #region Properties

        [Range(1, 240), NinjaScriptProperty]
        [Display(Name = "Middle timeframe (minutes)", Description = "Built from the chart's own candles; must be a multiple of the chart's minutes.", Order = 1, GroupName = "1. Alignment")]
        public int MiddleMinutes { get; set; }

        [Range(1, 1440), NinjaScriptProperty]
        [Display(Name = "Slow timeframe (minutes)", Description = "Its candle's lot balance (buys minus sells) is the strength filter.", Order = 2, GroupName = "1. Alignment")]
        public int SlowMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Slow timeframe must align", Description = "Off: only the chart and middle timeframes need HA, CVD and VD on the same side; the slow one still gives the lot balance.", Order = 3, GroupName = "1. Alignment")]
        public bool RequireSlowTimeframe { get; set; }

        [Range(0, 100000), NinjaScriptProperty]
        [Display(Name = "Minimum slow balance (lots)", Description = "Lots the slow candle must have left over on the signal's side. 0 turns the filter off.", Order = 4, GroupName = "1. Alignment")]
        public int MinimumBalance { get; set; }

        [Display(Name = "Shade aligned candles", Order = 5, GroupName = "1. Alignment")]
        public bool ShadeAlignedBars { get; set; }

        [Range(1, 100)]
        [Display(Name = "Shade opacity (%)", Order = 6, GroupName = "1. Alignment")]
        public int ShadeOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Journal", Description = "Trade every new alignment inside the window by the rules below and write the trades to Documents\\NinjaTrader 8\\FlowAlignment.", Order = 1, GroupName = "2. Replay journal")]
        public bool JournalEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Window start", Description = "HH:MM, New York time.", Order = 2, GroupName = "2. Replay journal")]
        public string WindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Window end", Description = "HH:MM, New York time.", Order = 3, GroupName = "2. Replay journal")]
        public string WindowEnd { get; set; }

        [Range(1, 10000), NinjaScriptProperty]
        [Display(Name = "Target (ticks)", Order = 4, GroupName = "2. Replay journal")]
        public int TargetTicks { get; set; }

        [Range(1, 10000), NinjaScriptProperty]
        [Display(Name = "Minimum stop (ticks)", Description = "The stop sits one tick past the previous middle-timeframe Heikin-Ashi candle; signals with a closer stop are skipped.", Order = 5, GroupName = "2. Replay journal")]
        public int MinimumStopTicks { get; set; }

        [Range(1, 10000), NinjaScriptProperty]
        [Display(Name = "Maximum stop (ticks)", Description = "Signals with a farther stop are skipped.", Order = 6, GroupName = "2. Replay journal")]
        public int MaximumStopTicks { get; set; }

        [Range(0, 10000), NinjaScriptProperty]
        [Display(Name = "Breakeven after (ticks)", Description = "Move the stop to the entry once the trade has gone this far in favor. 0 = never.", Order = 7, GroupName = "2. Replay journal")]
        public int BreakevenTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit on flow flip", Description = "Close at a candle's close when the chart's or the middle timeframe's VD turns against the trade.", Order = 8, GroupName = "2. Replay journal")]
        public bool ExitOnFlowFlip { get; set; }

        [Range(0, 1440), NinjaScriptProperty]
        [Display(Name = "Maximum hold (minutes)", Description = "0 = no time exit.", Order = 9, GroupName = "2. Replay journal")]
        public int MaximumHoldMinutes { get; set; }

        [Range(1, 100), NinjaScriptProperty]
        [Display(Name = "Maximum trades per day", Order = 10, GroupName = "2. Replay journal")]
        public int MaximumTradesPerDay { get; set; }

        [Display(Name = "Show trades on chart", Order = 11, GroupName = "2. Replay journal")]
        public bool ShowTradesOnChart { get; set; }

        [XmlIgnore]
        [Display(Name = "Buy color", Order = 1, GroupName = "3. Appearance")]
        public System.Windows.Media.Brush BuyColor { get; set; }

        [Browsable(false)]
        public string BuyColorSerializable
        {
            get { return Serialize.BrushToString(BuyColor); }
            set { BuyColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Sell color", Order = 2, GroupName = "3. Appearance")]
        public System.Windows.Media.Brush SellColor { get; set; }

        [Browsable(false)]
        public string SellColorSerializable
        {
            get { return Serialize.BrushToString(SellColor); }
            set { SellColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Neutral color", Order = 3, GroupName = "3. Appearance")]
        public System.Windows.Media.Brush NeutralColor { get; set; }

        [Browsable(false)]
        public string NeutralColorSerializable
        {
            get { return Serialize.BrushToString(NeutralColor); }
            set { NeutralColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Text color", Order = 4, GroupName = "3. Appearance")]
        public System.Windows.Media.Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Font", Order = 5, GroupName = "3. Appearance")]
        public SimpleFont LabelFont { get; set; }

        #endregion
    }
}
