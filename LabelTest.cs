// Tests for line-numberless BASIC with @labels and for renumbering (Labels.cs).
// Run by tests/run_tests.sh: LabelTest.exe
// Prints one line per check and "all OK" (exit 0) or the number of failures (exit 1).
using System;
using System.Collections.Generic;
using System.Linq;
using ZX81Compiler;

public static class LabelTest
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what + (!ok && detail.Length > 0 ? "  (" + detail + ")" : ""));
        if (!ok) fails++;
    }

    static CompileResult Compile(string text, bool labels = false)
    {
        return Compiler.Compile(text, new Options { Numbers = NumberMode.FloatIslands, MakeListing = false, Labels = labels });
    }

    static bool SameCode(CompileResult a, CompileResult b) { return a.Success && b.Success && a.PFile.SequenceEqual(b.PFile); }

    static string Errors(CompileResult r) { return string.Join(" / ", r.Diagnostics.Where(d => d.IsError).Select(d => d.ToString())); }

    public static int Main()
    {
        // 1. a labelled program compiles exactly like the same program with numbers
        string labelled =
            "# a game loop\n" +
            "@start:\n" +
            "CLS\n" +
            "LET N=0\n" +
            "@loop: LET N=N+1\n" +
            "PRINT N;\" @LOOP\";\n" +          // @ inside a string is left alone
            "IF N<5 THEN GOTO @loop\n" +
            "IF N=5 THEN @done\n" +
            "GOSUB @sub\n" +
            "@done:\n" +
            "GO SUB @Sub\n" +                   // names are not case sensitive
            "STOP\n" +
            "9000 @sub: PRINT \"SUB\"\n" +
            "RETURN\n";
        string numbered =
            "10 CLS\n20 LET N=0\n30 LET N=N+1\n40 PRINT N;\" @LOOP\";\n50 IF N<5 THEN GOTO 30\n60 IF N=5 THEN 80\n" +
            "70 GOSUB 9000\n80 GO SUB 9000\n90 STOP\n9000 PRINT \"SUB\"\n9010 RETURN\n";
        var a = Compile(labelled); var b = Compile(numbered);
        Check("labels: compiles to the same machine code as the numbered listing", SameCode(a, b), Errors(a) + Errors(b));
        var conv = new List<Diagnostic>();
        string asNumbers = Labels.Number(labelled, false, conv);
        Check("labels: converted listing", asNumbers != null && asNumbers.Replace("\r", "").Contains("50 IF N<5 THEN GOTO 30\n60 IF N=5 THEN 80\n") && asNumbers.Contains("9000 PRINT \"SUB\""), asNumbers);
        Check("labels: the #comment is kept, label-only lines go", asNumbers != null && asNumbers.StartsWith("# a game loop") && !asNumbers.Contains("@start"));

        // 2. keepLayout: the same number of text lines, so messages point at the right line
        var keep = Labels.Number(labelled, true, new List<Diagnostic>());
        Check("labels: compiler's numbering keeps every text line in place", keep != null && keep.Split('\n').Length == labelled.Split('\n').Length);

        // 3. mixed numbering: explicit numbers are kept; lines in between are spaced to fit
        var mixed = Labels.Number("PRINT 1\nPRINT 2\n25 PRINT 3\nPRINT 4\nPRINT 5\n30 PRINT 6\nPRINT 7\n", false, new List<Diagnostic>());
        Check("mixed numbering: 10 20 25, then 26 27 between 25 and 30, then 40",
            mixed != null && mixed.Replace("\r", "") == "10 PRINT 1\n20 PRINT 2\n25 PRINT 3\n26 PRINT 4\n27 PRINT 5\n30 PRINT 6\n40 PRINT 7\n", mixed);
        // --labels / #LABELS: unnumbered lines without any label
        var plain = Compile("CLS\nPRINT \"HI\"\n", true);
        var direct = Compile("#LABELS\nCLS\nPRINT \"HI\"\n");
        var plainN = Compile("10 CLS\n20 PRINT \"HI\"\n");
        Check("--labels numbers lines that have no labels", SameCode(plain, plainN), Errors(plain));
        Check("#LABELS does the same", SameCode(direct, plainN), Errors(direct));
        var off = Compile("CLS\nPRINT \"HI\"\n");
        Check("without labels, an unnumbered line is still an error", !off.Success && Errors(off).Contains("does not start with a line number"));

        // 4. errors, each with its text line
        var e1 = Compile("@a:\nPRINT 1\nGOTO @nowhere\n@a: PRINT 2\nGOSUB @a\n");
        var errs = e1.Diagnostics.Where(d => d.IsError).ToList();
        Check("undefined label: error on text line 3", errs.Any(d => d.SourceLine == 3 && d.Message.Contains("@nowhere is not defined")), Errors(e1));
        Check("duplicate label: error on text line 4, naming line 1", errs.Any(d => d.SourceLine == 4 && d.Message.Contains("defined twice") && d.Message.Contains("text line 1")), Errors(e1));
        var e2 = Compile("10 PRINT 1\n@x: PRINT 2\nPRINT 3\n11 PRINT 4\nGOTO @x\n");
        Check("no room between 10 and 11: error on text line 2", e2.Diagnostics.Any(d => d.IsError && d.SourceLine == 2 && d.Message.Contains("no room")), Errors(e2));
        var e3 = Compile("9990 @x: PRINT 1\nPRINT 2\nGOTO @x\n");
        Check("numbers past 9999: error on text line 2 (it would be 10000)", e3.Diagnostics.Any(d => d.IsError && d.SourceLine == 2 && d.Message.Contains("past 9999")), Errors(e3));
        var e4 = Compile("@end:\n");
        Check("a label with no line after it is an error", e4.Diagnostics.Any(d => d.IsError && d.Message.Contains("no BASIC line after it")), Errors(e4));

        // 5. renumbering
        string prog = "5 REM START\n7 LET A=1\n12 IF A=1 THEN 30\n13 GOTO 7\n30 GOSUB 100\n31 RUN 5\n100 PRINT \"X\"\n101 RETURN\n";
        var rd = new List<Diagnostic>();
        string ren = Labels.Renumber(prog, 100, 5, rd);
        Check("renumber: lines and targets", ren != null && ren.Replace("\r", "") == "100 REM START\n105 LET A=1\n110 IF A=1 THEN 120\n115 GOTO 105\n120 GOSUB 130\n125 RUN 100\n130 PRINT \"X\"\n135 RETURN\n", ren);
        Check("renumber: the program compiles to the same machine code", SameCode(Compile(prog), Compile(ren)));
        var rd2 = new List<Diagnostic>();
        string ren2 = Labels.Renumber("10 GOTO 15\n20 GOTO 10+N\n30 PRINT \"GOTO 10\"\n", 1, 1, rd2);
        Check("renumber: a missing target goes to the next line; strings untouched", ren2 != null && ren2.Replace("\r", "") == "1 GOTO 2\n2 GOTO 10+N\n3 PRINT \"GOTO 10\"\n", ren2);
        Check("renumber: a calculated GOTO is warned about", rd2.Any(d => !d.IsError && d.Message.Contains("worked out")));
        var rd3 = new List<Diagnostic>();
        Check("renumber: refuses to go past 9999", Labels.Renumber(prog, 9990, 10, rd3) == null && rd3.Any(d => d.IsError));
        var rd4 = new List<Diagnostic>();
        string ren4 = Labels.Renumber("@a: PRINT 1\nGOTO @a\n", 1000, 1, rd4);
        Check("renumber: a labelled listing is numbered first", ren4 != null && ren4.Replace("\r", "") == "1000 PRINT 1\n1001 GOTO 1000\n", ren4);

        Console.WriteLine(fails == 0 ? "all OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
