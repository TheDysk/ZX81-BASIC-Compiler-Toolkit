// =====================================================================================
//  Labels.cs - BASIC written with labels instead of (or as well as) line numbers,
//              and renumbering
// -------------------------------------------------------------------------------------
//  A listing may leave out line numbers and use labels for GOTO and GOSUB:
//
//      @start:
//      PRINT "HELLO"
//      @loop: IF INKEY$="" THEN GOTO @loop
//      GOSUB @beep
//      GOTO @start
//      9000 @beep: PRINT "BEEP"        (explicit numbers can be mixed in)
//      RETURN
//
//  @name: defines a label, on a line of its own (it labels the next statement) or in
//  front of a statement. @name anywhere else in a statement (outside strings) is
//  replaced by that line's number, so it works after GOTO, GO SUB, THEN, RUN, LIST ...
//  and even in expressions (GOTO @table+N*10). Label names are letters, digits and _,
//  and are not case sensitive.
//
//  Lines without a number get one: 10 more than the line before (10 for the first). If
//  that would run into the next explicit number, the lines in between are spaced out
//  evenly; if there is no room at all, or a number goes past 9999, that is an error.
//
//  Labels are used when the listing has an @name: definition, a #LABELS line, or the
//  --labels option (Options.Labels); then unnumbered lines are numbered too.
//  Compiler.Compile calls Number() with keepLayout, so every text line stays on the
//  same line and error messages still point at the right place in the editor.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZX81Compiler
{
    public static class Labels
    {
        /// <summary>The directive that switches label mode on.</summary>
        public const string Directive = "#LABELS";

        static readonly Regex Def = new Regex(@"^@([A-Za-z_][A-Za-z0-9_]*)\s*:", RegexOptions.Compiled);

        static bool IsComment(string t) { return t.StartsWith("#") || t.StartsWith(";") || t.StartsWith("//"); }

        /// <summary>Does the listing use labels? (a #LABELS line, an @name: definition
        /// at the start of a line, after an optional line number, or a GOTO / GO SUB /
        /// THEN / RUN / LIST @name outside a string)</summary>
        public static bool Wanted(string text)
        {
            foreach (var raw in Lines(text))
            {
                string t = raw.Trim();
                if (t.ToUpperInvariant() == Directive) return true;
                if (IsComment(t)) continue;
                int p = 0;
                while (p < t.Length && char.IsDigit(t[p])) p++;
                if (Def.IsMatch(t.Substring(p).TrimStart())) return true;
                // a reference (GOTO @name ...): labels are meant, even if the definition is
                // missing - that then gets a clear "unknown label" error
                if (Ref.IsMatch(Regex.Replace(t, "\"[^\"]*\"?", "\"\""))) return true;
            }
            return false;
        }
        static readonly Regex Ref = new Regex(@"\b(GO\s*TO|GO\s*SUB|THEN|RUN|LIST|LLIST)\s*@[A-Za-z_]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static string[] Lines(string text) { return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'); }

        class SrcLine
        {
            public int Index;            // text line (0-based)
            public int Number = -1;      // explicit number, or -1
            public string Body = "";     // the statement
            public List<KeyValuePair<string, int>> Labels = new List<KeyValuePair<string, int>>();   // (name, text line it is written on)
            public bool IsStatement;     // false: blank, comment or label-only line
        }

        /// <summary>Turn a listing that uses labels and/or unnumbered lines into an ordinary
        /// numbered listing. keepLayout: one output line for every input line (a line
        /// that only defines a label becomes blank), so text line numbers don't change.
        /// Errors go into diags (with the text line); returns null if there were any.</summary>
        public static string Number(string text, bool keepLayout, List<Diagnostic> diags)
        {
            var src = Lines(text);
            var lines = new List<SrcLine>();
            var pending = new List<KeyValuePair<string, int>>();   // labels waiting for their statement (name, text line)
            int errors = diags.Count(d => d.IsError), firstDiag = diags.Count;
            for (int i = 0; i < src.Length; i++)
            {
                var sl = new SrcLine { Index = i };
                lines.Add(sl);
                string t = src[i].Trim();
                if (t.Length == 0 || IsComment(t)) continue;
                int p = 0;
                while (p < t.Length && char.IsDigit(t[p])) p++;
                if (p > 0)
                {
                    long n;
                    sl.Number = long.TryParse(t.Substring(0, Math.Min(p, 9)), out n) && n <= 99999 ? (int)n : 99999;
                }
                string rest = t.Substring(p).TrimStart();
                var m = Def.Match(rest);
                while (m.Success)
                {
                    sl.Labels.Add(new KeyValuePair<string, int>(m.Groups[1].Value, i));
                    rest = rest.Substring(m.Length).TrimStart();
                    m = Def.Match(rest);
                }
                sl.Body = rest;
                if (sl.Number < 0 && rest.Length == 0)
                {
                    // a label on a line of its own: it belongs to the next statement
                    pending.AddRange(sl.Labels);
                    sl.Labels.Clear();
                    continue;
                }
                sl.IsStatement = true;
                sl.Labels.InsertRange(0, pending);
                pending.Clear();
            }
            foreach (var l in pending)
                diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = l.Value + 1, Message = "label @" + l.Key + " has no BASIC line after it" });

            // numbers for the lines that have none
            var stmts = lines.Where(l => l.IsStatement).ToList();
            int prev = 0;
            for (int k = 0; k < stmts.Count; )
            {
                if (stmts[k].Number >= 0)
                {
                    if (stmts[k].Number < 1 || stmts[k].Number > 9999)
                        diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = stmts[k].Index + 1, Message = "line number " + stmts[k].Number + " out of range (1-9999)" });
                    prev = stmts[k].Number;
                    k++;
                    continue;
                }
                int j = k;
                while (j < stmts.Count && stmts[j].Number < 0) j++;
                int count = j - k;
                int next = j < stmts.Count ? stmts[j].Number : -1;      // the next explicit number
                int step = 10;
                if (next > prev && prev + 10 * count >= next)
                {
                    step = (next - prev) / (count + 1);
                    if (step < 1)
                    {
                        diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = stmts[k].Index + 1, Message = "no room to number " + count + " line" + (count == 1 ? "" : "s") + " between lines " + prev + " and " + next + " (renumber the program, or leave out the explicit number " + next + ")" });
                        step = 1;
                    }
                }
                for (int q = k; q < j; q++)
                {
                    prev += step;
                    stmts[q].Number = prev;
                    if (prev > 9999)
                    {
                        diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = stmts[q].Index + 1, Message = "the line numbers run past 9999 here (numbering in steps of " + step + " from the line before); use fewer lines or explicit numbers" });
                        break;
                    }
                }
                k = j;
            }

            // the labels
            var where = new Dictionary<string, SrcLine>(StringComparer.OrdinalIgnoreCase);
            var defLine = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var sl in stmts)
                foreach (var l in sl.Labels)
                {
                    if (where.ContainsKey(l.Key))
                        diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = l.Value + 1, Message = "label @" + l.Key + " is defined twice (also on text line " + (defLine[l.Key] + 1) + ")" });
                    else { where[l.Key] = sl; defLine[l.Key] = l.Value; }
                }

            // replace @name by line numbers, and build the output
            var sb = new StringBuilder();
            foreach (var sl in lines)
            {
                if (!keepLayout && sl.Index == src.Length - 1 && src[sl.Index].Length == 0) break;   // the end of the last line
                string outLine;
                if (sl.IsStatement)
                {
                    string body = ReplaceRefs(sl.Body, name =>
                    {
                        SrcLine target;
                        if (where.TryGetValue(name, out target)) return target.Number.ToString();
                        diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = sl.Index + 1, Message = "label @" + name + " is not defined" });
                        return "0";
                    });
                    outLine = sl.Number + (body.Length > 0 ? " " + body : "");
                }
                else
                {
                    string t = src[sl.Index].Trim();
                    if (t.ToUpperInvariant() == Directive) t = "";       // done its job
                    else if (!(t.Length == 0 || IsComment(t))) t = "";   // a label-only line
                    outLine = t;
                    if (!keepLayout && outLine.Length == 0 && src[sl.Index].Trim().Length > 0) continue;
                }
                sb.Append(outLine).Append("\r\n");
            }
            // messages in text order
            var mine = diags.Skip(firstDiag).OrderBy(d => d.SourceLine).ToList();
            diags.RemoveRange(firstDiag, diags.Count - firstDiag);
            diags.AddRange(mine);
            if (diags.Count(d => d.IsError) > errors) return null;
            string res = sb.ToString();
            return keepLayout ? res.Substring(0, Math.Max(0, res.Length - 2)) : res;
        }

        /// <summary>Replace each @name outside "strings" with what repl gives for it.</summary>
        static string ReplaceRefs(string s, Func<string, string> repl)
        {
            var sb = new StringBuilder();
            bool inStr = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') { inStr = !inStr; sb.Append(c); continue; }   // "" inside a string toggles twice
                if (!inStr && c == '@' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '_'))
                {
                    int j = i + 1;
                    while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                    sb.Append(repl(s.Substring(i + 1, j - i - 1)));
                    i = j - 1;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- renumbering
        static readonly Regex Target = new Regex(@"\b(GO\s*TO|GO\s*SUB|THEN|RUN|LIST|LLIST)(\s*)(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex AnyJump = new Regex(@"\b(GO\s*TO|GO\s*SUB|RUN)\b\s*\S", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Renumber a listing: start, start+step, ... in line-number order. GOTO,
        /// GO SUB, THEN, RUN, LIST and LLIST followed by a constant line number are changed
        /// to match (a number with no line goes to the next line after it, as on the ZX81).
        /// A listing with labels or unnumbered lines is numbered first. Calculated targets
        /// (GOTO 100+N) can't be changed: each gets a warning. Returns null on errors.</summary>
        public static string Renumber(string text, int start, int step, List<Diagnostic> diags)
        {
            if (start < 1 || step < 1) { diags.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = "the first line number and the step must be at least 1" }); return null; }
            if (Wanted(text) || HasUnnumbered(text))
            {
                text = Number(text, false, diags);
                if (text == null) return null;
            }
            var src = Lines(text);
            var nums = new List<KeyValuePair<int, int>>();            // (old number, text line)
            for (int i = 0; i < src.Length; i++)
            {
                string t = src[i].Trim();
                int p = 0;
                while (p < t.Length && char.IsDigit(t[p])) p++;
                if (p > 0 && p <= 5) nums.Add(new KeyValuePair<int, int>(int.Parse(t.Substring(0, p)), i));
            }
            var order = nums.Select(x => x.Key).Distinct().OrderBy(x => x).ToList();
            if (order.Count == 0) return text;
            long last = start + (long)step * (order.Count - 1);
            if (last > 9999)
            {
                diags.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = order.Count + " lines from " + start + " in steps of " + step + " would go up to " + last + "; line numbers stop at 9999" });
                return null;
            }
            var map = new Dictionary<int, int>();
            for (int k = 0; k < order.Count; k++) map[order[k]] = start + step * k;
            Func<int, int> newFor = n =>
            {
                int m;
                if (map.TryGetValue(n, out m)) return m;
                foreach (int o in order) if (o >= n) return map[o];    // no such line: the next one
                return (int)last + 1;                                     // past the end: still past the end
            };
            var sb = new StringBuilder();
            int count = src.Length > 0 && src[src.Length - 1].Length == 0 ? src.Length - 1 : src.Length;   // (text ending in a newline)
            for (int i = 0; i < count; i++)
            {
                string line = src[i];
                string t = line.Trim();
                int p = 0;
                while (p < t.Length && char.IsDigit(t[p])) p++;
                if (p == 0 || p > 5) { sb.Append(line).Append("\r\n"); continue; }
                int old = int.Parse(t.Substring(0, p));
                string body = t.Substring(p);
                string code = Unquoted(body);
                var m = Target.Match(code);
                if (m.Success)
                {
                    int tgt = int.Parse(m.Groups[3].Value);
                    // the same position in the real text (strings are blanked in code, not moved)
                    body = body.Substring(0, m.Groups[3].Index) + newFor(tgt) + body.Substring(m.Groups[3].Index + m.Groups[3].Length);
                }
                else if (AnyJump.IsMatch(code))
                    diags.Add(new Diagnostic { BasicLine = map[old], SourceLine = i + 1, Message = "the line number after " + AnyJump.Match(code).Groups[1].Value.ToUpperInvariant() + " is worked out, so it can't be renumbered: check it (it was line " + old + ")" });
                sb.Append(map[old]).Append(body).Append("\r\n");
            }
            string res = sb.ToString();
            return res.EndsWith("\r\n") && !text.EndsWith("\n") ? res.Substring(0, res.Length - 2) : res;
        }

        static bool HasUnnumbered(string text)
        {
            foreach (var raw in Lines(text))
            {
                string t = raw.Trim();
                if (t.Length == 0 || IsComment(t)) continue;
                if (!char.IsDigit(t[0])) return true;
            }
            return false;
        }

        /// <summary>The text with everything inside "strings" replaced by spaces (same length).</summary>
        static string Unquoted(string s)
        {
            var c = s.ToCharArray();
            bool inStr = false;
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i] == '"') { inStr = !inStr; continue; }
                if (inStr) c[i] = ' ';
            }
            return new string(c);
        }
    }
}
