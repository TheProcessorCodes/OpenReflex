// The player's rider, built at run time from the player's own Reflex install: the MXRider_Hierarchy skeleton (69 nodes) and the
// gear models on it, each binding's material resolved as the game does (the outfit's material packages, the skin tone).
// Ported from _re/formats/rider_asset_export.py (parts, bone palettes -> hierarchy node indices, UBYTE4N weights) and
// Editor/OriginalWorld/RiderAssetBuilder (bones[i] == hierarchy node i, bindposes[i] = inverse(bindWorld[i]) in Unity's column
// convention; rigid helmet / goggles / neck brace hang on nodes 35 / 36 / 7). OriginalRider poses it from reflex_world_rider_pose, and
// OriginalOnboardCamera reads the head / eye bones (31, 32, 33) and the bone-weight dominant bones for the cockpit view.
// The gear the captured world uses is the default set of MXTables_Default (RIDER_FORMAT.md): the Answer red outfit, white skin.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Reflex.Port.OriginalWorld;

namespace Reflex.Port.Content
{
    public sealed class RiderSpec
    {
        public sealed class Part
        {
            public string key, modelDb, modelPkg, asset, matsDb, matsPkg;
            /// A "gear" .rxmod piece (Content/RxGear.cs: the MX vs ATV Alive gear) drawn in this part's place (a suit: the stock model's gloves and skin stay); null: none.
            public RxGearPiece rx;
        }
        public string hierarchyDb = "Rider", hierarchyPkg = "Rider", hierarchyAsset = "MXRider_Hierarchy";
        public string skinDb = "Rider_Skin_Materials", skinPkg = "Rider_Skin_White";
        public List<Part> parts = new List<Part>();
        /// Material packages of pieces without a model (offered to the material override with the outfit's).
        public List<(string db, string pkg)> extraMaterials = new List<(string db, string pkg)>();
        public int[] gearIds;   // the RiderGear ids it was made from (ReflexVehicleSpecs.Rider), null for a fixed outfit
        /// The AIRiderId whose Name / Number the plates carry (a pro rider on the stage); -1: the profile's own rider (not part of Key: the plates are separate textures).
        public int plateRider = -1;
        /// Equal keys build equal riders.
        public string Key => string.Join("|", hierarchyDb, hierarchyPkg, hierarchyAsset, skinDb, skinPkg,
            string.Join(",", parts.Select(p => $"{p.key}:{p.modelDb}/{p.modelPkg}/{p.asset}:{p.matsDb}/{p.matsPkg}" + (p.rx == null ? "" : ":rx=" + p.rx.Key))), string.Join(",", extraMaterials.Select(p => p.db + "/" + p.pkg)));

        /// The Answer red outfit with white skin (RIDER_FORMAT.md section 1).
        public static RiderSpec AnswerRed()
        {
            var s = new RiderSpec();
            void Add(string key, string mdb, string mpkg, string asset, string tdb, string tpkg) => s.parts.Add(new Part { key = key, modelDb = mdb, modelPkg = mpkg, asset = asset, matsDb = tdb, matsPkg = tpkg });
            Add("jersey", "Rider_Jerseys_Models", "Jerseys_BaseModel", "MXRider_Jersey", "Rider_Jerseys_Materials", "Jersey_Answer_Alpha_Red");
            Add("helmet", "Rider_Helmets_Models", "Helmet_Answer", "MXRider_Helmet_Answer", "Rider_Helmets_Materials", "Helmet_Answer_Alpha_Red");
            Add("goggles", "Rider_Goggles_Models", "Goggles_Answer", "MXRider_Goggles_Answer", "Rider_Goggles_Materials", "Goggles_Answer_Red");
            Add("boots", "Rider_Boots_Models", "Boots_Answer", "MXRider_Boots_Answer", "Rider_Boots_Materials", "Boots_Answer_White");
            Add("chest_protector", "Rider_CPs_Models", "CPs_Answer", "MXRider_Chstptr_MSR", "Rider_CPs_Materials", "ChestProtector_Answer_Red");
            return s;
        }
    }

    public sealed partial class ReflexRiderModel
    {
        public GameObject root;
        public OriginalRider rider;
        /// The fallback capsule rider (shown until the game's pose arrives) and the object that follows rider body 1.
        public Transform proxyRoot; public GameObject proxyArt;
        public readonly List<(string name, string material, string resolved)> parts = new List<(string, string, string)>();

