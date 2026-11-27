// MX vs ATV Unleashed motions, the part the scenery converter uses of the reference _re/tools/mot.py: a .mot file (parse_mot), the
// .mcf motion list (parse_mcf) and the game's own key sampling (rot_eval, pos_eval) in single precision, operation by operation.
//   .mot   i32 -version (or the frame count of a version 1 file), [v5+: u32], i32 frames, [v3+: i32 bones x { u32 n, bytes }],
//          f32 duration, i32 rotation type (0 byte axis, 1 short axis), i32 position type (0 linear, 1 bezier), i32 position channels,
//          i32 rotation channels, [v6+: i32 float channels], [v4+: i32 actions], the channels { i32 keys, i32 bone, keys }, ...
// The sine / cosine / arc cosine tables are built with the platform's libm in double precision and rounded to single, as the reference
// builds them; a libm that rounds a value differently in its last place could change a table entry (not seen on macOS / arm64).
using System;
using System.Collections.Generic;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnMotRotKey { public float t; public int a, x, y, z; }

    public sealed class UnMotRotState { public int cache = 1; }

    public sealed class UnMotion
    {
        public string name = "";
        public int version = 1, frames;
        public readonly List<string> bones = new List<string>();
        public float duration;
        public int rotType, posType;
        public readonly List<KeyValuePair<int, float[][]>> pos = new List<KeyValuePair<int, float[][]>>();          // bone, keys (4 or 10 floats)
        public readonly List<KeyValuePair<int, UnMotRotKey[]>> rot = new List<KeyValuePair<int, UnMotRotKey[]>>();   // bone, keys
    }

    public sealed class UnMcfMotion { public int index; public string name, vue; public int loop; public double transition; }

    public static class UnMot
    {
        // ---- single precision, one rounding per operation (the casts keep that true on a runtime that widens float expressions)
        public static float F32(double x) => (float)x;
        static float Fadd(float a, float b) => (float)((double)a + b);
        static float Fsub(float a, float b) => (float)((double)a - b);
        static float Fmul(float a, float b) => (float)((double)a * b);
        static float Fdiv(float a, float b)
        {
            if (b != 0) return (float)((double)a / b);
            if (a != 0) return (double)a * 1 >= 0 ? float.PositiveInfinity : float.NegativeInfinity;   // copysign(inf, a * (1 if b >= 0 else -1)): b = +-0 is >= 0
            return float.NaN;
        }

        static float Bits(uint u) => BitConverter.Int32BitsToSingle(unchecked((int)u));

        static readonly float KShortStep = Bits(0x3a490fdb), KShortScale = Bits(0x38000100), KByteStep = Bits(0x3c490fdb), KByteScale = Bits(0x3c010204);
        static readonly float AcosScale = Bits(0x466a5e00);
        static float[] _shortSin, _shortCos, _byteSin, _byteCos, _acos;
        static readonly object _lock = new object();

        static void ShortTab()
        {
            lock (_lock)
            {
                if (_shortSin != null) return;
                var s = new float[4096]; var c = new float[4096];
                for (int i = 0; i < 4096; i++) { double a = i * (double)KShortStep; s[i] = (float)(Math.Sin(a) * KShortScale); c[i] = (float)Math.Cos(a); }
                _shortCos = c; _shortSin = s;
            }
        }
        static void ByteTab()
        {
            lock (_lock)
            {
                if (_byteSin != null) return;
                var s = new float[256]; var c = new float[256];
                for (int i = 0; i < 256; i++) { double a = i * (double)KByteStep; s[i] = (float)(Math.Sin(a) * KByteScale); c[i] = (float)Math.Cos(a); }
                _byteCos = c; _byteSin = s;
            }
        }
        static float[] AcosTab()
        {
            lock (_lock)
            {
                if (_acos != null) return _acos;
                double c = (float)6.666888657e-05;
                var t = new float[30001];
                for (int i = 0; i < 30000; i++)
                {
                    double xd = i * c - 1.0; float x = (float)xd;
                    if (xd > 1.0) x = 1.0f;
                    t[i] = (float)Math.Acos(x);
                }
                t[30000] = t[29999];
                return _acos = t;
            }
        }

        static float Acos402810(float x)
        {
            if (1.0f < x) x = 1.0f; else if (x < -1.0f) x = -1.0f;
            float t = Fmul(Fadd(x, 1.0f), AcosScale);
            int i = (int)t;
            float fr = Fsub(t, i);
            var tab = AcosTab();
            return Fadd(Fmul(Fsub(1.0f, fr), tab[i]), Fmul(fr, tab[i + 1]));
        }

        static float[] FlipSign(float[] q)
        {
            if (BitConverter.SingleToInt32Bits(q[3]) < 0) return new[] { -q[0], -q[1], -q[2], -q[3] };
            return q;
        }

        static float[] Decomp(UnMotRotKey k, int rtype)
        {
            float s, c;
            if (rtype == 1)
            {
                ShortTab();
                if (k.a >= 4096) throw new UnValueError("Unleashed motion: rotation angle outside the table");   // Python: IndexError
                s = _shortSin[k.a]; c = _shortCos[k.a];
            }
            else { ByteTab(); s = _byteSin[k.a]; c = _byteCos[k.a]; }
            return FlipSign(new[] { Fmul(k.x, s), Fmul(k.y, s), Fmul(k.z, s), c });
        }

        /// FUN_0045bfb0.
        static float[] Slerp(float[] a, float[] b, float u)
        {
            float d = Fadd(Fadd(Fadd(Fmul(a[2], b[2]), Fmul(a[1], b[1])), Fmul(b[3], a[3])), Fmul(a[0], b[0]));
            float sign = 1.0f;
            if (d < 0.0f) { d = -d; sign = -1.0f; }
            if (d < 1.0f)
            {
                float th = Acos402810(d);
                float inv = UnNum.ExactRecipToSingle(Math.Sin(th));
                float w1 = UnNum.ExactMulToSingle(Math.Sin(Fsub(th, Fmul(th, u))), inv);
                float w2 = Fmul(UnNum.ExactMulToSingle(Math.Sin(Fmul(th, u)), inv), sign);
                return new[] { Fadd(Fmul(w1, a[0]), Fmul(w2, b[0])), Fadd(Fmul(w1, a[1]), Fmul(w2, b[1])), Fadd(Fmul(w1, a[2]), Fmul(w2, b[2])), Fadd(Fmul(w2, b[3]), Fmul(w1, a[3])) };
            }
            return (float[])a.Clone();
        }

        /// rot_eval(keys, rtype, t, st, prev) with the reader's zeroed "key before the array" (time 0): the quaternion x, y, z, w, or
        /// null for a channel without keys.
        public static float[] RotEval(UnMotRotKey[] keys, int rtype, float t, UnMotRotState st)
        {
            int n = keys.Length;
            if (n < 1) return null;
            bool ok = true; float u; int i0, i1;
            if (n < 3)
            {
                if (n == 2) { u = Fdiv(Fsub(t, 0.0f), Fsub(keys[1].t, keys[0].t)); i0 = 0; i1 = 1; }
                else { u = 0.0f; i0 = 0; i1 = 0; }
            }
            else
            {
                int last = n - 1, q = last >> 2, h = (last >> 1) + q;
                if (st.cache > h && t < keys[q].t) st.cache = 1;
                else if (st.cache < q && !(t <= keys[h].t)) st.cache = last;
                int i = Math.Max(1, st.cache);
                if (keys[i].t < t)
                {
                    for (; ; )
                    {
                        i++;
                        if (n <= i) { ok = false; i = last; break; }
                        if (!(keys[i].t < t)) break;
                    }
                }
                while (t < keys[i - 1].t)
                {
                    i--;
                    if (i < 1) { i = 1; ok = false; break; }
                }
                st.cache = i;
                u = Fdiv(Fsub(t, keys[i - 1].t), Fsub(keys[i].t, keys[i - 1].t));
                if (!ok) return Decomp(keys[i], rtype);
                i0 = i - 1; i1 = i;
            }
            return Slerp(Decomp(keys[i0], rtype), Decomp(keys[i1], rtype), u);
        }

        /// FUN_004544c0 / FUN_004547d0.
        static int Search(float[][] keys, int n, float t)
        {
            if (n == 0) return 0;
            if (t < keys[n - 1][0])
            {
                if (t < keys[0][0]) return 0;
                int lo = 0;
                for (; ; )
                {
                    int mid;
                    for (; ; )
                    {
                        mid = (n + lo) >> 1;
                        if (t < keys[mid][0]) break;
                        lo = mid;
                        if (t < keys[mid + 1][0]) return mid + 1;
                    }
                    n = mid;
                    if (!(t < keys[mid - 1][0])) return mid;
                }
            }
            return n;
        }

        /// pos_eval(keys, ptype, t, out, prev) with the reader's zeroed "key before the array": o (x, y, z, w) is written. False where
        /// the game leaves z as it was (the one-key linear path), true otherwise.
        public static bool PosEval(float[][] keys, int ptype, float t, float[] o)
        {
            int n = keys.Length;
            var prev = new float[ptype == 0 ? 4 : 10];
            if (n > 1)
            {
                int i = Search(keys, n, t);
                float[] k0, k1; float u; bool interp;
                if (i != n) { k0 = i - 1 < 0 ? prev : keys[i - 1]; k1 = keys[i]; u = Fdiv(Fsub(t, k0[0]), Fsub(k1[0], k0[0])); interp = true; }
                else { k0 = keys[n - 2]; k1 = keys[n - 1]; u = Fdiv(Fsub(t, k1[0]), Fsub(keys[i - 1][0], keys[i - 2][0])); interp = false; }
                if (interp && ptype == 1)
                {
                    float s2 = Fmul(u, u), s3 = Fmul(u, s2);
                    float h1 = Fsub(Fmul(s2, 3.0f), Fadd(s3, s3)), h0 = Fsub(1.0f, h1), h3 = Fsub(s3, s2), h2 = Fadd(Fsub(h3, s2), u);
                    for (int c = 0; c < 3; c++)
                        o[c] = Fadd(Fadd(Fadd(Fmul(h2, k0[7 + c]), Fmul(h3, k1[4 + c])), Fmul(h0, k0[1 + c])), Fmul(h1, k1[1 + c]));
                    o[3] = 1.0f;
                    return true;
                }
                var bas = interp ? k0 : k1;
                for (int c = 0; c < 3; c++) o[c] = Fadd(Fmul(Fsub(k1[1 + c], k0[1 + c]), u), bas[1 + c]);
                o[3] = 1.0f;
                return true;
            }
            int j = Search(keys, n, t);
            var k = j - 1 < 0 ? prev : keys[j - 1];
            o[3] = 1.0f;
            if (ptype == 0) { o[0] = k[1]; o[1] = k[2]; o[0] = k[3]; return false; }   // the game writes z into x and leaves z (kept)
            o[0] = k[1]; o[1] = k[2]; o[2] = k[3];
            return true;
        }

        // ------------------------------------------------------------------------------------------------ readers

        sealed class R
        {
            public readonly byte[] b; public int o;
            public R(byte[] b) { this.b = b; }
            void Need(long n) { if (n < 0 || o + n > b.Length) throw new UnValueError("read past EOF at 0x" + o.ToString("x") + " (+" + n + ")"); }
            public uint U32() { Need(4); uint v = BitConverter.ToUInt32(b, o); o += 4; return v; }
            public int I32() { Need(4); int v = BitConverter.ToInt32(b, o); o += 4; return v; }
            public float F() { Need(4); float v = BitConverter.ToSingle(b, o); o += 4; return v; }
            public int Raw(long n) { Need(n); int at = o; o += (int)n; return at; }
        }

        /// mot.parse_mot(b, name): the motion (float channels and actions are skipped over; the converter does not read them).
        public static UnMotion ParseMot(byte[] b, string name)
        {
            var m = new UnMotion { name = name };
            var r = new R(b);
            int v0 = r.I32(), ver = 1;
            if (v0 < 0)
            {
                ver = -v0;
                if (v0 == -3 || v0 == -4 || ver > 5 || v0 == -5)
                {
                    if (ver >= 5) r.U32();
                    m.frames = r.I32();
                    int nb = r.I32();
                    for (int i = 0; i < nb; i++)
                    {
                        uint n = r.U32(); int at = r.Raw(n);
                        m.bones.Add(UnNum.CStr(b, at, (int)n));
                    }
                }
                else m.frames = r.I32();
            }
            else m.frames = r.I32();
            m.version = ver;
            m.duration = r.F();
            m.rotType = r.I32(); m.posType = r.I32();
            int npos = r.I32(), nrot = r.I32();
            if (ver > 5) r.I32();
            if (ver > 3) r.I32();
            for (int c = 0; c < npos; c++)
            {
                int n = r.I32(), bone = r.I32();
                float[][] ks;
                if (m.posType == 0 || m.posType == 1)
                {
                    int size = m.posType == 0 ? 16 : 40, fl = size / 4;
                    if (n < 0) throw new UnValueError("Unleashed motion: key count");   // (Python reads a negative count as a slice from the end)
                    int at = r.Raw((long)n * size);
                    ks = new float[n][];
                    for (int i = 0; i < n; i++) { ks[i] = new float[fl]; for (int k = 0; k < fl; k++) ks[i][k] = BitConverter.ToSingle(b, at + i * size + 4 * k); }
                }
                else ks = new float[0][];
                m.pos.Add(new KeyValuePair<int, float[][]>(bone, ks));
            }
            for (int c = 0; c < nrot; c++)
            {
                int n = r.I32(), bone = r.I32();
                if (ver < 2) throw new UnValueError("version 1 rotation keys (5 floats, compressed at load by 004030b0) not supported");
                UnMotRotKey[] ks;
                if (m.rotType == 0 || m.rotType == 1)
                {
                    int size = m.rotType == 1 ? 12 : 8;
                    if (n < 0) throw new UnValueError("Unleashed motion: key count");
                    int at = r.Raw((long)n * size);
                    ks = new UnMotRotKey[n];
                    for (int i = 0; i < n; i++)
                    {
                        int p = at + i * size;
                        var k = new UnMotRotKey { t = BitConverter.ToSingle(b, p) };
                        if (m.rotType == 1) { k.a = b[p + 4] | b[p + 5] << 8; k.x = (short)(b[p + 6] | b[p + 7] << 8); k.y = (short)(b[p + 8] | b[p + 9] << 8); k.z = (short)(b[p + 10] | b[p + 11] << 8); }
                        else { k.a = b[p + 4]; k.x = (sbyte)b[p + 5]; k.y = (sbyte)b[p + 6]; k.z = (sbyte)b[p + 7]; }
                        ks[i] = k;
                    }
                }
                else ks = new UnMotRotKey[0];
                m.rot.Add(new KeyValuePair<int, UnMotRotKey[]>(bone, ks));
            }
            return m;
        }

        /// mot.parse_mcf(text): the [General Info] keys (lower-cased) and the motion list.
        public static List<UnMcfMotion> ParseMcf(string text, out Dictionary<string, string> general)
        {
            var secs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Dictionary<string, string> cur = null;
            foreach (string raw in UnNum.SplitLines(text))
            {
                string line = raw; int semi = line.IndexOf(';'); if (semi >= 0) line = line.Substring(0, semi);
                line = UnIni.Strip(line);
                if (line.Length == 0) continue;
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close < 0) throw new UnValueError("substring not found");
                    string name = UnIni.Lower(UnIni.Strip(line.Substring(1, close - 1)));
                    if (!secs.TryGetValue(name, out cur)) secs[name] = cur = new Dictionary<string, string>(StringComparer.Ordinal);
                }
                else
                {
                    int eq = line.IndexOf('=');
                    if (eq >= 0 && cur != null)
                    {
                        string k = UnIni.Lower(UnIni.Strip(line.Substring(0, eq)));
                        if (!cur.ContainsKey(k)) cur[k] = UnIni.Strip(line.Substring(eq + 1));
                    }
                }
            }
            if (!secs.TryGetValue("general info", out general)) general = new Dictionary<string, string>(StringComparer.Ordinal);
            string count = general.TryGetValue("numberofmotions", out var cn) ? cn : "0";
            if (!UnNum.TryIntLiteral(count, out long nmot)) throw new UnValueError("invalid literal for int() with base 0: '" + count + "'");
            var list = new List<UnMcfMotion>();
            for (int i = 1; i <= nmot; i++)
            {
                if (!secs.TryGetValue("motion " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), out var s)) s = new Dictionary<string, string>();
                string lp = (s.TryGetValue("looping", out var l) ? l : "0").ToUpperInvariant();
                int loop;
                if (lp.StartsWith("T", StringComparison.Ordinal) || lp.StartsWith("Y", StringComparison.Ordinal) || lp.StartsWith("ON", StringComparison.Ordinal)) loop = 1;
                else if (lp.StartsWith("F", StringComparison.Ordinal) || lp.StartsWith("N", StringComparison.Ordinal) || lp.StartsWith("OFF", StringComparison.Ordinal)) loop = 0;
                else
                {
                    if (!UnNum.TryIntLiteral(lp.Length == 0 ? "0" : lp, out long lv)) throw new UnValueError("invalid literal for int() with base 0: '" + lp + "'");
                    loop = lv != 0 ? (int)Math.Max(Math.Min(lv, int.MaxValue), int.MinValue) : 0;
                }
                string tt = s.TryGetValue("transitiontime", out var tv) ? tv : "0";
                double transition = tt.Length == 0 ? 0 : UnNum.Float(tt);
                list.Add(new UnMcfMotion { index = i, name = s.TryGetValue("name", out var nm) ? nm : "", vue = s.TryGetValue("vue", out var vu) ? vu : "", loop = loop, transition = transition });
            }
            return list;
        }
    }
}
