# Changelog

Notable changes to the indicators in this repository. Dates follow ISO 8601
(YYYY-MM-DD); entries describe what changed on the chart, not internals.

## 2026-07-21

### RenkoWicks (NinjaTrader, changed)
- Up and down bricks now have separate outline and wick colors. The chart
  style's existing "Candle outline" and "Candle wick" settings became the
  up-brick pair (saved charts keep their colors), and two new settings —
  "Candle outline (down)" and "Candle wick (down)" — control the down
  bricks, defaulting to the same black as before until recolored. Restart
  NinjaTrader after compiling for the new settings to appear.

### MultiSeriesEMA (NinjaTrader, fixed)
- Follow-up to yesterday's crash fix: the error could still return when a
  workspace restored with the indicator on the chart. The Renko source
  series now follows the chart's instrument with no instrument lookup at
  configure time, which survives workspace restores and reconnects alike.
  If adding the source series ever fails anyway, the indicator now simply
  plots nothing (with a note in the Output window) instead of leaving the
  chart in a state where every click errors. After compiling, remove the
  indicator from the chart and add it back once — the workspace still
  carries the broken instance saved while the old build was crashing, and
  a fresh add is what purges it.

## 2026-07-20

### MultiSeriesEMA (NinjaTrader, fixed)
- Fixed the endless "Unhandled exception: Object reference not set to an
  instance of an object" errors that started on every mouse click or move
  over the chart after a data reconnect. With Source Type set to Renko,
  the secondary series was added under the instrument's runtime name,
  which NinjaTrader cannot reliably resolve when it replays the
  indicator's configuration during a reconnect; the failed reload left
  the indicator half-initialized and every chart interaction after that
  crashed inside the platform. The series now always follows the chart's
  own instrument. If the secondary series ever fails to load anyway, the
  EMA simply doesn't plot instead of erroring.

### ChartTrading (NinjaTrader, changed)
- The indicator's settings groups are now numbered (1. Gesture,
  2. Bracket, 3. Trading, 4. Appearance, 5. Colors) so they all sort
  together at the top of the properties window instead of interleaving
  alphabetically with NinjaTrader's own sections.

### ChartTrading, OrderDecorator, RiskRewardTargets (NinjaTrader, fixed)
- Hardened against platform-level "Unhandled exception" errors: closing a
  chart, recompiling, or reloading while the mouse is moving, orders are
  updating, or a drawing tool is being deleted can no longer crash with
  "Object reference not set to an instance of an object". If anything else
  ever fails inside ChartTrading's order/position handling, the log now
  names ChartTrading and carries the full error instead of an anonymous
  platform message.
- OrderDecorator: with a position open before the first quotes arrive
  (fresh connect), the average-price label shows side and quantity in
  neutral gray until a P&L mark exists, instead of risking the indicator
  being disabled mid-session.

## 2026-07-18

### RenkoWicks (NinjaTrader, fixed)
- With Break at EOD on, a session roll no longer leaves an unspanned
  vertical void in the brick chain. The overnight move is now walked with
  faded zero-volume bricks from the previous session's last brick to the
  new session's first price (including a partial step for the sub-brick
  remainder), and the session opens with a doji there — every bar opens on
  the previous bar's body, at session boundaries and everywhere else. The
  previous session's final brick keeps its real body and wicks.
- After a chart reload or reconnect mid-series, the brick grid re-anchors
  at the last brick's close instead of guessing from the second-to-last
  bar, which could rebuild a wrong grid right after a session roll.

## 2026-07-17

### RenkoWicks (NinjaTrader, fixed)
- No more spurious brick staircases around session opens. Brick completion
  and gap filling are now keyed on the close, exactly like the platform's
  own Renko: when historical data arrives as whole OHLC bars instead of
  ticks, a single spike-wick bar used to fabricate a run of faded bricks up
  to the spike top that the close never confirmed. Such spikes now stay
  visible as wicks — this bars type's whole point — instead of becoming
  bricks. Tick-built charts are unaffected (bit-identical output, verified
  by simulation), and the up/down mirror property still holds.

