// MX vs ATV Unleashed import, the scenery side of one level: what tools/unleashed/import_library.py does after the terrain, in its
// order -
//   scenery.convert   scenery.rxscenery, collision.rxcollision, vegetation.json, scenery-fx.json, scenery-manifest.json,
//                     toys.rxtoys (UnToys), funnel.rxfunnel (UnFunnel), reflex-forest.bin / reflex-resource-* / reflex-vegetation.json
//                     (UnReflexScenery) and the report of level.json's "scenery"
//   startgate.write   startgate.rxgate and level.json's "start_gate" (UnStartGate; needs the level's terrain.rxheight and race.rxrace)
//   stadium_lights    stadium-lights.json for the stadium levels (UnStadiumLights)
// The source is every .pak of the data folder, in name order, the first archive holding a path wins (scenery.py's file table).
// Nothing here uses Unity; the arithmetic is UnNum's (the reference's numpy operations, rounding for rounding).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public sealed class UnSceneryInput
    {
        public string sourceDataDir;      // folder holding <id>.pak, Env.pak ...
        public string reflexRoot;         // the player's Reflex library root (null: no Reflex vegetation, as reflex_archive=None)
        public string trackId;            // "Nat04"
        public double centerX, centerZ;   // terrain centre (import_library.py: center)
        public double maxExtent;          // float(max(extent))
        public string sceneText;          // the track's .scn decoded as latin-1 (the start gate reads it; null: read from the paks)
        public Func<double, double, double> sampleHeight;   // import_library.py sample(x, z): not used by the scenery side
        /// The level's terrain.rxheight exactly as written (startgate.py reads the gate's ground from it). null: no start gate.
        public byte[] terrainRxheight;
        /// The level's race.rxrace, or null when the level has no race (then there is no start gate).
        public byte[] raceRxrace;
    }

    public sealed class UnSceneryResult
    {
        /// file name inside Assets/unleashed/UNL_<id>/ -> bytes.
        public Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        /// scenery.convert's report (level.json "scenery"): a JObj, key order as Python's dict (UnJson.Dumps(report, 2) inside level.json).
        public object report;
        /// startgate.write's return (level.json "start_gate"): a JObj, or null (then the key is not written by a fresh import).
        public object startGate;
    }

    /// Every .pak of the source's data folder as one file table (scenery.py: files / read / resolve; stadium_lights.py: library).
    public sealed class UnSceneryPaks : IDisposable
    {
        readonly List<UnPak> _paks = new List<UnPak>();
        readonly Dictionary<string, KeyValuePair<UnPak, UnPak.Entry>> _files = new Dictionary<string, KeyValuePair<UnPak, UnPak.Entry>>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _byBase = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string Key(string path) => UnIni.Lower(path).Replace('\\', '/');

        /// PurePosixPath(p).name.
        public static string BaseName(string posix)
        {
            int end = posix.Length;
            for (; ; )
            {
                while (end > 0 && posix[end - 1] == '/') end--;
                if (end == 0) return "";
                int start = posix.LastIndexOf('/', end - 1) + 1;
                if (end - start == 1 && posix[start] == '.' && start > 0) { end = start; continue; }   // a '.' component is dropped
                return posix.Substring(start, end - start);
            }
        }

        public UnSceneryPaks(string dataDir)
        {
            var names = new List<string>();
            foreach (string f in Directory.GetFiles(dataDir))
                if (f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal)) names.Add(f);   // glob('*.pak')
            names.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));   // sorted(glob('*.pak'))
            try
            {
                foreach (string f in names)
                {
                    var pak = UnPak.Open(f); _paks.Add(pak);
                    foreach (var e in pak.Entries)
                    {
                        string k = Key(e.Name);
                        if (_files.ContainsKey(k)) continue;
                        _files[k] = new KeyValuePair<UnPak, UnPak.Entry>(pak, e);
                        string b = BaseName(k);
                        if (!_byBase.ContainsKey(b)) _byBase[b] = k;
                    }
                }
            }
            catch { Dispose(); throw; }
        }

        public bool Has(string path) => _files.ContainsKey(Key(path));

        /// read(path): KeyNotFoundException when no archive has it.
        public byte[] Read(string path)
        {
            if (!_files.TryGetValue(Key(path), out var f)) throw new KeyNotFoundException(path);
            return f.Key.Read(f.Value);
        }

        /// resolve(name, extension): the first file whose name is the name's last component (its own extension dropped) + extension;
        /// null where Python raises FileNotFoundError.
        public string Resolve(string name, string extension)
        {
            string b = UnIni.Lower(BaseName(name.Replace('\\', '/')));
            int dot = b.LastIndexOf('.');
            if (dot >= 0) b = b.Substring(0, dot);
            return _byBase.TryGetValue(b + extension, out var p) ? p : null;
        }

        /// stadium_lights.library: the first file with this (lower-case) name in any folder; null when there is none.
        public string ByName(string lowerName) => _byBase.TryGetValue(lowerName, out var p) ? p : null;

        public void Dispose() { foreach (var p in _paks) p.Dispose(); _paks.Clear(); }
    }

    /// One decoded surface of a model at its place.
    public sealed class UnSceneryPart
    {
        public int material, count;
        public double[] vertices;    // count x 3
        public double[] uv0, uv1;    // count x 2
        public int[] indices;
        public byte[] colors;        // count x 4, as stored (0x80 = 1.0)
    }

    public static class UnScenery
    {
        /// The scenery side of a fresh import. progress: (what, 0..1). Throws what the Python raises as an Exception with its message.
        public static UnSceneryResult Convert(UnSceneryInput input, Action<string, float> progress, CancellationToken ct)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var result = new UnSceneryResult();
            using (var files = new UnSceneryPaks(input.sourceDataDir))
            {
                var c = new UnSceneryConverter(input, files, progress, ct);
                c.Run(result);
                ct.ThrowIfCancellationRequested();
                progress?.Invoke("Start gate", 0.92f);
                string scene = input.sceneText ?? UnNum.Latin1(files.Read("Data/Env/" + input.trackId + "/" + input.trackId + ".scn"));
                var gate = UnStartGate.Write(scene, input.centerX, input.centerZ, input.trackId, input.terrainRxheight, input.raceRxrace, out byte[] gateFile);
                if (gateFile != null) result.files["startgate.rxgate"] = gateFile;
                result.startGate = gate;
                ct.ThrowIfCancellationRequested();
                if (UnStadiumLights.Applies(input.trackId))
                {
                    progress?.Invoke("Stadium lights", 0.95f);
                    result.files["stadium-lights.json"] = UnStadiumLights.Write(files, input.trackId, input.centerX, input.centerZ, ct);
                }
            }
            progress?.Invoke("Scenery", 1f);
            return result;
        }
    }

    /// scenery.convert(source, center, track, extent, vegetation_out, reflex_archive).
    public sealed class UnSceneryConverter
    {
        readonly UnSceneryInput _in; readonly UnSceneryPaks _files; readonly Action<string, float> _progress; readonly CancellationToken _ct;
        readonly string _track; readonly double _cx, _cz;

        readonly List<byte[]> _textures = new List<byte[]>(), _materials = new List<byte[]>(), _draws = new List<byte[]>();
        readonly Dictionary<string, int> _known = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<string> _missing = new List<string>(), _missingCollision = new List<string>(), _missingTextures = new List<string>();
        readonly List<object> _fx = new List<object>();
        readonly JObj _notes = new JObj();
        sealed class Tex { public int id; public bool alpha; public List<int> frames; }
        readonly Dictionary<string, Tex> _decoded = new Dictionary<string, Tex>(StringComparer.Ordinal);
        readonly Dictionary<string, UnIni> _recipeOf = new Dictionary<string, UnIni>(StringComparer.Ordinal);
        readonly Dictionary<string, UnDxg> _models = new Dictionary<string, UnDxg>(StringComparer.Ordinal);
        readonly Dictionary<string, UnCol> _colliders = new Dictionary<string, UnCol>(StringComparer.Ordinal);

        public UnSceneryConverter(UnSceneryInput input, UnSceneryPaks files, Action<string, float> progress, CancellationToken ct)
        {
            _in = input; _files = files; _progress = progress; _ct = ct; _track = input.trackId; _cx = input.centerX; _cz = input.centerZ;
        }

        public UnSceneryPaks Files => _files;
        public double CenterX => _cx;
        public double CenterZ => _cz;

        void Note(string text) => _notes.Set(text, (_notes.Get(text) is long n ? n : 0L) + 1);

        // ------------------------------------------------------------------------------------------------ textures and materials

        int AddTexture(int w, int h, byte[] rgba)
        {
            var b = new byte[12 + rgba.Length];
            UnNum.PutI32(b, 0, _textures.Count + 1); UnNum.PutI32(b, 4, w); UnNum.PutI32(b, 8, h);
            Buffer.BlockCopy(rgba, 0, b, 12, rgba.Length);
            _textures.Add(b); return _textures.Count;
        }

        static UnDxtImage DecodeDxt(byte[] b, string what) => UnDxt.Decode(b) ?? throw new UnValueError("Cannot decode " + what);

        static uint U32(byte[] b, long o)
        {
            if (o < 0 || o + 4 > b.Length) throw new UnValueError("Unleashed animated texture: truncated");   // struct.error
            return BitConverter.ToUInt32(b, (int)o);
        }

        /// .atx: u32 1, u32 frames, u32, then whole .dxt files.
        static List<byte[]> AtxFrames(byte[] b, string what)
        {
            if (b.Length < 12 || U32(b, 0) != 1) throw new UnValueError("Invalid animated texture " + what);
            uint count = U32(b, 4); long o = 12; var frames = new List<byte[]>();
            for (uint i = 0; i < count; i++)
            {
                if (o + 24 > b.Length) break;
                long q = o + 12 + U32(b, o + 8) * 4L; uint mips = U32(b, q); q += 12;
                for (uint m = 0; m < mips; m++) q += 12 + U32(b, q + 8);
                if (q > b.Length) break;
                var f = new byte[q - o]; Buffer.BlockCopy(b, (int)o, f, 0, f.Length); frames.Add(f); o = q;
            }
            if (frames.Count == 0) throw new UnValueError("Empty animated texture " + what);
            return frames;
        }

        static bool AnyAlpha(byte[] rgba)
        {
            for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] < 255) return true;
            return false;
        }

        /// texture(name, fallback, animated): (id, has alpha, frame ids) of a pass's texture, by the game's order of file types.
        Tex Texture(string name, string fallback = null, bool animated = false)
        {
            string low = UnIni.Lower(name), key = low + (animated ? "\n1" : "\n0");
            if (_decoded.TryGetValue(key, out var done)) return done;
            Tex result;
            if (name.StartsWith("&", StringComparison.Ordinal) || low == "framebuffer" || low == "displaybuffer")
            {
                // A render target (the stadium screens). The mesh texture's own image stands in when the source has one.
                if (!string.IsNullOrEmpty(fallback) && UnIni.Lower(fallback) != low && _files.Resolve(fallback, ".dxt") != null)
                {
                    var t = Texture(fallback); _decoded[key] = t;
                    Note("render target shown as its still image: " + fallback);
                    return t;
                }
                Note("render target (a live screen in the original): dark panel");
                result = new Tex { id = AddTexture(1, 1, new byte[] { 32, 32, 32, 255 }), alpha = false, frames = null };
            }
            else
            {
                var ids = new List<int>(); byte[] raw; List<int> frames = null;
                string path = _files.Resolve(name, ".dxt");
                if (path != null)
                {
                    var img = DecodeDxt(_files.Read(path), name); ids.Add(AddTexture(img.width, img.height, img.rgba)); raw = img.rgba;
                }
                else
                {
                    string atx = _files.Resolve(name, ".atx");
                    if (atx == null)
                    {
                        _missingTextures.Add(name);
                        var none = new Tex { id = AddTexture(1, 1, new byte[] { 255, 255, 255, 255 }), alpha = false, frames = null };
                        _decoded[key] = none; return none;
                    }
                    var blobs = AtxFrames(_files.Read(atx), name);
                    raw = null;
                    int take = animated ? blobs.Count : 1;
                    for (int i = 0; i < take; i++)
                    {
                        var img = DecodeDxt(blobs[i], name);
                        if (i == 0) raw = img.rgba;
                        ids.Add(AddTexture(img.width, img.height, img.rgba));
                    }
                    frames = ids.Count > 1 ? ids : null;
                }
                result = new Tex { id = ids[0], alpha = AnyAlpha(raw), frames = frames };
            }
            _decoded[key] = result; return result;
        }

        /// number(value, default) where the default is a float: float(str(value).split(',')[0]) or the default.
        public static double Number(string value, double fallback = 0.0)
        {
            if (value == null) return fallback;
            int comma = value.IndexOf(',');
            return UnNum.TryFloat(comma < 0 ? value : value.Substring(0, comma), out double v) ? v : fallback;
        }

        /// number(value, default) with an integer default: the parsed float, or the default as the integer Python keeps it (JSON
        /// writes 1, not 1.0).
        static object NumberOr(string value, long fallback)
        {
            if (value == null) return fallback;
            int comma = value.IndexOf(',');
            return UnNum.TryFloat(comma < 0 ? value : value.Substring(0, comma), out double v) ? (object)v : fallback;
        }

        /// int(number(value, default)).
        static int IntNumber(string value, int fallback) => UnNum.Int(Number(value, fallback));

        /// truth(value, default).
        public static bool Truth(string value, bool fallback)
        {
            string s = value != null ? UnIni.Lower(UnIni.Strip(value)) : "";
            char c = s.Length > 0 ? s[0] : '\0';
            return c == 't' || c == '1' || c == 'y' ? true : c == 'f' || c == '0' || c == 'n' ? false : fallback;
        }

        sealed class Pass
        {
            public int texture; public bool alpha; public List<int> frames; public JObj frame, uv;
            public int blend; public bool additive; public int cutoff; public bool clampU, clampV, uv1;
        }

        static string Quoted(string s) => UnIni.Lower(UnNum.StripChars(UnIni.Strip(s), '"'));

        /// One [ShaderPass_N] as the game's material loader reads it (shader null: the mesh template's pass).
        Pass PassState(UnIniSection shader, string name, int first)
        {
            string Get(string k) => shader?.Get(k);
            string Or(string v, string d) => string.IsNullOrEmpty(v) ? d : v;
            string bound = Or(Get("texturemap_1"), name);
            string vs = UnIni.Lower(Or(Get("vertexshadername"), "DXVPrelit.xvu"));
            JObj frame = null, uv = null;
            int animators = IntNumber(Get("numanimators"), 0);
            var recipe = _recipeOf[name];
            for (int k = 1; k <= animators; k++)
            {
                var a = recipe.HasSection("ShaderPass_" + first + "_Animator_" + k) ? recipe.Section("ShaderPass_" + first + "_Animator_" + k) : null;
                if (a == null) continue;
                string alg = Quoted(a.Get("algorithm") ?? "");
                if (alg == "textureframe" && a.Get("name") == null)   // named ones are driven by the race (holeshot / leader lights)
                    frame = new JObj().Set("fps", NumberOr(a.Get("animatedmapframespersecond"), 1)).Set("initial", NumberOr(a.Get("animatedmapinitialframe"), 0))
                        .Set("looping", IntNumber(a.Get("animatedmaplooping"), 1) != 0).Set("reverse", IntNumber(a.Get("animatedmapreverselooponend"), 0) != 0)
                        .Set("loopTo", (long)IntNumber(a.Get("animatedmaplooponendtoframe"), -1));
                else if (alg == "uvanimation" && vs.Contains("textransform") && Quoted(Or(Get("vertexanimation"), "none")) != "none")
                {
                    object OrOne(object v) => v is double d && d == 0.0 ? 1L : v;   // number(...) or 1
                    uv = new JObj().Set("angle", Number(a.Get("angle"))).Set("rate", Number(a.Get("uvangularrate"))).Set("uCenter", Number(a.Get("ucenter"))).Set("vCenter", Number(a.Get("vcenter")))
                        .Set("uScale", NumberOr(a.Get("uscale"), 1)).Set("vScale", NumberOr(a.Get("vscale"), 1)).Set("uShear", Number(a.Get("ushear"))).Set("vShear", Number(a.Get("vshear")))
                        .Set("uMax", OrOne(NumberOr(a.Get("umax"), 1))).Set("vMax", OrOne(NumberOr(a.Get("vmax"), 1))).Set("uVel", Number(a.Get("uvelocity"))).Set("vVel", Number(a.Get("vvelocity")));
                }
            }
            var tex = Texture(bound, name, frame != null);
            bool blendOn = Truth(Get("alphablendenable"), false);
            bool additive = blendOn && UnIni.Lower(Get("destblend") ?? "") == "d3dblend_one";
            // the alpha test: GREATER ref unless the pass says otherwise (the template's is "> 0": only clear texels are dropped)
            bool test = Truth(Get("alphatestenable"), true); string func = UnIni.Lower(Or(Get("alphafunc"), "d3dcmp_greater")); int rf = IntNumber(Get("alpharef"), 0);
            int cut = !test || func.EndsWith("always", StringComparison.Ordinal) ? 0 : func.EndsWith("never", StringComparison.Ordinal) ? 255
                : func.EndsWith("greaterequal", StringComparison.Ordinal) ? Math.Max(rf, 1) : Math.Min(rf + 1, 255);
            string tci = Get("texcoordindex_0") ?? ""; int bar = tci.IndexOf('|');
            return new Pass
            {
                texture = tex.id, alpha = tex.alpha, frames = frame != null ? tex.frames : null, frame = frame, uv = uv,
                blend = additive ? 1 : blendOn ? 5 : 0, additive = additive, cutoff = tex.alpha ? cut : 0,
                clampU = UnIni.Lower(Get("addressu_0") ?? "").Contains("clamp"), clampV = UnIni.Lower(Get("addressv_0") ?? "").Contains("clamp"),
                uv1 = (bar < 0 ? tci : tci.Substring(0, bar)).Contains("1"),
            };
        }

        static byte B(int v) { if (v < 0 || v > 255) throw new UnValueError("ubyte format requires 0 <= number <= 255"); return (byte)v; }

        static List<object> Longs(List<int> ids) { var l = new List<object>(ids.Count); foreach (int i in ids) l.Add((long)i); return l; }

        static JObj Fx(int material, int stage, Pass p, bool frames)
        {
            var o = new JObj().Set("material", (long)material).Set("stage", (long)stage);
            if (frames) { o.Set("frames", Longs(p.frames)); foreach (var kv in p.frame.items) o.Set(kv.Key, kv.Value); }
            else o.Set("uv", p.uv);
            return o;
        }

        /// material(name): the scenery material of a mesh texture name - pass 1 as the base, the later passes as layers over it.
        public int Material(string name)
        {
            if (_known.TryGetValue(name, out int have)) return have;
            string dxm = _files.Resolve(name, ".dxm");
            var recipe = UnIni.Parse(dxm != null ? UnNum.Latin1(_files.Read(dxm)) : "", true);
            _recipeOf[name] = recipe;
            var states = new List<Pass>();
            int total = Math.Max(1, IntNumber(recipe.HasSection("General") && recipe.Section("General").Has("TotalShaderPasses") ? recipe.Section("General").Get("TotalShaderPasses") : "1", 1));
            for (int n = 1; n <= total; n++)
            {
                string sec = "ShaderPass_" + n;
                if (!recipe.HasSection(sec))
                {
                    if (n == 1) states.Add(PassState(null, name, n));
                    continue;
                }
                var shader = recipe.Section(sec);
                if (IntNumber(shader.Get("enablethisshader"), 1) == 0) continue;
                // the LOD passes: drawn from MaterialPassStart on; the pass at distance 0 is the one a rider sees
                if (states.Count > 0 && Number(shader.Get("materialpassstart"), 0) > 0) { Note("distance (LOD) material pass left out"); continue; }
                states.Add(PassState(shader, name, n));
            }
            if (states.Count == 0) states.Add(PassState(null, name, 1));
            var bas = states[0]; var layers = new MemoryStream();
            int index = _materials.Count;
            if (bas.frames != null && bas.frames.Count > 0) _fx.Add(Fx(index, 0, bas, true));
            if (bas.uv != null) _fx.Add(Fx(index, 0, bas, false));
            int count = 0;
            for (int i = 1; i < states.Count && i < 4; i++)
            {
                var p = states[i];
                if (p.blend == 0) { Note("opaque second material pass left out"); continue; }
                count++;
                // set 1 holds the mesh's second UVs, set 2 the first (a layer cannot address set 0)   LAYER = '<IBB??2fB??3f'
                var l = new byte[31];
                UnNum.PutI32(l, 0, p.texture); l[4] = B(p.blend); l[5] = 128; l[6] = 0; l[7] = 0;
                UnNum.PutF32(l, 8, 1); UnNum.PutF32(l, 12, 1); l[16] = (byte)(p.uv1 ? 1 : 2); l[17] = (byte)(p.clampU ? 1 : 0); l[18] = (byte)(p.clampV ? 1 : 0);
                UnNum.PutF32(l, 19, 2); UnNum.PutF32(l, 23, 2); UnNum.PutF32(l, 27, 2);
                layers.Write(l, 0, l.Length);
                if (p.frames != null && p.frames.Count > 0) _fx.Add(Fx(index, count, p, true));
                if (p.uv != null) _fx.Add(Fx(index, count, p, false));
            }
            // MATERIAL = '<IBB??B???3f?2fB'
            var m = new byte[34 + layers.Length];
            UnNum.PutI32(m, 0, bas.texture); m[4] = B(bas.blend); m[5] = B(bas.cutoff); m[6] = (byte)(bas.alpha ? 1 : 0); m[7] = (byte)(bas.additive ? 1 : 0); m[8] = 128;
            m[9] = 0; m[10] = (byte)(bas.clampU ? 1 : 0); m[11] = (byte)(bas.clampV ? 1 : 0);
            UnNum.PutF32(m, 12, 2); UnNum.PutF32(m, 16, 2); UnNum.PutF32(m, 20, 2); m[24] = 0; UnNum.PutF32(m, 25, 1); UnNum.PutF32(m, 29, 1); m[33] = B(count);
            Buffer.BlockCopy(layers.ToArray(), 0, m, 34, (int)layers.Length);
            _materials.Add(m);
            _known[name] = index; return index;
        }

        // ------------------------------------------------------------------------------------------------ models

        /// model(name): the parsed .dxg; FileNotFoundException where Python raises FileNotFoundError.
        public UnDxg Model(string name)
        {
            if (name == null) throw new UnValueError("Unleashed scene: a model without a name");
            if (_models.TryGetValue(name, out var m)) return m;
            string path = _files.Resolve(name, ".dxg") ?? throw new FileNotFoundException(name + ".dxg");
            return _models[name] = UnDxg.Parse(_files.Read(path));
        }

        /// collider(name): the parsed .col, or null where Python raises FileNotFoundError.
        public UnCol Collider(string name)
        {
            if (name == null) throw new UnValueError("Unleashed scene: a collision object without a name");
            if (_colliders.TryGetValue(name, out var c)) return c;
            string path = _files.Resolve(name, ".col");
            if (path == null) return null;
            return _colliders[name] = UnCol.Parse(_files.Read(path));
        }

        static double[] Matrix16(float[] m) { var d = new double[16]; for (int i = 0; i < 16; i++) d[i] = m[i]; return d; }

        /// The D3DCOLOR vertex colours as stored (0x80 = 1.0, brighter above: not clamped); no colour = 1.0.
        public static byte[] Colors(UnDxgSurface surf, int count)
        {
            var colors = new byte[count * 4];
            for (int i = 0; i < count; i++) { colors[i * 4] = colors[i * 4 + 1] = colors[i * 4 + 2] = 128; colors[i * 4 + 3] = 255; }
            if (surf.Has(2) && surf.attrs[2].Key == 0x40)
            {
                byte[] p = surf.attrs[2].Value;
                if (p.Length != count * 4) throw new UnValueError("Unleashed model: vertex colour count");   // numpy: cannot broadcast
                for (int i = 0; i < count; i++) { colors[i * 4] = p[i * 4 + 2]; colors[i * 4 + 1] = p[i * 4 + 1]; colors[i * 4 + 2] = p[i * 4]; colors[i * 4 + 3] = p[i * 4 + 3]; }
            }
            return colors;
        }

        /// The surface's positions (count x 3) and its two UV sets as Python decodes them.
        public static void Streams(UnDxgSurface surf, out double[] vertices, out int count, out double[] uv, out double[] uv1)
        {
            vertices = UnDxg.Decode(surf.attrs.TryGetValue(0, out var a0) ? a0.Key : throw new UnValueError("Unleashed model: a surface without positions"), a0.Value, surf.PosScale, out int n);
            if (n != 3) throw new UnValueError("Unleashed model: positions of " + n + " components");
            count = vertices.Length / 3;
            if (surf.attrs.TryGetValue(3, out var a3))
            {
                uv = UnDxg.Decode(a3.Key, a3.Value, surf.UvScale, out int un);
                if (un != 2 || uv.Length != count * 2) throw new UnValueError("Unleashed model: texture coordinates do not match the positions");
            }
            else uv = new double[count * 2];
            uv1 = uv;
            if (surf.attrs.TryGetValue(10, out var a10))
            {
                var second = UnDxg.Decode(a10.Key, a10.Value, surf.UvScale, out int un);
                if (un == 2 && second.Length == uv.Length) uv1 = second;   // another shape: the first set stands in
            }
        }

        /// decoded_model(name, pos, rotation): the surfaces of the model's first LOD group at its place.
        public List<UnSceneryPart> DecodedModel(string name, double[] pos, double[] rotation)
        {
            var mesh = Model(name); var result = new List<UnSceneryPart>();
            if (mesh.groups.Count == 0) throw new UnValueError("Unleashed model without geometry: " + name);
            foreach (var surf in mesh.groups[0].surfaces)
            {
                Streams(surf, out double[] vertices, out int count, out double[] uv, out double[] uv1);
                if (surf.Has(4))
                {
                    var bones = UnDxg.Decode(surf.attrs[4].Key, surf.attrs[4].Value, 1.0, out int bn);
                    if (bones.Length != count) throw new UnValueError("Unleashed model: bone indices do not match the positions");
                    var palettes = mesh.palettes; int pi = surf.palette ?? 0;
                    byte[] palette = palettes != null && palettes.Count > 0 ? palettes[pi < palettes.Count ? pi : 0] : null;
                    var matrices = new List<double[]>();
                    foreach (var node in mesh.nodes)
                    {
                        var matrix = Matrix16(node.matrix);
                        if (node.parent >= 0)
                        {
                            if (node.parent >= matrices.Count) throw new UnValueError("Unleashed model: a node before its parent in " + name);   // IndexError
                            matrix = UnNum.Mul4(matrix, matrices[node.parent]);
                        }
                        matrices.Add(matrix);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        int slot = UnNum.Int(bones[i]);
                        int nodeIndex = palette != null ? (slot >= 0 && slot < palette.Length ? palette[slot] : -1) : (slot >= 0 && slot < mesh.nodes.Count ? slot : -1);
                        if (nodeIndex < 0 || nodeIndex >= matrices.Count) continue;   // source renderer leaves unbound vertices in model space
                        var m = matrices[nodeIndex];
                        double x = vertices[i * 3], y = vertices[i * 3 + 1], z = vertices[i * 3 + 2];
                        vertices[i * 3] = UnNum.Chain4(x, m[0], y, m[4], z, m[8], 1.0, m[12]);
                        vertices[i * 3 + 1] = UnNum.Chain4(x, m[1], y, m[5], z, m[9], 1.0, m[13]);
                        vertices[i * 3 + 2] = UnNum.Chain4(x, m[2], y, m[6], z, m[10], 1.0, m[14]);
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    UnNum.RowMat3(vertices[i * 3], vertices[i * 3 + 1], vertices[i * 3 + 2], rotation, out double x, out double y, out double z);
                    x += pos[0]; y += pos[1]; z += pos[2];
                    if (!UnNum.Finite(x) || !UnNum.Finite(y) || !UnNum.Finite(z)) throw new UnValueError("Invalid scenery geometry: " + name);
                    vertices[i * 3] = x; vertices[i * 3 + 1] = y; vertices[i * 3 + 2] = z;
                }
                var indices = UnDxg.StripTris(surf.indices, surf.nverts);
                if (indices.Length == 0) continue;
                if (surf.material >= mesh.textures.Count) throw new UnValueError("Unleashed model: a surface's material is not in " + name);   // IndexError
                int mi = Material(mesh.textures[(int)surf.material]);
                result.Add(new UnSceneryPart { material = mi, count = count, vertices = vertices, uv0 = uv, uv1 = uv1, indices = indices, colors = Colors(surf, count) });
            }
            return result;
        }

        static byte[] DrawHeader(int mi, bool sky, bool billboard, int group, int vertices, int indices, int body)
        {
            // '<i??iii'
            var b = new byte[18 + body];
            UnNum.PutI32(b, 0, mi); b[4] = (byte)(sky ? 1 : 0); b[5] = (byte)(billboard ? 1 : 0); UnNum.PutI32(b, 6, group); UnNum.PutI32(b, 10, vertices); UnNum.PutI32(b, 14, indices);
            return b;
        }

        /// emit_draw(mi, vertices, uv, indices, colors, group, sky, into): one 48 byte vertex each ('<11f4B').
        public byte[] Draw(UnSceneryPart p, double[] vertices, int group = -1, bool sky = false)
        {
            int n = p.count;
            foreach (int ix in p.indices) if (ix < 0) throw new UnValueError("Unleashed model: a negative index");
            var b = DrawHeader(p.material, sky, false, group, n, p.indices.Length, n * 48 + p.indices.Length * 4);
            int o = 18;
            for (int i = 0; i < n; i++, o += 48)
            {
                UnNum.PutF32(b, o, vertices[i * 3]); UnNum.PutF32(b, o + 4, vertices[i * 3 + 1]); UnNum.PutF32(b, o + 8, vertices[i * 3 + 2]);
                float u0 = UnNum.F32(p.uv0[i * 2]), v0 = UnNum.F32(p.uv0[i * 2 + 1]);
                UnNum.PutF32Raw(b, o + 12, u0); UnNum.PutF32Raw(b, o + 16, v0);
                UnNum.PutF32(b, o + 20, p.uv1[i * 2]); UnNum.PutF32(b, o + 24, p.uv1[i * 2 + 1]);
                UnNum.PutF32Raw(b, o + 28, u0); UnNum.PutF32Raw(b, o + 32, v0); UnNum.PutF32Raw(b, o + 36, u0); UnNum.PutF32Raw(b, o + 40, v0);
                b[o + 44] = p.colors[i * 4]; b[o + 45] = p.colors[i * 4 + 1]; b[o + 46] = p.colors[i * 4 + 2]; b[o + 47] = p.colors[i * 4 + 3];
            }
            foreach (int ix in p.indices) { UnNum.PutI32(b, o, ix); o += 4; }
            return b;
        }

        public void EmitDraw(UnSceneryPart p, int group = -1) => _draws.Add(Draw(p, p.vertices, group));

        static double[] Vec3(string text, string what)
        {
            var v = UnNum.Floats(text ?? throw new UnValueError("Unleashed scene: empty " + what));
            if (v.Length != 3) throw new UnValueError("Unleashed scene: " + what + " of " + v.Length + " components");
            return v;
        }

        /// Position (terrain-centred) and the rotation rows right / up / forward of a .scn object.
        void Placement(UnIniSection obj, out double[] pos, out double[] rotation)
        {
            pos = Vec3(obj.Get("position", "0,0,0"), "Position"); pos[0] -= _cx; pos[2] -= _cz;
            rotation = UnNum.Frame(Vec3(obj.Get("lookvector", "0,0,1"), "LookVector"), Vec3(obj.Get("upvector", "0,1,0"), "UpVector"));
        }

        static readonly double[] Zero3 = { 0, 0, 0 };
        static readonly double[] Eye3 = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        static byte[] DotnetString(string value)
        {
            var b = Encoding.UTF8.GetBytes(value); int n = b.Length; var prefix = new List<byte>();
            while (n >= 128) { prefix.Add((byte)((n & 127) | 128)); n >>= 7; }
            prefix.Add((byte)n);
            var r = new byte[prefix.Count + b.Length]; prefix.CopyTo(r); Buffer.BlockCopy(b, 0, r, prefix.Count, b.Length); return r;
        }

        static byte[] Group(string name, bool hidden)
        {
            var s = DotnetString(name); var r = new byte[s.Length + 2]; Buffer.BlockCopy(s, 0, r, 0, s.Length); r[s.Length] = (byte)(hidden ? 1 : 0); return r;
        }

        static byte[] Collection(List<byte[]> items)
        {
            long n = 4; foreach (var i in items) n += i.Length;
            var b = new byte[n]; UnNum.PutI32(b, 0, items.Count); int o = 4;
            foreach (var i in items) { Buffer.BlockCopy(i, 0, b, o, i.Length); o += i.Length; }
            return b;
        }

        static string StemOfWindowsPath(string path)
        {
            string p = path;
            if (p.Length >= 2 && p[1] == ':' && (p[0] >= 'A' && p[0] <= 'Z' || p[0] >= 'a' && p[0] <= 'z')) p = p.Substring(2);
            string name = UnSceneryPaks.BaseName(p.Replace('\\', '/'));
            int i = name.LastIndexOf('.');
            return i > 0 && i < name.Length - 1 ? name.Substring(0, i) : name;
        }

        // ------------------------------------------------------------------------------------------------ the conversion

        public void Run(UnSceneryResult result)
        {
            string track = _track; bool reflex = _in.reflexRoot != null;
            _progress?.Invoke("Scenery", 0f);
            var scn = UnIni.Parse(UnNum.Latin1(_files.Read("Data/Env/" + track + "/" + track + ".scn")), true);
            bool races = UnStartGate.Races(track);
            var rules = UnSceneRules.HiddenModels(scn, races);
            int models = 0; var special = new List<byte[]>(); var manifest = new List<object>();
            var funnelCfg = races ? UnFunnel.Sections(track, _files) : null;   // (the start's bale sets: race levels)
            var funnelParts = new[] { new List<UnSceneryPart>(), new List<UnSceneryPart>() };
            var funnelTris = new[] { new List<double[]>(), new List<double[]>() };
            int sectionCount = scn.Sections.Count, at = 0;
            foreach (var obj in scn.Sections)
            {
                _ct.ThrowIfCancellationRequested();
                if ((at++ & 63) == 0) _progress?.Invoke("Scenery", 0.6f * at / Math.Max(1, sectionCount));
                string sec = obj.Name;
                if (!UnIni.Lower(sec).StartsWith("model", StringComparison.Ordinal) || !obj.Has("slt")) continue;
                if (rules.hidden.Contains(sec)) continue;
                string name = obj["slt"]; Model(name);
                Placement(obj, out double[] pos, out double[] rotation);
                string kind = UnIni.Lower(UnIni.Strip(obj.Get("type") ?? ""));
                int fset = UnFunnel.Which(funnelCfg, name);
                if (fset >= 0)
                {
                    funnelParts[fset].AddRange(DecodedModel(name, pos, rotation)); funnelTris[fset].Add(UnFunnel.Triangles(this, name, pos, rotation));
                    manifest.Add(new JObj().Set("section", sec).Set("slt", name).Set("mode", "funnel " + UnFunnel.Names[fset])); models++; continue;
                }
                if (kind == "camerafollowing" && (Truth(obj.Get("followcamera"), false) || Truth(obj.Get("facecamera"), false)))
                {
                    // CameraFollowingModel: it sits at the camera's eye (FollowCamera) or turns to it where it stands (FaceCamera)
                    if (Truth(obj.Get("followcamera"), false))
                    {
                        if (Number(obj.Get("maxtiltangle"), 0) > 0)
                        {
                            manifest.Add(new JObj().Set("section", sec).Set("slt", name).Set("mode", "dropped").Set("reason", "the camera-following rain: the weather draws rain")); continue;
                        }
                        var sky = DecodedModel(name, Zero3, rotation);
                        foreach (var part in sky) special.Add(Draw(part, part.vertices, -1, true));
                        manifest.Add(new JObj().Set("section", sec).Set("slt", name).Set("mode", "sky").Set("draws", (long)sky.Count)); models++; continue;
                    }
                    var boards = DecodedModel(name, Zero3, Eye3);
                    foreach (var p in boards)
                    {
                        // '<11f4B5f': the vertex, then the anchor and the corner offset
                        var b = DrawHeader(p.material, false, true, -1, p.count, p.indices.Length, p.count * 68 + p.indices.Length * 4);
                        int o = 18;
                        for (int i = 0; i < p.count; i++, o += 68)
                        {
                            double vx = p.vertices[i * 3], vy = p.vertices[i * 3 + 1], vz = p.vertices[i * 3 + 2];
                            UnNum.PutF32(b, o, pos[0] + vx); UnNum.PutF32(b, o + 4, pos[1] + vy); UnNum.PutF32(b, o + 8, pos[2] + vz);
                            UnNum.PutF32(b, o + 12, p.uv0[i * 2]); UnNum.PutF32(b, o + 16, p.uv0[i * 2 + 1]);
                            UnNum.PutF32(b, o + 20, p.uv1[i * 2]); UnNum.PutF32(b, o + 24, p.uv1[i * 2 + 1]);
                            UnNum.PutF32(b, o + 28, p.uv0[i * 2]); UnNum.PutF32(b, o + 32, p.uv0[i * 2 + 1]); UnNum.PutF32(b, o + 36, p.uv0[i * 2]); UnNum.PutF32(b, o + 40, p.uv0[i * 2 + 1]);
                            b[o + 44] = p.colors[i * 4]; b[o + 45] = p.colors[i * 4 + 1]; b[o + 46] = p.colors[i * 4 + 2]; b[o + 47] = p.colors[i * 4 + 3];
                            UnNum.PutF32(b, o + 48, pos[0]); UnNum.PutF32(b, o + 52, pos[1]); UnNum.PutF32(b, o + 56, pos[2]); UnNum.PutF32(b, o + 60, vx); UnNum.PutF32(b, o + 64, vy);
                        }
                        foreach (int ix in p.indices) { UnNum.PutI32(b, o, ix); o += 4; }
                        special.Add(b);
                    }
                    manifest.Add(new JObj().Set("section", sec).Set("slt", name).Set("mode", "billboard").Set("draws", (long)boards.Count)); models++; continue;
                }
                var parts = DecodedModel(name, pos, rotation);
                foreach (var part in parts) EmitDraw(part);
                manifest.Add(new JObj().Set("section", sec).Set("slt", name).Set("mode", "placed").Set("draws", (long)parts.Count));
                models++;
            }
            int placedDraws = _draws.Count; _draws.AddRange(special);
            _progress?.Invoke("Vegetation", 0.62f);

            string ecoName = scn.Get("Environment", "EcosystemFile", "");
            if (ecoName == null) throw new UnValueError("Unleashed scene: empty EcosystemFile");
            string ecoPath = "Data/Env/" + track + "/" + StemOfWindowsPath(ecoName) + ".esb";
            var ecosystem = ecoName.Length > 0 && _files.Has(ecoPath) ? UnEsb.Parse(_files.Read(ecoPath)) : new UnEsb();
            var treeGroups = new List<object>(); var treeInstances = new List<JObj>(); var instanceSpecies = new List<int>(); long treeTriangles = 0;
            for (int si = 0; si < ecosystem.species.Count; si++)
            {
                _ct.ThrowIfCancellationRequested();
                var species = ecosystem.species[si];
                var parts = DecodedModel(species.mesh, Zero3, Eye3);
                if (parts.Count == 0) throw new UnValueError("Empty ecosystem model " + species.mesh);
                double lox = double.PositiveInfinity, loy = lox, loz = lox, hix = double.NegativeInfinity, hiy = hix, hiz = hix;
                foreach (var p in parts)
                    for (int i = 0; i < p.count; i++)
                    {
                        double x = p.vertices[i * 3], y = p.vertices[i * 3 + 1], z = p.vertices[i * 3 + 2];
                        if (x < lox) lox = x; if (x > hix) hix = x; if (y < loy) loy = y; if (y > hiy) hiy = y; if (z < loz) loz = z; if (z > hiz) hiz = z;
                    }
                double height = hiy - loy, radius = Math.Max(Math.Max(Math.Abs(lox), Math.Abs(hix)), Math.Max(Math.Abs(loz), Math.Abs(hiz)));
                if (height <= 0 || radius <= 0) throw new UnValueError("Invalid ecosystem model bounds " + species.mesh);
                treeGroups.Add("UnleashedTree:" + si.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (var p in parts)
                {
                    var v = new double[p.count * 3];
                    for (int i = 0; i < p.count; i++)
                    {
                        v[i * 3] = p.vertices[i * 3] / radius; v[i * 3 + 1] = (p.vertices[i * 3 + 1] - loy) / height; v[i * 3 + 2] = p.vertices[i * 3 + 2] / radius;
                    }
                    _draws.Add(Draw(p, v, si)); treeTriangles += p.indices.Length / 3;
                }
            }
            // Match the source loader: instances marked snap take the current terrain height.
            var ground = new UnEsbGround(_files.Read("Data/Env/" + track + "/" + track + ".dx.trn"));

            var replaced = new HashSet<int>();
            UnReflexScenery.Plan selection = null;
            if (reflex)
            {
                selection = UnReflexScenery.PlanOf(track);
                if (selection != null)
                    for (int i = 0; i < ecosystem.species.Count; i++) if (selection.species.ContainsKey(UnIni.Lower(ecosystem.species[i].mesh))) replaced.Add(i);
            }
            long vegetation = 0;
            var plantOrder = new List<int>(); var plantRaw = new Dictionary<int, MemoryStream>(); var plantIndices = new Dictionary<int, List<int>>();
            var corner = new byte[68];
            foreach (var inst in ecosystem.instances)
            {
                ecosystem.World(inst, _in.maxExtent, out double x, out double y, out double z, out double height, out double radius);
                int si = inst.species; var species = ecosystem.SpeciesAt(si);
                if (inst.snap) { double? g = ground.Sample(x, z); if (g.HasValue) y = g.Value; }
                treeInstances.Add(new JObj().Set("species", (long)si).Set("x", x - _cx).Set("y", y).Set("z", z - _cz).Set("height", height).Set("radius", radius));
                instanceSpecies.Add(si);
                if (replaced.Contains(si)) continue;
                int mi = Material(species.billboard); x -= _cx; z -= _cz;
                if (!plantRaw.TryGetValue(mi, out var raw)) { plantRaw[mi] = raw = new MemoryStream(); plantIndices[mi] = new List<int>(); plantOrder.Add(mi); }
                int first = (int)(raw.Length / 68);
                for (int i = 0; i < 4; i++)
                {
                    double px = i == 0 || i == 3 ? x - radius : x + radius, py = i < 2 ? y : y + height;
                    double u = i == 0 || i == 3 ? species.uLeft : species.uRight, v = i < 2 ? species.vBottom : species.vTop;
                    UnNum.PutF32(corner, 0, px); UnNum.PutF32(corner, 4, py); UnNum.PutF32(corner, 8, z);
                    for (int k = 0; k < 4; k++) { UnNum.PutF32(corner, 12 + k * 8, u); UnNum.PutF32(corner, 16 + k * 8, v); }
                    corner[44] = 128; corner[45] = 128; corner[46] = 128; corner[47] = 255;
                    UnNum.PutF32(corner, 48, x); UnNum.PutF32(corner, 52, y); UnNum.PutF32(corner, 56, z);
                    UnNum.PutF32(corner, 60, i == 0 || i == 3 ? -radius : radius); UnNum.PutF32(corner, 64, i < 2 ? 0 : height);
                    raw.Write(corner, 0, 68);
                }
                var ixs = plantIndices[mi];
                ixs.Add(first); ixs.Add(first + 2); ixs.Add(first + 1); ixs.Add(first); ixs.Add(first + 3); ixs.Add(first + 2);
                vegetation++;
            }
            foreach (int mi in plantOrder)
            {
                var raw = plantRaw[mi].ToArray(); var ixs = plantIndices[mi];
                var b = DrawHeader(mi, false, true, -1, raw.Length / 68, ixs.Count, raw.Length + ixs.Count * 4);
                Buffer.BlockCopy(raw, 0, b, 18, raw.Length);
                int o = 18 + raw.Length; foreach (int ix in ixs) { UnNum.PutI32(b, o, ix); o += 4; }
                _draws.Add(b);
            }
            _progress?.Invoke("Animated props", 0.7f);
            // the .toy's animated props (the ferris wheel, the stadium finish arch, windmills ...): hidden part groups + baked motion
            var toys = UnToys.Convert(this, track, treeGroups.Count, _ct);
            var funnelGroups = new List<string>(); byte[] funnelData = null;
            if (funnelCfg != null && (funnelParts[0].Count > 0 || funnelParts[1].Count > 0))
            {
                for (int k = 0; k < 2; k++)
                {
                    funnelGroups.Add("UnleashedFunnel:" + UnFunnel.Names[k]);
                    foreach (var part in funnelParts[k]) EmitDraw(part, treeGroups.Count + toys.groups.Count + k);
                }
                funnelData = UnFunnel.Pack(funnelCfg, _cx, _cz, funnelTris);
            }
            _progress?.Invoke("Collision", 0.75f);
            var collision = new List<byte[]>();
            foreach (var obj in scn.Sections)
            {
                _ct.ThrowIfCancellationRequested();
                if (!UnIni.Lower(obj.Name).StartsWith("object", StringComparison.Ordinal) || !obj.Has("col")) continue;
                string col = obj["col"] ?? throw new UnValueError("Unleashed scene: empty Col in [" + obj.Name + "]");
                string colLow = UnIni.Lower(col); bool skip = false;
                foreach (string name in rules.collisionNames) if (colLow.Contains(name)) { skip = true; break; }
                if (skip) continue;
                var shape = Collider(col);
                if (shape == null) { _missingCollision.Add(col); continue; }
                Placement(obj, out double[] pos, out double[] rotation);
                foreach (var part in shape.Parts)
                {
                    if (!part.hasVerts) throw new UnValueError("Unsupported collision type");
                    var points = CollisionPoints(part, rotation, pos, true);
                    for (int start = 0; start < points.Length / 3; start += 48000)
                    {
                        int n = Math.Min(48000, points.Length / 3 - start);
                        var b = new byte[4 + n * 12]; UnNum.PutI32(b, 0, n);
                        for (int i = 0; i < n * 3; i++) UnNum.PutF32Raw(b, 4 + i * 4, points[start * 3 + i]);
                        collision.Add(b);
                    }
                }
            }
            if (_missing.Count > 0)
            {
                var names = new SortedSet<string>(_missing, StringComparer.Ordinal);
                throw new UnValueError("Unresolved material textures: " + string.Join(", ", names));
            }
            var groups = new List<byte[]>();
            foreach (string g in treeGroups) groups.Add(Group(g, true));
            foreach (string g in toys.groups) groups.Add(Group(g, true));
            foreach (string g in funnelGroups) groups.Add(Group(g, false));

            _progress?.Invoke("Reflex vegetation", 0.8f);
            if (reflex)
            {
                var actual = UnReflexScenery.Write(_in.reflexRoot, track, treeInstances, instanceSpecies, ecosystem.species, result.files, _ct);
                if (!actual.SetEquals(replaced)) throw new UnValueError("Reviewed Reflex substitutions differ from the level's species");   // assert actual==replaced
            }
            var kept = new List<object>(); long reflexVegetation = 0;
            for (int i = 0; i < treeInstances.Count; i++) { if (replaced.Contains(instanceSpecies[i])) reflexVegetation++; else kept.Add(treeInstances[i]); }
            result.files["vegetation.json"] = UnJson.DumpsBytes(new JObj().Set("version", 1L).Set("groups", treeGroups).Set("instances", kept));
            result.files["scenery-fx.json"] = UnJson.DumpsBytes(new JObj().Set("version", 1L).Set("materials", _fx));
            if (toys.data != null) result.files["toys.rxtoys"] = toys.data;
            if (funnelData != null) result.files["funnel.rxfunnel"] = funnelData;
            result.files["scenery-manifest.json"] = UnJson.DumpsBytes(new JObj().Set("version", 1L).Set("rules", 2L).Set("placedDraws", (long)placedDraws).Set("models", manifest).Set("notes", _notes));

            _progress?.Invoke("Scenery file", 0.85f);
            var head = new byte[8]; head[0] = (byte)'R'; head[1] = (byte)'X'; head[2] = (byte)'S'; head[3] = (byte)'C'; UnNum.PutI32(head, 4, 1);
            using (var o = new MemoryStream())
            {
                o.Write(head, 0, 8);
                foreach (var collection in new[] { _textures, _materials, groups, _draws }) { var b = Collection(collection); o.Write(b, 0, b.Length); }
                result.files["scenery.rxscenery"] = o.ToArray();
            }
            using (var o = new MemoryStream())
            {
                var h = new byte[12]; h[0] = (byte)'R'; h[1] = (byte)'X'; h[2] = (byte)'C'; h[3] = (byte)'O'; UnNum.PutI32(h, 4, 1); UnNum.PutI32(h, 8, collision.Count);
                o.Write(h, 0, 12);
                foreach (var cchunk in collision) o.Write(cchunk, 0, cchunk.Length);
                result.files["collision.rxcollision"] = o.ToArray();
            }
            object SortedUnique(List<string> l) { var s = new SortedSet<string>(l, StringComparer.Ordinal); var r = new List<object>(); foreach (var x in s) r.Add(x); return r; }
            result.report = new JObj().Set("hiddenModels", rules.reasons).Set("models", (long)models).Set("draws", (long)_draws.Count).Set("textures", (long)_textures.Count)
                .Set("collisionChunks", (long)collision.Count).Set("vegetation", vegetation).Set("reflexVegetation", reflexVegetation).Set("treeModels", (long)treeGroups.Count)
                .Set("treeModelTriangles", treeTriangles).Set("missingSourceCollision", SortedUnique(_missingCollision)).Set("missingSourceTextures", SortedUnique(_missingTextures))
                .Set("materialRules", 2L).Set("animatedMaterials", (long)_fx.Count).Set("animatedProps", toys.report)
                .Set("funnel", funnelData != null ? new List<object> { (long)funnelTris[0].Count, (long)funnelTris[1].Count } : null).Set("notes", _notes);
        }

        /// A collision mesh at a scene pose: its triangles' corners as singles, 3 per corner (scenery.py's collision loop and
        /// funnel.triangles: (vertices x matrix[:3] + matrix[3]) x rotation + pos). check: refuse values that are not finite.
        public static float[] CollisionPoints(UnColMesh part, double[] rotation, double[] pos, bool check)
        {
            var v = UnCol.Vertices(part); int nv = v.Length / 3;
            var m = new double[9]; for (int i = 0; i < 9; i++) m[i] = part.matrix[i];
            double tx = part.matrix[9], ty = part.matrix[10], tz = part.matrix[11];
            for (int i = 0; i < nv; i++)
            {
                UnNum.RowMat3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2], m, out double x, out double y, out double z);
                x += tx; y += ty; z += tz;
                UnNum.RowMat3(x, y, z, rotation, out double wx, out double wy, out double wz);
                v[i * 3] = wx + pos[0]; v[i * 3 + 1] = wy + pos[1]; v[i * 3 + 2] = wz + pos[2];
            }
            var points = new float[part.tris.Length * 3];
            for (int i = 0; i < part.tris.Length; i++)
            {
                int ix = part.tris[i];
                if (ix >= nv) throw new UnValueError("Unleashed collision: a triangle outside its vertices");   // IndexError
                for (int k = 0; k < 3; k++)
                {
                    float f = (float)v[ix * 3 + k];   // astype('<f4'): out of range becomes infinity
                    if (check && (float.IsNaN(f) || float.IsInfinity(f))) throw new UnValueError("Invalid collision geometry");
                    points[i * 3 + k] = f;
                }
            }
            return points;
        }
    }
}
