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
    /// How each bracket pair's stop and target distances are set.
    /// </summary>
    public enum ChartTradingBracketDistance
    {
        Ticks,
        Atr,
    }

    public enum ChartTradingAtrSmoothing
    {
        Exponential,
        Wilder,
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
    /// Where a click's entry goes: at the price under the mouse, or at the market.
    /// </summary>
    public enum ChartTradingEntryPrice
    {
        Mouse,
        Market,
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
    /// How long submitted orders stay alive: Day expires at session end, GTC
    /// rests until filled or cancelled.
    /// </summary>
    public enum ChartTradingTimeInForce
    {
        Day,
        Gtc,
    }

    [System.ComponentModel.TypeConverter("NinjaTrader.NinjaScript.Indicators.FilipeAmaral.ChartTradingTypeConverter")]
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
        // removing the indicator. Mounted in the ChartTrader sidebar.
        private System.Windows.Controls.Button toggleButton;
        private System.Windows.Controls.Grid buttonPanel;
        private System.Windows.Controls.Grid chartTraderGrid;
        private System.Windows.Controls.RowDefinition toggleButtonRow;

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
        private Series<double> atrSeries;
        private double currentAtr;
        // The ATR stop of the filled entry that opened the current position, so
        // an R-based breakeven trigger measures against that stop; 0 when unknown.
        private int openingAtrStopTicks;

        // Position cache fed by Account.PositionUpdate, the way ABCompleteChartTrader
        // tracks it; read on the market-data thread by the auto-breakeven trigger.
        private double currentAvgPrice;
        private int currentQuantity;
        private MarketPosition currentMarketPosition = MarketPosition.Flat;
        // A trim found an exit mid-change; retried on that exit's next update.
        private bool reconcilePending;
        // When an entry fill was counted while the cache read flat. Normally the
        // fill's own position update follows; if that update came first, the fill
        // was flattening, and its exits close nothing or outsize the position.
        private DateTime flatCoverageSince = DateTime.MinValue;
        private System.Threading.Timer flatCoverageTimer;
        // When exits were first seen larger than the position. An exit fill can
        // shrink the position before its own fill state lands; cutting at once
        // could cut the bracket the fill did not touch, so the cut waits this long.
        private DateTime excessSince = DateTime.MinValue;
        private System.Threading.Timer trimTimer;
        private static readonly TimeSpan TrimDelay = TimeSpan.FromMilliseconds(500);
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
            // ATR stop distance of an entry clicked while flat; 0 otherwise.
            public int OpeningAtrStopTicks;

            // The configured exit distances the prices are derived from, compacted to
            // the enabled pairs and index-aligned with PairQuantities. Prices are
            // resolved from the entry's actual fill, not the click location: an entry
            // dragged or riding an attached indicator (a moving average) fills away
            // from where it was placed, and the stops and targets must sit their
            // configured distance from THAT, not from the original click. StopPrices/
            // TargetPrices above start as the click-relative estimate and are
            // overwritten once, the first time exits are created and EntryFillPrice is
            // known (PricesResolved). Freezing at first fill keeps a later partial's
            // drift in the average from moving stops that are already working.
            public int[] StopTicks;
            public int[] TargetTicks;
            public int ProfitSign;
            public bool PricesResolved;

            // The entry quantity the exits currently cover, and the live exit orders
            // per pair (null until that pair first gets quantity). Fills are assigned
            // to pairs sequentially: pair 1 up to its own (possibly unequal) size,
            // then pair 2, and on -- PairQuantities[i] came from percentage
            // apportionment, so pairs are not all the same size.
            public int CoveredQuantity;
            // The quantity EnsureExits last actually staged (create or change) for
            // each pair. The retry hook compares this against the pair's desired
            // quantity: a difference means a resize was skipped while the pair's
            // orders were in flight and must be retried when they come alive. A
            // trader's manual quantity change leaves this equal to desired, so it
            // never triggers a regrow of what they changed on purpose.
            public int[] PairAppliedQuantity;
            public Order[] StopOrders;
            public Order[] TargetOrders;
            // Set once the position these exits close is gone; EnsureExits then
            // places nothing more for this commit.
            public bool Orphaned;
            // Filled lots whose exits were filled, cancelled or trimmed away.
            public int OffsetQuantity;
            // Kept through a flat that may be the stale close of the position
            // before its fill; the next position update keeps it (its side) or
            // retires it.
            public bool KeptAtFlat;
            // When this commit's last fill was counted.
            public DateTime FillCountedAt = DateTime.MinValue;
            // The stop quantity the last grow asked for, 0 once acknowledged; a
            // stop coming back alive below it means the grow was rejected.
            public int[] RequestedStopQuantity;
            // One rejected stop or grow is retried; a second closes those lots.
            public bool ExitRetried;

            public int PairDesiredQuantity(int pairIndex, int filled)
            {
                int priorPairsQuantity = 0;
                for (int i = 0; i < pairIndex; i++)
                    priorPairsQuantity += PairQuantities[i];
                // The last pair takes any fill beyond the clicked size: the entry's quantity
                // can be raised on the chart while it rests, and those lots need exits too.
                int pairCap = pairIndex == PairQuantities.Length - 1 ? int.MaxValue : PairQuantities[pairIndex];
                return Math.Max(0, Math.Min(pairCap, filled - priorPairsQuantity));
            }
        }
        #endregion

        #region Parameters
        [Display(Name = "Buy modifier", Order = 1, GroupName = "1. Gesture",
                 Description = "Hold this key and move over the chart to preview a buy bracket.")]
        public ChartTradingModifier BuyModifier { get; set; }

        [Display(Name = "Enabled", Order = 0, GroupName = "1. Gesture",
                 Description = "Whether clicks place orders. The ChartTrading ON/OFF button flips this, " +
                               "and the state is kept across reloads and saved with the workspace.")]
        public bool TradingEnabled { get; set; }

        [Display(Name = "Sell modifier", Order = 2, GroupName = "1. Gesture",
                 Description = "Hold this key and move over the chart to preview a sell bracket.")]
        public ChartTradingModifier SellModifier { get; set; }

        // Each pair is enabled by its own checkbox and carries a percentage share of
        // the ChartTrader quantity -- the entry always trades exactly that quantity,
        // never multiplied by pair count. Percentages are meant to total 100 across
        // the three pairs, but are renormalized across whichever pairs are enabled
        // (see ApportionQuantity), so the full entry quantity always ends up with a
        // stop/target somewhere and a disabled pair's share never goes uncovered.
        // With every pair disabled, a click means a plain entry with no bracket.
        [Display(Name = "Bracket 1", Order = 10, GroupName = "2. Brackets")]
        public bool Bracket1Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 1 (ticks)", Order = 12, GroupName = "2. Brackets")]
        public int Stop1Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 1 (ticks)", Order = 13, GroupName = "2. Brackets")]
        public int Target1Ticks { get; set; }

        [Range(0, 100)]
        [Display(Name = "Percent 1", Order = 11, GroupName = "2. Brackets",
                 Description = "Share of the ChartTrader quantity this pair takes. The three " +
                               "percentages should total 100; they are renormalized across " +
                               "enabled pairs either way.")]
        public int Percent1 { get; set; }

        [Display(Name = "Bracket 2", Order = 20, GroupName = "2. Brackets")]
        public bool Bracket2Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 2 (ticks)", Order = 22, GroupName = "2. Brackets")]
        public int Stop2Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 2 (ticks)", Order = 23, GroupName = "2. Brackets")]
        public int Target2Ticks { get; set; }

        [Range(0, 100)]
        [Display(Name = "Percent 2", Order = 21, GroupName = "2. Brackets",
                 Description = "Share of the ChartTrader quantity this pair takes. The three " +
                               "percentages should total 100; they are renormalized across " +
                               "enabled pairs either way.")]
        public int Percent2 { get; set; }

        [Display(Name = "Bracket 3", Order = 30, GroupName = "2. Brackets")]
        public bool Bracket3Enabled { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop 3 (ticks)", Order = 32, GroupName = "2. Brackets")]
        public int Stop3Ticks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 3 (ticks)", Order = 33, GroupName = "2. Brackets")]
        public int Target3Ticks { get; set; }

        [Range(0, 100)]
        [Display(Name = "Percent 3", Order = 31, GroupName = "2. Brackets",
                 Description = "Share of the ChartTrader quantity this pair takes. The three " +
                               "percentages should total 100; they are renormalized across " +
                               "enabled pairs either way.")]
        public int Percent3 { get; set; }

        [Display(Name = "Distances", Order = 1, GroupName = "2. Brackets",
                 Description = "Ticks uses each pair's stop and target in ticks. ATR puts every " +
                               "stop the ATR multiple away, and each target that many R " +
                               "(times the stop distance), matching the position sizer's stop.")]
        [System.ComponentModel.RefreshProperties(System.ComponentModel.RefreshProperties.All)]
        public ChartTradingBracketDistance BracketDistance { get; set; }

        [Range(0.01, double.MaxValue)]
        [Display(Name = "Stop size (x ATR)", Order = 1, GroupName = "3. ATR stop")]
        public double StopAtrMultiple { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "ATR length", Order = 2, GroupName = "3. ATR stop")]
        public int AtrLength { get; set; }

        [Display(Name = "ATR smoothing", Order = 3, GroupName = "3. ATR stop")]
        public ChartTradingAtrSmoothing AtrSmoothing { get; set; }

        [Range(0.01, double.MaxValue)]
        [Display(Name = "Target 1 (R)", Order = 14, GroupName = "2. Brackets")]
        public double Target1R { get; set; }

        [Range(0.01, double.MaxValue)]
        [Display(Name = "Target 2 (R)", Order = 24, GroupName = "2. Brackets")]
        public double Target2R { get; set; }

        [Range(0.01, double.MaxValue)]
        [Display(Name = "Target 3 (R)", Order = 34, GroupName = "2. Brackets")]
        public double Target3R { get; set; }

        [Display(Name = "Tag position", Order = 1, GroupName = "6. Appearance",
                 Description = "Where the order tags sit: left, center, or right of the chart.")]
        public ChartTradingTagPosition TagPosition { get; set; }

        [Range(0, 2000)]
        [Display(Name = "Tag margin (pixels)", Order = 2, GroupName = "6. Appearance",
                 Description = "Distance kept between the tag and the chart border.")]
        public int TagMargin { get; set; }

        [Display(Name = "Show price", Order = 3, GroupName = "6. Appearance",
                 Description = "Stop and target tags show the level's price. The entry always shows its price. " +
                               "With every value below off, stops and targets draw without a price tag.")]
        public bool ShowLevelPrice { get; set; }

        [Display(Name = "Show ticks", Order = 4, GroupName = "6. Appearance",
                 Description = "Stop and target tags show their signed distance from the entry, in ticks.")]
        public bool ShowLevelTicks { get; set; }

        [Display(Name = "Show points", Order = 5, GroupName = "6. Appearance",
                 Description = "Stop and target tags show their signed distance from the entry, in points.")]
        public bool ShowLevelPoints { get; set; }

        [Display(Name = "Show money", Order = 6, GroupName = "6. Appearance",
                 Description = "Stop and target tags show the signed money value of the distance for the level's quantity.")]
        public bool ShowLevelMoney { get; set; }

        [Display(Name = "Entry price", Order = 0, GroupName = "4. Trading",
                 Description = "Mouse places the entry at the price under the pointer. Market sends a " +
                               "market order on click, wherever the pointer is; the preview sits at the " +
                               "ask for a buy and the bid for a sell, and the bracket is placed from the fill.")]
        [System.ComponentModel.RefreshProperties(System.ComponentModel.RefreshProperties.All)]
        public ChartTradingEntryPrice EntryPriceMode { get; set; }

        [Display(Name = "Limit-side entry", Order = 1, GroupName = "4. Trading",
                 Description = "Order type when the click is on the favorable side of the market: " +
                               "plain limit, or market-if-touched.")]
        public ChartTradingLimitSideType LimitSideType { get; set; }

        [Display(Name = "Stop-side entry", Order = 2, GroupName = "4. Trading",
                 Description = "Order type when the click is beyond the market: stop-market, or " +
                               "stop-limit with the offset below.")]
        public ChartTradingStopSideType StopSideType { get; set; }

        [Range(0, 1000)]
        [Display(Name = "Stop-limit offset (ticks)", Order = 3, GroupName = "4. Trading",
                 Description = "How far beyond the stop trigger the stop-limit's limit price sits, " +
                               "in the direction of the entry.")]
        public int StopLimitOffsetTicks { get; set; }

        [Display(Name = "Time in force", Order = 5, GroupName = "4. Trading",
                 Description = "Applies to the entry and to every stop and target it places. " +
                               "Day orders expire at session end; GTC orders rest until filled " +
                               "or cancelled.")]
        public ChartTradingTimeInForce OrderTimeInForce { get; set; }

        [Display(Name = "Auto breakeven", Order = 1, GroupName = "5. Breakeven",
                 Description = "Once price runs the trigger distance in the position's favor, move every " +
                               "working protective stop on the instrument to breakeven automatically, " +
                               "once per position. The Breakeven Stops indicator makes the same move by hand.")]
        public bool AutoBreakevenEnabled { get; set; }

        [Range(1, 10000)]
        [Display(Name = "Auto breakeven trigger (ticks)", Order = 2, GroupName = "5. Breakeven",
                 Description = "How many ticks in profit before the automatic move fires.")]
        public int AutoBreakevenTriggerTicks { get; set; }

        [Range(0.01, double.MaxValue)]
        [Display(Name = "Auto breakeven trigger (R)", Order = 3, GroupName = "5. Breakeven",
                 Description = "How many R in profit before the automatic move fires: " +
                               "1 means price has run one stop distance in the position's favor.")]
        public double AutoBreakevenTriggerR { get; set; }

        [Range(-100, 1000)]
        [Display(Name = "Breakeven offset (ticks)", Order = 4, GroupName = "5. Breakeven",
                 Description = "Where breakeven lands relative to the position's average price, in the " +
                               "profit direction: 2 locks two ticks of profit, 0 is exact breakeven.")]
        public int BreakevenOffsetTicks { get; set; }

        [Display(Name = "Separate stacked stops", Order = 4, GroupName = "4. Trading",
                 Description = "When two pairs put their stops on the same price, nudge each extra stop " +
                               "one tick further from the entry, so the chart shows them individually " +
                               "and they can be moved one at a time. Off keeps the exact configured " +
                               "prices; stacked stops then drag as one.")]
        public bool SeparateStackedStops { get; set; }

        // Strokes rather than plain brushes so each level carries its own color, width,
        // and dash style, and binds to the render target the way the platform's own
        // price-line indicator does. NinjaTrader persists Stroke properties natively.
        [Display(Name = "Entry line", Order = 1, GroupName = "7. Colors")]
        public Stroke EntryStroke { get; set; }

        [Display(Name = "Stop line", Order = 2, GroupName = "7. Colors")]
        public Stroke StopStroke { get; set; }

        [Display(Name = "Target line", Order = 3, GroupName = "7. Colors")]
        public Stroke TargetStroke { get; set; }
        #endregion

        private static System.Windows.Media.Brush Rgb(byte r, byte g, byte b)
        {
            System.Windows.Media.SolidColorBrush brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Preview a click-to-trade bracket by holding a modifier key; click to submit it.";
                Name = "ChartTrading";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;

                TradingEnabled = true;
                EntryPriceMode = ChartTradingEntryPrice.Mouse;
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
                BracketDistance = ChartTradingBracketDistance.Ticks;
                StopAtrMultiple = 1.5;
                AtrLength = 5;
                AtrSmoothing = ChartTradingAtrSmoothing.Exponential;
                Target1R = 1;
                Target2R = 2;
                Target3R = 3;

                LimitSideType = ChartTradingLimitSideType.Limit;
                StopSideType = ChartTradingStopSideType.StopMarket;
                StopLimitOffsetTicks = 2;
                SeparateStackedStops = false;
                OrderTimeInForce = ChartTradingTimeInForce.Day;
                AutoBreakevenEnabled = false;
                AutoBreakevenTriggerTicks = 30;
                AutoBreakevenTriggerR = 1;
                BreakevenOffsetTicks = 0;
                TagPosition = ChartTradingTagPosition.Center;
                TagMargin = 40;
                ShowLevelPrice = true;
                ShowLevelTicks = true;
                ShowLevelPoints = false;
                ShowLevelMoney = true;

                // Solid and thin, matching how the platform draws a working order.
                EntryStroke = new Stroke(Rgb(0x3E, 0x68, 0xA3), DashStyleHelper.Solid, 1f);
                StopStroke = new Stroke(Brushes.Crimson, DashStyleHelper.Solid, 1f);
                TargetStroke = new Stroke(Brushes.LimeGreen, DashStyleHelper.Solid, 1f);
            }
            else if (State == State.DataLoaded)
            {
                atrSeries = new Series<double>(this);
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
                // Live exits stay working, but an unfilled entry would fill with no
                // bracket once this instance stops listening, so it is cancelled.
                List<BracketCommit> pending = new List<BracketCommit>();
                lock (orderLock)
                {
                    foreach (BracketCommit commit in activeCommits)
                    {
                        if (commit.Entry == null)
                            continue;
                        OrderState entryState = commit.Entry.OrderState;
                        if (entryState != OrderState.Filled && entryState != OrderState.Cancelled && entryState != OrderState.Rejected)
                            pending.Add(commit);
                    }
                }
                foreach (BracketCommit commit in pending)
                {
                    commit.Account.Cancel(new[] { commit.Entry });
                    Log("ChartTrading: indicator removed or reloaded; cancelled unfilled entry " + commit.Entry.OrderId + " so it cannot fill without a stop.",
                        NinjaTrader.Cbi.LogLevel.Warning);
                }

                if (subscribedAccount != null)
                {
                    subscribedAccount.OrderUpdate -= OnAccountOrderUpdate;
                    subscribedAccount.PositionUpdate -= OnAccountPositionUpdate;
                    subscribedAccount = null;
                }
                lock (orderLock)
                {
                    if (flatCoverageTimer != null)
                    {
                        flatCoverageTimer.Dispose();
                        flatCoverageTimer = null;
                    }
                    if (trimTimer != null)
                    {
                        trimTimer.Dispose();
                        trimTimer = null;
                    }
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

                // The toggle lives in the ChartTrader sidebar: one auto-height row
                // appended to the ChartTrader grid.
                toggleButton = new System.Windows.Controls.Button
                {
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(8, 10, 8, 10),
                    Margin = new Thickness(2, 6, 2, 6),
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                };
                toggleButton.Click += OnToggleClicked;
                UpdateToggleVisual();

                buttonPanel = new System.Windows.Controls.Grid();
                buttonPanel.Children.Add(toggleButton);

                var chartTrader = (chartWindow as Chart)?.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                chartTraderGrid = chartTrader?.Content as System.Windows.Controls.Grid;
                if (chartTraderGrid != null)
                {
                    buttonPanel.Margin = new Thickness(2, 6, 2, 6);
                    toggleButtonRow = new System.Windows.Controls.RowDefinition { Height = GridLength.Auto };
                    chartTraderGrid.RowDefinitions.Add(toggleButtonRow);
                    System.Windows.Controls.Grid.SetRow(buttonPanel, chartTraderGrid.RowDefinitions.Count - 1);
                    System.Windows.Controls.Grid.SetColumnSpan(buttonPanel,
                        Math.Max(1, chartTraderGrid.ColumnDefinitions.Count));
                    chartTraderGrid.Children.Add(buttonPanel);
                }
                else
                {
                    Log("ChartTrading: show Chart Trader on this chart, then re-add the indicator, to get the ON/OFF button.",
                        NinjaTrader.Cbi.LogLevel.Warning);
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
                        chartTraderGrid?.Children.Remove(buttonPanel);
                        if (toggleButtonRow != null)
                            chartTraderGrid?.RowDefinitions.Remove(toggleButtonRow);
                        toggleButton = null;
                        buttonPanel = null;
                        toggleButtonRow = null;
                        chartTraderGrid = null;
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
            if (!TradingEnabled)
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
            TradingEnabled = !TradingEnabled;
            UpdateToggleVisual();
            // Clears any armed preview the moment trading is switched off.
            UpdatePreview(Side.None);
        }

        private void UpdateToggleVisual()
        {
            if (toggleButton == null)
                return;

            toggleButton.Content = TradingEnabled ? "ChartTrading ON" : "ChartTrading OFF";
            toggleButton.Background = TradingEnabled ? Brushes.SeaGreen : Brushes.DimGray;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (ChartControl == null || ChartPanel == null)
                return;

            // The presentation source detaches while the chart is torn down (tab
            // close, recompile) with mouse events still queued; converting
            // coordinates through it then throws on the UI thread, where no
            // NinjaScript wrapper catches.
            if (ChartControl.PresentationSource == null)
                return;

            // ChartTrader raises no account-changed event we hook, so a switched
            // account is picked up on the next pointer move over the chart;
            // otherwise auto-breakeven would keep guarding the old account. The
            // switch waits until the old account is flat with no working entry:
            // exits are placed and resized from that account's order updates.
            Account selectedAccount = ChartControl.OwnerChart?.ChartTrader?.Account;
            if (State < State.Terminated && selectedAccount != null && !ReferenceEquals(selectedAccount, subscribedAccount))
            {
                bool switched = false;
                lock (orderLock)
                {
                    if (currentMarketPosition == MarketPosition.Flat && !HasWorkingEntryOn(subscribedAccount))
                    {
                        EnsureAccountSubscription(selectedAccount);
                        switched = true;
                    }
                }
                if (switched)
                    SeedPositionCache(selectedAccount);
            }

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

            UpdatePreview(ShownSide(ResolveSide()));
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
            UpdatePreview(pointerOverPanel ? ShownSide(ResolveSide()) : Side.None);
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
            // Key-up never reaches an unfocused chart, so the armed preview can outlive the modifier.
            Side side = ResolveSide();
            if (ShownSide(side) != previewSide)
                UpdatePreview(ShownSide(side));
            if (side == Side.None || !pointerOverPanel)
                return;

            MasterInstrument master = Instrument?.MasterInstrument;
            ChartScale scale = activeChartScale;
            if (master == null || scale == null || ChartControl == null)
                return;

            var chartTrader = ChartControl.OwnerChart?.ChartTrader;
            Account account = chartTrader?.Account;
            if (account == null)
            {
                Log("ChartTrading: no ChartTrader account selected; order not placed.",
                    NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }
            if (subscribedAccount != null && !ReferenceEquals(account, subscribedAccount))
            {
                bool oldAccountBusy;
                lock (orderLock)
                    oldAccountBusy = currentMarketPosition != MarketPosition.Flat || HasWorkingEntryOn(subscribedAccount);
                if (oldAccountBusy)
                {
                    Log("ChartTrading: " + subscribedAccount.Name + " still has a position or a pending entry; order on " + account.Name + " not placed.",
                        NinjaTrader.Cbi.LogLevel.Warning);
                    return;
                }
            }

            bool isBuy = side == Side.Buy;
            Position livePosition = LivePosition(account);
            bool liveOpposite = livePosition != null && livePosition.MarketPosition == (isBuy ? MarketPosition.Short : MarketPosition.Long);
            if (liveOpposite || OppositeSideOpen(isBuy))
            {
                Log("ChartTrading: close the " + (isBuy ? "short" : "long") + " position first (Close half / Close all); opposite orders are disabled.",
                    NinjaTrader.Cbi.LogLevel.Warning);
                e.Handled = true;
                return;
            }

            int quantity = chartTrader.Quantity > 0 ? chartTrader.Quantity : previewQuantity;
            int profitSign = isBuy ? 1 : -1;
            double tick = master.TickSize;

            double entryPrice;
            string entryLabel;
            OrderType entryType;
            if (!ResolveEntry(isBuy, scale, master, out entryPrice, out entryType, out entryLabel))
                return;
            double limitPrice;
            double stopPrice;
            EntryPriceFields(entryType, isBuy, entryPrice, master, out limitPrice, out stopPrice);

            bool[] pairEnabled = { Bracket1Enabled, Bracket2Enabled, Bracket3Enabled };
            int[] stopTicks;
            int[] targetTicks;
            if (!ResolveBracketTicks(out stopTicks, out targetTicks))
            {
                Log("ChartTrading: ATR distances are on but no ATR is available yet; order not placed.",
                    NinjaTrader.Cbi.LogLevel.Warning);
                return;
            }
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
            // Compacted to the enabled pairs, same order as stops/PairQuantities, so
            // the exit prices can be re-derived from the real fill in EnsureExits.
            var enabledStopTicks = new List<int>();
            var enabledTargetTicks = new List<int>();
            for (int i = 0; i < effectiveEnabled.Length; i++)
            {
                if (!effectiveEnabled[i])
                    continue;
                targets.Add(master.RoundToTickSize(entryPrice + profitSign * targetTicks[i] * tick));
                enabledStopTicks.Add(stopTicks[i]);
                enabledTargetTicks.Add(targetTicks[i]);
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
                StopTicks = enabledStopTicks.ToArray(),
                TargetTicks = enabledTargetTicks.ToArray(),
                ProfitSign = profitSign,
                OpeningAtrStopTicks = (BracketDistance == ChartTradingBracketDistance.Atr
                    && currentMarketPosition == MarketPosition.Flat) ? stopTicks[0] : 0,
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

                try
                {
                    account.Submit(new[] { entry });
                }
                catch (Exception ex)
                {
                    lock (orderLock)
                        activeCommits.Remove(commit);
                    Log("ChartTrading: entry not placed - " + ex.Message, NinjaTrader.Cbi.LogLevel.Error);
                }
            }, null);

            // The click belongs to the commit; the chart must not also act on it.
            e.Handled = true;
        }

        /// <summary>
        /// Orders against the open position or a working entry are refused: one
        /// side at a time keeps every bracket closing the position it was placed
        /// for. Closing goes through Close half / Close all.
        /// </summary>
        private bool OppositeSideOpen(bool isBuy)
        {
            MarketPosition opposite = isBuy ? MarketPosition.Short : MarketPosition.Long;
            lock (orderLock)
            {
                if (currentMarketPosition == opposite)
                    return true;
                foreach (BracketCommit commit in activeCommits)
                {
                    if (CommitSide(commit) == opposite && commit.Entry != null && !Order.IsTerminalState(commit.Entry.OrderState))
                        return true;
                }
            }
            return false;
        }

        private Side ShownSide(Side side)
        {
            return side != Side.None && OppositeSideOpen(side == Side.Buy) ? Side.None : side;
        }

        private bool HasWorkingEntryOn(Account account)
        {
            foreach (BracketCommit commit in activeCommits)
            {
                if (commit.Entry == null || !ReferenceEquals(commit.Account, account))
                    continue;
                OrderState entryState = commit.Entry.OrderState;
                if (entryState != OrderState.Filled && entryState != OrderState.Cancelled && entryState != OrderState.Rejected)
                    return true;
            }
            return false;
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
            currentQuantity = 0;
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
            int quantity = 0;
            MarketPosition marketPosition = MarketPosition.Flat;
            lock (account.Positions)
            {
                foreach (Position position in account.Positions)
                {
                    if (position.Instrument != null && position.Instrument.FullName == Instrument.FullName)
                    {
                        avgPrice = position.AveragePrice;
                        quantity = position.Quantity;
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
                currentQuantity = quantity;
                currentMarketPosition = marketPosition;
                positionCacheGeneration++;
            }
        }

        // Arrives off the UI thread; keeps the auto-breakeven trigger armed with the
        // live position, and re-arms it whenever the position closes or flips.
        // Account events run outside NinjaScript's exception wrapping: anything
        // escaping logs only as an anonymous platform-level "Unhandled exception"
        // naming no culprit, so failures are contained and named here instead.
        private void OnAccountPositionUpdate(object sender, PositionEventArgs e)
        {
            try
            {
                ProcessAccountPositionUpdate(sender, e);
            }
            catch (Exception ex)
            {
                Log("ChartTrading: position update handling failed - " + ex,
                    NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        private void ProcessAccountPositionUpdate(object sender, PositionEventArgs e)
        {
            // A queued event can outlive this instance's teardown; Instrument may
            // already be gone by the time it is delivered.
            if (Instrument == null || e.Position?.Instrument == null
                || e.Position.Instrument.FullName != Instrument.FullName)
                return;

            // A closed position arrives as Operation.Remove with the last direction
            // still on it -- the shipped market-analyzer columns key off Operation for
            // exactly this reason. MarketPosition alone would read as still open.
            bool nowFlat = e.Operation == Operation.Remove || e.MarketPosition == MarketPosition.Flat;
            ApplyPositionUpdate((Account)sender, nowFlat, e.MarketPosition, e.Quantity, e.AveragePrice);
        }

        private void ApplyPositionUpdate(Account account, bool nowFlat, MarketPosition marketPosition, int quantity, double averagePrice)
        {
            List<Order> orphanedExits = new List<Order>();
            List<BracketCommit> slotsToClear = new List<BracketCommit>();
            List<BracketCommit> toGrow = new List<BracketCommit>();
            bool trimNeeded;

            lock (orderLock)
            {
                // A queued event from a superseded account must not contaminate the
                // current account's cache.
                if (!ReferenceEquals(account, subscribedAccount))
                    return;
                positionSeedPending = false;

                if (nowFlat || marketPosition != currentMarketPosition)
                    autoBreakevenState = AutoBreakevenState.Armed;
                bool flipped = currentMarketPosition != MarketPosition.Flat && marketPosition != currentMarketPosition;
                bool settleKept = false;
                foreach (BracketCommit commit in activeCommits)
                {
                    if (!commit.KeptAtFlat)
                        continue;
                    if (!nowFlat && marketPosition == CommitSide(commit))
                        commit.KeptAtFlat = false;
                    else
                        settleKept = true;
                }
                if (nowFlat || flipped)
                    openingAtrStopTicks = 0;
                if (nowFlat || flipped || settleKept)
                    CollectOrphanedExits(nowFlat ? MarketPosition.Flat : marketPosition, orphanedExits, slotsToClear);
                // Only a shrinking or reversed position trims exits: a scale-in fill can
                // reach the exits before its own position update does.
                trimNeeded = !nowFlat && (flipped || quantity < currentQuantity);
                // Every position change restarts the wait before a cut.
                excessSince = DateTime.MinValue;
                currentMarketPosition = nowFlat ? MarketPosition.Flat : marketPosition;
                currentQuantity = nowFlat ? 0 : quantity;
                currentAvgPrice = nowFlat ? 0 : averagePrice;
                positionCacheGeneration++;
                if (nowFlat && activeCommits.Exists(kept => kept.KeptAtFlat))
                    StartFlatCoverageCheck();
                if (!nowFlat)
                {
                    foreach (BracketCommit commit in activeCommits)
                    {
                        if (CommitSide(commit) == currentMarketPosition && commit.CoveredQuantity > 0)
                            toGrow.Add(commit);
                    }
                }
            }

            if (nowFlat)
                SweepLeftoverExits(account, orphanedExits);
            foreach (BracketCommit commit in slotsToClear)
                ClearSlots(commit, account);
            if (trimNeeded)
                ReconcileExits(account);
            foreach (BracketCommit commit in toGrow)
                EnsureExits(commit, account);

            if (orphanedExits.Count > 0)
            {
                account.Cancel(orphanedExits);
                Log("ChartTrading: position on " + Instrument.FullName + " closed or reversed outside its brackets; cancelled "
                    + orphanedExits.Count + " leftover stop/target order(s).", NinjaTrader.Cbi.LogLevel.Information);
            }
        }

        /// <summary>
        /// Exits that no longer close anything: all of them once flat, and after a
        /// reversal the ones on the new position's side, which would add to it
        /// instead of closing it. Commits of finished entries are retired; a
        /// resting entry's commit is kept, its filled lots written off and its
        /// slots queued for clearing so later fills get fresh pairs. Caller holds
        /// orderLock and has not yet moved the position cache.
        /// </summary>
        private void CollectOrphanedExits(MarketPosition newPosition, List<Order> orphanedExits, List<BracketCommit> slotsToClear)
        {
            for (int i = activeCommits.Count - 1; i >= 0; i--)
            {
                BracketCommit commit = activeCommits[i];
                bool exitBuys = commit.ExitAction == OrderAction.Buy || commit.ExitAction == OrderAction.BuyToCover;
                bool exitAddsToPosition = exitBuys ? newPosition == MarketPosition.Long : newPosition == MarketPosition.Short;
                if (newPosition != MarketPosition.Flat && !exitAddsToPosition)
                    continue;

                // A flat right after this commit's fill can be the stale close of the
                // position before it (a stop filling as a new entry fills, their
                // position updates late). The next update decides.
                bool justFilled = DateTime.UtcNow - commit.FillCountedAt < TimeSpan.FromSeconds(1);
                if (newPosition == MarketPosition.Flat && justFilled && !commit.KeptAtFlat)
                {
                    commit.KeptAtFlat = true;
                    continue;
                }

                // A fill not yet counted still needs its bracket, so its commit stays.
                bool entryDone = Order.IsTerminalState(commit.Entry.OrderState) && commit.CoveredQuantity == commit.Entry.Filled;
                if (entryDone)
                {
                    commit.Orphaned = true;
                    activeCommits.RemoveAt(i);
                }
                else
                {
                    commit.OffsetQuantity = commit.CoveredQuantity;
                    slotsToClear.Add(commit);
                }
                if (commit.StopOrders == null)
                    continue;
                for (int p = 0; p < commit.StopOrders.Length; p++)
                {
                    foreach (Order exit in new[] { commit.StopOrders[p], commit.TargetOrders[p] })
                    {
                        if (exit != null && !Order.IsTerminalState(exit.OrderState))
                            orphanedExits.Add(exit);
                    }
                }
            }
        }

        /// <summary>
        /// Empties a kept commit's pair slots as a custom event, serialized with
        /// EnsureExits, which reads them outside orderLock. Anything an EnsureExits
        /// queued earlier placed in the meantime is cancelled with them.
        /// </summary>
        private void ClearSlots(BracketCommit commit, Account account)
        {
            TriggerCustomEvent(o =>
            {
                var leftovers = new List<Order>();
                lock (orderLock)
                {
                    if (commit.StopOrders == null)
                        return;
                    for (int p = 0; p < commit.StopOrders.Length; p++)
                    {
                        foreach (Order exit in new[] { commit.StopOrders[p], commit.TargetOrders[p] })
                        {
                            if (exit != null && !Order.IsTerminalState(exit.OrderState)
                                && exit.OrderState != OrderState.CancelPending && exit.OrderState != OrderState.CancelSubmitted)
                                leftovers.Add(exit);
                        }
                        commit.StopOrders[p] = null;
                        commit.TargetOrders[p] = null;
                        commit.PairAppliedQuantity[p] = 0;
                        commit.RequestedStopQuantity[p] = 0;
                    }
                }
                if (leftovers.Count > 0)
                    account.Cancel(leftovers);
            }, null);
        }

        /// <summary>
        /// Once flat, any CT exit still working closes nothing -- including ones a
        /// previous instance of this indicator placed before a reload.
        /// </summary>
        private void SweepLeftoverExits(Account account, List<Order> orphanedExits)
        {
            lock (account.Orders)
            {
                foreach (Order order in account.Orders)
                {
                    if (order.Instrument == null || order.Instrument.FullName != Instrument.FullName)
                        continue;
                    if (order.Name != "CT Stop" && order.Name != "CT Target")
                        continue;
                    if (!Order.IsTerminalState(order.OrderState) && !orphanedExits.Contains(order))
                        orphanedExits.Add(order);
                }
            }
        }

        private static MarketPosition CommitSide(BracketCommit commit)
        {
            return commit.ProfitSign > 0 ? MarketPosition.Long : MarketPosition.Short;
        }

        private static int UncoveredFill(BracketCommit commit)
        {
            return Math.Max(0, commit.CoveredQuantity - commit.OffsetQuantity);
        }

        private static int OpenQuantity(Order order)
        {
            return order == null || Order.IsTerminalState(order.OrderState) ? 0 : order.Quantity - order.Filled;
        }

        // A pair holds only what both legs still hold: once its target has filled
        // (fully or partly), the OCO takes the stop's matching lots with it.
        private static int PairLiveLots(Order stop, Order target)
        {
            int stopLots = OpenQuantity(stop);
            bool targetStands = target != null && target.OrderState != OrderState.Cancelled && target.OrderState != OrderState.Rejected;
            return targetStands ? Math.Min(stopLots, OpenQuantity(target)) : stopLots;
        }

        private static bool IsSettled(Order order)
        {
            return order == null || Order.IsTerminalState(order.OrderState)
                || order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.PartFilled;
        }

        /// <summary>
        /// The lots this commit's exits may cover. Flat (the fill has not reached the
        /// position yet): all its uncovered fill. Position on the commit's side: its
        /// share of the position, older commits first. Opposite: none yet -- the
        /// position update that follows the fill grows it. Caller holds orderLock.
        /// </summary>
        private int ExitCoverage(BracketCommit commit)
        {
            MarketPosition side = CommitSide(commit);
            if (currentMarketPosition == MarketPosition.Flat)
                return UncoveredFill(commit);
            if (currentMarketPosition != side)
                return 0;

            int room = currentQuantity;
            foreach (BracketCommit candidate in activeCommits)
            {
                if (CommitSide(candidate) != side)
                    continue;
                int coverage = Math.Min(UncoveredFill(candidate), room);
                if (ReferenceEquals(candidate, commit))
                    return coverage;
                room -= coverage;
            }
            return 0;
        }

        /// <summary>
        /// Lots that left a pair (its stop or target filled, or a resize from
        /// outside) leave the plan too, so they are never regrown. Lowering the
        /// pair's share along with the offset keeps PairDesiredQuantity equal to
        /// what every other pair already holds. Caller holds orderLock.
        /// </summary>
        private static void WriteOff(BracketCommit commit, int pairIndex, int lots)
        {
            commit.PairAppliedQuantity[pairIndex] -= lots;
            if (pairIndex < commit.PairQuantities.Length - 1)
                commit.PairQuantities[pairIndex] -= lots;
            commit.OffsetQuantity += lots;
        }

        // A cut the broker refused leaves a pair holding more than the plan says;
        // the plan takes those lots back so the next cut is not counted twice.
        private static void TakeBack(BracketCommit commit, int pairIndex, int lots)
        {
            commit.PairAppliedQuantity[pairIndex] += lots;
            if (pairIndex < commit.PairQuantities.Length - 1)
                commit.PairQuantities[pairIndex] += lots;
            commit.OffsetQuantity = Math.Max(0, commit.OffsetQuantity - lots);
        }

        /// <summary>
        /// After the position shrank or reversed, cuts this tool's exits on the
        /// closing side down to the position, newest commit first, so no stop can
        /// fill past flat. Deferred while any of them is mid-change, since its
        /// quantity is not final yet, and for TrimDelay before any cut. Runs as
        /// a custom event so it never interleaves with EnsureExits on the same
        /// commit.
        /// </summary>
        private void ReconcileExits(Account account)
        {
            TriggerCustomEvent(o =>
            {
                var changes = new List<Order>();
                var cancels = new List<Order>();
                TimeSpan cutWait = TimeSpan.Zero;
                lock (orderLock)
                {
                    reconcilePending = false;
                    MarketPosition side = currentMarketPosition;
                    if (side == MarketPosition.Flat)
                    {
                        excessSince = DateTime.MinValue;
                        return;
                    }
                    var sameSide = new List<BracketCommit>();
                    foreach (BracketCommit commit in activeCommits)
                    {
                        if (CommitSide(commit) == side && commit.StopOrders != null)
                            sameSide.Add(commit);
                    }

                    foreach (BracketCommit commit in sameSide)
                    {
                        for (int p = 0; p < commit.StopOrders.Length; p++)
                        {
                            if (!IsSettled(commit.StopOrders[p]) || !IsSettled(commit.TargetOrders[p]))
                            {
                                reconcilePending = true;
                                return;
                            }
                        }
                    }

                    int liveLots = 0;
                    foreach (BracketCommit commit in sameSide)
                    {
                        for (int p = 0; p < commit.StopOrders.Length; p++)
                        {
                            if (commit.StopOrders[p] == null)
                                continue;
                            int live = PairLiveLots(commit.StopOrders[p], commit.TargetOrders[p]);
                            int gone = commit.PairAppliedQuantity[p] - live;
                            if (gone > 0)
                                WriteOff(commit, p, gone);
                            else if (gone < 0)
                                TakeBack(commit, p, -gone);
                            liveLots += live;
                        }
                    }

                    int excess = liveLots - currentQuantity;
                    if (excess <= 0)
                        excessSince = DateTime.MinValue;
                    else if (excessSince == DateTime.MinValue)
                        excessSince = DateTime.UtcNow;
                    if (excess > 0 && DateTime.UtcNow - excessSince < TrimDelay)
                    {
                        cutWait = TrimDelay - (DateTime.UtcNow - excessSince);
                        excess = 0;
                    }

                    // Every pair is visited, even with nothing to cut, so a stop left
                    // larger than its partly filled target is brought down to it.
                    for (int c = sameSide.Count - 1; c >= 0; c--)
                    {
                        BracketCommit commit = sameSide[c];
                        foreach (bool cutPartlyTakenPairs in new[] { true, false })
                        {
                            for (int p = 0; p < commit.StopOrders.Length; p++)
                            {
                                Order stop = commit.StopOrders[p];
                                Order target = commit.TargetOrders[p];
                                bool partlyTaken = target != null && target.Filled > 0;
                                if (partlyTaken != cutPartlyTakenPairs)
                                    continue;
                                int live = PairLiveLots(stop, target);
                                if (live == 0)
                                    continue;
                                int cut = Math.Min(Math.Max(excess, 0), live);
                                excess -= cut;
                                if (cut > 0)
                                    WriteOff(commit, p, cut);
                                int kept = live - cut;
                                if (kept == 0)
                                {
                                    cancels.Add(stop);
                                    if (OpenQuantity(target) > 0)
                                        cancels.Add(target);
                                    continue;
                                }
                                if (OpenQuantity(stop) > kept)
                                {
                                    stop.QuantityChanged = stop.Filled + kept;
                                    stop.LimitPriceChanged = stop.LimitPrice;
                                    stop.StopPriceChanged = stop.StopPrice;
                                    changes.Add(stop);
                                    commit.RequestedStopQuantity[p] = stop.QuantityChanged;
                                }
                                // A target larger than its stop would cancel the stop on fill.
                                if (OpenQuantity(target) > kept)
                                {
                                    target.QuantityChanged = target.Filled + kept;
                                    target.LimitPriceChanged = target.LimitPrice;
                                    target.StopPriceChanged = target.StopPrice;
                                    changes.Add(target);
                                }
                            }
                        }
                    }
                }

                if (changes.Count > 0)
                    account.Change(changes);
                if (cancels.Count > 0)
                    account.Cancel(cancels);
                if (changes.Count + cancels.Count > 0)
                    Log("ChartTrading: position on " + Instrument.FullName + " shrank outside its brackets; trimmed stops and targets to "
                        + currentQuantity + ".", NinjaTrader.Cbi.LogLevel.Information);
                if (cutWait > TimeSpan.Zero)
                    StartTrimTimer(cutWait, account);
            }, null);
        }

        private void StartTrimTimer(TimeSpan wait, Account account)
        {
            lock (orderLock)
            {
                if (trimTimer == null)
                    trimTimer = new System.Threading.Timer(o => OnTrimTimer((Account)o), account,
                        System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                trimTimer.Change((int)wait.TotalMilliseconds + 20, System.Threading.Timeout.Infinite);
            }
        }

        private void OnTrimTimer(Account account)
        {
            try
            {
                if (ReferenceEquals(account, subscribedAccount))
                    ReconcileExits(account);
            }
            catch (Exception ex)
            {
                Log("ChartTrading: deferred trim failed - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        // Arrives off the UI thread. Exits go live only against filled entry quantity;
        // a resting stop or target without a position would OPEN a trade, not close one.
        // Same containment rationale as position updates: account events run outside
        // NinjaScript's exception wrapping.
        private void OnAccountOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                ProcessAccountOrderUpdate(sender, e);
            }
            catch (Exception ex)
            {
                Log("ChartTrading: order update handling failed - " + ex,
                    NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        private void ProcessAccountOrderUpdate(object sender, OrderEventArgs e)
        {
            if (e.Order == null || Instrument == null)
                return;

            // An auto-breakeven that fired before the exits were live re-arms as soon
            // as a protective stop is accepted, instead of polling every tick. Any
            // stop-type order qualifies, matching the widened Stops-to-BE scan: an
            // externally placed stop coming alive must wake the latch too.
            if ((e.Order.OrderType == OrderType.StopMarket || e.Order.OrderType == OrderType.StopLimit)
                && e.Order.Name != "CT Entry"
                && (e.OrderState == OrderState.Working || e.OrderState == OrderState.Accepted))
            {
                lock (orderLock)
                {
                    if (autoBreakevenState == AutoBreakevenState.WaitingForStops
                        && ReferenceEquals(sender, subscribedAccount))
                        autoBreakevenState = AutoBreakevenState.Armed;
                }
            }

            // A pair order coming alive is the retry point for a grow that was skipped
            // while the order was still in flight: EnsureExits only resizes pairs in
            // Working/Accepted states, and a fast entry fill can outrun the exits' own
            // acceptance. Without this retry the missed quantity was never applied --
            // a 4-lot entry could end up covered by 3 lots of exits for good. The
            // retry fires only when THIS pair's desired quantity exceeds what
            // EnsureExits last staged for it, so a trader's own quantity change
            // never regrows what they shrank on purpose, and no flag needs to be
            // published ahead of the acceptance events.
            bool isPairOrder = e.Order.Name == "CT Stop" || e.Order.Name == "CT Target";
            bool pairOrderAlive = e.OrderState == OrderState.Working || e.OrderState == OrderState.Accepted;
            if (isPairOrder && (pairOrderAlive || (e.Order.Name == "CT Stop" && e.OrderState == OrderState.Rejected)))
                CheckStopOutcome(e.Order, e.OrderState, sender as Account);

            // A pair order ending or filling is a retry point too: a rejected
            // stop's target finishing its cancel lets the pair be placed again,
            // and a fill explains a position shrink a deferred trim waits on.
            if (isPairOrder && (pairOrderAlive || e.OrderState == OrderState.PartFilled || Order.IsTerminalState(e.OrderState)))
            {
                // Any commit, not just this order's: one losing its lots frees
                // position room another commit was held back from.
                var toGrow = new List<BracketCommit>();
                lock (orderLock)
                {
                    foreach (BracketCommit candidate in activeCommits)
                    {
                        if (candidate.StopOrders == null)
                            continue;
                        int coverage = ExitCoverage(candidate);
                        for (int p = 0; p < candidate.StopOrders.Length; p++)
                        {
                            if (candidate.PairDesiredQuantity(p, coverage) > candidate.PairAppliedQuantity[p])
                            {
                                toGrow.Add(candidate);
                                break;
                            }
                        }
                    }
                }
                foreach (BracketCommit candidate in toGrow)
                    EnsureExits(candidate, sender as Account);
                bool retryTrim;
                lock (orderLock)
                {
                    bool exitFilled = e.OrderState == OrderState.PartFilled || e.OrderState == OrderState.Filled;
                    retryTrim = (reconcilePending || excessSince != DateTime.MinValue || exitFilled)
                        && ReferenceEquals(sender, subscribedAccount);
                }
                if (retryTrim)
                    ReconcileExits((Account)sender);
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
                        RecordOpeningAtrStop(commit);
                        StartFlatCoverageCheck();
                        commit.FillCountedAt = DateTime.UtcNow;
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
                    // A commit whose entry partially filled stays registered: its
                    // exits are live and may still need the grow-retry above. Only a
                    // clean zero-fill end drops it.
                    if (e.Order.Filled == 0)
                        activeCommits.Remove(commit);
                    if (e.Order.Filled > commit.CoveredQuantity)
                    {
                        RecordOpeningAtrStop(commit);
                        StartFlatCoverageCheck();
                        commit.FillCountedAt = DateTime.UtcNow;
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
        /// A rejected stop leaves its pair's lots unprotected, a grow the broker
        /// refused comes back alive at the old size, and so does a refused cut.
        /// Each is retried once (the pair placed, grown or cut again); a second
        /// refusal closes the lots the pair should cover at market rather than
        /// leave them without a stop, or a stop larger than the position.
        /// </summary>
        private void CheckStopOutcome(Order leg, OrderState state, Account account)
        {
            if (account == null)
                return;
            TriggerCustomEvent(o => ResolveStopOutcome(leg, state, account), null);
        }

        // Runs as a custom event: it rewrites the pair plan EnsureExits reads.
        private void ResolveStopOutcome(Order leg, OrderState state, Account account)
        {
            BracketCommit owner = null;
            int pairIndex = -1;
            int unprotected = 0;
            int refusedCutKeeps = 0;
            bool retry = false;
            bool stopRequestPending;
            Order stop;
            Order target;
            lock (orderLock)
            {
                foreach (BracketCommit candidate in activeCommits)
                {
                    if (candidate.StopOrders == null)
                        continue;
                    pairIndex = Array.IndexOf(candidate.StopOrders, leg);
                    if (pairIndex < 0)
                        pairIndex = Array.IndexOf(candidate.TargetOrders, leg);
                    if (pairIndex >= 0)
                    {
                        owner = candidate;
                        break;
                    }
                }
                if (owner == null)
                    return;

                stop = owner.StopOrders[pairIndex];
                target = owner.TargetOrders[pairIndex];
                if (ReferenceEquals(leg, stop))
                {
                    int requested = owner.RequestedStopQuantity[pairIndex];
                    if (state == OrderState.Rejected)
                        unprotected = owner.PairAppliedQuantity[pairIndex];
                    else if (requested > 0 && stop.Quantity < requested)
                        unprotected = requested - stop.Quantity;
                    else if (requested > 0 && stop.Quantity > requested)
                        refusedCutKeeps = requested - stop.Filled;
                    owner.RequestedStopQuantity[pairIndex] = 0;

                    if (unprotected > 0 || refusedCutKeeps > 0)
                    {
                        retry = !owner.ExitRetried;
                        owner.ExitRetried = true;
                    }
                    if (unprotected > 0)
                    {
                        if (retry)
                            owner.PairAppliedQuantity[pairIndex] -= unprotected;
                        else
                            WriteOff(owner, pairIndex, unprotected);
                    }
                    else if (refusedCutKeeps > 0 && !retry)
                    {
                        WriteOff(owner, pairIndex, owner.PairAppliedQuantity[pairIndex]);
                        unprotected = refusedCutKeeps;
                    }
                }
                stopRequestPending = owner.RequestedStopQuantity[pairIndex] > 0;
            }

            if (state == OrderState.Rejected && target != null && !Order.IsTerminalState(target.OrderState))
                account.Cancel(new[] { target });

            if (refusedCutKeeps > 0)
            {
                if (retry)
                {
                    Log("ChartTrading: a stop cut on " + Instrument.FullName + " was refused; cutting it again.",
                        NinjaTrader.Cbi.LogLevel.Error);
                    ReconcileExits(account);
                    return;
                }
                var refusedPair = new List<Order>();
                foreach (Order exit in new[] { stop, target })
                {
                    if (exit != null && !Order.IsTerminalState(exit.OrderState))
                        refusedPair.Add(exit);
                }
                if (refusedPair.Count > 0)
                    account.Cancel(refusedPair);
            }
            else if (unprotected == 0)
            {
                // A target left larger than its settled stop (a refused cut) would
                // fill past the position; dropping it keeps the stop's protection.
                if (stop != null && target != null && !stopRequestPending && IsSettled(stop) && IsSettled(target)
                    && !Order.IsTerminalState(stop.OrderState) && !Order.IsTerminalState(target.OrderState)
                    && OpenQuantity(target) > OpenQuantity(stop))
                {
                    account.Cancel(new[] { target });
                    Log("ChartTrading: a target on " + Instrument.FullName + " stayed larger than its stop; cancelled it.",
                        NinjaTrader.Cbi.LogLevel.Error);
                }
                return;
            }
            else if (retry)
            {
                Log("ChartTrading: a stop for " + unprotected + " lot(s) on " + Instrument.FullName + " was refused; placing it again.",
                    NinjaTrader.Cbi.LogLevel.Error);
                EnsureExits(owner, account);
                return;
            }

            // Never more than the position really holds on that side, or the exit would open a trade.
            Position open = LivePosition(account);
            int closable = open != null && open.MarketPosition == CommitSide(owner) ? Math.Min(unprotected, open.Quantity) : 0;
            // A grown target the stop could not follow would cancel the stop on fill.
            if (state != OrderState.Rejected && refusedCutKeeps == 0 && target != null && OpenQuantity(target) > OpenQuantity(stop))
            {
                target.QuantityChanged = target.Filled + OpenQuantity(stop);
                target.LimitPriceChanged = target.LimitPrice;
                target.StopPriceChanged = target.StopPrice;
                account.Change(new[] { target });
            }
            if (closable > 0)
                account.Submit(new[] { account.CreateOrder(Instrument, owner.ExitAction, OrderType.Market, OrderEntry.Manual, TimeInForce.Day,
                    closable, 0, 0, string.Empty, "CT Exit", Core.Globals.MaxDate, null) });
            Log("ChartTrading: a stop on " + Instrument.FullName + " was refused twice; closed " + closable + " lot(s) at market.",
                NinjaTrader.Cbi.LogLevel.Error);
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
                int growthRoom;
                lock (orderLock)
                {
                    if (commit.Orphaned)
                        return;
                    filled = ExitCoverage(commit);

                    // Resolve the exit prices from the actual fill, once, before any
                    // pair is created -- in the same critical section as the arrays
                    // below, so a grow-retry callback can never read prices that are
                    // half-swapped. After this the prices are immutable, matching the
                    // create-once invariant the rest of EnsureExits relies on.
                    if (!commit.PricesResolved)
                    {
                        MasterInstrument exitMaster = Instrument?.MasterInstrument;
                        if (exitMaster != null && commit.EntryFillPrice > 0 && commit.StopTicks != null)
                        {
                            bool[] allEnabled = new bool[commit.StopTicks.Length];
                            for (int k = 0; k < allEnabled.Length; k++)
                                allEnabled[k] = true;
                            commit.StopPrices = BuildStopPrices(exitMaster, commit.EntryFillPrice,
                                commit.ProfitSign, allEnabled, commit.StopTicks).ToArray();
                            var resolvedTargets = new double[commit.TargetTicks.Length];
                            for (int k = 0; k < resolvedTargets.Length; k++)
                                resolvedTargets[k] = exitMaster.RoundToTickSize(
                                    commit.EntryFillPrice + commit.ProfitSign * commit.TargetTicks[k] * exitMaster.TickSize);
                            commit.TargetPrices = resolvedTargets;
                            commit.PricesResolved = true;
                        }
                        else if (commit.CoveredQuantity > 0 && commit.EntryFillPrice <= 0)
                        {
                            // Exits are only ever created after a fill, so a fill with
                            // no average price should be impossible; keep the
                            // click-relative estimate but say so, because it silently
                            // reproduces the bug this resolution fixes.
                            Log("ChartTrading: entry filled without an average fill price; exits placed at their click-relative estimate.",
                                NinjaTrader.Cbi.LogLevel.Warning);
                            commit.PricesResolved = true;
                        }
                    }

                    if (commit.StopOrders == null)
                    {
                        commit.StopOrders = new Order[commit.StopPrices.Length];
                        commit.TargetOrders = new Order[commit.StopPrices.Length];
                        commit.PairAppliedQuantity = new int[commit.StopPrices.Length];
                        commit.RequestedStopQuantity = new int[commit.StopPrices.Length];
                    }

                    // The per-pair plan assumes earlier pairs fill first; after cuts and
                    // retries a later pair can hold lots the plan gives an earlier one.
                    growthRoom = filled;
                    foreach (int appliedLots in commit.PairAppliedQuantity)
                        growthRoom -= appliedLots;
                    // Coverage is shared out oldest commit first, but a newer commit
                    // may already hold lots an older one's late fill now claims.
                    if (currentMarketPosition == CommitSide(commit))
                    {
                        int positionRoom = currentQuantity;
                        foreach (BracketCommit candidate in activeCommits)
                        {
                            if (CommitSide(candidate) != currentMarketPosition || candidate.PairAppliedQuantity == null)
                                continue;
                            foreach (int appliedLots in candidate.PairAppliedQuantity)
                                positionRoom -= appliedLots;
                        }
                        growthRoom = Math.Min(growthRoom, positionRoom);
                    }
                }

                TimeInForce tif = OrderTimeInForce == ChartTradingTimeInForce.Gtc
                    ? TimeInForce.Gtc : TimeInForce.Day;
                var submissions = new List<Order>();
                var changes = new List<Order>();
                for (int i = 0; i < commit.StopPrices.Length; i++)
                {
                    int applied = commit.PairAppliedQuantity[i];
                    int desired = Math.Min(commit.PairDesiredQuantity(i, filled), applied + Math.Max(growthRoom, 0));
                    // Exits only grow here; shrinking is ReconcileExits' job, driven by
                    // the position. Comparing against what was applied, not the live
                    // quantity, leaves a trader's or Close half's own resize alone.
                    if (desired <= applied)
                        continue;
                    growthRoom -= desired - applied;

                    Order stop = commit.StopOrders[i];
                    Order pairTarget = commit.TargetOrders[i];
                    bool pairDead = stop != null && Order.IsTerminalState(stop.OrderState)
                        && (pairTarget == null || Order.IsTerminalState(pairTarget.OrderState));
                    if (stop == null || pairDead)
                    {
                        int lots = desired - applied;
                        // Each pair's stop and target cancel each other.
                        string oco = "CT-" + Guid.NewGuid().ToString("N");
                        stop = account.CreateOrder(Instrument, commit.ExitAction, OrderType.StopMarket,
                            OrderEntry.Manual, tif, lots, 0, commit.StopPrices[i],
                            oco, "CT Stop", Core.Globals.MaxDate, null);
                        Order target = account.CreateOrder(Instrument, commit.ExitAction, OrderType.Limit,
                            OrderEntry.Manual, tif, lots, commit.TargetPrices[i], 0,
                            oco, "CT Target", Core.Globals.MaxDate, null);
                        lock (orderLock)
                        {
                            commit.StopOrders[i] = stop;
                            commit.TargetOrders[i] = target;
                            commit.PairAppliedQuantity[i] = desired;
                        }
                        submissions.Add(stop);
                        submissions.Add(target);
                        continue;
                    }

                    // The pair exists from an earlier partial fill; grow it if it is
                    // still alive. A pair whose orders are mid-flight (submitted, or a
                    // change in progress) cannot take a Change yet -- its
                    // PairAppliedQuantity keeps the old value, and the retry hook
                    // re-runs when the pair's orders come alive and sees desired >
                    // applied.
                    bool stopAlive = stop.OrderState == OrderState.Working || stop.OrderState == OrderState.Accepted;
                    bool targetAlive = pairTarget != null
                        && (pairTarget.OrderState == OrderState.Working || pairTarget.OrderState == OrderState.Accepted);
                    // A target dropped for outgrowing its stop leaves a stop-only pair, which still grows.
                    bool targetDropped = pairTarget == null
                        || pairTarget.OrderState == OrderState.Cancelled || pairTarget.OrderState == OrderState.Rejected;
                    if (stopAlive && (targetAlive || targetDropped))
                    {
                        int grownLots = OpenQuantity(stop) + (desired - applied);
                        stop.QuantityChanged = stop.Filled + grownLots;
                        stop.LimitPriceChanged = stop.LimitPrice;
                        stop.StopPriceChanged = stop.StopPrice;
                        changes.Add(stop);
                        // Sized from the stop, so a target a refused grow left larger is evened out.
                        if (targetAlive)
                        {
                            pairTarget.QuantityChanged = pairTarget.Filled + grownLots;
                            pairTarget.LimitPriceChanged = pairTarget.LimitPrice;
                            pairTarget.StopPriceChanged = pairTarget.StopPrice;
                            changes.Add(pairTarget);
                        }
                        lock (orderLock)
                        {
                            commit.PairAppliedQuantity[i] = desired;
                            commit.RequestedStopQuantity[i] = stop.QuantityChanged;
                        }
                    }
                }

                if (submissions.Count > 0)
                    account.Submit(submissions);
                if (changes.Count > 0)
                    account.Change(changes);

                // The position may have closed while this ran; exits placed for it
                // would then close nothing.
                var leftovers = new List<Order>();
                lock (orderLock)
                {
                    if (commit.Orphaned)
                    {
                        for (int i = 0; i < commit.StopOrders.Length; i++)
                        {
                            foreach (Order exit in new[] { commit.StopOrders[i], commit.TargetOrders[i] })
                            {
                                if (exit != null && !Order.IsTerminalState(exit.OrderState))
                                    leftovers.Add(exit);
                            }
                        }
                    }
                }
                if (leftovers.Count > 0)
                    account.Cancel(leftovers);
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
        /// The price and order type a click would enter with. Market mode prices the
        /// entry at the ask for a buy and the bid for a sell, falling back to the
        /// last price before quotes arrive; false when there is no price at all.
        /// </summary>
        private bool ResolveEntry(bool isBuy, ChartScale scale, MasterInstrument master,
            out double entryPrice, out OrderType entryType, out string entryLabel)
        {
            if (EntryPriceMode == ChartTradingEntryPrice.Mouse)
            {
                entryPrice = master.RoundToTickSize(scale.GetValueByY(pointerDeviceY));
                entryType = InferEntryType(isBuy, entryPrice, out entryLabel);
                return true;
            }

            entryType = OrderType.Market;
            entryLabel = "MKT";
            entryPrice = isBuy ? GetCurrentAsk() : GetCurrentBid();
            if (entryPrice <= 0 && Bars != null && Bars.Count > 0)
                entryPrice = Bars.GetClose(Bars.Count - 1);
            entryPrice = master.RoundToTickSize(entryPrice);
            return entryPrice > 0;
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
        /// Watches live ticks for the auto-breakeven trigger: once price has run the
        /// configured distance in the position's favor, move every protective stop to
        /// breakeven, once per position.
        /// </summary>
        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate.MarketDataType != MarketDataType.Last)
                return;
            if (!AutoBreakevenEnabled)
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
            int triggerTicks = Math.Max(AutoBreakevenTriggerTickCount(), BreakevenOffsetTicks + 1);
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

            MoveStopsToBreakeven(account, price, generation);
        }

        private void ReArmAutoBreakeven()
        {
            lock (orderLock)
                autoBreakevenState = AutoBreakevenState.Armed;
        }

        private void MoveStopsToBreakeven(Account account, double last, int expectedGeneration)
        {
            TriggerCustomEvent(o =>
            {
                try
                {
                    MoveStopsToBreakevenNow(account, last, expectedGeneration);
                }
                catch (Exception ex)
                {
                    ReArmAutoBreakeven();
                    Log("ChartTrading: auto breakeven failed - " + ex.Message, NinjaTrader.Cbi.LogLevel.Error);
                }
            }, null);
        }

        private void MoveStopsToBreakevenNow(Account account, double last, int expectedGeneration)
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

            MasterInstrument master = Instrument?.MasterInstrument;
            if (master == null)
            {
                ReArmAutoBreakeven();
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
                ReArmAutoBreakeven();
                Log("ChartTrading: no open position on " + Instrument.FullName + "; stops not moved.",
                    NinjaTrader.Cbi.LogLevel.Information);
                return;
            }

            // Clamped so the stop never crosses the market -- the same guard as a manual move: a long's stop stays at or
            // below the last price, a short's at or above it.
            bool isLong = position.MarketPosition == MarketPosition.Long;
            double target = position.AveragePrice
                + (isLong ? 1 : -1) * BreakevenOffsetTicks * master.TickSize;
            // Several fills average between ticks; round toward profit so the stop never locks a loss.
            double breakeven = isLong
                ? Math.Ceiling(target / master.TickSize - 1e-9) * master.TickSize
                : Math.Floor(target / master.TickSize + 1e-9) * master.TickSize;
            if (last > 0)
            {
                // One tick inside the market, not at it: a stop resting exactly at
                // the last price gets rejected or fills instantly.
                breakeven = isLong
                    ? Math.Min(breakeven, last - master.TickSize)
                    : Math.Max(breakeven, last + master.TickSize);
            }
            breakeven = master.RoundToTickSize(breakeven);

            // Every protective stop on the instrument moves, not only this tool's:
            // any working stop-type order on the position's EXIT side is protection,
            // whatever placed it. Same-side stop orders stay untouched -- those are
            // stop entries (a buy stop above the market on a long, for instance),
            // and yanking an entry to breakeven would fire it.
            OrderAction longExit1 = OrderAction.Sell;
            OrderAction shortExit1 = OrderAction.Buy;
            OrderAction shortExit2 = OrderAction.BuyToCover;
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
                    // This tool's own stop-side entries are exits by action but
                    // entries by intent; while short, a resting CT buy-stop entry
                    // would otherwise match the exit side and get yanked to BE.
                    if (order.Name == "CT Entry" || order.Name == "Entry")
                        continue;
                    bool exitSide = isLong
                        ? order.OrderAction == longExit1
                        : order.OrderAction == shortExit1 || order.OrderAction == shortExit2;
                    if (!exitSide)
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

                // Stage every changeable field. A stop-limit's limit price shifts
                // by the same distance as its stop, keeping the configured
                // trigger-to-limit offset -- leaving the limit behind would make
                // the order unfillable once triggered.
                order.QuantityChanged = order.Quantity;
                order.LimitPriceChanged = order.OrderType == OrderType.StopLimit
                    ? master.RoundToTickSize(order.LimitPrice + (breakeven - order.StopPrice))
                    : order.LimitPrice;
                order.StopPriceChanged = breakeven;
                changes.Add(order);
            }

            if (changes.Count == 0)
            {
                // Stops already at or beyond BE count as done; no stops at all means
                // the exits are not live yet -- wait for one to be accepted instead
                // of burning the latch (or polling every tick).
                lock (orderLock)
                    autoBreakevenState = alreadySafer > 0
                        ? AutoBreakevenState.Fired
                        : AutoBreakevenState.WaitingForStops;
                Log(alreadySafer > 0
                        ? $"ChartTrading: all {alreadySafer} stop(s) on {Instrument.FullName} already at or beyond breakeven; nothing moved (auto)."
                        : $"ChartTrading: no working protective stops on {Instrument.FullName} to move (auto).",
                    NinjaTrader.Cbi.LogLevel.Information);
                return;
            }

            account.Change(changes);
            lock (orderLock)
                autoBreakevenState = AutoBreakevenState.Fired;
            string skippedNote = alreadySafer > 0 ? $", {alreadySafer} already safer left alone" : string.Empty;
            Log($"ChartTrading: moved {changes.Count} stop(s) to breakeven {master.FormatPrice(breakeven)}{skippedNote} (auto).",
                NinjaTrader.Cbi.LogLevel.Information);
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

            double tick = master.TickSize;
            bool isBuy = previewSide == Side.Buy;

            // Buy: stop below entry, targets above. Sell: mirror. sign is +1 in the
            // direction the trade profits.
            int profitSign = isBuy ? 1 : -1;

            string enter = isBuy ? "Buy" : "Sell";
            string exit = isBuy ? "Sell" : "Buy";

            // The label previews exactly the order type a click would submit.
            double entryPrice;
            string entryType;
            OrderType ignoredType;
            if (!ResolveEntry(isBuy, chartScale, master, out entryPrice, out ignoredType, out entryType))
                return;

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
                int[] stopTicks;
                int[] targetTicks;
                if (!ResolveBracketTicks(out stopTicks, out targetTicks))
                    return;
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
                bool hasPriceTag = priceLabel.Length > 0;
                float priceBoxW = hasPriceTag ? priceLayout.Metrics.Width + 2f * padX : 0f;

                // The price tag hugs the right edge, its point aimed at the level.
                float priceBoxX = panelRight - priceBoxW;
                float priceTipX = hasPriceTag ? priceBoxX - tip : panelRight;

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

                if (!hasPriceTag)
                    return;

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
        /// What the right-side tag reads on a stop or target: each enabled value of
        /// the level price, the signed distance from entry in ticks and points, and
        /// the signed money value for the level's quantity. Empty with every value
        /// off, which drops the price tag.
        /// </summary>
        private string LevelValueLabel(MasterInstrument master, double levelPrice, int signedTicks, int quantity)
        {
            IFormatProvider culture = Core.Globals.GeneralOptions.CurrentCulture;
            double signedPoints = signedTicks * master.TickSize;
            List<string> parts = new List<string>();
            if (ShowLevelPrice)
                parts.Add(master.FormatPrice(levelPrice));
            if (ShowLevelTicks)
                parts.Add(string.Format(culture, "{0:+0;-0}t", signedTicks));
            if (ShowLevelPoints)
                parts.Add(string.Format(culture, "{0:+0.00##;-0.00##}pt", signedPoints));
            if (ShowLevelMoney)
                parts.Add(string.Format(culture, "{0:+$#,##0.00;-$#,##0.00}", signedPoints * master.PointValue * quantity));
            return string.Join("  ", parts);
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

        // Same ATR as the position sizer: true range smoothed with k = 2/(n+1)
        // (exponential) or 1/n (Wilder), seeded with the running mean of the
        // first AtrLength true ranges, live bar included.
        protected override void OnBarUpdate()
        {
            double barRange = High[0] - Low[0];
            double trueRange = CurrentBar == 0
                ? barRange
                : Math.Max(barRange, Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));

            if (CurrentBar == 0)
                atrSeries[0] = trueRange;
            else if (CurrentBar < AtrLength)
                atrSeries[0] = (atrSeries[1] * CurrentBar + trueRange) / (CurrentBar + 1);
            else
            {
                double k = AtrSmoothing == ChartTradingAtrSmoothing.Exponential
                    ? 2.0 / (AtrLength + 1)
                    : 1.0 / AtrLength;
                atrSeries[0] = k * trueRange + (1 - k) * atrSeries[1];
            }
            currentAtr = atrSeries[0];
        }

        // The account's own position on this instrument, not the event-fed cache; null when flat.
        private Position LivePosition(Account account)
        {
            lock (account.Positions)
            {
                foreach (Position position in account.Positions)
                {
                    if (position.Instrument != null && position.Instrument.FullName == Instrument.FullName
                        && position.MarketPosition != MarketPosition.Flat && position.Quantity > 0)
                        return position;
                }
            }
            return null;
        }

        /// <summary>
        /// A second after a fill was counted against a flat cache, every event for
        /// it has landed: re-read the account's position, apply it and trim to it.
        /// That cancels exits placed for a fill that only flattened the position,
        /// whichever order its fill and position updates arrived in.
        /// </summary>
        private void CheckFlatCoverage()
        {
            Account account;
            lock (orderLock)
            {
                if (flatCoverageSince == DateTime.MinValue || DateTime.UtcNow - flatCoverageSince < TimeSpan.FromSeconds(1))
                    return;
                flatCoverageSince = DateTime.MinValue;
                account = subscribedAccount;
            }
            if (account == null)
                return;

            Position open = LivePosition(account);
            if (open == null)
            {
                ApplyPositionUpdate(account, true, MarketPosition.Flat, 0, 0);
                return;
            }
            ApplyPositionUpdate(account, false, open.MarketPosition, open.Quantity, open.AveragePrice);
            ReconcileExits(account);
        }

        // Called under orderLock as a fill is counted. Applying the live position
        // right away is unsafe: an older position update may still be queued.
        private void StartFlatCoverageCheck()
        {
            if (currentMarketPosition != MarketPosition.Flat || flatCoverageSince != DateTime.MinValue)
                return;
            flatCoverageSince = DateTime.UtcNow;
            if (flatCoverageTimer == null)
                flatCoverageTimer = new System.Threading.Timer(o => OnFlatCoverageTimer(), null,
                    System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            flatCoverageTimer.Change(1100, System.Threading.Timeout.Infinite);
        }

        private void OnFlatCoverageTimer()
        {
            try
            {
                CheckFlatCoverage();
            }
            catch (Exception ex)
            {
                Log("ChartTrading: flat coverage check failed - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        // Called under orderLock on an entry's first covered fill, whichever order
        // state reports it.
        private void RecordOpeningAtrStop(BracketCommit commit)
        {
            if (commit.CoveredQuantity == 0 && commit.OpeningAtrStopTicks > 0 && openingAtrStopTicks == 0)
                openingAtrStopTicks = commit.OpeningAtrStopTicks;
        }

        // With ATR distances the trigger is an R multiple of the stop distance:
        // the ATR stop of the click that opened the position, or the current
        // ATR stop for a position opened some other way. Tick distances keep the fixed tick trigger.
        private int AutoBreakevenTriggerTickCount()
        {
            if (BracketDistance != ChartTradingBracketDistance.Atr)
                return AutoBreakevenTriggerTicks;

            int stopTicks = openingAtrStopTicks;
            if (stopTicks <= 0)
            {
                int[] currentStopTicks;
                int[] unusedTargetTicks;
                if (!ResolveBracketTicks(out currentStopTicks, out unusedTargetTicks))
                    return AutoBreakevenTriggerTicks;
                stopTicks = currentStopTicks[0];
            }
            return Math.Max(1, (int)Math.Round(stopTicks * AutoBreakevenTriggerR, MidpointRounding.AwayFromZero));
        }

        // False when ATR distances are on but no ATR exists yet: the caller must
        // not fall back to the tick distances the trader did not choose.
        private bool ResolveBracketTicks(out int[] stopTicks, out int[] targetTicks)
        {
            stopTicks = new[] { Stop1Ticks, Stop2Ticks, Stop3Ticks };
            targetTicks = new[] { Target1Ticks, Target2Ticks, Target3Ticks };
            if (BracketDistance != ChartTradingBracketDistance.Atr)
                return true;
            double atr = currentAtr;
            if (atr <= 0)
                return false;

            int atrStopTicks = Math.Max(1, (int)Math.Round(atr * StopAtrMultiple / Instrument.MasterInstrument.TickSize, MidpointRounding.AwayFromZero));
            double[] targetR = { Target1R, Target2R, Target3R };
            for (int i = 0; i < 3; i++)
            {
                stopTicks[i] = atrStopTicks;
                targetTicks[i] = Math.Max(1, (int)Math.Round(atrStopTicks * targetR[i], MidpointRounding.AwayFromZero));
            }
            return true;
        }
    }

    public class ChartTradingTypeConverter : IndicatorBaseConverter
    {
        private static readonly string[] TickDistanceProperties =
        {
            "Stop1Ticks", "Target1Ticks", "Stop2Ticks", "Target2Ticks", "Stop3Ticks", "Target3Ticks",
            "AutoBreakevenTriggerTicks",
        };

        private static readonly string[] AtrDistanceProperties =
        {
            "StopAtrMultiple", "AtrLength", "AtrSmoothing", "Target1R", "Target2R", "Target3R",
            "AutoBreakevenTriggerR",
        };

        private static readonly string[] MouseEntryProperties =
        {
            "LimitSideType", "StopSideType", "StopLimitOffsetTicks",
        };

        public override System.ComponentModel.PropertyDescriptorCollection GetProperties(System.ComponentModel.ITypeDescriptorContext context, object component, Attribute[] attributes)
        {
            ChartTrading indicator = component as ChartTrading;

            System.ComponentModel.PropertyDescriptorCollection properties = GetPropertiesSupported(context)
                ? base.GetProperties(context, component, attributes)
                : System.ComponentModel.TypeDescriptor.GetProperties(component, attributes);

            if (indicator == null || properties == null)
                return properties;

            List<string> hidden = new List<string>(indicator.BracketDistance == ChartTradingBracketDistance.Atr
                ? TickDistanceProperties
                : AtrDistanceProperties);
            if (indicator.EntryPriceMode == ChartTradingEntryPrice.Market)
                hidden.AddRange(MouseEntryProperties);
            foreach (string name in hidden)
            {
                System.ComponentModel.PropertyDescriptor descriptor = properties[name];
                if (descriptor != null)
                    properties.Remove(descriptor);
            }
            return properties;
        }

        public override bool GetPropertiesSupported(System.ComponentModel.ITypeDescriptorContext context)
        {
            return true;
        }
    }
}
