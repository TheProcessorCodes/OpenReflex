// DYNAMIC RESOLUTION (OPTIONS > VIDEO; not part of the original game): the 3D picture's render resolution follows the measured frame
// time so that a machine whose graphics card is the limit holds its frame rate without the player touching an option.
//   what scales  the world's render size under ANTI-ALIASING: TAA, exactly as UPSCALING does it (ReflexTemporalAA: the URP asset's render
//                scale while the game camera renders; the TAA resolve rebuilds the picture at the full size, so the post chain, the HUD
//                and every menu stay at the output resolution). Needs TAA: with MSAA / OFF the row says NEEDS TAA and nothing scales.
//   the scales   a fixed ladder (about 7 % per step in each direction, 13 % fewer pixels): 100, 93, 87, 81, 75, 70, 65, 61, 58, 54, 50 %
//                of the output, from UPSCALING's share (100 / 67 / 58 %) down to DYNAMIC MINIMUM. A fixed set means URP's render graph
//                pool only ever sees these sizes (a change takes targets of the new size from the pool; nothing is allocated every
//                frame). No step lies within 5 % of 100 %: URP draws those at 100 % (its kRenderScaleThreshold).
//   histories    the TAA history is kept at the output size, so it survives a change untouched (the previous motion vectors are at the
//                input size: the disocclusion test skips one frame). The volumetric light's history is sampled by uv, so last frame's
//                (other size) is still the history of the first frame after a change (ReflexVolumetricPass).
//   mip bias     URP sets _GlobalMipBias to log2(render scale) for the camera, and its Core.hlsl redefines SAMPLE_TEXTURE2D (and _BIAS,
//                _GRAD) with it; this project's shaders include Core.hlsl, so textures keep the output resolution's mip levels.
//   the signal   the frame time against the frame period of the target (the display rate with VSync, the FRAME RATE CAP when one is
//                set, else DYNAMIC TARGET), with FrameTimingManager (players are built with Frame Timing Stats) telling whose
//                frame it was: the main and render threads' work without their wait for Present, and the GPU time where the
//                driver reports one (Metal does: about twice the frame interval when the GPU is the limit, measured in a window;
//                D3D11's is not measured here). Without any frame timings the late frames alone decide.
//   the rule     DOWN only when frames really are late (the mean of the last 24 is over 1.04 periods; a few long frames in a fast
//                run are stutter and change nothing)
//                AND the CPU is not the cause (its own work under 85 % of the period) AND, where a GPU time exists, the GPU is busy
//                (over 85 % of the period). A frame that waits for VSync is never late, and a GPU that clocks itself down under a
//                light load reads busy without late frames, so neither lowers the resolution. Without VSync the drop is sized from
//                the frame time (at most 3 steps at once); with VSync it is one step at a time (about a third of a second each).
//                UP one step when the GPU time predicted at the next size stays under 80 % of the period for 1.5 s, or as a probe
//                after a quiet time without slow or stuttering frames (8 s, doubled whenever a step up is taken back within 10 s, at most 120 s).
//                8 frames after every change are not judged (the timings arrive a few frames late).
//   never        while paused, in the menus, during loading, in PHOTO MODE's accumulation, in the editor: the scale holds (a new
//                event starts at the top again).
// Tests: REFLEX_DYNRES=0 / 1, REFLEX_DYNRES_MIN=<percent>, REFLEX_DYNRES_TARGET=<fps>, REFLEX_DYNRES_FORCE=<percent> (that scale,
// no controller), REFLEX_DYNRES_LOG=1 (a "[dynres]" line per change and per second), REFLEX_TAA_OFFSCREEN=1 (TAA for a camera
// that renders into a target: -batchmode players).
using System;
using Reflex.Port.Options;
using UnityEngine;

namespace Reflex.Port.Rendering
{
    public sealed class ReflexDynamicResolution : MonoBehaviour
    {
        // ------------------------------------------------------------------------------------------------ the options
        const string KeyOn = "Options.DynamicResolution", KeyMin = "Options.DynamicResolutionMin", KeyTarget = "Options.DynamicResolutionTarget";
        public static readonly int[] Minimums = { 50, 58, 67, 75 };
        public static readonly int[] Targets = { 30, 45, 60, 75, 90, 120, 144 };
        public const int DefaultMinimum = 67, DefaultTarget = 60;
        /// Tests: -1 the option; 0 / 1 off / on.
        public static int EnabledOverride = -1;
        /// Tests: > 0 the minimum (percent) / the target (frames per second) instead of the options.
        public static int MinimumOverride, TargetOverride;
        /// Tests: > 0 renders at this share (0.3 .. 1) of the output; the controller rests.
        public static float ForceScale;
        public static bool LogChanges;
        /// Tests (-batchmode players present nothing, so FrameTimingManager has no GPU time there): >= 0 is the GPU time (ms) of the last frame.
        public static float TestGpuMs = -1f;

