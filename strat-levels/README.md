# Strat Levels (working name), v0.1.0 draft

A management view built on the same FTFC / Signals In Force read as TheStrat Suite v3.1.1 and the FTFC vs In Force study v0.3.1. It does not call direction. It shows where each timeframe's state changes, where a position's C1 trail sits, and how far along the current move already is.

**Status: draft, not compiled.** Reviewed against the Suite v3.1.1, the Grammar library and the study on 2026-11-08 (fixes listed under "Review fixes" below); still never run through the Pine compiler. Written without a Pine compiler. Load it in the TradingView Pine Editor and fix whatever it reports before relying on any readout. It imports the published `SpinTrades/TheStratGrammar/1` library, like the Suite does.

Source: [`StratLevels_v0.1.0.pine`](StratLevels_v0.1.0.pine). Mozilla Public License 2.0.

## What each readout means

**Flip levels (lines and labels).** For the chart timeframe and each enabled 1H / 4H / D / W / M:

- **C1H / C1L** (dashed): the prior completed bar's high and low. These are the prices the forming bar has to break to change state, or close back inside to fail. Label notes describe the bar's state, not a trade: `break = 2u` / `break = 2d`, `break = 3` (the other side is already broken), `back inside = F` (holding beyond it now, and a close back inside is the failing flip; plain `back inside` under the Open and Reclaim + Open methods, where a close inside is not enough), `reclaim` (broken but price is back inside), `3, locked` (both sides broken: a 3 stays a 3 and there is no failing 3). A side already broken is drawn in the Suite's Crossed gray.
- **O** (dotted): the forming bar's open. Crossing it flips that timeframe's above-open side, which is what FTFC counts.
- Where two timeframes share a price, the higher timeframe keeps the line and the labels are joined.

**Table, one row per timeframe.** State (Suite notation and colours: 1u/1d, 2u/2d, F2u/F2d, 3u/3d), AO (above/below the forming open), IF (signal in force up/down/none, the Suite's rules), then the distance from price to C1H, C1L and the open in daily ATR units (`+` = above price). A grayed distance means that side is already taken.

**FTFC / In force.** Across the enabled 1H/4H/D/W/M (the chart timeframe is not part of this set): FTFC up / down / conflict, and whether every timeframe has a signal in force in the same direction.

**Run (context readout, not a signal).** The study's Agreement colour on the chart timeframe: agree up/down (bright), FTFC only (faded), in force without FTFC (blue/purple), neither. It shows how many bars the current colour has held, and how far price has moved since the close of the bar where it first appeared, in daily ATR. `>=` means the run started before the loaded data. If any of 1H/4H/D/W/M is still in a period that began before the first loaded chart bar (a W or M on a 1m chart, usually), the row shows n/a and names it: that period's range cannot be rebuilt from the chart, so its history cannot be graded.

**Day range (context readout, not a signal).** Expected range = the mean high-low of the last 14 completed daily bars, price only. "Used" = today's session range as a share of it. "From low" = price minus session low, and "from high" = session high minus price, each as a share of the expected range. Past the threshold (default 80%) the share is dimmed or hidden. The session is the symbol's daily bar (18:00 ET open on CME futures). n/a above a daily chart.

**C1 trail (trade management).** Set Side, Entry TF and Entry Time. The stop starts at C1's opposite extreme (C1 is the entry-TF bar before the one containing the entry). At each entry-TF close it moves to the just-closed bar's opposite extreme if that is tighter, and it never loosens. It ends on the bar where price trades at or through it, marked with an X. If the entry time is earlier than the first loaded chart bar, the table flags the trail as possibly looser than the real one. This is the only alert in the script ("C1 Trail Crossed"), and it reports a touch, not a direction.

**Invalidation line.** Placeholder only (SECTION 8 of the source). Its definition waits on Nate's answers from 2026-11-08.

## Repainting

Higher-timeframe values come from `request.security` with `lookahead_on`, and only completed (`[1]`, `[2]`) or period-stable values are used (the period open and its open time). Each forming bar is rebuilt from chart bars, as a running high/low keyed on the served period-open time with the chart close as its price. That is the same method as the study and the Suite's FTFC candles, so history is graded as it stood at each bar's close. Daily ATR and the expected range use completed days only.

One exception, on the last bar only: a period that began before the first loaded chart bar cannot be rebuilt, so the table and levels take the served period-to-date high/low for it (the true range so far at that moment). Such a period is never used to grade history, and the alignment run skips it.

## Not verified

- **Compilation.** Nothing here has been through the Pine compiler.
- **Parity with the Suite and the study.** The state, AO and IF cells have not been compared bar for bar with the Suite's table or the study's heatmap. They should match because the functions are copied unchanged, but nobody has checked.
- **AO convention.** It follows the Suite and Grammar: above the open means strictly above, and a close at the open counts as down. The project's `>=` note and this convention are still unreconciled (open item 5 in the 2026-11-08 thread).
- **Flip notes assume the Reclaim failing method.** Under the Open-based methods, "back inside = F" is not exactly the failing rule.
- **Trail edge cases.** On the entry bar itself, the whole chart bar's range is tested against the stop, including any part before the entry time. The trail has not been checked against the project's simulator on any trade, and it differs from `lib/sim_1m.py::simulate_c1_trail_detail` in three known ways: the simulator skips the entry bar by default, it only ratchets to a level the bar can reach (new level at or below the bar's high for a long), so a gap through the new level does not stop it out, and it exits at the session change. This line tightens on every entry-TF close and counts a gap through as a cross.
- **Session range.** A partial first session (history starting mid-day) and extended-hours versus regular-hours charts on equities have not been checked against the served daily bar.
- **No preview mode.** The Suite's weekend/holiday preview and calendar-straddle handling are not ported. With the market closed, every row shows the last traded period.
- **Usefulness.** The range-used and run-age readouts come from ideas that are research-only and untested as filters. They are shown as context, not as rules.

## Review fixes (2026-11-08)

- Forming W/M (and the first 1H/4H/D) period on low-timeframe charts: when the chart's history starts after the period opened, the rebuilt high/low missed everything before the first loaded bar, so State, IF, the flip notes, All In Force and the Run could be wrong on the last bar (a 1m chart with a few weeks of history always has a partial M). Fixed: the last bar takes the served period-to-date range for that period, and the Run is not tracked through it.
- A 3 (both sides broken) labelled its taken side `back inside = F`. There is no failing 3, so it now says `3, locked`.
- `back inside = F` was shown under every Failing 2 method; it now drops `= F` where a close inside is not enough.
- Label wording `take = ...` changed to `break = ...` so it cannot be read as "take the trade".
- Trail: entry before the first loaded bar is flagged.
- `if4` / `ao4` renamed `if4H` / `ao4H` (only to remove any doubt about an identifier starting with a keyword).
