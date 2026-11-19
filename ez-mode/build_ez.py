#!/usr/bin/env python3
"""Generate TheStrat Suite EZ Mode from the TheStrat Suite source.

EZ Mode is the Suite with most settings fixed to their defaults, plus a teaching
layer (hover text on the data table and level labels, and an Explain row). It is a
generated file so the engine never drifts from the Suite: fix the Suite, rerun this.

    python3 ez-mode/build_ez.py            # write the EZ Mode file
    python3 ez-mode/build_ez.py --check    # fail if the committed file is stale

How it works:
  1. Every `name = input.*(default, ...)` line becomes `name = default`, unless the
     name is in OVERRIDES (a different fixed value, or one of the EZ Mode inputs).
  2. PATCHES are exact-text replacements for the teaching layer. Each anchor must
     match exactly once, so a Suite change that moves an anchor fails the build
     loudly instead of producing a silently different script.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SOURCE = ROOT / "pine" / "TheStratSuite_v3.1.1.pine"
EZ_VERSION = "0.1.0"
OUTPUT = Path(__file__).resolve().parent / f"TheStratSuite_EZ_v{EZ_VERSION}.pine"
TITLE = "TheStrat Suite EZ Mode - Learn TheStrat"

INPUT_RE = re.compile(r"^(\w+) = input\.(\w+)\((.*)$")


def first_arg(args: str) -> str:
    """Return the first top-level argument of an input.*( call body."""
    depth = 0
    quote = ""
    for i, ch in enumerate(args):
        if quote:
            if ch == quote:
                quote = ""
            continue
        if ch in "\"'":
            quote = ch
        elif ch in "([":
            depth += 1
        elif ch in ")]":
            if depth == 0:
                return args[:i].strip()
            depth -= 1
        elif ch == "," and depth == 0:
            return args[:i].strip()
    raise ValueError(f"cannot parse input arguments: {args[:80]}")


# ---------------------------------------------------------------------------
# Inputs: fixed values that differ from the Suite default, and the EZ inputs.
# ---------------------------------------------------------------------------

SETUP_BLOCK = '''// EZ-MODE: one Style input stands in for the Suite's preset and its 24 timeframe inputs.
// Four timeframes, lowest to highest. TheStrat Classic is the 60-minute, daily, weekly and
// monthly set that Full Timeframe Continuity is taught on.
grpEZSetup = "Setup"
grpEZSignals = "Extra Signals (optional)"
grpEZChart = "On The Chart"
ezStyle = input.string("TheStrat Classic", "Timeframes", options = ["TheStrat Classic", "Day Trade", "Swing Trade", "Investing", "Custom"], group = grpEZSetup, tooltip = "Picks the four timeframes to read. TheStrat Classic: 60-minute, Day, Week, Month. Day Trade: 15-minute, 60-minute, Day, Week. Swing Trade: 4-hour, Day, Week, Month. Investing: Day, Week, Month, Quarter. Custom: choose your own below. New to TheStrat? Leave this on TheStrat Classic.")
ezCustom = ezStyle == "Custom"
ezTF1 = input.timeframe("60", "Timeframe 1 (lowest)", group = grpEZSetup, active = ezCustom, tooltip = "Only used when Timeframes is set to Custom. Set the four timeframes from lowest to highest. A timeframe lower than your chart cannot be read and shows as blank.")
ezTF2 = input.timeframe("D", "Timeframe 2", group = grpEZSetup, active = ezCustom)
ezTF3 = input.timeframe("W", "Timeframe 3", group = grpEZSetup, active = ezCustom)
ezTF4 = input.timeframe("M", "Timeframe 4 (highest)", group = grpEZSetup, active = ezCustom)
tfPreset = "Custom"'''


def style_tf(classic: str, day: str, swing: str, invest: str, custom: str) -> str:
    return (f'ezStyle == "TheStrat Classic" ? "{classic}" : ezStyle == "Day Trade" ? "{day}" : '
            f'ezStyle == "Swing Trade" ? "{swing}" : ezStyle == "Investing" ? "{invest}" : {custom}')


OVERRIDES: dict[str, str] = {
    "tfPreset": SETUP_BLOCK,
    "labelStyleMode": 'labelStyleMode = input.string("Strat Numbers", "Labels", options = ["Strat Numbers", "Plain Words"], group = grpEZSetup, tooltip = "Strat Numbers: labels use Strat notation, for example 2d-2u, MAG and EXH. Plain Words: words instead. TARGET is magnitude, FINAL is exhaustion, REV is a reversal, CONT a continuation, INS an inside bar break, EXP an expansion, OUT an outside bar.")',
    # Four slots on, lowest to highest; slots 5 and 6 stay off.
    "tf1Enabled": "tf1Enabled = true",
    "tf2Enabled": "tf2Enabled = true",
    "tf3Enabled": "tf3Enabled = true",
    "tf4Enabled": "tf4Enabled = true",
    "tf1": "tf1 = " + style_tf("60", "15", "240", "D", "ezTF1"),
    "tf2": "tf2 = " + style_tf("D", "60", "D", "W", "ezTF2"),
    "tf3": "tf3 = " + style_tf("W", "D", "W", "M", "ezTF3"),
    "tf4": "tf4 = " + style_tf("M", "W", "M", "3M", "ezTF4"),
    "tf1LineWidth": "tf1LineWidth = 1",
    "tf2LineWidth": "tf2LineWidth = 2",
    "tf3LineWidth": "tf3LineWidth = 3",
    "tf4LineWidth": "tf4LineWidth = 4",
    # Optional signals: a plain on/off each, no filters beside them.
    "showContinuations": 'showContinuations = input.bool(false, "2-2 Continuations", group = grpEZSignals, tooltip = "Reversals and inside bar breaks are always shown. This adds the 2-2 continuation: a 2u after a 2u, or a 2d after a 2d. The trend keeps going the same way.")',
    "show_p3_toggle": 'show_p3_toggle = input.bool(false, "Failing 2s", group = grpEZSignals, tooltip = "A Failing 2 is a bar that broke the prior bar\'s high but is now trading below its own open (red), or broke the prior low but is now trading above its own open (green). One side took the level and the other side took the bar back. If it goes on to break the other side it becomes a 3.")',
    "showRangeExpansions": 'showRangeExpansions = input.bool(false, "3-2 Expansions", group = grpEZSignals, tooltip = "A 3 is an outside bar: it took both the high and the low of the bar before it. A 3-2 is the next bar breaking one side of that wide bar.")',
    # A Failing 2 is judged against the bar's own open (decided 2026-11-18).
    "failed_2_method": 'failed_2_method = "Open"',
    "showDominoInTable": "showDominoInTable = false",
    "showStopLevels": 'showStopLevels = input.bool(true, "Stops", group = grpEZChart, tooltip = "Draws a stop level for each signal: the price where the idea is wrong. Treat it as a reference level, not a full risk plan.")',
    "previewMode": 'previewMode = input.string("Auto", "Preview Mode", options = ["Off", "On", "Auto"], group = grpEZChart, tooltip = "Auto: when the market is closed, shows the levels for the next bar so you can plan ahead. The table says PREVIEW MODE while it is on. Off: never preview. Use Off with Bar Replay. On: always preview.")',
    "showBarTypeTable": 'showBarTypeTable = input.bool(true, "Data Table", inline = "ez_tbl", group = grpEZChart, tooltip = "The table of timeframes. Hover any cell for a plain-English read of what it means right now.")',
    "tablePosition": 'tablePosition = input.string("Top Right", "", options = ["Bottom Center", "Bottom Left", "Bottom Right", "Middle Center", "Middle Left", "Middle Right", "Top Center", "Top Left", "Top Right"], inline = "ez_tbl", group = grpEZChart)\n'
                     'ezTableStyle = input.string("Simple", "Table Style", options = ["Simple", "Detailed"], group = grpEZChart, tooltip = "Simple: one line per timeframe showing the combo, oldest bar first, with the current bar highlighted at the end. If the bar before the current one is a hammer or shooter, the Prior column says so. Detailed: the full Suite table with a column each for C2, C1, the actionable signal and the current candle.")\n'
                     'ezSimpleTable = ezTableStyle == "Simple"\n'
                     'ezLastCol = ezSimpleTable ? 3 : 4\n'
                     'ezExplain = input.bool(true, "Explain Row", group = grpEZChart, tooltip = "Adds a short plain-English summary under the table: which way the timeframes lean and what is in force. Turn it off once you can read the table on your own.")',
    "showBarNumbers": 'showBarNumbers = input.bool(true, "Bar Numbers", group = grpEZChart, tooltip = "Prints each candle\'s Strat number on the last 50 bars. 1: inside bar, took neither side of the bar before it. 2u: took the prior high. 2d: took the prior low. 3: outside bar, took both. F2u and F2d: a Failing 2, a 2 trading back through its own open.")',
}

# Alert conditions that do not appear in EZ Mode's alert dialog.
DROPPED_ALERTS = re.compile(r'^alertcondition\(.*"(Signal In-Force \(TF\d\)|Domino Setup)"')


# ---------------------------------------------------------------------------
# Teaching layer: helper functions, inserted before SECTION 8.
# ---------------------------------------------------------------------------

EZ_HELPERS = '''// ============================================================================
// EZ-MODE: TEACHING LAYER (hover text and the Explain row)
// ============================================================================
// TheStrat was created by Rob Smith. The explanations below are this script's own wording
// of his concepts. Where this script's usage differs from the classic one (3u/3d and
// exhaustion), the hover says so.

// Hover text for one bar-type cell. `live` is true for the forming bar.
ezBarTip(string t, string tf, bool live) =>
    string which = live ? "The current " + tf + " bar (still forming)" : "This closed " + tf + " bar"
    string openUp = live ? "It is trading above its own open." : "It closed above its own open."
    string openDn = live ? "It is trading below its own open." : "It closed below its own open."
    switch t
        "1u" => "1 = inside bar. " + which + " has not taken the high or the low of the bar before it. Price is paused and must eventually break one side. " + openUp
        "1d" => "1 = inside bar. " + which + " has not taken the high or the low of the bar before it. Price is paused and must eventually break one side. " + openDn
        "2u" => "2u = " + which + " took out the prior bar's high and not its low. Buyers were willing to pay up."
        "2d" => "2d = " + which + " took out the prior bar's low and not its high. Sellers were willing to sell down."
        "F2u" => "F2u = Failing 2u. " + which + " took out the prior bar's high, but price is back below this bar's own open (red). Buyers took the level and sellers took the bar back. If it also breaks the prior low it becomes a 3."
        "F2d" => "F2d = Failing 2d. " + which + " took out the prior bar's low, but price is back above this bar's own open (green). Sellers took the level and buyers took the bar back. If it also breaks the prior high it becomes a 3."
        "3u" => "3 = outside bar. " + which + " took out both the high and the low of the bar before it. The u means it is above its own open. (Classic usage keeps 3u for a 3 that closes above the prior bar's high.)"
        "3d" => "3 = outside bar. " + which + " took out both the high and the low of the bar before it. The d means it is below its own open. (Classic usage keeps 3d for a 3 that closes below the prior bar's low.)"
        => "No bar to read here yet."

// Plain name for a combo string such as 2d-2u or *3-1-2u.
ezComboName(string combo) =>
    string c = str.replace_all(str.replace_all(combo, "-", ""), "*", "")
    string n = "combo"
    if str.endswith(c, "12u") or str.endswith(c, "12d")
        n := str.startswith(c, "3") ? "3-1-2 (an outside bar, an inside bar, then the break)" : "inside bar break (price paused inside the prior bar, then picked a side)"
    else if str.endswith(c, "2d2u") or str.endswith(c, "2u2d")
        n := str.startswith(c, "1") ? "1-2-2 RevStrat (an inside bar broke one way, then price reversed)" : str.startswith(c, "3") ? "3-2-2 reversal (an outside bar, a 2 one way, then a 2 back the other way)" : "2-2 reversal (a bar trending one way, answered by a bar trending the other way)"
    else if str.endswith(c, "2u2u") or str.endswith(c, "2d2d")
        n := "2-2 continuation (the trend keeps going the same way)"
    else if str.endswith(c, "32u") or str.endswith(c, "32d")
        n := "3-2 (an outside bar, then a break of one of its sides)"
    n

// Hover text for one label piece. side: 1 = label above price (highs), -1 = below (lows), 0 = opens.
ezPieceTip(string piece, string px, int side) =>
    int sp = str.pos(piece, " ")
    string tf = na(sp) ? "" : str.substring(piece, 0, sp)
    string body = na(sp) ? piece : str.substring(piece, sp + 1)
    string dirWord = side == 1 ? "bullish" : "bearish"
    string out = ""
    if str.endswith(piece, "STOP")
        // A bullish signal's stop sits below price, so it is collected with the lows.
        string stopDir = side == -1 ? "bullish" : "bearish"
        string stopMove = side == -1 ? "back below" : "back above"
        out := tf == "" ? "A stop level also sits at this price." : tf + " STOP (" + px + "): the stop for the " + stopDir + " " + tf + " signal. If price trades " + stopMove + " this level, the idea is wrong. Treat it as a reference level, not a full risk plan."
    else if str.endswith(body, "OPEN")
        out := tf + " OPEN (" + px + "): where the " + tf + " bar opened. Above it the bar is green, below it red. Full Timeframe Continuity compares price with these opens."
    else if str.contains(body, "MAG") or str.contains(body, "TARGET")
        out := tf + " magnitude (" + px + "): the target for the " + dirWord + " " + tf + " signal. It is the far side of the range the signal is moving back into. Once price gets here the signal has done what it measured."
    else if str.contains(body, "EXH") or str.contains(body, "FINAL")
        out := tf + " exhaustion (" + px + "): the next prior " + (side == 1 ? "high" : "low") + " beyond magnitude, where the move may run out of room. Rob Smith used \\"exhaustion risk\\" for a signal running out of time or reaching its magnitude. This level is this script's price-based version of that idea."
    else if str.contains(body, "F2u") or str.contains(body, "F2d") or str.contains(body, "FAILING")
        out := tf + " Failing 2 (" + px + "): the " + tf + " bar broke this level but price is back on the wrong side of the bar's own open. The break is failing. If the bar goes on to break its other side it becomes a 3 (outside bar)."
    else if str.contains(body, "*")
        bool toOutside = str.contains(body, "3u") or str.contains(body, "3d") or str.contains(body, "OUT")
        string becomes = toOutside ? "a 3 (outside bar), because it has already taken the other side" : side == 1 ? "a 2u" : "a 2d"
        out := tf + " trigger (" + px + "), not hit yet. If price trades " + (side == 1 ? "above" : "below") + " this level, the " + tf + " bar becomes " + becomes + "." + (toOutside or simpleLabelMode ? "" : " That would put this combo in force: a " + ezComboName(body) + ".")
    else if simpleLabelMode
        string word = str.contains(body, "REV") ? "REV = reversal: a bar trending one way answered by a bar trending the other way." : str.contains(body, "CONT") ? "CONT = continuation: the trend keeps going the same way." : str.contains(body, "INS") ? "INS = inside bar break: price paused inside the prior bar, then picked a side." : str.contains(body, "EXP") ? "EXP = expansion: the range is getting wider, as with an outside bar." : "OUT = outside bar: both sides of the prior bar taken."
        out := tf + " level (" + px + "). " + word + " The arrow shows the direction a break of this level points."
    else
        bool lastUp = str.endswith(body, "2u") or str.endswith(body, "2u HAM") or str.endswith(body, "2u SHO")
        bool lastDn = str.endswith(body, "2d") or str.endswith(body, "2d HAM") or str.endswith(body, "2d SHO")
        bool last3 = str.contains(body, "3u") or str.contains(body, "3d")
        if (lastUp and side == 1) or (lastDn and side == -1)
            out := tf + " " + body + " (" + px + "): in force. The " + tf + " bar traded " + (side == 1 ? "above the prior bar's high" : "below the prior bar's low") + ", which put this combo in force: a " + ezComboName(body) + ". It stays in force while price holds " + (side == 1 ? "above" : "below") + " this level and the " + tf + " bar is still open."
        else if lastUp or lastDn
            out := tf + " (" + px + "): the other side of the " + tf + " bar. This bar has already taken the prior " + (lastUp ? "high" : "low") + ". If it also trades " + (side == 1 ? "above" : "below") + " this level it becomes a 3 (outside bar)."
        else if last3
            out := tf + " (" + px + "): the " + tf + " bar is a 3 (outside bar). It has taken both the high and the low of the bar before it, and this is the " + (side == 1 ? "high" : "low") + " it took."
        else
            out := tf + " level (" + px + ")."
    if str.contains(body, "HAM")
        out := out + " HAM: the prior bar is a hammer (long lower wick, closed near its high), which adds weight to the upside."
    if str.contains(body, "SHO")
        out := out + " SHO: the prior bar is a shooter (long upper wick, closed near its low), which adds weight to the downside."
    out

// Simple table: a prior bar's type without the open-side letter (1u -> 1, 3d -> 3).
ezPlain(string t) =>
    str.startswith(t, "1") ? "1" : str.startswith(t, "3") ? "3" : t

// Hover text for a whole label. Labels at the same price are joined with " + ".
ezLabelTip(string txt, float price, int side) =>
    string px = str.tostring(price, format.mintick)
    string out = ""
    for piece in str.split(txt, " + ")
        out := out + (out == "" ? "" : "\\n\\n") + ezPieceTip(piece, px, side)
    out

'''


# ---------------------------------------------------------------------------
# Exact-text patches. (anchor, replacement). Each anchor must occur exactly once.
# ---------------------------------------------------------------------------

PATCHES: list[tuple[str, str]] = [
    # Helpers go in just before SECTION 8.
    ("// ============================================================================\n"
     "// SECTION 8: LABEL COLLECTION & CONSOLIDATION\n",
     EZ_HELPERS +
     "// ============================================================================\n"
     "// SECTION 8: LABEL COLLECTION & CONSOLIDATION\n"),

    # The Labels input uses plain option names.
    ('simpleLabelMode = labelStyleMode == "Universal"', 'simpleLabelMode = labelStyleMode == "Plain Words"'),

    # Level labels carry hover text.
    ("acquireLabel(pool, int x, float y, string txt, bool useBarIndex, color bg, color txtCol, sz) =>",
     "acquireLabel(pool, int x, float y, string txt, bool useBarIndex, color bg, color txtCol, sz, string tip) =>"),
    ("        array.push(pool.items, lbl)\n    pool.used += 1\n    lbl\n",
     "        array.push(pool.items, lbl)\n    label.set_tooltip(lbl, tip)  // EZ-MODE\n    pool.used += 1\n    lbl\n"),
    ("consolidateAndCreate(labelData, pool) =>",
     "consolidateAndCreate(labelData, pool, int ezSide) =>"),
    ("            labelTextColor = readableOn(labelColor, labelBackgroundColor)  // FIX LABELTEXT-1\n",
     "            labelTextColor = readableOn(labelColor, labelBackgroundColor)  // FIX LABELTEXT-1\n"
     "            string ezTip = ezLabelTip(labelText, price, ezSide)  // EZ-MODE\n"),
    ("displayText, false, labelBgColor, labelTextColor, lblSize)",
     "displayText, false, labelBgColor, labelTextColor, lblSize, ezTip)"),
    ("displayText, true, labelBgColor, labelTextColor, lblSize)",
     "displayText, true, labelBgColor, labelTextColor, lblSize, ezTip)"),
    ("    consolidateAndCreate(highLabels, lblPool)\n"
     "    consolidateAndCreate(lowLabels, lblPool)\n"
     "    consolidateAndCreate(openLabels, lblPool)\n",
     "    consolidateAndCreate(highLabels, lblPool, 1)\n"
     "    consolidateAndCreate(lowLabels, lblPool, -1)\n"
     "    consolidateAndCreate(openLabels, lblPool, 0)\n"),

    # Data table: a lower-than-chart row explains why it is blank.
    ("        table.cell(tbl, 0, row, tfLabelStr, text_color = #555555, bgcolor = #111111, text_size = sz)\n",
     "        table.cell(tbl, 0, row, tfLabelStr, text_color = #555555, bgcolor = #111111, text_size = sz, tooltip = tfLabelStr + \" is a lower timeframe than this chart, so it cannot be read here. Switch the chart to \" + tfLabelStr + \" or lower to see it.\")\n"),

    ("        table.cell(tbl, 4, row, \"\", text_color = color.gray, bgcolor = COLOR_BG_BLACK, text_size = sz)\n",
     "        table.cell(tbl, 4, row, \"\", text_color = color.gray, bgcolor = COLOR_BG_BLACK, text_size = sz)\n"
     "        if ezSimpleTable  // EZ-MODE: the Simple table has four columns\n"
     "            table.clear(tbl, 4, row, 4, row)\n"),

    # Data table: every cell in a timeframe row carries a live read.
    ("        table.cell(tbl, 0, row, tfLabelStr, text_color = tfTextColor, bgcolor = tfBgColor, text_size = sz)\n"
     "        table.cell(tbl, 1, row, barType_C2, text_color = getBarTypeTextColor(barType_C2), bgcolor = COLOR_BG_BLACK, text_size = sz)\n"
     "        table.cell(tbl, 2, row, barType_C1, text_color = getBarTypeTextColor(barType_C1), bgcolor = COLOR_BG_BLACK, text_size = sz)\n",
     "        // EZ-MODE: hover text for the row.\n"
     "        string ezTfTip = \"Nothing is in force on \" + tfLabelStr + \" right now. This cell lights up when a signal triggers.\"\n"
     "        if isBullInForce\n"
     "            ezTfTip := tfLabelStr + \" has a signal in force to the upside: it triggered, and price is holding above its trigger (\" + str.tostring(pResult.high, format.mintick) + \"). A signal stays in force only while its bar is open.\"\n"
     "        else if isBearInForce\n"
     "            ezTfTip := tfLabelStr + \" has a signal in force to the downside: it triggered, and price is holding below its trigger (\" + str.tostring(pResult.low, format.mintick) + \"). A signal stays in force only while its bar is open.\"\n"
     "        table.cell(tbl, 0, row, tfLabelStr, text_color = tfTextColor, bgcolor = tfBgColor, text_size = sz, tooltip = ezTfTip)\n"
     "        table.cell(tbl, 1, row, barType_C2, text_color = getBarTypeTextColor(barType_C2), bgcolor = COLOR_BG_BLACK, text_size = sz, tooltip = \"C2, two bars back. \" + ezBarTip(barType_C2, tfLabelStr, false))\n"
     "        table.cell(tbl, 2, row, barType_C1, text_color = getBarTypeTextColor(barType_C1), bgcolor = COLOR_BG_BLACK, text_size = sz, tooltip = \"C1, the previous bar. \" + ezBarTip(barType_C1, tfLabelStr, false))\n"),
    ("        table.cell(tbl, 3, row, asText, text_color = color.white, bgcolor = COLOR_BG_BLACK, text_size = sz)\n",
     "        string ezAsTip = data.c1IsHammer ? \"HAM = hammer. The previous \" + tfLabelStr + \" bar has a long lower wick and closed near its high: sellers pushed down and lost. A break above its high is the trigger.\" : data.c1IsShooter ? \"SHO = shooter. The previous \" + tfLabelStr + \" bar has a long upper wick and closed near its low: buyers pushed up and lost. A break below its low is the trigger.\" : data.c1IsInside ? \"INS = inside bar. The previous \" + tfLabelStr + \" bar stayed inside the bar before it. A break of its high or its low is the trigger.\" : \"No actionable signal on the previous \" + tfLabelStr + \" bar. This column shows HAM (hammer), SHO (shooter) or INS (inside bar) when there is one.\"\n"
     "        table.cell(tbl, 3, row, asText, text_color = color.white, bgcolor = COLOR_BG_BLACK, text_size = sz, tooltip = ezAsTip)\n"),
    ("        table.cell(tbl, 4, row, ccDisplayText, text_color = ccTextColor, bgcolor = ccBgColor, text_size = sz)\n",
     "        table.cell(tbl, 4, row, ccDisplayText, text_color = ccTextColor, bgcolor = ccBgColor, text_size = sz, tooltip = ccUnknown ? \"Preview Mode: the next \" + tfLabelStr + \" bar has not started trading, so there is nothing to read yet.\" : \"CC, the current bar. \" + ezBarTip(barType_CC, tfLabelStr, true))\n"
     "        if ezSimpleTable\n"
     "            // EZ-MODE: Simple table. The combo reads across two cells that sit flush: the bars\n"
     "            // leading in (right-aligned, ending in a dash) and the current bar (left-aligned,\n"
     "            // in its bar color). A hammer or shooter on C1 goes in its own Prior column so it\n"
     "            // does not split the combo.\n"
     "            // C2 is named only when it is part of the combo: an inside C1 (2-1-2, 3-1-2), or\n"
     "            // a 1 or 3 ahead of a 2 (1-2-2, 3-2-2), the same rule the level labels use.\n"
     "            string ezC1 = ezPlain(barType_C1)\n"
     "            string ezC2 = ezPlain(barType_C2)\n"
     "            bool ezC1Is2 = str.contains(ezC1, \"2\")\n"
     "            bool ezNeedC2 = ezC2 != \"\" and (ezC1 == \"1\" or (ezC1Is2 and (ezC2 == \"1\" or ezC2 == \"3\")))\n"
     "            string ezBefore = (ezNeedC2 ? ezC2 + \"-\" : \"\") + ezC1\n"
     "            string ezShape = data.c1IsHammer ? \"Hammer\" : data.c1IsShooter ? \"Shooter\" : \"\"\n"
     "            string ezNow = ccUnknown ? \"?\" : ezPlain(barType_CC)\n"
     "            string ezName = ezComboName(ezBefore + ezNow)\n"
     "            string ezCombo = ezBefore + \"-\" + ezNow\n"
     "            string ezRead = \"The \" + tfLabelStr + \" combo is \" + ezCombo + \". Read it left to right, oldest bar first. The highlighted bar at the end is the current one, still forming.\" + (ezNow == \"1\" ? \" It is waiting on the current bar to break one side.\" : ezName != \"combo\" ? \" That is a \" + ezName + \".\" : \"\")\n"
     "            string ezBeforeTip = ezRead + \" The bar before the current one: \" + ezBarTip(barType_C1, tfLabelStr, false)\n"
     "            string ezShapeTip = data.c1IsHammer ? \"The \" + tfLabelStr + \" bar before the current one is a hammer: a long lower wick and a close near its high. Sellers pushed down and lost. A break above its high is the trigger.\" : data.c1IsShooter ? \"The \" + tfLabelStr + \" bar before the current one is a shooter: a long upper wick and a close near its low. Buyers pushed up and lost. A break below its low is the trigger.\" : \"Shows Hammer or Shooter when the \" + tfLabelStr + \" bar before the current one is one. Blank otherwise.\"\n"
     "            string ezNowTip = ccUnknown ? \"Preview Mode: the next \" + tfLabelStr + \" bar has not started trading, so there is nothing to read yet.\" : ezRead + \" The current bar: \" + ezBarTip(barType_CC, tfLabelStr, true)\n"
     "            table.clear(tbl, 1, row, 4, row)\n"
     "            table.cell(tbl, 1, row, ezBefore + \"-\", text_color = #b2b5be, bgcolor = COLOR_BG_BLACK, text_size = sz, text_halign = text.align_right, tooltip = ezBeforeTip)\n"
     "            table.cell(tbl, 2, row, ezNow, text_color = ccTextColor, bgcolor = ccBgColor, text_size = sz, text_halign = text.align_left, tooltip = ezNowTip)\n"
     "            table.cell(tbl, 3, row, ezShape, text_color = color.white, bgcolor = COLOR_BG_BLACK, text_size = sz, tooltip = ezShapeTip)\n"),

    # Data table: header glossary.
    ('        table.cell(barTypeTable, 0, 0, "TF", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize)\n'
     '        table.cell(barTypeTable, 1, 0, "C2", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize)\n'
     '        table.cell(barTypeTable, 2, 0, "C1", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize)\n'
     '        table.cell(barTypeTable, 3, 0, "AS", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize)\n'
     '        table.cell(barTypeTable, 4, 0, "CC", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize)\n',
     '        table.cell(barTypeTable, 0, 0, "TF", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize, tooltip = "TF = timeframe. Each row is one timeframe, highest at the top. The cell lights up while a signal is in force on that timeframe. Hover any cell for what it means right now. TheStrat was created by Rob Smith.")\n'
     '        table.cell(barTypeTable, 1, 0, "C2", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize, tooltip = "C2 = the candle two bars back on each timeframe. Every bar is a 1 (took neither side of the bar before it), a 2 (took one side) or a 3 (took both).")\n'
     '        table.cell(barTypeTable, 2, 0, "C1", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize, tooltip = "C1 = the previous candle on each timeframe. Its high and low are the triggers the current bar can break.")\n'
     '        table.cell(barTypeTable, 3, 0, "AS", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize, tooltip = "AS = actionable signal on the previous candle: HAM (hammer), SHO (shooter) or INS (inside bar). Blank when there is none.")\n'
     '        table.cell(barTypeTable, 4, 0, "CC", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize, tooltip = "CC = the current candle on each timeframe, still forming. Read C2, C1, CC left to right to get the combo, for example 2d-2u.")\n'
     '        if ezSimpleTable\n'
     '            table.clear(barTypeTable, 1, 0, 4, 0)\n'
     '            table.merge_cells(barTypeTable, 1, 0, 2, 0)\n'
     '            table.cell(barTypeTable, 1, 0, "Combo", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize, tooltip = "Combo = the last few bars on each timeframe, oldest first. The highlighted bar at the end is the current one, still forming. Every bar is a 1 (took neither side of the bar before it), a 2 (2u took the high, 2d took the low) or a 3 (took both). So 3-1-2u is an outside bar, an inside bar, then the current bar breaking up. Hover a row for its read.")\n'
     '            table.cell(barTypeTable, 3, 0, "Prior", text_color = #888888, bgcolor = #1a1a1a, text_size = textSize, tooltip = "Prior = the shape of the bar before the current one, when it matters: Hammer (long lower wick, closed near its high) or Shooter (long upper wick, closed near its low). Blank otherwise.")\n'
     '        string ezUp = ""\n'
     '        string ezDn = ""\n'
     '        string ezIn = ""\n'),

    # Explain row: collect what each timeframe is doing while the rows are drawn.
    ("                populateBarTypeRow(barTypeTable, row, cfg.tf, data, raw, result, tableTextSize, array.get(reconOKArr, i))\n"
     "                row += 1\n",
     "                populateBarTypeRow(barTypeTable, row, cfg.tf, data, raw, result, tableTextSize, array.get(reconOKArr, i))\n"
     "                row += 1\n"
     "                // EZ-MODE: rows run highest to lowest, so prepend to list lowest first.\n"
     "                bool ezReadable = not na(result) and not na(raw) and timeframe.in_seconds(cfg.tf) >= chartTfSeconds and not (raw.isPreview and not array.get(reconOKArr, i))\n"
     "                if ezReadable\n"
     "                    string ezLbl = getTimeframeLabel(cfg.tf)\n"
     "                    if result.signalInForceHigh\n"
     "                        ezUp := ezUp == \"\" ? ezLbl : ezLbl + \", \" + ezUp\n"
     "                    else if result.signalInForceLow\n"
     "                        ezDn := ezDn == \"\" ? ezLbl : ezLbl + \", \" + ezDn\n"
     "                    else if str.startswith(result.ccType, \"1\")\n"
     "                        ezIn := ezIn == \"\" ? ezLbl : ezLbl + \", \" + ezIn\n"),

    # FTFC row hover, then the Explain row.
    ("        table.merge_cells(barTypeTable, 0, row, 4, row)\n"
     "        table.cell(barTypeTable, 0, row, tableFtfcText, text_color = tableFtfcTextColor, bgcolor = tableFtfcBgColor, text_size = textSize)\n"
     "        row += 1\n",
     "        table.merge_cells(barTypeTable, 0, row, 4, row)\n"
     "        table.cell(barTypeTable, 0, row, tableFtfcText, text_color = tableFtfcTextColor, bgcolor = tableFtfcBgColor, text_size = textSize, tooltip = \"Full Timeframe Continuity (FTFC) compares the current price with the open of every timeframe in the table. Up: price is above all of them. Down: below all of them. Conflict: the timeframes disagree. Signals in the direction of continuity have the higher timeframes behind them.\")\n"
     "        row += 1\n"
     "        if ezExplain  // EZ-MODE\n"
     "            string ezLean = str.contains(tableFtfcText, \"Up\") ? \"Price is above every open.\\nThe timeframes agree: up.\" : str.contains(tableFtfcText, \"Down\") ? \"Price is below every open.\\nThe timeframes agree: down.\" : \"The timeframes disagree:\\nsome are up, some are down.\"\n"
     "            string ezForce = (ezUp != \"\" ? \"\\nIn force up: \" + ezUp : \"\") + (ezDn != \"\" ? \"\\nIn force down: \" + ezDn : \"\")\n"
     "            if ezForce == \"\"\n"
     "                ezForce := \"\\nNo signal in force yet.\"\n"
     "            string ezWait = ezIn != \"\" ? \"\\nInside, waiting to break: \" + ezIn : \"\"\n"
     "            table.merge_cells(barTypeTable, 0, row, 4, row)\n"
     "            table.cell(barTypeTable, 0, row, ezLean + ezForce + ezWait, text_color = #cccccc, bgcolor = #111111, text_size = textSize, text_halign = text.align_left, tooltip = \"A plain-English read of the table. In force: a signal has triggered and price is holding beyond its trigger. Inside: the bar has not taken either side of the bar before it yet. Turn this row off in the settings (Explain Row) once you no longer need it. TheStrat was created by Rob Smith.\")\n"
     "            row += 1\n"),

    # Preview row hover.
    ("            table.merge_cells(barTypeTable, 0, row, 4, row)\n"
     "            table.cell(barTypeTable, 0, row, \"PREVIEW MODE\", text_color = color.yellow, bgcolor = color.new(color.yellow, 85), text_size = textSize)\n",
     "            table.merge_cells(barTypeTable, 0, row, 4, row)\n"
     "            table.cell(barTypeTable, 0, row, \"PREVIEW MODE\", text_color = color.yellow, bgcolor = color.new(color.yellow, 85), text_size = textSize, tooltip = \"The market is closed, so the chart is showing the levels for the next bar to help you plan. A ? means that bar has not started trading. Set Preview Mode to Off for Bar Replay.\")\n"),
]

HEADER = f'''//@version=6
// {TITLE} - v{EZ_VERSION}
//
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// GENERATED FILE. Do not edit by hand. Built by ez-mode/build_ez.py from TheStrat Suite
// ({{source}}), so the engine is the Suite's engine. Fix the Suite, then rebuild.
//
// TheStrat was created by Rob Smith. This script is a learning tool for his method:
// every candle is a 1 (took neither side of the bar before it), a 2 (took one side) or a
// 3 (took both), read across four timeframes at once.
//
// What EZ Mode is: TheStrat Suite with the settings reduced to a handful, plus hover text
// that explains what you are looking at.
//   - Four timeframes, picked by one Timeframes setting (default: 60-minute, Day, Week, Month)
//   - Reversals and inside bar breaks always on; three optional signals, no filters
//   - Magnitude and exhaustion targets, Take Action Windows, stops, Preview Mode
//   - Bar numbers on the chart candles
//   - Data table: hover any cell or level label for a plain-English read
//   - Explain row: a one-glance summary of the table
//   - A Failing 2 is judged against the bar's own open: a 2u trading red, a 2d trading green
//
// Want the filters, colors, six timeframes and detailed alerts? Use TheStrat Suite.
// Documentation: thestratsuite.com

indicator("{TITLE}", shorttitle = "TheStrat Suite EZ", overlay = true, max_labels_count = 200, max_lines_count = 200, max_boxes_count = 200, max_bars_back = 200)
'''


def build(source: Path) -> str:
    text = source.read_text(encoding="utf-8")
    lines = text.split("\n")

    # Header: everything up to and including the indicator() call is replaced.
    ind = [i for i, ln in enumerate(lines) if ln.startswith("indicator(")]
    if len(ind) != 1:
        raise SystemExit("expected exactly one indicator() line")
    try:
        shown = source.resolve().relative_to(ROOT)
    except ValueError:
        shown = source.name
    body = lines[ind[0] + 1:]

    out: list[str] = []
    seen: set[str] = set()
    fixed = 0
    for ln in body:
        if DROPPED_ALERTS.match(ln):
            continue
        m = INPUT_RE.match(ln)
        if not m:
            out.append(ln)
            continue
        name = m.group(1)
        seen.add(name)
        if name in OVERRIDES:
            out.append(OVERRIDES[name])
        else:
            out.append(f"{name} = {first_arg(m.group(3))}")
            fixed += 1

    missing = set(OVERRIDES) - seen
    if missing:
        raise SystemExit(f"override names not found in the Suite source: {sorted(missing)}")

    result = HEADER.format(source=shown) + "\n".join(out)
    for anchor, replacement in PATCHES:
        n = result.count(anchor)
        if n != 1:
            raise SystemExit(f"patch anchor matched {n} times (expected 1):\n{anchor[:200]}")
        result = result.replace(anchor, replacement)

    # The Simple table is three columns wide, so full-width rows merge to ezLastCol.
    wide = "table.merge_cells(barTypeTable, 0, row, 4, row)"
    if result.count(wide) != 5:
        raise SystemExit(f"expected 5 full-width table rows, found {result.count(wide)}")
    result = result.replace(wide, "table.merge_cells(barTypeTable, 0, row, ezLastCol, row)")

    inputs = len(re.findall(r"= input\.\w+\(", result))
    print(f"source: {shown}  fixed inputs: {fixed}  EZ inputs: {inputs}", file=sys.stderr)
    return result


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    ap.add_argument("--out", type=Path, default=OUTPUT)
    ap.add_argument("--check", action="store_true", help="fail if the output file is stale")
    args = ap.parse_args()

    result = build(args.source)
    if args.check:
        current = args.out.read_text(encoding="utf-8") if args.out.exists() else ""
        if current != result:
            raise SystemExit(f"{args.out} is stale: rerun ez-mode/build_ez.py")
        print("up to date", file=sys.stderr)
        return
    args.out.write_text(result, encoding="utf-8")
    print(f"wrote {args.out}", file=sys.stderr)


if __name__ == "__main__":
    main()
