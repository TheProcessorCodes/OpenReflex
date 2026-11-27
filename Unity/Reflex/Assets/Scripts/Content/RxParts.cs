// Parts of a .rxmod bike (core/plan/RXMOD.md "Parts"): its other graphic kits (liveries), exhausts, handlebars, tyres ... - what GARAGE >
// VEHICLE > CUSTOMIZE lists on that bike INSTEAD of the original game's parts.
//
// Where compatibility is declared: IN THE MANIFEST. A part exists only as an option of a slot that names its bike:
//   * a "parts" .rxmod ("type": "parts"): "parts": [ { "mod": <the bike mod's id or name>, "bike": <the bike's id / model / VehicleModelId>,
//     "slots": [ ... ] } ]  - found for a bike as a skin finds its bike (RxModBikes.Fits); a parts mod whose bike is not installed is left out;
//     or, a kit pack, by model: { "share": <the bike's share key>, "bike": <its VehicleModelId>, "model": <the bike's name>, "slots": [ ... ] }
//     (the installed bike of that share, VehicleModelId and name - case, spaces, punctuation ignored; no "model": any of that share and
//     VehicleModelId; its GraphicKit options join the bike's GRAPHIC KIT: BikeMods.ResolveParts);
//   * or the bike's own mod.json: "bikes": [ { ..., "parts": [ <slots> ] } ].
//   A slot: { "slot": "Exhaust", "name": "EXHAUST", "replaces": [17, 18], "options": [ ... ] }  ("replaces": the surfaces of the bike's own
//   mesh, by index, that an option with a mesh takes the place of: the bike's default part).
//   An option: { "id": "1096", "name": "Evo R", "line": "Big Gun", "brand": "Big Gun", "default": true (the bike as it is), "mesh":
//   "meshes/x.rxmesh" (surfaces in the bike's mesh space, on its nodes), "materials": { <name>: { template, diffuse, sfx / spec, normal,
//   ... } } }  - a material named as one of the bike's replaces it (a kit, a bar colour: with "template" the whole material, without it only
//   the fields given), any other name is a material of the option's mesh.
// Nothing derives fit from model slots or names: an original part is never drawn on a .rxmod bike and a .rxmod part never on an original
// bike (it has no entry there), so each list holds only what fits. What still WORKS on a .rxmod bike is the original vehicle's tuning: the
// bike rides as that vehicle 1:1, so its performance parts (the part types with a TuningAttribute: exhaust = engine, suspension, tyres)
// keep bringing their tuning points; the garage lists those under PERFORMANCE and hides the purely visual original types (OriginalTypeFits).
//
// The choice: per bike and slot in the client save, "RxPart.<bike mod id>.<VehicleModelId>.<slot>" = the option's id ("" = the default);
// a shared bike's (RXMOD.md "Shared bikes": a "share" key) "RxPart.share-<share>-<name>.<VehicleModelId>.<slot>" (the name: its model line, letters and digits
// lower case); a further bike of a mod on the same model (BikeMods.SplitBikes) "RxPart.<mod id>#<bike>....". An option that is gone: the default.
// Nothing here touches the profile (PlayerVehicleParts / PlayerVehicleTuning): tuning points are earned and spent as on the original bike.
using System;
using System.Collections.Generic;
using System.Linq;
using Reflex.Port.Json;
using UnityEngine;

namespace Reflex.Port.Content
{
    public sealed class RxPartOption
    {
        public RxPartSlot slot; public RxMod mod;
        public string id = "", name = "", line = "", brand = "", mesh;
        public bool isDefault;
        /// An option of a colour slot (RxPartSlot.color): the colour, 0 .. 1; null: not one.
        public float[] rgb;
        /// name -> the fields given (null: not given); whole: it has a "template" (it replaces the bike's material of that name entirely).
        public readonly Dictionary<string, (RxModMaterial m, bool whole, HashSet<string> given)> materials = new Dictionary<string, (RxModMaterial, bool, HashSet<string>)>(StringComparer.OrdinalIgnoreCase);
        public string Title => line.Length > 0 && !name.StartsWith(line, StringComparison.OrdinalIgnoreCase) ? line + " " + name : name;
        public string Key => mod.hash + "/" + slot.id + "/" + id;
    }

