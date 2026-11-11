#region Using declarations
using System;
using System.Collections.Generic;
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
    /// A Stops to BE button in the ChartTrader sidebar, styled like ChartTrading's
    /// toggle. It dims when no stop is short of breakeven.
    /// </summary>
    public class BreakevenStops : Indicator
    {
        private System.Windows.Controls.Button breakevenButton;
        private System.Windows.Controls.Grid chartTraderGrid;
        private System.Windows.Controls.RowDefinition buttonRow;
        private System.Windows.Threading.DispatcherTimer refreshTimer;
        private bool handlersAttached;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "A Stops to BE button in the ChartTrader sidebar.";
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
                    Log("Breakeven Stops: show Chart Trader on this chart, then re-add the indicator.", NinjaTrader.Cbi.LogLevel.Warning);
                    return;
                }

                breakevenButton = new System.Windows.Controls.Button
                {
                    Content = "Stops to BE",
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(2, 6, 2, 6),
                    Cursor = Cursors.Hand,
                    Foreground = Brushes.White,
                    Background = Brushes.SteelBlue,
                    BorderThickness = new Thickness(0),
                };
                breakevenButton.Click += OnBreakevenClicked;

                buttonRow = new System.Windows.Controls.RowDefinition { Height = GridLength.Auto };
                chartTraderGrid.RowDefinitions.Add(buttonRow);
                System.Windows.Controls.Grid.SetRow(breakevenButton, chartTraderGrid.RowDefinitions.Count - 1);
                System.Windows.Controls.Grid.SetColumnSpan(breakevenButton, Math.Max(1, chartTraderGrid.ColumnDefinitions.Count));
                chartTraderGrid.Children.Add(breakevenButton);

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
                if (breakevenButton != null)
                {
                    breakevenButton.Click -= OnBreakevenClicked;
                    chartTraderGrid.Children.Remove(breakevenButton);
                    if (buttonRow != null)
                        chartTraderGrid.RowDefinitions.Remove(buttonRow);
                    breakevenButton = null;
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
            RefreshButton();
        }

        private void RefreshButton()
        {
            if (breakevenButton == null)
                return;

            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            Position position = account != null ? OpenPosition(account) : null;
            double breakeven;
            bool enabled = position != null && StopsToMove(account, position, out breakeven).Count > 0;
            breakevenButton.IsEnabled = enabled;
            breakevenButton.Opacity = enabled ? 1.0 : 0.35;
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
