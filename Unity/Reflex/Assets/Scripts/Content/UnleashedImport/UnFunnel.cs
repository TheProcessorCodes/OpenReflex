// The funnel blockers of an Unleashed race level (the bales / barrels that shape the start): port of tools/unleashed/funnel.py.
// Source: the track's .aty [FunnelBlockDown] / [FunnelBlockUp]. The two sets leave the static scenery: their meshes go into the RXSC
// groups UnleashedFunnel:down / :up, and funnel.rxfunnel carries the gate, the moves and each set's collision triangles:
//   'RXFN' u32 version (1), f32 gate position x, y, z, f32 gate look x, y, z, f32 width, height, f32 rate (ft/s)
//   2 x { f32 direction x, y, z, f32 distance, u32 vertices (3 per triangle), vertices x f32 x, y, z }   (0 = down, 1 = up)
using System;
using System.Collections.Generic;
using System.IO;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnFunnelSet
    {
        public string name;
        public double[] direction, position, look;
        public double distance, rate, width, height;
    }

    public static class UnFunnel
    {
        public static readonly string[] Names = { "down", "up" };

        /// number(text, default).
        static double Number(string text, double fallback = 0.0) => UnSceneryConverter.Number(text, fallback);

        /// sections(track, files, read): [down, up], or null.
        public static UnFunnelSet[] Sections(string track, UnSceneryPaks files)
        {
            string path = "data/env/" + track + "/" + track + ".aty";
            if (!files.Has(path)) return null;
            var aty = UnToys.Ini(UnNum.Latin1(files.Read(path)));
            var o = new UnFunnelSet[2];
            string[] keys = { "funnelblockdown", "funnelblockup" };
            for (int k = 0; k < 2; k++)
            {
                if (!aty.TryGetValue(keys[k], out var s) || s.Count == 0) return null;
                if (!s.TryGetValue("collectionname", out string collection) || collection.Length == 0) return null;
                string Get(string key) => s.TryGetValue(key, out var v) ? v : null;
                var direction = UnToys.Vec(Get("movedirection"), 0, 0, 0); double distance = Number(Get("movedistance")), rate = Number(Get("moverate"));
                var look = UnToys.Vec(Get("triggerlook"), 0, 0, 1); look[1] = 0;
                double dn = UnNum.Norm3(direction[0], direction[1], direction[2]), ln = UnNum.Norm3(look[0], look[1], look[2]);
                if (dn < .5 || !(0 < distance && distance <= 200) || !(0 < rate && rate <= 1000) || ln < .01) return null;
                o[k] = new UnFunnelSet
                {
                    name = UnIni.Lower(UnNum.StripChars(UnIni.Strip(collection), '"')),
                    direction = new[] { direction[0] / dn, direction[1] / dn, direction[2] / dn }, distance = distance, rate = rate,
                    position = UnToys.Vec(Get("triggerposition"), 0, 0, 0), look = new[] { look[0] / ln, look[1] / ln, look[2] / ln },
                    width = Number(Get("triggerwidth"), 100), height = Number(Get("triggerheight"), 200),
                };
            }
            return o;
        }

        /// which(config, model): 0 (down) / 1 (up) for a model of one of the two collections, else -1.
        public static int Which(UnFunnelSet[] config, string model)
        {
            if (config == null) return -1;
            string low = UnIni.Lower(UnIni.Strip(model));
            for (int k = 0; k < config.Length; k++) if (low.StartsWith(config[k].name, StringComparison.Ordinal)) return k;
            return -1;
        }

        /// triangles(collider, model, pos, rotation): the model's collision mesh (<model>f.col) at its scene pose, 3 values per corner
        /// (as the singles funnel.pack stores; empty without the file).
        public static double[] Triangles(UnSceneryConverter c, string model, double[] pos, double[] rotation)
        {
            var shape = c.Collider(model + "f");
            if (shape == null) return new double[0];
            var o = new List<double>();
            foreach (var part in shape.Parts)
            {
                if (!part.hasVerts) continue;
                foreach (float f in UnSceneryConverter.CollisionPoints(part, rotation, pos, false)) o.Add(f);
            }
            return o.ToArray();
        }

        /// pack(config, center, tris): funnel.rxfunnel.
        public static byte[] Pack(UnFunnelSet[] config, double cx, double cz, List<double[]>[] tris)
        {
            var gate = config[1];
            using (var o = new MemoryStream())
            {
                var h = new byte[44]; h[0] = (byte)'R'; h[1] = (byte)'X'; h[2] = (byte)'F'; h[3] = (byte)'N'; UnNum.PutI32(h, 4, 1);
                UnNum.PutF32(h, 8, gate.position[0] - cx); UnNum.PutF32(h, 12, gate.position[1]); UnNum.PutF32(h, 16, gate.position[2] - cz);
                UnNum.PutF32(h, 20, gate.look[0]); UnNum.PutF32(h, 24, gate.look[1]); UnNum.PutF32(h, 28, gate.look[2]);
                UnNum.PutF32(h, 32, gate.width); UnNum.PutF32(h, 36, gate.height); UnNum.PutF32(h, 40, Math.Min(config[0].rate, config[1].rate));
                o.Write(h, 0, h.Length);
                for (int k = 0; k < 2; k++)
                {
                    int values = 0; foreach (var t in tris[k]) values += t.Length;
                    int points = values / 3;
                    foreach (var t in tris[k]) foreach (double v in t) if (!UnNum.Finite(v)) throw new UnValueError("Invalid funnel collision");
                    if (points > 60000) throw new UnValueError("Invalid funnel collision");
                    var b = new byte[20 + values * 4];
                    UnNum.PutF32(b, 0, config[k].direction[0]); UnNum.PutF32(b, 4, config[k].direction[1]); UnNum.PutF32(b, 8, config[k].direction[2]);
                    UnNum.PutF32(b, 12, config[k].distance); UnNum.PutI32(b, 16, points);
                    int at = 20;
                    foreach (var t in tris[k]) foreach (double v in t) { UnNum.PutF32Raw(b, at, (float)v); at += 4; }
                    o.Write(b, 0, b.Length);
                }
                return o.ToArray();
            }
        }
    }
}
