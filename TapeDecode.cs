// =====================================================================================
//  TapeDecode.cs - reading ZX81 programs back from cassette audio and tape images
// -------------------------------------------------------------------------------------
//  The opposite of Tape.cs. Each format holds the same thing a ZX81 sends to tape:
//     the program name (ZX81 characters, the last with bit 7 set)
//     followed by the .p contents (memory from 4009h up to E_LINE)
//
//  .p81   that data as it is (several programs may follow each other)
//  .tzx   a tape image: the data is in a "generalized data" block (19h), one symbol per
//         bit (the symbol with more pulses is a 1), or in a "direct recording" (15h),
//         which is decoded like audio
//  .wav   cassette audio (PCM 8/16/24/32-bit or 32-bit float, mono or stereo, any rate).
//         Decoded by finding the pulses: each bit is a burst of 4 (a 0) or 9 (a 1)
//         pulses of about 300us, with about 1.3ms of silence between bits.
//
//  If audio can't be read (a dropout, noise, a damaged recording), a TapeDecodeException
//  says where, as [mm:ss.ms] from the start of the file.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZX81Compiler
{
    /// <summary>A tape or audio file that couldn't be decoded. Seconds (and Stamp, as
    /// [mm:ss.ms]) say where in the recording it went wrong, or are -1 if not known.</summary>
    public class TapeDecodeException : Exception
    {
        public readonly double Seconds;
        public TapeDecodeException(string message, double seconds)
            : base(seconds >= 0 ? Stamp(seconds) + " " + message : message) { Seconds = seconds; }

        /// <summary>A time as [mm:ss.ms], e.g. [01:23.456].</summary>
        public static string Stamp(double seconds)
        {
            if (seconds < 0) seconds = 0;
            long ms = (long)Math.Round(seconds * 1000);
            return string.Format("[{0:00}:{1:00}.{2:000}]", ms / 60000, (ms / 1000) % 60, ms % 1000);
        }
    }

    /// <summary>One program read from a tape: its name and its .p bytes.</summary>
    public class TapeProgram
    {
        public string Name;                          // name as text (e.g. "FORTRESS")
        public byte[] P;                             // the .p file contents
        public double StartSeconds = -1, EndSeconds = -1;   // where it is in a recording (audio only)
    }

    /// <summary>Settings for reading .wav recordings of tapes, for recordings that don't
    /// decode with the defaults (zx81dc --wav-threshold / --wav-hysteresis / --wav-invert,
    /// the .P Toolkit's Options > Audio Import Options).</summary>
    public class AudioOptions
    {
        /// <summary>Noise threshold: where the signal (over the last millisecond) is quieter
        /// than this part of the loudest signal in the recording, it counts as silence.
        /// Lower it for recordings that get quieter; raise it for noisy ones. Default 0.20.</summary>
        public double Threshold = DefaultThreshold;
        /// <summary>Hysteresis: how far past the middle of the local signal a level must go to
        /// count as high or low, as a part of the local signal's size. Raise it to ignore
        /// ripples and hiss on the pulses; lower it for weak, rounded pulses. Default 0.15.</summary>
        public double Hysteresis = DefaultHysteresis;
        /// <summary>Turn the recording upside down first (pulses are then found on the other edge).</summary>
        public bool Invert;

        public const double DefaultThreshold = 0.20, DefaultHysteresis = 0.15;
        public bool IsDefault { get { return Threshold == DefaultThreshold && Hysteresis == DefaultHysteresis && !Invert; } }

        /// <summary>Check the settings; null if fine, else what is wrong.</summary>
        public string Problem()
        {
            if (Threshold < 0.01 || Threshold > 0.9) return "the noise threshold must be between 1% and 90%";
            if (Hysteresis < 0.01 || Hysteresis > 0.45) return "the hysteresis must be between 1% and 45%";
            return null;
        }

        public override string ToString()
        {
            return "noise threshold " + Math.Round(Threshold * 100) + "%, hysteresis " + Math.Round(Hysteresis * 100) + "%" + (Invert ? ", inverted" : "");
        }
    }

    public static class TapeDecode
    {
        // ------------------------------------------------------------------
        //  Name + .p data (shared by all the formats)
        // ------------------------------------------------------------------

        /// <summary>Read one program (name, then .p data) from tape bytes starting at
        /// start. next = where the following program would start. Throws a
        /// TapeDecodeException (with no time) if the bytes aren't a ZX81 program.</summary>
        public static TapeProgram ParseTapeData(byte[] d, int start, out int next)
        {
            int i = start;
            var name = new StringBuilder();
            while (true)
            {
                if (i >= d.Length) throw new TapeDecodeException("the data ends inside the program name", -1);
                int c = d[i++];
                name.Append(Charset.Printable[c & 0x3F]);
                if ((c & 0x80) != 0) break;
                if (c >= 0x40) throw new TapeDecodeException("this isn't a ZX81 program name (character code " + Z80Disasm.Hex(c, 2) + ")", -1);
                if (name.Length > 127) throw new TapeDecodeException("no end to the program name in the first 127 bytes", -1);
            }
            int len = PLength(d, i);
            if (i + len > d.Length)
                throw new TapeDecodeException("the program is cut short: it should be " + len + " bytes but only " + (d.Length - i) + " are there", -1);
            var p = new byte[len];
            Array.Copy(d, i, p, 0, len);
            next = i + len;
            return new TapeProgram { Name = name.ToString(), P = p };
        }

        /// <summary>The length of .p data starting at offset i (from its E_LINE system
        /// variable, at 4014h). Throws if that isn't a sensible ZX81 program.</summary>
        static int PLength(byte[] d, int i)
        {
            if (i + 13 > d.Length) throw new TapeDecodeException("the data ends inside the system variables", -1);
            int eLine = d[i + 0x0B] | d[i + 0x0C] << 8;
            int len = eLine - PFile.Base;
            if (len < PFile.ProgStart - PFile.Base + 1 || len > 0x10000 - PFile.Base)
                throw new TapeDecodeException("this isn't a ZX81 program (its E_LINE system variable, " + Z80Disasm.Hex(eLine, 4) + ", is impossible)", -1);
            return len;
        }

        /// <summary>A .p81 file: one or more programs, each a name followed by its .p data.</summary>
        public static List<TapeProgram> ReadP81(byte[] d)
        {
            var list = new List<TapeProgram>();
            int i = 0;
            while (i < d.Length)
            {
                int next;
                try { list.Add(ParseTapeData(d, i, out next)); }
                catch (TapeDecodeException) { if (list.Count > 0) break; throw; }   // trailing junk after a good program
                i = next;
            }
            return list;
        }

        /// <summary>A .p81 file for one program.</summary>
        public static byte[] MakeP81(byte[] pfile, string name) { return Tape.TapeBytes(pfile, name); }

        // ------------------------------------------------------------------
        //  WAV
        // ------------------------------------------------------------------

        /// <summary>Decode cassette audio. Returns every ZX81 program found, in order
        /// (normally one). Throws a TapeDecodeException with the time of the problem if
        /// the recording can't be read.</summary>
        public static List<TapeProgram> ReadWav(byte[] wav, AudioOptions opts = null)
        {
            int rate;
            float[] s = WavSamples(wav, out rate);
            return DecodeSamples(s, rate, 0, opts);
        }

        /// <summary>The audio in a .wav file as mono samples (channels averaged).</summary>
        public static float[] WavSamples(byte[] wav, out int rate)
        {
            if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
                throw new TapeDecodeException("this isn't a .wav file (no RIFF/WAVE header)", -1);
            int fmt = -1, channels = 0, bits = 0, dataAt = -1, dataLen = 0;
            rate = 0;
            for (int p = 12; p + 8 <= wav.Length;)
            {
                string id = Encoding.ASCII.GetString(wav, p, 4);
                int len = BitConverter.ToInt32(wav, p + 4);
                int body = p + 8;
                if (len < 0 || body + len > wav.Length) len = wav.Length - body;   // truncated file: use what is there
                if (id == "fmt ")
                {
                    fmt = BitConverter.ToUInt16(wav, body);
                    channels = BitConverter.ToUInt16(wav, body + 2);
                    rate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToUInt16(wav, body + 14);
                    if (fmt == 0xFFFE && len >= 26) fmt = BitConverter.ToUInt16(wav, body + 24);   // WAVE_FORMAT_EXTENSIBLE: the sub-format
                }
                else if (id == "data") { dataAt = body; dataLen = len; break; }
                p = body + len + (len & 1);            // chunks are padded to an even length
            }
            if (fmt < 0 || dataAt < 0) throw new TapeDecodeException("the .wav file has no format or no audio data", -1);
            if (!(fmt == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !(fmt == 3 && bits == 32))
                throw new TapeDecodeException("unsupported .wav format (format " + fmt + ", " + bits + "-bit): use PCM 8/16/24/32-bit or 32-bit float", -1);
            if (channels < 1 || rate < 8000) throw new TapeDecodeException("unsupported .wav format (" + channels + " channels, " + rate + " Hz): it needs at least 8000 Hz", -1);
            int bytesPer = bits / 8, frame = bytesPer * channels;
            int n = dataLen / frame;
            var s = new float[n];
            for (int k = 0; k < n; k++)
            {
                double sum = 0;
                int o = dataAt + k * frame;
                for (int c = 0; c < channels; c++, o += bytesPer)
                {
                    double v;
                    if (fmt == 3) v = BitConverter.ToSingle(wav, o);
                    else if (bits == 8) v = (wav[o] - 128) / 128.0;              // 8-bit is unsigned
                    else if (bits == 16) v = BitConverter.ToInt16(wav, o) / 32768.0;
                    else if (bits == 24) v = ((wav[o] | wav[o + 1] << 8 | (sbyte)wav[o + 2] << 16)) / 8388608.0;
                    else v = BitConverter.ToInt32(wav, o) / 2147483648.0;
                    sum += v;
                }
                s[k] = (float)(sum / channels);
            }
            return s;
        }

        // Timings (seconds). Nominal: a pulse every 300us inside a bit, then 1.3ms of silence.
        const double Window = 0.0010;        // the level is judged against the last 1 ms
        const double BitSplit = 0.00075;     // pulses further apart than this belong to different bits
        const double Dropout = 0.030;        // this long with no pulses inside a program: the signal was lost
        const double Lead = 0.020;           // a program starts after at least this much silence

        /// <summary>Find ZX81 programs in mono audio samples. offset = time of sample 0
        /// (seconds), so errors give the time in the whole recording.</summary>
        public static List<TapeProgram> DecodeSamples(float[] s, int rate, double offset, AudioOptions opts = null)
        {
            opts = opts ?? new AudioOptions();
            string bad = opts.Problem();
            if (bad != null) throw new TapeDecodeException("audio import options: " + bad, -1);
            if (opts.Invert) { var inv = new float[s.Length]; for (int i = 0; i < s.Length; i++) inv[i] = -s[i]; s = inv; }
            var edges = PulseEdges(s, rate, opts);
            // group the pulses into bits: (time of first pulse, number of pulses)
            var bitT = new List<double>();
            var bitN = new List<int>();
            for (int i = 0; i < edges.Count; i++)
            {
                double t = edges[i] / (double)rate;
                if (bitT.Count == 0 || t - LastPulse(edges, i, rate) > BitSplit) { bitT.Add(t); bitN.Add(1); }
                else bitN[bitN.Count - 1]++;
            }
            var found = new List<TapeProgram>();
            int g = 0;
            while (true)
            {
                // the next program: a run of good bits after some silence
                int start = -1;
                for (int k = g; k + 8 <= bitT.Count; k++)
                {
                    bool quiet = k == 0 || bitT[k] - bitT[k - 1] > Lead;
                    if (!quiet) continue;
                    bool ok = true;
                    for (int j = k; j < k + 8 && ok; j++) ok = BitValue(bitN[j]) >= 0 && (j == k || bitT[j] - bitT[j - 1] < Dropout);
                    if (ok) { start = k; break; }
                }
                if (start < 0) break;
                g = ReadProgram(bitT, bitN, start, offset, found);
            }
            if (found.Count == 0)
                throw new TapeDecodeException(edges.Count == 0 ? "no ZX81 tape signal found in the recording (it is silent or too quiet)"
                    : "no ZX81 program found: there are sounds, but no run of ZX81 bits (4 or 9 pulses each)", -1);
            return found;
        }

        /// <summary>Time of the pulse before pulse i (seconds).</summary>
        static double LastPulse(List<int> edges, int i, int rate) { return i == 0 ? -1 : edges[i - 1] / (double)rate; }

        /// <summary>A bit from its number of pulses: 0 (about 4), 1 (about 9), or -1 if neither.</summary>
        static int BitValue(int pulses) { return pulses >= 3 && pulses <= 5 ? 0 : pulses >= 7 && pulses <= 11 ? 1 : -1; }

        /// <summary>Read one program starting at bit 'start'. Adds it to found and returns
        /// the index of the next unused bit. Throws with the time of the first bad bit.</summary>
        static int ReadProgram(List<double> bitT, List<int> bitN, int start, double offset, List<TapeProgram> found)
        {
            var bytes = new List<byte>();
            int nameEnd = -1, total = -1;           // index of the first .p byte; full length (name + .p)
            int k = start;
            int cur = 0, nbits = 0;
            while (total < 0 || bytes.Count < total)
            {
                if (k >= bitT.Count)
                {
                    double tEnd = bitT[bitT.Count - 1];
                    throw new TapeDecodeException("the recording ends in the middle of the program (" + Have(bytes, nameEnd, total) + ")", tEnd + offset);
                }
                if (k > start && bitT[k] - bitT[k - 1] > Dropout)
                    throw new TapeDecodeException("the signal was lost (no pulses for " + Math.Round((bitT[k] - bitT[k - 1]) * 1000) + " ms) in the middle of the program (" +
                        Have(bytes, nameEnd, total) + ")", bitT[k - 1] + offset);
                int v = BitValue(bitN[k]);
                if (v < 0 && k + 1 < bitT.Count && bitT[k + 1] - bitT[k] > Dropout)
                    throw new TapeDecodeException("the signal was lost (no pulses for " + Math.Round((bitT[k + 1] - bitT[k]) * 1000) + " ms) in the middle of the program (" +
                        Have(bytes, nameEnd, total) + ")", bitT[k] + offset);
                if (v < 0 && k + 1 >= bitT.Count)
                    throw new TapeDecodeException("the recording ends in the middle of the program (" + Have(bytes, nameEnd, total) + ")", bitT[k] + offset);
                if (v < 0)
                    throw new TapeDecodeException("can't read a bit: it has " + bitN[k] + " pulses (a 0 has 4, a 1 has 9) - noise or a damaged recording (" +
                        Have(bytes, nameEnd, total) + ")", bitT[k] + offset);
                cur = cur << 1 | v;
                if (++nbits == 8)
                {
                    bytes.Add((byte)cur);
                    cur = 0; nbits = 0;
                    if (nameEnd < 0)
                    {
                        byte c = bytes[bytes.Count - 1];
                        if ((c & 0x80) != 0) nameEnd = bytes.Count;
                        else if (c >= 0x40 || bytes.Count > 127)
                            throw new TapeDecodeException("this isn't a ZX81 program name (character code " + Z80Disasm.Hex(c, 2) + ")", bitT[k] + offset);
                    }
                    else if (total < 0 && bytes.Count - nameEnd == 13)
                    {
                        var arr = bytes.ToArray();
                        try { total = nameEnd + PLength(arr, nameEnd); }
                        catch (TapeDecodeException e) { throw new TapeDecodeException(e.Message, bitT[k] + offset); }
                    }
                }
                k++;
            }
            var data = bytes.ToArray();
            int dummy;
            var prog = ParseTapeData(data, 0, out dummy);
            prog.StartSeconds = bitT[start] + offset;
            prog.EndSeconds = bitT[k - 1] + offset;
            found.Add(prog);
            return k;
        }

        /// <summary>"name read, 1,234 of 5,678 bytes" - for error messages.</summary>
        static string Have(List<byte> bytes, int nameEnd, int total)
        {
            if (nameEnd < 0) return "while reading the name";
            return (bytes.Count - nameEnd) + " of " + (total < 0 ? "?" : (total - nameEnd).ToString()) + " program bytes read";
        }

        /// <summary>Sample numbers where a pulse starts. The level is judged against the
        /// highest and lowest values of the last millisecond, so DC offset, either polarity,
        /// pulses that only go one way, and volume changes are all fine. Quiet stretches
        /// (less than a fifth of the loudest signal, or AudioOptions.Threshold) count as silence.</summary>
        static List<int> PulseEdges(float[] s, int rate, AudioOptions opts)
        {
            var edges = new List<int>();
            if (s.Length == 0) return edges;
            float gmax = float.MinValue, gmin = float.MaxValue;
            foreach (float v in s) { if (v > gmax) gmax = v; if (v < gmin) gmin = v; }
            double quiet = opts.Threshold * (gmax - gmin);
            if (gmax - gmin < 1e-4) return edges;
            int w = Math.Max(2, (int)(Window * rate));
            // sliding maximum and minimum over the last w samples, with monotonic queues
            // kept in ring buffers (they never hold more than w+1 entries)
            int cap = w + 2;
            var qmax = new int[cap]; int hmax = 0, nmax = 0;
            var qmin = new int[cap]; int hmin = 0, nmin = 0;
            int state = 0;                           // 0 = silence, 1 = high, -1 = low
            for (int i = 0; i < s.Length; i++)
            {
                while (nmax > 0 && s[qmax[(hmax + nmax - 1) % cap]] <= s[i]) nmax--;
                qmax[(hmax + nmax++) % cap] = i;
                while (nmin > 0 && s[qmin[(hmin + nmin - 1) % cap]] >= s[i]) nmin--;
                qmin[(hmin + nmin++) % cap] = i;
                while (qmax[hmax] <= i - w) { hmax = (hmax + 1) % cap; nmax--; }
                while (qmin[hmin] <= i - w) { hmin = (hmin + 1) % cap; nmin--; }
                double hi = s[qmax[hmax]], lo = s[qmin[hmin]], amp = hi - lo;
                if (amp < quiet) { state = 0; continue; }
                double mid = (hi + lo) / 2, hyst = opts.Hysteresis * amp;
                // a pulse starts where the level rises to high, from low or from silence
                if (s[i] > mid + hyst) { if (state != 1) edges.Add(i); state = 1; }
                else if (s[i] < mid - hyst) state = -1;
            }
            // (a low straight out of silence isn't a pulse start; the rise after it is)
            return edges;
        }

        // ------------------------------------------------------------------
        //  TZX
        // ------------------------------------------------------------------

        /// <summary>Read the ZX81 programs in a .tzx tape image. Text blocks become notes.</summary>
        public static List<TapeProgram> ReadTzx(byte[] d, List<string> notes)
        {
            if (d.Length < 10 || Encoding.ASCII.GetString(d, 0, 7) != "ZXTape!" || d[7] != 0x1A)
                throw new TapeDecodeException("this isn't a .tzx file (no ZXTape! header)", -1);
            var list = new List<TapeProgram>();
            int p = 10;
            int blockNo = 0;
            while (p < d.Length)
            {
                int id = d[p++];
                blockNo++;
                int len;
                switch (id)
                {
                    case 0x19: len = (int)BitConverter.ToUInt32(d, p) + 4; list.AddRange(Tzx19(d, p + 4, blockNo)); break;
                    case 0x15:
                        {
                            len = 8 + U24(d, p + 5);
                            int tstates = BitConverter.ToUInt16(d, p), used = d[p + 4];
                            list.AddRange(Tzx15(d, p + 8, len - 8, tstates, used));
                            break;
                        }
                    case 0x10: len = 4 + BitConverter.ToUInt16(d, p + 2); notes.Add("block " + blockNo + " is a ZX Spectrum block (skipped)"); break;
                    case 0x11: len = 0x12 + U24(d, p + 0x0F); notes.Add("block " + blockNo + " is a turbo-speed (Spectrum style) block (skipped)"); break;
                    case 0x12: len = 4; break;
                    case 0x13: len = 1 + 2 * d[p]; break;
                    case 0x14: len = 10 + U24(d, p + 7); notes.Add("block " + blockNo + " is a pure data block (skipped)"); break;
                    case 0x18: case 0x2A: len = (int)BitConverter.ToUInt32(d, p) + 4; break;
                    case 0x20: case 0x23: case 0x24: len = 2; break;
                    case 0x21: len = 1 + d[p]; break;
                    case 0x22: case 0x25: case 0x27: len = 0; break;
                    case 0x26: len = 2 + 2 * BitConverter.ToUInt16(d, p); break;
                    case 0x28: case 0x32: len = 2 + BitConverter.ToUInt16(d, p); break;
                    case 0x2B: len = 5; break;
                    case 0x30: len = 1 + d[p]; notes.Add("tape text: " + Encoding.ASCII.GetString(d, p + 1, d[p])); break;
                    case 0x31: len = 2 + d[p + 1]; break;
                    case 0x33: len = 1 + 3 * d[p]; break;
                    case 0x35: len = 20 + (int)BitConverter.ToUInt32(d, p + 16); break;
                    case 0x5A: len = 9; break;
                    default:
                        // TZX 1.10+: unknown blocks start with a 4-byte length
                        if (p + 4 > d.Length) throw new TapeDecodeException("the .tzx file is damaged (block " + blockNo + ")", -1);
                        len = (int)BitConverter.ToUInt32(d, p) + 4;
                        notes.Add("block " + blockNo + " has an unknown type " + Z80Disasm.Hex(id, 2) + " (skipped)");
                        break;
                }
                if (len < 0 || p + len > d.Length) throw new TapeDecodeException("the .tzx file is damaged or cut short (block " + blockNo + ", type " + Z80Disasm.Hex(id, 2) + ")", -1);
                p += len;
            }
            if (list.Count == 0) throw new TapeDecodeException("no ZX81 program in the .tzx file (ZX81 programs are in generalized data blocks, type 19h)", -1);
            return list;
        }

        static int U24(byte[] d, int p) { return d[p] | d[p + 1] << 8 | d[p + 2] << 16; }

        /// <summary>Block 19h (generalized data): pilot/sync symbols are skipped; the data
        /// symbols give the bits (1 bit per symbol for ZX81 tapes).</summary>
        static List<TapeProgram> Tzx19(byte[] d, int p, int blockNo)
        {
            uint totp = BitConverter.ToUInt32(d, p + 2);
            int npp = d[p + 6], asp = d[p + 7] == 0 ? 256 : d[p + 7];
            uint totd = BitConverter.ToUInt32(d, p + 8);
            int npd = d[p + 12], asd = d[p + 13] == 0 ? 256 : d[p + 13];
            int q = p + 14;
            if (totp > 0) q += asp * (1 + 2 * npp) + (int)totp * 3;   // pilot symbol table and pilot stream
            if (totd == 0) return new List<TapeProgram>();
            // data symbols: count the pulses of each, to tell a 0 from a 1
            var pulses = new int[asd];
            for (int k = 0; k < asd; k++)
            {
                int n = 0;
                for (int e = 0; e < npd; e++) if (BitConverter.ToUInt16(d, q + 1 + k * (1 + 2 * npd) + 2 * e) != 0) n++;
                pulses[k] = n;
            }
            q += asd * (1 + 2 * npd);
            if (asd != 2) throw new TapeDecodeException("block " + blockNo + " (19h) uses " + asd + " symbols; ZX81 data uses 2 (a 0 and a 1)", -1);
            int one = pulses[1] >= pulses[0] ? 1 : 0;       // the symbol with more pulses is a 1
            int nbytes = (int)((totd + 7) / 8);
            if (q + nbytes > d.Length) throw new TapeDecodeException("block " + blockNo + " is cut short", -1);
            var data = new byte[(totd + 7) / 8];
            for (int b = 0; b < totd; b++)
            {
                int sym = (d[q + b / 8] >> (7 - b % 8)) & 1;
                if (sym == one) data[b / 8] |= (byte)(0x80 >> (b % 8));
            }
            int next;
            var prog = ParseTapeData(data, 0, out next);
            return new List<TapeProgram> { prog };
        }

        /// <summary>Block 15h (direct recording): one bit per sample; decoded like audio.</summary>
        static List<TapeProgram> Tzx15(byte[] d, int p, int len, int tstates, int usedLast)
        {
            int rate = (int)Math.Round(3500000.0 / Math.Max(1, tstates));
            int n = (len - 1) * 8 + (usedLast == 0 ? 8 : usedLast);
            var s = new float[Math.Max(0, n)];
            for (int i = 0; i < s.Length; i++) s[i] = ((d[p + i / 8] >> (7 - i % 8)) & 1) != 0 ? 1f : -1f;
            return DecodeSamples(s, rate, 0);
        }
    }
}
