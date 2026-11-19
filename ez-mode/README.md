# TheStrat Suite EZ Mode - Learn TheStrat

TheStrat Suite with 16 settings instead of 139, plus hover text that explains what is on
the chart. It is for people who are new to TheStrat or who want the Suite's read without the
setup. TheStrat was created by Rob Smith.

Status: v0.1.0, not yet published on TradingView.

## What it shows

- Four timeframes, picked by one **Style** setting. The default, TheStrat Classic, is
  60-minute, Day, Week and Month. Day Trade, Swing Trade, Investing and Custom are the others.
- Reversals and inside bar breaks, always on. 2-2 Continuations, Failing 2s and 3-2
  Expansions are optional and have no filters.
- Magnitude and exhaustion targets, Take Action Windows, stops (on by default), Preview Mode.
- Bar numbers on the last 50 chart candles (on by default).
- The data table in one of two styles. **Simple** (default) has one line per timeframe:
  Before (the bar or bars leading in, with HAM or SHO), then Now (the current bar,
  highlighted). Read together they are the combo. **Detailed** is the Suite's five-column table.
- An **Explain Row** under the table that sums it up in plain English.
- Alerts: Signal In-Force (Any, Bullish, Bearish), New Potential, and the four FTFC alerts.

## The teaching layer

- Hover any data table cell for a live read of that bar or timeframe. Headers are a glossary.
- Hover any level label for what the level is: a trigger and what breaking it would make, a
  magnitude target, an exhaustion level, or a stop.
- The Explain Row needs no hover, so it also works on a phone.

## Where it differs from the Suite

| | Suite default | EZ Mode |
|---|---|---|
| Failing 2 | Reclaim: price back inside the prior bar's range | Open: a 2u trading below its own open, a 2d trading above it |
| Timeframes | Six slots, TheStrat Classic preset is 30m, 60m, D, W, M | Four slots, TheStrat Classic is 60m, D, W, M |
| Stops | Off | On |
| Bar numbers | Off | On |
| Filters (HAM/SHO, F2, FTFC, Lead Signal), Domino, bar coloring, colors, per-timeframe alerts | Available | Not present |

3u / 3d and exhaustion keep the Suite's meaning. The hover text says where that differs from
classic usage.

## It is a generated file

`TheStratSuite_EZ_v0.1.0.pine` is built from the Suite source by `build_ez.py`. Do not edit
it by hand. The engine is the Suite's engine, so a fix in the Suite reaches EZ Mode by
rebuilding.

```
python3 ez-mode/build_ez.py            # rebuild
python3 ez-mode/build_ez.py --check    # fail if the committed file is stale
```

The generator turns every Suite input into its default value, except the ones listed in
`OVERRIDES`, then applies the exact-text `PATCHES` for the teaching layer. A patch whose
anchor no longer matches the Suite stops the build, so drift is caught at build time.
