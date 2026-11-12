# TheStrat Replay

Replays a trading day through TheStrat Suite's logic one minute at a time, for the day replay tool.

**Status: Phases 1 to 3 of the MVP, private.** It builds every timeframe's forming bar minute by minute,
finds each one's C1, labels its bar type and reads FTFC (Phase 1). It also runs the Suite's signal, in-force,
magnitude and exhaustion logic for every timeframe at every minute (Phase 2, `signals.py`). Then it turns
the day into a ranked event feed (Phase 3, `events.py`). The replay page comes next.

## Signals (Phase 2)

`signals.py` is a line-by-line port of `computeSignalState`, `shouldDrawC1Level`, `detectFailed2` and
`findExhaustionLevels` from `pine/TheStratSuite_v3.1.1.pine`, using the Pine's own variable names.
`SignalSettings()` holds the Suite's defaults: inside reversals, 2-2 reversals and inside continuations on,
everything else off, and Reclaim Failing 2 detection. `SignalSettings.all_signals()` turns on every family.

Levels latch per period the way the Suite does. The trigger is C1, magnitude is C2, and exhaustion is the
nearest intact pivot scanned from completed bars at period open. Hit flags only ever turn on within a period.
Exhaustion needs more than 50 completed bars on that timeframe (the Pine's `bar_index > 50` gate). That means
about 4 years of history for M and a year for W.

Not ported: stops and break-even, the Lead filter, Domino, preview and straddle shifts, and line styles. The
`combo` string is the replay's own label and has not been checked against the Suite's label text yet. This port
has not been compared to the Suite on real data yet.

## Why a separate engine

Every Strat-related value the replay page shows comes from here. The page only plays the day file back.
That leaves one implementation to check against the Suite, not two. Bar types come from
[`thestrat_grammar`](../grammar/), which is parity-tested against the Suite's Pine. This package only decides
*which* candles get compared at each minute.

## What it guarantees (tests in `tests/`)

- The forming bar at minute t is the aggregate of the 1m bars from its open through t. Nothing later is used
  (`test_no_lookahead`).
- At a bar's last minute, its bar type equals the grammar's verdict on the completed bar against the prior one.
- Bar types only move forward within a bar: 1 can become anything, 2u only 2u or 3, 3 stays 3.
- The first 1H bar of a session uses the prior session's 15:30 ET bar as C1.
- W and M carry the week's and month's earlier sessions. D, W and M give the same result whether daily history
  comes from the 1m bars or from a separate daily file.

## Sessions

US equities, regular hours, TradingView boundaries. Intraday bars count from 09:30 ET: 1H at 09:30, 10:30, and
so on to 15:30, and 4H at 09:30 and 13:30 (6:30 AM and 10:30 AM PST). W starts Monday and M is the calendar
month. Futures sessions and CME holiday-glued bars are not handled yet.

Above-open is the grammar's rule: a close exactly at the open is not above (SPEC.md section 3). The research
repo's sims use `>=`. The replay follows the Suite.

## Use

```bash
pip install -e grammar/ -e replay/
python -m thestrat_replay spy_1m.parquet 2026-09-17 --out SPY_2026-09-17.json
python -m pytest replay/tests
```

The input is 1m bars with an ET timestamp column `ts` plus open, high, low and close (volume is optional), as
CSV or parquet. Include at least the prior session. For W and M, also pass `--daily` with about two months of
daily bars, or include that much 1m history.

The day file (`schema: thestrat-replay/day/1`) holds one entry per minute: the 1m bar, FTFC, and for each
timeframe the forming OHLC, its C1, and its bar type.

## Events (Phase 3)

`events.py` compares each minute with the one before and emits:

| Type | Fires when | Default |
| --- | --- | --- |
| `signal_in_force` | a timeframe's signal goes in force, once per side per bar | on |
| `signal_lost` | an in-force signal drops back through its trigger | on |
| `failing_2` | a forming bar becomes F2u or F2d, once per bar | on |
| `simultaneous_break` | 2 or more distinct C1 prices broken on the same side in the same minute | on |
| `magnitude_hit` / `exhaustion_hit` | a drawn target is touched (the Suite's crossed latch) | on |
| `liquidity_touch` | first touch today of a prior D, W or M high or low, or a D/W/M exhaustion level | on |
| `ftfc_change` | FTFC turns up, down or to conflict and holds for 5 minutes | on |
| `bar_type_change` | a forming bar changes type (1 to 2u, 2u to 3, and so on) | off |

Each event gets a score: a base for its type, plus half its highest timeframe's weight (5m = 1 up to
M = 8), plus 1 when it agrees with FTFC. Events in the opening minute lose 1.5, because every intraday bar
starts then and breaks are expected. The top 4 default-on events in each clock hour are marked as headlines.
These weights are a first guess, to be tuned on real days. Times in the feed are Pacific.
