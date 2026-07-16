# Changelog

Notable changes to the indicators in this repository. Dates follow ISO 8601
(YYYY-MM-DD); entries describe what changed on the chart, not internals.

## 2026-07-16

### ChartTrading (NinjaTrader, new)
- First public milestone of a click-to-trade tool: hold a modifier key
  (Shift = buy, Alt = sell) to preview the full order bracket — entry, stop,
  and up to three targets — at the mouse pointer. Preview only; it places no
  orders yet.
- Preview levels render like the platform's own working orders: chevron order
  tags, a line from the tag to the right edge, and a pointed price tag at the
  right. Tag placement is configurable (left / center / right with a border
  margin).
- Entry tag infers LMT vs STP from the pointer being below or above the last
  traded price; tag quantity follows the ChartTrader quantity selector.
- The stop and target tags can show the price, the tick distance from entry,
  or the money value for the current quantity ("Level value" setting).
- A "ChartTrading ON/OFF" button in the ChartTrader sidebar (floating on the
  chart when ChartTrader is hidden) disables the modifier-key gestures without
  removing the indicator, freeing the keys for other tools.
- Order submission (off by default): with "Enable order submission" on, a
  click while the preview is armed submits the entry to the ChartTrader
  account, and each enabled pair's stop and target go live as an OCO group
  once the entry fills. Live accounts additionally require "Allow live
  accounts"; otherwise only Sim/Playback accounts accept orders. The sidebar
  button shows the mode: OFF, PREVIEW, or ON.
- Configurable bracket pairs: three stop/target pairs, each toggled by its own
  "Bracket N" checkbox, each with its own stop and target. The ChartTrader
  quantity sizes each enabled pair, so the entry trades quantity x enabled
  pairs and the stops always cover the full position. Defaults center the tags
  and use 50-tick steps with two pairs enabled.

### RenkoWicks (NinjaTrader)
- **Fixed: lower wicks were never drawn.** A rendering bug present since the
  first version made every down-pointing wick silently disappear; up bricks now
  show their pull-back lows and down bricks their rally highs, as intended.
- Bricks that only exist to fill a price gap now render faded ("Gap brick
  opacity" setting) and no longer repaint colors carried by bar-override
  brushes from other scripts.

### ErgonomicCharts (NinjaTrader)
- Zoom and pan gestures now stop at the chart panel's edges (DPI-correct hit
  test), so they no longer trigger over the price/time axis strips; on
  multi-panel charts they scope to the panel the indicator is loaded on.
- Hardened the drag-to-pan key release against event suppression, closing one
  more way the synthetic Ctrl could have been left held.
- Drag-to-pan is now enabled by default (tested working); a tap of Ctrl clears
  a stuck key if the platform ever crashes mid-drag.

### Namespacing
- The custom indicators now live under the `FilipeAmaral` sub-namespace, so
  they group together in NinjaTrader's lists as first-party work.

### Repository
- Open-sourced properly: MIT license, English root README, per-platform
  documentation, and this changelog.
- Vendor documentation (NinjaTrader developer docs, Nelogica NTSL manual)
  removed from the repository and its history; the tooling to build local
  copies remains.

## 2026-07-15

### RenkoWicks (NinjaTrader)
- **Fixed: reversals deleted real price extremes.** A brick completed by a
  reversal lost the counter-trend high/low that actually traded, understating
  ATR and stop distances downstream. Completed bricks now keep the true
  extreme; the trend side stays pinned to the boundary so gap overshoot is
  never misattributed.
- Fixed the previous session's last bar being flattened into a doji when
  Break EOD is enabled.
- Chart style fixes: bar-width changes take effect immediately, and the
  "Candle Outline" / "Candle Wick" settings color what their names say
  (previously they were applied by bar direction).

### ErgonomicCharts (NinjaTrader)
- Scroll-wheel zoom now drives the platform's own bar-spacing handlers,
  removing zoom drift (a wheel up + down no longer shrinks the chart) and
  respecting the user's bar-spacing ratio.
- Drag-to-pan is now off by default and hardened when enabled: the synthetic
  Ctrl key is shared across charts, released on window deactivation and lost
  capture, and never fights a physically held Ctrl. A crash mid-drag can still
  leave Ctrl held — that trade-off is documented in the setting.

### Documentation tooling (NinjaTrader)
- Docs scraper rewritten: content-hash change detection, per-product output
  folders, no browser dependency, and a generated topic index.

## Earlier

- Initial collection: Profit Chart (NTSL) confluence coloring system, signal
  labels, signal board, tape-reading and MACD histograms; TradingView Pine
  scripts (candle countdown / position sizer, time-based price levels); and
  the first NinjaTrader ports (RenkoWicks, ErgonomicCharts, ExponentialATR,
  MACDHistogram, VolumeWithEma).
