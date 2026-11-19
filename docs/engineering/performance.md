# Performance

Where the Suite spends its execution budget, the bounds designed into each cost center, and the gates that keep expensive work off the hot paths.

Written against `pine/TheStratSuite_v3.2.0.pine`. Code references are function names and `FIX` tags; grep the source for them. Repaint rules decide *when* work is allowed to run — `repaint-prevention.md` — and this doc assumes them.

---

## The cost model

A Pine indicator pays in four places, and the Suite's architecture assigns each one a strategy:

1. **Security contexts.** Every `request.security` call replays its expression over the requested timeframe's history in a separate execution context. Cost is per *call*, not per returned value. Strategy: few calls, fat tuples, bounded depth.
2. **Per-bar main-context work.** Runs once for every chart bar at load, then on every realtime tick. Strategy: keep the every-bar path to state maintenance only.
3. **Last-bar work.** Runs once per tick, never across history. Strategy: everything visual lives here.
4. **Drawing-object churn.** Creating and deleting lines, labels, boxes every tick causes flicker and garbage-collection pressure. Strategy: persistent references, update in place.

The one-sentence version, codified in `../DESIGN_CONSTRAINTS.md`: **state tracking runs every bar; rendering runs only on `barstate.islast`; objects are conditionally created, never created-then-hidden.**

---

## One security call per slot — tuples, because calls are the unit of cost

Each of the six timeframe slots makes exactly one `request.security` call, and each call returns the 23-value tuple from `getDataWithExhaustion()`: the forming bar (CC), completed bars C1–C4 (time and OHLC for C1 and C2; only high/low for C3 and C4), and the two exhaustion pivots.

One more request exists: `request.security_lower_tf` fetching completed daily sub-bars for straddled-slot reconstruction (`FIX HTF-STRADDLE-1`, T3). It sits behind the constant gate `canRecon` (chart timeframe above daily), so it either always executes or never does — both legal under Pine v6 dynamic-request rules, and charts at or below daily never pay for it.

Total: **at most 7 of TradingView's 40-call request budget.**

Why tuples and not more calls: adding a field to the tuple is nearly free — one more series copied out of a context that already exists. Adding a call spins up an entire new context that replays HTF history. The tuple is flat (23 positional values, no arrays) because arrays cannot pass through `request.security`; the same constraint is why `calculateFTFC` takes an unrolled 18-parameter per-TF signature instead of arrays. Both are deliberate — see `../DESIGN_CONSTRAINTS.md`.

The rule for contributions: **need more HTF data? Extend the tuple. Never add a call.**

---

## `calc_bars_count` tiers

