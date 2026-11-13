# Reading the Chart

Everything the Suite draws — labels, lines, the data table, alert messages — is a rendering of the same underlying state. This doc is the decoder: what each piece of text means, what the colors say, and how to read the table row by row. Notation itself (what `2u` or `F2d` *means*) is defined in `bar-types.md`; signal semantics live in `signals.md`. This doc assumes both.

Applies to v2.2.7-split and later. Code references are function names and `FIX` tags; grep the source for them.

---

## Label anatomy

Every level label is assembled from the same ordered parts (`collectTimeframeLabels`). A fully-loaded example:

```
1H *2d-1-2u HAM H 542.10
```

| Part | Example | Meaning | Always shown? |
|---|---|---|---|
| Timeframe | `1H` | Which monitored timeframe owns this level (`getTimeframeLabel`: `15m`, `30m`, `1H`, `D`, `W`, `M`, …) | Always |
| Combo / word | `*2d-1-2u` | The setup — or a level word (`MAG`, `EXH`, `OPEN`, `STOP`) | Always |
| Pattern suffix | ` HAM` / ` SHO` | C1 is a hammer/shooter. Universal mode uses `◆` instead | When detected |
| Side | ` H` / ` L` | High-side or low-side level | Only with **Show H/L in Labels** on |
| ` + STOP` | | The stop sits at break-even — exactly on this trigger — so its label merged in | When stop = trigger price |
| Price | `542.10` | The level's price | Only with **Show Price in Label** on |

Two more assembly rules:

- **Same price, one label.** Any labels landing on the same tick-rounded price merge, joined with ` + ` (`consolidateAndCreate`). `1H MAG + 15m 2u-2u` is one price where the hourly magnitude and a 15m trigger coincide — confluence, made legible. Parts join in collection order, TF slot 6 down to slot 1 — with the standard presets, that's highest timeframe first.
- **Two placements.** *Timeline labels* (default on) sit at each line's end. *Floating labels* (default off) hold a fixed offset from the current bar so they never scroll away. Same text either way.

## The level vocabulary

Six kinds of level, six words:

| Label text | Universal mode | The level | Line style |
|---|---|---|---|
| combo (`*2d-1-2u`, `2u-F2u`, …) | words (`↑REV`, …) | Trigger — C1's high or low | Dashed (solid when the structure itself is the event: confirmed F2 reclaim side, committed outside bar) |
| `MAG` | `TARGET` | Magnitude — first target, C2's high/low | Solid |
| `EXH` | `FINAL` | Exhaustion — extended target | Solid |
| `OPEN` | `OPEN` | The current period's open | Dotted, gray |
| `STOP` | `STOP` | Protective stop (drawn on the opposite side of the trigger) | Solid |
| `*F2d` / `*F2u`, then `F2d` / `F2u` | `OPEN` | The CC open during a Failing 2 — the reclaim reference level | Dotted |

The shaded boxes between trigger and target are **Take Action Windows** — the tradeable zone for an in-force signal, filled with the same bullish/bearish Signal colors as the lines.

---

## Combo strings and the canonical tuple

A combo reads **oldest → newest, and the last token is always the forming candle**: in `2d-1-2u`, the `2u` is happening right now. Full reading rules are in `bar-types.md`; the ones you need constantly:

- **`*` prefix = potential.** `*2d-1-2u` is a setup whose trigger hasn't broken yet. When price crosses, the `*` drops and the line takes the signal color.
- **`F` marks a slot that was a Failing 2.** `F2d-2u` — the prior bar failed its downside break, the current bar is following through up.
- **A leading `1` or `3` names C2 on a 2-2.** `1-2d-2u` is the 1-2-2 RevStrat (C2 inside), `3-2d-2u` the 3-2-2. Same signal and levels as a plain `2d-2u` (the 2-2 Reversals row); the extra token just names the setup (`REVSTRAT-122-1`). Failing 2 labels keep their C1-only form (`2d-F2u`).
- **Dashes are cosmetic.** **Use Dash Separator** (default on) turns `2d12u` into `2d-1-2u` (`formatCombo`). Same string.
- **During a live Failing 2, both sides tell the story.** The failing side reads the F2 combo (`2u-F2u`); the *opposite* trigger reads a potential outside bar (`*2u-3d`) — because a full traversal through the other side would print a 3.