        public void Destroy()
        {
            foreach (var r in new[] { root, proxyRoot ? proxyRoot.gameObject : null })
                if (r)
                {
                    foreach (var f in r.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh) UnityEngine.Object.Destroy(f.sharedMesh);
                    foreach (var s in r.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.sharedMesh) UnityEngine.Object.Destroy(s.sharedMesh);
                    foreach (var rr in r.GetComponentsInChildren<Renderer>(true)) foreach (var m in rr.sharedMaterials) if (m) UnityEngine.Object.Destroy(m);
                    UnityEngine.Object.Destroy(r);
                }
        }

        /// The capsule stand-in of the rider (body 1 of the game's physics), shown until the original rider pose arrives.
        public static ReflexRiderModel BuildProxy()
        {
            var r = new ReflexRiderModel();
            var proxyRoot = new GameObject("Rider body 1"); r.proxyRoot = proxyRoot.transform;
            var mat = ReflexObjectShading.Fallback(new Color(0.72f, 0.76f, 0.81f), "Rider proxy");
            var art = new GameObject("Rider PROXY capsule (fallback)"); art.transform.SetParent(proxyRoot.transform, false);
            void Prim(string name, PrimitiveType type, Vector3 p, Vector3 s)
            {
                var item = GameObject.CreatePrimitive(type); item.name = name; item.transform.SetParent(art.transform, false);
                item.transform.localPosition = p; item.transform.localScale = s;
                UnityEngine.Object.Destroy(item.GetComponent<Collider>()); item.GetComponent<Renderer>().sharedMaterial = mat;
            }
            Prim("Torso proxy", PrimitiveType.Capsule, new Vector3(0, 0.45f, 0), new Vector3(0.85f, 1.15f, 0.65f));
            Prim("Helmet proxy", PrimitiveType.Sphere, new Vector3(0, 1.8f, 0.2f), Vector3.one * 0.82f);
            r.proxyArt = art;
            return r;
        }

        /// The rider's high-res twins (the garage's: "HR_<db>/HR_<pkg>", the skin tones "HR_<db>/<pkg>"; the hierarchy stays Rider/Rider), its
        /// parts parallel to rs's. Null: no gear model of the rider has a twin installed. (From FrontEnd/FeGarage, 2026-11-16: a race takes
        /// them too with BIKE AND RIDER DETAIL HIGH.)
        public static RiderSpec HighRes(RiderSpec rs)
        {
            if (rs == null) return null;
            var install = VehicleCatalog.Shared.install;
            (string db, string pkg) Twin(string db, string pkg)
            {
                // (a pack's package keeps its namespace: "mod:kit/<hash>>RiderDLC010_graphicskit" -> "mod:kit/<hash>>HR_RiderDLC010_graphicskit", KitPacks)
                string ns = BikeMods.NamespaceOf(db), bare = BikeMods.BaseDb(db);
                if (db == null || pkg == null || bare.StartsWith("HR_", StringComparison.Ordinal)) return (db, pkg);
                if (install.Pkg(ns + "HR_" + bare, "HR_" + pkg) != null) return (ns + "HR_" + bare, "HR_" + pkg);
                if (install.Pkg(ns + "HR_" + bare, pkg) != null) return (ns + "HR_" + bare, pkg);
                return (db, pkg);
            }
            var o = new RiderSpec { hierarchyDb = rs.hierarchyDb, hierarchyPkg = rs.hierarchyPkg, hierarchyAsset = rs.hierarchyAsset, gearIds = rs.gearIds };
            (o.skinDb, o.skinPkg) = Twin(rs.skinDb, rs.skinPkg);
            bool any = false;
            foreach (var p in rs.parts)
            {
                var m = Twin(p.modelDb, p.modelPkg); var t = Twin(p.matsDb, p.matsPkg);
                any |= m.db != p.modelDb;
                string asset = p.asset != null && m.db != p.modelDb && install.Pkg(m.db, m.pkg)?.Find("HR_" + p.asset, "model") != null ? "HR_" + p.asset : m.db != p.modelDb ? null : p.asset;
                o.parts.Add(new RiderSpec.Part { key = p.key, modelDb = m.db, modelPkg = m.pkg, asset = asset, matsDb = t.db, matsPkg = t.pkg, rx = p.rx });   // (rx: a "gear" .rxmod piece stays on the high-res rider: the copy dropped it, so an Alive suit worn with the game's helmet showed the game's jersey)
            }
            foreach (var e in rs.extraMaterials) o.extraMaterials.Add(Twin(e.db, e.pkg));
            return any ? o : null;
        }

