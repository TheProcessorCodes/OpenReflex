// MX vs ATV Unleashed .dxg (static / skinned mesh): port of the reference parser _re/tools/dxg.py (parse, decode, strip_tris).
//   "dxg" / "psg", u32 version, u32 nodes x { char[128] name, f32[16] matrix, f32[3] centre, f32[3] extent, f32 radius, i32 parent },
//   u32 textures x char[64], u8 groups, u8 skin flags, u8, u8, groups x { f32 lod distance, u32 surfaces x surface },
//   skinned (flag bit 0 or more than one node): u32 palettes (bit 31 explicit, bit 30 zero) x { u32 n, u8[n] }, a palette byte per surface.
// A surface: u32 material, u32 flags, (flags & 0x2000: f32[8] quantisation scales), 6 bytes, extra floats, (flags & 0x2200: 16 type
// bytes), u16 vertices, u16 indices, the strip, then one array per attribute slot (Xbox D3DVSDT_* types).
// Values are decoded as Python decodes them: every number a double (the stored integer or single times the stream scale).
using System;
using System.Collections.Generic;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnDxgNode
    {
        public string name;
        public float[] matrix;   // 16, row-major
        public int parent;
    }

    public sealed class UnDxgSurface
    {
        public long material; public uint flags;
        public bool hasQuant; public float posScale, nrmScale, uvScale;
        public int nverts; public int[] indices;
        public int[] types;
        /// slot -> (type, bytes), only the slots the file holds.
        public readonly Dictionary<int, KeyValuePair<int, byte[]>> attrs = new Dictionary<int, KeyValuePair<int, byte[]>>();
        /// The surface's palette index; null where the file has none (Python: the key is missing or None).
        public int? palette;

        public bool Has(int slot) => attrs.ContainsKey(slot);
        /// quant.get('pos_scale', 1) / quant.get('uv_scale', 1).
        public double PosScale => hasQuant ? posScale : 1.0;
        public double UvScale => hasQuant ? uvScale : 1.0;
    }

    public sealed class UnDxgGroup
    {
        public float lodDist;
        public readonly List<UnDxgSurface> surfaces = new List<UnDxgSurface>();
    }

    public sealed class UnDxg
    {
        public string magic; public long version;
        public readonly List<UnDxgNode> nodes = new List<UnDxgNode>();
        public readonly List<string> textures = new List<string>();
        public readonly List<UnDxgGroup> groups = new List<UnDxgGroup>();
        /// null where the model is not skinned (Python: no 'palettes' key).
        public List<byte[]> palettes;
        public int end;

        static readonly int[] DefaultTypes = { 0x32, 0x32, 0x40, 0x22, 0x12, 2, 2, 2, 0x42, 0x42, 0x22, 2, 2, 2, 2, 2 };
        static readonly int[] FlagMask = { 0x1, 0x2, 0x8, 0x4, 0x10, 0x20, 0x40, 0x80, 0x1000, 0x1000 };
        static readonly int[] FlagSlot = { 0, 1, 2, 3, 10, 13, 4, 5, 8, 9 };
        static readonly int[] ReadOrder = { 0, 1, 2, 3, 4, 8, 9, 10 };

        /// (element size in bytes, components, normalise kind) of a vertex type; size -1 = unknown type, 0 = none.
        public static void TypeInfo(int t, out int elem, out int n, out int norm, out char fmt)
        {
            elem = 0; n = 0; norm = 0; fmt = '\0';
            switch (t)
            {
                case 0x02: return;
                case 0x11: case 0x21: case 0x31: case 0x41: fmt = 'h'; elem = 2; n = t >> 4; norm = 1; return;
                case 0x12: case 0x22: case 0x32: case 0x42: fmt = 'f'; elem = 4; n = t >> 4; norm = 0; return;
                case 0x15: case 0x25: case 0x35: case 0x45: fmt = 'h'; elem = 2; n = t >> 4; norm = 1; return;
                case 0x14: case 0x24: case 0x34: case 0x44: fmt = 'B'; elem = 1; n = t >> 4; norm = 2; return;
                case 0x40: fmt = 'I'; elem = 4; n = 1; norm = 3; return;
                case 0x16: fmt = 'I'; elem = 4; n = 1; norm = 4; return;
                case 0x72: fmt = 'e'; elem = 2; n = 4; norm = 0; return;
                default: elem = -1; return;
            }
        }

        sealed class R
        {
            public readonly byte[] b; public int o;
            public R(byte[] b) { this.b = b; }
            void Need(int n) { if (o + n > b.Length || n < 0) throw new UnValueError("Unleashed model: truncated at " + o); }
            public int U8() { Need(1); return b[o++]; }
            public int U16() { Need(2); int v = b[o] | b[o + 1] << 8; o += 2; return v; }
            public uint U32() { Need(4); uint v = BitConverter.ToUInt32(b, o); o += 4; return v; }
            public int I32() { Need(4); int v = BitConverter.ToInt32(b, o); o += 4; return v; }
            public float F32() { Need(4); float v = BitConverter.ToSingle(b, o); o += 4; return v; }
            /// A slice, short at the end of the file as Python's slice is.
            public byte[] Raw(int n)
            {
                int take = Math.Max(0, Math.Min(n, b.Length - o));
                var r = new byte[take]; Buffer.BlockCopy(b, Math.Min(o, b.Length), r, 0, take); o += n; return r;
            }
            public string CStr(int n) { string s = UnNum.CStr(b, Math.Min(o, b.Length), n); o += n; return s; }
        }

        /// dxg.parse(b).
        public static UnDxg Parse(byte[] b)
        {
            var r = new R(b);
            var magic = r.Raw(4);
            string tag = magic.Length >= 3 ? UnNum.Latin1(magic, 0, 3) : "";
            if (tag != "dxg" && tag != "psg") throw new UnValueError("bad magic");
            var m = new UnDxg { magic = tag, version = r.U32() };
            uint nn = r.U32();
            for (uint i = 0; i < nn; i++)
            {
                var n = new UnDxgNode { name = r.CStr(128), matrix = new float[16] };
                for (int k = 0; k < 16; k++) n.matrix[k] = r.F32();
                for (int k = 0; k < 7; k++) r.F32();
                n.parent = r.I32();
                m.nodes.Add(n);
            }
            uint nt = r.U32();
            for (uint i = 0; i < nt; i++) m.textures.Add(r.CStr(64));
            int ngroups = r.U8(), fl = r.U8(); r.U8(); r.U8();
            bool skinned = (fl & 1) != 0 || m.nodes.Count > 1;
            for (int g = 0; g < ngroups; g++)
            {
                var grp = new UnDxgGroup { lodDist = r.F32() };
                uint ns = r.U32();
                for (uint s = 0; s < ns; s++) grp.surfaces.Add(ParseSurface(r));
                m.groups.Add(grp);
            }
            if (skinned)
            {
                uint cnt = r.U32();
                bool explicitPal = (cnt & 0x80000000) != 0, zero = (cnt & 0x40000000) != 0;
                cnt &= 0x3fffffff;
                m.palettes = new List<byte[]>();
                for (uint i = 0; i < cnt; i++) { uint n = r.U32(); if (n > int.MaxValue) throw new UnValueError("Unleashed model: palette size"); m.palettes.Add(r.Raw((int)n)); }
                foreach (var grp in m.groups)
                    foreach (var s in grp.surfaces)
                        s.palette = explicitPal ? r.U8() : zero ? 0 : (int?)null;
            }
            m.end = r.o;
            return m;
        }

        static UnDxgSurface ParseSurface(R r)
        {
            var s = new UnDxgSurface { material = r.U32() };
            int offset = r.o;
            uint flags = s.flags = r.U32();
            if ((flags & 0x2000) != 0)
            {
                s.hasQuant = true;
                s.posScale = r.F32(); s.nrmScale = r.F32(); s.uvScale = r.F32();
                for (int i = 0; i < 5; i++) r.F32();
            }
            int nf = r.U8(); r.U8(); int pushbuffer = r.U8(); r.U8(); r.U8(); r.U8();
            for (int i = 0; i < nf; i++) r.F32();
            var types = (int[])DefaultTypes.Clone();
            if ((flags & 0x2200) != 0)
            {
                var raw = r.Raw(16);
                if (raw.Length < 16) throw new UnValueError("Unleashed model: truncated vertex types");
                for (int i = 0; i < 16; i++) types[i] = raw[i];
            }
            for (int i = 0; i < FlagMask.Length; i++) if ((flags & FlagMask[i]) == 0) types[FlagSlot[i]] = 2;
            s.types = types;
            int nv = s.nverts = r.U16(), ni = r.U16();
            if (pushbuffer != 0) throw new UnValueError("pushbuffer surface not handled (offset " + offset.ToString("x") + ")");
            if (r.o + 2 * ni > r.b.Length) throw new UnValueError("Unleashed model: truncated strip");
            s.indices = new int[ni];
            for (int i = 0; i < ni; i++) s.indices[i] = r.b[r.o + 2 * i] | r.b[r.o + 2 * i + 1] << 8;
            r.o += 2 * ni;
            foreach (int slot in ReadOrder)
            {
                int t = types[slot];
                if ((slot == 8 || slot == 9) && types[4] != 2) continue;
                TypeInfo(t, out int elem, out int n, out _, out _);
                if (elem < 0) throw new UnValueError("unknown vertex type 0x" + t.ToString("x") + " slot " + slot);
                int sz = elem * n;
                if (sz != 0) s.attrs[slot] = new KeyValuePair<int, byte[]>(t, r.Raw(sz * nv));
            }
            return s;
        }

        /// dxg.decode(t, blob, scale): the values in order (count x components), each as Python computes it; components = n.
        /// Integer kinds (D3DCOLOR, packed normals) come back as their integer values.
        public static double[] Decode(int t, byte[] blob, double scale, out int components)
        {
            TypeInfo(t, out int elem, out int n, out int norm, out char fmt);
            if (elem <= 0) throw new UnValueError("Unleashed model: vertex type 0x" + t.ToString("x") + " has no values");
            if (blob.Length % elem != 0) throw new UnValueError("Unleashed model: vertex data size");   // struct.error
            int count = blob.Length / elem;
            var vals = new double[count];
            for (int i = 0; i < count; i++)
            {
                int o = i * elem; double v;
                switch (fmt)
                {
                    case 'h': v = (short)(blob[o] | blob[o + 1] << 8); break;
                    case 'f': v = BitConverter.ToSingle(blob, o); break;
                    case 'B': v = blob[o]; break;
                    case 'I': v = BitConverter.ToUInt32(blob, o); break;
                    default: v = Half(blob[o] | blob[o + 1] << 8); break;
                }
                if (norm == 2) v = v * scale / 255.0;
                else if (norm == 0 || norm == 1) v = v * scale;
                vals[i] = v;
            }
            components = n;
            return vals;
        }

        static double Half(int h)
        {
            int sign = (h >> 15) & 1, exp = (h >> 10) & 31, frac = h & 1023;
            double v;
            if (exp == 0) v = frac * UnNum.Pow2(-24);
            else if (exp == 31) v = frac == 0 ? double.PositiveInfinity : double.NaN;
            else v = (1024 + frac) * UnNum.Pow2(exp - 25);
            return sign != 0 ? -v : v;
        }

        /// dxg.strip_tris(idx, nverts) flattened: 3 indices per triangle.
        public static int[] StripTris(int[] idx, int nverts)
        {
            if (idx == null || idx.Length == 0) { idx = new int[nverts]; for (int i = 0; i < nverts; i++) idx[i] = i; }
            var o = new List<int>(Math.Max(0, idx.Length - 2) * 3);
            for (int i = 0; i + 2 < idx.Length; i++)
            {
                int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                if (a == b || b == c || a == c) continue;
                if (i % 2 == 0) { o.Add(a); o.Add(b); o.Add(c); } else { o.Add(b); o.Add(a); o.Add(c); }
            }
            return o.ToArray();
        }
    }
}
