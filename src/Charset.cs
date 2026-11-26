// =====================================================================================
//  Charset.cs - the ZX81 character set
// -------------------------------------------------------------------------------------
//  The ZX81 does not use ASCII. Its character codes are:
//
//    00-3F   printable characters: 00 space, 01-0A block graphics, 0B ", 0C £, 0D $,
//            0E :, 0F ?, 10 (, 11 ), 12 >, 13 <, 14 =, 15 +, 16 -, 17 *, 18 /, 19 ;,
//            1A ',', 1B ., 1C-25 digits 0-9, 26-3F letters A-Z
//    40-42   RND, INKEY$, PI (keyword codes)
//    43-75   not used / control codes. 76 = NEWLINE (also marks the end of each
//            screen line in the display file - never print it as a character!)
//    7E      marks a hidden 5-byte number inside a BASIC line
//    80-BF   inverse video versions of 00-3F (80 = inverse space = solid block)
//    C0-FF   keyword codes ("" AT TAB ... COPY) - see the Tokens table below
//
//  Only codes with bit 6 clear (00-3F and 80-BF) can be stored in the screen memory.
//
//  Block graphics: each character cell is split into 4 quarters. Codes 00-07 have the
//  quarters in bits 0-2 (bit0 = top-left, bit1 = top-right, bit2 = bottom-left; the
//  bottom-right is never set). Patterns that include the bottom-right are the inverse
//  of a pattern without it, so they are stored as (pattern XOR 8F).
//  Example: all four quarters = mask 15 -> 15 XOR 8F = 80 (inverse space). PLOT in
//  Runtime.cs uses the same rule.
// =====================================================================================
using System;
using System.Collections.Generic;

namespace ZX81Compiler
{
    public static class Charset
    {
        /// <summary>ZX81 codes 0x00-0x3F as text (block graphics shown as '?').
        /// Printable[code] gives the character for a code.</summary>
        public const string Printable = " ??????????\"£$:?()><=+-*/;,.0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        /// <summary>Keyword text for each keyword code (index = code), e.g. Tokens[0xF5] = "PRINT".
        /// Entries that are not keywords are null.</summary>
        public static readonly string[] Tokens = new string[256];

        /// <summary>Fills in the Tokens table when the class is first used.</summary>
        static Charset()
        {
            Tokens[0x40] = "RND"; Tokens[0x41] = "INKEY$"; Tokens[0x42] = "PI";
            // Codes C0 to FF, in order
            string[] hi = {
                "\"\"", "AT", "TAB", "?", "CODE", "VAL", "LEN", "SIN", "COS", "TAN", "ASN", "ACS", "ATN", "LN", "EXP", "INT",
                "SQR", "SGN", "ABS", "PEEK", "USR", "STR$", "CHR$", "NOT", "**", "OR", "AND", "<=", ">=", "<>", "THEN", "TO",
                "STEP", "LPRINT", "LLIST", "STOP", "SLOW", "FAST", "NEW", "SCROLL", "CONT", "DIM", "REM", "FOR", "GOTO", "GOSUB", "INPUT", "LOAD",
                "LIST", "LET", "PAUSE", "NEXT", "POKE", "PRINT", "PLOT", "RUN", "SAVE", "RAND", "IF", "CLS", "UNPLOT", "CLEAR", "RETURN", "COPY" };
            for (int i = 0; i < hi.Length; i++) Tokens[0xC0 + i] = hi[i];
        }

