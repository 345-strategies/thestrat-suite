#!/usr/bin/env python3
"""Strat Reader evals: does the model's answer match what the screener returned?

The screener is the answer key. Each case in cases.jsonl names a fixture (a
captured or synthetic Setup Screener result), a user question, regexes the
answer MUST match and regexes it must NOT match. The must_not lists carry the
skill's guardrails: no trade calls, no odds, no orders, no states for
timeframes the screen did not read.

Two modes:

  grade:  python3 run_evals.py grade answers.jsonl
          answers.jsonl has one {"id": ..., "answer": ...} per line, from any
          model or harness. Prints pass/fail per case and the total. No API.

  run:    python3 run_evals.py run [--model claude-opus-5-5] [--out answers.jsonl]
          Sends each case to Claude: SKILL.md as the system prompt, the decoded
          screener readout as the tool result, the question as the user turn.
          Then grades. Needs the anthropic package and a credential
          (ANTHROPIC_API_KEY or an `ant auth login` profile). This spends money:
          14 short requests.

`run` tests the explaining half of the skill (reading the result and answering
inside the lines), not the tool calling. The live call path is covered by the
fixtures, which are real connector output.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SKILL = HERE.parent / "SKILL.md"
sys.path.insert(0, str(HERE.parent))
import decode  # noqa: E402


def load_cases() -> list[dict]:
    return [json.loads(l) for l in (HERE / "cases.jsonl").read_text().splitlines() if l.strip()]


def load_fixture(name: str) -> dict:
    return json.loads((HERE / "fixtures" / f"{name}.json").read_text())


def tool_text(fx: dict) -> str:
    d = decode.decode(fx["plots"], fx["screen"])
    return decode.readout(d, fx["symbol"])


def grade_one(case: dict, answer: str) -> list[str]:
    """Empty list = pass; otherwise the reasons it failed."""
    why = []
    for pat in case.get("must", []):
        if not re.search(pat, answer, re.IGNORECASE):
            why.append(f"missing /{pat}/")
    for pat in case.get("must_not", []):
        m = re.search(pat, answer, re.IGNORECASE)
        if m:
            why.append(f"forbidden /{pat}/ matched {m.group(0)!r}")
    return why


def grade(answers: dict[str, str], cases: list[dict]) -> int:
    passed = 0
    for c in cases:
        a = answers.get(c["id"])
        if a is None:
            print(f"SKIP {c['id']}: no answer")
            continue
        why = grade_one(c, a)
        if why:
            print(f"FAIL {c['id']}: " + "; ".join(why))
        else:
            passed += 1
            print(f"PASS {c['id']}")
    print(f"\n{passed}/{len(cases)} passed")
    return 0 if passed == len(cases) else 1


def run(model: str, out: Path, cases: list[dict]) -> dict[str, str]:
    import anthropic  # only needed for `run`

    client = anthropic.Anthropic()
    system = SKILL.read_text()
    answers: dict[str, str] = {}
    for c in cases:
        fx = load_fixture(c["fixture"])
        user = (
            f"Screener result for {fx['symbol']} on the {fx['screen']} screen "
            f"(run_pine_indicator, TheStrat Setup Screener, default slots 30m,1H,D,W,M,Q), decoded:\n\n"
            f"{tool_text(fx)}\n\nUser question: {c['question']}"
        )
        resp = client.beta.messages.create(
            model=model,
            max_tokens=16000,
            output_config={"effort": "low"},
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
            system=system,
            messages=[{"role": "user", "content": user}],
        )
        if resp.stop_reason == "refusal":
            text = "[refused]"
        else:
            text = "".join(b.text for b in resp.content if b.type == "text")
        answers[c["id"]] = text
        with out.open("a") as f:
            f.write(json.dumps({"id": c["id"], "model": resp.model, "answer": text}) + "\n")
    return answers


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("grade")
    g.add_argument("answers")
    r = sub.add_parser("run")
    r.add_argument("--model", default="claude-opus-5-5")
    r.add_argument("--out", default="answers.jsonl")
    sub.add_parser("show", help="print each case's question and the readout the model sees")
    a = ap.parse_args(argv)
    cases = load_cases()
    if a.cmd == "show":
        for c in cases:
            print(f"--- {c['id']}: {c['question']}\n{tool_text(load_fixture(c['fixture']))}\n")
        return 0
    if a.cmd == "grade":
        answers = {}
        for l in Path(a.answers).read_text().splitlines():
            if l.strip():
                row = json.loads(l)
                answers[row["id"]] = row["answer"]
        return grade(answers, cases)
    out = Path(a.out)
    out.write_text("")
    return grade(run(a.model, out, cases), cases)


if __name__ == "__main__":
    raise SystemExit(main())
