// WEATHER, SNOW: the snow trail map (LIGHTING: ENHANCED only; not part of the original game). A world-locked square around the camera
// (Span ft, Size texels; half-float r = trench, g = rim: Shaders/Resources/ReflexSnowTrail.shader) that remembers where tyres went through
// the snow, for the terrain's snow shading (Shaders/ReflexWeather.hlsl: trench walls, packed floor, raised rims).
//   What goes in: the core's own tyre-track ribbons (rx_session_tracks: every wheel of every vehicle on the ground - bikes, ATVs, UTVs,
//     trucks; the player, the computer riders, online riders), so nothing here knows about vehicles. Each fetch (at most every 4th frame)
//     the ribbons are compared with the last fetch, strip by strip (a strip only changes where its ring is being written), and the new
//     triangles are stamped as capsules: the ribbon's centre line and half width, the depth from the ribbon's own intensity (the game's
//     measure of how hard the tyre marks the ground: its load on soft soil).
//   What it does over time: while snow falls the trenches fill and the rims settle (Fill, a few times a second; slowly by drift without
//     snowfall); the map survives the core's ribbon rings (a track stays until the snow has filled it, not until its ring chunk is reused).
//   The square follows the camera in steps (Scroll: the old content is carried over; the ground that enters is stamped from the ribbons
//     the core still holds). Outside the square the terrain falls back to the tyre-track map's coarse trail.
// Physics: none (the grip loss is ReflexWeather.GripFactor, unchanged). Cost: nothing unless snow lies; then one small mesh draw per ribbon
// fetch, a full-target blend 4 times a second, and a copy when the square moves.
using System;
using System.Collections.Generic;
using Reflex.Port.RxCore;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reflex.Port.Rendering
{
    public static class ReflexSnowTrails
    {
        /// The square's side (ft) and resolution by ENHANCED QUALITY (LOW / MEDIUM: 1024 texels over 144 ft; HIGH: 2048 over 224 ft).
        public static float Span { get; private set; } = 224f;
        public static int Size { get; private set; } = 2048;
        /// Tests: off (the terrain's coarse trail only).
        public static bool Disabled;
        /// Stamps drawn since the map was made, and by the last fetch (tests, the perf overlay).
        public static int Stamped { get; private set; }
        public static int LastStamped { get; private set; }

        static readonly int MapId = Shader.PropertyToID("_RfxSnowMap"), RectId = Shader.PropertyToID("_RfxSnowMapRect"), TexelId = Shader.PropertyToID("_RfxSnowMapTexel"),
            SnowRectId = Shader.PropertyToID("_SnowRect"), FillId = Shader.PropertyToID("_SnowFill"), PrevId = Shader.PropertyToID("_SnowPrev"), ShiftId = Shader.PropertyToID("_SnowShift");

        static RenderTexture map, spare;
        static Material material;
        static CommandBuffer cmd;
        static Mesh mesh;
        static readonly List<Vector3> verts = new List<Vector3>(1024);
        static readonly List<Vector4> uvs = new List<Vector4>(1024);
        static readonly List<int> indices = new List<int>(1536);
        static Vector2 origin; static bool placed, wanted;
        static float fillClock, fillTrench, fillRim;
        // the last fetch (a copy: the simulation reuses its array) and its strips by (set, slot)
        static float[] last; static int lastTriangles; static bool haveLast;
        const int Strips = 3 * 64;
        static readonly int[] lastFirst = new int[Strips], lastCount = new int[Strips];
        static readonly int[] nowFirst = new int[Strips], nowCount = new int[Strips];
        const int MaxStamps = 6000;

        /// Snow lies and the map is live (the terrain reads it).
        public static bool Active => wanted && map;

        /// The simulation's ribbon fetch (OpenReflexResultsAdapter, when the game flushed new track vertices): tris is 18 floats per triangle
        /// (x, z, side, V, intensity, type per vertex), ribbons the strips' places in it. No GPU work here (any thread): the new pieces wait
        /// in the stamp lists for the next Frame.
        public static void Feed(float[] tris, int triangles, RxTrackRibbon[] ribbons, int ribbonCount)
        {
            lock (gate) FeedLocked(tris, triangles, ribbons, ribbonCount);
        }
        static readonly object gate = new object();

        static void FeedLocked(float[] tris, int triangles, RxTrackRibbon[] ribbons, int ribbonCount)
        {
            if (tris == null || ribbons == null) return;
            if (!wanted || Disabled) { haveLast = false; return; }   // (no snow on the ground: nothing is kept, nothing compared)
            triangles = Mathf.Clamp(triangles, 0, tris.Length / 18);
            Array.Clear(nowFirst, 0, Strips); Array.Clear(nowCount, 0, Strips);
            for (int i = 0; i < ribbonCount && i < ribbons.Length; i++)
            {
                var r = ribbons[i];
                int k = r.set * 64 + r.slot;
                if (k < 0 || k >= Strips) continue;
                int first = r.first_vertex / 3, count = Mathf.Min(r.triangles, triangles - first);
                if (first < 0 || count <= 0) continue;
                nowFirst[k] = first; nowCount[k] = count;
            }
            // (the first fetch after the snow came only sets the reference: the tracks laid before it are under the snow)
            if (placed && haveLast)
            {
                int before = verts.Count;
                for (int k = 0; k < Strips; k++)
                {
                    int nf = nowFirst[k], nt = nowCount[k];
                    if (nt == 0) continue;
                    int pf = lastFirst[k], pt = Mathf.Min(lastCount[k], lastTriangles - pf);
                    int both = Mathf.Min(nt, Mathf.Max(pt, 0)), head = 0, tail = 0;
                    while (head < both && Same(tris, nf + head, last, pf + head)) head++;
                    while (tail < both - head && Same(tris, nf + nt - 1 - tail, last, pf + pt - 1 - tail)) tail++;
                    for (int t = nf + head; t < nf + nt - tail; t++) Add(tris, t, false);
                }
                LastStamped = (verts.Count - before) / 4;
            }
            // keep the fetch for the next comparison (and for the ground the square moves onto)
            int n = triangles * 18;
            if (last == null || last.Length < n) last = new float[Mathf.NextPowerOfTwo(Mathf.Max(n, 18 * 1024))];
            Buffer.BlockCopy(tris, 0, last, 0, n * sizeof(float));
            lastTriangles = triangles; haveLast = true;
            Array.Copy(nowFirst, lastFirst, Strips); Array.Copy(nowCount, lastCount, Strips);
        }

        static bool Same(float[] a, int ta, float[] b, int tb)
        {
            int i = ta * 18, j = tb * 18;
            return a[i] == b[j] && a[i + 1] == b[j + 1] && a[i + 6] == b[j + 6] && a[i + 7] == b[j + 7] && a[i + 12] == b[j + 12] && a[i + 13] == b[j + 13];
        }

        // One ribbon triangle as a capsule: two of its vertices lie on one edge of the ribbon, the third on the other; the centre line runs
        // half way between. entering: only where the square just moved onto (outside its old place).
        static Vector2 oldOrigin; static bool hadOld;
        static void Add(float[] d, int t, bool entering)
        {
            if (verts.Count >= MaxStamps * 4) return;
            int i = t * 18;
            float s0 = d[i + 2], s1 = d[i + 8], s2 = d[i + 14];
            int lone = s0 != s1 && s0 != s2 ? 0 : s1 != s0 && s1 != s2 ? 1 : s2 != s0 && s2 != s1 ? 2 : -1;
            if (lone < 0) return;
            int ia = i + 6 * ((lone + 1) % 3), ib = i + 6 * ((lone + 2) % 3), ic = i + 6 * lone;
            Vector2 A = new Vector2(d[ia], d[ia + 1]), B = new Vector2(d[ib], d[ib + 1]), C = new Vector2(d[ic], d[ic + 1]);
            // the lone vertex's partner across the ribbon: the one of the pair at its own place along the track (V)
            bool mateA = Mathf.Abs(d[ia + 3] - d[ic + 3]) <= Mathf.Abs(d[ib + 3] - d[ic + 3]);
            Vector2 across = (C - (mateA ? A : B)) * 0.5f;
            Vector2 centre = across;
            float hw = across.magnitude;
            if (!(hw > 0.02f && hw < 4f)) return;
            // the ribbon is the mark's soft-edged texture (about 2 ft across for a bike), far wider than the tyre that presses the snow:
            // the trench is the tyre's own width (a bike's about 0.4 ft, an ATV's / UTV's wider ribbons up to 0.75 ft)
            across *= Mathf.Clamp(hw * 0.22f, 0.2f, 0.38f) / hw;
            hw = across.magnitude;
            Vector2 a = A + centre, b = B + centre;
            float len = (b - a).magnitude;
            if (!(len < 30f)) return;
            float reach = hw * 2.6f + 0.2f;
            Vector2 mid = (a + b) * 0.5f;
            float lo = reach + len * 0.5f;
            if (mid.x < origin.x - lo || mid.y < origin.y - lo || mid.x > origin.x + Span + lo || mid.y > origin.y + Span + lo) return;
            if (entering && hadOld && mid.x > oldOrigin.x + lo && mid.y > oldOrigin.y + lo && mid.x < oldOrigin.x + Span - lo && mid.y < oldOrigin.y + Span - lo) return;
            float intensity = Mathf.Max(d[i + 4], Mathf.Max(d[i + 10], d[i + 16]));
            // soft snow gives way under any tyre; a harder mark (the tyre's load, sliding, a landing) packs it to the ground
            float strength = Mathf.Clamp01(0.5f + 0.6f * intensity);
            Vector2 dir = len > 1e-4f ? (b - a) / len : new Vector2(1, 0), nrm = new Vector2(-dir.y, dir.x);
            float R = hw * 2.6f, L = len / hw;
            int v0 = verts.Count;
            Vector2 p0 = a - dir * R - nrm * R, p1 = a - dir * R + nrm * R, p2 = b + dir * R + nrm * R, p3 = b + dir * R - nrm * R;
            verts.Add(new Vector3(p0.x, p0.y, 0)); uvs.Add(new Vector4(-2.6f, -2.6f, L, strength));
            verts.Add(new Vector3(p1.x, p1.y, 0)); uvs.Add(new Vector4(-2.6f, 2.6f, L, strength));
            verts.Add(new Vector3(p2.x, p2.y, 0)); uvs.Add(new Vector4(L + 2.6f, 2.6f, L, strength));
            verts.Add(new Vector3(p3.x, p3.y, 0)); uvs.Add(new Vector4(L + 2.6f, -2.6f, L, strength));
            indices.Add(v0); indices.Add(v0 + 1); indices.Add(v0 + 2); indices.Add(v0); indices.Add(v0 + 2); indices.Add(v0 + 3);
        }

        static void End()
        {
            if (verts.Count == 0) return;
            if (!map || !material) { verts.Clear(); uvs.Clear(); indices.Clear(); return; }
            if (!mesh) { mesh = new Mesh { name = "Reflex snow trail stamps", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic(); }
            mesh.Clear();
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(indices, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
            Stamped += verts.Count / 4;
            cmd.Clear();
            cmd.SetRenderTarget(map);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            material.SetVector(SnowRectId, new Vector4(origin.x, origin.y, 1f / Span, 0));
            cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            verts.Clear(); uvs.Clear(); indices.Clear();
        }

        /// Every frame the weather is drawn (ReflexWeather): cover = the snow on the ground 0..1, snowfall 0..1, the camera, the quality tier.
        internal static void Frame(Camera cam, float cover, float snowfall, float dt, int tier)
        {
            lock (gate) FrameLocked(cam, cover, snowfall, dt, tier);
        }

        static void FrameLocked(Camera cam, float cover, float snowfall, float dt, int tier)
        {
            bool want = cover > 0.02f && cam && !Disabled;
            if (!want) { if (wanted || map) ReleaseLocked(); return; }
            int size = tier >= 2 ? 2048 : 1024; float span = tier >= 2 ? 224f : 144f;
            if (map && (map.width != size || !map.IsCreated())) ReleaseLocked();
            if (!map)
            {
                var sh = Resources.Load<Shader>("ReflexSnowTrail");
                var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf) ? RenderTextureFormat.RGHalf : RenderTextureFormat.ARGBHalf;
                if (!sh || !sh.isSupported || !SystemInfo.SupportsRenderTextureFormat(fmt)) { Disabled = true; Debug.LogWarning("[Reflex weather] snow trails: Resources/ReflexSnowTrail or half-float targets not supported; the coarse trail only"); return; }
                Size = size; Span = span;
                material = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                map = Make(fmt, "Reflex snow trails"); spare = Make(fmt, "Reflex snow trails (scroll)");
                cmd = new CommandBuffer { name = "Reflex snow trails" };
                placed = false; hadOld = false; Stamped = 0; fillClock = 0; fillTrench = fillRim = 0;
                Debug.Log($"[Reflex weather] snow trails: {Size} x {Size} over {Span:0} ft ({Span / Size * 12f:0.0} in a texel), {fmt}");
            }
            wanted = true;
            // the square: centred on the camera, moved in steps of a 16th of its side once the camera is an 8th off centre (whole texels)
            var p = cam.transform.position;
            float step = Span / 16f;
            Vector2 centre = origin + new Vector2(Span, Span) * 0.5f;
            if (!placed || Mathf.Abs(p.x - centre.x) > Span / 8f || Mathf.Abs(p.z - centre.y) > Span / 8f)
            {
                var o = new Vector2(Mathf.Round((p.x - Span * 0.5f) / step) * step, Mathf.Round((p.z - Span * 0.5f) / step) * step);
                bool far = !placed || Mathf.Abs(o.x - origin.x) >= Span || Mathf.Abs(o.y - origin.y) >= Span;
                cmd.Clear();
                if (far) { cmd.SetRenderTarget(map); cmd.ClearRenderTarget(false, true, Color.clear); hadOld = false; }
                else
                {
                    material.SetVector(ShiftId, new Vector4((o.x - origin.x) / Span, (o.y - origin.y) / Span, 0, 0));
                    cmd.SetGlobalTexture(PrevId, map);
                    cmd.SetRenderTarget(spare);
                    cmd.DrawProcedural(Matrix4x4.identity, material, 2, MeshTopology.Triangles, 3, 1);
                    hadOld = true; oldOrigin = origin;
                }
                Graphics.ExecuteCommandBuffer(cmd);
                if (!far) { var t = map; map = spare; spare = t; }
                origin = o; placed = true;
                // the ground that entered: the ribbons the core still holds there
                if (haveLast && lastTriangles > 0)
                    for (int t = 0; t < lastTriangles; t++) Add(last, t, true);
            }
            End();   // the new pieces since the last frame (and the ground that entered)
            // snowfall fills the trenches (HEAVY: about two minutes for a full one; a slow drift without snowfall) and settles the rims faster
            fillTrench += dt * (0.0012f + 0.0075f * snowfall);
            fillRim += dt * (0.003f + 0.014f * snowfall);
            fillClock += dt;
            if (fillClock >= 0.25f)
            {
                fillClock = 0;
                material.SetVector(FillId, new Vector4(fillTrench, fillRim, 0, 0)); fillTrench = fillRim = 0;
                cmd.Clear();
                cmd.SetRenderTarget(map);
                cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            Shader.SetGlobalTexture(MapId, map);
            Shader.SetGlobalVector(RectId, new Vector4(origin.x, origin.y, 1f / Span, 1f));
            Shader.SetGlobalVector(TexelId, new Vector4(1f / Size, Span / Size, 0, 0));
        }

        static RenderTexture Make(RenderTextureFormat fmt, string name)
        {
            var t = new RenderTexture(Size, Size, 0, fmt, RenderTextureReadWrite.Linear) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, hideFlags = HideFlags.HideAndDontSave };
            t.Create();
            var was = RenderTexture.active; RenderTexture.active = t; GL.Clear(false, true, Color.clear); RenderTexture.active = was;
            return t;
        }

        /// No snow on the ground (or the weather off): the map is dropped, the terrain reads none.
        internal static void Release()
        {
            lock (gate) ReleaseLocked();
        }

        static void ReleaseLocked()
        {
            verts.Clear(); uvs.Clear(); indices.Clear();
            wanted = false; placed = false; hadOld = false; haveLast = false;
            Shader.SetGlobalVector(RectId, Vector4.zero);
            if (map) { map.Release(); UnityEngine.Object.Destroy(map); map = null; }
            if (spare) { spare.Release(); UnityEngine.Object.Destroy(spare); spare = null; }
            if (material) { UnityEngine.Object.Destroy(material); material = null; }
            if (mesh) { UnityEngine.Object.Destroy(mesh); mesh = null; }
            cmd?.Release(); cmd = null;
        }

        /// A new level / ride: the last fetch is another world's.
        internal static void Forget() { lock (gate) { haveLast = false; lastTriangles = 0; Array.Clear(lastFirst, 0, Strips); Array.Clear(lastCount, 0, Strips); if (map) ReleaseLocked(); } }
    }
}
