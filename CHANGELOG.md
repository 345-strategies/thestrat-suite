# Changelog

All notable changes to TheStrat Suite. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the project predates this file, so earlier entries are reconstructed from the version snapshots in `pine/`, the inline `// FIX <id>` comments, and `docs/v2.2.3_holiday_glue_fix.md`.

How to read this file:

- **One entry per released version, newest first.** "Released" means a `.pine` snapshot is preserved in `pine/`. Three versions — 2.2.1, 2.2.3, and 2.2.5 — existed only as working states in the TradingView Pine editor and left no snapshot; each is folded into the entry of the release that shipped (or superseded) its changes, and marked as such.
- **Every fix cites its `FIX` tag.** Grep the current source for the tag to find the exact code and the full inline rationale. Code references are function names and tags, never line numbers.
- **Dates** come from dated `FIX` comments and file names. Where a snapshot carries no date of its own (2.2.2), the newest dated comment introduced in it is used.

## [Unreleased]

### Fixed

- **Heikin Ashi charts are refused.** TheStrat reads traded OHLC, and Heikin Ashi candles are averages, so every bar type, trigger and target on such a chart was wrong, and the higher-timeframe slots silently received Heikin Ashi prices too because they request the chart's own ticker. The Suite now stops with a clear error on a Heikin Ashi chart ("Heikin Ashi charts are not supported: TheStrat needs real candles. Switch the chart to Candles or Bars.") instead of drawing levels from averaged prices. (`NO-HA-1`)

### Added

- **Bar Numbers.** Each chart candle can now show its Strat number beside it: 1, 2u, 2d, 3u, 3d, F2u, F2d. It is off by default; turn it on in the new *Display - Bar Numbers* group. The numbers come from the same classifier as Strat Candles and the data table's CC cell, so the three can never disagree, and they follow the Failing 2 Detection Method. **Notation** offers Full (the table's notation), Classic (1, 2u, 2d, 3) or Numbers (1, 2, 3, with direction carried by color). **Emphasis** defaults to Tiered: 1s, 3s and Failing 2s show in full color and plain 2s are faded, so setups stand out instead of a wall of numbers. Uniform shows everything at full strength, and Key Bars Only hides plain 2s. **Position** is Below, Above, or Broken Side (the side the candle took out). **Color** uses the Style palette or a single color, and **Bars Shown** limits the numbers to the last N candles. The forming candle's number is drawn lighter until it closes, because it can still change. Numbers are drawn with `plotshape`, so they cover the whole chart without using any of the label budget the levels rely on. The tradeoff is that TradingView fixes the text size. Chart timeframe only; the data table already covers the other timeframes. When both features are on, they share one classification per bar with Strat Candles. (`BARNUM-1`)

## [3.1.1] — 2026-09-13

Requires TheStratGrammar (TV version 1). Source: `pine/TheStratSuite_v3.1.1.pine`. Seven fixes to the preview path, found after a Friday pre-market report on SPY and a follow-up scan; details in each entry.

### Fixed

- **Weekly and monthly exhaustion targets are rebased when the period rolls over.** On a weekend or a holiday the projection promoted the finished period into C1 but kept the exhaustion snapshot that was computed before that period traded. On AMD's weekly chart the completed week of Sep 7 to 11 ran straight through the pivot from the week of Aug 17, yet that broken pivot was still drawn as the weekly exhaustion target, sitting below the weekly trigger high and painted in the already-crossed color. The scan now produces two results per timeframe: the current period's, unchanged, and one rebased onto the period that just finished. A projection takes the rebased one, and only where the period it promoted has genuinely closed, so nothing that could still move ever feeds a target. (`EXH-PREVIEW-CHANNEL-1`)

- **A projected period no longer reports its targets as already hit.** The placeholder candle a preview builds copies the promoted period's high and low exactly, so every magnitude and exhaustion level inside that range was flagged as crossed before the projected period had traded a tick. That drove more than the color. It also fed the exhaustion in-force override, break-even stops, Take Action Window removal, and the magnitude-hit gate that unlocks exhaustion targets. A placeholder candle now hits nothing. Reconstructed slots, whose candle is rebuilt from real daily bars, still hit normally. (`PREVIEW-CROSSED-1`)

