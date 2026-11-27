// An imported MX vs ATV Alive vehicle's OTHER graphic kits (its liveries) and parts (exhausts, handlebars, tyres; an ATV's wheels, bumpers,
// nerf bars, A-arms, shocks ...): a "parts" .rxmod folder beside the bike's own (core/plan/RXMOD.md "Parts"), <content>/AliveBikes/<slug>-parts/,
// that names the bike it fits ("mod" / "bike": the bike's ids). The bike's own folder stays byte for byte what it was (its content hash is
// the online token two players share), so a player without the Rider Gear packs' kits still sees the same bike.
//
//   which parts     Alive's own tables: the VehicleModelParts rows of the vehicle (AliveBikeSource.slots), by VehiclePartType. Only what
//                   Alive lists for that vehicle is made for it: that list IS the compatibility (a part is never offered on another bike).
//   an option       is the bike with that one part in place of the default of its type, made exactly as the bike was (AliveBikes.cs):
//                   a part with a model of its own (another exhaust, another bar) gives a mesh that replaces the default part's surfaces
//                   ("replaces": their indices in the bike's mesh); a part that only brings materials (a graphic kit, a bar colour) gives
//                   the bike's materials it changes ("materials": by the bike's material names), every binding resolved again with the
//                   option's package in the pool (the graphic kit first) and the option's colours (VehiclePartCustomColor).
//   left out        a part that changes nothing drawn (Alive's forks, frames, brakes and hubs carry tuning files and stickers only), the
//                   number plate colours (they repaint the kit's plastics: one bake per kit and colour), and a type left with its default alone.
// Alive's own tuning bonus of a part (DefaultTuneAttribute) is written as "aliveTune" for reference; the client does not apply it: the
// tuning points stay those of the Reflex vehicle the bike rides as (core/plan/ALIVE_BIKES.md "Parts").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Reflex.Port.Content.AliveImport
{
    public static partial class AliveBikes
    {
        public const string PartsSuffix = "-parts";
        /// The version of what this importer makes beside a bike (its parts folder's "importer"; a bike's own folder carries none: it must
        /// stay byte for byte the same). 2: the high-res garage set (AliveBikeHr.cs) and the colour slots. The garage flags a bike whose
        /// parts folder is older (BikeMods.ResolveParts -> RxModBike.importOutdated): it was imported before, or without its source.
        public const int ImportVersion = 2;

        /// Where a bike of the sources stands in the content folder: 0 not imported, 1 imported by this importer, 2 imported by an older
        /// one (its parts folder's "importer" is older than ImportVersion: it has no high-res garage model and no colour slots yet).
        public static int ImportState(string content, AliveBikeSource b)
        {
            if (!Installed(content, b)) return 0;
            string parts = Path.Combine(Root(content), b.Slug + PartsSuffix, "mod.json");
            if (!File.Exists(parts)) return 1;   // (a vehicle Alive has no other kit or part for: nothing newer to make)
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(parts), "\"importer\"\\s*:\\s*(\\d+)");
                return m.Success && int.Parse(m.Groups[1].Value) >= ImportVersion ? 1 : 2;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return 1; }
        }

        /// The garage's name of an Alive part type.
        static string SlotTitle(string type)
        {
            switch (type)
            {
                case "GraphicKits": return "GRAPHIC KIT";
                case "Exhausts": return "EXHAUST";
                case "Handlebars": return "HANDLEBARS";
                case "Tires": return "TIRES";
                case "Wheels": return "WHEELS";
                case "A-Arms": return "A-ARMS";
                case "Bumpers": return "BUMPER";
                case "NerfBars": return "NERF BARS";
                case "Suspensions": return "SHOCKS";
                case "HandGuards": return "HAND GUARDS";
                case "Grilles": return "GRILLE";
                case "SwingarmMaterial": return "SWINGARM";
                case "FrameMaterial": return "FRAME FINISH";
                default: return (type ?? "PART").ToUpperInvariant();
            }
        }

        static bool SamePkg((string db, string pkg)? a, (string db, string pkg)? b) =>
            a == null ? b == null : b != null && string.Equals(a.Value.db, b.Value.db, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Value.pkg, b.Value.pkg, StringComparison.OrdinalIgnoreCase);

        /// The option's material of a binding (its own cache: two packages may name their materials alike).
        static Mat ResolveOption(Dictionary<string, Mat> cache, string binding, PackageAssets own, List<PackageAssets> pool)
        {
            string kb = MaterialKey(binding);
            PackageAssets at = null; string name = null;
            foreach (var p in pool)
            {
                foreach (string m in p.Names("material"))
                {
                    string km = MaterialKey(m);
                    if (km.Length > 0 && kb.Length >= km.Length && string.Compare(kb, 0, km, 0, km.Length, StringComparison.OrdinalIgnoreCase) == 0) { at = p; name = m; break; }
                }
                if (at != null) break;
            }
            if (at == null) { at = own; name = binding; }
            string key = at.db + "/" + at.pkg + "/" + name;
            if (cache.TryGetValue(key, out var c)) return c;
            var bytes = at.Find(name, "material");
            var def = bytes != null ? ReadMaterial(name, bytes) : new Rfx.Material { name = name };
            return cache[key] = new Mat { key = Safe(name), pkg = at, def = def, template = Template(def) };
        }

        /// Whether two packages hold the same material under one name: the same document and the same bytes of every texture it names that
        /// either package holds. (An HR exhaust package repeats the chassis' engine material, HR_ATV_Eng_TRX450: found there first by an
        /// option's pool, it counted as a change of the engine by every kit and part, and the parts were then left out as repainting the kit.)
        static bool SameMaterial(Mat a, Mat b)
        {
            if (!string.Equals(a.def.name, b.def.name, StringComparison.OrdinalIgnoreCase)) return false;
            byte[] da = a.pkg.Find(a.def.name, "material"), db = b.pkg.Find(b.def.name, "material");
            if (da == null || db == null || !da.SequenceEqual(db)) return false;
            foreach (string t in a.def.textures.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                byte[] ta = a.pkg.Find(t, "texture"), tb = b.pkg.Find(t, "texture");
                if (ta == null && tb == null) continue;
                if (ta == null || tb == null || !ta.SequenceEqual(tb)) return false;
            }
            return true;
        }

        /// Whether a material's constants come out differently under two sets of part colours.
        static bool ColoursDiffer(Rfx.Material def, Dictionary<string, float[]> a, Dictionary<string, float[]> b)
        {
            foreach (var k in def.parameters.Keys)
            {
                string c = k.TrimEnd('_');
                a.TryGetValue(c, out var x); b.TryGetValue(c, out var y);
                if (x == null && y == null) continue;
                if (x == null || y == null || !x.SequenceEqual(y)) return true;
            }
            return false;
        }

        /// A plain name for a colour of Alive's palette (its rows carry no names): grey levels by their value, the others by their hue.
        static string ColourName(float[] c)
        {
            float mx = Math.Max(c[0], Math.Max(c[1], c[2])), mn = Math.Min(c[0], Math.Min(c[1], c[2])), d = mx - mn;
            if (d < 0.08f) return mx > 0.8f ? "White" : mx > 0.45f ? "Silver" : mx > 0.22f ? "Grey" : mx > 0.08f ? "Charcoal" : "Black";
            float h = mx == c[0] ? (c[1] - c[2]) / d % 6f : mx == c[1] ? (c[2] - c[0]) / d + 2f : (c[0] - c[1]) / d + 4f;
            h *= 60f; if (h < 0) h += 360f;
            string hue = h < 12 || h >= 345 ? "Red" : h < 38 ? "Orange" : h < 50 ? "Gold" : h < 68 ? "Yellow" : h < 95 ? "Lime" : h < 165 ? "Green" : h < 200 ? "Teal" : h < 225 ? "Sky Blue" : h < 255 ? "Blue" : h < 285 ? "Purple" : h < 325 ? "Magenta" : "Pink";
            if (d / Math.Max(mx, 1e-3f) < 0.3f) return (mx > 0.5f ? "Pale " : "Dusty ") + hue;
            return mx < 0.32f ? "Dark " + hue : mx < 0.62f && hue != "Gold" ? "Deep " + hue : hue;
        }

        /// The bike's "parts" .rxmod (see the head of this file) into `folder` (created only when the bike has an option to offer).
        static void ConvertParts(BikeBuild x, string folder, AliveBikeResult res)
        {
            var b = x.b;
            var o = new MapOut { folder = folder, res = new AliveBikeResult { bike = b }, dedupe = true, count = (n, bytes) => { res.partMaps += n; res.partBytes += bytes; } };
            var cache = new Dictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);
            var meshes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // model package -> the mesh entry written
            var slotsJson = new List<object>();
            var kitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PackageAssets Read((string db, string pkg)? p) => p == null ? null : x.index.Read(p.Value.db, p.Value.pkg, x.cancel);
            string Prefix(Mat m) => m.pkg.pkg + ".";

            foreach (var slot in b.slots.OrderBy(s => s.type == "GraphicKits" ? 0 : 1))   // (the kits first: what they repaint, no other part may)
            {
                x.cancel.ThrowIfCancellationRequested();
                var def = slot.options.FirstOrDefault(q => q.isDefault);
                if (def == null || slot.options.Count < 2) continue;
                if (slot.type == "NumberPlate") continue;   // (the plate's colours: a colour slot, below)
                bool kits = slot.type == "GraphicKits";
                var defMats = Read(def.materials);
                var replaced = kits ? new List<Part>() : x.parts.Where(p => p.slot == slot.type).ToList();
                var optionsJson = new List<object>();
                int dropped = 0, clash = 0;
                foreach (var opt in slot.options)
                {
                    if (opt.isDefault) continue;
                    x.cancel.ThrowIfCancellationRequested();
                    var optMats = Read(opt.materials);
                    bool sameModel = kits || SamePkg(opt.model, def.model);
                    if (!sameModel && opt.model != null && x.index.Read(opt.model.Value.db, opt.model.Value.pkg, x.cancel) == null) { res.partNotes.Add($"{slot.type} {opt.name}: {opt.model.Value.db}/{opt.model.Value.pkg} is not in the sources"); continue; }
                    // the pool with the option's material package where the default's was (a graphic kit: first)
                    var pool = new List<PackageAssets>(x.pool);
                    int at = defMats != null ? pool.IndexOf(defMats) : -1;
                    if (at >= 0) pool.RemoveAt(at);
                    if (optMats != null && !pool.Contains(optMats)) pool.Insert(kits ? 0 : at >= 0 ? at : pool.Count, optMats);
                    // the colours: the default parts' without this type's, with the option's
                    var colors = new Dictionary<string, float[]>(b.colors, StringComparer.OrdinalIgnoreCase);
                    foreach (var k in def.colors.Keys) colors.Remove(k);
                    foreach (var kv in opt.colors) colors[kv.Key] = kv.Value;

                    // the bike's own surfaces that stay: each binding resolved again; a changed material is the option's
                    var overrides = new JObj(); var changes = new List<(string key, Mat m)>();
                    foreach (var p in x.parts)
                    {
                        if (!sameModel && replaced.Contains(p)) continue;
                        var own = p.slot == slot.type ? optMats : p.own;
                        var m = ResolveOption(cache, p.binding, p.model, own != null ? new List<PackageAssets> { own }.Concat(pool).ToList() : pool);
                        bool changed = m.pkg != p.mat.pkg && !SameMaterial(m, p.mat) || !string.Equals(m.def.name, p.mat.def.name, StringComparison.OrdinalIgnoreCase) || ColoursDiffer(m.def, b.colors, colors);
                        if (changed && !changes.Any(c => string.Equals(c.key, p.mat.key, StringComparison.OrdinalIgnoreCase))) changes.Add((p.mat.key, m));
                    }
                    if (!kits && changes.Any(c => kitKeys.Contains(c.key))) { clash++; continue; }
                    foreach (var c in changes) overrides.Set(c.key, x.MaterialEntry(o, c.m, colors, pool, Prefix(c.m), false));
                    // a model of its own: the mesh that replaces the default part's surfaces, its materials by binding name
                    string mesh = null;
                    if (!sameModel && opt.model != null)
                    {
                        var made = x.PartModel(slot.type, opt.model.Value, opt.materials, (binding, pa, own) => ResolveOption(cache, binding, pa, own != null ? new List<PackageAssets> { own }.Concat(pool).ToList() : pool), res.partNotes);
                        if (made.Count > 0)
                        {
                            string mk = opt.model.Value.db + "/" + opt.model.Value.pkg;
                            var names = made.Select(p => p.binding).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                            if (!meshes.TryGetValue(mk, out mesh))
                            {
                                mesh = "meshes/" + Safe(opt.model.Value.pkg) + ".rxmesh";
                                var bytes = WriteMesh(x.hier, made, names, p => names.FindIndex(n => string.Equals(n, p.binding, StringComparison.OrdinalIgnoreCase)), null);
                                Directory.CreateDirectory(Path.Combine(folder, "meshes"));
                                File.WriteAllBytes(Path.Combine(folder, mesh.Replace('/', Path.DirectorySeparatorChar)), bytes);
                                res.partBytes += bytes.Length; res.partMeshes++;
                                meshes[mk] = mesh;
                            }
                            foreach (var p in made.GroupBy(p => p.binding, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                                overrides.Set(p.binding, x.MaterialEntry(o, p.mat, colors, pool, Prefix(p.mat), false));
                        }
                    }
                    if (mesh == null && overrides.items.Count == 0) { dropped++; continue; }
                    if (mesh == null && !sameModel && !kits) { dropped++; continue; }   // (a model that gave no surface: nothing to put in the default's place)
                    if (kits) foreach (var kv in overrides.items) kitKeys.Add(kv.Key);
                    var e = new JObj().Set("id", opt.partId.ToString()).Set("name", opt.name);
                    if (opt.line.Length > 0) e.Set("line", opt.line);
                    if (opt.brand.Length > 0) e.Set("brand", opt.brand);
                    if (opt.tune1 != 0 || opt.tune2 != 0) e.Set("aliveTune", new List<object> { (long)opt.tune1, (long)opt.tune2 });
                    if (mesh != null) e.Set("mesh", mesh);
                    if (overrides.items.Count > 0) e.Set("materials", overrides);
                    optionsJson.Add(e);
                }
                if (dropped > 0) res.partNotes.Add($"{slot.type}: {dropped} of {slot.options.Count - 1} change nothing drawn (left out)");
                if (clash > 0) res.partNotes.Add($"{slot.type}: {clash} repaint the graphic kit's materials (left out)");
                if (optionsJson.Count == 0) continue;
                var d = new JObj().Set("id", def.partId.ToString()).Set("name", def.name).Set("default", true);
                if (def.line.Length > 0) d.Set("line", def.line);
                if (def.brand.Length > 0) d.Set("brand", def.brand);
                optionsJson.Insert(0, d);
                var sj = new JObj().Set("slot", slot.factory).Set("name", SlotTitle(slot.type));
                if (replaced.Count > 0) sj.Set("replaces", replaced.Select(p => (object)(long)x.parts.IndexOf(p)).ToList());
                sj.Set("options", optionsJson);
                slotsJson.Add((slot.ordinal, sj));
                res.partSlots++; res.partOptions += optionsJson.Count - 1;
            }
            // the run-time colours (RXMOD.md "Colours"): the bike's own materials Alive colours at run time ("tints": the parts' materials carry
            // theirs), and a colour slot for each colour that paints something: the number plate's (Alive's NumberPlate parts: their
            // DynNumPlate colours) and the ones the player may paint with Alive's palette (CustomColor.UserCustomizable: rims, hubs, an ATV's
            // frame, swing arm and A-arms). The plastics' own colour (UserDynColor) is not one: it is baked per kit (the kit's alpha).
            var tintsJson = new JObj();
            foreach (var m in x.used) { var t = x.TintEntry(o, m, b.colors, x.pool, "bike."); if (t != null) tintsJson.Set(m.key, t); }
            int colourOrdinal = 1000;
            foreach (var (id, slotId, title) in new[] { ("DynNumPlate", "NumberPlate", "NUMBER PLATE"), ("MxRimDynColor", "RimColor", "RIM COLOR"), ("MxHubDynColor", "HubColor", "HUB COLOR"), ("AtvRimDynColor", "RimColor", "RIM COLOR"),
                                                        ("AtvFrameDynColor", "FrameColor", "FRAME COLOR"), ("AtvSwingDynColor", "SwingarmColor", "SWINGARM COLOR"), ("ATVArmDynColor", "ArmColor", "A-ARM COLOR") })
            {
                colourOrdinal++;
                if (!x.tintColors.Contains(id)) continue;
                var options = new List<object>();
                if (id == "DynNumPlate")
                {   // Alive's own plate parts (Stock, Blue, Red, Yellow: the classes' plates)
                    var plates = b.slots.FirstOrDefault(s => s.type == "NumberPlate");
                    if (plates == null) continue;
                    foreach (var p in plates.options)
                    {
                        var e = new JObj().Set("id", p.partId.ToString()).Set("name", p.name);
                        if (p.isDefault) e.Set("default", true);
                        else if (p.colors.TryGetValue("usr_DynNumPlate", out var c)) e.Set("rgb", new List<object> { (double)c[0], (double)c[1], (double)c[2] });
                        else continue;
                        options.Add(e);
                    }
                }
                else
                {
                    options.Add(new JObj().Set("id", "stock").Set("name", "Stock").Set("default", true));
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (pid, rgb) in b.palette)
                    {
                        string name = ColourName(rgb); for (int k = 2; !names.Add(name); k++) name = ColourName(rgb) + " " + k;
                        options.Add(new JObj().Set("id", "c" + pid).Set("name", name).Set("rgb", new List<object> { (double)rgb[0], (double)rgb[1], (double)rgb[2] }));
                    }
                }
                if (options.Count < 2) continue;
                slotsJson.Add((colourOrdinal, new JObj().Set("slot", slotId).Set("name", title).Set("color", id).Set("options", options)));
                res.partSlots++; res.partOptions += options.Count - 1;
            }
            foreach (var n in o.res.notes) res.partNotes.Add(n);
            if (slotsJson.Count == 0) { if (Directory.Exists(folder)) Directory.Delete(folder, true); return; }
            Directory.CreateDirectory(folder);
            var ordered = slotsJson.Cast<(int ordinal, JObj j)>().OrderBy(s => s.ordinal).Select(s => (object)s.j).ToList();
            var fit = new JObj().Set("mod", b.Slug).Set("bike", BikeId(b)).Set("slots", ordered);
            if (tintsJson.items.Count > 0) fit.Set("tints", tintsJson);
            var manifest = new JObj().Set("format", "reflex-mod").Set("version", 1L).Set("importer", (long)ImportVersion).Set("type", "parts").Set("id", b.Slug + PartsSuffix)
                .Set("name", b.Label + " kits and parts").Set("author", Author)
                .Set("description", $"The graphic kits and parts MX vs ATV Alive has for the {b.Label}, imported from your own copy; they fit that bike only")
                .Set("parts", new List<object> { fit });
            var text = Encoding.UTF8.GetBytes(Json.Dumps(manifest, 1) + "\n");
            File.WriteAllBytes(Path.Combine(folder, "mod.json"), text);
            res.partBytes += text.Length;
            res.partsFolder = folder;
        }
    }
}
