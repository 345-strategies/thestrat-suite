# Repaint Prevention

How TheStrat Suite guarantees that what you saw live is what you see after a reload — and the rules any contribution must follow to keep it that way. Every rule here came out of a bug I shipped, found, and fixed; the `FIX` tags are the receipts.

Written against `pine/TheStratSuite_v3.1.1.pine`. Code references are function names and `FIX` tags; grep the source for them.

---

## What "repaint" means here

Three distinct failure modes, only two of which are bugs:

1. **Live/reload divergence.** The chart said one thing during the session; a refresh says something different about the same moment. This is the trust-killer — an alert fired on a level that no longer exists.
2. **State loss on reload.** A locked stop or a "magnitude already hit" flag vanishes after a refresh because the state was only ever computed on realtime ticks.
3. **Forming-bar movement.** The current candle's trigger status changes as price moves. This is not a bug — a live trigger *is* the product. But forming-bar data must be fenced off from anything the indicator remembers.

Every rule below exists to eliminate 1 and 2 while keeping 3 deliberate and contained.

---

## The architectural stance: no historical drawing objects

All lines, boxes, labels, and table cells are created or updated **only on the last bar** (`barstate.islast`), from state latched per higher-timeframe (HTF) period. The Suite never draws signal objects onto past bars.

This is the single biggest anti-repaint decision. There is no historical trail of drawing objects that could silently differ after a reload. The only object surface that must be reproducible is the *current period's* state — and every rule below exists to make that state rebuildable from bar history alone.

One deliberate historical surface exists as of v3: **bar coloring** (`BARCOLOR-1`, `SECTION 14`) paints classification onto every historical candle via `barcolor()`. It creates no drawing objects, and it is allowed precisely because it obeys Rule 8 — historical paint may read only series that evaluate identically live and on reload.

---

## Rule 1 — Completed HTF bars come from `lookahead_on` plus offsets

Each timeframe slot makes **one** `request.security` call (`getDataWithExhaustion`) returning a 23-value tuple: the forming bar at offset `[0]` (called **CC**), and completed bars at `[1]` (**C1**), `[2]` (**C2**), `[3]`, `[4]`, plus exhaustion pivots.

With `barmerge.lookahead_on`:

- **Offsets `[1]` and beyond are final, confirmed values.** This is the canonical non-repainting HTF idiom: by the time a chart bar exists inside period N, period N−1's OHLC is settled and identical live or on reload.
- **Offset `[0]` behaves differently by context.** For a *closed* period, historical chart bars receive the period's final values. For the *open* period, every chart bar receives the period's running values — which on reload means early-session bars see the whole-session-so-far extremes, not what they saw at the time.

Consequence: **signals are anchored to C1 and C2.** Trigger prices, magnitude targets, and combo classification all come from completed bars. CC participates only as live trigger state — which brings in Rule 2.

---

## Rule 2 — CC may drive monotone latches, never snapshots

State derived from security data falls into two categories:

**Monotone predicates** — "has the period high crossed magnitude yet?" Once true, stays true for the rest of the period. These are safe to latch from CC extremes on any bar: live, the flag latches progressively tick by tick; on reload, it latches from period-extremes-so-far. Both paths converge to the same value *now*, and only *now* is displayed. The crossed flags (`magHighCrossed`, `exhHighCrossed`, …) and the stop trigger latches work this way.

**Snapshots** — "what was the exhaustion pivot at period open?" Point-in-time values. These must be taken exclusively from completed bars (`[1]`+), because a `[0]` term evaluates differently live versus on reload.

### Case study: `FIX P0-2`

`findExhaustionLevels` seeded its running max/min from `high[0]`/`low[0]` inside the security context. Live, `[0]` was the developing bar; on reload it was the finished bar — so the latched exhaustion level moved after a refresh. The fix seeds from `high[1]`/`low[1]` and starts the pivot scan at `i = 2` so no term ever touches the forming bar.

The fix comment records a deliberate tradeoff: the reload-stable seed can leave a rare mid-period re-latch slightly behind live price, and that is accepted. **When freshness and reload-stability conflict, choose reload-stability.**

---

## Rule 3 — No `ta.*` calls in per-slot code paths

