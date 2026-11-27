using System;
using System.Collections.Generic;
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reflex.Port.Rendering
{
    /// TERRAIN TESSELLATION (OPTIONS > VIDEO, on by default): the original's fine terrain grid around the camera.
    ///
    /// The original does not draw its height field one vertex per sample. HFTerrainSurface_InitDeformAndTracks (007c4890) builds the
    /// terrain a geometry clipmap with the water's mesh class (FUN_007e4280 called at 007c49c8: EAX = 0x80 -> a 129 x 129 grid, 0x40 with
    /// the engine's low-detail flag; XMM0 = [0x92ff84] = 0.25, the finest cell), and the terrain draw (007c4f30 -> 007cd4f0 -> FUN_007e9b20)
    /// draws its levels around the camera, each twice as coarse as the one inside it, with vs_hft_lod0 while a level's half size is at
    /// most 513 units ([0x9ddfe8]) and vs_hft_lod1 beyond. Every vertex fetches the height map, the deformation surface and RT_Bump.z
    /// (vs_hft_lod0 0648 / 0658 / 0870), so near the rider the bump textures' relief and the rut decals are real geometry on a 0.25-unit
    /// grid. Unity's terrain has one vertex per height-map sample (1 or 2 units) at best, so the port smooths RT_Bump.z over two samples.
    ///
    /// With the option on this component draws the same nested grid (129 x 129 per level, finest cell = the height-map spacing divided
    /// down to about 0.25) around the game camera, out to the level whose cell is one height-map sample, with the terrain's own material
    /// (ReflexTerrainInput.hlsl RfxClipVertex). Unity's terrain is lowered out of sight under it (RfxClipSink) and draws as before
    /// beyond. Not the original's: the levels move together on the coarsest level's lattice (the original snaps each level on its own),
    /// the far levels stay Unity's terrain, and RT_Bump.z comes from the world-locked near target where that reaches.
    [DisallowMultipleComponent]
    public sealed class ReflexTerrainClipmap : MonoBehaviour
    {
        public const int GridCells = 128;             // 129 vertices a side (007c4991: mov esi, 0x80, made odd by 007e4280)
        public const float OriginalCell = 0.25f;      // [0x92ff84]
        const float SinkDepth = 3f;

        public OriginalWorld.OriginalTerrain terrain;
        public ReflexRTBump bump;

        /// REFLEX_TERRAIN_TESS=1 / 0 forces the option for a run (tests); Force does the same from code (null: the saved option).
        public static bool? Force;
        public static bool Wanted => Force ?? ReflexSettings.TerrainTessellation == 1;
        /// What the last drawn frame used (tests): levels, vertices, triangles; 0 while off.
        public static int LastLevels, LastVertices, LastTriangles;

        static readonly int OriginId = Shader.PropertyToID("_RfxClipOrigin"), RectId = Shader.PropertyToID("_RfxClipRect"), GridId = Shader.PropertyToID("_RfxClipGrid");
        static readonly int HeightId = Shader.PropertyToID("_RfxClipHeight"), BoundsId = Shader.PropertyToID("_RfxClipBounds"), HeightTexId = Shader.PropertyToID("_RfxClipHeightTex");
        static readonly int GridParamsId = Shader.PropertyToID("_GridParams"), GridOriginId = Shader.PropertyToID("_GridOrigin");

        Mesh mesh; int meshLevels;
        MeshRenderer drawn; MeshFilter filter;
        Camera renderingCamera;
        static ReflexTerrainClipmap owner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            string e = Environment.GetEnvironmentVariable("REFLEX_TERRAIN_TESS");
            Force = e == "1" ? true : e == "0" ? false : (bool?)null;
            owner = null; LastLevels = LastVertices = LastTriangles = 0;
            Shader.SetGlobalVector(OriginId, Vector4.zero);
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            Clear();
            if (drawn) Destroy(drawn.gameObject);
            if (mesh) Destroy(mesh);
            mesh = null; drawn = null; filter = null;
        }

        void Clear()
        {
            if (owner != this) return;
            Shader.SetGlobalVector(OriginId, Vector4.zero);
            owner = null; renderingCamera = null;
        }

        void OnEnd(ScriptableRenderContext context, Camera c)
        {
            if (c == renderingCamera) Clear();
        }

        // The terrain this frame can be drawn for: the original terrain material with its own height map (it rebuilds the normal per
        // pixel: _GridParams.y), on a built Unity terrain.
        bool Usable(out Terrain unity, out Material mat)
        {
            unity = null; mat = null;
            if (!Wanted || !terrain || !terrain.Built) return false;
            unity = terrain.UnityTerrain; mat = unity ? unity.materialTemplate : null;
            return mat && unity.terrainData && unity.enabled && unity.gameObject.activeInHierarchy && terrain.Info.spacing > 0f
                && mat.HasProperty(GridOriginId) && mat.HasProperty(GridParamsId) && mat.GetVector(GridParamsId).y > 0.5f;
        }

        // The grid is a renderer of its own with the terrain's material (every pass the terrain has); its vertices are placed by the
        // globals OnBegin sets for the game camera, and collapse to nothing in any other camera's view (RfxClipVertex).
        void LateUpdate()
        {
            bool on = Usable(out var unity, out var mat);
            if (!on) { if (drawn && drawn.enabled) drawn.enabled = false; return; }
            int levels = LevelsFor(terrain.Info.spacing, out _);
            if (!mesh || meshLevels != levels) { if (mesh) Destroy(mesh); mesh = Build(levels); meshLevels = levels; if (filter) filter.sharedMesh = mesh; }
            if (!drawn)
            {
                var go = new GameObject("Terrain clipmap (TERRAIN TESSELLATION)", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false); go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                filter = go.GetComponent<MeshFilter>(); filter.sharedMesh = mesh;
                drawn = go.GetComponent<MeshRenderer>();
                drawn.shadowCastingMode = ShadowCastingMode.Off; drawn.receiveShadows = true; drawn.lightProbeUsage = LightProbeUsage.Off;
                drawn.reflectionProbeUsage = ReflectionProbeUsage.Off; drawn.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            }
            if (drawn.gameObject.layer != unity.gameObject.layer) drawn.gameObject.layer = unity.gameObject.layer;
            if (drawn.sharedMaterial != mat) drawn.sharedMaterial = mat;
            if (!drawn.enabled) drawn.enabled = true;
        }

        /// Levels for a height-map spacing: the finest cell is the spacing halved down to about the original's 0.25, the coarsest one sample.
        public static int LevelsFor(float spacing, out float finest)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(Mathf.Max(spacing, 1e-3f) / OriginalCell, 2f)), 0, 4);
            finest = spacing / (1 << k);
            return k + 1;
        }

        void OnBegin(ScriptableRenderContext context, Camera c)
        {
            var target = bump && bump.targetCamera ? bump.targetCamera : Camera.main;
            if (!OriginalWorld.ReflexWorldViews.Is(target, c)) return;   // (SPLIT SCREEN, w-split: the grid around the second rider's camera in its view)
            LastLevels = LastVertices = LastTriangles = 0;
            if (!drawn || !drawn.enabled || !mesh || !Usable(out var unity, out _)) return;
            var data = unity.terrainData; var info = terrain.Info; float spacing = info.spacing;
            int levels = LevelsFor(spacing, out float finest);
            if (levels != meshLevels) return;

            Vector3 origin = unity.transform.position; int res = data.heightmapResolution;
            Vector3 cam = c.transform.position;
            // the centre on the height-map lattice: every level's vertices then sit on fixed world points
            float cx = origin.x + Mathf.Round((cam.x - origin.x) / spacing) * spacing, cz = origin.z + Mathf.Round((cam.z - origin.z) / spacing) * spacing;
            float half = GridCells * 0.5f * spacing;            // the coarsest level's half size
            float margin = 8f * spacing;
            Shader.SetGlobalVector(OriginId, new Vector4(cx, cz, finest, 1f));
            Shader.SetGlobalVector(RectId, new Vector4(half, half, margin, SinkDepth));
            Shader.SetGlobalVector(GridId, new Vector4(origin.x, origin.z, 1f / spacing, res));
            Shader.SetGlobalVector(HeightId, new Vector4(origin.y, data.size.y * (65535f / 32766f), spacing / finest, 0f));
            Shader.SetGlobalVector(BoundsId, new Vector4(info.min_x, info.min_z, info.max_x, info.max_z));
            Shader.SetGlobalTexture(HeightTexId, data.heightmapTexture);
            owner = this; renderingCamera = c;
            LastLevels = levels; LastVertices = mesh.vertexCount; LastTriangles = (int)(mesh.GetIndexCount(0) / 3);
        }

        /// The nested grid in finest-cell units: level 0 a full 128 x 128 cells, each further level a ring of 128 x 128 cells twice as
        /// large around the last one. A level's odd rim vertices are moved onto their even neighbour, so its edge is the next level's
        /// (the original's ring pieces do the same with their end vertices). y = -(10000 + the level's cell) marks the mesh for the shader (ReflexTerrainVertexWS).
        static Mesh Build(int levels)
        {
            const int N = GridCells, V = N + 1, H = N / 2, Q = N / 4;
            var pos = new List<Vector3>(V * V * levels); var uv = new List<Vector2>(V * V * levels); var nrm = new List<Vector3>(V * V * levels);
            var tri = new List<int>(N * N * 6 * levels);
            for (int L = 0; L < levels; L++)
            {
                int s = 1 << L, b = pos.Count; bool stitch = L < levels - 1;
                for (int j = 0; j <= N; j++)
                    for (int i = 0; i <= N; i++)
                    {
                        int x = i, z = j;
                        if (stitch)
                        {
                            if ((j == 0 || j == N) && (i & 1) == 1) x = i + 1;
                            if ((i == 0 || i == N) && (j & 1) == 1) z = j + 1;
                        }
                        pos.Add(new Vector3((x - H) * s, -(10000f + s), (z - H) * s)); uv.Add(new Vector2(s, 0f)); nrm.Add(Vector3.up);
                    }
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        if (L > 0 && i >= Q && i < N - Q && j >= Q && j < N - Q) continue;   // the finer levels' square
                        int a = b + j * V + i, r = a + 1, u = a + V, d = u + 1;
                        // (clockwise from above: Unity's front face)
                        if (((i ^ j) & 1) == 0) { tri.Add(a); tri.Add(u); tri.Add(d); tri.Add(a); tri.Add(d); tri.Add(r); }
                        else { tri.Add(a); tri.Add(u); tri.Add(r); tri.Add(r); tri.Add(u); tri.Add(d); }
                    }
            }
            var m = new Mesh { name = "Terrain clipmap (" + levels + " levels)", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            m.SetVertices(pos); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(tri, 0, false);
            m.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
            m.UploadMeshData(true);
            return m;
        }
    }
}
