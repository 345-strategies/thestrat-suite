import random
from datetime import date, datetime, timedelta

import pytest

from thestrat_replay import Bar


def session_days(start: date, n: int) -> list[date]:
    days, d = [], start
    while len(days) < n:
        if d.weekday() < 5:
            days.append(d)
        d += timedelta(days=1)
    return days


def random_minutes(days, seed=7, start_price=500.0) -> list[Bar]:
    rnd = random.Random(seed)
    px = start_price
    out = []
    for d in days:
        px += rnd.gauss(0, 2)   # overnight gap
        t = datetime(d.year, d.month, d.day, 9, 30)
        for _ in range(390):
            o = px
            c = o + rnd.gauss(0, 0.25)
            h = max(o, c) + abs(rnd.gauss(0, 0.1))
            l = min(o, c) - abs(rnd.gauss(0, 0.1))
            out.append(Bar(t, round(o, 2), round(h, 2), round(l, 2), round(c, 2), rnd.randint(1000, 9000)))
            px = c
            t += timedelta(minutes=1)
    return out


@pytest.fixture(scope="session")
def days():
    return session_days(date(2026, 9, 1), 30)   # Sep 1 (Tue) .. mid Oct 2026


@pytest.fixture(scope="session")
def minutes(days):
    return random_minutes(days)
