# Engine Architecture

The pipeline a bar of data travels from `request.security` to pixels — and where each engineering invariant lives along the way. If you are forking or contributing, read this first; the companion docs go deep on the two hardest stages.

Written against `pine/TheStratSuite_v3.1.1.pine`. Code references are function names, `SECTION` banners, and `FIX` tags; grep the source for them. Anti-repaint rules: `repaint-prevention.md`. Security-call traps: `htf-correctness.md`. Intentional design decisions a reviewer must not "fix": `../DESIGN_CONSTRAINTS.md`.

---

## The pipeline at a glance

```
 inputs ─ preset resolution (rTF1..rTF6, rEN1..rEN6)
   │
   ▼
 SECURITY LAYER          six request.security tuple calls (getDataWithExhaustion)
   │                     + one request.security_lower_tf("D") for reconstruction
   ▼
 SHIFT LAYER             applyPreviewShift → TFRawData[6]  (preview / straddle promotion)
   │                     + daily-sub-bar reconstruction of the forming HTF candle
   ▼
 COMPUTE                 for i = 5 to 0: computeSignalState → ProcessingResult[6]
   │                     (latches mutate TimeframeData; classification + decision logic)
   ▼
 FILTER                  Lead Signal anchor scan; mutates ProcessingResult flags only
   │
   ▼
 RENDER  (islast only)   renderSignalLevels → label collection → line suppression
                         → label consolidation → data table + Domino → debug panel
   │
   ▼
 ALERTS                  edge detection on persisted was-state arrays
   │
   ▼
 PAINT  (every bar)      chart-bar Strat classification / FTFC re-grade → barcolor()
```

Two cadences run through everything (`../DESIGN_CONSTRAINTS.md` item 5):

- **State tracking runs on every bar.** The compute loop executes for all of history so `var` latches (crossed flags, sticky stops, period bookkeeping) rebuild identically on reload.
- **Rendering runs only on `barstate.islast`.** No drawing object is ever created on a historical bar — there is no historical signal trail to repaint (`repaint-prevention.md`). The one stated exception is Stage 7's bar coloring, which paints (but never draws objects on) historical candles under the repaint doc's reload-stability rule.

The heavy classification work inside `computeSignalState` is additionally gated `barstate.islast or showStopLevels` (`FIX P1-a`): with stops off (the default) history pays only for the latches; with stops on, detection runs every bar because the sticky-stop lock must be rebuildable from history.

---

## Source layout

The file is organized into grep-able `SECTION` banners. Mapping to pipeline stages:

| Banner | Contents | Stage |
|---|---|---|
| `SECTION 1` / `2` / `2A` | drawing helpers, inputs, preset resolution | inputs |
| `SECTION 3` / `3B` | UDT definitions, `suppress*Flags` methods | (types) |
| `SECTION 4` | `calcMetrics`, hammer/shooter detectors, `findExhaustionLevels`, `calcBarType`, `getDataWithExhaustion`, `calculateFTFC` | security layer |
| `SECTION 5` | `detectFailed2`, `shouldDrawC1Level` and wrappers | compute |
| `SECTION 6` | slot arrays, the six security calls, `applyPreviewShift`, reconstruction | security + shift |
| `SECTION 7` | `computeSignalState`, `renderSignalLevels` | compute + render |
| `SECTION 8` | label pool, `collectTimeframeLabels`, `consolidateAndCreate` | render |
| `SECTION 9` | the 6-slot processing loop, Lead Signal filter, render phase | compute + filter + render |
| `SECTION 10` | `collectLinesFromTF`, `suppressLowerTFLines` | render |
| `SECTION 11` | data table, Domino calculation | render |
| `SECTION 12` | debug panel | render |
| `SECTION 13` | was-state arrays, `alert()`, `alertcondition`s | alerts |
| `SECTION 14` | `getChartBarColor`, `paintSlotFailed2`, FTFC re-grade, `barcolor()` | paint |

---

## Stage 1 — Security layer: six slots, one tuple call each

Everything is built around **six timeframe slots**. Inputs resolve to `rTF1`–`rTF6` / `rEN1`–`rEN6` (a preset overrides the manual rows, which grey out via `active` — `FIX MOD-3`), packed into `tfConfigs` (`TFConfig`: enabled, tf string, line width, show-open, debug label).

Each enabled slot costs exactly **one** `request.security` call — `getDataWithExhaustion()` with `lookahead = barmerge.lookahead_on` — returning a 23-value tuple:

- **CC** (forming bar, offset `[0]`): time/OHLC — live-updating by design.
- **C1** (offset `[1]`) and **C2** (`[2]`): time/OHLC — settled, non-repainting.
- **C3**/**C4** (`[3]`/`[4]`): high/low only — enough to classify C2 and C3's bar numbers for the combo labels and the table's C2 cell.
- **Exhaustion pivots**: computed *inside* the HTF security context by `findExhaustionLevels` (a pivot scan seeded from completed bars — `FIX P0-2`), because the scan needs HTF bar history, not chart-bar history.

Why a tuple and not something nicer: `request.security` cannot return arrays, so the six calls are unrolled, and `calculateFTFC` takes all eighteen open/close/enabled values as flat parameters (`../DESIGN_CONSTRAINTS.md` item 4 — do not propose array-based returns).

Each call is budgeted with a tiered `calc_bars_count` (`cb1`–`cb6`): coarser slots request fewer bars, with a floor of 100 so the exhaustion scan's history requirement is always met. The tiers are inlined ternaries because the value must be a `simple int` — a function return would be `series` and rejected.

One more request sits beside the six: `request.security_lower_tf(syminfo.tickerid, "D", …)` fetches the current chart bar's completed **daily sub-bars**, gated by the constant `canRecon` (chart above D). It exists solely for straddle reconstruction (Stage 2).

The offset-`[0]`-vs-`[1]` semantics under `lookahead_on` — what repaints, what doesn't, and why signals anchor to C1/C2 — are the subject of `repaint-prevention.md` Rules 1–2. The chart-bar→HTF-bar mapping and its failure modes are `htf-correctness.md`.

---

## Stage 2 — Shift layer: preview, straddle, reconstruction

Raw tuples never reach the engine directly. `applyPreviewShift` normalizes each slot into a `TFRawData` value and stores it in `tfRawArr`. In the normal case this is a straight repack. In two cases it **promotes the chain** — served CC becomes C1, C1 becomes C2, and so on — and synthesizes a neutral placeholder CC:

- **Preview** (`shouldApplyPreview`): market closed, new calendar period begun; traders see next period's levels before the open.
- **Straddle** (`isStraddleTF` + `shouldStraddle`): a calendar TF rolled mid-chart-bar, so the served data is one period stale during live trading (`FIX HTF-STRADDLE-1`, `FIX GLUE-2b`).

Either way the slot is flagged `isPreview = true`, and `realPeriodTime` preserves the *unshifted* served period-open — the one identity that survives the rewrite. Everything downstream that must detect a genuine period change keys off `realPeriodTime`, never off shifted fields (`FIX P0-1`, `FIX P1-c`).

A follow-up pass (islast only) replaces the synthetic placeholder CC for straddled slots with the **true forming HTF candle**, rebuilt from the daily sub-bars whose `pkeyCal(+12h)` matches the current period, plus the live close. Success is recorded per slot in `reconOKArr`; the slot stays `isPreview = true` regardless — reconstructed data renders but never alerts. Full rules and the bug history: `htf-correctness.md` Rules 3–5.

---

## Stage 3 — Compute: `computeSignalState`, once per slot

The main loop (`SECTION 9`) iterates `for i = 5 to 0` — highest slot first — calling `computeSignalState(data, tf, raw, …)` with the slot's persistent `TimeframeData` and this bar's `TFRawData`. It runs on **every** bar. Inside, in order:

1. **New-period detection and latch.** `raw.realPeriodTime != data.lastPeriodStart` (plus the two preview re-latch triggers, `FIX P1-d`) resets all period-scoped state and snapshots C1/C2/exhaustion into `data.prevHigh/prevLow/prevMag*/exh*` (`repaint-prevention.md` Rule 4 has the full state inventory). This runs unconditionally, even for a slot the next step refuses.
2. **Refusal gate.** `validTimeframe = timeframe.in_seconds() <= timeframe.in_seconds(tf)` — a slot below the chart TF skips everything after the latch and returns an empty result (`htf-correctness.md` Rule 1).
3. **Monotone latches.** `magHighCrossed`, `exhLowCrossed`, etc. latch from CC extremes — safe because they are monotone predicates (`repaint-prevention.md` Rule 2).
4. **Classification.** `detectPatterns` (hammer/shooter), `isInsideBar`, `detectFailed2` (CC type + F2 + pre-F2), `calcBarType`/`calcBarNum` for the C-chain, `detectBarTypeAndFailed` for "C1/C2 was itself an F2".
5. **Decision logic.** The centralized `shouldDrawC1High`/`shouldDrawC1Low` (both thin wrappers over one `shouldDrawC1Level` — one decision, both directions), a bank of named suppression predicates, the `signal_in_force_high/low` computation (`FIX CSS-1`), magnitude/exhaustion draw decisions (`shouldDrawMagnitude` plus forced OB/3-exp paths, `FIX CSS-2`), and level colors/styles (`NOTATION-1` governs label text).
6. **Sticky stops.** First in-force latches `stopHighTriggered/Price` per the stop-reference mode; break-even logic may ratchet the price to the trigger (`FIX P1-a` explains why this runs every bar when stops are on).

The output is a `ProcessingResult`: prices, colors, draw flags, in-force flags, classification strings. **It contains decisions, not drawings** — nothing has touched the chart yet. Results land in `tfResults`.

---

## Stage 4 — Filter: mutate flags, never draw

The Lead Signal filter (islast only, when enabled) scans slots top-down with `getSignalDirection` (F2 direction resolved explicitly first — `FIX LEAD-F2-TIEBREAK-2`), takes the first in-force slot that is not a preview/straddled slot as the anchor (`FIX LEAD-PREVIEW-1`), then walks the *lower* slots and mutates their `ProcessingResult` flags: `suppressHighFlags`/`suppressLowFlags`/`suppressAllFlags`, plus clearing `signalInForceHigh/Low` so the table and Lead row agree with the hidden lines (`FIX P1-g`).

This is the pattern every filter must follow: **filters edit the decision record; only the render phase reads it.** Because lines, labels, the table, and alerts all consume the same flags, one mutation stays consistent across every surface (`../DESIGN_CONSTRAINTS.md` item 3 — flag any path where lines and labels can diverge).

---

## Stage 5 — Render: islast only, update-in-place

All drawing happens under `barstate.islast`, in source order:

1. **`renderSignalLevels`** per slot: every level follows the same shape — flag true → `updateOrCreateLine` (create once, then move via setters); flag false → `deleteLine`. Conditional creation, never create-then-hide (`../DESIGN_CONSTRAINTS.md` item 2). Take-action-window boxes are rebuilt here too (`FIX TAW-1`: each direction checks only its own trigger).
2. **Label collection**: `collectTimeframeLabels` pushes price/text/color into the `SimpleLabelData` buffers (`highLabels`/`lowLabels`/`openLabels`), applying the same directional-filter suppression the lines got.
3. **Line suppression** (`SECTION 10`): `collectLinesFromTF` gathers live lines as `LineInfo` (price, tfSeconds, owner slot, kind); `suppressLowerTFLines` keeps only the highest-TF line at each rounded price, deleting losers and nulling the owner's field via `nullSuppressedLine` so the render phase recreates it cleanly if the collision clears (`FIX P1-h`). Stop lines are deliberately exempt — risk management stays visible.
4. **Label consolidation**: `consolidateAndCreate` merges same-price labels (`"W + D"` style) and materializes them through the label pool — update-in-place, surplus trimmed after render (`FIX P1-i`).
5. **Data table + Domino** (`SECTION 11`): `populateBarTypeRow` classifies via `detectBarTypeAndFailed` — a deliberately separate detection path from the engine's `detectFailed2` (`../DESIGN_CONSTRAINTS.md` item 7; logical disagreement is a bug, duplication is not). The Domino run (consecutive inside-CC slots, deduped by `ccStartTime`) is computed just before the table and shared with alerts.
6. **Debug panel** (`SECTION 12`): renders the `DebugInfo` snapshot the compute stage populated for the selected slot.

---

## Stage 6 — Alerts: edges of persisted state

`SECTION 13` never asks "is it active?" — only "did it just become active?". Per-slot `was*` arrays reset on `realPeriodTime` change (`FIX P1-c`), update unconditionally even when a TF's alert checkbox is off (`FIX U2`), and are snapshotted before the update loop so the per-TF `alertcondition`s see true edges. Preview/straddled slots never alert (`slotPreview`, `okA1`–`okA6`). The consolidated `alert()` fires once per bar with the assembled message. The full rule set is `repaint-prevention.md` Rule 6.

---

## Stage 7 — Bar coloring: every bar, no drawing objects

`SECTION 14` (`BARCOLOR-1`, off by default) runs in the main context on **every** bar — `barcolor()` paints history, so its classification cannot be islast-gated. Strat Candles mode reuses `detectBarTypeAndFailed` — the data table's CC classifier — on the chart bar itself, so candle paint and table notation cannot disagree. FTFC Candles mode re-runs `calculateFTFC` with the chart bar's own close against the served HTF opens (never the lookahead-final HTF closes), and `paintSlotFailed2` rebuilds each slot's intra-period range from chart bars for the flip highlight. No lines, labels, or boxes are involved, so the object budget is untouched. This is the one stated exception to "rendering runs only on islast," and it is safe because it obeys `repaint-prevention.md` Rule 8: historical paint reads only series that evaluate identically live and on reload.

`SECTION 15` (`BARNUM-1`, off by default) is the second exception, on the same terms. Bar Numbers reads `chartBarType`, the classification SECTION 14 now computes once for either consumer. It draws one `plotshape` per token, because plotshape text must be a const string, with an up-side and a down-side call where a token's side depends on the bar. There are 11 calls, and no labels, so `max_labels_count` stays with the levels. History reads only the chart bar's OHLC and the prior bar's high/low (Rule 8). The forming bar is lightened until `barstate.isconfirmed`.

---

## The major state arrays

Six-element arrays, indexed by slot, carry everything across stages:

| Array | Element type | Lifetime | Role |
|---|---|---|---|
| `tfConfigs` | `TFConfig` | init once (`barstate.isfirst`) | resolved per-slot settings |
| `tfDataArr` | `TimeframeData` | persistent (`var`), mutated by compute + render | the slot's memory: latched C1/C2/exhaustion snapshots, crossed flags, sticky stops, period bookkeeping — plus all drawing handles (lines/boxes) |
| `tfRawArr` | `TFRawData` | rebuilt every bar | this bar's shift-layer output: the C-chain the engine actually sees, `isPreview`, `realPeriodTime` |
| `tfResults` | `ProcessingResult` | rebuilt every bar | this bar's decisions: prices, colors, draw/in-force flags, classification strings |
| `reconOKArr` | `bool` | rebuilt every islast | straddle reconstruction succeeded this tick |
| `wasSetupBull/Bear`, `wasF2Bull/Bear`, `wasPotentialBull/Bear`, `alertLastPeriodStart` | `bool`/`int` | persistent (`var`) | alert edge-detection memory |

The one-way flow is the thing to preserve: **`TFRawData` (per-bar input) → `TimeframeData` (persistent memory) → `ProcessingResult` (per-bar decisions) → drawings.** Filters touch only `ProcessingResult`. Render touches only drawing handles. Nothing downstream writes back into `TFRawData`.

Render-side scratch (label buffers, `LineInfo` arrays, the label pool, the suppression maps) is `var`-allocated once and cleared each islast tick — reuse over reallocation throughout.

---

## Where each invariant is enforced

The conscious decisions in `../DESIGN_CONSTRAINTS.md`, mapped to the code that enforces them:

| Invariant | Enforced at |
|---|---|
| No-repaint contract (`lookahead_on` + `[1]` offsets; live `[0]` intentional) | the six security calls; latch discipline in `computeSignalState` and `findExhaustionLevels` (`FIX P0-2`); drawing only on islast — `repaint-prevention.md` |
| Conditional creation over create-then-hide | `renderSignalLevels` via `updateOrCreateLine`/`deleteLine`; `updateOrCreateBox`/`deleteBox` |
| Centralized decision logic; lines and labels cannot diverge | `shouldDrawC1Level` single decision; `ProcessingResult` flags consumed by render, labels, table, and alerts alike; filter mutations via `suppress*Flags` (`FIX P1-g`) |
| One security call per TF; unrolled FTFC | `getDataWithExhaustion` tuple; `calculateFTFC` flat-parameter chain |
| State every bar, render only islast | compute loop ungated; every drawing block gated `barstate.islast`; `FIX P1-a` for the stop-latch exception |
| UDT-per-timeframe, 6-slot loop | the `tfDataArr`/`tfRawArr`/`tfResults` triple and `for i = 5 to 0` |
| Table detection is a separate path (agreement required, not unification) | `detectBarTypeAndFailed` vs `detectFailed2` |
| Stop lines exempt from suppression | `collectLinesFromTF` (stops never collected) |

---

## Contributor checklist

Before merging anything that adds a feature, a level, or a filter:

1. Does new per-slot data flow follow the one-way chain — security tuple → `TFRawData` → `TimeframeData`/`ProcessingResult` → render? Anything writing upstream, or drawing outside the islast render phase, is wrong.
2. Does a new *display* filter mutate `ProcessingResult` **draw flags** rather than deleting drawings directly — and leave `signalInForceHigh/Low` alone, so the data table keeps reporting market truth (`FIX LEAD-TABLE-1`), so lines, labels, table, and alerts stay consistent (`FIX P1-g`)?
3. Does a new level follow the render pattern — flag-driven `updateOrCreateLine`/`deleteLine`, registered in `collectLinesFromTF` with a new kind and handled in `nullSuppressedLine` (`FIX P1-h`) — and should it be exempt from suppression like stops?
4. Does new period-scoped or latched state live in `TimeframeData`, reset on the `realPeriodTime` key, and rebuild from history on reload (`FIX P0-1`, `FIX P1-a`, `repaint-prevention.md` checklist)?
5. Does anything added to the security tuple keep the call count at one per slot, and is every new consumer of `[0]` data a monotone latch, not a snapshot (`FIX P0-2`)?
6. If the change touches preview, straddle, or calendar logic — run the `htf-correctness.md` checklist too.
