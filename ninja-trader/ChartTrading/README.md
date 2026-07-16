# ChartTrading — a click-to-trade indicator for NinjaTrader 8

A clean-room reimplementation of the behavior described on Volaty's Clicker page,
built on verified NinjaTrader 8 APIs. Not started yet; this folder holds the design.

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
- **Next:** M2 order submission (entry + stop + targets via `Account.CreateOrder` with a
  shared OCO id, quantity from ChartTrader), then M3 order-type inference, M6 OCO groups,
  M7 grid/DCA. See `IMPLEMENTATION_PLAN.md`.
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
