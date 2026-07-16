#region Using declarations
using System;
using NinjaTrader;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.BarsTypes
{
    /// <summary>
    /// Custom Renko bars type that tracks and displays price wicks (extremes) for each brick.
    /// Unlike standard Renko bars which only show the brick body, this implementation preserves
    /// the actual high/low price extremes as wicks, providing additional market information.
    /// </summary>
    public class RenkoWicksBarsType : BarsType
    {
        #region Constants
        /// <summary>
        /// Minimum allowed brick size in ticks
        /// </summary>
        private const int MIN_BRICK_SIZE = 1;

        /// <summary>
        /// Maximum allowed brick size in ticks
        /// </summary>
        private const int MAX_BRICK_SIZE = 10000;

        /// <summary>
        /// Default brick size in ticks
        /// </summary>
        private const int DEFAULT_BRICK_SIZE = 20;

        /// <summary>
        /// Default number of days to load for historical data
        /// </summary>
        private const int DEFAULT_DAYS_TO_LOAD = 3;

        /// <summary>
        /// Unique id registering this bars type and its matching chart style.
        /// RenkoWickStyle declares the same value as its ChartStyleType.
        /// </summary>
        private const int TYPE_ID = 2588;
        #endregion

        #region Fields
        /// <summary>
        /// The brick size (offset) in price terms. Cached for performance.
        /// </summary>
        private double offset;

        /// <summary>
        /// Upper boundary of the current Renko brick
        /// </summary>
        private double renkoHigh;

        /// <summary>
        /// Lower boundary of the current Renko brick
        /// </summary>
        private double renkoLow;

        /// <summary>
        /// Tracks the highest high within the current brick formation (actual price extreme)
        /// </summary>
        private double currentWickHigh;

        /// <summary>
        /// Tracks the lowest low within the current brick formation (actual price extreme)
        /// </summary>
        private double currentWickLow;

        /// <summary>
        /// Object used for thread synchronization to ensure thread-safe operations
        /// </summary>
        private readonly object syncLock = new object();

        /// <summary>
        /// Cached session iterator to avoid redundant creation
        /// </summary>
        private SessionIterator cachedSessionIterator;

        /// <summary>
        /// Flag to track if offset has been calculated
        /// </summary>
        private bool offsetCalculated;
        #endregion

        #region BarsType Override Methods
        /// <summary>
        /// No default base period values to apply in this implementation.
        /// </summary>
        public override void ApplyDefaultBasePeriodValue(BarsPeriod period) { }

        /// <summary>
        /// Sets the default brick size value for the UI.
        /// </summary>
        /// <param name="period">The BarsPeriod to configure</param>
        public override void ApplyDefaultValue(BarsPeriod period)
        {
            period.Value = DEFAULT_BRICK_SIZE;
        }

        /// <summary>
        /// Returns a chart label for the given time (formatted as time string).
        /// </summary>
        /// <param name="time">The DateTime to format</param>
        /// <returns>Formatted time string for chart display</returns>
        public override string ChartLabel(DateTime time)
        {
            return time.ToString("T", Core.Globals.GeneralOptions.CurrentCulture);
        }

        /// <summary>
        /// Determines the number of days of historical data to load.
        /// </summary>
        /// <param name="period">The BarsPeriod configuration</param>
        /// <param name="tradingHours">Trading hours for the instrument</param>
        /// <param name="barsBack">Number of bars to look back</param>
        /// <returns>Number of days to load</returns>
        public override int GetInitialLookBackDays(BarsPeriod period, TradingHours tradingHours, int barsBack)
        {
            return DEFAULT_DAYS_TO_LOAD;
        }

        /// <summary>
        /// Returns the completion percentage of the current bar (not used for Renko).
        /// </summary>
        /// <param name="bars">The Bars object</param>
        /// <param name="now">Current time</param>
        /// <returns>Always returns 0 as Renko bars don't have time-based completion</returns>
        public override double GetPercentComplete(Bars bars, DateTime now)
        {
            return 0;
        }

        /// <summary>
        /// Indicates that the implementation supports removal of the last bar.
        /// </summary>
        /// <remarks>
        /// RemoveLastBar() is needed to restate a forming brick once it completes.
        /// The cost is that NinjaTrader disables Tick Replay for this bars type;
        /// see isremovelastbarsupported.md. Strategies wanting replay events cannot
        /// use this series.
        /// </remarks>
        public override bool IsRemoveLastBarSupported { get { return true; } }

        /// <summary>
        /// Main method that processes each incoming data point and builds Renko bars with wicks.
        /// This method is called for each tick/bar in the base dataset.
        /// </summary>
        /// <param name="bars">The Bars object being built</param>
        /// <param name="open">Open price of the data point</param>
        /// <param name="high">High price of the data point</param>
        /// <param name="low">Low price of the data point</param>
        /// <param name="close">Close price of the data point</param>
        /// <param name="time">Time of the data point</param>
        /// <param name="volume">Volume of the data point</param>
        /// <param name="isBar">Indicates if this is a completed bar</param>
        /// <param name="bid">Bid price (if available)</param>
        /// <param name="ask">Ask price (if available)</param>
        protected override void OnDataPoint(Bars bars, double open, double high, double low, double close, DateTime time, long volume, bool isBar, double bid, double ask)
        {
            // Thread synchronization to ensure thread-safe operations
            lock (syncLock)
            {
                try
                {
                    // Initialize session iterator if needed (cached for performance)
                    if (cachedSessionIterator == null)
                    {
                        cachedSessionIterator = new SessionIterator(bars);
                    }

                    // Calculate and cache the brick offset once
                    if (!offsetCalculated && bars.Instrument != null && bars.Instrument.MasterInstrument != null)
                    {
                        // Validate brick size
                        double brickSize = bars.BarsPeriod.Value;
                        if (brickSize < MIN_BRICK_SIZE || brickSize > MAX_BRICK_SIZE)
                        {
                            brickSize = DEFAULT_BRICK_SIZE;
                        }

                        offset = brickSize * bars.Instrument.MasterInstrument.TickSize;
                        offsetCalculated = true;
                    }

                    // Check if a new trading session is starting
                    bool newSession = cachedSessionIterator.IsNewSession(time, isBar);
                    if (newSession)
                    {
                        cachedSessionIterator.GetNextSession(time, isBar);
                    }

                    // Handle session initialization or first bar scenario
                    if (bars.Count == 0 || (bars.IsResetOnNewTradingDay && newSession))
                    {
                        HandleFirstBar(bars, close, time, volume);
                        return;
                    }

                    // Initialize wick tracking if it hasn't been set yet
                    if (currentWickHigh.ApproxCompare(0.0) == 0 && currentWickLow.ApproxCompare(0.0) == 0)
                    {
                        currentWickHigh = high;
                        currentWickLow = low;
                    }
                    else
                    {
                        // Update the wick extremes based on the current data point
                        currentWickHigh = Math.Max(currentWickHigh, high);
                        currentWickLow = Math.Min(currentWickLow, low);
                    }

                    // Only the open is needed on every tick. The rest of the last bar is
                    // read inside the branches that close a brick, which most ticks skip.
                    int lastIndex = bars.Count - 1;
                    double barOpen = bars.GetOpen(lastIndex);

                    // Initialize Renko boundaries if not yet set
                    if (renkoHigh.ApproxCompare(0.0) == 0 || renkoLow.ApproxCompare(0.0) == 0)
                    {
                        InitializeRenkoBoundaries(bars);
                    }

                    // Process price movement
                    // Check if the HIGH reached the upper threshold OR close breached it
                    if (high.ApproxCompare(renkoHigh) >= 0)
                    {
                        ProcessUpwardMovement(bars, close, time, volume, barOpen,
                            bars.GetHigh(lastIndex), bars.GetLow(lastIndex),
                            bars.GetTime(lastIndex), bars.GetVolume(lastIndex));
                    }
                    // Check if the LOW reached the lower threshold OR close breached it
                    else if (low.ApproxCompare(renkoLow) <= 0)
                    {
                        ProcessDownwardMovement(bars, close, time, volume, barOpen,
                            bars.GetHigh(lastIndex), bars.GetLow(lastIndex),
                            bars.GetTime(lastIndex), bars.GetVolume(lastIndex));
                    }
                    else
                    {
                        // No brick closed, so the price stayed inside the boundaries and the
                        // accumulated extremes cannot reach them. Record them as they are.
                        UpdateBar(bars,
                                  Math.Max(barOpen, currentWickHigh),
                                  Math.Min(barOpen, currentWickLow),
                                  close, time, volume);
                    }

                    // Update the last price of the bars
                    bars.LastPrice = close;
                }
                catch (Exception ex)
                {
                    // Rethrown after logging: a fault here can land between RemoveLastBar
                    // and its replacement AddBar, and continuing from a half-applied bar
                    // mutation would corrupt the series silently.
                    NinjaTrader.Code.Output.Process($"RenkoWicksBarsType.OnDataPoint error: {ex}", PrintTo.OutputTab1);
                    throw;
                }
            }
        }

        /// <summary>
        /// Handles initialization and configuration states.
        /// </summary>
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                // Set initial default properties for the chart
                Name = "Renko with Wicks";
                BarsPeriod = new BarsPeriod
                {
                    BarsPeriodType = (BarsPeriodType)TYPE_ID,
                    BarsPeriodTypeName = $"RenkoWicksBarsType({TYPE_ID})",
                    MarketDataType = MarketDataType.Last
                };
                BuiltFrom = BarsPeriodType.Tick;
                DaysToLoad = DEFAULT_DAYS_TO_LOAD;
                DefaultChartStyle = (Gui.Chart.ChartStyleType)TYPE_ID;
                IsIntraday = true;
                IsTimeBased = false;
            }
            else if (State == State.Configure)
            {
                // Update the name to include the brick size
                Name = string.Format(Core.Globals.GeneralOptions.CurrentCulture, "Renko Wicks {0}", BarsPeriod.Value);

                // Remove properties that are not applicable for this Renko implementation
                Properties.Remove(Properties.Find("BaseBarsPeriodType", true));
                Properties.Remove(Properties.Find("BaseBarsPeriodValue", true));
                Properties.Remove(Properties.Find("PointAndFigurePriceType", true));
                Properties.Remove(Properties.Find("ReversalType", true));
                Properties.Remove(Properties.Find("Value2", true));

                // Rename the "Value" property to "Brick Size" for clarity
                SetPropertyName("Value", "Brick Size");
            }
            else if (State == State.Terminated)
            {
                // Clean up resources
                cachedSessionIterator = null;
                offsetCalculated = false;
            }
        }
        #endregion

        #region Private Helper Methods
        /// <summary>
        /// Starts a fresh brick series: the very first bar, or the first bar of a new
        /// session when Break EOD is enabled.
        /// </summary>
        /// <remarks>
        /// The preceding session's final bar is left exactly as it was built. Rewriting
        /// it to a doji, as this once did, threw away that bar's real body and extremes.
        /// The session iterator is advanced by the caller only.
        /// </remarks>
        private void HandleFirstBar(Bars bars, double close, DateTime time, long volume)
        {
            // Initialize Renko boundaries around the current price
            renkoHigh = close + offset;
            renkoLow = close - offset;

            // Set wick extremes to the current close price
            currentWickHigh = close;
            currentWickLow = close;

            // Add the initial bar with all price values equal to the current close
            AddBar(bars, close, close, close, close, time, volume);
            bars.LastPrice = close;
        }

        /// <summary>
        /// Initialize Renko boundaries based on previous bars
        /// </summary>
        private void InitializeRenkoBoundaries(Bars bars)
        {
            if (bars.Count == 1)
            {
                double barOpen = bars.GetOpen(0);
                renkoHigh = barOpen + offset;
                renkoLow = barOpen - offset;
            }
            else if (bars.Count >= 2)
            {
                double previousClose = bars.GetClose(bars.Count - 2);
                double previousOpen = bars.GetOpen(bars.Count - 2);

                if (previousClose > previousOpen)
                {
                    // Previous bar was bullish
                    renkoHigh = previousClose + offset;
                    renkoLow = previousClose - 2.0 * offset;
                }
                else
                {
                    // Previous bar was bearish
                    renkoHigh = previousClose + 2.0 * offset;
                    renkoLow = previousClose - offset;
                }
            }
        }

        /// <summary>
        /// Process upward price movement and create bullish bricks
        /// </summary>
        private void ProcessUpwardMovement(Bars bars, double close, DateTime time, long volume,
            double barOpen, double barHigh, double barLow, DateTime barTime, long barVolume)
        {
            // Calculate the brick's open level for upward movement
            double brickOpenUp = renkoHigh - offset;

            // The brick closes at renkoHigh, and price cannot have traded above that
            // without completing it, so renkoHigh is the high. Any overshoot beyond it
            // belongs to the gap bricks emitted below, not to this one.
            //
            // The low is the real dip that occurred while the brick was forming. It is
            // recorded as traded, never clamped: the wicks are the whole point of this
            // bars type, and clamping silently understates anything reading Low
            // downstream, such as ATR or a stop.
            double completedHigh = Math.Max(brickOpenUp, renkoHigh);
            double completedLow = Math.Min(brickOpenUp, currentWickLow);

            // Update the current bar if it doesn't match expected values
            if (barOpen.ApproxCompare(brickOpenUp) != 0 ||
                barHigh.ApproxCompare(completedHigh) != 0 ||
                barLow.ApproxCompare(completedLow) != 0)
            {
                RemoveLastBar(bars);
                AddBar(bars, brickOpenUp, completedHigh, completedLow,
                       renkoHigh, barTime, barVolume);
            }

            // Update Renko boundaries for the next brick
            renkoLow = renkoHigh - 2.0 * offset;
            renkoHigh = renkoHigh + offset;

            // Fill in any "empty" bricks if the price HIGH moves several brick sizes at once
            // Use the actual high value that was reached, not the close
            while (currentWickHigh.ApproxCompare(renkoHigh) >= 0)
            {
                double brickOpenEmpty = renkoHigh - offset;
                AddBar(bars, brickOpenEmpty,
                       Math.Max(brickOpenEmpty, renkoHigh),
                       Math.Min(brickOpenEmpty, renkoHigh),
                       renkoHigh, time, 0);

                // Update boundaries for the next potential brick
                renkoLow = renkoHigh - 2.0 * offset;
                renkoHigh = renkoHigh + offset;
            }

            // Reset wick tracking for the new brick with actual price
            currentWickHigh = close;
            currentWickLow = close;

            double newBrickOpenUp = renkoHigh - offset;
            // Add the new brick with the latest price data
            AddBar(bars, newBrickOpenUp,
                   Math.Max(newBrickOpenUp, currentWickHigh),
                   Math.Min(newBrickOpenUp, currentWickLow),
                   close, time, volume);
        }

        /// <summary>
        /// Process downward price movement and create bearish bricks
        /// </summary>
        private void ProcessDownwardMovement(Bars bars, double close, DateTime time, long volume,
            double barOpen, double barHigh, double barLow, DateTime barTime, long barVolume)
        {
            // Calculate the brick's open level for downward movement
            double brickOpenDown = renkoLow + offset;

            // Mirror of ProcessUpwardMovement: the brick closes at renkoLow, so that is
            // the low, and the real rally that happened while it formed is the high.
            double completedHigh = Math.Max(brickOpenDown, currentWickHigh);
            double completedLow = Math.Min(brickOpenDown, renkoLow);

            // Update the current bar if it doesn't match expected values
            if (barOpen.ApproxCompare(brickOpenDown) != 0 ||
                barHigh.ApproxCompare(completedHigh) != 0 ||
                barLow.ApproxCompare(completedLow) != 0)
            {
                RemoveLastBar(bars);
                AddBar(bars, brickOpenDown, completedHigh, completedLow,
                       renkoLow, barTime, barVolume);
            }

            // Update Renko boundaries for the next brick
            renkoHigh = renkoLow + 2.0 * offset;
            renkoLow = renkoLow - offset;

            // Fill in any empty bricks if the price LOW move spans multiple brick sizes
            // Use the actual low value that was reached, not the close
            while (currentWickLow.ApproxCompare(renkoLow) <= 0)
            {
                double brickOpenEmptyDown = renkoLow + offset;
                AddBar(bars, brickOpenEmptyDown,
                       Math.Max(brickOpenEmptyDown, renkoLow),
                       Math.Min(brickOpenEmptyDown, renkoLow),
                       renkoLow, time, 0);

                // Update boundaries for subsequent bricks
                renkoHigh = renkoLow + 2.0 * offset;
                renkoLow = renkoLow - offset;
            }

            // Reset wick tracking for the new brick with actual price
            currentWickHigh = close;
            currentWickLow = close;

            double newBrickOpenDown = renkoLow + offset;
            // Add the new brick with updated wick information
            AddBar(bars, newBrickOpenDown,
                   Math.Max(newBrickOpenDown, currentWickHigh),
                   Math.Min(newBrickOpenDown, currentWickLow),
                   close, time, volume);
        }
        #endregion
    }
}
