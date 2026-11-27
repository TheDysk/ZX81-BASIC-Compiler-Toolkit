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
//  Opens .p .81 .p81 ZX80 .o/.80 files and tapes (.wav recordings, .tzx images).
//  Export menu: listing text, hex dump, disassembly, screen picture/text, .wav, .tzx,
//  .p81, ZX80 .o; Play Audio to ZX81 (plays the tape sound to a real ZX81).
//  Run in Emulator (Ctrl+F5) starts the emulator set up in Options > Configure Emulator.
//  Listing tab: double-click a {=value} to edit a hidden number; right-click a REM line
//  to inject a binary file into it. (BrowserExtras.cs holds these.)
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

using System.Reflection;

// Version and description stored in the .exe (Windows: Properties > Details). Keep the
// version in step with Versions.cs.
[assembly: AssemblyTitle(".P File Browser")]
[assembly: AssemblyDescription("Inspects, edits and exports ZX81 .p files")]
[assembly: AssemblyProduct("ZX81 BASIC Toolset")]
[assembly: AssemblyCopyright("Copyright (c) Simon Round 2026")]
[assembly: AssemblyVersion("1.00.0.0")]
[assembly: AssemblyFileVersion("1.00.0.0")]

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
        bool converted;          // opened from a tape, .p81 or ZX80 file (not saved as .p yet)
        string tapeName;         // the program's name on the tape it was opened from (else null)
        string loadNotes = "";   // what was done when it was opened (tape position, conversion)

        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        public BrowserForm()
        {
            Text = ".P File Browser " + Versions.Show(Versions.Browser);
            Width = 1180; Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&Open program or tape (.p .p81 .o .wav .tzx)...", null, (s, e) => Open(), Keys.Control | Keys.O));
            file.DropDownItems.Add(new ToolStripMenuItem("&Save as .p...", null, (s, e) => SaveP(), Keys.Control | Keys.S));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("&Run in Emulator", null, (s, e) => RunInEmulator(), Keys.Control | Keys.F5));
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
            export.DropDownItems.Add(new ToolStripMenuItem("Program with its tape name (.p81)...", null, (s, e) => ExportP81()));
            export.DropDownItems.Add(new ToolStripMenuItem("ZX80 program (.o)...", null, (s, e) => ExportZx80()));
            export.DropDownItems.Add(new ToolStripSeparator());
            export.DropDownItems.Add(new ToolStripMenuItem("&Play Audio to ZX81...", null, (s, e) => PlayAudio()));
            var options = new ToolStripMenuItem("&Options");
            options.DropDownItems.Add(new ToolStripMenuItem("Configure &Emulator...", null, (s, e) => EmulatorUi.Configure(this)));
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (s, e) => MessageBox.Show(this,
                ".P File Browser " + Versions.Show(Versions.Browser) + "\n\nInspect ZX81 .p files: system variables, memory map, BASIC listing, screen, variables, hex and Z80 disassembly, graphics; open tapes (.wav, .tzx), .p81 and ZX80 .o files; export to text, .wav, .tzx, .p81 and .o; play the tape sound to a real ZX81; run in an emulator." + UiCommon.AboutText, "About")));
            menu.Items.Add(file); menu.Items.Add(export); menu.Items.Add(options); menu.Items.Add(help);

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(new ToolStripButton("Open...", null, (s, e) => Open()) { Font = new Font(Font, FontStyle.Bold), ToolTipText = "Open a .p, .p81, ZX80 .o, .wav or .tzx file (Ctrl+O)" });
            tools.Items.Add(new ToolStripButton("Run in Emulator  (Ctrl+F5)", null, (s, e) => RunInEmulator()));
            tools.Items.Add(new ToolStripButton("Play Audio to ZX81", null, (s, e) => PlayAudio()) { ToolTipText = "Play the program as tape sound into a real ZX81's EAR socket" });
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
            status.Text = "Open a ZX81 program or tape: .p, .p81, ZX80 .o, .wav or .tzx (or drag one onto this window).";
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
            using (var d = new OpenFileDialog { Filter = TapeFormats.OpenFilter, Title = "Open a ZX81 program or tape" })
                if (d.ShowDialog(this) == DialogResult.OK) OpenFile(d.FileName);
        }

        public void OpenFile(string file)
        {
            if (edited && MessageBox.Show(this, "You have edited bytes that aren't saved. Open another file anyway?", ".P File Browser", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var lp = UiCommon.OpenProgram(this, file);    // shows the error ([mm:ss.ms] for audio) itself
            if (lp == null) return;
            try
            {
                LoadInto(PFile.Load(lp.P));
                path = file;
                edited = false;
                converted = lp.Converted;
                tapeName = lp.Format == "WAV audio" || lp.Format.StartsWith("TZX") || lp.Format.StartsWith(".p81") ? lp.Name : null;
                loadNotes = lp.Notes.Count > 0 ? " (" + lp.Format + ": " + string.Join("; ", lp.Notes) + ")" : "";
                Text = ".P File Browser " + Versions.Show(Versions.Browser) + " - " + Path.GetFileName(file) + (converted ? " (" + lp.Format + ")" : "");
                status.Text = Path.GetFileName(file) + ": " + p.Data.Length + " bytes, " + p.Lines.Count + " BASIC lines. " + p.AutoRunDescription() + "." + loadNotes;
                if (lp.Notes.Count > 1 || lp.Format.StartsWith("ZX80"))
                    MessageBox.Show(this, "Opened " + Path.GetFileName(file) + " (" + lp.Format + "):\n\n  " + string.Join("\n  ", lp.Notes), ".P File Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        /// <summary>The name to give the program on tape: the one it had on the tape it came
        /// from, else the file name.</summary>
        string TapeName { get { return string.IsNullOrEmpty(tapeName) ? Stem.ToUpperInvariant() : tapeName; } }

        // ---------------- exports ----------------
        void SaveP()
        {
            if (!Ready()) return;
            string f = UiCommon.SaveBytes(this, p.Data, "Save .p file", "ZX81 program (*.p)|*.p|All files (*.*)|*.*", Stem + (edited ? "_edited.p" : ".p"));
            if (f != null) { edited = false; converted = false; path = f; status.Text = "Saved " + f; }
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
            string name = TapeName;
            byte[] data = tzx ? Tape.MakeTzx(p.Data, name) : Tape.MakeWav(p.Data, name);
            string f = UiCommon.SaveBytes(this, data, tzx ? "Export tape image" : "Export cassette audio",
                tzx ? "TZX tape image (*.tzx)|*.tzx" : "WAV audio (*.wav)|*.wav", Stem + (tzx ? ".tzx" : ".wav"));
            if (f == null) return;
            status.Text = "Saved " + f + (tzx ? "" : " - play it into the ZX81's EAR socket after typing LOAD \"\" (program name on tape: " + name + ")");
        }
    }
}
