# Drawing Decisions

Why a level or label is on your chart — and, more often the interesting question, why it isn't. Every line and label passes through one decision funnel; this doc walks the funnel stage by stage and names the rule (and the `FIX` tag or design constraint) behind each suppression.

Written against `pine/TheStratSuite_v3.2.0.pine`. Code references are function names and `FIX` tags; grep the source for them. Signal semantics are in `../concepts/signals.md`; bar-type notation in `../concepts/bar-types.md`.

Two design constraints from `../DESIGN_CONSTRAINTS.md` frame everything here:

- **One decision per level, applied to both lines and labels.** Any path where a line and its label can disagree is a bug.
- **Conditional creation over create-then-hide.** A suppressed level is never drawn invisibly — it is not created, or it is deleted and its handle nulled.

---

## The funnel

Every drawn object passes through up to six stages:

1. **Slot gates** — the timeframe slot is enabled and valid on this chart.
2. **Classification tree** — `shouldDrawC1Level` decides whether the C1/CC structure is a drawable setup at all, honoring the per-family signal toggles and FTFC filters.
3. **Structural suppressors** — post-tree rules that drop one *side* when the market has already resolved it.
4. **Derived-level gates** — period open, magnitude, exhaustion, and F2 open each ride on a surviving trigger, with their own gates layered on top.
5. **Lead Signal filter** — cross-timeframe directional suppression, mutating draw flags before render.
6. **Price-collision suppression** — two lines at the same price: the higher timeframe wins.

Stops are the deliberate exception — they skip most of the funnel (see Stage 4).

---

## Stage 1 — the slot gates

Nothing in a slot computes unless the slot is enabled (`TFConfig.enabled`) and the chart timeframe is at or below the slot's timeframe:

```pine
validTimeframe = timeframe.in_seconds() <= timeframe.in_seconds(tf)
```

An invalid slot draws nothing and is greyed in the data table rather than hidden. The full reasoning lives in `htf-correctness.md` (Rule 1).

---

## Stage 2 — the classification tree: `shouldDrawC1Level`

One function, called twice per slot with mirrored parameters: `shouldDrawC1High` passes `isBullish = true`, `shouldDrawC1Low` passes `isBullish = false` **with hammer and shooter swapped**, so inside the tree `momo_pattern` always means "the conviction candle for *this* side" (hammer for the high, shooter for the low). Every directional variable (`cc_in_force`, `c1_was_same`, `failed_opp`, `ftfc_blocks`, …) is resolved through the same `isBullish ?` mirror, so the tree is written once and cannot go asymmetric.

It returns a tuple `[result, is_continuation]`. `result` is the draw verdict; `is_continuation` classifies the setup for the Universal label vocabulary (`↑CONTINUATION` vs `↑REVERSAL`, via `c1IsHighContinuation`/`c1IsLowContinuation`) — it does not gate drawing.

Branches evaluate in priority order; the first match wins, and the default is **false** — nothing draws unless a branch affirms it.

| # | Branch (first match wins) | Family toggle | Conviction filter | FTFC toggle |
|---|---|---|---|---|
| 1 | CC is an outside bar (`cc_is_3`) | `show3Expansions` | — | `threeExpRequireFTFC` |
| 2 | Confirmed F2, either direction (`is_f2u`/`is_f2d`) | `show_p3_toggle` | — | `p3RequireFTFC` |
| 3 | C1 inside — reversal (C2 opposite **or flat**) | `showInsideReversals` | `insideRevOnlyMomo` | `insideRevRequireFTFC` |
| 3 | C1 inside — continuation (C2 same direction) | `showInsideContinuations` | `insideContOnlyMomo` | `insideContRequireFTFC` |
| 4 | C1 was a 3, CC broke this side (3-2 in force) | `showRangeExpansions` | `rangeExpOnlyMomo` | `rangeExpansionsRequireFTFC` |
| 5 | C1 was a 3, CC still inside (potential 3-2) | `showRangeExpansions` | `rangeExpOnlyMomo` | `rangeExpansionsRequireFTFC` |
| 6 | C1 is a hammer/shooter (momo branch) | — / `showContinuations` | (is itself the filter) | `reversalsRequireFTFC` / `continuationsRequireFTFC` |
| 7 | C1 was 2 opposite (2-2 reversal), CC in force or not opposite | `showAllReversals` | `reversalsRequireActionable` | `reversalsRequireFTFC` |
| 8 | C1 was 2 same (2-2 continuation), CC in force or inside | `showContinuations` | `twoTwoContOnlyMomo` | `continuationsRequireFTFC` |

