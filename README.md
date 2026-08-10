# Trading Indicators

Personal collection of trading indicators and chart tools for three platforms:

| Folder | Platform | Language |
|--------|----------|----------|
| [`ninja-trader/`](ninja-trader/) | NinjaTrader 8 | C# (NinjaScript) |
| [`profit-chart/`](profit-chart/) | Profit Chart (Nelogica) | NTSL |
| [`tradingview/`](tradingview/) | TradingView | Pine Script |

## NinjaTrader 8 (`ninja-trader/`)

Install steps and platform quirks: [`ninja-trader/README.md`](ninja-trader/README.md).

### RenkoWicks — Renko bars type + chart style

- Keeps the counter-trend price movement inside each brick as a wick, instead
  of discarding it like standard Renko.
- Up bricks: pull-back low becomes the lower wick. Down bricks: rally high
  becomes the upper wick. Trend side stays pinned to the brick boundary.
- Gap-filler synthetic bricks render faded ("Gap brick opacity") with zero
  volume. This includes session boundaries: with Break at EOD on, the
  overnight move is walked with faded bricks to the session open instead of
  leaving an unspanned jump — the chain never shows a void.
- "Candle Outline" and "Candle Wick" color settings; bar-width changes apply
  immediately.
- Defaults: 50-tick bricks, 15 days of data.
- Tick Replay not supported for this bars type.
- Use: compile both files, restart NinjaTrader, select "Renko Wicks" as the
  chart's bar type in Data Series ("Brick Size" = ticks), F5 to rebuild after
  changing the size.

### ErgonomicCharts — native-feeling zoom and pan

- Scroll-wheel zoom without holding Ctrl, using the platform's own
  bar-spacing hotkeys (no drift).
- Drag-to-pan ("Enable drag-to-pan", on by default) via simulated
  Ctrl-drag.
- Gestures stop at the panel edges, skip the price/time axis, and scope to
  the hosting panel on multi-panel charts.
- The simulated Ctrl press is session-wide; if NinjaTrader crashes mid-drag,
  tap the physical Ctrl key to clear the stuck state.
- Use: add to any chart, scroll to zoom, drag to pan.

### ChartTrading — click-to-trade with an exact bracket preview

- Hold a modifier key over the chart to preview a bracket at the mouse;
  click to submit it.
- Shift = buy preview, Alt = sell preview (both keys configurable). Preview
  tracks the pointer and labels the exact order type a click would submit
  (`LMT`, `MIT`, `STP`, `STP LMT`, `MKT`).
- Click submits the entry to the ChartTrader account. Stop/target orders go
  live against filled quantity, grow with partial fills, and are OCO-linked
  per pair. They sit their configured distance from where the entry actually
  fills, so a limit moved after placement (dragged, or attached to a moving
  average) still gets its stop and targets at the right distance.
- Preview is exact — comes from the indicator's own settings, so it matches
  what gets submitted to the tick.
- Up to three stop/target pairs, each with its own checkbox, tick distances,
  and percentage share.
- Entry always trades the ChartTrader quantity outright (never multiplied by
  pair count); each enabled pair gets its percentage share of that quantity,
  split into whole lots as evenly as the math allows (e.g. 4 lots at
  25/25/50% → 1/1/2).
- Percentages are meant to total 100 but are renormalized across whichever
  pairs are enabled, so a disabled pair's share always lands on the others. A
  pair whose share rounds down to zero lots is skipped for that order.
- Entry type: Limit or MIT on the favorable side of the market, Stop-Market
  or Stop-Limit (with a tick offset) beyond it.
- Time in force: Day (default) or GTC, applied to the entry and every
  stop/target.
- "Separate stacked stops": nudges colliding stop prices one tick apart so
  each stays individually draggable.
- Tag position (left/center/right + margin); tag value shows price, tick
  distance, or money value.
- "ChartTrading ON/OFF" button (mounted in the ChartTrader panel, floating if
  ChartTrader is hidden): green = clicks place orders, gray = disabled.
