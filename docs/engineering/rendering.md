# Rendering

How the Suite turns resolved signal state into chart objects — anchoring, object lifetimes, the label pool, and the consolidation passes — inside TradingView's drawing-object limits and without breaking the no-repaint contract.

Written against `pine/TheStratSuite_v3.2.0.pine`. Code references are function names and `FIX` tags; grep the source for them. This doc covers *how* drawing executes; *which* levels qualify to draw is decision logic, covered in `drawing-decisions.md`. Repaint fundamentals live in `repaint-prevention.md`; this doc assumes them.

---

## The object budget

TradingView caps drawing objects per script. The defaults are ~50 of each kind; the hard maximum is 500. The Suite declares its budget in the `indicator()` header: `max_labels_count = 200, max_lines_count = 200, max_boxes_count = 200`.

The platform behavior that makes this matter: **exceeding a cap is not an error — the oldest objects are silently garbage-collected.** For a script that drew a historical trail, that would mean levels quietly vanishing off the left edge. The Suite's answer is architectural: there is no trail. Every object on screen describes *now* — the current period's state across up to six timeframe slots — so the live set is small and bounded:

| Kind | Worst case | Where it comes from |
|---|---|---|
| Lines | 60 | 10 owned line fields per `TimeframeData` slot (trigger high/low, open, magnitude ×2, exhaustion ×2, stop ×2, F2 open) × 6 slots |
| Boxes | 12 | 2 Take Action Window boxes per slot × 6 |
| Labels | ~120 | ≤10 level labels per slot before consolidation, × 2 render modes (timeline + floating) |

200 of each covers the worst case with headroom. The data table and debug panel are `table` objects — a separate class with its own platform limits that does not consume this budget (created once on `barstate.isfirst`, cells rewritten on `barstate.islast`).

The pooling and update-in-place machinery below exists to keep the *churn* as bounded as the count: no delete-all-and-recreate storms on every tick, no flicker, no id turnover for the garbage collector to feed on.

---

## The pipeline: decide fully, then draw once

Rendering is the last phase of a strict sequence:

1. **Compute** — `computeSignalState` produces a `ProcessingResult` per slot: prices, colors, styles, and one `draw*` flag per level. It is called on every bar (state latching needs the full history); its flag-compute phase is gated `barstate.islast or showStopLevels` — with stops off (the default) it runs only on the last bar, with stops on it must also run on historical bars so the sticky stop latch survives a reload (see the comment above that gate). The filter and render phases below run only under `barstate.islast`.
2. **Filter** — the Lead Signal directional filter mutates those flags in place (`suppressHighFlags` / `suppressLowFlags` / `suppressAllFlags`).
3. **Render** — `renderSignalLevels` reads the *final* flags and draws. The comment above it states the rule: it is called after the filter has modified flags, **so no draw-then-delete is needed**.

Two invariants fall out of this ordering:

- **One decision per level, applied to both lines and labels.** The label-suppression logic in the `collectTimeframeLabels` call site mirrors the line-suppression flags exactly (building a `modifiedResult` with the suppressed sides blanked). Any path where a line and its label could disagree is a bug.
- **Conditional creation over create-then-hide.** An object exists if and only if its draw flag is true this tick; otherwise it is deleted. Nothing is ever "hidden" with transparent colors or off-screen coordinates. (This is a stated design constraint — see `../DESIGN_CONSTRAINTS.md` — do not propose hide-based schemes.)

---

## X/Y anchoring rules

### Lines and boxes anchor in time, not bar index

Every line and box uses `xloc.bar_time` (`updateOrCreateLine`, `updateOrCreateBox`). Two reasons:

1. **HTF geometry doesn't live on chart bars.** A weekly level's start is the weekly period's open timestamp, which need not coincide with any chart bar's index — and must land in the same place on a 15m chart and a 4H chart.
2. **The right edge is in the future.** Lines and boxes extend to `lineEndTime = max(raw.ccTime + periodDuration, time_close(tf))` for non-preview slots (`FIX LINEEND-CLOSE-1`, computed in `computeSignalState` and again in the render loop); preview slots keep `raw.ccTime + periodDuration`, because their `ccTime` is the chart bar and their served close belongs to the stale period, so the scheduled close would shorten the projection. `xloc.bar_time` accepts future timestamps freely; `xloc.bar_index` is capped a few hundred bars ahead.

Every line is horizontal — `y1 == y2 ==` the level price. The x-anchors per level:

