// A .rxmod bike (Content/RxModBikes.cs, core/plan/RXMOD.md) built like an original one: the original bike's MX_Hierarchy (the core poses
// it: steering, fork travel, swingarm, wheel spin, levers) moves the mod's parts, each by the hierarchy node its surface is bound to;
// the mod's mesh is fitted to that hierarchy at load (its rear axle on the original's, its front axle on the original's axle line, scaled
// to the original's wheelbase unless the bike says "fit": "keep"), and its wheels to the original's tyre radius, so the wheels sit
// where the physics rolls them. Its materials are the original bike's (program, constants, mud) with the mod's maps.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Reflex.Port.OriginalWorld;

namespace Reflex.Port.Content
{
    public sealed partial class ReflexBikeModel
    {
        /// An imported MX vs ATV Alive bike (the importer's AliveBikes folder; a bike's own folder carries no "importer", only its parts
        /// folder does, so RxMod.importer alone did not tell: 2026-11-16, the Alive bikes' riders had their feet moved to template pegs and
        /// their knees folded into the bike as they stood).
        static bool AliveImported(RxMod m)
        {
            if (m == null) return false;
            if (m.importer > 0) return true;
            string path = (m.path ?? "").Replace('\\', '/'), mark = "/" + AliveImport.AliveBikes.Folder;
            return path.IndexOf(mark + "/", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith(mark, StringComparison.OrdinalIgnoreCase);
        }

        /// What the last .rxmod build measured (diagnostics / tests): the fit's scale and pitch, the steering axis offset from the original's,
        /// the tyre radius corrections.
        public string rxFit;

        /// Reads the mod's mesh off the main thread (the garage's Prefetch).
        static void PrefetchRx(BikeSpec spec)
        {
            try
            {
                RxModBikes.Mesh(spec.rx); if (spec.rx.hr != null) RxModBikes.Mesh(spec.rx.hr); Load(VehicleCatalog.Shared.install, spec.chassisDb, spec.chassisPkg, spec.chassisModel);
                if (spec.rxParts != null) foreach (var o in spec.rxParts) if (o.mesh != null) RxModBikes.Mesh(o.mod, o.mesh);
            }
            catch (Exception) { }
        }

        /// The original tyre's radius (its tyre-material surface's half height), 0 when it cannot be read.
        static float TyreRadius(Rfx.Install install, BikeSpec spec, string model)
        {
            try
            {
                var t = Load(install, spec.tireDb, spec.tirePkg, model);
                float best = 0;
                foreach (var b in t.model.Lod(0))
                {
                    var p = t.surfaces[b.surface].positions; if (p.Length == 0) continue;
                    float lo = p.Min(v => v.y), hi = p.Max(v => v.y);
                    best = Mathf.Max(best, (hi - lo) * 0.5f);
                }
                return best;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException) { return 0; }
        }

        /// The mod's wheel radius: the largest distance from the axle (in the YZ plane) of its wheel node's vertices.
        static float ModWheelRadius(RxMesh mesh, int node)
        {
            if (node < 0) return 0;
            Vector3 a = mesh.world[node].GetColumn(3); float r = 0;
            foreach (var (s, _, n) in mesh.surfaces)
                if (n == node && !s.skinned) foreach (var v in s.positions) r = Mathf.Max(r, new Vector2(v.y - a.y, v.z - a.z).magnitude);
            return r;
        }

        /// The mod space -> the original's model space: the rear axles together, the axle lines together (a pitch about X), scaled to the
        /// original's wheelbase ("axles") or not ("keep"), centred on the original's axle x.
        static Matrix4x4 RxFit(RxMesh mesh, Rfx.Model hier, string fit, out float scale, out float pitch)
        {
            int mf = mesh.NodeIndex("OBJ_FRT_Tire"), mr = mesh.NodeIndex("OBJ_BCK_Tire"), hf = hier.NodeIndex("OBJ_FRT_Tire"), hr = hier.NodeIndex("OBJ_BCK_Tire");
            if (mf < 0 || mr < 0) throw new InvalidDataException("the mod's mesh has no OBJ_FRT_Tire / OBJ_BCK_Tire node (its axles)");
            if (hf < 0 || hr < 0) throw new InvalidDataException("the original hierarchy has no tyre nodes");
            Vector3 Fm = mesh.world[mf].GetColumn(3), Rm = mesh.world[mr].GetColumn(3), F = hier.nodes[hf].world.GetColumn(3), R = hier.nodes[hr].world.GetColumn(3);
            var dm = new Vector2(Fm.z - Rm.z, Fm.y - Rm.y); var d = new Vector2(F.z - R.z, F.y - R.y);
            if (dm.magnitude < 0.5f) throw new InvalidDataException("the mod's axles are " + dm.magnitude.ToString("F2") + " ft apart (a bike's are about 4.8 ft: is it in feet, Z forward?)");
            scale = fit == "keep" ? 1f : d.magnitude / dm.magnitude;
            pitch = Mathf.Atan2(d.y, d.x) - Mathf.Atan2(dm.y, dm.x);
            float c = Mathf.Cos(pitch) * scale, s = Mathf.Sin(pitch) * scale;
            var M = Matrix4x4.identity;   // x' = scale x; z' = c z - s y; y' = s z + c y
            M.m00 = scale; M.m11 = c; M.m12 = s; M.m21 = -s; M.m22 = c;
            var center = (F.x + R.x) * 0.5f;
            return Matrix4x4.Translate(new Vector3(center, R.y, R.z)) * M * Matrix4x4.Translate(new Vector3(-(Fm.x + Rm.x) * 0.5f, -Rm.y, -Rm.z));
        }

        /// The spec with the bike's high-res twin (RxModBike.hr: the garage's model of an imported MX vs ATV Alive bike) and the twins of the
        /// worn parts (the same slot and option ids). The spec itself when the bike has no twin, a worn part has none, or a skin paints the
        /// bike (a skin names the bike's own materials).
        public static BikeSpec RxHighRes(BikeSpec spec)
        {
            var hr = spec?.rx?.hr;
            if (hr == null || spec.rxSkin != null) return spec;
            var worn = new List<RxPartOption>();
            if (spec.rxParts != null)
                foreach (var o in spec.rxParts)
                {
                    var t = hr.slots.FirstOrDefault(s => string.Equals(s.id, o.slot.id, StringComparison.OrdinalIgnoreCase))?.Find(o.id);
                    if (t == null) return spec;
                    if (!t.isDefault) worn.Add(t);
                }
            var c = spec.Clone(); c.rx = hr; c.rxParts = worn;
            return c;
        }

        /// A twin's race model takes over beyond this distance from the camera (feet, times DRAW DISTANCE's scale as the original models'
        /// LOD distances are: the original bikes' first LOD record is at 40 ft).
        const int RxFarDistance = 40;

        /// farSpec (BIKE DETAIL HIGH: ALL RIDERS, the other riders of a race): spec is the bike's high-res twin and farSpec its race model,
        /// drawn instead beyond RxFarDistance (one further LOD, built when first reached: ReflexBikeModel.Lod.cs); null: spec at any distance.
        static ReflexBikeModel BuildRx(BikeSpec spec, BikeSpec farSpec = null, bool stand = true)
        {
            var cat = VehicleCatalog.Shared; var install = cat.install; var b = spec.rx;
            var mesh = RxModBikes.Mesh(b);
            var result = new ReflexBikeModel();
            // the original's hierarchy (the pose's nodes, in its order)
            var hierAsset = install.Pkg(spec.hierarchyDb ?? spec.chassisDb, spec.hierarchyPkg ?? spec.chassisPkg)?.Find(spec.hierarchyModel, "model")
                            ?? throw new InvalidDataException($"{spec.chassisDb}/{spec.chassisPkg}: no {spec.hierarchyModel}");
            var hier = Rfx.ParseModel(install.Read(hierAsset));
            result.hierarchyNodes = hier.nodes.Select(n => n.name).ToArray();
            result.hierarchyBind = hier.nodes.Select(n => n.world).ToArray();
            int frame = Math.Max(0, hier.NodeIndex("OBJ_Frame"));
            // each mod node's pose node: its namesake in the hierarchy, else its nearest ancestor's, else the frame
            var poseOf = new int[mesh.nodes.Length];
            for (int i = 0; i < mesh.nodes.Length; i++)
            {
                int j = i; while (j >= 0 && hier.NodeIndex(mesh.nodes[j]) < 0) j = mesh.parents[j];
                poseOf[i] = j < 0 ? frame : hier.NodeIndex(mesh.nodes[j]);
                if (j != i) Debug.LogWarning($"Reflex bike {b.mod.name}: node {mesh.nodes[i]} is not in {spec.hierarchyModel}: drawn on {hier.nodes[poseOf[i]].name}");
            }
            // "pose": "physics" (a bike of another size with its own hierarchy in the core: the Alive 50cc, ALIVE_BIKES.md): the mesh as it is, each
            // part bound to the mod's own node rest pose, which the core's pose of the vehicle has (rx_session_vehicle_physics_nodes)
            bool own = b.OwnNodes;
            float scale = 1f, pitch = 0f;
            var T = own ? Matrix4x4.identity : RxFit(mesh, hier, b.fit, out scale, out pitch);
            var bindW = hier.nodes.Select(n => n.world).ToArray();
            if (own)
                for (int i = 0; i < mesh.nodes.Length; i++) { int h = hier.NodeIndex(mesh.nodes[i]); if (h >= 0) bindW[h] = mesh.world[i]; }
            result.hierarchyBind = bindW;
            int hfn = hier.NodeIndex("OBJ_FRT_Tire"), hbn = hier.NodeIndex("OBJ_BCK_Tire");
            // no wheels in the mod (an MX Simulator / MX Bikes style model: its wheels are separate there): the original bike's tyres and rims
            bool ownWheels = mesh.surfaces.Any(x => !x.surface.skinned && (poseOf[x.node] == hfn || poseOf[x.node] == hbn));
            var R3 = Matrix4x4.Rotate(T.rotation);
            // the steering axes (the mod's after the fit against the original's): how far apart at the head, and their angle
            string steer = "";
            int mu = mesh.NodeIndex("OBJ_UPR_Forks"), hu = hier.NodeIndex("OBJ_UPR_Forks");
            if (mu >= 0 && hu >= 0)
            {
                Vector3 pm = T.MultiplyPoint3x4(mesh.world[mu].GetColumn(3)), am = R3.MultiplyVector(mesh.world[mu].GetColumn(1)).normalized;
                Vector3 ph = hier.nodes[hu].world.GetColumn(3), ah = ((Vector3)hier.nodes[hu].world.GetColumn(1)).normalized;
                Vector3 off = pm - ph; off -= Vector3.Dot(off, ah) * ah;
                steer = $", steering axis {off.magnitude * 12f:F2} in off the original's, {Vector3.Angle(am, ah):F1} deg apart";
            }
            // the wheels: scaled about their axles to the original tyres' radius (where the physics rolls them)
            Dictionary<int, float> WheelScale(RxMesh m, bool log, float scale)
            {
                var ws = new Dictionary<int, float>();
                foreach (var (node, model) in ownWheels && !own ? new[] { ("OBJ_FRT_Tire", spec.frontModel), ("OBJ_BCK_Tire", spec.rearModel) } : new (string, string)[0])
                {
                    int mi = m.NodeIndex(node); float want = TyreRadius(install, spec, model), have = ModWheelRadius(m, mi) * scale;
                    float k = want > 0.3f && have > 0.3f ? want / have : 1f;
                    if (k < 0.85f || k > 1.15f) { if (have > 0.3f) Debug.LogWarning($"Reflex bike {b.mod.name}: {node} radius {have:F3} ft against the original's {want:F3}: left as modelled"); k = 1f; }
                    if (mi >= 0) ws[mi] = k;
                    if (log) steer += $", {node} {have * 12f:F2} in -> {want * 12f:F2} in";
                }
                return ws;
            }
            var wheelScale = WheelScale(mesh, true, scale);
            result.rxFit = $"scale {scale:F4}, pitch {pitch * Mathf.Rad2Deg:F2} deg" + steer + (ownWheels ? "" : ", the original's wheels");
            Debug.Log($"Reflex bike {b.mod.name} / {b.name?.Full}{(spec.rxSkin != null ? " / skin " + spec.rxSkin.Title + " (" + spec.rxSkin.mod.name + ")" : "")}: {mesh.surfaces.Count} surfaces, {mesh.Vertices} vertices, {mesh.Triangles} triangles on {spec.chassisPkg}'s hierarchy: {result.rxFit}");

            var root = new GameObject($"RxMod {b.name?.Full ?? b.mod.name} chassis · body 0");
            result.chassis = root;
            GameObject Holder(string n, int hn) { var g = new GameObject(n); if (hn >= 0) g.transform.position = bindW[hn].GetColumn(3); return g; }
            var front = Holder("RxMod front wheel · body 2", hfn); var rear = Holder("RxMod rear wheel · body 3", hbn);
            result.frontWheel = front.transform; result.rearWheel = rear.transform;

            var worn = spec.rxParts;   // (the bike's own kits / parts the player wears: Content/RxParts.cs)
            var replaced = RxParts.Replaced(worn);
            // a mesh's surfaces on the pose: the bike's own (but the ones a worn part replaces), then each worn part's mesh (made in the bike's
            // mesh space, on its nodes: fitted and wheel-scaled as the bike's own)
            void AddMesh(RxMesh m, int[] poseOfM, Dictionary<int, float> ws, string label, Func<string, UnityEngine.Material> materialOf, HashSet<int> skip, Matrix4x4 T, Matrix4x4 R3)
            {
                bool lod = result.lodSink != null;   // (a further LOD's parts hang under the posed parts: no wheel holder, no skin of their own)
                var boneOf = new Dictionary<int, Transform>();
                Transform Bone(int j)
                {
                    if (boneOf.TryGetValue(j, out var t)) return t;
                    var go = new GameObject("bone " + m.nodes[j]); go.transform.SetParent(root.transform, false);
                    result.bones.Add((go.transform, poseOfM[j], bindW[poseOfM[j]]));
                    return boneOf[j] = go.transform;
                }
                for (int si = 0; si < m.surfaces.Count; si++)
                {
                    if (skip != null && skip.Contains(si)) continue;
                    var (src, mi, node) = m.surfaces[si];
                    string matName = m.materials[mi];
                    if (!stand && IsStand(matName, m.nodes[node])) continue;   // (a race: no garage stand under a twin)
                    int pn = poseOfM[node];
                    bool wheel = !src.skinned && (pn == hfn || pn == hbn) && ws.TryGetValue(node, out _);
                    Vector3 axle = wheel ? (Vector3)hier.nodes[pn].world.GetColumn(3) : Vector3.zero; float k = wheel ? ws[node] : 1f;
                    var s = new Rfx.Surface { skinned = src.skinned, uv0 = src.uv0, uv1 = src.uv1, triangles = src.triangles, strip = src.strip, bones = src.bones, weights = src.weights,
                                              positions = new Vector3[src.positions.Length], normals = new Vector3[src.normals.Length] };
                    for (int i = 0; i < s.positions.Length; i++)
                    {
                        var p = T.MultiplyPoint3x4(src.positions[i]);
                        s.positions[i] = wheel ? axle + (p - axle) * k : p;
                        s.normals[i] = R3.MultiplyVector(src.normals[i]).normalized;
                    }
                    if (src.tangents != null) { s.tangents = new Vector3[src.tangents.Length]; for (int i = 0; i < s.tangents.Length; i++) s.tangents[i] = R3.MultiplyVector(src.tangents[i]); }
                    else RxTangents(s);   // (a mesh without tangents: the Alive importer's, most mods': the tyre program's normal map needs the frame)
                    var mat = materialOf(matName);
                    var parent = wheel ? (pn == hfn ? front.transform : rear.transform) : root.transform;
                    var t = result.AddPart(parent, $"{label}_{si}_{hier.nodes[pn].name}", s, Matrix4x4.identity, pn, bindW[pn], matName, mat);
                    if (wheel && !lod) t.position = Vector3.zero;   // (the holder sits on the axle: the parts stay at the model origin, as the original's)
                    if (s.skinned && s.bones != null && !lod) result.Skin(t, s, node, Bone);
                }
            }
            // a bike's mesh and its worn parts' meshes (sp: the spec they belong to, the twin's or the race model's)
            void AddBike(BikeSpec sp, RxMesh bm, int[] poseOfB, List<RxPartOption> wornB, HashSet<int> replacedB, Matrix4x4 Tb, float scaleB, string suffix)
            {
                var bb = sp.rx; var Rb = Matrix4x4.Rotate(Tb.rotation);
                var materials = new Dictionary<string, UnityEngine.Material>(StringComparer.OrdinalIgnoreCase);
                AddMesh(bm, poseOfB, bm == mesh ? wheelScale : WheelScale(bm, false, scaleB), Path.GetFileNameWithoutExtension(bb.mesh) + suffix, name =>
                {
                    if (!materials.TryGetValue(name, out var mat)) materials[name] = mat = RxMaterial(cat, install, sp, name, RxParts.Material(wornB, name, RxModBikes.MaterialOf(bb, sp.rxSkin, name), bb));   // (the skin's maps, the worn kit's)
                    return mat;
                }, replacedB, Tb, Rb);
                if (wornB == null) return;
                foreach (var o in wornB)
                {
                    if (o.mesh == null) continue;
                    try
                    {
                        var pm = RxModBikes.Mesh(o.mod, o.mesh);
                        var partPose = new int[pm.nodes.Length];
                        for (int i = 0; i < pm.nodes.Length; i++) { int j = i; while (j >= 0 && hier.NodeIndex(pm.nodes[j]) < 0) j = pm.parents[j]; partPose[i] = j < 0 ? frame : hier.NodeIndex(pm.nodes[j]); }
                        var own2 = new Dictionary<string, UnityEngine.Material>(StringComparer.OrdinalIgnoreCase);
                        AddMesh(pm, partPose, WheelScale(pm, false, scaleB), o.slot.id + "_" + Path.GetFileNameWithoutExtension(o.mesh) + suffix, name =>
                        {
                            if (own2.TryGetValue(name, out var mat)) return mat;
                            // the option's own material of that name, else the bike's (a part mesh may reuse the bike's materials)
                            var mm = o.materials.TryGetValue(name, out var e) ? RxParts.Tinted(wornB, e.m) : RxParts.Material(wornB, name, RxModBikes.MaterialOf(bb, sp.rxSkin, name), bb);
                            return own2[name] = RxMaterial(cat, install, sp, name, mm);
                        }, null, Tb, Rb);
                    }
                    catch (Exception e) when (e is IOException || e is InvalidDataException) { Debug.LogWarning($"Reflex bike {bb.mod.name}: part {o.slot.name} {o.Title}: {e.Message} (not drawn)"); }
                }
            }
            AddBike(spec, mesh, poseOf, worn, replaced, T, scale, "");
            if (worn != null && worn.Count > 0) Debug.Log($"Reflex bike {b.mod.name} / {b.name?.Full}: wears " + string.Join(", ", worn.Select(o => o.slot.name + " " + o.Title)) + (replaced.Count > 0 ? $" ({replaced.Count} of its own surfaces replaced)" : ""));
            if (!ownWheels && !spec.atv && spec.tireDb != null) RxOriginalWheels(result, spec, install, cat, hier, front, rear);
            // the twin's race model beyond RxFarDistance (the other riders of a race): one further LOD over every part of the twin
            if (farSpec?.rx != null && farSpec.rx != b && !own && !farSpec.rx.OwnNodes && result.parts.Count > 0)
                try
                {
                    var fb = farSpec.rx; var fm = RxModBikes.Mesh(fb);
                    var farPose = new int[fm.nodes.Length];
                    for (int i = 0; i < fm.nodes.Length; i++) { int j = i; while (j >= 0 && hier.NodeIndex(fm.nodes[j]) < 0) j = fm.parents[j]; farPose[i] = j < 0 ? frame : hier.NodeIndex(fm.nodes[j]); }
                    bool farWheels = fm.surfaces.Any(x => !x.surface.skinned && (farPose[x.node] == hfn || farPose[x.node] == hbn));
                    if (farWheels != ownWheels) throw new InvalidDataException("the race model and the twin differ in having wheels of their own");
                    var Tf = RxFit(fm, hier, fb.fit, out float farScale, out _);
                    var farWorn = farSpec.rxParts; var farReplaced = RxParts.Replaced(farWorn);
                    // (a posed, empty carrier on every hierarchy node the twin draws nothing on: AddLodPart)
                    var used = new HashSet<int>(result.parts.Select(p => p.node));
                    for (int i = 0; i < hier.nodes.Length; i++)
                    {
                        if (used.Contains(i)) continue;
                        var go = new GameObject("lod carrier " + hier.nodes[i].name); go.transform.SetParent(root.transform, false);
                        result.bones.Add((go.transform, i, bindW[i]));
                    }
                    result.highResLods = true;
                    bool farNew = false;
                    result.AddLodModel("rx " + fb.mod.name, new[] { (0, 0, 0), (0, 0, RxFarDistance) }, 0, result.parts.Count,
                        (lod, parent) => { AddBike(farSpec, fm, farPose, farWorn, farReplaced, Tf, farScale, " far"); farNew = true; });
                    result.LodNewMaterials = () => { bool n = farNew; farNew = false; return n; };
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException) { Debug.LogWarning($"Reflex bike {b.mod.name}: its race model for the distance: {e.Message} (the high-res twin at any distance)"); }
            if (own)
            {
                result.rxFit = b.physicsPose ? "its own size and hierarchy (pose: physics)" : "its own nodes (fit: nodes)";
                Matrix4x4 Local(int i) => mesh.parents[i] < 0 ? mesh.world[i] : mesh.world[mesh.parents[i]].inverse * mesh.world[i];
                int mu2 = mesh.NodeIndex("OBJ_UPR_Forks"), ml = mesh.NodeIndex("OBJ_LWR_Forks"), mt = mesh.NodeIndex("OBJ_FRT_Tire");
                int hu2 = hier.NodeIndex("OBJ_UPR_Forks"), hl = hier.NodeIndex("OBJ_LWR_Forks"), ht = hier.NodeIndex("OBJ_FRT_Tire");
                if (b.physicsPose && mu2 >= 0 && ml >= 0 && mt >= 0 && hu2 >= 0 && hl >= 0 && ht >= 0 && mesh.parents[ml] == mu2 && mesh.parents[mt] == ml)
                {   // the fork onto the physics axle (OriginalBikePose.ForkFix): the nodes under the upper forks in the pose's hierarchy
                    bool Under(int i) { for (int p = hier.nodes[i].parent; p >= 0; p = hier.nodes[p].parent) if (p == hu2) return true; return false; }
                    result.forkFix = new OriginalBikePose.ForkFix { upr = hu2, lwr = hl, frt = ht, restUL = Local(ml), restLT = Local(mt),
                                                                     subtree = Enumerable.Range(0, hier.nodes.Length).Where(i => i != hl && i != ht && Under(i)).ToArray() };
                }
                int msw = mesh.NodeIndex("OBJ_Swingarm"), mbt = mesh.NodeIndex("OBJ_BCK_Tire"), hsw = hier.NodeIndex("OBJ_Swingarm"), hbt = hier.NodeIndex("OBJ_BCK_Tire");
                if (b.physicsPose && msw >= 0 && mbt >= 0 && hsw >= 0 && hbt >= 0 && mesh.parents[mbt] == msw)
                {   // the swing arm onto the simulation's rear wheel (OriginalBikePose.SwingFix)
                    bool UnderArm(int i) { for (int p = hier.nodes[i].parent; p >= 0; p = hier.nodes[p].parent) if (p == hsw) return true; return false; }
                    result.swingFix = new OriginalBikePose.SwingFix { arm = hsw, tyre = hbt, restTyre = Local(mbt).GetColumn(3),
                                                                       subtree = Enumerable.Range(0, hier.nodes.Length).Where(i => i != hbt && UnderArm(i)).ToArray() };
                }
                // the rider's hands and feet on the bike's own grips and pegs (RiderLimbRetarget): the core's rider reaches for the original's
                // (its lean animations keep the original's locators), so each limb moves by the locator's change, carried in the frame of the
                // locator's parent as the core poses it. "pose": "physics" (the core poses the mod's own nodes): the change in the bike frame's
                // space (the mod's rest pose against the original's, each from its frame node: the parents themselves move on a bike of another
                // size, the 50cc's steering head 0.3 ft lower and back), less the rider's own move (riderOffset), in the mod parent's rest frame.
                // "fit": "nodes" (the core poses the original's nodes): the change in the parent's frame.
                result.riderShift = b.physicsPose ? b.riderOffset : Vector3.zero;
                int mfr = mesh.NodeIndex("OBJ_Frame"), hfr = hier.NodeIndex("OBJ_Frame");
                Matrix4x4 meshFrame = mfr >= 0 ? mesh.world[mfr] : Matrix4x4.identity, hierFrame = hfr >= 0 ? hier.nodes[hfr].world : Matrix4x4.identity;
                var limbs = new List<RiderLimbRetarget.Offset>();
                foreach (string n in new[] { "NDE_RHT_Hand", "NDE_LFT_Hand", "NDE_RHT_Foot", "NDE_LFT_Foot" })
                {
                    int i = hier.NodeIndex(n), j = mesh.NodeIndex(n);
                    if (i < 0 || j < 0 || hier.nodes[i].parent < 0 || mesh.parents[j] < 0) continue;
                    if (!string.Equals(hier.nodes[hier.nodes[i].parent].name, mesh.nodes[mesh.parents[j]], StringComparison.OrdinalIgnoreCase)) continue;
                    Vector3 d;
                    if (b.physicsPose)
                    {
                        Vector3 inFrame = meshFrame.inverse.MultiplyPoint3x4(mesh.world[j].GetColumn(3)) - hierFrame.inverse.MultiplyPoint3x4(hier.nodes[i].world.GetColumn(3)) - result.riderShift;
                        d = (meshFrame.inverse * mesh.world[mesh.parents[j]]).inverse.MultiplyVector(inFrame);   // (in the parent's rest frame)
                    }
                    else d = (Vector3)(Local(j).GetColumn(3) - hier.nodes[i].local.GetColumn(3));   // ("fit": "nodes": the core poses the original's parents)
                    if (d.sqrMagnitude < 1e-8f || !RiderLimbRetarget.Chain(n, out int u, out int l, out int e)) continue;
                    limbs.Add(new RiderLimbRetarget.Offset { locator = n, node = i, parent = hier.nodes[i].parent, delta = d, upper = u, lower = l, end = e });
                }
                if (limbs.Count > 0) { result.limbOffsets = limbs.ToArray(); Debug.Log($"Reflex bike {b.mod.name}: its own grips and pegs: " + string.Join(", ", limbs.Select(o => o.locator + " " + o.delta.ToString("F3")))); }
            }
            else if (!AliveImported(b.mod))   // (an imported Alive bike stays as it was tuned: its rider keeps the original's grips and pegs)
            {   // "fit": "axles" / "keep" (2026-11-15: the Reflex Skin Builder's bikes, whose bars sit higher than the original's: the hands
                // were drawn below the grips): the mod's hand / foot locators as fitted (T) against the original's, in the rest frame of the
                // locator's parent in the hierarchy, as RiderLimbRetarget carries it. A mesh without the locators changes nothing.
                var limbs = new List<RiderLimbRetarget.Offset>();
                foreach (string n in new[] { "NDE_RHT_Hand", "NDE_LFT_Hand", "NDE_RHT_Foot", "NDE_LFT_Foot" })
                {
                    int i = hier.NodeIndex(n), j = mesh.NodeIndex(n);
                    if (i < 0 || j < 0 || hier.nodes[i].parent < 0) continue;
                    Vector3 fitted = T.MultiplyPoint3x4(mesh.world[j].GetColumn(3));
                    Vector3 d = hier.nodes[hier.nodes[i].parent].world.inverse.MultiplyVector(fitted - (Vector3)hier.nodes[i].world.GetColumn(3));
                    if (d.sqrMagnitude < 1e-8f || d.sqrMagnitude > 1f || !RiderLimbRetarget.Chain(n, out int u, out int l, out int e)) continue;   // (within 1 ft)
                    limbs.Add(new RiderLimbRetarget.Offset { locator = n, node = i, parent = hier.nodes[i].parent, delta = d, upper = u, lower = l, end = e });
                }
                if (limbs.Count > 0) { result.limbOffsets = limbs.ToArray(); Debug.Log($"Reflex bike {b.mod.name}: its own grips and pegs (fitted): " + string.Join(", ", limbs.Select(o => o.locator + " " + o.delta.ToString("F3")))); }
            }
            var pose = root.AddComponent<OriginalBikePose>();
            pose.forkFix = result.forkFix; pose.swingFix = result.swingFix;
            pose.parts = result.parts.Select(p => p.part).Concat(result.bones.Select(x => x.bone)).ToArray();
            pose.nodes = result.parts.Select(p => p.node).Concat(result.bones.Select(x => x.node)).ToArray();
            pose.inverseBind = result.binds.Concat(result.bones.Select(x => x.bind)).Select(w => w.inverse).ToArray();
            result.pose = pose;
            return result;
        }

        /// The tangents of a .rxmod surface that has none (the Alive importer's .rxmesh files and most mods carry positions, normals and UVs only;
        /// the vertex programs then got a zero tangent: the tyre program's normal map, x T + y (T x N) + z N, lost its x / y). As the game's own
        /// meshes have them: the unit direction of growing u on the surface, made square to the normal - the rider gear's rule
        /// (ReflexRiderModel.GearTangents, private to that class: the same maths here).
        static void RxTangents(Rfx.Surface s)
        {
            if (s.tangents != null || s.positions == null || s.uv0 == null || s.normals == null) return;
            int n = s.positions.Length; var tu = new Vector3[n];
            var tri = s.Triangles().ToArray();
            for (int t = 0; t + 2 < tri.Length; t += 3)
            {
                int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
                Vector3 e1 = s.positions[i1] - s.positions[i0], e2 = s.positions[i2] - s.positions[i0];
                Vector2 d1 = s.uv0[i1] - s.uv0[i0], d2 = s.uv0[i2] - s.uv0[i0];
                float det = d1.x * d2.y - d2.x * d1.y; if (Mathf.Abs(det) < 1e-12f) continue;
                // (the direction only, weighted by the triangle's area: a sliver with a huge 1 / det does not outvote its neighbours)
                var su = (e1 * d2.y - e2 * d1.y) * Mathf.Sign(det); float len = su.magnitude; if (len < 1e-12f) continue;
                su *= Vector3.Cross(e1, e2).magnitude / len;
                tu[i0] += su; tu[i1] += su; tu[i2] += su;
            }
            var tan = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var N = s.normals[i].normalized;
                var T = tu[i] - N * Vector3.Dot(N, tu[i]);
                if (T.sqrMagnitude < 1e-12f) T = Vector3.Cross(N, Mathf.Abs(N.y) < 0.9f ? Vector3.up : Vector3.right);   // (no UV direction there: any direction square to the normal)
                tan[i] = T.normalized;
            }
            s.tangents = tan;
        }

