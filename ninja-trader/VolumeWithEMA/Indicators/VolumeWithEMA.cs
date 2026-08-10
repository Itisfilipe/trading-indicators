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
                VolumeAboveColor = Brushes.Blue;
                VolumeBelowColor = Brushes.Gray;
                EMAColor = Brushes.Goldenrod;

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
                ema = EMA(Volume, Period);
            }
        }

        protected override void OnBarUpdate()
        {
            // Get the volume value; if dealing with cryptocurrencies, convert volume appropriately.
            double volVal = Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency 
                            ? Core.Globals.ToCryptocurrencyVolume((long)Volume[0]) 
                            : Volume[0];

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
