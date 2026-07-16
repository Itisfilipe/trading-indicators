# NinjaTrader 8

C# / NinjaScript projects for NinjaTrader 8. Each folder is one importable project.

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

## Projects

### RenkoWicks

Renko bars type plus matching chart style. Each completed brick keeps the real
counter-trend extreme that traded while it formed -- up bricks carry lower
wicks, down bricks carry upper wicks -- while the trend side stays pinned to
the boundary the brick closed on, so gap overshoot is never misattributed. The
synthetic bricks that span multi-brick price jumps render faded ("Gap brick
opacity" setting) and carry zero volume.

Because it restates the forming brick with `RemoveLastBar()`, Tick Replay is
unavailable for this bars type (a platform-wide constraint for such bars).

### ErgonomicCharts

Scroll-wheel zoom without holding Ctrl, driven through NinjaTrader's own
bar-spacing hotkey handlers. Optional drag-to-pan (off by default) simulates
holding Ctrl so the platform's native pan engages; the key press is
session-wide, so a crash mid-drag can leave Ctrl logically held down -- the
setting's description spells out that trade-off before you enable it.

### ChartTrading

Work-in-progress click-to-trade tool (live bracket preview while holding a
modifier key; order submission not yet implemented). Design and status:
[ChartTrading/README.md](ChartTrading/README.md).

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
