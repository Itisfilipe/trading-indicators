#region Using declarations
using System;
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
                MarkInsideCandles = true;
                MinBarsForReversal = 2;
                SignalOffsetTicks = 3;

                UpColor = Brushes.LimeGreen;
                DownColor = Brushes.Red;
                InsideColor = Brushes.Silver;
                WickColor = Brushes.Black;
            }
            else if (State == State.DataLoaded)
            {
                haOpen = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haHigh = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haLow = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haClose = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        protected override void OnBarUpdate()
        {
            double open = CurrentBar == 0 ? (Open[0] + Close[0]) / 2 : (haOpen[1] + haClose[1]) / 2;
            double close = (Open[0] + High[0] + Low[0] + Close[0]) / 4;
            haOpen[0] = open;
            haClose[0] = close;
            haHigh[0] = Math.Max(High[0], Math.Max(open, close));
            haLow[0] = Math.Min(Low[0], Math.Min(open, close));

            if (HidePriceBars)
            {
                BarBrush = Brushes.Transparent;
                CandleOutlineBrush = Brushes.Transparent;
            }
        }

        private bool IsRising(int barIndex)
        {
            return haClose.GetValueAt(barIndex) >= haOpen.GetValueAt(barIndex);
        }

        private bool IsInside(int barIndex)
        {
            return barIndex > 0
                && haHigh.GetValueAt(barIndex) <= haHigh.GetValueAt(barIndex - 1)
                && haLow.GetValueAt(barIndex) >= haLow.GetValueAt(barIndex - 1);
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

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.Direct2D1.Brush upBrush = UpColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush downBrush = DownColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush insideBrush = InsideColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush wickBrush = WickColor.ToDxBrush(RenderTarget);

            try
            {
                float barWidth = Math.Max(1f, (float)chartControl.GetBarPaintWidth(ChartBars));
                float triangleHalfWidth = Math.Max(4f, barWidth / 2);
                int formingBar = State == State.Realtime ? Bars.Count - 1 : Bars.Count;

                for (int barIndex = ChartBars.FromIndex; barIndex <= ChartBars.ToIndex; barIndex++)
                {
                    if (!haClose.IsValidDataPointAt(barIndex))
                        continue;

                    float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
                    double open = haOpen.GetValueAt(barIndex);
                    double close = haClose.GetValueAt(barIndex);
                    bool rising = close >= open;

                    SharpDX.Direct2D1.Brush bodyBrush = rising ? upBrush : downBrush;
                    if (MarkInsideCandles && IsInside(barIndex))
                        bodyBrush = insideBrush;

                    float highY = chartScale.GetYByValue(haHigh.GetValueAt(barIndex));
                    float lowY = chartScale.GetYByValue(haLow.GetValueAt(barIndex));
                    RenderTarget.DrawLine(new SharpDX.Vector2(x, highY), new SharpDX.Vector2(x, lowY), wickBrush, 1f);

                    float bodyTop = chartScale.GetYByValue(Math.Max(open, close));
                    float bodyBottom = chartScale.GetYByValue(Math.Min(open, close));
                    SharpDX.RectangleF body = new SharpDX.RectangleF(x - barWidth / 2, bodyTop, barWidth, Math.Max(1f, bodyBottom - bodyTop));
                    RenderTarget.FillRectangle(body, bodyBrush);
                    RenderTarget.DrawRectangle(body, wickBrush, 1f);

                    if (barIndex >= formingBar || !IsReversal(barIndex))
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
            }
        }

        #region Properties

        [Display(Name = "Hide price bars", Description = "Paint the chart's own candles transparent so only the Heikin-Ashi candles show.", Order = 1, GroupName = "1. Heikin-Ashi")]
        public bool HidePriceBars { get; set; }

        [Display(Name = "Gray inside candles", Description = "Gray a candle that breaks neither the previous Heikin-Ashi high nor low.", Order = 2, GroupName = "1. Heikin-Ashi")]
        public bool MarkInsideCandles { get; set; }

        [Range(1, 20), NinjaScriptProperty]
        [Display(Name = "Candles before a reversal", Description = "Same-color candles needed before a color change gets a triangle.", Order = 3, GroupName = "1. Heikin-Ashi")]
        public int MinBarsForReversal { get; set; }

        [Range(0, 100)]
        [Display(Name = "Triangle offset (ticks)", Order = 4, GroupName = "1. Heikin-Ashi")]
        public int SignalOffsetTicks { get; set; }

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
