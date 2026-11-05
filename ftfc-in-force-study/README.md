# TheStrat FTFC vs In Force (study)

A learning study, separate from TheStrat Suite. It shows, bar by bar on the Hour, Day, Week and Month, where **Full Timeframe Continuity** and **strict Signals In Force** agree and where they differ.

- **FTFC**: every timeframe trading on the same side of its open.
- **All In Force (strict)**: every timeframe has a Suite signal in force (reversal, continuation, Failing 2, expansion), all in the same direction. A timeframe with nothing in force, such as an inside bar, breaks it. Signal type never matters, only direction.

## Candles (Agreement mode)

| Color | Meaning |
|---|---|
| Full green / red | FTFC and all-in-force agree, bull / bear |
| Faded green / red | FTFC aligned, but not every timeframe has a signal in force |
| Purple | Every timeframe in force, but FTFC not aligned with it |
| Gray | Neither |

*FTFC Only* and *In Force Only* modes show one question at a time, so you can flip between them.

## Pane

A heatmap with two rows per timeframe (`open` = above/below the period open, `IF` = signal in force) and two summary rows (`FTFC`, `ALL IF`). Green = bull, red = bear, dim = neither. The row that is dim or opposite is the timeframe holding the other back. A stats table counts how often each state occurs over the loaded history.

## Notes

- No `calc_bars_count` limit, so it uses all the history your TradingView plan loads.
- Timeframes below the chart's are skipped. Use a 1H or lower chart to see all four.
- No repainting: each timeframe's forming bar is rebuilt from chart bars, so history is graded as it stood at each bar's close. The in-force test mirrors TheStrat Suite v3.1.1 with the FTFC and HAM/SHO filters off.
