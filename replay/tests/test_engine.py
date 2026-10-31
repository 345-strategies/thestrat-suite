from datetime import date, datetime

import pytest
from thestrat_grammar import Candle, Continuity, classify

from thestrat_replay import Bar, EquityRTH, replay_day
from thestrat_replay.engine import _aggregate

S = EquityRTH()
ALLOWED = {"1": {"1", "2u", "2d", "3"}, "2u": {"2u", "3"}, "2d": {"2d", "3"}, "3": {"3"}}


def test_hour_and_4h_boundaries_follow_tv_equity_session():
    starts = {S.bucket_start(datetime(2026, 9, 2, 9 + (30 + m) // 60, (30 + m) % 60), "60") for m in range(390)}
    assert sorted(t.strftime("%H:%M") for t in starts) == ["09:30", "10:30", "11:30", "12:30", "13:30", "14:30", "15:30"]
    starts4 = {S.bucket_start(datetime(2026, 9, 2, 9 + (30 + m) // 60, (30 + m) % 60), "240") for m in range(390)}
    assert sorted(t.strftime("%H:%M") for t in starts4) == ["09:30", "13:30"]


@pytest.fixture(scope="module")
def replay(minutes, days):
    return replay_day(minutes, days[16])   # a Thursday mid-September


def test_forming_bar_is_the_running_aggregate(replay, minutes, days):
    today = [b for b in minutes if b.ts.date() == days[16]]
    for i, ms in enumerate(replay.minutes):
        for tf in ("5", "15", "30", "60", "240", "D"):
            k = S.bucket(ms.ts, tf)
            part = [b for b in today[: i + 1] if S.bucket(b.ts, tf) == k]
            agg = _aggregate(part, tf, S)[0]
            got = ms.tfs[tf].bar
            assert (got.open, got.high, got.low, got.close) == (agg.open, agg.high, agg.low, agg.close)


def test_last_minute_of_each_bar_matches_completed_bar_classification(replay, minutes, days):
    """The forming state at a bar's final minute is the grammar's verdict on the completed bar."""
    upto = [b for b in minutes if b.ts.date() <= days[16]]
    for tf in ("5", "15", "30", "60", "240"):
        done = _aggregate(upto, tf, S)
        by_start = {b.ts: (prev, b) for prev, b in zip(done, done[1:])}
        last_minute = {}
        for ms in replay.minutes:
            last_minute[ms.tfs[tf].bar.ts] = ms
        for start, ms in last_minute.items():
            prev, bar = by_start[start]
            want = classify(bar.candle(), prev.candle(), live=True)
            assert ms.tfs[tf].state == want, (tf, start)


def test_bar_types_only_move_forward(replay):
    """Irreversibility: within a bar, 2u can only stay 2u or become 3, and so on."""
    for tf in replay.timeframes:
        prev_start, prev_struct = None, None
        for ms in replay.minutes:
            s = ms.tfs[tf]
            if s.state is None:
                continue
            struct = s.state.structure.value
            if s.bar.ts == prev_start:
                assert struct in ALLOWED[prev_struct], (tf, ms.ts, prev_struct, struct)
            prev_start, prev_struct = s.bar.ts, struct


def test_first_hour_uses_prior_session_last_hour_as_c1(replay, minutes, days):
    prior = [b for b in minutes if b.ts.date() == days[15] and b.ts >= datetime(days[15].year, days[15].month, days[15].day, 15, 30)]
    c1 = replay.minutes[0].tfs["60"].c1
    assert c1.ts.strftime("%H:%M") == "15:30" and c1.ts.date() == days[15]
    assert c1.high == max(b.high for b in prior) and c1.low == min(b.low for b in prior)


def test_week_and_month_carry_earlier_sessions(minutes, days):
    wed = days[11]   # 2026-09-16, a Wednesday
    assert wed.weekday() == 2
    r = replay_day(minutes, wed)
    first = r.minutes[0].tfs["W"]
    mon = [b for b in minutes if b.ts.date() == days[9]]
    week_before = [b for b in minutes if days[9] <= b.ts.date() < wed]
    assert first.bar.open == mon[0].open
    assert first.bar.high >= max(b.high for b in week_before)
    prior_week = [b for b in minutes if days[4] <= b.ts.date() <= days[8]]
    assert first.c1.high == max(b.high for b in prior_week)
    assert first.c1.low == min(b.low for b in prior_week)
    m = r.minutes[0].tfs["M"]
    assert m.bar.open == minutes[0].open        # Sep 1 open carries into the forming M
    assert m.c1 is None                          # no August data: no C1, no type


def test_daily_history_source_gives_same_answer(minutes, days):
    daily = _aggregate([b for b in minutes if b.ts.date() < days[16]], "D", S)
    a = replay_day(minutes, days[16])
    b = replay_day(minutes, days[16], daily=daily)
    for x, y in zip(a.minutes, b.minutes):
        for tf in ("D", "W"):
            assert x.tfs[tf].state == y.tfs[tf].state
            assert x.tfs[tf].bar == y.tfs[tf].bar


def test_no_lookahead(minutes, days):
    """Changing a later minute never changes an earlier state."""
    base = replay_day(minutes, days[16])
    idx = next(i for i, b in enumerate(minutes) if b.ts == datetime(2026, 9, 23, 13, 0))
    bumped = list(minutes)
    b = bumped[idx]
    bumped[idx] = Bar(b.ts, b.open, b.high + 50, b.low - 50, b.close, b.volume)
    alt = replay_day(bumped, days[16])
    cut = next(i for i, m in enumerate(base.minutes) if m.ts == b.ts)
    assert [m.as_dict() for m in base.minutes[:cut]] == [m.as_dict() for m in alt.minutes[:cut]]
    assert base.minutes[cut].as_dict() != alt.minutes[cut].as_dict()


def test_ftfc_reads_sign_channel_of_forming_bars():
    d0, d1 = date(2026, 9, 1), date(2026, 9, 2)
    bars = [Bar(datetime(2026, 9, 1, 9 + (30 + i) // 60, (30 + i) % 60), 100, 101, 99, 100.5) for i in range(390)]
    # Day 2 opens at 100, rallies, then dumps below the open in the last minute.
    bars += [Bar(datetime(2026, 9, 2, 9, 30), 100, 102, 100, 101.5),
             Bar(datetime(2026, 9, 2, 9, 31), 101.5, 101.6, 98.9, 99.2)]
    r = replay_day(bars, d1, timeframes=("5", "D"))
    assert r.minutes[0].ftfc is Continuity.UP
    assert r.minutes[1].ftfc is Continuity.DOWN
    assert r.minutes[0].tfs["D"].notation == "2u"
    assert r.minutes[1].tfs["D"].notation == "3d"


def test_failed_2_appears_and_clears_live():
    """A 2u that closes back inside C1 reads F2u, and 2u again once price reclaims the high."""
    prior = [Bar(datetime(2026, 9, 1, 9 + (30 + i) // 60, (30 + i) % 60), 100, 101, 99, 100) for i in range(390)]
    t = lambda m: datetime(2026, 9, 2, 9, 30 + m)
    day = [Bar(t(0), 100, 101.5, 100, 101.4),   # breaks D C1 high 101: 2u
           Bar(t(1), 101.4, 101.4, 100.5, 100.6),  # back inside: F2u
           Bar(t(2), 100.6, 101.8, 100.6, 101.7)]  # reclaims: 2u
    r = replay_day(prior + day, date(2026, 9, 2), timeframes=("D",))
    assert [m.tfs["D"].notation for m in r.minutes] == ["2u", "F2u", "2u"]


def test_outside_hours_and_bad_input():
    with pytest.raises(ValueError):
        replay_day([Bar(datetime(2026, 9, 2, 8, 0), 1, 1, 1, 1)], date(2026, 9, 2))
    with pytest.raises(ValueError):
        replay_day([Bar(datetime(2026, 9, 2, 9, 30), 1, 1, 1, 1)], date(2026, 9, 2), timeframes=("2",))
