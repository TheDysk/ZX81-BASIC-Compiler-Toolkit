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

        /// <summary>The end of every About box: copyright and the author's email.</summary>
        public static string AboutText { get { return "\n\nCopyright (c) Simon Round 2026\nEmail: " + ContactEmail(); } }

        // Built from pieces so the full address doesn't appear as plain text in the source
        // (spam bots scan GitHub). Keep the \u0040 escape: some editors turn it into the character.
        public static string ContactEmail()
        {
            return string.Join("", new[] { "thedysk", "\u0040", "gmail", ".", "com" });
        }

        // ------------------------------------------------------------------
        //  Help windows ("How it works")
        // ------------------------------------------------------------------
        /// <summary>Show help text in a resizable window where paragraphs flow to the
        /// window's width. Write each paragraph on one line (don't wrap it by hand: the
        /// window wraps it). A tiny markup:
        ///   # Heading            a heading (bold)
        ///   ordinary line        a paragraph
        ///   - item               a bullet
        ///   term :: meaning      a definition: the term on the left, the meaning flowing beside it
        ///   lines indented by 4 spaces are examples, shown as they are in a fixed-width font</summary>
        public static void ShowHelp(IWin32Window owner, string title, string text)
        {
            using (var f = new Form { Text = title, Width = 980, Height = 760, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, ShowIcon = false })
            {
                var box = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical };
                box.Rtf = HelpRtf(text);
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 6, 6), BackColor = Color.White };
                pad.Controls.Add(box);
                var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Width = 90, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
                var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
                close.Location = new Point(bottom.Width - close.Width - 12, 8);
                bottom.Controls.Add(close);
                bottom.Resize += (s, e) => close.Left = bottom.Width - close.Width - 12;
                f.Controls.Add(pad);
                f.Controls.Add(bottom);
                f.CancelButton = close;
                f.ShowDialog(owner);
            }
        }

        /// <summary>Turn the help markup (see ShowHelp) into RTF.</summary>
        public static string HelpRtf(string text)
        {
            var rtf = new StringBuilder();
            rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fswiss Segoe UI;}{\f1\fmodern Consolas;}}");
            rtf.Append(@"{\colortbl;\red0\green0\blue0;\red0\green60\blue140;\red90\green40\blue0;}");
            rtf.Append(@"\f0\fs19 ");
            var lines = text.Replace("\r", "").Split('\n');
            var para = new StringBuilder();
            string kind = null, term = null;          // what para holds: "p", "li", "dt"
            bool first = true, inCode = false;
            Action flush = () =>
            {
                if (kind == null) return;
                string body = RtfEsc(para.ToString().Trim());
                if (kind == "p") rtf.Append(@"\pard\sa100\li0\fi0 ").Append(body).Append(@"\par" + "\r\n");
                else if (kind == "li") rtf.Append(@"\pard\sa50\li480\fi-240 \bullet\tab ").Append(body).Append(@"\par" + "\r\n");
                else rtf.Append(@"\pard\sa50\li2200\fi-2000\tx2200 {\f1\cf3 ").Append(RtfEsc(term)).Append(@"}\tab ").Append(body).Append(@"\par" + "\r\n");
                para.Length = 0; kind = null; term = null;
            };
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) { flush(); continue; }
                if (line.StartsWith("    "))
                {
                    flush();
                    inCode = true;
                    rtf.Append(@"\pard\sa0\li480\fi0 {\f1\fs18 ").Append(RtfEsc(line.Substring(4))).Append(@"}\par" + "\r\n");
                    continue;
                }
                string t = line.Trim();
                if (inCode) { rtf.Append(@"\pard\sa0\li0\fi0\fs8\par\fs19 "); inCode = false; }   // a little space after an example
                if (t.StartsWith("# "))
                {
                    flush();
                    rtf.Append(@"\pard\sb" + (first ? "0" : "220") + @"\sa80\li0\fi0 {\b\fs23\cf2 ").Append(RtfEsc(t.Substring(2))).Append(@"}\par" + "\r\n");
                    first = false;
                    continue;
                }
                first = false;
                int dd = t.IndexOf(" :: ");
                if (t.StartsWith("- ")) { flush(); kind = "li"; para.Append(t.Substring(2)); continue; }
                if (dd > 0) { flush(); kind = "dt"; term = t.Substring(0, dd); para.Append(t.Substring(dd + 4)); continue; }
                flush(); kind = "p"; para.Append(t);
            }
            flush();
            rtf.Append("}");
            return rtf.ToString();
        }

        static string RtfEsc(string s)
        {
            var e = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\' || c == '{' || c == '}') e.Append('\\').Append(c);
                else if (c > 126) e.Append(@"\u").Append((short)c).Append('?');
                else e.Append(c);
            }
            return e.ToString();
        }
    }
}
