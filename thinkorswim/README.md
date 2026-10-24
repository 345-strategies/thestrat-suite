# TheStrat Suite for thinkorswim

A thinkScript port of TheStrat Suite v3.1.1. Free and open source under MPL-2.0, like the Pine
original. The goal is a **full port** of every Suite feature; `FULL_PORT_PLAN.md` maps each one
to a thinkScript approach, lists the hard platform limits and their equivalents, and sets the
build order. The study shipped today is layer 1 of that plan.

**Status: v0.2.0 beta, layer 1. Not yet loaded in thinkorswim.** The classifier and the presets
are checked by Python tests (below); everything else needs the in-app checks at the bottom of
this file. `TESTER_GUIDE.md` is the short version to hand to beta testers.

## Install

1. In thinkorswim: Charts → Studies → Edit Studies → Create.
2. Name it `TheStratSuite`, delete the placeholder code, and paste the contents of
   `TheStratSuite_TOS_v0.2.0.txt`.
3. OK → add it to the chart → OK. Pick a preset in the study's settings; colors are under the
   Globals tab.

## Files

| Path | What it is |
|---|---|
| `TheStratSuite_TOS_v0.2.0.txt` | The study. Paste it into a new study named `TheStratSuite`. |
| `probes/TheStratSuite_TOS_Probes_v0.2.0.txt` | Optional second study for testers. It prints labels that answer the plan's in-app questions P1, P4, P6, P10 and P22. |
| `TESTER_GUIDE.md` | What beta testers install, check and report. |
| `FULL_PORT_PLAN.md` | Every v3.1.1 feature mapped to thinkScript, the hard limits, the 12-layer build order and the in-app probes. |

## What layer 1 does

Classifier (1u/1d, 2u/2d, F2u/F2d, 3u/3d), Strat Candles and FTFC Candles bar coloring, a
timeframe strip, an FTFC label, the six Suite presets, and a diagnostics label for beta reports.

Layers 2 to 12 (probes and a code generator, HTF data core, detection engine and debug panel,
table, targets, levels and labels, stops and Take Action Windows, F2 flip highlight, preview,
alerts, packaging) are laid out in `FULL_PORT_PLAN.md`.

## Changes

- **v0.2.0 beta (2026-10-24).** First tester build. The study is now `TheStratSuite` (it was
  `TheStratSuiteLite`). New `showDiagnostics` label with the version and each timeframe's CC and
  C1 values. Custom slots name 2m, 4m, 10m and 20m as 2, 4, 10 and 20, as the TradingView Suite
  does. On a custom chart timeframe (45m, 3H) a slot below the chart reads the next standard
  timeframe instead of the chart's own, so thinkorswim never gets a custom period. The
  classifier is unchanged.
- **v0.1.0 (2026-09-28).** Layer 1 as "Lite".

## How it differs from the Suite on TradingView

- **Timeframes below the chart's are not read.** thinkorswim rejects a secondary aggregation
  below the chart timeframe, so a lower slot is clamped, shown gray, and left out of FTFC. The
  Suite also grays these cells, but its FTFC still counts them (and those votes change on
  reload), so FTFC can differ on a chart above an enabled timeframe.
- **No 12H.** thinkorswim has no 12-hour aggregation. The Futures/Crypto preset runs 1H, 4H, D, W.
  The full port synthesizes 12H from chart bars (see `FULL_PORT_PLAN.md`).
- **Futures 4H is likely misaligned in v0.2.0.** thinkorswim appears to anchor time bars at
  midnight CT, so its native 4H on futures would run 10P/2A/6A/10A/2P/6P PT (01/05/09/13/17/21 ET)
  instead of the Suite's 3P/7P/11P/3A/7A/11A PT (18/22/02/06/10/14 ET). This affects the Day Trade
  and Futures/Crypto presets on futures. Tester guide Test 3 confirms it; the full port builds
  futures 4H from chart bars.
- **Label strip instead of a table.** One colored label per enabled timeframe, highest first,
  reading `<TF> <current candle>`, then the FTFC label. It follows the Compact table's Bar State
  coloring.
- **FTFC label ignores exhaustion.** The Suite's table FTFC cell can drop a timeframe that has
  already hit exhaustion (Exhaustion Excludes from FTFC, off by default). Layer 1 has no
  exhaustion levels, so its FTFC label always uses every monitored timeframe, the same as the
  Suite's FTFC Candles paint and signal filters.
- **No Failing 2 flip highlight** on FTFC Candles yet.
- **Higher-timeframe bars are built by thinkorswim**, not by the Suite's straddle/glue/+12h
  logic. They must be checked against TradingView (checklist below) before anything is trusted.
- **No Heikin-Ashi.** TheStrat reads traded highs and lows. Use regular candles or bars.

## Classifier and preset parity

`grammar/tests/test_tos_parity.py` reads the study file itself and evaluates it with a small
interpreter:

- the `StratState` classifier against `thestrat_grammar` across 45,000 random bars with ties on
  every boundary, all four Failing 2 methods, detection on and off;
- every preset's six timeframes and enables against the Pine's `rTF`/`rEN` lines;
- the slot clamp on every standard chart timeframe plus 45m and 3H custom charts: no slot asks
  for a period below the chart's or for a non-standard one.

```bash
cd grammar && python3 -m pytest -q tests/test_tos_parity.py
```

## Verification checklist (in thinkorswim)

Run these before calling layer 1 done. Record symbol, chart timeframe, preset and result.
`TESTER_GUIDE.md` walks testers through the same checks.

- [ ] The study compiles and applies with no red error in the editor.
- [ ] Computed aggregations work: switching presets changes the strip without a
      "secondary period" error, on a 5m, 15m, 1H and D chart.
- [ ] Strat Candles on SPY 15m match the Suite's Strat Candles on TradingView bar for bar for
      one full session, including at least one F2u/F2d and one 3.
- [ ] Label strip on SPY 15m (TheStrat Classic) matches the Suite's Compact table (Bar State)
      live during RTH, including an inside bar on one timeframe.
- [ ] FTFC label matches the Suite's FTFC row at the same moment.
- [ ] ES 1H chart, Futures/Crypto preset: 4H boundaries land at 3PM/7PM/11PM/3AM/7AM/11AM PST
      (18/22/02/06/10/14 ET), and the D bar opens at 3PM PST (18:00 ET).
- [ ] A CME holiday week on ES (for example July 3 to 6, 2026): D and W candles match TradingView.
- [ ] SPY with extended hours on and off: D candles match TradingView's RTH daily bars.
- [ ] Reload test: note the Strat Candles and FTFC Candles on the last 20 bars, reload the
      chart, confirm nothing changed.
- [ ] Label text is readable on each state color (thinkorswim picks label text color itself).
- [ ] Mobile: note which parts render in the thinkorswim mobile app.
- [ ] The probe study's labels are recorded for P1, P4, P6, P10 and P22 (tester guide Test 4).
