# TheStrat FTFC vs In Force (study)

A learning study, separate from TheStrat Suite. It shows, bar by bar on the Hour, Day, Week and Month, where **Full Timeframe Continuity** and **strict Signals In Force** agree and where they differ.

- **FTFC**: every timeframe trading on the same side of its open.
- **All In Force (strict)**: every timeframe has a Suite signal in force (reversal, continuation, Failing 2, expansion), all in the same direction. A timeframe with nothing in force, such as an inside bar, breaks it. Signal type never matters, only direction.

## Layers (v0.2.0)

Each layer can be switched on or off to compare.

**Two-channel candles** (default). The body answers FTFC, the outline and wick answer All In Force:

| Candle | Meaning |
|---|---|
| Filled green/red, matching outline | Both agree |
| Filled, gray outline | FTFC aligned, but not every timeframe has triggered |
| Hollow, colored outline | Every timeframe in force, but price not on the same side of every open |
| Hollow, gray outline | Neither |

Hide the chart's own candles (Chart Settings > Symbol: uncheck Body, Borders, Wick) for the cleanest look.

**Cascade ladder.** A letter (H, D, W, M) on the bar where each timeframe goes in force: green below for bull, red above for bear, stacked by timeframe so the order they trigger in is visible. First entry per period by default; *Every Re-entry* shows contested triggers chopping in and out.

**Score pane.** Columns count how many timeframes are above (+) or below (-) their open, full color only at FTFC. The white line counts how many have a signal in force. Tall columns with the line near zero means price is on the right side of the opens but nothing has triggered.

**Heatmap pane.** Two rows per timeframe (`open` = above/below the period open, `IF` = signal in force) and two summary rows (`FTFC`, `ALL IF`). The dim or opposite row is the timeframe holding the other back.

**Agreement Colors.** The v0.1.0 single color: full green/red both agree, faded FTFC only, purple in force only, gray neither. *FTFC Only* and *In Force Only* show one question at a time.

A stats table counts how often each state occurs over the loaded history, and a legend explains whichever layers are on.

## Notes

- No `calc_bars_count` limit, so it uses all the history your TradingView plan loads.
- Timeframes below the chart's are skipped. Use a 1H or lower chart to see all four.
- No repainting: each timeframe's forming bar is rebuilt from chart bars, so history is graded as it stood at each bar's close. The in-force test mirrors TheStrat Suite v3.1.1 with the FTFC and HAM/SHO filters off.
