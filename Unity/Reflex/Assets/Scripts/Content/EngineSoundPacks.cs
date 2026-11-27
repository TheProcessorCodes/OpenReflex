// Custom engine sound packs in the MX Bikes format (engine.scl + sfx.cfg + WAV samples), the per-bike choice of pack, and the WAV reader.
//
// Where packs come from (EngineSoundPacks.Roots, scanned in this order; the first pack of an id wins):
//   <StreamingAssets>/EngineSounds/<pack>/            packs shipped with a client build (none in the public repository)
//   <client root>/Mods/EngineSounds/<pack>/           the client's own Mods folder (ReflexMods: next to OpenReflex.app / OpenReflex.exe),
//                                                     the same convention as Mods/Bikes; created with a README on the first start
//   <the Reflex install>/Mods/EngineSounds/<pack>/    next to MXReflex.exe ("Mods" / "Mod", any case: the Open Unleashed convention)
//   <the install's parent>/Mods/EngineSounds/<pack>/  a Mods folder beside the install folder
//   <persistentDataPath>/Mods/EngineSounds/<pack>/    the per-user folder (macOS ~/Library/Application Support/<company>/<product>/...)
// A pack is any folder (at any depth up to eight: players make their own folders) that holds an engine.scl; MX Bikes' own
// "<bike>/<NAME>_SOUND/" layout drops in as is.
//
// engine.scl (MX Bikes' sound layer file): MaxValue = the rpm of normalized 1.0; AutoPitch; Layer0 = on throttle, Layer1 = off throttle;
// each SampleN { Wav, NumPoints, PointN { Value (normalized rpm), Volume }, RefPitch (the normalized rpm the sample was recorded at),
// MinPitch, MaxPitch }. ";;" starts a comment. Wav names may be lower case and carry a folder prefix ("HRC_250_SOUND\x.wav"): they are
// resolved case-insensitively by file name inside the pack folder.
// sfx.cfg (optional): engine { scl, min_distance, pos { x y z }, max_rpm, idlethrottle, blend_exp, limiter { wav, volume } } plus starter /
// gearbox / brakes / tyres, which name MX Bikes' base files (not in the packs: not used). Without sfx.cfg: max_rpm = MaxValue, the
// limiter = the folder's *limiter*.wav at 0.8, blend_exp 1.5, idlethrottle 0, min_distance 3.
// pack.ini (optional, ours): name = ..., author = ... for the menus; trim = a multiplier of the pack's level (default 1) on top of the
// loudness the player measures (EnginePackPlayer: every pack plays at the original engine's A-weighted level); gain = w-enginepacks' own
// calibration (a multiplier of EnginePackPlayer.MixGain), used only if the measurement fails.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Reflex.Port.Content
{
    public sealed class EnginePack
    {
        public sealed class Sample
        {
            public string wav;                 // the resolved file ("" = missing)
            public float[] value = new float[0], volume = new float[0];
            public float refPitch = 1f, minPitch = 0f, maxPitch = 4f;
            public float VolumeAt(float n)
            {
                int k = value.Length;
                if (k == 0) return 0f;
                if (n <= value[0]) return volume[0];
                for (int i = 1; i < k; i++)
                    if (n <= value[i]) { float d = value[i] - value[i - 1]; float t = d > 0f ? (n - value[i - 1]) / d : 1f; return volume[i - 1] + (volume[i] - volume[i - 1]) * t; }
                return volume[k - 1];
            }
            public float PitchAt(float n, bool autoPitch)
            {
                if (!autoPitch || refPitch <= 0f) return 1f;
                return Mathf.Clamp(n / refPitch, minPitch, maxPitch);
            }
        }
        public string id, name, author = "", folder, source;
        public float maxValue = 14000f, maxRpm = 14000f, idleThrottle = 0f, blendExp = 1.5f, minDistance = 3f;
        public float gain = 1f; public bool gainSet;   // pack.ini gain (x EnginePackPlayer.MixGain: the fallback when the loudness is not measured)
        public float trim = 1f;                // pack.ini trim (x the measured loudness gain)
        public bool autoPitch = true;
        public Vector3 pos;
        public readonly List<Sample>[] layers = { new List<Sample>(), new List<Sample>() };   // 0 on throttle, 1 off throttle
        public string limiterWav = ""; public float limiterVolume = 0.8f;
        public readonly List<string> warnings = new List<string>();
        public string Title => string.IsNullOrEmpty(author) ? name : name + " · " + author;

        /// The normalized rpm the pack idles at: the off-throttle layer's first sample's recording rpm (else the on layer's).
        public float IdleNorm
        {
            get
            {
                foreach (var l in new[] { layers[1], layers[0] }) if (l.Count > 0 && l[0].refPitch > 0f) return Mathf.Clamp(l[0].refPitch, 0.02f, 0.6f);
                return 0.18f;
            }
        }
    }

    public static class EngineSoundPacks
    {
        public const string Original = "";             // the choice "Original" (the game's own engine event)
        public const string KeyPrefix = "EngineSound.Model.";
        public const string DefaultKey = "EngineSound.Default";

        static List<EnginePack> s_packs;
        static readonly object s_lock = new object();
        static string s_streaming, s_persistent;

        /// The folders scanned (see the file header). Main thread first (Application paths).
        public static List<(string dir, string source)> Roots()
        {
            if (s_streaming == null) { try { s_streaming = Path.Combine(Application.streamingAssetsPath, "EngineSounds"); } catch (Exception) { s_streaming = ""; } }
            if (s_persistent == null) { try { s_persistent = Path.Combine(Application.persistentDataPath, "Mods", "EngineSounds"); } catch (Exception) { s_persistent = ""; } }
            var l = new List<(string, string)>();
            if (s_streaming.Length > 0) l.Add((s_streaming, "builtin"));
            string client = ReflexMods.Folder(ReflexMods.EngineSoundsName);
            if (!string.IsNullOrEmpty(client)) l.Add((client, "mods"));
            string install = null; try { install = ReflexInstall.Dir; } catch (Exception) { }
            if (!string.IsNullOrEmpty(install))
            {
                foreach (var baseDir in new[] { install, Path.GetDirectoryName(install.TrimEnd('/', '\\')) })
                {
                    if (string.IsNullOrEmpty(baseDir)) continue;
                    var mods = ChildIgnoreCase(baseDir, "Mods") ?? ChildIgnoreCase(baseDir, "Mod");
                    if (mods == null) continue;
                    l.Add((ChildIgnoreCase(mods, "EngineSounds") ?? Path.Combine(mods, "EngineSounds"), "mods"));
                }
            }
            if (s_persistent.Length > 0) l.Add((s_persistent, "user"));
            return l;
        }
        /// The per-user mods folder (created on demand by the menus' "open folder").
        public static string UserFolder { get { Roots(); return s_persistent; } }

        static string ChildIgnoreCase(string dir, string name)
        {
            try { if (!Directory.Exists(dir)) return null; foreach (var d in Directory.GetDirectories(dir)) if (string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)) return d; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            return null;
        }

        /// Every pack found (cached; Refresh rescans).
        public static List<EnginePack> All { get { lock (s_lock) { if (s_packs == null) s_packs = Scan(); return s_packs; } } }
        public static void Refresh() { lock (s_lock) s_packs = null; lock (s_resolved) s_choices++; }
        public static EnginePack Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = All;   // (a loop, not LINQ with a capturing lambda: the engine sound asks every frame for every rider, w-gcspikes)
            for (int i = 0; i < all.Count; i++) if (all[i].id == id) return all[i];
            return null;
        }

        static List<EnginePack> Scan()
        {
            var packs = new List<EnginePack>(); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (root, source) in Roots())
            {
                if (!Directory.Exists(root)) continue;
                var found = new List<string>();
                void Walk(string d, int depth)
                {
                    try
                    {
                        if (Directory.GetFiles(d).Any(f => string.Equals(Path.GetFileName(f), "engine.scl", StringComparison.OrdinalIgnoreCase))) { found.Add(d); return; }
                        if (depth < 8) foreach (var c in Directory.GetDirectories(d).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) if (!ReflexMods.IsJunk(Path.GetFileName(c))) Walk(c, depth + 1);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
                foreach (var c in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) if (!ReflexMods.IsJunk(Path.GetFileName(c))) Walk(c, 1);
                foreach (var dir in found)
                {
                    string rel = dir.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                    string id = (source == "builtin" ? "builtin:" : "mod:") + rel;
                    if (!ids.Add(id)) continue;
                    try { packs.Add(Load(dir, id, source, rel)); }
                    catch (Exception e) { Debug.LogWarning("[EngineSounds] " + dir + ": " + e.Message); }
                }
            }
            return packs;
        }

        // ------------------------------------------------------------------------------------------------ the files
        /// A block file (scl / cfg): "key = value" lines and "Name { ... }" blocks; ";;" / ";" / "#" / "//" comments.
        sealed class Block
        {
            public readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<(string name, Block block)> blocks = new List<(string, Block)>();
            public Block Child(string name) { foreach (var b in blocks) if (string.Equals(b.name, name, StringComparison.OrdinalIgnoreCase)) return b.block; return null; }
            public string S(string k, string d = null) => values.TryGetValue(k, out var v) ? v : d;
            public float F(string k, float d) => values.TryGetValue(k, out var v) && float.TryParse(v.Trim().TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : d;
        }
        static Block Parse(string text)
        {
            var root = new Block(); var stack = new Stack<Block>(); stack.Push(root);
            string pending = null;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw;
                int c = IndexOfComment(line); if (c >= 0) line = line.Substring(0, c);
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line == "{") { var b = new Block(); stack.Peek().blocks.Add((pending ?? "", b)); stack.Push(b); pending = null; continue; }
                if (line == "}") { if (stack.Count > 1) stack.Pop(); pending = null; continue; }
                if (line.EndsWith("{")) { var b = new Block(); stack.Peek().blocks.Add((line.TrimEnd('{').Trim(), b)); stack.Push(b); pending = null; continue; }
                int eq = line.IndexOf('=');
                if (eq > 0) { stack.Peek().values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Trim('"'); pending = null; }
                else pending = line;
            }
            return root;
        }
        static int IndexOfComment(string s)
        {
            int best = -1;
            foreach (var m in new[] { ";", "#", "//" }) { int i = s.IndexOf(m, StringComparison.Ordinal); if (i >= 0 && (best < 0 || i < best)) best = i; }
            return best;
        }

        static EnginePack Load(string dir, string id, string source, string rel)
        {
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            string FindFile(string name)
            {
                if (string.IsNullOrEmpty(name)) return "";
                string leaf = name.Replace('\\', '/'); leaf = leaf.Substring(leaf.LastIndexOf('/') + 1).Trim();
                foreach (var f in files) if (string.Equals(Path.GetFileName(f), leaf, StringComparison.OrdinalIgnoreCase)) return f;
                return "";
            }
            var p = new EnginePack { id = id, folder = dir, source = source };
            var segs = rel.Split('/').Where(s => s.Length > 0 && !s.EndsWith("_SOUND", StringComparison.OrdinalIgnoreCase) && !string.Equals(s, "sound", StringComparison.OrdinalIgnoreCase) && !string.Equals(s, "sounds", StringComparison.OrdinalIgnoreCase)).ToArray();
            p.name = segs.Length > 0 ? string.Join(" · ", segs) : Path.GetFileName(dir);
            string ini = FindFile("pack.ini");
            if (ini.Length > 0) { var b = Parse(File.ReadAllText(ini)); p.name = b.S("name", p.name); p.author = b.S("author", ""); p.gainSet = b.S("gain") != null; p.gain = Mathf.Clamp(b.F("gain", 1f), 0f, 4f); p.trim = Mathf.Clamp(b.F("trim", 1f), 0f, 4f); }
            var cfgFile = FindFile("sfx.cfg");
            var cfg = cfgFile.Length > 0 ? Parse(File.ReadAllText(cfgFile)).Child("engine") : null;
            string sclName = cfg?.S("scl") ?? "engine.scl";
            string sclFile = FindFile(sclName); if (sclFile.Length == 0) sclFile = FindFile("engine.scl");
            var scl = Parse(File.ReadAllText(sclFile));
            p.maxValue = scl.F("MaxValue", 14000f);
            p.autoPitch = scl.F("AutoPitch", 1f) != 0f;
            for (int l = 0; l < 2; l++)
            {
                var layer = scl.Child("Layer" + l);
                if (layer == null) continue;
                int n = (int)layer.F("NumSamples", 0);
                for (int s = 0; s < Math.Max(n, layer.blocks.Count); s++)
                {
                    var b = layer.Child("Sample" + s); if (b == null) continue;
                    var smp = new EnginePack.Sample { refPitch = b.F("RefPitch", 1f), minPitch = b.F("MinPitch", 0f), maxPitch = b.F("MaxPitch", 4f) };
                    string wav = b.S("Wav", ""); smp.wav = FindFile(wav);
                    if (smp.wav.Length == 0) p.warnings.Add("missing " + wav);
                    int k = (int)b.F("NumPoints", 0); var vs = new List<float>(); var vo = new List<float>();
                    for (int q = 0; q < Math.Max(k, b.blocks.Count); q++) { var pt = b.Child("Point" + q); if (pt == null) continue; vs.Add(pt.F("Value", 0f)); vo.Add(pt.F("Volume", 0f)); }
                    smp.value = vs.ToArray(); smp.volume = vo.ToArray();
                    if (smp.wav.Length > 0) p.layers[l].Add(smp);
                }
            }
            p.maxRpm = p.maxValue;
            if (cfg != null)
            {
                p.maxRpm = cfg.F("max_rpm", p.maxValue); p.idleThrottle = cfg.F("idlethrottle", 0f); p.blendExp = cfg.F("blend_exp", 1.5f);
                p.minDistance = cfg.F("min_distance", 3f);
                var pos = cfg.Child("pos"); if (pos != null) p.pos = new Vector3(pos.F("x", 0f), pos.F("y", 0f), pos.F("z", 0f));
                var lim = cfg.Child("limiter"); if (lim != null) { p.limiterWav = FindFile(lim.S("wav", "")); p.limiterVolume = lim.F("volume", 0.8f); }
            }
            if (p.limiterWav.Length == 0) p.limiterWav = files.FirstOrDefault(f => Path.GetExtension(f).Equals(".wav", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(f).IndexOf("limiter", StringComparison.OrdinalIgnoreCase) >= 0) ?? "";
            if (p.layers[0].Count == 0 && p.layers[1].Count == 0) throw new InvalidDataException("no playable samples in " + Path.GetFileName(sclFile));
            return p;
        }

        // ------------------------------------------------------------------------------------------------ the per-bike choice
        // A choice is a string: "" Original, "mod:..." / "builtin:..." an MX Bikes pack, "alive:<PROJECT>" an imported MX vs ATV Alive engine
        // set (AliveEngineSets, played by OriginalWorld/AliveEngineAudio). Which one a model plays (core/plan/ALIVE_ENGINE_SOUNDS.md section 6):
        //   1. while a bike mod is active on the model (BikeMods.ChoiceFor, the garage's TEST RIDE first): its own choice EngineSound.Mod.<modId>,
        //      so two mods on one Reflex slot (two imported Alive bikes) keep their own engines;
        //   2. an imported Alive bike (mod "AliveBikes/alive-<aliveId>-...") without one: its own Alive set (AliveEngineSets.ForAliveVehicle);
        //   3. the model's choice EngineSound.Model.<id>;
        //   4. the OPTIONS default EngineSound.Default; else Original.
        // Resolved once per model and cached until a choice, a bike mod, the TEST RIDE, the packs or the Alive sets change: PackFor / AliveSetFor
        // run every game frame for every rider (no allocation, w-gcspikes).
        public const string ModKeyPrefix = "EngineSound.Mod.";

        sealed class Resolved
        {
            public int choices = -1, bikes, sets, rideModel; public string rideMod;
            public string mod = "", key = "", choice = Original; public bool own, bikesOwn;
            public AliveEngineSet ownSet, aliveSet; public EnginePack pack;
        }
        static readonly Dictionary<int, Resolved> s_resolved = new Dictionary<int, Resolved>();
        static int s_choices;   // bumped by every change of an EngineSound.* key and by Refresh
        static bool s_hooked;

        static Resolved Resolve(int model)
        {
            lock (s_resolved)
            {
                if (!s_hooked) { s_hooked = true; Options.ReflexSave.Changed += k => { if (k != null && k.StartsWith("EngineSound.", StringComparison.Ordinal)) lock (s_resolved) s_choices++; }; }
                if (!s_resolved.TryGetValue(model, out var r)) s_resolved[model] = r = new Resolved();
                if (r.choices == s_choices && r.bikes == BikeMods.Version && r.sets == AliveEngineSets.Version && r.rideModel == BikeMods.RideModel && ReferenceEquals(r.rideMod, BikeMods.RideMod)) return r;
                r.choices = s_choices; r.bikes = BikeMods.Version; r.sets = AliveEngineSets.Version; r.rideModel = BikeMods.RideModel; r.rideMod = BikeMods.RideMod;
                r.mod = ActiveMod(model);
                r.key = r.mod.Length > 0 ? ModKeyPrefix + r.mod : KeyPrefix + model;
                r.ownSet = AliveEngineSets.ForAliveVehicle(AliveIdOf(PaintedMod(r.mod, model)));
                string v = null; r.own = r.bikesOwn = false;
                if (r.mod.Length > 0 && Options.ReflexSave.TryGetString(r.key, out v)) r.own = true;
                else if (r.ownSet != null) { v = r.ownSet.id; r.bikesOwn = true; }
                else if (Options.ReflexSave.TryGetString(KeyPrefix + model, out v)) r.own = true;
                else v = Options.ReflexSave.GetString(DefaultKey, Original);
                r.choice = v ?? Original;
                r.pack = Find(r.choice);
                r.aliveSet = r.choice.StartsWith(AliveEngineSets.IdPrefix, StringComparison.OrdinalIgnoreCase) ? AliveEngineSets.Find(r.choice) : null;
                return r;
            }
        }
        /// The bike mod in effect on a model: the garage's TEST RIDE of it, else its saved choice ("" the original bike).
        public static string ActiveMod(int vehicleModelId)
        {
            if (vehicleModelId < 0) return Original;
            if (vehicleModelId == BikeMods.RideModel && BikeMods.RideMod != null && (BikeMods.RideMod.Length == 0 || BikeMods.Find(BikeMods.RideMod) != null)) return BikeMods.RideMod;
            return BikeMods.ChoiceFor(vehicleModelId) ?? Original;
        }
        /// The bike mod whose bike a mod puts on a model: a skin mod's painted bike's mod (an imported Alive bike under a skin keeps its own
        /// Alive set), else the mod itself.
        static string PaintedMod(string modId, int model)
        {
            var m = BikeMods.Find(modId);
            if (m?.rx == null || m.rx.type != "skins") return modId;
            var b = m.rx.skins.FirstOrDefault(k => k.target != null && k.target.vehicleModelId == model)?.target;
            return b == null ? modId : BikeMods.All.FirstOrDefault(x => ReferenceEquals(x.rx, b.mod))?.id ?? modId;
        }
        /// The Alive VehicleModel id of an imported Alive bike's mod id ("AliveBikes/alive-<id>-<label>", AliveImport.AliveBikeSource.Slug), -1 other.
        public static int AliveIdOf(string modId)
        {
            string p = AliveImport.AliveBikes.Folder + "/alive-";
            if (modId == null || !modId.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return -1;
            int i = p.Length, v = 0, n = 0;
            for (; i < modId.Length && modId[i] >= '0' && modId[i] <= '9' && n < 9; i++, n++) v = v * 10 + (modId[i] - '0');
            return n > 0 ? v : -1;
        }

        /// The choice a vehicle model plays (VehicleModelId; the garage's ENGINE SOUND), resolved as above.
        public static string ChoiceFor(int vehicleModelId) => vehicleModelId < 0 ? Original : Resolve(vehicleModelId).choice;
        /// Whether the model (or its active mod) has its own stored choice (else it follows the bike's own Alive set or the default).
        public static bool HasOwnChoice(int vehicleModelId) => vehicleModelId >= 0 && Resolve(vehicleModelId).own;
        /// The model plays its imported Alive bike's own set because nothing else is chosen ("BIKE'S OWN" in the menus).
        public static bool IsBikesOwn(int vehicleModelId) => vehicleModelId >= 0 && Resolve(vehicleModelId).bikesOwn;
        /// The installed Alive set of the model's active imported Alive bike, null: none.
        public static AliveEngineSet OwnAliveSet(int vehicleModelId) => vehicleModelId < 0 ? null : Resolve(vehicleModelId).ownSet;
        /// Stores a choice: per mod while a bike mod is active on the model, else per model.
        public static void SetChoice(int vehicleModelId, string packId)
        {
            if (vehicleModelId < 0) return;
            Options.ReflexSave.SetString(Resolve(vehicleModelId).key, packId ?? Original); Options.ReflexSave.Save();
        }
        /// Removes the model's (or its active mod's) own choice: it follows the bike's own Alive set or the default again.
        public static void ClearChoice(int vehicleModelId)
        {
            if (vehicleModelId < 0) return;
            string key = Resolve(vehicleModelId).key;
            if (Options.ReflexSave.Has(key)) { Options.ReflexSave.Delete(key); Options.ReflexSave.Save(); }
        }
        public static string DefaultChoice { get => Options.ReflexSave.GetString(DefaultKey, Original); set { Options.ReflexSave.SetString(DefaultKey, value ?? Original); Options.ReflexSave.Save(); } }
        /// The MX Bikes pack to play for a model (null = not a pack: the original engine or an Alive set): a chosen pack that is not installed
        /// any more plays the original.
        public static EnginePack PackFor(int vehicleModelId) => vehicleModelId < 0 ? null : Resolve(vehicleModelId).pack;
        /// The Alive engine set to play for a model (null = none chosen, or the chosen set is not installed any more: the original engine).
        public static AliveEngineSet AliveSetFor(int vehicleModelId) => vehicleModelId < 0 ? null : Resolve(vehicleModelId).aliveSet;
        /// "ORIGINAL", the pack's title or the Alive set's ("ALIVE 450F"); a choice that is not installed reads "ORIGINAL" (what plays).
        public static string Title(string id)
        {
            if (id != null && id.StartsWith(AliveEngineSets.IdPrefix, StringComparison.OrdinalIgnoreCase)) { var s = AliveEngineSets.Find(id); return s == null ? "ORIGINAL" : s.Title; }
            var p = Find(id); return p == null ? "ORIGINAL" : p.Title.ToUpperInvariant();
        }
        /// The choices in menu order: Original, every pack, then every installed Alive set.
        public static List<string> Choices()
        {
            var l = new List<string> { Original }; l.AddRange(All.Select(p => p.id));
            foreach (var s in AliveEngineSets.All) l.Add(s.id);
            return l;
        }
    }

    /// RIFF WAVE reader: PCM 8 / 16 / 24 / 32-bit and IEEE float, any channel count (downmixed to mono for the 3D engine voices).
    public static class EngineWav
    {
        public sealed class Data { public float[] samples; public int rate; public string name; }
        public static Data ReadMono(string path)
        {
            var b = File.ReadAllBytes(path);
            if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE") throw new InvalidDataException("not a RIFF WAVE file");
            int format = 0, channels = 0, rate = 0, bits = 0, dataAt = -1, dataLen = 0;
            for (int p = 12; p + 8 <= b.Length;)
            {
                string id = Encoding.ASCII.GetString(b, p, 4); int len = BitConverter.ToInt32(b, p + 4);
                if (len < 0) break;
                int body = p + 8;
                if (id == "fmt " && body + 16 <= b.Length)
                {
                    format = BitConverter.ToUInt16(b, body); channels = BitConverter.ToUInt16(b, body + 2); rate = BitConverter.ToInt32(b, body + 4); bits = BitConverter.ToUInt16(b, body + 14);
                    if (format == 0xfffe && len >= 40) format = BitConverter.ToUInt16(b, body + 24);   // WAVE_FORMAT_EXTENSIBLE: the sub-format GUID's first word
                }
                else if (id == "data") { dataAt = body; dataLen = Math.Min(len, b.Length - body); break; }
                p = body + len + (len & 1);
            }
            if (dataAt < 0 || channels <= 0 || rate <= 0) throw new InvalidDataException("no fmt / data chunk");
            if (!(format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !(format == 3 && (bits == 32 || bits == 64))) throw new InvalidDataException("unsupported WAVE format " + format + " / " + bits + " bit");
            int bytes = bits / 8, frames = dataLen / (bytes * channels);
            var o = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    int at = dataAt + (f * channels + c) * bytes; float v;
                    if (format == 3) v = bits == 32 ? BitConverter.ToSingle(b, at) : (float)BitConverter.ToDouble(b, at);
                    else switch (bits)
                    {
                        case 8: v = (b[at] - 128) / 128f; break;
                        case 16: v = BitConverter.ToInt16(b, at) / 32768f; break;
                        case 24: v = ((b[at] | (b[at + 1] << 8) | ((sbyte)b[at + 2] << 16))) / 8388608f; break;
                        default: v = BitConverter.ToInt32(b, at) / 2147483648f; break;
                    }
                    sum += v;
                }
                o[f] = sum / channels;
            }
            return new Data { samples = o, rate = rate, name = Path.GetFileNameWithoutExtension(path) };
        }
    }
}
