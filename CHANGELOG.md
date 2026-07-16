# Changelog

Notable changes to the indicators in this repository. Dates follow ISO 8601
(YYYY-MM-DD); entries describe what changed on the chart, not internals.

## 2026-07-16

### MultiSeriesEMA (NinjaTrader, new)
- Overlays an EMA computed on a bar series independent of the chart it's on
  (different Renko brick size, higher timeframe, tick/range bars) without
  ever drawing that series' bars or opening a second panel.
- "Source Type" selects Minute, Renko, Tick, Range, or Day; the property
  grid swaps between a brick-size-in-ticks field (Renko) and a period field
  (everything else) based on the selection.

### RenkoSizeTable (NinjaTrader, new)
- Table of suggested Renko box sizes (half ATR, points and ticks) across
  several minute timeframes at once, each computed on its own secondary bar
  series so nothing but the table is drawn on the chart.
- Four fixed timeframe slots (minutes, default 2/5/15/60); "Ignore gaps" and
  ATR length configurable, same sizing method as ATRRenkoSizeCalculator.

### ChartTrading (NinjaTrader, new)
- First public milestone of a click-to-trade tool: hold a modifier key
  (Shift = buy, Alt = sell) to preview the full order bracket — entry, stop,
  and up to three targets — at the mouse pointer. Preview only; it places no
  orders yet.
- Preview levels render like the platform's own working orders: chevron order
  tags, a line from the tag to the right edge, and a pointed price tag at the
  right. Tag placement is configurable (left / center / right with a border
  margin).
- Entry tag infers LMT vs STP from the pointer being below or above the last
  traded price; tag quantity follows the ChartTrader quantity selector.
- The stop and target tags can show the price, the tick distance from entry,
  or the money value for the current quantity ("Level value" setting).
- A "ChartTrading ON/OFF" button in the ChartTrader sidebar (floating on the
  chart when ChartTrader is hidden) disables the modifier-key gestures without
  removing the indicator, freeing the keys for other tools.
- Order submission, gated by the ChartTrading button alone: green ON, a click
  while the preview is armed submits the entry to the ChartTrader account, and
  each enabled pair's stop and target go live as an OCO group once the entry
  fills; gray OFF, keys and clicks do nothing. Live accounts additionally
  require "Allow live accounts" (default off); Sim/Playback always accepts.
- Configurable entry order types: Limit or MIT on the favorable side of the
  market, Stop-Market or Stop-Limit (with a tick offset) beyond it; the
  preview tag always labels what the click will submit.
- Optional "Separate stacked stops": when two pairs share a stop price, each
  extra stop nudges one tick further from entry so the chart shows and drags
  them individually. Off by default; the preview mirrors the nudge.
- A "Stops to BE" button below the toggle moves every working ChartTrading
  stop on the instrument to the position's average fill price, clamped so a
  stop never crosses the market. It finds the stops on the account by name,
  so it works even after a recompile or reload, touches only stops this tool
  created, and logs every outcome instead of failing silently.
- Configurable bracket pairs: three stop/target pairs, each toggled by its own
  "Bracket N" checkbox, each with its own stop and target. The ChartTrader
  quantity sizes each enabled pair, so the entry trades quantity x enabled
  pairs and the stops always cover the full position. Defaults center the tags
  and use 50-tick steps with two pairs enabled.
- Auto-breakeven ("Auto breakeven" checkbox, default off): once price runs the
  configured trigger distance (ticks) in the position's favor, every working
  ChartTrading stop moves to breakeven automatically — the same move as the
  "Stops to BE" button, fired once per position and re-armed when the position
  closes or flips. A "Breakeven offset (ticks)" setting shifts where breakeven
  lands (e.g. 2 locks two ticks of profit) and applies to the button as well,
  so manual and auto always agree. Like an ATM, it only acts while the chart
  is open.
- Breakeven moves (button and auto) never loosen protection: a stop already
  at or beyond breakeven — manually trailed, say — stays where it is. Moves
  clamp one tick inside the market instead of exactly at it, and the auto
  trigger waits until the configured offset fits inside the market.
- Auto-breakeven no longer disarms for the rest of the position if it
  triggers in the instant before the stops go live (it retries once they
  do), and a trigger caught mid-flight by a position change stands down
  instead of acting on the replacement position.
