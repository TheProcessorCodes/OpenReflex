// MX vs ATV Unleashed .col (collision): port of the reference parser _re/tools/col.py.
//   u32 0x4D2, "col", u32 version, u32 type
//   type 0 / 9   triangle mesh: (version > 1: u32 material word), mesh
//   type 1 / 10  hierarchy: u32 children, u32, children x { u32 bone, (version > 1: u32 material word), mesh }
//   type 2       rays (no mesh: the converter leaves such an object out or refuses it, as the reference does)
//   mesh         f32[12] matrix (4 x 3), u32, u32 vertices (low 16 bits), u32 nodes, u32 triangles, u32 compressed,
//                vertices (s16[3] compressed, else f32[3]), u32 has second set (+ the set), nodes x 32 bytes,
//                triangles x { f32 plane d, f32[3] normal, u16[3] vertices }
using System;
using System.Collections.Generic;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnColMesh
    {
        /// False for a ray object (Python: no 'verts' key).
        public bool hasVerts;
        public float[] matrix;      // 12: rows 0-2 the 3 x 3, row 3 the translation
        public bool compressed;
        public int nverts;
        public byte[] verts;
        public int[] tris;          // 3 vertex indices per triangle
    }

    public sealed class UnCol
    {
        public long version, type;
        public UnColMesh mesh;                 // the object itself (types 0, 9, 2)
        public List<UnColMesh> children;       // types 1, 10; else null

        /// shape.get('children', [shape]).
        public IEnumerable<UnColMesh> Parts => children != null ? (IEnumerable<UnColMesh>)children : new[] { mesh };

        static void Need(byte[] b, long o, long n) { if (o < 0 || n < 0 || o + n > b.Length) throw new UnValueError("Unleashed collision: truncated at " + o); }

        static int ParseMesh(byte[] b, int o, UnColMesh c)
        {
            Need(b, o, 68);
            c.matrix = new float[12];
            for (int i = 0; i < 12; i++) c.matrix[i] = BitConverter.ToSingle(b, o + 4 * i);
            o += 48;
            uint nv = BitConverter.ToUInt32(b, o + 4), nnodes = BitConverter.ToUInt32(b, o + 8), ntris = BitConverter.ToUInt32(b, o + 12), comp = BitConverter.ToUInt32(b, o + 16);
            o += 20;
            c.nverts = (int)(nv & 0xFFFF); c.compressed = comp != 0; c.hasVerts = true;
            int vsz = c.compressed ? 6 : 12;
            Need(b, o, (long)vsz * c.nverts + 4);
            c.verts = new byte[vsz * c.nverts]; Buffer.BlockCopy(b, o, c.verts, 0, c.verts.Length); o += c.verts.Length;
            uint has2 = BitConverter.ToUInt32(b, o); o += 4;
            if (has2 != 0) { Need(b, o, (long)vsz * c.nverts); o += vsz * c.nverts; }
            Need(b, o, 32L * nnodes + 22L * ntris);
            o += (int)(32 * nnodes);
            c.tris = new int[ntris * 3];
            for (int i = 0; i < ntris; i++)
            {
                int p = o + 22 * i + 16;
                c.tris[i * 3] = b[p] | b[p + 1] << 8; c.tris[i * 3 + 1] = b[p + 2] | b[p + 3] << 8; c.tris[i * 3 + 2] = b[p + 4] | b[p + 5] << 8;
            }
            return o + (int)(22 * ntris);
        }

        /// col.parse(b).
        public static UnCol Parse(byte[] b)
        {
            Need(b, 0, 16);
            if (BitConverter.ToUInt32(b, 0) != 0x4D2 || b[4] != 'c' || b[5] != 'o' || b[6] != 'l') throw new UnValueError("bad magic");
            var c = new UnCol { version = BitConverter.ToUInt32(b, 8), type = BitConverter.ToUInt32(b, 12) };
            int o = 16;
            if (c.type == 0 || c.type == 9)
            {
                if (c.version > 1) { Need(b, o, 4); o += 4; }
                c.mesh = new UnColMesh(); ParseMesh(b, o, c.mesh);
            }
            else if (c.type == 1 || c.type == 10)
            {
                Need(b, o, 8);
                uint count = BitConverter.ToUInt32(b, o); o += 8;
                c.children = new List<UnColMesh>();
                for (uint i = 0; i < count; i++)
                {
                    Need(b, o, 4); o += 4;
                    if (c.version > 1) { Need(b, o, 4); o += 4; }
                    var ch = new UnColMesh(); o = ParseMesh(b, o, ch); c.children.Add(ch);
                }
            }
            else if (c.type == 2)
            {
                if (c.version > 1) o += 4;
                Need(b, o, 76);
                uint nr = BitConverter.ToUInt32(b, o + 68), np = BitConverter.ToUInt32(b, o + 72);
                Need(b, o + 76, 32L * nr + 24L * np);
                c.mesh = new UnColMesh { hasVerts = false };
            }
            else throw new UnValueError("type " + c.type + " not handled");
            return c;
        }

        /// The mesh's vertices as doubles (np.frombuffer(verts, '<i2' or '<f4').reshape(-1, 3).astype(float)).
        public static double[] Vertices(UnColMesh part)
        {
            int elem = part.compressed ? 2 : 4, count = part.verts.Length / elem;
            if (part.verts.Length % elem != 0 || count % 3 != 0) throw new UnValueError("Unleashed collision: vertex data size");
            var v = new double[count];
            for (int i = 0; i < count; i++)
                v[i] = part.compressed ? (short)(part.verts[2 * i] | part.verts[2 * i + 1] << 8) : (double)BitConverter.ToSingle(part.verts, 4 * i);
            return v;
        }
    }
}
