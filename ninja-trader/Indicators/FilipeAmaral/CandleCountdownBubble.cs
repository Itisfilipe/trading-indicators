#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// Time left in the chart's current candle, in a rounded box beside the
    /// last candle at its price.
    /// </summary>
    public class CandleCountdownBubble : Indicator
    {
        private System.Windows.Threading.DispatcherTimer refreshTimer;

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
                Description = "Time left in the chart's candle, in a box beside the last candle. Minute charts.";
                Name = "Candle Countdown Bubble";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;

                BoxColor = Rgb(0x6B, 0x6B, 0x6B);
                BoxOpacity = 90;
                TextColor = System.Windows.Media.Brushes.White;
                LabelFont = new SimpleFont("Arial", 22);
                OffsetPixels = 12;
            }
            else if (State == State.Historical)
            {
                if (refreshTimer == null && ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
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

        protected override void OnBarUpdate() { }

        // Playback charts must read the replay clock, not the wall clock.
        private DateTime PlatformNow()
        {
            return Connection.PlaybackConnection != null ? Connection.PlaybackConnection.Now : Core.Globals.Now;
        }

        private static string FormatCountdown(TimeSpan left)
        {
            if (left < TimeSpan.Zero)
                left = TimeSpan.Zero;
            if (left.TotalHours >= 1)
                return ((int)left.TotalHours).ToString() + ":" + left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00");
            return left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00");
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest || Bars.Count == 0)
                return;
            if (Bars.BarsPeriod.BarsPeriodType != BarsPeriodType.Minute)
                return;

            int lastIndex = Bars.Count - 1;
            if (lastIndex > ChartBars.ToIndex)
                return;

            // Time-based bars are stamped with their close time.
            string text = FormatCountdown(Bars.GetTime(lastIndex) - PlatformNow());

            SharpDX.DirectWrite.TextFormat textFormat = LabelFont.ToDirectWriteTextFormat();
            SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, text, textFormat, 400, 200);
            SharpDX.Direct2D1.Brush boxBrush = BoxColor.ToDxBrush(RenderTarget, BoxOpacity / 100f);
            SharpDX.Direct2D1.Brush textBrush = TextColor.ToDxBrush(RenderTarget);
            try
            {
                float paddingX = (float)LabelFont.Size * 0.6f;
                float paddingY = (float)LabelFont.Size * 0.35f;
                float width = layout.Metrics.Width + 2 * paddingX;
                float height = layout.Metrics.Height + 2 * paddingY;
                float barWidth = (float)chartControl.GetBarPaintWidth(ChartBars);
                float left = chartControl.GetXByBarIndex(ChartBars, lastIndex) + barWidth / 2 + OffsetPixels;
                float top = chartScale.GetYByValue(Bars.GetClose(lastIndex)) - height / 2;

                SharpDX.Direct2D1.RoundedRectangle box = new SharpDX.Direct2D1.RoundedRectangle
                {
                    Rect = new SharpDX.RectangleF(left, top, width, height),
                    RadiusX = height / 5,
                    RadiusY = height / 5
                };
                RenderTarget.FillRoundedRectangle(box, boxBrush);
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(left + paddingX, top + paddingY), layout, textBrush,
                    SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
            finally
            {
                layout.Dispose();
                textFormat.Dispose();
                boxBrush.Dispose();
                textBrush.Dispose();
            }
        }

        #region Properties

        [XmlIgnore]
        [Display(Name = "Box color", Order = 1, GroupName = "Bubble")]
        public System.Windows.Media.Brush BoxColor { get; set; }

        [Browsable(false)]
        public string BoxColorSerializable
        {
            get { return Serialize.BrushToString(BoxColor); }
            set { BoxColor = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "Box opacity (%)", Order = 2, GroupName = "Bubble")]
        public int BoxOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Text color", Order = 3, GroupName = "Bubble")]
        public System.Windows.Media.Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Font", Order = 4, GroupName = "Bubble")]
        public SimpleFont LabelFont { get; set; }

        [Range(0, 200)]
        [Display(Name = "Gap from candle (px)", Order = 5, GroupName = "Bubble")]
        public int OffsetPixels { get; set; }

        #endregion
    }
}
