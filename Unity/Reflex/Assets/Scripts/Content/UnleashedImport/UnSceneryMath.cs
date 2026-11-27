// The arithmetic of the Unleashed scenery converter, as the Python reference (numpy 2.0 on Apple's Accelerate, arm64) rounds it. The
// reference library's bytes are the target, so every helper here states which numpy operation it stands for:
//   Fma               a * b + c with one rounding (the fused multiply-add the reference's einsum / matmul kernels compile to), in
//                     portable double arithmetic: Dekker's exact product + Boldo and Melquiond's round-to-odd sum
//   Chain3 / RowMat3  np.einsum("ij,jk->ik", v, m), and v @ m for a 3 column operand: 0 + a0 b0, then fused adds in order
//   Mul4 / RowsMul4   a @ b for 4 x 4 operands (Accelerate's dgemm): rows in pairs fma(a2, b2, a0 b0) + fma(a3, b3, a1 b1), the odd
//                     last row (and every row from 400 rows on) as the chain
//   Norm3 / Norm4     np.linalg.norm (sqrt(x.dot(x))): (x0 x0 + x1 x1) + x2 x2; (x0 x0 + x2 x2) + (x1 x1 + x3 x3)
//   Cross             np.cross: a1 b2 - a2 b1, a2 b0 - a0 b2, a0 b1 - a1 b0 (separate operations, nothing fused)
//   F32               struct.pack('f') / astype('<f4'): the nearest single
//   NpRound           round() of a numpy float64: rint(x * 10^n) / 10^n
// Nothing here uses Unity.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnNum
    {
        const double Splitter = 134217729.0;   // 2^27 + 1

        /// a * b + c, rounded once (IEEE fma). Exact for finite operands whose product neither overflows nor underflows.
        public static double Fma(double a, double b, double c)
        {
            double p = a * b;
            if (p == 0 || c == 0 || double.IsNaN(p) || double.IsInfinity(p) || double.IsNaN(c) || double.IsInfinity(c)) return p + c;
            // p + e = a * b exactly (Dekker / Veltkamp)
            double t = Splitter * a, ah = t - (t - a), al = a - ah;
            t = Splitter * b; double bh = t - (t - b), bl = b - bh;
            double e = ((ah * bh - p) + ah * bl + al * bh) + al * bl;
            // s + r = p + c exactly (Knuth)
            double s = p + c, bb = s - p, r = (p - (s - bb)) + (c - bb);
            // v = r + e rounded to odd
            double v = r + e, vb = v - r, ve = (r - (v - vb)) + (e - vb);
            if (ve != 0)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                if ((bits & 1) == 0) v = BitConverter.Int64BitsToDouble((ve > 0) == (v > 0) ? bits + 1 : bits - 1);
            }
            return s + v;
        }

        /// One element of einsum("ij,jk->ik") / of a 3 column matmul: the sum starts at +0.
        public static double Chain3(double a0, double b0, double a1, double b1, double a2, double b2)
            => Fma(a2, b2, Fma(a1, b1, a0 * b0 + 0.0));

        public static double Chain4(double a0, double b0, double a1, double b1, double a2, double b2, double a3, double b3)
            => Fma(a3, b3, Fma(a2, b2, Fma(a1, b1, a0 * b0 + 0.0)));

        static double Pair4(double a0, double b0, double a1, double b1, double a2, double b2, double a3, double b3)
            => Fma(a2, b2, a0 * b0 + 0.0) + Fma(a3, b3, a1 * b1 + 0.0);

        /// v (3) times the 3 x 3 matrix m (row-major, 9 values; rows are the basis vectors): out_k = sum_j v_j m[j, k].
        public static void RowMat3(double x, double y, double z, double[] m, out double ox, out double oy, out double oz)
        {
            ox = Chain3(x, m[0], y, m[3], z, m[6]);
            oy = Chain3(x, m[1], y, m[4], z, m[7]);
            oz = Chain3(x, m[2], y, m[5], z, m[8]);
        }

        /// a @ b for two 4 x 4 matrices (row-major 16).
        public static double[] Mul4(double[] a, double[] b)
        {
            var o = new double[16];
            for (int i = 0; i < 4; i++)
                for (int k = 0; k < 4; k++)
                    o[i * 4 + k] = Pair4(a[i * 4], b[k], a[i * 4 + 1], b[4 + k], a[i * 4 + 2], b[8 + k], a[i * 4 + 3], b[12 + k]);
            return o;
        }

        /// rows (n x 4, row-major) @ b (4 x 4): numpy's matmul on Accelerate, by row count.
        public static double[] RowsMul4(double[] rows, int n, double[] b)
        {
            var o = new double[n * 4];
            for (int i = 0; i < n; i++)
            {
                bool chain = n >= 400 || ((n & 1) == 1 && i == n - 1);
                for (int k = 0; k < 4; k++)
                {
                    double a0 = rows[i * 4], a1 = rows[i * 4 + 1], a2 = rows[i * 4 + 2], a3 = rows[i * 4 + 3];
                    o[i * 4 + k] = chain ? Chain4(a0, b[k], a1, b[4 + k], a2, b[8 + k], a3, b[12 + k]) : Pair4(a0, b[k], a1, b[4 + k], a2, b[8 + k], a3, b[12 + k]);
                }
            }
            return o;
        }

        /// np.linalg.inv of a 4 x 4 matrix: LU with partial pivoting and two triangular solves. NOT bit-exact with Accelerate's LAPACK
        /// (about a third of random matrices agree in every bit, the rest differ in the last places); null where the matrix is singular.
        public static double[] Inv4(double[] m)
        {
            const int n = 4;
            var a = (double[])m.Clone(); var piv = new int[n];
            for (int i = 0; i < n; i++) piv[i] = i;
            for (int k = 0; k < n; k++)
            {
                int p = k; double best = Math.Abs(a[k * n + k]);
                for (int i = k + 1; i < n; i++) if (Math.Abs(a[i * n + k]) > best) { best = Math.Abs(a[i * n + k]); p = i; }
                if (best == 0 || double.IsNaN(best)) return null;
                if (p != k)
                {
                    for (int j = 0; j < n; j++) { double t = a[k * n + j]; a[k * n + j] = a[p * n + j]; a[p * n + j] = t; }
                    int ti = piv[k]; piv[k] = piv[p]; piv[p] = ti;
                }
                double r = 1.0 / a[k * n + k];
                for (int i = k + 1; i < n; i++) a[i * n + k] = a[i * n + k] * r;
                for (int i = k + 1; i < n; i++)
                    for (int j = k + 1; j < n; j++) a[i * n + j] = Fma(-a[i * n + k], a[k * n + j], a[i * n + j]);
            }
            var x = new double[16]; var b = new double[n];
            for (int c = 0; c < n; c++)
            {
                for (int i = 0; i < n; i++) b[i] = piv[i] == c ? 1.0 : 0.0;
                for (int k = 0; k < n; k++)
                    for (int i = k + 1; i < n; i++) b[i] = Fma(-b[k], a[i * n + k], b[i]);
                for (int k = n - 1; k >= 0; k--)
                {
                    b[k] = b[k] * (1.0 / a[k * n + k]);
                    for (int i = 0; i < k; i++) b[i] = Fma(-b[k], a[i * n + k], b[i]);
                }
                for (int i = 0; i < n; i++) x[i * n + c] = b[i];
            }
            return x;
        }

        public static double Norm3(double x, double y, double z) => Math.Sqrt((x * x + y * y) + z * z);
        public static double Norm4(double a, double b, double c, double d) => Math.Sqrt((a * a + c * c) + (b * b + d * d));

        public static void Cross(double[] a, double[] b, double[] o)
        {
            double x = a[1] * b[2] - a[2] * b[1], y = a[2] * b[0] - a[0] * b[2], z = a[0] * b[1] - a[1] * b[0];
            o[0] = x; o[1] = y; o[2] = z;
        }

        /// The frame of a placed model: rows right, up, forward (scenery.py / toys.py / stadium_lights.py). look and up are the raw
        /// vectors; NaN where look or the cross product has no length, as numpy's division gives.
        public static double[] Frame(double[] look, double[] upIn)
        {
            double n = Norm3(look[0], look[1], look[2]);
            var f = new[] { look[0] / n, look[1] / n, look[2] / n };
            var right = new double[3]; Cross(upIn, f, right);
            n = Norm3(right[0], right[1], right[2]);
            right[0] /= n; right[1] /= n; right[2] /= n;
            var up = new double[3]; Cross(f, right, up);
            return new[] { right[0], right[1], right[2], up[0], up[1], up[2], f[0], f[1], f[2] };
        }

        /// struct.pack('f', x): the nearest single (Python raises OverflowError for a finite value out of range).
        public static float F32(double x)
        {
            float f = (float)x;
            if (float.IsInfinity(f) && !double.IsInfinity(x)) throw new UnValueError("float too large to pack with f format");
            return f;
        }

        public static void PutF32(byte[] b, int at, double x) => PutF32Raw(b, at, F32(x));

        public static void PutF32Raw(byte[] b, int at, float f)
        {
            int v = BitConverter.SingleToInt32Bits(f);
            b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24);
        }

        public static void PutI32(byte[] b, int at, int v)
        {
            b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24);
        }

        public static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        /// float(text) as the converter's number() / vec() helpers need it: false where Python raises ValueError.
        public static bool TryFloat(string text, out double v)
        {
            v = 0;
            if (text == null) return false;
            string s = UnIni.Strip(text);
            string low = s.ToLowerInvariant();
            string body = low.StartsWith("+", StringComparison.Ordinal) || low.StartsWith("-", StringComparison.Ordinal) ? low.Substring(1) : low;
            if (body == "inf" || body == "infinity") { v = low[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity; return true; }
            if (body == "nan") { v = double.NaN; return true; }
            // Python's grammar: digits with single underscores between them, an optional fraction and exponent
            var sb = new StringBuilder(s.Length); bool digit = false, any = false, point = false, exp = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { sb.Append(c); digit = true; any = true; }
                else if (c == '_') { if (!digit || i + 1 >= s.Length || s[i + 1] < '0' || s[i + 1] > '9') return false; }
                else if ((c == '+' || c == '-') && (i == 0 || s[i - 1] == 'e' || s[i - 1] == 'E')) { sb.Append(c); digit = false; }
                else if (c == '.' && !point && !exp) { sb.Append(c); point = true; digit = false; }
                else if ((c == 'e' || c == 'E') && !exp && any) { sb.Append(c); exp = true; digit = false; any = false; }
                else return false;
            }
            if (!any) return false;
            try { v = UnJson.Float(sb.ToString()); return true; }
            catch (Exception) { return false; }
        }

        /// float(text): UnValueError where Python raises ValueError.
        public static double Float(string text)
        {
            if (!TryFloat(text, out double v)) throw new UnValueError("could not convert string to float: '" + text + "'");
            return v;
        }

        /// [float(x) for x in text.split(',')].
        public static double[] Floats(string text)
        {
            var parts = text.Split(',');
            var r = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = Float(parts[i]);
            return r;
        }

        /// int(x) of a float: towards zero (Python raises for NaN and infinities).
        public static int Int(double x)
        {
            if (!Finite(x)) throw new UnValueError("cannot convert float " + (double.IsNaN(x) ? "NaN" : "infinity") + " to integer");
            return checked((int)Math.Truncate(x));
        }

        /// int(text, 0): Python's integer literal (decimal without leading zeros, 0x / 0o / 0b, underscores, a sign); false = ValueError.
        public static bool TryIntLiteral(string text, out long v)
        {
            v = 0;
            string s = UnIni.Strip(text ?? "");
            bool neg = false; int p = 0;
            if (p < s.Length && (s[p] == '+' || s[p] == '-')) { neg = s[p] == '-'; p++; }
            int radix = 10;
            if (p + 1 < s.Length && s[p] == '0')
            {
                char c = char.ToLowerInvariant(s[p + 1]);
                if (c == 'x') radix = 16; else if (c == 'o') radix = 8; else if (c == 'b') radix = 2;
                if (radix != 10) { p += 2; if (p < s.Length && s[p] == '_') p++; }
            }
            if (p >= s.Length) return false;
            BigInteger acc = BigInteger.Zero; bool prevDigit = false, anyNonZero = false; int digits = 0; bool leadingZero = radix == 10 && s[p] == '0';
            for (; p < s.Length; p++)
            {
                char c = s[p];
                if (c == '_') { if (!prevDigit || p + 1 >= s.Length) return false; prevDigit = false; continue; }
                int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'z' ? c - 'a' + 10 : c >= 'A' && c <= 'Z' ? c - 'A' + 10 : 99;
                if (d >= radix) return false;
                if (d != 0) anyNonZero = true;
                acc = acc * radix + d; prevDigit = true; digits++;
            }
            if (!prevDigit) return false;
            if (leadingZero && anyNonZero) return false;   // "010" is not a literal; "0", "00" are
            if (acc > long.MaxValue) throw new UnValueError("integer literal out of range: " + text);
            v = neg ? -(long)acc : (long)acc;
            return true;
        }

        /// round(x, n) of a numpy float64 scalar (np.round): rint(x * 10^n) / 10^n, ties to even.
        public static double NpRound(double x, int n)
        {
            double f = 1; for (int i = 0; i < n; i++) f *= 10;
            return Math.Round(x * f, MidpointRounding.ToEven) / f;
        }

        // ------------------------------------------------------------------------------------------------ exact rounding of rationals

        /// The nearest single (ties to even) of the exact rational num / den (den > 0).
        public static float RationalToSingle(BigInteger num, BigInteger den)
        {
            if (num.IsZero) return 0f;
            bool neg = num.Sign < 0; if (neg) num = -num;
            // q = round(num * 2^s / den) with 2^23 <= q < 2^24 where the value is normal (s <= 149: the subnormal grid)
            int s = 24 - (int)(BitLength(num) - BitLength(den));
            for (; ; )
            {
                BigInteger scaled = s >= 0 ? num << s : num, d = s >= 0 ? den : den << -s;
                BigInteger q = BigInteger.DivRem(scaled, d, out BigInteger rem);
                if (q >= (BigInteger.One << 24)) { s--; continue; }
                if (q < (BigInteger.One << 23) && s < 149) { s++; continue; }
                if (s > 149) { s = 149; continue; }
                BigInteger twice = rem << 1;
                int cmp = twice.CompareTo(d);
                if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
                double value = (double)(long)q * Pow2(-s);
                float f = (float)value;
                return neg ? -f : f;
            }
        }

        static long BitLength(BigInteger v)
        {
            if (v.IsZero) return 0;
            byte[] b = v.ToByteArray();   // little-endian two's complement, v > 0
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) top--;
            int bits = 0; for (int x = b[top]; x != 0; x >>= 1) bits++;
            return top * 8L + bits;
        }

        /// 2^k as a double (exact for -1074 <= k <= 1023).
        public static double Pow2(int k)
        {
            if (k > 1023) return double.PositiveInfinity;
            if (k >= -1022) return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
            if (k >= -1074) return BitConverter.Int64BitsToDouble(1L << (k + 1074));
            return 0;
        }

        /// A finite double as mantissa * 2^exponent (mantissa an integer, signed).
        public static void Exact(double x, out BigInteger mantissa, out int exponent)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF); long frac = bits & 0xFFFFFFFFFFFFFL;
            long m = be == 0 ? frac : frac | (1L << 52);
            exponent = be == 0 ? -1074 : be - 1075;
            mantissa = bits < 0 ? -m : m;
        }

        /// The nearest single of the exact product s * f.
        public static float ExactMulToSingle(double s, double f)
        {
            Exact(s, out var m1, out int e1); Exact(f, out var m2, out int e2);
            BigInteger num = m1 * m2; int e = e1 + e2;
            return e >= 0 ? RationalToSingle(num << e, BigInteger.One) : RationalToSingle(num, BigInteger.One << -e);
        }

        /// The nearest single of the exact 1 / s (s finite, not zero).
        public static float ExactRecipToSingle(double s)
        {
            Exact(s, out var m, out int e);
            BigInteger num = BigInteger.One, den = m;
            if (den.Sign < 0) { den = -den; num = -num; }
            return e >= 0 ? RationalToSingle(num, den << e) : RationalToSingle(num << -e, den);
        }

        // ------------------------------------------------------------------------------------------------ Python text

        /// str.splitlines().
        public static List<string> SplitLines(string text)
        {
            var r = new List<string>(); int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool brk = c == '\n' || c == '\r' || c == '\v' || c == '\f' || c == '\x1c' || c == '\x1d' || c == '\x1e' || c == '\x85' || c == (char)0x2028 || c == (char)0x2029;
                if (!brk) continue;
                r.Add(text.Substring(start, i - start));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = i + 1;
            }
            if (start < text.Length) r.Add(text.Substring(start));
            return r;
        }

        /// s.strip(chars).
        public static string StripChars(string s, char c)
        {
            int a = 0, b = s.Length;
            while (a < b && s[a] == c) a++;
            while (b > a && s[b - 1] == c) b--;
            return s.Substring(a, b - a);
        }

        /// latin-1: one byte, one char.
        public static string Latin1(byte[] b, int offset, int count)
        {
            var sb = new StringBuilder(count);
            for (int i = 0; i < count; i++) sb.Append((char)b[offset + i]);
            return sb.ToString();
        }
        public static string Latin1(byte[] b) => Latin1(b, 0, b.Length);

        /// A NUL-terminated latin-1 string in a fixed field (b[o : o + n].split(b"\0", 1)[0]).
        public static string CStr(byte[] b, int offset, int n)
        {
            int len = 0;
            while (len < n && offset + len < b.Length && b[offset + len] != 0) len++;
            return Latin1(b, offset, len);
        }
    }
}
