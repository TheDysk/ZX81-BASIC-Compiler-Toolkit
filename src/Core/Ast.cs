// =====================================================================================
//  Ast.cs - the Abstract Syntax Tree (AST)
// -------------------------------------------------------------------------------------
//  The parser (Parser.cs) turns each line of BASIC text into a tree of the objects
//  defined here, and the code generator (CodeGen.cs) walks the tree to produce Z80
//  assembly. Nothing in this file does any work; it only describes the shape of a
//  program.
//
//  Example:  20 LET X = A + 2*B   becomes
//
//      BasicLine { Number = 20,
//                  Stmt = LetStmt { Target = NumVar("X"),
//                                   Value  = Binary("+", NumVar("A"),
//                                                        Binary("*", NumLit(2), NumVar("B"))) } }
//
//  There are two families of node:
//    * Expr - something that produces a value (a number or a string)
//    * Stmt - a BASIC statement (one per ZX81 line, or the statement after THEN)
// =====================================================================================
using System.Collections.Generic;

namespace ZX81Compiler
{
    /// <summary>Value Type of an expression. Every expression is either a number or a string,
    /// and the parser checks this as it builds the tree (so "LET A=B$" is caught early).</summary>
    public enum VT { Num, Str }

    /// <summary>Base class of every expression node.</summary>
    public abstract class Expr
    {
        /// <summary>Num or Str: what kind of value this expression produces.</summary>
        public VT Type;
        /// <summary>The BASIC line number the expression came from (used in error messages).</summary>
        public int Line;
    }

    // ---------------------------------------------------------------------------------
    //  Simple values
    // ---------------------------------------------------------------------------------

    /// <summary>A number written in the program, e.g. 42 or 4E4.
    /// Stored as 0..65535 (the 16-bit pattern); negative values are stored in two's
    /// complement, so -1 is stored as 65535.</summary>
    public class NumLit : Expr
    {
        public int Value;
        /// <summary>True for a number 32768..65535 as written (e.g. an address like 40000).
        /// In whole-number code it is just the 16-bit pattern; in floating-point code it
        /// keeps its real (positive) value rather than being read as negative.</summary>
        public bool Unsigned;
        public NumLit(int v) { Value = v; Type = VT.Num; }
    }

    /// <summary>A number that isn't a whole number from -32768 to 65535, e.g. 0.5, 3.14 or
    /// 1E6. Any expression containing one is worked out in floating point (see FloatMath).
    /// Bytes holds the 5-byte ZX81 form of Value.</summary>
    public class FloatLit : Expr
    {
        public double Value;
        public byte[] Bytes;
        public FloatLit(double v) { Value = v; Bytes = ZxFloat.Encode(v); Type = VT.Num; }
        public FloatLit(byte[] b) { Bytes = b; Value = ZxFloat.Decode(b, 0); Type = VT.Num; }
    }

    /// <summary>PI (3.1415927). Always floating point.</summary>
    public class PiConst : Expr { public PiConst() { Type = VT.Num; } }

    /// <summary>A string written in quotes, e.g. "HELLO". The text has already been
    /// converted to ZX81 character codes (so 'A' is 38, space is 0, \85 is 133, ...).</summary>
    public class StrLit : Expr
    {
        public byte[] Codes;
        public StrLit(byte[] c) { Codes = c; Type = VT.Str; }
    }

    /// <summary>A simple numeric variable such as X or SCORE (names may be several letters/digits).</summary>
    public class NumVar : Expr
    {
        public string Name;
        public NumVar(string n) { Name = n; Type = VT.Num; }
    }

    /// <summary>An element of a numeric array, e.g. A(5) or B(I,J). Index holds 1 or 2 expressions.</summary>
    public class NumArr : Expr
    {
        public char Name;
        public List<Expr> Index;
        public NumArr(char n, List<Expr> i) { Name = n; Index = i; Type = VT.Num; }
    }

    // ---------------------------------------------------------------------------------
    //  Strings: variables, subscripts and slicing
    // ---------------------------------------------------------------------------------

