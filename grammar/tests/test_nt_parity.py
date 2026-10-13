"""Parity with the NinjaTrader 8 port (ninjatrader/src/Indicators/TheStratSuite/).

NinjaTrader is Windows-only, but its engine file is plain C# with no NinjaTrader
dependencies. This test compiles that file with Mono's `mcs` (C# 5, the oldest
language level NinjaTrader 8 accepts) together with a small stdin/stdout harness,
feeds it the same random bars the thinkorswim parity test uses, and compares the
answers with the grammar. It also type-checks the indicator file against stand-in
NinjaTrader types. Skipped when `mcs` or `mono` is not installed
(`apt-get install mono-mcs mono-runtime`).
"""

import datetime
import os
import random
import shutil
import subprocess
import tempfile

import pytest

from thestrat_grammar import Candle, FailedMethod, PatternMethod, classify, is_hammer, is_shooter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
NT = os.path.join(ROOT, "ninjatrader")
ENGINE = os.path.join(NT, "src", "Indicators", "TheStratSuite", "TheStratSuiteEngine.cs")
INDICATOR = os.path.join(NT, "src", "Indicators", "TheStratSuite", "TheStratSuite.cs")
HARNESS = os.path.join(NT, "tests", "ParityHarness.cs")
STUBS = os.path.join(NT, "tests", "stubs", "NinjaTraderStubs.cs")

METHODS = [FailedMethod.RECLAIM, FailedMethod.OPEN, FailedMethod.RECLAIM_AND_OPEN, FailedMethod.RECLAIM_OR_OPEN]
PATTERNS = [PatternMethod.BROAD, PatternMethod.CLASSIC, PatternMethod.PIN_BAR]

needs_mono = pytest.mark.skipif(not (shutil.which("mcs") and shutil.which("mono")), reason="mono not installed")


@pytest.fixture(scope="module")
def harness():
    out = os.path.join(tempfile.mkdtemp(), "harness.exe")
    subprocess.run(["mcs", "-langversion:5", "-nowarn:1591", "-out:" + out, ENGINE, HARNESS], check=True)

    def run(lines):
        res = subprocess.run(["mono", out], input="\n".join(lines) + "\n", capture_output=True, text=True, check=True)
        return res.stdout.split("\n")[: len(lines)]

    return run


def random_bars(rng):
    """Prior/current OHLC pairs with deliberate ties on every boundary (same as test_tos_parity)."""
    pl = rng.choice([100.0, 100.25, 99.5])
    ph = pl + rng.choice([0.25, 0.5, 1.0, 2.0])
    grid = [pl - 0.5, pl - 0.25, pl, (pl + ph) / 2, ph, ph + 0.25, ph + 0.5]
    h = rng.choice(grid)
    l = rng.choice([g for g in grid if g <= h])
    o = rng.choice([g for g in grid if l <= g <= h] or [l])
    c = rng.choice([g for g in grid if l <= g <= h] or [h])
    if rng.random() < 0.1:
        c = o  # doji: must bucket down
    return ph, pl, o, h, l, c


def fmt(*xs):
    return " ".join(repr(float(x)) if not isinstance(x, str) else x for x in xs)


@needs_mono
@pytest.mark.parametrize("m_idx", range(4))
def test_nt_classifier_matches_grammar(harness, m_idx):
    rng = random.Random(1000 + m_idx)
    cases = [random_bars(rng) for _ in range(10_000)]
    got = harness(["bar " + fmt(*b, str(m_idx), "1") for b in cases])
    for (ph, pl, o, h, l, c), g in zip(cases, got):
        expected = classify(Candle(o, h, l, c), Candle(pl, ph, pl, ph), method=METHODS[m_idx]).notation()
        assert g == expected, (ph, pl, o, h, l, c, METHODS[m_idx])


@needs_mono
def test_nt_classifier_f2_detection_off(harness):
    rng = random.Random(7)
    cases = [random_bars(rng) for _ in range(5_000)]
    got = harness(["bar " + fmt(*b, "0", "0") for b in cases])
    for (ph, pl, o, h, l, c), g in zip(cases, got):
        assert g == classify(Candle(o, h, l, c), Candle(pl, ph, pl, ph)).notation().lstrip("F")


