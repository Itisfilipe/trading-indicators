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
  platform's own bar-spacing handlers, plus optional (off by default) drag-to-pan.
- **ChartTrading** — work-in-progress click-to-trade tool: hold a modifier key to
  preview the full order bracket (entry, stop, targets) at the pointer before
  committing. Preview only so far; order submission is upcoming milestone work.

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

This is trading software shared for educational purposes. It is **not**
financial advice, and it may contain bugs. Trading involves substantial risk of
loss. Test everything in simulation before trading real money, and read the
code you are about to trade with.

## License

[MIT](LICENSE)
