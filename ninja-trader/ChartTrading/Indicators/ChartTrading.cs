#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
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
// The ChartTrading button is the single gate. ON, a click while the preview is
// armed submits the entry to the ChartTrader account, and each enabled pair's
// stop and target -- OCO-linked per pair -- go live only once the entry fills,
// so a resting exit can never open a position. OFF, keys and clicks do nothing.
// Which account (sim or live) is fair game is the trader's own call via the
// ChartTrader account selector; this tool does not second-guess it.

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
    /// Entry type used when the click is on the favorable side of the market.
    /// </summary>
    public enum ChartTradingLimitSideType
    {
        Limit,
        MIT,
    }

    /// <summary>
    /// Entry type used when the click is beyond the market.
    /// </summary>
    public enum ChartTradingStopSideType
    {
        StopMarket,
        StopLimit,
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

    /// <summary>
    /// How long submitted orders stay alive: Day expires at session end, GTC
    /// rests until filled or cancelled.
    /// </summary>
    public enum ChartTradingTimeInForce
    {
        Day,
        Gtc,
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
        private System.Windows.Controls.Button breakevenButton;
        private System.Windows.Controls.Grid buttonPanel;
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

        // Position cache fed by Account.PositionUpdate, the way ABCompleteChartTrader
        // tracks it; read on the market-data thread by the auto-breakeven trigger.
        private double currentAvgPrice;
        private MarketPosition currentMarketPosition = MarketPosition.Flat;
        private AutoBreakevenState autoBreakevenState = AutoBreakevenState.Armed;
        private int positionCacheGeneration;
        private bool positionSeedPending;

        // Armed: waiting for the trigger. Pending: a move is in flight. WaitingForStops:
        // fired before this tool's stops were live; re-arms when one is accepted. Fired:
        // done for this position.
        private enum AutoBreakevenState { Armed, Pending, WaitingForStops, Fired }

        private class BracketCommit
        {
            public Order Entry;
            public Account Account;
            public double EntryFillPrice;
            public OrderAction ExitAction;
            public double[] StopPrices;
            public double[] TargetPrices;
            public int[] PairQuantities;

            // The entry quantity the exits currently cover, and the live exit orders
            // per pair (null until that pair first gets quantity). Fills are assigned
            // to pairs sequentially: pair 1 up to its own (possibly unequal) size,
            // then pair 2, and on -- PairQuantities[i] came from percentage
            // apportionment, so pairs are not all the same size.
            public int CoveredQuantity;
            public Order[] StopOrders;
            public Order[] TargetOrders;

            public int PairDesiredQuantity(int pairIndex, int filled)
            {
                int priorPairsQuantity = 0;
                for (int i = 0; i < pairIndex; i++)
                    priorPairsQuantity += PairQuantities[i];
                return Math.Max(0, Math.Min(PairQuantities[pairIndex], filled - priorPairsQuantity));
            }
        }
        #endregion

        #region Parameters
        [Display(Name = "Buy modifier", Order = 1, GroupName = "Gesture",
                 Description = "Hold this key and move over the chart to preview a buy bracket.")]
        public ChartTradingModifier BuyModifier { get; set; }

        [Display(Name = "Sell modifier", Order = 2, GroupName = "Gesture",
                 Description = "Hold this key and move over the chart to preview a sell bracket.")]
        public ChartTradingModifier SellModifier { get; set; }

        // Each pair is enabled by its own checkbox and carries a percentage share of
        // the ChartTrader quantity -- the entry always trades exactly that quantity,
        // never multiplied by pair count. Percentages are meant to total 100 across
        // the three pairs, but are renormalized across whichever pairs are enabled
        // (see ApportionQuantity), so the full entry quantity always ends up with a
        // stop/target somewhere and a disabled pair's share never goes uncovered.
        // With every pair disabled, a click means a plain entry with no bracket.
        [Display(Name = "Bracket 1", Order = 1, GroupName = "Bracket")]
        public bool Bracket1Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 1 (ticks)", Order = 2, GroupName = "Bracket")]
        public int Stop1Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 1 (ticks)", Order = 3, GroupName = "Bracket")]
        public int Target1Ticks { get; set; }

        [Range(0, 100)]
        [Display(Name = "Percent 1", Order = 4, GroupName = "Bracket",
                 Description = "Share of the ChartTrader quantity this pair takes. The three " +
                               "percentages should total 100; they are renormalized across " +
                               "enabled pairs either way.")]
        public int Percent1 { get; set; }

        [Display(Name = "Bracket 2", Order = 5, GroupName = "Bracket")]
        public bool Bracket2Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 2 (ticks)", Order = 6, GroupName = "Bracket")]
        public int Stop2Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 2 (ticks)", Order = 7, GroupName = "Bracket")]
        public int Target2Ticks { get; set; }

        [Range(0, 100)]
        [Display(Name = "Percent 2", Order = 8, GroupName = "Bracket",
                 Description = "Share of the ChartTrader quantity this pair takes. The three " +
                               "percentages should total 100; they are renormalized across " +
                               "enabled pairs either way.")]
        public int Percent2 { get; set; }

        [Display(Name = "Bracket 3", Order = 9, GroupName = "Bracket")]
        public bool Bracket3Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 3 (ticks)", Order = 10, GroupName = "Bracket")]
        public int Stop3Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 3 (ticks)", Order = 11, GroupName = "Bracket")]
        public int Target3Ticks { get; set; }

        [Range(0, 100)]
        [Display(Name = "Percent 3", Order = 12, GroupName = "Bracket",
                 Description = "Share of the ChartTrader quantity this pair takes. The three " +
                               "percentages should total 100; they are renormalized across " +
                               "enabled pairs either way.")]
        public int Percent3 { get; set; }

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

        [Display(Name = "Limit-side entry", Order = 2, GroupName = "Trading",
                 Description = "Order type when the click is on the favorable side of the market: " +
                               "plain limit, or market-if-touched.")]
        public ChartTradingLimitSideType LimitSideType { get; set; }

        [Display(Name = "Stop-side entry", Order = 3, GroupName = "Trading",
                 Description = "Order type when the click is beyond the market: stop-market, or " +
                               "stop-limit with the offset below.")]
        public ChartTradingStopSideType StopSideType { get; set; }

        [Range(0, 1000)]
        [Display(Name = "Stop-limit offset (ticks)", Order = 4, GroupName = "Trading",
                 Description = "How far beyond the stop trigger the stop-limit's limit price sits, " +
                               "in the direction of the entry.")]
        public int StopLimitOffsetTicks { get; set; }

        [Display(Name = "Time in force", Order = 9, GroupName = "Trading",
                 Description = "Applies to the entry and to every stop and target it places. " +
                               "Day orders expire at session end; GTC orders rest until filled " +
                               "or cancelled.")]
        public ChartTradingTimeInForce OrderTimeInForce { get; set; }

        [Display(Name = "Auto breakeven", Order = 6, GroupName = "Trading",
                 Description = "Once price runs the trigger distance in the position's favor, move every " +
                               "working ChartTrading stop to breakeven automatically -- the same move as " +
                               "the Stops-to-BE button, fired once per position. The button remains the " +
                               "manual bypass.")]
        public bool AutoBreakevenEnabled { get; set; }

        [Range(1, 10000)]
        [Display(Name = "Auto breakeven trigger (ticks)", Order = 7, GroupName = "Trading",
                 Description = "How many ticks in profit before the automatic move fires.")]
        public int AutoBreakevenTriggerTicks { get; set; }

        [Range(-100, 1000)]
        [Display(Name = "Breakeven offset (ticks)", Order = 8, GroupName = "Trading",
                 Description = "Where breakeven lands relative to the position's average price, in the " +
                               "profit direction: 2 locks two ticks of profit, 0 is exact breakeven. " +
                               "Applies to the button and to auto breakeven.")]
        public int BreakevenOffsetTicks { get; set; }

        [Display(Name = "Separate stacked stops", Order = 5, GroupName = "Trading",
                 Description = "When two pairs put their stops on the same price, nudge each extra stop " +
                               "one tick further from the entry, so the chart shows them individually " +
                               "and they can be moved one at a time. Off keeps the exact configured " +
                               "prices; stacked stops then drag as one.")]
        public bool SeparateStackedStops { get; set; }

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
                Percent1 = 25;
                Stop2Ticks = 50;
                Target2Ticks = 100;
                Percent2 = 25;
                Stop3Ticks = 50;
                Target3Ticks = 150;
                Percent3 = 50;

                LimitSideType = ChartTradingLimitSideType.Limit;
                StopSideType = ChartTradingStopSideType.StopMarket;
                StopLimitOffsetTicks = 2;
                SeparateStackedStops = false;
                OrderTimeInForce = ChartTradingTimeInForce.Day;
                AutoBreakevenEnabled = false;
                AutoBreakevenTriggerTicks = 30;
                BreakevenOffsetTicks = 0;
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
                    Margin = new Thickness(0, 0, 0, 2),
                    Cursor = Cursors.Hand,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                };
                toggleButton.Click += OnToggleClicked;
                UpdateToggleVisual();

                // Moves every working ChartTrading stop to its entry's fill price.
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
                buttonPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                System.Windows.Controls.Grid.SetRow(toggleButton, 0);
                System.Windows.Controls.Grid.SetRow(breakevenButton, 1);
                buttonPanel.Children.Add(toggleButton);
                buttonPanel.Children.Add(breakevenButton);

                var chartTrader = (chartWindow as Chart)?.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                chartTraderGrid = chartTrader?.Content as System.Windows.Controls.Grid;
                if (chartTraderGrid != null)
                {
                    buttonPanel.Margin = new Thickness(2, 6, 2, 2);
                    toggleButtonRow = new System.Windows.Controls.RowDefinition { Height = GridLength.Auto };
                    chartTraderGrid.RowDefinitions.Add(toggleButtonRow);
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

                // Subscribe to the ChartTrader account up front, so auto-breakeven
                // and the position cache work for positions that predate this
                // instance (a commit re-subscribes if the account changes later).
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
                    if (buttonPanel != null)
                    {
                        if (toggleButton != null)
                            toggleButton.Click -= OnToggleClicked;
                        if (breakevenButton != null)
                            breakevenButton.Click -= OnBreakevenClicked;
                        if (buttonInChartTrader)
                        {
                            chartTraderGrid?.Children.Remove(buttonPanel);
                            if (toggleButtonRow != null)
                                chartTraderGrid?.RowDefinitions.Remove(toggleButtonRow);
                        }
                        else
                        {
                            UserControlCollection.Remove(buttonPanel);
                        }
                        toggleButton = null;
                        breakevenButton = null;
                        buttonPanel = null;
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

            toggleButton.Content = tradingEnabled ? "ChartTrading ON" : "ChartTrading OFF";
            toggleButton.Background = tradingEnabled ? Brushes.SeaGreen : Brushes.DimGray;
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
            if (!tradingEnabled || previewSide == Side.None || !pointerOverPanel)
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

            int quantity = chartTrader.Quantity > 0 ? chartTrader.Quantity : previewQuantity;
            double entryPrice = master.RoundToTickSize(scale.GetValueByY(pointerDeviceY));
            bool isBuy = previewSide == Side.Buy;
            int profitSign = isBuy ? 1 : -1;
            double tick = master.TickSize;

            string entryLabel;
            OrderType entryType = InferEntryType(isBuy, entryPrice, out entryLabel);
            double limitPrice;
            double stopPrice;
            EntryPriceFields(entryType, isBuy, entryPrice, master, out limitPrice, out stopPrice);

            bool[] pairEnabled = { Bracket1Enabled, Bracket2Enabled, Bracket3Enabled };
            int[] stopTicks = { Stop1Ticks, Stop2Ticks, Stop3Ticks };
            int[] targetTicks = { Target1Ticks, Target2Ticks, Target3Ticks };
            int[] percents = { Percent1, Percent2, Percent3 };

            bool[] effectiveEnabled;
            List<int> pairQuantities;
            SplitByShare(quantity, pairEnabled, percents, out effectiveEnabled, out pairQuantities);

            // A zero-share pair must not reach BuildStopPrices at all: SeparateStackedStops
            // nudges colliding stop prices apart, and a pair that will never place an order
            // must not reserve a price slot that pushes a real pair's stop away from its
            // configured distance.
            List<double> stops = BuildStopPrices(master, entryPrice, profitSign, effectiveEnabled, stopTicks);
            var targets = new List<double>();
            for (int i = 0; i < effectiveEnabled.Length; i++)
            {
                if (!effectiveEnabled[i])
                    continue;
                targets.Add(master.RoundToTickSize(entryPrice + profitSign * targetTicks[i] * tick));
            }

            bool anyPairEnabled = false;
            foreach (bool enabled in pairEnabled)
                if (enabled) { anyPairEnabled = true; break; }
            if (anyPairEnabled && stops.Count == 0)
            {
                Log("ChartTrading: enabled pairs round to 0 lots each (percentages too low or all 0%); entry would be unprotected, order not placed.",
                    NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }
            int entryQuantity = quantity;

            var commit = new BracketCommit
            {
                Account = account,
                ExitAction = isBuy ? OrderAction.Sell : OrderAction.BuyToCover,
                StopPrices = stops.ToArray(),
                TargetPrices = targets.ToArray(),
                PairQuantities = pairQuantities.ToArray(),
            };
            OrderAction entryAction = isBuy ? OrderAction.Buy : OrderAction.SellShort;

            // Creation and submission run on NinjaTrader's own thread, the shape the
            // ABCompleteChartTrader reference uses. The commit registers before Submit
            // so a fast fill cannot race past the registry.
            TimeInForce entryTif = OrderTimeInForce == ChartTradingTimeInForce.Gtc
                ? TimeInForce.Gtc : TimeInForce.Day;
            TriggerCustomEvent(o =>
            {
                Order entry = account.CreateOrder(Instrument, entryAction, entryType, OrderEntry.Manual,
                    entryTif, entryQuantity, limitPrice, stopPrice, string.Empty, "CT Entry",
                    Core.Globals.MaxDate, null);

                lock (orderLock)
                {
                    commit.Entry = entry;
                    activeCommits.Add(commit);
                    EnsureAccountSubscription(account);
                }
                SeedPositionCache(account);

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
        /// Seeds the position cache from the account so a reload mid-position still
        /// arms auto-breakeven. Runs after (never inside) orderLock: the positions
        /// collection needs its own lock -- the shipped market-analyzer columns take
        /// lock(account.Positions) before enumerating -- and nesting the two would
        /// invite lock-order inversion. A PositionUpdate that lands in between wins
        /// over this snapshot via the positionSeedPending handshake.
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

        // Arrives off the UI thread; keeps the auto-breakeven trigger armed with the
        // live position, and re-arms it whenever the position closes or flips.
        private void OnAccountPositionUpdate(object sender, PositionEventArgs e)
        {
            if (e.Position?.Instrument == null || e.Position.Instrument.FullName != Instrument.FullName)
                return;

            // A closed position arrives as Operation.Remove with the last direction
            // still on it -- the shipped market-analyzer columns key off Operation for
            // exactly this reason. MarketPosition alone would read as still open.
            bool nowFlat = e.Operation == Operation.Remove || e.MarketPosition == MarketPosition.Flat;

            lock (orderLock)
            {
                // A queued event from a superseded account must not contaminate the
                // current account's cache.
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

        // Arrives off the UI thread. Exits go live only against filled entry quantity;
        // a resting stop or target without a position would OPEN a trade, not close one.
        private void OnAccountOrderUpdate(object sender, OrderEventArgs e)
        {
            // An auto-breakeven that fired before the exits were live re-arms as soon
            // as one of this tool's stops is accepted, instead of polling every tick.
            if (e.Order.Name == "CT Stop"
                && (e.OrderState == OrderState.Working || e.OrderState == OrderState.Accepted))
            {
                lock (orderLock)
                {
                    if (autoBreakevenState == AutoBreakevenState.WaitingForStops
                        && ReferenceEquals(sender, subscribedAccount))
                        autoBreakevenState = AutoBreakevenState.Armed;
                }
            }

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

            if (e.OrderState == OrderState.PartFilled || e.OrderState == OrderState.Filled)
            {
                // Bracket whatever has filled so far: a partial fill gets exits sized
                // to the filled amount immediately, grown as further fills arrive,
                // instead of waiting (exposed) for the full fill.
                bool submitNow = false;
                lock (orderLock)
                {
                    if (e.Order.Filled > commit.CoveredQuantity)
                    {
                        commit.CoveredQuantity = e.Order.Filled;
                        commit.EntryFillPrice = e.AverageFillPrice;
                        submitNow = true;
                    }
                }
                if (submitNow)
                    EnsureExits(commit, sender as Account);
            }
            else if (e.OrderState == OrderState.Cancelled || e.OrderState == OrderState.Rejected)
            {
                bool coverNow = false;
                lock (orderLock)
                {
                    activeCommits.Remove(commit);
                    if (e.Order.Filled > commit.CoveredQuantity)
                    {
                        commit.CoveredQuantity = e.Order.Filled;
                        commit.EntryFillPrice = e.AverageFillPrice;
                        coverNow = true;
                    }
                }
                if (coverNow)
                {
                    EnsureExits(commit, sender as Account);
                    Log($"ChartTrading: entry ended after a partial fill of {e.Order.Filled}; bracket sized to the filled amount.",
                        NinjaTrader.Cbi.LogLevel.Information);
                }
            }
        }

        /// <summary>
        /// Makes the commit's exit orders cover its filled entry quantity: pairs that
        /// need quantity for the first time are created (stop + target, OCO-linked),
        /// pairs whose quantity grew are resized in place. Safe to queue repeatedly --
        /// TriggerCustomEvent callbacks run serialized on the instrument's event
        /// thread, and each run reads the latest covered quantity.
        /// </summary>
        private void EnsureExits(BracketCommit commit, Account account)
        {
            if (account == null)
                return;

            TriggerCustomEvent(o =>
            {
                int filled;
                lock (orderLock)
                {
                    filled = commit.CoveredQuantity;
                    if (commit.StopOrders == null)
                    {
                        commit.StopOrders = new Order[commit.StopPrices.Length];
                        commit.TargetOrders = new Order[commit.StopPrices.Length];
                    }
                }

                TimeInForce tif = OrderTimeInForce == ChartTradingTimeInForce.Gtc
                    ? TimeInForce.Gtc : TimeInForce.Day;
                var submissions = new List<Order>();
                var changes = new List<Order>();
                for (int i = 0; i < commit.StopPrices.Length; i++)
                {
                    int desired = commit.PairDesiredQuantity(i, filled);
                    if (desired <= 0)
                        continue;

                    Order stop = commit.StopOrders[i];
                    if (stop == null)
                    {
                        // Each pair's stop and target cancel each other.
                        string oco = "CT-" + Guid.NewGuid().ToString("N");
                        stop = account.CreateOrder(Instrument, commit.ExitAction, OrderType.StopMarket,
                            OrderEntry.Manual, tif, desired, 0, commit.StopPrices[i],
                            oco, "CT Stop", Core.Globals.MaxDate, null);
                        Order target = account.CreateOrder(Instrument, commit.ExitAction, OrderType.Limit,
                            OrderEntry.Manual, tif, desired, commit.TargetPrices[i], 0,
                            oco, "CT Target", Core.Globals.MaxDate, null);
                        commit.StopOrders[i] = stop;
                        commit.TargetOrders[i] = target;
                        submissions.Add(stop);
                        submissions.Add(target);
                        continue;
                    }

                    // The pair exists from an earlier partial fill; grow it if it is
                    // still alive. A pair the market already took out stays as-is.
                    Order pairTarget = commit.TargetOrders[i];
                    if (stop.Quantity != desired
                        && (stop.OrderState == OrderState.Working || stop.OrderState == OrderState.Accepted)
                        && pairTarget != null
                        && (pairTarget.OrderState == OrderState.Working || pairTarget.OrderState == OrderState.Accepted))
                    {
                        stop.QuantityChanged = desired;
                        stop.LimitPriceChanged = stop.LimitPrice;
                        stop.StopPriceChanged = stop.StopPrice;
                        changes.Add(stop);
                        pairTarget.QuantityChanged = desired;
                        pairTarget.LimitPriceChanged = pairTarget.LimitPrice;
                        pairTarget.StopPriceChanged = pairTarget.StopPrice;
                        changes.Add(pairTarget);
                    }
                }

                if (submissions.Count > 0)
                    account.Submit(submissions);
                if (changes.Count > 0)
                    account.Change(changes);
            }, null);
        }

        /// <summary>
        /// The stop price for each enabled pair, in pair order. With separation on,
        /// a stop landing on an already-used price steps one tick further from the
        /// entry per collision, so the first pair always keeps its exact configured
        /// distance and stacked stops become individually draggable on the chart.
        /// </summary>
        private List<double> BuildStopPrices(MasterInstrument master, double entryPrice,
            int profitSign, bool[] pairEnabled, int[] stopTicks)
        {
            var prices = new List<double>();
            for (int i = 0; i < pairEnabled.Length; i++)
            {
                if (!pairEnabled[i])
                    continue;

                double price = master.RoundToTickSize(entryPrice - profitSign * stopTicks[i] * master.TickSize);
                if (SeparateStackedStops)
                    while (prices.Contains(price))
                        price = master.RoundToTickSize(price - profitSign * master.TickSize);
                prices.Add(price);
            }
            return prices;
        }

        /// <summary>
        /// Splits totalQuantity across enabled pairs by their configured percentage, in
        /// pair order (matching BuildStopPrices' compaction, so the result lines up
        /// with StopPrices/TargetPrices index for index). Percentages are renormalized
        /// across whichever pairs are enabled -- they need not sum to 100 -- so the full
        /// quantity is always accounted for as long as at least one enabled pair has a
        /// positive share.
        ///
        /// Uses largest-remainder apportionment: each pair's ideal (fractional) share is
        /// floored, then the leftover lots from rounding go one at a time to the pairs
        /// with the largest fractional remainder, so no lot is lost to rounding and every
        /// pair gets as close to its configured share as an integer allows. Ties in the
        /// remainder are broken by pair order so the result is deterministic.
        /// </summary>
        private static int[] ApportionQuantity(int totalQuantity, bool[] pairEnabled, int[] percents)
        {
            var enabledPercents = new List<int>();
            for (int i = 0; i < pairEnabled.Length; i++)
                if (pairEnabled[i])
                    enabledPercents.Add(percents[i]);

            int pairCount = enabledPercents.Count;
            var quantities = new int[pairCount];
            if (totalQuantity <= 0 || pairCount == 0)
                return quantities;

            double percentSum = 0;
            foreach (int percent in enabledPercents)
                percentSum += percent;
            if (percentSum <= 0)
                return quantities;

            var idealShares = new double[pairCount];
            var flooredShares = new int[pairCount];
            int flooredTotal = 0;
            for (int i = 0; i < pairCount; i++)
            {
                idealShares[i] = totalQuantity * (enabledPercents[i] / percentSum);
                flooredShares[i] = (int)Math.Floor(idealShares[i]);
                flooredTotal += flooredShares[i];
                quantities[i] = flooredShares[i];
            }

            var byRemainderDesc = new List<int>();
            for (int i = 0; i < pairCount; i++)
                byRemainderDesc.Add(i);
            byRemainderDesc.Sort((a, b) =>
            {
                double remainderA = idealShares[a] - flooredShares[a];
                double remainderB = idealShares[b] - flooredShares[b];
                int cmp = remainderB.CompareTo(remainderA);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            int leftoverLots = totalQuantity - flooredTotal;
            for (int i = 0; i < leftoverLots && i < pairCount; i++)
                quantities[byRemainderDesc[i]] += 1;

            return quantities;
        }

        /// <summary>
        /// Apportions totalQuantity via ApportionQuantity, then drops pairs whose share
        /// rounded down to zero lots from "enabled" entirely. Shared by the click handler
        /// and the preview so a pair that will never place an order never reaches
        /// BuildStopPrices, in either path -- otherwise it would still reserve a stop-price
        /// slot for SeparateStackedStops' collision separation despite placing nothing.
        /// </summary>
        private static void SplitByShare(int totalQuantity, bool[] pairEnabled, int[] percents,
            out bool[] effectiveEnabled, out List<int> pairQuantities)
        {
            int[] rawPairQuantities = ApportionQuantity(totalQuantity, pairEnabled, percents);
            effectiveEnabled = new bool[pairEnabled.Length];
            pairQuantities = new List<int>();
            int compactedIndex = 0;
            for (int i = 0; i < pairEnabled.Length; i++)
            {
                if (!pairEnabled[i])
                    continue;
                int pairQuantity = rawPairQuantities[compactedIndex++];
                if (pairQuantity <= 0)
                    continue;
                effectiveEnabled[i] = true;
                pairQuantities.Add(pairQuantity);
            }
        }

        /// <summary>
        /// The entry order type a click at this price would submit: the configured
        /// limit-side type on the favorable side of the last traded price, the
        /// configured stop-side type beyond it, and market exactly at it.
        /// </summary>
        private OrderType InferEntryType(bool isBuy, double entryPrice, out string label)
        {
            label = "MKT";
            if (Bars == null || Bars.Count == 0)
                return OrderType.Market;

            double last = Bars.GetClose(Bars.Count - 1);
            if (entryPrice.ApproxCompare(last) == 0)
                return OrderType.Market;

            bool favorable = isBuy ? entryPrice < last : entryPrice > last;
            if (favorable)
            {
                bool mit = LimitSideType == ChartTradingLimitSideType.MIT;
                label = mit ? "MIT" : "LMT";
                return mit ? OrderType.MIT : OrderType.Limit;
            }

            bool stopLimit = StopSideType == ChartTradingStopSideType.StopLimit;
            label = stopLimit ? "STP LMT" : "STP";
            return stopLimit ? OrderType.StopLimit : OrderType.StopMarket;
        }

        /// <summary>
        /// The limit/stop price fields for an entry of the given type. MIT rides the
        /// stop field, and a stop-limit's limit sits the configured offset beyond the
        /// trigger in the direction of the entry.
        /// </summary>
        private void EntryPriceFields(OrderType type, bool isBuy, double entryPrice,
            MasterInstrument master, out double limitPrice, out double stopPrice)
        {
            limitPrice = 0;
            stopPrice = 0;
            switch (type)
            {
                case OrderType.Limit:
                    limitPrice = entryPrice;
                    break;
                case OrderType.MIT:
                case OrderType.StopMarket:
                    stopPrice = entryPrice;
                    break;
                case OrderType.StopLimit:
                    stopPrice = entryPrice;
                    limitPrice = master.RoundToTickSize(
                        entryPrice + (isBuy ? 1 : -1) * StopLimitOffsetTicks * master.TickSize);
                    break;
            }
        }

        /// <summary>
        /// Moves every working ChartTrading stop on this instrument to the position's
        /// average fill price. The manual bypass for auto-breakeven: it makes the same
        /// move, on demand, whether or not the automatic trigger is enabled or has fired.
        /// </summary>
        /// <remarks>
        /// Deliberately reload-proof: the stops are found on the account by their
        /// "CT Stop" name instead of through this instance's in-memory registry,
        /// because recompiling or reloading the indicator wipes that memory while the
        /// orders live on. Every outcome lands in the log, so a click is never silent.
        /// </remarks>
        private void OnBreakevenClicked(object sender, RoutedEventArgs e)
        {
            double last = Bars != null && Bars.Count > 0 ? Bars.GetClose(Bars.Count - 1) : 0;
            Account account = ChartControl.OwnerChart?.ChartTrader?.Account;
            if (account == null)
            {
                Log("ChartTrading: no ChartTrader account selected; stops not moved.",
                    NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }

            MoveStopsToBreakeven(account, last, "button");
        }

        /// <summary>
        /// Watches live ticks for the auto-breakeven trigger: once price has run the
        /// configured distance in the position's favor, make the same move the button
        /// makes, once per position.
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

            // The offset has to land inside the market when the move fires, or the
            // clamp would park the stop at the last price; require at least one tick
            // of room beyond it before triggering.
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
                // The cache may have moved on (position closed, flipped, or scaled)
                // between the snapshot and here; a stale tick must not fire on a
                // replacement position that never reached its own trigger.
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
                // The auto path validates its snapshot is still current before touching
                // orders; the button (no generation) always acts on what is live now.
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
                    Log("ChartTrading: no open position on " + Instrument.FullName + "; stops not moved.",
                        NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                // Clamped so the stop never crosses the market -- the same guard the
                // ABCompleteChartTrader breakeven uses: a long's stop stays at or
                // below the last price, a short's at or above it.
                bool isLong = position.MarketPosition == MarketPosition.Long;
                double breakeven = position.AveragePrice
                    + (isLong ? 1 : -1) * BreakevenOffsetTicks * master.TickSize;
                if (last > 0)
                {
                    // One tick inside the market, not at it: a stop resting exactly at
                    // the last price gets rejected or fills instantly.
                    breakeven = isLong
                        ? Math.Min(breakeven, last - master.TickSize)
                        : Math.Max(breakeven, last + master.TickSize);
                }
                breakeven = master.RoundToTickSize(breakeven);

                var candidates = new List<Order>();
                lock (account.Orders)
                {
                    foreach (Order order in account.Orders)
                    {
                        if (order.Name != "CT Stop" || order.Instrument == null
                            || order.Instrument.FullName != Instrument.FullName)
                            continue;
                        if (order.OrderState != OrderState.Working && order.OrderState != OrderState.Accepted)
                            continue;
                        candidates.Add(order);
                    }
                }

                var changes = new List<Order>();
                int alreadySafer = 0;
                foreach (Order order in candidates)
                {
                    // Never loosen protection: a stop already at or beyond breakeven
                    // (say, manually trailed past it) stays where it is.
                    if (isLong ? order.StopPrice >= breakeven : order.StopPrice <= breakeven)
                    {
                        alreadySafer++;
                        continue;
                    }

                    // Stage every changeable field; only the stop price moves.
                    order.QuantityChanged = order.Quantity;
                    order.LimitPriceChanged = order.LimitPrice;
                    order.StopPriceChanged = breakeven;
                    changes.Add(order);
                }

                if (changes.Count == 0)
                {
                    // Stops already at or beyond BE count as done; no stops at all means
                    // the exits are not live yet -- wait for one to be accepted instead
                    // of burning the latch (or polling every tick).
                    if (expectedGeneration >= 0)
                        lock (orderLock)
                            autoBreakevenState = alreadySafer > 0
                                ? AutoBreakevenState.Fired
                                : AutoBreakevenState.WaitingForStops;
                    Log(alreadySafer > 0
                            ? $"ChartTrading: all {alreadySafer} CT stop(s) on {Instrument.FullName} already at or beyond breakeven; nothing moved ({reason})."
                            : $"ChartTrading: no working CT stops on {Instrument.FullName} to move ({reason}).",
                        NinjaTrader.Cbi.LogLevel.Information);
                    return;
                }

                account.Change(changes);
                if (expectedGeneration >= 0)
                    lock (orderLock)
                        autoBreakevenState = AutoBreakevenState.Fired;
                string skippedNote = alreadySafer > 0 ? $", {alreadySafer} already safer left alone" : string.Empty;
                Log($"ChartTrading: moved {changes.Count} stop(s) to breakeven {master.FormatPrice(breakeven)}{skippedNote} ({reason}).",
                    NinjaTrader.Cbi.LogLevel.Information);
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

            // The label previews exactly the order type a click would submit.
            string entryType;
            InferEntryType(isBuy, entryPrice, out entryType);

            SharpDX.DirectWrite.TextFormat textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
            try
            {
                // The entry always trades the ChartTrader quantity outright, never
                // multiplied by pair count; each enabled pair's stop/target instead
                // carries its renormalized percentage share (see ApportionQuantity).
                // Levels sharing a price merge into one marker with the summed
                // quantity, the way the orders would sit in the book. With no pair
                // enabled, the click means a plain entry of the ChartTrader quantity.
                bool[] pairEnabled = { Bracket1Enabled, Bracket2Enabled, Bracket3Enabled };
                int[] stopTicks = { Stop1Ticks, Stop2Ticks, Stop3Ticks };
                int[] targetTicks = { Target1Ticks, Target2Ticks, Target3Ticks };
                int[] percents = { Percent1, Percent2, Percent3 };

                bool[] effectiveEnabled;
                List<int> pairQuantities;
                SplitByShare(previewQuantity, pairEnabled, percents, out effectiveEnabled, out pairQuantities);

                bool anyPairEnabled = false;
                foreach (bool enabled in pairEnabled)
                    if (enabled) { anyPairEnabled = true; break; }
                if (anyPairEnabled && pairQuantities.Count == 0)
                    // Every enabled pair rounds down to 0 lots -- a click would be
                    // rejected outright (see OnPanelClick), so preview nothing rather
                    // than show an order that can't actually be submitted.
                    return;

                int entryQuantity = previewQuantity;

                DrawOrderMarker(chartScale, entryPrice, EntryStroke,
                    $"{entryQuantity} {enter} {entryType}", master.FormatPrice(entryPrice), textFormat);

                var stopLevels = new Dictionary<double, LevelInfo>();
                var targetLevels = new Dictionary<double, LevelInfo>();

                List<double> nudgedStops = BuildStopPrices(master, entryPrice, profitSign, effectiveEnabled, stopTicks);
                int stopIndex = 0;
                for (int i = 0; i < effectiveEnabled.Length; i++)
                {
                    if (!effectiveEnabled[i])
                        continue;

                    // The tag shows the stop's true distance, which with separation on
                    // can be a tick beyond the configured one.
                    double stopPrice = nudgedStops[stopIndex];
                    int pairQuantity = pairQuantities[stopIndex];
                    stopIndex++;

                    int stopDistance = (int)Math.Round((entryPrice - stopPrice) * profitSign / tick);
                    AccumulateLevel(stopLevels, stopPrice, pairQuantity, -stopDistance);
                    AccumulateLevel(targetLevels,
                        master.RoundToTickSize(entryPrice + profitSign * targetTicks[i] * tick),
                        pairQuantity, targetTicks[i]);
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
