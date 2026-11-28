// =====================================================================================
//  DecompilerFormCtl.cs - control files (.ctl) in the decompiler window (part of
//                         DecompilerForm)
// -------------------------------------------------------------------------------------
//  program.ctl next to the opened file is loaded automatically. File > Load / Save
//  Control File, and right-clicking a line of the Machine code tab gives Rename Label
//  and Add Comment, which change the control file and redraw the .asm at once.
//  The format is in src/Core/ControlFile.cs.
// =====================================================================================
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public partial class DecompilerForm
    {
        ControlFile ctl;          // the control file in use (null if none)
        string ctlPath;           // where it was loaded from / saved to
        bool ctlDirty;            // changed since it was loaded or saved

        void AddControlFileMenu(ToolStripMenuItem file)
        {
            int at = file.DropDownItems.IndexOfKey("exitSeparator");
            var load = new ToolStripMenuItem("&Load Control File (.ctl)...", null, (s, e) => LoadControlFile());
            var save = new ToolStripMenuItem("Sa&ve Control File (.ctl)...", null, (s, e) => SaveControlFile());
            file.DropDownItems.Insert(at, new ToolStripSeparator());
            file.DropDownItems.Insert(at + 1, load);
            file.DropDownItems.Insert(at + 2, save);
            asm.ContextMenuStrip = AsmMenu();
            FormClosing += (s, e) => { if (!AskSaveControlFile()) e.Cancel = true; };
        }

        /// <summary>The sidecar of a newly opened file (program.ctl), if there is one.</summary>
        void LoadSidecarFor(string file)
        {
            ctl = null; ctlPath = null; ctlDirty = false;
            try
            {
                ctl = ControlFile.LoadSidecar(file);
                if (ctl != null) ctlPath = ControlFile.SidecarPath(file);
            }
            catch (Exception e) { UiCommon.Error(this, "Could not read the control file " + ControlFile.SidecarPath(file) + ":\n" + e.Message); }
        }

        string CtlNote { get { return ctl == null ? "" : "; control file " + Path.GetFileName(ctlPath ?? "(new)") + (ctlDirty ? " (changed)" : ""); } }

        void LoadControlFile()
        {
            if (!Ready() || !AskSaveControlFile()) return;
            using (var d = new OpenFileDialog { Filter = "Decompiler control files (*.ctl)|*.ctl|All files (*.*)|*.*", Title = "Load a control file", FileName = Path.GetFileName(ControlFile.SidecarPath(path)) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { ctl = ControlFile.Load(d.FileName); ctlPath = d.FileName; ctlDirty = false; }
                catch (Exception e) { UiCommon.Error(this, "Could not read the control file:\n" + e.Message); return; }
            }
            Redecompile();
            if (ctl.Problems.Count > 0)
                MessageBox.Show(this, "Some lines of the control file weren't understood:\n\n  " + string.Join("\n  ", ctl.Problems), "Control file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            status.Text = "Control file " + Path.GetFileName(ctlPath) + ": " + ctl.Summary(false);
        }

        /// <summary>Save the control file; false if cancelled.</summary>
        bool SaveControlFile()
        {
            if (!Ready()) return false;
            if (ctl == null) ctl = new ControlFile();
            string suggest = ctlPath ?? ControlFile.SidecarPath(path);
            using (var d = new SaveFileDialog { Filter = "Decompiler control files (*.ctl)|*.ctl|All files (*.*)|*.*", Title = "Save the control file", FileName = Path.GetFileName(suggest), InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(suggest)) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return false;
                try { File.WriteAllText(d.FileName, ctl.Save(Path.GetFileName(path))); }
                catch (Exception e) { UiCommon.Error(this, "Could not save the control file:\n" + e.Message); return false; }
                ctlPath = d.FileName; ctlDirty = false;
                status.Text = "Saved " + d.FileName + (Path.GetFullPath(d.FileName) == Path.GetFullPath(ControlFile.SidecarPath(path)) ? " - it will be used whenever " + Path.GetFileName(path) + " is opened" : "");
                return true;
            }
        }

        /// <summary>If the control file has unsaved changes, offer to save them. false = cancel.</summary>
        bool AskSaveControlFile()
        {
            if (ctl == null || !ctlDirty) return true;
            var r = MessageBox.Show(this, "The control file (labels and comments) has changes that aren't saved. Save it?", "ZX81 Decompiler", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveControlFile();
            ctlDirty = false;
            return true;
        }

        /// <summary>Decompile the open program again (after CODE/DATA may have changed).</summary>
        void Redecompile()
        {
            if (result == null) return;
            int k = result.Report.IndexOf("File: ");
            string before = k > 0 ? result.Report.Substring(0, k) : "";     // e.g. "Opened from game.wav ..."
            result = Decompiler.Run(result.File, result.Name, ctl);
            result.Report = before + result.Report;
            asm.Text = UiCommon.Crlf(result.Asm ?? "; No machine code found.");
            report.Text = UiCommon.Crlf(result.Report);
        }

        /// <summary>Only the .asm changes when a label or comment is added: redraw it in place.</summary>
        void RedrawAsm()
        {
            int pos = asm.SelectionStart, first = asm.GetLineFromCharIndex(asm.GetCharIndexFromPosition(new Point(2, 2)));
            result.Asm = result.Code.Blocks.Count > 0 ? CodeFinder.ToAsm(result.File, result.Code, result.Name, ctl) : null;
            asm.Text = UiCommon.Crlf(result.Asm ?? "; No machine code found.");
            // back to where we were
            int ci = asm.GetFirstCharIndexFromLine(Math.Min(asm.Lines.Length - 1, first + 30));
            if (ci >= 0) { asm.SelectionStart = ci; asm.ScrollToCaret(); }
            asm.SelectionStart = Math.Min(pos, asm.TextLength); asm.SelectionLength = 0;
        }

        // ---------------- right-click in the Machine code tab ----------------
        static readonly Regex AddrInLine = new Regex(@";\s([0-9A-F]{4})(\s|$)");

        /// <summary>The address a line of the .asm is about: its own (instructions and DB
        /// lines end with "; XXXX"), or for a label or comment line the line after it.</summary>
        int AsmLineAddress(int line)
        {
            var lines = asm.Lines;
            for (int i = line; i >= 0 && i < lines.Length && i < line + 20; i++)
            {
                string t = lines[i];
                if (t.TrimStart().StartsWith("ORG") || t.StartsWith("; ----")) return -1;
                var m = AddrInLine.Match(t);
                if (m.Success && !t.TrimStart().StartsWith(";")) return Convert.ToInt32(m.Groups[1].Value, 16);
            }
            return -1;
        }

        ContextMenuStrip AsmMenu()
        {
            var m = new ContextMenuStrip();
            var rename = new ToolStripMenuItem("Rename Label...");
            var comment = new ToolStripMenuItem("Add Comment...");
            var copy = new ToolStripMenuItem("Copy", null, (s, e) => { if (asm.SelectionLength > 0) asm.Copy(); });
            m.Items.AddRange(new ToolStripItem[] { rename, comment, new ToolStripSeparator(), copy });
            int addr = -1;
            m.Opening += (s, e) =>
            {
                addr = -1;
                if (result != null)
                {
                    int ci = asm.GetCharIndexFromPosition(asm.PointToClient(Cursor.Position));
                    addr = AsmLineAddress(asm.GetLineFromCharIndex(ci));
                }
                string a = addr >= 0 ? " (" + addr.ToString("X4") + "h)" : "";
                rename.Text = "Rename Label" + a + "...";
                comment.Text = "Add Comment" + a + "...";
                rename.Enabled = comment.Enabled = addr >= 0;
                copy.Enabled = asm.SelectionLength > 0;
            };
            rename.Click += (s, e) => RenameLabel(addr);
            comment.Click += (s, e) => AddComment(addr);
            return m;
        }

        void RenameLabel(int addr)
        {
            if (addr < 0) return;
            if (ctl == null) ctl = new ControlFile();
            string cur;
            if (!ctl.Labels.TryGetValue(addr, out cur)) cur = "L" + addr.ToString("X4");
            string name = Prompt("Rename Label", "Name for " + addr.ToString("X4") + "h (" + addr + "). Letters, digits and _; leave empty to go back to the automatic name.", cur);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) ctl.Labels.Remove(addr);
            else if (!ControlFile.ValidName(name)) { UiCommon.Error(this, "'" + name + "' can't be a label: use letters, digits and _, not starting with a digit."); return; }
            else
            {
                var clash = ctl.Labels.FirstOrDefault(kv => kv.Value.Equals(name, StringComparison.OrdinalIgnoreCase) && kv.Key != addr);
                if (clash.Value != null) { UiCommon.Error(this, "The label " + name + " is already used for " + clash.Key.ToString("X4") + "h."); return; }
                ctl.Labels[addr] = name;
            }
            ctlDirty = true;
            RedrawAsm();
            status.Text = (name.Length == 0 ? "Label removed from " : "Label " + name + " given to ") + addr.ToString("X4") + "h. File > Save Control File keeps it" + CtlNote + ".";
        }

        void AddComment(int addr)
        {
            if (addr < 0) return;
            string text = Prompt("Add Comment", "Comment for " + addr.ToString("X4") + "h (" + addr + "). It goes on a line of its own above that address.", "");
            if (string.IsNullOrWhiteSpace(text)) return;
            if (ctl == null) ctl = new ControlFile();
            ctl.AddComment(addr, text.Trim());
            ctlDirty = true;
            RedrawAsm();
            status.Text = "Comment added at " + addr.ToString("X4") + "h. File > Save Control File keeps it" + CtlNote + ".";
        }

        /// <summary>A one-line question; null if cancelled.</summary>
        string Prompt(string title, string question, string initial)
        {
            using (var f = new Form { Text = title, Width = 460, Height = 170, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var l = new Label { Text = question, Left = 12, Top = 10, Width = 420, Height = 36 };
                var box = new TextBox { Left = 12, Top = 50, Width = 420, Text = initial };
                var ok = new Button { Text = "OK", Left = 266, Top = 88, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 352, Top = 88, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l, box, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                box.SelectAll();
                return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
