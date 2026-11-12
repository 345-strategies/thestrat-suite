"""Loading 1m bars and writing day files. Parquet needs pandas + pyarrow."""

from __future__ import annotations

import csv
import json
from datetime import datetime
from pathlib import Path

from .bars import Bar
from .engine import DayReplay


def bars_from_csv(path: str | Path, *, ts_col: str = "ts") -> list[Bar]:
    """CSV with columns ts (ET, ISO), open, high, low, close[, volume]."""
    out = []
    with open(path, newline="") as f:
        for row in csv.DictReader(f):
            out.append(Bar(
                datetime.fromisoformat(row[ts_col]).replace(tzinfo=None),
                float(row["open"]), float(row["high"]), float(row["low"]), float(row["close"]),
                float(row.get("volume") or 0),
            ))
    return out


def bars_from_parquet(path: str | Path, *, ts_col: str = "ts") -> list[Bar]:
    """Parquet with an ET timestamp column (naive ET or tz-aware) and OHLC[V] columns."""
    import pandas as pd

    df = pd.read_parquet(path)
    ts = pd.to_datetime(df[ts_col])
    if ts.dt.tz is not None:
        ts = ts.dt.tz_convert("America/New_York").dt.tz_localize(None)
    vol = df["volume"] if "volume" in df else 0.0
    df = df.assign(_ts=ts, _v=vol).sort_values("_ts")
    return [
        Bar(t.to_pydatetime(), float(o), float(h), float(l), float(c), float(v))
        for t, o, h, l, c, v in zip(df["_ts"], df["open"], df["high"], df["low"], df["close"], df["_v"])
    ]


def write_day_file(replay: DayReplay, path: str | Path) -> None:
    Path(path).write_text(json.dumps(replay.as_dict(), separators=(",", ":")))
