// =====================================================================================
//  TapeFormats.cs - opening any supported file as a ZX81 program
// -------------------------------------------------------------------------------------
//  One place that knows every input format, used by the .P Toolkit and the Decompiler:
//     .p .81       the ZX81 program itself
//     .p81         name + program (the first program is used if there are several)
//     .o .80       a ZX80 program, converted to ZX81 BASIC (see Zx80.cs)
//     .wav         cassette audio, decoded (see TapeDecode.cs)
//     .tzx         a tape image
//  Files with another extension are recognised by their contents where possible.
//  Every result is a plain .p image, so the rest of the toolset doesn't need to know
//  where it came from; Notes say what was done to get it.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>A program opened from any supported file.</summary>
    public class LoadedProgram
    {
        public byte[] P;                     // the .p image
        public string Name;                  // the program's name (from the tape, or the file name)
        public string Format;                // e.g. "ZX81 .p file", "WAV audio", "ZX80 .o file"
        public List<string> Notes = new List<string>();   // what was done (conversions, other programs on the tape ...)
        public bool Converted;               // true if it isn't simply a .p file (saving it means saving a new .p)
    }

    public static class TapeFormats
    {
        /// <summary>Extensions the toolset can open.</summary>
        public static readonly string[] Extensions = { ".p", ".81", ".p81", ".o", ".80", ".wav", ".tzx" };

        // Every kind of file, for the Open dialogs: name and extensions. Windows' Open
        // dialog matches extensions whatever their case.
        static readonly string[][] ProgramTypes = {
            new[] { "ZX81 programs", "*.p;*.81" },
            new[] { "ZX81 programs with their tape name", "*.p81" },
            new[] { "ZX80 programs", "*.o;*.80" },
            new[] { "Cassette audio recordings", "*.wav" },
            new[] { "TZX tape images", "*.tzx" },
        };
        static readonly string[][] ListingTypes = {
            new[] { "ZX81 BASIC listings", "*.txt;*.bas;*.b81" },
        };

        /// <summary>An Open dialog filter: first everything together, then each kind on its
        /// own, then All files.</summary>
        static string MakeFilter(string allName, IEnumerable<string[]> types)
        {
            var list = types.ToList();
            string all = string.Join(";", list.Select(t => t[1]));
            var parts = new List<string> { allName + " (" + all.Replace(";", ", ") + ")|" + all };
            foreach (var t in list) parts.Add(t[0] + " (" + t[1].Replace(";", ", ") + ")|" + t[1]);
            parts.Add("All files (*.*)|*.*");
            return string.Join("|", parts);
        }

        /// <summary>Open dialog filter for the decompiler and the .P Toolkit: programs and tapes.</summary>
        public static readonly string OpenFilter = MakeFilter("All ZX81 / ZX80 programs and tapes", ProgramTypes);

        /// <summary>Open dialog filter for the compiler: BASIC listings, and programs and
        /// tapes (which are opened as BASIC through the decompiler).</summary>
        public static readonly string CompilerOpenFilter = MakeFilter("All listings, programs and tapes", ListingTypes.Concat(ProgramTypes));

        /// <summary>Open dialog filter for BASIC listings only (.txt .bas .b81).</summary>
        public static readonly string ListingFilter = MakeFilter("ZX81 BASIC listings", ListingTypes).Replace("|ZX81 BASIC listings (*.txt, *.bas, *.b81)|*.txt;*.bas;*.b81|", "|");

        /// <summary>Is this a BASIC listing (.txt .bas .b81), by its extension?</summary>
        public static bool IsListingFile(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            return e == ".txt" || e == ".bas" || e == ".b81";
        }

        /// <summary>The text of a listing file. Tries UTF-8 first (removing the byte order
        /// mark that Notepad may add); if the file isn't valid UTF-8 it is read as
        /// Windows-1252, which covers older files with a £ sign saved by other editors.</summary>
        public static string ReadListingText(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            try
            {
                // new[] { ... } rather than a single char: .NET Framework has no TrimStart(char)
                return new System.Text.UTF8Encoding(false, true).GetString(b).TrimStart(new[] { '\uFEFF' });
            }
            catch (System.Text.DecoderFallbackException)
            {
                return System.Text.Encoding.GetEncoding(1252).GetString(b);
            }
        }

        /// <summary>Is this a program or tape (rather than a text listing), by its extension?</summary>
        public static bool IsProgramFile(string path)
        {
            return Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        /// <summary>Is this one of the formats that has to be converted (not a plain .p)?</summary>
        public static bool NeedsConversion(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            return e != ".p" && e != ".81";
        }

        /// <summary>Open a file of any supported format. Throws TapeDecodeException (audio
        /// errors carry a [mm:ss.ms] time) or IOException.</summary>
        public static LoadedProgram Load(string path, AudioOptions audio = null)
        {
            return Load(File.ReadAllBytes(path), Path.GetExtension(path), Path.GetFileNameWithoutExtension(path), audio);
        }

        /// <summary>Open file contents, given the file's extension (".wav" ...) and name.
        /// audio: settings for .wav recordings (null = the defaults).</summary>
        public static LoadedProgram Load(byte[] data, string ext, string fileName, AudioOptions audio = null)
        {
            ext = (ext ?? "").ToLowerInvariant();
            if (ext == "") ext = Sniff(data);
            var r = new LoadedProgram { Name = fileName };
            List<TapeProgram> progs;
            switch (ext)
            {
                case ".wav":
                    r.Format = "WAV audio";
                    progs = TapeDecode.ReadWav(data, audio);
                    TakeFirst(r, progs, true);
                    if (audio != null && !audio.IsDefault) r.Notes.Add("read with " + audio);
                    break;
                case ".tzx":
                    r.Format = "TZX tape image";
                    progs = TapeDecode.ReadTzx(data, r.Notes);
                    TakeFirst(r, progs, false);
                    break;
                case ".p81":
                    r.Format = ".p81 file (name + program)";
                    progs = TapeDecode.ReadP81(data);
                    TakeFirst(r, progs, false);
                    break;
                case ".o":
                case ".80":
                    {
                        r.Format = "ZX80 " + ext + " file";
                        var z = Zx80.ToP(data);
                        r.P = z.P;
                        r.Notes.AddRange(z.Notes);
                        r.Converted = true;
                        break;
                    }
                default:
                    {
                        string guess = Sniff(data);
                        if (guess != ".p" && guess != ext) return Load(data, guess, fileName, audio);   // e.g. a .wav saved with another extension
                        r.Format = "ZX81 .p file";
                        r.P = data;
                        break;
                    }
            }
            return r;
        }

        static void TakeFirst(LoadedProgram r, List<TapeProgram> progs, bool audio)
        {
            var first = progs[0];
            r.P = first.P;
            r.Name = first.Name;
            r.Converted = true;
            r.Notes.Add("program \"" + first.Name + "\" (" + first.P.Length + " bytes)" +
                (audio ? " found at " + TapeDecodeException.Stamp(first.StartSeconds) + "-" + TapeDecodeException.Stamp(first.EndSeconds) : ""));
            if (progs.Count > 1)
            {
                var others = new List<string>();
                for (int i = 1; i < progs.Count; i++) others.Add("\"" + progs[i].Name + "\"");
                r.Notes.Add("the tape has " + (progs.Count - 1) + " more program" + (progs.Count == 2 ? "" : "s") + " (" + string.Join(", ", others) + "); only the first was opened");
            }
        }

        /// <summary>Guess the format from the contents.</summary>
        public static string Sniff(byte[] d)
        {
            if (d.Length >= 12 && Encoding.ASCII.GetString(d, 0, 4) == "RIFF") return ".wav";
            if (d.Length >= 8 && Encoding.ASCII.GetString(d, 0, 7) == "ZXTape!") return ".tzx";
            if (d.Length > 0 && d[0] != 0 && (d[0] & 0x80) == 0 && d[0] < 0x40)
            {
                // a .p starts with VERSN = 0; a .p81 with a name; a .o with ERR_NR (usually FFh)
                int dummy;
                try { TapeDecode.ParseTapeData(d, 0, out dummy); return ".p81"; } catch (TapeDecodeException) { }
            }
            if (d.Length > 0 && d[0] != 0 && Zx80.LooksLikeZx80(d)) return ".o";
            return ".p";
        }
    }
}
