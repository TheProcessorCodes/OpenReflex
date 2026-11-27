using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Reflex.Port.Content
{
    // The animated material passes of an imported MX vs ATV Unleashed level (scenery-fx.json beside its scenery, written by
    // tools/unleashed/scenery.py from the source .dxm animators), run as the original runs them:
    //  - UVAnimation (waterfalls, rivers, the ocean, the northern lights): angle += dt x rate; the texture matrix
    //    u' = u R1.x + v R2.x + offU, v' = u R1.y + v R2.y + offV with R1 = (c uScale - s vShear, c uShear - s vScale),
    //    R2 = (c vShear + s uScale, c vScale + s uShear), off = fmod(off + dt x velocity, max) from (uCenter, vCenter);
    //  - TextureFrame (the stands' crowd, fountains): f += dt x fps over the .atx frames; past the end it reverses (ping-pong),
    //    loops to a frame or wraps; not looping = it stops on the last frame.
    // Stage 0 is the material's base pass, 1 its first layer. The clock stops with the game (Time.deltaTime).
    public sealed class UnleashedSceneryFx : MonoBehaviour
    {
        [Serializable] sealed class Uv { public float angle, rate, uCenter, vCenter, uScale = 1, vScale = 1, uShear, vShear, uMax = 1, vMax = 1, uVel, vVel; }
        [Serializable] sealed class Item { public int material, stage; public uint[] frames; public float fps = 1, initial; public bool looping = true, reverse; public int loopTo = -1; public Uv uv; }
        [Serializable] sealed class Data { public int version; public Item[] materials; }

        sealed class UvRun { public List<Material> mats; public int stage; public Uv uv; public float angle, offU, offV; }
        sealed class FrameRun { public List<Material> mats; public int stage; public Texture2D[] frames; public float f, fps; public bool looping, reverse, reversing, stopped; public int loopTo, shown = -1; }
        readonly List<UvRun> uvs = new List<UvRun>();
        readonly List<FrameRun> frames = new List<FrameRun>();
        public static int LastCount { get; private set; }

        static Data Read()
        {
            if (UnleashedLevels.Bound == null) return null;
            string path = Path.Combine(ExperimentalTerrain.Path, "scenery-fx.json");
            if (!RxFiles.Exists(path)) return null;
            var d = JsonUtility.FromJson<Data>(System.Text.Encoding.UTF8.GetString(RxFiles.ReadAllBytes(path)));
            if (d == null || d.version != 1 || d.materials == null || d.materials.Length > 4096) throw new InvalidDataException("Invalid Unleashed scenery effects");
            foreach (var m in d.materials)
                if (m == null || m.material < 0 || m.stage < 0 || m.stage > 1 || (m.frames != null && m.frames.Length > 256) || !float.IsFinite(m.fps) || !float.IsFinite(m.initial))
                    throw new InvalidDataException("Invalid Unleashed scenery effect");
            return d;
        }

        /// Every later frame of an animated texture -> its first frame (the frames take the first one's alpha test, mips and wrap).
        internal static Dictionary<uint, uint> FrameSources()
        {
            var map = new Dictionary<uint, uint>(); var d = Read(); if (d == null) return map;
            foreach (var m in d.materials) if (m.frames != null && m.frames.Length > 1) for (int i = 1; i < m.frames.Length; i++) map[m.frames[i]] = m.frames[0];
            return map;
        }

        static bool Moves(Uv u) => u != null && (u.uVel != 0 || u.vVel != 0 || u.rate != 0 || u.angle != 0 || u.uScale != 1 || u.vScale != 1 || u.uShear != 0 || u.vShear != 0 || u.uCenter != 0 || u.vCenter != 0);

        internal static void Attach(Transform root, Dictionary<int, List<Material>> materials, Dictionary<uint, Texture2D> textures, Func<Texture2D, bool, bool, Texture2D> clamped, List<UnityEngine.Object> owned)
        {
            LastCount = 0;
            var d = Read(); if (d == null || d.materials.Length == 0) return;
            UnleashedSceneryFx fx = null;
            foreach (var m in d.materials)
            {
                if (!materials.TryGetValue(m.material, out var mats) || mats.Count == 0) continue;
                if (m.frames != null && m.frames.Length > 1)
                {
                    var list = new List<Texture2D>(); string slot = m.stage == 0 ? "_MainTex" : "_L1Tex"; var shown = mats[0].GetTexture(slot);
                    foreach (uint id in m.frames)
                    {
                        if (!textures.TryGetValue(id, out var t) || !t) { list = null; break; }
                        // the frame at the address modes the pass draws with
                        if (shown) t = clamped(t, shown.wrapModeU == TextureWrapMode.Clamp, shown.wrapModeV == TextureWrapMode.Clamp);
                        list.Add(t);
                    }
                    if (list == null) continue;
                    if (!fx) fx = root.gameObject.AddComponent<UnleashedSceneryFx>();
                    fx.frames.Add(new FrameRun { mats = mats, stage = m.stage, frames = list.ToArray(), f = Mathf.Max(0, m.initial), fps = m.fps, looping = m.looping, reverse = m.reverse, loopTo = m.loopTo });
                }
                else if (Moves(m.uv))
                {
                    if (!fx) fx = root.gameObject.AddComponent<UnleashedSceneryFx>();
                    var u = m.uv; if (u.uMax == 0) u.uMax = 1; if (u.vMax == 0) u.vMax = 1;
                    fx.uvs.Add(new UvRun { mats = mats, stage = m.stage, uv = u, angle = u.angle, offU = u.uCenter, offV = u.vCenter });
                }
            }
            if (!fx) return;
            fx.Step(0); LastCount = fx.uvs.Count + fx.frames.Count;
            Debug.Log($"[Unleashed] animated materials: {fx.uvs.Count} UV, {fx.frames.Count} texture-frame passes");
        }

        static readonly int[] RowU = { Shader.PropertyToID("_BaseUvU"), Shader.PropertyToID("_L1UvU") }, RowV = { Shader.PropertyToID("_BaseUvV"), Shader.PropertyToID("_L1UvV") };
        static readonly int[] Slot = { Shader.PropertyToID("_MainTex"), Shader.PropertyToID("_L1Tex") };

        void Update() => Step(Time.deltaTime);

        void Step(float dt)
        {
            foreach (var u in uvs)
            {
                var a = u.uv;
                if (a.rate != 0) { u.angle += dt * a.rate; while (u.angle > Mathf.PI) u.angle -= 2 * Mathf.PI; while (u.angle < -Mathf.PI) u.angle += 2 * Mathf.PI; }
                float c = Mathf.Cos(u.angle), s = Mathf.Sin(u.angle);
                u.offU = (u.offU + dt * a.uVel) % a.uMax; u.offV = (u.offV + dt * a.vVel) % a.vMax;   // (C#'s % keeps the sign, as fmod)
                var ru = new Vector4(c * a.uScale - s * a.vShear, c * a.vShear + s * a.uScale, u.offU, 0);
                var rv = new Vector4(c * a.uShear - s * a.vScale, c * a.vScale + s * a.uShear, u.offV, 0);
                foreach (var m in u.mats) if (m) { m.SetVector(RowU[u.stage], ru); m.SetVector(RowV[u.stage], rv); }
            }
            foreach (var f in frames)
            {
                int n = f.frames.Length;
                if (!f.stopped)
                {
                    f.f += (f.reversing ? -1 : 1) * dt * f.fps;
                    if (f.f >= n)
                    {
                        float r = f.f % n;
                        if (f.reverse) { f.f = Mathf.Max(0, n - 1 - r); f.reversing = true; }
                        else if (f.loopTo >= 0 && f.loopTo < n) f.f = r % (n - f.loopTo) + f.loopTo;
                        else f.f = r;
                        if (!f.looping) f.stopped = true;
                    }
                    else if (f.f < 0)
                    {
                        if (f.reverse) { f.f = Mathf.Abs(f.f % n); f.reversing = false; }
                        else if (f.reversing) f.stopped = true;
                    }
                    if (f.stopped) f.f = f.reversing ? 0 : n - 1;
                }
                int k = Mathf.Clamp((int)f.f, 0, n - 1);
                if (k == f.shown) continue;
                f.shown = k;
                foreach (var m in f.mats) if (m) m.SetTexture(Slot[f.stage], f.frames[k]);
            }
        }
    }
}
