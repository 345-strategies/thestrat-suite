# Signals

A signal in TheStrat Suite is a **context + action** pair: what the prior candle (`C1`) set up, and what the current candle (`CC`) does to its range. The Suite draws the actionable levels, tells you when one is live, and stays out of the way otherwise.

Bar-type notation is defined in `bar-types.md` — this doc assumes it.

---

## How a signal works

1. **Levels.** When an enabled setup exists, the prior candle's high and/or low are drawn as trigger lines. An inside-bar setup draws both sides (yellow above, orange below) — the break direction isn't known yet.
2. **Potential.** Until price crosses a trigger, labels carry the `*` prefix (`*2d-1-2u`): a setup, not a trade.
3. **In force.** Price beyond the trigger with the setup's conditions met: the line takes the signal color (green/red), the `*` drops, the alert fires, and the timeframe highlights in the data table. A signal stays in force only while price holds beyond the trigger. One exception: a Failing 2 is in force from *detection* — the failure itself is the event, not a trigger cross.
4. **Targets.** Magnitude and exhaustion levels project from there — covered in `targets-and-stops.md`.

---

## The signal types

| Family | Type | Example | Default | One line |
|---|---|---|---|---|
| Reversals | Inside Reversal | `2d-1-2u` | **On** | Consolidation resolves against the prior move |
| Reversals | 2-2 Reversal | `2d-2u` | **On** | Direct reversal, no pause |
| Continuations | Inside Continuation | `2u-1-2u` | **On** | Pause, then the move resumes |
| Continuations | 2-2 Continuation | `2u-2u` | Off | Sustained push — noisy unfiltered |
| Reclaims | Failing 2 (Range Reclaim) | `F2u` / `F2d` | Off | The breakout itself fails — one-bar reversal |
| Expansions | 3-2 Expansion | `3-2u` | Off | Outside bar's indecision resolves |
| Expansions | Outside Bar (3 Exp) | `3u` / `3d` | Off | CC engulfs the prior range |

### Inside Reversals — the bread-and-butter

`C1` is inside, `CC` breaks opposite `C2`'s direction. Price coiled, then rejected the prior move. A `C2` with no direction (doji) counts as the reversal side, so the setup still qualifies. Filters: **HAM/SHO** (require `C1` to be a hammer/shooter — the coil already rejected one side), **FTFC**.

### 2-2 Reversals — faster, less coiled

`CC` immediately breaks against a directional `C1`. Filters: **HAM/SHO**, **FTFC**, and the distinctive one — **F2**: require `C1` to have been a Failing 2 itself (`F2d-2u`). The prior bar already trapped traders on a failed break; their exits fuel your move.

### Inside Continuations

`C1` inside, `CC` resumes `C2`'s direction — a trend pullback in miniature. Filters: HAM/SHO, FTFC.

### 2-2 Continuations — off for a reason

Any two trending bars qualify, which is why this ships disabled. Turn it on with HAM/SHO or FTFC attached, or expect noise. The pattern is `C1` and `CC` only — what happened before `C1` does not gate it, so a `3 → 2u → 2u` sequence is a valid continuation.

### Failing 2s (Range Reclaims)

`CC` breaks a side, then reclaims it — the one-bar reversal the community also calls a Potential 3 or 1-bar Rev Strat. Direction letter names the *failed break*: `F2u` is bearish, `F2d` bullish. In force from detection; the take-action zone is the reclaimed `C1` range itself.

Note the two-layer design: **detection** (Advanced → Failing 2 Detection, on by default) classifies F2s everywhere — table, labels, line styling — while the **signal toggle** (Signals → Reclaims, off by default) decides whether they trade: levels, windows, alerts. Detection on + signal off means you *see* F2s but aren't prompted to act on them.

### 3-2 Expansions

`C1` was an outside bar — both sides ran, indecision. `CC` committing to one side resolves it, and the move often runs hard if the 3 shook out weak hands. Filters: HAM/SHO, FTFC.

### Outside Bars (3 Exp)

`CC` itself takes both sides of `C1`. Direction from close vs open (`3u`/`3d`). This is volatility expansion, not a directional setup in the classic sense — magnitude/exhaustion targets for it are separately toggleable under Advanced.

In force: a 3 is in force bull while price is above `C1`'s high and bear while below `C1`'s low. Back inside `C1` it is contested (an F3) and not in force. That one rule feeds the Lead Signal, stops and the data table (`OUTSIDE-IF-1`).

---

## Cross-cutting filters

- **HAM/SHO** — per-family toggle requiring `C1` conviction (see `bar-types.md` for the three definitions).
- **FTFC** — per-family toggle requiring all monitored timeframes to agree on direction (close vs open). Ships off everywhere. It filters hard, so I made it opt-in.
- **Lead Signal** (Filters → Lead Signal, off) — the highest timeframe with a signal in force becomes the Lead; lower timeframes only show setups aligned with it. Counter-trend noise on the 15m stops fighting your daily.
- **Domino** (always computed) — consecutive inside bars stacked across timeframes, flagged in the table and alertable. A market coiled on multiple timeframes at once.

## Universal mode names

With Label Style = Universal, the same signals read in plain language: `↑REV`, `↑CONT`, `↑INS`, `↑EXP`, `OUTSIDE`, and `FAILING` for a live F2 — with `◆` marking HAM/SHO conviction and `*` still meaning potential. Same engine, same triggers; only the vocabulary changes.

---

*Why a given line draws or doesn't: `../engineering/drawing-decisions.md`. Targets and stops: `targets-and-stops.md`.*
