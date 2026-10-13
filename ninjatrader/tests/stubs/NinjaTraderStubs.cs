// Minimal stand-ins for the NinjaTrader 8, WPF and SharpDX types TheStratSuite.cs uses, so the
// indicator can be type-checked with Mono's compiler on Linux (NinjaTrader itself is
// Windows-only). Signatures follow the NinjaTrader 8 help guide. Passing this compile means the
// file is well-formed C# against those signatures; it does not prove NinjaTrader accepts it.
// Nothing here runs.
using System;
using System.Collections.Generic;

namespace System.Windows.Media
{
    public struct Color { public byte A, R, G, B; public static Color FromArgb(byte a, byte r, byte g, byte b) { return new Color { A = a, R = r, G = g, B = b }; } public static Color FromRgb(byte r, byte g, byte b) { return FromArgb(255, r, g, b); } }
    public abstract class Brush { public void Freeze() { } }
    public class SolidColorBrush : Brush { public SolidColorBrush(Color c) { Color = c; } public Color Color { get; set; } }
    public static class Brushes { public static readonly Brush Transparent = new SolidColorBrush(new Color()), Black = Transparent, White = Transparent; }
}

namespace System.Windows.Threading
{
    public class DispatcherOperation { }
    public class Dispatcher { public DispatcherOperation InvokeAsync(Action a) { a(); return null; } }
    public class DispatcherTimer { public TimeSpan Interval { get; set; } public event EventHandler Tick; public void Start() { } public void Stop() { if (Tick != null) { } } }
}

namespace SharpDX
{
    public struct Vector2 { public float X, Y; public Vector2(float x, float y) { X = x; Y = y; } }
    public struct RectangleF { public RectangleF(float x, float y, float w, float h) { } }
    public struct Color4 { public Color4(float r, float g, float b, float a) { } }
    public class DisposeBase : IDisposable { public bool IsDisposed { get; set; } public void Dispose() { } }
}

namespace SharpDX.Direct2D1
{
    public enum AntialiasMode { PerPrimitive, Aliased }
    public enum DashStyle { Solid, Dash, Dot }
    public struct StrokeStyleProperties { public DashStyle DashStyle; }
    public class Factory { }
    public class Brush : SharpDX.DisposeBase { }
    public class SolidColorBrush : Brush { public SolidColorBrush(RenderTarget rt, SharpDX.Color4 c) { } }
    public class StrokeStyle : SharpDX.DisposeBase { public StrokeStyle(Factory f, StrokeStyleProperties p) { } }
    public class RenderTarget
    {
        public AntialiasMode AntialiasMode { get; set; }
        public void DrawLine(SharpDX.Vector2 a, SharpDX.Vector2 b, Brush br, float w) { }
        public void DrawLine(SharpDX.Vector2 a, SharpDX.Vector2 b, Brush br, float w, StrokeStyle s) { }
        public void FillRectangle(SharpDX.RectangleF r, Brush br) { }
        public void DrawRectangle(SharpDX.RectangleF r, Brush br, float w) { }
        public void DrawTextLayout(SharpDX.Vector2 o, SharpDX.DirectWrite.TextLayout l, Brush br) { }
    }
}

namespace SharpDX.DirectWrite
{
    public enum TextAlignment { Leading, Trailing, Center }
    public enum ParagraphAlignment { Near, Far, Center }
    public class Factory { }
    public class TextMetrics { public float Width, Height; }
    public class TextFormat : SharpDX.DisposeBase { public TextFormat(Factory f, string family, float size) { } public TextAlignment TextAlignment { get; set; } }
    public class TextLayout : SharpDX.DisposeBase { public TextLayout(Factory f, string text, TextFormat tf, float w, float h) { } public TextMetrics Metrics { get { return new TextMetrics(); } } public ParagraphAlignment ParagraphAlignment { get; set; } }
}

namespace NinjaTrader.Core
{
    public static class Globals
    {
        public static DateTime Now { get { return NowOverride ?? DateTime.Now; } }
        public static DateTime? NowOverride;
        public static string InstallDir { get { return ""; } }
        public static SharpDX.Direct2D1.Factory D2DFactory { get { return null; } }
        public static SharpDX.DirectWrite.Factory DirectWriteFactory { get { return null; } }
    }
}

namespace NinjaTrader.Cbi
{
    public class Instrument { public string FullName { get; set; } }
}

