// =====================================================================================
//  BasicWriter.cs - turning an AST (Ast.cs) back into BASIC text
// -------------------------------------------------------------------------------------
//  Used by the decompiler after it has rebuilt a program's statements from machine
//  code. Brackets are added only where they are needed for the text to be read back
//  into the same tree by Parser.cs, using the same operator priorities:
//      OR 2, AND 3, NOT 4, comparisons 5, + - 6, * / 8, unary minus 9, ** 10,
//      numbers/variables/functions/bracketed expressions 12
//  Keeping the same tree matters: the decompiler checks its work by compiling the text
//  again and comparing the machine code, and a different tree gives different code.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>A reference to a program address (a GOTO / GOSUB target) that becomes a
    /// line number once the lines are numbered.</summary>
    public class AddrRef : Expr
    {
        public int Addr;
        public AddrRef(int a) { Addr = a; Type = VT.Num; }
    }

    /// <summary>A number that reads better unsigned (PAUSE 40000 rather than PAUSE -25536).</summary>
    public class NumLitU : NumLit
    {
        public NumLitU(int v) : base(v) { }
    }

    public static class BasicWriter
    {
        /// <summary>Gives the line number for an AddrRef.</summary>
        public static Func<int, int> LineOf = a => a;

        static int Prec(Expr e)
        {
            var b = e as Binary;
            if (b != null)
            {
                switch (b.Op)
                {
                    case "OR": return 2;
                    case "AND": return 3;
                    case "+": case "-": case "CAT": return 6;
                    case "*": case "/": return 8;
                    case "**": return 10;
                    default: return 5;    // comparisons
                }
            }
            var u = e as Unary;
            if (u != null) return u.Op == "NOT" ? 4 : 9;
            var n = e as NumLit;
            if (n != null && !(n is NumLitU) && n.Value > 32767) return 9;   // printed with a minus sign
            return 12;
        }

        /// <summary>Expression text, bracketed if its priority is below min.</summary>
        public static string Expr(Expr e, int min)
        {
            string s = Raw(e);
            return Prec(e) < min ? "(" + s + ")" : s;
        }

        public static string Expr(Expr e) { return Expr(e, 0); }

        static string Raw(Expr e)
        {
            if (e is AddrRef) return LineOf(((AddrRef)e).Addr).ToString();
            if (e is NumLitU) return ((NumLit)e).Value.ToString();
            if (e is NumLit) { int v = ((NumLit)e).Value; return v > 32767 ? "-" + (65536 - v) : v.ToString(); }
            if (e is NumVar) return ((NumVar)e).Name;
            if (e is NumArr)
            {
                var a = (NumArr)e;
                var parts = new List<string>();
                foreach (var i in a.Index) parts.Add(Expr(i));
                return a.Name + "(" + string.Join(",", parts) + ")";
            }
            if (e is StrLit) return Literal(((StrLit)e).Codes);
            if (e is StrVar)
            {
                var v = (StrVar)e;
                if (v.Args.Count == 0) return v.Name + "$";
                var parts = new List<string>();
                foreach (var a in v.Args) parts.Add(Slice(a));
                return v.Name + "$(" + string.Join(",", parts) + ")";
            }
            if (e is SliceExpr) { var s = (SliceExpr)e; return Expr(s.Base, 12) + "(" + Slice(s.Arg) + ")"; }
            if (e is Inkey) return "INKEY$";
            if (e is RndN) return "INT (RND*" + Expr(((RndN)e).N, 9) + ")";
            if (e is RndBare) return "RND";
            if (e is Func)
            {
                var f = (Func)e;
                return f.Name + " " + Expr(f.Arg, 12);
            }
            if (e is Unary)
            {
                var u = (Unary)e;
                return u.Op == "NOT" ? "NOT " + Expr(u.A, 4) : "-" + Expr(u.A, 10);
            }
            var b = e as Binary;
            if (b != null)
            {
                string op = b.Op == "CAT" ? "+" : b.Op.StartsWith("S") ? b.Op.Substring(1) : b.Op;
                int p = Prec(b);
                int lmin, rmin;
                switch (p)
                {
                    case 2: lmin = 2; rmin = 3; break;     // OR
                    case 3: lmin = 3; rmin = 4; break;     // AND
                    case 5: lmin = 5; rmin = 6; break;     // comparisons
                    case 6: lmin = 6; rmin = 8; break;     // + -
                    case 8: lmin = 8; rmin = 9; break;     // * /
                    default: lmin = 10; rmin = 12; break;  // **
                }
                string sep = (b.Op == "AND" || b.Op == "OR") ? " " + op + " " : op;
                return Expr(b.L, lmin) + sep + Expr(b.R, rmin);
            }
            return "?";
        }

        static string Slice(SliceArg a)
        {
            if (!a.IsRange) return Expr(a.From);
            return (a.From == null ? "" : Expr(a.From) + " ") + "TO" + (a.To == null ? "" : " " + Expr(a.To));
        }

        /// <summary>A string literal in the compiler's listing format.</summary>
        public static string Literal(byte[] codes)
        {
            var sb = new StringBuilder("\"");
            foreach (byte c in codes)
            {
                if (c == 0x0B) sb.Append("\"\"");
                else if (c == 0x00) sb.Append(' ');
                else if ((c >= 0x01 && c <= 0x07) || (c >= 0x80 && c <= 0x87))
                {
                    int mask = c < 8 ? c : (c ^ 0x8F);
                    char[] half = { ' ', '\'', '.', ':' };
                    sb.Append('\\').Append(half[(mask & 1) | ((mask & 4) >> 1)]).Append(half[((mask & 2) >> 1) | ((mask & 8) >> 2)]);
                }
                else if (c >= 0x0C && c < 0x40) sb.Append(Charset.Printable[c]);
                else if (c >= 0x8C && c < 0xC0) sb.Append('%').Append(Charset.Printable[c & 0x3F]);
                else sb.Append('\\').Append(c.ToString("X2"));
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Statement text (without the line number).</summary>
        public static string Stmt(Stmt s)
        {
            if (s is LetStmt) { var l = (LetStmt)s; return "LET " + Expr(l.Target) + "=" + Expr(l.Value); }
            if (s is PrintStmt)
            {
                var p = (PrintStmt)s;
                var sb = new StringBuilder("PRINT");
                bool needSep = false, first = true;
                foreach (var it in p.Items)
                {
                    if (it.Kind == PIKind.Comma) { sb.Append(first ? " ," : ","); needSep = false; first = false; continue; }
                    if (needSep) sb.Append(';');
                    sb.Append(first ? " " : "");
                    first = false;
                    switch (it.Kind)
                    {
                        case PIKind.At: sb.Append("AT " + Expr(it.A) + "," + Expr(it.B)); break;
                        case PIKind.Tab: sb.Append("TAB " + Expr(it.A)); break;
                        default: sb.Append(Expr(it.A)); break;
                    }
                    needSep = true;
                }
                if (!p.Newline && needSep) sb.Append(';');
                return sb.ToString();
            }
            if (s is IfStmt) { var f = (IfStmt)s; return "IF " + Expr(f.Cond) + " THEN " + Stmt(f.Then); }
            if (s is GotoStmt) { var g = (GotoStmt)s; return (g.Sub ? "GOSUB " : "GOTO ") + Expr(g.Target); }
            if (s is ReturnStmt) return "RETURN";
            if (s is ForStmt)
            {
                var f = (ForStmt)s;
                return "FOR " + f.Var + "=" + Expr(f.From) + " TO " + Expr(f.To) + (f.Step == null ? "" : " STEP " + Expr(f.Step));
            }
            if (s is NextStmt) return "NEXT " + ((NextStmt)s).Var;
            if (s is DimStmt)
            {
                var d = (DimStmt)s;
                return "DIM " + d.Name + (d.IsStr ? "$" : "") + "(" + string.Join(",", d.Dims) + ")";
            }
            if (s is InputStmt) return "INPUT " + Expr(((InputStmt)s).Target);
            if (s is PokeStmt) { var p = (PokeStmt)s; return "POKE " + Expr(p.Addr) + "," + Expr(p.Value); }
            if (s is PlotStmt) { var p = (PlotStmt)s; return (p.Unplot ? "UNPLOT " : "PLOT ") + Expr(p.X) + "," + Expr(p.Y); }
            if (s is PauseStmt) return "PAUSE " + Expr(((PauseStmt)s).N);
            if (s is RandStmt) { var r = (RandStmt)s; return r.N == null ? "RAND" : "RAND " + Expr(r.N); }
            if (s is RunStmt) { var r = (RunStmt)s; return r.N == null ? "RUN" : "RUN " + Expr(r.N); }
            if (s is SimpleStmt) { var k = ((SimpleStmt)s).Kind; return k == "NOP" ? "REM" : k; }
            return "REM ?";
        }
    }
}
