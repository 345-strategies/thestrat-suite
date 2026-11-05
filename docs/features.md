# Feature Reference

Every feature in TheStrat Suite: what it does, where its settings live, and which doc covers it in depth.

Applies to v3.0.0. Settings paths refer to the indicator's settings panel groups.

---

## Detection

**Six-timeframe engine.** The Suite monitors up to six timeframes at once from a single chart — bar classification, signals, and continuity on each. Presets configure all six in one click: TheStrat Classic, Scalp, Day Trade, Futures/Crypto, Swing Trade, Investing, or Custom for your own set. Settings: *Timeframe Preset*, *Timeframes*. Deep dive: [architecture](engineering/architecture.md), [htf-correctness](engineering/htf-correctness.md).

**Bar classification.** Every candle typed against the prior candle's range: inside (1), directional (2u/2d), outside (3u/3d), with Failing 2s (F2u/F2d) as a modifier. The same classifier feeds the chart, the table, and the alerts, so they can never disagree. Deep dive: [bar-types](concepts/bar-types.md).

**Strat combo labels.** Signals are labeled in standard Strat notation (`2d-1-2u HAM`), oldest to newest, with `*` marking potential setups that haven't triggered. Settings: *Display - Labels*. Deep dive: [reading-labels](concepts/reading-labels.md).

**Inside Reversals.** C1 inside, current candle breaks against C2's direction. On by default. Filters: HAM/SHO, FTFC. Deep dive: [signals](concepts/signals.md).

**2-2 Reversals.** Direct reversal with no inside-bar pause. On by default. Filters: HAM/SHO, FTFC, and F2 (require the prior bar to have been a Failing 2 — trapped traders fuel the move). Deep dive: [signals](concepts/signals.md).

**Inside Continuations.** Pause, then the prior move resumes. On by default. Filters: HAM/SHO, FTFC. Deep dive: [signals](concepts/signals.md).

**2-2 Continuations.** Sustained push, two directional bars the same way. Off by default because it's noisy unfiltered. Filters: HAM/SHO, FTFC. Deep dive: [signals](concepts/signals.md).

**Failing 2s (Range Reclaims).** A breakout that fails and reclaims the range — the one-bar reversal. Four detection methods (Reclaim, Open, both, either). Detection is always on for classification; the signal toggle decides whether they trade. Settings: *Signals - Reclaims*, *Advanced - Failing 2 Detection*. Deep dive: [signals](concepts/signals.md), [bar-types](concepts/bar-types.md).

**3-2 Expansions.** An outside bar's indecision resolved by the next candle committing to a side. Off by default. Deep dive: [signals](concepts/signals.md).

**Outside Bars (3 Exp).** The current candle engulfs the prior range — volatility expansion, direction from close vs open. Off by default. Deep dive: [signals](concepts/signals.md).

**Hammers & Shooters.** Candle-proportion patterns used as conviction filters or standalone signals, with three selectable definitions (Broad, Classic, Pin Bar) and an optional color match. Settings: *Filters - Hammer/Shooter Detection*. Deep dive: [bar-types](concepts/bar-types.md).

**Full Timeframe Continuity (FTFC).** Are all monitored timeframes trading above (or below) their opens right now? Shown in the table, usable as a per-signal filter, and alertable on shifts. Off as a filter by default — it filters hard, so I made it opt-in. Deep dive: [ftfc](concepts/ftfc.md).

**Lead Signal filter.** The highest timeframe with a signal in force becomes the Lead; lower timeframes only *draw* setups that agree with it. Chart-only — the data table keeps reporting every timeframe truthfully, filtered or not. Settings: *Filters - Lead Signal*. Deep dive: [signals](concepts/signals.md).

**Domino detection.** Consecutive inside bars stacked across timeframes — a market coiled on several scales at once. Shown in the table, alertable. Settings: *Filters - Domino Setups*. Deep dive: [signals](concepts/signals.md).

