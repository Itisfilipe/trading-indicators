# Changelog

Notable changes to the indicators in this repository. Dates follow ISO 8601
(YYYY-MM-DD); entries describe what changed on the chart, not internals.

## 2026-11-13

- Flow Absorption now behaves like Mark50's: bands change only at a candle
  close and start on the next candle, the number only grows and stays with
  the price, and a level a candle closes through comes back on the other
  side one candle later with the same number.
- ICT Killzones, ICT Macros, Time-Based Vertical Lines, Time-Based Price
  Levels and Economic Calendar: drawings now start on the candle that opens
  at their time, as on TradingView. NinjaTrader labels a candle with its
  close time, so a 09:30 line used to sit one candle early, on the candle
  that closed at 09:30. Killzone boxes now cover their candles edge to edge.
- ChartTrading and Breakeven Stops: pop-up windows only when a position may
  be left without a stop or with stops of the wrong size. Refused clicks,
  Stops to BE not possible, retries and cleanups go to the log only; an
  error that repeats pops up at most once a minute.
- ChartTrading: a stop or target hit is a normal close again; the other leg
  is cancelled quietly instead of reported as a position closed elsewhere.
  A leftover stop or target that comes back alive on a flat account is
  cancelled again.

## 2026-11-12

- New Candle Countdown Bubble: the time left in the chart's candle in a box
  beside the last candle, updated every second.
- ChartTrading and Breakeven Stops: messages that need attention (a
  refused click, an order the tools placed or cancelled on their own, a
  failure that can leave the position unprotected) now pop up instead of
  only reaching the Log tab.
- ChartTrading: a bracket replaced after being stuck mid-change keeps the
  stop and target prices it last held (moved to breakeven, dragged), and a
  stuck order whose cancel is refused is managed again instead of being
  doubled. A stop whose target you cancelled still grows to cover later
  fills, and a target left working after its stop is gone is cancelled.
  Two quick opposite clicks can no longer both get through.
- Close half: when its exit fills in pieces, stops and targets are cut
  again at each new position size, not only once.
- ChartTrading: a stop or target stuck mid-change for 5 s (a Sim risk
  limit can freeze one) is cancelled and placed again; a target change the
  broker refuses is sent once more, then that bracket is closed at market.
  A new entry filling just as a stop closes the previous position keeps
  its bracket, and a fill that lands as an order from elsewhere flips the
  position no longer leaves exits that would add to it.
- ChartTrading: clicks against the open position (or a working entry on
  the other side) are refused; close the position first with Close half /
  Close all. A position that shrinks because a target filled
  waits half a second before trimming, so the trim comes out of the bracket
  that filled rather than another one. A trim the broker refuses is sent
  again once, then that bracket is closed at market; a target left larger
  than its stop is resized to it.
- ChartTrading: a resting entry keeps its stops and targets measured from
  its actual fill even when the position changes while it rests.
- Close half stays greyed out until its market exit has finished filling,
  so quick repeat clicks can no longer close past flat.
- ChartTrading: exits placed for a resting entry whose fill only
  flattened a position opened elsewhere are cancelled within about a
  second, even when no trades print.
- ChartTrading: a stop the broker refuses (or a stop it refuses to
  enlarge) is placed again once; refused a second time, the contracts it
  should have covered are closed at market and the log says so.
- ChartTrading: when one target fills, the trim after it comes out of
  that same bracket instead of cancelling another bracket's stop.
- ChartTrading: when the position shrinks from outside (a market order, a
  manual exit, Close half), stops and targets shrink to match; a target
  hit while the entry is still filling no longer leaves later lots without
  a stop; stops left over from before a reload are cancelled once flat.
- Close half waits if a stop or target is still being changed, and for a
  few seconds afterwards cuts any stop left larger than the position
  (for example after a rejected resize), logging an error if the exit
  never filled. ChartTrading's own stops and targets are left to
  ChartTrading, so the two never cut the same order twice.
- Flow POC: new "POC period" setting shows a longer timeframe's POC on a
  faster chart (2 minutes up to 12 hours, or one per session), drawn
  across the candles of each window.
- ChartTrading: when the position is closed or reversed by an order from
  elsewhere (a market order, Close all), its leftover stops and targets are
  cancelled instead of staying on the chart where they could open a new
  trade.
- Breakeven Stops: large Close half and Close all buttons under Stops to
  BE. Close half exits half the position (one contract closes outright)
  and shrinks stops and targets to the size left.
