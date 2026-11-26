// =====================================================================================
//  BrowserTabs.cs - building and filling the tabs of the .P File Browser
// -------------------------------------------------------------------------------------
//  Each tab has a Build...() method (creates its controls, called once) and a
//  Fill...() method (shows the current file, called after opening or editing).
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public partial class BrowserForm
    {
        // =================================================================
        //  OVERVIEW: system variables, auto-run, RAM, memory map
        // =================================================================
        Label lblAuto, lblFacts;
        ListView lvSys, lvMap;
        MemoryMapPanel mapPanel;
        ComboBox cbRam;

        void BuildOverviewTab()
        {
            var page = new TabPage("Overview");
            lblAuto = new Label { Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI", 13f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
            lblFacts = new Label { Dock = DockStyle.Top, Height = 58, Padding = new Padding(8, 2, 0, 0) };
            var mapBox = new GroupBox { Text = "Memory map", Dock = DockStyle.Top, Height = 230 };
            var ramRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28 };
            ramRow.Controls.Add(new Label { Text = "Show the map for a ZX81 with:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            cbRam = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            cbRam.Items.AddRange(new object[] { "1K (RAMTOP 4400h)", "2K (RAMTOP 4800h)", "4K (RAMTOP 5000h)", "16K (RAMTOP 8000h)", "32K (RAMTOP C000h)" });
            cbRam.SelectedIndexChanged += (s, e) => { if (p != null) FillMap(); };
            ramRow.Controls.Add(cbRam);
            ramRow.Controls.Add(new Label { Text = "(RAMTOP isn't saved in .p files, so choose the machine)", AutoSize = true, Padding = new Padding(6, 5, 0, 0), ForeColor = Color.DimGray });
            mapPanel = new MemoryMapPanel { Dock = DockStyle.Top, Height = 70 };
            lvMap = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, Font = UiCommon.Mono(9f) };
            foreach (var c in new[] { "Area", "From", "To", "Bytes", "Notes" }) lvMap.Columns.Add(c);
            lvMap.Columns[0].Width = 240; lvMap.Columns[1].Width = 110; lvMap.Columns[2].Width = 110; lvMap.Columns[3].Width = 70; lvMap.Columns[4].Width = 500;
            mapBox.Controls.Add(lvMap); mapBox.Controls.Add(mapPanel); mapBox.Controls.Add(ramRow);

            var sysBox = new GroupBox { Text = "System variables (4009h - 407Ch, as saved)", Dock = DockStyle.Fill };
            lvSys = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, Font = UiCommon.Mono(9f) };
            foreach (var c in new[] { "Address", "Name", "Value", "Meaning", "What it says about this file" }) lvSys.Columns.Add(c);
            lvSys.Columns[0].Width = 110; lvSys.Columns[1].Width = 80; lvSys.Columns[2].Width = 130; lvSys.Columns[3].Width = 430; lvSys.Columns[4].Width = 330;
            sysBox.Controls.Add(lvSys);

            page.Controls.Add(sysBox);
            page.Controls.Add(mapBox);
            page.Controls.Add(lblFacts);
            page.Controls.Add(lblAuto);
            tabs.TabPages.Add(page);
        }

        void FillOverview()
        {
            tracedLines.Clear();
            var al = p.AutoRunLine;
            lblAuto.Text = al != null ? "AUTO-RUN: starts at line " + al.Number + " as soon as it has loaded" : "NO AUTO-RUN: loads and stops at the BASIC prompt (type RUN)";
            lblAuto.ForeColor = al != null ? Color.DarkGreen : Color.DarkBlue;
            int need;
            string ram = p.RamEstimate(out need);
            var facts = new StringBuilder();
            facts.AppendLine("Size " + p.Data.Length + " bytes, " + p.Lines.Count + " BASIC lines" + (p.Lines.Count > 0 ? " (" + p.Lines[0].Number + "-" + p.Lines[p.Lines.Count - 1].Number + ")" : "") +
                ". Program cursor E_PPC = line " + p.EPpc + " (PPC itself isn't saved). Display file: " + (p.Display.Collapsed ? "collapsed (1K style)" : "expanded (16K style)") + ".");
            string needs = "RAM needed: " + ram + ".";
            if (rec != null && rec.IsOurs) needs += " Made by the ZX81 BASIC Compiler: needs 16K (its variables use RAM after the file).";
            foreach (var e in ca.Entries) if (!e.InFile && e.Address >= 0x4000) { needs += " Calls machine code at " + e.Address + " outside the file."; break; }
            facts.AppendLine(needs);
            if (p.Problems.Count > 0) facts.Append("Warnings: " + string.Join("; ", p.Problems));
            lblFacts.Text = facts.ToString();
            lblFacts.ForeColor = p.Problems.Count > 0 ? Color.DarkRed : SystemColors.ControlText;

            lvSys.BeginUpdate();
            lvSys.Items.Clear();
            foreach (var v in PFile.Sysvars)
            {
                var it = new ListViewItem(v.Address.ToString("X4") + "h " + v.Address);
                it.SubItems.Add(v.Name);
                it.SubItems.Add(p.SysvarValue(v));
                it.SubItems.Add(v.Meaning);
                it.SubItems.Add(p.SysvarNote(v));
                if (v.Name == "NXTLIN" || v.Name == "E_PPC") it.BackColor = al != null ? Color.FromArgb(210, 245, 210) : Color.FromArgb(255, 245, 200);
                if (v.IsPointer && p.SysvarNote(v).Contains("outside")) it.ForeColor = Color.DarkRed;
                lvSys.Items.Add(it);
            }
            lvSys.EndUpdate();
            // default RAM size for the map
            int idx = need <= 1024 && p.Display.Collapsed ? 0 : need <= 2048 && p.Display.Collapsed ? 1 : need <= 4096 && p.Display.Collapsed ? 2 : 3;
            if (p.FileEnd > 0x8000) idx = 4;
            if (cbRam.SelectedIndex == idx) FillMap(); else cbRam.SelectedIndex = idx;
        }

        static readonly int[] RamTops = { 0x4400, 0x4800, 0x5000, 0x8000, 0xC000 };

        void FillMap()
        {
            int top = RamTops[Math.Max(0, cbRam.SelectedIndex)];
            var areas = new List<MemoryMapPanel.Area>();
            Action<string, int, int, Color, string> add = (n, a, b, c, note) => { if (b > a) areas.Add(new MemoryMapPanel.Area { Name = n, From = a, To = b, Color = c, Note = note }); };
            int prog = Math.Min(p.ProgEnd, p.FileEnd), dEnd = Math.Min(Math.Max(p.VarsAddr, p.DFile), p.FileEnd);
            add("System variables (not saved)", 0x4000, 0x4009, Color.Silver, "ERR_NR, FLAGS, ERR_SP, RAMTOP, MODE, PPC");
            add("System variables", 0x4009, 0x407D, Color.Gray, "saved in the file");
            add("BASIC program", 0x407D, prog, Color.SteelBlue, p.Lines.Count + " lines" + (ca.CodeLines.Count > 0 ? ", " + ca.CodeLines.Count + " with machine code / data" : ""));
            add("Display file", p.DFile, dEnd, Color.MediumSeaGreen, p.Display.Collapsed ? "collapsed: empty lines are just 76h" : "expanded: 24 lines x 32 characters");
            add("Variables", p.VarsAddr, Math.Min(p.ELine, p.FileEnd), Color.Goldenrod, p.ReadVariables().Count + " saved variable(s)");
            if (p.FileEnd > p.ELine && p.ELine >= p.VarsAddr) add("Extra bytes after E_LINE", p.ELine, p.FileEnd, Color.IndianRed, "saved by some tools; ignored by the ZX81");
            int free = Math.Max(p.FileEnd, p.ELine) + 1;
            if (rec != null && rec.IsOurs && rec.BssSize > 0)
            {
                add("Edit line + calculator stack", free, rec.BssStart, Color.Plum, "used while running (not saved)");
                add("Compiled program's variables", rec.BssStart, rec.BssStart + rec.BssSize, Color.Orange, "cleared when the compiled program starts");
                free = rec.BssStart + rec.BssSize;
            }
            add("Spare RAM (machine stack at the top)", free, top, Color.FromArgb(235, 235, 235), top < free ? "TOO SMALL - the program needs more RAM" : (top - free) + " bytes free (before the stacks grow)");
            if (top < free) areas.Add(new MemoryMapPanel.Area { Name = "Over RAMTOP!", From = top, To = free, Color = Color.Red, Note = "this RAM size is too small" });
            foreach (var blk in ca.Blocks) areas.Add(new MemoryMapPanel.Area { Name = "machine code / data", From = blk[0], To = blk[1], Color = Color.DarkRed, Note = p.Region(blk[0]), Overlay = true });
            mapPanel.SetAreas(areas, 0x4000, Math.Max(top, free));

            lvMap.BeginUpdate();
            lvMap.Items.Clear();
            foreach (var a in areas)
            {
                var it = new ListViewItem((a.Overlay ? "   " : "") + a.Name);
                it.SubItems.Add(a.From.ToString("X4") + "h " + a.From);
                it.SubItems.Add((a.To - 1).ToString("X4") + "h " + (a.To - 1));
                it.SubItems.Add((a.To - a.From).ToString());
                it.SubItems.Add(a.Note);
                it.ForeColor = a.Overlay ? Color.DarkRed : SystemColors.WindowText;
                lvMap.Items.Add(it);
            }
            lvMap.EndUpdate();
        }

        // =================================================================
        //  LISTING
        // =================================================================
        RichTextBox rtb;
        CheckBox chkFloats, chkCode;
        const int MaxCodeRows = 300;      // Z80 rows shown under each REM line in the listing
        const int MaxLineChars = 1500;    // longer lines are shortened in the listing view

        void BuildListingTab()
        {
            var page = new TabPage("BASIC listing");
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(4, 5, 0, 0) };
            chkFloats = new CheckBox { Text = "Show the hidden 5-byte numbers  {=value}  ( ! = differs from the digits shown)", AutoSize = true };
            chkCode = new CheckBox { Text = "Show machine code in REM lines as Z80", AutoSize = true, Checked = true };
            chkFloats.CheckedChanged += (s, e) => { if (p != null) FillListing(); };
            chkCode.CheckedChanged += (s, e) => { if (p != null) FillListing(); };
            row.Controls.Add(chkFloats); row.Controls.Add(chkCode);
            rtb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = UiCommon.Mono(10f), BackColor = Color.White, DetectUrls = false };
            page.Controls.Add(rtb);
            page.Controls.Add(row);
            tabs.TabPages.Add(page);
        }

        void FillListing()
        {
            var o = new DetokOptions { Style = ListStyle.Display, ShowFloats = chkFloats.Checked, SummariseCodeRems = true };
            var rtf = new Rtf();
            foreach (var l in p.Lines)
            {
                var lt = Basic81.Detokenize(l.Body, o);
                rtf.Text(l.Number.ToString().PadLeft(5) + " ", Rtf.Gray);
                bool showCode = lt.IsCodeRem && chkCode.Checked;
                AppendColoured(rtf, lt);
                rtf.NewLine();
                if (showCode)
                {
                    int s = l.BodyAddr + 1, e = Math.Min(l.End - 1, p.FileEnd);
                    var rows = DisasmRange(s, e, true);
                    int shown = 0;
                    foreach (var r in rows)
                    {
                        if (shown++ == MaxCodeRows)
                        {
                            rtf.Text("        ... " + (rows.Count - MaxCodeRows) + " more rows - see the Hex / Z80 disassembly tab", Rtf.Gray);
                            rtf.NewLine();
                            break;
                        }
                        rtf.Text("        " + r.Addr.ToString("X4") + "  " + r.Bytes.PadRight(12) + "  " + r.Text.PadRight(24), r.Traced ? Rtf.Green : Rtf.Olive);
                        if (r.Note.Length > 0) rtf.Text("; " + r.Note, Rtf.Gray);
                        rtf.NewLine();
                    }
                }
            }
            rtb.Rtf = rtf.ToString();
        }

        /// <summary>Colour a line: keywords blue, strings dark red, hidden numbers purple,
        /// inverse characters white on black.</summary>
        void AppendColoured(Rtf rtf, LineText lt)
        {
            string t = lt.Text;
            string more = "";
            if (t.Length > MaxLineChars) { more = " ... (" + (t.Length - MaxLineChars) + " more characters)"; t = t.Substring(0, MaxLineChars); }
            var inv = new bool[t.Length];
            foreach (var kv in lt.Inverse) for (int k = kv.Key; k < kv.Key + kv.Value && k < t.Length; k++) inv[k] = true;
            var kw = new bool[t.Length];
            bool inStr = false;
            bool rem = t.StartsWith("REM");
            for (int i = 0; i < t.Length && !rem; i++)
            {
                if (t[i] == '"') { inStr = !inStr; continue; }
                if (inStr || !char.IsLetter(t[i]) || (i > 0 && char.IsLetterOrDigit(t[i - 1]))) continue;
                int j = i; while (j < t.Length && (char.IsLetter(t[j]) || t[j] == '$')) j++;
                string w = t.Substring(i, j - i);
                if (Basic81.KeywordCodes.ContainsKey(w) || w == "GOTO" || w == "GOSUB") for (int k = i; k < j; k++) kw[k] = true;
            }
            if (rem) for (int i = 0; i < Math.Min(3, t.Length); i++) kw[i] = true;
            inStr = false;
            bool inFloat = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '{' && i + 1 < t.Length && t[i + 1] == '=') inFloat = true;
                int col = rem && i >= 3 ? Rtf.Gray : inFloat ? Rtf.Purple : inStr || c == '"' ? Rtf.DarkRed : kw[i] ? Rtf.Blue : Rtf.Black;
                if (inv[i]) rtf.Inverse(c.ToString()); else rtf.Run(c, col);
                if (c == '"' && !inFloat) inStr = !inStr;
                if (c == '}') inFloat = false;
            }
            if (more.Length > 0) rtf.Text(more, Rtf.Gray);
        }

        // =================================================================
        //  SCREEN
        // =================================================================
        PictureBox picScreen;
        Label lblScreen;
        ComboBox cbScale;

        void BuildScreenTab()
        {
            var page = new TabPage("Screen (display file)");
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(4, 5, 0, 0), WrapContents = false };
            row.Controls.Add(new Label { Text = "Size:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            cbScale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
            cbScale.Items.AddRange(new object[] { "x1", "x2", "x3", "x4" });
            cbScale.SelectedIndex = 2;
            cbScale.SelectedIndexChanged += (s, e) => { if (p != null) FillScreen(); };
            row.Controls.Add(cbScale);
            var btn = new Button { Text = "Save picture...", AutoSize = true };
            btn.Click += (s, e) => ExportScreenPng();
            row.Controls.Add(btn);
            lblScreen = new Label { AutoSize = true, Padding = new Padding(10, 4, 0, 0) };
            row.Controls.Add(lblScreen);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.DimGray };
            picScreen = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(10, 10) };
            scroll.Controls.Add(picScreen);
            page.Controls.Add(scroll);
            page.Controls.Add(row);
            tabs.TabPages.Add(page);
        }

        void FillScreen()
        {
            var d = p.Display;
            if (picScreen.Image != null) picScreen.Image.Dispose();
            picScreen.Image = ZxRender.Screen(d, cbScale.SelectedIndex + 1);
            lblScreen.Text = (d.Collapsed ? "Collapsed display file (1K style): short lines are padded with spaces here." : "Expanded display file (16K style).") +
                " " + d.Size + " bytes at " + p.DFile.ToString("X4") + "h." + (d.Problem != null ? " Problem: " + d.Problem : "") +
                "  Letters are drawn with a Windows font (the ZX81's own font is in its ROM).";
        }

        // =================================================================
        //  VARIABLES
        // =================================================================
        ListView lvVars;

        void BuildVariablesTab()
        {
            var page = new TabPage("Variables");
            lvVars = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, Font = UiCommon.Mono(9.5f) };
            foreach (var c in new[] { "Name", "Type", "Address", "Bytes", "Value" }) lvVars.Columns.Add(c);
            lvVars.Columns[0].Width = 120; lvVars.Columns[1].Width = 160; lvVars.Columns[2].Width = 110; lvVars.Columns[3].Width = 60; lvVars.Columns[4].Width = 650;
            page.Controls.Add(lvVars);
            tabs.TabPages.Add(page);
        }

        void FillVariables()
        {
            lvVars.BeginUpdate();
            lvVars.Items.Clear();
            var vars = p.ReadVariables();
            foreach (var v in vars)
            {
                var it = new ListViewItem(v.Name);
                it.SubItems.Add(v.Kind);
                it.SubItems.Add(v.Address.ToString("X4") + "h " + v.Address);
                it.SubItems.Add(v.Size.ToString());
                it.SubItems.Add(v.Value);
                lvVars.Items.Add(it);
            }
            if (vars.Count == 0) lvVars.Items.Add(new ListViewItem("(no variables saved)"));
            lvVars.EndUpdate();
        }

        // =================================================================
        //  HEX / Z80 (synchronised)
        // =================================================================
        ListView lvHex, lvDis;
        TextBox txtGoto;
        CheckBox chkAllCode;
        bool syncing;

        class DisRow { public int Addr, Len; public string Bytes, Text, Note; public bool Traced; }
        List<DisRow> disRows = new List<DisRow>();
        const int HexBase = 0x4000;
        int HexRows { get { return p == null ? 0 : (p.FileEnd - HexBase + 15) / 16; } }

        void BuildHexTab()
        {
            var page = new TabPage("Hex / Z80 disassembly");
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(4, 4, 0, 0) };
            row.Controls.Add(new Label { Text = "Go to address (hex or decimal):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            txtGoto = new TextBox { Width = 80 };
            txtGoto.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { GoTo(); e.SuppressKeyPress = true; } };
            row.Controls.Add(txtGoto);
            var go = new Button { Text = "Go", AutoSize = true }; go.Click += (s, e) => GoTo(); row.Controls.Add(go);
            var edit = new Button { Text = "Edit bytes...", AutoSize = true }; edit.Click += (s, e) => EditBytes(SelectedAddress()); row.Controls.Add(edit);
            var save = new Button { Text = "Save .p as...", AutoSize = true }; save.Click += (s, e) => SaveP(); row.Controls.Add(save);
            chkAllCode = new CheckBox { Text = "Decode everything as Z80", AutoSize = true, Padding = new Padding(8, 3, 0, 0) };
            chkAllCode.CheckedChanged += (s, e) => { if (p != null) FillHex(); };
            row.Controls.Add(chkAllCode);
            row.Controls.Add(new Label { Text = "Green = traced machine code. Double-click a hex row to edit.", AutoSize = true, Padding = new Padding(10, 5, 0, 0), ForeColor = Color.DimGray });

            lvHex = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, Font = UiCommon.Mono(9.5f), HideSelection = false, MultiSelect = false };
            lvHex.Columns.Add("Address", 90); lvHex.Columns.Add("00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F", 400); lvHex.Columns.Add("ZX81 characters", 160);
            lvHex.RetrieveVirtualItem += (s, e) => e.Item = HexItem(e.ItemIndex);
            lvHex.SelectedIndexChanged += (s, e) => SyncFromHex();
            lvHex.DoubleClick += (s, e) => EditBytes(SelectedAddress());

            lvDis = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, Font = UiCommon.Mono(9.5f), HideSelection = false, MultiSelect = false };
            lvDis.Columns.Add("Address", 70); lvDis.Columns.Add("Bytes", 110); lvDis.Columns.Add("Instruction", 200); lvDis.Columns.Add("Where / notes", 260);
            lvDis.RetrieveVirtualItem += (s, e) => e.Item = DisItem(e.ItemIndex);
            lvDis.SelectedIndexChanged += (s, e) => SyncFromDis();

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            split.Panel1.Controls.Add(lvHex);
            split.Panel2.Controls.Add(lvDis);
            page.Controls.Add(split);
            page.Controls.Add(row);
            page.Layout += (s, e) => { try { split.SplitterDistance = page.Width * 52 / 100; } catch { } };
            tabs.TabPages.Add(page);
        }

        string AddrLabel(int a)
        {
            if (ca != null && ca.Labels.Contains(a)) return "L" + a.ToString("X4");
            return Z80Disasm.KnownLabel(a);
        }

        /// <summary>Should this byte be shown as data (DB) rather than decoded?
        /// Smart mode: only machine-code areas are decoded - traced code, and REM lines
        /// holding code that no USR reaches (they might still be code). Untraced bytes in a
        /// REM line that does have traced code are data.</summary>
        bool IsData(int a, bool smart)
        {
            if (!smart || ca.IsCode[a]) return false;
            var l = p.LineAt(a);
            if (l == null || a <= l.BodyAddr || a >= l.End - 1 || !ca.CodeLines.Contains(l)) return true;
            return LineHasTraced(l);   // code traced in this line: the rest is data
        }

        readonly Dictionary<PLine, bool> tracedLines = new Dictionary<PLine, bool>();
        bool LineHasTraced(PLine l)
        {
            bool b;
            if (tracedLines.TryGetValue(l, out b)) return b;
            b = false;
            for (int k = l.BodyAddr; k < l.End && k < p.FileEnd; k++) if (ca.IsCode[k]) { b = true; break; }
            tracedLines[l] = b;
            return b;
        }

        /// <summary>Disassemble a range. Traced instructions are used where known, so the listing
        /// stays in step with the real code; elsewhere bytes are decoded one after another
        /// (or, in smart mode, shown as data outside machine-code areas).</summary>
        List<DisRow> DisasmRange(int start, int end, bool smart = true)
        {
            var list = new List<DisRow>();
            int a = start;
            while (a < end)
            {
                if (IsData(a, smart))
                {
                    // up to 8 data bytes, stopping where the area or line changes
                    int n = 1;
                    string reg = p.Region(a);
                    while (n < 8 && a + n < end && IsData(a + n, smart) && p.Region(a + n) == reg && !Z80Disasm.SysvarLabels.ContainsKey(a + n)) n++;
                    var d = new DisRow { Addr = a, Len = n, Traced = false };
                    var hb = new StringBuilder(); var db = new List<string>();
                    for (int k = 0; k < n; k++) { hb.Append(p.Mem[a + k].ToString("X2")).Append(' '); db.Add(Z80Disasm.Hex(p.Mem[a + k], 2)); }
                    d.Bytes = hb.ToString().TrimEnd();
                    d.Text = "DB " + string.Join(",", db);
                    string sv;
                    d.Note = Z80Disasm.SysvarLabels.TryGetValue(a, out sv) ? sv : "";
                    list.Add(d);
                    a += n;
                    continue;
                }
                if (ca.CalcBytes[a] && !ca.InstrStarts.Contains(a))
                {
                    // the bytes after RST 28h (calculator codes, up to 34h end-calc) or
                    // RST 08h (error report): one row, with their names
                    int n = 1;
                    if (p.Mem[a - 1] != 0xCF)
                        while (a + n < end && n < 16 && ca.CalcBytes[a + n] && !ca.InstrStarts.Contains(a + n) && p.Mem[a + n - 1] != 0x34) n++;
                    var c = new DisRow { Addr = a, Len = n, Traced = true };
                    var hb = new StringBuilder(); var db = new List<string>();
                    for (int k = 0; k < n; k++) { hb.Append(p.Mem[a + k].ToString("X2")).Append(' '); db.Add(Z80Disasm.Hex(p.Mem[a + k], 2)); }
                    c.Bytes = hb.ToString().TrimEnd();
                    c.Text = "DB " + string.Join(",", db);
                    c.Note = (ca.Labels.Contains(a) ? "L" + a.ToString("X4") + ": " : "") + Z80Disasm.CalcComment(p.Mem, a, n);
                    list.Add(c);
                    a += n;
                    continue;
                }
                var ins = Z80Disasm.Decode(p.Mem, a, AddrLabel);
                bool traced = ca.InstrStarts.Contains(a);
                int len = ins.Len;
                // don't run over the start of a traced instruction
                for (int k = 1; k < len; k++) if (ca.InstrStarts.Contains(a + k)) { len = k; ins = null; break; }
                if (a + len > end) { len = end - a; ins = null; }
                var r = new DisRow { Addr = a, Len = len, Traced = traced };
                var sb = new StringBuilder();
                for (int k = 0; k < len; k++) sb.Append(p.Mem[a + k].ToString("X2")).Append(' ');
                r.Bytes = sb.ToString().TrimEnd();
                if (ins == null || (ca.CalcBytes[a] && !traced))
                {
                    var db = new List<string>();
                    for (int k = 0; k < len; k++) db.Add(Z80Disasm.Hex(p.Mem[a + k], 2));
                    r.Text = "DB " + string.Join(",", db);
                }
                else r.Text = ins.Text;
                r.Note = ca.Labels.Contains(a) ? "L" + a.ToString("X4") + ":" : "";
                list.Add(r);
                a += Math.Max(1, len);
            }
            return list;
        }

        void FillHex()
        {
            disRows = DisasmRange(PFile.Base, p.FileEnd, !chkAllCode.Checked);
            // note where each region starts
            string last = null;
            foreach (var r in disRows)
            {
                string reg = p.Region(r.Addr);
                if (reg.StartsWith("BASIC line")) reg = reg.Replace(" (header)", "");
                if (reg != last) { r.Note = ("[" + reg + "] " + r.Note).Trim(); last = reg; }
            }
            lvHex.VirtualListSize = HexRows;
            lvDis.VirtualListSize = disRows.Count;
            lvHex.Invalidate(); lvDis.Invalidate();
        }

        string HexRowText(int row, bool withAddr)
        {
            int a0 = HexBase + row * 16;
            var hex = new StringBuilder(); var chars = new StringBuilder();
            for (int k = 0; k < 16; k++)
            {
                int a = a0 + k;
                if (a < PFile.Base || a >= p.FileEnd) { hex.Append("-- "); chars.Append(' '); continue; }
                hex.Append(p.Mem[a].ToString("X2")).Append(' ');
                int c = p.Mem[a];
                chars.Append(c < 0x40 || (c >= 0x80 && c < 0xC0) ? Basic81.CodeToDisplay(c) : "·");
            }
            return (withAddr ? a0.ToString("X4") + "  " : "") + hex + " " + chars;
        }

        ListViewItem HexItem(int row)
        {
            int a0 = HexBase + row * 16;
            var it = new ListViewItem(a0.ToString("X4") + "h");
            string t = HexRowText(row, false);
            it.SubItems.Add(t.Substring(0, 48));
            it.SubItems.Add(t.Substring(49));
            bool code = false;
            for (int k = 0; k < 16; k++) if (a0 + k < 0x10000 && ca.IsCode[a0 + k]) code = true;
            if (code) it.BackColor = Color.FromArgb(225, 245, 225);
            return it;
        }

        ListViewItem DisItem(int i)
        {
            var r = disRows[i];
            var it = new ListViewItem(r.Addr.ToString("X4"));
            it.SubItems.Add(r.Bytes); it.SubItems.Add(r.Text); it.SubItems.Add(r.Note);
            if (r.Traced) it.BackColor = Color.FromArgb(225, 245, 225);
            return it;
        }

        int SelectedAddress()
        {
            if (p == null) return PFile.Base;
            if (lvDis.Focused && lvDis.SelectedIndices.Count > 0) return disRows[lvDis.SelectedIndices[0]].Addr;
            if (lvHex.SelectedIndices.Count > 0) return Math.Max(PFile.Base, HexBase + lvHex.SelectedIndices[0] * 16);
            return PFile.Base;
        }

        void SyncFromHex()
        {
            if (syncing || lvHex.SelectedIndices.Count == 0) return;
            syncing = true;
            int a = Math.Max(PFile.Base, HexBase + lvHex.SelectedIndices[0] * 16);
            int i = DisIndex(a);
            if (i >= 0) { lvDis.SelectedIndices.Clear(); lvDis.SelectedIndices.Add(i); lvDis.EnsureVisible(i); }
            syncing = false;
        }

        void SyncFromDis()
        {
            if (syncing || lvDis.SelectedIndices.Count == 0) return;
            syncing = true;
            int row = (disRows[lvDis.SelectedIndices[0]].Addr - HexBase) / 16;
            lvHex.SelectedIndices.Clear(); lvHex.SelectedIndices.Add(row); lvHex.EnsureVisible(row);
            syncing = false;
        }

        /// <summary>Index of the disassembly row containing address a (binary search).</summary>
        int DisIndex(int a)
        {
            int lo = 0, hi = disRows.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (disRows[mid].Addr <= a) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return best;
        }

        static bool TryAddr(string s, out int a)
        {
            s = (s ?? "").Trim();
            a = 0;
            if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase)) return int.TryParse(s.Substring(0, s.Length - 1), NumberStyles.HexNumber, null, out a);
            if (s.StartsWith("$") || s.StartsWith("#")) return int.TryParse(s.Substring(1), NumberStyles.HexNumber, null, out a);
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return int.TryParse(s.Substring(2), NumberStyles.HexNumber, null, out a);
            if (int.TryParse(s, out a)) return true;
            return int.TryParse(s, NumberStyles.HexNumber, null, out a);
        }

        void GoTo()
        {
            if (!Ready()) return;
            int a;
            if (!TryAddr(txtGoto.Text, out a) || a < HexBase || a >= p.FileEnd) { UiCommon.Error(this, "Give an address between " + PFile.Base.ToString("X4") + "h and " + (p.FileEnd - 1).ToString("X4") + "h, e.g. 4082h or 16514."); return; }
            int row = (a - HexBase) / 16;
            lvHex.SelectedIndices.Clear(); lvHex.SelectedIndices.Add(row); lvHex.EnsureVisible(row);
            int i = DisIndex(a);
            if (i >= 0) { syncing = true; lvDis.SelectedIndices.Clear(); lvDis.SelectedIndices.Add(i); lvDis.EnsureVisible(i); syncing = false; }
        }

        /// <summary>Change bytes in the file, then re-read it (so every tab shows the change).</summary>
        void EditBytes(int start)
        {
            if (!Ready()) return;
            using (var f = new Form { Text = "Edit bytes", Width = 560, Height = 200, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var lab1 = new Label { Text = "Address (hex):", Left = 12, Top = 16, AutoSize = true };
                var addr = new TextBox { Left = 120, Top = 12, Width = 80, Text = start.ToString("X4") };
                var lab2 = new Label { Text = "Bytes (hex):", Left = 12, Top = 50, AutoSize = true };
                var bytes = new TextBox { Left = 120, Top = 46, Width = 410, Font = UiCommon.Mono(9.5f) };
                Action load = () =>
                {
                    int a;
                    if (!TryAddr(addr.Text + "h", out a)) return;
                    var sb = new StringBuilder();
                    for (int k = 0; k < 16 && a + k < p.FileEnd; k++) if (a + k >= PFile.Base) sb.Append(p.Mem[a + k].ToString("X2")).Append(' ');
                    bytes.Text = sb.ToString().TrimEnd();
                };
                load();
                addr.Leave += (s, e) => load();
                var note = new Label { Text = "Change the hex values (separated by spaces). Bytes before 4009h or past the end of the file can't be edited.", Left = 12, Top = 80, Width = 520, Height = 32, ForeColor = Color.DimGray };
                var ok = new Button { Text = "Apply", Left = 360, Top = 120, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 450, Top = 120, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lab1, addr, lab2, bytes, note, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                int a0;
                if (!TryAddr(addr.Text + "h", out a0) || a0 < PFile.Base) { UiCommon.Error(this, "Bad address."); return; }
                var parts = bytes.Text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var data = (byte[])p.Data.Clone();
                for (int k = 0; k < parts.Length; k++)
                {
                    int v;
                    if (!int.TryParse(parts[k], NumberStyles.HexNumber, null, out v) || v > 255) { UiCommon.Error(this, "'" + parts[k] + "' is not a hex byte."); return; }
                    int idx = a0 + k - PFile.Base;
                    if (idx >= data.Length) { UiCommon.Error(this, "That goes past the end of the file."); return; }
                    data[idx] = (byte)v;
                }
                int keepRow = lvHex.SelectedIndices.Count > 0 ? lvHex.SelectedIndices[0] : 0;
                LoadInto(PFile.Load(data));
                edited = true;
                Text = ".P File Browser " + Versions.Show(Versions.Browser) + " - " + Path.GetFileName(path) + " (edited)";
                status.Text = parts.Length + " byte(s) changed at " + a0.ToString("X4") + "h. Use Save .p as... to keep the changes.";
                tabs.SelectedIndex = 4;
                if (keepRow < HexRows) { lvHex.SelectedIndices.Add(keepRow); lvHex.EnsureVisible(keepRow); }
            }
        }

        // =================================================================
        //  GRAPHICS (UDG / character set ripper)
        // =================================================================
        TextBox txtTileAddr;
        NumericUpDown numCount, numPerRow, numZoom;
        ListBox lbFound;
        PictureBox picTiles;

        void BuildGraphicsTab()
        {
            var page = new TabPage("Graphics (8x8 tiles)");
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4, 4, 0, 0) };
            row.Controls.Add(new Label { Text = "Address:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            txtTileAddr = new TextBox { Width = 70 };
            txtTileAddr.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { FillTiles(); e.SuppressKeyPress = true; } };
            row.Controls.Add(txtTileAddr);
            Func<string, int, int, int, NumericUpDown> num = (label, min, max, val) =>
            {
                row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(6, 5, 0, 0) });
                var n = new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 55 };
                n.ValueChanged += (s, e) => { if (p != null) FillTiles(); };
                row.Controls.Add(n);
                return n;
            };
            numCount = num("Tiles:", 8, 2048, 128);
            numPerRow = num("Per row:", 1, 64, 16);
            numZoom = num("Zoom:", 1, 8, 3);
            Action<string, Action> button = (t, act) => { var b = new Button { Text = t, AutoSize = true }; b.Click += (s, e) => act(); row.Controls.Add(b); };
            button("Show", FillTiles);
            button("-1 byte", () => Nudge(-1));
            button("+1 byte", () => Nudge(1));
            button("Next page", () => Nudge((int)numCount.Value * 8));
            button("Find character sets", FindSets);
            button("Export...", ExportTiles);

            lbFound = new ListBox { Dock = DockStyle.Left, Width = 220, Font = UiCommon.Mono(9f) };
            lbFound.SelectedIndexChanged += (s, e) =>
            {
                var item = lbFound.SelectedItem as string;
                if (item != null && item.Length >= 4 && Uri.IsHexDigit(item[0])) { txtTileAddr.Text = item.Substring(0, 4); FillTiles(); }
            };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            picTiles = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(6, 6) };
            scroll.Controls.Add(picTiles);
            var help = new Label { Dock = DockStyle.Bottom, Height = 36, ForeColor = Color.DimGray, Padding = new Padding(4, 2, 0, 0),
                Text = "Each 8 bytes are drawn as one 8x8 tile (one byte per row, bit 7 on the left) - the format hi-res (HRG) programs use for their characters and UDGs. Use -1/+1 byte to line the tiles up." };
            page.Controls.Add(scroll);
            page.Controls.Add(lbFound);
            page.Controls.Add(help);
            page.Controls.Add(row);
            tabs.TabPages.Add(page);
        }

        void FillGraphicsDefaults()
        {
            int a = ca.Blocks.Count > 0 ? ca.Blocks[0][0] : PFile.ProgStart;
            txtTileAddr.Text = a.ToString("X4");
            FillTiles();
            FindSets();
        }

        int TileAddr()
        {
            int a;
            if (!TryAddr(txtTileAddr.Text + "h", out a)) a = PFile.Base;
            return Math.Max(PFile.Base, Math.Min(a, Math.Max(PFile.Base, p.FileEnd - 8)));
        }

        void FillTiles()
        {
            if (p == null) return;
            int a = TileAddr();
            txtTileAddr.Text = a.ToString("X4");
            int count = Math.Min((int)numCount.Value, Math.Max(1, (p.FileEnd - a) / 8));
            if (picTiles.Image != null) picTiles.Image.Dispose();
            picTiles.Image = ZxRender.Tiles(p.Mem, a, count, (int)numPerRow.Value, (int)numZoom.Value, 2);
        }

        void Nudge(int by)
        {
            if (p == null) return;
            txtTileAddr.Text = Math.Max(PFile.Base, TileAddr() + by).ToString("X4");
            FillTiles();
        }

        void FindSets()
        {
            if (!Ready()) return;
            lbFound.Items.Clear();
            var found = ZxRender.FindCharSets(p.Mem, PFile.Base, p.FileEnd);
            lbFound.Items.Add("Possible character sets:");
            foreach (var a in found) lbFound.Items.Add(a.ToString("X4") + "h  " + p.Region(a));
            if (found.Count == 0) lbFound.Items.Add("  none found");
        }

        void ExportTiles()
        {
            if (!Ready()) return;
            int a = TileAddr(), count = Math.Min((int)numCount.Value, (p.FileEnd - a) / 8);
            using (var d = new SaveFileDialog { Filter = "PNG picture (*.png)|*.png|Raw bytes (*.bin)|*.bin|Assembly DB lines (*.asm)|*.asm", FileName = Stem + "_" + a.ToString("X4") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string ext = Path.GetExtension(d.FileName).ToLowerInvariant();
                if (ext == ".bin")
                {
                    var b = new byte[count * 8]; Array.Copy(p.Mem, a, b, 0, b.Length); File.WriteAllBytes(d.FileName, b);
                }
                else if (ext == ".asm")
                {
                    var sb = new StringBuilder("; " + count + " 8x8 tiles from " + Stem + " at " + a.ToString("X4") + "h\r\n");
                    for (int t = 0; t < count; t++)
                    {
                        var db = new List<string>();
                        for (int k = 0; k < 8; k++) db.Add(Z80Disasm.Hex(p.Mem[a + t * 8 + k], 2));
                        sb.Append("        DB " + string.Join(",", db) + "   ; tile " + t + "\r\n");
                    }
                    File.WriteAllText(d.FileName, sb.ToString());
                }
                else using (var bmp = ZxRender.Tiles(p.Mem, a, count, (int)numPerRow.Value, (int)numZoom.Value, 2)) bmp.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
                status.Text = "Saved " + d.FileName;
            }
        }
    }

    // =====================================================================
    //  Memory map bar
    // =====================================================================
    public class MemoryMapPanel : Panel
    {
        public class Area { public string Name, Note; public int From, To; public Color Color; public bool Overlay; }
        List<Area> areas = new List<Area>();
        int lo = 0x4000, hi = 0x8000;
        readonly ToolTip tip = new ToolTip();

        public MemoryMapPanel() { DoubleBuffered = true; ResizeRedraw = true; }

        public void SetAreas(List<Area> a, int from, int to) { areas = a; lo = from; hi = Math.Max(to, from + 1); Invalidate(); }

        int X(int addr) { int w = Width - 20; return 10 + (int)((long)(Math.Min(Math.Max(addr, lo), hi) - lo) * w / (hi - lo)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int top = 6, h = 32;
            foreach (var a in areas)
            {
                int x1 = X(a.From), x2 = Math.Max(X(a.To), x1 + 1);
                if (a.Overlay) { using (var br = new SolidBrush(Color.FromArgb(170, a.Color))) g.FillRectangle(br, x1, top + h - 8, x2 - x1, 8); continue; }
                using (var br = new SolidBrush(a.Color)) g.FillRectangle(br, x1, top, x2 - x1, h);
                g.DrawRectangle(Pens.Black, x1, top, x2 - x1, h);
                var size = g.MeasureString(a.Name, Font);
                if (size.Width < x2 - x1 - 4) g.DrawString(a.Name, Font, Brushes.Black, x1 + 3, top + 3);
            }
            g.DrawString(lo.ToString("X4") + "h", Font, Brushes.Black, 6, top + h + 4);
            string end = hi.ToString("X4") + "h (" + ((hi - 0x4000) / 1024.0).ToString("0.#") + "K of RAM shown)";
            g.DrawString(end, Font, Brushes.Black, Width - g.MeasureString(end, Font).Width - 6, top + h + 4);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            foreach (var a in areas)
                if (!a.Overlay && e.X >= X(a.From) && e.X <= X(a.To))
                {
                    string t = a.Name + ": " + a.From.ToString("X4") + "h-" + (a.To - 1).ToString("X4") + "h, " + (a.To - a.From) + " bytes";
                    if (tip.GetToolTip(this) != t) tip.SetToolTip(this, t);
                    return;
                }
        }
    }

    // =====================================================================
    //  A tiny RTF writer for the coloured listing (much faster than colouring a RichTextBox piece by piece)
    // =====================================================================
    public class Rtf
    {
        public const int Black = 1, Gray = 2, Blue = 3, DarkRed = 4, Purple = 5, Green = 6, White = 7, Olive = 8;
        readonly StringBuilder sb = new StringBuilder();
        public Rtf()
        {
            sb.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fmodern Consolas;}}");
            sb.Append(@"{\colortbl;\red0\green0\blue0;\red128\green128\blue128;\red0\green0\blue200;\red150\green0\blue0;\red128\green0\blue160;\red0\green120\blue0;\red255\green255\blue255;\red120\green120\blue40;}");
            sb.Append(@"\f0\fs20 ");
        }
        static string Esc(string s)
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
        int current = -1;   // colour of the run being written (-1 = none)
        /// <summary>Add one character, starting a new colour run only when the colour changes.</summary>
        public void Run(char c, int colour)
        {
            if (colour != current) { sb.Append(@"\cf").Append(colour).Append(' '); current = colour; }
            sb.Append(Esc(c.ToString()));
        }
        public void Text(string s, int colour) { sb.Append(@"\cf").Append(colour).Append(' ').Append(Esc(s)); current = colour; }
        public void Inverse(string s) { sb.Append(@"\cf7\highlight1 ").Append(Esc(s)).Append(@"\highlight0 "); current = 7; }
        public void NewLine() { sb.Append(@"\par" + "\r\n"); }
        public override string ToString() { return sb.ToString() + "}"; }
    }
}
