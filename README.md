# Trading Indicators

Custom indicators and coloring rules for day trading on Renko charts, written in **NTSL (Nelogica Trading Strategy Language)** for the [Profit Chart](https://www.nelogica.com.br/produtos/profitchart) platform.

## Indicators

| File | Type | Description |
|------|------|-------------|
| `confluence-coloring.ntsl` | Coloring Rule | Multi-rule confluence system that combines MACD, Tape Reading, EMA, and Keltner signals into a priority-based bar coloring cascade |
| `ma-cloud.ntsl` | Indicator | Moving average cloud (EMA envelope) |
| `ma-cloud-coloring.ntsl` | Coloring Rule | Bar coloring based on MA cloud position |
| `macd-histogram.ntsl` | Indicator | MACD histogram |
| `tape-reading.ntsl` | Indicator | Tape reading (order flow) signals |
| `day-open.ntsl` | Indicator | Daily opening price reference line |

## Setup

NTSL files are compiled directly inside Profit Chart:

1. Right-click on a Renko chart
2. Select **Insert Indicator** or **Insert Coloring Rule**
3. Create a new strategy and paste the `.ntsl` source code
4. Compile and apply

## Documentation

The `profit-chart/docs/` directory contains the full NTSL language reference (27 chapters, in Portuguese), converted from the official Nelogica manual.

## License

This project is provided for personal and educational use.
