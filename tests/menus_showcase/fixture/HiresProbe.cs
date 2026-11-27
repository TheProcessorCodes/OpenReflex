// TEST ONLY (tests/menus_showcase fixtures): BIKE DETAIL (OPTIONS > VIDEO) before / after in a real race.
// REFLEX_HIRES_PROBE=<event key or type>@<riders>[@<details, e.g. 012>][,...]: for each item and each BIKE DETAIL level (0 ORIGINAL, 1 HIGH, 2 HIGH:
// ALL RIDERS; REFLEX_BIKE_DETAIL is set in the process before the launch) starts the event through the shell's own launch, skips the intro on a
// virtual pad and, on the gate (the same pose every launch):
//   - logs the local vehicle (parts, triangles, the grips RiderGripIK found), the other riders' (triangles drawn, LODs), the texture memory
//   - saves matched pictures from cameras fixed to the bike ("<item>-d<detail>-<view>.png" into REFLEX_HIRES_SHOTS): chase, right, front-right,
//     the hands on the grips from above, the right boot on its peg, the left boot
//   - measures the mean time of a 1080p render of the game camera (3 s), then rides for 8 s, brakes, and measures and pictures again
// REFLEX_HIRES_MOD=<part of a .rxmod bike's mod id> (e.g. alive-23-blaze-450f): the local rider is on that bike (a pro's vehicle of the model it
// rides as, the mod chosen for the model); REFLEX_HIRES_MODEL=<VehicleModelId>: on the original bike of that model. Neither: the profile's own.
// REFLEX_HIRES_LODS=1: beside the local bike on the first gate, the same bike built as another rider's (the twin + the race models' further LODs)
// pictured at each of its LODs ("<item>-lodcheck-<n>.png"). Muted. "[hires-probe] ..." lines; quits at the end.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.OriginalWorld;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Reflex.Port.Showcase
{
    public sealed class HiresProbe : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Script => Environment.GetEnvironmentVariable("REFLEX_HIRES_PROBE");
        Gamepad pad; string shots; OriginalWorldRuntime rt;
        bool overrideCam; Vector3 camOffset, camAim; float camFov = 40f; string pendingShot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (string.IsNullOrEmpty(Script)) return;
            OpenShell.SkipIntro = true;
            var g = new GameObject("hires probe (test only)"); DontDestroyOnLoad(g); g.AddComponent<HiresProbe>();
        }

        void Start()
        {
            Application.runInBackground = true; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            shots = Environment.GetEnvironmentVariable("REFLEX_HIRES_SHOTS"); if (!string.IsNullOrEmpty(shots)) Directory.CreateDirectory(shots);
            StartCoroutine(Run());
        }
        void Log(string s) => Debug.Log("[hires-probe] " + s);
        IEnumerator Seconds(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        void Pad(bool a, float throttle, float brake = 0f)
        {
            if (pad == null || !pad.added) pad = InputSystem.AddDevice<Gamepad>("hires probe pad");
            var st = new GamepadState { rightTrigger = throttle, leftTrigger = brake };
            if (a) st = st.WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(pad, st);
        }

        void LateUpdate()
        {
            if (AudioListener.volume != 0f) AudioListener.volume = 0f;   // test players are muted
            if (overrideCam && rt && rt.chaseCamera && rt.chassis)
            {
                var cam = rt.chaseCamera; var aim = rt.chassis.position + rt.chassis.rotation * camAim;
                cam.transform.position = aim + rt.chassis.rotation * camOffset;
                cam.transform.rotation = Quaternion.LookRotation(aim - cam.transform.position, Vector3.up);
                cam.fieldOfView = camFov; cam.nearClipPlane = 0.05f;
            }
            if (pendingShot != null) { OffscreenShot(rt ? rt.chaseCamera : Camera.main, pendingShot); pendingShot = null; }
        }

        void OffscreenShot(Camera cam, string file)
        {
            if (!cam) { Log("OFFSCREEN no camera for " + file); return; }
            const int W = 1920, H = 1080;
            var rtx = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var prior = cam.targetTexture;
            try
            {
                cam.targetTexture = rtx;
                for (int i = 0; i < 8; i++) cam.Render();   // (the TAA settles)
                var prev = RenderTexture.active; RenderTexture.active = rtx;
                var t = new Texture2D(W, H, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(file, t.EncodeToPNG()); Destroy(t);
            }
            catch (Exception e) { Log("OFFSCREEN failed: " + e.Message); }
            finally { cam.targetTexture = prior; RenderTexture.ReleaseTemporary(rtx); }
        }
        IEnumerator Shot(string name)
        {
            if (string.IsNullOrEmpty(shots)) yield break;
            pendingShot = Path.Combine(shots, name.Replace('/', '_') + ".png");
            while (pendingShot != null) yield return null;
            Log("shot " + name);
        }
        IEnumerator View(string tag, string view, Vector3 aim, Vector3 off, float fov)
        {
            camAim = aim; camOffset = off; camFov = fov; overrideCam = true;
            yield return Frames(40);   // (the cut's motion blur and the TAA settle)
            yield return Shot(tag + "-" + view);
        }

        float frameMs;
        // (a batch player presents no frames: the game camera is rendered into a target here, and a 1 x 1 read-back waits for the GPU)
        IEnumerator Measure(float seconds)
        {
            var cam = rt ? rt.chaseCamera : Camera.main; frameMs = 0f;
            if (!cam) yield break;
            var tex = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var prev = cam.targetTexture; int n = 0; double total = 0;
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < seconds;)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                cam.targetTexture = tex;
                for (int k = 0; k < 10; k++) cam.Render();
                cam.targetTexture = prev;
                RenderTexture.active = tex; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;
                total += sw.Elapsed.TotalMilliseconds; n += 10;
                yield return null;
            }
            frameMs = (float)(total / Mathf.Max(1, n));
            tex.Release(); Destroy(tex); Destroy(one);
        }

        static (int triangles, int renderers) Drawn(GameObject root)
        {
            int tris = 0, n = 0;
            if (root)
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled || r.forceRenderingOff) continue;
                    Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
                    if (!m) continue;
                    n++; for (int k = 0; k < m.subMeshCount; k++) tris += (int)(m.GetIndexCount(k) / 3);
                }
            return (tris, n);
        }
        static (int triangles, int renderers) Drawn(ReflexBikeModel b)
        {
            if (b == null) return (0, 0);
            var a = Drawn(b.chassis); var f = Drawn(b.frontWheel ? b.frontWheel.gameObject : null); var r = Drawn(b.rearWheel ? b.rearWheel.gameObject : null);
            return (a.triangles + f.triangles + r.triangles, a.renderers + f.renderers + r.renderers);
        }
        static long TextureBytes(ReflexBikeModel b)
        {
            if (b == null) return 0; var seen = new HashSet<Texture>(); long bytes = 0;
            foreach (var g in new[] { b.chassis, b.frontWheel ? b.frontWheel.gameObject : null, b.rearWheel ? b.rearWheel.gameObject : null })
                if (g) foreach (var r in g.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials)
                    if (m) foreach (int id in m.GetTexturePropertyNameIDs()) { var t = m.GetTexture(id); if (t && seen.Add(t)) bytes += (long)(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(t.width, t.height, t.graphicsFormat) * (t.mipmapCount > 1 ? 4.0 / 3.0 : 1.0)); }
            return bytes;
        }

        IEnumerator Run()
        {
            OpenShell shell = null;
            for (float t = 0; t < 120f && !(shell = OpenShell.Instance); t += Time.unscaledDeltaTime) yield return null;
            yield return Seconds(2f);
            Reflex.Port.Rendering.ReflexSpeedBlur.Setting = 0;   // (the scratch save: the views cut between fixed cameras, and the blur smears a cut)
            // the local vehicle: a pro's vehicle of the model (as tests/rxmod's RxModRideProbe launches one), the mod chosen for it
            string modPart = Environment.GetEnvironmentVariable("REFLEX_HIRES_MOD");
            int model = int.TryParse(Environment.GetEnvironmentVariable("REFLEX_HIRES_MODEL"), NumberStyles.Integer, Inv, out int mv) ? mv : -1;
            if (!string.IsNullOrEmpty(modPart))
            {
                var mod = BikeMods.All.FirstOrDefault(m => m.rx != null && m.rx.bikes.Count > 0 && m.id.IndexOf(modPart, StringComparison.OrdinalIgnoreCase) >= 0 && !m.id.EndsWith("-hr", StringComparison.OrdinalIgnoreCase) && !m.id.EndsWith("-parts", StringComparison.OrdinalIgnoreCase));
                if (mod == null) { Log("FAIL no .rxmod bike mod matches " + modPart + " (installed: " + string.Join(", ", BikeMods.All.Where(m => m.rx != null).Select(m => m.id).Take(40)) + ")"); Application.Quit(1); yield break; }
                var bike = mod.rx.bikes[0]; model = bike.vehicleModelId;
                BikeMods.SetChoice(model, mod.id);
                Log($"the local bike: {mod.id} / {bike.name?.Full} as VehicleModelId {model}; its high-res twin: {(bike.hr != null ? bike.hr.mod.name : "none")}");
            }
            foreach (var item in Script.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0))
            {
                var f = item.Split('@'); string mode = f[0]; int riders = f.Length > 1 ? int.Parse(f[1], Inv) : 12; string details = f.Length > 2 ? f[2] : "012";
                var evRow = ReflexEvents.All.FirstOrDefault(e => string.Equals(e.spec.type, mode, StringComparison.OrdinalIgnoreCase)) ?? ReflexEvents.Find(mode);
                if (evRow == null) { Log("FAIL no event " + mode); continue; }
                string key = evRow.key;
                ReflexWorldFiles.Garage garage = null;
                if (model >= 0)
                {
                    var T = ReflexEvents.Tables;
                    int I(Dictionary<string, object> r, string k) { try { return r != null && r.TryGetValue(k, out var o) ? Convert.ToInt32(o, Inv) : -1; } catch { return -1; } }
                    foreach (var arv in T["AIRiderVehicle"])
                    {
                        var av = T["AIVehicle"].FirstOrDefault(x => I(x, "AIVehicleId") == I(arv, "AIVehicleId"));
                        if (I(av, "VehicleModelId") != model) continue;
                        int riderId = I(arv, "AIRiderId"), gear = I(T["AIRider"].FirstOrDefault(x => I(x, "AIRiderId") == riderId), "DefaultRiderGearSetId");
                        int type = I(T["VehicleModel"].FirstOrDefault(x => I(x, "VehicleModelId") == model), "VehicleTypeId");
                        garage = new ReflexWorldFiles.Garage { riderOwner = ReflexWorldFiles.Garage.AI, riderId = riderId, riderGearSet = gear, vehicleOwner = ReflexWorldFiles.Garage.AI, vehicleId = I(av, "AIVehicleId"), vehicleTypeId = type, vehicleClass = -1 };
                        var g2 = type >= 0 ? evRow.GarageFor(type) : null; if (g2 != null) garage.vehicleClass = g2.vehicleClass;
                        break;
                    }
                    if (garage == null) Log("no pro rides model " + model + ": the profile's own vehicle");
                }
                bool lodChecked = false;
                foreach (char dc in details)
                {
                    int detail = dc - '0';
                    Environment.SetEnvironmentVariable("REFLEX_BIKE_DETAIL", detail.ToString(Inv));
                    string tag = (mode + "-" + riders + "-d" + detail).Replace('/', '_');
                    Log($"== {key} riders {riders} BIKE DETAIL {detail}");
                    ReflexEventSelect.Launch(key, riders <= 1 ? 0 : 2, false, "Reflex", 0, garage, riders: riders);
                    rt = null;
                    for (float t = 0; t < 300f && !((rt = FindAnyObjectByType<OriginalWorldRuntime>()) && rt.Running && rt.Roster != null); t += Time.unscaledDeltaTime) yield return null;
                    if (!rt) { Log("FAIL no world for " + key); continue; }
                    yield return Seconds(3f);
                    for (int i = 0; i < 4; i++) { Pad(true, 0); yield return Seconds(0.1f); Pad(false, 0); yield return Seconds(0.6f); }   // SKIP INTRO
                    int others = rt.Roster.entries.Count(e => !e.Local);
                    var opp = FindAnyObjectByType<OriginalOpponents>();
                    for (float t = 0; t < 180f && others > 0 && !(opp && opp.Built >= others); t += Time.unscaledDeltaTime) { if (!opp) opp = FindAnyObjectByType<OriginalOpponents>(); yield return null; }
                    for (float t = 0; t < 30f && rt.BikeModel == null; t += Time.unscaledDeltaTime) yield return null;
                    yield return Seconds(2.5f);
                    var bm = rt.BikeModel;
                    var mine = Drawn(bm);
                    int oppTris = 0, oppParts = 0; var lodLines = new List<string>();
                    if (opp) foreach (var o in opp.Riders) { var d = Drawn(o.bike); oppTris += d.triangles; oppParts += d.renderers; lodLines.Add(o.name + ": " + d.triangles + " tris" + (o.bike != null && o.bike.HasLods ? " [" + o.bike.LodState + "]" : "") + (o.bike?.FarOwnMaterials != null ? " far-own materials " + o.bike.FarOwnMaterials() : "")); }
                    Log($"STATS {tag}: local bike \"{(bm?.chassis ? bm.chassis.name : "none")}\" {bm?.parts.Count} parts, {mine.triangles} triangles in {mine.renderers} renderers, textures {TextureBytes(bm) / 1048576f:0.0} MB, rx fit {bm?.rxFit ?? "-"}; "
                        + $"{others} other riders: {oppTris} bike triangles in {oppParts} renderers drawn now, built in {(opp ? opp.buildMilliseconds : 0):0} ms; all textures {Texture.currentTextureMemory / 1048576f:0} MB, meshes+all graphics {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576f:0} MB");
                    {
                        var rm = Drawn(rt.rider ? rt.rider.gameObject : null); int oppRider = 0; var rl = new List<string>();
                        if (opp) foreach (var o in opp.Riders) { int t = Drawn(o.rider?.root).triangles; oppRider += t; if (o.rider != null) rl.Add(o.name + ": " + t + (o.rider.highRes ? " HR" : "") + " [" + o.rider.LodState + "] far-own " + o.rider.farOwnMaterials); }
                        Log($"RIDER {tag}: local rider {rm.triangles} triangles in {rm.renderers} renderers; other riders' gear {oppRider} triangles drawn now" + (rl.Count > 0 ? "\n  " + string.Join("\n  ", rl.Take(4)) : ""));
                    }
                    if (bm != null) Log("PARTS " + tag + ": " + string.Join(", ", bm.parts.Select(p => p.name + "=" + p.material)));
                    if (lodLines.Count > 0) Log("LODS " + tag + ":\n  " + string.Join("\n  ", lodLines));
                    yield return Shot(tag + "-gate-chase");
                    yield return View(tag, "gate-right", new Vector3(0, 0.6f, 0), new Vector3(5.5f, 0.6f, 0.2f), 38f);
                    yield return View(tag, "gate-front-right", new Vector3(0, 0.6f, 0), new Vector3(2.6f, 1.2f, 4.4f), 38f);
                    yield return View(tag, "gate-hands", new Vector3(0, 1.9f, 0.4f), new Vector3(0.0f, 1.5f, 2.4f), 50f);
                    yield return View(tag, "gate-right-hand", new Vector3(1.1f, 1.95f, 0.4f), new Vector3(0.7f, 0.5f, 1.2f), 34f);
                    yield return View(tag, "gate-left-hand", new Vector3(-1.1f, 1.95f, 0.4f), new Vector3(-0.7f, 0.5f, 1.2f), 34f);
                    yield return View(tag, "gate-right-boot", new Vector3(0.5f, -0.4f, -0.3f), new Vector3(2.0f, 0.3f, 1.2f), 40f);
                    yield return View(tag, "gate-left-boot", new Vector3(-0.5f, -0.4f, -0.3f), new Vector3(-2.0f, 0.3f, 1.2f), 40f);
                    yield return View(tag, "gate-engine", new Vector3(0, 0.2f, 0.1f), new Vector3(2.4f, 0.1f, 0.3f), 34f);
                    yield return View(tag, "gate-rider", new Vector3(0, 2.2f, -0.3f), new Vector3(3.2f, 0.8f, 3.4f), 42f);
                    yield return View(tag, "gate-rider-back", new Vector3(0, 2.6f, -0.3f), new Vector3(-1.6f, 0.9f, -3.0f), 40f);
                    yield return View(tag, "gate-helmet", new Vector3(0, 3.6f, 0.1f), new Vector3(1.3f, 0.3f, 1.6f), 34f);
                    if (riders > 1) yield return View(tag, "gate-field", new Vector3(0, 0.6f, 0), new Vector3(2.5f, 3.2f, 9.0f), 55f);
                    overrideCam = false;
                    if (!lodChecked && Environment.GetEnvironmentVariable("REFLEX_HIRES_LODS") == "1") { lodChecked = true; yield return LodCheck(tag); }
                    yield return Frames(6);
                    yield return Measure(3f);
                    Log($"RESULT {tag} gate: {frameMs:0.00} ms / 1080p camera render");
                    for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 8f;) { Pad(false, 1f); yield return Seconds(0.25f); }
                    for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 4f;) { Pad(false, 0f, 1f); yield return Seconds(0.25f); }
                    Pad(false, 0f, 0f);
                    yield return Seconds(1.5f);
                    yield return Shot(tag + "-ridden-chase");
                    yield return View(tag, "ridden-right", new Vector3(0, 0.6f, 0), new Vector3(5.5f, 0.6f, 0.2f), 38f);
                    yield return View(tag, "ridden-hands", new Vector3(0, 1.9f, 0.4f), new Vector3(0.0f, 1.5f, 2.4f), 50f);
                    overrideCam = false;
                    yield return Frames(6);
                    yield return Measure(3f);
                    int oppTris2 = 0; if (opp) foreach (var o in opp.Riders) oppTris2 += Drawn(o.bike).triangles;
                    Log($"RESULT {tag} ridden: {frameMs:0.00} ms / 1080p camera render; other riders' bike triangles drawn now {oppTris2}, riders with a further LOD {(opp ? opp.LodRiders : 0)}");
                    OriginalPauseMenu.QuitToFrontEnd();
                    for (float t = 0; t < 60f && FindAnyObjectByType<OriginalWorldRuntime>(); t += Time.unscaledDeltaTime) yield return null;
                    yield return Seconds(2.5f);
                }
            }
            Log("DONE");
            Application.Quit(0);
        }

        // the local bike built as another rider's with BIKE DETAIL HIGH: ALL RIDERS (the twin + the race models' further LODs), stood 5 ft to the
        // right of the local bike, at each LOD: the far LODs must sit where the twin does
        IEnumerator LodCheck(string tag)
        {
            var spec = BikeMods.ForPlayer(rt.Roster?.Local?.bike);
            ReflexBikeModel b = null;
            try { b = ReflexBikeModel.Build(spec, highRes: true, lods: true, stand: false); }
            catch (Exception e) { Log("FAIL lodcheck build: " + e); }
            if (b == null) yield break;
            var holder = new GameObject("hires lod check (test only)");
            foreach (var g in new[] { b.chassis, b.frontWheel.gameObject, b.rearWheel.gameObject }) g.transform.SetParent(holder.transform, false);
            if (b.pose) b.pose.enabled = false;
            holder.transform.SetPositionAndRotation(rt.chassis.position + rt.chassis.right * 5f + Vector3.up * 0.2f, rt.chassis.rotation);
            Log($"LODCHECK {tag}: \"{b.chassis.name}\" has LODs {b.HasLods}, twin with race LODs {b.highResLods}: {b.LodState}");
            int n = 0;
            foreach (float d in new[] { 0f, 60f, 500f })
            {
                b.SelectLods(d, 1f, 0f);
                var drawn = Drawn(b);
                Log($"LODCHECK {tag} at {d:0} ft: {b.LodState}; {drawn.triangles} triangles in {drawn.renderers} renderers; far-own materials {(b.FarOwnMaterials != null ? b.FarOwnMaterials() : 0)}, LOD parts built {b.LodPartsBuilt}");
                yield return View(tag, "lodcheck-" + n, new Vector3(5f, 0.8f, 0), new Vector3(5.0f, 0.6f, 0.4f), 38f);
                yield return View(tag, "lodcheck-" + n + "-front", new Vector3(5f, 0.8f, 0), new Vector3(2.6f, 1.2f, 4.4f), 38f);
                n++;
            }
            overrideCam = false;
            b.Destroy(); Destroy(holder);
        }
    }
}