Sharp edges, in branch order:

- **An outside CC preempts everything (branch 1).** When CC engulfs C1, both of C1's triggers have already broken — no C1-based setup is meaningful. If `show3Expansions` is off, an outside CC therefore draws *no* trigger levels at all. This is the most common "my signal vanished" report, and it is by design.
- **A confirmed F2 draws both sides (branch 2).** The reclaimed C1 range *is* the trade zone, so `failed_same` and `failed_opp` both return true — each side's call takes this branch. If the Reclaims family (`show_p3_toggle`) is off, the candle falls through and may still draw under a later branch as an ordinary 2.
- **A flat (doji) C2 counts as a reversal context (branch 3).** `inside_is_reversal` accepts "C2 opposite *or* C2 flat." `FIX CSS-1` aligned the in-force computation to this same rule after the two paths disagreed — the line drew and alerted while the table, sticky stops, and only-when-in-force gates said nothing was live. The tree's definition is canonical; anything classifying inside reversals must match it.
- **The momo branch is guarded by `not (c1_is_3 and not showRangeExpansions)` (branch 6).** Without the guard, a hammer that was also an outside bar would sneak a 3-2 level in through the back door while that family is toggled off.
- **The momo *reversal* side has no family toggle (branch 6).** A hammer/shooter reversal level draws whenever the pattern context is reversal-shaped and CC is not already a 2 the opposite way (`not cc_opposite`) — beyond that, subject only to FTFC. The momo *continuation* side requires `showContinuations`. One hard suppression heads the branch: `cc_is_opp_continuation` (`cc_opposite and c1_was_opp` — CC already continuing the opposite way through the pattern) returns false before either outcome is considered.
- **`FIX BTC-1` (branch 6):** the momo branch used to test the *inside-reversal* FTFC toggle for both of its outcomes. It now obeys the toggle of the family it resolves to — reversal → `reversalsRequireFTFC`, continuation → `continuationsRequireFTFC`. When adding a branch, wire the FTFC gate to the family the branch *is*, not the one it sits near.
- **The 2-2 reversal's F2 filter is not in the tree (branch 7).** `reversalsRequireC1F2` is enforced post-tree (Stage 3) because it needs `c1_was_f2u`/`c1_was_f2d`, computed alongside the other per-slot classification. `reversalsRequireActionable` is bypassed when C1 is itself a hammer/shooter — the conviction requirement is already met.
- **2-2 continuations look no further back than C1 (branch 8).** The pattern is C1 and CC; nothing about C2 gates it. `FIX CONT22-PRIOR-1` removed the old `not c2_was_3` guard and dropped the parameter from `shouldDrawC1Level` — it had been silently discarding every continuation in a `3 → 2u → 2u` sequence. A branch that wants a three-bar combination names C2 explicitly, the way 3-2 Expansions do.

---

## Stage 3 — structural suppressors

The tree says "this setup family is enabled and qualifies." A second layer then drops one side when price action has already resolved it:

```pine
drawHigh = drawHighResult and not three_2d_suppress_high and not inside_breakout_suppress_high and not twotwo_rev_f2_suppress_high
```

(and the mirrored `drawLow`.) The suppressors:

