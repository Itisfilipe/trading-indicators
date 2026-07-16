# ChartTrading for NinjaTrader 8 — staged implementation plan

Date: 2026-07-16  
Research target: NinjaTrader 8.1.x; the worked reference was exported by NT 8.1.6.2.

## Current status

Built and working on Sim (owner-tested): bracket preview with native-marker
styling, order submission gated by the sidebar ON/OFF button, exits-on-fill
OCO pairs, configurable entry types (M3), per-pair bracket checkboxes,
stacked-stop separation, the Stops-to-BE button, and auto-breakeven with a
shared breakeven offset. Option A won (indicator owns the bracket): ATM
template offsets are not readable before StartAtmStrategy, so an exact preview
requires owning the numbers. M7 (grid/DCA) is dropped — this tool mirrors
Profit Chart's simple click trading. Still open: axis-strip price marker
experiment, TIF from ChartTrader (hardcoded Day), partial-fill hardening,
first MIT fill unverified in Playback.

The plan below predates these decisions; where they conflict, the status
above and the code win.

## Feasibility verdict

Yes: the core product can be built as an NT8 Indicator. A chart Indicator can observe chart mouse input, convert the pointer Y coordinate through ChartScale, draw an OnRender preview, create account-level orders, submit direct orders, and start a selected ATM strategy around an entry order.

There are three important boundaries:

1. Direct orders should use Account.CreateOrder(...) followed by Account.Submit(...). If a ChartTrader ATM is selected, create the entry Order and pass it to NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(selectedAtm, entryOrder); do not also call Account.Submit. AtmStrategyCreate(...) is the Strategy-instance API demonstrated by NinjaTrader's shipped sample, not the right Indicator/AddOn route.
2. Account, instrument, and quantity have observed ChartTrader properties, but TIF and the full selected ATM object require inspecting ChartTrader's WPF controls. NinjaTrader staff repeatedly describe these ChartTrader internals and Automation IDs as unsupported/subject to change. Therefore the trading mechanism is feasible, but matching all live ChartTrader selections is version-sensitive.
3. A normal native StopLimit with non-negative offset is feasible. Reproducing NinjaTrader's UI convention in which a negative offset creates a locally simulated stop is not available through a documented, supported Account.CreateOrder path: it uses the undocumented CustomOrder object or a locally managed synthetic trigger. Negative offsets should be blocked in the supported release and considered only as an explicitly experimental follow-up.

There is also a semantic conflict in the requested behavior: if all N grid entries share one OCO ID, the first fill or cancellation cancels the other N-1 entries. That is a one-of-N alternative-entry ladder, not an accumulating DCA ladder. The design should expose two explicit modes:

- OcoOneOfN: every generated entry shares one fresh OCO ID.
- IndependentDca: every entry has an empty OCO ID and can accumulate.

Never imply that OcoOneOfN will accumulate fills.

## Audit of ABCompleteChartTrader

The reference is valuable, but the brief overstates what it reads from ChartTrader. It reads the account once when enabled, reads quantity and ATM at submission, hard-codes TimeInForce.Day, submits the Indicator's Instrument, and compares the click with Close[0]. It does not dynamically use ChartTrader's instrument or TIF.

### Reuse

- The State.Historical -> ChartControl.Dispatcher attach pattern.
- Window.GetWindow(ChartControl.Parent), ChartWindow/ChartTrader discovery, and tab-selection awareness.
- The device-pixel conversion followed by ChartScale.GetValueByY and MasterInstrument.RoundToTickSize.
- ChartPanel mouse/key event concepts.
- Account.OrderUpdate and Account.PositionUpdate subscriptions.
- Caching the current ChartScale in OnRender for a later UI-thread pointer event.

### Deliberately diverge

- Read an atomic, fresh ChartTrader snapshot for every preview refresh and again at commit. Do not cache an account selected minutes earlier.
- Use ChartTrader's selected Instrument, Quantity, TIF/GTD, and selected ATM object. Reject if the selected instrument does not match the chart scale's instrument.
- Use selected-instrument market data, not Close[0], for the live comparison.
- Pass zero for irrelevant limit/stop fields.
- Use Core.Globals.MaxDate except for a real GTD selection.
- For ATM, call StartAtmStrategy only. AB calls StartAtmStrategy and then Account.Submit on the same Order; current staff guidance says the ATM call replaces Submit.
- Do not inspect atmSelector.Template to decide whether an ATM exists. That misses the Custom selection. Use AtmStrategySelector.SelectedAtmStrategy; null means None.
- Consume a mouse event only after an exact valid gesture has passed all preflight checks and is being committed.
- Reset gesture state on mouse-up, mouse-leave, lost capture, window deactivation, tab deselection, disconnect, and teardown.
- Use idempotent attach/detach flags and a captured owner Dispatcher so termination can detach even if ChartControl is already null.
- Do not index chartTraderGrid.Children[0], and do not require injecting a row into ChartTrader for the first release.
- Never use CancelAllOrders or cancel account-wide orders. Track and cancel only Order objects created by this Indicator instance.

