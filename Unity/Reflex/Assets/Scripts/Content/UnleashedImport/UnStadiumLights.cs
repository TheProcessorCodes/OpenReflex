// Stadium lamp positions recovered from the source's bulb geometry: port of tools/unleashed/stadium_lights.py (stadium-lights.json of
// the supercross stadiums, the freestyle arenas FS01-FS08 and MC06-MC08).
// Every placed model's surfaces whose texture names a bulb / lamp are split into connected pieces; a piece's mean vertex, placed, is
// a light when it stands 12 ft above the terrain centre's ground and 12 ft from every light found before it. The order of the pieces
// is the reference's, which walks a Python set of vertex indices: PySetOrder reproduces CPython 3.9's set layout.
using System;
using System.Collections.Generic;
using System.Threading;
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnStadiumLights
    {
        static readonly string[] Wanted = { "blub", "bulb", "light", "lamp", "fluor" }, Refused = { "sign", "leader", "hole", "support", "pole" };

        public static bool Applies(string track)
        {
            if (track.StartsWith("SX", StringComparison.Ordinal)) return true;
            for (int i = 1; i < 9; i++) if (track == "FS0" + i) return true;
            return track == "MC06" || track == "MC07" || track == "MC08";
        }

        /// The iteration order of a CPython 3.9 set that received these non-negative ints in this order (setobject.c: open addressing,
        /// 9 linear probes, then the perturbed jump; the table grows to the first power of two above 4 x used when 3/5 full).
        public static List<int> PySetOrder(IEnumerable<int> insertions)
        {
            const int LinearProbes = 9, PerturbShift = 5;
            long[] table = new long[8]; for (int i = 0; i < 8; i++) table[i] = -1;
            long mask = 7; long fill = 0;
            foreach (int key in insertions)
            {
                if (key < 0) throw new UnValueError("negative vertex index");
                ulong perturb = (ulong)key; long i0 = key & mask; long slot = -1;
                if (table[i0] < 0) slot = i0;
                else
                {
                    long i = i0; bool found = false;
                    for (; ; )
                    {
                        if (table[i] == key) { found = true; break; }
                        if (i + LinearProbes <= mask)
                        {
                            bool done = false;
                            for (int j = 1; j <= LinearProbes; j++)
                            {
                                if (table[i + j] < 0) { slot = i + j; done = true; break; }
                                if (table[i + j] == key) { found = true; done = true; break; }
                            }
                            if (done) break;
                        }
                        perturb >>= PerturbShift;
                        i = (long)(((ulong)i * 5 + 1 + perturb) & (ulong)mask);
                        if (table[i] < 0) { slot = i; break; }
                    }
                    if (found) continue;
                }
                table[slot] = key; fill++;
                if (fill * 5 < mask * 3) continue;
                long minused = fill > 50000 ? fill * 2 : fill * 4, newsize = 8;
                while (newsize <= minused) newsize <<= 1;
                var old = table; table = new long[newsize]; for (long k = 0; k < newsize; k++) table[k] = -1;
                mask = newsize - 1;
                foreach (long e in old)
                {
                    if (e < 0) continue;
                    ulong p = (ulong)e; long i = e & mask;
                    for (; ; )
                    {
                        if (table[i] < 0) { table[i] = e; break; }
                        bool placed = false;
                        if (i + LinearProbes <= mask)
                            for (int j = 1; j <= LinearProbes; j++) if (table[i + j] < 0) { table[i + j] = e; placed = true; break; }
                        if (placed) break;
                        p >>= PerturbShift;
                        i = (long)(((ulong)i * 5 + 1 + p) & (ulong)mask);
                    }
                }
            }
            var order = new List<int>((int)fill);
            foreach (long e in table) if (e >= 0) order.Add((int)e);
            return order;
        }

        static string Stem(string name)
        {
            string p = name;
            if (p.Length >= 2 && p[1] == ':' && (p[0] >= 'A' && p[0] <= 'Z' || p[0] >= 'a' && p[0] <= 'z')) p = p.Substring(2);
            string b = UnSceneryPaks.BaseName(p.Replace('\\', '/'));
            int i = b.LastIndexOf('.');
            return i > 0 && i < b.Length - 1 ? b.Substring(0, i) : b;
        }

        /// lamp_parts(source, name): the mean vertex of every connected piece of the model's lamp surfaces (model space).
        static List<double[]> LampParts(UnSceneryPaks files, string name)
        {
            string key = UnIni.Lower(Stem(name)) + ".dxg";
            string path = files.ByName(key) ?? throw new KeyNotFoundException(key);
            var m = UnDxg.Parse(files.Read(path)); var o = new List<double[]>();
            if (m.groups.Count == 0) throw new UnValueError("Unleashed model without geometry: " + name);
            foreach (var s in m.groups[0].surfaces)
            {
                if (s.material >= m.textures.Count) throw new UnValueError("Unleashed model: a surface's material is not in " + name);
                string tex = UnIni.Lower(m.textures[(int)s.material]);
                bool want = false, refuse = false;
                foreach (string k in Wanted) if (tex.Contains(k)) want = true;
                foreach (string k in Refused) if (tex.Contains(k)) refuse = true;
                if (!want || refuse) continue;
                if (!s.attrs.TryGetValue(0, out var a0)) throw new UnValueError("Unleashed model: a surface without positions");
                var v = UnDxg.Decode(a0.Key, a0.Value, s.PosScale, out int n);
                int nv = v.Length / n;
                var tri = UnDxg.StripTris(s.indices, s.nverts);
                var parent = new int[nv]; for (int i = 0; i < nv; i++) parent[i] = i;
                int Root(int i)
                {
                    if (i >= nv) throw new UnValueError("Unleashed model: an index outside the vertices of " + name);   // IndexError
                    while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                    return i;
                }
                for (int t = 0; t < tri.Length; t += 3)
                {
                    int ra = Root(tri[t]); parent[Root(tri[t + 1])] = ra;
                    ra = Root(tri[t]); parent[Root(tri[t + 2])] = ra;
                }
                var roots = new List<int>(); var groups = new Dictionary<int, List<int>>();
                foreach (int i in PySetOrder(tri))
                {
                    int r = Root(i);
                    if (!groups.TryGetValue(r, out var ids)) { groups[r] = ids = new List<int>(); roots.Add(r); }
                    ids.Add(i);
                }
                foreach (int r in roots)
                {
                    var ids = groups[r]; var sum = new double[n];
                    for (int k = 0; k < n; k++) sum[k] = v[ids[0] * n + k];
                    for (int j = 1; j < ids.Count; j++) for (int k = 0; k < n; k++) sum[k] += v[ids[j] * n + k];
                    for (int k = 0; k < n; k++) sum[k] /= ids.Count;   // v[ids].mean(0)
                    o.Add(sum);
                }
            }
            return o;
        }

        /// A value read through Python's default (basic) interpolation: "%%" is a percent sign, anything else with '%' is refused.
        static string Interpolated(string v)
        {
            if (v == null || v.IndexOf('%') < 0) return v;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] != '%') { sb.Append(v[i]); continue; }
                if (i + 1 < v.Length && v[i + 1] == '%') { sb.Append('%'); i++; continue; }
                throw new UnValueError("Unleashed scene: a '%' in a value the stadium lights read (Python: InterpolationSyntaxError)");
            }
            return sb.ToString();
        }

        static double[] Vec3(string text)
        {
            var v = UnNum.Floats(Interpolated(text));
            if (v.Length != 3) throw new UnValueError("Unleashed scene: a vector of " + v.Length + " components");
            return v;
        }

        /// write(source, folder, track): stadium-lights.json.
        public static byte[] Write(UnSceneryPaks files, string track, double cx, double cz, CancellationToken ct)
        {
            string scnPath = files.ByName(UnIni.Lower(track + ".scn")) ?? throw new KeyNotFoundException(track + ".scn");
            var c = UnIni.Parse(UnNum.Latin1(files.Read(scnPath)), false);
            string trnPath = files.ByName(UnIni.Lower(track + ".dx.trn")) ?? throw new KeyNotFoundException(track + ".dx.trn");
            var sample = new UnEsbGround(files.Read(trnPath));
            double ground = sample.Sample(cx, cz) ?? throw new UnValueError("float() argument must be a string or a number, not 'NoneType'");
            var lights = new List<object>(); var at = new List<double[]>();
            var hidden = UnSceneRules.HiddenModels(c).hidden;
            var cache = new Dictionary<string, List<double[]>>(StringComparer.Ordinal);
            var all = new List<UnIniSection> { c.Defaults }; all.AddRange(c.Sections);
            foreach (var o in all)
            {
                ct.ThrowIfCancellationRequested();
                if (!o.Has("slt") || hidden.Contains(o.Name)) continue;
                var pos = Vec3(o.Get("position", "0,0,0")); pos[0] -= cx; pos[2] -= cz;
                var rotation = UnNum.Frame(Vec3(o.Get("lookvector", "0,0,1")), Vec3(o.Get("upvector", "0,1,0")));
                string slt = Interpolated(o["slt"]) ?? throw new UnValueError("Unleashed scene: a model without a name");
                if (!cache.TryGetValue(slt, out var parts)) cache[slt] = parts = LampParts(files, slt);
                foreach (var local in parts)
                {
                    if (local.Length != 3) throw new UnValueError("Unleashed model: positions of " + local.Length + " components");
                    UnNum.RowMat3(local[0], local[1], local[2], rotation, out double x, out double y, out double z);
                    x += pos[0]; y += pos[1]; z += pos[2];
                    if (y < ground + 12) continue;
                    bool near = false;
                    foreach (var l in at) if (UnNum.Norm3(x - l[0], y - l[1], z - l[2]) < 12) { near = true; break; }
                    if (near) continue;
                    at.Add(new[] { x, y, z });
                    lights.Add(new JObj().Set("x", x).Set("y", y).Set("z", z).Set("ground", ground));
                }
            }
            return UnJson.DumpsBytes(new JObj().Set("version", 1L).Set("lights", lights));
        }
    }
}
