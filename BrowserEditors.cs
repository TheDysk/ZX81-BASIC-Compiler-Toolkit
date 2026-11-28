// =====================================================================================
//  BrowserEditors.cs - the .P Toolkit's tile editor (Graphics tab) and screen editor
//                      (Screen tab); part of BrowserForm
// -------------------------------------------------------------------------------------
//  Graphics tab: click a tile to load it into the 8x8 editor beside the tiles. Click or
//  drag over its pixels to set or clear them, then Apply writes the 8 bytes back where
//  they came from; "Add as new REM line..." puts the tiles (with the edit) into a new
//  REM line instead, leaving the original bytes alone.
//  Screen tab: tick Draw, choose a character in the strip (or right-click the screen
//  to pick one up from it), then click or drag on the screen. Only an expanded (16K
//  style) display file can be drawn on, because the characters are changed in place.
//  The edits are made by PEdit (src/Core/PEdit.cs) and shown with ReloadEdited, so
//  every tab is up to date and File > Save as .p keeps them.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public partial class BrowserForm
    {
        // =================================================================
        //  Tile editor
        // =================================================================
        const int PixelSize = 24;
        PictureBox picTileEdit;
        Label lblTile;
        int tileAddr = -1;                     // address of the tile being edited
        readonly byte[] tileBytes = new byte[8];
        bool keepGraphicsView;                 // a reload after an edit keeps the tile address

        void BuildTileEditor(TabPage page)
        {
            var panel = new Panel { Dock = DockStyle.Right, Width = 8 * PixelSize + 40, Padding = new Padding(8) };
            lblTile = new Label { Left = 8, Top = 6, Width = 8 * PixelSize + 24, Height = 48, Text = "Click a tile to edit it here." };
            picTileEdit = new PictureBox { Left = 8, Top = 56, Width = 8 * PixelSize + 1, Height = 8 * PixelSize + 1, Cursor = Cursors.Cross, BorderStyle = BorderStyle.None };
            int paint = -1;                    // 1 = setting pixels, 0 = clearing, -1 = not drawing
            MouseEventHandler at = (s, e) =>
            {
                if (tileAddr < 0 || paint < 0) return;
                int x = e.X / PixelSize, y = e.Y / PixelSize;
                if (x < 0 || x > 7 || y < 0 || y > 7) return;
                int bit = 0x80 >> x;
                byte nv = (byte)(paint == 1 ? tileBytes[y] | bit : tileBytes[y] & ~bit);
                if (nv == tileBytes[y]) return;
                tileBytes[y] = nv;
                DrawTileEditor();
            };
            picTileEdit.MouseDown += (s, e) =>
            {
                if (tileAddr < 0 || e.Button != MouseButtons.Left) return;
                int x = e.X / PixelSize, y = e.Y / PixelSize;
                if (x < 0 || x > 7 || y < 0 || y > 7) return;
                paint = (tileBytes[y] & (0x80 >> x)) != 0 ? 0 : 1;     // the opposite of the pixel clicked
                at(s, e);
            };
            picTileEdit.MouseMove += at;
            picTileEdit.MouseUp += (s, e) => paint = -1;
            panel.Controls.Add(lblTile);
            panel.Controls.Add(picTileEdit);
            int y0 = 64 + 8 * PixelSize;
            Func<string, int, int, Action, Button> btn = (t, x, y, act) =>
            {
                var b = new Button { Text = t, Left = x, Top = y, Width = 4 * PixelSize - 2, Height = 26 };
                b.Click += (s, e) => { if (tileAddr >= 0) act(); };
                panel.Controls.Add(b);
                return b;
            };
            btn("Apply", 8, y0, ApplyTile);
            btn("Revert", 8 + 4 * PixelSize, y0, () => { Array.Copy(p.Mem, tileAddr, tileBytes, 0, 8); DrawTileEditor(); });
            btn("Clear", 8, y0 + 30, () => { Array.Clear(tileBytes, 0, 8); DrawTileEditor(); });
            btn("Invert", 8 + 4 * PixelSize, y0 + 30, () => { for (int i = 0; i < 8; i++) tileBytes[i] ^= 0xFF; DrawTileEditor(); });
            btn("Mirror", 8, y0 + 60, () => { for (int i = 0; i < 8; i++) tileBytes[i] = Mirror(tileBytes[i]); DrawTileEditor(); });
            btn("Flip", 8 + 4 * PixelSize, y0 + 60, () => { Array.Reverse(tileBytes); DrawTileEditor(); });
            var rem = new Button { Text = "Add as new REM line...", Left = 8, Top = y0 + 96, Width = 8 * PixelSize - 2, Height = 26 };
            rem.Click += (s, e) => { if (tileAddr >= 0) AddTilesAsRem(); };
            panel.Controls.Add(rem);
            page.Controls.Add(panel);
            picTiles.MouseClick += (s, e) => PickTile(e.Location);
            picTiles.Cursor = Cursors.Hand;
            DrawTileEditor();
        }

        static byte Mirror(byte b)
        {
            int r = 0;
            for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) r |= 0x80 >> i;
            return (byte)r;
        }

        /// <summary>Which tile of the tiles picture was clicked (same layout as ZxRender.Tiles).</summary>
        void PickTile(Point pt)
        {
            if (p == null) return;
            const int gap = 2;
            int zoom = (int)numZoom.Value, perRow = (int)numPerRow.Value, tw = 8 * zoom + gap;
            int col = (pt.X - gap) / tw, row = (pt.Y - gap) / tw;
            if (pt.X < gap || pt.Y < gap || col >= perRow || (pt.X - gap) % tw >= 8 * zoom || (pt.Y - gap) % tw >= 8 * zoom) return;
            int t = row * perRow + col;
            int a = TileAddr() + t * 8;
            if (a + 8 > p.FileEnd || t >= (int)numCount.Value) return;
            tileAddr = a;
            Array.Copy(p.Mem, a, tileBytes, 0, 8);
            DrawTileEditor();
            status.Text = "Editing the tile at " + a.ToString("X4") + "h (" + p.Region(a) + "). Click its pixels, then Apply.";
        }

        void DrawTileEditor()
        {
            var bmp = new Bitmap(picTileEdit.Width, picTileEdit.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        if (tileAddr >= 0 && (tileBytes[y] & (0x80 >> x)) != 0) g.FillRectangle(Brushes.Black, x * PixelSize, y * PixelSize, PixelSize, PixelSize);
                for (int i = 0; i <= 8; i++)
                {
                    g.DrawLine(Pens.LightGray, i * PixelSize, 0, i * PixelSize, 8 * PixelSize);
                    g.DrawLine(Pens.LightGray, 0, i * PixelSize, 8 * PixelSize, i * PixelSize);
                }
            }
            if (picTileEdit.Image != null) picTileEdit.Image.Dispose();
            picTileEdit.Image = bmp;
            if (tileAddr < 0) { lblTile.Text = "Click a tile to edit it here."; return; }
            bool changed = !Enumerable.Range(0, 8).All(i => p.Mem[tileAddr + i] == tileBytes[i]);
            lblTile.Text = "Tile at " + tileAddr.ToString("X4") + "h" + (changed ? " (changed)" : "") + "\n" +
                string.Join(" ", tileBytes.Select(b => b.ToString("X2")));
        }

        void ApplyTile()
        {
            try
            {
                var data = PEdit.WriteBytes(p, tileAddr, (byte[])tileBytes.Clone());
                int a = tileAddr;
                keepGraphicsView = true;
                ReloadEdited(data, "Tile at " + a.ToString("X4") + "h written back (" + string.Join(" ", tileBytes.Select(b => b.ToString("X2"))) + ").");
                tileAddr = a;
                DrawTileEditor();
            }
            catch (PEditException e) { UiCommon.Error(this, "The tile can't be written there: " + e.Message); }
            finally { keepGraphicsView = false; }
        }

        void AddTilesAsRem()
        {
            int start = TileAddr();
            int shown = Math.Min((int)numCount.Value, Math.Max(1, (p.FileEnd - start) / 8));
            using (var f = new Form { Text = "Add tiles as a new REM line", Width = 430, Height = 220, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var l1 = new Label { Text = "Line number:", Left = 12, Top = 16, AutoSize = true };
                var num = new NumericUpDown { Left = 200, Top = 12, Width = 80, Minimum = 1, Maximum = 9999, Value = PEdit.NextLineNumber(p) };
                var l2 = new Label { Text = "Tiles (from " + start.ToString("X4") + "h):", Left = 12, Top = 50, AutoSize = true };
                var cnt = new NumericUpDown { Left = 200, Top = 46, Width = 80, Minimum = 1, Maximum = shown, Value = shown };
                var only = new CheckBox { Text = "Just the tile being edited", Left = 12, Top = 80, AutoSize = true };
                var note = new Label { Left = 12, Top = 106, Width = 400, Height = 34, ForeColor = Color.DimGray, Text = "The tiles go in as they are shown, with the edit in the editor included. The original bytes are left alone." };
                var ok = new Button { Text = "Add", Left = 234, Top = 142, Width = 85, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 326, Top = 142, Width = 85, DialogResult = DialogResult.Cancel };
                only.CheckedChanged += (s, e) => cnt.Enabled = !only.Checked;
                f.Controls.AddRange(new Control[] { l1, num, l2, cnt, only, note, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                byte[] payload;
                if (only.Checked) payload = (byte[])tileBytes.Clone();
                else
                {
                    payload = new byte[(int)cnt.Value * 8];
                    Array.Copy(p.Mem, start, payload, 0, payload.Length);
                    int off = tileAddr - start;
                    if (off >= 0 && off + 8 <= payload.Length) Array.Copy(tileBytes, 0, payload, off, 8);
                }
                try
                {
                    int at;
                    var data = PEdit.AddRemLine(p, (int)num.Value, payload, out at);
                    ReloadEdited(data, payload.Length + " bytes (" + payload.Length / 8 + " tile" + (payload.Length == 8 ? "" : "s") + ") put in a new line " + num.Value + " REM; they start at " + at.ToString("X4") + "h (" + at + ").");
                }
                catch (PEditException e) { UiCommon.Error(this, "The line can't be added: " + e.Message); }
            }
        }

        // =================================================================
        //  Screen editor
        // =================================================================
        CheckBox chkDraw;
        PictureBox picPalette, picCurrent;
        int drawCode = 0x80;                   // the character drawn (a black square to start with)
        byte[] drawData;                       // the file being drawn on (until the mouse is let go)
        const int StripCell = 16;

        void BuildScreenEditor(FlowLayoutPanel row, TabPage page)
        {
            chkDraw = new CheckBox { Text = "Draw", AutoSize = true, Padding = new Padding(10, 2, 0, 0) };
            chkDraw.CheckedChanged += (s, e) =>
            {
                if (chkDraw.Checked && p != null && (p.Display.Collapsed || p.Display.Problem != null))
                {
                    chkDraw.Checked = false;
                    UiCommon.Error(this, "Only an expanded (16K style) display file can be drawn on: this one is " +
                        (p.Display.Problem ?? "collapsed (1K style), so its lines are shorter than 32 characters and drawing would mean moving the rest of the file") + ".");
                    return;
                }
                picPalette.Visible = chkDraw.Checked;
                picScreen.Cursor = chkDraw.Checked ? Cursors.Cross : Cursors.Default;
                if (chkDraw.Checked) status.Text = "Draw: click or drag on the screen to put the character there; right-click the screen to pick up a character.";
            };
            row.Controls.Add(chkDraw);
            picCurrent = new PictureBox { Width = 22, Height = 22, Margin = new Padding(4, 2, 0, 0) };
            row.Controls.Add(picCurrent);
            // the strip of characters to draw with: 00h-3Fh then 80h-BFh, 32 a row
            picPalette = new PictureBox { Dock = DockStyle.Top, Height = 4 * StripCell + 2, Visible = false, Cursor = Cursors.Hand, BackColor = Color.Gray };
            var bmp = new Bitmap(32 * StripCell + 1, 4 * StripCell + 1);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(FontFamily.GenericMonospace, StripCell * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.Gray);
                for (int i = 0; i < 128; i++)
                    ZxRender.DrawChar(g, font, i < 64 ? i : 0x80 + i - 64, (i % 32) * StripCell + 1, (i / 32) * StripCell + 1, StripCell - 1);
            }
            picPalette.Image = bmp;
            picPalette.SizeMode = PictureBoxSizeMode.Normal;
            picPalette.MouseClick += (s, e) =>
            {
                int c = e.X / StripCell, r = e.Y / StripCell;
                if (c < 0 || c > 31 || r < 0 || r > 3) return;
                int i = r * 32 + c;
                SetDrawCode(i < 64 ? i : 0x80 + i - 64);
            };
            page.Controls.Add(picPalette);
            SetDrawCode(drawCode);

            picScreen.MouseDown += (s, e) =>
            {
                if (!chkDraw.Checked || p == null) return;
                int r, c;
                if (!ScreenCell(e.Location, out r, out c)) return;
                if (e.Button == MouseButtons.Right) { SetDrawCode(p.Display.Chars[r, c]); return; }
                if (e.Button != MouseButtons.Left) return;
                drawData = (byte[])p.Data.Clone();
                DrawAt(r, c);
            };
            picScreen.MouseMove += (s, e) =>
            {
                if (drawData == null) return;
                int r, c;
                if (ScreenCell(e.Location, out r, out c)) DrawAt(r, c);
            };
            picScreen.MouseUp += (s, e) =>
            {
                if (drawData == null) return;
                var d = drawData; drawData = null;
                if (d.SequenceEqual(p.Data)) return;
                int changed = Enumerable.Range(0, d.Length).Count(i => d[i] != p.Data[i]);
                ReloadEdited(d, "Drew " + changed + " character" + (changed == 1 ? "" : "s") + " on the screen.");
            };
        }

        void SetDrawCode(int code)
        {
            drawCode = code;
            var bmp = new Bitmap(22, 22);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(FontFamily.GenericMonospace, 14f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.Gray);
                ZxRender.DrawChar(g, font, code, 1, 1, 20);
            }
            if (picCurrent.Image != null) picCurrent.Image.Dispose();
            picCurrent.Image = bmp;
            if (chkDraw != null && chkDraw.Checked) status.Text = "Drawing with character " + code.ToString("X2") + "h.";
        }

        /// <summary>Screen row and column under a point of the screen picture (ZxRender.Screen
        /// draws a one-character border).</summary>
        bool ScreenCell(Point pt, out int row, out int col)
        {
            int cell = 8 * (cbScale.SelectedIndex + 1);
            col = (pt.X - cell) / cell; row = (pt.Y - cell) / cell;
            return pt.X >= cell && pt.Y >= cell && col < 32 && row < 24;
        }

        void DrawAt(int row, int col)
        {
            try
            {
                var cur = PFile.Load(drawData);
                if (cur.Display.Chars[row, col] == drawCode) return;
                drawData = PEdit.SetScreenChar(cur, row, col, drawCode);
                var shown = PFile.Load(drawData);
                if (picScreen.Image != null) picScreen.Image.Dispose();
                picScreen.Image = ZxRender.Screen(shown.Display, cbScale.SelectedIndex + 1);
            }
            catch (PEditException e) { drawData = null; UiCommon.Error(this, e.Message); }
        }
    }
}