Relevant reference locations are ABCompleteChartTrader.cs lines 159-181, 423-451, 570-631, 688-815, 843-989, 1012-1089, and 1097-1101.

## Architecture

### 1. ChartTradingIndicator shell

- IsOverlay = true and IsChartOnly = true.
- In State.Historical, capture ChartControl, ChartPanel, chart window, and Dispatcher; schedule an idempotent AttachUi.
- In State.Terminated, invalidate a generation token and schedule idempotent DetachUi on the captured Dispatcher.
- Track whether this chart tab is selected. An unselected tab is always disarmed.
- OnRender does rendering only and stores the latest scale reference for the Indicator's panel.

Follow the defensive lifecycle used by ErgonomicCharts: captured owner, attached flag, partial-attach rollback, exception logging, and cleanup on deactivation/lost capture. Do not copy its global/synthetic key mechanism.

### 2. ChartTraderAdapter

On the chart Dispatcher, return an immutable snapshot containing:

- Account from ChartControl.OwnerChart.ChartTrader.Account.
- Instrument from ChartControl.OwnerChart.ChartTrader.Instrument.
- Quantity from ChartControl.OwnerChart.ChartTrader.Quantity.
- TIF and GTD date from the TifSelector with Automation ID ChartTraderControlTIFSelector.
- ATM object from AtmStrategySelector.SelectedAtmStrategy with Automation ID ChartTraderControlATMStrategySelector.
- A timestamp and the account/instrument identity used for stale-snapshot checks.

Do not carry a last-known-good value across a missing control. If ChartTrader is hidden and the controls or a complete snapshot are unavailable, show “ChartTrader unavailable” and fail closed. An optional future fallback may expose independent account/instrument/TIF properties, but it would no longer satisfy “use live ChartTrader selections” and must require separate arming.

At commit, require the ChartTrader instrument to equal the charted instrument whose scale produced the price. A linked ChartTrader selecting another instrument makes the Y-to-price mapping meaningless and must be rejected.

### 3. MarketSnapshot

- Use the selected Instrument's live Last price as the behavior reference. NinjaTrader's shipped LastPrice column demonstrates Instrument.MarketData.Last.Price.
- Also maintain bid, ask, update timestamp, and connection state for validation. Use OnMarketData when the selected instrument is the Indicator instrument; if arbitrary selected instruments are ever allowed, use Instrument.MarketData.Update on that instrument's Dispatcher and unsubscribe when it changes.
- Reject missing, NaN, zero/invalid, disconnected, or stale market data.
- Do not substitute the bar's Close[0].

### 4. GestureController

- Defaults: Control + left = buy; Control + Alt + left = sell.
- Configurable required modifiers are limited to Control, Alt, and Shift; configurable button is left or middle.
- Compare the complete supported-modifier mask for exact equality, so Control+Alt does not accidentally fire a Control-only buy.
- Observe local WPF PreviewKeyDown/PreviewKeyUp and PreviewMouseDown/PreviewMouseUp/MouseMove on the active chart panel/window. Read Keyboard.Modifiers; do not install a global keyboard hook.
- Key handlers update preview state but do not set e.Handled.
- A mouse-down is consumed only after exactly one configured side matches, the tool is armed, preflight succeeds, and a commit token is acquired.
- A per-button down/up latch plus a short monotonic sequence token prevents duplicate submission from bubbling, key repeat, or re-entrant callbacks.

### 5. PriceMapper and OrderPlanBuilder

These are pure, deterministic functions and should be unit-tested outside NT:

- Pointer WPF Y -> device-pixel Y -> panel containment check -> ChartScale.GetValueByY -> selected instrument tick rounding.
- Compare each generated order price with a fresh Last snapshot.
- Buy below Last / sell above Last is “better”; buy above Last / sell below Last is “worse.”
- Equal-to-Last is ambiguous and should reject rather than silently choose a stop.
- BetterType may be Limit or MIT. WorseType may be StopMarket or StopLimit.
- Fields:
  - Limit: limitPrice = entry price, stopPrice = 0.
  - MIT: limitPrice = 0, stopPrice = entry price.
  - StopMarket: limitPrice = 0, stopPrice = entry price.
  - StopLimit: stopPrice = entry price; limitPrice = stop + sideSign * offsetTicks * tickSize, where sideSign is +1 for buy and -1 for sell.
- Round every generated price through the selected instrument's MasterInstrument.
- For the supported path, require offsetTicks >= 0. Do not silently translate a negative offset into another behavior.
- Validate stop-side legality against a fresh bid/ask immediately before execution; reject rather than clamp or change the requested order type.

### 6. OrderExecutor

The executor accepts only an immutable, preflighted plan and an unchanged ChartTrader snapshot:

