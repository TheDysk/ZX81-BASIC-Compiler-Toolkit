// Test helper for run_tests.sh: tape export and listing round trip for each .p given.
using System; using System.IO; using System.Linq; using System.Collections.Generic; using ZX81Compiler;
public static class ToolTest {
  public static int Main(string[] args) {
    int bad = 0;
    // "--tape file.p" makes /tmp/zx81test.wav and .tzx from that file instead (checked by tapecheck.py)
    if (args.Length == 2 && args[0] == "--tape") {
      var p0 = File.ReadAllBytes(args[1]);
      File.WriteAllBytes("/tmp/zx81test.wav", Tape.MakeWav(p0, "TEST"));
      File.WriteAllBytes("/tmp/zx81test.tzx", Tape.MakeTzx(p0, "TEST"));
      return 0;
    }
    foreach (var f in args) {
      var p = PFile.Load(f);
      var r = Decompiler.Run(p, Path.GetFileName(f));
      var d = new List<Diagnostic>();
      byte[] back = r.MakeP(r.Listing, d, true, true);
      bool same = back != null && back.SequenceEqual(p.Data);
      if (!same) bad++;
      string rec = r.Recovered.IsOurs ? (r.Recovered.Verified ? "BASIC rebuilt+verified" : "BASIC NOT VERIFIED") : "not compiled by toolset";
      if (r.Recovered.IsOurs && !r.Recovered.Verified) bad++;
      Console.WriteLine("{0,-34} listing->.p {1}; {2}", Path.GetFileName(f), same ? "identical" : "DIFFERENT", rec);
    }
    Console.WriteLine(bad == 0 ? "all OK" : bad + " problem(s)");
    return bad == 0 ? 0 : 1;
  }
}
