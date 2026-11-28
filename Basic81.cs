// =====================================================================================
//  Basic81.cs - converting between stored (tokenised) ZX81 BASIC and text
// -------------------------------------------------------------------------------------
//  The ZX81 stores BASIC "tokenised": each keyword is one byte (PRINT = F5h), other
//  characters use the ZX81 character codes, and each number is followed by 7Eh and
//  its 5-byte value (see ZxFloat.cs).
//
//  This file has three parts:
//
//  1. DETOKENISER (bytes -> text). Two styles:
//     * Exact   - for saving listings that can be turned back into IDENTICAL bytes
//                 by the tokeniser. Anything that has no plain-text form is written as
//                 an escape: \hh (any byte in hex), \'. (block graphics), %A (inverse).
//                 Machine code in REM lines becomes a run of \hh escapes. Every line is
//                 checked by tokenising it again; if the bytes don't match, the whole
//                 line is written as \hh escapes, which always round-trips.
//     * Display - for showing on screen: Unicode block graphics, inverse characters
//                 marked, keywords inside strings shown as words, hidden numbers hidden
//                 or shown as {=value}.
//
//  2. TOKENISER (text -> bytes). Reads the same listing format the compiler reads
//     (numbered lines, keywords as words, escapes in strings), plus:
//       - \hh is allowed anywhere, and inserts that byte exactly
//       - after a number, the 7Eh + 5 bytes are added automatically, unless the text
//         already gives them explicitly as \7E\hh\hh\hh\hh\hh
//       - after REM the rest of the line is copied as characters (no keywords)
//       - "" inside a string is the ZX81 quote-image token C0h
//       - spaces next to keywords are left out (the ZX81 adds them when listing);
//         other spaces outside strings are kept as space characters
//       - lines stay in the order written (ZX81 programs sometimes break the rules
//         with out-of-order or repeated line numbers, and this keeps them exact)
//
//  3. BUILDER: puts tokenised lines together into a complete .p file, optionally
//     keeping the display file, variables and system variables of an original file.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;

namespace ZX81Compiler
{
    public enum ListStyle { Exact, Display }

    /// <summary>Options for turning a line into text.</summary>
    public class DetokOptions
    {
        public ListStyle Style = ListStyle.Exact;
        /// <summary>Display style: show the hidden 5-byte numbers as {=value}.</summary>
        public bool ShowFloats;
        /// <summary>Display style: show REM lines that contain machine code as a short note.</summary>
        public bool SummariseCodeRems = true;
    }

    /// <summary>A line converted to text.</summary>
    public class LineText
    {
        public string Text;                                     // the text (without the line number)
        public List<KeyValuePair<int, int>> Inverse = new List<KeyValuePair<int, int>>();  // Display style: (start, length) of inverse characters
        public bool IsCodeRem;                                  // a REM line holding machine code
        public bool FullyEscaped;                               // Exact style: the normal form didn't round-trip, so every byte is \hh
        public int FloatMismatches;                             // numbers whose hidden value differs from the digits shown
    }

    public static class Basic81
    {
        // ------------------------------------------------------------------
        //  Keyword tables
        // ------------------------------------------------------------------
        /// <summary>Keyword text -> token byte, e.g. "PRINT" -> F5h.</summary>
        public static readonly Dictionary<string, byte> KeywordCodes = new Dictionary<string, byte>();

        static Basic81()
        {
            for (int c = 0xC1; c <= 0xFF; c++)
                if (c != 0xC3 && Charset.Tokens[c] != null) KeywordCodes[Charset.Tokens[c]] = (byte)c;
            KeywordCodes["RND"] = 0x40; KeywordCodes["INKEY$"] = 0x41; KeywordCodes["PI"] = 0x42;
        }

        static bool IsToken(int b) { return b >= 0xC0 || (b >= 0x40 && b <= 0x42); }
        static bool IsLetterCode(int b) { return b >= 0x26 && b <= 0x3F; }
        static bool IsDigitCode(int b) { return b >= 0x1C && b <= 0x25; }

        /// <summary>Is a keyword token made of letters (so it gets spaces round it)?</summary>
        static bool IsWordToken(int b) { string t = Charset.Tokens[b]; return t != null && char.IsLetter(t[0]); }