- Direct route: create every Order with Account.CreateOrder and submit the collection once with Account.Submit.
- ATM route: create each entry with name Entry and call static AtmStrategy.StartAtmStrategy(selectedAtm, order). Do not call Account.Submit afterward.
- Every ATM entry starts a separate ATM instance. It cannot scale an existing active ATM. Start with N = 1 for ATM mode; enable multi-entry ATM only after Playback/Sim proves the OCO, partial-fill, and child-order behavior.
- SubmitOrderUnmanaged belongs to a NinjaScript Strategy and is not used here.
- AtmStrategyCreate is likewise not the Indicator path; NinjaTrader's shipped sample demonstrates it as a Strategy instance method with generated ATM/order IDs.

Because the entry is submitted at Account level rather than owned by a running NinjaScript Strategy, the Indicator has no Strategy SystemPerformance, managed-order rules, or automatic cancellation on termination. Subscribe to Account.OrderUpdate/ExecutionUpdate as needed, retain the actual Order references, and own lifecycle/safety explicitly.

### 7. PreviewModel and PreviewRenderer

- Gesture/UI events build an immutable array of preview primitives: side, price, quantity, inferred type, OCO mode, validity, and warning text.
- OnRender reads only that immutable array. It must not dereference WPF controls, query ChartTrader, or submit orders.
- Convert price back with chartScale.GetYByValue and draw SharpDX lines/labels inside ChartPanel.X/Y/W/H.
- Create/dispose device-dependent brushes in OnRenderTargetChanged, following NinjaTrader's shipped rendering samples.
- Preview is advisory. Commit always recaptures ChartTrader/market state and rebuilds the plan; if it changed, reject or submit the newly displayed/confirmed plan, never a stale hidden plan.
- Use ForceRefresh on key state changes and a throttled refresh while the pointer moves. Avoid raw ChartControl.InvalidateVisual unless a target-version test proves it necessary.

### 8. SafetyController and OwnedOrderRegistry

- Default account policy: Sim accounts only.
- Two-stage live enable: AllowLiveTrading property plus an on-chart/ChartTrader arm action for the current account and instrument.
- Disarm on disconnect/reconnect, account change, instrument change, ChartTrader becoming unavailable, chart tab deselection, window deactivation, reload, or validation failure.
- Limits: max order count, max quantity per order, max aggregate quantity, max price distance, and checked arithmetic.
- One commit token per physical mouse-down.
- Register created Order objects before routing; observe state, ErrorCode, and native error messages.
- Kill switch first disarms. A separate explicit “cancel ChartTrading orders” action may cancel only still-working objects in this instance's registry.
- On Indicator removal/reload, default to leave already submitted orders working and report them. An optional “cancel owned working orders on termination” policy must be explicit and must never broaden to account-wide cancellation.
- Write an audit line for account, instrument, side, type, quantity, prices, TIF, OCO ID, ATM/direct route, source gesture, timestamp, and rejection reason.

## Exact submission decisions

### Direct versus ATM

The route is selected from the fresh ATM selector value:

- SelectedAtmStrategy == null: direct Account.CreateOrder + Account.Submit.
- SelectedAtmStrategy != null: Account.CreateOrder with name Entry + StartAtmStrategy(selectedAtm, order), with no Account.Submit.

This preserves a Custom ATM selection because it passes the selected AtmStrategy object instead of relying on its Template string. Staff examples show this exact Indicator/AddOn shape. Staff also state that each call starts a new ATM and that there is no known supported way to scale into an existing active ATM through this mechanism.

### Thread ownership

- All ChartTrader/WPF reads, event subscription changes, and gesture handling occur on the captured chart Dispatcher.
- Account creation/submission is serialized from the commit handler after the immutable snapshot is captured. The first implementation should keep this path on the UI callback, matching working Indicator/AddOn examples, and keep it short.
- If TriggerCustomEvent is used to move execution into NinjaScript serialization, pass only immutable values into it and never dereference WPF there. Recheck the generation token before execution. It is not a substitute for the UI Dispatcher.
- Account and market-data callbacks must assume they are not on the WPF thread. Update thread-safe primitive state, then Dispatcher.BeginInvoke only for UI state.
- Asynchronous attach callbacks must check State < State.Terminated and the generation token before mutating UI.

## Key code shapes

These snippets show the load-bearing API calls. CtSnapshot, PriceFields, PlannedEntry, and validation helpers are project-owned types, not NinjaTrader APIs.

