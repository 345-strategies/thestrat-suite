# Higher-Timeframe Correctness

How the Suite reads six timeframes from one chart without serving stale, misaligned, or fabricated data — and the traps in `request.security` that make this the hardest part of the codebase.

Written against `pine/TheStratSuite_v3.1.1.pine`. Companion postmortem: `../v2.2.3_holiday_glue_fix.md`. Repaint fundamentals live in `repaint-prevention.md`; this doc assumes them.

---

## The two directions of failure

- **Chart TF above the slot's TF** (asking a daily chart for 15-minute structure): the data is structurally unavailable — plain `request.security` samples the lower TF at chart-bar boundaries, so you'd classify one arbitrary 15m bar per daily candle. The Suite *refuses* this direction.
- **Chart TF below the slot's TF** (the normal case): the data is available but the mapping between chart bars and HTF bars has sharp edges — session-stamped opens, mid-bar calendar rolls, and exchange trade-date gluing. The Suite *corrects* this direction. Most of this doc is about those corrections.

---

## Rule 1 — Refuse lower-TF reads; grey them, don't hide them

`computeSignalState` gates all per-slot work on:

```pine
validTimeframe = timeframe.in_seconds() <= timeframe.in_seconds(tf)
```

A slot only computes when the chart timeframe is **at or below** the slot's timeframe. There is no fallback path — an invalid slot produces an empty result, draws nothing, and alerts nothing.

The data table makes the refusal visible instead of silent: `populateBarTypeRow` renders lower-TF rows greyed out (`isLowerTF`), so the user sees the slot exists but is inert on this chart, rather than wondering where it went. The compact table does the same. **Grey, don't hide** — a disappeared row reads as a bug; a greyed row reads as "zoom out."

---

## The mapping fact everything else follows from

`request.security` maps each chart bar to **the HTF bar in effect at the chart bar's open**. That single sentence generates every problem below:

1. If an HTF period ends *mid-chart-bar*, the served data stays one period stale until the next chart bar opens (**the straddle problem**).
2. If an instrument's sessions open before the calendar day of the period they belong to, the bar's open *timestamp* lies about which calendar period it is (**the session-stamp problem**).
3. If an exchange glues a holiday session into the next trade date, the open timestamp can lie by *days*, not hours (**the glue problem**).

---

## Rule 2 — Only calendar TFs can straddle, and staleness is a time test, not a calendar test

**Who can straddle** (`isCalendarTF`, `isStraddleTF`): only weekly-and-above ("W", "M", "3M", "6M", "12M") — their period boundaries don't have to align with chart-bar opens (a month or quarter rolls mid-week under a weekly chart bar). Intraday and daily requests from lower charts always align on session opens. And only a calendar TF **strictly coarser than the chart** qualifies — the chart's own TF can never straddle itself.

**When it's straddling** (`shouldStraddle`, final GLUE-2b form):

```pine
shouldStraddle(string tf) =>
    int tc = time_close(tf)
    not na(tc) and timenow > tc
```

One line: the served HTF bar is stale exactly when *now is past its scheduled close*. This replaced an entire calendar-key comparison apparatus, and the history of getting here is the most instructive bug chain in the project (Rule 4).

**What happens on straddle**: the same shift the preview system uses (`applyPreviewShift`) — promote the served forming bar to C1, C1 to C2, and so on down the chain, then synthesize a neutral placeholder CC spanning the promoted bar's range (open = its close, close = its midpoint), and rebase the exhaustion snapshot onto the promoted period (`FIX EXH-PREVIEW-CHANNEL-1`: the scan returns a current-period and a next-period channel, both clock-free, and the shift picks the next-period one only where the promoted bar has closed). The placeholder is not a traded range, so it is barred from hit-testing magnitude and exhaustion (`FIX PREVIEW-CROSSED-1`); a reconstructed CC is real data and hit-tests normally. The placeholder deliberately classifies as an inside bar, and the table masks it with a yellow `?` unless reconstruction (Rule 5) replaces it with real data.

The whole straddle path is armed by `straddleWindowOK = previewMode != "Off" and barstate.islast and not isBarReplay` — so `previewMode = Off` remains a full kill-switch (the bar-replay workaround), and nothing straddle-related ever touches historical bars.

