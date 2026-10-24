# Beta testing TheStrat Suite for thinkorswim

Thanks for testing. This is a free, open-source port of TheStrat Suite for TradingView (v3.1.1) to thinkorswim. This first beta, v0.2.0, is layer 1 of a full port. It classifies candles (1, 2u, 2d, 3, F2u, F2d), paints Strat Candles or FTFC Candles, and shows a strip of six timeframes with their current candle plus an FTFC label. Signals, levels, targets, stops, the data table and alerts come in later versions.

The candle rules are checked against the Suite's grammar in automated tests, but nobody has loaded this version in thinkorswim yet, so you may be the first. Your job is to find where it fails to load, breaks, or disagrees with the TradingView Suite. Times below are Pacific, with Eastern in parentheses.

This is a study only. It never places orders. A paperMoney login is enough.

## What you need

- thinkorswim desktop on Windows, Mac or Linux, with a Schwab login. paperMoney works. Studies are written in the desktop app.
- Quotes for SPY and /ES, live or delayed.
- Optional but most useful: TradingView with TheStrat Suite v3.1.1 on the same symbol, to compare side by side.

## Install

1. Get `TheStratSuite_TOS_v0.2.0.txt`. Open it in a text editor, select all and copy.
2. In thinkorswim, on the **Charts** tab, click **Studies > Edit Studies**. In the window that opens, click **Create** (bottom left).
3. At the top of the editor, replace the name (`NewStudy0`) with `TheStratSuite`. Delete the placeholder code, paste, and click **OK**.
4. Back in Edit Studies, add **TheStratSuite** to the chart and click **OK**.

If the editor shows red text at the bottom, or will not let you click OK, that is a compile error. **A compile error is the most valuable report you can send.** Copy the red text exactly, with the line it points to, and a screenshot of the editor.

If you were sent a thinkorswim shared link instead, use **Setup > Open shared item**, paste the link and import it.

Settings open from the study's gear in Edit Studies. Colors are under the **Globals** tab of the same window.

## Test 1: it loads and draws (about 15 minutes)

Use a SPY 15-minute chart (20 days), then an /ES 5-minute chart (5 days), then a SPY daily chart (1 year).

- Colored labels appear in the chart's top left, one per timeframe, highest first. Each reads the timeframe and its current candle, such as `M 2u`, `W 1d`, `D 3u`, `1H F2d` and `30m 2u`. An FTFC label follows (FTFC Up, FTFC Down or Conflict).
- A timeframe below the chart's shows its name in gray with no candle type. On the SPY daily chart with TheStrat Classic, 30m and 1H are gray. That is expected: thinkorswim cannot read a timeframe below the chart's, and the Suite grays those cells too.
- Try each **preset**. The labels change to match. Futures/Crypto shows 1H, 4H, D and W only, because thinkorswim has no 12H.
- With **Custom**, turn on `tf1Enabled` to `tf6Enabled` (they start off, as in the Suite) and pick a period for each. The labels follow. 2m, 4m, 10m and 20m read 2, 4, 10 and 20, as in the TradingView Suite.
- Set **barColorMode** to **Strat Candles**. 1s paint yellow (1u) or orange (1d), 2s green (2u) or red (2d), 3s teal (3u) or pink (3d), F2u light red and F2d light green. Then try **FTFC Candles**: green when every timeframe is above its open, red when every one is below, gray when they disagree. Green and red mean bull and bear.
- The paint toggles (`paintInsideBars` and the rest) turn each family's paint off. With `paintFailedBars` off, an F2u paints as a plain 2u.
- Change a color under Globals, close and reopen thinkorswim. The change is still there.
- Label text is readable on every color. thinkorswim picks the label text color itself, so tell us any label you cannot read, and whether you use the dark or light theme.
- Turn on **showDiagnostics**. A gray label shows the version, symbol, chart timeframe and preset. One gray label per timeframe shows its current candle (CC: open, high, low, close) and the prior candle (C1: high and low). Screenshots of these make any mismatch fast to pin down. Turn it off afterwards.
- **Speed:** a 1-minute chart with 5 days loads in reasonable time and stays responsive.
- Use regular candles or bars. TheStrat reads traded highs and lows, and Heikin-Ashi candles are averages, so Heikin-Ashi charts are not supported.

## Test 2: it matches TradingView (about 30 minutes, best after a session close)

Use the same symbol, timeframe and preset on both, with extended hours off on both, for example SPY 15-minute with TheStrat Classic. On TradingView, set the Suite's **Display - Data Table** mode to **Compact** with **Bar State** colors, or read the CC column in Full mode, and set **Color Chart Candles** to match what you test here.

On the **most recent bar** compare:
- each timeframe's candle type in the strip against the Suite's table;
- the FTFC label against the Suite's FTFC row;

and over the **last full session**, bar for bar:
- Strat Candles, including at least one F2 and one 3;
- FTFC Candles.

Then a reload test: note the colors of the last 20 candles in each mode, switch to another symbol and back, and confirm nothing changed.

**Expected differences:**
- The Suite's FTFC also counts timeframes below the chart's, and thinkorswim cannot read them. So compare FTFC on a chart at or below every enabled timeframe, such as 15-minute with TheStrat Classic.
- The Suite's FTFC Candles highlight a Failing 2 flip; this beta does not yet.
- No 12H, and /ES 4H may start at different times (Test 3).

## Test 3: futures timing (about 10 minutes)

thinkorswim may start /ES 4-hour candles at different times than the Suite, which would make the 4H candle type differ on the Day Trade and Futures/Crypto presets.

- Set an /ES chart to 4 hours with 10 days. Hover over one day's bars and write down when each 4H candle starts, and which time zone your thinkorswim shows times in.
- The Suite's 4H candles start at 3 PM, 7 PM, 11 PM, 3 AM, 7 AM and 11 AM PT (6 PM, 10 PM, 2 AM, 6 AM, 10 AM and 2 PM ET). We expect thinkorswim's to start at 10 PM, 2 AM, 6 AM, 10 AM, 2 PM and 6 PM PT (1 AM, 5 AM, 9 AM, 1 PM, 5 PM and 9 PM ET). Tell us which you see.
- Do the same at 2 hours, and check that the /ES daily candle starts at 3 PM PT (6 PM ET).

## Test 4 (optional): help build the full port (about 15 minutes)

A few answers the next layers depend on can only come from inside thinkorswim. `TheStratSuite_TOS_Probes_v0.2.0.txt` is a second study that only draws labels. Install it the same way, named `TheStratSuite_Probes`, and send a screenshot of its labels on each of these charts:

1. SPY 5-minute, 20 days, extended hours **on**.
2. /ES 5-minute, 5 days.
3. SPY 1-hour, 180 days.
4. SPY daily, as many years as thinkorswim offers.

Also send one screenshot of its colored labels (the P22 row) in the dark theme and one in the light theme. A label that reads N/A is an answer too. If the probe study fails to compile, send its red error text.

If you use thinkorswim mobile, open a chart with TheStratSuite on your phone and tell us which parts show: the labels, the candle colors, or neither.

## How to report

One report per problem. Copy this:

```
thinkorswim build (from About thinkorswim):
Computer and operating system:
Live account or paperMoney, and live or delayed quotes:
Symbol, chart timeframe and days loaded, extended hours on or off:
Preset and any settings you changed:
What you did:
What you expected (TradingView screenshot if it is a mismatch):
What happened (screenshot):
Red error text from the study editor, if any:
```

Screenshots of thinkorswim and TradingView side by side are the fastest way to pin down a mismatch.
