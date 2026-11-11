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
    /// A "Stops to BE" button in the ChartTrader sidebar that moves every
    /// working protective stop on the chart's instrument to breakeven --
    /// whatever placed the stop: an ATM, an OCO bracket, a manual order, or
    /// another tool. Button only, by design: it exists for hand-managed
    /// market-order trades, where the trader decides the moment. ChartTrading
    /// carries the automatic-trigger variant of the same move.
    /// </summary>
    public class BreakevenStops : Indicator
    {
        // Button, mounted in the ChartTrader sidebar when present, floating on
        // the chart otherwise.
        private System.Windows.Controls.Button breakevenButton;
        private System.Windows.Controls.Grid buttonPanel;
        private System.Windows.Controls.Grid chartTraderGrid;
        private System.Windows.Controls.RowDefinition buttonRow;
        private bool buttonInChartTrader;
        private bool handlersAttached;

        #region Properties

        [Range(-100, 1000)]
        [Display(Name = "Breakeven Offset (ticks)", GroupName = "1. Breakeven", Order = 1,
                 Description = "Where breakeven lands relative to the position's average price, in the profit direction: 2 locks two ticks of profit, 0 is exact breakeven.")]
        public int BreakevenOffsetTicks { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "A ChartTrader button that moves every working protective stop on the instrument to breakeven.";
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

        #region Breakeven
        /// <summary>
        /// Make the move on whatever account ChartTrader has selected right
        /// now. Deliberately reload-proof -- the stops are found on the account
        /// by side and type, never through in-memory registries a recompile
        /// would wipe. Every outcome lands in the log, so a click is never
        /// silent.
        /// </summary>
        private void OnBreakevenClicked(object sender, RoutedEventArgs e)
        {
            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            if (account == null)
            {
                Log("Breakeven Stops: no ChartTrader account selected; stops not moved.", NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }

            MoveStopsToBreakeven(account);
        }

        private void MoveStopsToBreakeven(Account account)
        {
            TriggerCustomEvent(o =>
            {
                MasterInstrument master = Instrument?.MasterInstrument;
                if (master == null)
                    return;

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
                    Log("Breakeven Stops: no open position on " + Instrument.FullName + "; stops not moved.",
                        NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                bool isLong = position.MarketPosition == MarketPosition.Long;
                double target = position.AveragePrice
                    + (isLong ? 1 : -1) * BreakevenOffsetTicks * master.TickSize;
                // Several fills average between ticks; round toward profit so the stop never locks a loss.
                double breakeven = master.RoundToTickSize(isLong
                    ? Math.Ceiling(target / master.TickSize - 1e-9) * master.TickSize
                    : Math.Floor(target / master.TickSize + 1e-9) * master.TickSize);

                // A long's stop fills at the bid, a short's at the ask; it must rest at least one tick away.
                double exitQuote = isLong ? GetCurrentBid() : GetCurrentAsk();
                bool roomForStop = exitQuote <= 0 || (isLong
                    ? breakeven <= exitQuote - master.TickSize
                    : breakeven >= exitQuote + master.TickSize);
                if (!roomForStop)
                {
                    Log($"Breakeven Stops: price has not passed breakeven {master.FormatPrice(breakeven)} by a tick yet; stops not moved.",
                        NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

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
                    Log(alreadySafer > 0
                            ? $"Breakeven Stops: all {alreadySafer} stop(s) on {Instrument.FullName} already at or beyond breakeven; nothing moved."
                            : $"Breakeven Stops: no working protective stops on {Instrument.FullName} to move.",
                        NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                account.Change(changes);
                string skippedNote = alreadySafer > 0 ? $", {alreadySafer} already safer left alone" : string.Empty;
                Log($"Breakeven Stops: moved {changes.Count} stop(s) to breakeven {master.FormatPrice(breakeven)}{skippedNote}.",
                    NinjaTrader.Cbi.LogLevel.Information);
            }, null);
        }
        #endregion
    }
}
