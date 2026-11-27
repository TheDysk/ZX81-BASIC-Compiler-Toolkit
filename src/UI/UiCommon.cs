// =====================================================================================
//  UiCommon.cs - small helpers shared by the three Windows programs
// =====================================================================================
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public static class UiCommon
    {
        /// <summary>A fixed-width font for listings (Consolas if installed).</summary>
        public static Font Mono(float size)
        {
            try
            {
                var f = new Font("Consolas", size);
                if (f.Name == "Consolas") return f;
            }
            catch { }
            return new Font(FontFamily.GenericMonospace, size);
        }

        /// <summary>A read-only or editable text box for listings.</summary>
        public static TextBox ListingBox(bool readOnly)
        {
            return new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                ReadOnly = readOnly,
                Dock = DockStyle.Fill,
                Font = Mono(10f),
                MaxLength = 0,
                HideSelection = false,
                BackColor = SystemColors.Window
            };
        }

        /// <summary>Text with Windows line endings, as a TextBox needs.</summary>
        public static string Crlf(string s)
        {
            return (s ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        /// <summary>Ask for a file name and save text (UTF-8, no byte order mark). Returns the path or null.</summary>
        public static string SaveText(IWin32Window owner, string text, string title, string filter, string suggested)
        {
            using (var d = new SaveFileDialog { Title = title, Filter = filter, FileName = suggested })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                File.WriteAllText(d.FileName, text, new UTF8Encoding(false));
                return d.FileName;
            }
        }

        /// <summary>Ask for a file name and save bytes. Returns the path or null.</summary>
        public static string SaveBytes(IWin32Window owner, byte[] data, string title, string filter, string suggested)
        {
            using (var d = new SaveFileDialog { Title = title, Filter = filter, FileName = suggested })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                File.WriteAllBytes(d.FileName, data);
                return d.FileName;
            }
        }

        public static void Error(IWin32Window owner, string msg)
        {
            MessageBox.Show(owner, msg, "ZX81 toolset", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>Allow files to be dropped on a form.</summary>
        public static void AcceptDrops(Form f, Action<string> open)
        {
            f.AllowDrop = true;
            f.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            f.DragDrop += (s, e) =>
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) open(files[0]);
            };
        }

        /// <summary>Open a program in any supported format (.p .81 .p81 .o .80 .wav .tzx).
        /// Shows an error box and returns null if it can't be read; a .wav that can't be
        /// decoded is reported with the [mm:ss.ms] time in the recording where it failed.</summary>
        public static LoadedProgram OpenProgram(IWin32Window owner, string file)
        {
            try { return TapeFormats.Load(file); }
            catch (TapeDecodeException e)
            {
                string where = e.Seconds >= 0 ? "\n\nThe problem is " + TapeDecodeException.Stamp(e.Seconds) + " (minutes:seconds.milliseconds) into the recording." : "";
                MessageBox.Show(owner, "Could not read the tape in " + System.IO.Path.GetFileName(file) + ":\n\n" + e.Message + where +
                    "\n\nTry a cleaner recording: a higher volume, no tone controls or noise reduction, and the whole program from the start.",
                    "Tape could not be read", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception e) { Error(owner, "Could not open " + System.IO.Path.GetFileName(file) + ":\n" + e.Message); }
            return null;
        }

        public const string AboutText = "\n\nCopyright (c) Simon Round 2026";
    }
}
