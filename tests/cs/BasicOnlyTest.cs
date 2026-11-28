// Tests for BASIC only output (BasicOnly.cs): a listing -> a .p file of ordinary BASIC.
// Run by tests/run_tests.sh: BasicOnlyTest.exe tests/t1.bas
// Prints one line per check and "all OK" (exit 0) or the number of failures (exit 1).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZX81Compiler;

public static class BasicOnlyTest
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what + (!ok && detail.Length > 0 ? "  (" + detail + ")" : ""));
        if (!ok) fails++;
    }

    static CompileResult Build(string text, int ram = 16, bool autorun = true)
    {
        return Compiler.Compile(text, new Options { BasicOnly = true, Ram = ram, Autorun = autorun, MakeListing = false });
    }

    static string Errors(CompileResult r) { return string.Join(" / ", r.Diagnostics.Where(d => d.IsError).Select(d => d.ToString())); }
    static string Warnings(CompileResult r) { return string.Join(" / ", r.Diagnostics.Where(d => !d.IsError).Select(d => d.ToString())); }

    /// <summary>The program as the ZX81 would LIST it.</summary>
    static string Listing(CompileResult r)
    {
        int esc;
        return Basic81.ProgramText(PFile.Load(r.PFile), new DetokOptions { Style = ListStyle.Exact }, out esc).Replace("\r", "").Trim()
            .Split('\n').Select(l => l.TrimEnd()).Aggregate((x, y) => x + "\n" + y);   // the ZX81 lists CLS as "CLS "
    }

    public static int Main(string[] args)
    {
        // 1. a plain listing gives exactly the same .p file as tokenising it directly
        //    (the way the tests make interpreted BASIC to compare compiled programs with)
        string t1 = File.ReadAllText(args[0]);
        var d0 = new List<Diagnostic>();
        byte[] direct = Basic81.BuildP(Basic81.TokenizeProgram(t1, d0), null, false, false, -1);
        var r1 = Build(t1, 16, false);
        Check("a plain listing: the same bytes as tokenising it directly", r1.Success && r1.PFile.SequenceEqual(direct), Errors(r1));
        Check("it is BASIC, not compiled: no machine code line", r1.BasicOnly && !Listing(r1).Contains("USR 16514"));

        // 2. labels, missing numbers, LET and THEN GOTO put right
        string lab = "#BASIC_ONLY\n10 CLS\nN=4\nIF N>3 THEN @big\nPRINT \"SMALL\"\n@big: PRINT \"BIG\";N\nIF N=4 THEN PRINT \"X=1\"\nIF N=4 THEN X=2\nSTOP\n";
        var r2 = Compiler.Compile(lab, new Options());      // chosen by the #BASIC_ONLY line alone
        string want2 = "10 CLS\n20 LET N=4\n30 IF N>3 THEN GOTO 50\n40 PRINT \"SMALL\"\n50 PRINT \"BIG\";N\n60 IF N=4 THEN PRINT \"X=1\"\n70 IF N=4 THEN LET X=2\n80 STOP";
        string got2 = r2.Success ? Listing(r2) : "";
        Check("#BASIC_ONLY in the listing chooses BASIC only", r2.BasicOnly && r2.Success, Errors(r2));
        Check("labels numbered; LET and THEN GOTO added (not inside strings)", got2 == want2, got2.Replace("\n", " | "));
        Check("each change is reported as a warning", r2.Diagnostics.Count(d => !d.IsError && d.Message.StartsWith("LET added")) == 2 &&
            r2.Diagnostics.Count(d => !d.IsError && d.Message.StartsWith("GOTO added")) == 1, Warnings(r2));
        Check("auto-runs from the first line", r2.Success && PFile.Load(r2.PFile).AutoRunLine != null && PFile.Load(r2.PFile).AutoRunLine.Number == 10);
        Check("no auto-run when it is turned off", PFile.Load(Build(lab, 16, false).PFile).AutoRunLine == null);
        Check("lines are sorted by number", Listing(Build("20 PRINT 2\n10 PRINT 1\n")) == "10 PRINT 1\n20 PRINT 2");

        // 3. the ZX81's rules, not the compiler's
        var r3 = Build("10 LET N=3\n20 DIM A(N)\n30 DIM B(2,3,4)\n40 LET B(1,2,3)=5\n50 PRINT B(1,2,3)\n");
        Check("DIM A(N) and three dimensions are fine (the ZX81 allows them)", r3.Success, Errors(r3));
        var c3 = Compiler.Compile("10 LET N=3\n20 DIM A(N)\n", new Options());
        Check("... but still errors when compiled", !c3.Success && Errors(c3).Contains("DIM sizes must be constant"));
        Check("a statement separator is an error", Errors(Build("10 PRINT 1: PRINT 2\n")).Contains("statement separator"));
        Check("a missing quote is an error", Errors(Build("10 PRINT \"HELLO\n")).Contains("missing closing quote"));
        Check("an unknown label is an error", Errors(Build("10 GOTO @nowhere\n")).Contains("@nowhere is not defined"));
        var un = Build("10 PRINT 1\nPRINT 2\n");
        Check("lines without numbers are numbered even with no labels", un.Success && Listing(un) == "10 PRINT 1\n20 PRINT 2", Errors(un));
        var r4 = Build("#STRICT_INT\n10 PRINT 7/2\n");
        Check("#STRICT_INT has no effect (with a warning)", r4.Success && Warnings(r4).Contains("no effect with BASIC only"), Warnings(r4));
        Check("the compiler's own # lines are not put in the program", r4.Success && Listing(r4) == "10 PRINT 7/2");
        var r5 = Build("10 PRINT \"hello\"\n");
        var r5i = Compiler.Compile("10 PRINT \"hello\"\n", new Options { BasicOnly = true, LowerIsInverse = true });
        Check("lower case: normal letters, or inverse with the option", r5.Success && r5i.Success && Listing(r5) == "10 PRINT \"HELLO\"" &&
            PFile.Load(r5i.PFile).Lines[0].Body.Contains((byte)(0x2D | 0x80)) && !PFile.Load(r5.PFile).Lines[0].Body.Contains((byte)(0x2D | 0x80)));

        // 4. memory: 1K and 2K
        var k1 = Build("10 PRINT \"HI\"\n", 1);
        var pk = k1.Success ? PFile.Load(k1.PFile) : null;
        Check("1K: a collapsed display (25 NEWLINEs)", pk != null && pk.Display.Collapsed && pk.VarsAddr - pk.DFile == 25, Errors(k1));
        Check("1K: the .p file is small", k1.Success && k1.PFile.Length < 200, k1.Success ? k1.PFile.Length.ToString() : "");
        var big = Build("10 DIM A(300)\n20 PRINT 1\n", 1);
        Check("1K: an array too big for 1K is an error", !big.Success && Errors(big).Contains("doesn't fit a ZX81 with 1K"), Errors(big));
        Check("... that fits 16K", Build("10 DIM A(300)\n20 PRINT 1\n", 16).Success);
        Check("2K: room for it", Build("10 DIM A(200)\n20 PRINT 1\n", 2).Success);
        Check("64K: a note that BASIC only uses 16K", Warnings(Build("10 PRINT 1\n", 64)).Contains("first 16K"));
        var c1 = Compiler.Compile("10 PRINT 1\n", new Options { Ram = 1 });
        Check("a compiled program for 1K is an error", !c1.Success && Errors(c1).Contains("needs at least 16K"));

        // 5. compiling is unchanged when BASIC only isn't chosen
        var comp = Compiler.Compile("10 PRINT 1\n", new Options());
        Check("without BASIC only the listing is compiled as before", comp.Success && !comp.BasicOnly && comp.CodeSize > 0);

        Console.WriteLine(fails == 0 ? "all OK" : fails + " failure(s)");
        return fails == 0 ? 0 : 1;
    }
}
