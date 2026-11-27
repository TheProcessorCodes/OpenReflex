// The terrain side of tools/unleashed/import_library.py convert(): the source height grid, its resampling onto the level's 2048 x 2048
// grid (terrain.rxheight), the colour map (colour.rxcolour) and the start position / direction.
// The arithmetic is numpy's and Python's: doubles with one rounding per operation, np.clip = min(max()), astype(int) truncates, np.rint
// rounds half to even, astype('<f4') / struct 'f' round to the nearest single. np.linalg.norm of a short vector is the square root of
// the squares summed in order (checked against numpy 2.0.2 on 400,000 vectors).
using System;
using System.IO;
using System.Threading;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnTerrain
    {
        public const int N = 2048;

        public UnTrn trn;
        /// The source vertex grid: w x h vertices, heights[z * w + x] in feet (node height * cell size), cell = the grid step.
        public int w, h; public double cell; public double[] heights;
        public double extentX, extentZ, centerX, centerZ;
        /// max(extent): the size scenery.py is given.
        public double maxExtent;
        /// The level grid's step (2, 4, 8 or 16) and its sample positions in source coordinates: column i at x[i], row j at z[j].
        public int spacing; public double[] x, z;

        /// The heights of every leaf patch laid into one grid, as convert() does.
        public static UnTerrain Build(UnTrn trn, CancellationToken ct = default)
        {
            var t = new UnTerrain { trn = trn };
            trn.GridSize(out t.w, out t.h);
            int w = t.w, h = t.h;
            t.cell = trn.cellSize;
            var heights = new double[(long)w * h];
            for (int i = 0; i < heights.Length; i++) heights[i] = double.NaN;
            var node = new double[289];
            foreach (var n in trn.nodes)
            {
                if (n.level != 0) continue;
                ct.ThrowIfCancellationRequested();
                trn.NodeHeights(n, node);
                for (int vz = 0; vz < 17; vz++)
                {
                    int row = (n.z + vz) * w + n.x;
                    for (int vx = 0; vx < 17; vx++) heights[row + vx] = node[vz * 17 + vx] * t.cell;
                }
            }
            foreach (double v in heights)
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new UnValueError("Incomplete source height coverage");
            t.heights = heights;
            t.extentX = (w - 1) * t.cell; t.extentZ = (h - 1) * t.cell;
            t.centerX = t.extentX / 2; t.centerZ = t.extentZ / 2;
            t.maxExtent = Math.Max(t.extentX, t.extentZ);
            t.spacing = 0;
            foreach (int s in new[] { 2, 4, 8, 16 })
                if (t.maxExtent <= 2047 * s) { t.spacing = s; break; }
            if (t.spacing == 0) throw new UnValueError("Source exceeds supported terrain extent");
            double half = 1023.5 * t.spacing;
            t.x = new double[N]; t.z = new double[N];
            for (int i = 0; i < N; i++)
            {
                double a = (double)(i * t.spacing) - half;
                t.x[i] = a + t.centerX;
                double b = half - (double)(i * t.spacing);
                t.z[i] = b + t.centerZ;
            }
            return t;
        }

        /// convert()'s sample(x, z): bilinear height at a source position, clamped to the grid.
        public double Sample(double px, double pz)
        {
            double u = px / cell; if (u < 0) u = 0; if (u > w - 1) u = w - 1;       // np.clip(x / cell, 0, w - 1)
            double v = pz / cell; if (v < 0) v = 0; if (v > h - 1) v = h - 1;
            int ix = Math.Min((int)u, w - 2), iz = Math.Min((int)v, h - 2);
            double fx = u - ix, fz = v - iz;
            double gx = 1 - fx, gz = 1 - fz;
            int p = iz * w + ix;
            double a0 = heights[p] * gx, a1 = heights[p + 1] * fx;
            double a = a0 + a1;
            double b0 = heights[p + w] * gx, b1 = heights[p + w + 1] * fx;
            double b = b0 + b1;
            double ra = a * gz, rb = b * fz;
            return ra + rb;
        }

        /// terrain.rxheight: 'RXM1', u32 1, u32 2048, f32 spacing, f32 spawn[3], f32 direction[3], then the grid (rows north to south).
        public byte[] HeightFile(double[] spawn, double[] direction, CancellationToken ct = default)
        {
            var o = new byte[40 + N * N * 4];
            o[0] = (byte)'R'; o[1] = (byte)'X'; o[2] = (byte)'M'; o[3] = (byte)'1';
            PutU32(o, 4, 1); PutU32(o, 8, N); PutF32(o, 12, spacing);
            for (int i = 0; i < 3; i++) { PutF32(o, 16 + 4 * i, (float)spawn[i]); PutF32(o, 28 + 4 * i, (float)direction[i]); }
            int p = 40;
            for (int j = 0; j < N; j++)
            {
                ct.ThrowIfCancellationRequested();
                double pz = z[j];
                for (int i = 0; i < N; i++, p += 4) PutF32(o, p, (float)Sample(x[i], pz));
            }
            return o;
        }

        /// The level's colour map, 2048 x 2048 RGBA: the source colour map (decoded at 2048 x 2048 over the whole source grid) looked up
        /// at each level grid position. Not the file: colour.rxcolour = 'RXCM', u32 1, u32 2048 + these bytes.
        public byte[] Colour(byte[] trnBytes, CancellationToken ct = default)
        {
            var native = UnTrnColour.Parse(trnBytes) ?? throw new UnValueError("Source terrain colour decode failed");
            byte[] pixels = native.ColorRgba(N, N, ct);
            var cx = new int[N]; var cz = new int[N];
            for (int i = 0; i < N; i++)
            {
                cx[i] = Index(Math.Round(x[i] / extentX * 2047, MidpointRounding.ToEven));
                cz[i] = Index(Math.Round(z[i] / extentZ * 2047, MidpointRounding.ToEven));
            }
            var o = new byte[N * N * 4];
            for (int j = 0; j < N; j++)
            {
                int src = cz[j] * N * 4, dst = j * N * 4;
                for (int i = 0; i < N; i++) Buffer.BlockCopy(pixels, src + cx[i] * 4, o, dst + i * 4, 4);
            }
            return o;
        }

        static int Index(double r) => r < 0 ? 0 : r > 2047 ? 2047 : (int)r;

        public static byte[] ColourFile(byte[] colour) => Tagged("RXCM", colour);

        /// struct.pack('<4sII', tag, 1, 2048) + data.
        public static byte[] Tagged(string tag, byte[] data)
        {
            var o = new byte[12 + data.Length];
            for (int i = 0; i < 4; i++) o[i] = (byte)tag[i];
            PutU32(o, 4, 1); PutU32(o, 8, N);
            Buffer.BlockCopy(data, 0, o, 12, data.Length);
            return o;
        }

        /// The start: (px, pz) in source coordinates and the riding direction, from the course's start gate, else a .scn section whose
        /// name holds "start" and has a position, else the scene's default start. Returns spawn (level coordinates, 2 ft above the
        /// ground) and the normalised horizontal direction.
        public void Start(UnTdf course, string sceneText, out double[] spawn, out double[] direction)
        {
            double px, pz; double[] dir = { 0, 0, 1 };
            var gate = course?.Gate("start_gate");
            if (gate != null) { px = gate.pos[0]; pz = gate.pos[2]; dir = (double[])gate.normal.Clone(); }
            else
            {
                var scn = UnIni.Parse(sceneText);
                UnIniSection start = null;
                foreach (var s in scn.Sections)
                    if (UnIni.Lower(s.Name).Contains("start") && s.Has("position")) { start = s; break; }
                if (start != null) { var p = Floats3(start["position"]); px = p[0]; pz = p[2]; }
                else
                {
                    var info = scn["SceneInfo"];
                    var p = Floats3(info["DefaultStartPosition"]); px = p[0]; pz = p[2];
                    dir = Floats3(info["DefaultStartDirection"]);
                }
            }
            dir[1] = 0;
            double norm = Norm(dir);
            if (norm < .01) throw new UnValueError("Invalid authored start direction");
            direction = new[] { dir[0] / norm, dir[1] / norm, dir[2] / norm };
            spawn = new[] { px - centerX, Sample(px, pz) + 2, pz - centerZ };
        }

        /// map(float, text.split(',')) unpacked into three names.
        static double[] Floats3(string text)
        {
            if (text == null) throw new UnValueError("a start entry of the scene has no value");
            var parts = text.Split(',');
            if (parts.Length != 3) throw new UnValueError((parts.Length > 3 ? "too many values to unpack (expected 3)" : "not enough values to unpack (expected 3, got " + parts.Length + ")"));
            return new[] { UnJson.Float(parts[0]), UnJson.Float(parts[1]), UnJson.Float(parts[2]) };
        }

        /// np.linalg.norm of a short vector: sqrt of the squares added in order.
        public static double Norm(params double[] v)
        {
            double s = 0;
            for (int i = 0; i < v.Length; i++) { double q = v[i] * v[i]; s = s + q; }
            return Math.Sqrt(s);
        }

        internal static void PutU32(byte[] b, int p, uint v) { b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); b[p + 2] = (byte)(v >> 16); b[p + 3] = (byte)(v >> 24); }
        internal static void PutF32(byte[] b, int p, float v) => PutU32(b, p, (uint)BitConverter.SingleToInt32Bits(v));
    }
}