- "Stops to BE" button: moves every working protective stop on the
  instrument — whatever tool placed it — to the position's average fill
  price plus an offset, clamped inside the market; survives
  recompiles/reloads, leaves stop-entry orders alone, never loosens an
  existing breakeven-or-better stop. Stop-limits keep their
  trigger-to-limit offset.
- Auto breakeven (off by default): moves stops to breakeven once price runs
  a configured tick trigger in the position's favor, once per position,
  re-arms on close/flip; shares the offset setting with the button.
- No live-account gate — whichever account is selected in ChartTrader is
  what a click trades; the ON/OFF button is the only switch.
- Auto-breakeven only runs while the chart is open (local automation, not
  server-side).
- Removing the indicator leaves its working orders working.
- Don't enter the same instrument through another automated bracket/order-
  management feature at the same time — two systems will fight over the
  same position's exits.
- Use: add to a chart with ChartTrader visible, set your bracket pairs, turn
  ChartTrading ON, hold Shift/Alt to preview, click to trade.

### ATRRenkoSizeCalculator — ATR sized for Renko bricks

- ATR smoothed with an EMA (not Wilder's average), plotted with its half
  value.
- On-chart table: ATR, half ATR, half ATR in ticks.
- "Ignore gaps" (default on) excludes the session-open gap from the true
  range.
- Settings: ATR length, decimal places, table and half-ATR toggles.
- Use: add to the instrument you trade, read "Renko Size" (half ATR in
  ticks) as the brick size.

### MACDHistogram — momentum-colored MACD histogram

- Bright color when momentum grows (rising above zero, falling below), dark
  when it fades back toward zero, neutral color at exactly zero.
- All five colors configurable, persist across workspace reloads.
- Histogram bar width matches the chart's bar width.
- Settings: fast/slow/smooth periods (12/26/9 defaults) plus the colors.
- Use: add to a chart; defaults give the standard 12/26/9 histogram in its
  own panel.

### VolumeWithEMA — volume vs its average

- Volume histogram with an EMA of volume plotted over it.
- Bars colored by relation to the EMA (above/below colors configurable,
  persist across reloads).
- EMA period configurable (14 default).
- Updates tick by tick.
- Use: add to a chart, watch for above-average bars to confirm moves.

### MultiSeriesEMA — an EMA from a different bar series

- Overlays an EMA computed on a bar series independent of the chart it's
  on (different Renko brick size, higher timeframe, tick/range bars), never
  drawing that series.
- "Source Type": Minute, Renko, Tick, Range, or Day. Renko swaps in a
  brick-size-in-ticks field; every other type shows a period field.
- Source series is data-only: no second panel, no extra candles/bricks.
- EMA period and line color configurable.
- Use: add to any chart, pick a source type/size different from the chart's
  own, set the EMA period.

### OrderDecorator — distances on your working orders

- Labels every working order on the chart's instrument (any tool's orders,
  not just this repo's) with its distance from the position's average
  price: ticks, optional points, optional money value for the order's
  remaining quantity.
- Profit-side orders also show their R multiple against the nearest
  working stop.
- The execution (average price) line gets its own label: position side,
  quantity, and live unrealized P&L in ticks and money.
- Colors by side: profit green, loss red; flat = neutral gray with
  distance from the last price instead.
- Orders at the same price merge into one label with the summed quantity,
  matching how the platform stacks their markers.
- "Right margin" setting (default 50 px) keeps the labels clear of the
  platform's own order markers.
- Reads the ChartTrader-selected account; labels update tick by tick.
- Use: add to a chart with ChartTrader visible; working orders get their
  numbers automatically.

### RiskRewardTargets — risk/reward drawing tool with three targets

- Drawing tool (chart's Draw menu), not an indicator: two clicks place entry
  and stop, targets appear at 1R/2R/3R.
- Every anchor — entry, stop, and each target — drags independently; each
  target's label shows its price, distance in ticks, and its R multiple,
  recomputed live from the current entry/stop distance. The built-in
  RiskReward tool instead locks target and stop to a fixed ratio.
- 1 to 3 targets ("Targets" setting); works for long and short (stop side
  decides direction).
- Lines extendable left/right; entry/stop/target line colors configurable.
- Use: Draw menu → "Risk Reward Targets", click entry, click stop, then drag
  any level; read each target's R off its label.

### RectangleMidline — rectangle with a 50% line

- Drawing tool (chart's Draw menu), not an indicator: the platform's own
  rectangle with a horizontal line across its vertical centre, the level a
  pullback into the zone is measured against.
- Draws, drags, resizes by the corners and alerts exactly like the built-in
  Rectangle; fill, opacity and outline are the same settings.
- Midline has its own on/off, color, width and dash style ("Midline" group).
- The line sits at the geometric centre of the box, not at the nearest tick,
  so it stays centred when the box spans an odd number of ticks.
- Use: Draw menu → "Rectangle Midline", drag out the zone.

### RenkoSizeTable — Renko box sizes across timeframes at once

- Same sizing method as ATRRenkoSizeCalculator, but a table with one row
  per timeframe instead of a single value for the chart you're on.
- Four configurable timeframes (minutes, default 1/5/15/60), each computed
  on its own secondary bar series — no panel, no bars drawn, only the table
  shows.
- Each row: timeframe, ATR (points), half ATR (points), half ATR (ticks),
  "Med ATR" (points) and "Med Ticks" — the median of that row's last-N
  daily ATRs and half of it in ticks. ATR/Ticks are the live exponential
  read; the Med columns hold steady through outlier days (one hot session
  out of ten doesn't move them), so Med Ticks is the more stable
  brick-size pick on volatile instruments.
- Each timeframe has its own ATR lookback in days (defaults: 2min/3d,
  5min/5d, 15min/10d, 60min/20d), converted to a bar count from how many
  bars that timeframe's sessions actually hold. ATR smoothing is
  exponential. The chart needs at least the largest configured day count
  of data loaded, plus one.
- "Ignore gaps" (default on).
- Settings: per-timeframe days, decimal places, top margin (default 40 px,
  keeps the table clear of the chart's top-right toolbar icons).
- Use: add to any chart, set the timeframes, read "Ticks" off the row you're
  sizing a Renko chart for.

### CustomVWAP — anchored VWAP with deviation bands

- Volume-weighted average price with up to three standard-deviation band
  pairs (multipliers configurable, 0 hides a pair; defaults 1 and 2).
- Anchor per instance: daily session or weekly (Sunday-keyed). Add the
  indicator twice for a daily and a weekly line side by side.
- Optional RTH window (on by default, 930–1600 exchange time, end
  excluded): only regular-hours trades accumulate, even on an ETH chart —
  outside the window the lines hold flat. With the window off, the whole
  session accumulates and the daily anchor resets at the session roll,
  not at midnight.
- The window is compared in the instrument's exchange time zone, so it
  stays correct whatever time zone the platform is configured to.
- Computed from a tick series at a configurable granularity (default 10
  ticks); smaller is more precise and loads more data. The chart must have
  enough days loaded to cover the current week for the weekly anchor to be
  complete.
- Use: add to the chart you trade; set the anchor, toggle the RTH window
  to taste, and size the bands with the deviation multipliers.

### Candle Countdown & Position Sizer — clock, candle countdowns, lot size

- NinjaTrader port of the TradingView tool of the same name: an on-chart
  table with the current time, live countdowns to the next candle on up to
  three timeframes, and an ATR-based position-size suggestion.
- Countdown timeframes are entered in minutes (defaults 1/5/15); candle
  boundaries are anchored to the Unix epoch (UTC), so both platforms flip
  on the same second. A countdown inside the alert lead time (default 10 s)
  switches its cell to the alert colors and stays there.
- Risk sized as a fixed dollar amount or a percent of account — the
  settings panel only shows the fields of the selected mode; stop
  distance is a multiple of ATR (EMA or Wilder smoothing); the Lot row is
  the contract count that keeps the loss at the risk budget if that stop is
  hit, from the instrument's point value. Percent mode adds a Stop Cash row
  with the dollars actually at risk at the rounded lot count.
- Clock timezone selectable (Exchange, UTC, New York, London, Tokyo,
  Sydney); Exchange means the instrument's own trading-hours zone. Table
  corner, margin from the viewport edges, and colors configurable; the
  ATR and Stop ATR rows can be hidden; text uses the chart's label font.
- "Auto-Set ChartTrader Quantity" (off by default) mirrors the suggested
  lot into ChartTrader's quantity field whenever the suggestion changes,
  so orders placed from ChartTrader trade that size with no retyping. It
  only ever writes a size of 1 or more, only while live, and a size you
  type by hand survives until the ATR actually moves the suggestion. It
  reaches an unofficial corner of the platform; if an update moves it,
  the indicator logs one line and leaves the field alone.
- Use: add to the chart you trade; confirm the point value against your
  broker's contract specs before trusting the lot size.

### Time-Based Price Levels — lines at the prices of key times

- NinjaTrader port of the TradingView tool of the same name: horizontal
  lines at up to 10 price levels, each anchored to an intraday clock time
  or to a higher-timeframe period (D, W, M, 3M, 6M, 12M).
- An intraday slot marks the O/H/L/C of the bar spanning that time each
  day, on any chart timeframe, with an end-of-day cutoff and a days-of-
  history control. An HTF slot tracks the developing period's O/H/L/C
  live from the chart's own bars — no secondary series.
- Labels are generated from the slot (D.O, W.O, ... / the time for
  intraday, with a price suffix when it is not the Open). Label font and
  how far the line and its label run past the last bar are configurable.
- Per-slot on/off, time, price type, and stroke (color, width, dash);
  timezone selectable (New York default) for the intraday times. Period
  slots roll with the instrument's trading day, so a daily level on an
  overnight instrument resets at the session open, not at midnight.
- Use: add to an intraday chart, type each level's time or period into its
  slot; a slot that fails to parse is skipped and logged, never fatal.

### Time-Based Vertical Lines — the day's times, ahead of time

- NinjaTrader port of the TradingView tool of the same name: vertical lines
  at the clock times that matter during a session, drawn for the whole day,
  so the ones still ahead are already on the chart with a countdown to them.
  The macro windows that the TradingView version carries live in the ICT
  Macros indicator instead.
- Ten line slots (on/off, HH:MM, label, stroke). NY midnight, the 08:30
  news and the 09:30 open start on; the close, futures open, London
  hours, FOMC and a spare fill the other slots, off.
- Labels are pinned to the viewport near the top or bottom edge (or the
  middle), riding there through scroll and zoom at no cost in chart scale —
  unlike the TradingView version, which had to give up price range for the
  room.
- Days of history counted from the last day with bars (a weekend never
  strands the lines); "Include Tomorrow" adds the next day late in the
  session. A time past the last bar is placed by extending the recent bars'
  own pace, so on Renko/tick charts a future line's spot is an estimate.
- Use: add to the chart you trade; times are read as HH:MM in the selected
  zone, and an entry that does not parse is skipped and logged.

### ICT Macros — the macro windows, bracketed and counted down

- The ICT macro windows as first-class chart objects: each window bracketed
  by a vertical line at its start and end, joined by a captioned band whose
  caption counts down to the window ("in 4:32") and then counts out what is
  left of it ("12:10 left"). Windows are drawn for the whole day, the ones
  still ahead included.
- Twelve slots, each with its own label ("Final Hour", "MOC" and the
  London pair come pre-named; an empty label captions with the window's
  times). The on-by-default set is the tradeable one: the six :50-:10
  windows inside regular hours plus the Final Hour (15:15-15:45) and
  Market On Close (15:45-16:00) windows. The pre-market pair (07:50,
  08:50) and the two London macros are carried but start off.
- Bands ride a strip pinned to the bottom (or top) of the viewport — bottom
  by default so the strip never collides with Time-Based Vertical Lines
  captions at the top when both are on the same chart.
- Same timezone, history and include-tomorrow controls as Time-Based
  Vertical Lines; one stroke sets the color, width and dash of every
  window's lines and band border, with fill opacity separate.
- Use: add to the chart you trade; on anything but NY-time instruments set
  the zone accordingly.

### ICT Killzones — a box around each killzone's candles

- A translucent box hugging each killzone's candles: from the window's
  first bar to its last, from its running high to its running low, growing
  with the session while the window is open — the killzone-shading style
  familiar from TradingView.
- Eight zone slots (on/off, name, start, end, color). On by default, in
  New York time: Asia Range 20:00-00:00, London Open KZ 02:00-04:00,
  NY Open KZ 07:00-09:00, London Close KZ 10:00-12:00 (the index-futures
  readings). The session blocks — AM, Lunch, PM, CBDR — fill the other
  four slots but start off.
- A window may run past midnight (the Asia Range does); it stays keyed to
  the day it starts on. Fill opacity is global; days of history
  configurable (default 5).
- Use: add to the chart you trade; each slot's name field is a reminder of
  what it marks, nothing is drawn from it.

### Breakeven Stops — every stop to breakeven, one button

- A "Stops to BE" button in the ChartTrader sidebar (floating on the
  chart when ChartTrader is hidden) that moves every working protective
  stop on the chart's instrument to breakeven — whatever placed the
  stop: an ATM, an OCO bracket, a manual order, another tool. Meant for
  hand-managed market-order trades; ChartTrading carries the
  automatic-trigger variant of the same move.
- Only stop-type orders on the position's exit side move; a stop already
  at or beyond breakeven is never loosened, and the new price is clamped
  one tick inside the market. Acts on whatever account ChartTrader has
  selected at the click.
- One setting: breakeven offset in ticks (profit direction; 2 locks two
  ticks, 0 is exact breakeven).
- Use: add to the chart you trade with ChartTrader visible; watch the NT
  log, every outcome (moved, skipped, nothing to move) is logged.

### Economic Calendar — the week's releases, on the chart

- The week's economic-calendar events pulled straight from Forex Factory's
  public weekly feed: a vertical line at each release (future ones placed
  ahead of the last bar), a caption naming it (impact letter, currency,
  title), and a corner table of the day's or week's list with past
  releases grayed out. The feed refreshes hourly.
- Impact filter (high / medium / low / holidays) with impact-colored lines
  and text; currency filter with an Automatic mode that follows the
  chart's instrument — both sides of a forex pair, otherwise the
  instrument's denomination currency.
- Chart history: today, this week, or a manual number of days back;
  lines drawable for future releases only or past ones too.
- Times print in the platform's own display timezone — the same clock as
  the time axis — so there is no timezone setting to keep in sync (the
  TradingView original needs one because a Pine script cannot read the
  chart's zone).
- Table times are 24-hour; needs an internet connection, and a failed
  fetch keeps the previous list and retries in a few minutes.
- Use: add to the chart you trade; with Automatic currency on an NQ chart
  you get the USD calendar.

## Profit Chart (`profit-chart/`)

- Confluence coloring system: paints each candle by how many rules agree
  (major/minor MACD trend, tape reading, EMA pullback, rejection).
- Companion tools: signal labels, signal board, tape-reading and MACD
  histograms, MA cloud, day-open marker, Renko size calculator.
- Full per-indicator documentation, in Portuguese:
  [`profit-chart/README.md`](profit-chart/README.md).

## TradingView (`tradingview/`)

### Candle Countdown & Position Sizer

- On-chart table: current time, live countdown to the next candle close and
  to the next higher-timeframe close.
- Position sizer: turns account risk and stop distance into a contract/share
  quantity.

### Time-Based Price Levels

- Up to 10 horizontal price levels.
- Each anchored to an intraday time (any HH:MM, your time zone) or a
  higher-timeframe open (daily, weekly, monthly, quarterly, semi-annual,
  yearly).
- Extended through the session with an end-of-day cutoff.

### Renko Size Table

- On-chart table of suggested Renko brick sizes (half ATR, in price and in
  ticks) across four timeframes at once — the TradingView port of the
  NinjaTrader indicator of the same name.
- Two reads per row: the live EMA ATR, and the median of each recent day's
  mean true range, which holds steady through an outlier session.
- Each timeframe has its own ATR lookback in days; "Ignore gaps" excludes
  the session-open gap.
- Place it on a chart timeframe at or below the smallest row (a 1-minute
  chart for the 1/5/15/60 defaults) — TradingView can only feed a
  higher-or-equal timeframe into each row; a row below the chart is flagged
  instead of showing a wrong number.

### Time-Based Vertical Lines

- Vertical lines at the clock times that matter during a session — New York
  midnight, the 09:30 open, a news release. The companion to Time-Based
  Price Levels: that one answers "at what price", this one "at what time".
- 10 line slots, one compact row each: on/off, HH:MM, label, color, style.
- Every line is drawn for the whole day, **including the ones still ahead**,
  each carrying a countdown to it.
- 12 more slots for macro windows, defaulting to the ICT ones grouped by
  session: 07:50–08:10 and 08:50–09:10 ahead of the bell, 09:50–10:10 and
  10:50–11:10 in the AM session, 11:50–12:10 and 12:50–13:10 over lunch,
  then 13:50–14:10, 14:50–15:10, 15:15–15:45 and 15:45–16:00 through the PM
  session. Two spare rows hold the London macros (02:33–03:00,
  04:03–04:30), switched off.
- Each macro has its own on/off and its own start and end time, is
  bracketed by a line at both ends, and has those two lines joined by a
  rectangle carrying its name: `Macro 09:50-10:10`. The caption changes
  tense with the window — counting down to the open while it is ahead,
  counting out what is left once inside it.
- Captions and macro rectangles share one strip below (or above) the bars,
  set clear of them by an adjustable gap, welded there through any scroll
  or zoom. Nothing is drawn over the candles. Captions read across rather
  than
  down the line: a script cannot use the chart's own vertical-line drawing,
  and a Pine label has no rotation, so short captions work best.
- History days are only drawn if the chart has bars for them, so a past
  weekend never collects a set of lines nothing traded under. Today is
  always drawn whether or not it has bars yet, and tomorrow too if you
  switch it on — their lines are the ones still ahead, which is the point
  of them.
- To keep the lines still ahead on screen, widen the chart's right margin
  (Chart settings → Appearance → Right margin, or drag the chart left) — a
  line an hour out sits an hour's worth of bars past the last one, and
  TradingView will not scroll there on its own.

Use: paste a `.pine` file into TradingView's Pine editor, save, add to the
chart.

## Vendor documentation is not included

NinjaTrader's developer docs and Nelogica's NTSL manual belong to their
vendors and are **not** part of this repository. Tooling to build your own
local copies:

- `ninja-trader/scrape_ninjatrader_docs.py` mirrors developer.ninjatrader.com
  to local markdown; `ninja-trader/build_llms_index.py` indexes the mirror.
- `profit-chart/convert_to_md.py` converts your own copy of the NTSL manual
  PDF to markdown.

Their outputs are gitignored and stay on your machine.

## Disclaimer

Built for my own personal trading, shared as-is. **No responsibility** taken
for use by anyone, for any losses, missed trades, or misbehavior — use
entirely at your own risk. Not financial advice; the code may contain bugs.
Trading involves substantial risk of loss. Test in simulation before trading
real money, and read the code before you trade with it.

## License

[MIT](LICENSE)
