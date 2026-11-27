// tools/unleashed/racing.py: the course's section graph as one closed Reflex lap and a start chute (race.rxrace).
//   'RXRA', u32 1, u32 kind (1 supercross, 0 other), u32 lap points, u32 lead points, 2 x gate (f32 position[3], forward[3], right[3],
//   width), then the lap's and the lead's points (f32 x, y, z, width, 45).
// A course that cannot be raced is not an error: Assets returns no file and the reason (level.json's race_note), exactly the messages
// the Python tool gives. Arithmetic as in Python: doubles, math.hypot as CPython 3.9 computes it (scaled, compensated sum).
using System;
using System.Collections.Generic;
using System.IO;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnRace
    {
        /// racing.assets(): (race.rxrace bytes or null, the reason when null).
        public static byte[] Assets(UnPak pak, string track, UnTerrain terrain, out string note)
        {
            note = null;
            string name = "Data/Env/" + track + "/" + track + ".tdf";
            if (!pak.Has(name)) { note = "No authored race route"; return null; }
            try
            {
                var course = UnTdf.Parse(pak.Read(name));
                return Payload(course, terrain, track.StartsWith("SX", StringComparison.Ordinal) ? 1 : 0);
            }
            catch (UnValueError e) { note = e.Message; return null; }
        }

        static InvalidDataException Bad(string what) => new InvalidDataException("Unleashed race route: " + what);

        static double[] Need(double[] v, string what) => v ?? throw Bad("a course point has no " + what);

        static double Dist3(double[] a, double[] b) => UnTerrain.Norm(a[0] - b[0], a[1] - b[1], a[2] - b[2]);

        static List<UnTdfPoint> Concat(UnTdf course, List<int> ids, int from, int to)
        {
            var points = new List<UnTdfPoint>();
            for (int n = from; n < to; n++)
            {
                var part = course.byIndex[ids[n]].points;
                if (points.Count > 0)
                {
                    if (part.Count == 0) throw Bad("a section has no points");
                    if (Dist3(Need(points[points.Count - 1].centre, "centre"), Need(part[0].centre, "centre")) > 2) throw new UnValueError("Disconnected source sections");
                    for (int i = 1; i < part.Count; i++) points.Add(part[i]);
                }
                else points.AddRange(part);
            }
            return points;
        }

        /// math.hypot(a, b) of CPython 3.8 / 3.9 (vector_norm).
        public static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            if (double.IsInfinity(a) || double.IsInfinity(b)) return double.PositiveInfinity;
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            double max = a > b ? a : b;
            if (max == 0.0) return max;
            double csum = 1.0, frac = 0.0;
            for (int i = 0; i < 2; i++)
            {
                double x = i == 0 ? a : b;
                x = x / max;
                x = x * x;
                double old = csum;
                csum = csum + x;
                double d = old - csum;
                d = d + x;
                frac = frac + d;
            }
            double s = csum - 1.0;
            s = s + frac;
            return max * Math.Sqrt(s);
        }

        static List<double[]> Dense(List<double[]> points)
        {
            var o = new List<double[]>();
            for (int k = 0; k + 1 < points.Count; k++)
            {
                double[] a = points[k], b = points[k + 1];
                double d = Hypot(b[0] - a[0], b[2] - a[2]);
                if (d < .01) continue;
                int n = Math.Max(1, (int)Math.Ceiling(d / 10));
                for (int i = 0; i < n; i++)
                {
                    var p = new double[5];
                    for (int c = 0; c < 5; c++)
                    {
                        double step = (b[c] - a[c]) * i;
                        step = step / n;
                        p[c] = a[c] + step;
                    }
                    o.Add(p);
                }
            }
            if (points.Count == 0) throw Bad("empty route");
            o.Add(points[points.Count - 1]);
            return o;
        }

        static int Nearest(List<double[]> loop, double tx, double tz)
        {
            int best = -1; double bd = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                double d = UnTerrain.Norm(loop[i][0] - tx, loop[i][2] - tz);
                if (best < 0 || d < bd) { best = i; bd = d; }
            }
            if (best < 0) throw new UnValueError("min() arg is an empty sequence");
            return best;
        }

        /// racing.payload(course, center, sample, kind).
        public static byte[] Payload(UnTdf course, UnTerrain terrain, int kind)
        {
            var order = new List<int>(); var seen = new Dictionary<int, int>();
            int current = 0;
            while (!seen.ContainsKey(current))
            {
                if (!course.byIndex.TryGetValue(current, out var sec)) throw new UnValueError("Broken source section link");
                seen[current] = order.Count; order.Add(current);
                if (sec.links.Count == 0) throw new UnValueError("Source route is point-to-point");
                int low = int.MaxValue;
                foreach (int key in sec.links.Keys) if (key < low) low = key;
                current = sec.links[low];
            }
            int split = seen[current];
            var raw = Concat(course, order, split, order.Count);
            if (raw.Count < 4 || Dist3(Need(raw[0].centre, "centre"), Need(raw[raw.Count - 1].centre, "centre")) > 2) throw new UnValueError("Source route has no closed circuit");
            double cx = terrain.centerX, cz = terrain.centerZ;
            double[] Point(UnTdfPoint p)
            {
                var c = Need(p.centre, "centre"); var r = Need(p.right, "right edge"); var l = Need(p.left, "left edge");
                double width = Dist3(r, l);
                if (width < 10) width = 10; if (width > 300) width = 300;
                return new[] { c[0] - cx, terrain.Sample(c[0], c[2]), c[2] - cz, width, 45.0 };
            }
            var loop = new List<double[]>();
            for (int i = 0; i + 1 < raw.Count; i++) loop.Add(Point(raw[i]));
            var finish = course.Gate("finish_gate") ?? throw Bad("the course has no finish gate");
            int k = Nearest(loop, finish.pos[0] - cx, finish.pos[2] - cz);
            var lap = new List<double[]>();
            for (int i = k; i < loop.Count; i++) lap.Add(loop[i]);
            for (int i = 0; i < k; i++) lap.Add(loop[i]);
            lap.Add(loop[k]);
            var lead = new List<double[]>();
            if (split != 0)
            {
                foreach (var p in Concat(course, order, 0, split)) lead.Add(Point(p));
                for (int i = 1; i < k + 1 && i < loop.Count; i++) lead.Add(loop[i]);
            }
            else
            {
                var start = course.Gate("start_gate") ?? throw Bad("the course has no start gate");
                int j = Nearest(loop, start.pos[0] - cx, start.pos[2] - cz);
                if (j < k) for (int i = j; i < k + 1; i++) lead.Add(loop[i]);
                else
                {
                    for (int i = j; i < loop.Count; i++) lead.Add(loop[i]);
                    for (int i = 0; i < k + 1; i++) lead.Add(loop[i]);
                }
                if (lead.Count < 2) { lead.Clear(); lead.Add(loop[((k - 1) % loop.Count + loop.Count) % loop.Count]); lead.Add(loop[k]); }
            }
            lap = Dense(lap); lead = Dense(lead);
            if (Math.Max(lap.Count, lead.Count) > 4096) throw new UnValueError("Race route too long");
            var o = new byte[20 + 80 + 20 * (lap.Count + lead.Count)];
            o[0] = (byte)'R'; o[1] = (byte)'X'; o[2] = (byte)'R'; o[3] = (byte)'A';
            UnTerrain.PutU32(o, 4, 1); UnTerrain.PutU32(o, 8, (uint)kind); UnTerrain.PutU32(o, 12, (uint)lap.Count); UnTerrain.PutU32(o, 16, (uint)lead.Count);
            int at = 20;
            foreach (var points in new[] { lead, lap })
            {
                if (points.Count < 2) throw Bad("a route of one point");
                double[] a = points[0], b = points[1];
                double d0 = b[0] - a[0], d2 = b[2] - a[2];
                double n = UnTerrain.Norm(d0, 0, d2);
                d0 = d0 / n; double d1 = 0 / n; d2 = d2 / n;
                foreach (double v in new[] { a[0], a[1], a[2], d0, d1, d2, -d2, 0, d0, a[3] }) { UnTerrain.PutF32(o, at, (float)v); at += 4; }
            }
            foreach (var points in new[] { lap, lead })
                foreach (var p in points)
                    for (int c = 0; c < 5; c++) { UnTerrain.PutF32(o, at, (float)p[c]); at += 4; }
            return o;
        }
    }
}
