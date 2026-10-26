# TheStrat Setup Screener

A companion to TheStrat Suite for TradingView's **Pine Screener** (Products > Screeners > Pine). The Suite draws levels, labels and a data table; the screener can read none of that. It reads only `plot()` values and `alertcondition()` states. This script runs the Suite's signal engine and puts every screenable state into a numeric plot, so a watchlist or an index's constituents can be filtered by FTFC, Strat combos, failing 2s, targets and Domino setups.

Source: `TheStratSetupScreener_v0.4.1.pine`. Requires TheStratGrammar (`SpinTrades/TheStratGrammar/1`), the same library the Suite imports. Pine Screener requires a TradingView Premium plan or higher.

## How to use it

1. Open Products > Screeners > Pine.
2. Choose the symbol source: the **Index** tab (S&P 500, NASDAQ 100, Dow 30, Russell 2000, sector indices and more) or the **Watchlist** tab. One source is active at a time.
3. Click the indicator chip and pick **TheStrat Setup Screener** from the Indicators dialog. Favoriting is not required (since TradingView's 2026-09-01 update), but a favorite shows at the top of the list.
4. Pick the screen's timeframe. It is the **focus**: the State, Pattern, C2, C1, CC and other focus columns describe that timeframe's candles, like a timeframe tab in a setups table (1D = the daily row, 1W = the weekly row). Slots smaller than it are ignored.
5. Set slots and signal toggles in the indicator's settings, add filters on the columns below, and press **Scan**. Changing anything requires a rescan.

## Columns

Every column follows one sign rule: **positive = bullish, negative = bearish, 0 = none or conflict.** The one exception is a potential (State 1), which has no direction until it triggers. Code columns show whole numbers (-2, not -2.00). Plots draw in one grey on a chart: a colored plot would count twice against TradingView's 64-plot limit.

The first ten appear automatically. The rest can be added from the column menu.

### Focus columns: the screen's own timeframe

| # | Column | Values | Meaning |
|---|---|---|---|
| 1 | State | 2 / -2 / 1 / 0 | 2 = bullish signal in force, -2 = bearish in force, 1 = potential (CC is inside, a setup is drawn, the trigger is untouched; it can still break either way), 0 = none. |
| 2 | Pattern | signed code | In force: the signal's combo, signed (codes below). Potential: the unsigned shape it would complete, 212 for 2-1, 312 for 3-1, 112 for 1-1. 0 = none. |
| 3 | C2 | 1, ±2, ±3 | Target candle: 1 inside, 2 = 2u, -2 = 2d, 3 = 3u, -3 = 3d. A failing 2 keeps its structure (F2u is 2). |
| 4 | C1 | 1, ±2, ±3 | Trigger candle, same codes. |
| 5 | CC | 1, ±2, ±3 | Current candle, same codes. |
| 6 | CT | 1 / -1 / 0 | Continuation: C1 and CC are the same directional 2 (2u-2u = 1, 2d-2d = -1). |
| 7 | C1 Shape | 1 / -1 / 0 | C1 is a hammer (1) or shooter (-1), by the Hammer/Shooter settings. |
| 20 | C1 Color | 1 / -1 | C1 closed green (above its open) or red. A doji counts red, as in FTFC. |

On a 1D screen the focus row is the daily: State, Pattern and CC match S3 State and S3 Type with the default slots.

### Continuity, Lead and slots

| # | Column | Values | Meaning |
|---|---|---|---|
| 8 | FTFC | 1 / -1 / 0 | Up, Down, Conflict across counted slots. Same rule as the Suite: close strictly above open; a doji counts as down. |
| 9 | FTFC Score | -6 to +6 | Counted slots above their open minus slots below. Equals the slot count at full bullish continuity. |
| 10 | Lead Direction | 1 / -1 / 0 | Direction of the largest slot with a signal in force (the Suite's Lead anchor). |
| 11 | Lead Pattern | signed code | The Lead's combo. 0 when there is no Lead. |
| 12 | Lead TF (min) | minutes | 60 = 1H, 240 = 4H, 1440 = D, 10080 = W, 43200 = M, 129600 = Q, 518400 = Y. |
| 13 | Lead Magnitude R | R multiple | (magnitude - trigger) / (trigger - stop). Stop is the CC's opposite extreme, the Suite's default stop reference. Blank when there is no magnitude ahead. |
| 14 | Lead Exhaustion R | R multiple | Same, to the exhaustion level. Blank when there is none ahead. |
| 15 | Nearest Setup % | signed percent | Closest untriggered trigger among slots whose current candle is an inside bar with a signal drawn. +0.4 = a bullish trigger 0.4% above price; -0.4 = a bearish trigger 0.4% below. |
| 16 | Nearest Setup TF (min) | minutes | The slot that trigger belongs to. |
| 17 | Domino | signed count | Consecutive inside-bar slots with a setup, largest first (the Suite's Domino). 0 below Minimum Timeframes. |
| 18 | Net In Force | -6 to +6 | Slots with a bullish signal in force minus slots with a bearish one. |
| 19 | Lead C1 HAM/SHO | 1 / -1 / 0 | The Lead slot's C1 is a hammer (1) or shooter (-1). |
| 21-26 | S1 to S6 State | 2 / -2 / 1 / 0 | Same codes as State, per slot. |
| 27-32 | S1 to S6 Type | 1, ±2, ±3 | Current candle per slot, same codes as CC. |
| 33-38 | S1 to S6 Above Open | 1 / -1 | Current candle above or below its open. Pick the slots that must line up (S4 = 1 and S5 = 1 for W and M up). |

S1 to S6 are the slots in settings order. Column titles cannot carry the slot's timeframe, so keep a note of your slot setup (default: 30m, 1H, D, W, M, Q). Focus columns plus per-slot columns make multi-timeframe screens: a weekly signal in force with a daily inside bar is S4 State = 2 and CC = 1 on a 1D screen.

### Pattern codes

Read like the Suite's alert combo, signed by direction:

| Shape | Code | Examples |
|---|---|---|
| C1 is inside | C2, C1, CC digits | 212 (2-1-2), 312 (3-1-2), 112, 213 (2-1-3) |
| Anything else | C1, CC digits | 22 (2-2), 32 (3-2), 23, 33 |
| CC is a Failing 2 | 1000 + C1 digit x 10 + 2 | 1022 (2-F2), 1032 (3-F2), 1012 (1-F2) |

So -212 is a bearish 2-1-2 and 1022 is a bullish 2-F2d.

### Named filters (True)

Columns hold numbers only, so each focus candle state is also a named filter: pick it from the filter list and it matches when True. They describe the screen's own timeframe.

| Group | Filters |
|---|---|
| Signal | `In force` (either direction; State shows which) |
| C2 (target) | `C2: 1`, `C2: 2U`, `C2: 2D`, `C2: 3` |
| C1 (trigger) | `C1: 1`, `C1: 2U`, `C1: 2D`, `C1: 3`, `C1: green`, `C1: red`, `C1: hammer`, `C1: shooter` |
| CC (current) | `CC: 1`, `CC: 2U`, `CC: 2D`, `CC: 3`, `CC: 2U red`, `CC: 2D green`, `CC: hammer`, `CC: shooter` |

A 2U or 2D is by structure, so `CC: 2D` includes a 2D closing green. Colors use FTFC's rule: a doji counts red. Hammer and shooter follow the Hammer/Shooter settings; the default, Broad, calls a candle a hammer when its open and close are both in the upper half of its range. Combine filters for a sequence: `C1: 1` plus `CC: 2U` is a 1-2U.

Broad is the loosest of the three definitions: it checks where the open and close sit, not how long the wick is, so it flags many candles without a long wick. For a strict long-wick read, choose Classic or Pin Bar.

### Other alert conditions (usable as filters)

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
| Setup 2-1 waiting for the 2 (potential) | State = 1, Pattern = 212 |
| Bullish 2-1-2 in force | State = 2, Pattern = 212 |
| 1-2U: inside then up | `C1: 1`, `CC: 2U` |
| Signal in force with continuity | State = 2 and FTFC = 1, or State = -2 and FTFC = -1 |
| Fresh triggers only | CT = 0 |
| Hammer trigger candle | `C1: hammer` |
| 2-1 setup with a green trigger candle | `C2: 2U` or `C2: 2D`, `C1: 1`, `C1: green` |
| 2D closing green (failing bearish) | `CC: 2D green` |
| Weekly 2D green hammer (1W screen) | `CC: 2D green`, `CC: hammer` |
| Weekly bullish in force, daily inside (1D screen) | S4 State = 2, CC = 1 |
| Weekly inside bar | S4 Type = 1 (default slots), or CC = 1 on a 1W screen |

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

**v0.4.1 (2026-10-25).** Renamed TheStrat Setup Screener (short title Setup Screener). `CC: hammer` and `CC: shooter` filters. `C1: hammer` tests the candle before CC, so a 2D closing green as a hammer took a second pass. CC's shape uses the same Hammer/Shooter settings as C1. Filters only, no column. Budget: 38 plots + 26 alert conditions = 64 of 64, the limit, so a new plot or filter needs one removed first.

**v0.4.0 (2026-10-25).** Named candle filters: each focus candle state is also a True filter (`C2: 2U`, `CC: 2D green`, `C1: hammer`, `In force` and so on), because screener columns can only show numbers. Code columns show whole numbers. Lead Trigger Dist %, C2 Color and CC Color are dropped to keep the screener Strat-focused. Budget: 38 plots + 24 alert conditions = 62 of 64.

**v0.3.0 (2026-10-25).** Focus columns: State, Pattern, C2, C1, CC, CT, C1 Shape and the three candle colors describe the screen's own timeframe, read from the chart series with the same engine as a slot. They are now the first seven columns. Per-slot Pattern and Failing are removed. Budget: 41 plots + 5 alert conditions = 46 of 64.

**v0.2.0 (2026-10-25).** Fixes the chart runtime error RE10140, "The script creates too many plots (67). The limit is 64." A plot with a series color counts as two plots and each alert condition as one, so v0.1.0 used 31 x 2 + 5 = 67. Plot colors are now constant. Adds S1 to S6 State and Pattern, so a slot's potential or in-force signal can be screened even when a larger slot holds the Lead. Budget: 43 plots + 5 alert conditions = 48 of 64.

**v0.1.0 (2026-10-24).** First build. Compiled, but failed on the chart with RE10140 (tested 2026-10-25).