    /// <summary>One subscript inside string brackets. Covers all the ZX81 forms:
    ///   A$(3)        From=3, To=3, IsRange=false
    ///   A$(2 TO 5)   From=2, To=5, IsRange=true
    ///   A$( TO 4)    From=null (means 1), To=4
    ///   A$(4 TO )    From=4, To=null (means "to the end")</summary>
    public class SliceArg
    {
        public Expr From, To;   // either may be null (default 1 / len)
        public bool IsRange;    // false: single index (From == To)
    }

    /// <summary>String variable reference with optional subscripts/slicers: A$  A$(3)  A$(2 TO 5)  A$(i,j)
    /// What the subscripts mean depends on whether A$ was DIMensioned, so this is only
    /// decided later, in CodeGen.GenStrVar.</summary>
    public class StrVar : Expr
    {
        public char Name;
        public List<SliceArg> Args;   // empty list when there are no brackets
        public StrVar(char n, List<SliceArg> a) { Name = n; Args = a; Type = VT.Str; }
    }

    /// <summary>Slice applied to any string expression: ("ABC"+X$)(2 TO 3)
    /// Also used for a second set of brackets after a variable, e.g. A$(2)(1 TO 3).</summary>
    public class SliceExpr : Expr
    {
        public Expr Base;      // the string being sliced
        public SliceArg Arg;   // which part of it
        public SliceExpr(Expr b, SliceArg a) { Base = b; Arg = a; Type = VT.Str; }
    }

    // ---------------------------------------------------------------------------------
    //  Operators and functions
    // ---------------------------------------------------------------------------------

    /// <summary>An operator with one operand. Op is "NEG" (unary minus, -X) or "NOT".</summary>
    public class Unary : Expr
    {
        public string Op;
        public Expr A;
        public Unary(string op, Expr a) { Op = op; A = a; Type = VT.Num; }
    }

    /// <summary>An operator with two operands L and R. Op is one of:
    ///   "+" "-" "*" "/" "**"            arithmetic
    ///   "=" "&lt;&gt;" "&lt;" "&gt;" "&lt;=" "&gt;="     numeric comparison (gives 1 or 0)
    ///   "S=" "S&lt;&gt;" "S&lt;" ...              string comparison (the "S" prefix marks it)
    ///   "AND" "OR"                       ZX81-style logical operators
    ///   "CAT"                            string concatenation (+ between strings)
    /// Type is Num for everything except CAT and "A$ AND n", which give strings.</summary>
    public class Binary : Expr
    {
        public string Op;
        public Expr L, R;
        public Binary(string op, Expr l, Expr r, VT t) { Op = op; L = l; R = r; Type = t; }
    }

    /// <summary>A built-in function with one argument: ABS SGN INT SQR PEEK USR and
    /// SIN COS TAN ASN ACS ATN LN EXP (number in, number out), CODE LEN VAL (string in,
    /// number out) and CHR$ STR$ (number in, string out).</summary>
    public class Func : Expr
    {
        public string Name;
        public Expr Arg;
        public Func(string n, Expr a, VT t) { Name = n; Arg = a; Type = t; }
    }

    /// <summary>INKEY$ - the key being pressed now, or "" if none.</summary>
    public class Inkey : Expr { public Inkey() { Type = VT.Str; } }

    /// <summary>RND on its own: a fraction from 0 to just under 1, so it is worked out in
    /// floating point. The parser turns the usual "RND*n" into an RndN node instead,
    /// which in whole-number code is a fast random whole number 0 to n-1.</summary>
    public class RndBare : Expr { public RndBare() { Type = VT.Num; } }

    /// <summary>RND*n - a random whole number from 0 to n-1.</summary>
    public class RndN : Expr
    {
        public Expr N;
        public RndN(Expr n) { N = n; Type = VT.Num; }
    }

    // ---------------------------------------------------------------------------------
    //  Statements
    // ---------------------------------------------------------------------------------

