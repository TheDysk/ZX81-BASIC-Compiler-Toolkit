// =====================================================================================
//  DecompilerForm.cs - the Windows decompiler (ZX81Decompiler.exe)
// -------------------------------------------------------------------------------------
//  Open a .p file (button, File menu, drag and drop, or command line) - or a tape (.wav
//  recording, .tzx), a .p81, or a ZX80 .o / .80 program (converted to ZX81 BASIC). Four tabs:
//     Listing   the BASIC exactly as stored (editable)
//     BASIC     the BASIC rebuilt from machine code, for programs made by the
//               ZX81 BASIC compiler (editable; empty otherwise)
//     Machine code   the .asm listing
//     Report    what was found: auto-run, machine code locations, variables ...
//  Save buttons write each one; "Make .p" turns the Listing or BASIC tab (including any
//  edits) back into a .p file, keeping the original screen and variables if ticked.
//  All the work is done by Decompiler.cs.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using System.Reflection;

// Version and description stored in the .exe (Windows: Properties > Details). Keep the
// version in step with Versions.cs.
[assembly: AssemblyTitle("ZX81 Decompiler")]
[assembly: AssemblyDescription("Turns ZX81 .p files back into BASIC listings and Z80 assembly")]
[assembly: AssemblyProduct("ZX81 BASIC Toolset")]
[assembly: AssemblyCopyright("Copyright (c) Simon Round 2026")]
[assembly: AssemblyVersion("1.00.0.0")]
[assembly: AssemblyFileVersion("1.00.0.0")]

