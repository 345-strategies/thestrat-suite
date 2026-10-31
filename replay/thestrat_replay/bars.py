"""One-minute input bars and the aggregation of them into a timeframe's bar."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime

from thestrat_grammar import Candle


@dataclass(frozen=True)
class Bar:
    """A bar stamped with its OPEN time in US/Eastern (naive datetime, ET wall clock).

    A 1m bar stamped 09:30 covers 09:30:00 to 09:30:59.
    """

    ts: datetime
    open: float
    high: float
    low: float
    close: float
    volume: float = 0.0

    def __post_init__(self) -> None:
        if self.high < self.low:
            raise ValueError(f"{self.ts}: high {self.high} below low {self.low}")

    def candle(self) -> Candle:
        return Candle(self.open, self.high, self.low, self.close)

    def extend(self, nxt: "Bar") -> "Bar":
        """This bar with a later bar folded in. Open and timestamp stay; the rest updates."""
        return Bar(
            ts=self.ts,
            open=self.open,
            high=max(self.high, nxt.high),
            low=min(self.low, nxt.low),
            close=nxt.close,
            volume=self.volume + nxt.volume,
        )
