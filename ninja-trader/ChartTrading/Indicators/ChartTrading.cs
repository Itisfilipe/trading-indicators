#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

// ChartTrading -- click-to-trade indicator for NinjaTrader 8.
//
// Live bracket preview plus optional one-click submission (off by default).
//
// Hold the buy modifier (default Shift) or the sell modifier (default Alt) and move
// the mouse over the chart: the indicator draws the bracket a click would place at
// the pointer, styled like NinjaTrader's own resting-order markers -- a chevron tag
// naming each order ("1 Buy LMT"), a line to the right edge, and a price tag at the
// edge. Up to three stop/target pairs split the quantity, each pair with its own
// stop and target. Entry LMT vs STP is inferred from the pointer being below or
// above the last traded price. This is "Option A": the indicator owns the bracket
// geometry, so what you see is exactly what a later milestone will submit.
//
// With "Enable order submission" off (the default) nothing touches an account: the
// only ChartTrader read is the order quantity, for the tag text. Switched on, a
// click while the preview is armed submits the entry to the ChartTrader account,
// and each enabled pair's stop and target -- OCO-linked per pair -- go live only
// once the entry fills, so a resting exit can never open a position. Live accounts
// additionally require "Allow live accounts"; otherwise only Sim/Playback accepts.

namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// Which keyboard modifier arms a side of the click-to-trade preview.
    /// </summary>
    public enum ChartTradingModifier
    {
        Shift,
        Alt,
        Control,
    }

    /// <summary>
    /// Where the order tags sit horizontally on the chart.
    /// </summary>
    public enum ChartTradingTagPosition
    {
        Left,
        Center,
        Right,
    }

    /// <summary>
    /// What the right-side tag shows for the stop and target levels.
    /// </summary>
    public enum ChartTradingValueDisplay
    {
        Price,
        Ticks,
        Currency,
    }

    public class ChartTrading : Indicator
    {
        #region Preview state
        private enum Side { None, Buy, Sell }

        // Written by the WPF input handlers, read by OnRender. Both run on the chart UI
        // thread, so plain fields are enough; no order or account state lives here.
        private Side previewSide = Side.None;
        private int pointerDeviceX;
        private int pointerDeviceY;
        private bool pointerOverPanel;

        private ChartPanel hookedPanel;
        private Window chartWindow;
        private bool handlersAttached;

        // Quantity shown in the tags, read from ChartTrader on mouse events (UI thread).
        private int previewQuantity = 1;

        // Toggle so the modifier keys can be handed back to other tools without
        // removing the indicator. Mounted in the ChartTrader sidebar when present,
        // floating on the chart otherwise.
        private System.Windows.Controls.Button toggleButton;
        private System.Windows.Controls.Grid chartTraderGrid;
        private System.Windows.Controls.RowDefinition toggleButtonRow;
        private bool buttonInChartTrader;
        private bool tradingEnabled = true;

        // Tag text picks black or white per level for contrast against the tag fill.
        private SharpDX.Direct2D1.Brush textWhiteBrushDx;
        private SharpDX.Direct2D1.Brush textBlackBrushDx;

        // Scale cached by OnRender so a click can map its Y to a price. Always fresh
        // at commit time: a click only commits while the preview is armed, which is
        // exactly when OnRender is running.
        private ChartScale activeChartScale;

        // Order submission state. Account events arrive off the UI thread, so the
        // commit registry is guarded; every order we touch is one we created.
        private readonly object orderLock = new object();
        private readonly List<BracketCommit> activeCommits = new List<BracketCommit>();
        private Account subscribedAccount;

        private class BracketCommit
        {
            public Order Entry;
            public bool ExitsSubmitted;
            public OrderAction ExitAction;
            public double[] StopPrices;
            public double[] TargetPrices;
            public int PairQuantity;
        }
        #endregion

        #region Parameters
        [Display(Name = "Buy modifier", Order = 1, GroupName = "Gesture",
                 Description = "Hold this key and move over the chart to preview a buy bracket.")]
        public ChartTradingModifier BuyModifier { get; set; }

        [Display(Name = "Sell modifier", Order = 2, GroupName = "Gesture",
                 Description = "Hold this key and move over the chart to preview a sell bracket.")]
        public ChartTradingModifier SellModifier { get; set; }

        // Each pair is enabled by its own checkbox. The ChartTrader quantity is the
        // size of EACH enabled pair, so the entry trades quantity x enabled pairs --
        // three 1-lot targets need a 3-lot entry. With every pair disabled, a click
        // means a plain entry of the ChartTrader quantity with no bracket.
        [Display(Name = "Bracket 1", Order = 1, GroupName = "Bracket")]
        public bool Bracket1Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 1 (ticks)", Order = 2, GroupName = "Bracket")]
        public int Stop1Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 1 (ticks)", Order = 3, GroupName = "Bracket")]
        public int Target1Ticks { get; set; }

        [Display(Name = "Bracket 2", Order = 4, GroupName = "Bracket")]
        public bool Bracket2Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 2 (ticks)", Order = 5, GroupName = "Bracket")]
        public int Stop2Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 2 (ticks)", Order = 6, GroupName = "Bracket")]
        public int Target2Ticks { get; set; }

        [Display(Name = "Bracket 3", Order = 7, GroupName = "Bracket")]
        public bool Bracket3Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 3 (ticks)", Order = 8, GroupName = "Bracket")]
        public int Stop3Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 3 (ticks)", Order = 9, GroupName = "Bracket")]
        public int Target3Ticks { get; set; }

        [Display(Name = "Tag position", Order = 1, GroupName = "Appearance",
                 Description = "Where the order tags sit: left, center, or right of the chart.")]
        public ChartTradingTagPosition TagPosition { get; set; }

        [Range(0, 2000)]
        [Display(Name = "Tag margin (pixels)", Order = 2, GroupName = "Appearance",
                 Description = "Distance kept between the tag and the chart border.")]
        public int TagMargin { get; set; }

        [Display(Name = "Level value", Order = 3, GroupName = "Appearance",
                 Description = "What the right-side tag shows on the stop and targets: the price, the " +
                               "tick distance from entry, or the money value for the current quantity. " +
                               "The entry always shows its price.")]
        public ChartTradingValueDisplay ValueDisplay { get; set; }

        [Display(Name = "Enable order submission", Order = 1, GroupName = "Trading",
                 Description = "OFF: preview only, clicks place nothing. ON: a click while the preview " +
                               "is armed submits the entry to the ChartTrader account, and each enabled " +
                               "pair's stop and target go live once the entry fills.")]
        public bool EnableOrderSubmission { get; set; }

        [Display(Name = "Allow live accounts", Order = 2, GroupName = "Trading",
                 Description = "OFF: orders are only accepted on accounts named Sim* or Playback*. " +
                               "Turn on deliberately to trade a live account.")]
        public bool AllowLiveAccounts { get; set; }

        // Strokes rather than plain brushes so each level carries its own color, width,
        // and dash style, and binds to the render target the way the platform's own
        // price-line indicator does. NinjaTrader persists Stroke properties natively.
        [Display(Name = "Entry line", Order = 1, GroupName = "Colors")]
        public Stroke EntryStroke { get; set; }

        [Display(Name = "Stop line", Order = 2, GroupName = "Colors")]
        public Stroke StopStroke { get; set; }

        [Display(Name = "Target line", Order = 3, GroupName = "Colors")]
        public Stroke TargetStroke { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Preview a click-to-trade bracket by holding a modifier key. Places no orders.";
                Name = "ChartTrading";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;

                BuyModifier = ChartTradingModifier.Shift;
                SellModifier = ChartTradingModifier.Alt;
                Bracket1Enabled = true;
                Bracket2Enabled = true;
                Bracket3Enabled = false;
                Stop1Ticks = 50;
                Target1Ticks = 50;
                Stop2Ticks = 50;
                Target2Ticks = 100;
                Stop3Ticks = 50;
                Target3Ticks = 150;

                EnableOrderSubmission = false;
                AllowLiveAccounts = false;
                TagPosition = ChartTradingTagPosition.Center;
                TagMargin = 40;
                ValueDisplay = ChartTradingValueDisplay.Price;

                // Solid and thin, matching how the platform draws a working order.
                EntryStroke = new Stroke(Brushes.DodgerBlue, DashStyleHelper.Solid, 1f);
                StopStroke = new Stroke(Brushes.Crimson, DashStyleHelper.Solid, 1f);
                TargetStroke = new Stroke(Brushes.LimeGreen, DashStyleHelper.Solid, 1f);
            }
            else if (State == State.Historical)
            {
                // First state where the chart is reliably present, and its input events
                // must be wired on its own thread. Mirrors the hardened ErgonomicCharts
                // lifecycle: capture the owner, attach through the dispatcher, and guard
                // against attaching after teardown has begun.
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
                // Working orders are deliberately left working; removing the indicator
                // must not silently cancel a live bracket. Only the event subscription
                // is dropped.
                if (subscribedAccount != null)
                {
                    subscribedAccount.OrderUpdate -= OnAccountOrderUpdate;
                    subscribedAccount = null;
                }

                ChartControl owner = ChartControl;
                if (owner != null)
                    owner.Dispatcher.InvokeAsync(DetachHandlers);
                else
                    DetachHandlers();
            }
        }

        #region Input wiring
        private void AttachHandlers()
        {
            if (handlersAttached || ChartPanel == null)
                return;

            try
            {
                hookedPanel = ChartPanel;
                hookedPanel.MouseMove += OnMouseMove;
                hookedPanel.MouseLeave += OnMouseLeave;
                hookedPanel.PreviewMouseLeftButtonDown += OnPanelClick;

                // Modifier keys register at the window, not the panel: panel key events
                // only arrive while the panel has keyboard focus, which made hold and
                // release take effect only on the next mouse move.
                chartWindow = Window.GetWindow(ChartControl);
                if (chartWindow != null)
                {
                    chartWindow.PreviewKeyDown += OnKeyChanged;
                    chartWindow.PreviewKeyUp += OnKeyChanged;
                    chartWindow.Deactivated += OnWindowDeactivated;
                }

                // The toggle prefers the ChartTrader sidebar, mounted the way the
                // ABCompleteChartTrader reference does it: one auto-height row appended
                // to the ChartTrader grid. With ChartTrader unavailable it falls back to
                // floating on the chart through UserControlCollection, the platform's
                // supported spot for custom chart controls.
                toggleButton = new System.Windows.Controls.Button
                {
                    Padding = new Thickness(8, 3, 8, 3),
                    Cursor = Cursors.Hand,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                };
                toggleButton.Click += OnToggleClicked;
                UpdateToggleVisual();

                var chartTrader = (chartWindow as Chart)?.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                chartTraderGrid = chartTrader?.Content as System.Windows.Controls.Grid;
                if (chartTraderGrid != null)
                {
                    toggleButton.Margin = new Thickness(2, 6, 2, 2);
                    toggleButtonRow = new System.Windows.Controls.RowDefinition { Height = GridLength.Auto };
                    chartTraderGrid.RowDefinitions.Add(toggleButtonRow);
                    System.Windows.Controls.Grid.SetRow(toggleButton, chartTraderGrid.RowDefinitions.Count - 1);
                    System.Windows.Controls.Grid.SetColumnSpan(toggleButton,
                        Math.Max(1, chartTraderGrid.ColumnDefinitions.Count));
                    chartTraderGrid.Children.Add(toggleButton);
                    buttonInChartTrader = true;
                }
                else
                {
                    toggleButton.Margin = new Thickness(6);
                    toggleButton.HorizontalAlignment = HorizontalAlignment.Left;
                    toggleButton.VerticalAlignment = VerticalAlignment.Top;
                    toggleButton.Opacity = 0.85;
                    UserControlCollection.Add(toggleButton);
                    buttonInChartTrader = false;
                }

                handlersAttached = true;
            }
            catch (Exception ex)
            {
                DetachHandlers();
                Log("ChartTrading: failed to attach handlers - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        private void DetachHandlers()
        {
            if (hookedPanel != null || chartWindow != null)
            {
                try
                {
                    if (hookedPanel != null)
                    {
                        hookedPanel.MouseMove -= OnMouseMove;
                        hookedPanel.MouseLeave -= OnMouseLeave;
                        hookedPanel.PreviewMouseLeftButtonDown -= OnPanelClick;
                    }
                    if (chartWindow != null)
                    {
                        chartWindow.PreviewKeyDown -= OnKeyChanged;
                        chartWindow.PreviewKeyUp -= OnKeyChanged;
                        chartWindow.Deactivated -= OnWindowDeactivated;
                    }
                    if (toggleButton != null)
                    {
                        toggleButton.Click -= OnToggleClicked;
                        if (buttonInChartTrader)
                        {
                            chartTraderGrid?.Children.Remove(toggleButton);
                            if (toggleButtonRow != null)
                                chartTraderGrid?.RowDefinitions.Remove(toggleButtonRow);
                        }
                        else
                        {
                            UserControlCollection.Remove(toggleButton);
                        }
                        toggleButton = null;
                        toggleButtonRow = null;
                        chartTraderGrid = null;
                        buttonInChartTrader = false;
                    }
                }
                catch (Exception ex)
                {
                    Log("ChartTrading: failed to detach handlers - " + ex, NinjaTrader.Cbi.LogLevel.Error);
                }
                hookedPanel = null;
                chartWindow = null;
            }

            previewSide = Side.None;
            pointerOverPanel = false;
            handlersAttached = false;
        }
        #endregion

        #region Gesture
        private ModifierKeys ToModifierKeys(ChartTradingModifier modifier)
        {
            switch (modifier)
            {
                case ChartTradingModifier.Alt: return ModifierKeys.Alt;
                case ChartTradingModifier.Control: return ModifierKeys.Control;
                default: return ModifierKeys.Shift;
            }
        }

        /// <summary>
        /// Resolve which side, if any, the currently held modifiers arm.
        /// </summary>
        /// <remarks>
        /// Exact equality, not a flag test, so an accidental Shift+Ctrl does not read as
        /// a plain Shift buy.
        /// </remarks>
        private Side ResolveSide()
        {
            if (!tradingEnabled)
                return Side.None;

            ModifierKeys held = Keyboard.Modifiers;
            if (held == ToModifierKeys(BuyModifier))
                return Side.Buy;
            if (held == ToModifierKeys(SellModifier))
                return Side.Sell;
            return Side.None;
        }

        private void OnToggleClicked(object sender, RoutedEventArgs e)
        {
            tradingEnabled = !tradingEnabled;
            UpdateToggleVisual();
            // Clears any armed preview the moment trading is switched off.
            UpdatePreview(Side.None);
        }

        private void UpdateToggleVisual()
        {
            if (toggleButton == null)
                return;

            if (!tradingEnabled)
            {
                toggleButton.Content = "ChartTrading OFF";
                toggleButton.Background = Brushes.DimGray;
            }
            else if (EnableOrderSubmission)
            {
                toggleButton.Content = "ChartTrading ON";
                toggleButton.Background = Brushes.SeaGreen;
            }
            else
            {
                toggleButton.Content = "ChartTrading PREVIEW";
                toggleButton.Background = Brushes.SteelBlue;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (ChartControl == null || ChartPanel == null)
                return;

            System.Windows.Point pos = e.GetPosition(ChartControl as IInputElement);
            pointerDeviceX = ChartingExtensions.ConvertToHorizontalPixels(pos.X, ChartControl.PresentationSource);
            pointerDeviceY = ChartingExtensions.ConvertToVerticalPixels(pos.Y, ChartControl.PresentationSource);

            pointerOverPanel =
                pointerDeviceX >= ChartPanel.X && pointerDeviceX <= ChartPanel.X + ChartPanel.W &&
                pointerDeviceY >= ChartPanel.Y && pointerDeviceY <= ChartPanel.Y + ChartPanel.H;

            // The tags show the quantity ChartTrader would trade. Mouse events run on
            // the chart UI thread, so reading the control here is thread-consistent.
            // Null-guarded rather than try/caught: a hidden ChartTrader would otherwise
            // throw and swallow an exception on every single mouse move.
            var chartTrader = ChartControl.OwnerChart?.ChartTrader;
            if (chartTrader != null && chartTrader.Quantity > 0)
                previewQuantity = chartTrader.Quantity;

            UpdatePreview(ResolveSide());
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            pointerOverPanel = false;
            UpdatePreview(Side.None);
        }

        // Catches holding or releasing the modifier without moving the mouse. Hooked at
        // the chart window, so it fires regardless of which control has keyboard focus.
        private void OnKeyChanged(object sender, KeyEventArgs e)
        {
            UpdatePreview(pointerOverPanel ? ResolveSide() : Side.None);
        }

        // A drag or alt-tab away from the window never delivers the key-up; treat
        // deactivation as release.
        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            UpdatePreview(Side.None);
        }

        private void UpdatePreview(Side side)
        {
            bool sideChanged = side != previewSide;
            previewSide = side;

            // Repaint on every pointer move while a side is armed and once on the
            // transition to None so the preview clears. ForceRefresh alone only marks
            // the chart dirty for NinjaTrader's next render pass, which on a quiet
            // chart arrives noticeably late; InvalidateVisual forces the pass now.
            if (side != Side.None || sideChanged)
            {
                ForceRefresh();
                ChartControl?.InvalidateVisual();
            }
        }
        #endregion

        #region Order submission
        private void OnPanelClick(object sender, MouseButtonEventArgs e)
        {
            if (!EnableOrderSubmission || !tradingEnabled || previewSide == Side.None || !pointerOverPanel)
                return;

            MasterInstrument master = Instrument?.MasterInstrument;
            ChartScale scale = activeChartScale;
            if (master == null || scale == null)
                return;

            var chartTrader = ChartControl.OwnerChart?.ChartTrader;
            Account account = chartTrader?.Account;
            if (account == null)
            {
                Log("ChartTrading: no ChartTrader account selected; order not placed.",
                    NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }

            bool simAccount = account.Name.StartsWith("Sim", StringComparison.OrdinalIgnoreCase)
                || account.Name.StartsWith("Playback", StringComparison.OrdinalIgnoreCase);
            if (!simAccount && !AllowLiveAccounts)
            {
                Log($"ChartTrading: {account.Name} is a live account and 'Allow live accounts' is off; order not placed.",
                    NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }

            int quantity = chartTrader.Quantity > 0 ? chartTrader.Quantity : previewQuantity;
            double entryPrice = master.RoundToTickSize(scale.GetValueByY(pointerDeviceY));
            bool isBuy = previewSide == Side.Buy;
            int profitSign = isBuy ? 1 : -1;
            double tick = master.TickSize;

            // Same inference the preview shows: favorable side of last is a limit,
            // beyond it a stop-market, exactly at it a market order.
            OrderType entryType = OrderType.Market;
            if (Bars != null && Bars.Count > 0)
            {
                double last = Bars.GetClose(Bars.Count - 1);
                if (entryPrice.ApproxCompare(last) != 0)
                {
                    bool favorable = isBuy ? entryPrice < last : entryPrice > last;
                    entryType = favorable ? OrderType.Limit : OrderType.StopMarket;
                }
            }
            double limitPrice = entryType == OrderType.Limit ? entryPrice : 0;
            double stopPrice = entryType == OrderType.StopMarket ? entryPrice : 0;

            bool[] pairEnabled = { Bracket1Enabled, Bracket2Enabled, Bracket3Enabled };
            int[] stopTicks = { Stop1Ticks, Stop2Ticks, Stop3Ticks };
            int[] targetTicks = { Target1Ticks, Target2Ticks, Target3Ticks };
            var stops = new List<double>();
            var targets = new List<double>();
            for (int i = 0; i < pairEnabled.Length; i++)
            {
                if (!pairEnabled[i])
                    continue;
                stops.Add(master.RoundToTickSize(entryPrice - profitSign * stopTicks[i] * tick));
                targets.Add(master.RoundToTickSize(entryPrice + profitSign * targetTicks[i] * tick));
            }
            int entryQuantity = stops.Count > 0 ? quantity * stops.Count : quantity;

            var commit = new BracketCommit
            {
                ExitAction = isBuy ? OrderAction.Sell : OrderAction.BuyToCover,
                StopPrices = stops.ToArray(),
                TargetPrices = targets.ToArray(),
                PairQuantity = quantity,
            };
            OrderAction entryAction = isBuy ? OrderAction.Buy : OrderAction.SellShort;

            // Creation and submission run on NinjaTrader's own thread, the shape the
            // ABCompleteChartTrader reference uses. The commit registers before Submit
            // so a fast fill cannot race past the registry.
            TriggerCustomEvent(o =>
            {
                Order entry = account.CreateOrder(Instrument, entryAction, entryType, OrderEntry.Manual,
                    TimeInForce.Day, entryQuantity, limitPrice, stopPrice, string.Empty, "CT Entry",
                    Core.Globals.MaxDate, null);

                lock (orderLock)
                {
                    commit.Entry = entry;
                    activeCommits.Add(commit);
                    EnsureAccountSubscription(account);
                }

                account.Submit(new[] { entry });
            }, null);

            // The click belongs to the commit; the chart must not also act on it.
            e.Handled = true;
        }

        private void EnsureAccountSubscription(Account account)
        {
            if (ReferenceEquals(subscribedAccount, account))
                return;

            if (subscribedAccount != null)
                subscribedAccount.OrderUpdate -= OnAccountOrderUpdate;
            subscribedAccount = account;
            account.OrderUpdate += OnAccountOrderUpdate;
        }

        // Arrives off the UI thread. Exits go live only on the entry's full fill; a
        // resting stop or target without a position would OPEN a trade, not close one.
        private void OnAccountOrderUpdate(object sender, OrderEventArgs e)
        {
            BracketCommit commit = null;
            lock (orderLock)
            {
                for (int i = 0; i < activeCommits.Count; i++)
                {
                    if (ReferenceEquals(activeCommits[i].Entry, e.Order))
                    {
                        commit = activeCommits[i];
                        break;
                    }
                }
            }
            if (commit == null)
                return;

            if (e.OrderState == OrderState.Filled)
            {
                bool submitNow = false;
                lock (orderLock)
                {
                    if (!commit.ExitsSubmitted)
                    {
                        commit.ExitsSubmitted = true;
                        submitNow = true;
                    }
                }
                if (submitNow)
                    SubmitExits(commit, sender as Account);
            }
            else if (e.OrderState == OrderState.Cancelled || e.OrderState == OrderState.Rejected)
            {
                lock (orderLock)
                    activeCommits.Remove(commit);
                if (e.Order.Filled > 0 && !commit.ExitsSubmitted)
                    Log($"ChartTrading: entry cancelled after a partial fill of {e.Order.Filled}; that position has no bracket.",
                        NinjaTrader.Cbi.LogLevel.Warning);
            }
        }

        private void SubmitExits(BracketCommit commit, Account account)
        {
            if (account == null)
                return;

            TriggerCustomEvent(o =>
            {
                var exits = new List<Order>();
                for (int i = 0; i < commit.StopPrices.Length; i++)
                {
                    // Each pair's stop and target cancel each other.
                    string oco = "CT-" + Guid.NewGuid().ToString("N");
                    exits.Add(account.CreateOrder(Instrument, commit.ExitAction, OrderType.StopMarket,
                        OrderEntry.Manual, TimeInForce.Day, commit.PairQuantity, 0, commit.StopPrices[i],
                        oco, "CT Stop", Core.Globals.MaxDate, null));
                    exits.Add(account.CreateOrder(Instrument, commit.ExitAction, OrderType.Limit,
                        OrderEntry.Manual, TimeInForce.Day, commit.PairQuantity, commit.TargetPrices[i], 0,
                        oco, "CT Target", Core.Globals.MaxDate, null));
                }
                if (exits.Count > 0)
                    account.Submit(exits);
            }, null);
        }
        #endregion

        #region Rendering
        public override void OnRenderTargetChanged()
        {
            // Strokes bind to the render target the way the platform's own price-line
            // indicator binds its ask/bid/last strokes.
            if (EntryStroke != null) EntryStroke.RenderTarget = RenderTarget;
            if (StopStroke != null) StopStroke.RenderTarget = RenderTarget;
            if (TargetStroke != null) TargetStroke.RenderTarget = RenderTarget;

            textWhiteBrushDx?.Dispose();
            textBlackBrushDx?.Dispose();
            textWhiteBrushDx = null;
            textBlackBrushDx = null;
            if (RenderTarget != null)
            {
                textWhiteBrushDx = Brushes.White.ToDxBrush(RenderTarget);
                textBlackBrushDx = Brushes.Black.ToDxBrush(RenderTarget);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            activeChartScale = chartScale;

            if (previewSide == Side.None || !pointerOverPanel || RenderTarget == null)
                return;

            MasterInstrument master = Instrument?.MasterInstrument;
            if (master == null || EntryStroke?.BrushDX == null)
                return;

            double entryPrice = master.RoundToTickSize(chartScale.GetValueByY(pointerDeviceY));
            double tick = master.TickSize;
            bool isBuy = previewSide == Side.Buy;

            // Buy: stop below entry, targets above. Sell: mirror. sign is +1 in the
            // direction the trade profits.
            int profitSign = isBuy ? 1 : -1;

            string enter = isBuy ? "Buy" : "Sell";
            string exit = isBuy ? "Sell" : "Buy";

            // The entry becomes a limit when it is on the favorable side of the last
            // traded price and a stop-market beyond it -- the label previews the order
            // type a click would actually submit.
            string entryType = "MKT";
            if (Bars != null && Bars.Count > 0)
            {
                double last = Bars.GetClose(Bars.Count - 1);
                bool favorable = isBuy ? entryPrice < last : entryPrice > last;
                if (entryPrice.ApproxCompare(last) != 0)
                    entryType = favorable ? "LMT" : "STP";
            }

            SharpDX.DirectWrite.TextFormat textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
            try
            {
                // The ChartTrader quantity sizes each enabled bracket pair, so the
                // entry trades quantity times enabled pairs and every stop and target
                // carries its pair's full size: three 1-lot targets need a 3-lot
                // entry, and the stops must cover those same 3 lots. Levels sharing a
                // price merge into one marker with the summed quantity, the way the
                // orders would sit in the book. With no pair enabled, the click means
                // a plain entry of the ChartTrader quantity.
                bool[] pairEnabled = { Bracket1Enabled, Bracket2Enabled, Bracket3Enabled };
                int[] stopTicks = { Stop1Ticks, Stop2Ticks, Stop3Ticks };
                int[] targetTicks = { Target1Ticks, Target2Ticks, Target3Ticks };
                int pairs = 0;
                foreach (bool enabled in pairEnabled)
                    if (enabled)
                        pairs++;
                int entryQuantity = pairs > 0 ? previewQuantity * pairs : previewQuantity;

                DrawOrderMarker(chartScale, entryPrice, EntryStroke,
                    $"{entryQuantity} {enter} {entryType}", master.FormatPrice(entryPrice), textFormat);

                var stopLevels = new Dictionary<double, LevelInfo>();
                var targetLevels = new Dictionary<double, LevelInfo>();

                for (int i = 0; i < pairEnabled.Length; i++)
                {
                    if (!pairEnabled[i])
                        continue;

                    AccumulateLevel(stopLevels,
                        master.RoundToTickSize(entryPrice - profitSign * stopTicks[i] * tick),
                        previewQuantity, -stopTicks[i]);
                    AccumulateLevel(targetLevels,
                        master.RoundToTickSize(entryPrice + profitSign * targetTicks[i] * tick),
                        previewQuantity, targetTicks[i]);
                }

                foreach (KeyValuePair<double, LevelInfo> level in stopLevels)
                    DrawOrderMarker(chartScale, level.Key, StopStroke,
                        $"{level.Value.Quantity} {exit} STP",
                        LevelValueLabel(master, level.Key, level.Value.SignedTicks, level.Value.Quantity), textFormat);

                foreach (KeyValuePair<double, LevelInfo> level in targetLevels)
                    DrawOrderMarker(chartScale, level.Key, TargetStroke,
                        $"{level.Value.Quantity} {exit} LMT",
                        LevelValueLabel(master, level.Key, level.Value.SignedTicks, level.Value.Quantity), textFormat);
            }
            finally
            {
                textFormat.Dispose();
            }
        }

        /// <summary>
        /// Draws one preview level the way the platform draws a working order: a chevron
        /// tag naming the order, a line running from the tag's point to the right edge,
        /// and a price tag pointing at the level from the right.
        /// </summary>
        private void DrawOrderMarker(ChartScale chartScale, double price, Stroke stroke,
            string label, string priceLabel, SharpDX.DirectWrite.TextFormat textFormat)
        {
            if (stroke?.BrushDX == null || textWhiteBrushDx == null || textBlackBrushDx == null)
                return;

            float y = chartScale.GetYByValue(price);
            if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
                return;

            SharpDX.Direct2D1.Brush textBrush = TagTextBrush(stroke);
            float panelLeft = ChartPanel.X;
            float panelRight = ChartPanel.X + ChartPanel.W;
            const float padX = 5f, padY = 2f;

            using (var labelLayout = new SharpDX.DirectWrite.TextLayout(
                       Core.Globals.DirectWriteFactory, label, textFormat, ChartPanel.W, textFormat.FontSize))
            using (var priceLayout = new SharpDX.DirectWrite.TextLayout(
                       Core.Globals.DirectWriteFactory, priceLabel, textFormat, ChartPanel.W, textFormat.FontSize))
            {
                float tagBoxW = labelLayout.Metrics.Width + 2f * padX;
                float tagH = labelLayout.Metrics.Height + 2f * padY;
                float tip = tagH / 2f;
                float priceBoxW = priceLayout.Metrics.Width + 2f * padX;

                // The price tag hugs the right edge, its point aimed at the level.
                float priceBoxX = panelRight - priceBoxW;
                float priceTipX = priceBoxX - tip;

                float tagX;
                switch (TagPosition)
                {
                    case ChartTradingTagPosition.Center:
                        tagX = panelLeft + (ChartPanel.W - tagBoxW - tip) / 2f;
                        break;
                    case ChartTradingTagPosition.Right:
                        tagX = priceTipX - TagMargin - tagBoxW - tip;
                        break;
                    default:
                        tagX = panelLeft + TagMargin;
                        break;
                }
                tagX = Math.Max(panelLeft + 2f, Math.Min(tagX, priceTipX - tagBoxW - tip - 2f));

                float top = y - tagH / 2f;
                float bottom = y + tagH / 2f;
                float tagTipX = tagX + tagBoxW + tip;

                // The line runs from the tag's point to the price tag's point only --
                // a working order's line does not cross behind its own tag.
                if (tagTipX < priceTipX)
                    RenderTarget.DrawLine(new Vector2(tagTipX, y), new Vector2(priceTipX, y),
                        stroke.BrushDX, stroke.Width, stroke.StrokeStyle);

                // Order tag: rectangle with a point on its right.
                FillTagGeometry(stroke.BrushDX, new[]
                {
                    new Vector2(tagX, top),
                    new Vector2(tagX + tagBoxW, top),
                    new Vector2(tagTipX, y),
                    new Vector2(tagX + tagBoxW, bottom),
                    new Vector2(tagX, bottom),
                });
                RenderTarget.DrawTextLayout(new Vector2(tagX + padX, top + padY), labelLayout, textBrush);

                // Price tag: rectangle against the right edge with a point on its left.
                FillTagGeometry(stroke.BrushDX, new[]
                {
                    new Vector2(priceBoxX, top),
                    new Vector2(panelRight, top),
                    new Vector2(panelRight, bottom),
                    new Vector2(priceBoxX, bottom),
                    new Vector2(priceTipX, y),
                });
                RenderTarget.DrawTextLayout(new Vector2(priceBoxX + padX, top + padY), priceLayout, textBrush);
            }
        }

        /// <summary>
        /// Fills a closed polygon, used for the pointed order and price tags.
        /// </summary>
        private void FillTagGeometry(SharpDX.Direct2D1.Brush brush, Vector2[] points)
        {
            using (var geometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory))
            {
                using (GeometrySink sink = geometry.Open())
                {
                    sink.BeginFigure(points[0], FigureBegin.Filled);
                    for (int i = 1; i < points.Length; i++)
                        sink.AddLine(points[i]);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }
                RenderTarget.FillGeometry(geometry, brush);
            }
        }

        /// <summary>
        /// What the right-side tag reads on a stop or target: the level price, the
        /// signed tick distance from entry, or the signed money value for the
        /// current quantity (ticks * tick value * quantity).
        /// </summary>
        private string LevelValueLabel(MasterInstrument master, double levelPrice, int signedTicks, int quantity)
        {
            switch (ValueDisplay)
            {
                case ChartTradingValueDisplay.Ticks:
                    return string.Format(Core.Globals.GeneralOptions.CurrentCulture, "{0:+0;-0}t", signedTicks);
                case ChartTradingValueDisplay.Currency:
                    double money = signedTicks * master.TickSize * master.PointValue * quantity;
                    return string.Format(Core.Globals.GeneralOptions.CurrentCulture, "{0:+$#,##0.00;-$#,##0.00}", money);
                default:
                    return master.FormatPrice(levelPrice);
            }
        }

        private struct LevelInfo
        {
            public int Quantity;
            public int SignedTicks;
        }

        private static void AccumulateLevel(Dictionary<double, LevelInfo> levels,
            double price, int quantity, int signedTicks)
        {
            LevelInfo info;
            if (levels.TryGetValue(price, out info))
                info.Quantity += quantity;
            else
                info = new LevelInfo { Quantity = quantity, SignedTicks = signedTicks };
            levels[price] = info;
        }

        /// <summary>
        /// Black or white tag text, whichever contrasts with the stroke's color.
        /// </summary>
        private SharpDX.Direct2D1.Brush TagTextBrush(Stroke stroke)
        {
            var solid = stroke.Brush as System.Windows.Media.SolidColorBrush;
            if (solid == null)
                return textWhiteBrushDx;

            double luminance = 0.299 * solid.Color.R + 0.587 * solid.Color.G + 0.114 * solid.Color.B;
            return luminance > 145 ? textBlackBrushDx : textWhiteBrushDx;
        }
        #endregion

        protected override void OnBarUpdate()
        {
            // Chart interaction only; nothing to calculate per bar.
        }
    }
}
