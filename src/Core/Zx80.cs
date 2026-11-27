// =====================================================================================
//  Zx80.cs - ZX80 programs (.o / .80 files) to and from ZX81 programs (.p)
// -------------------------------------------------------------------------------------
//  A ZX80 .o file is memory from 4000h up to E_LINE, as the ZX80 (4K ROM) saves it:
//     4000h-4027h  system variables (40 bytes - "truncated" compared with the ZX81's)
//                  4008h VARS, 400Ah E_LINE (= end of the file), 400Ch D_FILE ...
//     4028h        BASIC program: each line is the line number (2 bytes, HIGH byte
//                  first), the text, then NEWLINE (76h). No length bytes, and numbers
//                  are just digits (ZX80 BASIC is whole numbers only, no hidden values).
//     VARS         variables, ending with 80h.  The display file is not saved.
//  ZX80 files have no name, and can't start by themselves after LOAD.
//
//  ZX80 and ZX81 BASIC look alike but are coded differently: the character codes for
//  " - + * / = > < and the block graphics differ, and the keyword codes are completely
//  different (the ZX80 spells functions such as PEEK and CHR$ out in letters).
//
//  ToP turns a ZX80 program into a ZX81 one by writing each line as ZX81 listing text
//  and tokenising that (Basic81.TokenizeProgram), so keywords, functions and the hidden
//  5-byte numbers come out exactly as the ZX81 stores them. Variables are converted too
//  (whole numbers become ZX81 numbers). FromP does the reverse, for programs that only
//  use what the ZX80 has; anything else is reported.
//
//  Codes and tables are from the ZX80 ROM listing (token table at 00BAh, character set
//  at 0E00h). ZX80 keywords are D5h-FEh; D4h is a printable quote inside strings.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>A ZX80 program converted to ZX81: the .p bytes, the ZX81 listing it was
    /// tokenised from, and notes about anything that changed or couldn't be converted.</summary>
    public class Zx80Result
    {
        public byte[] P;
        public string Listing;
        public List<string> Notes = new List<string>();
    }

    /// <summary>A program that can't be written as a ZX80 program; Problems lists why.</summary>
    public class Zx80Exception : Exception
    {
        public readonly List<string> Problems;
        public Zx80Exception(List<string> problems)
            : base("This program can't be saved as a ZX80 program:\n  " + string.Join("\n  ", problems.Take(12)) + (problems.Count > 12 ? "\n  (and " + (problems.Count - 12) + " more)" : ""))
        { Problems = problems; }
    }

    public static class Zx80
    {
        public const int Base = 0x4000;          // a .o file starts here
        public const int ProgStart = 0x4028;     // first BASIC line
        const int VARS = 0x4008, E_LINE = 0x400A;

        /// <summary>ZX80 character code (00h-3Fh, 80h-BFh) to ZX81 code. -1: none.</summary>
        static readonly int[] To81 = new int[256];
        /// <summary>ZX81 character code to ZX80 code. -1: none.</summary>
        static readonly int[] From81 = new int[256];

        /// <summary>ZX80 keywords D4h-FFh as ZX81 keyword text (null = unused code).
        /// RANDOMISE, CONTINUE, GO TO and GO SUB are RAND, CONT, GOTO and GOSUB on the ZX81.</summary>
        static readonly string[] Tok = {
            "\"", "THEN", "TO", ";", ",", ")", "(", "NOT", "-", "+", "*", "/", "AND", "OR", "**", "=", ">", "<",
            "LIST", "RETURN", "CLS", "DIM", "SAVE", "FOR", "GOTO", "POKE", "INPUT", "RAND", "LET", null, null,
            "NEXT", "PRINT", null, "NEW", "RUN", "STOP", "CONT", "IF", "GOSUB", "LOAD", "CLEAR", "REM", null };

        /// <summary>ZX81 function keywords the ZX80 has too (spelled out in letters there).</summary>
        static readonly string[] SharedFuncs = { "ABS", "CHR$", "CODE", "PEEK", "RND", "STR$", "USR" };

        static Zx80()
        {
            for (int i = 0; i < 256; i++) { To81[i] = -1; From81[i] = -1; }
            // the same code on both: space, £ $ : ? ( ), ; , . and 0-9, A-Z
            foreach (int c in new[] { 0x00, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x19, 0x1A, 0x1B }) To81[c] = c;
            for (int c = 0x1C; c < 0x40; c++) To81[c] = c;
            To81[0x01] = 0x0B;                                   // "
            To81[0x12] = 0x16; To81[0x13] = 0x15; To81[0x14] = 0x17; To81[0x15] = 0x18;   // - + * /
            To81[0x16] = 0x14; To81[0x17] = 0x12; To81[0x18] = 0x13;                      // = > <
            // block graphics: worked out by matching the two ROMs' character bitmaps
            int[] g = { 0x05, 0x83, 0x01, 0x02, 0x04, 0x87, 0x06, 0x08, 0x09, 0x0A };    // ZX80 02h-0Bh
            for (int k = 0; k < 10; k++) { To81[0x02 + k] = g[k]; To81[0x82 + k] = g[k] ^ 0x80; }
            // inverse video of everything else
            for (int c = 0; c < 0x40; c++) if (To81[c] >= 0 && (c < 2 || c > 0x0B)) To81[c | 0x80] = To81[c] | 0x80;
            for (int c = 0; c < 256; c++) if (To81[c] >= 0) From81[To81[c]] = c;
        }

        /// <summary>Does this look like a ZX80 .o file? (VARS and E_LINE make sense.)</summary>
        public static bool LooksLikeZx80(byte[] o)
        {
            if (o.Length < ProgStart - Base + 1) return false;
            int vars = o[VARS - Base] | o[VARS - Base + 1] << 8, eLine = o[E_LINE - Base] | o[E_LINE - Base + 1] << 8;
            return vars >= ProgStart && vars < eLine && eLine - Base <= o.Length;
        }

        // ------------------------------------------------------------------
        //  ZX80 -> ZX81
        // ------------------------------------------------------------------

        /// <summary>Convert a ZX80 .o file into a ZX81 program.</summary>
        public static Zx80Result ToP(byte[] o)
        {
            var res = new Zx80Result();
            if (o.Length < ProgStart - Base + 1) throw new TapeDecodeException("the .o file is too short to be a ZX80 program", -1);
            var m = new byte[0x10000];
            Array.Copy(o, 0, m, Base, Math.Min(o.Length, 0x10000 - Base));
            Func<int, int> w = x => m[x] | m[x + 1] << 8;
            int vars = w(VARS), eLine = w(E_LINE);
            if (vars < ProgStart || vars >= eLine || eLine - Base > o.Length)
                throw new TapeDecodeException("this isn't a ZX80 program: its VARS (" + Z80Disasm.Hex(vars, 4) + ") and E_LINE (" + Z80Disasm.Hex(eLine, 4) +
                    ") system variables don't fit a " + o.Length + "-byte file", -1);
            if (o.Length > eLine - Base) res.Notes.Add((o.Length - (eLine - Base)) + " bytes after the end of the ZX80 program (after E_LINE) were ignored");

            // the program, as ZX81 listing text
            var text = new StringBuilder();
            int a = ProgStart, lines = 0;
            while (a < vars)
            {
                if ((m[a] & 0xC0) != 0) break;                  // the end of the program
                int num = m[a] << 8 | m[a + 1];
                int end = a + 2;
                while (end < vars && m[end] != 0x76) end++;
                if (end >= vars) { res.Notes.Add("the last line (" + num + ") has no NEWLINE; it was cut at VARS"); }
                text.Append(num).Append(' ').Append(LineText(m, a + 2, end, num, res.Notes)).Append('\n');
                lines++;
                a = end + 1;
            }
            res.Listing = text.ToString();
            var diags = new List<Diagnostic>();
            var tok = Basic81.TokenizeProgram(res.Listing, diags);
            foreach (var dg in diags) res.Notes.Add("converting: " + dg.Message);
            byte[] p = Basic81.BuildP(tok, null, false, false, -1);
            // the variables
            var vb = VarsTo81(m, vars, eLine, res.Notes);
            res.P = SetVariables(p, vb);
            res.Notes.Insert(0, "converted from a ZX80 program (" + lines + " lines). ZX80 BASIC is whole-number only (7/2 is 3); a ZX81 works in floating point " +
                "(7/2 is 3.5), so compile it with the ZX81 BASIC Compiler to keep whole-number arithmetic.");
            return res;
        }

        /// <summary>One ZX80 line (from a up to, not including, the NEWLINE at end) as ZX81 listing text.</summary>
        static string LineText(byte[] m, int a, int end, int lineNo, List<string> notes)
        {
            var sb = new StringBuilder();
            bool inStr = false, inRem = false;
            int depth = 0;
            var rndClose = new Stack<int>();                    // bracket depths where a converted RND(n) ends
            for (int i = a; i < end; i++)
            {
                int c = m[i];
                if (inRem) { sb.Append(CharText(c, true, lineNo, notes)); continue; }
                if (c == 0x01) { inStr = !inStr; sb.Append('"'); continue; }
                if (inStr)
                {
                    if (c == 0xD4) sb.Append("\"\"");            // a printable quote inside a string
                    else if (c >= 0xD5) sb.Append(Tok[c - 0xD4] ?? "?");
                    else sb.Append(CharText(c, true, lineNo, notes));
                    continue;
                }
                // ZX80 RND(n) gives 1..n; the ZX81's RND is a fraction, so write (1+INT (RND*(n)))
                if (c == 0x37 && i + 3 < end && m[i + 1] == 0x33 && m[i + 2] == 0x29 && (m[i + 3] == 0x10 || m[i + 3] == 0xDA) && (i == a || !IsLetterOrDigit(m[i - 1])))
                {
                    sb.Append("(1+INT (RND*");
                    rndClose.Push(depth);
                    i += 2;                                      // the bracket itself is copied next time round
                    continue;
                }
                if (c == 0x10 || c == 0xDA) { depth++; sb.Append('('); continue; }
                if (c == 0x11 || c == 0xD9)
                {
                    depth--; sb.Append(')');
                    if (rndClose.Count > 0 && rndClose.Peek() == depth) { rndClose.Pop(); sb.Append("))"); }
                    continue;
                }
                if (c >= 0xD4)
                {
                    string t = Tok[c - 0xD4];
                    if (t == null) { notes.Add("line " + lineNo + ": unknown ZX80 code " + Z80Disasm.Hex(c, 2) + " left out"); continue; }
                    if (t.Length > 0 && char.IsLetter(t[0])) sb.Append(' ').Append(t).Append(' ');
                    else sb.Append(t);
                    if (t == "REM") inRem = true;
                    continue;
                }
                // TL$ has no ZX81 equivalent
                if (c == 0x39 && i + 2 < end && m[i + 1] == 0x31 && m[i + 2] == 0x0D) notes.Add("line " + lineNo + ": TL$ (all but the first character) has no ZX81 keyword; use A$(2 TO )");
                sb.Append(CharText(c, false, lineNo, notes));
            }
            if (rndClose.Count > 0) notes.Add("line " + lineNo + ": RND( without its closing bracket");
            return sb.ToString().Trim();
        }

        static bool IsLetterOrDigit(int c) { return c >= 0x1C && c < 0x40; }

        /// <summary>A ZX80 character code as ZX81 listing text: letters, digits and simple
        /// symbols as themselves, everything else (graphics, inverse) as a \hh escape.</summary>
        static string CharText(int c, bool inStrOrRem, int lineNo, List<string> notes)
        {
            int z = To81[c];
            if (z < 0) { notes.Add("line " + lineNo + ": ZX80 code " + Z80Disasm.Hex(c, 2) + " has no ZX81 equivalent (written as ?)"); return "?"; }
            if (z < 0x40 && (z == 0 || z > 0x0B)) return Charset.Printable[z].ToString();
            return "\\" + z.ToString("X2");
        }

        /// <summary>ZX80 variables (vars up to E_LINE) as ZX81 variables, ending with 80h.</summary>
        static byte[] VarsTo81(byte[] m, int vars, int eLine, List<string> notes)
        {
            var o = new List<byte>();
            int a = vars, count = 0;
            while (a < eLine && m[a] != 0x80)
            {
                int b = m[a], type = b >> 5, letter = b & 0x1F;
                Func<int, int> i16 = x => (short)(m[x] | m[x + 1] << 8);
                if (type == 3)                                   // integer, one letter
                {
                    o.Add((byte)(0x60 | letter)); o.AddRange(ZxFloat.Encode(i16(a + 1)));
                    a += 3;
                }
                else if (type == 2)                              // integer, long name (last character inverse)
                {
                    int q = a + 1;
                    var name = new List<byte>();
                    while (q < eLine && (m[q] & 0x80) == 0) name.Add(m[q++]);
                    if (q >= eLine) break;
                    name.Add(m[q++]);
                    o.Add((byte)(0xA0 | letter));
                    for (int k = 0; k < name.Count; k++) { int z = To81[name[k] & 0x7F]; o.Add((byte)((z < 0 ? 0x26 : z) | (k == name.Count - 1 ? 0x80 : 0))); }
                    o.AddRange(ZxFloat.Encode(i16(q)));
                    a = q + 2;
                }
                else if (type == 4)                              // string, ends with a quote (01h)
                {
                    int q = a + 1;
                    var chars = new List<byte>();
                    while (q < eLine && m[q] != 0x01) { int z = To81[m[q]]; if (m[q] == 0xD4) z = 0x0B; chars.Add((byte)(z < 0 ? 0x0F : z)); q++; }
                    o.Add((byte)(0x40 | letter)); o.Add((byte)chars.Count); o.Add((byte)(chars.Count >> 8)); o.AddRange(chars);
                    a = q + 1;
                }
                else if (type == 5)                              // array A(0) to A(max)
                {
                    int max = m[a + 1], n = max + 1;
                    int bodyLen = 1 + 2 + 5 * n;
                    o.Add((byte)(0x80 | letter)); o.Add((byte)bodyLen); o.Add((byte)(bodyLen >> 8)); o.Add(1); o.Add((byte)n); o.Add((byte)(n >> 8));
                    for (int k = 0; k < n; k++) o.AddRange(ZxFloat.Encode(i16(a + 2 + 2 * k)));
                    notes.Add("array " + Charset.Printable[letter + 0x20] + "(" + max + "): a ZX80 array starts at element 0, a ZX81 one at 1, so element k is now k+1");
                    a += 2 + 2 * n;
                }
                else if (type == 7)                              // FOR loop: value, limit, line
                {
                    o.Add((byte)(0xE0 | letter));
                    o.AddRange(ZxFloat.Encode(i16(a + 1))); o.AddRange(ZxFloat.Encode(i16(a + 3))); o.AddRange(ZxFloat.Encode(1));
                    int line = m[a + 5] | m[a + 6] << 8;
                    o.Add((byte)line); o.Add((byte)(line >> 8));
                    a += 7;
                }
                else
                {
                    notes.Add("ZX80 variable of unknown type at " + Z80Disasm.Hex(a, 4) + ": it and any after it were left out (RUN clears variables anyway)");
                    break;
                }
                count++;
            }
            o.Add(0x80);
            if (count > 0) notes.Add(count + " variable" + (count == 1 ? "" : "s") + " converted");
            return o.ToArray();
        }

        /// <summary>Put new variables (ending 80h) into a .p, moving E_LINE and the pointers after it.</summary>
        public static byte[] SetVariables(byte[] p, byte[] vb)
        {
            Func<int, int> w = x => p[x - PFile.Base] | p[x - PFile.Base + 1] << 8;
            int vars = w(0x4010), eLine = w(0x4014);
            var o = new List<byte>(p.Take(vars - PFile.Base));
            o.AddRange(vb);
            int delta = vb.Length - (eLine - vars);
            var r = o.ToArray();
            foreach (int sv in new[] { 0x4014, 0x4016, 0x4018, 0x401A, 0x401C })
            {
                int v = w(sv);
                if (v >= eLine) { v += delta; r[sv - PFile.Base] = (byte)v; r[sv - PFile.Base + 1] = (byte)(v >> 8); }
            }
            return r;
        }

        // ------------------------------------------------------------------
        //  ZX81 -> ZX80
        // ------------------------------------------------------------------

        /// <summary>Write a ZX81 program as a ZX80 .o file. Throws Zx80Exception listing
        /// every line that uses something the ZX80 doesn't have. notes gets what changed.</summary>
        public static byte[] FromP(PFile p, List<string> notes)
        {
            var problems = new List<string>();
            var prog = new List<byte>();
            foreach (var ln in p.Lines)
            {
                if (ln.Body == null) continue;
                if (ln.Number > 9999) problems.Add("line " + ln.Number + ": ZX80 line numbers only go up to 9999");
                prog.Add((byte)(ln.Number >> 8)); prog.Add((byte)ln.Number);
                prog.AddRange(LineTo80(ln, problems));
                prog.Add(0x76);
            }
            var vb = VarsTo80(p, notes);
            if (problems.Count > 0) throw new Zx80Exception(problems);

            int vars = ProgStart + prog.Count, eLine = vars + vb.Length, dfile = eLine + 2;
            var sv = new byte[ProgStart - Base];
            Action<int, int> b8 = (x, v) => sv[x - Base] = (byte)v;
            Action<int, int> w16 = (x, v) => { sv[x - Base] = (byte)v; sv[x - Base + 1] = (byte)(v >> 8); };
            // the values the ZX80 ROM sets up; its LOAD rebuilds the edit line and display afterwards
            b8(0x4000, 0xFF);           // ERR_NR: no error
            b8(0x4001, 0x88);           // FLAGS
            w16(0x4002, 0xFFFE);        // PPC: no line running
            w16(0x4004, eLine);         // P_PTR
            w16(0x4006, 0);             // E_PPC
            w16(VARS, vars);
            w16(E_LINE, eLine);
            w16(0x400C, dfile);         // D_FILE (after the edit line: not saved)
            w16(0x400E, dfile + 1);     // DF_EA
            w16(0x4010, dfile + 1);     // DF_END
            b8(0x4012, 2);              // DF_SZ
            w16(0x4024, 0x1721);        // S_POSN: column 33, line 23
            w16(0x4026, eLine);         // CH_ADD
            var o = new List<byte>(sv);
            o.AddRange(prog);
            o.AddRange(vb);
            notes.Add("written as a ZX80 program (" + p.Lines.Count + " lines). The ZX80 has no program names and doesn't auto-run programs.");
            return o.ToArray();
        }

        /// <summary>One ZX81 line body as ZX80 codes (without the line number or NEWLINE).</summary>
        static List<byte> LineTo80(PLine ln, List<string> problems)
        {
            var o = new List<byte>();
            byte[] b = ln.Body;
            bool inStr = false, inRem = false;
            int stop = b.Length > 0 && b[b.Length - 1] == 0x76 ? b.Length - 1 : b.Length;
            for (int i = 0; i < stop; i++)
            {
                int c = b[i];
                if (c == 0x76) { problems.Add("line " + ln.Number + ": contains a NEWLINE byte (machine code?), which ends a line on the ZX80"); break; }
                if (inRem || inStr)
                {
                    if (inStr && c == 0x0B) { inStr = false; o.Add(0x01); continue; }
                    if (inStr && c == 0xC0) { o.Add(0xD4); continue; }         // "" : a quote inside the string
                    int z = c < 0xC0 ? From81[c] : -1;
                    if (z < 0) { problems.Add("line " + ln.Number + ": character " + Z80Disasm.Hex(c, 2) + (c >= 0xC0 ? " (" + Charset.Tokens[c] + ")" : "") + " has no ZX80 equivalent"); continue; }
                    o.Add((byte)z);
                    continue;
                }
                if (c == 0x0B) { inStr = true; o.Add(0x01); continue; }
                if (c == 0x7E)
                {
                    // a hidden number: the ZX80 only has the digits, and only whole numbers
                    if (i + 5 < b.Length)
                    {
                        double v = ZxFloat.Decode(b, i + 1);
                        if (v != Math.Floor(v) || v > 32767 || v < -32768) problems.Add("line " + ln.Number + ": the number " + ZxFloat.Format(v) + " isn't a ZX80 whole number (-32768 to 32767)");
                    }
                    i += 5;
                    continue;
                }
                if (c >= 0x43 && c < 0x80) { problems.Add("line " + ln.Number + ": code " + Z80Disasm.Hex(c, 2) + " has no ZX80 equivalent"); continue; }
                string t = c >= 0xC0 || c == 0x40 || c == 0x41 || c == 0x42 ? Charset.Tokens[c] : null;
                if (t != null)
                {
                    int k = Array.IndexOf(Tok, t);
                    if (k > 0) { o.Add((byte)(0xD4 + k)); if (t == "REM") inRem = true; continue; }
                    if (Array.IndexOf(SharedFuncs, t) >= 0)
                    {
                        // a function: the ZX80 spells it out, and needs its argument in brackets
                        foreach (char ch in t) o.Add((byte)Charset.FromChar(ch, false));
                        int nx = i + 1;
                        if (t != "RND" && (nx >= stop || b[nx] != 0x10)) problems.Add("line " + ln.Number + ": " + t + " needs its argument in brackets on the ZX80, e.g. " + t + "(X)");
                        if (t == "RND") problems.Add("line " + ln.Number + ": RND is a fraction on the ZX81 but RND(n) gives 1 to n on the ZX80");
                        continue;
                    }
                    string zx80 = t == "<=" || t == ">=" || t == "<>" ? " (write it with NOT, e.g. NOT A>B)" : "";
                    problems.Add("line " + ln.Number + ": " + t + " isn't in ZX80 BASIC" + zx80);
                    continue;
                }
                int z2 = From81[c];
                if (z2 < 0) { problems.Add("line " + ln.Number + ": character " + Z80Disasm.Hex(c, 2) + " has no ZX80 equivalent"); continue; }
                if (c == 0x1B) problems.Add("line " + ln.Number + ": a decimal point - the ZX80 only has whole numbers");
                // operators are keywords on the ZX80
                int op = Array.IndexOf(Tok, Charset.Printable[c & 0x3F].ToString());
                if (c < 0x40 && op > 0 && "-+*/=><;,".IndexOf(Charset.Printable[c]) >= 0) { o.Add((byte)(0xD4 + op)); continue; }
                o.Add((byte)z2);
            }
            return o;
        }

        /// <summary>The ZX81 variables as ZX80 variables (whole-number values only). Ones
        /// the ZX80 can't hold are left out, with a note.</summary>
        static byte[] VarsTo80(PFile p, List<string> notes)
        {
            var o = new List<byte>();
            byte[] v = p.VariablesBytes();
            int a = 0, dropped = 0;
            Func<int, int> whole = x => { double d = ZxFloat.Decode(v, x); return d == Math.Floor(d) && d >= -32768 && d <= 32767 ? (int)d : int.MinValue; };
            Action<int> i16 = x => { o.Add((byte)x); o.Add((byte)(x >> 8)); };
            while (a < v.Length && v[a] != 0x80)
            {
                int b = v[a], type = b >> 5, letter = b & 0x1F;
                int size;
                if (type == 3) size = 6;
                else if (type == 5) { int q = a + 1; while (q < v.Length && (v[q] & 0x80) == 0) q++; size = q + 1 - a + 5; }
                else if (type == 2 || type == 4 || type == 6) size = 3 + (v[a + 1] | v[a + 2] << 8);
                else if (type == 7) size = 18;
                else break;
                if (a + size > v.Length) break;
                if (type == 3 && whole(a + 1) != int.MinValue) { o.Add((byte)(0x60 | letter)); i16(whole(a + 1)); }
                else if (type == 5 && whole(a + size - 5) != int.MinValue)
                {
                    o.Add((byte)(0x40 | letter));
                    for (int q = a + 1; q < a + size - 5; q++) o.Add((byte)(From81[v[q] & 0x7F] | (q == a + size - 6 ? 0x80 : 0)));
                    i16(whole(a + size - 5));
                }
                else if (type == 2 && Enumerable.Range(a + 3, size - 3).All(q => From81[v[q]] >= 0 && v[q] != 0x0B))
                {
                    o.Add((byte)(0x80 | letter));
                    for (int q = a + 3; q < a + size; q++) o.Add((byte)From81[v[q]]);
                    o.Add(0x01);
                }
                else if (type == 4 && v[a + 3] == 1 && Enumerable.Range(0, (size - 6) / 5).All(k => whole(a + 6 + 5 * k) != int.MinValue) && (size - 6) / 5 <= 256)
                {
                    int n = (size - 6) / 5;
                    o.Add((byte)(0xA0 | letter)); o.Add((byte)(n - 1));
                    for (int k = 0; k < n; k++) i16(whole(a + 6 + 5 * k));
                }
                else if (type == 7 && whole(a + 1) != int.MinValue && whole(a + 6) != int.MinValue && ZxFloat.Decode(v, a + 11) == 1)
                {
                    o.Add((byte)(0xE0 | letter)); i16(whole(a + 1)); i16(whole(a + 6));
                    o.Add(v[a + 16]); o.Add(v[a + 17]);
                }
                else dropped++;
                a += size;
            }
            o.Add(0x80);
            if (dropped > 0) notes.Add(dropped + " variable" + (dropped == 1 ? "" : "s") + " left out: the ZX80 only holds whole numbers, strings, 1-dimension arrays and FOR loops with STEP 1");
            return o.ToArray();
        }
    }
}
