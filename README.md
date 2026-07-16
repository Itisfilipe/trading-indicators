# Trading Indicators

Personal collection of trading indicators and chart tools for three platforms:

| Folder | Platform | Language |
|--------|----------|----------|
| [`ninja-trader/`](ninja-trader/) | NinjaTrader 8 | C# (NinjaScript) |
| [`profit-chart/`](profit-chart/) | Profit Chart (Nelogica) | NTSL |
| [`tradingview/`](tradingview/) | TradingView | Pine Script |

## NinjaTrader 8 (`ninja-trader/`)

Import instructions and platform quirks (restart after compiling chart styles,
F5 to rebuild bars, and so on): [`ninja-trader/README.md`](ninja-trader/README.md).

### RenkoWicks — Renko bricks that keep their wicks

A Renko bars type plus matching chart style. Standard Renko throws away the
counter-trend price movement inside each brick; RenkoWicks keeps it as a wick,
so what actually traded stays visible.

**Features**

- Completed up bricks carry their pull-back low as a lower wick; down bricks
  carry their rally high as an upper wick. The trend side stays pinned to the
  brick boundary.
- Synthetic bricks that only exist to span a price gap render faded ("Gap
  brick opacity" setting) and carry zero volume, so they read as filler.
- "Candle Outline" and "Candle Wick" style settings color what their names
  say; bar-width changes apply immediately.
- Defaults: 50-tick bricks, 15 days of data.

**How to use:** compile both files, restart NinjaTrader (chart styles register
at startup), then pick "Renko Wicks" as the chart's bar type in Data Series;
"Brick Size" is in ticks. After changing the brick size press F5 to rebuild.
Tick Replay is unavailable for this bars type (a platform constraint for bars
that restate the forming brick).

### ErgonomicCharts — natural zoom and pan

Chart interaction the way other platforms do it, driven through NinjaTrader's
own handlers.

**Features**

- Scroll-wheel zoom without holding Ctrl, using the platform's bar-spacing
  hotkeys, so there is no zoom drift and your bar-spacing ratio is respected.
- Drag-to-pan ("Enable drag-to-pan", on by default): click-drag pans the
  chart by simulating the platform's native Ctrl-drag.
- Gestures stop at the chart panel's edges and never trigger over the price
  or time axis; on multi-panel charts they scope to the panel that hosts the
  indicator.

**How to use:** add the indicator to any chart and scroll to zoom, drag to
pan. The simulated Ctrl press is session-wide: if NinjaTrader ever crashes
mid-drag, a tap of the physical Ctrl key clears the stuck state.

### ChartTrading — click-to-trade with an exact bracket preview

Hold a modifier key over the chart to see exactly where an order bracket
would land — entry at the mouse, stop and profit target(s) drawn like
NinjaTrader's own working-order markers — then click to place it. Inspired by
the click trading in Nelogica's Profit Chart and tools like Volaty's Clicker.

**Features**

- **Shift + move** previews a buy bracket, **Alt + move** a sell bracket
  (both keys configurable). The preview tracks the pointer in real time and
  labels the exact order type a click would submit (`LMT`, `MIT`, `STP`,
  `STP LMT`, `MKT`), inferred from which side of the market you point at.
- **Click** submits the entry to the ChartTrader account. Stop and target
  orders go live against filled quantity — partial fills are bracketed as
  they happen and the exits grow with further fills — with each stop/target
  pair OCO-linked, so a resting exit can never open a position.
- The preview is exact: the bracket comes from the indicator's settings, so
  what you see is what gets submitted, to the tick.
- **Bracket pairs** — up to three stop/target pairs, each with its own
  checkbox, tick distances, and percentage share. The entry always trades
  the ChartTrader quantity outright (never multiplied by pair count); each
  enabled pair's stop/target gets its share of that quantity, split as
  evenly as whole lots allow (e.g. 4 lots at 25/25/50% → 1/1/2). Percentages
  are meant to total 100 but are renormalized across whichever pairs are
  enabled, so a disabled pair's share always lands on the others rather than
  going unprotected. A pair whose share rounds down to zero lots is skipped
  entirely for that order.
- **Entry types** — Limit or MIT on the favorable side of the market,
  Stop-Market or Stop-Limit (with a tick offset) beyond it.
- **Time in force** — Day (default) or GTC, applied to the entry and every
  stop and target it places.
- **Separate stacked stops** — when two pairs share a stop price, nudge each
  extra stop one tick further out so the chart shows them as individually
  draggable orders instead of one stacked marker.
- **Appearance** — tag position (left/center/right with a margin), and
  whether stop/target tags show the price, tick distance from entry, or
  money value.
- **Sidebar buttons**, mounted into the ChartTrader panel (floating on the
  chart if ChartTrader is hidden):
  - **ChartTrading ON/OFF** — the single gate. Green: clicks place orders.
    Gray: keys and clicks do nothing, freeing the modifiers for other tools.
  - **Stops to BE** — moves every working ChartTrading stop on the
    instrument to the position's average fill price (plus the configured
    offset), clamped one tick inside the market. Works after recompiles and
    reloads, touches only stops this tool created, and never loosens a stop
    that already sits at or beyond breakeven.
- **Auto breakeven** (off by default) — once price runs a configured number
  of ticks in the position's favor, all working stops move to breakeven
  automatically, once per position, re-armed when the position closes or
  flips. "Breakeven offset (ticks)" shifts where breakeven lands (e.g. 2
  locks two ticks of profit) and applies to the button too.

**How to use:** add the indicator to a chart with ChartTrader visible, set
your bracket pairs, click the ChartTrading button green, hold Shift or Alt to
preview, click to trade.

