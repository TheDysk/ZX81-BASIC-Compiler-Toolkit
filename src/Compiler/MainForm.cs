// =====================================================================================
//  MainForm.cs - the Windows Forms front end (ZX81BasicCompiler.exe)
// -------------------------------------------------------------------------------------
//  The window has, from top to bottom:
//     menu bar        File / Build / Help
//     toolbar         Open, Save, Compile, Compile as, Check
//     options row     the tick boxes, "Max string length" and "RAM"
//     editor          the BASIC listing (an ordinary multi-line TextBox)
//     messages        errors and warnings; double-click one to jump to its line
//     status bar      what just happened
//
//  The window is built in code in the constructor (there is no Visual Studio
//  designer file). All the compiling is done by Compiler.Compile; this file only
//  handles the user interface and files.
// =====================================================================================
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ZX81Compiler
{
    /// <summary>Entry point of ZX81BasicCompiler.exe. A listing can be passed on the command
    /// line (or by dropping a file on the .exe in Explorer) and is opened at start-up.</summary>
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var f = new MainForm();
            if (args.Length > 0 && File.Exists(args[0])) f.LoadListing(args[0]);
            Application.Run(f);
        }
    }

    public class MainForm : Form
    {
        // ---- controls ----
        readonly TextBox editor;                               // the BASIC listing
        readonly ListBox messages;                             // errors / warnings (Diagnostic objects) and summaries (strings)
        readonly CheckBox chkAutorun, chkBreak, chkLower, chkAsm;   // the compile options
        readonly NumericUpDown numMaxStr;                      // Options.MaxString
        readonly ComboBox cbRam;                               // Options.Ram: 16K, 32K or 64K
        readonly CheckBox chkStrict;                           // Options.StrictInt
        readonly ToolStripStatusLabel status;                  // status bar text
        // ---- state ----
        string listingPath;   // file the listing was loaded from / saved to (null = never saved)
        string outputPath;    // where the last .p was saved (F5 saves there again without asking)
        bool dirty;           // listing changed since it was loaded or saved

        /// <summary>Build the window and its controls.</summary>
        public MainForm()
        {
            Text = "ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler);
            // Wide enough for the whole options row; never bigger than the screen. If the
            // window is made narrower, the options wrap onto a second row instead of vanishing.
            var work = Screen.PrimaryScreen.WorkingArea;
            Width = Math.Min(1280, work.Width); Height = Math.Min(760, work.Height);
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;    // files can be dragged onto the window
            Font = new Font("Segoe UI", 9f);

            // ---- menu ----
            // Each item: text (& marks the Alt-key letter), no icon, what to do, shortcut key.
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&New", null, (s, e) => NewListing(), Keys.Control | Keys.N));
            file.DropDownItems.Add(new ToolStripMenuItem("&Open listing, program or tape...", null, (s, e) => OpenListing(), Keys.Control | Keys.O));
            file.DropDownItems.Add(new ToolStripMenuItem("&Save listing", null, (s, e) => SaveListing(false), Keys.Control | Keys.S));
            file.DropDownItems.Add(new ToolStripMenuItem("Save listing &as...", null, (s, e) => SaveListing(true)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (s, e) => Close()));
            var build = new ToolStripMenuItem("&Build");
            build.DropDownItems.Add(new ToolStripMenuItem("&Compile to .p", null, (s, e) => CompileNow(false), Keys.F5));
            build.DropDownItems.Add(new ToolStripMenuItem("Compile to .p &as...", null, (s, e) => CompileNow(true), Keys.Shift | Keys.F5));
            build.DropDownItems.Add(new ToolStripMenuItem("Check &syntax only", null, (s, e) => CheckOnly(), Keys.F7));
            build.DropDownItems.Add(new ToolStripSeparator());
            build.DropDownItems.Add(new ToolStripMenuItem("Compile && &Run", null, (s, e) => CompileAndRun(), Keys.Control | Keys.F5));
            var options = new ToolStripMenuItem("&Options");
            options.DropDownItems.Add(new ToolStripMenuItem("Configure &Emulator...", null, (s, e) => EmulatorUi.Configure(this)));
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&How it works / Supported BASIC...", null, (s, e) => ShowHelp(), Keys.F1));
            help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (s, e) => MessageBox.Show(this,
                "ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler) + "\n\nCompiles a ZX81 BASIC text listing into Z80 machine code held in line 0 (REM), with line 10 RAND USR 16514, saved as a .p file.\n\nTarget: ZX81 with 16K, 32K or 64K RAM." + UiCommon.AboutText, "About")));
            menu.Items.Add(file); menu.Items.Add(build); menu.Items.Add(options); menu.Items.Add(help);

            // ---- toolbar ----
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(new ToolStripButton("Open listing...", null, (s, e) => OpenListing()));
            tools.Items.Add(new ToolStripButton("Save listing", null, (s, e) => SaveListing(false)));
            tools.Items.Add(new ToolStripSeparator());
            var compileBtn = new ToolStripButton("Compile to .p  (F5)", null, (s, e) => CompileNow(false)) { Font = new Font(Font, FontStyle.Bold) };
            tools.Items.Add(compileBtn);
            tools.Items.Add(new ToolStripButton("Compile as...", null, (s, e) => CompileNow(true)));
            tools.Items.Add(new ToolStripButton("Check (F7)", null, (s, e) => CheckOnly()));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("Compile && Run  (Ctrl+F5)", null, (s, e) => CompileAndRun()));

            // ---- options panel ----
            // A FlowLayoutPanel lays its controls out left to right, wrapping onto another
            // row (and growing taller) when the window is too narrow for them all.
            var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(6, 6, 6, 4), WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            chkAutorun = new CheckBox { Text = "Auto-run after LOAD", Checked = true, AutoSize = true };
            chkBreak = new CheckBox { Text = "BREAK (SPACE) stops program", Checked = true, AutoSize = true };
            chkLower = new CheckBox { Text = "Lower case in strings = inverse", Checked = false, AutoSize = true };
            chkAsm = new CheckBox { Text = "Also save .asm listing", Checked = false, AutoSize = true };
            chkStrict = new CheckBox { Text = "Strict integer", Checked = false, AutoSize = true };
            var lbl = new Label { Text = "Max string length:", AutoSize = true, Padding = new Padding(10, 4, 0, 0) };
            numMaxStr = new NumericUpDown { Minimum = 1, Maximum = 255, Value = 255, Width = 55 };
            var lblRam = new Label { Text = "RAM:", AutoSize = true, Padding = new Padding(10, 4, 0, 0) };
            cbRam = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
            cbRam.Items.AddRange(new object[] { "16K", "32K", "64K" });
            cbRam.SelectedIndex = 0;
            var tips = new ToolTip();   // the hints shown when the mouse rests on an option
            tips.SetToolTip(chkAutorun, "The program starts by itself when loaded (via line 10 RAND USR 16514).");
            tips.SetToolTip(chkBreak, "Checks the BREAK key on every GOTO and NEXT, like ZX81 BASIC. Untick for games that use SPACE.");
            tips.SetToolTip(chkLower, "If ticked, lower case letters inside \"strings\" become inverse video letters.");
            tips.SetToolTip(chkStrict, "No floating point: SIN, PI, numbers like 0.5, VAL and RND on its own become errors, so the program is all fast whole-number code. A line #STRICT_INT in the listing does the same.");
            tips.SetToolTip(numMaxStr, "Room reserved for each ordinary string variable (A$..Z$). Lower it if you run out of memory.");
            tips.SetToolTip(cbRam, "The RAM pack the program is for. With 32K or 64K the variables go above 32K (8000h), giving 16K or 32K for variables, arrays and strings. The code itself must still fit below 32K.");
            foreach (Control c in new Control[] { chkAutorun, chkBreak, chkLower, chkAsm, chkStrict, lbl, numMaxStr, lblRam, cbRam })
            {
                c.Margin = new Padding(4, 0, 12, 4);
                opts.Controls.Add(c);
            }

            // ---- editor & messages ----
            editor = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AcceptsTab = true,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 11f),
                MaxLength = 0,          // no limit on the text length
                HideSelection = false   // keep the selection visible (used by GoToMessage)
            };
            try { editor.Font = new Font("Consolas", 11f); } catch { }   // nicer font if available
            editor.TextChanged += (s, e) => { if (!dirty) { dirty = true; UpdateTitle(); } };

            messages = new ListBox { Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9.5f), IntegralHeight = false, HorizontalScrollbar = true };
            try { messages.Font = new Font("Consolas", 9.5f); } catch { }
            messages.DoubleClick += (s, e) => GoToMessage();

            // editor above, messages below, with a draggable divider
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(editor);
            var msgGroup = new GroupBox { Text = "Messages (double-click to go to the line)", Dock = DockStyle.Fill };
            msgGroup.Controls.Add(messages);
            split.Panel2.Controls.Add(msgGroup);

            var strip = new StatusStrip();
            status = new ToolStripStatusLabel("Open a ZX81 BASIC listing (.txt / .bas) or type one in, then press F5.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            strip.Items.Add(status);

            // Docked controls are laid out in reverse order of adding, so the Fill
            // control (split) is added first and the menu, which goes at the very top, last.
            Controls.Add(split);
            Controls.Add(opts);
            Controls.Add(tools);
            Controls.Add(menu);
            Controls.Add(strip);
            MainMenuStrip = menu;

            // ---- events ----
            Load += (s, e) => { try { split.SplitterDistance = (int)(split.Height * 0.72); } catch { } };   // editor gets ~3/4
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && ConfirmDiscard()) LoadListing(files[0]);
            };
            FormClosing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
            UpdateTitle();
        }

        /// <summary>Window title: file name, with * if there are unsaved changes.</summary>
        void UpdateTitle()
        {
            Text = "ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler) + " - " + (listingPath == null ? "untitled" : Path.GetFileName(listingPath)) + (dirty ? " *" : "");
        }

        /// <summary>Before throwing the listing away: offer to save unsaved changes.
        /// Returns false if the user pressed Cancel.</summary>
        bool ConfirmDiscard()
        {
            if (!dirty || editor.TextLength == 0) return true;
            var r = MessageBox.Show(this, "Save changes to the listing first?", "ZX81 BASIC Compiler", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveListing(false);
            return true;
        }

        /// <summary>File > New.</summary>
        void NewListing()
        {
            if (!ConfirmDiscard()) return;
            editor.Clear(); listingPath = null; outputPath = null; dirty = false; messages.Items.Clear(); UpdateTitle();
        }

        /// <summary>File > Open listing.</summary>
        void OpenListing()
        {
            if (!ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = TapeFormats.CompilerOpenFilter, Title = "Open a ZX81 BASIC listing, program or tape" })
                if (d.ShowDialog(this) == DialogResult.OK) LoadListing(d.FileName);
        }

        /// <summary>Load a listing file into the editor. A program or tape (.p, .p81, ZX80
        /// .o, .wav, .tzx) is turned into BASIC by the decompiler and opened as a new,
        /// unsaved listing.</summary>
        public void LoadListing(string path)
        {
            bool program = TapeFormats.IsProgramFile(path);
            try
            {
                Cursor = Cursors.WaitCursor;
                string text;
                try { text = Cli.ReadListing(path); }
                finally { Cursor = Cursors.Default; }
                // A Windows TextBox needs \r\n line endings; files may have just \n
                editor.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                listingPath = program ? null : path;   // Save asks for a name for a listing made from a program
                outputPath = null;
                dirty = program;
                messages.Items.Clear();
                // How big will it be? A trial compile (nothing is saved) tells the user
                // straight away whether the program fits the ZX81's RAM.
                Cursor = Cursors.WaitCursor;
                string est = Compiler.Estimate(editor.Text, GetOptions());
                Cursor = Cursors.Default;
                AddLines(est);
                status.Text = (program ? "Opened " + path + " as BASIC (save it as a listing to keep it)" : "Loaded " + path) + "  -  " + est.Split('\n').Last();
                UpdateTitle();
            }
            catch (TapeDecodeException ex)
            {
                MessageBox.Show(this, "Could not read the tape in " + Path.GetFileName(path) + ":\n\n" + ex.Message +
                    (ex.Seconds >= 0 ? "\n\nThe problem is " + TapeDecodeException.Stamp(ex.Seconds) + " (minutes:seconds.milliseconds) into the recording." : ""),
                    "Tape could not be read", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not open the file:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        /// <summary>Save the listing. asNew = always ask for a file name (Save as).
        /// Returns false if the user cancelled.</summary>
        bool SaveListing(bool asNew)
        {
            if (asNew || listingPath == null)
            {
                using (var d = new SaveFileDialog { Filter = "Text listing (*.txt)|*.txt|BASIC listing (*.bas)|*.bas|All files (*.*)|*.*", Title = "Save listing" })
                {
                    if (listingPath != null) d.FileName = Path.GetFileName(listingPath);
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    listingPath = d.FileName;
                }
            }
            File.WriteAllText(listingPath, editor.Text, new UTF8Encoding(false));   // UTF-8 without a byte order mark
            dirty = false; UpdateTitle();
            status.Text = "Saved " + listingPath;
            return true;
        }

        /// <summary>Read the option controls into an Options object.</summary>
        Options GetOptions()
        {
            return new Options
            {
                Autorun = chkAutorun.Checked,
                BreakCheck = chkBreak.Checked,
                LowerIsInverse = chkLower.Checked,
                MaxString = (int)numMaxStr.Value,
                MakeListing = chkAsm.Checked,
                Ram = 16 << cbRam.SelectedIndex,         // 16, 32, 64
                StrictInt = chkStrict.Checked
            };
        }

        /// <summary>Compile the editor's text. An unexpected exception (a bug in the
        /// compiler) is turned into an error message rather than crashing the program.</summary>
        CompileResult RunCompiler()
        {
            messages.Items.Clear();
            Cursor = Cursors.WaitCursor;
            try { return Compiler.Compile(editor.Text, GetOptions()); }
            catch (Exception ex)
            {
                var r = new CompileResult();
                r.Diagnostics.Add(new Diagnostic { IsError = true, BasicLine = -1, Message = "internal compiler error: " + ex.Message });
                return r;
            }
            finally { Cursor = Cursors.Default; }
        }

        /// <summary>Show errors first, then warnings.</summary>
        void ShowDiagnostics(CompileResult res)
        {
            foreach (var d in res.Diagnostics.OrderBy(d => d.IsError ? 0 : 1)) messages.Items.Add(d);
        }

        /// <summary>Build > Check syntax only: compile but don't save.</summary>
        void CheckOnly()
        {
            var res = RunCompiler();
            ShowDiagnostics(res);
            if (res.Success) { AddLines(res.Summary); status.Text = "No errors. Machine code " + Compiler.Size(res.CodeSize) + "."; }
            else status.Text = "Errors found - see messages.";
        }

        /// <summary>Build > Compile && Run (Ctrl+F5): compile, save, and start the emulator
        /// set up in Options > Configure Emulator with the new file.</summary>
        void CompileAndRun()
        {
            if (!CompileNow(false)) return;
            if (EmulatorUi.Run(this, outputPath)) status.Text = "Saved " + outputPath + " and started the emulator.";
        }

        /// <summary>Build > Compile to .p. Asks for a file name the first time (or always
        /// when askName is true), then saves there on later compiles. Returns true if saved.</summary>
        bool CompileNow(bool askName)
        {
            var res = RunCompiler();
            ShowDiagnostics(res);
            if (!res.Success)
            {
                if (res.MemoryEnd != 0) AddLines(Compiler.SizeLines(res));   // out of memory: show the sizes
                status.Text = "Compilation failed - see messages.";
                if (messages.Items.Count > 0) messages.SelectedIndex = 0;
                return false;
            }
            if (askName || outputPath == null)
            {
                using (var d = new SaveFileDialog { Filter = "ZX81 program (*.p)|*.p|All files (*.*)|*.*", Title = "Save compiled program", DefaultExt = "p" })
                {
                    // suggest the last output name, or the listing's name with .p
                    if (outputPath != null) { d.InitialDirectory = Path.GetDirectoryName(outputPath); d.FileName = Path.GetFileName(outputPath); }
                    else if (listingPath != null) { d.InitialDirectory = Path.GetDirectoryName(listingPath); d.FileName = Path.GetFileNameWithoutExtension(listingPath) + ".p"; }
                    else d.FileName = "program.p";
                    if (d.ShowDialog(this) != DialogResult.OK) { status.Text = "Compiled OK but not saved."; return false; }
                    outputPath = d.FileName;
                }
            }
            try
            {
                File.WriteAllBytes(outputPath, res.PFile);
                if (chkAsm.Checked && res.Listing != null) File.WriteAllText(Path.ChangeExtension(outputPath, ".asm"), res.Listing + res.Symbols);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not write the file:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            AddLines(res.Summary);
            messages.Items.Add("Saved " + outputPath);
            status.Text = "Saved " + outputPath + "  -  machine code " + Compiler.Size(res.CodeSize) + ", .p file " + Compiler.Size(res.PFile.Length);
            return true;
        }

        /// <summary>Add text to the messages list, one item per line.</summary>
        void AddLines(string text)
        {
            foreach (var line in text.Split('\n')) if (line.Length > 0) messages.Items.Add(line);
        }

        /// <summary>Double-click on a message: select the line it refers to in the editor.
        /// Uses the text line if known, otherwise searches for the BASIC line number.</summary>
        void GoToMessage()
        {
            var d = messages.SelectedItem as Diagnostic;   // summaries are plain strings: ignored
            if (d == null) return;
            int target = d.SourceLine;
            if (target <= 0 && d.BasicLine >= 0)
            {
                // find the text line that starts with this BASIC line number
                for (int i = 0; i < editor.Lines.Length; i++)
                {
                    string l = editor.Lines[i].TrimStart();
                    int p = 0; while (p < l.Length && char.IsDigit(l[p])) p++;
                    int n;
                    if (p > 0 && int.TryParse(l.Substring(0, p), out n) && n == d.BasicLine) { target = i + 1; break; }
                }
            }
            if (target <= 0 || target > editor.Lines.Length) return;
            int start = editor.GetFirstCharIndexFromLine(target - 1);
            editor.Focus();
            editor.Select(start, editor.Lines[target - 1].Length);
            editor.ScrollToCaret();
        }

        /// <summary>Help > How it works: the help text in a window where paragraphs flow.</summary>
        void ShowHelp() { UiCommon.ShowHelp(this, "ZX81 BASIC Compiler - how it works and what is supported", HelpText); }

        /// <summary>The Help > How it works text. Keep this up to date when features change
        /// (README.md has the same information). In a @"..." string, "" is one quote.
        /// Markup (see UiCommon.ShowHelp): "# " heading, "- " bullet, "term :: meaning",
        /// 4-space indented examples; every other line is a paragraph (the window wraps it).</summary>
        public const string HelpText =
@"# What it does
The listing is a plain text file, one numbered ZX81 BASIC line per text line, for example:
    10 PRINT AT 0,0;""\00\00\2d\85\17\17\17\2b\2e\33\29""
The compiler turns it into a .p file with the machine code in line 0 (a REM) and line 10 RAND USR 16514, which runs it. By default the program starts by itself after loading. It needs a ZX81 with 16K RAM, or a 32K or 64K RAM pack (see below).
You can also open a program or tape (.p, .81, .p81, a ZX80 .o or .80, a .wav recording or a .tzx image). Its BASIC opens as a new listing: for a program made by this compiler, the BASIC rebuilt from its machine code; otherwise the BASIC as stored.

# Special characters in strings
\hh :: Any ZX81 character code in hex, e.g. \80 (black square), \85, \17 (*), \00 (space). Keyword codes (\C1 and up) print as the keyword text; \C0 is the """" quote image.
\'' \.. \:: :: Block graphics, zmakebas/ZXText2P style: two characters for the left and right halves; ' is the top, . the bottom and : both.
%A :: An inverse character (ZXText2P style).
"""" :: A quote character inside a string.
▘▝▀▖▌▞▛▒█ :: Unicode block characters (▘▝▀▖▌▞▛▒█▟▙▄▜▐▚▗) are accepted too.

# Statements
LET (optional), PRINT (with ; , AT y,x and TAB n), IF ... THEN followed by a statement or a line number, GOTO / GO TO, GOSUB / GO SUB, RETURN, FOR ... TO ... STEP, NEXT, DIM, INPUT, POKE, PLOT, UNPLOT, SCROLL, CLS, PAUSE, RAND, STOP, CLEAR, RUN, FAST, SLOW and REM.
- GOTO and GOSUB can go to a calculated line number, e.g. GOTO 100*N.
- LPRINT (with ; , AT and TAB) and COPY use the ZX printer through the ROM.
- SAVE ""name"" saves the program to tape and then carries on. LOAD ""name"" (or LOAD """") loads another program, which then runs if it auto-runs.
- NEW wipes the program, as on the ZX81.
- LIST, LLIST and CONT do nothing in a compiled program (with a warning): there is no BASIC to list and nothing to continue.

# Functions and operators
ABS SGN INT SQR PEEK USR CODE LEN VAL CHR$ STR$ INKEY$ RND PI SIN COS TAN ASN ACS ATN LN EXP; NOT AND OR; comparisons = <> < > <= >= on numbers and strings; + - * / **.
String slicing works: A$(2), A$(2 TO 5), A$( TO 3), A$(4 TO ).

# Whole numbers and floating point
Numbers are normally whole numbers from -32768 to 32767, which is what makes compiled programs fast.
An expression that uses SIN COS TAN ASN ACS ATN LN EXP SQR PI VAL, RND on its own, or a number with a decimal point (0.5, 1.0) is worked out in floating point by the ZX81 ROM's own calculator, exactly as the ZX81 would, e.g.
    PLOT 32+20*COS (A*PI/180),22+20*SIN (A*PI/180)
- Where the result is used as a number (stored in a variable, used by PLOT or AT, a subscript ...) it is rounded to the nearest whole number, as the ZX81 does when it needs a whole number.
- PRINT and STR$ show the full value: PRINT SIN 1 shows 0.84147099.
- Floating point is as slow as the ZX81 itself, so keep it out of the busiest loops.
- Errors in floating point (SQR -1, LN 0 ...) stop the program with the ZX81 report code, e.g. A/10.
- VAL works out any expression in its string, but can't see the program's variables.

# Strict integer mode
Put #STRICT_INT on a line of its own (or tick Strict integer, or use --strict-int on the command line) to forbid floating point. Every expression that would need the ROM calculator, and every number that isn't whole, is then a compile error, so the program is guaranteed to be all fast code.

# Compile & Run
Build > Compile & Run (Ctrl+F5, or --run on the command line) saves the .p file and starts an emulator with it. Set the emulator up with Options > Configure Emulator...: the program to run and its arguments, where {file} is replaced by the .p file. The settings are kept in ZX81Toolset.ini next to the programs (or in %APPDATA%\ZX81Toolset).

# Sizes
When a listing is opened, a trial compile shows the expected size of the machine code and variables, and whether it fits the ZX81's RAM (and if not, whether a 32K or 64K RAM pack would). After compiling, the sizes are shown in bytes and K.

# RAM packs: 16K, 32K and 64K
Choose the RAM the program is for in the RAM box (or --ram 16|32|64).
16K :: The default: everything lives below 32K (8000h).
32K / 64K :: The variables, arrays and strings go above 32K: 8000h-BFFFh (16K of room) with a 32K pack, 8000h-FFFFh (32K of room) with a 64K pack.
The machine code and the screen stay below 32K, because a ZX81 can only run code and show the display there, so the code itself can't grow beyond about 15K. RAMTOP can be left alone, or raised (the variables then sit below the stack). When the program starts it checks that the RAM is really there, and shows NEEDS 32K RAM (or 64K) if not.

# Differences from ZX81 BASIC
- Outside floating point, maths is whole-number: 7/2 = 3 and 2**-1 = 0. Write 7/2.0 (or 7/2*1.0) to get 3.5.
- Variables hold whole numbers, so LET X=2.5 stores 3.
- RND*n gives a fast whole number from 0 to n-1, so the usual INT (RND*6)+1 works as expected. RND on its own is 0 to 0.99998.
- Variables start at 0 (no 'variable not found' error).
- DIM sizes must be constant numbers. Arrays are not bounds checked.
- Ordinary string variables hold up to 'Max string length' characters (default 255). DIM A$(n) gives a fixed-length string and DIM A$(n,m) an array of n strings of m characters, as on the ZX81.
- Printing past the bottom of the screen scrolls instead of stopping with error 5.
- Only one statement per line, as on the ZX81.
- INPUT accepts a number or text typed on the bottom line; NEWLINE ends it and RUBOUT (SHIFT 0) deletes.
";
    }
}
