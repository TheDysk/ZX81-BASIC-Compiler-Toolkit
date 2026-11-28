// =====================================================================================
//  BrowserImport.cs - .P Toolkit: File > Import BASIC Listing
// -------------------------------------------------------------------------------------
//  Turns a text listing (.txt .bas .b81) into a .p file of ordinary ZX81 BASIC (not
//  compiled), with the same code as the compiler's "BASIC only" output (BasicOnly.cs):
//  labels and missing line numbers filled in, every line checked, LET and THEN GOTO
//  put in where the ZX81 needs them. The program is then shown like any opened file,
//  ready to look at, edit, run in the emulator, or save as .p or a tape (.wav / .tzx).
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public partial class BrowserForm
    {
        // the import choices, kept for the next import while the program is open
        int importRam = 16;
        bool importAutorun = true, importLower = false;

        void ImportListing()
        {
            using (var d = new OpenFileDialog { Filter = TapeFormats.ListingFilter, Title = "Import a BASIC listing (as ordinary BASIC, not compiled)" })
                if (d.ShowDialog(this) == DialogResult.OK) ImportListing(d.FileName);
        }

        /// <summary>Ask for the RAM and auto-run, then build the listing into a .p file of
        /// ordinary BASIC and show it.</summary>
        void ImportListing(string file)
        {
            if (edited && MessageBox.Show(this, "You have edited bytes that aren't saved. Import a listing anyway?", ".P Toolkit", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            string text;
            try { text = TapeFormats.ReadListingText(file); }
            catch (Exception e) { UiCommon.Error(this, "Could not read " + Path.GetFileName(file) + ":\n" + e.Message); return; }
            if (!AskImportOptions(Path.GetFileName(file))) return;

            CompileResult res;
            Cursor = Cursors.WaitCursor;
            try { res = BasicOnly.Build(text, new Options { Ram = importRam, Autorun = importAutorun, LowerIsInverse = importLower, BasicOnly = true }); }
            catch (Exception e)
            {
                res = new CompileResult();
                res.Diagnostics.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = "internal error: " + e.Message });
            }
            finally { Cursor = Cursors.Default; }

            var errors = res.Diagnostics.Where(d => d.IsError).ToList();
            var warnings = res.Diagnostics.Where(d => !d.IsError).ToList();
            if (!res.Success)
            {
                ShowMessages("Import BASIC Listing", Path.GetFileName(file) + " has " + errors.Count + " error" + (errors.Count == 1 ? "" : "s") +
                    ", so no program was made. Correct the listing (the compiler shows the same messages and takes you to each line) and import it again.",
                    errors.Concat(warnings), MessageBoxIcon.Error);
                return;
            }
            try
            {
                LoadInto(PFile.Load(res.PFile));
                path = Path.ChangeExtension(file, ".p");   // Save as .p suggests the listing's name
                edited = false;
                converted = true;                          // not saved as a .p file yet
                tapeName = null;
                loadNotes = " (imported from the listing " + Path.GetFileName(file) + " as ordinary BASIC, not saved yet)";
                Text = ".P Toolkit " + Versions.Show(Versions.Browser) + " - " + Path.GetFileName(file) + " (imported BASIC listing)";
                string last = res.Summary.Split('\n').Last();
                status.Text = Path.GetFileName(file) + ": " + res.ProgramLines + " BASIC lines, " + p.Data.Length + " bytes. " + last + "  Use File > Save as .p, or Export to make a tape.";
                if (warnings.Count > 0)
                    ShowMessages("Import BASIC Listing", "Imported " + Path.GetFileName(file) + " with " + warnings.Count + " warning" + (warnings.Count == 1 ? "" : "s") + ":",
                        warnings, MessageBoxIcon.Information);
            }
            catch (Exception e) { UiCommon.Error(this, "Could not show the imported program:\n" + e.Message); }
        }

        /// <summary>The import choices: RAM, auto-run, lower case = inverse. False if cancelled.</summary>
        bool AskImportOptions(string name)
        {
            using (var f = new Form { Text = "Import BASIC Listing", Width = 480, Height = 290, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var intro = new Label { Left = 12, Top = 10, Width = 445, Height = 48, Text = name + " will be put into a .p file as ordinary ZX81 BASIC, as if typed in on a ZX81 (not compiled). Labels and missing line numbers are filled in, and every line is checked first." };
                var l1 = new Label { Text = "RAM of the ZX81 it is for:", Left = 12, Top = 70, AutoSize = true };
                var ram = new ComboBox { Left = 250, Top = 66, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
                ram.Items.AddRange(new object[] { "1K (no RAM pack)", "2K", "16K or more" });
                ram.SelectedIndex = importRam == 1 ? 0 : importRam == 2 ? 1 : 2;
                var h1 = new Label { Left = 24, Top = 94, Width = 430, Height = 30, ForeColor = Color.DimGray, Text = "Checks that the program fits. With 1K or 2K the screen is saved collapsed, as a ZX81 without a RAM pack keeps it." };
                var auto = new CheckBox { Text = "Run the program by itself after LOAD", Left = 12, Top = 132, AutoSize = true, Checked = importAutorun };
                var lower = new CheckBox { Text = "Lower case letters in strings become inverse video", Left = 12, Top = 158, AutoSize = true, Checked = importLower };
                var ok = new Button { Text = "Import", Left = 280, Top = 210, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 368, Top = 210, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { intro, l1, ram, h1, auto, lower, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK) return false;
                importRam = new[] { 1, 2, 16 }[ram.SelectedIndex];
                importAutorun = auto.Checked;
                importLower = lower.Checked;
                return true;
            }
        }

        /// <summary>A window with a heading and a scrollable list of messages.</summary>
        void ShowMessages(string title, string heading, IEnumerable<Diagnostic> items, MessageBoxIcon icon)
        {
            using (var f = new Form { Text = title, Width = 760, Height = 420, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, ShowIcon = false })
            {
                var head = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0), Text = heading,
                    ForeColor = icon == MessageBoxIcon.Error ? Color.DarkRed : SystemColors.ControlText };
                var box = UiCommon.ListingBox(true);
                box.Dock = DockStyle.Fill;
                box.Text = string.Join("\r\n", items.Select(d => d.ToString()));
                var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 30, DialogResult = DialogResult.OK };
                f.Controls.Add(box); f.Controls.Add(head); f.Controls.Add(ok);
                f.AcceptButton = ok;
                f.ShowDialog(this);
            }
        }
    }
}