    /// <summary>Base class of every statement.</summary>
    public abstract class Stmt
    {
        /// <summary>The BASIC line number this statement belongs to.</summary>
        public int Line;
    }

    /// <summary>The kinds of thing that can appear in a PRINT list.</summary>
    public enum PIKind
    {
        Expr,   // a value to print (number or string), in PrintItem.A
        At,     // AT row,column - row in A, column in B
        Tab,    // TAB column - column in A
        Comma   // a comma: move to the next half of the line
    }

    /// <summary>One item of a PRINT statement (see PIKind for what A and B hold).</summary>
    public class PrintItem
    {
        public PIKind Kind;
        public Expr A, B;
    }

    /// <summary>PRINT item; item, ...  Newline is false when the statement ends with ';' or ','.</summary>
    public class PrintStmt : Stmt
    {
        public List<PrintItem> Items = new List<PrintItem>();
        public bool Newline = true;
        /// <summary>True for LPRINT (output goes to the ZX printer instead of the screen).</summary>
        public bool Printer;
    }

    /// <summary>SAVE Name or LOAD Name (Load = true). Name is a string expression.</summary>
    public class TapeStmt : Stmt
    {
        public Expr Name;
        public bool Load;
    }

    /// <summary>LET Target = Value. Target is a NumVar, NumArr, StrVar or SliceExpr.</summary>
    public class LetStmt : Stmt { public Expr Target, Value; }

    /// <summary>IF Cond THEN statement. "IF x THEN 100" is stored with Then = GOTO 100.</summary>
    public class IfStmt : Stmt
    {
        public Expr Cond;
        public Stmt Then;
    }

    /// <summary>GOTO Target, or GOSUB Target when Sub is true. Target may be any expression.</summary>
    public class GotoStmt : Stmt
    {
        public Expr Target;
        public bool Sub;
    }

    /// <summary>RETURN (from a GOSUB).</summary>
    public class ReturnStmt : Stmt { }

    /// <summary>FOR Var = From TO To STEP Step. Step is null when there is no STEP (means 1).</summary>
    public class ForStmt : Stmt
    {
        public char Var;
        public Expr From, To, Step;
    }

    /// <summary>NEXT Var.</summary>
    public class NextStmt : Stmt { public char Var; }

    /// <summary>DIM Name(d1[,d2]) or DIM Name$(d1[,d2]). The sizes must be constants.</summary>
    public class DimStmt : Stmt
    {
        public char Name;
        public bool IsStr;
        public List<int> Dims = new List<int>();
    }

    /// <summary>INPUT Target (a numeric or string variable / array element / slice).</summary>
    public class InputStmt : Stmt { public Expr Target; }

    /// <summary>POKE Addr, Value.</summary>
    public class PokeStmt : Stmt { public Expr Addr, Value; }

    /// <summary>PLOT X,Y or UNPLOT X,Y (Unplot = true).</summary>
    public class PlotStmt : Stmt
    {
        public Expr X, Y;
        public bool Unplot;
    }

    /// <summary>PAUSE N (frames of 1/50 second).</summary>
    public class PauseStmt : Stmt { public Expr N; }

    /// <summary>RAND [N]. N is null for a plain RAND.</summary>
    public class RandStmt : Stmt { public Expr N; }

    /// <summary>RUN [N]. N is null for a plain RUN (start from the first line).</summary>
    public class RunStmt : Stmt { public Expr N; }

    /// <summary>Statements with no operands. Kind is one of
    /// CLS, SCROLL, STOP, CLEAR, FAST, SLOW, COPY, NEW, or NOP (REM, and LIST, LLIST and
    /// CONT, which have nothing to do in a compiled program).</summary>
    public class SimpleStmt : Stmt
    {
        public string Kind;
        public SimpleStmt(string k) { Kind = k; }
    }

    /// <summary>One line of the BASIC program.</summary>
    public class BasicLine
    {
        public int Number;      // the BASIC line number, e.g. 10
        public int SourceLine;  // which line of the text file it came from (for messages)
        public Stmt Stmt;       // the statement on that line
    }

