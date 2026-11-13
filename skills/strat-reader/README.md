# Strat Reader (Claude skill, prototype)

Ask Claude for a TheStrat read of any symbol, with no chart open: "what is SPY doing on the daily?", "any 2-1-2s in my watchlist?", "is QQQ in full continuity?".

It works by running **TheStrat Setup Screener** (published open source on TradingView) through TradingView's own Claude connector. The script does all the classification; Claude only reads the numbers back and explains them. Nothing here computes a bar type.

## Where the data comes from

From the user's own TradingView account. The connector runs the script "under the user's TradingView plan and exchange subscriptions": real time where they pay for the exchange data, delayed otherwise, regular session only. No data passes through us, so there is nothing to license or redistribute.

## What you need

- Claude with the TradingView connector turned on (in beta as of 2026-11).
- This skill installed (`SKILL.md`). `decode.py` is optional: Claude uses it when it can run code, otherwise it reads the table in `SKILL.md`.

## What it can and can't do

| Can | Can't |
|---|---|
| Read any symbol TradingView serves, on screens from 1m to M, with D/W/M/Q context | Scan a whole market: the TradingView screener tool only filters built-in columns, so each symbol is one script run |
| FTFC, signals in force, Lead, nearest setup, Domino, hammer/shooter, failing 2s | Read history: the screener plots only the last bar, by design (anything older would repaint) |
| Explain what must happen next from the bar-type rules | Say whether a setup will work. The states are descriptions, not a tested edge |
| | Place orders. Read-only |

Cost: each script run returns about 10k tokens, because the script's settings come back with every call. A 10-symbol watchlist is about 100k tokens.

The published Suite (v3.1.1) and the FTFC vs In Force study do **not** run through the connector: both fail on a color input ("Invalid value #FFFFFF"), and neither exposes its states as plots. The Setup Screener does both, which is why the skill uses it.

## Evals

`evals/` checks whether a model's answers match what the screener returned. The screener is the answer key.

- `fixtures/`: three real connector results (SPY 1D, QQQ 30m, NVDA 1D, captured 2026-11-13) and three synthetic ones (a failing 2d, FTFC up, a Domino).
- `cases.jsonl`: 14 questions, each with patterns the answer must contain and patterns it must not (the guardrails: no trade calls, no odds or win rates, no orders, no states for a timeframe the screen did not read).
- `run_evals.py grade answers.jsonl` grades any model's answers. `run_evals.py run` sends the cases to Claude and grades the replies (needs an Anthropic API credential; 14 short requests).

The grader is regex based, so it catches the clear failures (a wrong state, a trade call) and can miss a subtly wrong answer. It is a floor, not a full review.

```bash
python3 -m pytest skills/strat-reader/tests      # decoder + grader tests
python3 skills/strat-reader/evals/run_evals.py show   # what the model sees per case
```

## Status

Prototype on a branch. Not yet run against a model end to end, and not yet tried in a real Claude conversation with the connector.