| Object | x1 anchor | y |
|---|---|---|
| Trigger high/low | `data.prevTime` (C1 period open) | C1 high / low |
| Magnitude high/low | `data.prevMagTime` (C2 period open) | C2 high / low |
| Exhaustion high/low | `data.exhHighTime` / `data.exhLowTime` (pivot bar time) | pivot price |
| Open, stops, F2 open | `r.ccStartTime` (CC period open) | open / locked stop / CC open |
| Take Action Window boxes | `data.prevTime` → `lineEndTime` | trigger ↔ magnitude (or exhaustion); P3 windows span trigger ↔ trigger |

One honest caveat: `periodDuration` is `timeframe.in_seconds(tf) * 1000`, a nominal period — a month is not a constant number of seconds, and session gaps are not modeled. `FIX LINEEND-CLOSE-1` covers the cases where that falls short (31-day months, holiday-glued futures sessions) by taking the scheduled close when it is later. The right edge is cosmetic. **Only the y (the price) is load-bearing**; nothing computes off a line's x2.

### Labels: two render modes, two anchor systems

`consolidateAndCreate` can emit each consolidated label in either or both modes:

- **Timeline labels** — `xloc.bar_time`, anchored at the level's `lineEndTime` plus `labelOffset` chart-bars (converted to milliseconds). They sit at the line's right edge and stay with it.
- **Floating labels** — `xloc.bar_index`, anchored at `bar_index + floatingLabelOffset`. Re-stamped every tick, so they ride along with the latest bar.

Both use `label.style_label_left`: the anchor is the point, text extends right.

### Price identity is always mintick-rounded

Every same-price comparison in the render path rounds through `syminfo.mintick` first: label consolidation, line suppression, stop-at-break-even detection (`stopHighAtBE`), and F2-open overlap checks. Raw float equality is never used. Add a new comparison, inherit the rounding.

---

## Two lifetime models

The Suite deliberately uses different object lifetimes for lines and labels, matched to how stable each object's identity is.

### Owned lines and boxes: update in place

Each slot's `TimeframeData` owns its line and box references as named fields (`highLine`, `magLowLine`, `actionWindowBullish`, …). The render phase runs every field through one of two helpers:

- `updateOrCreateLine` / `updateOrCreateBox` — if the reference is `na`, create; otherwise mutate the existing object with setters (`line.set_xy1/xy2/color/width/style`). The object keeps its id across ticks; only its attributes move.
- `deleteLine` / `deleteBox` — delete if present, **return `na`**, and the caller always assigns the result back to the owning field.

That assign-back protocol is the safety rail. Its violation is a recorded bug class:

**Case study: `FIX P1-h`.** The line-suppression pass (below) deleted collision-losing lines but left the owning `TimeframeData` field pointing at the dead id. Next tick, the render phase called `line.set_*` on a deleted object — undefined behavior — and the suppressed line could never come back. The fix threads owner + kind through `LineInfo` so `nullSuppressedLine` can null the originating field: the render phase then recreates via `line.new` next tick, and a suppressed line reappears once the price collision clears. **Every path that deletes an owned object must null its field, no exceptions.**

Update-in-place is also what makes cheap state transitions possible: a magnitude level that gets hit takes its "Crossed" color (`COLOR_HIT_HIGH` / `COLOR_HIT_LOW`, gray by default) rather than disappearing — same object, one `set_color`.

### Pooled labels: rebuilt every tick (`FIX P1-i`)

Labels can't be owned the same way. Consolidation merges an arbitrary, changing set of levels into an arbitrary, changing set of labels — text, price, color, and even *count* shift tick to tick. There is no stable identity to update in place, and the old code's answer (delete every label, recreate every label, every tick) was pure churn.

The pool (`LabelPoolT`: an `array<label>` plus a `used` cursor) reuses by *position* instead of identity:

1. **Reset** — at the top of the `barstate.islast` block, `lblPool.used := 0`. No labels are deleted; the cursor just rewinds.
2. **Acquire** — `acquireLabel` hands out `items[used]` if one exists, re-stamping *every* attribute via setters (`set_xloc`, `set_y`, `set_text`, `set_color`, `set_textcolor`, `set_size`, `set_style`) so no residue survives from the label's previous life. Past the end of the array, it creates via `label.new` and pushes — the pool grows to the session's high-water mark. Either way the cursor advances. (The pool object is passed as a parameter so the function can mutate the cursor — Pine functions cannot assign globals.)
3. **Trim** — after all consolidation runs, a `while` loop pops and deletes every pooled label beyond `used`. The trim runs on every `islast` **regardless of whether labels are enabled** — with labels toggled off, `used` stays 0 and the trim clears the entire pool. Toggling a feature off must visibly remove its objects.

