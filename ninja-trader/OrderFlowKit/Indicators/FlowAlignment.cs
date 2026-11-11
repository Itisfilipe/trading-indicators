#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
    /// two larger ones built from it, and the larger timeframe's lot balance.
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

        private Timeframe[] timeframes;
        private Series<double>[] bucketKeys;
        private double cvd;
        private string setupError;
        private TimeZoneInfo newYorkZone;
        private SessionIterator sessionIterator;
        private System.Windows.Media.Brush buyShade, sellShade;
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
                Description = "HA, CVD and VD direction on three timeframes, and the larger one's lot balance. Needs Tick Replay for history.";
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
                    bucketKeys[i][0] = (minute + timeframes[i].Minutes - 1) / timeframes[i].Minutes;            }

            for (int i = 0; i < timeframes.Length; i++)
                UpdateCandle(timeframes[i], i);
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
            if (ShadeAlignedBars)
                BackBrush = alignedSide > 0 ? buyShade : alignedSide < 0 ? sellShade : null;
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

        [XmlIgnore]
        [Display(Name = "Buy color", Order = 1, GroupName = "2. Appearance")]
        public System.Windows.Media.Brush BuyColor { get; set; }

        [Browsable(false)]
        public string BuyColorSerializable
        {
            get { return Serialize.BrushToString(BuyColor); }
            set { BuyColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Sell color", Order = 2, GroupName = "2. Appearance")]
        public System.Windows.Media.Brush SellColor { get; set; }

        [Browsable(false)]
        public string SellColorSerializable
        {
            get { return Serialize.BrushToString(SellColor); }
            set { SellColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Neutral color", Order = 3, GroupName = "2. Appearance")]
        public System.Windows.Media.Brush NeutralColor { get; set; }

        [Browsable(false)]
        public string NeutralColorSerializable
        {
            get { return Serialize.BrushToString(NeutralColor); }
            set { NeutralColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Text color", Order = 4, GroupName = "2. Appearance")]
        public System.Windows.Media.Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Font", Order = 5, GroupName = "2. Appearance")]
        public SimpleFont LabelFont { get; set; }

        #endregion
    }
}
