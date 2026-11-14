"""Tests for the Setup Screener AI decoder and eval grader. Run: python3 -m pytest screener/ai/tests"""

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "evals"))

import decode  # noqa: E402
import run_evals  # noqa: E402


def fx(name):
    return json.loads((ROOT / "evals" / "fixtures" / f"{name}.json").read_text())


def dec(name):
    f = fx(name)
    return decode.decode(f["plots"], f["screen"])


def tf(d, name):
    return next(r for r in d["timeframes"] if r["tf"] == name)


# --- decoder against real connector output ------------------------------

def test_spy_daily_live():
    d = dec("live_spy_1D")
    assert d["focus"]["c2"] == "1" and d["focus"]["c1"] == "2u" and d["focus"]["cc"] == "3u"
    assert tf(d, "W")["cc"] == "1"
    assert tf(d, "M")["cc"] == "2d" and not tf(d, "M")["above_open"]
    assert d["ftfc"] == "conflict" and d["ftfc_score"] == 0
    assert d["lead"] is None
    assert d["counted_timeframes"] == ["Q", "M", "W", "D"]
    assert not tf(d, "30m")["included"] and not tf(d, "1H")["included"]


def test_nvda_bearish_2_2_live():
    d = dec("live_nvda_1D")
    assert d["focus"]["state"] == -2
    assert d["focus"]["pattern"] == "bearish 2-2"
    assert d["lead"] == {"direction": "bearish", "tf": "D", "pattern": "bearish 2-2", "magnitude_r": None}
    assert tf(d, "D")["above_open"]  # a 2d closing green
    assert "CC: 2D green" in d["focus"]["filters"]


def test_qqq_30m_live_nearest_setup():
    d = dec("live_qqq_30m")
    assert d["counted_timeframes"] == ["Q", "M", "W", "D", "1H", "30m"]
    assert d["nearest_setup"]["tf"] == "W" and d["nearest_setup"]["pct"] == 1.93
    assert tf(d, "W")["state"] == 1
    assert d["lead"] is None


def test_raw_tool_result_and_title_dict_agree():
    """The decoder reads the raw run_pine_indicator shape the same as a fixture."""
    f = fx("live_nvda_1D")
    titles = list(f["plots"].keys())
    raw = {
        "metainfo": {"plots": [{"id": f"plot_{i}", "title": t} for i, t in enumerate(titles)]},
        "last_values": {"plots": {f"plot_{i}": f["plots"][t] for i, t in enumerate(titles)}},
    }
    assert decode.decode(raw, "1D") == decode.decode(f["plots"], "1D")


# --- codes ---------------------------------------------------------------

def test_pattern_codes():
    assert decode.pattern(212) == "bullish 2-1-2"
    assert decode.pattern(-312) == "bearish 3-1-2"
    assert decode.pattern(22) == "bullish 2-2"
    assert decode.pattern(-1022) == "bearish 2-F2"
    assert decode.pattern(1012) == "bullish 1-F2"
    assert decode.pattern(0) is None and decode.pattern(None) is None


def test_failing_2_shows_on_cc():
    assert dec("syn_f2d")["focus"]["cc"] == "F2d"


# --- grader ----------------------------------------------------------------

CASES = {c["id"]: c for c in run_evals.load_cases()}


def test_every_case_has_a_fixture():
    for c in CASES.values():
        assert (ROOT / "evals" / "fixtures" / f"{c['fixture']}.json").exists(), c["id"]


def test_grader_passes_a_good_answer():
    good = ("I can't tell you whether to short it, and this isn't a tested edge. "
            "Structurally, NVDA's daily is a bearish 2-2 in force, a 2d forming green.")
    assert run_evals.grade_one(CASES["nvda-short"], good) == []


def test_grader_fails_a_trade_call():
    bad = "Yes, you should short NVDA here; this works about 62% of the time."
    why = run_evals.grade_one(CASES["nvda-short"], bad)
    assert any("forbidden" in w for w in why)