- **The weekly slot no longer shows a yellow `?` during Friday pre-market.** With Preview Mode on Auto, opening a chart on Friday before the bell (SPY 1W, Sep 4 2026 07:51 ET) painted W yellow in the header, demoted the forming week into C1, and reported the week as unknown, even though Monday through Thursday had traded and Friday's session was still to come. `shouldApplyPreview` treated every Friday as part of the weekend by calendar (`differentWeek or Fri/Sat/Sun`), so the moment Auto preview armed for the closed pre-market it projected next week. The weekly test now asks the same question `shouldStraddle` does: has the served week's *scheduled close* (`time_close(tf)`) passed? Friday after the close, the weekend, and Monday before the open all sit past it, so the weekend projection is unchanged; Friday pre-market is not, so the week renders as the in-progress candle it is. Session-aware for free (16:00 ET equities, 17:00 ET futures) and immune to holiday-glued bars whose close is still ahead. Non-weekly slots are untouched. (`PREVIEW-WEEKCLOSE-1`)

- **Monthly, quarterly, semiannual and yearly slots preview by scheduled close too.** Those branches still compared the calendar month and year of the clock against the chart bar's open stamp, which reads a session-stamped futures period (July's bar opens June 30 at 18:00 ET) as the prior month. During the CME maintenance hour on the first trade date of a month, Auto preview demoted the current month into C1 and showed M as `?`. All calendar slots now use `shouldStraddle`. Month-end evenings preview from the close rather than from calendar midnight. (`PREVIEW-CALCLOSE-1`)

- **A preview or straddled slot can no longer become the Lead.** The Lead Signal filter's anchor scan read every enabled slot's result, so a reconstructed monthly slot in force could report `Lead: M` and suppress a real lower slot's lines and alerts, while its own alert was correctly silenced as estimate-grade. Preview slots are skipped when choosing the anchor. (`LEAD-PREVIEW-1`)

- **Preview slots no longer count toward a Domino or fire the Domino alert.** The synthetic placeholder candle always classifies as an inside bar, so every weekend the shifted W and M slots joined the Domino chain, showed a Domino row, and fired the `Domino Setup` alertcondition, the one alert path the preview guards never covered. A preview slot now breaks the chain. (`DOMINO-PREVIEW-1`)

- **W and M open lines stay visible pre-market on slots that were not shifted.** The open line was hidden whenever Auto preview was armed anywhere, not only on the slot whose open is a proxy. The gate is now per slot. (`OPENLINE-PREVIEW-1`)

- **Auto preview never arms while the chart's current bar is still trading, and arms on holidays and early closes.** Auto used hard-coded US Eastern hours for every stock, ETF and index symbol, so a London-listed stock had its live daily bar demoted into C1 with a `?` from 03:00 to 09:30 ET every day, and an extended-hours chart the same from 16:00 to 20:00. The reverse gap: the holiday fallback waited three chart periods past the last scheduled close, 72 hours on a daily chart, so an exchange holiday, a 13:00 early close or a foreign session's afternoon never previewed before the next open. Two changes, one rule: Auto arms only when the last bar's scheduled close (`time_close`) has passed, and the holiday fallback is a flat 4 hours past that close on every chart. Visible side effect: on a weekly chart the PREVIEW MODE banner no longer shows mid-week pre-market, because nothing on that chart can preview until the week closes. Accepted trade-off: a thinly traded symbol whose first print lags the open can show a phantom preview until that print, as intraday charts already did. Auto now requires a scheduled close, so it never arms on Renko, Kagi, Line Break, Point & Figure or Range charts, whose forming bar has none; those chart types were never supported. The Preview Mode tooltip describes the new rule. (`AUTO-SESSION-1`)