- Rectangle Blue, Rectangle Gray and Rectangle Amber: Rectangle Midline in
  three colors, one Draw menu click each, with colored icons.

## 2026-11-11

### Repository layout
- `ninja-trader/` now mirrors `Documents/NinjaTrader 8/bin/Custom/`: copy its
  `Indicators`, `DrawingTools`, `BarsTypes` and `ChartStyles` folders there to
  install everything. Indicators live in `Indicators/FilipeAmaral/`; delete
  older copies left directly in `Indicators/`, and any `FilipeAmaral`
  subfolder in `DrawingTools/`, `BarsTypes/` or `ChartStyles/`, first.

### More review fixes
- ChartTrading: raising a resting entry's quantity on the chart now gets
  stops and targets for the extra contracts (they go to the last bracket
  pair) instead of leaving them unprotected.
- RenkoWicks: after a reload or reconnect mid-session, the next brick
  stays attached to the chain and keeps the wick already formed.
- Flow CVD: "Reset at time" works on tick, volume and range charts.
- Time-Based Price Levels: a weekly or longer level is no longer drawn
  from a period the chart only partly loaded; weekend and holiday gaps no
  longer stack every slot at the reopening price.
- Economic Calendar: charts share the saved week instead of each one
  downloading the feed, and only the last six weeks are read back.

### Economic Calendar, ICT Macros, Time-Based Vertical Lines
- Future releases, macros and lines keep their real spacing right after a
  weekend or the daily break instead of piling up next to the last candle.
- Economic Calendar's week table shows the coming week on Sunday evening.
- The calendar table and the macro bands no longer block click-and-drag
  panning of the chart.

### ChartTrading
- The "Stops to BE" button is gone; Breakeven Stops has it.
- The ON/OFF button only appears in the ChartTrader sidebar; it no longer
  floats on the chart when ChartTrader is hidden.
- A click only places an order if the modifier is still held at the
  click; a preview left armed after releasing the key on an unfocused
  chart no longer sends an order.
- Removing or reloading the indicator cancels its unfilled entries, which
  would otherwise fill with no stop or target.
- A click on a newly selected ChartTrader account is refused while the
  previous account still has a position or pending entry, so that
  account keeps getting its exits. Auto breakeven stays.
- Auto breakeven rounds a between-ticks average price toward profit, so a
  position built from several fills no longer gets its stop a tick short
  of breakeven.

### Breakeven Stops
- A Stops to BE button in the ChartTrader sidebar, styled like the
  ChartTrading toggle; it dims when no stop is short of breakeven.
- Stops to BE rounds toward profit when several fills average between
  ticks, and moves nothing until the bid (long) or ask (short) is a tick
  past breakeven, instead of parking stops short of it.

### OrderFlowKit
- Flow Big Trades (new): large aggressive market orders as bubbles, one per
  price, rebuilt from their fills, sized by lots, with a manual lot filter or an automatic
  one that keeps the largest orders the chart has seen.
- Flow POC squares are a little larger by default (marker size 18), and
  the per-candle volume and delta are hidden by default. The baseline is
  shorter for scalping: the previous 12 candles (was 30), or 4 past
  sessions in SameTimeOfDay mode (was 20). Its settings are
  regrouped: when to mark (baseline, square and outlier thresholds), POC,
  markers, absorption ring, colors.
- Flow POC outliers: a POC at least 3 times the average ("Outlier at", 0 = off) is drawn 30% larger, solid, with a thick border;
  size, fill and border are settings.
