// Tests for the tape/audio decoders (TapeDecode.cs), the format loader (TapeFormats.cs)
// and ZX80 conversion (Zx80.cs). Run by tests/run_tests.sh:
//     TapeTest.exe file.p ...      round trips and damaged-audio checks for each file
// Prints one line per check and "all OK" (exit 0) or the number of failures (exit 1).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZX81Compiler;

public static class TapeTest
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what + (detail.Length > 0 ? "  (" + detail + ")" : ""));
        if (!ok) fails++;
    }

    public static int Main(string[] args)
    {
        // TapeTest --write-tapes dir prog.p ... : just write prog.wav/.tzx/.p81/.o into dir
        // (run_tests.sh feeds them to zx81dc to check that it reads every format)
        if (args.Length > 1 && args[0] == "--write-tapes")
        {
            for (int i = 2; i < args.Length; i++)
            {
                byte[] p = File.ReadAllBytes(args[i]);
                string stem = Path.Combine(args[1], Path.GetFileNameWithoutExtension(args[i]));
                string name = Path.GetFileNameWithoutExtension(args[i]).ToUpperInvariant();
                File.WriteAllBytes(stem + ".wav", Tape.MakeWav(p, name));
                File.WriteAllBytes(stem + ".tzx", Tape.MakeTzx(p, name));
                File.WriteAllBytes(stem + ".p81", TapeDecode.MakeP81(p, name));
                try { File.WriteAllBytes(stem + ".o", Zx80.FromP(PFile.Load(p), new List<string>())); } catch (Zx80Exception) { }
            }
            // and a small ZX80 program
            var toks = Basic81.TokenizeProgram("10 PRINT \"HELLO ZX80\"\n20 LET A=12-3\n30 GOTO 10\n", new List<Diagnostic>());
            File.WriteAllBytes(Path.Combine(args[1], "zx80demo.o"), Zx80.FromP(PFile.Load(Basic81.BuildP(toks, null, false, false, -1)), new List<string>()));
            return 0;
        }
        foreach (var f in args)
        {
            byte[] p = File.ReadAllBytes(f);
            string name = Path.GetFileNameWithoutExtension(f).ToUpperInvariant();
            string tag = Path.GetFileName(f) + ": ";
            RoundTrips(tag, p, name);
            EditTests(tag, p);
        }
        if (args.Length > 0) Damaged(File.ReadAllBytes(args[0]));
        Zx80Tests();
        Console.WriteLine(fails == 0 ? "all OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- browser edits
    // REM injection (Basic81.InjectRem) and hidden numbers (Basic81.HiddenNumberOffsets),
    // which the .P Toolkit uses.
    static void EditTests(string tag, byte[] p)
    {
        var pf = PFile.Load(p);
        // every {=value} in the listing has an offset, in the same order
        var o = new DetokOptions { Style = ListStyle.Display, ShowFloats = true };
        bool same = true;
        foreach (var l in pf.Lines)
        {
            var offs = Basic81.HiddenNumberOffsets(l.Body);
            string txt = Basic81.Detokenize(l.Body, o).Text;
            int shown = 0; for (int i = txt.IndexOf("{="); i >= 0; i = txt.IndexOf("{=", i + 2)) shown++;
            if (shown != offs.Count || offs.Any(k => l.Body[k] != 0x7E)) same = false;
        }
        Check(tag + "hidden numbers line up with the {=value} annotations", same);

        var rem = pf.Lines.FirstOrDefault(l => l.Body.Length > 1 && l.Body[0] == 0xEA);
        if (rem == null) return;
        byte[] payload = rem.Body.Skip(1).Take(rem.Body.Length - 2).ToArray();
        string why;
        // putting the same bytes back gives the identical file
        byte[] again = Basic81.InjectRem(pf, rem.Number, payload, out why);
        Check(tag + "REM injection of the same bytes gives the identical file", again != null && again.SequenceEqual(p));
        // a bigger and a smaller payload, then the original again: identical again
        foreach (int n in new[] { payload.Length + 300, Math.Max(1, payload.Length / 3) })
        {
            var bin = Enumerable.Range(0, n).Select(i => (byte)(i * 13 + 5)).ToArray();
            var q = PFile.Load(Basic81.InjectRem(pf, rem.Number, bin, out why));
            var ql = q.Lines.First(l => l.Number == rem.Number);
            int last = q.Lines.Last().End;
            bool ok = q.Problems.Count == 0 && ql.Body.Skip(1).Take(n).SequenceEqual(bin) && q.DFile == last
                && q.VarsAddr - q.DFile == pf.VarsAddr - pf.DFile && q.ELine - q.VarsAddr == pf.ELine - pf.VarsAddr
                && q.DfCc - q.DFile == pf.DfCc - pf.DFile
                && (pf.AutoRunLine == null) == (q.AutoRunLine == null) && (q.AutoRunLine == null || q.AutoRunLine.Number == pf.AutoRunLine.Number)
                && q.DisplayBytes().SequenceEqual(pf.DisplayBytes()) && q.VariablesBytes().SequenceEqual(pf.VariablesBytes());
            Check(tag + "REM injection of " + n + " bytes: D_FILE, DF_CC, VARS, E_LINE, NXTLIN moved correctly", ok,
                "problems " + q.Problems.Count + ", D_FILE " + q.DFile.ToString("X4") + " end " + last.ToString("X4"));
            byte[] back = Basic81.InjectRem(q, rem.Number, payload, out why);
            Check(tag + "  ... and the original bytes back gives the identical file", back != null && back.SequenceEqual(p));
        }
    }

    // ---------------------------------------------------------------- round trips
    static void RoundTrips(string tag, byte[] p, string name)
    {
        byte[] wav = Tape.MakeWav(p, name);
        Same(tag + ".p -> .wav -> .p", () => TapeDecode.ReadWav(wav), p, name);
        Same(tag + ".p -> .tzx -> .p", () => TapeDecode.ReadTzx(Tape.MakeTzx(p, name), new List<string>()), p, name);
        Same(tag + ".p -> .p81 -> .p", () => TapeDecode.ReadP81(TapeDecode.MakeP81(p, name)), p, name);
        // other recordings of the same tape
        int rate;
        float[] s = TapeDecode.WavSamples(wav, out rate);
        Same(tag + "8-bit stereo 22050 Hz", () => TapeDecode.ReadWav(MakeWav(Resample(s, rate, 22050, 1.0), 22050, 8, 2, false)), p, name);
        Same(tag + "24-bit 48000 Hz, inverted", () => TapeDecode.ReadWav(MakeWav(Resample(s, rate, 48000, 1.0).Select(v => -v).ToArray(), 48000, 24, 1, false)), p, name);
        Same(tag + "32-bit float, pulses one way only + DC offset", () => TapeDecode.ReadWav(MakeWav(s.Select(v => v > 0 ? 0.6f + v * 0.3f : 0.6f).ToArray(), rate, 32, 1, true)), p, name);
        var rnd = new Random(1);
        Same(tag + "quiet, with 3% noise", () => TapeDecode.ReadWav(MakeWav(s.Select(v => v * 0.2f + (float)(rnd.NextDouble() - 0.5) * 0.03f).ToArray(), rate, 16, 1, false)), p, name);
        Same(tag + "tape 8% fast", () => TapeDecode.ReadWav(MakeWav(Resample(s, rate, rate, 1.08), rate, 16, 1, false)), p, name);
        Same(tag + ".tzx direct recording (block 15h)", () => TapeDecode.ReadTzx(DirectRecordingTzx(s, rate), new List<string>()), p, name);
        Same(tag + "TapeFormats.Load(.wav)", () => new List<TapeProgram> { Wrap(TapeFormats.Load(wav, ".wav", "x")) }, p, name);
        Same(tag + "TapeFormats.Load(no extension, sniffed .tzx)", () => new List<TapeProgram> { Wrap(TapeFormats.Load(Tape.MakeTzx(p, name), "", "x")) }, p, name);
    }

    static TapeProgram Wrap(LoadedProgram l) { return new TapeProgram { P = l.P, Name = l.Name }; }

    static void Same(string what, Func<List<TapeProgram>> decode, byte[] p, string name)
    {
        try
        {
            var r = decode();
            bool ok = r.Count >= 1 && r[0].P.SequenceEqual(p) && r[0].Name == Tape.NameBytes(name).Select(c => Charset.Printable[c & 0x3F]).Aggregate("", (a, c) => a + c);
            Check(what, ok, ok ? "" : "got " + r[0].P.Length + " bytes, name " + r[0].Name);
        }
        catch (Exception e) { Check(what, false, e.Message); }
    }

    // ---------------------------------------------------------------- damaged audio
    static void Damaged(byte[] p)
    {
        int rate;
        float[] s = TapeDecode.WavSamples(Tape.MakeWav(p, "TEST"), out rate);
        double len = s.Length / (double)rate;
        // a dropout of 200 ms at 3.0 s
        var d1 = (float[])s.Clone();
        for (int i = (int)(3.0 * rate); i < (int)(3.2 * rate); i++) d1[i] = 0;
        Expect("dropout at 3.000 s", MakeWav(d1, rate, 16, 1, false), 2.99, 3.01, "signal was lost");
        // a burst of loud noise at 4.5 s
        var d2 = (float[])s.Clone();
        var rnd = new Random(7);
        for (int i = (int)(4.5 * rate); i < (int)(4.52 * rate); i++) d2[i] = (float)(rnd.NextDouble() * 2 - 1);
        Expect("noise burst at 4.500 s", MakeWav(d2, rate, 16, 1, false), 4.49, 4.53, "");
        // the recording stops at 5 s
        var d3 = s.Take((int)(5.0 * rate)).ToArray();
        Expect("recording cut at 5.000 s", MakeWav(d3, rate, 16, 1, false), 4.98, 5.0, "ends in the middle");
        // a silent file
        try { TapeDecode.ReadWav(MakeWav(new float[rate], rate, 16, 1, false)); Check("silent file is an error", false); }
        catch (TapeDecodeException e) { Check("silent file is an error", e.Message.Contains("no ZX81 tape signal"), e.Message); }
        Console.WriteLine("      (recording is " + TapeDecodeException.Stamp(len) + " long)");
    }

    static void Expect(string what, byte[] wav, double from, double to, string text)
    {
        try { TapeDecode.ReadWav(wav); Check(what + ": reported", false, "decoded without an error"); }
        catch (TapeDecodeException e)
        {
            bool when = e.Seconds >= from && e.Seconds <= to;
            bool stamp = e.Message.StartsWith(TapeDecodeException.Stamp(e.Seconds)) && System.Text.RegularExpressions.Regex.IsMatch(e.Message, @"^\[\d\d:\d\d\.\d\d\d\] ");
            Check(what + ": reported at " + TapeDecodeException.Stamp(e.Seconds), when && stamp && (text.Length == 0 || e.Message.Contains(text)), e.Message);
        }
    }

    // ---------------------------------------------------------------- ZX80
    static void Zx80Tests()
    {
        // a ZX81 program that only uses ZX80 BASIC -> .o -> back
        string src = "10 REM ZX80 TEST\n20 LET A=5\n30 PRINT \"HELLO \";A*2;\"\"\"Q\"\"\"\n40 FOR I=1 TO 3\n50 PRINT CHR$ (38+I);\n" +
                     "60 NEXT I\n70 IF A=5 THEN GOTO 90\n80 STOP\n90 POKE 16514,PEEK (16514)\n100 RAND 7\n110 GOSUB 200\n120 PRINT \"\\'.\\:'%A\"\n200 RETURN\n";
        var diags = new List<Diagnostic>();
        byte[] p81 = Basic81.BuildP(Basic81.TokenizeProgram(src, diags), null, false, false, -1);
        var notes = new List<string>();
        try
        {
            byte[] o = Zx80.FromP(PFile.Load(p81), notes);
            Check("ZX80: .o looks like a ZX80 file", Zx80.LooksLikeZx80(o) && TapeFormats.Sniff(o) == ".o");
            var back = Zx80.ToP(o);
            var o1 = new DetokOptions { Style = ListStyle.Exact };
            int esc;
            string a = Basic81.ProgramText(PFile.Load(p81), o1, out esc), b = Basic81.ProgramText(PFile.Load(back.P), o1, out esc);
            Check("ZX80: ZX81 -> .o -> ZX81 gives the same listing", a == b, a == b ? "" : "\n" + a + "\n---\n" + b);
            Check("ZX80: same program bytes", PFile.Load(p81).Lines.Select(l => l.Body).SelectMany(x => x).SequenceEqual(PFile.Load(back.P).Lines.Select(l => l.Body).SelectMany(x => x)));
        }
        catch (Exception e) { Check("ZX80: ZX81 -> .o -> ZX81", false, e.Message); }

        // something the ZX80 can't do
        byte[] bad = Basic81.BuildP(Basic81.TokenizeProgram("10 PLOT 1,2\n20 PRINT SIN 1\n30 LET A=1.5\n", diags), null, false, false, -1);
        try { Zx80.FromP(PFile.Load(bad), notes); Check("ZX80: PLOT/SIN/1.5 refused", false); }
        catch (Zx80Exception e)
        {
            Check("ZX80: PLOT/SIN/1.5 refused, all three lines named", e.Problems.Count >= 3 && e.Message.Contains("line 10") && e.Message.Contains("line 20") && e.Message.Contains("line 30"), e.Message.Replace("\n", " | "));
        }

        // a ZX80 program made by hand: tokens, a quote in a string, graphics, RND(6), variables
        var m = new List<byte>(new byte[0x28]);
        Action<int[]> add = xs => { foreach (int x in xs) m.Add((byte)x); };
        add(new[] { 0x00, 0x0A, 0xF4, 0x01, 0x2D, 0x2A, 0x31, 0x31, 0x34, 0xD4, 0x01, 0xD7, 0x37, 0x33, 0x29, 0x10, 0x22, 0x11, 0x76 });   // 10 PRINT "HELLO"";RND(6)
        add(new[] { 0x00, 0x14, 0xF0, 0x26, 0xE3, 0x1D, 0x1E, 0xDC, 0x1F, 0x76 });                    // 20 LET A=12-3
        add(new[] { 0x00, 0x1E, 0xF4, 0x01, 0x04, 0x09, 0x82, 0x01, 0x76 });                          // 30 PRINT "<graphics>"
        add(new[] { 0x00, 0x28, 0xEF, 0x76 });                                                        // 40 RANDOMISE
        int vars = 0x4000 + m.Count;
        add(new[] { 0x66, 9, 0 });                                                                    // A = 9
        add(new[] { 0x87, 0x2D, 0x2E, 0x01 });                                                        // B$ = "HI"
        add(new[] { 0xA8, 2, 1, 0, 2, 0, 3, 0 });                                                     // C(0..2) = 1,2,3
        add(new[] { 0xE9, 4, 0, 10, 0, 50, 0 });                                                      // FOR D: value 4, limit 10, line 50
        add(new[] { 0x80 });
        int eLine = 0x4000 + m.Count;
        m[0x08] = (byte)vars; m[0x09] = (byte)(vars >> 8); m[0x0A] = (byte)eLine; m[0x0B] = (byte)(eLine >> 8);
        byte[] ofile = m.ToArray();
        try
        {
            var r = TapeFormats.Load(ofile, ".o", "hand");
            var pf = PFile.Load(r.P);
            int esc;
            string txt = Basic81.ProgramText(pf, new DetokOptions { Style = ListStyle.Exact }, out esc);
            // graphics: ZX80 04h = top-left quarter, 09h = grey, 82h = inverse left half (= right half)
            string want = "10 PRINT \"HELLO\"\"\";(1+INT (RND *(6)))\n20 LET A=12-3\n30 PRINT \"\\' \\08\\ :\"\n40 RAND \n";
            bool vok = pf.ReadVariables().Count == 4;
            Check("ZX80: hand-made .o converts", txt.Replace("\r", "") == want, txt.Replace("\n", " | ") + "  vs  " + want.Replace("\n", " | "));
            Console.WriteLine("      variables: " + string.Join(", ", pf.ReadVariables().Select(v => v.Name + "=" + v.Value)));
            Check("ZX80: its 4 variables convert", vok, string.Join(", ", pf.ReadVariables().Select(v => v.Name + "=" + v.Value)));
            Check("ZX80: notes mention whole-number arithmetic", r.Notes.Any(n => n.Contains("whole-number")));
        }
        catch (Exception e) { Check("ZX80: hand-made .o converts", false, e.Message); }
    }

    // ---------------------------------------------------------------- helpers
    /// <summary>Change the sample rate (and speed: speed 1.08 = tape 8% fast).</summary>
    static float[] Resample(float[] s, int from, int to, double speed)
    {
        int n = (int)(s.Length * (double)to / from / speed);
        var o = new float[n];
        for (int i = 0; i < n; i++) { int k = (int)(i * speed * from / to); o[i] = s[Math.Min(k, s.Length - 1)]; }
        return o;
    }

    /// <summary>A .wav file in any of the formats the decoder reads.</summary>
    static byte[] MakeWav(float[] s, int rate, int bits, int channels, bool isFloat)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int bps = bits / 8, dataLen = s.Length * bps * channels;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)(isFloat ? 3 : 1)); w.Write((short)channels);
        w.Write(rate); w.Write(rate * bps * channels); w.Write((short)(bps * channels)); w.Write((short)bits);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
        foreach (float f in s)
            for (int c = 0; c < channels; c++)
            {
                double v = Math.Max(-1, Math.Min(0.99999, f));
                if (isFloat) w.Write((float)v);
                else if (bits == 8) w.Write((byte)(128 + (int)(v * 127)));
                else if (bits == 16) w.Write((short)(v * 32767));
                else if (bits == 24) { int x = (int)(v * 8388607); w.Write((byte)x); w.Write((byte)(x >> 8)); w.Write((byte)(x >> 16)); }
                else w.Write((int)(v * 2147483647));
            }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>A .tzx with the audio as a direct recording block (15h), 1 bit per sample.</summary>
    static byte[] DirectRecordingTzx(float[] s, int rate)
    {
        int tps = (int)Math.Round(3500000.0 / rate);
        var bits = new byte[(s.Length + 7) / 8];
        for (int i = 0; i < s.Length; i++) if (s[i] > 0.1f) bits[i / 8] |= (byte)(0x80 >> (i % 8));
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("ZXTape!")); w.Write((byte)0x1A); w.Write((byte)1); w.Write((byte)20);
        w.Write((byte)0x15); w.Write((ushort)tps); w.Write((ushort)0); w.Write((byte)(s.Length % 8 == 0 ? 8 : s.Length % 8));
        w.Write((byte)bits.Length); w.Write((byte)(bits.Length >> 8)); w.Write((byte)(bits.Length >> 16));
        w.Write(bits);
        w.Flush();
        return ms.ToArray();
    }
}