@needs_mono
def test_nt_classifier_no_data(harness):
    assert harness(["bar nan nan 1.0 2.0 0.5 1.5 0 1", "bar 2.0 1.0 1.0 0.5 1.0 1.0 0 1"]) == ["-", "-"]


@needs_mono
@pytest.mark.parametrize("p_idx", range(3))
@pytest.mark.parametrize("color", [False, True])
def test_nt_hammer_shooter_match_grammar(harness, p_idx, color):
    rng = random.Random(50 + p_idx * 2 + color)
    cases = []
    for _ in range(10_000):
        l = 100.0
        h = l + rng.choice([0.0, 1.0, 2.0, 4.0])
        grid = [l + h_ * (h - l) / 20 for h_ in range(21)]
        o, c = rng.choice(grid), rng.choice(grid)
        cases.append((o, h, l, c))
    got = harness(["pat " + fmt(*cs, str(p_idx), "1" if color else "0") for cs in cases])
    for (o, h, l, c), g in zip(cases, got):
        cd = Candle(o, h, l, c)
        want = f"{int(is_hammer(cd, PATTERNS[p_idx], color))} {int(is_shooter(cd, PATTERNS[p_idx], color))}"
        assert g == want, (o, h, l, c, PATTERNS[p_idx], color)


@needs_mono
def test_nt_calendar_period_keys(harness):
    # Weeks start Monday, so a futures session opening Sunday evening (trade date Monday)
    # keys to the new week; quarters and halves are calendar-aligned.
    d = datetime.date
    cases = [
        ("Week", d(2026, 9, 28), d(2026, 10, 4), True),   # Mon and Sun of one week
        ("Week", d(2026, 9, 27), d(2026, 9, 28), False),  # Sun and Mon straddle weeks
        ("Quarter", d(2026, 3, 31), d(2026, 4, 1), False),
        ("Quarter", d(2026, 4, 1), d(2026, 6, 30), True),
        ("HalfYear", d(2026, 6, 30), d(2026, 7, 1), False),
        ("Year", d(2026, 1, 2), d(2026, 12, 31), True),
        ("Month", d(2026, 9, 30), d(2026, 10, 1), False),
    ]
    lines = []
    for tf, a, b, _ in cases:
        lines += [f"key {tf} {a.isoformat()}", f"key {tf} {b.isoformat()}"]
    got = harness(lines)
    for i, (tf, a, b, same) in enumerate(cases):
        assert (got[2 * i] == got[2 * i + 1]) == same, (tf, a, b)


@needs_mono
def test_nt_indicator_typechecks_against_stubs():
    out = os.path.join(tempfile.mkdtemp(), "ind.dll")
    res = subprocess.run(["mcs", "-langversion:5", "-target:library", "-r:System.Xml.dll", "-out:" + out,
                          ENGINE, INDICATOR, STUBS], capture_output=True, text=True)
    assert res.returncode == 0, res.stdout + res.stderr


@needs_mono
def test_nt_indicator_smoke_and_no_lookahead():
    """Runs the indicator end to end on synthetic sessions (ninjatrader/tests/SmokeTest.cs):
    every preset, a 5-minute and a daily chart, realtime ticks, preview and a render pass.
    It also replays each chart with the higher-timeframe series trailing the chart (only
    closed candles loaded) and requires the painted history to be identical, which is the
    no-lookahead check."""
    out = os.path.join(tempfile.mkdtemp(), "smoke.exe")
    subprocess.run(["mcs", "-langversion:5", "-r:System.Xml.dll", "-out:" + out, ENGINE, INDICATOR, STUBS,
                    os.path.join(NT, "tests", "SmokeTest.cs")], check=True, capture_output=True)
    res = subprocess.run(["mono", out], capture_output=True, text=True)
    assert res.returncode == 0 and res.stdout.strip().endswith("OK"), res.stdout[-4000:] + res.stderr[-2000:]
