"""Turn a day's minute-by-minute Suite state into a ranked event narrative.

An event is a change between two consecutive minutes, so each one traces back to
one Suite rule. Times are kept in ET (the data's clock) and rendered in Pacific
for the feed; US Eastern and Pacific change clocks on the same dates, so the
offset is always 3 hours.

Scores are a first guess to tune on real days, not a validated measure of
importance.
"""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field, asdict
from datetime import datetime, timedelta

from .engine import DayReplay, MinuteState

TF_LABEL = {"5": "5m", "15": "15m", "30": "30m", "60": "1H", "240": "4H", "D": "D", "W": "W", "M": "M"}
TF_WEIGHT = {"5": 1, "15": 2, "30": 3, "60": 4, "240": 5, "D": 6, "W": 7, "M": 8}

# type: (base score, on by default)
EVENT_TYPES = {
    "signal_in_force": (3.0, True),
    "signal_lost": (0.5, True),
    "failing_2": (2.0, True),
    "simultaneous_break": (3.0, True),
    "magnitude_hit": (2.0, True),
    "exhaustion_hit": (3.0, True),
    "liquidity_touch": (3.0, True),
    "ftfc_change": (2.0, True),
    "bar_type_change": (0.5, False),
}
HEADLINES_PER_HOUR = 4
FTFC_MIN_HOLD = 5        # minutes a new FTFC reading must hold before it counts
OPENING_PENALTY = 1.5    # the first minute starts every intraday bar at once, so its breaks are expected


@dataclass
class Event:
    ts: datetime                 # ET, the 1m bar that caused it
    type: str
    text: str
    direction: str | None        # "bull" / "bear" / None
    tfs: list[str]
    price: float | None
    score: float = 0.0
    headline: bool = False
    detail: dict = field(default_factory=dict)

    @property
    def time_pt(self) -> str:
        t = self.ts - timedelta(hours=3)
        return t.strftime("%-I:%M %p")

    def as_dict(self) -> dict:
        d = asdict(self)
        d["ts"] = self.ts.isoformat(timespec="minutes")
        d["time_pt"] = self.time_pt
        return d


def _fmt(x: float | None) -> str:
    return "" if x is None else f"{x:.2f}"


def _join(names: list[str]) -> str:
    return names[0] if len(names) == 1 else ", ".join(names[:-1]) + " and " + names[-1]


