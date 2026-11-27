// .rxmod bike mods (core/plan/RXMOD.md): a bike made in Blender, drawn over an original bike of the game whose physics it keeps 1:1.
//
// A .rxmod is a Reflex archive (RXAR, core/plan/RXARCHIVE.md, kind "MODS") - or, while a modder works on it, a plain folder - holding
//   mod.json         the manifest: name, author, description, thumbnail and its bikes; each bike names the original VehicleModelId it
//                    rides as (power, handling, price, ownership: that bike's), its brand / model lines, its mesh and its materials
//   *.rxmesh         a bike's mesh (RxMesh below): nodes named as the original MX_Hierarchy's (OBJ_Frame, OBJ_UPR_Forks, OBJ_LWR_Forks,
//                    OBJ_FRT_Tire, OBJ_Swingarm, OBJ_BCK_Tire, OBJ_Throttle, OBJ_ClutchLVR, OBJ_BrakeLVR) at the mod's own pivots, and
//                    triangle surfaces bound to them (rigid) or weighted over them (the rear shock between the frame and the swingarm)
//   *.png / *.jpg    the maps: a diffuse atlas, a spec map (gives the original programs' SFX map), a normal map (the tyre program's)
// Nothing of the original game is in a .rxmod. Each material borrows an original material of the bike it rides as (its pixel program, its
// constants and render states, the mud) with the mod's maps in place of the original's (ReflexBikeModel.RxMod.cs).
//
// A SKIN .rxmod ("type": "skins") holds no mesh: it paints a bike of another .rxmod (its "mod": that mod's "id" or name, its "bike": the
// bike's "id", model or VehicleModelId) by replacing map files ("maps": {"<the bike mod's entry>": "<the skin's entry>"}) and / or
// material maps ("materials"). Each skin is a MOD card of its own under the original the bike rides as (RxSkin, BikeMods.ResolveSkins).
// Install: <client root>/Mods/Bikes/ (as the classic bike mods, BikeMods.cs): a *.rxmod file, or a folder whose mod.json says
// "format": "reflex-mod", at any depth. BikeMods.Scan lists them next to the classic mods; the garage shows each bike as a MOD card under
// the original it rides as.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Reflex.Port.Json;

namespace Reflex.Port.Content
{
    /// A material of a .rxmod bike: the original material it borrows (template) and the mod's maps.
    /// A material's run-time colour (core/plan/RXMOD.md "Colours"): the player's choice of a colour slot ("color": its id, a parts mod's
    /// slot with "color") repaints the material's diffuse from a source map, as the importer baked the default colour ("default"):
    ///   bake "rim"    source R x (1 + B (0.92 c - 1))     Alive's Template_MX_Wheel_Fnl rims (AliveBikeMaps.BakeWheel)
    ///        "hub"    source R x c                         its hubs
    ///        "tint"   source R x c                         the ATV frames, swing arms, A-arms, rims and springs (BakeTint)
    ///        "plate"  diffuse + (c - default) x source R   the number plate of the plastics (source R: the plate's mask; BakePlastic)
    public sealed class RxTint { public string color = "", bake = "", source; public RxMod mod; public float[] rgb0 = { 0, 0, 0 }; }

    public sealed class RxModMaterial
    {
        public string name;
        /// The material's run-time colour and the colour worn (null: the default, the diffuse as it is).
        public RxTint tint; public float[] tintValue;
        /// plastic, frame, engine, wheel, tire, chain (the original programs a bike has: Template_Vehicle_Plstc_Fnl, Template_MX_Frame_FNL,
        /// Template_ATV_MX_Engine_FNL, Template_MX_Wheel_Fnl, Template_tire_FNL, Template_ATV_MX_AlphaMtl_UI_FNL).
        public string template = "plastic";
        public string diffuse, spec, normal, sfx;   // entries of the mod (sfx: an authored SFX map in place of the one made from spec)
        public float mud = 1f, shine = 1f;          // how much mud the surface takes (0..1); a scale of the spec map
        public bool twoSided, alphaTest; public int alphaRef = 128;
        public bool normalFlipY;                    // the normal map's green points down (DirectX) - the programs want it up
        /// "numbers": the rider's run-time number plate on the second UV set, as the original plastics draw it (the mesh's uv1 laid out as
        /// the original's "Numbers" plate: an imported Alive bike's plastics); false: clear (the plates are in the mod's diffuse).
        public bool numbers;
        /// The mod each map is read from: the bike's own mod, or a skin's (RxSkin.Material) for the maps the skin replaces.
        public RxMod diffuseMod, specMod, normalMod, sfxMod;
        public string Key => string.Join("|", template, Src(diffuseMod, diffuse), Src(specMod, spec), Src(normalMod, normal), Src(sfxMod, sfx), mud, shine, twoSided, alphaTest, alphaRef, normalFlipY, numbers);
        static string Src(RxMod m, string e) => e == null ? "-" : (m?.hash ?? "?") + "/" + e;
        public RxModMaterial Clone() => (RxModMaterial)MemberwiseClone();
    }

    /// A skin of a .rxmod bike (a "skins" .rxmod): the maps it replaces, by the bike mod's entry name or per material.
    public sealed class RxSkin
    {
        public RxMod mod; public int index;
        public string targetMod = "", targetBike = "", name = "";
        /// An ORIGINAL bike's skin ("original": its VehicleModelId; -1: a .rxmod bike's): its maps replace the graphics kit's textures by role
        /// ("plastics", "seat", "frame", "engine", "rims", "tires") or by texture name (RXMOD.md "Skins of the original bikes").
        public int original = -1;
        public string logo;   // a path on disk or null
        /// "<the bike mod's map entry>" -> "<this skin's entry>": every material of the bike that uses that map takes the skin's.
        public readonly Dictionary<string, string> maps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// Per material of the bike: the maps (and mud / shine / template) given here replace the bike's; what is not given stays the bike's.
        public readonly Dictionary<string, (string diffuse, string spec, string normal, string sfx, string template, float? mud, float? shine)> materials =
            new Dictionary<string, (string, string, string, string, string, float?, float?)>(StringComparer.OrdinalIgnoreCase);
        /// The bike it paints (BikeMods.ResolveSkins; null: that bike mod is not installed).
        public RxModBike target;
        public string Key => mod.hash + "#s" + index;
        /// The skin by content ("s:<12 hex of the mod's content>:<index>"; the showcase tests log it).
        public string Token => "s:" + mod.hash + ":" + index;
        public string Title => string.IsNullOrEmpty(name) ? mod.name : name;
        /// Whether it paints this vehicle model (an original's skin, or a .rxmod bike's riding as it).
        public bool For(int vehicleModelId) => vehicleModelId >= 0 && (original == vehicleModelId || target?.vehicleModelId == vehicleModelId);

        /// The skin's map for an original material's diffuse texture, null: the kit's own. By the texture's name (HR_ ignored) first, then by
        /// the role of the MATERIAL (its name: MX_Seat_* the seat - whose texture is the full-size bike's plastics atlas -, MX_Plstc_* the
        /// plastics, *Frame*, *_Eng*, *Rim*, *Tire*).
        public string OriginalMap(string texture, string material)
        {
            if (original < 0 || string.IsNullOrEmpty(texture)) return null;
            string t = texture.StartsWith("HR_", StringComparison.OrdinalIgnoreCase) ? texture.Substring(3) : texture;
            if (maps.TryGetValue(t, out var e) || maps.TryGetValue(texture, out e)) return e;
            string n = material ?? "";
            string role = n.IndexOf("Seat", StringComparison.OrdinalIgnoreCase) >= 0 ? "seat" : n.IndexOf("Plstc", StringComparison.OrdinalIgnoreCase) >= 0 ? "plastics"
                        : n.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0 ? "frame" : n.IndexOf("_Eng", StringComparison.OrdinalIgnoreCase) >= 0 ? "engine"
                        : n.IndexOf("Rim", StringComparison.OrdinalIgnoreCase) >= 0 ? "rims" : n.IndexOf("Tire", StringComparison.OrdinalIgnoreCase) >= 0 ? "tires" : null;
            return role != null && maps.TryGetValue(role, out e) ? e : null;
        }

        /// The bike's material as this skin paints it.
        public RxModMaterial Material(RxModMaterial b)
        {
            var m = b.Clone();
            string Map(string entry, RxMod from, out RxMod src) { src = from; if (entry != null && maps.TryGetValue(entry, out var e) && mod.Exists(e)) { src = mod; return e; } return entry; }
            m.diffuse = Map(b.diffuse, b.diffuseMod, out m.diffuseMod); m.spec = Map(b.spec, b.specMod, out m.specMod);
            m.normal = Map(b.normal, b.normalMod, out m.normalMod); m.sfx = Map(b.sfx, b.sfxMod, out m.sfxMod);
            if (materials.TryGetValue(b.name, out var o))
            {
                if (o.diffuse != null) { m.diffuse = o.diffuse; m.diffuseMod = mod; }
                if (o.spec != null) { m.spec = o.spec; m.specMod = mod; }
                if (o.normal != null) { m.normal = o.normal; m.normalMod = mod; }
                if (o.sfx != null) { m.sfx = o.sfx; m.sfxMod = mod; }
                if (o.template != null && Array.IndexOf(RxModBikes.Templates, o.template) >= 0) m.template = o.template;
                if (o.mud != null) m.mud = o.mud.Value; if (o.shine != null) m.shine = o.shine.Value;
            }
            return m;
        }
    }