---

## Rule 3 — Normalize session-stamped opens by +12h before any calendar parsing

Futures periods open around 18:00 ET on the *prior* calendar day. Parse a quarterly bar's open stamp naively and you get the wrong quarter — the probe confirmed a Q3 bar stamped `2026-06-30 18:00` parsing as Q2.

The fix is `OFF_MS` (12 hours): every timestamp entering `pkeyCal` (the calendar-period keying function) gets `+12h` first. That lands any session-stamped open in its true calendar period across futures (18:00 prior day → next morning), equities (9:30 → same day), and crypto (00:00 → same day) — without ever overshooting the day.

### Case study: `FIX RECON-KEY-1`

The reconstruction loop keyed the served daily bars with `+12h` but keyed *now* raw. For the ~7 hours between a period's session close (~17:00 ET) and calendar midnight, the two readings disagreed by one period — so at each roll, reconstruction matched the **prior** period's dailies and painted a wrong forming candle as current. The rule that came out of it: **every timestamp entering the same comparison gets the same normalization.** Mixed readings are a one-period-off bug waiting for a roll boundary.

---

## Rule 4 — Never infer staleness or calendar position from an open stamp

The bug chain that produced this rule (full narrative in `../v2.2.3_holiday_glue_fix.md`):

1. **`FIX GLUE-1`** — CME glued the Friday July 3 2026 holiday half-session into Monday July 6's trade date. Monday's *live* bar opened with a Thursday-evening stamp, so `timenow - time` read ~87h and Auto preview engaged mid-session: phantom PREVIEW MODE banner, D/W table rows showing `?`, and the forming week demoted into C1 — "weekly levels on the current day." Fix: measure the holiday fallback from **`time_close`** (`isHolidayClosed`). A live bar's scheduled close is always in the future, so the check *cannot* fire during a session; genuinely halted markets still trip it once now passes the last scheduled close.
2. **`FIX GLUE-2` (superseded same day)** — first attempt keyed the straddle test off the *served* bar's open stamp instead of the chart bar's. Better, but still calendar-parsing an open stamp.
3. **`FIX GLUE-2b`** — the glue reaches the weekly bar too: the week's bar opened Thursday 18:00 ET, ~84h before its true period — beyond what `+12h` can normalize. The calendar-key approach was unsalvageable for glued bars, so it was replaced with the pure time test in Rule 2.

The distilled principle: **open stamps encode the exchange's trade-date bookkeeping, not the calendar.** `time_close` is computed from the same trade-date calendar that stamps the bars, so it is immune to early opens, needs no week-numbering or timezone parsing, and is direction-aware — a glued session running *ahead* of the calendar can never fire it. When you need staleness: compare `timenow` to `time_close`. When you need calendar identity and only have an open stamp: `+12h` normalize (Rule 3) — and know that gluing can still defeat it, which is why staleness must never depend on it.

---

## Rule 5 — Reconstruct the forming HTF candle from daily sub-bars; keep it flagged as preview

A straddled slot during live trading shouldn't show a synthetic placeholder — the true forming HTF candle exists, just not through the stale mapping. The T3 reconstruction rebuilds it:

- `request.security_lower_tf(syminfo.tickerid, "D", …)` returns the current chart bar's completed **daily** sub-bars (`canRecon` gates this to charts above D — a constant gate, which Pine v6 dynamic requests permit).
- Filter the dailies to those whose `pkeyCal(+12h)` matches now's period; aggregate open/high/low from them.
- Splice the live close on top: `ccC := close`, `ccH := max(reconHigh, close)`, `ccL := min(reconLow, close)`.

Probe-validated against finer charts — the BTC 1W case reproduced the forming week's `f2d` exactly, including a below-C1 wick the earlier midpoint estimate missed.

Three deliberate conservatisms:

