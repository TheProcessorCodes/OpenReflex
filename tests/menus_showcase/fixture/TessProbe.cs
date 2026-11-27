// TEST ONLY (tests/menus_showcase fixtures): TERRAIN TESSELLATION before / after. REFLEX_TESS_PROBE=<event key or type>@<riders>[@<seconds>][,...]:
// starts each event through the shell's own launch (AI on, so the field ruts the ground), skips the intro on a virtual pad, rides for
// <seconds> (default 18), brakes to a stop, and then saves the same view with the option off and on ("<key>-off.png" / "<key>-on.png" into
// REFLEX_TESS_SHOTS) with the mean frame time of each (3 s). Muted. "[tess-probe] ..." lines; quits at the end.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.OriginalWorld;
using Reflex.Port.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Reflex.Port.Showcase
{
    public sealed class TessProbe : MonoBehaviour
    {
        static string Script => Environment.GetEnvironmentVariable("REFLEX_TESS_PROBE");
        Gamepad pad; string shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (string.IsNullOrEmpty(Script)) return;
            OpenShell.SkipIntro = true;
            var g = new GameObject("tess probe (test only)"); DontDestroyOnLoad(g); g.AddComponent<TessProbe>();
        }

        void Start()
        {
            Application.runInBackground = true; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            shots = Environment.GetEnvironmentVariable("REFLEX_TESS_SHOTS"); if (!string.IsNullOrEmpty(shots)) Directory.CreateDirectory(shots);
            StartCoroutine(Run());
        }
        void LateUpdate() { if (AudioListener.volume != 0f) AudioListener.volume = 0f; }   // test players are muted
        void Log(string s) => Debug.Log("[tess-probe] " + s);
        IEnumerator Seconds(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
        void Pad(bool a, float throttle, float brake = 0f)
        {
            if (pad == null || !pad.added) pad = InputSystem.AddDevice<Gamepad>("tess probe pad");
            var st = new GamepadState { rightTrigger = throttle, leftTrigger = brake };
            if (a) st = st.WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(pad, st);
        }
        IEnumerator Shot(string name)
        {
            if (string.IsNullOrEmpty(shots)) yield break;
            string path = Path.Combine(shots, name.Replace('/', '_') + ".png");
            if (Application.isBatchMode) yield return UiShowcaseOffscreen.Shot(path);
            else { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(path); yield return null; yield return null; }
            Log("shot " + name);
        }
        float frameMs;
        // (a batch player presents no frames: the game camera is rendered into a target here, and a 1 x 1 read-back waits for the GPU)
        IEnumerator Measure(float seconds)
        {
            var cam = Camera.main; frameMs = 0f;
            if (!cam) yield break;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var prev = cam.targetTexture; int n = 0; double total = 0;
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < seconds;)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                cam.targetTexture = rt;
                for (int k = 0; k < 10; k++) cam.Render();
                cam.targetTexture = prev;
                RenderTexture.active = rt; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;
                total += sw.Elapsed.TotalMilliseconds; n += 10;
                yield return null;
            }
            frameMs = (float)(total / Mathf.Max(1, n));
            rt.Release(); Destroy(rt); Destroy(one);
        }

        IEnumerator Run()
        {
            OpenShell shell = null;
            for (float t = 0; t < 120f && !(shell = OpenShell.Instance); t += Time.unscaledDeltaTime) yield return null;
            yield return Seconds(2f);
            foreach (var item in Script.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0))
            {
                var f = item.Split('@'); string mode = f[0]; int riders = f.Length > 1 ? int.Parse(f[1]) : 12; float ride = f.Length > 2 ? float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture) : 18f;
                string key = ReflexEvents.All.FirstOrDefault(e => string.Equals(e.spec.type, mode, StringComparison.OrdinalIgnoreCase))?.key ?? mode;
                Log("== " + key + " riders " + riders);
                ReflexTerrainClipmap.Force = false;
                ReflexEventSelect.Launch(key, riders <= 1 ? 0 : 2, false, "Reflex", 0, null, riders: riders);
                OriginalWorldRuntime rt = null;
                for (float t = 0; t < 240f && !((rt = FindAnyObjectByType<OriginalWorldRuntime>()) && rt.Running && rt.Roster != null); t += Time.unscaledDeltaTime) yield return null;
                if (!rt) { Log("FAIL no world for " + key); continue; }
                yield return Seconds(3f);
                for (int i = 0; i < 4; i++) { Pad(true, 0); yield return Seconds(0.1f); Pad(false, 0); yield return Seconds(0.6f); }   // SKIP INTRO
                for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < ride;) { Pad(false, 1f); yield return Seconds(0.25f); }
                for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 5f;) { Pad(false, 0f, 1f); yield return Seconds(0.25f); }
                Pad(false, 0f, 0f);
                yield return Seconds(2f);
                var cam = Camera.main; var terrain = FindAnyObjectByType<OriginalTerrain>();
                Log(key + ": camera " + (cam ? cam.transform.position.ToString("0.00") : "none") + " spacing " + (terrain ? terrain.Info.spacing : 0f) + " samples " + (terrain ? terrain.Samples : 0));
                for (int pass = 0; pass < 4; pass++)
                {
                    bool on = (pass & 1) == 1;
                    ReflexTerrainClipmap.Force = on;
                    yield return Seconds(0.5f);
                    if (pass < 2) yield return Shot(key + (on ? "-on" : "-off"));
                    yield return Measure(3f);
                    Log($"RESULT {key} tessellation {(on ? "ON" : "OFF")}: {frameMs:0.00} ms / 1080p camera render; clipmap levels {ReflexTerrainClipmap.LastLevels} vertices {ReflexTerrainClipmap.LastVertices} triangles {ReflexTerrainClipmap.LastTriangles}; camera {(cam ? cam.transform.position.ToString("0.000") : "none")}");
                }
                ReflexTerrainClipmap.Force = false;
                OriginalPauseMenu.QuitToFrontEnd();
                for (float t = 0; t < 60f && FindAnyObjectByType<OriginalWorldRuntime>(); t += Time.unscaledDeltaTime) yield return null;
                yield return Seconds(2.5f);
            }
            Application.Quit(0);
        }
    }
}