- **`three_2u_suppress_low` / `three_2d_suppress_high`** — C1 was a 3 and CC committed to one side. The losing side's trigger drops: once the 3-2 resolves upward, the low trigger is noise. Exception: CC is itself failing (`is_f2u`/`is_f2d`) — the failure keeps both sides relevant.
- **`inside_breakout_suppress_low` / `inside_breakout_suppress_high`** — inside setup, CC broke one side. The coil resolved; the unbroken side's trigger drops. Exceptions: an active F2 (the reclaim draws both sides) or CC going 3 (branch 1 owns that case).
- **`twotwo_rev_f2_suppress_high` / `_low`** — the F2 filter for 2-2 reversals: with `reversalsRequireC1F2` on, a reversal against a C1 that was *not* itself a Failed 2 is suppressed. Carve-outs: C1 is 3, C1 is inside, or CC has an active F2 — those are different setups that merely share the geometry.
- **`cc_3u_suppress_low` / `cc_3d_suppress_high`** — CC is an outside bar closing one way. These do **not** touch the trigger lines (an outside bar broke both, both stay) — they only enter `high_suppress_base`/`low_suppress_base`, which gate magnitude and exhaustion: targets project only on the side the expansion is closing toward.

---

## Stage 4 — derived levels ride their trigger

Each derived level requires a surviving parent and adds gates of its own. The recurring theme: **no orphans** — a target with no visible signal behind it is treated as a bug, so every fallback path re-requires `drawHigh`/`drawLow`.

### Period open line

Draws only when `showOpen and (drawHigh or drawLow)` — an open line with no signal is an orphan. Suppressed additionally when:

