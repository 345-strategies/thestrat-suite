# TheStrat Replay

Replays a trading day through TheStrat Suite's logic one minute at a time, for the day replay tool.

**Status: Phase 1 of the MVP, private.** It builds every timeframe's forming bar minute by minute, finds each
one's C1, labels its bar type and reads FTFC. Signals, targets and events come in later phases.

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
