# TheStrat Suite Screener

A companion to TheStrat Suite for TradingView's **Pine Screener** (Products > Screeners > Pine). The Suite draws levels, labels and a data table; the screener can read none of that. It reads only `plot()` values and `alertcondition()` states. This script runs the Suite's signal engine and puts every screenable state into a numeric plot, so a watchlist of up to 1,000 symbols can be filtered by FTFC, Strat combos, failing 2s, targets and Domino setups.

Source: `TheStratSuiteScreener_v0.1.0.pine`. Requires TheStratGrammar (`SpinTrades/TheStratGrammar/1`), the same library the Suite imports. Pine Screener requires a TradingView Premium plan or higher.

## How to use it

1. Add the script to a chart once, then mark it as a favorite (the star in the indicator list). Pine Screener only lists favorites.
2. Open Products > Screeners > Pine, choose a watchlist, then pick **TheStrat Suite Screener** as the indicator.
3. Pick the screen's timeframe (this is the chart timeframe each symbol is evaluated on). Slots smaller than it are ignored.
4. Set slots and signal toggles in the indicator's settings, add filters on the columns below, and press **Scan**. Changing anything requires a rescan.

## Columns

Every column follows one sign rule: **positive = bullish, negative = bearish, 0 = none or conflict.** On a chart the plot colors follow it too: green is bull, red is bear.

The first ten appear automatically. The rest can be added from the column menu.

| # | Column | Values | Meaning |
|---|---|---|---|
| 1 | FTFC Score | -6 to +6 | Counted slots above their open minus slots below. Equals the slot count at full bullish continuity. |
| 2 | FTFC | 1 / -1 / 0 | Up, Down, Conflict. Same rule as the Suite: close strictly above open; a doji counts as down. |
| 3 | Lead Direction | 1 / -1 / 0 | Direction of the largest slot with a signal in force (the Suite's Lead anchor). |
| 4 | Lead Pattern | signed code | The Lead's combo. See pattern codes below. 0 when there is no Lead. |
| 5 | Lead TF (min) | minutes | 60 = 1H, 240 = 4H, 1440 = D, 10080 = W, 43200 = M, 129600 = Q, 518400 = Y. |
| 6 | Lead Trigger Dist % | percent | Distance from the trigger in the signal's direction. Positive = price is beyond the trigger. A Failing 2 can be in force with a negative value. |
| 7 | Lead Magnitude R | R multiple | (magnitude - trigger) / (trigger - stop). Stop is the CC's opposite extreme, the Suite's default stop reference. Blank when there is no magnitude ahead. |
| 8 | Lead Exhaustion R | R multiple | Same, to the exhaustion level. Blank when there is none ahead. |
| 9 | Nearest Setup % | signed percent | Closest untriggered trigger among slots whose current candle is an inside bar with a signal drawn (the Suite's "potential"). +0.4 = a bullish trigger 0.4% above price; -0.4 = a bearish trigger 0.4% below. |
| 10 | Nearest Setup TF (min) | minutes | The slot that trigger belongs to. |
| 11 | Domino | signed count | Consecutive inside-bar slots with a setup, largest first (the Suite's Domino). 0 below Minimum Timeframes. |
| 12 | Net In Force | -6 to +6 | Slots with a bullish signal in force minus slots with a bearish one. |
| 13 | Lead C1 HAM/SHO | 1 / -1 / 0 | The Lead slot's C1 is a hammer (1) or shooter (-1). |
| 14-19 | S1 to S6 Type | 1, ±2, ±3 | Current candle: 1 inside, 2 = 2u, -2 = 2d, 3 = 3u, -3 = 3d. A failing 2 keeps its structure (F2u is 2). |
| 20-25 | S1 to S6 Failing | 1 / -1 / 0 | 1 = F2d (bullish reclaim), -1 = F2u (bearish reclaim). |
| 26-31 | S1 to S6 Above Open | 1 / -1 | Current candle above or below its open. Build custom continuity filters from these. |

S1 to S6 are the slots in settings order. Column titles cannot carry the slot's timeframe, so keep a note of your slot setup (default: 30m, 1H, D, W, M, Q).

### Pattern codes

Read like the Suite's alert combo, signed by direction:

| Shape | Code | Examples |
|---|---|---|
| C1 is inside | C2, C1, CC digits | 212 (2-1-2), 312 (3-1-2), 112, 213 (2-1-3) |
| Anything else | C1, CC digits | 22 (2-2), 32 (3-2), 23, 33 |
| CC is a Failing 2 | 1000 + C1 digit x 10 + 2 | 1022 (2-F2), 1032 (3-F2), 1012 (1-F2) |

So -212 is a bearish 2-1-2 and 1022 is a bullish 2-F2d.

### Alert conditions (usable as filters)

`FTFC Up (state)`, `FTFC Down (state)`, `Bullish Signal In Force (any slot)`, `Bearish Signal In Force (any slot)`, `Domino (state)`. These are states, not edges: they are true for as long as the condition holds, which is what a screen asks. The Suite's own alerts fire once when a condition first becomes true.

## Screen recipes

| Screen | Filters |
|---|---|
| Full continuity up | FTFC = 1 |
| Daily or larger signal in force, bullish | Lead Direction = 1, Lead TF (min) >= 1440 |
| Bullish 2-1-2 Lead | Lead Pattern = 212 |
| About to trigger | Nearest Setup % between -0.3 and 0.3 |
| Bullish setup with continuity | Nearest Setup % > 0, FTFC = 1 |
| Room to run | Lead Magnitude R >= 2 |
| Domino | Domino >= 2 or Domino <= -2 |
| Weekly inside bar | S4 Type = 1 (with the default slots) |
| Bearish reclaim on the daily | S3 Failing = -1 (with the default slots, Failing 2s on) |

Room-to-run columns are information, not a tested edge. The project's own research (STR-551) found that filtering on Room adds nothing over TFC alone once a lookahead leak was removed.

## How it differs from the Suite

- **Timeframe slots are dropdowns.** Pine Screener ignores `input.timeframe()` and serves only 1m, 5m, 15m, 30m, 1H, 2H, 4H, D, W and M. The Suite's presets that use 12H, 3M or 12M cannot run there.
- **Q and Y are built from monthly bars** (`SCREENER-QY-1`), because 3M and 12M cannot be requested. Month and year are read with the Suite's +12h normalization, so futures months stamped at 18:00 ET the prior evening land in the right quarter.
- **No Preview Mode, no live straddle.** Slots always read the period in progress.
- **Slots below the chart timeframe are excluded from FTFC.** The Suite skips them in its table but still counts them in FTFC.
- **Last bar only.** History plots `na`. Under `lookahead_on` the forming higher-timeframe candle shows its final high and low on historical bars, so any historical value would repaint. Filter on values, not on "crossing" conditions.
- **Stateless.** The Suite's per-period latches reduce to direct tests on the last bar. Stops always use the CC reference.

## Checking it against the Suite

The script draws a small check table (TF, C2, C1, CC, FTFC, Lead) in its pane. Put the Suite and this script on the same chart with matching slots and signal settings, and the two tables should agree cell for cell on every slot at or above the chart timeframe. Any difference is a bug in this script.

## Version history

**v0.1.0 (2026-10-24).** First build. Not yet compiled or run in TradingView; the first test is a compile, then a 100-symbol Pine Screener scan.
