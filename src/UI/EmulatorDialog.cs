// =====================================================================================
//  EmulatorDialog.cs - choosing the emulator, and running a program in it
// -------------------------------------------------------------------------------------
//  Shared by the compiler and the .P Toolkit. The settings are kept by ToolConfig
//  (src/Core/ToolConfig.cs) in ZX81Toolset.ini next to the programs.
//     EmulatorUi.Configure(owner)    the Options > Configure Emulator... dialog
//     EmulatorUi.Run(owner, file)    start the emulator with a .p file (offers to set
//                                    one up first if there isn't one)
// =====================================================================================
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ZX81Compiler
{
    public static class EmulatorUi
    {
        /// <summary>Show the Configure Emulator dialog. Returns true if settings were saved.</summary>
        public static bool Configure(IWin32Window owner)
        {
            var cfg = ToolConfig.Load();
            using (var f = new Form
            {
                Text = "Configure Emulator",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(560, 210),
                Font = new Font("Segoe UI", 9f)
            })
            {
                var lblPath = new Label { Text = "Emulator program (for example EightyOne.exe):", AutoSize = true, Location = new Point(12, 12) };
                var txtPath = new TextBox { Text = cfg.EmulatorPath, Location = new Point(12, 32), Width = 440 };
                var browse = new Button { Text = "Browse...", Location = new Point(460, 30), Width = 88 };
                var lblArgs = new Label { Text = "Command line ({file} is replaced by the .p file):", AutoSize = true, Location = new Point(12, 66) };
                var txtArgs = new TextBox { Text = cfg.EmulatorArgs, Location = new Point(12, 86), Width = 536 };
                var hint = new Label
                {
                    Text = "Usually just \"{file}\" (with the quotes). Add any options your emulator needs, e.g. to choose a ZX81 with 16K.\n" +
                           "Saved in " + ToolConfig.ConfigPath(),
                    Location = new Point(12, 114), Size = new Size(536, 48), ForeColor = SystemColors.GrayText
                };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(380, 172), Width = 80 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(468, 172), Width = 80 };
                browse.Click += (s, e) =>
                {
                    using (var d = new OpenFileDialog { Title = "Choose the emulator", Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*" })
                    {
                        if (txtPath.Text.Length > 0) try { d.InitialDirectory = Path.GetDirectoryName(txtPath.Text); } catch (ArgumentException) { }
                        if (d.ShowDialog(f) == DialogResult.OK) txtPath.Text = d.FileName;
                    }
                };
                f.Controls.AddRange(new Control[] { lblPath, txtPath, browse, lblArgs, txtArgs, hint, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                cfg.EmulatorPath = txtPath.Text.Trim().Trim('"');
                cfg.EmulatorArgs = txtArgs.Text.Trim().Length == 0 ? ToolConfig.DefaultArgs : txtArgs.Text.Trim();
                try { cfg.Save(); }
                catch (Exception ex) { UiCommon.Error(owner, "Could not save the settings:\n" + ex.Message); return false; }
                if (cfg.EmulatorPath.Length > 0 && !File.Exists(cfg.EmulatorPath))
                    MessageBox.Show(owner, "Saved, but " + cfg.EmulatorPath + " doesn't exist.", "Configure Emulator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }
        }

        /// <summary>Run a .p file in the emulator. If none is set up, offers the dialog first.
        /// Returns true if the emulator was started.</summary>
        public static bool Run(IWin32Window owner, string pfile)
        {
            var cfg = ToolConfig.Load();
            if (!cfg.EmulatorReady)
            {
                string why = cfg.EmulatorPath.Length == 0 ? "No emulator is set up yet." : "The emulator " + cfg.EmulatorPath + " can't be found.";
                if (MessageBox.Show(owner, why + "\n\nChoose one now?", "Run in Emulator", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
                if (!Configure(owner)) return false;
                cfg = ToolConfig.Load();
            }
            try { cfg.RunInEmulator(pfile); return true; }
            catch (InvalidOperationException e) { UiCommon.Error(owner, e.Message); return false; }
        }
    }
}
