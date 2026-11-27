// A custom track's pictures for the menus, made by the game (PORT ADDITION: the original shows a custom track with no picture at all).
//
// The track browser shows two pictures of a track (OpenFrontEnd/OpenContent.cs Picture / MapPicture): the stock tracks' location picture
// (the cards, the preview) and their track map (the preview's and the setup page's corner). A custom track (Content/TrackMods.cs) ships
// neither, at best a preview picture of its author's. So:
//   the map      the track's race line, drawn like the game's own track maps (a gold course on a clear ground, the finish marked):
//                a level's `tdf` asset (the AI's line: sections of nodes, each a centre and two edge points; core/plan/ALIVE_RACING.md 1),
//                a terrain package's course (ExperimentalTerrain.Metadata.course). No race line: no map.
//   the picture  the track's own preview picture when it ships one (TrackMods.Thumbnail: unchanged); else a top-down picture of the
//                track: the terrain's own top-down colour map (the HFTerrain material's first texture, the far-distance ground) shaded by
//                its height map, framed on the race line with the line drawn over it; without a race line, framed on the level's scenery.
//                A track with a race line whose terrain does not read gets the line on a plain ground; one with neither keeps no picture.
// Made once, on one low-priority worker (never on the main thread: only the textures are made there, from decoded pixels), most recently
// asked first, and kept in <client data>/ReflexCache/track-pictures/<key>.v<Version>.rxtp. The key is a SHA-256 over the track's
// database, level and scene files and its package's size and time (a terrain package: its size, time and first bytes), so an updated
// track is drawn again and nothing is ever written into the track's own files or the game's. REFLEX_TRACK_PICTURES=0 turns it off,
// REFLEX_TRACK_PICTURES_DIR moves the cache (tests).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Reflex.Port.Content
{
    public static class TrackPictures
    {
        /// The pictures' look: a change here draws every track again.
        public const int Version = 1;
        public const int CardWidth = 768, CardHeight = 432, MapSize = 256;

        sealed class Made
        {
            public string key, source = "none", note, error;
            public byte[] card, map;   // RGBA, Unity row order (bottom first): CardWidth x CardHeight, MapSize x MapSize; null: none
            public bool cached; public long ms;
        }
        sealed class Entry { public Task<Made> task; public Made made; public Texture2D card, map; public bool built; }

        static readonly Dictionary<TrackMod, Entry> s_entries = new Dictionary<TrackMod, Entry>();
        static readonly Dictionary<string, Entry> s_byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static readonly bool s_off = Environment.GetEnvironmentVariable("REFLEX_TRACK_PICTURES") == "0";

        /// The generated picture of a custom track for the cards and the preview (null: none, or not made yet: pending).
        public static Texture2D Card(TrackMod m, out bool pending) { var e = Get(m); pending = e != null && !e.built; return e?.card; }
        /// The generated track map of a custom track (null: no race line, or not made yet: pending).
        public static Texture2D Map(TrackMod m, out bool pending) { var e = Get(m); pending = e != null && !e.built; return e?.map; }
        /// What the track's pictures were made from once they are ("line + top-down", "line", "top-down", "none"), else null.
        public static string Source(TrackMod m) => m != null && s_entries.TryGetValue(m, out var e) && e.built ? e.made.source : null;
        /// Whether those pictures came from the cache (false: drawn this run, or not made yet).
        public static bool FromCache(TrackMod m) => m != null && s_entries.TryGetValue(m, out var e) && e.built && e.made.cached;
        /// A picture asked for is still being read or drawn.
        public static bool Busy => s_entries.Values.Any(e => !e.built && !e.task.IsCompleted);

        static Entry Get(TrackMod m)
        {
            if (s_off || m == null || !m.Ok) return null;
            if (!s_entries.TryGetValue(m, out var e))
            {
                string cache = CacheFolder();
                Dictionary<string, string> files;
                lock (m.files) files = new Dictionary<string, string>(m.files, StringComparer.Ordinal);
                var done = new TaskCompletionSource<Made>();
                s_entries[m] = e = new Entry { task = done.Task };
                Queue(() =>
                {
                    Made made;
                    try { made = Make(m, files, cache); }
                    catch (Exception ex) { made = new Made { error = ex.GetType().Name + ": " + ex.Message }; }
                    done.SetResult(made);
                });
            }
            if (!e.built && e.task.IsCompleted)
            {
                var made = e.made = e.task.Result;
                // (a rescan makes new TrackMod objects: the same content keeps the textures it has)
                if (made.key != null && s_byKey.TryGetValue(made.key, out var same) && same.built) { e.card = same.card; e.map = same.map; }
                else
                {
                    e.card = Texture(made.card, CardWidth, CardHeight, "ou_trackpic " + m.id);
                    e.map = Texture(made.map, MapSize, MapSize, "ou_trackmap " + m.id);
                    if (made.key != null) s_byKey[made.key] = e;
                }
                e.built = true;
                Debug.Log($"[TrackPictures] {m.id}: {made.source}" + (made.note != null ? " (" + made.note + ")" : "") + (made.error != null ? "; not drawn: " + made.error : "")
                          + (made.cached ? ", from the cache" : $", drawn in {made.ms} ms"));
            }
            return e;
        }

        static Texture2D Texture(byte[] rgba, int w, int h, string name)
        {
            if (rgba == null || rgba.Length != w * h * 4) return null;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            t.SetPixelData(rgba, 0); t.Apply(true, false);
            return t;
        }

        static string s_cache; static bool s_cacheAsked;
        /// The cache's folder (main thread: the client data folder is Unity's), null when it cannot be made (the pictures are then drawn every run).
        static string CacheFolder()
        {
            if (s_cacheAsked) return s_cache;
            s_cacheAsked = true;
            try
            {
                string d = Environment.GetEnvironmentVariable("REFLEX_TRACK_PICTURES_DIR");
                if (string.IsNullOrEmpty(d)) d = Path.Combine(ReflexInstall.CacheDir, "track-pictures");
                Directory.CreateDirectory(d);
                s_cache = d;
            }
            catch (Exception e) { Debug.Log("[TrackPictures] no cache folder (" + e.Message + "): the pictures are drawn each run"); }
            return s_cache;
        }

        // ------------------------------------------------------------------------------------------------ the worker

        static readonly List<Action> s_jobs = new List<Action>();
        static Thread s_thread;
        static void Queue(Action job)
        {
            lock (s_jobs)
            {
                s_jobs.Add(job);
                if (s_thread == null)
                {
                    s_thread = new Thread(Work) { IsBackground = true, Name = "track pictures", Priority = System.Threading.ThreadPriority.BelowNormal };
                    s_thread.Start();
                }
                Monitor.Pulse(s_jobs);
            }
        }
        static void Work()
        {
            for (;;)
            {
                Action job;
                lock (s_jobs)
                {
                    while (s_jobs.Count == 0) Monitor.Wait(s_jobs);
                    job = s_jobs[s_jobs.Count - 1]; s_jobs.RemoveAt(s_jobs.Count - 1);   // (the card asked for last is the one in view)
                }
                try { job(); } catch (Exception) { }
            }
        }

        static Made Make(TrackMod m, Dictionary<string, string> files, string cache)
        {
            var watch = Stopwatch.StartNew();
            string key = Key(m, files);
            string file = cache == null ? null : Path.Combine(cache, key + ".v" + Version + ".rxtp");
            if (file != null && File.Exists(file))
                try { var kept = Load(file); kept.key = key; kept.cached = true; return kept; }
                catch (Exception) { }   // (a damaged file: drawn again)
            var made = m.experimentalTerrain ? MakePackage(m) : MakeLevel(m, files);
            made.key = key; made.ms = watch.ElapsedMilliseconds;
            if (file != null)
                try { Save(file, made); }
                catch (Exception e) { made.note = (made.note != null ? made.note + "; " : "") + "not kept: " + e.Message; }
            return made;
        }

        static string Key(TrackMod m, Dictionary<string, string> files)
        {
            using var sha = SHA256.Create();
            void Feed(byte[] b) => sha.TransformBlock(b, 0, b.Length, null, 0);
            void Text(string s) => Feed(Encoding.UTF8.GetBytes(s + "\n"));
            Text("orx-track-picture");
            if (m.experimentalTerrain)
            {
                Text(new FileInfo(m.folder).Length + " " + File.GetLastWriteTimeUtc(m.folder).Ticks);
                using var f = new FileStream(m.folder, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var head = new byte[Math.Min(f.Length, 1 << 16)]; int got = 0;
                while (got < head.Length) { int n = f.Read(head, got, head.Length - got); if (n <= 0) break; got += n; }
                Feed(head);
            }
            else
            {
                foreach (string ext in new[] { ".dx9.database", ".dx9.level", ".dx9.scene" })
                {
                    Text(ext);
                    if (files.TryGetValue((m.level + ext).ToLowerInvariant(), out var p)) Feed(RxFiles.ReadAllBytes(p));
                }
                if (files.TryGetValue((m.level + ".dx9.package").ToLowerInvariant(), out var pkg)) Text(RxFiles.Length(pkg) + " " + RxFiles.Stamp(pkg));
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var sb = new StringBuilder();
            for (int i = 0; i < 16; i++) sb.Append(sha.Hash[i].ToString("x2"));
            return sb.ToString();
        }

        const uint Magic = 0x50545852;   // "RXTP"
        static void Save(string file, Made made)
        {
            string tmp = file + ".tmp" + Thread.CurrentThread.ManagedThreadId;
            using (var w = new BinaryWriter(new GZipStream(new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None), System.IO.Compression.CompressionLevel.Fastest)))
            {
                w.Write(Magic); w.Write(Version); w.Write(made.source ?? "none"); w.Write(made.note ?? "");
                w.Write(made.card != null ? CardWidth : 0); w.Write(made.card != null ? CardHeight : 0); if (made.card != null) w.Write(made.card);
                w.Write(made.map != null ? MapSize : 0); if (made.map != null) w.Write(made.map);
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }
        static Made Load(string file)
        {
            using var r = new BinaryReader(new GZipStream(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read), CompressionMode.Decompress));
            if (r.ReadUInt32() != Magic || r.ReadInt32() != Version) throw new InvalidDataException("track picture header");
            var made = new Made { source = r.ReadString() };
            string note = r.ReadString(); made.note = note.Length > 0 ? note : null;
            int cw = r.ReadInt32(), ch = r.ReadInt32();
            if (cw != 0 || ch != 0)
            {
                if (cw != CardWidth || ch != CardHeight) throw new InvalidDataException("track picture size");
                made.card = Bytes(r, cw * ch * 4);
            }
            int mn = r.ReadInt32();
            if (mn != 0) { if (mn != MapSize) throw new InvalidDataException("track map size"); made.map = Bytes(r, mn * mn * 4); }
            return made;
        }
        static byte[] Bytes(BinaryReader r, int n)
        {
            var b = new byte[n]; int got = 0;
            while (got < n) { int k = r.Read(b, got, n - got); if (k <= 0) throw new EndOfStreamException(); got += k; }
            return b;
        }

        // ------------------------------------------------------------------------------------------------ what a track holds

        /// A race line on the ground plane: segments (x, z to x, z, with the course's half width there) and the finish (a bar across it).
        sealed class Line
        {
            public readonly List<float[]> segments = new List<float[]>();   // x0, z0, x1, z1, half width
            public float[] finish;                                         // x0, z0, x1, z1 or null
            public string name; public int nodes;
            public float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            public void Add(float x0, float z0, float x1, float z1, float half)
            {
                segments.Add(new[] { x0, z0, x1, z1, half });
                minX = Math.Min(minX, Math.Min(x0, x1) - half); maxX = Math.Max(maxX, Math.Max(x0, x1) + half);
                minZ = Math.Min(minZ, Math.Min(z0, z1) - half); maxZ = Math.Max(maxZ, Math.Max(z0, z1) + half);
            }
            public bool Usable => segments.Count >= 8 && Math.Max(maxX - minX, maxZ - minZ) > 30f;
            public float HalfWidth { get { var w = segments.Select(s => s[4]).OrderBy(v => v).ToList(); return w.Count > 0 ? w[w.Count / 2] : 0f; } }
        }

        /// A terrain seen from above: the height grid (row 0 = +z; world height = value x 2048) and the top-down colour map (RGBA, row 0 = +z).
        sealed class Ground
        {
            public float[] heights; public int size = 2048; public float spacing = 1f;
            public byte[] colour; public int cw, ch;
            public float Half => (size - 1) * 0.5f * spacing;
        }

        static Made MakeLevel(TrackMod m, Dictionary<string, string> files)
        {
            var made = new Made();
            var notes = new List<string>();
            var install = new Rfx.Install(m.packed ? Path.Combine(m.folder, "Database") : m.folder, m.Namespace, files);
            try
            {
                var db = install.Db(m.Namespace + m.level);
                if (db == null) throw new InvalidDataException("the track's database does not read");
                // ---- the race line: the level's tdf (the race's own first: <level>-nat / -sx; else the one with the most nodes)
                Line line = null;
                string want = m.kind == "Supercross" ? "-sx" : m.kind == "National" ? "-nat" : null;
                foreach (var a in db.packages.Values.SelectMany(p => p.OfType("tdf")))
                {
                    Line l = null;
                    try { l = ReadTdf(install.Read(a)); } catch (Exception) { }
                    if (l == null || !l.Usable) continue;
                    l.name = a.name;
                    bool own = want != null && a.name != null && a.name.EndsWith(want, StringComparison.OrdinalIgnoreCase);
                    bool has = line != null && want != null && line.name.EndsWith(want, StringComparison.OrdinalIgnoreCase);
                    if (line == null || own && !has || own == has && l.nodes > line.nodes) line = l;
                }
                if (line != null) notes.Add($"tdf {line.name}, {line.nodes} nodes");
                // ---- the ground and the scenery's extent
                Ground ground = null; List<float[]> things = null;
                try { ground = ReadGround(install, db, files, m, line == null, out things); }
                catch (Exception e) { notes.Add("terrain: " + e.Message); }
                Finish(made, line, ground, things);
            }
            finally { install.Close(); }
            if (notes.Count > 0) made.note = string.Join("; ", notes);
            return made;
        }

        /// A terrain package (RXPK: the MTX tracks): its course, heights and colour map.
        static Made MakePackage(TrackMod m)
        {
            var made = new Made();
            var notes = new List<string>();
            Line line = null; Ground ground = null;
            try
            {
                var c = ExperimentalTerrain.Info(m.folder).course;
                if (c != null && c.Length >= 40 && c.Length % 5 == 0 && c.Length <= 20000)
                {
                    line = new Line { name = "course", nodes = c.Length / 5 };
                    for (int j = 0; j < c.Length; j += 5)
                    {
                        int k = (j + 5) % c.Length;
                        line.Add(c[j], c[j + 2], c[k], c[k + 2], Mathf.Clamp(Math.Max(c[j + 3], c[k + 3]) * 0.5f, 2f, 60f));
                    }
                    float dx = c[5] - c[0], dz = c[7] - c[2], len = Mathf.Sqrt(dx * dx + dz * dz), half = Mathf.Clamp(c[3] * 0.5f, 2f, 60f) * 1.7f;
                    if (len > 1e-3f) line.finish = new[] { c[0] - dz / len * half, c[2] + dx / len * half, c[0] + dz / len * half, c[2] - dx / len * half };
                    if (!line.Usable) line = null; else notes.Add($"course, {line.nodes} points");
                }
            }
            catch (Exception e) { notes.Add("course: " + e.Message); }
            try
            {
                ground = new Ground { heights = ExperimentalTerrain.Read(m.folder), size = 2048, spacing = ExperimentalTerrain.Spacing(m.folder) };
                try { var rgb = ExperimentalTerrain.Colour(m.folder); if (rgb != null && rgb.Length == 2048 * 2048 * 4) { ground.colour = rgb; ground.cw = ground.ch = 2048; } }
                catch (Exception e) { notes.Add("colour: " + e.Message); }
            }
            catch (Exception e) { ground = null; notes.Add("terrain: " + e.Message); }
            Finish(made, line, ground, null);
            if (notes.Count > 0) made.note = string.Join("; ", notes);
            return made;
        }

        static void Finish(Made made, Line line, Ground ground, List<float[]> things)
        {
            if (line == null && ground == null) return;
            made.source = line != null && ground != null ? "line + top-down" : line != null ? "line" : "top-down";
            made.card = DrawCard(line, ground, things);
            if (line != null) made.map = DrawMap(line);
        }

        // ---- the race line of a `tdf` asset (PC: a little-endian size word, then the big-endian stream the consoles have: six header
        // words, a path, then items {id, size, payload}: 11 opens a section, 20 a node; a node's 21 / 22 / 23 are its centre and edge
        // points; a section's 14 names a next section; the global 9 is the finish line with its (section, node)).
        sealed class Section { public readonly List<float[]> nodes = new List<float[]>(); public readonly List<int> next = new List<int>(); }
        static uint BE(byte[] d, long p) => (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]);
        static float BF(byte[] d, long p) => BitConverter.Int32BitsToSingle(unchecked((int)BE(d, p)));
        static bool Good(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) < 1e6f;

        static Line ReadTdf(byte[] d)
        {
            if (d == null || d.Length < 0x24) return null;
            long b;
            if (BitConverter.ToUInt32(d, 0) == (uint)(d.Length - 4) && BE(d, 0x18) == 260) b = 4;
            else if (BE(d, 0x14) == 260) b = 0;
            else return null;
            long p = b + 24 + BE(d, b + 20);
            var sections = new List<Section>();
            Section cur = null; float[] node = null;
            int finishSection = -1, finishNode = -1;
            while (p + 8 <= d.Length)
            {
                uint id = BE(d, p), size = BE(d, p + 4);
                long n = size == 0x10 ? 12 : size, at = p + 8;
                if (at + n > d.Length) break;
                if (id == 11) { cur = new Section(); sections.Add(cur); node = null; }
                else if (id == 20) { if (cur == null) return null; node = new float[7]; node[6] = 0f; cur.nodes.Add(node); }
                else if (node != null && n == 12 && (id == 21 || id == 22 || id == 23))
                {
                    int o = id == 21 ? 0 : id == 22 ? 2 : 4;
                    node[o] = BF(d, at); node[o + 1] = BF(d, at + 8);
                    node[6] = (int)node[6] | (id == 21 ? 1 : id == 22 ? 2 : 4);
                }
                else if (node == null && cur != null && id == 14 && n == 8) cur.next.Add((int)BE(d, at + 4));
                else if (cur == null && id == 9 && n == 48 && d[at + 36] != 0) { finishSection = (int)BE(d, at + 40); finishNode = (int)BE(d, at + 44); }
                p = at + n;
            }
            // the sections the race runs over: every one reached from the finish's by the next links (all of them when the finish is not named)
            var use = new bool[sections.Count];
            if (finishSection >= 0 && finishSection < sections.Count)
            {
                var todo = new Stack<int>(); todo.Push(finishSection); use[finishSection] = true;
                while (todo.Count > 0)
                    foreach (int nx in sections[todo.Pop()].next)
                        if (nx >= 0 && nx < sections.Count && !use[nx]) { use[nx] = true; todo.Push(nx); }
            }
            else for (int i = 0; i < use.Length; i++) use[i] = true;
            var line = new Line();
            float Half(float[] a) => ((int)a[6] & 6) == 6 ? Mathf.Clamp(0.5f * Mathf.Sqrt((a[2] - a[4]) * (a[2] - a[4]) + (a[3] - a[5]) * (a[3] - a[5])), 2f, 60f) : 6f;
            bool Ok(float[] a) => ((int)a[6] & 1) != 0 && Good(a[0]) && Good(a[1]);
            for (int s = 0; s < sections.Count; s++)
            {
                if (!use[s]) continue;
                var ns = sections[s].nodes.Where(Ok).ToList();
                line.nodes += ns.Count;
                for (int i = 0; i + 1 < ns.Count; i++) line.Add(ns[i][0], ns[i][1], ns[i + 1][0], ns[i + 1][1], Math.Max(Half(ns[i]), Half(ns[i + 1])));
                if (ns.Count == 0) continue;
                var last = ns[ns.Count - 1];
                foreach (int nx in sections[s].next)
                {
                    if (nx < 0 || nx >= sections.Count) continue;
                    var first = sections[nx].nodes.FirstOrDefault(Ok);
                    if (first == null) continue;
                    float gx = first[0] - last[0], gz = first[1] - last[1];
                    if (gx * gx + gz * gz < 400f * 400f) line.Add(last[0], last[1], first[0], first[1], Math.Max(Half(last), Half(first)));   // (never a chord across the map)
                }
            }
            if (finishSection >= 0 && finishSection < sections.Count)
            {
                var ns = sections[finishSection].nodes;
                if (finishNode >= 0 && finishNode < ns.Count && Ok(ns[finishNode]))
                {
                    var a = ns[finishNode];
                    float[] to = finishNode + 1 < ns.Count ? ns[finishNode + 1] : finishNode > 0 ? ns[finishNode - 1] : null;
                    if (to != null && Ok(to))
                    {
                        float dx = to[0] - a[0], dz = to[1] - a[1], len = Mathf.Sqrt(dx * dx + dz * dz), half = Half(a) * 1.7f;
                        if (len > 1e-3f) line.finish = new[] { a[0] - dz / len * half, a[1] + dx / len * half, a[0] + dz / len * half, a[1] - dx / len * half };
                    }
                }
            }
            return line;
        }

        /// The level's terrain (its HFTerrain entity: the height map, the grid spacing, the material's top-down colour map) and, for a
        /// track without a race line, where its scenery stands (x, z of every placed thing).
        static Ground ReadGround(Rfx.Install install, Rfx.Database db, Dictionary<string, string> files, TrackMod m, bool wantThings, out List<float[]> things)
        {
            things = null;
            if (!files.TryGetValue((m.level + ".dx9.scene").ToLowerInvariant(), out var scenePath)) throw new InvalidDataException("no scene file");
            var data = RxFiles.ReadAllBytes(scenePath);
            var names = ReflexScene.SectionNames(data);
            string section = names.FirstOrDefault(n => n.EndsWith("Base", StringComparison.Ordinal)) ?? names[0];
            var entities = ReflexScene.Read(data, section);
            var te = entities.FirstOrDefault(e => e.Type == "HFTerrainRSData");
            if (te == null) throw new InvalidDataException("no terrain entity");
            if (wantThings)
            {
                things = new List<float[]>();
                foreach (var e in entities)
                {
                    string t = e.Type;
                    if (t == "HFTerrainRSData" || t.IndexOf("Light", StringComparison.Ordinal) >= 0 || t.StartsWith("Sky", StringComparison.Ordinal)
                        || t.IndexOf("Fog", StringComparison.Ordinal) >= 0 || t.StartsWith("HDR", StringComparison.Ordinal)) continue;
                    var mx = e.Floats("Matrix");
                    if (mx != null && mx.Length >= 16 && Good(mx[12]) && Good(mx[14]) && (mx[12] != 0f || mx[14] != 0f)) things.Add(new[] { mx[12], mx[14] });
                }
            }
            Rfx.Asset Find(string name, string type)
            {
                if (string.IsNullOrEmpty(name)) return null;
                foreach (var p in db.packages.Values) { var a = p.Find(name, type); if (a != null) return a; }
                return null;
            }
            var g = new Ground();
            var grid = te.Field("GridResolution")?.children.Find(c => c.tag == "EnumValue");
            g.spacing = 1 << Mathf.Clamp(grid?.value is int gv ? gv : 0, 0, 6);
            var ha = Find(te.Asset("HeightMapAsset"), "texture");
            if (ha != null)
                try
                {
                    var h = ReflexTex.ReadR32F(install.Read(ha), out int hw, out int hh);
                    if (hw == hh && hw >= 64) { g.heights = h; g.size = hw; }
                }
                catch (Exception) { }
            var ma = Find(te.Asset("MaterialAsset"), "material");
            if (ma != null)
                try
                {
                    string top = Rfx.ParseMaterial(ma.name, install.Read(ma)).textures.FirstOrDefault();
                    var ta = Find(top, "texture");
                    if (ta != null)
                    {
                        var raw = install.Read(ta);
                        int level = 0, w0 = Rfx.I32(raw, 44);
                        while ((w0 >> level) > 1024) level++;
                        var rgba = ReflexTex.DecodeExactRgba(raw, level, out int w, out int h2) ?? ReflexTex.DecodeExactRgba(raw, 0, out w, out h2);
                        if (rgba != null && w >= 16 && h2 >= 16) { g.colour = rgba; g.cw = w; g.ch = h2; }
                    }
                }
                catch (Exception) { }
            if (g.heights == null && g.colour == null) throw new InvalidDataException("neither the height map nor the top-down map reads");
            return g;
        }

        // ------------------------------------------------------------------------------------------------ drawing

        static readonly Color32 Paper = new Color32(22, 24, 28, 255), Off = new Color32(13, 14, 17, 255), Ink = new Color32(10, 11, 13, 255),
                                Course = new Color32(246, 244, 238, 255), FinishMark = new Color32(240, 120, 52, 255), Dirt = new Color32(124, 98, 70, 255),
                                MapCourse = new Color32(221, 166, 62, 255), MapFinish = new Color32(246, 244, 238, 255);   // (the stock track maps' gold)

        /// The card: 16 : 9, north (+z) up. The ground under the race line (or under the scenery, or the whole terrain), the line over it.
        static byte[] DrawCard(Line line, Ground g, List<float[]> things)
        {
            int W = CardWidth, H = CardHeight;
            // ---- the frame: centre and world units per pixel
            float minX, maxX, minZ, maxZ;
            if (line != null) { minX = line.minX; maxX = line.maxX; minZ = line.minZ; maxZ = line.maxZ; }
            else
            {
                float half = g.Half;
                minX = minZ = -half; maxX = maxZ = half;
                if (things != null && things.Count >= 6)
                {   // where the track's things stand (the outliers left out), never less than 300 units across
                    var xs = things.Select(t => t[0]).OrderBy(v => v).ToList(); var zs = things.Select(t => t[1]).OrderBy(v => v).ToList();
                    int lo = (int)(xs.Count * 0.03f), hi = Math.Min(xs.Count - 1, (int)(xs.Count * 0.97f));
                    float x0 = Math.Max(-half, xs[lo]), x1 = Math.Min(half, xs[hi]), z0 = Math.Max(-half, zs[lo]), z1 = Math.Min(half, zs[hi]);
                    if (x1 > x0 && z1 > z0) { minX = x0; maxX = x1; minZ = z0; maxZ = z1; }
                }
            }
            float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            float bw = Math.Max(maxX - minX, 300f), bh = Math.Max(maxZ - minZ, 300f * H / W);
            float margin = line != null ? 1.22f : things != null && things.Count >= 6 ? 1.3f : 1f;
            float s = Math.Max(bw / W, bh / H) * margin;
            if (g != null && line == null)
            {   // (a view never wider than the terrain, and kept inside it)
                float half = g.Half;
                s = Math.Min(s, 2f * half / W);
                float vw = s * W * 0.5f, vh = s * H * 0.5f;
                cx = vw >= half ? 0f : Mathf.Clamp(cx, -half + vw, half - vw);
                cz = vh >= half ? 0f : Mathf.Clamp(cz, -half + vh, half - vh);
            }
            var px = new byte[W * H * 4];
            // ---- the ground
            if (g == null) for (int i = 0; i < W * H; i++) Put(px, i, Paper);
            else
            {
                float half = g.Half;
                // the view's heights (one sample a pixel and a ring around it) for the shading
                float[] hs = null;
                if (g.heights != null)
                {
                    hs = new float[(W + 2) * (H + 2)];
                    for (int y = -1; y <= H; y++)
                        for (int x = -1; x <= W; x++)
                            hs[(y + 1) * (W + 2) + x + 1] = Height(g, cx + (x + 0.5f - W * 0.5f) * s, cz - (y + 0.5f - H * 0.5f) * s);
                }
                // the colour map's own brightness in view, brought to the menus' pictures' (a dark top-down map stays readable)
                float gain = 1f;
                if (g.colour != null)
                {
                    double sum = 0; int count = 0;
                    for (int y = 4; y < H; y += 8)
                        for (int x = 4; x < W; x += 8)
                        {
                            float wx = cx + (x + 0.5f - W * 0.5f) * s, wz = cz - (y + 0.5f - H * 0.5f) * s;
                            if (Math.Abs(wx) > half || Math.Abs(wz) > half) continue;
                            Colour(g, wx, wz, out float r, out float gg, out float b);
                            sum += 0.299 * r + 0.587 * gg + 0.114 * b; count++;
                        }
                    if (count > 0 && sum > 0) gain = Mathf.Clamp(104f / (float)(sum / count), 0.85f, 2.6f);
                }
                // light from the north-west, a flat ground at 1
                float lx = -0.55f, ly = 0.72f, lz = 0.42f; { float ll = Mathf.Sqrt(lx * lx + ly * ly + lz * lz); lx /= ll; ly /= ll; lz /= ll; }
                float relief = g.colour != null ? 0.85f : 1.25f;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        float wx = cx + (x + 0.5f - W * 0.5f) * s, wz = cz - (y + 0.5f - H * 0.5f) * s;
                        // past the terrain's edge (a view wider than a small terrain): the ground mirrored there, dimmed, so the picture has no bars
                        float outside = Math.Max(Math.Abs(wx), Math.Abs(wz)) - half, dim = 1f;
                        if (outside > 0f)
                        {
                            if (outside > 2f * half) { Put(px, i, Off); continue; }
                            dim = Mathf.Lerp(0.62f, 0.4f, Mathf.Clamp01(outside / (0.5f * half)));
                            if (wx > half) wx = 2f * half - wx; else if (wx < -half) wx = -2f * half - wx;
                            if (wz > half) wz = 2f * half - wz; else if (wz < -half) wz = -2f * half - wz;
                        }
                        float r = Dirt.r, gg = Dirt.g, b = Dirt.b;
                        if (g.colour != null) { Colour(g, wx, wz, out r, out gg, out b); r *= gain; gg *= gain; b *= gain; }
                        float lit = 1f;
                        if (hs != null)
                        {
                            int k = (y + 1) * (W + 2) + x + 1;
                            float dx = (hs[k + 1] - hs[k - 1]) / (2f * s), dz = (hs[k - (W + 2)] - hs[k + (W + 2)]) / (2f * s);   // (+z is up the picture)
                            float nx = -dx, nz = -dz, nl = Mathf.Sqrt(nx * nx + 1f + nz * nz);
                            lit = outside > 0f ? 1f : Mathf.Clamp(1f + relief * ((nx * lx + ly + nz * lz) / nl / ly - 1f), 0.42f, 1.5f);
                        }
                        lit *= dim;
                        px[i * 4] = Byte(r * lit); px[i * 4 + 1] = Byte(gg * lit); px[i * 4 + 2] = Byte(b * lit); px[i * 4 + 3] = 255;
                    }
            }
            // ---- the race line
            if (line != null) Stroke(px, W, H, line, cx, cz, s, 1.9f, 4.5f, 1.6f, g != null ? 0.92f : 1f, Course, FinishMark);
            return Flip(px, W, H);
        }

        /// The map: square, clear, the race line alone (the stock track maps: the course's shape, the finish marked).
        static byte[] DrawMap(Line line)
        {
            int N = MapSize;
            float cx = (line.minX + line.maxX) * 0.5f, cz = (line.minZ + line.maxZ) * 0.5f;
            float s = Math.Max(Math.Max(line.maxX - line.minX, line.maxZ - line.minZ), 60f) / (N * 0.84f);
            var px = new byte[N * N * 4];
            Stroke(px, N, N, line, cx, cz, s, 4f, 6f, 1.5f, 1f, MapCourse, MapFinish);
            return Flip(px, N, N);
        }

        /// The line over a picture: a dark edge, the course in white at its own width (never thinner than min, never wider than max
        /// pixels), the finish across it.
        static void Stroke(byte[] px, int W, int H, Line line, float cx, float cz, float s, float min, float max, float edge, float alpha, Color32 course, Color32 finish)
        {
            var inner = new float[W * H]; var outer = new float[W * H];
            float X(float x) => (x - cx) / s + W * 0.5f;
            float Y(float z) => H * 0.5f - (z - cz) / s;
            float half = line.HalfWidth;
            float r = Mathf.Clamp(half / s, min, max);
            foreach (var g in line.segments)
            {
                float rr = half > 0f ? Mathf.Clamp(r * Mathf.Clamp(g[4] / half, 0.75f, 1.35f), min, max) : r;
                Cover(inner, W, H, X(g[0]), Y(g[1]), X(g[2]), Y(g[3]), rr);
                Cover(outer, W, H, X(g[0]), Y(g[1]), X(g[2]), Y(g[3]), rr + edge);
            }
            for (int i = 0; i < W * H; i++)
            {
                if (outer[i] > 0f) Over(px, i, Ink, outer[i] * 0.86f * alpha);
                if (inner[i] > 0f) Over(px, i, course, inner[i] * alpha);
            }
            if (line.finish == null) return;
            Array.Clear(inner, 0, inner.Length); Array.Clear(outer, 0, outer.Length);
            var f = line.finish;
            // (the bar: across the course and a little past its edges, whatever the course's drawn width)
            float mx = (X(f[0]) + X(f[2])) * 0.5f, my = (Y(f[1]) + Y(f[3])) * 0.5f, ux = X(f[2]) - X(f[0]), uy = Y(f[3]) - Y(f[1]), ul = Mathf.Sqrt(ux * ux + uy * uy);
            if (ul < 1e-4f) return;
            float reach = Math.Max(ul * 0.5f, r + 3.5f); ux /= ul; uy /= ul;
            float bar = Math.Max(1.5f, r * 0.55f);
            Cover(inner, W, H, mx - ux * reach, my - uy * reach, mx + ux * reach, my + uy * reach, bar);
            Cover(outer, W, H, mx - ux * reach, my - uy * reach, mx + ux * reach, my + uy * reach, bar + edge);
            for (int i = 0; i < W * H; i++)
            {
                if (outer[i] > 0f) Over(px, i, Ink, outer[i] * 0.9f);
                if (inner[i] > 0f) Over(px, i, finish, inner[i]);
            }
        }

        /// A round-ended stroke's coverage (the larger of what is there and this one's), by the distance to its segment.
        static void Cover(float[] cov, int W, int H, float ax, float ay, float bx, float by, float r)
        {
            if (!Good(ax) || !Good(ay) || !Good(bx) || !Good(by)) return;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - r - 1f)), x1 = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(ax, bx) + r + 1f));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - r - 1f)), y1 = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(ay, by) + r + 1f));
            float dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float qx = x + 0.5f - ax, qy = y + 0.5f - ay;
                    float t = len2 > 0f ? Mathf.Clamp01((qx * dx + qy * dy) / len2) : 0f;
                    float ex = qx - t * dx, ey = qy - t * dy;
                    float c = Mathf.Clamp01(r - Mathf.Sqrt(ex * ex + ey * ey) + 0.5f);
                    int i = y * W + x;
                    if (c > cov[i]) cov[i] = c;
                }
        }

        static byte Byte(float v) => (byte)(v <= 0f ? 0 : v >= 255f ? 255 : (int)(v + 0.5f));
        static void Put(byte[] px, int i, Color32 c) { px[i * 4] = c.r; px[i * 4 + 1] = c.g; px[i * 4 + 2] = c.b; px[i * 4 + 3] = c.a; }
        /// c at alpha a over the pixel (straight alpha; the pixel may be clear).
        static void Over(byte[] px, int i, Color32 c, float a)
        {
            a = Mathf.Clamp01(a);
            float da = px[i * 4 + 3] / 255f, oa = a + da * (1f - a);
            if (oa <= 0f) return;
            px[i * 4] = Byte((c.r * a + px[i * 4] * da * (1f - a)) / oa);
            px[i * 4 + 1] = Byte((c.g * a + px[i * 4 + 1] * da * (1f - a)) / oa);
            px[i * 4 + 2] = Byte((c.b * a + px[i * 4 + 2] * da * (1f - a)) / oa);
            px[i * 4 + 3] = Byte(oa * 255f);
        }
        static byte[] Flip(byte[] px, int W, int H)
        {
            var o = new byte[px.Length]; int row = W * 4;
            for (int y = 0; y < H; y++) Buffer.BlockCopy(px, y * row, o, (H - 1 - y) * row, row);
            return o;
        }

        /// The ground's height (world units) at a point, between its four samples.
        static float Height(Ground g, float x, float z)
        {
            float half = g.Half, c = (x + half) / g.spacing, r = (half - z) / g.spacing;
            int n = g.size;
            c = Mathf.Clamp(c, 0f, n - 1.001f); r = Mathf.Clamp(r, 0f, n - 1.001f);
            int c0 = (int)c, r0 = (int)r; float fc = c - c0, fr = r - r0;
            var h = g.heights; int i = r0 * n + c0;
            return ((h[i] * (1f - fc) + h[i + 1] * fc) * (1f - fr) + (h[i + n] * (1f - fc) + h[i + n + 1] * fc) * fr) * 2048f;
        }

        /// The top-down map's colour at a point, between its four texels.
        static void Colour(Ground g, float x, float z, out float r, out float gg, out float b)
        {
            float half = g.Half, u = (x + half) / (2f * half) * g.cw - 0.5f, v = (half - z) / (2f * half) * g.ch - 0.5f;
            u = Mathf.Clamp(u, 0f, g.cw - 1.001f); v = Mathf.Clamp(v, 0f, g.ch - 1.001f);
            int u0 = (int)u, v0 = (int)v; float fu = u - u0, fv = v - v0;
            var c = g.colour; int i = (v0 * g.cw + u0) * 4, j = i + g.cw * 4;
            float w00 = (1f - fu) * (1f - fv), w10 = fu * (1f - fv), w01 = (1f - fu) * fv, w11 = fu * fv;
            r = c[i] * w00 + c[i + 4] * w10 + c[j] * w01 + c[j + 4] * w11;
            gg = c[i + 1] * w00 + c[i + 5] * w10 + c[j + 1] * w01 + c[j + 5] * w11;
            b = c[i + 2] * w00 + c[i + 6] * w10 + c[j + 2] * w01 + c[j + 6] * w11;
        }
    }
}
