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
    /// Plots an EMA computed on a source series of its own bar type/size, independent
    /// of the chart it is on. Time/tick/range sources ride a real secondary series;
    /// the Renko source is synthesized internally from the primary's price stream and
    /// never adds a series at all.
    /// </summary>
    /// <remarks>
    /// The internal Renko path exists because a secondary Renko series repeatedly
    /// wrecked whole charts: whether registered via AddRenko or AddDataSeries, the
    /// platform could fail to bind it (Configure replays on workspace restore and
    /// reconnect, AddDataSeries rejects stock Renko outright), and an indicator left
    /// without bound data crashes NinjaTrader's own ChartPanel.SnapToPrice with
    /// NullReferenceExceptions on every drawing-tool interaction -- a failure that
    /// happens inside the platform's loader, beyond any try/catch in this class. A
    /// close-keyed brick engine fed from the primary has nothing to load and nothing
    /// to fail. Its brick logic is the property-tested completion state machine from
    /// RenkoWicksBarsType (stock Renko parity), reduced to closes.
    /// </remarks>
    [TypeConverter("NinjaTrader.NinjaScript.Indicators.FilipeAmaral.MultiSeriesEMATypeConverter")]
    public class MultiSeriesEMA : Indicator
    {
        // EMA state, fed exactly one value per completed source brick or bar.
        // Matches the shipped @EMA recursion: seed = first value, k = 2/(period+1).
        private double emaAlpha;
        private double emaValue;
        private int emaFedCount;

        // Internal close-keyed Renko engine (Source Type = Renko only). Seeded one
        // brick either side of the first price; a close at a boundary completes
        // bricks (several for a jump), continuation one brick away, reversal two --
        // stock Renko semantics. The grid runs continuously across sessions.
        private double brickSize;
        private double renkoHigh;
        private double renkoLow;
        private bool renkoSeeded;

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
                // PeriodValue already holds the user's saved value by this point
                // (State.SetDefaults ran, then saved values were restored), so it is
                // safe to use despite AddDataSeries's "hardcoded arguments" warning,
                // which targets values computed at runtime (Instrument, Bars), not
                // configured properties. No overload names an instrument: the series
                // always follows the primary's.
                //
                // Renko adds nothing here on purpose -- see the class remarks.
                //
                // The adds are contained because a Configure that throws leaves the
                // indicator half-initialized, and the platform then throws
                // NullReferenceException out of ChartPanel.SnapToPrice on every chart
                // interaction that reads its series. Degrading to a healthy indicator
                // that plots nothing is strictly better than that.
                try
                {
                    switch (SourceType)
                    {
                        case EmaSourceBarsType.Renko:
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
                catch (Exception ex)
                {
                    // Output window rather than Log: Log routing is not guaranteed this
                    // early in the lifecycle, and this handler must never throw itself.
                    NinjaTrader.Code.Output.Process(
                        "MultiSeriesEMA: could not add the source series - " + ex.Message,
                        PrintTo.OutputTab1);
                }
            }
            else if (State == State.DataLoaded)
            {
                emaAlpha = 2.0 / (EmaPeriod + 1);
                emaValue = 0;
                emaFedCount = 0;
                renkoSeeded = false;
                brickSize = BrickSizeTicks * (Instrument?.MasterInstrument?.TickSize ?? 0);

                if (SourceType != EmaSourceBarsType.Renko && BarsArray.Length < 2)
                    NinjaTrader.Code.Output.Process(
                        "MultiSeriesEMA: no source series was added; the EMA will not plot (see the message above for the cause).",
                        PrintTo.OutputTab1);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                // A completed secondary bar commits to the EMA exactly once, on the
                // first tick of its successor. Historical bars arrive as one call
                // per bar, where IsFirstTickOfBar is always true.
                if (IsFirstTickOfBar && CurrentBars[1] > 0)
                    FeedCompleted(Closes[1][1]);
                return;
            }
            if (BarsInProgress != 0)
                return;

            // The value still forming -- the open brick's price, or the secondary
            // bar in progress -- contributes provisionally below, the way the
            // platform's own EMA restates its current bar; committed state only
            // ever advances in FeedCompleted.
            double formingValue;
            if (SourceType == EmaSourceBarsType.Renko)
            {
                // Historically this consumes the chart's bar closes (ticks arrive
                // only in real time), so a source brick finer than the chart's bars
                // seeds coarsely and converges as bricks accumulate -- an EMA
                // forgets its seed exponentially.
                FeedRenkoEngine(Close[0]);
                formingValue = Close[0];
            }
            else
            {
                if (CurrentBars.Length < 2 || CurrentBars[1] < 0)
                    return;
                formingValue = Closes[1][0];
            }

            // Secondary series warm up on their own schedule (bricks/bars form from
            // price, not in lockstep with the primary), so hold the plot until the
            // EMA has eaten a full period.
            if (emaFedCount < EmaPeriod)
                return;

            Value[0] = formingValue * emaAlpha + emaValue * (1 - emaAlpha);
            PlotBrushes[0][0] = EMAColor;
        }

        /// <summary>
        /// Advances the internal Renko grid with a close and commits every brick the
        /// move completes: at least one when a boundary is reached, several for a
        /// jump. Continuation completes one brick beyond the last close, reversal
        /// two -- the completion state machine of RenkoWicksBarsType reduced to
        /// closes (wicks and gap-brick bookkeeping do not exist off-chart).
        /// </summary>
        private void FeedRenkoEngine(double price)
        {
            if (brickSize <= 0)
                return;

            if (!renkoSeeded)
            {
                renkoHigh = price + brickSize;
                renkoLow = price - brickSize;
                renkoSeeded = true;
                return;
            }

            if (price.ApproxCompare(renkoHigh) >= 0)
            {
                while (price.ApproxCompare(renkoHigh) >= 0)
                {
                    FeedCompleted(renkoHigh);
                    renkoLow = renkoHigh - 2.0 * brickSize;
                    renkoHigh = renkoHigh + brickSize;
                }
            }
            else if (price.ApproxCompare(renkoLow) <= 0)
            {
                while (price.ApproxCompare(renkoLow) <= 0)
                {
                    FeedCompleted(renkoLow);
                    renkoHigh = renkoLow + 2.0 * brickSize;
                    renkoLow = renkoLow - brickSize;
                }
            }
        }

        private void FeedCompleted(double close)
        {
            emaValue = emaFedCount == 0
                ? close
                : close * emaAlpha + emaValue * (1 - emaAlpha);
            emaFedCount++;
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