def build_events(replay: DayReplay) -> list[Event]:
    events: list[Event] = []
    tfs = replay.timeframes
    prev: MinuteState | None = None
    # per (tf, side): has the signal been in force yet in this period?
    seen_in_force: dict[tuple, object] = {}
    seen_f2: dict[str, object] = {}
    taken_levels: set = set()

    for ms in replay.minutes:
        def emit(type_, text, direction, tf_list, price, **detail):
            base, _ = EVENT_TYPES[type_]
            top = max((TF_WEIGHT[t] for t in tf_list), default=0)
            score = base + 0.5 * top
            if direction and ms.ftfc.value == ("up" if direction == "bull" else "down"):
                score += 1.0
            if prev is None:
                score -= OPENING_PENALTY
                detail["opening"] = True
            tf_list = sorted(tf_list, key=TF_WEIGHT.get)
            events.append(Event(ms.ts, type_, text, direction, tf_list, price, round(score, 2), False, detail))

        # Per-timeframe signal, Failing 2, bar type and target events.
        breaks = {"high": defaultdict(list), "low": defaultdict(list)}
        for tf in tfs:
            cur = ms.tfs[tf]
            old = prev.tfs[tf] if prev else None
            same_bar = old is not None and old.bar.ts == cur.bar.ts
            lab = TF_LABEL[tf]

            if cur.c1 is not None:
                for side, broke in (("high", cur.bar.high > cur.c1.high), ("low", cur.bar.low < cur.c1.low)):
                    was = same_bar and (old.bar.high > old.c1.high if side == "high" else old.bar.low < old.c1.low)
                    if broke and not was:
                        breaks[side][cur.c1.high if side == "high" else cur.c1.low].append(tf)

            if cur.state is not None and same_bar and old.state is not None and cur.state.structure != old.state.structure:
                emit("bar_type_change", f"{lab} became {cur.notation}", None, [tf], None, frm=old.notation, to=cur.notation)

            if cur.state is not None and cur.state.failed and seen_f2.get(tf) != cur.bar.ts and not (prev is None and tf in ("W", "M")):
                seen_f2[tf] = cur.bar.ts
                bull = cur.notation == "F2d"
                lvl = cur.c1.low if bull else cur.c1.high
                emit("failing_2", f"{lab} {cur.notation}: took {_fmt(lvl)} and closed back inside",
                     "bull" if bull else "bear", [tf], lvl)

            sig, osig = cur.signal, (old.signal if same_bar else None)
            if sig is None:
                continue
            for side, d in (("high", "bull"), ("low", "bear")):
                s = getattr(sig, side)
                o = getattr(osig, side) if osig else None
                word = "bullish" if d == "bull" else "bearish"
                if s.in_force and not (o and o.in_force):
                    key = (tf, side)
                    if seen_in_force.get(key) != cur.bar.ts:
                        seen_in_force[key] = cur.bar.ts
                        tgt = (f", magnitude {_fmt(s.magnitude)}" if s.draw_magnitude else
                               f", exhaustion {_fmt(s.exhaustion)}" if s.draw_exhaustion else "")
                        emit("signal_in_force", f"{lab} {sig.combo} {word} in force {'over' if d == 'bull' else 'under'} {_fmt(s.trigger)}{tgt}",
                             d, [tf], s.trigger, combo=sig.combo, terms=list(s.terms))
                if o and o.in_force and not s.in_force:
                    emit("signal_lost", f"{lab} {word} signal no longer in force (back through {_fmt(s.trigger)})", d, [tf], s.trigger)
                if s.magnitude_hit and o and not o.magnitude_hit and (o.draw_magnitude or s.draw_magnitude):
                    emit("magnitude_hit", f"{lab} {word} magnitude {_fmt(s.magnitude)} hit", d, [tf], s.magnitude)
                if s.exhaustion_hit and o and not o.exhaustion_hit and (o.draw_exhaustion or s.draw_exhaustion):
                    emit("exhaustion_hit", f"{lab} {word} exhaustion {_fmt(s.exhaustion)} hit", d, [tf], s.exhaustion)

        # Simultaneous breaks: distinct C1 prices broken on the same side in this minute.
        for side, by_price in breaks.items():
            if len(by_price) >= 2:
                names = [TF_LABEL[t] for t in sorted((t for p in by_price for t in by_price[p]), key=TF_WEIGHT.get)]
                d = "bull" if side == "high" else "bear"
                emit("simultaneous_break", f"{_join(names)} broke their {'highs' if side == 'high' else 'lows'} together ({len(by_price)} levels)",
                     d, [t for p in by_price for t in by_price[p]], None, levels=sorted(by_price), count=len(by_price))
                events[-1].score += len(by_price) - 2

        # Liquidity: prior D/W/M highs and lows, and HTF exhaustion levels, first touch today.
        for tf in ("D", "W", "M"):
            if tf not in tfs:
                continue
            cur = ms.tfs[tf]
            levels = []
            if cur.c1 is not None:
                levels += [("high", cur.c1.high, f"prior {_PERIOD[tf]} high"), ("low", cur.c1.low, f"prior {_PERIOD[tf]} low")]
            if cur.signal is not None:
                if cur.signal.high.exhaustion is not None:
                    levels.append(("high", cur.signal.high.exhaustion, f"{TF_LABEL[tf]} exhaustion high"))
                if cur.signal.low.exhaustion is not None:
                    levels.append(("low", cur.signal.low.exhaustion, f"{TF_LABEL[tf]} exhaustion low"))
            for side, px, name in levels:
                key = (name, px)
                hit = ms.minute.high >= px if side == "high" else ms.minute.low <= px
                if hit and key not in taken_levels:
                    taken_levels.add(key)
                    emit("liquidity_touch", f"Reached {name} {_fmt(px)}", "bull" if side == "high" else "bear", [tf], px, level=name)

        prev = ms

    events += _ftfc_events(replay)
    events.sort(key=lambda e: e.ts)
    _mark_headlines(events)
    return events


def _ftfc_events(replay: DayReplay) -> list[Event]:
    """FTFC changes that hold for FTFC_MIN_HOLD minutes, stamped at the minute they began."""
    out = []
    mins = replay.minutes
    names = f"{TF_LABEL[replay.ftfc_timeframes[0]]} to {TF_LABEL[replay.ftfc_timeframes[-1]]}"
    settled = mins[0].ftfc if mins else None
    i = 1
    while i < len(mins):
        v = mins[i].ftfc
        if v != settled:
            run = 1
            while i + run < len(mins) and mins[i + run].ftfc == v:
                run += 1
            if run >= FTFC_MIN_HOLD or i + run == len(mins):
                base, _ = EVENT_TYPES["ftfc_change"]
                if v.value == "conflict":
                    out.append(Event(mins[i].ts, "ftfc_change", f"FTFC back to conflict ({names})", None,
                                     list(replay.ftfc_timeframes), None, base - 1.0))
                else:
                    d = "bull" if v.value == "up" else "bear"
                    out.append(Event(mins[i].ts, "ftfc_change", f"FTFC turned {v.value} ({names})", d,
                                     list(replay.ftfc_timeframes), None, base + 2.0))
                settled = v
            i += run
        else:
            i += 1
    return out


_PERIOD = {"D": "day", "W": "week", "M": "month"}


def _mark_headlines(events: list[Event]) -> None:
    by_hour = defaultdict(list)
    for e in events:
        if EVENT_TYPES[e.type][1]:
            by_hour[(e.ts.date(), e.ts.hour)].append(e)
    for group in by_hour.values():
        for e in sorted(group, key=lambda e: (-e.score, e.ts))[:HEADLINES_PER_HOUR]:
            e.headline = True