        // ------------------------------------------------------------------
        //  Characters
        // ------------------------------------------------------------------
        static readonly string[] UnicodeBlocks = { " ", "▘", "▝", "▀", "▖", "▌", "▞", "▛", "▒", "░", "▒" };
        static readonly string[] UnicodeInvBlocks = { "█", "▟", "▙", "▄", "▜", "▐", "▚", "▗", "▒", "░", "▒" };

        /// <summary>Display text for one ZX81 character code (Unicode graphics; inverse letters shown
        /// as the normal letter - the caller marks them). Codes that aren't characters give [hh].</summary>
        public static string CodeToDisplay(int c)
        {
            int b = c & 0x7F;
            if (c < 0x40 || (c >= 0x80 && c < 0xC0))
            {
                if (b <= 0x0A) return (c & 0x80) != 0 ? UnicodeInvBlocks[b] : UnicodeBlocks[b];
                return Charset.Printable[b].ToString();
            }
            if (Charset.Tokens[c] != null) return Charset.Tokens[c];
            return "[" + c.ToString("X2") + "]";
        }

        /// <summary>A run of codes as display text (used for variables and the screen).</summary>
        public static string CodesToDisplay(byte[] mem, int start, int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count && start + i < mem.Length; i++)
            {
                int c = mem[start + i];
                string s = CodeToDisplay(c);
                if (c >= 0xC0 && IsWordToken(c)) s = " " + s + " ";
                sb.Append(s);
            }
            return sb.ToString();
        }

        /// <summary>Exact-style text for a code inside a string or REM: a plain character, a block
        /// escape (\'. etc.), %X for inverse characters, or \hh.</summary>
        static string CodeToExact(int c)
        {
            if (c == 0x00) return " ";
            if (c >= 0x01 && c <= 0x07) return BlockEscape(c);
            if (c >= 0x80 && c <= 0x87) return BlockEscape(c);
            if (c >= 0x0B && c < 0x40) return Charset.Printable[c].ToString();
            if (c >= 0x8B && c < 0xC0) return "%" + Charset.Printable[c & 0x3F];
            return "\\" + c.ToString("X2");
        }

        /// <summary>Block graphic code -> zmakebas-style escape: \ then left half, right half,
        /// each ' (top), . (bottom), : (both) or space.</summary>
        static string BlockEscape(int c)
        {
            int mask = c < 8 ? c : (c ^ 0x8F);
            char[] half = { ' ', '\'', '.', ':' };
            int l = (mask & 1) | ((mask & 4) >> 1);
            int r = ((mask & 2) >> 1) | ((mask & 8) >> 2);
            return "\\" + half[l] + half[r];
        }

