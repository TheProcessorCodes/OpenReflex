// The writers of the experimental Reflex track package (RXPK 4, tools/mtx/FORMAT.txt), used by the track builder (TrackBuilder/TrackExport.cs):
// the RXM1 height grid (2048 x 2048 samples, 2 ft apart, rows from +Z, columns from -X, centred on the package centre), the RXSC scenery
// (textures, materials, world-space draws), the RXCO collision chunks, the RXSM surface weights, the RXCM colour map and the RXRA race route.
// Everything is written in the package frame: x and z less the centre, y (feet) unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Reflex.Port.Content.TrackPackage
{
    public sealed class TrackPackageException : Exception
    {
        public TrackPackageException(string message) : base(message) { }
    }

    internal sealed class Grid
    {
        public const int N = 2048;
        /// Feet between samples: 2 (a race course: 4,094 ft across) or 4 (the Free Ride of a whole world: 8,188 ft; the package reader takes both).
        public readonly float Spacing, Half;
        public readonly float cx, cz;
        public readonly float[] heights = new float[N * N];
        public Grid(float cx, float cz, float spacing = 2f) { this.cx = cx; this.cz = cz; Spacing = spacing; Half = (N - 1) * spacing / 2; for (int i = 0; i < heights.Length; i++) heights[i] = float.NaN; }
        /// Grid coordinates (column, row) of a world point.
        public float Col(float x) => (x - cx + Half) / Spacing;
        public float Row(float z) => (Half - (z - cz)) / Spacing;
        public bool Inside(float x, float z, float margin = 0) { float c = Col(x), r = Row(z); return c >= -margin && r >= -margin && c <= N - 1 + margin && r <= N - 1 + margin; }
        public float Sample(float x, float z)
        {
            float c = Math.Max(0, Math.Min(N - 1.001f, Col(x))), r = Math.Max(0, Math.Min(N - 1.001f, Row(z)));
            int i = (int)c, j = (int)r; float fx = c - i, fz = r - j;
            float a = heights[j * N + i], b = heights[j * N + i + 1], d = heights[(j + 1) * N + i], e = heights[(j + 1) * N + i + 1];
            return (a * (1 - fx) + b * fx) * (1 - fz) + (d * (1 - fx) + e * fx) * fz;
        }

        /// Rasterizes a world triangle over the samples it covers: `visit(index, barycentric b, barycentric c, y)` (a + b (B - A) + c (C - A)).
        public void Raster(Vector3 A, Vector3 B, Vector3 C, Action<int, float, float, float> visit)
        {
            float ax = Col(A.X), az = Row(A.Z), bx = Col(B.X) - ax, bz = Row(B.Z) - az, qx = Col(C.X) - ax, qz = Row(C.Z) - az;
            float det = bx * qz - bz * qx; if (Math.Abs(det) < 1e-6f) return;
            int x0 = Math.Max(0, (int)Math.Ceiling(Math.Min(ax, Math.Min(ax + bx, ax + qx)) - .001f)), x1 = Math.Min(N - 1, (int)Math.Floor(Math.Max(ax, Math.Max(ax + bx, ax + qx)) + .001f));
            int z0 = Math.Max(0, (int)Math.Ceiling(Math.Min(az, Math.Min(az + bz, az + qz)) - .001f)), z1 = Math.Min(N - 1, (int)Math.Floor(Math.Max(az, Math.Max(az + bz, az + qz)) + .001f));
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                float u = ((x - ax) * qz - (z - az) * qx) / det, v = (bx * (z - az) - bz * (x - ax)) / det;
                if (u < -.001f || v < -.001f || u + v > 1.001f) continue;
                visit(z * N + x, u, v, A.Y + u * (B.Y - A.Y) + v * (C.Y - A.Y));
            }
        }

        /// Holes take their nearest covered neighbour's height (no missing samples in Reflex's collision grid).
        public int Fill()
        {
            var valid = new bool[heights.Length]; var queue = new Queue<int>(); int holes = 0;
            for (int i = 0; i < heights.Length; i++) if (!float.IsNaN(heights[i])) { valid[i] = true; queue.Enqueue(i); } else holes++;
            if (queue.Count == 0) throw new TrackPackageException("No terrain under the package grid");
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % N, z = i / N;
                void Try(int k) { if (!valid[k]) { valid[k] = true; heights[k] = heights[i]; queue.Enqueue(k); } }
                if (x > 0) Try(i - 1); if (x < N - 1) Try(i + 1); if (z > 0) Try(i - N); if (z < N - 1) Try(i + N);
            }
            return holes;
        }
    }

    /// RXSC 2: textures (id, size, RGBA8), materials (the MTX pass model), no groups (every draw in package space), draws, no clips.
    internal sealed class SceneryWriter
    {
        public sealed class Mat
        {
            public uint texture; public byte blend, cutoff, fixedAlpha = 128; public bool texAlpha, sky;
            public readonly bool clampU = false, clampV = false;
            public Vector3 colour = Vector3.One; public uint lightmap; public byte lightmapUv = 1;   // a modulate layer (0: none) on UV set 1 or 2 (a copy of UV0)
            public string Key => $"{texture}/{blend}/{cutoff}/{texAlpha}/{clampU}/{clampV}/{colour}/{lightmap}/{lightmapUv}/{sky}";
        }
        sealed class Draw { public int material; public bool sky, billboard; public readonly List<float> b = new List<float>(); public readonly List<float> v = new List<float>(); public readonly List<uint> c = new List<uint>(); public readonly List<int> i = new List<int>(); public int Count => c.Count; }
        readonly List<(uint id, int w, int h, byte[] px)> textures = new List<(uint, int, int, byte[])>();
        readonly List<Mat> materials = new List<Mat>(); readonly Dictionary<string, int> materialIndex = new Dictionary<string, int>();
        readonly List<Draw> draws = new List<Draw>(); readonly Dictionary<int, Draw> open = new Dictionary<int, Draw>();
        public int Triangles { get; private set; }
        public int TextureCount => textures.Count;
        public long TextureBytes => textures.Sum(t => (long)t.px.Length);
        /// The draws' size in the section (vertices, indices, headers).
        public long GeometryBytes => draws.Sum(d => 18L + d.Count * (d.billboard ? 68L : 48L) + d.i.Count * 4L);

        internal static byte[] Halve(byte[] px, int w, int h)
        {
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2); var o = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++) for (int k = 0; k < 4; k++)
            {
                int x0 = Math.Min(w - 1, x * 2), x1 = Math.Min(w - 1, x * 2 + 1), y0 = Math.Min(h - 1, y * 2), y1 = Math.Min(h - 1, y * 2 + 1);
                o[(y * nw + x) * 4 + k] = (byte)((px[(y0 * w + x0) * 4 + k] + px[(y0 * w + x1) * 4 + k] + px[(y1 * w + x0) * 4 + k] + px[(y1 * w + x1) * 4 + k] + 2) / 4);
            }
            return o;
        }
        public uint AddTexture(int w, int h, byte[] px) { uint id = (uint)textures.Count + 1; textures.Add((id, w, h, px)); return id; }
        public int Material(Mat m) { if (materialIndex.TryGetValue(m.Key, out int k)) return k; materials.Add(m); return materialIndex[m.Key] = materials.Count - 1; }

        /// Appends triangles in package space: positions, UV0, UV1, RGBA8 colours (r in the low byte), indices.
        public void Add(int material, bool sky, IList<Vector3> pos, IList<Vector2> uv0, IList<Vector2> uv1, IList<uint> col, IList<int> tri)
        {
            if (tri.Count == 0) return;
            if (!open.TryGetValue(material, out var d) || d.Count + pos.Count > 60000) { d = new Draw { material = material, sky = sky }; draws.Add(d); open[material] = d; }
            int baseIndex = d.Count;
            for (int k = 0; k < pos.Count; k++)
            {
                var p = pos[k]; var a = uv0 != null ? uv0[k] : Vector2.Zero; var b = uv1 != null ? uv1[k] : a;
                if (float.IsNaN(a.X) || float.IsNaN(a.Y)) a = Vector2.Zero; if (float.IsNaN(b.X) || float.IsNaN(b.Y)) b = a;
                d.v.Add(p.X); d.v.Add(p.Y); d.v.Add(p.Z); d.v.Add(a.X); d.v.Add(a.Y); d.v.Add(b.X); d.v.Add(b.Y); d.v.Add(a.X); d.v.Add(a.Y); d.v.Add(b.X); d.v.Add(b.Y);
                d.c.Add(col != null ? col[k] : 0xff808080u);
            }
            foreach (int t in tri) d.i.Add(baseIndex + t);
            Triangles += tri.Count / 3;
        }

        /// A camera-facing card (it turns about the vertical through `pivot`): `width` x `height` from the pivot up, the texture rectangle
        /// uvMin (top left) .. uvMax (bottom right), a colour at its foot and one at its top. `facing`: the way it faces at rest.
        public void AddBillboard(int material, Vector3 pivot, Vector3 facing, float width, float height, Vector2 uvMin, Vector2 uvMax, uint bottom, uint top)
        {
            int key = material | 1 << 30;
            if (!open.TryGetValue(key, out var d) || d.Count + 4 > 60000) { d = new Draw { material = material, billboard = true }; draws.Add(d); open[key] = d; }
            var right = Vector3.Cross(Vector3.UnitY, facing); right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
            int baseIndex = d.Count;
            for (int k = 0; k < 4; k++)
            {
                float ox = (k == 0 || k == 3 ? -.5f : .5f) * width, oy = k < 2 ? 0 : height; var p = pivot + right * ox + Vector3.UnitY * oy;
                float u = k == 0 || k == 3 ? uvMin.X : uvMax.X, v = k < 2 ? uvMax.Y : uvMin.Y;
                d.v.Add(p.X); d.v.Add(p.Y); d.v.Add(p.Z); for (int j = 0; j < 4; j++) { d.v.Add(u); d.v.Add(v); }
                d.c.Add(k < 2 ? bottom : top);
                d.b.Add(pivot.X); d.b.Add(pivot.Y); d.b.Add(pivot.Z); d.b.Add(ox); d.b.Add(oy);
            }
            foreach (int t in new[] { 0, 2, 1, 0, 3, 2 }) d.i.Add(baseIndex + t);
            Triangles += 2;
        }

        /// The section, the largest textures halved (box filter) until all of them fit `textureBudget` bytes.
        public byte[] Bytes(long textureBudget)
        {
            while (TextureBytes > textureBudget)
            {
                // (by area: a 2048 x 256 board or a 2048 x 512 sky keeps its width while the 2048 x 2048 maps give way first)
                long largest = textures.Max(t => (long)t.w * t.h); if (largest <= 32 * 32) break;
                for (int k = 0; k < textures.Count; k++)
                {
                    var t = textures[k]; if ((long)t.w * t.h < largest) continue;
                    textures[k] = (t.id, Math.Max(1, t.w / 2), Math.Max(1, t.h / 2), Halve(t.px, t.w, t.h));
                }
            }
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write(0x43535852); w.Write(2);
            w.Write(textures.Count); foreach (var t in textures) { w.Write(t.id); w.Write(t.w); w.Write(t.h); w.Write(t.px); }
            w.Write(materials.Count);
            foreach (var m in materials)
            {
                w.Write(m.texture); w.Write(m.blend); w.Write(m.cutoff); w.Write(m.texAlpha); w.Write(false); w.Write(m.fixedAlpha); w.Write(false); w.Write(m.clampU); w.Write(m.clampV);
                w.Write(m.colour.X); w.Write(m.colour.Y); w.Write(m.colour.Z); w.Write(false); w.Write(1f); w.Write(1f);
                if (m.lightmap == 0) w.Write((byte)0);
                else { w.Write((byte)1); w.Write(m.lightmap); w.Write((byte)8); w.Write((byte)128); w.Write(false); w.Write(true); w.Write(1f); w.Write(1f); w.Write(m.lightmapUv); w.Write(false); w.Write(false); w.Write(1f); w.Write(1f); w.Write(1f); }
            }
            w.Write(0);   // no groups
            var kept = draws.Where(d => d.i.Count > 0).ToList();
            if (kept.Count == 0) throw new TrackPackageException("The scenery has no draws");
            w.Write(kept.Count);
            foreach (var d in kept)
            {
                w.Write(d.material); w.Write(d.sky); w.Write(d.billboard); w.Write(-1); w.Write(d.Count); w.Write(d.i.Count);
                for (int k = 0; k < d.Count; k++)
                {
                    for (int j = 0; j < 11; j++) w.Write(d.v[k * 11 + j]); w.Write(d.c[k]);
                    if (d.billboard) for (int j = 0; j < 5; j++) w.Write(d.b[k * 5 + j]);   // the pivot and the corner's offset (across, up)
                }
                foreach (int t in d.i) w.Write(t);
            }
            w.Write(0);   // no animation clips
            w.Flush(); return ms.ToArray();
        }
    }

    /// RXCO 1: chunks of at most 48,000 vertices (16,000 triangles), binned by 64 ft tiles of the package grid so the broadphase stays local.
    internal sealed class CollisionWriter
    {
        readonly Dictionary<(int, int), List<Vector3>> bins = new Dictionary<(int, int), List<Vector3>>();
        public int Triangles { get; private set; }
        public void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-8f) return;
            var mid = (a + b + c) / 3; var key = ((int)Math.Floor(mid.X / 128), (int)Math.Floor(mid.Z / 128));
            if (!bins.TryGetValue(key, out var list)) bins[key] = list = new List<Vector3>();
            list.Add(a); list.Add(b); list.Add(c); Triangles++;
        }
        public byte[] Bytes()
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            var chunks = new List<List<Vector3>>();
            foreach (var list in bins.Values) for (int p = 0; p < list.Count; p += 48000) chunks.Add(list.GetRange(p, Math.Min(48000, list.Count - p)));
            if (chunks.Count > 10000) throw new TrackPackageException("Too many collision chunks");
            w.Write(0x4f435852); w.Write(1); w.Write(chunks.Count);
            foreach (var ch in chunks) { w.Write(ch.Count); foreach (var v in ch) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); } }
            w.Flush(); return ms.ToArray();
        }
    }

    internal static class PackageWriter
    {
        /// The RXRA payload: kind 0 National / 1 Supercross, the lap (closes on its first point) and the lead-in (ends there); points
        /// are (x, y, z, width, desired speed) in package space, at most 10 ft apart.
        public static byte[] Race(int kind, List<float[]> lap, List<float[]> lead)
        {
            List<float[]> Dense(List<float[]> pts)
            {
                var o = new List<float[]>();
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    var a = pts[k]; var b = pts[k + 1]; float d = (float)Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[2] - a[2]) * (b[2] - a[2]));
                    if (d < .01f) continue; int n = Math.Max(1, (int)Math.Ceiling(d / 10));
                    for (int j = 0; j < n; j++) o.Add(a.Zip(b, (x, y) => x + (y - x) * j / n).ToArray());
                }
                o.Add(pts[pts.Count - 1]); return o;
            }
            lap = Dense(lap); lead = Dense(lead);
            if (lap.Count < 10 || lead.Count < 2 || lap.Count > 4096 || lead.Count > 4096 || 100 + 20 * (lap.Count + lead.Count) > 164000) throw new TrackPackageException("Race route size");
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write(0x41525852); w.Write(1); w.Write(kind); w.Write(lap.Count); w.Write(lead.Count);
            foreach (var pts in new[] { lead, lap })
            {
                var a = pts[0]; var b = pts[1]; var d = Vector3.Normalize(new Vector3(b[0] - a[0], 0, b[2] - a[2]));
                w.Write(a[0]); w.Write(a[1]); w.Write(a[2]); w.Write(d.X); w.Write(0f); w.Write(d.Z); w.Write(-d.Z); w.Write(0f); w.Write(d.X); w.Write(a[3]);
            }
            foreach (var p in lap.Concat(lead)) for (int k = 0; k < 5; k++) w.Write(p[k]);
            w.Flush(); return ms.ToArray();
        }

        public static void Write(string path, string metadataJson, Grid g, Vector3 spawn, Vector3 forward, byte[] scenery, byte[] collision, byte[] surface, byte[] colour, byte[] race)
        {
            var meta = Encoding.UTF8.GetBytes(metadataJson); if (meta.Length < 2 || meta.Length > 65536) throw new TrackPackageException("Package metadata size");
            if (scenery.Length > 128 * 1024 * 1024) throw new TrackPackageException("Scenery exceeds 128 MiB");
            if (collision.Length > 32 * 1024 * 1024) throw new TrackPackageException("Collision exceeds 32 MiB");
            string partial = path + ".partial";
            using (var file = File.Create(partial))
            using (var gz = new GZipStream(file, CompressionLevel.Optimal))
            using (var w = new BinaryWriter(gz))
            {
                w.Write(0x4b505852); w.Write(4); w.Write(meta.Length); w.Write(meta);
                w.Write(0x314d5852); w.Write(1); w.Write(Grid.N); w.Write(g.Spacing);
                w.Write(spawn.X); w.Write(spawn.Y); w.Write(spawn.Z); w.Write(forward.X); w.Write(forward.Y); w.Write(forward.Z);
                var buf = new byte[Grid.N * 4];
                for (int r = 0; r < Grid.N; r++) { Buffer.BlockCopy(g.heights, r * Grid.N * 4, buf, 0, buf.Length); w.Write(buf); }
                w.Write(0x454e4353); w.Write(scenery.Length); w.Write(scenery);
                w.Write(0x4c4c4f43); w.Write(collision.Length); w.Write(collision);
                w.Write(0x46525553); w.Write(12 + Grid.N * Grid.N * 4); w.Write(0x4d535852); w.Write(1); w.Write(Grid.N); w.Write(surface);
                w.Write(0x524c4f43); w.Write(12 + Grid.N * Grid.N * 4); w.Write(0x4d435852); w.Write(1); w.Write(Grid.N); w.Write(colour);
                if (race != null) { w.Write(0x45434152); w.Write(race.Length); w.Write(race); }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(partial, path);
        }

        public static string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        public static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
