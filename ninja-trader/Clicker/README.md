# Clicker — a click-to-trade indicator for NinjaTrader 8

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

**Decision needed before building the preview milestone.** Option A gives the faithful
Profit-Chart experience; Option B keeps ATM features at the cost of an approximate preview.

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
