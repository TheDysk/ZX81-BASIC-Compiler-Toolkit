// =====================================================================================
//  MainFormTools.cs - the compiler window's Tools menu and character palette
//                     (part of MainForm)
// -------------------------------------------------------------------------------------
//    Tools > Renumber Lines...             Labels.Renumber (GOTO/GOSUB/THEN/RUN targets follow)
//    Tools > Convert Labels to Line Numbers  Labels.Number (the @label listing becomes numbered)
//    Tools > Character Palette (F8)        a panel beside the editor with every ZX81
//                                          character; clicking one types its escape
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public partial class MainForm
    {
        Panel palette;              // the character palette (shown or hidden by Tools > Character Palette)
        ToolStripMenuItem miPalette;

        ToolStripMenuItem BuildToolsMenu()
        {
            var tools = new ToolStripMenuItem("&Tools");
            tools.DropDownItems.Add(new ToolStripMenuItem("&Renumber Lines...", null, (s, e) => RenumberLines()));
            tools.DropDownItems.Add(new ToolStripMenuItem("&Convert Labels to Line Numbers", null, (s, e) => ConvertLabels()));
            tools.DropDownItems.Add(new ToolStripSeparator());
            miPalette = new ToolStripMenuItem("Character &Palette", null, (s, e) => TogglePalette(), Keys.F8) { CheckOnClick = false };
            tools.DropDownItems.Add(miPalette);
            return tools;
        }

        /// <summary>Put a changed listing into the editor (keeping it marked as changed).</summary>
        void ReplaceListing(string text, string what)
        {
            editor.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            dirty = true;
            UpdateTitle();
            status.Text = what;
        }

        void ShowToolMessages(List<Diagnostic> diags, string title)
        {
            messages.Items.Clear();
            messages.Items.Add(title);
            foreach (var d in diags.OrderBy(d => d.IsError ? 0 : 1)) messages.Items.Add(d);
        }

        // ---------------- Tools > Renumber Lines ----------------
        void RenumberLines()
        {
            using (var f = new Form { Text = "Renumber Lines", Width = 360, Height = 190, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var l1 = new Label { Text = "First line number:", Left = 14, Top = 18, AutoSize = true };
                var start = new NumericUpDown { Left = 170, Top = 14, Width = 80, Minimum = 1, Maximum = 9999, Value = 10 };
                var l2 = new Label { Text = "Step:", Left = 14, Top = 52, AutoSize = true };
                var step = new NumericUpDown { Left = 170, Top = 48, Width = 80, Minimum = 1, Maximum = 1000, Value = 10 };
                var note = new Label { Left = 14, Top = 80, Width = 320, Height = 34, ForeColor = Color.DimGray, Text = "GOTO, GO SUB, THEN, RUN and LIST followed by a line number are changed to match. Labels are turned into numbers." };
                var ok = new Button { Text = "Renumber", Left = 160, Top = 118, Width = 85, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 252, Top = 118, Width = 85, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l1, start, l2, step, note, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var diags = new List<Diagnostic>();
                string res = Labels.Renumber(editor.Text, (int)start.Value, (int)step.Value, diags);
                ShowToolMessages(diags, res == null ? "Renumber: not done" : "Renumbered from " + start.Value + " in steps of " + step.Value);
                if (res != null) ReplaceListing(res, "Renumbered from " + start.Value + " in steps of " + step.Value + (diags.Count > 0 ? " - see the messages" : "") + ".");
            }
        }

        // ---------------- Tools > Convert Labels to Line Numbers ----------------
        void ConvertLabels()
        {
            var diags = new List<Diagnostic>();
            string res = Labels.Number(editor.Text, false, diags);
            ShowToolMessages(diags, res == null ? "Convert labels: not done - fix these first" : "Labels converted to line numbers");
            if (res == null) { status.Text = "The labels couldn't be converted: see the messages."; return; }
            if (res.TrimEnd() == editor.Text.Replace("\r\n", "\n").Replace("\n", "\r\n").TrimEnd()) { status.Text = "Nothing to convert: every line already has a number and there are no labels."; return; }
            ReplaceListing(res, "Labels converted to line numbers.");
        }

        // ---------------- the character palette ----------------
        const int PalCell = 26, PalCols = 8;

        /// <summary>The palette panel: 64 characters, then their inverse versions.</summary>
        Panel BuildPalette()
        {
            var panel = new Panel { Dock = DockStyle.Right, Width = PalCols * PalCell + 40, Visible = false, BackColor = SystemColors.Control, AutoScroll = true };
            var head = new Label { Dock = DockStyle.Top, Height = 34, Text = "Click a character to type it\n(use it inside \"strings\")", Padding = new Padding(4, 2, 0, 0) };
            var grid = new PictureBox { Width = PalCols * PalCell + 1, Height = 16 * PalCell + 1, Left = 8, Top = 38, Cursor = Cursors.Hand };
            var bmp = new Bitmap(grid.Width, grid.Height);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(FontFamily.GenericMonospace, PalCell * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.Gray);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                for (int i = 0; i < 128; i++)
                {
                    int code = PalCode(i);
                    ZxRender.DrawChar(g, font, code, (i % PalCols) * PalCell + 1, (i / PalCols) * PalCell + 1, PalCell - 1);
                }
            }
            grid.Image = bmp;
            var tip = new ToolTip();
            int lastTip = -1;
            grid.MouseMove += (s, e) =>
            {
                int i = PalIndexAt(e.Location);
                if (i == lastTip) return;
                lastTip = i;
                tip.SetToolTip(grid, i < 0 ? "" : "Code " + PalCode(i).ToString("X2") + "h: types " + EscapeFor(PalCode(i)));
            };
            grid.MouseClick += (s, e) =>
            {
                int i = PalIndexAt(e.Location);
                if (i < 0) return;
                string ins = EscapeFor(PalCode(i));
                editor.SelectedText = ins;
                editor.Focus();
                status.Text = "Typed " + ins + " (ZX81 code " + PalCode(i).ToString("X2") + "h).";
            };
            panel.Controls.Add(grid);
            panel.Controls.Add(head);
            return panel;
        }

        /// <summary>Tile i of the palette: rows 0-7 are codes 00h-3Fh, rows 8-15 the inverse 80h-BFh.</summary>
        static int PalCode(int i) { return i < 64 ? i : 0x80 + (i - 64); }

        static int PalIndexAt(Point p)
        {
            int c = (p.X - 1) / PalCell, r = (p.Y - 1) / PalCell;
            if (p.X < 1 || p.Y < 1 || c >= PalCols || r >= 16) return -1;
            return r * PalCols + c;
        }

        const string Blocks = " ▘▝▀▖▌▞▛";        // codes 00h-07h
        const string InvBlocks = "█▟▙▄▜▐▚▗";      // codes 80h-87h

        /// <summary>What to type for a ZX81 character in a listing string: the character itself,
        /// a Unicode block graphic, %X for inverse, "" for a quote, or \hh.</summary>
        public static string EscapeFor(int code)
        {
            bool inv = (code & 0x80) != 0;
            int b = code & 0x7F;
            if (b <= 7) return (inv ? InvBlocks[b] : Blocks[b]).ToString();
            if (b == 8 && !inv) return "▒";
            if (b <= 0x0A) return "\\" + code.ToString("x2");
            if (b == 0x0B) return inv ? "\\8b" : "\"\"";
            char ch = Charset.Printable[b];
            return inv ? "%" + ch : ch.ToString();
        }

        void TogglePalette()
        {
            palette.Visible = !palette.Visible;
            miPalette.Checked = palette.Visible;
            if (palette.Visible) status.Text = "Character palette: click a character to type its escape at the cursor (F8 hides it).";
        }
    }
}
