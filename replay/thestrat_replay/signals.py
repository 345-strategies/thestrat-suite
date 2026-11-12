"""Signals, magnitude and exhaustion, ported from TheStrat Suite v3.1.1 Pine.

Source of truth: pine/TheStratSuite_v3.1.1.pine
  - findExhaustionLevels()   -> exhaustion_levels()
  - detectFailed2()          -> _detect_failed2()
  - shouldDrawC1Level()      -> _should_draw_level()
  - computeSignalState()     -> evaluate()   (signal, in-force, magnitude, exhaustion parts)

The port keeps the Pine's variable names where it can, so a line here can be
checked against the line it came from. Not ported (Suite defaults leave them off
or they are display-only): stops and break-even, Lead filter, Domino, preview
and straddle shifts, line colors and styles, label text.

Hammer/shooter come from thestrat_grammar, the same library the Suite imports.
"""

from __future__ import annotations

from dataclasses import dataclass, field, asdict

from thestrat_grammar import Candle, PatternMethod, is_hammer, is_shooter

from .bars import Bar


@dataclass(frozen=True)
class SignalSettings:
    """Suite inputs that change signal logic, at the Suite's defaults."""

    showInsideReversals: bool = True
    insideRevOnlyMomo: bool = False
    insideRevRequireFTFC: bool = False
    showAllReversals: bool = True           # 2-2 reversals
    reversalsRequireActionable: bool = False
    reversalsRequireC1F2: bool = False
    reversalsRequireFTFC: bool = False
    showInsideContinuations: bool = True
    insideContOnlyMomo: bool = False
    insideContRequireFTFC: bool = False
    showContinuations: bool = False         # 2-2 continuations
    twoTwoContOnlyMomo: bool = False
    continuationsRequireFTFC: bool = False
    show_p3_toggle: bool = False            # Failing 2s as signals
    p3RequireFTFC: bool = False
    showRangeExpansions: bool = False       # 3-2 expansions
    rangeExpOnlyMomo: bool = False
    rangeExpansionsRequireFTFC: bool = False
    show3Expansions: bool = False           # outside bars
    threeExpRequireFTFC: bool = False
    showMagnitude: bool = True
    magOnlyWhenInForce: bool = False
    showExhaustion: bool = True
    exhOnlyWhenInForce: bool = False
    exhRequiresMagHit: bool = False
    exhDisablesInForce: bool = False
    show3ExpMagnitude: bool = True
    show3ExpExhaustion: bool = True
    enableFailed2Detection: bool = True
    failed_2_method: str = "Reclaim"
    logicType: PatternMethod = PatternMethod.BROAD
    onlyColor: bool = False

    @classmethod
    def all_signals(cls) -> "SignalSettings":
        """Every signal family on, as a replay user might want for study."""
        return cls(showContinuations=True, show_p3_toggle=True, showRangeExpansions=True, show3Expansions=True)


EXH_LOOKBACK = 48
EXH_MIN_BARS = 51   # Pine: bar_index > 50 inside the security context


def exhaustion_levels(completed: list[Bar]) -> tuple[Bar | None, Bar | None]:
    """The CURRENT channel of findExhaustionLevels (FIX P0-2): seeded from C1, candidates from [2].

    `completed` is the timeframe's completed bars, oldest first; Pine's high[i] is
    completed[-i] for i >= 1. Returns (exhaustion-high bar, exhaustion-low bar).
    """
    n = len(completed)
    if n < EXH_MIN_BARS:
        return None, None
    hi = lambda i: completed[-i].high
    lo = lambda i: completed[-i].low
    max_cur, min_cur = hi(1), lo(1)
    exh_h = exh_l = None
    for i in range(2, EXH_LOOKBACK + 1):
        if i + 1 > n:
            break
        pivot_h = hi(i) > hi(i - 1) and hi(i) > hi(i + 1)
        pivot_l = lo(i) < lo(i - 1) and lo(i) < lo(i + 1)
        if exh_h is None and pivot_h and max_cur < hi(i):
            exh_h = completed[-i]
        if exh_l is None and pivot_l and min_cur > lo(i):
            exh_l = completed[-i]
        max_cur = max(max_cur, hi(i))
        min_cur = min(min_cur, lo(i))
        if exh_h is not None and exh_l is not None:
            break
    return exh_h, exh_l


