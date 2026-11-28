// =====================================================================================
//  PEdit.cs - edits to a .p file made by the .P Toolkit's editors
// -------------------------------------------------------------------------------------
//  Each method returns new file contents (the old PFile is left alone), or throws
//  PEditException with a message for the user.
//    SetScreenChar   one character of an expanded display file (the bytes are changed
//                    in place, so no system variable moves)
//    WriteBytes      bytes anywhere inside the program, variables or display (e.g. an
//                    edited 8x8 tile); also in place
//    AddRemLine      a new REM line holding the given bytes (e.g. tiles), inserted in
//                    line-number order. The file is rebuilt with Basic81.BuildP, so
//                    D_FILE, DF_CC, VARS, E_LINE, NXTLIN ... are worked out again and the
//                    display file and variables are kept.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZX81Compiler
{
    public class PEditException : Exception { public PEditException(string m) : base(m) { } }

    public static class PEdit
    {
        /// <summary>Address of a screen position in an expanded display file
        /// (D_FILE + 1 + row*33 + column).</summary>
        public static int ScreenAddr(PFile p, int row, int col)
        {
            if (row < 0 || row > 23 || col < 0 || col > 31) throw new PEditException("the screen is 24 rows of 32 columns");
            if (p.Display.Collapsed || p.Display.Problem != null)
                throw new PEditException("only an expanded (16K style) display file can be drawn on: this one is " +
                    (p.Display.Problem ?? "collapsed (1K style), so its lines are shorter than 32 characters") + ".");
            return p.DFile + 1 + row * 33 + col;
        }

        /// <summary>Put character code at (row, col) of an expanded display file. Codes
        /// 76h (NEWLINE) and anything that isn't a printable character are refused.</summary>
        public static byte[] SetScreenChar(PFile p, int row, int col, int code)
        {
            if (!Charset.IsDisplayable(code) || code == 0x76) throw new PEditException("code " + code.ToString("X2") + "h isn't a character that can be shown on the screen");
            int a = ScreenAddr(p, row, col);
            var data = (byte[])p.Data.Clone();
            data[a - PFile.Base] = (byte)code;
            return data;
        }

        /// <summary>Set several screen characters at once: (row, col, code) triples.</summary>
        public static byte[] SetScreenChars(PFile p, IEnumerable<int[]> cells)
        {
            var data = (byte[])p.Data.Clone();
            foreach (var c in cells)
            {
                if (!Charset.IsDisplayable(c[2]) || c[2] == 0x76) throw new PEditException("code " + c[2].ToString("X2") + "h isn't a character that can be shown on the screen");
                data[ScreenAddr(p, c[0], c[1]) - PFile.Base] = (byte)c[2];
            }
            return data;
        }

        /// <summary>Replace bytes at addr (inside the file, after the system variables).
        /// Nothing moves, so the system variables stay right.</summary>
        public static byte[] WriteBytes(PFile p, int addr, byte[] bytes)
        {
            if (addr < PFile.ProgStart || addr + bytes.Length > p.FileEnd)
                throw new PEditException("the bytes " + addr.ToString("X4") + "h-" + (addr + bytes.Length - 1).ToString("X4") + "h aren't all inside the program, display file and variables (" + PFile.ProgStart.ToString("X4") + "h-" + (p.FileEnd - 1).ToString("X4") + "h)");
            var data = (byte[])p.Data.Clone();
            Array.Copy(bytes, 0, data, addr - PFile.Base, bytes.Length);
            return data;
        }

        /// <summary>The first free line number after the last line (in steps of 10), for a new line.</summary>
        public static int NextLineNumber(PFile p)
        {
            int last = p.Lines.Count > 0 ? p.Lines.Max(l => l.Number) : 0;
            return Math.Min(9999, (last / 10 + 1) * 10);
        }

        /// <summary>A new line "n REM" followed by payload, inserted in line-number order. The
        /// rest of the program, the display file and the variables are kept and every system
        /// variable that points past it is moved. payloadAddr gets where the bytes now are.</summary>
        public static byte[] AddRemLine(PFile p, int number, byte[] payload, out int payloadAddr)
        {
            if (number < 1 || number > 9999) throw new PEditException("line numbers go from 1 to 9999");
            if (p.Lines.Any(l => l.Number == number)) throw new PEditException("there is already a line " + number);
            if (payload.Length + 2 > 0xFFFF) throw new PEditException("too many bytes for one line");
            var lines = new List<KeyValuePair<int, byte[]>>();
            bool added = false;
            var body = new byte[payload.Length + 2];
            body[0] = 0xEA;                                   // REM
            Array.Copy(payload, 0, body, 1, payload.Length);
            body[body.Length - 1] = 0x76;
            foreach (var l in p.Lines)
            {
                if (!added && l.Number > number) { lines.Add(new KeyValuePair<int, byte[]>(number, body)); added = true; }
                lines.Add(new KeyValuePair<int, byte[]>(l.Number, l.Body));
            }
            if (!added) lines.Add(new KeyValuePair<int, byte[]>(number, body));
            var auto = p.AutoRunLine;
            byte[] data = Basic81.BuildP(lines, p, true, true, auto != null ? auto.Number : -1);
            var np = PFile.Load(data);
            payloadAddr = np.Lines.First(l => l.Number == number).BodyAddr + 1;
            return data;
        }
    }
}
