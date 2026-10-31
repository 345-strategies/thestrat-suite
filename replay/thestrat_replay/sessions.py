"""Which bar of a timeframe a 1m bar belongs to, for US equities in regular hours.

Boundaries follow TradingView's regular-session equity charts, which is what the
Suite runs on for SPY:

- Intraday timeframes count from the 09:30 ET open, so 1H bars are 09:30, 10:30,
  ... 15:30 (a 30-minute last bar) and 4H bars are 09:30 and 13:30.
- D is the session date. W starts Monday. M is the calendar month.

Times are ET. The player converts to PST for display.

Not handled here, on purpose: futures sessions (18:00 ET open, 4H at
18/22/02/06/10/14 ET) and CME holiday-glued bars. Those come with ES support.
"""

from __future__ import annotations

from datetime import date, datetime, timedelta

TIMEFRAMES = ("5", "15", "30", "60", "240", "D", "W", "M")

_INTRADAY_MINUTES = {"5": 5, "15": 15, "30": 30, "60": 60, "240": 240}


class EquityRTH:
    open_minute = 9 * 60 + 30   # 09:30 ET
    close_minute = 16 * 60      # 16:00 ET

    def in_session(self, ts: datetime) -> bool:
        m = ts.hour * 60 + ts.minute
        return self.open_minute <= m < self.close_minute and ts.weekday() < 5

    def bucket(self, ts: datetime, tf: str) -> tuple:
        """A sortable key, equal for every 1m bar inside the same `tf` bar."""
        d = ts.date()
        if tf in _INTRADAY_MINUTES:
            n = _INTRADAY_MINUTES[tf]
            since_open = ts.hour * 60 + ts.minute - self.open_minute
            return (d.toordinal(), since_open // n)
        if tf == "D":
            return (d.toordinal(),)
        if tf == "W":
            monday = d - timedelta(days=d.weekday())
            return (monday.toordinal(),)
        if tf == "M":
            return (d.year, d.month)
        raise ValueError(f"unsupported timeframe: {tf}")

    def bucket_start(self, ts: datetime, tf: str) -> datetime:
        """Open time of the `tf` bar that contains `ts` (calendar TFs: the session date's 09:30)."""
        d = ts.date()
        if tf in _INTRADAY_MINUTES:
            n = _INTRADAY_MINUTES[tf]
            since_open = ts.hour * 60 + ts.minute - self.open_minute
            start = self.open_minute + (since_open // n) * n
            return datetime(d.year, d.month, d.day, start // 60, start % 60)
        if tf == "D":
            first = d
        elif tf == "W":
            first = d - timedelta(days=d.weekday())
        elif tf == "M":
            first = date(d.year, d.month, 1)
        else:
            raise ValueError(f"unsupported timeframe: {tf}")
        return datetime(first.year, first.month, first.day, 9, 30)
