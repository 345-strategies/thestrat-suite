# The Strat Stats (NQ), by @notprofgreen

[@notprofgreen](https://x.com/notprofgreen) published **The Strat Stats**, a descriptive study of TheStrat setups on NQ futures, 2019-06 to 2025-03, on 15m through 8h charts, measured on 1-minute data:

**[The Strat Stats (NQ)](https://claude.ai/artifact/JQ5Uam7QrbktqqrAd54ZoY)**

It is the cleanest public test of the setups that I know of. Every setup is measured the same way and compared against a baseline of every break on that timeframe, with honest statistics. All credit for the study and its format goes to him.

This page explains how to read it in this repo's vocabulary (`concepts/bar-types.md`).

---

## What one row measures

| Piece | Rule |
|---|---|
| Setup bar | `C1`, the last **closed** bar. The setup is known only once it closes. |
| Combo | `C2` then `C1`: the two closed bars. The trigger bar makes the third token, so a `2d-1` row traded long is the `2d-1-2u` signal. |
| Entry bar | The **next bar** after `C1` closes. |
| Entry | The first trade through `C1`'s high (long) or low (short) during the entry bar: a stop order at the trigger. If the bar opens beyond the trigger, the fill is that open. If it never breaks either side, there is no trade. |
| Both sides in one minute | Dropped, since the order can't be known. |
| Stop | The other side of `C1`, one tick beyond. 1R = entry to stop. |
| Outcome | +1R (and +2R) reached before the stop, within the entry bar plus two more bars, checked on 1-minute bars. The entry minute itself is skipped, and a minute that touches both counts as the stop. |
| Baseline | All breaks on the same timeframe and direction. A row is interesting only when it beats its baseline. |

Equal is not a break: a high that only matches `C1`'s high has not triggered.

## Failed 2s

His grid uses the four structures `1`, `2u`, `2d`, `3`. A Failing 2 (`F2u`/`F2d`) is folded into its `2`.

This repo's definition, for anyone extending the study: a `2u` is an `F2u` when it **closes back inside the prior bar's range**, edges included (prior low <= close <= prior high), and mirror for `F2d`. That is the Reclaim method, the Suite's default (`concepts/bar-types.md`). Only 2s can fail. An outside bar is `3u` if it closes above its own open, otherwise `3d`. There is no F3.

## What it shows

On this trade, almost no setup beats a random break of the prior bar. +1R before the stop lands about 50-53% of the time almost everywhere, close to the baseline. His one setup that passes the statistics is a small negative: 15m `2d-2d` traded long.

I checked it independently. We ran the same method on our own NQ 1-minute data over his dates, and it reproduces his page almost cell for cell (bar counts within 0.1%, setup lifts correlating 0.99). Running it on ES from 2018 gives the same picture.

## What it doesn't show

- It tests a symmetric 1R bracket over three bars. Exits, holding for magnitude or exhaustion targets, and trade management are not tested.
- It doesn't test selection: which setups you skip, where they sit in the bigger picture, or what the higher timeframes are doing beyond simple continuity.
- No commissions or slippage.

So it isn't evidence that TheStrat has no edge. It's evidence that the **combo label alone** doesn't pick direction on this trade. Whatever edge there is has to come from somewhere else: context, targets, or management.
