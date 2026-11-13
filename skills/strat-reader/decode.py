#!/usr/bin/env python3
"""Decode a TheStrat Setup Screener run into a plain TheStrat readout.

The Strat Reader skill runs the published Setup Screener through TradingView's
`run_pine_indicator` tool. That tool returns numbered plots (plot_0, plot_1,
...) plus `metainfo.plots`, which names each one. This module maps the numbers
back to TheStrat words using the screener's own column rules
(screener/README.md), so a model reading the result never has to remember a
code table, and the eval grader has one source of truth.

Usage:
    python3 decode.py result.json [--screen 1D] [--slots 30m,1H,D,W,M,Q]
    cat result.json | python3 decode.py - --json

Pure standard library. Nothing here calls TradingView.
"""

from __future__ import annotations

import argparse
import json
import sys
from typing import Any

DEFAULT_SLOTS = ["30m", "1H", "D", "W", "M", "Q"]

TYPE_LABEL = {1: "1", 2: "2u", -2: "2d", 3: "3u", -3: "3d"}
STATE_LABEL = {2: "bullish signal in force", -2: "bearish signal in force", 1: "potential (inside, untriggered)", 0: "no signal"}
TF_MINUTES = {
    1: "1m", 5: "5m", 15: "15m", 30: "30m", 60: "1H", 120: "2H", 240: "4H",
    1440: "D", 10080: "W", 43200: "M", 129600: "Q", 518400: "Y",
}
# Larger first, the order a Strat read goes in.
TF_RANK = ["Y", "Q", "M", "W", "D", "4H", "2H", "1H", "30m", "15m", "5m", "1m"]

FOCUS_FILTERS = [
    "In force", "CC: F2D", "CC: F2U", "CC: 2U red", "CC: 2D green",
    "CC: hammer", "CC: shooter", "C1: hammer", "C1: shooter",
    "FTFC Up (state)", "FTFC Down (state)", "Domino (state)",
]


def _int(v: Any) -> int | None:
    if v is None:
        return None
    try:
        f = float(v)
    except (TypeError, ValueError):
        return None
    if f != f:  # NaN
        return None
    return int(round(f))


def by_title(result: dict) -> dict[str, Any]:
    """{plot title: last value} from a run_pine_indicator result.

    Accepts the raw tool result, or a dict that is already {title: value}
    (the fixture format), so tests and the live tool share one path.
    """
    if "metainfo" not in result and "last_values" not in result:
        return dict(result)
    names = {p["id"]: p["title"] for p in result.get("metainfo", {}).get("plots", [])}
    values = (result.get("last_values") or {}).get("plots") or {}
    return {names.get(k, k): v for k, v in values.items()}


def candle(code: Any) -> str | None:
    c = _int(code)
    return None if c is None else TYPE_LABEL.get(c, f"?{c}")


def pattern(code: Any) -> str | None:
    """Signed screener pattern code -> 'bullish 2-1-2', 'bearish 2-F2', ..."""
    c = _int(code)
    if not c:
        return None
    side = "bullish" if c > 0 else "bearish"
    a = abs(c)
    failing = a >= 1000
    digits = str(a - 1000 if failing else a)
    parts = list(digits)
    if failing:
        parts[-1] = "F2"
    return f"{side} {'-'.join(parts)}"