Without `calc_bars_count`, each security context computes over the symbol's *entire* history at the requested timeframe. The Suite draws nothing historical, so deep history buys nothing — the only depth that matters is warmup for the C1–C4 offsets and the exhaustion scan's window. Each slot therefore caps its context (`cb1`–`cb6`, tiered by the slot's timeframe string):

| Timeframe family | `calc_bars_count` |
|---|---|
| Monthly and above (`M`, `1M`, `3M`, `6M`, `12M`) | 100 |
| Weekly (`W`, `1W`) | 100 |
| Daily (`D`, `1D`) | 150 |
| 4H–12H (`240`, `480`, `720`) | 200 |
| Finer intraday (everything else) | 250 |

Two constraints shape the numbers:

- **The floor is 100.** The exhaustion scan requires `bar_index > 50` of warmup plus its 100-bar display window (see next section). Lowering any tier below 100 silently kills exhaustion levels on that slot.
- **Finer timeframes get more headroom.** A fine-resolution bar is cheap to compute, and session gaps and holidays consume proportionally more bars at intraday resolutions; coarse timeframes need nothing beyond the floor (100 monthly bars is over eight years).

Implementation note, and the reason the six tier expressions are copy-pasted ternaries rather than a helper: `calc_bars_count` demands a `simple int`, and a Pine function's return is a series — `request.security` rejects it. The inline form is ugly on purpose. Grep `cb1` for the comment.

---

## Inside the security context: the exhaustion scan is triple-gated

`findExhaustionLevels` is the only loop the Suite ships into its security contexts, so it carries three gates:

1. `showAnyTargets` — targets disabled means no scan runs anywhere, on any bar.
2. `bar_index > last_bar_index - 100` — only the most recent 100 HTF bars scan. Historical exhaustion is never displayed, so every older bar returns `na` immediately.
3. `bar_index > 50` — warmup, guaranteeing the 49-offset lookback never underflows.

The scan itself is `for i = 2 to 48` with an early `break` once both pivots are found — a designed worst case of 100 bars × ~47 iterations of comparisons per context, and typically far less. (The `i = 2` start and `[1]` seed are repaint constraints, not perf ones — `FIX P0-2` in `repaint-prevention.md`.)

Everything else in the tuple is raw series offsets (`high[1]`, `time[2]`, …): free.

---

## Every bar vs last bar: the two-speed engine

The main context runs at two speeds. **Every-bar** work exists only where reload-rebuildability demands it (`repaint-prevention.md`, Rule 5):

- Tuple unpack into `TFRawData`, per-slot new-period detection, and the monotone crossed-flag latches — the head of `computeSignalState` (`FIX P0-1`).
- Alert was-state maintenance (`FIX P1-c`, `FIX U2`) — boolean array writes. The expensive part, message assembly via `buildAlertLabel`, runs only when an edge actually fires, and its detail string is built lazily field-by-field (`FIX P1-j`).
- Conditionally: full C1 pattern and F2 detection when `showStopLevels` is on (`FIX P1-a`). The gate is `barstate.islast or showStopLevels` — the sticky-stop latch must have run on the bars where the signal fired or a reload loses the lock, so the every-bar cost is paid, but only behind the toggle. Stops off (the default) keeps the cheap `islast`-only path.
- Conditionally: bar coloring (`BARCOLOR-1`, off by default) — `barcolor()` paints history, so its classification must run on every bar. Strat mode costs one `detectBarTypeAndFailed` call per chart bar; FTFC mode one `calculateFTFC` re-run plus, with flip highlighting on, six `paintSlotFailed2` range updates.

**Last-bar-only** work is everything else:

- Daily-sub-bar reconstruction for straddled slots (`FIX HTF-STRADDLE-1`, T3) — 6 slots × the dailies inside the current chart bar (≤ ~23 even on a monthly chart).
- The directional-filter anchor scan, `renderSignalLevels`, take-action window boxes.
- Label collection, consolidation, and pool render; line collection and `suppressLowerTFLines`.
- The data table, the debug panel, and all `timenow` / market-closed logic (Rule 7 of the repaint doc).

The two docs meet in one rule, read from opposite sides. Repaint says: a latch that must survive reload cannot be `islast`-only. Performance says: **new work defaults to `islast`-only, and widening a gate to every-bar is a correctness decision that must be justified and, where possible, paid for behind a feature toggle** — exactly the `FIX P1-a` pattern.

---

## Drawing objects: update in place, delete only on state change

One principle — a drawing object's lifetime matches its level's lifetime, not a tick. The cost claims:

- **Lines and boxes** hold persistent references, mutated via setters on last-bar ticks; deletion happens only when a level stops qualifying.
- **Labels are pooled** (`FIX P1-i`): update-in-place, surplus trimmed after render. The pool replaced a delete-all-and-recreate-every-tick pattern — the single largest per-tick object churn removed in the 2.2.x line.
- **Line suppression is the one sanctioned delete path** (`FIX P1-h`): a suppressed line is deleted and its owner's field nulled so the next tick recreates it cleanly.

No drawing object is ever created on a historical bar, so the live population is bounded by enabled slots × level types — order tens against the declared 200-per-type caps (boundaries table below). Mechanics, the budget inventory, and the P1-h/P1-i case studies: `rendering.md`.

---

## Tables: allocate once, write cells only on the last bar

`barTypeTable` and `debugTable` are `var` references created exactly once, on `barstate.isfirst` — the bar-type table with dimensions frozen from `enabledTFCount` and the table mode, the debug panel at a fixed size. No runtime resize path exists or is needed — changing any input re-instantiates the script from bar zero, so "once" means once per instantiation.

Every `table.cell` write sits under a `barstate.islast` gate. A Pine table only ever displays its last-bar state, so a cell write on a historical bar is pure waste; the gate makes the table's cost across history exactly zero and its per-tick cost a few dozen cell writes (`populateBarTypeRow` and the compact-mode path).

---

## Designed boundaries

| Boundary | Value | Where |
|---|---|---|
| `request.*` calls | ≤ 7 (6 slots + conditional daily recon) of 40 | `getDataWithExhaustion`, `canRecon` |
| Security-context depth | 100–250 bars per slot | `cb1`–`cb6` tiers |
| Exhaustion scan | last 100 HTF bars × ≤ 47 iterations, early break | `findExhaustionLevels` |
| Straddle reconstruction | last bar only; 6 slots × ≤ ~23 dailies | `FIX HTF-STRADDLE-1` (T3) |
| Drawing objects | 200 per type declared; live population order tens | `indicator()` declaration |
| Bar coloring | constant per-bar work; no `request.*` calls, no drawing objects (`barcolor` sits outside the 200-object caps) | `SECTION 14` |
| Main-context history | `max_bars_back = 200` | `indicator()` declaration |

These are *designed* bounds, not benchmark results — the repo records no Pine Profiler measurements. A contribution claiming a performance win should come with before/after evidence from TradingView's Pine Profiler, not reasoning alone.

---

## Contributor checklist

Before merging anything that requests data, loops, draws, or writes tables:

1. Does new HTF data extend the existing tuple, or add a `request.*` call? Extend the tuple — the call count is the budget (grep `getDataWithExhaustion`).
2. Does new code loop inside a security context? Gate it like `findExhaustionLevels`: feature toggle, recent-bars window, warmup — and keep every `cb` tier at or above the 100-bar floor the scan requires.
3. Must new per-bar work really run on historical bars for reload rebuild (`repaint-prevention.md`, Rule 5)? If not, gate on `barstate.islast`; if yes, put the cost behind its feature toggle like `FIX P1-a`.
4. Do new drawing objects hold persistent refs updated via setters (`updateOrCreateLine`, `acquireLabel`), deleting only on state change — and does every delete path null the owning field (`FIX P1-h`)?
5. Are new tables allocated once on `barstate.isfirst` and their cells written only under `barstate.islast`?
6. If you touched `cb1`–`cb6`: still `simple int`, still inline (no function wrapper — `request.security` rejects series), still ≥ 100?