    /// One bike of a .rxmod.
    public sealed class RxModBike
    {
        public RxMod mod; public int index;
        /// The original bike it rides as: its VehicleModelId (the garage's bikes; core/plan/RXMOD.md lists them).
        public int vehicleModelId = -1;
        public string original = "", mesh, id = "";   // id: the bike's own id in its mod (skins name it), optional
        public BikeName name;
        public string logo;   // a path on disk (extracted from an archive) or null
        public readonly Dictionary<string, RxModMaterial> materials = new Dictionary<string, RxModMaterial>(StringComparer.OrdinalIgnoreCase);
        /// "keep" (the mod's own size, aligned on the rear axle and the axle line) or "axles" (scaled so both axles sit on the original's).
        public string fit = "axles";
        /// ALIVE CLASSES (core/plan/ALIVE_BIKES.md): "physics": {"file": <a parameter file of the mx*_vehicle schema in the mod>, "class": "mx50"
        /// / "mx650f"}: the bike rides on its own physics (the file over the original's class file, for the local rider only:
        /// rx_session_vehicle_physics) instead of the original's 1:1. Only an imported MX vs ATV Alive bike has one (the importer writes
        /// Alive's own file from the player's install); physicsClass the core's class id (9, 10), -1 none.
        public string physicsFile, physicsNodes; public int physicsClass = -1;
        /// "class": the garage's class header of the bike ("50CC", "650"); null: the original's class.
        public string garageClass;
        public static readonly string[] PhysicsClasses = { "mx50", "mx650f", "atv1000" };
        public const int PhysicsClassFirst = 9;
        /// "pose": "physics": the parts placed by the simulation's bodies on the mod's own hierarchy (its nodes' rest pose), not by the
        /// original's hierarchy (a bike of another size: the Alive 50cc); "fit" is then "keep".
        public bool physicsPose;
        /// "fit": "nodes": the mod's parts bound to its own node rest pose, unscaled, posed by the original's hierarchy (the mod's nodes are the
        /// original's: an imported Alive ATV over a Reflex ATV, ATV_Hierarchy node for node). Also true with physicsPose.
        public bool OwnNodes => physicsPose || fit == "nodes";
        /// "riderOffset": [x, y, z] (ft, the bike frame's axes; with "pose": "physics" only, each within 1 ft): the drawn rider moved by it. The
        /// core seats its rider as on the original bike; a bike of another size has its seat, bars and pegs elsewhere (the Alive 50cc's
        /// seat 0.3 ft lower: ALIVE_BIKES.md "Rider"). The hands and feet still reach the bike's own grips and pegs (RiderLimbRetarget).
        public Vector3 riderOffset;
        /// The bike's own parts (Content/RxParts.cs): its "parts" slots and those of the installed "parts" .rxmods that name it
        /// (BikeMods.ResolveParts). Only these are listed on the bike; empty: it has none.
        public readonly List<RxPartSlot> slots = new List<RxPartSlot>();
        /// The bike's high-res twin (an "hr" .rxmod that names it: the imported MX vs ATV Alive bikes' garage models, AliveBikeHr.cs), with
        /// the high-res twins of its parts as its slots; null: none. Drawn where the game draws its own HR twins (the garage, the menus).
        public RxModBike hr;
        /// An imported MX vs ATV Alive bike whose kits and parts were made by an older importer than this game's (it has no high-res garage
        /// model and no colour slots until it is imported again with its source: the disc for Alive's own bikes, the DLC folder for the others).
        public bool importOutdated;
        /// The run-time colours of the bike's own materials, by material name (a parts mod's "tints": BikeMods.ResolveParts).
        public readonly Dictionary<string, RxTint> tints = new Dictionary<string, RxTint>(StringComparer.OrdinalIgnoreCase);
        /// SHARED BIKES (core/plan/RXMOD.md "Shared bikes"): "share" the model's key (its geometry, UV layout, which part each piece goes on
        /// and its material names; no map, no fit: "sb1-<16 hex>"), null: none. Bikes of several mods with the same share and VehicleModelId
        /// are one bike: one card, its GRAPHIC KIT slot holding every mod's kits (BikeMods.ResolveShared).
        public string share;
        /// "kit" / "kitBrand": the name and brand of the bike's own graphics kit (the default of a shared GRAPHIC KIT); null: not given.
        public string kitName, kitBrand;
        /// "kitMaterials": the materials a kit repaints; null: every material name the joined bikes have in common.
        public string[] kitMaterials;
        /// The bike this one was joined into (its kits are listed there; it has no card of its own); null: not joined.
        public RxModBike sharedInto;
        /// The authors of the mods whose bikes were joined into this one (the card's BY line), its own mod's not included.
        public readonly List<string> sharedAuthors = new List<string>();
        /// This bike, or the one it was joined into.
        public RxModBike Shown => sharedInto ?? this;
        /// A further bike of its mod on the same VehicleModelId (its own card, BikeMods.SplitBikes): its key in the mod (its "id", else its
        /// index), the card's id "<mod id>#<variant>"; null: the mod's first bike of its model (the mod's own card).
        public string variant;
        public string Key => mod.hash + "#" + index;
        /// The bike by content ("b:<12 hex of the mod's content>:<VehicleModelId>"; the showcase tests log it); a further bike of the same
        /// model (variant) has its own 12 hex in place of the mod's, the first 12 of SHA-256("<mod hash>#<variant>").
        public string Token => "b:" + (variant == null ? mod.hash : VariantHash()) + ":" + vehicleModelId;
        string VariantHash()
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(mod.hash + "#" + variant)), 0, 6).Replace("-", "").ToLowerInvariant();
        }
    }

    /// A .rxmod (an archive or a folder) and what its manifest says.
    public sealed class RxMod
    {
        public string path;          // the .rxmod file or the folder
        public bool archive;
        IntPtr handle;
        readonly object gate = new object();
        public string hash = "";     // 12 hex: SHA-256 of the manifest and every entry's path, size and CRC
        public string name = "", author = "", description = "", thumbnail;   // thumbnail: a path on disk or null
        /// "importer": the version of the importer that made the mod (the MX vs ATV Alive importer's parts folders: AliveBikes.ImportVersion); 0: none.
        public int importer;
        public string id = "", type = "bikes";   // id: the mod's own stable id (skins name it), optional; type: bikes or skins
        public readonly List<RxModBike> bikes = new List<RxModBike>();
        public readonly List<RxSkin> skins = new List<RxSkin>();
        /// A "parts" .rxmod: the slots it has for bikes of other .rxmods (Content/RxParts.cs).
        public readonly List<RxPartSet> partSets = new List<RxPartSet>();
        /// A "gear" .rxmod: its rider gear pieces (Content/RxGear.cs).
        public readonly List<RxGearPiece> gear = new List<RxGearPiece>();
        /// An "hr" .rxmod (a folder): the bike it is the high-res twin of ("mod" / "bike", as a parts mod names its bike); its "bike" folder is
        /// a bikes .rxmod of that one bike, its "parts" folder (optional) a parts .rxmod of its kits and parts (BikeMods.ResolveHr).
        public string hrTargetMod = "", hrTargetBike = "";
        public readonly List<string> entries = new List<string>();
        public string error;

        public bool Exists(string entry) => entry != null && entries.Any(e => string.Equals(e, Norm(entry), StringComparison.OrdinalIgnoreCase));
        static string Norm(string e) => (e ?? "").Replace('\\', '/').TrimStart('/');

        /// An entry's bytes (thread-safe); throws FileNotFoundException / IOException.
        public byte[] Read(string entry)
        {
            entry = Norm(entry);
            if (entry.Length == 0 || entry.Split('/').Any(p => p == ".." || p == ".")) throw new FileNotFoundException("bad entry name " + entry);
            if (!archive)
            {
                string f = Path.Combine(path, entry.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(f)) throw new FileNotFoundException(entry + " is not in " + path);
                return File.ReadAllBytes(f);
            }
            lock (gate)
            {
                if (handle == IntPtr.Zero) handle = Reflex.Port.RxCore.RxArchiveNative.Open(path);
                if (handle == IntPtr.Zero) throw new IOException(path + ": " + Reflex.Port.RxCore.RxArchiveNative.LastError());
                int i = Reflex.Port.RxCore.RxArchiveNative.Find(handle, entry);
                if (i < 0) throw new FileNotFoundException(entry + " is not in " + Path.GetFileName(path));
                long n = Reflex.Port.RxCore.RxArchiveNative.Size(handle, i);
                if (n < 0 || n > 512L << 20) throw new IOException(entry + ": size " + n);
                var b = new byte[n];
                if (!Reflex.Port.RxCore.RxArchiveNative.Read(handle, i, 0, b, 0, (int)n)) throw new IOException(entry + ": " + Reflex.Port.RxCore.RxArchiveNative.LastError());
                return b;
            }
        }

        public void Close() { lock (gate) { if (handle != IntPtr.Zero) Reflex.Port.RxCore.RxArchiveNative.Close(handle); handle = IntPtr.Zero; } }

        /// An entry as a file on disk (the menus load logos / thumbnails from paths): the folder's own file, or a copy extracted once into
        /// the cache (RxModBikes.CacheRoot/<hash>/). Null when it is missing.
        public string FileOf(string entry)
        {
            if (string.IsNullOrEmpty(entry) || !Exists(entry)) return null;
            if (!archive) return Path.Combine(path, Norm(entry).Replace('/', Path.DirectorySeparatorChar));
            try
            {
                string dir = Path.Combine(RxModBikes.CacheRoot, hash);
                string f = Path.Combine(dir, Norm(entry).Replace('/', '_'));
                if (!File.Exists(f)) { Directory.CreateDirectory(dir); File.WriteAllBytes(f + ".partial", Read(entry)); if (File.Exists(f)) File.Delete(f); File.Move(f + ".partial", f); }
                return f;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning("[RxMod] " + Path.GetFileName(path) + " " + entry + ": " + e.Message); return null; }
        }
    }

    /// A .rxmod bike's mesh (the .rxmesh entry), as read.
    internal sealed class RxMesh
    {
        public string[] nodes; public int[] parents; public Matrix4x4[] world;   // mod space: feet, X right, Y up, Z forward
        public string[] materials;
        public readonly List<(Rfx.Surface surface, int material, int node)> surfaces = new List<(Rfx.Surface, int, int)>();
        public int NodeIndex(string n) { for (int i = 0; i < nodes.Length; i++) if (string.Equals(nodes[i], n, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
        public int Vertices => surfaces.Sum(s => s.surface.positions.Length);
        public int Triangles => surfaces.Sum(s => s.surface.triangles.Length / 3);
    }

    public static class RxModBikes
    {
        public const string Ext = ".rxmod", MeshMagic = "RXMS", Format = "reflex-mod";
        public const uint Kind = 0x53444F4Du;   // "MODS" as a little-endian u32 (the archive header's kind)
        public static readonly string[] Templates = { "plastic", "frame", "engine", "wheel", "tire", "chain" };
        /// Where an archive's logos / thumbnails are extracted for the menus (any thread: no Unity path API).
        internal static string CacheRoot => Path.Combine(Path.GetTempPath(), "OpenReflex-rxmod");

        // ------------------------------------------------------------------------------------------------ open + manifest

        /// Whether a file / folder is a .rxmod (a *.rxmod file, or a folder with a reflex-mod mod.json).
        public static bool IsRxMod(string path)
        {
            if (File.Exists(path)) return path.EndsWith(Ext, StringComparison.OrdinalIgnoreCase);
            string j = Path.Combine(path, "mod.json");
            if (!File.Exists(j)) return false;
            try { var m = J.Parse(File.ReadAllText(j)); return m["format"].String == Format; } catch (Exception) { return false; }
        }

        const string KitPackNote = "a kit pack (bike graphics kits and rider gear): read by the kit packs, not as a bike mod";
        /// Every .rxmod under a folder (MODS ANYWHERE, ReflexMods.cs): the *.rxmod files and the reflex-mod folders (not entered), junk skipped.
        public static List<string> Find(string root, int maxDepth = 10)
        {
            var found = new List<string>();
            void Walk(string dir, int depth)
            {
                string[] files, dirs;
                try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return; }
                if (depth > 0 && files.Any(f => string.Equals(Path.GetFileName(f), "mod.json", StringComparison.OrdinalIgnoreCase)) && IsRxMod(dir)) { found.Add(dir); return; }
                foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) if (f.EndsWith(Ext, StringComparison.OrdinalIgnoreCase) && !ReflexMods.IsJunk(Path.GetFileName(f))) found.Add(f);
                if (depth >= maxDepth) return;
                foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) if (!ReflexMods.IsJunk(Path.GetFileName(d))) Walk(d, depth + 1);
            }
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) Walk(root, 0);
            return found;
        }

        /// Opens a .rxmod and reads its manifest (never throws: a bad mod has .error set and no bikes).
        public static RxMod Open(string path)
        {
            var m = new RxMod { path = path, archive = File.Exists(path), name = Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\')) };
            try
            {
                if (m.archive)
                {
                    RxFiles.EnsureCore?.Invoke();   // (the archive reader is the core library's: loaded on first use, a ride need not have started)
                    var h = Reflex.Port.RxCore.RxArchiveNative.Open(path);
                    if (h == IntPtr.Zero) throw new IOException(Reflex.Port.RxCore.RxArchiveNative.LastError());
                    try
                    {
                        if (Reflex.Port.RxCore.RxArchiveNative.Kind(h) != Kind) throw new InvalidDataException("not a mod archive (kind " + Reflex.Port.RxCore.RxArchiveNative.Kind(h).ToString("x8") + ")");
                        Reflex.Port.RxCore.RxArchiveNative.List(h, out var paths, out var sizes, out _);
                        m.entries.AddRange(paths);
                        m.hash = Hash(m, paths.Select((p, i) => p + ":" + sizes[i] + ":" + EntryId(h, i)));
                    }
                    finally { Reflex.Port.RxCore.RxArchiveNative.Close(h); }
                }
                else
                {
                    foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(path.TrimEnd('/', '\\').Length + 1).Replace('\\', '/');
                        if (!ReflexMods.IsJunk(Path.GetFileName(f))) m.entries.Add(rel);
                    }
                    m.entries.Sort(StringComparer.Ordinal);
                    m.hash = Hash(m, m.entries.Select(e => e + ":" + new FileInfo(Path.Combine(path, e)).Length));
                }
                ReadManifest(m, Encoding.UTF8.GetString(m.Read("mod.json")));
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is FormatException || e is UnauthorizedAccessException || e is EntryPointNotFoundException || e is DllNotFoundException)
            {
                m.error = e.Message; m.bikes.Clear(); m.skins.Clear(); m.partSets.Clear(); m.gear.Clear();
                if (e.Message != KitPackNote) Debug.LogWarning("[RxMod] " + path + ": " + e.Message);   // (a kit pack is KitPacks' to read, wherever it lies: nothing wrong with it)
            }
            return m;
        }

        static string EntryId(IntPtr h, int i) => Reflex.Port.RxCore.RxArchiveNative.EntrySha(h, i, out var hex) != 0 ? hex : "";
        static string Hash(RxMod m, IEnumerable<string> lines)
        {
            using var sha = SHA256.Create();
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
            return BitConverter.ToString(b, 0, 6).Replace("-", "").ToLowerInvariant();
        }

        /// mod.json (core/plan/RXMOD.md "Manifest").
        internal static void ReadManifest(RxMod m, string text)
        {
            var j = J.Parse(text);
            if (!j.IsObject || j["format"].String != Format) throw new InvalidDataException("mod.json: \"format\" is not \"" + Format + "\"");
            if (j["version"].IsNumber && j["version"].Int > 1) throw new InvalidDataException("mod.json: version " + j["version"].Int + " is newer than this game reads");
            string type = j["type"].String ?? "bikes";
            if (type == "kits") throw new InvalidDataException(KitPackNote);
            if (type != "bikes" && type != "bike" && type != "skins" && type != "skin" && type != "parts" && type != "gear" && type != "hr") throw new InvalidDataException("mod.json: type \"" + type + "\" (this game reads bike, skin, parts and gear mods)");
            if (!string.IsNullOrWhiteSpace(j["name"].String)) m.name = j["name"].String.Trim();
            m.author = j["author"].String?.Trim() ?? ""; m.description = j["description"].String?.Trim() ?? "";
            m.id = j["id"].String?.Trim() ?? "";
            if (j["importer"].IsInteger) m.importer = j["importer"].Int;
            if (!string.IsNullOrWhiteSpace(j["thumbnail"].String)) m.thumbnail = m.FileOf(j["thumbnail"].String.Trim());
            if (type.StartsWith("skin", StringComparison.Ordinal)) { m.type = "skins"; ReadSkins(m, j); return; }
            if (type == "parts") { m.type = "parts"; RxParts.ReadSets(m, j); return; }
            if (type == "hr")
            {   // a bike's high-res twin (core/plan/RXMOD.md "High-res twin")
                m.type = "hr"; m.hrTargetMod = j["mod"].String?.Trim() ?? ""; m.hrTargetBike = (j["bike"].IsNumber ? j["bike"].Int.ToString() : j["bike"].String ?? "").Trim();
                if (m.archive || m.hrTargetMod.Length == 0 || !m.Exists("bike/mod.json")) throw new InvalidDataException("mod.json: an \"hr\" mod is a folder that names its bike (\"mod\") and holds it in \"bike\"");
                return;
            }
            if (type == "gear") { m.type = "gear"; RxGear.Read(m, j); return; }   // (rider gear: Content/RxGear.cs, scanned from <content>/AliveGear)   // (the parts of another .rxmod's bike: Content/RxParts.cs)
            var bikes = j["bikes"]; if (!bikes.IsArray) throw new InvalidDataException("mod.json: no \"bikes\" list");
            for (int i = 0; i < bikes.Count; i++)
            {
                var o = bikes[i]; if (!o.IsObject) continue;
                var b = new RxModBike { mod = m, index = i, mesh = o["mesh"].String, original = o["original"].String ?? "", fit = o["fit"].String ?? "axles", id = o["id"].String?.Trim() ?? "" };
                b.vehicleModelId = o["bike"].IsInteger ? o["bike"].Int : int.TryParse(o["bike"].String, out int id) ? id : -1;
                var ph = o["physics"];
                if (ph.IsObject)
                {
                    int ci = Array.IndexOf(RxModBike.PhysicsClasses, (ph["class"].String ?? "").ToLowerInvariant());
                    if (ci >= 0 && m.Exists(ph["file"].String))
                    {
                        b.physicsFile = ph["file"].String; b.physicsClass = RxModBike.PhysicsClassFirst + ci;
                        if (m.Exists(ph["nodes"].String)) b.physicsNodes = ph["nodes"].String;
                    }
                    else Debug.LogWarning($"[RxMod] {m.name}: bike {i}: physics {ph["class"].String} / {ph["file"].String} not read (the original's physics)");
                }
                b.physicsPose = string.Equals(o["pose"].String, "physics", StringComparison.OrdinalIgnoreCase) && b.physicsNodes != null;   // (only with the core's own nodes)
                if (!string.IsNullOrWhiteSpace(o["class"].String)) b.garageClass = o["class"].String.Trim().ToUpperInvariant();
                if (b.physicsPose) b.fit = "keep";
                var ro = o["riderOffset"];
                if (b.physicsPose && ro.IsArray)
                {
                    var v = ro.Select(x => x.IsNumber ? (float)x.Number : float.NaN).ToArray();
                    if (v.Length == 3 && v.All(x => Mathf.Abs(x) <= 1f)) b.riderOffset = new Vector3(v[0], v[1], v[2]);
                    else Debug.LogWarning($"[RxMod] {m.name}: bike {i}: riderOffset is not three numbers within 1 ft (not moved)");
                }
                if (b.vehicleModelId < 0) { Debug.LogWarning($"[RxMod] {m.name}: bike {i} names no original bike (\"bike\": its VehicleModelId)"); continue; }
                if (string.IsNullOrEmpty(b.mesh) || !m.Exists(b.mesh)) { Debug.LogWarning($"[RxMod] {m.name}: bike {i}: mesh {b.mesh} is not in the mod"); continue; }
                b.name = !string.IsNullOrWhiteSpace(o["brand"].String) || !string.IsNullOrWhiteSpace(o["model"].String)
                    ? new BikeName { brand = o["brand"].String?.Trim() ?? "", model = o["model"].String?.Trim() ?? "" }
                    : BikeName.Parse(o["name"].String);
                if (!string.IsNullOrWhiteSpace(o["logo"].String)) b.logo = m.FileOf(o["logo"].String.Trim());
                // a shared bike's key and its own kit (core/plan/RXMOD.md "Shared bikes"; older mods have none: the bike stands alone)
                if (!string.IsNullOrWhiteSpace(o["share"].String)) b.share = o["share"].String.Trim();
                if (!string.IsNullOrWhiteSpace(o["kit"].String)) b.kitName = o["kit"].String.Trim();
                if (!string.IsNullOrWhiteSpace(o["kitBrand"].String)) b.kitBrand = o["kitBrand"].String.Trim();
                if (o["kitMaterials"].IsArray) b.kitMaterials = o["kitMaterials"].Select(x => x.String?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var mats = o["materials"];
                if (mats.IsObject)
                    foreach (string k in mats.Keys)
                    {
                        var x = mats[k]; if (!x.IsObject) continue;
                        var mm = new RxModMaterial
                        {
                            name = k, template = (x["template"].String ?? "plastic").ToLowerInvariant(), diffuse = x["diffuse"].String, spec = x["spec"].String,
                            normal = x["normal"].String, sfx = x["sfx"].String, twoSided = x["twoSided"].Boolean,
                            alphaTest = x["alphaTest"].Boolean, normalFlipY = x["normalFlipY"].Boolean, numbers = x["numbers"].Boolean,
                            diffuseMod = m, specMod = m, normalMod = m, sfxMod = m,
                        };
                        if (x["mud"].IsNumber) mm.mud = Mathf.Clamp01((float)x["mud"].Number);
                        if (x["shine"].IsNumber) mm.shine = Mathf.Clamp((float)x["shine"].Number, 0f, 4f);
                        if (x["alphaRef"].IsInteger) mm.alphaRef = Mathf.Clamp(x["alphaRef"].Int, 1, 255);
                        if (Array.IndexOf(Templates, mm.template) < 0) { Debug.LogWarning($"[RxMod] {m.name}: material {k}: template {mm.template} unknown (plastic used)"); mm.template = "plastic"; }
                        b.materials[k] = mm;
                    }
                b.slots.AddRange(RxParts.ReadSlots(m, o["parts"], "bike " + i));   // (its own parts, if the mod carries them)
                m.bikes.Add(b);
            }
            if (m.bikes.Count == 0) throw new InvalidDataException("mod.json: no bike could be read");
        }

        /// A skins mod.json's "skins" (core/plan/RXMOD.md "Skins"): each {"mod", "bike" (a name or a list), "name", "maps", "materials", "logo"}.
        static void ReadSkins(RxMod m, J j)
        {
            var list = j["skins"]; if (!list.IsArray) throw new InvalidDataException("mod.json: no \"skins\" list");
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i]; if (!o.IsObject) continue;
                var targets = o["bike"].IsArray ? o["bike"].Select(x => x.IsNumber ? x.Int.ToString() : x.String).ToList() : new List<string> { o["bike"].IsNumber ? o["bike"].Int.ToString() : o["bike"].String ?? "" };
                foreach (var t in targets)
                {
                    var k = new RxSkin { mod = m, index = m.skins.Count, targetMod = o["mod"].String?.Trim() ?? "", targetBike = t?.Trim() ?? "", name = o["name"].String?.Trim() ?? "" };
                    if (o["original"].IsInteger) k.original = o["original"].Int; else if (int.TryParse(o["original"].String, out int ov)) k.original = ov;
                    if (k.targetMod.Length == 0 && k.original < 0) { Debug.LogWarning($"[RxMod] {m.name}: skin {i} names neither a bike mod (\"mod\") nor an original bike (\"original\": its VehicleModelId)"); continue; }
                    if (!string.IsNullOrWhiteSpace(o["logo"].String)) k.logo = m.FileOf(o["logo"].String.Trim());
                    var maps = o["maps"];
                    if (maps.IsObject) foreach (string key in maps.Keys)
                    {
                        string e = maps[key].String;
                        if (string.IsNullOrEmpty(e) || !m.Exists(e)) { Debug.LogWarning($"[RxMod] {m.name}: skin {i}: map {e} is not in the mod"); continue; }
                        k.maps[key.Replace('\\', '/').TrimStart('/')] = e;   // (an original's skin: a role or a texture name)
                    }
                    var mats = o["materials"];
                    if (mats.IsObject) foreach (string key in mats.Keys)
                    {
                        var x = mats[key]; if (!x.IsObject) continue;
                        string E(string f) { var v = x[f].String; if (v == null) return null; if (!m.Exists(v)) { Debug.LogWarning($"[RxMod] {m.name}: skin {i}: {f} {v} is not in the mod"); return null; } return v; }
                        k.materials[key] = (E("diffuse"), E("spec"), E("normal"), E("sfx"), x["template"].String?.ToLowerInvariant(),
                                            x["mud"].IsNumber ? Mathf.Clamp01((float)x["mud"].Number) : (float?)null, x["shine"].IsNumber ? Mathf.Clamp((float)x["shine"].Number, 0f, 4f) : (float?)null);
                    }
                    if (k.maps.Count == 0 && k.materials.Count == 0) { Debug.LogWarning($"[RxMod] {m.name}: skin {i} replaces no map"); continue; }
                    m.skins.Add(k);
                }
            }
            if (m.skins.Count == 0) throw new InvalidDataException("mod.json: no skin could be read");
        }

        /// Whether a skin's "mod" / "bike" name this bike: the mod's id or name; the bike's id, model ("YZ250F"), brand + model or VehicleModelId
        /// (an empty "bike": the mod's only bike). Case and spaces ignored.
        public static bool Paints(RxSkin k, RxModBike b) => k.original < 0 && Names(k.targetMod, k.targetBike, b);

        /// Whether a "mod" / "bike" pair (a skin's, a parts mod's) names this bike.
        public static bool Names(string targetMod, string targetBike, RxModBike b)
        {
            string N(string x) => new string((x ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (b?.mod == null || b.mod.type != "bikes") return false;
            string t = N(targetMod);
            if (t.Length == 0 || (t != N(b.mod.id) && t != N(b.mod.name))) return false;
            string w = N(targetBike);
            if (w.Length == 0) return b.mod.bikes.Count == 1;
            return w == N(b.id) || w == N(b.name?.model) || w == N(b.name?.Full) || w == b.vehicleModelId.ToString();
        }

        /// The material a bike's mesh names, as drawn (its skin's when it has one); null: not in the bike's mod.json.
        public static RxModMaterial MaterialOf(RxModBike b, RxSkin skin, string name)
        {
            if (b == null || !b.materials.TryGetValue(name, out var mm)) return null;
            return skin != null && skin.target == b ? skin.Material(mm) : mm;
        }

        // ------------------------------------------------------------------------------------------------ meshes

        static readonly Dictionary<string, RxMesh> meshes = new Dictionary<string, RxMesh>();
        public static void ReleaseParsed() { lock (meshes) meshes.Clear(); }

        /// A bike's mesh, read once (any thread).
        internal static RxMesh Mesh(RxModBike b)
        {
            string key = b.mod.hash + "/" + b.mesh;
            lock (meshes) if (meshes.TryGetValue(key, out var c)) return c;
            using var perf = Reflex.Port.Platform.ReflexMenuPerf.Measure("model read and parse");
            var m = ParseMesh(b.mod.Read(b.mesh));
            lock (meshes) meshes[key] = m;
            return m;
        }

        /// A part's mesh of a parts mod (Content/RxParts.cs), read once (any thread).
        internal static RxMesh Mesh(RxMod mod, string entry)
        {
            string key = mod.hash + "/" + entry;
            lock (meshes) if (meshes.TryGetValue(key, out var c)) return c;
            var m = ParseMesh(mod.Read(entry));
            lock (meshes) meshes[key] = m;
            return m;
        }

        /// The .rxmesh layout (little-endian; core/plan/RXMOD.md "Mesh"):
        ///   "RXMS" u32 version (1) u32 flags (0)
        ///   u32 nodes; each: str name, i32 parent (-1 root, else an earlier node), f32[16] world matrix (row-major, column vectors: the
        ///     translation is elements 3, 7, 11) in mod space (feet; X right, Y up, Z forward)
        ///   u32 materials; each: str name
        ///   u32 surfaces; each: u32 material, u32 node, u32 flags (1 skinned, 2 uv1, 4 tangents), u32 vertices V, u32 indices I,
        ///     f32[3V] positions, f32[3V] normals, f32[2V] uv0 (OpenGL / Blender: v up from the image's bottom), [f32[2V] uv1],
        ///     [f32[4V] tangents], [u8[4V] bone nodes (255 none), f32[4V] weights], u32[I] triangles (clockwise front faces)
        ///   str = u16 byte length + UTF-8
        internal static RxMesh ParseMesh(byte[] data)
        {
            using var r = new BinaryReader(new MemoryStream(data, false), Encoding.UTF8);
            void Need(bool ok, string what) { if (!ok) throw new InvalidDataException("rxmesh: " + what); }
            string Str() { int n = r.ReadUInt16(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
            Need(data.Length >= 16 && Encoding.ASCII.GetString(r.ReadBytes(4)) == MeshMagic, "magic");
            int version = r.ReadInt32(); Need(version == 1, "version " + version + " is newer than this game reads"); r.ReadInt32();
            var m = new RxMesh();
            int nodes = r.ReadInt32(); Need(nodes > 0 && nodes <= 256, "node count");
            m.nodes = new string[nodes]; m.parents = new int[nodes]; m.world = new Matrix4x4[nodes];
            for (int i = 0; i < nodes; i++)
            {
                m.nodes[i] = Str(); m.parents[i] = r.ReadInt32(); Need(m.parents[i] >= -1 && m.parents[i] < i, "node order");
                var w = new Matrix4x4(); for (int k = 0; k < 16; k++) w[k / 4, k % 4] = r.ReadSingle();
                m.world[i] = w;
            }
            int mats = r.ReadInt32(); Need(mats >= 0 && mats <= 256, "material count");
            m.materials = new string[mats]; for (int i = 0; i < mats; i++) m.materials[i] = Str();
            int surfs = r.ReadInt32(); Need(surfs >= 0 && surfs <= 4096, "surface count");
            float[] F(int n) { var a = new float[n]; var b = r.ReadBytes(n * 4); Need(b.Length == n * 4, "truncated"); Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }
            for (int s = 0; s < surfs; s++)
            {
                int mat = r.ReadInt32(), node = r.ReadInt32(), flags = r.ReadInt32(), v = r.ReadInt32(), ix = r.ReadInt32();
                Need(mat >= 0 && mat < mats && node >= 0 && node < nodes && v > 0 && v <= 4_000_000 && ix % 3 == 0 && ix > 0 && ix <= 12_000_000 && (flags & ~7) == 0, "surface " + s);
                var p = F(v * 3); var n = F(v * 3); var t0 = F(v * 2); var t1 = (flags & 2) != 0 ? F(v * 2) : null; var tg = (flags & 4) != 0 ? F(v * 4) : null;
                var sf = new Rfx.Surface { positions = new Vector3[v], normals = new Vector3[v], uv0 = new Vector2[v], uv1 = new Vector2[v], strip = new int[0] };
                for (int i = 0; i < v; i++)
                {
                    sf.positions[i] = new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]);
                    sf.normals[i] = new Vector3(n[i * 3], n[i * 3 + 1], n[i * 3 + 2]);
                    sf.uv0[i] = new Vector2(t0[i * 2], 1f - t0[i * 2 + 1]);   // (stored as the game's surfaces: D3D rows; ReflexMeshes.Build flips back)
                    if (t1 != null) sf.uv1[i] = new Vector2(t1[i * 2], 1f - t1[i * 2 + 1]);
                }
                if (tg != null)
                {
                    sf.tangents = new Vector3[v]; bool mirrored = false;
                    for (int i = 0; i < v; i++) { sf.tangents[i] = new Vector3(tg[i * 4], tg[i * 4 + 1], tg[i * 4 + 2]); mirrored |= tg[i * 4 + 3] < 0f; }
                    // the handedness (w -1: a mirrored UV island), kept where the mod has one; the game's own meshes have none (null: every w is 1)
                    if (mirrored) { sf.tangentW = new float[v]; for (int i = 0; i < v; i++) sf.tangentW[i] = tg[i * 4 + 3] < 0f ? -1f : 1f; }
                }
                if ((flags & 1) != 0)
                {
                    sf.skinned = true; sf.bones = new int[v * 4]; var bb = r.ReadBytes(v * 4); Need(bb.Length == v * 4, "truncated");
                    for (int i = 0; i < v * 4; i++) { sf.bones[i] = bb[i] == 255 ? -1 : bb[i]; Need(sf.bones[i] < nodes, "bone node"); }
                    sf.weights = F(v * 4);
                }
                var tri = new int[ix]; var ib = r.ReadBytes(ix * 4); Need(ib.Length == ix * 4, "truncated"); Buffer.BlockCopy(ib, 0, tri, 0, ib.Length);
                foreach (int k in tri) Need(k >= 0 && k < v, "index");
                sf.triangles = tri;
                m.surfaces.Add((sf, mat, node));
            }
            return m;
        }

        // ------------------------------------------------------------------------------------------------ maps

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        /// The maps that could not be read (a missing entry, not a picture): warned about once, by name.
        static readonly HashSet<string> mapFailed = new HashSet<string>();
        public static void ReleaseTextures() { foreach (var t in textures.Values) if (t) UnityEngine.Object.Destroy(t); textures.Clear(); pixels.Clear(); mapFailed.Clear(); }

        /// A map up to this many texels (2048 x 1024: every map of the imported MX vs ATV Alive bikes, parts and gear; 11 MB with its
        /// mips) is kept as the file has it, RGBA32; a larger colour map is block-compressed (DXT1, DXT5 with alpha: a quarter / an eighth
        /// of the memory) by RxBlocks, once per file (the result is kept in the cache folder).
        const int PlainTexels = 2048 * 1024;
        /// Tests: the maps as they were made before (Texture2D.Compress's fast DXT for every colour and SFX map and the gear's 8-bit normal
        /// maps, RGHalf normal maps, no TEXTURE FILTERING): REFLEX_RXMOD_LEGACY_MAPS=1, a before / after in one build.
        public static bool LegacyMaps = Environment.GetEnvironmentVariable("REFLEX_RXMOD_LEGACY_MAPS") == "1";
        /// Tests: REFLEX_RXMOD_MAP_LOG=1 logs every map made (size, format, bytes on the GPU, the time on the main thread).
        static readonly bool mapLog = Environment.GetEnvironmentVariable("REFLEX_RXMOD_MAP_LOG") == "1";
        static void MapMade(Texture2D t, string what, System.Diagnostics.Stopwatch clock, bool cached = false)
        {
            if (!mapLog || !t) return;
            long bytes = 0; for (int l = 0; l < t.mipmapCount; l++) bytes += UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1, t.width >> l), Math.Max(1, t.height >> l), t.graphicsFormat);
            Debug.Log($"[RxMod] map {what} {t.name}: {t.width} x {t.height} {t.graphicsFormat}, {t.mipmapCount} levels, {bytes} bytes, {clock.Elapsed.TotalMilliseconds:0.0} ms{(cached ? " (from the cache)" : "")}{(LegacyMaps ? " (legacy)" : "")}");
        }
        static void MapFailed(string key, RxMod m, string entry, string why)
        {
            if (mapFailed.Add(key)) Debug.LogWarning($"[RxMod] {m?.name}: map {entry}: {why} (its material is drawn without it)");
        }

        /// A map of the mod as the programs sample it: raw values (no sRGB), mipmapped (main thread). normal: an 8-bit tangent-space
        /// normal map the program reads as it is (the gear programs' other than the jersey's): never block-compressed; over 2048 px it is
        /// brought down by a box filter, its normals made unit length again.
        internal static Texture2D Map(RxMod m, string entry, bool normal = false)
        {
            bool data = normal;
            if (string.IsNullOrEmpty(entry) || m == null) return null;
            string key = m.hash + "/" + entry + (data && !LegacyMaps ? "/data" : "");
            if (textures.TryGetValue(key, out var c) && c) return c;
            if (mapFailed.Contains(key)) return null;
            try
            {
                using var perf = Reflex.Port.Platform.ReflexMenuPerf.Measure("rxmod texture");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var bytes = m.Read(entry);
                string cache = null;
                if (!LegacyMaps && !data)
                {   // (a large map: its blocks from the cache, without decoding the picture)
                    var (pw, ph) = PictureSize(bytes);
                    if ((long)pw * ph > PlainTexels && pw % 4 == 0 && ph % 4 == 0)
                    {
                        cache = RxBlocks.CacheFile(bytes);
                        var cached = RxBlocks.Read(cache, Path.GetFileName(entry));
                        if (cached) { Sampler(cached, 4); MapMade(cached, "colour", clock, true); return textures[key] = cached; }
                    }
                }
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, true) { name = Path.GetFileName(entry), hideFlags = HideFlags.DontSave };
                if (!t.LoadImage(bytes, false)) { UnityEngine.Object.Destroy(t); throw new InvalidDataException("not a PNG / JPG"); }
                if (LegacyMaps) { t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4; t.Compress(false); t.Apply(false, true); MapMade(t, "colour", clock); return textures[key] = t; }
                // block compression only for a large colour map whose size is a multiple of 4 (DXT's own rule; any other size stays RGBA32)
                if (!data && (long)t.width * t.height > PlainTexels && t.width % 4 == 0 && t.height % 4 == 0)
                {
                    var blocks = RxBlocks.Compress(t, cache);
                    if (blocks) { UnityEngine.Object.Destroy(t); t = blocks; } else t.Apply(false, true);
                }
                else if (normal && Math.Max(t.width, t.height) > 2048)
                {
                    int w = t.width >= t.height ? 2048 : Math.Max(1, t.width * 2048 / t.height), h = t.width >= t.height ? Math.Max(1, t.height * 2048 / t.width) : 2048;
                    var small = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = t.name, hideFlags = HideFlags.DontSave };
                    small.SetPixels32(Resample(t, w, h, true)); small.Apply(true, true);
                    UnityEngine.Object.Destroy(t); t = small;
                }
                else t.Apply(false, true);
                Sampler(t, 4);
                MapMade(t, data ? "normal (8-bit)" : "colour", clock);
                return textures[key] = t;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException) { MapFailed(key, m, entry, e.Message); return null; }
        }

        /// A material's "tint" object.
        internal static RxTint ReadTint(RxMod m, J j)
        {
            if (!j.IsObject) return null;
            string source = j["source"].String, color = j["color"].String, bake = (j["bake"].String ?? "").ToLowerInvariant();
            if (string.IsNullOrEmpty(color) || source == null || !m.Exists(source) || Array.IndexOf(new[] { "rim", "hub", "tint", "plate" }, bake) < 0) return null;
            var t = new RxTint { color = color, bake = bake, source = source, mod = m };
            var d = j["default"]; if (d.IsArray && d.Count >= 3 && d.All(x => x.IsFinite)) t.rgb0 = d.Take(3).Select(x => (float)x.Number).ToArray();
            return t;
        }

        /// The diffuse of a material in the colour worn (RxTint), null: it cannot be made (the material's own diffuse is drawn).
        internal static Texture2D Tinted(RxModMaterial mm)
        {
            var t = mm?.tint; var c = mm?.tintValue;
            if (t == null || c == null || c.Length < 3) return null;
            bool plate = t.bake == "plate";
            string key = t.mod.hash + "/tint/" + t.source + "/" + t.bake + "/" + c[0].ToString("R") + "," + c[1].ToString("R") + "," + c[2].ToString("R") + (plate ? "/" + mm.diffuseMod?.hash + "/" + mm.diffuse : "");
            if (textures.TryGetValue(key, out var made) && made) return made;
            if (mapFailed.Contains(key)) return null;
            Texture2D src = null, dif = null;
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                src = Load(t.mod, t.source); if (!src) return null;
                var sp = src.GetPixels32(); int sw = src.width, sh = src.height, w = sw, h = sh;
                Color32[] dp = null;
                if (plate)
                {
                    dif = string.IsNullOrEmpty(mm.diffuse) ? null : Load(mm.diffuseMod, mm.diffuse); if (!dif) return null;
                    dp = dif.GetPixels32(); w = dif.width; h = dif.height;
                }
                var o = new Color32[w * h];
                byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var s = sp[(int)((long)y * sh / h) * sw + (int)((long)x * sw / w)];
                        float r = s.r / 255f, b = s.b / 255f; int i = y * w + x;
                        if (plate) o[i] = new Color32(B(dp[i].r / 255f + (c[0] - t.rgb0[0]) * r), B(dp[i].g / 255f + (c[1] - t.rgb0[1]) * r), B(dp[i].b / 255f + (c[2] - t.rgb0[2]) * r), 255);
                        else if (t.bake == "rim") o[i] = new Color32(B(r * (1f + b * (c[0] * 0.92f - 1f))), B(r * (1f + b * (c[1] * 0.92f - 1f))), B(r * (1f + b * (c[2] * 0.92f - 1f))), 255);
                        else o[i] = new Color32(B(r * c[0]), B(r * c[1]), B(r * c[2]), 255);
                    }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = Path.GetFileName(t.source) + " " + t.color, hideFlags = HideFlags.DontSave };
                tex.SetPixels32(o); tex.Apply(true, true);
                Sampler(tex, 4);
                MapMade(tex, "colour (" + t.bake + ")", clock);
                return textures[key] = tex;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException) { MapFailed(key, t.mod, t.source, e.Message); return null; }
            finally { if (src) UnityEngine.Object.Destroy(src); if (dif) UnityEngine.Object.Destroy(dif); }
        }

        /// The maps' sampler state (repeat, trilinear; the colour maps 4x anisotropic, as they always were) and TEXTURE FILTERING from now on.
        static void Sampler(Texture2D t, int aniso)
        {
            t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = aniso;
            Rendering.ReflexTextureFiltering.Register(t);
        }

        /// A PNG's / JPEG's size from its header, (0, 0) when it cannot be told (the picture is then decoded to know).
        static (int w, int h) PictureSize(byte[] b)
        {
            if (b == null) return (0, 0);
            if (b.Length >= 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G')
                return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
            if (b.Length >= 4 && b[0] == 0xff && b[1] == 0xd8)
                for (int p = 2; p + 9 < b.Length;)
                {
                    if (b[p] != 0xff) return (0, 0);
                    int marker = b[p + 1], len = (b[p + 2] << 8) | b[p + 3];
                    if (marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc) return ((b[p + 7] << 8) | b[p + 8], (b[p + 5] << 8) | b[p + 6]);
                    p += 2 + len;
                }
            return (0, 0);
        }

        /// The original programs' SFX / combo map for a material, made from its spec map (core/plan/RXMOD.md "Materials"): R the shine the
        /// frame / engine / wheel programs blend towards (0 on plastics: there R lights the diffuse itself), G where mud may build up (mud x MudMask), B the
        /// reflection; the chain program's B is its alpha (the diffuse's). An authored "sfx" map is used as it is.
        /// G of the SFX map at "mud": 1 (the share of the surface a fully muddy bike has caked: mud = saturate(G + usr_mud_amount - 1)). The
        /// original plastics' mask is 0.03 at its median and 0.73 at its 90th percentile (their lower edges): half everywhere is the even middle.
        public const float MudMask = 0.5f;
        internal static Texture2D Sfx(RxModMaterial mat)
        {
            if (!string.IsNullOrEmpty(mat.sfx)) return Map(mat.sfxMod, mat.sfx);
            string key = "sfx/" + mat.template + "/" + mat.specMod?.hash + "/" + mat.spec + "/" + mat.diffuseMod?.hash + "/" + mat.diffuse + "/" + mat.mud + "/" + mat.shine;
            if (textures.TryGetValue(key, out var c) && c) return c;
            {
                using var perf = Reflex.Port.Platform.ReflexMenuPerf.Measure("rxmod sfx map");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                bool chain = mat.template == "chain";
                var (sw, sh) = !string.IsNullOrEmpty(mat.spec) ? SizeOf(mat.specMod, mat.spec) : (0, 0);
                var (dw, dh) = chain && !string.IsNullOrEmpty(mat.diffuse) ? SizeOf(mat.diffuseMod, mat.diffuse) : (0, 0);
                int w = sw > 0 ? sw : dw > 0 ? dw : 4, h = sw > 0 ? sh : dw > 0 ? dh : 4;
                if (w > 2048) { h = Math.Max(4, h * 2048 / w); w = 2048; }
                var sp = sw > 0 ? Pixels(mat.specMod, mat.spec, w, h) : null; var dp = dw > 0 ? Pixels(mat.diffuseMod, mat.diffuse, w, h) : null;
                var o = new Color32[w * h]; byte mud = (byte)Mathf.RoundToInt(mat.mud * MudMask * 255);
                bool plastic = mat.template == "plastic";
                for (int i = 0; i < o.Length; i++)
                {
                    int s = sp != null ? Mathf.Clamp(Mathf.RoundToInt((sp[i].r * 0.299f + sp[i].g * 0.587f + sp[i].b * 0.114f) * mat.shine), 0, 255) : 64;
                    o[i] = new Color32(plastic ? (byte)0 : (byte)s, mud, chain ? (dp != null ? dp[i].a : (byte)255) : (byte)s, 255);
                }
                var t = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = mat.name + "_SFX", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
                t.SetPixels32(o);
                if (LegacyMaps) { t.Apply(true, false); t.Compress(false); t.Apply(false, true); MapMade(t, "sfx", clock); return textures[key] = t; }
                t.Apply(true, false);
                if ((long)w * h > PlainTexels && w % 4 == 0 && h % 4 == 0)
                {
                    var blocks = RxBlocks.Compress(t, null);
                    if (blocks) { UnityEngine.Object.Destroy(t); t = blocks; } else t.Apply(false, true);
                }
                else t.Apply(false, true);
                Sampler(t, 1);
                MapMade(t, "sfx", clock);
                return textures[key] = t;
            }
        }

        /// The tyre program's normal map (the original's is G16R16F: signed X / Y) from an 8-bit normal map: two signed bytes a texel
        /// (R8G8_SNorm: the map's own 8 bits, half the memory of the half floats it was; RGHalf where the device does not filter it).
        /// biased: x / y stored 0 .. 1 (the rider's Jersey_FNL reads its G16R16F map so: the stock jersey maps' mean is 0.5, the program
        /// takes 2 v - 1): the map's own two bytes (RG16). A map over 2048 px is brought down by a box filter; every level's normals are
        /// averaged as vectors and made unit length again.
        internal static Texture2D Normal(RxModMaterial mat, bool biased = false)
        {
            if (string.IsNullOrEmpty(mat.normal)) return null;
            var m = mat.normalMod;
            string key = m.hash + (biased ? "/nrmb/" : "/nrm/") + mat.normal + "/" + mat.normalFlipY;
            if (textures.TryGetValue(key, out var c) && c) return c;
            if (mapFailed.Contains(key)) return null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var src = Load(m, mat.normal); if (!src) { MapFailed(key, m, mat.normal, "no normal map could be read"); return null; }
            try
            {
                int w = Math.Min(src.width, 2048), h = Math.Max(4, src.height * w / src.width);
                var px = Resample(src, w, h, true);
                if (LegacyMaps)
                {
                    var o = new Color[w * h];
                    for (int i = 0; i < o.Length; i++)
                    {
                        float x = px[i].r / 127.5f - 1f, y = (mat.normalFlipY ? -1f : 1f) * (px[i].g / 127.5f - 1f);
                        o[i] = biased ? new Color(x * 0.5f + 0.5f, y * 0.5f + 0.5f, 0, 1) : new Color(x, y, 0, 1);
                    }
                    var lt = new Texture2D(w, h, TextureFormat.RGHalf, true, true) { name = mat.name + "_NRML", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
                    lt.SetPixels(o); lt.Apply(true, true);
                    MapMade(lt, "normal", clock);
                    return textures[key] = lt;
                }
                // the levels: x, y as floats, each level the unit average of the four normals under a texel
                var xs = new float[w * h]; var ys = new float[w * h];
                for (int i = 0; i < xs.Length; i++) { xs[i] = px[i].r / 127.5f - 1f; ys[i] = (mat.normalFlipY ? -1f : 1f) * (px[i].g / 127.5f - 1f); }
                int levels = 1; for (int s = Math.Max(w, h); s > 1; s >>= 1) levels++;
                bool snorm = SystemInfo.IsFormatSupported(UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_SNorm, UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Linear);
                Texture2D t = biased ? new Texture2D(w, h, TextureFormat.RG16, levels, true)
                            : snorm ? new Texture2D(w, h, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_SNorm, levels, UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain)
                            : new Texture2D(w, h, TextureFormat.RGHalf, levels, true);
                t.name = mat.name + "_NRML"; t.hideFlags = HideFlags.DontSave;
                int lw = w, lh = h;
                for (int l = 0; l < levels; l++)
                {
                    int n = lw * lh;
                    if (biased) { var d = new byte[n * 2]; for (int i = 0; i < n; i++) { d[i * 2] = (byte)Mathf.Clamp(Mathf.RoundToInt((xs[i] * 0.5f + 0.5f) * 255f), 0, 255); d[i * 2 + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt((ys[i] * 0.5f + 0.5f) * 255f), 0, 255); } t.SetPixelData(d, l); }
                    else if (snorm) { var d = new sbyte[n * 2]; for (int i = 0; i < n; i++) { d[i * 2] = (sbyte)Mathf.Clamp(Mathf.RoundToInt(xs[i] * 127f), -127, 127); d[i * 2 + 1] = (sbyte)Mathf.Clamp(Mathf.RoundToInt(ys[i] * 127f), -127, 127); } t.SetPixelData(d, l); }
                    else { var d = new ushort[n * 2]; for (int i = 0; i < n; i++) { d[i * 2] = Mathf.FloatToHalf(xs[i]); d[i * 2 + 1] = Mathf.FloatToHalf(ys[i]); } t.SetPixelData(d, l); }
                    if (l + 1 == levels) break;
                    int nw = Math.Max(1, lw >> 1), nh = Math.Max(1, lh >> 1);
                    var nx = new float[nw * nh]; var ny = new float[nw * nh];
                    for (int y = 0; y < nh; y++)
                        for (int x = 0; x < nw; x++)
                        {
                            float sx = 0, sy = 0, sz = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int at = Math.Min(lh - 1, y * 2 + (k >> 1)) * lw + Math.Min(lw - 1, x * 2 + (k & 1));
                                float vx = xs[at], vy = ys[at];
                                sx += vx; sy += vy; sz += Mathf.Sqrt(Mathf.Max(0f, 1f - vx * vx - vy * vy));
                            }
                            float len = Mathf.Sqrt(sx * sx + sy * sy + sz * sz);
                            if (len > 1e-6f) { nx[y * nw + x] = sx / len; ny[y * nw + x] = sy / len; }
                        }
                    xs = nx; ys = ny; lw = nw; lh = nh;
                }
                t.Apply(false, true);
                Sampler(t, 1);
                MapMade(t, "normal", clock);
                return textures[key] = t;
            }
            finally { UnityEngine.Object.Destroy(src); }
        }

        static readonly Dictionary<string, (int w, int h, Color32[] px)> pixels = new Dictionary<string, (int, int, Color32[])>();
        /// A map's pixels at w x h, decoded once per mod and size (the three templates' SFX maps come from one spec map).
        static Color32[] Pixels(RxMod m, string entry, int w, int h)
        {
            string key = m.hash + "/" + entry + "/" + w + "x" + h;
            if (pixels.TryGetValue(key, out var c)) return c.px;
            var t = Load(m, entry); if (!t) return null;
            try { var px = Resample(t, w, h); if (pixels.Count > 8) pixels.Clear(); pixels[key] = (w, h, px); return px; }
            finally { UnityEngine.Object.Destroy(t); }
        }
        static (int w, int h) SizeOf(RxMod m, string entry)
        {
            foreach (var kv in pixels) if (kv.Key.StartsWith(m.hash + "/" + entry + "/", StringComparison.Ordinal)) return (kv.Value.w, kv.Value.h);
            var t = Load(m, entry); if (!t) return (0, 0);
            try { return (t.width, t.height); } finally { UnityEngine.Object.Destroy(t); }
        }

        static Texture2D Load(RxMod m, string entry)
        {
            if (string.IsNullOrEmpty(entry)) return null;
            string key = m.hash + "/" + entry + "/load";
            if (mapFailed.Contains(key)) return null;
            try
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.DontSave };
                if (!t.LoadImage(m.Read(entry), false)) { UnityEngine.Object.Destroy(t); MapFailed(key, m, entry, "not a PNG / JPG"); return null; }
                return t;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException) { MapFailed(key, m, entry, e.Message); return null; }
        }

        /// A texture's pixels at w x h: each texel the average of the source texels it covers (a box filter; the nearest texel where
        /// the map is enlarged). normal: a tangent-space normal map (x, y in R, G): averaged as vectors and made unit length again.
        static Color32[] Resample(Texture2D t, int w, int h, bool normal = false)
        {
            var src = t.GetPixels32(); if (t.width == w && t.height == h) return src;
            int sw = t.width, sh = t.height;
            var o = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = (int)((long)y * sh / h), y1 = Math.Min(sh, Math.Max(y0 + 1, (int)(((long)(y + 1) * sh + h - 1) / h)));
                for (int x = 0; x < w; x++)
                {
                    int x0 = (int)((long)x * sw / w), x1 = Math.Min(sw, Math.Max(x0 + 1, (int)(((long)(x + 1) * sw + w - 1) / w)));
                    if (x1 - x0 == 1 && y1 - y0 == 1) { o[y * w + x] = src[y0 * sw + x0]; continue; }
                    int n = (x1 - x0) * (y1 - y0);
                    if (normal)
                    {
                        float sx = 0, sy = 0, sz = 0; int a = 0;
                        for (int yy = y0; yy < y1; yy++)
                            for (int xx = x0; xx < x1; xx++)
                            {
                                var p = src[yy * sw + xx]; float vx = p.r / 127.5f - 1f, vy = p.g / 127.5f - 1f;
                                sx += vx; sy += vy; sz += Mathf.Sqrt(Mathf.Max(0f, 1f - vx * vx - vy * vy)); a += p.a;
                            }
                        float len = Mathf.Sqrt(sx * sx + sy * sy + sz * sz); if (len < 1e-6f) { sx = 0; sy = 0; sz = 1; len = 1; }
                        byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt((v / len + 1f) * 127.5f), 0, 255);
                        o[y * w + x] = new Color32(B(sx), B(sy), B(sz), (byte)((a + n / 2) / n));
                    }
                    else
                    {
                        int r = 0, g = 0, b = 0, a = 0;
                        for (int yy = y0; yy < y1; yy++)
                            for (int xx = x0; xx < x1; xx++) { var p = src[yy * sw + xx]; r += p.r; g += p.g; b += p.b; a += p.a; }
                        o[y * w + x] = new Color32((byte)((r + n / 2) / n), (byte)((g + n / 2) / n), (byte)((b + n / 2) / n), (byte)((a + n / 2) / n));
                    }
                }
            }
            return o;
        }
    }

    /// DXT1 / DXT5 blocks for the .rxmod maps too large to keep as RGBA32 (RxModBikes.Map): per block the colours' principal axis gives
    /// the two end colours, refined by least squares over the texels' indices (as stb_dxt does), a single-colour block takes the pair
    /// whose 1/3 point is nearest; DXT5's alpha block is its 8-step ramp between the block's lowest and highest alpha. Slower and
    /// closer than Texture2D.Compress's run-time compressor (the block rows are compressed on the worker threads), so the result is
    /// kept: <cache>/RxModMaps/<SHA-256 of the picture file>.v1.rxblocks (the OS's cache folder, never the mod's own: a mod's content
    /// is what its online token names), read back on the next load without decoding the picture. REFLEX_RXMOD_MAP_CACHE=0: no cache;
    /// =<folder>: that folder (tests).
    internal static class RxBlocks
    {
        const uint Magic = 0x314b4252;   // "RBK1": u32 magic, i32 width, height, levels, alpha (1: DXT5), then the levels' blocks
        static readonly byte[] Exp5 = new byte[32], Exp6 = new byte[64];
        static readonly byte[] Match5 = new byte[512], Match6 = new byte[512];   // per 8-bit value: the (max, min) pair whose (2 max + min) / 3 is nearest
        static readonly string cacheSetting = Environment.GetEnvironmentVariable("REFLEX_RXMOD_MAP_CACHE");
        static readonly bool useCache = cacheSetting != "0";

        static RxBlocks()
        {
            for (int i = 0; i < 32; i++) Exp5[i] = (byte)((i << 3) | (i >> 2));
            for (int i = 0; i < 64; i++) Exp6[i] = (byte)((i << 2) | (i >> 4));
            Prepare(Match5, Exp5); Prepare(Match6, Exp6);
        }
        static void Prepare(byte[] table, byte[] expand)
        {
            for (int v = 0; v < 256; v++)
            {
                int best = int.MaxValue;
                for (int mn = 0; mn < expand.Length; mn++)
                    for (int mx = 0; mx < expand.Length; mx++)
                    {
                        int err = Math.Abs((2 * expand[mx] + expand[mn]) / 3 - v) * 256 + Math.Abs(mx - mn);
                        if (err < best) { best = err; table[v * 2] = (byte)mx; table[v * 2 + 1] = (byte)mn; }
                    }
            }
        }

        /// The cache file of a picture file's blocks, null: no cache.
        public static string CacheFile(byte[] picture)
        {
            if (!useCache || picture == null) return null;
            try
            {
                using var sha = SHA256.Create();
                var h = sha.ComputeHash(picture);
                return Path.Combine(string.IsNullOrEmpty(cacheSetting) ? Path.Combine(Application.temporaryCachePath, "RxModMaps") : cacheSetting, BitConverter.ToString(h, 0, 16).Replace("-", "").ToLowerInvariant() + ".v1.rxblocks");
            }
            catch (Exception) { return null; }
        }

        static int Levels(int w, int h) { int n = 1; for (int s = Math.Max(w, h); s > 1; s >>= 1) n++; return n; }

        /// The cached blocks as a texture, null: not cached (or not this build's file).
        public static Texture2D Read(string file, string name)
        {
            if (file == null) return null;
            Texture2D t = null;
            try
            {
                if (!File.Exists(file)) return null;
                var b = File.ReadAllBytes(file);
                if (b.Length < 20 || BitConverter.ToUInt32(b, 0) != Magic) return null;
                int w = BitConverter.ToInt32(b, 4), h = BitConverter.ToInt32(b, 8), levels = BitConverter.ToInt32(b, 12); bool alpha = BitConverter.ToInt32(b, 16) == 1;
                if (w < 4 || h < 4 || w > 16384 || h > 16384 || w % 4 != 0 || h % 4 != 0 || levels != Levels(w, h)) return null;
                var fmt = alpha ? TextureFormat.DXT5 : TextureFormat.DXT1; int bs = alpha ? 16 : 8;
                long total = 20; for (int l = 0; l < levels; l++) total += (long)((Math.Max(1, w >> l) + 3) / 4) * ((Math.Max(1, h >> l) + 3) / 4) * bs;
                if (total != b.Length || !SystemInfo.SupportsTextureFormat(fmt)) return null;
                t = new Texture2D(w, h, fmt, levels, true) { name = name, hideFlags = HideFlags.DontSave };
                int p = 20;
                for (int l = 0; l < levels; l++)
                {
                    int size = ((Math.Max(1, w >> l) + 3) / 4) * ((Math.Max(1, h >> l) + 3) / 4) * bs;
                    var level = new byte[size]; Buffer.BlockCopy(b, p, level, 0, size); t.SetPixelData(level, l); p += size;
                }
                t.Apply(false, true);
                return t;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                if (t) UnityEngine.Object.Destroy(t);
                return null;
            }
        }

        /// A readable RGBA32 texture with its mips as DXT1 (DXT5 when a texel is not opaque), written to cacheFile; null where the
        /// device samples no DXT. Main thread; the source is left as it is.
        public static Texture2D Compress(Texture2D src, string cacheFile)
        {
            int w = src.width, h = src.height, levels = src.mipmapCount;
            var top = src.GetPixels32(0);
            bool alpha = false; for (int i = 0; i < top.Length; i++) if (top[i].a != 255) { alpha = true; break; }
            var fmt = alpha ? TextureFormat.DXT5 : TextureFormat.DXT1;
            if (!SystemInfo.SupportsTextureFormat(fmt) || levels != Levels(w, h)) return null;
            var data = new byte[levels][];
            for (int l = 0; l < levels; l++) data[l] = Encode(l == 0 ? top : src.GetPixels32(l), Math.Max(1, w >> l), Math.Max(1, h >> l), alpha);
            var t = new Texture2D(w, h, fmt, levels, true) { name = src.name, hideFlags = HideFlags.DontSave };
            for (int l = 0; l < levels; l++) t.SetPixelData(data[l], l);
            t.Apply(false, true);
            if (cacheFile != null)
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                    string tmp = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var f = new BinaryWriter(File.Create(tmp)))
                    {
                        f.Write(Magic); f.Write(w); f.Write(h); f.Write(levels); f.Write(alpha ? 1 : 0);
                        foreach (var d in data) f.Write(d);
                    }
                    if (File.Exists(cacheFile)) File.Delete(cacheFile);
                    File.Move(tmp, cacheFile);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning("[RxMod] map cache " + cacheFile + ": " + e.Message); }
            return t;
        }

        sealed class Work { public readonly int[] r = new int[16], g = new int[16], b = new int[16], a = new int[16]; public readonly int[] pal = new int[12]; }

        /// A level's pixels (row 0 first, as the texture holds them) as blocks in the same row order.
        internal static byte[] Encode(Color32[] px, int w, int h, bool alpha)
        {
            int bw = (w + 3) / 4, bh = (h + 3) / 4, bs = alpha ? 16 : 8;
            var o = new byte[bw * bh * bs];
            System.Threading.Tasks.Parallel.For(0, bh, () => new Work(), (by, _, k) =>
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        var p = px[Math.Min(h - 1, by * 4 + (i >> 2)) * w + Math.Min(w - 1, bx * 4 + (i & 3))];
                        k.r[i] = p.r; k.g[i] = p.g; k.b[i] = p.b; k.a[i] = p.a;
                    }
                    int at = (by * bw + bx) * bs;
                    if (alpha) { AlphaBlock(k, o, at); at += 8; }
                    ColorBlock(k, o, at);
                }
                return k;
            }, _ => { });
            return o;
        }

        static void AlphaBlock(Work k, byte[] o, int at)
        {
            int mn = 255, mx = 0;
            for (int i = 0; i < 16; i++) { if (k.a[i] < mn) mn = k.a[i]; if (k.a[i] > mx) mx = k.a[i]; }
            o[at] = (byte)mx; o[at + 1] = (byte)mn;
            ulong bits = 0;
            if (mx > mn)
                for (int i = 0; i < 16; i++)
                {
                    int best = 0, bestErr = int.MaxValue;
                    for (int j = 0; j < 8; j++)
                    {
                        int v = j == 0 ? mx : j == 1 ? mn : ((8 - j) * mx + (j - 1) * mn) / 7;
                        int err = Math.Abs(v - k.a[i]); if (err < bestErr) { bestErr = err; best = j; }
                    }
                    bits |= (ulong)best << (3 * i);
                }
            for (int i = 0; i < 6; i++) o[at + 2 + i] = (byte)(bits >> (8 * i));
        }

        static int Pack(int r, int g, int b) => (((r * 31 + 127) / 255) << 11) | (((g * 63 + 127) / 255) << 5) | ((b * 31 + 127) / 255);

        /// The texels' indices for the end colours c0 > c1 (palette: c0, c1, (2 c0 + c1) / 3, (c0 + 2 c1) / 3) and their squared error.
        static uint Assign(Work k, int c0, int c1, out long error)
        {
            var p = k.pal;
            p[0] = Exp5[c0 >> 11]; p[1] = Exp6[(c0 >> 5) & 63]; p[2] = Exp5[c0 & 31];
            p[3] = Exp5[c1 >> 11]; p[4] = Exp6[(c1 >> 5) & 63]; p[5] = Exp5[c1 & 31];
            for (int c = 0; c < 3; c++) { p[6 + c] = (2 * p[c] + p[3 + c]) / 3; p[9 + c] = (p[c] + 2 * p[3 + c]) / 3; }
            uint bits = 0; error = 0;
            for (int i = 0; i < 16; i++)
            {
                int best = 0, bestErr = int.MaxValue;
                for (int j = 0; j < 4; j++)
                {
                    int dr = k.r[i] - p[j * 3], dg = k.g[i] - p[j * 3 + 1], db = k.b[i] - p[j * 3 + 2];
                    int err = dr * dr + dg * dg + db * db; if (err < bestErr) { bestErr = err; best = j; }
                }
                bits |= (uint)best << (2 * i); error += bestErr;
            }
            return bits;
        }

        static void ColorBlock(Work k, byte[] o, int at)
        {
            int[] r = k.r, g = k.g, b = k.b;
            bool flat = true; int sr = 0, sg = 0, sb = 0, minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
            for (int i = 0; i < 16; i++)
            {
                if (r[i] != r[0] || g[i] != g[0] || b[i] != b[0]) flat = false;
                sr += r[i]; sg += g[i]; sb += b[i];
                if (r[i] < minR) minR = r[i]; if (r[i] > maxR) maxR = r[i]; if (g[i] < minG) minG = g[i]; if (g[i] > maxG) maxG = g[i]; if (b[i] < minB) minB = b[i]; if (b[i] > maxB) maxB = b[i];
            }
            int c0, c1; uint bits;
            if (flat)
            {
                c0 = (Match5[r[0] * 2] << 11) | (Match6[g[0] * 2] << 5) | Match5[b[0] * 2];
                c1 = (Match5[r[0] * 2 + 1] << 11) | (Match6[g[0] * 2 + 1] << 5) | Match5[b[0] * 2 + 1];
                bits = 0xaaaaaaaa;                                                       // index 2: (2 c0 + c1) / 3
                if (c0 < c1) { int x = c0; c0 = c1; c1 = x; bits = 0xffffffff; }         // (swapped: index 3)
                else if (c0 == c1) bits = 0;
            }
            else
            {
                // the principal axis of the block's colours (power iteration on the covariance, from the bounding box's diagonal)
                float mr = sr / 16f, mg = sg / 16f, mb = sb / 16f;
                float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
                for (int i = 0; i < 16; i++)
                {
                    float dr = r[i] - mr, dg = g[i] - mg, db = b[i] - mb;
                    xx += dr * dr; xy += dr * dg; xz += dr * db; yy += dg * dg; yz += dg * db; zz += db * db;
                }
                float vr = maxR - minR, vg = maxG - minG, vb = maxB - minB;
                for (int it = 0; it < 4; it++)
                {
                    float nr = vr * xx + vg * xy + vb * xz, ng = vr * xy + vg * yy + vb * yz, nb = vr * xz + vg * yz + vb * zz;
                    float m = Math.Max(Math.Abs(nr), Math.Max(Math.Abs(ng), Math.Abs(nb)));
                    if (m < 1e-6f) break;
                    vr = nr / m; vg = ng / m; vb = nb / m;
                }
                int lo = 0, hi = 0; float dLo = float.MaxValue, dHi = float.MinValue;
                for (int i = 0; i < 16; i++) { float d = r[i] * vr + g[i] * vg + b[i] * vb; if (d < dLo) { dLo = d; lo = i; } if (d > dHi) { dHi = d; hi = i; } }
                c0 = Pack(r[hi], g[hi], b[hi]); c1 = Pack(r[lo], g[lo], b[lo]);
                long bestErr = long.MaxValue; int b0 = c0, b1 = c1; bits = 0;
                for (int it = 0; it < 4; it++)
                {
                    if (c0 < c1) { int x = c0; c0 = c1; c1 = x; }
                    uint now = Assign(k, c0, c1, out long err);
                    if (err >= bestErr) break;
                    bestErr = err; b0 = c0; b1 = c1; bits = now;
                    if (err == 0 || c0 == c1) break;
                    // the end colours that fit these indices best (least squares; weights in thirds: 3, 0, 2, 1 for c0)
                    int a = 0, bq = 0, cq = 0; int ar = 0, ag = 0, ab = 0, br = 0, bg = 0, bb = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        int j = (int)((now >> (2 * i)) & 3), w3 = j == 0 ? 3 : j == 1 ? 0 : j == 2 ? 2 : 1, u3 = 3 - w3;
                        a += w3 * w3; bq += w3 * u3; cq += u3 * u3;
                        ar += w3 * r[i]; ag += w3 * g[i]; ab += w3 * b[i]; br += u3 * r[i]; bg += u3 * g[i]; bb += u3 * b[i];
                    }
                    int det = a * cq - bq * bq; if (det == 0) break;
                    float f = 3f / det;
                    int E(float v) => v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5f);
                    int n0 = Pack(E((cq * ar - bq * br) * f), E((cq * ag - bq * bg) * f), E((cq * ab - bq * bb) * f));
                    int n1 = Pack(E((a * br - bq * ar) * f), E((a * bg - bq * ag) * f), E((a * bb - bq * ab) * f));
                    if ((n0 == c0 && n1 == c1) || (n0 == c1 && n1 == c0)) break;
                    c0 = n0; c1 = n1;
                }
                c0 = b0; c1 = b1;
                if (c0 == c1) bits = 0;
            }
            o[at] = (byte)c0; o[at + 1] = (byte)(c0 >> 8); o[at + 2] = (byte)c1; o[at + 3] = (byte)(c1 >> 8);
            o[at + 4] = (byte)bits; o[at + 5] = (byte)(bits >> 8); o[at + 6] = (byte)(bits >> 16); o[at + 7] = (byte)(bits >> 24);
        }
    }
}
