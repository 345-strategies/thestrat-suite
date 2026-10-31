"""Replay a trading day through TheStrat Suite's logic, one minute at a time.

Phase 1: every timeframe's forming bar, its C1, its bar type and FTFC, at every
minute of the day. Classification is delegated to thestrat_grammar, which is
parity-tested against the Suite's Pine, so this package only decides which
candles get compared, never how.
"""

from .bars import Bar
from .sessions import EquityRTH, TIMEFRAMES
from .engine import replay_day, DayReplay, MinuteState

__version__ = "0.1.0"

__all__ = ["Bar", "EquityRTH", "TIMEFRAMES", "replay_day", "DayReplay", "MinuteState"]
