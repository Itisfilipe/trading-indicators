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
bar-spacing hotkey handlers. Drag-to-pan simulates holding Ctrl so the
platform's native pan engages; the key press is session-wide, so a crash
mid-drag can leave Ctrl logically held down -- the setting's description
spells out that trade-off.

### ChartTrading

Click-to-trade: hold Shift (buy) or Alt (sell) to preview a full order
bracket at the mouse -- drawn like the platform's own working-order markers --
and click to place it. Includes sidebar ON/OFF and stops-to-breakeven
buttons, and an optional auto-breakeven trigger. Details:
[ChartTrading/README.md](ChartTrading/README.md).

### ATRRenkoSizeCalculator

ATR smoothed with an EMA instead of Wilder's average, plotted with its half
value and shown in an on-chart table: ATR, half ATR, and half ATR in ticks --
the number to feed a Renko brick size. "Ignore gaps" (on by default) keeps
session-open gaps out of the true range so overnight jumps do not inflate
the suggested size.

### MACDHistogram

Just the MACD histogram, colored by momentum: bright when the move is
strengthening (rising above zero, falling below), dark when it is fading
back toward zero. Colors are configurable and the bars match the chart's
bar width.

### VolumeWithEMA

Volume histogram with an EMA of volume; bars color differently above vs
below the average, so activity spikes stand out. Updates tick by tick.

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