- the slot is in preview or straddled (`FIX HTF-STRADDLE-1`): a straddled slot's open is a proxy value, and drawing it would present an estimate as data;
- an F2/pre-F2 is active with `showF2OpenLine` on (`f2SuppressesOpen`): the F2 open reference line replaces it;
- the Lead filter suppressed the only side that justified it (Stage 5's "orphaned open" cleanup).

### Magnitude (`drawMagHigh` / `drawMagLow`)

Two positive paths, then a battery of suppressors. Positive: `force_mag_*` (global `showMagnitude` plus a qualifying trigger, inside→3 setup, or outside-bar expansion side), or the fallback `drawHigh and shouldDrawMagnitude(...)`. The fallback deliberately requires `drawHigh`/`drawLow` — without it, `signal_in_force` firing while the family toggle is off produced orphan magnitude lines.

`shouldDrawMagnitude(c1_broke_c2, is_signal_in_force)` is `not c1_broke_c2 and is_signal_in_force`: if C1 already traded through C2's extreme, the "target" is behind the entry — meaningless, so suppressed.

The suppressors, each with its reason:

| Suppressor | Why the level is hidden |
|---|---|
| `high_suppress_base` / `low_suppress_base` | Side structurally resolved away (the `three_2*` and `inside_breakout` suppressors plus the outside-CC closing direction — the `twotwo_rev_f2` suppressor is *not* part of the base) |
| `p3_suppress_mag_high = is_f2u` (mirror for low) | An F2u *is* a failed upside — projecting an upside target off a failed upside is a contradiction |
| `cont_suppress_mag_high/low` (`is_22_cont_*`, `is_32_cont_*`) | Continuations broke C2's extreme by definition; magnitude is already behind price. Exhaustion takes over as the target (`cont_show_exh_*`) |
| `exh_high_same_as_mag` / `exh_low_same_as_mag` | Dedup: exhaustion at the same price wins, one line not two |
| `mag_high_same_as_trigger` / `mag_low_same_as_trigger` | Dedup: equal highs/lows make C2's extreme identical to the trigger; the trigger wins |
| `magOnlyWhenInForce and not rawInForce*` | User opted to see targets only on live trades |

### Exhaustion (`drawExhHigh` / `drawExhLow`)

Base condition (`exh_*_base_condition`): global `showExhaustion`, a valid pivot (`exh_*_valid` — exists and differs from the trigger), the optional `exhRequiresMagHit` gate, dedup against the *opposite* side's trigger and magnitude, `*_suppress_base`, and the same F2 directional suppression as magnitude. Then one of four positive paths: the parent trigger draws; **the level was already crossed** (`data.exh*Crossed`); a continuation is using exhaustion as its target (`cont_show_exh_*`); or an outside-bar expansion projects it (`threeExp_show_exh_*`, which per `FIX CSS-2` requires the global `showExhaustion` — it once drew with the master toggle off).

The "already crossed" path implements the product decision that **hit levels turn grey rather than disappear** (`COLOR_HIT_HIGH`/`COLOR_HIT_LOW`): a target you reached stays on the chart as a record even after its trigger line is gone. Same rule colors crossed magnitude.

### Outside-bar (3 Exp) targets

Separately toggled (`show3ExpMagnitude`, `show3ExpExhaustion`) and one-sided: direction comes from CC's close vs open, and only the expansion side projects (`threeExp_show_mag_high` requires the bar closing up, etc.). The inside→3 case (`inside_3u_setup`/`inside_3d_setup`) is gated behind `show3Expansions` for the same anti-orphan reason as the magnitude fallback.

### Stops — the deliberate exemption

`drawStopHigh/Low` needs only `showStopLevels` and the sticky latch (`stopHighTriggered`) — no FTFC, no structural suppressors, and stops are excluded from Stage 6's line suppression (see the comment in `collectLinesFromTF`). One deliberate exception: Stage 5's Lead filter does clear stop flags on a suppressed counter-trend side (`suppressHighFlags`/`suppressLowFlags` include `drawStop*`, and a trend-aligned F2 drops the counter-side stop). Per `../DESIGN_CONSTRAINTS.md`: stop lines are intentionally excluded from line suppression (risk management). Do not route stops through any cosmetic suppression you add.

---

## Stage 5 — the Lead Signal filter

With `enableDirectionalFilter` on, the highest enabled timeframe with a signal in force becomes the anchor (`getSignalDirection`; when both sides are in force, `FIX LEAD-F2-TIEBREAK-2` resolves F2s explicitly first — F2u is bearish, F2d bullish — because the generic "ends in u" test reads `F2u` as bullish and would invert the whole filter). Slots *below* the anchor then have their flags mutated before render:

- **Counter-trend confirmed F2** → `suppressAllFlags()`: everything goes, including the open line. In-force state is deliberately left alone (`FIX LEAD-TABLE-1`, superseding `P1-g`) so the data table still reports what that timeframe is doing; only drawing is affected. Keep highlighting a timeframe whose lines were hidden.
- **Trend-aligned confirmed F2** → the signal stays; only the counter-side extras (mag/exh/stop flags on the suppressed side) drop.
- **Regular signal or pre-F2** → the counter-trend side's draw flags are suppressed (in-force is untouched — `FIX LEAD-TABLE-1`); pre-F2 flags are cleared (not yet a confirmed reversal); and if the surviving side isn't drawing, the open line is cleaned up as an orphan.

Two invariants make this stage safe:

1. **Filter first, render second.** `renderSignalLevels` runs after the filter has settled the flags, so there is no draw-then-delete flash and no `line.set_*` on something about to be deleted.
2. **Labels read the same verdict.** The label pass rebuilds the identical suppression decision (`skipHighLabels`/`skipLowLabels`) and feeds `collectTimeframeLabels` a `modifiedResult` with the suppressed side's fields nulled. One decision, two surfaces — a line without its label, or a label without its line, is a Stage 5 bug.

`exhDisablesInForce` interacts here: with it on, a signal that hit exhaustion stops counting as in force, which releases the Lead anchor and lets lower-timeframe reversals draw again.

---

## Stage 6 — price-collision suppression across timeframes

Six slots frequently produce lines at the same price (a daily C1 high that is also the weekly C1 high). `collectLinesFromTF` gathers every live line with its price, timeframe seconds, owner slot, and a kind code; `suppressLowerTFLines` rounds prices to `syminfo.mintick` and keeps only the highest-timeframe line at each price, deleting the rest. High-side, low-side, and open lines are pooled separately.

`FIX P1-h` is the load-bearing detail: deleting the line is not enough — the owning `TimeframeData` field must be nulled (`nullSuppressedLine`), for two reasons. First, the render phase would otherwise call `line.set_*` on a deleted id (undefined behavior). Second, nulling the field makes the render phase recreate the line via `line.new` on the next tick, so a suppressed line **reappears automatically once the collision clears**. Suppression here is a per-tick verdict, not a permanent deletion.

Stop lines are never collected into the pools — the risk-management exemption again.

---

## Why is my level not showing?

The diagnostic path, roughly cheapest-first. The Debug Panel (the `Debug` settings group) surfaces most of these flags (`drawHigh`, `drawLow`, `drawMagHigh`, `force_mag_high`, the classification booleans) for one chosen slot.

| Symptom | Likely rule | Where |
|---|---|---|
| No lines at all on a slot | Chart TF above slot TF, or slot disabled | `validTimeframe`, Stage 1 |
| Signal candle, no trigger line | Family toggle off, family FTFC toggle blocking, or HAM/SHO filter | Stage 2 table |
| Everything vanished on a big candle | CC went outside bar with `show3Expansions` off | Stage 2, branch 1 |
| One side's line disappeared mid-bar | Inside breakout or 3-2 resolution suppressed the losing side | Stage 3 |
| Trigger draws, no magnitude | C1 broke C2 (`shouldDrawMagnitude`), continuation (exhaustion is the target), F2 direction, dedup vs trigger/exhaustion, or `magOnlyWhenInForce` | Stage 4 |
| Exhaustion missing | No valid pivot, `exhRequiresMagHit`, dedup, or `exhOnlyWhenInForce` | Stage 4 |
| Lower TF blank while higher TF signals | Lead Signal filter suppressing counter-trend | Stage 5 |
| Line missing on one TF but present on another at the same price | Collision suppression — higher TF won | Stage 6 |
| Open line missing | Preview/straddled slot, F2 open line replacing it, or orphan cleanup | Stage 4 / Stage 5 |
| F2 levels absent though F2s show in the table | Detection on, Reclaims signal family off — see the two-layer note in `../concepts/signals.md` | `show_p3_toggle` vs `enableFailed2Detection` |

---

## Contributor checklist

Before merging anything that adds, gates, or suppresses a drawn object:

1. Is the decision centralized — one predicate, consumed by **both** the line path and the label path? Any way for them to diverge fails review (`../DESIGN_CONSTRAINTS.md`).
2. Is it conditional creation, not create-then-hide? If you suppress an existing object outside `computeSignalState`, do you delete it **and null the owning field** so render recreates it cleanly later (`FIX P1-h`)?
3. If you hide a signal's lines, do you leave its in-force flags alone? The data table reports market truth, not what the chart chose to draw (`FIX LEAD-TABLE-1`, which superseded `P1-g`). Gate drawing on the draw flags instead.
4. Does your FTFC gate belong to the family your branch resolves to, not a neighboring one (`FIX BTC-1`)? Does your in-force logic classify edge structures (flat C2) identically to the draw tree (`FIX CSS-1`)?
5. Does any new gate require the *opposite* side's level to exist? The opposite trigger is suppressed exactly when a signal goes in force — that requirement made the Take Action Window vanish for the default config (`FIX TAW-1`).
6. Can your feature produce an orphan — a magnitude, exhaustion, or open line whose parent trigger isn't drawn? Re-require the parent in every fallback path.
7. Did you leave stops alone? Stop levels bypass the structural, FTFC, and price-collision suppression by design (the Lead filter is the one sanctioned exception).
