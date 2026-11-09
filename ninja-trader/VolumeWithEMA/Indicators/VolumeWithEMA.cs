#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// Combines a volume histogram and an EMA of volume. Volume bars are colored based on whether the volume is above or below the EMA.
    /// </summary>
    public class VolumeWithEMA : Indicator
    {
        private EMA ema;
        private Series<double> displayedVolume;

        #region Properties

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Period", Order = 0, GroupName = "1. Parameters")]
        public int Period { get; set; }

        [XmlIgnore]
        [Display(Name = "Volume Above Color", Order = 1, GroupName = "2. Colors")]
        public Brush VolumeAboveColor { get; set; }

        [Browsable(false)]
        public string VolumeAboveColorSerializable
        {
            get { return Serialize.BrushToString(VolumeAboveColor); }
            set { VolumeAboveColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Volume Below Color", Order = 2, GroupName = "2. Colors")]
        public Brush VolumeBelowColor { get; set; }

        [Browsable(false)]
        public string VolumeBelowColorSerializable
        {
            get { return Serialize.BrushToString(VolumeBelowColor); }
            set { VolumeBelowColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "EMA Color", Order = 3, GroupName = "2. Colors")]
        public Brush EMAColor { get; set; }

        [Browsable(false)]
        public string EMAColorSerializable
        {
            get { return Serialize.BrushToString(EMAColor); }
            set { EMAColor = Serialize.StringToBrush(value); }
        }

        #endregion

        private static System.Windows.Media.Brush Rgb(byte r, byte g, byte b)
        {
            System.Windows.Media.SolidColorBrush brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Volume indicator with an EMA. Volume bars are colored based on their relation to the EMA.";
                Name = "Volume with EMA";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                DrawOnPricePanel = false;
                BarsRequiredToPlot = 0;
                Period = 14;

                // Set default colors
                VolumeAboveColor = Rgb(0x5B, 0x9C, 0xF5);
                VolumeBelowColor = Rgb(0x96, 0x9B, 0xA4);
                EMAColor = Rgb(0x37, 0x41, 0x51);

                // Add two plots:
                // Plot 0: Volume histogram - using a Stroke to allow dynamic coloring.
                Stroke volumeStroke = new Stroke(VolumeAboveColor, 2);
                AddPlot(volumeStroke, PlotStyle.Bar, "Volume");
				Plots[0].AutoWidth = true;
				
                // Plot 1: EMA line.
                AddPlot(new Stroke(EMAColor), PlotStyle.Line, "EMA");
            }
            else if (State == State.DataLoaded)
            {
                // Initialize the EMA for the volume.
                displayedVolume = new Series<double>(this);
                ema = EMA(displayedVolume, Period);
            }
        }

        protected override void OnBarUpdate()
        {
            // Get the volume value; if dealing with cryptocurrencies, convert volume appropriately.
            double volVal = Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency 
                            ? Core.Globals.ToCryptocurrencyVolume((long)Volume[0]) 
                            : Volume[0];

            displayedVolume[0] = volVal;

            // Plot the volume histogram.
            Value[0] = volVal;
            // Plot the EMA of volume.
            Values[1][0] = ema[0];

            // Set the color of the volume histogram:
            // If volume is above the EMA, use the configured VolumeAboveColor; otherwise, use VolumeBelowColor.
            if (volVal > ema[0])
                PlotBrushes[0][0] = VolumeAboveColor;
            else
                PlotBrushes[0][0] = VolumeBelowColor;

            // Set the EMA line color.
            PlotBrushes[1][0] = EMAColor;
        }
    }
}
