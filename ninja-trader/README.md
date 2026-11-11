# NinjaTrader 8

C# / NinjaScript for NinjaTrader 8, laid out exactly like the
`Documents/NinjaTrader 8/` folder; what every indicator does and how to use
it is documented in the [root README](../README.md).

## Installing

1. Copy the `bin` and `templates` folders from here onto
   `Documents/NinjaTrader 8/`, merging with what is there. Every file lands
   in a `FilipeAmaral` subfolder of `Indicators/`, `DrawingTools/`,
   `BarsTypes/` and `ChartStyles/`, so updating is: delete those
   `FilipeAmaral` folders, copy again.
2. Open the NinjaScript Editor and compile (F5).
3. Restart NinjaTrader when a bars type or chart style is new.

A copy of the same file left directly in `Indicators/` (an older install)
defines the same class twice and fails to compile; delete it.

The "Modern Green" chart template ([chart palette](../README.md#chart-palette))
lands in `templates/Chart/`; right-click a chart > Templates > Load. It is a complete 1-minute
candlestick setup on the CME US Index Futures ETH (full) session, with this
repo's indicators already added and configured. Loading it replaces the
chart's indicators, interval and session with the template's; the
indicators must be installed first, or NinjaTrader skips them.

Two platform quirks worth knowing:

- **Chart styles and bars types register at startup.** After compiling a new
  one, restart NinjaTrader or it will not appear in the selection lists.
- **Bars only rebuild on reload.** After changing a bars type, press F5 on the
  chart (reload all historical data); until then the bars on screen were built
  by the previously compiled logic.

## Documentation tooling

`scrape_ninjatrader_docs.py` builds a local markdown mirror of
developer.ninjatrader.com (content-hash change detection, output namespaced by
product), and `build_llms_index.py` generates a searchable topic index from it.
The outputs (`docs/`, `llms.txt`, `llms-full.txt`) are NinjaTrader's content
and therefore local-only: they are gitignored and not part of the repository.

```bash
pip install -r requirements.txt
python3 scrape_ninjatrader_docs.py
python3 build_llms_index.py
```
