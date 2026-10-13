// TheStrat Suite for NinjaTrader 8 - v0.1.0 (port of TheStrat Suite v3.1.1 for TradingView)
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
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.StratSuiteCore;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.StratSuiteCore
{
    // Shows an enum's [Description] in the property grid (Pine option names).
    public class FriendlyEnumConverter<T> : EnumConverter
    {
        public FriendlyEnumConverter() : base(typeof(T)) { }

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

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value != null && value.GetType() == typeof(T))
                return Describe(value);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            string text = value as string;
            if (text != null)
            {
                foreach (object v in Enum.GetValues(typeof(T)))
                    if (Describe(v) == text)
                        return v;
            }
            return base.ConvertFrom(context, culture, value);
        }
    }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TheStratSuite : Indicator
    {
        public const string Version = "0.1.0";

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
        private readonly Dictionary<string, int> alertFiredBar = new Dictionary<string, int>();
        private RenderModel model;
        private DispatcherTimer clockTimer;
        private readonly Dictionary<int, SolidColorBrush> wpfBrushes = new Dictionary<int, SolidColorBrush>();
        private readonly Dictionary<int, SharpDX.Direct2D1.SolidColorBrush> dxBrushes = new Dictionary<int, SharpDX.Direct2D1.SolidColorBrush>();
        private SharpDX.Direct2D1.StrokeStyle dashStyle, dotStyle;

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
                committed = new EvalState();
                working = committed.Clone();
                workingBar = -1;
            }
            else if (State == State.Historical)
            {
                StartClock();
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
                    int m = StratTimeframes.Minutes(s.Tf);
                    if (!minuteSeries.ContainsKey(m))
                    {
                        AddDataSeries(instrument, new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = m }, StratTimeframes.HtfBarsTier(s.Tf), th, null);
                        minuteSeries[m] = nextIndex++;
                    }
                }
                else
                {
                    int perBar = s.Tf == StratTimeframe.Day ? 1 : s.Tf == StratTimeframe.Week ? 5 : s.Tf == StratTimeframe.Month ? 21
                        : s.Tf == StratTimeframe.Quarter ? 63 : s.Tf == StratTimeframe.HalfYear ? 126 : 252;
                    dailyBars = Math.Max(dailyBars, StratTimeframes.HtfBarsTier(s.Tf) * perBar + perBar);
                }
            }
            if (dailyBars > 0)
            {
                AddDataSeries(instrument, new BarsPeriod { BarsPeriodType = BarsPeriodType.Day, Value = 1 }, Math.Min(Math.Max(dailyBars, 160), MaxDailyBars), th, null);
                daySeries = nextIndex++;
            }
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
            chartDayBased = false;
            chartIntradayTime = false;
            chartSeconds = 0;
            chartAsTf = StratTimeframe.Day;
            switch (bp.BarsPeriodType)
            {
                case BarsPeriodType.Second: chartSeconds = bp.Value; chartIntradayTime = true; break;
                case BarsPeriodType.Minute: chartSeconds = bp.Value * 60L; chartIntradayTime = true; break;
                case BarsPeriodType.Day: chartSeconds = bp.Value * 86400L; chartDayBased = true; chartAsTf = StratTimeframe.Day; break;
                case BarsPeriodType.Week: chartSeconds = bp.Value * 604800L; chartDayBased = true; chartAsTf = StratTimeframe.Week; break;
                case BarsPeriodType.Month: chartSeconds = bp.Value * 2629746L; chartDayBased = true; chartAsTf = bp.Value >= 12 ? StratTimeframe.Year : bp.Value >= 6 ? StratTimeframe.HalfYear : bp.Value >= 3 ? StratTimeframe.Quarter : StratTimeframe.Month; break;
                case BarsPeriodType.Year: chartSeconds = bp.Value * 31556952L; chartDayBased = true; chartAsTf = StratTimeframe.Year; break;
                default: chartSeconds = 0; break;   // tick, volume, range, Renko...: no timeframe to compare
            }
            // Pine validTimeframe: a slot below the chart's timeframe is not evaluated (its row is
            // greyed) but still votes in FTFC. Non-time charts have no timeframe, so nothing is lower.
            for (int i = 0; i < 6; i++)
                slots[i].Lower = chartSeconds > 0 && slots[i].Seconds < chartSeconds;
        }

        // ====================================================================
        // PER-BAR EVALUATION
        // ====================================================================

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || slots == null)
                return;
            Evaluate(CurrentBars[0]);
        }

        private bool IsLastBar(int k)
        {
            int count = BarsArray[0].Count;
            if (State == State.Realtime && Calculate == Calculate.OnBarClose)
                return k >= count - 2;
            return k >= count - 1;
        }

        private DateTime TradingDateOf(int k)
        {
            DateTime t = BarsArray[0].GetTime(k);
            if (chartDayBased)
                return t.Date;
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
            DateTime now = NinjaTrader.Core.Globals.Now;
            bool previewNext = false;
            if (isLast && PreviewMode != PreviewModeOption.Off)
                previewNext = PreviewMode == PreviewModeOption.On || MarketLikelyClosed(k, now);
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
                    HtfSource src = s.Src;
                    StratEngine.FindExhaustion(src.H, src.L, htfIdx[i], settings.ShowAnyTargets, out eH, out eHi, out eL, out eLi, out nH, out nHi, out nL, out nLi);
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
            PaintBar(st, k, raws, o0, h0, l0, c0);
            Values[0][0] = ftfcUp && !ftfcDown ? 1 : ftfcDown && !ftfcUp ? -1 : 0;
            for (int i = 0; i < 6; i++)
                Values[i + 1][0] = results[i] == null ? 0 : StratEngine.SignalDirection(results[i]);

            // ---- 10. Render model ----
            if (isLast)
                model = BuildModel(st, k, raws, results, anchorIndex, anchorDirection, ftfcUp, ftfcDown, dominoBestRun, dominoBestTFs, previewNext, dbg, c0, tick);
        }

        // ====================================================================
        // PREVIEW MODE (clock + trading hours)
        // ====================================================================
        // Auto arms when the chart's last bar has passed its scheduled close and the market is
        // not trading according to the instrument's trading hours template (which carries the
        // exchange holidays and early closes), or 4 hours after the last bar's close.

        private bool MarketLikelyClosed(int k, DateTime now)
        {
            try
            {
                DateTime lastClose = BarsArray[0].GetTime(k);
                bool lastBarClosed;
                if (chartIntradayTime)
                    lastBarClosed = now > lastClose;
                else if (chartDayBased)
                {
                    lastClose = PeriodEndFromDate(chartAsTf, TradingDateOf(k));
                    lastBarClosed = now > lastClose;
                }
                else
                    lastBarClosed = true;
                if (!lastBarClosed) return false;
                clockIt.GetNextSession(now, true);
                bool inSession = now >= clockIt.ActualSessionBegin && now <= clockIt.ActualSessionEnd;
                bool holidayClosed = (now - lastClose).TotalHours > 4;
                return !inSession || holidayClosed;
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

        // Scheduled end of the calendar period holding a trading date: walk sessions forward
        // until the period key changes.
        private DateTime PeriodEndFromDate(StratTimeframe tf, DateTime tradingDate)
        {
            long key = StratTimeframes.PeriodKey(tf, tradingDate);
            endIt.GetNextSession(tradingDate.Date, true);
            // Advance to the session whose trading day is the given date.
            int guard = 0;
            while (endIt.ActualTradingDayExchange.Date < tradingDate.Date && guard++ < 10)
                endIt.GetNextSession(endIt.ActualSessionEnd, false);
            DateTime end = endIt.ActualSessionEnd;
            guard = 0;
            while (guard++ < 400)
            {
                DateTime prevEnd = endIt.ActualSessionEnd;
                endIt.GetNextSession(prevEnd, false);
                if (endIt.ActualSessionEnd <= prevEnd) break;
                if (StratTimeframes.PeriodKey(tf, endIt.ActualTradingDayExchange) != key) break;
                end = endIt.ActualSessionEnd;
            }
            return end;
        }

        private void StartClock()
        {
            if (ChartControl == null || PreviewMode != PreviewModeOption.Auto) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (clockTimer != null) return;
                clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                clockTimer.Tick += OnClockTick;
                clockTimer.Start();
            });
        }

        private void StopClock()
        {
            DispatcherTimer t = clockTimer;
            clockTimer = null;
            if (t == null) return;
            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => { t.Stop(); t.Tick -= OnClockTick; });
        }

        // Re-evaluates the last bar with no new tick, so Auto preview arms and disarms on the
        // clock the way the Pine's timenow does on TradingView.
        private void OnClockTick(object sender, EventArgs e)
        {
            if (State != State.Realtime && State != State.Historical) return;
            TriggerCustomEvent(o =>
            {
                if (CurrentBars[0] >= 0 && IsLastBar(CurrentBars[0]))
                    Evaluate(CurrentBars[0]);
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

            if (!EnableAlerts || State != State.Realtime)
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
            SColor color = SColor.None;
            if (settings.BarColorMode == BarColorModeOption.StratCandles)
            {
                if (k > 0)
                {
                    bool failed;
                    Bars b0 = BarsArray[0];
                    string t = StratGrammar.DetectBarTypeAndFailed(b0.GetHigh(k - 1), b0.GetLow(k - 1), h0, l0, o0, c0, settings.EnableFailed2Detection, settings.Failed2Method, out failed);
                    color = StratCandleColor(t);
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
                    if (settings.PaintFtfcTransitions && en[i] && !slots[i].Lower && !raws[i].IsPreview)
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
                    if (settings.PaintFtfcTransitions && flipUp && anyF2d) color = settings.ColorFailed2D;
                    else if (settings.PaintFtfcTransitions && flipDown && anyF2u) color = settings.ColorFailed2U;
                    else if (up) color = settings.ColorFtfcUpBar;
                    else if (down) color = settings.ColorFtfcDownBar;
                    else if (settings.PaintFtfcConflict) color = settings.ColorFtfcConflictBar;
                }
            }
            if (color.IsNone)
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

        private SColor StratCandleColor(string barType)
        {
            StratSettings s = settings;
            switch (barType)
            {
                case "1u": return s.PaintInsideBars ? s.ColorBullishPotential : SColor.None;
                case "1d": return s.PaintInsideBars ? s.ColorBearishPotential : SColor.None;
                case "2u": return s.Paint2Bars ? s.ColorBullishActive : SColor.None;
                case "2d": return s.Paint2Bars ? s.ColorBearishActive : SColor.None;
                case "F2u": return s.PaintFailedBars ? s.ColorFailed2U : s.Paint2Bars ? s.ColorBullishActive : SColor.None;
                case "F2d": return s.PaintFailedBars ? s.ColorFailed2D : s.Paint2Bars ? s.ColorBearishActive : SColor.None;
                case "3u": return s.PaintOutsideBars ? s.Color3High : SColor.None;
                case "3d": return s.PaintOutsideBars ? s.Color3Low : SColor.None;
                default: return SColor.None;
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
                if (raw.IsPreview)
                    end = lastTime.AddSeconds(s.Seconds);
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
                        s.EndTime = PeriodEndFromDate(s.Tf, TradingDateOf(lastBar));
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
                return found;
            }
            if (chartSeconds <= 0)
                return lastBar + 10;       // no clock on tick/range charts: a short projection
            if (chartDayBased)
            {
                double days = 0;
                DateTime cur = lastTime;
                int guard = 0;
                endIt.GetNextSession(cur, false);
                while (guard++ < 400 && endIt.ActualSessionBegin < t)
                {
                    days += 1;
                    DateTime prevEnd = endIt.ActualSessionEnd;
                    endIt.GetNextSession(prevEnd, false);
                    if (endIt.ActualSessionEnd <= prevEnd) break;
                }
                return lastBar + Math.Max(1, Math.Ceiling(days * 86400.0 / chartSeconds));
            }
            // Intraday: count only in-session time, as the chart's own future bars would.
            double seconds = 0;
            DateTime c = lastTime;
            int n = 0;
            while (n++ < 400 && c < t)
            {
                endIt.GetNextSession(c, false);
                DateTime begin = endIt.ActualSessionBegin > c ? endIt.ActualSessionBegin : c;
                DateTime finish = endIt.ActualSessionEnd < t ? endIt.ActualSessionEnd : t;
                if (finish > begin) seconds += (finish - begin).TotalSeconds;
                if (endIt.ActualSessionEnd <= c) break;
                c = endIt.ActualSessionEnd;
            }
            return lastBar + Math.Ceiling(seconds / chartSeconds);
        }

        private RenderModel BuildModel(EvalState st, int k, SlotRaw[] raws, SlotResult[] results, int anchorIndex, int anchorDirection,
            bool ftfcUp, bool ftfcDown, int dominoBestRun, string dominoBestTFs, bool previewNext, DebugInfo dbg, double close, double tick)
        {
            StratSettings s = settings;
            RenderModel m = new RenderModel();
            m.LastBar = k;
            double[] endX = new double[6];
            int[] htf = new int[6];
            Bars b0 = BarsArray[0];
            DateTime barTime = b0.GetTime(k);
            DateTime td = TradingDateOf(k);
            for (int i = 0; i < 6; i++)
            {
                htf[i] = -1;
                if (!slots[i].Enabled || slots[i].Src == null || !raws[i].HasData) continue;
                bool exact;
                long key;
                htf[i] = slots[i].Src.IndexFor(MapTime(k, barTime, td), td, out exact, out key);
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
                case "F2u": return settings.ColorFailed2U;
                case "F2d": return settings.ColorFailed2D;
                case "3u": return settings.ColorBullish3Bar;
                case "3d": return settings.ColorBearish3Bar;
                default: return SColor.TvGray.WithTransp(50);
            }
        }

        private SColor BarTypeTextColor(string t)
        {
            return t == "" ? SColor.TvGray : BarTypeColor(t);
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
                dashStyle = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dash });
                dotStyle = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dot });
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
            RenderModel m = model;
            if (m == null || RenderTarget == null || ChartBars == null) return;
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
            return new SColor(sb.Color.R, sb.Color.G, sb.Color.B, 255);
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
            HtfTradingHours = ""; MaxDailyBars = 5000;
            ShowDebugPanel = false; DebugTimeframe = DebugSlot.TF1;
        }

        #region Properties

        // ---- 01. Timeframe Preset ----
        [Display(Name = "Preset", Order = 1, GroupName = "01. Timeframe Preset", Description = "Select a timeframe preset to auto-configure timeframes and line widths. When set to anything other than Custom, individual Timeframe 1-6 settings are ignored. You must select Custom to use your own timeframe configuration.")]
        [TypeConverter(typeof(FriendlyEnumConverter<StratPreset>))]
        public StratPreset Preset { get; set; }

        [Display(Name = "Label Style", Order = 2, GroupName = "01. Timeframe Preset", Description = "TheStrat: standard Strat combo notation (e.g. 2d-1-2u HAM). Universal: plain-language labels - REV, CONT, INS, OUT, EXP, plus FAILING - with an up/down arrow for direction and a diamond for hammer/shooter confidence.")]
        [TypeConverter(typeof(FriendlyEnumConverter<StratLabelStyle>))]
        public StratLabelStyle LabelStyle { get; set; }

        // ---- 02. Timeframes (Custom preset only) ----
        [Display(Name = "Timeframe 1", Order = 10, GroupName = "02. Timeframes (Custom preset)")] public bool Tf1Enabled { get; set; }
        [Display(Name = "Timeframe 1 TF", Order = 11, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(FriendlyEnumConverter<StratTimeframe>))] public StratTimeframe Tf1 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 1 Width", Order = 12, GroupName = "02. Timeframes (Custom preset)")] public int Tf1LineWidth { get; set; }
        [Display(Name = "Timeframe 1 Open", Order = 13, GroupName = "02. Timeframes (Custom preset)")] public bool Tf1ShowOpen { get; set; }
        [Display(Name = "Timeframe 2", Order = 20, GroupName = "02. Timeframes (Custom preset)")] public bool Tf2Enabled { get; set; }
        [Display(Name = "Timeframe 2 TF", Order = 21, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(FriendlyEnumConverter<StratTimeframe>))] public StratTimeframe Tf2 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 2 Width", Order = 22, GroupName = "02. Timeframes (Custom preset)")] public int Tf2LineWidth { get; set; }
        [Display(Name = "Timeframe 2 Open", Order = 23, GroupName = "02. Timeframes (Custom preset)")] public bool Tf2ShowOpen { get; set; }
        [Display(Name = "Timeframe 3", Order = 30, GroupName = "02. Timeframes (Custom preset)")] public bool Tf3Enabled { get; set; }
        [Display(Name = "Timeframe 3 TF", Order = 31, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(FriendlyEnumConverter<StratTimeframe>))] public StratTimeframe Tf3 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 3 Width", Order = 32, GroupName = "02. Timeframes (Custom preset)")] public int Tf3LineWidth { get; set; }
        [Display(Name = "Timeframe 3 Open", Order = 33, GroupName = "02. Timeframes (Custom preset)")] public bool Tf3ShowOpen { get; set; }
        [Display(Name = "Timeframe 4", Order = 40, GroupName = "02. Timeframes (Custom preset)")] public bool Tf4Enabled { get; set; }
        [Display(Name = "Timeframe 4 TF", Order = 41, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(FriendlyEnumConverter<StratTimeframe>))] public StratTimeframe Tf4 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 4 Width", Order = 42, GroupName = "02. Timeframes (Custom preset)")] public int Tf4LineWidth { get; set; }
        [Display(Name = "Timeframe 4 Open", Order = 43, GroupName = "02. Timeframes (Custom preset)")] public bool Tf4ShowOpen { get; set; }
        [Display(Name = "Timeframe 5", Order = 50, GroupName = "02. Timeframes (Custom preset)")] public bool Tf5Enabled { get; set; }
        [Display(Name = "Timeframe 5 TF", Order = 51, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(FriendlyEnumConverter<StratTimeframe>))] public StratTimeframe Tf5 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 5 Width", Order = 52, GroupName = "02. Timeframes (Custom preset)")] public int Tf5LineWidth { get; set; }
        [Display(Name = "Timeframe 5 Open", Order = 53, GroupName = "02. Timeframes (Custom preset)")] public bool Tf5ShowOpen { get; set; }
        [Display(Name = "Timeframe 6", Order = 60, GroupName = "02. Timeframes (Custom preset)")] public bool Tf6Enabled { get; set; }
        [Display(Name = "Timeframe 6 TF", Order = 61, GroupName = "02. Timeframes (Custom preset)")][TypeConverter(typeof(FriendlyEnumConverter<StratTimeframe>))] public StratTimeframe Tf6 { get; set; }
        [Range(1, 5)][Display(Name = "Timeframe 6 Width", Order = 62, GroupName = "02. Timeframes (Custom preset)")] public int Tf6LineWidth { get; set; }
        [Display(Name = "Timeframe 6 Open", Order = 63, GroupName = "02. Timeframes (Custom preset)")] public bool Tf6ShowOpen { get; set; }

        // ---- 03. Signals - Reversals ----
        [Display(Name = "Inside Reversals", Order = 1, GroupName = "03. Signals - Reversals", Description = "C1 is inside, CC breaks opposite C2's direction (e.g. 2d-1-2u).")] public bool ShowInsideReversals { get; set; }
        [Display(Name = "  Inside Reversals: HAM/SHO", Order = 2, GroupName = "03. Signals - Reversals", Description = "Require C1 to be a hammer or shooter.")] public bool InsideRevOnlyMomo { get; set; }
        [Display(Name = "  Inside Reversals: FTFC", Order = 3, GroupName = "03. Signals - Reversals", Description = "Require all monitored timeframes to agree on direction.")] public bool InsideRevRequireFTFC { get; set; }
        [Display(Name = "2-2 Reversals", Order = 4, GroupName = "03. Signals - Reversals", Description = "Direct reversal with no inside bar pause (e.g. 2d-2u).")] public bool ShowAllReversals { get; set; }
        [Display(Name = "  2-2 Reversals: HAM/SHO", Order = 5, GroupName = "03. Signals - Reversals")] public bool ReversalsRequireActionable { get; set; }
        [Display(Name = "  2-2 Reversals: F2", Order = 6, GroupName = "03. Signals - Reversals", Description = "Require C1 to be a Failed 2 itself (e.g. F2d-2u).")] public bool ReversalsRequireC1F2 { get; set; }
        [Display(Name = "  2-2 Reversals: FTFC", Order = 7, GroupName = "03. Signals - Reversals")] public bool ReversalsRequireFTFC { get; set; }

        // ---- 04. Signals - Continuations ----
        [Display(Name = "Inside Continuations", Order = 1, GroupName = "04. Signals - Continuations", Description = "C1 is inside, CC breaks same direction as C2 (e.g. 2u-1-2u).")] public bool ShowInsideContinuations { get; set; }
        [Display(Name = "  Inside Continuations: HAM/SHO", Order = 2, GroupName = "04. Signals - Continuations")] public bool InsideContOnlyMomo { get; set; }
        [Display(Name = "  Inside Continuations: FTFC", Order = 3, GroupName = "04. Signals - Continuations")] public bool InsideContRequireFTFC { get; set; }
        [Display(Name = "2-2 Continuations", Order = 4, GroupName = "04. Signals - Continuations", Description = "Sustained momentum with no pause (e.g. 2u-2u).")] public bool ShowContinuations { get; set; }
        [Display(Name = "  2-2 Continuations: HAM/SHO", Order = 5, GroupName = "04. Signals - Continuations")] public bool TwoTwoContOnlyMomo { get; set; }
        [Display(Name = "  2-2 Continuations: FTFC", Order = 6, GroupName = "04. Signals - Continuations")] public bool ContinuationsRequireFTFC { get; set; }

        // ---- 05. Signals - Reclaims ----
        [Display(Name = "Failing 2s (Range Reclaims)", Order = 1, GroupName = "05. Signals - Reclaims", Description = "Range Reclaim signals where price reverses after breaking out. Also known as Failing 2s, Potential 3, or 1-bar Rev Strat.")] public bool ShowFailing2s { get; set; }
        [Display(Name = "  Failing 2s: FTFC", Order = 2, GroupName = "05. Signals - Reclaims")] public bool Failing2sRequireFTFC { get; set; }

        // ---- 06. Signals - Expansions ----
        [Display(Name = "3-2 Expansions", Order = 1, GroupName = "06. Signals - Expansions", Description = "C1 was an outside bar, CC commits to one direction (e.g. 3u-2u).")] public bool ShowRangeExpansions { get; set; }
        [Display(Name = "  3-2 Expansions: HAM/SHO", Order = 2, GroupName = "06. Signals - Expansions")] public bool RangeExpOnlyMomo { get; set; }
        [Display(Name = "  3-2 Expansions: FTFC", Order = 3, GroupName = "06. Signals - Expansions")] public bool RangeExpansionsRequireFTFC { get; set; }
        [Display(Name = "Outside Bars (3 Exp)", Order = 4, GroupName = "06. Signals - Expansions", Description = "CC itself is an outside bar. Direction by close vs open.")] public bool Show3Expansions { get; set; }
        [Display(Name = "  Outside Bars: FTFC", Order = 5, GroupName = "06. Signals - Expansions")] public bool ThreeExpRequireFTFC { get; set; }

        // ---- 07. Filters ----
        [Display(Name = "Hammer/Shooter Definition", Order = 1, GroupName = "07. Filters", Description = "Broad: body position. Classic: small body, long wick (3x body). Pin Bar: stricter wick requirements.")]
        [TypeConverter(typeof(FriendlyEnumConverter<HammerDefinition>))]
        public HammerDefinition HammerLogic { get; set; }
        [Display(Name = "Match Candle Color", Order = 2, GroupName = "07. Filters", Description = "Hammers must close up and shooters must not close up.")] public bool MatchCandleColor { get; set; }
        [Range(2, 6)][Display(Name = "Domino: Minimum Timeframes", Order = 3, GroupName = "07. Filters")] public int MinDominoTFs { get; set; }
        [Display(Name = "Domino: Show in Data Table", Order = 4, GroupName = "07. Filters")] public bool ShowDominoInTable { get; set; }
        [Display(Name = "Only Show Signals Following the Lead", Order = 5, GroupName = "07. Filters", Description = "Only show lower-timeframe signals that align with the highest timeframe signal in force (the Lead).")] public bool OnlyFollowTheLead { get; set; }

        // ---- 08. Targets ----
        [Display(Name = "Show Magnitude Levels", Order = 1, GroupName = "08. Targets")] public bool ShowMagnitude { get; set; }
        [Display(Name = "  Magnitude: Only When In-Force", Order = 2, GroupName = "08. Targets")] public bool MagOnlyWhenInForce { get; set; }
        [Display(Name = "Show Exhaustion Levels", Order = 3, GroupName = "08. Targets")] public bool ShowExhaustion { get; set; }
        [Display(Name = "  Exhaustion: Only When In-Force", Order = 4, GroupName = "08. Targets")] public bool ExhOnlyWhenInForce { get; set; }
        [Display(Name = "  Exhaustion: Only After Magnitude Hit", Order = 5, GroupName = "08. Targets")] public bool ExhRequiresMagHit { get; set; }
        [Display(Name = "Show Take Action Windows", Order = 6, GroupName = "08. Targets")] public bool ShowTakeActionWindows { get; set; }
        [Display(Name = "  TAW: Only When In-Force", Order = 7, GroupName = "08. Targets")] public bool TawOnlyWhenInForce { get; set; }
        [Display(Name = "  TAW: Extend to Exhaustion", Order = 8, GroupName = "08. Targets")] public bool TawExtendToExhaustion { get; set; }
        [Display(Name = "    Extend: Only When In-Force", Order = 9, GroupName = "08. Targets")] public bool TawExhOnlyWhenInForce { get; set; }
        [Range(0, 100)][Display(Name = "  TAW: Fill Opacity", Order = 10, GroupName = "08. Targets")] public int TawFillOpacity { get; set; }
        [Range(0, 100)][Display(Name = "  TAW: Border Opacity", Order = 11, GroupName = "08. Targets")] public int TawBorderOpacity { get; set; }

        // ---- 09. Stops ----
        [Display(Name = "Stops: Enable", Order = 1, GroupName = "09. Stops", Description = "Once a signal goes in force, the stop is locked for the rest of that period.")] public bool ShowStopLevels { get; set; }
        [Display(Name = "Reference", Order = 2, GroupName = "09. Stops", Description = "CC: breakout bar's opposite extreme. C1: prior bar's opposite side. Failing 2s always use CC.")]
        [TypeConverter(typeof(FriendlyEnumConverter<StopReferenceMode>))]
        public StopReferenceMode StopReference { get; set; }
        [Display(Name = "Smallest Timeframe Only", Order = 3, GroupName = "09. Stops")] public bool StopSmallestOnly { get; set; }
        [Display(Name = "Break Even at Magnitude", Order = 4, GroupName = "09. Stops")] public bool StopBEatMag { get; set; }
        [Display(Name = "Break Even at Exhaustion", Order = 5, GroupName = "09. Stops")] public bool StopBEatExh { get; set; }
        [Display(Name = "Color Mode", Order = 6, GroupName = "09. Stops")]
        [TypeConverter(typeof(FriendlyEnumConverter<StopColorMode>))]
        public StopColorMode StopColor { get; set; }
        [XmlIgnore][Display(Name = "Custom Color", Order = 7, GroupName = "09. Stops")] public Brush StopCustomColor { get; set; }
        [Browsable(false)] public string StopCustomColorSerializable { get { return Serialize.BrushToString(StopCustomColor); } set { StopCustomColor = Serialize.StringToBrush(value); } }

        // ---- 10. Display ----
        [Display(Name = "Preview Mode", Order = 1, GroupName = "10. Display", Description = "Off: never preview. On: always preview next period levels. Auto: previews once the chart's last bar has passed its scheduled close and the market is closed per the instrument's trading hours (holidays included), or 4 hours after the last bar's close.")]
        [TypeConverter(typeof(FriendlyEnumConverter<PreviewModeOption>))]
        public PreviewModeOption PreviewMode { get; set; }
        [Display(Name = "Show Data Table", Order = 2, GroupName = "10. Display")] public bool ShowDataTable { get; set; }
        [Display(Name = "Table Mode", Order = 3, GroupName = "10. Display")]
        [TypeConverter(typeof(FriendlyEnumConverter<TableModeOption>))]
        public TableModeOption TableMode { get; set; }
        [Display(Name = "Compact Cell Color", Order = 4, GroupName = "10. Display")]
        [TypeConverter(typeof(FriendlyEnumConverter<CompactCellColorMode>))]
        public CompactCellColorMode CompactCellColor { get; set; }
        [Display(Name = "Table Position", Order = 5, GroupName = "10. Display")]
        [TypeConverter(typeof(FriendlyEnumConverter<TablePositionOption>))]
        public TablePositionOption TablePosition { get; set; }
        [Display(Name = "Table Text Size", Order = 6, GroupName = "10. Display")]
        [TypeConverter(typeof(FriendlyEnumConverter<TextSizeOption>))]
        public TextSizeOption TableTextSize { get; set; }
        [Display(Name = "Use Dash Separator", Order = 7, GroupName = "10. Display")] public bool UseDashSeparator { get; set; }
        [Display(Name = "Show Timeline Labels", Order = 8, GroupName = "10. Display")] public bool ShowTimelineLabels { get; set; }
        [Range(0, 20)][Display(Name = "  Timeline Offset (bars)", Order = 9, GroupName = "10. Display")] public int LabelOffset { get; set; }
        [Display(Name = "  Keep Timeline Labels On Screen", Order = 10, GroupName = "10. Display", Description = "NinjaTrader cannot scroll past its right margin, so a label anchored at a far period end is pulled back to the chart's right edge.")] public bool ClampTimelineLabels { get; set; }
        [Display(Name = "Show Floating Labels", Order = 11, GroupName = "10. Display")] public bool ShowFloatingLabels { get; set; }
        [Range(-20, 50)][Display(Name = "  Floating Offset (bars)", Order = 12, GroupName = "10. Display")] public int FloatingLabelOffset { get; set; }
        [Display(Name = "Show Price in Label", Order = 13, GroupName = "10. Display")] public bool ShowPriceInLabel { get; set; }
        [Display(Name = "Show H/L in Labels", Order = 14, GroupName = "10. Display")] public bool ShowHighLowInLabel { get; set; }
        [Display(Name = "Label Text Size", Order = 15, GroupName = "10. Display")]
        [TypeConverter(typeof(FriendlyEnumConverter<TextSizeOption>))]
        public TextSizeOption LabelTextSize { get; set; }
        [XmlIgnore][Display(Name = "Label Background", Order = 16, GroupName = "10. Display")] public Brush LabelBackground { get; set; }
        [Browsable(false)] public string LabelBackgroundSerializable { get { return Serialize.BrushToString(LabelBackground); } set { LabelBackground = Serialize.StringToBrush(value); } }
        [Range(0, 100)][Display(Name = "Label Transparency", Order = 17, GroupName = "10. Display")] public int LabelBackgroundTransparency { get; set; }

        // ---- 11. Bar Coloring ----
        [Display(Name = "Color Chart Candles", Order = 1, GroupName = "11. Bar Coloring")]
        [TypeConverter(typeof(FriendlyEnumConverter<BarColorModeOption>))]
        public BarColorModeOption BarColorMode { get; set; }
        [Display(Name = "Strat: 1 (Inside)", Order = 2, GroupName = "11. Bar Coloring")] public bool PaintInsideBars { get; set; }
        [Display(Name = "Strat: 2 (Directional)", Order = 3, GroupName = "11. Bar Coloring")] public bool Paint2Bars { get; set; }
        [Display(Name = "Strat: 3 (Outside)", Order = 4, GroupName = "11. Bar Coloring")] public bool PaintOutsideBars { get; set; }
        [Display(Name = "Strat: F2 (Failing)", Order = 5, GroupName = "11. Bar Coloring")] public bool PaintFailedBars { get; set; }
        [XmlIgnore][Display(Name = "FTFC Up", Order = 6, GroupName = "11. Bar Coloring")] public Brush FtfcUpColor { get; set; }
        [Browsable(false)] public string FtfcUpColorSerializable { get { return Serialize.BrushToString(FtfcUpColor); } set { FtfcUpColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "FTFC Down", Order = 7, GroupName = "11. Bar Coloring")] public Brush FtfcDownColor { get; set; }
        [Browsable(false)] public string FtfcDownColorSerializable { get { return Serialize.BrushToString(FtfcDownColor); } set { FtfcDownColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "FTFC Conflict", Order = 8, GroupName = "11. Bar Coloring")] public Brush FtfcConflictColor { get; set; }
        [Browsable(false)] public string FtfcConflictColorSerializable { get { return Serialize.BrushToString(FtfcConflictColor); } set { FtfcConflictColor = Serialize.StringToBrush(value); } }
        [Display(Name = "Color Conflict Bars", Order = 9, GroupName = "11. Bar Coloring")] public bool PaintFtfcConflict { get; set; }
        [Display(Name = "Highlight Failing 2 Flips", Order = 10, GroupName = "11. Bar Coloring")] public bool HighlightFailing2Flips { get; set; }

        // ---- 12. Style ----
        [XmlIgnore][Display(Name = "Bullish Signal", Order = 1, GroupName = "12. Style")] public Brush BullSignalColor { get; set; }
        [Browsable(false)] public string BullSignalColorSerializable { get { return Serialize.BrushToString(BullSignalColor); } set { BullSignalColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bullish Crossed", Order = 2, GroupName = "12. Style")] public Brush BullCrossedColor { get; set; }
        [Browsable(false)] public string BullCrossedColorSerializable { get { return Serialize.BrushToString(BullCrossedColor); } set { BullCrossedColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bullish 1 (Inside Bar)", Order = 3, GroupName = "12. Style")] public Brush BullInsideColor { get; set; }
        [Browsable(false)] public string BullInsideColorSerializable { get { return Serialize.BrushToString(BullInsideColor); } set { BullInsideColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bullish 3 (Outside Bar)", Order = 4, GroupName = "12. Style")] public Brush BullOutsideColor { get; set; }
        [Browsable(false)] public string BullOutsideColorSerializable { get { return Serialize.BrushToString(BullOutsideColor); } set { BullOutsideColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bullish Range Reclaim (F2d)", Order = 5, GroupName = "12. Style")] public Brush BullReclaimColor { get; set; }
        [Browsable(false)] public string BullReclaimColorSerializable { get { return Serialize.BrushToString(BullReclaimColor); } set { BullReclaimColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish Signal", Order = 6, GroupName = "12. Style")] public Brush BearSignalColor { get; set; }
        [Browsable(false)] public string BearSignalColorSerializable { get { return Serialize.BrushToString(BearSignalColor); } set { BearSignalColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish Crossed", Order = 7, GroupName = "12. Style")] public Brush BearCrossedColor { get; set; }
        [Browsable(false)] public string BearCrossedColorSerializable { get { return Serialize.BrushToString(BearCrossedColor); } set { BearCrossedColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish 1 (Inside Bar)", Order = 8, GroupName = "12. Style")] public Brush BearInsideColor { get; set; }
        [Browsable(false)] public string BearInsideColorSerializable { get { return Serialize.BrushToString(BearInsideColor); } set { BearInsideColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish 3 (Outside Bar)", Order = 9, GroupName = "12. Style")] public Brush BearOutsideColor { get; set; }
        [Browsable(false)] public string BearOutsideColorSerializable { get { return Serialize.BrushToString(BearOutsideColor); } set { BearOutsideColor = Serialize.StringToBrush(value); } }
        [XmlIgnore][Display(Name = "Bearish Range Reclaim (F2u)", Order = 10, GroupName = "12. Style")] public Brush BearReclaimColor { get; set; }
        [Browsable(false)] public string BearReclaimColorSerializable { get { return Serialize.BrushToString(BearReclaimColor); } set { BearReclaimColor = Serialize.StringToBrush(value); } }

        // ---- 13. Alerts ----
        [Display(Name = "Enable Alerts", Order = 1, GroupName = "13. Alerts", Description = "Master switch. Alerts appear in NinjaTrader's Alerts Log and play the sound below.")] public bool EnableAlerts { get; set; }
        [Display(Name = "Sound File", Order = 2, GroupName = "13. Alerts", Description = "A file in NinjaTrader's sounds folder, e.g. Alert1.wav. Leave blank for no sound.")] public string AlertSound { get; set; }
        [Display(Name = "Signal In-Force (Any) - consolidated message", Order = 3, GroupName = "13. Alerts")] public bool AlertSignalInForceAny { get; set; }
        [Display(Name = "  Include TF1", Order = 4, GroupName = "13. Alerts")] public bool AlertIncludeTF1 { get; set; }
        [Display(Name = "  Include TF2", Order = 5, GroupName = "13. Alerts")] public bool AlertIncludeTF2 { get; set; }
        [Display(Name = "  Include TF3", Order = 6, GroupName = "13. Alerts")] public bool AlertIncludeTF3 { get; set; }
        [Display(Name = "  Include TF4", Order = 7, GroupName = "13. Alerts")] public bool AlertIncludeTF4 { get; set; }
        [Display(Name = "  Include TF5", Order = 8, GroupName = "13. Alerts")] public bool AlertIncludeTF5 { get; set; }
        [Display(Name = "  Include TF6", Order = 9, GroupName = "13. Alerts")] public bool AlertIncludeTF6 { get; set; }
        [Display(Name = "  Include FTFC", Order = 10, GroupName = "13. Alerts")] public bool AlertShowFTFC { get; set; }
        [Display(Name = "  Include Trigger", Order = 11, GroupName = "13. Alerts")] public bool AlertShowTrigger { get; set; }
        [Display(Name = "  Include MAG", Order = 12, GroupName = "13. Alerts")] public bool AlertShowMagnitude { get; set; }
        [Display(Name = "  Include EXH", Order = 13, GroupName = "13. Alerts")] public bool AlertShowExhaustion { get; set; }
        [Display(Name = "  Include STOP", Order = 14, GroupName = "13. Alerts")] public bool AlertShowStop { get; set; }
        [Display(Name = "  Include Domino", Order = 15, GroupName = "13. Alerts")] public bool IncludeDominoInConsolidated { get; set; }
        [Display(Name = "Signal In-Force (TF1)", Order = 16, GroupName = "13. Alerts")] public bool AlertTF1InForce { get; set; }
        [Display(Name = "Signal In-Force (TF2)", Order = 17, GroupName = "13. Alerts")] public bool AlertTF2InForce { get; set; }
        [Display(Name = "Signal In-Force (TF3)", Order = 18, GroupName = "13. Alerts")] public bool AlertTF3InForce { get; set; }
        [Display(Name = "Signal In-Force (TF4)", Order = 19, GroupName = "13. Alerts")] public bool AlertTF4InForce { get; set; }
        [Display(Name = "Signal In-Force (TF5)", Order = 20, GroupName = "13. Alerts")] public bool AlertTF5InForce { get; set; }
        [Display(Name = "Signal In-Force (TF6)", Order = 21, GroupName = "13. Alerts")] public bool AlertTF6InForce { get; set; }
        [Display(Name = "Signal In-Force (Bullish)", Order = 22, GroupName = "13. Alerts")] public bool AlertBullishInForce { get; set; }
        [Display(Name = "Signal In-Force (Bearish)", Order = 23, GroupName = "13. Alerts")] public bool AlertBearishInForce { get; set; }
        [Display(Name = "New Potential (Any)", Order = 24, GroupName = "13. Alerts")] public bool AlertNewPotential { get; set; }
        [Display(Name = "Domino Setup", Order = 25, GroupName = "13. Alerts")] public bool AlertDominoSetup { get; set; }
        [Display(Name = "FTFC Shifted", Order = 26, GroupName = "13. Alerts")] public bool AlertFtfcShifted { get; set; }
        [Display(Name = "FTFC Up", Order = 27, GroupName = "13. Alerts")] public bool AlertFtfcUp { get; set; }
        [Display(Name = "FTFC Down", Order = 28, GroupName = "13. Alerts")] public bool AlertFtfcDown { get; set; }
        [Display(Name = "FTFC Conflict", Order = 29, GroupName = "13. Alerts")] public bool AlertFtfcConflict { get; set; }

        // ---- 14. Advanced ----
        [Display(Name = "Exhaustion Disables 'In Force'", Order = 1, GroupName = "14. Advanced")] public bool ExhDisablesInForce { get; set; }
        [Display(Name = "Exhaustion Excludes from FTFC", Order = 2, GroupName = "14. Advanced", Description = "Affects the data table's FTFC row only, as in the Suite.")] public bool ExhDisablesFTFC { get; set; }
        [Display(Name = "Show Magnitude for Outside Bars", Order = 3, GroupName = "14. Advanced")] public bool Show3ExpMagnitude { get; set; }
        [Display(Name = "Show Exhaustion for Outside Bars", Order = 4, GroupName = "14. Advanced")] public bool Show3ExpExhaustion { get; set; }
        [Display(Name = "Enable Failing 2 Detection", Order = 5, GroupName = "14. Advanced")] public bool EnableFailed2Detection { get; set; }
        [Display(Name = "Failing 2 Detection Method", Order = 6, GroupName = "14. Advanced", Description = "Reclaim: closes back inside C1 range. Open: closes against breakout direction. Reclaim + Open: both. Reclaim OR Open: either.")]
        [TypeConverter(typeof(FriendlyEnumConverter<F2Method>))]
        public F2Method Failed2DetectionMethod { get; set; }
        [Display(Name = "Show Failing 2 Open Level", Order = 7, GroupName = "14. Advanced")] public bool ShowF2OpenLine { get; set; }
        [Display(Name = "Color TF When In-Force", Order = 8, GroupName = "14. Advanced")] public bool ColorTFWhenInForce { get; set; }
        [Display(Name = "TAW: Include Failing 2s", Order = 9, GroupName = "14. Advanced")] public bool TawIncludeFailing2s { get; set; }
        [Display(Name = "TAW: Include Outside Bars", Order = 10, GroupName = "14. Advanced")] public bool TawIncludeOutsideBars { get; set; }
        [Display(Name = "HTF Trading Hours", Order = 11, GroupName = "14. Advanced", Description = "Trading hours template for the higher-timeframe data (e.g. 'CME US Index Futures ETH'). Blank uses the instrument's default template. Match your chart's template.")] public string HtfTradingHours { get; set; }
        [Range(200, 20000)][Display(Name = "Max Daily Bars To Load", Order = 12, GroupName = "14. Advanced", Description = "Daily history behind the D, W, M, 3M, 6M and 12M slots. Monthly exhaustion needs about 1,100 daily bars, quarterly about 3,300.")] public int MaxDailyBars { get; set; }

        // ---- 15. Debug ----
        [Display(Name = "Show Debug Panel", Order = 1, GroupName = "15. Debug")] public bool ShowDebugPanel { get; set; }
        [Display(Name = "Timeframe to Debug", Order = 2, GroupName = "15. Debug")] public DebugSlot DebugTimeframe { get; set; }

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