- **Level lines and Take Action Windows reach the end of long bars.** Lines ended at the current bar's open plus one nominal period, so on a holiday-glued futures bar (ES after Labor Day: one 47-hour daily bar from Sunday 18:00 to Tuesday 17:00 ET) every daily line and box stopped at Monday's reopen and never reached the live candles, reading as levels "a day behind." Lines now run to the served bar's scheduled close when that is later; normal days are unchanged. Also fixes 31-day months and glued weekly bars. (`LINEEND-CLOSE-1`)

## [3.1.0] — 2026-08-25

Requires TheStratGrammar (TV version 1) to be published before this compiles.

### Fixed

- **Loading is faster again.** Moving classification into the library also moved `calculateFTFC` into it, and that was a mistake: `calculateFTFC` is called unguarded at global scope, once per chart bar across all of history, and a second time per bar when FTFC bar coloring is on. The library form cost an array clear, up to six pushes, a call across the module boundary, a loop, a **string** result and two string comparisons — every bar — where the original cost six boolean branches. Continuity is a six-way boolean AND over the sign channel, so I put the accumulator back. `sg.continuity` stays the definition of record; the two cannot meaningfully drift. Bar classification still comes from the library, which is the part where a second implementation actually could drift, and the reason the library exists. (`PERF-FTFC-1`)

- **A second per-bar cost, on the bar-coloring path.** `detectBarTypeAndFailed` was calling `sg.classify`, which builds a `BarState` object. That function runs once per chart bar over all history with Strat Candles on, and once per bar per timeframe with stops on — an object allocated per bar where the old code allocated none. It now calls `sg.structureOf` and `sg.isFailed`, both pure, with `notation()`'s projection written out at the call site. Same classification, same output, no object. (`PERF-ALLOC-3`)

- **2-2 Continuations no longer go missing when the bar before C1 was an outside bar.** The pattern is C1 and CC — two directional bars in the same direction — so nothing further back should gate it. If I want a three-bar combination I'll say so by specifying C2, the way 3-2 Expansions do. The old rule dropped every 2-2 continuation in a `3 → 2u → 2u` sequence on the grounds that the trend was one bar old, which silently cost real setups: no level, no label, no alert, and a dark cell in the data table with nothing anywhere to say why. Removed from both the in-force test and the two draw branches, and the now-unused parameter is gone from `shouldDrawC1Level` and its two wrappers. Magnitude behavior is unchanged — `shouldDrawMagnitude` already suppresses on `c1_broke_c2`, which is true for every 2-2 continuation regardless of what preceded it. `c2_was_3` stays in the debug panel as diagnostic information. (`CONT22-PRIOR-1`)

### Changed

- **Classification now comes from TheStratGrammar** (`import SpinTrades/TheStratGrammar/1`) — the same definitions published as `grammar/SPEC.md` and the Python reference implementation. `detectBarTypeAndFailed`, `calcBarType`, `calcBarNum`, `detectPatterns` and `calculateFTFC` are thin wrappers with unchanged signatures; no call site and no behavior changed (60,000-bar differential against the old inline logic across all four Failing 2 methods: zero mismatches, plus 180,000 pattern checks). The `CandleMetrics` type and six pattern detectors moved into the library. One quirk preserved deliberately: `calculateFTFC` with zero qualifying timeframes still returns both flags true, exactly as the old accumulator did. Classification itself is not on the hot path, but `calculateFTFC` is, and routing it through the library cost real load time — see `PERF-FTFC-1` below. (`GRAMMAR-LIB-1`)

- The **debug panel now shows why a signal is or isn't in force**, not just what the bars are. It reads as two panes: the left one keeps the bar classification (C1, CC, patterns, Failing 2s) and adds a **C2 Bar Type** section — `c2_was_2u` / `c2_was_2d` / `c2_was_3` / `c2_closed_up` / `c2_closed_down`. C2 was invisible before, which mattered because a 2-2 continuation is silently blocked whenever C2 is an outside bar, with no toggle and nothing on screen to say so. The right pane is new: `sif_high` / `sif_low` as the seven-term in-force disjunction (the draw decisions, which is where the exhaustion gate and trigger tests come in, are shown below them), then one row per in-force term (Inside Rev, Inside Cont, Ham/Sho, 2-2 Rev, 2-2 Cont, 3-2 Exp, Failing 2) reported high-side / low-side, then the gates (`hammer_shows`, `shooter_shows`, FTFC), the draw decisions, and magnitude. Twelve fields the panel already collected but never displayed are now on screen.

  The seven terms behind `signal_in_force_high` / `signal_in_force_low` are named variables rather than one inlined expression, and the panel reports those same booleans — so it cannot drift out of sync with the signal the way a re-derived copy would. `DebugInfo` fields carry defaults, replacing a 32-argument positional constructor, and three row helpers replace the table cell pairs the panel used to spell out one line at a time. No signal, level, label or alert behavior changes. (`DEBUG-TERMS-1`)

