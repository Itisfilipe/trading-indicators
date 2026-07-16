# ChartTrading — a click-to-trade indicator for NinjaTrader 8

A clean-room reimplementation of the behavior described on Volaty's Clicker page,
built on verified NinjaTrader 8 APIs.

## The one feature that matters most

Hold a modifier (Shift = buy, Alt = sell) and, **while holding, see a live on-chart
preview of the whole bracket a click would place** — entry line at the mouse price,
plus the stop and profit target(s) — so you can visualize placement before committing.
A click commits the order. This is how the "Profit Chart" NT8 tool behaves.

## The load-bearing constraint (verified 2026-07-16)

**An ATM strategy template's stop and target offsets cannot be read from NinjaScript
before the strategy is started.** Every `GetAtmStrategy*` reader operates on a
*still-active* strategy (post-`StartAtmStrategy`); there is no API to inspect a
selected template's bracket geometry ahead of time. See `getatmstrategystoptargetorderstatus.md`
and the forum thread cited in the plan.

Consequence for the preview: we cannot draw the *actual selected ATM's* stop/targets
before the click, because those numbers are locked inside the template. This forces a
design choice between two brackets:

### Option A — the indicator owns the bracket (recommended for an exact preview)
The indicator places entry + stop + N targets itself via `Account.CreateOrder` with a
shared OCO id, using stop/target offsets configured in *this indicator's* settings.
Because the indicator owns the numbers, the preview is exact and matches what gets
submitted, to the tick. Trade-off: NinjaTrader's ATM auto-management (breakeven steps,
trailing, auto-move-to-BE) is not used and would have to be reimplemented if wanted.
This is the natural fit for a Profit-Chart-style "see exactly where everything lands."

### Option B — route through the ChartTrader-selected ATM on submit
The real bracket comes from the selected ATM via `StartAtmStrategy`, so you keep ATM
auto-management. But the preview can only show offsets the user separately re-enters
into this indicator to *mirror* their ATM — preview and reality diverge if they drift
apart. The preview is advisory, not guaranteed.

**Decision: Option A.** The indicator owns the bracket, for a faithful, exact preview.

## Status

- **M1 — live bracket preview (built, untested).** `Indicators/ChartTrading.cs`. Hold the buy
  modifier (default Shift) or sell modifier (default Alt) and move over the chart: it
  draws the entry line at the pointer, the stop, and up to three targets at the tick
  offsets in the indicator settings. Buy puts the stop below and targets above; sell
  mirrors. Places NO orders and touches no account or network — safe to run live while
  you tune the feel. Not yet compiled or run in NinjaTrader.
  - **Fixed:** the preview repaints on every mouse move while armed, so it tracks the
    pointer in real time (it previously only repainted when the buy/sell side changed).
  - **Built — order-marker styling.** Each level now renders like a working order:
    a dashed line (per-level `Stroke` settings for color/width/dash) with a left-edge
    tag reading like the order itself — entry `1 Buy LMT 14924.25` (LMT vs STP inferred
    from the pointer being below or above the last traded price), stop `1 Sell STP ...`,
    targets `1 Sell LMT ... (T1)`. The quantity is read live from ChartTrader (falls
    back to the last known value). Remaining polish: a price tag on the right axis like
    the platform's own markers; the price currently lives in the left tag instead.
  - **Built — native marker geometry.** Levels now render the way the platform draws a
    working order: a chevron tag (`1 Buy LMT`, pointed tip), a thin solid line running
    from the tag's point to the right edge only (no full-width line), and a pointed
    price tag hugging the right edge aimed at the level. Tag placement is configurable —
    Left / Center / Right with a pixel margin off the border ("Appearance" settings) —
    and tag text picks black or white automatically for contrast against the level color.
  - **Built — level value display.** The right-side tag on the stop and targets can show
    the price, the signed tick distance from entry (`+20t` / `-20t`), or the signed money
    value for the current quantity (`+$100.00` / `-$200.00`, from ticks x tick value x
    quantity). "Level value" in the Appearance settings; the entry always shows its price.
  - **Built — enable/disable button in the ChartTrader sidebar.** A "ChartTrading ON/OFF"
    toggle mounts as a new row in the ChartTrader panel (the ABCompleteChartTrader
    mounting pattern); when ChartTrader is unavailable it falls back to floating at the
    chart's top-left via UserControlCollection. Off releases the modifier keys for other
    tools and clears any armed preview instantly. Note ChartTrader is one panel per chart
    window, so multiple tabs each carrying the indicator each add their own button row.
  - **Built — bracket pairs with per-pair checkboxes.** Each of three stop/target pairs
    has its own "Bracket N" checkbox; a click places every enabled pair, each with its
    own stop and target. The ChartTrader quantity sizes EACH enabled pair, so the entry
    trades quantity x enabled pairs (three enabled 1-lot targets = a 3-lot entry, stops
    covering the same 3 lots). With no pair enabled, a click is a plain entry of the
    ChartTrader quantity. Pairs landing on the same price merge into one marker with the
    summed quantity. Defaults: tags centered, brackets 1 and 2 enabled at 50-tick steps
    (50/50 and 50/100), bracket 3 off (pre-filled 50/150). Money values are per level.
  - **Perf:** modifier keys now hook the chart window (panel hooks needed keyboard focus,
    so hold/release only registered on the next mouse move), and repaints call
    `InvalidateVisual` in addition to `ForceRefresh`, which alone waits for the next
    scheduled render pass and read as lag on a quiet chart.
