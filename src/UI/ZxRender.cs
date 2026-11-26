// =====================================================================================
//  ZxRender.cs - drawing the ZX81 screen and 8x8 graphics as pictures
// -------------------------------------------------------------------------------------
//  The ZX81's character shapes live in its ROM, which isn't part of this toolset, so
//  letters, digits and symbols are drawn with a Windows font. Block graphics and the
//  grey (chequered) characters are drawn exactly, quarter by quarter.
//  Inverse characters (code + 80h) are drawn white on black.
// =====================================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ZX81Compiler
{
    public static class ZxRender
    {
        /// <summary>Picture of the 32 x 24 character screen. scale = pixels per ZX81 pixel.</summary>
        public static Bitmap Screen(PFile.DisplayInfo d, int scale)
        {
            int cell = 8 * scale;
            var bmp = new Bitmap(32 * cell + 2 * cell, 24 * cell + 2 * cell);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(FontFamily.GenericMonospace, cell * 0.78f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                for (int r = 0; r < 24; r++)
                    for (int c = 0; c < 32; c++)
                        DrawChar(g, font, d.Chars[r, c], cell + c * cell, cell + r * cell, cell);
            }
            return bmp;
        }

        /// <summary>Draw one ZX81 character code in a cell at (x, y).</summary>
        public static void DrawChar(Graphics g, Font font, int code, int x, int y, int cell)
        {
            bool inv = (code & 0x80) != 0;
            int b = code & 0x7F;
            Brush ink = inv ? Brushes.White : Brushes.Black;
            g.FillRectangle(inv ? Brushes.Black : Brushes.White, x, y, cell, cell);
            if (b >= 0x40) { g.FillRectangle(Brushes.Red, x + cell / 4, y + cell / 4, cell / 2, cell / 2); return; }   // not a character
            if (b == 0) return;
            if (b <= 7)
            {
                // block graphics: bit 0 top-left, bit 1 top-right, bit 2 bottom-left
                int h = cell / 2;
                if ((b & 1) != 0) g.FillRectangle(ink, x, y, h, h);
                if ((b & 2) != 0) g.FillRectangle(ink, x + h, y, h, h);
                if ((b & 4) != 0) g.FillRectangle(ink, x, y + h, h, h);
                return;
            }
            if (b <= 0x0A)
            {
                // grey: 08 whole cell, 09 bottom half, 0A top half
                int top = b == 0x09 ? cell / 2 : 0, height = b == 0x08 ? cell : cell / 2;
                int dot = Math.Max(1, cell / 8);
                for (int yy = top; yy < top + height; yy += dot)
                    for (int xx = ((yy / dot) % 2) * dot; xx < cell; xx += 2 * dot)
                        g.FillRectangle(ink, x + xx, y + yy, dot, dot);
                return;
            }
            string s = Charset.Printable[b].ToString();
            var size = g.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic);
            g.DrawString(s, font, ink, x + (cell - size.Width) / 2, y + (cell - font.Height) / 2 + 1, StringFormat.GenericTypographic);
        }

        /// <summary>Picture of memory as 8x8 tiles: each 8 bytes = one tile, one byte per row,
        /// bit 7 = leftmost pixel. Used to find character sets and UDGs.</summary>
        public static Bitmap Tiles(byte[] mem, int addr, int count, int perRow, int zoom, int gap)
        {
            int rows = (count + perRow - 1) / perRow;
            int tw = 8 * zoom + gap;
            var bmp = new Bitmap(Math.Max(1, perRow * tw + gap), Math.Max(1, rows * tw + gap));
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(200, 210, 230));
                for (int t = 0; t < count; t++)
                {
                    int ox = gap + (t % perRow) * tw, oy = gap + (t / perRow) * tw;
                    g.FillRectangle(Brushes.White, ox, oy, 8 * zoom, 8 * zoom);
                    for (int row = 0; row < 8; row++)
                    {
                        int a = addr + t * 8 + row;
                        if (a < 0 || a >= mem.Length) continue;
                        int v = mem[a];
                        for (int bit = 0; bit < 8; bit++)
                            if ((v & (0x80 >> bit)) != 0) g.FillRectangle(Brushes.Black, ox + bit * zoom, oy + row * zoom, zoom, zoom);
                    }
                }
            }
            return bmp;
        }

        /// <summary>Look for blocks of memory that look like a character set: a run of at least
        /// 32 tiles starting with a blank tile (the space), where most tiles have some pixels
        /// and a blank top or bottom row - typical of letter shapes. Returns candidate addresses,
        /// best first.</summary>
        public static System.Collections.Generic.List<int> FindCharSets(byte[] mem, int start, int end)
        {
            var found = new System.Collections.Generic.List<int[]>();   // address, score
            for (int a = start; a + 8 * 32 <= end; a++)
            {
                bool blank = true;
                for (int k = 0; k < 8; k++) if (mem[a + k] != 0) { blank = false; break; }
                if (!blank) continue;
                int score = 0, tiles = 0;
                for (int t = 1; t < 64 && a + 8 * t + 8 <= end; t++)
                {
                    int b = a + 8 * t, rowsUsed = 0, distinct = 0;
                    for (int k = 0; k < 8; k++) if (mem[b + k] != 0) rowsUsed++;
                    for (int k = 1; k < 8; k++) if (mem[b + k] != mem[b + k - 1]) distinct++;
                    bool letterLike = rowsUsed >= 3 && rowsUsed <= 7 && (mem[b] == 0 || mem[b + 7] == 0) && distinct >= 2
                                      && mem[b] != 0x76 && mem[b + 7] != 0x76;
                    if (letterLike) score++;
                    tiles++;
                }
                if (tiles >= 32 && score >= tiles * 6 / 10) found.Add(new[] { a, score });
            }
            found.Sort((x, y) => y[1].CompareTo(x[1]));
            var result = new System.Collections.Generic.List<int>();
            foreach (var f in found)
            {
                bool near = false;
                foreach (var r in result) if (Math.Abs(r - f[0]) < 64 * 8) near = true;
                if (!near) result.Add(f[0]);
                if (result.Count >= 12) break;
            }
            return result;
        }
    }
}
