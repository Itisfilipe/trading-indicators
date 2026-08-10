#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// Moves every working protective stop on the chart's instrument to
    /// breakeven -- whatever placed the stop: an ATM, an OCO bracket, a manual
    /// order, or another tool. A "Stops to BE" button in the ChartTrader
    /// sidebar makes the move on demand, and an automatic trigger makes it
    /// once per position after price has run a configured distance in the
    /// position's favor. Standalone extraction of ChartTrading's breakeven
    /// feature for traders who do not use its click-to-trade side.
    /// </summary>
    public class BreakevenStops : Indicator
    {
        // Account and position state. Account events arrive off the UI thread,
        // so everything they touch is guarded.
        private readonly object orderLock = new object();
        private Account subscribedAccount;
        private double currentAvgPrice;
        private MarketPosition currentMarketPosition = MarketPosition.Flat;
        private AutoBreakevenState autoBreakevenState = AutoBreakevenState.Armed;
        private int positionCacheGeneration;
        private bool positionSeedPending;

        // Armed: waiting for the trigger. Pending: a move is in flight.
        // WaitingForStops: fired before any stop was live; re-arms when one is
        // accepted. Fired: done for this position.
        private enum AutoBreakevenState { Armed, Pending, WaitingForStops, Fired }

        // Button, mounted in the ChartTrader sidebar when present, floating on
        // the chart otherwise.
        private System.Windows.Controls.Button breakevenButton;
        private System.Windows.Controls.Grid buttonPanel;
        private System.Windows.Controls.Grid chartTraderGrid;
        private System.Windows.Controls.RowDefinition buttonRow;
        private bool buttonInChartTrader;
        private bool handlersAttached;

        #region Properties

        [Display(Name = "Auto Breakeven", GroupName = "1. Breakeven", Order = 1,
                 Description = "Move the stops automatically once price has run the trigger distance in the position's favor, once per position. The button works either way.")]
        public bool AutoBreakevenEnabled { get; set; }

        [Range(1, 10000)]
        [Display(Name = "Auto Trigger (ticks)", GroupName = "1. Breakeven", Order = 2,
                 Description = "How many ticks in profit before the automatic move fires.")]
        public int AutoBreakevenTriggerTicks { get; set; }

        [Range(-100, 1000)]
        [Display(Name = "Breakeven Offset (ticks)", GroupName = "1. Breakeven", Order = 3,
                 Description = "Where breakeven lands relative to the position's average price, in the profit direction: 2 locks two ticks of profit, 0 is exact breakeven. Applies to the button and to the automatic move.")]
        public int BreakevenOffsetTicks { get; set; }

        [Display(Name = "Show Button", GroupName = "1. Breakeven", Order = 4,
                 Description = "The Stops to BE button in the ChartTrader sidebar (or floating on the chart when ChartTrader is hidden).")]
        public bool ShowButton { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Moves every working protective stop on the instrument to breakeven: a ChartTrader button on demand, and an optional automatic trigger once per position.";
                Name = "Breakeven Stops";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                AutoBreakevenEnabled = true;
                AutoBreakevenTriggerTicks = 30;
                BreakevenOffsetTicks = 0;
                ShowButton = true;
            }
            else if (State == State.Historical)
            {
                // First state where the chart is reliably present, and its UI
                // must be wired on its own thread (the ChartTrading lifecycle).
                ChartControl owner = ChartControl;
                if (owner != null && !handlersAttached)
                {
                    owner.Dispatcher.InvokeAsync(() =>
                    {
                        if (State < State.Terminated)
                            AttachHandlers();
                    });
                }
            }
            else if (State == State.Terminated)
            {
                // Working orders are deliberately left working; removing the
                // indicator must not touch a live bracket. Only the event
                // subscription is dropped.
                if (subscribedAccount != null)
                {
                    subscribedAccount.OrderUpdate -= OnAccountOrderUpdate;
                    subscribedAccount.PositionUpdate -= OnAccountPositionUpdate;
                    subscribedAccount = null;
                }

                ChartControl owner = ChartControl;
                if (owner != null)
                    owner.Dispatcher.InvokeAsync(DetachHandlers);
                else
                    DetachHandlers();
            }
        }

        protected override void OnBarUpdate() { }

        #region UI wiring
        private void AttachHandlers()
        {
            if (handlersAttached || ChartControl == null)
                return;

            try
            {
                if (ShowButton)
                {
                    breakevenButton = new System.Windows.Controls.Button
                    {
                        Content = "Stops to BE",
                        Padding = new Thickness(8, 3, 8, 3),
                        Cursor = Cursors.Hand,
                        Foreground = Brushes.White,
                        Background = Brushes.SteelBlue,
                        BorderThickness = new Thickness(0),
                    };
                    breakevenButton.Click += OnBreakevenClicked;

                    buttonPanel = new System.Windows.Controls.Grid();
                    buttonPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                    System.Windows.Controls.Grid.SetRow(breakevenButton, 0);
                    buttonPanel.Children.Add(breakevenButton);

                    // Mounted the way ChartTrading mounts its buttons: one
                    // auto-height row appended to the ChartTrader grid, falling
                    // back to floating on the chart when ChartTrader is hidden.
                    Window chartWindow = Window.GetWindow(ChartControl);
                    var chartTrader = (chartWindow as Chart)?.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                    chartTraderGrid = chartTrader?.Content as System.Windows.Controls.Grid;
                    if (chartTraderGrid != null)
                    {
                        buttonPanel.Margin = new Thickness(2, 6, 2, 2);
                        buttonRow = new System.Windows.Controls.RowDefinition { Height = GridLength.Auto };
                        chartTraderGrid.RowDefinitions.Add(buttonRow);
                        System.Windows.Controls.Grid.SetRow(buttonPanel, chartTraderGrid.RowDefinitions.Count - 1);
                        System.Windows.Controls.Grid.SetColumnSpan(buttonPanel,
                            Math.Max(1, chartTraderGrid.ColumnDefinitions.Count));
                        chartTraderGrid.Children.Add(buttonPanel);
                        buttonInChartTrader = true;
                    }
                    else
                    {
                        buttonPanel.Margin = new Thickness(6);
                        buttonPanel.HorizontalAlignment = HorizontalAlignment.Left;
                        buttonPanel.VerticalAlignment = VerticalAlignment.Top;
                        buttonPanel.Opacity = 0.85;
                        UserControlCollection.Add(buttonPanel);
                        buttonInChartTrader = false;
                    }
                }

                // Subscribe to the ChartTrader account up front, so the auto
                // trigger and position cache work for positions that predate
                // this instance; a button click re-subscribes if the selected
                // account changed since.
                Account attachAccount = ChartControl.OwnerChart?.ChartTrader?.Account;
                if (attachAccount != null)
                {
                    lock (orderLock)
                        EnsureAccountSubscription(attachAccount);
                    SeedPositionCache(attachAccount);
                }

                handlersAttached = true;
            }
            catch (Exception ex)
            {
                DetachHandlers();
                Log("Breakeven Stops: failed to attach - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        private void DetachHandlers()
        {
            try
            {
                if (buttonPanel != null)
                {
                    if (breakevenButton != null)
                        breakevenButton.Click -= OnBreakevenClicked;
                    if (buttonInChartTrader)
                    {
                        chartTraderGrid?.Children.Remove(buttonPanel);
                        if (buttonRow != null)
                            chartTraderGrid?.RowDefinitions.Remove(buttonRow);
                    }
                    else
                    {
                        UserControlCollection.Remove(buttonPanel);
                    }
                    breakevenButton = null;
                    buttonPanel = null;
                    buttonRow = null;
                    chartTraderGrid = null;
                    buttonInChartTrader = false;
                }
            }
            catch (Exception ex)
            {
                Log("Breakeven Stops: failed to detach - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
            handlersAttached = false;
        }
        #endregion

        #region Account subscription and position cache
        private void EnsureAccountSubscription(Account account)
        {
            if (ReferenceEquals(subscribedAccount, account))
                return;

            if (subscribedAccount != null)
            {
                subscribedAccount.OrderUpdate -= OnAccountOrderUpdate;
                subscribedAccount.PositionUpdate -= OnAccountPositionUpdate;
            }
            subscribedAccount = account;
            account.OrderUpdate += OnAccountOrderUpdate;
            account.PositionUpdate += OnAccountPositionUpdate;

            currentAvgPrice = 0;
            currentMarketPosition = MarketPosition.Flat;
            autoBreakevenState = AutoBreakevenState.Armed;
            positionCacheGeneration++;
            positionSeedPending = true;
        }

        /// <summary>
        /// Seeds the position cache from the account so a reload mid-position
        /// still arms the auto trigger. Runs after (never inside) orderLock:
        /// the positions collection needs its own lock, and nesting the two
        /// would invite lock-order inversion. A PositionUpdate that lands in
        /// between wins over this snapshot via the positionSeedPending
        /// handshake.
        /// </summary>
        private void SeedPositionCache(Account account)
        {
            double avgPrice = 0;
            MarketPosition marketPosition = MarketPosition.Flat;
            lock (account.Positions)
            {
                foreach (Position position in account.Positions)
                {
                    if (position.Instrument != null && position.Instrument.FullName == Instrument.FullName)
                    {
                        avgPrice = position.AveragePrice;
                        marketPosition = position.MarketPosition;
                        break;
                    }
                }
            }

            lock (orderLock)
            {
                if (!ReferenceEquals(subscribedAccount, account) || !positionSeedPending)
                    return;
                positionSeedPending = false;
                currentAvgPrice = avgPrice;
                currentMarketPosition = marketPosition;
                positionCacheGeneration++;
            }
        }

        // Arrives off the UI thread; keeps the auto trigger armed with the live
        // position, and re-arms it whenever the position closes or flips.
        // Account events run outside NinjaScript's exception wrapping, so
        // failures are contained and named here.
        private void OnAccountPositionUpdate(object sender, PositionEventArgs e)
        {
            try
            {
                // A queued event can outlive this instance's teardown.
                if (Instrument == null || e.Position?.Instrument == null
                    || e.Position.Instrument.FullName != Instrument.FullName)
                    return;

                // A closed position arrives as Operation.Remove with the last
                // direction still on it; MarketPosition alone would read as
                // still open.
                bool nowFlat = e.Operation == Operation.Remove || e.MarketPosition == MarketPosition.Flat;

                lock (orderLock)
                {
                    if (!ReferenceEquals(sender, subscribedAccount))
                        return;
                    positionSeedPending = false;

                    if (nowFlat || e.MarketPosition != currentMarketPosition)
                        autoBreakevenState = AutoBreakevenState.Armed;
                    currentMarketPosition = nowFlat ? MarketPosition.Flat : e.MarketPosition;
                    currentAvgPrice = nowFlat ? 0 : e.AveragePrice;
                    positionCacheGeneration++;
                }
            }
            catch (Exception ex)
            {
                Log("Breakeven Stops: position update handling failed - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        // An auto move that fired before any stop was live re-arms as soon as a
        // protective stop is accepted, instead of polling every tick.
        private void OnAccountOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                if (e.Order == null || Instrument == null)
                    return;
                if ((e.Order.OrderType == OrderType.StopMarket || e.Order.OrderType == OrderType.StopLimit)
                    && (e.OrderState == OrderState.Working || e.OrderState == OrderState.Accepted))
                {
                    lock (orderLock)
                    {
                        if (autoBreakevenState == AutoBreakevenState.WaitingForStops
                            && ReferenceEquals(sender, subscribedAccount))
                            autoBreakevenState = AutoBreakevenState.Armed;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Breakeven Stops: order update handling failed - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }
        #endregion

        #region Breakeven
        /// <summary>
        /// The button: make the move on whatever account ChartTrader has
        /// selected right now. Deliberately reload-proof -- the stops are found
        /// on the account by side and type, never through in-memory registries
        /// a recompile would wipe. Every outcome lands in the log.
        /// </summary>
        private void OnBreakevenClicked(object sender, RoutedEventArgs e)
        {
            double last = Bars != null && Bars.Count > 0 ? Bars.GetClose(Bars.Count - 1) : 0;
            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            if (account == null)
            {
                Log("Breakeven Stops: no ChartTrader account selected; stops not moved.", NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }

            // The click is the natural moment to follow an account switch made
            // in the ChartTrader dropdown since attach.
            lock (orderLock)
                EnsureAccountSubscription(account);
            SeedPositionCache(account);

            MoveStopsToBreakeven(account, last, "button");
        }

        /// <summary>
        /// Watches live ticks for the auto trigger: once price has run the
        /// configured distance in the position's favor, make the same move the
        /// button makes, once per position.
        /// </summary>
        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (!AutoBreakevenEnabled || marketDataUpdate.MarketDataType != MarketDataType.Last)
                return;

            double avgPrice;
            MarketPosition position;
            Account account;
            int generation;
            lock (orderLock)
            {
                if (autoBreakevenState != AutoBreakevenState.Armed)
                    return;
                avgPrice = currentAvgPrice;
                position = currentMarketPosition;
                account = subscribedAccount;
                generation = positionCacheGeneration;
            }
            if (account == null || position == MarketPosition.Flat)
                return;

            MasterInstrument master = Instrument?.MasterInstrument;
            if (master == null)
                return;

            // The offset has to land inside the market when the move fires, or
            // the clamp would park the stop at the last price; require at least
            // one tick of room beyond it before triggering.
            int triggerTicks = Math.Max(AutoBreakevenTriggerTicks, BreakevenOffsetTicks + 1);
            double trigger = triggerTicks * master.TickSize;
            double price = marketDataUpdate.Price;
            bool reached = position == MarketPosition.Long
                ? price >= avgPrice + trigger
                : price <= avgPrice - trigger;
            if (!reached)
                return;

            lock (orderLock)
            {
                // The cache may have moved on (position closed, flipped, or
                // scaled) between the snapshot and here; a stale tick must not
                // fire on a replacement position that never reached its own
                // trigger.
                if (autoBreakevenState != AutoBreakevenState.Armed || generation != positionCacheGeneration)
                    return;
                autoBreakevenState = AutoBreakevenState.Pending;
            }

            MoveStopsToBreakeven(account, price, "auto", generation);
        }

        private void ReArmAutoBreakeven(int expectedGeneration)
        {
            if (expectedGeneration < 0)
                return;
            lock (orderLock)
                autoBreakevenState = AutoBreakevenState.Armed;
        }

        private void MoveStopsToBreakeven(Account account, double last, string reason, int expectedGeneration = -1)
        {
            TriggerCustomEvent(o =>
            {
                // The auto path validates its snapshot is still current before
                // touching orders; the button (no generation) always acts on
                // what is live now.
                if (expectedGeneration >= 0)
                {
                    lock (orderLock)
                    {
                        if (positionCacheGeneration != expectedGeneration
                            || !ReferenceEquals(subscribedAccount, account))
                        {
                            autoBreakevenState = AutoBreakevenState.Armed;
                            return;
                        }
                    }
                }

                MasterInstrument master = Instrument?.MasterInstrument;
                if (master == null)
                {
                    ReArmAutoBreakeven(expectedGeneration);
                    return;
                }

                Position position = null;
                lock (account.Positions)
                {
                    foreach (Position candidate in account.Positions)
                    {
                        if (candidate.Instrument != null
                            && candidate.Instrument.FullName == Instrument.FullName)
                        {
                            position = candidate;
                            break;
                        }
                    }
                }
                if (position == null || position.MarketPosition == MarketPosition.Flat)
                {
                    ReArmAutoBreakeven(expectedGeneration);
                    Log("Breakeven Stops: no open position on " + Instrument.FullName + "; stops not moved.",
                        NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                // Clamped so the stop never crosses the market: a long's stop
                // stays at or below the last price, a short's at or above it,
                // one tick inside rather than at it -- a stop resting exactly
                // at the last price gets rejected or fills instantly.
                bool isLong = position.MarketPosition == MarketPosition.Long;
                double breakeven = position.AveragePrice
                    + (isLong ? 1 : -1) * BreakevenOffsetTicks * master.TickSize;
                if (last > 0)
                {
                    breakeven = isLong
                        ? Math.Min(breakeven, last - master.TickSize)
                        : Math.Max(breakeven, last + master.TickSize);
                }
                breakeven = master.RoundToTickSize(breakeven);

                // Every protective stop on the instrument moves, whatever
                // placed it -- an ATM, an OCO bracket, a manual order. Only
                // stop-type orders on the position's EXIT side qualify:
                // same-side stops are entries (a buy stop above the market on
                // a long), and yanking an entry to breakeven would fire it.
                var candidates = new List<Order>();
                lock (account.Orders)
                {
                    foreach (Order order in account.Orders)
                    {
                        if (order.Instrument == null
                            || order.Instrument.FullName != Instrument.FullName)
                            continue;
                        if (order.OrderType != OrderType.StopMarket && order.OrderType != OrderType.StopLimit)
                            continue;
                        if (order.OrderState != OrderState.Working && order.OrderState != OrderState.Accepted)
                            continue;
                        bool exitSide = isLong
                            ? order.OrderAction == OrderAction.Sell
                            : order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover;
                        if (!exitSide)
                            continue;
                        candidates.Add(order);
                    }
                }

                var changes = new List<Order>();
                int alreadySafer = 0;
                foreach (Order order in candidates)
                {
                    // Never loosen protection: a stop already at or beyond
                    // breakeven (say, manually trailed past it) stays put.
                    if (isLong ? order.StopPrice >= breakeven : order.StopPrice <= breakeven)
                    {
                        alreadySafer++;
                        continue;
                    }

                    // Stage every changeable field. A stop-limit's limit price
                    // shifts by the same distance as its stop, keeping the
                    // configured trigger-to-limit offset -- leaving the limit
                    // behind would make the order unfillable once triggered.
                    order.QuantityChanged = order.Quantity;
                    order.LimitPriceChanged = order.OrderType == OrderType.StopLimit
                        ? master.RoundToTickSize(order.LimitPrice + (breakeven - order.StopPrice))
                        : order.LimitPrice;
                    order.StopPriceChanged = breakeven;
                    changes.Add(order);
                }

                if (changes.Count == 0)
                {
                    // Stops already at or beyond BE count as done; no stops at
                    // all means the exits are not live yet -- wait for one to
                    // be accepted instead of burning the latch.
                    if (expectedGeneration >= 0)
                        lock (orderLock)
                            autoBreakevenState = alreadySafer > 0
                                ? AutoBreakevenState.Fired
                                : AutoBreakevenState.WaitingForStops;
                    Log(alreadySafer > 0
                            ? $"Breakeven Stops: all {alreadySafer} stop(s) on {Instrument.FullName} already at or beyond breakeven; nothing moved ({reason})."
                            : $"Breakeven Stops: no working protective stops on {Instrument.FullName} to move ({reason}).",
                        NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                account.Change(changes);
                if (expectedGeneration >= 0)
                    lock (orderLock)
                        autoBreakevenState = AutoBreakevenState.Fired;
                string skippedNote = alreadySafer > 0 ? $", {alreadySafer} already safer left alone" : string.Empty;
                Log($"Breakeven Stops: moved {changes.Count} stop(s) to breakeven {master.FormatPrice(breakeven)}{skippedNote} ({reason}).",
                    NinjaTrader.Cbi.LogLevel.Information);
            }, null);
        }
        #endregion
    }
}
