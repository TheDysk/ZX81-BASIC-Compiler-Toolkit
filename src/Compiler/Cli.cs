// =====================================================================================
//  Cli.cs - the command-line front end:  zx81c input.txt [output.p] [options]
// -------------------------------------------------------------------------------------
//  Builds zx81c.exe. The Windows GUI (MainForm.cs) calls the same Compiler.Compile,
//  so the two always behave the same. ReadListing is shared by both.
//
//  Exit codes: 0 = success, 1 = the program had errors, 2 = no input file given.
// =====================================================================================
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ZX81Compiler
{
    public static class Cli
    {
        /// <summary>Run the compiler with command-line arguments, writing messages to outw.</summary>
        public static int Run(string[] args, TextWriter outw)
        {
            string input = null, output = null, asmFile = null;
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
                    case "--asm": asmFile = args[++i]; break;
                    case "-h":
                    case "--help":
                    case "/?":
                        outw.WriteLine("Usage: zx81c input.txt [output.p] [--no-autorun] [--no-break] [--lower-inverse] [--maxstr N] [--asm listing.asm]");
                        return 0;
                    default:
                        if (input == null) input = a; else output = a;
                        break;
                }
            }
            if (input == null) { outw.WriteLine("No input file. Use --help for usage."); return 2; }
            if (output == null) output = Path.ChangeExtension(input, ".p");   // default: same name, .p
            string text = ReadListing(input);
            var res = Compiler.Compile(text, opt);
            foreach (var d in res.Diagnostics) outw.WriteLine(d);
            if (!res.Success) { outw.WriteLine("Compilation failed."); return 1; }
            File.WriteAllBytes(output, res.PFile);
            if (asmFile != null && res.Listing != null) File.WriteAllText(asmFile, res.Listing + res.Symbols);
            outw.WriteLine(res.Summary);
            outw.WriteLine("Written " + output);
            return 0;
        }

        /// <summary>Read a listing file. Tries UTF-8 first (removing the byte order mark that
        /// Notepad may add); if the file isn't valid UTF-8 it is read as Windows-1252, which
        /// covers older files with a £ sign saved by other editors.</summary>
        public static string ReadListing(string path)
        {
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