Pine `ta.*` history buffers belong to the **lexical call site**, not to the loop iteration executing it. A function called once per bar has one buffer; the same function called six times per bar (once per timeframe slot) *still has one buffer*, now interleaving six unrelated series.

### Case study: `FIX P0-1`

`computeSignalState` detected a new HTF period with `ta.change(time(tf))`. The function has a single call site inside `for i = 5 to 0`, so all six slots shared one buffer: on each iteration, `[1]` held the *previous slot's* value from the *previous bar*. "New period" fired on nearly every bar, wiping the crossed flags and sticky stops constantly.

The fix is explicit per-slot state: the raw tuple carries `realPeriodTime` (the served, unshifted period-open), and each slot compares it against its own stored `lastPeriodStart`. **Per-slot logic uses explicitly stored state, never `ta.*` implicit series.**

---

## Rule 4 — Period-scoped state resets only on a real period change

The reset key is `raw.realPeriodTime != data.lastPeriodStart` — the *served* period-open, immune to preview rewrites (preview reuses `ccTime` as a synthetic C1, so keying off shifted fields would reset every bar).

Two extra re-latch triggers exist for preview mode (`FIX P1-d`):

- **Every tick while previewing** (`isInPreview and barstate.islast`). This is also the defense against Pine's realtime rollback: mutations made during a tick are rolled back before the next tick's calc, so synthetic preview state must be re-derived each tick to stay alive.
- **On preview exit without a rollover** (`prevWasPreview and not isInPreview`), so preview-shifted values don't leak into the real period's latched state.

### Period-scoped state inventory (`TimeframeData`)

| Field(s) | Kind | Reset on |
|---|---|---|
| `prevHigh/Low/Time` (C1), `prevMagHigh/Low/Time` (C2) | Snapshot | new period / preview transition |
| `exhHighPrice/Time`, `exhLowPrice/Time` | Snapshot | new period / preview transition |
| `magHighCrossed`, `magLowCrossed`, `exhHighCrossed`, `exhLowCrossed` | Monotone latch | new period / preview transition |
| `stopHighTriggered/Price`, `stopLowTriggered/Price` | Monotone latch | new period / preview transition |
| `lastPeriodStart`, `wasPreview` | Bookkeeping | continuously |

---

## Rule 5 — Latched state must be reproducible from historical bars

A reload turns yesterday's realtime ticks into historical bars. Any latch that only runs under `barstate.islast` never executes for those bars on reload — the state silently vanishes.

### Case study: `FIX P1-a`

Sticky stops ("once a signal goes in force, the stop locks for the period") latched inside an `islast`-only block. Live: signal fires mid-session, stop locks, everything looks right. Reload: the bars where the signal fired are now historical, the latch never ran, the locked stop is gone.

The fix widens the gate to `if barstate.islast or showStopLevels`: when stops are enabled, detection and latching run on **every** bar so history rebuilds the lock; when they're off, the cheap `islast`-only path is kept. **For every `var` latch, ask: "if the chart reloads right now, does bar history rebuild this?"** Pay the every-bar cost only when the feature demands it.

---

## Rule 6 — Alerts fire on edges of persisted state

Alert logic never asks "is the signal active?" — it asks "did it just *become* active?" That requires last-bar state, and last-bar state is where repaint bugs breed:

- **Edge detection:** `wasSetupBull/Bear`, `wasF2Bull/Bear`, `wasPotentialBull/Bear` arrays hold the prior bar's classification; alerts fire only on false→true.
- **Snapshot before update:** the per-TF `alertcondition`s read pre-loop snapshots (`wasSetBull0`…), because the main loop overwrites the arrays before `alertcondition` evaluates.
- **Reset per period (`FIX P1-c`):** was-state resets when `realPeriodTime` changes, so a signal already in force at the open of a new period still produces an edge. Keyed off `realPeriodTime`, *not* `ccStartTime` — the latter is rewritten in preview and would reset every bar.
- **Update unconditionally (`FIX U2`):** even when a TF's checkbox excludes it from the consolidated alert message, its was-state still updates. An early `continue` here once made the per-TF `alertcondition`s fire every bar instead of on edges.
- **Never alert from preview or straddled slots:** their CC is synthetic or estimate-grade (`slotPreview`, `okA1`–`okA6` gates). The period reset re-arms cleanly when the slot exits preview, so the first real bar alerts normally.
- **Alerts obey the same toggles as display (`FIX P0-3`):** `isF2Bull/Bear` gate on `show_p3_toggle`, so a signal type the user disabled can't alert while the table says nothing is in force.

