// TheStrat Suite for NinjaTrader 8 - v0.2.0 (port of TheStrat Suite v3.1.1 for TradingView)
//
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Multi-timeframe price action indicator implementing TheStrat methodology: six configurable
// timeframes, bar classification, signal detection with magnitude and exhaustion targets,
// Full Timeframe Continuity, stops, Take Action Windows, Domino detection, alerts, a
// multi-timeframe data table and Strat / FTFC bar coloring.
//
// The decision logic lives in TheStratSuiteEngine.cs (a line-for-line port of the Pine).
// This file is the NinjaTrader side: higher-timeframe data, the as-of forming candle,
// preview mode, rendering and alerts. How it maps to the Pine, and where NinjaTrader
// differs, is in ninjatrader/README.md in the TheStrat Suite repository.
//
// For setup guides and documentation, visit: thestratsuite.com

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.StratSuiteCore;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.StratSuiteCore
{
    // Shows an enum's [Description] in the property grid (the Pine option names). This follows
    // NinjaTrader's friendly-enum sample: the standard values are the display strings, and the
    // property also carries the StringStandardValuesEditorKey editor so the current value shows
    // selected. Each enum gets a non-generic subclass, so the attribute names a plain type that
    // NinjaTrader can resolve after a recompile.
    public class FriendlyEnumConverter<T> : TypeConverter
    {
        static string Describe(object value)
        {
            System.Reflection.FieldInfo fi = typeof(T).GetField(value.ToString());
            if (fi != null)
            {
                object[] attrs = fi.GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (attrs.Length > 0)
                    return ((DescriptionAttribute)attrs[0]).Description;
            }
            return value.ToString();
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            List<string> values = new List<string>();
            foreach (object v in Enum.GetValues(typeof(T)))
                values.Add(Describe(v));
            return new StandardValuesCollection(values);
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) { return true; }
        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) { return true; }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is T) return value;
            string text = value == null ? "" : value.ToString();
            foreach (object v in Enum.GetValues(typeof(T)))
                if (Describe(v) == text || v.ToString() == text)
                    return v;
            return Enum.GetValues(typeof(T)).GetValue(0);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return value == null ? string.Empty : Describe(ConvertFrom(context, culture, value));
            if (destinationType.IsAssignableFrom(typeof(T)))
                return ConvertFrom(context, culture, value);
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }

    public class PresetConverter : FriendlyEnumConverter<StratPreset> { }
    public class TimeframeConverter : FriendlyEnumConverter<StratTimeframe> { }
    public class LabelStyleConverter : FriendlyEnumConverter<StratLabelStyle> { }
    public class HammerDefinitionConverter : FriendlyEnumConverter<HammerDefinition> { }
    public class F2MethodConverter : FriendlyEnumConverter<F2Method> { }
    public class StopReferenceConverter : FriendlyEnumConverter<StopReferenceMode> { }
    public class StopColorConverter : FriendlyEnumConverter<StopColorMode> { }
    public class PreviewModeConverter : FriendlyEnumConverter<PreviewModeOption> { }
    public class TableModeConverter : FriendlyEnumConverter<TableModeOption> { }
    public class CompactCellColorConverter : FriendlyEnumConverter<CompactCellColorMode> { }
    public class TablePositionConverter : FriendlyEnumConverter<TablePositionOption> { }
    public class TextSizeConverter : FriendlyEnumConverter<TextSizeOption> { }
    public class BarColorModeConverter : FriendlyEnumConverter<BarColorModeOption> { }
    public class BarNumNotationConverter : FriendlyEnumConverter<BarNumNotationOption> { }
    public class BarNumEmphasisConverter : FriendlyEnumConverter<BarNumEmphasisOption> { }
    public class BarNumPositionConverter : FriendlyEnumConverter<BarNumPositionOption> { }
    public class BarNumColorModeConverter : FriendlyEnumConverter<BarNumColorModeOption> { }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TheStratSuite : Indicator
    {
        public const string Version = "0.2.0";

        // ====================================================================
        // HIGHER-TIMEFRAME SOURCES
        // ====================================================================
        // A source answers "which HTF candle holds this chart bar" and serves completed candles
        // by index (oldest first). Intraday slots read a minute series directly. D, W, M, 3M, 6M
        // and 12M are all built from one daily series keyed by trade date, so every calendar
        // slot uses the same trade-date calendar (the Pine's +12h normalization problem does
        // not arise) and quarters, halves and years are exact calendar periods.

        private abstract class HtfSource
        {
            public abstract int Count { get; }
            public abstract double O(int i);
            public abstract double H(int i);
            public abstract double L(int i);
            public abstract double C(int i);
            public abstract long Key(int i);
            public virtual void Refresh() { }
            // Index of the candle holding the chart bar; exact = false when the chart bar sits
            // outside every HTF candle (session gap, trading-hours mismatch) and the most recent
            // completed candle is returned instead. -1 = no data. Count (one past the last candle)
            // means the chart bar opens a candle the HTF series has not printed yet (its first
            // tick reached the chart series first); the indicator then builds it from chart bars.
            public abstract int IndexFor(DateTime barTime, DateTime tradingDate, out bool exact, out long key);
            protected long VirtualKey;
        }

        private sealed class MinuteSource : HtfSource
        {
            readonly Bars bars;
            readonly int minutes;
            readonly SessionIterator sessions;
            public MinuteSource(Bars b, int m, SessionIterator s) { bars = b; minutes = m; sessions = s; }
            public override int Count { get { return bars == null ? 0 : bars.Count; } }
            public override double O(int i) { return i >= 0 && i < Count ? bars.GetOpen(i) : double.NaN; }
            public override double H(int i) { return i >= 0 && i < Count ? bars.GetHigh(i) : double.NaN; }
            public override double L(int i) { return i >= 0 && i < Count ? bars.GetLow(i) : double.NaN; }
            public override double C(int i) { return i >= 0 && i < Count ? bars.GetClose(i) : double.NaN; }
            public override long Key(int i) { return i == Count ? VirtualKey : bars.GetTime(i).Ticks; }
            public DateTime End(int i) { return new DateTime(Key(i)); }

            public override int IndexFor(DateTime barTime, DateTime tradingDate, out bool exact, out long key)
            {
                exact = false;
                key = 0;
                int n = Count;
                if (n == 0) return -1;
                int lo = 0, hi = n - 1, found = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (bars.GetTime(mid) >= barTime) { found = mid; hi = mid - 1; }
                    else lo = mid + 1;
                }
                if (found == -1)
                {
                    // The candle after the last printed one. Its end follows NinjaTrader's minute
                    // aggregation: session-anchored, cut at the session end.
                    DateTime lastEnd = bars.GetTime(n - 1);
                    sessions.GetNextSession(barTime, true);
                    DateTime anchor = lastEnd >= sessions.ActualSessionBegin ? lastEnd : sessions.ActualSessionBegin;
                    DateTime end = anchor.AddMinutes(Math.Ceiling((barTime - anchor).TotalMinutes / minutes) * minutes);
                    if (end > sessions.ActualSessionEnd) end = sessions.ActualSessionEnd;
                    exact = true;
                    VirtualKey = key = end.Ticks;
                    return n;
                }
                // Contained only if the chart bar ends after this candle's nominal start.
                if (barTime > bars.GetTime(found).AddMinutes(-minutes))
                {
                    exact = true;
                    key = Key(found);
                    return found;
                }
                if (found > 0) key = Key(found - 1);
                return found - 1;           // in a gap before this candle: the prior one is current
            }
        }

        private sealed class DailyAggSource : HtfSource
        {
            readonly Bars day;
            readonly StratTimeframe tf;
            readonly List<long> keys = new List<long>();
            readonly List<int> first = new List<int>();
            readonly List<double> o = new List<double>(), h = new List<double>(), l = new List<double>(), c = new List<double>();
            int processed;

            public DailyAggSource(Bars d, StratTimeframe t) { day = d; tf = t; }
            public override int Count { get { return keys.Count; } }
            public override double O(int i) { return i >= 0 && i < Count ? o[i] : double.NaN; }
            public override double H(int i) { return i >= 0 && i < Count ? h[i] : double.NaN; }
            public override double L(int i) { return i >= 0 && i < Count ? l[i] : double.NaN; }
            public override double C(int i) { return i >= 0 && i < Count ? c[i] : double.NaN; }
            public override long Key(int i) { return i == Count ? VirtualKey : keys[i]; }

            public override void Refresh()
            {
                if (day == null) return;
                int n = day.Count;
                if (n < processed)
                {
                    // The series shrank (a data reload): start over rather than read past its end.
                    keys.Clear(); first.Clear(); o.Clear(); h.Clear(); l.Clear(); c.Clear();
                    processed = 0;
                    rebuiltAll = false;
                }
                // Groups are appended as daily bars arrive; the newest group is always recomputed
                // because its last daily bar is still forming in realtime.
                while (processed < n)
                {
                    long k = StratTimeframes.PeriodKey(tf, day.GetTime(processed).Date);
                    if (keys.Count == 0 || keys[keys.Count - 1] != k)
                    {
                        keys.Add(k); first.Add(processed);
                        o.Add(double.NaN); h.Add(double.NaN); l.Add(double.NaN); c.Add(double.NaN);
                    }
                    processed++;
                }
                // Recompute the last two groups (the newest can have been re-opened by a late bar).
                for (int g = Math.Max(0, keys.Count - 2); g < keys.Count; g++)
                    Rebuild(g, n);
                if (rebuiltAll == false)
                {
                    for (int g = 0; g < keys.Count - 2; g++) Rebuild(g, n);
                    rebuiltAll = true;
                }
            }
            bool rebuiltAll;

            void Rebuild(int g, int n)
            {
                int start = first[g];
                int end = g + 1 < keys.Count ? first[g + 1] : n;
                double hh = double.MinValue, ll = double.MaxValue;
                for (int i = start; i < end; i++)
                {
                    hh = Math.Max(hh, day.GetHigh(i));
                    ll = Math.Min(ll, day.GetLow(i));
                }
                o[g] = day.GetOpen(start);
                h[g] = hh;
                l[g] = ll;
                c[g] = day.GetClose(end - 1);
            }

            // High and low of group g's daily bars dated before a trade date.
            public bool RangeBefore(int g, DateTime tradingDate, out double hh, out double ll)
            {
                hh = double.MinValue;
                ll = double.MaxValue;
                if (day == null || g < 0 || g >= keys.Count) return false;
                int end = g + 1 < keys.Count ? first[g + 1] : day.Count;
                bool any = false;
                for (int i = first[g]; i < end && day.GetTime(i).Date < tradingDate.Date; i++)
                {
                    hh = Math.Max(hh, day.GetHigh(i));
                    ll = Math.Min(ll, day.GetLow(i));
                    any = true;
                }
                return any;
            }

            public override int IndexFor(DateTime barTime, DateTime tradingDate, out bool exact, out long key)
            {
                exact = false;
                key = 0;
                int n = keys.Count;
                if (n == 0) return -1;
                long k = StratTimeframes.PeriodKey(tf, tradingDate);
                int lo = 0, hi = n - 1, found = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (keys[mid] <= k) { found = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                if (found >= 0 && keys[found] == k)
                    exact = true;
                else if (found == n - 1)
                {
                    exact = true;           // a period the daily series has not printed yet
                    VirtualKey = k;
                    found = n;
                }
                if (found >= 0) key = found == n ? k : keys[found];
                return found;
            }
        }

        private sealed class Slot
        {
            public int Index;
            public bool Enabled;
            public StratTimeframe Tf;
            public int LineWidth;
            public bool ShowOpen;
            public string Label;
            public long Seconds;
            public bool Lower;
            public HtfSource Src;
            public bool Intraday;
            public bool ShortHistory;
            public readonly Dictionary<long, int> StartBars = new Dictionary<long, int>();
            // Exhaustion snapshot cache (a pure function of the HTF candle index).
            public long ExhKey = long.MinValue;
            public double ExhHP = double.NaN, ExhLP = double.NaN;
            public int ExhHIdx = -1, ExhLIdx = -1;
            // Period-end cache for line ends.
            public long EndKey = long.MinValue;
            public DateTime EndTime;
        }

        // Everything the Pine keeps in `var` state, so a realtime tick can roll back to the last
        // closed chart bar exactly as Pine's realtime rollback does.
        private sealed class EvalState
        {
            public SlotState[] Slots = new SlotState[6];
            public bool[] WasSetupBull = new bool[6], WasSetupBear = new bool[6], WasF2Bull = new bool[6], WasF2Bear = new bool[6];
            public bool[] WasPotentialBull = new bool[6], WasPotentialBear = new bool[6];
            public long[] AlertLastPeriod = new long[6];
            public bool[] AlertHasPeriod = new bool[6];
            public bool WasFtfcUp, WasFtfcDown;
            public string LastDominoCombo = "";
            public bool WasPaintFtfcUp, WasPaintFtfcDown;

            public EvalState()
            {
                for (int i = 0; i < 6; i++) Slots[i] = new SlotState();
            }

            public EvalState Clone()
            {
                EvalState e = (EvalState)MemberwiseClone();
                e.Slots = new SlotState[6];
                for (int i = 0; i < 6; i++) e.Slots[i] = Slots[i].Clone();
                e.WasSetupBull = (bool[])WasSetupBull.Clone(); e.WasSetupBear = (bool[])WasSetupBear.Clone();
                e.WasF2Bull = (bool[])WasF2Bull.Clone(); e.WasF2Bear = (bool[])WasF2Bear.Clone();
                e.WasPotentialBull = (bool[])WasPotentialBull.Clone(); e.WasPotentialBear = (bool[])WasPotentialBear.Clone();
                e.AlertLastPeriod = (long[])AlertLastPeriod.Clone(); e.AlertHasPeriod = (bool[])AlertHasPeriod.Clone();
                return e;
            }
        }

        // ====================================================================
        // RENDER MODEL (built on the last bar, drawn in OnRender)
        // ====================================================================

        private sealed class RLine { public double X1, X2, Price; public SColor Color; public int Width; public LineStyleCode Style; }
        private sealed class RBox { public double X1, X2, Top, Bottom; public SColor Fill, Border; }
        private sealed class RLabel { public double X; public double Price; public string Text; public SColor Bg, Fg; }
        private sealed class RCell { public string Text = ""; public SColor Fg, Bg; }
        private sealed class RTable
        {
            public int Columns;
            public List<RCell[]> Rows = new List<RCell[]>();      // a 1-element row spans all columns
            public TablePositionOption Position;
            public TextSizeOption Size;
        }
        private sealed class RenderModel
        {
            public int LastBar;
            public List<RLine> Lines = new List<RLine>();
            public List<RBox> Boxes = new List<RBox>();
            public List<RLabel> Labels = new List<RLabel>();
            public RTable Table;
            public RTable Debug;
        }

        private StratSettings settings;
        private Slot[] slots;
        private EvalState committed, working;
        private int workingBar = -1;
        private SessionIterator sessionIt, endIt, clockIt;
        private DateTime cachedSessionBegin = DateTime.MinValue, cachedSessionEnd = DateTime.MinValue, cachedTradingDay;
        private long chartSeconds;
        private bool chartDayBased, chartIntradayTime;
        private StratTimeframe chartAsTf;
        private BarsPeriodType chartType;     // Heiken-Ashi resolved to its base period
        private int chartValue;
        private bool chartSynthetic;          // chart prices are computed (Heiken-Ashi), not traded
        private readonly Dictionary<string, int> alertFiredBar = new Dictionary<string, int>();
        private RenderModel model;
        private volatile int formingBar = -1;
        private DispatcherTimer clockTimer;
        private bool clockPass;               // a display-only re-evaluation from the clock
        private readonly Dictionary<int, SolidColorBrush> wpfBrushes = new Dictionary<int, SolidColorBrush>();
        private readonly Dictionary<int, SharpDX.Direct2D1.SolidColorBrush> dxBrushes = new Dictionary<int, SharpDX.Direct2D1.SolidColorBrush>();
        private SharpDX.Direct2D1.StrokeStyle dashStyle, dotStyle;
        private SharpDX.DirectWrite.TextFormat barNumFormat;
        private readonly Dictionary<string, SharpDX.DirectWrite.TextLayout> barNumLayouts = new Dictionary<string, SharpDX.DirectWrite.TextLayout>();

        // ====================================================================
        // STATE
        // ====================================================================

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "TheStrat Suite v3.1.1 for NinjaTrader 8: multi-timeframe Strat bar types, signals, magnitude and exhaustion targets, FTFC, stops, Take Action Windows, Domino, alerts and a data table. Free and open source (MPL-2.0). thestratsuite.com";
                Name = "TheStratSuite";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsAutoScale = false;
                IsSuspendedWhileInactive = false;
                ScaleJustification = ScaleJustification.Right;
                BarsRequiredToPlot = 0;
                SetInputDefaults();

                AddPlot(Brushes.Transparent, "FTFC");
                AddPlot(Brushes.Transparent, "TF1 Signal");
                AddPlot(Brushes.Transparent, "TF2 Signal");
                AddPlot(Brushes.Transparent, "TF3 Signal");
                AddPlot(Brushes.Transparent, "TF4 Signal");
                AddPlot(Brushes.Transparent, "TF5 Signal");
                AddPlot(Brushes.Transparent, "TF6 Signal");
            }
            else if (State == State.Configure)
            {
                BuildSlotsAndSeries();
            }
            else if (State == State.DataLoaded)
            {
                settings = BuildSettings();
                BindSources();
                sessionIt = new SessionIterator(BarsArray[0]);
                endIt = new SessionIterator(BarsArray[0]);
                clockIt = new SessionIterator(BarsArray[0]);
                ClassifyChart();
                CheckHistoryDepth();
                committed = new EvalState();
                working = committed.Clone();
                workingBar = -1;
            }
            else if (State == State.Historical)
            {
                StartClock();
            }
            else if (State == State.Realtime)
            {
                // The last historical bar was evaluated before realtime began, so it could not mark
                // the live candle as forming; with On bar close nothing runs until that candle closes.
                if (settings != null)
                    SetFormingBar(NinjaTrader.Core.Globals.Now);
            }
            else if (State == State.Terminated)
            {
                StopClock();
                DisposeDx();
            }
        }

        // Pine's preset resolution, then one data series per distinct intraday timeframe plus a
        // single daily series for every D-and-up slot.
        private readonly Dictionary<int, int> minuteSeries = new Dictionary<int, int>();
        private int daySeries = -1;
        private int dailyBarsRequested;

        private void BuildSlotsAndSeries()
        {
            StratTimeframes.SlotConfig[] custom = new StratTimeframes.SlotConfig[]
            {
                new StratTimeframes.SlotConfig { Enabled = Tf1Enabled, Tf = Tf1, LineWidth = Tf1LineWidth, ShowOpen = Tf1ShowOpen },
                new StratTimeframes.SlotConfig { Enabled = Tf2Enabled, Tf = Tf2, LineWidth = Tf2LineWidth, ShowOpen = Tf2ShowOpen },
                new StratTimeframes.SlotConfig { Enabled = Tf3Enabled, Tf = Tf3, LineWidth = Tf3LineWidth, ShowOpen = Tf3ShowOpen },
                new StratTimeframes.SlotConfig { Enabled = Tf4Enabled, Tf = Tf4, LineWidth = Tf4LineWidth, ShowOpen = Tf4ShowOpen },
                new StratTimeframes.SlotConfig { Enabled = Tf5Enabled, Tf = Tf5, LineWidth = Tf5LineWidth, ShowOpen = Tf5ShowOpen },
                new StratTimeframes.SlotConfig { Enabled = Tf6Enabled, Tf = Tf6, LineWidth = Tf6LineWidth, ShowOpen = Tf6ShowOpen },
            };
            StratTimeframes.SlotConfig[] r = StratTimeframes.Resolve(Preset, custom);
            slots = new Slot[6];
            minuteSeries.Clear();
            daySeries = -1;
            int nextIndex = 1;
            int dailyBars = 0;
            string th = string.IsNullOrWhiteSpace(HtfTradingHours) ? null : HtfTradingHours.Trim();
            string instrument = Instrument != null ? Instrument.FullName : null;
            Dictionary<int, int> minuteBars = new Dictionary<int, int>();
            for (int i = 0; i < 6; i++)
            {
                Slot s = new Slot();
                s.Index = i;
                s.Enabled = r[i].Enabled;
                s.Tf = r[i].Tf;
                s.LineWidth = Math.Max(1, Math.Min(5, r[i].LineWidth));
                s.ShowOpen = r[i].ShowOpen;
                s.Label = StratTimeframes.Label(s.Tf);
                s.Seconds = StratTimeframes.Seconds(s.Tf);
                s.Intraday = StratTimeframes.IsIntraday(s.Tf);
                slots[i] = s;
                if (!s.Enabled) continue;
                if (s.Intraday)
                {
                    int m = StratTimeframes.Minutes(s.Tf), have;
                    minuteBars[m] = Math.Max(minuteBars.TryGetValue(m, out have) ? have : 0, StratTimeframes.HtfBarsTier(s.Tf));
                }
                else
                {
                    int perBar = DailyPerBar(s.Tf);
                    dailyBars = Math.Max(dailyBars, StratTimeframes.HtfBarsTier(s.Tf) * perBar + perBar);
                }
            }
            // NinjaTrader honors barsToLoad only on the first added series of a bar type, and not at
            // all when that type matches the chart's (minute series on a minute chart load the
            // chart's own range). Adding the longest timeframe first gives it the widest window on
            // tick, Renko and second charts; short history is reported in the table (ShortHistory).
            List<int> mins = new List<int>(minuteBars.Keys);
            mins.Sort();
            mins.Reverse();
            foreach (int m in mins)
            {
                AddDataSeries(instrument, new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = m }, minuteBars[m], th, null);
                minuteSeries[m] = nextIndex++;
            }
            dailyBarsRequested = 0;
            if (dailyBars > 0)
            {
                dailyBarsRequested = Math.Min(Math.Max(dailyBars, 160), MaxDailyBars);
                AddDataSeries(instrument, new BarsPeriod { BarsPeriodType = BarsPeriodType.Day, Value = 1 }, dailyBarsRequested, th, null);
                daySeries = nextIndex++;
            }
        }

        // Trading days in one candle of a D-and-up timeframe, for sizing the daily series.
        private static int DailyPerBar(StratTimeframe tf)
        {
            return tf == StratTimeframe.Day ? 1 : tf == StratTimeframe.Week ? 5 : tf == StratTimeframe.Month ? 21
                : tf == StratTimeframe.Quarter ? 63 : tf == StratTimeframe.HalfYear ? 126 : 252;
        }

        private void BindSources()
        {
            for (int i = 0; i < 6; i++)
            {
                Slot s = slots[i];
                if (!s.Enabled) continue;
                if (s.Intraday)
                    s.Src = new MinuteSource(BarsArray[minuteSeries[StratTimeframes.Minutes(s.Tf)]], StratTimeframes.Minutes(s.Tf), new SessionIterator(BarsArray[0]));
                else
                    s.Src = new DailyAggSource(BarsArray[daySeries], s.Tf);
            }
        }

        private void ClassifyChart()
        {
            BarsPeriod bp = BarsArray[0].BarsPeriod;
            chartType = bp.BarsPeriodType;
            chartValue = bp.Value;
            chartSynthetic = chartType == BarsPeriodType.HeikenAshi;
            // Heiken-Ashi bars keep their interval in the base period; on a time base they are
            // ordinary time bars, as an HA chart is on TradingView. (Kagi, Line Break and P&F also
            // have a base period but are not fixed-interval bars, so they stay non-time.)
            if (chartType == BarsPeriodType.HeikenAshi)
            {
                chartType = bp.BaseBarsPeriodType;
                chartValue = bp.BaseBarsPeriodValue;
            }
            chartDayBased = false;
            chartIntradayTime = false;
            chartSeconds = 0;
            chartAsTf = StratTimeframe.Day;
            switch (chartType)
            {
                case BarsPeriodType.Second: chartSeconds = chartValue; chartIntradayTime = true; break;
                case BarsPeriodType.Minute: chartSeconds = chartValue * 60L; chartIntradayTime = true; break;
                case BarsPeriodType.Day: chartSeconds = chartValue * 86400L; chartDayBased = true; chartAsTf = StratTimeframe.Day; break;
                case BarsPeriodType.Week: chartSeconds = chartValue * 604800L; chartDayBased = true; chartAsTf = StratTimeframe.Week; break;
                case BarsPeriodType.Month: chartSeconds = chartValue * 2629746L; chartDayBased = true; chartAsTf = chartValue >= 12 ? StratTimeframe.Year : chartValue >= 6 ? StratTimeframe.HalfYear : chartValue >= 3 ? StratTimeframe.Quarter : StratTimeframe.Month; break;
                case BarsPeriodType.Year: chartSeconds = chartValue * 31556952L; chartDayBased = true; chartAsTf = StratTimeframe.Year; break;
                default: chartSeconds = 0; break;   // tick, volume, range, Renko, Kagi...: no timeframe to compare
            }
            // Pine validTimeframe: a slot below the chart's timeframe is not evaluated (its row is
            // greyed) but still votes in FTFC. Non-time charts have no timeframe, so nothing is lower.
            for (int i = 0; i < 6; i++)
                slots[i].Lower = chartSeconds > 0 && slots[i].Seconds < chartSeconds;
        }

        // The exhaustion scan needs more than 50 earlier candles (Pine bar_index > 50), which
        // TradingView always has from its own higher-timeframe history. Here a slot only has what
        // its series loaded, and minute series (and the daily series on a daily chart) follow the
        // chart's own history, so a short chart can leave a slot without exhaustion levels.
        private void CheckHistoryDepth()
        {
            // NinjaTrader ignores barsToLoad for a series of the chart's own bar type, so minute
            // slots on a minute chart, and D-and-up slots on a daily chart, follow Days to load.
            BarsPeriodType primary = BarsArray[0].BarsPeriod.BarsPeriodType;
            bool dayCapped = daySeries >= 0 && primary != BarsPeriodType.Day;
            // An honored request that came back short means the provider has no older daily data.
            bool dayProviderOut = dayCapped && BarsArray[daySeries].Count < dailyBarsRequested - 5;
            List<string> shortTfs = new List<string>(), providerTfs = new List<string>();
            bool needDays = false, needMaxDaily = false;
            for (int i = 0; i < 6; i++)
            {
                Slot s = slots[i];
                s.ShortHistory = false;
                if (!s.Enabled || s.Lower || s.Src == null || !settings.ShowExhaustion) continue;
                // 12M exhaustion needs 52+ years of daily data, which TradingView itself has for very
                // few symbols (the Max Daily Bars tooltip gives the bars it takes): not flagged.
                if (s.Tf == StratTimeframe.Year) continue;
                s.Src.Refresh();
                if (s.Src.Count >= 52) continue;
                s.ShortHistory = true;
                bool followsChart = s.Intraday ? primary == BarsPeriodType.Minute : !dayCapped;
                if (followsChart) needDays = true;
                else if (!s.Intraday && !dayProviderOut) needMaxDaily = true;
                else { if (!providerTfs.Contains(s.Label)) providerTfs.Add(s.Label); continue; }
                if (!shortTfs.Contains(s.Label)) shortTfs.Add(s.Label);
            }
            shortHistoryText = shortTfs.Count == 0 ? "" : "Short history: " + string.Join(", ", shortTfs.ToArray())
                + (needDays && needMaxDaily ? " (load more days, raise Max Daily Bars)" : needMaxDaily ? " (raise Max Daily Bars)" : " (load more days)");
            if (shortTfs.Count > 0)
                Print(string.Format("TheStratSuite: {0} loaded fewer than 52 candles, so exhaustion levels cannot form there.{1}{2}",
                    string.Join(", ", shortTfs.ToArray()),
                    needDays ? " Raise the chart's Days to load (Data Series window): minute timeframes on a minute chart, and every timeframe on a daily chart, follow it." : "",
                    needMaxDaily ? " Raise Max Daily Bars To Load (15. Advanced): D and longer on this chart follow it." : ""));
            if (providerTfs.Count > 0)
                Print(string.Format("TheStratSuite: {0} loaded fewer than 52 candles because the data provider has no older history, so exhaustion levels cannot form there.",
                    string.Join(", ", providerTfs.ToArray())));
        }
        private string shortHistoryText = "";

        // ====================================================================
        // PER-BAR EVALUATION
        // ====================================================================

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || slots == null)
                return;
            Evaluate(CurrentBars[0]);
            // With On bar close the next bar processed can be the session's last, inside the same
            // candles, so preview never reaches the evaluated state; it is shown by a display pass.
            if (Calculate == Calculate.OnBarClose && PreviewMode == PreviewModeOption.Auto && IsLastBar(CurrentBars[0])
                && MarketLikelyClosed(CurrentBars[0], NinjaTrader.Core.Globals.Now))
                DisplayPass();
        }

        // Re-evaluates the last bar for display only: no alerts, no bar colors or plot values, and
        // the evaluated state is put back afterwards.
        private void DisplayPass()
        {
            EvalState keepCommitted = committed, keepWorking = working;
            int keepBar = workingBar;
            clockPass = true;
            try { Evaluate(CurrentBars[0]); }
            finally
            {
                clockPass = false;
                committed = keepCommitted;
                working = keepWorking;
                workingBar = keepBar;
            }
        }

        // With Calculate On bar close NinjaTrader never processes the forming bar, in history or
        // realtime, so the last bar it processes is Count - 2.
        private bool IsLastBar(int k)
        {
            int count = BarsArray[0].Count;
            return k >= count - (Calculate == Calculate.OnBarClose ? 2 : 1);
        }

        private DateTime TradingDateOf(int k)
        {
            DateTime t = BarsArray[0].GetTime(k);
            if (chartDayBased)
            {
                // The live bar of a weekly or monthly chart is stamped with its period's last day,
                // which can sit in the next month or quarter (a week spanning a month end). Its
                // trade date so far is the newest daily bar inside the live bar: in the same chart
                // period, or for a multi-period bar (2 weeks, 2 months) after the previous bar.
                DateTime d = t.Date;
                if (k == BarsArray[0].Count - 1 && daySeries >= 0 && BarsArray[daySeries].Count > 0)
                {
                    DateTime newest = BarsArray[daySeries].GetTime(BarsArray[daySeries].Count - 1).Date;
                    long dk = StratTimeframes.PeriodKey(chartAsTf, d);
                    DateTime prevStamp = k > 0 ? BarsArray[0].GetTime(k - 1).Date : DateTime.MinValue;
                    if (newest < d && (StratTimeframes.PeriodKey(chartAsTf, newest) == dk || newest > prevStamp))
                        d = newest;
                    else if (newest < d)
                    {
                        // The daily series (another trading hours template, or a tick behind) has
                        // not printed this chart period yet: use the chart session's trade date.
                        sessionIt.GetNextSession(NinjaTrader.Core.Globals.Now, true);
                        DateTime live = sessionIt.ActualTradingDayExchange.Date;
                        if (live < d && (StratTimeframes.PeriodKey(chartAsTf, live) == dk || live > prevStamp))
                            d = live;
                    }
                }
                return d;
            }
            if (t > cachedSessionEnd || t <= cachedSessionBegin)
            {
                sessionIt.GetNextSession(t, true);
                cachedSessionBegin = sessionIt.ActualSessionBegin;
                cachedSessionEnd = sessionIt.ActualSessionEnd;
                cachedTradingDay = sessionIt.ActualTradingDayExchange;
            }
            return cachedTradingDay;
        }

        // The moment a chart bar closes, for finding intraday candles. Day-based charts stamp bars
        // with the trade date, so their close is the end of that date's last session.
        private DateTime mapDate = DateTime.MinValue, mapEnd;
        private DateTime MapTime(int k, DateTime barTime, DateTime tradingDate)
        {
            if (!chartDayBased) return barTime;
            if (tradingDate != mapDate)
            {
                try { mapEnd = PeriodEndFromDate(chartAsTf, tradingDate); }
                catch { mapEnd = barTime; }
                mapDate = tradingDate;
            }
            return mapEnd;
        }

        private int StartBarOf(Slot s, int htfIndex)
        {
            if (htfIndex < 0 || htfIndex >= s.Src.Count) return -1;
            int b;
            return s.StartBars.TryGetValue(s.Src.Key(htfIndex), out b) ? b : -1;
        }

        private void Evaluate(int k)
        {
            if (k < 0) return;
            // Pine realtime rollback: every tick recomputes from the state committed at the close
            // of the previous chart bar.
            if (k != workingBar)
            {
                if (workingBar >= 0)
                    committed = working;
                workingBar = k;
            }
            working = committed.Clone();
            EvalState st = working;

            Bars b0 = BarsArray[0];
            DateTime barTime = b0.GetTime(k);
            DateTime tradingDate = TradingDateOf(k);
            DateTime barOpen = chartIntradayTime ? IntradayBarOpen(k) : barTime;
            double o0 = b0.GetOpen(k), h0 = b0.GetHigh(k), l0 = b0.GetLow(k), c0 = b0.GetClose(k);
            bool isLast = IsLastBar(k);
            double tick = TickSize > 0 ? TickSize : 0.01;

            // ---- 1. Raw candles per slot (the as-of engine) ----
            SlotRaw[] raws = new SlotRaw[6];
            int[] htfIdx = new int[6];
            for (int i = 0; i < 6; i++)
            {
                raws[i] = new SlotRaw();
                htfIdx[i] = -1;
                Slot s = slots[i];
                if (!s.Enabled || s.Src == null) continue;
                s.Src.Refresh();
                bool exact;
                long key;
                int p = s.Src.IndexFor(MapTime(k, barTime, tradingDate), tradingDate, out exact, out key);
                // A lower slot's candle ending with the live chart bar has not printed yet: use the
                // newest printed lower-timeframe candle, as request.security serves on the live bar.
                if (s.Lower && p >= s.Src.Count && s.Src.Count > 0) { p = s.Src.Count - 1; key = s.Src.Key(p); }
                // A slot whose period is not a whole multiple of the chart's (45m on 30m, 3H on 2H,
                // 15m on 2m) has chart bars that straddle two of its candles. The live bar's close
                // stamp points at the next candle before it opens; until the slot's series prints it,
                // the candle in progress is the newest printed one, as request.security serves.
                if (chartIntradayTime && s.Intraday && !s.Lower && s.Seconds % chartSeconds != 0
                    && k == b0.Count - 1 && p >= s.Src.Count && s.Src.Count > 0
                    && s.Src.Key(s.Src.Count - 1) > barOpen.Ticks)
                {
                    p = s.Src.Count - 1;
                    key = s.Src.Key(p);
                }
                htfIdx[i] = p;
                if (p < 0) continue;
                SlotState ss = st.Slots[i];
                SlotRaw raw = raws[i];
                raw.HasData = true;
                raw.RealPeriodKey = key;
                if (s.Lower || !exact)
                {
                    // A lower slot's candle closes with the chart bar; a candle the chart bar sits
                    // outside of is complete. Either way the served values are known at this bar.
                    raw.CCO = s.Src.O(p); raw.CCH = s.Src.H(p); raw.CCL = s.Src.L(p); raw.CCC = s.Src.C(p);
                    raw.CCBar = StartBarOf(s, p);
                }
                else
                {
                    if (!ss.HasCur || ss.CurKey != key)
                    {
                        ss.HasCur = true;
                        ss.CurKey = key;
                        ss.RunO = o0;
                        ss.RunH = h0;
                        ss.RunL = l0;
                        // A period that began before the chart's first bar: its earlier days are
                        // complete daily bars (dated before this bar's trade date), so they count
                        // without looking ahead.
                        DailyAggSource das = s.Src as DailyAggSource;
                        double preH, preL;
                        if (das != null && das.RangeBefore(p, tradingDate, out preH, out preL))
                        {
                            ss.RunH = Math.Max(ss.RunH, preH);
                            ss.RunL = Math.Min(ss.RunL, preL);
                        }
                        ss.PeriodStartBar = k > 0 ? k : -1;
                        if (k > 0) s.StartBars[key] = k;
                    }
                    else
                    {
                        ss.RunH = Math.Max(ss.RunH, h0);
                        ss.RunL = Math.Min(ss.RunL, l0);
                    }
                    double sO = s.Src.O(p);
                    if (StratGrammar.IsNa(sO)) sO = ss.RunO;     // candle not printed yet by the HTF series
                    if (isLast && !StratGrammar.IsNa(s.Src.H(p)))
                    {
                        // The live bar reads the served candle, as the Pine does: nothing in it is
                        // ahead of "now". Historical bars use the chart-bar rebuild so no candle
                        // shows data from after its own chart bar (the Pine's lookahead leak).
                        raw.CCO = sO; raw.CCH = s.Src.H(p); raw.CCL = s.Src.L(p); raw.CCC = s.Src.C(p);
                        if (string.IsNullOrWhiteSpace(HtfTradingHours) && k == b0.Count - 1 && !chartSynthetic)
                        {
                            // Same session template: the chart series can receive a tick before the
                            // higher-timeframe series does. Only that tick can be missing, and it lies
                            // inside this candle. The chart bar's own high and low are not merged: the
                            // bar can begin in an earlier candle (tick and Renko bars, a 45m bar over
                            // the hour, a weekly bar over a month end). A Heiken-Ashi close is an
                            // average, not a tick, so it is never merged.
                            raw.CCH = Math.Max(raw.CCH, c0);
                            raw.CCL = Math.Min(raw.CCL, c0);
                            raw.CCC = c0;
                        }
                    }
                    else
                    {
                        raw.CCO = sO;
                        raw.CCH = Math.Max(ss.RunH, sO);
                        raw.CCL = Math.Min(ss.RunL, sO);
                        raw.CCC = c0;
                    }
                    raw.CCBar = ss.PeriodStartBar;
                }
                raw.CCStartId = raw.CCBar >= 0 ? raw.CCBar : -((long)(i + 1) * 1000000000L) - p;
                raw.C1H = s.Src.H(p - 1); raw.C1L = s.Src.L(p - 1); raw.C1O = s.Src.O(p - 1); raw.C1C = s.Src.C(p - 1);
                raw.C2H = s.Src.H(p - 2); raw.C2L = s.Src.L(p - 2); raw.C2O = s.Src.O(p - 2); raw.C2C = s.Src.C(p - 2);
                raw.C3H = s.Src.H(p - 3); raw.C3L = s.Src.L(p - 3);
                raw.C4H = s.Src.H(p - 4); raw.C4L = s.Src.L(p - 4);
                raw.C1Bar = StartBarOf(s, p - 1);
                raw.C2Bar = StartBarOf(s, p - 2);
                if (s.ExhKey != key)
                {
                    double nH, nL; int nHi, nLi;
                    HtfSource src = s.Src;
                    StratEngine.FindExhaustion(src.H, src.L, p, settings.ShowAnyTargets, out s.ExhHP, out s.ExhHIdx, out s.ExhLP, out s.ExhLIdx, out nH, out nHi, out nL, out nLi);
                    s.ExhKey = key;
                }
                raw.ExhHP = s.ExhHP; raw.ExhHBar = StartBarOf(s, s.ExhHIdx);
                raw.ExhLP = s.ExhLP; raw.ExhLBar = StartBarOf(s, s.ExhLIdx);
            }

            // ---- 2. Raw FTFC (served opens/closes, unshifted, every enabled slot) ----
            bool[] ftfcEn = new bool[6];
            double[] ftfcO = new double[6], ftfcC = new double[6];
            for (int i = 0; i < 6; i++)
            {
                ftfcEn[i] = slots[i].Enabled && raws[i].HasData;
                ftfcO[i] = raws[i].CCO;
                ftfcC[i] = raws[i].CCC;
            }
            bool ftfcUp, ftfcDown;
            StratGrammar.CalculateFTFC(ftfcEn, ftfcO, ftfcC, out ftfcUp, out ftfcDown);

            // ---- 3. Preview (last bar only) ----
            // The served candles stay as they are for bar coloring (Pine paints from tfNO, which
            // preview never shifts). ApplyPreviewShift returns copies, so a shallow copy is enough.
            SlotRaw[] served = (SlotRaw[])raws.Clone();
            DateTime now = NinjaTrader.Core.Globals.Now;
            bool previewNext = false;
            if (isLast && PreviewMode != PreviewModeOption.Off)
                previewNext = PreviewMode == PreviewModeOption.On
                    || ((Calculate != Calculate.OnBarClose || clockPass) && MarketLikelyClosed(k, now));
            for (int i = 0; i < 6; i++)
            {
                Slot s = slots[i];
                if (!previewNext || !s.Enabled || !raws[i].HasData) continue;
                bool closed = PeriodClosed(s, htfIdx[i], now);
                bool doPreview = StratTimeframes.IsCalendar(s.Tf) ? closed : true;
                if (!doPreview) continue;
                double nH = double.NaN, nL = double.NaN; int nHi = -1, nLi = -1;
                if (closed)
                {
                    double eH, eL; int eHi, eLi;
                    // A closed candle the HTF series has not printed yet scans from its as-of values.
                    HtfSource src = s.Src;
                    SlotRaw cur = raws[i];
                    int pp = htfIdx[i];
                    Func<int, double> fh = j => j == pp && StratGrammar.IsNa(src.H(j)) ? cur.CCH : src.H(j);
                    Func<int, double> fl = j => j == pp && StratGrammar.IsNa(src.L(j)) ? cur.CCL : src.L(j);
                    StratEngine.FindExhaustion(fh, fl, pp, settings.ShowAnyTargets, out eH, out eHi, out eL, out eLi, out nH, out nHi, out nL, out nLi);
                }
                raws[i] = StratEngine.ApplyPreviewShift(raws[i], k, closed, nH, StartBarOf(s, nHi), nL, StartBarOf(s, nLi));
            }

            // ---- 4. Signal state per slot ----
            DebugInfo dbg = (isLast && ShowDebugPanel) ? new DebugInfo() : null;
            SlotResult[] results = new SlotResult[6];
            for (int i = 5; i >= 0; i--)
            {
                Slot s = slots[i];
                if (!s.Enabled) continue;
                bool isDebugTF = dbg != null && (int)DebugTimeframe == i;
                results[i] = StratEngine.ComputeSignalState(settings, st.Slots[i], raws[i], s.ShowOpen && !raws[i].IsPreview, !s.Lower,
                    raws[i].IsPreview, ftfcUp, ftfcDown, s.Label, isDebugTF ? dbg : null, isLast);
            }

            // ---- 5. Lead anchor and filter ----
            int anchorIndex = -1, anchorDirection = 0;
            if (settings.EnableDirectionalFilter)
            {
                for (int i = 5; i >= 0; i--)
                {
                    if (!slots[i].Enabled || anchorIndex != -1 || raws[i].IsPreview) continue;
                    int dir = StratEngine.SignalDirection(results[i]);
                    if (dir != 0) { anchorIndex = i; anchorDirection = dir; }
                }
                if (anchorIndex > 0 && anchorDirection != 0)
                    for (int i = 0; i < anchorIndex; i++)
                        if (slots[i].Enabled && results[i] != null)
                            StratEngine.ApplyLeadFilter(results[i], anchorDirection);
            }

            // ---- 6. Smallest Timeframe Only stops (display only) ----
            if (isLast && settings.ShowStopLevels && settings.StopSmallestOnly)
            {
                int smallest = -1;
                for (int i = 0; i < 6 && smallest == -1; i++)
                {
                    SlotResult r = results[i];
                    if (slots[i].Enabled && r != null && ((r.DrawStopHigh && !StratGrammar.IsNa(r.StopHigh)) || (r.DrawStopLow && !StratGrammar.IsNa(r.StopLow))))
                        smallest = i;
                }
                if (smallest != -1)
                    for (int i = 0; i < 6; i++)
                        if (slots[i].Enabled && i != smallest && results[i] != null) { results[i].DrawStopHigh = false; results[i].DrawStopLow = false; }
            }

            // ---- 7. Domino ----
            bool[] en = new bool[6], prev = new bool[6];
            string[] labels = new string[6];
            for (int i = 0; i < 6; i++) { en[i] = slots[i].Enabled; prev[i] = raws[i].IsPreview; labels[i] = slots[i].Label; }
            int dominoBestRun; string dominoBestTFs;
            StratDomino.Compute(results, en, prev, labels, out dominoBestRun, out dominoBestTFs);

            // ---- 8. Alerts ----
            RunAlerts(st, k, raws, results, anchorIndex, anchorDirection, ftfcUp, ftfcDown, dominoBestRun, dominoBestTFs, tick);

            // ---- 9. Bar coloring and outputs ----
            if (!clockPass)
            {
                PaintBar(st, k, served, o0, h0, l0, c0);
                Values[0][0] = ftfcUp && !ftfcDown ? 1 : ftfcDown && !ftfcUp ? -1 : 0;
                for (int i = 0; i < 6; i++)
                    Values[i + 1][0] = results[i] == null ? 0 : StratEngine.SignalDirection(results[i]);
            }

            // ---- 10. Render model ----
            if (isLast)
            {
                SetFormingBar(now);
                model = BuildModel(st, k, raws, htfIdx, results, anchorIndex, anchorDirection, ftfcUp, ftfcDown, dominoBestRun, dominoBestTFs, previewNext, dbg, c0, tick);
            }
        }

        // The live candle's bar number is drawn lighter until it closes (BARNUM-1). With Calculate
        // On bar close the evaluated bar has closed; the live candle is the next one.
        private void SetFormingBar(DateTime now)
        {
            bool forming = false;
            int live = BarsArray[0].Count - 1;
            if (settings.ShowBarNumbers && State == State.Realtime && live >= 0)
            {
                try { DateTime lc; forming = !LastBarClosed(live, now, out lc) || chartSeconds == 0; }
                catch { forming = false; }
            }
            formingBar = forming ? live : -1;
        }

        // ====================================================================
        // PREVIEW MODE (clock + trading hours)
        // ====================================================================
        // Auto arms when the chart's last bar has passed its scheduled close and, per the chart
        // series' trading hours template (which carries the exchange holidays and early closes),
        // the market is out of session and the next session belongs to another trade date, or 4
        // hours after the last bar's close.

        // Has the chart bar passed its scheduled close? Non-time charts have no schedule.
        private bool LastBarClosed(int k, DateTime now, out DateTime lastClose)
        {
            lastClose = BarsArray[0].GetTime(k);
            if (chartIntradayTime)
                return now > lastClose;
            if (chartDayBased)
            {
                // The bar's own stamp, not its trade date so far: a 2-week or 2-month bar ends
                // with its last unit.
                lastClose = PeriodEndFromDate(chartAsTf, lastClose.Date);
                return now > lastClose;
            }
            return true;
        }

        private bool MarketLikelyClosed(int k, DateTime now)
        {
            try
            {
                // The chart's newest bar: with Calculate On bar close, k is the bar that just closed.
                int live = Math.Max(k, BarsArray[0].Count - 1);
                DateTime lastClose;
                if (!LastBarClosed(live, now, out lastClose)) return false;
                clockIt.GetNextSession(now, true);
                bool inSession = now >= clockIt.ActualSessionBegin && now <= clockIt.ActualSessionEnd;
                // A break between two sessions of one trading day (grains, lunch breaks) is not a
                // close; the Pine only treats the weekend, the daily maintenance hour or an equity
                // overnight as closed, and in each of those the next session is another trade date.
                bool tradeDateOver = clockIt.ActualTradingDayExchange.Date != TradingDateOf(live).Date;
                bool holidayClosed = (now - lastClose).TotalHours > 4;
                return (!inSession && tradeDateOver) || holidayClosed;
            }
            catch
            {
                return false;
            }
        }

        // Has the slot's current period passed its scheduled close? (Pine shouldStraddle.)
        private bool PeriodClosed(Slot s, int p, DateTime now)
        {
            try
            {
                if (s.Intraday)
                {
                    MinuteSource ms = s.Src as MinuteSource;
                    return ms != null && p >= 0 && now >= ms.End(p);
                }
                clockIt.GetNextSession(now, true);
                DateTime nextTradingDay = clockIt.ActualTradingDayExchange;
                return StratTimeframes.PeriodKey(s.Tf, nextTradingDay) != s.Src.Key(p);
            }
            catch
            {
                return false;
            }
        }

        // Scheduled end of the calendar period holding a trading date: the end of the last session
        // whose trading day keys to that period. The walk starts a week early because a period
        // bar's stamp (a month's last calendar day, a holiday Friday) need not be a trading day.
        private DateTime PeriodEndFromDate(StratTimeframe tf, DateTime tradingDate)
        {
            long key = StratTimeframes.PeriodKey(tf, tradingDate);
            endIt.GetNextSession(tradingDate.Date.AddDays(-7), true);
            DateTime end = DateTime.MinValue;
            for (int guard = 0; guard < 2000; guard++)
            {
                long k2 = StratTimeframes.PeriodKey(tf, endIt.ActualTradingDayExchange);
                if (k2 == key) end = endIt.ActualSessionEnd;
                else if (k2 > key) break;
                DateTime prevEnd = endIt.ActualSessionEnd;
                endIt.GetNextSession(prevEnd, false);
                if (endIt.ActualSessionEnd <= prevEnd) break;
            }
            if (end == DateTime.MinValue)
            {
                endIt.GetNextSession(tradingDate.Date, true);
                end = endIt.ActualSessionEnd;
            }
            return end;
        }

        // First session begin of the calendar period holding a trading date (the higher-timeframe
        // candle's open, Pine's ccTime): the first session whose trading day keys to that period.
        private DateTime PeriodOpenFromDate(StratTimeframe tf, DateTime tradingDate)
        {
            DateTime d = tradingDate.Date;
            DateTime start;
            switch (tf)
            {
                case StratTimeframe.Week: start = d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); break;
                case StratTimeframe.Month: start = new DateTime(d.Year, d.Month, 1); break;
                case StratTimeframe.Quarter: start = new DateTime(d.Year, (d.Month - 1) / 3 * 3 + 1, 1); break;
                case StratTimeframe.HalfYear: start = new DateTime(d.Year, d.Month <= 6 ? 1 : 7, 1); break;
                case StratTimeframe.Year: start = new DateTime(d.Year, 1, 1); break;
                default: start = d; break;
            }
            long key = StratTimeframes.PeriodKey(tf, d);
            endIt.GetNextSession(start.AddDays(-7), true);
            for (int guard = 0; guard < 2000; guard++)
            {
                long k2 = StratTimeframes.PeriodKey(tf, endIt.ActualTradingDayExchange);
                if (k2 == key) return endIt.ActualSessionBegin;
                if (k2 > key) break;
                DateTime prevEnd = endIt.ActualSessionEnd;
                endIt.GetNextSession(prevEnd, false);
                if (endIt.ActualSessionEnd <= prevEnd) break;
            }
            return DateTime.MinValue;
        }

        // An intraday chart bar's real open: one chart period before its close stamp, except a
        // session's last bar, which NinjaTrader cuts at the session end and which opens where the
        // bar before it closed (bars in a session are contiguous).
        private DateTime IntradayBarOpen(int k)
        {
            Bars b0 = BarsArray[0];
            DateTime open = b0.GetTime(k).AddSeconds(-chartSeconds);
            if (k > 0 && b0.GetTime(k - 1) > open)
                open = b0.GetTime(k - 1);
            return open;
        }

        // Chart periods (trade dates on a daily chart) in one bar of a multi-period chart.
        private double ChartPerBar()
        {
            return chartType == BarsPeriodType.Day ? Math.Max(1, chartValue)
                : Math.Max(1, Math.Round(chartSeconds / (double)StratTimeframes.Seconds(chartAsTf)));
        }

        // Open of a day-based chart bar: Pine's `time`, the first session of the bar. NinjaTrader
        // stamps a daily bar with its trade date (an ES daily bar opens at 18:00 ET the calendar
        // day before) and a weekly or monthly bar with its period's last day. A multi-period bar
        // opens with the first session after the previous bar. Cached: the live bar asks per tick.
        private int barOpenK = -1;
        private DateTime barOpenDate, barOpenTime;
        private DateTime ChartBarOpen(int k)
        {
            DateTime td = TradingDateOf(k);
            if (k == barOpenK && td == barOpenDate) return barOpenTime;
            DateTime open = DateTime.MinValue;
            if (k > 0 && ChartPerBar() > 1)
            {
                if (chartType == BarsPeriodType.Day)
                    SeekMultiDayBarStart(k);
                else
                    endIt.GetNextSession(PeriodEndFromDate(chartAsTf, BarsArray[0].GetTime(k - 1).Date), false);
                open = endIt.ActualSessionBegin;
            }
            if (open == DateTime.MinValue)
                open = PeriodOpenFromDate(chartAsTf, td);
            if (open == DateTime.MinValue)
                open = BarsArray[0].GetTime(k);
            barOpenK = k;
            barOpenDate = td;
            barOpenTime = open;
            return open;
        }

        // Puts endIt on the first session of multi-day chart bar k (k > 0). A multi-day bar is
        // stamped with its newest trade date, so it starts with the first trade date after the
        // previous bar's.
        private void SeekMultiDayBarStart(int k)
        {
            DateTime prevDate = BarsArray[0].GetTime(k - 1).Date;
            endIt.GetNextSession(prevDate, false);
            for (int g = 0; g < 20 && endIt.ActualTradingDayExchange.Date <= prevDate; g++)
            {
                DateTime pe = endIt.ActualSessionEnd;
                endIt.GetNextSession(pe, false);
                if (endIt.ActualSessionEnd <= pe) break;
            }
        }

        private void StartClock()
        {
            // Auto preview and the forming bar number both change when a bar closes with no new tick.
            if (ChartControl == null || (PreviewMode != PreviewModeOption.Auto && !ShowBarNumbers)) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (clockTimer != null || State >= State.Terminated) return;
                clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                clockTimer.Tick += OnClockTick;
                clockTimer.Start();
            });
        }

        // ChartControl can already be null at Terminated (chart tab closed), so the timer is
        // stopped through its own dispatcher, and a tick after Terminated stops it as well.
        private void StopClock()
        {
            DispatcherTimer t = clockTimer;
            clockTimer = null;
            if (t == null) return;
            if (t.Dispatcher.CheckAccess()) { t.Stop(); t.Tick -= OnClockTick; }
            else t.Dispatcher.InvokeAsync(() => { t.Stop(); t.Tick -= OnClockTick; });
        }

        // Re-evaluates the last bar with no new tick, so Auto preview arms and disarms on the
        // clock the way the Pine's timenow does on TradingView. The pass is display only: a bar
        // NinjaTrader processed in history, or one that closed with no new tick, must not fire its
        // alerts again or keep preview-shifted state.
        private void OnClockTick(object sender, EventArgs e)
        {
            if (State >= State.Terminated)
            {
                DispatcherTimer dt = sender as DispatcherTimer;
                if (dt != null) { dt.Stop(); dt.Tick -= OnClockTick; }
                return;
            }
            if (State != State.Realtime && State != State.Historical) return;
            TriggerCustomEvent(o =>
            {
                // Without Auto preview only the forming bar's number depends on the clock.
                if (PreviewMode != PreviewModeOption.Auto)
                {
                    if (settings != null) SetFormingBar(NinjaTrader.Core.Globals.Now);
                    return;
                }
                if (CurrentBars[0] >= 0 && IsLastBar(CurrentBars[0]))
                    DisplayPass();
            }, 0, null);
            ForceRefresh();
        }

        // ====================================================================
        // ALERTS (Pine SECTION 13)
        // ====================================================================

        private void RunAlerts(EvalState st, int k, SlotRaw[] raws, SlotResult[] results, int anchorIndex, int anchorDirection,
            bool ftfcUp, bool ftfcDown, int dominoBestRun, string dominoBestTFs, double tick)
        {
            // FIX P1-c: reset was-state on a new HTF period.
            for (int i = 0; i < 6; i++)
            {
                if (!raws[i].HasData) continue;
                long sp = raws[i].RealPeriodKey;
                if (!st.AlertHasPeriod[i] || st.AlertLastPeriod[i] != sp)
                {
                    st.WasSetupBull[i] = st.WasSetupBear[i] = st.WasF2Bull[i] = st.WasF2Bear[i] = false;
                    st.WasPotentialBull[i] = st.WasPotentialBear[i] = false;
                }
                st.AlertLastPeriod[i] = sp;
                st.AlertHasPeriod[i] = true;
            }
            bool[] newlyBull = new bool[6], newlyBear = new bool[6];
            bool anyNewlyInForce = false, anyNewlyPotential = false;
            string alertMsg = "";
            for (int i = 0; i < 6; i++)
            {
                if (!slots[i].Enabled) continue;
                SlotResult r = results[i];
                SlotState d = st.Slots[i];
                bool slotPreview = raws[i].IsPreview;
                bool suppressBull = settings.EnableDirectionalFilter && anchorDirection == -1 && i < anchorIndex;
                bool suppressBear = settings.EnableDirectionalFilter && anchorDirection == 1 && i < anchorIndex;
                bool nowSetupBull = !suppressBull && StratLabels.IsSetupBull(r);
                bool nowSetupBear = !suppressBear && StratLabels.IsSetupBear(r);
                bool nowF2Bull = !suppressBull && StratLabels.IsF2Bull(settings, r);
                bool nowF2Bear = !suppressBear && StratLabels.IsF2Bear(settings, r);
                // Pine's per-TF alertconditions read raw (unsuppressed) setups.
                newlyBull[i] = !slotPreview && ((StratLabels.IsSetupBull(r) && !st.WasSetupBull[i]) || (StratLabels.IsF2Bull(settings, r) && !st.WasF2Bull[i]));
                newlyBear[i] = !slotPreview && ((StratLabels.IsSetupBear(r) && !st.WasSetupBear[i]) || (StratLabels.IsF2Bear(settings, r) && !st.WasF2Bear[i]));
                if (settings.AlertTF[i] && !slotPreview)
                {
                    if (nowSetupBull && !st.WasSetupBull[i]) { anyNewlyInForce = true; alertMsg = Join(alertMsg, StratLabels.BuildAlertLabel(settings, r, true, d, r, tick)); }
                    if (nowSetupBear && !st.WasSetupBear[i]) { anyNewlyInForce = true; alertMsg = Join(alertMsg, StratLabels.BuildAlertLabel(settings, r, false, d, r, tick)); }
                    if (nowF2Bull && !st.WasF2Bull[i]) { anyNewlyInForce = true; alertMsg = Join(alertMsg, StratLabels.BuildAlertLabel(settings, r, true, d, r, tick)); }
                    if (nowF2Bear && !st.WasF2Bear[i]) { anyNewlyInForce = true; alertMsg = Join(alertMsg, StratLabels.BuildAlertLabel(settings, r, false, d, r, tick)); }
                }
                st.WasSetupBull[i] = nowSetupBull;
                st.WasSetupBear[i] = nowSetupBear;
                st.WasF2Bull[i] = nowF2Bull;
                st.WasF2Bear[i] = nowF2Bear;
                bool potBull = StratLabels.IsPotentialBull(r), potBear = StratLabels.IsPotentialBear(r);
                if (settings.AlertTF[i] && !slotPreview && ((potBull && !st.WasPotentialBull[i]) || (potBear && !st.WasPotentialBear[i])))
                    anyNewlyPotential = true;
                st.WasPotentialBull[i] = potBull;
                st.WasPotentialBear[i] = potBear;
            }
            if (anyNewlyInForce && alertMsg.Length > 0 && settings.AlertShowFTFC)
                alertMsg = alertMsg + " | " + (ftfcUp ? "FTFC Up" : ftfcDown ? "FTFC Down" : "Conflict");

            bool dominoTriggered = false;
            if (dominoBestRun >= settings.MinDominoTFs && dominoBestTFs != st.LastDominoCombo)
            {
                dominoTriggered = true;
                st.LastDominoCombo = dominoBestTFs;
            }
            else if (dominoBestRun < settings.MinDominoTFs)
                st.LastDominoCombo = "";

            bool shiftedUp = ftfcUp && !st.WasFtfcUp;
            bool shiftedDown = ftfcDown && !st.WasFtfcDown;
            bool shiftedConflict = !ftfcUp && !ftfcDown && (st.WasFtfcUp || st.WasFtfcDown);
            st.WasFtfcUp = ftfcUp;
            st.WasFtfcDown = ftfcDown;

            if (!EnableAlerts || State != State.Realtime || clockPass)
                return;
            if (AlertSignalInForceAny && anyNewlyInForce && alertMsg.Length > 0) Fire("any", k, alertMsg, Priority.High);
            if (settings.EnableDominoAlerts && dominoTriggered) Fire("dominoMsg", k, "Domino: " + dominoBestTFs, Priority.Medium);
            bool[] perTf = new bool[] { AlertTF1InForce, AlertTF2InForce, AlertTF3InForce, AlertTF4InForce, AlertTF5InForce, AlertTF6InForce };
            bool anyBull = false, anyBear = false;
            for (int i = 0; i < 6; i++)
            {
                if (!slots[i].Enabled) continue;
                if (perTf[i] && (newlyBull[i] || newlyBear[i])) Fire("tf" + (i + 1), k, "TF" + (i + 1) + " In-Force (" + slots[i].Label + ")", Priority.Medium);
                anyBull |= newlyBull[i];
                anyBear |= newlyBear[i];
            }
            if (AlertBullishInForce && anyBull) Fire("bull", k, "Bullish In-Force", Priority.Medium);
            if (AlertBearishInForce && anyBear) Fire("bear", k, "Bearish In-Force", Priority.Medium);
            if (AlertNewPotential && anyNewlyPotential) Fire("potential", k, "New Potential Signal", Priority.Low);
            if (AlertDominoSetup && dominoTriggered) Fire("domino", k, "Domino Setup: " + dominoBestTFs, Priority.Medium);
            if (AlertFtfcShifted && (shiftedUp || shiftedDown || shiftedConflict)) Fire("ftfc", k, "FTFC Shifted", Priority.Medium);
            if (AlertFtfcUp && shiftedUp) Fire("ftfcUp", k, "FTFC Up", Priority.Medium);
            if (AlertFtfcDown && shiftedDown) Fire("ftfcDown", k, "FTFC Down", Priority.Medium);
            if (AlertFtfcConflict && shiftedConflict) Fire("ftfcConflict", k, "FTFC Conflict", Priority.Low);
        }

        private static string Join(string a, string b) { return a == "" ? b : a + " | " + b; }

        // Once per chart bar per alert, like the Pine's alert.freq_once_per_bar.
        private void Fire(string id, int k, string message, Priority priority)
        {
            int last;
            if (alertFiredBar.TryGetValue(id, out last) && last == k) return;
            alertFiredBar[id] = k;
            string sound = string.IsNullOrWhiteSpace(AlertSound) ? "" : System.IO.Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", AlertSound.Trim());
            Alert("TheStratSuite." + id, priority, message, sound, 0, Brushes.Black, Brushes.White);
        }

        // ====================================================================
        // BAR COLORING (Pine SECTION 14)
        // ====================================================================

        private void PaintBar(EvalState st, int k, SlotRaw[] raws, double o0, double h0, double l0, double c0)
        {
            if (settings.BarColorMode == BarColorModeOption.Off)
                return;
            // A transparent user color still paints (an invisible candle, as Pine barcolor does),
            // so "paint or leave the chart's color" is tracked apart from the color itself.
            SColor color = SColor.None;
            bool paint = false;
            if (settings.BarColorMode == BarColorModeOption.StratCandles)
            {
                if (k > 0)
                {
                    bool failed;
                    Bars b0 = BarsArray[0];
                    string t = StratGrammar.DetectBarTypeAndFailed(b0.GetHigh(k - 1), b0.GetLow(k - 1), h0, l0, o0, c0, settings.EnableFailed2Detection, settings.Failed2Method, out failed);
                    SColor? sc = StratCandleColor(t);
                    if (sc.HasValue) { color = sc.Value; paint = true; }
                }
            }
            else
            {
                // FTFC Candles: the chart bar's close against each slot's open as it stood at this
                // bar, so painted history shows continuity as it developed.
                bool[] en = new bool[6];
                double[] opens = new double[6], closes = new double[6];
                bool hasData = false, anyF2u = false, anyF2d = false;
                for (int i = 0; i < 6; i++)
                {
                    en[i] = slots[i].Enabled && raws[i].HasData;
                    opens[i] = raws[i].CCO;
                    closes[i] = c0;
                    if (en[i] && !StratGrammar.IsNa(raws[i].CCO)) hasData = true;
                    if (settings.PaintFtfcTransitions && en[i] && !slots[i].Lower)
                    {
                        StratGrammar.Failed2 f = StratGrammar.DetectFailed2(raws[i].C1H, raws[i].C1L, raws[i].CCO, c0, raws[i].CCH, raws[i].CCL, settings.EnableFailed2Detection, settings.Failed2Method);
                        if (!StratGrammar.IsNa(raws[i].C1H) && !StratGrammar.IsNa(raws[i].C1L) && !StratGrammar.IsNa(raws[i].CCO))
                        {
                            anyF2u |= f.IsF2u;
                            anyF2d |= f.IsF2d;
                        }
                    }
                }
                bool up, down;
                StratGrammar.CalculateFTFC(en, opens, closes, out up, out down);
                bool flipUp = up && !st.WasPaintFtfcUp;
                bool flipDown = down && !st.WasPaintFtfcDown;
                st.WasPaintFtfcUp = up;
                st.WasPaintFtfcDown = down;
                if (hasData)
                {
                    paint = true;
                    if (settings.PaintFtfcTransitions && flipUp && anyF2d) color = settings.ColorFailed2D;
                    else if (settings.PaintFtfcTransitions && flipDown && anyF2u) color = settings.ColorFailed2U;
                    else if (up) color = settings.ColorFtfcUpBar;
                    else if (down) color = settings.ColorFtfcDownBar;
                    else if (settings.PaintFtfcConflict) color = settings.ColorFtfcConflictBar;
                    else paint = false;
                }
            }
            if (!paint)
            {
                BarBrushes[0] = null;
                CandleOutlineBrushes[0] = null;
            }
            else
            {
                Brush br = WpfBrush(color);
                BarBrushes[0] = br;
                CandleOutlineBrushes[0] = br;
            }
        }

        // null leaves the chart's own candle color.
        private SColor? StratCandleColor(string barType)
        {
            StratSettings s = settings;
            switch (barType)
            {
                case "1u": return s.PaintInsideBars ? s.ColorBullishPotential : (SColor?)null;
                case "1d": return s.PaintInsideBars ? s.ColorBearishPotential : (SColor?)null;
                case "2u": return s.Paint2Bars ? s.ColorBullishActive : (SColor?)null;
                case "2d": return s.Paint2Bars ? s.ColorBearishActive : (SColor?)null;
                case "F2u": return s.PaintFailedBars ? s.ColorFailed2U : s.Paint2Bars ? s.ColorBullishActive : (SColor?)null;
                case "F2d": return s.PaintFailedBars ? s.ColorFailed2D : s.Paint2Bars ? s.ColorBearishActive : (SColor?)null;
                case "3u": return s.PaintOutsideBars ? s.Color3High : (SColor?)null;
                case "3d": return s.PaintOutsideBars ? s.Color3Low : (SColor?)null;
                default: return null;
            }
        }

        private Brush WpfBrush(SColor c)
        {
            SolidColorBrush b;
            if (!wpfBrushes.TryGetValue(c.Key, out b))
            {
                b = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
                b.Freeze();
                wpfBrushes[c.Key] = b;
            }
            return b;
        }

        // ====================================================================
        // RENDER MODEL (Pine SECTIONS 7-12, last bar only)
        // ====================================================================

        // Chart-bar position (fractional, may lie beyond the last bar) of a period end.
        private double EndX(Slot s, SlotRaw raw, int htfIndex, int lastBar)
        {
            try
            {
                DateTime lastTime = BarsArray[0].GetTime(lastBar);
                DateTime end;
                if (raw.IsPreview)   // Pine: the next period runs from this chart bar's open
                    end = (chartIntradayTime ? IntradayBarOpen(lastBar)
                        : chartDayBased ? ChartBarOpen(lastBar)
                        : lastTime).AddSeconds(s.Seconds);
                else if (s.Intraday)
                {
                    MinuteSource ms = s.Src as MinuteSource;
                    end = ms != null && htfIndex >= 0 ? ms.End(htfIndex) : lastTime.AddSeconds(s.Seconds);
                }
                else
                {
                    long key = s.Src.Key(htfIndex);
                    if (s.EndKey != key)
                    {
                        // Pine: max(period open + nominal period, scheduled close).
                        DateTime td = TradingDateOf(lastBar);
                        DateTime close = PeriodEndFromDate(s.Tf, td);
                        DateTime open = PeriodOpenFromDate(s.Tf, td);
                        DateTime nominal = open == DateTime.MinValue ? close : open.AddSeconds(s.Seconds);
                        s.EndTime = nominal > close ? nominal : close;
                        s.EndKey = key;
                    }
                    end = s.EndTime;
                }
                return TimeToX(end, lastBar);
            }
            catch
            {
                return lastBar + 1;
            }
        }

        // Pine's xloc.bar_time puts a time on the bar that OPENS then. NinjaTrader stamps intraday
        // bars with their close, so the bar closing at t is one bar early: step to the next one.
        private double TimeToX(DateTime t, int lastBar)
        {
            Bars b0 = BarsArray[0];
            DateTime lastTime = b0.GetTime(lastBar);
            if (t <= lastTime)
            {
                int lo = 0, hi = lastBar, found = lastBar;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (b0.GetTime(mid) >= t) { found = mid; hi = mid - 1; }
                    else lo = mid + 1;
                }
                if (chartIntradayTime && IntradayBarOpen(found) < t)
                    return found + 1;
                // Day-based bars are stamped with their period's last calendar day, so the search
                // finds the bar holding t. As on intraday charts, a time after the bar's open (inside
                // it, or after its last session and before the stamp) belongs to the next bar.
                if (chartDayBased && t > ChartBarOpen(found))
                    return found + 1;
                return found;
            }
            if (chartSeconds <= 0)
                return lastBar + 10;       // no clock on tick/range charts: a short projection
            if (chartDayBased)
            {
                // One unit per chart period (a trade date on a daily chart, so a template with two
                // sessions a day still counts one), from the live bar's first unit. A multi-period
                // bar (2 days, 2 weeks, 2 months) holds several units. Weekly and monthly bars are
                // stamped with their last period's last day, so every unit of the live bar lies up to
                // the stamp; a multi-day bar is stamped with its newest trade date, so its walk
                // starts at the bar's first session instead.
                double perBar = ChartPerBar();
                long prevKey = StratTimeframes.PeriodKey(chartAsTf, lastTime.Date);
                double units = perBar;
                if (chartType == BarsPeriodType.Day && perBar > 1 && lastBar > 0)
                {
                    SeekMultiDayBarStart(lastBar);
                    prevKey = long.MinValue;
                    units = 0;
                }
                else
                    endIt.GetNextSession(lastTime, false);
                int guard = 0;
                while (guard++ < 2000 && endIt.ActualSessionBegin < t)
                {
                    long pk = StratTimeframes.PeriodKey(chartAsTf, endIt.ActualTradingDayExchange);
                    if (pk != prevKey) { units += 1; prevKey = pk; }
                    DateTime prevEnd = endIt.ActualSessionEnd;
                    endIt.GetNextSession(prevEnd, false);
                    if (endIt.ActualSessionEnd <= prevEnd) break;
                }
                return lastBar + Math.Max(1, Math.Floor((units - 1) / perBar) + 1);
            }
            // Intraday: count only in-session time, as the chart's own future bars would.
            // NinjaTrader cuts each session's last bar at the session end, and that short bar is a
            // whole slot on the chart (as in TradingView's future time scale): a session the window
            // runs through to its end counts in whole bars, and only the final partial one by time.
            double bars = 0, seconds = 0;
            DateTime c = lastTime;
            int n = 0;
            while (n++ < 2000 && c < t)
            {
                endIt.GetNextSession(c, false);
                DateTime begin = endIt.ActualSessionBegin > c ? endIt.ActualSessionBegin : c;
                DateTime finish = endIt.ActualSessionEnd < t ? endIt.ActualSessionEnd : t;
                if (finish > begin)
                {
                    if (finish == endIt.ActualSessionEnd) bars += Math.Ceiling((finish - begin).TotalSeconds / chartSeconds);
                    else seconds += (finish - begin).TotalSeconds;
                }
                if (endIt.ActualSessionEnd <= c) break;
                c = endIt.ActualSessionEnd;
            }
            return lastBar + bars + Math.Ceiling(seconds / chartSeconds) + 1;
        }

        private RenderModel BuildModel(EvalState st, int k, SlotRaw[] raws, int[] htf, SlotResult[] results, int anchorIndex, int anchorDirection,
            bool ftfcUp, bool ftfcDown, int dominoBestRun, string dominoBestTFs, bool previewNext, DebugInfo dbg, double close, double tick)
        {
            StratSettings s = settings;
            RenderModel m = new RenderModel();
            m.LastBar = k;
            double[] endX = new double[6];
            for (int i = 0; i < 6; i++)
            {
                if (!slots[i].Enabled || slots[i].Src == null || !raws[i].HasData) continue;
                endX[i] = EndX(slots[i], raws[i], htf[i], k);
            }

            // Lines and boxes (renderSignalLevels), then cross-timeframe suppression.
            List<KeyValuePair<RLine, long>> highPool = new List<KeyValuePair<RLine, long>>();
            List<KeyValuePair<RLine, long>> lowPool = new List<KeyValuePair<RLine, long>>();
            List<KeyValuePair<RLine, long>> openPool = new List<KeyValuePair<RLine, long>>();
            List<RLine> stops = new List<RLine>();
            for (int i = 5; i >= 0; i--)
            {
                Slot sl = slots[i];
                SlotResult r = results[i];
                if (!sl.Enabled || r == null) continue;
                SlotState d = st.Slots[i];
                SlotRaw raw = raws[i];
                double x2 = endX[i];
                int w = sl.LineWidth;
                double ccStart = Math.Max(0, raw.CCBar);
                if (r.DrawHigh) highPool.Add(Pair(Line(Math.Max(0, d.PrevBar), x2, r.High, r.HighColor, w, r.HighStyle), sl.Seconds));
                if (r.DrawLow) lowPool.Add(Pair(Line(Math.Max(0, d.PrevBar), x2, r.Low, r.LowColor, w, r.LowStyle), sl.Seconds));
                if (r.DrawOpen) openPool.Add(Pair(Line(ccStart, x2, r.Open, SColor.TvGray, w, LineStyleCode.Dotted), sl.Seconds));
                if (r.DrawMagHigh) highPool.Add(Pair(Line(Math.Max(0, d.PrevMagBar), x2, r.MagHigh, r.MagHighColor, w, LineStyleCode.Solid), sl.Seconds));
                if (r.DrawMagLow) lowPool.Add(Pair(Line(Math.Max(0, d.PrevMagBar), x2, r.MagLow, r.MagLowColor, w, LineStyleCode.Solid), sl.Seconds));
                if (r.DrawExhHigh) highPool.Add(Pair(Line(Math.Max(0, d.ExhHighBar), x2, r.ExhHigh, r.ExhHighColor, w, LineStyleCode.Solid), sl.Seconds));
                if (r.DrawExhLow) lowPool.Add(Pair(Line(Math.Max(0, d.ExhLowBar), x2, r.ExhLow, r.ExhLowColor, w, LineStyleCode.Solid), sl.Seconds));
                bool stopHighOverlaps = r.DrawStopHigh && !StratGrammar.IsNa(r.StopHigh) && !StratGrammar.IsNa(r.High) && StratLabels.TickKey(r.StopHigh, tick) == StratLabels.TickKey(r.High, tick);
                if (r.DrawStopHigh && !StratGrammar.IsNa(r.StopHigh) && !stopHighOverlaps) stops.Add(Line(ccStart, x2, r.StopHigh, s.ColorStopHigh, w, LineStyleCode.Solid));
                bool stopLowOverlaps = r.DrawStopLow && !StratGrammar.IsNa(r.StopLow) && !StratGrammar.IsNa(r.Low) && StratLabels.TickKey(r.StopLow, tick) == StratLabels.TickKey(r.Low, tick);
                if (r.DrawStopLow && !StratGrammar.IsNa(r.StopLow) && !stopLowOverlaps) stops.Add(Line(ccStart, x2, r.StopLow, s.ColorStopLow, w, LineStyleCode.Solid));
                bool drawF2Open = s.ShowF2OpenLine && !StratGrammar.IsNa(r.F2OpenPrice) && (r.PreF2d || r.PreF2u || r.IsF2d || r.IsF2u);
                if (drawF2Open)
                {
                    SColor f2c = r.PreF2d ? s.ColorBullishActive : r.IsF2d ? s.ColorFailed2D : r.PreF2u ? s.ColorBearishActive : s.ColorFailed2U;
                    bool overLow = !StratGrammar.IsNa(r.Low) && StratLabels.TickKey(r.F2OpenPrice, tick) == StratLabels.TickKey(r.Low, tick);
                    bool overHigh = !StratGrammar.IsNa(r.High) && StratLabels.TickKey(r.F2OpenPrice, tick) == StratLabels.TickKey(r.High, tick);
                    bool overlaps = (r.PreF2d || r.IsF2d) ? overLow : overHigh;
                    if (!overlaps) openPool.Add(Pair(Line(ccStart, x2, r.F2OpenPrice, f2c, w, LineStyleCode.Dotted), sl.Seconds));
                }
                AddTaw(m, s, d, r, Math.Max(0, d.PrevBar), x2, close);
            }
            m.Lines.AddRange(Suppress(highPool, tick));
            m.Lines.AddRange(Suppress(lowPool, tick));
            m.Lines.AddRange(Suppress(openPool, tick));
            m.Lines.AddRange(stops);   // stops are never suppressed (risk management)

            // StratLabels.
            if (s.ShowTimelineLabels || s.ShowFloatingLabels)
            {
                List<LabelEntry> hi = new List<LabelEntry>(), lo = new List<LabelEntry>(), op = new List<LabelEntry>();
                for (int i = 5; i >= 0; i--)
                {
                    SlotResult r = results[i];
                    if (!slots[i].Enabled || r == null) continue;
                    bool skipHigh = false, skipLow = false;
                    if (s.EnableDirectionalFilter && i < anchorIndex)
                        StratEngine.LeadLabelSkips(r, anchorDirection, out skipHigh, out skipLow);
                    SlotResult view = (!skipHigh && !skipLow) ? r : StratEngine.LabelView(r, skipHigh, skipLow);
                    StratLabels.CollectTimeframeLabels(s, st.Slots[i], view, tick, endX[i], hi, lo, op);
                }
                SColor bg = s.LabelBackgroundColor.WithTransp(s.LabelBackgroundTransparency);
                foreach (List<LabelEntry> pool in new List<LabelEntry>[] { hi, lo, op })
                {
                    foreach (LabelEntry e in StratLabels.Consolidate(pool, tick))
                    {
                        string text = s.ShowPriceInLabel ? e.Text + " " + StratLabels.FormatPrice(e.Price, tick) : e.Text;
                        SColor fg = ColorMath.ReadableOn(e.Color, s.LabelBackgroundColor);
                        if (s.ShowTimelineLabels)
                            m.Labels.Add(new RLabel { X = e.EndX + s.LabelOffset, Price = e.Price, Text = text, Bg = bg, Fg = fg });
                        if (s.ShowFloatingLabels)
                            m.Labels.Add(new RLabel { X = k + s.FloatingLabelOffset, Price = e.Price, Text = text, Bg = bg, Fg = fg });
                    }
                }
            }

            if (ShowDataTable)
                m.Table = BuildTable(st, raws, results, anchorIndex, anchorDirection, ftfcUp, ftfcDown, dominoBestRun, dominoBestTFs, previewNext);
            if (ShowDebugPanel && dbg != null)
                m.Debug = BuildDebug(dbg);
            return m;
        }

        private static KeyValuePair<RLine, long> Pair(RLine l, long seconds) { return new KeyValuePair<RLine, long>(l, seconds); }

        private static RLine Line(double x1, double x2, double price, SColor c, int w, LineStyleCode style)
        {
            return new RLine { X1 = x1, X2 = x2, Price = price, Color = c, Width = w, Style = style };
        }

        // Pine suppressLowerTFLines: at one tick-rounded price keep only the highest timeframe's
        // line; a tie keeps the one collected first (higher slot).
        private static List<RLine> Suppress(List<KeyValuePair<RLine, long>> pool, double tick)
        {
            Dictionary<long, int> keep = new Dictionary<long, int>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (StratGrammar.IsNa(pool[i].Key.Price)) continue;
                long key = StratLabels.TickKey(pool[i].Key.Price, tick);
                int idx;
                if (!keep.TryGetValue(key, out idx) || pool[i].Value > pool[idx].Value)
                    keep[key] = i;
            }
            List<RLine> output = new List<RLine>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (StratGrammar.IsNa(pool[i].Key.Price)) continue;
                if (keep[StratLabels.TickKey(pool[i].Key.Price, tick)] == i)
                    output.Add(pool[i].Key);
            }
            return output;
        }

        // Pine renderSignalLevels, Take Action Window part.
        private static void AddTaw(RenderModel m, StratSettings s, SlotState d, SlotResult r, double x1, double x2, double close)
        {
            RBox bull = null, bear = null;
            bool aboveTrigger = !StratGrammar.IsNa(r.High) && close > r.High;
            bool belowTrigger = !StratGrammar.IsNa(r.Low) && close < r.Low;
            bool ccIs3 = r.CCType.StartsWith("3");
            if (s.ShowTakeActionWindows || s.TawExtendToExhaustion)
            {
                bool magBull = s.ShowTakeActionWindows && r.DrawHigh && !r.IsF2u && !ccIs3 && (!s.TawOnlyWhenInForce || r.SignalInForceHigh) && aboveTrigger;
                bool magBear = s.ShowTakeActionWindows && r.DrawLow && !r.IsF2d && !ccIs3 && (!s.TawOnlyWhenInForce || r.SignalInForceLow) && belowTrigger;
                bool exhBull = s.TawExtendToExhaustion && r.DrawExhHigh && !r.IsF2u && !ccIs3 && (!s.TawExhOnlyWhenInForce || r.SignalInForceHigh) && aboveTrigger;
                bool exhBear = s.TawExtendToExhaustion && r.DrawExhLow && !r.IsF2d && !ccIs3 && (!s.TawExhOnlyWhenInForce || r.SignalInForceLow) && belowTrigger;
                if (exhBull && !StratGrammar.IsNa(d.ExhHighPrice) && !d.ExhHighCrossed) bull = Box(x1, x2, d.ExhHighPrice, r.High, s, true);
                else if (magBull && !StratGrammar.IsNa(d.PrevMagHigh) && !d.MagHighCrossed) bull = Box(x1, x2, d.PrevMagHigh, r.High, s, true);
                if (exhBear && !StratGrammar.IsNa(d.ExhLowPrice) && !d.ExhLowCrossed) bear = Box(x1, x2, r.Low, d.ExhLowPrice, s, false);
                else if (magBear && !StratGrammar.IsNa(d.PrevMagLow) && !d.MagLowCrossed) bear = Box(x1, x2, r.Low, d.PrevMagLow, s, false);
            }
            if (s.ShowP3TakeActionWindows && s.ShowP3)
            {
                if (r.IsF2d && r.DrawHigh && !aboveTrigger && (!s.TawOnlyWhenInForce || r.SignalInForceHigh)) bull = Box(x1, x2, r.High, r.Low, s, true);
                if (r.IsF2u && r.DrawLow && !belowTrigger && (!s.TawOnlyWhenInForce || r.SignalInForceLow)) bear = Box(x1, x2, r.High, r.Low, s, false);
            }
            if (s.Show3ExpTakeActionWindows && s.Show3Expansions && ccIs3)
            {
                bool up = r.CCType.EndsWith("u");
                if (up && r.DrawHigh && !StratGrammar.IsNa(d.PrevMagHigh) && !d.MagHighCrossed && (!s.TawOnlyWhenInForce || r.SignalInForceHigh)) bull = Box(x1, x2, d.PrevMagHigh, r.High, s, true);
                if (!up && r.DrawLow && !StratGrammar.IsNa(d.PrevMagLow) && !d.MagLowCrossed && (!s.TawOnlyWhenInForce || r.SignalInForceLow)) bear = Box(x1, x2, r.Low, d.PrevMagLow, s, false);
            }
            if (bull != null) m.Boxes.Add(bull);
            if (bear != null) m.Boxes.Add(bear);
        }

        private static RBox Box(double x1, double x2, double top, double bottom, StratSettings s, bool bullish)
        {
            if (StratGrammar.IsNa(top) || StratGrammar.IsNa(bottom)) return null;
            return new RBox
            {
                X1 = x1, X2 = x2, Top = Math.Max(top, bottom), Bottom = Math.Min(top, bottom),
                Fill = bullish ? s.TawBullishFill : s.TawBearishFill,
                Border = bullish ? s.TawBullishBorder : s.TawBearishBorder
            };
        }

        // ---- Data table (Pine SECTION 11) ----

        private SColor BarTypeColor(string t)
        {
            switch (t)
            {
                case "1u": return SColor.Hex(0xFFEB3B);
                case "1d": return SColor.Hex(0xFF9800);
                case "2u": return SColor.Hex(0x4CAF50);
                case "2d": return SColor.Hex(0xF23645);
                // Pine getBarTypeColor: color.new(x, 0), so a cell stays solid whatever the input's alpha.
                case "F2u": return settings.ColorFailed2U.WithTransp(0);
                case "F2d": return settings.ColorFailed2D.WithTransp(0);
                case "3u": return settings.ColorBullish3Bar.WithTransp(0);
                case "3d": return settings.ColorBearish3Bar.WithTransp(0);
                default: return SColor.TvGray.WithTransp(50);
            }
        }

        // Pine getBarTypeTextColor: the input colors as given.
        private SColor BarTypeTextColor(string t)
        {
            switch (t)
            {
                case "1u": return SColor.Hex(0xFFEB3B);
                case "1d": return SColor.Hex(0xFF9800);
                case "2u": return SColor.Hex(0x4CAF50);
                case "2d": return SColor.Hex(0xF23645);
                case "F2u": return settings.ColorFailed2U;
                case "F2d": return settings.ColorFailed2D;
                case "3u": return settings.ColorBullish3Bar;
                case "3d": return settings.ColorBearish3Bar;
                default: return SColor.TvGray;
            }
        }

        private static RCell Cell(string text, SColor fg, SColor bg) { return new RCell { Text = text, Fg = fg, Bg = bg }; }

        private RTable BuildTable(EvalState st, SlotRaw[] raws, SlotResult[] results, int anchorIndex, int anchorDirection,
            bool ftfcUp, bool ftfcDown, int dominoBestRun, string dominoBestTFs, bool previewNext)
        {
            StratSettings s = settings;
            int enabledCount = 0;
            for (int i = 0; i < 6; i++) if (slots[i].Enabled) enabledCount++;
            if (enabledCount == 0) return null;
            SColor black = SColor.TvBlack;
            SColor dark = SColor.Hex(0x111111);
            RTable t = new RTable();
            t.Position = TablePosition;
            t.Size = TableTextSize;
            bool f2 = s.EnableFailed2Detection;

            // FTFC row, with Exhaustion Excludes from FTFC (table only, FIX P1-e).
            bool modUp = ftfcUp, modDown = ftfcDown;
            if (s.ExhDisablesFTFC)
            {
                modUp = true; modDown = true;
                for (int i = 5; i >= 0; i--)
                {
                    if (!slots[i].Enabled || slots[i].Lower || !raws[i].HasData) continue;
                    SlotState d = st.Slots[i];
                    bool closedUp = raws[i].CCC > raws[i].CCO;
                    bool bullMag = StratGrammar.IsNa(d.ExhHighPrice) && !StratGrammar.IsNa(d.PrevMagHigh) && d.MagHighCrossed;
                    bool bearMag = StratGrammar.IsNa(d.ExhLowPrice) && !StratGrammar.IsNa(d.PrevMagLow) && d.MagLowCrossed;
                    bool bullEx = closedUp && (d.ExhHighCrossed || bullMag);
                    bool bearEx = !closedUp && (d.ExhLowCrossed || bearMag);
                    if (closedUp && !bullEx) modDown = false;
                    if (!closedUp && !bearEx) modUp = false;
                }
            }
            bool simple = s.SimpleLabelMode;
            string ftfcText = modUp ? (simple ? "Trend Up" : "FTFC Up") : modDown ? (simple ? "Trend Down" : "FTFC Down") : (simple ? "No Trend" : "Conflict");
            SColor ftfcFg = modUp ? SColor.TvGreen : modDown ? SColor.TvRed : SColor.TvGray;
            SColor ftfcBg = modUp ? SColor.TvGreen.WithTransp(80) : modDown ? SColor.TvRed.WithTransp(80) : SColor.TvGray.WithTransp(80);

            bool hasLead = s.EnableDirectionalFilter && anchorIndex >= 0 && anchorDirection != 0;
            string leadText = hasLead ? "Lead: " + slots[anchorIndex].Label + " " + (anchorDirection == 1 ? "BULL" : "BEAR") : "";
            SColor leadFg = anchorDirection == 1 ? SColor.TvGreen : SColor.TvRed;
            SColor leadBg = anchorDirection == 1 ? SColor.TvGreen.WithTransp(85) : SColor.TvRed.WithTransp(85);

            if (TableMode == TableModeOption.Compact)
            {
                t.Columns = enabledCount;
                List<RCell> row = new List<RCell>();
                for (int i = 5; i >= 0; i--)
                {
                    if (!slots[i].Enabled) continue;
                    SColor bg = dark, fg = SColor.Hex(0x555555);
                    SlotRaw raw = raws[i];
                    SlotResult r = results[i];
                    if (!slots[i].Lower && raw.HasData && r != null)
                    {
                        bool failed;
                        string cc = StratGrammar.DetectBarTypeAndFailed(raw.C1H, raw.C1L, raw.CCH, raw.CCL, raw.CCO, raw.CCC, f2, s.Failed2Method, out failed);
                        if (raw.IsPreview) fg = SColor.TvYellow;
                        else if (CompactCellColor == CompactCellColorMode.BarState)
                        {
                            bg = BarTypeColor(cc);
                            fg = cc != "" ? black : SColor.TvWhite;
                        }
                        else
                        {
                            bool hl = r.SignalInForceHigh || r.SignalInForceLow || (s.Show3Expansions && cc.StartsWith("3"));
                            bg = hl ? BarTypeColor(cc) : dark;
                            fg = hl ? black : SColor.TvWhite;
                        }
                    }
                    row.Add(Cell(slots[i].Label, fg, bg));
                }
                t.Rows.Add(row.ToArray());
            }
            else
            {
                t.Columns = 5;
                SColor hdrFg = SColor.Hex(0x888888), hdrBg = SColor.Hex(0x1A1A1A);
                t.Rows.Add(new[] { Cell("TF", hdrFg, hdrBg), Cell("C2", hdrFg, hdrBg), Cell("C1", hdrFg, hdrBg), Cell("AS", hdrFg, hdrBg), Cell("CC", hdrFg, hdrBg) });
                for (int i = 5; i >= 0; i--)
                {
                    Slot sl = slots[i];
                    if (!sl.Enabled) continue;
                    if (sl.Lower || !raws[i].HasData)
                    {
                        t.Rows.Add(new[] { Cell(sl.Label, SColor.Hex(0x555555), dark), Cell("", SColor.TvGray, black), Cell("", SColor.TvGray, black), Cell("", SColor.TvGray, black), Cell("", SColor.TvGray, black) });
                        continue;
                    }
                    SlotRaw raw = raws[i];
                    SlotResult r = results[i];
                    SlotState d = st.Slots[i];
                    bool fl;
                    string cc = StratGrammar.DetectBarTypeAndFailed(raw.C1H, raw.C1L, raw.CCH, raw.CCL, raw.CCO, raw.CCC, f2, s.Failed2Method, out fl);
                    string c1 = StratGrammar.DetectBarTypeAndFailed(raw.C2H, raw.C2L, raw.C1H, raw.C1L, raw.C1O, raw.C1C, f2, s.Failed2Method, out fl);
                    string c2 = StratGrammar.DetectBarTypeAndFailed(raw.C3H, raw.C3L, raw.C2H, raw.C2L, raw.C2O, raw.C2C, f2, s.Failed2Method, out fl);
                    bool inForce = s.ColorTFWhenInForce && r != null && (r.SignalInForceHigh || r.SignalInForceLow);
                    bool cc3 = s.ColorTFWhenInForce && s.Show3Expansions && cc.StartsWith("3");
                    bool hl = inForce || cc3;
                    string asText = d.C1IsHammer ? "HAM" : d.C1IsShooter ? "SHO" : d.C1IsInside ? "INS" : "";
                    bool unknown = raw.IsPreview;
                    t.Rows.Add(new[]
                    {
                        Cell(sl.Label, hl ? black : SColor.TvWhite, hl ? BarTypeColor(cc) : dark),
                        Cell(c2, BarTypeTextColor(c2), black),
                        Cell(c1, BarTypeTextColor(c1), black),
                        Cell(asText, SColor.TvWhite, black),
                        Cell(unknown ? "?" : cc, unknown ? SColor.TvYellow : cc != "" ? black : SColor.TvWhite, unknown ? black : BarTypeColor(cc))
                    });
                }
            }
            if (s.ShowDominoInTable && dominoBestRun >= s.MinDominoTFs)
                t.Rows.Add(new[] { Cell("Domino: " + dominoBestTFs, black, SColor.TvWhite) });
            t.Rows.Add(new[] { Cell(ftfcText, ftfcFg, ftfcBg) });
            if (hasLead)
                t.Rows.Add(new[] { Cell(leadText, leadFg, leadBg) });
            if (previewNext)
                t.Rows.Add(new[] { Cell("PREVIEW MODE", SColor.TvYellow, SColor.TvYellow.WithTransp(85)) });
            if (shortHistoryText.Length > 0)
                t.Rows.Add(new[] { Cell(shortHistoryText, SColor.TvOrange, black) });
            return t;
        }

        // ---- Debug panel (Pine SECTION 12) ----
        // YES/NO use neutral fills: green and red on this indicator always mean bull and bear.

        private RTable BuildDebug(DebugInfo d)
        {
            RTable t = new RTable { Columns = 4, Position = TablePositionOption.MiddleLeft, Size = TextSizeOption.Small };
            SColor hdr = SColor.Hex(0x333333), rowBg = SColor.Hex(0x1A1A1A), lbl = SColor.Hex(0xAAAAAA);
            string[] left = new string[26];
            bool?[] leftV = new bool?[26];
            string[] right = new string[26];
            object[] rightV = new object[26];
            Action<int, string, bool?> L = (r, name, v) => { left[r] = name; leftV[r] = v; };
            Action<int, string, object> R = (r, name, v) => { right[r] = name; rightV[r] = v; };
            L(0, "DEBUG: " + d.TfLabel, null);
            L(1, "#C1 Bar Type", null); L(2, "c1_was_2u", d.c1_was_2u); L(3, "c1_was_2d", d.c1_was_2d); L(4, "c1_is_3", d.c1_is_3);
            L(5, "#C2 Bar Type", null); L(6, "c2_was_2u", d.c2_was_2u); L(7, "c2_was_2d", d.c2_was_2d); L(8, "c2_was_3", d.c2_was_3);
            L(9, "c2_closed_up", d.c2_closed_up); L(10, "c2_closed_down", d.c2_closed_down);
            L(11, "#CC Bar Type", null); L(12, "cc_is_2u", d.cc_is_2u); L(13, "cc_is_2d", d.cc_is_2d); L(14, "cc_is_3", d.cc_is_3);
            L(15, "cc_broke_high", d.cc_broke_high); L(16, "cc_broke_low", d.cc_broke_low);
            L(17, "#Patterns", null); L(18, "isHammer", d.isHammer); L(19, "isShooter", d.isShooter); L(20, "isInside", d.isInside);
            L(21, "#Failed 2s", null); L(22, "is_f2u", d.is_f2u); L(23, "is_f2d", d.is_f2d); L(24, "pre_f2u", d.pre_f2u); L(25, "pre_f2d", d.pre_f2d);
            R(0, "#In Force", null); R(1, "sif_high", d.sif_high); R(2, "sif_low", d.sif_low);
            R(3, "#Terms  H / L", null);
            R(4, "Inside Rev", new[] { d.t_insideRev_h, d.t_insideRev_l }); R(5, "Inside Cont", new[] { d.t_insideCont_h, d.t_insideCont_l });
            R(6, "Ham/Sho", new[] { d.t_hamSho_h, d.t_hamSho_l }); R(7, "2-2 Rev", new[] { d.t_rev22_h, d.t_rev22_l });
            R(8, "2-2 Cont", new[] { d.t_cont22_h, d.t_cont22_l }); R(9, "3-2 Exp", new[] { d.t_exp32_h, d.t_exp32_l });
            R(10, "Failing 2", new[] { d.t_f2_h, d.t_f2_l });
            R(11, "#Gates", null); R(12, "hammer_shows", d.hammer_shows); R(13, "shooter_shows", d.shooter_shows);
            R(14, "ftfc_up", d.ftfc_up); R(15, "ftfc_down", d.ftfc_down);
            R(16, "#Draw Decisions", null); R(17, "drawHigh", d.drawHigh); R(18, "drawLow", d.drawLow);
            R(19, "drawMagHigh", d.drawMagHigh); R(20, "drawMagLow", d.drawMagLow); R(21, "drawExhHigh", d.drawExhHigh); R(22, "drawExhLow", d.drawExhLow);
            R(23, "#Magnitude", null); R(24, "force_mag", new[] { d.force_mag_high, d.force_mag_low }); R(25, "mag_ftfc", new[] { d.mag_high_passes_ftfc, d.mag_low_passes_ftfc });
            SColor yesBg = SColor.Hex(0x5A6A7E), noBg = SColor.Hex(0x262626), yesFg = SColor.TvWhite, noFg = SColor.TvGray;
            for (int r = 0; r < 26; r++)
            {
                RCell[] row = new RCell[4];
                // left pane
                if (r == 0) { row[0] = Cell(left[0], SColor.TvOrange, hdr); row[1] = Cell("", SColor.TvWhite, hdr); }
                else if (left[r].StartsWith("#")) { row[0] = Cell(left[r].Substring(1), SColor.TvWhite, hdr); row[1] = Cell("", SColor.TvWhite, hdr); }
                else
                {
                    bool v = leftV[r] == true;
                    row[0] = Cell(left[r], lbl, rowBg);
                    row[1] = Cell(v ? "YES" : "NO", v ? yesFg : noFg, v ? yesBg : noBg);
                }
                // right pane
                if (right[r].StartsWith("#")) { row[2] = Cell(right[r].Substring(1), SColor.TvWhite, hdr); row[3] = Cell("", SColor.TvWhite, hdr); }
                else if (rightV[r] is bool[])
                {
                    bool[] hl = (bool[])rightV[r];
                    bool any = hl[0] || hl[1];
                    row[2] = Cell(right[r], lbl, rowBg);
                    row[3] = Cell((hl[0] ? "Y" : "-") + " / " + (hl[1] ? "Y" : "-"), any ? yesFg : noFg, any ? yesBg : noBg);
                }
                else
                {
                    bool v = rightV[r] is bool && (bool)rightV[r];
                    row[2] = Cell(right[r], lbl, rowBg);
                    row[3] = Cell(v ? "YES" : "NO", v ? yesFg : noFg, v ? yesBg : noBg);
                }
                t.Rows.Add(row);
            }
            return t;
        }

        // ====================================================================
        // RENDERING
        // ====================================================================

        public override void OnRenderTargetChanged()
        {
            DisposeDx();
        }

        private void DisposeDx()
        {
            foreach (SharpDX.Direct2D1.SolidColorBrush b in dxBrushes.Values)
                if (b != null && !b.IsDisposed) b.Dispose();
            dxBrushes.Clear();
            if (dashStyle != null && !dashStyle.IsDisposed) dashStyle.Dispose();
            if (dotStyle != null && !dotStyle.IsDisposed) dotStyle.Dispose();
            dashStyle = null;
            dotStyle = null;
            foreach (SharpDX.DirectWrite.TextLayout l in barNumLayouts.Values)
                if (l != null && !l.IsDisposed) l.Dispose();
            barNumLayouts.Clear();
            if (barNumFormat != null && !barNumFormat.IsDisposed) barNumFormat.Dispose();
            barNumFormat = null;
        }

        private SharpDX.Direct2D1.SolidColorBrush Dx(SColor c)
        {
            SharpDX.Direct2D1.SolidColorBrush b;
            if (!dxBrushes.TryGetValue(c.Key, out b) || b == null || b.IsDisposed)
            {
                b = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
                dxBrushes[c.Key] = b;
            }
            return b;
        }

        private SharpDX.Direct2D1.StrokeStyle Stroke(LineStyleCode style)
        {
            if (style == LineStyleCode.Solid) return null;
            if (dashStyle == null)
            {
                dashStyle = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dash, MiterLimit = 10f });
                // Direct2D's dot is a zero-length dash: with the default flat caps it draws nothing.
                // Square caps (not round: lines draw aliased) give each dot its width.
                dotStyle = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dot, DashCap = SharpDX.Direct2D1.CapStyle.Square, MiterLimit = 10f });
            }
            return style == LineStyleCode.Dashed ? dashStyle : dotStyle;
        }

        private static float FontSize(TextSizeOption s)
        {
            switch (s)
            {
                case TextSizeOption.Tiny: return 9f;
                case TextSizeOption.Normal: return 13f;
                case TextSizeOption.Large: return 16f;
                default: return 11f;
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (RenderTarget == null || ChartBars == null) return;
            if (settings != null && settings.ShowBarNumbers)
                DrawBarNumbers(chartControl, chartScale);
            RenderModel m = model;
            if (m == null) return;
            SharpDX.Direct2D1.AntialiasMode oldAa = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            try
            {
                int last = Math.Min(m.LastBar, ChartBars.Count - 1);
                float xLast = chartControl.GetXByBarIndex(ChartBars, last);
                float barDist = (float)chartControl.Properties.BarDistance;
                Func<double, float> X = u => u <= last ? chartControl.GetXByBarIndex(ChartBars, (int)Math.Max(0, Math.Round(u))) : xLast + (float)((u - last) * barDist);
                float panelRight = ChartPanel.X + ChartPanel.W;

                foreach (RBox b in m.Boxes)
                {
                    float x1 = X(b.X1), x2 = X(b.X2);
                    float y1 = chartScale.GetYByValue(b.Top), y2 = chartScale.GetYByValue(b.Bottom);
                    SharpDX.RectangleF rect = new SharpDX.RectangleF(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
                    RenderTarget.FillRectangle(rect, Dx(b.Fill));
                    RenderTarget.DrawRectangle(rect, Dx(b.Border), 1f);
                }
                foreach (RLine l in m.Lines)
                {
                    float y = chartScale.GetYByValue(l.Price);
                    SharpDX.Direct2D1.StrokeStyle ss = Stroke(l.Style);
                    SharpDX.Vector2 p1 = new SharpDX.Vector2(X(l.X1), y), p2 = new SharpDX.Vector2(X(l.X2), y);
                    if (ss == null) RenderTarget.DrawLine(p1, p2, Dx(l.Color), l.Width);
                    else RenderTarget.DrawLine(p1, p2, Dx(l.Color), l.Width, ss);
                }
                RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
                using (SharpDX.DirectWrite.TextFormat tf = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", FontSize(LabelTextSize)))
                {
                    foreach (RLabel lb in m.Labels)
                    {
                        using (SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, lb.Text, tf, 2000, 200))
                        {
                            float w = layout.Metrics.Width + 8, h = layout.Metrics.Height + 4;
                            float x = X(lb.X);
                            if (ClampTimelineLabels && x + w > panelRight) x = panelRight - w - 2;
                            float y = chartScale.GetYByValue(lb.Price) - h / 2;
                            SharpDX.RectangleF rect = new SharpDX.RectangleF(x, y, w, h);
                            RenderTarget.FillRectangle(rect, Dx(lb.Bg));
                            RenderTarget.DrawTextLayout(new SharpDX.Vector2(x + 4, y + 2), layout, Dx(lb.Fg));
                        }
                    }
                }
                if (m.Table != null) DrawTable(m.Table, true);
                if (m.Debug != null) DrawTable(m.Debug, false);
            }
            finally
            {
                RenderTarget.AntialiasMode = oldAa;
            }
        }

        // Pine SECTION 15 (BARNUM-1). Drawn per visible candle at render time: the number reads
        // only the candle's own OHLC and the prior candle's high and low, so nothing needs storing.
        private void DrawBarNumbers(ChartControl chartControl, ChartScale chartScale)
        {
            Bars b0 = BarsArray[0];
            int count = b0.Count;
            int from = Math.Max(1, ChartBars.FromIndex), to = Math.Min(ChartBars.ToIndex, count - 1);
            if (settings.BarNumBarsShown > 0)
                from = Math.Max(from, count - settings.BarNumBarsShown);
            if (to < from) return;
            const float gap = 4f;
            // Only a handful of strings exist (1, 2, 2u, 3d, F2u...), so their layouts are cached.
            if (barNumFormat == null || barNumFormat.IsDisposed)
            {
                barNumFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", FontSize(BarNumTextSize));
                barNumFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
            }
            SharpDX.Direct2D1.AntialiasMode oldAa = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
            try
            {
                for (int i = from; i <= to; i++)
                {
                    bool failed;
                    string type = StratGrammar.DetectBarTypeAndFailed(b0.GetHigh(i - 1), b0.GetLow(i - 1), b0.GetHigh(i), b0.GetLow(i),
                        b0.GetOpen(i), b0.GetClose(i), settings.EnableFailed2Detection, settings.Failed2Method, out failed);
                    BarNumberToken t = StratBarNumbers.Token(settings, type, i == formingBar);
                    if (!t.Visible) continue;
                    SharpDX.DirectWrite.TextLayout lay;
                    if (!barNumLayouts.TryGetValue(t.Text, out lay) || lay == null || lay.IsDisposed)
                    {
                        lay = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, t.Text, barNumFormat, 60, 40);
                        barNumLayouts[t.Text] = lay;
                    }
                    float h = lay.Metrics.Height;
                    float x = chartControl.GetXByBarIndex(ChartBars, i) - 30;
                    float y = t.Above ? chartScale.GetYByValue(b0.GetHigh(i)) - gap - h : chartScale.GetYByValue(b0.GetLow(i)) + gap;
                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), lay, Dx(t.Color));
                }
            }
            finally
            {
                RenderTarget.AntialiasMode = oldAa;
            }
        }

        private void DrawTable(RTable t, bool framed)
        {
            if (t.Rows.Count == 0) return;
            float size = FontSize(t.Size);
            const float padX = 6f, padY = 3f, margin = 10f;
            using (SharpDX.DirectWrite.TextFormat tf = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", size))
            {
                tf.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                float[] colW = new float[t.Columns];
                float rowH = 0, spanW = 0;
                foreach (RCell[] row in t.Rows)
                {
                    for (int c = 0; c < row.Length; c++)
                    {
                        using (SharpDX.DirectWrite.TextLayout lay = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, row[c].Text.Length == 0 ? " " : row[c].Text, tf, 1000, 100))
                        {
                            float w = lay.Metrics.Width + 2 * padX;
                            rowH = Math.Max(rowH, lay.Metrics.Height + 2 * padY);
                            if (row.Length == 1 && t.Columns > 1) spanW = Math.Max(spanW, w);
                            else colW[c] = Math.Max(colW[c], w);
                        }
                    }
                }
                float totalW = 0;
                foreach (float w in colW) totalW += w;
                if (spanW > totalW)
                {
                    float extra = (spanW - totalW) / t.Columns;
                    for (int c = 0; c < t.Columns; c++) colW[c] += extra;
                    totalW = spanW;
                }
                float totalH = rowH * t.Rows.Count;
                float left = ChartPanel.X, top = ChartPanel.Y, width = ChartPanel.W, height = ChartPanel.H;
                float x0, y0;
                switch (t.Position)
                {
                    case TablePositionOption.TopLeft: x0 = left + margin; y0 = top + margin; break;
                    case TablePositionOption.TopCenter: x0 = left + (width - totalW) / 2; y0 = top + margin; break;
                    case TablePositionOption.MiddleLeft: x0 = left + margin; y0 = top + (height - totalH) / 2; break;
                    case TablePositionOption.MiddleCenter: x0 = left + (width - totalW) / 2; y0 = top + (height - totalH) / 2; break;
                    case TablePositionOption.MiddleRight: x0 = left + width - totalW - margin; y0 = top + (height - totalH) / 2; break;
                    case TablePositionOption.BottomLeft: x0 = left + margin; y0 = top + height - totalH - margin; break;
                    case TablePositionOption.BottomCenter: x0 = left + (width - totalW) / 2; y0 = top + height - totalH - margin; break;
                    case TablePositionOption.BottomRight: x0 = left + width - totalW - margin; y0 = top + height - totalH - margin; break;
                    default: x0 = left + width - totalW - margin; y0 = top + margin; break;
                }
                // The Pine tables have an opaque background that translucent cells (FTFC, Lead,
                // PREVIEW MODE, empty CC) blend over.
                RenderTarget.FillRectangle(new SharpDX.RectangleF(x0, y0, totalW, totalH), Dx(framed ? SColor.TvBlack : SColor.Hex(0x0D0D0D)));
                SharpDX.Direct2D1.SolidColorBrush border = Dx(SColor.Hex(0x333333));
                float y = y0;
                foreach (RCell[] row in t.Rows)
                {
                    float x = x0;
                    for (int c = 0; c < row.Length; c++)
                    {
                        float w = row.Length == 1 ? totalW : colW[c];
                        SharpDX.RectangleF rect = new SharpDX.RectangleF(x, y, w, rowH);
                        RenderTarget.FillRectangle(rect, Dx(row[c].Bg.IsNone ? SColor.TvBlack : row[c].Bg));
                        RenderTarget.DrawRectangle(rect, border, 1f);
                        if (row[c].Text.Length > 0)
                            using (SharpDX.DirectWrite.TextLayout lay = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, row[c].Text, tf, w, rowH))
                            {
                                lay.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                                RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), lay, Dx(row[c].Fg));
                            }
                        x += w;
                    }
                    y += rowH;
                }
                SharpDX.RectangleF frame = new SharpDX.RectangleF(x0, y0, totalW, totalH);
                RenderTarget.DrawRectangle(frame, Dx(framed ? SColor.TvGray : SColor.TvOrange), framed ? 1f : 2f);
            }
        }

        // ====================================================================
        // SETTINGS
        // ====================================================================

        private static SColor ToS(Brush b, SColor fallback)
        {
            SolidColorBrush sb = b as SolidColorBrush;
            if (sb == null) return fallback;
            double op = Math.Max(0.0, Math.Min(1.0, sb.Opacity));
            return new SColor(sb.Color.R, sb.Color.G, sb.Color.B, (byte)Math.Round(sb.Color.A * op));
        }

        private StratSettings BuildSettings()
        {
            StratSettings s = new StratSettings();
            s.LabelStyle = LabelStyle;
            s.ShowInsideReversals = ShowInsideReversals; s.InsideRevOnlyMomo = InsideRevOnlyMomo; s.InsideRevRequireFTFC = InsideRevRequireFTFC;
            s.ShowAllReversals = ShowAllReversals; s.ReversalsRequireActionable = ReversalsRequireActionable; s.ReversalsRequireC1F2 = ReversalsRequireC1F2; s.ReversalsRequireFTFC = ReversalsRequireFTFC;
            s.ShowInsideContinuations = ShowInsideContinuations; s.InsideContOnlyMomo = InsideContOnlyMomo; s.InsideContRequireFTFC = InsideContRequireFTFC;
            s.ShowContinuations = ShowContinuations; s.TwoTwoContOnlyMomo = TwoTwoContOnlyMomo; s.ContinuationsRequireFTFC = ContinuationsRequireFTFC;
            s.ShowP3 = ShowFailing2s; s.P3RequireFTFC = Failing2sRequireFTFC;
            s.ShowRangeExpansions = ShowRangeExpansions; s.RangeExpOnlyMomo = RangeExpOnlyMomo; s.RangeExpansionsRequireFTFC = RangeExpansionsRequireFTFC;
            s.Show3Expansions = Show3Expansions; s.ThreeExpRequireFTFC = ThreeExpRequireFTFC;
            s.LogicType = HammerLogic; s.OnlyColor = MatchCandleColor;
            s.MinDominoTFs = MinDominoTFs; s.ShowDominoInTable = ShowDominoInTable;
            s.EnableDirectionalFilter = OnlyFollowTheLead;
            s.ShowMagnitude = ShowMagnitude; s.MagOnlyWhenInForce = MagOnlyWhenInForce;
            s.ShowExhaustion = ShowExhaustion; s.ExhOnlyWhenInForce = ExhOnlyWhenInForce; s.ExhRequiresMagHit = ExhRequiresMagHit;
            s.ShowTakeActionWindows = ShowTakeActionWindows; s.TawOnlyWhenInForce = TawOnlyWhenInForce; s.TawExtendToExhaustion = TawExtendToExhaustion; s.TawExhOnlyWhenInForce = TawExhOnlyWhenInForce;
            s.TawFillOpacity = TawFillOpacity; s.TawBorderOpacity = TawBorderOpacity;
            s.ShowStopLevels = ShowStopLevels; s.StopReference = StopReference; s.StopSmallestOnly = StopSmallestOnly; s.StopBEatMag = StopBEatMag; s.StopBEatExh = StopBEatExh;
            s.StopColor = StopColor; s.ColorStop = ToS(StopCustomColor, SColor.TvWhite);
            s.UseDashSeparator = UseDashSeparator;
            s.ShowTimelineLabels = ShowTimelineLabels; s.LabelOffset = LabelOffset; s.ShowFloatingLabels = ShowFloatingLabels; s.FloatingLabelOffset = FloatingLabelOffset;
            s.ShowPriceInLabel = ShowPriceInLabel; s.ShowHighLowInLabel = ShowHighLowInLabel;
            s.LabelBackgroundColor = ToS(LabelBackground, SColor.Hex(0x2E2E2E)); s.LabelBackgroundTransparency = LabelBackgroundTransparency;
            s.ColorBullishActive = ToS(BullSignalColor, SColor.TvGreen); s.ColorBullishHit = ToS(BullCrossedColor, SColor.Hex(0x9C9C9C));
            s.ColorBullishPotential = ToS(BullInsideColor, SColor.Hex(0xFFEB3B)); s.ColorBullish3Bar = ToS(BullOutsideColor, SColor.Hex(0x089981)); s.ColorBullishP3 = ToS(BullReclaimColor, SColor.Hex(0x81C784));
            s.ColorBearishActive = ToS(BearSignalColor, SColor.TvRed); s.ColorBearishHit = ToS(BearCrossedColor, SColor.Hex(0x4A4A4A));
            s.ColorBearishPotential = ToS(BearInsideColor, SColor.Hex(0xFF9800)); s.ColorBearish3Bar = ToS(BearOutsideColor, SColor.Hex(0xE91E63)); s.ColorBearishP3 = ToS(BearReclaimColor, SColor.Hex(0xF77C80));
            s.AlertTF = new[] { AlertIncludeTF1, AlertIncludeTF2, AlertIncludeTF3, AlertIncludeTF4, AlertIncludeTF5, AlertIncludeTF6 };
            s.AlertShowFTFC = AlertShowFTFC; s.AlertShowTrigger = AlertShowTrigger; s.AlertShowMagnitude = AlertShowMagnitude; s.AlertShowExhaustion = AlertShowExhaustion; s.AlertShowStop = AlertShowStop;
            s.ExhDisablesInForce = ExhDisablesInForce; s.ExhDisablesFTFC = ExhDisablesFTFC;
            s.Show3ExpMagnitude = Show3ExpMagnitude; s.Show3ExpExhaustion = Show3ExpExhaustion;
            s.EnableFailed2Detection = EnableFailed2Detection; s.Failed2Method = Failed2DetectionMethod; s.ShowF2OpenLine = ShowF2OpenLine;
            s.ColorTFWhenInForce = ColorTFWhenInForce;
            s.ShowP3TakeActionWindows = TawIncludeFailing2s; s.Show3ExpTakeActionWindows = TawIncludeOutsideBars;
            s.EnableDominoAlerts = IncludeDominoInConsolidated;
            s.BarColorMode = BarColorMode;
            s.PaintInsideBars = PaintInsideBars; s.Paint2Bars = Paint2Bars; s.PaintOutsideBars = PaintOutsideBars; s.PaintFailedBars = PaintFailedBars;
            s.ColorFtfcUpBar = ToS(FtfcUpColor, SColor.Hex(0x4CAF50)); s.ColorFtfcDownBar = ToS(FtfcDownColor, SColor.Hex(0xF23645)); s.ColorFtfcConflictBar = ToS(FtfcConflictColor, SColor.Hex(0x808080));
            s.PaintFtfcConflict = PaintFtfcConflict; s.PaintFtfcTransitions = HighlightFailing2Flips;
            s.ShowBarNumbers = ShowBarNumbers; s.BarNumNotation = BarNumNotation; s.BarNumEmphasis = BarNumEmphasis; s.BarNumPosition = BarNumPosition;
            s.BarNumColorMode = BarNumColorMode; s.BarNumColor = ToS(BarNumColor, SColor.Hex(0x9598A1)); s.BarNumBarsShown = BarNumBarsShown;
            return s;
        }

        private static SolidColorBrush Hex(int rgb)
        {
            SolidColorBrush b = new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            b.Freeze();
            return b;
        }

        private void SetInputDefaults()
        {
            Preset = StratPreset.TheStratClassic;
            LabelStyle = StratLabelStyle.TheStrat;
            Tf1Enabled = false; Tf1 = StratTimeframe.Min15; Tf1LineWidth = 1; Tf1ShowOpen = false;
            Tf2Enabled = false; Tf2 = StratTimeframe.Min30; Tf2LineWidth = 1; Tf2ShowOpen = false;
            Tf3Enabled = false; Tf3 = StratTimeframe.Hour1; Tf3LineWidth = 1; Tf3ShowOpen = false;
            Tf4Enabled = false; Tf4 = StratTimeframe.Day; Tf4LineWidth = 2; Tf4ShowOpen = false;
            Tf5Enabled = false; Tf5 = StratTimeframe.Week; Tf5LineWidth = 3; Tf5ShowOpen = false;
            Tf6Enabled = false; Tf6 = StratTimeframe.Month; Tf6LineWidth = 4; Tf6ShowOpen = false;
            ShowInsideReversals = true; InsideRevOnlyMomo = false; InsideRevRequireFTFC = false;
            ShowAllReversals = true; ReversalsRequireActionable = false; ReversalsRequireC1F2 = false; ReversalsRequireFTFC = false;
            ShowInsideContinuations = true; InsideContOnlyMomo = false; InsideContRequireFTFC = false;
            ShowContinuations = false; TwoTwoContOnlyMomo = false; ContinuationsRequireFTFC = false;
            ShowFailing2s = false; Failing2sRequireFTFC = false;
            ShowRangeExpansions = false; RangeExpOnlyMomo = false; RangeExpansionsRequireFTFC = false;
            Show3Expansions = false; ThreeExpRequireFTFC = false;
            HammerLogic = HammerDefinition.Broad; MatchCandleColor = false;
            MinDominoTFs = 2; ShowDominoInTable = true;
            OnlyFollowTheLead = false;
            ShowMagnitude = true; MagOnlyWhenInForce = false; ShowExhaustion = true; ExhOnlyWhenInForce = false; ExhRequiresMagHit = false;
            ShowTakeActionWindows = true; TawOnlyWhenInForce = true; TawExtendToExhaustion = true; TawExhOnlyWhenInForce = true; TawFillOpacity = 8; TawBorderOpacity = 30;
            ShowStopLevels = false; StopReference = StopReferenceMode.CC; StopSmallestOnly = false; StopBEatMag = false; StopBEatExh = false;
            StopColor = StopColorMode.OppositeSignal; StopCustomColor = Hex(0xFFFFFF);
            PreviewMode = PreviewModeOption.Auto;
            ShowDataTable = true; TableMode = TableModeOption.Full; CompactCellColor = CompactCellColorMode.BarState; TablePosition = TablePositionOption.TopRight; TableTextSize = TextSizeOption.Small; UseDashSeparator = true;
            ShowTimelineLabels = true; LabelOffset = 0; ShowFloatingLabels = false; FloatingLabelOffset = 1; ShowPriceInLabel = false; ShowHighLowInLabel = false;
            LabelTextSize = TextSizeOption.Small; LabelBackground = Hex(0x2E2E2E); LabelBackgroundTransparency = 20; ClampTimelineLabels = true;
            BarColorMode = BarColorModeOption.Off; PaintInsideBars = true; Paint2Bars = true; PaintOutsideBars = true; PaintFailedBars = true;
            FtfcUpColor = Hex(0x4CAF50); FtfcDownColor = Hex(0xF23645); FtfcConflictColor = Hex(0x808080); PaintFtfcConflict = true; HighlightFailing2Flips = true;
            ShowBarNumbers = false; BarNumNotation = BarNumNotationOption.Full; BarNumEmphasis = BarNumEmphasisOption.Tiered; BarNumPosition = BarNumPositionOption.Below;
            BarNumColorMode = BarNumColorModeOption.BarTypePalette; BarNumColor = Hex(0x9598A1); BarNumBarsShown = 0; BarNumTextSize = TextSizeOption.Small;
            BullSignalColor = Hex(0x4CAF50); BullCrossedColor = Hex(0x9C9C9C); BullInsideColor = Hex(0xFFEB3B); BullOutsideColor = Hex(0x089981); BullReclaimColor = Hex(0x81C784);
            BearSignalColor = Hex(0xF23645); BearCrossedColor = Hex(0x4A4A4A); BearInsideColor = Hex(0xFF9800); BearOutsideColor = Hex(0xE91E63); BearReclaimColor = Hex(0xF77C80);
            AlertIncludeTF1 = AlertIncludeTF2 = AlertIncludeTF3 = AlertIncludeTF4 = AlertIncludeTF5 = AlertIncludeTF6 = true;
            AlertShowFTFC = false; AlertShowTrigger = false; AlertShowMagnitude = false; AlertShowExhaustion = false; AlertShowStop = false;
            EnableAlerts = false; AlertSound = "Alert1.wav"; AlertSignalInForceAny = true;
            AlertTF1InForce = AlertTF2InForce = AlertTF3InForce = AlertTF4InForce = AlertTF5InForce = AlertTF6InForce = false;
            AlertBullishInForce = false; AlertBearishInForce = false; AlertNewPotential = false; AlertDominoSetup = false;
            AlertFtfcShifted = false; AlertFtfcUp = false; AlertFtfcDown = false; AlertFtfcConflict = false;
            ExhDisablesInForce = false; ExhDisablesFTFC = false;
            Show3ExpMagnitude = true; Show3ExpExhaustion = true;
            EnableFailed2Detection = true; Failed2DetectionMethod = F2Method.Reclaim; ShowF2OpenLine = false;
            ColorTFWhenInForce = true;
            TawIncludeFailing2s = true; TawIncludeOutsideBars = true;
            IncludeDominoInConsolidated = false;
            HtfTradingHours = ""; MaxDailyBars = 7000;
            ShowDebugPanel = false; DebugTimeframe = DebugSlot.TF1;
        }

        #region Properties

        // ---- 01. Timeframe Preset ----
        [Display(Name = "Preset", Order = 1, GroupName = "01. Timeframe Preset", Description = "Select a timeframe preset to auto-configure timeframes and line widths. When set to anything other than Custom, individual Timeframe 1-6 settings are ignored. You must select Custom to use your own timeframe configuration.")]
        [TypeConverter(typeof(PresetConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public StratPreset Preset { get; set; }

        [Display(Name = "Label Style", Order = 2, GroupName = "01. Timeframe Preset", Description = "TheStrat: standard Strat combo notation (e.g. 2d-1-2u HAM). Universal: plain-language labels - REV (reversal), CONT (continuation), INS (inside), OUT (outside), EXP (expansion), plus FAILING when a breakout is failing - with an up/down arrow for direction and a diamond for hammer/shooter confidence. Designed for traders who are new to TheStrat or prefer a simplified view.")]
        [TypeConverter(typeof(LabelStyleConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public StratLabelStyle LabelStyle { get; set; }

        // ---- 02. Timeframes (Custom preset only) ----
        [Display(Name = "Timeframe 1", Order = 10, GroupName = "02. Timeframes (Custom preset)")] public bool Tf1Enabled { get; set; }
        [Display(Name = "Timeframe 1 TF", Order = 11, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(TimeframeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")] public StratTimeframe Tf1 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 1 Width", Order = 12, GroupName = "02. Timeframes (Custom preset)")] public int Tf1LineWidth { get; set; }
        [Display(Name = "Timeframe 1 Open", Order = 13, GroupName = "02. Timeframes (Custom preset)")] public bool Tf1ShowOpen { get; set; }
        [Display(Name = "Timeframe 2", Order = 20, GroupName = "02. Timeframes (Custom preset)")] public bool Tf2Enabled { get; set; }
        [Display(Name = "Timeframe 2 TF", Order = 21, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(TimeframeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")] public StratTimeframe Tf2 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 2 Width", Order = 22, GroupName = "02. Timeframes (Custom preset)")] public int Tf2LineWidth { get; set; }
        [Display(Name = "Timeframe 2 Open", Order = 23, GroupName = "02. Timeframes (Custom preset)")] public bool Tf2ShowOpen { get; set; }
        [Display(Name = "Timeframe 3", Order = 30, GroupName = "02. Timeframes (Custom preset)")] public bool Tf3Enabled { get; set; }
        [Display(Name = "Timeframe 3 TF", Order = 31, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(TimeframeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")] public StratTimeframe Tf3 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 3 Width", Order = 32, GroupName = "02. Timeframes (Custom preset)")] public int Tf3LineWidth { get; set; }
        [Display(Name = "Timeframe 3 Open", Order = 33, GroupName = "02. Timeframes (Custom preset)")] public bool Tf3ShowOpen { get; set; }
        [Display(Name = "Timeframe 4", Order = 40, GroupName = "02. Timeframes (Custom preset)")] public bool Tf4Enabled { get; set; }
        [Display(Name = "Timeframe 4 TF", Order = 41, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(TimeframeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")] public StratTimeframe Tf4 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 4 Width", Order = 42, GroupName = "02. Timeframes (Custom preset)")] public int Tf4LineWidth { get; set; }
        [Display(Name = "Timeframe 4 Open", Order = 43, GroupName = "02. Timeframes (Custom preset)")] public bool Tf4ShowOpen { get; set; }
        [Display(Name = "Timeframe 5", Order = 50, GroupName = "02. Timeframes (Custom preset)")] public bool Tf5Enabled { get; set; }
        [Display(Name = "Timeframe 5 TF", Order = 51, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(TimeframeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")] public StratTimeframe Tf5 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 5 Width", Order = 52, GroupName = "02. Timeframes (Custom preset)")] public int Tf5LineWidth { get; set; }
        [Display(Name = "Timeframe 5 Open", Order = 53, GroupName = "02. Timeframes (Custom preset)")] public bool Tf5ShowOpen { get; set; }
        [Display(Name = "Timeframe 6", Order = 60, GroupName = "02. Timeframes (Custom preset)")] public bool Tf6Enabled { get; set; }
        [Display(Name = "Timeframe 6 TF", Order = 61, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(TimeframeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")] public StratTimeframe Tf6 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 6 Width", Order = 62, GroupName = "02. Timeframes (Custom preset)")] public int Tf6LineWidth { get; set; }
        [Display(Name = "Timeframe 6 Open", Order = 63, GroupName = "02. Timeframes (Custom preset)")] public bool Tf6ShowOpen { get; set; }

        // ---- 03. Signals - Reversals ----
        [Display(Name = "Inside Reversals", Order = 1, GroupName = "03. Signals - Reversals", Description = "C1 is inside, CC breaks opposite C2's direction (e.g. 2d-1-2u). The bread-and-butter Strat pattern: price consolidated then reversed.")] public bool ShowInsideReversals { get; set; }
        [Display(Name = "  Inside Reversals: HAM/SHO", Order = 2, GroupName = "03. Signals - Reversals", Description = "HAM/SHO filter: Require C1 to be a hammer or shooter. These candles tested one side and got rejected, adding conviction that the breakout direction has structural backing.")] public bool InsideRevOnlyMomo { get; set; }
        [Display(Name = "  Inside Reversals: FTFC", Order = 3, GroupName = "03. Signals - Reversals", Description = "FTFC filter: Require all monitored timeframes to agree on direction. Removes setups that fight the broader structural alignment, keeping only those swimming with the current.")] public bool InsideRevRequireFTFC { get; set; }
        [Display(Name = "2-2 Reversals", Order = 4, GroupName = "03. Signals - Reversals", Description = "Direct reversal with no inside bar pause (e.g. 2d-2u). CC immediately breaks opposite C1. Faster signal but less coiled energy than inside reversals.")] public bool ShowAllReversals { get; set; }
        [Display(Name = "  2-2 Reversals: HAM/SHO", Order = 5, GroupName = "03. Signals - Reversals", Description = "HAM/SHO filter: Require C1 to be a hammer or shooter. These candles tested one side and got rejected, adding conviction that the breakout direction has structural backing.")] public bool ReversalsRequireActionable { get; set; }
        [Display(Name = "  2-2 Reversals: F2", Order = 6, GroupName = "03. Signals - Reversals", Description = "F2 filter: Require C1 to be a Failed 2 itself (e.g. F2d-2u). Adds structural confluence: the prior bar already reversed a failed breakout, so trapped traders from that failure fuel the current move.")] public bool ReversalsRequireC1F2 { get; set; }
        [Display(Name = "  2-2 Reversals: FTFC", Order = 7, GroupName = "03. Signals - Reversals", Description = "FTFC filter: Require all monitored timeframes to agree on direction. Removes setups that fight the broader structural alignment, keeping only those swimming with the current.")] public bool ReversalsRequireFTFC { get; set; }

        // ---- 04. Signals - Continuations ----
        [Display(Name = "Inside Continuations", Order = 1, GroupName = "04. Signals - Continuations", Description = "C1 is inside, CC breaks same direction as C2 (e.g. 2u-1-2u). Price paused to consolidate then continued the prior move. Trend continuation with a pullback.")] public bool ShowInsideContinuations { get; set; }
        [Display(Name = "  Inside Continuations: HAM/SHO", Order = 2, GroupName = "04. Signals - Continuations", Description = "HAM/SHO filter: Require C1 to be a hammer or shooter. These candles tested one side and got rejected, adding conviction that the breakout direction has structural backing.")] public bool InsideContOnlyMomo { get; set; }
        [Display(Name = "  Inside Continuations: FTFC", Order = 3, GroupName = "04. Signals - Continuations", Description = "FTFC filter: Require all monitored timeframes to agree on direction. Removes setups that fight the broader structural alignment, keeping only those swimming with the current.")] public bool InsideContRequireFTFC { get; set; }
        [Display(Name = "2-2 Continuations", Order = 4, GroupName = "04. Signals - Continuations", Description = "Sustained momentum with no pause (e.g. 2u-2u). CC keeps pushing in C1's direction. Can be noisy without filters since any two trending bars qualify.")] public bool ShowContinuations { get; set; }
        [Display(Name = "  2-2 Continuations: HAM/SHO", Order = 5, GroupName = "04. Signals - Continuations", Description = "HAM/SHO filter: Require C1 to be a hammer or shooter. These candles tested one side and got rejected, adding conviction that the breakout direction has structural backing.")] public bool TwoTwoContOnlyMomo { get; set; }
        [Display(Name = "  2-2 Continuations: FTFC", Order = 6, GroupName = "04. Signals - Continuations", Description = "FTFC filter: Require all monitored timeframes to agree on direction. Removes setups that fight the broader structural alignment, keeping only those swimming with the current.")] public bool ContinuationsRequireFTFC { get; set; }

        // ---- 05. Signals - Reclaims ----
        [Display(Name = "Failing 2s (Range Reclaims)", Order = 1, GroupName = "05. Signals - Reclaims", Description = "Show Range Reclaim signals where price reverses after breaking out. Also known as Failing 2s, Potential 3, or 1-bar Rev Strat.")] public bool ShowFailing2s { get; set; }
        [Display(Name = "  Failing 2s: FTFC", Order = 2, GroupName = "05. Signals - Reclaims", Description = "FTFC filter: Require all monitored timeframes to agree on direction. Removes setups that fight the broader structural alignment, keeping only those swimming with the current.")] public bool Failing2sRequireFTFC { get; set; }

        // ---- 06. Signals - Expansions ----
        [Display(Name = "3-2 Expansions", Order = 1, GroupName = "06. Signals - Expansions", Description = "C1 was a 3-bar (outside bar), CC commits to one direction (e.g. 3u-2u). After C1 took both sides, CC resolves the indecision. Often powerful when the 3-bar shook out weak hands.")] public bool ShowRangeExpansions { get; set; }
        [Display(Name = "  3-2 Expansions: HAM/SHO", Order = 2, GroupName = "06. Signals - Expansions", Description = "HAM/SHO filter: Require C1 to be a hammer or shooter. A 3-bar that also formed a hammer/shooter tested and rejected one side despite the range expansion, adding directional conviction.")] public bool RangeExpOnlyMomo { get; set; }
        [Display(Name = "  3-2 Expansions: FTFC", Order = 3, GroupName = "06. Signals - Expansions", Description = "FTFC filter: Require all monitored timeframes to agree on direction. Removes setups that fight the broader structural alignment, keeping only those swimming with the current.")] public bool RangeExpansionsRequireFTFC { get; set; }
        [Display(Name = "Outside Bars (3 Exp)", Order = 4, GroupName = "06. Signals - Expansions", Description = "CC itself is a 3-bar, engulfing C1's entire range. Represents range expansion and volatility. Direction determined by close vs open (3u = bullish, 3d = bearish).")] public bool Show3Expansions { get; set; }
        [Display(Name = "  Outside Bars: FTFC", Order = 5, GroupName = "06. Signals - Expansions", Description = "FTFC filter: Require all monitored timeframes to agree on direction. Removes setups that fight the broader structural alignment, keeping only those swimming with the current.")] public bool ThreeExpRequireFTFC { get; set; }

        // ---- 07. Filters ----
        [Display(Name = "Hammer/Shooter Definition", Order = 1, GroupName = "07. Filters", Description = "Broad: relaxed detection based on body position. Classic: small body, long wick (3x body), body in upper/lower third. Pin Bar: stricter wick requirements.")]
        [TypeConverter(typeof(HammerDefinitionConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public HammerDefinition HammerLogic { get; set; }
        [Display(Name = "Match Candle Color", Order = 2, GroupName = "07. Filters", Description = "When enabled, hammers must be green (bullish) and shooters must be red (bearish) to qualify")] public bool MatchCandleColor { get; set; }
        [Range(2, 6)][Display(Name = "Domino: Minimum Timeframes", Order = 3, GroupName = "07. Filters", Description = "Minimum number of consecutive inside bar timeframes (with visible signals) to trigger a Domino. Respects your signal filter settings.")] public int MinDominoTFs { get; set; }
        [Display(Name = "Domino: Show in Data Table", Order = 4, GroupName = "07. Filters", Description = "Show a Domino row in the Data Table when consecutive inside bars are detected")] public bool ShowDominoInTable { get; set; }
        [Display(Name = "Only Show Signals Following the Lead", Order = 5, GroupName = "07. Filters", Description = "When enabled, only shows signals on lower timeframes that align with the direction of the highest timeframe signal in-force (the Lead). If no higher TF has a signal in-force, all signals are shown.")] public bool OnlyFollowTheLead { get; set; }

        // ---- 08. Targets ----
        [Display(Name = "Show Magnitude Levels", Order = 1, GroupName = "08. Targets", Description = "Show magnitude (C2 high/low) as take-profit targets for actionable signals")] public bool ShowMagnitude { get; set; }
        [Display(Name = "  Magnitude: Only When In-Force", Order = 2, GroupName = "08. Targets", Description = "Only show magnitude levels when the signal has triggered (breakout occurred). Hides targets on potential setups so you only see them when the trade is live.")] public bool MagOnlyWhenInForce { get; set; }
        [Display(Name = "Show Exhaustion Levels", Order = 3, GroupName = "08. Targets", Description = "Show exhaustion levels (prior swing high/low beyond C2) as extended targets")] public bool ShowExhaustion { get; set; }
        [Display(Name = "  Exhaustion: Only When In-Force", Order = 4, GroupName = "08. Targets", Description = "Only show exhaustion levels when the signal has triggered (breakout occurred). Hides extended targets on potential setups so you only see them when the trade is live.")] public bool ExhOnlyWhenInForce { get; set; }
        [Display(Name = "  Exhaustion: Only After Magnitude Hit", Order = 5, GroupName = "08. Targets", Description = "When enabled, exhaustion levels only appear after price has reached the magnitude level")] public bool ExhRequiresMagHit { get; set; }
        [Display(Name = "Show Take Action Windows", Order = 6, GroupName = "08. Targets", Description = "Highlight the zone between trigger and target levels for actionable signals")] public bool ShowTakeActionWindows { get; set; }
        [Display(Name = "  TAW: Only When In-Force", Order = 7, GroupName = "08. Targets", Description = "Only show take action windows when the signal has triggered (breakout occurred)")] public bool TawOnlyWhenInForce { get; set; }
        [Display(Name = "  TAW: Extend to Exhaustion", Order = 8, GroupName = "08. Targets", Description = "Extend the take action window to the exhaustion level instead of magnitude")] public bool TawExtendToExhaustion { get; set; }
        [Display(Name = "    Extend: Only When In-Force", Order = 9, GroupName = "08. Targets", Description = "Only extend to exhaustion when the signal has triggered")] public bool TawExhOnlyWhenInForce { get; set; }
        [Range(0, 100)][Display(Name = "  TAW: Fill Opacity", Order = 10, GroupName = "08. Targets", Description = "Opacity of the take action window fill (0 = transparent, 100 = solid)")] public int TawFillOpacity { get; set; }
        [Range(0, 100)][Display(Name = "  TAW: Border Opacity", Order = 11, GroupName = "08. Targets", Description = "Opacity of the take action window border")] public int TawBorderOpacity { get; set; }

        // ---- 09. Stops ----
        [Display(Name = "Stops: Enable", Order = 1, GroupName = "09. Stops", Description = "Show stop loss levels on the opposite side of the trigger. Once a signal goes in force, the stop is locked and persists for the remainder of that period even if the signal is no longer in force.")] public bool ShowStopLevels { get; set; }
        [Display(Name = "Reference", Order = 2, GroupName = "09. Stops", Description = "CC: stop tracks the breakout bar's opposite extreme in real time (updates as the candle forms, locks when it closes). Tighter risk, invalidated when the breakout candle itself is reversed. C1: stop at the prior bar's opposite side, locked when signal goes in force. Wider risk, invalidated when the full setup range is taken out. Failing 2 signals always use CC regardless of this setting.")]
        [TypeConverter(typeof(StopReferenceConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public StopReferenceMode StopReference { get; set; }
        [Display(Name = "Smallest Timeframe Only", Order = 3, GroupName = "09. Stops", Description = "Draw only the stop belonging to the smallest enabled timeframe that currently has one; stops on larger timeframes are hidden. Larger-timeframe stops keep tracking underneath (break even still arms, alert messages still carry their prices) - this only reduces what is drawn. Uses slot order, like the Lead Signal filter.")] public bool StopSmallestOnly { get; set; }
        [Display(Name = "Break Even at Magnitude", Order = 4, GroupName = "09. Stops", Description = "Move stop to the entry trigger (break even) when price hits the magnitude level. If no magnitude level exists, uses exhaustion as the trigger instead.")] public bool StopBEatMag { get; set; }
        [Display(Name = "Break Even at Exhaustion", Order = 5, GroupName = "09. Stops", Description = "Move stop to the entry trigger (break even) when price hits the exhaustion level. If no exhaustion level exists, uses magnitude as the trigger instead.")] public bool StopBEatExh { get; set; }
        [Display(Name = "Color Mode", Order = 6, GroupName = "09. Stops", Description = "Opposite Signal: bullish stops use the bearish signal color, bearish stops use the bullish signal color. Match Signal: stops match their signal's color. Custom: use the single color below for all stops.")]
        [TypeConverter(typeof(StopColorConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public StopColorMode StopColor { get; set; }
        [XmlIgnore][Display(Name = "Custom Color", Order = 7, GroupName = "09. Stops", Description = "Color for stop level lines and labels when Color Mode is set to Custom")] public Brush StopCustomColor { get; set; }
        [Browsable(false)] public string StopCustomColorSerializable { get { return Serialize.BrushToString(StopCustomColor); } set { StopCustomColor = Serialize.StringToBrush(value); } }

        // ---- 10. Display ----
        [Display(Name = "Preview Mode", Order = 1, GroupName = "10. Display", Description = "Off: never preview. On: always preview next period levels. Auto: previews once the chart's last bar has passed its scheduled close and the chart's trading hours template shows the trading day is over (weekends, the daily maintenance break, the equity overnight, holidays and early closes), or 4 hours after the last bar's close. A break inside one trading day (grains, lunch breaks) does not count.")]
        [TypeConverter(typeof(PreviewModeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public PreviewModeOption PreviewMode { get; set; }
        [Display(Name = "Show Data Table", Order = 2, GroupName = "10. Display", Description = "Show the multi-timeframe data table")] public bool ShowDataTable { get; set; }
        [Display(Name = "Table Mode", Order = 3, GroupName = "10. Display", Description = "Full: complete table with C2, C1, AS, CC columns. Compact: a single horizontal row of timeframe labels, color-coded. Use Compact Cell Color below to choose what that coloring means.")]
        [TypeConverter(typeof(TableModeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public TableModeOption TableMode { get; set; }
        [Display(Name = "Compact Cell Color", Order = 4, GroupName = "10. Display", Description = "What the colored timeframe cells mean in Compact mode - each option mirrors one of the two colorings in the Full table. Bar State: colors every cell by that timeframe's current candle, exactly like the Full table's CC column (yellow/orange inside, green/red directional, plus the outside-bar and Failing 2 colors). Signals In Force: colors a cell only when a signal is actually in force there, exactly like the Full table's TF column - so the row stays dark until something is live.")]
        [TypeConverter(typeof(CompactCellColorConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public CompactCellColorMode CompactCellColor { get; set; }
        [Display(Name = "Table Position", Order = 5, GroupName = "10. Display", Description = "Position of the data table on the chart")]
        [TypeConverter(typeof(TablePositionConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public TablePositionOption TablePosition { get; set; }
        [Display(Name = "Table Text Size", Order = 6, GroupName = "10. Display", Description = "Size of the table text")]
        [TypeConverter(typeof(TextSizeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public TextSizeOption TableTextSize { get; set; }
        [Display(Name = "Use Dash Separator", Order = 7, GroupName = "10. Display", Description = "Separate candle values with a dash (e.g., 2u-2d instead of 2u2d)")] public bool UseDashSeparator { get; set; }
        [Display(Name = "Show Timeline Labels", Order = 8, GroupName = "10. Display", Description = "Show labels at the end of each level line, anchored to the line's end time")] public bool ShowTimelineLabels { get; set; }
        [Range(0, 20)][Display(Name = "  Timeline Offset (bars)", Order = 9, GroupName = "10. Display", Description = "Number of bars to offset timeline labels from the line end")] public int LabelOffset { get; set; }
        [Display(Name = "  Keep Timeline Labels On Screen", Order = 10, GroupName = "10. Display", Description = "NinjaTrader cannot scroll past its right margin, so a label anchored at a far period end is pulled back to the chart's right edge.")] public bool ClampTimelineLabels { get; set; }
        [Display(Name = "Show Floating Labels", Order = 11, GroupName = "10. Display", Description = "Show labels at a fixed offset from the current bar, keeping them visible on screen as price moves")] public bool ShowFloatingLabels { get; set; }
        [Range(-20, 50)][Display(Name = "  Floating Offset (bars)", Order = 12, GroupName = "10. Display", Description = "Number of bars to offset floating labels from the current bar (negative = left, positive = right)")] public int FloatingLabelOffset { get; set; }
        [Display(Name = "Show Price in Label", Order = 13, GroupName = "10. Display", Description = "Include the price level in each label")] public bool ShowPriceInLabel { get; set; }
        [Display(Name = "Show H/L in Labels", Order = 14, GroupName = "10. Display", Description = "Show 'H' for high levels and 'L' for low levels in labels and alerts")] public bool ShowHighLowInLabel { get; set; }
        [Display(Name = "Label Text Size", Order = 15, GroupName = "10. Display", Description = "Size of the label text")]
        [TypeConverter(typeof(TextSizeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public TextSizeOption LabelTextSize { get; set; }
        [XmlIgnore][Display(Name = "Label Background", Order = 16, GroupName = "10. Display", Description = "Background color for labels")] public Brush LabelBackground { get; set; }
        [Browsable(false)] public string LabelBackgroundSerializable { get { return Serialize.BrushToString(LabelBackground); } set { LabelBackground = Serialize.StringToBrush(value); } }
        [Range(0, 100)][Display(Name = "Label Transparency", Order = 17, GroupName = "10. Display", Description = "Transparency of the label background (0 = solid, 100 = invisible)")] public int LabelBackgroundTransparency { get; set; }

        // ---- 11. Bar Coloring ----
        [Display(Name = "Color Chart Candles", Order = 1, GroupName = "11. Bar Coloring", Description = "Paint the chart's candles.\n\nStrat Candles: color each chart bar by its Strat classification against the prior bar - inside 1, directional 2u/2d, outside 3u/3d, and Failing 2s - using the same classification (and Failing 2 Detection Method) as the data table's CC cell.\n\nFTFC Candles: color each bar by Full Timeframe Continuity - all monitored timeframes above their open (up), all below (down), or conflicting. Historical bars are graded by that bar's close against each timeframe's open as it stood at the time, so painted history shows continuity as it actually developed, not as the periods finally closed.")]
        [TypeConverter(typeof(BarColorModeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public BarColorModeOption BarColorMode { get; set; }
        [Display(Name = "Strat: 1 (Inside)", Order = 2, GroupName = "11. Bar Coloring", Description = "Toggle candle paint per family. Colors follow the Style sections: 1s use the Inside Bar colors, 2s use the Signal colors, 3s use the Outside Bar colors, and Failing 2s use the Range Reclaim colors - so a custom palette applies to lines, labels, and candles together. With F2 (Failing) unchecked, or Failing 2 Detection disabled, a Failing 2 paints as its plain 2u/2d.")] public bool PaintInsideBars { get; set; }
        [Display(Name = "Strat: 2 (Directional)", Order = 3, GroupName = "11. Bar Coloring", Description = "Toggle candle paint per family. Colors follow the Style sections: 1s use the Inside Bar colors, 2s use the Signal colors, 3s use the Outside Bar colors, and Failing 2s use the Range Reclaim colors - so a custom palette applies to lines, labels, and candles together. With F2 (Failing) unchecked, or Failing 2 Detection disabled, a Failing 2 paints as its plain 2u/2d.")] public bool Paint2Bars { get; set; }
        [Display(Name = "Strat: 3 (Outside)", Order = 4, GroupName = "11. Bar Coloring", Description = "Toggle candle paint per family. Colors follow the Style sections: 1s use the Inside Bar colors, 2s use the Signal colors, 3s use the Outside Bar colors, and Failing 2s use the Range Reclaim colors - so a custom palette applies to lines, labels, and candles together. With F2 (Failing) unchecked, or Failing 2 Detection disabled, a Failing 2 paints as its plain 2u/2d.")] public bool PaintOutsideBars { get; set; }
        [Display(Name = "Strat: F2 (Failing)", Order = 5, GroupName = "11. Bar Coloring", Description = "Toggle candle paint per family. Colors follow the Style sections: 1s use the Inside Bar colors, 2s use the Signal colors, 3s use the Outside Bar colors, and Failing 2s use the Range Reclaim colors - so a custom palette applies to lines, labels, and candles together. With F2 (Failing) unchecked, or Failing 2 Detection disabled, a Failing 2 paints as its plain 2u/2d.")] public bool PaintFailedBars { get; set; }
        [XmlIgnore][Display(Name = "FTFC Up", Order = 6, GroupName = "11. Bar Coloring", Description = "Candle colors for full timeframe continuity up, down, and conflict.")] public Brush FtfcUpColor { get; set; }
        [Browsable(false)] public string FtfcUpColorSerializable { get { return Serialize.BrushToString(FtfcUpColor); } set { FtfcUpColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "FTFC Down", Order = 7, GroupName = "11. Bar Coloring", Description = "Candle colors for full timeframe continuity up, down, and conflict.")] public Brush FtfcDownColor { get; set; }
        [Browsable(false)] public string FtfcDownColorSerializable { get { return Serialize.BrushToString(FtfcDownColor); } set { FtfcDownColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "FTFC Conflict", Order = 8, GroupName = "11. Bar Coloring", Description = "Candle colors for full timeframe continuity up, down, and conflict.")] public Brush FtfcConflictColor { get; set; }
        [Browsable(false)] public string FtfcConflictColorSerializable { get { return Serialize.BrushToString(FtfcConflictColor); } set { FtfcConflictColor = Serialize.StringToBrush(value); } }
        [Display(Name = "Color Conflict Bars", Order = 9, GroupName = "11. Bar Coloring", Description = "OFF: conflicted bars keep the chart's normal candle colors, so only full up/down agreement is painted.")] public bool PaintFtfcConflict { get; set; }
        [Display(Name = "Highlight Failing 2 Flips", Order = 10, GroupName = "11. Bar Coloring", Description = "When continuity flips while a Failing 2 (Range Reclaim) is live on a monitored timeframe, paint the flip bar with the Range Reclaim color instead of the plain continuity color. A reclaim that flips continuity is the highest-conviction version of the signal - trapped traders from the failed breakout fuel the new direction.")] public bool HighlightFailing2Flips { get; set; }

        // ---- 12. Bar Numbers (BARNUM-1) ----
        [Display(Name = "Show Bar Numbers", Order = 1, GroupName = "12. Bar Numbers", Description = "Print each chart candle's Strat number (1, 2u, 2d, 3) next to it, classified against the prior candle. Uses the same classification (and Failing 2 Detection Method) as the data table's CC cell and Strat Candles, so numbers, candle paint and table always agree. Chart timeframe only - the data table covers your other timeframes. Not meaningful on Heikin Ashi or other synthetic candles.")] public bool ShowBarNumbers { get; set; }
        [Display(Name = "Notation", Order = 2, GroupName = "12. Bar Numbers", Description = "Full: 1, 2u, 2d, 3u, 3d, F2u, F2d - exactly the data table's notation.\n\nClassic: 1, 2u, 2d, 3. Outside bars drop their close direction and Failing 2s show as their plain 2u/2d.\n\nNumbers: 1, 2, 3. Direction is carried by color alone.")]
        [TypeConverter(typeof(BarNumNotationConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public BarNumNotationOption BarNumNotation { get; set; }
        [Display(Name = "Emphasis", Order = 3, GroupName = "12. Bar Numbers", Description = "Tiered: 1s, 3s and Failing 2s in full color, plain 2s faded - setups start at 1s, 3s and failing 2s, so those stand out while trending 2s recede.\n\nUniform: every number at full strength.\n\nKey Bars Only: plain 2s are hidden. In Classic and Numbers notation a Failing 2 shows as a plain 2, so it is hidden too.")]
        [TypeConverter(typeof(BarNumEmphasisConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public BarNumEmphasisOption BarNumEmphasis { get; set; }
        [Display(Name = "Position", Order = 4, GroupName = "12. Bar Numbers", Description = "Below / Above: every number on the same side, reading as one steady row.\n\nBroken Side: the number sits on the side the candle took out - 2u and F2u above, 2d and F2d below. Outside bars follow their close (3u above, 3d below); inside bars sit below.")]
        [TypeConverter(typeof(BarNumPositionConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public BarNumPositionOption BarNumPosition { get; set; }
        [Display(Name = "Color", Order = 5, GroupName = "12. Bar Numbers", Description = "Bar Type Palette: each number takes its family color from the Style sections (Signal, Inside Bar, Outside Bar, Range Reclaim), the same colors Strat Candles paints. Single Color: one color for every number - useful with Strat Candles on, so color is not shown twice. Emphasis still applies.")]
        [TypeConverter(typeof(BarNumColorModeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public BarNumColorModeOption BarNumColorMode { get; set; }
        [XmlIgnore][Display(Name = "Single Color", Order = 6, GroupName = "12. Bar Numbers")] public Brush BarNumColor { get; set; }
        [Browsable(false)] public string BarNumColorSerializable { get { return Serialize.BrushToString(BarNumColor); } set { BarNumColor = Serialize.StringToBrush(value); } }
        [Range(0, 100000)][Display(Name = "Bars Shown", Order = 7, GroupName = "12. Bar Numbers", Description = "Number only the most recent N candles. 0 = every candle on the chart. The forming candle's number is drawn lighter because it can still change (a 2u can become a 3 or fail into an F2u); it takes full strength when the candle closes.")] public int BarNumBarsShown { get; set; }
        [Display(Name = "Text Size", Order = 8, GroupName = "12. Bar Numbers", Description = "NinjaTrader only. TradingView fixes this size; NinjaTrader can draw any size at no cost.")]
        [TypeConverter(typeof(TextSizeConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public TextSizeOption BarNumTextSize { get; set; }

        // ---- 13. Style ----
        [XmlIgnore][Display(Name = "Bullish Signal", Order = 1, GroupName = "13. Style")] public Brush BullSignalColor { get; set; }
        [Browsable(false)] public string BullSignalColorSerializable { get { return Serialize.BrushToString(BullSignalColor); } set { BullSignalColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bullish Crossed", Order = 2, GroupName = "13. Style")] public Brush BullCrossedColor { get; set; }
        [Browsable(false)] public string BullCrossedColorSerializable { get { return Serialize.BrushToString(BullCrossedColor); } set { BullCrossedColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bullish 1 (Inside Bar)", Order = 3, GroupName = "13. Style")] public Brush BullInsideColor { get; set; }
        [Browsable(false)] public string BullInsideColorSerializable { get { return Serialize.BrushToString(BullInsideColor); } set { BullInsideColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bullish 3 (Outside Bar)", Order = 4, GroupName = "13. Style")] public Brush BullOutsideColor { get; set; }
        [Browsable(false)] public string BullOutsideColorSerializable { get { return Serialize.BrushToString(BullOutsideColor); } set { BullOutsideColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bullish Range Reclaim (F2d)", Order = 5, GroupName = "13. Style", Description = "Color for bullish Range Reclaim signals (failed bearish breakout)")] public Brush BullReclaimColor { get; set; }
        [Browsable(false)] public string BullReclaimColorSerializable { get { return Serialize.BrushToString(BullReclaimColor); } set { BullReclaimColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish Signal", Order = 6, GroupName = "13. Style")] public Brush BearSignalColor { get; set; }
        [Browsable(false)] public string BearSignalColorSerializable { get { return Serialize.BrushToString(BearSignalColor); } set { BearSignalColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish Crossed", Order = 7, GroupName = "13. Style")] public Brush BearCrossedColor { get; set; }
        [Browsable(false)] public string BearCrossedColorSerializable { get { return Serialize.BrushToString(BearCrossedColor); } set { BearCrossedColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish 1 (Inside Bar)", Order = 8, GroupName = "13. Style")] public Brush BearInsideColor { get; set; }
        [Browsable(false)] public string BearInsideColorSerializable { get { return Serialize.BrushToString(BearInsideColor); } set { BearInsideColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish 3 (Outside Bar)", Order = 9, GroupName = "13. Style")] public Brush BearOutsideColor { get; set; }
        [Browsable(false)] public string BearOutsideColorSerializable { get { return Serialize.BrushToString(BearOutsideColor); } set { BearOutsideColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish Range Reclaim (F2u)", Order = 10, GroupName = "13. Style", Description = "Color for bearish Range Reclaim signals (failed bullish breakout)")] public Brush BearReclaimColor { get; set; }
        [Browsable(false)] public string BearReclaimColorSerializable { get { return Serialize.BrushToString(BearReclaimColor); } set { BearReclaimColor = Serialize.StringToBrush(value); } }

        // ---- 14. Alerts ----
        [Display(Name = "Enable Alerts", Order = 1, GroupName = "14. Alerts", Description = "Master switch. Alerts appear in NinjaTrader's Alerts Log and play the sound below.")] public bool EnableAlerts { get; set; }
        [Display(Name = "Sound File", Order = 2, GroupName = "14. Alerts", Description = "A file in NinjaTrader's sounds folder, e.g. Alert1.wav. Leave blank for no sound.")] public string AlertSound { get; set; }
        [Display(Name = "Signal In-Force (Any) - consolidated message", Order = 3, GroupName = "14. Alerts")] public bool AlertSignalInForceAny { get; set; }
        [Display(Name = "  Include TF1", Order = 4, GroupName = "14. Alerts", Description = "Select which timeframes to include in the consolidated 'Signal In-Force (Any)' alert. Uncheck a TF to exclude it from alert messages. The separate per-timeframe alerts (e.g. 'Signal In-Force (TF1)') are not affected by these checkboxes.")] public bool AlertIncludeTF1 { get; set; }
        [Display(Name = "  Include TF2", Order = 5, GroupName = "14. Alerts", Description = "Select which timeframes to include in the consolidated 'Signal In-Force (Any)' alert. Uncheck a TF to exclude it from alert messages. The separate per-timeframe alerts (e.g. 'Signal In-Force (TF1)') are not affected by these checkboxes.")] public bool AlertIncludeTF2 { get; set; }
        [Display(Name = "  Include TF3", Order = 6, GroupName = "14. Alerts", Description = "Select which timeframes to include in the consolidated 'Signal In-Force (Any)' alert. Uncheck a TF to exclude it from alert messages. The separate per-timeframe alerts (e.g. 'Signal In-Force (TF1)') are not affected by these checkboxes.")] public bool AlertIncludeTF3 { get; set; }
        [Display(Name = "  Include TF4", Order = 7, GroupName = "14. Alerts", Description = "Select which timeframes to include in the consolidated 'Signal In-Force (Any)' alert. Uncheck a TF to exclude it from alert messages. The separate per-timeframe alerts (e.g. 'Signal In-Force (TF1)') are not affected by these checkboxes.")] public bool AlertIncludeTF4 { get; set; }
        [Display(Name = "  Include TF5", Order = 8, GroupName = "14. Alerts", Description = "Select which timeframes to include in the consolidated 'Signal In-Force (Any)' alert. Uncheck a TF to exclude it from alert messages. The separate per-timeframe alerts (e.g. 'Signal In-Force (TF1)') are not affected by these checkboxes.")] public bool AlertIncludeTF5 { get; set; }
        [Display(Name = "  Include TF6", Order = 9, GroupName = "14. Alerts", Description = "Select which timeframes to include in the consolidated 'Signal In-Force (Any)' alert. Uncheck a TF to exclude it from alert messages. The separate per-timeframe alerts (e.g. 'Signal In-Force (TF1)') are not affected by these checkboxes.")] public bool AlertIncludeTF6 { get; set; }
        [Display(Name = "  Include FTFC", Order = 10, GroupName = "14. Alerts", Description = "Enrich the consolidated alert message with additional context. Each toggle appends data to the alert when available. Example output: 1H 2d-1-2u HAM @ 542.10 MAG 545.30 EXH 548.20 STOP 538.10 | FTFC Up. Fields that don't exist for a signal (e.g. no magnitude) are omitted automatically.")] public bool AlertShowFTFC { get; set; }
        [Display(Name = "  Include Trigger", Order = 11, GroupName = "14. Alerts", Description = "Enrich the consolidated alert message with additional context. Each toggle appends data to the alert when available. Example output: 1H 2d-1-2u HAM @ 542.10 MAG 545.30 EXH 548.20 STOP 538.10 | FTFC Up. Fields that don't exist for a signal (e.g. no magnitude) are omitted automatically.")] public bool AlertShowTrigger { get; set; }
        [Display(Name = "  Include MAG", Order = 12, GroupName = "14. Alerts", Description = "Enrich the consolidated alert message with additional context. Each toggle appends data to the alert when available. Example output: 1H 2d-1-2u HAM @ 542.10 MAG 545.30 EXH 548.20 STOP 538.10 | FTFC Up. Fields that don't exist for a signal (e.g. no magnitude) are omitted automatically.")] public bool AlertShowMagnitude { get; set; }
        [Display(Name = "  Include EXH", Order = 13, GroupName = "14. Alerts", Description = "Enrich the consolidated alert message with additional context. Each toggle appends data to the alert when available. Example output: 1H 2d-1-2u HAM @ 542.10 MAG 545.30 EXH 548.20 STOP 538.10 | FTFC Up. Fields that don't exist for a signal (e.g. no magnitude) are omitted automatically.")] public bool AlertShowExhaustion { get; set; }
        [Display(Name = "  Include STOP", Order = 14, GroupName = "14. Alerts", Description = "Enrich the consolidated alert message with additional context. Each toggle appends data to the alert when available. Example output: 1H 2d-1-2u HAM @ 542.10 MAG 545.30 EXH 548.20 STOP 538.10 | FTFC Up. Fields that don't exist for a signal (e.g. no magnitude) are omitted automatically.")] public bool AlertShowStop { get; set; }
        [Display(Name = "  Include Domino", Order = 15, GroupName = "14. Alerts", Description = "Fire a 'Domino: <timeframes>' alert each time a new Domino forms (the Pine's Domino alert() call, which shares the consolidated alert's subscription on TradingView). In NinjaTrader it is its own Alerts Log entry and fires even when 'Signal In-Force (Any)' is off. The 'Domino Setup' alert below fires on the same Domino, so turning on both gives two entries.")] public bool IncludeDominoInConsolidated { get; set; }
        [Display(Name = "Signal In-Force (TF1)", Order = 16, GroupName = "14. Alerts")] public bool AlertTF1InForce { get; set; }
        [Display(Name = "Signal In-Force (TF2)", Order = 17, GroupName = "14. Alerts")] public bool AlertTF2InForce { get; set; }
        [Display(Name = "Signal In-Force (TF3)", Order = 18, GroupName = "14. Alerts")] public bool AlertTF3InForce { get; set; }
        [Display(Name = "Signal In-Force (TF4)", Order = 19, GroupName = "14. Alerts")] public bool AlertTF4InForce { get; set; }
        [Display(Name = "Signal In-Force (TF5)", Order = 20, GroupName = "14. Alerts")] public bool AlertTF5InForce { get; set; }
        [Display(Name = "Signal In-Force (TF6)", Order = 21, GroupName = "14. Alerts")] public bool AlertTF6InForce { get; set; }
        [Display(Name = "Signal In-Force (Bullish)", Order = 22, GroupName = "14. Alerts")] public bool AlertBullishInForce { get; set; }
        [Display(Name = "Signal In-Force (Bearish)", Order = 23, GroupName = "14. Alerts")] public bool AlertBearishInForce { get; set; }
        [Display(Name = "New Potential (Any)", Order = 24, GroupName = "14. Alerts")] public bool AlertNewPotential { get; set; }
        [Display(Name = "Domino Setup", Order = 25, GroupName = "14. Alerts")] public bool AlertDominoSetup { get; set; }
        [Display(Name = "FTFC Shifted", Order = 26, GroupName = "14. Alerts")] public bool AlertFtfcShifted { get; set; }
        [Display(Name = "FTFC Up", Order = 27, GroupName = "14. Alerts")] public bool AlertFtfcUp { get; set; }
        [Display(Name = "FTFC Down", Order = 28, GroupName = "14. Alerts")] public bool AlertFtfcDown { get; set; }
        [Display(Name = "FTFC Conflict", Order = 29, GroupName = "14. Alerts")] public bool AlertFtfcConflict { get; set; }

        // ---- 15. Advanced ----
        [Display(Name = "Exhaustion Disables 'In Force'", Order = 1, GroupName = "15. Advanced", Description = "OFF (default): A signal stays 'in force' even after its targets are hit. The data table continues showing the signal as active, and the Lead Signal filter continues using it for directional bias. ON: Once a signal hits exhaustion (or magnitude if no exhaustion exists), it is no longer considered 'in force.' This frees up the Lead Signal filter to allow reversal setups on lower timeframes. Example: if the Daily has a bullish signal that already hit exhaustion, turning this on lets bearish setups appear on the 15m/1H instead of staying suppressed by a move that may be done.")] public bool ExhDisablesInForce { get; set; }
        [Display(Name = "Exhaustion Excludes from FTFC", Order = 2, GroupName = "15. Advanced", Description = "OFF (default): All timeframes count toward FTFC regardless of whether their targets have been hit. ON: A timeframe that has hit exhaustion (or magnitude if no exhaustion exists) is removed from the FTFC calculation. This means a timeframe whose move is structurally complete no longer blocks FTFC from flipping. Example: if 5 of 6 timeframes are bearish but the 6th is bullish and already hit its exhaustion, turning this on excludes that exhausted bullish timeframe so FTFC reads as bearish instead of Conflict. Affects the data table's FTFC row only, as in the Suite.")] public bool ExhDisablesFTFC { get; set; }
        [Display(Name = "Show Magnitude for Outside Bars", Order = 3, GroupName = "15. Advanced", Description = "Show the C2 high/low as a magnitude target for outside bar (3-bar) expansions")] public bool Show3ExpMagnitude { get; set; }
        [Display(Name = "Show Exhaustion for Outside Bars", Order = 4, GroupName = "15. Advanced", Description = "Show exhaustion levels for outside bar (3-bar) expansions")] public bool Show3ExpExhaustion { get; set; }
        [Display(Name = "Enable Failing 2 Detection", Order = 5, GroupName = "15. Advanced", Description = "When enabled, detects Failing 2s (F2u/F2d) where price reclaims the prior range or crosses back against the breakout direction. When disabled, only shows standard 2u/2d signals.")] public bool EnableFailed2Detection { get; set; }
        [Display(Name = "Failing 2 Detection Method", Order = 6, GroupName = "15. Advanced", Description = "Reclaim: closes back inside C1 range. Open: closes against breakout direction. Reclaim + Open: requires both conditions. Reclaim OR Open: requires one or the other.")]
        [TypeConverter(typeof(F2MethodConverter))][PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        public F2Method Failed2DetectionMethod { get; set; }
        [Display(Name = "Show Failing 2 Open Level", Order = 7, GroupName = "15. Advanced", Description = "Show the CC open price as a dotted reference line when a Failing 2 is detected. The open acts as the trigger level for the reclaim.")] public bool ShowF2OpenLine { get; set; }
        [Display(Name = "Color TF When In-Force", Order = 8, GroupName = "15. Advanced", Description = "Highlight the TF column with the CC color when a signal you have enabled is in-force (Full mode only)")] public bool ColorTFWhenInForce { get; set; }
        [Display(Name = "TAW: Include Failing 2s", Order = 9, GroupName = "15. Advanced", Description = "Highlight the C1 range as a take action zone for Failing 2 (Range Reclaim) setups")] public bool TawIncludeFailing2s { get; set; }
        [Display(Name = "TAW: Include Outside Bars", Order = 10, GroupName = "15. Advanced", Description = "Highlight the zone between trigger and magnitude for outside bar (3-bar) expansions")] public bool TawIncludeOutsideBars { get; set; }
        [Display(Name = "HTF Trading Hours", Order = 11, GroupName = "15. Advanced", Description = "Trading hours template for the higher-timeframe data (e.g. 'CME US Index Futures ETH'). Blank (recommended) uses the chart's own template. Set a name only to build the higher timeframes from different hours than the chart.")] public string HtfTradingHours { get; set; }
        [Range(200, 20000)][Display(Name = "Max Daily Bars To Load", Order = 12, GroupName = "15. Advanced", Description = "Upper limit on the daily history behind the D, W, M, 3M, 6M and 12M slots; each loads about 100 of its candles up to this limit. Exhaustion needs about 1,100 daily bars for monthly, 3,300 for quarterly, 6,700 for 6M and 13,400 for 12M. On a daily chart the chart's own Days to load governs instead.")] public int MaxDailyBars { get; set; }

        // ---- 16. Debug ----
        [Display(Name = "Show Debug Panel", Order = 1, GroupName = "16. Debug", Description = "Show detailed debug information for a selected timeframe to troubleshoot signal logic")] public bool ShowDebugPanel { get; set; }
        [Display(Name = "Timeframe to Debug", Order = 2, GroupName = "16. Debug", Description = "Select which timeframe slot to show debug info for (TF1 = first timeframe in settings, etc.)")] public DebugSlot DebugTimeframe { get; set; }

        // ---- Outputs (for strategies and Market Analyzer) ----
        [Browsable(false)][XmlIgnore] public Series<double> FtfcState { get { return Values[0]; } }
        [Browsable(false)][XmlIgnore] public Series<double> Tf1Signal { get { return Values[1]; } }
        [Browsable(false)][XmlIgnore] public Series<double> Tf2Signal { get { return Values[2]; } }
        [Browsable(false)][XmlIgnore] public Series<double> Tf3Signal { get { return Values[3]; } }
        [Browsable(false)][XmlIgnore] public Series<double> Tf4Signal { get { return Values[4]; } }
        [Browsable(false)][XmlIgnore] public Series<double> Tf5Signal { get { return Values[5]; } }
        [Browsable(false)][XmlIgnore] public Series<double> Tf6Signal { get { return Values[6]; } }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TheStratSuite[] cacheTheStratSuite;
		public TheStratSuite TheStratSuite()
		{
			return TheStratSuite(Input);
		}

		public TheStratSuite TheStratSuite(ISeries<double> input)
		{
			if (cacheTheStratSuite != null)
				for (int idx = 0; idx < cacheTheStratSuite.Length; idx++)
					if (cacheTheStratSuite[idx] != null &&  cacheTheStratSuite[idx].EqualsInput(input))
						return cacheTheStratSuite[idx];
			return CacheIndicator<TheStratSuite>(new TheStratSuite(), input, ref cacheTheStratSuite);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TheStratSuite TheStratSuite()
		{
			return indicator.TheStratSuite(Input);
		}

		public Indicators.TheStratSuite TheStratSuite(ISeries<double> input )
		{
			return indicator.TheStratSuite(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TheStratSuite TheStratSuite()
		{
			return indicator.TheStratSuite(Input);
		}

		public Indicators.TheStratSuite TheStratSuite(ISeries<double> input )
		{
			return indicator.TheStratSuite(input);
		}
	}
}

#endregion
