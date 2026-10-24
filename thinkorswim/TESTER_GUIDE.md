# Beta testing TheStrat Suite for thinkorswim

Thanks for testing. This is a free, open-source port of TheStrat Suite for TradingView (v3.1.1) to thinkorswim. This first beta, v0.2.0, is layer 1 of a full port. It classifies candles (1, 2u, 2d, 3, F2u, F2d), paints Strat Candles or FTFC Candles, and shows a strip of up to six timeframes with their current candle, plus an FTFC label. The presets show four or five timeframes (five on the default TheStrat Classic); six takes the Custom preset. Signals, levels, targets, stops, the data table and alerts come in later versions.

The candle rules and presets are checked against the Suite in automated tests, but nobody has loaded this version in thinkorswim yet, so you may be the first. Your job is to find where it fails to load, breaks, or disagrees with the TradingView Suite. Times below are Pacific, with Eastern in parentheses.

This is a study only. It never places orders. A paperMoney login is enough.

## What you need

- thinkorswim desktop on Windows, Mac or Linux, with a Schwab login. paperMoney works. thinkorswim web does not run custom studies, and studies are created in the desktop app.
- Quotes for SPY and /ES, live or delayed.
- Optional but most useful: TradingView with TheStrat Suite v3.1.1 on the same symbol, to compare side by side.

## Install

