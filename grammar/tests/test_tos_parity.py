"""Parity with the thinkorswim port (thinkorswim/TheStratSuite_TOS_v*.txt).

thinkScript has no runtime outside thinkorswim, so this test reads the port's
`StratState` script and its preset resolution straight out of the study file and
evaluates them with a small interpreter for the subset they use (def/plot,
if-then-else, and/or/!, comparisons, IsNaN). Edit the study and this test checks the
edit, not a copy. The presets are checked against the Pine source itself.
"""

import glob
import math
import os
import random
import re

import pytest

from thestrat_grammar import Candle, FailedMethod, classify

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
STUDIES = sorted(glob.glob(os.path.join(ROOT, "thinkorswim", "TheStratSuite_TOS_v*.txt")))
PINE = os.path.join(ROOT, "pine", "TheStratSuite_v3.1.1.pine")
CODES = {1: "1u", 2: "1d", 3: "2u", 4: "2d", 5: "F2u", 6: "F2d", 7: "3u", 8: "3d"}
METHODS = [FailedMethod.RECLAIM, FailedMethod.OPEN, FailedMethod.RECLAIM_AND_OPEN, FailedMethod.RECLAIM_OR_OPEN]


def extract_script(path, name="StratState"):
    src = open(path).read()
    m = re.search(r"script\s+" + name + r"\s*\{(.*?)\n\}", src, re.S)
    assert m, f"script {name} not found in {path}"
    body = re.sub(r"#[^\n]*", "", m.group(1))
    stmts = [s.strip() for s in body.split(";") if s.strip()]
    inputs, defs = [], []
    for s in stmts:
        kind, rest = s.split(None, 1)
        lhs, rhs = rest.split("=", 1)
        (inputs if kind == "input" else defs).append((kind, lhs.strip(), rhs.strip()))
    return [n for _, n, _ in inputs], defs


TOKEN = re.compile(r"\s*(<=|>=|==|!=|[()<>!+\-*/,]|\d+\.\d+|\d+|[A-Za-z_][A-Za-z_0-9]*(?:\.[A-Za-z_][A-Za-z_0-9]*)?)")


def tokenize(expr):
    pos, out = 0, []
    while pos < len(expr):
        m = TOKEN.match(expr, pos)
        if not m:
            raise SyntaxError(f"cannot tokenize at: {expr[pos:]}")
        out.append(m.group(1))
        pos = m.end()
    return out


class Eval:
    """Recursive-descent evaluator for the thinkScript subset in StratState."""

    def __init__(self, tokens, env):
        self.t, self.i, self.env = tokens, 0, env

    def peek(self):
        return self.t[self.i] if self.i < len(self.t) else None

    def take(self, want=None):
        tok = self.peek()
        if want is not None and tok != want:
            raise SyntaxError(f"expected {want}, got {tok}")
        self.i += 1
        return tok

    def expr(self):
        if self.peek() == "if":
            self.take("if")
            cond = self.expr()
            self.take("then")
            a = self.expr()
            self.take("else")
            b = self.expr()
            return a if cond else b
        return self.or_()

    def or_(self):
        v = self.and_()
        while self.peek() == "or":
            self.take()
            r = self.and_()
            v = bool(v) or bool(r)
        return v

    def and_(self):
        v = self.cmp()
        while self.peek() == "and":
            self.take()
            r = self.cmp()
            v = bool(v) and bool(r)
        return v

    def cmp(self):
        v = self.unary()
        ops = {"<": lambda a, b: a < b, ">": lambda a, b: a > b, "<=": lambda a, b: a <= b,
               ">=": lambda a, b: a >= b, "==": lambda a, b: a == b, "!=": lambda a, b: a != b}
        while self.peek() in ops:
            op = self.take()
            v = ops[op](v, self.unary())
        return v

    def unary(self):
        if self.peek() == "!":
            self.take()
            return not self.unary()
        return self.atom()

    def atom(self):
        tok = self.take()
        if tok == "(":
            v = self.expr()
            self.take(")")
            return v
        if tok == "IsNaN":
            self.take("(")
            v = self.expr()
            self.take(")")
            return isinstance(v, float) and math.isnan(v)
        if tok in ("yes", "no"):
            return tok == "yes"
        if re.fullmatch(r"\d+(\.\d+)?", tok):
            return float(tok)
        return self.env[tok]


