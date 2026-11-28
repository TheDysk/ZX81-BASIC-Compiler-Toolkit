// =====================================================================================
//  DecompilerCli.cs - command-line decompiler (zx81dc.exe)
// -------------------------------------------------------------------------------------
//  zx81dc program.p [options]
//    The input can also be a tape or another format: .wav (a recording of a ZX81 tape),
//    .tzx, .p81, or a ZX80 program (.o / .80, converted to ZX81 BASIC).
//    --txt  file   BASIC listing exactly as stored (default: program.txt)
//    --basic file  BASIC rebuilt from machine code, for programs made by the
//                  ZX81 BASIC compiler (default: program.bas when available)
//    --asm  file   machine code as an assembly listing (default: program.asm when found)
//    --p    file   a .p file made from the decompiled text (default: not made)
//    --p-from listing|basic   which text the .p is made from (default: basic if
//                  rebuilt, else listing)
//    --extract file  write the program as read from the input (e.g. a .wav) as a .p file
//    --ctl file    control file (LABEL / COMMENT / CODE / DATA, see ControlFile.cs);
//                  default: program.ctl next to the input, if there is one
//    --report      print the analysis report
//  Exit codes: 0 = done, 1 = could not read the file (a tape that can't be decoded is
//  reported with the [mm:ss.ms] time where it went wrong), 2 = bad arguments.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using System.Reflection;

// Version and description stored in the .exe (Windows: Properties > Details). Keep the
// version in step with Versions.cs.
[assembly: AssemblyTitle("ZX81 Decompiler (command line)")]
[assembly: AssemblyDescription("Turns ZX81 .p files back into BASIC listings and Z80 assembly")]
[assembly: AssemblyProduct("ZX81 BASIC Toolset")]
[assembly: AssemblyCopyright("Copyright (c) Simon Round 2026")]
[assembly: AssemblyVersion("0.9.0.0")]
[assembly: AssemblyFileVersion("0.9.0.0")]