**Worth knowing before trading with it**

- **Live accounts are refused by default.** Orders only go to accounts named
  `Sim*`/`Playback*` unless you deliberately enable "Allow live accounts".
- Automation runs locally, like an ATM: auto-breakeven only acts while the
  chart is open in NinjaTrader.
- Removing the indicator leaves its working orders working, by design.

### ATRRenkoSizeCalculator — ATR sized for Renko bricks

EMA-smoothed ATR with an on-chart answer to "what brick size should I use?"

**Features**

- ATR smoothed with an EMA instead of Wilder's average, plotted with its
  half value.
- On-chart table: ATR, half ATR, and half ATR in ticks — the number to feed
  a Renko brick size.
- "Ignore gaps" (on by default) keeps session-open gaps out of the true
  range so overnight jumps do not inflate the suggested size.
- Settings: ATR length, decimal places, table and half-ATR toggles.

**How to use:** add it to the instrument you trade, read "Renko Size" (half
ATR in ticks) from the table, use it as the brick size.

### MACDHistogram — momentum-colored histogram

Just the MACD histogram, colored by whether the move is strengthening or
fading.

**Features**

- Bright color when momentum grows (rising above zero, falling below), dark
  when it fades back toward zero; a neutral color at exactly zero.
- All five colors configurable ("Colors" group) and they persist across
  workspace reloads.
- Histogram bars match the chart's bar width.
- Settings: fast/slow/smooth periods (12/26/9 defaults) plus the colors.

**How to use:** add to a chart; defaults give the standard 12/26/9 MACD
histogram in its own panel.

### VolumeWithEMA — volume vs its average

Volume histogram with an EMA of volume; bars color differently above vs
below the average, so activity spikes stand out.

**Features**

- Volume bars colored by their relation to the EMA (above/below colors
  configurable, persisted across reloads).
- EMA line plotted over the histogram; period configurable (14 default).
- Updates tick by tick.

**How to use:** add to a chart; watch for above-average bars to confirm
moves.

### MultiSeriesEMA — an EMA from a different bar series

Overlays an EMA computed on a bar series independent of the chart it's on —
a different Renko brick size, a higher timeframe, whatever you pick — without
ever drawing that series' bars.

**Features**

- "Source Type" selects Minute, Renko, Tick, Range, or Day for the series the
  EMA is computed on; picking Renko swaps the field for a brick-size-in-ticks
  input, any other type shows a period input instead.
- The source series is data-only: no second panel, no extra candles/bricks on
  the chart, just the EMA line.
- EMA period and line color configurable.

**How to use:** add to any chart, pick a source type and size different from
the chart's own, set the EMA period.

### RenkoSizeTable — Renko box sizes across timeframes at once

Like ATRRenkoSizeCalculator, but a table with one row per timeframe instead
of a single value for the chart you're on — no more switching charts to
check what box size a 5-min ATR would suggest versus a 15-min one.

**Features**

- Four configurable timeframes (minutes, default 2/5/15/60), each computed
  independently on its own secondary bar series — none of them get a panel
  or draw bars on the chart, only the table shows.
- Each row: timeframe, ATR (points), half ATR (points), half ATR (ticks) —
  the number to feed a Renko brick size.
- "Ignore gaps" (on by default) keeps session-open gaps out of the true
  range, same as ATRRenkoSizeCalculator.
- Settings: ATR length, decimal places.

**How to use:** add to any chart, set the timeframe list, read "Ticks" off
the row for the timeframe you're sizing a Renko chart for.

## Profit Chart (`profit-chart/`)

NTSL indicators and candle-coloring rules for day trading on Nelogica's
Profit Chart. The centerpiece is a confluence coloring system that paints
each candle by how many rules agree (major/minor MACD trend, tape reading,
EMA pullback, rejection), with companion signal labels, a signal board, tape
reading and MACD histograms, MA cloud, day open marker, and a Renko size
calculator. Full documentation, in Portuguese, with per-indicator usage:
[`profit-chart/README.md`](profit-chart/README.md).

## TradingView (`tradingview/`)

### Candle Countdown & Position Sizer

On-chart info table: current time, live countdown to the next candle close
(and to the next higher-timeframe close), plus a position sizer that turns
your account risk into a contract/share quantity from the stop distance.

### Time-Based Price Levels

Horizontal lines at up to 10 key price levels, each anchored to an intraday
time (any HH:MM in your time zone) or a higher-timeframe open (daily, weekly,
monthly, quarterly, semi-annual, yearly), extended through the session with
an end-of-day cutoff.

**How to use:** paste a `.pine` file into TradingView's Pine editor, save,
and add to the chart.

## Vendor documentation is not included

Some of this code was developed against the platforms' official documentation
(NinjaTrader's developer docs, Nelogica's NTSL manual). That content belongs to
its vendors and is **not** part of this repository. Instead, the repo ships
tooling to build your own local copies:

- `ninja-trader/scrape_ninjatrader_docs.py` mirrors developer.ninjatrader.com
  to local markdown, and `ninja-trader/build_llms_index.py` indexes the mirror.
- `profit-chart/convert_to_md.py` converts your own copy of the NTSL manual PDF
  to markdown.

Their outputs are gitignored and stay on your machine.

## Disclaimer

I built these indicators for my own personal trading and share them as-is. I
take **no responsibility** for their use by anyone, for any losses, missed
trades, or misbehavior of any kind — if you use them, you do so entirely at
your own risk. This is not financial advice, and the code may contain bugs.
Trading involves substantial risk of loss. Test everything in simulation before
trading real money, and read the code you are about to trade with.

## License

[MIT](LICENSE)