        /// The skeleton and the gear, unparented at the origin in the bind pose. proxy: shown by OriginalRider until a pose arrives.
        /// lods (the other riders of a race, OriginalOpponents): the gear models' further LOD records are kept, built and switched by distance (SelectLods).
        /// highRes (a race with BIKE AND RIDER DETAIL HIGH; the garage passes HighRes's spec itself): the gear's high-res twins, on the same
        /// 69-node skeleton the core poses. highRes + lods: a twin is LOD 0 and its race model's further LOD records follow at their own
        /// distances, with the twin's materials (the mud and the name / number plates reach them).
        public static ReflexRiderModel Build(RiderSpec spec = null, GameObject proxy = null, bool lods = false, bool highRes = false)
        {
            spec ??= RiderSpec.AnswerRed();
            var far = spec;   // (the race gear: the further LODs of a twin)
            if (highRes)
            {
                RiderSpec hr = null;
                try { hr = HighRes(spec); } catch (Exception e) when (e is IOException || e is InvalidDataException) { Debug.LogWarning("Reflex rider: high-res gear: " + e.Message); }
                if (hr != null && hr.parts.Count == spec.parts.Count)
                    try { var r = Build(hr, far, proxy, lods); r.highRes = true; return r; }
                    catch (Exception e) when (e is IOException || e is InvalidDataException) { Debug.LogWarning("Reflex rider: high-res gear: " + e.Message + " (the race gear)"); }
            }
            return Build(spec, spec, proxy, lods);
        }
        /// The gear drawn is the high-res twins' (Build's highRes found them).
        public bool highRes;

        static ReflexRiderModel Build(RiderSpec spec, RiderSpec far, GameObject proxy, bool lods)
        {
            var cat = VehicleCatalog.Shared; var install = cat.install;
            var result = new ReflexRiderModel();

            // ---- skeleton (MXRider_Hierarchy: 69 nodes) ----
            var hp = install.Pkg(spec.hierarchyDb, spec.hierarchyPkg); Rfx.Check(hp != null, "the rider hierarchy package");
            using var perfAll = Reflex.Port.Platform.ReflexMenuPerf.Measure("rider model built");
            var hier = Rfx.ParseModel(install.Read(hp.Find(spec.hierarchyAsset, "model") ?? throw new InvalidDataException("MXRider_Hierarchy missing")));
            int count = hier.nodes.Length;
            Rfx.Check(count == OriginalRider.OriginalBoneCount, "the rider hierarchy has 69 nodes");
            var root = new GameObject("Original rider (MXRider_Hierarchy, original animation)");
            result.root = root;
            var skeletonRoot = new GameObject("Skeleton"); skeletonRoot.transform.SetParent(root.transform, false);
            var bones = new Transform[count]; var parents = new int[count]; var bindWorld = new float[count * 16]; var bindposes = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                var n = hier.nodes[i];
                var go = new GameObject(n.name);
                parents[i] = n.parent;
                go.transform.SetParent(n.parent < 0 ? skeletonRoot.transform : bones[n.parent], false);
                var w = n.world;   // Unity column matrix; the game's row-major row-vector layout is its transpose
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) bindWorld[i * 16 + r * 4 + c] = w[c, r];
                Vector3 right = w.GetColumn(0), up = w.GetColumn(1), fwd = w.GetColumn(2), pos = w.GetColumn(3);
                // mesh nodes (Jersey / Gloves / Boots) carry a uniform scale: identity rotation when the basis is not unit length
                bool rigid = Mathf.Abs(right.magnitude - 1) < 1e-3f && Mathf.Abs(up.magnitude - 1) < 1e-3f && Mathf.Abs(fwd.magnitude - 1) < 1e-3f;
                go.transform.SetPositionAndRotation(pos, rigid ? Quaternion.LookRotation(fwd, up) : Quaternion.identity);
                bones[i] = go.transform;
                bindposes[i] = w.inverse;
            }

