// =====================================================================================
//  BrowserForm.cs - the .P File Browser (ZX81PBrowser.exe)
// -------------------------------------------------------------------------------------
//  Tabs:
//    Overview      system variables in plain English, auto-run status, RAM needed,
//                  memory map (bar + table)
//    Listing       the BASIC with ZX81 characters (inverse video, block graphics,
//                  keywords); hidden 5-byte numbers on/off; REM machine code as Z80
//    Screen        the saved display file drawn as a picture (collapsed or expanded)
//    Variables     the saved BASIC variables
//    Hex / Z80     synchronised hex dump and disassembly of 4009h to the end, with
//                  ROM routines and system variables named; bytes can be edited
//    Graphics      memory shown as 8x8 tiles to find character sets / UDGs
//  Export menu: listing text, hex dump, disassembly, screen picture/text, .wav, .tzx.
//  The work is done by the shared classes in src/Core; this file is the user interface.
//  (BrowserTabs.cs holds the code that fills each tab.)
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public static class BrowserProgram
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var f = new BrowserForm();
            if (args.Length > 0 && File.Exists(args[0])) f.Shown += (s, e) => f.OpenFile(args[0]);
            Application.Run(f);
        }
    }

    public partial class BrowserForm : Form
    {
        // ---- the file being shown ----
        PFile p;                 // parsed file (rebuilt after byte edits)
        string path;             // where it came from
        CodeAnalysis ca;         // machine code found in it
        RecoverResult rec;       // compiled-by-the-toolset details (if any)
        bool edited;             // bytes changed in the hex editor

        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        public BrowserForm()
        {
            Text = ".P File Browser";
            Width = 1180; Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&Open .p file...", null, (s, e) => Open(), Keys.Control | Keys.O));
            file.DropDownItems.Add(new ToolStripMenuItem("&Save .p as... (after editing bytes)", null, (s, e) => SaveP(), Keys.Control | Keys.S));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (s, e) => Close()));
            var export = new ToolStripMenuItem("&Export");
            export.DropDownItems.Add(new ToolStripMenuItem("BASIC listing as text (UTF-8, \\.. style escapes)...", null, (s, e) => ExportListing()));
            export.DropDownItems.Add(new ToolStripMenuItem("Hex dump (.txt)...", null, (s, e) => ExportHex()));
            export.DropDownItems.Add(new ToolStripMenuItem("Z80 disassembly (.asm)...", null, (s, e) => ExportAsm()));
            export.DropDownItems.Add(new ToolStripSeparator());
            export.DropDownItems.Add(new ToolStripMenuItem("Screen picture (.png)...", null, (s, e) => ExportScreenPng()));
            export.DropDownItems.Add(new ToolStripMenuItem("Screen as text (.txt)...", null, (s, e) => ExportScreenText()));
            export.DropDownItems.Add(new ToolStripSeparator());
            export.DropDownItems.Add(new ToolStripMenuItem("Cassette audio for a real ZX81 (.wav)...", null, (s, e) => ExportTape(false)));
            export.DropDownItems.Add(new ToolStripMenuItem("Tape image (.tzx)...", null, (s, e) => ExportTape(true)));
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (s, e) => MessageBox.Show(this,
                ".P File Browser\n\nInspect ZX81 .p files: system variables, memory map, BASIC listing, screen, variables, hex and Z80 disassembly, graphics; export to text, .wav and .tzx." + UiCommon.AboutText, "About")));
            menu.Items.Add(file); menu.Items.Add(export); menu.Items.Add(help);

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(new ToolStripButton("Open .p file...", null, (s, e) => Open()) { Font = new Font(Font, FontStyle.Bold) });
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("Export .wav...", null, (s, e) => ExportTape(false)));
            tools.Items.Add(new ToolStripButton("Export .tzx...", null, (s, e) => ExportTape(true)));
            tools.Items.Add(new ToolStripButton("Export listing...", null, (s, e) => ExportListing()));

            BuildOverviewTab();
            BuildListingTab();
            BuildScreenTab();
            BuildVariablesTab();
            BuildHexTab();
            BuildGraphicsTab();

            var strip = new StatusStrip();
            status.Text = "Open a ZX81 .p file (or drag one onto this window).";
            strip.Items.Add(status);
            Controls.Add(tabs);
            Controls.Add(tools);
            Controls.Add(menu);
            Controls.Add(strip);
            MainMenuStrip = menu;
            UiCommon.AcceptDrops(this, OpenFile);
            FormClosing += (s, e) =>
            {
                if (edited && MessageBox.Show(this, "You have edited bytes that aren't saved. Close anyway?", ".P File Browser", MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true;
            };
        }

        void Open()
        {
            using (var d = new OpenFileDialog { Filter = "ZX81 programs (*.p;*.81;*.p81)|*.p;*.81;*.p81|All files (*.*)|*.*", Title = "Open a ZX81 program" })
                if (d.ShowDialog(this) == DialogResult.OK) OpenFile(d.FileName);
        }

        public void OpenFile(string file)
        {
            try
            {
                LoadInto(PFile.Load(file));
                path = file;
                edited = false;
                Text = ".P File Browser - " + Path.GetFileName(file);
                status.Text = Path.GetFileName(file) + ": " + p.Data.Length + " bytes, " + p.Lines.Count + " BASIC lines. " + p.AutoRunDescription() + ".";
            }
            catch (Exception e) { UiCommon.Error(this, "Could not open the file:\n" + e.Message); }
        }

        /// <summary>Analyse a file and fill every tab.</summary>
        void LoadInto(PFile file)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                p = file;
                ca = CodeFinder.Analyze(p);
                rec = Recover.Run(p, ca);
                FillOverview();
                FillListing();
                FillScreen();
                FillVariables();
                FillHex();
                FillGraphicsDefaults();
            }
            finally { Cursor = Cursors.Default; }
        }

        bool Ready()
        {
            if (p != null) return true;
            UiCommon.Error(this, "Open a .p file first.");
            return false;
        }

        string Stem { get { return Path.GetFileNameWithoutExtension(path ?? "program"); } }

        // ---------------- exports ----------------
        void SaveP()
        {
            if (!Ready()) return;
            string f = UiCommon.SaveBytes(this, p.Data, "Save .p file", "ZX81 program (*.p)|*.p|All files (*.*)|*.*", Stem + (edited ? "_edited.p" : ".p"));
            if (f != null) { edited = false; status.Text = "Saved " + f; }
        }

        void ExportListing()
        {
            if (!Ready()) return;
            int esc;
            string text = "# " + Stem + " - " + p.AutoRunDescription() + "\r\n" + Basic81.ProgramText(p, new DetokOptions { Style = ListStyle.Exact }, out esc);
            string f = UiCommon.SaveText(this, text, "Export BASIC listing", "Text listing (*.txt)|*.txt|BASIC (*.bas)|*.bas|All files (*.*)|*.*", Stem + ".txt");
            if (f != null) status.Text = "Saved " + f + (esc > 0 ? " (" + esc + " line(s) written as \\hh escapes)" : "");
        }

        void ExportHex()
        {
            if (!Ready()) return;
            var sb = new StringBuilder();
            for (int row = 0; row < HexRows; row++) sb.AppendLine(HexRowText(row, true));
            string f = UiCommon.SaveText(this, sb.ToString(), "Export hex dump", "Text (*.txt)|*.txt|All files (*.*)|*.*", Stem + "_hex.txt");
            if (f != null) status.Text = "Saved " + f;
        }

        void ExportAsm()
        {
            if (!Ready()) return;
            var sb = new StringBuilder();
            if (ca.Blocks.Count > 0)
            {
                sb.Append(CodeFinder.ToAsm(p, ca, Stem));
                sb.AppendLine().AppendLine();
            }
            sb.AppendLine("; ---- whole file from 4009h: machine code as Z80, everything else as data ----");
            foreach (var r in disRows) sb.AppendLine(("; " + r.Addr.ToString("X4") + "  " + r.Bytes.PadRight(12) + " " + r.Text).PadRight(50) + (r.Note.Length > 0 ? " ; " + r.Note : ""));
            string f = UiCommon.SaveText(this, sb.ToString(), "Export disassembly", "Assembly (*.asm)|*.asm|Text (*.txt)|*.txt|All files (*.*)|*.*", Stem + ".asm");
            if (f != null) status.Text = "Saved " + f;
        }

        void ExportScreenPng()
        {
            if (!Ready()) return;
            using (var d = new SaveFileDialog { Filter = "PNG picture (*.png)|*.png", FileName = Stem + "_screen.png" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                using (var bmp = ZxRender.Screen(p.Display, 3)) bmp.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
                status.Text = "Saved " + d.FileName;
            }
        }

        void ExportScreenText()
        {
            if (!Ready()) return;
            var sb = new StringBuilder();
            for (int r = 0; r < 24; r++)
            {
                var line = new StringBuilder();
                for (int c = 0; c < 32; c++) line.Append(Basic81.CodeToDisplay(p.Display.Chars[r, c]));
                sb.AppendLine(line.ToString());
            }
            string f = UiCommon.SaveText(this, sb.ToString(), "Export screen as text", "Text (*.txt)|*.txt", Stem + "_screen.txt");
            if (f != null) status.Text = "Saved " + f;
        }

        void ExportTape(bool tzx)
        {
            if (!Ready()) return;
            string name = Stem.ToUpperInvariant();
            byte[] data = tzx ? Tape.MakeTzx(p.Data, name) : Tape.MakeWav(p.Data, name);
            string f = UiCommon.SaveBytes(this, data, tzx ? "Export tape image" : "Export cassette audio",
                tzx ? "TZX tape image (*.tzx)|*.tzx" : "WAV audio (*.wav)|*.wav", Stem + (tzx ? ".tzx" : ".wav"));
            if (f == null) return;
            status.Text = "Saved " + f + (tzx ? "" : " - play it into the ZX81's EAR socket after typing LOAD \"\" (program name on tape: " + name + ")");
        }
    }
}
