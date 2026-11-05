# TradingView publications

The importable copy of each library lives on TradingView; this file maps every published
version back to the exact repo state it was built from. Imports pin a version integer and
never auto-upgrade, so when a library updates, sweep the repo for old
`import SpinTrades/<name>/<N>` lines and bump them deliberately.

| Library | TV version | Import | Published | Listing | Git tag |
|---|---|---|---|---|---|
| PineDraw | 1 | `SpinTrades/PineDraw/1` | 2026-08-12 | https://www.tradingview.com/script/L1Yo5Rir-PineDraw/ | `tv-PineDraw-v1` |
| PineDraw | 2 | `SpinTrades/PineDraw/2` | 2026-08-13 | https://www.tradingview.com/script/L1Yo5Rir-PineDraw/ | `tv-PineDraw-v2` |
| TheStratGrammar | 1 | `SpinTrades/TheStratGrammar/1` | 2026-08-24 | https://www.tradingview.com/script/fyVsfy8l-TheStrat-Grammar-Objective-Bar-State-Definitions-and-Notation/ | `tv-TheStratGrammar-v1` |

## Indicators and studies

| Script | Version | Published | Listing | Source |
|---|---|---|---|---|
| TheStrat: Full Timeframe Continuity (FTFC) vs Signals In Force | v0.3.1 | 2026-11-05 | https://www.tradingview.com/script/1EMdC3R7-TheStrat-Full-Timeframe-Continuity-FTFC-vs-Signals-In-Force/ | `ftfc-in-force-study/FTFC_vs_InForce_v0.3.1.pine` as of commit `489a577` (replaced by v0.3.2 in the repo, not yet published), description `ftfc-in-force-study/TV_DESCRIPTION.txt` |

## Notes

**TheStratGrammar v1.** The publication title is
"TheStrat Grammar: Objective Bar State Definitions and Notation" — TradingView does not tie the
listing title to the `library()` name, so the import path stays `SpinTrades/TheStratGrammar/1`.

The tag `tv-TheStratGrammar-v1` points at `c14dcc8`, not at the tip. The source was copied to the
Pine editor before `487e89b` landed, so the published copy still carries the comment wording that
commit replaced. TradingView generates the listing description from those comments and freezes it
at publish time; the description was corrected by hand during the post-publish edit window, so the
listing text matches the tip while the published *source* matches `c14dcc8`. The two reconverge at
the next version bump — whatever ships as v2 carries the tip's comments.
