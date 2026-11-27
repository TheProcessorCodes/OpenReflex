// The MX vs ATV Alive engine sets the player imported (GAME LIBRARY > MX vs ATV ALIVE > ALIVE ENGINE SOUNDS, AliveImport/AliveEngineSounds),
// as the race finds them: <install>/AliveEngineSounds/<PROJECT>/ (core/plan/ALIVE_ENGINE_SOUNDS.md section 4) holding
//   <PROJECT>.fev            the FMOD Designer project as Alive ships it (FEV1 v0x40: 25 events of group STATES, one per engine state)
//   <PROJECT>.fsb            its bank as FSB4 PCM16 mono 44.1 kHz, every sample header with the recording's SYNC points
//   AudioEngineConfig.xml    AudioEngineConfig_<size>.bxml as XML: <audio_engine state_group sound startEngineState> with one child per
//                            state naming its event ("STATES/IDLE"); notused="True" states have none
//   EngineActions.xml        EngineActions*.bxml as XML: <engineactionlists> of <engineactionlist name repeateveryframe="T|F"> trees of
//                            <engineaction actiontype statename|actionlistname|foleyname|message> with <tests> (all must pass),
//                            <testspassedactions>, <testsfailedactions>; a <test testtype datamember comparisontype comparisonvalue
//                            rangelow rangehigh seconds>
//   set.json                 {"format":1, "project", "name" ("450F"), "actions" (the actions file's stem), "vehicles":[{alive_id, name}],
//                            "source", optional "trim"}
// A set is listed only when all five files are there; the two XML files are read by AliveEngineMachine.Program (OriginalWorld/AliveEngineAudio.cs).
// Thread-safe (the lists are built under a lock and never changed afterwards).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Reflex.Port.Json;

namespace Reflex.Port.Content
{
    public sealed class AliveEngineSet
    {
        public string id;                   // "alive:<PROJECT>": the engine-sound choice (EngineSoundPacks)
        public string project, name, folder, actions, source;
        public int[] aliveIds;              // the Alive VehicleModel ids that use it
        public string[] vehicles;           // their names, as set.json gives them
        public float trim = 1f;             // the calibration trim (set.json "trim", default 1)
        public string Title => "ALIVE " + (string.IsNullOrEmpty(name) ? project : name).ToUpperInvariant();
        public string FevPath => Path.Combine(folder, project + ".fev");
        public string FsbPath => Path.Combine(folder, project + ".fsb");
        public string ConfigPath => Path.Combine(folder, AliveEngineSets.ConfigFile);
        public string ActionsPath => Path.Combine(folder, AliveEngineSets.ActionsFile);
    }

    public static class AliveEngineSets
    {
        public const string FolderName = "AliveEngineSounds", ConfigFile = "AudioEngineConfig.xml", ActionsFile = "EngineActions.xml", SetFile = "set.json";
        public const string IdPrefix = "alive:";
        static readonly object s_lock = new object();
        static List<AliveEngineSet> s_sets;
        /// Bumped by Refresh (callers that cache what they derived from All compare it).
        public static int Version { get; private set; }

        /// <install>/AliveEngineSounds, or null without an install.
        public static string Folder { get { string dir = ReflexInstall.Dir; return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FolderName); } }

        /// Every installed set, by project name (cached; Refresh rescans).
        public static IReadOnlyList<AliveEngineSet> All
        {
            get
            {
                lock (s_lock)
                {
                    if (s_sets != null) return s_sets;
                    var problems = new List<string>();
                    try { s_sets = Scan(Folder, problems); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { s_sets = new List<AliveEngineSet>(); problems.Add("scan: " + e.Message); }
#if UNITY_5_3_OR_NEWER
                    foreach (var s in s_sets) UnityEngine.Debug.Log($"[AliveEngineSets] {s.id}: {s.Title} ({s.aliveIds.Length} vehicles) in {s.folder}");
                    foreach (var p in problems) UnityEngine.Debug.LogWarning("[AliveEngineSets] " + p);
#endif
                    return s_sets;
                }
            }
        }
        public static void Refresh() { lock (s_lock) { s_sets = null; Version++; } }

        /// The set of a choice id ("alive:<PROJECT>", case-insensitive); null when it is not installed.
        public static AliveEngineSet Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// The set an Alive vehicle (its VehicleModel id) uses; null when none installed lists it.
        public static AliveEngineSet ForAliveVehicle(int aliveId)
        {
            foreach (var s in All) if (Array.IndexOf(s.aliveIds, aliveId) >= 0) return s;
            return null;
        }

        /// The sets in folder (each sub-folder with a valid set.json and its four files), sorted by project name.
        public static List<AliveEngineSet> Scan(string folder, List<string> problems = null)
        {
            var list = new List<AliveEngineSet>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;
            var dirs = Directory.GetDirectories(folder);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                string leaf = Path.GetFileName(dir);
                if (leaf.StartsWith(".", StringComparison.Ordinal)) continue;                   // a staging folder of an import
                try
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) { problems?.Add(leaf + ": a link, not read"); continue; }
                    var s = Read(dir);
                    if (list.Exists(o => string.Equals(o.project, s.project, StringComparison.OrdinalIgnoreCase))) { problems?.Add(leaf + ": project " + s.project + " is there twice"); continue; }
                    list.Add(s);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is FormatException || e is OverflowException) { problems?.Add(leaf + ": " + e.Message); }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.project, b.project));
            return list;
        }

        /// One set folder; InvalidDataException / FileNotFoundException when it is not a complete set.
        public static AliveEngineSet Read(string dir)
        {
            string json = Path.Combine(dir, SetFile);
            if (!File.Exists(json)) throw new FileNotFoundException("no " + SetFile);
            var j = J.Parse(File.ReadAllText(json, Encoding.UTF8));
            if (!j.IsObject || !j["format"].IsInteger || j["format"].Int != 1) throw new InvalidDataException(SetFile + ": \"format\" is not 1");
            string project = (j["project"].String ?? "").Trim();
            if (project.Length == 0) project = Path.GetFileName(dir);
            if (project.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || project.Contains("..")) throw new InvalidDataException(SetFile + ": bad project name " + project);
            var ids = new List<int>(); var names = new List<string>();
            foreach (var v in j["vehicles"])
            {
                if (!v.IsObject || !v["alive_id"].IsInteger) continue;
                ids.Add(v["alive_id"].Int); names.Add(v["name"].String ?? "");
            }
            var s = new AliveEngineSet
            {
                id = IdPrefix + project, project = project, name = (j["name"].String ?? "").Trim(), folder = dir, actions = j["actions"].String ?? "",
                source = j["source"].String ?? "", aliveIds = ids.ToArray(), vehicles = names.ToArray(),
                trim = j["trim"].IsFinite && j["trim"].Number > 0 ? (float)j["trim"].Number : 1f
            };
            foreach (string f in new[] { s.FevPath, s.FsbPath, s.ConfigPath, s.ActionsPath })
                if (!File.Exists(f)) throw new FileNotFoundException(Path.GetFileName(f) + " is missing");
            return s;
        }
    }
}