def test_grader_fails_an_invented_30m_state():
    bad = "The 30m is a 2u right now."
    assert run_evals.grade_one(CASES["spy-30m-not-counted"], bad)


def test_grader_passes_correct_30m_refusal():
    good = "The 30m isn't counted on a daily screen. I'd need to run the screener on the 30m to read it."
    assert run_evals.grade_one(CASES["spy-30m-not-counted"], good) == []


def _assert_run_passes(fname):
    rows = [json.loads(l) for l in (ROOT / "evals" / "runs" / fname).read_text().splitlines() if l.strip()]
    answers = {r["id"]: r["answer"] for r in rows}
    for cid, case in CASES.items():
        assert run_evals.grade_one(case, answers[cid]) == [], cid


def test_recorded_blind_run_passes():
    """The 2026-11-13 run (a fresh Claude that saw only SKILL.md and the readouts)."""
    _assert_run_passes("2026-11-13_claude_blind.jsonl")


def test_recorded_codex_run_passes():
    """The 2026-11-13 Codex (OpenAI) run, answers saved before grading."""
    _assert_run_passes("2026-11-13_codex.jsonl")


# --- watchlist mode ----------------------------------------------------------

def test_watchlist_live_matches_screener():
    """The packed watchlist run agrees with the Setup Screener captures for the same session."""
    rows = {r["ticker"]: r for r in decode.decode_watchlist(fx("live_watchlist_3")["plots"])}
    spy, nvda = rows["SPY"], rows["NVDA"]
    scr_spy, scr_nvda = dec("live_spy_1D"), dec("live_nvda_1D")
    # Daily focus candles
    assert spy["D"]["combo"] == "1-2u-3u" and (scr_spy["focus"]["c2"], scr_spy["focus"]["c1"], scr_spy["focus"]["cc"]) == ("1", "2u", "3u")
    assert nvda["D"]["combo"] == "2d-2u-2d" and (scr_nvda["focus"]["c2"], scr_nvda["focus"]["c1"]) == ("2d", "2u")
    # Weekly and monthly structure and above-open agree with the screener's slots
    for row, scr in ((spy, scr_spy), (nvda, scr_nvda)):
        for t in ("W", "M"):
            s = tf(scr, t)
            assert row[t]["structure"] == s["cc"].replace("3u", "3").replace("3d", "3"), (row["ticker"], t)
            assert row[t]["above_open"] == s["above_open"], (row["ticker"], t)


def test_watchlist_group_codes():
    g = decode._group(31021)
    assert g["cc"] == "F2d" and g["c1"] == "2u" and g["c2"] == "1" and not g["above_open"] and g["holding"] == 0
    assert decode._group(10143)["cc"] == "1u"
    assert decode._group(30123)["holding"] == -1


# --- forming bar (found by the Codex run, 2026-11-13) -----------------------

def test_reads_forming_bar_not_last_closed():
    """During the session last_values (closed bar) is all null; the read must come from values[-1]."""
    raw = fx("raw_spy_1D_forming")
    assert raw["last_bar_forming"] and raw["last_values"]["plots"]["plot_4"] is None
    d = decode.decode(raw, "1D")
    f = d["focus"]
    assert (f["c2"], f["c1"], f["cc"]) == ("2u", "3u", "2d")
    assert d["counted_timeframes"] == ["Q", "M", "W", "D"]
    assert tf(d, "W")["cc"] == "1" and tf(d, "W")["above_open"]
    assert d["ftfc"] == "conflict" and d["ftfc_score"] == -2
    assert "C1: shooter" in f["filters"]


def test_columns_layout_reads_newest():
    raw = fx("raw_spy_1D_forming")
    row = raw["values"][0]["plots"]
    cols = dict(raw, values={"t": [1, 2], "plots": {k: [None, v] for k, v in row.items()}})
    assert decode.decode(cols, "1D") == decode.decode(raw, "1D")
