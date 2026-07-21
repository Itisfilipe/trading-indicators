#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    // No [NinjaScriptProperty] on the property carrying this enum: that attribute
    // puts it into the generated-code signatures, which live in the parent
    // Indicators namespace and cannot see this type (CS0246). Same rule as
    // ChartTrading's and MultiSeriesEMA's enums.
    public enum VwapAnchor
    {
        DailySession,
        Weekly
    }

    /// <summary>
    /// Volume-weighted average price with standard-deviation bands, anchored per
    /// daily session or per week, optionally restricted to a regular-hours window.
    /// Add it twice for a daily and a weekly line side by side.
    /// </summary>
    /// <remarks>
    /// Prices and volumes come from a coarse tick series committed one completed
    /// bar at a time; close * volume per committed bar is the standard VWAP
    /// approximation and the granularity property bounds its error. The RTH window
    /// is compared in the instrument's exchange time zone (via the trading-hours
    /// template), so a platform configured to any local time zone filters the same
    /// exchange hours. With the window off, the daily anchor resets at the actual
    /// session roll (a futures trading day starts the prior evening; a midnight
    /// date change must not reset it). Bands use the volume-weighted deviation
    /// sqrt(sum(v*p^2)/sum(v) - vwap^2). If the tick series cannot be added, the
    /// chart's own completed bars feed the accumulators instead -- coarser, never
    /// a crash (the containment-and-fallback shape MultiSeriesEMA settled on).
    /// </remarks>
    public class VWAP : Indicator
    {
        private double sumPriceVolume;
        private double sumVolume;
        private double sumPrice2Volume;
        private DateTime dayKey = DateTime.MinValue;
        private DateTime weekKey = DateTime.MinValue;
        private bool nextCommitStartsSession;
        private bool seeded;

        private TimeSpan rthStart;
        private TimeSpan rthEnd;
        private TimeZoneInfo exchangeTimeZone;

        #region Properties

        [Display(Name = "Anchor", Order = 0, GroupName = "Calculation",
                 Description = "Daily session resets the average every session; Weekly accumulates the whole trading week.")]
        public VwapAnchor Anchor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit to RTH window", Order = 1, GroupName = "Calculation",
                 Description = "Accumulate only trades inside the RTH window below. Off: the whole session, with the daily anchor resetting at the session roll.")]
        public bool UseRthWindow { get; set; }

        [Range(0, 2359), NinjaScriptProperty]
        [Display(Name = "RTH start (HHMM)", Order = 2, GroupName = "Calculation",
                 Description = "Regular session open in the instrument's exchange time zone, e.g. 930. Used only with the RTH window on.")]
        public int RthStartHHMM { get; set; }

        [Range(0, 2359), NinjaScriptProperty]
        [Display(Name = "RTH end (HHMM)", Order = 3, GroupName = "Calculation",
                 Description = "Regular session close in the instrument's exchange time zone, e.g. 1600. The end minute is excluded.")]
        public int RthEndHHMM { get; set; }

        [Range(1, 500), NinjaScriptProperty]
        [Display(Name = "Granularity (ticks)", Order = 4, GroupName = "Calculation",
                 Description = "Size of the tick bars the VWAP accumulates. Smaller is more precise and heavier to load.")]
        public int GranularityTicks { get; set; }

        [Range(0, 10), NinjaScriptProperty]
        [Display(Name = "Band 1 deviations", Order = 5, GroupName = "Bands",
                 Description = "Standard-deviation multiplier for the first band pair. 0 hides it.")]
        public double Band1Deviations { get; set; }

        [Range(0, 10), NinjaScriptProperty]
        [Display(Name = "Band 2 deviations", Order = 6, GroupName = "Bands",
                 Description = "Standard-deviation multiplier for the second band pair. 0 hides it.")]
        public double Band2Deviations { get; set; }

        [Range(0, 10), NinjaScriptProperty]
        [Display(Name = "Band 3 deviations", Order = 7, GroupName = "Bands",
                 Description = "Standard-deviation multiplier for the third band pair. 0 hides it.")]
        public double Band3Deviations { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "VWAP with deviation bands, anchored per daily session or per week, optionally limited to regular trading hours.";
                Name = "VWAP";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                BarsRequiredToPlot = 0;

                Anchor = VwapAnchor.DailySession;
                UseRthWindow = true;
                RthStartHHMM = 930;
                RthEndHHMM = 1600;
                GranularityTicks = 10;
                Band1Deviations = 1;
                Band2Deviations = 2;
                Band3Deviations = 0;

                AddPlot(new Stroke(System.Windows.Media.Brushes.DarkOrange, 2), PlotStyle.Line, "VWAP");
                AddPlot(new Stroke(System.Windows.Media.Brushes.DimGray, 1), PlotStyle.Line, "Upper 1");
                AddPlot(new Stroke(System.Windows.Media.Brushes.DimGray, 1), PlotStyle.Line, "Lower 1");
                AddPlot(new Stroke(System.Windows.Media.Brushes.DarkGray, 1), PlotStyle.Line, "Upper 2");
                AddPlot(new Stroke(System.Windows.Media.Brushes.DarkGray, 1), PlotStyle.Line, "Lower 2");
                AddPlot(new Stroke(System.Windows.Media.Brushes.LightGray, 1), PlotStyle.Line, "Upper 3");
                AddPlot(new Stroke(System.Windows.Media.Brushes.LightGray, 1), PlotStyle.Line, "Lower 3");
            }
            else if (State == State.Configure)
            {
                // Contained for the same reason as MultiSeriesEMA: a Configure that
                // throws leaves the indicator half-initialized, and the platform
                // then throws NullReferenceException out of ChartPanel.SnapToPrice
                // on every chart interaction that reads its series.
                try
                {
                    AddDataSeries(BarsPeriodType.Tick, GranularityTicks);
                }
                catch (Exception ex)
                {
                    NinjaTrader.Code.Output.Process(
                        "VWAP: could not add the tick series - " + ex.Message,
                        PrintTo.OutputTab1);
                }
            }
            else if (State == State.DataLoaded)
            {
                // Minutes clamp to 59: TimeSpan would silently normalize a typo like
                // 960 into 10:00, shifting the window without any signal.
                rthStart = new TimeSpan(RthStartHHMM / 100, Math.Min(RthStartHHMM % 100, 59), 0);
                rthEnd = new TimeSpan(RthEndHHMM / 100, Math.Min(RthEndHHMM % 100, 59), 0);
                exchangeTimeZone = Bars?.TradingHours?.TimeZoneInfo;

                if (BarsArray.Length < 2)
                    NinjaTrader.Code.Output.Process(
                        "VWAP: tick series missing; accumulating from the chart's own bars instead.",
                        PrintTo.OutputTab1);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                // A completed tick bar commits exactly once, on the first tick of
                // its successor; feeding the forming bar would re-count its growing
                // volume on every tick. The session flag rides one commit behind:
                // the bar committed when a session-opening bar starts forming still
                // belongs to the old session, and the opener itself resets the
                // daily anchor when IT commits.
                if (IsFirstTickOfBar)
                {
                    if (CurrentBars[1] > 0)
                    {
                        Accumulate(Times[1][1], Closes[1][1], Volumes[1][1], nextCommitStartsSession);
                        nextCommitStartsSession = false;
                    }
                    if (Bars.IsFirstBarOfSession)
                        nextCommitStartsSession = true;
                }
                return;
            }
            if (BarsInProgress != 0)
                return;

            // Fallback feed when the tick series is absent: the chart's own
            // completed bars, with the same one-commit-behind session flag.
            if (BarsArray.Length < 2 && IsFirstTickOfBar)
            {
                if (CurrentBar > 0)
                {
                    Accumulate(Time[1], Close[1], Volume[1], nextCommitStartsSession);
                    nextCommitStartsSession = false;
                }
                if (Bars.IsFirstBarOfSession)
                    nextCommitStartsSession = true;
            }

            if (sumVolume <= 0)
                return;

            double vwap = sumPriceVolume / sumVolume;
            Values[0][0] = vwap;

            double deviation = Math.Sqrt(Math.Max(0, sumPrice2Volume / sumVolume - vwap * vwap));
            SetBand(1, 2, Band1Deviations, vwap, deviation);
            SetBand(3, 4, Band2Deviations, vwap, deviation);
            SetBand(5, 6, Band3Deviations, vwap, deviation);
        }

        private void SetBand(int upperIndex, int lowerIndex, double multiplier, double vwap, double deviation)
        {
            if (multiplier <= 0)
                return;
            Values[upperIndex][0] = vwap + multiplier * deviation;
            Values[lowerIndex][0] = vwap - multiplier * deviation;
        }

        /// <summary>
        /// Folds one completed source bar into the anchor's accumulators. Bars
        /// outside an enabled RTH window are ignored entirely. The daily anchor
        /// resets on the exchange-date change while the window is on (an RTH
        /// window never crosses midnight) and on the session roll while it is off
        /// (a futures trading day starts the prior evening). The weekly anchor is
        /// Sunday-keyed either way. Between resets nothing clears, so the plots
        /// hold their last values flat through any gap.
        /// </summary>
        private void Accumulate(DateTime time, double price, double volume, bool startsNewSession)
        {
            DateTime exchangeTime = exchangeTimeZone != null
                ? TimeZoneInfo.ConvertTime(time, Core.Globals.GeneralOptions.TimeZoneInfo, exchangeTimeZone)
                : time;

            if (UseRthWindow)
            {
                TimeSpan timeOfDay = exchangeTime.TimeOfDay;
                if (timeOfDay < rthStart || timeOfDay >= rthEnd)
                    return;
            }

            DateTime date = exchangeTime.Date;
            bool reset;
            if (Anchor == VwapAnchor.Weekly)
            {
                DateTime week = date.AddDays(-(int)date.DayOfWeek);
                reset = week != weekKey;
                weekKey = week;
            }
            else if (UseRthWindow)
            {
                reset = date != dayKey;
                dayKey = date;
            }
            else
            {
                reset = startsNewSession || !seeded;
            }
            seeded = true;

            if (reset)
            {
                sumPriceVolume = 0;
                sumVolume = 0;
                sumPrice2Volume = 0;
            }

            sumPriceVolume += price * volume;
            sumVolume += volume;
            sumPrice2Volume += price * price * volume;
        }
    }
}