namespace ZX81Compiler
{
    public static class DecompilerProgram
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var f = new DecompilerForm();
            if (args.Length > 0 && File.Exists(args[0])) f.Shown += (s, e) => f.OpenFile(args[0]);
            Application.Run(f);
        }
    }

    public class DecompilerForm : Form
    {
        readonly TabControl tabs;
        readonly TextBox listing, basic, asm, report;
        readonly TabPage pageListing, pageBasic, pageAsm, pageReport;
        readonly CheckBox chkDisplay, chkVars;
        readonly ToolStripStatusLabel status;
        DecompileResult result;   // the decompiled file (null until one is opened)
        string path;              // its path

        public DecompilerForm()
        {
            Text = "ZX81 Decompiler " + Versions.Show(Versions.Decompiler);
            Width = 1050; Height = 740;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&Open program or tape (.p .p81 .o .wav .tzx)...", null, (s, e) => Open(), Keys.Control | Keys.O));
            file.DropDownItems.Add(new ToolStripMenuItem("Save &listing (.txt)...", null, (s, e) => SaveListing()));
            file.DropDownItems.Add(new ToolStripMenuItem("Save rebuilt &BASIC (.bas)...", null, (s, e) => SaveBasic()));
            file.DropDownItems.Add(new ToolStripMenuItem("Save &machine code (.asm)...", null, (s, e) => SaveAsm()));
            file.DropDownItems.Add(new ToolStripMenuItem("Make .&p file from this tab...", null, (s, e) => MakeP(), Keys.Control | Keys.P));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (s, e) => Close()));
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&How it works...", null, (s, e) => MessageBox.Show(this, HelpText, "ZX81 Decompiler")));
            help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (s, e) => MessageBox.Show(this,
                "ZX81 Decompiler " + Versions.Show(Versions.Decompiler) + "\n\nTurns a .p file back into a BASIC listing, finds and disassembles machine code, and rebuilds the BASIC of programs made by the ZX81 BASIC Compiler." + UiCommon.AboutText, "About")));
            menu.Items.Add(file); menu.Items.Add(help);

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(new ToolStripButton("Open...", null, (s, e) => Open()) { Font = new Font(Font, FontStyle.Bold), ToolTipText = "Open a .p, .p81, ZX80 .o, .wav or .tzx file (Ctrl+O)" });
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("Save listing...", null, (s, e) => SaveListing()));
            tools.Items.Add(new ToolStripButton("Save BASIC...", null, (s, e) => SaveBasic()));
            tools.Items.Add(new ToolStripButton("Save .asm...", null, (s, e) => SaveAsm()));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("Make .p from this tab...", null, (s, e) => MakeP()));

            var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(6, 5, 6, 0) };
            chkDisplay = new CheckBox { Text = "Keep the original screen in the .p", Checked = true, AutoSize = true };
            chkVars = new CheckBox { Text = "Keep the saved variables in the .p", Checked = true, AutoSize = true };
            opts.Controls.Add(chkDisplay); opts.Controls.Add(chkVars);

            listing = UiCommon.ListingBox(false);
            basic = UiCommon.ListingBox(false);
            asm = UiCommon.ListingBox(true);
            report = UiCommon.ListingBox(true);
            tabs = new TabControl { Dock = DockStyle.Fill };
            pageListing = new TabPage("Listing (as stored)"); pageListing.Controls.Add(listing);
            pageBasic = new TabPage("BASIC rebuilt from machine code"); pageBasic.Controls.Add(basic);
            pageAsm = new TabPage("Machine code (.asm)"); pageAsm.Controls.Add(asm);
            pageReport = new TabPage("Report"); pageReport.Controls.Add(report);
            tabs.TabPages.AddRange(new[] { pageReport, pageListing, pageBasic, pageAsm });

            var strip = new StatusStrip();
            status = new ToolStripStatusLabel("Open a ZX81 program or tape: .p, .p81, ZX80 .o, .wav or .tzx (or drag one onto this window).") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            strip.Items.Add(status);

            Controls.Add(tabs);
            Controls.Add(opts);
            Controls.Add(tools);
            Controls.Add(menu);
            Controls.Add(strip);
            MainMenuStrip = menu;
            UiCommon.AcceptDrops(this, OpenFile);
        }

        void Open()
        {
            using (var d = new OpenFileDialog { Filter = TapeFormats.OpenFilter, Title = "Open a ZX81 program or tape" })
                if (d.ShowDialog(this) == DialogResult.OK) OpenFile(d.FileName);
        }

        /// <summary>Decompile a file and fill the tabs.</summary>
        public void OpenFile(string file)
        {
            Cursor = Cursors.WaitCursor;
            var lp = UiCommon.OpenProgram(this, file);   // shows the error itself ([mm:ss.ms] for a recording)
            Cursor = Cursors.Default;
            if (lp == null) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                var p = PFile.Load(lp.P);
                result = Decompiler.Run(p, Path.GetFileName(file));
                if (lp.Converted) result.Report = "Opened from " + Path.GetFileName(file) + " (" + lp.Format + "):\n  " + string.Join("\n  ", lp.Notes) + "\n\n" + result.Report;
                path = file;
                listing.Text = UiCommon.Crlf(result.Listing);
                basic.Text = UiCommon.Crlf(result.Basic ?? "# This program wasn't made by the ZX81 BASIC Compiler, so there is no BASIC to rebuild from machine code.\n# The Listing tab has the BASIC as stored, with any machine code kept as \\hh escapes.");
                asm.Text = UiCommon.Crlf(result.Asm ?? "; No machine code found.");
                report.Text = UiCommon.Crlf(result.Report);
                Text = "ZX81 Decompiler " + Versions.Show(Versions.Decompiler) + " - " + Path.GetFileName(file);
                tabs.SelectedTab = result.Basic != null ? pageBasic : pageListing;
                status.Text = Path.GetFileName(file) + ": " + p.Lines.Count + " lines; " + p.AutoRunDescription() +
                    (result.Recovered.Success ? (result.Recovered.Verified ? "; BASIC rebuilt and verified" : "; BASIC rebuilt") : "") +
                    (lp.Converted ? " (read from " + lp.Format + " - see the Report tab)" : "");
            }
            catch (Exception e) { UiCommon.Error(this, "Could not decompile the file:\n" + e.Message); }
            finally { Cursor = Cursors.Default; }
        }

        bool Ready()
        {
            if (result != null) return true;
            UiCommon.Error(this, "Open a .p file first.");
            return false;
        }

        string Stem { get { return Path.GetFileNameWithoutExtension(path ?? "program"); } }

        void SaveListing()
        {
            if (!Ready()) return;
            string f = UiCommon.SaveText(this, listing.Text, "Save BASIC listing", "Text listing (*.txt)|*.txt|All files (*.*)|*.*", Stem + ".txt");
            if (f != null) status.Text = "Saved " + f;
        }

        void SaveBasic()
        {
            if (!Ready()) return;
            if (result.Basic == null) { UiCommon.Error(this, "There is no rebuilt BASIC: this program wasn't made by the ZX81 BASIC Compiler."); return; }
            string f = UiCommon.SaveText(this, basic.Text, "Save rebuilt BASIC", "BASIC listing (*.bas)|*.bas|Text (*.txt)|*.txt|All files (*.*)|*.*", Stem + ".bas");
            if (f != null) status.Text = "Saved " + f + " - open it in the ZX81 BASIC Compiler to compile it again.";
        }

        void SaveAsm()
        {
            if (!Ready()) return;
            if (result.Asm == null) { UiCommon.Error(this, "No machine code was found in this program."); return; }
            string f = UiCommon.SaveText(this, asm.Text, "Save machine code listing", "Assembly listing (*.asm)|*.asm|All files (*.*)|*.*", Stem + ".asm");
            if (f != null) status.Text = "Saved " + f;
        }

        /// <summary>Tokenise the Listing or BASIC tab back into a .p file.</summary>
        void MakeP()
        {
            if (!Ready()) return;
            bool fromBasic = tabs.SelectedTab == pageBasic;
            if (fromBasic && result.Basic == null) { UiCommon.Error(this, "There is no rebuilt BASIC for this program - use the Listing tab."); return; }
            var diags = new List<Diagnostic>();
            byte[] data = result.MakeP(fromBasic ? basic.Text : listing.Text, diags, chkDisplay.Checked, chkVars.Checked && !fromBasic);
            if (data == null)
            {
                UiCommon.Error(this, "The text has errors, so no .p file was made:\n\n" + string.Join("\n", diags.Where(d => d.IsError).Take(12).Select(d => d.ToString())));
                return;
            }
            string f = UiCommon.SaveBytes(this, data, "Save .p file", "ZX81 program (*.p)|*.p|All files (*.*)|*.*", Stem + (fromBasic ? "_basic.p" : "_rebuilt.p"));
            if (f == null) return;
            bool same = !fromBasic && data.SequenceEqual(result.File.Data);
            status.Text = "Saved " + f + " (made from the " + (fromBasic ? "rebuilt BASIC" : "listing") + ")" + (same ? " - identical to the original file" : "");
            int warn = diags.Count(d => !d.IsError);
            if (warn > 0) MessageBox.Show(this, string.Join("\n", diags.Take(12).Select(d => d.ToString())), "Warnings");
        }

        const string HelpText =