- **M2 — order submission (built, untested).** The ChartTrading button is the single
  gate: **green ON, a click while the preview is armed places the order**; gray OFF,
  keys and clicks do nothing.
  - The click submits the entry (limit, stop-market, or market, exactly as the preview
    labels it) to the ChartTrader account, sized ChartTrader quantity x enabled pairs.
  - **Exits go live only after the entry fills** (watched via `Account.OrderUpdate`):
    each enabled pair then submits its stop + target as an OCO group sized to that
    pair. A resting exit can never open a position.
  - **Allow live accounts** (default off, the one setting): orders are refused unless
    the account name starts with `Sim`/`Playback`. Flip it deliberately for live.
  - v1 caveats: TIF is Day; exits wait for the FULL entry fill, so an entry cancelled
    after a partial fill leaves that partial unbracketed (logged as a warning);
    removing the indicator leaves working orders working, by design; rejected exits
    are not retried. Watch the Orders tab while testing.
  - **Sim test script:** Sim101 selected in ChartTrader, button green, Shift+click
    below market -> `Buy LMT` entry at the click price; when it fills, each enabled
    pair's stop and target appear, OCO-linked per pair (fill a target, its stop
    cancels — the other pairs stay). Alt+click above market mirrors it short.
- **Next:** M7 grid/DCA entry, the breakeven features below, and the axis-strip price
  marker experiment. See `IMPLEMENTATION_PLAN.md`.
- **Requested, later — position management (owner, while testing M1):**
  - **Auto-breakeven:** once price moves a configurable number of ticks in favor, move the
    stop to the entry price (with an optional offset in ticks).
  - **"Move orders to breakeven" button:** a manual action that moves the working stop(s)
    to breakeven on click.

  These are the ATM auto-management we gave up by choosing Option A, so the indicator has
  to do them itself: track the live position/orders (Account order & position events) and
  issue `Order`/`Account.Change` on the stop. The automation runs locally, so it only acts
  while NinjaTrader is open — same limitation as an ATM. The `ABCompleteChartTrader`
  reference already implements breakeven buttons (`btnAutoStopBreakeven`,
  `btnAutoLimitBreakeven`, and the `BreakevenPosition*` states), so it is the worked
  example to learn both the button and the stop-move mechanics from.

### Testing M1 in NinjaTrader
Import `ChartTrading.cs` (NinjaScript Editor → compile, or bundle with `Info.xml`), add the
ChartTrading indicator to a chart, hold Shift and move the mouse: a blue entry line, a red
stop, and green target lines should track the pointer. Try 100%/125%/150% Windows
display scaling — the lines must sit exactly under the crosshair on tick. Release the key
or leave the panel and the preview clears.

## Verified mechanics (from ABCompleteChartTrader + shipped NT source)

- Read live ChartTrader: `ChartControl.OwnerChart.ChartTrader.{Account, Instrument, Quantity, AtmStrategy}`.
- Click Y to price (DPI-safe): `ChartingExtensions.ConvertToVerticalPixels(...)` then
  `ChartScale.GetValueByY(y)`, then `MasterInstrument.RoundToTickSize(...)`.
- Order type from click vs market: buy below / sell above = Limit; the other side = Stop-Market.
- Submit: `Account.CreateOrder(...)` + `Account.Submit(...)`; for ATM, `AtmStrategy.StartAtmStrategy(selectedAtm, entryOrder)` and no Submit.
- Draw preview lines back with `ChartScale.GetYByValue(price)` in `OnRender`.

## Files

- `IMPLEMENTATION_PLAN.md` — the full staged plan (M1–M9), architecture, code shapes,
  and a tiered evidence registry. Note: it predates the priority above, so it treats the
  preview as milestone M5 rather than the centerpiece, and its preview draws entry lines
  only, not the stop/target bracket. Re-order around Option A/B once that decision is made.
