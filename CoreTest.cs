// Test driver: round-trip every .p file through text and back.
using System; using System.IO; using System.Linq; using System.Collections.Generic; using ZX81Compiler;
public static class CoreTest {
  public static int Main(string[] args) {
    int ok = 0, bad = 0, esc = 0, lines = 0;
    foreach (var f in args) {
      var p = PFile.Load(f);
      int e; string txt = Basic81.ProgramText(p, new DetokOptions(), out e);
      esc += e; lines += p.Lines.Count;
      var d = new List<Diagnostic>();
      var toks = Basic81.TokenizeProgram(txt, d);
      var auto = p.AutoRunLine;
      byte[] nb = Basic81.BuildP(toks, p, true, true, auto == null ? -1 : auto.Number);
      int progLen = p.ProgEnd - PFile.ProgStart;
      bool progSame = nb.Length >= p.Data.Length && Enumerable.Range(PFile.ProgStart - PFile.Base, p.Data.Length - (PFile.ProgStart - PFile.Base)).All(i => nb[i] == p.Data[i]);
      var svDiff = Enumerable.Range(0, PFile.ProgStart - PFile.Base).Where(i => i < nb.Length && nb[i] != p.Data[i]).Select(i => (i + PFile.Base).ToString("X4")).ToList();
      if (progSame && nb.Length == p.Data.Length) ok++; else bad++;
      Console.WriteLine("{0,-40} lines {1,3} escaped {2,2} {3} sysvar diffs: {4} {5}", Path.GetFileName(f), p.Lines.Count, e,
        progSame && nb.Length == p.Data.Length ? "SAME" : "DIFF(len " + nb.Length + " vs " + p.Data.Length + ")", string.Join(",", svDiff), string.Join("; ", p.Problems));
      if (d.Count > 0) Console.WriteLine("   diags: " + string.Join(" | ", d.Select(x => x.ToString()).Take(3)));
    }
    Console.WriteLine("files identical: {0}, different: {1}; fully-escaped lines {2} of {3}", ok, bad, esc, lines);
    return 0;
  }
}