## Levels and risk

**Trigger levels.** The prior candle's high/low drawn as entry lines, styled by state: yellow/orange potential, green/red in force, gray crossed. Deep dive: [reading-labels](concepts/reading-labels.md), [drawing-decisions](engineering/drawing-decisions.md).

**Magnitude targets.** The measured first target (C2's extreme) for every actionable signal, with an "only when in force" mode. Settings: *Targets - Magnitude & Exhaustion*. Deep dive: [targets-and-stops](concepts/targets-and-stops.md).

**Exhaustion levels.** The extended target: the prior intact swing beyond magnitude, found by pivot scan. Optional "only after magnitude hit." Deep dive: [targets-and-stops](concepts/targets-and-stops.md).

**Take Action Windows.** The zone between trigger and target, shaded while an entry or add still works. Extends to exhaustion if you want. Settings: *Targets - Take Action Windows*. Deep dive: [targets-and-stops](concepts/targets-and-stops.md).

**Stop losses.** A stop on the opposite side of the trigger, CC or C1 referenced, locked for the period once a signal fires, with optional break-even moves at magnitude or exhaustion, and a Smallest Timeframe Only display mode that draws just the tightest active stop when several timeframes are in force. Off by default. Settings: *Stops - Stop Levels*. Deep dive: [targets-and-stops](concepts/targets-and-stops.md).

## Display and workflow

**Data table.** Live multi-timeframe state: C2, C1, actionable-setup and current-candle columns in Strat notation (Full mode), or a compact color-coded row (Compact mode), plus FTFC, Domino, and Lead rows. Settings: *Display - Data Table*. Deep dive: [reading-labels](concepts/reading-labels.md).

**Universal labels.** The same signals in plain language — REV, CONT, INS, OUT, EXP, FAILING — for traders who don't speak Strat notation yet. Settings: *Timeframe Preset → Label Style*. Deep dive: [reading-labels](concepts/reading-labels.md).

**Bar coloring** (new in v3). Chart candles painted by their Strat classification or by FTFC direction, with optional Failing-2 flip highlighting. History is graded honestly — the deep dive covers how. Off by default. Settings: *Display - Bar Coloring*. Deep dive: [settings-reference](TheStratSuite_v2.2.7_Settings_Reference.md#display---bar-coloring).

**Bar numbers** (unreleased). Each chart candle's Strat number (1, 2u, 2d, 3u, 3d, F2u, F2d) printed beside it, from the same classifier as Strat Candles and the data table. Tiered emphasis fades plain 2s so 1s, 3s and Failing 2s stand out. Off by default. Settings: *Display - Bar Numbers*. Deep dive: [settings-reference](TheStratSuite_v2.2.7_Settings_Reference.md#display---bar-numbers).

**Preview mode.** When the market is closed, the Suite shifts to next period's levels so you can plan before the open — automatic per asset class (equities, futures, crypto), with holiday handling. Settings: *Display - Preview Mode*. Deep dive: [htf-correctness](engineering/htf-correctness.md).

**Alerts.** One consolidated alert covering every enabled timeframe with optional trigger/target/stop prices in the message, plus per-timeframe, direction, potential-setup, Domino, and FTFC-shift alert conditions. Alerts fire on state changes, never on every bar. Settings: *Alerts - Detailed*. Deep dive: [settings-reference](TheStratSuite_v2.2.7_Settings_Reference.md), [repaint-prevention](engineering/repaint-prevention.md).

**No repainting.** Not a toggle — a contract. Completed-bar signals never change on reload; the rules and their receipts are documented. Deep dive: [repaint-prevention](engineering/repaint-prevention.md).

**Debug panel.** Per-timeframe internals (classification flags, draw decisions) on the chart for troubleshooting a setup that didn't draw. Settings: *Debug*. Deep dive: [drawing-decisions](engineering/drawing-decisions.md).
