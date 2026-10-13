// Runs the NinjaTrader indicator end to end against the functional stubs in
// stubs/NinjaTraderStubs.cs: synthetic ES-like sessions, every preset, most toggles, a 5-minute
// and a daily chart, historical replay (with the higher-timeframe series fully loaded and with
// it trailing the chart), realtime ticks, preview and a render pass. It fails on any exception
// and prints a one-line summary per run. It proves the code paths run; it does not prove the
// output matches TradingView (see ninjatrader/README.md for the in-app checks).
using System;
using System.Collections.Generic;
using System.Reflection;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Indicators.StratSuiteCore;

public class SmokeDriver : TheStratSuite
{
    public void Step(State s) { State = s; OnStateChange(); }
    public void Bar() { OnBarUpdate(); }
    public void Render() { OnRender(ChartControl, new NinjaTrader.Gui.Chart.ChartScale()); }
}

public static class SmokeTest
{
    static Bars minute1, daily;
    static readonly Random rng = new Random(11);
    static readonly Dictionary<string, long> Fingerprints = new Dictionary<string, long>();

    static void Build()
    {
        minute1 = new Bars { Period = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 } };
        daily = new Bars { Period = new BarsPeriod { BarsPeriodType = BarsPeriodType.Day, Value = 1 } };
        DateTime lastDay = new DateTime(2026, 9, 25);
        List<DateTime> days = new List<DateTime>();
        for (DateTime d = lastDay; days.Count < 2600; d = d.AddDays(-1))
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) days.Insert(0, d);
        double px = 1500;
        for (int i = 0; i < days.Count; i++)
        {
            bool intraday = i >= days.Count - 30;
            double o = px, h = px, l = px;
            if (intraday)
            {
                DateTime begin = days[i].AddDays(-1).AddHours(18);
                for (int m = 1; m <= 23 * 60; m++)
                {
                    double mo = px;
                    double step = Math.Round((rng.NextDouble() - 0.5) * 8) * 0.25;
                    px = Math.Max(10, px + step);
                    double mh = Math.Max(mo, px) + Math.Round(rng.NextDouble() * 2) * 0.25;
                    double ml = Math.Min(mo, px) - Math.Round(rng.NextDouble() * 2) * 0.25;
                    minute1.T.Add(begin.AddMinutes(m)); minute1.O.Add(mo); minute1.H.Add(mh); minute1.L.Add(ml); minute1.C.Add(px);
                    h = Math.Max(h, mh); l = Math.Min(l, ml);
                }
            }
            else
            {
                double range = px * 0.012 * (0.3 + rng.NextDouble());
                double close = Math.Round((px + (rng.NextDouble() - 0.49) * range) * 4) / 4;
                h = Math.Round((Math.Max(px, close) + rng.NextDouble() * range / 2) * 4) / 4;
                l = Math.Round((Math.Min(px, close) - rng.NextDouble() * range / 2) * 4) / 4;
                px = close;
            }
            daily.T.Add(days[i]); daily.O.Add(o); daily.H.Add(h); daily.L.Add(l); daily.C.Add(px);
        }
    }

    // Session-anchored aggregation, bars stamped at their end time (NinjaTrader convention).
    static Bars Aggregate(int minutes, int keepLast)
    {
        Bars b = new Bars { Period = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = minutes } };
        SessionIterator si = new SessionIterator(minute1);
        for (int i = 0; i < minute1.Count; i++)
        {
            DateTime t = minute1.GetTime(i);
            si.GetNextSession(t, true);
            double mins = (t - si.ActualSessionBegin).TotalMinutes;
            DateTime end = si.ActualSessionBegin.AddMinutes(Math.Ceiling(mins / minutes) * minutes);
            if (end > si.ActualSessionEnd) end = si.ActualSessionEnd;
            int n = b.T.Count;
            if (n > 0 && b.T[n - 1] == end)
            {
                b.H[n - 1] = Math.Max(b.H[n - 1], minute1.H[i]);
                b.L[n - 1] = Math.Min(b.L[n - 1], minute1.L[i]);
                b.C[n - 1] = minute1.C[i];
            }
            else
            {
                b.T.Add(end); b.O.Add(minute1.O[i]); b.H.Add(minute1.H[i]); b.L.Add(minute1.L[i]); b.C.Add(minute1.C[i]);
            }
        }
        return Trim(b, keepLast);
    }

    static Bars Trim(Bars src, int keepLast)
    {
        if (keepLast <= 0 || src.T.Count <= keepLast) return src;
        Bars b = new Bars { Period = src.Period };
        int from = src.T.Count - keepLast;
        b.T.AddRange(src.T.GetRange(from, keepLast)); b.O.AddRange(src.O.GetRange(from, keepLast));
        b.H.AddRange(src.H.GetRange(from, keepLast)); b.L.AddRange(src.L.GetRange(from, keepLast)); b.C.AddRange(src.C.GetRange(from, keepLast));
        return b;
    }

    static string Run(string name, bool dailyChart, bool trailing, Action<TheStratSuite> configure)
    {
        SmokeDriver ind = new SmokeDriver();
        ind.Step(State.SetDefaults);
        configure(ind);
        ind.Instrument = new NinjaTrader.Cbi.Instrument { FullName = "ES 12-26" };
        ind.TickSize = 0.25;
        ind.Step(State.Configure);
        Bars chart = dailyChart ? Trim(daily, 400) : Aggregate(5, 0);
        List<Bars> arr = new List<Bars> { chart };
        foreach (SeriesRequest r in ind.Requests)
            arr.Add(r.Period.BarsPeriodType == BarsPeriodType.Day ? Trim(daily, r.BarsToLoad) : Aggregate(r.Period.Value, r.BarsToLoad));
        ind.BarsArray = arr.ToArray();
        ind.CurrentBars = new int[arr.Count];
        ind.BarBrushes.Current = ind.CandleOutlineBrushes.Current = () => ind.CurrentBars[0];
        ind.ChartControl = new NinjaTrader.Gui.Chart.ChartControl();
        ind.ChartPanel = new NinjaTrader.Gui.Chart.ChartPanel();
        ind.RenderTarget = new SharpDX.Direct2D1.RenderTarget();
        ind.Step(State.DataLoaded);
        ind.Step(State.Historical);
        int total = chart.T.Count;
        SessionIterator si = new SessionIterator(chart);
        for (int k = 0; k < total; k++)
        {
            // The last bar is the live one, where the forming higher-timeframe candles exist.
            if (trailing && k < total - 1)
            {
                // Higher-timeframe series show only candles that closed by this chart bar.
                DateTime t = chart.T[k];
                DateTime tradingDay = t.Date;
                if (!dailyChart) { si.GetNextSession(t, true); tradingDay = si.ActualTradingDayExchange; }
                for (int j = 1; j < arr.Count; j++)
                {
                    Bars b = arr[j];
                    int c = 0;
                    DateTime limit = b.Period.BarsPeriodType == BarsPeriodType.Day ? tradingDay.AddDays(dailyChart ? 1 : 0)
                        : dailyChart ? tradingDay.AddHours(17).AddSeconds(1) : t;
                    while (c < b.T.Count && b.T[c] < limit) c++;
                    // The first loaded candle has no predecessor to build from, so it shows once the
                    // chart reaches it, as it would in a series that starts mid-history.
                    bool isDay = b.Period.BarsPeriodType == BarsPeriodType.Day;
                    if (c == 0 && b.T.Count > 0 && (isDay ? tradingDay >= b.T[0] : t > b.T[0].AddMinutes(-b.Period.Value)))
                        c = 1;
                    b.Visible = c;
                }
            }

            if (k == total - 1)
                foreach (Bars b in arr) b.Visible = int.MaxValue;
            ind.CurrentBars[0] = k;
            ind.BarsInProgress = 0;
            ind.Bar();
            if (Trace != null) Trace.Add(StateDump(ind));
        }
        foreach (Bars b in arr) b.Visible = int.MaxValue;
        long fp = 17;
        for (int k = 0; k < total; k++)
        {
            foreach (var v in ind.Values) { double x; fp = fp * 31 + (v.Data.TryGetValue(k, out x) ? (long)(x * 3 + 7) : 1); }
            System.Windows.Media.Brush br;
            fp = fp * 31 + (ind.BarBrushes.Data.TryGetValue(k, out br) && br != null ? ((System.Windows.Media.SolidColorBrush)br).Color.R + 1 : 0);
        }
        ind.Step(State.Realtime);
        // Realtime ticks on the last bar, then a weekend clock for Auto preview.
        for (int tick = 0; tick < 5; tick++)
        {
            chart.C[total - 1] += 0.25 * (tick % 2 == 0 ? 3 : -2);
            chart.H[total - 1] = Math.Max(chart.H[total - 1], chart.C[total - 1]);
            chart.L[total - 1] = Math.Min(chart.L[total - 1], chart.C[total - 1]);
            ind.Bar();
        }
        ind.ChartBars = new NinjaTrader.Gui.Chart.ChartBars { Count = total };
        NinjaTrader.Core.Globals.NowOverride = new DateTime(2026, 9, 25, 16, 0, 0);
        ind.Bar();
        ind.Render();
        string live = Summary(ind);
        NinjaTrader.Core.Globals.NowOverride = new DateTime(2026, 9, 26, 12, 0, 0);
        ind.Bar();
        ind.Render();
        string weekend = Summary(ind);
        NinjaTrader.Core.Globals.NowOverride = null;
        ind.Step(State.Terminated);
        Fingerprints[name] = fp;
        return string.Format("{0,-34} history={4:X16} live[{1}]  weekend[{2}]  alerts={3}", name, live, weekend, ind.Alerts.Count, fp);
    }

    public static List<string> Trace;

    // Every field of every slot's state after the bar, for locating a divergence.
    static string StateDump(TheStratSuite ind)
    {
        object st = typeof(TheStratSuite).GetField("working", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ind);
        Array slots = (Array)st.GetType().GetField("Slots").GetValue(st);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < slots.Length; i++)
        {
            object ss = slots.GetValue(i);
            sb.Append("slot" + i + ":");
            foreach (FieldInfo f in ss.GetType().GetFields())
                sb.Append(" " + f.Name + "=" + f.GetValue(ss));
            sb.Append("\n");
        }
        return sb.ToString();
    }

    static string Summary(TheStratSuite ind)
    {
        object model = typeof(TheStratSuite).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ind);
        if (model == null) return "no model";
        Type mt = model.GetType();
        int lines = ((System.Collections.ICollection)mt.GetField("Lines").GetValue(model)).Count;
        int boxes = ((System.Collections.ICollection)mt.GetField("Boxes").GetValue(model)).Count;
        int labels = ((System.Collections.ICollection)mt.GetField("Labels").GetValue(model)).Count;
        object table = mt.GetField("Table").GetValue(model);
        string last = "";
        if (table != null)
        {
            var rows = (System.Collections.IList)table.GetType().GetField("Rows").GetValue(table);
            foreach (object row in rows)
            {
                Array cells = (Array)row;
                if (cells.Length == 1) last += cells.GetValue(0).GetType().GetField("Text").GetValue(cells.GetValue(0)) + ";";
            }
        }
        return string.Format("lines={0} boxes={1} labels={2} {3}", lines, boxes, labels, last);
    }

    public static int Main(string[] args)
    {
        Build();
        if (args.Length == 2)
        {
            // Diagnose one preset: SmokeTest.exe <preset> <d|5>
            StratPreset preset = (StratPreset)Enum.Parse(typeof(StratPreset), args[0]);
            bool dc = args[1] == "d";
            Action<TheStratSuite> cf = i => { i.Preset = preset; i.Tf1Enabled = i.Tf2Enabled = i.Tf3Enabled = i.Tf4Enabled = true; };
            Trace = new List<string>(); Run("full", dc, false, cf); List<string> full = Trace;
            Trace = new List<string>(); Run("trailing", dc, true, cf); List<string> tr = Trace;
            for (int k = 0; k < full.Count; k++)
                if (full[k] != tr[k])
                {
                    string[] a = full[k].Split('\n'), b = tr[k].Split('\n');
                    Console.WriteLine("first difference at chart bar " + k);
                    for (int i = 0; i < a.Length; i++)
                        if (a[i] != b[i]) { Console.WriteLine("full:     " + a[i]); Console.WriteLine("trailing: " + b[i]); }
                    return 1;
                }
            Console.WriteLine("identical");
            return 0;
        }
        int failures = 0;
        List<KeyValuePair<string, Action<TheStratSuite>>> configs = new List<KeyValuePair<string, Action<TheStratSuite>>>();
        foreach (StratPreset p in Enum.GetValues(typeof(StratPreset)))
        {
            StratPreset pp = p;
            configs.Add(new KeyValuePair<string, Action<TheStratSuite>>(p.ToString(), i => { i.Preset = pp; i.Tf1Enabled = i.Tf2Enabled = i.Tf3Enabled = i.Tf4Enabled = true; }));
        }
        configs.Add(new KeyValuePair<string, Action<TheStratSuite>>("Everything on", i =>
        {
            i.ShowContinuations = i.ShowFailing2s = i.ShowRangeExpansions = i.Show3Expansions = true;
            i.ShowStopLevels = true; i.StopBEatMag = true; i.OnlyFollowTheLead = true; i.ShowF2OpenLine = true;
            i.ShowFloatingLabels = true; i.ShowPriceInLabel = true; i.ShowHighLowInLabel = true; i.ShowDebugPanel = true;
            i.BarColorMode = BarColorModeOption.StratCandles; i.EnableAlerts = true;
            i.AlertTF1InForce = i.AlertBullishInForce = i.AlertBearishInForce = i.AlertNewPotential = i.AlertDominoSetup = true;
            i.AlertFtfcShifted = i.AlertFtfcUp = i.AlertFtfcDown = i.AlertFtfcConflict = true;
            i.AlertShowFTFC = i.AlertShowTrigger = i.AlertShowMagnitude = i.AlertShowExhaustion = i.AlertShowStop = true;
            i.ExhDisablesInForce = i.ExhDisablesFTFC = true; i.Failed2DetectionMethod = F2Method.ReclaimOrOpen;
        }));
        configs.Add(new KeyValuePair<string, Action<TheStratSuite>>("FTFC candles, compact, universal", i =>
        {
            i.BarColorMode = BarColorModeOption.FtfcCandles; i.TableMode = TableModeOption.Compact; i.LabelStyle = StratLabelStyle.Universal;
            i.StopSmallestOnly = true; i.ShowStopLevels = true; i.PreviewMode = PreviewModeOption.On; i.Failed2DetectionMethod = F2Method.Open;
        }));
        configs.Add(new KeyValuePair<string, Action<TheStratSuite>>("Custom 5m..12M", i =>
        {
            i.Preset = StratPreset.Custom;
            i.Tf1Enabled = i.Tf2Enabled = i.Tf3Enabled = i.Tf4Enabled = i.Tf5Enabled = i.Tf6Enabled = true;
            i.Tf1 = StratTimeframe.Min5; i.Tf2 = StratTimeframe.Hour2; i.Tf3 = StratTimeframe.Hour8; i.Tf4 = StratTimeframe.Week; i.Tf5 = StratTimeframe.Quarter; i.Tf6 = StratTimeframe.Year;
            i.Tf1ShowOpen = i.Tf4ShowOpen = true; i.PreviewMode = PreviewModeOption.Off;
        }));
        foreach (var cfg in configs)
            foreach (bool dailyChart in new[] { false, true })
                foreach (bool trailing in new[] { false, true })
                {
                    string baseName = cfg.Key + (dailyChart ? " / D chart" : " / 5m chart");
                    string name = baseName + (trailing ? " / trailing" : "");
                    try { Console.WriteLine(Run(name, dailyChart, trailing, cfg.Value)); }
                    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
                    // No lookahead: history must not depend on how much HTF data is loaded ahead of the chart.
                    long a, b;
                    if (trailing && Fingerprints.TryGetValue(baseName, out a) && Fingerprints.TryGetValue(name, out b) && a != b)
                    {
                        failures++;
                        Console.WriteLine("FAIL " + baseName + ": history differs when the higher-timeframe series trails the chart");
                    }
                }
        Console.WriteLine(failures == 0 ? "OK" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