Allocation is therefore demand-driven, reuse is total, and the budget exposure is exactly the maximum number of labels simultaneously visible — never a function of how many ticks have elapsed.

---

## Consolidation and suppression

Two passes keep six timeframes' worth of levels readable when they land on the same price. Both key on mintick-rounded price; both run only on the last bar.

### Label consolidation (`consolidateAndCreate`)

Labels are collected per tick into three side-buckets — `highLabels`, `lowLabels`, `openLabels` — grouped by which side of price they sit on, not by signal direction (a bullish stop sits *below* at C1's low, so it collects into `lowLabels`; the bearish stop into `highLabels`). Within a bucket, a map from rounded price to index merges collisions: the first entry at a price keeps its color and anchor time, later entries append their text with `" + "`. One price, one label: `60m 2d-1-2u + D MAG 512.50` instead of two labels stacked unreadably.

A related fold happens earlier, at collection: a stop sitting exactly at its trigger price (break-even) skips its own label and instead appends `" + STOP"` to the trigger label (`stopHighAtBE` / `stopLowAtBE` in `collectTimeframeLabels`).

What consolidation does *not* do is collision layout for labels at *nearby-but-different* prices. Label overlap is a known platform limitation, deliberately sidelined (`../DESIGN_CONSTRAINTS.md`) — do not propose collision systems.

### Line suppression (`suppressLowerTFLines`)

Lines can't merge text, so the same collision is resolved by rank: **when two timeframes draw a line at the same rounded price, the higher timeframe wins** and the lower-TF line is deleted. `collectLinesFromTF` gathers every live line into `LineInfo` records (price, `tfSeconds`, line ref, owner slot, kind code); a map from rounded price to best-so-far settles each collision, deleting the loser *and* nulling its owner field via `nullSuppressedLine` (`FIX P1-h`, above) so it can return when prices diverge.

One deliberate exemption, commented at the collection site: **stop lines never enter suppression.** A stop must stay visible for risk management regardless of what trigger or magnitude line shares its price. Intentional — do not "fix" it.

---

## How rendering respects the no-repaint contract

The drawing layer inherits its guarantees from `repaint-prevention.md`; rendering adds nothing that could violate them:

- **Nothing historical is drawn.** Every create/update/delete in this doc sits under `barstate.islast`. State tracking runs on every bar; rendering does not. There is no painted trail whose past could differ after a reload.
- **Rendering is a pure function of resolved current state.** Each tick fully re-derives every object — position, color, text, existence — from this tick's `ProcessingResult` and latched slot state. No drawing decision depends on what was drawn last tick. Reload rebuilds the same state from bar history (the repaint doc's Rules 4–5), so it redraws the same objects.
- **Realtime rollback is harmless here.** Pine rolls back intra-bar mutations between ticks; because every object is re-stamped on every `islast` tick anyway, rolled-back drawing state is simply re-applied on the next tick. The pool's reset→acquire→trim cycle is idempotent by construction.
- **Anchors come from served data, not the clock.** Line x-anchors are period-open timestamps out of the security tuple (`data.prevTime`, `r.ccStartTime`), never `timenow`. The only forward projection is the cosmetic `lineEndTime`.
- **Suppression is display-only.** The directional filter and line suppression flip draw flags and delete objects; where hiding a line would desynchronize the table or alerts, the same pass clears the semantic flags too (`FIX P1-g`). Alerts edge off persisted was-state, not off what happens to be drawn.

---

## Contributor checklist

Before merging anything that creates, mutates, or deletes a drawing object:

1. Is creation conditional on a resolved draw flag, and does the flag dropping *delete* the object — never hide it via transparency or off-screen coordinates?
2. Does every delete path assign `na` back to the owning field (`deleteLine`/`deleteBox` return value, or `nullSuppressedLine` for suppression) so no later tick can call `set_*` on a dead id (`FIX P1-h`)?
3. Do new labels go through `acquireLabel` and the correct side-bucket — never a bare `label.new` in the render path — and does `acquireLabel` still re-stamp every attribute your label varies (`FIX P1-i`)?
4. Is every new same-price comparison rounded through `syminfo.mintick` before comparing?
5. Does the worst-case live-object count still clear the 200/200/200 header budget with headroom? Redo the inventory math in this doc if you added a per-slot object.
6. Is all new drawing under `barstate.islast`, anchored `xloc.bar_time` from served period timestamps (floating labels excepted — they ride `bar_index`) — with nothing load-bearing on a projected right edge?
