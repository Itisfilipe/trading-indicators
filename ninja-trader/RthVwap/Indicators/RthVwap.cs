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
    /// <summary>
    /// Volume-weighted average price restricted to the regular trading hours,
    /// plotted on any chart -- including an ETH chart, where the lines hold flat
    /// outside the RTH window. Two anchors run side by side: the daily RTH session
    /// and the trading week (Sunday-anchored, so Monday's RTH open starts it).
    /// </summary>
    /// <remarks>
    /// Prices and volumes come from a coarse tick series committed one completed
    /// bar at a time; close * volume per committed bar is the standard VWAP
    /// approximation, and the granularity property bounds its error. The RTH
    /// window is compared in the instrument's exchange time zone (via the
    /// trading-hours template), so a platform configured to any local time zone
    /// still filters the same exchange hours. If the tick series cannot be added,
    /// the chart's own completed bars feed the accumulators instead -- coarser,
    /// never a crash (the same containment-and-fallback shape as MultiSeriesEMA,
    /// and the lesson of the same week of traces).
    /// </remarks>
    public class RthVwap : Indicator
    {
        private double dayPriceVolume;
        private double dayVolume;
        private double weekPriceVolume;
        private double weekVolume;
        private DateTime dayKey = DateTime.MinValue;
        private DateTime weekKey = DateTime.MinValue;

        private TimeSpan rthStart;
        private TimeSpan rthEnd;
        private TimeZoneInfo exchangeTimeZone;

        #region Properties

        [Range(0, 2359), NinjaScriptProperty]
        [Display(Name = "RTH start (HHMM)", Order = 0, GroupName = "Session",
                 Description = "Regular session open in the instrument's exchange time zone, e.g. 930.")]
        public int RthStartHHMM { get; set; }

        [Range(0, 2359), NinjaScriptProperty]
        [Display(Name = "RTH end (HHMM)", Order = 1, GroupName = "Session",
                 Description = "Regular session close in the instrument's exchange time zone, e.g. 1600. The end minute is excluded.")]
        public int RthEndHHMM { get; set; }

        [Range(1, 500), NinjaScriptProperty]
        [Display(Name = "Granularity (ticks)", Order = 2, GroupName = "Session",
                 Description = "Size of the tick bars the VWAP accumulates. Smaller is more precise and heavier to load.")]
        public int GranularityTicks { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Daily-session and weekly VWAP computed from regular trading hours only, on any chart.";
                Name = "RTH VWAP";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                BarsRequiredToPlot = 0;

                RthStartHHMM = 930;
                RthEndHHMM = 1600;
                GranularityTicks = 10;

                AddPlot(new Stroke(System.Windows.Media.Brushes.DarkOrange, 2), PlotStyle.Line, "Daily VWAP");
                AddPlot(new Stroke(System.Windows.Media.Brushes.MediumPurple, 2), PlotStyle.Line, "Weekly VWAP");
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
                        "RthVwap: could not add the tick series - " + ex.Message,
                        PrintTo.OutputTab1);
                }
            }
            else if (State == State.DataLoaded)
            {
                rthStart = new TimeSpan(RthStartHHMM / 100, RthStartHHMM % 100, 0);
                rthEnd = new TimeSpan(RthEndHHMM / 100, RthEndHHMM % 100, 0);
                exchangeTimeZone = Bars?.TradingHours?.TimeZoneInfo;

                if (BarsArray.Length < 2)
                    NinjaTrader.Code.Output.Process(
                        "RthVwap: tick series missing; accumulating from the chart's own bars instead.",
                        PrintTo.OutputTab1);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                // A completed tick bar commits exactly once, on the first tick of
                // its successor. Feeding the forming bar instead would re-count its
                // growing volume on every tick.
                if (IsFirstTickOfBar && CurrentBars[1] > 0)
                    Accumulate(Times[1][1], Closes[1][1], Volumes[1][1]);
                return;
            }
            if (BarsInProgress != 0)
                return;

            // Fallback feed when the tick series is absent: the chart's own
            // completed bars.
            if (BarsArray.Length < 2 && IsFirstTickOfBar && CurrentBar > 0)
                Accumulate(Time[1], Close[1], Volume[1]);

            if (dayVolume > 0)
                Values[0][0] = dayPriceVolume / dayVolume;
            if (weekVolume > 0)
                Values[1][0] = weekPriceVolume / weekVolume;
        }

        /// <summary>
        /// Folds one completed source bar into both anchors. Bars outside the RTH
        /// window are ignored entirely; the first RTH bar of a new exchange date
        /// resets the daily accumulators, of a new Sunday-anchored week the weekly
        /// ones. Between sessions nothing accumulates, so the plots hold the last
        /// session's final values flat across the overnight.
        /// </summary>
        private void Accumulate(DateTime time, double price, double volume)
        {
            DateTime exchangeTime = exchangeTimeZone != null
                ? TimeZoneInfo.ConvertTime(time, Core.Globals.GeneralOptions.TimeZoneInfo, exchangeTimeZone)
                : time;

            TimeSpan timeOfDay = exchangeTime.TimeOfDay;
            if (timeOfDay < rthStart || timeOfDay >= rthEnd)
                return;

            DateTime date = exchangeTime.Date;
            if (date != dayKey)
            {
                dayKey = date;
                dayPriceVolume = 0;
                dayVolume = 0;
            }

            DateTime week = date.AddDays(-(int)date.DayOfWeek);
            if (week != weekKey)
            {
                weekKey = week;
                weekPriceVolume = 0;
                weekVolume = 0;
            }

            dayPriceVolume += price * volume;
            dayVolume += volume;
            weekPriceVolume += price * volume;
            weekVolume += volume;
        }
    }
}
