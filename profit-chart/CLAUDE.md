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

## Architecture: Confluence Coloring

The main indicator (`confluence-coloring.ntsl`) is a multi-rule coloring system that computes independent boolean signals and combines them in a priority cascade:

1. **Rules** (computed independently): Major MACD, Minor MACD, Tape Reading, EMA Pullback, Keltner Reversal
2. **Cascade** (highest priority wins): REVERSAL (white) → ENTRY (blue) → TAPE (green/red) → MACD BOTH (medium green/red) → MACD MAJOR (dim green/red)
3. **Toggles**: `Enable_Tape`, `Enable_EMA`, `Enable_Keltner` control which color tiers appear (signals always compute)

To add a new rule: add inputs, add variables, compute a boolean signal, insert into the cascade at the appropriate priority level.
