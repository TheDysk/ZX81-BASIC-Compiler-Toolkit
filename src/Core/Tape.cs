// =====================================================================================
//  Tape.cs - making cassette audio (.wav) and tape images (.tzx) from a .p file
// -------------------------------------------------------------------------------------
//  What a ZX81 saves to tape:
//     the program name (ZX81 characters; the last one has bit 7 set)
//     followed by the .p file contents (memory from 4009h to E_LINE)
//  There is no lead-in tone, only silence before the data.
//
//  Each byte is sent most significant bit first. Each bit is a burst of pulses:
//     0 = 4 pulses, 1 = 9 pulses, each pulse about 150us high then 150us low,
//  followed by about 1300us of silence.
//
//  .wav: 16-bit mono, 44100 Hz. Play it from a computer or phone into the ZX81's EAR
//        socket with LOAD "" (or LOAD "NAME") - use a high volume.
//  .tzx: a standard tape image using a "generalized data" block (ID 19h) with the two
//        pulse patterns above, as the TZX format describes for the ZX81.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZX81Compiler
{
    public static class Tape
    {
        const int SampleRate = 44100;
        const double PulseHigh = 150e-6, PulseLow = 150e-6, BitGap = 1300e-6;

        /// <summary>Program name as ZX81 codes, last character with bit 7 set.
        /// Only letters, digits and a few symbols are kept; an empty name becomes "P".</summary>
        public static byte[] NameBytes(string name)
        {
            var list = new List<byte>();
            foreach (char c in (name ?? "").ToUpperInvariant())
            {
                int z = Charset.FromChar(c, false);
                if (z > 0 && z < 0x40 && list.Count < 32) list.Add((byte)z);
            }
            if (list.Count == 0) list.Add(0x35);   // P
            list[list.Count - 1] |= 0x80;
            return list.ToArray();
        }

        /// <summary>Everything that goes on the tape: name + .p contents.</summary>
        public static byte[] TapeBytes(byte[] pfile, string name)
        {
            var all = new List<byte>(NameBytes(name));
            all.AddRange(pfile);
            return all.ToArray();
        }

        // ------------------------------------------------------------------
        //  WAV
        // ------------------------------------------------------------------
        public static byte[] MakeWav(byte[] pfile, string name)
        {
            var samples = new List<short>();
            double t = 0;   // time written so far, in seconds (kept exact to avoid drift)
            Action<double, short> level = (secs, v) =>
            {
                double end = t + secs;
                int from = (int)Math.Round(t * SampleRate), to = (int)Math.Round(end * SampleRate);
                for (int i = from; i < to; i++) samples.Add(v);
                t = end;
            };
            const short high = 26000, low = -26000;
            level(1.0, 0);                             // lead-in silence
            foreach (byte b in TapeBytes(pfile, name))
                for (int bit = 7; bit >= 0; bit--)
                {
                    int pulses = ((b >> bit) & 1) != 0 ? 9 : 4;
                    for (int k = 0; k < pulses; k++) { level(PulseHigh, high); level(PulseLow, low); }
                    level(BitGap, 0);
                }
            level(1.0, 0);                             // trailing silence

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int dataLen = samples.Count * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
            foreach (short s in samples) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }

        // ------------------------------------------------------------------
        //  TZX
        // ------------------------------------------------------------------
        // Pulse lengths in T-states of the TZX reference clock (3.5 MHz):
        // 530 high, 520 low; the last low of each bit is stretched to include the gap.
        static readonly ushort[] Sym0 = { 530, 520, 530, 520, 530, 520, 530, 4689 };
        static readonly ushort[] Sym1 = { 530, 520, 530, 520, 530, 520, 530, 520, 530, 520, 530, 520, 530, 520, 530, 520, 530, 4689 };

        public static byte[] MakeTzx(byte[] pfile, string name)
        {
            byte[] data = TapeBytes(pfile, name);
            const int npd = 18;                        // most pulses (edges) in one symbol
            var block = new MemoryStream();
            var bw = new BinaryWriter(block);
            bw.Write((ushort)1000);                    // pause after the block (ms)
            bw.Write((uint)0);                         // TOTP: no pilot/sync
            bw.Write((byte)0);                         // NPP
            bw.Write((byte)0);                         // ASP
            bw.Write((uint)(data.Length * 8));         // TOTD: one symbol per bit
            bw.Write((byte)npd);                       // NPD
            bw.Write((byte)2);                         // ASD: two symbols (0 and 1)
            foreach (var sym in new[] { Sym0, Sym1 })
            {
                bw.Write((byte)0);                     // each edge flips the level
                for (int i = 0; i < npd; i++) bw.Write(i < sym.Length ? sym[i] : (ushort)0);
            }
            bw.Write(data);                            // 1 bit per symbol, most significant first = the bytes themselves
            bw.Flush();
            byte[] body = block.ToArray();

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("ZXTape!")); w.Write((byte)0x1A); w.Write((byte)1); w.Write((byte)20);
            // text description block (30h)
            string desc = "ZX81 program: " + (name ?? "") + " (made by the ZX81 toolset)";
            if (desc.Length > 255) desc = desc.Substring(0, 255);
            w.Write((byte)0x30); w.Write((byte)desc.Length); w.Write(Encoding.ASCII.GetBytes(desc));
            w.Write((byte)0x20); w.Write((ushort)1000);  // one second of silence first
            w.Write((byte)0x19); w.Write((uint)body.Length); w.Write(body);
            w.Flush();
            return ms.ToArray();
        }
    }
}
