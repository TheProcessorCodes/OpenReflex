// Rider gear of "gear" .rxmods (core/plan/RXMOD.md "Gear"): the MX vs ATV Alive gear the in-client importer made from the player's own
// Alive install (Content/AliveImport/AliveGear.cs: <content>/AliveGear/<slug>/, one folder per gear model with its colours / styles as
// pieces). A piece is a helmet, goggles, a jersey (Alive's whole suit: jersey and pants), boots or a neck brace: GARAGE > RIDER lists it
// in its gear type next to the game's own and the kit packs' pieces (OpenGarageData.GearItems), tagged ALIVE; it is owned at once.
//
// A piece's mesh is made on Alive's rider (its rest skeleton is the mesh's node table); the rider builder fits it to Reflex's rider
// (ReflexRiderModel.RxGear.cs, RxGearRig.cs). Its materials borrow the Reflex gear program of their role (Jersey_FNL, Helmet_FNL,
// Boots_FNL, Goggle_FNL ...) with the piece's maps.
//
// Ids: a piece and a line get ids of their own for the run (IdBase / LineBase up: above the game's and the kit packs'), so the garage's
// lists and the spec builder take them like any RiderGear id. The choice is stored by name: client save "RxGear.<RiderGearTypeId>" =
// "<mod id>/<piece id>" ("" = the profile's stock piece or the kit pack's). The profile is not touched (its stock piece stays equipped, as
// with the kit packs). Online: not sent (the other riders see the profile's stock piece).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Reflex.Port.Json;
using UnityEngine;

namespace Reflex.Port.Content
{
    public sealed class RxGearMaterial
    {
        public string name, role = "", program = "", diffuse, sfx, normal;
        public bool plate, alphaTest, alphaBlend, twoSided; public int alphaRef = 128;
        public readonly Dictionary<string, float[]> constants = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class RxGearPiece
    {
        public RxMod mod;
        public int id, lineId = -1, type;          // the run's ids (RxGear.IdBase / LineBase); the Reflex RiderGearTypeId of its slot
        public string pieceId = "", slot = "", name = "", line = "", brand = "", mesh;
        public float[] headBox;                     // Alive's reference helmet box (helmets, goggles), null: none
        public readonly Dictionary<string, RxGearMaterial> materials = new Dictionary<string, RxGearMaterial>(StringComparer.OrdinalIgnoreCase);
        public string Token => mod.id + "/" + pieceId;
        public string Key => mod.hash + "#g" + pieceId;
    }

    public static class RxGear
    {
        public const int IdBase = 30000000, LineBase = 31000000;
        public const string KeyPrefix = "RxGear.";
        public static int Version { get; private set; }

        /// slot -> the Reflex RiderGearTypeId (ReflexVehicleSpecs.GearOrder's keys).
        public static int TypeOf(string slot)
        {
            switch (slot) { case "helmet": return 1; case "goggles": return 2; case "jersey": return 3; case "boots": return 4; case "chest_protector": return 9; case "neck_brace": return 11; default: return -1; }
        }

        // ------------------------------------------------------------------------------------------------ the manifest

        /// A gear mod.json's "gear" list (RxModBikes.ReadManifest).
        internal static void Read(RxMod m, J j)
        {
            var list = j["gear"]; if (!list.IsArray) throw new InvalidDataException("mod.json: no \"gear\" list");
            float[] head = j["headBox"].IsArray && j["headBox"].Count == 6 && j["headBox"].All(x => x.IsFinite) ? j["headBox"].Select(x => (float)x.Number).ToArray() : null;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i]; if (!o.IsObject) continue;
                var p = new RxGearPiece { mod = m, pieceId = (o["id"].IsNumber ? o["id"].Int.ToString() : o["id"].String ?? "").Trim(), slot = (o["slot"].String ?? "").Trim().ToLowerInvariant(),
                                          name = (o["name"].String ?? "").Trim(), line = (o["line"].String ?? "").Trim(), brand = (o["brand"].String ?? "").Trim(), mesh = o["mesh"].String, headBox = head };
                p.type = TypeOf(p.slot);
                if (p.pieceId.Length == 0) p.pieceId = "piece" + i;
                if (p.name.Length == 0) p.name = p.pieceId;
                if (p.type < 0) { Debug.LogWarning($"[RxGear] {m.name}: piece {p.name}: slot \"{p.slot}\" is not a gear slot (helmet, goggles, jersey, boots, neck_brace)"); continue; }
                if (string.IsNullOrEmpty(p.mesh) || !m.Exists(p.mesh)) { Debug.LogWarning($"[RxGear] {m.name}: piece {p.name}: mesh {p.mesh} is not in the mod"); continue; }
                if (m.gear.Any(x => x.pieceId == p.pieceId)) continue;
                var mats = o["materials"];
                if (mats.IsObject)
                    foreach (string name in mats.Keys)
                    {
                        var x = mats[name]; if (!x.IsObject) continue;
                        string E(string f) { var v = x[f].String; return v != null && m.Exists(v) ? v : null; }
                        var mm = new RxGearMaterial { name = name, role = (x["role"].String ?? "").ToLowerInvariant(), program = x["program"].String ?? "", diffuse = E("diffuse"), sfx = E("sfx"), normal = E("normal"),
                                                      plate = x["plate"].Boolean, alphaTest = x["alphaTest"].Boolean, alphaBlend = x["alphaBlend"].Boolean, twoSided = x["twoSided"].Boolean };
                        if (x["alphaRef"].IsInteger) mm.alphaRef = Mathf.Clamp(x["alphaRef"].Int, 1, 255);
                        foreach (string k in x.Keys)
                            if (k.StartsWith("usr_", StringComparison.Ordinal) && x[k].IsArray && x[k].Count >= 3 && x[k].All(v => v.IsFinite)) mm.constants[k] = x[k].Select(v => (float)v.Number).ToArray();
                        p.materials[name] = mm;
                    }
                m.gear.Add(p);
            }
            if (m.gear.Count == 0) throw new InvalidDataException("mod.json: no gear piece could be read");
        }