def run_script(inputs, defs, args):
    env = dict(zip(inputs, args))
    out = None
    for kind, name, rhs in defs:
        ev = Eval(tokenize(rhs), env)
        env[name] = ev.expr()
        assert ev.peek() is None, f"unparsed tail in {name}: {ev.t[ev.i:]}"
        if kind == "plot":
            out = env[name]
    return out


def random_bars(rng):
    """Prior/current OHLC pairs with deliberate ties on every boundary."""
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


@pytest.mark.skipif(not STUDIES, reason="no thinkorswim study file")
@pytest.mark.parametrize("path", STUDIES)
@pytest.mark.parametrize("m_idx", range(4))
def test_tos_classifier_matches_grammar(path, m_idx):
    inputs, defs = extract_script(path)
    rng = random.Random(1000 + m_idx)
    for _ in range(10_000):
        ph, pl, o, h, l, c = random_bars(rng)
        code = run_script(inputs, defs, [ph, pl, o, h, l, c, float(m_idx), True])
        expected = classify(Candle(o, h, l, c), Candle(pl, ph, pl, ph), method=METHODS[m_idx]).notation()
        assert CODES[int(code)] == expected, (ph, pl, o, h, l, c, METHODS[m_idx])


@pytest.mark.skipif(not STUDIES, reason="no thinkorswim study file")
@pytest.mark.parametrize("path", STUDIES)
def test_tos_classifier_f2_detection_off(path):
    inputs, defs = extract_script(path)
    rng = random.Random(7)
    for _ in range(5_000):
        ph, pl, o, h, l, c = random_bars(rng)
        code = run_script(inputs, defs, [ph, pl, o, h, l, c, 0.0, False])
        expected = classify(Candle(o, h, l, c), Candle(pl, ph, pl, ph)).notation().lstrip("F")
        assert CODES[int(code)] == expected


@pytest.mark.skipif(not STUDIES, reason="no thinkorswim study file")
@pytest.mark.parametrize("path", STUDIES)
def test_tos_classifier_no_data(path):
    inputs, defs = extract_script(path)
    nan = float("nan")
    assert run_script(inputs, defs, [nan, nan, 1.0, 2.0, 0.5, 1.5, 0.0, True]) == 0
    assert run_script(inputs, defs, [2.0, 1.0, 1.0, 0.5, 1.0, 1.0, 0.0, True]) == 0  # high below low


# AggregationPeriod constants in milliseconds (thinkScript reference). Only their order matters.
AGG = {
    "MIN": 60_000, "TWO_MIN": 120_000, "THREE_MIN": 180_000, "FOUR_MIN": 240_000,
    "FIVE_MIN": 300_000, "TEN_MIN": 600_000, "FIFTEEN_MIN": 900_000, "TWENTY_MIN": 1_200_000,
    "THIRTY_MIN": 1_800_000, "HOUR": 3_600_000, "TWO_HOURS": 7_200_000, "FOUR_HOURS": 14_400_000,
    "DAY": 86_400_000, "TWO_DAYS": 172_800_000, "THREE_DAYS": 259_200_000, "FOUR_DAYS": 345_600_000,
    "WEEK": 604_800_000, "MONTH": 2_592_000_000, "QUARTER": 7_776_000_000, "YEAR": 31_536_000_000,
}
AGG_ENV = {"AggregationPeriod." + k: float(v) for k, v in AGG.items()}
# Pine timeframe strings to thinkorswim. 720 (12H) has no thinkorswim aggregation.
PINE_TF = {"5": "FIVE_MIN", "15": "FIFTEEN_MIN", "30": "THIRTY_MIN", "60": "HOUR", "240": "FOUR_HOURS",
           "720": None, "D": "DAY", "W": "WEEK", "M": "MONTH", "3M": "QUARTER", "12M": "YEAR"}
