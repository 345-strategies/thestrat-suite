# Settings Reference

*Version 3.0.0 | Last updated 2026-07-31*

This reference documents every setting in the TheStrat Suite indicator, grouped in the order the panels appear in the TradingView settings dialog. Each entry lists the default value and available options, what the setting does, and when to use it. New to TheStrat? Start with the **Overview** and **TheStrat Concepts (Primer)** below.

Written against `pine/TheStratSuite_v3.1.1.pine`. Where behavior traces to a specific fix, the `FIX` tag is cited; grep the source for it.

---

## Contents
- [Overview](#overview)
- [TheStrat Concepts (Primer)](#thestrat-concepts-primer)
- [Timeframe Preset](#timeframe-preset)
- [Timeframes (Custom)](#timeframes-custom)
- [Signals - Reversals](#signals---reversals)
- [Signals - Continuations](#signals---continuations)
- [Signals - Reclaims](#signals---reclaims)
- [Signals - Expansions](#signals---expansions)
- [Filters - Hammer/Shooter Detection](#filters---hammershooter-detection)
- [Filters - Domino Setups](#filters---domino-setups)
- [Filters - Lead Signal](#filters---lead-signal)
- [Targets - Magnitude & Exhaustion](#targets---magnitude--exhaustion)
- [Targets - Take Action Windows](#targets---take-action-windows)
- [Stops - Stop Levels](#stops---stop-levels)
- [Display - Preview Mode](#display---preview-mode)
- [Display - Data Table](#display---data-table)
- [Display - Labels](#display---labels)
- [Display - Bar Coloring](#display---bar-coloring)
- [Display - Bar Numbers](#display---bar-numbers)
- [Style - Level Colors](#style---level-colors)
- [Alerts](#alerts)
- [Advanced - Exhaustion Behavior](#advanced---exhaustion-behavior)
- [Advanced - Outside Bars](#advanced---outside-bars)
- [Advanced - Failing 2 Detection](#advanced---failing-2-detection)
- [Advanced - Data Table](#advanced---data-table)
- [Advanced - Take Action Windows](#advanced---take-action-windows)
- [Advanced - Domino Setups](#advanced---domino-setups)
- [Debug](#debug)
- [Troubleshooting & FAQ](#troubleshooting--faq)

---

## Overview

TheStrat Suite is a multi-timeframe price-action indicator that implements TheStrat methodology on your TradingView chart. It watches up to six timeframes at once, classifies every candle by its TheStrat bar type (inside, directional, or outside), and draws the key structural levels (triggers, magnitude targets, exhaustion targets, and stops) that those bars create. On top of the raw structure it detects the named setups traders actually trade (inside reversals and continuations, 2-2 reversals and continuations, Failing 2 Range Reclaims, and 3-bar expansions), each with optional Hammer/Shooter, FTFC, and Failed-2 confluence filters so you see only the setups that match your style.

Beyond signals, the Suite gives you a live read on the whole timeframe stack. A data table reports the current bar type on each timeframe in either full Strat notation or a plain-language Universal view, Full Timeframe Continuity (FTFC) tells you when every monitored timeframe agrees on direction, the Lead Signal filter keeps lower-timeframe entries aligned with the highest timeframe already in force, and Domino detection flags stacked inside bars that are coiling across timeframes. Take Action Windows shade the zone between trigger and target so the tradeable area is obvious at a glance.

Everything is alert-ready and plan-ahead friendly. Consolidated and per-timeframe alerts can be enriched with trigger, magnitude, exhaustion, stop, and FTFC context, and Preview Mode projects next-period levels while the market is closed so you can prepare before the open. The Suite is built to be non-repainting, so the levels you see while a bar is live are the levels that remain after it closes (see `docs/engineering/repaint-prevention.md` for how that guarantee is kept).

### Quick Start

1. Add TheStrat Suite to your chart. It overlays directly on price.
2. Open the settings and pick a **Timeframe Preset** (TheStrat Classic, Scalp, Day Trade, Futures/Crypto, Swing Trade, or Investing). The preset auto-configures which timeframes are monitored and their line widths, so you do not need to touch the individual Timeframe rows. Choose **Custom** only if you want to set the six timeframes yourself. Then set your chart at or below the preset's fastest timeframe - the Suite reads higher timeframes from a lower chart, never the reverse. On TheStrat Classic that means a 30m chart or lower (Scalp: 5m, Day Trade: 15m). Any slot faster than your chart timeframe shows as a dimmed, empty table row.
3. Choose a **Label Style**. Leave it on **TheStrat** for standard Strat combo notation (for example 2d-1-2u HAM), or switch to **Universal** for plain-language labels (REV, CONT, INS, OUT) if you are new to TheStrat.
4. Optionally tune which signals show under the Signals panels, then create one TradingView alert on the indicator with the condition "Any alert() function call" - the consolidated alert, and the only path that carries the full message (trigger and target prices, FTFC). Use the named conditions ("Signal In-Force (TF1)", "Domino Setup", and so on) only when you want separate routing; they fire with fixed text.

## TheStrat Concepts (Primer)

These definitions describe the vocabulary used throughout the rest of this documentation, grounded in how TheStrat Suite actually computes each one.

- **Bar type 1 (inside bar)** - A candle whose high did not exceed the prior bar's high and whose low did not undercut the prior bar's low. It sits entirely inside the previous bar's range, meaning the market paused and coiled. Type 1 bars are the setup state that precedes reversals and continuations.
- **Bar type 2u** - A candle that broke above the prior bar's high but did not break below its low. It is a one-sided directional (up) bar. A bar that has gone 2u can only ever close as 2u or 3, never back to 1 or 2d.
- **Bar type 2d** - A candle that broke below the prior bar's low but did not break above its high. It is a one-sided directional (down) bar, and like 2u it can only resolve to 2d or 3.
- **Bar type 3 (outside bar)** - A candle that broke both the prior bar's high and its low, engulfing the entire prior range. It is graded directionally by its close versus open: 3u if it closed above its open (bullish), 3d if it closed below (bearish). A type 3 bar represents range expansion and volatility.
- **C1 / C2 / CC notation** - These name the candles relative to the current one, as used in the data table and combo labels. CC is the current (forming) candle, C1 is the prior completed candle, and C2 is the candle before C1. A combo like 2d-1-2u reads left to right as C2-C1-CC. In the Full data table the columns are TF, C2, C1, AS, and CC, where the **AS** column flags whether C1 is actionable (HAM, SHO, or INS).
- **In-Force vs setup** - A setup is a structure that has not yet triggered (for example C1 is an inside bar but CC has not broken out). A signal is In-Force once CC has actually broken the trigger and price is currently beyond it (or, for Failing 2s, once the reclaim is confirmed). In-Force means the trade is live, not merely potential; many display options (magnitude, exhaustion, take action windows) can be restricted to In-Force only.
- **Failing 2 (Range Reclaim, F2u / F2d)** - A type 2 bar that broke out but then failed: price reclaimed back inside the prior range or closed against the breakout direction. F2u is a failed upside breakout (bearish), F2d is a failed downside breakout (bullish). Trapped breakout traders fuel the reversal. Also called a Potential 3 or 1-bar Rev Strat. Notation is always uppercase `F2u` / `F2d` and is tense-free (`NOTATION-1`): whether the F2 is forming or completed is carried by its slot in the combo (last token = forming candle), the `*` potential prefix, and words like FAILING - not by letter case.
- **Failing 2 detection methods** - How a fail is confirmed: **Reclaim** (the candle closes back inside C1's range), **Open** (the candle closes against its own breakout direction), **Reclaim + Open** (both conditions required), or **Reclaim OR Open** (either one). Reclaim is the default.
- **FTFC (Full Timeframe Continuity)** - Agreement across every monitored timeframe on direction, measured by each bar's close versus its open. FTFC Up means all enabled timeframes closed above their open; FTFC Down means all closed below. If they disagree it reads as Conflict. Used as an optional filter to keep signals aligned with the broader structure.
- **Magnitude vs Exhaustion targets** - **Magnitude** is the nearest take-profit target, the opposite extreme of C2's range projected from the trigger. **Exhaustion** is an extended target: the first intact prior swing high or low (a pivot) found by scanning back beyond C2. Magnitude is the first objective; exhaustion is where the move is structurally likely to run out.
- **Hammer / Shooter** - Single-candle rejection patterns. A Hammer tested lower and got rejected (long lower wick), implying bullish backing; a Shooter tested higher and got rejected (long upper wick), implying bearish backing. Detection strictness is configurable (Broad, Classic, or Pin Bar), and you can optionally require the candle's color to match its direction. Used as a confluence filter on most signals.
- **Domino** - A stack of consecutive inside bars across adjacent timeframes that all carry a visible setup in the same direction. When the run reaches your minimum-timeframe threshold it flags coiled, multi-timeframe energy primed to release. Domino respects your signal filters and de-duplicates timeframes that share the same underlying candle.
- **Lead signal** - The highest enabled timeframe that currently has a signal In-Force. When the Lead Signal filter is on, lower-timeframe signals are only shown if they align with the Lead's direction, keeping your entries in step with the dominant structure. If no higher timeframe is In-Force, all signals are shown.

## Timeframe Preset

This panel controls how the indicator's six timeframe slots are configured. Choosing a built-in preset is the fastest way to get a sensible multi-timeframe setup; choosing Custom hands full control to the Timeframes panel below.

- **Preset** (default: TheStrat Classic | options: Custom / TheStrat Classic / Scalp / Day Trade / Futures/Crypto / Swing Trade / Investing) - Auto-configures which timeframes are monitored, which ones are enabled, and the line width used to draw each one. When set to anything other than Custom, your manual Timeframe 1-6 rows are ignored and greyed out (a direct visual signal that the manual config is inert), so you only edit those rows after switching to Custom. Whichever preset you pick, set your chart at or below its fastest timeframe - any slot faster than the chart timeframe shows as a dimmed, empty table row. What each preset sets:
  - **Custom** - Disables the preset override entirely and makes the Timeframes (Custom) panel editable. The six rows then use whatever you set them to.
  - **TheStrat Classic** - 30m, 1H (60), Daily, Weekly, Monthly, Monthly. TF1-TF5 are enabled (TF5 Monthly on), TF6 off. Widths increase with timeframe (1 / 1 / 2 / 3 / 4).
  - **Scalp** - 5m, 15m, 30m, 1H (60), 1H (60), 1H (60). TF1-TF4 enabled, TF5 and TF6 off. Widths 1 / 2 / 3 / 4 / 4.
  - **Day Trade** - 15m, 30m, 1H (60), 4H (240), Daily, Daily. TF1-TF5 enabled (TF5 Daily on), TF6 off. Widths 1 / 1 / 2 / 3 / 4.
  - **Futures/Crypto** - 1H (60), 4H (240), 12H (720), Daily, Weekly, Weekly. TF1-TF5 enabled (TF5 Weekly on), TF6 off. Widths 1 / 2 / 2 / 3 / 4.
  - **Swing Trade** - 4H (240), Daily, Weekly, Monthly, Monthly, Monthly. TF1-TF4 enabled, TF5 and TF6 off. Widths 1 / 2 / 3 / 4 / 4.
  - **Investing** - Weekly, Monthly, Quarterly (3M), Yearly (12M), Yearly (12M), Yearly (12M). TF1-TF4 enabled, TF5 and TF6 off. Widths 1 / 2 / 3 / 4 / 4.

  Note: under any preset, the per-timeframe Open lines are forced off and TF6 is always off, regardless of the disabled manual rows behind them.

- **Label Style** (default: TheStrat | options: TheStrat / Universal) - Controls the notation used on chart signal labels. TheStrat shows standard Strat combo notation (for example 2d-1-2u HAM). Universal shows plain-language labels (REV (reversal), CONT (continuation), INS (inside), OUT (outside), EXP (expansion), plus FAILING when a breakout is failing) with a direction arrow and a diamond marker for hammer/shooter confidence. Choose Universal if you are new to TheStrat or prefer a simplified read; switch to TheStrat once the combo notation is second nature. This setting only changes label wording, not which signals fire.

## Timeframes (Custom)

These six rows define your own multi-timeframe stack. They are only editable when Preset = Custom; selecting any other preset greys out every field here (`FIX MOD-3`), since the preset supplies the values instead. Each timeframe has four controls on two inline rows: an Enabled toggle, the Timeframe itself, a line Width, and an Open toggle.

- **Timeframe 1** (Enabled default: off | Timeframe default: 15 | Width default: 1 | Open default: off)
- **Timeframe 2** (Enabled default: off | Timeframe default: 30 | Width default: 1 | Open default: off)
- **Timeframe 3** (Enabled default: off | Timeframe default: 60 | Width default: 1 | Open default: off)
- **Timeframe 4** (Enabled default: off | Timeframe default: D | Width default: 2 | Open default: off)
- **Timeframe 5** (Enabled default: off | Timeframe default: W | Width default: 3 | Open default: off)
- **Timeframe 6** (Enabled default: off | Timeframe default: M | Width default: 4 | Open default: off)

For each row:
- **Enabled** - Turns monitoring of that timeframe on or off. A disabled timeframe contributes nothing: no levels, no signals, no data-table row, and no part in FTFC. Note that all six rows default to off in Custom mode, so a fresh Custom setup shows nothing until you enable at least one timeframe.
- **Timeframe** - The TradingView timeframe string for that slot (minutes as numbers, or D / W / M, and so on). The defaults above climb from 15m through Monthly so a Custom stack starts ordered from fast to slow.
- **Width** (minval: 1 | maxval: 5) - Line thickness, in pixels, for every level this timeframe draws on the chart. Higher numbers make a timeframe visually dominant; the defaults deliberately make slower timeframes thicker (1 up to 4) so the higher-timeframe structure stands out from lower-timeframe noise.
- **Open** - Draws that timeframe's period Open as a reference line on the chart. Off by default for every slot. Turn it on for the timeframes whose open you trade against (for example the Daily or Weekly open as an above/below pivot); leave it off elsewhere to avoid clutter. Under any preset this line is forced off, and it is also suppressed while a slot is previewed or straddle-shifted, because the served open would be a proxy rather than the real period open (`FIX HTF-STRADDLE-1`).

## Signals - Reversals

Reversal signals fire when the current bar (CC) breaks the prior bar's range in the direction opposite to the prior trend. Each signal has its own per-signal filters that you can stack to tighten the setup. The filter checkboxes sit on the same row as the signal toggle (HAM/SHO, F2, and FTFC, where applicable). All filters are off by default, so out of the box you see the raw pattern.

- **Inside Reversals** (default: on | options: on / off) - Detects the C1-inside reversal, where the prior bar (C1) is an inside bar and the current bar (CC) breaks in the opposite direction to the bar before the inside bar (C2), for example 2d-1-2u. This is the classic coiled-then-reversed Strat pattern: price consolidated inside a prior range, then broke out against the preceding move. A C2 with no direction (a flat doji) counts as the reversal side, so the setup still qualifies (`FIX CSS-1`). On by default because it is the bread-and-butter setup. Turn it off if you only want direct reversals or are decluttering the chart.
- **HAM/SHO** (default: off) - Per-signal momentum filter for Inside Reversals. Requires C1 (the inside bar) to be a hammer or shooter, meaning the bar tested one side and got rejected, which adds conviction that the breakout direction has structural backing. Turn it on to keep only inside reversals that coil off a rejection candle. What counts as a hammer or shooter is set globally under Filters - Hammer/Shooter Detection (Definition, Match Candle Color), so this filter inherits that definition.
- **FTFC** (default: off) - Per-signal Full Timeframe Continuity filter for Inside Reversals. Requires all monitored timeframes to agree on direction, removing setups that fight the broader structural alignment. Turn it on when you only want reversals that are swimming with the higher-timeframe current. Note that FTFC can be loosened by the Advanced - Exhaustion Behavior setting "Exhaustion Excludes from FTFC," which drops timeframes whose move is structurally complete from the calculation.

- **2-2 Reversals** (default: on | options: on / off) - Detects the direct reversal with no inside-bar pause, where CC immediately breaks opposite to C1, for example 2d-2u. This is a faster signal than an inside reversal but carries less coiled energy because there was no consolidation. On by default. This signal has three filters: HAM/SHO, F2, and FTFC.
- **HAM/SHO** (default: off) - Per-signal momentum filter for 2-2 Reversals. Requires C1 to be a hammer or shooter (tested and rejected one side) before the reversal qualifies. Uses the global Hammer/Shooter Detection definition. When this filter admits a reversal, that reversal still obeys this panel's own FTFC toggle (`FIX BTC-1`).
- **F2** (default: off) - Per-signal Failed-2 filter unique to 2-2 Reversals. Requires C1 to itself be a Failed 2, for example F2d-2u. This adds structural confluence: the prior bar already reversed a failed breakout, so traders trapped by that failure fuel the current move. Turn it on to demand that the bar feeding the reversal was itself a trap. This filter interacts with Advanced - Failing 2 Detection: F2 classification depends on Enable Failing 2 Detection being on and on the Detection Method you choose (Reclaim, Open, etc.), so the F2 filter only finds setups that the detection settings actually flag as Failed 2s.
- **FTFC** (default: off) - Per-signal Full Timeframe Continuity filter for 2-2 Reversals. Requires all monitored timeframes to agree on direction. Same behavior and Exhaustion-Excludes interaction as the Inside Reversals FTFC filter.

## Signals - Continuations

Continuation signals fire when CC breaks the prior range in the same direction as the preceding move. They are trend-following setups. Filters work the same way as on the reversal panel and are off by default. A 2-2 Continuation is read from C1 and CC only - whatever came before C1 does not gate it.

- **Inside Continuations** (default: on | options: on / off) - Detects the C1-inside continuation, where C1 is an inside bar and CC breaks in the same direction as C2, for example 2u-1-2u. Price paused to consolidate and then continued the prior move, a trend continuation with a built-in pullback. On by default. Has HAM/SHO and FTFC filters.
- **HAM/SHO** (default: off) - Per-signal momentum filter for Inside Continuations. Requires C1 (the inside bar) to be a hammer or shooter, adding conviction from a tested-and-rejected side. Uses the global Hammer/Shooter Detection definition.
- **FTFC** (default: off) - Per-signal Full Timeframe Continuity filter for Inside Continuations. Requires all monitored timeframes to agree on direction. Because a continuation already runs with the prior move, this filter keeps only those that also align across every monitored timeframe.

- **2-2 Continuations** (default: off | options: on / off) - Detects sustained momentum with no pause, where CC keeps pushing in C1's direction, for example 2u-2u. Off by default because any two trending bars qualify, so it can be noisy without filters. Turn it on when you want to catch running momentum, and consider pairing it with HAM/SHO or FTFC to cut the noise. Has HAM/SHO and FTFC filters.
- **HAM/SHO** (default: off) - Per-signal momentum filter for 2-2 Continuations. Requires C1 to be a hammer or shooter. Uses the global Hammer/Shooter Detection definition. This is the primary noise-reduction filter for this signal.
- **FTFC** (default: off) - Per-signal Full Timeframe Continuity filter for 2-2 Continuations. Requires all monitored timeframes to agree on direction, the second main way to filter the noise on this signal. Applies to HAM/SHO-admitted continuations as well (`FIX BTC-1`).

## Signals - Reclaims

- **Failing 2s (Range Reclaims)** (default: off | options: on / off) - Shows Range Reclaim signals, where price breaks out and then reverses back, also known as Failing 2s, Potential 3s, or 1-bar Rev Strat. A bullish Range Reclaim is a failed bearish breakout (F2d) and a bearish Range Reclaim is a failed bullish breakout (F2u). Off by default. Turn it on to trade the reversal of a failed breakout. A Failing 2 is in force from *detection* - the failure itself is the event, not a trigger cross - and a not-yet-confirmed one carries the standard `*` potential prefix (`*F2u` / `*F2d`, `NOTATION-1`). This toggle controls whether the signals are displayed; the actual detection and what qualifies as a Failing 2 is governed by Advanced - Failing 2 Detection (Enable Failing 2 Detection plus the Detection Method). If Failing 2 Detection is disabled there, no Range Reclaims will appear here regardless of this toggle.
- **FTFC** (default: off) - Per-signal Full Timeframe Continuity filter for Failing 2s. Requires all monitored timeframes to agree on direction, keeping only reclaims that align with the broader structure. Note: a Range Reclaim by nature fades a just-failed breakout, so demanding full continuity can sharply reduce how many signals appear.

## Signals - Expansions

Expansion signals deal with outside bars (3-bars), which take out both sides of the prior bar's range. Both signals are off by default.

- **3-2 Expansions** (default: off | options: on / off) - Detects the case where C1 was an outside bar (3-bar) and CC then commits to one direction, for example 3-2u. After C1 took both sides of its prior bar, CC resolves the indecision, which is often powerful when the 3-bar shook out weak hands first. Off by default. Has HAM/SHO and FTFC filters.
- **HAM/SHO** (default: off) - Per-signal momentum filter for 3-2 Expansions. Requires C1 (the 3-bar) to also be a hammer or shooter, meaning it tested and rejected one side despite the range expansion, which adds directional conviction. Uses the global Hammer/Shooter Detection definition.
- **FTFC** (default: off) - Per-signal Full Timeframe Continuity filter for 3-2 Expansions. Requires all monitored timeframes to agree on direction.

- **Outside Bars (3 Exp)** (default: off | options: on / off) - Detects when CC itself is a 3-bar, engulfing C1's entire range. This represents range expansion and rising volatility, with direction set by close versus open (3u is bullish, 3d is bearish). Off by default. Has only an FTFC filter (no HAM/SHO). Target behavior for this signal is configured separately under Advanced - Outside Bars (Show Magnitude for Outside Bars, Show Exhaustion for Outside Bars), and its take-action zone under Advanced - Take Action Windows (Include Outside Bars).
- **FTFC** (default: off) - Per-signal Full Timeframe Continuity filter for Outside Bars. Requires all monitored timeframes to agree on direction before the outside-bar signal (and, by extension, its magnitude and exhaustion targets) is shown.

## Filters - Hammer/Shooter Detection

A hammer or shooter (collectively "actionable" candles, also called pivot machines) is a single bar that pushed hard into one side of its range and got rejected, closing back near the other end. Hammers reject lows (bullish rejection), shooters reject highs (bearish rejection). Several signal filters in the Signals panels (the HAM/SHO toggles) depend on these definitions, so the strictness you pick here directly controls how many of those filtered signals appear.

- **Definition** (default: Broad (Loose) | options: Broad (Loose) / Classic / Pin Bar (Strict)) - sets how demanding the indicator is before it will call a candle a hammer or shooter. Each level is a different geometric test:
  - **Broad (Loose)** is purely about where the open and close sit within the bar's range. A hammer needs both the open and the close strictly above the range midpoint; a shooter needs both strictly below it. The comparison is strict, so a body centered exactly on the midpoint qualifies as neither (`FIX BTC-2`), and a flat, zero-range bar never qualifies. It ignores body size and wick length, so it catches the most candles, including ones with sizable bodies. Best when you want maximum signal flow and treat the HAM/SHO filter as a light directional lean rather than a strict gate.
  - **Classic** is the textbook TheStrat actionable signal: a small body (body at most 30% of the range), a dominant rejection wick at least 3x the body, the opposite wick kept short (at most 35% of the range), the body center sitting in the rejection third of the bar (within 33% of the relevant extreme), and the close within 25% of that extreme. This is the balanced choice and matches how most TheStrat traders define a hammer or shooter.
  - **Pin Bar (Strict)** only checks that both the open and the close finish very near the rejecting extreme (each within 25% of the high for a hammer, within 25% of the low for a shooter). It does not require a small body or a 3x wick ratio, but because it demands the open AND close both crowd into the last quarter of the range, it isolates the cleanest, most decisive rejection candles and produces the fewest signals. Use it when you only want the highest-conviction pivots.

  Switching this setting changes which bars satisfy every HAM/SHO toggle across the Reversals, Continuations, and Expansions panels at once, so loosen it if HAM/SHO-filtered signals dry up, and tighten it if you are getting too many marginal ones.

- **Match Candle Color** (default: off) - when on, a hammer must also be a green (bullish, close above open) candle and a shooter must also be a red (bearish, close below open) candle to qualify. When off, color is ignored and the geometric test alone decides. Turn it on to demand that the candle's close direction agrees with its rejection direction (an extra confirmation layer); leave it off if you accept, for example, a long lower-wick rejection that still closed slightly red as a valid hammer. Note this acts as an additional gate on top of the Definition setting, so combining Match Candle Color with Pin Bar (Strict) is the most restrictive possible configuration.

## Filters - Domino Setups

A Domino is a stacking signal: it fires when several consecutive timeframes (scanning from your highest enabled TF down to your lowest) all currently show an inside bar (a Type 1, where the forming bar is still trading inside the prior bar's range) that also carries a visible setup in the SAME direction. Because the indicator only counts a timeframe if it actually has a drawn signal under your current filter settings, the Domino "respects your signal filters" - a TF with an inside bar but no enabled/visible setup breaks the chain. The result is a read on how many timeframes are coiled and leaning the same way at once, which is the classic "dominoes lined up" pre-breakout condition. The indicator computes the best bullish run and the best bearish run separately and reports whichever is longer. It also de-duplicates: if two adjacent timeframes are reading the exact same underlying candle (same start time, common at TF coupling moments), that shared candle is not double-counted.

- **Minimum Timeframes** (default: 2 | range: 2 to 6) - the minimum length of the consecutive inside-bar run required before a Domino is recognized. At 2, any two stacked, same-direction inside-bar setups trigger it; raising it (up to 6) demands a longer, more aligned stack and produces rarer, higher-conviction Dominoes. This threshold governs both the data table row and the Domino alert, so it is the single dial for how selective Domino detection is.

- **Show in Data Table** (default: on) - adds a Domino row to the data table whenever the detected run meets or exceeds Minimum Timeframes, listing the participating timeframes lowest-first (for example "Domino: 15m+30m+1H"). Turn it off to keep the table compact if you would rather rely on alerts alone. This only controls the table display; it does not affect whether Domino alerts fire (see Advanced - Domino Setups for that) or the underlying detection.

## Filters - Lead Signal

The Lead is the highest enabled timeframe that currently has a signal in-force (a breakout that has triggered and is holding). In TheStrat's cascade logic, that highest in-force timeframe sets the dominant directional bias, and lower timeframes should be traded with it, not against it. This filter enforces that: it finds the Lead by scanning from your top timeframe downward, takes its direction (bullish if its high-side signal is in force, bearish if its low-side signal is in force), and then filters the timeframes below the Lead so only setups aligned with the Lead's direction remain visible. If no higher timeframe has a signal in force, there is no Lead and nothing is filtered (all signals show).

- **Only Show Signals Following the Lead** (default: off) - turns the Lead filter on. When on, counter-trend setups on timeframes below the Lead are suppressed, with one nuance worth knowing from the logic: a confirmed counter-trend Failing 2 (a lower-TF F2 fighting the Lead) has all its lines, labels, targets and stops removed entirely, while a trend-aligned Failing 2 keeps its trigger and just drops its counter-side targets/stop; a regular counter-trend signal has its counter-trend side suppressed. Lower-TF signals that already point the Lead's way pass through untouched. When off, every enabled timeframe's signals draw independently with no cross-timeframe gating. Turn it on to stop fading the higher-timeframe move and to focus on with-trend entries; leave it off if you intentionally trade counter-trend reversals or want to see the full unfiltered picture. The table shows a "Lead:" row (for example "Lead: 1H BULL", or "No Lead") so you can see which timeframe is driving. Interaction worth knowing: the Advanced setting "Exhaustion Disables 'In Force'" affects this filter directly - with it on, once the Lead's move hits its target it stops counting as in-force, which releases the suppression and lets opposite (reversal) setups appear on the lower timeframes. **Scope (changed in v3.0.1):** this is a chart filter only. The data table is deliberately left alone and keeps reporting what every timeframe is actually doing, including signals the chart is hiding - the table is the unfiltered reference you check precisely *because* the chart is filtered. Earlier builds cleared the in-force state outright (`FIX P1-g`), which made suppressed timeframes read as quiet in the table too; `FIX LEAD-TABLE-1` reverts that. Alerts, targets, stops and Take Action Windows follow the chart, as before.

## Targets - Magnitude & Exhaustion

In TheStrat, every actionable signal carries built-in profit targets drawn from the structure of the candles themselves. The Suite plots two kinds:

- **Magnitude** is the C2 high or low (the extreme of the candle two bars back, the one whose break defines the setup). It is the first, nearest, most reliable target: the level the move is structurally "expected" to reach.
- **Exhaustion** is the first intact prior swing high or low located beyond C2. It is the extended target: where the move is likely to run out of room. Exhaustion is found by scanning back for an untouched pivot (`findExhaustionLevels`), so it only appears when such a level exists.

This panel controls whether each target type is drawn and when.

- **Show Magnitude Levels** (default: on | options: on / off) - Draws the magnitude (C2 high/low) take-profit target for actionable signals. Leave this on for the core "where is my first target" read on every setup. Turn it off if you only want to see entry triggers and exhaustion, or to declutter a busy chart. Note the exact scope of "off": potential setups and outside-bar expansions lose their magnitude lines (outside-bar magnitude requires this switch), but a signal that is already in-force still draws its magnitude target through an in-force fallback path, so live trades keep their first target on the chart.

- **Only When In-Force** (default: off | options: on / off) - When off, magnitude is shown on potential setups (before the breakout) as well as live ones. When on, magnitude only appears once the signal has actually triggered (price broke and held beyond the trigger). Turn it on if you want a clean chart that shows targets only on trades that are actually live, and not on every candle that might become a setup. Has no effect when Show Magnitude Levels is off.

- **Show Exhaustion Levels** (default: on | options: on / off) - Draws the exhaustion level (prior swing high/low beyond C2) as the extended target. Keep it on to see how much runway a move has past its first target, which is useful for deciding whether to hold for a second target or trail. Turn it off if you trade to magnitude only. Exhaustion is also what the Take Action Window can extend to, and outside-bar exhaustion requires it too (`FIX CSS-2`), so turning this off limits those features.

- **Only When In-Force** (default: off | options: on / off) - The exhaustion counterpart to the magnitude toggle above. Off shows exhaustion on potential setups; on hides it until the signal triggers, so you only see extended targets on live trades. Has no effect when Show Exhaustion Levels is off.

- **Only After Magnitude Hit** (default: off | options: on / off) - When on, an exhaustion level stays hidden until price has actually reached the magnitude level first, then it appears. This mirrors how the move develops: first target, then extended target. Turn it on to avoid being distracted by a far-off exhaustion level while price is still working toward magnitude. When off, exhaustion is drawn as soon as it otherwise qualifies, alongside magnitude. This setting has no effect when Show Exhaustion Levels is off.

## Targets - Take Action Windows

A Take Action Window is the shaded zone between the entry trigger and the target. It gives you an at-a-glance picture of the trade's "working area": where price has room to travel from the trigger toward magnitude (or, optionally, all the way to exhaustion). The window is colored with the signal's bullish or bearish color.

- **Show Take Action Windows** (default: on | options: on / off) - Master switch for the shaded trigger-to-target zone on actionable signals. Leave it on for an immediate visual of each setup's runway. Turn it off if you prefer just the lines without fills. With this off, the zone-to-magnitude fill is suppressed even if the extend-to-exhaustion options below are on (those govern only the exhaustion portion).

- **Only When In-Force** (default: on | options: on / off) - When on (the default), the window is only drawn once the signal has triggered, so you see a working zone only on live trades. Turn it off to also shade the zone on potential setups before the breakout. This is the master in-force gate for the windows: it applies to the magnitude window, to Failing 2 windows, and to Outside Bar windows (see Advanced - Take Action Windows). It has no effect when Show Take Action Windows is off (except that it still gates the Failing 2 and Outside Bar windows, which have their own enable toggles).

- **Extend to Exhaustion** (default: on | options: on / off) - When on, the window stretches all the way to the exhaustion level instead of stopping at magnitude, showing the full potential runway of the move. Turn it off to keep the window tight to the first (magnitude) target. This draws independently of the main Show Take Action Windows switch, so you can have the exhaustion extension on even with the magnitude fill off. It relies on a valid exhaustion level existing for that signal.

- **Only When In-Force** (default: on | options: on / off) - The in-force gate specific to the exhaustion extension. When on (default), the window only extends to exhaustion after the signal triggers; when off, it can extend on potential setups too. Has no effect when Extend to Exhaustion is off.

- **Fill Opacity** (default: 8 | range: 0 to 100) - How solid the shaded interior of the window is. 0 is fully transparent (no visible fill), 100 is fully solid. The low default of 8 keeps the zone subtle so it does not bury the candles. Raise it if the window is hard to see against your chart background.

- **Border Opacity** (default: 30 | range: 0 to 100) - How solid the window's outline is. 0 hides the border, 100 makes it fully opaque. The default of 30 gives a faint edge that defines the zone without dominating. Pair a higher border opacity with a low fill opacity if you want the zone outlined but not filled in.

## Stops - Stop Levels

Stop levels mark the price where your trade thesis is invalidated, drawn on the opposite side of the trigger from your targets. This panel controls whether they appear, how they are anchored, when they move to break even, and how they are colored.

- **Enable** (default: off | options: on / off) - Turns stop level lines and labels on. Stops are drawn on the opposite side of the trigger from your magnitude/exhaustion targets. Once a signal goes in-force (the breakout occurs), the stop is locked and persists for the remainder of that period even if the signal later stops being in-force, so a stop you were managing does not vanish mid-trade. Locked stops also survive a chart reload: with stops enabled, the lock is rebuilt from bar history so a refresh cannot reset it (`FIX P1-a`). While this is off, the greyed-out controls below cannot take effect.

- **Reference** (default: CC | options: CC / C1) - Chooses which bar anchors the stop. CC places the stop at the breakout bar's opposite extreme: it updates in real time as the candle forms and locks when the candle closes, giving tighter risk that is invalidated when the breakout candle itself is reversed. C1 places the stop at the prior bar's opposite side and locks it when the signal goes in-force, giving wider risk that is invalidated only when the full setup range is taken out. Choose CC for tighter, more reactive stops and C1 for more room to let the structure play out. Important interaction: Failing 2 (Range Reclaim) signals always use CC regardless of this setting, because their invalidation is defined by the reclaim bar itself.

- **Smallest Timeframe Only** (default: off | options: on / off) - Draws only the stop belonging to the smallest enabled timeframe that currently has one; stops on larger timeframes are hidden. This is a display filter, nothing else changes: larger-timeframe stops keep tracking underneath, their break-even mechanics still arm, and alert messages still carry their prices. Useful when several timeframes are in force at once and stacked stop lines clutter the chart - the tightest stop is usually the one being managed. "Smallest" follows slot order, the same convention the Lead Signal filter uses. New in v3.0.0 (`STOP-SMALLEST-1`).

- **Break Even at Magnitude** (default: off | options: on / off) - When price reaches the magnitude (first target) level, the stop moves to the entry trigger so the trade can no longer lose. If the signal has no magnitude level, it uses the exhaustion level as the move-to-break-even trigger instead. Turn this on to bank risk-free runners early; leave it off if you prefer to let the original stop ride to its structural invalidation.

- **Break Even at Exhaustion** (default: off | options: on / off) - The same break-even mechanic, but triggered when price reaches the exhaustion (extended target) level. If no exhaustion level exists, it falls back to using magnitude as the trigger. This is a later, more conservative break-even point than Break Even at Magnitude; you can enable both, in which case the stop moves to break even at whichever level price reaches.

- **Color Mode** (default: Opposite Signal | options: Opposite Signal / Match Signal / Custom) - Sets how stop lines and labels are colored. Opposite Signal colors a bullish trade's stop with the bearish signal color and a bearish trade's stop with the bullish signal color, so the stop visually reads as the "danger" side. Match Signal colors the stop the same as its own signal, keeping each trade monochromatic. Custom uses the single Custom Color below for all stops regardless of direction.

- **Custom Color** (default: #FFFFFF white | color picker) - The single color applied to all stop lines and labels when Color Mode is set to Custom. This control is greyed out unless stops are enabled AND Color Mode is set to Custom, so it only appears active when it can actually take effect (`FIX MOD-3`).

## Display - Preview Mode

- **Preview Mode** (default: Auto | options: Off / On / Auto) - Controls whether the indicator projects next-period levels onto the chart before the new period begins. Off never previews and must be selected to use TradingView's Bar Replay mode. On always previews the next period's levels. Auto previews only when the market is likely not actively trading, and detects this per asset type:
  - **Stocks, ETFs and funds, ADRs, indices, and options** - before 9:30 AM ET, after 4:00 PM ET, and on weekends.
  - **Futures, forex, and commodities** - over the weekend close (Friday 5 PM through Sunday 6 PM ET) and during the daily 5-6 PM ET maintenance window (Monday-Thursday).
  - **Crypto** (trades 24/7) - only via the outage fallback below.
  - **Holiday / outage fallback (all asset types)** - preview engages when the current time is well past the live bar's *scheduled close* (three chart periods or four hours, whichever is larger). Measuring from the scheduled close rather than the bar's open stamp matters: some exchanges glue holiday half-sessions into the next trade date, giving a live bar a days-old open stamp, and an open-stamp measurement would flip preview on in the middle of a live session (`FIX GLUE-1`; the full story is in `docs/v2.2.3_holiday_glue_fix.md`). A live bar's scheduled close is always in the future, so this fallback can never misfire while a session trades.

  Bar-replay caveat: Auto automatically disables itself during bar replay, but if you specifically need clean replay behavior, set Preview Mode to Off.

**Straddle handling (related, automatic).** Calendar timeframes coarser than your chart (Weekly, Monthly, Quarterly, Yearly) can go stale mid-bar when a chart bar straddles a period boundary - for example a weekly chart bar spanning a mid-week quarter roll. When that happens on the live bar, the Suite applies the same shift preview uses, and where possible rebuilds the true forming candle from completed daily sub-bars so the slot shows real data instead of an estimate (`FIX HTF-STRADDLE-1`, with period-key normalization from `FIX RECON-KEY-1`). A previewed or straddled slot that could *not* be rebuilt shows `?` in the data table's CC cell; any previewed or straddled slot - rebuilt or not - never fires alerts. Setting Preview Mode to Off disables straddle handling along with preview - that is part of why Off is the required setting for Bar Replay.

## Display - Data Table

The data table is the multi-timeframe scoreboard showing each monitored timeframe's bar state at a glance.

- **Show Data Table** (default: on | options: on / off) - Shows or hides the multi-timeframe data table.

- **Mode** (default: Full | options: Full / Compact) - Full renders the complete table using Strat notation with four columns per timeframe row: C2 (two-bars-back state), C1 (prior bar state), AS (the actionable signal, if any: HAM, SHO, or INS), and CC (the current forming bar's state). This is the detailed view for traders fluent in the cascade. Compact collapses each timeframe to a single colored label in a horizontal row. Compact is the simplified view for newer traders or a cleaner chart; what the colors mean there is set by Compact Cell Color below. (Renamed in v3.0.1 from "Full (TheStrat)" / "Compact (Universal)" - if you had Compact selected on an earlier build, reselect it once after updating.)

- **Compact Cell Color** (default: Bar State | options: Bar State / Signals In Force) - What the colored timeframe cells mean in Compact mode; greyed out in Full mode. Each option mirrors one of the two colorings the Full table already uses, so the compact row is a genuine condensation of it rather than a third scheme. **Bar State** colors every cell by that timeframe's current candle, exactly like the Full table's **CC column**: yellow when inside and above its open, orange when inside and below, green/red for directional bars, plus the outside-bar and Failing 2 colors. Every timeframe always carries a color, so you can read the whole stack at a glance. **Signals In Force** colors a cell only when a signal is actually in force on that timeframe, exactly like the Full table's **TF column**: the row stays dark until something is live, then lights up in that signal's color. An enabled outside bar counts as in force by definition (it broke both sides of C1); an inside bar never does, the same rule as `FIX TFCOLOR-1`. Use Bar State to see structure everywhere, Signals In Force to see only what is actionable right now. New in v3.0.1 (`TABLE-COMPACT-COLOR-1`).

  In both modes, Failing 2s render in canonical uppercase (`F2u` / `F2d`) whether forming or completed (`NOTATION-1`). While preview is active the table shows a yellow "PREVIEW MODE" banner, and a previewed slot whose forming candle could not be rebuilt from real daily sub-bars shows `?` in its CC cell instead of a guessed bar type (`FIX HTF-STRADDLE-1`).

- **Position** (default: Top Right | options: Bottom Center / Bottom Left / Bottom Right / Middle Center / Middle Left / Middle Right / Top Center / Top Left / Top Right) - Places the table in any of the nine standard chart anchor positions. Move it to keep it clear of your price action or other indicators.

- **Text Size** (default: Small | options: Tiny / Small / Normal / Large) - Sizes the table text. Use Tiny to minimize footprint or Large for readability on high-resolution or distant displays.

- **Use Dash Separator** (default: on | options: on / off) - Separates candle values with a dash for legibility (for example 2u-2d instead of 2u2d). Turn off for a more compact, run-together notation.

## Display - Labels

These settings govern the text labels attached to level lines, including their placement, content, and styling.

- **Show Timeline Labels** (default: on | options: on / off) - Shows labels at the end of each level line, anchored to that line's end time so the label sits where the line stops.

- **Offset (bars)** (default: 0 | range: 0 to 20) - Shifts timeline labels by the given number of bars away from the line end, useful for nudging labels off a busy line cluster. Greyed out when Show Timeline Labels is off (`FIX MOD-3`).

- **Show Floating Labels** (default: off | options: on / off) - Shows labels at a fixed offset from the current bar so they stay visible on screen as price moves, rather than being pinned to a line's end. Useful when you want level names always in view at the right edge.

- **Offset (bars)** (default: 1 | range: -20 to 50) - Shifts floating labels relative to the current bar, where negative values move them left (into history) and positive values move them right (into the future area past the last bar). Greyed out when Show Floating Labels is off (`FIX MOD-3`).

- **Show Price in Label** (default: off | options: on / off) - Appends the actual price level to each label so you can read the exact value without hovering the line.

- **Show H/L in Labels** (default: off | options: on / off) - Adds "H" for high levels and "L" for low levels in both the on-chart labels and the alert messages, clarifying which side of a setup each level represents.

- **Text Size** (default: Small | options: Tiny / Small / Normal / Large) - Sizes the label text independently of the data table text.

- **Background Color** (default: #2e2e2e dark gray | color picker) - Sets the fill color behind label text.

- **Transparency** (default: 20 | range: 0 to 100) - Controls how see-through the label background is, where 0 is solid and 100 is fully invisible. Raise it to let chart content show through behind labels.

## Display - Bar Coloring

New in v3.0.0 (`BARCOLOR-1`). These settings paint the chart candles themselves - either by each bar's Strat classification or by Full Timeframe Continuity. Candle paint is off by default, one mode is active at a time, and each mode's controls below are greyed out unless that mode is selected.

- **Color Chart Candles** (default: Off | options: Off / Strat Candles / FTFC Candles) - Paints the chart's candles.
  - **Strat Candles** colors each chart bar by its Strat classification against the prior bar - inside 1, directional 2u/2d, outside 3u/3d, and Failing 2s - using the same classification (and Failing 2 Detection Method) as the data table's CC cell, so candle paint and table notation cannot disagree.
  - **FTFC Candles** colors each bar by Full Timeframe Continuity - all monitored timeframes above their open (up), all below (down), or conflicting. Historical bars are graded by that bar's close against each timeframe's open as it stood at the time, so painted history shows continuity as it actually developed, not as the periods finally closed.

- **1 (Inside) / 2 (Directional) / 3 (Outside) / F2 (Failing)** (default: all four on | options: on / off) - Strat Candles mode only: toggle candle paint per bar family. Colors follow the Style sections, so a custom palette applies to lines, labels, and candles together:
  - **1s** use the **1 (Inside Bar)** colors - `1u` (closed above its open) takes the bullish yellow, `1d` the bearish orange.
  - **2s** use the **Signal** colors - `2u` bullish, `2d` bearish.
  - **3s** use the **3 (Outside Bar)** colors - `3u` bullish, `3d` bearish.
  - **Failing 2s** use the **Range Reclaim** colors - `F2u` bearish, `F2d` bullish.

  With **F2 (Failing)** unchecked, or Failing 2 Detection disabled, a Failing 2 paints as its plain `2u`/`2d` (an `F2u` *is* a `2u`, shown without failure paint), so it follows the **2 (Directional)** toggle and Signal colors instead.

- **Up / Down / Conflict** (defaults: #4caf50 green / #f23645 red / #808080 gray | three color pickers) - FTFC Candles mode only: the candle colors for full timeframe continuity up, down, and conflict.

- **Color Conflict Bars** (default: on | options: on / off) - FTFC Candles mode only. When off, conflicted bars keep the chart's normal candle colors, so only full up/down agreement is painted.

- **Highlight Failing 2 Flips** (default: on | options: on / off) - FTFC Candles mode only. When continuity flips while a Failing 2 (Range Reclaim) is live on a monitored timeframe, the flip bar is painted with the Range Reclaim color instead of the plain continuity color. A reclaim that flips continuity is the highest-conviction version of the signal - trapped traders from the failed breakout fuel the new direction.

Cross-panel note: what counts as a Failing 2 - for Strat Candles paint and the FTFC flip highlight alike - follows **Advanced - Failing 2 Detection** (the Enable toggle and the Detection Method). With detection disabled there, no candle paints or highlights as an F2 anywhere in the Suite. And because the family colors are read from the Style panels below, changing a Style color also recolors the candles.

## Display - Bar Numbers

Unreleased (`BARNUM-1`). Prints each chart candle's Strat number beside it, classified against the prior candle by the same engine as Strat Candles and the data table's CC cell. Off by default; every control below is greyed out until **Show Bar Numbers** is on. Chart timeframe only: the data table already shows your other timeframes.

- **Show Bar Numbers** (default: off | options: on / off) - Turns the numbers on. They follow **Advanced - Failing 2 Detection** like the rest of the Suite. They are not meaningful on Heikin Ashi or other synthetic candles.
- **Notation** (default: Full | options: Full / Classic / Numbers)
  - **Full**: `1`, `2u`, `2d`, `3u`, `3d`, `F2u`, `F2d`, the data table's notation.
  - **Classic**: `1`, `2u`, `2d`, `3`. Outside bars drop their close direction and Failing 2s show as their plain `2u`/`2d`.
  - **Numbers**: `1`, `2`, `3`. Direction is carried by color alone.
- **Emphasis** (default: Tiered | options: Tiered / Uniform / Key Bars Only)
  - **Tiered**: 1s, 3s and Failing 2s in full color, plain 2s faded to about 35% strength. Setups start at 1s, 3s and failing 2s, so those stand out while trending 2s recede.
  - **Uniform**: every number at full strength.
  - **Key Bars Only**: plain 2s are hidden. In Classic and Numbers notation a Failing 2 shows as a plain 2, so it is hidden too.
- **Position** (default: Below | options: Below / Above / Broken Side) - Below and Above put every number on one side, so they read as one steady row. Broken Side puts the number on the side the candle took out: `2u` and `F2u` above, `2d` and `F2d` below, `3u` above and `3d` below by their close, and inside bars below.
- **Color** (default: Bar Type Palette | options: Bar Type Palette / Single Color, plus a color picker defaulting to #9598a1 gray) - Bar Type Palette takes each family's color from the Style sections, the same mapping Strat Candles uses (see Display - Bar Coloring). Single Color uses one color for every number, which is useful with Strat Candles on so color is not shown twice. Emphasis still applies.
- **Bars Shown** (default: 0 | 0 or more) - Numbers only the most recent N candles; 0 numbers every candle on the chart.

The forming candle's number is drawn lighter because it can still change: a `2u` can become a `3` or fail into an `F2u`. It takes full strength when the candle closes. Closed candles never change, so history never repaints. Text size is fixed by TradingView: the numbers are drawn with `plotshape`, which keeps them off the label budget the levels use but does not allow resizing text.

## Style - Level Colors

These inputs set the colors used to draw level lines and labels. There are two panels, one for bullish (high-side) levels and one for bearish (low-side) levels, with matching roles in each. Colors are purely cosmetic; they do not change which signals fire.

### Style - Bullish Levels

- **Signal** (default: green / color.green) - The color for an active, in-force bullish level (a triggered bullish signal). This is the main "live long setup" color.
- **Crossed** (default: #9c9c9c, light gray) - The color a bullish level changes to once price has crossed (hit) it, marking the level as already taken out so it visually recedes.
- **1 (Inside Bar)** (default: #ffeb3b, yellow) - The color for a bullish potential level coming from an inside bar (a type 1), that is, a setup that has not yet triggered on the high side.
- **3 (Outside Bar)** (default: #089981, teal-green) - The color for a bullish outside-bar (type 3) expansion level (3u, the high side of an outside bar).
- **Range Reclaim (F2d)** (default: #81c784, soft green) - The color for a bullish Range Reclaim signal, that is, a failed bearish breakout (F2d) where price broke the low and reclaimed back up.

### Style - Bearish Levels

- **Signal** (default: red / color.red) - The color for an active, in-force bearish level (a triggered bearish signal). The main "live short setup" color.
- **Crossed** (default: #4a4a4a, dark gray) - The color a bearish level changes to once price has crossed (hit) it, marking it as already taken out.
- **1 (Inside Bar)** (default: #ff9800, orange) - The color for a bearish potential level from an inside bar (type 1) that has not yet triggered on the low side.
- **3 (Outside Bar)** (default: #e91e63, pink/magenta) - The color for a bearish outside-bar (type 3) expansion level (3d, the low side of an outside bar).
- **Range Reclaim (F2u)** (default: #f77c80, soft red) - The color for a bearish Range Reclaim signal, that is, a failed bullish breakout (F2u) where price broke the high and reclaimed back down.

Note: these Signal colors are reused elsewhere. The Take Action Window fills derive from the bullish/bearish Signal colors, and stop-level lines can be tied to them through the Stops panel's Color Mode (Opposite Signal or Match Signal), so changing a Signal color here also shifts those elements.

## Alerts

The Suite condenses all multi-timeframe activity into one push so you do not have to watch the chart. There is one consolidated `alert()` that fires automatically, plus a full set of named `alertcondition()` entries you can wire to your own TradingView alerts. The settings below shape only the consolidated message.

### Alerts - Detailed

This panel controls which timeframes feed the consolidated alert message and which enrichment fields get appended to it.

- **TF1 / TF2 / TF3 / TF4 / TF5 / TF6** (default: all six on | options: on / off) - Per-timeframe include checkboxes for the consolidated alert. Uncheck a TF to drop its signals from the consolidated message body so a busy lower timeframe does not flood your phone. Important interaction: these checkboxes gate the consolidated `alert()` text (and, because they share its detection loop, the "Signal In-Force (Any)" and "New Potential (Any)" alertconditions). The per-TF alertconditions (for example "Signal In-Force (TF1)") and the Bullish/Bearish ones read their own pre-loop state snapshots and are completely independent of these boxes, so a per-TF alert still fires even if that TF is unchecked here (`FIX U2` keeps their state updated regardless).
- **Include FTFC** (default: off | options: on / off) - When a consolidated alert fires, appends the live full-timeframe-continuity verdict to the end of the message as one of `FTFC Up`, `FTFC Down`, or `Conflict`. Turn it on when you want directional context attached to every push.
- **Trigger** (default: off | options: on / off) - Appends the entry trigger price as `@ <price>` (bull = breakout high, bear = breakout low). Omitted automatically if no trigger price exists for that signal.
- **MAG** (default: off | options: on / off) - Appends the magnitude (first take-profit) level as `MAG <price>`. Omitted when the signal has no magnitude.
- **EXH** (default: off | options: on / off) - Appends the exhaustion (extended target) level as `EXH <price>`. Omitted when no exhaustion level exists.
- **STOP** (default: off | options: on / off) - Appends the stop level as `STOP <price>`. Omitted when no stop exists for the signal. Note the stop value is only computed while the Stops panel's Enable is on, so with stops disabled this field is always omitted.

The four enrichment toggles (Trigger / MAG / EXH / STOP) join with single spaces and only attach when their value is actually present, so you never get a dangling field or a blank `MAG` (`FIX P1-j`).

### How alerts work

There are two delivery paths, and you can use either or both.

**1. The consolidated `alert()` (automatic).** On every bar the indicator checks each enabled timeframe for a NEW in-force event, meaning a signal (or a Failing 2) that was not present last bar and is now. For each new event on an included TF it builds one label and joins multiple labels with ` | `. If anything fired and Include FTFC is on, the FTFC verdict is appended. The whole thing is sent once per bar via `alert.freq_once_per_bar`. To use it: create a TradingView alert on the indicator and choose the "Any alert() function call" condition. This single alert respects the TF include checkboxes and the enrichment toggles above. (The Domino setup is added to this consolidated stream only when "Advanced - Domino Setups > Include in Consolidated Alerts" is on; otherwise use the dedicated Domino alertcondition below.)

**2. The named `alertcondition()` list (you choose).** These appear in TradingView's alert "Condition" dropdown so you can create targeted alerts. The per-TF and Bullish/Bearish conditions are driven by their own state snapshots and are NOT affected by the TF include checkboxes; the two "(Any)" conditions share the consolidated stream's detection loop, so they DO respect the TF include checkboxes. The full list:

- **Signal In-Force (Any)** - any monitored TF prints a new in-force signal. Respects the TF include checkboxes above.
- **Signal In-Force (TF1)** through **Signal In-Force (TF6)** - a new in-force signal on that specific timeframe slot (TF1 = first timeframe in your settings, and so on). Active for a slot only when that timeframe is enabled.
- **Signal In-Force (Bullish)** - a new bullish in-force signal on any TF.
- **Signal In-Force (Bearish)** - a new bearish in-force signal on any TF.
- **New Potential (Any)** - a new potential setup appears on any TF (the inside/forming state, before it triggers). Respects the TF include checkboxes above.
- **Domino Setup** - the required number of consecutive inside-bar timeframes line up (a Domino). Fires regardless of the consolidated-alert toggle.
- **FTFC Shifted** - full-timeframe continuity changed state on this bar.
- **FTFC Up** - continuity flipped to fully bullish.
- **FTFC Down** - continuity flipped to fully bearish.
- **FTFC Conflict** - continuity moved into a conflicted (mixed) state.

Neither path ever alerts from a previewed or straddled slot: their forming candle is synthetic or estimate-grade, so both the consolidated alert and the per-TF alertconditions skip them, and the slot re-arms cleanly when real data resumes (`FIX HTF-STRADDLE-1`). Alerts also obey the same signal toggles as the display, so a signal type you have turned off cannot alert (`FIX P0-3`).

**Message format.** Each signal label in the consolidated message follows:

`<TF> <combo><pattern><H/L><marker> | <enrichment fields>`

- `<TF>` is the timeframe label (for example `1H`).
- `<combo>` is the Strat combo when Label Style is TheStrat (for example `2d-1-2u`, `3-2u`, or a live Failing 2 in canonical notation like `2u-F2d`), or plain language plus a direction arrow when Label Style is Universal (for example `↑REV`, `↓CONT`, `↑EXP`, `↓FAILING`).
- `<pattern>` is ` HAM` or ` SHO` when C1 is a hammer/shooter (a `◆` diamond in Universal mode), omitted otherwise.
- `<H/L>` appears only when "Show H/L in Labels" is enabled.
- `<marker>` is a green or red circle indicating bull or bear.
- The enrichment fields (`@ trigger`, `MAG`, `EXH`, `STOP`, and the FTFC verdict) follow after a ` | ` separator, joined by single spaces, each present only when its toggle is on and its value exists.

A push with every enrichment on looks like: `1H 2d-1-2u HAM 🟢 | @ 542.10 MAG 545.30 EXH 548.20 STOP 538.10 | FTFC Up`, and several simultaneous events are chained with ` | ` between each TF's label.

## Advanced - Exhaustion Behavior

These two settings change how a timeframe whose move has already hit its target is treated downstream. Both fall back to magnitude when no exhaustion level exists (a TF that crossed magnitude with no exhaustion counts as "exhausted" for these purposes). Read these carefully: each affects a specific, limited part of the indicator, not the chart at large.

- **Exhaustion Disables 'In Force'** (default: off | options: on / off) - Controls whether a signal that has reached its target still counts as "in force" for the data table and the Lead Signal filter. When off (default), a signal stays in force even after hitting exhaustion (or magnitude when there is no exhaustion): the data table keeps showing it as active, and the Lead Signal filter keeps using it for directional bias. When on, once the signal hits exhaustion (or its magnitude fallback) it is no longer treated as in force in those two places. The practical payoff is on the Lead filter: an exhausted higher-timeframe signal stops suppressing opposite setups below it, so (for example) bearish 15m/1H setups can appear again after a Daily bullish signal has already run to exhaustion. Note the precise scope: this affects the data table's in-force display and the Lead Signal filter only. It does NOT change protective stop behavior, which deliberately uses the raw in-force state so a locked stop is not dropped just because a target was reached. It also does not, by itself, add or remove signal lines on the chart.

- **Exhaustion Excludes from FTFC** (default: off | options: on / off) - Controls whether an exhausted timeframe still counts in the FTFC reading shown in the data table. When off (default), every timeframe counts toward FTFC regardless of whether its target was hit. When on, a timeframe that has hit exhaustion (or its magnitude fallback) is dropped from the FTFC calculation, so a structurally finished move no longer blocks FTFC from flipping. Example: if 5 of 6 timeframes are bearish but the 6th is bullish and has already hit exhaustion, turning this on excludes that spent bullish TF, so the cell reads "FTFC Down" instead of "Conflict". Important scope note: this affects the data table's FTFC cell only. It does NOT gate which signals draw, and it does NOT change alert content; signal drawing and alerts use the raw FTFC value, so this setting will not cause new signals to appear or disappear. Treat it as a display/read aid for the table, not a signal filter.

## Advanced - Outside Bars

Outside bars (3-bars) engulf the prior candle's full range and represent a burst of expansion. Because they behave differently from clean 2-bar setups, their targets have their own toggles here. Both require the "Outside Bars (3 Exp)" signal to be enabled before they have any effect, and both sit *under* the global toggles in Targets - Magnitude & Exhaustion: outside-bar magnitude also requires Show Magnitude Levels, and outside-bar exhaustion also requires Show Exhaustion Levels (`FIX CSS-2` made the exhaustion side match the magnitude side).

- **Show Magnitude for Outside Bars** (default: on | options: on / off) - Draws the C2 high/low as a magnitude target on outside-bar (3-bar) expansions. Leave it on to get a first target on these volatile setups. Turn it off if you find 3-bar magnitude targets unreliable and prefer to manage outside bars manually.

- **Show Exhaustion for Outside Bars** (default: on | options: on / off) - Draws exhaustion (the prior swing beyond C2) as an extended target on outside-bar expansions. Keep it on to see the full runway on a 3-bar move; turn it off to suppress extended targets specifically for outside bars. Like all exhaustion, it only appears when a valid exhaustion level exists.

## Advanced - Failing 2 Detection

This panel defines what the indicator treats as a Failing 2 (F2u / F2d). It is the engine behind the Failing 2s (Range Reclaims) signal and the F2 filter on 2-2 Reversals, so settings here ripple into both of those panels. Note the two-layer design: detection (here, on by default) classifies F2s everywhere - table, labels, line styling - while the signal toggle (Signals - Reclaims, off by default) decides whether they trade: levels, windows, alerts. Detection on + signal off means you *see* F2s but are not prompted to act on them.

- **Enable Failing 2 Detection** (default: on | options: on / off) - When on, the indicator detects Failing 2s (F2u / F2d) where price reclaims the prior range or crosses back against the breakout direction. When off, only standard 2u / 2d signals are shown and no bar is classified as a Failed 2. Leave it on (the default) if you want Range Reclaims or the 2-2 Reversal F2 filter to work, since both depend on this detection. Turning it off makes the Failing 2s (Range Reclaims) signal produce nothing and makes the 2-2 Reversal F2 filter match nothing. This switch also keeps the data table and labels consistent with the signal engine, so with detection off the table no longer shows F2u / F2d.
- **Detection Method** (default: Reclaim | options: Reclaim / Open / Reclaim + Open / Reclaim OR Open) - Chooses the rule that decides when a 2u / 2d has failed:
  - Reclaim: the bar closes back inside C1's range after breaking out. The most common definition; price gave back the breakout and returned into the prior range.
  - Open: the bar closes against its breakout direction (a 2u that closes below its open, or a 2d that closes above its open), regardless of whether it re-entered the prior range. Catches failures by close direction even when price held part of the breakout.
  - Reclaim + Open: requires both conditions at once (closed back inside C1's range and closed against the breakout direction). The strictest setting, producing the fewest but highest-conviction Failing 2s.
  - Reclaim OR Open: requires either condition. The loosest setting, flagging the most Failing 2s, useful if you want to see every potential reclaim.
- **Show Failing 2 Open Level** (default: off | options: on / off) - When on, draws the CC open price as a dotted reference line whenever a Failing 2 is detected. The open acts as the trigger level for the reclaim, so this line shows where price would need to reclaim through. Turn it on if you trade off the open as your trigger; leave it off to keep the chart clean. It only draws when a Failing 2 is actually detected, so it has no effect while Enable Failing 2 Detection is off.

## Advanced - Data Table

- **Color TF When In-Force** (default: on | options: on / off) - Highlights a timeframe's TF column using the current-bar (CC) color whenever a signal you have enabled is in-force on that timeframe, making active timeframes pop out of the table. "In force" here means a live 2/F2 signal, or an enabled outside-bar (3) expansion - which is in force by definition, having broken both sides of C1. A plain inside bar never lights the TF column, because an inside bar is by definition *not* in force; it stays colored in the CC cell only (`FIX TFCOLOR-1` - earlier builds highlighted the TF column on every inside bar, which lit up most of the table). This applies in Full mode only; it has no effect in Compact mode (where in-force coloring is already built into the single colored label).

## Advanced - Take Action Windows

These extend the shaded trigger-to-target zone to two signal types that are handled specially. Each still respects the main "Only When In-Force" gate from the Take Action Windows panel and requires its parent signal to be enabled.

- **Include Failing 2s** (default: on | options: on / off) - Shades the C1 range as a take action zone for Failing 2 (Range Reclaim) setups, where price broke out and then reclaimed back inside. The reclaimed range is the working zone for the reversal. Turn it off to keep windows on standard 2-bar setups only. Has no effect unless the Failing 2s (Range Reclaims) signal is enabled, and it still obeys the Take Action Windows "Only When In-Force" setting.

- **Include Outside Bars** (default: on | options: on / off) - Shades the zone between trigger and magnitude for outside-bar (3-bar) expansions. Turn it off if you want windows on cleaner setups but not on volatile 3-bars. Has no effect unless the Outside Bars (3 Exp) signal is enabled; it also requires a valid, not-yet-crossed magnitude level, and it still obeys the Take Action Windows "Only When In-Force" setting.

## Advanced - Domino Setups

- **Include in Consolidated Alerts** (default: off | options: on / off) - when on, Domino events are appended to the indicator's main consolidated `alert()` stream, so a single TradingView alert built on the main alert function will also notify you of Dominoes. When off (the default), the main alert stays free of Domino messages. Either way, you can always create a dedicated Domino notification using the separate "Domino Setup" alertcondition when setting up the alert in TradingView, which fires independently of this toggle. Note that what counts as a Domino (and therefore what triggers either alert path) is still governed by Minimum Timeframes in the Filters - Domino Setups panel.

## Debug

This panel is a troubleshooting aid for inspecting the indicator's signal logic on a single timeframe. Most traders can leave it off; use it only when verifying behavior or reporting an issue.

- **Show Debug Panel** (default: off | options: on / off) - Shows a detailed debug panel with internal signal-logic information for the selected timeframe, so you can see why a signal did or did not fire.

- **Timeframe to Debug** (default: TF1 | options: TF1 / TF2 / TF3 / TF4 / TF5 / TF6) - Selects which timeframe slot the debug panel reports on, where TF1 is the first timeframe in your settings and so on through TF6. Greyed out when Show Debug Panel is off (`FIX MOD-3`).

## Troubleshooting & FAQ

- **I added the Suite and see almost nothing.** Three checks, in order: (1) Preset - Custom leaves all six timeframe rows disabled until you enable them; start from a built-in preset. (2) Chart timeframe - slots faster than your chart show as dimmed, empty rows and draw nothing; move your chart to Timeframe 1 or lower. (3) Signal toggles - 2-2 Continuations, Failing 2s, and both expansion signals are off by default; out of the box only reversals and inside continuations draw.

- **I updated the indicator and my alerts stopped working or send old messages.** TradingView alerts bind to the version of the script that existed when the alert was created; they do not automatically pick up a new release. After updating the Suite, delete your existing alerts and recreate them so they bind to the new version.

- **The chart visuals look stale, frozen, or wrong after an update or settings change.** This is a TradingView rendering quirk, not a data error. Remove the indicator from the chart and re-add it to force a clean redraw.

- **Bar Replay is not drawing anything (or shows preview levels instead).** Preview Mode must be Off for Bar Replay. The "Auto" setting is designed to disable preview during bar replay, but if you still see issues, set Display - Preview Mode > Preview Mode to Off explicitly while using Bar Replay.

- **A data-table row shows `?` in the CC cell.** That slot is previewed or straddle-shifted and its forming candle could not be rebuilt from real daily data, so the Suite marks it unknown rather than guessing (`FIX HTF-STRADDLE-1`). It resolves on its own when the period opens or the reconstruction data arrives. A slot in this state also never fires alerts.

- **Preview engaged during a live session around a holiday.** Fixed in v2.2.3 (`FIX GLUE-1`): the holiday fallback now measures staleness from the bar's scheduled close, so exchange-glued holiday half-sessions (which give a live bar a days-old open stamp) can no longer flip Auto preview on mid-session. If you are on v2.2.3 or later and still see a mid-session PREVIEW MODE banner, please report it with the symbol, chart timeframe, and time.

- **My labels are overlapping and hard to read.** Label collision is a known TradingView limitation; the platform does not auto-space labels. Reduce clutter by lowering the number of enabled timeframes, turning off levels you are not using, or adjusting the label offset settings. There is no way for the indicator to fully prevent overlap.

- **The indicator shows nothing on past/historical bars.** This is by design. The Suite draws only the current live state (the levels, targets, and signals in force right now), not a history of every signal that ever printed. Scroll to the right edge / current bar to see active drawings.