        /// The original bike's tyres and rims on the wheel holders (as Build draws them: a tyre model is modelled in its node's space and hangs
        /// on the body's tyre node), for a mod without wheels of its own.
        static void RxOriginalWheels(ReflexBikeModel result, BikeSpec spec, Rfx.Install install, VehicleCatalog cat, Rfx.Model hier, GameObject front, GameObject rear)
        {
            var pool = spec.materials != null && spec.materials.Count > 0 ? spec.materials : spec.kitPkg != null ? new List<(string db, string pkg)> { (spec.kitDb, spec.kitPkg) } : new List<(string db, string pkg)>();
            var body = Load(install, spec.chassisDb, spec.chassisPkg, spec.chassisModel);
            var mats = new Dictionary<string, UnityEngine.Material>();
            foreach (var (asset, nodeName, holder) in new[] { (spec.frontModel, "OBJ_FRT_Tire", front), (spec.rearModel, "OBJ_BCK_Tire", rear) })
            {
                var tire = Load(install, spec.tireDb, spec.tirePkg, asset);
                int attach = body.model.NodeIndex(nodeName), pn = hier.NodeIndex(nodeName);
                Rfx.Check(attach >= 0 && pn >= 0 && tire.model.nodes.Length == 1, "the tyre's attachment node");
                var attachWorld = body.model.nodes[attach].world;
                foreach (var bd in tire.model.Lod(0))
                {
                    var s = tire.surfaces[bd.surface];
                    if (!mats.TryGetValue(bd.material, out var mat))
                    {
                        var def = GameMaterial(cat, bd.material, (spec.tireDb, spec.tirePkg), pool) ?? cat.Resolve(bd.material, (spec.tireDb, spec.tirePkg), pool);
                        mats[bd.material] = mat = cat.Build(def, bd.material + (def != null && def.name != bd.material ? " -> " + def.name : ""));
                    }
                    var t = result.AddPart(holder.transform, $"{asset}_{bd.surface}_{tire.model.nodes[bd.node].name}", s, s.skinned ? Matrix4x4.identity : attachWorld, pn, attachWorld, bd.material, mat);
                    t.position = Vector3.zero;
                }
            }
        }

