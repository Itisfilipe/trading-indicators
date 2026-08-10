# Changelog

Notable changes to the indicators in this repository. Dates follow ISO 8601
(YYYY-MM-DD); entries describe what changed on the chart, not internals.

## 2026-08-10

### NinjaTrader settings panels (all indicators)
- Every indicator's settings groups are numbered so the panel lists them
  in working order — main controls first, colors and cosmetics last —
  instead of alphabetically. ATR Renko Size Calculator's flat parameter
  list splits into ATR and Display groups.

### Candle Countdown & Position Sizer (NinjaTrader, new)
- Port of the TradingView tool: an on-chart table with the current time in
  a chosen timezone, live countdowns to the next candle on up to three
  minute-based timeframes (highlighting when a new candle is seconds
  away), the ATR, the stop distance at a configured ATR multiple, and the
  lot size that keeps the loss at the risk budget if that stop is hit.
  Countdown timeframes are entered in minutes, and the table text follows
  the chart's label font. The margin between the table and the viewport
  edges is adjustable, and the ATR and Stop ATR rows can be hidden. The
  settings panel shows only the selected risk mode's fields: Fixed $
  keeps the dollar amount, Percent of Account keeps the account size
  and percentage.
- "Auto-Set ChartTrader Quantity" (off by default) mirrors the suggested
  lot into ChartTrader's quantity field whenever the suggestion changes,
  so orders placed from ChartTrader trade that size without retyping it.

### Time-Based Price Levels (NinjaTrader, new)
- Port of the TradingView tool: horizontal lines at up to ten price
  levels, each from an intraday clock time or a higher-timeframe period
  (D, W, M, 3M, 6M, 12M), with auto labels, per-line stroke, and a
  days-of-history control. Intraday levels now catch their bar on any
  chart timeframe (the Pine version needed a bar stamped exactly at the
  minute), and higher-timeframe High/Low/Close track the developing
  period live. Label font and how far a running line and its label
  extend past the last bar are configurable.

### Time-Based Vertical Lines (NinjaTrader, new)
- Port of the TradingView tool: vertical lines at chosen clock times drawn
  for the whole day — future ones included, each with a countdown pinned
  near the viewport edge. A time past the last bar is placed by extending
  the recent bars' pace.

### ICT Macros (NinjaTrader, new)
- The macro windows from the Time-Based Vertical Lines port, split into
  their own indicator: each window bracketed by a line at both ends with a
  captioned band joining the pair, the caption counting down to the window
  and then counting out what is left of it. The bands ride a strip at the
  bottom (or top) of the viewport. On by default: the six :50-:10 windows
  inside regular hours plus the Final Hour and Market On Close windows —
  the playbook's tradeable set; the pre-market and London pairs are
  carried but off.

### ICT Killzones (NinjaTrader, new)
- A translucent box around each killzone's candles — first bar to last,
  running high to running low, growing while the window is open. Eight
  zone slots with per-zone colors; the playbook's four killzones (Asia
  Range, London Open, NY Open, London Close, index-futures readings in
  New York time) start on, the session blocks start off. Windows may run
  past midnight; history is kept a configurable number of days.

### Breakeven Stops (NinjaTrader, new)
- ChartTrading's breakeven feature as a standalone indicator: a "Stops
  to BE" button in the ChartTrader sidebar plus an automatic once-per-
  position trigger (on by default, 30 ticks) that move every working
  protective stop on the instrument — ATM, OCO, or manual — to
  breakeven plus a configurable tick offset. Stops already at or beyond
  breakeven are never loosened, and the move is clamped one tick inside
  the market.

### Economic Calendar (NinjaTrader, new)
- Port of a TradingView economic-calendar indicator: the week's Forex
  Factory releases as impact-colored vertical lines (future ones placed
  ahead of the last bar), captions naming each release, and a corner
  table with past rows grayed out. Impact and currency filters, with an
  Automatic mode that follows the chart's instrument. The feed is pulled
  from Forex Factory's public weekly calendar over HTTPS and refreshes
  hourly; times print in the platform's display timezone.

### Rectangle Midline (NinjaTrader, new)
- Drawing tool: the chart's rectangle with a horizontal line across its
  middle, the way TradingView's rectangle draws one. It is drawn, dragged
  and resized like the built-in rectangle; the midline has its own on/off,
  color, width and dash style, and stays at the geometric centre of the box
  rather than snapping to the nearest tick.

## 2026-08-06

### Time-Based Vertical Lines (TradingView, new)
- Vertical lines at the clock times that matter during a session: New York
  midnight, the 09:30 open, the 08:30 news. Ten slots, each with its own
  time, label, color and style, plus twelve macro slots that default to the
  ICT windows, keep their own on/off and start and end times, and are
  bracketed by a line at each end with a captioned rectangle joining the
  pair, whose caption counts down to the window and then counts out what is
  left of it. Every line is drawn for the whole day, the ones still ahead
  included, each with a countdown to it, so the chart shows what is coming
  and how long there is until it. Captions and rectangles share one strip
  set clear of the bars, above or below them, so nothing is drawn over the
  candles.

