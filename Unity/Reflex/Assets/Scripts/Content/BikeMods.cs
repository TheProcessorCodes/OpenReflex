// Bike model mods as a CHOICE next to the original bikes, never a replacement (core/plan/QUEUE.md "w-bikemods").
//
// Install (players): <client root>/Mods/Bikes/ (ReflexMods.cs), any folder at any depth that holds .dx9.database / .dx9.package files
// is one mod (its folder name names it; an optional mod.ini or mod.json gives name / author / description / thumbnail). The classic PC
// mods are made as drop-in replacements of the game's Database files (MXs, HR_MXs, MX_GraphicKits, ...): here they stay in their own
// folder and the install is never touched.
//
// Loading: each mod is its own Rfx.Install over its folder whose databases carry the mod's namespace ("mod:<id>>MXs"), so nothing of a
// mod is cached under an original name (models, materials, textures are all keyed by database name) and the original packages stay
// as they are. Apply(spec, mod) rewrites a BikeSpec's packages to the mod's namespace for every package the mod has (the chassis, the
// graphic kit, the part models and materials it carries; what the mod lacks - tyres, wheels, forks - stays the original), keeping the
// original chassis package's MX_Hierarchy for the pose's node order (the core poses the original hierarchy). ForPlayer(spec) applies
// the player's choice for that bike.
//
// The choice: per VehicleModelId in the client save, save.json "BikeModel.Model.<VehicleModelId>" = the mod id ("" = Original), set in
// GARAGE > VEHICLE > MODEL (OpenFrontEnd/BikeModelPicker.cs); the garage previews and the local rider's race bike use it, computer
// riders keep the originals. The core never reads a mod (the model is drawn by Unity; QUEUE.md "w-bikemods" has the evidence).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Reflex.Port.Content
{
    public sealed class BikeMod
    {
        public string id;            // the folder relative to Mods/Bikes, '/' separators ("Organized/New Bike Models")
        public string name, author = "", description = "", thumbnail;   // thumbnail: a full path or null
        public string folder;
        /// The mod's own names for the bikes it re-skins ([Bikes] of mod.ini / "bikes" of mod.json): the original bike's title or VehicleModelId -> what it really is.
        public readonly Dictionary<string, BikeName> bikeNames = new Dictionary<string, BikeName>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> databases = new List<string>();     // base names ("MXs", "HR_MX_GraphicKits"), as the files have them
        public string Namespace => BikeMods.NamespaceMark + id + ">";
        internal Rfx.Install install;
        /// A .rxmod (Content/RxModBikes.cs): its own models drawn over the original bikes it names; null: a classic .dx9 mod.
        public RxMod rx;
        /// A "bikes" .rxmod with several bikes on one VehicleModelId: each further bike is a card of its own (BikeMods.SplitBikes), an entry
        /// "<mod id>#<bike>" sharing the mod's RxMod and restricted to this bike; null: the mod's own entry (its bikes but those split off).
        public RxModBike only;
        public string Title => string.IsNullOrEmpty(author) ? name : name + " · " + author;

        /// Whether the mod carries this package (db: an original database name).
        public bool Has(string db, string pkg)
        {
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(pkg) || install == null) return false;
            if (!databases.Any(d => string.Equals(d, db, StringComparison.OrdinalIgnoreCase))) return false;
            try { return install.Pkg(Namespace + db, pkg) != null; }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException) { Debug.LogWarning("[BikeMods] " + name + " " + db + ": " + e.Message); return false; }
        }
        public bool Has((string db, string pkg)? p) => p != null && Has(p.Value.db, p.Value.pkg);

        /// The mod's chassis model of a chassis package (the package's model that is not the hierarchy), null: the mod has none.
        public string ChassisModel(string db, string pkg, string preferred)
        {
            if (!Has(db, pkg)) return null;
            var p = install.Pkg(Namespace + db, pkg);
            if (preferred != null && p.Find(preferred, "model") != null) return preferred;
            return p.OfType("model").Select(a => a.name).FirstOrDefault(n => n.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0);
        }
    }

    /// What a mod calls one of its bikes: a brand line and a model line (like the originals' "INTERNAL BLAZE" / "BLAZE 250F") and optionally a brand logo (a file in the mod's folder).
    public sealed class BikeName
    {
        public string brand = "", model = "", logo;   // logo: a full path or null
        public string Full => string.IsNullOrEmpty(brand) ? model : brand + " " + model;
        /// "Honda CRF450R" (the brand is the first word), "Husqvarna | FC 250" (a brand of several words: the bar splits), "CRF450R" (no brand).
        public static BikeName Parse(string text)
        {
            text = (text ?? "").Trim(); if (text.Length == 0) return null;
            int bar = text.IndexOf('|');
            if (bar >= 0) return new BikeName { brand = text.Substring(0, bar).Trim(), model = text.Substring(bar + 1).Trim() };
            int sp = text.IndexOf(' ');
            return sp < 0 ? new BikeName { model = text } : new BikeName { brand = text.Substring(0, sp), model = text.Substring(sp + 1).Trim() };
        }
    }

    public static class BikeMods
    {
        static BikeMods() { Rfx.Install.ModRouter = InstallOf; }
        public const string NamespaceMark = "mod:";
        public const string KeyPrefix = "BikeModel.Model.";
        public const string Original = "";

        static List<BikeMod> s_mods;
        static readonly object s_lock = new object();
        /// Bumped by every choice and rescan: what a preview was built for (OpenShell's garage keys).
        public static int Version { get; private set; }
        /// Raised after a choice is stored (vehicle model id, mod id).
        public static event Action<int, string> Changed;
        /// Computer riders keep the original bikes (as their engine sound: OriginalVehicleSound.PacksForComputerRiders).
        public static bool ForComputerRiders = false;

        /// The scanned folder (<client root>/Mods/Bikes), null without a client root.
        public static string Folder => ReflexMods.Folder(ReflexMods.BikesName);

        /// The MX vs ATV Alive bikes the in-client importer made from the player's own Alive install (<content>/AliveBikes: .rxmod folders,
        /// Content/AliveImport/AliveBikes.cs), scanned next to Folder as mods "AliveBikes/<slug>"; null without a connected install.
        public static string AliveFolder
        {
            get
            {
                string dir = ReflexInstall.Dir;
                if (string.IsNullOrEmpty(dir)) return null;
                string a = Path.Combine(dir, AliveImport.AliveBikes.Folder);
                return Directory.Exists(a) ? a : null;
            }
        }

        /// Every mod found (cached; Refresh rescans).
        public static List<BikeMod> All
        {
            get
            {
                lock (s_lock)
                {
                    if (s_mods != null) return s_mods;
                    s_mods = new List<BikeMod>();
                    try { s_mods = Scan(Folder, AliveFolder, ReflexMods.ModsRoot); }
                    catch (Exception e) { Debug.LogWarning("[BikeMods] scan: " + e.Message); }
                    Rfx.Install.ModRouter = InstallOf;
                    foreach (var m in s_mods) Debug.Log($"[BikeMods] {m.id}: \"{m.name}\" ({string.Join(", ", m.databases)}) in {m.folder}");
                    return s_mods;
                }
            }
        }
        /// A choice that changes what a bike is built as without changing its mod (a .rxmod bike's part: RxParts.Set).
        internal static void Touch() => Version++;
        public static void Refresh() { lock (s_lock) { if (s_mods != null) foreach (var m in s_mods) { m.install?.Close(); if (m.only == null) m.rx?.Close(); /* (a split bike's entry shares its mod's RxMod) */ } s_mods = null; } Version++; }
        public static BikeMod Find(string id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(m => m.id == id);

        /// The install of a namespaced database name ("mod:<id>>MXs"), null for none.
        internal static Rfx.Install InstallOf(string db)
        {
            var (id, _) = Split(db);
            if (id != null && id.StartsWith(KitPacks.IdMark, StringComparison.Ordinal)) return KitPacks.InstallOf(id);   // a gear / kit pack (KitPacks: "mod:kit/<hash>>...")
            return id == null ? null : Find(id)?.install;
        }
        /// (mod id, original database name) of a database name; (null, db) for an original one.
        public static (string id, string db) Split(string db)
        {
            if (db == null || !db.StartsWith(NamespaceMark, StringComparison.Ordinal)) return (null, db);
            int e = db.LastIndexOf('>');
            return e < NamespaceMark.Length ? (null, db) : (db.Substring(NamespaceMark.Length, e - NamespaceMark.Length), db.Substring(e + 1));
        }
        /// The database name without a mod namespace.
        public static string BaseDb(string db) => Split(db).db;
        /// The namespace part of a database name ("" for an original one).
        public static string NamespaceOf(string db) { var (id, _) = Split(db); return id == null ? "" : NamespaceMark + id + ">"; }

        // ------------------------------------------------------------------------------------------------ the scan

        const int MaxDepth = 10;

        /// The mods under root (pure file system walk, no Unity): every folder that directly holds a .dx9.database with its .dx9.package
        /// (case-insensitive, junk ignored) is a mod and is not searched further; any other folder is searched (to MaxDepth). Ordered by id.
        /// modsRoot (MODS ANYWHERE, ReflexMods.cs): the Mods folder; the rest of it is searched too (ids "../<path from Mods>"): every .rxmod
        /// that is not a kit pack, and the folders of .dx9 pairs that are neither a track (a .dx9.level) nor a kit pack (an MXTables_* database).
        public static List<BikeMod> Scan(string root, string aliveRoot = null, string modsRoot = null)
        {
            var mods = new List<BikeMod>();
            bool rest = !string.IsNullOrEmpty(modsRoot) && Directory.Exists(modsRoot) && !ReflexMods.SameFolder(modsRoot, root);
            if ((string.IsNullOrEmpty(root) || !Directory.Exists(root)) && (string.IsNullOrEmpty(aliveRoot) || !Directory.Exists(aliveRoot)) && !rest) return mods;
            void Walk(string dir, int depth)
            {
                string[] files, dirs;
                try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return; }
                // a .rxmod folder (a reflex-mod mod.json: a modder's work in progress, Content/RxModBikes.cs) is one mod; *.rxmod files in any folder
                if (depth > 0 && files.Any(f => string.Equals(Path.GetFileName(f), "mod.json", StringComparison.OrdinalIgnoreCase)) && RxModBikes.IsRxMod(dir))
                {
                    var rx = ProbeRx(root, dir); if (rx != null) mods.Add(rx);
                    return;
                }
                foreach (var f in files.Where(f => f.EndsWith(RxModBikes.Ext, StringComparison.OrdinalIgnoreCase) && !ReflexMods.IsJunk(Path.GetFileName(f))).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var rx = ProbeRx(root, f); if (rx != null) mods.Add(rx);
                }
                var m = depth > 0 ? Probe(root, dir, files) : null;   // (the Bikes folder itself is not a mod: its files are ignored)
                if (m != null) { mods.Add(m); return; }
                if (depth >= MaxDepth) return;
                foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) if (!ReflexMods.IsJunk(Path.GetFileName(d))) Walk(d, depth + 1);
            }
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) Walk(root, 0);
            void WalkRest(string dir, int depth)
            {
                if (ReflexMods.SameFolder(dir, root)) return;   // (Mods/Bikes: walked above)
                string[] files, dirs;
                try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return; }
                if (depth > 0 && files.Any(f => string.Equals(Path.GetFileName(f), "mod.json", StringComparison.OrdinalIgnoreCase)) && RxModBikes.IsRxMod(dir))
                {
                    var rx = ProbeRx(modsRoot, dir, ReflexMods.OutsideId(modsRoot, dir)); if (rx != null) mods.Add(rx);
                    return;
                }
                foreach (var f in files.Where(f => f.EndsWith(RxModBikes.Ext, StringComparison.OrdinalIgnoreCase) && !ReflexMods.IsJunk(Path.GetFileName(f))).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (TrackArchives.IsKitRxMod(f)) continue;   // (a kit pack: KitPacks installs it)
                    var rx = ProbeRx(modsRoot, f, ReflexMods.OutsideId(modsRoot, f)); if (rx != null) mods.Add(rx);
                }
                // a track's folder or a kit pack's: theirs (TrackMods, KitPacks), with what lies under it
                if (files.Any(f => { string n = Path.GetFileName(f).ToLowerInvariant(); return !ReflexMods.IsJunk(n) && (n.EndsWith(".dx9.level", StringComparison.Ordinal) || n.StartsWith("mxtables_", StringComparison.Ordinal) && n.EndsWith(DbExt, StringComparison.Ordinal)); })) return;
                var m = depth > 0 ? Probe(modsRoot, dir, files, ReflexMods.OutsideId(modsRoot, dir)) : null;   // (the Mods folder itself is not a mod)
                if (m != null) { mods.Add(m); return; }
                if (depth >= MaxDepth) return;
                foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) if (!ReflexMods.IsJunk(Path.GetFileName(d))) WalkRest(d, depth + 1);
            }
            if (rest) WalkRest(modsRoot, 0);
            // the imported Alive bikes: .rxmod folders only (their ids "AliveBikes/<slug>")
            if (!string.IsNullOrEmpty(aliveRoot) && Directory.Exists(aliveRoot))
            {
                string[] dirs; try { dirs = Directory.GetDirectories(aliveRoot); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { dirs = new string[0]; }
                foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string n = Path.GetFileName(d);
                    if (n.StartsWith(".", StringComparison.Ordinal) || ReflexMods.IsJunk(n) || !RxModBikes.IsRxMod(d)) continue;
                    var rx = ProbeRx(aliveRoot, d);
                    if (rx == null) continue;
                    rx.id = AliveImport.AliveBikes.Folder + "/" + n;
                    if (!mods.Any(m => m.id == rx.id)) mods.Add(rx);
                }
            }
            SplitBikes(mods);      // (first: every bike of its own card is reachable as its entry's)
            ResolveShared(mods);   // (then: the skins and parts mods find the joined card)
            ResolveSkins(mods);
            ResolveParts(mods);
            ResolveHr(mods);
            mods.RemoveAll(m => m.rx != null && m.rx.type == "hr");   // (never a card of its own: it is its bike's garage model)
            mods.RemoveAll(m => m.rx != null && m.rx.type == "skins" && !m.rx.skins.Any(k => k.target != null || k.original >= 0));   // (a skin of no installed bike)
            mods.RemoveAll(m => m.rx != null && m.rx.type == "parts" && !m.rx.partSets.Any(k => k.target != null));                   // (parts of no installed bike)
            mods.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.OrdinalIgnoreCase));
            // two mods of the same folder name: the parent folder tells them apart
            foreach (var g in mods.Where(m => m.only == null).GroupBy(m => m.name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var m in g)
                {
                    int s = m.id.LastIndexOf('/'); if (s <= 0) continue;
                    string parent = m.id.Substring(0, s);
                    m.name += " (" + (parent == ".." ? ReflexMods.ModsName : parent.StartsWith(ReflexMods.OutsideMark, StringComparison.Ordinal) ? parent.Substring(ReflexMods.OutsideMark.Length) : parent) + ")";
                }
            foreach (var e in mods.Where(m => m.only != null)) e.name = mods.FirstOrDefault(m => m.rx == e.rx && m.only == null)?.name ?? e.name;   // (a split bike: its mod's name)
            return mods;
        }

        const string DbExt = ".dx9.database", PkgExt = ".dx9.package";

        /// The mod of a folder (null: not one).
        static BikeMod Probe(string root, string dir, string[] files, string idOutside = null)
        {
            var byLower = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in files) { string n = Path.GetFileName(f); if (!ReflexMods.IsJunk(n)) byLower[n.ToLowerInvariant()] = f; }
            var dbs = new List<string>();
            foreach (var kv in byLower)
                if (kv.Key.EndsWith(DbExt, StringComparison.Ordinal) && kv.Key.Length > DbExt.Length && byLower.ContainsKey(kv.Key.Substring(0, kv.Key.Length - DbExt.Length) + PkgExt))
                    dbs.Add(Path.GetFileName(kv.Value).Substring(0, Path.GetFileName(kv.Value).Length - DbExt.Length));
            if (dbs.Count == 0) return null;
            string id = idOutside ?? ReflexMods.RelativeId(root, dir) ?? Path.GetFileName(dir);
            var m = new BikeMod { id = id, name = Path.GetFileName(dir.TrimEnd('/', '\\')), folder = dir };
            m.databases.AddRange(dbs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            ReadInfo(m, byLower);
            m.install = new Rfx.Install(dir, m.Namespace, byLower);
            return m;
        }

        /// The mod of a .rxmod file or folder (null: unreadable; the log says why): its id is the path relative to Mods/Bikes.
        static BikeMod ProbeRx(string root, string path, string idOutside = null)
        {
            var rx = RxModBikes.Open(path);
            if (rx.error != null || rx.bikes.Count + rx.skins.Count + rx.partSets.Count == 0 && rx.type != "hr") return null;
            string id = idOutside ?? ReflexMods.RelativeId(root, path) ?? Path.GetFileName(path);
            var m = new BikeMod { id = id, name = rx.name, author = rx.author, description = rx.description, thumbnail = rx.thumbnail, folder = rx.archive ? Path.GetDirectoryName(path) : path, rx = rx };
            foreach (var b in rx.bikes)
                if (!m.bikeNames.ContainsKey(b.vehicleModelId.ToString())) m.bikeNames[b.vehicleModelId.ToString()] = CardNameOf(b);   // (the first of a model: a further one is its own card, SplitBikes)
            return m;
        }

        static BikeName CardNameOf(RxModBike b)
            => b.name != null ? new BikeName { brand = b.name.brand, model = b.name.model, logo = b.logo } : new BikeName { model = b.mod.name, logo = b.logo };

        /// SEVERAL BIKES OF ONE MODEL (core/plan/RXMOD.md "Bikes"): a "bikes" mod with more than one bike on a VehicleModelId keeps its first
        /// under its own entry (its id, so saved choices stay) and gets an entry per further bike, "<mod id>#<bike id>" (else its index),
        /// the same RxMod restricted to that bike (BikeMod.only): a card of its own, named as that bike, chosen and saved under its own id.
        static void SplitBikes(List<BikeMod> mods)
        {
            foreach (var m in mods.Where(m => m.rx != null && m.rx.type == "bikes" && m.only == null).ToList())
                foreach (var g in m.rx.bikes.GroupBy(b => b.vehicleModelId).Where(g => g.Count() > 1))
                    foreach (var b in g.Skip(1))
                    {
                        string v = string.IsNullOrEmpty(b.id) ? b.index.ToString() : b.id;
                        if (m.rx.bikes.Any(x => x != b && x.variant == v)) v += "-" + b.index;   // (two bikes of one id: the index tells them apart)
                        b.variant = v;
                        var e = new BikeMod { id = m.id + "#" + v, name = m.name, author = m.author, description = m.description, thumbnail = m.thumbnail, folder = m.folder, rx = m.rx, only = b };
                        e.bikeNames[b.vehicleModelId.ToString()] = CardNameOf(b);
                        mods.Add(e);
                        Debug.Log($"[RxMod] {m.name} ({m.id}): a further bike of #{b.vehicleModelId}, {b.name?.Full}: a card of its own ({e.id})");
                    }
        }

        /// The bikes of a mod's entry: a split bike's entry its bike, the mod's own entry its bikes but those split off (SplitBikes).
        public static IEnumerable<RxModBike> Bikes(BikeMod m)
            => m?.rx == null ? Enumerable.Empty<RxModBike>() : m.only != null ? new[] { m.only } : m.rx.bikes.Where(b => b.variant == null);

        /// The entry (card) a .rxmod bike belongs to (null: none scanned). mods: the scan's list (default All).
        public static BikeMod OwnerOf(RxModBike b, IEnumerable<BikeMod> mods = null)
        {
            if (b == null) return null;
            mods = mods ?? All;
            return b.variant != null ? mods.FirstOrDefault(x => x.only == b) : mods.FirstOrDefault(x => x.rx == b.mod && x.only == null);
        }

        /// A bike name compared as people mean it: letters and digits only, lower case ("YZ 450-F" = "yz450f").
        public static string NormName(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        /// The slot id of a bike's graphics kits (RXMOD.md "Parts"), which a shared bike's kits join.
        public const string GraphicKitSlot = "GraphicKit";

        /// SHARED BIKES (core/plan/RXMOD.md "Shared bikes"): the bikes of "bikes" mods with the same "share" key and VehicleModelId are one
        /// bike - the same model (geometry, UV layout, parts, material names), so each mod's kits fit the others' mesh. The first by mod id
        /// is the base: it keeps its card, and its GRAPHIC KIT slot (made when it has none, its default the base's own kit) gets each other
        /// bike's own kit (its maps on the kit materials, read from its own mod) and that bike's other kits, ids "<its mod id>:<option id>".
        /// The others are joined (RxModBike.sharedInto): no card of their own; their mods' authors are named on the base's card. The bikes'
        /// names must match too (their model lines, NormName: case, spaces and punctuation ignored): a "YZ450F" and a "YZ450F Monster" of one
        /// model stay two cards. A name alone never joins: two "YZ450F" of different models, or an updated model, have different keys.
        static void ResolveShared(List<BikeMod> mods)
        {
            var groups = mods.Where(m => m.rx != null && m.rx.type == "bikes")
                             .SelectMany(m => Bikes(m).Where(b => !string.IsNullOrEmpty(b.share)).Select(b => (m, b)))
                             .GroupBy(x => x.b.share + "#" + x.b.vehicleModelId + "#" + NormName(x.b.name?.model), StringComparer.Ordinal)
                             .Where(g => g.Count() > 1);
            foreach (var g in groups)
            {
                var list = g.OrderBy(x => x.m.id, StringComparer.OrdinalIgnoreCase).ToList();
                var (bm, b) = list[0];
                var kit = JoinedKit(b);
                foreach (var (om, o) in list.Skip(1))
                {
                    int added = 0;
                    var okit = KitSlot(o);
                    // its own kit: its maps on the kit materials ("kitMaterials", else every material both bikes have), read from its own mod
                    string ownId = okit?.Default?.id, ownName = o.kitName ?? (okit?.Default != null && okit.Default.name != "STOCK" ? okit.Default.name : null);
                    var own = new RxPartOption { slot = kit, mod = o.mod, id = KitNs(om) + ":" + (string.IsNullOrEmpty(ownId) ? "stock" : ownId), name = ownName ?? om.name, brand = o.kitBrand ?? okit?.Default?.brand ?? "" };
                    foreach (string n in (o.kitMaterials ?? o.materials.Keys.ToArray()).Where(n => o.materials.ContainsKey(n) && b.materials.ContainsKey(n)))
                    {
                        var src = o.materials[n];
                        // (with its "numbers": whether its kit shows the rider's number is the kit's, as a kit option's; 2026-11-15, user:
                        // kits with the number painted in)
                        var mm = new RxModMaterial { name = n, diffuse = src.diffuse, spec = src.spec, normal = src.normal, sfx = src.sfx, diffuseMod = o.mod, specMod = o.mod, normalMod = o.mod, sfxMod = o.mod, numbers = src.numbers };
                        own.materials[n] = (mm, false, new HashSet<string> { "diffuse", "spec", "normal", "sfx", "numbers" });
                    }
                    if (own.materials.Count == 0) Debug.LogWarning($"[RxMod] shared bike {o.name?.Full} ({om.id}): no kit material in common with {b.name?.Full} ({bm.id}): its own kit is not listed");
                    else if (AddShared(kit, own, om, bm.name)) added++;
                    // its other kits, over its own kit (a kit names only what it changes: the rest is its bike's)
                    foreach (var p in okit?.options.Where(p => !p.isDefault) ?? Enumerable.Empty<RxPartOption>())
                    {
                        var c = new RxPartOption { slot = kit, mod = p.mod, id = KitNs(om) + ":" + p.id, name = p.name, line = p.line, brand = p.brand, mesh = p.mesh, rgb = p.rgb };
                        foreach (var kv in own.materials) c.materials[kv.Key] = kv.Value;
                        foreach (var kv in p.materials)
                        {
                            if (kv.Value.whole || !c.materials.TryGetValue(kv.Key, out var under)) { c.materials[kv.Key] = kv.Value; continue; }
                            var given = new HashSet<string>(under.given); given.UnionWith(kv.Value.given);
                            c.materials[kv.Key] = (RxParts.Over(under.m, kv.Value.m, kv.Value.given, p.mod), false, given);
                        }
                        if (AddShared(kit, c, om, bm.name)) added++;
                    }
                    o.sharedInto = b;
                    if (!string.IsNullOrEmpty(om.author) && !string.Equals(om.author, bm.author, StringComparison.OrdinalIgnoreCase) && !b.sharedAuthors.Contains(om.author, StringComparer.OrdinalIgnoreCase)) b.sharedAuthors.Add(om.author);
                    Debug.Log($"[RxMod] shared bike {o.name?.Full} of {om.name} ({om.id}) joined into {b.name?.Full} of {bm.name} ({bm.id}) (#{b.vehicleModelId}, {b.share}): {added} kits");
                }
            }
        }

        /// The namespace of another mod's kits in a joined GRAPHIC KIT ("<it>:<option id>"): the mod's own manifest "id" (stable when its
        /// file or folder is renamed), else its id under Mods/Bikes.
        static string KitNs(BikeMod m) => string.IsNullOrEmpty(m.rx?.id) ? m.id : m.rx.id + (m.only != null ? "#" + m.only.variant : "");

        static RxPartSlot KitSlot(RxModBike b) => b.slots.FirstOrDefault(s => string.Equals(s.id, GraphicKitSlot, StringComparison.OrdinalIgnoreCase));

        /// A bike's GRAPHIC KIT as a list other mods' kits join (RxPartSlot.shared): its own slot, else one made whose default is the bike as
        /// it is, named by its "kit" / "kitBrand" (else STOCK). An unnamed default (STOCK, added by the reader) takes the bike's "kit" name.
        static RxPartSlot JoinedKit(RxModBike b)
        {
            var kit = KitSlot(b);
            if (kit == null)
            {
                kit = new RxPartSlot { mod = b.mod, id = GraphicKitSlot, name = "GRAPHIC KIT" };
                kit.options.Add(new RxPartOption { slot = kit, mod = b.mod, id = "", name = b.kitName ?? "STOCK", brand = b.kitBrand ?? "", isDefault = true });
                b.slots.Insert(0, kit);
            }
            else if (kit.Default is RxPartOption d && d.id.Length == 0 && d.name == "STOCK" && b.kitName != null) { d.name = b.kitName; if (d.brand.Length == 0) d.brand = b.kitBrand ?? ""; }
            kit.shared = true;
            return kit;
        }

        /// A parts mod's kits (a GraphicKit slot's options but its default) into a joined GRAPHIC KIT, ids "<parts mod id>:<option id>", each
        /// option's files read from its own mod. The number added.
        static int JoinKits(RxPartSlot into, RxPartSlot s, BikeMod m, string bikeName)
        {
            int added = 0;
            foreach (var o in s.options.Where(o => !o.isDefault))
            {
                var c = new RxPartOption { slot = into, mod = o.mod, id = KitNs(m) + ":" + o.id, name = o.name, line = o.line, brand = o.brand, mesh = o.mesh, rgb = o.rgb };
                foreach (var kv in o.materials) c.materials[kv.Key] = kv.Value;
                if (AddShared(into, c, m, bikeName)) added++;
            }
            return added;
        }

        /// A kit of another mod in a shared GRAPHIC KIT (false: its id is taken, logged and left out).
        static bool AddShared(RxPartSlot kit, RxPartOption o, BikeMod from, string into)
        {
            if (kit.options.Any(q => q.id == o.id)) { Debug.LogWarning($"[RxMod] shared bike: {from.name} ({from.id}): a second kit \"{o.id}\" on {into} (left out)"); return false; }
            kit.options.Add(o);
            return true;
        }

        /// The authors a bike mod's card names for a vehicle model: the mod's, then those of the mods whose bikes were joined into its bike
        /// (shared bikes), ", " between them.
        public static string AuthorFor(BikeMod m, int vehicleModelId)
        {
            if (m == null) return "";
            var b = ListedBike(m, vehicleModelId);
            var all = new List<string>(); if (!string.IsNullOrEmpty(m.author)) all.Add(m.author);
            if (b != null) foreach (var a in b.sharedAuthors) if (!all.Contains(a, StringComparer.OrdinalIgnoreCase)) all.Add(a);
            return string.Join(", ", all);
        }

        /// The .rxmod bike a mod has a card for under a vehicle model (null: none; a shared bike joined into another mod's has none).
        public static RxModBike ListedBike(BikeMod m, int vehicleModelId) => Bikes(m).FirstOrDefault(b => b.vehicleModelId == vehicleModelId && b.sharedInto == null);

        /// The bike a mod's entry lists for a vehicle model (its own bike only, for a split one: BikeMod.only): a shared bike joined into
        /// another mod's is that one (null: the entry has none).
        public static RxModBike BikeOf(BikeMod m, int vehicleModelId)
            => ListedBike(m, vehicleModelId) ?? Bikes(m).FirstOrDefault(b => b.vehicleModelId == vehicleModelId)?.Shown;

        /// Each skin's bike among the scanned .rxmod bikes (RxModBikes.Paints) and its card's names: the bike's brand, its model + the skin's name.
        /// One skin per original bike and skin mod (the choice is per mod); the others are logged and left out.
        static void ResolveSkins(List<BikeMod> mods)
        {
            var bikes = mods.Where(m => m.rx != null).SelectMany(Bikes).ToList();
            foreach (var m in mods.Where(m => m.rx != null && m.rx.type == "skins"))
                foreach (var k in m.rx.skins)
                {
                    if (k.original >= 0)
                    {   // an original bike's skin: the card's model line is the skin's name (the brand stays the original's)
                        string ok = k.original.ToString();
                        if (m.bikeNames.ContainsKey(ok)) { Debug.LogWarning($"[RxMod] skin {k.Title} ({m.id}): a second skin for bike #{ok} in the same mod (one per bike: put it in a mod of its own)"); k.original = -1; continue; }
                        m.bikeNames[ok] = new BikeName { model = k.Title, logo = k.logo };
                        Debug.Log($"[RxMod] skin {k.Title} ({m.id}) paints the original bike #{ok}");
                        continue;
                    }
                    k.target = bikes.FirstOrDefault(b => RxModBikes.Paints(k, b));
                    if (k.target == null) { Debug.LogWarning($"[RxMod] skin {k.Title} ({m.id}): no installed bike mod \"{k.targetMod}\" with a bike \"{k.targetBike}\""); continue; }
                    string key = k.target.vehicleModelId.ToString();
                    if (m.bikeNames.ContainsKey(key)) { Debug.LogWarning($"[RxMod] skin {k.Title} ({m.id}): a second skin for {k.target.name?.Full} in the same mod (one per bike: put it in a mod of its own)"); k.target = null; continue; }
                    var bn = k.target.name;
                    m.bikeNames[key] = new BikeName { brand = bn?.brand ?? "", model = ((bn?.model ?? k.target.mod.name) + " · " + k.Title).Trim(), logo = k.logo ?? k.target.logo };
                    Debug.Log($"[RxMod] skin {k.Title} ({m.id}) paints {k.target.name?.Full} of {k.target.mod.name} (#{k.target.vehicleModelId})");
                }
        }

        /// Each parts mod's bike among the scanned .rxmod bikes (RxModBikes.Names): its slots join that bike's own (Content/RxParts.cs). A part
        /// is listed on the bike its manifest names and nowhere else; a second slot of the same id for a bike is logged and left out, except
        /// kits: a GraphicKit slot joins the bike's GRAPHIC KIT when it has one (JoinKits), and a kit pack named by "share" + "bike" (any
        /// installed bike of that model) makes one when it has none (JoinedKit).
        static void ResolveParts(List<BikeMod> mods)
        {
            var bikes = mods.Where(m => m.rx != null).SelectMany(Bikes).ToList();
            // (an imported Alive bike's own parts mod first: its GRAPHIC KIT is the bike's, whose option ids stay as saved; other kit packs
            // join it. Then by mod id: which pack's slot is added as it is does not depend on the folder walk)
            foreach (var m in mods.Where(m => m.rx != null && m.rx.type == "parts").OrderBy(m => m.rx.importer > 0 ? 0 : 1).ThenBy(m => m.id, StringComparer.OrdinalIgnoreCase))
                foreach (var set in m.rx.partSets)
                {
                    // (a shared bike joined into another: that one's card; a share-named pack with a "model": the bike of that name too)
                    set.target = set.share != null
                        ? bikes.FirstOrDefault(b => b.mod.type == "bikes" && string.Equals(b.share, set.share, StringComparison.Ordinal) && b.vehicleModelId.ToString() == set.targetBike
                                                    && (set.model == null || NormName(b.name?.model) == NormName(set.model) || NormName(b.name?.Full) == NormName(set.model)))?.Shown
                        : bikes.FirstOrDefault(b => RxModBikes.Names(set.targetMod, set.targetBike, b))?.Shown;
                    if (set.target == null)
                    {
                        if (set.share != null) Debug.LogWarning($"[RxMod] parts {m.name} ({m.id}): needs the bike pack of {set.model ?? "its bike"} (share {set.share}, bike {set.targetBike}): no installed bike has it (its parts are not listed anywhere)");
                        else Debug.LogWarning($"[RxMod] parts {m.name} ({m.id}): no installed bike mod \"{set.targetMod}\" with a bike \"{set.targetBike}\" (its parts are not listed anywhere)");
                        continue;
                    }
                    int added = 0;
                    if (m.id.StartsWith(AliveImport.AliveBikes.Folder + "/", StringComparison.Ordinal) && m.rx.importer < AliveImport.AliveBikes.ImportVersion)
                    {
                        set.target.importOutdated = true;
                        Debug.Log($"[RxMod] {set.target.name?.Full}: imported by an older importer (version {m.rx.importer}, this game's is {AliveImport.AliveBikes.ImportVersion}): no high-res garage model or colour slots until it is imported again");
                    }
                    foreach (var kv in set.tints) set.target.tints[kv.Key] = kv.Value;
                    foreach (var s in set.slots)
                    {
                        // kits always join the bike's GRAPHIC KIT when it has one (its own, a shared bike's, an earlier parts mod's; its
                        // options keep their ids, the joining ones are "<parts mod ns>:<option id>"); a kit pack named by "share" makes it
                        // when the bike has none (its default the bike's own kit), a mod-named pack's slot is then added as it is.
                        bool kits = string.Equals(s.id, GraphicKitSlot, StringComparison.OrdinalIgnoreCase);
                        var into = kits ? (set.share != null ? JoinedKit(set.target) : KitSlot(set.target))
                                        : set.target.slots.FirstOrDefault(x => x.shared && string.Equals(x.id, s.id, StringComparison.OrdinalIgnoreCase));
                        if (into != null) { added += JoinKits(into, s, m, set.target.name?.Full ?? set.target.mod.name); continue; }
                        if (set.target.slots.Any(x => string.Equals(x.id, s.id, StringComparison.OrdinalIgnoreCase))) { Debug.LogWarning($"[RxMod] parts {m.name} ({m.id}): {set.target.name?.Full} already has a slot \"{s.id}\" (left out)"); continue; }
                        set.target.slots.Add(s); added++;
                    }
                    Debug.Log($"[RxMod] parts {m.name} ({m.id}) fit {set.target.name?.Full} of {set.target.mod.name}: {added} slots, {set.slots.Sum(s => s.options.Count(o => !o.isDefault))} options");
                }
        }

        /// Each "hr" mod's bike among the scanned .rxmod bikes: its "bike" folder's bike becomes that bike's high-res twin (RxModBike.hr), with
        /// the slots of its "parts" folder. A twin whose bike is not installed, or that does not read, is logged and left out (the bike is
        /// then drawn as it is, everywhere).
        static void ResolveHr(List<BikeMod> mods)
        {
            var bikes = mods.Where(m => m.rx != null).SelectMany(Bikes).ToList();
            foreach (var m in mods.Where(m => m.rx != null && m.rx.type == "hr"))
            {
                var target = bikes.FirstOrDefault(b => RxModBikes.Names(m.rx.hrTargetMod, m.rx.hrTargetBike, b));
                if (target == null) { Debug.LogWarning($"[RxMod] high-res twin {m.name} ({m.id}): no installed bike mod \"{m.rx.hrTargetMod}\" with a bike \"{m.rx.hrTargetBike}\""); continue; }
                var inner = RxModBikes.Open(Path.Combine(m.rx.path, "bike"));
                if (inner.error != null || inner.type != "bikes" || inner.bikes.Count == 0) { Debug.LogWarning($"[RxMod] high-res twin {m.name} ({m.id}): its bike does not read ({inner.error})"); continue; }
                var hb = inner.bikes.FirstOrDefault(b => b.vehicleModelId == target.vehicleModelId) ?? inner.bikes[0];
                int options = 0;
                if (m.rx.Exists("parts/mod.json"))
                {
                    var parts = RxModBikes.Open(Path.Combine(m.rx.path, "parts"));
                    if (parts.error == null && parts.type == "parts")
                    {
                        foreach (var k in parts.partSets) foreach (var kv in k.tints) hb.tints[kv.Key] = kv.Value;
                        foreach (var s in parts.partSets.SelectMany(k => k.slots))
                            if (!hb.slots.Any(x => string.Equals(x.id, s.id, StringComparison.OrdinalIgnoreCase))) { hb.slots.Add(s); options += s.options.Count(o => !o.isDefault); }
                    }
                }
                target.hr = hb;
                Debug.Log($"[RxMod] high-res twin {m.name} ({m.id}) of {target.name?.Full} ({target.mod.name}): {hb.slots.Count} slots, {options} options");
            }
        }

        [Serializable] sealed class BikeEntry { public string bike, name, logo; }
        [Serializable] sealed class Info { public string name, author, description, thumbnail; public BikeEntry[] bikes; }

        /// One line of [Bikes]: "<original bike title or VehicleModelId> = <Brand Model>" or "<same>.logo = file.png" (a file in the mod's folder).
        static void AddBikeName(BikeMod m, string key, string value)
        {
            key = (key ?? "").Trim(); value = (value ?? "").Trim();
            if (key.Length == 0 || value.Length == 0) return;
            bool logo = key.EndsWith(".logo", StringComparison.OrdinalIgnoreCase);
            if (logo) key = key.Substring(0, key.Length - 5).TrimEnd();
            if (!m.bikeNames.TryGetValue(key, out var n)) m.bikeNames[key] = n = new BikeName();
            if (logo)
            {
                string path = Path.Combine(m.folder, value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) n.logo = path;
                return;
            }
            var parsed = BikeName.Parse(value);
            if (parsed != null) { n.brand = parsed.brand; n.model = parsed.model; }
        }

        /// The key = value lines of one [section] of an ini text (comments as ParseIni; the keys keep their case and spaces).
        public static Dictionary<string, string> ParseIniSection(string text, string section)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inside = false;
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string l = raw.Trim().TrimStart('\uFEFF');
                if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;
                if (l[0] == '[') { inside = l.TrimEnd().EndsWith("]") && string.Equals(l.Substring(1, l.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase); continue; }
                if (!inside) continue;
                int e = l.IndexOf('='); if (e <= 0) continue;
                string v = l.Substring(e + 1).Trim();
                if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
                d[l.Substring(0, e).Trim()] = v;
            }
            return d;
        }

        /// What the mod calls the bike of this vehicle model (its original title, or its id; null: the mod does not name it).
        public static BikeName NameFor(BikeMod m, int vehicleModelId, string originalTitle)
        {
            if (m == null || m.bikeNames.Count == 0) return null;
            if (m.bikeNames.TryGetValue(vehicleModelId.ToString(), out var n) && !string.IsNullOrEmpty(n.model)) return n;
            if (!string.IsNullOrEmpty(originalTitle) && m.bikeNames.TryGetValue(originalTitle.Trim(), out n) && !string.IsNullOrEmpty(n.model)) return n;
            return null;
        }

        /// A garage card's name: NameFor, with the pack's name after the model ("YZ450F · Stock OEMs 2025") when another card of a .rxmod
        /// bike has the same model name (NormName), under any original: two packs' bikes of one name that were not joined (another model,
        /// no share key). Unique names stay as they are. mods: the scan's list (default All).
        public static BikeName CardNameFor(BikeMod m, int vehicleModelId, string originalTitle, IEnumerable<BikeMod> mods = null)
        {
            var n = NameFor(m, vehicleModelId, originalTitle);
            if (n == null || m?.rx == null || m.rx.type != "bikes" || string.IsNullOrEmpty(m.name) || ListedBike(m, vehicleModelId) == null) return n;
            string key = NormName(n.model);
            if (key.Length == 0) return n;
            bool clash = (mods ?? All).Any(x => x != m && x.rx != null && x.rx.type == "bikes" && Bikes(x).Any(b => b.sharedInto == null && NormName(CardNameOf(b).model) == key));
            return clash ? new BikeName { brand = n.brand, model = n.model + " · " + m.name, logo = n.logo } : n;
        }

        /// mod.ini (key = value; ';' / '#' comments) or mod.json ({"name": ..}) of a mod folder.
        static void ReadInfo(BikeMod m, Dictionary<string, string> files)
        {
            try
            {
                if (files.TryGetValue("mod.ini", out var ini))
                {
                    var kv = ParseIni(File.ReadAllText(ini));
                    if (kv.TryGetValue("name", out var n) && n.Length > 0) m.name = n;
                    if (kv.TryGetValue("author", out var a)) m.author = a;
                    if (kv.TryGetValue("description", out var d)) m.description = d;
                    if (kv.TryGetValue("thumbnail", out var t) && t.Length > 0) m.thumbnail = Path.Combine(m.folder, t.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                    foreach (var e in ParseIniSection(File.ReadAllText(ini), "Bikes")) AddBikeName(m, e.Key, e.Value);
                }
                else if (files.TryGetValue("mod.json", out var json))
                {
                    var info = JsonUtility.FromJson<Info>(File.ReadAllText(json));
                    if (info != null)
                    {
                        if (!string.IsNullOrWhiteSpace(info.name)) m.name = info.name.Trim();
                        m.author = info.author?.Trim() ?? ""; m.description = info.description?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(info.thumbnail)) m.thumbnail = Path.Combine(m.folder, info.thumbnail.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                        if (info.bikes != null)
                            foreach (var b in info.bikes)
                            {
                                if (b == null || string.IsNullOrWhiteSpace(b.bike)) continue;
                                AddBikeName(m, b.bike.Trim(), b.name);
                                if (!string.IsNullOrWhiteSpace(b.logo)) AddBikeName(m, b.bike.Trim() + ".logo", b.logo);
                            }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[BikeMods] " + m.folder + " info: " + e.Message); }
            if (m.thumbnail != null && !File.Exists(m.thumbnail)) m.thumbnail = null;
        }

        /// key = value lines (keys lower case; ';' and '#' start a comment line; [sections] ignored; quotes around a value dropped).
        public static Dictionary<string, string> ParseIni(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string l = raw.Trim().TrimStart('﻿');
                if (l.Length == 0 || l[0] == ';' || l[0] == '#' || l[0] == '[') continue;
                int e = l.IndexOf('='); if (e <= 0) continue;
                string v = l.Substring(e + 1).Trim();
                if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
                d[l.Substring(0, e).Trim().ToLowerInvariant()] = v;
            }
            return d;
        }

        // ------------------------------------------------------------------------------------------------ what a mod applies to

        /// The mods that have a chassis for this bike (its chassis package), Original not included.
        public static List<BikeMod> ModsFor(BikeSpec original)
        {
            if (original == null || original.chassisDb == null || NamespaceOf(original.chassisDb).Length > 0 || original.rx != null) return new List<BikeMod>();
            // (an ATV: only a .rxmod bike bound to its own nodes, an imported Alive ATV: ATV_Hierarchy node for node; a shared bike joined
            // into another mod's has no card of its own: BikeMods.ResolveShared)
            return All.Where(m => m.rx != null ? !original.machine && (Bikes(m).Any(b => b.vehicleModelId == original.vehicleModelId && b.sharedInto == null && (!original.atv || b.OwnNodes)) || !original.atv && m.rx.skins.Any(k => k.For(original.vehicleModelId)))
                                               : m.ChassisModel(original.chassisDb, original.chassisPkg, original.chassisModel) != null).ToList();
        }

        /// The bike built from the mod's packages wherever the mod has them (the chassis must be the mod's), null: the mod has no chassis for it.
        public static BikeSpec Apply(BikeSpec s, BikeMod m)
        {
            if (s == null || m == null || NamespaceOf(s.chassisDb).Length > 0 || s.rx != null) return null;
            if (m.rx != null)
            {
                var rxSpec = m.rx.type == "skins" ? ApplyRx(s, null, m.rx.skins.FirstOrDefault(k => k.For(s.vehicleModelId)))
                                                  : ApplyRx(s, BikeOf(m, s.vehicleModelId));
                // the player's own bike: with the kits / parts of it the player wears (Content/RxParts.cs; another rider's copy, built by
                // ApplyRx from the look's token, keeps the bike's defaults: the choices are not sent)
                if (rxSpec?.rx != null) rxSpec.rxParts = RxParts.Chosen(rxSpec.rx);
                return rxSpec;
            }
            string chassis = m.ChassisModel(s.chassisDb, s.chassisPkg, s.chassisModel);
            if (chassis == null) return null;
            string ns = m.Namespace;
            (string db, string pkg) N((string db, string pkg) p) => m.Has(p.db, p.pkg) ? (ns + p.db, p.pkg) : p;
            var r = s.Clone();
            r.hierarchyDb = s.hierarchyDb ?? s.chassisDb; r.hierarchyPkg = s.hierarchyPkg ?? s.chassisPkg;   // the pose's node order: the original's
            r.chassisDb = ns + s.chassisDb; r.chassisModel = chassis;
            if (s.kitDb != null && s.kitPkg != null && m.Has(s.kitDb, s.kitPkg)) r.kitDb = ns + s.kitDb;
            if (s.tireDb != null && m.Has(s.tireDb, s.tirePkg))
            {
                var tp = m.install.Pkg(ns + s.tireDb, s.tirePkg);
                if (tp.Find(s.frontModel, "model") != null && tp.Find(s.rearModel, "model") != null) r.tireDb = ns + s.tireDb;
            }
            r.partModels = s.partModels.Select(N).ToList();
            r.partMaterials = s.partMaterials.Select(p => p == null ? ((string db, string pkg)?)null : N(p.Value)).ToList();
            r.materials = s.materials?.Select(N).ToList();
            if (s.forkStickers != null) r.forkStickers = N(s.forkStickers.Value);
            r.modId = m.id; r.modName = m.name;
            r.name = s.name + " [" + m.name + "]";
            return r;
        }

        /// The spec of a .rxmod bike over its original (null: none / not a motocross bike): the original's packages stay (its hierarchy poses
        /// the mod's model, its materials are the templates), the model is the mod's (ReflexBikeModel.RxMod.cs).
        public static BikeSpec ApplyRx(BikeSpec s, RxModBike b, RxSkin skin = null)
        {
            if (skin != null && skin.original >= 0) return ApplyOriginalSkin(s, skin);
            if (skin != null) b = skin.target;   // a skin: its bike, painted
            if (s == null || b == null || (s.atv && (!b.OwnNodes || skin != null)) || s.machine || s.rx != null || b.vehicleModelId != s.vehicleModelId) return null;
            var r = s.Clone();
            r.rx = b; r.rxSkin = skin;
            var owner = skin != null ? All.FirstOrDefault(x => x.rx == skin.mod) : OwnerOf(b);
            r.modId = owner?.id ?? b.mod.name; r.modName = owner?.name ?? b.mod.name;
            r.name = s.name + " [" + r.modName + "]";
            return r;
        }

        // ------------------------------------------------------------------------------------------------ the player's choice

        /// An original bike painted by a .rxmod skin (its graphics kit's textures replaced by the skin's maps: ReflexBikeModel.Build), null: not its bike.
        public static BikeSpec ApplyOriginalSkin(BikeSpec s, RxSkin skin)
        {
            if (s == null || skin == null || skin.original != s.vehicleModelId || s.rx != null || s.rxSkin != null || NamespaceOf(s.chassisDb).Length > 0) return null;
            var r = s.Clone(); r.rxSkin = skin;
            var owner = All.FirstOrDefault(x => x.rx == skin.mod);
            r.modId = owner?.id ?? skin.mod.name; r.modName = owner?.name ?? skin.mod.name;
            r.name = s.name + " [" + skin.Title + "]";
            return r;
        }


        /// The chosen mod id of a vehicle model ("" = Original; a mod that is not installed any more counts as Original; a mod whose bike of
        /// the model was joined into another mod's - a shared bike, ResolveShared - counts as that mod, the card that bike is on).
        public static string ChoiceFor(int vehicleModelId)
        {
            if (vehicleModelId < 0) return Original;
            string id = Options.ReflexSave.GetString(KeyPrefix + vehicleModelId, Original);
            var m = Find(id);
            if (m == null) return Original;
            var b = m.rx?.type == "bikes" ? BikeOf(m, vehicleModelId) : null;
            if (b != null && b.sharedInto == null && OwnerOf(b) is BikeMod o && o != m) return o.id;
            return id;
        }
        public static void SetChoice(int vehicleModelId, string modId)
        {
            if (vehicleModelId < 0) return;
            Options.ReflexSave.SetString(KeyPrefix + vehicleModelId, modId ?? Original); Options.ReflexSave.Save();
            Version++;
            Changed?.Invoke(vehicleModelId, modId ?? Original);
        }

        /// The player's bike as chosen: the mod's variant when the bike's model has a mod chosen that has its chassis, else the spec as it is.
        /// With no mods installed this returns spec itself.
        public static BikeSpec ForPlayer(BikeSpec spec)
        {
            if (spec == null || spec.vehicleModelId < 0 || spec.modId != null) return spec;
            string id = spec.vehicleModelId == RideModel && RideMod != null && (RideMod.Length == 0 || Find(RideMod) != null) ? RideMod : ChoiceFor(spec.vehicleModelId);
            if (id.Length == 0) return spec;
            try { return Apply(spec, Find(id)) ?? spec; }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException) { Debug.LogWarning("[BikeMods] " + id + ": " + e.Message); return spec; }
        }

        /// The garage's TEST RIDE of a bike card (OpenFrontEnd/OpenTestRide.cs): the model and the mod ("" the original) the ride shows, whatever
        /// the bike's saved choice is (RideModel -1: none). Only ForPlayer reads it (the race's local bike); the menus keep the saved choice.
        public static int RideModel = -1;
        public static string RideMod;

        /// The mod chosen for a vehicle model (null: Original).
        public static BikeMod ChosenFor(int vehicleModelId) => Find(ChoiceFor(vehicleModelId));

        /// ALIVE CLASSES (core/plan/ALIVE_BIKES.md): the .rxmod bike the player rides for a model (its chosen mod's bike of that model, or the
        /// bike a chosen skin paints; the garage's TEST RIDE choice first), null: none.
        public static RxModBike RxBikeFor(int vehicleModelId)
        {
            string id = vehicleModelId == RideModel && RideMod != null ? RideMod : ChoiceFor(vehicleModelId);
            var m = Find(id);
            if (m?.rx == null) return null;
            if (m.rx.type == "skins") return m.rx.skins.FirstOrDefault(k => k.target != null && k.target.vehicleModelId == vehicleModelId)?.target;
            return BikeOf(m, vehicleModelId);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterOwnPhysics() => Reflex.Port.RxCore.RxOwnPhysics.Provider = OwnPhysics;

        /// Every model the player rides as a bike with physics of its own (an imported Alive 50cc / 650: RxModBike.physicsClass): the model,
        /// the class id and the parameter file's bytes, for rx_session_vehicle_physics before the event loads. Empty without one.
        public static List<(int model, int cls, byte[] file, string name, string nodes)> OwnPhysics()
        {
            var o = new List<(int, int, byte[], string, string)>();
            foreach (var b in All.Where(m => m.rx != null).SelectMany(Bikes).Where(b => b.physicsClass >= 0 && b.physicsFile != null && b.sharedInto == null))
            {
                if (o.Any(x => x.Item1 == b.vehicleModelId) || RxBikeFor(b.vehicleModelId)?.Key != b.Key) continue;
                try { o.Add((b.vehicleModelId, b.physicsClass, b.mod.Read(b.physicsFile), b.name?.Full ?? b.mod.name, b.physicsNodes == null ? null : System.Text.Encoding.ASCII.GetString(b.mod.Read(b.physicsNodes)))); }
                catch (IOException e) { Debug.LogWarning($"[BikeMods] {b.mod.name}: {b.physicsFile}: {e.Message} (the original's physics)"); }
            }
            return o;
        }

        /// Whether the mod changes a part (its model package or its material package is the mod's).
        public static bool ChangesPart(BikeMod m, (string db, string pkg)? model, (string db, string pkg)? material) => m != null && (m.Has(model) || m.Has(material));

        /// Diagnostics (the editor tests, logs): a mod's chassis of a package parsed through the mod's namespace, as
        /// "nodes N; surfaces S; vertices V; lod0 B bindings: m1,m2..; hierarchy: n1,n2..; database <name> mod=<bool>". Null: not in the mod.
        public static string Describe(BikeMod m, string db, string pkg, string model)
        {
            if (m == null || !m.Has(db, pkg)) return null;
            var p = m.install.Pkg(m.Namespace + db, pkg);
            var a = p.Find(model, "model"); if (a == null) return null;
            var md = Rfx.ParseModel(m.install.Read(a));
            var sf = Rfx.ParseSurfaces(m.install.Read(p.Find(model, "surface") ?? p.OfType("surface").First(x => x.name.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0)));
            var h = p.Find("MX_Hierarchy", "model") ?? p.OfType("model").FirstOrDefault(x => x.name.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) >= 0);
            string hier = h == null ? "-" : string.Join(",", Rfx.ParseModel(m.install.Read(h)).nodes.Select(x => x.name));
            var lod = md.Lod(0).ToList();
            return $"nodes {md.nodes.Length}; surfaces {sf.Length} of {md.surfaceCount}; vertices {sf.Sum(x => x.positions.Length)}; lod0 {lod.Count} bindings: "
                 + string.Join(",", lod.Select(x => x.material).Distinct()) + "; hierarchy: " + hier + "; database " + p.database.name + " mod=" + p.database.mod;
        }

        /// Diagnostics (the editor tests): every LOD 0 surface of a mod chassis with the node it is bound to and the nodes its vertices are
        /// weighted to, and for each skinned one a steer of `steer` degrees (OBJ_UPR_Forks and the nodes under it turned about the
        /// steering node's axis) through the real skin (ReflexBikeModel.SkinPart / OriginalBikeSkin) on scratch objects: "skin err" the
        /// largest distance of a vertex from its weighted sum (0: the skin follows the mod's weights), "rigid" how far the vertices weighted
        /// to no steering node at all would move if the surface moved with its binding node (the old placement; 0 unless it steers).
        /// One line per surface; the last line "<k> bad" counts skinned surfaces whose skin err exceeds 1 mm. Null: not in the mod.
        public static List<string> SkinCheck(BikeMod m, string db, string pkg, string model, float steer = 30f)
        {
            if (m == null || !m.Has(db, pkg)) return null;
            var p = m.install.Pkg(m.Namespace + db, pkg);
            var a = p.Find(model, "model"); if (a == null) return null;
            var md = Rfx.ParseModel(m.install.Read(a));
            var sf = Rfx.ParseSurfaces(m.install.Read(p.Find(model, "surface") ?? p.OfType("surface").First(x => x.name.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0)));
            int fork = md.NodeIndex("OBJ_UPR_Forks");
            bool Steers(int j) { for (; j >= 0; j = md.nodes[j].parent) if (j == fork) return true; return false; }
            Matrix4x4 R = Matrix4x4.identity;
            if (fork >= 0)
            {
                var f = md.nodes[fork].world; Vector3 pivot = f.GetColumn(3), axis = ((Vector3)f.GetColumn(1)).normalized;
                R = Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.AngleAxis(steer, axis)) * Matrix4x4.Translate(-pivot);
            }
            var lines = new List<string>(); int bad = 0;
            var root = new GameObject("SkinCheck " + model);
            try
            {
                foreach (var b in md.Lod(0))
                {
                    var s = sf[b.surface];
                    string bound = md.nodes[b.node].name;
                    if (!s.skinned || s.bones == null) { lines.Add($"surf {b.surface} {b.material}: static on {bound}{(Steers(b.node) ? " (steers)" : "")}"); continue; }
                    var go = new GameObject("part " + b.surface); go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = ReflexMeshes.Build("check", s, Matrix4x4.identity, false);
                    var bones = new Dictionary<int, Transform>();
                    Transform Bone(int j)
                    {
                        if (bones.TryGetValue(j, out var t)) return t;
                        t = new GameObject("bone " + j).transform; t.SetParent(root.transform, false);
                        if (Steers(j)) t.SetPositionAndRotation(R.GetColumn(3), R.rotation);
                        return bones[j] = t;
                    }
                    var skin = ReflexBikeModel.SkinPart(go.transform, s, Bone, out var nodes);
                    if (Steers(b.node)) go.transform.SetPositionAndRotation(R.GetColumn(3), R.rotation);   // where the pose puts the part: its binding node's
                    skin.Deform();
                    var v = go.GetComponent<MeshFilter>().sharedMesh.vertices;
                    float err = 0, rigid = 0; int free = 0;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector3 want = Vector3.zero; float ws = 0; bool any = false;
                        for (int k = 0; k < 4; k++)
                        {
                            int j = s.bones[i * 4 + k]; float w = s.weights[i * 4 + k]; if (j < 0 || w <= 0) continue;
                            want += w * (Steers(j) ? R.MultiplyPoint3x4(s.positions[i]) : s.positions[i]); ws += w; any |= Steers(j);
                        }
                        want = ws > 0 ? want / ws : go.transform.TransformPoint(s.positions[i]);   // (no weight: it stays with the part)
                        err = Mathf.Max(err, (go.transform.TransformPoint(v[i]) - want).magnitude);
                        if (!any) { free++; rigid = Mathf.Max(rigid, ((Steers(b.node) ? R.MultiplyPoint3x4(s.positions[i]) : s.positions[i]) - s.positions[i]).magnitude); }
                    }
                    if (err > 1e-3f) bad++;
                    lines.Add($"surf {b.surface} {b.material}: SKINNED, bound to {bound}{(Steers(b.node) ? " (steers)" : "")}, weighted to {string.Join("+", nodes.Select(j => md.nodes[j].name))}; "
                            + $"{v.Length} vertices, {free} on no steering node; skin err {err:F5}, rigid {rigid:F3}");
                }
            }
            finally { foreach (var f in root.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh) UnityEngine.Object.DestroyImmediate(f.sharedMesh); UnityEngine.Object.DestroyImmediate(root); }
            lines.Add(bad + " bad");
            return lines;
        }

        /// The menu title of a choice.
        public static string Title(string id) { var m = Find(id); return m == null ? "ORIGINAL" : m.name.ToUpperInvariant(); }
    }
}