### 1. Fresh ChartTrader snapshot on the UI Dispatcher

    private CtSnapshot ReadChartTraderSnapshot()
    {
        if (!ChartControl.Dispatcher.CheckAccess())
            throw new InvalidOperationException("ChartTrader must be read on its Dispatcher.");

        var chartWindow = Window.GetWindow(ChartControl.Parent);
        var chartTrader = ChartControl.OwnerChart == null
            ? null
            : ChartControl.OwnerChart.ChartTrader;

        var tifSelector = chartWindow.FindFirst("ChartTraderControlTIFSelector")
            as NinjaTrader.Gui.Tools.TifSelector;
        var atmSelector = chartWindow.FindFirst("ChartTraderControlATMStrategySelector")
            as NinjaTrader.Gui.NinjaScript.AtmStrategy.AtmStrategySelector;

        if (chartTrader == null || chartTrader.Account == null ||
            chartTrader.Instrument == null || chartTrader.Quantity <= 0 ||
            tifSelector == null || atmSelector == null)
            throw new InvalidOperationException("Complete ChartTrader selection is unavailable.");

        TimeInForce tif = tifSelector.SelectedTif;
        DateTime gtd = tif == TimeInForce.Gtd
            ? tifSelector.GtdDate
            : Core.Globals.MaxDate;

        return new CtSnapshot(
            chartTrader.Account,
            chartTrader.Instrument,
            chartTrader.Quantity,
            tif,
            gtd,
            atmSelector.SelectedAtmStrategy);
    }

Evidence: AB lines 428-434 and 576-620 demonstrate OwnerChart.ChartTrader account/quantity/ATM access. NinjaTrader staff demonstrate Instrument, TifSelector.SelectedTif/GtdDate, the ATM selector Automation ID/type, and SelectedAtmStrategy. This is Tier 2 observed WPF/API usage, not a supported stable ChartTrader contract.

