#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// Heikin-Ashi candles drawn over the chart's own bars, with candles that
    /// break neither the previous high nor low grayed out and a triangle where
    /// the color turns after a run.
    /// </summary>
    public class FlowHA : Indicator
    {
        private Series<double> haOpen;
        private Series<double> haHigh;
        private Series<double> haLow;
        private Series<double> haClose;
        private EMA smoothedOpen;
        private EMA smoothedHigh;
        private EMA smoothedLow;
        private EMA smoothedClose;

        private class CczZone
        {
            public int StartBar;
            public double Top;
            public double Bottom;
            public int EndBar = -1;
        }

        private readonly List<CczZone> cczZones = new List<CczZone>();
        private int lastCczBar = -1;

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
                Description = "Heikin-Ashi candles over the price bars, inside candles grayed, reversal triangles.";
                Name = "Flow HA";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                HidePriceBars = true;
                SmoothingPeriod = 0;
                ShowTrafficArea = true;
                MarkInsideCandles = true;
                MinBarsForReversal = 2;
                ShowReversalTriangles = true;
                ShowCczZones = true;
                CczBodyPercent = 7;
                CczOpacity = 10;
                SignalOffsetTicks = 3;

                UpColor = Rgb(0x37, 0xF0, 0x9C);
                DownColor = Rgb(0x1A, 0x1C, 0x26);
                InsideColor = Rgb(0xD1, 0xD5, 0xDB);
                ActiveCczColor = Rgb(0xB8, 0x86, 0x2B);
                FinishedCczColor = Rgb(0x5F, 0x67, 0x73);
                WickColor = Rgb(0x1A, 0x1C, 0x26);
                TrafficColor = Rgb(0x5F, 0x67, 0x73);
            }
            else if (State == State.DataLoaded)
            {
                if (SmoothingPeriod > 0)
                {
                    smoothedOpen = EMA(Open, SmoothingPeriod);
                    smoothedHigh = EMA(High, SmoothingPeriod);
                    smoothedLow = EMA(Low, SmoothingPeriod);
                    smoothedClose = EMA(Close, SmoothingPeriod);
                }
                haOpen = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haHigh = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haLow = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haClose = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        protected override void OnBarUpdate()
        {
            bool smoothed = SmoothingPeriod > 0;
            double barOpen = smoothed ? smoothedOpen[0] : Open[0];
            double barHigh = smoothed ? smoothedHigh[0] : High[0];
            double barLow = smoothed ? smoothedLow[0] : Low[0];
            double barClose = smoothed ? smoothedClose[0] : Close[0];

            double open = CurrentBar == 0 ? (barOpen + barClose) / 2 : (haOpen[1] + haClose[1]) / 2;
            double close = (barOpen + barHigh + barLow + barClose) / 4;
            haOpen[0] = open;
            haClose[0] = close;
            haHigh[0] = Math.Max(barHigh, Math.Max(open, close));
            haLow[0] = Math.Min(barLow, Math.Min(open, close));

            if (HidePriceBars)
            {
                BarBrush = Brushes.Transparent;
                CandleOutlineBrush = Brushes.Transparent;
            }

            if (ShowCczZones && IsFirstTickOfBar)
            {
                bool barIsComplete = Calculate == Calculate.OnBarClose || (State == State.Historical && !Bars.IsTickReplay);
                int closedBarsAgo = barIsComplete ? 0 : 1;
                if (CurrentBar >= closedBarsAgo && CurrentBar - closedBarsAgo > lastCczBar)
                    UpdateCczZones(closedBarsAgo);
            }
        }

        private void UpdateCczZones(int closedBarsAgo)
        {
            int closedBar = CurrentBar - closedBarsAgo;
            lastCczBar = closedBar;
            double close = Close[closedBarsAgo];
            double body = Math.Abs(haClose[closedBarsAgo] - haOpen[closedBarsAgo]);
            double top = haHigh[closedBarsAgo];
            double bottom = haLow[closedBarsAgo];

            lock (cczZones)
            {
                foreach (CczZone zone in cczZones)
                    if (zone.EndBar < 0 && zone.StartBar < closedBar && (close > zone.Top || close < zone.Bottom))
                        zone.EndBar = closedBar;

                if (top > bottom && body <= (top - bottom) * CczBodyPercent / 100.0)
                    cczZones.Add(new CczZone { StartBar = closedBar, Top = top, Bottom = bottom });
            }
        }

        private bool IsRising(int barIndex)
        {
            return haClose.GetValueAt(barIndex) >= haOpen.GetValueAt(barIndex);
        }

        private bool IsAbsorptionCandle(int barIndex)
        {
            if (barIndex <= 0)
                return false;
            bool brokeHigh = haHigh.GetValueAt(barIndex) > haHigh.GetValueAt(barIndex - 1);
            bool brokeLow = haLow.GetValueAt(barIndex) < haLow.GetValueAt(barIndex - 1);
            return brokeHigh == brokeLow;
        }

        private bool IsReversal(int barIndex)
        {
            if (barIndex < MinBarsForReversal)
                return false;

            bool previousSide = IsRising(barIndex - 1);
            if (IsRising(barIndex) == previousSide)
                return false;

            for (int offset = 1; offset <= MinBarsForReversal; offset++)
                if (IsRising(barIndex - offset) != previousSide)
                    return false;
            return true;
        }

        private void FillTriangle(SharpDX.Vector2 tip, float halfWidth, float height, SharpDX.Direct2D1.Brush brush)
        {
            SharpDX.Direct2D1.PathGeometry geometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
            SharpDX.Direct2D1.GeometrySink sink = geometry.Open();
            try
            {
                sink.BeginFigure(tip, SharpDX.Direct2D1.FigureBegin.Filled);
                sink.AddLine(new SharpDX.Vector2(tip.X - halfWidth, tip.Y + height));
                sink.AddLine(new SharpDX.Vector2(tip.X + halfWidth, tip.Y + height));
                sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                sink.Close();
                RenderTarget.FillGeometry(geometry, brush);
            }
            finally
            {
                sink.Dispose();
                geometry.Dispose();
            }
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;
            if (ChartBars == null)
                return;

            for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
            {
                if (!haLow.IsValidDataPointAt(barIndex))
                    continue;
                MinValue = Math.Min(MinValue, haLow.GetValueAt(barIndex));
                MaxValue = Math.Max(MaxValue, haHigh.GetValueAt(barIndex));
            }
        }

        private void DrawTrafficArea(ChartControl chartControl, ChartScale chartScale, int previousBar)
        {
            if (!haHigh.IsValidDataPointAt(previousBar) || previousBar > ChartBars.ToIndex)
                return;

            double high = haHigh.GetValueAt(previousBar);
            double low = haLow.GetValueAt(previousBar);
            float x1 = chartControl.GetXByBarIndex(ChartBars, previousBar);
            float x2 = ChartPanel.X + ChartPanel.W;

            SharpDX.Direct2D1.Brush brush = TrafficColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.StrokeStyle dotted = new SharpDX.Direct2D1.StrokeStyle(Core.Globals.D2DFactory,
                new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dot });
            try
            {
                foreach (double price in new[] { high, (high + low) / 2, low })
                {
                    float y = chartScale.GetYByValue(price);
                    RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(x2, y), brush, 1.5f, dotted);
                }
            }
            finally
            {
                dotted.Dispose();
                brush.Dispose();
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.Direct2D1.Brush upBrush = UpColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush downBrush = DownColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush insideBrush = InsideColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush wickBrush = WickColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush activeCczFill = ActiveCczColor.ToDxBrush(RenderTarget, CczOpacity / 100f);
            SharpDX.Direct2D1.Brush finishedCczFill = FinishedCczColor.ToDxBrush(RenderTarget, CczOpacity / 100f);
            SharpDX.Direct2D1.Brush activeCczLine = ActiveCczColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush finishedCczLine = FinishedCczColor.ToDxBrush(RenderTarget);

            try
            {
                float barWidth = Math.Max(1f, (float)chartControl.GetBarPaintWidth(ChartBars));
                float triangleHalfWidth = Math.Max(4f, barWidth / 2);
                int formingBar = State == State.Realtime ? Bars.Count - 1 : Bars.Count;

                if (ShowTrafficArea && Bars.Count >= 2)
                    DrawTrafficArea(chartControl, chartScale, Bars.Count - 2);

                if (ShowCczZones)
                {
                    float panelRight = ChartPanel.X + ChartPanel.W;
                    lock (cczZones)
                        foreach (CczZone zone in cczZones)
                        {
                            bool finished = zone.EndBar >= 0;
                            if (zone.StartBar > ChartBars.ToIndex || (finished && zone.EndBar < ChartBars.FromIndex))
                                continue;

                            float x1 = chartControl.GetXByBarIndex(ChartBars, zone.StartBar) - barWidth / 2;
                            float x2 = finished ? chartControl.GetXByBarIndex(ChartBars, zone.EndBar) : panelRight;
                            float top = chartScale.GetYByValue(zone.Top);
                            float bottom = chartScale.GetYByValue(zone.Bottom);
                            if (x2 <= x1 || bottom <= top)
                                continue;

                            SharpDX.RectangleF box = new SharpDX.RectangleF(x1, top, x2 - x1, bottom - top);
                            RenderTarget.FillRectangle(box, finished ? finishedCczFill : activeCczFill);
                            RenderTarget.DrawRectangle(box, finished ? finishedCczLine : activeCczLine, 1f);
                        }
                }

                for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
                {
                    if (!haClose.IsValidDataPointAt(barIndex))
                        continue;

                    float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
                    double open = haOpen.GetValueAt(barIndex);
                    double close = haClose.GetValueAt(barIndex);
                    bool rising = close >= open;

                    SharpDX.Direct2D1.Brush bodyBrush = rising ? upBrush : downBrush;
                    if (MarkInsideCandles && IsAbsorptionCandle(barIndex))
                        bodyBrush = insideBrush;

                    float highY = chartScale.GetYByValue(haHigh.GetValueAt(barIndex));
                    float lowY = chartScale.GetYByValue(haLow.GetValueAt(barIndex));
                    RenderTarget.DrawLine(new SharpDX.Vector2(x, highY), new SharpDX.Vector2(x, lowY), wickBrush, 1f);

                    float bodyTop = chartScale.GetYByValue(Math.Max(open, close));
                    float bodyBottom = chartScale.GetYByValue(Math.Min(open, close));
                    SharpDX.RectangleF body = new SharpDX.RectangleF(x - barWidth / 2, bodyTop, barWidth, Math.Max(1f, bodyBottom - bodyTop));
                    RenderTarget.FillRectangle(body, bodyBrush);
                    RenderTarget.DrawRectangle(body, wickBrush, 1f);

                    if (!ShowReversalTriangles || barIndex >= formingBar || !IsReversal(barIndex))
                        continue;

                    float offset = (float)(SignalOffsetTicks * TickSize);
                    if (rising)
                    {
                        float tipY = chartScale.GetYByValue(haLow.GetValueAt(barIndex) - offset);
                        FillTriangle(new SharpDX.Vector2(x, tipY), triangleHalfWidth, triangleHalfWidth * 1.5f, upBrush);
                    }
                    else
                    {
                        float tipY = chartScale.GetYByValue(haHigh.GetValueAt(barIndex) + offset);
                        FillTriangle(new SharpDX.Vector2(x, tipY), triangleHalfWidth, -triangleHalfWidth * 1.5f, downBrush);
                    }
                }
            }
            finally
            {
                upBrush.Dispose();
                downBrush.Dispose();
                insideBrush.Dispose();
                wickBrush.Dispose();
                activeCczFill.Dispose();
                finishedCczFill.Dispose();
                activeCczLine.Dispose();
                finishedCczLine.Dispose();
            }
        }

        #region Properties

        [Display(Name = "Hide price bars", Description = "Paint the chart's own candles transparent so only the Heikin-Ashi candles show.", Order = 1, GroupName = "1. Heikin-Ashi")]
        public bool HidePriceBars { get; set; }

        [Display(Name = "Gray absorption candles", Description = "Gray a candle that breaks neither the previous Heikin-Ashi high nor low, or breaks both.", Order = 2, GroupName = "1. Heikin-Ashi")]
        public bool MarkInsideCandles { get; set; }

        [Display(Name = "Show reversal triangles", Order = 7, GroupName = "1. Heikin-Ashi")]
        public bool ShowReversalTriangles { get; set; }

        [Range(1, 20), NinjaScriptProperty]
        [Display(Name = "Candles before a reversal", Description = "Same-color candles needed before a color change gets a triangle.", Order = 3, GroupName = "1. Heikin-Ashi")]
        public int MinBarsForReversal { get; set; }

        [Range(0, 100)]
        [Display(Name = "Triangle offset (ticks)", Order = 4, GroupName = "1. Heikin-Ashi")]
        public int SignalOffsetTicks { get; set; }

        [Range(0, 200), NinjaScriptProperty]
        [Display(Name = "Smoothing period", Description = "0 = classic Heikin-Ashi. Above 0, the candles' open, high, low and close are smoothed with an EMA of this period first.", Order = 5, GroupName = "1. Heikin-Ashi")]
        public int SmoothingPeriod { get; set; }

        [Display(Name = "Show traffic area", Description = "Dotted lines at the previous Heikin-Ashi candle's high, low and middle: the stop and the zone for adding lots.", Order = 6, GroupName = "1. Heikin-Ashi")]
        public bool ShowTrafficArea { get; set; }

        [XmlIgnore]
        [Display(Name = "Traffic area", Order = 6, GroupName = "2. Colors")]
        public System.Windows.Media.Brush TrafficColor { get; set; }

        [Browsable(false)]
        public string TrafficColorSerializable
        {
            get { return Serialize.BrushToString(TrafficColor); }
            set { TrafficColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Rising candle", Order = 1, GroupName = "2. Colors")]
        public System.Windows.Media.Brush UpColor { get; set; }

        [Browsable(false)]
        public string UpColorSerializable
        {
            get { return Serialize.BrushToString(UpColor); }
            set { UpColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Falling candle", Order = 2, GroupName = "2. Colors")]
        public System.Windows.Media.Brush DownColor { get; set; }

        [Browsable(false)]
        public string DownColorSerializable
        {
            get { return Serialize.BrushToString(DownColor); }
            set { DownColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Inside candle", Order = 3, GroupName = "2. Colors")]
        public System.Windows.Media.Brush InsideColor { get; set; }

        [Browsable(false)]
        public string InsideColorSerializable
        {
            get { return Serialize.BrushToString(InsideColor); }
            set { InsideColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Show CCZ zones", Description = "Box the range of each Heikin-Ashi candle whose body is a sliver of its range, until a candle closes outside the box.", Order = 1, GroupName = "3. CCZ zones")]
        public bool ShowCczZones { get; set; }

        [Range(1, 50), NinjaScriptProperty]
        [Display(Name = "Body (% of range)", Description = "Largest body, as a share of the candle's range, that still opens a zone.", Order = 2, GroupName = "3. CCZ zones")]
        public int CczBodyPercent { get; set; }

        [Range(0, 100)]
        [Display(Name = "Zone opacity (%)", Order = 3, GroupName = "3. CCZ zones")]
        public int CczOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Open zone", Order = 4, GroupName = "3. CCZ zones")]
        public System.Windows.Media.Brush ActiveCczColor { get; set; }

        [Browsable(false)]
        public string ActiveCczColorSerializable
        {
            get { return Serialize.BrushToString(ActiveCczColor); }
            set { ActiveCczColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Finished zone", Order = 5, GroupName = "3. CCZ zones")]
        public System.Windows.Media.Brush FinishedCczColor { get; set; }

        [Browsable(false)]
        public string FinishedCczColorSerializable
        {
            get { return Serialize.BrushToString(FinishedCczColor); }
            set { FinishedCczColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Wick and outline", Order = 4, GroupName = "2. Colors")]
        public System.Windows.Media.Brush WickColor { get; set; }

        [Browsable(false)]
        public string WickColorSerializable
        {
            get { return Serialize.BrushToString(WickColor); }
            set { WickColor = Serialize.StringToBrush(value); }
        }

        #endregion
    }
}