1. Get `TheStratSuite_TOS_v0.2.0.txt` from the [thinkorswim folder on GitHub](https://github.com/345-strategies/thestrat-suite/tree/main/thinkorswim), or from whoever sent you this guide. On GitHub, open the file and click **Copy raw file**. Otherwise open it in a text editor, select all and copy.
2. In thinkorswim, on the **Charts** tab, click **Studies > Edit Studies**. In the window that opens, click **Create** (bottom left).
3. At the top of the editor, replace the name (`NewStudy0`) with `TheStratSuite`. Delete the placeholder code, paste, and click **OK**.
4. Back in Edit Studies, add **TheStratSuite** to the chart and click **OK**.

If the editor shows red text at the bottom, or will not let you click OK, that is a compile error. **A compile error is the most valuable report you can send.** Copy the red text exactly, with the line it points to, and a screenshot of the editor.

If you were sent a thinkorswim shared link instead, click **Setup** (top right) > **Open shared item**, paste the link, click **Preview**, then **Import**. If it offers to replace one of your studies, choose **Create new** unless you mean to update an older copy.

Settings open from the study's gear in Edit Studies, and colors are under its **Globals** tab. This guide names settings as they are in the code. thinkorswim may show them split into lowercase words, for example `barColorMode` as "bar color mode". Tell us the exact names you see.

## Test 1: it loads and draws (about 15 minutes)

Use a SPY 15-minute chart (20 days), then an /ES 5-minute chart (5 days), then a SPY daily chart (1 year). Use time-based charts with regular candles. Tick, range and Renko charts are not supported in this beta, and neither is Heikin-Ashi, because TheStrat reads traded highs and lows.

- Colored labels appear in the chart's top left, one per timeframe, highest first. Each reads the timeframe and its current candle, such as `M 2u`, `W 1d`, `D 3u`, `1H F2d` and `30m 2u`. An FTFC label follows (FTFC Up, FTFC Down or Conflict).
- A timeframe below the chart's shows its name in gray with no candle type. On the SPY daily chart with TheStrat Classic, 30m and 1H are gray. That is expected: thinkorswim cannot read a timeframe below the chart's, and the Suite grays those cells too.
- Try each **preset**. The labels change to match. Futures/Crypto shows 1H, 4H, D and W only, because thinkorswim has no 12H.
- With **Custom**, turn on `tf1Enabled` to `tf6Enabled` (they start off, as in the Suite) and pick a period for each. The labels follow. 2m, 4m, 10m and 20m read 2, 4, 10 and 20, as in the TradingView Suite.
- Set `barColorMode` to **Strat Candles**. 1s paint yellow (1u) or orange (1d), 2s green (2u) or red (2d), 3s teal (3u) or pink (3d), F2u light red and F2d light green. Then try **FTFC Candles**: green when the close is above the open of every timeframe at or above the chart's, red when it is at or below every one (a close exactly at the open counts as down, as in the Suite), gray when they disagree. Green and red mean bull and bear.
- The paint toggles (`paintInsideBars` and the rest) turn each family's paint off. With `paintFailedBars` off, an F2u paints as a plain 2u.
- Change a color under Globals, close and reopen thinkorswim. The change is still there.
- Label text is readable on every color. thinkorswim picks the label text color itself, so tell us any label you cannot read, and whether you use the dark or light theme.
- Turn on `showDiagnostics`. A gray label shows the version, symbol, chart timeframe and preset. Then one gray label per enabled timeframe, numbered TF1 to TF6 in slot order (lowest first, unlike the strip), shows its current candle (CC: open, high, low, close) and its prior candle (C1: high and low). A timeframe below the chart's reads "below chart, not read" (on the SPY daily chart that is 30m and 1H), and one with no data reads "no data". Screenshots of these make any mismatch fast to pin down. Turn it off afterwards.
- **Speed:** a 1-minute chart with 5 days loads in reasonable time and stays responsive.

## Test 2: it matches TradingView (about 30 minutes)

Use the same symbol, timeframe and preset on both, with extended hours off on both, for example SPY 15-minute with TheStrat Classic.

- On TradingView, set the Suite's **Display - Preview Mode** to **Off**. Its default, Auto, shows a preview of the next period after the close and before the open, which this beta does not have yet.
- On TradingView, set **Display - Data Table** mode to **Compact** with **Bar State** colors, or read the CC column in Full mode. Set **Color Chart Candles** to match what you test here.
- In thinkorswim, open the chart settings (gear), go to the **Equities** tab, and note whether **Start aggregations at market open** is checked. It can only be checked with extended hours off. TradingView's regular-hours 1H and 4H candles start at 6:30 AM PT (9:30 AM ET). If the 1H candle (or 4H on Day Trade) differs, switch the setting, check again, and tell us both results.

On the **most recent bar** compare:
- each timeframe's candle type in the strip against the Suite's table;
- the FTFC label against the Suite's FTFC row;

and over the **last full session**, bar for bar:
- Strat Candles, including at least one F2 and one 3;
- FTFC Candles.

Then a reload test: note the colors of the last 20 candles in each mode, switch to another symbol and back, and confirm nothing changed. If you can, repeat the FTFC Candles reload test on SPY 5-minute with extended hours on: screenshot the 4:00 to 6:30 AM PT (7:00 to 9:30 AM ET) candles as they paint live, then reload after 6:30 AM PT and screenshot the same candles.

**Expected differences:**
- The Suite's FTFC also counts timeframes below the chart's, and thinkorswim cannot read them. So compare FTFC on a chart at or below every enabled timeframe, such as 15-minute with TheStrat Classic.
- Further back, the Suite reads only each timeframe's most recent candles (250 intraday, 200 for 4H, 150 for D, 100 for W and M) and leaves that timeframe out of older FTFC Candles. thinkorswim grades every loaded bar with every timeframe. So compare FTFC Candles only within the last 250 candles of the shortest enabled timeframe. On SPY 15-minute with TheStrat Classic, the last full session is safe. With Scalp on /ES (a 5-minute slot), compare only about the last 20 hours.
- The Suite's FTFC Candles highlight a Failing 2 flip; this beta does not yet.
- No 12H. /ES 4H, and SPY 4H with extended hours on, may start at different times (Test 3).

## Test 3: candle start times (about 15 minutes)

thinkorswim may start 4-hour candles at different times than the Suite, which would make the 4H candle type differ on the Day Trade, Futures/Crypto and Swing Trade presets. Use the Last price (the default), not Mark, which thinkorswim aligns differently.

- Set an /ES chart to 4 hours with 10 days. Hover over one day's bars and write down when each 4H candle starts, and which time zone your thinkorswim shows times in.
- The Suite's /ES 4H candles start at 3 PM, 7 PM, 11 PM, 3 AM, 7 AM and 11 AM PT (6 PM, 10 PM, 2 AM, 6 AM, 10 AM and 2 PM ET). We expect thinkorswim's to start at 10 PM, 2 AM, 6 AM, 10 AM, 2 PM and 6 PM PT (1 AM, 5 AM, 9 AM, 1 PM, 5 PM and 9 PM ET). Tell us which you see. Do the same at 2 hours.
- On a SPY 4-hour chart, first with extended hours off and then on, write down when each 4H candle starts, and whether **Start aggregations at market open** was checked.
- Then check where the /ES daily candle starts. On an /ES 5-minute chart (5 days) with TheStrat Classic, turn on `showDiagnostics` and note the CC open (O) in the "TF3 D" label. Find the 5-minute bar at 3:00 PM PT (6:00 PM ET) that starts the current session (on a weekend, the last one before the weekend) and compare its open. If they differ, find the 5-minute bar whose open matches and tell us its time.

## Test 4 (optional): help build the full port (about 20 minutes)

A few answers the next layers depend on can only come from inside thinkorswim. `probes/TheStratSuite_TOS_Probes_v0.2.0.txt` (in the same GitHub folder) is a second study that draws labels and one bubble. Install it the same way, named `TheStratSuite_Probes`, and send a screenshot of its labels on each of these charts, noting the date and time you took it:

1. SPY 5-minute, 20 days, extended hours **on**, after 6:30 AM PT (9:30 AM ET).
2. The same SPY 5-minute chart on a weekday between 1:30 and 5:00 PM PT (4:30 and 8:00 PM ET), once after-hours bars have printed. Its P6b label shows where the daily candle ends.
3. /ES 5-minute, 5 days.
4. SPY 1-minute, 1 day.
5. SPY 15-minute, 20 days.
6. SPY 1-hour, 360 days (the longest thinkorswim offers for intraday charts).
7. SPY daily, as many years as thinkorswim offers.

Also:
- One screenshot of its colored labels (the P22 row) in the dark theme and one in the light theme.
- The P10 label and the P10 bubble on the last bar. Then, during market hours (or on /ES, which trades nearly around the clock), set `fireP10Alert` to yes and screenshot the alert and its entry in the Message Center. If nothing appears, open the probe study's settings in Edit Studies, scroll to its Alerts section, and tell us whether Enabled and Show in Message center are checked. The alert fires once each time the study is added, so to try again, remove the probe study from the chart and add it back. Tell us whether thinkorswim's use of your computer's system fonts is on or off (under Setup > Application Settings), and if you can, repeat the P10 screenshots with it switched.
- Load TheStratSuite with Strat Candles on a 1,000-tick /ES chart and tell us whether anything draws: labels, candle colors, or nothing.

A label that reads N/A is an answer too. If the probe study fails to compile, send its red error text. If the error points at a line under the P10 heading (the one with arrows and colored circles), delete the three lines below that heading's comments, click OK again, and carry on with the rest.

If you use thinkorswim mobile, open a chart with TheStratSuite on your phone and tell us which parts show: the labels, the candle colors, or neither.

## How to report

Open one [GitHub issue](https://github.com/345-strategies/thestrat-suite/issues) per problem, paste this template, and attach your screenshots. If you got this guide directly, you can send the report back the same way instead. Issues are public, so crop or cover your account number, balances and positions before attaching a thinkorswim screenshot.

```
thinkorswim build (from About thinkorswim):
Computer and operating system:
Live account or paperMoney, and live or delayed quotes:
Symbol, chart timeframe and days loaded, extended hours on or off:
Start aggregations at market open (Equities tab): on, off or not shown
Preset and any settings you changed:
Date and time of the screenshot, with time zone:
What you did:
What you expected (TradingView screenshot if it is a mismatch):
What happened (screenshot):
Red error text from the study editor, if any:
```

Screenshots of thinkorswim and TradingView side by side are the fastest way to pin down a mismatch.
