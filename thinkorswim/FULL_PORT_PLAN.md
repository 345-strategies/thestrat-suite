# thinkorswim Full Port Plan (TheStrat Suite v3.1.1)

Nate asked on 2026-09-28 for a port of all of `pine/TheStratSuite_v3.1.1.pine` to thinkorswim desktop, not a Lite subset. Here "full port" means that every input, signal family, level, label, table row, bar color, preview rule, debug field and alert condition gets a thinkScript counterpart. The port keeps three things from the Suite:

- its color palette and defaults;
- no repainting: a reload shows what was seen live;
- green and red always mean bull and bear.

Where thinkorswim cannot match the Suite, the matrix says exactly what differs. The port ships free under MPL-2.0 as one generated study plus a small companion study for server-side alerts. It builds on Lite v0.1.0 (layer 1) and replaces the Lite roadmap for layers 2 and 3.

None of this has been loaded in thinkorswim yet. A row marked (probe) depends on an in-app test listed under Open questions. Times are in Eastern, as in the Pine. Pacific is 3 hours earlier (18:00 ET = 3 PM PT).

**Hard platform limits and the chosen equivalents**
- **No aggregation below the chart timeframe.** A lower slot is greyed and left out. Pine's own values for such slots repaint on reload, so leaving them out is the no-repaint behavior.
- **No 8H, 12H or 6M aggregation, and custom periods are rejected.** thinkorswim time bars also start at midnight CT, so the native futures 4H is one hour off. Equivalent: build 4H, 8H and 12H from chart bars on charts of 1H or less, and build 6M from pairs of quarter bars.
- **No wall clock.** Preview On and Off are kept. Auto becomes a "last bar is the session's final bar" proxy. The 4h holiday fallback is dropped. Straddles are detected from data on charts above D.
- **No lower-timeframe data.** A straddled calendar slot shows `?`, which is Pine's own no-data path.
- **Display limits.** Strings cannot be stored, label and bubble text color is fixed by the platform, there is no alpha channel, and labels have 4 corners only. Equivalents: numeric codes rendered into text, colored fills with a theme input, colors pre-blended against the background, and a corner mapping.
- **Alerts.** Alert() is client-side only, and server-side contexts reject multi-timeframe code. Equivalent: local Alert() plus a single-timeframe companion study for push, email and SMS. There are no webhooks.

**How the port is built**
- **Generated code.** A Python generator writes the study: six suffixed slot blocks, and the low side of every rule generated from the high side by substitution. The generator is needed because thinkScript has no arrays, UDTs, loops over slots or stored strings. Parity tests extend `grammar/tests/test_tos_parity.py`.
- **Slot data.** Each slot reads HTF data through pure-secondary defs (`high(period = aN)[k]`). Chart-bar history is read only through primary mirrors. The generator enforces this with a lint.
- **Display is last-bar state.** Visibility uses SetHiding, which reads the last real bar, and AddLabel reads the last bar by definition. HighestAll broadcast is used only where a value must be constant across a plotted span.
- **As-of engine.** The detection engine re-runs per chart bar on the forming HTF candle, rebuilt from chart bars. It feeds alert edges, Domino edges, sticky stops and F2 flips. It is required because every chart bar inside an open HTF period reads the same served values.

## Feature matrix

Feasibility:
- **exact**: same behavior.
- **equivalent**: same result by a different mechanism.
- **partial**: differs in the stated cases.
- **impossible**: cannot be built; a fallback is given.

Effort (relative): S, M, L, XL.

