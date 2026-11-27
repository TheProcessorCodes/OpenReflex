// MX vs ATV Alive's engine sounds, imported from the player's own Alive install (core/plan/ALIVE_ENGINE_SOUNDS.md): each of Alive's engine
// sound sets becomes a folder <content>/AliveEngineSounds/<PROJECT>/ that AliveEngineSets lists and AliveEngineAudio plays with Alive's
// own state machine. Built on the player's machine from the disc and the LIVE packages (nothing of Alive ships with the game).
//
//   which sets     every Alive vehicle (VehicleModel rows of a "MotocrossBike" or "Atv" VehicleType, also those AliveBikes cannot
//                  import) has an EngineAudio part (VehiclePartType "EngineAudio"): its VehicleModelParts row marked Default, else one
//                  of the model's PresetVehicleParts (PresetVehicle.VehicleModelId; the MXFR650, model 22, reaches its part 869 only that
//                  way). The part's material package (<db>_<size>_Default) holds the whole set: AudioEngineConfig_<size>.bxml (its
//                  root's sound= names the FMOD project), EngineActions*.bxml, <project>.sound (char[64] name, u32 bank count, char[64]
//                  per bank, u32 FEV size, the FEV1 v0x40 image) and <bank>.soundbnk (char[64] name, u32 size, the FSB4 image). Seven
//                  projects: MX_450_TRACK, MX_250F, MX_125R, MX_400_TRACK, ATV_1000cc (disc), MX_50cc (Stewart Compound), MX_250R
//                  (Sliderock); the ATV packages hold byte-identical copies of the bike sets, so one source per project (the first
//                  package that holds it, the bikes' before the ATVs')
//   the samples    FSB4, mono 44.1 kHz XMA with the sync points (SYNC: u32 count, count x {u32 sample, char[256] name}) after the XMA
//                  seek block in each sample header. Each is wrapped as RIFF/XMA2 and decoded by the client's ffmpeg (AliveXma) to
//                  PCM16 of exactly the sample's length
//   the output     <PROJECT>.fev (the FEV as shipped), <PROJECT>.fsb (FSB4: the original 48-byte header with header mode 0, per sample
//                  the fixed 80-byte header - length, PCM16 | mono | the loop bits, loop points and 3D defaults kept - and the original
//                  SYNC block, the PCM16 LE data back to back), AudioEngineConfig.xml and EngineActions.xml (the bxml element trees as
//                  indented XML), set.json (what it is, which Alive vehicles use it, its calibration trim). Same disc and ffmpeg: same bytes.
//   installing     as AliveBikes: staged in <content>/AliveEngineSounds/.ae-<8hex>, moved in; <PROJECT>.import.json lists every file with
//                  its sha256; a <PROJECT> folder without that manifest is the player's and is never replaced; links are refused.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace Reflex.Port.Content.AliveImport
{
    /// One Alive engine sound set (an FMOD project and its bank) found in the player's sources.
    public sealed class AliveEngineSoundSource
    {
        public string project = "";                  // the FMOD project ("MX_450_TRACK"): the folder's name
        public string db = "", pkg = "";             // the material package that holds it (MX_engineaudio / MX_engineaudio_MX450f_Default)
        public string actions = "";                  // the action lists' asset ("EngineActions_2Stroke")
        public List<int> aliveIds = new List<int>(); // the Alive vehicles (VehicleModelId) whose EngineAudio part names it
        public List<string> vehicles = new List<string>();   // their names, in aliveIds' order ("Honda CRF450R")
        public string source = "";                   // the disc or the LIVE pack it came from
        public bool installed;                       // imported before (set by the caller: Installed)
        public string problem;                       // why it cannot be imported (null: it can)
        public string name = "";                     // the class it is ("450F"): set.json, the menus' "ALIVE 450F"
        public string config = "", bank = "";        // the config's and the bank's asset names
        public int samples; public double seconds;   // the bank's sample count and length
        public long Bytes => (long)(seconds * 44100) * 2;   // about the size of the PCM16 output
        public bool Importable => problem == null;
        /// The list row's title: "450F ENGINE · CRF450R, YZ450F, BLAZE 450F +3".
        public string Label
        {
            get
            {
                var shown = shortNames.Take(3).ToList();
                return (name + " ENGINE" + (shown.Count > 0 ? "  ·  " + string.Join(", ", shown) + (shortNames.Count > 3 ? " +" + (shortNames.Count - 3) : "") : "")).ToUpperInvariant();
            }
        }
        internal readonly List<string> shortNames = new List<string>();
    }

    public sealed class AliveEngineSoundResult
    {
        public AliveEngineSoundSource set; public bool ok; public string error, folder;
        public int samples, syncs; public long bytes; public double seconds;
    }

    public static class AliveEngineSounds
    {
        public const string Folder = "AliveEngineSounds";
        public const int Format = 1;
        // FSB sample mode bits (ReflexFmod's: this file builds outside the client as well)
        const uint LoopOff = 0x1, LoopNormal = 0x2, LoopBits = 0x7, Pcm16 = 0x10, Mono = 0x20, XmaMode = 0x01000000;

        /// The set's class by its project (the menus' "ALIVE 450F"); another project is named by itself.
        static readonly (string project, string name)[] Names =
        {
            ("MX_450_TRACK", "450F"), ("MX_250F", "250F"), ("MX_125R", "125"), ("MX_400_TRACK", "650"), ("MX_50cc", "50cc"), ("MX_250R", "250R"), ("ATV_1000cc", "ATV 1000"),
        };
        static int NameIndex(string project) => Array.FindIndex(Names, n => string.Equals(n.project, project, StringComparison.OrdinalIgnoreCase));

        /// The calibration trim per project (set.json "trim": the event volume AliveEngineAudio posts, x the field trim for the computer riders;
        /// core/plan/ALIVE_ENGINE_SOUNDS.md section 7): the Reflex engine of the set's class over the set, as A-weighted rms levels of the local
        /// rider's engine on a race mix of the states (AIFX 0, the events' own volumes; tests/alive_engine/level, 2026-11-14), rounded to 0.01 and
        /// clamped to 0.25 .. 4. Measured levels (rms, full scale 1):
        ///   MX_450_TRACK 0.2338 vs MX_450F_01 0.2043 (+1.17 dB) -> 0.87      MX_250F 0.2946 vs MX_250F_01 0.1339 (+6.85 dB) -> 0.45
        ///   MX_125R      0.2916 vs MX_125R_01 0.2087 (+2.91 dB) -> 0.72      MX_250R 0.2637 vs MX_250R_TEST_05 0.1883 (+2.93 dB) -> 0.71
        ///   MX_400_TRACK 0.2118 vs MX_450F_01 0.2043 (+0.31 dB) -> 0.96      ATV_1000cc 0.2596 vs MX_450F_01 0.2043 (+2.08 dB) -> 0.79
        ///   MX_50cc      0.1538 vs MX_125R_01 0.2087 (-2.65 dB) -> 1.36
        /// Another project gets no "trim" (it plays at 1).
        static readonly (string project, double trim)[] Trims =
        {
            ("MX_450_TRACK", 0.87), ("MX_250F", 0.45), ("MX_125R", 0.72), ("MX_400_TRACK", 0.96), ("MX_50cc", 1.36), ("MX_250R", 0.71), ("ATV_1000cc", 0.79),
        };
        /// The project's calibration trim, null when it has none.
        public static double? TrimOf(string project)
        {
            int i = Array.FindIndex(Trims, t => string.Equals(t.project, project, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? Trims[i].trim : (double?)null;
        }
        static readonly string[] RealBrands = { "Honda", "Yamaha", "KTM", "Suzuki", "Kawasaki" };

        // ------------------------------------------------------------------------------------- finding the sets

        /// Every engine sound set the sources' vehicles use (AliveImporter.Scan, after the bikes: their names come from r.bikes).
        public static List<AliveEngineSoundSource> Find(AliveSources r, CancellationToken cancel)
        {
            var t = new AliveBikes.Tables();
            foreach (var v in r.volumes.OrderBy(AliveImporter.PackOrder))
                foreach (var db in v.files.Where(f => f.Name.EndsWith(".xenon.database", StringComparison.OrdinalIgnoreCase) && f.Name.StartsWith("MXTables_", StringComparison.Ordinal)
                                                    && !f.Name.StartsWith("MXTables_Schema", StringComparison.Ordinal) && !f.Name.StartsWith("MXTables_Leaderboards", StringComparison.Ordinal))
                                          .OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    cancel.ThrowIfCancellationRequested();
                    foreach (var a in AliveBikes.Package.All(v, db, (p, n, ty) => ty == "tbldata", cancel)) t.Add(a.data, v.displayName);
                }
            Dictionary<long, Dictionary<string, object>> ById(string table, string key) => t.Of(table).Where(x => AliveBikes.Tables.L(x, key) != null).GroupBy(x => AliveBikes.Tables.L(x, key).Value).ToDictionary(g => g.Key, g => g.Last());
            var types = ById("VehicleType", "VehicleTypeId");
            var parts = ById("VehiclePart", "VehiclePartId");
            var partTypes = ById("VehiclePartType", "VehiclePartTypeId");
            var pkgs = ById("AssetPackage", "PackageId"); var dbs = ById("AssetDatabase", "DatabaseId"); var pkgDb = ById("AssetPackageDatabase", "PackageDatabaseId");
            (string db, string pkg)? PkgOf(long? pdb)
            {
                if (!(pdb is long x) || !pkgDb.TryGetValue(x, out var row)) return null;
                string p = pkgs.TryGetValue(AliveBikes.Tables.L(row, "PackageId") ?? -1, out var pr) ? AliveBikes.Tables.S(pr, "PackageName") : null;
                string d = dbs.TryGetValue(AliveBikes.Tables.L(row, "DatabaseId") ?? -1, out var dr) ? AliveBikes.Tables.S(dr, "DatabaseName") : null;
                return p != null && d != null ? (d, p) : ((string, string)?)null;
            }
            (string db, string pkg)? AudioPart(long partId)
            {
                if (!parts.TryGetValue(partId, out var part)) return null;
                if (!partTypes.TryGetValue(AliveBikes.Tables.L(part, "VehiclePartTypeId") ?? -1, out var pt) || AliveBikes.Tables.S(pt, "Name") != "EngineAudio") return null;
                return PkgOf(AliveBikes.Tables.L(part, "MaterialPackageDatabaseId"));
            }
            var modelParts = t.Of("VehicleModelParts").ToList();
            var presets = t.Of("PresetVehicle").Where(x => AliveBikes.Tables.L(x, "PresetVehicleId") != null).OrderBy(x => AliveBikes.Tables.L(x, "PresetVehicleId")).ToList();
            var presetParts = t.Of("PresetVehicleParts").ToList();
            (string db, string pkg)? EngineAudio(long id)
            {
                foreach (var mp in modelParts)
                    if (AliveBikes.Tables.L(mp, "VehicleModelId") == id && AliveBikes.Tables.L(mp, "Default") == 1 && AudioPart(AliveBikes.Tables.L(mp, "VehiclePartId") ?? -1) is var a && a != null) return a;
                foreach (var pv in presets)
                {
                    if (AliveBikes.Tables.L(pv, "VehicleModelId") != id) continue;
                    long pid = AliveBikes.Tables.L(pv, "PresetVehicleId").Value;
                    foreach (var pp in presetParts)
                        if (AliveBikes.Tables.L(pp, "PresetVehicleId") == pid && AudioPart(AliveBikes.Tables.L(pp, "VehiclePartId") ?? -1) is var a && a != null) return a;
                }
                return null;
            }

            // the vehicles: the bikes before the ATVs, the licensed models first (the row titles), then by id
            var labels = r.bikes.GroupBy(b => b.aliveId).ToDictionary(g => g.Key, g => g.First());
            var vehicles = new List<(int id, bool atv, string label, string shortName)>();
            foreach (var m in t.Of("VehicleModel").GroupBy(x => AliveBikes.Tables.L(x, "VehicleModelId") ?? -1).Select(g => g.Last()))
            {
                long id = AliveBikes.Tables.L(m, "VehicleModelId") ?? -1;
                if (id < 0 || !types.TryGetValue(AliveBikes.Tables.L(m, "VehicleTypeId") ?? -1, out var type)) continue;
                string factory = AliveBikes.Tables.S(type, "FactoryName");
                if (factory != "MotocrossBike" && factory != "Atv") continue;
                labels.TryGetValue((int)id, out var b);
                string label = b != null ? b.Label : "Vehicle " + id;
                bool real = b != null && RealBrands.Any(x => string.Equals(x, b.brand, StringComparison.OrdinalIgnoreCase));
                vehicles.Add(((int)id, factory == "Atv", label, b == null ? label : real || b.brand == "MX vs ATV" ? b.model : b.Label));
            }
            vehicles = vehicles.OrderBy(x => x.atv ? 1 : 0).ThenBy(x => RealBrands.Any(rb => x.label.StartsWith(rb + " ", StringComparison.OrdinalIgnoreCase)) ? 0 : 1).ThenBy(x => x.id).ToList();

            // each vehicle's package: its config names the project; the first package that holds the project's .sound and bank is the source
            var index = new AliveBikes.PackageIndex(r.volumes);
            var volumeOf = new Dictionary<string, Volume>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in r.volumes)
                foreach (var f in v.files.Where(f => f.Name.EndsWith(".xenon.database", StringComparison.OrdinalIgnoreCase)))
                {
                    string n = f.Name.Substring(0, f.Name.Length - ".xenon.database".Length);
                    if (!volumeOf.ContainsKey(n)) volumeOf[n] = v;
                }
            var read = new Dictionary<string, (string project, AliveEngineSoundSource held)>(StringComparer.OrdinalIgnoreCase);
            var sets = new List<AliveEngineSoundSource>();
            foreach (var veh in vehicles)
            {
                cancel.ThrowIfCancellationRequested();
                var part = EngineAudio(veh.id);
                if (part == null) continue;
                string key = part.Value.db + "/" + part.Value.pkg;
                if (!read.TryGetValue(key, out var info))
                {
                    var pa = index.Has(part.Value.db) ? index.Read(part.Value.db, part.Value.pkg, cancel) : null;
                    info = pa == null ? (null, null) : Inspect(pa, volumeOf.TryGetValue(part.Value.db, out var vol) ? vol : null);
                    read[key] = info;
                }
                if (info.project == null) continue;   // (its pack is not among the sources)
                var set = sets.FirstOrDefault(s => string.Equals(s.project, info.project, StringComparison.OrdinalIgnoreCase));
                if (set == null)
                {
                    set = info.held ?? new AliveEngineSoundSource { project = info.project, db = part.Value.db, pkg = part.Value.pkg, problem = $"{part.Value.db}/{part.Value.pkg} does not hold the {info.project} sound bank" };
                    set.name = NameIndex(set.project) is int ni && ni >= 0 ? Names[ni].name : set.project;
                    sets.Add(set);
                }
                else if (set.problem != null && info.held != null)
                {   // a later package holds the project the first one only named
                    int i = sets.IndexOf(set);
                    info.held.aliveIds.AddRange(set.aliveIds); info.held.vehicles.AddRange(set.vehicles); info.held.shortNames.AddRange(set.shortNames);
                    info.held.name = set.name; sets[i] = set = info.held;
                }
                if (set.aliveIds.Contains(veh.id)) continue;
                set.aliveIds.Add(veh.id); set.vehicles.Add(veh.label); set.shortNames.Add(veh.shortName);
            }
            // the menus' order: the known sets by class (Names), then the others by project
            return sets.OrderBy(s => NameIndex(s.project) is int i && i >= 0 ? i : Names.Length).ThenBy(s => s.project, StringComparer.Ordinal).ToList();
        }

        /// The project a package's config names, and the set when the package holds it (its .sound, the bank the .sound names, the actions).
        static (string project, AliveEngineSoundSource held) Inspect(AliveBikes.PackageAssets pa, Volume volume)
        {
            string config = pa.Names("bxml").FirstOrDefault(n => n.StartsWith("AudioEngineConfig", StringComparison.OrdinalIgnoreCase));
            if (config == null) return (null, null);
            string project;
            try { project = Bxml.Parse(pa.Find(config, "bxml"), scene: true).ToElement().Get("sound"); }
            catch (AliveImportException) { return (null, null); }
            if (string.IsNullOrEmpty(project)) return (null, null);
            var sound = pa.Find(project, "sound");
            string actions = pa.Names("bxml").FirstOrDefault(n => n.StartsWith("EngineActions", StringComparison.OrdinalIgnoreCase));
            if (sound == null || actions == null) return (project, null);
            var s = new AliveEngineSoundSource { project = project, db = pa.db, pkg = pa.pkg, actions = actions, config = config,
                                                 source = volume is StfsVolume ? volume.displayName : "MX vs. ATV Alive (disc)" };
            try
            {
                var banks = BankNames(sound);
                if (banks.Count != 1) { s.problem = $"{project}: {banks.Count} sound banks (one expected)"; return (project, s); }
                s.bank = banks[0];
                var bank = pa.Find(s.bank, "soundbnk");
                if (bank == null) { s.problem = $"{pa.pkg}: no bank {s.bank}"; return (project, s); }
                var list = AliveFsb.Read(bank);
                s.samples = list.Count;
                foreach (var x in list) s.seconds += x.freq > 0 ? (double)x.samples / x.freq : 0;
                if (list.Any(x => !x.Xma)) s.problem = $"{s.bank}: a sample is not XMA";
            }
            catch (Exception e) when (e is AliveImportException || e is InvalidDataException || e is ArgumentException || e is IndexOutOfRangeException) { s.problem = project + ": " + e.Message; }
            return (project, s);
        }

        /// The bank names of a .sound asset (char[64] name, u32 count, count x char[64] "<bank>.soundbnk"), without their extension.
        static List<string> BankNames(byte[] sound)
        {
            Bin.Require(sound.Length >= 0x48, "Truncated .sound asset");
            int n = (int)Bin.U32LE(sound, 0x40);
            Bin.Require(n >= 0 && n <= 64 && 0x44 + 64L * n + 4 <= sound.Length, "Bad .sound bank count");
            var list = new List<string>();
            for (int i = 0; i < n; i++) list.Add(Stem(CString(sound, 0x44 + 64 * i, 64)));
            return list;
        }

        /// The FEV1 image of a .sound asset, as shipped.
        static byte[] FevOf(byte[] sound)
        {
            int n = (int)Bin.U32LE(sound, 0x40), at = 0x44 + 64 * n;
            Bin.Require(at + 8 <= sound.Length, "Truncated .sound asset");
            int size = (int)Bin.U32LE(sound, at);
            Bin.Require(size > 8 && at + 4 + (long)size <= sound.Length && sound[at + 4] == 'F' && sound[at + 5] == 'E' && sound[at + 6] == 'V' && sound[at + 7] == '1', "No FEV1 project in the .sound asset");
            return Bin.Slice(sound, at + 4, size);
        }

        static string CString(byte[] b, int at, int max)
        {
            int e = at; while (e < at + max && e < b.Length && b[e] != 0) e++;
            return Encoding.ASCII.GetString(b, at, e - at);
        }
        static string Stem(string n) { int i = n.LastIndexOf('.'); return i > 0 ? n.Substring(0, i) : n; }

        // ------------------------------------------------------------------------------------- converting a set

        /// Converts one set into `folder` (created; must not exist): the XMA samples decoded by `ffmpeg`, a scratch folder beside `folder`
        /// for the decoder's files (removed). `step` (worker thread) reports (samples done, samples). Returns the result (ok or why not).
        public static AliveEngineSoundResult Convert(AliveEngineSoundSource src, AliveSources sources, string folder, string ffmpeg, CancellationToken cancel, Action<int, int> step = null)
        {
            var res = new AliveEngineSoundResult { set = src, folder = folder };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { ConvertSet(src, sources, folder, ffmpeg, res, step, cancel); res.ok = true; }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is AliveImportException || e is InvalidDataException || e is IOException || e is ArgumentException || e is IndexOutOfRangeException
                                      || e is TimeoutException || e is UnauthorizedAccessException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            { res.error = e.Message; }
            res.seconds = clock.Elapsed.TotalSeconds;
            return res;
        }

        static void ConvertSet(AliveEngineSoundSource src, AliveSources sources, string folder, string ffmpeg, AliveEngineSoundResult res, Action<int, int> step, CancellationToken cancel)
        {
            Bin.Require(src.problem == null, src.problem ?? "");
            Bin.Require(!string.IsNullOrEmpty(ffmpeg) && File.Exists(ffmpeg), "ffmpeg was not found (it decodes Alive's XMA recordings): put an ffmpeg build in the ffmpeg folder beside the game, or set REFLEX_FFMPEG");
            var pa = new AliveBikes.PackageIndex(sources.volumes).Read(src.db, src.pkg, cancel) ?? throw new AliveImportException($"{src.db}/{src.pkg} is not in the sources");
            byte[] Need(string name, string type) => pa.Find(name, type) ?? throw new AliveImportException($"{src.pkg}: no {name} ({type})");
            var config = Need(src.config, "bxml"); var actions = Need(src.actions, "bxml"); var sound = Need(src.project, "sound"); var bank = Need(src.bank, "soundbnk");
            var fev = FevOf(sound);
            var samples = AliveFsb.Read(bank);
            Bin.Require(samples.Count > 0, src.bank + ": no samples");
            int fsbAt = Bin.IndexOf(bank, Encoding.ASCII.GetBytes("FSB4"), 0, Math.Min(bank.Length, 0x400));
            Bin.Require(fsbAt >= 0 && fsbAt + 48 <= bank.Length, src.bank + ": no FSB4 header");
            var configXml = Xml(config); var actionsXml = Xml(actions);

            Directory.CreateDirectory(folder);
            string scratch = folder.TrimEnd('/', '\\') + "-xma";
            Directory.CreateDirectory(scratch);
            try
            {
                var pcm = new List<short[]>(samples.Count);
                for (int i = 0; i < samples.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var s = samples[i];
                    Bin.Require(s.Xma, $"{src.bank}: sample {s.name} is not XMA");
                    Bin.Require(s.samples > 0 && s.samples <= int.MaxValue / 4, $"{src.bank}: sample {s.name} has {s.samples} samples");
                    var wave = AliveXma.Wave(bank, s.offset, s.length, s.samples, s.channels, s.freq);
                    var p = AliveFfmpeg.XmaToPcm(ffmpeg, wave, s.samples, scratch, cancel);
                    Bin.Require(p != null && p.LongLength == s.samples, $"{src.bank}: sample {s.name} decoded to {p?.LongLength ?? 0} of {s.samples} samples");
                    pcm.Add(p);
                    step?.Invoke(i + 1, samples.Count);
                }
                var fsb = BuildFsb(bank, fsbAt, samples, pcm, res);
                void Put(string file, byte[] data) { File.WriteAllBytes(Path.Combine(folder, file), data); res.bytes += data.Length; }
                Put(src.project + ".fev", fev);
                Put(src.project + ".fsb", fsb);
                Put("AudioEngineConfig.xml", configXml);
                Put("EngineActions.xml", actionsXml);
                var vehicles = new List<object>();
                for (int i = 0; i < src.aliveIds.Count; i++) vehicles.Add(new JObj().Set("alive_id", (long)src.aliveIds[i]).Set("name", i < src.vehicles.Count ? src.vehicles[i] : ""));
                var set = new JObj().Set("format", (long)Format).Set("project", src.project).Set("name", src.name).Set("actions", src.actions).Set("vehicles", vehicles).Set("source", src.source);
                if (TrimOf(src.project) is double trim) set.Set("trim", trim);
                Put("set.json", new UTF8Encoding(false).GetBytes(Json.Dumps(set, 1) + "\n"));
                res.samples = samples.Count;
            }
            finally { try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch (Exception) { } }
        }

        /// The bank rebuilt as PCM16 LE mono: the original's 48-byte header (its version and bank hash; header mode 0), per sample the fixed
        /// 80-byte header (size, name, length, PCM bytes, loop points, mode = M_16BITS | M_MONO | loop bits, rate, volume / pan / priority,
        /// channels 1, 3D distances) and the original SYNC block (the XMA seek block left out), then the data back to back (ReflexFmod.ParseFsb).
        static byte[] BuildFsb(byte[] bank, int fsbAt, List<FsbSample> samples, List<short[]> pcm, AliveEngineSoundResult res)
        {
            var headers = new List<byte[]>(samples.Count);
            long data = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i]; var h0 = s.header;
                Bin.Require(h0 != null && h0.Length >= 80, $"sample {s.name}: no sample header");
                var sync = SyncBlock(s);
                if (sync.Length >= 8) res.syncs += (int)Bin.U32LE(sync, 4);
                var h = new byte[80 + sync.Length];
                Buffer.BlockCopy(h0, 0, h, 0, 80);
                Buffer.BlockCopy(sync, 0, h, 80, sync.Length);
                Bin.Require(h.Length <= 0xFFFF, $"sample {s.name}: header too large");
                long bytes = (long)pcm[i].Length * 2;
                uint loop = s.mode & LoopBits;
                if (loop == 0) loop = s.loopStart == 0 && s.loopEnd == s.samples - 1 ? LoopNormal : LoopOff;
                Bin.PutU16LE(h, 0, (ushort)h.Length);
                Bin.PutU32LE(h, 32, (uint)pcm[i].Length);
                Bin.PutU32LE(h, 36, (uint)bytes);
                Bin.PutU32LE(h, 48, Pcm16 | Mono | loop);
                Bin.PutU16LE(h, 62, 1);
                if (Bin.U32LE(h0, 72) == h0.Length) Bin.PutU32LE(h, 72, (uint)h.Length);   // (Alive's headers repeat their size at +72)
                headers.Add(h);
                data += bytes;
            }
            long shdr = headers.Sum(x => (long)x.Length);
            Bin.Require(48 + shdr + data <= int.MaxValue, "bank too large");
            var o = new byte[48 + shdr + data];
            Buffer.BlockCopy(bank, fsbAt, o, 0, 48);
            Bin.PutU32LE(o, 4, (uint)samples.Count);
            Bin.PutU32LE(o, 8, (uint)shdr);
            Bin.PutU32LE(o, 12, (uint)data);
            Bin.PutU32LE(o, 20, 0);
            int at = 48;
            foreach (var h in headers) { Buffer.BlockCopy(h, 0, o, at, h.Length); at += h.Length; }
            foreach (var p in pcm)
                for (int k = 0; k < p.Length; k++) { short v = p[k]; o[at++] = (byte)v; o[at++] = (byte)(v >> 8); }
            return o;
        }

        /// The sample header's SYNC block as it is (u32 'SYNC', u32 count, count x {u32 sample, char[256] name}); built from the parsed sync
        /// points when the header has none in its bytes; empty without sync points.
        static byte[] SyncBlock(FsbSample s)
        {
            var h = s.header;
            int at = Bin.IndexOf(h, Encoding.ASCII.GetBytes("SYNC"), 80);
            if (at >= 0)
            {
                Bin.Require(at + 8 <= h.Length, $"sample {s.name}: truncated SYNC block");
                long n = Bin.U32LE(h, at + 4), len = 8 + n * 260;
                Bin.Require(at + len <= h.Length, $"sample {s.name}: truncated SYNC block");
                return Bin.Slice(h, at, (int)len);
            }
            if (s.syncs == null || s.syncs.Count == 0) return Array.Empty<byte>();
            var b = new byte[8 + s.syncs.Count * 260];
            Encoding.ASCII.GetBytes("SYNC", 0, 4, b, 0);
            Bin.PutU32LE(b, 4, (uint)s.syncs.Count);
            for (int i = 0; i < s.syncs.Count; i++)
            {
                Bin.PutU32LE(b, 8 + i * 260, (uint)s.syncs[i].sample);
                var nm = Encoding.ASCII.GetBytes(s.syncs[i].name ?? "");
                Buffer.BlockCopy(nm, 0, b, 12 + i * 260, Math.Min(nm.Length, 255));
            }
            return b;
        }

        /// A bxml document's element tree as indented XML (UTF-8, "\n" lines): the same element and attribute names, typed values as
        /// their invariant text ("1", "0.5", "true") without the element view's "_type:" prefix.
        internal static byte[] Xml(byte[] bxml)
        {
            var root = Bxml.Parse(bxml, scene: true).ToElement();
            var ms = new MemoryStream();
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\n", NewLineHandling = NewLineHandling.Replace, Encoding = new UTF8Encoding(false), CloseOutput = false };
            using (var w = XmlWriter.Create(ms, settings))
            {
                w.WriteStartDocument();
                WriteElement(w, root);
                w.WriteEndDocument();
            }
            ms.WriteByte((byte)'\n');
            return ms.ToArray();
        }

        static void WriteElement(XmlWriter w, Elem e)
        {
            w.WriteStartElement(e.tag);
            foreach (var kv in e.attrib)
            {
                if (kv.Key == "_valuetype") continue;
                string v = kv.Value.Text;
                if (kv.Value.kind != 1) { int c = v.IndexOf(':'); if (c >= 0) v = v.Substring(c + 1); }
                w.WriteAttributeString(kv.Key, v ?? "");
            }
            if (!string.IsNullOrEmpty(e.text)) w.WriteString(e.text);
            foreach (var c in e.children) WriteElement(w, c);
            w.WriteEndElement();
        }

        // ------------------------------------------------------------------------------------- installing

        /// Where the imported sets go: <content>/AliveEngineSounds (AliveEngineSets.Folder).
        public static string Root(string content) => Path.Combine(content, Folder);
        static string Manifest(string content, AliveEngineSoundSource s) => Path.Combine(Root(content), s.project + ".import.json");

        /// Whether the set was imported before (its folder and its import manifest exist).
        public static bool Installed(string content, AliveEngineSoundSource s)
        {
            try { return content != null && File.Exists(Manifest(content, s)) && Directory.Exists(Path.Combine(Root(content), s.project)); }
            catch (Exception) { return false; }
        }

        /// Why the set cannot be installed into `content` (null: it can): a folder of that name this importer did not make, a link.
        public static string InstallProblem(string content, AliveEngineSoundSource s)
        {
            try
            {
                string root = Root(content), dest = Path.Combine(root, s.project);
                if (Directory.Exists(root) && ReflexFs.IsLink(root)) return "A symbolic link is in the way: " + root;
                if (File.Exists(dest)) return "A file is in the way: " + dest;
                if (Directory.Exists(dest) && ReflexFs.IsLink(dest)) return "A symbolic link is in the way: " + dest;
                if (Directory.Exists(dest) && !File.Exists(Manifest(content, s))) return dest + " exists and was not made by this importer; remove it first";
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { return e.Message; }
        }

        /// Converts and installs the given sets into <content>/AliveEngineSounds/<PROJECT> (each staged, then moved in; a set imported before
        /// by this importer is replaced). A failed set leaves nothing behind. `progress` (worker thread): level = the project, stage
        /// "Decoding", done / total = its samples.
        public static List<AliveEngineSoundResult> Import(IEnumerable<AliveEngineSoundSource> sets, AliveSources sources, string content, string ffmpeg, Action<AliveProgress> progress, CancellationToken cancel)
        {
            content = Path.GetFullPath(content);
            string root = Root(content);
            Directory.CreateDirectory(root);
            Bin.Require(!ReflexFs.IsLink(root), "Would write through a symbolic link: " + root);
            var results = new List<AliveEngineSoundResult>();
            foreach (var s in sets)
            {
                if (cancel.IsCancellationRequested) break;
                progress?.Invoke(new AliveProgress { level = s.project, stage = "Decoding", done = 0, total = Math.Max(1, s.samples) });
                string stage = Path.Combine(root, ".ae-" + Guid.NewGuid().ToString("N").Substring(0, 8)), dest = Path.Combine(root, s.project);
                AliveEngineSoundResult res;
                try
                {
                    string why = InstallProblem(content, s);
                    Bin.Require(why == null, why ?? "");
                    res = Convert(s, sources, stage, ffmpeg, cancel, (d, n) => progress?.Invoke(new AliveProgress { level = s.project, stage = "Decoding", done = d, total = n }));
                    if (res.ok)
                    {
                        progress?.Invoke(new AliveProgress { level = s.project, stage = "Installing", done = 1, total = 1 });
                        string manifest = Manifest(content, s);
                        if (Directory.Exists(dest))
                        {
                            Bin.Require(File.Exists(manifest) && !ReflexFs.IsLink(dest), s.project + ": " + dest + " exists and was not made by this importer; remove it first");
                            Directory.Delete(dest, true);
                        }
                        Directory.Move(stage, dest);
                        res.folder = dest;
                        var files = Directory.GetFiles(dest, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                            .Select(f => (object)new JObj().Set("path", f.Substring(dest.Length + 1).Replace('\\', '/')).Set("sha256", LevelInstaller.Sha256(f))).ToList();
                        var m = new JObj().Set("version", 1L).Set("project", s.project).Set("name", s.name).Set("source", s.source)
                            .Set("alive_ids", s.aliveIds.Select(x => (object)(long)x).ToList())
                            .Set("imported_utc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Set("files", files);
                        File.WriteAllText(manifest, Json.Dumps(m, 1) + "\n", new UTF8Encoding(false));
                        s.installed = true;
                    }
                }
                catch (OperationCanceledException) { res = new AliveEngineSoundResult { set = s, error = "Cancelled" }; results.Add(res); break; }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is AliveImportException) { res = new AliveEngineSoundResult { set = s, error = e.Message }; }
                finally
                {
                    try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (Exception) { }
                    try { if (Directory.Exists(stage + "-xma")) Directory.Delete(stage + "-xma", true); } catch (Exception) { }
                }
                results.Add(res);
            }
            return results;
        }
    }
}
