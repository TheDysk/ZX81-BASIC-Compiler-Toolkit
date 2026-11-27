// =====================================================================================
//  Decompiler.cs - everything the decompiler produces for one .p file
// -------------------------------------------------------------------------------------
//  Used by both the command-line decompiler (zx81dc.exe) and the Windows one.
//
//  Outputs:
//   * Listing      - the BASIC exactly as stored, as text. Machine code in REM lines is
//                    kept as \hh escapes, so turning the text back into a .p (Tokenize)
//                    gives identical bytes. Starts with # comment lines describing the
//                    file (auto-run line, machine code found, saved variables).
//   * Basic        - for programs made by this toolset's compiler: the original BASIC
//                    rebuilt from the machine code (Recover.cs). Null otherwise.
//   * Asm          - the machine code as an assembly listing (CodeFinder.ToAsm).
//   * MakeP(text)  - a .p file from (possibly edited) listing text, keeping the original
//                    display file, variables and system variables.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZX81Compiler
{
    public class DecompileResult
    {
        public PFile File;
        public string Name;
        public CodeAnalysis Code;
        public RecoverResult Recovered;
        public string Listing;
        public string Basic;
        public string Asm;
        public string Report;
        public int EscapedLines;

        /// <summary>Make a .p file from listing text. Lines starting with # are notes and are skipped.
        /// The original display file, variables and auto-run setting are kept.</summary>
        public byte[] MakeP(string text, List<Diagnostic> diags, bool keepDisplay, bool keepVars)
        {
            var lines = Basic81.TokenizeProgram(text, diags);
            if (diags.Any(d => d.IsError)) return null;
            int auto = -1;
            var al = File.AutoRunLine;
            if (al != null) auto = lines.Any(l => l.Key == al.Number) ? al.Number : (lines.Count > 0 ? lines[0].Key : -1);
            return Basic81.BuildP(lines, File, keepDisplay, keepVars, auto);
        }
    }

    public static class Decompiler
    {
        public static DecompileResult Run(PFile p, string name)
        {
            var r = new DecompileResult { File = p, Name = name };
            r.Code = CodeFinder.Analyze(p);
            r.Recovered = Recover.Run(p, r.Code);
            if (r.Recovered.Success) r.Basic = Header(p, name, "BASIC rebuilt from the machine code made by the ZX81 BASIC compiler", r) + r.Recovered.Listing;

            int esc;
            string body = Basic81.ProgramText(p, new DetokOptions { Style = ListStyle.Exact }, out esc);
            r.EscapedLines = esc;
            r.Listing = Header(p, name, "BASIC listing exactly as stored", r) + VariablesComment(p) + body;
            r.Asm = r.Code.Blocks.Count > 0 ? CodeFinder.ToAsm(p, r.Code, name) : null;
            r.Report = MakeReport(p, name, r);
            return r;
        }

        static string Header(PFile p, string name, string what, DecompileResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + what + " - from " + name);
            sb.AppendLine("# " + p.AutoRunDescription());
            if (r.Recovered.IsOurs && r.Recovered.Success && what.StartsWith("BASIC rebuilt"))
            {
                sb.AppendLine("# Variable names are not stored in machine code, so they have been renamed.");
                sb.AppendLine("# Compile with: Max string length " + r.Recovered.Options.MaxString +
                    ", BREAK check " + (r.Recovered.Options.BreakCheck ? "on" : "off") + ", auto-run " + (r.Recovered.Options.Autorun ? "on" : "off") +
                    ", RAM " + r.Recovered.Options.Ram + "K");
                foreach (var n in r.Recovered.Notes) sb.AppendLine("# " + n);
            }
            else
            {
                foreach (var e in r.Code.Entries) sb.AppendLine("# Machine code entry " + e.Address + " (" + Z80Disasm.Hex(e.Address, 4) + "): " + e.Source);
                foreach (var n in r.Code.Notes) sb.AppendLine("# " + n);
                if (r.Code.CodeLines.Count > 0) sb.AppendLine("# Machine code in REM lines is kept as \\hh escapes so the program reloads exactly.");
            }
            sb.AppendLine("#");
            return sb.ToString();
        }

        static string VariablesComment(PFile p)
        {
            var vars = p.ReadVariables();
            if (vars.Count == 0) return "";
            var sb = new StringBuilder("# Saved variables (kept when making a .p file):\n");
            foreach (var v in vars) sb.AppendLine("#   " + v.Name + " = " + v.Value);
            sb.AppendLine("#");
            return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        static string MakeReport(PFile p, string name, DecompileResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("File: " + name + "  (" + p.Data.Length + " bytes)");
            sb.AppendLine("BASIC lines: " + p.Lines.Count + (p.Lines.Count > 0 ? "  (" + p.Lines[0].Number + " to " + p.Lines[p.Lines.Count - 1].Number + ")" : ""));
            sb.AppendLine("Auto-run: " + p.AutoRunDescription());
            int need;
            string ram = p.RamEstimate(out need);
            var rec = r.Recovered;
            if (rec != null && rec.IsOurs && rec.BssSize > 0)
                ram = rec.Options.Ram + "K RAM pack - compiled for " + rec.Options.Ram + "K: its variables use " +
                    Z80Disasm.Hex(rec.BssStart, 4) + "-" + Z80Disasm.Hex(rec.BssStart + rec.BssSize - 1, 4) + " while it runs (not saved in the file)";
            sb.AppendLine("RAM needed: " + ram);
            sb.AppendLine("Display file: " + (p.Display.Collapsed ? "collapsed (1K style)" : "expanded (16K style)") + ", " + p.Display.Size + " bytes");
            foreach (var pr in p.Problems) sb.AppendLine("Warning: " + pr);
            foreach (var hn in p.HardwareNotes(r.Code.Entries.Select(e => e.Address))) sb.AppendLine("Warning: " + hn);
            sb.AppendLine();
            sb.AppendLine("MACHINE CODE");
            if (!r.Code.HasMachineCode) sb.AppendLine("  none found");
            foreach (var e in r.Code.Entries) sb.AppendLine("  Entry " + e.Address + " (" + Z80Disasm.Hex(e.Address, 4) + "): " + e.Source);
            foreach (var n in r.Code.Notes) sb.AppendLine("  " + n);
            sb.AppendLine();
            if (r.Recovered.IsOurs)
            {
                sb.AppendLine("COMPILED BY THE ZX81 BASIC COMPILER");
                foreach (var n in r.Recovered.Notes) sb.AppendLine("  " + n);
                if (r.Recovered.Success)
                    sb.AppendLine("  Options detected: max string length " + r.Recovered.Options.MaxString + ", BREAK check " + (r.Recovered.Options.BreakCheck ? "on" : "off") + ", RAM " + r.Recovered.Options.Ram + "K" + (r.Recovered.Options.Ram > 16 ? " (variables at " + Z80Disasm.Hex(r.Recovered.BssStart, 4) + " and up)" : ""));
                sb.AppendLine();
            }
            if (r.EscapedLines > 0)
                sb.AppendLine(r.EscapedLines + " line(s) contain bytes that can't be written as normal BASIC text; they are written entirely as \\hh escapes so they reload exactly.");
            var vars = p.ReadVariables();
            if (vars.Count > 0)
            {
                sb.AppendLine("SAVED VARIABLES");
                foreach (var v in vars) sb.AppendLine("  " + v.Name.PadRight(10) + " " + v.Kind.PadRight(22) + v.Value);
            }
            return sb.ToString();
        }
    }
}
