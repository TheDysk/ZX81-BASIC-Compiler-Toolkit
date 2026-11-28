// =====================================================================================
//  BasicOnly.cs - "BASIC only" output: a listing -> a .p file of ordinary ZX81 BASIC
// -------------------------------------------------------------------------------------
//  Instead of compiling to machine code, the listing is put into the .p file as the
//  BASIC program itself, exactly as if it had been typed in on a ZX81, so the ZX81's
//  own interpreter runs it (and it can be LISTed, edited and SAVEd there).
//
//  Chosen by the compiler's Output box, zx81c --basic-only, a #BASIC_ONLY line in the
//  listing, or the .P Toolkit's File > Import BASIC Listing. The steps:
//     1. labels and unnumbered lines are turned into line numbers (Labels.cs) - always,
//        not only when the listing has labels - and the compiler's own # lines are left out;
//     2. every line is checked with the compiler's parser (in Interpreted mode, so
//        only the ZX81's own rules apply: DIM A(N) and 3-dimensional arrays are fine);
//     3. two things the compiler accepts but the ZX81 doesn't are put right, with a
//        warning: an assignment without LET (X=1 becomes LET X=1) and IF ... THEN
//        followed by a line number (THEN 100 becomes THEN GOTO 100);
//     4. the lines are tokenised (Basic81.TokenizeBody) - keywords become their
//        one-byte codes and every number gets its hidden 5-byte value - sorted by
//        line number, and built into a .p file (Basic81.BuildP), set to run from the
//        first line after LOAD unless auto-run is off;
//     5. the memory it needs is checked against the RAM chosen (1K, 2K or 16K+).
//
//  Numbers: the ZX81's BASIC always uses full floating point, so the Numbers option
//  and #FLOAT_ISLANDS / #STRICT_INT lines don't apply here.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZX81Compiler
{
    public static class BasicOnly
    {
        /// <summary>The line that chooses BASIC only output in a listing.</summary>
        public const string Directive = "#BASIC_ONLY";

        /// <summary>Is this line the #BASIC_ONLY directive?</summary>
        public static bool IsDirective(string line)
        {
            return (line ?? "").Trim().ToUpperInvariant() == Directive;
        }

        /// <summary>Does the listing contain a #BASIC_ONLY line?</summary>
        public static bool Wanted(string text)
        {
            foreach (string l in (text ?? "").Replace("\r", "").Split('\n')) if (IsDirective(l)) return true;
            return false;
        }

        // keyword codes used when putting lines right
        static byte Kw(string k) { return Basic81.KeywordCodes[k]; }

        /// <summary>Build the .p file. Uses opt.Autorun, opt.LowerIsInverse and
        /// opt.Ram (1, 2, 16, 32 or 64). Never throws for a bad listing: check Success and
        /// Diagnostics.</summary>
        public static CompileResult Build(string text, Options opt)
        {
            var res = new CompileResult { Ram = opt.Ram, Numbers = NumberMode.FullFloat, BasicOnly = true };
            var diags = res.Diagnostics;

            // 0. number-mode lines don't apply
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                var m = NumberModes.Directive(raw);
                if (m != null && m.Value != NumberMode.FullFloat)
                    diags.Add(new Diagnostic { BasicLine = -1, Message = "the line " + raw.Trim().ToUpperInvariant() +
                        " has no effect with BASIC only output: the ZX81's own BASIC always uses full floating point" });
            }

            // 1. labels -> line numbers (keeping every text line in place). Always done here,
            //    so lines without a number are numbered even with no labels in the listing:
            //    getting a listing written on the PC into a ZX81 is what this is for
            text = Labels.Number(text, true, diags);
            if (text == null) return res;

            // 2. check every line as the ZX81 would
            var o = opt.Clone();
            o.Numbers = NumberMode.FullFloat;
            o.BasicOnly = true;
            var check = new List<Diagnostic>();
            var parsed = Compiler.ParseProgram(text, o, check);
            // keep the errors and "line number used twice"; the parser's other warnings are
            // about compiled programs (LIST does nothing ...), which don't apply here
            foreach (var d in check)
                if (d.IsError || d.Message.StartsWith("line number used twice")) diags.Add(d);
            if (diags.Any(d => d.IsError)) return res;
            if (parsed.Count == 0)
            {
                diags.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = "no numbered BASIC lines found" });
                return res;
            }

            // 3 + 4. tokenise each line, put it right, sort (a later line replaces an earlier one)
            var byNumber = new SortedDictionary<int, byte[]>();
            string[] src = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < src.Length; i++)
            {
                string l = src[i].Trim();
                if (l.Length == 0 || !char.IsDigit(l[0])) continue;   // blank, # ; // lines: not BASIC
                int p = 0;
                while (p < l.Length && char.IsDigit(l[p])) p++;
                int num = int.Parse(l.Substring(0, Math.Min(p, 6)));
                string body = l.Substring(p);
                if (body.StartsWith(" ")) body = body.Substring(1);
                var warn = new List<string>();
                byte[] bytes;
                try { bytes = Basic81.TokenizeBody(body, warn, opt.LowerIsInverse); }
                catch (CompileError e)
                {
                    diags.Add(new Diagnostic { IsError = true, BasicLine = num, SourceLine = i + 1, Message = e.Message });
                    continue;
                }
                foreach (var w in warn) diags.Add(new Diagnostic { BasicLine = num, SourceLine = i + 1, Message = w });
                foreach (var fix in PutRight(ref bytes))
                    diags.Add(new Diagnostic { BasicLine = num, SourceLine = i + 1, Message = fix });
                if (bytes.Length == 1)
                    diags.Add(new Diagnostic { BasicLine = num, SourceLine = i + 1, Message = "line " + num + " is empty: on a ZX81 typing just a line number deletes the line, so it is left out" });
                else byNumber[num] = bytes;
            }
            if (diags.Any(d => d.IsError)) return res;
            var lines = byNumber.Select(kv => new KeyValuePair<int, byte[]>(kv.Key, kv.Value)).ToList();
            if (lines.Count == 0)
            {
                diags.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = "no BASIC lines to put in the program" });
                return res;
            }

            // 5. build it: an unexpanded ZX81 (1K / 2K) gets a collapsed display, as its own
            //    SAVE would make, since a full one would take 793 of its bytes
            bool small = opt.Ram < 16;
            byte[] file = Basic81.BuildP(lines, null, false, false, opt.Autorun ? lines[0].Key : -1);
            if (small) file = Collapse(file);
            res.PFile = file;
            res.ProgramLines = lines.Count;

            int progBytes = lines.Sum(kv => 4 + kv.Value.Length);
            int fileEnd = 0x4009 + file.Length;                 // E_LINE: where the .p file ends in memory
            int ramtop = opt.Ram >= 16 ? 0x8000 : 0x4000 + opt.Ram * 1024;
            int vars = EstimateVariables(parsed, text, o);
            const int workspace = 100;                          // edit line, calculator and GOSUB stacks: a minimum
            int left = ramtop - fileEnd - vars - workspace;
            res.CodeSize = progBytes;
            res.VarsSize = vars;
            res.MemoryEnd = fileEnd + vars;

            string ramName = opt.Ram >= 16 ? "16K" : opt.Ram + "K";
            if (opt.Ram > 16)
                diags.Add(new Diagnostic { BasicLine = -1, Message = "ordinary ZX81 BASIC only uses the first 16K of RAM (the ZX81 sets RAMTOP no higher than 32768), " +
                    "so a " + opt.Ram + "K RAM pack gives it no more room than 16K" });
            if (left < 0)
            {
                diags.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = string.Format(
                    "the program doesn't fit a ZX81 with {0} RAM: it needs about {1} (program and screen {2}, variables at least {3}, " +
                    "and {4} bytes of workspace) but there are only {5}{6}",
                    ramName, Compiler.Size(fileEnd - 0x4000 + vars + workspace), Compiler.Size(fileEnd - 0x4000), Compiler.Size(vars), workspace,
                    Compiler.Size(ramtop - 0x4000), opt.Ram < 16 ? ". Choose 16K in the RAM box if a 16K RAM pack is fitted." : "") });
                res.PFile = null;
                return res;
            }
            if (left < 128)
                diags.Add(new Diagnostic { BasicLine = -1, Message = "only about " + left + " bytes will be left for strings, GOSUB and the calculator: " +
                    "the program may stop with report 4 (out of memory) on a ZX81 with " + ramName + " RAM" });

            res.Success = true;
            var sb = new StringBuilder();
            sb.Append(string.Format("BASIC only (not compiled): {0} BASIC lines for a ZX81 with {1} RAM, run by the ZX81's own BASIC (full floating point).\n", lines.Count, ramName));
            sb.Append("BASIC program: " + Compiler.Size(progBytes) + "; screen: " + Compiler.Size(small ? 25 : 793) + (small ? " (collapsed, as a 1K/2K ZX81 keeps it)" : "") + ".\n");
            sb.Append("Variables: at least " + Compiler.Size(vars) + " (made when the program runs).\n");
            sb.Append("Memory: about " + Compiler.Size(Math.Max(0, left)) + " of the " + ramName + " left for strings, GOSUB and the calculator.\n");
            sb.Append(".p file: " + Compiler.Size(file.Length) + (opt.Autorun ? "; runs from line " + lines[0].Key + " after LOAD." : "; doesn't run by itself after LOAD (type RUN)."));
            res.Summary = sb.ToString();
            return res;
        }

        /// <summary>Put right, in one tokenised line, the two things the compiler accepts but
        /// the ZX81 doesn't. Returns a message for each change made.</summary>
        static List<string> PutRight(ref byte[] body)
        {
            var msgs = new List<string>();
            var b = new List<byte>(body);
            byte LET = Kw("LET"), THEN = Kw("THEN"), GOTO = Kw("GOTO"), REM = Kw("REM");
            // statement starts: the beginning of the line, and after THEN
            int i = 0;
            bool start = true, afterThen = false;
            while (i < b.Count && b[i] != 0x76)
            {
                byte c = b[i];
                if (start)
                {
                    start = false;
                    if (c >= 0x26 && c <= 0x3F)            // a letter: an assignment without LET
                    {
                        b.Insert(i, LET);
                        msgs.Add("LET added: on a ZX81 every statement starts with a keyword, so an assignment needs LET");
                        i++;
                        afterThen = false;
                        continue;
                    }
                    if (afterThen && c >= 0x1C && c <= 0x25)   // a digit after THEN: a line number
                    {
                        b.Insert(i, GOTO);
                        msgs.Add("GOTO added after THEN: on a ZX81, THEN is followed by a statement (THEN GOTO 100, not THEN 100)");
                        i++;
                        afterThen = false;
                        continue;
                    }
                    afterThen = false;
                }
                if (c == REM) break;                        // the rest is text
                if (c == 0x0B)                              // a string: skip to its closing quote
                {
                    i++;
                    while (i < b.Count && b[i] != 0x0B && b[i] != 0x76) i++;
                    i++;
                    continue;
                }
                if (c == 0x7E) { i += 6; continue; }        // a number's hidden 5-byte value
                if (c == THEN) { start = true; afterThen = true; }
                i++;
            }
            body = b.ToArray();
            return msgs;
        }

        /// <summary>The same .p file with a collapsed display file (just the 25 NEWLINEs),
        /// the system variables' pointers moved to match.</summary>
        static byte[] Collapse(byte[] file)
        {
            const int Base = 0x4009;
            Func<int, int> r16 = a => file[a - Base] | file[a - Base + 1] << 8;
            int dfile = r16(0x400C);
            int full = 793, small = 25;
            int start = dfile - Base;
            var outp = new List<byte>();
            outp.AddRange(file.Take(start));
            for (int k = 0; k < small; k++) outp.Add(0x76);
            outp.AddRange(file.Skip(start + full));
            var f = outp.ToArray();
            int delta = full - small;
            Action<int, int> w16 = (a, v) => { f[a - Base] = (byte)v; f[a - Base + 1] = (byte)(v >> 8); };
            // pointers after the display file move down
            foreach (int a in new[] { 0x4010, 0x4014, 0x4016, 0x401A, 0x401C })
            {
                int v = f[a - Base] | f[a - Base + 1] << 8;
                if (v > dfile) w16(a, v - delta);
            }
            w16(0x400E, dfile + 1);                         // DF_CC: the start of the screen
            int nxt = f[0x4029 - Base] | f[0x4029 - Base + 1] << 8;
            if (nxt >= dfile + full) w16(0x4029, nxt - delta); else if (nxt > dfile) w16(0x4029, dfile);
            return f;
        }

        /// <summary>The least memory the variables will take when the program runs, in bytes:
        /// the arrays with constant sizes in DIM lines, a FOR loop's control variable (18
        /// bytes), and each other numeric (5 + name) and string (3 + text) variable.</summary>
        static int EstimateVariables(List<BasicLine> lines, string text, Options o)
        {
            int total = 0;
            var arrays = new HashSet<string>();
            var forVars = new HashSet<char>();
            foreach (var ln in lines)
            {
                for (Stmt st = ln.Stmt; st != null; st = st is IfStmt ? ((IfStmt)st).Then : null)
                {
                    var d = st as DimStmt;
                    if (d != null && d.Dims.All(n => n > 0))
                    {
                        string key = d.Name + (d.IsStr ? "$" : "");
                        if (arrays.Add(key))
                        {
                            int elems = d.Dims.Aggregate(1, (a, n) => a * n);
                            total += 4 + 2 * d.Dims.Count + elems * (d.IsStr ? 1 : 5);
                        }
                    }
                    var f = st as ForStmt;
                    if (f != null) forVars.Add(f.Var);
                }
            }
            total += 18 * forVars.Count;
            // other variables: names seen in the listing (not arrays, not FOR variables)
            var names = new HashSet<string>();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string l = raw.Trim();
                int p = 0;
                while (p < l.Length && char.IsDigit(l[p])) p++;
                if (p == 0) continue;
                List<Token> toks;
                try { toks = new Lexer(l.Substring(p), 0, o.LowerIsInverse).Run(); }
                catch (CompileError) { continue; }
                for (int k = 0; k < toks.Count; k++)
                {
                    var t = toks[k];
                    bool arr = k + 1 < toks.Count && toks[k + 1].Kind == TK.Op && toks[k + 1].Text == "(";
                    if (t.Kind == TK.Ident && !arr && !(t.Text.Length == 1 && forVars.Contains(t.Text[0]))) names.Add(t.Text);
                    else if (t.Kind == TK.StrIdent && !arrays.Contains(t.Text + "$")) names.Add(t.Text + "$");
                }
            }
            foreach (string n in names) total += n.EndsWith("$") ? 3 : 5 + n.Length;
            return total + 1;                               // + the 80h end marker
        }
    }
}
