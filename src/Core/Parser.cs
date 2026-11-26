// =====================================================================================
//  Parser.cs - reading the BASIC text: the Lexer and the Parser
// -------------------------------------------------------------------------------------
//  Each BASIC line is handled in two steps:
//
//  1. Lexer: chops the text after the line number into Tokens, e.g.
//         PRINT AT 0,X+1;"HI"
//     becomes   [Kw PRINT] [Kw AT] [Num 0] [Op ,] [Ident X] [Op +] [Num 1] [Op ;] [Str "HI"] [End]
//     Strings are converted to ZX81 character codes here, including the \hh escapes.
//
//  2. Parser: reads the tokens and builds the tree of Stmt/Expr objects (Ast.cs). It is
//     a "recursive descent" parser: there is one method per level of operator
//     priority, and each calls the next-higher level for its operands. Priorities,
//     lowest first (these follow Sinclair BASIC):
//
//         OR                         ParseOr
//         AND                        ParseAnd
//         NOT                        ParseNot       (so NOT A=B means NOT (A=B))
//         = <> < > <= >=             ParseCompare
//         + -                        ParseAdd
//         * /                        ParseMul
//         unary minus                ParseUnary
//         **                         ParsePow
//         numbers, variables, (..),  ParsePrimary
//         functions
//
//     A function takes the single "primary" after it as its argument, as in Sinclair
//     BASIC, so  PEEK 16396+256*PEEK 16397  means  PEEK(16396) + 256*PEEK(16397).
//
//  The parser also checks types (number vs string) and reports errors as CompileError
//  exceptions, which Compiler.cs catches and turns into messages.
//
//  Numbers: whole numbers -32768..65535 become NumLit; anything else (0.5, 1.0, 1E6)
//  becomes FloatLit, which makes the expression around it floating point (see
//  FloatMath in Ast.cs). Fold works out constant parts at compile time, in whole
//  numbers or floating point to match how the code would do it.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>An error in the BASIC program. Line is the BASIC line number.</summary>
    public class CompileError : Exception
    {
        public int Line;
        public CompileError(int line, string msg) : base(msg) { Line = line; }
    }

    /// <summary>One error or warning message shown to the user.</summary>
    public class Diagnostic
    {
        public bool IsError;   // true = error (no .p file is produced), false = warning
        public int BasicLine;  // ZX81 line number, -1 if none
        public int SourceLine; // line in the text file
        public string Message;
        /// <summary>The text shown in the message list, e.g. "ERROR  Line 20: missing closing quote".</summary>
        public override string ToString()
        {
            string where = BasicLine >= 0 ? "Line " + BasicLine + ": " : (SourceLine > 0 ? "Text line " + SourceLine + ": " : "");
            return (IsError ? "ERROR  " : "Warning ") + where + Message;
        }
    }

    /// <summary>Token Kind.</summary>
    public enum TK
    {
        Num,       // a number, value in Token.Num
        Str,       // a "string", ZX81 codes in Token.Str
        Ident,     // a numeric variable name, e.g. SCORE
        StrIdent,  // a string variable letter, e.g. A for A$
        Kw,        // a keyword, e.g. PRINT or CHR$
        Op,        // an operator or punctuation: + - * / ** = <> < > <= >= ( ) , ;
        End        // the end of the line (always the last token)
    }

    /// <summary>One token produced by the Lexer.</summary>
    public class Token
    {
        public TK Kind;
        public string Text;     // upper-case keyword / identifier / operator
        public double Num;      // value of a number token
        public byte[] Str;      // ZX81 codes of a string token
        public override string ToString() { return Kind == TK.Str ? "string" : Text; }
    }

    // =================================================================================
    //  LEXER
    // =================================================================================
    public class Lexer
    {
        /// <summary>All the words recognised as keywords (GO TO / GO SUB are joined to GOTO / GOSUB).</summary>
        public static readonly HashSet<string> Keywords = new HashSet<string>(new[] {
            "LET","PRINT","IF","THEN","GOTO","GOSUB","RETURN","FOR","TO","STEP","NEXT","REM","CLS","PAUSE","RAND",
            "POKE","PLOT","UNPLOT","SCROLL","INPUT","STOP","DIM","CLEAR","RUN","FAST","SLOW","LIST","NEW","CONT",
            "SAVE","LOAD","LPRINT","LLIST","COPY","ABS","SGN","INT","SQR","PEEK","USR","CODE","LEN","VAL","CHR$",
            "STR$","INKEY$","RND","PI","NOT","AND","OR","AT","TAB","SIN","COS","TAN","ASN","ACS","ATN","LN","EXP" });

        readonly string s;            // the text being read (the line without its number)
        int pos;                      // current position in s
        readonly int line;            // BASIC line number, for error messages
        readonly bool lowerInverse;   // option: lower case in strings = inverse video
        /// <summary>The tokens found so far.</summary>
        public List<Token> Tokens = new List<Token>();
        /// <summary>Warnings found while reading (e.g. a character with no ZX81 equivalent).</summary>
        public List<string> Warnings = new List<string>();

        public Lexer(string text, int lineNo, bool lowerIsInverse)
        {
            s = text; line = lineNo; lowerInverse = lowerIsInverse;
        }

        CompileError Err(string m) { return new CompileError(line, m); }

        /// <summary>Read the whole line and return its tokens, ending with an End token.</summary>
        public List<Token> Run()
        {
            while (true)
            {
                while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                if (pos >= s.Length) break;
                char c = s[pos];
                if (c == '"') { ReadString(); continue; }
                if (char.IsDigit(c) || (c == '.' && pos + 1 < s.Length && char.IsDigit(s[pos + 1]))) { ReadNumber(); continue; }
                // A word. After REM the rest of the line is a comment, so stop reading.
                if (char.IsLetter(c)) { ReadWord(); if (Tokens.Count > 0 && Tokens[Tokens.Count - 1].Text == "REM") break; continue; }
                // Two-character operators must be checked before one-character ones
                string two = pos + 1 < s.Length ? s.Substring(pos, 2) : "";
                if (two == "**" || two == "<=" || two == ">=" || two == "<>")
                {
                    Tokens.Add(new Token { Kind = TK.Op, Text = two }); pos += 2; continue;
                }
                if ("+-*/=<>(),;".IndexOf(c) >= 0)
                {
                    Tokens.Add(new Token { Kind = TK.Op, Text = c.ToString() }); pos++; continue;
                }
                if (c == ':') throw Err("':' is not a ZX81 statement separator - use one statement per line");
                throw Err("unexpected character '" + c + "'");
            }
            Tokens.Add(new Token { Kind = TK.End, Text = "<end of line>" });
            return Tokens;
        }

        /// <summary>Read a number such as 42, 1.5, .5 or 4E4.</summary>
        void ReadNumber()
        {
            int st = pos;
            while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            if (pos < s.Length && s[pos] == '.') { pos++; while (pos < s.Length && char.IsDigit(s[pos])) pos++; }
            if (pos < s.Length && (s[pos] == 'E' || s[pos] == 'e'))
            {
                // Exponent: only if digits follow the E (optionally signed); otherwise the E
                // is not part of the number.
                int save = pos; pos++;
                if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                if (pos < s.Length && char.IsDigit(s[pos])) { while (pos < s.Length && char.IsDigit(s[pos])) pos++; }
                else pos = save;
            }
            string t = s.Substring(st, pos - st);
            double v = double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture);
            Tokens.Add(new Token { Kind = TK.Num, Num = v, Text = t });
        }

        /// <summary>Read a word: a keyword, a variable name, or a keyword stuck to a number.</summary>
        void ReadWord()
        {
            int st = pos;
            while (pos < s.Length && char.IsLetterOrDigit(s[pos])) pos++;
            string w = s.Substring(st, pos - st).ToUpperInvariant();
            bool dollar = pos < s.Length && s[pos] == '$';
            // CHR$ STR$ INKEY$
            if (dollar && Keywords.Contains(w + "$")) { pos++; Tokens.Add(new Token { Kind = TK.Kw, Text = w + "$" }); return; }
            // ZX81 listings show GOTO and GOSUB as two words: GO TO, GO SUB
            if (w == "GO")
            {
                int p = pos; while (p < s.Length && s[p] == ' ') p++;
                int q = p; while (q < s.Length && char.IsLetter(s[q])) q++;
                string nxt = s.Substring(p, q - p).ToUpperInvariant();
                if (nxt == "TO" || nxt == "SUB") { pos = q; Tokens.Add(new Token { Kind = TK.Kw, Text = "GO" + nxt }); return; }
            }
            if (Keywords.Contains(w)) { Tokens.Add(new Token { Kind = TK.Kw, Text = w }); return; }
            // A$ .. Z$
            if (dollar)
            {
                pos++;
                if (w.Length != 1) throw Err("string variable names must be a single letter: " + w + "$");
                Tokens.Add(new Token { Kind = TK.StrIdent, Text = w });
                return;
            }
            // keyword immediately followed by digits, e.g. GOTO100
            int d = w.Length; while (d > 0 && char.IsDigit(w[d - 1])) d--;
            if (d < w.Length && d > 0 && Keywords.Contains(w.Substring(0, d)))
            {
                Tokens.Add(new Token { Kind = TK.Kw, Text = w.Substring(0, d) });
                pos = st + d;   // re-read the digits as a number token
                return;
            }
            // Anything else is a numeric variable name
            Tokens.Add(new Token { Kind = TK.Ident, Text = w });
        }

        /// <summary>Read a "quoted string", converting it to ZX81 codes. Handles:
        ///   ""          a quote character
        ///   \hh         a ZX81 code in hex (e.g. \85)
        ///   \'. etc.    block graphics (two of ' . : space)
        ///   \\          a slash (the ZX81 has no backslash)
        ///   %A          inverse A
        ///   anything else - converted by Charset.FromChar</summary>
        void ReadString()
        {
            pos++; // opening quote
            var codes = new List<byte>();
            while (true)
            {
                if (pos >= s.Length) throw Err("missing closing quote");
                char c = s[pos];
                if (c == '"')
                {
                    if (pos + 1 < s.Length && s[pos + 1] == '"') { codes.Add(0x0B); pos += 2; continue; }
                    pos++; break;   // closing quote
                }
                if (c == '\\')
                {
                    // \hh - two hex digits
                    if (pos + 2 < s.Length && Uri.IsHexDigit(s[pos + 1]) && Uri.IsHexDigit(s[pos + 2]))
                    {
                        int code = int.Parse(s.Substring(pos + 1, 2), NumberStyles.HexNumber);
                        foreach (int x in Charset.ExpandForString(code, lowerInverse)) codes.Add((byte)x);
                        if (!Charset.IsDisplayable(code) && code != 0xC0)
                            Warnings.Add("code \\" + s.Substring(pos + 1, 2) + " is a keyword/control code; expanded to text");
                        pos += 3; continue;
                    }
                    // block graphic escape
                    if (pos + 2 < s.Length)
                    {
                        int g = Charset.BlockFromEscape(s[pos + 1], s[pos + 2]);
                        if (g >= 0) { codes.Add((byte)g); pos += 3; continue; }
                    }
                    if (pos + 1 < s.Length && s[pos + 1] == '\\') { codes.Add(0x18); pos += 2; continue; }
                    throw Err("unknown escape '\\" + (pos + 1 < s.Length ? s.Substring(pos + 1, Math.Min(2, s.Length - pos - 1)) : "") + "' in string");
                }
                // %X = inverse X (ZXText2P style)
                if (c == '%' && pos + 1 < s.Length)
                {
                    int code = Charset.FromChar(s[pos + 1], false);
                    if (code >= 0) { codes.Add((byte)(code | 0x80)); pos += 2; continue; }
                }
                // an ordinary character
                int z = Charset.FromChar(c, lowerInverse);
                if (z < 0) { Warnings.Add("character '" + c + "' has no ZX81 equivalent, using '?'"); z = 0x0F; }
                codes.Add((byte)z);
                pos++;
            }
            Tokens.Add(new Token { Kind = TK.Str, Str = codes.ToArray(), Text = "string" });
        }
    }

    // =================================================================================
    //  PARSER
    // =================================================================================
    public class Parser
    {
        List<Token> t;   // the tokens of the line being parsed
        int p;           // index of the next token to read
        int line;        // BASIC line number, for error messages and AST nodes
        /// <summary>Warnings (e.g. ignored statements, numbers rounded down).</summary>
        public List<string> Warnings = new List<string>();

        // ---- small helpers for reading tokens ----
        CompileError Err(string m) { return new CompileError(line, m); }
        /// <summary>The next token, without moving past it.</summary>
        Token Peek { get { return t[p]; } }
        /// <summary>The next token, moving past it.</summary>
        Token Next() { return t[p++]; }
        bool IsOp(string o) { return Peek.Kind == TK.Op && Peek.Text == o; }
        bool IsKw(string k) { return Peek.Kind == TK.Kw && Peek.Text == k; }
        /// <summary>If the next token is operator o, move past it and return true.</summary>
        bool AcceptOp(string o) { if (IsOp(o)) { p++; return true; } return false; }
        bool AcceptKw(string k) { if (IsKw(k)) { p++; return true; } return false; }
        /// <summary>The next token must be operator o, otherwise it's an error.</summary>
        void ExpectOp(string o) { if (!AcceptOp(o)) throw Err("expected '" + o + "' but found " + Peek); }
        void ExpectKw(string k) { if (!AcceptKw(k)) throw Err("expected " + k + " but found " + Peek); }

        /// <summary>Parse one line's tokens into a statement. The whole line must be used up.</summary>
        public Stmt ParseLine(List<Token> toks, int lineNo)
        {
            t = toks; p = 0; line = lineNo;
            Stmt s = ParseStatement();
            if (Peek.Kind != TK.End) throw Err("unexpected " + Peek + " after statement");
            return s;
        }

        /// <summary>Parse a statement and record its line number.</summary>
        Stmt ParseStatement()
        {
            Token k = Peek;
            Stmt st = ParseStatementInner();
            st.Line = line;
            return st;
        }

        /// <summary>Parse a statement, choosing what to do from its first keyword.</summary>
        Stmt ParseStatementInner()
        {
            Token k = Peek;
            if (k.Kind == TK.Ident || k.Kind == TK.StrIdent)
            {
                // implicit LET
                return ParseLet();
            }
            if (k.Kind != TK.Kw) throw Err("statement expected, found " + k);
            p++;
            switch (k.Text)
            {
                case "REM": return new SimpleStmt("NOP");
                case "LET": return ParseLet();
                case "PRINT": return ParsePrint();
                case "IF":
                    {
                        // IF condition THEN statement      or      IF condition THEN line-number
                        var s = new IfStmt();
                        s.Cond = Num(ParseExpr());
                        ExpectKw("THEN");
                        if (Peek.Kind == TK.Num) { var g = new GotoStmt(); g.Target = ParseExpr(); g.Line = line; s.Then = g; }
                        else { s.Then = ParseStatement(); }
                        return s;
                    }
                case "GOTO":
                case "GOSUB":
                    return new GotoStmt { Target = Num(ParseExpr()), Sub = k.Text == "GOSUB" };
                case "RETURN": return new ReturnStmt();
                case "FOR":
                    {
                        // FOR v = from TO to [STEP step]
                        var f = new ForStmt();
                        Token v = Next();
                        if (v.Kind != TK.Ident || v.Text.Length != 1) throw Err("FOR needs a single-letter numeric variable");
                        f.Var = v.Text[0];
                        ExpectOp("=");
                        f.From = Num(ParseExpr());
                        ExpectKw("TO");
                        f.To = Num(ParseExpr());
                        if (AcceptKw("STEP")) f.Step = Num(ParseExpr());
                        return f;
                    }
                case "NEXT":
                    {
                        Token v = Next();
                        if (v.Kind != TK.Ident || v.Text.Length != 1) throw Err("NEXT needs a single-letter numeric variable");
                        return new NextStmt { Var = v.Text[0] };
                    }
                case "DIM":
                    {
                        // DIM A(n[,m]) or DIM A$(n[,m]). Sizes must work out to constants, because
                        // the compiler reserves the memory in advance.
                        var d = new DimStmt();
                        Token v = Next();
                        if ((v.Kind != TK.Ident && v.Kind != TK.StrIdent) || v.Text.Length != 1) throw Err("DIM needs a single-letter array name");
                        d.Name = v.Text[0]; d.IsStr = v.Kind == TK.StrIdent;
                        ExpectOp("(");
                        do
                        {
                            Expr e = Fold(Num(ParseExpr()));
                            var lit = e as NumLit;
                            if (lit == null) throw Err("DIM sizes must be constant numbers in compiled programs");
                            if (lit.Value < 1) throw Err("DIM size must be at least 1");
                            d.Dims.Add(lit.Value);
                        } while (AcceptOp(","));
                        ExpectOp(")");
                        if (d.Dims.Count > 2) throw Err("only 1 or 2 dimensional arrays are supported");
                        return d;
                    }
                case "INPUT":
                    {
                        var e = ParsePrimaryTarget();
                        return new InputStmt { Target = e };
                    }
                case "POKE":
                    {
                        var s = new PokeStmt(); s.Addr = Num(ParseExpr()); ExpectOp(","); s.Value = Num(ParseExpr()); return s;
                    }
                case "PLOT":
                case "UNPLOT":
                    {
                        var s = new PlotStmt(); s.Unplot = k.Text == "UNPLOT";
                        s.X = Num(ParseExpr()); ExpectOp(","); s.Y = Num(ParseExpr()); return s;
                    }
                case "PAUSE": return new PauseStmt { N = Num(ParseExpr()) };
                case "RAND":
                    return new RandStmt { N = Peek.Kind == TK.End ? null : Num(ParseExpr()) };
                case "RUN":
                    return new RunStmt { N = Peek.Kind == TK.End ? null : Num(ParseExpr()) };
                case "CLS":
                case "SCROLL":
                case "STOP":
                case "CLEAR":
                case "FAST":
                case "SLOW":
                    return new SimpleStmt(k.Text);
                case "COPY":    // copy the screen to the ZX printer (ROM routine)
                case "NEW":     // wipe the program, as on the ZX81 (ROM routine)
                    return new SimpleStmt(k.Text);
                case "LPRINT":
                    {
                        var lp = (PrintStmt)ParsePrint();
                        lp.Printer = true;
                        return lp;
                    }
                case "SAVE":
                case "LOAD":
                    return new TapeStmt { Name = Str(ParseExpr()), Load = k.Text == "LOAD" };
                // Statements with nothing to do in a compiled program
                case "LIST":
                case "LLIST":
                    if (Peek.Kind != TK.End) Num(ParseExpr());
                    Warnings.Add(k.Text + " does nothing in a compiled program: the only BASIC in memory is the machine code line, so there is nothing to list");
                    return new SimpleStmt("NOP");
                case "CONT":
                    Warnings.Add("CONT does nothing inside a compiled program (it continues a stopped BASIC program, typed as a command)");
                    return new SimpleStmt("NOP");
            }
            throw Err(k.Text + " cannot start a statement");
        }

        /// <summary>Parse something that can be assigned to (LET / INPUT):
        /// X, A(3), A$, A$(2 TO 4) ...</summary>
        Expr ParsePrimaryTarget()
        {
            Token v = Peek;
            if (v.Kind == TK.Ident || v.Kind == TK.StrIdent)
            {
                Expr e = ParsePrimary();
                if (e is NumVar || e is NumArr || e is StrVar || e is SliceExpr) return e;
            }
            throw Err("variable expected");
        }

        /// <summary>[LET] target = value</summary>
        Stmt ParseLet()
        {
            var s = new LetStmt();
            s.Target = ParsePrimaryTarget();
            ExpectOp("=");
            s.Value = ParseExpr();
            if (s.Value.Type != s.Target.Type) throw Err("type mismatch in LET (number vs string)");
            return s;
        }

        /// <summary>PRINT [item {; or , item}] [; or ,]
        /// A trailing ; or , means "don't start a new line afterwards".</summary>
        Stmt ParsePrint()
        {
            var s = new PrintStmt();
            bool lastSep = false;   // was the last thing read a ; or , ?
            while (Peek.Kind != TK.End)
            {
                if (AcceptOp(";")) { lastSep = true; continue; }
                if (AcceptOp(",")) { s.Items.Add(new PrintItem { Kind = PIKind.Comma }); lastSep = true; continue; }
                lastSep = false;
                if (AcceptKw("AT"))
                {
                    var it = new PrintItem { Kind = PIKind.At };
                    it.A = Num(ParseExpr()); ExpectOp(","); it.B = Num(ParseExpr());
                    s.Items.Add(it);
                }
                else if (AcceptKw("TAB"))
                {
                    s.Items.Add(new PrintItem { Kind = PIKind.Tab, A = Num(ParseExpr()) });
                }
                else
                {
                    s.Items.Add(new PrintItem { Kind = PIKind.Expr, A = ParseExpr() });
                }
                if (Peek.Kind != TK.End && !IsOp(";") && !IsOp(","))
                {
                    // ZX81 allows items with no separator after AT/TAB in some listings; be strict otherwise
                    throw Err("expected ; or , in PRINT but found " + Peek);
                }
            }
            s.Newline = !lastSep;
            return s;
        }

        /// <summary>Type checks: the expression must be a number / a string, else an error.
        /// They return the expression so they can wrap a parse call.</summary>
        Expr Num(Expr e)
        {
            if (e.Type != VT.Num) throw Err("number expected but found a string expression");
            return e;
        }
        Expr Str(Expr e)
        {
            if (e.Type != VT.Str) throw Err("string expected but found a number expression");
            return e;
        }

        // ---------------- expressions ----------------
        // Each method handles one priority level (see the table at the top of the file).

        /// <summary>Parse a complete expression and simplify any constant parts.</summary>
        public Expr ParseExpr() { var e = ParseOr(); return Fold(e); }

        Expr ParseOr()
        {
            Expr l = ParseAnd();
            while (AcceptKw("OR"))
            {
                Expr r = Num(ParseAnd());
                if (l.Type != VT.Num) throw Err("OR needs numbers");
                l = Mk(new Binary("OR", l, r, VT.Num));
            }
            return l;
        }

        Expr ParseAnd()
        {
            Expr l = ParseNot();
            while (AcceptKw("AND"))
            {
                Expr r = Num(ParseNot());
                l = Mk(new Binary("AND", l, r, l.Type)); // A$ AND cond is allowed
            }
            return l;
        }

        Expr ParseNot()
        {
            if (AcceptKw("NOT")) return Mk(new Unary("NOT", Num(ParseNot())));
            return ParseCompare();
        }

        static readonly string[] CmpOps = { "=", "<>", "<", ">", "<=", ">=" };

        /// <summary>Comparisons. Comparing two strings gives an "S" operator (e.g. "S=")
        /// so the code generator knows to compare text rather than numbers.</summary>
        Expr ParseCompare()
        {
            Expr l = ParseAdd();
            while (Peek.Kind == TK.Op && Array.IndexOf(CmpOps, Peek.Text) >= 0)
            {
                string op = Next().Text;
                Expr r = ParseAdd();
                if (l.Type != r.Type) throw Err("cannot compare a string with a number");
                l = Mk(new Binary(l.Type == VT.Str ? "S" + op : op, l, r, VT.Num));
            }
            return l;
        }

        /// <summary>+ and -. A + between two strings joins them (CAT).</summary>
        Expr ParseAdd()
        {
            Expr l = ParseMul();
            while (IsOp("+") || IsOp("-"))
            {
                string op = Next().Text;
                Expr r = ParseMul();
                if (l.Type == VT.Str || r.Type == VT.Str)
                {
                    if (op != "+" || l.Type != r.Type) throw Err("strings can only be joined with +");
                    l = Mk(new Binary("CAT", l, r, VT.Str));
                }
                else l = Mk(new Binary(op, l, r, VT.Num));
            }
            return l;
        }

        /// <summary>* and /. RND*n (or n*RND) is turned into a single RndN node here.</summary>
        Expr ParseMul()
        {
            Expr l = ParseUnary();
            while (IsOp("*") || IsOp("/"))
            {
                string op = Next().Text;
                Expr r = ParseUnary();
                Num(l); Num(r);
                if (op == "*" && l is RndBare) { l = Mk(new RndN(r)); continue; }
                if (op == "*" && r is RndBare) { l = Mk(new RndN(l)); continue; }
                l = Mk(new Binary(op, l, r, VT.Num));
            }
            return l;
        }

        Expr ParseUnary()
        {
            if (AcceptOp("-")) return Mk(new Unary("NEG", Num(ParseUnary())));
            if (AcceptOp("+")) return Num(ParseUnary());
            return ParsePow();
        }

        /// <summary>A ** B (raise to a power).</summary>
        Expr ParsePow()
        {
            Expr l = ParsePrimary();
            while (AcceptOp("**"))
            {
                Expr r = ParseFuncArg();
                l = Mk(new Binary("**", Num(l), Num(r), VT.Num));
            }
            return l;
        }

        /// <summary>The argument of a function: a primary, optionally with a minus sign (ABS -5).</summary>
        Expr ParseFuncArg()
        {
            if (AcceptOp("-")) return Mk(new Unary("NEG", Num(ParseFuncArg())));
            return ParsePrimary();
        }

        /// <summary>Stamp the current line number on a new node and return it.</summary>
        T Mk<T>(T e) where T : Expr { e.Line = line; return e; }

        /// <summary>A primary, plus any slices after a string: "ABC"(2), A$(1)(2 TO 3).</summary>
        Expr ParsePrimary()
        {
            Expr e = ParsePrimaryNoSlice();
            if (e.Type == VT.Str)
            {
                while (IsOp("("))
                {
                    p++;
                    var a = ParseSliceArg();
                    ExpectOp(")");
                    e = Mk(new SliceExpr(e, a));
                }
            }
            return e;
        }

        /// <summary>One subscript: n, n TO m, TO m, n TO, or just TO (see SliceArg).</summary>
        SliceArg ParseSliceArg()
        {
            var a = new SliceArg();
            if (AcceptKw("TO"))
            {
                a.IsRange = true;
                if (!IsOp(")") && !IsOp(",")) a.To = Num(ParseExpr());
                return a;
            }
            a.From = Num(ParseExpr());
            if (AcceptKw("TO"))
            {
                a.IsRange = true;
                if (!IsOp(")") && !IsOp(",")) a.To = Num(ParseExpr());
            }
            else a.To = a.From;
            return a;
        }

        /// <summary>The simplest building blocks of an expression: numbers, strings,
        /// variables, array elements, (bracketed expressions) and functions.</summary>
        Expr ParsePrimaryNoSlice()
        {
            Token k = Next();
            switch (k.Kind)
            {
                case TK.Num:
                    // Whole numbers -32768..65535 are NumLits; anything else (0.5, 1E6 ...)
                    // is a FloatLit, which makes the expression around it floating point.
                    // A number written with a decimal point (1.0) is a FloatLit too, so
                    // 7/2*1.0 is 3.5, as on the ZX81.
                    if (k.Text.IndexOf('.') >= 0) return Mk(new FloatLit(k.Num));
                    return Mk(FloatMath.Const(k.Num));
                case TK.Str:
                    return Mk(new StrLit(k.Str));
                case TK.Ident:
                    // A(..) is an array element; a bare name is a simple variable
                    if (IsOp("("))
                    {
                        if (k.Text.Length != 1) throw Err("array names must be a single letter");
                        p++;
                        var idx = new List<Expr>();
                        do { idx.Add(Num(ParseExpr())); } while (AcceptOp(","));
                        ExpectOp(")");
                        if (idx.Count > 2) throw Err("only 1 or 2 dimensional arrays are supported");
                        return Mk(new NumArr(k.Text[0], idx));
                    }
                    return Mk(new NumVar(k.Text));
                case TK.StrIdent:
                    {
                        // A$ with optional (subscripts) - their meaning is worked out in CodeGen
                        var args = new List<SliceArg>();
                        if (IsOp("("))
                        {
                            p++;
                            do { args.Add(ParseSliceArg()); } while (AcceptOp(","));
                            ExpectOp(")");
                            if (args.Count > 2) throw Err("too many subscripts");
                        }
                        return Mk(new StrVar(k.Text[0], args));
                    }
                case TK.Op:
                    if (k.Text == "(")
                    {
                        Expr e = ParseOr();
                        ExpectOp(")");
                        return e;
                    }
                    break;
                case TK.Kw:
                    switch (k.Text)
                    {
                        case "RND": return Mk(new RndBare());
                        case "INKEY$": return Mk(new Inkey());
                        case "PI": return Mk(new PiConst());
                        // number -> number
                        case "ABS":
                        case "SGN":
                        case "INT":
                        case "SQR":
                        case "PEEK":
                        case "USR":
                            return Mk(new Func(k.Text, Num(ParseFuncArg()), VT.Num));
                        // number -> string
                        case "CHR$":
                        case "STR$":
                            return Mk(new Func(k.Text, Num(ParseFuncArg()), VT.Str));
                        // string -> number
                        case "CODE":
                        case "LEN":
                        case "VAL":
                            return Mk(new Func(k.Text, Str(ParsePrimary()), VT.Num));
                        case "NOT":
                            return Mk(new Unary("NOT", Num(ParseNot())));
                        // number -> number, worked out by the ROM's floating-point calculator
                        case "SIN":
                        case "COS":
                        case "TAN":
                        case "ASN":
                        case "ACS":
                        case "ATN":
                        case "LN":
                        case "EXP":
                            return Mk(new Func(k.Text, Num(ParseFuncArg()), VT.Num));
                    }
                    break;
            }
            throw Err("unexpected " + k + " in expression");
        }

        // ---------------- constant folding ----------------
        /// <summary>Work out at compile time any part of an expression that only uses
        /// constants, e.g. 2*16+1 becomes 33. Whole-number parts use signed 16-bit
        /// arithmetic like the compiled code (so 7/2 is 3); parts inside a float island
        /// (see FloatMath) use floating point like the ZX81 (so SIN (7/2) is SIN 3.5).
        /// Division by zero is left alone (so it happens at run time).</summary>
        public static Expr Fold(Expr e) { return Fold(e, false); }

        static bool IsConst(Expr e) { return e is NumLit || e is FloatLit; }

        /// <summary>Fold e; fl = e is inside a float island.</summary>
        static Expr Fold(Expr e, bool fl)
        {
            bool here = fl || FloatMath.IsFloat(e);    // is this node worked out in floating point?
            var b = e as Binary;
            if (b != null)
            {
                bool numeric = b.L.Type == VT.Num && Array.IndexOf(FloatMath.PassOps, b.Op) >= 0;
                b.L = Fold(b.L, numeric && here);
                b.R = Fold(b.R, numeric && here);
                if (!numeric || !IsConst(b.L) || !IsConst(b.R)) return e;
                var l = b.L as NumLit; var r = b.R as NumLit;
                bool useFloat = here || l == null || r == null || l.Unsigned || r.Unsigned;
                if (useFloat)
                {
                    // floating point, as the ZX81 would
                    double x = FloatMath.Value(b.L), y = FloatMath.Value(b.R), fv;
                    switch (b.Op)
                    {
                        case "+": fv = x + y; break;
                        case "-": fv = x - y; break;
                        case "*": fv = x * y; break;
                        case "/": if (y == 0) return e; fv = x / y; break;
                        case "=": fv = x == y ? 1 : 0; break;
                        case "<>": fv = x != y ? 1 : 0; break;
                        case "<": fv = x < y ? 1 : 0; break;
                        case ">": fv = x > y ? 1 : 0; break;
                        case "<=": fv = x <= y ? 1 : 0; break;
                        case ">=": fv = x >= y ? 1 : 0; break;
                        default: return e;
                    }
                    if (double.IsInfinity(fv) || double.IsNaN(fv) || Math.Abs(fv) > 1e38) return e;   // leave it for the ROM to report
                    var c = FloatMath.Const(fv); c.Line = e.Line; return c;
                }
                int xi = (short)l.Value, yi = (short)r.Value, v;   // (short) gives the signed value
                switch (b.Op)
                {
                    case "+": v = xi + yi; break;
                    case "-": v = xi - yi; break;
                    case "*": v = xi * yi; break;
                    case "/": if (yi == 0) return e; v = xi / yi; break;
                    case "=": v = xi == yi ? 1 : 0; break;
                    case "<>": v = xi != yi ? 1 : 0; break;
                    case "<": v = xi < yi ? 1 : 0; break;
                    case ">": v = xi > yi ? 1 : 0; break;
                    case "<=": v = xi <= yi ? 1 : 0; break;
                    case ">=": v = xi >= yi ? 1 : 0; break;
                    default: return e;
                }
                var n = new NumLit(v & 0xFFFF); n.Line = e.Line; return n;
            }
            var u = e as Unary;
            if (u != null)
            {
                u.A = Fold(u.A, here);
                if (!IsConst(u.A)) return e;
                var a = u.A as NumLit;
                Expr res;
                if (u.Op == "NEG")
                    res = (here || a == null || a.Unsigned) ? FloatMath.Const(-FloatMath.Value(u.A)) : new NumLit(-(short)a.Value & 0xFFFF);
                else
                    res = new NumLit(FloatMath.Value(u.A) == 0 ? 1 : 0);
                res.Line = e.Line;
                return res;
            }
            var f = e as Func;
            if (f != null && f.Arg != null)
            {
                // SIN etc. work on a float argument; ABS SGN INT SQR follow their context;
                // everything else (PEEK, CHR$ ...) wants a whole number
                bool argFloat = Array.IndexOf(FloatMath.FloatFuncs, f.Name) >= 0 ? f.Name != "VAL"
                              : Array.IndexOf(FloatMath.PassFuncs, f.Name) >= 0 && here;
                f.Arg = Fold(f.Arg, argFloat);
                // INT of a whole-number constant, in whole-number code: just the constant
                // (INT makes no code there, so 39+56 and INT 39+56 must fold alike)
                if (f.Name == "INT" && !here && f.Arg is NumLit) return f.Arg;
            }
            var rn = e as RndN;
            if (rn != null) rn.N = Fold(rn.N, here);
            return e;
        }
    }
}
