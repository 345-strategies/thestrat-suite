# Beta testing TheStrat Suite for NinjaTrader 8

Thanks for testing. This is a free, open-source port of TheStrat Suite for TradingView (v3.1.1) to NinjaTrader 8. It was built and tested on Linux against stand-ins for NinjaTrader's types, and **it has not yet been loaded in NinjaTrader itself**. Your job is to find where it fails to compile or load, breaks, or disagrees with the TradingView Suite. Times below are Pacific, with Eastern in parentheses.

This is an indicator only. It never places orders.

## What you need

- NinjaTrader 8 (8.1.x) on Windows. A free account with the Sim101 account and Playback connection is enough.
- Some data: a live or delayed feed, or Market Replay data downloaded under **Tools > Historical Data**.
- Optional but most useful: TradingView with TheStrat Suite v3.1.1 on the same symbol, to compare side by side.

## Install

1. Get `TheStratSuite_NT8_v0.2.0.zip`.
2. In NinjaTrader: **Tools > Import > NinjaScript Add-On**, pick the zip.
3. Open a chart, right-click > **Indicators**, add **TheStratSuite**, click OK.

If the import fails, copy the two `.cs` files from the zip into `Documents\NinjaTrader 8\bin\Custom\Indicators\TheStratSuite\`, open **New > NinjaScript Editor**, and press F5. **A compile error is the most valuable report you can send.** Copy the full error list (file, line, message).

## Test 1: it loads and draws (about 20 minutes)

Use an ES or NQ 5-minute chart, then a SPY 5-minute chart, then any daily chart. In the chart's **Data Series** window set **Days to load** to 90 for the minute charts and 5000 (about 14 years) for the daily chart. NinjaTrader loads the minute timeframes (and, on a daily chart, every timeframe) from the chart's own history, and exhaustion levels need 52 candles of each timeframe.

- The data table appears (top right by default) and every row has bar types in C2, C1 and CC.
- Lines and labels appear near price. Labels are readable.
- Try each **Timeframe Preset** under 01 (for Custom, first turn on the timeframes under 02. Timeframes (Custom preset); they start off, as in the Suite). Every row of the table fills within a few seconds. If the table shows a **Short history** row, it names the timeframes with too little data for exhaustion levels and the setting to raise (Days to load, or **Max Daily Bars To Load** under 15. Advanced). The NinjaScript Output window (New > NinjaScript Output) explains. Report it if the row stays after you raise that setting and reload, and the chart's first bar reaches back that far (about 90 days on the minute charts, about 14 years on the daily chart). If your data starts later, for example a few sessions of Market Replay data or a recently listed symbol, the row cannot clear; that is expected. 12M is never flagged: its exhaustion needs about 52 years of data, which TradingView rarely has either.
- Under **Custom**, the slots also offer 2m, 10m, 45m and 3H. Their table and label names read 2, 10, 45 and 180, as in the TradingView Suite.
- Open the indicator settings. Option lists read like the TradingView Suite ("Pin Bar (Strict)", "Reclaim + Open"), not code names, and picking one sticks. Hovering over a setting's name shows the Suite's tooltip text after about a second.
- Set a color to **Transparent** (for example FTFC Conflict under 11. Bar Coloring with FTFC Candles on). Those candles turn invisible, as on TradingView, rather than white.
- Save the workspace, close and reopen NinjaTrader. Your color and setting changes are still there.
- Load it on a tick, Renko or range chart. No errors, even if it shows little. On a Heiken-Ashi chart it should show only a "Heiken-Ashi charts are not supported" notice: TheStrat needs real candles.

## Test 2: it matches TradingView (about 30 minutes, best after a session close)

Same symbol, same contract month (for ES, compare NinjaTrader's ES 12-26 against TradingView's ESZ2026, not ES1!), same timeframe, same preset, defaults otherwise. Turn on **Show Price in Label** in both.

On the **most recent bar** compare:
- the C2, C1 and CC types in each table row, and the FTFC row;
- each trigger, magnitude and exhaustion price in the labels;
- which lines are drawn, and their colors (green and red mean bull and bear).

**Expected difference:** on older bars the NinjaTrader version shows what you would have seen live at that moment. TradingView's history uses each higher-timeframe candle as it finally closed. So history can legitimately differ; the most recent bar should not.

## Test 3: live behavior (about 30 minutes, live or Market Replay)

- Watch a 1- or 5-minute chart while bars form. The table and lines update with each tick and nothing freezes. Line ends and their labels sit at the end of each period, not one bar off. Most of those ends lie to the right of the last bar, and NinjaTrader shows only its right margin there, so to check them raise **Right side margin** in the chart's Properties (a few hundred pixels shows the 30m and 1H ends on a 5-minute chart). With **Keep Timeline Labels On Screen** (10. Display, on by default), a label that would run past the chart's right edge is drawn against that edge on purpose, so with the default margin every timeline label sits at the right edge. D, W and M ends on an intraday chart are usually too far out to see.
- Set the indicator's **Calculate** to On bar close. The table and lines still draw, one bar behind.
- **Bar Numbers** (12. Bar Numbers, off by default): turn it on. Each candle gets its Strat number: 1, 2u, 2d, 3u, 3d, F2u and F2d in the default Full notation (Classic shows 1, 2u, 2d and 3; Numbers shows 1, 2 and 3). The forming candle's number is lighter. Try each Notation, Emphasis, Position and Color option. This follows a Suite feature that is not released on TradingView yet, so judge it on whether it reads well rather than against TradingView.
- **Alerts** (14. Alerts): turn on **Enable Alerts** and a few conditions. Alerts appear in the Alerts Log once per bar with a sound, and never while the chart is loading history.
- **Preview** (Auto by default): in Market Replay, play through a Friday close (2:00 PM PT, 5:00 PM ET, for CME futures). The table shows PREVIEW MODE afterwards and goes back to normal at the Sunday open (3:00 PM PT, 6:00 PM ET).
- **Speed:** a 1-minute chart with several months of data and the Custom preset, with Timeframe 1 to Timeframe 6 all turned on under 02. Timeframes (Custom preset) (they start off), loads in reasonable time and stays responsive.

## How to report

One report per problem. Copy this:

```
NinjaTrader version (Help > About):
Windows version, and whether it is a VM or a Mac:
Data feed / connection:
Symbol, contract and chart timeframe:
Preset and any settings you changed:
What you did:
What you expected (TradingView screenshot if it is a mismatch):
What happened (screenshot):
Text from Control Center > Log and the NinjaScript Output window, if any:
```

Screenshots of NinjaTrader and TradingView side by side are the fastest way to pin down a mismatch.