    public sealed class RxPartSlot
    {
        public RxMod mod; public string id = "", name = "";
        /// A colour slot ("color": a colour's id, RxTint.color): its options are colours ("rgb") that repaint the materials tinted by that id.
        public string color;
        public int[] replaces = new int[0];
        /// A shared bike's GRAPHIC KIT holding the kits of several mods (BikeMods.ResolveShared): a parts mod's slot of the same id adds its
        /// options to it instead of being left out.
        public bool shared;
        public readonly List<RxPartOption> options = new List<RxPartOption>();
        public RxPartOption Default => options.FirstOrDefault(o => o.isDefault);
        public RxPartOption Find(string optionId) => string.IsNullOrEmpty(optionId) ? null : options.FirstOrDefault(o => o.id == optionId);
    }

    /// The slots a "parts" .rxmod has for one bike of another .rxmod.
    public sealed class RxPartSet
    {
        public RxMod mod; public string targetMod = "", targetBike = "";
        /// A kit pack's bike named by model (RXMOD.md "Shared bikes"): "share" the model's share key (with "bike" its VehicleModelId);
        /// null: the bike is named by "mod" / "bike". "model": the bike's name (BikeMods.NormName): with "share" the pack goes only on the
        /// bike of that name (null: any bike of the share and VehicleModelId).
        public string share, model;
        public readonly List<RxPartSlot> slots = new List<RxPartSlot>();
        /// "tints": the run-time colours of the bike's own materials (by material name).
        public readonly Dictionary<string, RxTint> tints = new Dictionary<string, RxTint>(StringComparer.OrdinalIgnoreCase);
        public RxModBike target;   // (BikeMods.ResolveParts; null: that bike is not installed)
    }

    public static class RxParts
    {
        public const string KeyPrefix = "RxPart.";
        /// Bumped by every choice (the garage's preview keys).
        public static int Version { get; private set; }

        // ------------------------------------------------------------------------------------------------ the manifest