---

## Rule 7 — Wall-clock logic touches only the last bar

`timenow` drives exactly one subsystem: market-closed detection for Auto preview (session windows, weekends, the holiday fallback). All of it is gated on `barstate.islast and not isBarReplay`. Historical computation never consults the clock, so a reload reproduces history identically at 3 AM or noon.

Two sharp edges, both learned the hard way:

- **Measure staleness from the scheduled close, never the open stamp** (`FIX GLUE-1`). CME glues holiday half-sessions into the next trade date, so a live Monday bar can open with a Thursday stamp; `timenow - time` read ~87h and engaged preview mid-session. `timenow - time_close` cannot misfire — a live bar's scheduled close is always in the future. Full story in `htf-correctness.md` and `../v2.2.3_holiday_glue_fix.md`.
- **Bar replay is detected** (`bar_index < last_bar_index`) and disables Auto preview; `previewMode = Off` is the manual override for replay testing.

---

## Rule 8 — Historical paint must be reload-stable

v3's bar coloring (`BARCOLOR-1`, `SECTION 14`) is the one surface that touches historical bars: `barcolor()` paints every candle by its Strat classification or FTFC grade. Painted history is only honest if every input evaluates identically live and on reload, which restricts what historical paint may read:

- **Safe:** the chart bar's own OHLC; completed HTF bars at offset `[1]`+ (already settled per Rule 1); and served HTF period *opens* — an open never changes after its period starts, so it is period-stable under `lookahead_on`.
- **Unsafe:** served `[0]` HTF closes and extremes. On historical bars, `lookahead_on` serves the period's **final** values — painting history with `ftfc_up`/`ftfc_down` would color every bar of an up-closing day green from the open. Prophetic hindsight, not what the trader saw.

The two consequences in `SECTION 14`:

- **FTFC Candles re-runs `calculateFTFC`** with the chart bar's own close against the served HTF opens, grading every historical bar exactly as continuity stood at that bar's close. On the live bar the served HTF close *is* the live close, so the candle always agrees with the table's FTFC row.
- **Signals In Force Candles rebuild each slot's forming period** from chart bars (`paintSlotInForce`): running high/low keyed on the served period-open time, the chart close as the period close, and crossed-target flags latched per period. C1, C2, the period open and the current-channel exhaustion are completed or period-stable under `lookahead_on`, so the in-force test runs on honest inputs at every historical bar. It never reads `signalInForceHigh/Low`, whose served CC values are period-final on history. The FTFC filters inside it use continuity graded at the bar's close, as FTFC Candles does.
- **`paintSlotFailed2` rebuilds each slot's intra-period range from chart bars** — a running high/low keyed on the served period-open time — because the served CC high/low are final period values on historical bars, the same lookahead leak. `detectFailed2` then grades the honestly-reconstructed range with the same engine the signal path uses.

Strat Candles needs no reconstruction: `detectBarTypeAndFailed` reads only the chart bar and its predecessor, identical live and on reload by definition.

---

## Contributor checklist

Before merging anything that adds state, reads security data, or emits alerts:

1. Does any latched value read offset `[0]` in a security context? If it isn't a monotone predicate, it repaints (`FIX P0-2`).
2. Does new code call `ta.*` anywhere reachable per-slot, per-loop-iteration, or conditionally? Replace with explicitly stored state (`FIX P0-1`).
3. Is every new `var` latch rebuilt from historical bars on reload — or is it `islast`-only *on purpose* and display-only (`FIX P1-a`)?
4. Do new alerts edge off persisted was-state, reset on `realPeriodTime` change, update state even when muted, and stay silent in preview (`FIX P1-c`, `FIX U2`)?
5. Does anything consult `timenow` outside a `barstate.islast` gate — and if it measures staleness, does it measure from `time_close` (`FIX GLUE-1`)?
6. Does new historical paint read anything that differs live vs on reload — a served `[0]` close or extreme instead of chart-bar OHLC, completed `[1]`+ bars, or served period opens (`BARCOLOR-1`, Rule 8)?
