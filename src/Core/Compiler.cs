// =====================================================================================
//  Compiler.cs - the top level: listing text -> .p file
// -------------------------------------------------------------------------------------
//  Compile() runs the whole process:
//     1. ParseProgram: split the text into lines, read each with the Lexer/Parser
//     2. CodeGen: turn the program into Z80 assembly
//     3. Z80Asm: assemble it (twice - see below)
//     4. BuildPFile: wrap the machine code in a ZX81 program and save-file image
//
//  WHAT A .p FILE IS
//  -----------------
//  A .p file is exactly the bytes the ZX81 saves to tape: its memory from address
//  4009h (16393) up to the system variable E_LINE. That is:
//
//     4009h  system variables (116 bytes: cursor, print position, pointers ...)
//     407Dh  the BASIC program:
//               line 0:  REM <our machine code>   - code starts at 4082h = 16514
//               line 10: RAND USR 16514           - runs it
//            the display file (screen memory), fully expanded: 793 bytes
//            the BASIC variables area: just the end marker 80h
//     E_LINE (end of the file)
//
//  Each BASIC line in memory is: line number (2 bytes, high byte FIRST),
//  length of the rest (2 bytes, low byte first), the tokens, then 76h (NEWLINE).
//
//  WHERE THE VARIABLES GO
//  ----------------------
//  The compiled program's variables are not saved in the file (that would make it
//  longer to load from tape). They go in free memory 256 bytes after E_LINE, leaving
//  room for the ZX81's edit line and calculator stack, and are cleared when the
//  program starts. That address ("BSS") depends on how big the code is, so the
//  program is assembled once to measure the code, then again with the real address.
//  (Every instruction has the same size whatever its operands, so the size can't
//  change between the two.)
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>Everything Compile() produces.</summary>
    public class CompileResult
    {
        public bool Success;              // true if a .p file was produced
        public byte[] PFile;              // the .p file contents
        public string Listing;            // assembly listing (if Options.MakeListing)
        public List<Diagnostic> Diagnostics = new List<Diagnostic>();   // errors and warnings
        public int CodeSize, VarsAddress, VarsSize, ProgramLines;       // figures for the summary
        public int Ram = 16;              // RAM the program was compiled for (Options.Ram)
        public NumberMode Numbers;        // how numbers were worked out (option or directive)
        public bool StrictInt { get { return Numbers == NumberMode.IntegerOnly; } }
        public int MemoryEnd;             // first byte after the variables (0 if the layout wasn't reached)
        public bool UsesCalc;             // uses the ROM calculator (extra room after E_LINE)
        /// <summary>Summary shown after a successful compile: several lines (sizes in bytes
        /// and K, where things go, how much memory is left), separated by newlines.</summary>
        public string Summary;
        public string Symbols = "";       // label table, appended to the .asm listing
    }

    public static class Compiler
    {
        public const int CodeStart = 16514;   // first byte after "0 REM"
        const int ProgStart = 0x407D;           // start of the BASIC program in memory
        const int MemoryTop = 0x8000;           // 16K RAM pack: top of RAM; also the top of code and display for any pack
        const int HighVars = 0x8000;            // 32K / 64K packs: where the variables start
        const int StackReserve = 0x200;         // room left below the top of memory for the machine stack

        /// <summary>Compile a whole listing. Check result.Success, then use result.PFile;
        /// result.Diagnostics has the errors and warnings either way.</summary>
        public static CompileResult Compile(string text, Options opt)
        {
            var res = new CompileResult();
            // directives: a line #FULL_FLOAT, #FLOAT_ISLANDS or #STRICT_INT chooses how
            // numbers are worked out, whatever the options say (the last one counts)
            opt = opt.Clone();
            NumberMode chosen = opt.Numbers;
            string directive = null;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                var m = NumberModes.Directive(raw);
                if (m != null) { opt.Numbers = m.Value; directive = raw.Trim().ToUpperInvariant(); }
            }
            res.Numbers = opt.Numbers;
            // (say so when it overrides a choice other than the default)
            if (directive != null && opt.Numbers != chosen && chosen != NumberMode.FullFloat)
                res.Diagnostics.Add(new Diagnostic { BasicLine = -1, Message = "the line " + directive + " in the listing sets the numbers to " +
                    NumberModes.Display[(int)opt.Numbers].ToLowerInvariant() + " (instead of " + NumberModes.Display[(int)chosen].ToLowerInvariant() + ")" });
            // labels and unnumbered lines (Labels.cs): numbered first, keeping every text
            // line in its place so messages still point at the right line
            if (opt.Labels || Labels.Wanted(text))
            {
                text = Labels.Number(text, true, res.Diagnostics);
                if (text == null) return res;
            }
            var lines = ParseProgram(text, opt, res.Diagnostics);
            res.ProgramLines = lines.Count;
            if (res.Diagnostics.Any(d => d.IsError)) return res;
            if (lines.Count == 0)
            {
                res.Diagnostics.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = "no numbered BASIC lines found" });
                return res;
            }

            CodeGen gen;
            byte[] code;
            try
            {
                gen = new CodeGen(lines, opt);
                gen.Generate();
                res.Diagnostics.AddRange(gen.Diags);
                if (res.Diagnostics.Any(d => d.IsError)) return res;

                // the digits of the NEEDS 32K RAM message (32K / 64K packs only)
                bool big = opt.Ram > 16 && HighVars + gen.BssSize > 0xC000;
                gen.Asm.Define("RAMD1", big ? 0x22 : 0x1F);   // 6 or 3
                gen.Asm.Define("RAMD2", big ? 0x20 : 0x1E);   // 4 or 2
                // first assembly with a provisional variables address, to find the code size
                gen.Asm.Define("BSS", 0x6000);
                code = gen.Asm.Assemble(CodeStart, false);
                int vars = opt.Ram > 16 ? HighVars : VarsAddress(code.Length, gen.UsesCalc);
                // second assembly with the real address
                gen.Asm.Symbols.Clear();
                gen.Asm.Define("RAMD1", big ? 0x22 : 0x1F);
                gen.Asm.Define("RAMD2", big ? 0x20 : 0x1E);
                gen.Asm.Define("BSS", vars);
                code = gen.Asm.Assemble(CodeStart, opt.MakeListing);
                if (opt.Ram == 16 && VarsAddress(code.Length, gen.UsesCalc) != vars) throw new AsmException("internal: code size changed");
                res.VarsAddress = vars;
                res.VarsSize = gen.BssSize;
                res.CodeSize = code.Length;
                if (gen.Asm.Listing != null) res.Listing = gen.Asm.Listing.ToString();
                // label table in address order, for the .asm listing file
                var symsb = new StringBuilder("\n; ---- symbols ----\n");
                foreach (var kv in gen.Asm.Symbols.OrderBy(k => k.Value)) symsb.AppendLine("; " + kv.Key + " = " + kv.Value.ToString("X4"));
                res.Symbols = symsb.ToString();
            }
            catch (AsmException e)
            {
                res.Diagnostics.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = e.Message });
                return res;
            }

            int end = res.VarsAddress + res.VarsSize;
            res.Ram = opt.Ram;
            res.MemoryEnd = end;
            res.UsesCalc = gen.UsesCalc;
            string err = CheckMemory(opt, res.CodeSize, gen.UsesCalc, end);
            if (err != null)
            {
                res.Diagnostics.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = err });
                return res;
            }

            res.PFile = BuildPFile(code, opt.Autorun);
            res.Success = true;
            res.ProgramLines = lines.Count;
            res.Summary = string.Format("Compiled {0} BASIC lines for a ZX81 with {1}K RAM; numbers: {2}.", lines.Count, opt.Ram, NumberModes.Describe(opt.Numbers)) + "\n" +
                SizeLines(res) + "\n" + ".p file: " + Size(res.PFile.Length) + ".";
            return res;
        }

        // ---------------- sizes for people ----------------

        /// <summary>A size as "3,456 bytes (3.38K)" (1K = 1024 bytes).</summary>
        public static string Size(int bytes)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return bytes.ToString("N0", inv) + " bytes (" + (bytes / 1024.0).ToString("0.00", inv) + "K)";
        }

        /// <summary>The size lines of a compile (successful, or one that only failed on
        /// memory): machine code, variables, and the memory used and left.</summary>
        public static string SizeLines(CompileResult r)
        {
            var sb = new StringBuilder();
            sb.Append("Machine code: " + Size(r.CodeSize) + " at " + CodeStart + "-" + (CodeStart + r.CodeSize - 1) + ".\n");
            sb.Append("Variables: " + Size(r.VarsSize) + " at " + r.VarsAddress + "-" + (r.MemoryEnd - 1) + " (made when the program runs; not in the .p file).\n");
            sb.Append(MemoryLine(r));
            return sb.ToString();
        }

        /// <summary>How much of the RAM the program uses, and what is left for the stack.</summary>
        static string MemoryLine(CompileResult r)
        {
            if (r.Ram == 16)
            {
                int used = r.MemoryEnd - 0x4000;               // 16384 up to the end of the variables
                int left = MemoryTop - StackReserve - r.MemoryEnd;
                return "Memory: " + Size(used) + " of the 16K RAM used (program, screen, workspace and variables); " +
                    (left >= 0 ? "about " + Size(left) + " left." : "too much by " + Size(-left) + ".");
            }
            int low = VarsAddress(r.CodeSize, r.UsesCalc) - 0x4000;      // below 32K: program, screen and workspace
            int top = r.Ram == 32 ? 0xC000 : 0x10000;
            int high = r.MemoryEnd - HighVars, room = top - StackReserve - HighVars;
            return "Memory: below 32K " + Size(low) + " used (program, screen and workspace; about " + Size(Math.Max(0, MemoryTop - StackReserve - low - 0x4000)) +
                " left); above 32K " + Size(high) + " of about " + Size(room) + " used by the variables.";
        }

        /// <summary>Compile the listing just to see how big it will be: used by the GUI when
        /// a listing is loaded. Says whether it fits the RAM chosen in opt, and if it doesn't
        /// fit 16K, whether it would fit a 32K or 64K RAM pack. Never throws.</summary>
        public static string Estimate(string listing, Options opt)
        {
            try
            {
                var o = opt.Clone();      // every option (RAM, numbers, labels ...), without the listing
                o.MakeListing = false;
                var r = Compile(listing, o);
                if (r.MemoryEnd == 0)
                {
                    int errs = r.Diagnostics.Count(d => d.IsError);
                    return "Estimated size: not known yet - the listing has " + errs + " error" + (errs == 1 ? "" : "s") + " (press F7 to check the syntax and see them).";
                }
                string fits;
                if (r.Success)
                    fits = "It fits a ZX81 with " + o.Ram + "K RAM.";
                else
                {
                    fits = "It does NOT fit a ZX81 with " + o.Ram + "K RAM.";
                    foreach (int bigger in new[] { 32, 64 })
                    {
                        if (bigger <= o.Ram) continue;
                        o.Ram = bigger;
                        var rb = Compile(listing, o);
                        if (rb.Success) { fits += " It would fit a " + bigger + "K RAM pack (choose " + bigger + "K in the RAM box)."; break; }
                        if (rb.Diagnostics.Any(d => d.IsError && d.Message.StartsWith("the machine code is too big")))
                        {
                            fits += " The machine code itself is too big for any ZX81, whatever RAM is fitted: it has to fit below 32K (32768) with the screen.";
                            break;
                        }
                        if (bigger == 64) fits += " It is too big even for a 64K RAM pack.";
                    }
                }
                return "Estimated size when compiled:\n" + SizeLines(r) + "\n" + fits;
            }
            catch (Exception ex) { return "Estimated size: not known (" + ex.Message + ")."; }
        }

        /// <summary>Where the variables will go, for a given code size. Must match the
        /// layout BuildPFile produces.</summary>
        /// <summary>Extra room left after E_LINE when the program uses the ROM calculator
        /// (floating point, VAL, SAVE/LOAD): the calculator stack and the ROM's workspace
        /// (text made by STR$, the copy VAL makes of its string) grow up from there.</summary>
        public const int CalcReserve = 768;

        /// <summary>Does the program fit the chosen RAM? null if so, else the error message.
        ///  16K: code, display, the ROM's workspace, the variables and the stack all below 8000h.
        ///  32K/64K: code, display, workspace and the stack below 8000h (the ROM puts the stack
        ///  there unless RAMTOP is raised; code can only run, and the display only be shown,
        ///  below 8000h); the variables from 8000h up to BFFFh (32K) or FFFFh (64K).</summary>
        static string CheckMemory(Options opt, int codeLength, bool usesCalc, int end)
        {
            int low = VarsAddress(codeLength, usesCalc);          // first free byte after the display, edit line and calculator room
            if (opt.Ram == 16)
            {
                if (end <= MemoryTop - StackReserve) return null;
                return string.Format("program needs memory up to {0} but a 16K ZX81 only has room up to about {1}. " +
                    "Try a smaller 'max string length' (each string variable reserves that much), or compile for a 32K or 64K RAM pack.",
                    end, MemoryTop - StackReserve);
            }
            if (low > MemoryTop - StackReserve)
                return string.Format("the machine code is too big: with the screen it needs memory up to {0}, but a ZX81 can only run machine code " +
                    "below 32768 (8000h) whatever RAM pack is fitted, leaving room for the stack up to about {1}.", low, MemoryTop - StackReserve);
            int top = opt.Ram == 32 ? 0xC000 : 0x10000;
            if (end > top - StackReserve)
                return string.Format("the variables need memory up to {0} but a {1}K RAM pack only has room up to about {2}. " +
                    "Try a smaller 'max string length'{3}.", end, opt.Ram, top - StackReserve, opt.Ram == 32 ? ", or compile for 64K" : "");
            return null;
        }

        static int VarsAddress(int codeLength, bool usesCalc)
        {
            int dfile = ProgStart + (4 + 1 + codeLength + 1) + Line10().Length;   // line 0 = header + REM + code + 76h
            int varsArea = dfile + 793;   // display file: 76h + 24 x (32 + 76h)
            int eLine = varsArea + 1;     // VARS area is just the 80h end marker
            return eLine + 256 + (usesCalc ? CalcReserve : 0);  // leave room for the edit line and calculator stack
        }

        /// <summary>Split the text into numbered lines and parse each one. Lines are
        /// sorted by number (the text doesn't have to be in order); blank lines and lines
        /// starting with ; # or // are skipped. All errors are collected, not just the first.</summary>
        public static List<BasicLine> ParseProgram(string text, Options opt, List<Diagnostic> diags)
        {
            var byNumber = new SortedDictionary<int, BasicLine>();
            string[] src = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < src.Length; i++)
            {
                string raw = src[i];
                string l = raw.Trim();
                if (l.Length == 0) continue;
                // the line number
                int p = 0;
                while (p < l.Length && char.IsDigit(l[p])) p++;
                if (p == 0)
                {
                    if (l.StartsWith(";") || l.StartsWith("#") || l.StartsWith("//")) continue; // comment lines
                    diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = i + 1, Message = "line does not start with a line number: " + l });
                    continue;
                }
                int num = int.Parse(l.Substring(0, Math.Min(p, 6)));
                if (num < 1 || num > 9999)
                {
                    diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = i + 1, Message = "line number " + num + " out of range (1-9999)" });
                    continue;
                }
                // the statement after it
                string body = l.Substring(p);
                try
                {
                    var lex = new Lexer(body, num, opt.LowerIsInverse);
                    var toks = lex.Run();
                    foreach (var w in lex.Warnings) diags.Add(new Diagnostic { BasicLine = num, SourceLine = i + 1, Message = w });
                    var par = new Parser { FullFloat = opt.FullFloat };
                    // a line number with nothing after it (just the End token) does nothing
                    Stmt st = toks.Count == 1 ? new SimpleStmt("NOP") : par.ParseLine(toks, num);
                    foreach (var w in par.Warnings) diags.Add(new Diagnostic { BasicLine = num, SourceLine = i + 1, Message = w });
                    if (byNumber.ContainsKey(num))
                        diags.Add(new Diagnostic { BasicLine = num, SourceLine = i + 1, Message = "line number used twice; the later line replaces the earlier one" });
                    byNumber[num] = new BasicLine { Number = num, SourceLine = i + 1, Stmt = st };
                }
                catch (CompileError e)
                {
                    diags.Add(new Diagnostic { IsError = true, BasicLine = num, SourceLine = i + 1, Message = e.Message });
                }
            }
            return byNumber.Values.ToList();
        }

        // ---------------- .p file ----------------
        /// <summary>A number in the ZX81's 5-byte floating point format. Inside a BASIC
        /// line, each number is stored twice: as the digits you see, then 7Eh and these
        /// 5 bytes, which are what the ROM actually uses.
        /// Format: exponent byte (80h + number of bits), then 4 mantissa bytes with the top
        /// bit replaced by the sign (0 = positive). 16514 gives 8F 01 04 00 00.</summary>
        static byte[] ZxNumber(int n)
        {
            // 5-byte ZX81 floating point for a positive integer
            if (n == 0) return new byte[5];
            int bits = 0; while ((n >> bits) != 0) bits++;   // how many bits the number needs
            uint mant = (uint)n << (32 - bits);               // move its top 1 bit to bit 31
            mant &= 0x7FFFFFFF; // sign bit = positive
            return new[] { (byte)(0x80 + bits), (byte)(mant >> 24), (byte)(mant >> 16), (byte)(mant >> 8), (byte)mant };
        }

        /// <summary>The bytes of line 10: RAND USR 16514</summary>
        static byte[] Line10()
        {
            var body = new List<byte> { 0xF9, 0xD4 }; // RAND USR
            foreach (char c in CodeStart.ToString()) body.Add((byte)(0x1C + (c - '0')));   // the digits 1 6 5 1 4
            body.Add(0x7E);                            // hidden number follows
            body.AddRange(ZxNumber(CodeStart));
            body.Add(0x76);                            // end of line
            var line = new List<byte> { 0, 10, (byte)body.Count, (byte)(body.Count >> 8) };   // line number 10, length
            line.AddRange(body);
            return line.ToArray();
        }

        /// <summary>Build the complete .p file around the machine code.
        /// autorun: the program starts by itself after LOAD.</summary>
        public static byte[] BuildPFile(byte[] code, bool autorun)
        {
            var prog = new List<byte>();
            // line 0: REM <machine code>
            int len0 = 1 + code.Length + 1;   // REM token + code + 76h
            prog.AddRange(new byte[] { 0, 0, (byte)len0, (byte)(len0 >> 8), 0xEA });
            prog.AddRange(code);
            prog.Add(0x76);
            int line10Addr = ProgStart + prog.Count;
            prog.AddRange(Line10());

            int dfile = ProgStart + prog.Count;
            // fully expanded display file
            // (A 16K ZX81 always uses the full 24 lines x 32 columns; the compiled program
            // writes characters straight into it.)
            prog.Add(0x76);
            for (int r = 0; r < 24; r++) { for (int c = 0; c < 32; c++) prog.Add(0); prog.Add(0x76); }
            int vars = ProgStart + prog.Count;
            prog.Add(0x80);                    // empty BASIC variables area
            int eLine = ProgStart + prog.Count;

            // The system variables. sv[0] is address 4009h. Values are what the ZX81
            // would have after SAVE; most matter little because the ROM resets them.
            var sv = new byte[ProgStart - 0x4009];
            Action<int, int> w8 = (a, v) => sv[a - 0x4009] = (byte)v;                                              // write a byte
            Action<int, int> w16 = (a, v) => { sv[a - 0x4009] = (byte)v; sv[a - 0x4009 + 1] = (byte)(v >> 8); };   // write a word
            w8(0x4009, 0);            // VERSN
            w16(0x400A, 10);          // E_PPC
            w16(0x400C, dfile);       // D_FILE
            w16(0x400E, dfile + 1);   // DF_CC
            w16(0x4010, vars);        // VARS
            w16(0x4012, 0);           // DEST
            w16(0x4014, eLine);       // E_LINE
            w16(0x4016, eLine - 1);   // CH_ADD
            w16(0x4018, 0);           // X_PTR
            w16(0x401A, eLine + 1);   // STKBOT
            w16(0x401C, eLine + 1);   // STKEND
            w8(0x401E, 0);            // BERG
            w16(0x401F, 0x405D);      // MEM -> MEMBOT
            w8(0x4022, 2);            // DF_SZ
            w16(0x4023, 2);           // S_TOP
            w16(0x4025, 0xFFFF);      // LAST_K
            w8(0x4027, 0xFF);         // DB_ST
            w8(0x4028, 55);           // MARGIN
            // NXTLIN: the line to run after LOADing. Pointing it at line 10 starts the
            // program automatically; pointing it at the display file (which doesn't look
            // like a line) means "don't run anything".
            w16(0x4029, autorun ? line10Addr : dfile); // NXTLIN
            w16(0x402B, 0);           // OLDPPC
            w8(0x402D, 0);            // FLAGX
            w16(0x402E, 0);           // STRLEN
            w16(0x4030, 0x0C8D);      // T_ADDR
            w16(0x4032, 0);           // SEED
            w16(0x4034, 0xFFFF);      // FRAMES
            w16(0x4036, 0);           // COORDS
            w8(0x4038, 0xBC);         // PR_CC
            w8(0x4039, 33);           // S_POSN column
            w8(0x403A, 24);           // S_POSN line
            w8(0x403B, 0x40);         // CDFLAG: SLOW mode
            for (int i = 0; i < 32; i++) w8(0x403C + i, 0); // PRBUFF
            w8(0x403C + 32, 0x76);
            // MEMBOT and spare bytes are already zero

            var file = new List<byte>(sv);
            file.AddRange(prog);
            return file.ToArray();
        }
    }
}
