# TheStrat: Full Timeframe Continuity (FTFC) vs Signals In Force

**On TradingView (open source):** [TheStrat: Full Timeframe Continuity (FTFC) vs Signals In Force](https://www.tradingview.com/script/1EMdC3R7-TheStrat-Full-Timeframe-Continuity-FTFC-vs-Signals-In-Force/)

A companion study to TheStrat Suite. It puts two questions side by side on every bar, across the Hour, Day, Week and Month (plus optional 15m and 30m), and colors your candles by whether the answers agree:

1. **Full Timeframe Continuity (FTFC):** is every timeframe trading on the same side of its open?
2. **All In Force:** does every timeframe have a Strat signal that has triggered and is still holding, all pointing the same way? A timeframe with nothing in force (an inside candle, a break with no qualifying setup, or a signal that lost its trigger) breaks it. Signal type never matters, only direction.

They are often treated as the same thing. They are not. FTFC only asks where price sits against each period's open; In Force asks whether a real setup has triggered on each timeframe.

## Install

- **From TradingView:** open the listing above and click *Add to favorites*, then add it from Indicators > Favorites.
- **From source:** open the Pine Editor, paste [`FTFC_vs_InForce_v0.3.3.pine`](FTFC_vs_InForce_v0.3.3.pine), and click *Add to chart*. It imports the published `SpinTrades/TheStratGrammar/1` library.

Use a 1-hour or lower chart so all four timeframes count. For the cleanest look, hide the chart's own candles (Chart Settings > Symbol: uncheck Body, Borders and Wick).

## Reading it

**Candles (default: Agreement)**

| Candle | Meaning |
|---|---|
| Bright green / red | FTFC and All In Force agree, bull / bear |
| Faded green / red | FTFC only: every timeframe on the same side of its open, but not every one has a signal in force |
| Blue / purple | All In Force only: every timeframe in force up / down, but price not on the same side of every open |
| Hollow gray | Neither |

Other candle modes: *Agreement, Solid*, *Agreement, Outlines*, *Body = FTFC, Outline = In Force*, *FTFC Only*, *In Force Only*, *Off*.

**Heatmap pane.** Top rows: FTFC and ALL IF per bar. Then one band per timeframe (M, W, D, H, plus 30m and 15m when on): top half above (green) or below (red) its open, bottom half its signal in force (green bull, red bear, gray none). When a summary row goes dim, the gray or opposite band is the timeframe holding it back. A faded top half means that timeframe's period began on the same bar as a lower timeframe's (Monday: Week = Day; first trading day: Month = Day; first hour: Day = Hour), so the two share one opening print and are not independent confirmation.

**Optional layers (off by default).** *Score* (green columns above zero = timeframes above their open, red below zero = timeframes below it; white step lines = timeframes in force, bull above zero and bear below, counted separately so they never cancel), *Cascade Ladder* (H/D/W/M letters, ¼ for 15m and ½ for 30m, where each timeframe goes in force, stacked so the trigger order is visible; the most recent 500 marks; letter size Tiny to Huge), and a *Stats Table* (how often each state occurred over the loaded history, including FTFC resting on shared opens).

Full definitions of every term and signal are in the listing description, kept here as [`TV_DESCRIPTION.txt`](TV_DESCRIPTION.txt). v0.3.2 changes one of them: an outside bar (3) now counts as in force toward the side of C1 it closes beyond. See [`TV_RELEASE_NOTES_v0.3.3.txt`](TV_RELEASE_NOTES_v0.3.3.txt).

**Alerts (v0.3.3).** One *Any alert() function call* alert fires when a chosen timeframe goes in force (the ladder's events, whether or not the ladder is shown), with optional Full Agreement alerts. Bar close by default; Intrabar is optional and can fire on a move that does not hold.

## Repainting

None on closed bars. Each timeframe's forming candle is rebuilt from the chart's own candles (running high/low since the period opened, the chart close as its price), and C1, C2 and the period open come from completed or period-stable data under `lookahead_on`. Every historical bar is graded as things stood at that bar's close.

## Maintenance

The in-force test in `evalSlot` mirrors `computeSignalState` in `pine/TheStratSuite_v3.1.1.pine` (FTFC filters, HAM/SHO filters and Exhaustion Disables In Force left out). A change to the Suite's in-force rules should be made here too.

One deliberate difference since v0.3.2: an outside bar (3) is in force toward the side of C1 it closes beyond. The Suite's `computeSignalState` only lets a 3 through on the hammer/shooter path, while the Suite's timeframe table (FIX TFCOLOR-1) already treats an enabled 3 expansion as in force. If the Suite settles that inconsistency, match it here.

## Versions

| Version | Date | Notes |
|---|---|---|
| v0.3.3 | 2026-11-08 | Ladder Size (Tiny to Huge) and Spacing, alerts when a timeframe goes in force. From a TradingView user request. Awaiting TradingView compile test |
| v0.3.2 | 2026-11-05 | Outside bars count as in force, score split into bull and bear, shared opens faded in the heatmap, optional 15m/30m. Not published on its own; ships with v0.3.3 |
| v0.3.1 | 2026-11-05 | Published. Agreement candles (blue/purple split, hollow for neither), heatmap pane; score, ladder and stats off by default |
| v0.3.0 | 2026-11-05 | Agreement colors as hollow/outline candles; stats moved to the price chart |
| v0.2.0 | 2026-11-05 | Two-channel candles, cascade ladder, score pane |
| v0.1.0 | 2026-11-05 | First version: agreement candle colors and heatmap |

Source: Mozilla Public License 2.0 (see the repository `LICENSE`).
