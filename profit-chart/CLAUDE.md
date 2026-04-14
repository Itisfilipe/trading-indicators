# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Nelogica Profit Chart indicators and coloring rules for Renko chart day trading, written in **NTSL (Nelogica Trading Strategy Language)** — a Pascal-like DSL with Portuguese function names.

There are no build/test/lint commands. NTSL files are compiled directly inside Profit Chart (right-click chart → insert indicator or coloring rule).

## Repository Structure

- `indicators/` — NTSL source files (indicators and coloring rules)
- `docs/` — NTSL language reference (27 chapters, Portuguese, converted from PDF)
- `ManualNTSL.pdf` — Original NTSL manual
- `.claude/agents/renko-trader-dev.md` — Specialized Renko trading agent config

## NTSL Conventions

### File Structure

Every NTSL file follows three sections: `input` (parameters), `var` (declarations), `begin`/`end` (execution per bar).

### Variable Naming

- **Floats:** `f` prefix — `fEmaFast`, `fBoxSize`, `fMajorHistogram`
- **Booleans:** `b` prefix — `bMajorBullish`, `bTapeHighlight`
- **Integers:** `n` prefix — `nCurrentColor`, `nPositiveColor`
- **Inputs:** PascalCase with underscores — `Major_MACD_Fast(73)`, `EMA_Max_Distance(3)`

### Key Language Notes

- `Close >= Open` = bullish (green) Renko brick; `Close < Open` = bearish (red)
- Bar references: `variable[1]` = previous bar, `variable[2]` = two bars ago
- Multi-statement if/else requires `begin`/`end` blocks
- Variables declared in `var` are series (retain history, support `[n]` indexing)
- `MediaExp(period, source)` = EMA; `Media(period, source)` = SMA
- `PaintBar(RGB(r,g,b))` is for coloring rules only, not indicators
- `PriorCote(index)`: 0=close, 1=open, 2=high, 3=low, 4=settlement

### Known Platform Limitations

- `HorizontalLineCustom` and `PlotN` cannot coexist in the same indicator
- `fBoxSize` can be zero on the first bar — always guard with `if fBoxSize = 0 then fBoxSize := 1`

## Architecture: Confluence System

Three synchronized files share identical signal logic — only output differs:

- `confluence-coloring.ntsl` — Coloring rule: paints bars via `PaintBar()`
- `confluence-labels.ntsl` — Indicator: plots text labels (B, S, BS, SS, BR, SR, BSR, SSR) near bricks
- `confluence-letreiro.ntsl` — Indicator: colored banner in separate sub-window

**IMPORTANT:** Signal logic must stay identical across all three files. Only output sections differ.

### Rules (computed independently)

1. **Major MACD** — long-term trend (histogram >= 0 = bullish)
2. **Minor MACD** — short-term trend (enables scalp signals)
3. **Tape Reading** — volume + aggression above MA, aligned with candle direction
4. **EMA Zone** — candle near EMA zone after pullback (lookback: 6 bars, max 2 crossed beyond slow EMA, tolerance in ticks). Split into `bEmaZoneBull`/`bEmaZoneBear` (direction-agnostic) and `bEmaBullish`/`bEmaBearish` (requires candle color)
5. **Rejection** — tape against signal direction (not candle direction) + wick >= ratio * body. Uses `bEmaZoneBull`/`bEmaZoneBear` so candle color is irrelevant — wick decides

### Signal Types

| Signal | Condition | Color (light theme) |
|--------|-----------|-------------------|
| Strong Buy/Sell | EMA zone + green/red candle + tape aligned + Major MACD agrees | Blue / Red |
| Scalp Buy/Sell | EMA zone + green/red candle + tape aligned + Major MACD opposes + Minor confirms | Light blue / Dark red |
| Rejection variants | EMA zone (any candle color) + big wick + tape against signal direction | Same colors (labels differentiate) |

### Color Cascade (highest priority wins)

Strong triggers → Scalp triggers → Rejection strong → Rejection scalp → Both MACDs agree (trend) → One MACD disagrees (soft trend)

### Toggles

- `Enable_EMA` — master toggle for all EMA-based entry signals
- `EMA_Zone_Tolerance` — ticks of tolerance for candle "touching" EMA zone (default 2, helps with candle charts)
- `Ignore_Pullback` — when true, EMA signal only needs trend direction + EMA zone touch (skips pullback check, close vs slow EMA, nCrossed limit)
- `Enable_Scalp` — when false, disables all scalp and scalp-rejection signals
- `Trend_Follow_Major` — trend color follows Major MACD (true) or Minor MACD (false)
- `Tema_Escuro` — dark/light theme colors

To add a new rule: add inputs, add variables, compute a boolean signal, insert into the cascade at the appropriate priority level. Update all three files.
