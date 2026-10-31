"""Build a day file: python -m thestrat_replay MINUTES DATE [--daily DAILY] [--out PATH]

MINUTES / DAILY are .csv (ts, open, high, low, close[, volume]; ts in ET) or .parquet.
"""

from __future__ import annotations

import argparse
from datetime import date
from pathlib import Path

from .engine import replay_day
from .io import bars_from_csv, bars_from_parquet, write_day_file


def _load(path: str, ts_col: str):
    return bars_from_parquet(path, ts_col=ts_col) if path.endswith(".parquet") else bars_from_csv(path, ts_col=ts_col)


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(prog="thestrat_replay")
    ap.add_argument("minutes")
    ap.add_argument("day", type=date.fromisoformat)
    ap.add_argument("--daily")
    ap.add_argument("--symbol", default="SPY")
    ap.add_argument("--ts-col", default="ts")
    ap.add_argument("--timeframes", default="5,15,30,60,240,D,W,M")
    ap.add_argument("--ftfc", default="15,30,60,240,D", help="comma list; default is the Suite Day Trade preset")
    ap.add_argument("--out")
    a = ap.parse_args(argv)
    tfs = tuple(a.timeframes.split(","))
    r = replay_day(
        _load(a.minutes, a.ts_col), a.day, symbol=a.symbol,
        daily=_load(a.daily, a.ts_col) if a.daily else None,
        timeframes=tfs, ftfc_timeframes=tuple(a.ftfc.split(",")) if a.ftfc else None,
    )
    out = Path(a.out or f"{a.symbol}_{a.day.isoformat()}.json")
    write_day_file(r, out)
    print(f"wrote {out} ({len(r.minutes)} minutes, {len(tfs)} timeframes)")


if __name__ == "__main__":
    main()
