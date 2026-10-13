# TheStrat Suite for NinjaTrader 8

A full port of TheStrat Suite v3.1.1 (`pine/TheStratSuite_v3.1.1.pine`) to NinjaTrader 8 NinjaScript. It has the same six timeframe slots, presets, signal families, magnitude and exhaustion targets, Take Action Windows, stops, Lead filter, Domino, FTFC, data table, debug panel, bar coloring, preview mode and alerts. It ships free and open source under MPL-2.0, like the Pine.

**Status: v0.1.0, not yet loaded in NinjaTrader.** NinjaTrader runs only on Windows, so this was built and tested on Linux against stand-in NinjaTrader types (see Testing). Work through the in-app checklist below before publishing it.

## Install

1. Download `dist/TheStratSuite_NT8_v0.1.0.zip`.
2. In NinjaTrader 8: **Tools > Import > NinjaScript Add-On**, pick the zip. NinjaTrader compiles it.
3. Open a chart, right-click > **Indicators**, add **TheStratSuite**.

If the import is refused, install by hand. Copy both `.cs` files from `src/Indicators/TheStratSuite/` to `Documents\NinjaTrader 8\bin\Custom\Indicators\TheStratSuite\`. Then open any NinjaScript Editor window and press F5 to compile.

The indicator loads its own higher-timeframe data. Monthly and longer slots need years of daily history. If a slot's row stays empty, raise **Max Daily Bars To Load** under 14. Advanced, and make sure your data provider serves that much daily history.

## Files

| Path | What it is |
|---|---|
| `src/Indicators/TheStratSuite/TheStratSuiteEngine.cs` | The decision logic, ported from the Pine: grammar, presets, `computeSignalState`, `shouldDrawC1Level`, Lead filter, Domino, exhaustion scan, label and alert text. Plain C# with no NinjaTrader dependencies, so it can be tested anywhere. |
| `src/Indicators/TheStratSuite/TheStratSuite.cs` | The NinjaTrader indicator: settings, higher-timeframe data, the as-of forming candle, preview, alerts, bar coloring and rendering. |
| `dist/TheStratSuite_NT8_v0.1.0.zip` | Import package. Rebuild it with `python3 ninjatrader/build_zip.py`. |
| `tests/` | The Linux harness: NinjaTrader stand-in types, a parity harness and an end-to-end smoke test. `grammar/tests/test_nt_parity.py` runs them. |

## Same as the TradingView Suite

- Every input, with the same names, defaults and tooltips, grouped into 15 property-grid groups.
- Bar types, Failing 2 methods, hammer and shooter definitions. These are checked bar for bar against the grammar (see Testing).
- The signal tree, the seven in-force terms, suppressors, FTFC gates, sticky stops and break-even moves. Also magnitude and exhaustion gates, Take Action Windows, line suppression by timeframe, label consolidation, the Lead filter, Smallest Timeframe Only stops, Domino, both table modes, the debug panel, and Strat and FTFC candles with the Failing 2 flip.
- The color palette. Green and red mean bull and bear everywhere.
- Alerts fire on the same edges: the consolidated message with its detail toggles, plus each of the 15 alert conditions.

## Where NinjaTrader differs

| Area | TradingView Suite | NinjaTrader port | Why |
|---|---|---|---|
| History | On historical bars the forming higher-timeframe candle is read as it finally closed, so past signals can show values the chart had not reached yet. | Each forming candle is rebuilt from the chart bars up to that bar. History shows what you would have seen live. The live bar reads the same served candle as the Suite. | No-repaint rule. Past signals, stops and alerts can therefore differ from TradingView's history; the live bar should match. |
| Higher-timeframe data | `request.security` per slot | One minute series per intraday timeframe, and one daily series from which D, W, M, 3M, 6M and 12M are built by trade date. Weeks start Monday. | Keeps every calendar slot on the same trade-date calendar and makes 3M, 6M and 12M exact calendar periods. |
| Preview straddle | Pine shifts a slot whose period closed mid chart bar. | Not needed. Chart bars map to higher-timeframe candles by close time, so no bar straddles two periods. A preview slot shows `?` in the table's CC column. | |
| Preview Auto | Arms from TradingView's clock and session. | Arms from NinjaTrader's clock and the instrument's trading hours template (holidays included), or 4 hours after the last bar closed. A 15-second timer re-checks it while no ticks arrive. | Works in Market Replay too. |
| Timeline labels | Drawn at each line's end, in the future. | Same, but NinjaTrader cannot scroll past its right margin. **Keep Timeline Labels On Screen** (on by default) pulls a far label back to the chart's right edge. | |
| Alerts | TradingView alert dialog, webhooks. | **Enable Alerts** (off by default) sends to NinjaTrader's Alerts Log with a sound. Each of the Suite's alert conditions is its own toggle under 13. Alerts. Alerts fire only on live data, never while history loads. | No webhooks. |
| Debug panel YES/NO | Green and red fills. | Neutral slate and dark fills. | Green and red are reserved for bull and bear. |
| Non-time charts (tick, volume, range, Renko) | Timeframe-based charts only. | Allowed. No slot counts as lower than the chart, and lines project 10 bars ahead. | |
| Outputs | None. | Hidden plots for strategies and Market Analyzer: `FtfcState` (+1 up, -1 down, 0 conflict) and `Tf1Signal`..`Tf6Signal` (+1 bull, -1 bear, 0 none). | |

## Check in NinjaTrader before publishing

These could not be tested on Linux. Most are one look at a chart. On a Mac, `TESTING_ON_MAC.md` covers getting Windows, data and Market Replay set up, and groups these checks into test sessions. Compare against the TradingView Suite on the same symbol and preset, for example ES 5-minute with TheStrat Classic.

1. **Import and compile.** The zip imports with no compile errors. If NinjaTrader rejects `Info.xml`, export any script from your install and copy its version line into `build_zip.py`.
2. **Higher-timeframe data.** Every enabled slot fills its table row. On a futures ETH chart, try the default (blank) and your ETH template in **HTF Trading Hours**.
3. **Daily bars.** D, W and M rows match TradingView, including Monday week opens after a Sunday-evening session.
4. **Live bar parity.** The table, lines and labels on the last bar match TradingView for each preset.
5. **Line positions.** Lines start at the right candle and end at the period end. Dashed and dotted styles show. Future ends project sensibly on 1m, 5m, 60m and daily charts.
6. **Labels.** The ↑ ↓ ◆ glyphs of Universal style render, and label text is readable on the label background.
7. **Table and debug panel.** Every position setting, both modes and all text sizes look right.
8. **Bar coloring.** Strat Candles and FTFC Candles, including the Failing 2 flip.
9. **Preview.** Auto arms after the Friday close (2 PM PT, 5 PM ET, for CME equity futures) and on holidays, and disarms at the Sunday open (3 PM PT, 6 PM ET). On and Off behave.
10. **Alerts.** Turn on Enable Alerts and a few conditions in Market Replay. They reach the Alerts Log once per bar, with sound. The 🟢 and 🔴 in the consolidated message may render in monochrome.
11. **Property grid.** Enum settings show the Suite's option names ("Pin Bar (Strict)", "Reclaim + Open"). Color settings save and reload with the workspace.
12. **Performance.** A 1-minute chart with six slots and several months of data loads in reasonable time and stays responsive on ticks.
13. **Other charts.** Tick, Renko and daily or weekly charts load without errors.

NinjaTrader's guidance is that `AddDataSeries` arguments should not depend on settings. The Suite has to depend on them because the presets choose the timeframes. That works on charts, but a strategy that hosts this indicator may need the same series added itself. Check this if you use the outputs from a strategy.

## Testing done here

`grammar/tests/test_nt_parity.py` (needs `mono-mcs` and `mono-runtime`):

- The engine's bar classifier matches the grammar on 40,000 random bars across the four Failing 2 methods, plus 5,000 with detection off and the no-data cases. This is the same generator as the thinkorswim parity test.
- Hammer and shooter match the grammar on 60,000 candles across the three definitions, with and without Match Candle Color.
- Calendar period keys: Monday week start, and quarter, half-year and year edges.
- The indicator type-checks at C# 5 against stand-ins for the NinjaTrader, WPF and SharpDX types it uses.
- An end-to-end run on synthetic ES-style sessions. It covers every preset, an everything-on configuration, compact table with FTFC candles, and a custom 5m to 12M set, on 5-minute and daily charts. It runs history, realtime ticks, a live and a weekend clock (preview), alerts and a render pass. It passes with no exceptions.
- **No-lookahead check.** Each of those runs is repeated with the higher-timeframe series holding only candles that had closed at each chart bar. The painted history (FTFC, signal outputs, bar colors) must be identical to the fully loaded run, and it is.

What this does not show: that NinjaTrader accepts the file, or that output matches TradingView bar for bar. That is the checklist above.
