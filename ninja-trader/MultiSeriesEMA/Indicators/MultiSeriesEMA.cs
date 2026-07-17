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
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    public enum EmaSourceBarsType
    {
        Minute,
        Renko,
        Tick,
        Range,
        Day
    }

    /// <summary>
    /// Plots an EMA computed on a secondary bar series (its own bar type/size, independent of the
    /// chart it is on) as an overlay line on the current chart. The secondary series is data-only:
    /// it never gets its own panel and its bars are never drawn.
    /// </summary>
    [TypeConverter("NinjaTrader.NinjaScript.Indicators.FilipeAmaral.MultiSeriesEMATypeConverter")]
    public class MultiSeriesEMA : Indicator
    {
        private EMA ema;

        #region Properties

        // No [NinjaScriptProperty] here: that attribute puts the property into the
        // signatures NinjaTrader writes into its auto-generated code region, which
        // lives in the parent Indicators namespace and cannot see this enum (CS0246).
        // ChartTrading's enums follow the same rule.
        [RefreshProperties(RefreshProperties.All)]
        [Display(Name = "Source Type", Order = 0, GroupName = "Source Series", Description = "Bar type the EMA is computed on.")]
        public EmaSourceBarsType SourceType { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Period", Order = 1, GroupName = "Source Series", Description = "Bar interval for the selected source type (minutes, ticks, range ticks, or days).")]
        public int PeriodValue { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Brick Size (Ticks)", Order = 2, GroupName = "Source Series")]
        public int BrickSizeTicks { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "EMA Period", Order = 3, GroupName = "EMA")]
        public int EmaPeriod { get; set; }

        [XmlIgnore]
        [Display(Name = "EMA Color", Order = 4, GroupName = "EMA")]
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
                Description = "Overlays an EMA computed on a different bar series (type and size chosen independently of the chart) without ever drawing that series.";
                Name = "Multi-Series EMA";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                BarsRequiredToPlot = 0;

                SourceType = EmaSourceBarsType.Renko;
                PeriodValue = 15;
                BrickSizeTicks = 20;
                EmaPeriod = 20;
                EMAColor = Brushes.DodgerBlue;

                AddPlot(new Stroke(EMAColor), PlotStyle.Line, "EMA");
            }
            else if (State == State.Configure)
            {
                // PeriodValue/BrickSizeTicks already hold the user's saved property values by this
                // point (State.SetDefaults ran, then saved values were restored), so they are safe
                // to use here despite AddDataSeries/AddRenko's "hardcoded arguments" warning, which
                // targets values computed at runtime (Instrument, Bars), not configured properties.
                switch (SourceType)
                {
                    case EmaSourceBarsType.Renko:
                        AddRenko(Instrument.FullName, BrickSizeTicks, MarketDataType.Last);
                        break;
                    case EmaSourceBarsType.Tick:
                        AddDataSeries(BarsPeriodType.Tick, PeriodValue);
                        break;
                    case EmaSourceBarsType.Range:
                        AddDataSeries(BarsPeriodType.Range, PeriodValue);
                        break;
                    case EmaSourceBarsType.Day:
                        AddDataSeries(BarsPeriodType.Day, PeriodValue);
                        break;
                    default:
                        AddDataSeries(BarsPeriodType.Minute, PeriodValue);
                        break;
                }
            }
            else if (State == State.DataLoaded)
            {
                ema = EMA(BarsArray[1], EmaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            // Secondary series warms up on its own schedule (bricks/bars form from price, not in
            // lockstep with the primary series), so guard until it has enough bars for the EMA.
            if (CurrentBars[1] < EmaPeriod)
                return;

            Value[0] = ema[0];
            PlotBrushes[0][0] = EMAColor;
        }
    }

    public class MultiSeriesEMATypeConverter : IndicatorBaseConverter
    {
        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object component, Attribute[] attributes)
        {
            MultiSeriesEMA indicator = component as MultiSeriesEMA;

            PropertyDescriptorCollection properties = GetPropertiesSupported(context)
                ? base.GetProperties(context, component, attributes)
                : TypeDescriptor.GetProperties(component, attributes);

            if (indicator == null || properties == null)
                return properties;

            // Only one of Period / Brick Size applies to the selected source type; hide the other.
            string hiddenProperty = indicator.SourceType == EmaSourceBarsType.Renko ? "PeriodValue" : "BrickSizeTicks";
            PropertyDescriptor descriptorToHide = properties[hiddenProperty];
            if (descriptorToHide != null)
                properties.Remove(descriptorToHide);

            return properties;
        }

        public override bool GetPropertiesSupported(ITypeDescriptorContext context)
        {
            return true;
        }
    }
}
