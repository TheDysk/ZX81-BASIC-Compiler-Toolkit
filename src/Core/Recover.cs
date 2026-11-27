// =====================================================================================
//  Recover.cs - rebuilding the BASIC of a program made by this toolset's compiler
// -------------------------------------------------------------------------------------
//  General machine code can't be turned back into BASIC, but code made by our own
//  compiler can: the code generator (CodeGen.cs) always produces the same instruction
//  patterns for the same BASIC, so they can be read backwards.
//
//  Steps:
//   1. RECOGNISE the program: it starts with LD (ENTRYSP),SP / CALL rt_memcheck /
//      CALL rt_init, and ENTRYSP marks where the compiled lines end and the runtime
//      library begins.
//   2. IDENTIFY THE RUNTIME ROUTINES: each library chunk (Runtime.cs) is assembled on
//      its own twice, at different addresses; bytes that differ between the two are
//      addresses (wildcards), the rest must match exactly. The chunks were written in
//      name order, so they are matched one after another from ENTRYSP+2.
//   3. PRE-SCAN the compiled lines to classify the variable addresses (numeric
//      variables, FOR blocks, arrays, strings, temporary buffers).
//   4. INTERPRET the compiled lines symbolically: instead of numbers, the registers
//      hold expressions. E.g. "LD HL,(V); INC HL; LD (V),HL" builds X+1 in HL and then
//      the store becomes LET X=X+1. Calls to output routines (rt_prnum ...) and
//      conditional jumps become PRINT, IF, GOTO and so on.
//   5. NAME the variables (names aren't stored in machine code), NUMBER the lines
//      (real numbers if the program had a line table, else 10, 20, 30 ...).
//   6. VERIFY: compile the rebuilt listing and compare the machine code with the
//      original. If they're identical, the recovery is certainly right.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZX81Compiler
{
    public class RecoverResult
    {
        public bool IsOurs;          // the program was made by this compiler
        public bool Success;         // BASIC was rebuilt
        public bool Verified;        // ...and recompiles to identical machine code
        public string Listing;       // the rebuilt BASIC
        public Options Options = new Options();   // compiler options detected
        public int BssStart, BssSize;             // where the compiled program keeps its variables at run time
        public readonly List<string> Notes = new List<string>();
    }

    public class Recover
    {
        // ------------------------------------------------------------------
        //  Runtime chunk templates (built once)
        // ------------------------------------------------------------------
        class Template
        {
            public Runtime.Chunk Chunk;
            public byte[] Bytes;
            public bool[] Wild;                                         // true = address byte, can be anything
            public Dictionary<string, int> LabelOffsets = new Dictionary<string, int>();
        }
        static List<Template> templates;

        static List<Template> Templates()
        {
            if (templates != null) return templates;
            var list = new List<Template>();
            foreach (var ch in Runtime.Chunks.Values.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                byte[] a = AssembleChunk(ch, 0x5000, 0x6100, 0x7000);
                byte[] b = AssembleChunk(ch, 0x5833, 0x3155, 0x2123);
                var t = new Template { Chunk = ch, Bytes = a, Wild = new bool[a.Length] };
                for (int i = 0; i < a.Length; i++) t.Wild[i] = a[i] != b[i];
                var asm = BuildAsm(ch, 0x5000, 0x6100, 0x7000);
                asm.Assemble(0x5000, false);
                foreach (var kv in asm.Symbols)
                    if (kv.Value >= 0x5000 && kv.Value < 0x5000 + a.Length && !kv.Key.Contains("EQU") && ch.Code.Contains(kv.Key + ":"))
                        t.LabelOffsets[kv.Key] = kv.Value - 0x5000;
                list.Add(t);
            }
            templates = list;
            return list;
        }

        static Z80Asm BuildAsm(Runtime.Chunk ch, int org, int bss, int undef)
        {
            var asm = new Z80Asm();
            asm.Define("BSS", bss);
            // the digits of rt_memcheckx's message are single bytes: give them small values
            // that differ between the two assemblies, so they become wildcards
            asm.Define("RAMD1", undef & 0x7F);
            asm.Define("RAMD2", (undef >> 1) & 0x7F);
            asm.Undefined = name => (undef + Math.Abs(name.GetHashCode() % 0x600)) & 0xFFFF;
            foreach (var l in Runtime.BssEquates.Split('\n')) if (l.Trim().Length > 0) asm.Line(l.TrimEnd('\r'));
            foreach (var l in ch.Code.Split('\n')) if (l.Trim().Length > 0) asm.Line(l.TrimEnd('\r'));
            return asm;
        }

        static byte[] AssembleChunk(Runtime.Chunk ch, int org, int bss, int undef)
        {
            return BuildAsm(ch, org, bss, undef).Assemble(org, false);
        }

        static bool MatchAt(byte[] mem, int a, Template t)
        {
            if (a + t.Bytes.Length > mem.Length) return false;
            for (int i = 0; i < t.Bytes.Length; i++) if (!t.Wild[i] && mem[a + i] != t.Bytes[i]) return false;
            return true;
        }

        // ------------------------------------------------------------------
        //  State
        // ------------------------------------------------------------------
        PFile p;
        RecoverResult res = new RecoverResult();
        int entry, entrySp, mainStart, mainEnd, runtimeStart, bss, bssSize, rtStop;
        readonly Dictionary<int, string> rtAt = new Dictionary<int, string>();   // address -> runtime label
        List<Instr> code;                                                        // the compiled lines, decoded
        Dictionary<int, int> index;                                              // address -> position in code

        // variable addresses found by the pre-scan
        readonly SortedSet<int> numVars = new SortedSet<int>();
        readonly HashSet<int> forBlocks = new HashSet<int>();
        readonly SortedDictionary<int, int> dims = new SortedDictionary<int, int>();   // DIM base -> size in bytes
        readonly HashSet<int> numArrBase = new HashSet<int>();
        readonly Dictionary<int, int> strArr2 = new Dictionary<int, int>();             // 2D string array base -> element length
        readonly HashSet<int> strFixed = new HashSet<int>();
        readonly SortedSet<int> strDyn = new SortedSet<int>();
        readonly HashSet<int> temps = new HashSet<int>();
        readonly Dictionary<int, int> arr2Cols = new Dictionary<int, int>();             // numeric 2D array base -> columns
        readonly Dictionary<int, string> names = new Dictionary<int, string>();

        public static RecoverResult Run(PFile pf, CodeAnalysis ca)
        {
            var r = new Recover { p = pf };
            try { r.Go(ca); }
            catch (RecoverFail e) { r.res.Success = false; r.res.Notes.Add("BASIC could not be rebuilt: " + e.Message + (r.curAddr > 0 ? " (at " + Z80Disasm.Hex(r.curAddr, 4) + ")" : "")); }
            catch (Exception e) { r.res.Success = false; r.res.Notes.Add("BASIC could not be rebuilt (" + e.GetType().Name + ": " + e.Message + ")"); }
            return r.res;
        }

        class RecoverFail : Exception { public RecoverFail(string m) : base(m) { } }
        static RecoverFail Fail(string m) { return new RecoverFail(m); }

        // ------------------------------------------------------------------
        //  1 + 2: recognise the program and its runtime
        // ------------------------------------------------------------------
        void Go(CodeAnalysis ca)
        {
            byte[] m = p.Mem;
            foreach (var e in ca.Entries)
            {
                int a = e.Address;
                if (a < PFile.Base || a + 10 > p.FileEnd) continue;
                if (m[a] == 0xED && m[a + 1] == 0x73 && m[a + 4] == 0xCD && m[a + 7] == 0xCD) { entry = a; break; }
            }
            if (entry == 0) return;     // not one of ours
            entrySp = m[entry + 2] | m[entry + 3] << 8;
            if (entrySp <= entry || entrySp >= p.FileEnd || m[entrySp - 3] != 0xC3) return;
            res.IsOurs = true;
            mainStart = entry + 10;
            mainEnd = entrySp - 3;            // the final JP rt_stop
            runtimeStart = entrySp + 2;
            rtStop = m[entrySp - 2] | m[entrySp - 1] << 8;

            // decode the compiled lines
            code = new List<Instr>();
            index = new Dictionary<int, int>();
            for (int a = mainStart; a <= mainEnd;)
            {
                var ins = Z80Disasm.Decode(m, a, null);
                if (m[a] == 0xEF)
                {
                    // RST 28h: the ROM calculator. The bytes after it are operation codes up
                    // to 34h (end-calc), not instructions: make them one "CALC" step.
                    var ops = new List<int>();
                    int q = a + 1;
                    while (q < mainEnd && m[q] != 0x34) ops.Add(m[q++]);
                    calcOps[a] = ops;
                    ins = new Instr { Addr = a, Len = q + 1 - a, Shape = "CALC", Text = "RST 28h (calculator)", Continues = true };
                }
                index[a] = code.Count;
                code.Add(ins);
                a += ins.Len;
            }

            // match the runtime chunks in name order (with backtracking)
            var tl = Templates();
            var found = new List<KeyValuePair<int, Template>>();
            if (!MatchChunks(m, runtimeStart, -1, tl, found)) throw Fail("the runtime library doesn't match this version of the compiler");
            SetRuntime(found);
            int init = rtAt.FirstOrDefault(x => x.Value == "rt_init").Key;
            if (init == 0) throw Fail("rt_init not found");
            bss = m[init + 1] | m[init + 2] << 8;
            bssSize = m[init + 4] | m[init + 5] << 8;
            res.BssStart = bss; res.BssSize = bssSize;
            res.Options.BreakCheck = rtAt.ContainsValue("rt_brk");
            // variables at 8000h and up: compiled for a 32K or 64K RAM pack
            res.Options.Ram = bss >= 0x8000 ? (bss + bssSize > 0xC000 ? 64 : 32) : 16;
            res.Options.Autorun = p.AutoRunLine != null;
            PreScan();
            Interpret();
            NameVariables();
            Output();
            Verify();
        }

        int runtimeEnd;
        int curAddr;   // address being interpreted (for error messages)
        const int PastEnd = 0x10000;   // AddrRef target meaning "a line number after the last line"

        void SetRuntime(List<KeyValuePair<int, Template>> found)
        {
            rtAt.Clear();
            foreach (var kv in found)
                foreach (var lo in kv.Value.LabelOffsets) rtAt[kv.Key + lo.Value] = lo.Key;
            runtimeEnd = found.Count == 0 ? runtimeStart : found[found.Count - 1].Key + found[found.Count - 1].Value.Bytes.Length;
        }

        /// <summary>Match chunk templates one after another from address a. A complete match
        /// is accepted only if every CALL/JP from the compiled lines into the runtime area
        /// lands on a known routine.</summary>
        bool MatchChunks(byte[] m, int a, int prevIdx, List<Template> tl, List<KeyValuePair<int, Template>> found)
        {
            for (int i = prevIdx + 1; i < tl.Count; i++)
            {
                if (!MatchAt(m, a, tl[i])) continue;
                found.Add(new KeyValuePair<int, Template>(a, tl[i]));
                if (MatchChunks(m, a + tl[i].Bytes.Length, i, tl, found)) return true;
                found.RemoveAt(found.Count - 1);
            }
            if (found.Count == 0) return false;
            SetRuntime(found);
            foreach (var x in code)
                if ((x.IsCall || x.IsJump) && x.Target >= runtimeStart && x.Target < runtimeEnd && !rtAt.ContainsKey(x.Target)) return false;
            foreach (var x in code)
                if ((x.IsCall || x.IsJump) && x.Target >= runtimeEnd && x.Target < p.FileEnd) return false;
            return true;
        }

        // ------------------------------------------------------------------
        //  helpers for reading the decoded instructions
        // ------------------------------------------------------------------
        Instr At(int i) { return i >= 0 && i < code.Count ? code[i] : new Instr { Shape = "END", Text = "END" }; }
        bool Is(int i, string shape) { return At(i).Shape == shape; }
        bool IsCallTo(int i, string rt) { var x = At(i); return x.Shape == "CALL nn" && RtName(x.Target) == rt; }
        string RtName(int a) { string s; return rtAt.TryGetValue(a, out s) ? s : null; }
        bool InBss(int a) { return a >= bss && a < bss + bssSize; }

        // ------------------------------------------------------------------
        //  3: pre-scan
        // ------------------------------------------------------------------
        void PreScan()
        {
            for (int i = 0; i < code.Count; i++)
            {
                var x = code[i];
                if (x.Shape == "LD HL,(nn)" || x.Shape == "LD DE,(nn)" || x.Shape == "LD (nn),HL")
                {
                    if (InBss(x.Value)) numVars.Add(x.Value);
                }
                if (x.Shape == "LD HL,nn" && InBss(x.Value))
                {
                    if (IsCallTo(i + 1, "rt_next") || IsCallTo(i + 1, "rt_forchk")) forBlocks.Add(x.Value);
                    else if (Is(i + 1, "LD (HL),n") && At(i + 1).Value == 0) temps.Add(x.Value);
                    else if (Is(i + 1, "LD BC,nn") && IsCallTo(i + 2, "rt_zero")) dims[x.Value] = At(i + 1).Value;
                    else if (Is(i + 1, "LD BC,nn")) strFixed.Add(x.Value);
                    else if (Is(i + 1, "LD C,(HL)")) strDyn.Add(x.Value);
                }
                if (x.Shape == "LD DE,nn" && InBss(x.Value))
                {
                    // a string array element is followed by LD BC,length; check that first, because
                    // its subscript may also end in ADD HL,HL (multiplying by a power of two)
                    if (Is(i + 1, "ADD HL,DE") && Is(i + 2, "LD BC,nn")) strArr2[x.Value] = At(i + 2).Value;
                    else if (Is(i - 1, "ADD HL,HL") && Is(i + 1, "ADD HL,DE")) numArrBase.Add(x.Value);
                    else if (Is(i + 1, "LD A,n") && IsCallTo(i + 2, "rt_strset")) { strDyn.Add(x.Value); res.Options.MaxString = At(i + 1).Value; }
                }
            }
            foreach (var t in temps) strDyn.Remove(t);
            // A DIM whose array is never used doesn't show whether it holds numbers or text.
            // The compiler puts number arrays before all string variables in memory, so an
            // unknown one is numeric unless a known string variable comes before it.
            var knownStr = new List<int>(strDyn); knownStr.AddRange(strFixed); knownStr.AddRange(strArr2.Keys);
            foreach (var d in dims.Keys)
                if (!numArrBase.Contains(d) && !strFixed.Contains(d) && !strArr2.ContainsKey(d) && !knownStr.Any(k => k < d)) numArrBase.Add(d);
            // string DIMs and string variables get provisional names now (final letters later, by address)
            foreach (var d in dims.Keys) if (!numArrBase.Contains(d)) StrName(d);
            foreach (var d in strDyn) StrName(d);
            foreach (var d in strFixed) StrName(d);
            // capacity of ordinary strings, from the spacing of two of them
            if (strDyn.Count >= 2) { var l = strDyn.ToList(); res.Options.MaxString = l[1] - l[0] - 1; }
            foreach (var f in forBlocks) numVars.Add(f);
            // FOR blocks also cover value+2..+6: those aren't separate variables
            foreach (var f in forBlocks) { numVars.Remove(f + 2); numVars.Remove(f + 4); numVars.Remove(f + 6); }
        }

        // ------------------------------------------------------------------
        //  4: symbolic interpretation
        // ------------------------------------------------------------------
        // Symbolic register contents
        abstract class SV { }
        class SN : SV { public Expr E; public SN(Expr e) { E = e; } }        // a number
        class SK : SV { public int V; public SK(int v) { V = v; } }          // a constant (number or address)
        class SR : SV { public Expr S; public SR(Expr s) { S = s; } }        // a string (HL = address, BC = length)
        class SArr : SV { public NumArr E; public SArr(NumArr e) { E = e; } } // address of a numeric array element
        class SCmp : SV { public Expr L, R; public bool Dec; }               // result of rt_strcmp (-1/0/1)
        class SInput : SV { }                                                // the text typed at INPUT
        class SKey : SV { }                                                  // A = INKEY$ character

        SV HL, DE, BC, A;
        readonly List<SV> stack = new List<SV>();
        // The ROM calculator stack, symbolically: each entry is a numeric expression, or a
        // STR$(...) string made by calculator code 2Ah. calcOps holds the codes of each
        // RST 28h sequence (by address).
        readonly List<Expr> fstack = new List<Expr>();
        readonly Dictionary<int, List<int>> calcOps = new Dictionary<int, List<int>>();
        Expr FPop()
        {
            if (fstack.Count == 0) throw Fail("calculator stack underflow");
            var v = fstack[fstack.Count - 1]; fstack.RemoveAt(fstack.Count - 1); return v;
        }

        /// <summary>Apply one RST 28h sequence to the symbolic calculator stack.</summary>
        void CalcStep(List<int> ops)
        {
            foreach (int c in ops)
            {
                string op = Runtime.CalcOps.FirstOrDefault(kv => kv.Value == c).Key;
                if (op == null) throw Fail("unknown calculator code " + Z80Disasm.Hex(c, 2));
                if (Array.IndexOf(FloatMath.PassOps, op) >= 0)
                {
                    Expr r = FPop(), l = FPop();
                    fstack.Add(new Binary(op, l, r, VT.Num));
                }
                else if (op == "NEG" || op == "NOT") fstack.Add(new Unary(op, FPop()));
                else if (op == "STR$") fstack.Add(new Func("STR$", FPop(), VT.Str));
                else if (op == "VAL") throw Fail("unexpected calculator VAL");
                else fstack.Add(new Func(op, FPop(), VT.Num));
            }
        }

        class Build { public int Addr; public List<Expr> Parts = new List<Expr>(); public List<int[]> Ands = new List<int[]>(); public List<Expr> AndConds = new List<Expr>(); }
        readonly List<Build> builds = new List<Build>();

        // statements produced: (start address, statement)
        readonly List<KeyValuePair<int, Stmt>> stmts = new List<KeyValuePair<int, Stmt>>();
        readonly List<KeyValuePair<Expr, int>> pendingIf = new List<KeyValuePair<Expr, int>>();
        PrintStmt print; int printStart;
        int lineStart;
        // FOR in progress
        int forVar = -1, forStage; Expr forFrom, forTo, forStep; int forStart, forBody;
        bool brkPending;

        Expr Num(SV v)
        {
            if (v is SN) return ((SN)v).E;
            if (v is SK) return new NumLit(((SK)v).V & 0xFFFF);
            throw Fail("expected a number");
        }
        Expr Str(SV v)
        {
            if (v is SR) return ((SR)v).S;
            throw Fail("expected a string");
        }
        void Push(SV v) { stack.Add(v); }
        SV Pop()
        {
            if (stack.Count == 0) throw Fail("stack underflow");
            var v = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); return v;
        }

        Expr VarRef(int a) { return new NumVar("@" + a); }

        // Rules for building statements:
        //  * lineStart = address where the code of the next statement begins
        //  * an open PRINT is closed (without a newline) by any other statement, an IF
        //    condition or a FOR - the next statement then starts where the PRINT ended
        //  * pending IF conditions wrap the next statement completed

        void Add(Stmt s, int start) { stmts.Add(new KeyValuePair<int, Stmt>(start, WrapIf(s))); }

        void Emit(Stmt s, int end)
        {
            FlushFor();
            ClosePrint();
            Add(s, lineStart);
            lineStart = end;
        }

        void AddIf(Expr cond, int target)
        {
            FlushFor();
            ClosePrint();
            pendingIf.Add(new KeyValuePair<Expr, int>(cond, target));
        }

        int printEnd;

        void PrintItem(PrintItem it, int end, bool printer = false)
        {
            FlushFor();
            if (print != null && print.Printer != printer) ClosePrint();   // PRINT then LPRINT
            if (print == null) { print = new PrintStmt { Printer = printer }; printStart = lineStart; }
            // a floating-point number is printed as its STR$: show just the number
            var fs = it.A as Func;
            if (it.Kind == PIKind.Expr && fs != null && fs.Name == "STR$" && FloatMath.IsFloat(fs.Arg)) it.A = fs.Arg;
            print.Items.Add(it);
            printEnd = end;
        }

        void PrintNewline(int end, bool printer = false)
        {
            FlushFor();
            if (print != null && print.Printer != printer) ClosePrint();
            if (print == null) { print = new PrintStmt { Printer = printer }; printStart = lineStart; }
            var ps = print; print = null;
            ps.Newline = true;
            Add(ps, printStart);
            lineStart = end;
        }

        void ClosePrint()
        {
            if (print == null) return;
            var ps = print; print = null;
            ps.Newline = false;
            Add(ps, printStart);
            lineStart = printEnd;
        }

        /// <summary>A store to a FOR variable that turned out not to start a FOR loop: it was a LET.</summary>
        void FlushFor()
        {
            if (forVar >= 0 && forStage == 1)
            {
                int v = forVar; forVar = -1;
                Add(new LetStmt { Target = VarRef(v), Value = forFrom }, forStart);
                lineStart = forStore;
            }
        }
        int forStore;

        Stmt WrapIf(Stmt s)
        {
            if (pendingIf.Count == 0) return s;
            Expr cond = pendingIf[0].Key;
            for (int i = 1; i < pendingIf.Count; i++) cond = new Binary("AND", cond, pendingIf[i].Key, VT.Num);
            pendingIf.Clear();
            return new IfStmt { Cond = cond, Then = s };
        }

        void Interpret()
        {
            lineStart = mainStart;
            int i = 0;
            while (i < code.Count)
            {
                var x = code[i];
                int addr = x.Addr;
                // string AND inside a build ends at its skip label
                foreach (var b in builds)
                    for (int k = b.Ands.Count - 1; k >= 0; k--)
                        if (b.Ands[k][1] == addr)
                        {
                            int from = b.Ands[k][0];
                            Expr cat = Concat(b.Parts.GetRange(from, b.Parts.Count - from));
                            b.Parts.RemoveRange(from, b.Parts.Count - from);
                            b.Parts.Add(new Binary("AND", cat, b.AndConds[k], VT.Str));
                            b.Ands.RemoveAt(k); b.AndConds.RemoveAt(k);
                        }
                if (addr == mainEnd) break;     // the final JP rt_stop
                curAddr = addr;
                i = Step(i);
            }
            FlushFor();
            ClosePrint();
            if (pendingIf.Count > 0) throw Fail("IF without a statement at the end");
        }

        static Expr Concat(List<Expr> parts)
        {
            if (parts.Count == 0) return new StrLit(new byte[0]);
            Expr e = parts[0];
            for (int k = 1; k < parts.Count; k++) e = new Binary("CAT", e, parts[k], VT.Str);
            return e;
        }

        Build FindBuild(SV de)
        {
            var k = de as SK;
            if (k == null) throw Fail("string build buffer expected");
            for (int j = builds.Count - 1; j >= 0; j--) if (builds[j].Addr == k.V) return builds[j];
            throw Fail("unknown string buffer");
        }

        /// <summary>Interpret the instruction at position i; returns the next position.</summary>
        int Step(int i)
        {
            var x = code[i];
            string sh = x.Shape;
            int next = i + 1;
            Func<int, int> endOf = k => At(k).Addr + At(k).Len;

            switch (sh)
            {
                case "LD HL,nn":
                    {
                        int v = x.Value;
                        if (temps.Contains(v) && Is(i + 1, "LD (HL),n") && At(i + 1).Value == 0)
                        {
                            builds.Add(new Build { Addr = v });
                            return i + 2;
                        }
                        if (Is(i + 1, "LD C,(HL)") && Is(i + 2, "LD B,n") && Is(i + 3, "INC HL"))
                        {
                            if (builds.Count > 0 && builds[builds.Count - 1].Addr == v)
                            {
                                var b = builds[builds.Count - 1]; builds.RemoveAt(builds.Count - 1);
                                HL = BC = new SR(Concat(b.Parts));
                            }
                            else HL = BC = new SR(new StrVar(StrName(v), new List<SliceArg>()));
                            return i + 4;
                        }
                        if (Is(i + 1, "LD BC,nn") && IsCallTo(i + 2, "rt_zero"))
                        {
                            int size = At(i + 1).Value;
                            Emit(new DimStmt { Name = '?', Dims = new List<int> { v, size } }, endOf(i + 2));
                            return i + 3;
                        }
                        if (Is(i + 1, "LD BC,nn"))
                        {
                            int n = At(i + 1).Value;
                            if (InBss(v)) HL = BC = new SR(new StrVar(StrName(v), new List<SliceArg>()));
                            else
                            {
                                var bytes = new byte[n];
                                Array.Copy(p.Mem, v, bytes, 0, n);
                                HL = BC = new SR(new StrLit(bytes));
                            }
                            return i + 2;
                        }
                        HL = new SK(v);
                        return next;
                    }
                case "LD HL,(nn)": HL = new SN(VarRef(x.Value)); return next;
                case "LD DE,nn": DE = new SK(x.Value); return next;
                case "LD DE,(nn)": DE = new SN(VarRef(x.Value)); return next;
                case "LD BC,nn": BC = new SK(x.Value); return next;
                case "LD A,n": A = new SK(x.Value); return next;
                case "LD C,n": BC = new SK(x.Value); return next;
                case "LD A,L": A = HL; return next;
                case "LD A,C": A = BC; return next;
                case "EX DE,HL":
                    {
                        // EX DE,HL / CALL rt_scmp / JP C or NC  =  IF a>b / IF a<=b
                        if (IsCallTo(i + 1, "rt_scmp") && (Is(i + 2, "JP C,nn") || Is(i + 2, "JP NC,nn")))
                        {
                            bool nc = Is(i + 2, "JP NC,nn");
                            AddIf(new Binary(nc ? ">" : "<=", Num(HL), Num(DE), VT.Num), At(i + 2).Target);
                            return i + 3;
                        }
                        // EX DE,HL / LD C,n / CALL rt_plot
                        if (Is(i + 1, "LD C,n") && IsCallTo(i + 2, "rt_plot"))
                        {
                            Emit(new PlotStmt { X = Num(HL), Y = Num(DE), Unplot = At(i + 1).Value == 1 }, endOf(i + 2));
                            return i + 3;
                        }
                        var t = HL; HL = DE; DE = t;
                        return next;
                    }
                case "EX (SP),HL":
                    {
                        var t = Pop(); Push(HL); HL = t;
                        return next;
                    }
                case "PUSH HL": Push(HL); return next;
                case "PUSH BC": Push(BC); return next;
                case "POP HL": HL = Pop(); return next;
                case "POP DE": DE = Pop(); return next;
                case "POP BC": BC = Pop(); return next;
                case "INC HL": HL = new SN(new Binary("+", Num(HL), new NumLit(1), VT.Num)); return next;
                case "DEC HL":
                    if (HL is SCmp) { ((SCmp)HL).Dec = true; return next; }
                    HL = new SN(new Binary("-", Num(HL), new NumLit(1), VT.Num)); return next;
                case "ADD HL,HL": HL = new SN(new Binary("*", Num(HL), new NumLit(2), VT.Num)); return next;
                case "ADD HL,DE":
                    {
                        var k = DE as SK;
                        if (k != null && numArrBase.Contains(k.V)) { HL = new SArr(ArrayElement(k.V, Num(HL))); return next; }
                        if (k != null && strArr2.ContainsKey(k.V) && Is(i + 1, "LD BC,nn"))
                        {
                            Expr idx = PlusOne(StripMul(Num(HL), strArr2[k.V]));
                            HL = BC = new SR(new StrVar(StrName(k.V), new List<SliceArg> { new SliceArg { From = idx, To = idx } }));
                            return i + 2;
                        }
                        HL = new SN(new Binary("+", Num(HL), Num(DE), VT.Num));
                        return next;
                    }
                case "OR A":
                    if (Is(i + 1, "SBC HL,DE"))
                    {
                        if (Is(i + 2, "JP NZ,nn") || Is(i + 2, "JP Z,nn"))
                        {
                            bool nz = Is(i + 2, "JP NZ,nn");
                            AddIf(new Binary(nz ? "=" : "<>", Num(HL), Num(DE), VT.Num), At(i + 2).Target);
                            return i + 3;
                        }
                        HL = new SN(new Binary("-", Num(HL), Num(DE), VT.Num));
                        return i + 2;
                    }
                    throw Fail("unexpected OR A at " + Z80Disasm.Hex(x.Addr, 4));
                case "LD A,H":
                    if (Is(i + 1, "OR L") && (Is(i + 2, "JP Z,nn") || Is(i + 2, "JP NZ,nn")))
                    {
                        bool z = Is(i + 2, "JP Z,nn");
                        int target = At(i + 2).Target;
                        if (HL is SCmp)
                        {
                            var c = (SCmp)HL;
                            string op = c.Dec ? (z ? "S<=" : "S>") : (z ? "S<>" : "S=");
                            AddIf(new Binary(op, c.L, c.R, VT.Num), target);
                            return i + 3;
                        }
                        // floating-point truth test: NOT x, then jump if that is not zero
                        var notf = HL is SN ? ((SN)HL).E as Unary : null;
                        bool floatTest = !z && notf != null && notf.Op == "NOT" && FloatMath.IsFloat(notf.A);
                        if (floatTest && builds.Count > 0)
                        {
                            var b = builds[builds.Count - 1];      // A$ AND (floating-point condition)
                            b.Ands.Add(new[] { b.Parts.Count, target });
                            b.AndConds.Add(notf.A);
                            return i + 3;
                        }
                        if (floatTest) { AddIf(notf.A, target); return i + 3; }
                        if (z && builds.Count > 0)
                        {
                            // A$ AND n inside a string being built
                            var b = builds[builds.Count - 1];
                            b.Ands.Add(new[] { b.Parts.Count, target });
                            b.AndConds.Add(Num(HL));
                            return i + 3;
                        }
                        // IF x THEN (jump if zero) / IF x=0 THEN (jump if not zero)
                        AddIf(z ? Num(HL) : new Binary("=", Num(HL), new NumLit(0), VT.Num), target);
                        return i + 3;
                    }
                    throw Fail("unexpected LD A,H at " + Z80Disasm.Hex(x.Addr, 4));
                case "BIT 7,H":
                    if (HL is SCmp && (Is(i + 1, "JP Z,nn") || Is(i + 1, "JP NZ,nn")))
                    {
                        var c = (SCmp)HL;
                        AddIf(new Binary(Is(i + 1, "JP Z,nn") ? "S<" : "S>=", c.L, c.R, VT.Num), At(i + 1).Target);
                        return i + 2;
                    }
                    throw Fail("unexpected BIT 7,H");
                case "LD A,(HL)":
                    if (Is(i + 1, "INC HL") && Is(i + 2, "LD H,(HL)") && Is(i + 3, "LD L,A"))
                    {
                        var arr = HL as SArr;
                        if (arr == null) throw Fail("array read expected");
                        HL = new SN(arr.E);
                        return i + 4;
                    }
                    throw Fail("unexpected LD A,(HL)");
                case "LD L,(HL)":
                    if (Is(i + 1, "LD H,n")) { HL = new SN(new Func("PEEK", Unsigned(Num(HL)), VT.Num)); return i + 2; }
                    throw Fail("unexpected LD L,(HL)");
                case "LD L,C":
                    if (Is(i + 1, "LD H,n")) { HL = new SN(new Func("LEN", Str(HL), VT.Num)); return i + 2; }
                    throw Fail("unexpected LD L,C");
                case "LD (HL),E":
                    if (Is(i + 1, "INC HL") && Is(i + 2, "LD (HL),D"))
                    {
                        var arr = HL as SArr;
                        if (arr == null) throw Fail("array store expected");
                        if (DE is SInput) Emit(new InputStmt { Target = arr.E }, endOf(i + 2));
                        else Emit(new LetStmt { Target = arr.E, Value = Num(DE) }, endOf(i + 2));
                        return i + 3;
                    }
                    Emit(new PokeStmt { Addr = Unsigned(Num(HL)), Value = Num(DE) }, endOf(i));
                    return next;
                case "LD (nn),HL":
                    {
                        int v = x.Value;
                        if (forVar >= 0 && forStage == 1 && v == forVar + 2) { forTo = Num(HL); forStage = 2; return next; }
                        if (forVar >= 0 && forStage == 2 && v == forVar + 4) { forStep = Num(HL); forStage = 3; return next; }
                        if (forVar >= 0 && forStage == 3 && v == forVar + 6) { forBody = ((SK)HL).V; forStage = 4; return next; }
                        if (forVar >= 0 && forStage == 4) throw Fail("FOR pattern broken");
                        if (HL is SInput) { Emit(new InputStmt { Target = VarRef(v) }, x.Addr + x.Len); return next; }
                        if (forBlocks.Contains(v))
                        {
                            FlushFor();
                            ClosePrint();
                            forVar = v; forStage = 1; forFrom = Num(HL); forStart = lineStart; forStore = x.Addr + x.Len;
                            return next;
                        }
                        Emit(new LetStmt { Target = VarRef(v), Value = Num(HL) }, x.Addr + x.Len);
                        return next;
                    }
                case "LD SP,(nn)":
                    if (IsCallTo(i + 1, "rt_init") && Is(i + 2, "JP nn"))
                    {
                        int t = At(i + 2).Target;
                        Emit(new RunStmt { N = t == mainStart ? null : (Expr)new AddrRef(t) }, endOf(i + 2));
                        return i + 3;
                    }
                    throw Fail("unexpected LD SP");
                case "RET":
                    Emit(new ReturnStmt(), x.Addr + 1);
                    return next;
                case "JP nn":
                    {
                        string rt = RtName(x.Target);
                        if (rt == "rt_stop")
                        {
                            // after a BREAK check this was GOTO to a line past the end; otherwise STOP
                            if (brkPending) Emit(new GotoStmt { Target = new AddrRef(PastEnd) }, x.Addr + 3);
                            else Emit(new SimpleStmt("STOP"), x.Addr + 3);
                            brkPending = false;
                            return next;
                        }
                        if (rt == "rt_goto") { brkPending = false; Emit(new GotoStmt { Target = Num(HL) }, x.Addr + 3); return next; }
                        if (x.Target == Runtime.ROM_NEW) { Emit(new SimpleStmt("NEW"), x.Addr + 3); return next; }
                        brkPending = false;
                        Emit(new GotoStmt { Target = new AddrRef(x.Target) }, x.Addr + 3);
                        return next;
                    }
                case "JP Z,nn":
                case "JP NZ,nn":
                case "JP C,nn":
                case "JP NC,nn":
                    throw Fail("unexpected conditional jump at " + Z80Disasm.Hex(x.Addr, 4));
                case "CALL C,nn":
                    throw Fail("unexpected CALL C");
                case "CALL nn":
                    return Call(i);
                case "CALC":
                    CalcStep(calcOps[x.Addr]);
                    return next;
                case "LD B,n":
                    if (Is(i + 1, "CALC")) return next;     // comparisons give their code in B too
                    throw Fail("unexpected LD B,n");
            }
            throw Fail("unrecognised code at " + Z80Disasm.Hex(x.Addr, 4) + ": " + x.Text);
        }


        int Call(int i)
        {
            var x = code[i];
            int end = x.Addr + 3;
            string rt = RtName(x.Target);
            if (rt == null)
            {
                if (x.Target == Runtime.ROM_FAST) { Emit(new SimpleStmt("FAST"), end); return i + 1; }
                if (x.Target == Runtime.ROM_SLOW) { Emit(new SimpleStmt("SLOW"), end); return i + 1; }
                if (x.Target == Runtime.ROM_COPY) { Emit(new SimpleStmt("COPY"), end); return i + 1; }
                if (x.Target == Runtime.ROM_STACK_BC)
                {
                    // LD BC,n / CALL STACK-BC: a number 32768..65535 in floating-point code
                    var k = BC as SK;
                    if (k == null) throw Fail("STACK-BC without a constant");
                    fstack.Add(new NumLitU(k.V) { Unsigned = true });
                    return i + 1;
                }
                if (x.Target >= mainStart && x.Target <= mainEnd) { Emit(new GotoStmt { Sub = true, Target = new AddrRef(x.Target) }, end); return i + 1; }
                throw Fail("CALL to unknown address " + Z80Disasm.Hex(x.Target, 4));
            }
            switch (rt)
            {
                // ---- numeric operators and functions ----
                case "neg_hl": HL = new SN(new Unary("NEG", Num(HL))); break;
                case "rt_not": HL = new SN(new Unary("NOT", Num(HL))); break;
                case "rt_abs": HL = new SN(new Func("ABS", Num(HL), VT.Num)); break;
                case "rt_sgn": HL = new SN(new Func("SGN", Num(HL), VT.Num)); break;
                case "rt_sqr": HL = new SN(new Func("SQR", Num(HL), VT.Num)); break;
                case "rt_rndn": HL = new SN(new RndN(Num(HL))); break;
                case "rt_mul": HL = new SN(new Binary("*", Num(HL), Num(DE), VT.Num)); break;
                case "rt_div": HL = new SN(new Binary("/", Num(HL), Num(DE), VT.Num)); break;
                case "rt_pow": HL = new SN(new Binary("**", Num(HL), Num(DE), VT.Num)); break;
                case "rt_and": HL = new SN(new Binary("AND", Num(HL), Num(DE), VT.Num)); break;
                case "rt_or": HL = new SN(new Binary("OR", Num(HL), Num(DE), VT.Num)); break;
                case "rt_eq": case "rt_ne": case "rt_lt": case "rt_gt": case "rt_le": case "rt_ge":
                    {
                        string op = rt == "rt_eq" ? "=" : rt == "rt_ne" ? "<>" : rt == "rt_lt" ? "<" : rt == "rt_gt" ? ">" : rt == "rt_le" ? "<=" : ">=";
                        var c = HL as SCmp;
                        if (c != null) HL = new SN(new Binary("S" + op, c.L, c.R, VT.Num));
                        else HL = new SN(new Binary(op, Num(HL), Num(DE), VT.Num));
                        break;
                    }
                case "rt_scmp":
                    if (Is(i + 1, "JP NC,nn") || Is(i + 1, "JP C,nn"))
                    {
                        bool nc = Is(i + 1, "JP NC,nn");
                        AddIf(new Binary(nc ? "<" : ">=", Num(HL), Num(DE), VT.Num), At(i + 1).Target);
                        return i + 2;
                    }
                    throw Fail("unexpected rt_scmp");
                case "rt_usr":
                    if (Is(i + 1, "LD H,B") && Is(i + 2, "LD L,C")) { HL = new SN(new Func("USR", Unsigned(Num(HL)), VT.Num)); return i + 3; }
                    throw Fail("unexpected USR");
                case "rt_code": HL = new SN(new Func("CODE", Str(HL), VT.Num)); break;
                case "rt_val":
                    if (HL is SR && ((SR)HL).S == null) { HL = new SInput(); break; }
                    HL = new SN(new Func("VAL", Str(HL), VT.Num)); break;
                case "rt_strcmp":
                    HL = new SCmp { L = Str(DE), R = Str(HL) };
                    break;
                // ---- strings ----
                case "rt_numstr": HL = BC = new SR(new Func("STR$", Num(HL), VT.Str)); break;
                case "rt_append": FindBuild(DE).Parts.Add(Str(HL)); break;
                case "rt_appchr": FindBuild(DE).Parts.Add(new Func("CHR$", Num(A), VT.Str)); break;
                case "rt_inkey":
                    if (Is(i + 1, "LD DE,nn") && Is(i + 2, "CALL C,nn"))
                    {
                        FindBuild(new SK(At(i + 1).Value)).Parts.Add(new Inkey());
                        return i + 3;
                    }
                    throw Fail("unexpected INKEY$");
                case "rt_slice":
                    {
                        var region = Pop();
                        Expr from = Num(HL), to = Num(DE);
                        var arg = new SliceArg();
                        if (ReferenceEquals(HL, DE) || (HL is SN && DE is SN && ReferenceEquals(((SN)HL).E, ((SN)DE).E))) { arg.From = arg.To = from; arg.IsRange = false; }
                        else
                        {
                            arg.IsRange = true;
                            arg.From = from;
                            var tk = DE as SK;
                            arg.To = tk != null && tk.V == 0x7FFF ? null : to;
                        }
                        Expr b = Str(region);
                        var sv = b as StrVar;
                        if (sv != null && sv.Args.Count < 2)
                        {
                            var na = new List<SliceArg>(sv.Args); na.Add(arg);
                            if (sv.Args.Count == 0 || strArr2.ContainsKey(StrAddr(sv.Name)))
                            { HL = BC = new SR(new StrVar(sv.Name, na)); break; }
                        }
                        HL = BC = new SR(new SliceExpr(b, arg));
                        break;
                    }
                case "rt_input": HL = BC = new SR(null); break;
                case "rt_strset":
                    {
                        var k = (SK)DE;
                        var target = new StrVar(StrName(k.V), new List<SliceArg>());
                        if (((SR)HL).S == null) Emit(new InputStmt { Target = target }, end);
                        else Emit(new LetStmt { Target = target, Value = Str(HL) }, end);
                        break;
                    }
                case "rt_strput":
                    {
                        Expr dst = Str(DE);
                        var src = HL as SR;
                        if (src.S == null) Emit(new InputStmt { Target = dst }, end);
                        else Emit(new LetStmt { Target = dst, Value = src.S }, end);
                        break;
                    }
                // ---- PRINT ----
                case "rt_prnum": PrintItem(new PrintItem { Kind = PIKind.Expr, A = Num(HL) }, end); break;
                case "rt_prstr": PrintItem(new PrintItem { Kind = PIKind.Expr, A = Str(HL) }, end); break;
                case "rt_at": PrintItem(new PrintItem { Kind = PIKind.At, A = Num(HL), B = Num(DE) }, end); break;
                case "rt_tab": PrintItem(new PrintItem { Kind = PIKind.Tab, A = Num(HL) }, end); break;
                case "rt_comma": PrintItem(new PrintItem { Kind = PIKind.Comma }, end); break;
                case "rt_newline": PrintNewline(end); break;
                // ---- other statements ----
                case "rt_cls": Emit(new SimpleStmt("CLS"), end); break;
                case "rt_scroll": Emit(new SimpleStmt("SCROLL"), end); break;
                case "rt_clear": Emit(new SimpleStmt("CLEAR"), end); break;
                case "rt_pause": Emit(new PauseStmt { N = Unsigned(Num(HL)) }, end); break;
                case "rt_randomize": Emit(new RandStmt(), end); break;
                case "rt_rand": Emit(new RandStmt { N = Unsigned(Num(HL)) }, end); break;
                case "rt_brk": brkPending = true; break;
                // ---- floating point ----
                case "rt_finit": lineStart = end; break;        // start-up code
                case "rt_fpushi": fstack.Add(Num(HL)); break;
                case "rt_fplit":
                    {
                        var k = HL as SK;
                        if (k == null) throw Fail("floating-point constant expected");
                        var b = new byte[5];
                        Array.Copy(p.Mem, k.V, b, 0, 5);
                        fstack.Add(b.SequenceEqual(Runtime.PiBytes) ? (Expr)new PiConst() : new FloatLit(b));
                        break;
                    }
                case "rt_frnd": fstack.Add(new RndBare()); break;
                case "rt_fval": fstack.Add(new Func("VAL", Str(HL), VT.Num)); break;
                case "rt_fpop": HL = new SN(FPop()); break;
                case "rt_fstr":
                    {
                        var sf = FPop() as Func;
                        if (sf == null || sf.Name != "STR$") throw Fail("STR$ expected on the calculator stack");
                        HL = BC = new SR(sf);
                        break;
                    }
                case "rt_freset": break;
                // ---- printer ----
                case "rt_lprnum": PrintItem(new PrintItem { Kind = PIKind.Expr, A = Num(HL) }, end, true); break;
                case "rt_lprstr": PrintItem(new PrintItem { Kind = PIKind.Expr, A = Str(HL) }, end, true); break;
                case "rt_lat": PrintItem(new PrintItem { Kind = PIKind.At, A = Num(HL), B = Num(DE) }, end, true); break;
                case "rt_ltab": PrintItem(new PrintItem { Kind = PIKind.Tab, A = Num(HL) }, end, true); break;
                case "rt_lcomma": PrintItem(new PrintItem { Kind = PIKind.Comma }, end, true); break;
                case "rt_lnewline": PrintNewline(end, true); break;
                // ---- tape ----
                case "rt_save": Emit(new TapeStmt { Name = Str(HL) }, end); break;
                case "rt_load": Emit(new TapeStmt { Name = Str(HL), Load = true }, end); break;
                case "rt_goto":
                    Emit(new GotoStmt { Sub = true, Target = Num(HL) }, end); break;
                case "rt_next":
                    {
                        var k = HL as SK;
                        if (k == null) throw Fail("NEXT without a variable");
                        brkPending = false;
                        Emit(new NextStmt { Var = '?', Line = k.V }, end);
                        break;
                    }
                case "rt_forchk":
                    {
                        if (forVar < 0 || forStage != 4 || !Is(i + 1, "JP C,nn")) throw Fail("FOR pattern broken");
                        int v = forVar; forVar = -1;
                        var nl = forStep as NumLit;
                        var fs = new ForStmt { Var = '?', From = forFrom, To = forTo, Step = nl != null && nl.Value == 1 ? null : forStep, Line = v };
                        ClosePrint();
                        Add(fs, forStart);
                        lineStart = At(i + 1).Addr + 3;
                        return i + 2;
                    }
                default:
                    throw Fail("unexpected call to " + rt);
            }
            return i + 1;
        }

        static Expr Unsigned(Expr e)
        {
            var n = e as NumLit;
            return n != null && !(n is NumLitU) ? new NumLitU(n.Value) : e;
        }

        /// <summary>x - 1  ->  x;  constant k -> k+1;  else x + 1.</summary>
        static Expr PlusOne(Expr e)
        {
            var b = e as Binary;
            if (b != null && b.Op == "-" && b.R is NumLit && ((NumLit)b.R).Value == 1) return b.L;
            var n = e as NumLit;
            if (n != null) return new NumLit((n.Value + 1) & 0xFFFF);
            return new Binary("+", e, new NumLit(1), VT.Num);
        }

        /// <summary>Remove a multiplication by m (x*m, or x*2*2... for powers of two).</summary>
        static Expr StripMul(Expr e, int m)
        {
            if (m == 1) return e;
            var b = e as Binary;
            if (b != null && b.Op == "*" && b.R is NumLit && ((NumLit)b.R).Value == m) return b.L;
            int prod = 1; Expr cur = e;
            while (prod < m)
            {
                var c = cur as Binary;
                if (c == null || c.Op != "*" || !(c.R is NumLit) || ((NumLit)c.R).Value != 2) throw Fail("array subscript not recognised");
                prod *= 2; cur = c.L;
            }
            if (prod != m) throw Fail("array subscript not recognised");
            return cur;
        }

        /// <summary>Numeric array element from its offset expression 2*(...).
        /// 1D:  2*(i-1)                       2D:  2*((j-1) + (i-1)*cols)
        /// (in 2D the column part is worked out last, so it is the left operand of the +)</summary>
        NumArr ArrayElement(int baseAddr, Expr offset)
        {
            var b = offset as Binary;
            if (b == null || b.Op != "*" || !(b.R is NumLit) || ((NumLit)b.R).Value != 2) throw Fail("array address not recognised");
            Expr inner = b.L;
            var add = inner as Binary;
            if (add != null && add.Op == "+")
            {
                var jm = add.L as Binary;
                if (jm == null || jm.Op != "-" || !(jm.R is NumLit) || ((NumLit)jm.R).Value != 1) throw Fail("2D array subscript not recognised");
                Expr ipart = add.R; int cols = 1;
                var mul = ipart as Binary;
                if (mul != null && mul.Op == "*" && mul.R is NumLit && ((NumLit)mul.R).Value != 2) { cols = ((NumLit)mul.R).Value; ipart = mul.L; }
                else
                    while (ipart is Binary && ((Binary)ipart).Op == "*" && ((Binary)ipart).R is NumLit && ((NumLit)((Binary)ipart).R).Value == 2) { cols *= 2; ipart = ((Binary)ipart).L; }
                var im = ipart as Binary;
                if (im == null || im.Op != "-" || !(im.R is NumLit) || ((NumLit)im.R).Value != 1) throw Fail("2D array subscript not recognised");
                arr2Cols[baseAddr] = cols;
                return new NumArr('?', new List<Expr> { im.L, jm.L }) { Line = baseAddr };
            }
            return new NumArr('?', new List<Expr> { PlusOne(inner) }) { Line = baseAddr };
        }

        // ------------------------------------------------------------------
        //  5: names and line numbers
        // ------------------------------------------------------------------
        readonly Dictionary<int, char> strLetters = new Dictionary<int, char>();
        readonly Dictionary<char, int> strLetterAddr = new Dictionary<char, int>();
        char StrName(int addr)
        {
            char c;
            if (!strLetters.TryGetValue(addr, out c))
            {
                // provisional: real letters are given in address order later
                c = (char)(0x100 + strLetters.Count);
                strLetters[addr] = c;
                strLetterAddr[c] = addr;
            }
            return c;
        }
        int StrAddr(char c) { int a; return strLetterAddr.TryGetValue(c, out a) ? a : -1; }

        readonly Dictionary<char, char> strFinal = new Dictionary<char, char>();
        readonly Dictionary<int, string> numNames = new Dictionary<int, string>();
        readonly Dictionary<int, char> arrNames = new Dictionary<int, char>();

        void NameVariables()
        {
            // numeric variables: FOR variables need single letters, so they choose first
            var letters = new Queue<char>("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
            var order = numVars.ToList();
            foreach (var a in order.Where(a => forBlocks.Contains(a))) { if (letters.Count == 0) throw Fail("too many FOR variables"); numNames[a] = letters.Dequeue().ToString(); }
            int two = 0;
            foreach (var a in order.Where(a => !forBlocks.Contains(a)))
                numNames[a] = letters.Count > 0 ? letters.Dequeue().ToString() : "V" + (++two);
            // arrays (a separate set of names)
            char an = 'A';
            foreach (var a in dims.Keys.Where(k => numArrBase.Contains(k))) arrNames[a] = an++;
            // strings: ordinary ones then DIM'd ones, in address order
            char sn = 'A';
            foreach (var a in strLetters.Keys.OrderBy(k => k)) strFinal[strLetters[a]] = sn++;
        }


        string NumName(int a)
        {
            string s;
            if (numNames.TryGetValue(a, out s)) return s;
            if (numNames.TryGetValue(a - 2, out s)) return s;
            throw Fail("unknown variable at " + Z80Disasm.Hex(a, 4));
        }

        /// <summary>Replace the provisional names (@address, '?') throughout an expression.</summary>
        Expr Fix(Expr e)
        {
            if (e == null) return null;
            var nv = e as NumVar;
            if (nv != null && nv.Name.StartsWith("@")) return new NumVar(NumName(int.Parse(nv.Name.Substring(1))));
            var na = e as NumArr;
            if (na != null) return new NumArr(arrNames[na.Line], na.Index.Select(Fix).ToList());
            var sv = e as StrVar;
            if (sv != null) return new StrVar(strFinal.ContainsKey(sv.Name) ? strFinal[sv.Name] : sv.Name, sv.Args.Select(FixSlice).ToList());
            var se = e as SliceExpr;
            if (se != null) return new SliceExpr(Fix(se.Base), FixSlice(se.Arg));
            var b = e as Binary;
            if (b != null) { var fb = FoldIncs(b); return new Binary(fb.Op, Fix(fb.L), Fix(fb.R), fb.Type); }
            var u = e as Unary;
            if (u != null) return new Unary(u.Op, Fix(u.A));
            var f = e as Func;
            if (f != null) return new Func(f.Name, Fix(f.Arg), f.Type);
            var r = e as RndN;
            if (r != null) return new RndN(Fix(r.N));
            return e;
        }

        /// <summary>x+1+1+1+1 (from repeated INC HL) -> x+3+1: groups of up to 3, which the
        /// compiler turns back into the same INC/DEC instructions.</summary>
        static Binary FoldIncs(Binary b)
        {
            if ((b.Op != "+" && b.Op != "-") || !(b.R is NumLit) || ((NumLit)b.R).Value != 1) return b;
            int n = 0; Expr cur = b;
            while (cur is Binary && ((Binary)cur).Op == b.Op && ((Binary)cur).R is NumLit && ((NumLit)((Binary)cur).R).Value == 1 && n < 3) { n++; cur = ((Binary)cur).L; }
            return new Binary(b.Op, cur, new NumLit(n), VT.Num);
        }

        SliceArg FixSlice(SliceArg a) { return new SliceArg { From = Fix(a.From), To = Fix(a.To), IsRange = a.IsRange }; }

        Stmt FixStmt(Stmt s)
        {
            if (s is LetStmt) { var l = (LetStmt)s; return new LetStmt { Target = Fix(l.Target), Value = Fix(l.Value) }; }
            if (s is IfStmt) { var f = (IfStmt)s; return new IfStmt { Cond = Fix(f.Cond), Then = FixStmt(f.Then) }; }
            if (s is PrintStmt)
            {
                var p0 = (PrintStmt)s; var n = new PrintStmt { Newline = p0.Newline, Printer = p0.Printer };
                foreach (var it in p0.Items) n.Items.Add(new PrintItem { Kind = it.Kind, A = Fix(it.A), B = Fix(it.B) });
                return n;
            }
            if (s is GotoStmt) { var g = (GotoStmt)s; return new GotoStmt { Sub = g.Sub, Target = Fix(g.Target) }; }
            if (s is TapeStmt) { var t = (TapeStmt)s; return new TapeStmt { Name = Fix(t.Name), Load = t.Load }; }
            if (s is ForStmt) { var f = (ForStmt)s; return new ForStmt { Var = NumName(f.Line)[0], From = Fix(f.From), To = Fix(f.To), Step = Fix(f.Step) }; }
            if (s is NextStmt) return new NextStmt { Var = NumName(((NextStmt)s).Line)[0] };
            if (s is DimStmt)
            {
                var d = (DimStmt)s;
                int baseA = d.Dims[0], size = d.Dims[1];
                if (arrNames.ContainsKey(baseA))
                {
                    int cols; var dl = arr2Cols.TryGetValue(baseA, out cols) ? new List<int> { size / 2 / cols, cols } : new List<int> { size / 2 };
                    return new DimStmt { Name = arrNames[baseA], IsStr = false, Dims = dl };
                }
                char c = strFinal[StrName(baseA)];
                int ecol;
                var sd = strArr2.TryGetValue(baseA, out ecol) ? new List<int> { size / ecol, ecol } : new List<int> { size };
                return new DimStmt { Name = c, IsStr = true, Dims = sd };
            }
            if (s is InputStmt) return new InputStmt { Target = Fix(((InputStmt)s).Target) };
            if (s is PokeStmt) { var k = (PokeStmt)s; return new PokeStmt { Addr = Fix(k.Addr), Value = Fix(k.Value) }; }
            if (s is PlotStmt) { var k = (PlotStmt)s; return new PlotStmt { X = Fix(k.X), Y = Fix(k.Y), Unplot = k.Unplot }; }
            if (s is PauseStmt) return new PauseStmt { N = Fix(((PauseStmt)s).N) };
            if (s is RandStmt) return new RandStmt { N = Fix(((RandStmt)s).N) };
            if (s is RunStmt) return new RunStmt { N = Fix(((RunStmt)s).N) };
            return s;
        }

        void Output()
        {
            // line numbers: from the line table if the program has one, else 10, 20, 30...
            var tab = new Dictionary<int, int>();
            bool hasTable = false;
            int lt = rtAt.ContainsValue("rt_goto") ? FindLineTable() : -1;
            if (lt > 0)
            {
                hasTable = true;
                for (int a = lt; a + 3 < p.FileEnd; a += 4)
                {
                    int n = p.Mem[a] | p.Mem[a + 1] << 8;
                    if (n == 0xFFFF) break;
                    int ad = p.Mem[a + 2] | p.Mem[a + 3] << 8;
                    int old;
                    if (!tab.TryGetValue(ad, out old) || n > old) tab[ad] = n;
                }
            }
            var fixedStmts = stmts.Select(kv => new KeyValuePair<int, Stmt>(kv.Key, FixStmt(kv.Value))).ToList();
            var numbers = new List<int>();
            int nextNum = 10;
            foreach (var kv in fixedStmts)
            {
                int n;
                if (hasTable && tab.TryGetValue(kv.Key, out n)) { numbers.Add(n); nextNum = n + 1; }
                else { if (numbers.Count > 0 && nextNum <= numbers[numbers.Count - 1]) nextNum = numbers[numbers.Count - 1] + 1; numbers.Add(hasTable ? nextNum++ : nextNum); if (!hasTable) nextNum += 10; }
            }
            // targets that point at the end of the program (a final empty line)
            bool needEnd = false;
            int endNum = (numbers.Count > 0 ? numbers[numbers.Count - 1] : 0) + (hasTable ? 1 : 10);
            if (hasTable) { int n; if (tab.TryGetValue(mainEnd, out n)) endNum = n; }
            int lastNum = Math.Max(numbers.Count > 0 ? numbers.Max() : 0, hasTable ? TableMax(lt) : 0);
            BasicWriter.LineOf = a =>
            {
                if (a == PastEnd) return lastNum + 1;
                for (int k = 0; k < fixedStmts.Count; k++) if (fixedStmts[k].Key >= a) return numbers[k];
                needEnd = true;
                return endNum;
            };
            var sb = new StringBuilder();
            if (hasTable)
            {
                // the table lists every original line, including ones with no code (REM):
                // put those back as REM lines so the table (and the code) come out the same
                var used = new HashSet<int>(numbers);
                var all = new SortedSet<int>(numbers);
                for (int a = lt; a + 3 < p.FileEnd; a += 4)
                {
                    int n = p.Mem[a] | p.Mem[a + 1] << 8;
                    if (n == 0xFFFF) break;
                    all.Add(n);
                }
                var text = new Dictionary<int, string>();
                for (int k = 0; k < fixedStmts.Count; k++) text[numbers[k]] = BasicWriter.Stmt(fixedStmts[k].Value);
                foreach (int n in all) sb.Append(n).Append(' ').Append(text.ContainsKey(n) ? text[n] : "REM").Append("\r\n");
                needEnd = false;
            }
            else
                for (int k = 0; k < fixedStmts.Count; k++) sb.Append(numbers[k]).Append(' ').Append(BasicWriter.Stmt(fixedStmts[k].Value)).Append("\r\n");
            if (needEnd) sb.Append(endNum).Append(" REM end\r\n");
            res.Listing = sb.ToString();
            res.Success = true;
            BasicWriter.LineOf = a => a;
        }

        /// <summary>The line table (pairs of line number + address, ending FFFFh) comes after the
        /// string literals: find it by looking for a table whose addresses all point into
        /// the compiled lines.</summary>
        int TableMax(int lt)
        {
            int mx = 0;
            for (int a = lt; a + 3 < p.FileEnd; a += 4) { int n = p.Mem[a] | p.Mem[a + 1] << 8; if (n == 0xFFFF) break; mx = Math.Max(mx, n); }
            return mx;
        }

        int FindLineTable()
        {
            for (int a = runtimeEnd; a + 4 < p.FileEnd; a++)
            {
                int ok = 0, b = a;
                while (b + 3 < p.FileEnd)
                {
                    int n = p.Mem[b] | p.Mem[b + 1] << 8;
                    if (n == 0xFFFF) break;
                    int ad = p.Mem[b + 2] | p.Mem[b + 3] << 8;
                    if (n > 9999 || ad < mainStart || ad > mainEnd) { ok = -1; break; }
                    ok++; b += 4;
                }
                if (ok > 0 && b + 1 < p.FileEnd && (p.Mem[b] | p.Mem[b + 1] << 8) == 0xFFFF) return a;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        //  6: verify by recompiling
        // ------------------------------------------------------------------
        void Verify()
        {
            var cr = Compiler.Compile(res.Listing, new Options { Autorun = res.Options.Autorun, BreakCheck = res.Options.BreakCheck, MaxString = res.Options.MaxString, Ram = res.Options.Ram, MakeListing = false });
            if (!cr.Success)
            {
                res.Notes.Add("The rebuilt BASIC doesn't compile: " + string.Join("; ", cr.Diagnostics.Where(d => d.IsError).Select(d => d.ToString()).Take(3)));
                return;
            }
            var np = PFile.Load(cr.PFile);
            var ol = p.Lines.FirstOrDefault(l => l.BodyAddr + 1 == entry);
            var nl = np.Lines.FirstOrDefault();
            if (ol != null && nl != null && ol.Body.SequenceEqual(nl.Body))
            {
                res.Verified = true;
                res.Notes.Add("Verified: the rebuilt BASIC compiles to exactly the same machine code.");
            }
            else res.Notes.Add("The rebuilt BASIC compiles, but not to identical machine code - check it before relying on it.");
        }
    }
}
