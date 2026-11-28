// Tests for decompiler control files (ControlFile.cs, CodeFinder with a .ctl).
// Run by tests/run_tests.sh: CtlTest.exe
// Prints one line per check and "all OK" (exit 0) or the number of failures (exit 1).
using System;
using System.Collections.Generic;
using System.Linq;
using ZX81Compiler;

public static class CtlTest
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what + (!ok && detail.Length > 0 ? "  (" + detail + ")" : ""));
        if (!ok) fails++;
    }

    // 4082 LD A,5 / 4084 JP 408A / 4087 00 00 00 / 408A RET / 408B 01 02 03 04 (data)
    // 408F CALL 4093 / 4092 RET / 4093 LD HL,0 / 4096 RET   (408F.. is reached by no USR)
    const string Listing = "1 REM \\3e\\05\\c3\\8a\\40\\00\\00\\00\\c9\\01\\02\\03\\04\\cd\\93\\40\\c9\\21\\00\\00\\c9\n10 RAND USR 16514\n";

    static PFile Program()
    {
        var d = new List<Diagnostic>();
        return PFile.Load(Basic81.BuildP(Basic81.TokenizeProgram(Listing, d), null, false, false, -1));
    }

    /// <summary>Assemble an .asm made by ToAsm (one block) and compare it with memory.</summary>
    static bool Reassembles(string asmText, PFile p, int start, int end, out string why)
    {
        var a = new Z80Asm();
        foreach (var l in asmText.Replace("\r", "").Split('\n'))
        {
            string t = l.Trim();
            if (t.StartsWith("ORG")) continue;
            a.Line(l);
        }
        try
        {
            byte[] b = a.Assemble(start, false);
            for (int i = 0; i < end - start; i++)
                if (i >= b.Length || b[i] != p.Mem[start + i]) { why = "differs at " + (start + i).ToString("X4"); return false; }
            why = ""; return true;
        }
        catch (Exception e) { why = e.Message; return false; }
    }

    public static int Main()
    {
        var p = Program();
        var plain = CodeFinder.Analyze(p);
        Check("without a control file, 408Fh isn't reached", !plain.InstrStarts.Contains(0x408F) && plain.InstrStarts.Contains(0x408A));

        // parsing: every address form, comments, bad lines
        var ctl = ControlFile.Parse(
            "; a comment\n# another\n" +
            "LABEL 4082h start\nLABEL 16522 done\nLABEL 0x4093 helper\nLABEL $408F caller\n" +
            "COMMENT 4084h jump over the gap\nCOMMENT #408F the code no USR reaches\n" +
            "CODE 408Fh\nDATA 408Bh 4\n" +
            "LABEL 4000h 9bad\nDATA 4000h\nCODE 4090h 4080h\nFROB 1\nLABEL nothex x\n");
        Check("parse: 4 labels (hex h, decimal, 0x, $)", ctl.Labels.Count == 4 && ctl.Labels[0x408A] == "done" && ctl.Labels[0x4093] == "helper" && ctl.Labels[0x408F] == "caller");
        Check("parse: comments, CODE and DATA", ctl.Comments.Count == 2 && ctl.Code.Count == 1 && ctl.Data.Count == 1 && ctl.Data[0][1] == 0x408F);
        Check("parse: 5 bad lines reported with their line numbers", ctl.Problems.Count == 5 && ctl.Problems[0].StartsWith("line 11:"), string.Join(" | ", ctl.Problems));
        var again = ControlFile.Parse(ctl.Save("test.p"));
        Check("save and load again gives the same", again.Save("test.p") == ctl.Save("test.p") && again.Problems.Count == 0);

        // CODE / DATA / LABEL / COMMENT in the analysis and the .asm
        var ca = CodeFinder.Analyze(p, ctl);
        Check("CODE: 408Fh is traced, and the CALL is followed to 4093h", ca.InstrStarts.Contains(0x408F) && ca.InstrStarts.Contains(0x4093) && ca.InstrStarts.Contains(0x4096));
        Check("DATA: 408Bh-408Eh are not code", Enumerable.Range(0x408B, 4).All(a => !ca.IsCode[a]));
        string asm = CodeFinder.ToAsm(p, ca, "test.p", ctl);
        Check("LABEL: names in label lines and operands", asm.Contains("\nstart:") && asm.Contains("JP done") && asm.Contains("CALL helper") && asm.Contains("\nhelper:") && asm.Contains("\ncaller:") && !asm.Contains("L4082"), asm);
        Check("COMMENT: comment lines above their addresses", asm.Contains("; jump over the gap\r\n        JP done") || asm.Contains("; jump over the gap\n        JP done"), asm);
        Check("DATA: written as DB", asm.Contains("DB 01h,02h,03h,04h"), asm);
        string why;
        Check("the .asm with the control file's names assembles back to the same bytes", Reassembles(asm, p, 0x4082, 0x4097, out why), why);

        // CODE with an end address decodes the whole range; DATA inside it still wins
        var lin = ControlFile.Parse("CODE 408Bh 4097h\nDATA 408Bh 4\n");
        var cl = CodeFinder.Analyze(p, lin);
        Check("CODE start end: the range is decoded (408F, 4092, 4093)", cl.InstrStarts.Contains(0x408F) && cl.InstrStarts.Contains(0x4092) && cl.InstrStarts.Contains(0x4093));
        Check("CODE start end: DATA inside it stays data", !cl.InstrStarts.Contains(0x408B) && !cl.IsCode[0x408B]);
        // DATA stops tracing
        var stop = ControlFile.Parse("CODE 408Fh\nDATA 4093h 4\n");
        var cs = CodeFinder.Analyze(p, stop);
        Check("DATA: tracing doesn't go into it (the CALL target stays data)", cs.InstrStarts.Contains(0x408F) && !cs.IsCode[0x4093] && !cs.IsCode[0x4096]);
        string asm2 = CodeFinder.ToAsm(p, cs, "test.p", stop);
        Check("...and that .asm still assembles back to the same bytes", Reassembles(asm2, p, 0x4082, 0x4097, out why), why);

        // the decompiler passes it on, and reports it
        var r = Decompiler.Run(p, "test.p", ctl);
        Check("Decompiler.Run uses the control file and says so", r.Asm.Contains("CALL helper") && r.Report.Contains("Control file: 4 label(s)"));
        // a CODE outside the file is reported, not followed
        var outside = CodeFinder.Analyze(p, ControlFile.Parse("CODE 9000h\n"));
        Check("CODE outside the file: a note, nothing traced", outside.Notes.Any(n => n.Contains("outside the file")));

        Console.WriteLine(fails == 0 ? "all OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
