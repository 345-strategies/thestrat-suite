// TheStrat Suite for NinjaTrader 8 - engine
//
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Pure C# port of the decision logic in pine/TheStratSuite_v3.1.1.pine and
// grammar/TheStratGrammar.pine. Nothing in this file touches NinjaTrader types, so it
// compiles and runs outside NinjaTrader: grammar/tests/test_nt_parity.py builds it with
// Mono and checks it against the Python grammar. Function names follow the Pine so a
// reviewer can read the two side by side; Pine fix tags (P0-1, CONT22-PRIOR-1, ...) are
// kept where the behavior they describe lives.
//
// Written against C# 5 so it compiles on every NinjaTrader 8 release.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace NinjaTrader.NinjaScript.Indicators.StratSuiteCore
{
    // ============================================================================
    // ENUMS (also used as indicator properties)
    // ============================================================================

    // [Description] strings are the Pine option names; the indicator's property grid shows them.
    public enum StratPreset
    {
        [Description("Custom")] Custom,
        [Description("TheStrat Classic")] TheStratClassic,
        [Description("Scalp")] Scalp,
        [Description("Day Trade")] DayTrade,
        [Description("Futures/Crypto")] FuturesCrypto,
        [Description("Swing Trade")] SwingTrade,
        [Description("Investing")] Investing
    }

    public enum StratTimeframe
    {
        [Description("1m")] Min1,
        [Description("3m")] Min3,
        [Description("5m")] Min5,
        [Description("15m")] Min15,
        [Description("30m")] Min30,
        [Description("1H")] Hour1,
        [Description("2H")] Hour2,
        [Description("4H")] Hour4,
        [Description("8H")] Hour8,
        [Description("12H")] Hour12,
        [Description("D")] Day,
        [Description("W")] Week,
        [Description("M")] Month,
        [Description("3M")] Quarter,
        [Description("6M")] HalfYear,
        [Description("12M")] Year
    }

    public enum StratLabelStyle { [Description("TheStrat")] TheStrat, [Description("Universal")] Universal }
    public enum HammerDefinition { [Description("Broad (Loose)")] Broad, [Description("Classic")] Classic, [Description("Pin Bar (Strict)")] PinBar }
    public enum F2Method { [Description("Reclaim")] Reclaim, [Description("Open")] Open, [Description("Reclaim + Open")] ReclaimAndOpen, [Description("Reclaim OR Open")] ReclaimOrOpen }
    public enum StopReferenceMode { [Description("CC")] CC, [Description("C1")] C1 }
    public enum StopColorMode { [Description("Opposite Signal")] OppositeSignal, [Description("Match Signal")] MatchSignal, [Description("Custom")] Custom }
    public enum PreviewModeOption { [Description("Off")] Off, [Description("On")] On, [Description("Auto")] Auto }
    public enum TableModeOption { [Description("Full")] Full, [Description("Compact")] Compact }
    public enum CompactCellColorMode { [Description("Bar State")] BarState, [Description("Signals In Force")] SignalsInForce }
    public enum TablePositionOption
    {
        [Description("Bottom Center")] BottomCenter, [Description("Bottom Left")] BottomLeft, [Description("Bottom Right")] BottomRight,
        [Description("Middle Center")] MiddleCenter, [Description("Middle Left")] MiddleLeft, [Description("Middle Right")] MiddleRight,
        [Description("Top Center")] TopCenter, [Description("Top Left")] TopLeft, [Description("Top Right")] TopRight
    }
    public enum TextSizeOption { [Description("Tiny")] Tiny, [Description("Small")] Small, [Description("Normal")] Normal, [Description("Large")] Large }
    public enum BarColorModeOption { [Description("Off")] Off, [Description("Strat Candles")] StratCandles, [Description("FTFC Candles")] FtfcCandles }
    public enum DebugSlot { TF1, TF2, TF3, TF4, TF5, TF6 }
    public enum LineStyleCode { Solid, Dashed, Dotted }

    public enum Structure { None, One, TwoUp, TwoDown, Three }

    // ============================================================================
    // COLOR (RGBA, independent of WPF / SharpDX)
    // ============================================================================

    public struct SColor
    {
        public byte R, G, B, A;
        public SColor(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }

        public static SColor Hex(int rgb) { return new SColor((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255), 255); }
        public static readonly SColor None = new SColor(0, 0, 0, 0);
        public bool IsNone { get { return A == 0; } }

        // Pine color.new(c, transp): transp 0 = solid, 100 = invisible.
        public SColor WithTransp(int transp)
        {
            int t = Math.Max(0, Math.Min(100, transp));
            return new SColor(R, G, B, (byte)Math.Round((100 - t) * 255.0 / 100.0));
        }

        public bool SameAs(SColor o) { return R == o.R && G == o.G && B == o.B && A == o.A; }
        public int Key { get { return (R << 24) | (G << 16) | (B << 8) | A; } }

        // TradingView's named colors, which the Pine uses through color.green etc.
        public static readonly SColor TvGreen = Hex(0x4CAF50);
        public static readonly SColor TvRed = Hex(0xF23645);
        public static readonly SColor TvGray = Hex(0x787B86);
        public static readonly SColor TvYellow = Hex(0xFFEB3B);
        public static readonly SColor TvOrange = Hex(0xFF9800);
        public static readonly SColor TvBlack = Hex(0x363A45);
        public static readonly SColor TvWhite = Hex(0xFFFFFF);
    }

    public static class ColorMath
    {
        static double SrgbLin(double v) { return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }

        public static double RelLum(SColor c)
        {
            return 0.2126 * SrgbLin(c.R / 255.0) + 0.7152 * SrgbLin(c.G / 255.0) + 0.0722 * SrgbLin(c.B / 255.0);
        }

        public static double ContrastOn(SColor fg, SColor bg)
        {
            double lf = RelLum(fg), lb = RelLum(bg);
            return (Math.Max(lf, lb) + 0.05) / (Math.Min(lf, lb) + 0.05);
        }

        // FIX LABELTEXT-1: lift a label's text color only when it would not read on the
        // configured label background (below 3.0:1); once lifting, climb to 3.5:1.
        public static SColor ReadableOn(SColor fg, SColor bg)
        {
            SColor output = fg;
            if (ContrastOn(fg, bg) < 3.0)
            {
                double tr = RelLum(bg) < 0.18 ? 255.0 : 0.0;
                double sr = fg.R, sg = fg.G, sb = fg.B;
                for (int i = 1; i <= 12; i++)
                {
                    double k = i / 12.0;
                    SColor cand = new SColor(Clamp(sr + (tr - sr) * k), Clamp(sg + (tr - sg) * k), Clamp(sb + (tr - sb) * k), 255);
                    output = cand;
                    if (ContrastOn(cand, bg) >= 3.5)
                        break;
                }
            }
            return output;
        }

        // Pine's color.rgb rounds float channels.
        static byte Clamp(double v) { return (byte)Math.Max(0, Math.Min(255, Math.Round(v))); }
    }

    // ============================================================================
    // SETTINGS (the Pine inputs, resolved)
    // ============================================================================

    public sealed class StratSettings
    {
        public StratLabelStyle LabelStyle = StratLabelStyle.TheStrat;
        public bool SimpleLabelMode { get { return LabelStyle == StratLabelStyle.Universal; } }

        // Signals
        public bool ShowInsideReversals = true, InsideRevOnlyMomo = false, InsideRevRequireFTFC = false;
        public bool ShowAllReversals = true, ReversalsRequireActionable = false, ReversalsRequireC1F2 = false, ReversalsRequireFTFC = false;
        public bool ShowInsideContinuations = true, InsideContOnlyMomo = false, InsideContRequireFTFC = false;
        public bool ShowContinuations = false, TwoTwoContOnlyMomo = false, ContinuationsRequireFTFC = false;
        public bool ShowP3 = false, P3RequireFTFC = false;
        public bool ShowRangeExpansions = false, RangeExpOnlyMomo = false, RangeExpansionsRequireFTFC = false;
        public bool Show3Expansions = false, ThreeExpRequireFTFC = false;

        // Filters
        public HammerDefinition LogicType = HammerDefinition.Broad;
        public bool OnlyColor = false;
        public int MinDominoTFs = 2;
        public bool ShowDominoInTable = true;
        public bool EnableDirectionalFilter = false;

        // Targets
        public bool ShowMagnitude = true, MagOnlyWhenInForce = false;
        public bool ShowExhaustion = true, ExhOnlyWhenInForce = false, ExhRequiresMagHit = false;
        public bool ShowAnyTargets { get { return ShowMagnitude || ShowExhaustion; } }

        // Take Action Windows
        public bool ShowTakeActionWindows = true, TawOnlyWhenInForce = true, TawExtendToExhaustion = true, TawExhOnlyWhenInForce = true;
        public int TawFillOpacity = 8, TawBorderOpacity = 30;

        // Stops
        public bool ShowStopLevels = false;
        public StopReferenceMode StopReference = StopReferenceMode.CC;
        public bool StopSmallestOnly = false, StopBEatMag = false, StopBEatExh = false;
        public StopColorMode StopColor = StopColorMode.OppositeSignal;
        public SColor ColorStop = SColor.Hex(0xFFFFFF);

        // Labels
        public bool UseDashSeparator = true;
        public bool ShowTimelineLabels = true, ShowFloatingLabels = false, ShowPriceInLabel = false, ShowHighLowInLabel = false;
        public int LabelOffset = 0, FloatingLabelOffset = 1;
        public SColor LabelBackgroundColor = SColor.Hex(0x2E2E2E);
        public int LabelBackgroundTransparency = 20;

        // Style
        public SColor ColorBullishActive = SColor.TvGreen, ColorBullishHit = SColor.Hex(0x9C9C9C), ColorBullishPotential = SColor.Hex(0xFFEB3B), ColorBullish3Bar = SColor.Hex(0x089981), ColorBullishP3 = SColor.Hex(0x81C784);
        public SColor ColorBearishActive = SColor.TvRed, ColorBearishHit = SColor.Hex(0x4A4A4A), ColorBearishPotential = SColor.Hex(0xFF9800), ColorBearish3Bar = SColor.Hex(0xE91E63), ColorBearishP3 = SColor.Hex(0xF77C80);

        // Alerts - Detailed
        public bool[] AlertTF = new bool[] { true, true, true, true, true, true };
        public bool AlertShowFTFC = false, AlertShowTrigger = false, AlertShowMagnitude = false, AlertShowExhaustion = false, AlertShowStop = false;

        // Advanced
        public bool ExhDisablesInForce = false, ExhDisablesFTFC = false;
        public bool Show3ExpMagnitude = true, Show3ExpExhaustion = true;
        public bool EnableFailed2Detection = true;
        public F2Method Failed2Method = F2Method.Reclaim;
        public bool ShowF2OpenLine = false;
        public bool ColorTFWhenInForce = true;
        public bool ShowP3TakeActionWindows = true, Show3ExpTakeActionWindows = true;
        public bool EnableDominoAlerts = false;

        // Bar coloring
        public BarColorModeOption BarColorMode = BarColorModeOption.Off;
        public bool PaintInsideBars = true, Paint2Bars = true, PaintOutsideBars = true, PaintFailedBars = true;
        public SColor ColorFtfcUpBar = SColor.Hex(0x4CAF50), ColorFtfcDownBar = SColor.Hex(0xF23645), ColorFtfcConflictBar = SColor.Hex(0x808080);
        public bool PaintFtfcConflict = true, PaintFtfcTransitions = true;

        // Derived colors (Pine's COLOR_* vars)
        public SColor ColorFailed2U { get { return ColorBearishP3; } }
        public SColor ColorFailed2D { get { return ColorBullishP3; } }
        public SColor Color3High { get { return ColorBullish3Bar; } }
        public SColor Color3Low { get { return ColorBearish3Bar; } }
        public SColor ColorInsideHigh { get { return ColorBullishPotential; } }
        public SColor ColorInsideLow { get { return ColorBearishPotential; } }
        public SColor ColorHitHigh { get { return ColorBullishHit; } }
        public SColor ColorHitLow { get { return ColorBearishHit; } }
        public SColor ColorStopHigh { get { return StopColor == StopColorMode.OppositeSignal ? ColorBearishActive : StopColor == StopColorMode.MatchSignal ? ColorBullishActive : ColorStop; } }
        public SColor ColorStopLow { get { return StopColor == StopColorMode.OppositeSignal ? ColorBullishActive : StopColor == StopColorMode.MatchSignal ? ColorBearishActive : ColorStop; } }
        public SColor TawBullishFill { get { return ColorBullishActive.WithTransp(100 - TawFillOpacity); } }
        public SColor TawBullishBorder { get { return ColorBullishActive.WithTransp(100 - TawBorderOpacity); } }
        public SColor TawBearishFill { get { return ColorBearishActive.WithTransp(100 - TawFillOpacity); } }
        public SColor TawBearishBorder { get { return ColorBearishActive.WithTransp(100 - TawBorderOpacity); } }
    }

    // ============================================================================
    // TIMEFRAMES AND PRESETS
    // ============================================================================

    public static class StratTimeframes
    {
        // Nominal seconds, as Pine's timeframe.in_seconds(): M is 30.436875 days ("1M" = 2629746 s).
        public static long Seconds(StratTimeframe tf)
        {
            switch (tf)
            {
                case StratTimeframe.Min1: return 60;
                case StratTimeframe.Min3: return 180;
                case StratTimeframe.Min5: return 300;
                case StratTimeframe.Min15: return 900;
                case StratTimeframe.Min30: return 1800;
                case StratTimeframe.Hour1: return 3600;
                case StratTimeframe.Hour2: return 7200;
                case StratTimeframe.Hour4: return 14400;
                case StratTimeframe.Hour8: return 28800;
                case StratTimeframe.Hour12: return 43200;
                case StratTimeframe.Day: return 86400;
                case StratTimeframe.Week: return 604800;
                case StratTimeframe.Month: return 2629746;
                case StratTimeframe.Quarter: return 3 * 2629746L;
                case StratTimeframe.HalfYear: return 6 * 2629746L;
                default: return 12 * 2629746L;
            }
        }

        public static int Minutes(StratTimeframe tf) { return IsIntraday(tf) ? (int)(Seconds(tf) / 60) : 0; }
        public static bool IsIntraday(StratTimeframe tf) { return tf < StratTimeframe.Day; }

        // Pine isCalendarTF: W, M, 3M, 6M, 12M.
        public static bool IsCalendar(StratTimeframe tf) { return tf >= StratTimeframe.Week; }

        // Pine getTimeframeLabel.
        public static string Label(StratTimeframe tf)
        {
            switch (tf)
            {
                case StratTimeframe.Min1: return "1m";
                case StratTimeframe.Min3: return "3m";
                case StratTimeframe.Min5: return "5m";
                case StratTimeframe.Min15: return "15m";
                case StratTimeframe.Min30: return "30m";
                case StratTimeframe.Hour1: return "1H";
                case StratTimeframe.Hour2: return "2H";
                case StratTimeframe.Hour4: return "4H";
                case StratTimeframe.Hour8: return "8H";
                case StratTimeframe.Hour12: return "12H";
                case StratTimeframe.Day: return "D";
                case StratTimeframe.Week: return "W";
                case StratTimeframe.Month: return "M";
                case StratTimeframe.Quarter: return "3M";
                case StratTimeframe.HalfYear: return "6M";
                default: return "12M";
            }
        }

        // Pine calc_bars_count tiers: Monthly+ 100, Weekly 100, Daily 150, 4H+ 200, intraday 250.
        public static int HtfBarsTier(StratTimeframe tf)
        {
            if (tf >= StratTimeframe.Week) return 100;
            if (tf == StratTimeframe.Day) return 150;
            if (tf >= StratTimeframe.Hour4) return 200;
            return 250;
        }

        // Calendar period key from a trading date. Weeks start Monday (TradingView), so a
        // Sunday-evening futures session, whose trading date is Monday, keys to its own week.
        public static long PeriodKey(StratTimeframe tf, DateTime tradingDate)
        {
            DateTime d = tradingDate.Date;
            switch (tf)
            {
                case StratTimeframe.Day: return d.Ticks;
                case StratTimeframe.Week:
                    int back = ((int)d.DayOfWeek + 6) % 7;
                    return d.AddDays(-back).Ticks;
                case StratTimeframe.Month: return d.Year * 100L + d.Month;
                case StratTimeframe.Quarter: return d.Year * 10L + (d.Month - 1) / 3 + 1;
                case StratTimeframe.HalfYear: return d.Year * 10L + (d.Month <= 6 ? 1 : 2);
                case StratTimeframe.Year: return d.Year;
                default: return d.Ticks;
            }
        }

        public struct SlotConfig
        {
            public bool Enabled;
            public StratTimeframe Tf;
            public int LineWidth;
            public bool ShowOpen;
        }

        // Pine rTF / rEN / rLW / rSO preset resolution.
        public static SlotConfig[] Resolve(StratPreset preset, SlotConfig[] custom)
        {
            if (preset == StratPreset.Custom)
                return (SlotConfig[])custom.Clone();
            StratTimeframe[] tfs;
            switch (preset)
            {
                case StratPreset.TheStratClassic: tfs = new[] { StratTimeframe.Min30, StratTimeframe.Hour1, StratTimeframe.Day, StratTimeframe.Week, StratTimeframe.Month, StratTimeframe.Month }; break;
                case StratPreset.Scalp: tfs = new[] { StratTimeframe.Min5, StratTimeframe.Min15, StratTimeframe.Min30, StratTimeframe.Hour1, StratTimeframe.Hour1, StratTimeframe.Hour1 }; break;
                case StratPreset.DayTrade: tfs = new[] { StratTimeframe.Min15, StratTimeframe.Min30, StratTimeframe.Hour1, StratTimeframe.Hour4, StratTimeframe.Day, StratTimeframe.Day }; break;
                case StratPreset.FuturesCrypto: tfs = new[] { StratTimeframe.Hour1, StratTimeframe.Hour4, StratTimeframe.Hour12, StratTimeframe.Day, StratTimeframe.Week, StratTimeframe.Week }; break;
                case StratPreset.SwingTrade: tfs = new[] { StratTimeframe.Hour4, StratTimeframe.Day, StratTimeframe.Week, StratTimeframe.Month, StratTimeframe.Month, StratTimeframe.Month }; break;
                default: tfs = new[] { StratTimeframe.Week, StratTimeframe.Month, StratTimeframe.Quarter, StratTimeframe.Year, StratTimeframe.Year, StratTimeframe.Year }; break;
            }
            bool en5 = preset == StratPreset.TheStratClassic || preset == StratPreset.FuturesCrypto || preset == StratPreset.DayTrade;
            bool classicOrDay = preset == StratPreset.TheStratClassic || preset == StratPreset.DayTrade;
            int lw3 = (preset == StratPreset.TheStratClassic || preset == StratPreset.FuturesCrypto || preset == StratPreset.DayTrade) ? 2 : 3;
            int lw4 = (preset == StratPreset.TheStratClassic || preset == StratPreset.FuturesCrypto || preset == StratPreset.DayTrade) ? 3 : 4;
            int[] lws = new[] { 1, classicOrDay ? 1 : 2, lw3, lw4, 4, 4 };
            bool[] ens = new[] { true, true, true, true, en5, false };
            SlotConfig[] r = new SlotConfig[6];
            for (int i = 0; i < 6; i++)
                r[i] = new SlotConfig { Enabled = ens[i], Tf = tfs[i], LineWidth = lws[i], ShowOpen = false };
            return r;
        }
    }

    // ============================================================================
    // GRAMMAR (TheStratGrammar.pine)
    // ============================================================================

    public static class StratGrammar
    {
        public static bool IsNa(double v) { return double.IsNaN(v); }

        // Pine compares with na as false; C# NaN != x is true. These keep Pine's reading.
        public static bool Eq(double a, double b) { return !double.IsNaN(a) && !double.IsNaN(b) && a == b; }
        public static bool Ne(double a, double b) { return !double.IsNaN(a) && !double.IsNaN(b) && a != b; }

        public static bool AboveOpen(double o, double c) { return c > o; }

        public static Structure StructureOf(double prevH, double prevL, double h, double l)
        {
            if (IsNa(prevH) || IsNa(prevL) || IsNa(h) || IsNa(l) || h < l || prevH < prevL)
                return Structure.None;
            bool brokeHigh = h > prevH;
            bool brokeLow = l < prevL;
            return brokeHigh && brokeLow ? Structure.Three : brokeHigh ? Structure.TwoUp : brokeLow ? Structure.TwoDown : Structure.One;
        }

        public static bool IsFailed(double prevH, double prevL, double o, double c, Structure s, F2Method method)
        {
            if (s != Structure.TwoUp && s != Structure.TwoDown)
                return false;
            bool reclaimed = c <= prevH && c >= prevL;
            bool againstOpen = s == Structure.TwoUp ? !AboveOpen(o, c) : AboveOpen(o, c);
            switch (method)
            {
                case F2Method.Open: return againstOpen;
                case F2Method.ReclaimAndOpen: return reclaimed && againstOpen;
                case F2Method.ReclaimOrOpen: return reclaimed || againstOpen;
                default: return reclaimed;
            }
        }

        public static bool IsHammer(double o, double h, double l, double c, HammerDefinition method, bool requireColor)
        {
            double r = h - l;
            if (r == 0 || IsNa(r))
                return false;
            if (requireColor && !AboveOpen(o, c))
                return false;
            double openFromLow = (o - l) / r;
            double closeFromLow = (c - l) / r;
            switch (method)
            {
                case HammerDefinition.Classic:
                    {
                        double body = Math.Abs(c - o);
                        double bodyPct = body / r;
                        double wickRatio = bodyPct > 0.001 ? (Math.Min(o, c) - l) / body : 0;
                        double upperWickPct = (h - Math.Max(o, c)) / r;
                        double bodyCenterFromHigh = (h - (o + c) / 2) / r;
                        double closeFromHigh = (h - c) / r;
                        return bodyPct <= 0.30 && wickRatio >= 3.0 && upperWickPct <= 0.35 && bodyCenterFromHigh <= 0.33 && closeFromHigh <= 0.25;
                    }
                case HammerDefinition.PinBar:
                    return (h - c) / r <= 0.25 && (h - o) / r <= 0.25;
                default:
                    return openFromLow > 0.50 && closeFromLow > 0.50;
            }
        }

        public static bool IsShooter(double o, double h, double l, double c, HammerDefinition method, bool requireColor)
        {
            double r = h - l;
            if (r == 0 || IsNa(r))
                return false;
            if (requireColor && AboveOpen(o, c))
                return false;
            double openFromLow = (o - l) / r;
            double closeFromLow = (c - l) / r;
            switch (method)
            {
                case HammerDefinition.Classic:
                    {
                        double body = Math.Abs(c - o);
                        double bodyPct = body / r;
                        double wickRatio = bodyPct > 0.001 ? (h - Math.Max(o, c)) / body : 0;
                        double lowerWickPct = (Math.Min(o, c) - l) / r;
                        double bodyCenterFromLow = ((o + c) / 2 - l) / r;
                        return bodyPct <= 0.30 && wickRatio >= 3.0 && lowerWickPct <= 0.35 && bodyCenterFromLow <= 0.33 && closeFromLow <= 0.25;
                    }
                case HammerDefinition.PinBar:
                    return (c - l) / r <= 0.25 && (o - l) / r <= 0.25;
                default:
                    return openFromLow < 0.50 && closeFromLow < 0.50;
            }
        }

        public static bool IsInsideBar(double currH, double currL, double prevH, double prevL)
        {
            return currH <= prevH && currL >= prevL;
        }

        // Pine calcBarType: combo token with bare "1" and signed 3s.
        public static string CalcBarType(double prevH, double prevL, double currH, double currL, double currO, double currC)
        {
            if (IsNa(currO) || IsNa(currC))
                return "";
            Structure st = StructureOf(prevH, prevL, currH, currL);
            if (st == Structure.Three)
                return currC > currO ? "3u" : "3d";
            return StructureToken(st);
        }

        // Pine calcBarNum: first character of the structure.
        public static string CalcBarNum(double prevH, double prevL, double currH, double currL)
        {
            Structure st = StructureOf(prevH, prevL, currH, currL);
            switch (st)
            {
                case Structure.One: return "1";
                case Structure.TwoUp:
                case Structure.TwoDown: return "2";
                case Structure.Three: return "3";
                default: return "";
            }
        }

        public static string StructureToken(Structure s)
        {
            switch (s)
            {
                case Structure.One: return "1";
                case Structure.TwoUp: return "2u";
                case Structure.TwoDown: return "2d";
                case Structure.Three: return "3";
                default: return "";
            }
        }

        // Pine detectBarTypeAndFailed: chart notation token (1u, F2d, 3u...) and the failed flag.
        public static string DetectBarTypeAndFailed(double prevH, double prevL, double currH, double currL, double currO, double currC, bool enableF2, F2Method method, out bool isFailed)
        {
            isFailed = false;
            if (IsNa(prevH) || IsNa(prevL) || IsNa(currH) || IsNa(currL) || IsNa(currO) || IsNa(currC) || currH < currL || prevH < prevL)
                return "";
            Structure s = StructureOf(prevH, prevL, currH, currL);
            bool aboveOpen = currC > currO;
            isFailed = enableF2 && IsFailed(prevH, prevL, currO, currC, s, method);
            if (s == Structure.One) return aboveOpen ? "1u" : "1d";
            if (s == Structure.Three) return aboveOpen ? "3u" : "3d";
            return (isFailed ? "F" : "") + StructureToken(s);
        }

        public struct Failed2
        {
            public bool Is2u, Is2d, Is3, IsF2u, IsF2d, PreF2u, PreF2d;
        }

        // Pine detectFailed2.
        public static Failed2 DetectFailed2(double c1High, double c1Low, double ccOpen, double ccClose, double ccHigh, double ccLow, bool enableF2, F2Method method)
        {
            Failed2 r = new Failed2();
            r.Is3 = ccHigh > c1High && ccLow < c1Low;
            r.Is2u = ccHigh > c1High && ccLow >= c1Low && !r.Is3;
            r.Is2d = ccLow < c1Low && ccHigh <= c1High && !r.Is3;
            if (enableF2 && (r.Is2u || r.Is2d) && !r.Is3)
            {
                bool insideC1 = ccClose <= c1High && ccClose >= c1Low;
                bool aboveOpen = ccClose > ccOpen;
                if (method == F2Method.Open)
                {
                    r.IsF2u = r.Is2u && !aboveOpen;
                    r.IsF2d = r.Is2d && aboveOpen;
                }
                else if (method == F2Method.Reclaim)
                {
                    r.IsF2u = r.Is2u && insideC1;
                    r.IsF2d = r.Is2d && insideC1;
                }
                else if (method == F2Method.ReclaimAndOpen)
                {
                    r.IsF2u = r.Is2u && !aboveOpen && insideC1;
                    r.IsF2d = r.Is2d && aboveOpen && insideC1;
                }
                else
                {
                    r.IsF2u = r.Is2u && (!aboveOpen || insideC1);
                    r.IsF2d = r.Is2d && (aboveOpen || insideC1);
                }
                if ((method == F2Method.Open || method == F2Method.ReclaimAndOpen) && !r.IsF2u && !r.IsF2d)
                {
                    if (r.Is2u && insideC1 && aboveOpen) r.PreF2u = true;
                    if (r.Is2d && insideC1 && !aboveOpen) r.PreF2d = true;
                }
            }
            return r;
        }

        // Pine calculateFTFC, including the zero-slot quirk (returns up and down both true).
        public static void CalculateFTFC(bool[] enabled, double[] opens, double[] closes, out bool up, out bool down)
        {
            up = true;
            down = true;
            for (int i = 0; i < enabled.Length; i++)
            {
                if (enabled[i] && !IsNa(closes[i]) && !IsNa(opens[i]))
                {
                    if (closes[i] > opens[i]) down = false;
                    else up = false;
                }
            }
        }
    }

    // ============================================================================
    // PER-SLOT DATA (Pine TFRawData, TimeframeData, ProcessingResult)
    // ============================================================================

    // One slot's candles as the chart bar sees them. *Bar fields are chart bar indices where
    // that candle started (the Pine's x1 times); -1 means before the first loaded chart bar.
    public sealed class SlotRaw
    {
        public bool HasData;
        public int C1Bar = -1, CCBar = -1, C2Bar = -1;
        public double C1H = double.NaN, C1L = double.NaN, C1O = double.NaN, C1C = double.NaN;
        public double CCO = double.NaN, CCH = double.NaN, CCL = double.NaN, CCC = double.NaN;
        public double C2H = double.NaN, C2L = double.NaN, C2O = double.NaN, C2C = double.NaN;
        public double C3H = double.NaN, C3L = double.NaN, C4H = double.NaN, C4L = double.NaN;
        public double ExhHP = double.NaN, ExhLP = double.NaN;
        public int ExhHBar = -1, ExhLBar = -1;
        public bool IsPreview;
        public long RealPeriodKey;
        public long CCStartId;      // Domino "same candle" identity (Pine ccStartTime)

        public SlotRaw Clone() { return (SlotRaw)MemberwiseClone(); }
    }

    // Pine TimeframeData, minus the drawing handles (the NinjaTrader side renders from results).
    public sealed class SlotState
    {
        public double PrevHigh = double.NaN, PrevLow = double.NaN, CurrOpen = double.NaN;
        public double PrevMagHigh = double.NaN, PrevMagLow = double.NaN;
        public double ExhHighPrice = double.NaN, ExhLowPrice = double.NaN;
        public int PrevBar = -1, PrevMagBar = -1, ExhHighBar = -1, ExhLowBar = -1;
        public bool MagHighCrossed, MagLowCrossed, ExhHighCrossed, ExhLowCrossed;
        public bool C1IsInside, C1IsHammer, C1IsShooter, C1IsHighContinuation, C1IsLowContinuation;
        public bool StopHighTriggered, StopLowTriggered;
        public double StopHighPrice = double.NaN, StopLowPrice = double.NaN;
        public bool HasLastPeriod;
        public long LastPeriodStart;
        public bool WasPreview;

        // As-of forming candle (the chart-bar rebuild of the HTF CC; see the indicator).
        public bool HasCur;
        public long CurKey;
        public double RunO = double.NaN, RunH = double.NaN, RunL = double.NaN;
        public int PeriodStartBar = -1;

        public SlotState Clone() { return (SlotState)MemberwiseClone(); }
    }

    public sealed class SlotResult
    {
        public double High = double.NaN, Low = double.NaN, Open = double.NaN, MagHigh = double.NaN, MagLow = double.NaN, ExhHigh = double.NaN, ExhLow = double.NaN;
        public SColor HighColor, LowColor, OpenColor, MagHighColor, MagLowColor, ExhHighColor, ExhLowColor;
        public bool DrawHigh, DrawLow, DrawOpen, DrawMagHigh, DrawMagLow, DrawExhHigh, DrawExhLow;
        public string TfLabel = "";
        public bool IsF2u, IsF2d, C1Is3;
        public string C3Num = "", C2Num = "", C1Num = "", C1Type = "", CCType = "";
        public bool SignalInForceHigh, SignalInForceLow;
        public double StopHigh = double.NaN, StopLow = double.NaN;
        public bool DrawStopHigh, DrawStopLow;
        public bool PreF2u, PreF2d;
        public double F2OpenPrice = double.NaN;
        public long CCStartId;
        public bool C1WasF2, C2WasF2;
        public LineStyleCode HighStyle = LineStyleCode.Dashed, LowStyle = LineStyleCode.Dashed;

        public SlotResult Clone() { return (SlotResult)MemberwiseClone(); }

        public void SuppressHighFlags() { DrawHigh = false; DrawMagHigh = false; DrawExhHigh = false; DrawStopHigh = false; }
        public void SuppressLowFlags() { DrawLow = false; DrawMagLow = false; DrawExhLow = false; DrawStopLow = false; }
        public void SuppressAllFlags() { SuppressHighFlags(); SuppressLowFlags(); }
    }

    // FEATURE DEBUG-TERMS-1: every boolean the debug panel shows, captured before the Lead filter.
    public sealed class DebugInfo
    {
        public string TfLabel = "";
        public bool c1_was_2u, c1_was_2d, c1_is_3, c2_was_2u, c2_was_2d, c2_was_3, c2_closed_up, c2_closed_down;
        public bool isHammer, isShooter, isInside, cc_is_2u, cc_is_2d, cc_is_3, cc_broke_high, cc_broke_low;
        public bool is_f2u, is_f2d, pre_f2u, pre_f2d, ftfc_up, ftfc_down;
        public bool is_hammer_reversal, is_hammer_momo, hammer_shows, is_shooter_reversal, is_shooter_momo, shooter_shows;
        public bool sif_high, sif_low;
        public bool t_insideRev_h, t_insideRev_l, t_insideCont_h, t_insideCont_l, t_hamSho_h, t_hamSho_l;
        public bool t_rev22_h, t_rev22_l, t_cont22_h, t_cont22_l, t_exp32_h, t_exp32_l, t_f2_h, t_f2_l;
        public bool drawHigh, drawLow, drawMagHigh, drawMagLow, drawExhHigh, drawExhLow;
        public bool force_mag_high, force_mag_low, mag_high_passes_ftfc, mag_low_passes_ftfc;
    }

    // ============================================================================
    // DECISION LOGIC (Pine SECTION 5 and computeSignalState)
    // ============================================================================

    public static class StratEngine
    {
        // Pine shouldDrawC1Level. Returns the draw decision; isContinuation is its second value.
        public static bool ShouldDrawC1Level(StratSettings s, bool isBullish, bool c1IsInside, bool c1IsHammer, bool c1IsShooter,
            bool ccIs2u, bool ccIs2d, bool ccIs3, bool isF2u, bool isF2d, bool ftfcUp, bool ftfcDown,
            bool c1Was2u, bool c1Was2d, bool c1Is3, bool c2ClosedUp, bool c2ClosedDown, out bool isContinuation)
        {
            bool result = false;
            isContinuation = false;
            bool ccIsInside = !ccIs2u && !ccIs2d && !ccIs3;
            bool ftfcBlocks = isBullish ? ftfcDown : ftfcUp;
            bool c2SameDir = isBullish ? c2ClosedUp : c2ClosedDown;
            bool c2OppDir = isBullish ? c2ClosedDown : c2ClosedUp;
            bool momoPattern = isBullish ? c1IsHammer : c1IsShooter;
            bool ccInForce = isBullish ? ccIs2u : ccIs2d;
            bool ccOpposite = isBullish ? ccIs2d : ccIs2u;
            bool c1WasSame = isBullish ? c1Was2u : c1Was2d;
            bool c1WasOpp = isBullish ? c1Was2d : c1Was2u;
            bool failedSame = isBullish ? isF2u : isF2d;
            bool failedOpp = isBullish ? isF2d : isF2u;
            bool insideRevFtfcBlocks = s.InsideRevRequireFTFC && ftfcBlocks;
            bool reversalsFtfcBlocks = s.ReversalsRequireFTFC && ftfcBlocks;
            bool continuationsFtfcBlocks = s.ContinuationsRequireFTFC && ftfcBlocks;
            bool insideContFtfcBlocks = s.InsideContRequireFTFC && ftfcBlocks;
            bool expansionsFtfcBlocks = s.RangeExpansionsRequireFTFC && ftfcBlocks;
            bool threeExpFtfcBlocks = s.ThreeExpRequireFTFC && ftfcBlocks;
            bool p3FtfcBlocks = s.P3RequireFTFC && ftfcBlocks;
            bool insideIsContinuation = c1IsInside && c2SameDir;
            bool insideIsReversal = c1IsInside && (c2OppDir || (!c2ClosedUp && !c2ClosedDown));

            if (ccIs3)
            {
                if (s.Show3Expansions && !threeExpFtfcBlocks)
                    result = true;
            }
            else if (s.ShowP3 && failedSame)
                result = !p3FtfcBlocks;
            else if (s.ShowP3 && failedOpp)
                result = !p3FtfcBlocks;
            else if (c1IsInside)
            {
                if (insideIsReversal && s.ShowInsideReversals)
                    result = (s.InsideRevOnlyMomo && !momoPattern) ? false : !insideRevFtfcBlocks;
                else if (insideIsReversal && !s.ShowInsideReversals)
                    result = false;
                else if (insideIsContinuation && s.ShowInsideContinuations)
                {
                    if (s.InsideContOnlyMomo && !momoPattern)
                        result = false;
                    else
                    {
                        result = !insideContFtfcBlocks;
                        isContinuation = result;
                    }
                }
                else if (insideIsContinuation && !s.ShowInsideContinuations)
                    result = false;
                else if (s.ShowInsideReversals)
                    result = (s.InsideRevOnlyMomo && !momoPattern) ? false : !insideRevFtfcBlocks;
            }
            else if (c1Is3 && ccInForce && s.ShowRangeExpansions)
            {
                if (s.RangeExpOnlyMomo && !momoPattern)
                    result = false;
                else
                {
                    result = !expansionsFtfcBlocks;
                    isContinuation = result;
                }
            }
            else if (c1Is3 && ccIsInside && s.ShowRangeExpansions)
            {
                if (s.RangeExpOnlyMomo && !momoPattern)
                    result = false;
                else
                {
                    result = !expansionsFtfcBlocks;
                    isContinuation = result;
                }
            }
            else if (momoPattern && !(c1Is3 && !s.ShowRangeExpansions))
            {
                bool isMomoReversal = c1WasOpp || (c2OppDir && !c1WasSame) || (!c1Was2u && !c1Was2d && !c2SameDir);
                bool isMomoCont = c1WasSame || (c2SameDir && !c1WasOpp);
                bool ccIsOppContinuation = ccOpposite && c1WasOpp;
                if (ccIsOppContinuation)
                    result = false;
                else if (isMomoReversal && !ccOpposite)
                    result = !reversalsFtfcBlocks;
                else if (isMomoCont && s.ShowContinuations)
                {
                    result = !continuationsFtfcBlocks;
                    isContinuation = result;
                }
            }
            else if (c1WasOpp && ccInForce && s.ShowAllReversals)
            {
                if (momoPattern) result = !reversalsFtfcBlocks;
                else if (s.ReversalsRequireActionable) result = false;
                else result = !reversalsFtfcBlocks;
            }
            else if (c1WasOpp && s.ShowAllReversals && !ccOpposite)
            {
                if (momoPattern) result = !reversalsFtfcBlocks;
                else if (s.ReversalsRequireActionable) result = false;
                else result = !reversalsFtfcBlocks;
            }
            // FIX CONT22-PRIOR-1: no C2 guard on either 2-2 continuation branch.
            else if (c1WasSame && ccInForce && s.ShowContinuations)
            {
                if (s.TwoTwoContOnlyMomo && !momoPattern)
                    result = false;
                else
                {
                    result = !continuationsFtfcBlocks;
                    isContinuation = result;
                }
            }
            else if (c1WasSame && ccIsInside && s.ShowContinuations)
            {
                if (s.TwoTwoContOnlyMomo && !momoPattern)
                    result = false;
                else
                {
                    result = !continuationsFtfcBlocks;
                    isContinuation = result;
                }
            }
            return result;
        }

        // Pine applyPreviewShift, preview branch. The caller decides doPreview; nxt* is the
        // next exhaustion channel, used only when the served CC has closed (EXH-PREVIEW-CHANNEL-1).
        public static SlotRaw ApplyPreviewShift(SlotRaw raw, int chartBar, bool ccClosed, double nxtHP, int nxtHBar, double nxtLP, int nxtLBar)
        {
            SlotRaw p = raw.Clone();
            p.C1Bar = raw.CCBar; p.C1H = raw.CCH; p.C1L = raw.CCL; p.C1O = raw.CCO; p.C1C = raw.CCC;
            p.CCBar = chartBar; p.CCO = raw.CCC; p.CCH = raw.CCH; p.CCL = raw.CCL; p.CCC = (raw.CCH + raw.CCL) / 2;
            p.C2Bar = raw.C1Bar; p.C2H = raw.C1H; p.C2L = raw.C1L; p.C2O = raw.C1O; p.C2C = raw.C1C;
            p.C3H = raw.C2H; p.C3L = raw.C2L;
            p.C4H = raw.C3H; p.C4L = raw.C3L;
            if (ccClosed)
            {
                p.ExhHP = nxtHP; p.ExhHBar = nxtHBar;
                p.ExhLP = nxtLP; p.ExhLBar = nxtLBar;
            }
            p.IsPreview = true;
            p.CCStartId = -1000000L - chartBar;
            return p;
        }

        // Pine computeSignalState. `data` is mutated (latches). `dbg` may be null.
        public static SlotResult ComputeSignalState(StratSettings s, SlotState data, SlotRaw raw, bool showOpen, bool validTimeframe,
            bool syntheticCC, bool ftfcUp, bool ftfcDown, string tfLabel, DebugInfo dbg, bool isLastBar)
        {
            SlotResult ret = new SlotResult();
            ret.TfLabel = tfLabel;
            ret.CCStartId = raw.CCStartId;

            double tfH = raw.C1H, tfL = raw.C1L, tfO_C1 = raw.C1O, tfC_C1 = raw.C1C;
            double tfO = raw.CCO, tfH_curr = raw.CCH, tfL_curr = raw.CCL, tfC = raw.CCC;
            double tfMagH = raw.C2H, tfMagL = raw.C2L, tfO_C2 = raw.C2O, tfC_C2 = raw.C2C;
            double tfH_C3 = raw.C3H, tfL_C3 = raw.C3L, tfH_C4 = raw.C4H, tfL_C4 = raw.C4L;
            bool isInPreview = raw.IsPreview;

            // FIX P0-1 / P1-d: per-slot new-period latch; re-latch every last-bar pass in
            // preview and once when preview exits mid-period.
            bool prevWasPreview = data.WasPreview;
            bool newPeriod = !raw.HasData || !data.HasLastPeriod || raw.RealPeriodKey != data.LastPeriodStart;
            if (newPeriod || (isInPreview && isLastBar) || (prevWasPreview && !isInPreview))
            {
                data.MagHighCrossed = false;
                data.MagLowCrossed = false;
                data.ExhHighCrossed = false;
                data.ExhLowCrossed = false;
                data.StopHighTriggered = false;
                data.StopLowTriggered = false;
                data.StopHighPrice = double.NaN;
                data.StopLowPrice = double.NaN;
                data.PrevHigh = tfH;
                data.PrevLow = tfL;
                data.PrevBar = raw.C1Bar;
                data.PrevMagHigh = tfMagH;
                data.PrevMagLow = tfMagL;
                data.PrevMagBar = raw.C2Bar;
                data.ExhHighPrice = raw.ExhHP;
                data.ExhHighBar = raw.ExhHBar;
                data.ExhLowPrice = raw.ExhLP;
                data.ExhLowBar = raw.ExhLBar;
                data.LastPeriodStart = raw.RealPeriodKey;
                data.HasLastPeriod = raw.HasData;
            }
            data.WasPreview = isInPreview;
            data.CurrOpen = tfO;

            if (!(validTimeframe && raw.HasData && !StratGrammar.IsNa(data.PrevHigh)))
                return ret;

            // FIX PREVIEW-CROSSED-1: a synthetic preview CC cannot cross anything.
            if (!syntheticCC)
            {
                if (!StratGrammar.IsNa(data.PrevMagHigh) && !data.MagHighCrossed && tfH_curr >= data.PrevMagHigh) data.MagHighCrossed = true;
                if (!StratGrammar.IsNa(data.PrevMagLow) && !data.MagLowCrossed && tfL_curr <= data.PrevMagLow) data.MagLowCrossed = true;
                if (!StratGrammar.IsNa(data.ExhHighPrice) && !data.ExhHighCrossed && tfH_curr >= data.ExhHighPrice) data.ExhHighCrossed = true;
                if (!StratGrammar.IsNa(data.ExhLowPrice) && !data.ExhLowCrossed && tfL_curr <= data.ExhLowPrice) data.ExhLowCrossed = true;
            }

            bool isHammer = StratGrammar.IsHammer(tfO_C1, tfH, tfL, tfC_C1, s.LogicType, s.OnlyColor);
            bool isShooter = StratGrammar.IsShooter(tfO_C1, tfH, tfL, tfC_C1, s.LogicType, s.OnlyColor);
            bool isInside = StratGrammar.IsInsideBar(tfH, tfL, tfMagH, tfMagL);
            data.C1IsInside = isInside;
            data.C1IsHammer = isHammer;
            data.C1IsShooter = isShooter;
            StratGrammar.Failed2 f = StratGrammar.DetectFailed2(data.PrevHigh, data.PrevLow, tfO, tfC, tfH_curr, tfL_curr, s.EnableFailed2Detection, s.Failed2Method);
            bool cc_is_2u = f.Is2u, cc_is_2d = f.Is2d, cc_is_3 = f.Is3, is_f2u = f.IsF2u, is_f2d = f.IsF2d, pre_f2u = f.PreF2u, pre_f2d = f.PreF2d;
            ret.IsF2u = is_f2u;
            ret.IsF2d = is_f2d;
            ret.C3Num = StratGrammar.CalcBarNum(tfH_C4, tfL_C4, tfH_C3, tfL_C3);
            ret.C2Num = StratGrammar.CalcBarNum(tfH_C3, tfL_C3, tfMagH, tfMagL);
            ret.C1Num = StratGrammar.CalcBarNum(tfMagH, tfMagL, tfH, tfL);
            string c1Type = StratGrammar.CalcBarType(tfMagH, tfMagL, tfH, tfL, tfO_C1, tfC_C1);
            if (is_f2u) ret.CCType = "F2u";
            else if (is_f2d) ret.CCType = "F2d";
            else if (cc_is_3) ret.CCType = tfC > tfO ? "3u" : "3d";
            else if (cc_is_2u) ret.CCType = "2u";
            else if (cc_is_2d) ret.CCType = "2d";
            else ret.CCType = "1";
            ret.C1Type = c1Type;
            bool c1f2flag, c2f2flag;
            StratGrammar.DetectBarTypeAndFailed(tfMagH, tfMagL, tfH, tfL, tfO_C1, tfC_C1, s.EnableFailed2Detection, s.Failed2Method, out c1f2flag);
            StratGrammar.DetectBarTypeAndFailed(tfH_C3, tfL_C3, tfMagH, tfMagL, tfO_C2, tfC_C2, s.EnableFailed2Detection, s.Failed2Method, out c2f2flag);
            ret.C1WasF2 = c1f2flag;
            ret.C2WasF2 = c2f2flag;
            bool cc_broke_high = tfH_curr > data.PrevHigh;
            bool cc_broke_low = tfL_curr < data.PrevLow;
            bool c1_broke_mag_high = tfH > tfMagH;
            bool c1_broke_mag_low = tfL < tfMagL;
            bool c1_was_2u = c1_broke_mag_high && !c1_broke_mag_low;
            bool c1_was_2d = c1_broke_mag_low && !c1_broke_mag_high;
            bool c2_broke_c3_high = tfMagH > tfH_C3;
            bool c2_broke_c3_low = tfMagL < tfL_C3;
            bool c2_was_2u = c2_broke_c3_high && !c2_broke_c3_low;
            bool c2_was_2d = c2_broke_c3_low && !c2_broke_c3_high;
            bool c2_was_3 = c2_broke_c3_high && c2_broke_c3_low;
            bool c2_closed_up = tfC_C2 > tfO_C2;
            bool c2_closed_down = tfC_C2 < tfO_C2;

            // Pine quirk kept (decision 1 in the thinkorswim plan): a hard-coded Reclaim test.
            bool c1_was_f2d = c1_was_2d && tfC_C1 > tfMagL;
            bool c1_was_f2u = c1_was_2u && tfC_C1 < tfMagH;

            bool c1_was_neutral = !c1_was_2u && !c1_was_2d;
            bool inside_bullish = isInside && c2_closed_up;
            bool inside_bearish = isInside && c2_closed_down;
            bool is_hammer_reversal = c1_was_2d || inside_bearish || (c1_was_neutral && !isInside);
            bool is_shooter_reversal = c1_was_2u || inside_bullish || (c1_was_neutral && !isInside);
            bool is_hammer_momo = c1_was_2u || inside_bullish;
            bool is_shooter_momo = c1_was_2d || inside_bearish;
            bool hammer_shows = isHammer && (is_hammer_reversal || (is_hammer_momo && s.ShowContinuations));
            bool shooter_shows = isShooter && (is_shooter_reversal || (is_shooter_momo && s.ShowContinuations));
            bool inside_cont_high_passes_momo = !s.InsideContOnlyMomo || isHammer;
            bool inside_cont_low_passes_momo = !s.InsideContOnlyMomo || isShooter;
            bool twotwo_cont_high_passes_momo = !s.TwoTwoContOnlyMomo || isHammer;
            bool twotwo_cont_low_passes_momo = !s.TwoTwoContOnlyMomo || isShooter;
            bool c1_is_3 = tfH > tfMagH && tfL < tfMagL;
            ret.C1Is3 = c1_is_3;
            bool is_22_cont_high = c1_was_2u && c2_was_2u && s.ShowContinuations && twotwo_cont_high_passes_momo;
            bool is_22_cont_low = c1_was_2d && c2_was_2d && s.ShowContinuations && twotwo_cont_low_passes_momo;
            bool is_32_cont_high = c1_is_3 && cc_is_2u && s.ShowRangeExpansions;
            bool is_32_cont_low = c1_is_3 && cc_is_2d && s.ShowRangeExpansions;
            bool twotwo_rev_high_passes = (!s.ReversalsRequireActionable || isHammer) && (!s.ReversalsRequireC1F2 || c1_was_f2d);
            bool twotwo_rev_low_passes = (!s.ReversalsRequireActionable || isShooter) && (!s.ReversalsRequireC1F2 || c1_was_f2u);

            bool p3_high_passes_ftfc = !s.P3RequireFTFC || ftfcUp;
            bool p3_low_passes_ftfc = !s.P3RequireFTFC || ftfcDown;
            // FIX CSS-1 + DEBUG-TERMS-1: the seven named in-force terms.
            bool t_insideRev_h = isInside && !c2_closed_up && cc_is_2u;
            bool t_insideCont_h = inside_bullish && s.ShowInsideContinuations && inside_cont_high_passes_momo && cc_is_2u;
            bool t_hamSho_h = hammer_shows && cc_broke_high;
            bool t_rev22_h = s.ShowAllReversals && c1_was_2d && cc_is_2u && twotwo_rev_high_passes;
            bool t_cont22_h = s.ShowContinuations && c1_was_2u && cc_is_2u && twotwo_cont_high_passes_momo;
            bool t_exp32_h = s.ShowRangeExpansions && c1_is_3 && cc_is_2u;
            bool t_f2_h = s.ShowP3 && is_f2d && p3_high_passes_ftfc;
            bool t_insideRev_l = isInside && !c2_closed_down && cc_is_2d;
            bool t_insideCont_l = inside_bearish && s.ShowInsideContinuations && inside_cont_low_passes_momo && cc_is_2d;
            bool t_hamSho_l = shooter_shows && cc_broke_low;
            bool t_rev22_l = s.ShowAllReversals && c1_was_2u && cc_is_2d && twotwo_rev_low_passes;
            bool t_cont22_l = s.ShowContinuations && c1_was_2d && cc_is_2d && twotwo_cont_low_passes_momo;
            bool t_exp32_l = s.ShowRangeExpansions && c1_is_3 && cc_is_2d;
            bool t_f2_l = s.ShowP3 && is_f2u && p3_low_passes_ftfc;
            bool signal_in_force_high = t_insideRev_h || t_insideCont_h || t_hamSho_h || t_rev22_h || t_cont22_h || t_exp32_h || t_f2_h;
            bool signal_in_force_low = t_insideRev_l || t_insideCont_l || t_hamSho_l || t_rev22_l || t_cont22_l || t_exp32_l || t_f2_l;
            bool currently_above_trigger = tfC > data.PrevHigh;
            bool currently_below_trigger = tfC < data.PrevLow;
            bool three_2u_suppress_low = c1_is_3 && cc_is_2u && !is_f2u;
            bool three_2d_suppress_high = c1_is_3 && cc_is_2d && !is_f2d;
            bool cc_3u_suppress_low = cc_is_3 && tfC > tfO;
            bool cc_3d_suppress_high = cc_is_3 && tfC <= tfO;
            bool inside_breakout_suppress_low = isInside && cc_broke_high && !is_f2u && !cc_is_3;
            bool inside_breakout_suppress_high = isInside && cc_broke_low && !is_f2d && !cc_is_3;
            bool twotwo_rev_f2_suppress_high = s.ReversalsRequireC1F2 && c1_was_2d && !c1_was_f2d && !c1_is_3 && !isInside && !is_f2u && !is_f2d;
            bool twotwo_rev_f2_suppress_low = s.ReversalsRequireC1F2 && c1_was_2u && !c1_was_f2u && !c1_is_3 && !isInside && !is_f2u && !is_f2d;
            bool isHighContinuation, isLowContinuation;
            bool drawHighResult = ShouldDrawC1Level(s, true, isInside, isHammer, isShooter, cc_is_2u, cc_is_2d, cc_is_3, is_f2u, is_f2d, ftfcUp, ftfcDown, c1_was_2u, c1_was_2d, c1_is_3, c2_closed_up, c2_closed_down, out isHighContinuation);
            bool drawLowResult = ShouldDrawC1Level(s, false, isInside, isHammer, isShooter, cc_is_2u, cc_is_2d, cc_is_3, is_f2u, is_f2d, ftfcUp, ftfcDown, c1_was_2u, c1_was_2d, c1_is_3, c2_closed_up, c2_closed_down, out isLowContinuation);
            bool drawHigh = drawHighResult && !three_2d_suppress_high && !inside_breakout_suppress_high && !twotwo_rev_f2_suppress_high;
            bool drawLow = drawLowResult && !three_2u_suppress_low && !inside_breakout_suppress_low && !twotwo_rev_f2_suppress_low;
            bool magFallbackHigh = StratGrammar.IsNa(data.ExhHighPrice) && !StratGrammar.IsNa(data.PrevMagHigh) && data.MagHighCrossed;
            bool magFallbackLow = StratGrammar.IsNa(data.ExhLowPrice) && !StratGrammar.IsNa(data.PrevMagLow) && data.MagLowCrossed;
            bool exhDisablesHigh = s.ExhDisablesInForce && (data.ExhHighCrossed || magFallbackHigh);
            bool exhDisablesLow = s.ExhDisablesInForce && (data.ExhLowCrossed || magFallbackLow);
            bool non_f2_in_force_high = signal_in_force_high && !is_f2d && currently_above_trigger;
            bool f2_in_force_high = s.ShowP3 && is_f2d && drawHigh && p3_high_passes_ftfc;
            bool non_f2_in_force_low = signal_in_force_low && !is_f2u && currently_below_trigger;
            bool f2_in_force_low = s.ShowP3 && is_f2u && drawLow && p3_low_passes_ftfc;
            bool rawInForceHigh = (non_f2_in_force_high || f2_in_force_high) && drawHigh;
            bool rawInForceLow = (non_f2_in_force_low || f2_in_force_low) && drawLow;
            ret.SignalInForceHigh = rawInForceHigh && !exhDisablesHigh;
            ret.SignalInForceLow = rawInForceLow && !exhDisablesLow;

            // Sticky stops (P1-a). F2 signals always use CC.
            bool useCC = s.StopReference == StopReferenceMode.CC;
            if (rawInForceHigh && !data.StopHighTriggered)
            {
                data.StopHighTriggered = true;
                data.StopHighPrice = (is_f2d || useCC) ? tfL_curr : data.PrevLow;
            }
            else if (data.StopHighTriggered && (is_f2d || useCC))
                data.StopHighPrice = tfL_curr;
            if (rawInForceLow && !data.StopLowTriggered)
            {
                data.StopLowTriggered = true;
                data.StopLowPrice = (is_f2u || useCC) ? tfH_curr : data.PrevHigh;
            }
            else if (data.StopLowTriggered && (is_f2u || useCC))
                data.StopLowPrice = tfH_curr;
            if (data.StopHighTriggered)
            {
                bool magHitHigh = data.MagHighCrossed && !StratGrammar.IsNa(data.PrevMagHigh);
                bool exhHitHigh = data.ExhHighCrossed && !StratGrammar.IsNa(data.ExhHighPrice);
                bool noMagHigh = StratGrammar.IsNa(data.PrevMagHigh);
                bool noExhHigh = StratGrammar.IsNa(data.ExhHighPrice);
                bool beMag = s.StopBEatMag && (magHitHigh || (noMagHigh && exhHitHigh));
                bool beExh = s.StopBEatExh && (exhHitHigh || (noExhHigh && magHitHigh));
                if (beMag || beExh)
                    data.StopHighPrice = data.PrevHigh;
            }
            if (data.StopLowTriggered)
            {
                bool magHitLow = data.MagLowCrossed && !StratGrammar.IsNa(data.PrevMagLow);
                bool exhHitLow = data.ExhLowCrossed && !StratGrammar.IsNa(data.ExhLowPrice);
                bool noMagLow = StratGrammar.IsNa(data.PrevMagLow);
                bool noExhLow = StratGrammar.IsNa(data.ExhLowPrice);
                bool beMag = s.StopBEatMag && (magHitLow || (noMagLow && exhHitLow));
                bool beExh = s.StopBEatExh && (exhHitLow || (noExhLow && magHitLow));
                if (beMag || beExh)
                    data.StopLowPrice = data.PrevLow;
            }
            data.C1IsHighContinuation = isHighContinuation;
            data.C1IsLowContinuation = isLowContinuation;

            bool cc_above_open_for_mag = tfC > tfO;
            bool threeExp_show_mag_high = cc_is_3 && s.Show3Expansions && s.Show3ExpMagnitude && (!s.ThreeExpRequireFTFC || ftfcUp) && cc_above_open_for_mag;
            bool threeExp_show_mag_low = cc_is_3 && s.Show3Expansions && s.Show3ExpMagnitude && (!s.ThreeExpRequireFTFC || ftfcDown) && !cc_above_open_for_mag;
            bool threeExp_show_exh_high = cc_is_3 && s.Show3Expansions && s.ShowExhaustion && s.Show3ExpExhaustion && (!s.ThreeExpRequireFTFC || ftfcUp) && cc_above_open_for_mag;
            bool threeExp_show_exh_low = cc_is_3 && s.Show3Expansions && s.ShowExhaustion && s.Show3ExpExhaustion && (!s.ThreeExpRequireFTFC || ftfcDown) && !cc_above_open_for_mag;
            bool inside_3u_setup = isInside && cc_is_3 && cc_above_open_for_mag && s.Show3Expansions && (!s.ThreeExpRequireFTFC || ftfcUp);
            bool inside_3d_setup = isInside && cc_is_3 && !cc_above_open_for_mag && s.Show3Expansions && (!s.ThreeExpRequireFTFC || ftfcDown);
            bool p3_suppress_mag_high = is_f2u;
            bool p3_suppress_mag_low = is_f2d;
            bool cont_suppress_mag_high = is_22_cont_high || is_32_cont_high;
            bool cont_suppress_mag_low = is_22_cont_low || is_32_cont_low;
            bool high_suppress_base = three_2d_suppress_high || inside_breakout_suppress_high || cc_3d_suppress_high;
            bool low_suppress_base = three_2u_suppress_low || inside_breakout_suppress_low || cc_3u_suppress_low;
            bool exh_high_valid = !StratGrammar.IsNa(data.ExhHighPrice) && StratGrammar.Ne(data.ExhHighPrice, data.PrevHigh);
            bool exh_low_valid = !StratGrammar.IsNa(data.ExhLowPrice) && StratGrammar.Ne(data.ExhLowPrice, data.PrevLow);
            bool cont_show_exh_high = s.ShowExhaustion && drawHigh && cont_suppress_mag_high;
            bool cont_show_exh_low = s.ShowExhaustion && drawLow && cont_suppress_mag_low;
            bool force_mag_high = s.ShowMagnitude && (drawHigh || inside_3u_setup || threeExp_show_mag_high) && !p3_suppress_mag_high && !cont_suppress_mag_high;
            bool force_mag_low = s.ShowMagnitude && (drawLow || inside_3d_setup || threeExp_show_mag_low) && !p3_suppress_mag_low && !cont_suppress_mag_low;
            bool mag_high_passes_ftfc = true;
            bool mag_low_passes_ftfc = true;
            bool price_hit_mag_high = !syntheticCC && tfH_curr >= data.PrevMagHigh;
            bool price_hit_mag_low = !syntheticCC && tfL_curr <= data.PrevMagLow;
            bool exh_high_mag_filter = !s.ExhRequiresMagHit || price_hit_mag_high;
            bool exh_low_mag_filter = !s.ExhRequiresMagHit || price_hit_mag_low;
            bool exh_high_base_condition = s.ShowExhaustion && mag_high_passes_ftfc && exh_high_mag_filter && exh_high_valid && StratGrammar.Ne(data.ExhHighPrice, data.PrevLow) && StratGrammar.Ne(data.ExhHighPrice, data.PrevMagLow) && !high_suppress_base && !p3_suppress_mag_high;
            bool exh_low_base_condition = s.ShowExhaustion && mag_low_passes_ftfc && exh_low_mag_filter && exh_low_valid && StratGrammar.Ne(data.ExhLowPrice, data.PrevHigh) && StratGrammar.Ne(data.ExhLowPrice, data.PrevMagHigh) && !low_suppress_base && !p3_suppress_mag_low;
            bool drawExhHigh = ((exh_high_base_condition && drawHigh) || (exh_high_base_condition && data.ExhHighCrossed) || (cont_show_exh_high && exh_high_valid) || (threeExp_show_exh_high && exh_high_valid)) && (!s.ExhOnlyWhenInForce || rawInForceHigh);
            bool drawExhLow = ((exh_low_base_condition && drawLow) || (exh_low_base_condition && data.ExhLowCrossed) || (cont_show_exh_low && exh_low_valid) || (threeExp_show_exh_low && exh_low_valid)) && (!s.ExhOnlyWhenInForce || rawInForceLow);
            bool exh_high_same_as_mag = drawExhHigh && StratGrammar.Eq(data.ExhHighPrice, data.PrevMagHigh);
            bool exh_low_same_as_mag = drawExhLow && StratGrammar.Eq(data.ExhLowPrice, data.PrevMagLow);
            bool mag_high_same_as_trigger = StratGrammar.Eq(data.PrevMagHigh, data.PrevHigh);
            bool mag_low_same_as_trigger = StratGrammar.Eq(data.PrevMagLow, data.PrevLow);
            bool drawMagHigh = ((force_mag_high && mag_high_passes_ftfc && !c1_broke_mag_high) || (drawHigh && !c1_broke_mag_high && signal_in_force_high)) && !high_suppress_base && !exh_high_same_as_mag && !p3_suppress_mag_high && !cont_suppress_mag_high && !mag_high_same_as_trigger && (!s.MagOnlyWhenInForce || rawInForceHigh);
            bool drawMagLow = ((force_mag_low && mag_low_passes_ftfc && !c1_broke_mag_low) || (drawLow && !c1_broke_mag_low && signal_in_force_low)) && !low_suppress_base && !exh_low_same_as_mag && !p3_suppress_mag_low && !cont_suppress_mag_low && !mag_low_same_as_trigger && (!s.MagOnlyWhenInForce || rawInForceLow);

            if (dbg != null)
            {
                dbg.TfLabel = tfLabel;
                dbg.c1_was_2u = c1_was_2u; dbg.c1_was_2d = c1_was_2d; dbg.c1_is_3 = c1_is_3;
                dbg.c2_was_2u = c2_was_2u; dbg.c2_was_2d = c2_was_2d; dbg.c2_was_3 = c2_was_3;
                dbg.c2_closed_up = c2_closed_up; dbg.c2_closed_down = c2_closed_down;
                dbg.isHammer = isHammer; dbg.isShooter = isShooter; dbg.isInside = isInside;
                dbg.cc_is_2u = cc_is_2u; dbg.cc_is_2d = cc_is_2d; dbg.cc_is_3 = cc_is_3;
                dbg.cc_broke_high = cc_broke_high; dbg.cc_broke_low = cc_broke_low;
                dbg.is_f2u = is_f2u; dbg.is_f2d = is_f2d; dbg.pre_f2u = pre_f2u; dbg.pre_f2d = pre_f2d;
                dbg.ftfc_up = ftfcUp; dbg.ftfc_down = ftfcDown;
                dbg.is_hammer_reversal = is_hammer_reversal; dbg.is_hammer_momo = is_hammer_momo; dbg.hammer_shows = hammer_shows;
                dbg.is_shooter_reversal = is_shooter_reversal; dbg.is_shooter_momo = is_shooter_momo; dbg.shooter_shows = shooter_shows;
                dbg.sif_high = signal_in_force_high; dbg.sif_low = signal_in_force_low;
                dbg.t_insideRev_h = t_insideRev_h; dbg.t_insideRev_l = t_insideRev_l;
                dbg.t_insideCont_h = t_insideCont_h; dbg.t_insideCont_l = t_insideCont_l;
                dbg.t_hamSho_h = t_hamSho_h; dbg.t_hamSho_l = t_hamSho_l;
                dbg.t_rev22_h = t_rev22_h; dbg.t_rev22_l = t_rev22_l;
                dbg.t_cont22_h = t_cont22_h; dbg.t_cont22_l = t_cont22_l;
                dbg.t_exp32_h = t_exp32_h; dbg.t_exp32_l = t_exp32_l;
                dbg.t_f2_h = t_f2_h; dbg.t_f2_l = t_f2_l;
                dbg.drawHigh = drawHigh; dbg.drawLow = drawLow;
                dbg.drawMagHigh = drawMagHigh; dbg.drawMagLow = drawMagLow;
                dbg.drawExhHigh = drawExhHigh; dbg.drawExhLow = drawExhLow;
                dbg.force_mag_high = force_mag_high; dbg.force_mag_low = force_mag_low;
                dbg.mag_high_passes_ftfc = mag_high_passes_ftfc; dbg.mag_low_passes_ftfc = mag_low_passes_ftfc;
            }

            // A NaN magnitude (no C2 loaded) draws nothing in the Pine either: line.new and
            // label.new with an na price render nothing. The debug panel above keeps the raw flag.
            drawMagHigh = drawMagHigh && !StratGrammar.IsNa(data.PrevMagHigh);
            drawMagLow = drawMagLow && !StratGrammar.IsNa(data.PrevMagLow);

            // Trigger color and style.
            SColor highColor = SColor.TvGray, lowColor = SColor.TvGray;
            LineStyleCode highStyle = LineStyleCode.Dashed, lowStyle = LineStyleCode.Dashed;
            bool cc_is_inside = !cc_broke_high && !cc_broke_low && !cc_is_3;
            if (drawHigh)
            {
                if (cc_is_3)
                {
                    bool ccCloseBelow = tfC < data.PrevLow;
                    bool ccClosingUp = tfC > tfO;
                    highColor = s.Color3High;
                    highStyle = (s.Show3Expansions && ccClosingUp && !ccCloseBelow) ? LineStyleCode.Dashed : LineStyleCode.Solid;
                }
                else if (is_f2d) { highColor = s.ColorFailed2D; highStyle = LineStyleCode.Solid; }
                else if (c1_is_3 && cc_is_2u) { highColor = s.ColorBullishActive; highStyle = LineStyleCode.Dashed; }
                else if (c1_is_3 && cc_is_inside) { highColor = s.ColorInsideHigh; highStyle = LineStyleCode.Dashed; }
                else if (c1_is_3) { highColor = s.Color3High; highStyle = LineStyleCode.Solid; }
                else if (cc_broke_high) { highColor = s.ColorBullishActive; highStyle = LineStyleCode.Dashed; }
                else if (cc_is_inside) { highColor = s.ColorInsideHigh; highStyle = LineStyleCode.Dashed; }
                else { highColor = s.ColorBullishActive; highStyle = LineStyleCode.Dashed; }
            }
            if (drawLow)
            {
                if (cc_is_3)
                {
                    bool ccCloseAbove = tfC > data.PrevHigh;
                    bool ccClosingDown = tfC < tfO;
                    lowColor = s.Color3Low;
                    lowStyle = (s.Show3Expansions && ccClosingDown && !ccCloseAbove) ? LineStyleCode.Dashed : LineStyleCode.Solid;
                }
                else if (is_f2u) { lowColor = s.ColorFailed2U; lowStyle = LineStyleCode.Solid; }
                else if (c1_is_3 && cc_is_2d) { lowColor = s.ColorBearishActive; lowStyle = LineStyleCode.Dashed; }
                else if (c1_is_3 && cc_is_inside) { lowColor = s.ColorInsideLow; lowStyle = LineStyleCode.Dashed; }
                else if (c1_is_3) { lowColor = s.Color3Low; lowStyle = LineStyleCode.Solid; }
                else if (cc_broke_low) { lowColor = s.ColorBearishActive; lowStyle = LineStyleCode.Dashed; }
                else if (cc_is_inside) { lowColor = s.ColorInsideLow; lowStyle = LineStyleCode.Dashed; }
                else { lowColor = s.ColorBearishActive; lowStyle = LineStyleCode.Dashed; }
            }
            if (is_f2u || pre_f2u) highStyle = LineStyleCode.Dashed;
            if (is_f2d || pre_f2d) lowStyle = LineStyleCode.Dashed;
            ret.HighStyle = highStyle;
            ret.LowStyle = lowStyle;

            if (drawHigh) { ret.High = data.PrevHigh; ret.HighColor = highColor; ret.DrawHigh = true; }
            if (drawLow) { ret.Low = data.PrevLow; ret.LowColor = lowColor; ret.DrawLow = true; }
            bool f2SuppressesOpen = (is_f2u || is_f2d || pre_f2u || pre_f2d) && s.ShowF2OpenLine;
            if (showOpen && (drawHigh || drawLow) && !f2SuppressesOpen)
            {
                ret.Open = data.CurrOpen;
                ret.OpenColor = SColor.TvGray;
                ret.DrawOpen = true;
            }
            if (drawMagHigh) { ret.MagHigh = data.PrevMagHigh; ret.MagHighColor = data.MagHighCrossed ? s.ColorHitHigh : s.ColorBullishActive; ret.DrawMagHigh = true; }
            if (drawMagLow) { ret.MagLow = data.PrevMagLow; ret.MagLowColor = data.MagLowCrossed ? s.ColorHitLow : s.ColorBearishActive; ret.DrawMagLow = true; }
            if (drawExhHigh) { ret.ExhHigh = data.ExhHighPrice; ret.ExhHighColor = data.ExhHighCrossed ? s.ColorHitHigh : s.ColorBullishActive; ret.DrawExhHigh = true; }
            if (drawExhLow) { ret.ExhLow = data.ExhLowPrice; ret.ExhLowColor = data.ExhLowCrossed ? s.ColorHitLow : s.ColorBearishActive; ret.DrawExhLow = true; }
            bool drawStopHigh = s.ShowStopLevels && data.StopHighTriggered;
            bool drawStopLow = s.ShowStopLevels && data.StopLowTriggered;
            if (drawStopHigh && !StratGrammar.IsNa(data.StopHighPrice)) { ret.StopHigh = data.StopHighPrice; ret.DrawStopHigh = true; }
            if (drawStopLow && !StratGrammar.IsNa(data.StopLowPrice)) { ret.StopLow = data.StopLowPrice; ret.DrawStopLow = true; }
            if (pre_f2d && !StratGrammar.IsNa(tfO)) { ret.PreF2d = true; ret.F2OpenPrice = tfO; }
            else if (is_f2d && !StratGrammar.IsNa(tfO)) ret.F2OpenPrice = tfO;
            if (pre_f2u && !StratGrammar.IsNa(tfO)) { ret.PreF2u = true; ret.F2OpenPrice = tfO; }
            else if (is_f2u && !StratGrammar.IsNa(tfO)) ret.F2OpenPrice = tfO;
            return ret;
        }

        // Pine getSignalDirection (with FIX LEAD-F2-TIEBREAK-2).
        public static int SignalDirection(SlotResult r)
        {
            if (r == null) return 0;
            if (r.SignalInForceHigh && r.SignalInForceLow)
            {
                if (r.IsF2u) return -1;
                if (r.IsF2d) return 1;
                return r.CCType.EndsWith("u") ? 1 : -1;
            }
            if (r.SignalInForceHigh) return 1;
            if (r.SignalInForceLow) return -1;
            return 0;
        }

        // Pine's Lead Signal filter applied to one slot below the anchor.
        public static void ApplyLeadFilter(SlotResult r, int anchorDirection)
        {
            bool suppressHigh = anchorDirection == -1;
            bool isCounterF2 = (suppressHigh ? r.IsF2d : r.IsF2u) && !r.C1Is3;
            bool isTrendF2 = (suppressHigh ? r.IsF2u : r.IsF2d) && !r.C1Is3;
            if (isCounterF2)
            {
                r.SuppressAllFlags();
                r.DrawOpen = false;
            }
            else if (isTrendF2)
            {
                if (suppressHigh) { r.DrawMagHigh = false; r.DrawExhHigh = false; r.DrawStopHigh = false; }
                else { r.DrawMagLow = false; r.DrawExhLow = false; r.DrawStopLow = false; }
            }
            else
            {
                if (suppressHigh) r.SuppressHighFlags(); else r.SuppressLowFlags();
                if (r.PreF2d || r.PreF2u) { r.PreF2u = false; r.PreF2d = false; }
                bool activeSideDraw = suppressHigh ? r.DrawLow : r.DrawHigh;
                if (!activeSideDraw) r.DrawOpen = false;
            }
            if (!r.DrawHigh && !r.DrawLow)
                r.DrawOpen = false;
        }

        // Pine's label-side mirror of the Lead filter: which label sides to skip.
        public static void LeadLabelSkips(SlotResult r, int anchorDirection, out bool skipHigh, out bool skipLow)
        {
            skipHigh = false;
            skipLow = false;
            bool suppressHigh = anchorDirection == -1;
            bool isCounterF2 = (suppressHigh ? r.IsF2d : r.IsF2u) && !r.C1Is3;
            bool isTrendF2 = (suppressHigh ? r.IsF2u : r.IsF2d) && !r.C1Is3;
            if (isCounterF2) { skipHigh = true; skipLow = true; }
            else if (!isTrendF2)
            {
                if (suppressHigh) skipHigh = true; else skipLow = true;
            }
        }

        // The Pine's modifiedResult for label collection when one side is skipped.
        public static SlotResult LabelView(SlotResult r, bool skipHigh, bool skipLow)
        {
            SlotResult m = r.Clone();
            if (skipHigh)
            {
                m.High = double.NaN; m.MagHigh = double.NaN; m.ExhHigh = double.NaN; m.StopHigh = double.NaN;
                m.DrawHigh = false; m.DrawMagHigh = false; m.DrawExhHigh = false; m.DrawStopHigh = false; m.PreF2u = false;
            }
            if (skipLow)
            {
                m.Low = double.NaN; m.MagLow = double.NaN; m.ExhLow = double.NaN; m.StopLow = double.NaN;
                m.DrawLow = false; m.DrawMagLow = false; m.DrawExhLow = false; m.DrawStopLow = false; m.PreF2d = false;
            }
            m.DrawOpen = r.DrawOpen && ((!skipHigh && r.DrawHigh) || (!skipLow && r.DrawLow));
            // The Pine rebuilds this object positionally and leaves the style fields at their
            // defaults ("Dashed"); styles only feed lines, never labels, so nothing differs.
            return m;
        }

        // Pine findExhaustionLevels over an HTF series indexed oldest-first; `p` is the forming
        // bar. Current channel (P0-2): seed [1], candidates from i=2. Next channel: seed [0],
        // candidates from i=1. Offsets returned are HTF indices (-1 = none).
        public static void FindExhaustion(Func<int, double> high, Func<int, double> low, int p, bool showAnyTargets,
            out double exhH, out int exhHIdx, out double exhL, out int exhLIdx,
            out double nxtH, out int nxtHIdx, out double nxtL, out int nxtLIdx)
        {
            exhH = exhL = nxtH = nxtL = double.NaN;
            exhHIdx = exhLIdx = nxtHIdx = nxtLIdx = -1;
            if (!showAnyTargets || p <= 50)
                return;
            double maxCur = high(p - 1), minCur = low(p - 1);
            double maxNxt = high(p), minNxt = low(p);
            for (int i = 1; i <= 48; i++)
            {
                double hi = high(p - i), lo = low(p - i);
                bool isPivotHigh = hi > high(p - i + 1) && hi > high(p - i - 1);
                bool isPivotLow = lo < low(p - i + 1) && lo < low(p - i - 1);
                if (StratGrammar.IsNa(nxtH) && isPivotHigh && maxNxt < hi) { nxtH = hi; nxtHIdx = p - i; }
                if (StratGrammar.IsNa(nxtL) && isPivotLow && minNxt > lo) { nxtL = lo; nxtLIdx = p - i; }
                if (i >= 2)
                {
                    if (StratGrammar.IsNa(exhH) && isPivotHigh && maxCur < hi) { exhH = hi; exhHIdx = p - i; }
                    if (StratGrammar.IsNa(exhL) && isPivotLow && minCur > lo) { exhL = lo; exhLIdx = p - i; }
                    maxCur = Math.Max(maxCur, hi);
                    minCur = Math.Min(minCur, lo);
                }
                maxNxt = Math.Max(maxNxt, hi);
                minNxt = Math.Min(minNxt, lo);
                if (!StratGrammar.IsNa(exhH) && !StratGrammar.IsNa(exhL) && !StratGrammar.IsNa(nxtH) && !StratGrammar.IsNa(nxtL))
                    break;
            }
        }
    }

    // ============================================================================
    // LABEL TEXT (Pine SECTION 8) AND ALERT TEXT (SECTION 13)
    // ============================================================================

    public sealed class LabelEntry
    {
        public double Price;
        public string Text;
        public SColor Color;
        public double EndX;     // line end, in chart-bar units (see the indicator)
    }

    public static class StratLabels
    {
        public const string Up = "↑";
        public const string Down = "↓";
        public const string Diamond = "◆";
        public const string GreenCircle = "\U0001F7E2";
        public const string RedCircle = "\U0001F534";

        // Pine formatCombo: a dash before each bar token that follows a completed one.
        public static string FormatCombo(string combo, bool useDash)
        {
            if (!useDash || combo.Length < 2)
                return combo;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < combo.Length; i++)
            {
                char ch = combo[i];
                bool isBarStart = ch == '1' || ch == '2' || ch == '3' || ch == 'F' || ch == 'f' || ch == '*';
                if (isBarStart && i > 0)
                {
                    char prev = combo[i - 1];
                    if (prev == 'u' || prev == 'd' || prev == '1' || prev == '2' || prev == '3')
                        sb.Append('-');
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        public static long TickKey(double price, double tick)
        {
            return (long)Math.Round(price / tick, MidpointRounding.AwayFromZero);
        }

        public static string FormatPrice(double price, double tick)
        {
            double rounded = Math.Round(price / tick, MidpointRounding.AwayFromZero) * tick;
            int decimals = 0;
            double t = tick;
            while (decimals < 10 && Math.Abs(t - Math.Round(t)) > 1e-9) { t *= 10; decimals++; }
            return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        static void Push(List<LabelEntry> list, double price, string text, SColor color, double endX)
        {
            LabelEntry e = new LabelEntry();
            e.Price = price; e.Text = text; e.Color = color; e.EndX = endX;
            list.Add(e);
        }

        // Pine collectTimeframeLabels.
        public static void CollectTimeframeLabels(StratSettings s, SlotState data, SlotResult r, double tick, double endX,
            List<LabelEntry> highLabels, List<LabelEntry> lowLabels, List<LabelEntry> openLabels)
        {
            bool ccIsInside = r.CCType == "1";
            bool ccIs3 = r.CCType.StartsWith("3");
            bool stopHighAtBE = r.DrawStopHigh && !StratGrammar.IsNa(r.StopHigh) && !StratGrammar.IsNa(r.High) && TickKey(r.StopHigh, tick) == TickKey(r.High, tick);
            bool stopLowAtBE = r.DrawStopLow && !StratGrammar.IsNa(r.StopLow) && !StratGrammar.IsNa(r.Low) && TickKey(r.StopLow, tick) == TickKey(r.Low, tick);
            if (r.DrawHigh || r.DrawLow)
            {
                string patternSuffixHigh = data.C1IsHammer ? " HAM" : "";
                string patternSuffixLow = data.C1IsShooter ? " SHO" : "";
                string comboHigh = "", comboLow = "";
                bool c1IsInside = r.C1Num == "1";
                bool is22Pattern = r.C1Num == "2";
                bool is32Pattern = r.C1Num == "3";
                bool isDoubleInside = r.C1Num == "1" && r.C2Num == "1";
                string c1TypeF2 = (r.C1WasF2 ? "F" : "") + r.C1Type;
                string c1NumF2 = (r.C1WasF2 ? "F" : "") + r.C1Num;
                string c2NumF2 = (r.C2WasF2 ? "F" : "") + r.C2Num;
                if (s.SimpleLabelMode)
                {
                    string simpleHigh, simpleLow;
                    bool ccIs2u = r.CCType.StartsWith("2u");
                    bool ccIs2d = r.CCType.StartsWith("2d");
                    if (ccIs3) { simpleHigh = Up + "EXP"; simpleLow = Down + "EXP"; }
                    else if (is32Pattern && ccIs2u) { simpleHigh = Up + "EXP"; simpleLow = "*OUT"; }
                    else if (is32Pattern && ccIs2d) { simpleHigh = "*OUT"; simpleLow = Down + "EXP"; }
                    else if (is32Pattern) { simpleHigh = Up + "EXP"; simpleLow = Down + "EXP"; }
                    else if (c1IsInside)
                    {
                        if (ccIsInside) { simpleHigh = "*" + Up + "INS"; simpleLow = "*" + Down + "INS"; }
                        else { simpleHigh = Up + "INS"; simpleLow = Down + "INS"; }
                    }
                    else if (ccIsInside)
                    {
                        simpleHigh = data.C1IsHighContinuation ? "*" + Up + "CONT" : "*" + Up + "REV";
                        simpleLow = data.C1IsLowContinuation ? "*" + Down + "CONT" : "*" + Down + "REV";
                    }
                    else
                    {
                        simpleHigh = data.C1IsHighContinuation ? Up + "CONT" : Up + "REV";
                        simpleLow = data.C1IsLowContinuation ? Down + "CONT" : Down + "REV";
                    }
                    if (data.C1IsHammer) simpleHigh = simpleHigh + Diamond;
                    if (data.C1IsShooter) simpleLow = simpleLow + Diamond;
                    if (r.IsF2u || r.PreF2u)
                    {
                        simpleHigh = simpleHigh + " FAILING";
                        simpleLow = "*OUT";
                    }
                    else if (r.IsF2d || r.PreF2d)
                    {
                        simpleLow = simpleLow + " FAILING";
                        simpleHigh = "*OUT";
                    }
                    comboHigh = simpleHigh;
                    comboLow = simpleLow;
                    patternSuffixHigh = "";
                    patternSuffixLow = "";
                }
                else
                {
                    if (ccIsInside)
                    {
                        if (isDoubleInside) { comboHigh = "*" + r.C3Num + c2NumF2 + c1NumF2 + "2u"; comboLow = "*" + r.C3Num + c2NumF2 + c1NumF2 + "2d"; }
                        else if (c1IsInside) { comboHigh = "*" + c2NumF2 + c1NumF2 + "2u"; comboLow = "*" + c2NumF2 + c1NumF2 + "2d"; }
                        else if (is32Pattern) { comboHigh = "*32u"; comboLow = "*32d"; }
                        else if (r.C2Num == "3") { comboHigh = "*3" + c1TypeF2 + "2u"; comboLow = "*3" + c1TypeF2 + "2d"; }
                        else { comboHigh = "*" + c1TypeF2 + "2u"; comboLow = "*" + c1TypeF2 + "2d"; }
                    }
                    else
                    {
                        string baseCCType = r.CCType;
                        if (r.IsF2u) baseCCType = "2u";
                        else if (r.IsF2d) baseCCType = "2d";
                        string defaultCombo;
                        string c2Prefix = (r.C2Num == "3" && is22Pattern) ? "3" : "";
                        if (is22Pattern) defaultCombo = c2Prefix + c1TypeF2 + baseCCType;
                        else if (is32Pattern) defaultCombo = "3" + baseCCType;
                        else if (isDoubleInside) defaultCombo = r.C3Num + c2NumF2 + c1NumF2 + baseCCType;
                        else defaultCombo = c2NumF2 + c1NumF2 + baseCCType;
                        if (r.IsF2u) { comboHigh = c1TypeF2 + "F2u"; comboLow = "*" + c1TypeF2 + "3d"; }
                        else if (r.IsF2d) { comboLow = c1TypeF2 + "F2d"; comboHigh = "*" + c1TypeF2 + "3u"; }
                        else { comboHigh = defaultCombo; comboLow = defaultCombo; }
                    }
                }
                if (r.DrawHigh)
                    Push(highLabels, r.High, r.TfLabel + " " + FormatCombo(comboHigh, s.UseDashSeparator) + patternSuffixHigh + (s.ShowHighLowInLabel ? " H" : "") + (stopHighAtBE ? " + STOP" : ""), r.HighColor, endX);
                if (r.DrawLow)
                    Push(lowLabels, r.Low, r.TfLabel + " " + FormatCombo(comboLow, s.UseDashSeparator) + patternSuffixLow + (s.ShowHighLowInLabel ? " L" : "") + (stopLowAtBE ? " + STOP" : ""), r.LowColor, endX);
            }
            if (r.DrawOpen)
                Push(openLabels, r.Open, r.TfLabel + " OPEN", r.OpenColor, endX);
            if (r.DrawMagHigh)
                Push(highLabels, r.MagHigh, r.TfLabel + (s.SimpleLabelMode ? " TARGET" : " MAG") + (s.ShowHighLowInLabel ? " H" : ""), r.MagHighColor, endX);
            if (r.DrawMagLow)
                Push(lowLabels, r.MagLow, r.TfLabel + (s.SimpleLabelMode ? " TARGET" : " MAG") + (s.ShowHighLowInLabel ? " L" : ""), r.MagLowColor, endX);
            if (r.DrawExhHigh)
                Push(highLabels, r.ExhHigh, r.TfLabel + (s.SimpleLabelMode ? " FINAL" : " EXH") + (s.ShowHighLowInLabel ? " H" : ""), r.ExhHighColor, endX);
            if (r.DrawExhLow)
                Push(lowLabels, r.ExhLow, r.TfLabel + (s.SimpleLabelMode ? " FINAL" : " EXH") + (s.ShowHighLowInLabel ? " L" : ""), r.ExhLowColor, endX);
            if (r.DrawStopHigh && !stopHighAtBE)
                Push(lowLabels, r.StopHigh, r.TfLabel + " STOP", s.ColorStopHigh, endX);
            if (r.DrawStopLow && !stopLowAtBE)
                Push(highLabels, r.StopLow, r.TfLabel + " STOP", s.ColorStopLow, endX);
            if (s.ShowF2OpenLine && r.PreF2d && !StratGrammar.IsNa(r.F2OpenPrice))
            {
                if (!(StratGrammar.IsNa(r.Low) ? false : TickKey(r.F2OpenPrice, tick) == TickKey(r.Low, tick)))
                    Push(highLabels, r.F2OpenPrice, r.TfLabel + (s.SimpleLabelMode ? " OPEN" : " *F2d"), s.ColorBullishActive, endX);
            }
            if (s.ShowF2OpenLine && r.PreF2u && !StratGrammar.IsNa(r.F2OpenPrice))
            {
                if (!(StratGrammar.IsNa(r.High) ? false : TickKey(r.F2OpenPrice, tick) == TickKey(r.High, tick)))
                    Push(lowLabels, r.F2OpenPrice, r.TfLabel + (s.SimpleLabelMode ? " OPEN" : " *F2u"), s.ColorBearishActive, endX);
            }
            if (s.ShowF2OpenLine && r.IsF2d && !r.PreF2d && !StratGrammar.IsNa(r.F2OpenPrice) && (r.DrawHigh || r.DrawLow))
            {
                if (!(StratGrammar.IsNa(r.Low) ? false : TickKey(r.F2OpenPrice, tick) == TickKey(r.Low, tick)))
                    Push(highLabels, r.F2OpenPrice, r.TfLabel + (s.SimpleLabelMode ? " OPEN" : " F2d"), s.ColorFailed2D, endX);
            }
            if (s.ShowF2OpenLine && r.IsF2u && !r.PreF2u && !StratGrammar.IsNa(r.F2OpenPrice) && (r.DrawHigh || r.DrawLow))
            {
                if (!(StratGrammar.IsNa(r.High) ? false : TickKey(r.F2OpenPrice, tick) == TickKey(r.High, tick)))
                    Push(lowLabels, r.F2OpenPrice, r.TfLabel + (s.SimpleLabelMode ? " OPEN" : " F2u"), s.ColorFailed2U, endX);
            }
        }

        // Pine consolidateAndCreate's merge step: same tick-rounded price joins with " + ",
        // keeping the first entry's color and end.
        public static List<LabelEntry> Consolidate(List<LabelEntry> input, double tick)
        {
            List<LabelEntry> output = new List<LabelEntry>();
            Dictionary<long, int> byPrice = new Dictionary<long, int>();
            foreach (LabelEntry e in input)
            {
                long k = TickKey(e.Price, tick);
                int idx;
                if (byPrice.TryGetValue(k, out idx))
                    output[idx].Text = output[idx].Text + " + " + e.Text;
                else
                {
                    byPrice[k] = output.Count;
                    LabelEntry c = new LabelEntry();
                    c.Price = e.Price; c.Text = e.Text; c.Color = e.Color; c.EndX = e.EndX;
                    output.Add(c);
                }
            }
            return output;
        }

        // Pine isSetupBull / isSetupBear / isF2Bull / isF2Bear / isPotentialBull / isPotentialBear.
        public static bool IsSetupBull(SlotResult r) { return r != null && r.DrawHigh && !r.IsF2d && (r.CCType.StartsWith("3") ? r.CCType == "3u" : r.CCType == "2u"); }
        public static bool IsSetupBear(SlotResult r) { return r != null && r.DrawLow && !r.IsF2u && (r.CCType.StartsWith("3") ? r.CCType == "3d" : r.CCType == "2d"); }
        public static bool IsF2Bull(StratSettings s, SlotResult r) { return s.ShowP3 && r != null && r.DrawHigh && r.IsF2d; }
        public static bool IsF2Bear(StratSettings s, SlotResult r) { return s.ShowP3 && r != null && r.DrawLow && r.IsF2u; }
        public static bool IsPotentialBull(SlotResult r) { return r != null && r.DrawHigh && r.CCType == "1"; }
        public static bool IsPotentialBear(SlotResult r) { return r != null && r.DrawLow && r.CCType == "1"; }

        // Pine buildAlertLabel. `full` carries the pre-Lead prices (P1-j detail fields).
        public static string BuildAlertLabel(StratSettings s, SlotResult r, bool isBullish, SlotState data, SlotResult full, double tick)
        {
            if (r == null) return "";
            string patternSuffix;
            if (s.SimpleLabelMode)
                patternSuffix = (isBullish && data.C1IsHammer) ? Diamond : (!isBullish && data.C1IsShooter) ? Diamond : "";
            else
                patternSuffix = (isBullish && data.C1IsHammer) ? " HAM" : (!isBullish && data.C1IsShooter) ? " SHO" : "";
            string combo;
            bool c1IsInside = r.C1Num == "1";
            bool is32Pattern = r.C1Is3;
            bool ccIs3 = r.CCType.StartsWith("3");
            bool ccIsInsideAlert = r.CCType == "1";
            string c1TypeF2a = (r.C1WasF2 ? "F" : "") + r.C1Type;
            string c1NumF2a = (r.C1WasF2 ? "F" : "") + r.C1Num;
            string c2NumF2a = (r.C2WasF2 ? "F" : "") + r.C2Num;
            if (s.SimpleLabelMode)
            {
                string dirArrow = isBullish ? Up : Down;
                if (ccIs3 || is32Pattern) combo = dirArrow + "EXP";
                else if ((r.IsF2d && isBullish) || (r.IsF2u && !isBullish)) combo = dirArrow + "FAILING";
                else if (c1IsInside) combo = dirArrow + "INS";
                else if (ccIsInsideAlert)
                {
                    bool isCont = (isBullish && data.C1IsHighContinuation) || (!isBullish && data.C1IsLowContinuation);
                    combo = isCont ? dirArrow + "CONT" : dirArrow + "REV";
                }
                else if (isBullish && data.C1IsHighContinuation) combo = dirArrow + "CONT";
                else if (!isBullish && data.C1IsLowContinuation) combo = dirArrow + "CONT";
                else combo = dirArrow + "REV";
            }
            else
            {
                if (r.IsF2d && isBullish) combo = c1TypeF2a + "F2d";
                else if (r.IsF2u && !isBullish) combo = c1TypeF2a + "F2u";
                else if (is32Pattern) combo = "3" + r.CCType;
                else if (c1IsInside) combo = c2NumF2a + c1NumF2a + r.CCType;
                else combo = c1TypeF2a + r.CCType;
            }
            string side = s.ShowHighLowInLabel ? (isBullish ? " H" : " L") : "";
            string emoji = isBullish ? " " + GreenCircle : " " + RedCircle;
            string result = r.TfLabel + " " + FormatCombo(combo, s.UseDashSeparator) + patternSuffix + side + emoji;
            string details = "";
            if (s.AlertShowTrigger)
            {
                double p = isBullish ? full.High : full.Low;
                if (!StratGrammar.IsNa(p)) details = details + (details == "" ? "" : " ") + "@ " + FormatPrice(p, tick);
            }
            if (s.AlertShowMagnitude)
            {
                double p = isBullish ? full.MagHigh : full.MagLow;
                if (!StratGrammar.IsNa(p)) details = details + (details == "" ? "" : " ") + "MAG " + FormatPrice(p, tick);
            }
            if (s.AlertShowExhaustion)
            {
                double p = isBullish ? full.ExhHigh : full.ExhLow;
                if (!StratGrammar.IsNa(p)) details = details + (details == "" ? "" : " ") + "EXH " + FormatPrice(p, tick);
            }
            if (s.AlertShowStop)
            {
                double p = isBullish ? full.StopHigh : full.StopLow;
                if (!StratGrammar.IsNa(p)) details = details + (details == "" ? "" : " ") + "STOP " + FormatPrice(p, tick);
            }
            if (details != "")
                result = result + " | " + details;
            return result;
        }
    }

    // ============================================================================
    // DOMINO (Pine SECTION 11, runs every bar)
    // ============================================================================

    public static class StratDomino
    {
        // results/previews/enabled indexed by slot 0..5; walks 5 -> 0 like the Pine.
        public static void Compute(SlotResult[] results, bool[] enabled, bool[] isPreview, string[] tfLabels, out int bestRun, out string bestTFs)
        {
            int currentRunBull = 0, currentRunBear = 0, bestRunBull = 0, bestRunBear = 0;
            string currentTFsBull = "", currentTFsBear = "", bestTFsBull = "", bestTFsBear = "";
            bool hasPrevBull = false, hasPrevBear = false;
            long prevBull = 0, prevBear = 0;
            for (int i = 5; i >= 0; i--)
            {
                if (!enabled[i]) continue;
                SlotResult r = results[i];
                bool ccIsInside = r != null && !isPreview[i] && r.CCType.StartsWith("1");
                bool hasBull = r != null && r.DrawHigh;
                bool hasBear = r != null && r.DrawLow;
                bool hasCC = r != null;
                long cc = r != null ? r.CCStartId : 0;
                bool bullSame = hasCC && hasPrevBull && cc == prevBull;
                if (ccIsInside && hasBull && !bullSame)
                {
                    currentRunBull++;
                    currentTFsBull = currentTFsBull == "" ? tfLabels[i] : tfLabels[i] + "+" + currentTFsBull;
                    prevBull = cc; hasPrevBull = hasCC;
                    if (currentRunBull > bestRunBull) { bestRunBull = currentRunBull; bestTFsBull = currentTFsBull; }
                }
                else if (ccIsInside && hasBull && bullSame)
                {
                    prevBull = cc; hasPrevBull = hasCC;
                }
                else
                {
                    currentRunBull = 0; currentTFsBull = ""; hasPrevBull = false;
                }
                bool bearSame = hasCC && hasPrevBear && cc == prevBear;
                if (ccIsInside && hasBear && !bearSame)
                {
                    currentRunBear++;
                    currentTFsBear = currentTFsBear == "" ? tfLabels[i] : tfLabels[i] + "+" + currentTFsBear;
                    prevBear = cc; hasPrevBear = hasCC;
                    if (currentRunBear > bestRunBear) { bestRunBear = currentRunBear; bestTFsBear = currentTFsBear; }
                }
                else if (ccIsInside && hasBear && bearSame)
                {
                    prevBear = cc; hasPrevBear = hasCC;
                }
                else
                {
                    currentRunBear = 0; currentTFsBear = ""; hasPrevBear = false;
                }
            }
            if (bestRunBull >= bestRunBear) { bestRun = bestRunBull; bestTFs = bestTFsBull; }
            else { bestRun = bestRunBear; bestTFs = bestTFsBear; }
        }
    }
}
