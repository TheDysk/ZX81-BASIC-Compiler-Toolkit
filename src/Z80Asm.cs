// =====================================================================================
//  Z80Asm.cs - a small two-pass Z80 assembler
// -------------------------------------------------------------------------------------
//  The code generator and the runtime library produce Z80 assembly language as text
//  lines, e.g.
//
//        rt_cls:              <- a label
//            LD HL,(D_FILE)   <- an instruction (lines added with Op() start with a TAB)
//            INC HL
//        PR_ROW EQU BSS+0     <- a named constant
//
//  This class turns those lines into machine code bytes.
//
//  Why two passes? An instruction can refer to a label that is defined further down
//  (e.g. "JP L_100" before line 100 has been reached). Pass 1 walks all the lines just
//  to work out the address of every label; unknown values are treated as 0 for now.
//  Pass 2 walks them again, and this time every label is known, so the real bytes are
//  produced. This works because every Z80 instruction we use has the same size
//  whatever its operand value is.
//
//  Only the instructions this compiler needs are supported (no IX/IY instructions,
//  for example - the ZX81 display needs those registers left alone).
//
//  Expression syntax for operands: numbers (decimal, 0FFh, $FF, 0xFF), labels,
//  $ (the current address), + - * / and brackets.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>Thrown for any assembly error (unknown instruction, undefined label,
    /// relative jump too far, ...). The compiler shows the message to the user.</summary>
    public class AsmException : Exception
    {
        public AsmException(string msg) : base(msg) { }
    }

    public class Z80Asm
    {
        /// <summary>The source lines, in order, as added by Line/Label/Op/Comment.</summary>
        readonly List<string> lines = new List<string>();
        /// <summary>Label and EQU values. Case-insensitive, so "rt_cls" and "RT_CLS" are the same.</summary>
        readonly Dictionary<string, int> symbols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The machine code produced by the current pass.</summary>
        List<byte> output;
        /// <summary>Program counter: the address the next byte will be placed at.</summary>
        int pc;
        /// <summary>Address of the first byte (16514 for our REM line).</summary>
        int origin;
        /// <summary>False during pass 1 (just collecting labels), true during pass 2.</summary>
        bool finalPass;
        /// <summary>The assembly listing (address, bytes, source) when requested, else null.</summary>
        public StringBuilder Listing;

        // ---------------- adding source lines ----------------

        /// <summary>Add a raw line (used for runtime library code and EQU lines).</summary>
        public void Line(string s) { lines.Add(s); }
        /// <summary>Add a label definition, e.g. Label("L_10") adds "L_10:".</summary>
        public void Label(string name) { lines.Add(name + ":"); }
        /// <summary>Add an instruction, e.g. Op("LD HL,5"). The TAB only makes the listing tidy.</summary>
        public void Op(string s) { lines.Add("\t" + s); }
        /// <summary>Add a comment line to the listing.</summary>
        public void Comment(string s) { lines.Add("; " + s); }
        public int LineCount { get { return lines.Count; } }

        /// <summary>All labels and EQUs with their values (after assembling).</summary>
        public IDictionary<string, int> Symbols { get { return symbols; } }

        /// <summary>Define a symbol from outside, e.g. Define("BSS", 0x5200).</summary>
        public void Define(string name, int value) { symbols[name] = value; }

        // ---------------- assembling ----------------

        /// <summary>Assemble all the lines, starting at address org, and return the bytes.
        /// If makeListing is true, Listing is filled in during the second pass.</summary>
        public byte[] Assemble(int org, bool makeListing)
        {
            origin = org;
            finalPass = false;
            RunPass(false);          // pass 1: find label addresses
            finalPass = true;
            Listing = makeListing ? new StringBuilder() : null;
            RunPass(makeListing);    // pass 2: produce the final bytes
            return output.ToArray();
        }

        /// <summary>Process every source line once, optionally writing the listing.</summary>
        void RunPass(bool list)
        {
            output = new List<byte>();
            pc = origin;
            for (int i = 0; i < lines.Count; i++)
            {
                string raw = lines[i];
                int startLen = output.Count;   // so we know which bytes this line produced
                int startPc = pc;
                try { DoLine(raw); }
                catch (AsmException e) { throw new AsmException("Assembler: " + e.Message + " in: " + raw.Trim()); }
                if (list)
                {
                    // Listing line: "4082  ED 73 34 4C     LD (ENTRYSP),SP"
                    var sb = new StringBuilder();
                    sb.Append(startPc.ToString("X4")).Append("  ");
                    int n = output.Count - startLen;
                    for (int k = 0; k < Math.Min(n, 4); k++) sb.Append(output[startLen + k].ToString("X2")).Append(' ');
                    if (n > 4) sb.Append(".. ");
                    sb.Append(' ', Math.Max(0, 20 - sb.Length));
                    sb.Append(raw.Replace("\t", "        "));
                    Listing.AppendLine(sb.ToString());
                }
            }
        }

        /// <summary>Remove a ';' comment from a line. A quote after a letter (as in AF') is
        /// not treated as the start of a quoted string.</summary>
        static string StripComment(string s)
        {
            bool inq = false;   // inside a '...' quoted character?
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\'' && !(i > 0 && char.IsLetter(s[i - 1]))) inq = !inq;
                if (s[i] == ';' && !inq) return s.Substring(0, i);
            }
            return s;
        }

        /// <summary>Handle one source line: optional "label:", then an EQU, a directive
        /// (DB/DW/DS/ORG) or an instruction.</summary>
        void DoLine(string raw)
        {
            string s = StripComment(raw).Trim();
            if (s.Length == 0) return;

            // A label: "name:" at the start of the line
            int colon = s.IndexOf(':');
            if (colon > 0 && IsIdent(s.Substring(0, colon)))
            {
                string name = s.Substring(0, colon);
                if (!finalPass)   // labels are recorded in pass 1 only
                {
                    if (symbols.ContainsKey(name)) throw new AsmException("duplicate label " + name);
                    symbols[name] = pc;
                }
                s = s.Substring(colon + 1).Trim();
                if (s.Length == 0) return;
            }
            // EQU: "name EQU expression". Set in both passes; any symbols an EQU uses are
            // already defined, so the value is correct in pass 2.
            string[] eq = s.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (eq.Length == 3 && eq[1].ToUpperInvariant() == "EQU")
            {
                int v = Eval(eq[2]);
                symbols[eq[0]] = v;
                return;
            }
            // Instruction: mnemonic followed by comma-separated operands
            int sp = s.IndexOfAny(new[] { ' ', '\t' });
            string mn = (sp < 0 ? s : s.Substring(0, sp)).ToUpperInvariant();
            string rest = sp < 0 ? "" : s.Substring(sp + 1).Trim();
            List<string> ops = SplitOperands(rest);
            Encode(mn, ops);
        }

        /// <summary>Is s a valid label name? (letter, _ or . followed by letters, digits, _ or .)</summary>
        static bool IsIdent(string s)
        {
            if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_' || s[0] == '.')) return false;
            foreach (char c in s) if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.')) return false;
            return true;
        }

        /// <summary>Split "A,(HL+1),B" into operands at commas that are not inside brackets.</summary>
        static List<string> SplitOperands(string s)
        {
            var r = new List<string>();
            if (s.Length == 0) return r;
            int depth = 0; var cur = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '(') depth++;
                if (c == ')') depth--;
                if (c == ',' && depth == 0) { r.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                cur.Append(c);
            }
            r.Add(cur.ToString().Trim());
            return r;
        }

        // ---------- expressions ----------
        // A small recursive-descent evaluator:
        //   Sum  := Prod { (+|-) Prod }
        //   Prod := Atom { (*|/) Atom }
        //   Atom := -Atom | (Sum) | $ | number | label
        // "pos" is the current position in the expression text, passed by reference.

        /// <summary>Evaluate an operand expression to a number.</summary>
        public int Eval(string e)
        {
            e = e.Replace(" ", "");
            int pos = 0;
            int v = EvalSum(e, ref pos);
            if (pos != e.Length) throw new AsmException("bad expression '" + e + "'");
            return v;
        }

        int EvalSum(string e, ref int pos)
        {
            int v = EvalProd(e, ref pos);
            while (pos < e.Length && (e[pos] == '+' || e[pos] == '-'))
            {
                char op = e[pos++];
                int r = EvalProd(e, ref pos);
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }

        int EvalProd(string e, ref int pos)
        {
            int v = EvalAtom(e, ref pos);
            while (pos < e.Length && (e[pos] == '*' || e[pos] == '/'))
            {
                char op = e[pos++];
                int r = EvalAtom(e, ref pos);
                v = op == '*' ? v * r : (r == 0 ? 0 : v / r);
            }
            return v;
        }

        int EvalAtom(string e, ref int pos)
        {
            if (pos >= e.Length) throw new AsmException("missing value");
            if (e[pos] == '-') { pos++; return -EvalAtom(e, ref pos); }
            if (e[pos] == '(') { pos++; int v = EvalSum(e, ref pos); if (pos >= e.Length || e[pos] != ')') throw new AsmException("missing )"); pos++; return v; }
            // "$" on its own = the address of the current instruction
            if (e[pos] == '$' && (pos + 1 >= e.Length || !IsHex(e[pos + 1]))) { pos++; return pc; }
            // hex as $FF or 0xFF
            if (e[pos] == '$' || (e[pos] == '0' && pos + 1 < e.Length && (e[pos + 1] == 'x' || e[pos + 1] == 'X')))
            {
                pos += e[pos] == '$' ? 1 : 2;
                int st = pos; while (pos < e.Length && IsHex(e[pos])) pos++;
                return int.Parse(e.Substring(st, pos - st), NumberStyles.HexNumber);
            }
            // decimal, or hex ending in h (0FFh - must start with a digit)
            if (char.IsDigit(e[pos]))
            {
                int st = pos; while (pos < e.Length && char.IsLetterOrDigit(e[pos])) pos++;
                string t = e.Substring(st, pos - st);
                if (t.EndsWith("h") || t.EndsWith("H")) return int.Parse(t.Substring(0, t.Length - 1), NumberStyles.HexNumber);
                return int.Parse(t, CultureInfo.InvariantCulture);
            }
            // label / EQU name
            if (char.IsLetter(e[pos]) || e[pos] == '_' || e[pos] == '.')
            {
                int st = pos; while (pos < e.Length && (char.IsLetterOrDigit(e[pos]) || e[pos] == '_' || e[pos] == '.')) pos++;
                string name = e.Substring(st, pos - st);
                int v;
                if (symbols.TryGetValue(name, out v)) return v;
                if (finalPass) throw new AsmException("undefined symbol " + name);
                return 0;   // pass 1: not known yet, any value will do
            }
            throw new AsmException("bad expression '" + e + "'");
        }

        static bool IsHex(char c) { return Uri.IsHexDigit(c); }

        // ---------- emit ----------
        /// <summary>Output one byte.</summary>
        void B(int b) { output.Add((byte)(b & 0xFF)); pc++; }
        /// <summary>Output a 16-bit word, low byte first (the Z80 is little-endian).</summary>
        void W(int w) { B(w); B(w >> 8); }
        /// <summary>Output an 8-bit operand (-128..255 accepted, so -1 means FF).</summary>
        void N8(string e) { int v = Eval(e); if (finalPass && (v < -128 || v > 255)) throw new AsmException("byte out of range " + v); B(v); }
        /// <summary>Output a signed 8-bit displacement (not used at present - for IX/IY+d).</summary>
        void D8(string e)
        {
            int v = Eval(e);
            if (finalPass && (v < -128 || v > 127)) throw new AsmException("index out of range");
            B(v);
        }
        /// <summary>Output the offset byte of a relative jump (JR / DJNZ). The offset is
        /// counted from the address after the instruction, and must be within -128..127.</summary>
        void Rel(string e)
        {
            int target = Eval(e);
            int d = target - (pc + 1);
            if (finalPass && (d < -128 || d > 127)) throw new AsmException("relative jump out of range");
            B(d);
        }

        // Register and condition tables. The position in each table is the number the
        // Z80 uses for that register inside the opcode (e.g. "LD r,r'" = 01 rrr r'r'r'),
        // so Idx(R8, "A") = 7 is the value to put into the opcode bits.
        static readonly string[] R8 = { "B", "C", "D", "E", "H", "L", "(HL)", "A" };     // 8-bit registers
        static readonly string[] RP = { "BC", "DE", "HL", "SP" };                        // register pairs
        static readonly string[] RP2 = { "BC", "DE", "HL", "AF" };                       // pairs for PUSH/POP
        static readonly string[] CC = { "NZ", "Z", "NC", "C", "PO", "PE", "P", "M" };    // conditions
        static readonly string[] ALU = { "ADD", "ADC", "SUB", "SBC", "AND", "XOR", "OR", "CP" };   // 8-bit arithmetic
        static readonly string[] ROT = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL" };   // CB-prefixed shifts

        /// <summary>Position of s in table t (ignoring case and spaces), or -1.</summary>
        static int Idx(string[] t, string s) { return Array.IndexOf(t, s.ToUpperInvariant().Replace(" ", "")); }
        /// <summary>Is the operand a memory reference in brackets, e.g. (4000h)?</summary>
        static bool IsMem(string s) { return s.StartsWith("(") && s.EndsWith(")"); }
        /// <summary>The text inside the brackets of a memory operand.</summary>
        static string Inner(string s) { return s.Substring(1, s.Length - 2).Trim(); }

        /// <summary>Turn one instruction into bytes. mn is the mnemonic (upper case) and o
        /// the operands. a/b are the first two operands as written; A/Bu are the same in
        /// upper case without spaces (for comparing against register names).</summary>
        void Encode(string mn, List<string> o)
        {
            int n = o.Count;
            string a = n > 0 ? o[0] : null, b = n > 1 ? o[1] : null;
            string A = a == null ? null : a.ToUpperInvariant().Replace(" ", "");
            string Bu = b == null ? null : b.ToUpperInvariant().Replace(" ", "");
            int r, r2, p;   // register / register / pair numbers looked up in the tables
            switch (mn)
            {
                // ---- directives ----
                case "ORG": pc = Eval(a); return;
                case "DB":
                case "DEFB":
                    foreach (var x in o) N8(x);
                    return;
                case "DW":
                case "DEFW":
                    foreach (var x in o) W(Eval(x));
                    return;
                case "DS":
                case "DEFS":
                    { int cnt = Eval(a); int fill = n > 1 ? Eval(b) : 0; for (int i = 0; i < cnt; i++) B(fill); return; }

                // ---- instructions with no operands ----
                case "NOP": B(0x00); return;
                case "RLCA": B(0x07); return;
                case "RRCA": B(0x0F); return;
                case "RLA": B(0x17); return;
                case "RRA": B(0x1F); return;
                case "DAA": B(0x27); return;
                case "CPL": B(0x2F); return;
                case "SCF": B(0x37); return;
                case "CCF": B(0x3F); return;
                case "HALT": B(0x76); return;
                case "DI": B(0xF3); return;
                case "EI": B(0xFB); return;
                case "EXX": B(0xD9); return;
                case "NEG": B(0xED); B(0x44); return;
                case "LDIR": B(0xED); B(0xB0); return;
                case "LDDR": B(0xED); B(0xB8); return;
                case "LDI": B(0xED); B(0xA0); return;
                case "LDD": B(0xED); B(0xA8); return;
                case "CPIR": B(0xED); B(0xB1); return;

                // ---- exchanges ----
                case "EX":
                    if (A == "DE" && Bu == "HL") { B(0xEB); return; }
                    if (A == "(SP)" && Bu == "HL") { B(0xE3); return; }
                    if (A == "AF" && Bu == "AF'") { B(0x08); return; }
                    break;

                // ---- stack ----
                case "PUSH":
                case "POP":
                    p = Idx(RP2, A); if (p < 0) break;
                    B((mn == "PUSH" ? 0xC5 : 0xC1) | p << 4); return;

                // ---- increment / decrement (8-bit register or 16-bit pair) ----
                case "INC":
                case "DEC":
                    r = Idx(R8, A);
                    if (r >= 0) { B((mn == "INC" ? 0x04 : 0x05) | r << 3); return; }
                    p = Idx(RP, A);
                    if (p >= 0) { B((mn == "INC" ? 0x03 : 0x0B) | p << 4); return; }
                    break;

                // ---- jumps, calls and returns ----
                case "JP":
                    if (n == 1)
                    {
                        if (A == "(HL)") { B(0xE9); return; }
                        B(0xC3); W(Eval(a)); return;
                    }
                    r = Idx(CC, A); if (r < 0) break;
                    B(0xC2 | r << 3); W(Eval(b)); return;
                case "CALL":
                    if (n == 1) { B(0xCD); W(Eval(a)); return; }
                    r = Idx(CC, A); if (r < 0) break;
                    B(0xC4 | r << 3); W(Eval(b)); return;
                case "RET":
                    if (n == 0) { B(0xC9); return; }
                    r = Idx(CC, A); if (r < 0) break;
                    B(0xC0 | r << 3); return;
                case "JR":
                    if (n == 1) { B(0x18); Rel(a); return; }
                    r = Idx(CC, A); if (r < 0 || r > 3) break;   // JR only allows NZ, Z, NC, C
                    B(0x20 | r << 3); Rel(b); return;
                case "DJNZ": B(0x10); Rel(a); return;
                case "RST": B(0xC7 | (Eval(a) & 0x38)); return;

                // ---- input / output ports ----
                case "IN":
                    if (A == "A" && IsMem(b) && Bu != "(C)") { B(0xDB); N8(Inner(b)); return; }
                    r = Idx(R8, A);
                    if (r >= 0 && r != 6 && Bu == "(C)") { B(0xED); B(0x40 | r << 3); return; }
                    break;
                case "OUT":
                    if (Bu == "A" && IsMem(a) && A != "(C)") { B(0xD3); N8(Inner(a)); return; }
                    break;

                // ---- bit operations (CB prefix) ----
                case "BIT":
                case "SET":
                case "RES":
                    {
                        int bit = Eval(a); r = Idx(R8, Bu);
                        if (r < 0 || bit < 0 || bit > 7) break;
                        int bas = mn == "BIT" ? 0x40 : mn == "RES" ? 0x80 : 0xC0;
                        B(0xCB); B(bas | bit << 3 | r); return;
                    }

                // ---- loads ----
                case "LD":
                    if (EncodeLd(a, b, A, Bu)) return;
                    break;
            }
            // ---- shifts and rotates: RLC RRC RL RR SLA SRA SLL SRL r ----
            int rot = Array.IndexOf(ROT, mn);
            if (rot >= 0 && n == 1)
            {
                r = Idx(R8, A); if (r >= 0) { B(0xCB); B(rot << 3 | r); return; }
            }
            // ---- arithmetic: ADD ADC SUB SBC AND XOR OR CP ----
            int alu = Array.IndexOf(ALU, mn);
            if (alu >= 0)
            {
                // 16-bit forms: ADD HL,rr  ADC HL,rr  SBC HL,rr
                if (n == 2 && (mn == "ADD" || mn == "ADC" || mn == "SBC") && A == "HL")
                {
                    p = Idx(RP, Bu);
                    if (p >= 0)
                    {
                        if (mn == "ADD") B(0x09 | p << 4);
                        else { B(0xED); B((mn == "ADC" ? 0x4A : 0x42) | p << 4); }
                        return;
                    }
                }
                // 8-bit forms accept both "SUB B" and "SUB A,B" style
                string src = null, srcU = null;
                if (n == 2 && A == "A") { src = b; srcU = Bu; }
                else if (n == 1) { src = a; srcU = A; }
                if (src != null)
                {
                    r2 = Idx(R8, srcU);
                    if (r2 >= 0) { B(0x80 | alu << 3 | r2); return; }   // with a register
                    B(0xC6 | alu << 3); N8(src); return;                // with a number
                }
            }
            throw new AsmException("unsupported instruction");
        }

        /// <summary>Encode the many forms of LD. Returns false if the combination is not valid.
        /// Checked in order, from register-to-register to the 16-bit memory forms.</summary>
        bool EncodeLd(string a, string b, string A, string Bu)
        {
            if (a == null || b == null) return false;
            int r = Idx(R8, A), r2 = Idx(R8, Bu);
            // LD r,r'   (LD (HL),(HL) does not exist - that code is HALT)
            if (r >= 0 && r2 >= 0) { if (r == 6 && r2 == 6) return false; B(0x40 | r << 3 | r2); return true; }
            // LD A,(BC) / LD A,(DE) / LD (BC),A / LD (DE),A / LD SP,HL
            if (A == "A" && Bu == "(BC)") { B(0x0A); return true; }
            if (A == "A" && Bu == "(DE)") { B(0x1A); return true; }
            if (A == "(BC)" && Bu == "A") { B(0x02); return true; }
            if (A == "(DE)" && Bu == "A") { B(0x12); return true; }
            if (A == "SP" && Bu == "HL") { B(0xF9); return true; }
            // LD A,(address) / LD (address),A
            if (A == "A" && IsMem(b)) { B(0x3A); W(Eval(Inner(b))); return true; }
            if (IsMem(a) && Bu == "A") { B(0x32); W(Eval(Inner(a))); return true; }
            // LD r,number
            if (r >= 0) { B(0x06 | r << 3); N8(b); return true; }
            // LD rr,(address) and LD rr,number
            int p = Idx(RP, A);
            if (p >= 0)
            {
                if (IsMem(b))
                {
                    int addr = Eval(Inner(b));
                    if (p == 2) { B(0x2A); W(addr); }                // LD HL,(nn) has a short form
                    else { B(0xED); B(0x4B | p << 4); W(addr); }
                    return true;
                }
                B(0x01 | p << 4); W(Eval(b)); return true;
            }
            // LD (address),rr
            int p2 = Idx(RP, Bu);
            if (IsMem(a) && p2 >= 0)
            {
                int addr = Eval(Inner(a));
                if (p2 == 2) { B(0x22); W(addr); }                   // LD (nn),HL has a short form
                else { B(0xED); B(0x43 | p2 << 4); W(addr); }
                return true;
            }
            return false;
        }
    }
}