PRESETS = ["TheStrat Classic", "Scalp", "Day Trade", "Futures/Crypto", "Swing Trade", "Investing"]


def extract_defs(path):
    """Top-level `def name = expr;` statements, outside script blocks, in order."""
    src = re.sub(r"script\s+\w+\s*\{.*?\n\}", "", open(path).read(), flags=re.S)
    src = re.sub(r"#[^\n]*", "", src)
    return re.findall(r"^def\s+(\w+)\s*=\s*(.*?);", src, re.M | re.S)


def pine_presets():
    """Slot timeframes and enables per preset, read from the Pine's rTF/rEN lines."""
    src = open(PINE).read()
    tfs, ens = {}, {}
    for n in range(1, 7):
        line = re.search(rf"^rTF{n} = usePreset \? \((.*) : tf{n}\) : tf{n}\s*$", src, re.M).group(1)
        tfs[n] = dict(re.findall(r'tfPreset == "([^"]+)" \? "([^"]+)"', line))
        en = re.search(rf"^rEN{n} = usePreset \? (.*?)\s*: tf{n}Enabled\s*$", src, re.M).group(1)
        ens[n] = {p: en == "true" or (en.startswith("(") and f'"{p}"' in en) for p in PRESETS}
    return tfs, ens


def resolve(path, p, chart_agg, custom=None, custom_en=False):
    env = dict(AGG_ENV, p=float(p), chartAgg=float(chart_agg))
    for n in range(1, 7):
        env[f"tf{n}"] = float(AGG[(custom or {}).get(n, "DAY")])
        env[f"tf{n}Enabled"] = custom_en
    env["yes"], env["no"] = True, False
    wanted = re.compile(r"^(chartStd|raw\d|en\d|lower\d|a\d)$")
    for name, rhs in extract_defs(path):
        if wanted.match(name):
            ev = Eval(tokenize(rhs), env)
            env[name] = ev.expr()
            assert ev.peek() is None, f"unparsed tail in {name}"
    return env


CHARTS = list(AGG.values()) + [2_700_000, 10_800_000]  # plus 45m and 3H custom charts


@pytest.mark.skipif(not STUDIES, reason="no thinkorswim study file")
@pytest.mark.parametrize("path", STUDIES)
def test_tos_presets_match_pine(path):
    tfs, ens = pine_presets()
    for k, name in enumerate(PRESETS, start=1):
        env = resolve(path, k, AGG["MIN"])
        for n in range(1, 7):
            tos = PINE_TF[tfs[n][name]]
            if tos is None:  # 12H: thinkorswim has no such aggregation, so the slot is off
                assert not env[f"en{n}"], (name, n)
                continue
            assert env[f"raw{n}"] == AGG[tos], (name, n)
            assert bool(env[f"en{n}"]) == ens[n][name], (name, n)


@pytest.mark.skipif(not STUDIES, reason="no thinkorswim study file")
@pytest.mark.parametrize("path", STUDIES)
def test_tos_slot_clamp(path):
    """A slot never asks for a period below the chart's, or for a non-standard one."""
    standard = set(float(v) for v in AGG.values())
    custom = {1: "TWO_MIN", 2: "TEN_MIN", 3: "TWO_HOURS", 4: "THREE_DAYS", 5: "QUARTER", 6: "YEAR"}
    for chart in CHARTS:
        for p in range(0, 7):
            env = resolve(path, p, chart, custom=custom, custom_en=True)
            for n in range(1, 7):
                raw, a, lower = env[f"raw{n}"], env[f"a{n}"], env[f"lower{n}"]
                assert lower == (raw < chart)
                assert a >= chart and a in standard, (p, chart, n, a)
                if not lower:
                    assert a == raw
            if p == 0:
                assert all(env[f"raw{n}"] == AGG[custom[n]] for n in range(1, 7))
                assert all(env[f"en{n}"] for n in range(1, 7))
