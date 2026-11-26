// =====================================================================================
//  ZxFloat.cs - the ZX81's 5-byte floating point number format
// -------------------------------------------------------------------------------------
//  Every number in a ZX81 BASIC line is stored twice: the digits you see, then the
//  marker byte 7Eh and 5 hidden bytes holding the value. Numeric variables also use
//  these 5 bytes.
//
//  Format:  byte 0     exponent + 128 (0 means the number is zero)
//           bytes 1-4  mantissa, most significant byte first. The value is
//                      0.1mmm...m (binary) x 2^(exponent). The leading 1 is not
//                      stored: bit 7 of byte 1 holds the sign instead (1 = negative).
//
//  Example: 16514 = 0.50396... x 2^15  ->  8F 01 04 00 00
// =====================================================================================
using System;
using System.Globalization;

namespace ZX81Compiler
{
    public static class ZxFloat
    {
        /// <summary>Decode 5 bytes starting at mem[off].</summary>
        public static double Decode(byte[] mem, int off)
        {
            int exp = mem[off];
            if (exp == 0) return 0;
            bool neg = (mem[off + 1] & 0x80) != 0;
            // put the hidden leading 1 back
            uint mant = ((uint)(mem[off + 1] | 0x80) << 24) | ((uint)mem[off + 2] << 16) | ((uint)mem[off + 3] << 8) | mem[off + 4];
            double v = mant / 4294967296.0 * Math.Pow(2, exp - 128);
            return neg ? -v : v;
        }

        /// <summary>Encode a number as 5 bytes (rounded to the nearest representable value).</summary>
        public static byte[] Encode(double v)
        {
            var r = new byte[5];
            if (v == 0 || double.IsNaN(v)) return r;
            bool neg = v < 0;
            double a = Math.Abs(v);
            int e = (int)Math.Floor(Math.Log(a, 2)) + 1;      // so that a / 2^e is in [0.5, 1)
            double frac = a / Math.Pow(2, e);
            if (frac >= 1) { e++; frac /= 2; }                  // guard against rounding in Log
            if (frac < 0.5) { e--; frac *= 2; }
            ulong m = (ulong)Math.Round(frac * 4294967296.0);
            if (m >= 4294967296UL) { m >>= 1; e++; }            // rounding carried into a new bit
            int expByte = e + 128;
            if (expByte <= 0) return r;                         // too small: zero
            if (expByte > 255) expByte = 255;                   // too big: largest exponent
            r[0] = (byte)expByte;
            r[1] = (byte)(((m >> 24) & 0x7F) | (neg ? 0x80u : 0u));
            r[2] = (byte)(m >> 16);
            r[3] = (byte)(m >> 8);
            r[4] = (byte)m;
            return r;
        }

        /// <summary>Do the 5 bytes at mem[off] equal the encoding of the number written as text?</summary>
        public static bool Matches(string numberText, byte[] mem, int off)
        {
            double v;
            if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            byte[] e = Encode(v);
            for (int i = 0; i < 5; i++) if (e[i] != mem[off + i]) return false;
            return true;
        }

        /// <summary>Number as short readable text (whole numbers without a decimal point).</summary>
        public static string Format(double v)
        {
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("G10", CultureInfo.InvariantCulture);
        }
    }
}
