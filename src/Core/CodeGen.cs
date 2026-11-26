// =====================================================================================
//  CodeGen.cs - the code generator: AST -> Z80 assembly text
// -------------------------------------------------------------------------------------
//  Walks the tree built by the parser and writes Z80 assembly lines into a Z80Asm
//  object. Most of the hard work (printing, string handling, multiplication ...) is
//  done by routines in the runtime library (Runtime.cs); the code generated here mostly
//  loads values and CALLs those routines.
//
//  REGISTER CONVENTIONS used throughout the generated code
//  -------------------------------------------------------
//    Numbers:  every numeric expression leaves its result in HL (16-bit signed).
//              For an operator, the left operand ends up in HL and the right in DE.
//    Strings:  a string value is a "region": HL = address of the first character,
//              BC = length. Lengths are always 0..255, so B is always 0.
//
//  STRING TEMPORARY BUFFERS AND "DEPTH"
//  ------------------------------------
//    A variable or a literal can be used where it is - its region just points at it.
//    Results that have to be built (A$+B$, CHR$ 65, STR$ X, INKEY$) are assembled in a
//    temporary buffer T_0, T_1, ... (each 256 bytes: a length byte then the text).
//    The parameter "f" in many methods is the first free buffer number ("depth").
//    Rule: while a region at depth f is still needed, anything evaluated meanwhile must
//    use depth f+1 or more, or it would overwrite it. Example: A$+B$ < C$+D$ builds
//    A$+B$ in T_0 and C$+D$ in T_1 before comparing them.
//
//  FLOATING POINT ("FLOAT ISLANDS", see FloatMath in Ast.cs)
//  -------------------------------------------------------
//    An expression containing SIN, PI, 0.5 ... is worked out on the ROM calculator's
//    stack instead of in HL (GenFloat): values are put there (rt_fpushi, rt_fplit,
//    STACK-BC), each operation is RST 28h + its code + 34h, and the result is either
//    rounded into HL (rt_fpop) or turned into text by the ROM's STR$ (rt_fstr). The
//    rest of the program stays in fast 16-bit whole numbers.
//
//  LABELS
//  ------
//    L_10 / E_10   start / end of BASIC line 10 (an IF that is false jumps to E_n)
//    V_X           numeric variable X (FOR variables use an 8-byte block, see GenFor)
//    A_X           numeric array X()          S_X   ordinary string variable X$
//    SA_X          DIM'd string X$            T_n   temporary string buffer n
//    LIT_n         a string literal           _kN   other generated labels
//    rt_xxx        runtime library routines   LINETAB  line number -> address table
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>The user's compile options (tick boxes in the GUI / command-line switches).</summary>
    public class Options
    {
        public bool Autorun = true;          // start the program automatically after LOAD
        public bool BreakCheck = true;       // SPACE (BREAK) stops the program at GOTO / NEXT
        public bool LowerIsInverse = false;  // lower case letters in strings become inverse video
        public int MaxString = 255;          // capacity of ordinary string variables
        public bool MakeListing = true;      // produce an assembly listing
    }

    public class CodeGen
    {
        readonly List<BasicLine> prog;   // the program, sorted by line number
        readonly Options opt;
        /// <summary>The assembler the generated code is written into.</summary>
        public readonly Z80Asm Asm = new Z80Asm();
        /// <summary>Errors and warnings found while generating code.</summary>
        public readonly List<Diagnostic> Diags = new List<Diagnostic>();

        // ---- what the program uses (filled in as code is generated) ----
        readonly HashSet<string> need = new HashSet<string>();                                  // runtime chunks to include
        readonly List<string> numVars = new List<string>();                                      // numeric variable names
        readonly HashSet<string> forVars = new HashSet<string>();                                // ...of which FOR loop variables
        readonly Dictionary<char, List<int>> numArr = new Dictionary<char, List<int>>();         // numeric arrays -> DIM sizes
        readonly Dictionary<char, List<int>> strArr = new Dictionary<char, List<int>>();         // DIM'd strings -> DIM sizes
        readonly List<char> strDyn = new List<char>();                                           // ordinary (un-DIM'd) string variables
        readonly Dictionary<string, string> literals = new Dictionary<string, string>();         // string literal contents -> label
        readonly List<KeyValuePair<string, byte[]>> litList = new List<KeyValuePair<string, byte[]>>();  // literals in order of use
        int maxDepth = -1;       // highest temporary string buffer number used (-1 = none)
        /// <summary>True if the program uses the ROM's calculator or workspace (floating
        /// point, VAL, SAVE, LOAD). Then rt_finit runs at the start, and Compiler.cs leaves
        /// extra room above E_LINE for the calculator stack before the variables.</summary>
        public bool UsesCalc;
        bool needLineTab;        // a calculated GOTO/GOSUB/RUN needs the line table
        int lblCount;            // counter for NewLabel()
        int curLine;             // BASIC line being compiled (for error messages)
        /// <summary>Total size in bytes of the variables area (worked out at the end of Generate).</summary>
        public int BssSize;

        public CodeGen(List<BasicLine> program, Options options)
        {
            prog = program;
            opt = options;
        }

        // ---- small helpers ----
        void Op(string s) { Asm.Op(s); }
        void Lbl(string s) { Asm.Label(s); }
        /// <summary>A new unique label name: _k0, _k1, ...</summary>
        string NewLabel() { return "_k" + (lblCount++); }
        CompileError Err(string m) { return new CompileError(curLine, m); }

        /// <summary>Emit "CALL rt" and make sure the runtime routine is included.</summary>
        void Call(string rt)
        {
            Use(rt);
            Op("CALL " + rt);
        }
        /// <summary>Mark a runtime routine as needed (some labels live inside another
        /// routine's chunk - Runtime.EntryChunk says which).</summary>
        void Use(string rt)
        {
            string c;
            need.Add(Runtime.EntryChunk.TryGetValue(rt, out c) ? c : rt);
        }

        // ------------------------------------------------------------------
        /// <summary>Generate the whole program. Layout of the output:
        ///   start code -> each BASIC line -> JP rt_stop -> ENTRYSP word ->
        ///   runtime routines -> string literals -> LINETAB -> variable addresses (EQUs)</summary>
        public void Generate()
        {
            // pre-pass: DIM statements. Array sizes must be known before any code that
            // uses the array is generated, even if the DIM comes later in the listing.
            foreach (var ln in prog)
            {
                curLine = ln.Number;
                // look at the statement, and the statement after THEN for IFs
                for (Stmt s = ln.Stmt; s != null; s = (s is IfStmt) ? ((IfStmt)s).Then : null)
                {
                    var d = s as DimStmt;
                    if (d == null) continue;
                    var table = d.IsStr ? strArr : numArr;
                    List<int> old;
                    if (table.TryGetValue(d.Name, out old))
                    {
                        if (!old.SequenceEqual(d.Dims))
                            Diags.Add(new Diagnostic { IsError = true, BasicLine = ln.Number, Message = "array " + d.Name + (d.IsStr ? "$" : "") + " is DIMensioned twice with different sizes" });
                    }
                    else table[d.Name] = d.Dims;
                    if (d.IsStr && d.Dims[d.Dims.Count - 1] > 255)
                        Diags.Add(new Diagnostic { IsError = true, BasicLine = ln.Number, Message = "string lengths over 255 are not supported" });
                }
            }

            // Program entry (address 16514): remember SP so STOP can get back to BASIC,
            // check there's enough memory, clear the variables and the screen.
            Asm.Comment("Compiled ZX81 BASIC program");
            Lbl("start");
            Op("LD (ENTRYSP),SP");
            Call("rt_memcheck");
            Call("rt_init");
            UsesCalc = prog.Any(ln => StmtUsesCalc(ln.Stmt));
            if (UsesCalc) Call("rt_finit");   // note where the ROM's calculator stack is
            Use("rt_stop");

            // Each BASIC line. An error in one line is recorded and compilation carries
            // on, so the user sees all the errors at once.
            for (int i = 0; i < prog.Count; i++)
            {
                var ln = prog[i];
                curLine = ln.Number;
                Asm.Comment("line " + ln.Number);
                Lbl("L_" + ln.Number);
                try
                {
                    NormStmt(ln.Stmt);
                    GenStmt(ln.Stmt, i);
                }
                catch (CompileError e)
                {
                    Diags.Add(new Diagnostic { IsError = true, BasicLine = ln.Number, SourceLine = ln.SourceLine, Message = e.Message });
                }
                Lbl("E_" + ln.Number);
            }
            // Running off the end of the program returns to BASIC.
            Op("JP rt_stop");
            Lbl("ENTRYSP");      // the saved stack pointer (2 bytes inside the code)
            Op("DW 0");

            // runtime library: only the chunks that were used
            foreach (var ch in Runtime.Resolve(need))
                foreach (var l in ch.Code.Split('\n'))
                    if (l.Trim().Length > 0) Asm.Line(l.TrimEnd('\r'));

            // string literals
            foreach (var kv in litList)
            {
                Lbl(kv.Key);
                if (kv.Value.Length > 0) Op("DB " + string.Join(",", kv.Value.Select(b => b.ToString())));
            }

            // Line table for calculated GOTO/GOSUB: pairs of (line number, address),
            // ending with FFFF.
            if (needLineTab)
            {
                Lbl("LINETAB");
                foreach (var ln in prog) Op("DW " + ln.Number + ",L_" + ln.Number);
                Op("DW 0FFFFh");
            }

            // variables (outside the program, in free memory after E_LINE)
            // They are not stored in the .p file; each gets an address BSS+offset.
            // BSS itself is defined by Compiler.cs once the code size is known.
            Asm.Comment("variables");
            foreach (var l in Runtime.BssEquates.Split('\n')) if (l.Trim().Length > 0) Asm.Line(l.TrimEnd('\r'));
            int off = Runtime.BSS_FIXED;   // the runtime's own state comes first
            foreach (var v in numVars)
            {
                Asm.Line("V_" + v + " EQU BSS+" + off);
                off += forVars.Contains(v) ? 8 : 2;    // FOR vars: value, limit, step, loop address
            }
            foreach (var kv in numArr)
            {
                Asm.Line("A_" + kv.Key + " EQU BSS+" + off);
                off += 2 * kv.Value.Aggregate(1, (x, y) => x * y);   // 2 bytes per element
            }
            foreach (var c in strDyn)
            {
                Asm.Line("S_" + c + " EQU BSS+" + off);
                off += 1 + opt.MaxString;                          // length byte + text
            }
            foreach (var kv in strArr)
            {
                Asm.Line("SA_" + kv.Key + " EQU BSS+" + off);
                off += kv.Value.Aggregate(1, (x, y) => x * y);     // 1 byte per character
            }
            for (int d = 0; d <= maxDepth; d++)
            {
                Asm.Line("T_" + d + " EQU BSS+" + off);
                off += 256;
            }
            off += 1;   // spare byte, so the area after VARSTART is never empty (rt_clear needs >= 1)
            BssSize = off;
            Asm.Line("BSS_SIZE EQU " + off);
            Asm.Line("BSS_END EQU BSS+" + off);
        }

        // ------------------------------------------------------------------
        /// <summary>Label to jump to for "GOTO n": line n, or the next line after it if
        /// there is no line n (as the ZX81 does), or rt_stop if n is past the end.</summary>
        string LineLabel(int n)
        {
            foreach (var ln in prog) if (ln.Number >= n) return "L_" + ln.Number;
            Use("rt_stop");
            return "rt_stop";
        }

        /// <summary>Record that a numeric variable exists (so it gets memory).</summary>
        void UseNumVar(string name)
        {
            if (!numVars.Contains(name)) numVars.Add(name);
        }

        /// <summary>Generate code for one statement. idx is the line's position in prog.</summary>
        void GenStmt(Stmt s, int idx)
        {
            if (s is PrintStmt) { GenPrint((PrintStmt)s); return; }
            if (s is TapeStmt)
            {
                // SAVE / LOAD "name": through the ROM's tape routines
                var t = (TapeStmt)s;
                GenStrRegion(t.Name, 0);
                Call(t.Load ? "rt_load" : "rt_save");
                return;
            }
            if (s is LetStmt) { var l = (LetStmt)s; GenAssign(l.Target, l.Value, false); return; }
            if (s is IfStmt)
            {
                // IF: jump to the end of the line when the condition is false
                var f = (IfStmt)s;
                GenJumpIfFalse(f.Cond, "E_" + prog[idx].Number);
                GenStmt(f.Then, idx);
                return;
            }
            if (s is GotoStmt)
            {
                var g = (GotoStmt)s;
                var lit = g.Target as NumLit;
                if (opt.BreakCheck && !g.Sub) Call("rt_brk");
                if (lit != null)
                {
                    // constant line number: a direct JP / CALL
                    string target = LineLabel(lit.Value);
                    Op((g.Sub ? "CALL " : "JP ") + target);
                }
                else
                {
                    // calculated line number: look it up in LINETAB at run time
                    GenNum(g.Target, 0);
                    needLineTab = true;
                    Use("rt_goto");
                    Op((g.Sub ? "CALL" : "JP") + " rt_goto");
                }
                return;
            }
            // GOSUB is a CALL, so RETURN is just RET
            if (s is ReturnStmt) { Op("RET"); return; }
            if (s is ForStmt) { GenFor((ForStmt)s, idx); return; }
            if (s is NextStmt)
            {
                // rt_next adds the step, tests the limit and jumps back if the loop continues
                var n = (NextStmt)s;
                string v = n.Var.ToString();
                UseNumVar(v); forVars.Add(v);
                if (opt.BreakCheck) Call("rt_brk");
                Op("LD HL,V_" + v);
                Call("rt_next");
                return;
            }
            if (s is DimStmt)
            {
                // the memory is reserved at compile time; DIM just clears it
                var d = (DimStmt)s;
                int size = d.Dims.Aggregate(1, (x, y) => x * y) * (d.IsStr ? 1 : 2);
                Op("LD HL," + (d.IsStr ? "SA_" : "A_") + d.Name);
                Op("LD BC," + size);
                Call("rt_zero");
                return;
            }
            if (s is InputStmt) { GenInput(((InputStmt)s).Target); return; }
            if (s is PokeStmt)
            {
                var p = (PokeStmt)s;
                GenOperands(p.Addr, p.Value, 0);   // HL = address, DE = value
                Op("LD (HL),E");
                return;
            }
            if (s is PlotStmt)
            {
                var p = (PlotStmt)s;
                GenOperands(p.X, p.Y, 0);
                Op("EX DE,HL");                    // rt_plot wants DE = x, HL = y
                Op("LD C," + (p.Unplot ? 1 : 0));
                Call("rt_plot");
                return;
            }
            if (s is PauseStmt)
            {
                GenNum(((PauseStmt)s).N, 0);
                Call("rt_pause");
                return;
            }
            if (s is RandStmt)
            {
                var r = (RandStmt)s;
                if (r.N == null) Call("rt_randomize");
                else { GenNum(r.N, 0); Call("rt_rand"); }
                return;
            }
            if (s is RunStmt)
            {
                // RUN: throw away any GOSUB returns, clear everything, jump to the line
                var r = (RunStmt)s;
                Op("LD SP,(ENTRYSP)");
                Call("rt_init");
                if (r.N == null) Op("JP " + (prog.Count > 0 ? "L_" + prog[0].Number : "rt_stop"));
                else if (r.N is NumLit) Op("JP " + LineLabel(((NumLit)r.N).Value));
                else { GenNum(r.N, 0); needLineTab = true; Use("rt_goto"); Op("JP rt_goto"); }
                return;
            }
            var sm = s as SimpleStmt;
            if (sm != null)
            {
                switch (sm.Kind)
                {
                    case "CLS": Call("rt_cls"); return;
                    case "SCROLL": Call("rt_scroll"); return;
                    case "STOP": Use("rt_stop"); Op("JP rt_stop"); return;
                    case "CLEAR": Call("rt_clear"); return;
                    case "FAST": Op("CALL " + Runtime.ROM_FAST); return;   // ROM routines
                    case "SLOW": Op("CALL " + Runtime.ROM_SLOW); return;
                    case "COPY": Op("CALL " + Runtime.ROM_COPY); return;   // screen to the ZX printer
                    case "NEW": Op("JP " + Runtime.ROM_NEW); return;       // wipe everything, as on the ZX81
                    case "NOP": return;
                }
            }
            throw Err("internal: unhandled statement");
        }

        /// <summary>FOR v = a TO b STEP s.
        /// Each FOR variable has an 8-byte block at V_v:
        ///   V_v+0 value, V_v+2 limit, V_v+4 step, V_v+6 address of the loop body.
        /// Storing the body address lets NEXT jump back without knowing where the FOR was.
        /// As on the ZX81, if the loop wouldn't run at all (e.g. FOR I=5 TO 1) it jumps
        /// straight past the matching NEXT.</summary>
        void GenFor(ForStmt f, int idx)
        {
            string v = f.Var.ToString();
            UseNumVar(v); forVars.Add(v);
            WarnIfRounded(f.From, "FOR variable " + v);
            WarnIfRounded(f.To, "FOR limit (TO)");
            WarnIfRounded(f.Step, "FOR STEP");
            GenNum(f.From, 0);
            Op("LD (V_" + v + "),HL");
            GenNum(f.To, 0);
            Op("LD (V_" + v + "+2),HL");
            if (f.Step == null) Op("LD HL,1"); else GenNum(f.Step, 0);
            Op("LD (V_" + v + "+4),HL");
            string body = NewLabel();
            Op("LD HL," + body);
            Op("LD (V_" + v + "+6),HL");
            // find the matching NEXT: the first "NEXT v" after this line (also inside an IF),
            // which is how the ZX81 ROM finds it
            string after = null;
            for (int j = idx + 1; j < prog.Count && after == null; j++)
            {
                for (Stmt s = prog[j].Stmt; s != null; s = (s is IfStmt) ? ((IfStmt)s).Then : null)
                {
                    var n = s as NextStmt;
                    if (n != null && n.Var == f.Var) { after = "E_" + prog[j].Number; break; }
                }
            }
            if (after == null)
            {
                Diags.Add(new Diagnostic { BasicLine = curLine, Message = "FOR " + v + " has no matching NEXT " + v });
                Use("rt_stop");
                after = "rt_stop";
            }
            // skip the loop if it shouldn't run at all
            Op("LD HL,V_" + v);
            Call("rt_forchk");
            Op("JP C," + after);
            Lbl(body);
        }

        // ---------------- PRINT ----------------
        /// <summary>PRINT (or LPRINT): one runtime call per item, then a newline unless the
        /// statement ended with ; or ,  LPRINT uses the rt_l... printer versions.
        /// A floating-point number is printed as its STR$, which is what the ZX81 shows.</summary>
        void GenPrint(PrintStmt p)
        {
            string pre = p.Printer ? "rt_l" : "rt_";
            foreach (var it in p.Items)
            {
                switch (it.Kind)
                {
                    case PIKind.Expr:
                        if (it.A.Type == VT.Num && !FloatMath.IsFloat(it.A)) { GenNum(it.A, 0); Call(pre + "prnum"); }
                        else
                        {
                            Expr se = it.A.Type == VT.Num ? new Func("STR$", it.A, VT.Str) { Line = it.A.Line } : it.A;
                            GenStrRegion(se, 0);
                            Call(pre + "prstr");
                        }
                        break;
                    case PIKind.At:
                        GenOperands(it.A, it.B, 0);    // HL = row, DE = column
                        Call(pre + "at");
                        break;
                    case PIKind.Tab:
                        GenNum(it.A, 0);
                        Call(pre + "tab");
                        break;
                    case PIKind.Comma:
                        Call(pre + "comma");
                        break;
                }
            }
            if (p.Newline) Call(pre + "newline");
        }

        // ---------------- assignment / INPUT ----------------
        /// <summary>Is the target a whole ordinary string variable (A$ with no brackets
        /// and no DIM)? Those can change length; everything else is fixed-length.</summary>
        bool IsDynamicWhole(Expr target)
        {
            var sv = target as StrVar;
            return sv != null && sv.Args.Count == 0 && StrKind(sv.Name) == 0;
        }

        /// <summary>What kind of string variable A$ is:
        /// 0 = ordinary (no DIM), 1 = DIM A$(n) fixed-length string, 2 = DIM A$(n,m) array of strings.</summary>
        int StrKind(char name)
        {
            List<int> d;
            if (!strArr.TryGetValue(name, out d)) return 0; // dynamic
            return d.Count == 1 ? 1 : 2;                      // fixed string / 2D array
        }

        /// <summary>Record that an ordinary string variable exists (so it gets memory).</summary>
        void UseStr(char c)
        {
            if (StrKind(c) == 0 && !strDyn.Contains(c)) strDyn.Add(c);
        }

        /// <summary>LET target = value, or INPUT target when fromInput is true (then value is
        /// null and the value comes from the keyboard instead).</summary>
        void GenAssign(Expr target, Expr value, bool fromInput)
        {
            if (target.Type == VT.Num)
            {
                // INPUT reads a line of text, then rt_val converts it to a number in HL
                if (fromInput) { Call("rt_input"); Call("rt_val"); }
                else WarnIfRounded(value, "variable " + BasicWriter.Expr(target));
                var nv = target as NumVar;
                if (nv != null)
                {
                    UseNumVar(nv.Name);
                    if (!fromInput) GenNum(value, 0);
                    Op("LD (V_" + nv.Name + "),HL");
                    return;
                }
                // array element: work out its address in HL and the value in DE, then store
                var na = (NumArr)target;
                if (!fromInput && IsSimple(value))
                {
                    GenArrAddr(na, 0);
                    LoadDE(value);
                }
                else
                {
                    if (!fromInput) GenNum(value, 0);
                    Op("PUSH HL");
                    GenArrAddr(na, 0);
                    Op("POP DE");
                }
                Op("LD (HL),E");
                Op("INC HL");
                Op("LD (HL),D");
                return;
            }
            // strings
            if (IsDynamicWhole(target))
            {
                // ordinary string variable: copy the new text in and set its length
                char c = ((StrVar)target).Name;
                UseStr(c);
                if (fromInput) Call("rt_input"); else GenStrRegion(value, 0);
                Op("LD DE,S_" + c);
                Op("LD A," + opt.MaxString);
                Call("rt_strset");
                return;
            }
            // Fixed-length target (slice, DIM'd string or string array element): the ZX81
            // copies the new text in, cutting it short or padding it with spaces to fit.
            CheckAssignable(target);
            GenStrRegion(target, 0);          // where it goes: HL, C
            Op("PUSH HL");
            Op("PUSH BC");
            if (fromInput) Call("rt_input"); else GenStrRegion(value, 0);   // what goes there
            Op("LD A,C");                     // A = source length
            Op("POP BC");                     // C = destination length
            Op("POP DE");                     // DE = destination address
            Call("rt_strput");
        }

        /// <summary>A string target must be based on a variable ("ABC"(1)="X" makes no sense).</summary>
        void CheckAssignable(Expr t)
        {
            if (t is StrVar) return;
            var se = t as SliceExpr;
            if (se != null) { CheckAssignable(se.Base); return; }
            throw Err("cannot assign to this string expression");
        }

        void GenInput(Expr target)
        {
            GenAssign(target, null, true);
        }

        // ---------------- numeric expressions ----------------
        /// <summary>A constant or a plain variable can be loaded straight into a register
        /// without disturbing anything else, which allows shorter code.</summary>
        static bool IsSimple(Expr e) { return e is NumLit || e is NumVar; }

        /// <summary>Load a simple expression into DE.</summary>
        void LoadDE(Expr e)
        {
            if (e is NumLit) Op("LD DE," + ((NumLit)e).Value);
            else { var v = (NumVar)e; UseNumVar(v.Name); Op("LD DE,(V_" + v.Name + ")"); }
        }

        /// <summary>HL = left, DE = right
        /// Avoids saving on the stack when either side is simple; otherwise the left value
        /// is pushed while the right one is worked out.</summary>
        void GenOperands(Expr l, Expr r, int f)
        {
            if (IsSimple(r)) { GenNum(l, f); LoadDE(r); }
            else if (IsSimple(l)) { GenNum(r, f); Op("EX DE,HL"); GenNum(l, f); }
            else { GenNum(l, f); Op("PUSH HL"); GenNum(r, f); Op("EX DE,HL"); Op("POP HL"); }
        }

        /// <summary>If e is the constant 2, 4, 8 ... 256, returns the power (1..8), else -1.
        /// Used to replace a multiplication with repeated doubling (ADD HL,HL).</summary>
        static int Pow2(Expr e)
        {
            var n = e as NumLit;
            if (n == null) return -1;
            for (int k = 1; k <= 8; k++) if (n.Value == 1 << k) return k;
            return -1;
        }

        /// <summary>Runtime routine for each comparison operator (each returns 1 or 0 in HL).</summary>
        static readonly Dictionary<string, string> CmpRt = new Dictionary<string, string> {
            {"=","rt_eq"},{"<>","rt_ne"},{"<","rt_lt"},{">","rt_gt"},{"<=","rt_le"},{">=","rt_ge"} };

        /// <summary>Generate code that leaves the value of a numeric expression in HL.
        /// f = first free temporary string buffer (for any strings inside, e.g. LEN A$).</summary>
        void GenNum(Expr e, int f)
        {
            if (e is NumLit) { Op("LD HL," + ((NumLit)e).Value); return; }
            if (e is FloatLit) { Op("LD HL," + RoundConst((FloatLit)e)); return; }
            if (FloatMath.IsFloat(e))
            {
                // a float island: work it out with the ROM calculator, then round it
                GenFloat(e, f);
                Call("rt_fpop");
                return;
            }
            if (e is NumVar) { var v = (NumVar)e; UseNumVar(v.Name); Op("LD HL,(V_" + v.Name + ")"); return; }
            if (e is NumArr)
            {
                // address of the element, then load the 2-byte value from it
                GenArrAddr((NumArr)e, f);
                Op("LD A,(HL)"); Op("INC HL"); Op("LD H,(HL)"); Op("LD L,A");
                return;
            }
            if (e is Unary)
            {
                var u = (Unary)e;
                GenNum(u.A, f);
                Call(u.Op == "NEG" ? "neg_hl" : "rt_not");
                return;
            }
            if (e is RndN) { GenNum(((RndN)e).N, f); Call("rt_rndn"); return; }
            if (e is Func) { GenFunc((Func)e, f); return; }
            var b = e as Binary;
            if (b == null) throw Err("internal: bad numeric expression");
            if (b.Op.StartsWith("S"))
            {
                // string comparison: rt_strcmp gives -1/0/1, then compare that with 0
                GenStrCmp(b.L, b.R, f);
                Op("LD DE,0");
                Call(CmpRt[b.Op.Substring(1)]);
                return;
            }
            var rl = b.R as NumLit;                   // is the right-hand side a constant?
            int rv = rl == null ? 0 : (short)rl.Value; // ...and its signed value
            switch (b.Op)
            {
                case "+":
                    // X+1 .. X+3 (or minus): INC/DEC is shorter and faster than an add
                    if (rl != null && rv >= -3 && rv <= 3)
                    {
                        GenNum(b.L, f);
                        for (int i = 0; i < Math.Abs(rv); i++) Op(rv > 0 ? "INC HL" : "DEC HL");
                        return;
                    }
                    GenOperands(b.L, b.R, f); Op("ADD HL,DE"); return;
                case "-":
                    if (rl != null && rv >= -3 && rv <= 3)
                    {
                        GenNum(b.L, f);
                        for (int i = 0; i < Math.Abs(rv); i++) Op(rv > 0 ? "DEC HL" : "INC HL");
                        return;
                    }
                    // OR A clears the carry flag, so SBC acts as a plain subtract
                    GenOperands(b.L, b.R, f); Op("OR A"); Op("SBC HL,DE"); return;
                case "*":
                    {
                        // multiply by 2, 4, 8 ... : double HL that many times
                        int k = Pow2(b.R); Expr other = b.L;
                        if (k < 0) { k = Pow2(b.L); other = b.R; }
                        if (k > 0)
                        {
                            GenNum(other, f);
                            for (int i = 0; i < k; i++) Op("ADD HL,HL");
                            return;
                        }
                        GenOperands(b.L, b.R, f); Call("rt_mul"); return;
                    }
                case "/": GenOperands(b.L, b.R, f); Call("rt_div"); return;
                case "**": GenOperands(b.L, b.R, f); Call("rt_pow"); return;
                case "AND": GenOperands(b.L, b.R, f); Call("rt_and"); return;
                case "OR": GenOperands(b.L, b.R, f); Call("rt_or"); return;
            }
            string rt;
            if (CmpRt.TryGetValue(b.Op, out rt)) { GenOperands(b.L, b.R, f); Call(rt); return; }
            throw Err("internal: unknown operator " + b.Op);
        }

        // ---------------- floating point ----------------
        /// <summary>Put the value of e on the ROM's calculator stack, worked out in floating
        /// point as the ZX81 would (see FloatMath). Whole-number parts that aren't
        /// arithmetic (variables, PEEK, CODE, string comparisons ...) are worked out
        /// as usual and then stacked. Every operation is RST 28h, its code, 34h.</summary>
        void GenFloat(Expr e, int f)
        {
            if (e is FloatLit) { Op("LD HL," + Literal(((FloatLit)e).Bytes)); Call("rt_fplit"); return; }
            if (e is PiConst) { Op("LD HL," + Literal(Runtime.PiBytes)); Call("rt_fplit"); return; }
            var n = e as NumLit;
            if (n != null)
            {
                if (n.Unsigned) { Op("LD BC," + n.Value); Op("CALL " + Runtime.ROM_STACK_BC); }   // 32768..65535
                else { Op("LD HL," + n.Value); Call("rt_fpushi"); }
                return;
            }
            if (e is RndBare) { Call("rt_frnd"); return; }
            var rn = e as RndN;
            if (rn != null) { Call("rt_frnd"); GenFloat(rn.N, f); Calc("*"); return; }   // RND*n with n a float
            var fn = e as Func;
            if (fn != null && fn.Name == "VAL") { GenStrRegion(fn.Arg, f); Call("rt_fval"); return; }
            if (fn != null && (FloatMath.FloatFuncs.Contains(fn.Name) || FloatMath.PassFuncs.Contains(fn.Name)))
            {
                GenFloat(fn.Arg, f);
                Calc(fn.Name);
                return;
            }
            var u = e as Unary;
            if (u != null) { GenFloat(u.A, f); Calc(u.Op); return; }
            var b = e as Binary;
            if (b != null && b.L.Type == VT.Num && FloatMath.PassOps.Contains(b.Op))
            {
                GenFloat(b.L, f);
                GenFloat(b.R, f);
                Calc(b.Op);
                return;
            }
            GenNum(e, f);
            Call("rt_fpushi");
        }

        /// <summary>One ROM calculator operation on the top of the calculator stack.</summary>
        void Calc(string op)
        {
            int c = Runtime.CalcOps[op];
            string hex = "0" + c.ToString("X2") + "h";
            if (Runtime.CalcNeedsB(c)) Op("LD B," + hex);     // comparisons also read B
            Op("RST 28h");
            Op("DB " + hex + ",34h");
        }

        /// <summary>A constant like 2.5 used where a whole number is needed: round it now
        /// (as the ROM would at run time) and say so.</summary>
        int RoundConst(FloatLit c)
        {
            double r = FloatMath.Round(c.Value);
            string txt = c.Value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
            if (r < -32768 || r > 65535)
            {
                Diags.Add(new Diagnostic { BasicLine = curLine, Message = "number " + txt + " is too big for a whole number (-32768 to 65535)" });
                return (int)((long)r & 0xFFFF);
            }
            if (r != c.Value)
                Diags.Add(new Diagnostic { BasicLine = curLine, Message = "number " + txt + " is not a whole number; a whole number is needed here, so it becomes " + r });
            return (int)r & 0xFFFF;
        }

        /// <summary>Warn when a floating-point value that may have a fraction is rounded
        /// to be stored (variables and FOR loops hold whole numbers): LET A=H*PI/6 stores
        /// 1, not 0.5236. Not for INT (...), SGN, comparisons, or constants (RoundConst
        /// warns about those itself).</summary>
        void WarnIfRounded(Expr value, string what)
        {
            if (value == null || !FloatMath.IsFloat(value) || value is FloatLit) return;
            var fn = value as Func;
            if (fn != null && (fn.Name == "INT" || fn.Name == "SGN")) return;
            var b = value as Binary;
            if (b != null && (CmpRt.ContainsKey(b.Op) || b.Op == "AND" || b.Op == "OR")) return;
            if (value is Unary && ((Unary)value).Op == "NOT") return;
            Diags.Add(new Diagnostic { BasicLine = curLine, Message = what + " holds whole numbers, so the floating-point value " +
                BasicWriter.Expr(value) + " is rounded. To keep the fraction, use the expression where it is needed (e.g. COS (" +
                BasicWriter.Expr(value) + ")) instead of storing it." });
        }

        // ---------------- constants like 2.5 where a whole number is needed ----------------
        /// <summary>Before generating a statement: a floating-point constant (2.5, 34.0)
        /// used where a whole number is needed becomes that whole number (rounded, with a
        /// warning if it had a fraction), so the code is the same as for 34. Constants
        /// that are worked out in floating point (inside SIN ..., or PRINTed) stay.</summary>
        void NormStmt(Stmt s)
        {
            if (s is PrintStmt)
                foreach (var it in ((PrintStmt)s).Items) { it.A = Norm(it.A, it.Kind == PIKind.Expr); it.B = Norm(it.B, false); }
            else if (s is LetStmt) { var l = (LetStmt)s; l.Target = Norm(l.Target, false); l.Value = Norm(l.Value, false); }
            else if (s is IfStmt) { var f = (IfStmt)s; f.Cond = Norm(f.Cond, true); NormStmt(f.Then); }   // IF 0.5 is true
            else if (s is GotoStmt) ((GotoStmt)s).Target = Norm(((GotoStmt)s).Target, false);
            else if (s is ForStmt) { var f = (ForStmt)s; f.From = Norm(f.From, false); f.To = Norm(f.To, false); f.Step = Norm(f.Step, false); }
            else if (s is InputStmt) ((InputStmt)s).Target = Norm(((InputStmt)s).Target, false);
            else if (s is PokeStmt) { var p = (PokeStmt)s; p.Addr = Norm(p.Addr, false); p.Value = Norm(p.Value, false); }
            else if (s is PlotStmt) { var p = (PlotStmt)s; p.X = Norm(p.X, false); p.Y = Norm(p.Y, false); }
            else if (s is PauseStmt) ((PauseStmt)s).N = Norm(((PauseStmt)s).N, false);
            else if (s is RandStmt) ((RandStmt)s).N = Norm(((RandStmt)s).N, false);
            else if (s is RunStmt) ((RunStmt)s).N = Norm(((RunStmt)s).N, false);
            else if (s is TapeStmt) ((TapeStmt)s).Name = Norm(((TapeStmt)s).Name, false);
        }

        /// <summary>See NormStmt. fl = the value of e is used as floating point here.</summary>
        Expr Norm(Expr e, bool fl)
        {
            if (e == null) return null;
            var c = e as FloatLit;
            if (c != null)
            {
                if (fl) return e;
                var n = new NumLit(RoundConst(c)) { Line = e.Line };
                n.Unsigned = n.Value > 32767 && c.Value > 0;
                return n;
            }
            // which children are worked out in floating point (see GenFloat)?
            bool kids = FloatMath.IsFloat(e);
            var b = e as Binary;
            if (b != null)
            {
                bool numeric = b.L.Type == VT.Num && FloatMath.PassOps.Contains(b.Op);
                b.L = Norm(b.L, kids && numeric);
                b.R = Norm(b.R, (kids && numeric) || (b.Op == "AND" && b.Type == VT.Str));   // A$ AND 0.5
                return e;
            }
            var u = e as Unary; if (u != null) { u.A = Norm(u.A, kids); return e; }
            var f = e as Func;
            if (f != null && f.Arg != null)
            {
                bool argFloat = (f.Name == "STR$") ||
                    (kids && f.Name != "VAL" && (FloatMath.FloatFuncs.Contains(f.Name) || FloatMath.PassFuncs.Contains(f.Name)));
                f.Arg = Norm(f.Arg, argFloat);
                return e;
            }
            var r = e as RndN; if (r != null) { r.N = Norm(r.N, kids); return e; }
            var a = e as NumArr; if (a != null) { for (int k = 0; k < a.Index.Count; k++) a.Index[k] = Norm(a.Index[k], false); return e; }
            var sv = e as StrVar; if (sv != null) { foreach (var x in sv.Args) { x.From = Norm(x.From, false); x.To = Norm(x.To, false); } return e; }
            var se = e as SliceExpr; if (se != null) { se.Base = Norm(se.Base, false); se.Arg.From = Norm(se.Arg.From, false); se.Arg.To = Norm(se.Arg.To, false); return e; }
            return e;
        }

        /// <summary>Does the statement use the ROM calculator or workspace?</summary>
        static bool StmtUsesCalc(Stmt s)
        {
            if (s is TapeStmt) return true;
            return StmtExprs(s).Any(ExprUsesCalc);
        }

        /// <summary>All the expressions directly inside a statement.</summary>
        static IEnumerable<Expr> StmtExprs(Stmt s)
        {
            var list = new List<Expr>();
            if (s is PrintStmt) foreach (var it in ((PrintStmt)s).Items) { list.Add(it.A); list.Add(it.B); }
            else if (s is LetStmt) { list.Add(((LetStmt)s).Target); list.Add(((LetStmt)s).Value); }
            else if (s is IfStmt) { list.Add(((IfStmt)s).Cond); list.AddRange(StmtExprs(((IfStmt)s).Then)); }
            else if (s is GotoStmt) list.Add(((GotoStmt)s).Target);
            else if (s is ForStmt) { var fs = (ForStmt)s; list.Add(fs.From); list.Add(fs.To); list.Add(fs.Step); }
            else if (s is InputStmt) list.Add(((InputStmt)s).Target);
            else if (s is PokeStmt) { list.Add(((PokeStmt)s).Addr); list.Add(((PokeStmt)s).Value); }
            else if (s is PlotStmt) { list.Add(((PlotStmt)s).X); list.Add(((PlotStmt)s).Y); }
            else if (s is PauseStmt) list.Add(((PauseStmt)s).N);
            else if (s is RandStmt) list.Add(((RandStmt)s).N);
            else if (s is RunStmt) list.Add(((RunStmt)s).N);
            else if (s is TapeStmt) list.Add(((TapeStmt)s).Name);
            return list.Where(x => x != null);
        }

        /// <summary>Does the expression (or anything inside it) use floating point?</summary>
        static bool ExprUsesCalc(Expr e)
        {
            if (e == null) return false;
            if (FloatMath.IsFloat(e)) return true;
            var b = e as Binary; if (b != null) return ExprUsesCalc(b.L) || ExprUsesCalc(b.R);
            var u = e as Unary; if (u != null) return ExprUsesCalc(u.A);
            var f = e as Func; if (f != null) return ExprUsesCalc(f.Arg);
            var r = e as RndN; if (r != null) return ExprUsesCalc(r.N);
            var a = e as NumArr; if (a != null) return a.Index.Any(ExprUsesCalc);
            var sv = e as StrVar; if (sv != null) return sv.Args.Any(x => ExprUsesCalc(x.From) || ExprUsesCalc(x.To));
            var se = e as SliceExpr; if (se != null) return ExprUsesCalc(se.Base) || ExprUsesCalc(se.Arg.From) || ExprUsesCalc(se.Arg.To);
            return false;
        }

        /// <summary>Functions that return a number.</summary>
        void GenFunc(Func fn, int f)
        {
            switch (fn.Name)
            {
                case "ABS": GenNum(fn.Arg, f); Call("rt_abs"); return;
                case "SGN": GenNum(fn.Arg, f); Call("rt_sgn"); return;
                case "INT": GenNum(fn.Arg, f); return;           // values are already whole numbers
                case "PEEK": GenNum(fn.Arg, f); Op("LD L,(HL)"); Op("LD H,0"); return;
                // USR: call the machine code; like the ZX81, the result is whatever is in BC
                case "USR": GenNum(fn.Arg, f); Call("rt_usr"); Op("LD H,B"); Op("LD L,C"); return;
                case "CODE": GenStrRegion(fn.Arg, f); Call("rt_code"); return;
                case "LEN": GenStrRegion(fn.Arg, f); Op("LD L,C"); Op("LD H,0"); return;   // length is in C
            }
            throw Err("internal: function " + fn.Name);   // (VAL and SIN etc. are floating point: see GenFloat)
        }

        /// <summary>Leave the address of a numeric array element in HL.
        /// Elements are 2 bytes, subscripts start at 1, and 2D arrays are stored row by row:
        ///   A(i)    -> A_x + 2*(i-1)
        ///   A(i,j)  -> A_x + 2*((i-1)*cols + (j-1))
        /// There is no bounds checking.</summary>
        void GenArrAddr(NumArr a, int f)
        {
            List<int> dims;
            if (!numArr.TryGetValue(a.Name, out dims)) throw Err("array " + a.Name + "() used without DIM");
            if (dims.Count != a.Index.Count) throw Err("array " + a.Name + "() needs " + dims.Count + " subscript(s)");
            GenNum(a.Index[0], f);
            Op("DEC HL");
            if (dims.Count == 2)
            {
                MulConst(dims[1]);
                Op("PUSH HL");
                GenNum(a.Index[1], f);
                Op("DEC HL");
                Op("POP DE");
                Op("ADD HL,DE");
            }
            Op("ADD HL,HL");
            Op("LD DE,A_" + a.Name);
            Op("ADD HL,DE");
        }

        /// <summary>HL = HL * m for a constant m (doubling for powers of 2, else rt_mul).</summary>
        void MulConst(int m)
        {
            if (m == 1) return;
            for (int k = 1; k <= 8; k++) if (m == 1 << k) { for (int i = 0; i < k; i++) Op("ADD HL,HL"); return; }
            Op("LD DE," + m);
            Call("rt_mul");
        }

        // ---------------- conditions ----------------
        /// <summary>Code for IF: jump to label if the condition is false (zero).
        /// Comparisons are tested directly with the processor flags, rather than first
        /// making a 1/0 value, which makes IF statements a lot faster.
        ///   rt_scmp sets the carry flag when HL &lt; DE (signed), so for example
        ///   "IF A&lt;B" = compare, jump if no carry; "IF A&gt;B" = swap, compare, jump if no carry.</summary>
        void GenJumpIfFalse(Expr c, string label)
        {
            var b = c as Binary;
            if (b != null)
            {
                // A AND B is false if either part is false
                if (b.Op == "AND" && b.L.Type == VT.Num)
                {
                    GenJumpIfFalse(b.L, label);
                    GenJumpIfFalse(b.R, label);
                    return;
                }
                if (CmpRt.ContainsKey(b.Op) && !FloatMath.IsFloat(b))
                {
                    // X=0 / X<>0: just test whether HL is zero
                    var rl = b.R as NumLit;
                    if ((b.Op == "=" || b.Op == "<>") && rl != null && rl.Value == 0)
                    {
                        GenNum(b.L, 0);
                        Op("LD A,H"); Op("OR L");
                        Op((b.Op == "=" ? "JP NZ," : "JP Z,") + label);
                        return;
                    }
                    GenOperands(b.L, b.R, 0);
                    switch (b.Op)
                    {
                        case "=": Op("OR A"); Op("SBC HL,DE"); Op("JP NZ," + label); return;
                        case "<>": Op("OR A"); Op("SBC HL,DE"); Op("JP Z," + label); return;
                        case "<": Call("rt_scmp"); Op("JP NC," + label); return;
                        case ">=": Call("rt_scmp"); Op("JP C," + label); return;
                        case ">": Op("EX DE,HL"); Call("rt_scmp"); Op("JP NC," + label); return;
                        case "<=": Op("EX DE,HL"); Call("rt_scmp"); Op("JP C," + label); return;
                    }
                }
                if (b.Op.StartsWith("S"))
                {
                    // string comparison: HL = -1 (less), 0 (equal) or 1 (greater)
                    GenStrCmp(b.L, b.R, 0);
                    switch (b.Op.Substring(1))
                    {
                        case "=": Op("LD A,H"); Op("OR L"); Op("JP NZ," + label); return;
                        case "<>": Op("LD A,H"); Op("OR L"); Op("JP Z," + label); return;
                        case "<": Op("BIT 7,H"); Op("JP Z," + label); return;        // false unless -1
                        case ">=": Op("BIT 7,H"); Op("JP NZ," + label); return;
                        case ">": Op("DEC HL"); Op("LD A,H"); Op("OR L"); Op("JP NZ," + label); return;  // false unless 1
                        case "<=": Op("DEC HL"); Op("LD A,H"); Op("OR L"); Op("JP Z," + label); return;
                    }
                }
            }
            // anything else: work out the value and test for zero
            if (FloatMath.IsFloat(c))
            {
                // floating point: true means not zero (0.3 is true), so test NOT c, which is 1 or 0
                GenFloat(c, 0);
                Calc("NOT");
                Call("rt_fpop");
                Op("LD A,H"); Op("OR L");
                Op("JP NZ," + label);
                return;
            }
            GenNum(c, 0);
            Op("LD A,H"); Op("OR L");
            Op("JP Z," + label);
        }

        // ---------------- strings ----------------
        /// <summary>Compare two strings: HL = -1, 0 or 1. The right side uses depth f+1
        /// because the left side's region (possibly in T_f) is still needed.</summary>
        void GenStrCmp(Expr l, Expr r, int f)
        {
            GenStrRegion(l, f);
            Op("PUSH HL");
            Op("PUSH BC");
            GenStrRegion(r, f + 1);
            Op("LD A,C");      // A = right length, HL = right address
            Op("POP BC");      // C = left length
            Op("POP DE");      // DE = left address
            Call("rt_strcmp");
        }

        /// <summary>Label for a string literal. Identical literals share one copy.</summary>
        string Literal(byte[] codes)
        {
            string key = BitConverter.ToString(codes);
            string lbl;
            if (!literals.TryGetValue(key, out lbl))
            {
                lbl = "LIT_" + literals.Count;
                literals[key] = lbl;
                litList.Add(new KeyValuePair<string, byte[]>(lbl, codes));
            }
            return lbl;
        }

        /// <summary>Evaluate a string expression: HL = address, BC = length (B = 0)
        /// Literals, variables and slices of them are used where they are; anything else
        /// is built in temporary buffer T_f.</summary>
        void GenStrRegion(Expr e, int f)
        {
            if (e is StrLit)
            {
                var s = (StrLit)e;
                if (s.Codes.Length > 255) throw Err("string literal longer than 255 characters");
                Op("LD HL," + Literal(s.Codes));
                Op("LD BC," + s.Codes.Length);
                return;
            }
            if (e is StrVar) { GenStrVar((StrVar)e, f); return; }
            if (e is SliceExpr)
            {
                var se = (SliceExpr)e;
                GenSlice(se.Arg, () => GenStrRegion(se.Base, f), f);
                return;
            }
            // needs building in a temporary buffer
            if (f > maxDepth) maxDepth = f;
            if (f > 12) throw Err("string expression too complicated");
            Op("LD HL,T_" + f);
            Op("LD (HL),0");               // start with an empty string
            GenAppend(e, f, f + 1);        // add the parts; anything nested uses f+1
            Op("LD HL,T_" + f);            // the result: HL = text, C = length
            Op("LD C,(HL)");
            Op("LD B,0");
            Op("INC HL");
        }

        /// <summary>Add the value of string expression e to the end of temporary buffer T_t.
        /// f is the first buffer that nested evaluation may use (always more than t).</summary>
        void GenAppend(Expr e, int t, int f)
        {
            var b = e as Binary;
            // A$+B$: add each side in turn
            if (b != null && b.Op == "CAT") { GenAppend(b.L, t, f); GenAppend(b.R, t, f); return; }
            // A$ AND n: add A$ only if n is not zero
            if (b != null && b.Op == "AND")
            {
                string skip = NewLabel();
                if (FloatMath.IsFloat(b.R))
                {
                    // floating-point condition: any value but 0 counts (test NOT n)
                    GenFloat(b.R, f);
                    Calc("NOT");
                    Call("rt_fpop");
                    Op("LD A,H"); Op("OR L");
                    Op("JP NZ," + skip);
                }
                else
                {
                    GenNum(b.R, f);
                    Op("LD A,H"); Op("OR L");
                    Op("JP Z," + skip);
                }
                GenAppend(b.L, t, f);
                Lbl(skip);
                return;
            }
            var fn = e as Func;
            if (fn != null && fn.Name == "CHR$")
            {
                GenNum(fn.Arg, f);
                Op("LD A,L");
                Op("LD DE,T_" + t);
                Call("rt_appchr");
                return;
            }
            if (fn != null && fn.Name == "STR$" && FloatMath.IsFloat(fn.Arg))
            {
                // floating point: the ROM makes the text, exactly as the ZX81 prints it
                GenFloat(fn.Arg, f);
                Calc("STR$");
                Call("rt_fstr");       // HL, BC = the text (in the ROM's workspace)
                Op("LD DE,T_" + t);
                Call("rt_append");
                Call("rt_freset");     // give the workspace back
                return;
            }
            if (fn != null && fn.Name == "STR$")
            {
                GenNum(fn.Arg, f);
                Call("rt_numstr");     // number -> digits (HL, BC)
                Op("LD DE,T_" + t);
                Call("rt_append");
                return;
            }
            if (e is Inkey)
            {
                // rt_inkey sets carry if a key is down; only then add the character
                Call("rt_inkey");
                Op("LD DE,T_" + t);
                Use("rt_appchr");
                Op("CALL C,rt_appchr");
                return;
            }
            // anything else: get its region, then copy it in
            GenStrRegion(e, f);
            Op("LD DE,T_" + t);
            Call("rt_append");
        }

        /// <summary>Slice a string: X(from TO to). The from/to values are pushed first,
        /// then baseGen produces the string being sliced (HL, BC), and rt_slice works out
        /// the part wanted. A single subscript X(n) is the slice X(n TO n).
        /// A missing "from" means 1; a missing "to" means 7FFFh, which rt_slice reduces to
        /// the string's length.</summary>
        void GenSlice(SliceArg a, Action baseGen, int f)
        {
            if (!a.IsRange)
            {
                GenNum(a.From, f);
                Op("PUSH HL");        // from
                Op("PUSH HL");        // to (the same)
            }
            else
            {
                if (a.From == null) Op("LD HL,1"); else GenNum(a.From, f);
                Op("PUSH HL");
                if (a.To == null) Op("LD HL,7FFFh"); else GenNum(a.To, f);
                Op("PUSH HL");
            }
            baseGen();
            Op("POP DE");             // DE = to
            Op("EX (SP),HL");         // HL = from, and the string address goes on the stack
            Call("rt_slice");
        }

        /// <summary>String variable reference. What the brackets mean depends on the kind:
        ///   ordinary / DIM A$(n):  A$  A$(3)  A$(2 TO 5)     brackets slice the string
        ///   DIM A$(n,m):           A$(i)                     the i-th string (m characters)
        ///                          A$(i,j) / A$(i, a TO b)   a slice of the i-th string</summary>
        void GenStrVar(StrVar v, int f)
        {
            int kind = StrKind(v.Name);
            UseStr(v.Name);
            if (kind == 0 || kind == 1)
            {
                // baseGen produces the whole string's region
                Action baseGen;
                if (kind == 0)
                    // ordinary: S_x holds a length byte then the text
                    baseGen = () => { Op("LD HL,S_" + v.Name); Op("LD C,(HL)"); Op("LD B,0"); Op("INC HL"); };
                else
                {
                    // fixed length: always n characters
                    int n = strArr[v.Name][0];
                    baseGen = () => { Op("LD HL,SA_" + v.Name); Op("LD BC," + n); };
                }
                if (v.Args.Count == 0) { baseGen(); return; }
                if (v.Args.Count > 1) throw Err(v.Name + "$ is not a 2-dimensional string array");
                GenSlice(v.Args[0], baseGen, f);
                return;
            }
            // 2D string array: A$(i) is a string of the second dimension's length
            var dims = strArr[v.Name];
            if (v.Args.Count == 0) throw Err(v.Name + "$ is a string array and needs a subscript");
            if (v.Args[0].IsRange) throw Err("the first subscript of string array " + v.Name + "$ must be a single number");
            // elem produces the region of string number i: SA_x + (i-1)*m, length m
            Action elem = () =>
            {
                GenNum(v.Args[0].From, f);
                Op("DEC HL");
                MulConst(dims[1]);
                Op("LD DE,SA_" + v.Name);
                Op("ADD HL,DE");
                Op("LD BC," + dims[1]);
            };
            if (v.Args.Count == 1) { elem(); return; }
            GenSlice(v.Args[1], elem, f);
        }
    }
}