- Partial fills are now bracketed as they happen: exits go live sized to the
  filled quantity and grow as further fills arrive, so an entry cancelled
  after a partial fill no longer leaves that partial unprotected. Fills are
  assigned to bracket pairs in order (pair 1 up to its size, then pair 2).
- New "Time in force" setting: Day (default) or GTC, applied to the entry
  and every stop and target.

### ATRRenkoSizeCalculator (NinjaTrader)
- Removed a stale duplicate of the indicator that would break compilation if
  both copies were imported; the surviving version renders its values table
  with the chart's own graphics pipeline.

### MACDHistogram (NinjaTrader)
- The five momentum colors (positive rising/falling, negative falling/rising,
  neutral) are now real settings under a "Colors" group — previously they
  were hardcoded despite the indicator describing itself as configurable —
  and they persist across workspace and template reloads.

### VolumeWithEMA (NinjaTrader)
- Custom colors now survive workspace and template reloads (they previously
  reset to defaults on restore).
### RenkoWicks (NinjaTrader)
- New chart defaults: 50-tick bricks and 15 days of data to load (were 20
  and 3).
- **Fixed: lower wicks were never drawn.** A rendering bug present since the
  first version made every down-pointing wick silently disappear; up bricks now
  show their pull-back lows and down bricks their rally highs, as intended.
- Bricks that only exist to fill a price gap now render faded ("Gap brick
  opacity" setting) and no longer repaint colors carried by bar-override
  brushes from other scripts.

### ErgonomicCharts (NinjaTrader)
- Zoom and pan gestures now stop at the chart panel's edges (DPI-correct hit
  test), so they no longer trigger over the price/time axis strips; on
  multi-panel charts they scope to the panel the indicator is loaded on.
- Hardened the drag-to-pan key release against event suppression, closing one
  more way the synthetic Ctrl could have been left held.
- Drag-to-pan is now enabled by default (tested working); a tap of Ctrl clears
  a stuck key if the platform ever crashes mid-drag.

### Namespacing
- The custom indicators now live under the `FilipeAmaral` sub-namespace, so
  they group together in NinjaTrader's lists as first-party work.

### Repository
- Documentation consolidated: the root README is now the home page, with a
  feature list and usage notes for every indicator on every platform; the
  NinjaTrader README keeps install steps and platform quirks. ChartTrading's
  finished planning documents were removed.
- Open-sourced properly: MIT license, English root README, per-platform
  documentation, and this changelog.
- Vendor documentation (NinjaTrader developer docs, Nelogica NTSL manual)
  removed from the repository and its history; the tooling to build local
  copies remains.

## 2026-07-15

### RenkoWicks (NinjaTrader)
- **Fixed: reversals deleted real price extremes.** A brick completed by a
  reversal lost the counter-trend high/low that actually traded, understating
  ATR and stop distances downstream. Completed bricks now keep the true
  extreme; the trend side stays pinned to the boundary so gap overshoot is
  never misattributed.
- Fixed the previous session's last bar being flattened into a doji when
  Break EOD is enabled.
- Chart style fixes: bar-width changes take effect immediately, and the
  "Candle Outline" / "Candle Wick" settings color what their names say
  (previously they were applied by bar direction).

### ErgonomicCharts (NinjaTrader)
- Scroll-wheel zoom now drives the platform's own bar-spacing handlers,
  removing zoom drift (a wheel up + down no longer shrinks the chart) and
  respecting the user's bar-spacing ratio.
- Drag-to-pan is now off by default and hardened when enabled: the synthetic
  Ctrl key is shared across charts, released on window deactivation and lost
  capture, and never fights a physically held Ctrl. A crash mid-drag can still
  leave Ctrl held — that trade-off is documented in the setting.

### Documentation tooling (NinjaTrader)
- Docs scraper rewritten: content-hash change detection, per-product output
  folders, no browser dependency, and a generated topic index.

## Earlier

- Initial collection: Profit Chart (NTSL) confluence coloring system, signal
  labels, signal board, tape-reading and MACD histograms; TradingView Pine
  scripts (candle countdown / position sizer, time-based price levels); and
  the first NinjaTrader ports (RenkoWicks, ErgonomicCharts, ExponentialATR,
  MACDHistogram, VolumeWithEma).
