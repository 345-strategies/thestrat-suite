from datetime import datetime, timedelta

from thestrat_replay import Bar, replay_day
from thestrat_replay.signals import PeriodLatch, SignalSettings, evaluate, exhaustion_levels

T0 = datetime(2026, 9, 1, 9, 30)


def b(i, o, h, l, c):
    return Bar(T0 + timedelta(days=i), o, h, l, c)


def run(c3, c2, c1, cc, settings=None, ftfc=(False, False)):
    L = PeriodLatch()
    L.roll(cc.ts, c1, c2, None, None)
    return evaluate(cc, c1, c2, c3, L, ftfc[0], ftfc[1], settings or SignalSettings())


C3 = b(0, 100, 105, 95, 101)
C2_2D_RED = b(1, 100, 104, 90, 92)     # 2d, closed down
C1_INSIDE = b(2, 93, 97, 91, 95)       # inside C2


def test_inside_reversal_goes_in_force_with_magnitude_at_c2_high():
    cc = b(3, 95, 98, 94, 97.5)        # breaks C1 high 97, closes above
    s = run(C3, C2_2D_RED, C1_INSIDE, cc)
    assert s.combo == "2d-1-2u"
    assert s.high.in_force and "insideRev" in s.high.terms
    assert s.high.trigger == 97
    assert s.high.magnitude == 104 and s.high.draw_magnitude
    assert not s.low.in_force and not s.low.draw   # inside breakout suppresses the other side


def test_poke_back_inside_is_f2u_not_in_force():
    cc = b(3, 95, 98, 94, 96)          # took 97, closed back inside
    s = run(C3, C2_2D_RED, C1_INSIDE, cc)
    assert s.is_f2u and s.combo == "2d-1-F2u"
    assert s.high.draw and not s.high.in_force
    assert not s.high.draw_magnitude   # F2u suppresses bullish magnitude


def test_inside_continuation_bear():
    cc = b(3, 95, 96, 89, 90)          # breaks C1 low 91, closes below
    s = run(C3, C2_2D_RED, C1_INSIDE, cc)
    assert s.low.in_force and "insideCont" in s.low.terms
    assert s.low.magnitude == 90


def test_two_two_reversal():
    c1 = b(2, 95, 100, 88, 89)         # 2d against C2 (broke 90, not 104)
    cc = b(3, 89, 101, 89, 100.5)      # 2u over 100, closes above
    s = run(C3, C2_2D_RED, c1, cc)
    assert s.combo == "2d-2u"
    assert s.high.in_force and "rev22" in s.high.terms


def test_two_two_continuation_needs_its_toggle():
    c2 = b(1, 100, 106, 96, 105)       # 2u
    c1 = b(2, 105, 108, 103, 104)      # 2u, red body low in range: not a hammer
    cc = b(3, 104, 110, 104, 109)
    assert "cont22" not in run(C3, c2, c1, cc).high.terms
    assert "cont22" in run(C3, c2, c1, cc, SignalSettings.all_signals()).high.terms


def test_failing_2_is_a_signal_only_when_enabled():
    cc = b(3, 95, 96, 90, 93)          # broke C1 low 91, closed back inside: F2d (bullish)
    off = run(C3, C2_2D_RED, C1_INSIDE, cc)
    on = run(C3, C2_2D_RED, C1_INSIDE, cc, SignalSettings.all_signals())
    assert off.is_f2d and not off.high.in_force
    assert on.high.in_force and "f2" in on.high.terms


def test_exhaustion_is_first_intact_pivot_beyond_c1():
    bars = [b(i, 100, 101, 99, 100) for i in range(60)]
    bars[52] = b(52, 100, 110, 99, 100)   # pivot high at [8]
    bars[56] = b(56, 100, 106, 99, 100)   # nearer pivot high at [4], also beyond C1
    hi, lo = exhaustion_levels(bars)
    assert hi.high == 106                 # the nearest untested swing wins
    assert lo is None                     # flat lows: no pivot
    assert exhaustion_levels(bars[:50]) == (None, None)   # Pine needs bar_index > 50


def test_targets_snapshot_per_period_and_latch(minutes, days):
    r = replay_day(minutes, days[16], signals=SignalSettings.all_signals())
    for tf in ("15", "60", "D"):
        by_period = {}
        for ms in r.minutes:
            sig = ms.tfs[tf].signal
            if sig is None:
                continue
            key = ms.tfs[tf].bar.ts
            snap = (sig.high.trigger, sig.low.trigger, sig.high.magnitude, sig.low.magnitude,
                    sig.high.exhaustion, sig.low.exhaustion)
            prev = by_period.setdefault(key, (snap, False, False))
            assert prev[0] == snap, (tf, ms.ts)                        # levels fixed for the period
            assert sig.high.magnitude_hit >= prev[1] and sig.low.magnitude_hit >= prev[2]   # latches never unset
            by_period[key] = (snap, sig.high.magnitude_hit, sig.low.magnitude_hit)