@"OPENING
.p, .81 and .p81 files, tapes - a .wav recording of a ZX81 tape (8 or 16-bit,
mono or stereo) or a .tzx image - and ZX80 programs (.o, .80), which are converted
to ZX81 BASIC. If a recording can't be read, the message gives the time in it
[minutes:seconds.milliseconds] where it went wrong.

LISTING (AS STORED)
The BASIC exactly as it is stored in the file. Anything that can't be written as
ordinary BASIC text - such as machine code hidden in REM lines - is written as \hh
escapes (one byte in hex), so 'Make .p' turns the text back into the same bytes.
Lines starting with # are notes and are ignored.

BASIC REBUILT FROM MACHINE CODE
Only for programs made by the ZX81 BASIC Compiler. The decompiler reads the
compiled machine code back into BASIC statements. Variable names aren't stored in
machine code, so variables are renamed A, B, C ... If the report says 'Verified',
compiling the rebuilt BASIC gives exactly the same machine code.
The RAM the program was compiled for (16K, 32K or 64K) is detected too, and shown
in the report and at the top of the rebuilt BASIC.
Everything the compiler supports is recognised, including floating point (SIN, PI,
0.5 ...), LPRINT, COPY, SAVE, LOAD and NEW. Lines that compile to nothing (REM,
LIST, LLIST, CONT) can only come back as REM, and only when the program had a
line table (computed GOTO / GOSUB); otherwise lines are renumbered 10, 20, 30 ...
Ordinary machine code (written by hand or by other tools) can't be turned back
into BASIC - it is shown in the Machine code tab instead.

MACHINE CODE (.ASM)
Machine code found by following every USR in the program: in REM lines, split
across several lines, in the variables area or after the display file. Known ROM
routines and system variables are shown by name, and the bytes after RST 28h
(the ROM calculator) and RST 08h (error reports) are explained. The listing can
be assembled again (e.g. with pasmo) to give the same bytes.

MAKE .P
Turns the Listing or BASIC tab (with your edits) into a .p file for an emulator.";
    }
}