| Feature | Feasibility | thinkorswim approach | Difference from Suite | Effort |
|---|---|---|---|---|
| **HTF data foundation** | | | | |
| Per-slot HTF data (CC, C1-C4; Pine's 27-value tuple) | equivalent | Per slot, pure-secondary defs `open(period = aN)` ... `low(period = aN)[4]`, or one `script Slot { input agg; ... }` referenced per slot (probe) | No HTF timestamps; HTF bars are built by thinkorswim | M |
| Six-slot processing (computeSignalState per slot) | equivalent | Generated suffixed blocks; pure per-candle helpers in script{} that return numeric plots | Runs on every bar, not only the last; no visible difference | L |
| Computed aggregation in `period=` | equivalent (probe) | Numeric def or script input (threads 10625, 12279); fallback is an if-chain of constants | None | S |
| Custom chart timeframes (e.g. 45m) | equivalent | Compare lower slots against the chart aggregation, but never pass it to `period=`; point lower and disabled slots at a fixed valid constant | None | S |
| lookahead_on semantics ([1] and older settled, [0] live) | exact | Triggers, magnitudes and combos anchor to [1]/[2]; [0] is read only on the last bar | None | S |
| Offset-context rules (counterpart of Pine P0-1 / Rule 3) | equivalent (probe) | Generator lint. (1) HTF offsets only as direct `f(period = a)[n]` in pure-secondary defs. (2) Chart-bar history only on primary mirrors (`if GetYYYYMMDD() > 0 then x else 0`). (3) Never index a mixed-context def | None if the rules hold | M |
| Per-slot state isolation | exact | Generated per-slot code or script instances; no fold over slots | None | S |
| Lower-than-chart slots (validTimeframe) | exact | lowerN = raw < chart aggregation; slot engine off; row greyed | FTFC differs (see FTFC rows) | S |
| Slot data-availability gate | exact | useN = enabled, not lower, C1 not NaN; a token is 0 when C(n+1) is missing | None | S |
| No-repaint contract | equivalent | Served [0] is never shown on historical bars. Historical markers come only from the as-of engine. Expansion-area values go through a forward-fill primary mirror | Plots instead of line objects | S |
| Per-slot new-period detection | equivalent | Change test on primary mirrors of the CC open and C1/C2 values, plus trade-date keys (GetYYYYMMDD) for D/W/M/Q/Y | Boundary inferred from values and dates, not a timestamp; two identical flat HTF bars in a row miss a reset | M |
| Forming HTF candle as of each chart bar (as-of CC) | equivalent | runH/runL via CompoundValue from the period start; `open(period)` for the open; chart close; RTH gate for equity D+ slots | Equals the served CC only when chart bars cover the period in the same session; a slot's edges stay silent until a period start is seen | M |
| History depth (calc_bars_count tiers) | partial | Per-slot depth check with a grey "extend chart range" label; chart range documented per preset | Depth is set by the chart range (intraday maximum 360 days), not by the script | S |
| Exhaustion warmup gate (HTF bar_index > 50) | exact | `!IsNaN(high(period = aN)[51])` in a pure-secondary def | None; 12M exhaustion stays na, as in Pine | S |
| Tick, range, Renko, Kagi, P&F, Line Break charts | equivalent | Detect with `IsNaN(close(period = AggregationPeriod.YEAR))` and show "Time-based charts only"; candles-only companion study if the whole study blanks (probe) | Explicit refusal instead of silent degradation | S |
| Heikin Ashi charts | partial (probe) | Test whether studies read real or HA OHLC; add a note label | Unknown until tested | S |
| Futures data settings (settlement close, roll adjustment) | equivalent | Document the thinkorswim chart settings that match TradingView; never rely on a served D/W/M close equaling the chart close | The setting lives on the chart, not in the study | S |
| Thin symbols (NaN served data) | equivalent | A NaN served [0] on the last bar marks the slot unavailable (grey, no alerts) | No carried-forward CC | S |
| Per-bar cost | equivalent | If-statements, not if-expressions, gate exhaustion scans and disabled slots (only the taken branch is computed) | None | M |
| **Timeframes and presets** | | | | |
| Six presets (Classic, Scalp, Day Trade, Futures/Crypto, Swing Trade, Investing) | equivalent | Enum mapped to constants, as in Lite; 3M = QUARTER, 12M = YEAR; futures 240 and 720 synthesized | Synthesized slots need a chart of 1H or less; "Crypto" means CME crypto futures | S |
| Per-slot enable and Custom slot timeframe | partial | AggregationPeriod input plus a mode input {Native, 8H, 12H, 6M, N minutes} | No seconds, 2W or arbitrary multi-month timeframes | M |
| Timeframe N Width (incl. preset widths) | exact | SetLineWeight on an input-only if-expression (thread 13703 pattern) | None | S |
| Timeframe N Open line | exact | Plot `open(period = aN)` over the current period; SetHiding on the draw flag; gray POINTS style | Dot pattern is thinkorswim's | S |
| Futures 4H (18/22/02/06/10/14 ET = 3P/7P/11P/3A/7A/11A PT) | equivalent | Chart-bar buckets anchored at 18:00 ET on charts of 1H or less (native 4H starts at midnight CT) | Unavailable for futures on charts of 2H and up | L |
| 12H slot | equivalent | Same buckets (43200 s); shift registers for C1-C4 and for the 51+ buckets the exhaustion scan needs | Same chart limit; about 26 trading days must be loaded for exhaustion | L |
| 8H slot | equivalent | Same buckets (28800 s) | Same chart limit; TradingView's equity 8H anchor is unconfirmed | M |
| 6M slot | equivalent | Two QUARTER bars per half year; aggregates precomputed in pure-QUARTER defs; phase from the trade-date month | Deep C4 and exhaustion history is rarely available | M |
| 3M and 12M slots | exact | QUARTER, YEAR | None | S |
| Equity 1H/4H alignment (RTH vs ETH) | equivalent (probe) | Native where the probe shows 09:30 anchoring; otherwise buckets at 09:30 (RTH) or 04:00 (ETH) | "Start aggregations at market open" is not available with extended hours on | M |
| CME trade dates and holiday glue | equivalent (probe) | Trade-date keys; if thinkorswim splits a holiday session differently from TradingView, rebuild D/W from chart bars on intraday charts | Holiday-week candles follow thinkorswim's bookkeeping | M |
| Session-stamp normalization (+12h, RECON-KEY-1) | equivalent | All keys from GetYYYYMMDD (trade date); never from GetMonth/GetWeek/GetDay (CST calendar) | None | S |
| Calendar and straddle timeframe classes | exact | calN = raw >= WEEK and not OPT_EXP; straddle only when coarser than the chart | None | S |
| Timeframe names (getTimeframeLabel) | exact | If-chain printing Pine's strings, with raw fallthrough for unlisted TFs ("2", "4", "10", "20", "2D", "3D", "4D"); OPT_EXP not offered | None | S |
| **Detection and signal logic** | | | | |
| Hammer/shooter: Broad, Classic, Pin Bar | exact | `script HamSho` transcribed from grammar/TheStratGrammar.pine (the code, not the bar-types.md wording); parity test | None | S |
| Match Candle Color | exact | A hammer needs c > o; a shooter is rejected only when c > o, so a doji passes as a shooter | None | S |
| Enable Failing 2 Detection | exact | Input ANDed into the F2, pre-F2 and C1/C2 F flags | None | S |
| F2 Detection Method and pre-F2 | exact | Per-slot formulas from detectFailed2; reclaim test inclusive | None | S |
| Classification tree (shouldDrawC1Level, 8 branches) | exact | Generated if-blocks per slot and side; low side from a substitution table | None | M |
| Inside Reversals (+HAM/SHO, FTFC) | exact | Tree branch 3 and t_insideRev | None | S |
| 2-2 Reversals (+HAM/SHO, F2, FTFC) | exact | Branch 7 plus the post-tree F2 filter, copied with its Reclaim-only c1_was_f2 test | None; Pine quirk copied (see Decisions) | S |
| Hammer/shooter-led setups (branch 6) | exact | The tree's and the in-force reversal predicates transcribed separately | None | S |
| Inside Continuations | exact | Branch 3; an inside C1 always reads INS | None | S |
| 2-2 Continuations | exact | Branch 8 with no C2 test (CONT22-PRIOR-1); C2-dependent target flag kept separate | None | S |
| Failing 2s (Range Reclaims) + FTFC | exact | Branch 2; FTFC is a veto for drawing and needs strict agreement for in force | None | S |
| 3-2 Expansions | exact | Branches 4-5, momo guard, 3-2 side suppressors | None | S |
| Outside Bars (3 Exp) + FTFC | exact | Branch 1 preempts the rest; no in-force term except through hammer/shooter | None | S |
| Structural suppressors | exact | Boolean defs per side; twotwo_rev_f2 kept out of the targets' base | None | S |
| Signal in force (seven terms, rawInForce, signalInForce) | exact | Same defs. Stops and MAG/EXH Only-When-In-Force read raw; TAW, table and Lead read signal | Meaningful only on the last bar, as in Pine | M |
| Exhaustion Disables "In Force" | exact | Last-bar read: CC high >= exhaustion, or >= C2 high when there is no exhaustion | None | S |
| Potential (*) vs in force | equivalent | Star and word codes come from the label decision chains, not from the CC type; color, in force and alertable stay three separate defs | None on display | M |
| Trigger color and style decision | equivalent | Int color and style codes per side from the same if-chain | Rendered as a plot pair (see Levels) | M |
| Raw FTFC for signal filters | partial | Chart close against each usable slot's open | Lower-than-chart slots cannot vote | S |
| Combo tokens (CC type, C1 type, C1-C3 digits, F flags) | exact | Numeric codes per slot; text produced only at render | None | S |
| Lead anchor and Lead row | exact | Highest-first if-chain over slot directions with the F2 tiebreak; row is a label | Opaque label background | S |
| Only Show Signals Following the Lead | exact | fin* draw defs per slot and side; in-force left unfiltered for the table | None | M |
| Domino detection (Minimum Timeframes) | equivalent | Unrolled chain per direction; same candle = equal chart-bar count since period start (or equal period-start GetTime) | Candle identity comes from chart bars; illiquid symbols can dedupe wrongly | M |
| Domino table row | equivalent | Row-owned white label placed before FTFC | A label, not a merged table row | S |
| **Chart labels and text** | | | | |
| TheStrat combo text (collectTimeframeLabels) | exact | Inline token concatenation in bubble text, generated per slot and side | None; prints 2-1-2u as the code does (docs say 2d-1-2u) | L |
| Universal style (REV/CONT/INS/OUT/EXP/FAILING, arrows, diamond, TARGET/FINAL/OPEN) | equivalent | Same decision chain as int codes; glyph input {Unicode, ASCII} | ASCII glyphs if Unicode renders as boxes | M |
| Use Dash Separator (formatCombo) | exact | Separator between non-empty tokens; never after `*`; F bound to the token after it | None | S |
| HAM/SHO suffix, Show H/L | exact | Conditional text pieces | None | S |
| Label buckets and order | exact | Generated static entry lists per high/low/open bucket, slots from high to low | None | M |
| Same-price consolidation | equivalent | Numeric key, leader and rank per entry. The leader bubble renders up to M-1 followers from packed codes, then "+N more". Fallback: adjacent bubbles | Possible "+N more" truncation; the fallback splits text across bubbles | L |
| Show Timeline Labels and Offset | equivalent | AddChartBubble at the line-end bar plus offset, clamped to the expansion area | Bubble points at the price instead of sitting to its right; expansion limit is 1000 bars | M |
| Show Floating Labels and Offset | equivalent | Bubble at the last bar plus offset | Bubble shape and stacking differ | S |
| Show Price in Label | exact | `AsPrice(Round(p / TickSize(), 0) * TickSize())` | Bond futures print in 32nds, not decimals | S |
| Label Text Size | partial | Input dropped; bubbles follow the chart font | Not settable | S |
| Label background, transparency, readable text (LABELTEXT-1) | partial | Fill = level color; Chart Theme input; fills below 3:1 against the forced text color are lifted | Colored fill instead of colored text on a dark background; no transparency | S |
| **Levels and targets** | | | | |
| Trigger lines (potential, in force, outside, F2 dash state) | exact | Solid and dashed plot per trigger; SetHiding on the last-bar style bit; AssignValueColor from GlobalColor | Each trigger is two entries in the plot list | M |
| Line start anchors | exact | Per-slot period counter; a level k HTF bars back shows over the last k+1 periods | Starts at the first chart bar of the period | M |
| Line end (LINEEND-CLOSE-1) | equivalent | Gate on GetTime() < period end if it is defined in expansion bars (probe); otherwise count session bars | Cut at the expansion limit (maximum 1000 bars) | M |
| Show Magnitude (gate battery, dedup) | exact | Same boolean algebra; raw-equality dedup kept | None | M |
| Magnitude Only When In-Force | exact | AND rawInForce | None | S |
| Show Exhaustion (four positive paths) | exact | Same algebra | None | M |
| Exhaustion Only When In-Force | exact | AND rawInForce after all paths | None | S |
| Only After Magnitude Hit | exact | Served CC high >= C2 high, and CC not synthetic | None | S |
| Exhaustion scan, current channel (P0-2) | equivalent | Unrolled pure-secondary offsets [1] to [49] per side; fold/GetValue only if the probe shows they step HTF bars | Returns an HTF offset, not a time; depth limited by the chart range | L |
| Hit latches and Crossed color | exact | Last-bar read of the served extreme against the level, not synthetic | None | S |
| Exhaustion Excludes from FTFC | exact | Crossed flags feed the table's FTFC | None | S |
| Show Magnitude / Exhaustion for Outside Bars | exact | Same defs | None | S |
| Show Failing 2 Open Level | exact | Dotted plot at the CC open; the overlap test runs only against a trigger drawn at compute time | None, except the orphan-line choice (Decisions) | S |
| Level colors (Signal, Crossed, Inside, Outside, Range Reclaim) | equivalent | DefineGlobalColor with Suite defaults (green #4CAF50, red #F23645, ...) | Colors are edited in the Globals tab | S |
| Cross-timeframe line suppression (P1-h) | exact | Pairwise hidden flags per pool (high, low, open), ranked by TF then collection order, on tick-rounded keys; stops exempt | Lines reappear automatically | M |
| Tick-rounded price identity | exact | `Round(p / TickSize(), 0)`; raw equality where Pine uses raw | None | S |
| One decision for lines and labels | exact | final = base and Lead-keep and smallest-keep; lines also require not-hidden | None | M |
| Last-bar-only rendering | equivalent | SetHiding for visibility; offset selection per period for fixed prices; HighestAll only for colors, stops, exhaustion and TAW edges | Plots instead of line objects; same visible result | M |
| Object bookkeeping (create/delete/null, 200-object budget, label pool) | equivalent | Not needed: a NaN draws nothing | None | S |
| **Take Action Windows** | | | | |
| Show TAW (TAW-1) | equivalent | AddCloud per slot and direction; exclusive priority chain (exhaustion, magnitude, F2, outside) | No vertical box edges | M |
| TAW Only When In-Force | exact | AND signalInForce | None | S |
| Extend to Exhaustion (+ Only When In-Force) | exact | First branch of the chain | None | S |
| TAW Fill Opacity | partial | Fill pre-blended toward a Chart Background input; stacked clouds above the native alpha; 0 = off | Approximate; color comes from RGB inputs, not a GlobalColor | S |
| TAW Border Opacity | partial | showBorder off; top and bottom edge plots in a blended color | No vertical edges | S |
| TAW Include Failing 2s | equivalent | F2 branch spanning the C1 range | Same fill and border limits | S |
| TAW Include Outside Bars | equivalent | Outside-bar branch | Same fill and border limits | S |
| **Stops** | | | | |
| Stops: Enable | exact | Per-slot solid stop plots | None | S |
| Stops: Reference (CC / C1; F2 always CC) | exact | Primary-context recursion that copies Pine's branch order | None | S |
| Sticky stop latch (P1-a) | equivalent | Latch on as-of in-force per chart bar (as-of CC and as-of FTFC); reset on a new period | Survives a reload where Pine's drops (see Decisions) | L |
| Break Even at Magnitude / Exhaustion | exact | Last clause of the recursion | None | S |
| Stop Color Mode and Custom Color | exact | Enum plus GlobalColor "Stop Custom" | Custom color is set in Globals | S |
| Smallest Timeframe Only | exact | Owner = lowest slot with a post-Lead stop; stops folded at break even still count | None | S |
| Stop/trigger fold at break even | exact | Fold only when the trigger was drawn at compute time; " + STOP" appended to a visible trigger bubble | None | S |
| **Table and panels** | | | | |
| Show Data Table | equivalent | Fixed set of AddLabel calls gated by inputs | Shares the label strip with other studies; no frame | S |
| Mode: Full / Compact | equivalent | Both layouts generated | Full-mode geometry differs | M |
| Full layout (TF, C2, C1, AS, CC; footer rows) | equivalent (probe) | Row-owned TF label, then 4 flowing cells per slot; footers row-owned | TF sits above its cells; 2 rows per slot; no gridlines | L |
| Position (9 anchors) | partial | Mapped to the 4 corners | Center and middle positions collapse to corners | S |
| Table Text Size | equivalent | Tiny and Small map to SMALL, Normal to MEDIUM, Large to LARGE | Tiny renders the same as Small | S |
| Cell colors and palette | equivalent | Filled cells; Dark/Light palette input; opaque footers; the table's hard-coded 1u/1d/2u/2d colors copied | No colored text on black; no translucency | M |
| TF in-force highlight, Color TF When In-Force | equivalent | Reads unfiltered signalInForce, or 3 Exp on with a CC 3 | Off state is gray, not near-black | M |
| C2 and C1 columns | exact | StratState on [3]/[2] and [2]/[1], shifted by one in preview | Filled cell | S |
| AS column (HAM/SHO/INS) | exact | Priority chain | A blank cell still needs a " " label | S |
| CC column and `?` | equivalent | StratState on [1]/[0]; `?` on a neutral "unknown" color | Not yellow text on black | S |
| Lower-than-chart slots greyed | exact | Dim label, blank cells | Dim color chosen for readability | S |
| Compact chips (Bar State, Signals In Force, no-data, preview) | equivalent | Flowing labels; gray off state; `<TF> ?` for preview | Text color set by the platform | S |
| FTFC row (and Universal wording) | partial | Row-owned label from served closes against opens over usable slots | Lower slots excluded (Pine's values there repaint) | M |
| FTFC row with Exhaustion Excludes | exact | Crossed flags | None | M |
| Lead row | exact | See Lead anchor | Opaque background | S |
| PREVIEW MODE banner | equivalent | Yellow row-owned label | Filled yellow | S |
| Debug panel container | equivalent | Its own corner with row-owned section headers, or two multi-line bubbles | No frame; YES/NO colors neutral (see Decisions) | M |
| Timeframe to Debug | equivalent | One debug engine instance fed the selected slot's inputs | N/A for disabled or lower slots, instead of stale values | M |
| Debug panes: bar types, patterns, F2, terms, gates, draw decisions | exact | Same booleans as named plots, same sections, captured before the Lead filter | Layout only | M |
| Mobile rendering | partial (probe) | Test labels, bubbles, clouds and price color | Unknown | S |
| **Bar coloring** | | | | |
| Color Chart Candles mode | exact | AssignPriceColor; Color.CURRENT for none | Only one study can paint a bar | S |
| Strat Candles classification and family toggles | exact | Lite StratState; globals renamed to Style roles so one palette is shared | Colors edited in Globals | S |
| FTFC Candles | partial | Chart close > `open(period)` over usable slots | Lower slots excluded (Pine repaints there) | S |
| FTFC colors, Color Conflict Bars | exact | Global colors; Color.CURRENT when off | None | S |
| Highlight Failing 2 Flips | equivalent | Per-slot running H/L from chart bars since the period start; flip taken from raw FTFC without the no-data guard | Period key comes from dates and values | M |
| No-data guard and history horizon | equivalent | ftfcAny guard | Deep history graded with all slots (more complete than Pine) | S |
| **Preview and straddle** | | | | |
| Preview Mode: Off | exact | Disables preview and straddle | None | S |
| Preview Mode: On | partial | D and intraday slots always shift; calendar slots shift only on the last session of their period (computed holiday calendar) | Holiday-shortened periods depend on the computed calendar; one bar early | M |
| Auto: asset class (syminfo.type) | equivalent | Option test (`GetUnderlyingSymbol() != GetSymbol()`), `TickValue()/TickSize() > 1`, evening-bar test, manual override | Uses metadata proxies, not an asset-type field | M |
| Auto: closed-market windows and lastBarClosed veto | partial | Arms when the last bar is the session's final bar: equity RTH end, 20:00 ET for ETH charts (5 PM PT), 17:00 ET for CME (2 PM PT). Early-close dates computed. Chart-size cutoff input, default 15m | Arms up to one chart bar early; never arms on D+ charts; follows the last printed bar, not the clock | M |
| Auto: 4h holiday/outage fallback | impossible | Not built; use On | Halts and 24x7 outages never arm Auto | S |
| Bar replay | exact | No detection needed; data-only logic replays as it would live | Users need not switch Preview to Off | S |
| Straddle detection (HTF-STRADDLE-1) | partial | Data test only on charts above D: served close differs from the chart close, or chart extremes fall outside the served range, plus a structural guard | Fires at the new period's first print, not at the scheduled close | M |
| Preview shift (applyPreviewShift, synthetic CC) | exact | Per-slot offset selectors; synthetic CC arithmetic | None on the live bar | M |
| Exhaustion next channel (EXH-PREVIEW-CHANNEL-1) | partial | Next channel only when a straddle is proven; On keeps the current channel | A weekend with Preview On uses the current channel where Pine uses the next | M |
| Preview state hygiene (P1-d, PREVIEW-CROSSED-1) | equivalent | Preview applied as last-bar overrides; the synthetic CC is never hit-tested | No stored state to re-latch | M |
| Forming-candle reconstruction (T3, reconOK) | impossible | `?` fallback (Pine's own n == 0 path); probe MONTH [-1] | A straddled M/3M/6M/12M CC shows `?` | S |
| Preview-slot exclusions (open line, Domino, Lead, alerts) | exact | Per-slot isPreview bit | None | S |
| **Alerts** | | | | |
| Edge state (was-state arrays, P1-c reset) | equivalent | As-of engine: chart bar [1] against the last bar, with as-of CC, FTFC, Lead and crossings; a new period resets | Matches Pine's committed realtime state; no burst on load unless enabled | L |
| Pine's re-fire on load (Stops off) | equivalent | Optional `Alert(..., Alert.ONCE)` | Opt-in | S |
| Consolidated message (Signal In-Force alert()) | equivalent | One Alert with inline text; segments in slot order 1 to 6, each with setup bull, setup bear, F2 bull, F2 bear | Separators placed from flags | XL |
| Alert segment text (buildAlertLabel combos) | equivalent | Generated separately from chart combos (no C3 on double inside, no "3" prefix on 2-2 after a 3, ccType printed as 3u/3d) | None | M |
| Alert TF checkboxes (U2) | exact | Gate the consolidated text and the Any/Potential conditions | None | S |
| Detail fields: trigger, MAG, EXH, STOP (P1-j) | equivalent | A field prints when its pre-Lead draw flag was set | Bond futures print in 32nds | M |
| Include FTFC suffix | exact | Raw FTFC; "Conflict" in both label styles | Lower slots excluded | S |
| Preview/straddle alert suppression | exact | AND not preview | None | S |
| Lead filter on alerts | equivalent | As-of Lead at [1] | None on the live bar | M |
| F2 alerts obey the toggle (P0-3) | exact | Same input gates display and alerts | None | S |
| Signal In-Force (Any, TF1-TF6, Bullish, Bearish), New Potential (Any) | equivalent | One Alert per condition behind inputs; messages "TF1 In-Force" etc. | Chosen in study settings, not in an alert dialog | M |
| Domino alert and Include in Consolidated | equivalent | Bitmask signature; fires on change | Message built from the bits | M |
| FTFC Shifted / Up / Down / Conflict | equivalent | Strict close > open; was-state from primary-mirrored opens | Lower slots excluded; zero-slot case per Decisions | M |
| Alert frequency | equivalent | ONCE, BAR, TICK; once-per-bar-close emulated from the [1] edge | No once-per-minute | S |
| Glyphs (green/red circles, arrows, diamond) | equivalent (probe) | Unicode with an ASCII fallback input ("(bull)", "(bear)", "^", "v") | Possibly ASCII | S |
| Delivery (server alerts, push, webhooks) | partial | Local Alert() popup and sound; single-TF companion study for MarketWatch and Stock Hacker alerts (push, email, SMS); symbol prefix in text | Desktop must be open; no webhooks; server alerts cover one timeframe only | L |

## Hard limits

1. **Slots below the chart timeframe (partial: FTFC row, FTFC Candles, signal FTFC filters).** Limit: "secondary aggregation period cannot be less than the primary" (Chapter 11). Equivalent: the slot is greyed and gets no vote.
   - Pine calls request.security with lookahead_on, which returns the first intrabar on history and the last in realtime (Pine FAQ). Pine's own votes for these slots therefore change on reload, so excluding them is the non-repainting behavior.
   - The default Classic preset on a 1H chart hits this case through its 30m slot.
2. **8H, 12H, 6M and custom timeframes (equivalent via synthesis; partial for arbitrary custom TFs).** Limit: no such AggregationPeriod constants, and custom millisecond values are invalid as `period=` (AggregationPeriod reference; thread 12791).
   - Equivalent: 8H and 12H from chart-bar buckets on charts of 1H or less; 6M from quarter pairs; N-minute buckets when the chart divides N.
   - Seconds timeframes, 2W and arbitrary multi-month timeframes remain impossible.
3. **Futures 4H alignment.** Limit: thinkorswim aggregates last-price bars from midnight CT (Time Charts manual; thread 20693), so the native 4H runs 01/05/09/13/17/21 ET. Its 2H bars break on odd ET hours. Equivalent: 18:00-ET buckets on charts of 1H or less; futures 4H and 12H are greyed on 2H+ charts.
4. **Non-time charts.** Limit: a time aggregation plots nothing on tick charts, and GetAggregationPeriod returns ticks or price units there (thread 7473; GetAggregationPeriod reference). Equivalent: a single "time-based charts only" label. If the whole study blanks, ship a candles-only companion study.
5. **No wall clock.** Limit: GetTime is the bar's time and GetLastDay is the last bar's day. There is no "now", and studies recalculate only on ticks (GetTime, GetLastDay; threads 19777, 8675).
   - Preview Auto becomes a session-final-bar proxy (partial).
   - The 4h holiday fallback is impossible.
   - Calendar gating under Preview On uses a computed US holiday calendar (partial).
   - The next exhaustion channel is used only when a straddle is data-proven (partial).
6. **Straddle detection on charts at or below D (partial).** Limit: no clock, and the served D/W/M close can be a settlement close that differs from the intraday close (futures chart settings). Equivalent: detect on W and multi-day charts only. On lower charts a real straddle cannot occur, because a new-period print opens a chart bar in the new period.
7. **Forming-candle reconstruction (impossible).** Limit: no lower-timeframe data at all (Chapter 11). Equivalent: `?` with the slot still preview-flagged. If thinkorswim maps a straddling W bar to the new month, served M[0] is already the true forming candle and this is moot (probe).
8. **History depth (partial).** Limit: there is no per-request depth, and intraday charts load at most 360 days (Time Charts manual). It is unverified whether secondary data extends past the chart range. Equivalent: a depth-check label plus a documented chart range per preset. Monthly and larger exhaustion from intraday charts may be unavailable.
9. **Labels and table geometry (partial).** Limits:
   - AddLabel has 4 locations only (Location).
   - Label and bubble text is black on the dark theme and white on the light theme (threads 10176, 12763).
   - The smallest label font is FontSize.SMALL.
   - AddChartBubble has no size argument.

   Equivalents: corner mapping, filled cells, a Dark/Light palette input, the Text Size input mapped onto three sizes, and the bubble size input dropped.
10. **Transparency (partial).** Limit: AddCloud has no alpha and CreateColor is RGB only (references; thread 5721). Equivalent: colors pre-blended toward a Chart Background input, stacked clouds, and edge plots for the border.
11. **Strings (equivalent).** Limit: a def cannot hold a string and a script cannot return one (thread 7377). Equivalent: numeric codes rendered inline inside AddLabel, AddChartBubble and Alert text (Chapter 14), with consolidation driven by numeric keys.
12. **Per-plot style (equivalent).** Limit: painting style is fixed per plot (thread 1072). Equivalent: a solid plot and a dashed plot per trigger, with SetHiding choosing between them.
13. **Expansion area (equivalent).** Limit: at most 1000 bars (time axis settings). Equivalent: lines and timeline labels clamp at the edge.
14. **Alert delivery (partial).** Limits:
    - Study Alert() is local, cannot go to phone or email, and fires for every open chart carrying the study (threads 6006, 5430).
    - Scans, watchlists, chart alerts and conditional orders reject multiple timeframes (threads 15110, 6006).
    - A MarketWatch study alert needs exactly one plot (studyalerts).
    - There is no webhook channel.

    Equivalent: local Alert() plus a single-TF companion study.
15. **Alert frequency (equivalent).** Limit: only Alert.ONCE, BAR and TICK exist (Alert reference). Equivalent: once-per-bar-close emulated from the prior bar's edge; no once-per-minute.
16. **Glyphs (equivalent, probe).** Limit: some Unicode characters render as rectangles (threads 7155, 21325). Equivalent: an ASCII fallback input.
17. **Heikin Ashi and mobile (partial, probe).** Behavior is unverified; results will be documented after testing.

## Build order

Each layer lands only after its verification passes. HTF correctness comes before anything that reads HTF data.

**Layer 1 (exists, v0.2.0 beta): classifier, Strat/FTFC candles, label strip, FTFC label, presets.**
- Remaining work: run the `thinkorswim/README.md` checklist in the app. Beta testers run it through `TESTER_GUIDE.md`, which also collects probes P1, P4, P6, P10 and P22 with `probes/TheStratSuite_TOS_Probes_v0.2.0.txt`.
- Depends on: nothing.
- Verify against TradingView:
  - SPY 15m Strat Candles bar for bar;
  - strip vs Compact table;
  - ES 1H 4H/D boundaries;
  - a reload test.

**Layer 2: probes and the generator.**
- Contents:
  - probe studies P1-P12 (Open questions);
  - a Python generator under `thinkorswim/` that emits the study from templates, including the context-rule lint;
  - the test_tos_parity.py interpreter extended beyond StratState.
- Its first output must regenerate layer 1 (v0.2.0) with identical classifier and preset behavior.
- Depends on: layer 1 compiled.
- Verify: probe results recorded, and the generated layer 1 passes the existing parity test.

**Layer 3: HTF data core.**
- Contents:
  - per-slot pure-secondary data (CC, C1-C4), valid-constant clamps, and the lower-slot, data and non-time gates;
  - new-period detection and trade-date keys;
  - the as-of forming candle, with the equity session gate;
  - synthesized futures 4H, 8H, 12H and 6M;
  - equity 1H/4H anchoring per probe;
  - the history-depth label;
  - per-slot C1-C4 selectors, pass-through until layer 10.
- Depends on: layer 2 probes P1-P5.
- Verify against TradingView's Data Window, using a temporary debug label printing each slot's CC and C1-C4 OHLC:
  - SPY 15m RTH and 5m ETH;
  - ES 5m with the Futures/Crypto preset (12H), across the 17:00-18:00 ET break (2-3 PM PT);
  - a W chart;
  - OnDemand replay of Jul 3-6 2026;
  - a reload test.

**Layer 4: detection engine and debug panel.**
- Contents: HamSho, F2 and pre-F2, the tree, the seven families, suppressors, in-force terms, tokens, raw FTFC, Lead anchor and fin* filtering, Domino, and the debug panel (the engine's named plots).
- Depends on: layer 3.
- Verify:
  - Python truth tables generated from the Pine functions (tree, HamSho, suppressors, Lead, Domino) against the thinkScript interpreter;
  - in the app, the debug panel against the Suite's debug panel for the same slot and moment on SPY 15m and ES 5m.

**Layer 5: table and footers.**
- Contents: Full and Compact layouts, TF highlight, AS column, CC `?`, lower-slot rows, and the Domino, FTFC, Lead and PREVIEW rows.
- Depends on: layer 4.
- Verify: live against the Suite's table in both modes during RTH, including an inside bar, a Lead and a Domino.

**Layer 6: targets.**
- Contents: exhaustion scan (current channel, warmup gate), magnitude, crossed latches, Exhaustion Disables In Force, Exhaustion Excludes FTFC, outside-bar targets.
- Depends on: layer 4.
- Verify: MAG and EXH prices against the Suite's labels on SPY, ES and QQQ for D, W and M slots from 15m and 1H charts, plus one young symbol to confirm the na warmup.

**Layer 7: level rendering and chart labels.**
- Contents:
  - trigger, open, F2-open, MAG and EXH plots;
  - anchors, line end and cross-timeframe suppression;
  - combo text, Universal style, consolidation, timeline and floating bubbles, price and H/L.
- Depends on: layers 5-6.
- Verify: generated label strings compared to Pine strings in the Python harness, and a screenshot comparison with TradingView on the same bars.

**Layer 8: stops and TAW.**
- Contents: as-of sticky stops, CC/C1 reference, break even, color mode, Smallest-Only, stop fold, TAW clouds with blended opacity.
- Depends on: layers 6-7.
- Verify: stop prices live against TradingView, and a reload test. The expected difference is that thinkorswim keeps a latch that Pine drops (see Decisions).

**Layer 9: F2 flip highlight.**
- Depends on: layer 3 (as-of range).
- Verify: bar for bar against FTFC Candles with flips for one full session.

**Layer 10: preview and straddle.**
- Contents: Off/On, the Auto proxy with its cutoff, the computed US holiday and early-close calendar, the straddle detector on W+ charts, the shift, the channel choice, and preview exclusions.
- Depends on: layers 3-8.
- Verify:
  - Friday close on a 1m chart (Auto);
  - a weekend with On;
  - a W chart across the Sep 30 / Oct 1 2026 month and quarter roll;
  - Thanksgiving week, Nov 26-27 2026, including the 13:00 ET early close (10 AM PT);
  - OnDemand.

**Layer 11: alerts.**
- Contents: the as-of edge engine, the consolidated message, all 15 alertcondition equivalents, detail fields, frequency, Alert.ONCE load option, and the single-TF companion study.
- Depends on: layers 4-10.
- Verify: one session side by side, logging the time and text of every TradingView and thinkorswim alert; a reload with no duplicate fires; an OnDemand replay.

**Layer 12: performance and packaging.**
- Contents:
  - measure load time on 5D:1m and 180D:1H;
  - apply the if-statement gating;
  - decide one study versus a split;
  - README, settings reference, parity tests in CI.
- Verify: the README checklist, extended to the full port.

## Decisions for Nate

Items found in the Pine and the docs while mapping. Each needs a copy-or-fix call before the matching layer.

1. **2-2 Reversal F2 filter** (pine:1400-1401). c1_was_f2d/f2u is a hard-coded Reclaim test. It ignores Enable Failing 2 Detection and the Detection Method, and it uses a strict `>` where the label's F prefix uses an inclusive `>=`. A label can read F2d-2u while the filter hides the setup.
2. **F2 open line orphan under the Lead filter** (pine:2309-2320 against 2161/2168). The line draws while its label is suppressed, which breaks DESIGN_CONSTRAINTS item 3. Recommendation: thinkorswim gates the line on the label's condition, and the Pine gets the same fix.
3. **Sticky stop after reload** (pine:1351, 1478-1503). Pine drops a stop that latched live once price is back under the trigger at reload time. The thinkorswim as-of latch keeps it. Recommendation: keep the thinkorswim behavior and fix the Pine with the paintSlotFailed2 pattern.
4. **Lower-timeframe slots in FTFC** (pine:1064, 3322). This makes the Pine repaint. Recommendation: gate calculateFTFC by validTimeframe in the Pine; the thinkorswim FTFC then matches exactly.
5. **FTFC with zero voting slots.** Pine returns up and down both true, which shows "FTFC Up" and blocks every FTFC-filtered family. Lite shows no FTFC. Pick one rule.
6. **Stop-only slot timeline label** (pine:1719-1762, 2135). Pine never places it; thinkorswim would show it. Recommendation: show it and fix the Pine.
7. **Alerts on load with Stops off** (pine:1351, 1716-1727). Pine re-fires every in-force signal and Domino on the first realtime tick. Choose the default for the Alert.ONCE option.
8. **Preview Auto on thinkorswim.** The proxy arms up to one chart bar early, which conflicts with the AUTO-SESSION-1 veto. Options:
   - proxy with a chart-size cutoff (default 15m);
   - proxy with a "PREVIEW MODE (early)" banner;
   - no Auto at all.
9. **Table colors.** The table's hard-coded 1u/1d/2u/2d colors ignore the Style inputs, while the candles honor them.
10. **Debug colors.** Debug YES/NO uses green/red fills, which conflicts with green/red = bull/bear. Recommendation: neutral fills.
11. **Universal alert wording.** A bullish reclaim alert reads "↑FAILING", while the chart puts FAILING on the failed side.
12. **Consolidation default.** Exact merged text (larger study) or adjacent bubbles.
13. **Names for thinkorswim-only timeframes.** Raw strings ("10") per Pine's fallthrough, or "10m".
14. **Docs fixes (no code impact):**
    - reading-labels.md shows 2d-1-2u where the code prints 2-1-2u; decide which is intended.
    - signals.md says "*OUTSIDE"; the code prints "*OUT".
    - The "No Lead" row was removed by LEADROW-1.
    - bar-types.md describes the Classic hammer wrongly: the code puts the body and close near the high.
    - The Settings Reference still says "3x chart period or 4h"; it is now 4h.
    - repaint-prevention.md and OPEN_ISSUES say 23 tuple values; there are 27.
    - features.md mentions a table "Universal view"; only the FTFC row changes.

## Open questions to settle in the app

Where the mappers and verifiers disagreed without decisive evidence, the question is listed here with its test.

1. **P1: offset context.** Chapter 11, the StanL collection and thread 154 disagree on this. On a /ES 5m and a SPY 5m chart, define:
   - `def d = close(period = AggregationPeriod.DAY);`
   - `def m = d + 0 * close;`
   - `def p = if GetYYYYMMDD() > 0 then close(period = AggregationPeriod.DAY) else 0;`

   Label `d[1]`, `close(period = AggregationPeriod.DAY)[1]`, `m[1]`, `p[1]`, and `if IsNaN(close) then 0 else close(period = AggregationPeriod.DAY)[1]`. Yesterday's close means HTF stepping; today's running close means chart-bar stepping. Every generator rule depends on this result.
2. **P2: computed period.** On 5m, 1H and D charts, plot `close(period = Max(AggregationPeriod.THIRTY_MIN, GetAggregationPeriod()))`, an if-chain def, and the `script plotClose { input pag = 15; ... }` form. Repeat on a 45m custom chart to confirm that custom values are rejected.
3. **P3: fold and GetValue on secondary data.** In a pure def, compare `fold i = 1 to 4 with s do s + GetValue(high(period = AggregationPeriod.DAY), i)` with the sum of `high(period = AggregationPeriod.DAY)[1]` through `[3]`. This decides fold versus unrolled scans.
4. **P4: history depth.** Label `!IsNaN(high(period = X)[4])` and `[51]` for WEEK, MONTH, QUARTER and YEAR on 1D:1m, 5D:5m, 20D:15m and 360D:1H charts.
5. **P5: 4H and 2H alignment.** Mark every change of `open(period = AggregationPeriod.FOUR_HOURS)` on /ES 1H and on SPY RTH (start at market open on and off) and SPY ETH. If /ES 4H starts at 18:00 ET, 12H can instead be composed from native 4H offsets on charts up to 4H.
6. **P6: pre-market D mapping.** On a SPY ETH 5m chart, check whether `open(period = DAY)` at 07:00 ET (4 AM PT) is today's or yesterday's daily open. Also check whether D stays RTH-only.
7. **P7: month roll on a W chart, Sep 30 / Oct 1 2026.** Check `open/close(period = MONTH)` and `close(period = MONTH)[-1]` on the straddling weekly bar against TradingView. This decides whether reconstruction or the `?` fallback is needed.
8. **P8: expansion area.** Test whether GetTime() and BarNumber() advance on expansion bars, whether a bubble at `BarNumber() == lastBN + 5` renders, and what the time axis "Studies" auto-expansion option does.
9. **P9: row ownership.** Two row-owned labels with four flowing labels between them: do the four stay on one row, in order, above or below?
10. **P10: glyphs.** Test ↑ ↓ ◆ ♦ and the two circle emoji in AddLabel, AddChartBubble, Alert and the Message Center, with "Use System Fonts" on and off.
11. **P11: tick charts.** On a 1000-tick chart, does a study with one secondary aggregation plus AssignPriceColor blank entirely, or only the secondary plot?
12. **P12: script instance cost.** Compare 6 slots times about 50 `Slot(aN).x` references against inline defs, by load time on 5D:1m.
13. **Alert text.** Find the maximum Alert text length, confirm nested string if-expressions inside Alert, confirm that one study on two charts fires twice, and check that GetSymbol() works in the text.
14. **Calendar functions.** Does `RegularTradingEnd` on Nov 27 2026 return 13:00 ET? Does `CountTradingDays` over future dates count scheduled sessions?
15. **Futures settlement close.** On /ES, compare `close(period = DAY)` on a 5m chart with the chart close after 17:00 ET under "Daily Close: Settlement" and under "Last".
16. **Evaluation cost.** Time an exhaustion scan gated by an if-statement against the same scan in an if-expression.
17. **Input-only style arguments.** Check that SetLineWeight and SetStyle accept an input-only if-expression (preset widths).
18. **AddCloud.** Confirm that a NaN on either series suppresses the fill, and estimate the native alpha by overlaying known colors on black and on white.
19. **HighestAll load.** Time 150 HighestAll broadcasts against the SetHiding-first design on 5D:1m.
20. **Holiday glue.** Compare /ES D and W for Thanksgiving week 2026 (Nov 26-27) and in an OnDemand replay of Jul 3-6 2026 against TradingView.
21. **OnDemand.** Is the HTF [0] served as of the replay time or as the day's final values?
22. **Label contrast.** Check the platform text color on every Suite fill color under both the Dark and Light themes.
23. **Bond futures.** Check `AsPrice` and `TickSize()` output on /ZN and /ZF.
24. **Thin symbols.** Does `close(period = DAY)` return NaN on a lightly traded name when 1m bars are missing?
25. **Heikin Ashi.** Does a study on an HA chart read real or HA OHLC?
26. **Mobile.** Which of labels, row ownership, bubbles, clouds and AssignPriceColor render in the thinkorswim mobile app?
27. **Study size.** If one study loads too slowly, does referencing a second custom study's plots work, and at what cost?

## Sources

Official thinkScript reference and thinkManual:
- https://toslc.thinkorswim.com/center/reference/thinkScript/tutorials/Advanced/Chapter-10---Referencing-Historical-Data
- https://toslc.thinkorswim.com/center/reference/thinkScript/tutorials/Advanced/Chapter-11---Referencing-Secondary-Aggregation
- https://tlc.tdameritrade.com.sg/center/reference/thinkScript/tutorials/Advanced/Chapter-11---Referencing-Secondary-Aggregation
- https://toslc.thinkorswim.com/center/reference/thinkScript/tutorials/Advanced/Chapter-13---Referencing-Other-Data
- https://toslc.thinkorswim.com/center/reference/thinkScript/tutorials/Advanced/Chapter-14---Concatenating-Strings
- https://toslc.thinkorswim.com/center/reference/thinkScript/tutorials/Basic/Chapter-8---Formatting-Output
- https://toslc.thinkorswim.com/center/reference/thinkScript/tutorials/Appendices/Appendix-A---Creating-Local-Alerts
- https://toslc.thinkorswim.com/center/reference/thinkScript/Constants/AggregationPeriod
- https://toslc.thinkorswim.com/center/reference/thinkScript/Constants/Curve
- https://toslc.thinkorswim.com/center/reference/thinkScript/Constants/Location
- https://toslc.thinkorswim.com/center/reference/thinkScript/Constants/FontSize
- https://toslc.thinkorswim.com/center/reference/thinkScript/Reserved-Words/script
- https://toslc.thinkorswim.com/center/reference/thinkScript/Reserved-Words/if
- https://toslc.thinkorswim.com/center/reference/thinkScript/Reserved-Words/fold
- https://toslc.thinkorswim.com/center/reference/thinkScript/Reserved-Words/reference
- https://toslc.thinkorswim.com/center/reference/thinkScript/Declarations/once-per-bar
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Fundamentals/open.html
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Others/GetAggregationPeriod
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Others/Alert
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Others/CompoundValue
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Others/GetValue
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Others/TickSize
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Others/TickValue
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Others/AsPrice
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Others/GetSymbol
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/GetTime
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/GetYYYYMMDD.html
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/GetMonth
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/GetWeek
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/GetDayOfWeek
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/GetLastDay
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/SecondsFromTime
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/RegularTradingEnd
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Date---Time/CountTradingDays
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Tech-Analysis/HighestAll
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/AddLabel
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/AddChartBubble
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/AddCloud
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/AssignPriceColor
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/CreateColor
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/DefineGlobalColor
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/SetStyle
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/SetLineWeight
- https://toslc.thinkorswim.com/center/reference/thinkScript/Functions/Look---Feel/SetHiding
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/charts/Chart-Aggregation/Time-Charts
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/charts/Chart-Aggregation/Tick-Charts
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/charts/Chart-Style-Settings/timeaxis
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/charts/Chart-Style-Settings/equities
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/charts/Chart-Style-Settings/futures
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/MarketWatch/Alerts
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/MarketWatch/Alerts/studyalerts
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/Scan/Stock-Hacker
- https://toslc.thinkorswim.com/center/howToTos/thinkManual/Trade/Order-Entry-Tools/Order-Types/thinkScript-in-Conditional-Orders

usethinkscript.com:
- https://usethinkscript.com/threads/aggregationperiod-variable.10625/
- https://usethinkscript.com/threads/how-can-i-make-this-previous-day-low-high-script-work-on-all-time-frames.12279/
- https://usethinkscript.com/threads/input-aggregationperiods.6374/
- https://usethinkscript.com/threads/custom-aggregation-periods-for-thinkorswim.12791/
- https://usethinkscript.com/threads/previous-day-high-low-close-for-thinkorswim.3494/
- https://usethinkscript.com/threads/previous-day-high-and-low-breakout-indicator-for-thinkorswim.154/
- https://usethinkscript.com/threads/important-note-for-mtf-studies-utilizing-script-for-secondary-aggs.969/
- https://usethinkscript.com/threads/secondary-aggregation-periods-and-using-built-in-functions-against-them.14702/
- https://usethinkscript.com/threads/mtf-multi-timeframe-repainting-pitfalls.16359/
- https://usethinkscript.com/threads/multi-supertrend-with-no-repaint-htf-for-thinkoswim.14445/
- https://usethinkscript.com/threads/multi-timeframe-candles-overlay-for-thinkorswim.1425/page-10
- https://usethinkscript.com/threads/can-i-aggregation-period-in-tick-chart.7473/
- https://usethinkscript.com/threads/does-the-four-hour-candle-open-different-on-thinkorswim.20693/
- https://usethinkscript.com/threads/answers-to-commonly-asked-questions.6006/
- https://usethinkscript.com/threads/how-to-pass-a-string-parameter-of-a-script-to-another-script.7377/
- https://usethinkscript.com/threads/how-to-pass-text-to-labels-and-bubbles-in-thinkorswim.21781/
- https://usethinkscript.com/threads/supported-unicode-symbols-for-chart-labels.7155/
- https://usethinkscript.com/threads/addlabel-text-alt-codes-for-thinkorswim.21325/
- https://usethinkscript.com/threads/addlabel-labels-text-color-in-thinkorswim.10176/
- https://usethinkscript.com/threads/addchartbubble-text-color.12763/
- https://usethinkscript.com/threads/black-bubbles.3799/
- https://usethinkscript.com/threads/relocate-labels-in-thinkorswim.9475/
- https://usethinkscript.com/threads/plotting-bubbles-to-the-right-of-horizonatl-lines.13901/
- https://usethinkscript.com/threads/plotting-in-the-expansion-area.9765/
- https://usethinkscript.com/threads/expansion-area-ma-lines-bubble.11201/
- https://usethinkscript.com/threads/single-horizontal-line-with-specified-left-right-extensions.15575/
- https://usethinkscript.com/threads/define-a-desired-painting-strategy-with-a-number-or-an-input.13703/
- https://usethinkscript.com/threads/thinkscript-setpaintingstrategy-with-multiple-conditions.1072/
- https://usethinkscript.com/threads/how-to-use-a-variable-as-length.10110/
- https://usethinkscript.com/threads/clouds-with-if-then-else-for-thinkorswim.12487/
- https://usethinkscript.com/threads/change-opacity-of-cloud.5721/
- https://usethinkscript.com/threads/how-to-modify-thinkscript-transparent-opacity-color.2625/
- https://usethinkscript.com/threads/thinkscript-size-limits.8838/
- https://usethinkscript.com/threads/maximum-amount-of-lines-in-a-script.8258/
- https://usethinkscript.com/threads/how-efficient-is-thinkscript-code.11579/
- https://usethinkscript.com/threads/too-complex-any-thoughts-overcoming-processing-limitations.15076/
- https://usethinkscript.com/threads/thinkorswim-marketwatch-quotes-alert-too-complex.924/
- https://usethinkscript.com/threads/is-theres-a-way-to-determine-if-a-symbol-is-an-option-or-an-equity.6569/
- https://usethinkscript.com/threads/currenttime-now-in-a-scan-filter.19777/
- https://usethinkscript.com/threads/access-current-time-in-second-granularity.8675/
- https://usethinkscript.com/threads/why-secondary-aggregations-do-not-work-in-scan-hacker-or-watchlist-columns-in-thinkorswim.15110/
- https://usethinkscript.com/threads/secondary-period-not-allowed.9747/
- https://usethinkscript.com/threads/how-to-create-study-alert-with-2-different-timeframe-studies.12150/
- https://usethinkscript.com/threads/how-to-receive-thinkorswim-alert-notifications-via-phone.5430/
- https://usethinkscript.com/threads/does-the-alert-still-show-in-your-message-center-when-you-have-thinkorswim-minimized.13576/
- https://usethinkscript.com/resources/how-to-add-alert-script-to-thinkorswim-indicators.9/

Other:
- https://www.tradingview.com/pine-script-docs/faq/other-data-and-timeframes/
- https://jshingler.github.io/TOS-and-Thinkscript-Snippet-Collection/TOS%20&%20Thinkscript%20Collection.html
- https://thinkscript101.com/thinkscript-get-current-date-time/

Repository files:
- pine/TheStratSuite_v3.1.1.pine
- grammar/TheStratGrammar.pine
- grammar/tests/test_tos_parity.py
- thinkorswim/TheStratSuite_TOS_v0.2.0.txt (layer 1; v0.1.0 was TheStratSuite_Lite_TOS_v0.1.0.txt)
- thinkorswim/README.md
- docs/engineering/ (repaint-prevention.md, htf-correctness.md, drawing-decisions.md, rendering.md, performance.md)
- docs/DESIGN_CONSTRAINTS.md
- docs/OPEN_ISSUES.md
- docs/TheStratSuite_v2.2.7_Settings_Reference.md