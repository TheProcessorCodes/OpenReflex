// MX vs ATV Untamed converter, assembly parts: Untamed's painted ground overlays (not in the Python reference converter; every stadium,
// the first test was Harris Park). Untamed's stadium terrain is a mesh with several materials. Some of them carry a texture of their own
// that the pixel program multiplies onto the dirt, by a weight painted per vertex (the vertex colour's red byte, 0 .. 1) and through
// the material's own texture coordinates (uv0, not the planar ones of the other layers):
//   the lanes (HP_TrackRutsA ...)       UI_Indoor_Ruts01, one strip along the whole lap fetched at (1.5 u, v) (2 u or 3 u on some levels),
//                                       colour x lerp(1, tex, weight): the fake ruts in the corners and between the jumps
//   the jump sides (HP_DirtSidesA ...)  Stadium_Side_Dirt, colour x lerp(1, tex^3, weight): the clumpy dirt on the sides of the jumps
// (read from the SharedPixelShader programs of the disc, see LEVELS: the last instructions are  mad r1, (variant x tex) - base, weight, base.)
// The Reflex height-field terrain has neither vertex colours nor per-material texture coordinates, so the converter bakes both onto the
// height grid (1024 x 1024 nodes, 1 ft) and the Open Reflex client multiplies the overlay onto the terrain's detail (ReflexTerrain.shader,
// "UNTAMED GROUND OVERLAY"). The track package gets five more textures, named after the terrain's detail mask <M>:
//   <M>_UxOvU, <M>_UxOvV   R32F: the overlay texture coordinate at every grid node (Untamed's uv0, D3D v down), filled outward from the
//                          nearest painted node
//   <M>_UxOvW              R32F: the weight; > 0 = layer A (the ruts), < 0 = layer B (the sides); 0 where the coordinates jump (the lap's
//                          seam, a chart border)
//   <M>_UxOvA, <M>_UxOvB   DXT1: the two overlay textures as the client multiplies them (gain and power applied)
// A client without the overlay ignores them. The far ground (the TopDown map) takes the overlays' mean colour by the same weight.
// Pure C# (System only).
using System;
using System.Collections.Generic;
using System.Threading;
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UntamedImport
{
    public static class UxGroundOverlay
    {
        public const string TOOL = "ux_groundoverlay/1";
        /// The grid: the height field's own nodes (node (r, c): x = c - 511.5, z = 511.5 - r).
        public const int N = UxCommon.HF_SIZE;
        /// A coordinate step above this between two neighbouring nodes is a seam (a painted lane moves 0.02 .. 0.15 per ft).
        public const double SEAM_STEP = 0.35;
        public static readonly string[] CHOICES = { "auto", "none" };

        /// One terrain material that carries an overlay: slot 0 = A (the ruts), 1 = B (the sides); uScale: the factor its pixel program
        /// puts on the u coordinate before the fetch.
        public sealed class Layer
        {
            public readonly string material; public readonly int slot; public readonly double uScale;
            public Layer(string material, int slot, double uScale) { this.material = material; this.slot = slot; this.uScale = uScale; }
        }
        /// One overlay texture of a level: colour x lerp(1, gain x tex ^ power, weight). A null Tex = the level has no such layer.
        public sealed class Tex
        {
            public readonly string texture; public readonly int power; public readonly double gain;
            public Tex(string texture, int power = 1, double gain = 1.0) { this.texture = texture; this.power = power; this.gain = gain; }
        }
        public sealed class Level
        {
            public readonly Tex a, b; public readonly Layer[] layers;
            public Level(Tex a, Tex b, params Layer[] layers) { this.a = a; this.b = b; this.layers = layers; }
        }
        const string RUTS = "UI_Indoor_Ruts01", SIDE = "Stadium_Side_Dirt";
        static Layer A(string material, double uScale = 1.5) => new Layer(material, 0, uScale);
        static Layer B(string material, double uScale = 1.0) => new Layer(material, 1, uScale);
        static readonly Tex RutsTex = new Tex(RUTS), SideTex = new Tex(SIDE, 3);
        /// The levels, as their terrain materials' SharedPixelShader programs were read (each ends  mad r, (variant x tex ^ p) - base,
        /// weight, base  or the same with the lit colour on both sides; the weight is the raw vertex colour lane; the ruts are fetched at
        /// (uScale x u, v), the side dirt at uv0 unless noted). Not carried: the EnduroCross arenas' rocks / logs (ordinary textured
        /// surfaces tinted by the vertex colour) and Lincoln's tarp (a colour replacement, not a multiply).
        static readonly Dictionary<string, Level> LEVELS = new Dictionary<string, Level>(StringComparer.Ordinal)
        {
            { "BridgePort_STADIUM", new Level(RutsTex, SideTex, A("Bridgeport_Track_In"), A("Bridgeport_Track_Funnel"), A("Bridgeport_Track_Over"), B("Bridgeport_Track_Sides")) },
            { "Capital_City_STADIUM", new Level(RutsTex, SideTex, A("CC_TrackRutsA", 2), B("CC_DirtSidesB")) },
            { "Columbus_STADIUM", new Level(RutsTex, SideTex, A("Columbus_Track_In"), B("Columbus_Track_Side")) },
            { "Denton_STADIUM", new Level(RutsTex, SideTex, A("Denton_SXIn", 2), B("Denton_SXSide")) },
            { "Dyson_STADIUM", new Level(RutsTex, new Tex(SIDE, 2), A("DysonMM_TIn", 3), A("DysonMM_Tover", 3), A("DysonMM_Under", 3), B("DysonMM_TSide", 1.5)) },
            { "Edgewater_STADIUM", new Level(RutsTex, SideTex, A("Edgewater_TRuts"), B("Edgewater_TSides", 2)) },
            { "Fairview_STADIUM", new Level(new Tex(RUTS, 1, 1.788236), null, A("Fairview_Ruts")) },
            { "Foothills_STADIUM", new Level(RutsTex, SideTex, A("Foothills_SXIn"), B("Foothills_SXSide")) },
            { "HarrisPark_STADIUM", new Level(RutsTex, SideTex, A("HP_TrackRutsA"), B("HP_DirtSidesA")) },
            { "Jefferson_STADIUM", new Level(RutsTex, null, A("Jefferson_SXIn", 2), A("Jefferson_SXOver", 2)) },
            { "Lincoln_STADIUM", new Level(new Tex(RUTS, 1, 1.788236), null, A("Lincoln_Ruts")) },
            { "Norton_STADIUM", new Level(RutsTex, SideTex, A("Norton_SXIn"), B("Norton_SXSides")) },
            { "RBWCenter_STADIUM", new Level(RutsTex, SideTex, A("RBW_Ruts"), B("RBW_DirtSide")) },
            { "Southbend_STADIUM", new Level(RutsTex, SideTex, A("SouthbendFMX_TIn"), B("SouthbendFMX_TSide")) },
            { "THQSpectrum_STADIUM", new Level(RutsTex, SideTex, A("THQSpectrumFMX_TIn"), B("THQSpectrumFMX_TSide")) },
            { "WestLake_STADIUM", new Level(RutsTex, SideTex, A("WestLArena_Ruts"), B("Westlake_Dirt_Sides")) },
            { "WestStone_STADIUM", new Level(RutsTex, SideTex, A("Weststone_SXIn"), B("Weststone_SXSide")) },
            { "Wilford_STADIUM", new Level(RutsTex, SideTex, A("Wilford_Track_In", 2), A("Wilford_Track_InOVER", 2), B("Wilford_Track_Sides")) },
        };
        public static Level For(UxLevelRow lv) => LEVELS.TryGetValue(lv.disc, out var l) ? l : null;

        public sealed class Result
        {
            /// N x N, row 0 = +z (the height map's order).
            public float[] u, v, w;
            /// The two overlay textures (PC DXT1 with mips) and their mean colours (0 .. 1).
            public byte[] texA, texB; public double[] meanA, meanB;
            public JObj rec;
            public static string NameU(string mask) => mask + "_UxOvU";
            public static string NameV(string mask) => mask + "_UxOvV";
            public static string NameW(string mask) => mask + "_UxOvW";
            public static string NameA(string mask) => mask + "_UxOvA";
            public static string NameB(string mask) => mask + "_UxOvB";
        }

        /// terrain: the level's terrain mesh; materials: its material names in order; readTexture(name): the converted (PC) texture bytes.
        public static Result Build(UxTerrainMesh terrain, IList<string> materials, Level level, Func<string, byte[]> readTexture, Action<string> log = null,
            CancellationToken cancel = default)
        {
            UxBin.Require(level != null && level.a != null, "ground overlay: the level has no layer A");
            var layers = level.layers;
            UxBin.Require(terrain.materials.Count == materials.Count, "ground overlay: the terrain's material count differs from its list");
            int NN = N * N;
            var chart = new byte[NN]; var top = new double[NN]; var u = new double[NN]; var v = new double[NN]; var wt = new double[NN];
            for (int i = 0; i < NN; i++) top[i] = double.NegativeInfinity;
            var tris = new long[3]; var us = new double[NN];
            for (int k = 0; k < terrain.materials.Count; k++)
            {
                cancel.ThrowIfCancellationRequested();
                byte role = 0; double uScale = 1;
                foreach (var ly in layers) if (ly.material == materials[k]) { role = (byte)(ly.slot + 1); uScale = ly.uScale; }
                var m = terrain.materials[k]; var P = m.pos; var T = m.tris;
                for (int t = 0; t + 2 < T.Length; t += 3)
                {
                    int a = T[t], b = T[t + 1], c = T[t + 2];
                    double ax = P[3 * a] + 511.5, az = 511.5 - P[3 * a + 2], bx = P[3 * b] + 511.5, bz = 511.5 - P[3 * b + 2], cx = P[3 * c] + 511.5, cz = 511.5 - P[3 * c + 2];
                    double den = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
                    if (Math.Abs(den) < 1e-12) continue;                                   // a vertical or degenerate triangle covers no node
                    int c0 = Math.Max(0, (int)Math.Ceiling(Math.Min(ax, Math.Min(bx, cx)) - 1e-6)), c1 = Math.Min(N - 1, (int)Math.Floor(Math.Max(ax, Math.Max(bx, cx)) + 1e-6));
                    int r0 = Math.Max(0, (int)Math.Ceiling(Math.Min(az, Math.Min(bz, cz)) - 1e-6)), r1 = Math.Min(N - 1, (int)Math.Floor(Math.Max(az, Math.Max(bz, cz)) + 1e-6));
                    if (c0 > c1 || r0 > r1) continue;
                    tris[role]++;
                    double wa = role != 0 ? ((m.color[a] >> 16) & 255) / 255.0 : 0, wb = role != 0 ? ((m.color[b] >> 16) & 255) / 255.0 : 0, wc = role != 0 ? ((m.color[c] >> 16) & 255) / 255.0 : 0;
                    for (int r = r0; r <= r1; r++)
                        for (int col = c0; col <= c1; col++)
                        {
                            double l0 = ((bz - cz) * (col - cx) + (cx - bx) * (r - cz)) / den, l1 = ((cz - az) * (col - cx) + (ax - cx) * (r - cz)) / den, l2 = 1 - l0 - l1;
                            if (l0 < -1e-6 || l1 < -1e-6 || l2 < -1e-6) continue;
                            double y = l0 * P[3 * a + 1] + l1 * P[3 * b + 1] + l2 * P[3 * c + 1];
                            int i = r * N + col;
                            if (!(y > top[i])) continue;                                    // the highest surface is the one seen (and ridden)
                            top[i] = y; chart[i] = role;
                            if (role == 0) continue;
                            u[i] = l0 * m.uv0[2 * a] + l1 * m.uv0[2 * b] + l2 * m.uv0[2 * c];
                            v[i] = l0 * m.uv0[2 * a + 1] + l1 * m.uv0[2 * b + 1] + l2 * m.uv0[2 * c + 1];
                            wt[i] = l0 * wa + l1 * wb + l2 * wc; us[i] = uScale;
                        }
                }
            }
            long nA = 0, nB = 0; double maxA = 0, maxB = 0;
            for (int i = 0; i < NN; i++) { if (chart[i] == 1) { nA++; maxA = Math.Max(maxA, wt[i]); } else if (chart[i] == 2) { nB++; maxB = Math.Max(maxB, wt[i]); } }
            if (nA + nB == 0) throw new UxException("ground overlay: no overlay material covers a grid node");
            // ---- seams: a node whose neighbour belongs to the other layer, or whose coordinates jump to a neighbour of its own layer
            var seam = new bool[NN]; long nSeam = 0;
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                {
                    int i = r * N + c; if (chart[i] == 0) continue;
                    bool s = false;
                    for (int dr = -1; dr <= 1 && !s; dr++)
                        for (int dc = -1; dc <= 1 && !s; dc++)
                        {
                            int rr = r + dr, cc = c + dc; if ((dr == 0 && dc == 0) || rr < 0 || cc < 0 || rr >= N || cc >= N) continue;
                            int j = rr * N + cc; if (chart[j] == 0) continue;
                            s = chart[j] != chart[i] || Math.Abs(u[j] - u[i]) > SEAM_STEP || Math.Abs(v[j] - v[i]) > SEAM_STEP;
                        }
                    if (s) { seam[i] = true; nSeam++; }
                }
            // the weight is 0 on a seam node and on its neighbours, half one node further out
            bool[] kill = UxNd.BinaryDilation(seam, N, N, 1), half = UxNd.BinaryDilation(kill, N, N, 1);
            var res = new Result { u = new float[NN], v = new float[NN], w = new float[NN] };
            for (int i = 0; i < NN; i++)
            {
                if (chart[i] == 0) continue;
                double x = kill[i] ? 0.0 : half[i] ? 0.5 * wt[i] : wt[i];
                res.w[i] = (float)(chart[i] == 1 ? x : -x);
            }
            // ---- the coordinates of the unpainted nodes: the nearest painted node's
            cancel.ThrowIfCancellationRequested();
            var unpainted = new bool[NN]; for (int i = 0; i < NN; i++) unpainted[i] = chart[i] == 0;
            UxNd.DistanceTransformEdt(unpainted, N, N, out int[] nr, out int[] nc);
            for (int i = 0; i < NN; i++) { int j = chart[i] != 0 ? i : nr[i] * N + nc[i]; res.u[i] = (float)(u[j] * us[j]); res.v[i] = (float)v[j]; }
            // ---- the two textures
            res.texA = Texture(level.a, readTexture, out res.meanA, out int aw, out int ah);
            res.texB = Texture(level.b, readTexture, out res.meanB, out int bw, out int bh);
            string Mats(int slot) { var n = new List<string>(); foreach (var ly in layers) if (ly.slot == slot) n.Add(ly.material + " x" + ly.uScale.ToString(System.Globalization.CultureInfo.InvariantCulture)); return string.Join(", ", n); }
            JObj L(Tex t, int slot, long nodes, double max, double[] mean, int w, int h, byte[] tex) => new JObj().Set("materials", Mats(slot)).Set("texture", t?.texture).Set("power", (long)(t?.power ?? 0))
                .Set("gain", t?.gain ?? 0.0).Set("nodes", nodes).Set("weight_max", UxNp.PyRound(max, 4)).Set("size", new List<object> { (long)w, (long)h })
                .Set("mean", new List<object> { UxNp.PyRound(mean[0], 4), UxNp.PyRound(mean[1], 4), UxNp.PyRound(mean[2], 4) }).Set("md5", UxBin.Md5(tex));
            res.rec = new JObj().Set("tool", TOOL).Set("grid", (long)N).Set("a", L(level.a, 0, nA, maxA, res.meanA, aw, ah, res.texA)).Set("b", L(level.b, 1, nB, maxB, res.meanB, bw, bh, res.texB))
                .Set("triangles", new List<object> { tris[1], tris[2], tris[0] }).Set("seam_nodes", nSeam).Set("seam_step", SEAM_STEP);
            log?.Invoke("ground overlay: A " + nA + " nodes (weight max " + UxNp.PyRound(maxA, 3).ToString(System.Globalization.CultureInfo.InvariantCulture) + "), B " + nB
                + " nodes (weight max " + UxNp.PyRound(maxB, 3).ToString(System.Globalization.CultureInfo.InvariantCulture) + "), " + nSeam + " seam nodes");
            return res;
        }

        /// The overlay texture the client multiplies: gain x the Untamed texture's colour ^ power (clamped to 1), DXT1 with mips.
        /// A level without that layer: a white 4 x 4.
        static byte[] Texture(Tex l, Func<string, byte[]> readTexture, out double[] mean, out int w, out int h)
        {
            if (l == null)
            {
                w = h = 4; mean = new[] { 1.0, 1.0, 1.0 };
                var white = new byte[4 * 4 * 3]; for (int i = 0; i < white.Length; i++) white[i] = 255;
                return UxBase.TexBc(new UxImage(white, 4, 4, 3), false);
            }
            var im = UxDirtLook.Decode(readTexture(l.texture));
            w = im.w; h = im.h;
            var lut = new byte[256];
            for (int i = 0; i < 256; i++) { double x = i / 255.0, y = l.gain; for (int p = 0; p < l.power; p++) y *= x; lut[i] = (byte)Math.Min(255.0, Math.Floor(y * 255 + 0.5)); }
            var px = new byte[im.w * im.h * 3]; long s0 = 0, s1 = 0, s2 = 0;
            for (int i = 0, s = 0, d = 0; i < im.w * im.h; i++, s += im.c, d += 3)
            {
                px[d] = lut[im.data[s]]; px[d + 1] = lut[im.data[s + 1]]; px[d + 2] = lut[im.data[s + 2]];
                s0 += px[d]; s1 += px[d + 1]; s2 += px[d + 2];
            }
            double n = 255.0 * im.w * im.h;
            mean = new[] { s0 / n, s1 / n, s2 / n };
            return UxBase.TexBc(new UxImage(px, im.h, im.w, 3), false);
        }

        /// The far ground: a G x G RGB TopDown picture (row-major, the client's grid) times each overlay's mean colour by its weight.
        public static void ApplyTopDown(byte[] topdown, int G, Result r)
        {
            UxBin.Require(topdown.Length == G * G * 3 && G % N == 0, "ground overlay: the TopDown picture is not a multiple of the grid");
            int f = G / N;
            for (int row = 0; row < G; row++)
                for (int col = 0; col < G; col++)
                {
                    double w = r.w[(row / f) * N + col / f];
                    if (w == 0) continue;
                    double[] mean = w > 0 ? r.meanA : r.meanB; double a = Math.Abs(w); int p = (row * G + col) * 3;
                    for (int k = 0; k < 3; k++) topdown[p + k] = (byte)Math.Min(255.0, Math.Floor(topdown[p + k] * (1 + (mean[k] - 1) * a) + 0.5));
                }
        }
    }
}