namespace NinjaTrader.Data
{
    public enum BarsPeriodType { Tick, Volume, Range, Second, Minute, Day, Week, Month, Year }
    public class BarsPeriod { public BarsPeriodType BarsPeriodType { get; set; } public int Value { get; set; } }

    // In-memory bars for the smoke test (tests/SmokeTest.cs fills them).
    public class Bars
    {
        public readonly List<DateTime> T = new List<DateTime>();
        public readonly List<double> O = new List<double>(), H = new List<double>(), L = new List<double>(), C = new List<double>();
        public int Visible = int.MaxValue;     // bars "loaded so far" while the smoke test replays
        public BarsPeriod Period;
        public int Count { get { return Math.Min(T.Count, Visible); } }
        public BarsPeriod BarsPeriod { get { return Period; } }
        void Check(int i) { if (i < 0 || i >= Count) throw new ArgumentOutOfRangeException("i", i + " of " + Count); }
        public DateTime GetTime(int i) { Check(i); return T[i]; }
        public double GetOpen(int i) { Check(i); return O[i]; }
        public double GetHigh(int i) { Check(i); return H[i]; }
        public double GetLow(int i) { Check(i); return L[i]; }
        public double GetClose(int i) { Check(i); return C[i]; }
    }

    // CME-style sessions for the smoke test: 18:00 to 17:00 ET, Sunday through Friday, trading
    // day = the session's end date.
    public class SessionIterator
    {
        public SessionIterator(Bars b) { }
        public DateTime ActualSessionBegin { get; private set; }
        public DateTime ActualSessionEnd { get; private set; }
        public DateTime ActualTradingDayExchange { get; private set; }
        public bool GetNextSession(DateTime t, bool includesEndTimeStamp)
        {
            DateTime day = t.Date;
            for (int i = -1; i < 10; i++)
            {
                DateTime td = day.AddDays(i);
                if (td.DayOfWeek == DayOfWeek.Saturday || td.DayOfWeek == DayOfWeek.Sunday) continue;
                DateTime begin = td.AddDays(-1).AddHours(18), end = td.AddHours(17);
                bool inside = includesEndTimeStamp ? (t > begin && t <= end) : (t >= begin && t < end);
                if (inside || begin > t)
                {
                    ActualSessionBegin = begin; ActualSessionEnd = end; ActualTradingDayExchange = td;
                    return true;
                }
            }
            return false;
        }
    }
}

namespace NinjaTrader.Gui
{
    public static class Serialize
    {
        public static string BrushToString(System.Windows.Media.Brush b) { return ""; }
        public static System.Windows.Media.Brush StringToBrush(string s) { return null; }
    }
}

namespace NinjaTrader.Gui.Chart
{
    public enum ScaleJustification { Left, Right, Overlay }
    public class ChartBars { public int Count { get; set; } }
    public class ChartControlProperties { public float BarDistance { get; set; } }
    public class ChartControl
    {
        readonly System.Windows.Threading.Dispatcher d = new System.Windows.Threading.Dispatcher();
        readonly ChartControlProperties p = new ChartControlProperties { BarDistance = 8 };
        public System.Windows.Threading.Dispatcher Dispatcher { get { return d; } }
        public ChartControlProperties Properties { get { return p; } }
        public int GetXByBarIndex(ChartBars bars, int index) { if (index < 0 || index >= bars.Count) throw new ArgumentOutOfRangeException("index"); return index * 8; }
    }
    public class ChartPanel { public int X, Y, W = 1600, H = 900; }
    public class ChartScale { public int GetYByValue(double v) { if (double.IsNaN(v)) throw new ArgumentException("NaN price"); return (int)(1000 - v); } }
}

namespace NinjaTrader.Gui.NinjaScript
{
    public class IndicatorRenderBase : NinjaTrader.NinjaScript.NinjaScriptBase { }
    public class StrategyRenderBase : NinjaTrader.NinjaScript.NinjaScriptBase { }
}

