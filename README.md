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
  volume.
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
  per pair.
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
- "Stops to BE" button: moves every working ChartTrading stop to the
  position's average fill price plus an offset, clamped inside the market;
  survives recompiles/reloads, only touches stops it created, never loosens
  an existing breakeven-or-better stop.
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

### RenkoSizeTable — Renko box sizes across timeframes at once

- Same sizing method as ATRRenkoSizeCalculator, but a table with one row
  per timeframe instead of a single value for the chart you're on.
- Four configurable timeframes (minutes, default 2/5/15/60), each computed
  on its own secondary bar series — no panel, no bars drawn, only the table
  shows.
- Each row: timeframe, ATR (points), half ATR (points), half ATR (ticks).
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
- Use: paste a `.pine` file into TradingView's Pine editor, save, add to the
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
