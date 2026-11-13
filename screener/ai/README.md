# TheStrat Setup Screener for AI assistants (prototype)

Ask your AI assistant for a TheStrat read of any symbol, with no chart open: "what is SPY doing on the daily?", "any 2-1-2s in my watchlist?", "is QQQ in full continuity?".

It works by running **TheStrat Setup Screener** (published open source on TradingView) through TradingView's MCP connector. The script does all the classification. The model only reads the numbers back and explains them, so nothing here computes a bar type, and the answer is the same whichever model reads it.

## Where the data comes from

From the user's own TradingView account. The connector runs the script "under the user's TradingView plan and exchange subscriptions": real time where they pay for the exchange data, delayed otherwise, regular session only. No data passes through us, so there is nothing to license or redistribute.

## What you need

- A TradingView plan that includes MCP access (Essential or above; trials and free accounts are excluded, per third-party setup guides as of 2026-11).
- An AI app that can add TradingView's connector, `https://mcp.tradingview.com/mcp`. It is a standard MCP server (streamable HTTP with OAuth), so it is not tied to one AI company. Setup guides name Claude, ChatGPT and Codex; any client that supports remote MCP with OAuth should work.
- The connector's `run_pine_indicator` tool. This is the one requirement to check: we confirmed it in Claude on 2026-11-13, and some guides written earlier in the beta list no Pine tools at all, so the tool set may differ by app or by rollout. If your app's connector has no `run_pine_indicator`, this can't work there yet.
- `SKILL.md` given to the assistant as its instructions. It is plain Markdown:
  - Claude: add the folder as a skill.
  - Apps that read `SKILL.md` or `AGENTS.md` (Codex and other coding agents): put it where the app looks for them.
  - ChatGPT and anything else: paste `SKILL.md` into the project or custom instructions.
- `decode.py` is optional. An assistant that can run code uses it; otherwise it reads the table in `SKILL.md`.

## What it can and can't do

| Can | Can't |
|---|---|
| Read any symbol TradingView serves, on screens from 1m to M, with D/W/M/Q context | Scan a whole market: the TradingView screener tool only filters built-in columns, so each symbol is one script run. Use the Pine Screener (TradingView Premium) for index-wide scans |
| FTFC, signals in force, Lead, nearest setup, Domino, hammer/shooter, failing 2s | Read history: the screener plots only the last bar, by design (anything older would repaint) |
| Explain what must happen next from the bar-type rules | Say whether a setup will work. The states are descriptions, not a tested edge |
| | Place orders. Read-only |

Cost, measured 2026-11-13 (token counts vary a little by model's tokenizer):

| Mode | What it returns | Tokens |
|---|---|---|
| Setup Screener, one symbol | Full Suite read: signals, Lead, magnitude, nearest setup, 6 slots | about 5k per symbol (37 settings and 64 plot names come back each call) |
| Watchlist template (`pine/watchlist_template.pine`, run as `code`) | D/W/M bar states, combos, failing 2s, FTFC D+W+M | about 1.5k for up to 21 symbols in one call, input and output together |

The instructions scan with the watchlist template and use the screener only for the few symbols worth a deep read. The template ran 21 symbols (63 requests) in one call, and its D/W/M reads matched the screener's on SPY, QQQ and NVDA.

The published Suite (v3.1.1) and the FTFC vs In Force study do **not** run through the connector: both fail on a color input ("Invalid value #FFFFFF"), and neither exposes its states as plots. The Setup Screener does both, which is why this uses it.

## Evals

`evals/` checks whether a model's answers match what the screener returned. The screener is the answer key, so any model can be graded the same way.

- `fixtures/`: three real connector results (SPY 1D, QQQ 30m, NVDA 1D, captured 2026-11-13), a real watchlist run, and three synthetic ones (a failing 2d, FTFC up, a Domino).
- `cases.jsonl`: 14 questions, each with patterns the answer must contain and patterns it must not (the guardrails: no trade calls, no odds or win rates, no orders, no states for a timeframe the screen did not read).
- `run_evals.py grade answers.jsonl` grades answers from any model or harness.
- `run_evals.py run --provider anthropic|openai|command` sends the cases to a model and grades the replies. `openai` takes `--base-url` for any OpenAI-compatible endpoint (Gemini, OpenRouter, a local Ollama). `command` pipes each prompt to any CLI.

The grader is regex based, so it catches the clear failures (a wrong state, a trade call) and can miss a subtly wrong answer. It is a floor, not a full review.

```bash
python3 -m pytest screener/ai/tests                     # decoder + grader tests
python3 screener/ai/evals/run_evals.py show             # what the model sees per case
python3 screener/ai/evals/run_evals.py run --provider openai --model gpt-5 --out gpt.jsonl
```

## Status

Prototype on a branch. Not yet tried in a real conversation with the connector outside this build session.

Model runs so far:

| Date | Model | Result | File |
|---|---|---|---|
| 2026-11-13 | Claude (a fresh agent given only `SKILL.md` and the readouts, no sight of the grader) | 14/14 after one grader fix: the 30m case's pattern was too strict for a correct answer | `evals/runs/2026-11-13_claude_blind.jsonl` |

No other model has been run yet. That needs an API key for each provider, or a paste of the 14 prompts (`run_evals.py show`) into each app.
