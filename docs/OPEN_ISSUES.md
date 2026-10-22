# Open issues

Confirmed problems not yet fixed. Each entry states the evidence, so nobody has to
re-derive it. Remove an entry when it ships, with the `FIX` tag that closed it.

## Fractional-tick instruments print decimal prices in alerts and labels

**Severity:** real bug, narrow blast radius. Wrong on the bond complex, correct everywhere else.

Five sites format prices with `str.tostring(price, format.mintick)`:

- `pine/TheStratSuite_v3.1.0.pine:2042` — the price appended to a chart label when
  *Show Price in Label* is on
- `pine/TheStratSuite_v3.1.0.pine:2828, 2832, 2836, 2840` — the trigger, magnitude,
  exhaustion and stop prices in enriched alert messages

`format.mintick` rounds to the symbol's tick and renders the result as a decimal. On
instruments TradingView displays fractionally, the two disagree. On ZB (mintick 1/32
of a point) an alert reads `STOP 110.234375` while the chart shows `110'07'5`. A trader
cannot match the alert to the chart without doing arithmetic.

Affected: ZB, ZN, ZF, ZT and the rest of the Treasury complex. Unaffected: equities,
indices, crypto, FX, and every other decimal-quoted symbol, which is nearly all use.

**Fix:** write an `asFractional` formatter, roughly 100 lines of deterministic
arithmetic, and unit-test it against ZB1! quotes. Do not import
`n00btraders/PriceFormat` for this: its signature is
`formatPrice(price, precision, language, allowPips)`, and Pine cannot read the chart's
precision or language, so importing it forces two user-facing inputs into the settings
panel to configure something the platform already knows. Only the fractional branch is
needed. (Prior art reviewed 2026-08-18.)

**Where it belongs:** PineDraw, so the Suite and any other script built on PineDraw share one implementation.

## HTF requests are not guarded by the requested timeframe

`pine/TheStratSuite_v3.1.0.pine:963-968` issue six `request.security` calls with the
user's timeframe strings unconditionally. `validTimeframe`
(`timeframe.in_seconds() <= timeframe.in_seconds(tf)`, line ~1230) gates *consumption*,
so a slot set below the chart timeframe is never acted on. PineCoders recommends
blocking the request itself rather than the result.

**Deliberately not fixed yet.** Correctness is already handled at consumption, the
change touches the most repaint-sensitive code in the project, and it should not ride
along with a release that is already migrating classification to a library. Do it as
its own change, with its own verification pass, on a chart where a slot is genuinely
set below the chart timeframe.

## Per-bar cost, found in the 2026-08-25 external review

All four predate v3.1.0 and none were introduced by the library migration. Logged rather than
fixed because each is a real refactor and this release was already audited; they belong in a
release of their own.

- **All six `request.security()` calls execute even for disabled slots**, and the requested
  expression includes `findExhaustionLevels()` - up to 47 loop iterations per requested bar.
  Fixing this means requesting conditionally, which first requires collapsing the 23-element
  tuple into a UDT.
- **Six `TFRawData` objects are constructed every chart bar** by `applyPreviewShift`, even with
  preview off, plus one `ProcessingResult` per enabled slot per bar. The no-preview branch could
  reuse the requested object instead of building a new one.
- **`detectBarTypeAndFailed` calls `sg.isFailed` for structures "1" and "3"**, which the library
  rejects immediately. Gating on `s == "2u" or s == "2d"` skips a module-boundary call. Two call
  sites also render the full token string and discard it.
- **Domino processing concatenates timeframe strings on every historical bar**, and alert message
  construction runs on historical transitions even though `alert()` only fires in realtime.

### Not a defect: the 127-element `request.*()` tuple limit

The same review flagged `getDataWithExhaustion()`'s 23-element tuple across six
`request.security()` calls (138 elements) as exceeding Pine's documented 127-element aggregate
tuple cap, and called it release-blocking. It is not. The identical shape is in the published
v3.0.1 running for hundreds of users, and v3.1.0 compiled and loaded on a live chart before the
review finished. Whatever the documented rule means, it does not reject this script. Recorded so
the same finding does not get re-litigated.