def _detect_failed2(c1h, c1l, o, c, h, l, s: SignalSettings):
    cc_is_3 = h > c1h and l < c1l
    cc_is_2u = h > c1h and l >= c1l and not cc_is_3
    cc_is_2d = l < c1l and h <= c1h and not cc_is_3
    is_f2u = is_f2d = False
    if s.enableFailed2Detection and (cc_is_2u or cc_is_2d):
        inside = c1l <= c <= c1h
        above = c > o
        m = s.failed_2_method
        if m == "Open":
            is_f2u, is_f2d = cc_is_2u and not above, cc_is_2d and above
        elif m == "Reclaim":
            is_f2u, is_f2d = cc_is_2u and inside, cc_is_2d and inside
        elif m == "Reclaim + Open":
            is_f2u, is_f2d = cc_is_2u and not above and inside, cc_is_2d and above and inside
        else:
            is_f2u, is_f2d = cc_is_2u and (not above or inside), cc_is_2d and (above or inside)
    return cc_is_2u, cc_is_2d, cc_is_3, is_f2u, is_f2d


def _should_draw_level(bull: bool, s: SignalSettings, c1_is_inside, c1_is_hammer, c1_is_shooter,
                       cc_is_2u, cc_is_2d, cc_is_3, is_f2u, is_f2d, ftfc_up, ftfc_down,
                       c1_was_2u, c1_was_2d, c1_is_3, c2_closed_up, c2_closed_down):
    result = is_cont = False
    cc_is_inside = not cc_is_2u and not cc_is_2d and not cc_is_3
    ftfc_blocks = ftfc_down if bull else ftfc_up
    c2_same = c2_closed_up if bull else c2_closed_down
    c2_opp = c2_closed_down if bull else c2_closed_up
    momo = c1_is_hammer if bull else c1_is_shooter
    cc_in_force = cc_is_2u if bull else cc_is_2d
    cc_opposite = cc_is_2d if bull else cc_is_2u
    c1_same = c1_was_2u if bull else c1_was_2d
    c1_opp = c1_was_2d if bull else c1_was_2u
    failed_same = is_f2u if bull else is_f2d
    failed_opp = is_f2d if bull else is_f2u
    inside_cont = c1_is_inside and c2_same
    inside_rev = c1_is_inside and (c2_opp or (not c2_closed_up and not c2_closed_down))
    if cc_is_3:
        if s.show3Expansions and not (s.threeExpRequireFTFC and ftfc_blocks):
            result = True
    elif s.show_p3_toggle and (failed_same or failed_opp):
        result = not (s.p3RequireFTFC and ftfc_blocks)
    elif c1_is_inside:
        if inside_rev and s.showInsideReversals:
            result = False if (s.insideRevOnlyMomo and not momo) else not (s.insideRevRequireFTFC and ftfc_blocks)
        elif inside_rev:
            result = False
        elif inside_cont and s.showInsideContinuations:
            if s.insideContOnlyMomo and not momo:
                result = False
            else:
                result = not (s.insideContRequireFTFC and ftfc_blocks)
                is_cont = result
        elif inside_cont:
            result = False
        elif s.showInsideReversals:
            result = False if (s.insideRevOnlyMomo and not momo) else not (s.insideRevRequireFTFC and ftfc_blocks)
    elif c1_is_3 and (cc_in_force or cc_is_inside) and s.showRangeExpansions:
        if s.rangeExpOnlyMomo and not momo:
            result = False
        else:
            result = not (s.rangeExpansionsRequireFTFC and ftfc_blocks)
            is_cont = result
    elif momo and not (c1_is_3 and not s.showRangeExpansions):
        momo_rev = c1_opp or (c2_opp and not c1_same) or (not c1_was_2u and not c1_was_2d and not c2_same)
        momo_cont = c1_same or (c2_same and not c1_opp)
        if cc_opposite and c1_opp:
            result = False
        elif momo_rev and not cc_opposite:
            result = not (s.reversalsRequireFTFC and ftfc_blocks)
        elif momo_cont and s.showContinuations:
            result = not (s.continuationsRequireFTFC and ftfc_blocks)
            is_cont = result
    elif c1_opp and s.showAllReversals and (cc_in_force or not cc_opposite):
        if momo:
            result = not (s.reversalsRequireFTFC and ftfc_blocks)
        elif s.reversalsRequireActionable:
            result = False
        else:
            result = not (s.reversalsRequireFTFC and ftfc_blocks)
    elif c1_same and (cc_in_force or cc_is_inside) and s.showContinuations:
        if s.twoTwoContOnlyMomo and not momo:
            result = False
        else:
            result = not (s.continuationsRequireFTFC and ftfc_blocks)
            is_cont = result
    return result, is_cont