def decode(result: dict, screen: str = "1D", slots: list[str] | None = None) -> dict:
    v = by_title(result)
    slots = list(slots or DEFAULT_SLOTS)

    rows = []
    for i, tf in enumerate(slots, start=1):
        if tf == "Off":
            continue
        t = _int(v.get(f"S{i} Type"))
        if t is None:
            # Below the screen timeframe, or off: the screener plots na.
            rows.append({"tf": tf, "included": False})
            continue
        ao = _int(v.get(f"S{i} Above Open"))
        st = _int(v.get(f"S{i} State")) or 0
        rows.append({
            "tf": tf,
            "included": True,
            "cc": TYPE_LABEL.get(t, f"?{t}"),
            "above_open": ao == 1,
            "state": st,
            "state_text": STATE_LABEL.get(st, f"?{st}"),
        })
    rows.sort(key=lambda r: TF_RANK.index(r["tf"]) if r["tf"] in TF_RANK else 99)

    ftfc = _int(v.get("FTFC"))
    lead_dir = _int(v.get("Lead Direction")) or 0
    lead_tf = _int(v.get("Lead TF (min)"))
    near = v.get("Nearest Setup %")
    near_tf = _int(v.get("Nearest Setup TF (min)"))
    mag_r = v.get("Lead Magnitude R")

    cc_code = _int(v.get("CC"))
    cc_label = candle(cc_code)
    if cc_label and _int(v.get("CC: F2U")) == 1:
        cc_label = "F2u"
    elif cc_label and _int(v.get("CC: F2D")) == 1:
        cc_label = "F2d"

    focus = {
        "tf": screen,
        "c2": candle(v.get("C2")),
        "c1": candle(v.get("C1")),
        "cc": cc_label,
        "state": _int(v.get("State")) or 0,
        "state_text": STATE_LABEL.get(_int(v.get("State")) or 0),
        "pattern": pattern(v.get("Pattern")),
        "continuation": {1: "bullish 2-2 continuation", -1: "bearish 2-2 continuation"}.get(_int(v.get("CT")) or 0),
        "c1_shape": {1: "hammer", -1: "shooter"}.get(_int(v.get("C1 Shape")) or 0),
        "c1_color": {1: "green", -1: "red"}.get(_int(v.get("C1 Color")) or 0),
        "filters": [f for f in FOCUS_FILTERS if _int(v.get(f)) == 1],
    }

    return {
        "screen": screen,
        "focus": focus,
        "timeframes": rows,
        "ftfc": {1: "up", -1: "down", 0: "conflict"}.get(ftfc, None) if ftfc is not None else None,
        "ftfc_score": _int(v.get("FTFC Score")),
        "counted_timeframes": [r["tf"] for r in rows if r["included"]],
        "lead": None if lead_dir == 0 else {
            "direction": "bullish" if lead_dir > 0 else "bearish",
            "tf": TF_MINUTES.get(lead_tf, f"{lead_tf}min") if lead_tf else None,
            "pattern": pattern(v.get("Lead Pattern")),
            "magnitude_r": None if mag_r is None else round(float(mag_r), 2),
        },
        "net_in_force": _int(v.get("Net In Force")),
        "domino": _int(v.get("Domino")) or 0,
        "nearest_setup": None if near is None else {
            "pct": round(float(near), 2),
            "side": "bullish trigger above price" if float(near) > 0 else "bearish trigger below price",
            "tf": TF_MINUTES.get(near_tf, f"{near_tf}min") if near_tf else None,
        },
    }


def readout(d: dict, symbol: str = "") -> str:
    """The compact text a model reads first."""
    f = d["focus"]
    head = f"{symbol} {d['screen']} screen".strip()
    lines = [head]
    lines.append(
        f"Focus {f['tf']}: C2 {f['c2']}, C1 {f['c1']}, CC {f['cc']}. {f['state_text'].capitalize()}"
        + (f" ({f['pattern']})" if f["pattern"] else "") + "."
    )
    for r in d["timeframes"]:
        if not r["included"]:
            lines.append(f"  {r['tf']:>3}: not counted (smaller than the {d['screen']} screen)")
            continue
        lines.append(
            f"  {r['tf']:>3}: CC {r['cc']:<2} {'above' if r['above_open'] else 'below'} open, {r['state_text']}"
        )
    lines.append(
        f"FTFC across {'+'.join(d['counted_timeframes'])}: {d['ftfc']} (score {d['ftfc_score']:+d})"
        if d["ftfc_score"] is not None else f"FTFC: {d['ftfc']}"
    )
    if d["lead"]:
        L = d["lead"]
        lines.append(
            f"Lead: {L['pattern'] or L['direction']} on {L['tf']}"
            + (f", magnitude {L['magnitude_r']}R away" if L["magnitude_r"] is not None else "")
        )
    else:
        lines.append("Lead: none (no slot has a signal in force)")
    if d["nearest_setup"]:
        n = d["nearest_setup"]
        lines.append(f"Nearest setup: {n['side']}, {abs(n['pct'])}% from price, on {n['tf']}")
    if d["domino"]:
        lines.append(f"Domino: {abs(d['domino'])} timeframes inside in a row")
    if f["filters"]:
        lines.append("Matches: " + ", ".join(f["filters"]))
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("path", help="run_pine_indicator result JSON, or - for stdin")
    ap.add_argument("--screen", default="1D", help="timeframe the screener ran on")
    ap.add_argument("--slots", default=",".join(DEFAULT_SLOTS), help="slot timeframes in settings order")
    ap.add_argument("--symbol", default="")
    ap.add_argument("--json", action="store_true", help="print the decoded dict instead of the readout")
    a = ap.parse_args(argv)
    raw = sys.stdin.read() if a.path == "-" else open(a.path, encoding="utf-8").read()
    result = json.loads(raw)
    d = decode(result, a.screen, a.slots.split(","))
    print(json.dumps(d, indent=2) if a.json else readout(d, a.symbol or result.get("symbol", "")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
