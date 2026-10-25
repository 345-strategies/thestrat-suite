# TheStrat Suite for thinkorswim

A thinkScript port of TheStrat Suite v3.1.1. Free and open source under MPL-2.0, like the Pine
original. The goal is a **full port** of every Suite feature; `FULL_PORT_PLAN.md` maps each one
to a thinkScript approach, lists the hard platform limits and their equivalents, and sets the
build order. The study shipped today is layer 1 of that plan.

> **Beta, for testing.** v0.2.0 is a pre-release for beta testers, not a finished study. Nobody
> has loaded it in thinkorswim yet. Download it from the
> [v0.2.0 beta pre-release](https://github.com/345-strategies/thestrat-suite/releases/tag/tos-TheStratSuite-v0.2.0-beta) or copy it from this folder, and follow
> `TESTER_GUIDE.md`.

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
| `probes/TheStratSuite_TOS_Probes_v0.2.0.txt` | Optional second study for testers. Its labels, one bubble and an opt-in alert collect answers to the plan's in-app questions P1, P4, P6, P10 and P22. |
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
  timeframe instead of the chart's own, so thinkorswim never gets a custom period. The FTFC
  label grades each timeframe's served close, like the Suite's FTFC row, so it agrees with the
  strip. The classifier is unchanged.
- **v0.1.0 (2026-09-28).** Layer 1 as "Lite".

## How it differs from the Suite on TradingView

- **Timeframes below the chart's are not read.** thinkorswim rejects a secondary aggregation
  below the chart timeframe, so a lower slot is clamped, shown gray, and left out of FTFC. The
  Suite also grays these cells, but its FTFC still counts them (and those votes change on
  reload), so FTFC can differ on a chart above an enabled timeframe.
- **No 12H.** thinkorswim has no 12-hour aggregation. The Futures/Crypto preset runs 1H, 4H, D, W.
  The full port synthesizes 12H from chart bars (see `FULL_PORT_PLAN.md`).
- **4H is likely misaligned in v0.2.0 on futures, and on stocks with extended hours on.**
  thinkorswim aggregates Last-price time bars from midnight CT (Time Charts manual), so its
  native 4H runs 10P/2A/6A/10A/2P/6P PT (01/05/09/13/17/21 ET).
  - /ES: the Suite runs 3P/7P/11P/3A/7A/11A PT (18/22/02/06/10/14 ET).
  - SPY with extended hours on: TradingView runs 1A/5A/9A/1P PT (04/08/12/16 ET), and
    thinkorswim's "Start aggregations at market open" is unavailable with extended hours on.
  - SPY with extended hours off: 1H and 4H match TradingView's 6:30 AM PT (9:30 ET) bars only if
    "Start aggregations at market open" is checked and also reaches a study's secondary
    aggregations, which is not yet known. 1H with extended hours on starts on the hour on both.

  This affects 4H on the Day Trade, Futures/Crypto and Swing Trade presets, and possibly 1H on
  stocks. Tester guide Test 3 records the real start times; the full port builds futures 4H from
  chart bars.
- **No preview mode yet.** With Preview Mode on Auto (its default), after the close and before
  the open the Suite moves each just-closed candle into C1 and shows `?` for CC, plus a PREVIEW
  MODE row. The strip keeps showing the candle that just closed. Set the Suite's Display -
  Preview Mode to Off to compare.
- **FTFC Candles in older history.** The Suite reads only each timeframe's most recent candles
  (250 intraday, 200 for 4H, 150 for D, 100 for W and M) and grades older bars without that
  timeframe's vote. thinkorswim grades every loaded bar with every timeframe it can read, so
  older FTFC Candles can differ. With Scalp's 5m slot that cutoff is about 20 hours back.
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
- **Time-based charts only.** On tick, range and Renko charts GetAggregationPeriod returns ticks
  or a price range, so no higher timeframe can be read; the strip and FTFC will not work, and the
  study may draw nothing (tester guide Test 4 checks). The full port adds a "time-based charts
  only" label (`FULL_PORT_PLAN.md`, hard limit 4).
- **No Heikin-Ashi.** TheStrat reads traded highs and lows. Use regular candles or bars.
- **Stock pre-market bars.** Which daily candle a pre-market bar belongs to on a stock chart with
  extended hours on is still unconfirmed (probe P6), so FTFC Candles there are not yet known to
  hold on reload.

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
- [ ] Label strip on SPY 15m (TheStrat Classic) matches the Suite's Compact table (Bar State,
      Preview Mode Off) live during RTH, including an inside bar on one timeframe. Record whether
      "Start aggregations at market open" is checked.
- [ ] FTFC label matches the Suite's FTFC row at the same moment.
- [ ] ES 1H chart, Futures/Crypto preset: record where each 4H candle starts (tester guide
      Test 3). Expected in v0.2.0: 10 PM/2 AM/6 AM/10 AM/2 PM/6 PM PT (01/05/09/13/17/21 ET).
      The Suite's are 3 PM/7 PM/11 PM/3 AM/7 AM/11 AM PT (18/22/02/06/10/14 ET); layer 3 builds
      futures 4H to match, so a mismatch here is a known limit, not a layer-1 failure. Pass/fail:
      the D candle opens at 3 PM PT (18:00 ET).
- [ ] A CME holiday week on ES (for example July 3 to 6, 2026): D and W candles match TradingView.
- [ ] SPY with extended hours on and off: D candles match TradingView's RTH daily bars.
- [ ] Reload test: note the Strat Candles and FTFC Candles on the last 20 bars, reload the
      chart, confirm nothing changed.
- [ ] Label text is readable on each state color (thinkorswim picks label text color itself).
- [ ] Mobile: note which parts render in the thinkorswim mobile app.
- [ ] The probe study's answers are recorded for P1, P4, P6, P10 and P22, and P11 (a tick chart)
      for the study itself (tester guide Test 4).
