# ChartTrading — click-to-trade for NinjaTrader 8

Hold a modifier key over the chart to see exactly where an order bracket would
land — entry at the mouse, stop and profit target(s) drawn like NinjaTrader's
own working-order markers — then click to place it. Inspired by the click
trading in Nelogica's Profit Chart and tools like Volaty's Clicker.

- **Shift + move** previews a buy bracket, **Alt + move** a sell bracket
  (both keys configurable). The preview tracks the pointer in real time and
  labels the exact order type a click would submit (`LMT`, `MIT`, `STP`,
  `STP LMT`, `MKT`), inferred from which side of the market you point at.
- **Click** submits the entry to the ChartTrader account. Stop and target
  orders go live only after the entry fills, each stop/target pair OCO-linked,
  so a resting exit can never open a position.
- The preview is exact: the bracket comes from this indicator's settings, so
  what you see is what gets submitted, to the tick.

## Sidebar buttons

Two buttons mount into the ChartTrader panel (or float on the chart if
ChartTrader is hidden):

- **ChartTrading ON/OFF** — the single gate. Green: clicks place orders.
  Gray: keys and clicks do nothing, freeing the modifiers for other tools.
- **Stops to BE** — moves every working ChartTrading stop on the instrument
  to the position's average fill price (plus the configured offset), clamped
  so a stop never crosses the market. Works after recompiles and reloads, and
  only ever touches stops this tool created.

## Bracket and entry settings

- **Bracket pairs** — up to three stop/target pairs, each with its own
  checkbox and tick distances. The ChartTrader quantity sizes *each* enabled
  pair: three enabled pairs at quantity 1 place a 3-lot entry with three
  1-lot exits on each side.
- **Entry types** — choose Limit or MIT for clicks on the favorable side of
  the market, Stop-Market or Stop-Limit (with a tick offset) beyond it.
- **Separate stacked stops** — when two pairs share a stop price, nudge each
  extra stop one tick further out so the chart shows them as individually
  draggable orders instead of one stacked marker.
- **Appearance** — tag position (left/center/right), and whether stop/target
  tags show the price, tick distance from entry, or money value.

## Auto breakeven

Optional (off by default): once price runs a configured number of ticks in
the position's favor, all working ChartTrading stops move to breakeven
automatically — the same move as the Stops-to-BE button, fired once per
position and re-armed when the position closes or flips. **Breakeven offset
(ticks)** shifts where breakeven lands (e.g. 2 locks two ticks of profit) and
applies to both the automatic move and the button.

## Worth knowing before trading with it

- **Live accounts are refused by default.** Orders only go to accounts named
  `Sim*`/`Playback*` unless you deliberately enable "Allow live accounts".
- Automation runs locally, like an ATM: breakeven only acts while the chart
  is open in NinjaTrader.
- Removing the indicator leaves its working orders working, by design.
- Time-in-force is Day. An entry cancelled after a partial fill leaves that
  partial without a bracket (a warning is logged) — watch the Orders tab.

## Installing

Copy `Indicators/ChartTrading.cs` into `Documents/NinjaTrader 8/bin/Custom/Indicators/`
and compile in the NinjaScript Editor (see [../README.md](../README.md)), then
add the ChartTrading indicator to a chart.

`IMPLEMENTATION_PLAN.md` documents the design and development status.