- Flow VD, CVD, POC and Absorption keep updating in Market Replay outside
  real trading hours (the session check used the computer's clock).
- Flow VD's z-score no longer counts candles without Tick Replay data as
  zero-delta candles.
- Flow Alignment removed (corner table, aligned-state shading and replay
  journal); read alignment from Flow HA, Flow CVD and Flow VD directly.

## 2026-11-10

### OrderFlowKit (new)
- Flow POC: each candle's most traded price marked and colored by the side
  that dominated it; strong ones (above the 30-candle average) drawn as a
  circle or square with the contract count, the rest as a dash. Volume and
  delta print under every candle.
- Flow VD: per-candle delta histogram, live while the candle forms.
- Flow CVD: cumulative delta as candles, with swing circles, level lines
  until the CVD closes through them, and blue/red absorption bands.
- Flow HA: Heikin-Ashi candles over the chart, inside candles grayed,
  reversal triangles after a run.
- POC, VD and CVD need Tick Replay to show history.
- Flow POC can judge strength against the same time of day in past
  sessions, and can ring absorption POCs (losing aggression at the
  candle's edge). Flow VD can plot delta as a z-score. All off by default.
- Flow CVD can restart at a time of day (New York time, default 09:30),
  for ETH charts whose sessions open in the evening.
- Flow Absorption (new): blue and red bands at prices that took heavy
  one-sided aggression and held, labeled with the lots still defending
  them, ending when those lots are eaten or a candle closes through.
- Flow HA draws CCZ zones: a box over each small-body Heikin-Ashi candle's
  range, gray once price closes outside it.
- Flow POC labels strong POCs with the lots left over at that price (buys
  minus sells) by default; the old contract count is the "Volume" label.
- Flow POC prints volume and delta above falling candles and below rising
  ones, and strong POC markers grow less, to keep busy charts readable.
- Flow POC draws strong POCs as squares by default; "Marker" = Circle
  brings the circles back.
- Flow POC labels and judges strong POCs by the volume traded at the POC
  price again, as the reference chart does (a tied, gray POC there still
  reads 84); leftover lots stay available as "Label" and "Strength
  measure" = Delta.
- Flow POC markers are solid over the chart background, so the number
  stays readable on dark candle bodies.
- Flow CVD and Flow VD can build delta the way the reference trader's
  1-minute panels do ("Delta source" = HeikinAshiVolume): each candle's
  whole volume, signed by a Heikin-Ashi candle of price. The default stays
  the real bid/ask delta.
- Flow Absorption counts all the one-sided aggression in a zone (not the
  net) and keeps a level until its lots are eaten, even after price closes
  beyond it, as on the reference chart.
- Flow Absorption hides eaten levels by default ("Show consumed levels").
- Flow Absorption eats a level only with each later candle's net
  aggression into it, so levels survive price passing through, as his do.
- Flow Absorption finds many more levels: it reads net aggression over
  4-tick zones instead of gross volume at one tick, with lower defaults
  (50 lots, 2x the candle average, bottom/top half of the candle). Bands
  are at least 8 px tall, buyers absorbing are blue, and the lot count
  sits in a colored box on the right edge.
- Flow POC squares default to 16 px and barely grow (10% at most), with
  9 pt text; absorption bands are 40% opaque with bold lot boxes.
- Flow Alignment (new): a corner table with Heikin-Ashi, CVD and VD
  direction on the 1-minute chart and on 5 and 15 minutes built from it,
  the 15-minute lot balance, and BUY/SELL aligned shading when all agree
  with at least 500 lots behind them.
- Flow Alignment's replay journal trades each new alignment in a New York
  window by fixed rules (stop past the previous 5-minute Heikin-Ashi
  candle, 60-tick target, exit on VD flip or time), marks each trade with
  its R on the chart, sums the results in the corner and writes them to a
  CSV under Documents\NinjaTrader 8\FlowAlignment.
- Buying is blue instead of green across Flow POC, VD, CVD, Absorption and
  Alignment (selling stays red), which also reads under red-green color
  blindness. Flow HA keeps the chart's candle colors.
- Flow CVD draws only the pivot levels the CVD has not crossed yet ("Show
  broken levels" brings the rest back) and needs 3 candles on each side
  for a pivot, so the panel carries far fewer lines.
- Flow CVD draws the cumulative delta as Heikin-Ashi candles by default,
  like the reference trader's panel: smoother runs and fewer pivots.
  "Heikin-Ashi candles" off restores the raw candles.
- Flow VD can smooth bar height with an EMA ("Smoothing period", off by
  default; the color still follows each candle's own delta).
  "Strength measure" can judge strong POCs by those leftover lots instead
  of their volume.
- Flow HA grays candles that break both the previous high and low, not
  only those that break neither; adds an optional EMA smoothing and dotted
  traffic-area lines at the previous candle's high, middle and low.
- Default colors fit the Modern Green chart: buy and sell aggression in
  the same green and crimson the trading tools use for profit and loss,
  Heikin-Ashi candles in the chart's own mint and near-black, CCZ zones
  and the absorption ring in the palette's amber, neutral marks in gray.

## 2026-11-09

### Chart palette (changed)
- New palette, "Modern Green": mint up candles (#37F09C), near-black down
  candles (#1A1C26) and one near-black outline, wick and doji color for
  both sides, on the same off-white background with amber zones.
- One NinjaTrader chart template, "Modern Green", replaces the three
  "Blue Slate" ones: a 1-minute ETH candlestick chart with the repo's
  indicators added and configured. Renko with Wicks, Volume with EMA and
  MACD Histogram default to the new candle colors.

### ChartTrading (changed)
- Settings regrouped: Brackets (distance mode, then each pair's on/off,
  percent, stop and target), ATR stop, Trading, Breakeven, Appearance,
  Colors. Only the selected distance mode's fields show: ticks mode hides
  the ATR and R settings, ATR mode hides the tick stops, targets and tick
  breakeven trigger. Existing settings carry over.
- Stop and target preview tags can show price, ticks, points and money
  together, each with its own setting ("Show price", "Show ticks", "Show
  points", "Show money"); this replaces the single "Level value" choice,
  which resets to the new defaults (price, ticks, money).
  With all four off, stops and targets draw without a price tag.
- New "Entry price" setting: Mouse (as before) or Market. With Market, a
  click sends a market order wherever the pointer is, buy or sell by the
  held key, with the preview at the ask or bid and the bracket placed from
  the fill.
- ChartTrading no longer switches itself OFF on every add, reload or
  recompile: the ON/OFF state is kept and saved with the workspace, and a
  new "Enabled" setting (on by default) sets it.

### Order Decorator (changed)
- "Show Ticks" setting: the tick distance can be hidden like the other
  values.
- Labels of orders a few ticks apart no longer print on top of each other:
  they stack, keeping price order, and stay inside the chart.

### ICT Killzones (changed)
- Killzone boxes default to 8% fill opacity (was 15%), lighter behind the candles.
- London Open KZ defaults to soft red (#EF9A9A) instead of soft green,
  which blended with the mint up candles inside it; London Close KZ moves
  from soft red to soft purple (#CE93D8).
- Click-and-drag inside a killzone box pans the chart again; the box no
  longer grabs the click. The high/low lines can still be clicked to select
  the indicator.
- Each killzone's high and low now draw as lines in the zone's color,
  starting at the candle that made them and running right until a later
  candle touches or trades through them; a mitigated line stops at that candle.
  Only candles after the window closes count. "Show Session High/Low" (on
  by default), "High/Low Line Width" and "High/Low Line Style" control them.
- The high and low lines carry labels with the zone's name and price, in
  the zone's color. "Show High/Low Labels" (on by default) turns them off;
  "High/Low Label Position" puts them at the line's start or end;
  "High/Low Label Price" drops the price; "High/Low Label Font" sets the font.

### Profit Chart indicators (fixed)
- MACD Histogram: with the outline line off, the zero line still shows.
- Tape Reading: light theme reference lines are dark gray instead of white,
  so they are visible on a white background.
- SMC Sessions: the live session box is redrawn once per candle instead of
  on every tick, which stops stray edges when the session's high or low
  extends inside a candle.

### ErgonomicCharts (fixed)
- Removing or reloading the indicator now fully unhooks it from the chart
  window; old copies no longer keep reacting to mouse releases.

### Economic Calendar (changed)
- Works in Playback: every week downloaded is saved locally, and replayed
  days show that week's releases, lines and table. Only weeks the
  indicator was running during are available.
- Captions now sit at the release candle: above its high, below its low,
  or Auto (default), opposite the candle's direction. Releases still
  ahead stay at the panel's edge. New "Label Outline" (on by default)
  boxes each caption in its impact color.
- New "12-Hour Times" option for the table.
- The table can sit at any of nine positions, adding the edge centers and
  the middle.
- The table border follows the chart's text color by default, so it shows
  on dark charts; turn "Border Follows Chart Text" off to pick a color.
- "Today" and "This Week" now roll over at midnight in the selected time
  zone (New York by default) instead of the computer's clock.

### Economic Calendar (fixed)
- A malformed calendar download keeps the previous event list instead of
  blanking the chart until the next hourly refresh.

### Time-Based Price Levels (TradingView, fixed)
- Higher-timeframe High, Low and Close lines follow the developing period
  live instead of freezing at the value they had when drawn.

### ATRRenkoSizeCalculator (fixed)
- The table draws inside the indicator's own panel instead of at the top of
  the chart.

### NinjaTrader chart templates (new)
- "Blue Slate 1m", "Blue Slate 5m" and "Blue Slate 15m": candlestick charts
  on the CME US Index Futures ETH (full) session in the chart palette, off-white
  background, slate text, crosshair and axis, grid lines off.

### Chart palette (NinjaTrader and TradingView, changed)
- Calmer for long sessions: up candles a muted blue (#6995D3, outline
  #3E68A3) and down candles slate (#8F959E, outline #5F6773) at the same
  brightness. Rectangle Midline zones default to amber (#B8862B outline,
  12% fill, matching midline and quarter lines), the hue farthest from both
  candle colors, so candles inside a zone stay readable.
- Default colors follow one palette for white charts (see the README's
  Chart palette): Renko with Wicks candles blue up and slate down with
  matching outlines and wicks; MACD Histogram blue above zero and slate
  below; Volume with EMA blue above average and slate below with a dark
  slate average; Psych Levels, VWAP bands, Time-Based Price Levels and the
  gray Time-Based Vertical Lines in slate grays; the info tables on a dark
  slate background. Saved charts keep their colors; re-add an indicator
  to pick up the new defaults.

### Candle Countdown & Position Sizer (changed)
- The stop distance is rounded to whole ticks before sizing, so the lot
  count matches the stop ChartTrading's ATR mode actually places and never
  exceeds the risk budget at that stop.

### Psych Levels (new, NinjaTrader, TradingView and Profit Chart)
- Horizontal lines at round price levels in three tiers, each with its own
  color, width and dash: every 50 (regular), every 100 (highlighted) and
  every 1000 (major) by default.

### ICT Macros (NinjaTrader, changed)
- Same 24-slot macro map as the TradingView version, grouped by session
  (Asia, London, NY pre-market, AM, lunch, PM, final hour). The old
  02:33-03:00 and 04:03-04:30 "London macro" slots are gone in favor of
  the :50-:10 London windows, and a Settlement (15:50-16:00) slot is
  added, off by default.
- Windows can cross midnight (23:50-00:10).
- Saved charts keep their values by slot number, so remove and re-add the
  indicator to get the new defaults.

### ChartTrading (changed)
- New "Distances" setting: ATR mode places every stop at a multiple of ATR
  and each target at an R multiple of that stop (defaults 1.5 x ATR, 1R,
  2R, 3R), matching the stop the position sizer uses to size the lot. In
  ATR mode auto breakeven triggers at an R multiple of that stop (new
  "Auto breakeven trigger (R)", default 1R) instead of a fixed tick count.
- Click-to-trade now starts OFF whenever the indicator is added, reloaded
  or recompiled; click the ON/OFF button to arm it.

### Candle Countdown & Position Sizer (changed, NinjaTrader and TradingView)
- The Lot row rounds down instead of to the nearest contract, so the loss
  at the stop never exceeds the risk budget (1.5 shows 1; under 1 shows 0).

### ChartTrading (fixed)
- After switching the account in ChartTrader, auto breakeven follows the
  new account once the previous one is flat with no working entry and the
  pointer moves over the chart, instead of guarding the previous account
  until the next click-to-trade.

### OrderDecorator (fixed)
- The R multiple on target labels now measures risk to the nearest stop on
  the losing side; a stop already trailed into profit no longer counts as
  the risk basis.

### VolumeWithEMA (fixed)
- On crypto instruments the EMA line now averages the same converted volume
  the histogram shows, so above/below-average bar colors are correct.

### MACDHistogram (fixed)
- The first bar of the chart is painted in the neutral color, like any other
  zero histogram value.

### CustomVWAP and MultiSeriesEMA (fixed)
- Session resets follow the tick feed's own session boundaries explicitly.

### Profit Chart MACD Histogram (fixed)
- Light theme: the outline line is dark gray instead of white, so it is
  visible on a white background.

## 2026-09-23

### Profit Chart indicators (changed)
- Dark theme is now the default in every indicator. Its green and red match
  candles colored #3DE295 and #E22B3D on a black background (MA Cloud and
  its coloring rule, MACD Histogram, Bias Coloring, Confluence Coloring's
  trend colors, Tape Reading).
- MA Cloud and MA Cloud Coloring default to 17/34 EMAs.
- MA Cloud and MA Cloud Coloring: new "Exibir_Neutro" switch (on by
  default). Off, the gray "price disagrees with the trend" color is dropped
  and the cloud or candle keeps the trend's green or red.
- MACD Histogram defaults to 21/89 EMAs with a 42 signal.
- Renko Size Calculator: four readings, each with its own switch and line:
  the median in ticks (half the median of recent days' mean true range, the
  suggested box, the only one on by default), half ATR in ticks, half ATR
  in points, and ATR in points. Turning on several now shows several
  lines. A label on the last candle names the chart timeframe and lists
  each visible value. "Ignorar Gaps" is now on by default, and values no
  longer drift upward during the live session. The regular-hours (RTH)
  filter and its New York time settings are gone; every candle on the chart
  counts.
- Tape Reading: bars below their average are now near-gray, so the ones
  that cross the line stand out.

## 2026-08-27

### RectangleMidline (NinjaTrader)
- New extend handle on the middle of the rectangle's right edge (visible when
  selected): dragging it stretches the zone in time only, ignoring price snap,
  so a box magnetized onto a candle's wick or body keeps its exact height
  while being extended. Corner grips and placement clicks still snap normally.
- The single midline became a "Levels" choice: 50% midline, quadrants
  (25/50/75%) or octets (every 12.5%), or none. The non-50% lines get their
  own stroke ("Other levels"), dotted by default.

## 2026-08-13

### ICT Macros (TradingView, new)
- The macro windows split out of Time-Based Vertical Lines into their own
  indicator, mirroring the NinjaTrader pair: bracket lines joined by a
  captioned band on a strip that defaults to the bottom edge, per-window
  labels, and the countdown under the window's name.
- The slots grow from twelve to the full standard macro map — one row per
  :50→:10 window from the 18:50 Asia macro around the clock, plus the
  Final Hour, Market On Close and Settlement specials — grouped by
  session in the settings. The New York set the playbook trades starts
  enabled, everything else is there to switch on, and the circulated
  02:33–03:00 and 04:03–04:30 "London macro" times are gone in favor of
  the rule's own windows. The 23:50–00:10 window crosses midnight and
  stays keyed to the day it starts on.

### Time-Based Vertical Lines (TradingView)
- Slims down to the ten line slots; the macro rows are gone from its
  settings. Captions keep the top edge by default, so the two tools share
  a chart without double-booking a strip.
- With no band to straddle, the captions sit at the strip's edge — half a
  band height closer to the bars than they rode in the combined tool.
- Updating by pasting the new source over the old script scrambles saved
  settings (TradingView keeps input values by position, and the macro rows
  are gone). Add the slimmed script fresh instead.

## 2026-08-12

### Time-Based Vertical Lines (TradingView)
- Each macro window takes its own label, shown on the band in place of the
  window's times; the Final Hour, Market On Close and London windows come
  pre-labeled, and an empty label keeps the time caption.
- The macro countdown moves to its own line under the window's name, so
  the name stays put while the ticking part changes underneath it.
- Defaults now match the NinjaTrader pair of tools: the six regular-hours
  macros plus Final Hour and MOC start enabled with the pre-market pair
  carried but off, and only the three morning lines — NY midnight, the
  08:30 news, the 09:30 open — start on.

### Candle Countdown & Position Sizer (TradingView)
- The ATR and Stop ATR rows get on/off switches for a tighter table,
  matching the NinjaTrader port.

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
  and then counting out what is left of it on its own line under the
  window's name. The bands ride a strip at the
  bottom (or top) of the viewport. On by default: the six :50-:10 windows
  inside regular hours plus the Final Hour and Market On Close windows —
  the playbook's tradeable set; the pre-market and London pairs are
  carried but off. Each window takes its own label ("Final Hour", "MOC"
  and the London pair come pre-named; empty captions with the window's
  times).

### ICT Killzones (NinjaTrader, new)
- A translucent box around each killzone's candles — first bar to last,
  running high to running low, growing while the window is open. Eight
  zone slots with per-zone colors; the playbook's four killzones (Asia
  Range, London Open, NY Open, London Close, index-futures readings in
  New York time) start on, the session blocks start off. Windows may run
  past midnight; history is kept a configurable number of days.

### Breakeven Stops (NinjaTrader, new)
- ChartTrading's breakeven button as a standalone indicator: a "Stops
  to BE" button in the ChartTrader sidebar that moves every working
  protective stop on the instrument — ATM, OCO, or manual — to
  breakeven plus a configurable tick offset. Stops already at or beyond
  breakeven are never loosened, and the move is clamped one tick inside
  the market. Button only, for hand-managed market-order trades; the
  automatic trigger stays a ChartTrading feature.

### Economic Calendar (NinjaTrader, new)
- Port of a TradingView economic-calendar indicator: the week's Forex
  Factory releases as impact-colored vertical lines (future ones placed
  ahead of the last bar), captions naming each release, and a corner
  table with past rows grayed out. Impact and currency filters, with an
  Automatic mode that follows the chart's instrument. The feed is pulled
  from Forex Factory's public weekly calendar over HTTPS and refreshes
  hourly; release times print in a selectable timezone, New York by
  default.

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
