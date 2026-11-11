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
    public enum TradingPanelPlacement { Fixed, AtMouse }

    public enum TradingPanelCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    public enum TradingPanelTrigger { MiddleClick, HoldSpace }

    /// <summary>
    /// Large market-order, flatten and stops-to-breakeven buttons, fixed in a
    /// chart corner or popped up under the mouse. Buttons dim when they have
    /// nothing to act on.
    /// </summary>
    public class BetterTradingPanel : Indicator
    {
        private System.Windows.Controls.Grid buttonPanel;
        private System.Windows.Controls.Button buyButton;
        private System.Windows.Controls.Button sellButton;
        private System.Windows.Controls.Button flattenButton;
        private System.Windows.Controls.Button breakevenButton;
        private System.Windows.Threading.DispatcherTimer refreshTimer;
        private Window chartWindow;
        private bool handlersAttached;
        private Order lastEntry;
        private bool lastEntryIsBuy;
        private DateTime lastEntryTime;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Large BUY MKT, SELL MKT, FLATTEN and Stops to BE buttons, fixed on the chart or under the mouse.";
                Name = "Better Trading Panel";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                Placement = TradingPanelPlacement.Fixed;
                Corner = TradingPanelCorner.BottomLeft;
                MouseTrigger = TradingPanelTrigger.MiddleClick;
                ButtonFontSize = 14;
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
                    owner.Dispatcher.InvokeAsync(() => DetachHandlers(owner));
                else
                    DetachHandlers(null);
            }
        }

        protected override void OnBarUpdate() { }

        #region UI wiring
        private System.Windows.Controls.Button MakeButton(string text, System.Windows.Media.Brush color, RoutedEventHandler onClick)
        {
            System.Windows.Controls.Button button = new System.Windows.Controls.Button
            {
                Content = text,
                FontSize = ButtonFontSize,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(ButtonFontSize, ButtonFontSize / 2.0, ButtonFontSize, ButtonFontSize / 2.0),
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                Focusable = false,
                Foreground = Brushes.White,
                Background = color,
                BorderThickness = new Thickness(0),
            };
            button.Click += onClick;
            return button;
        }

        private static void Place(System.Windows.Controls.Button button, int row, int column, int columnSpan)
        {
            System.Windows.Controls.Grid.SetRow(button, row);
            System.Windows.Controls.Grid.SetColumn(button, column);
            System.Windows.Controls.Grid.SetColumnSpan(button, columnSpan);
        }

        private void AttachHandlers()
        {
            if (handlersAttached || ChartControl == null)
                return;

            try
            {
                buyButton = MakeButton("BUY MKT", Brushes.DodgerBlue, OnBuyClicked);
                sellButton = MakeButton("SELL MKT", Brushes.Crimson, OnSellClicked);
                flattenButton = MakeButton("FLATTEN", Brushes.DarkOrange, OnFlattenClicked);
                breakevenButton = MakeButton("Stops to BE", Brushes.SeaGreen, OnBreakevenClicked);

                // A transparent background keeps the gaps between buttons inside the panel for MouseLeave.
                buttonPanel = new System.Windows.Controls.Grid { Background = Brushes.Transparent };
                buttonPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                buttonPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                for (int row = 0; row < 3; row++)
                    buttonPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                Place(buyButton, 0, 0, 1);
                Place(sellButton, 0, 1, 1);
                Place(breakevenButton, 1, 0, 2);
                Place(flattenButton, 2, 0, 2);
                buttonPanel.Children.Add(buyButton);
                buttonPanel.Children.Add(sellButton);
                buttonPanel.Children.Add(flattenButton);
                buttonPanel.Children.Add(breakevenButton);

                if (Placement == TradingPanelPlacement.Fixed)
                {
                    bool top = Corner == TradingPanelCorner.TopLeft || Corner == TradingPanelCorner.TopRight;
                    bool left = Corner == TradingPanelCorner.TopLeft || Corner == TradingPanelCorner.BottomLeft;
                    buttonPanel.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                    buttonPanel.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                    buttonPanel.Margin = new Thickness(8);
                }
                else
                {
                    buttonPanel.HorizontalAlignment = HorizontalAlignment.Left;
                    buttonPanel.VerticalAlignment = VerticalAlignment.Top;
                    buttonPanel.Visibility = Visibility.Hidden;
                    if (MouseTrigger == TradingPanelTrigger.MiddleClick)
                    {
                        ChartControl.PreviewMouseDown += OnChartMouseDown;
                        buttonPanel.MouseLeave += OnPanelMouseLeave;
                    }
                    else
                    {
                        chartWindow = Window.GetWindow(ChartControl);
                        if (chartWindow != null)
                        {
                            chartWindow.PreviewKeyDown += OnKeyDown;
                            chartWindow.PreviewKeyUp += OnKeyUp;
                            chartWindow.Deactivated += OnWindowDeactivated;
                        }
                    }
                }

                UserControlCollection.Add(buttonPanel);

                refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                refreshTimer.Tick += OnRefreshTick;
                refreshTimer.Start();
                RefreshButtons();

                handlersAttached = true;
            }
            catch (Exception ex)
            {
                DetachHandlers(ChartControl);
                Log("Better Trading Panel: failed to attach - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        private void DetachHandlers(ChartControl owner)
        {
            try
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Stop();
                    refreshTimer.Tick -= OnRefreshTick;
                    refreshTimer = null;
                }
                if (owner != null)
                    owner.PreviewMouseDown -= OnChartMouseDown;
                if (chartWindow != null)
                {
                    chartWindow.PreviewKeyDown -= OnKeyDown;
                    chartWindow.PreviewKeyUp -= OnKeyUp;
                    chartWindow.Deactivated -= OnWindowDeactivated;
                    chartWindow = null;
                }
                if (buttonPanel != null)
                {
                    buttonPanel.MouseLeave -= OnPanelMouseLeave;
                    buyButton.Click -= OnBuyClicked;
                    sellButton.Click -= OnSellClicked;
                    flattenButton.Click -= OnFlattenClicked;
                    breakevenButton.Click -= OnBreakevenClicked;
                    UserControlCollection.Remove(buttonPanel);
                    buttonPanel = null;
                }
            }
            catch (Exception ex)
            {
                Log("Better Trading Panel: failed to detach - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
            handlersAttached = false;
        }

        private void ShowAtMouse()
        {
            FrameworkElement host = buttonPanel.Parent as FrameworkElement ?? ChartControl;
            System.Windows.Point mouse = Mouse.GetPosition(host);
            double left = Math.Max(0, Math.Min(mouse.X - buttonPanel.ActualWidth / 2, host.ActualWidth - buttonPanel.ActualWidth));
            // Opens just below the cursor, or just above it near the bottom edge, so a reflex
            // click lands on the chart, never on a button.
            bool fitsBelow = mouse.Y + 12 + buttonPanel.ActualHeight <= host.ActualHeight;
            bool fitsAbove = mouse.Y - 12 - buttonPanel.ActualHeight >= 0;
            double top = fitsBelow ? mouse.Y + 12 : Math.Max(0, mouse.Y - 12 - buttonPanel.ActualHeight);
            if (!fitsBelow && !fitsAbove)
                left = mouse.X + 12 + buttonPanel.ActualWidth <= host.ActualWidth
                    ? mouse.X + 12
                    : Math.Max(0, mouse.X - 12 - buttonPanel.ActualWidth);
            buttonPanel.Margin = new Thickness(left, top, 0, 0);
            RefreshButtons();
            buttonPanel.Visibility = Visibility.Visible;
        }

        private void HideIfAtMouse()
        {
            if (buttonPanel != null && Placement == TradingPanelPlacement.AtMouse)
                buttonPanel.Visibility = Visibility.Hidden;
        }

        private void OnChartMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || buttonPanel == null)
                return;
            if (buttonPanel.Visibility == Visibility.Visible)
                HideIfAtMouse();
            else
                ShowAtMouse();
            e.Handled = true;
        }

        private void OnPanelMouseLeave(object sender, MouseEventArgs e)
        {
            HideIfAtMouse();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || buttonPanel == null || !ChartControl.IsVisible || Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
                return;
            if (!e.IsRepeat)
                ShowAtMouse();
            e.Handled = true;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || buttonPanel == null || buttonPanel.Visibility != Visibility.Visible)
                return;
            HideIfAtMouse();
            e.Handled = true;
        }

        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            HideIfAtMouse();
        }
        #endregion

        #region Button state
        private void OnRefreshTick(object sender, EventArgs e)
        {
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            if (buttonPanel == null)
                return;

            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            Position position = account != null ? OpenPosition(account) : null;
            double breakeven;
            SetEnabled(buyButton, account != null && !IsPosition(position, MarketPosition.Short) && !OppositeEntryInFlight(true));
            SetEnabled(sellButton, account != null && !IsPosition(position, MarketPosition.Long) && !OppositeEntryInFlight(false));
            SetEnabled(flattenButton, account != null && (position != null || HasWorkingOrders(account)));
            SetEnabled(breakevenButton, position != null && StopsToMove(account, position, out breakeven).Count > 0);
        }

        // Until a market entry's fill shows up in the position, an opposite click would see a flat
        // account and start a second bracket on the other side.
        private bool OppositeEntryInFlight(bool isBuy)
        {
            if (lastEntry == null || lastEntryIsBuy == isBuy)
                return false;
            if (lastEntry.OrderState == OrderState.Cancelled || lastEntry.OrderState == OrderState.Rejected)
                return false;
            return DateTime.UtcNow - lastEntryTime < TimeSpan.FromSeconds(3);
        }

        private static bool IsPosition(Position position, MarketPosition side)
        {
            return position != null && position.MarketPosition == side;
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

        private static bool IsWorking(Order order)
        {
            return order.OrderState == OrderState.Working
                || order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.Submitted
                || order.OrderState == OrderState.ChangePending
                || order.OrderState == OrderState.ChangeSubmitted;
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
        #endregion

        #region Actions
        private void OnBuyClicked(object sender, RoutedEventArgs e)
        {
            SubmitMarket(true);
            HideAfterClick();
        }

        private void OnSellClicked(object sender, RoutedEventArgs e)
        {
            SubmitMarket(false);
            HideAfterClick();
        }

        private void OnFlattenClicked(object sender, RoutedEventArgs e)
        {
            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            if (account != null)
            {
                account.Flatten(new[] { Instrument });
                Log("Better Trading Panel: flattened " + Instrument.FullName + " on " + account.Name + ".", NinjaTrader.Cbi.LogLevel.Information);
            }
            HideAfterClick();
        }

        private void OnBreakevenClicked(object sender, RoutedEventArgs e)
        {
            Account account = ChartControl?.OwnerChart?.ChartTrader?.Account;
            if (account != null)
                MoveStopsToBreakeven(account);
            HideAfterClick();
        }

        private void HideAfterClick()
        {
            if (MouseTrigger == TradingPanelTrigger.MiddleClick)
                HideIfAtMouse();
            RefreshButtons();
        }

        private void SubmitMarket(bool isBuy)
        {
            ChartTrader chartTrader = ChartControl?.OwnerChart?.ChartTrader;
            Account account = chartTrader?.Account;
            if (account == null)
            {
                Log("Better Trading Panel: no ChartTrader account selected; order not placed.", NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }

            // An opposite order would leave the open position's bracket working; FLATTEN closes instead.
            Position position = OpenPosition(account);
            if (IsPosition(position, isBuy ? MarketPosition.Short : MarketPosition.Long) || OppositeEntryInFlight(isBuy))
            {
                Log("Better Trading Panel: a position or an entry is open the other way; use FLATTEN to close it.", NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }

            try
            {
                int quantity = Math.Max(1, chartTrader.Quantity);
                string atmTemplate = chartTrader.AtmStrategy != null ? chartTrader.AtmStrategy.Template : null;
                if (chartTrader.AtmStrategy != null && string.IsNullOrEmpty(atmTemplate))
                {
                    Log("Better Trading Panel: ChartTrader shows an ATM without a template name; order not placed, so it cannot go in without a stop.", NinjaTrader.Cbi.LogLevel.Warning);
                    return;
                }
                // StartAtmStrategy only attaches the template to an entry named exactly "Entry".
                Order entry = account.CreateOrder(Instrument, isBuy ? OrderAction.Buy : OrderAction.SellShort, OrderType.Market,
                    OrderEntry.Manual, TimeInForce.Day, quantity, 0, 0, string.Empty, "Entry", Core.Globals.MaxDate, null);
                lastEntry = entry;
                lastEntryIsBuy = isBuy;
                lastEntryTime = DateTime.UtcNow;
                if (string.IsNullOrEmpty(atmTemplate))
                    account.Submit(new[] { entry });
                else
                    NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(atmTemplate, entry);
            }
            catch (Exception ex)
            {
                Log("Better Trading Panel: market order failed - " + ex.Message, NinjaTrader.Cbi.LogLevel.Error);
            }
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
                    Log("Better Trading Panel: no open position on " + Instrument.FullName + "; stops not moved.", NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                double breakeven;
                List<Order> stops = StopsToMove(account, position, out breakeven);
                if (stops.Count == 0)
                {
                    Log("Better Trading Panel: no stop on " + Instrument.FullName + " is short of breakeven; nothing moved.", NinjaTrader.Cbi.LogLevel.Information);
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
                    Log("Better Trading Panel: price has not passed breakeven " + master.FormatPrice(breakeven) + " by a tick yet; stops not moved.", NinjaTrader.Cbi.LogLevel.Information);
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
                Log("Better Trading Panel: moved " + stops.Count + " stop(s) to breakeven " + master.FormatPrice(breakeven) + ".", NinjaTrader.Cbi.LogLevel.Information);
            }, null);
        }
        #endregion

        #region Properties
        [Display(Name = "Placement", Description = "Fixed: always in a chart corner. AtMouse: pops up centered under the mouse.", Order = 1, GroupName = "1. Panel")]
        public TradingPanelPlacement Placement { get; set; }

        [Display(Name = "Corner", Description = "Where the fixed panel sits.", Order = 2, GroupName = "1. Panel")]
        public TradingPanelCorner Corner { get; set; }

        [Display(Name = "Mouse trigger", Description = "AtMouse only. MiddleClick: click the wheel to show or hide; it hides after a button is clicked or the mouse leaves it. HoldSpace: shown while Space is held.", Order = 3, GroupName = "1. Panel")]
        public TradingPanelTrigger MouseTrigger { get; set; }

        [Range(8, 40)]
        [Display(Name = "Button font size", Order = 4, GroupName = "1. Panel")]
        public int ButtonFontSize { get; set; }

        [Range(-100, 1000)]
        [Display(Name = "Breakeven offset (ticks)", Description = "Where breakeven lands relative to the position's average price, in the profit direction: 2 locks two ticks of profit, 0 is exact breakeven.", Order = 1, GroupName = "2. Stops to BE")]
        public int BreakevenOffsetTicks { get; set; }
        #endregion
    }
}