namespace ZX81Compiler
{
    public static class DecompilerCli
    {
        public static int Main(string[] args)
        {
            string input = null, ctlFile = null, extract = null, txt = null, basic = null, asm = null, pout = null, pFrom = null;
            bool report = false;
            var audio = new AudioOptions();
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--txt": txt = args[++i]; break;
                        case "--basic": basic = args[++i]; break;
                        case "--asm": asm = args[++i]; break;
                        case "--p": pout = args[++i]; break;
                        case "--p-from": pFrom = args[++i].ToLowerInvariant(); break;
                        case "--report": report = true; break;
                        case "--extract": extract = args[++i]; break;
                        case "--ctl": ctlFile = args[++i]; break;
                        case "--wav-threshold": audio.Threshold = Percent(args[++i]); break;
                        case "--wav-hysteresis": audio.Hysteresis = Percent(args[++i]); break;
                        case "--wav-invert": audio.Invert = true; break;
                        case "-h": case "--help": case "/?": case "-?": case "--?": Usage(); return 0;
                        case "--version": Console.WriteLine("zx81dc - ZX81 Decompiler " + Versions.Show(Versions.Decompiler)); return 0;
                        default:
                            if (input != null) { Console.WriteLine("Unexpected argument: " + args[i]); return 2; }
                            input = args[i]; break;
                    }
                }
            }
            catch (IndexOutOfRangeException) { Console.WriteLine("An option is missing its file name."); return 2; }
            catch (FormatException) { Console.WriteLine("--wav-threshold and --wav-hysteresis take a percentage, e.g. --wav-threshold 10"); return 2; }
            if (audio.Problem() != null) { Console.WriteLine("Audio options: " + audio.Problem() + "."); return 2; }
            if (input == null) { Console.WriteLine("No input file. Use --? for a list of the options."); return 2; }

            PFile p;
            LoadedProgram lp;
            try { lp = TapeFormats.Load(input, audio); p = PFile.Load(lp.P); }
            catch (TapeDecodeException e) { Console.WriteLine("Could not read the tape in " + input + ": " + e.Message); return 1; }
            catch (Exception e) { Console.WriteLine("Could not read " + input + ": " + e.Message); return 1; }
            string name = Path.GetFileName(input);
            if (lp.Converted)
            {
                Console.WriteLine("Read " + name + " (" + lp.Format + "):");
                foreach (var n in lp.Notes) Console.WriteLine("  " + n);
            }
            if (extract != null)
            {
                File.WriteAllBytes(extract, lp.P);
                Console.WriteLine("Written " + extract + " (the program as read, " + lp.P.Length + " bytes)");
            }
            // the control file: --ctl, or program.ctl next to the input
            ControlFile ctl = null;
            try
            {
                if (ctlFile != null) ctl = ControlFile.Load(ctlFile);
                else { ctl = ControlFile.LoadSidecar(input); if (ctl != null) ctlFile = ControlFile.SidecarPath(input); }
            }
            catch (Exception e) { Console.WriteLine("Could not read the control file " + (ctlFile ?? ControlFile.SidecarPath(input)) + ": " + e.Message); return 1; }
            if (ctl != null)
            {
                Console.WriteLine("Control file " + ctlFile + ": " + ctl.Summary(false));
                foreach (var pr in ctl.Problems) Console.WriteLine("  Warning: " + pr);
            }
            var r = Decompiler.Run(p, name, ctl);
            if (lp.Converted) r.Report = "Opened from " + name + " (" + lp.Format + "):\n  " + string.Join("\n  ", lp.Notes) + "\n\n" + r.Report;
            string stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input)), Path.GetFileNameWithoutExtension(input));

            if (txt == null) txt = stem + ".txt";
            Write(txt, r.Listing, "listing");
            if (r.Basic != null) Write(basic ?? stem + ".bas", r.Basic, "rebuilt BASIC");
            else if (basic != null) Console.WriteLine("No BASIC to rebuild: the program wasn't made by the ZX81 BASIC compiler.");
            if (r.Asm != null) Write(asm ?? stem + ".asm", r.Asm, "machine code listing");
            else if (asm != null) Console.WriteLine("No machine code found, so no .asm written.");
            if (pout != null)
            {
                bool fromBasic = pFrom == "basic" || (pFrom == null && r.Basic != null);
                if (fromBasic && r.Basic == null) { Console.WriteLine("--p-from basic: there is no rebuilt BASIC for this file."); return 2; }
                var diags = new List<Diagnostic>();
                byte[] pb = r.MakeP(fromBasic ? r.Basic : r.Listing, diags, true, !fromBasic);
                foreach (var d in diags) Console.WriteLine(d);
                if (pb == null) { Console.WriteLine("The .p file was not made."); return 1; }
                File.WriteAllBytes(pout, pb);
                Console.WriteLine("Written " + pout + " (from the " + (fromBasic ? "rebuilt BASIC" : "listing") + ", " + pb.Length + " bytes)");
            }
            if (report) Console.WriteLine(r.Report);
            else
            {
                Console.WriteLine(p.Lines.Count + " BASIC lines. " + p.AutoRunDescription() + ".");
                foreach (var n in r.Code.Notes) Console.WriteLine("  " + n);
                foreach (var n in r.Recovered.Notes) Console.WriteLine("  " + n);
            }
            return 0;
        }

        static void Write(string path, string text, string what)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Console.WriteLine("Written " + path + " (" + what + ")");
        }

        /// <summary>"10" or "10%" -> 0.10</summary>
        static double Percent(string s)
        {
            return double.Parse(s.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) / 100.0;
        }

        static void Usage()
        {
            Console.WriteLine("zx81dc - ZX81 Decompiler " + Versions.Show(Versions.Decompiler));
            Console.Write(
@"Usage: zx81dc input [options]

  input            a .p, .81 or .p81 file, a tape (.wav recording or .tzx image), or a
                   ZX80 program (.o, .80), which is converted to ZX81 BASIC

Output files (each defaults to the input's name with that extension):
  --txt file       the BASIC listing exactly as stored (always written)
  --basic file     for a program made by the ZX81 BASIC Compiler: the BASIC rebuilt from
                   its machine code. It starts with #FULL_FLOAT or #FLOAT_ISLANDS: how
                   the program was compiled to work out numbers, found from its code
                   (full floating point or float islands), so it compiles the same way
  --asm file       the machine code as a Z80 assembly listing (when there is any)
  --p file         also make a .p file from the text (not made unless asked)
  --p-from listing|basic   which text --p uses (default: basic if rebuilt, else listing)
  --extract file   write the program exactly as read (e.g. from a .wav) as a .p file

Control file (.ctl): program.ctl next to the input is used automatically, or:
  --ctl file       use this control file. One directive per line (; starts a comment):
                     LABEL addr name       name an address in the .asm (instead of Lxxxx)
                     COMMENT addr text     a comment line above that address
                     CODE start [end]      disassemble from start (and decode all of
                                           start..end): code that no USR reaches
                     DATA start length     bytes that are data, never code
                   Addresses: decimal (16514) or hex (4082h, 0x4082, $4082)

Reading .wav recordings that don't decode with the standard settings:
  --wav-threshold N   noise threshold, in % of the loudest sound (default 20): quieter
                      than this counts as silence. Lower it if the recording fades or
                      has a loud click; raise it if hiss is read as pulses
  --wav-hysteresis N  how far (in % of the signal) a level must swing to count
                      (default 15). Raise it for ripples on the pulses, lower it for
                      weak, rounded pulses
  --wav-invert        turn the recording upside down before reading it

Other options:
  --report         print the analysis report (includes how numbers are worked out)
  --version        show the version
  --?              show this list (also -?, /?, -h, --help)

The number mode needs no option: the decompiler reads it from the program.
Exit codes: 0 done, 1 the input can't be read (a tape that can't be decoded is reported
with the [mm:ss.ms] time where it went wrong), 2 bad arguments.
");
        }
    }
}