### Casing carries structure, never tense (`NOTATION-1`)

Failing 2s are **always uppercase `F2u`/`F2d`, live or closed**. Earlier versions wrote a forming Failing 2 in lowercase (`f2u`); that was retired in v2.2.7 because case-significant notation can't be spoken, doesn't survive small label sizes, and marked liveness for exactly one bar type. Liveness is carried where every bar type carries it:

1. **Slot position** — the last token of a combo is the forming candle.
2. **`*` prefix** — potential, not yet confirmed (`*F2d` on a pre-F2 open level).
3. **Words** — Universal mode says `FAILING` while live; past tense is failed.

### The tuple is the truth

Every string on the chart is a projection of one four-field tuple per candle — `structure` (1 / 2u / 2d / 3), `above_open`, `failed`, `live` (the full table is in `bar-types.md`). When two displays seem to disagree — a table cell versus a label, a label versus an alert — resolve both back to the tuple before assuming a bug. They cannot actually conflict; they can only be rendering different fields.

## Universal mode

With **Label Style = Universal**, combos become words: `↑REV`, `↓CONT`, `↑INS`, `↑EXP`, `*OUTSIDE`, with `FAILING` appended to the side a live F2 is failing and `◆` replacing ` HAM`/` SHO`. The `*` prefix still means potential. `MAG`/`EXH` become `TARGET`/`FINAL`. Same engine, same triggers, same colors — only the vocabulary changes.

---

## Colors

All configurable under Style — these are the defaults, and each has one job:

| Color | Default | Means |
|---|---|---|
| Green (Signal) | `color.green` | Bullish level, live — trigger broken or in force |
| Red (Signal) | `color.red` | Bearish level, live |
| Yellow | `#ffeb3b` | Inside-bar potential, high side — break not yet known |
| Orange | `#ff9800` | Inside-bar potential, low side |
| Teal | `#089981` | Bullish outside-bar (3) expansion level |
| Pink | `#e91e63` | Bearish outside-bar expansion level |
| Soft green | `#81c784` | Range Reclaim `F2d` — failed downside break, bullish |
| Soft red | `#f77c80` | Range Reclaim `F2u` — failed upside break, bearish |
| Light / dark gray | `#9c9c9c` / `#4a4a4a` | Crossed — a magnitude or exhaustion level already hit, visually receding |

The read at a glance: **yellow/orange = waiting, green/red = live, gray = done.** An inside-bar setup draws both sides (yellow above, orange below) because the break direction isn't known yet; the moment one side breaks, that line goes signal-colored and the `*` drops.

---

## The data table

The multi-timeframe scoreboard, updated on the live bar only. Two modes (**Display → Data Table → Mode**).

### Full mode

One row per enabled timeframe, five columns (`populateBarTypeRow`):

| Column | Contents |
|---|---|
| **TF** | The timeframe label. Its background lights up in the CC bar-type color when a signal is **in force** on that timeframe — a live 2/F2 signal or an enabled outside bar closing beyond `C1` (`OUTSIDE-IF-1`). Inside bars never light it (`FIX TFCOLOR-1`): an inside bar is by definition *not* in force. |
| **C2** | Two bars back, completed — colored text on black |
| **C1** | The prior bar, completed — colored text on black |
| **AS** | Actionable signal on C1: `HAM`, `SHO`, or `INS` (inside), else blank |
| **CC** | The forming bar — black text on a **solid color block**. The one cell that changes as price moves |

The typography is deliberate: completed bars are colored text, the forming bar is a filled cell. Your eye goes to the live column.

Cell text and colors (`getBarTypeColor`, `getBarTypeTextColor`) follow the same scheme as the chart: `1u` yellow / `1d` orange (inside, closed above/below open), `2u` green / `2d` red, `3u`/`3d` and `F2u`/`F2d` in your Style colors for outside bars and Range Reclaims. Two special cases:

- **`?` in the CC cell** — the slot is previewing a period boundary and its forming candle could not be rebuilt from real daily sub-bars, so the Suite shows "unknown" rather than a guess (`FIX HTF-STRADDLE-1`). A `?` slot never fires alerts.
- **Dimmed, empty row** — that timeframe is *lower* than your chart timeframe, so its bars can't be resolved from this chart. Switch to a lower chart TF to activate it.

### The footer rows

Below the timeframe rows, in order, each spanning the table width:

| Row | Appears when | Reads |
|---|---|---|
| **Domino** | Stacked same-direction inside-bar setups ≥ your minimum (white background) | `Domino: 15m+30m+1H` — the coiled timeframes, lowest first |
| **FTFC** | Always | `FTFC Up` (green) / `FTFC Down` (red) / `Conflict` (gray). Universal: `Trend Up` / `Trend Down` / `No Trend`. With **Exhaustion Excludes from FTFC** on, this cell — and only this cell — drops exhausted TFs from the calculation (`FIX P1-e`); signals and alerts always use raw FTFC |
| **Lead** | Lead Signal filter enabled | `Lead: D BULL` — the anchor timeframe and its direction, or `No Lead` |
| **PREVIEW MODE** | Preview is active | Yellow banner — some slots are showing shifted/estimated periods |

### Compact mode

The whole table collapses to one colored chip per timeframe in a horizontal row, plus the same footer rows. Chip colors: **yellow** = inside and above open, **orange** = inside and below open, **green/red** = signal in force in that direction (Range Reclaim and outside-bar signals use their own Style colors), dark with white text = nothing live, **yellow text** = previewed slot with no rebuilt data (the `?` equivalent).

---

## Alert messages

The consolidated `alert()` message is built per signal by `buildAlertLabel`, one label per newly in-force event, joined with ` | `:

```
<TF> <combo><pattern><H/L> <marker> | <detail fields> | <FTFC>
```

A fully-enriched single-signal push:

```
1H 2d-1-2u HAM 🟢 | @ 542.10 MAG 545.30 EXH 548.20 STOP 538.10 | FTFC Up
```

- **`<TF> <combo>`** — same timeframe label and combo notation as the chart labels (Universal mode sends the word forms: `1H ↑REV◆ 🟢`). Failing 2 alerts use the canonical combo (`2u-F2u`), uppercase per `NOTATION-1`.
- **`<marker>`** — 🟢 bullish, 🔴 bearish. Always present; it's the direction read when the combo is ambiguous at a glance.
- **Detail fields** — `@` (trigger price), `MAG`, `EXH`, `STOP`, each opt-in under Alerts and each *omitted entirely* when its value doesn't exist for that signal. The ` | ` separator only appears when at least one field is present — no dangling pipes, no blank `MAG` (`FIX P1-j`).
- **FTFC verdict** — appended once at the end of the whole message when **Include FTFC** is on: `FTFC Up` / `FTFC Down` / `Conflict`.
- **Multiple simultaneous events** chain with ` | `: `15m F2d-2u 🟢 | 1H 2d-1-2u 🟢 | FTFC Up`.

Domino alerts are their own message: `Domino: 15m+30m+1H`.

Two things worth knowing about delivery:

1. **The rich message only travels the `alert()` path** — create one TradingView alert with condition "Any alert() function call". The named `alertcondition()` entries ("Signal In-Force (TF1)", "FTFC Shifted", …) fire with fixed text, a Pine limitation; use them for targeted routing, the consolidated alert for content.
2. **What alerts is what displays.** Alerts obey the same signal toggles as the chart (`FIX P0-3`), fire only on a signal *becoming* in force — not on every bar it stays in force — and never fire from a previewed or straddled slot (`FIX HTF-STRADDLE-1`). If the table shows `?`, that slot is silent.

---

*Every knob referenced here (label placement, table mode, alert enrichment, colors) is documented in `../TheStratSuite_v2.2.7_Settings_Reference.md`. Notation rules and the canonical tuple: `bar-types.md`. What makes a signal fire in the first place: `signals.md`.*
