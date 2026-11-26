// =====================================================================================
//  DecompilerCli.cs - command-line decompiler (zx81dc.exe)
// -------------------------------------------------------------------------------------
//  zx81dc program.p [options]
//    --txt  file   BASIC listing exactly as stored (default: program.txt)
//    --basic file  BASIC rebuilt from machine code, for programs made by the
//                  ZX81 BASIC compiler (default: program.bas when available)
//    --asm  file   machine code as an assembly listing (default: program.asm when found)
//    --p    file   a .p file made from the decompiled text (default: not made)
//    --p-from listing|basic   which text the .p is made from (default: basic if
//                  rebuilt, else listing)
//    --report      print the analysis report
//  Exit codes: 0 = done, 1 = could not read the file, 2 = bad arguments.
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
[assembly: AssemblyVersion("1.00.0.0")]
[assembly: AssemblyFileVersion("1.00.0.0")]

namespace ZX81Compiler
{
    public static class DecompilerCli
    {
        public static int Main(string[] args)
        {
            string input = null, txt = null, basic = null, asm = null, pout = null, pFrom = null;
            bool report = false;
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
                        case "-h": case "--help": case "/?": Usage(); return 0;
                        case "--version": Console.WriteLine("zx81dc - ZX81 Decompiler " + Versions.Show(Versions.Decompiler)); return 0;
                        default:
                            if (input != null) { Console.WriteLine("Unexpected argument: " + args[i]); return 2; }
                            input = args[i]; break;
                    }
                }
            }
            catch (IndexOutOfRangeException) { Console.WriteLine("An option is missing its file name."); return 2; }
            if (input == null) { Usage(); return 2; }

            PFile p;
            try { p = PFile.Load(input); }
            catch (Exception e) { Console.WriteLine("Could not read " + input + ": " + e.Message); return 1; }
            string name = Path.GetFileName(input);
            var r = Decompiler.Run(p, name);
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

        static void Usage()
        {
            Console.WriteLine("zx81dc - ZX81 Decompiler " + Versions.Show(Versions.Decompiler));
            Console.WriteLine("Usage: zx81dc program.p [--txt file] [--basic file] [--asm file] [--p file] [--p-from listing|basic] [--report]");
            Console.WriteLine("  Writes the BASIC listing (.txt), rebuilt BASIC for compiled programs (.bas),");
            Console.WriteLine("  machine code (.asm), and optionally a new .p file made from the text.");
        }
    }
}