### ChartTrading (NinjaTrader, changed)
- "Stops to BE" (and auto breakeven) now moves every working protective
  stop on the instrument — whatever tool placed it — instead of only the
  stops ChartTrading created. Stop orders on the entry side (stop entries)
  are left alone, and a moved stop-limit keeps its trigger-to-limit
  offset.

### ChartTrading (NinjaTrader, fixed)
- A fast entry fill could leave part of the position unprotected for good:
  exits are only resized while their orders are alive, and a fill arriving
  before the exits finished submitting skipped the resize with no retry —
  a 4-lot entry could stay covered by 3 lots of exits, which also made
  "Stops to BE" look like it moved only some stops. Each exit order coming
  alive now re-checks its bracket's quantities, so a missed resize is
  applied as soon as the order can accept it.

### OrderDecorator (NinjaTrader, new)
- Labels every working order on the chart's instrument with its distance
  from the position's average price — ticks, optional points, optional
  money value for the remaining quantity — colored by side (profit/loss).
  Profit-side orders also show their R multiple against the nearest
  working stop. Flat, labels show distance from the last price in neutral
  gray. Works on any working order, whatever placed it.
- The execution (average price) line carries its own label too: position
  side, quantity, and live unrealized P&L in ticks and money, colored by
  whether the position is currently winning.
- Orders resting on the same price merge into one label with the summed
  quantity, matching how the platform stacks their markers (e.g. several
  bracket stops parked together at breakeven).
- Default right margin is 50 pixels.

### RiskRewardTargets (NinjaTrader, new)
- Risk/reward drawing tool with up to three independently draggable targets.
  Two clicks place entry and stop; targets seed at 1R/2R/3R and then every
  level moves freely, each target labeled with its price, tick distance, and
  R multiple recomputed live from the current entry/stop distance — unlike
  the built-in tool, which derives one side from the other through a fixed
  ratio. Works long and short; 1-3 targets configurable; lines extendable
  left/right.

### RenkoSizeTable (NinjaTrader, changed)
- New "Med ATR" and "Med Ticks" columns: the median of the row's last-N
  daily ATRs in points, and half of it in ticks (each completed session
  contributes its mean true range; N is the row's day count). Unlike the
  live exponential ATR/Ticks read, the median doesn't budge for one outlier
  session, giving a steadier brick-size suggestion on instruments whose ATR
  swings day to day.
- ATR period is now set in days instead of bars, and each timeframe row has
  its own day count ("ATR 1-4 (days)"; defaults 3/5/10/20 for the
  2/5/15/60-minute rows). A row converts its day count to a bar count by
  measuring how many bars its sessions actually hold. Smoothing remains
  exponential.
- New "Top Margin (pixels)" setting (default 40) drops the table below the
  chart's top-right toolbar icons so they stay clickable.

### ChartTrading (NinjaTrader, changed)
- Removed the "Allow live accounts" setting and its live-account gate.
  Whichever account is selected in ChartTrader is what a click trades now;
  the ChartTrading ON/OFF button is the only switch. Account choice is the
  trader's own responsibility.

## 2026-07-16

### ChartTrading (NinjaTrader, changed)
- Bracket pair sizing no longer multiplies the entry by the number of
  enabled pairs. The entry now always trades the ChartTrader quantity
  outright; each pair takes a configurable percentage share of it (new
  "Percent 1/2/3" settings, default 25/25/50), split into whole lots as
  evenly as the math allows (e.g. 3 lots at 25/25/50% → 1/1/1). Percentages
  are renormalized across whichever pairs are currently enabled, so a
  disabled pair's share always lands on the others instead of leaving part
  of the position unprotected; a pair whose share rounds down to zero lots
  is skipped for that order.

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
