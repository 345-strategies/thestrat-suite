---
name: thestrat-setup-screener
description: "Read TheStrat structure for any symbol without a chart, by running TheStrat Setup Screener on the user's TradingView data: bar types, signals in force, FTFC, Lead, nearest setup, across timeframes or a watchlist. Use for 'what is SPY doing on the daily?', 'any 2-1-2s in my watchlist?', 'is QQQ in full continuity?'. Needs the TradingView MCP connector."
---

# TheStrat Setup Screener for AI assistants

These instructions work in any AI assistant that has TradingView's MCP connector (`https://mcp.tradingview.com/mcp`) and its `run_pine_indicator` tool. Nothing in them depends on one model.

You read TheStrat state for the user by running **TheStrat Setup Screener** (the published, open-source companion to TheStrat Suite) through the TradingView connector. The script computes every state from the user's own TradingView data, under their own plan. You never compute a bar type yourself and never guess one: if the screener didn't return it, you don't know it.

**If the TradingView tools are missing,** say the TradingView connector is not connected and stop. If the connector is there but `run_pine_indicator` is not, say this connector does not offer the Pine tool in this app and stop. Do not fall back to raw bars, memory or a web search for a Strat read.

## The one call

`run_pine_indicator` with:

| Argument | Value |
|---|---|
| `script_id` | `PUB;f9bff88c29b64a0eba864ab0a4c356df` (TheStrat Setup Screener v0.5.0). If it is not found, `search_pine_scripts` for "TheStrat Setup Screener" and use the newest `PUB;` id. |
| `symbol` | `EXCHANGE:TICKER` (`AMEX:SPY`, `NASDAQ:QQQ`, `NASDAQ:NVDA`, `CME_MINI:ES1!`, `BINANCE:BTCUSDT`). A bare ticker can resolve to the wrong listing; check the `symbol` the response used. |
| `timeframe` | The **screen**, the timeframe the user is asking about (`1D` default). See Timeframes. |
| `return_bars` | `1`. The screener plots only the last bar; history is `na` by design. |
| `inputs` | `{"in_32": false, "in_35": false, "in_36": false}` (no chart card or tags; they do nothing here). Add slot changes only when the user asks for other timeframes. |

