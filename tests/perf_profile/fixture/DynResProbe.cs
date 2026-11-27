// TEST ONLY (tests/perf_profile; Rendering/ReflexDynamicResolution.cs). Off unless REFLEX_DYNRES_PROBE=1. In the bench's live window
// (run ... --batch --modes enhanced --tiers 2 --no-ablations --no-breakdown --window 1 --live 150 --env REFLEX_DYNRES_PROBE=1
//  --env REFLEX_AA=taa --env REFLEX_DYNRES_LOG=1) the game camera renders into a target of its own with the TAA as on screen, then:
//   A  forced scales, the world paused (the same frame): dyn_<scene>_s<percent>.png after 48 frames each, and the frame time at
//      each scale at the load resolution (every frame waits for the GPU, as a present does): "[dynres-probe] SCALE ..."
//   B  frames across a change, paused: 100 -> 75, 75 -> 100, 100 -> 58: the frame before and the 3 after saved, the mean luminance and
//      the mean difference to the frame before for 10 frames: "[dynres-probe] CHANGE ..." (a flash is a jump of either)
//   C  the same across 100 -> 75 with the world running (smear on moving riders): dyn_<scene>_live_*.png
//   D  the controller: the world running at the load resolution (REFLEX_DYNRES_PROBE_LOAD=WxH, default 3840x2160) against
//      REFLEX_DYNRES_PROBE_FPS (default 60) for 35 s, then at 1280x720 (the load gone) for 30 s: a "[dynres-probe] TRACE" line a second.
// A -batchmode player does not run the camera loop and presents nothing: the probe renders the camera once a frame itself, and gives
// the controller each frame's waited-for render time as the GPU time (ReflexDynamicResolution.TestGpuMs).
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using Reflex.Port.Options;
using Reflex.Port.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class DynResProbe : MonoBehaviour
{
    const string Tag = "[dynres-probe] ";
    static string Inv(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);
    string dir, prefix = "scene"; Camera cam; RenderTexture target;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("REFLEX_DYNRES_PROBE") != "1" || FindAnyObjectByType<DynResProbe>()) return;
        var g = new GameObject("dynres probe (test)"); DontDestroyOnLoad(g); g.AddComponent<DynResProbe>();
    }

    IEnumerator Start()
    {
        dir = Environment.GetEnvironmentVariable("REFLEX_PREVIEW_SHOTS") ?? Application.temporaryCachePath;
        prefix = Environment.GetEnvironmentVariable("REFLEX_DYNRES_PROBE_NAME") ?? "scene";
        // the bench's live window (its "LIVE BEGIN" line: before it the bench poses the camera, pauses the world and renders by itself)
        bool live = false;
        void OnLog(string msg, string stack, LogType type) { if (msg.Contains("[perf-bench] LIVE BEGIN")) live = true; }
        Application.logMessageReceived += OnLog;
        while (!live) yield return null;
        Application.logMessageReceived -= OnLog;
        for (int i = 0; i < 30; i++) yield return null;
        var rt = ReflexClientLive.Runtime;
        if (!rt) { Debug.Log(Tag + "no runtime"); yield break; }
        ReflexTemporalAA.OffscreenTest = true;
        cam = rt.chaseCamera ? rt.chaseCamera : Camera.main;
        if (!cam) { Debug.Log(Tag + "no camera"); yield break; }
        var taa = cam.GetComponent<ReflexTemporalAA>();
        var pipe = cam.GetComponent<ReflexRenderPipeline>();
        Vector2Int load = Res("REFLEX_DYNRES_PROBE_LOAD", 3840, 2160);
        int fps = int.TryParse(Environment.GetEnvironmentVariable("REFLEX_DYNRES_PROBE_FPS"), out int f0) ? f0 : 60;
        ReflexDynamicResolution.EnabledOverride = 1;
        Debug.Log(Tag + Inv($"BEGIN camera {cam.name}, load {load.x}x{load.y}, target {fps} fps, minimum {ReflexDynamicResolution.Minimum} %"));

        // ---- A: forced scales, paused
        rt.paused = true;
        SetTarget(1920, 1080);
        int[] scales = { 100, 87, 75, 67, 58, 50 };
        foreach (int s in scales)
        {
            ReflexDynamicResolution.ForceScale = s / 100f;
            for (int i = 0; i < 48; i++) { yield return null; cam.Render(); }
            Save(Read(), $"dyn_{prefix}_s{s}.png");
            Debug.Log(Tag + Inv($"SHOT {s} %: {(taa ? taa.report : "no TAA component")} [active {(taa ? taa.active : false)}, AA {ReflexAntiAliasing.Current}, enhanced {(pipe ? pipe.enhanced != null : false)}, post {(pipe ? pipe.postProcess : false)}, drawn at {ReflexDynamicResolution.CurrentScale * 100f:0} %]"));
        }
        SetTarget(load.x, load.y);
        foreach (int s in scales)
        {
            ReflexDynamicResolution.ForceScale = s / 100f;
            for (int i = 0; i < 12; i++) { yield return null; cam.Render(); Sync(); }
            var sw = System.Diagnostics.Stopwatch.StartNew(); const int N = 40; double gpu = 0; int gn = 0; var ft = new FrameTiming[1];
            for (int i = 0; i < N; i++)
            {
                yield return null; cam.Render(); Sync();
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].gpuFrameTime > 0) { gpu += ft[0].gpuFrameTime; gn++; }
            }
            Debug.Log(Tag + Inv($"SCALE {s} % at {load.x}x{load.y}: {sw.Elapsed.TotalMilliseconds / N:0.00} ms per frame, FrameTimingManager GPU {(gn > 0 ? (gpu / gn).ToString("0.00", CultureInfo.InvariantCulture) : "none")} ms ({gn}/{N} frames timed)"));
        }

        // ---- B: frames across a change, paused
        SetTarget(1920, 1080);
        yield return Across(100, 75, "paused", true);
        yield return Across(75, 100, "paused", true);
        yield return Across(100, 58, "paused", true);
        // ---- C: the same with the world running
        rt.paused = false;
        yield return Across(100, 75, "live", true);
        yield return Across(75, 100, "live", false);

        // ---- D: the controller under a real load
        ReflexDynamicResolution.ForceScale = 0f; ReflexDynamicResolution.TargetOverride = fps;
        SetTarget(load.x, load.y);
        yield return Trace("LOAD", 35f);
        SetTarget(1280, 720);
        yield return Trace("NO LOAD", 30f);
        Debug.Log(Tag + Inv($"DONE changes {ReflexDynamicResolution.Changes}"));
        ReflexDynamicResolution.TestGpuMs = -1f;
        cam.targetTexture = null;
    }

    static Vector2Int Res(string env, int w, int h)
    {
        var p = (Environment.GetEnvironmentVariable(env) ?? "").Split('x');
        return p.Length == 2 && int.TryParse(p[0], out int a) && int.TryParse(p[1], out int b) ? new Vector2Int(a, b) : new Vector2Int(w, h);
    }

    void SetTarget(int w, int h)
    {
        var old = target;
        target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "dynres probe target" };
        cam.targetTexture = target;
        if (old) Destroy(old);
    }

    // every frame waits for the GPU (what a present does on screen), so the frame time is the GPU's when it is the limit
    // (a synchronous one-pixel read: AsyncGPUReadback's WaitForCompletion came back on a 16.7 ms tick here)
    Texture2D one;
    void Sync()
    {
        if (!one) one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        var prev = RenderTexture.active; RenderTexture.active = target;
        one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
        RenderTexture.active = prev;
    }

    byte[] Read()
    {
        var req = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32); req.WaitForCompletion();
        return req.hasError ? null : req.GetData<byte>().ToArray();
    }

    void Save(byte[] px, string name)
    {
        if (px == null) return;
        var t = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        t.SetPixelData(px, 0); t.Apply(false);
        File.WriteAllBytes(Path.Combine(dir, name), t.EncodeToPNG()); Destroy(t);
    }

    IEnumerator Across(int from, int to, string label, bool save)
    {
        ReflexDynamicResolution.ForceScale = from / 100f;
        for (int i = 0; i < 40; i++) { yield return null; cam.Render(); }
        byte[] before = Read(), prev = before;
        if (save) Save(before, $"dyn_{prefix}_{label}_{from}to{to}_f0.png");
        ReflexDynamicResolution.ForceScale = to / 100f;
        var line = new System.Text.StringBuilder();
        line.Append(Inv($"lum {Lum(before):0.000}"));
        for (int k = 1; k <= 10; k++)
        {
            yield return null; cam.Render();
            var px = Read();
            if (save && k <= 3) Save(px, $"dyn_{prefix}_{label}_{from}to{to}_f{k}.png");
            line.Append(Inv($" | f{k} lum {Lum(px):0.000} d0 {Diff(px, before):0.000} d1 {Diff(px, prev):0.000}"));
            prev = px;
        }
        Debug.Log(Tag + $"CHANGE {label} {from} -> {to}: " + line);
    }

    static double Lum(byte[] p) { if (p == null) return 0; long s = 0; for (int i = 0; i < p.Length; i += 4) s += p[i] * 2 + p[i + 1] * 5 + p[i + 2]; return s / 8.0 / (p.Length / 4); }
    static double Diff(byte[] a, byte[] b) { if (a == null || b == null || a.Length != b.Length) return -1; long s = 0; for (int i = 0; i < a.Length; i += 4) s += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]); return s / 3.0 / (a.Length / 4); }

    IEnumerator Trace(string label, float seconds)
    {
        float t0 = Time.realtimeSinceStartup, next = t0 + 1f; int n = 0; float worst = 0, sum = 0; float min = 2, max = 0;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            yield return null;
            var sw = System.Diagnostics.Stopwatch.StartNew(); cam.Render(); Sync();
            ReflexDynamicResolution.TestGpuMs = (float)sw.Elapsed.TotalMilliseconds;   // the frame's render, waited for: the GPU's time when it is the limit
            float dt = Time.unscaledDeltaTime * 1000f; n++; sum += dt; if (dt > worst) worst = dt;
            float s = ReflexDynamicResolution.CurrentScale; if (s < min) min = s; if (s > max) max = s;
            if (Time.realtimeSinceStartup >= next)
            {
                Debug.Log(Tag + Inv($"TRACE {label} t {Time.realtimeSinceStartup - t0:0} s: scale {ReflexDynamicResolution.CurrentScale * 100f:0} % ({min * 100f:0}-{max * 100f:0}) {ReflexDynamicResolution.Status}, frame {sum / n:0.00} ms ({1000f * n / sum:0} fps), worst {worst:0.0} ms, changes {ReflexDynamicResolution.Changes}"));
                next += 1f; n = 0; sum = 0; worst = 0; min = 2; max = 0;
            }
        }
    }
}
