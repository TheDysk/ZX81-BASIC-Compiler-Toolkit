// =====================================================================================
//  ControlFile.cs - decompiler control files (.ctl)
// -------------------------------------------------------------------------------------
//  A .ctl file sits next to a program (game.p -> game.ctl) and tells the decompiler
//  what it can't work out for itself. One directive per line; blank lines and lines
//  starting with ; or # are comments. Addresses can be decimal (16514) or hex (4082h,
//  0x4082, $4082 or #4082).
//
//      LABEL   4082h  start             name an address in the .asm (instead of L4082)
//      COMMENT 4090h  wait for a key    a comment line above that address in the .asm
//      CODE    4100h  [4180h]           disassemble from here (code no USR reaches, e.g.
//                                       reached through a table or a stack trick); with
//                                       an end address, the whole range [start, end) is
//                                       decoded instruction by instruction
//      DATA    4200h  64                these bytes are data (DB), never code: tracing
//                                       stops there
//
//  CodeFinder.Analyze uses CODE and DATA; CodeFinder.ToAsm uses LABEL and COMMENT.
//  The decompiler loads <program>.ctl automatically (zx81dc --ctl file for another),
//  and its GUI can save one, including labels and comments added from the Machine
//  code tab.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZX81Compiler
{
    public class ControlFile
    {
        public readonly SortedDictionary<int, string> Labels = new SortedDictionary<int, string>();
        public readonly SortedDictionary<int, List<string>> Comments = new SortedDictionary<int, List<string>>();
        /// <summary>CODE ranges: [start, end) with end = -1 when only the start was given.</summary>
        public readonly List<int[]> Code = new List<int[]>();
        /// <summary>DATA ranges: [start, end).</summary>
        public readonly List<int[]> Data = new List<int[]>();
        /// <summary>Lines that couldn't be understood ("line 3: ...").</summary>
        public readonly List<string> Problems = new List<string>();

        static readonly Regex Name = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

        public bool IsEmpty { get { return Labels.Count == 0 && Comments.Count == 0 && Code.Count == 0 && Data.Count == 0; } }

        /// <summary>The control file that goes with a program: same folder and name, .ctl.</summary>
        public static string SidecarPath(string programPath) { return Path.ChangeExtension(programPath, ".ctl"); }

        /// <summary>The sidecar of a program if there is one, else null. problems gets any trouble reading it.</summary>
        public static ControlFile LoadSidecar(string programPath)
        {
            string f = SidecarPath(programPath);
            return File.Exists(f) ? Load(f) : null;
        }

        public static ControlFile Load(string path) { return Parse(File.ReadAllText(path)); }

        /// <summary>An address: decimal, or hex as 4082h, 0x4082, $4082 or #4082. -1 if it isn't one.</summary>
        public static int ParseAddr(string s)
        {
            s = (s ?? "").Trim();
            int v;
            string hex = null;
            if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase) && s.Length > 1) hex = s.Substring(0, s.Length - 1);
            else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = s.Substring(2);
            else if (s.StartsWith("$") || s.StartsWith("#")) hex = s.Substring(1);
            if (hex != null) return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) && v >= 0 && v <= 0xFFFF ? v : -1;
            return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v) && v <= 0xFFFF ? v : -1;
        }

        public static ControlFile Parse(string text)
        {
            var c = new ControlFile();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#")) continue;
                var parts = t.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
                string kw = parts[0].ToUpperInvariant();
                Action<string> bad = m => c.Problems.Add("line " + (i + 1) + ": " + m + ": " + t);
                if (parts.Length < 2) { bad("an address is missing"); continue; }
                int a = ParseAddr(parts[1]);
                if (a < 0) { bad("'" + parts[1] + "' is not an address"); continue; }
                string rest = parts.Length > 2 ? parts[2].Trim() : "";
                switch (kw)
                {
                    case "LABEL":
                        if (!Name.IsMatch(rest)) { bad("a label must be letters, digits and _ (not starting with a digit)"); continue; }
                        c.Labels[a] = rest;
                        break;
                    case "COMMENT":
                        if (rest.Length == 0) { bad("the comment text is missing"); continue; }
                        c.AddComment(a, rest);
                        break;
                    case "CODE":
                        {
                            int end = -1;
                            if (rest.Length > 0)
                            {
                                end = ParseAddr(rest.Split(' ', '\t')[0]);
                                if (end <= a) { bad("the end address must be after the start"); continue; }
                            }
                            c.Code.Add(new[] { a, end });
                            break;
                        }
                    case "DATA":
                        {
                            int len = ParseAddr(rest.Split(' ', '\t')[0]);
                            if (len <= 0) { bad("DATA needs a length (in bytes) after the address"); continue; }
                            c.Data.Add(new[] { a, Math.Min(0x10000, a + len) });
                            break;
                        }
                    default:
                        bad("unknown directive (use LABEL, COMMENT, CODE or DATA)");
                        continue;
                }
            }
            return c;
        }

        public void AddComment(int a, string text)
        {
            List<string> l;
            if (!Comments.TryGetValue(a, out l)) Comments[a] = l = new List<string>();
            l.Add(text);
        }

        /// <summary>Is a inside a DATA range?</summary>
        public bool IsData(int a)
        {
            foreach (var d in Data) if (a >= d[0] && a < d[1]) return true;
            return false;
        }

        /// <summary>A valid label name?</summary>
        public static bool ValidName(string s) { return s != null && Name.IsMatch(s); }

        /// <summary>The control file as text (what Load reads back).</summary>
        public string Save(string programName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; Decompiler control file" + (programName != null ? " for " + programName : ""));
            sb.AppendLine("; LABEL addr name / COMMENT addr text / CODE start [end] / DATA start length");
            foreach (var r in Code) sb.AppendLine("CODE    " + H(r[0]) + (r[1] >= 0 ? " " + H(r[1]) : ""));
            foreach (var r in Data) sb.AppendLine("DATA    " + H(r[0]) + " " + (r[1] - r[0]));
            foreach (var kv in Labels) sb.AppendLine("LABEL   " + H(kv.Key) + " " + kv.Value);
            foreach (var kv in Comments) foreach (var t in kv.Value) sb.AppendLine("COMMENT " + H(kv.Key) + " " + t);
            return sb.ToString();
        }

        static string H(int a) { return a.ToString("X4") + "h"; }

        /// <summary>One line for reports: what the control file contains.</summary>
        public string Summary(bool withProblems = true)
        {
            return Labels.Count + " label(s), " + Comments.Sum(kv => kv.Value.Count) + " comment(s), " + Code.Count + " CODE and " + Data.Count + " DATA range(s)" +
                (Problems.Count > 0 ? "; " + Problems.Count + " line(s) not understood" + (withProblems ? " (" + string.Join("; ", Problems) + ")" : "") : "");
        }
    }
}