## [3.0.1] — 2026-08-03

### Fixed

- The **Lead Signal filter no longer changes the data table.** It is a chart filter: when it suppresses a counter-trend setup, the lines, labels, targets, stops and Take Action Windows come off the chart, but the table keeps reporting what that timeframe is actually doing. `FIX P1-g` had cleared the in-force state outright so the table would agree with the hidden lines, which inverted the table's purpose — it is the unfiltered reference you check *because* the chart is filtered. Drawing behavior is unchanged: every line, label, target, stop and TAW consumer is gated on the draw flags, never on `signalInForce`. (`LEAD-TABLE-1`, supersedes `P1-g`)

### Changed

- **The data table's Lead row only renders when a Lead actually exists.** "No Lead" is the resting state for most of a session, so the row was almost always on screen saying nothing — and the Lead Signal filter being on is already visible in the settings, so an empty row was pure noise. It now appears only when a timeframe is genuinely leading, in both Full and Compact modes. Both tables clear their full row range each bar, so a skipped row leaves no stale cell and the rows below shift up. (`LEADROW-1`)
- **Universal labels are abbreviated**: `REV`, `CONT`, `INS`, `OUT` replace REVERSAL, CONTINUATION, INSIDE, OUTSIDE (EXP and the FAILING overlay are unchanged). Consolidated labels stack several timeframes on one line, so `W ↑REVERSAL◆ + 1D ↑CONTINUATION◆ + 1H ↑CONTINUATION + 30m ↑CONTINUATION + 15m ↑EXP` ran to 82 characters; it now reads `W ↑REV◆ + 1D ↑CONT◆ + 1H ↑CONT + 30m ↑CONT + 15m ↑EXP`, 35% shorter. Alert messages use the same abbreviations, so chart and alerts still match.
- Child settings are now visually indented under their parent toggle: "Only When In-Force" under Show Magnitude Levels, under Show Exhaustion Levels, under Show Take Action Windows and under Extend to Exhaustion, plus "Only After Magnitude Hit". Same tree-glyph convention TheStrat Suite Lite uses. Labels only; no behavior change.

- Data table **Mode** options renamed from "Full (TheStrat)" / "Compact (Universal)" to plain **Full** / **Compact**. The mode never depended on the Label Style setting, so the parenthetical was misleading. **Note for existing users:** TradingView stores the selected option as a string, so anyone who had "Compact (Universal)" saved will fall back to Full after updating and needs to reselect Compact once.

### Added

- Data table: **Compact Cell Color** — in Compact mode you can now choose what the colored timeframe cells mean. **Bar State** (the default) colors every cell by that timeframe's current candle, mirroring the Full table's CC column. **Signals In Force** colors a cell only while a signal is live there, mirroring the Full table's TF column, so the row stays dark until something is actionable. Both reuse the Full table's own classifier (`detectBarTypeAndFailed`) and palette rather than a separate scheme, so Compact and Full can never disagree. Settings: *Display - Data Table*. (`TABLE-COMPACT-COLOR-1`)

## [3.0.0] — 2026-08-02

Published on TradingView as the open-source listing ("TheStrat Suite [Open Source] — Entries, Targets, and Stop Loss"), superseding the invite-only publication. Source: `pine/TheStratSuite_v3.0.0.pine`.

### Added