namespace NinjaTrader.NinjaScript
{
    public enum State { SetDefaults, Configure, Active, DataLoaded, Historical, Transition, Realtime, Terminated, Finalized }
    public enum Calculate { OnBarClose, OnEachTick, OnPriceChange }
    public enum Priority { High, Medium, Low }
    public interface ISeries<T> { T this[int barsAgo] { get; } }
    public class Series<T> : ISeries<T>
    {
        public readonly Dictionary<int, T> Data = new Dictionary<int, T>();
        public Func<int> Current = () => 0;
        public T this[int barsAgo] { get { T v; return Data.TryGetValue(Current() - barsAgo, out v) ? v : default(T); } set { Data[Current() - barsAgo] = value; } }
    }
    public class BrushSeries
    {
        public readonly Dictionary<int, System.Windows.Media.Brush> Data = new Dictionary<int, System.Windows.Media.Brush>();
        public Func<int> Current = () => 0;
        public System.Windows.Media.Brush this[int barsAgo] { get { return null; } set { Data[Current() - barsAgo] = value; } }
    }

    public class SeriesRequest { public string Instrument; public NinjaTrader.Data.BarsPeriod Period; public int BarsToLoad; public string TradingHours; }

    public class NinjaScriptBase
    {
        public State State { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Calculate Calculate { get; set; }
        public int BarsInProgress { get; set; }
        public int[] CurrentBars { get; set; }
        public NinjaTrader.Data.Bars[] BarsArray { get; set; }
        public NinjaTrader.Cbi.Instrument Instrument { get; set; }
        public double TickSize { get; set; }
        public ISeries<double> Input { get; set; }
        public readonly List<SeriesRequest> Requests = new List<SeriesRequest>();
        public readonly List<string> Alerts = new List<string>();
        protected virtual void OnStateChange() { }
        protected virtual void OnBarUpdate() { }
        public void AddDataSeries(string instrumentName, NinjaTrader.Data.BarsPeriod barsPeriod, int barsToLoad, string tradingHoursName, bool? isResetOnNewTradingDay)
        {
            Requests.Add(new SeriesRequest { Instrument = instrumentName, Period = barsPeriod, BarsToLoad = barsToLoad, TradingHours = tradingHoursName });
        }
        public void Alert(string id, Priority priority, string message, string soundLocation, int rearmSeconds, System.Windows.Media.Brush backBrush, System.Windows.Media.Brush foreBrush) { Alerts.Add(id + ": " + message); }
        public void TriggerCustomEvent(Action<object> customEvent, int barsSeriesIndex, object state) { customEvent(state); }
        public bool EqualsInput(object input) { return false; }
        protected T CacheIndicator<T>(T indicator, ISeries<double> input, ref T[] cache) where T : NinjaScriptBase { return indicator; }
    }

    public class MarketAnalyzerColumnBase { protected NinjaTrader.NinjaScript.Indicators.Indicator indicator; public ISeries<double> Input { get; set; } }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        public bool IsOverlay { get; set; }
        public bool DisplayInDataBox { get; set; }
        public bool DrawOnPricePanel { get; set; }
        public bool PaintPriceMarkers { get; set; }
        public bool IsAutoScale { get; set; }
        public bool IsSuspendedWhileInactive { get; set; }
        public NinjaTrader.Gui.Chart.ScaleJustification ScaleJustification { get; set; }
        public int BarsRequiredToPlot { get; set; }
        public Series<double>[] Values = new Series<double>[0];
        public BrushSeries BarBrushes = new BrushSeries();
        public BrushSeries CandleOutlineBrushes = new BrushSeries();
        public NinjaTrader.Gui.Chart.ChartControl ChartControl { get; set; }
        public NinjaTrader.Gui.Chart.ChartBars ChartBars { get; set; }
        public NinjaTrader.Gui.Chart.ChartPanel ChartPanel { get; set; }
        public SharpDX.Direct2D1.RenderTarget RenderTarget { get; set; }
        public void AddPlot(System.Windows.Media.Brush brush, string name)
        {
            Series<double>[] v = new Series<double>[Values.Length + 1];
            Array.Copy(Values, v, Values.Length);
            v[Values.Length] = new Series<double> { Current = () => CurrentBars == null ? 0 : CurrentBars[0] };
            Values = v;
        }
        public void ForceRefresh() { }
        protected virtual void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale) { }
        public virtual void OnRenderTargetChanged() { }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : NinjaTrader.NinjaScript.MarketAnalyzerColumnBase { }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        protected NinjaTrader.NinjaScript.Indicators.Indicator indicator;
    }
}

namespace System.ComponentModel.DataAnnotations
{
    public class DisplayAttribute : Attribute { public string Name { get; set; } public int Order { get; set; } public string GroupName { get; set; } public string Description { get; set; } }
    public class RangeAttribute : Attribute { public RangeAttribute(int min, int max) { } public RangeAttribute(double min, double max) { } }
}
