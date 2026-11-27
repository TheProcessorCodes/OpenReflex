// MX vs ATV Unleashed -> Open Reflex level files: the orchestrator, port of tools/unleashed/import_library.py convert() (and of
// environment.py). One call converts one track of the player's Unleashed (data/<id>.pak, UI.pak, ...) into the files of a level of the
// game library, byte for byte what the Python pipeline writes:
//   Database/UNL_<id>.dx9.{level,database,scene}                       UnDescriptors (from the player's own Reflex library)
//   Assets/unleashed/UNL_<id>/terrain.rxheight, colour.rxcolour        UnTerrain
//   .../race.rxrace (when the course is a closed circuit)              UnRace
//   .../terrain-style.json, surface.rxsurface, ruts.json, preview.rximage, map.rximage    UnAppearance
//   .../level.json                                                     here (with the scenery side's report)
//   .../scenery.rxscenery, collision.rxcollision, vegetation, toys, start gate, funnel, stadium lights, Reflex donor scenery
//                                                                      the scenery side: UnConvert.Scenery (see the seam below)
// Nothing here uses the engine, and nothing is written: the caller gets the files and decides where they go.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Reflex.Port.Content.AliveImport;
using Reflex.Port.Content.UntamedImport;

namespace Reflex.Port.Content.UnleashedImport
{
    /// One row of tools/unleashed/tracks.json.
    public sealed class UnCatalogEntry
    {
        public string id, name, category;
        /// The key of its look in terrain_profiles.json.
        public string terrain_profile;
    }

    /// What the scenery side is given (the arguments of scenery.convert, startgate.write and stadium_lights.write).
    public sealed class UnSceneryCall
    {
        /// The folder holding <id>.pak, Env.pak, UI.pak ...; the player's Reflex library root.
        public string sourceDataDir, reflexRoot;
        /// "Nat04"; "UNL_Nat04".
        public string track, level;
        /// The terrain centre in source coordinates (level x = source x - centerX, level z = source z - centerZ) and max(extent).
        public double centerX, centerZ, extent;
        /// The track's .scn, latin-1 decoded.
        public string sceneText;
        /// The level's terrain.rxheight and race.rxrace (null: no race) as they will be written: the start gate is seated on them.
        public byte[] terrainHeight, race;
        /// The source terrain (heights, Sample(x, z) in source coordinates) and the parsed course (null: none).
        public UnTerrain terrain; public UnTdf course;
        /// fraction 0..1 of the scenery side's own work.
        public Action<string, float> progress;
        public CancellationToken ct;
    }

    /// What the scenery side returns.
    public sealed class UnSceneryFiles
    {
        /// File name inside Assets/unleashed/<level>/ ("scenery.rxscenery", "vegetation.json", "startgate.rxgate", ...) -> bytes.
        public Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        /// scenery.convert's report: level.json's "scenery" (its "materialRules" becomes "material_rules"). Keys in Python's order.
        public JObj report;
        /// startgate.write's report entry (position, direction, source), or null when the level has no start gate.
        public JObj startGate;
    }

    public static class UnConvert
    {
        public const string LevelPrefix = "UNL_";

        // ------------------------------------------------------------------------------------------------ THE SCENERY SEAM
        /// The scenery side of the conversion (UnScenery through UnSceneryHook: scenery.convert, startgate.write, stadium_lights.write).
        /// A test may replace it; null leaves the terrain side only ("scenery": null in level.json), which is not a complete level.
        public static Func<UnSceneryCall, UnSceneryFiles> Scenery = UnSceneryHook.Run;
        // -----------------------------------------------------------------------------------------------------------------

        static IReadOnlyList<UnCatalogEntry> s_catalog;

        /// tools/unleashed/tracks.json, in the file's order.
        public static IReadOnlyList<UnCatalogEntry> Catalog
        {
            get
            {
                var c = s_catalog;
                if (c != null) return c;
                var list = new List<UnCatalogEntry>();
                foreach (var row in UnJson.Arr(UnJson.Loads(UnData.Text(UnData.Tracks))))
                {
                    var o = UnJson.Obj(row);
                    list.Add(new UnCatalogEntry { id = o.Get("id") as string, name = o.Get("name") as string, category = o.Get("category") as string, terrain_profile = o.Get("terrain_profile") as string });
                }
                return s_catalog = list;
            }
        }