            // ---- gear ----
            var chosen = spec.parts.Where(p => p.matsPkg != null).Select(p => (p.matsDb, p.matsPkg)).Concat(spec.extraMaterials).Append((spec.skinDb, spec.skinPkg)).ToList();
            var materials = new Dictionary<string, UnityEngine.Material>();
            var skinned = new List<SkinnedMeshRenderer>(); var rigid2 = new List<Renderer>();
            var byDef = new Dictionary<VehicleMaterialDef, UnityEngine.Material>();   // (a twin's far LODs take the twin's material of the same definition)
            for (int pi = 0; pi < spec.parts.Count; pi++)
            {
                var part = spec.parts[pi];
                if (part.rx != null)
                {   // a "gear" .rxmod piece (ReflexRiderModel.RxGear.cs); a suit keeps Reflex's own gloves and skin of the stock model below
                    try { BuildRxGear(result, spec, part, hier, bones, bindposes, root, cat, install, chosen, skinned, rigid2); }
                    catch (Exception e) when (e is IOException || e is InvalidDataException) { Debug.LogWarning($"Reflex rider: {part.rx.name}: {e.Message} (the stock piece)"); part.rx = null; }
                    if (part.rx != null && (part.rx.slot != "jersey" || part.modelPkg == null)) continue;
                }
                var pk = install.Pkg(part.modelDb, part.modelPkg); Rfx.Check(pk != null, part.modelDb + "/" + part.modelPkg);
                // a gear package holds one model (the tables name the package): the spec's asset, else that model
                string asset = part.asset ?? pk.OfType("model").Select(a => a.name).FirstOrDefault(n => n.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0)
                               ?? throw new InvalidDataException(part.modelDb + "/" + part.modelPkg + ": no gear model");
                var (model, surfaces) = LoadGear(install, pk, asset);
                // the model's own nodes -> the rider hierarchy's, by name (skinned weights index the model's nodes)
                var map = model.nodes.Select(n => hier.NodeIndex(n.name)).ToArray();
                bool identity = map.Length <= count && map.Select((h, i) => h == i).All(x => x);
                // a twin's further LODs: the race gear's model of this part (its LOD records 1 ..), null: the model's own
                var hrModel = model; var hrSurfaces = surfaces; var hrMap = map; bool hrIdentity = identity; string hrAsset = asset;
                Rfx.Model farModel = null; Rfx.Surface[] farSurfaces = null; int[] farMap = null; bool farIdentity = false; string farAsset = null;
                var farPart = !ReferenceEquals(far, spec) && pi < far.parts.Count ? far.parts[pi] : null;
                if (lods && farPart != null && farPart.rx == null && farPart.modelPkg != null && (farPart.modelDb != part.modelDb || farPart.modelPkg != part.modelPkg))
                    try
                    {
                        var fpk = install.Pkg(farPart.modelDb, farPart.modelPkg);
                        farAsset = farPart.asset ?? fpk?.OfType("model").Select(a => a.name).FirstOrDefault(n => n.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0);
                        if (fpk != null && farAsset != null)
                        {
                            (farModel, farSurfaces) = LoadGear(install, fpk, farAsset);
                            farMap = farModel.nodes.Select(n => hier.NodeIndex(n.name)).ToArray();
                            farIdentity = farMap.Length <= count && farMap.Select((h, i) => h == i).All(x => x);
                            if (farModel.lods.Length < 2) farModel = null;
                        }
                    }
                    catch (Exception e) when (e is IOException || e is InvalidDataException) { farModel = null; }
                Renderer[] GearParts(int lod)
                {
                  bool farLod = lod != 0 && farModel != null;
                  var model = farLod ? farModel : hrModel; var surfaces = farLod ? farSurfaces : hrSurfaces; var map = farLod ? farMap : hrMap; bool identity = farLod ? farIdentity : hrIdentity; string asset = farLod ? farAsset : hrAsset;
                  var made = new List<Renderer>();
                  foreach (var b in model.Lod(lod))
                  {
                    // (under a .rxmod suit only the stock model's gloves and skin are drawn)
                    if (part.rx != null && !(string.Equals(model.nodes[b.node].name, "Gloves", StringComparison.OrdinalIgnoreCase) || b.material.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    var s = surfaces[b.surface];
                    string name = $"{asset}_lod{lod}_s{b.surface}_{b.material}";
                    string key = $"{part.key}:{b.material}";
                    if (!materials.TryGetValue(key, out var mat))
                    {
                        // (cat.Resolve ignores "HR_": a race LOD's binding under a twin resolves over the twin's packages, to the twin's material)
                        var def = cat.Resolve(b.material, (part.modelDb, part.modelPkg), chosen);
                        if (def == null) Debug.LogWarning("Reflex rider: no material for binding " + b.material);
                        if (farLod && def != null && byDef.TryGetValue(def, out var shared)) materials[key] = mat = shared;
                        else
                        {
                            materials[key] = mat = cat.Build(def, key + (def != null ? " -> " + def.name : ""));
                            if (def != null && !byDef.ContainsKey(def)) byDef[def] = mat;
                            if (farLod) result.farOwnMaterials++;
                            result.parts.Add((name, b.material, def?.Key));
                        }
                    }
                    var go = new GameObject(name);
                    if (s.skinned)
                    {
                        go.transform.SetParent(root.transform, false);
                        var smr = go.AddComponent<SkinnedMeshRenderer>();
                        smr.sharedMesh = ReflexMeshes.Build(name, s, Matrix4x4.identity, true, bindposes, identity ? null : map);
                        smr.sharedMaterial = mat; smr.bones = bones; smr.rootBone = bones[0];
                        // fixed bounds about the root bone (a whole rider, arms and legs out), as the big fields have drawn since RIDER CAP: with
                        // updateWhenOffscreen every surface of every rider walked its 69 bones twice a frame (bounds, then skinning), in view
                        // or not: about a third of the main thread in a 12-rider race. A rider out of view (and out of every shadow) is not skinned.
                        smr.updateWhenOffscreen = false;
                        smr.localBounds = new Bounds(Vector3.zero, Vector3.one * RiderBoundsFeet);
                        if (lod == 0) skinned.Add(smr);
                        made.Add(smr);
                    }
                    else
                    {
                        // rigid: v_model * world[node]; the model's single node is named like its rider hierarchy node (Helmet 35, Goggles 36)
                        int node = hier.NodeIndex(model.nodes[b.node].name);
                        Rfx.Check(node >= 0, "rigid gear node " + model.nodes[b.node].name);
                        go.transform.SetParent(bones[node], false);
                        // in the posed node's frame only, as the game draws it (v * world[node]); the gear model's own node matrix is not used.
                        // The neck braces' (Leatt, Alpinestar, HR_) is the chest bone's bind frame, their vertices 0.26..1.07 up its x axis:
                        // where every clip of the install puts NeckBrace (local identity under MXRider_Chest; the core's pose has world[7] ==
                        // world[6]), not MXRider_Hierarchy's bind NeckBrace (turned 90 degrees), so placing the brace by the difference of the
                        // two turned it a second time, beside the shoulder. The other differences are leftovers the game ignores
                        // (Goggles_Thor_Ally's node is mirrored, Goggles_Spy's scaled 0.886, Helmet_One_Trooper's 0.963, Helmet_Fox_V2's tilted 2 deg)
                        go.AddComponent<MeshFilter>().sharedMesh = ReflexMeshes.Build(name, s, Matrix4x4.identity, false);
                        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; if (lod == 0) rigid2.Add(mr);
                        made.Add(mr);
                    }
                  }
                  return made.ToArray();
                }
                var lod0 = GearParts(0);
                var lodModel = farModel ?? model;
                if (lods && lodModel.lods.Length > 1 && lod0.Length > 0)
                {
                    var sets = new Renderer[lodModel.lods.Length][]; sets[0] = lod0;
                    result.lodGear.Add(new LodGear { name = asset, distances = lodModel.lods.Select(l => l.distance).ToArray(), sets = sets, build = GearParts });
                }
            }
            var rider = root.AddComponent<OriginalRider>();
            rider.skinnedMeshes = skinned.ToArray(); rider.skinnedMesh = skinned.FirstOrDefault();
            rider.rigidMeshes = rigid2.ToArray(); rider.bones = bones; rider.boneParents = parents; rider.bindWorldMatrices = bindWorld;
            rider.proxy = proxy;
            rider.ClearOriginalPose();   // proxy visible, the gear hidden until the game's pose arrives
            result.rider = rider;
            return result;
        }

        // ---- distance LODs (the other riders of a race): each gear model's further LOD records (Rfx.Model.lods; the stock gear: 0 / 40 / 80 or
        // 120 ft, the same bindings with about 1/2 and 1/5 of LOD 0's vertices), built the first time their distance is reached on the same
        // bones and with LOD 0's materials (the mud and the name / number plates written to them reach every LOD). A set not drawn has its
        // objects switched off (OriginalRider owns the renderers' `enabled`: the gear hidden until a pose arrives; the replay and the tests
        // own forceRenderingOff): it is neither skinned nor drawn.
        /// Diagnostics: the materials a twin's far LODs did not share with the twin (made for them).
        public int farOwnMaterials;
        sealed class LodGear { public string name; public int[] distances; public Renderer[][] sets; public Func<int, Renderer[]> build; public int current; }
        readonly List<LodGear> lodGear = new List<LodGear>();
        public bool HasLods => lodGear.Count > 0;
        /// Diagnostics: each gear model's LOD now drawn.
        public string LodState => string.Join(", ", lodGear.Select(m => m.name + " " + m.current));
        public int MaxLod { get { int k = 0; foreach (var m in lodGear) if (m.current > k) k = m.current; return k; } }

        /// Each gear model's LOD for a camera at `distance` (feet), as ReflexBikeModel.SelectLods. True: something changed.
        public bool SelectLods(float distance, float scale, float hysteresis)
        {
            bool changed = false;
            if (!root) return false;
            foreach (var m in lodGear)
            {
                int target = 0;
                for (int k = 1; k < m.distances.Length; k++)
                    if (m.distances[k] > 0 && distance >= m.distances[k] * scale * (k <= m.current ? 1f - hysteresis : 1f)) target = k;
                if (target == m.current) continue;
                if (m.sets[target] == null)
                {
                    try { m.sets[target] = m.build(target); }
                    catch (Exception e) when (e is InvalidDataException || e is IOException || e is ArgumentException || e is IndexOutOfRangeException)
                    {
                        Debug.LogWarning($"Reflex rider LOD: {m.name} LOD {target}: {e.Message}");
                        m.distances = m.distances.Take(target).ToArray();   // (the model keeps the LODs before it)
                        continue;
                    }
                    // (OriginalRider.SetProxyVisible's state: the gear is hidden until the game's pose arrives)
                    bool shown = m.sets[0].Length > 0 && m.sets[0][0] && m.sets[0][0].enabled;
                    foreach (var r in m.sets[target]) if (r) { r.enabled = shown; if (rider) rider.lodMeshes.Add(r); }   // (and from now on with the gear: OriginalRider.SetProxyVisible)
                }
                foreach (var r in m.sets[m.current]) if (r) r.gameObject.SetActive(false);
                foreach (var r in m.sets[target]) if (r) r.gameObject.SetActive(true);
                m.current = target; changed = true;
            }
            return changed;
        }

        /// The side of a rider's fixed skinned bounds, about the root bone (feet).
        public const float RiderBoundsFeet = 16f;
        static readonly Dictionary<string, (Rfx.Model, Rfx.Surface[])> parsed = new Dictionary<string, (Rfx.Model, Rfx.Surface[])>(StringComparer.OrdinalIgnoreCase);
        /// Frees the parsed gear models kept for further riders (a field of AI riders shares most of them).
        public static void ReleaseParsed() { lock (parsed) parsed.Clear(); lock (s_shaped) s_shaped.Clear(); }
        static (Rfx.Model, Rfx.Surface[]) LoadGear(Rfx.Install install, Rfx.Package pk, string asset)
        {
            string key = pk.database.name + "/" + pk.name + "/" + asset;
            lock (parsed) if (parsed.TryGetValue(key, out var r)) return r;
            using var perf = Reflex.Port.Platform.ReflexMenuPerf.Measure("model read and parse");
            var model = Rfx.ParseModel(install.Read(pk.Find(asset, "model") ?? throw new InvalidDataException(asset + ".model missing")));
            var surfaces = Rfx.ParseSurfaces(install.Read(pk.Find(asset, "surface") ?? pk.OfType("surface").FirstOrDefault() ?? throw new InvalidDataException(asset + ".surface missing")));
            Rfx.Check(surfaces.Length == model.surfaceCount, "model / surface counts: " + asset);
            lock (parsed) parsed[key] = (model, surfaces);
            return (model, surfaces);
        }
    }
}
