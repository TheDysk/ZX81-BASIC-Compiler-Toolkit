// =====================================================================================
//  ToolConfig.cs - settings shared by the tools (the emulator to run programs in)
// -------------------------------------------------------------------------------------
//  Stored as a small INI file, ZX81Toolset.ini, next to the .exe files, e.g.
//
//      [Emulator]
//      Path=C:\EightyOne\EightyOne.exe
//      Arguments="{file}"
//
//  {file} is replaced by the full path of the .p file to run. If the folder of the .exe
//  files can't be written to (e.g. under Program Files), the settings go to
//  %APPDATA%\ZX81Toolset\ZX81Toolset.ini instead. The ZX81TOOLSET_INI environment
//  variable, if set, names the file to use (the tests use this).
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ZX81Compiler
{
    public class ToolConfig
    {
        /// <summary>The emulator program, e.g. C:\EightyOne\EightyOne.exe ("" = not set up).</summary>
        public string EmulatorPath = "";
        /// <summary>Its command line; {file} becomes the .p file's path.</summary>
        public string EmulatorArgs = DefaultArgs;
        public const string DefaultArgs = "\"{file}\"";
        public const string FileName = "ZX81Toolset.ini";

        /// <summary>Where the settings are read from / written to.</summary>
        public static string ConfigPath()
        {
            string env = Environment.GetEnvironmentVariable("ZX81TOOLSET_INI");
            if (!string.IsNullOrEmpty(env)) return env;
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            if (File.Exists(local)) return local;
            string roaming = AppDataPath();
            if (File.Exists(roaming)) return roaming;
            return local;
        }

        static string AppDataPath()
        {
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZX81Toolset"), FileName);
        }

        /// <summary>Read the settings (defaults if there is no file or it can't be read).</summary>
        public static ToolConfig Load()
        {
            var c = new ToolConfig();
            try
            {
                string path = ConfigPath();
                if (!File.Exists(path)) return c;
                string section = "";
                foreach (string raw in File.ReadAllLines(path))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith(";") || l.StartsWith("#")) continue;
                    if (l.StartsWith("[")) { section = l.Trim(new[] { '[', ']' }).ToLowerInvariant(); continue; }
                    int eq = l.IndexOf('=');
                    if (eq < 0) continue;
                    string key = l.Substring(0, eq).Trim().ToLowerInvariant(), val = l.Substring(eq + 1).Trim();
                    if (section == "emulator" && key == "path") c.EmulatorPath = val;
                    else if (section == "emulator" && key == "arguments") c.EmulatorArgs = val;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return c;
        }

        /// <summary>Write the settings. Returns the file written.</summary>
        public string Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; Settings for the ZX81 BASIC Toolset");
            sb.AppendLine("[Emulator]");
            sb.AppendLine("; the emulator to run .p files in (e.g. EightyOne.exe)");
            sb.AppendLine("Path=" + EmulatorPath);
            sb.AppendLine("; its command line: {file} is replaced by the .p file");
            sb.AppendLine("Arguments=" + EmulatorArgs);
            string path = ConfigPath();
            try { File.WriteAllText(path, sb.ToString()); return path; }
            catch (Exception e)
            {
                if (!(e is UnauthorizedAccessException) && !(e is IOException)) throw;
                if (Environment.GetEnvironmentVariable("ZX81TOOLSET_INI") != null) throw;
                // the program folder isn't writable: use the user's application data folder
                string alt = AppDataPath();
                Directory.CreateDirectory(Path.GetDirectoryName(alt));
                File.WriteAllText(alt, sb.ToString());
                return alt;
            }
        }

        /// <summary>Is an emulator set up (and does the program exist)?</summary>
        public bool EmulatorReady { get { return EmulatorPath.Length > 0 && File.Exists(EmulatorPath); } }

        /// <summary>The command line for running a file.</summary>
        public string ArgumentsFor(string pfile)
        {
            string full = Path.GetFullPath(pfile);
            string a = string.IsNullOrEmpty(EmulatorArgs) ? DefaultArgs : EmulatorArgs;
            return a.Contains("{file}") ? a.Replace("{file}", full) : a + " \"" + full + "\"";
        }

        /// <summary>Start the emulator with a .p file. Throws InvalidOperationException
        /// with a message for the user if no emulator is set up or it can't be started.</summary>
        public Process RunInEmulator(string pfile)
        {
            if (EmulatorPath.Length == 0)
                throw new InvalidOperationException("No emulator is set up. Choose one with Options > Configure Emulator (or edit " + ConfigPath() + ").");
            if (!File.Exists(EmulatorPath))
                throw new InvalidOperationException("The emulator " + EmulatorPath + " can't be found. Choose it again with Options > Configure Emulator.");
            var psi = new ProcessStartInfo(EmulatorPath, ArgumentsFor(pfile))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(EmulatorPath))
            };
            try { return Process.Start(psi); }
            catch (Exception e) { throw new InvalidOperationException("Could not start " + EmulatorPath + ": " + e.Message); }
        }

        /// <summary>Write a program to a temporary .p file (for running something that
        /// isn't saved, or was opened from another format). Returns the path.</summary>
        public static string TempP(byte[] p, string name)
        {
            string dir = Path.Combine(Path.GetTempPath(), "ZX81Toolset");
            Directory.CreateDirectory(dir);
            var safe = new StringBuilder();
            foreach (char ch in name ?? "program") safe.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            if (safe.Length == 0) safe.Append("program");
            string path = Path.Combine(dir, safe + ".p");
            File.WriteAllBytes(path, p);
            return path;
        }
    }
}
