# LRM setup for Classic Web Trader

[Guia de migração em português — configurações do layout MNQ e cinco retângulos](GUIA_DE_MIGRACAO.md)

## Install

1. Add **Code Editor** to the Classic workspace. Create one JavaScript file for each of [LRMMap.js](LRMMap.js), [LRMCountdown.js](LRMCountdown.js), [LRMRectangle.js](LRMRectangle.js) and [LRMFibProjection.js](LRMFibProjection.js). Copy each file in full and save. Update any existing files in place. Save the five LRMRectangle color variants in the same folder to list each color separately in Drawing Tools.
2. Open two candlestick charts of the same MNQ contract and expiry. Select the full electronic session, set the left chart to **1 minute**, the right to **15 minutes**, and add **LRM Map** and **LRM Countdown** from Indicators to each chart.
3. In Drawing Tools select **LRM Rectangle** or **LRM Fib Projection**. A rectangle takes two opposite corners. For the projection click A, B, then C. Drag C to move the target zone. For the separate pullback measurement, configure the native Fibonacci retracement with 61.8% and 78.6%.
4. To share chart drawings, assign both modules the same link color and enable **Drawing Tools → Sync Charts** on each. Verify a custom rectangle by moving one corner on the 15m chart and checking the 1m chart. [Classic sync guide](https://vendor-support.ninjatrader.com/s/article/How-Do-I-Synchronize-Drawing-Tools-in-NinjaTrader-Web-Charts?language=en_US)

## Match the light chart

Use a white background and hide the background grid. In the chart element editor, select the instrument to set candle colors. Apply the same choices on both charts.

| Chart element | Color |
| --- | --- |
| Background | `#FFFFFF` |
| Up candle body / outline | `#5B9CF5` / `#0C3299` |
| Down candle body / outline | `#9C9C9C` / `#000000` |
| Wicks | `#000000` |
| Manual horizontal swing line | `#613CB0` |
| Countdown panel / digits | `#3B4352` / `#FFFFFF` |

The red, orange, cyan, purple and gray rectangle presets use a solid midpoint, a lighter border and 10% fill. Quarters add 25% and 75%. Eighths draw each internal 12.5% level. The Portuguese guide includes the border, midpoint and fill colors for every preset. The gray fill appears near `#E6E6E6` over white. The map uses the exact session colors from the Pine scripts. On a white chart, Asia is blue, London purple, pre-market orange, NY AM teal and NY PM red.

## Data and timing

- The map reads the bars loaded in each chart. Load history back through the prior month boundary for the current monthly range, and through a full prior month for PMH/PML. An initial partial period is skipped. Session windows require their opening bar. Each chart places a level at the resolution of its own candles.
- Day, week and month boundaries use the futures trading date. The date rolls at 18:00 New York time. A horizontal open needs a bar at its configured minute. Gap levels use a 17:00 close and an 18:00 reopening. Early closes and missing edge bars leave that gap unmarked.
- Upcoming vertical markers assume continuous minute bars up to the event. On 15m the 15:50 MOC marker falls inside the 15:45 candle. Its position is interpolated. Future labels show minutes from the latest bar.
- Countdown reads **time remaining / contract count / EMA of true range**. It refreshes on quotes. The timer uses the live clock and shows a dash for a historical bar. Automatic point values are $2 for MNQ and $20 for NQ. The starting settings match Pine: $100 risk, 1.5 × ATR and 5 bars. Zero contracts is a valid result.
- The news table visible in the reference is a third-party TradingView overlay. The Classic scripts draw price and time studies only.
