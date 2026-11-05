# TheStrat FTFC vs In Force (study)

A learning study, separate from TheStrat Suite. It shows, bar by bar on the Hour, Day, Week and Month, where **Full Timeframe Continuity** and **strict Signals In Force** agree and where they differ.

- **FTFC**: every timeframe trading on the same side of its open.
- **All In Force (strict)**: every timeframe has a Suite signal in force (reversal, continuation, Failing 2, expansion), all in the same direction. A timeframe with nothing in force, such as an inside bar, breaks it. Signal type never matters, only direction.

## Layers (v0.3.0)

**Candles: Agreement, Hollow** (default). v0.1.0's agreement colors, drawn as real candles so the body and outline can say different things:

| Candle | Meaning |
|---|---|
| Solid green / red | FTFC and every timeframe in force agree |
| Faded body, full outline | FTFC aligned, but not every timeframe has a signal in force |
| Hollow, colored outline | Every timeframe in force, but price not on the same side of every open |
| Hollow gray | Neither |

Hide the chart's own candles (Chart Settings > Symbol: uncheck Body, Borders, Wick) for the cleanest look. Other candle modes: *Agreement, Solid* (v0.1.0, purple for in force without FTFC), *Body = FTFC, Outline = In Force* (v0.2.0), *FTFC Only*, *In Force Only*.

**Score pane.** Columns count how many timeframes are above (+) or below (-) their open, full color only at FTFC. The white step line counts how many have a signal in force. Line sitting on top of full columns = both agree. Full columns with the line near zero = continuity with nothing triggered.

**Heatmap pane.** One band per timeframe: top half above/below its open, bottom half signal in force (dim = none), with FTFC and ALL IF rows on top.

**Cascade ladder** (off by default). A letter (H, D, W, M) on the bar where each timeframe goes in force, stacked by timeframe so the order they trigger in is visible.

A small stats table on the price chart shows how often FTFC, all in force, and both occur over the loaded history.

## Notes

- No `calc_bars_count` limit, so it uses all the history your TradingView plan loads.
- Timeframes below the chart's are skipped. Use a 1H or lower chart to see all four.
- No repainting: each timeframe's forming bar is rebuilt from chart bars, so history is graded as it stood at each bar's close. The in-force test mirrors TheStrat Suite v3.1.1 with the FTFC and HAM/SHO filters off.
