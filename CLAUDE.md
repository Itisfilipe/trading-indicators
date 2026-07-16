# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

Trading indicators for three platforms, one folder each:

- `ninja-trader/` — NinjaTrader 8, C# (NinjaScript). Indicators, a custom bars
  type + chart style (RenkoWicks), and the ChartTrading click-to-trade tool.
  Design decisions live in commit messages and code comments; features and
  usage for every indicator live in the root `README.md`.
- `profit-chart/` — Nelogica Profit Chart, NTSL. Has its own `CLAUDE.md` scoped
  to NTSL development.
- `tradingview/` — Pine Script files, self-contained.

## The critical constraint: no NinjaTrader compiler here

NinjaTrader 8 is Windows-only; C# in this repo cannot be compiled or run in this
environment. The owner compiles and tests in NT and reports errors back (often as
a CSV export from the NinjaScript editor). Before handing code over:

- Check brace/paren balance and that files are **pure ASCII** — the `.cs` files
  carry no BOM, so a literal non-ASCII glyph gets mojibaked by editors assuming a
  local codepage. Use `\uXXXX` escapes instead.
- Watch namespace ambiguities: these files import both WPF (`System.Windows.*`)
  and SharpDX namespaces, which BOTH define `Brush`, `Point`, `PathGeometry`,
  and `SolidColorBrush`. Fully qualify or the file will not compile.
- For bars-type / price logic, verify behavior by porting the algorithm to a
  Python simulation and property-testing it (see the reflection/negation test
  approach: any tick sequence and its price mirror must produce mirrored bricks).

## Evidence hierarchy for NinjaTrader APIs

NinjaTrader's official documentation has been proven wrong more than once in
this repo (it claims `ChartStyle.OnRender` is `protected` — the base is
`public`, `protected` is CS0507; it claims BarsType `Icon` is a required
override — `BarsType` has no `Icon`, overriding it is CS0115). Rank evidence:

1. **NinjaTrader's own shipped source** — `@`-prefixed files under
   `bin/Custom/**`, mirrored on GitHub (e.g. `beckerben/NinjaTrader`). This
   compiles by definition and wins every conflict.
2. Working third-party code on GitHub and NinjaTrader forum staff replies.
3. The local docs mirror (`ninja-trader/docs/`, build it with the scraper below)
   — accurate as a mirror, but it faithfully reproduces NinjaTrader's errors.

## NinjaTrader platform quirks that cost real debugging time

- **Chart styles and bars types register at NinjaTrader startup.** After
  compiling a new one, the platform must be restarted or it won't appear in the
  selection lists.
- **Bars only rebuild on chart reload (F5).** After changing a bars type, the
  bricks on screen still reflect the previously compiled logic until reload.
- **Do not hand-write the "NinjaScript generated code" region** — the NT editor
  regenerates it on compile, and hand-written versions break when custom types
  (enums) are referenced from namespaces that cannot see them.
- RenkoWicks registers bars type and chart style under the shared id **2588**
  (`TYPE_ID` in both files); the pair must stay in sync.
- UI event handlers attach in `State.Historical` through
  `ChartControl.Dispatcher`, detach idempotently in `State.Terminated`, and key
  hooks belong on the chart **window** (panel key events need focus).
  `ForceRefresh()` alone repaints late; pair it with
  `ChartControl.InvalidateVisual()` for pointer-tracking visuals.
- Mouse coordinates are WPF units; `ChartPanel`/`ChartScale` work in device
  pixels. Convert with `ChartingExtensions.ConvertToVerticalPixels(...)` before
  `chartScale.GetValueByY(...)`, then `MasterInstrument.RoundToTickSize(...)`.

## Vendor documentation is never committed

`ninja-trader/docs/`, `ninja-trader/llms*.txt`, `profit-chart/ManualNTSL.pdf`,
and `profit-chart/docs/` are copyrighted vendor content. They are gitignored,
were scrubbed from git history, and must stay local-only. The tooling to build
them is part of the repo:

```bash
cd ninja-trader
pip install -r requirements.txt
python3 scrape_ninjatrader_docs.py   # mirrors developer.ninjatrader.com -> docs/
python3 build_llms_index.py          # regenerates llms-full.txt topic index
```

Search the mirror with `grep -i "keyword" ninja-trader/llms-full.txt`, then read
`ninja-trader/docs/<namespace>/<topic>.md`.

## Repository habits

- Commit and push every coherent change without asking; commit messages explain
  the why, with no ticket prefixes and no co-author trailers.
- User-visible changes get an entry in `CHANGELOG.md` (dated, described in
  chart-behavior terms, not internals).
- READMEs are for GitHub visitors: what the tool does and the non-obvious
  considerations for using it — never design decisions, rationale, milestone
  status, references, or implementation details. That material belongs in
  commit messages and code comments. Per-indicator features and usage are
  consolidated in the root `README.md` (the home page); platform READMEs
  carry install steps and platform quirks only.