        /// The catalogue row of a track id, or null.
        public static UnCatalogEntry Find(string id)
        {
            foreach (var e in Catalog) if (e.id == id) return e;
            return null;
        }

        // environment.py
        static readonly HashSet<string> Night = new HashSet<string> { "Nat12", "Nat16", "SM01", "FS13" };
        static readonly HashSet<string> StadiumNight = new HashSet<string> { "SX05", "SX08", "SX12", "SX17", "FS02", "FS03" };
        static readonly HashSet<string> Indoor = new HashSet<string> { "SX03", "SX07", "SX10", "SX11", "SX14", "SX15", "SX16", "SX19", "FS04", "FS07", "FS08", "MC07" };
        static readonly HashSet<string> Dusk = new HashSet<string> { "SX01", "SX09", "FS05", "SM02", "Nat13", "MC03" };
        static readonly HashSet<string> Overcast = new HashSet<string> { "SX06", "SX20", "FS01", "FS10", "MC01", "Nat08", "Nat11" };

        /// environment.profile(track): mode, lighting / sky donor level, sky (day only).
        public static void Environment(string track, out string mode, out string donor, out string sky)
        {
            mode = Night.Contains(track) ? "night" : StadiumNight.Contains(track) ? "stadium_night" : Indoor.Contains(track) ? "indoor" : Dusk.Contains(track) ? "dusk" : "day";
            donor = mode == "night" || mode == "stadium_night" || mode == "indoor" ? "SX_KingstonB" : mode == "dusk" ? "FMX_Dover" : "NAT_Concord";
            sky = mode != "day" ? null : Overcast.Contains(track) ? "overcast" : "partly_cloudy";
        }

        /// The levels stadium_lights.write runs for (import_library.py's rule).
        public static bool HasStadiumLights(string track)
        {
            if (track.StartsWith("SX", StringComparison.Ordinal)) return true;
            for (int i = 1; i < 9; i++) if (track == "FS0" + i) return true;
            return track == "MC06" || track == "MC07" || track == "MC08";
        }

