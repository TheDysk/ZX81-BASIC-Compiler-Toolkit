// =====================================================================================
//  Cli.cs - the command-line front end:  zx81c input.txt [output.p] [options]
// -------------------------------------------------------------------------------------
//  Builds zx81c.exe. The Windows GUI (MainForm.cs) calls the same Compiler.Compile,
//  so the two always behave the same. ReadListing is shared by both.
//
//  Exit codes: 0 = success, 1 = the program had errors, 2 = no input file given or it
//  couldn't be read, 3 = compiled but the emulator (--run) couldn't be started.
//  The input can be a listing, or a program or tape (.p .81 .p81 .o .80 .wav .tzx), which
//  is turned into BASIC by the decompiler first.
// =====================================================================================
using System;
using System.IO;
using System.Linq;
using System.Text;

using System.Reflection;

// Version and description stored in the .exe (Windows: Properties > Details). Keep the
// version in step with Versions.cs.
[assembly: AssemblyTitle("ZX81 BASIC Compiler")]
[assembly: AssemblyDescription("Compiles ZX81 BASIC listings to Z80 machine code .p files")]
[assembly: AssemblyProduct("ZX81 BASIC Toolset")]
[assembly: AssemblyCopyright("Copyright (c) Simon Round 2026")]
[assembly: AssemblyVersion("0.9.0.0")]
[assembly: AssemblyFileVersion("0.9.0.0")]