        /// <summary>Convert one ASCII/Unicode character to a ZX81 code, or -1.
        /// lowerIsInverse: if true, lower-case letters become inverse video letters;
        /// otherwise they are treated as upper case (the ZX81 has no lower case).</summary>
        public static int FromChar(char c, bool lowerIsInverse)
        {
            if (c >= 'a' && c <= 'z')
            {
                int code = 0x26 + (c - 'a');
                return lowerIsInverse ? code | 0x80 : code;   // bit 7 = inverse video
            }
            if (c >= 'A' && c <= 'Z') return 0x26 + (c - 'A');
            if (c >= '0' && c <= '9') return 0x1C + (c - '0');
            switch (c)
            {
                case ' ': return 0x00;
                case '"': return 0x0B;
                case '£': return 0x0C;
                case '$': return 0x0D;
                case ':': return 0x0E;
                case '?': return 0x0F;
                case '(': return 0x10;
                case ')': return 0x11;
                case '>': return 0x12;
                case '<': return 0x13;
                case '=': return 0x14;
                case '+': return 0x15;
                case '-': return 0x16;
                case '*': return 0x17;
                case '/': return 0x18;
                case ';': return 0x19;
                case ',': return 0x1A;
                case '.': return 0x1B;
                // Unicode block graphics (some listing tools export these)
                case '▘': return 0x01;
                case '▝': return 0x02;
                case '▀': return 0x03;
                case '▖': return 0x04;
                case '▌': return 0x05;
                case '▞': return 0x06;
                case '▛': return 0x07;
                case '▒': return 0x08;
                case '█': return 0x80;
                case '▟': return 0x81;
                case '▙': return 0x82;
                case '▄': return 0x83;
                case '▜': return 0x84;
                case '▐': return 0x85;
                case '▚': return 0x86;
                case '▗': return 0x87;
            }
            return -1;   // no ZX81 equivalent (e.g. | ! & #)
        }

        /// <summary>Block graphic from a 4-bit mask: 1=top-left 2=top-right 4=bottom-left 8=bottom-right.
        /// Masks 0-7 are the code itself; masks with the bottom-right set are stored inverted
        /// (see the explanation at the top of this file).</summary>
        public static int BlockFromMask(int mask)
        {
            mask &= 15;
            return mask < 8 ? mask : (mask ^ 0x8F);
        }

        /// <summary>zmakebas / ZXText2P style graphic escape: two chars from " '.:" (left half, right half).
        /// For each half: ' = top quarter, . = bottom quarter, : = both, space = neither.
        /// Example: \': means left half top only, right half full. Returns -1 if not a valid escape.</summary>
        public static int BlockFromEscape(char left, char right)
        {
            int l = HalfMask(left), r = HalfMask(right);
            if (l < 0 || r < 0) return -1;
            // half mask: bit0 = top, bit1 = bottom
            int mask = 0;
            if ((l & 1) != 0) mask |= 1;   // left top     -> top-left
            if ((r & 1) != 0) mask |= 2;   // right top    -> top-right
            if ((l & 2) != 0) mask |= 4;   // left bottom  -> bottom-left
            if ((r & 2) != 0) mask |= 8;   // right bottom -> bottom-right
            return BlockFromMask(mask);
        }

        /// <summary>One half of a graphic escape: bit0 = top quarter, bit1 = bottom quarter, -1 = not valid.</summary>
        static int HalfMask(char c)
        {
            switch (c)
            {
                case ' ': return 0;
                case '\'': return 1;
                case '.': return 2;
                case ':': return 3;
            }
            return -1;
        }

        /// <summary>Codes that are allowed in the ZX81 display file (screen memory):
        /// 00-3F and 80-BF, i.e. bit 6 clear.</summary>
        public static bool IsDisplayable(int code)
        {
            return (code & 0x40) == 0;
        }

        /// <summary>Expand a code that appears inside a string literal into displayable codes.
        /// Ordinary characters are returned unchanged. The "" quote image (C0) becomes a
        /// quote character. Other keyword codes become their keyword text with spaces
        /// round it (as the ZX81 prints them), e.g. F5 -> " PRINT ". Unknown codes become '?'.</summary>
        public static IEnumerable<int> ExpandForString(int code, bool lowerIsInverse)
        {
            if (IsDisplayable(code)) { yield return code; yield break; }
            if (code == 0xC0) { yield return 0x0B; yield break; } // "" quote image
            string t = Tokens[code];
            if (t == null) { yield return 0x0F; yield break; }
            bool word = char.IsLetter(t[0]);          // keywords made of letters get spaces, ** <= etc. don't
            if (word && code >= 0xC1) yield return 0x00;
            foreach (char ch in t) { int c = FromChar(ch, false); yield return c < 0 ? 0x0F : c; }
            if (word) yield return 0x00;
        }
    }
}
