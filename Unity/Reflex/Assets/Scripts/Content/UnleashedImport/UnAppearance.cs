// tools/unleashed/appearance.py assets(): the level's look files.
//   terrain-style.json   the track's terrain profile (terrain_profiles.json), as json.dumps(indent=2) writes it
//   surface.rxsurface    'RXSM', u32 1, u32 2048 + 2048 x 2048 RGBA weights: R grass / cover, G mud, B stone / pavement, A snow cover.
//                        From the terrain's material ids; where the terrain only says "dirt" (id 0), from the source colour and slope
//   ruts.json            the course's centre line segments (x, z, width) for the rut pass
//   preview.rximage      the track's menu picture from UI.pak ('RXIM', u32 1, u32 w, u32 h + RGBA); required
//   map.rximage          its map picture, when UI.pak has one
// The surface arithmetic is numpy's on float32 arrays: every operation rounds to single precision on its own (the casts below keep
// that true on runtimes that widen float expressions), Python float constants enter as their nearest single, np.clip = min(max()),
// astype(uint8) truncates. np.gradient: central differences / (2 * cell) inside, one-sided / cell on the border rows and columns.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnAppearance
    {
        const int N = UnTerrain.N;

        static float Clip01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// name -> bytes. colour: the level's 2048 x 2048 RGBA colour map (UnTerrain.Colour); uiPak: the source's UI.pak.
        public static Dictionary<string, byte[]> Build(UnCatalogEntry entry, UnTerrain terrain, UnTdf course, byte[] colour, UnPak uiPak, CancellationToken ct = default)
        {
            string track = entry.id;
            var profiles = UnJson.Obj(UnJson.Loads(UnData.Text(UnData.TerrainProfiles)));
            if (entry.terrain_profile == null || !profiles.Has(entry.terrain_profile)) throw new KeyNotFoundException("terrain profile " + entry.terrain_profile);
            var style = UnJson.Obj(profiles.Get(entry.terrain_profile));
            string styleName = style.Get("name") as string, cover = style.Get("cover") as string;
            if (!style.Has("name") || !style.Has("cover")) throw new KeyNotFoundException("terrain profile " + entry.terrain_profile + ": name / cover");

            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            result["terrain-style.json"] = UnJson.DumpsBytes(style, 2);
            result["surface.rxsurface"] = UnTerrain.Tagged("RXSM", Surface(terrain, colour, styleName, cover, ct));

            var segments = new List<object>();
            if (course != null)
            {
                double hx = terrain.extentX / 2, hz = terrain.extentZ / 2;
                foreach (var section in course.sections)
                    for (int k = 0; k + 1 < section.points.Count; k++)
                        for (int e = 0; e < 2; e++)
                        {
                            var point = section.points[k + e];
                            if (point.centre == null || point.left == null || point.right == null) throw new KeyNotFoundException("a course point lacks its centre or edges");
                            double width = UnTerrain.Norm(point.left[0] - point.right[0], point.left[1] - point.right[1], point.left[2] - point.right[2]);
                            double wide = width * .3; if (wide < 3) wide = 3; if (wide > 18) wide = 18;
                            segments.Add(point.centre[0] - hx); segments.Add(point.centre[2] - hz); segments.Add(wide);
                        }
            }
            result["ruts.json"] = UnJson.DumpsBytes(new JObj().Set("segments", segments));

            string low = UnIni.Lower(track);
            foreach (var kind in new[] { "preview", "map" })
            {
                var candidates = new List<string>();
                foreach (var s in kind == "preview" ? new[] { "_preview_01", "_preview_1", "_preview", "_lobby" } : new[] { "_map", "_map_ss" })
                    candidates.Add("data/ui/tex/" + low + s + ".dxt");
                if (kind == "preview") { candidates.Add("data/ui/tex/ss_" + low + "_lobby.dxt"); candidates.Add("data/ui/tex/store_" + low + ".dxt"); }
                string key = null;
                foreach (var c in candidates) if (uiPak.Has(c)) { key = c; break; }
                if (key == null)
                {
                    if (kind == "map") continue;
                    throw new UnValueError("No source preview for " + track);
                }
                var picture = UnDxt.Decode(uiPak.Read(key)) ?? throw new UnValueError("Invalid picture " + key);
                var o = new byte[16 + picture.rgba.Length];
                o[0] = (byte)'R'; o[1] = (byte)'X'; o[2] = (byte)'I'; o[3] = (byte)'M';
                UnTerrain.PutU32(o, 4, 1); UnTerrain.PutU32(o, 8, (uint)picture.width); UnTerrain.PutU32(o, 12, (uint)picture.height);
                Buffer.BlockCopy(picture.rgba, 0, o, 16, picture.rgba.Length);
                result[kind + ".rximage"] = o;
            }
            return result;
        }

        static byte[] Surface(UnTerrain t, byte[] colour, string styleName, string cover, CancellationToken ct)
        {
            int w = t.w, h = t.h;
            var trn = t.trn;
            // the material grid (ids, 7 bits), zero where no leaf covers
            var ids = new byte[w * h];
            foreach (var n in trn.nodes)
            {
                if (n.level != 0) continue;
                int at = trn.MaterialAt(n); byte[] src = trn.Bytes;
                for (int vz = 0; vz < 17; vz++)
                    for (int vx = 0; vx < 17; vx++) ids[(n.z + vz) * w + n.x + vx] = (byte)(src[at + vz * 17 + vx] & 127);
            }
            // nearest source vertex of every level grid column / row
            var ix = new int[N]; var iz = new int[N];
            for (int i = 0; i < N; i++)
            {
                double rx = Math.Round(t.x[i] / t.cell, MidpointRounding.ToEven), rz = Math.Round(t.z[i] / t.cell, MidpointRounding.ToEven);
                ix[i] = rx < 0 ? 0 : rx > w - 1 ? w - 1 : (int)rx;
                iz[i] = rz < 0 ? 0 : rz > h - 1 ? h - 1 : (int)rz;
            }
            // the heights as float32 and the slope at the vertices the level grid uses
            var hf = new float[w * h];
            for (int i = 0; i < hf.Length; i++) hf[i] = (float)t.heights[i];
            float cell = (float)t.cell, cell2 = (float)(2.0 * t.cell);
            float Slope(int z, int x)
            {
                int p = z * w + x;
                float dz = z == 0 ? (float)((float)(hf[p + w] - hf[p]) / cell) : z == h - 1 ? (float)((float)(hf[p] - hf[p - w]) / cell) : (float)((float)(hf[p + w] - hf[p - w]) / cell2);
                float dx = x == 0 ? (float)((float)(hf[p + 1] - hf[p]) / cell) : x == w - 1 ? (float)((float)(hf[p] - hf[p - 1]) / cell) : (float)((float)(hf[p + 1] - hf[p - 1]) / cell2);
                float s = (float)((float)(dx * dx) + (float)(dz * dz));
                return (float)Math.Sqrt(s);
            }
            if (w < 2 || h < 2) throw new UnValueError("Shape of array too small to calculate a numerical gradient, at least (edge_order + 1) elements are required.");
            bool woodland = styleName == "woodland" || styleName == "dry_woodland" || styleName == "red_clay";
            bool sparse = cover == "sparse_grass", snow = cover == "snow", pureSnow = styleName == "snow";
            bool supermoto = styleName == "supermoto", dunes = styleName == "dunes" || styleName == "coastal";
            const float k299 = .299f, k587 = .587f, k114 = .114f;
            var surface = new byte[N * N * 4];
            for (int j = 0; j < N; j++)
            {
                ct.ThrowIfCancellationRequested();
                int zrow = iz[j];
                for (int i = 0; i < N; i++)
                {
                    int o = (j * N + i) * 4;
                    int id = ids[zrow * w + ix[i]];
                    byte s0 = (byte)(id == 3 || id == 12 ? 255 : 0);
                    byte s1 = (byte)(id == 2 || id == 6 || id == 7 ? 255 : 0);
                    byte s2 = (byte)(id == 1 || id == 5 || id == 8 || id == 9 ? 255 : 0);
                    byte s3 = (byte)(snow ? 255 : 0);
                    if (id == 0)
                    {
                        float r = colour[o], g = colour[o + 1], b = colour[o + 2];
                        float gr = (float)(g - r), gb = (float)(g - b);
                        float green;
                        if (sparse) green = Clip01(Math.Min((float)((float)(gr - 8f) / 22f), (float)((float)(gb - 12f) / 25f)));
                        else if (woodland) green = Clip01(Math.Min((float)((float)(gr + 5f) / 12f), (float)((float)(gb - 4f) / 16f)));
                        else green = Clip01(Math.Min((float)((float)(gr - 2f) / 16f), (float)((float)(gb - 5f) / 20f)));
                        float slope = Slope(zrow, ix[i]);
                        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                        float chroma = (float)(max - min);
                        float grey = (float)(1f - Clip01((float)((float)(chroma - 12f) / 35f)));
                        float rock = (float)((float)(Clip01((float)((float)(slope - .65f) / .8f)) * grey) * (float)(1f - green));
                        if (snow)
                        {
                            float lum = (float)((float)((float)(k299 * r) + (float)(k587 * g)) + (float)(k114 * b));
                            if (pureSnow) { green = 1f; rock = 0f; }
                            else
                            {
                                float neutral = Clip01((float)(1f - (float)(chroma / Math.Max((float)(lum * .45f), 1f))));
                                green = (float)(Clip01((float)((float)(lum - 45f) / 65f)) * neutral);
                                rock = (float)(Clip01((float)((float)(slope - 1.6f) / 1.2f)) * (float)(1f - green));
                            }
                        }
                        else if (supermoto)
                        {
                            float lum = (float)((float)((float)(k299 * r) + (float)(k587 * g)) + (float)(k114 * b));
                            float neutral = Clip01((float)(1f - (float)(chroma / Math.Max((float)(lum * .14f), 1f))));
                            rock = (float)((float)((float)(neutral * Clip01((float)((float)(lum - 25f) / 30f))) * (float)(1f - green)) * Clip01((float)((float)(.6f - slope) / .3f)));
                        }
                        else if (dunes) rock = 0f;
                        s0 = (byte)(int)(float)(green * 255f);
                        s2 = (byte)(int)(float)(rock * 255f);
                    }
                    surface[o] = s0; surface[o + 1] = s1; surface[o + 2] = s2; surface[o + 3] = s3;
                }
            }
            return surface;
        }
    }
}