namespace ZX81Compiler
{
    public static class Cli
    {
        /// <summary>zx81c --? (also -?, /?, -h, --help): the options, one line each.</summary>
        public static string HelpText
        {
            get
            {
                return "zx81c - ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler) + "\n" +
@"Usage: zx81c input [output.p] [options]

  input            a BASIC listing (.txt .bas .b81), or a program or tape (.p .81 .p81
                   .o .80 .wav .tzx), whose BASIC is compiled (for a program made by
                   this compiler, the BASIC rebuilt from its machine code)
  output.p         the .p file to write (default: the input's name with .p)

Numbers (also chosen by a #FULL_FLOAT, #FLOAT_ISLANDS or #STRICT_INT line in the listing):
  --float full     full floating point, as the ZX81 (the default): every number is a
                   5-byte floating-point number, 7/2 is 3.5, LET X=2.5 keeps 2.5
  --float islands  float islands: fast whole numbers (-32768..32767); only expressions
                   with SIN, PI, 0.5, VAL ... use floating point, rounded when stored
  --float int      integer only: floating point is a compile error
  --strict-int     the same as --float int

Labels (also switched on by a #LABELS line, or any @name: in the listing):
  --labels         lines may leave out their numbers (numbered 10, 20 ... around any
                   explicit ones), and @name: labels a line for GOTO @name, GO SUB
                   @name, IF ... THEN @name, RUN @name ...

Other options:
  --ram 16|32|64   RAM the program is for: 16K (default), or a 32K or 64K RAM pack
  --no-autorun     don't start the program by itself after LOAD
  --no-break       don't check for BREAK (SPACE) at GOTO and NEXT (a little faster)
  --lower-inverse  lower-case letters in strings become inverse video
  --maxstr N       capacity of ordinary string variables, 1-255 (default 255)
  --asm file       also write the generated Z80 assembly listing
  --run            start the emulator set up in " + ToolConfig.FileName + @" with the new .p file
  --emulator path  start this emulator instead (for this run only; implies --run)
  --version        show the version
  --?              show this list (also -?, /?, -h, --help)

Exit codes: 0 done, 1 the program has errors, 2 bad arguments or the input can't be
read, 3 compiled but the emulator couldn't be started.
";
            }
        }

        /// <summary>Run the compiler with command-line arguments, writing messages to outw.</summary>
        public static int Run(string[] args, TextWriter outw)
        {
            string input = null, output = null, asmFile = null, emulator = null;
            bool run = false;
            var opt = new Options();
            // Options can appear anywhere; the first other argument is the input file and
            // the second the output file.
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a.ToLowerInvariant())
                {
                    case "--no-autorun": opt.Autorun = false; break;
                    case "--no-break": opt.BreakCheck = false; break;
                    case "--lower-inverse": opt.LowerIsInverse = true; break;
                    case "--maxstr": opt.MaxString = Math.Max(1, Math.Min(255, int.Parse(args[++i]))); break;   // clamp to 1..255
                    case "--ram":
                        opt.Ram = int.Parse(args[++i].TrimEnd(new[] { 'K', 'k' }));
                        if (opt.Ram != 16 && opt.Ram != 32 && opt.Ram != 64) { outw.WriteLine("--ram must be 16, 32 or 64"); return 1; }
                        break;
                    case "--asm": asmFile = args[++i]; break;
                    case "--float":                                            // how numbers are worked out
                        {
                            NumberMode m;
                            string w = i + 1 < args.Length ? args[++i] : "";
                            if (!NumberModes.TryParse(w, out m)) { outw.WriteLine("--float must be full, islands or int (not '" + w + "'). Use --? for help."); return 2; }
                            opt.Numbers = m;
                            break;
                        }
                    case "--strict-int": opt.Numbers = NumberMode.IntegerOnly; break;   // the same as --float int
                    case "--labels": opt.Labels = true; break;                  // like #LABELS in the listing
                    case "--run": run = true; break;                           // start the emulator afterwards
                    case "--emulator": emulator = args[++i]; run = true; break; // ... this one (not saved)
                    case "--version":
                        outw.WriteLine("zx81c - ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler));
                        return 0;
                    case "--?":
                    case "-?":
                    case "/?":
                    case "-h":
                    case "--help":
                        outw.Write(HelpText);
                        return 0;
                    default:
                        if (input == null) input = a; else output = a;
                        break;
                }
            }
            if (input == null) { outw.WriteLine("No input file. Use --? for a list of the options."); return 2; }
            if (output == null) output = Path.ChangeExtension(input, ".p");   // default: same name, .p
            string text;
            try { text = ReadListing(input); }
            catch (TapeDecodeException e) { outw.WriteLine("Could not read the tape in " + input + ": " + e.Message); return 2; }
            catch (Exception e) { outw.WriteLine("Could not read " + input + ": " + e.Message); return 2; }
            var res = Compiler.Compile(text, opt);
            foreach (var d in res.Diagnostics) outw.WriteLine(d);
            if (!res.Success)
            {
                // out of memory: still show the sizes, to help decide what to cut
                if (res.MemoryEnd != 0) outw.WriteLine(Compiler.SizeLines(res));
                outw.WriteLine("Compilation failed.");
                return 1;
            }
            File.WriteAllBytes(output, res.PFile);
            if (asmFile != null && res.Listing != null) File.WriteAllText(asmFile, res.Listing + res.Symbols);
            outw.WriteLine(res.Summary);
            outw.WriteLine("Written " + output);
            if (run)
            {
                // start the emulator with the new file
                var cfg = ToolConfig.Load();
                if (emulator != null) cfg.EmulatorPath = emulator;
                try
                {
                    cfg.RunInEmulator(output);
                    outw.WriteLine("Started " + cfg.EmulatorPath + " " + cfg.ArgumentsFor(output));
                }
                catch (InvalidOperationException e)
                {
                    outw.WriteLine(e.Message.Replace("Options > Configure Emulator", "the compiler's Options > Configure Emulator, or --emulator path"));
                    return 3;   // compiled, but couldn't run it
                }
            }
            return 0;
        }

        /// <summary>Read a listing file. Tries UTF-8 first (removing the byte order mark that
        /// Notepad may add); if the file isn't valid UTF-8 it is read as Windows-1252, which
        /// covers older files with a £ sign saved by other editors.</summary>
        public static string ReadListing(string path)
        {
            // a program or tape (.p .p81 .o .wav .tzx ...): its BASIC, through the decompiler
            // (rebuilt BASIC for programs made by this compiler). Tape errors carry [mm:ss.ms].
            if (TapeFormats.IsProgramFile(path))
            {
                bool rebuilt;
                return Decompiler.ListingForCompiler(TapeFormats.Load(path), Path.GetFileName(path), out rebuilt);
            }
            byte[] b = File.ReadAllBytes(path);
            try
            {
                // new[] { ... } rather than a single char: .NET Framework has no TrimStart(char)
                return new UTF8Encoding(false, true).GetString(b).TrimStart(new[] { '\uFEFF' });
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(1252).GetString(b);
            }
        }
    }

    /// <summary>Entry point of zx81c.exe.</summary>
    public static class CliMain
    {
        public static int Main(string[] args) { return Cli.Run(args, Console.Out); }
    }
}
