// MX vs ATV Alive's rider gear, imported from the player's own Alive install (core/plan/ALIVE_BIKES.md "Rider gear"): each gear MODEL of
// Alive (a helmet shell, a goggle frame, a boot, a neck brace, a jersey-and-pants suit: Alive's RiderGear rows name a model package and a
// material package, one row per colour / style) becomes a "gear" .rxmod folder in the client's content folder, <content>/AliveGear/<slug>/:
//   mod.json      "type": "gear": the pieces (one per RiderGear row of that model: its store name, line and brand, its slot - helmet,
//                 goggles, jersey, boots, neck_brace -, the mesh, and its materials by the mesh's material names)
//   gear.rxmesh   the model's first LOD (RxModBikes' mesh layout): a helmet / goggles / neck brace as Alive stores it, in the space of
//                 the rider node it hangs on (Helmet, Goggles, NeckBrace); a suit / boots skinned over Alive's own rider hierarchy
//                 (Rider_M_Hierarchy: 55 nodes named as Reflex's MXRider_Hierarchy but another rig: its rest pose is written with the mesh,
//                 the client fits it to Reflex's rider: Content/RxGear.cs)
//   maps/*.png    the piece's textures (AliveBikeMaps' deterministic PNG)
// What Alive's tables hold (RiderGearType): Helmet, Goggles, Jersey (the whole suit: jersey and pants are one piece in Alive, it has no
// separate pants or gloves), Boots, Neckbrace - imported; Butt Patch (a texture on the pants) and Skin (the rider's skin tone) - not
// imported (Reflex's rider has its own skin tones and no butt patch). Nothing of Alive ships with the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Reflex.Port.Content.AliveImport
{
    /// One piece of Alive rider gear (a RiderGear row).
    public sealed class AliveGearPiece
    {
        public int gearId; public string slot = "", name = "", line = "", brand = "", pack = "";
        public (string db, string pkg) model; public (string db, string pkg)? materials;
    }

    /// The pieces that share one model (a product line's colours): one "gear" .rxmod folder.
    public sealed class AliveGearModel
    {
        public string slot = ""; public (string db, string pkg) model;
        public readonly List<AliveGearPiece> pieces = new List<AliveGearPiece>();
        public string Title => pieces.Count > 0 && pieces[0].line.Length > 0 ? pieces[0].line : pieces.Count > 0 ? pieces[0].name : model.pkg;
        public string Slug => "alive-" + slot.Replace('_', '-') + "-" + new string(model.pkg.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-').Replace("--", "-");
    }

    public sealed class AliveGearResult
    {
        public AliveGearModel model; public bool ok; public string error, folder;
        public int pieces, maps; public long bytes; public double seconds;
        public readonly List<string> notes = new List<string>();
    }

    public static partial class AliveBikes
    {
        public const string GearFolder = "AliveGear";

        /// Alive's RiderGearType FactoryName -> the gear slot (RxGear / ReflexVehicleSpecs' part keys); null: not imported.
        static string GearSlot(string factory)
        {
            switch (factory)
            {
                case "Helmet": return "helmet";
                case "Goggle": return "goggles";
                case "Jersey": return "jersey";
                case "Boots": return "boots";
                case "Neckbrace": return "neck_brace";
                default: return null;   // (ButtPatch, SkinTone, ChestProtector: Alive has no chest protector rows)
            }
        }

        /// The gear models of the sources with their pieces (the tables joined), by slot and line.
        public static List<AliveGearModel> FindGear(AliveSources r, CancellationToken cancel)
        {
            var t = ReadTables(r, cancel);
            var index = new PackageIndex(r.volumes);
            string Str(long? id) => id is long x && t.strings.TryGetValue("STR_" + x, out var s) ? s : null;
            Dictionary<long, Dictionary<string, object>> By(string table, string key) => t.Of(table).Where(x => Tables.L(x, key) != null).GroupBy(x => Tables.L(x, key).Value).ToDictionary(g => g.Key, g => g.Last());
            var products = By("StoreProducts", "StoreProductId"); var assets = By("StoreAssets", "StoreAssetId"); var types = By("RiderGearType", "RiderGearTypeId");
            var brands = By("Brand", "BrandId"); var pkgs = By("AssetPackage", "PackageId"); var dbs = By("AssetDatabase", "DatabaseId"); var pkgDb = By("AssetPackageDatabase", "PackageDatabaseId");
            (string db, string pkg)? PkgOf(long? pdb)
            {
                if (!(pdb is long x) || !pkgDb.TryGetValue(x, out var row)) return null;
                string p = pkgs.TryGetValue(Tables.L(row, "PackageId") ?? -1, out var pr) ? Tables.S(pr, "PackageName") : null, d = dbs.TryGetValue(Tables.L(row, "DatabaseId") ?? -1, out var dr) ? Tables.S(dr, "DatabaseName") : null;
                return p != null && d != null ? (d, p) : ((string, string)?)null;
            }
            var models = new Dictionary<string, AliveGearModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in t.Of("RiderGear").Where(x => Tables.L(x, "RiderGearId") != null).GroupBy(x => Tables.L(x, "RiderGearId").Value).Select(x => x.Last()).OrderBy(x => Tables.L(x, "RiderGearId")))
            {
                cancel.ThrowIfCancellationRequested();
                if (!types.TryGetValue(Tables.L(g, "RiderGearTypeId") ?? -1, out var ty)) continue;
                string slot = GearSlot(Tables.S(ty, "FactoryName"));
                var model = PkgOf(Tables.L(g, "ModelPackageDatabaseId"));
                if (slot == null || model == null || !index.Has(model.Value.db)) continue;
                long? pid = assets.TryGetValue(Tables.L(g, "StoreAssetId") ?? -1, out var arow) ? Tables.L(arow, "StoreProductId") : null;
                products.TryGetValue(pid ?? -1, out var prow);
                if (prow == null || Tables.L(prow, "Hidden") == 1) continue;
                string name = (Str(pid) ?? "").Trim();
                if (name.Length == 0 || name == "Unknown Store Product" || name.StartsWith("DO NOT", StringComparison.OrdinalIgnoreCase)) continue;
                var p = new AliveGearPiece { gearId = (int)Tables.L(g, "RiderGearId").Value, slot = slot, name = name, model = model.Value, materials = PkgOf(Tables.L(g, "MaterialPackageDatabaseId")), pack = index.SourceOf(model.Value.db) ?? "" };
                long parent = Tables.L(prow, "ParentStoreProductId") ?? -1;
                if (parent >= 0) p.line = (Str(parent) ?? "").Trim();
                if (brands.TryGetValue(Tables.L(prow, "BrandId") ?? -1, out var br) && Tables.S(br, "BrandName") is string bn && !bn.StartsWith(".", StringComparison.Ordinal) && bn != "None") p.brand = bn;
                string key = model.Value.db + "/" + model.Value.pkg;
                if (!models.TryGetValue(key, out var m)) models[key] = m = new AliveGearModel { slot = slot, model = model.Value };
                m.pieces.Add(p);
            }
            return models.Values.OrderBy(m => m.slot, StringComparer.Ordinal).ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.model.pkg, StringComparer.Ordinal).ToList();
        }

        /// Converts one gear model with its pieces into a "gear" .rxmod folder at `folder` (created; must not exist).
        public static AliveGearResult ConvertGear(AliveGearModel g, AliveSources sources, string folder, CancellationToken cancel, IEnumerable<AliveGearModel> all = null)
        {
            var index = new PackageIndex(sources.volumes);
            return ConvertGear(g, index, folder, cancel, HeadBox(index, all ?? new[] { g }, cancel));
        }

        static AliveGearResult ConvertGear(AliveGearModel g, PackageIndex index, string folder, CancellationToken cancel, float[] headBox)
        {
            var res = new AliveGearResult { model = g, folder = folder };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { GearInto(g, index, folder, res, cancel, g.slot == "helmet" || g.slot == "goggles" ? headBox : null); res.ok = true; }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is AliveImportException || e is InvalidDataException || e is IOException || e is ArgumentException || e is IndexOutOfRangeException)
            { res.error = e.Message; try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } }
            res.seconds = clock.Elapsed.TotalSeconds;
            return res;
        }

        /// The bounds (min x y z, max x y z) of Alive's reference helmet in Alive's rest space: what the client lays on a Reflex helmet's to place
        /// Alive's helmets and goggles (RxGearRig.Head). The first helmet model of the sources by package name; null: none.
        static float[] HeadBox(PackageIndex index, IEnumerable<AliveGearModel> models, CancellationToken cancel)
        {
            var hp = index.Read("Rider", "Rider", cancel); var hb = hp?.Find("Rider_M_Hierarchy", "model");
            if (hb == null) return null;
            var rig = Rfx.ParseModel(Converters.Model(hb));
            foreach (var g in models.Where(m => m.slot == "helmet").OrderBy(m => m.model.pkg, StringComparer.Ordinal))
            {
                var pa = index.Read(g.model.db, g.model.pkg, cancel);
                string mn = pa?.Names("model").FirstOrDefault(n => n.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0);
                if (mn == null) continue;
                Rfx.Model model; Rfx.Surface[] surfaces;
                try { model = ReadModel(pa, mn, out surfaces); } catch (AliveImportException) { continue; }
                int node = rig.NodeIndex(model.nodes[0].name); if (node < 0) continue;
                var w = rig.nodes[node].world;
                var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                foreach (var bd in model.Lod(0))
                {
                    if (bd.material.IndexOf("Strap", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    foreach (var v in surfaces[bd.surface].positions) { var p = w.MultiplyPoint3x4(v); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                }
                if (hi.y > lo.y) return new[] { lo.x, lo.y, lo.z, hi.x, hi.y, hi.z };
            }
            return null;
        }

        static void GearInto(AliveGearModel g, PackageIndex index, string folder, AliveGearResult res, CancellationToken cancel, float[] headBox)
        {
            var pa = index.Read(g.model.db, g.model.pkg, cancel) ?? throw new AliveImportException($"{g.model.db}/{g.model.pkg} is not in the sources");
            string mn = pa.Names("model").FirstOrDefault(n => n.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0) ?? throw new AliveImportException($"{g.model.db}/{g.model.pkg} has no model");
            var model = ReadModel(pa, mn, out var surfaces);
            // the rig the mesh is written on: a skinned model's own copy of Alive's rider hierarchy; a rigid one (a single node: Helmet, Goggles,
            // NeckBrace) on Alive's rider hierarchy itself, so the client has the rest pose the piece was made for (Content/RxGearRig.cs)
            var rig = model;
            if (model.nodes.Length == 1)
            {
                var hp = index.Read("Rider", "Rider", cancel);
                var hb = hp?.Find("Rider_M_Hierarchy", "model") ?? throw new AliveImportException("Rider/Rider (Rider_M_Hierarchy: Alive's rider) is not in the sources");
                rig = Rfx.ParseModel(Converters.Model(hb));
                Bin.Require(rig.NodeIndex(model.nodes[0].name) >= 0, $"the gear hangs on {model.nodes[0].name}, not a node of Alive's rider");
            }
            // the first LOD; the rider's own head / skin surfaces of a suit are Reflex's (not written)
            var parts = new List<Part>();
            foreach (var bd in model.Lod(0))
            {
                if (bd.material.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 || bd.material.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var s = surfaces[bd.surface];
                parts.Add(new Part { s = Place(s, Matrix4x4.identity, model, rig), node = Math.Max(0, rig.NodeIndex(model.nodes[bd.node].name)), binding = bd.material, model = pa, from = g.model.pkg });
            }
            Bin.Require(parts.Count > 0, "the gear model has no surfaces");
            Directory.CreateDirectory(folder);
            var names = parts.Select(p => p.binding).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var mesh = WriteMesh(rig, parts, names, p => names.FindIndex(n => string.Equals(n, p.binding, StringComparison.OrdinalIgnoreCase)), null);
            File.WriteAllBytes(Path.Combine(folder, "gear.rxmesh"), mesh);
            res.bytes += mesh.Length;

            var bikeRes = new AliveBikeResult();
            var o = new MapOut { folder = folder, res = bikeRes, dedupe = true, count = (n, bytes) => { res.maps += n; res.bytes += bytes; } };
            var cache = new Dictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);
            var x = new BikeBuild { index = index, cancel = cancel, res = bikeRes, chassis = pa };
            var none = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            var piecesJson = new List<object>();
            foreach (var p in g.pieces)
            {
                cancel.ThrowIfCancellationRequested();
                var own = p.materials != null ? index.Read(p.materials.Value.db, p.materials.Value.pkg, cancel) : null;
                if (own == null) { res.notes.Add($"{p.name}: its materials {p.materials?.db}/{p.materials?.pkg} are not in the sources (left out)"); continue; }
                var pool = new List<PackageAssets> { own };
                var mats = new JObj();
                foreach (string binding in names)
                {
                    var m = ResolveOption(cache, binding, pa, pool);
                    if (m.pkg != own) continue;   // (a binding the piece has no material for: the goggle strap on a helmet takes the goggles': the client's)
                    var e = GearMaterial(x, o, m, pool, p.slot);
                    if (e != null) mats.Set(binding, e);
                }
                if (mats.items.Count == 0) { res.notes.Add($"{p.name}: no material of {own.pkg} fits the model's bindings ({string.Join(", ", names)}) (left out)"); continue; }
                var j = new JObj().Set("id", p.gearId.ToString()).Set("slot", p.slot).Set("name", p.name);
                if (p.line.Length > 0) j.Set("line", p.line);
                if (p.brand.Length > 0) j.Set("brand", p.brand);
                j.Set("mesh", "gear.rxmesh").Set("materials", mats);
                piecesJson.Add(j);
                res.pieces++;
            }
            foreach (var n in bikeRes.notes) res.notes.Add(n);
            Bin.Require(piecesJson.Count > 0, "no piece of the model could be converted");
            var manifest = new JObj().Set("format", "reflex-mod").Set("version", 1L).Set("type", "gear").Set("id", g.Slug)
                .Set("name", g.Title).Set("author", Author)
                .Set("description", $"{g.Title} ({g.slot.Replace('_', ' ')}) from MX vs ATV Alive, imported from your own copy")
                .Set("rig", "alive");
            if (headBox != null) manifest.Set("headBox", headBox.Select(v => (object)(double)v).ToList());
            manifest.Set("gear", piecesJson);
            var text = Encoding.UTF8.GetBytes(Json.Dumps(manifest, 1) + "\n");
            File.WriteAllBytes(Path.Combine(folder, "mod.json"), text);
            res.bytes += text.Length;
        }

        /// A gear material's entry: its role (the Reflex gear program it borrows: "jersey", "helmet", "boots", "goggles", "lens", "strap",
        /// "neck_brace") and its maps. Alive's gear programs colour an area at run time (usr_jerseydyncolor ...: the diffuse's alpha is the
        /// area, as on the plastics): baked into the diffuse with the material's own colour.
        static JObj GearMaterial(BikeBuild x, MapOut o, Mat m, List<PackageAssets> pool, string slot)
        {
            string ps = m.def.pixelShader ?? "", n = m.def.name ?? "";
            bool P(string s) => ps.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            string role = P("Lens") || n.IndexOf("Lens", StringComparison.OrdinalIgnoreCase) >= 0 ? "lens"
                        : n.IndexOf("Strap", StringComparison.OrdinalIgnoreCase) >= 0 ? "strap"
                        : slot == "jersey" ? "jersey" : slot == "helmet" ? "helmet" : slot == "boots" ? "boots" : slot == "goggles" ? "goggles" : "neck_brace";
            // the maps by name (Alive's gear naming): the name / number plate, the butt patch, the mud noise and the video placeholder are not the piece's
            string diffuse = null, sfx = null, normal = null;
            foreach (string t in m.def.textures)
            {
                if (string.IsNullOrEmpty(t)) continue;
                bool H(string s) => t.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
                if (H("Mud_Noise") || H("NamNum") || H("Bpatch") || H("VideoReplace") || H("Reflect") || H("SpecStudio") || H("Editor_")) continue;
                if (H("_SFX") || H("Combo")) { sfx ??= t; continue; }
                if (H("_NRM") || H("Nrml") || H("_Norm") || t.EndsWith("_N", StringComparison.OrdinalIgnoreCase)) { normal ??= t; continue; }
                diffuse ??= t;
            }
            string prefix = m.pkg.pkg + ".";
            var e = new JObj().Set("role", role).Set("program", ps);
            var D = x.Pixels(o, m, diffuse, pool, true); var C = x.Pixels(o, m, sfx, pool, true);
            float[] dyn = m.def.parameters.Where(kv => kv.Key.IndexOf("dyncolor", StringComparison.OrdinalIgnoreCase) >= 0).Select(kv => kv.Value).FirstOrDefault();
            string d = null;
            if (D != null)
            {
                bool alpha = false; var px = D.Value.px; for (int i = 3; i < px.Length; i += 4) if (px[i] != 255) { alpha = true; break; }
                d = alpha && dyn != null && role != "lens" ? o.Write(prefix + m.key + "_diffuse", AliveBikeMaps.BakePlastic(D.Value, null, dyn, null)) : o.Write(prefix + diffuse, D);
            }
            if (d != null) e.Set("diffuse", d);
            string s = C != null ? o.Write(prefix + sfx + "_" + role, GearSfx(C.Value, role)) : null; if (s != null) e.Set("sfx", s);
            string nm = o.Write(prefix + normal, x.Pixels(o, m, normal, pool, true)); if (nm != null) e.Set("normal", nm);
            if (m.def.textures.Any(t => t.IndexOf("NamNum", StringComparison.OrdinalIgnoreCase) >= 0)) e.Set("plate", true);
            foreach (var kv in m.def.parameters)
                if (kv.Key.IndexOf("lens_color", StringComparison.OrdinalIgnoreCase) >= 0 || kv.Key.IndexOf("opacity", StringComparison.OrdinalIgnoreCase) >= 0 || kv.Key.IndexOf("dyncolor", StringComparison.OrdinalIgnoreCase) >= 0 || kv.Key == "usr_color")
                    e.Set(kv.Key, kv.Value.Select(v => (object)(double)v).ToList());
            if (m.def.alphaTest) e.Set("alphaTest", true).Set("alphaRef", (long)Math.Max(1, Math.Min(255, m.def.alphaRef)));
            if (m.def.alphaBlend) e.Set("alphaBlend", true);
            if (m.def.cull == 1) e.Set("twoSided", true);
            return d == null && s == null && role != "lens" ? null : e;
        }

        /// Alive's gear SFX map (R the shading / shine, G the mud mask, B unused: 0) as the Reflex program of the role reads its own
        /// (measured on the stock pieces: G the mud mask in both):
        ///   jersey   Reflex R the folds' shine (mean 0.09), B 1; Alive's R is the cloth's soft shading (mean 0.7: fed as shine it washed the
        ///            suit out): R x 0.12, B 1
        ///   helmet   Reflex B the reflection, equal to R (the shell): B := R
        ///   boots    Reflex B a constant 0.44; goggles: B 1; a neck brace as a helmet
        static (int w, int h, byte[] px) GearSfx((int w, int h, byte[] px) c, string role)
        {
            var o = new byte[c.px.Length];
            for (int i = 0; i < c.w * c.h; i++)
            {
                byte r = c.px[i * 4], g = c.px[i * 4 + 1], b;
                switch (role)
                {
                    case "jersey": r = (byte)(r * 12 / 100); b = 255; break;
                    case "boots": b = 112; break;
                    case "goggles": case "strap": b = 255; break;
                    default: b = r; break;
                }
                o[i * 4] = r; o[i * 4 + 1] = g; o[i * 4 + 2] = b; o[i * 4 + 3] = 255;
            }
            return (c.w, c.h, o);
        }

        public static string GearRoot(string content) => Path.Combine(content, GearFolder);
        public static bool GearInstalled(string content, AliveGearModel g) => File.Exists(Path.Combine(GearRoot(content), g.Slug, "mod.json"));

        /// Converts and installs the gear models into <content>/AliveGear/<slug> (each staged, then moved in; one imported before by this
        /// importer is replaced). A failed model leaves nothing behind.
        public static List<AliveGearResult> ImportGear(IEnumerable<AliveGearModel> models, AliveSources sources, string content, Action<string> status, CancellationToken cancel, IEnumerable<AliveGearModel> all = null)
        {
            models = models.ToList();
            content = Path.GetFullPath(content);
            string root = GearRoot(content);
            Directory.CreateDirectory(root);
            Bin.Require(!ReflexFs.IsLink(root), "Would write through a symbolic link: " + root);
            var index = new PackageIndex(sources.volumes);
            var headBox = HeadBox(index, all ?? models, cancel);   // (one reference for every helmet and goggle: Alive's goggles keep their place in Alive's helmets)
            var results = new List<AliveGearResult>();
            foreach (var g in models)
            {
                if (cancel.IsCancellationRequested) break;
                status?.Invoke("Converting " + g.Title);
                string stage = Path.Combine(root, ".ag-" + Guid.NewGuid().ToString("N").Substring(0, 8)), dest = Path.Combine(root, g.Slug);
                AliveGearResult res;
                try
                {
                    res = ConvertGear(g, index, stage, cancel, headBox);
                    if (res.ok)
                    {
                        string manifest = Path.Combine(root, g.Slug + ".import.json");
                        if (Directory.Exists(dest))
                        {
                            Bin.Require(File.Exists(manifest), g.Title + ": " + dest + " exists and was not made by this importer; remove it first");
                            Directory.Delete(dest, true);
                        }
                        Directory.Move(stage, dest);
                        res.folder = dest;
                        var files = Directory.GetFiles(dest, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                            .Select(f => (object)new JObj().Set("path", f.Substring(dest.Length + 1).Replace('\\', '/')).Set("sha256", LevelInstaller.Sha256(f))).ToList();
                        var m = new JObj().Set("version", 1L).Set("name", g.Title).Set("slot", g.slot).Set("pieces", (long)res.pieces)
                            .Set("imported_utc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)).Set("files", files);
                        File.WriteAllText(manifest, Json.Dumps(m, 1) + "\n", new UTF8Encoding(false));
                    }
                }
                catch (OperationCanceledException) { res = new AliveGearResult { model = g, error = "Cancelled" }; results.Add(res); break; }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is AliveImportException) { res = new AliveGearResult { model = g, error = e.Message }; }
                finally { try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (Exception) { } }
                results.Add(res);
            }
            return results;
        }
    }
}