        public static bool Enabled
        {
            get => EnabledOverride >= 0 ? EnabledOverride == 1 : ReflexSave.GetInt(KeyOn, 1) != 0;
            set { ReflexSave.SetInt(KeyOn, value ? 1 : 0); ReflexSave.Save(); }
        }
        public static int Minimum
        {
            get => MinimumOverride > 0 ? Mathf.Clamp(MinimumOverride, 30, 100) : Mathf.Clamp(ReflexSave.GetInt(KeyMin, DefaultMinimum), 50, 75);
            set { ReflexSave.SetInt(KeyMin, Mathf.Clamp(value, 50, 75)); ReflexSave.Save(); }
        }
        /// DYNAMIC TARGET: the frame rate to hold when neither VSync nor a FRAME RATE CAP sets one.
        public static int Target
        {
            get => Mathf.Clamp(ReflexSave.GetInt(KeyTarget, DefaultTarget), 30, 240);
            set { ReflexSave.SetInt(KeyTarget, Mathf.Clamp(value, 30, 240)); ReflexSave.Save(); }
        }
        /// The frame rate comes from the display or the cap (the DYNAMIC TARGET row then has nothing to say).
        public static bool TargetIsFixed => TargetOverride <= 0 && (QualitySettings.vSyncCount > 0 || Application.targetFrameRate > 0);

        /// The frame rate the controller holds.
        public static float TargetFps
        {
            get
            {
                if (TargetOverride > 0) return TargetOverride;
                if (QualitySettings.vSyncCount > 0)
                {
                    double hz = Screen.currentResolution.refreshRateRatio.value;
                    if (hz > 1) return (float)(hz / QualitySettings.vSyncCount);
                }
                if (Application.targetFrameRate > 0) return Application.targetFrameRate;
                return Target;
            }
        }

        // ------------------------------------------------------------------------------------------------ the scale
        static readonly float[] Ladder = { 1f, 0.93f, 0.87f, 0.81f, 0.75f, 0.70f, 0.65f, 0.61f, 0.58f, 0.54f, 0.50f };
        static readonly float[] s_steps = new float[Ladder.Length + 2];
        static int s_stepCount, s_index; static float s_top = 1f, s_floor = 1f;
        static int s_changes;

        /// The steps from `top` (UPSCALING's share) down to the minimum.
        static void BuildSteps(float top, float floor)
        {
            if (s_stepCount > 0 && Mathf.Approximately(top, s_top) && Mathf.Approximately(floor, s_floor)) return;
            float current = s_stepCount > 0 ? s_steps[Mathf.Clamp(s_index, 0, s_stepCount - 1)] : top;
            s_top = top; s_floor = floor;
            int n = 0; s_steps[n++] = top;
            if (floor < top - 0.005f)
            {
                foreach (float l in Ladder) if (l < top - 0.03f && l > floor + 0.03f) s_steps[n++] = l;
                s_steps[n++] = floor;
            }
            s_stepCount = n;
            // the step nearest the scale in use (an option changed under a running race)
            s_index = 0;
            for (int i = 1; i < n; i++) if (Mathf.Abs(s_steps[i] - current) < Mathf.Abs(s_steps[s_index] - current)) s_index = i;
        }

        /// ReflexTemporalAA (each game camera, each frame): the share of the output to render at, given UPSCALING's own share.
        public static float Factor(float upscaling)
        {
            if (ForceScale > 0f) return Mathf.Clamp(ForceScale, 0.3f, 1f);
            if (!Enabled) { s_index = 0; return upscaling; }
            BuildSteps(upscaling, Mathf.Min(upscaling, Minimum / 100f));
            return s_steps[Mathf.Clamp(s_index, 0, s_stepCount - 1)];
        }

        /// The share of the output URP really draws at for a render scale (it draws anything within 5 % of 1 at 1).
        public static float UrpScale(float scale) => Mathf.Abs(1f - scale) < 0.05f ? 1f : scale;

        /// The perf overlay: the share of the output the world renders at now (1 = all of it), and whether the controller is at work.
        public static float CurrentScale { get; internal set; } = 1f;
        public static bool Working { get; private set; }
        public static string Status { get; private set; } = "OFF";
        public static int Changes => s_changes;

