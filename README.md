# TheStrat Suite

Free, open-source multi-timeframe price-action indicator for TradingView (Pine Script v6), implementing Rob Smith's TheStrat methodology. Built and maintained by **345 Strategies**; sibling tools and research tracks live in this repo alongside it.

**What counts as "TheStrat Suite":** whatever ships inside the one TheStrat Suite indicator script on TradingView. Tools that ship as their own scripts are siblings under this roof with their own names — if one later merges into the indicator, it joins the Suite. Distribution decides, not methodology.

## The indicator

Multi-timeframe price action indicator (Pine Script v6), implementing Rob Smith's TheStrat methodology. Six configurable timeframes on one chart: bar classification, signal detection with magnitude and exhaustion targets, Full Timeframe Continuity, stop levels, Domino detection, consolidated alerts, and a live multi-timeframe data table.

**Get it on TradingView:** [TheStrat Suite [Open Source] — Entries, Targets, and Stop Loss](https://www.tradingview.com/script/dnJOzGmk-TheStrat-Suite-Open-Source-Entries-Targets-and-Stop-Loss/)

Setup guides and background: [thestratsuite.com](https://thestratsuite.com)

### Quick start

1. Add [the indicator](https://www.tradingview.com/script/dnJOzGmk-TheStrat-Suite-Open-Source-Entries-Targets-and-Stop-Loss/) to your chart on TradingView — or copy `pine/TheStratSuite_v3.1.1.pine` into the Pine editor (it imports TheStratGrammar, published on TradingView).
2. Pick a Timeframe Preset (TheStrat Classic, Scalp, Day Trade, Futures/Crypto, Swing Trade, Investing) — or set Custom and configure the six slots yourself.
3. Read `docs/concepts/bar-types.md` and `docs/concepts/signals.md` to learn what you're looking at.

## Repository contents

### `pine/` — indicator source (newest first)

- **`TheStratSuite_v3.1.1.pine`** — current build. Preview-path bundle: PREVIEW-WEEKCLOSE-1 and
  PREVIEW-CALCLOSE-1 (calendar slots preview by scheduled close, so Friday pre-market keeps the
  forming week), AUTO-SESSION-1 (Auto never arms while the chart's bar is open; holidays and
  early closes arm it), LEAD-PREVIEW-1, DOMINO-PREVIEW-1, OPENLINE-PREVIEW-1 (preview slots
  cannot lead, domino, or hide an unshifted slot's open line), LINEEND-CLOSE-1 (lines reach the
  end of holiday-glued bars).
- `TheStratSuite_v3.1.0.pine` — published 2026-08-25. GRAMMAR-LIB-1 (classification delegated
  to TheStratGrammar), CONT22-PRIOR-1, DEBUG-TERMS-1, PERF-FTFC-1, PERF-ALLOC-3, LABELTEXT-1.
- `TheStratSuite_v3.0.1.pine` — TABLE-COMPACT-COLOR-1 (Compact table cells colored by signal
  state or per-timeframe continuity), LEAD-TABLE-1, abbreviated Universal labels.
- `TheStratSuite_v3.0.0.pine` — the first open-source publication on TradingView. BARCOLOR-1
  (bar coloring by Strat classification or FTFC) and STOP-SMALLEST-1 (Smallest Timeframe Only
  stop display).
- `TheStratSuite_v2.2.7-split.pine` — NOTATION-1: canonical uppercase F2u/F2d
  (lowercase live-'f' retired; liveness = slot position / '*' prefix / words).
- `TheStratSuite_v2.2.6-split.pine` — RECON-KEY-1 (+12h key normalization for daily
  reconstruction) and TFCOLOR-1 (in-force column no longer highlights inside bars).
- `TheStratSuite_v2.2.4-split.pine` — GLUE milestone: HTF-straddle fix line plus GLUE-1 / GLUE-2b
  (CME holiday-glued-bar handling).
- `TheStratSuite_v2.2.2-split.pine` — HTF-straddle T1 period shift + T3 daily reconstruction.
- `TheStratSuite_v2.2.0.pine` — pre-split baseline.
- `TheStratSuite_v180_June16_2026.pine` — June 2026 pre-audit original.
- `research/` — Volume Profile research fixtures and diagnostics (MPL-headered; not part of the
  shipped indicator). See `pine/research/README.md`.

### `grammar/` — the Strat grammar (Python)

Technical definitions of the price action states themselves: the four-field model every
candle reduces to, and the edge cases that make two implementations disagree. `SPEC.md` is
the definition; `thestrat_grammar/` is a dependency-free Python reference implementation.
`tests/test_pine_parity.py` transcribes `detectBarTypeAndFailed` from the shipped Pine and
compares the two across 40,000 random bars in both detection methods, so the spec can't
drift from the indicator.
`TheStratGrammar.pine` is the same grammar as a TradingView library; the Suite imports it as of
v3.1.1 (`import SpinTrades/TheStratGrammar/1`). Published versions: `PUBLISHED.md`.

### `ninjatrader/` — TheStrat Suite for NinjaTrader 8 (C#)

A full port of v3.1.1 as a NinjaScript indicator, free under MPL-2.0. Import
`ninjatrader/dist/TheStratSuite_NT8_v0.1.0.zip` in NinjaTrader. `ninjatrader/README.md` covers
install, where it differs from the Pine, and the in-app checks still to do.
`grammar/tests/test_nt_parity.py` checks its engine against the grammar.

### `pine-draw/` — drawing components (Pine v6)

The rendering layer with everything Strat-specific removed: update-in-place lines and
boxes, a label pool, and price-based label consolidation. Published as a Pine library for
anyone building their own script.

### `cross-levels/` — ES ↔ SPY/SPX level translation (Pine v6)

A standalone overlay that converts price levels between ES futures and the cash market
through a live basis: killzone session highs and lows, opening prices, prior day/week/month
levels, and SPY overnight gap boxes on ES. Built on the `pine-draw/` library. Not published
on TradingView. See `cross-levels/README.md`.

### `invalidation-stop/` — always-in Classic invalidation stop (Pine v6)

A standalone overlay that keeps one stop line on the chart at all times: the nearest
completed 30m/60m/day structure that should hold if the current move is real. A touch flips
it to the other side; a session reset re-seeds it at the open, at the gap-fill level after a
gap. Forked from the Classic path of my invalidation stop work. Not published on
TradingView. See `invalidation-stop/README.md`.

### `docs/`

Start at `docs/README.md` (the index). Trader-facing docs live in `docs/concepts/`, engineering docs in `docs/engineering/`. Highlights:

- `concepts/bar-types.md` — bar structures, notation rules, Failing 2s, hammers/shooters.
- `concepts/signals.md` — the seven signal types, defaults, filters, in-force mechanics.
- `engineering/repaint-prevention.md` — the no-repaint contract: 7 rules + contributor checklist.
- `engineering/htf-correctness.md` — multi-timeframe correctness: straddle/glue handling, +12h
  normalization, daily reconstruction, preview mode.
- `TheStratSuite_v2.2.7_Settings_Reference.md` — full settings reference, every input in panel order.
- `DESIGN_CONSTRAINTS.md` — intentional design decisions reviewers should not flag as bugs.
- `v2.2.3_holiday_glue_fix.md` — postmortem of the CME holiday-glue incident.

Version history lives in the root `CHANGELOG.md` — one entry per released `.pine` snapshot, every fix cited by its `FIX` tag.

### `diagnostics/`

Not part of the shipped indicator. Two kinds of material: throwaway probe scripts from the
HTF-straddle investigation (retained because they document how the straddle behavior was
measured), and `volume-profile-phase0/` — the offline evidence-gate tooling for the Volume
Profile research track (four Python `vpx` tools with unit tests, schemas, and worked examples;
see its README).

## Engineering ground rules

- **No-repaint is a hard, zero-tolerance requirement.** What you saw live is what a reload shows. The rules are documented, with case studies, in `docs/engineering/repaint-prevention.md`.
- Fix history is inline via `// FIX <id>` comments (P0/P1/P2, BTC, CSS, TAW, MOD, HTF-STRADDLE, GLUE, RECON-KEY, TFCOLOR, NOTATION). Every tag marks a solved bug and the rule that came out of it — grep for them.
- Contributions: start with `CONTRIBUTING.md` (required reading, `FIX`-tag conventions, manual verification workflow), and pass the contributor checklists at the end of the engineering docs before review.

## License

Mozilla Public License 2.0 — see `LICENSE`. Source files carry the standard MPL header.