        static string Sha256File(string path, CancellationToken ct)
        {
            using (var sha = SHA256.Create())
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                var buf = new byte[1 << 20]; int n;
                while ((n = f.Read(buf, 0, buf.Length)) > 0) { ct.ThrowIfCancellationRequested(); sha.TransformBlock(buf, 0, n, null, 0); }
                sha.TransformFinalBlock(buf, 0, 0);
                var sb = new StringBuilder();
                foreach (byte b in sha.Hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// level.json (json.dumps(info, indent=2)). report / startGate: the scenery side's (null: see the seam).
        public static byte[] LevelJson(UnCatalogEntry entry, string sourceSha256, int spacing, bool race, string raceNote, JObj report, JObj startGate)
        {
            string track = entry.id;
            Environment(track, out string mode, out string donor, out string sky);
            var info = new JObj()
                .Set("race_type", race ? (track.StartsWith("SX", StringComparison.Ordinal) ? "Supercross" : "National") : null)
                .Set("race_note", raceNote)
                .Set("version", 1L)
                .Set("level", LevelPrefix + track)
                .Set("display_name", entry.name)
                .Set("kind", entry.category)
                .Set("source_id", track)
                .Set("source_sha256", sourceSha256)
                .Set("spacing", (long)spacing)
                .Set("scenery", report)
                .Set("material_rules", report?.Get("materialRules"))
                .Set("limitations", new List<object> { "Full-race validation covers representative circuits, not every venue", "Ecosystem collision and animated materials pending" })
                .Set("environment_mode", mode)
                .Set("environment_donor", donor)
                .Set("environment_sky", sky);
            if (startGate != null) info.Set("start_gate", startGate);
            return UnJson.DumpsBytes(info, 2);
        }

        /// sourceDataDir: the folder holding <id>.pak, Env.pak ... ; reflexRoot: the player's Reflex library root (holds Database/, Assets/
        /// or packed archives).
        /// Returns relative path ("Database/UNL_Nat04.dx9.level", "Assets/unleashed/UNL_Nat04/terrain.rxheight") -> bytes.
        /// progress(text, fraction 0..1) may be null. Throws on a source the Python tool fails on (UnValueError and others: the
        /// message says why); OperationCanceledException when cancelled.
        public static Dictionary<string, byte[]> ConvertLevel(string sourceDataDir, string reflexRoot, UnCatalogEntry entry, Action<string, float> progress, CancellationToken ct)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id)) throw new ArgumentException("no track");
            void Say(string text, float f) { ct.ThrowIfCancellationRequested(); progress?.Invoke(text, f); }
            string track = entry.id, level = LevelPrefix + track;
            string archive = Path.Combine(sourceDataDir, track + ".pak");
            string folder = "Assets/unleashed/" + level + "/";
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            Say("Reading " + entry.name, 0f);
            byte[] raw; UnTdf course; string scene;
            using (var p = UnPak.Open(archive))
            {
                raw = p.Read("Data/Env/" + track + "/" + track + ".dx.trn");
                string coursePath = "data\\env\\" + track + "\\" + track + ".tdf";
                course = p.Has(coursePath) ? UnTdf.Parse(p.Read(coursePath)) : null;
                byte[] scn = p.Read("Data/Env/" + track + "/" + track + ".scn");
                scene = UnPak.Latin1(scn, 0, scn.Length);

                Say("Terrain", 0.03f);
                var terrain = UnTerrain.Build(UnTrn.Parse(raw), ct);
                terrain.Start(course, scene, out double[] spawn, out double[] direction);
                byte[] heightFile = terrain.HeightFile(spawn, direction, ct);

                Say("Race line", 0.12f);
                byte[] race = UnRace.Assets(p, track, terrain, out string raceNote);

                // ---- the scenery side (agent B). Python runs scenery.convert here, and startgate.write / stadium_lights.write after
                // the level's own files are written; all three are behind the one seam.
                UnSceneryFiles scenery = null;
                if (Scenery != null)
                {
                    Say("Scenery", 0.15f);
                    scenery = Scenery(new UnSceneryCall
                    {
                        sourceDataDir = sourceDataDir, reflexRoot = reflexRoot, track = track, level = level,
                        centerX = terrain.centerX, centerZ = terrain.centerZ, extent = terrain.maxExtent, sceneText = scene,
                        terrainHeight = heightFile, race = race, terrain = terrain, course = course,
                        progress = progress == null ? null : (Action<string, float>)((text, f) => progress(text, 0.15f + 0.55f * Math.Max(0f, Math.Min(1f, f)))), ct = ct,
                    }) ?? throw new InvalidOperationException("The scenery converter returned nothing");
                }
                // ----

                Say("Ground colour", 0.70f);
                byte[] colour = terrain.Colour(raw, ct);

                Say("Level description", 0.80f);
                string sha = Sha256File(archive, ct);
                using (var vfs = new UxVfs(reflexRoot))
                    foreach (var kv in UnDescriptors.Build(rel => vfs.Exists(rel) ? vfs.ReadAllBytes(rel) : throw new FileNotFoundException("The Reflex game library has no " + rel + " (the Unleashed tracks are built on it)", rel), level))
                        files["Database/" + level + "." + kv.Key] = kv.Value;

                Say("Surface and pictures", 0.84f);
                string uiPath = Path.Combine(sourceDataDir, "UI.pak");
                using (var ui = UnPak.Open(uiPath))
                    foreach (var kv in UnAppearance.Build(entry, terrain, course, colour, ui, ct))
                        files[folder + kv.Key] = kv.Value;

                files[folder + "terrain.rxheight"] = heightFile;
                files[folder + "colour.rxcolour"] = UnTerrain.ColourFile(colour);
                if (race != null) files[folder + "race.rxrace"] = race;
                if (scenery != null)
                    foreach (var kv in scenery.files) files[folder + kv.Key] = kv.Value;
                files[folder + "level.json"] = LevelJson(entry, sha, terrain.spacing, race != null, raceNote, scenery?.report, scenery?.startGate);
            }
            Say("Converted " + entry.name, 1f);
            return files;
        }
    }
}