        // ------------------------------------------------------------------------------------------------ the scan

        static List<RxMod> s_mods; static Dictionary<int, RxGearPiece> s_byId; static Dictionary<int, string> s_lines;
        static readonly object s_lock = new object();

        /// The folder of the imported Alive gear (<content>/AliveGear), null without a connected install or before an import.
        public static string AliveFolder
        {
            get
            {
                string dir = ReflexInstall.Dir;
                if (string.IsNullOrEmpty(dir)) return null;
                string a = Path.Combine(dir, AliveImport.AliveBikes.GearFolder);
                return Directory.Exists(a) ? a : null;
            }
        }

        static void Scan()
        {
            lock (s_lock)
            {
                if (s_mods != null) return;
                var mods = new List<RxMod>(); var byId = new Dictionary<int, RxGearPiece>(); var lines = new Dictionary<int, string>();
                string root = null;
                try { root = AliveFolder; } catch (Exception e) { Debug.LogWarning("[RxGear] " + e.Message); }
                if (root != null)
                {
                    string[] dirs; try { dirs = Directory.GetDirectories(root); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { dirs = new string[0]; }
                    foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        string n = Path.GetFileName(d);
                        if (n.StartsWith(".", StringComparison.Ordinal) || ReflexMods.IsJunk(n) || !RxModBikes.IsRxMod(d)) continue;
                        var m = RxModBikes.Open(d);
                        if (m.error != null || m.type != "gear" || m.gear.Count == 0) continue;
                        if (m.id.Length == 0) m.id = n;
                        mods.Add(m);
                    }
                }
                try
                {   // MODS ANYWHERE (ReflexMods.cs): the rider gear .rxmods a player put in the Mods folder, whatever its sub-folder
                    string modsRoot = ReflexMods.ModsRoot;
                    foreach (var path in RxModBikes.Find(modsRoot))
                    {
                        if (File.Exists(path) && TrackArchives.IsKitRxMod(path)) continue;   // (a kit pack: KitPacks)
                        var m = RxModBikes.Open(path);
                        if (m.error != null || m.type != "gear" || m.gear.Count == 0) continue;
                        if (m.id.Length == 0) m.id = Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\'));
                        if (!mods.Any(x => x.id == m.id)) mods.Add(m);
                    }
                }
                catch (Exception e) { Debug.LogWarning("[RxGear] Mods: " + e.Message); }
                // the run's ids: pieces in (mod, piece) order; a line per gear type and line name
                var lineIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in mods.SelectMany(m => m.gear))
                {
                    p.id = IdBase + byId.Count; byId[p.id] = p;
                    if (p.line.Length == 0) continue;
                    string lk = p.type + "\u0001" + p.line;
                    if (!lineIds.TryGetValue(lk, out int li)) { li = LineBase + lineIds.Count; lineIds[lk] = li; lines[li] = p.line; }
                    p.lineId = li;
                }
                s_byId = byId; s_lines = lines; s_mods = mods;
                if (mods.Count > 0) Debug.Log($"[RxGear] {byId.Count} pieces of {mods.Count} gear models in {root}: " + string.Join(", ", byId.Values.GroupBy(p => p.slot).Select(g => g.Key + " " + g.Count())));
            }
        }
        public static void Refresh() { lock (s_lock) { if (s_mods != null) foreach (var m in s_mods) m.Close(); s_mods = null; s_byId = null; s_lines = null; } Version++; }

        public static IEnumerable<RxGearPiece> All { get { Scan(); return s_byId.Values; } }
        public static bool IsId(int id) => id >= IdBase;
        public static RxGearPiece Piece(int id) { if (id < IdBase) return null; Scan(); return s_byId.TryGetValue(id, out var p) ? p : null; }
        /// A line's title (an id of LineBase up), null: not a line.
        public static string LineTitle(int id) { if (id < LineBase) return null; Scan(); return s_lines.TryGetValue(id, out var s) ? s : null; }
        public static RxGearPiece FromToken(string token) { if (string.IsNullOrEmpty(token)) return null; Scan(); return s_byId.Values.FirstOrDefault(p => p.Token == token); }

        // ------------------------------------------------------------------------------------------------ the choice

        /// The piece chosen for a gear type (null: the profile's stock piece / the kit pack's; a piece that is gone counts as none).
        public static RxGearPiece Chosen(int type)
        {
            var p = FromToken(Options.ReflexSave.GetString(KeyPrefix + type, ""));
            return p != null && p.type == type ? p : null;
        }
        public static List<RxGearPiece> Chosen() { Scan(); return s_byId.Values.Select(p => p.type).Distinct().Select(Chosen).Where(p => p != null).ToList(); }
        public static void Set(int type, RxGearPiece p)
        {
            Options.ReflexSave.SetString(KeyPrefix + type, p == null ? "" : p.Token); Options.ReflexSave.Save();
            Version++;
        }
        public static void Clear(int type) { if (Options.ReflexSave.GetString(KeyPrefix + type, "").Length > 0) Set(type, null); }

        /// A rider's gear ids with the chosen pieces in place of the piece of their type (added when the rider wears none of it).
        public static List<int> WithChosen(IEnumerable<int> ids, Func<int, int> typeOf)
        {
            var list = ids.ToList();
            foreach (var p in Chosen())
            {
                int k = list.FindIndex(x => typeOf(x) == p.type);
                if (k >= 0) list[k] = p.id; else list.Add(p.id);
            }
            return list;
        }

        /// The pieces on a rider's spec: each takes the place of the spec's part of its slot (a suit keeps the stock part's model: Reflex's
        /// own gloves and skin are still drawn from it), or is added.
        public static void Apply(RiderSpec spec, IEnumerable<RxGearPiece> pieces)
        {
            if (spec == null) return;
            foreach (var p in pieces)
            {
                var part = spec.parts.FirstOrDefault(x => x.key == p.slot);
                if (part == null)
                {
                    part = new RiderSpec.Part { key = p.slot };
                    if (p.slot == "jersey") { Body(part, null); spec.parts.Insert(0, part); }   // (the jersey leads the outfit: ReflexVehicleSpecs.GearOrder)
                    else spec.parts.Add(part);
                }
                part.rx = p;
            }
        }

        /// The rider's body under a suit: Reflex's rider has no body of its own, its head, neck and wrists (the skin) and its gloves are
        /// surfaces of the jersey model, so a suit's part must always name a Reflex jersey to take them from (and the gloves' colours):
        /// `stock`'s (the player's own jersey), else the reference outfit's. Without one the rider had no head, neck or hands.
        public static void Body(RiderSpec.Part suit, RiderSpec.Part stock)
        {
            if (suit == null) return;
            if (stock == null || stock.modelPkg == null) { if (suit.modelPkg != null) return; stock = RiderSpec.AnswerRed().parts.First(x => x.key == "jersey"); }
            suit.modelDb = stock.modelDb; suit.modelPkg = stock.modelPkg; suit.asset = stock.asset; suit.matsDb = stock.matsDb; suit.matsPkg = stock.matsPkg;
        }
        /// The player's chosen pieces on the spec of the player's own rider (the race: ReflexRoster).
        /// stock: the rider's spec of the profile's own gear, before the gear / kit pack pieces (KitPacks: a custom gear skin) were put on it.
        /// A piece takes the place of its whole gear type, the custom skin of that type included: its part goes back to the profile's
        /// piece (a suit's body, its gloves and skin, are the profile's jersey's, as the garage draws it: OpenGarageData.RiderWithGear), so
        /// the custom skin's model and material package are neither drawn under the piece nor offered to its materials. The types the
        /// player wears no Alive piece of keep their custom skin.
        public static void ApplyChosen(RiderSpec spec, RiderSpec stock = null)
        {
            try
            {
                var c = Chosen(); if (c.Count == 0 || spec == null) return;
                Apply(spec, c);
                foreach (var p in c)
                {
                    var part = spec.parts.FirstOrDefault(x => x.key == p.slot);
                    var own = stock?.parts.FirstOrDefault(x => x.key == p.slot);
                    if (part == null || own == null || own.modelPkg == null || ReferenceEquals(part, own)) continue;
                    part.modelDb = own.modelDb; part.modelPkg = own.modelPkg; part.asset = own.asset; part.matsDb = own.matsDb; part.matsPkg = own.matsPkg;
                }
            }
            catch (Exception e) { Debug.LogWarning("[RxGear] " + e.Message); }
        }
    }
}
