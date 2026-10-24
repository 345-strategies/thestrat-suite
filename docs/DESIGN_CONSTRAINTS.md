# TheStrat Suite v2.2.0 - Review Brief: Conscious Design Decisions

Source under review: `pine/TheStratSuite_v2.2.0.pine` (in this repo)
(Pine Script v6 TradingView indicator, ~2,800 lines. Correctness and performance both matter.)

These decisions are INTENTIONAL. Do NOT report them as bugs. You may report
implementation flaws WITHIN them, or cases where the implementation contradicts
the stated intent.

## Architecture (intentional)
1. NO-REPAINT CONTRACT: `request.security(..., lookahead = barmerge.lookahead_on)`
   combined with `[1]`-offset history for completed bars is the established
   non-repainting pattern for this codebase. Current-bar (offset 0) values ARE
   intentionally pulled and DO update intra-bar: live visuals must reflect
   current grading on every tick (NO barstate.isconfirmed stabilization). All
   drawing happens only on barstate.islast. Historical bars draw nothing.
   Only flag a repaint issue if a CONFIRMED-bar value (C1/C2/C3/C4) could change
   after its period closes, or if alert/level state could rewrite history.
2. Conditional creation over create-then-hide (do not suggest hiding objects).
3. Centralized decision logic: one shouldDraw decision per level, applied to both
   lines and labels. Flag any path where lines and labels can diverge.
4. Single request.security call per timeframe returning a tuple. FTFC uses an
   unrolled per-TF if/else chain because arrays cannot pass through
   request.security. Do not suggest array-based security returns.
5. State tracking runs every bar; rendering only on barstate.islast.
6. UDT-per-timeframe storage with a 6-slot processing loop.

## Product decisions (intentional)
7. F2 detection in the data table (detectBarTypeAndFailed) is deliberately a
   separate code path from the main processing detection (detectFailed2).
   You may flag LOGICAL DISAGREEMENT between the two paths (same inputs,
   different classification), but not the duplication itself.
8. Four Failed-2 methods (Open/Reclaim/Both/Either) and three hammer/shooter
   definitions are deliberate configurability.
9. Hit levels turn gray rather than disappear. Stop default reference is CC.
10. F2 labels preserve context ("FAILING" appended, asterisk = potential).
11. Alert tradeoffs accepted: freq_once_per_bar consolidation; re-trigger when a
    signal goes out-of-force and back in on a new bar; per-TF alertconditions
    read pre-loop snapshots (this fixed a real bug - do not undo).
12. Label overlap at *distinct* prices is a TradingView rendering limitation and
    stays sidelined: two levels a fraction of a point apart occupy the same pixel
    row and there is nothing Pine can do, because Pine cannot measure pixels. Do
    not propose collision systems for that case. Labels at the *same* price are a
    different matter and are solved - PineDraw's `LabelSet` consolidates them into
    one row, with a caller-chosen tolerance. (Updated 2026-08-18; the original
    blanket instruction predates PineDraw.)
13. Preview-mode auto incorrectly shows in bar replay - known TradingView
    limitation, tooltip workaround accepted. Do not re-report.
14. Emoji (green/red circles) in alert text and arrows/diamond in Universal labels are an
    advertised product feature. Note any platform-compat risk but do not demand
    removal.
15. Stop lines are intentionally excluded from line suppression (risk mgmt).
16. No Heiken-Ashi. TheStrat classifies traded opens, highs, lows and closes, and
    Heiken-Ashi candles are averages, so Strat output on a Heiken-Ashi chart is wrong
    by construction. Heiken-Ashi is unsupported in every tool here (root README,
    "No Heiken-Ashi"); do not propose support for it. (Added 2026-10-24.)

## What the owner wants from this review
- Latest Pine v6 / TradingView capabilities the script is NOT using but should
  (verify against current official docs - release notes, migration guide,
  profiling/optimization docs). Examples to check, not assume: dynamic requests,
  input.enum, lazy and/or evaluation, text formatting, force_overlay,
  per-call instancing rules, calc_bars_count semantics, negative array indexing,
  improved drawing limits.
- Logical inconsistencies (paths that disagree, dead conditions, asymmetric
  bull/bear handling that is not justified).
- Redundancies / dead code.
- Performance optimizations available given the design constraints above.
- Pine v6 semantic hazards: e.g. ta.*/time() calls inside loops or conditionals
  and per-instance buffer rules; behavior of setters on deleted drawing objects;
  partial-argument UDT .new() calls; map iteration order; var-in-function scope.

## Severity scale
- P0: wrong signals/levels/alerts for users, or runtime error risk
- P1: visible misbehavior in edge cases, or meaningful perf waste
- P2: dead code, redundancy, maintainability, minor modernization
