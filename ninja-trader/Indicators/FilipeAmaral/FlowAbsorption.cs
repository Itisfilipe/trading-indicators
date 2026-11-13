#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
    /// Absorption zones: prices where aggressor volume concentrated over the
    /// last window of 1-minute bars without a sustained break. Zones change
    /// only when a 1-minute bar closes; a zone below price is support (blue),
    /// above it resistance (red), and a broken zone that still qualifies comes
    /// back one bar later on its new side. A zone's number is the volume traded
    /// at its price inside the window.
    /// </summary>
    public class FlowAbsorption : Indicator
    {
        private class AbsorptionZone
        {
            public double Price;
            public bool IsSupport;
            public int StartBar;
            public int BarsBeyond;
        }

        private const int MinuteSeries = 1;
        private const float BandHeight = 7f;
        private const float FillOpacity = 0.4f;

        private Dictionary<double, double> formingCells = new Dictionary<double, double>();
        private readonly List<Dictionary<double, double>> window = new List<Dictionary<double, double>>();
        private readonly List<AbsorptionZone> zones = new List<AbsorptionZone>();
        private readonly Dictionary<double, double> zoneVolume = new Dictionary<double, double>();
        private int formingBar = -1;
        private SessionIterator minuteSession;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Prices where aggressor volume concentrated without a sustained break, with that volume. Needs Tick Replay for history.";
                Name = "Flow Absorption";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                WindowBars = 180;
                Sensitivity = 2.5;
                MinimumTouches = 2;
                BrokenBars = 3;
                BreakBufferTicks = 4;
                MaximumZones = 8;

                SupportColor = Brushes.DodgerBlue;
                ResistanceColor = Brushes.Red;
                TextColor = Brushes.Black;
                TextSize = 12;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                minuteSession = new SessionIterator(BarsArray[MinuteSeries]);
            }
            else if (State == State.Historical)
            {
                if (Calculate == Calculate.OnBarClose)
                    Draw.TextFixed(this, "FlowAbsorptionCalculate", "Flow Absorption: set Calculate to On each tick or On price change", TextPosition.BottomLeft);
                if (!Bars.IsTickReplay)
                    Draw.TextFixed(this, "FlowAbsorptionTickReplay", "Flow Absorption: enable Tick Replay on the data series to see past candles", TextPosition.BottomRight);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != MinuteSeries || CurrentBars[MinuteSeries] < 1 || !IsFirstTickOfBar)
                return;
            // OnMarketData runs after OnBarUpdate, so the cells still hold the whole bar that just closed.
            if (formingBar == CurrentBars[MinuteSeries] - 1)
                CloseMinute(Closes[MinuteSeries][1], CurrentBars[0]);
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (BarsInProgress != MinuteSeries || e.MarketDataType != MarketDataType.Last || CurrentBars[MinuteSeries] < 0)
                return;
            // Live trades keep arriving outside the trading-hours template while the minute series stands still.
            if (State == State.Realtime && !minuteSession.IsInSession(e.Time, true, true))
                return;

            if (CurrentBars[MinuteSeries] != formingBar)
            {
                formingCells = new Dictionary<double, double>();
                formingBar = CurrentBars[MinuteSeries];
            }

            bool aggressor = e.Price >= e.Ask || e.Price <= e.Bid;
            if (!aggressor)
                return;
            double price = Instrument.MasterInstrument.RoundToTickSize(e.Price);
            double volume;
            formingCells.TryGetValue(price, out volume);
            formingCells[price] = volume + e.Volume;
        }

        private void CloseMinute(double close, int startBar)
        {
            window.Add(formingCells);
            if (window.Count > WindowBars)
                window.RemoveAt(0);

            double sum = 0;
            double sumOfSquares = 0;
            int count = 0;
            foreach (Dictionary<double, double> cells in window)
                foreach (double volume in cells.Values)
                {
                    sum += volume;
                    sumOfSquares += volume * volume;
                    count++;
                }
            if (count == 0)
                return;
            double mean = sum / count;
            double deviation = Math.Sqrt(Math.Max(0, sumOfSquares / count - mean * mean));
            double threshold = mean + Sensitivity * deviation;

            Dictionary<double, int> touches = new Dictionary<double, int>();
            Dictionary<double, double> traded = new Dictionary<double, double>();
            foreach (Dictionary<double, double> cells in window)
                foreach (KeyValuePair<double, double> cell in cells)
                {
                    double total;
                    traded.TryGetValue(cell.Key, out total);
                    traded[cell.Key] = total + cell.Value;
                    if (cell.Value <= threshold)
                        continue;
                    int touched;
                    touches.TryGetValue(cell.Key, out touched);
                    touches[cell.Key] = touched + 1;
                }

            double buffer = BreakBufferTicks * TickSize;
            lock (zones)
            {
                HashSet<double> brokenNow = new HashSet<double>();
                for (int i = zones.Count - 1; i >= 0; i--)
                {
                    AbsorptionZone zone = zones[i];
                    int touchCount;
                    touches.TryGetValue(zone.Price, out touchCount);
                    if (touchCount < MinimumTouches)
                    {
                        zones.RemoveAt(i);
                        continue;
                    }
                    bool beyond = zone.IsSupport ? close < zone.Price - buffer : close > zone.Price + buffer;
                    zone.BarsBeyond = beyond ? zone.BarsBeyond + 1 : 0;
                    if (zone.BarsBeyond >= BrokenBars)
                    {
                        brokenNow.Add(zone.Price);
                        zones.RemoveAt(i);
                    }
                }

                foreach (KeyValuePair<double, int> entry in touches)
                {
                    double price = entry.Key;
                    if (entry.Value < MinimumTouches)
                        continue;
                    zoneVolume[price] = traded[price];
                    if (brokenNow.Contains(price) || zones.Exists(z => z.Price == price) || price == close)
                        continue;
                    zones.Add(new AbsorptionZone { Price = price, IsSupport = price < close, StartBar = startBar });
                }

                if (zones.Count > MaximumZones)
                {
                    zones.Sort((a, b) => zoneVolume[b.Price].CompareTo(zoneVolume[a.Price]));
                    zones.RemoveRange(MaximumZones, zones.Count - MaximumZones);
                }
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || IsInHitTest)
                return;

            SharpDX.Direct2D1.Brush supportFill = SupportColor.ToDxBrush(RenderTarget, FillOpacity);
            SharpDX.Direct2D1.Brush resistanceFill = ResistanceColor.ToDxBrush(RenderTarget, FillOpacity);
            SharpDX.Direct2D1.Brush supportTag = SupportColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush resistanceTag = ResistanceColor.ToDxBrush(RenderTarget);
            SharpDX.Direct2D1.Brush textBrush = TextColor.ToDxBrush(RenderTarget);
            SharpDX.DirectWrite.TextFormat textFormat = new SimpleFont("Arial", TextSize) { Bold = true }.ToDirectWriteTextFormat();
            textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

            try
            {
                float panelRight = ChartPanel.X + ChartPanel.W;

                lock (zones)
                    foreach (AbsorptionZone zone in zones)
                    {
                        if (zone.StartBar < 0 || zone.StartBar > ChartBars.ToIndex)
                            continue;
                        float x1 = chartControl.GetXByBarIndex(ChartBars, zone.StartBar);
                        float y = chartScale.GetYByValue(zone.Price);
                        float top = y - BandHeight / 2;
                        if (x1 >= panelRight || top > ChartPanel.Y + ChartPanel.H || top + BandHeight < ChartPanel.Y)
                            continue;

                        RenderTarget.FillRectangle(new SharpDX.RectangleF(x1, top, panelRight - x1, BandHeight),
                            zone.IsSupport ? supportFill : resistanceFill);

                        SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory,
                            zoneVolume[zone.Price].ToString("0"), textFormat, 200, 40);
                        try
                        {
                            float labelX = panelRight - layout.Metrics.Width - 4;
                            float labelY = y - layout.Metrics.Height / 2;
                            RenderTarget.FillRectangle(new SharpDX.RectangleF(labelX - 3, labelY - 1, layout.Metrics.Width + 6, layout.Metrics.Height + 2),
                                zone.IsSupport ? supportTag : resistanceTag);
                            RenderTarget.DrawTextLayout(new SharpDX.Vector2(labelX, labelY), layout, textBrush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
                        }
                        finally
                        {
                            layout.Dispose();
                        }
                    }
            }
            finally
            {
                supportFill.Dispose();
                resistanceFill.Dispose();
                supportTag.Dispose();
                resistanceTag.Dispose();
                textBrush.Dispose();
                textFormat.Dispose();
            }
        }

        #region Properties

        [Range(10, 5000), NinjaScriptProperty]
        [Display(Name = "Window (1-minute bars)", Description = "1-minute bars whose aggressor volume is measured, whatever the chart's period.", Order = 1, GroupName = "1. Parameters")]
        public int WindowBars { get; set; }

        [Range(0.5, 10), NinjaScriptProperty]
        [Display(Name = "Sensitivity (std devs above mean)", Description = "How far above the window's average volume per price a bar's volume at a price must be to count as a touch.", Order = 2, GroupName = "1. Parameters")]
        public double Sensitivity { get; set; }

        [Range(1, 50), NinjaScriptProperty]
        [Display(Name = "Minimum touches to confirm zone", Description = "1-minute bars in the window that must concentrate volume at the price.", Order = 3, GroupName = "1. Parameters")]
        public int MinimumTouches { get; set; }

        [Range(1, 50), NinjaScriptProperty]
        [Display(Name = "Bars to consider zone broken", Description = "Consecutive 1-minute closes beyond the zone, past the buffer, that remove it.", Order = 4, GroupName = "1. Parameters")]
        public int BrokenBars { get; set; }

        [Range(0, 200), NinjaScriptProperty]
        [Display(Name = "Break buffer (ticks)", Description = "How far beyond the zone a close must be to count toward breaking it.", Order = 5, GroupName = "1. Parameters")]
        public int BreakBufferTicks { get; set; }

        [Range(1, 100), NinjaScriptProperty]
        [Display(Name = "Maximum simultaneous zones", Order = 6, GroupName = "1. Parameters")]
        public int MaximumZones { get; set; }

        [XmlIgnore]
        [Display(Name = "Support zone color (buy)", Order = 1, GroupName = "2. Visual")]
        public System.Windows.Media.Brush SupportColor { get; set; }

        [Browsable(false)]
        public string SupportColorSerializable
        {
            get { return Serialize.BrushToString(SupportColor); }
            set { SupportColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Resistance zone color (sell)", Order = 2, GroupName = "2. Visual")]
        public System.Windows.Media.Brush ResistanceColor { get; set; }

        [Browsable(false)]
        public string ResistanceColorSerializable
        {
            get { return Serialize.BrushToString(ResistanceColor); }
            set { ResistanceColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Text color (volume)", Order = 3, GroupName = "2. Visual")]
        public System.Windows.Media.Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        [Range(6, 40)]
        [Display(Name = "Text size", Order = 4, GroupName = "2. Visual")]
        public int TextSize { get; set; }

        #endregion
    }
}
