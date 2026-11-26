// =====================================================================================
//  CodeFinder.cs - locating the machine code in a .p file, and writing it as .asm
// -------------------------------------------------------------------------------------
//  ZX81 programs keep machine code in several places:
//    * in a REM line (usually line 0 or 1), called with RAND USR 16514
//    * spread over several REM lines, jumping from one to the next
//    * in the variables area (e.g. inside a string variable) or after the display
//      file, found with PEEKs of the system variables: USR (PEEK 16396+256*PEEK 16397)
//    * outside the file altogether (above RAMTOP, loaded separately) - then all we can
//      do is say where the program expects it
//
//  How it works:
//   1. Every USR in the BASIC is found and its argument worked out. The argument is
//      evaluated like the ROM would: numbers use their hidden 5-byte values, PEEK reads
//      the saved memory, variables use their saved values, and VAL "..." evaluates the
//      digits in the string (RAND USR VAL "16514" is a common way to save memory).
//   2. From each of those addresses the code is TRACED: instructions are decoded one
//      after another, following jumps and calls, until RET / JP etc. Bytes reached this
//      way are certainly code; everything else in a REM line is data.
//   3. REM lines whose contents can't be text (codes 43h-7Fh) are reported as machine
//      code / data even if no USR points at them.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>A place machine code is called from.</summary>
    public class CodeEntry
    {
        public int Address;      // where the code starts
        public string Source;    // e.g. "RAND USR in line 10"
        public bool InFile;      // inside the .p file?
    }

    /// <summary>Results of the search.</summary>
    public class CodeAnalysis
    {
        public readonly List<CodeEntry> Entries = new List<CodeEntry>();
        public readonly bool[] IsCode = new bool[0x10000];            // traced instruction bytes
        public readonly HashSet<int> InstrStarts = new HashSet<int>(); // first byte of each traced instruction
        public readonly HashSet<int> Labels = new HashSet<int>();      // jump / call targets inside the file
        public readonly HashSet<int> DataRefs = new HashSet<int>();    // addresses loaded as data (LD HL,nn etc.)
        public readonly bool[] CalcBytes = new bool[0x10000];          // calculator literals after RST 28h
        public readonly List<string> Notes = new List<string>();
        public readonly List<PLine> CodeLines = new List<PLine>();     // REM lines holding code or data
        public readonly List<int[]> Blocks = new List<int[]>();        // [start, end) of each code / data block, in order

        public bool HasMachineCode { get { return Blocks.Count > 0 || Entries.Count > 0; } }
    }

    public static class CodeFinder
    {
        public static CodeAnalysis Analyze(PFile p)
        {
            var ca = new CodeAnalysis();
            var vars = p.ReadVariables();

            // ---- 1. USR calls in the BASIC ----
            foreach (var l in p.Lines)
            {
                byte[] b = l.Body;
                bool inStr = false;
                for (int i = 0; i < b.Length; i++)
                {
                    int c = b[i];
                    if (c == 0xEA && !inStr) break;          // REM: nothing after it is BASIC
                    if (c == 0x0B) { inStr = !inStr; continue; }
                    if (inStr) continue;
                    if (c == 0x7E) { i += 5; continue; }
                    if (c != 0xD4) continue;                 // USR
                    string before = i > 0 && b[i - 1] >= 0xC0 ? Charset.Tokens[b[i - 1]] + " " : "";
                    int pos = i + 1;
                    double v;
                    var ev = new Eval { B = b, P = p, Vars = vars, Pos = pos };
                    if (ev.TryPrimary(out v) && v >= 0 && v < 65536)
                    {
                        int a = (int)v;
                        var e = new CodeEntry { Address = a, Source = before + "USR in line " + l.Number, InFile = a >= PFile.Base && a < p.FileEnd };
                        if (!ca.Entries.Any(x => x.Address == a && x.Source == e.Source)) ca.Entries.Add(e);
                    }
                    else ca.Notes.Add("Line " + l.Number + ": USR with an address that can't be worked out from the saved program (it depends on values only known when it runs).");
                }
            }

            // ---- 2. trace from each entry ----
            foreach (var e in ca.Entries)
            {
                if (e.Address < 0x2000) { ca.Notes.Add(e.Source + " calls ROM address " + Z80Disasm.Hex(e.Address, 4) + (Z80Disasm.KnownLabel(e.Address) != null ? " (" + Z80Disasm.KnownLabel(e.Address) + ")" : "") + "."); continue; }
                if (!e.InFile) { ca.Notes.Add(e.Source + " calls " + e.Address + " (" + Z80Disasm.Hex(e.Address, 4) + "), which is outside the file - that machine code must be loaded or POKEd separately (often above RAMTOP)."); continue; }
                ca.Labels.Add(e.Address);
                Trace(p, ca, e.Address);
            }

            // ---- 3. REM lines with machine code or data ----
            foreach (var l in p.Lines)
            {
                bool traced = false;
                for (int a = l.BodyAddr; a < l.End && a < p.FileEnd; a++) if (ca.IsCode[a]) { traced = true; break; }
                if (traced || Basic81.IsCodeRem(l.Body)) ca.CodeLines.Add(l);
            }

            // ---- blocks: REM line contents, plus traced code outside REM lines ----
            foreach (var l in ca.CodeLines)
            {
                int s = l.BodyAddr + (l.Body.Length > 0 && l.Body[0] == 0xEA ? 1 : 0);
                int en = Math.Min(l.End - 1, p.FileEnd);       // without the final 76h
                if (en > s) ca.Blocks.Add(new[] { s, en });
            }
            int run = -1;
            for (int a = PFile.Base; a <= p.FileEnd; a++)
            {
                bool code = a < p.FileEnd && ca.IsCode[a] && !InBlocks(ca, a);
                if (code && run < 0) run = a;
                if (!code && run >= 0) { ca.Blocks.Add(new[] { run, a }); run = -1; }
            }
            ca.Blocks.Sort((x, y) => x[0].CompareTo(y[0]));

            // ---- describe where the code is ----
            foreach (var blk in ca.Blocks)
            {
                int n = 0;
                for (int a = blk[0]; a < blk[1]; a++) if (ca.IsCode[a]) n++;
                string where = p.Region(blk[0]);
                if (where.StartsWith("variables"))
                {
                    var v = vars.FirstOrDefault(x => blk[0] >= x.Address && blk[0] < x.Address + x.Size);
                    if (v != null) where += " (inside variable " + v.Name + ")";
                }
                ca.Notes.Add(string.Format("{0}-{1} ({2} bytes) in {3}: {4}", Z80Disasm.Hex(blk[0], 4), Z80Disasm.Hex(blk[1] - 1, 4), blk[1] - blk[0], where,
                    n == 0 ? "not reached from any USR (data, or code called some other way)" : n + " bytes traced as code"));
            }
            var lines = ca.CodeLines.Select(l => l.Number).ToList();
            if (lines.Count > 1) ca.Notes.Add("Machine code / data is split over lines " + string.Join(", ", lines) + ".");
            return ca;
        }

        static bool InBlocks(CodeAnalysis ca, int a)
        {
            foreach (var b in ca.Blocks) if (a >= b[0] && a < b[1]) return true;
            return false;
        }

        /// <summary>Follow the code from start: decode, mark, follow jumps and calls.</summary>
        static void Trace(PFile p, CodeAnalysis ca, int start)
        {
            var work = new Stack<int>();
            work.Push(start);
            int guard = 0;
            while (work.Count > 0 && guard++ < 200000)
            {
                int a = work.Pop();
                while (a >= PFile.Base && a < p.FileEnd && !ca.InstrStarts.Contains(a))
                {
                    var ins = Z80Disasm.Decode(p.Mem, a, null);
                    if (ins.Invalid) break;
                    ca.InstrStarts.Add(a);
                    for (int k = 0; k < ins.Len; k++) ca.IsCode[(a + k) & 0xFFFF] = true;
                    if (ins.Target >= 0)
                    {
                        if (ins.Target >= PFile.Base && ins.Target < p.FileEnd) { ca.Labels.Add(ins.Target); work.Push(ins.Target); }
                    }
                    else if (ins.Value >= PFile.Base && ins.Value < p.FileEnd && ins.Shape.Contains("nn")) ca.DataRefs.Add(ins.Value);
                    int next = a + ins.Len;
                    // RST 28h: calculator literals follow, ending with 34h (end-calc)
                    if (ins.Shape == "RST 28h")
                    {
                        int k = next;
                        while (k < p.FileEnd && k < next + 256 && p.Mem[k] != 0x34) { ca.CalcBytes[k] = true; ca.IsCode[k] = true; k++; }
                        if (k < p.FileEnd) { ca.CalcBytes[k] = true; ca.IsCode[k] = true; }
                        next = k + 1;
                    }
                    if (ins.Shape == "RST 08h" && next < p.FileEnd) { ca.CalcBytes[next] = true; ca.IsCode[next] = true; }   // error code byte
                    if (!ins.Continues) break;
                    a = next;
                }
            }
        }

        // ------------------------------------------------------------------
        //  Evaluating a USR argument from the tokenised BASIC
        // ------------------------------------------------------------------
        class Eval
        {
            public byte[] B; public PFile P; public List<PVar> Vars; public int Pos;
            int Peek() { return Pos < B.Length ? B[Pos] : 0x76; }

            /// <summary>The primary after USR (a function takes the next primary, as in Sinclair BASIC).</summary>
            public bool TryPrimary(out double v)
            {
                v = 0;
                int c = Peek();
                if (c == 0x10) { Pos++; if (!TryExpr(out v)) return false; if (Peek() != 0x11) return false; Pos++; return true; }   // ( ... )
                if (c >= 0x1C && c <= 0x25 || c == 0x1B)
                {
                    // digits and "." (codes 1Ch-25h, 1Bh), then normally 7Eh + the hidden 5-byte value
                    var digits = new StringBuilder();
                    while (Pos < B.Length && (B[Pos] >= 0x1C && B[Pos] <= 0x25 || B[Pos] == 0x1B))
                    {
                        digits.Append(B[Pos] == 0x1B ? '.' : (char)('0' + B[Pos] - 0x1C));
                        Pos++;
                    }
                    if (Peek() == 0x7E)
                    {
                        if (Pos + 5 >= B.Length) return false;
                        v = ZxFloat.Decode(B, Pos + 1);   // the hidden value - what the ROM uses
                        Pos += 6;
                        return true;
                    }
                    // no hidden value: digits inside a VAL "..." string
                    return double.TryParse(digits.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
                }
                if (c == 0xC5 && Pos + 1 < B.Length && B[Pos + 1] == 0x0B)   // VAL "expression"
                {
                    int q = Pos + 2, close = q;
                    while (close < B.Length && B[close] != 0x0B && B[close] != 0x76) close++;
                    if (close >= B.Length || B[close] != 0x0B) return false;
                    var inner = new byte[close - q + 1];
                    Array.Copy(B, q, inner, 0, close - q);
                    inner[inner.Length - 1] = 0x76;               // end marker
                    var sub = new Eval { B = inner, P = P, Vars = Vars, Pos = 0 };
                    if (!sub.TryExpr(out v) || sub.Pos != inner.Length - 1) return false;
                    Pos = close + 1;
                    return true;
                }
                if (c == 0xD3)   // PEEK
                {
                    Pos++;
                    double a;
                    if (!TryPrimary(out a)) return false;
                    int ad = (int)a & 0xFFFF;
                    if (ad >= 0x4000 && ad < PFile.Base) return false;   // not saved
                    v = P.Mem[ad];
                    return true;
                }
                if (c == 0x16) { Pos++; if (!TryPrimary(out v)) return false; v = -v; return true; }
                if (c >= 0x26 && c <= 0x3F)
                {
                    // a numeric variable: use its saved value
                    var sb = new StringBuilder();
                    while (Pos < B.Length && ((B[Pos] >= 0x26 && B[Pos] <= 0x3F) || (B[Pos] >= 0x1C && B[Pos] <= 0x25))) sb.Append(Charset.Printable[B[Pos++]]);
                    var pv = Vars.FirstOrDefault(x => x.Name == sb.ToString() && x.Kind.StartsWith("number"));
                    if (pv == null) return false;
                    return double.TryParse(pv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
                }
                return false;
            }

            /// <summary>+ - * / with the usual priorities.</summary>
            public bool TryExpr(out double v)
            {
                if (!TryTerm(out v)) return false;
                while (Peek() == 0x15 || Peek() == 0x16)
                {
                    int op = B[Pos++]; double r;
                    if (!TryTerm(out r)) return false;
                    v = op == 0x15 ? v + r : v - r;
                }
                return true;
            }

            bool TryTerm(out double v)
            {
                if (!TryPrimary(out v)) return false;
                while (Peek() == 0x17 || Peek() == 0x18)
                {
                    int op = B[Pos++]; double r;
                    if (!TryPrimary(out r)) return false;
                    if (op == 0x18 && r == 0) return false;
                    v = op == 0x17 ? v * r : v / r;
                }
                return true;
            }
        }

        // ------------------------------------------------------------------
        //  .asm export
        // ------------------------------------------------------------------
        /// <summary>The machine code and data as an assembly listing that assemblers such as
        /// pasmo or z80asm can assemble back to the same bytes (ORG + DB/instructions).</summary>
        public static string ToAsm(PFile p, CodeAnalysis ca, string sourceName)
        {
            var used = new SortedDictionary<int, string>();
            Func<int, string> label = a =>
            {
                if (ca.Labels.Contains(a)) return "L" + a.ToString("X4");
                string k = Z80Disasm.KnownLabel(a);
                if (k != null && !k.EndsWith("+1")) { used[a] = k; return k; }
                if (k != null) { used[a - 1] = k.Substring(0, k.Length - 2); return k; }
                return null;
            };
            var body = new StringBuilder();
            foreach (var blk in ca.Blocks)
            {
                body.AppendLine();
                body.AppendLine("; ---- " + p.Region(blk[0]) + " ----");
                body.AppendLine("        ORG " + Z80Disasm.Hex(blk[0], 4));
                int a = blk[0];
                while (a < blk[1])
                {
                    if (ca.Labels.Contains(a)) body.AppendLine("L" + a.ToString("X4") + ":");
                    if (ca.InstrStarts.Contains(a))
                    {
                        var ins = Z80Disasm.Decode(p.Mem, a, label);
                        if (a + ins.Len <= blk[1])
                        {
                            body.Append("        ").Append(ins.Text.PadRight(24)).Append("; ").Append(a.ToString("X4")).Append("  ");
                            for (int k = 0; k < ins.Len; k++) body.Append(p.Mem[a + k].ToString("X2")).Append(' ');
                            body.AppendLine();
                            a += ins.Len;
                            continue;
                        }
                    }
                    // data: up to 8 bytes, stopping at the next label or instruction
                    var db = new List<string>();
                    int st = a;
                    do { db.Add(Z80Disasm.Hex(p.Mem[a], 2)); a++; }
                    while (a < blk[1] && db.Count < 8 && !ca.InstrStarts.Contains(a) && !ca.Labels.Contains(a));
                    body.Append("        DB ").Append(string.Join(",", db).PadRight(22)).Append("; ").Append(st.ToString("X4"));
                    if (ca.CalcBytes[st]) body.Append("  " + Z80Disasm.CalcComment(p.Mem, st, a - st));
                    body.AppendLine();
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine("; Machine code from " + sourceName);
            sb.AppendLine("; Disassembled by the ZX81 toolset decompiler.");
            sb.AppendLine(";");
            foreach (var e in ca.Entries) sb.AppendLine("; Entry: " + Z80Disasm.Hex(e.Address, 4) + " (" + e.Address + ") - " + e.Source);
            foreach (var n in ca.Notes) sb.AppendLine("; " + n);
            sb.AppendLine();
            foreach (var kv in used) sb.AppendLine((kv.Value + "").PadRight(12) + "EQU " + Z80Disasm.Hex(kv.Key, 4));
            sb.Append(body);
            if (ca.Blocks.Count == 0) sb.AppendLine("; (no machine code found)");
            return sb.ToString();
        }
    }
}