        /// A "slots" list (a parts mod's entry, or a bike's own "parts").
        internal static List<RxPartSlot> ReadSlots(RxMod m, J list, string where)
        {
            var slots = new List<RxPartSlot>();
            if (!list.IsArray) return slots;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i]; if (!o.IsObject) continue;
                var s = new RxPartSlot { mod = m, id = (o["slot"].String ?? o["id"].String ?? "").Trim(), name = (o["name"].String ?? "").Trim().ToUpperInvariant() };
                if (s.id.Length == 0 || s.id.IndexOf('.') >= 0) { Debug.LogWarning($"[RxMod] {m.name}: {where}: slot {i} has no \"slot\" id (letters, no dot)"); continue; }
                if (slots.Any(x => string.Equals(x.id, s.id, StringComparison.OrdinalIgnoreCase))) { Debug.LogWarning($"[RxMod] {m.name}: {where}: a second slot \"{s.id}\" (left out)"); continue; }
                if (s.name.Length == 0) s.name = s.id.ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(o["color"].String)) s.color = o["color"].String.Trim();
                if (o["replaces"].IsArray) s.replaces = o["replaces"].Where(x => x.IsInteger && x.Int >= 0).Select(x => x.Int).Distinct().ToArray();
                var opts = o["options"];
                for (int k = 0; opts.IsArray && k < opts.Count; k++)
                {
                    var x = opts[k]; if (!x.IsObject) continue;
                    var p = new RxPartOption { slot = s, mod = m, id = (x["id"].IsNumber ? x["id"].Int.ToString() : x["id"].String ?? "").Trim(), name = (x["name"].String ?? "").Trim(),
                                               line = (x["line"].String ?? "").Trim(), brand = (x["brand"].String ?? "").Trim(), isDefault = x["default"].Boolean };
                    if (p.id.Length == 0) p.id = "option" + k;
                    if (p.name.Length == 0) p.name = p.id;
                    if (s.options.Any(q => q.id == p.id)) { Debug.LogWarning($"[RxMod] {m.name}: {where}: slot {s.id}: a second option \"{p.id}\" (left out)"); continue; }
                    if (s.color != null && x["rgb"].IsArray && x["rgb"].Count >= 3 && x["rgb"].All(v => v.IsFinite)) p.rgb = x["rgb"].Take(3).Select(v => Mathf.Clamp01((float)v.Number)).ToArray();
                    string mesh = x["mesh"].String;
                    if (!string.IsNullOrEmpty(mesh)) { if (m.Exists(mesh)) p.mesh = mesh; else { Debug.LogWarning($"[RxMod] {m.name}: {where}: option {p.name}: mesh {mesh} is not in the mod (left out)"); continue; } }
                    var mats = x["materials"];
                    if (mats.IsObject)
                        foreach (string name in mats.Keys)
                        {
                            var j = mats[name]; if (!j.IsObject) continue;
                            var given = new HashSet<string>();
                            string E(string f) { var v = j[f].String; if (v == null) return null; if (!m.Exists(v)) { Debug.LogWarning($"[RxMod] {m.name}: option {p.name}: {f} {v} is not in the mod"); return null; } given.Add(f); return v; }
                            var mm = new RxModMaterial { name = name, diffuse = E("diffuse"), spec = E("spec"), normal = E("normal"), sfx = E("sfx"), diffuseMod = m, specMod = m, normalMod = m, sfxMod = m };
                            string t = j["template"].String?.ToLowerInvariant();
                            if (t != null && Array.IndexOf(RxModBikes.Templates, t) >= 0) { mm.template = t; given.Add("template"); }
                            if (j["mud"].IsNumber) { mm.mud = Mathf.Clamp01((float)j["mud"].Number); given.Add("mud"); }
                            if (j["shine"].IsNumber) { mm.shine = Mathf.Clamp((float)j["shine"].Number, 0f, 4f); given.Add("shine"); }
                            if (j["twoSided"].IsBool) { mm.twoSided = j["twoSided"].Boolean; given.Add("twoSided"); }
                            if (j["alphaTest"].IsBool) { mm.alphaTest = j["alphaTest"].Boolean; given.Add("alphaTest"); }
                            if (j["alphaRef"].IsInteger) { mm.alphaRef = Mathf.Clamp(j["alphaRef"].Int, 1, 255); given.Add("alphaRef"); }
                            if (j["normalFlipY"].IsBool) { mm.normalFlipY = j["normalFlipY"].Boolean; given.Add("normalFlipY"); }
                            if (j["numbers"].IsBool) { mm.numbers = j["numbers"].Boolean; given.Add("numbers"); }
                            mm.tint = RxModBikes.ReadTint(m, j["tint"]); if (mm.tint != null) given.Add("tint");
                            p.materials[name] = (mm, given.Contains("template"), given);
                        }
                    if (!p.isDefault && p.mesh == null && p.materials.Count == 0 && p.rgb == null) { Debug.LogWarning($"[RxMod] {m.name}: {where}: option {p.name} changes nothing (left out)"); continue; }
                    s.options.Add(p);
                }
                // exactly one default (the bike as it is): the one marked, else one is added
                var defaults = s.options.Where(q => q.isDefault).ToList();
                for (int k = 1; k < defaults.Count; k++) defaults[k].isDefault = false;
                if (defaults.Count == 0) s.options.Insert(0, new RxPartOption { slot = s, mod = m, id = "", name = "STOCK", isDefault = true });
                if (s.options.Count(q => !q.isDefault) == 0) continue;   // (nothing to choose)
                slots.Add(s);
            }
            return slots;
        }

        /// A parts mod's "parts" list.
        internal static void ReadSets(RxMod m, J j)
        {
            var list = j["parts"]; if (!list.IsArray) throw new System.IO.InvalidDataException("mod.json: no \"parts\" list");
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i]; if (!o.IsObject) continue;
                var set = new RxPartSet { mod = m, targetMod = o["mod"].String?.Trim() ?? "", targetBike = (o["bike"].IsNumber ? o["bike"].Int.ToString() : o["bike"].String ?? "").Trim() };
                // a kit pack names its bike by model: "share" (the model's key) + "bike" (its VehicleModelId); "mod" is then optional
                if (!string.IsNullOrWhiteSpace(o["share"].String)) set.share = o["share"].String.Trim();
                if (!string.IsNullOrWhiteSpace(o["model"].String)) set.model = o["model"].String.Trim();
                if (set.share != null && !int.TryParse(set.targetBike, out _)) { Debug.LogWarning($"[RxMod] {m.name}: parts {i} name a shared bike (\"share\") without its VehicleModelId (\"bike\": a number) (left out)"); continue; }
                if (set.share == null && set.targetMod.Length == 0) { Debug.LogWarning($"[RxMod] {m.name}: parts {i} name no bike mod (\"mod\": its id or name) nor a shared bike (\"share\" + \"bike\"): a part is listed only on the bike it is made for"); continue; }
                set.slots.AddRange(ReadSlots(m, o["slots"], "parts " + i));
                var tints = o["tints"];
                if (tints.IsObject) foreach (string name in tints.Keys) { var t = RxModBikes.ReadTint(m, tints[name]); if (t != null) set.tints[name] = t; }
                if (set.slots.Count > 0) m.partSets.Add(set);
            }
            if (m.partSets.Count == 0) throw new System.IO.InvalidDataException("mod.json: no part could be read");
        }

        // ------------------------------------------------------------------------------------------------ the choice

        /// The id of the bike's card (a further bike of its mod on the same model: "<mod id>#<bike>", BikeMods.SplitBikes).
        static string OwnerId(RxModBike b) => BikeMods.OwnerOf(b)?.id ?? b.mod.name + (b.variant != null ? "#" + b.variant : "");
        /// A shared bike's choice is kept by its share key, not by its mod (which of the joined mods is the base changes as packs are added
        /// or removed), with its name (BikeMods.NormName: two bikes of one model but other names are two bikes): "RxPart.share-<share>-<name>.
        /// <VehicleModelId>.<slot>".
        public static string SaveKey(RxModBike b, RxPartSlot s)
        {
            string name = BikeMods.NormName(b.name?.model);
            return KeyPrefix + (string.IsNullOrEmpty(b.share) ? OwnerId(b) : SharePrefix + b.share + (name.Length > 0 ? "-" + name : "")) + "." + b.vehicleModelId + "." + s.id;
        }
        public const string SharePrefix = "share-";

        /// The option worn in a slot (the default when none is chosen, or the chosen one is gone).
        public static RxPartOption Chosen(RxModBike b, RxPartSlot s)
        {
            if (b == null || s == null) return null;
            return s.Find(Options.ReflexSave.GetString(SaveKey(b, s), "")) ?? s.Default;
        }
        /// Every slot's chosen option that is not the default, in slot order (what the bike is built with).
        public static List<RxPartOption> Chosen(RxModBike b)
        {
            var list = new List<RxPartOption>();
            if (b == null) return list;
            foreach (var s in b.slots) { var o = Chosen(b, s); if (o != null && !o.isDefault) list.Add(o); }
            return list;
        }
        public static void Set(RxModBike b, RxPartOption o)
        {
            if (b == null || o == null) return;
            Options.ReflexSave.SetString(SaveKey(b, o.slot), o.isDefault ? "" : o.id); Options.ReflexSave.Save();
            Version++;
            BikeMods.Touch();   // (the previews and the race bike are built again)
        }
        /// The options with `preview` in its slot's place (the garage's preview of an option not worn).
        public static List<RxPartOption> With(List<RxPartOption> worn, RxPartOption preview)
        {
            var list = (worn ?? new List<RxPartOption>()).Where(o => o.slot != preview.slot).ToList();
            if (!preview.isDefault) list.Add(preview);
            return list;
        }
        public static string Signature(IEnumerable<RxPartOption> options) => options == null ? "" : string.Join(",", options.Select(o => o.Key));

        // ------------------------------------------------------------------------------------------------ what they change

        /// A material of the bike as the worn options leave it (bike: the bike's own, painted by its skin; null: the mesh names none).
        public static RxModMaterial Material(List<RxPartOption> worn, string name, RxModMaterial bike, RxModBike of = null)
            => Tinted(worn, Merged(worn, name, bike), of != null && of.tints.TryGetValue(name, out var t) ? t : null);

        /// A material in the colours worn: its run-time colour (its own "tint", else the bike's for that material) set to the option worn in
        /// that colour's slot; the material as it is when the default is worn (or it has no run-time colour).
        public static RxModMaterial Tinted(List<RxPartOption> worn, RxModMaterial m, RxTint bikeTint = null)
        {
            if (m == null || worn == null) return m;
            var tint = m.tint ?? bikeTint;
            if (tint == null) return m;
            var o = worn.LastOrDefault(x => x.rgb != null && string.Equals(x.slot.color, tint.color, StringComparison.OrdinalIgnoreCase));
            if (o == null) return m;
            var c = m.Clone(); c.tint = tint; c.tintValue = o.rgb;
            return c;
        }

        static RxModMaterial Merged(List<RxPartOption> worn, string name, RxModMaterial bike)
        {
            if (worn == null) return bike;
            foreach (var o in worn)
            {
                if (!o.materials.TryGetValue(name, out var e)) continue;
                if (e.whole || bike == null) { bike = e.m; continue; }
                bike = Over(bike, e.m, e.given, o.mod);
            }
            return bike;
        }

        /// A material with the fields `given` of an option's (its maps read from `mod`) over it.
        internal static RxModMaterial Over(RxModMaterial bike, RxModMaterial o, HashSet<string> g, RxMod mod)
        {
            var m = bike.Clone();
            if (g.Contains("diffuse")) { m.diffuse = o.diffuse; m.diffuseMod = mod; }
            if (g.Contains("spec")) { m.spec = o.spec; m.specMod = mod; }
            if (g.Contains("normal")) { m.normal = o.normal; m.normalMod = mod; }
            if (g.Contains("sfx")) { m.sfx = o.sfx; m.sfxMod = mod; }
            if (g.Contains("mud")) m.mud = o.mud; if (g.Contains("shine")) m.shine = o.shine;
            if (g.Contains("twoSided")) m.twoSided = o.twoSided; if (g.Contains("alphaTest")) m.alphaTest = o.alphaTest;
            if (g.Contains("alphaRef")) m.alphaRef = o.alphaRef; if (g.Contains("normalFlipY")) m.normalFlipY = o.normalFlipY; if (g.Contains("numbers")) m.numbers = o.numbers;
            if (g.Contains("tint")) m.tint = o.tint;
            return m;
        }

        /// The bike's own surfaces (indices in its mesh) the worn options replace with their meshes.
        public static HashSet<int> Replaced(List<RxPartOption> worn)
        {
            var h = new HashSet<int>();
            if (worn != null) foreach (var o in worn) if (o.mesh != null) foreach (int i in o.slot.replaces) h.Add(i);
            return h;
        }

        // ------------------------------------------------------------------------------------------------ the compatibility rule

        /// Whether an ORIGINAL part type of the vehicle a .rxmod bike rides as still does something on that bike (the garage lists it under
        /// PERFORMANCE; the other original types are hidden there):
        ///   * a type with a tuning attribute (VehiclePartType.TuningAttribute: engine / suspension / tires ...): the bike rides as the original
        ///     vehicle 1:1, so the part's tuning points apply as on the original;
        ///   * the tyres and wheels of a bike that has none of its own (the original's are drawn on its axles: ReflexBikeModel.RxOriginalWheels).
        /// Everything else (graphics kits, handlebars, number plates, hand guards ...) changes the original's model, which is not drawn.
        public static bool OriginalTypeFits(string factory, string tuning, bool bikeHasOwnWheels)
        {
            if (!string.IsNullOrEmpty(tuning)) return true;
            if (bikeHasOwnWheels) return false;
            string f = (factory ?? "").ToLowerInvariant();
            return f == "tires" || f == "wheels" || f == "tire" || f == "wheel";
        }

        /// Whether a .rxmod bike's mesh has wheels of its own (surfaces on its tyre nodes); an ATV .rxmod always has (it is bound to its own nodes).
        public static bool HasOwnWheels(RxModBike b)
        {
            if (b == null) return false;
            if (b.OwnNodes) return true;
            try
            {
                var mesh = RxModBikes.Mesh(b);
                int f = mesh.NodeIndex("OBJ_FRT_Tire"), r = mesh.NodeIndex("OBJ_BCK_Tire");
                bool Under(int node, int root) { for (int j = node; j >= 0; j = mesh.parents[j]) if (j == root) return true; return false; }
                return mesh.surfaces.Any(s => !s.surface.skinned && (Under(s.node, f) || Under(s.node, r)));
            }
            catch (Exception e) when (e is System.IO.IOException || e is System.IO.InvalidDataException) { return true; }
        }
    }
}