        // ------------------------------------------------------------------------------------------------ the controller
        const int Ring = 24, Settle = 8;
        const float LateFrame = 1.12f, LateMean = 1.04f, GpuBusy = 0.85f, CpuFree = 0.85f, DropTo = 0.80f, RaiseBelow = 0.80f;
        const float RaiseSeconds = 1.5f, FirstProbe = 8f, MaxProbe = 120f, TakenBack = 10f, Hitch = 0.25f;

        static ReflexDynamicResolution s_inst;
        readonly FrameTiming[] ft = new FrameTiming[1];
        readonly float[] ring = new float[Ring]; int ringN, ringHead;
        float gpuFast, gpuSlow, cpuWork; bool haveGpu, haveCpu;
        int settle; float fitSince = -1f, quietSince = -1f, probe = FirstProbe, raisedAt = -100f;
        float logAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadEnvironment()
        {
            string v = Environment.GetEnvironmentVariable("REFLEX_DYNRES");
            if (v == "0" || v == "1") EnabledOverride = v[0] - '0';
            if (int.TryParse(Environment.GetEnvironmentVariable("REFLEX_DYNRES_MIN"), out int m)) MinimumOverride = m;
            if (int.TryParse(Environment.GetEnvironmentVariable("REFLEX_DYNRES_TARGET"), out int t)) TargetOverride = t;
            if (int.TryParse(Environment.GetEnvironmentVariable("REFLEX_DYNRES_FORCE"), out int f) && f > 0) ForceScale = f / 100f;
            LogChanges = Environment.GetEnvironmentVariable("REFLEX_DYNRES_LOG") == "1";
            if (Environment.GetEnvironmentVariable("REFLEX_TAA_OFFSCREEN") == "1") ReflexTemporalAA.OffscreenTest = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_inst || Application.isEditor) return;
            var go = new GameObject("Reflex Dynamic Resolution") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_inst = go.AddComponent<ReflexDynamicResolution>();
        }

        void ResetSignal() { ringN = 0; ringHead = 0; haveGpu = haveCpu = false; fitSince = quietSince = -1f; }

        void Rest(string status, bool top)
        {
            Working = false; Status = status;
            if (top) { s_index = 0; probe = FirstProbe; }
            settle = Settle; ResetSignal();
        }

        void Update()
        {
            if (!Enabled) { Rest("OFF", true); return; }
            if (ForceScale > 0f) { Rest("FORCED", false); return; }
            if (ReflexAntiAliasing.Current != ReflexAntiAliasing.Mode.Taa) { Rest("NEEDS TAA", true); return; }
            var rt = ReflexClientLive.Runtime;
            if (!rt || !rt.Running) { Rest("IDLE", true); return; }                 // the menus, a load: the next event starts at the top
            if (rt.paused || !ReflexTemporalAA.Active || !ReflexTemporalAA.Active.active || ReflexCinematic.Accumulating) { Rest("HOLD", false); return; }
            if (s_stepCount < 2) { Rest("AT MINIMUM", false); return; }
            float dt = Time.unscaledDeltaTime;
            if (dt > Hitch) { Rest("HOLD", false); return; }                        // a hitch (a load, the window dragged): not a sample
            Working = true;
            float period = 1f / Mathf.Max(1f, TargetFps), periodMs = period * 1000f;

            FrameTimingManager.CaptureFrameTimings();
            bool timed = FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].cpuFrameTime > 0;
            if (settle > 0) { settle--; Status = "SETTLING"; return; }

            ring[ringHead] = dt; ringHead = (ringHead + 1) % Ring; if (ringN < Ring) ringN++;
            if (timed)
            {
                // the CPU's own work: the main thread without its wait for Present, or the render thread, whichever is longer
                float work = (float)Math.Max(Math.Max(0.0, ft[0].cpuMainThreadFrameTime - ft[0].cpuMainThreadPresentWaitTime), ft[0].cpuRenderThreadFrameTime);
                cpuWork = haveCpu ? Mathf.Lerp(cpuWork, work, 0.2f) : work; haveCpu = true;
            }
            float g = TestGpuMs >= 0f ? TestGpuMs : timed ? (float)ft[0].gpuFrameTime : 0f;
            if (g > 0f)
            {
                gpuFast = haveGpu ? Mathf.Lerp(gpuFast, g, 0.3f) : g;
                gpuSlow = haveGpu ? Mathf.Lerp(gpuSlow, g, 0.06f) : g;
                haveGpu = true;
            }
            if (ringN < Ring / 2) { Status = "MEASURING"; return; }
            int late = 0; float sum = 0f;
            for (int i = 0; i < ringN; i++) { float f = ring[i]; sum += f; if (f > LateFrame * period) late++; }
            float mean = sum / ringN;
            // slow: the mean frame time is over the period (a GPU limit always shows there: with VSync every missed refresh adds a whole
            // period). A few long frames in an otherwise fast run are stutter, not a GPU limit (measured: 3 of 24 at a 10 ms mean).
            bool slow = mean > LateMean * period;
            bool stutter = late * 8 >= ringN;
            float now = Time.realtimeSinceStartup;
            float cur = s_steps[s_index];

