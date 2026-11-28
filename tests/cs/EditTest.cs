// Tests for the .P Toolkit's screen and tile editors (PEdit.cs): the bytes go where
// they should and the system variables (D_FILE, DF_CC, VARS, E_LINE, NXTLIN, STKBOT,
// STKEND) stay right. Run by tests/run_tests.sh: EditTest.exe
// Prints one line per check and "all OK" (exit 0) or the number of failures (exit 1).
using System;
using System.Collections.Generic;
using System.Linq;
using ZX81Compiler;

public static class EditTest
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what + (!ok && detail.Length > 0 ? "  (" + detail + ")" : ""));
        if (!ok) fails++;
    }

    static int W(byte[] d, int sysvar) { return d[sysvar - PFile.Base] | d[sysvar - PFile.Base + 1] << 8; }
    static void SetW(byte[] d, int sysvar, int v) { d[sysvar - PFile.Base] = (byte)v; d[sysvar - PFile.Base + 1] = (byte)(v >> 8); }

    static readonly int[] Pointers = { 0x400C, 0x400E, 0x4010, 0x4014, 0x4029, 0x401A, 0x401C };   // D_FILE DF_CC VARS E_LINE NXTLIN STKBOT STKEND
    static readonly string[] Names = { "D_FILE", "DF_CC", "VARS", "E_LINE", "NXTLIN", "STKBOT", "STKEND" };

    static string Moved(byte[] a, byte[] b)
    {
        var m = new List<string>();
        for (int i = 0; i < Pointers.Length; i++) if (W(a, Pointers[i]) != W(b, Pointers[i])) m.Add(Names[i] + " " + W(a, Pointers[i]).ToString("X4") + "->" + W(b, Pointers[i]).ToString("X4"));
        return string.Join(", ", m);
    }

    /// <summary>A small program (auto-running from line 10) with two saved variables, A=9 and B$="HI".</summary>
    static PFile Program()
    {
        var d = new List<Diagnostic>();
        var lines = Basic81.TokenizeProgram("1 REM \\00\\18\\24\\42\\7e\\42\\42\\00\\ff\\81\\81\\81\\81\\81\\81\\ff\n10 PRINT \"HELLO\"\n20 GOTO 10\n", d);
        byte[] data = Basic81.BuildP(lines, null, false, false, 10);
        var vb = new List<byte> { 0x66 };                       // A (number)
        vb.AddRange(ZxFloat.Encode(9));
        vb.AddRange(new byte[] { 0x47, 2, 0, 0x2D, 0x2E });      // B$ = "HI"
        vb.Add(0x80);
        return PFile.Load(Zx80.SetVariables(data, vb.ToArray()));
    }

    /// <summary>The same file with a collapsed (1K style) display file: 25 NEWLINEs.</summary>
    static PFile Collapsed(PFile p)
    {
        int df = p.DFile, vars = p.VarsAddr;
        var o = new List<byte>(p.Data.Take(df - PFile.Base));
        o.AddRange(Enumerable.Repeat((byte)0x76, 25));
        o.AddRange(p.Data.Skip(vars - PFile.Base));
        var r = o.ToArray();
        int delta = 25 - (vars - df);
        foreach (int sv in new[] { 0x4010, 0x4014, 0x4016, 0x4018, 0x401A, 0x401C }) SetW(r, sv, W(r, sv) + delta);
        SetW(r, 0x400E, df + 1);
        return PFile.Load(r);
    }

    static bool Throws(Action a, string contains)
    {
        try { a(); return false; }
        catch (PEditException e) { return e.Message.Contains(contains); }
    }

    public static int Main()
    {
        var p = Program();
        Check("test program: expanded display, 3 lines, 2 variables, auto-runs at 10, no problems",
            !p.Display.Collapsed && p.Lines.Count == 3 && p.ReadVariables().Count == 2 && p.AutoRunLine != null && p.AutoRunLine.Number == 10 && p.Problems.Count == 0,
            string.Join("; ", p.Problems));

        // ---- screen ----
        var s1 = PEdit.SetScreenChars(p, new[] { new[] { 0, 0, 0x80 }, new[] { 23, 31, 0x26 }, new[] { 5, 7, 0x08 } });
        var q = PFile.Load(s1);
        Check("screen: the characters are where they were drawn", q.Display.Chars[0, 0] == 0x80 && q.Display.Chars[23, 31] == 0x26 && q.Display.Chars[5, 7] == 0x08);
        Check("screen: D_FILE+1+row*33+col", s1[p.DFile + 1 + 23 * 33 + 31 - PFile.Base] == 0x26 && s1[p.DFile + 1 + 5 * 33 + 7 - PFile.Base] == 0x08);
        Check("screen: no system variable moves", Moved(p.Data, s1) == "", Moved(p.Data, s1));
        Check("screen: only those 3 bytes change, and the file is the same size", s1.Length == p.Data.Length && Enumerable.Range(0, s1.Length).Count(i => s1[i] != p.Data[i]) == 3);
        Check("screen: every line end (76h) is still there", Enumerable.Range(0, 25).All(r => s1[p.DFile + r * 33 - PFile.Base] == 0x76));
        Check("screen: program, variables and auto-run unchanged", q.Lines.Select(l => l.Number).SequenceEqual(new[] { 1, 10, 20 }) && q.ReadVariables().Select(v => v.Name + "=" + v.Value).SequenceEqual(p.ReadVariables().Select(v => v.Name + "=" + v.Value)) && q.AutoRunLine.Number == 10 && q.Problems.Count == 0);
        Check("screen: NEWLINE and keyword codes are refused", Throws(() => PEdit.SetScreenChar(p, 0, 0, 0x76), "can be shown") && Throws(() => PEdit.SetScreenChar(p, 0, 0, 0xF5), "can be shown"));
        Check("screen: positions off the screen are refused", Throws(() => PEdit.SetScreenChar(p, 24, 0, 0x26), "24 rows") && Throws(() => PEdit.SetScreenChar(p, 0, 32, 0x26), "32 columns"));
        var c = Collapsed(p);
        Check("screen: a collapsed display file is refused", c.Display.Collapsed && c.Problems.Count == 0 && Throws(() => PEdit.SetScreenChar(c, 0, 0, 0x26), "collapsed"), string.Join("; ", c.Problems));

        // ---- tiles: bytes written back in place ----
        var rem = p.Lines.First(l => l.Number == 1);
        int tile = rem.BodyAddr + 1;                           // the first 8 bytes after REM
        var t = new byte[] { 0x3C, 0x42, 0x81, 0x81, 0x81, 0x81, 0x42, 0x3C };
        var w = PEdit.WriteBytes(p, tile, t);
        var wq = PFile.Load(w);
        Check("tile: the 8 bytes are written back where the tile was", Enumerable.Range(0, 8).All(i => wq.Mem[tile + i] == t[i]));
        Check("tile: no system variable moves, the file is the same size", Moved(p.Data, w) == "" && w.Length == p.Data.Length, Moved(p.Data, w));
        Check("tile: the next tile and the rest of the program are untouched", Enumerable.Range(tile + 8, p.FileEnd - tile - 8).All(a => wq.Mem[a] == p.Mem[a]) && wq.Problems.Count == 0);
        Check("tile: writing over the system variables or past the end is refused", Throws(() => PEdit.WriteBytes(p, 0x4070, t), "aren't all inside") && Throws(() => PEdit.WriteBytes(p, p.FileEnd - 4, t), "aren't all inside"));

        // ---- tiles: a new REM line ----
        var payload = Enumerable.Range(0, 24).Select(i => (byte)(i * 11)).ToArray();     // 3 tiles
        foreach (int num in new[] { 5, 30 })                  // between lines, and after the last
        {
            int at;
            var a = PEdit.AddRemLine(p, num, payload, out at);
            var aq = PFile.Load(a);
            var nl = aq.Lines.FirstOrDefault(l => l.Number == num);
            int grow = payload.Length + 2 + 4;                // REM + bytes + 76h, and the 4-byte line header
            bool ok = nl != null && nl.Body[0] == 0xEA && nl.Body.Skip(1).Take(24).SequenceEqual(payload) && at == nl.BodyAddr + 1 &&
                      Enumerable.Range(0, 24).All(i => aq.Mem[at + i] == payload[i]);
            Check("new REM line " + num + ": holds the bytes, at the address reported", ok);
            Check("new REM line " + num + ": lines in order, auto-run still line 10", aq.Lines.Select(l => l.Number).SequenceEqual(num == 5 ? new[] { 1, 5, 10, 20 } : new[] { 1, 10, 20, 30 }) && aq.AutoRunLine != null && aq.AutoRunLine.Number == 10);
            Check("new REM line " + num + ": D_FILE, VARS, E_LINE, STKBOT and STKEND move by exactly the line's size (" + grow + ")",
                aq.DFile == p.DFile + grow && aq.VarsAddr == p.VarsAddr + grow && aq.ELine == p.ELine + grow && W(a, 0x401A) == W(p.Data, 0x401A) + grow && W(a, 0x401C) == W(p.Data, 0x401C) + grow, Moved(p.Data, a));
            Check("new REM line " + num + ": D_FILE is the end of the program; DF_CC keeps its place in the display",
                aq.DFile == aq.Lines.Last().End && aq.DfCc - aq.DFile == p.DfCc - p.DFile);
            Check("new REM line " + num + ": display file and variables are the same bytes", aq.DisplayBytes().SequenceEqual(p.DisplayBytes()) && aq.VariablesBytes().SequenceEqual(p.VariablesBytes()) && aq.Problems.Count == 0,
                string.Join("; ", aq.Problems));
        }
        int dummy;
        Check("new REM line: an existing line number is refused", Throws(() => PEdit.AddRemLine(p, 10, payload, out dummy), "already a line"));
        Check("next free line number after line 20 is 30", PEdit.NextLineNumber(p) == 30);

        Console.WriteLine(fails == 0 ? "all OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
