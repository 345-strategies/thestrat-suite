// Drives the NinjaTrader engine (TheStratSuiteEngine.cs) from grammar/tests/test_nt_parity.py.
// Reads one case per line on stdin and writes one answer per line:
//   bar  ph pl o h l c method enableF2   ->  the Suite's bar type (1u 1d 2u 2d F2u F2d 3u 3d, or "-" for no data)
//   pat  o h l c hammerMethod color      ->  "hammer shooter" as 0/1
//   key  tf yyyy-mm-dd                   ->  StratTimeframes.PeriodKey
using System;
using System.Globalization;
using NinjaTrader.NinjaScript.Indicators.StratSuiteCore;

public static class ParityHarness
{
    static double D(string s) { return s == "nan" ? double.NaN : double.Parse(s, CultureInfo.InvariantCulture); }

    public static void Main()
    {
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            string[] a = line.Split(' ');
            if (a[0] == "bar")
            {
                bool failed;
                string t = StratGrammar.DetectBarTypeAndFailed(D(a[1]), D(a[2]), D(a[4]), D(a[5]), D(a[3]), D(a[6]),
                    a[8] == "1", (F2Method)int.Parse(a[7]), out failed);
                Console.WriteLine(t == "" ? "-" : t);
            }
            else if (a[0] == "pat")
            {
                double o = D(a[1]), h = D(a[2]), l = D(a[3]), c = D(a[4]);
                HammerDefinition m = (HammerDefinition)int.Parse(a[5]);
                bool color = a[6] == "1";
                Console.WriteLine((StratGrammar.IsHammer(o, h, l, c, m, color) ? "1" : "0") + " " + (StratGrammar.IsShooter(o, h, l, c, m, color) ? "1" : "0"));
            }
            else if (a[0] == "bn")
            {
                // bn barType notation emphasis position colorMode forming  ("-" = no type)
                StratSettings st = new StratSettings();
                st.ShowBarNumbers = true;
                st.BarNumNotation = (BarNumNotationOption)int.Parse(a[2]);
                st.BarNumEmphasis = (BarNumEmphasisOption)int.Parse(a[3]);
                st.BarNumPosition = (BarNumPositionOption)int.Parse(a[4]);
                st.BarNumColorMode = (BarNumColorModeOption)int.Parse(a[5]);
                BarNumberToken t = StratBarNumbers.Token(st, a[1] == "-" ? "" : a[1], a[6] == "1");
                Console.WriteLine(t.Visible ? t.Text + " " + (t.Above ? "above" : "below") + " " + t.Color.R + "," + t.Color.G + "," + t.Color.B + "," + t.Color.A : "none");
            }
            else if (a[0] == "key")
            {
                StratTimeframe tf = (StratTimeframe)Enum.Parse(typeof(StratTimeframe), a[1]);
                DateTime d = DateTime.ParseExact(a[2], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                Console.WriteLine(StratTimeframes.PeriodKey(tf, d));
            }
        }
    }
}