        // ------------------------------------------------------------------
        //  Machine code detection
        // ------------------------------------------------------------------
        /// <summary>Does a REM line's content look like machine code rather than text?
        /// Text never contains codes 43h-7Fh (they can't be typed), so any of them means code.</summary>
        public static bool LooksLikeCode(byte[] body, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                int b = body[i];
                if (b >= 0x43 && b <= 0x7F) return true;
            }
            return false;
        }

        /// <summary>Is this line "REM" followed by machine code? (body includes the final 76h)</summary>
        public static bool IsCodeRem(byte[] body)
        {
            return body.Length > 1 && body[0] == 0xEA && LooksLikeCode(body, 1, body.Length - 1);
        }

        // ------------------------------------------------------------------
        //  DETOKENISER
        // ------------------------------------------------------------------
        /// <summary>Convert the body of a line (tokens + final 76h) to text.</summary>
        public static LineText Detokenize(byte[] body, DetokOptions o)
        {
            var lt = DetokenizeInner(body, o, false);
            if (o.Style == ListStyle.Exact)
            {
                // check that tokenising the text gives back the same bytes
                byte[] back = null;
                try { back = TokenizeBody(lt.Text, null); } catch (Exception) { }
                if (back == null || !SameBytes(back, body))
                {
                    lt = DetokenizeInner(body, o, true);
                    lt.FullyEscaped = true;
                }
            }
            return lt;
        }

        /// <summary>Where the hidden numbers are in a line: the offset of each 7Eh marker in
        /// the body (the 5 bytes of the number follow it), in the same order as the
        /// {=value} annotations Detokenize writes. Strings and REM text are skipped.</summary>
        public static List<int> HiddenNumberOffsets(byte[] body)
        {
            var r = new List<int>();
            int end = body.Length;
            if (end > 0 && body[end - 1] == 0x76) end--;
            bool inStr = false;
            for (int i = 0; i < end; i++)
            {
                int c = body[i];
                if (!inStr && c == 0xEA) break;                 // REM: the rest isn't tokenised
                if (c == 0x0B) { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == 0x7E && i + 5 <= end) { r.Add(i); i += 5; }
            }
            return r;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static LineText DetokenizeInner(byte[] body, DetokOptions o, bool allEscaped)
        {
            var lt = new LineText();
            var sb = new StringBuilder();
            int end = body.Length;
            if (end > 0 && body[end - 1] == 0x76) end--;   // the line end isn't written
            bool exact = o.Style == ListStyle.Exact;

            if (allEscaped)
            {
                for (int i = 0; i < end; i++) sb.Append('\\').Append(body[i].ToString("X2"));
                lt.Text = sb.ToString();
                return lt;
            }

            bool inStr = false;
            for (int i = 0; i < end; i++)
            {
                int c = body[i];

                // ---- REM: the rest of the line is not tokenised ----
                if (!inStr && c == 0xEA)
                {
                    AppendKeyword(sb, c);
                    lt.IsCodeRem = LooksLikeCode(body, i + 1, end);
                    if (i + 1 >= end) break;
                    if (exact)
                    {
                        // AppendKeyword has already added the one space the tokeniser skips after REM
                        for (int k = i + 1; k < end; k++)
                        {
                            int r = body[k];
                            // trailing spaces could be lost by text editors, so escape them
                            bool trailingSpace = r == 0 && AllSpaces(body, k, end);
                            if (lt.IsCodeRem || trailingSpace) sb.Append('\\').Append(r.ToString("X2"));
                            else sb.Append(CodeToExact(r));
                        }
                    }
                    else if (lt.IsCodeRem && o.SummariseCodeRems)
                    {
                        sb.Append("[machine code: " + (end - i - 1) + " bytes]");
                    }
                    else
                    {
                        for (int k = i + 1; k < end; k++) AppendDisplayChar(sb, lt, body[k], true);
                    }
                    break;
                }

                // ---- quotes ----
                if (c == 0x0B) { inStr = !inStr; sb.Append('"'); continue; }
                if (inStr)
                {
                    if (c == 0xC0) { sb.Append("\"\""); continue; }
                    if (exact) sb.Append(CodeToExact(c));
                    else AppendDisplayChar(sb, lt, c, true);
                    continue;
                }

                // ---- hidden number ----
                if (c == 0x7E && i + 5 < end + 1 && i + 5 <= end)
                {
                    string numText = NumberTextBefore(body, i);
                    bool match = numText.Length > 0 && i + 6 <= body.Length && ZxFloat.Matches(numText, body, i + 1);
                    if (!match) lt.FloatMismatches++;
                    if (exact)
                    {
                        if (!match) for (int k = i; k <= i + 5 && k < end; k++) sb.Append('\\').Append(body[k].ToString("X2"));
                    }
                    else if (o.ShowFloats)
                    {
                        sb.Append("{=" + ZxFloat.Format(ZxFloat.Decode(body, i + 1)) + (match ? "" : " !") + "}");
                    }
                    i += 5;
                    continue;
                }

                // ---- keywords ----
                if (IsToken(c) && c != 0xC0)
                {
                    AppendKeyword(sb, c);
                    continue;
                }

                // ---- names: escape the first letter if the tokeniser would read a keyword ----
                if (exact && IsLetterCode(c))
                {
                    int j = i;
                    var word = new StringBuilder();
                    while (j < end && (IsLetterCode(body[j]) || IsDigitCode(body[j]))) { word.Append(Charset.Printable[body[j]]); j++; }
                    bool dollar = j < end && body[j] == 0x0D;
                    // letters GO before a TO keyword would be read back as GO TO (one token)
                    bool goTo = word.ToString() == "GO" && j < end && (body[j] == 0xDF || body[j] == 0x00);
                    if (goTo || WouldBeKeyword(word.ToString(), dollar))
                    {
                        sb.Append('\\').Append(c.ToString("X2"));
                        for (int k = i + 1; k < j; k++) sb.Append(Charset.Printable[body[k]]);
                    }
                    else sb.Append(word.ToString());
                    i = j - 1;
                    continue;
                }

                // ---- digits not followed by a hidden number (e.g. after a variable name's letters) ----
                if (exact && (IsDigitCode(c) || c == 0x1B) && !NumberHasFloat(body, i, end) && !PrevIsNameChar(body, i))
                {
                    sb.Append('\\').Append(c.ToString("X2"));
                    continue;
                }

                // ---- ordinary characters ----
                if (c == 0x00)
                {
                    // a stored space: write it as a space unless next to a keyword
                    // (spaces next to keywords are dropped by the tokeniser)
                    bool nearKw = (i > 0 && IsToken(body[i - 1])) || (i + 1 < end && IsToken(body[i + 1])) || i == 0;
                    if (exact && nearKw) sb.Append("\\00"); else sb.Append(' ');
                    continue;
                }
                if (c > 0x00 && c < 0x40 && !(c >= 0x01 && c <= 0x0A))
                {
                    sb.Append(Charset.Printable[c]);
                    continue;
                }
                if (exact) sb.Append('\\').Append(c.ToString("X2"));
                else AppendDisplayChar(sb, lt, c, false);
            }
            lt.Text = sb.ToString();
            return lt;
        }

        static bool AllSpaces(byte[] b, int from, int end)
        {
            for (int k = from; k < end; k++) if (b[k] != 0) return false;
            return true;
        }

        static bool PrevIsNameChar(byte[] b, int i)
        {
            int k = i - 1;
            while (k >= 0 && (IsDigitCode(b[k]))) k--;
            return k >= 0 && IsLetterCode(b[k]);
        }

        /// <summary>Do the number characters starting at i end with 7Eh (a hidden number)?</summary>
        static bool NumberHasFloat(byte[] b, int i, int end)
        {
            int k = i;
            while (k < end && (IsDigitCode(b[k]) || b[k] == 0x1B || b[k] == 0x2A || ((b[k] == 0x15 || b[k] == 0x16) && k > 0 && b[k - 1] == 0x2A))) k++;
            return k < end && b[k] == 0x7E;
        }

        /// <summary>The number as written just before a 7Eh marker, e.g. "1.5" or "4E4".</summary>
        static string NumberTextBefore(byte[] b, int i)
        {
            int k = i;
            while (k > 0)
            {
                int c = b[k - 1];
                if (IsDigitCode(c) || c == 0x1B) { k--; continue; }
                if (c == 0x2A && k < i) { k--; continue; }                                  // E
                if ((c == 0x15 || c == 0x16) && k - 1 > 0 && b[k - 2] == 0x2A && k < i) { k--; continue; }   // sign after E
                break;
            }
            var sb = new StringBuilder();
            for (int j = k; j < i; j++) sb.Append(Charset.Printable[b[j]]);
            string s = sb.ToString();
            while (s.Length > 0 && !char.IsDigit(s[0]) && s[0] != '.') s = s.Substring(1);
            return s;
        }

        /// <summary>Would the tokeniser read this word as a keyword?</summary>
        static bool WouldBeKeyword(string w, bool followedByDollar)
        {
            if (followedByDollar && KeywordCodes.ContainsKey(w + "$")) return true;
            if (KeywordCodes.ContainsKey(w)) return true;
            int d = w.Length; while (d > 0 && char.IsDigit(w[d - 1])) d--;
            return d < w.Length && d > 0 && KeywordCodes.ContainsKey(w.Substring(0, d));
        }

        static void AppendKeyword(StringBuilder sb, int c)
        {
            string t = Charset.Tokens[c];
            if (IsWordToken(c))
            {
                if (sb.Length > 0)
                {
                    char p = sb[sb.Length - 1];
                    if (char.IsLetterOrDigit(p) || p == '$' || p == ')' || p == '"' || p == '.' || p == '}' || p == ']' || p == '£') sb.Append(' ');
                }
                sb.Append(t).Append(' ');
            }
            else sb.Append(t);
        }

        static void AppendDisplayChar(StringBuilder sb, LineText lt, int c, bool tokensAsWords)
        {
            string s = CodeToDisplay(c);
            if (c >= 0xC0 && tokensAsWords && IsWordToken(c)) s = " " + s + " ";
            if ((c & 0xC0) == 0x80 && c > 0x8A) lt.Inverse.Add(new KeyValuePair<int, int>(sb.Length, s.Length));
            sb.Append(s);
        }

        /// <summary>A whole program as text, one "number text" line per BASIC line.</summary>
        public static string ProgramText(PFile p, DetokOptions o, out int escapedLines)
        {
            var sb = new StringBuilder();
            escapedLines = 0;
            foreach (var l in p.Lines)
            {
                var lt = Detokenize(l.Body, o);
                if (lt.FullyEscaped) escapedLines++;
                sb.Append(l.Number).Append(' ').Append(lt.Text).Append("\r\n");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  TOKENISER
        // ------------------------------------------------------------------
        /// <summary>One item produced while tokenising a line.</summary>
        class Item
        {
            public List<byte> Bytes = new List<byte>();
            public bool IsKeyword;   // a keyword token (spaces next to it are dropped)
            public bool IsSpace;     // a run of spaces outside strings
            public int SpaceCount;
        }

        /// <summary>Tokenise the text of one line (without its line number). Returns the
        /// bytes including the final 76h. Throws CompileError for bad text.
        /// warnings may be null.</summary>
        public static byte[] TokenizeBody(string s, List<string> warnings)
        {
            var items = new List<Item>();
            int pos = 0;
            bool afterRem = false;
            while (pos < s.Length)
            {
                char c = s[pos];
                if (afterRem)
                {
                    // the rest of a REM line: characters as they are
                    var it = new Item();
                    while (pos < s.Length) pos = ReadChar(s, pos, it.Bytes, warnings, false);
                    items.Add(it);
                    break;
                }
                if (c == ' ' || c == '\t')
                {
                    var it = new Item { IsSpace = true };
                    while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t')) { it.SpaceCount++; pos++; }
                    items.Add(it);
                    continue;
                }
                if (c == '"')
                {
                    var it = new Item();
                    it.Bytes.Add(0x0B);
                    pos++;
                    while (true)
                    {
                        if (pos >= s.Length) throw new CompileError(0, "missing closing quote");
                        if (s[pos] == '"')
                        {
                            if (pos + 1 < s.Length && s[pos + 1] == '"') { it.Bytes.Add(0xC0); pos += 2; continue; }
                            pos++; break;
                        }
                        pos = ReadChar(s, pos, it.Bytes, warnings, true);
                    }
                    it.Bytes.Add(0x0B);
                    items.Add(it);
                    continue;
                }
                if (c == '\\' || c == '%')
                {
                    var it = new Item();
                    pos = ReadChar(s, pos, it.Bytes, warnings, false);
                    items.Add(it);
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && pos + 1 < s.Length && char.IsDigit(s[pos + 1])))
                {
                    // a number: its characters, then the hidden 5-byte value
                    int st = pos;
                    while (pos < s.Length && char.IsDigit(s[pos])) pos++;
                    if (pos < s.Length && s[pos] == '.') { pos++; while (pos < s.Length && char.IsDigit(s[pos])) pos++; }
                    if (pos < s.Length && (s[pos] == 'E' || s[pos] == 'e'))
                    {
                        int save = pos; pos++;
                        if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                        if (pos < s.Length && char.IsDigit(s[pos])) { while (pos < s.Length && char.IsDigit(s[pos])) pos++; }
                        else pos = save;
                    }
                    string t = s.Substring(st, pos - st);
                    var it = new Item();
                    foreach (char ch in t.ToUpperInvariant()) it.Bytes.Add((byte)Charset.FromChar(ch, false));
                    bool explicitFloat = pos + 2 < s.Length && s[pos] == '\\' && (s.Substring(pos + 1, 2).ToUpperInvariant() == "7E");
                    if (!explicitFloat)
                    {
                        it.Bytes.Add(0x7E);
                        it.Bytes.AddRange(ZxFloat.Encode(double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture)));
                    }
                    items.Add(it);
                    continue;
                }
                if (char.IsLetter(c))
                {
                    var it = ReadWord(s, ref pos);
                    items.Add(it);
                    if (it.IsKeyword && it.Bytes[0] == 0xEA)
                    {
                        afterRem = true;
                        if (pos < s.Length && s[pos] == ' ') pos++;   // one separating space after REM
                    }
                    continue;
                }
                string two = pos + 1 < s.Length ? s.Substring(pos, 2) : "";
                if (two == "**" || two == "<=" || two == ">=" || two == "<>")
                {
                    var it = new Item { IsKeyword = true };
                    it.Bytes.Add(KeywordCodes[two]);
                    items.Add(it);
                    pos += 2;
                    continue;
                }
                int code = Charset.FromChar(c, false);
                if (code < 0) throw new CompileError(0, "character '" + c + "' has no ZX81 equivalent");
                var one = new Item();
                one.Bytes.Add((byte)code);
                items.Add(one);
                pos++;
            }

            // spaces next to keywords (or at the start/end) are only for readability: drop them
            var result = new List<byte>();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.IsSpace)
                {
                    bool prevKw = i == 0 || items[i - 1].IsKeyword;
                    bool nextKw = i == items.Count - 1 || items[i + 1].IsKeyword;
                    if (prevKw || nextKw) continue;
                    for (int k = 0; k < it.SpaceCount; k++) result.Add(0x00);
                    continue;
                }
                result.AddRange(it.Bytes);
            }
            result.Add(0x76);
            return result.ToArray();
        }

        /// <summary>Read one character (or escape) at s[pos] into bytes; returns the new position.</summary>
        static int ReadChar(string s, int pos, List<byte> bytes, List<string> warnings, bool inString)
        {
            char c = s[pos];
            if (c == '\\')
            {
                // \hh - one byte given in hex
                if (pos + 2 < s.Length && Uri.IsHexDigit(s[pos + 1]) && Uri.IsHexDigit(s[pos + 2]))
                {
                    bytes.Add(byte.Parse(s.Substring(pos + 1, 2), NumberStyles.HexNumber));
                    return pos + 3;
                }
                // block graphic: two of ' . : space
                if (pos + 2 < s.Length)
                {
                    int g = Charset.BlockFromEscape(s[pos + 1], s[pos + 2]);
                    if (g >= 0) { bytes.Add((byte)g); return pos + 3; }
                }
                if (pos + 1 < s.Length && s[pos + 1] == '\\') { bytes.Add(0x18); return pos + 2; }
                throw new CompileError(0, "unknown escape '\\" + (pos + 1 < s.Length ? s[pos + 1].ToString() : "") + "'");
            }
            if (c == '%' && pos + 1 < s.Length)
            {
                int code = Charset.FromChar(s[pos + 1], false);
                if (code >= 0) { bytes.Add((byte)(code | 0x80)); return pos + 2; }
            }
            int z = Charset.FromChar(c, false);
            if (z < 0)
            {
                if (warnings != null) warnings.Add("character '" + c + "' has no ZX81 equivalent, using '?'");
                z = 0x0F;
            }
            bytes.Add((byte)z);
            return pos + 1;
        }

        /// <summary>A word: keyword (incl. GO TO / GO SUB and keyword$), keyword stuck to digits,
        /// or a variable name (letters/digits, with $ for string variables).</summary>
        static Item ReadWord(string s, ref int pos)
        {
            int st = pos;
            while (pos < s.Length && char.IsLetterOrDigit(s[pos]) && s[pos] < 128) pos++;
            string w = s.Substring(st, pos - st).ToUpperInvariant();
            bool dollar = pos < s.Length && s[pos] == '$';
            var it = new Item();
            byte code;
            if (dollar && KeywordCodes.TryGetValue(w + "$", out code)) { pos++; it.IsKeyword = true; it.Bytes.Add(code); return it; }
            if (w == "GO")
            {
                int p = pos; while (p < s.Length && s[p] == ' ') p++;
                int q = p; while (q < s.Length && char.IsLetter(s[q])) q++;
                string nxt = s.Substring(p, q - p).ToUpperInvariant();
                if (nxt == "TO" || nxt == "SUB") { pos = q; it.IsKeyword = true; it.Bytes.Add(KeywordCodes["GO" + nxt]); return it; }
            }
            if (KeywordCodes.TryGetValue(w, out code)) { it.IsKeyword = true; it.Bytes.Add(code); return it; }
            int d = w.Length; while (d > 0 && char.IsDigit(w[d - 1])) d--;
            if (d < w.Length && d > 0 && KeywordCodes.TryGetValue(w.Substring(0, d), out code))
            {
                pos = st + d;    // the digits are read again as a number
                it.IsKeyword = true; it.Bytes.Add(code); return it;
            }
            foreach (char ch in w) it.Bytes.Add((byte)Charset.FromChar(ch, false));
            return it;
        }

        /// <summary>Tokenise a whole listing. Lines keep their written order. Lines starting
        /// with # or ; and blank lines are skipped. Errors are added to diags.</summary>
        public static List<KeyValuePair<int, byte[]>> TokenizeProgram(string text, List<Diagnostic> diags)
        {
            var result = new List<KeyValuePair<int, byte[]>>();
            string[] src = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < src.Length; i++)
            {
                string l = src[i].TrimStart();
                if (l.TrimEnd().Length == 0 || l.StartsWith("#") || l.StartsWith(";") || l.StartsWith("//")) continue;
                int p = 0;
                while (p < l.Length && char.IsDigit(l[p])) p++;
                if (p == 0) { diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = i + 1, Message = "line does not start with a line number" }); continue; }
                int num;
                if (!int.TryParse(l.Substring(0, Math.Min(p, 6)), out num) || num > 16383)
                {
                    diags.Add(new Diagnostic { IsError = true, BasicLine = -1, SourceLine = i + 1, Message = "line number out of range (0-16383)" });
                    continue;
                }
                string body = l.Substring(p);
                if (body.StartsWith(" ")) body = body.Substring(1);   // the separating space
                body = body.TrimEnd('\r', '\n');
                var warn = new List<string>();
                try
                {
                    result.Add(new KeyValuePair<int, byte[]>(num, TokenizeBody(body, warn)));
                    foreach (var w in warn) diags.Add(new Diagnostic { BasicLine = num, SourceLine = i + 1, Message = w });
                }
                catch (CompileError e)
                {
                    diags.Add(new Diagnostic { IsError = true, BasicLine = num, SourceLine = i + 1, Message = e.Message });
                }
            }
            return result;
        }

        // ------------------------------------------------------------------
        //  BUILDER
        // ------------------------------------------------------------------
        /// <summary>Make a .p file from tokenised lines.
        ///   template:    an original file whose system variables, display file and
        ///                variables are reused (may be null)
        ///   keepDisplay: reuse the template's display file (else a blank expanded one)
        ///   keepVars:    reuse the template's variables (else none)
        ///   autorunLine: line number to start at after LOAD, or -1 for no auto-run</summary>
        public static byte[] BuildP(List<KeyValuePair<int, byte[]>> lines, PFile template, bool keepDisplay, bool keepVars, int autorunLine)
        {
            var prog = new List<byte>();
            int autorunAddr = -1;
            foreach (var kv in lines)
            {
                int addr = PFile.ProgStart + prog.Count;
                if (autorunAddr < 0 && autorunLine >= 0 && kv.Key >= autorunLine) autorunAddr = addr;
                prog.Add((byte)(kv.Key >> 8)); prog.Add((byte)kv.Key);
                prog.Add((byte)kv.Value.Length); prog.Add((byte)(kv.Value.Length >> 8));
                prog.AddRange(kv.Value);
            }
            int dfile = PFile.ProgStart + prog.Count;
            byte[] disp = keepDisplay && template != null ? template.DisplayBytes() : null;
            if (disp == null)
            {
                disp = new byte[793];
                disp[0] = 0x76;
                for (int r = 0; r < 24; r++) disp[33 + 33 * r] = 0x76;
            }
            prog.AddRange(disp);
            int vars = PFile.ProgStart + prog.Count;
            byte[] vb = keepVars && template != null ? template.VariablesBytes() : new byte[] { 0x80 };
            if (vb.Length == 0 || vb[vb.Length - 1] != 0x80) { var t = new List<byte>(vb); t.Add(0x80); vb = t.ToArray(); }
            prog.AddRange(vb);
            int eLine = PFile.ProgStart + prog.Count;

            // system variables: the template's, or ZX81 defaults
            var sv = new byte[PFile.ProgStart - PFile.Base];
            if (template != null && template.Data.Length >= sv.Length) Array.Copy(template.Data, 0, sv, 0, sv.Length);
            else
            {
                Action<int, int> b8 = (a, v) => sv[a - PFile.Base] = (byte)v;
                b8(0x4022, 2); b8(0x4023, 2); b8(0x4025, 0xFF); b8(0x4026, 0xFF); b8(0x4027, 0xFF); b8(0x4028, 55);
                b8(0x401F, 0x5D); b8(0x4020, 0x40); b8(0x4030, 0x8D); b8(0x4031, 0x0C); b8(0x4034, 0xFF); b8(0x4035, 0xFF);
                b8(0x4038, 0xBC); b8(0x4039, 33); b8(0x403A, 24); b8(0x403B, 0x40); b8(0x405C, 0x76);
            }
            Action<int, int> w16 = (a, v) => { sv[a - PFile.Base] = (byte)v; sv[a - PFile.Base + 1] = (byte)(v >> 8); };
            Func<int, int> r16 = a => sv[a - PFile.Base] | sv[a - PFile.Base + 1] << 8;
            sv[0] = 0;
            if (template != null)
            {
                // Pointers that pointed at or after the old display file move by the same
                // amount as the display file did (zero if the program wasn't changed).
                int delta = dfile - template.DFile;
                foreach (int a in new[] { 0x400E, 0x4012, 0x4016, 0x4018, 0x401A, 0x401C })
                {
                    int v = r16(a);
                    if (v != 0 && v >= template.DFile) w16(a, (v + delta) & 0xFFFF);
                }
                int dfcc = r16(0x400E);
                if (dfcc < dfile || dfcc >= vars) w16(0x400E, dfile + 1);
            }
            else
            {
                w16(0x400E, dfile + 1);
                w16(0x4016, eLine - 1);
                w16(0x401A, eLine + 1);
                w16(0x401C, eLine + 1);
            }
            w16(0x400C, dfile);
            w16(0x4010, vars);
            w16(0x4014, eLine);
            // NXTLIN: the auto-run line, else (for an unchanged "no auto-run" file) the old value moved along
            if (autorunAddr >= 0) w16(0x4029, autorunAddr);
            else if (template != null && template.AutoRunLine == null && template.NxtLin >= template.DFile) w16(0x4029, (template.NxtLin + dfile - template.DFile) & 0xFFFF);
            else w16(0x4029, dfile);

            // bytes some tools leave after E_LINE are kept too
            if (keepVars && template != null) prog.AddRange(template.TrailingBytes());

            var file = new List<byte>(sv);
            file.AddRange(prog);
            return file.ToArray();
        }

        /// <summary>The file with the text after REM in the given line replaced by bin; null
        /// (and why) if it can't be done. why also gets any warning.</summary>
        public static byte[] InjectRem(PFile p, int lineNumber, byte[] bin, out string why)
        {
            why = "";
            var line = p.Lines.FirstOrDefault(l => l.Number == lineNumber);
            if (line == null) { why = "There is no line " + lineNumber + "."; return null; }
            if (line.Body.Length == 0 || line.Body[0] != 0xEA) { why = "Line " + lineNumber + " doesn't start with REM."; return null; }
            if (bin.Length == 0) { why = "The file is empty."; return null; }
            if (bin.Length + 2 > 0xFFFF) { why = "The file is too big for one BASIC line (" + bin.Length + " bytes)."; return null; }
            var auto = p.AutoRunLine;
            var lines = new List<KeyValuePair<int, byte[]>>();
            foreach (var l in p.Lines)
            {
                byte[] body = l.Body;
                if (l == line)
                {
                    body = new byte[bin.Length + 2];
                    body[0] = 0xEA;
                    Array.Copy(bin, 0, body, 1, bin.Length);
                    body[body.Length - 1] = 0x76;
                }
                lines.Add(new KeyValuePair<int, byte[]>(l.Number, body));
            }
            byte[] data = Basic81.BuildP(lines, p, true, true, auto != null ? auto.Number : -1);
            int moved = bin.Length + 2 - line.Length;
            if (moved != 0 && p.Lines.Last() != line)
                why = " The lines after it moved by " + moved + " bytes, so machine code elsewhere that uses fixed addresses after line " + lineNumber + " may need changing.";
            if (Array.IndexOf(bin, (byte)0x76) >= 0)
                why += " (The binary contains 76h, which ends the line when it is LISTed on the ZX81; it still runs.)";
            return data;
        }
    }
}
