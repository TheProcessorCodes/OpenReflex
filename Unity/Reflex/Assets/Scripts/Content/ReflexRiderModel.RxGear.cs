// A "gear" .rxmod piece on Reflex's rider (Content/RxGear.cs; the MX vs ATV Alive gear): its mesh is made on Alive's rider (the mesh's node
// table is Alive's rest skeleton), so each of its bones is driven by a Reflex bone through a rest alignment (RxGearRig.Make): a skinned
// surface gets one Unity bone per Alive node (the driver's transform, bind pose = the driver's inverse bind x the alignment); a rigid one
// (a helmet / goggles on the Helmet / Goggles node, a neck brace) hangs on its driver, moved into that bone's rest frame, the head pieces by
// the head alignment (Alive's reference helmet box laid on a Reflex helmet's: RxGearRig.Head).
// A suit and boots are then SHAPED to Reflex's rider (ShapedPositions; RxGearRig.cs "the size"): Alive's rider is the bulkier one, so on a
// Reflex bike its gear looked a size too big.
// The materials borrow the Reflex gear program of their role from the rider's own stock piece of the slot (else the Answer reference
// outfit) with the piece's maps in its texture slots.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Reflex.Port.Content
{
    public sealed partial class ReflexRiderModel
    {
        /// A Reflex helmet's bounds in the rider's rest space (the Answer helmet of every install), null when it cannot be read.
        static (Vector3 min, Vector3 max)? s_headBox; static bool s_headBoxTried;
        static (Vector3 min, Vector3 max)? ReflexHeadBox(Rfx.Install install, Rfx.Model hier)
        {
            if (s_headBoxTried) return s_headBox;
            s_headBoxTried = true;
            try
            {
                var pk = install.Pkg("Rider_Helmets_Models", "Helmet_Answer"); if (pk == null) return null;
                string asset = pk.OfType("model").Select(a => a.name).FirstOrDefault(n => n.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0);
                var (model, surfaces) = LoadGear(install, pk, asset);
                int node = hier.NodeIndex(model.nodes[0].name); if (node < 0) return null;
                var w = hier.nodes[node].world;
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                foreach (var b in model.Lod(0))
                {
                    if (b.material.IndexOf("Strap", StringComparison.OrdinalIgnoreCase) >= 0 || b.material.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    foreach (var v in surfaces[b.surface].positions) { var p = w.MultiplyPoint3x4(v); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                }
                if (hi.y > lo.y) s_headBox = (lo, hi);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException) { Debug.LogWarning("Reflex rider: the head reference: " + e.Message); }
            return s_headBox;
        }

        // ---- the size: a suit and boots are shaped to Reflex's rider (RxGearRig.cs "the size") ----

        /// Reflex's rider as the shape's reference: the stock jersey's outline (Jerseys_BaseModel: every Reflex jersey is this one model; its
        /// gloves and skin left out) and the stock boots' boxes (the Answer boots of every install), in the rider's rest space.
        static RxGearRig.Frame s_shapeFrame; static RxGearRig.Body s_shapeBody; static Vector3[] s_bootMin, s_bootMax; static bool s_shapeTried;
        static void ShapeReference(Rfx.Install install, Rfx.Model hier, string[] rn, Matrix4x4[] rw)
        {
            if (s_shapeTried) return;
            s_shapeTried = true;
            try
            {
                var frame = new RxGearRig.Frame(rn, rw); if (!frame.ok) return;
                IEnumerable<Vector3> Rest(string db, string pkg, Func<Rfx.Model, Rfx.Binding, bool> take)
                {
                    var pk = install.Pkg(db, pkg); if (pk == null) yield break;
                    string asset = pk.OfType("model").Select(a => a.name).FirstOrDefault(n => n.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0); if (asset == null) yield break;
                    var (model, surfaces) = LoadGear(install, pk, asset);
                    foreach (var b in model.Lod(0))
                    {
                        if (!take(model, b)) continue;
                        var s = surfaces[b.surface];
                        if (s.skinned) foreach (var v in s.positions) yield return v;
                        else
                        {
                            int node = hier.NodeIndex(model.nodes[b.node].name); if (node < 0) continue;
                            foreach (var v in s.positions) yield return hier.nodes[node].world.MultiplyPoint3x4(v);
                        }
                    }
                }
                var body = new RxGearRig.Body(frame); int n = 0;
                foreach (var p in Rest("Rider_Jerseys_Models", "Jerseys_BaseModel", (m, b) => !string.Equals(m.nodes[b.node].name, "Gloves", StringComparison.OrdinalIgnoreCase) && b.material.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) < 0))
                { body.Add(p); n++; }
                s_shapeFrame = frame;
                if (n > 0) s_shapeBody = body.Done();
                if (RxGearRig.BootBoxes(Rest("Rider_Boots_Models", "Boots_Answer", (m, b) => true), out var lo, out var hi)) { s_bootMin = lo; s_bootMax = hi; }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException) { Debug.LogWarning("Reflex rider: the size reference: " + e.Message); }
        }

        /// A piece's skinned surfaces' positions shaped to Reflex's rider (per surface of its mesh; null: that surface as it is), made once
        /// per mesh. Null: nothing to shape (a helmet, goggles, a neck brace; no reference).
        static readonly Dictionary<string, (Vector3[][] positions, string note)> s_shaped = new Dictionary<string, (Vector3[][], string)>();
        static Vector3[][] ShapedPositions(RxGearPiece piece, RxMesh mesh, RxGearRig.Fit fit, Rfx.Install install, Rfx.Model hier, string[] rn, Matrix4x4[] rw, out string note)
        {
            note = null;
            bool suit = piece.slot == "jersey", boots = piece.slot == "boots";
            if (!RxGearRig.ShapeToReflex || !(suit || boots)) return null;
            ShapeReference(install, hier, rn, rw);
            if (s_shapeFrame == null || (suit && s_shapeBody == null) || (boots && s_bootMin == null)) return null;
            string key = piece.mod.hash + "/" + piece.mesh + "/" + piece.slot;
            lock (s_shaped) if (s_shaped.TryGetValue(key, out var c)) { note = c.note; return c.positions; }
            bool Takes(int si) { var (s, _, node) = mesh.surfaces[si]; return s.skinned && s.bones != null && !string.Equals(mesh.nodes[node], "Gloves", StringComparison.OrdinalIgnoreCase); }
            var body = new RxGearRig.Body(s_shapeFrame); var all = new List<Vector3>();
            for (int si = 0; si < mesh.surfaces.Count; si++)
            {
                if (!Takes(si)) continue;
                var s = mesh.surfaces[si].surface;
                foreach (var P in RxGearRig.Fitted(s.positions, s.bones, s.weights, fit.G)) { all.Add(P); if (suit) body.Add(P); }
            }
            Func<Vector3, Vector3> move;
            if (suit)
            {
                var shape = new RxGearRig.Shape(s_shapeFrame, s_shapeBody, body.Done());
                move = shape.Move;
                note = $"shaped to Reflex's rider: trunk {shape.Note(0)}, upper arm {shape.Note(1)}, forearm {shape.Note(2)}, thigh {shape.Note(3)}, shin {shape.Note(4)}";
            }
            else
            {
                if (!RxGearRig.BootBoxes(all, out var lo, out var hi)) return null;
                Vector3[] rl = s_bootMin, rh = s_bootMax;
                move = p => RxGearRig.MoveBoot(p, lo, hi, rl, rh);
                note = FormattableString.Invariant($"shaped to Reflex's boots: sole {lo[0].y:0.000} -> {rl[0].y:0.000} ft, height {hi[0].y - lo[0].y:0.00} -> {rh[0].y - rl[0].y:0.00}");
            }
            var made = new Vector3[mesh.surfaces.Count][];
            for (int si = 0; si < mesh.surfaces.Count; si++)
            {
                if (!Takes(si)) continue;
                var s = mesh.surfaces[si].surface;
                made[si] = RxGearRig.Shaped(s.positions, s.bones, s.weights, fit.G, move);
            }
            lock (s_shaped) s_shaped[key] = (made, note);
            return made;
        }

        /// A surface with other positions (the shared, parsed one is not changed).
        static Rfx.Surface WithPositions(Rfx.Surface s, Vector3[] positions) => new Rfx.Surface
        {
            skinned = s.skinned, positions = positions, normals = s.normals, uv0 = s.uv0, uv1 = s.uv1, colors = s.colors, tangents = s.tangents, tangentW = s.tangentW,
            bones = s.bones, weights = s.weights, strip = s.strip, triangles = s.triangles, invalid = s.invalid
        };

        /// The Reflex program a gear material's role borrows.
        static string RxGearProgram(string role)
        {
            switch (role)
            {
                case "jersey": return "Jersey_FNL";
                case "helmet": return "Helmet_FNL";
                case "boots": return "Boots_FNL";
                case "goggles": case "strap": return "Goggle_FNL";
                case "lens": return "Lens_Clr_FNL";
                default: return "Helmet_FNL";   // (a neck brace: hard plastic)
            }
        }

        /// The stock material of a program: the Answer reference outfit's (RiderSpec.AnswerRed: in every install), as the game resolves its bindings.
        static VehicleMaterialDef RxGearTemplate(VehicleCatalog cat, Rfx.Install install, RiderSpec.Part part, List<(string db, string pkg)> chosen, string program)
        {
            VehicleMaterialDef Among(string mdb, string mpkg, List<(string db, string pkg)> pool)
            {
                var pk = mdb == null ? null : install.Pkg(mdb, mpkg); if (pk == null) return null;
                string asset = pk.OfType("model").Select(a => a.name).FirstOrDefault(n => n.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) < 0); if (asset == null) return null;
                var (model, _) = LoadGear(install, pk, asset);
                foreach (string binding in model.Lod(0).Select(b => b.material).Distinct())
                {
                    var def = cat.Resolve(binding, (mdb, mpkg), pool);
                    if (def != null && string.Equals(def.pixelShader, program, StringComparison.OrdinalIgnoreCase)) return def;
                }
                return null;
            }
            try
            {
                // (never the rider's own piece of the slot: in a race that piece may be a gear / kit pack's - a custom gear skin - whose material
                // names its maps its own way and comes with its own maps: the Alive piece's maps then went into the wrong slots and the custom
                // skin's stayed in the others, drawn over the Alive mesh. The reference outfit's material is the same in the garage and the race.)
                var r = RiderSpec.AnswerRed();
                var pool = r.parts.Select(p => (p.matsDb, p.matsPkg)).ToList();
                foreach (var p in r.parts) { var d = Among(p.modelDb, p.modelPkg, pool); if (d != null) return d; }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException) { Debug.LogWarning("Reflex rider: gear template " + program + ": " + e.Message); }
            return null;
        }

        /// A piece's material: the stock material of its role's program with the piece's diffuse, SFX / combo and normal map in its slots
        /// (the mud noise stays; the name / number plate stays on a jersey that carries one, else it is clear).
        static UnityEngine.Material RxGearMaterial(VehicleCatalog cat, Rfx.Install install, RiderSpec.Part part, List<(string db, string pkg)> chosen, RxGearPiece piece, RxGearMaterial mm, string name)
        {
            string program = RxGearProgram(mm.role);
            var t = RxGearTemplate(cat, install, part, chosen, program) ?? (mm.role == "lens" ? null : RxGearTemplate(cat, install, part, chosen, "Helmet_FNL"));
            if (t == null) { Debug.LogWarning($"Reflex rider: {piece.name}: no stock {program} material to borrow for {name} (a plain grey)"); return cat.Build(null, "rxgear " + name); }
            var d = new VehicleMaterialDef { db = t.db, pkg = t.pkg, name = t.name, pixelShader = t.pixelShader, package = t.package, cull = mm.twoSided ? 1 : t.cull, alphaRef = mm.alphaTest ? mm.alphaRef : t.alphaRef,
                                             alphaFunc = mm.alphaTest ? 7 : t.alphaFunc, srcBlend = t.srcBlend, destBlend = t.destBlend, zwrite = t.zwrite, alphaTest = mm.alphaTest || t.alphaTest, alphaBlend = t.alphaBlend };
            foreach (var kv in t.parameters) d.parameters[kv.Key] = kv.Value;
            if (mm.role == "lens") foreach (var kv in mm.constants) if (d.parameters.ContainsKey(kv.Key)) d.parameters[kv.Key] = kv.Value.Length >= 4 ? kv.Value : new[] { kv.Value[0], kv.Value[1], kv.Value[2], 1f };
            // the maps as RxModBikes loads them (the normal map: 8-bit -> the programs' signed half floats when the stock one is G16R16F)
            var maps = new RxModMaterial { name = name, diffuse = mm.diffuse, sfx = mm.sfx, normal = mm.normal, diffuseMod = piece.mod, sfxMod = piece.mod, normalMod = piece.mod, specMod = piece.mod };
            int diffuse = -1;
            foreach (var x in t.textures)
            {
                var c = new VehicleMaterialDef.Tex { name = x.name, sampler = x.sampler, addrU = x.addrU, addrV = x.addrV, mipFilter = x.mipFilter, magFilter = x.magFilter, minFilter = x.minFilter, maxAniso = x.maxAniso, mipBias = x.mipBias, asset = x.asset };
                string n = x.name ?? "";
                if (n.IndexOf("Mud_Noise", StringComparison.OrdinalIgnoreCase) >= 0) { }
                else if (n.IndexOf("NamNum", StringComparison.OrdinalIgnoreCase) >= 0 || n.Equals("Numbers", StringComparison.OrdinalIgnoreCase)) { if (!mm.plate) { c.name = "rxgear no plate"; c.external = VehicleCatalog.ClearTexture; } }
                else if (n.EndsWith("_SFX", StringComparison.OrdinalIgnoreCase) || n.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0) c.external = RxModBikes.Map(piece.mod, mm.sfx) ?? VehicleCatalog.ClearTexture;
                else if (n.IndexOf("Nrm", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("_Norm", StringComparison.OrdinalIgnoreCase) >= 0)
                {   // the jersey program's normal map is G16R16F with x / y stored 0 .. 1 (it takes 2 v - 1; not the tyres' signed one: fed signed, every
                    // normal lay over on its side and the suit was lit flat and dark): converted so; the boots' is an 8-bit map: the piece's as it is
                    // (never block-compressed: a normal map is not colour)
                    if (mm.normal == null) { if (mm.role == "jersey") c.external = FlatGearNormal; }
                    else c.external = mm.role == "jersey" ? RxModBikes.Normal(maps, true) ?? FlatGearNormal : RxModBikes.Map(piece.mod, mm.normal, true);
                }
                else if (diffuse < 0 && n.IndexOf("Texture_Default", StringComparison.OrdinalIgnoreCase) < 0) diffuse = d.textures.Count;
                d.textures.Add(c);
            }
            if (diffuse >= 0 && mm.diffuse != null) d.textures[diffuse].external = RxModBikes.Map(piece.mod, mm.diffuse) ?? Texture2D.grayTexture;
            var built = cat.Build(d, "rxgear " + name + " (" + mm.role + ": " + t.name + ")");
            if (built && built.HasProperty("usr_mud_amount")) built.SetVector("usr_mud_amount", Vector4.zero);
            return built;
        }

        /// The tangents of a gear surface that has none (the Alive importer's .rxmesh files carry positions, normals and UVs only). The gear
        /// programs light through the tangent frame (Jersey_FNL / Helmet_FNL / Boots_FNL: normal = x T + y (N x T) + z N), so without them
        /// the pieces were lit flat and dark. As the game's own gear has them (measured on MXRider_Jersey, the Answer helmet and boots:
        /// dot 0.96 .. 0.99 with this): the unit direction of growing u on the surface, made square to the normal.
        static void GearTangents(Rfx.Surface s)
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
                if (T.sqrMagnitude < 1e-12f) { T = Vector3.Cross(N, Mathf.Abs(N.y) < 0.9f ? Vector3.up : Vector3.right); }   // (no UV direction there: any direction square to the normal)
                tan[i] = T.normalized;
            }
            s.tangents = tan;
        }

        static Texture2D flatGearNormal;
        static Texture2D FlatGearNormal
        {
            get
            {
                if (!flatGearNormal) { flatGearNormal = new Texture2D(4, 4, TextureFormat.RGHalf, false, true) { name = "rxgear flat normal", hideFlags = HideFlags.DontSave }; flatGearNormal.SetPixels(Enumerable.Repeat(new Color(0.5f, 0.5f, 0, 1), 16).ToArray()); flatGearNormal.Apply(); }
                return flatGearNormal;
            }
        }

        /// Builds a gear piece's surfaces on the rider's skeleton. worn: the outfit's other .rxmod pieces (a helmet's goggle strap takes the
        /// worn goggles' strap material).
        static void BuildRxGear(ReflexRiderModel result, RiderSpec spec, RiderSpec.Part part, Rfx.Model hier, Transform[] bones, Matrix4x4[] bindposes, GameObject root,
                                VehicleCatalog cat, Rfx.Install install, List<(string db, string pkg)> chosen, List<SkinnedMeshRenderer> skinned, List<Renderer> rigid)
        {
            var piece = part.rx;
            var mesh = RxModBikes.Mesh(piece.mod, piece.mesh);
            string[] rn = hier.nodes.Select(n => n.name).ToArray(); var rw = hier.nodes.Select(n => n.world).ToArray();
            var fit = RxGearRig.Make(mesh.nodes, mesh.world, rn, rw);
            var head = fit.head;
            var rbox = piece.headBox != null ? ReflexHeadBox(install, hier) : null;
            if (rbox != null) head = RxGearRig.Head(new Vector3(piece.headBox[0], piece.headBox[1], piece.headBox[2]), new Vector3(piece.headBox[3], piece.headBox[4], piece.headBox[5]), rbox.Value.min, rbox.Value.max);
            int n = mesh.nodes.Length;
            var drivers = new Transform[n]; var bind = new Matrix4x4[n];
            for (int i = 0; i < n; i++)
            {
                bool onHead = string.Equals(mesh.nodes[i], "Helmet", StringComparison.OrdinalIgnoreCase) || string.Equals(mesh.nodes[i], "Goggles", StringComparison.OrdinalIgnoreCase) || string.Equals(mesh.nodes[i], "MXRider_Head", StringComparison.OrdinalIgnoreCase);
                drivers[i] = bones[fit.driver[i]];
                bind[i] = bindposes[fit.driver[i]] * (onHead ? head : fit.G[i]);
            }
            var materials = new Dictionary<string, UnityEngine.Material>(StringComparer.OrdinalIgnoreCase);
            UnityEngine.Material MaterialOf(string name)
            {
                if (materials.TryGetValue(name, out var mat)) return mat;
                if (piece.materials.TryGetValue(name, out var mm)) mat = RxGearMaterial(cat, install, part, chosen, piece, mm, name);
                else
                {   // a binding the piece has no material for: a helmet's goggle strap takes the worn goggles' (an Alive pair), else a dark strap
                    var goggles = spec.parts.Select(p => p.rx).FirstOrDefault(p => p != null && p.slot == "goggles");
                    var strap = goggles?.materials.Values.FirstOrDefault(x => x.role == "strap") ?? goggles?.materials.Values.FirstOrDefault(x => x.role == "goggles");
                    if (name.IndexOf("Strap", StringComparison.OrdinalIgnoreCase) >= 0 && strap != null) mat = RxGearMaterial(cat, install, part, chosen, goggles, strap, name);
                    else { mat = cat.Build(null, "rxgear " + name); if (mat && mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.06f, 0.06f, 0.06f)); }
                }
                result.parts.Add(($"{piece.mod.id}/{piece.pieceId}:{name}", name, "rxgear"));
                return materials[name] = mat;
            }
            var shaped = ShapedPositions(piece, mesh, fit, install, hier, rn, rw, out string shapeNote);   // (the size: a suit's girth and the boots as Reflex's)
            int skippedGloves = 0;
            for (int si = 0; si < mesh.surfaces.Count; si++)
            {
                var (s, mi, node) = mesh.surfaces[si];
                // a suit's own gloves (its surface on the Gloves node) are not drawn: Reflex's gloves are, on Reflex's fourteen finger bones
                // (Alive's seven follow the wrist stiffly); drawn both, the rider had two pairs of hands
                if (piece.slot == "jersey" && part.modelPkg != null && string.Equals(mesh.nodes[node], "Gloves", StringComparison.OrdinalIgnoreCase)) { skippedGloves++; continue; }
                GearTangents(s);
                string matName = mesh.materials[mi];
                string name = $"rxgear_{piece.slot}_{piece.pieceId}_s{si}_{matName}";
                var go = new GameObject(name);
                if (s.skinned && s.bones != null)
                {
                    go.transform.SetParent(root.transform, false);
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = ReflexMeshes.Build(name, shaped?[si] != null ? WithPositions(s, shaped[si]) : s, Matrix4x4.identity, true, bind, null);
                    smr.sharedMaterial = MaterialOf(matName); smr.bones = drivers; smr.rootBone = bones[0];
                    smr.updateWhenOffscreen = false;
                    smr.localBounds = new Bounds(Vector3.zero, Vector3.one * RiderBoundsFeet);
                    skinned.Add(smr);
                }
                else
                {   // rigid on its node: Alive's rest (the node's world x the vertex) through the alignment into the driver's rest frame
                    go.transform.SetParent(drivers[node], false);
                    go.AddComponent<MeshFilter>().sharedMesh = ReflexMeshes.Build(name, s, bind[node] * mesh.world[node], false);
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = MaterialOf(matName); rigid.Add(mr);
                }
            }
            Debug.Log($"Reflex rider: {piece.slot} {piece.line} {piece.name} ({piece.mod.id}): {mesh.surfaces.Count} surfaces{(skippedGloves > 0 ? " (its gloves not drawn: Reflex's are)" : "")}, {mesh.Vertices} vertices on Alive's rig fitted to MXRider_Hierarchy" + (shapeNote != null ? ", " + shapeNote : "") + (rbox != null ? $", head box {rbox.Value.min:F2} .. {rbox.Value.max:F2}" : ""));
        }
    }
}