            // ---- down: frames are late and the graphics card is what is busy
            if (slow && s_index < s_stepCount - 1)
            {
                // not the CPU's doing: its own work (without the wait for Present) would fit the period. And, where there is a GPU time,
                // the GPU is busy. (Metal's GPU time reads about twice the frame interval when the GPU is the limit, measured in a
                // window, so it serves as this gate only, never as the size of a step.)
                bool test = TestGpuMs >= 0f;
                bool cpuFree = test || !haveCpu || cpuWork < CpuFree * periodMs;
                bool gpuBound = cpuFree && (!haveGpu || gpuFast > GpuBusy * periodMs);
                if (gpuBound)
                {
                    // the step: sized from the frame time when it runs free (no VSync: the frame time is the GPU's), else one step
                    // at a time (a VSync frame time is a whole number of refreshes and says nothing about how far off it is)
                    float want = QualitySettings.vSyncCount > 0 && !test ? 1f : Mathf.Clamp(DropTo * period / mean, 0.5f, 1f);
                    int to = s_index + 1;
                    while (to < s_stepCount - 1 && to < s_index + 3 && Sq(s_steps[to] / cur) > want) to++;
                    if (now - raisedAt < TakenBack) probe = Mathf.Min(probe * 2f, MaxProbe);   // the step up did not hold
                    Change(to, $"down: {late}/{ringN} late, mean {mean * 1000f:0.0} ms of {periodMs:0.0}, " + (haveGpu ? $"GPU {gpuFast:0.0} ms" : $"no GPU time, CPU {(haveCpu ? cpuWork.ToString("0.0") : "-")} ms"));
                    return;
                }
                Status = "CPU LIMIT";
            }
            else Status = s_index == 0 ? "FULL" : "SCALED";

            // ---- up: one step when it would still fit, or as a probe after a quiet time
            if (s_index > 0)
            {
                if (stutter || slow) { fitSince = quietSince = -1f; }
                else
                {
                    if (quietSince < 0f) quietSince = now;
                    float grow = Sq(s_steps[s_index - 1] / cur);
                    bool fits = haveGpu && gpuSlow * grow < RaiseBelow * periodMs && gpuFast * grow < RaiseBelow * periodMs;
                    if (!fits) fitSince = -1f; else if (fitSince < 0f) fitSince = now;
                    bool byTime = fits && now - fitSince >= RaiseSeconds && now - quietSince >= RaiseSeconds;
                    bool byProbe = now - quietSince >= probe;
                    if (byTime || byProbe)
                    {
                        raisedAt = now;
                        Change(s_index - 1, byTime ? $"up: GPU {gpuSlow:0.0} ms of {periodMs:0.0}" : $"up (probe after {probe:0} s without a late frame)");
                        return;
                    }
                }
                if (now - raisedAt > 2f * MaxProbe) probe = FirstProbe;
            }
            if (LogChanges && now >= logAt)
            {
                logAt = now + 1f;
                Debug.Log(FormattableString.Invariant($"[dynres] scale {cur * 100f:0} % ({Status}) frame {mean * 1000f:0.00} ms late {late}/{ringN} target {periodMs:0.00} ms GPU {(haveGpu ? gpuFast.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "-")} CPU {(haveCpu ? cpuWork.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "-")}"));
            }
        }

        static float Sq(float x) => x * x;

        void Change(int to, string why)
        {
            float from = s_steps[s_index];
            s_index = Mathf.Clamp(to, 0, s_stepCount - 1);
            s_changes++;
            settle = Settle; ringN = 0; ringHead = 0; fitSince = quietSince = -1f; haveGpu = false;
            Status = s_index == 0 ? "FULL" : "SCALED";
            if (LogChanges) Debug.Log(FormattableString.Invariant($"[dynres] CHANGE {from * 100f:0} -> {s_steps[s_index] * 100f:0} % ({why})"));
        }
    }
}
