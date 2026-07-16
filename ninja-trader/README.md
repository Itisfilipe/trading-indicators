# NinjaTrader 8

C# / NinjaScript projects for NinjaTrader 8. Each folder is one importable
project; what every indicator does and how to use it is documented in the
[root README](../README.md).

## Installing

Copy a project's `.cs` files into the matching folders under
`Documents/NinjaTrader 8/bin/Custom/` (`Indicators/`, `BarsTypes/`, `ChartStyles/`)
and compile in the NinjaScript Editor (F5). Alternatively, export from a machine
that has them installed and import the zip via Control Center > Tools > Import >
NinjaScript Add-On.

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
