using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Reflex.Port.Content
{
    // RXSC preserves the source material layers, vertex lighting, alpha and object placements.
    public static class ExperimentalMtxScenery
    {
        static void Require(bool value,string why) { if(!value) throw new InvalidDataException("RXSC: "+why); }
        sealed class LayerDef { public uint texture; public int blend, fixedAlpha, uvSet; public bool env, ignoreVertexAlpha, clampU, clampV; public float tileU, tileV; public Color color = Color.white; }
        sealed class MaterialDef
        {
            public uint texture; public int blend, cutoff, fixedAlpha; public bool texAlpha, flat, ignoreVertexAlpha, clampU, clampV, env, foliage; public float tileU = 1, tileV = 1;
            // A real alpha cutoff cuts the texture out whatever the blend (foliage, tree cards, fences: never sorted).
            // An Unleashed package of material rules 2 carries the source's own alpha test (the mesh template's is "> 0": only clear texels
            // are dropped, a mask in the alpha channel of a wall or a truck leaves it whole): an opaque pass is cut out at that test.
            public bool exact;
            public bool Cutout => texAlpha && (cutoff >= 16 || exact && cutoff >= 1 && blend == 0);
            /// The shader's _Cutoff (0: no alpha test). The Unleashed plants keep leaf silhouettes in the depth, normal and shadow passes;
            /// under material rules 2 everything else is cut at the source's reference.
            public float Cutoff(bool unleashed) => !Cutout ? 0 : unleashed && (!exact || foliage || plant) ? .4f : exact ? Mathf.Clamp((cutoff - .5f) / 255f, .004f, .95f) : Mathf.Max(.08f, cutoff / 255f);
            public bool plant;   // drawn by a plant (a billboard card or an ecosystem model)
            public Color color = Color.white;
            public readonly List<LayerDef> layers = new List<LayerDef>();
        }

        /// The venue's scenery from its render cache (world.mtx) under parent; the sky domes go in skies. Returns the counts.
        internal static (int surfaces, int textures) BuildScenery(byte[] data, Transform parent, List<UnityEngine.Object> owned, List<Transform> skies)
        {
            T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
            var shader = UnityEngine.Resources.Load<Shader>("ReflexMtxScenery"); Require(shader, "world shader");
            // MTX lighting (ExperimentalMtxLighting.cs): raw texture values like every Reflex world shader (MTX's own texture x prelit colour
            // x 2), the venue's world / sky tint and Reflex's distance fog. REFLEX_MTX_LIGHT=legacy: sRGB textures as before.
            var venue = ExperimentalMtxLighting.Current; bool raw = venue != null || UnleashedLevels.Bound != null;
            bool unleashed = UnleashedLevels.Bound != null, exact = unleashed && UnleashedLevels.Bound.material_rules >= 2;
            var fxMaterials = new Dictionary<int, List<Material>>(); long triangles = 0; int hiddenGroups = 0;
            var textures = new Dictionary<uint, Texture2D>(); int surfaces = 0;
            var sources = new List<(uint id, int w, int h, byte[] px)>();   // the pixels wait for the materials: what a texture is used for decides its mips
            var built = new Dictionary<(int, bool, bool, bool), Material>();
            using var r = new BinaryReader(new MemoryStream(data, false));
            Require(r.ReadInt32() == 0x43535852, "scenery magic");
            int version=r.ReadInt32(); Require(version>=1 && version<=3, "scenery version");
            int nt = r.ReadInt32(); Require(nt >= 0 && nt < 8192, "texture count");
            for (int i = 0; i < nt; i++)
            {
                uint id = r.ReadUInt32(); int w = r.ReadInt32(), h = r.ReadInt32(); Require(w > 0 && w <= 4096 && h > 0 && h <= 4096 && (long)w*h*4 <= r.BaseStream.Length-r.BaseStream.Position, "texture size");
                sources.Add((id, w, h, r.ReadBytes(w * h * 4)));
            }
            int nm = r.ReadInt32(); Require(nm >= 0 && nm < 100000, "material count");
            var defs = new List<MaterialDef>();
            for (int i = 0; i < nm; i++)
            {
                var d = new MaterialDef { texture = r.ReadUInt32(), blend = r.ReadByte(), cutoff = r.ReadByte(), texAlpha = r.ReadBoolean(), flat = r.ReadBoolean(), fixedAlpha = r.ReadByte(), ignoreVertexAlpha = r.ReadBoolean(), clampU = r.ReadBoolean(), clampV = r.ReadBoolean() };
                d.exact = exact; d.color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1); d.env = r.ReadBoolean(); d.tileU = r.ReadSingle(); d.tileV = r.ReadSingle();
                int layers = r.ReadByte(); Require(layers <= 3, "material layers");
                for (int k = 0; k < layers; k++)
                {
                    var l = new LayerDef { texture = r.ReadUInt32(), blend = r.ReadByte(), fixedAlpha = r.ReadByte(), env = r.ReadBoolean(), ignoreVertexAlpha = r.ReadBoolean(), tileU = r.ReadSingle(), tileV = r.ReadSingle(), uvSet = r.ReadByte(), clampU = r.ReadBoolean(), clampV = r.ReadBoolean() };
                    l.color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1); d.layers.Add(l);
                }
                defs.Add(d);
            }
            // What each texture is used for: the alpha test it is cut by (its mips keep that coverage), whether its alpha shows at all (the
            // colour under its clear texels is filled in), and its address modes (one mode only: the texture's own, no copy).
            var byId = new Dictionary<uint, int>(); for (int i = 0; i < sources.Count; i++) byId[sources[i].id] = i;
            var cut = new Dictionary<uint, float>(); var alphaUse = new HashSet<uint>(); var wraps = new Dictionary<uint, HashSet<(bool, bool)>>();
            void Wrap(uint id, bool u, bool v) { if (!wraps.TryGetValue(id, out var set)) wraps[id] = set = new HashSet<(bool, bool)>(); set.Add((u, v)); }
            if (exact) MarkPlants(data, r.BaseStream.Position, defs);
            foreach (var d in defs)
            {
                // The Unleashed plants are blended passes with no alpha test over an atlas of hard-edged leaves (alpha 0 or 255): unsorted
                // and without depth as a blend, they are cut out like the foliage they are (depth, shadows, occlusion, the leaf lighting).
                // A soft alpha (a waterfall, mist, a glow) stays the blend it was.
                if (unleashed && d.texAlpha && !d.Cutout && !d.flat && (d.blend == 5 || d.blend == 6) && byId.TryGetValue(d.texture, out int si) && HardAlpha(sources[si].px)) { d.cutoff = 128; d.foliage = true; }
                Wrap(d.texture, d.clampU, d.clampV);
                if (d.texAlpha) alphaUse.Add(d.texture);
                float c = d.Cutoff(unleashed); if (c > 0 && (!cut.TryGetValue(d.texture, out float was) || c < was)) cut[d.texture] = c;
                foreach (var l in d.layers) Wrap(l.texture, l.clampU, l.clampV);
            }
            // the later frames of an animated texture take their first frame's alpha test, mips and address mode
            if (unleashed) foreach (var kv in UnleashedSceneryFx.FrameSources())
            {
                if (cut.TryGetValue(kv.Value, out float fc)) cut[kv.Key] = fc;
                if (alphaUse.Contains(kv.Value)) alphaUse.Add(kv.Key);
                if (wraps.TryGetValue(kv.Value, out var fw)) wraps[kv.Key] = fw;
            }
            foreach (var (id, w, h, px) in sources)
            {
                bool one = wraps.TryGetValue(id, out var modes) && modes.Count == 1; var mode = one ? System.Linq.Enumerable.First(modes) : (false, false);
                textures[id] = Own(MakeTexture($"mtx {id:x8}", w, h, px, raw, cut.TryGetValue(id, out float c) ? c : 0, alphaUse.Contains(id), mode.Item1, mode.Item2));
            }
            sources.Clear();
            // A texture a material clamps is a GPU copy with that address mode (textures are shared between materials).
            var clamps = new Dictionary<(Texture2D, bool, bool), Texture2D>();
            Texture2D Clamped(Texture2D t, bool u, bool v)
            {
                if ((t.wrapModeU == TextureWrapMode.Clamp) == u && (t.wrapModeV == TextureWrapMode.Clamp) == v) return t;   // its only mode: the texture itself
                if (clamps.TryGetValue((t, u, v), out var c)) return c;
                if (SystemInfo.copyTextureSupport == CopyTextureSupport.None) return clamps[(t, u, v)] = t;
                c = Own(new Texture2D(t.width, t.height, t.format, t.mipmapCount, raw) { name = t.name + " clamped", filterMode = FilterMode.Trilinear, anisoLevel = 1,
                    wrapModeU = u ? TextureWrapMode.Clamp : TextureWrapMode.Repeat, wrapModeV = v ? TextureWrapMode.Clamp : TextureWrapMode.Repeat });
                Graphics.CopyTexture(t, c);
                return clamps[(t, u, v)] = Reflex.Port.Rendering.ReflexTextureFiltering.Register(c);
            }
            // the goal props: one child per named object (hidden ones inactive until a goal shows them)
            int ng = r.ReadInt32(); Require(ng >= 0 && ng < 10000, "prop count");
            var groups = new Transform[ng];
            for (int g = 0; g < ng; g++)
            {
                string name = r.ReadString(); bool hidden = r.ReadBoolean();
                var go = new GameObject(name); go.transform.SetParent(parent, false); go.SetActive(!hidden); groups[g] = go.transform; if (hidden) hiddenGroups++;
                if (r.ReadBoolean())   // a level object, modelled about its own origin: at its node
                {
                    go.transform.localPosition = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    go.transform.localRotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    ExperimentalMtxSpinner.Attach(go, name);
                }

            }
            int nd = r.ReadInt32(); Require(nd > 0 && nd < 100000, "draw count");
            for (int di = 0; di < nd; di++)
            {
                int mi = r.ReadInt32(); bool sky = r.ReadBoolean(), billboard = r.ReadBoolean(); int group = r.ReadInt32(), nv = r.ReadInt32(), ni = r.ReadInt32();
                Require(group >= -1 && group < ng, "prop index");
                Require(mi >= 0 && mi < nm && nv > 0 && nv <= 4000000 && ni >= 3 && ni % 3 == 0 && ni <= 12000000, "mesh size");
                Require((long)nv*(billboard?68:48)+(long)ni*4 <= r.BaseStream.Length-r.BaseStream.Position, "truncated mesh");
                var vertices = new Vector3[nv]; var uvs = new Vector2[4][]; for (int u = 0; u < 4; u++) uvs[u] = new Vector2[nv];
                var colors = new Color32[nv]; var origins = billboard ? new Vector3[nv] : null; var offsets = billboard ? new Vector2[nv] : null;
                for (int i = 0; i < nv; i++)
                {
                    vertices[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int u = 0; u < 4; u++) uvs[u][i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    colors[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                    if (billboard) { origins[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); offsets[i] = new Vector2(r.ReadSingle(), r.ReadSingle()); }
                }
                var indices = new int[ni]; for (int i = 0; i < ni; i++) { indices[i] = r.ReadInt32(); Require((uint)indices[i] < (uint)nv, "triangle index"); }
                if (version >= 3)
                {
                    // Root scales placement with the terrain. Cancel that scale only around
                    // each source object's common base pivot, including billboard offsets.
                    float scale = Reflex.Port.RxCore.RxMapScale.Current;
                    for (int i = 0; i < nv; i++)
                    {
                        var anchor = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        float fixedSize = r.ReadSingle();
                        Require(float.IsFinite(anchor.x) && float.IsFinite(anchor.y) && float.IsFinite(anchor.z)
                            && (fixedSize == 0 || fixedSize == 1), "prop anchor");
                        if (fixedSize == 0) continue;
                        vertices[i] = anchor + (vertices[i] - anchor) / scale;
                        if (billboard) { origins[i] = anchor + (origins[i] - anchor) / scale; offsets[i] /= scale; }
                    }
                }
                var mesh = Own(new Mesh { name = $"mtx {mi}{(sky ? " sky" : "")}{(billboard ? " billboard" : "")}", indexFormat = nv > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 });
                mesh.vertices = vertices; for (int u = 0; u < 4; u++) mesh.SetUVs(u, uvs[u]); mesh.colors32 = colors;
                if (billboard) { mesh.SetUVs(4, origins); mesh.SetUVs(5, offsets); }
                mesh.triangles = indices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                if (billboard) { var bnd = mesh.bounds; bnd.Expand(new Vector3(bnd.size.y, 0, bnd.size.y)); mesh.bounds = bnd; }
                mesh.UploadMeshData(!Application.isEditor);   // goal props stay readable (the hoops' glass shatters)
                // A blended pass over an opaque texture whose vertex alpha is painted (clear to solid across the mesh) fades by it:
                // the wet overlays laid over a track (Bayou's puddles, at most ~35%). Uniform vertex alpha (a banner, a sign) stays opaque.
                byte amin = 255, amax = 0; foreach (var c in colors) { if (c.a < amin) amin = c.a; if (c.a > amax) amax = c.a; }
                bool painted = amin < 0x20 && amax >= amin + 16;
                if (!built.TryGetValue((mi, sky, billboard, painted), out var mat))
                {
                    built[(mi, sky, billboard, painted)] = mat = Own(MakeMaterial(shader, defs[mi], textures, sky, billboard, Clamped, painted, venue, raw));
                    if (!fxMaterials.TryGetValue(mi, out var list)) fxMaterials[mi] = list = new List<Material>();
                    list.Add(mat);
                }
                triangles += ni / 3;
                var go = new GameObject(mesh.name); go.transform.SetParent(group >= 0 ? groups[group] : parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; surfaces++;
                if (sky) skies?.Add(go.transform);
            }
            if(version>=2) ExperimentalMtxAnimation.Read(r,parent.gameObject,groups);
            Require(r.BaseStream.Position == r.BaseStream.Length, "render cache end");
            if (unleashed) UnleashedSceneryFx.Attach(parent, fxMaterials, textures, Clamped, owned);
            Audit(surfaces, textures.Count, nm, ng, hiddenGroups, triangles, skies?.Count ?? 0, parent);
            return (surfaces, textures.Count);
        }

        /// REFLEX_SCENERY_AUDIT=<dir>: what the client built from the package, for tools/trackaudit (drawn vs converted vs source).
        static void Audit(int surfaces, int textures, int materials, int groups, int hiddenGroups, long triangles, int skies, Transform parent)
        {
            string dir = Environment.GetEnvironmentVariable("REFLEX_SCENERY_AUDIT"); if (string.IsNullOrEmpty(dir)) return;
            try
            {
                string name = UnleashedLevels.Bound?.level ?? TrackMods.Bound?.id ?? TrackMods.Bound?.level ?? "scenery"; int active = 0, renderers = 0;
                foreach (var mr in parent.GetComponentsInChildren<MeshRenderer>(true)) { renderers++; if (mr.gameObject.activeInHierarchy && mr.enabled) active++; }
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, name + ".json"), "{\"level\":\"" + name + "\",\"surfaces\":" + surfaces + ",\"renderers\":" + renderers + ",\"activeRenderers\":" + active + ",\"textures\":" + textures
                    + ",\"materials\":" + materials + ",\"groups\":" + groups + ",\"hiddenGroups\":" + hiddenGroups + ",\"triangles\":" + triangles + ",\"skyMeshes\":" + skies
                    + ",\"animatedMaterials\":" + UnleashedSceneryFx.LastCount + "}");
            }
            catch (Exception ex) { Debug.LogWarning("[MTX] scenery audit: " + ex.Message); }
        }

        /// The materials the plants draw with (the billboard cards and the ecosystem models' groups): a pass over the draw headers.
        static void MarkPlants(byte[] data, long at, List<MaterialDef> defs)
        {
            using var r = new BinaryReader(new MemoryStream(data, false)); r.BaseStream.Position = at;
            int ng = r.ReadInt32(); if (ng < 0 || ng >= 10000) return;
            var toys = new bool[ng];   // an animated prop's parts (UnleashedToys) are props, not plants
            for (int g = 0; g < ng; g++) { { string gn = r.ReadString(); toys[g] = gn.StartsWith("UnleashedToy:", StringComparison.Ordinal) || gn.StartsWith("UnleashedFunnel:", StringComparison.Ordinal); } r.ReadBoolean(); if (r.ReadBoolean()) r.BaseStream.Position += 28; }
            int nd = r.ReadInt32();
            for (int di = 0; di < nd && r.BaseStream.Position + 18 <= r.BaseStream.Length; di++)
            {
                int mi = r.ReadInt32(); r.ReadBoolean(); bool billboard = r.ReadBoolean(); int group = r.ReadInt32(), nv = r.ReadInt32(), ni = r.ReadInt32();
                if (mi < 0 || mi >= defs.Count || nv <= 0 || ni < 0) return;
                if ((billboard || group >= 0 && !(group < ng && toys[group])) && defs[mi].blend != 1 && defs[mi].blend != 2) defs[mi].plant = true;
                r.BaseStream.Position += (long)nv * (billboard ? 68 : 48) + (long)ni * 4;
            }
        }

        static Material MakeMaterial(Shader shader, MaterialDef d, Dictionary<uint, Texture2D> textures, bool sky, bool billboard, Func<Texture2D, bool, bool, Texture2D> clamped, bool painted, ExperimentalMtxLighting.Venue venue, bool raw)
        {
            textures.TryGetValue(d.texture, out var t0);
            if (t0) t0 = clamped(t0, d.clampU, d.clampV);   // the pass's address modes (sky panels clamp)
            // The pass colours are raw values in the raw pipeline (Vector properties: SetColor would gamma-decode them and darken every tint
            // that is not white); REFLEX_MTX_LIGHT=legacy (sRGB textures) keeps the decoded colour it had.
            Vector4 Tint(Color c) => raw ? (Vector4)c : (Vector4)c.linear;
            var m = new Material(shader) { name = $"MTX {d.texture:x8}", enableInstancing = true };
            if (t0) m.SetTexture("_MainTex", t0);
            // Layers (Neversoft blend modes): 1/2 add, 3/4 subtract, 5/6 blend, 7/8 modulate, 9/10 brighten, 12/13 blend by the
            // previous pass's alpha (inverse); environment passes are sphere-mapped from the view normal.
            for (int k = 0; k < 3; k++)
            {
                var l = k < d.layers.Count ? d.layers[k] : null; int n = k + 1;
                bool ok = l != null && textures.TryGetValue(l.texture, out var lt) && lt && l.blend != 0 && l.blend != 11 && l.blend <= 13;
                m.SetFloat($"_L{n}Mode", ok ? l.blend : 0);
                if (!ok) continue;
                var ltex = textures[l.texture];
                ltex = clamped(ltex, l.clampU, l.clampV);   // a decal layer: once where its UVs land
                m.SetTexture($"_L{n}Tex", ltex);
                m.SetVector($"_L{n}Params", new Vector4(l.fixedAlpha / 128f, l.env ? 1 : 0, l.ignoreVertexAlpha ? 1 : 0, l.uvSet));
                m.SetVector($"_L{n}Tile", new Vector4(l.tileU > 0 ? l.tileU : 1, l.tileV > 0 ? l.tileV : 1, 0, 0));
                // (an Unleashed pass colour is 2 = the x2 of the stored vertex colours; the shader doubles an unlit material's layers itself)
                m.SetVector($"_L{n}Color", Tint(UnleashedLevels.Bound != null && (d.blend == 1 || d.blend == 2) ? l.color * .5f : l.color));
            }
            bool blended = d.blend == 5 || d.blend == 6, additive = d.blend == 1 || d.blend == 2;
            bool cutout = d.Cutout;
            // Blended passes without one fade by texture alpha x vertex alpha (blend 6: fixed alpha): decals with alpha, the
            // panorama, and the flat shadow / shade decals. An opaque printed texture (banners, signs) stays opaque: its
            // vertex alpha is not a fade.
            bool fade = blended && !cutout && (d.texAlpha || d.flat || painted && !d.ignoreVertexAlpha);
            // ... and still blends what passes the test (the hardware did both): a rotor's pale blur disc stays a faint blur, a
            // leaf's soft edge stays soft; it writes depth like any cut-out, so it needs no sorting.
            bool cutBlend = cutout && blended;
            m.SetFloat("_BaseBlend", fade || additive || cutBlend ? 1 : 0); m.SetFloat("_Cutoff", d.Cutoff(false));
            m.SetFloat("_FixedAlpha", d.blend == 6 || d.blend == 2 ? d.fixedAlpha / 128f : 0);
            m.SetFloat("_IgnoreVertexAlpha", d.ignoreVertexAlpha ? 1 : 0);
            // Shadow decals (a flat grey texture faded by vertex alpha) darken the ground: their colour is not doubled.
            m.SetFloat("_ColorScale", fade && d.flat && !d.texAlpha ? 1 : 2);
            m.SetFloat("_SrcBlend", fade || additive || cutBlend ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            m.SetFloat("_DstBlend", fade || cutBlend ? (float)BlendMode.OneMinusSrcAlpha : additive ? (float)BlendMode.One : (float)BlendMode.Zero);
            m.SetFloat("_Sky", sky ? 1 : 0); m.SetFloat("_ZWrite", sky || fade || additive ? 0 : 1);
            m.SetFloat("_Billboard", billboard ? 1 : 0);
            m.SetVector("_BaseColor", Tint(d.color));
            m.SetVector("_BaseEnv", new Vector4(d.env ? 1 : 0, d.tileU > 0 ? d.tileU : 1, d.tileV > 0 ? d.tileV : 1, 0));
            m.renderQueue = sky ? 1000 : fade || additive ? 3000 : cutout ? 2450 : 2000;
            m.SetFloat("_MtxRaw", venue != null ? 1 : 0);
            if (venue != null)
            {
                // MTX's `lighting` script: world colours toward vc_target by vc_percent, then x lev / 128; the sky x sky / 128
                var world = ExperimentalMtxLighting.WorldTint(venue); var skyTint = ExperimentalMtxLighting.SkyTint(venue);
                m.SetVector("_MtxTint", sky ? new Vector4(skyTint.x, skyTint.y, skyTint.z, 0) : new Vector4(world.x, world.y, world.z, world.w));
                m.SetFloat("_MtxVcPercent", sky ? 0 : skyTint.w);
                // Reflex's fog: none on the sky; additive passes are only dimmed by it (they add no fog colour)
                m.SetFloat("_MtxFog", sky ? 0 : additive ? 2 : 1);
                m.SetFloat("_MtxSkyGamma", sky ? ExperimentalMtxLighting.SkyGamma : 1);
            }
            if (UnleashedLevels.Bound != null && sky)
            {
                // A camera-following dome of the level (the northern lights): over Reflex's sky, behind the world, in the raw pipeline, no fog
                m.SetFloat("_MtxRaw", 1); m.SetFloat("_MtxFog", 0); m.SetFloat("_ColorScale", 1); m.renderQueue = 2510;
            }
            if (UnleashedLevels.Bound != null && !sky)
            {
                m.SetFloat("_MtxRaw", 1); m.SetFloat("_MtxFog", additive ? 2 : 1);
                m.SetFloat("_ReflexLit", additive ? 0 : 1); m.SetFloat("_ColorScale", 1);
                // the leaf lighting: the plants' atlases and their distant cards (a fence or a sign that is cut out is lit as the solid it is)
                m.SetFloat("_Foliage", billboard || d.foliage ? 1 : 0);
                // Keep leaf silhouettes in the depth, normal and shadow passes.
                if (cutout) m.SetFloat("_Cutoff", d.Cutoff(true));
            }
            return m;
        }

        /// An atlas of hard-edged cut-outs: its alpha is clear or solid, the in-between only the filtered edges (and some of both).
        static bool HardAlpha(byte[] px)
        {
            int clear = 0, solid = 0, soft = 0;
            for (int i = 3; i < px.Length; i += 4) { byte a = px[i]; if (a < 24) clear++; else if (a > 231) solid++; else soft++; }
            int n = px.Length / 4;
            return clear > n / 50 && solid > n / 50 && soft * 2 < solid;
        }

        /// A scenery texture from its RGBA8 pixels, at its own sampler state (trilinear, isotropic: TEXTURE FILTERING raises it).
        /// cutoff > 0: the alpha test it is drawn with - every mip keeps the top level's coverage at that test (a box filter thins a
        /// fence, a net or a leaf out with distance until it shimmers and is gone). usesAlpha: its alpha shows (cut out or blended) -
        /// the colour under its clear texels, which filtering and the mips pull into the edges, is filled from the texels around.
        /// Large textures are block-compressed (4096 x 4096 RGBA8 with mips is 85 MB; DXT1 an eighth, DXT5 a quarter).
        static Texture2D MakeTexture(string name, int w, int h, byte[] px, bool linear, float cutoff, bool usesAlpha, bool clampU, bool clampV)
        {
            bool opaque = true; for (int i = 3; i < px.Length && opaque; i += 4) opaque = px[i] == 255;
            bool compress = (long)w * h >= 512 * 512 && (w & 3) == 0 && (h & 3) == 0 && SystemInfo.SupportsTextureFormat(opaque ? TextureFormat.DXT1 : TextureFormat.DXT5);
            if (usesAlpha && !opaque && (long)w * h <= 1 << 20) Bleed(px, w, h);
            Texture2D tex;
            if (compress && opaque)
            {
                var rgb = new byte[w * h * 3]; for (int i = 0, o = 0; i < px.Length; i += 4) { rgb[o++] = px[i]; rgb[o++] = px[i + 1]; rgb[o++] = px[i + 2]; }
                tex = new Texture2D(w, h, TextureFormat.RGB24, true, linear); tex.SetPixelData(rgb, 0);
            }
            else { tex = new Texture2D(w, h, TextureFormat.RGBA32, true, linear); tex.SetPixelData(px, 0); }
            tex.name = name; tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = 1;
            tex.wrapModeU = clampU ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; tex.wrapModeV = clampV ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            tex.Apply(true, false);
            if (cutoff > 0 && !opaque) CoverageMips(tex, px, cutoff);
            if (compress) tex.Compress(true);
            tex.Apply(false, true);
            return Reflex.Port.Rendering.ReflexTextureFiltering.Register(tex);
        }

        /// Scales each mip's alpha so the share of its texels that pass the alpha test is the top level's.
        static void CoverageMips(Texture2D tex, byte[] top, float cutoff)
        {
            int test = Mathf.Clamp(Mathf.RoundToInt(cutoff * 255), 1, 254), n = top.Length / 4; long pass = 0;
            for (int i = 3; i < top.Length; i += 4) if (top[i] >= test) pass++;
            if (pass == 0 || pass == n) return;
            double coverage = (double)pass / n; var hist = new int[256];
            for (int level = 1; level < tex.mipmapCount; level++)
            {
                var data = tex.GetPixelData<Color32>(level); Array.Clear(hist, 0, 256);
                for (int i = 0; i < data.Length; i++) hist[data[i].a]++;
                // the alpha at which this level passes the same share: the test moves there
                long want = Math.Max(1, (long)Math.Round(coverage * data.Length)), sum = 0; int at = 255;
                for (; at > 1; at--) { sum += hist[at]; if (sum >= want) break; }
                float scale = Mathf.Clamp((float)test / at, .25f, 8f);
                if (Mathf.Abs(scale - 1) < .02f) continue;
                for (int i = 0; i < data.Length; i++) { var c = data[i]; c.a = (byte)Mathf.Min(255, (int)(c.a * scale + .5f)); data[i] = c; }
            }
        }

        /// The colour of the texels around flows into the clear ones (alpha < 8, colour usually black), eight texels deep; alpha is kept.
        static void Bleed(byte[] px, int w, int h)
        {
            int n = w * h; var filled = new bool[n]; bool any = false;
            for (int i = 0; i < n; i++) { filled[i] = px[i * 4 + 3] >= 8; any |= !filled[i]; }
            if (!any) return;
            var next = new List<int>(); var rgb = new List<byte>();
            for (int pass = 0; pass < 8; pass++)
            {
                next.Clear(); rgb.Clear();
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; if (filled[i]) continue;
                    int r = 0, g = 0, b = 0, k = 0;
                    void Take(int j) { if (!filled[j]) return; r += px[j * 4]; g += px[j * 4 + 1]; b += px[j * 4 + 2]; k++; }
                    Take(y * w + (x + 1) % w); Take(y * w + (x + w - 1) % w); Take((y + 1) % h * w + x); Take((y + h - 1) % h * w + x);
                    if (k == 0) continue;
                    next.Add(i); rgb.Add((byte)(r / k)); rgb.Add((byte)(g / k)); rgb.Add((byte)(b / k));
                }
                if (next.Count == 0) break;
                for (int j = 0; j < next.Count; j++) { int i = next[j]; px[i * 4] = rgb[j * 3]; px[i * 4 + 1] = rgb[j * 3 + 1]; px[i * 4 + 2] = rgb[j * 3 + 2]; filled[i] = true; }
            }
        }

    }
}