@dataclass
class PeriodLatch:
    """Per-timeframe, per-period memory: the Pine's `data.*` fields reset on newPeriod."""

    period_start: object = None
    prevHigh: float | None = None
    prevLow: float | None = None
    prevMagHigh: float | None = None
    prevMagLow: float | None = None
    exhHigh: Bar | None = None
    exhLow: Bar | None = None
    magHighCrossed: bool = False
    magLowCrossed: bool = False
    exhHighCrossed: bool = False
    exhLowCrossed: bool = False

    def roll(self, period_start, c1: Bar, c2: Bar | None, exh_h: Bar | None, exh_l: Bar | None) -> None:
        self.period_start = period_start
        self.prevHigh, self.prevLow = c1.high, c1.low
        self.prevMagHigh = c2.high if c2 else None
        self.prevMagLow = c2.low if c2 else None
        self.exhHigh, self.exhLow = exh_h, exh_l
        self.magHighCrossed = self.magLowCrossed = False
        self.exhHighCrossed = self.exhLowCrossed = False


@dataclass(frozen=True)
class Side:
    """One side of a timeframe's signal picture: high = bullish, low = bearish."""

    trigger: float
    draw: bool                  # the Suite draws this trigger level (an enabled setup exists)
    in_force: bool              # rawInForce gated by exhDisablesInForce
    terms: tuple[str, ...]      # which in-force terms are true (names from DEBUG-TERMS-1)
    continuation: bool
    magnitude: float | None
    draw_magnitude: bool
    magnitude_hit: bool
    exhaustion: float | None
    exhaustion_time: str | None
    draw_exhaustion: bool
    exhaustion_hit: bool

    def as_dict(self) -> dict:
        return asdict(self)


@dataclass(frozen=True)
class SignalState:
    high: Side
    low: Side
    is_f2u: bool
    is_f2d: bool
    combo: str | None   # replay's label, e.g. "2d-1-2u"; not yet checked against Suite label text

    def as_dict(self) -> dict:
        return {"high": self.high.as_dict(), "low": self.low.as_dict(),
                "f2u": self.is_f2u, "f2d": self.is_f2d, "combo": self.combo}


def _token(prev: Bar, cur: Bar) -> str:
    bh, bl = cur.high > prev.high, cur.low < prev.low
    if bh and bl:
        return "3"
    if bh:
        return "2u"
    if bl:
        return "2d"
    return "1"


