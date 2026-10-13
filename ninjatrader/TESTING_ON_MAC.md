# Testing the NinjaTrader port from a Mac

NinjaTrader 8 is a Windows desktop program. Its web and mobile platforms do not run NinjaScript. To test the port from macOS, you need Windows. Times below are Pacific, with Eastern in parentheses.

## 1. Get a Windows machine (pick one)

| Option | Cost | Fit |
|---|---|---|
| **Parallels Desktop + Windows 11 ARM** on an Apple Silicon Mac (recommended) | Parallels subscription; Windows license optional for testing | NinjaTrader 8 is an x64 program, and Windows 11 ARM runs it through x64 emulation. NinjaTrader does not officially support ARM Windows. Community reports say it runs, but that is not verified here. Give the VM 4+ cores and 8 GB+ RAM. |
| **Cloud Windows VM** (Azure or AWS EC2 Windows, reached with Microsoft's Windows App) | About $0.20 to $0.50 an hour; stop it when idle | Native x64 and officially supported. Use this if the Parallels route misbehaves, or to rule out emulation when something looks off. |
| **Boot Camp** | Free | Intel Macs only. |
| **A Windows PC you already have** | Free | Best if available. |

The Mac mini also works: install Parallels there and use Screen Sharing.

## 2. Install NinjaTrader and data

1. Install NinjaTrader 8 from ninjatrader.com and sign in with a free account. You don't need a funded account or a live connection.
2. **Historical data:** without a paid feed, use **Tools > Historical Data > Download** from the free NinjaTrader connection for ES and SPY daily and minute data, if your account allows it. Otherwise use Market Replay data (next step).
3. **Market Replay:** under **Tools > Historical Data**, download replay data for ES 12-26 for a few recent sessions. Then connect to **Playback** (Connections > Playback Connection). Replay acts like a live feed: ticks, realtime state, alerts and the preview clock all run against replay time. This is how you test the live behavior on a weekend.

## 3. Install the indicator

**Tools > Import > NinjaScript Add-On** and pick `ninjatrader/dist/TheStratSuite_NT8_v0.1.0.zip`. If NinjaTrader rejects the zip, copy the two `.cs` files into `Documents\NinjaTrader 8\bin\Custom\Indicators\TheStratSuite\` inside Windows. Then open **New > NinjaScript Editor** and press F5. Send me any compile errors as text or screenshots, with the line numbers.

## 4. Test sessions

Each session uses the README checklist numbers. Record pass/fail and a screenshot per item. A shared folder between Mac and VM makes screenshots easy to hand back.

**Session A: loads and draws (about 30 minutes, any time)**
- Checklist 1, 2, 3, 11, 13: import, table rows fill, D/W/M rows, property grid names, tick and Renko charts.
- Charts: ES 12-26 5-minute (ETH), SPY 5-minute (RTH), ES daily.

**Session B: parity with TradingView (about 45 minutes, after a close)**

Put the Mac's TradingView chart beside the VM. Use the same symbol and the same preset, with defaults otherwise. For each of ES 5m Classic, ES 60m Futures/Crypto and SPY daily Swing Trade, compare:
- the table's C2, C1 and CC types per row, and the FTFC row;
- each trigger, magnitude and exhaustion price shown in labels (turn on **Show Price in Label** in both);
- stop lines, if enabled.

Compare the **last bar**, where the two should agree. On older bars the port shows what was visible live, so history can legitimately differ. TradingView's ES continuous contract vs NinjaTrader's single contract, and back-adjustment, can shift prices. Use the same contract month on both, e.g. ES1! vs ESZ2026 on TradingView. Checklist 4, 5, 6, 7, 8.

**Session C: live behavior in Market Replay (about 45 minutes)**

Replay a session that includes a Friday close.
- Checklist 9: Preview Auto arms after the Friday close (2:00 PM PT, 5:00 PM ET) and not before. The table shows `PREVIEW MODE` and `?` in CC for previewed rows.
- Checklist 10: Enable Alerts plus a few conditions. Alerts appear in the Alerts Log once per bar with sound.
- Checklist 12: 1-minute chart, six slots (Custom preset), several months of data. Load time and tick responsiveness should be acceptable.

**Session D: live (optional, market hours)**

With any live or simulated data feed during RTH (6:30 AM to 1:00 PM PT, 9:30 AM to 4:00 PM ET), watch a 5-minute chart for an hour. Confirm the lines and table update as new bars form, and that a reload shows the same history.

## 5. Reporting back

Post results in the project thread: the checklist numbers that failed, screenshots, and any NinjaScript Output window text. Anything that fails gets fixed on PR #3, and the Linux tests (`grammar/tests/test_nt_parity.py`) are rerun before each push.