    // ---------------------------------------------------------------------------------
    //  Floating point ("float islands")
    // ---------------------------------------------------------------------------------

    /// <summary>Rules for which parts of a program use floating point.
    ///
    /// Numbers are normally 16-bit whole numbers, which is fast. An expression that
    /// contains SIN COS TAN ASN ACS ATN LN EXP SQR PI VAL, a bare RND, or a number such as 0.5
    /// is a "float island": the whole arithmetic expression around it is worked out by
    /// the ZX81 ROM's floating-point calculator, exactly as the ZX81 would (so 7/2 is
    /// 3.5 there). Where the result is used as a number - stored in a variable, used
    /// for PLOT, AT, an array subscript and so on - it is rounded to the nearest whole
    /// number, as the ZX81 does when it needs a whole number. PRINT and STR$ show the
    /// full floating-point value.</summary>
    public static class FloatMath
    {
        /// <summary>Functions that always give a floating-point result.</summary>
        public static readonly string[] FloatFuncs = { "SIN", "COS", "TAN", "ASN", "ACS", "ATN", "LN", "EXP", "SQR", "VAL" };
        /// <summary>Functions that are floating point when their argument is.</summary>
        public static readonly string[] PassFuncs = { "ABS", "SGN", "INT" };
        /// <summary>Numeric operators that are floating point when either side is.</summary>
        public static readonly string[] PassOps = { "+", "-", "*", "/", "**", "=", "<>", "<", ">", "<=", ">=", "AND", "OR" };

        /// <summary>Is e worked out in floating point (does it contain a float)?</summary>
        public static bool IsFloat(Expr e)
        {
            if (e == null || e.Type != VT.Num) return false;
            if (e is FloatLit || e is PiConst || e is RndBare) return true;
            var f = e as Func;
            if (f != null)
            {
                if (System.Array.IndexOf(FloatFuncs, f.Name) >= 0) return true;
                if (System.Array.IndexOf(PassFuncs, f.Name) >= 0) return IsFloat(f.Arg);
                return false;
            }
            var r = e as RndN;
            if (r != null) return IsFloat(r.N);
            var u = e as Unary;
            if (u != null) return IsFloat(u.A);
            var b = e as Binary;
            if (b != null && System.Array.IndexOf(PassOps, b.Op) >= 0 && b.L.Type == VT.Num)
                return IsFloat(b.L) || IsFloat(b.R);
            return false;
        }

        /// <summary>In full floating point, the numeric expressions that are still worked out
        /// as whole numbers in HL (then put on the calculator stack): constants, PEEK, CODE,
        /// LEN, USR and string comparisons. Everything else uses the ROM calculator.</summary>
        public static bool WholeByNature(Expr e)
        {
            if (e is NumLit) return true;
            var f = e as Func;
            if (f != null && (f.Name == "PEEK" || f.Name == "CODE" || f.Name == "LEN" || f.Name == "USR")) return true;
            var b = e as Binary;
            return b != null && b.Op.StartsWith("S");
        }

        /// <summary>The constant node for a value: a NumLit for whole numbers -32768..65535
        /// (Unsigned for 32768 and up), otherwise a FloatLit.</summary>
        public static Expr Const(double v)
        {
            if (v == System.Math.Floor(v) && v >= -32768 && v <= 65535)
            {
                var n = new NumLit((int)v & 0xFFFF);
                n.Unsigned = v > 32767;
                return n;
            }
            return new FloatLit(v);
        }

        /// <summary>The value of a constant node (NumLit read as signed unless Unsigned).</summary>
        public static double Value(Expr e)
        {
            var n = e as NumLit;
            if (n != null) return n.Unsigned ? n.Value : (short)n.Value;
            return ((FloatLit)e).Value;
        }

        /// <summary>Round like the ZX81 ROM (FP-TO-BC): to the nearest whole number, halves away from 0.</summary>
        public static double Round(double v) { return System.Math.Round(v, System.MidpointRounding.AwayFromZero); }
    }
}