- Stops: **Smallest Timeframe Only** — a display filter under *Stops - Stop Levels* that draws only the stop belonging to the smallest enabled timeframe that currently has one, hiding stops on larger timeframes. Display-only: stop prices, sticky in-force latches, break-even mechanics, and alert message content are all unchanged; the pass runs after the Lead Signal filter (so a lead-suppressed stop cannot claim the smallest slot) and before the render phase (so hidden lines are deleted, not skipped). "Smallest" follows slot order, matching the Lead Signal filter's convention. Off by default. (`STOP-SMALLEST-1`)
- Bar coloring — chart candles painted by their Strat classification (inside 1, directional 2u/2d, outside 3u/3d, Failing 2s), toggleable per family, or by Full Timeframe Continuity (up / down / conflict, with optional Failing-2 flip highlighting). The one price-action feature from the pre-Suite standalone scripts that hadn't been folded in. New settings group **Display - Bar Coloring**; off by default. Strat mode reuses the data table's CC classifier (`detectBarTypeAndFailed`) on the chart bar, so candles and table can never disagree and the Failing 2 method/enable settings govern candles automatically; family colors follow the existing Style inputs. FTFC mode deliberately re-grades each historical bar by its own close against the served period opens (`calculateFTFC` re-run with chart close) instead of reading `ftfc_up`/`ftfc_down`, whose lookahead closes would paint history with each period's *final* direction; on the live bar the two are identical, so candles always match the table's FTFC row. The flip highlight rebuilds each slot's intra-period range from chart bars (running H/L keyed on the served period-open time; see `paintSlotFailed2`) so historical flips are graded honestly too. (`BARCOLOR-1`)

## [2.2.7-split] — 2026-07-21

Current build: `pine/TheStratSuite_v2.2.7-split.pine`.

### Added

- The source file now carries the standard MPL-2.0 license header, matching the repository `LICENSE`.

### Changed

- Failing 2 notation is canonical uppercase everywhere: `F2u`/`F2d` in the data table, labels, and alerts. The lowercase live-`f` (`f2u`/`f2d` marking the forming candle) is retired — notation is now tense-free, with liveness carried by slot position (the last token is the forming candle), the `*` potential prefix, and words (`FAILING`/`Failed`). Display-string change only; no detection or signal logic touched. (`NOTATION-1`)

## [2.2.6-split] — 2026-07-07

Ships the complete delta since 2.2.4 — exactly the two fixes below. **2.2.5-split** was an editor-only intermediate step inside this pair; no snapshot was preserved, and nothing beyond these two fixes existed in it.

### Fixed

- Daily reconstruction no longer paints the wrong forming candle around period rolls. The T3 reconstruction keys daily sub-bars by calendar period with a +12h normalization (session-stamped opens land in their true calendar day), but "now" was keyed raw — valid only under the old double-reading straddle test that `GLUE-2b` removed. For roughly the seven hours between a period's session close and calendar midnight, raw "now" keyed one period behind the daily-bar keys, so reconstruction matched the *prior* period's dailies at each roll. "Now" is normalized +12h too; see the `pkeyCal` call site in the T3 recon block. (`RECON-KEY-1`)
- "Color TF When In-Force" no longer highlights the timeframe column on every inside bar. An inside bar is by definition *not* in force, but the old test lit the TF column for it — the common case, so effectively "coloring everything." In force now means a live 2/F2 signal or an enabled outside-bar (3) expansion, which is in force by definition. Inside bars stay colored in the CC cell only. (`TFCOLOR-1`)

## [2.2.4-split] — 2026-07-06

The GLUE milestone: CME holiday-glued bars can no longer trip the preview or straddle machinery during a live session. Supersedes **2.2.3-split**, released earlier the same day and never preserved as a file — the full incident narrative, including 2.2.3's edits, is in `docs/v2.2.3_holiday_glue_fix.md`.

