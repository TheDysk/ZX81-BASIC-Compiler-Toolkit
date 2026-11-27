// =====================================================================================
//  BrowserExtras.cs - more of the .P File Browser (part of BrowserForm):
//    Run in Emulator            start the emulator set up in Options > Configure Emulator
//    Play Audio to ZX81         play the tape sound (with progress and Stop) so a real
//                               ZX81 can LOAD the program from the computer's sound output
//    Export .p81 / ZX80 .o      the program with its tape name; the program for a ZX80
//    Edit a hidden number       double-click a {=value} in the listing (with "Show the
//                               hidden 5-byte numbers" on) to change the 5-byte value the
//                               ZX81 really uses, without changing the digits shown
//    Inject Binary into REM     right-click a REM line: its text is replaced by the bytes
//                               of a .bin file, and the system variables (D_FILE, VARS,
//                               E_LINE, ...) are worked out again for the new length
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public partial class BrowserForm
    {
        // ---------------- run in emulator ----------------
        /// <summary>Run the program in the emulator. An unchanged .p file is run from where it
        /// is; anything else (edited, or opened from a tape) is written to a temporary .p first.</summary>
        void RunInEmulator()
        {
            if (!Ready()) return;
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            bool asIs = !edited && !converted && path != null && File.Exists(path) && (ext == ".p" || ext == ".81");
            string file;
            try { file = asIs ? path : ToolConfig.TempP(p.Data, Stem); }
            catch (Exception e) { UiCommon.Error(this, "Could not write a temporary .p file:\n" + e.Message); return; }
            if (EmulatorUi.Run(this, file)) status.Text = "Started the emulator with " + file + (asIs ? "" : " (a temporary copy of the program as shown)");
        }

        // ---------------- other exports ----------------
        void ExportP81()
        {
            if (!Ready()) return;
            string name = TapeName;
            string f = UiCommon.SaveBytes(this, TapeDecode.MakeP81(p.Data, name), "Export .p81", ".p81 file (*.p81)|*.p81", Stem + ".p81");
            if (f != null) status.Text = "Saved " + f + " (tape name " + name + ")";
        }

        void ExportZx80()
        {
            if (!Ready()) return;
            var notes = new List<string>();
            byte[] o;
            try { o = Zx80.FromP(p, notes); }
            catch (Zx80Exception e) { UiCommon.Error(this, e.Message + "\n\nThe ZX80 (4K ROM) has whole-number BASIC only, no PLOT, no SIN/COS etc."); return; }
            string f = UiCommon.SaveBytes(this, o, "Export ZX80 program", "ZX80 program (*.o)|*.o|All files (*.*)|*.*", Stem + ".o");
            if (f == null) return;
            status.Text = "Saved " + f + " (" + o.Length + " bytes)";
            if (notes.Count > 0) MessageBox.Show(this, "Saved " + Path.GetFileName(f) + ". Notes:\n\n  " + string.Join("\n  ", notes), "ZX80 export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------------- play audio to a real ZX81 ----------------
        /// <summary>Length in seconds of a PCM .wav made by Tape.MakeWav.</summary>
        static double WavSeconds(byte[] wav)
        {
            int byteRate = BitConverter.ToInt32(wav, 28);
            for (int i = 12; i + 8 <= wav.Length; )
            {
                string id = Encoding.ASCII.GetString(wav, i, 4);
                int size = BitConverter.ToInt32(wav, i + 4);
                if (id == "data") return byteRate > 0 ? (double)size / byteRate : 0;
                i += 8 + size + (size & 1);
            }
            return 0;
        }

        void PlayAudio()
        {
            if (!Ready()) return;
            string name = TapeName;
            byte[] wav = Tape.MakeWav(p.Data, name);
            double total = WavSeconds(wav);
            using (var f = new Form { Text = "Play Audio to ZX81", Width = 520, Height = 230, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var info = new Label
                {
                    Left = 12, Top = 10, Width = 480, Height = 64,
                    Text = "Connect the computer's sound output to the ZX81's EAR socket, type LOAD \"\" on the ZX81 " +
                           "(or LOAD \"" + name + "\") and press NEWLINE, then click Play. Turn the volume well up.\n" +
                           "Program " + name + ": " + p.Data.Length + " bytes, " + total.ToString("0.0", CultureInfo.InvariantCulture) + " seconds of sound."
                };
                var bar = new ProgressBar { Left = 12, Top = 80, Width = 480, Height = 22, Minimum = 0, Maximum = 1000 };
                var time = new Label { Left = 12, Top = 108, Width = 480, Height = 20, Text = TapeDecodeException.Stamp(0) + " / " + TapeDecodeException.Stamp(total) };
                var play = new Button { Text = "Play", Left = 236, Top = 140, Width = 80 };
                var stop = new Button { Text = "Stop", Left = 324, Top = 140, Width = 80, Enabled = false };
                var close = new Button { Text = "Close", Left = 412, Top = 140, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { info, bar, time, play, stop, close });
                f.CancelButton = close;

                SoundPlayer player = null;
                MemoryStream ms = null;
                var clock = new System.Diagnostics.Stopwatch();
                var timer = new Timer { Interval = 100 };
                Action halt = () =>
                {
                    timer.Stop(); clock.Reset();
                    if (player != null) { try { player.Stop(); } catch (Exception) { } player.Dispose(); player = null; }
                    if (ms != null) { ms.Dispose(); ms = null; }
                    play.Enabled = true; stop.Enabled = false;
                };
                timer.Tick += (s, e) =>
                {
                    double t = clock.Elapsed.TotalSeconds;
                    if (t >= total + 0.3) { halt(); bar.Value = bar.Maximum; time.Text = "Finished. If the ZX81 didn't load it, try a louder volume."; return; }
                    bar.Value = Math.Min(bar.Maximum, (int)(t / Math.Max(total, 0.001) * bar.Maximum));
                    time.Text = TapeDecodeException.Stamp(Math.Min(t, total)) + " / " + TapeDecodeException.Stamp(total);
                };
                play.Click += (s, e) =>
                {
                    halt();
                    try
                    {
                        ms = new MemoryStream(wav);
                        player = new SoundPlayer(ms);
                        player.Load();
                        player.Play();           // plays in the background; Stop() ends it
                    }
                    catch (Exception ex)
                    {
                        halt();
                        UiCommon.Error(f, "Could not play the sound:\n" + ex.Message + "\n\nYou can use Export > Cassette audio (.wav) and play the file with another program.");
                        return;
                    }
                    clock.Start(); timer.Start();
                    play.Enabled = false; stop.Enabled = true;
                };
                stop.Click += (s, e) => { halt(); time.Text = "Stopped."; bar.Value = 0; };
                f.FormClosing += (s, e) => halt();
                f.ShowDialog(this);
                timer.Dispose();
            }
        }

        // ---------------- listing: which line / annotation is at a point ----------------
        /// <summary>The BASIC line shown at a point of the listing view (null if none), and
        /// the index of the character in the whole text.</summary>
        PLine ListingLineAt(Point pt, out int charIndex, out int row)
        {
            charIndex = rtb.GetCharIndexFromPosition(pt);
            row = rtb.GetLineFromCharIndex(charIndex);
            return row >= 0 && row < listingRows.Count ? listingRows[row] : null;
        }

        /// <summary>Which {=value} annotation of a row a character is in: its number (0 = the
        /// first in the row), or -1.</summary>
        int AnnotationAt(int row, int charIndex)
        {
            string all = rtb.Text;
            int start = rtb.GetFirstCharIndexFromLine(row);
            if (start < 0) return -1;
            int stop = all.IndexOf('\n', start); if (stop < 0) stop = all.Length;
            string text = all.Substring(start, stop - start);
            int at = charIndex - start, k = 0;
            for (int i = text.IndexOf("{="); i >= 0; i = text.IndexOf("{=", i + 2), k++)
            {
                int close = text.IndexOf('}', i);
                if (close < 0) close = text.Length - 1;
                if (at >= i && at <= close) return k;
            }
            return -1;
        }

        void ListingDoubleClick(Point pt)
        {
            if (p == null) return;
            int ci, row;
            var line = ListingLineAt(pt, out ci, out row);
            if (line == null) return;
            if (!chkFloats.Checked)
            {
                if (Basic81.HiddenNumberOffsets(line.Body).Count > 0)
                    status.Text = "Tick \"Show the hidden 5-byte numbers\", then double-click a {=value} to change it.";
                return;
            }
            int k = AnnotationAt(row, ci);
            if (k < 0) return;
            var offs = Basic81.HiddenNumberOffsets(line.Body);
            if (k >= offs.Count) return;
            EditHiddenNumber(line, line.BodyAddr + offs[k] + 1);
        }

        /// <summary>Change the 5 bytes of a hidden number at addr (the byte after 7Eh). The
        /// digits in the listing stay the same; the ZX81 uses the hidden value when it runs.</summary>
        void EditHiddenNumber(PLine line, int addr)
        {
            if (addr + 5 > p.FileEnd) { UiCommon.Error(this, "That number runs past the end of the file."); return; }
            var cur = new byte[5];
            Array.Copy(p.Mem, addr, cur, 0, 5);
            Func<byte[], string> hex = b => string.Join(" ", b.Select(x => x.ToString("X2")));
            using (var f = new Form { Text = "Edit hidden number", Width = 470, Height = 230, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var head = new Label { Left = 12, Top = 10, Width = 440, Height = 34, Text = "Line " + line.Number + ", 5-byte number at " + addr.ToString("X4") + "h. The digits in the listing are not changed; the ZX81 uses this value when the program runs." };
                var l1 = new Label { Text = "Value:", Left = 12, Top = 56, AutoSize = true };
                var val = new TextBox { Left = 110, Top = 52, Width = 200, Text = ZxFloat.Format(ZxFloat.Decode(cur, 0)) };
                var l2 = new Label { Text = "Bytes (hex):", Left = 12, Top = 90, AutoSize = true };
                var bytes = new TextBox { Left = 110, Top = 86, Width = 200, Font = UiCommon.Mono(9.5f), Text = hex(cur) };
                var msg = new Label { Left = 12, Top = 118, Width = 440, Height = 20, ForeColor = Color.DimGray, Text = "Type a number, or the 5 bytes (exponent, then 4 mantissa bytes)." };
                var ok = new Button { Text = "Apply", Left = 280, Top = 150, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 370, Top = 150, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { head, l1, val, l2, bytes, msg, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                bool busy = false;
                byte[] result = cur;
                val.TextChanged += (s, e) =>
                {
                    if (busy) return;
                    double v;
                    if (double.TryParse(val.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsInfinity(v))
                    {
                        result = ZxFloat.Encode(v);
                        busy = true; bytes.Text = hex(result); busy = false;
                        msg.Text = "Stored as " + ZxFloat.Format(ZxFloat.Decode(result, 0)); ok.Enabled = true;
                    }
                    else { msg.Text = "Not a number."; ok.Enabled = false; }
                };
                bytes.TextChanged += (s, e) =>
                {
                    if (busy) return;
                    var parts = bytes.Text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var b = new byte[5];
                    bool good = parts.Length == 5;
                    for (int i = 0; good && i < 5; i++)
                    {
                        int x;
                        good = int.TryParse(parts[i], NumberStyles.HexNumber, null, out x) && x <= 255;
                        if (good) b[i] = (byte)x;
                    }
                    if (good)
                    {
                        result = b;
                        busy = true; val.Text = ZxFloat.Format(ZxFloat.Decode(b, 0)); busy = false;
                        msg.Text = "Value " + val.Text; ok.Enabled = true;
                    }
                    else { msg.Text = "Give exactly 5 hex bytes."; ok.Enabled = false; }
                };
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (result.SequenceEqual(cur)) return;
                var data = (byte[])p.Data.Clone();
                Array.Copy(result, 0, data, addr - PFile.Base, 5);
                ReloadEdited(data, "Line " + line.Number + ": hidden number at " + addr.ToString("X4") + "h set to " + ZxFloat.Format(ZxFloat.Decode(result, 0)) + " (" + hex(result) + ").");
            }
        }

        /// <summary>Show changed file contents in every tab, keeping the listing's place.</summary>
        void ReloadEdited(byte[] data, string what)
        {
            int first = rtb.GetCharIndexFromPosition(new Point(1, 1));
            int firstRow = rtb.GetLineFromCharIndex(first);
            LoadInto(PFile.Load(data));
            edited = true;
            Text = ".P File Browser " + Versions.Show(Versions.Browser) + " - " + Path.GetFileName(path) + " (edited)";
            status.Text = what + " Use Save as .p... to keep the changes.";
            if (firstRow > 0)
            {
                int ci = rtb.GetFirstCharIndexFromLine(Math.Min(firstRow, Math.Max(0, rtb.Lines.Length - 1)));
                if (ci >= 0) { rtb.SelectionStart = ci; rtb.ScrollToCaret(); }
            }
        }

        // ---------------- listing context menu: inject a binary into a REM ----------------
        PLine menuLine;   // the line that was right-clicked

        ContextMenuStrip ListingMenu()
        {
            var m = new ContextMenuStrip();
            var inject = new ToolStripMenuItem("Inject Binary into REM...", null, (s, e) => { if (menuLine != null) InjectIntoRem(menuLine); });
            var editNum = new ToolStripMenuItem("Edit hidden number...");
            int numAddr = -1;
            editNum.Click += (s, e) => { if (menuLine != null && numAddr > 0) EditHiddenNumber(menuLine, numAddr); };
            var copy = new ToolStripMenuItem("Copy", null, (s, e) => { if (rtb.SelectionLength > 0) rtb.Copy(); });
            m.Items.AddRange(new ToolStripItem[] { inject, editNum, new ToolStripSeparator(), copy });
            m.Opening += (s, e) =>
            {
                menuLine = null; numAddr = -1;
                if (p == null) { inject.Enabled = editNum.Enabled = false; return; }
                int ci, row;
                menuLine = ListingLineAt(rtb.PointToClient(Cursor.Position), out ci, out row);
                bool isRem = menuLine != null && menuLine.Body.Length > 0 && menuLine.Body[0] == 0xEA;
                inject.Enabled = isRem;
                inject.Text = isRem ? "Inject Binary into REM (line " + menuLine.Number + ")..." : "Inject Binary into REM... (right-click a REM line)";
                if (menuLine != null && chkFloats.Checked)
                {
                    int k = AnnotationAt(row, ci);
                    var offs = Basic81.HiddenNumberOffsets(menuLine.Body);
                    if (k >= 0 && k < offs.Count) numAddr = menuLine.BodyAddr + offs[k] + 1;
                }
                editNum.Enabled = numAddr > 0;
                copy.Enabled = rtb.SelectionLength > 0;
            };
            return m;
        }

        /// <summary>Replace what follows REM in a line with the bytes of a file, then rebuild
        /// the .p so the later lines, the display file and the variables move along and the
        /// system variables that point at them (D_FILE, DF_CC, VARS, E_LINE, NXTLIN ...) are right.</summary>
        void InjectIntoRem(PLine line)
        {
            if (line.Body.Length == 0 || line.Body[0] != 0xEA) { UiCommon.Error(this, "Line " + line.Number + " doesn't start with REM."); return; }
            byte[] bin;
            string binName;
            using (var d = new OpenFileDialog { Filter = "Binary files (*.bin;*.b;*.obj;*.com)|*.bin;*.b;*.obj;*.com|All files (*.*)|*.*", Title = "Binary to put into the REM in line " + line.Number })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                binName = Path.GetFileName(d.FileName);
                try { bin = File.ReadAllBytes(d.FileName); }
                catch (Exception e) { UiCommon.Error(this, "Could not read the file:\n" + e.Message); return; }
            }
            string why;
            byte[] data = Basic81.InjectRem(p, line.Number, bin, out why);
            if (data == null) { UiCommon.Error(this, why); return; }
            int start = line.BodyAddr + 1;
            ReloadEdited(data, "Line " + line.Number + ": " + bin.Length + " bytes from " + binName + " put into the REM (they start at " + start.ToString("X4") + "h = " + start + ")." + why);
        }
    }
}
