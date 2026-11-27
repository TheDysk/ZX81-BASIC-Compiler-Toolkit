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
        readonly ToolStripStatusLabel status;                  // status bar text
        // ---- state ----
        string listingPath;   // file the listing was loaded from / saved to (null = never saved)
        string outputPath;    // where the last .p was saved (F5 saves there again without asking)
        bool dirty;           // listing changed since it was loaded or saved

        /// <summary>Build the window and its controls.</summary>
        public MainForm()
        {
            Text = "ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler);
            Width = 1000; Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;    // files can be dragged onto the window
            Font = new Font("Segoe UI", 9f);

            // ---- menu ----
            // Each item: text (& marks the Alt-key letter), no icon, what to do, shortcut key.
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&New", null, (s, e) => NewListing(), Keys.Control | Keys.N));
            file.DropDownItems.Add(new ToolStripMenuItem("&Open listing...", null, (s, e) => OpenListing(), Keys.Control | Keys.O));
            file.DropDownItems.Add(new ToolStripMenuItem("&Save listing", null, (s, e) => SaveListing(false), Keys.Control | Keys.S));
            file.DropDownItems.Add(new ToolStripMenuItem("Save listing &as...", null, (s, e) => SaveListing(true)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (s, e) => Close()));
            var build = new ToolStripMenuItem("&Build");
            build.DropDownItems.Add(new ToolStripMenuItem("&Compile to .p", null, (s, e) => CompileNow(false), Keys.F5));
            build.DropDownItems.Add(new ToolStripMenuItem("Compile to .p &as...", null, (s, e) => CompileNow(true), Keys.Shift | Keys.F5));
            build.DropDownItems.Add(new ToolStripMenuItem("Check &syntax only", null, (s, e) => CheckOnly(), Keys.F7));
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&Supported BASIC...", null, (s, e) => ShowHelp()));
            help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (s, e) => MessageBox.Show(this,
                "ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler) + "\n\nCompiles a ZX81 BASIC text listing into Z80 machine code held in line 0 (REM), with line 10 RAND USR 16514, saved as a .p file.\n\nTarget: ZX81 with 16K RAM.\n\nCopyright (c) Simon Round 2026\nEmail: " + ContactEmail(), "About")));
            menu.Items.Add(file); menu.Items.Add(build); menu.Items.Add(help);

            // ---- toolbar ----
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(new ToolStripButton("Open listing...", null, (s, e) => OpenListing()));
            tools.Items.Add(new ToolStripButton("Save listing", null, (s, e) => SaveListing(false)));
            tools.Items.Add(new ToolStripSeparator());
            var compileBtn = new ToolStripButton("Compile to .p  (F5)", null, (s, e) => CompileNow(false)) { Font = new Font(Font, FontStyle.Bold) };
            tools.Items.Add(compileBtn);
            tools.Items.Add(new ToolStripButton("Compile as...", null, (s, e) => CompileNow(true)));
            tools.Items.Add(new ToolStripButton("Check (F7)", null, (s, e) => CheckOnly()));

            // ---- options panel ----
            // A FlowLayoutPanel lays its controls out left to right.
            var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
            chkAutorun = new CheckBox { Text = "Auto-run after LOAD", Checked = true, AutoSize = true };
            chkBreak = new CheckBox { Text = "BREAK (SPACE) stops program", Checked = true, AutoSize = true };
            chkLower = new CheckBox { Text = "Lower case in strings = inverse", Checked = false, AutoSize = true };
            chkAsm = new CheckBox { Text = "Also save .asm listing", Checked = false, AutoSize = true };
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
            tips.SetToolTip(numMaxStr, "Room reserved for each ordinary string variable (A$..Z$). Lower it if you run out of memory.");
            tips.SetToolTip(cbRam, "The RAM pack the program is for. With 32K or 64K the variables go above 32K (8000h), giving 16K or 32K for variables, arrays and strings. The code itself must still fit below 32K.");
            foreach (Control c in new Control[] { chkAutorun, chkBreak, chkLower, chkAsm, lbl, numMaxStr, lblRam, cbRam })
            {
                c.Margin = new Padding(4, 0, 12, 0);
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

        // Built from pieces so the full address doesn't appear as plain text in the source (spam bots scan GitHub)
        static string ContactEmail()
        {
            return string.Join("", new[] { "thedysk", "\u0040", "gmail", ".", "com" });
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
            using (var d = new OpenFileDialog { Filter = "ZX81 BASIC listings (*.txt;*.bas;*.b81)|*.txt;*.bas;*.b81|All files (*.*)|*.*", Title = "Open ZX81 BASIC listing" })
                if (d.ShowDialog(this) == DialogResult.OK) LoadListing(d.FileName);
        }

        /// <summary>Load a listing file into the editor.</summary>
        public void LoadListing(string path)
        {
            try
            {
                string text = Cli.ReadListing(path);
                // A Windows TextBox needs \r\n line endings; files may have just \n
                editor.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                listingPath = path;
                outputPath = null;
                dirty = false;
                messages.Items.Clear();
                status.Text = "Loaded " + path;
                UpdateTitle();
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
                Ram = 16 << cbRam.SelectedIndex          // 16, 32, 64
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
            if (res.Success) { messages.Items.Add(res.Summary); status.Text = "No errors."; }
            else status.Text = "Errors found - see messages.";
        }

        /// <summary>Build > Compile to .p. Asks for a file name the first time (or always
        /// when askName is true), then saves there on later compiles.</summary>
        void CompileNow(bool askName)
        {
            var res = RunCompiler();
            ShowDiagnostics(res);
            if (!res.Success)
            {
                status.Text = "Compilation failed - see messages.";
                if (messages.Items.Count > 0) messages.SelectedIndex = 0;
                return;
            }
            if (askName || outputPath == null)
            {
                using (var d = new SaveFileDialog { Filter = "ZX81 program (*.p)|*.p|All files (*.*)|*.*", Title = "Save compiled program", DefaultExt = "p" })
                {
                    // suggest the last output name, or the listing's name with .p
                    if (outputPath != null) { d.InitialDirectory = Path.GetDirectoryName(outputPath); d.FileName = Path.GetFileName(outputPath); }
                    else if (listingPath != null) { d.InitialDirectory = Path.GetDirectoryName(listingPath); d.FileName = Path.GetFileNameWithoutExtension(listingPath) + ".p"; }
                    else d.FileName = "program.p";
                    if (d.ShowDialog(this) != DialogResult.OK) { status.Text = "Compiled OK but not saved."; return; }
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
                return;
            }
            messages.Items.Add(res.Summary);
            messages.Items.Add("Saved " + outputPath);
            status.Text = "Saved " + outputPath + "  (" + res.PFile.Length + " bytes)";
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

        /// <summary>Help > Supported BASIC: shows HelpText in a read-only window.</summary>
        void ShowHelp()
        {
            var f = new Form { Text = "Supported BASIC", Width = 760, Height = 640, StartPosition = FormStartPosition.CenterParent };
            var t = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = HelpText.Replace("\n", "\r\n"), Font = new Font(FontFamily.GenericMonospace, 9.5f) };
            try { t.Font = new Font("Consolas", 9.5f); } catch { }
            f.Controls.Add(t);
            f.ShowDialog(this);
        }

        /// <summary>The Help > Supported BASIC text. Keep this up to date when features
        /// change (README.md has the same information). In a @"..." string, "" is one quote.</summary>
        public const string HelpText =
@"ZX81 BASIC COMPILER - WHAT IS SUPPORTED

The listing is a plain text file, one numbered ZX81 BASIC line per line:
    10 PRINT AT 0,0;""\00\00\2d\85\17\17\17\2b\2e\33\29""
Output: a .p file with the machine code in line 0 (REM) and
line 10 RAND USR 16514, which runs it. Needs a ZX81 with 16K RAM.

SPECIAL CHARACTERS IN STRINGS
  \hh      any ZX81 character code in hex, e.g. \80 (black square),
           \85, \17 (*), \00 (space). Keyword codes (\C1 and up) are
           printed as the keyword text; \C0 is the "" quote image.
  \'' \.. \:: etc.  block graphics, zmakebas/ZXText2P style
           (2 chars: left half, right half; ' top, . bottom, : both)
  %A       inverse character (ZXText2P style)
  """"       a quote character inside a string
  Unicode block characters (▘▝▀▖▌▞▛▒█▟▙▄▜▐▚▗) also work.

STATEMENTS
  LET (optional), PRINT (with ; , AT y,x  TAB n), IF..THEN statement
  or line number, GOTO / GO TO, GOSUB / GO SUB, RETURN, FOR..TO..STEP,
  NEXT, DIM, INPUT, POKE, PLOT, UNPLOT, SCROLL, CLS, PAUSE, RAND,
  STOP, CLEAR, RUN, FAST, SLOW, REM.
  GOTO/GOSUB to a calculated line number (GOTO 100*N) works.
  LPRINT (with ; , AT TAB) and COPY use the ZX printer through the ROM.
  SAVE ""name"" saves the program to tape and carries on; LOAD ""name""
  (or LOAD """") loads another program, which then runs if it auto-runs.
  NEW wipes the program, as on the ZX81.
  LIST, LLIST and CONT do nothing in a compiled program (warning).

FUNCTIONS
  ABS SGN INT SQR PEEK USR CODE LEN VAL CHR$ STR$ INKEY$ RND PI
  SIN COS TAN ASN ACS ATN LN EXP
  NOT AND OR, = <> < > <= >= (numbers and strings), + - * / **
  String slicing: A$(2), A$(2 TO 5), A$( TO 3), A$(4 TO ).

WHOLE NUMBERS AND FLOATING POINT
  Numbers are normally whole numbers from -32768 to 32767, which is
  what makes compiled programs fast. An expression that uses SIN COS
  TAN ASN ACS ATN LN EXP SQR PI VAL, RND on its own, or a number with
  a decimal point (0.5, 1.0) is worked out in floating point by the
  ZX81 ROM's own calculator, exactly as the ZX81 would, e.g.
      PLOT 32+20*COS (A*PI/180),22+20*SIN (A*PI/180)
  Where the result is used as a number - stored in a variable, used by
  PLOT, AT, a subscript... - it is rounded to the nearest whole number,
  as the ZX81 does when it needs a whole number. PRINT and STR$ show
  the full value (PRINT SIN 1 shows 0.84147099). This is slower, like
  the ZX81 itself, so keep it out of the busiest loops.
  Errors in floating point (SQR -1, LN 0, ...) stop the program with
  the ZX81 report code, e.g. A/10. VAL works out any expression in its
  string, but can't see the program's variables.

RAM PACKS: 16K, 32K AND 64K
  Choose the RAM the program is for (RAM box, or --ram 16|32|64).
  16K (the default): everything lives below 32K (8000h).
  32K / 64K: the variables, arrays and strings go above 32K: 8000h-BFFFh
  (16K of room) with a 32K pack, 8000h-FFFFh (32K of room) with a 64K
  pack. The machine code and the screen stay below 32K, because a ZX81
  can only run code and show the display there, so the code itself can't
  grow beyond about 15K. RAMTOP can be left alone, or raised (the
  variables then sit below the stack). When the program starts it checks
  that the RAM is really there and shows NEEDS 32K RAM (or 64K) if not.

DIFFERENCES FROM ZX81 BASIC
  * Outside floating point, maths is whole-number: 7/2 = 3, 2**-1 = 0.
    Write 7/2.0 (or 7/2*1.0) to get 3.5.
  * Variables hold whole numbers, so LET X=2.5 stores 3.
  * RND*n gives a fast whole number from 0 to n-1, so the usual
    INT (RND*6)+1 works as expected. RND on its own is 0 to 0.99998.
  * Variables start at 0 (no 'variable not found' error).
  * DIM sizes must be constant numbers. Arrays are not bounds checked.
  * Ordinary string variables hold up to 'Max string length'
    characters (default 255). DIM A$(n) gives a fixed-length string and
    DIM A$(n,m) an array of n strings of m characters, as on the ZX81.
  * Printing past the bottom of the screen scrolls instead of stopping
    with error 5.
  * Only one statement per line (as on the ZX81).
  * INPUT accepts a number or text typed on the bottom line; NEWLINE
    ends it and RUBOUT (SHIFT 0) deletes.
";
    }
}