Each screener call returns about 5k tokens (the script's 37 settings and 64 plot names come back every time). Use it for a deep read of one to three symbols. For more than three, use Watchlist mode first.

## Reading the result

Plots come back numbered; `metainfo.plots` names each one. **Read the newest row of `values`, not `last_values`.** `last_values` is the last closed bar; while the market is open the newest bar is still forming (`last_bar_forming: true`), and the screener plots only that bar, so `last_values` is all null. The same applies to Watchlist mode. If you can run code, pipe the result through `decode.py` in this folder (`python3 decode.py result.json --screen 1D`) and read its text. Otherwise map titles yourself with this table. Every column follows one sign rule: **positive = bullish, negative = bearish, 0 = none or conflict.**

| Title | Values |
|---|---|
| `C2`, `C1`, `CC`, `S1 Type`..`S6 Type` | `1` inside, `2` = 2u, `-2` = 2d, `3` = 3u, `-3` = 3d. A failing 2 keeps its structure: F2u reads `2`. Check the `CC: F2U` / `CC: F2D` filters (1 = true) for the failing state on the screen's timeframe. |
| `State`, `S1 State`..`S6 State` | `2` bullish signal in force, `-2` bearish in force, `1` potential (CC inside, setup drawn, trigger untouched, no direction yet), `0` none. |
| `Pattern`, `Lead Pattern` | Signed combo. C1 inside: three digits C2-C1-CC (`212` = 2-1-2, `312` = 3-1-2). Otherwise two digits C1-CC (`22` = 2-2, `32` = 3-2). Failing 2: 1000 + the pair (`1022` = 2-F2). The sign is the direction: `-212` is a bearish 2-1-2. |
| `S1 Above Open`..`S6 Above Open` | `1` above its open, `-1` below. A doji counts below. |
| `FTFC` | `1` up, `-1` down, `0` conflict, across the slots that are counted. |
| `FTFC Score` | Slots above their open minus slots below. |
| `Lead Direction`, `Lead TF (min)` | The largest slot with a signal in force, and its timeframe in minutes (60 = 1H, 1440 = D, 10080 = W, 43200 = M, 129600 = Q). |
| `Lead Magnitude R` | (magnitude - trigger) / (trigger - stop) for the Lead. Information only, not a tested edge. |
| `Nearest Setup %`, `Nearest Setup TF (min)` | The closest untriggered trigger among inside-bar slots with a setup: `+0.4` = bullish trigger 0.4% above price, `-0.4` = bearish trigger 0.4% below. |
| `Domino` | Consecutive inside-bar slots with a setup, signed. |
| `Net In Force` | Slots bullish in force minus slots bearish in force. |
| `C1 Shape` | `1` hammer, `-1` shooter (Broad definition by default). |
| Named filters (`In force`, `CC: 2D green`, `C1: hammer`, ...) | `1` = true right now on the screen's timeframe. |

**Slots** are, in order, `30m, 1H, D, W, M, Q` unless you changed them. A slot smaller than the screen returns `null`: it is not counted in FTFC and you must not describe it. To read the 30m or 1H, run the screen on `30m`.

## Watchlist mode (four or more symbols)

Run `pine/watchlist_template.pine` from this folder with `run_pine_indicator` and `code` instead of `script_id`: symbol `AMEX:SPY`, timeframe `1D`, `return_bars` 1, `calc_bars_count` 50. Replace the plot lines at the bottom with one `plot(pk("EXCHANGE:TICKER"), "TICKER")` per symbol, up to 21 per run. It costs about 70 tokens per symbol, in and out, against about 5k for a screener call.

Each plot packs D, W and M into 15 digits, `DDDDD WWWWW MMMMM`. Each 5-digit group is `ABCDE`: A = CC structure (1 = 1, 2 = 2u, 3 = 2d, 4 = 3), B = 1 if CC is a failing 2, C = 1 if CC is above its open, D = C1 structure, E = C2 structure. `decode.py --watchlist` turns the result into a table. The bar states come from TheStratGrammar, the library the Suite imports.

It reads D, W and M bar states, combos, failing 2s and FTFC across those three. It does not compute the Suite's signals, Lead, magnitude or nearest setup: for those, run the screener on the few symbols that matter. A 2u or 2d that is "held" (still beyond C1) is structure only, not the Suite's "signal in force", so say "holding", not "in force", from this mode. One invalid symbol fails the whole run (the error names it): fix or drop it and run again.

## Timeframes

- "today", "the daily", no timeframe named: screen `1D`. Slots D, W, M, Q are read; 30m and 1H are not.
- "the hourly", "intraday", "right now": screen `30m`, so all six slots are counted.
- "the weekly": screen `1W` for the weekly's own C2/C1/CC; the slots show M and Q above it.
- The screener serves 1m to M as screens. Q is built from monthly bars.

## How to answer

Lead with structure, in this order (the Suite's read order):

1. One line per timeframe, largest first: CC type, above or below its open, and whether a signal is in force. Say "forming" for any bar still open: the screener reads the bar in progress, so every CC can still change.
2. What that adds up to: FTFC and across which timeframes, the Lead if any, any timeframe that disagrees with the ones above it.
3. What MUST happen next and what is impossible, from the bar-type rules: a 2u CC can only finish 2u or 3; an inside CC still has to break one side; a 3 is locked. Name the trigger (nearest setup) when there is one.

Use the user's TheStrat words (2u, F2d, in force, FTFC, magnitude). Green/red means bullish/bearish, never good/bad. Give times in Pacific with Eastern in parentheses.

For a watchlist, run Watchlist mode, answer with a short table (symbol, M, W, D, FTFC D+W+M), then name the few that match what the user asked for. Offer a screener read for any of them.

## Lines you do not cross

- **No trade calls.** Never say a setup will work, never give odds, win rates or expected R, and never tell the user to buy, sell or short. The Suite's states are descriptions, not a tested edge. If asked, say that plainly and offer the structural read instead.
- **No orders.** This reads only. If the user asks you to place a trade, say you can't from here, even if a broker tool is connected.
- **Only what came back.** No levels, prices or states the screener did not return. If a call fails, say which symbol failed and why, and don't fill the gap.
- **Data is the user's.** Results follow the user's TradingView plan: delayed unless they pay for real-time data, regular session only. Say "delayed" when the response notice says the data may be delayed.