def evaluate(cc: Bar, c1: Bar, c2: Bar | None, c3: Bar | None, latch: PeriodLatch,
             ftfc_up: bool, ftfc_down: bool, s: SignalSettings) -> SignalState | None:
    """computeSignalState for one timeframe at one moment. Updates the latch's crossed flags."""
    if c2 is None or c3 is None:
        return None
    tfO, tfC, tfH_curr, tfL_curr = cc.open, cc.close, cc.high, cc.low
    tfH, tfL, tfO_C1, tfC_C1 = c1.high, c1.low, c1.open, c1.close
    tfMagH, tfMagL, tfO_C2, tfC_C2 = c2.high, c2.low, c2.open, c2.close
    tfH_C3, tfL_C3 = c3.high, c3.low
    L = latch

    if L.prevMagHigh is not None and not L.magHighCrossed and tfH_curr >= L.prevMagHigh:
        L.magHighCrossed = True
    if L.prevMagLow is not None and not L.magLowCrossed and tfL_curr <= L.prevMagLow:
        L.magLowCrossed = True
    if L.exhHigh is not None and not L.exhHighCrossed and tfH_curr >= L.exhHigh.high:
        L.exhHighCrossed = True
    if L.exhLow is not None and not L.exhLowCrossed and tfL_curr <= L.exhLow.low:
        L.exhLowCrossed = True

    c1c = Candle(tfO_C1, tfH, tfL, tfC_C1)
    isHammer = is_hammer(c1c, s.logicType, s.onlyColor)
    isShooter = is_shooter(c1c, s.logicType, s.onlyColor)
    isInside = tfH <= tfMagH and tfL >= tfMagL
    cc_is_2u, cc_is_2d, cc_is_3, is_f2u, is_f2d = _detect_failed2(L.prevHigh, L.prevLow, tfO, tfC, tfH_curr, tfL_curr, s)

    cc_broke_high = tfH_curr > L.prevHigh
    cc_broke_low = tfL_curr < L.prevLow
    c1_broke_mag_high = tfH > tfMagH
    c1_broke_mag_low = tfL < tfMagL
    c1_was_2u = c1_broke_mag_high and not c1_broke_mag_low
    c1_was_2d = c1_broke_mag_low and not c1_broke_mag_high
    c2_was_2u = tfMagH > tfH_C3 and not tfMagL < tfL_C3
    c2_was_2d = tfMagL < tfL_C3 and not tfMagH > tfH_C3
    c2_closed_up = tfC_C2 > tfO_C2
    c2_closed_down = tfC_C2 < tfO_C2
    c1_was_f2d = c1_was_2d and tfC_C1 > tfMagL
    c1_was_f2u = c1_was_2u and tfC_C1 < tfMagH
    c1_was_neutral = not c1_was_2u and not c1_was_2d
    inside_bullish = isInside and c2_closed_up
    inside_bearish = isInside and c2_closed_down
    is_hammer_reversal = c1_was_2d or inside_bearish or (c1_was_neutral and not isInside)
    is_shooter_reversal = c1_was_2u or inside_bullish or (c1_was_neutral and not isInside)
    is_hammer_momo = c1_was_2u or inside_bullish
    is_shooter_momo = c1_was_2d or inside_bearish
    hammer_shows = isHammer and (is_hammer_reversal or (is_hammer_momo and s.showContinuations))
    shooter_shows = isShooter and (is_shooter_reversal or (is_shooter_momo and s.showContinuations))
    ic_h_momo = not s.insideContOnlyMomo or isHammer
    ic_l_momo = not s.insideContOnlyMomo or isShooter
    tt_h_momo = not s.twoTwoContOnlyMomo or isHammer
    tt_l_momo = not s.twoTwoContOnlyMomo or isShooter
    c1_is_3 = tfH > tfMagH and tfL < tfMagL
    is_22_cont_high = c1_was_2u and c2_was_2u and s.showContinuations and tt_h_momo
    is_22_cont_low = c1_was_2d and c2_was_2d and s.showContinuations and tt_l_momo
    is_32_cont_high = c1_is_3 and cc_is_2u and s.showRangeExpansions
    is_32_cont_low = c1_is_3 and cc_is_2d and s.showRangeExpansions
    rev_h_passes = (not s.reversalsRequireActionable or isHammer) and (not s.reversalsRequireC1F2 or c1_was_f2d)
    rev_l_passes = (not s.reversalsRequireActionable or isShooter) and (not s.reversalsRequireC1F2 or c1_was_f2u)
    p3_h_ftfc = not s.p3RequireFTFC or ftfc_up
    p3_l_ftfc = not s.p3RequireFTFC or ftfc_down

    terms_h = {
        "insideRev": isInside and not c2_closed_up and cc_is_2u,
        "insideCont": inside_bullish and s.showInsideContinuations and ic_h_momo and cc_is_2u,
        "hamSho": hammer_shows and cc_broke_high,
        "rev22": s.showAllReversals and c1_was_2d and cc_is_2u and rev_h_passes,
        "cont22": s.showContinuations and c1_was_2u and cc_is_2u and tt_h_momo,
        "exp32": s.showRangeExpansions and c1_is_3 and cc_is_2u,
        "f2": s.show_p3_toggle and is_f2d and p3_h_ftfc,
    }
    terms_l = {
        "insideRev": isInside and not c2_closed_down and cc_is_2d,
        "insideCont": inside_bearish and s.showInsideContinuations and ic_l_momo and cc_is_2d,
        "hamSho": shooter_shows and cc_broke_low,
        "rev22": s.showAllReversals and c1_was_2u and cc_is_2d and rev_l_passes,
        "cont22": s.showContinuations and c1_was_2d and cc_is_2d and tt_l_momo,
        "exp32": s.showRangeExpansions and c1_is_3 and cc_is_2d,
        "f2": s.show_p3_toggle and is_f2u and p3_l_ftfc,
    }
    sif_h = any(terms_h.values())
    sif_l = any(terms_l.values())
    above_trig = tfC > L.prevHigh
    below_trig = tfC < L.prevLow
    three_2u_suppress_low = c1_is_3 and cc_is_2u and not is_f2u
    three_2d_suppress_high = c1_is_3 and cc_is_2d and not is_f2d
    cc_3u_suppress_low = cc_is_3 and tfC > tfO
    cc_3d_suppress_high = cc_is_3 and tfC <= tfO
    ib_suppress_low = isInside and cc_broke_high and not is_f2u and not cc_is_3
    ib_suppress_high = isInside and cc_broke_low and not is_f2d and not cc_is_3
    tt_f2_suppress_high = s.reversalsRequireC1F2 and c1_was_2d and not c1_was_f2d and not c1_is_3 and not isInside and not is_f2u and not is_f2d
    tt_f2_suppress_low = s.reversalsRequireC1F2 and c1_was_2u and not c1_was_f2u and not c1_is_3 and not isInside and not is_f2u and not is_f2d
    args = (isInside, isHammer, isShooter, cc_is_2u, cc_is_2d, cc_is_3, is_f2u, is_f2d, ftfc_up, ftfc_down,
            c1_was_2u, c1_was_2d, c1_is_3, c2_closed_up, c2_closed_down)
    dh, cont_h = _should_draw_level(True, s, *args)
    dl, cont_l = _should_draw_level(False, s, *args)
    drawHigh = dh and not three_2d_suppress_high and not ib_suppress_high and not tt_f2_suppress_high
    drawLow = dl and not three_2u_suppress_low and not ib_suppress_low and not tt_f2_suppress_low

    exh_h_price = L.exhHigh.high if L.exhHigh else None
    exh_l_price = L.exhLow.low if L.exhLow else None
    mag_fb_h = exh_h_price is None and L.prevMagHigh is not None and L.magHighCrossed
    mag_fb_l = exh_l_price is None and L.prevMagLow is not None and L.magLowCrossed
    exh_dis_h = s.exhDisablesInForce and (L.exhHighCrossed or mag_fb_h)
    exh_dis_l = s.exhDisablesInForce and (L.exhLowCrossed or mag_fb_l)
    raw_h = ((sif_h and not is_f2d and above_trig) or (s.show_p3_toggle and is_f2d and drawHigh and p3_h_ftfc)) and drawHigh
    raw_l = ((sif_l and not is_f2u and below_trig) or (s.show_p3_toggle and is_f2u and drawLow and p3_l_ftfc)) and drawLow

    up = tfC > tfO
    te_mag_h = cc_is_3 and s.show3Expansions and s.show3ExpMagnitude and (not s.threeExpRequireFTFC or ftfc_up) and up
    te_mag_l = cc_is_3 and s.show3Expansions and s.show3ExpMagnitude and (not s.threeExpRequireFTFC or ftfc_down) and not up
    te_exh_h = cc_is_3 and s.show3Expansions and s.showExhaustion and s.show3ExpExhaustion and (not s.threeExpRequireFTFC or ftfc_up) and up
    te_exh_l = cc_is_3 and s.show3Expansions and s.showExhaustion and s.show3ExpExhaustion and (not s.threeExpRequireFTFC or ftfc_down) and not up
    in3u = isInside and cc_is_3 and up and s.show3Expansions and (not s.threeExpRequireFTFC or ftfc_up)
    in3d = isInside and cc_is_3 and not up and s.show3Expansions and (not s.threeExpRequireFTFC or ftfc_down)
    p3_sup_h, p3_sup_l = is_f2u, is_f2d
    cont_sup_h = is_22_cont_high or is_32_cont_high
    cont_sup_l = is_22_cont_low or is_32_cont_low
    hsb = three_2d_suppress_high or ib_suppress_high or cc_3d_suppress_high
    lsb = three_2u_suppress_low or ib_suppress_low or cc_3u_suppress_low
    exh_h_valid = exh_h_price is not None and exh_h_price != L.prevHigh
    exh_l_valid = exh_l_price is not None and exh_l_price != L.prevLow
    cont_exh_h = s.showExhaustion and drawHigh and cont_sup_h
    cont_exh_l = s.showExhaustion and drawLow and cont_sup_l
    force_mag_h = s.showMagnitude and (drawHigh or in3u or te_mag_h) and not p3_sup_h and not cont_sup_h
    force_mag_l = s.showMagnitude and (drawLow or in3d or te_mag_l) and not p3_sup_l and not cont_sup_l
    hit_mag_h = L.prevMagHigh is not None and tfH_curr >= L.prevMagHigh
    hit_mag_l = L.prevMagLow is not None and tfL_curr <= L.prevMagLow
    emf_h = not s.exhRequiresMagHit or hit_mag_h
    emf_l = not s.exhRequiresMagHit or hit_mag_l
    ebc_h = (s.showExhaustion and emf_h and exh_h_valid and exh_h_price != L.prevLow
             and exh_h_price != L.prevMagLow and not hsb and not p3_sup_h)
    ebc_l = (s.showExhaustion and emf_l and exh_l_valid and exh_l_price != L.prevHigh
             and exh_l_price != L.prevMagHigh and not lsb and not p3_sup_l)
    draw_exh_h = (((ebc_h and drawHigh) or (ebc_h and L.exhHighCrossed) or (cont_exh_h and exh_h_valid)
                   or (te_exh_h and exh_h_valid)) and (not s.exhOnlyWhenInForce or raw_h))
    draw_exh_l = (((ebc_l and drawLow) or (ebc_l and L.exhLowCrossed) or (cont_exh_l and exh_l_valid)
                   or (te_exh_l and exh_l_valid)) and (not s.exhOnlyWhenInForce or raw_l))
    draw_mag_h = (((force_mag_h and not c1_broke_mag_high) or (drawHigh and not c1_broke_mag_high and sif_h))
                  and not hsb and not (draw_exh_h and exh_h_price == L.prevMagHigh) and not p3_sup_h
                  and not cont_sup_h and L.prevMagHigh != L.prevHigh and (not s.magOnlyWhenInForce or raw_h))
    draw_mag_l = (((force_mag_l and not c1_broke_mag_low) or (drawLow and not c1_broke_mag_low and sif_l))
                  and not lsb and not (draw_exh_l and exh_l_price == L.prevMagLow) and not p3_sup_l
                  and not cont_sup_l and L.prevMagLow != L.prevLow and (not s.magOnlyWhenInForce or raw_l))

    def side(bull: bool) -> Side:
        exh = L.exhHigh if bull else L.exhLow
        return Side(
            trigger=L.prevHigh if bull else L.prevLow,
            draw=drawHigh if bull else drawLow,
            in_force=(raw_h and not exh_dis_h) if bull else (raw_l and not exh_dis_l),
            terms=tuple(k for k, v in (terms_h if bull else terms_l).items() if v),
            continuation=cont_h if bull else cont_l,
            magnitude=L.prevMagHigh if bull else L.prevMagLow,
            draw_magnitude=draw_mag_h if bull else draw_mag_l,
            magnitude_hit=L.magHighCrossed if bull else L.magLowCrossed,
            exhaustion=(exh.high if bull else exh.low) if exh else None,
            exhaustion_time=exh.ts.isoformat(timespec="minutes") if exh else None,
            draw_exhaustion=draw_exh_h if bull else draw_exh_l,
            exhaustion_hit=L.exhHighCrossed if bull else L.exhLowCrossed,
        )

    t2, t1 = _token(c3, c2), _token(c2, c1)
    tc = "F2u" if is_f2u else "F2d" if is_f2d else ("3" if cc_is_3 else "2u" if cc_is_2u else "2d" if cc_is_2d else "1")
    combo = f"{t2}-{t1}-{tc}" if t1 == "1" else f"{t1}-{tc}"
    return SignalState(side(True), side(False), is_f2u, is_f2d, combo)
