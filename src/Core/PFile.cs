// =====================================================================================
//  PFile.cs - reading a .p file: system variables, BASIC lines, display file, variables
// -------------------------------------------------------------------------------------
//  A .p file is the ZX81's memory from 4009h up to (but not including) E_LINE, exactly
//  as the ROM saves it to tape. Loading it puts byte 0 of the file at address 4009h.
//  This class keeps a 64K memory image (Mem) with the file placed at 4009h, so every
//  address can be used directly, and decodes the parts of it:
//
//     4009h-407Ch  system variables (Sysvars table below)
//     407Dh        BASIC program: lines of
//                    [line number: 2 bytes, high byte first]
//                    [length of the rest: 2 bytes, low byte first]
//                    [tokens ...] [76h]
//     (D_FILE)     display file: 76h then 24 lines, each up to 32 characters + 76h.
//                  "Expanded" (16K) = all lines full length, 793 bytes.
//                  "Collapsed" (1K)  = lines stop early; an empty line is just 76h.
//     (VARS)       BASIC variables, ended by 80h
//     (E_LINE)     end of the file
//
//  RAMTOP (4004h), PPC (4007h) and the other variables below 4009h are not saved, so
//  a .p file can't say exactly how much RAM it needs; RamEstimate() works it out from
//  the sizes of the parts.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>One line of the BASIC program as stored in memory.</summary>
    public class PLine
    {
        public int Address;     // address of the line number bytes
        public int Number;      // line number
        public int Length;      // value of the length field (bytes after the 4-byte header, including the final 76h)
        public int BodyAddr;    // address of the first token (Address + 4)
        public byte[] Body;     // the tokens, including the final 76h (if inside the file)
        public int End { get { return BodyAddr + Length; } }   // address of the next line
    }

    /// <summary>A system variable: where it is, its size and what it means.</summary>
    public class SysVar
    {
        public int Address, Size;
        public string Name, Meaning;
        public bool IsPointer;   // a 16-bit address
        public SysVar(int a, int s, string n, string m, bool p) { Address = a; Size = s; Name = n; Meaning = m; IsPointer = p; }
    }

    /// <summary>One entry of the BASIC variables area.</summary>
    public class PVar
    {
        public int Address, Size;   // where it is and how many bytes it takes
        public string Name;         // e.g. A, SCORE, A$, A(), A$()
        public string Kind;         // number, string, numeric array, string array, FOR control variable
        public string Value;        // its value as readable text (shortened for big arrays)
    }

    public class PFile
    {
        public const int Base = 0x4009;          // first address in the file
        public const int ProgStart = 0x407D;     // first BASIC line

        /// <summary>64K memory image; the file starts at Base.</summary>
        public readonly byte[] Mem = new byte[0x10000];
        /// <summary>The original file bytes.</summary>
        public byte[] Data;
        /// <summary>First address after the file (should equal E_LINE).</summary>
        public int FileEnd { get { return Base + Data.Length; } }
        /// <summary>The BASIC lines, in memory order.</summary>
        public readonly List<PLine> Lines = new List<PLine>();
        /// <summary>Anything odd found while reading (pointers outside the file etc.).</summary>
        public readonly List<string> Problems = new List<string>();

        // ---- the main system variables ----
        public int Word(int a) { return Mem[a & 0xFFFF] | Mem[(a + 1) & 0xFFFF] << 8; }
        public int Byte(int a) { return Mem[a & 0xFFFF]; }
        public int DFile { get { return Word(0x400C); } }
        public int DfCc { get { return Word(0x400E); } }
        public int VarsAddr { get { return Word(0x4010); } }
        public int ELine { get { return Word(0x4014); } }
        public int NxtLin { get { return Word(0x4029); } }
        public int EPpc { get { return Word(0x400A); } }
        public int CdFlag { get { return Byte(0x403B); } }

        /// <summary>Address where the BASIC program ends (start of the display file, or the
        /// end of the last line found if D_FILE is bad).</summary>
        public int ProgEnd;

        /// <summary>Load a .p file from its bytes.</summary>
        public static PFile Load(byte[] data)
        {
            var p = new PFile();
            p.Data = data;
            int n = Math.Min(data.Length, 0x10000 - Base);
            Array.Copy(data, 0, p.Mem, Base, n);
            if (data.Length < ProgStart - Base) { p.Problems.Add("file is too short to hold the system variables"); p.ProgEnd = p.FileEnd; return p; }
            if (data[0] != 0) p.Problems.Add("VERSN is not 0 - this may not be a ZX81 .p file");
            p.ReadLines();
            p.CheckPointers();
            return p;
        }

        public static PFile Load(string path) { return Load(System.IO.File.ReadAllBytes(path)); }

        void ReadLines()
        {
            int limit = DFile >= ProgStart && DFile <= FileEnd ? DFile : FileEnd;
            int a = ProgStart;
            while (a + 4 <= limit)
            {
                if (Mem[a] >= 0x40) break;           // high byte of a line number is never >= 40h
                var l = new PLine();
                l.Address = a;
                l.Number = Mem[a] << 8 | Mem[a + 1];
                l.Length = Word(a + 2);
                l.BodyAddr = a + 4;
                int avail = Math.Max(0, Math.Min(l.Length, FileEnd - l.BodyAddr));
                l.Body = new byte[avail];
                Array.Copy(Mem, l.BodyAddr, l.Body, 0, avail);
                Lines.Add(l);
                if (l.Length == 0 || l.End > limit)
                {
                    if (l.End > limit) Problems.Add("line " + l.Number + " runs past the end of the program area");
                    a = Math.Min(l.End, limit);
                    if (l.Length == 0) break;
                    break;
                }
                a = l.End;
            }
            ProgEnd = a;
        }

        void CheckPointers()
        {
            if (DFile < ProgStart || DFile >= FileEnd) Problems.Add(string.Format("D_FILE ({0:X4}h) points outside the file", DFile));
            if (VarsAddr < DFile || VarsAddr >= FileEnd) Problems.Add(string.Format("VARS ({0:X4}h) points outside the file", VarsAddr));
            if (ELine != FileEnd) Problems.Add(string.Format("E_LINE is {0:X4}h but the file ends at {1:X4}h", ELine, FileEnd));
        }

        // ------------------------------------------------------------------
        //  System variables
        // ------------------------------------------------------------------
        public static readonly SysVar[] Sysvars = {
            new SysVar(0x4009, 1, "VERSN", "ROM version flag: 0 for a ZX81 .p file", false),
            new SysVar(0x400A, 2, "E_PPC", "line number of the program cursor (current line in the listing)", false),
            new SysVar(0x400C, 2, "D_FILE", "start of the display file (screen memory)", true),
            new SysVar(0x400E, 2, "DF_CC", "print position in the display file", true),
            new SysVar(0x4010, 2, "VARS", "start of the BASIC variables", true),
            new SysVar(0x4012, 2, "DEST", "address of the variable being assigned", true),
            new SysVar(0x4014, 2, "E_LINE", "start of the edit line = end of the saved file", true),
            new SysVar(0x4016, 2, "CH_ADD", "address of the next character to be interpreted", true),
            new SysVar(0x4018, 2, "X_PTR", "address of the character before a syntax error", true),
            new SysVar(0x401A, 2, "STKBOT", "bottom of the calculator stack", true),
            new SysVar(0x401C, 2, "STKEND", "end of the calculator stack", true),
            new SysVar(0x401E, 1, "BERG", "calculator's b register", false),
            new SysVar(0x401F, 2, "MEM", "address of the calculator's memory area (normally MEMBOT)", true),
            new SysVar(0x4021, 1, "(spare)", "not used", false),
            new SysVar(0x4022, 1, "DF_SZ", "number of lines in the lower part of the screen (normally 2)", false),
            new SysVar(0x4023, 2, "S_TOP", "line number of the top line on automatic listings", false),
            new SysVar(0x4025, 2, "LAST_K", "keyboard reading of the last key pressed", false),
            new SysVar(0x4027, 1, "DB_ST", "keyboard debounce status", false),
            new SysVar(0x4028, 1, "MARGIN", "blank lines above/below the picture: 55 = Europe (50Hz), 31 = USA (60Hz)", false),
            new SysVar(0x4029, 2, "NXTLIN", "address of the next program line to run - auto-run after LOAD if it points at a line", true),
            new SysVar(0x402B, 2, "OLDPPC", "line number CONT jumps to", false),
            new SysVar(0x402D, 1, "FLAGX", "various flags", false),
            new SysVar(0x402E, 2, "STRLEN", "length of a string being assigned", false),
            new SysVar(0x4030, 2, "T_ADDR", "address of the next item in the syntax table", true),
            new SysVar(0x4032, 2, "SEED", "seed for RND (set by RAND)", false),
            new SysVar(0x4034, 2, "FRAMES", "frame counter (counts down 50 times a second)", false),
            new SysVar(0x4036, 2, "COORDS", "x and y of the last point PLOTted", false),
            new SysVar(0x4038, 1, "PR_CC", "low byte of the printer buffer position", false),
            new SysVar(0x4039, 2, "S_POSN", "print position: column (33-col) and line (24-line)", false),
            new SysVar(0x403B, 1, "CDFLAG", "bit 7 set = SLOW (compute and display) mode; bit 6 = SLOW requested", false),
            new SysVar(0x403C, 33, "PRBUFF", "printer buffer (32 characters + 76h)", false),
            new SysVar(0x405D, 30, "MEMBOT", "calculator memory area", false),
            new SysVar(0x407B, 2, "(spare)", "not used", false),
        };

        /// <summary>Value of a system variable as text, with a plain-English note.</summary>
        public string SysvarValue(SysVar v)
        {
            if (v.Size == 1) return Byte(v.Address) + "  (" + Byte(v.Address).ToString("X2") + "h)";
            if (v.Size == 2) { int w = Word(v.Address); return w + "  (" + w.ToString("X4") + "h)"; }
            return v.Size + " bytes";
        }

        public string SysvarNote(SysVar v)
        {
            switch (v.Name)
            {
                case "E_PPC": return "line " + EPpc;
                case "D_FILE": return Display.Collapsed ? "collapsed (1K style) display" : "expanded (16K style) display";
                case "NXTLIN": return AutoRunDescription();
                case "CDFLAG": return (CdFlag & 0x40) != 0 ? "SLOW mode" : "FAST mode";
                case "MARGIN": return Byte(0x4028) == 55 ? "50Hz (UK/Europe)" : Byte(0x4028) == 31 ? "60Hz (USA)" : "";
                case "E_LINE": return ELine == FileEnd ? "matches the file length" : "does NOT match the file length";
                case "S_POSN": return "column " + (33 - Byte(0x4039)) + ", line " + (24 - Byte(0x403A));
            }
            if (v.IsPointer) return Region(Word(v.Address));
            return "";
        }

        // ------------------------------------------------------------------
        //  Auto-run
        // ------------------------------------------------------------------
        /// <summary>The line that runs after LOAD, or null if the program doesn't auto-run.
        /// The ROM continues at NXTLIN; it runs only if that points at a real line.</summary>
        public PLine AutoRunLine
        {
            get
            {
                int a = NxtLin;
                foreach (var l in Lines) if (l.Address == a) return l;
                return null;
            }
        }

        public string AutoRunDescription()
        {
            var l = AutoRunLine;
            if (l != null) return "AUTO-RUNS from line " + l.Number + " after loading";
            if (NxtLin >= DFile && NxtLin < FileEnd || Byte(NxtLin) >= 0x40) return "does not auto-run (stops at the BASIC prompt)";
            return "NXTLIN does not point at a line start - probably does not auto-run";
        }

        // ------------------------------------------------------------------
        //  Memory regions
        // ------------------------------------------------------------------
        /// <summary>Which part of memory an address is in, e.g. "BASIC line 10", "display file".</summary>
        public string Region(int a)
        {
            if (a < 0x2000) return "ROM";
            if (a < 0x4000) return "ROM mirror / unused";
            if (a < Base) return "system variables (not saved)";
            if (a < ProgStart) return "system variables";
            if (a < ProgEnd)
            {
                var ln = LineAt(a);
                if (ln != null) return "BASIC line " + ln.Number + (a < ln.BodyAddr ? " (header)" : "");
                return "BASIC program";
            }
            if (a < VarsAddr && a >= DFile) return "display file";
            if (a < ELine && a >= VarsAddr) return "variables";
            if (a == ELine) return "edit line (not saved)";
            if (a > ELine) return "free memory (not in the file)";
            return "?";
        }

        /// <summary>Estimated RAM needed: the file plus the edit line, calculator and machine
        /// stacks (about 100 bytes), rounded to the usual memory sizes.</summary>
        public string RamEstimate(out int bytesNeeded)
        {
            bytesNeeded = FileEnd - 0x4000 + 100;
            if (!Display.Collapsed && Display.Lines >= 24) bytesNeeded = Math.Max(bytesNeeded, FileEnd - 0x4000 + 100);
            int k = bytesNeeded <= 1024 ? 1 : bytesNeeded <= 2048 ? 2 : bytesNeeded <= 4096 ? 4 : 16;
            string s = k + "K";
            if (!Display.Collapsed && k < 4) s = "4K or more (the display file is expanded, which the ROM only does with more than 3.25K)";
            else if (k == 1) s = "1K (fits an unexpanded ZX81)";
            else s = k + "K" + (k == 16 ? " RAM pack" : "");
            return s;
        }

        // ------------------------------------------------------------------
        //  Display file
        // ------------------------------------------------------------------
        public class DisplayInfo
        {
            public byte[,] Chars = new byte[24, 32];   // screen codes (unused positions are 0 = space)
            public bool Collapsed;                     // some lines shorter than 32
            public int Lines;                          // number of lines found (24 normally)
            public int Size;                           // bytes used
            public string Problem;
        }
        DisplayInfo display;
        public DisplayInfo Display { get { if (display == null) display = ReadDisplay(); return display; } }

        DisplayInfo ReadDisplay()
        {
            var d = new DisplayInfo();
            int a = DFile;
            int end = Math.Min(VarsAddr >= DFile ? VarsAddr : FileEnd, FileEnd);
            if (a < Base || a >= FileEnd) { d.Problem = "D_FILE points outside the file"; d.Collapsed = true; return d; }
            if (Mem[a] != 0x76) d.Problem = "display file doesn't start with 76h";
            a++;
            for (int row = 0; row < 24 && a < end; row++)
            {
                int col = 0;
                while (a < end && Mem[a] != 0x76 && col < 33)
                {
                    if (col < 32) d.Chars[row, col] = Mem[a];
                    col++; a++;
                }
                if (col < 32) d.Collapsed = true;
                if (a < end && Mem[a] == 0x76) a++;
                d.Lines = row + 1;
            }
            d.Size = a - DFile;
            if (d.Lines < 24) d.Collapsed = true;
            return d;
        }

        // ------------------------------------------------------------------
        //  Variables area
        // ------------------------------------------------------------------
        /// <summary>Decode the BASIC variables between VARS and the 80h end marker.
        /// First byte of each: bits 7-5 = type, bits 4-0 = letter (code - 20h):
        ///   010 string, 011 number (1 letter), 101 number (longer name),
        ///   100 numeric array, 110 string array, 111 FOR control variable.</summary>
        public List<PVar> ReadVariables()
        {
            var list = new List<PVar>();
            int a = VarsAddr, end = FileEnd;
            while (a < end && a >= Base)
            {
                int b = Mem[a];
                if (b == 0x80) break;
                int type = b >> 5;
                char letter = Charset.Printable[Math.Min(63, (b & 0x1F) + 0x20)];
                var v = new PVar { Address = a };
                try
                {
                    switch (type)
                    {
                        case 2: // string
                            {
                                int len = Word(a + 1);
                                v.Name = letter + "$"; v.Kind = "string"; v.Size = 3 + len;
                                v.Value = "\"" + Basic81.CodesToDisplay(Mem, a + 3, Math.Min(len, 200)) + (len > 200 ? "..." : "") + "\"  (" + len + " chars)";
                                break;
                            }
                        case 3: // number, one letter
                            v.Name = letter.ToString(); v.Kind = "number"; v.Size = 6;
                            v.Value = ZxFloat.Format(ZxFloat.Decode(Mem, a + 1));
                            break;
                        case 5: // number, long name
                            {
                                var sb = new StringBuilder(); sb.Append(letter);
                                int p = a + 1;
                                while (p < end)
                                {
                                    int c = Mem[p++];
                                    sb.Append(Charset.Printable[c & 0x3F]);
                                    if ((c & 0x80) != 0) break;
                                }
                                v.Name = sb.ToString(); v.Kind = "number"; v.Size = p - a + 5;
                                v.Value = ZxFloat.Format(ZxFloat.Decode(Mem, p));
                                break;
                            }
                        case 4: // numeric array
                        case 6: // string array
                            {
                                int len = Word(a + 1);
                                int nd = Mem[a + 3];
                                var dims = new List<int>();
                                for (int i = 0; i < nd; i++) dims.Add(Word(a + 4 + 2 * i));
                                v.Size = 3 + len;
                                v.Name = letter + (type == 6 ? "$" : "") + "(" + string.Join(",", dims) + ")";
                                v.Kind = type == 4 ? "numeric array" : "string array";
                                int data = a + 4 + 2 * nd;
                                if (type == 4)
                                {
                                    var sb = new StringBuilder();
                                    int count = (a + 3 + len - data) / 5;
                                    for (int i = 0; i < Math.Min(count, 20); i++) { if (i > 0) sb.Append(", "); sb.Append(ZxFloat.Format(ZxFloat.Decode(Mem, data + 5 * i))); }
                                    if (count > 20) sb.Append(", ...");
                                    v.Value = sb.ToString();
                                }
                                else v.Value = "\"" + Basic81.CodesToDisplay(Mem, data, Math.Min(a + 3 + len - data, 200)) + "\"";
                                break;
                            }
                        case 7: // FOR control variable
                            v.Name = letter.ToString(); v.Kind = "FOR control variable"; v.Size = 18;
                            v.Value = string.Format("{0}  (TO {1} STEP {2}, loops back to line {3})",
                                ZxFloat.Format(ZxFloat.Decode(Mem, a + 1)), ZxFloat.Format(ZxFloat.Decode(Mem, a + 6)),
                                ZxFloat.Format(ZxFloat.Decode(Mem, a + 11)), Word(a + 16));
                            break;
                        default:
                            v.Name = "?"; v.Kind = "unknown"; v.Size = 1; v.Value = "byte " + b.ToString("X2") + "h - variables area damaged?";
                            list.Add(v);
                            return list;
                    }
                }
                catch (IndexOutOfRangeException) { break; }
                if (v.Size <= 0) break;
                list.Add(v);
                a += v.Size;
            }
            return list;
        }

        /// <summary>Bytes of the variables area including the 80h end marker (for rebuilding .p files).</summary>
        public byte[] VariablesBytes()
        {
            int a = VarsAddr;
            if (a < Base || a >= FileEnd) return new byte[] { 0x80 };
            int e = ELine >= a && ELine <= FileEnd ? ELine : FileEnd;
            var r = new byte[e - a];
            Array.Copy(Mem, a, r, 0, r.Length);
            return r;
        }

        /// <summary>Bytes in the file after E_LINE (some tools save one or two extra).</summary>
        public byte[] TrailingBytes()
        {
            int e = ELine;
            if (e < VarsAddr || e >= FileEnd) return new byte[0];
            var r = new byte[FileEnd - e];
            Array.Copy(Mem, e, r, 0, r.Length);
            return r;
        }

        /// <summary>Bytes of the display file (for rebuilding .p files).</summary>
        public byte[] DisplayBytes()
        {
            int a = DFile, e = VarsAddr;
            if (a < Base || e <= a || e > FileEnd) return null;
            var r = new byte[e - a];
            Array.Copy(Mem, a, r, 0, r.Length);
            return r;
        }

        /// <summary>The line containing an address, or null.</summary>
        public PLine LineAt(int addr)
        {
            int lo = 0, hi = Lines.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var l = Lines[mid];
                if (addr < l.Address) hi = mid - 1;
                else if (addr >= l.End) lo = mid + 1;
                else return l;
            }
            return null;
        }
    }
}
