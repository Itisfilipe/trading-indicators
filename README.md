# Trading Indicators

Personal collection of trading indicators and chart tools for three platforms:

| Folder | Platform | Language |
|--------|----------|----------|
| [`ninja-trader/`](ninja-trader/) | NinjaTrader 8 | C# (NinjaScript) |
| [`profit-chart/`](profit-chart/) | Profit Chart (Nelogica) | NTSL |
| [`tradingview/`](tradingview/) | TradingView | Pine Script |

## NinjaTrader 8 (`ninja-trader/`)

- **RenkoWicks** — Renko bars type and matching chart style that preserve each
  brick's real counter-trend extreme as a wick, and draw the synthetic bricks
  that fill price gaps faded, so what actually traded is visible at a glance.
- **ErgonomicCharts** — natural chart interaction: scroll-wheel zoom through the
  platform's own bar-spacing handlers, plus drag-to-pan.
- **ChartTrading** — click-to-trade: hold a modifier key to preview the full
  order bracket (entry, stop, targets) at the pointer, click to place it, with
  sidebar buttons for on/off and stops-to-breakeven plus optional auto-breakeven.
- **ATRRenkoSizeCalculator** — EMA-smoothed ATR with an on-chart table of ATR,
  half ATR, and half ATR in ticks, for sizing Renko bricks.
- **MACDHistogram** — MACD histogram with momentum-based coloring (rising vs
  falling on each side of zero), bar width matching the chart's bars.
- **VolumeWithEMA** — volume histogram colored by its relation to a volume EMA.

See [`ninja-trader/README.md`](ninja-trader/README.md) for import instructions
and platform quirks.

## Profit Chart (`profit-chart/`)

Indicators and candle-coloring rules for day trading on Nelogica's Profit Chart,
written in NTSL: a confluence coloring system (major/minor MACD, tape reading,
EMA pullback, rejection), signal labels, a signal board, and tape-reading and
MACD histograms. Documentation in Portuguese:
[`profit-chart/README.md`](profit-chart/README.md).

## TradingView (`tradingview/`)

- `candle-countdown-position-sizer.pine` — candle countdown and position-sizing helper.
- `time-based-price-levels.pine` — price levels anchored to configurable times of day.

Paste into TradingView's Pine editor and add to the chart.

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
