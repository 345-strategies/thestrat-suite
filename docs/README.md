# TheStrat Suite — Documentation

Docs index for the open-source release. Two audiences, two folders:

- **`concepts/`** — for traders using the indicator. What the signals mean, how to read the chart, how to configure it.
- **`engineering/`** — for builders forking or extending the script. How the engine works and the rules that keep it correct.

Engineering docs reference the code by function name and `FIX` tag (grep-able), not line number. Written against `pine/TheStratSuite_v3.1.1.pine`.

## Existing docs

| Doc | Audience | Notes |
|---|---|---|
| `concepts/bar-types.md` | Trader | The three structures, the u/d overload, Failing 2s + NOTATION-1 casing rule, HAM/SHO, combo strings, the canonical tuple |
| `concepts/signals.md` | Trader | The seven signal types with defaults and filters, potential vs in-force mechanics, detection-vs-signal F2 nuance, Universal names |
| `concepts/ftfc.md` | Trader | Full Timeframe Continuity: the sign channel, the voting slots, display and alert integration |
| `concepts/targets-and-stops.md` | Trader | Magnitude, exhaustion, and stop levels; Take Action Windows; every setting that touches them |
| `concepts/reading-labels.md` | Trader | Decoder for everything drawn: label anatomy, colors, the data table row by row, alert message format |
| `TheStratSuite_v2.2.7_Settings_Reference.md` | Trader | Full settings reference, every input in panel order |
| Bar coloring (v3) | Trader | Candle painting by Strat classification or FTFC: the two modes, family toggles, conflict and flip options — covered in the Settings Reference under Display - Bar Coloring |
| `engineering/architecture.md` | Builder | The pipeline: security → preview shift → compute → filter → render; where each invariant lives |
| `engineering/repaint-prevention.md` | Builder | The no-repaint contract: 8 rules + contributor checklist |
| `engineering/htf-correctness.md` | Builder | Reading 6 TFs from one chart: validTimeframe gate, straddle/glue handling, +12h rule, daily reconstruction, preview mode |
| `engineering/drawing-decisions.md` | Builder | Why a level is (or isn't) on the chart: the shouldDrawC1Level funnel and every suppression rule |
| `engineering/rendering.md` | Builder | x/y anchoring, update-in-place object lifetimes, the label pool, consolidation passes, the object budget |
| `engineering/performance.md` | Builder | The cost model: calc_bars_count tiers, tuple security calls, islast gating, bounded scans |
| `DESIGN_CONSTRAINTS.md` | Builder | Intentional design decisions — what reviewers must not flag as bugs |
| `v2.2.3_holiday_glue_fix.md` | Builder | Postmortem: CME holiday-glued bars (GLUE-1 / GLUE-2b) |
| `features.md` | Both | Feature-by-feature reference: every feature, its settings path, and the doc that goes deeper |
| Root `README.md` + `LICENSE` (MPL-2.0) | Both | Public-facing; confidential framing removed 2026-07-21 |
| Root `CHANGELOG.md` | Both | Full version history backfilled from snapshots and `FIX` comments, Keep a Changelog format |
| Root `CONTRIBUTING.md` | Builder | How to contribute: required reading, FIX-tag conventions, manual verification workflow, PR checklist |
| `RELEASE_CHECKLIST.md` | — | Pre-flight for the visibility flip: prep done, release-moment items left for the maintainer |

Research-track scripts are documented separately in `../pine/research/README.md`.

Outside research we point to: `research/strat-stats-notprofgreen.md`, @notprofgreen's NQ setup statistics and how to read them.

## Planned

The launch docs backlog is fully written; new docs get a row above when they land.

## Source material

- `FIX` comments in `pine/TheStratSuite_v3.1.1.pine` — each tag documents a solved bug and its rule
- `DESIGN_CONSTRAINTS.md` — the invariants list; feeds `engineering/architecture.md`
- `v2.2.3_holiday_glue_fix.md` — feeds `engineering/htf-correctness.md`
