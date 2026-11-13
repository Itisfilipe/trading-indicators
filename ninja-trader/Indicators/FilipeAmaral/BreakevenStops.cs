#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// Stops to BE, Close half and Close all buttons in the ChartTrader sidebar.
    /// Each dims when it has nothing to act on.
    /// </summary>
    public class BreakevenStops : Indicator
    {
        private System.Windows.Controls.Button breakevenButton;
        private System.Windows.Controls.Button closeHalfButton;
        private System.Windows.Controls.Button closeAllButton;
        private System.Windows.Controls.StackPanel buttonPanel;
        private System.Windows.Controls.Grid chartTraderGrid;
        private System.Windows.Controls.RowDefinition buttonRow;
        private System.Windows.Threading.DispatcherTimer refreshTimer;
        private bool handlersAttached;
        private static readonly TimeSpan CloseHalfWindow = TimeSpan.FromSeconds(3);
        private Order closeHalfExit;
        private DateTime closeHalfEndTime;
        private DateTime exitsUnsettledSince = DateTime.MinValue;
        // One re-cut per position size: a broker refusing it would otherwise pop up every
        // tick, while a Close half filling in pieces needs a cut at each new size.
        private int closeHalfRecutAt;
        private bool verifyingCloseHalf;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Stops to BE, Close half and Close all buttons in the ChartTrader sidebar.";
                Name = "Breakeven Stops";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                BreakevenOffsetTicks = 0;
            }
            else if (State == State.Historical)
            {
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
                // Mounted the way ChartTrading mounts its toggle: one auto-height row appended to the ChartTrader grid.
                ChartTrader chartTrader = (Window.GetWindow(ChartControl) as Chart)?.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                chartTraderGrid = chartTrader?.Content as System.Windows.Controls.Grid;
                if (chartTraderGrid == null)
                {
                    Log("Breakeven Stops: show Chart Trader on this chart, then re-add the indicator.", NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                breakevenButton = new System.Windows.Controls.Button
                {
                    Content = "Stops to BE",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(8, 10, 8, 10),
                    Margin = new Thickness(2, 6, 2, 6),
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    Foreground = Brushes.White,
                    Background = Brushes.SteelBlue,
                    BorderThickness = new Thickness(0),
                };
                breakevenButton.Click += OnBreakevenClicked;
                closeHalfButton = MakeCloseButton("Close half", Brushes.DarkOrange, OnCloseHalfClicked);
                closeAllButton = MakeCloseButton("Close all", Brushes.Crimson, OnCloseAllClicked);

                buttonPanel = new System.Windows.Controls.StackPanel();
                buttonPanel.Children.Add(breakevenButton);
                buttonPanel.Children.Add(closeHalfButton);
                buttonPanel.Children.Add(closeAllButton);

                buttonRow = new System.Windows.Controls.RowDefinition { Height = GridLength.Auto };
                chartTraderGrid.RowDefinitions.Add(buttonRow);
                System.Windows.Controls.Grid.SetRow(buttonPanel, chartTraderGrid.RowDefinitions.Count - 1);
                System.Windows.Controls.Grid.SetColumnSpan(buttonPanel, Math.Max(1, chartTraderGrid.ColumnDefinitions.Count));
                chartTraderGrid.Children.Add(buttonPanel);

                refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                refreshTimer.Tick += OnRefreshTick;
                refreshTimer.Start();
                RefreshButton();

                handlersAttached = true;
            }
            catch (Exception ex)
            {
                DetachHandlers();
                Log("Breakeven Stops: failed to attach - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        private static System.Windows.Controls.Button MakeCloseButton(string text, System.Windows.Media.Brush color, RoutedEventHandler onClick)
        {
            System.Windows.Controls.Button button = new System.Windows.Controls.Button
            {
                Content = text,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(8, 10, 8, 10),
                Margin = new Thickness(2, 6, 2, 6),
                Cursor = Cursors.Hand,
                Focusable = false,
                Foreground = Brushes.White,
                Background = color,
                BorderThickness = new Thickness(0),
            };
            button.Click += onClick;
            return button;
        }

        private void DetachHandlers()
        {
            try
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Stop();
                    refreshTimer.Tick -= OnRefreshTick;
                    refreshTimer = null;
                }
                if (buttonPanel != null)
                {
                    breakevenButton.Click -= OnBreakevenClicked;
                    closeHalfButton.Click -= OnCloseHalfClicked;
                    closeAllButton.Click -= OnCloseAllClicked;
                    chartTraderGrid.Children.Remove(buttonPanel);
                    if (buttonRow != null)
                        chartTraderGrid.RowDefinitions.Remove(buttonRow);
                    breakevenButton = null;
                    closeHalfButton = null;
                    closeAllButton = null;
                    buttonPanel = null;
                    buttonRow = null;
                    chartTraderGrid = null;
                }
            }
            catch (Exception ex)
            {
                Log("Breakeven Stops: failed to detach - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
            handlersAttached = false;
        }
        #endregion

        #region Button state
        private void OnRefreshTick(object sender, EventArgs e)
        {
            if (verifyingCloseHalf)
                VerifyCloseHalf();
            RefreshButton();
        }

        private void RefreshButton()
        {
            if (breakevenButton == null)
                return;

            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            Position position = account != null ? OpenPosition(account) : null;
            double breakeven;
            SetEnabled(breakevenButton, position != null && StopsToMove(account, position, out breakeven).Count > 0);
            SetEnabled(closeHalfButton, position != null && !CloseHalfInFlight());
            SetEnabled(closeAllButton, position != null || (account != null && HasWorkingOrders(account)));
        }

        private static void SetEnabled(System.Windows.Controls.Button button, bool enabled)
        {
            button.IsEnabled = enabled;
            button.Opacity = enabled ? 1.0 : 0.35;
        }

        private Position OpenPosition(Account account)
        {
            lock (account.Positions)
            {
                foreach (Position candidate in account.Positions)
                {
                    if (candidate.Instrument != null
                        && candidate.Instrument.FullName == Instrument.FullName
                        && candidate.MarketPosition != MarketPosition.Flat)
                        return candidate;
                }
            }
            return null;
        }

        #endregion

        #region Actions
        private void OnBreakevenClicked(object sender, RoutedEventArgs e)
        {
            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            if (account != null)
                MoveStopsToBreakeven(account);
            RefreshButton();
        }

        private void OnCloseHalfClicked(object sender, RoutedEventArgs e)
        {
            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            if (account != null)
                CloseHalf(account);
            RefreshButton();
        }

        private void OnCloseAllClicked(object sender, RoutedEventArgs e)
        {
            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            if (account != null)
                CloseAll(account);
            RefreshButton();
        }

        private void CloseAll(Account account)
        {
            account.Flatten(new[] { Instrument });
            Log("Breakeven Stops: closed all on " + Instrument.FullName + ".", NinjaTrader.Cbi.LogLevel.Information);
        }

        private static bool IsWorking(Order order)
        {
            return !Order.IsTerminalState(order.OrderState);
        }

        private static bool IsSettled(Order order)
        {
            return order.OrderState == OrderState.Working
                || order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.PartFilled;
        }

        private bool HasWorkingOrders(Account account)
        {
            lock (account.Orders)
            {
                foreach (Order order in account.Orders)
                {
                    if (order.Instrument != null && order.Instrument.FullName == Instrument.FullName && IsWorking(order))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Until the last Close half exit has ended, a second click would halve a
        /// size it is still closing and could close past flat.
        /// </summary>
        private bool CloseHalfInFlight()
        {
            return closeHalfExit != null && !Order.IsTerminalState(closeHalfExit.OrderState);
        }

        private static int OpenQuantity(Order order)
        {
            return order.Quantity - order.Filled;
        }

        /// <summary>
        /// Working orders on the instrument's exit side of the position, other
        /// than entries.
        /// </summary>
        private List<Order> ExitOrders(Account account, Position position)
        {
            bool isLong = position.MarketPosition == MarketPosition.Long;
            List<Order> exits = new List<Order>();
            lock (account.Orders)
            {
                foreach (Order order in account.Orders)
                {
                    if (order.Instrument == null || order.Instrument.FullName != Instrument.FullName)
                        continue;
                    if (!IsWorking(order))
                        continue;
                    if (order.Name == "Entry" || order.Name == "CT Entry")
                        continue;
                    bool isSell = order.OrderAction == OrderAction.Sell || order.OrderAction == OrderAction.SellShort;
                    if (isSell != isLong)
                        continue;
                    exits.Add(order);
                }
            }
            return exits;
        }

        /// <summary>
        /// Exits half the position at market (rounded down, so the larger half
        /// keeps running); a single contract is closed outright. Stops and targets
        /// are cut down to the remaining size first, or a full-size stop or target
        /// could fill past flat and reverse.
        /// </summary>
        private void CloseHalf(Account account)
        {
            Position position = OpenPosition(account);
            if (position == null)
            {
                Log("Breakeven Stops: no position on " + Instrument.FullName + " to halve.", NinjaTrader.Cbi.LogLevel.Information);
                return;
            }
            if (CloseHalfInFlight())
                return;
            if (position.Quantity == 1)
            {
                CloseAll(account);
                return;
            }

            bool isLong = position.MarketPosition == MarketPosition.Long;
            int closeQuantity = position.Quantity / 2;
            int remaining = position.Quantity - closeQuantity;

            List<Order> exits = ExitOrders(account, position);
            if (!exits.All(IsSettled))
            {
                if (DateTime.UtcNow - exitsUnsettledSince > TimeSpan.FromSeconds(30))
                    exitsUnsettledSince = DateTime.UtcNow;
                // A change can freeze mid-flight (a Sim risk limit leaves it ChangePending for good).
                bool stuck = DateTime.UtcNow - exitsUnsettledSince > TimeSpan.FromSeconds(5);
                Log("Breakeven Stops: a stop or target on " + Instrument.FullName + (stuck
                        ? " has been stuck mid-change for over 5 s; Close half not sent. Check the order in the Orders tab."
                        : " is still being changed; Close half not sent, click again."),
                    NinjaTrader.Cbi.LogLevel.Information);
                return;
            }
            exitsUnsettledSince = DateTime.MinValue;
            List<Order> stops = exits.Where(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit).ToList();
            List<Order> targets = exits.Where(o => o.OrderType == OrderType.Limit || o.OrderType == OrderType.MIT).ToList();
            try
            {
                Dictionary<string, int> ocoQuantities = new Dictionary<string, int>();
                TrimToQuantity(account, stops, targets, remaining, ocoQuantities);
                TrimToQuantity(account, targets, stops, remaining, ocoQuantities);

                Order exit = account.CreateOrder(Instrument, isLong ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.Market,
                    OrderEntry.Manual, TimeInForce.Day, closeQuantity, 0, 0, string.Empty, "Close half", Core.Globals.MaxDate, null);
                account.Submit(new[] { exit });
                closeHalfExit = exit;
                closeHalfEndTime = DateTime.MinValue;
                verifyingCloseHalf = true;
                closeHalfRecutAt = 0;
                Log("Breakeven Stops: closing " + closeQuantity + " of " + position.Quantity + " on " + Instrument.FullName + ".", NinjaTrader.Cbi.LogLevel.Information);
            }
            catch (Exception ex)
            {
                Log("Breakeven Stops: close half failed; stops and targets may already be reduced, check them - " + ex.Message, NinjaTrader.Cbi.LogLevel.Alert);
            }
        }

        /// <summary>
        /// While the Close half exit works and a few seconds after it ends: a
        /// resize the broker rejected, or an exit that was mid-change when the
        /// button was clicked, can leave stops larger than the position, so they
        /// are cut again. An exit that ended short of its quantity leaves the
        /// stops already reduced, which is reported.
        /// </summary>
        private void VerifyCloseHalf()
        {
            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            if (account == null)
            {
                verifyingCloseHalf = false;
                return;
            }

            Position position = OpenPosition(account);
            if (position != null)
            {
                // ChartTrading resizes its own brackets to the position; cutting them here too could cut twice.
                List<Order> exits = ExitOrders(account, position).Where(o => o.Name != "CT Stop" && o.Name != "CT Target").ToList();
                List<Order> stops = exits.Where(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit).ToList();
                List<Order> targets = exits.Where(o => o.OrderType == OrderType.Limit || o.OrderType == OrderType.MIT).ToList();
                bool oversized = stops.Sum(o => OpenQuantity(o)) > position.Quantity || targets.Sum(o => OpenQuantity(o)) > position.Quantity;
                if (oversized && position.Quantity != closeHalfRecutAt && exits.All(IsSettled))
                {
                    closeHalfRecutAt = position.Quantity;
                    try
                    {
                        Dictionary<string, int> ocoQuantities = new Dictionary<string, int>();
                        TrimToQuantity(account, stops, targets, position.Quantity, ocoQuantities);
                        TrimToQuantity(account, targets, stops, position.Quantity, ocoQuantities);
                        Log("Breakeven Stops: stops or targets on " + Instrument.FullName + " were still larger than the position; cut them to " + position.Quantity + ".", NinjaTrader.Cbi.LogLevel.Information);
                    }
                    catch (Exception ex)
                    {
                        Log("Breakeven Stops: could not cut stops and targets to the position - " + ex.Message, NinjaTrader.Cbi.LogLevel.Alert);
                    }
                }
            }

            if (CloseHalfInFlight())
                return;
            if (closeHalfEndTime == DateTime.MinValue)
            {
                closeHalfEndTime = DateTime.UtcNow;
                if (closeHalfExit.Filled < closeHalfExit.Quantity)
                    Log("Breakeven Stops: the Close half exit on " + Instrument.FullName + " ended with " + closeHalfExit.Filled + " of " + closeHalfExit.Quantity
                        + " filled; its stops and targets were already reduced, check them.", NinjaTrader.Cbi.LogLevel.Alert);
            }
            if (DateTime.UtcNow - closeHalfEndTime >= CloseHalfWindow)
                verifyingCloseHalf = false;
        }

        /// <summary>
        /// Shrinks the orders' total open quantity to keepQuantity, cutting the
        /// orders without an OCO partner first, so paired brackets stay whole. An order
        /// is first capped at the size its OCO partner was trimmed to (zero: the
        /// platform cancels it with the partner), so a stop and its target stay
        /// the same size; a target smaller than its stop would cancel the stop
        /// on fill and leave the rest of the position unprotected.
        /// </summary>
        private static bool IsCancellable(Order order)
        {
            return !Order.IsTerminalState(order.OrderState)
                && order.OrderState != OrderState.CancelPending && order.OrderState != OrderState.CancelSubmitted;
        }

        private static void TrimToQuantity(Account account, List<Order> orders, List<Order> partners, int keepQuantity, Dictionary<string, int> ocoQuantities)
        {
            HashSet<string> partnerOcos = new HashSet<string>(partners.Where(o => !string.IsNullOrEmpty(o.Oco)).Select(o => o.Oco));
            List<Order> live = new List<Order>();
            Dictionary<Order, int> newQuantities = new Dictionary<Order, int>();
            IEnumerable<Order> unpairedFirst = orders
                .OrderBy(o => partnerOcos.Contains(o.Oco))
                .ThenBy(o => o.Oco ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(o => o.Name ?? string.Empty, StringComparer.Ordinal);
            foreach (Order order in unpairedFirst)
            {
                int partnerQuantity = 0;
                bool hasTrimmedPartner = !string.IsNullOrEmpty(order.Oco) && ocoQuantities.TryGetValue(order.Oco, out partnerQuantity);
                if (hasTrimmedPartner && partnerQuantity == 0)
                    continue;
                live.Add(order);
                newQuantities[order] = hasTrimmedPartner ? Math.Min(OpenQuantity(order), partnerQuantity) : OpenQuantity(order);
            }

            int excess = live.Sum(o => newQuantities[o]) - keepQuantity;
            foreach (Order order in live)
            {
                if (excess <= 0)
                    break;
                int cut = Math.Min(excess, newQuantities[order]);
                excess -= cut;
                newQuantities[order] -= cut;
            }

            List<Order> toChange = new List<Order>();
            List<Order> toCancel = new List<Order>();
            foreach (Order order in live)
            {
                int newQuantity = newQuantities[order];
                if (newQuantity == OpenQuantity(order))
                    continue;
                if (!string.IsNullOrEmpty(order.Oco))
                    ocoQuantities[order.Oco] = newQuantity;
                if (newQuantity == 0)
                {
                    toCancel.Add(order);
                    // Cancelling one OCO leg is not trusted to cancel the other; a
                    // stop's target goes too. A target's stop never does: it may still protect.
                    bool isStop = order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit;
                    foreach (Order partner in partners)
                    {
                        if (isStop && !string.IsNullOrEmpty(order.Oco) && partner.Oco == order.Oco && IsCancellable(partner) && !toCancel.Contains(partner))
                            toCancel.Add(partner);
                    }
                }
                else
                {
                    order.QuantityChanged = newQuantity + order.Filled;
                    order.LimitPriceChanged = order.LimitPrice;
                    order.StopPriceChanged = order.StopPrice;
                    toChange.Add(order);
                }
            }
            if (toChange.Count > 0)
                account.Change(toChange);
            if (toCancel.Count > 0)
                account.Cancel(toCancel);
        }

        /// <summary>
        /// Exit-side stop orders on the instrument still short of breakeven.
        /// Same-side stops and stops named as entries are left alone.
        /// </summary>
        private List<Order> StopsToMove(Account account, Position position, out double breakeven)
        {
            MasterInstrument master = Instrument.MasterInstrument;
            bool isLong = position.MarketPosition == MarketPosition.Long;
            double target = position.AveragePrice + (isLong ? 1 : -1) * BreakevenOffsetTicks * master.TickSize;
            // Several fills average between ticks; round toward profit so the stop never locks a loss.
            breakeven = master.RoundToTickSize(isLong
                ? Math.Ceiling(target / master.TickSize - 1e-9) * master.TickSize
                : Math.Floor(target / master.TickSize + 1e-9) * master.TickSize);

            List<Order> stops = new List<Order>();
            lock (account.Orders)
            {
                foreach (Order order in account.Orders)
                {
                    if (order.Instrument == null || order.Instrument.FullName != Instrument.FullName)
                        continue;
                    if (order.OrderType != OrderType.StopMarket && order.OrderType != OrderType.StopLimit)
                        continue;
                    if (order.OrderState != OrderState.Working && order.OrderState != OrderState.Accepted)
                        continue;
                    if (order.Name == "Entry" || order.Name == "CT Entry")
                        continue;
                    bool exitSide = isLong
                        ? order.OrderAction == OrderAction.Sell
                        : order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover;
                    bool shortOfBreakeven = isLong ? order.StopPrice < breakeven : order.StopPrice > breakeven;
                    if (exitSide && shortOfBreakeven)
                        stops.Add(order);
                }
            }
            return stops;
        }

        private void MoveStopsToBreakeven(Account account)
        {
            TriggerCustomEvent(o =>
            {
                MasterInstrument master = Instrument.MasterInstrument;
                Position position = OpenPosition(account);
                if (position == null)
                {
                    Log("Breakeven Stops: no open position on " + Instrument.FullName + "; stops not moved.", NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                double breakeven;
                List<Order> stops = StopsToMove(account, position, out breakeven);
                if (stops.Count == 0)
                {
                    Log("Breakeven Stops: no stop on " + Instrument.FullName + " is short of breakeven; nothing moved.", NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                // A long's stop fills at the bid, a short's at the ask; it must rest at least one tick away.
                bool isLong = position.MarketPosition == MarketPosition.Long;
                double exitQuote = isLong ? GetCurrentBid() : GetCurrentAsk();
                bool roomForStop = exitQuote <= 0 || (isLong
                    ? breakeven <= exitQuote - master.TickSize
                    : breakeven >= exitQuote + master.TickSize);
                if (!roomForStop)
                {
                    Log("Breakeven Stops: price has not passed breakeven " + master.FormatPrice(breakeven) + " by a tick yet; stops not moved.", NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                foreach (Order order in stops)
                {
                    // A stop-limit keeps its trigger-to-limit gap, or it could not fill once triggered.
                    order.QuantityChanged = order.Quantity;
                    order.LimitPriceChanged = order.OrderType == OrderType.StopLimit
                        ? master.RoundToTickSize(order.LimitPrice + (breakeven - order.StopPrice))
                        : order.LimitPrice;
                    order.StopPriceChanged = breakeven;
                }
                account.Change(stops);
                Log("Breakeven Stops: moved " + stops.Count + " stop(s) to breakeven " + master.FormatPrice(breakeven) + ".", NinjaTrader.Cbi.LogLevel.Information);
            }, null);
        }
        #endregion

        #region Properties
        [Range(-100, 1000)]
        [Display(Name = "Breakeven offset (ticks)", Description = "Where breakeven lands relative to the position's average price, in the profit direction: 2 locks two ticks of profit, 0 is exact breakeven.", Order = 1, GroupName = "1. Stops to BE")]
        public int BreakevenOffsetTicks { get; set; }
        #endregion
    }
}