### ICT Macros (TradingView, removed)
- The macro-block indicator added on 2026-08-04 is gone. Time-Based
  Vertical Lines covers the macro windows as part of a general time-marking
  tool.

## 2026-07-22

### Renko Size Table (TradingView, new)
- TradingView (Pine v6) port of the NinjaTrader Renko Size Table: a table
  of suggested Renko brick sizes (half ATR, in price and ticks) across four
  timeframes at once, each with a live EMA ATR and a steadier
  median-of-days ATR. Place it on a chart timeframe at or below the
  smallest row (a 1-minute chart for the 1/5/15/60 defaults); a row below
  the chart timeframe is flagged rather than shown wrong.

### ChartTrading (NinjaTrader, fixed)
- Stops and targets now sit their configured distance from where the entry
  actually fills, not from where it was first clicked. Placing a limit and
  then moving it — dragging it, or attaching it to a moving average so it
  rides along — used to leave the stop and targets at the original click
  distances once it filled; they now track the real fill. Unmoved entries
  are unaffected; a stop or MIT entry that fills with slippage now measures
  its exits from the true fill.

## 2026-07-21

### RenkoWicks (NinjaTrader, changed)
- Up and down bricks now have separate outline and wick colors. The chart
  style's existing "Candle outline" and "Candle wick" settings became the
  up-brick pair (saved charts keep their colors), and two new settings —
  "Candle outline (down)" and "Candle wick (down)" — control the down
  bricks. Restart NinjaTrader after compiling for the new settings to
  appear.
- New default palette: up bricks green (#25D725) and down bricks red
  (#CC0000) across fill, outline, and wick, readable on dark and light
  backgrounds. Charts with saved colors keep them; the defaults apply to
  newly added charts or after resetting the chart style.

### CustomVWAP (NinjaTrader, fixed)
- The RTH window defaults were one hour off: the window is compared in
  the trading-hours template's time zone, and CME futures templates run
  on Chicago time, so US index RTH is 830–1500 there — not the New York
  930–1600 the defaults assumed. Instances added before this fix keep
  their saved 930/1600 and should be set to 830/1500 (or removed and
  re-added).

### CustomVWAP (NinjaTrader, new)
- New indicator: VWAP with standard-deviation bands (three configurable
  pairs, defaults 1 and 2 deviations), anchored per daily session or per
  week — add it twice for both lines. The RTH-only window is optional
  (on by default, 930–1600 exchange time); with it off the whole session
  accumulates and the daily anchor resets at the session roll. Works on
  any chart including ETH charts, where the lines hold flat outside an
  enabled RTH window. Calculation granularity configurable (default
  10-tick bars).

### RiskRewardTargets (NinjaTrader, fixed)
- Placing the tool can no longer stall half-built, and a drawing saved in
  that state by an older build no longer feeds unplaced anchors to the
  platform's snap-to-object logic (a source of "Object reference not set"
  errors on every mouse move while drawing with snapping enabled).

### RenkoSizeTable (NinjaTrader, changed)
- The first timeframe now defaults to 1 minute, making the default row
  set 1/5/15/60. Instances already on a chart keep their configured
  timeframes.

### MultiSeriesEMA (NinjaTrader, fixed)
- The internal Renko grid now re-anchors at each session's first price,
  matching the platform's own Break-at-EOD Renko. Without this the brick
  levels drifted from stock Renko's from the second session on, putting
  the EMA a little off everywhere.

### MultiSeriesEMA (NinjaTrader, fixed)
- The EMA line no longer chases every bar of the chart it overlays: it
  moves only when one of its own source bricks or bars completes, the way
  a higher-timeframe reference line should read. The internal Renko
  engine is also now fed from a fine tick series (a tenth of a brick)
  instead of the chart's own bar closes, so the historical line follows
  the true brick sequence instead of re-anchoring at each chart bar
  (simulation: ~0.06 brick average deviation from a perfect tick feed).
  If that tick series ever fails to load, the line falls back to
  chart-close granularity — coarser, never a crash.

### MultiSeriesEMA (NinjaTrader, fixed)
- The Renko-source EMA no longer uses a secondary data series at all: the
  bricks are computed internally from the chart's price stream (stock-Renko
  close-keyed semantics, property-tested against the RenkoWicks brick
  logic). This removes the entire class of crashes where a secondary Renko
  series failed to load — on workspace restore, reconnect, or the
  platform's own rejection — and every drawing-tool click then blew up the
  chart with "Object reference not set to an instance of an object". There
  is nothing left to load, so there is nothing left to fail. One visible
  nuance: chart history feeds the bricks at the chart's own bar
  granularity (live data is exact, tick by tick), so an EMA on bricks
  finer than the chart's bars starts from a coarser seed and converges
  within a brick fraction over the loaded history. Minute/Tick/Range/Day
  sources still use a real secondary series as before.

### MultiSeriesEMA (NinjaTrader, superseded same day)
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