1. **`n == 0` leaves the `?` candle.** A classic weekend preview projects a period that has no dailies yet; reconstruction must never override it with nothing.
2. **`isPreview` stays `true` even when reconstruction succeeds.** The every-tick preview re-latch is what keeps the shifted C1/C2 alive across realtime rollback (see `repaint-prevention.md`, Rule 4), and alerts stay suppressed — reconstructed data is good, but it's still estimate-grade until the mapping catches up.
3. **The table trusts it visually but not semantically**: a reconstructed slot renders its CC normally (`reconOKArr`), while a non-reconstructed preview slot shows the yellow `?` (`ccUnknown`).

---

## Rule 6 — Preview mode is the same shift, armed by the market calendar

The market-closed cousin of the straddle: when nothing is trading and a new calendar period has begun, `shouldApplyPreview` applies the identical promotion so traders can plan next period's levels before the open (every calendar slot, W through 12M, previews once the served period's scheduled close has passed, via `shouldStraddle`; `FIX PREVIEW-WEEKCLOSE-1` replaced the weekly "Fri/Sat/Sun by calendar" test that fired all day Friday, and `FIX PREVIEW-CALCLOSE-1` retired the month/quarter/year calendar comparisons that misread session-stamped futures periods during the CME maintenance hour).

Auto-detection is per asset class (`isEquityLike` / `isFuturesLike` / crypto):

- **Equities/ETFs/options/indices**: before 9:30 ET, after 16:00, weekends.
- **Futures/forex/commodities**: weekend closure (Fri 17:00 → Sun 18:00 ET) and the daily 17:00–18:00 maintenance window.
- **Crypto**: 24/7 — only the holiday fallback can trigger it.
- **Everything**: the holiday fallback — now is more than 4h past the last bar's scheduled close (`isHolidayClosed`, Rule 4; `FIX AUTO-SESSION-1` flattened the old `max(3 × chart period, 4h)`, which was 72h on daily charts and never fired on an exchange holiday or early close).
- **Veto, all classes**: none of the above arms while the chart's last bar is still ahead of its scheduled close (`lastBarClosed`, `FIX AUTO-SESSION-1`). The ET windows describe US exchanges; a live bar on any exchange, session or extended-hours chart wins over the clock.
- **Chart types without a scheduled close**: on Renko, Kagi, Line Break, Point & Figure and Range charts the forming bar's `time_close` is `na`, so Auto never arms and `shouldStraddle` never fires there (under `On`, only daily and intraday slots still preview). Those chart types are unsupported: `syminfo.tickerid` hands the chart-type-modified series to the HTF slots. Heikin Ashi is time-based and unaffected here, but it is refused outright by `NO-HA-1`: its averaged OHLC would misclassify every bar, and `syminfo.tickerid` would hand the averages to the HTF slots too.

All of it is wall-clock logic, so all of it is gated `barstate.islast and not isBarReplay` and never touches history. Preview slots suppress their own open line (the open is a proxy; the gate is per slot, `FIX OPENLINE-PREVIEW-1`), and every preview/straddled slot is excluded from alerts (`slotPreview`, `okA1`–`okA6`), from the Domino chain (`FIX DOMINO-PREVIEW-1`, the synthetic CC always classifies inside) and from Lead anchoring (`FIX LEAD-PREVIEW-1`, an estimate-grade slot must not suppress a real one) — the period reset re-arms alerting cleanly on the first real bar of the new period.

---

## Contributor checklist

Before merging anything that touches timeframes, sessions, or the preview path:

1. Does new per-slot logic run behind `validTimeframe`? In the UI, does an unavailable slot grey out rather than disappear?
2. Does any code parse calendar fields (month/week/quarter) from a bar's open timestamp? `+12h` normalize it — and normalize **every** timestamp entering the same comparison identically (`FIX RECON-KEY-1`).
3. Does any code judge data staleness? Compare `timenow` against `time_close(...)`, never against `time` (`FIX GLUE-1`, `FIX GLUE-2b`).
4. Does the change preserve the reconstruction conservatisms — `n == 0` keeps the `?`, `isPreview` stays true for reconstructed slots, preview slots never alert?
5. Does `previewMode = Off` still disable the entire preview *and* straddle apparatus (bar-replay workflow)?
6. Adding a calendar TF? Extend `isCalendarTF` **and** `pkeyCal`, then verify on a session-stamped instrument (CME futures around a quarter roll), not just crypto.
