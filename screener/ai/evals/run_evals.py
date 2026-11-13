#!/usr/bin/env python3
"""Setup Screener AI evals: does the model's answer match what the screener returned?

The screener is the answer key. Each case in cases.jsonl names a fixture (a
captured or synthetic Setup Screener result), a user question, regexes the
answer MUST match and regexes it must NOT match. The must_not lists carry the
guardrails: no trade calls, no odds, no orders, no states for timeframes the
screen did not read.

Model-agnostic: grading needs no model at all, and `run` works with any
provider.

  grade:  python3 run_evals.py grade answers.jsonl
          answers.jsonl has one {"id": ..., "answer": ...} per line, from any
          model or harness. Prints pass/fail per case and the total.

  run:    python3 run_evals.py run --provider P --model M [--out answers.jsonl]
          Sends each case to a model: SKILL.md as the system prompt, the
          decoded screener readout and the question as the user turn. Then
          grades. Providers:
            anthropic  Anthropic API (anthropic package, ANTHROPIC_API_KEY)
            openai     any OpenAI-compatible chat endpoint (openai package,
                       OPENAI_API_KEY; --base-url for Gemini, OpenRouter,
                       a local Ollama server, ...)
            command    any CLI: --cmd "my-model-cli" gets the system prompt and
                       user turn on stdin, and its stdout is the answer
          This spends money on the paid providers: 14 short requests.

  show:   python3 run_evals.py show
          Prints what the model sees for each case.

`run` tests the explaining half (reading the result and answering inside the
lines), not the tool calling. The live call path is covered by the fixtures,
which are real connector output.
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


def prompt_for(case: dict) -> str:
    fx = load_fixture(case["fixture"])
    return (
        f"Screener result for {fx['symbol']} on the {fx['screen']} screen "
        f"(run_pine_indicator, TheStrat Setup Screener, default slots 30m,1H,D,W,M,Q), decoded:\n\n"
        f"{tool_text(fx)}\n\nUser question: {case['question']}"
    )


def ask_anthropic(model: str, system: str, user: str, **_) -> tuple[str, str]:
    import anthropic

    client = anthropic.Anthropic()
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
        return "[refused]", resp.model
    return "".join(b.text for b in resp.content if b.type == "text"), resp.model


def ask_openai(model: str, system: str, user: str, base_url: str | None = None, **_) -> tuple[str, str]:
    from openai import OpenAI

    client = OpenAI(base_url=base_url) if base_url else OpenAI()
    resp = client.chat.completions.create(
        model=model,
        messages=[{"role": "system", "content": system}, {"role": "user", "content": user}],
    )
    return resp.choices[0].message.content or "", resp.model


def ask_command(model: str, system: str, user: str, cmd: str | None = None, **_) -> tuple[str, str]:
    import shlex
    import subprocess

    if not cmd:
        raise SystemExit("--provider command needs --cmd")
    out = subprocess.run(shlex.split(cmd), input=f"{system}\n\n---\n\n{user}", capture_output=True, text=True, check=True)
    return out.stdout.strip(), model or cmd


PROVIDERS = {"anthropic": ask_anthropic, "openai": ask_openai, "command": ask_command}


def run(provider: str, model: str, out: Path, cases: list[dict], **opts) -> dict[str, str]:
    ask = PROVIDERS[provider]
    system = SKILL.read_text()
    answers: dict[str, str] = {}
    for c in cases:
        text, used = ask(model, system, prompt_for(c), **opts)
        answers[c["id"]] = text
        with out.open("a") as f:
            f.write(json.dumps({"id": c["id"], "provider": provider, "model": used, "answer": text}) + "\n")
    return answers


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("grade")
    g.add_argument("answers")
    r = sub.add_parser("run")
    r.add_argument("--provider", required=True, choices=sorted(PROVIDERS))
    r.add_argument("--model", default="", help="model id for the provider (anthropic defaults to claude-opus-5-5)")
    r.add_argument("--base-url", default=None, help="openai provider: an OpenAI-compatible endpoint")
    r.add_argument("--cmd", default=None, help="command provider: the CLI to call")
    r.add_argument("--out", default="answers.jsonl")
    sub.add_parser("show", help="print each case's question and the readout the model sees")
    a = ap.parse_args(argv)
    cases = load_cases()
    if a.cmd == "show":
        for c in cases:
            print(f"--- {c['id']}\n{prompt_for(c)}\n")
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
    model = a.model or ("claude-opus-5-5" if a.provider == "anthropic" else "")
    if a.provider == "openai" and not model:
        raise SystemExit("--provider openai needs --model")
    return grade(run(a.provider, model, out, cases, base_url=a.base_url, cmd=a.cmd), cases)


if __name__ == "__main__":
    raise SystemExit(main())
