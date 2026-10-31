from collections import Counter
from datetime import date, datetime

from thestrat_replay import Bar, replay_day
from thestrat_replay.events import EVENT_TYPES, HEADLINES_PER_HOUR, build_events


def test_feed_basics(minutes, days):
    r = replay_day(minutes, days[16], ftfc_timeframes=("15", "30", "60", "240", "D"))
    ev = build_events(r)
    assert ev and all(e.type in EVENT_TYPES for e in ev)
    assert [e.ts for e in ev] == sorted(e.ts for e in ev)
    per_hour = Counter((e.ts.hour) for e in ev if e.headline)
    assert max(per_hour.values()) <= HEADLINES_PER_HOUR
    assert all(EVENT_TYPES[e.type][1] for e in ev if e.headline)   # off-by-default types never headline
    # One in-force event per timeframe, side and bar: re-entries are not repeated.
    keys = [(e.tfs[0], e.direction, e.detail.get("combo"), e.ts) for e in ev if e.type == "signal_in_force"]
    assert len(keys) == len(set(keys))


def _day(rows):
    t = lambda m: datetime(2026, 9, 2, 9 + (30 + m) // 60, (30 + m) % 60)
    return [Bar(t(i), *r) for i, r in enumerate(rows)]


def _prior():
    return [Bar(datetime(2026, 9, 1, 9 + (30 + i) // 60, (30 + i) % 60), 100, 101, 99, 100) for i in range(390)]


def test_prior_day_high_is_liquidity_and_times_are_pacific():
    rows = [(100, 100.5, 99.5, 100.2)] * 10 + [(100.2, 101.2, 100.1, 101.1)]
    r = replay_day(_prior() + _day(rows), date(2026, 9, 2), timeframes=("5", "D"))
    liq = [e for e in build_events(r) if e.type == "liquidity_touch"]
    assert [(e.text, e.time_pt) for e in liq] == [("Reached prior day high 101.00", "6:40 AM")]


def test_ftfc_flicker_is_ignored():
    # Price crosses the open back and forth every minute: FTFC never holds, so no FTFC events.
    rows = [(100, 100.2, 99.8, 100.1 if i % 2 else 99.9) for i in range(30)]
    rows[0] = (100, 100.2, 99.8, 100.1)
    r = replay_day(_prior() + _day(rows), date(2026, 9, 2), timeframes=("5", "D"))
    assert not [e for e in build_events(r) if e.type == "ftfc_change"]


def test_day_file_carries_events(minutes, days):
    d = replay_day(minutes, days[16]).as_dict()
    assert d["events"] and set(d["event_types"]) == set(EVENT_TYPES)
