// The animated props of an Unleashed level (the .toy's [AnimatedToys]: the ferris wheel, the stadiums' finish arch, windmills ...):
// port of tools/unleashed/toys.py. One hidden RXSC group per (model, node) with the node's triangles; the motion, sampled with the
// game's own key math (UnMot), baked into toys.rxtoys:
//   'RXTY' u32 version (1), u32 toys
//   per toy:  str name, str model, u8 looping, f32 duration, f32 initial time, u32 samples, u32 parts
//             per part:  str group (the RXSC group that holds its mesh)
//             samples x parts x { f32 x, y, z;  f32 qx, qy, qz, qw }   the part's frame in level space (terrain-centred feet)
//   (str = u16 length + UTF-8.) Sample k is at k x duration / (samples - 1); one sample = a prop that does not move.
// Not bit-exact with the reference in one place: a triangle whose corners belong to different nodes carries the foreign corners
// through the inverse of its node's bind frame (numpy's LAPACK inverse; UnNum.Inv4 agrees to the last places, not always the last bit).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnToysResult
    {
        public readonly List<string> groups = new List<string>();
        public byte[] data;
        public readonly List<object> report = new List<object>();
        /// Vertices that went through the inverse bind frame (the one step that is not bit-exact with the reference).
        public int foreignCorners;
    }

    public static class UnToys
    {
        const double Rate = 30.0;   // samples per second (the motions are keyed at 30)

        /// Diagnostic (tests): corners carried through the inverse bind frame since the process started - the one step that is not
        /// bit-exact with the reference.
        public static int ForeignCornersSeen;

        /// ini(text): sections and keys, case-insensitive, the first value of a repeated key (the game's Ini).
        public static Dictionary<string, Dictionary<string, string>> Ini(string text)
        {
            var secs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Dictionary<string, string> cur = null;
            foreach (string raw in UnNum.SplitLines(text))
            {
                string line = raw; int semi = line.IndexOf(';'); if (semi >= 0) line = line.Substring(0, semi);
                line = UnIni.Strip(line);
                int close = line.IndexOf(']');
                if (line.StartsWith("[", StringComparison.Ordinal) && close >= 0)
                {
                    string name = UnIni.Lower(UnIni.Strip(line.Substring(1, close - 1)));
                    if (!secs.TryGetValue(name, out cur)) secs[name] = cur = new Dictionary<string, string>(StringComparer.Ordinal);
                }
                else
                {
                    int eq = line.IndexOf('=');
                    if (eq >= 0 && cur != null)
                    {
                        string k = UnIni.Lower(UnIni.Strip(line.Substring(0, eq)));
                        if (!cur.ContainsKey(k)) cur[k] = UnIni.Strip(line.Substring(eq + 1));
                    }
                }
            }
            return secs;
        }

        /// vec(text, default): three finite floats, else the default.
        public static double[] Vec(string text, double dx, double dy, double dz)
        {
            if (text != null)
            {
                var parts = text.Split(',');
                int n = Math.Min(3, parts.Length); var v = new double[n]; bool ok = true;
                for (int i = 0; i < n && ok; i++) ok = UnNum.TryFloat(parts[i], out v[i]);
                if (ok && n == 3 && UnNum.Finite(v[0]) && UnNum.Finite(v[1]) && UnNum.Finite(v[2])) return v;
            }
            return new[] { dx, dy, dz };
        }

        /// truth(text).
        public static bool Truth(string text)
        {
            string c = UnIni.Strip(text ?? "").ToUpperInvariant();
            if (c.StartsWith("T", StringComparison.Ordinal) || c.StartsWith("Y", StringComparison.Ordinal) || c.StartsWith("ON", StringComparison.Ordinal)) return true;
            return UnNum.TryIntLiteral(c, out long v) && v != 0;
        }

        /// quat_matrix(q): the rotation rows of a motion quaternion (am_m4_from_quat_4b3ed0).
        static void QuatMatrix(float[] q, double[] m)
        {
            double x = q[0], y = q[1], z = q[2], w = q[3];
            double x2 = x + x, y2 = y + y, z2 = z + z;
            m[0] = 1 - (z2 * z + y2 * y); m[1] = y2 * x + z2 * w; m[2] = z2 * x - y2 * w;
            m[4] = y2 * x - z2 * w; m[5] = 1 - (z2 * z + x2 * x); m[6] = z2 * y + x2 * w;
            m[8] = z2 * x + y2 * w; m[9] = z2 * y - x2 * w; m[10] = 1 - (y2 * y + x2 * x);
        }

        /// matrix_quat(rows): the unit quaternion (x, y, z, w) of the 3 x 3 part of a 4 x 4 frame.
        static void MatrixQuat(double[] f, double[] q)
        {
            var m = new double[9];
            for (int i = 0; i < 3; i++)
            {
                double a = f[i * 4], b = f[i * 4 + 1], c = f[i * 4 + 2], n = UnNum.Norm3(a, b, c);
                if (n > 1e-12) { m[i * 3] = a / n; m[i * 3 + 1] = b / n; m[i * 3 + 2] = c / n; }
                else { m[i * 3] = i == 0 ? 1 : 0; m[i * 3 + 1] = i == 1 ? 1 : 0; m[i * 3 + 2] = i == 2 ? 1 : 0; }
            }
            double t = m[0] + m[4] + m[8], s;
            if (t > 0) { s = Math.Sqrt(t + 1) * 2; q[0] = (m[5] - m[7]) / s; q[1] = (m[6] - m[2]) / s; q[2] = (m[1] - m[3]) / s; q[3] = s / 4; }
            else if (m[0] > m[4] && m[0] > m[8]) { s = Math.Sqrt(1 + m[0] - m[4] - m[8]) * 2; q[0] = s / 4; q[1] = (m[1] + m[3]) / s; q[2] = (m[6] + m[2]) / s; q[3] = (m[5] - m[7]) / s; }
            else if (m[4] > m[8]) { s = Math.Sqrt(1 + m[4] - m[0] - m[8]) * 2; q[0] = (m[1] + m[3]) / s; q[1] = s / 4; q[2] = (m[5] + m[7]) / s; q[3] = (m[6] - m[2]) / s; }
            else { s = Math.Sqrt(1 + m[8] - m[0] - m[4]) * 2; q[0] = (m[6] + m[2]) / s; q[1] = (m[5] + m[7]) / s; q[2] = s / 4; q[3] = (m[1] - m[3]) / s; }
            double qn = UnNum.Norm4(q[0], q[1], q[2], q[3]);
            q[0] /= qn; q[1] /= qn; q[2] /= qn; q[3] /= qn;
        }

        static byte[] Str(string value)
        {
            var b = Encoding.UTF8.GetBytes(value);
            if (b.Length > 65535) throw new UnValueError("ushort format requires 0 <= number <= 65535");
            var r = new byte[2 + b.Length]; r[0] = (byte)b.Length; r[1] = (byte)(b.Length >> 8); Buffer.BlockCopy(b, 0, r, 2, b.Length); return r;
        }

        static string BeforeLastDot(string s) { int i = s.LastIndexOf('.'); return i < 0 ? s : s.Substring(0, i); }

        static double[] Matrix16(float[] m) { var d = new double[16]; for (int i = 0; i < 16; i++) d[i] = m[i]; return d; }

        sealed class ModelParts
        {
            public readonly SortedDictionary<int, string> named = new SortedDictionary<int, string>();
            public List<UnDxgNode> nodes;
        }

        /// convert(track, files, read, center, material, emit_draw, first_group): draws go out through the converter into the groups
        /// first_group and up.
        public static UnToysResult Convert(UnSceneryConverter c, string track, int firstGroup, CancellationToken ct)
        {
            var r = new UnToysResult(); var files = c.Files;
            string path = "data/env/" + track + "/" + track + ".toy";
            if (!files.Has(path)) return r;
            var toy = Ini(UnNum.Latin1(files.Read(path)));
            int count = 0;
            {
                string text = toy.TryGetValue("animatedtoys", out var at) && at.TryGetValue("numberofanimatedtoys", out var cn) ? cn : "0";
                if (text.Length > 0 && UnNum.TryFloat(text, out double f)) count = UnNum.Int(f);
            }
            var models = new Dictionary<string, ModelParts>(StringComparer.Ordinal); var o = new List<byte[]>();
            string folder = UnIni.Lower("data/env/" + track + "/anim/");

            ModelParts PartsOf(string slt)
            {
                if (models.TryGetValue(slt, out var have)) return have;
                var mesh = UnDxg.Parse(files.Read(folder + slt + ".dxg"));
                var nodes = mesh.nodes; var palettes = mesh.palettes ?? new List<byte[]>();
                var byNode = new SortedDictionary<int, List<UnSceneryPart>>();
                // the bind frames of the nodes in model space: a triangle is drawn by its first vertex's node, so a corner that belongs to
                // another node is carried into that node's frame as the bind pose places it
                var bind = new double[nodes.Count][];
                double[] BindOf(int k, int depth)
                {
                    if (bind[k] == null)
                    {
                        int parent = nodes[k].parent; var m = Matrix16(nodes[k].matrix);
                        bind[k] = parent >= 0 && parent < nodes.Count && parent != k && depth < 64 ? UnNum.Mul4(m, BindOf(parent, depth + 1)) : m;
                    }
                    return bind[k];
                }
                for (int k = 0; k < nodes.Count; k++) BindOf(k, 0);
                var unbind = new double[nodes.Count][];
                if (mesh.groups.Count == 0) throw new UnValueError("Unleashed model without geometry: " + slt);
                foreach (var surf in mesh.groups[0].surfaces)
                {
                    UnSceneryConverter.Streams(surf, out double[] vertices, out int nv, out double[] uv, out double[] uv1);
                    var colors = UnSceneryConverter.Colors(surf, nv);
                    // mxa_dxg_bones: the vertex's palette slot -> its node (none: node 0, as the reference's toy renderer)
                    var owner = new int[nv];
                    if (surf.Has(4))
                    {
                        var slot = UnDxg.Decode(surf.attrs[4].Key, surf.attrs[4].Value, 1.0, out _);
                        if (slot.Length > nv) throw new UnValueError("Unleashed model: bone indices do not match the positions");   // IndexError
                        int pi = surf.palette ?? 0; byte[] palette = palettes.Count > 0 ? palettes[pi < palettes.Count ? pi : 0] : null;
                        for (int i = 0; i < slot.Length; i++)
                        {
                            int s = UnNum.Int(slot[i]);
                            int node = palette != null ? (s >= 0 && s < palette.Length ? palette[s] : -1) : s;
                            owner[i] = node >= 0 && node < nodes.Count ? node : 0;
                        }
                    }
                    var indices = UnDxg.StripTris(surf.indices, surf.nverts);
                    int ntri = indices.Length / 3;
                    if (ntri == 0) continue;
                    foreach (int ix in indices) if (ix >= nv) throw new UnValueError("Unleashed model: an index outside the vertices of " + slt);
                    if (surf.material >= mesh.textures.Count) throw new UnValueError("Unleashed model: a surface's material is not in " + slt);
                    int mi = c.Material(mesh.textures[(int)surf.material]);
                    // a triangle goes with its first vertex's node
                    var leads = new SortedSet<int>();
                    for (int t = 0; t < ntri; t++) leads.Add(owner[indices[t * 3]]);
                    foreach (int node in leads)
                    {
                        var usedSet = new SortedSet<int>(); var tris = new List<int>();
                        for (int t = 0; t < ntri; t++)
                            if (owner[indices[t * 3]] == node) for (int k = 0; k < 3; k++) { tris.Add(indices[t * 3 + k]); usedSet.Add(indices[t * 3 + k]); }
                        var used = new List<int>(usedSet); var remap = new Dictionary<int, int>();
                        for (int i = 0; i < used.Count; i++) remap[used[i]] = i;
                        var points = new double[used.Count * 3];
                        for (int i = 0; i < used.Count; i++) { points[i * 3] = vertices[used[i] * 3]; points[i * 3 + 1] = vertices[used[i] * 3 + 1]; points[i * 3 + 2] = vertices[used[i] * 3 + 2]; }
                        var others = new SortedSet<int>();
                        foreach (int u in used) if (owner[u] != node) others.Add(owner[u]);
                        foreach (int other in others)
                        {
                            var take = new List<int>();
                            for (int i = 0; i < used.Count; i++) if (owner[used[i]] == other) take.Add(i);
                            var rows = new double[take.Count * 4];
                            for (int i = 0; i < take.Count; i++) { rows[i * 4] = points[take[i] * 3]; rows[i * 4 + 1] = points[take[i] * 3 + 1]; rows[i * 4 + 2] = points[take[i] * 3 + 2]; rows[i * 4 + 3] = 1.0; }
                            if (unbind[node] == null) unbind[node] = UnNum.Inv4(bind[node]) ?? throw new UnValueError("Singular matrix");
                            rows = UnNum.RowsMul4(UnNum.RowsMul4(rows, take.Count, bind[other]), take.Count, unbind[node]);
                            for (int i = 0; i < take.Count; i++) { points[take[i] * 3] = rows[i * 4]; points[take[i] * 3 + 1] = rows[i * 4 + 1]; points[take[i] * 3 + 2] = rows[i * 4 + 2]; }
                            r.foreignCorners += take.Count; System.Threading.Interlocked.Add(ref ForeignCornersSeen, take.Count);
                        }
                        var part = new UnSceneryPart { material = mi, count = used.Count, vertices = points, uv0 = new double[used.Count * 2], uv1 = new double[used.Count * 2], indices = new int[tris.Count], colors = new byte[used.Count * 4] };
                        for (int i = 0; i < used.Count; i++)
                        {
                            int u = used[i];
                            part.uv0[i * 2] = uv[u * 2]; part.uv0[i * 2 + 1] = uv[u * 2 + 1]; part.uv1[i * 2] = uv1[u * 2]; part.uv1[i * 2 + 1] = uv1[u * 2 + 1];
                            Buffer.BlockCopy(colors, u * 4, part.colors, i * 4, 4);
                        }
                        for (int i = 0; i < tris.Count; i++) part.indices[i] = remap[tris[i]];
                        if (!byNode.TryGetValue(node, out var list)) byNode[node] = list = new List<UnSceneryPart>();
                        list.Add(part);
                    }
                }
                var mp = new ModelParts { nodes = nodes };
                foreach (var kv in byNode)
                {
                    string name = "UnleashedToy:" + slt + ":" + kv.Key.ToString(CultureInfo.InvariantCulture);
                    mp.named[kv.Key] = name; int index = firstGroup + r.groups.Count; r.groups.Add(name);
                    foreach (var part in kv.Value) c.EmitDraw(part, index);
                }
                models[slt] = mp; return mp;
            }

            for (int ti = 1; ti <= count; ti++)
            {
                ct.ThrowIfCancellationRequested();
                if (!toy.TryGetValue("animatedtoy" + ti.ToString(CultureInfo.InvariantCulture), out var sec) || sec.Count == 0) continue;
                string Get(string k) => sec.TryGetValue(k, out var v) ? v : null;
                string name = Get("name") ?? "toy" + ti.ToString(CultureInfo.InvariantCulture);
                string mcf = UnIni.Lower(BeforeLastDot(Get("mcfname") ?? ""));
                if (mcf.Length == 0 || !files.Has(folder + mcf + ".mcf")) { r.report.Add(new JObj().Set("name", name).Set("state", "missing motion list " + mcf)); continue; }
                var motions = UnMot.ParseMcf(UnNum.Latin1(files.Read(folder + mcf + ".mcf")), out var general);
                string slt = UnIni.Lower(BeforeLastDot(general.TryGetValue("sltfile", out var sf) ? sf : ""));
                if (slt.Length == 0 || !files.Has(folder + slt + ".dxg")) { r.report.Add(new JObj().Set("name", name).Set("state", "missing model " + slt)); continue; }
                var parts = PartsOf(slt); var nodes = parts.nodes;
                if (parts.named.Count == 0) { r.report.Add(new JObj().Set("name", name).Set("state", "empty model " + slt)); continue; }
                // MatrixToy::vf31: Mat_FromForwardUp(Position, LookVector, UpVector) on the model's root (as the .scn models are placed)
                var pos = Vec(Get("position"), 0, 0, 0); pos[0] -= c.CenterX; pos[2] -= c.CenterZ;
                var frame = UnNum.Frame(Vec(Get("lookvector"), 0, 0, 1), Vec(Get("upvector"), 0, 1, 0));
                var place = new double[16];
                for (int i = 0; i < 3; i++) { place[i] = frame[i]; place[4 + i] = frame[3 + i]; place[8 + i] = frame[6 + i]; place[12 + i] = pos[i]; }
                place[15] = 1;
                var local = new double[nodes.Count][];
                for (int k = 0; k < nodes.Count; k++) local[k] = Matrix16(nodes[k].matrix);
                string initial = Get("initialmotion");
                string wanted = UnIni.Lower(UnNum.StripChars(UnIni.Strip(string.IsNullOrEmpty(initial) ? "none" : initial), '"'));
                UnMotion motion = null; bool looping = false;
                foreach (var m in motions)
                {
                    if (UnIni.Lower(m.name) == wanted && wanted != "none")
                    {
                        string file = folder + UnIni.Lower(BeforeLastDot(m.vue)) + ".mot";
                        if (files.Has(file)) { motion = UnMot.ParseMot(files.Read(file), m.name); looping = m.loop != 0; }
                        break;
                    }
                }
                double t0 = 0.0;
                {
                    string it = Get("initialtime") ?? "0";
                    if (it.Length > 0 && UnNum.TryFloat(it, out double v)) t0 = v;
                }
                var bone = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int k = 0; k < nodes.Count; k++) bone[nodes[k].name.ToUpperInvariant()] = k;
                int NodeOf(int b)
                {
                    if (b < motion.bones.Count)
                    {
                        if (b < -motion.bones.Count) throw new UnValueError("Unleashed motion: bone index");   // IndexError
                        string bn = motion.bones[b < 0 ? b + motion.bones.Count : b].TrimEnd('\0').ToUpperInvariant();
                        return bone.TryGetValue(bn, out int k) ? k : -1;
                    }
                    return b < nodes.Count ? b : -1;
                }

                double[][] Pose(float t, UnMotRotState[] states)
                {
                    var frames = new double[nodes.Count][];
                    for (int k = 0; k < nodes.Count; k++) frames[k] = (double[])local[k].Clone();
                    if (motion != null)
                    {
                        for (int ch = 0; ch < motion.rot.Count; ch++)
                        {
                            int node = NodeOf(motion.rot[ch].Key);
                            if (node < 0) continue;
                            var q = UnMot.RotEval(motion.rot[ch].Value, motion.rotType, t, states[ch]);
                            if (q != null) QuatMatrix(q, frames[node]);
                        }
                        for (int ch = 0; ch < motion.pos.Count; ch++)
                        {
                            int node = NodeOf(motion.pos[ch].Key);
                            if (node < 0) continue;
                            // the keys are singles: the frame's translation goes through the game's single precision record and back
                            var p = new[] { frames[node][12], frames[node][13], frames[node][14], 1.0 };
                            PosEvalInto(motion.pos[ch].Value, motion.posType, t, p);
                            frames[node][12] = p[0]; frames[node][13] = p[1]; frames[node][14] = p[2];
                        }
                    }
                    var world = new double[nodes.Count][];
                    double[] Resolve(int k, int depth)
                    {
                        if (world[k] == null)
                        {
                            int parent = nodes[k].parent;
                            world[k] = UnNum.Mul4(frames[k], parent >= 0 && parent < nodes.Count && parent != k && depth < 64 ? Resolve(parent, depth + 1) : place);
                        }
                        return world[k];
                    }
                    for (int k = 0; k < nodes.Count; k++) Resolve(k, 0);
                    return world;
                }

                double duration = motion != null && motion.duration > 0 ? (double)motion.duration : 0.0;
                int steps = duration > 0 ? Math.Max(1, (int)Math.Ceiling(duration * Rate - 1e-4)) : 0;
                var rotStates = new UnMotRotState[motion != null ? motion.rot.Count : 0];
                for (int i = 0; i < rotStates.Length; i++) rotStates[i] = new UnMotRotState();
                var order = new List<int>(parts.named.Keys);
                int np = order.Count; var flat = new List<float[]>();   // samples x (parts x 7)
                var qd = new double[4];
                for (int k = 0; k <= steps; k++)
                {
                    var world = Pose(steps != 0 ? UnMot.F32(duration * k / steps) : 0.0f, rotStates);
                    var row = new float[np * 7];
                    for (int pi = 0; pi < np; pi++)
                    {
                        var m = world[order[pi]];
                        for (int i = 0; i < 16; i++) if (!UnNum.Finite(m[i])) throw new UnValueError("Invalid animated prop pose: " + name);
                        MatrixQuat(m, qd);
                        row[pi * 7] = (float)m[12]; row[pi * 7 + 1] = (float)m[13]; row[pi * 7 + 2] = (float)m[14];
                        for (int i = 0; i < 4; i++) row[pi * 7 + 3 + i] = (float)qd[i];
                    }
                    flat.Add(row);
                }
                // keep neighbouring quaternions on one side (the client blends them linearly)
                for (int k = 1; k < flat.Count; k++)
                    for (int pi = 0; pi < np; pi++)
                    {
                        float[] a = flat[k], b = flat[k - 1]; int q = pi * 7 + 3;
                        if (Dot4(a[q], b[q], a[q + 1], b[q + 1], a[q + 2], b[q + 2], a[q + 3], b[q + 3]) < 0)
                            for (int i = 0; i < 4; i++) a[q + i] = -a[q + i];
                    }
                bool moving = false;
                if (flat.Count > 1)
                {
                    float max = 0; bool nan = false;
                    for (int k = 0; k < flat.Count; k++)
                        for (int i = 0; i < np * 7; i++)
                        {
                            float d = Math.Abs((float)((double)flat[k][i] - flat[0][i]));
                            if (float.IsNaN(d)) nan = true; else if (d > max) max = d;
                        }
                    moving = !nan && (double)max > 1e-4;
                }
                int samples = moving ? flat.Count : 1;
                var head = new byte[17];
                head[0] = (byte)(looping ? 1 : 0); UnNum.PutF32(head, 1, moving ? duration : 0.0); UnNum.PutF32(head, 5, t0); UnNum.PutI32(head, 9, samples); UnNum.PutI32(head, 13, np);
                using (var s = new MemoryStream())
                {
                    void W(byte[] b) => s.Write(b, 0, b.Length);
                    W(Str(name)); W(Str(slt)); W(head);
                    foreach (int node in order) W(Str(parts.named[node]));
                    var body = new byte[samples * np * 28];
                    for (int k = 0; k < samples; k++) for (int i = 0; i < np * 7; i++) UnNum.PutF32Raw(body, (k * np * 7 + i) * 4, flat[k][i]);
                    W(body);
                    o.Add(s.ToArray());
                }
                r.report.Add(new JObj().Set("name", name).Set("model", slt).Set("motion", motion != null ? wanted : "none").Set("looping", looping).Set("duration", UnJson.Round(duration, 3))
                    .Set("parts", (long)np).Set("samples", (long)samples)
                    .Set("state", moving ? "animated" : motion != null ? "placed (its motion moves no drawn part)" : "placed (no motion)").Set("collision", Truth(Get("hascollision"))));
            }
            if (o.Count == 0) return r;
            using (var s = new MemoryStream())
            {
                var h = new byte[12]; h[0] = (byte)'R'; h[1] = (byte)'X'; h[2] = (byte)'T'; h[3] = (byte)'Y'; UnNum.PutI32(h, 4, 1); UnNum.PutI32(h, 8, o.Count);
                s.Write(h, 0, 12);
                foreach (var b in o) s.Write(b, 0, b.Length);
                r.data = s.ToArray();
            }
            return r;
        }

        /// mot.pos_eval on a frame's translation: the values the motion writes are singles, what it leaves stays as it is.
        static void PosEvalInto(float[][] keys, int ptype, float t, double[] p)
        {
            var o = new float[4];
            bool z = UnMot.PosEval(keys, ptype, t, o);
            p[0] = o[0]; p[1] = o[1];
            if (z) p[2] = o[2];
        }

        /// np.einsum('ij,ij->i') on single precision rows: the sign of the quaternions' dot product.
        static float Dot4(float a0, float b0, float a1, float b1, float a2, float b2, float a3, float b3)
        {
            float s = (float)((double)a0 * b0 + 0.0);
            s = (float)((double)a1 * b1 + s); s = (float)((double)a2 * b2 + s); s = (float)((double)a3 * b3 + s);
            return s;
        }
    }
}
