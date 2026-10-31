"""Minute-by-minute Suite state for one trading day.

For every 1m bar of the day and every timeframe, the engine answers: what does
this timeframe's forming bar look like right now, what is its C1 (the last
completed bar), and what is its bar type against that C1? Plus FTFC across the
chosen timeframes. It never peeks: the state at minute t uses only bars that
have closed by the end of minute t.

Classification is thestrat_grammar.classify with the Suite's default Failing 2
detection (Reclaim), so a bar type here means exactly what it means in the Suite.
"""

from __future__ import annotations

from collections.abc import Iterable, Sequence
from dataclasses import dataclass, field
from datetime import date, datetime

from thestrat_grammar import Candle, Continuity, FailedMethod, State, classify, continuity

from .bars import Bar
from .sessions import TIMEFRAMES, EquityRTH

CALENDAR_TFS = ("D", "W", "M")


@dataclass(frozen=True)
class TFState:
    """One timeframe at one minute."""

    tf: str
    bar: Bar                 # the forming bar so far (ts = its open time)
    c1: Bar | None           # last completed bar; None when history is too short
    state: State | None      # bar type of the forming bar vs C1; None without C1

    @property
    def notation(self) -> str | None:
        return self.state.notation() if self.state else None

    def as_dict(self) -> dict:
        d = {
            "start": self.bar.ts.isoformat(timespec="minutes"),
            "o": self.bar.open, "h": self.bar.high, "l": self.bar.low, "c": self.bar.close,
            "type": self.notation,
        }
        if self.c1 is not None:
            d["c1"] = {"h": self.c1.high, "l": self.c1.low, "o": self.c1.open, "c": self.c1.close}
        if self.state is not None:
            d["structure"] = self.state.structure.value
            d["above_open"] = self.state.above_open
            d["failed"] = self.state.failed
        return d


@dataclass(frozen=True)
class MinuteState:
    """Every timeframe at the close of one 1m bar."""

    ts: datetime
    minute: Bar
    tfs: dict[str, TFState]
    ftfc: Continuity

    def as_dict(self) -> dict:
        return {
            "ts": self.ts.isoformat(timespec="minutes"),
            "m": {"o": self.minute.open, "h": self.minute.high, "l": self.minute.low,
                  "c": self.minute.close, "v": self.minute.volume},
            "ftfc": self.ftfc.value,
            "tf": {tf: s.as_dict() for tf, s in self.tfs.items()},
        }


@dataclass
class DayReplay:
    symbol: str
    day: date
    timeframes: tuple[str, ...]
    ftfc_timeframes: tuple[str, ...]
    minutes: list[MinuteState] = field(default_factory=list)

    def as_dict(self) -> dict:
        return {
            "schema": "thestrat-replay/day/1",
            "symbol": self.symbol,
            "date": self.day.isoformat(),
            "timezone": "America/New_York",
            "timeframes": list(self.timeframes),
            "ftfc_timeframes": list(self.ftfc_timeframes),
            "failed_method": "Reclaim",
            "minutes": [m.as_dict() for m in self.minutes],
        }


def _aggregate(bars: Iterable[Bar], tf: str, session: EquityRTH) -> list[Bar]:
    """Fold time-ordered bars into one bar per `tf` bucket, each stamped with its bucket open."""
    out: list[Bar] = []
    key = None
    for b in bars:
        k = session.bucket(b.ts, tf)
        if k != key:
            start = session.bucket_start(b.ts, tf)
            out.append(Bar(start, b.open, b.high, b.low, b.close, b.volume))
            key = k
        else:
            out[-1] = out[-1].extend(b)
    return out


def _check_sorted(bars: Sequence[Bar], what: str) -> None:
    for a, b in zip(bars, bars[1:]):
        if b.ts <= a.ts:
            raise ValueError(f"{what} must be strictly time-ordered: {a.ts} then {b.ts}")


def replay_day(
    minutes: Sequence[Bar],
    day: date,
    *,
    symbol: str = "SPY",
    daily: Sequence[Bar] | None = None,
    timeframes: Sequence[str] = TIMEFRAMES,
    ftfc_timeframes: Sequence[str] | None = None,
    failed_method: FailedMethod = FailedMethod.RECLAIM,
    session: EquityRTH | None = None,
) -> DayReplay:
    """Replay `day` minute by minute.

    minutes: 1m bars covering `day` plus at least the prior session (for the
        intraday C1s). Bars outside regular hours are ignored.
    daily: optional daily bars (ts = session date) for history before `day`. W
        and M need the prior week and month, and the forming W/M carries this
        week's and month's earlier sessions, so pass enough (two months covers
        it). Without it, daily history is built from `minutes`.
    ftfc_timeframes: which timeframes FTFC reads; defaults to all of them.
    """
    session = session or EquityRTH()
    tfs = tuple(timeframes)
    for tf in tfs:
        if tf not in TIMEFRAMES:
            raise ValueError(f"unsupported timeframe: {tf}")
    ftfc_tfs = tuple(ftfc_timeframes) if ftfc_timeframes is not None else tfs
    if not set(ftfc_tfs) <= set(tfs):
        raise ValueError("ftfc_timeframes must be a subset of timeframes")

    rth = [b for b in minutes if session.in_session(b.ts)]
    _check_sorted(rth, "minutes")
    before = [b for b in rth if b.ts.date() < day]
    today = [b for b in rth if b.ts.date() == day]
    if not today:
        raise ValueError(f"no regular-session 1m bars on {day}")

    if daily is not None:
        _check_sorted(daily, "daily")
        daily_before = [
            Bar(datetime(b.ts.year, b.ts.month, b.ts.day, 9, 30), b.open, b.high, b.low, b.close, b.volume)
            for b in daily if b.ts.date() < day
        ]
    else:
        daily_before = _aggregate(before, "D", session)

    # Per timeframe: completed bars before the day, and a carry-in partial bar
    # (sessions earlier this week/month) that today's minutes extend.
    completed: dict[str, list[Bar]] = {}
    forming: dict[str, Bar | None] = {}
    key: dict[str, tuple | None] = {}
    first_ts = today[0].ts
    for tf in tfs:
        hist = _aggregate(daily_before if tf in CALENDAR_TFS else before, tf, session)
        carry = None
        if hist and session.bucket(hist[-1].ts, tf) == session.bucket(first_ts, tf):
            carry = hist.pop()   # same W or M as today: still forming
        completed[tf] = hist
        forming[tf] = carry
        key[tf] = session.bucket(first_ts, tf) if carry else None

    replay = DayReplay(symbol, day, tfs, ftfc_tfs)
    for m in today:
        states: dict[str, TFState] = {}
        for tf in tfs:
            k = session.bucket(m.ts, tf)
            if k != key[tf]:
                if forming[tf] is not None:
                    completed[tf].append(forming[tf])
                forming[tf] = Bar(session.bucket_start(m.ts, tf), m.open, m.high, m.low, m.close, m.volume)
                key[tf] = k
            else:
                forming[tf] = forming[tf].extend(m)
            bar = forming[tf]
            c1 = completed[tf][-1] if completed[tf] else None
            st = classify(bar.candle(), c1.candle(), method=failed_method, live=True) if c1 else None
            states[tf] = TFState(tf, bar, c1, st)
        ftfc = continuity(states[tf].bar.candle() for tf in ftfc_tfs)
        replay.minutes.append(MinuteState(m.ts, m, states, ftfc))
    return replay
