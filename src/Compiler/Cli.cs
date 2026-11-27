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
[assembly: AssemblyVersion("1.00.0.0")]
[assembly: AssemblyFileVersion("1.00.0.0")]

namespace ZX81Compiler
{
    public static class Cli
    {
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
                    case "--strict-int": opt.StrictInt = true; break;          // like #STRICT_INT in the listing
                    case "--run": run = true; break;                           // start the emulator afterwards
                    case "--emulator": emulator = args[++i]; run = true; break; // ... this one (not saved)
                    case "--version":
                        outw.WriteLine("zx81c - ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler));
                        return 0;
                    case "-h":
                    case "--help":
                    case "/?":
                        outw.WriteLine("zx81c - ZX81 BASIC Compiler " + Versions.Show(Versions.Compiler));
                        outw.WriteLine("Usage: zx81c input.txt [output.p] [--no-autorun] [--no-break] [--lower-inverse] [--maxstr N] [--ram 16|32|64] [--strict-int] [--asm listing.asm] [--run] [--emulator path]");
                        outw.WriteLine("  input.txt can also be a program or tape (.p .81 .p81 .o .80 .wav .tzx): its BASIC is");
                        outw.WriteLine("  compiled (for a program made by this compiler, the BASIC rebuilt from its code).");
                        outw.WriteLine("  --strict-int   no floating point: SIN, PI, 0.5, VAL ... are errors (same as a #STRICT_INT line)");
                        outw.WriteLine("  --run          start the emulator set up in " + ToolConfig.FileName + " with the new .p file");
                        outw.WriteLine("  --emulator P   start emulator P instead (for this run only)");
                        return 0;
                    default:
                        if (input == null) input = a; else output = a;
                        break;
                }
            }
            if (input == null) { outw.WriteLine("No input file. Use --help for usage."); return 2; }
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
