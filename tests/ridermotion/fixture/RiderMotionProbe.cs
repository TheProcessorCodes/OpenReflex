// TEST ONLY (tests/ridermotion): RIDER MOTION LOOSE against ORIGINAL on the same ride. Launches one event with computer riders, follows one
// of them (or the local rider with the throttle held) with cameras fixed to its bike, and every `step` seconds of game time holds the world
// and draws the held frame twice: with the layer (as posed) and in the original pose of that same frame (RiderLooseMotion.Capture / Show).
// Every running frame goes to telemetry.csv (the followed bike's motion, the layer's state, how far the hands / feet of EVERY rider are from
// the original's, the limbs' extension), and the core's pose arrays are compared with the pose kept before the layer (they are what the
// ghosts and the online records take). `perf`: the layer switched off / on in turns, frame times and the layer's own time.
// Plan (REFLEX_RM_PLAN, JSON) -> REFLEX_RM_OUT. Presses nothing, writes no game state; muted.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Reflex.Port.Content;
using Reflex.Port.OriginalWorld;
using Reflex.Port.Rendering;
using UnityEngine;

[DefaultExecutionOrder(32000)]
public sealed class RiderMotionProbe : MonoBehaviour
{
    const string Tag = "[rider-motion-probe] ";
    [Serializable] public sealed class Cam { public string name = "side"; public float[] offset = { 8.5f, 0.6f, 0.3f }; public float lookY = 0.5f, fov = 36f; }   // (from the rider's pelvis, the bike's level heading)
    [Serializable] public sealed class Plan
    {
        public string @event = "National/NAT_Stillwater";
        public int ai = 11, follow = 0, width = 960, height = 540;   // follow: the opponent (OriginalOpponents.Riders order), -1 the local rider
        public float start = 1f, seconds = 40f, step = 0.1f, perf = 0f, perfSlice = 1.5f, settle = 0.5f;
        public bool hold, shots = true;
        public Cam[] cams = { new Cam() };
    }
    Plan plan; string dir; OriginalWorldRuntime rt; Camera cam; int camIndex = -1;
    readonly StringBuilder csv = new StringBuilder();
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string p = Environment.GetEnvironmentVariable("REFLEX_RM_PLAN"), d = Environment.GetEnvironmentVariable("REFLEX_RM_OUT");
        if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(d) || FindAnyObjectByType<RiderMotionProbe>()) return;
        var go = new GameObject("Rider motion probe (test)"); DontDestroyOnLoad(go);
        var s = go.AddComponent<RiderMotionProbe>();
        s.plan = JsonUtility.FromJson<Plan>(File.ReadAllText(p)); s.dir = d;
    }

    void Update() { AudioListener.volume = 0f; }
    void Fail(string why) { Debug.LogError(Tag + "FAIL " + why); Application.Quit(3); }

    // the followed rider: its bike's body, its rider
    bool Followed(out Transform body, out OriginalRider rider)
    {
        body = null; rider = null;
        if (!rt) return false;
        if (plan.follow < 0) { body = rt.chassis; rider = rt.rider; return body; }
        int k = 0;
        foreach (var (_, _, bike, rm, posed) in rt.opponentRiders.Riders)
        {
            if (k++ != plan.follow) continue;
            if (!posed || bike == null) return false;
            body = bike.parts.FirstOrDefault(p => p.node == 0).part ?? (bike.parts.Count > 0 ? bike.parts[0].part : null);
            rider = rm?.rider; return body;
        }
        return false;
    }
    IEnumerable<OriginalRider> AllRiders()
    {
        if (rt && rt.rider) yield return rt.rider;
        if (rt && rt.opponentRiders) foreach (var (_, _, _, rm, posed) in rt.opponentRiders.Riders) if (posed && rm?.rider) yield return rm.rider;
    }

    // the cameras ride with the bike's heading (level): the rider's motion on the bike is what shows
    void LateUpdate()
    {
        if (camIndex < 0 || !cam || !Followed(out var body, out var rider)) return;
        var c = plan.cams[camIndex];
        Vector3 f = body.forward; f.y = 0; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        Vector3 r = Vector3.Cross(Vector3.up, f);
        // (about the rider's pelvis as the ORIGINAL pose has it: the same camera for both poses of a held frame)
        Vector3 anchor = rider && RiderLooseMotion.TryGet(rider, out var st) && st.pos0 != null && st.captured ? st.pos0[0] : rider && rider.bones != null && rider.bones[0] ? rider.bones[0].position : body.position;
        Vector3 at = anchor + Vector3.up * c.lookY;
        Vector3 pos = anchor + r * c.offset[0] + Vector3.up * c.offset[1] + f * c.offset[2];
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(at - pos, Vector3.up));
        cam.fieldOfView = c.fov;
    }

    // ---- the pose arrays against the kept poses (every presented frame, where the online sender takes the local pose)
    long arrayFrames, layerFrames; double arrayMax, layerMax;
    static readonly int[] Torso = { 0, 1, 2, 6, 30, 31 };
    void OnView(OriginalWorldRuntime w, OriginalSimulation.Results v)
    {
        if (w != rt || !RiderLooseMotion.Capture) return;
        void Check(OriginalRider rider, float[] rows, int bones)
        {
            if (!rider || rows == null || bones != OriginalRider.OriginalBoneCount || !RiderLooseMotion.TryGet(rider, out var s) || s.pos0 == null || !s.captured || s.frame != Time.frameCount) return;
            double a = 0, l = 0;
            foreach (int i in Torso)
            {
                var p = new Vector3(rows[i * 16 + 12], rows[i * 16 + 13], rows[i * 16 + 14]);
                a = Math.Max(a, (p - s.pos0[i]).magnitude); l = Math.Max(l, (s.pos1[i] - s.pos0[i]).magnitude);
            }
            arrayFrames++; arrayMax = Math.Max(arrayMax, a);
            if (l > 1e-3) { layerFrames++; layerMax = Math.Max(layerMax, l); }
        }
        Check(rt.rider, v.riderPose, v.riderBones);
        if (rt.opponentRiders)
        {
            var bySlot = new Dictionary<int, OriginalRider>();
            foreach (var (_, slot, _, rm, posed) in rt.opponentRiders.Riders) if (posed && rm?.rider) bySlot[slot] = rm.rider;
            for (int i = 0; i < v.slotCount; i++) if (bySlot.TryGetValue(v.slots[i], out var r)) Check(r, v.slotRiderPose[i], v.slotRiderBones[i]);
        }
    }

    static float Ratio(Vector3[] p, int a) => (p[a + 2] - p[a]).magnitude / Mathf.Max(1e-3f, (p[a + 1] - p[a]).magnitude + (p[a + 2] - p[a + 1]).magnitude);

    void Telemetry(float t)
    {
        if (!Followed(out var body, out var rider)) return;
        // every rider: the hands / feet against the original's (position, rotation), the limbs' extension with the layer
        float handErr = 0, footErr = 0, rotErr = 0, armMax = 0, legMax = 0, armOrig = 0, legOrig = 0, torsoMove = 0; int loose = 0;
        foreach (var r in AllRiders())
        {
            if (!RiderLooseMotion.TryGet(r, out var s) || s.pos0 == null || !s.captured || s.frame != Time.frameCount) continue;
            if (s.weight > 0.01f) loose++;
            foreach (int i in new[] { 12, 41 }) { handErr = Mathf.Max(handErr, (s.pos1[i] - s.pos0[i]).magnitude); rotErr = Mathf.Max(rotErr, Quaternion.Angle(s.rot1[i], s.rot0[i])); }
            foreach (int i in new[] { 62, 65 }) { footErr = Mathf.Max(footErr, (s.pos1[i] - s.pos0[i]).magnitude); rotErr = Mathf.Max(rotErr, Quaternion.Angle(s.rot1[i], s.rot0[i])); }
            armMax = Mathf.Max(armMax, Mathf.Max(Ratio(s.pos1, 10), Ratio(s.pos1, 39))); legMax = Mathf.Max(legMax, Mathf.Max(Ratio(s.pos1, 60), Ratio(s.pos1, 63)));
            armOrig = Mathf.Max(armOrig, Mathf.Max(Ratio(s.pos0, 10), Ratio(s.pos0, 39))); legOrig = Mathf.Max(legOrig, Mathf.Max(Ratio(s.pos0, 60), Ratio(s.pos0, 63)));
            torsoMove = Mathf.Max(torsoMove, (s.pos1[6] - s.pos0[6]).magnitude);
        }
        Vector3 lag = Vector3.zero, head = Vector3.zero, seat = Vector3.zero; float w = 0, legRatio = 0, armRatio = 0, chestMove = 0, headTurn = 0;
        if (rider && RiderLooseMotion.TryGet(rider, out var fs) && fs.pos0 != null && fs.frame == Time.frameCount)
        {
            lag = fs.lag; head = fs.head; w = fs.weight;
            seat = Quaternion.Inverse(body.rotation) * (fs.pos0[0] - body.position);
            legRatio = Mathf.Min(Ratio(fs.pos0, 60), Ratio(fs.pos0, 63)); armRatio = Mathf.Max(Ratio(fs.pos0, 10), Ratio(fs.pos0, 39));
            chestMove = (fs.pos1[6] - fs.pos0[6]).magnitude; headTurn = Quaternion.Angle(fs.rot1[31], fs.rot0[31]);
        }
        Vector3 p = body.position, f = body.forward, u = body.up;
        csv.Append(string.Format(Inv, "{0:0.0000},{1:0.000},{2:0.000},{3:0.000},{4:0.0000},{5:0.0000},{6:0.0000},{7:0.0000},{8:0.0000},{9:0.0000},", t, p.x, p.y, p.z, f.x, f.y, f.z, u.x, u.y, u.z));
        csv.Append(string.Format(Inv, "{0:0.0000},{1:0.0000},{2:0.0000},{3:0.00},{4:0.00},{5:0.00},{6:0.000},{7:0.000},{8:0.000},{9:0.000},", lag.x, lag.y, lag.z, head.x, head.y, head.z, w, seat.x, seat.y, seat.z));
        csv.Append(string.Format(Inv, "{0:0.000},{1:0.000},{2:0.0000},{3:0.00},{4},{5:0.00000},{6:0.00000},{7:0.000},{8:0.0000},{9:0.0000},{10:0.0000},{11:0.0000},{12:0.0000},{13:0.00000}\n",
            legRatio, armRatio, chestMove, headTurn, loose, handErr, footErr, rotErr, armMax, legMax, armOrig, legOrig, torsoMove, Time.unscaledDeltaTime));
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 4f) yield return null;
        try
        {
            var all = ReflexEvents.All.ToList();
            var ev = ReflexEvents.Find(plan.@event) ?? all.FirstOrDefault(e => e.key.IndexOf(plan.@event, StringComparison.OrdinalIgnoreCase) >= 0);
            if (ev == null) throw new InvalidOperationException("no event " + plan.@event);
            Debug.Log(Tag + "LAUNCH " + ev.key + " ai " + plan.ai + " motion " + (RiderLooseMotion.Loose ? "LOOSE " + RiderLooseMotion.Strength : "ORIGINAL"));
            ReflexEventSelect.Launch(ev.key, plan.ai, false, "Reflex", 0, null);
        }
        catch (Exception e) { Fail(e.ToString()); yield break; }
        OriginalLevel level = null; float wait = Time.realtimeSinceStartup;
        while (true)
        {
            rt = FindAnyObjectByType<OriginalWorldRuntime>(); level = FindAnyObjectByType<OriginalLevel>();
            if (rt && rt.FailureMessage != null) { Fail("world: " + rt.FailureMessage); yield break; }
            if (rt && level && level.Loaded && rt.Running && !double.IsNaN(rt.PresentedGameFrame)) break;
            if (Time.realtimeSinceStartup - wait > 600) { Fail("the event did not start"); yield break; }
            yield return null;
        }
        Debug.Log(Tag + "RUNNING load " + (Time.realtimeSinceStartup - wait).ToString("0.0") + " s");
        Reflex.Port.Options.ReflexSettings.HudHidden = true;
        ReflexSpeedBlur.Setting = 0;   // (the scratch save)
        ReflexAntiAliasing.ModeOverride = 0;   // (each shot is one render of a held frame: no temporal history between the two poses)
        cam = rt.chaseCamera ? rt.chaseCamera : Camera.main;
        wait = Time.realtimeSinceStartup;
        while (!(rt.Race.valid != 0 && rt.Race.state == 2)) { if (Time.realtimeSinceStartup - wait > 180) { Fail("the race did not start"); yield break; } yield return null; }
        Debug.Log(Tag + "RACING after " + (Time.realtimeSinceStartup - wait).ToString("0.0") + " s");
        if (plan.hold) typeof(ExperimentalMtxLaunch).GetProperty("HoldThrottle").GetSetMethod(true).Invoke(null, new object[] { true });
        RiderLooseMotion.Capture = true; camIndex = 0;   // (the camera stays with the followed rider: it is a near rider throughout)
        OriginalWorldRuntime.ViewApplied += OnView;
        csv.Append("t,x,y,z,fx,fy,fz,ux,uy,uz,lagx,lagy,lagz,headp,heady,headr,weight,seatx,seaty,seatz,legratio,armratio,chestmove,headturn,loose,handerr,footerr,roterr,armmax,legmax,armorig,legorig,torsomove,dt\n");
        var index = new StringBuilder("index,t\n");
        float begin = float.NaN, next = 0f, last = float.NaN; int shot = 0;
        while (true)
        {
            yield return null;
            if (rt.FailureMessage != null) { Fail("world: " + rt.FailureMessage); yield break; }
            if (double.IsNaN(rt.PresentedGameFrame)) continue;
            float t = rt.PresentedGameTime;
            if (float.IsNaN(begin)) { begin = t + plan.start; next = begin; }
            if (t == last) continue;
            last = t;
            if (t < begin) continue;
            if (t > begin + plan.seconds) break;
            Telemetry(t - begin);
            if (!plan.shots || t < next || !Followed(out _, out _)) continue;
            next += plan.step; if (next < t) next = t + plan.step;
            // the frame held, drawn with the layer and in the original's pose
            rt.paused = true;
            float heldAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - heldAt < plan.settle) yield return null;   // (the speed blur of the moving camera fades out of the held frame)
            index.Append(shot).Append(',').Append((t - begin).ToString("0.0000", Inv)).Append('\n');
            for (int mode = 0; mode < 2; mode++)
            {
                RiderLooseMotion.Show(mode == 1);
                for (int ci = 0; ci < plan.cams.Length; ci++)
                {
                    camIndex = ci;
                    yield return null; yield return null;   // (the skinning of the pose just set; the camera placed by LateUpdate)
                    Shot(Path.Combine(dir, $"f{shot:0000}_{plan.cams[ci].name}_{(mode == 1 ? "original" : "loose")}.jpg"));
                }
            }
            RiderLooseMotion.Show(false);
            camIndex = 0; shot++;
            rt.paused = false;
        }
        OriginalWorldRuntime.ViewApplied -= OnView;
        File.WriteAllText(Path.Combine(dir, "telemetry.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(dir, "index.csv"), index.ToString());
        Debug.Log(Tag + string.Format(Inv, "ARRAYS rider pose arrays against the pose kept before the layer: {0} rider-frames, max difference {1:0.000000} ft; the layer moved the torso in {2} of them (up to {3:0.000} ft)", arrayFrames, arrayMax, layerFrames, layerMax));
        Debug.Log(Tag + "SHOTS " + shot);

        if (plan.perf > 0f)
        {   // the layer off / on in turns: frame times (the first 0.3 s of a slice left out), the layer's own time
            RiderLooseMotion.Capture = false; RiderLooseMotion.Timing = true;
            var ms = new[] { new List<float>(), new List<float>() }; long ticks = 0; int calls = 0, frames = 0;
            float end = Time.realtimeSinceStartup + plan.perf; int slice = 0;
            while (Time.realtimeSinceStartup < end)
            {
                int mode = slice++ & 1;
                RiderLooseMotion.ModeOverride = mode; RiderLooseMotion.TimingTicks = 0; RiderLooseMotion.TimingCalls = 0;
                float s0 = Time.realtimeSinceStartup; int f0 = 0;
                while (Time.realtimeSinceStartup - s0 < plan.perfSlice)
                {
                    yield return null;
                    if (Time.realtimeSinceStartup - s0 < 0.3f) { RiderLooseMotion.TimingTicks = 0; RiderLooseMotion.TimingCalls = 0; continue; }
                    ms[mode].Add(Time.unscaledDeltaTime * 1000f); f0++;
                }
                if (mode == 1) { ticks += RiderLooseMotion.TimingTicks; calls += RiderLooseMotion.TimingCalls; frames += f0; }
            }
            float Med(List<float> l) { if (l.Count == 0) return 0; l.Sort(); return l[l.Count / 2]; }
            float Mean(List<float> l) => l.Count == 0 ? 0 : l.Average();
            double layerMs = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log(Tag + string.Format(Inv, "PERF original median {0:0.000} ms mean {1:0.000} ({2} frames) | loose median {3:0.000} ms mean {4:0.000} ({5} frames) | the layer itself {6:0.0000} ms a frame, {7:0.00} riders a frame, {8:0.00} us a rider",
                Med(ms[0]), Mean(ms[0]), ms[0].Count, Med(ms[1]), Mean(ms[1]), ms[1].Count, frames > 0 ? layerMs / frames : 0, frames > 0 ? (double)calls / frames : 0, calls > 0 ? layerMs * 1000.0 / calls : 0));
        }
        Debug.Log(Tag + "DONE");
        Application.Quit(0);
    }

    void Shot(string file)
    {
        if (!cam) return;
        var rtx = RenderTexture.GetTemporary(plan.width, plan.height, 24, RenderTextureFormat.ARGB32);
        var prior = cam.targetTexture;
        try
        {
            cam.targetTexture = rtx;
            cam.Render(); cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rtx;
            var t = new Texture2D(plan.width, plan.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, plan.width, plan.height), 0, 0); t.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(file, t.EncodeToJPG(90)); Destroy(t);
        }
        catch (Exception e) { Debug.Log(Tag + "shot failed: " + e.Message); }
        finally { cam.targetTexture = prior; RenderTexture.ReleaseTemporary(rtx); }
    }
}