### 2. Click Y to rounded price, then price fields

    Point pointer = e.GetPosition(ChartControl as IInputElement);
    int y = ChartingExtensions.ConvertToVerticalPixels(
        pointer.Y, ChartControl.PresentationSource);

    if (activeChartScale == null ||
        y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
        return; // not this Indicator panel

    double clickPrice = selectedInstrument.MasterInstrument.RoundToTickSize(
        activeChartScale.GetValueByY(y));

    if (clickPrice == lastPrice)
        throw new InvalidOperationException("Click equals Last; order type is ambiguous.");

    bool better = action == OrderAction.Buy
        ? clickPrice < lastPrice
        : clickPrice > lastPrice;
    OrderType type = better ? configuredBetterType : configuredWorseType;

    double limitPrice = 0;
    double stopPrice = 0;

    switch (type)
    {
        case OrderType.Limit:
            limitPrice = clickPrice;
            break;
        case OrderType.MIT:
        case OrderType.StopMarket:
            stopPrice = clickPrice;
            break;
        case OrderType.StopLimit:
            stopPrice = clickPrice;
            double sideSign = action == OrderAction.Buy ? 1.0 : -1.0;
            limitPrice = selectedInstrument.MasterInstrument.RoundToTickSize(
                stopPrice + sideSign * stopLimitOffsetTicks * selectedInstrument.MasterInstrument.TickSize);
            break;
        default:
            throw new InvalidOperationException("Unsupported click order type.");
    }

Evidence: GetValueByY/GetYByValue and RoundToTickSize have Tier 1 shipped-source usage. The pointer conversion is present in AB lines 791-797. The MIT stop-field mapping is Tier 3 documentation and must receive an explicit Playback/Sim test.

### 3. Direct Account order and shared OCO submission

    Order order = account.CreateOrder(
        instrument,
        action,
        type,
        OrderEntry.Manual,
        tif,
        quantity,
        limitPrice,
        stopPrice,
        ocoId,
        "ChartTrading",
        gtd,
        null);

    account.Submit(new[] { order });

For a multi-order direct plan, create all Order objects first, register them, and call account.Submit(orders) once. The current 12-argument CreateOrder signature is corroborated by the NT 8.1.6.2 AB export and the current Account docs. Account.Submit accepts IEnumerable<Order>. This is Tier 2 plus Tier 3; no shipped @ sample found uses Account.CreateOrder.

### 4. Deterministic grid generation

    string ocoId = groupMode == GroupMode.OcoOneOfN
        ? string.Format("CLK-{0}-{1}", instanceId, Guid.NewGuid().ToString("N"))
        : string.Empty;

    int quantity = chartTraderQuantity;
    int gapTicks = initialGapTicks;
    double price = firstPrice;
    int priceDirection = action == OrderAction.Buy ? -1 : 1;

    for (int i = 0; i < orderCount; i++)
    {
        entries.Add(new PlannedEntry(price, quantity, ocoId));
        if (i == orderCount - 1)
            break;

        quantity = PositiveIntChecked(
            quantityMultiplier * quantity + quantityAddend);
        price = instrument.MasterInstrument.RoundToTickSize(
            price + priceDirection * gapTicks * instrument.MasterInstrument.TickSize);
        gapTicks = PositiveIntChecked(
            distanceMultiplier * gapTicks + distanceAddend);
    }

    private static int PositiveIntChecked(decimal value)
    {
        int result = checked((int)Math.Round(value, 0, MidpointRounding.AwayFromZero));
        if (result <= 0)
            throw new ArgumentOutOfRangeException("growth", "Generated value must be positive.");
        return result;
    }

The first gap is between entries 0 and 1. Each later integer is produced by next = A * current + B and rounded away from zero. This rounding rule is our specification; Volaty's public page does not specify fractional rounding. The buy-down/sell-up direction is also our default and must remain configurable because the product page does not define grid direction. Reclassify every generated price independently against the same fresh market snapshot before execution.

### 5. ATM route from an Indicator/AddOn

    Order entryOrder = account.CreateOrder(
        instrument,
        action,
        type,
        OrderEntry.Manual,
        tif,
        quantity,
        limitPrice,
        stopPrice,
        ocoId,
        "Entry",
        gtd,
        null);

    NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(
        selectedAtmStrategy,
        entryOrder);

    // Intentionally no account.Submit(...) here.

The Entry name is required. The AtmStrategy object overload preserves the selected Custom configuration. This exact static route and the “no Submit” rule come from Tier 2 staff replies and are consistent with the current StartAtmStrategy reference. The shipped @SampleAtmStrategy proves only the different Strategy-instance AtmStrategyCreate shape.

### 6. Render immutable previews

    protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
    {
        base.OnRender(chartControl, chartScale);
        activeChartScale = chartScale;

        PreviewRow[] rows = previewRows;
        if (rows == null || previewBrushDx == null || RenderTarget == null)
            return;

        float left = ChartPanel.X;
        float right = ChartPanel.X + ChartPanel.W;

        foreach (PreviewRow row in rows)
        {
            float y = chartScale.GetYByValue(row.Price);
            if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
                continue;

            RenderTarget.DrawLine(
                new SharpDX.Vector2(left, y),
                new SharpDX.Vector2(right, y),
                previewBrushDx,
                1.5f);
        }
    }

Create and dispose previewBrushDx in OnRenderTargetChanged. Add cached TextFormat/TextLayout labels only after the line preview is stable. This follows Tier 1 shipped rendering idioms.

## Staged build plan

Every milestone starts in Playback or Sim101. No milestone may silently broaden to live accounts.

### M1 — chart click-to-price readout

Build:

- Minimal overlay Indicator with defensive attach/detach and tab awareness.
- Track the current ChartScale in OnRender.
- On an unmodified diagnostic mouse event, print/display WPF Y, device Y, panel index, raw GetValueByY value, and tick-rounded value.
- Reject clicks outside this Indicator's ChartPanel.

Evidence:

- Tier 1: @DrawingToolTile for State.Historical UI attachment; @SampleCustomRender and drawing tools for device pixels and ChartScale; @HeikenAshiBarsType for RoundToTickSize.
- Tier 2: AB lines 791-797 for the complete click-to-price path.

Exit test:

- On 100%, 125%, and 150% Windows scaling, normal/inverted/log scales where supported, resized panels, and a multi-panel chart, the reported price matches the chart crosshair and stays on tick.
- Repeated add/remove, tab switching, and workspace close produce no duplicate handler or disposed-object exceptions.

Risk: cached ChartScale may be null or stale before first render; fail closed and request a refresh.

### M2 — one direct Limit order using live ChartTrader selections

Build:

- ChartTraderAdapter atomic snapshot.
- Fail-closed hidden/unavailable ChartTrader behavior.
- Read account, instrument, quantity, TIF/GTD, and ATM; for this milestone require ATM None.
- Require ChartTrader instrument == charted instrument.
- Create one Limit order with OrderEntry.Manual and submit through Account.Submit.
- Subscribe to Account.OrderUpdate and retain the returned Order object.

Evidence:

- Tier 2: AB's 12-argument Account.CreateOrder and forum staff's ChartTrader property/selector examples.
- Tier 3: current CreateOrder, Submit, TifSelector, and Order references.

Exit test:

- Change account, quantity, TIF, GTD date, and instrument immediately before clicking; the order uses the current values.
- With ChartTrader hidden, disconnected, incomplete, ATM selected, or instrument mismatched, no order is created.
- Verify Day, GTC, and a valid GTD in Sim/Playback.

Risk: ChartTrader properties and Automation IDs are observed, not a supported stable public contract. Put all such access behind one adapter and emit a clear compatibility error.

### M3 — inferred order types

Build:

- Fresh Last/bid/ask snapshot and stale-data validation.
- Buy/sell better/worse classification.
- Limit, MIT, StopMarket, and StopLimit price-field mapping.
- Non-negative stop-limit offset and stop-side validation.
- Equal-to-Last rejection.

Evidence:

- Tier 1: @LastPrice for Instrument.MarketData.Last.Price; @BarTimer for connection-state handling.
- Tier 2: AB's Buy/SellShort and above/below concept, corrected to use selected-instrument market data.
- Tier 3: CreateOrder field definitions, Price Selector MIT stop field, and order-type semantics.

Exit test:

- Playback matrix: buy/sell × above/below × all configured better/worse types.
- Orders tab shows expected Type, Limit, and Stop columns.
- Rapid market crossing between preview and click yields a clean reject/rebuild, not an invalid or silently changed order.

Risk: provider-specific native stop-limit constraints. Test each intended broker connection separately; keep negative offsets blocked.

### M4 — configurable modifier/mouse gesture

Build:

- Exact modifier-mask matching and configurable left/middle mouse button.
- Defaults from the product brief.
- Local preview key observation, commit-on-mouse-down latch, and all reset paths.
- Do not mark keys handled; mark the valid committing mouse event handled.

Evidence:

- Tier 2: AB's chart-panel handlers and ErgonomicCharts' reviewed lifecycle discipline.
- Tier 3: standard WPF Keyboard.Modifiers and routed input semantics.

Exit test:

- Normal click, drag, chart selection, drawing tools, scroll, and context menus behave normally when the gesture does not exactly match.
- Holding extra modifiers does not trigger a weaker gesture.
- Each physical click creates zero or one commit under key repeat, lost focus, drag-off-panel, and double-click.

Risk: routed-event order differs with active drawing tools. Test PreviewMouseDown versus MouseDown and consume only at the last safe commit point.

### M5 — live preview

Build:

- Immutable PreviewModel generated while a configured modifier set is active.
- One line/label per proposed order showing side, type, price, quantity, ATM/direct, and total quantity.
- Distinct invalid/warning appearance.
- OnRenderTargetChanged resource management and throttled ForceRefresh.

Evidence:

- Tier 1: @SampleCustomRender, @PriceLine, and @Text rendering patterns.
- Tier 2: reviewed RenkoWicks house pattern.

Exit test:

- No RenderTarget resource errors through DPI/theme/workspace changes.
- Preview clears on every disarm/reset path.
- Commit recaptures inputs and never uses stale preview data.

Risk: querying WPF or building text layouts per frame will cause latency. Keep rendering to immutable primitives and cache device/text resources.

### M6 — direct OCO groups

Build:

- Generate one fresh never-reused OCO ID for each OcoOneOfN click.
- Put the same string on every Order before one Account.Submit collection call.
- Provide IndependentDca with empty OCO strings.
- Track partial fills, fills, cancels, and rejections for each owned Order.

Evidence:

- Tier 2: NinjaTrader staff confirm same-ID account orders form a group and require a new ID after any group fill/cancel.
- Tier 3: Order.Oco, Account.CreateOrder, and Basic Entry OCO behavior.

Exit test:

- With two direct Sim orders, a fill cancels the peer and a manual cancellation cancels the peer as the connection defines.
- A later click has a different OCO ID and is accepted.
- IndependentDca entries do not cancel one another.

Risk: OCO behavior can be native/server-side or simulated by the platform/adapter. Disconnect tests are mandatory and results must be documented per connection.

### M7 — grid/DCA generator

Build:

- Pure generator for order count, initial gap, quantity A/B, and distance A/B.
- Explicit integer rounding, checked arithmetic, tick rounding, configurable direction, and aggregate limits.
- Classify each generated entry and preview the complete plan.
- Apply either one shared OCO ID or no OCO by explicit group mode.

Evidence:

- Product requirement only: Volaty's public page states the A * current + B growth model and that the initial distance is between the first two orders.
- NT API evidence is unchanged from M2/M3/M6.

Exit test:

- Table-driven unit cases for constant, additive, multiplicative, and mixed sequences; fractional results; zero/negative results; overflow; large N; and prices that cross Last.
- Sim submission exactly matches the preview and total-risk summary.

Risk: Volaty does not publicly specify rounding or grid direction. The stated rules are this product's own deterministic contract, not a claim of exact Volaty parity.

### M8 — selected ATM routing

Build:

- Use AtmStrategySelector.SelectedAtmStrategy; null means direct.
- Create name Entry and call StartAtmStrategy(selectedAtm, order) without Account.Submit.
- First support one entry only.
- Test saved template and Custom selections.
- Only after passing tests, experiment with N separate ATM entries and OCO entry IDs; clearly show that these are separate ATM instances.

Evidence:

- Tier 1: @SampleAtmStrategy shows the Strategy-only AtmStrategyCreate signature and asynchronous nature.
- Tier 2: staff Indicator/AddOn StartAtmStrategy examples, Entry-name requirement, selected-object route, no-Submit rule, and no supported scale-into-existing-ATM behavior.
- Tier 3: StartAtmStrategy reference.

Exit test:

- A single Sim entry reaches Working/Filled and its selected ATM stop/target children appear once.
- No duplicate entry submission occurs.
- None, named template, and Custom route correctly.
- Multi-entry remains disabled unless child-order and OCO behavior is proven across partial fill/cancel cases.

Risk: ATM creation is asynchronous; entry rejection and child creation may lag. Do not report success at method return. Observe Account events.

### M9 — production safety and compatibility gate

Build:

- Sim-only default, live allow-list, arm/disarm UI, limits, staleness/connection checks, duplicate gate, audit log, owned-order registry, and explicit kill/cancel actions.
- Compatibility probe for all ChartTrader controls on attach and after workspace/layout changes.
- Account/instrument/ATM/TIF changes immediately disarm and require re-arming.
- Error banner and diagnostic snapshot with no secrets.
- Broker/connection certification matrix.

Evidence:

- Tier 1: @BarTimer connection lifecycle and shipped UI teardown patterns.
- Tier 2: support warnings that ChartTrader controls/properties are internal and may be null/disconnected.

Exit test:

- Disconnect/reconnect, provider loss, ChartTrader hide/show, account/instrument switch, tab switch, template reload, compile reload, workspace close, and Indicator removal.
- Rejection, partial fill, race at market crossing, rapid clicks, and rendering/device recreation.
- Live mode cannot be reached accidentally from a newly added or reloaded Indicator.

Risk: account-level orders outlive the Indicator. The UI must state this plainly, and teardown must never claim that working orders were cancelled unless confirmed by OrderUpdate.

## Evidence registry

Tier numbers below follow the brief. Local shipped source was inspected in full/relevant sections and outranks documentation.

### Tier 1 — NinjaTrader shipped source

- @DrawingToolTile.cs: State.Historical plus ChartControl.Dispatcher WPF attachment. GitHub mirror: [@DrawingToolTile](https://github.com/beckerben/NinjaTrader/blob/97c3da9837da6b06c6a8153a95c22408519ad109/Indicators/%40DrawingToolTile.cs).
- @SampleCustomRender.cs and @PriceLine.cs: ChartPanel device coordinates, GetYByValue, RenderTarget drawing, and resource lifetime. GitHub mirrors: [@SampleCustomRender](https://github.com/beckerben/NinjaTrader/blob/97c3da9837da6b06c6a8153a95c22408519ad109/Indicators/%40SampleCustomRender.cs) and [@PriceLine](https://github.com/beckerben/NinjaTrader/blob/97c3da9837da6b06c6a8153a95c22408519ad109/Indicators/%40PriceLine.cs).
- @Text.cs and @FibonacciTools.cs: GetValueByY/GetYByValue usage in shipped drawing tools.
- @HeikenAshiBarsType.cs: repeated Instrument.MasterInstrument.RoundToTickSize(...) usage.
- @LastPrice.cs: Instrument.MarketData.Last.Price snapshot.
- @BarTimer.cs: OnConnectionStatusUpdate and connection-state lifecycle.
- @SampleAtmStrategy.cs: exact Strategy-instance call shape AtmStrategyCreate(OrderAction, OrderType, double limitPrice, double stopPrice, TimeInForce, string orderId, string templateName, string atmStrategyId, callback). GitHub mirror: [@SampleAtmStrategy](https://github.com/beckerben/NinjaTrader/blob/97c3da9837da6b06c6a8153a95c22408519ad109/Strategies/%40SampleAtmStrategy.cs).

The inspected local files are under /Users/filipe/Documents/NinjaTrader 8/bin/Custom/. No file there was modified.

### Tier 2 — worked code and NinjaTrader staff/forum evidence

- Local NT 8.1.6.2 worked reference: [ABCompleteChartTrader.cs](./abchart/ABCompleteChartTrader.cs).
- [Indicator/AddOn order plus ATM route; StartAtmStrategy replaces Submit](https://forum.ninjatrader.com/forum/ninjatrader-8/add-on-development/1314190-charttrader-button-new-order-and-attach-to-an-indicator).
- [Single-click Indicator feasibility and Account.CreateOrder/ATM route](https://forum.ninjatrader.com/forum/ninjatrader-8/platform-technical-support-aa/1115883-single-click-order-entry-on-chart).
- [ATM entry Order must be named Entry](https://forum.ninjatrader.com/forum/ninjatrader-8/indicator-development/1197896-when-submitting-order-with-startatmstrategy-the-order-is-stuck-at-initialized-st).
- [Exact selected ATM object and StartAtmStrategy shape](https://forum.ninjatrader.com/forum/historical-beta-archive/version-8-beta/90509-is-it-possible-to-enter-a-position-using-an-indicator-and-not-a-strategy).
- [ChartTrader quantity and ATM property path](https://forum.ninjatrader.com/forum/ninjatrader-8/indicator-development/1132389-having-an-issue-extracting-atm-strategy-name-from-chart-trader).
- [Null ATM meaning None and unsupported ChartTrader internals warning](https://forum.ninjatrader.com/forum/ninjatrader-8/indicator-development/1257942-how-to-test-when-atm-is-none).
- [ATM selector Automation ID and SelectedAtmStrategy type](https://forum.ninjatrader.com/forum/ninjatrader-8/indicator-development/1142258-how-do-i-set-the-atmstrategyselector-to-none).
- [No supported scale-in to an existing ATM](https://forum.ninjatrader.com/forum/ninjatrader-8/indicator-development/1256654-how-to-support-scaling-in-and-out-when-using-createorder-with-startatmstrategy).
- [ChartTrader Instrument property](https://forum.ninjatrader.com/forum/ninjatrader-8/indicator-development/1285482-access-instrument-selector).
- [ChartTrader properties are undocumented/unsupported](https://forum.ninjatrader.com/forum/ninjatrader-8/add-on-development/106260-add-a-button-to-chart-trader/page2).
- [TIF selector SelectedTif/GtdDate and UI Dispatcher](https://forum.ninjatrader.com/forum/ninjatrader-7/indicator-development-aa/1133427-how-to-obtain-time-in-force-setting-from-chart-trader).
- [Fresh ChartTrader control snapshot at action time](https://forum.ninjatrader.com/forum/ninjatrader-8/add-on-development/1286173-triggercustomevent-pointer-alignment).
- [Fresh OCO IDs and same-ID group semantics](https://forum.ninjatrader.com/forum/ninjatrader-8/platform-technical-support-aa/1161644-please-use-a-new-oco-id).
- [Negative/simulated stop requires unsupported CustomOrder](https://forum.ninjatrader.com/forum/ninjatrader-8/indicator-development/1125950-having-trouble-creating-stopmarket-and-stoplimit-orders).
- [MIT order behavior](https://forum.ninjatrader.com/forum/ninjatrader-8/strategy-development/1036240-unmanaged-mit-buy-sell-orders-not-working-as-expected).

### Tier 3 — documentation, used only where higher tiers are absent

- [Account.CreateOrder current 12-argument signature](https://ninjatrader.com/support/helpguides/nt8/createorder.htm).
- [Price Selector order fields; MIT uses Stop](https://ninjatrader.com/support/helpguides/nt8/price_selector.htm).
- [Order types](https://ninjatrader.com/support/helpguides/nt8/order_types.htm).
- [Basic Entry OCO/ATM behavior](https://ninjatrader.com/support/helpguides/nt8/submitting_orders_basic_entry.htm).
- [TIF selector behavior](https://ninjatrader.com/support/helpguides/nt8/tif_selector.htm).
- [AtmStrategySelector](https://developer.ninjatrader.com/docs/desktop/atmstrategyselector).

Known documentation hazards:

- The current CreateOrder page has the 12-argument signature, while some Submit and StartAtmStrategy examples still show a shorter historical overload.
- AB uses DateTime.MaxValue; current CreateOrder guidance and staff examples use Core.Globals.MaxDate.
- The RoundToTickSize documentation's prose about rounding direction should not be relied on; shipped code establishes use of the function, not that prose.
- Older forum answers sometimes describe AtmStrategyCreate before clarifying the static StartAtmStrategy AddOn route. The newer staff answer and exact Indicator/AddOn examples control this plan.

## Remaining risks and test-only questions

1. ChartTrader internals: OwnerChart.ChartTrader properties and Automation IDs can change between NT releases. Compile and run the compatibility probe on every target build; fail closed.
2. Hidden ChartTrader: do not assume hidden controls exist. The first release requires a complete ChartTrader snapshot even if the object happens to remain in the visual tree.
3. MIT field: official UI/order-grid references say MIT uses StopPrice, but no Tier 1 Account.CreateOrder MIT sample was found. Confirm in Playback/Sim before enabling MIT.
4. Negative stop-limit offset: standard broker-native inverted limit/stop relationships may work on some adapters, but NinjaTrader's simulated-stop convention is undocumented CustomOrder behavior. Keep disabled unless a broker-specific experimental mode is deliberately accepted.
5. Grid direction and fractional rounding: public product material does not specify them. This plan defines its own transparent behavior and tests it.
6. OCO transport: native versus platform-simulated behavior and disconnect guarantees vary by connection. Certify per provider.
7. Multi-entry ATM: each entry creates a separate ATM. OCO across those entry orders and child-order outcomes need Playback, Sim, partial-fill, rejection, and disconnect tests before release.
8. Market race: price can cross Last between preview and commit. The commit must use one fresh snapshot, validate all entries, and either submit that exact plan or reject.
9. Indicator ownership: removing/reloading the Indicator does not inherently cancel account orders. Registry loss after a platform restart means it cannot safely rediscover and cancel “its” orders solely by name; fail conservative.
10. WPF event interaction: drawing tools and chart gestures can change routed-event behavior. Regression-test supported tool combinations and only handle exact successful commits.

## Clean-room/legal/design note

This project should be a clean-room functional reimplementation based on public behavior descriptions, verified NinjaTrader APIs, the owner's ABCompleteChartTrader reference, and independently written code. Do not copy or decompile Volaty source, UI assets, text, branding, product name, screenshots, or distinctive visual design. Keep research notes and original tests. Functionality and source-code expression are treated differently in intellectual-property law, but the exact boundary is jurisdiction-specific; obtain legal advice before commercial distribution if branding, patents, license terms, or contractual access are concerns.

Public behavior source: [Volaty Clicker product page](https://www.volaty.com/products/indicators/clicker/).
