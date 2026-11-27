// MX vs ATV Unleashed terrain (Data\Env\<track>\<track>.dx.trn). Two readers, each a port of the reference the Python converter uses for
// that part, because the two references differ in details and the converted bytes follow them:
//   UnTrn        _re/tools/trn.py (parse, node_heights): the height and material grids. Heights are Python floats (doubles); the root
//                node is the one the header's root offset names.
//   UnTrnColour  native/src/trn.c (mxa_trn_parse + mxa_trn_color_rgba): the colour map. Single precision arithmetic, one rounding per
//                operation (the library is built with -ffp-contract=off); the root node is read directly after the colour tiles when
//                they parse, as the game's loader does.
// Layout: "LOD\0" | "PLN\0", '7', "dx \0", char[32] texture format, u32 has_palette, [u8 palette[256][4] BGRA], f32 cell_size, u32 levels,
// u32 root_offset, u32 nA, u32 block_count, u32 block_offset[nA], u32 nC, u32 tile_count, u32 tile_offset[nC], geometry blocks (3408 B
// LOD / 2027 B PLN: u32 base_height, u16 height[289], i8 normal[289][3], u8 normal_index[289], u8 material[289], LOD tables), colour
// tiles, 97-byte grid nodes in a 16x16-ary tree (leaves = level 0 = 17x17 vertex patches).
using System;
using System.Collections.Generic;
using System.IO;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnTrnNode
    {
        public float vscale, hscale;
        public int geom, level, x, z;
    }

    public sealed class UnTrn
    {
        public const int BlockLod = 3408, BlockPln = 2027, NodeSize = 97, MaterialOffset = 4 + 578 + 867 + 289;

        public string magic;
        public float cellSize;           // Python holds it as a double of the same value
        public int levels, blockCount, blockSize;
        public long rootOffset;
        public readonly List<UnTrnNode> nodes = new List<UnTrnNode>();   // depth first, parents before children (trn.py's order)
        byte[] _b;
        int _blocks;                     // offset of block 0

        static void Need(byte[] b, long at, long n, string what)
        {
            if (at < 0 || n < 0 || at + n > b.Length) throw new InvalidDataException("Unleashed terrain: " + what + " lies outside the file");
        }

        static uint U32(byte[] b, long o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static float F32(byte[] b, long o) => BitConverter.Int32BitsToSingle((int)U32(b, o));

        /// trn.parse(b).
        public static UnTrn Parse(byte[] b)
        {
            var t = new UnTrn { _b = b };
            Need(b, 0, 45, "header");
            int n = 0; while (n < 4 && b[n] != 0) n++;
            t.magic = UnPak.Latin1(b, 0, n);
            long o = 41;
            uint hasPal = U32(b, o); o += 4;
            if (hasPal != 0) o += 1024;
            Need(b, o, 20, "header");
            t.cellSize = F32(b, o);
            uint levels = U32(b, o + 4);
            t.rootOffset = U32(b, o + 8);
            uint nA = U32(b, o + 12), nB = U32(b, o + 16);
            o += 20;
            Need(b, o, 4L * nA + 8, "block table");
            o += 4L * nA;
            uint nC = U32(b, o);
            o += 8;
            Need(b, o, 4L * nC, "tile table");
            o += 4L * nC;
            t.blockSize = t.magic == "LOD" ? BlockLod : BlockPln;
            Need(b, o, (long)nB * t.blockSize, "geometry blocks");
            t._blocks = (int)o;
            t.blockCount = (int)nB;
            if (levels > 8) throw new InvalidDataException("Unleashed terrain: " + levels + " tree levels");
            t.levels = (int)levels;
            t.Node(t.rootOffset, t.levels, 0, 0);
            return t;
        }

        void Node(long o, int level, int x, int z)
        {
            Need(_b, o, NodeSize, "grid node");
            var n = new UnTrnNode { vscale = F32(_b, o), hscale = F32(_b, o + 4), geom = _b[o + 18] | _b[o + 19] << 8, level = level, x = x, z = z };
            nodes.Add(n);
            uint childTab = U32(_b, o + 93);
            if (childTab != 0 && level > 0)
            {
                Need(_b, childTab, 1024, "child table");
                for (int i = 0; i < 256; i++)
                {
                    uint co = U32(_b, childTab + 4L * i);
                    if (co != 0) Node(co, level - 1, ((i & 15) + x) * 16, ((i >> 4) + z) * 16);
                }
            }
        }

        int Block(UnTrnNode n)
        {
            if (n.geom >= blockCount) throw new InvalidDataException("Unleashed terrain: node geometry " + n.geom + " of " + blockCount + " blocks");
            return _blocks + n.geom * blockSize;
        }

        /// trn.node_heights(t, n): the node's 17x17 heights in cell units, as Python computes them (double arithmetic on the block's
        /// integers and the node's single precision scales).
        public void NodeHeights(UnTrnNode n, double[] out289)
        {
            int p = Block(n);
            double B = U32(_b, p);
            double vs = n.vscale, hs = n.hscale;
            if (n.vscale == 1.0f)
                for (int i = 0; i < 289; i++) out289[i] = (_b[p + 4 + 2 * i] | _b[p + 5 + 2 * i] << 8) * hs;
            else
                for (int i = 0; i < 289; i++)
                {
                    double h = _b[p + 4 + 2 * i] | _b[p + 5 + 2 * i] << 8;
                    double d = (h - B) * vs;     // one rounding per operation, as Python's float arithmetic
                    d = d + B;
                    out289[i] = d * hs;
                }
        }

        /// The node's 289 material bytes (blocks[geom]['material']); offset into Bytes.
        public int MaterialAt(UnTrnNode n) => Block(n) + MaterialOffset;
        public byte[] Bytes => _b;

        /// (w, h) of the vertex grid the leaves cover: max leaf origin + 17.
        public void GridSize(out int w, out int h)
        {
            w = h = -1;
            foreach (var n in nodes)
            {
                if (n.level != 0) continue;
                if (n.x + 17 > w) w = n.x + 17;
                if (n.z + 17 > h) h = n.z + 17;
            }
            if (w < 0) throw new InvalidDataException("Unleashed terrain: no leaf patches");   // Python: max() of an empty sequence
        }
    }

    /// native/src/trn.c: mxa_trn_parse and mxa_trn_color_rgba.
    public sealed class UnTrnColour
    {
        sealed class Mip { public int w, h; public int data; public int[] sx, sy; }
        sealed class Tile { public int format, mipCount; public Mip[] mips = new Mip[16]; }
        sealed class Node { public int level, shift, x, z; public int[] tiles = new int[17]; }

        byte[] _b;
        readonly byte[] _palette = new byte[1024];   // RGBA
        Tile[] _tiles;
        readonly List<Node> _nodes = new List<Node>();
        int _levels, _tileCount, _blockCount, _gridW, _gridH;

        static uint U32(byte[] b, long o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static float F32(byte[] b, long o) => BitConverter.Int32BitsToSingle((int)U32(b, o));

        /// mxa_trn_parse; null where the library returns NULL (the converter then fails with "Source terrain colour decode failed").
        public static UnTrnColour Parse(byte[] data)
        {
            if (data == null || data.Length < 64) return null;
            var t = new UnTrnColour { _b = data };
            byte[] b = data; long end = b.Length;
            bool lod = b[0] == 'L' && b[1] == 'O' && b[2] == 'D', pln = b[0] == 'P' && b[1] == 'L' && b[2] == 'N';
            if (!lod && !pln) return null;        // kind[3] is forced to NUL before the comparison
            if (b[4] != '7') return null;
            int blockSize = lod ? UnTrn.BlockLod : UnTrn.BlockPln;
            long p = 41;
            uint hasPal = U32(b, p);
            p += 4;
            if (hasPal != 0)
            {
                if (end - p < 1024 + 20) return null;
                for (int i = 0; i < 256; i++)
                {
                    t._palette[i * 4 + 0] = b[p + i * 4 + 2];
                    t._palette[i * 4 + 1] = b[p + i * 4 + 1];
                    t._palette[i * 4 + 2] = b[p + i * 4 + 0];
                    t._palette[i * 4 + 3] = b[p + i * 4 + 3];
                }
                p += 1024;
            }
            if (end - p < 20) return null;
            float cell = F32(b, p);
            int levels = (int)U32(b, p + 4);
            uint root = U32(b, p + 8), nA = U32(b, p + 12), nblocks = U32(b, p + 16);
            p += 20;
            if (nA > 1u << 20 || (ulong)(end - p) < 4ul * nA + 8) return null;
            p += 4L * nA;
            uint nC = U32(b, p), ntiles = U32(b, p + 4);
            p += 8;
            if (nC > 1u << 20 || (ulong)(end - p) < 4ul * nC) return null;
            p += 4L * nC;
            if (levels < 0 || levels > 4 || !(cell > 0)) return null;
            if ((ulong)(end - p) < (ulong)nblocks * (ulong)blockSize) return null;
            t._blockCount = (int)nblocks;
            p += (long)nblocks * blockSize;
            if (ntiles > (ulong)(end - p) / 16) return null;
            t._tiles = new Tile[ntiles + 1];
            bool ok = true;
            for (uint i = 0; i < ntiles && ok; i++)
            {
                if (end - p < 16) { ok = false; break; }
                var tile = new Tile { format = (int)U32(b, p) };
                t._tiles[i] = tile;
                uint nm = U32(b, p + 4);
                p += 16;
                if (nm == 0 || nm > 16) { ok = false; break; }
                tile.mipCount = (int)nm;
                for (uint m = 0; m < nm; m++)
                {
                    if (end - p < 12) { ok = false; break; }
                    uint w = U32(b, p), h = U32(b, p + 4), sz = U32(b, p + 8);
                    p += 12;
                    if ((ulong)(end - p) < sz || (ulong)w * h * (ulong)(tile.format == 200 ? 4 : 1) > sz || w == 0 || h == 0 || w > 16384 || h > 16384) { ok = false; break; }
                    tile.mips[nm - 1 - m] = new Mip { w = (int)w, h = (int)h, data = (int)p };
                    p += sz;
                }
            }
            if (ok) root = (uint)p;
            t._tileCount = ok ? (int)ntiles : 0;
            t._levels = levels;
            if (!t.AddNode(root, levels, levels * 4, 0, 0, 0)) return null;
            int gw = 0, gh = 0, leaves = 0;
            foreach (var n in t._nodes)
            {
                if (n.level != 0) continue;
                leaves++;
                if (n.x + 17 > gw) gw = n.x + 17;
                if (n.z + 17 > gh) gh = n.z + 17;
            }
            if (leaves == 0) return null;
            foreach (var n in t._nodes)
                for (int k = 0; k < 17; k++)
                    if (n.tiles[k] >= t._tileCount) n.tiles[k] = -1;
            t._gridW = gw; t._gridH = gh;
            return t;
        }

        bool AddNode(long off, int level, int shift, int x, int z, int depth)
        {
            if (depth > 8 || off < 0 || off + UnTrn.NodeSize > _b.Length) return false;
            var n = new Node { level = level, shift = shift, x = x, z = z };
            _nodes.Add(n);
            int geom = _b[off + 18] | _b[off + 19] << 8;
            for (int i = 0; i < 17; i++)
            {
                uint v = U32(_b, off + 20 + 4 * i) & 0xfffe;
                n.tiles[i] = v == 0xfffe ? -1 : (int)(v >> 1);
            }
            if (geom >= _blockCount) return false;
            uint childTab = U32(_b, off + 93);
            if (childTab != 0 && level > 0)
            {
                if ((long)childTab + 1024 > _b.Length) return false;
                for (int i = 0; i < 256; i++)
                {
                    uint co = U32(_b, childTab + 4L * i);
                    if (co != 0 && !AddNode(co, level - 1, shift - 4, ((i & 15) + x) * 16, ((i >> 4) + z) * 16, depth + 1)) return false;
                }
            }
            return true;
        }

        /// swz(x, y, w, h) = sx[x] | sy[y]: the x and y bits never share a position.
        static void Swizzle(Mip m)
        {
            if (m.sx != null) return;
            var sx = new int[m.w]; var sy = new int[m.h];
            uint w = (uint)m.w, h = (uint)m.h;
            int bit = 1;
            for (uint xb = 1, yb = 1; xb < w || yb < h;)
            {
                if (xb < w) { for (uint x = 0; x < w; x++) if ((x & xb) != 0) sx[x] |= bit; bit <<= 1; xb <<= 1; }
                if (yb < h) { for (uint y = 0; y < h; y++) if ((y & yb) != 0) sy[y] |= bit; bit <<= 1; yb <<= 1; }
            }
            m.sy = sy; m.sx = sx;
        }

        const float TileInset = 0.03125f, TileSpan = 0.9375f;

        /// mxa_trn_color_rgba(t, w, h): w * h * 4 bytes, rows = increasing z. Every float operation is rounded to single precision on its
        /// own (the casts keep that true on runtimes that evaluate float expressions in double).
        public byte[] ColorRgba(int w, int h, System.Threading.CancellationToken ct = default)
        {
            var rgba = new byte[w * h * 4];
            float cellsW = _gridW - 1, cellsH = _gridH - 1;
            float fw = w, fh = h;
            var c00 = new byte[4]; var c10 = new byte[4]; var c01 = new byte[4]; var c11 = new byte[4];
            for (int level = _levels; level > 0; level--)
                foreach (var n in _nodes)
                {
                    if (n.level != level) continue;
                    ct.ThrowIfCancellationRequested();
                    float span = (float)((float)(16 << n.shift) / 4.0f);
                    for (int k = 0; k < 16; k++)
                    {
                        int ti = n.tiles[1 + k];
                        if (ti < 0) continue;
                        Tile tile = _tiles[ti];
                        float cx0 = (float)((float)(n.x << n.shift) + (float)((k & 3) * span));
                        float cz0 = (float)((float)(n.z << n.shift) + (float)((k >> 2) * span));
                        int x0 = (int)MathF.Ceiling((float)((float)((float)(cx0 / cellsW) * fw) - 0.5f));
                        int x1 = (int)MathF.Ceiling((float)((float)((float)((float)(cx0 + span) / cellsW) * fw) - 0.5f));
                        int y0 = (int)MathF.Ceiling((float)((float)((float)(cz0 / cellsH) * fh) - 0.5f));
                        int y1 = (int)MathF.Ceiling((float)((float)((float)((float)(cz0 + span) / cellsH) * fh) - 0.5f));
                        if (x0 < 0) x0 = 0;
                        if (y0 < 0) y0 = 0;
                        if (x1 > w) x1 = w;
                        if (y1 > h) y1 = h;
                        if (x0 >= x1 || y0 >= y1) continue;
                        int mip = 0;
                        while (mip + 1 < tile.mipCount && (float)(tile.mips[mip].w * TileSpan) > (float)(2.0f * (float)(x1 - x0))) mip++;
                        Mip m = tile.mips[mip];
                        bool pal = tile.format != 200;
                        if (pal) Swizzle(m);
                        float mw = m.w, mh = m.h;
                        for (int y = y0; y < y1; y++)
                        {
                            float cz = (float)((float)((float)((float)y + 0.5f) / fh) * cellsH);
                            float fy = (float)((float)((float)(TileInset + (float)((float)((float)(cz - cz0) / span) * TileSpan)) * mh) - 0.5f);
                            if (fy < 0) fy = 0;
                            if (fy > m.h - 1) fy = m.h - 1;
                            int ty0 = (int)fy, ty1 = ty0 + 1 < m.h ? ty0 + 1 : ty0;
                            float wy = (float)(fy - ty0);
                            for (int x = x0; x < x1; x++)
                            {
                                float cx = (float)((float)((float)((float)x + 0.5f) / fw) * cellsW);
                                float fx = (float)((float)((float)(TileInset + (float)((float)((float)(cx - cx0) / span) * TileSpan)) * mw) - 0.5f);
                                if (fx < 0) fx = 0;
                                if (fx > m.w - 1) fx = m.w - 1;
                                int tx0 = (int)fx, tx1 = tx0 + 1 < m.w ? tx0 + 1 : tx0;
                                float wx = (float)(fx - tx0);
                                Pixel(tile, m, tx0, ty0, c00); Pixel(tile, m, tx1, ty0, c10);
                                Pixel(tile, m, tx0, ty1, c01); Pixel(tile, m, tx1, ty1, c11);
                                int o = (y * w + x) * 4;
                                for (int c = 0; c < 4; c++)
                                {
                                    float a = (float)(c00[c] + (float)((float)(c10[c] - c00[c]) * wx));
                                    float bb = (float)(c01[c] + (float)((float)(c11[c] - c01[c]) * wx));
                                    float v = (float)((float)(a + (float)((float)(bb - a) * wy)) + 0.5f);
                                    rgba[o + c] = (byte)(int)v;
                                }
                            }
                        }
                    }
                }
            return rgba;
        }

        void Pixel(Tile tile, Mip m, int x, int y, byte[] o)
        {
            if (tile.format == 200)
            {
                int p = m.data + 4 * (y * m.w + x);
                o[0] = _b[p + 2]; o[1] = _b[p + 1]; o[2] = _b[p]; o[3] = _b[p + 3];
            }
            else
            {
                int p = 4 * _b[m.data + (m.sx[x] | m.sy[y])];
                o[0] = _palette[p]; o[1] = _palette[p + 1]; o[2] = _palette[p + 2]; o[3] = _palette[p + 3];
            }
        }
    }
}