        /// The pixel programs whose texture slots RxMaterial fills (each sampled as: SFX / combo map, diffuse, mud noise, the number plate or a
        /// fork sticker on the second UV set, the tyre's normal map).
        static readonly string[] RxPrograms = { "Template_Vehicle_Plstc_Fnl", "Template_MX_Frame_FNL", "Template_ATV_MX_Engine_FNL", "Template_MX_Wheel_Fnl", "Template_tire_FNL", "Template_ATV_MX_AlphaMtl_UI_FNL" };

        /// The original bike's binding a template borrows: the first binding of its chassis (or tyre) model whose name fits the template.
        static readonly (string template, Func<string, bool> fits, bool tyre)[] RxTemplates =
        {
            ("plastic", n => n.StartsWith("MX_Plstc", StringComparison.OrdinalIgnoreCase) || n.StartsWith("HR_MX_Plstc", StringComparison.OrdinalIgnoreCase), false),   // (the garage's HR twin: HR_MX_Plstc)
            ("frame", n => n.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0, false),
            ("engine", n => n.IndexOf("_Eng", StringComparison.OrdinalIgnoreCase) >= 0, false),
            ("chain", n => n.IndexOf("Chain", StringComparison.OrdinalIgnoreCase) >= 0, false),
            ("wheel", n => n.IndexOf("Rim", StringComparison.OrdinalIgnoreCase) >= 0, true),
            ("tire", n => n.IndexOf("Tire", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Rim", StringComparison.OrdinalIgnoreCase) < 0, true),
        };

        /// A .rxmod material: the original bike's material of its template (as the game resolves that binding: the graphic kit first) with
        /// the mod's diffuse, its SFX map (from its spec map) and its normal map in place of the original's maps; the mud noise and the
        /// render states stay. Falls back to the plastics template, then to a plain grey.
        static readonly HashSet<string> s_oddTemplates = new HashSet<string>();
        /// Tests: REFLEX_RXMOD_LEGACY_TEMPLATES=1 builds a .rxmod bike's materials as before 2026-11-17 (a before / after in one build).
        static readonly bool LegacyTemplates = Environment.GetEnvironmentVariable("REFLEX_RXMOD_LEGACY_TEMPLATES") == "1";
        static UnityEngine.Material RxMaterial(VehicleCatalog cat, Rfx.Install install, BikeSpec spec, string name, RxModMaterial mm)
        {
            var b = spec.rx;   // (mm: the bike's material painted by the chosen skin, RxSkin, and the worn kit / part, RxParts; or a part mesh's own)
            if (mm == null) { Debug.LogWarning($"Reflex bike {b.mod.name}: material {name} is not in mod.json (plastic, no maps)"); mm = new RxModMaterial { name = name, diffuseMod = b.mod, specMod = b.mod, normalMod = b.mod, sfxMod = b.mod }; }
            var pool = spec.materials != null && spec.materials.Count > 0 ? spec.materials : spec.kitPkg != null ? new List<(string db, string pkg)> { (spec.kitDb, spec.kitPkg) } : new List<(string db, string pkg)>();
            // The template is never a gear / kit pack's material (KitPacks: a community graphics kit the player wears on the original bike of
            // this model, "mod:kit/<hash>>..." packages): such a material is made for that kit's own maps and layout (its own parameters, its
            // textures under any names), and borrowed for a .rxmod bike it drew the mod's kit in patches over plastics of the pack's colour
            // (2026-11-17, a player's report: Alive bikes "showing other kits' graphics"). The game's own materials only; with none left, the
            // chassis' own material, else the reference bike's.
            if (!LegacyTemplates) pool = pool.Where(x => BikeMods.NamespaceOf(x.db).Length == 0).ToList();
            // the programs whose texture slots a .rxmod's maps are known to fit (core/plan/RXMOD.md "Materials"); another program (the KTM 450's
            // Template_ATV_MX_Plstc: a dirt mask and a wet-mud normal map) is not borrowed: the chassis' own material, else the reference bike's
            bool Fits(VehicleMaterialDef d) => d != null && Array.IndexOf(RxPrograms, d.pixelShader) >= 0;
            VehicleMaterialDef Template(string template)
            {
                var e = RxTemplates.FirstOrDefault(x => x.template == template); if (e.fits == null) return null;
                try
                {
                    if (spec.atv || (e.tyre && spec.tireDb == null)) goto reference;   // (an ATV: the motocross programs of the reference bike)
                    var own = e.tyre ? (spec.tireDb, spec.tirePkg) : (spec.chassisDb, spec.chassisPkg);
                    var model = e.tyre ? Load(install, spec.tireDb, spec.tirePkg, spec.frontModel) : Load(install, spec.chassisDb, spec.chassisPkg, spec.chassisModel);
                    string binding = model.model.Lod(0).Select(x => x.material).FirstOrDefault(e.fits);
                    if (binding != null)
                    {
                        var kit = GameMaterial(cat, binding, own, pool); if (Fits(kit)) return kit;
                        var mine = cat.Material(own.Item1, own.Item2, binding); if (Fits(mine)) return mine;
                    }
                    // the reference bike every install has (the MarsF Lite with THQ tyres: BikeSpec.MarsFLite)
                    reference:
                    var r = BikeSpec.MarsFLite; var rown = e.tyre ? (r.tireDb, r.tirePkg) : (r.chassisDb, r.chassisPkg);
                    var rmodel = e.tyre ? Load(install, r.tireDb, r.tirePkg, r.frontModel) : Load(install, r.chassisDb, r.chassisPkg, r.chassisModel);
                    string rb = rmodel.model.Lod(0).Select(x => x.material).FirstOrDefault(e.fits);
                    var rpool = new List<(string db, string pkg)> { (r.kitDb, r.kitPkg) };
                    var rd = rb == null ? null : GameMaterial(cat, rb, rown, rpool) ?? cat.Material(rown.Item1, rown.Item2, rb);
                    return Fits(rd) ? rd : null;
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException) { Debug.LogWarning($"Reflex bike {b.mod.name}: template {template}: {ex.Message}"); return null; }
            }
            var t = Template(mm.template) ?? Template("plastic");
            if (t == null) { Debug.LogWarning($"Reflex bike {b.mod.name}: material {name}: {spec.chassisPkg} has no {mm.template} / plastic binding to borrow (a plain grey)"); return cat.Build(null, "rxmod " + name); }
            var d = new VehicleMaterialDef { db = t.db, pkg = t.pkg, name = t.name, pixelShader = t.pixelShader, package = t.package, cull = mm.twoSided ? 1 : t.cull, alphaRef = mm.alphaTest ? mm.alphaRef : t.alphaRef,
                                             alphaFunc = mm.alphaTest ? 7 : t.alphaFunc, srcBlend = t.srcBlend, destBlend = t.destBlend, zwrite = t.zwrite, alphaTest = mm.alphaTest || t.alphaTest, alphaBlend = t.alphaBlend };
            foreach (var kv in t.parameters) d.parameters[kv.Key] = kv.Value;
            // Each texture slot of the template by what the PROGRAM does with it (its sampler: usr_sampler_number is the rider's plate on the
            // second UV set, usr_sampler_combo_map the SFX map, usr_sampler_diffuse_texture the base colour), then by the texture's name as
            // before. By the name alone (until 2026-11-17) a template whose plate texture was not called "Numbers" (the material of an
            // original graphics kit the player had on that bike) counted as one more colour slot, and being the last it got the mod's
            // diffuse: the mod's kit was drawn through the plate's UV set (patches of it on the number plates and fenders) over plastics
            // left without their colour map.
            int diffuse = -1, diffuseBySampler = -1; bool normalBound = false; var slotLog = new System.Text.StringBuilder();
            foreach (var x in t.textures)
            {
                var c = new VehicleMaterialDef.Tex { name = x.name, sampler = x.sampler, addrU = x.addrU, addrV = x.addrV, mipFilter = x.mipFilter, magFilter = x.magFilter, minFilter = x.minFilter, maxAniso = x.maxAniso, mipBias = x.mipBias, asset = x.asset };
                string n = x.name ?? "", sm = x.sampler ?? "";
                bool Sampler(string part) => !LegacyTemplates && sm.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
                bool plate = n.Equals("Numbers", StringComparison.OrdinalIgnoreCase) || n.IndexOf("namnum", StringComparison.OrdinalIgnoreCase) >= 0 || Sampler("sampler_number");
                bool sfx = !plate && (n.EndsWith("_SFX", StringComparison.OrdinalIgnoreCase) || n.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0 || Sampler("combo"));
                bool nrm = !plate && !sfx && (n.IndexOf("NRML", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("_Norm", StringComparison.OrdinalIgnoreCase) >= 0 || Sampler("normal"));
                slotLog.Append(' ').Append(n).Append('@').Append(sm);
                if (n.IndexOf("Mud_Noise", StringComparison.OrdinalIgnoreCase) >= 0) { }
                else if (plate)
                { if (!mm.numbers) { c.name = "rxmod no plate"; c.external = VehicleCatalog.ClearTexture; } }   // ("numbers": the original's binding: the rider's plate)   // (the plates are in the mod's own diffuse: no run-time number on its second UV set)
                else if (sfx) c.external = RxModBikes.Sfx(mm) ?? VehicleCatalog.ClearTexture;
                else if (nrm) { c.external = RxModBikes.Normal(mm) ?? FlatNormal; normalBound = true; }
                else
                {   // (a fork sticker on the frame's second UV set: clear)
                    c.external = VehicleCatalog.ClearTexture;
                    if (Sampler("diffuse") && diffuseBySampler < 0) diffuseBySampler = d.textures.Count;
                    diffuse = d.textures.Count;
                }
                // (the mod's maps repeat; its UVs are in 0..1.) Not the rider's plate: its second UV set is laid out for a CLAMPED texture (the
                // plates span a little past 0..1 - the Alive CRF450R's frame plastics -0.28 .. 1.44 - and every other part is parked on a
                // clear corner texel), so repeated it drew the number again beside the plate and on the fenders (2026-11-17, the Alive OEM bikes)
                if (plate && !LegacyTemplates) { c.addrU = 3; c.addrV = 3; } else { c.addrU = 1; c.addrV = 1; }
                d.textures.Add(c);
            }
            if (!LegacyTemplates && diffuseBySampler >= 0 && diffuseBySampler != diffuse)
            {
                if (s_oddTemplates.Add(t.db + "/" + t.pkg + "/" + t.name)) Debug.Log($"Reflex bike {b.mod.name}: template {t.name} ({t.pkg}) has more than one colour slot:{slotLog}: the mod's diffuse goes to the program's diffuse sampler");
                diffuse = diffuseBySampler;
            }
            if (diffuse >= 0) d.textures[diffuse].external = (mm.tintValue != null ? RxModBikes.Tinted(mm) : null) ?? RxModBikes.Map(mm.diffuseMod, mm.diffuse) ?? Texture2D.grayTexture;   // (a run-time colour worn: RxTint)
            var built = cat.Build(d, "rxmod " + name + " (" + mm.template + ": " + t.name + ")");
            // clean until the race's mud record says otherwise (OriginalWorldRuntime / OriginalOpponents set usr_mud_amount every frame; the
            // garage and the menus never do, and the program's default would draw the mod's whole mud mask)
            if (built && built.HasProperty("usr_mud_amount")) built.SetVector("usr_mud_amount", Vector4.zero);
            // the mod's normal map on a template whose program has none (plastics, frame, engine, wheels: only the tyre's samples one): the
            // enhanced materials' shading normal (specular, clear coat, rain film; ReflexVehiclePBR.hlsl), in a slot no template program samples
            if (built && !normalBound && !string.IsNullOrEmpty(mm.normal))
            {
                var nm = RxModBikes.Normal(mm);
                if (nm) ReflexMaterialClasses.SetEnhancedNormal(built, nm);
            }
            return built;
        }

        /// An original material with a .rxmod skin's map in place of its diffuse texture (RxSkin.OriginalMap: by the texture's name or role);
        /// the material as it is when the skin does not paint it.
        static VehicleMaterialDef RxOriginalSkin(VehicleMaterialDef def, RxSkin skin)
        {
            if (def == null) return null;
            var tex = def.Sampler("diffuse") ?? def.textures.FirstOrDefault(t => t.name != null && !new[] { "SFX", "Numbers", "Mud_Noise", "NRML", "Norm", "Combo", "Fork", "namnum", "dirtmask", "Mud_WET" }.Any(x => t.name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0));
            string entry = skin.OriginalMap(tex?.name, def.name);
            if (entry == null) return def;
            var map = RxModBikes.Map(skin.mod, entry); if (!map) return def;
            var d = new VehicleMaterialDef { db = def.db, pkg = def.pkg, name = def.name, pixelShader = def.pixelShader, package = def.package, cull = def.cull, alphaRef = def.alphaRef,
                                             alphaFunc = def.alphaFunc, srcBlend = def.srcBlend, destBlend = def.destBlend, zwrite = def.zwrite, alphaTest = def.alphaTest, alphaBlend = def.alphaBlend };
            foreach (var kv in def.parameters) d.parameters[kv.Key] = kv.Value;
            foreach (var x in def.textures)
                d.textures.Add(new VehicleMaterialDef.Tex { name = x.name, sampler = x.sampler, addrU = x.addrU, addrV = x.addrV, mipFilter = x.mipFilter, magFilter = x.magFilter, minFilter = x.minFilter,
                                                            maxAniso = x.maxAniso, mipBias = x.mipBias, asset = x.asset, external = ReferenceEquals(x, tex) ? map : x.external });
            return d;
        }

        static Texture2D flatNormal;
        /// A flat normal (the tyre program's map when the mod has none): signed X / Y = 0.
        static Texture2D FlatNormal
        {
            get
            {
                if (!flatNormal) { flatNormal = new Texture2D(4, 4, TextureFormat.RGHalf, false, true) { name = "rxmod flat normal", hideFlags = HideFlags.DontSave }; flatNormal.SetPixels(Enumerable.Repeat(new Color(0, 0, 0, 1), 16).ToArray()); flatNormal.Apply(); }
                return flatNormal;
            }
        }
    }
}
