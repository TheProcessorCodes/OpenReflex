// MX vs ATV Unleashed ecosystem (.esb): port of the reference parser _re/tools/esb.py (parse, inst_world, terrain_sampler).
//   u32 version, 256 species slots (u8 present, then: str mesh, str billboard, 11 floats, 3 x u32, version 2: 9 floats + optional str,
//   u32 collision records x 36 bytes), optional str, (version 2: u32, optional str), u32 instances x { u8 species (bit 7 = snap to the
//   terrain), u16 x, y, z, s8 height, s8 radius }.   (str = u8 length incl. NUL + bytes.)
using System;
using System.Collections.Generic;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnEsbSpecies
    {
        public int slot, index;
        public string mesh, billboard;
        public float meanHeight, minHeight, maxHeight, meanRadius, minRadius, maxRadius, uLeft, uRight, vBottom, vTop, uCenter;
    }

    public struct UnEsbInstance
    {
        public int species;      // index into Species, -1 where the slot is absent
        public bool snap;
        public int x, y, z, h, rad;
    }

    public sealed class UnEsb
    {
        public long version;
        public readonly List<UnEsbSpecies> species = new List<UnEsbSpecies>();
        public readonly List<UnEsbInstance> instances = new List<UnEsbInstance>();

        sealed class R
        {
            public readonly byte[] b; public int o;
            public R(byte[] b) { this.b = b; }
            void Need(int n) { if (n < 0 || o + n > b.Length) throw new UnValueError("Unleashed ecosystem: truncated at " + o); }
            public int U8() { Need(1); return b[o++]; }
            public int S8() { Need(1); return (sbyte)b[o++]; }
            public int U16() { Need(2); int v = b[o] | b[o + 1] << 8; o += 2; return v; }
            public uint U32() { Need(4); uint v = BitConverter.ToUInt32(b, o); o += 4; return v; }
            public float F32() { Need(4); float v = BitConverter.ToSingle(b, o); o += 4; return v; }
            public void Skip(int n) { o += n; }
            public string PStr() { int n = U8(); string s = UnNum.CStr(b, Math.Min(o, b.Length), n); o += n; return s; }
            public string OptStr() => U8() != 0 ? PStr() : null;
        }

        /// esb.parse(b).
        public static UnEsb Parse(byte[] b)
        {
            var r = new R(b);
            var e = new UnEsb { version = r.U32() };
            var slotToIndex = new Dictionary<int, int>();
            for (int slot = 0; slot < 256; slot++)
            {
                if (r.U8() == 0) continue;
                var s = new UnEsbSpecies { slot = slot, index = e.species.Count, mesh = r.PStr(), billboard = r.PStr() };
                s.meanHeight = r.F32(); s.minHeight = r.F32(); s.maxHeight = r.F32(); s.meanRadius = r.F32(); s.minRadius = r.F32(); s.maxRadius = r.F32();
                s.uLeft = r.F32(); s.uRight = r.F32(); s.vBottom = r.F32(); s.vTop = r.F32(); s.uCenter = r.F32();
                r.U32(); r.U32(); r.Skip(4);
                if (e.version == 2) { for (int i = 0; i < 9; i++) r.F32(); r.OptStr(); }
                uint nc = r.U32();
                for (uint i = 0; i < nc; i++) { r.U32(); r.Skip(24); r.F32(); r.F32(); }
                slotToIndex[slot] = s.index;
                e.species.Add(s);
            }
            r.OptStr();
            if (e.version == 2) { r.U32(); r.OptStr(); }
            uint n = r.U32();
            for (uint i = 0; i < n; i++)
            {
                int sp = r.U8(); int x = r.U16(), y = r.U16(), z = r.U16(); int h = r.S8(), rad = r.S8();
                e.instances.Add(new UnEsbInstance { species = slotToIndex.TryGetValue(sp & 0x7f, out int si) ? si : -1, snap = (sp & 0x80) != 0, x = x, y = y, z = z, h = h, rad = rad });
            }
            return e;
        }

        /// e["species"][si] with Python's indexing (-1 = the last one).
        public UnEsbSpecies SpeciesAt(int si)
        {
            if (si < 0) si += species.Count;
            if (si < 0 || si >= species.Count) throw new UnValueError("Unleashed ecosystem: an instance of a species the file does not have");
            return species[si];
        }

        /// esb.inst_world(e, inst, world_size): x, y, z, height, radius in the source's units.
        public void World(UnEsbInstance inst, double worldSize, out double x, out double y, out double z, out double height, out double radius)
        {
            double q = worldSize / 65536.0;
            var s = SpeciesAt(inst.species);
            height = s.meanHeight + ((double)s.meanHeight - s.minHeight) * inst.h / 128.0;
            radius = s.meanRadius + ((double)s.maxRadius - s.meanRadius) * inst.rad / 128.0;
            x = inst.x * q; y = inst.y * q; z = inst.z * q;
        }
    }

    /// esb.terrain_sampler(trn bytes): the source terrain's own heights, bilinear, null outside it.
    public sealed class UnEsbGround
    {
        readonly double[] _grid; readonly bool[] _set; readonly int _w, _h; readonly double _cs;

        public UnEsbGround(byte[] trn)
        {
            var t = UnTrn.Parse(trn);
            t.GridSize(out _w, out _h);
            _grid = new double[_w * _h]; _set = new bool[_w * _h]; _cs = t.cellSize;
            var hs = new double[289];
            foreach (var n in t.nodes)
            {
                if (n.level != 0) continue;
                t.NodeHeights(n, hs);
                for (int vz = 0; vz < 17; vz++)
                    for (int vx = 0; vx < 17; vx++)
                    {
                        int at = (n.z + vz) * _w + n.x + vx;
                        _grid[at] = hs[vz * 17 + vx] * _cs; _set[at] = true;
                    }
            }
        }

        public double? Sample(double x, double z)
        {
            double gx = x / _cs, gz = z / _cs;
            if (double.IsNaN(gx) || double.IsNaN(gz) || Math.Abs(gx) > 1e9 || Math.Abs(gz) > 1e9) return null;
            int ix = (int)gx, iz = (int)gz;   // int(): towards zero
            if (!(0 <= ix && ix < _w - 1 && 0 <= iz && iz < _h - 1)) return null;
            int a = iz * _w + ix;
            if (!_set[a] || !_set[a + 1] || !_set[a + _w] || !_set[a + _w + 1]) return null;
            double fx = gx - ix, fz = gz - iz;
            return (_grid[a] * (1 - fx) + _grid[a + 1] * fx) * (1 - fz) + (_grid[a + _w] * (1 - fx) + _grid[a + _w + 1] * fx) * fz;
        }
    }
}