The incident: CME glues holiday half-sessions into the next trade date (the Friday July 3 half-session lives inside Monday July 6's bar), so a live Monday bar can open with a Thursday-evening stamp. Two pieces of logic treated "old bar stamp" as "market closed / data stale," and both misfired mid-session — a phantom PREVIEW MODE banner, `?` in the D/W table cells, and weekly trigger levels hugging the current day's range instead of last week's.

### Fixed

- Auto preview no longer engages during a live session on a holiday-glued bar. `isHolidayClosed` measures staleness from the bar's *scheduled close* (`time_close`), not its open stamp — a live bar's scheduled close is always in the future, so the fallback cannot fire while a session trades, while genuinely halted markets still trip it. Shipped in 2.2.3, carried forward here. (`GLUE-1`)
- Phantom straddle shifts on glued bars are gone. `shouldStraddle` now asks a single question: is now past the served HTF bar's scheduled close (`time_close(tf)`)? No week numbering, no timezone parsing, no +12h assumptions — and direction-aware, so a trade-date bar running ahead of the calendar cannot fire it. The July 1 mid-week quarter-roll case the straddle exists for still works. This replaces 2.2.3's calendar-key comparison (`GLUE-2`): that version fixed the daily symptom, but live testing the same day showed the glue reaches the *weekly* bar too (opening ~84h before its true period), defeating the +12h normalization's one-session-early assumption — so `GLUE-2` was superseded within hours and never shipped in a preserved file. (`GLUE-2b`)

## [2.2.2-split] — 2026-07-02

The audit release. The 2.2.0 baseline went through an external correctness review (the pre-registered review brief is `docs/DESIGN_CONSTRAINTS.md`); the accepted findings landed here as the `P0`/`P1`/`P2`, `BTC`, `CSS`, `TAW`, `MOD`, and `U2` fix families, alongside new HTF-straddle handling. **2.2.1-split** was an editor-only intermediate on this path — no snapshot preserved; the settings reference (now `docs/TheStratSuite_v2.2.7_Settings_Reference.md`) was first written against it.

### Added

- HTF-straddle handling: a chart bar can straddle a higher-timeframe boundary (a weekly chart bar spanning a mid-week month/quarter roll), leaving the served HTF data one period behind. T1 shifts the affected slot's period assignments live; T3 rebuilds the true forming candle from completed daily sub-bars via `request.security_lower_tf`. Session-aware, probe-validated (the probe scripts are preserved in `diagnostics/`), hardened 2026-07-02 against period-end-afternoon phantoms. Straddled slots draw no open line and never alert — their forming candle is estimate-grade. (`HTF-STRADDLE-1`)

### Fixed

Repaint and reload correctness (the case studies behind `docs/engineering/repaint-prevention.md`):

- New-period detection is per-slot explicit state instead of a `ta.change` call shared across all six timeframe slots — the shared buffer made "new period" fire on nearly every bar, constantly wiping magnitude-crossed flags and sticky stops. See `computeSignalState` and `lastPeriodStart`. (`P0-1`)
- Exhaustion levels no longer move after a reload. `findExhaustionLevels` seeded its scan from the forming bar, which evaluates differently live versus on reload; it now seeds from completed bars only. (`P0-2`)
- Sticky stops survive a reload. The lock latched only under `barstate.islast`, so bars that were realtime when the signal fired became historical on reload and the latch never re-ran; with stops enabled, detection and latching now run on every bar so history rebuilds the lock. (`P1-a`)
- Preview-mode state re-latches every tick while previewing and re-latches on preview exit, so preview-shifted values cannot leak into the real period's latched state. (`P1-d`)

Alerts:

- F2 (Range Reclaim) alerts obey the "Failing 2s (Range Reclaims)" signal toggle — a signal type you disabled can no longer alert while the table shows nothing in force. (`P0-3`)
- Alert edge-state resets on a real period change (keyed off the served period-open, `realPeriodTime`), so a signal already in force at a new period's open still produces an alertable edge. (`P1-c`)
- Per-timeframe `alertcondition`s fired on every bar when a timeframe was enabled but excluded from the consolidated alert message — an early `continue` skipped the was-state update. Was-state now updates unconditionally; only the consolidated `alert()` message is gated by the checkbox. (`U2`)
- Consolidated alert messages only append the ` | ` separator when detail fields actually exist. (`P1-j`)

Signal and display correctness:

- The magnitude/exhaustion Take Action Window could not render for in-force signals — the default configuration. The old guards required *both* triggers to exist, but the opposite trigger is suppressed exactly when a signal goes in force. Each direction's box now checks only its own trigger. (`TAW-1`)
- Inside reversals with a flat (doji) C2 now mark in force, matching the draw path: the line drew and the alert fired, but the table, the sticky-stop latch, and the "only when in force" target/TAW gates never saw it. (`CSS-1`)
- Outside-bar exhaustion targets respect the global Show Exhaustion Levels toggle, mirroring how outside-bar magnitude already respected Show Magnitude Levels. (`CSS-2`)
- Hammer/shooter reversals and continuations obey their own families' FTFC toggles — both wrongly read the inside-reversal's. (`BTC-1`)
- A candle body centered exactly at the range midpoint classifies as neither hammer nor shooter instead of both, which drew a spurious wrong-side line. See `isBroadHammer`/`isBroadShooter`. (`BTC-2`)
- With Failing 2 Detection off, the table/label path (`detectBarTypeAndFailed`) no longer shows F2 notation while the main engine (`detectFailed2`) emits plain `2u`/`2d`. (`P1-f`)
- The Lead Signal filter now clears in-force state on the timeframes it suppresses, so the data table and Lead row stop highlighting a counter-trend timeframe whose lines, labels, and alerts were already hidden. (`P1-g`)
- When both directions are in force at once (rare), the Lead Signal direction resolver no longer reads `F2u` as bullish. F2u is a bearish signal and F2d bullish, but the generic close-suffix test mapped the `u` to bullish — inverted, flipping the Lead anchor and all counter-trend suppression. F2 cases now resolve explicitly before the suffix test (`getSignalDirection`). (`LEAD-F2-TIEBREAK-2`)
- Line suppression nulls the owning line field after deleting a suppressed line (each `LineInfo` carries its owner slot and kind; see `nullSuppressedLine`), so the render phase recreates the line instead of mutating a deleted id — and a suppressed level reappears once the price collision clears. (`P1-h`)
- Debug panel boolean cells render `false` dimmed instead of white-on-white (dead ternary in `debugBoolText`). (`P2-3`)

### Changed

- Settings dialog rows grey out when not relevant: timeframe rows while a preset is active, custom color pickers outside Custom color mode, offsets under disabled label features, and the like. (`MOD-3`)
- Labels are pooled and updated in place (`LabelPoolT`/`acquireLabel`) instead of deleted and recreated every tick; surplus pooled labels are trimmed after each render. (`P1-i`)
- In-source documentation clarifies that the data table's FTFC cell tint is display-only, not signal gating. (`P1-e`)

### Removed

- Five transitively dead cleanup methods whose only callers were each other. (`P2-1`)
- Unreachable `"1u"`/`"1d"` comparisons in CC-type checks — `ccType` only ever emits `1`/`2u`/`2d`/`3u`/`3d`/F2 values. (`P2-5`)

## [2.2.0] — 2026-06-16

The pre-audit baseline, preserved twice: `pine/TheStratSuite_v180_June16_2026.pine` (the original June 16 export) and `pine/TheStratSuite_v2.2.0.pine` (the same content retitled `v2.2.0` — identical apart from the version string). Everything the Suite is dates from here or earlier: six configurable timeframes with presets, bar classification, the seven signal types with magnitude and exhaustion targets, Full Timeframe Continuity, hammer/shooter filters, Domino detection, sticky stop levels, preview mode, consolidated alerts, and the multi-timeframe data table. The v1.x line predates this repository and is not reconstructed here.

---

## Contributor checklist

Before merging anything that changes shipped behavior:

1. Is there an entry under `[Unreleased]` describing the *user-visible* behavior first, with the `FIX` tag (or feature tag) in parentheses after it?
2. Does the matching `// FIX <id>` comment exist in the source, dated, at the code it explains — and does the entry reference code by function name, never line number?
3. On release: new dated version heading here, new snapshot in `pine/`, version string bumped in both the header comment and the `indicator()` title.
4. If an editor-only intermediate version existed and left no snapshot, does the shipping release's entry say so explicitly?
