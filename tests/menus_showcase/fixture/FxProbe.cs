// TEST ONLY (tests/menus_showcase fixtures): the bikes' particles in a real race. REFLEX_FX_PROBE=<event key or type>@<riders>[,...]: starts each
// event through the shell's own launch (AI on), skips the intro on a virtual pad, holds the throttle, and logs the particle renderer's counts
// (OriginalParticles: live / normal / additive, the atlas) once a second for 40 s. Muted. "[fx-probe] ..." lines; quits at the end.
using System;
using System.Collections;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.OriginalWorld;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Reflex.Port.Showcase
{
    public sealed class FxProbe : MonoBehaviour
    {
        static string Script => Environment.GetEnvironmentVariable("REFLEX_FX_PROBE");
        Gamepad pad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (string.IsNullOrEmpty(Script)) return;
            OpenShell.SkipIntro = true;
            var g = new GameObject("fx probe (test only)"); DontDestroyOnLoad(g); g.AddComponent<FxProbe>();
        }

        void Start() { Application.runInBackground = true; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; StartCoroutine(Run()); }
        void LateUpdate() { if (AudioListener.volume != 0f) AudioListener.volume = 0f; }   // test players are muted
        void Log(string s) => Debug.Log("[fx-probe] " + s);
        IEnumerator Seconds(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
        void Pad(bool a, float throttle)
        {
            if (pad == null || !pad.added) pad = InputSystem.AddDevice<Gamepad>("fx probe pad");
            var st = new GamepadState { rightTrigger = throttle };
            if (a) st = st.WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(pad, st);
        }

        IEnumerator Run()
        {
            OpenShell shell = null;
            for (float t = 0; t < 120f && !(shell = OpenShell.Instance); t += Time.unscaledDeltaTime) yield return null;
            yield return Seconds(2f);
            foreach (var item in Script.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0))
            {
                var f = item.Split('@'); string mode = f[0]; int riders = f.Length > 1 ? int.Parse(f[1]) : 12;
                string key = ReflexEvents.All.FirstOrDefault(e => string.Equals(e.spec.type, mode, StringComparison.OrdinalIgnoreCase))?.key ?? mode;
                Log("== " + key + " riders " + riders + " roster " + ReflexBraap.Active.name);
                ReflexEventSelect.Launch(key, riders <= 1 ? 0 : 2, false, "Reflex", 0, null, riders: riders);
                OriginalWorldRuntime rt = null;
                for (float t = 0; t < 240f && !((rt = FindAnyObjectByType<OriginalWorldRuntime>()) && rt.Running && rt.Roster != null); t += Time.unscaledDeltaTime) yield return null;
                if (!rt) { Log("FAIL no world for " + key); continue; }
                yield return Seconds(3f);
                for (int i = 0; i < 4; i++) { Pad(true, 0); yield return Seconds(0.1f); Pad(false, 0); yield return Seconds(0.6f); }   // SKIP INTRO
                int maxLive = 0, maxDrawn = 0; Vector3 lastBike = Vector3.zero;
                for (int s = 0; s < 40; s++)
                {
                    Pad(false, 1f);
                    yield return Seconds(1f);
                    var p = FindAnyObjectByType<OriginalParticles>();
                    if (!p) { Log(key + " t" + s + ": no OriginalParticles component"); continue; }
                    maxLive = Math.Max(maxLive, p.Live); maxDrawn = Math.Max(maxDrawn, p.Normal + p.Additive);
                    if (s % 4 == 3)
                    {
                        var cam = Camera.main ? Camera.main.transform.position : Vector3.zero; int n8 = 0, n25 = 0, nb = 0;
                        Vector3 bike = rt.rider && rt.rider.bones != null && rt.rider.bones.Length > 0 && rt.rider.bones[0] ? rt.rider.bones[0].position : cam;
                        float moved = (bike - lastBike).magnitude; lastBike = bike;
                        var hist = new System.Collections.Generic.Dictionary<string, int>();
                        for (int i = 0; i < p.LastCount && p.LastParticles != null; i++)
                        {
                            var q = p.LastParticles[i]; float d = (new Vector3(q.px, q.py, q.pz) - cam).magnitude;
                            if (d < 8f) n8++; if (d < 25f) n25++; if ((new Vector3(q.px, q.py, q.pz) - bike).magnitude < 8f) nb++;
                            string k = q.uOffset.ToString("0.00") + "/" + q.vOffset.ToString("0.00") + "x" + q.uScale.ToString("0.00"); hist[k] = hist.TryGetValue(k, out int c) ? c + 1 : 1;
                        }
                        int b100 = 0, b300 = 0, b1000 = 0, bfar = 0; Vector3 farSum = Vector3.zero;
                        for (int i = 0; i < p.LastCount && p.LastParticles != null; i++)
                        {
                            var q = p.LastParticles[i]; var at = new Vector3(q.px, q.py, q.pz); float d = (at - bike).magnitude;
                            if (d < 100f) b100++; else if (d < 300f) b300++; else if (d < 1000f) b1000++; else { bfar++; farSum += at; }
                        }
                        Log($"{key} t{s + 1}: by distance from the rider: <100 ft {b100}, <300 {b300}, <1000 {b1000}, beyond {bfar}" + (bfar > 0 ? $" (centred {(farSum / bfar - bike).magnitude:0} ft away at {farSum / bfar:0}); the rider is at {bike:0}" : ""));
                        Log($"{key} t{s + 1}: within 8 ft of the rider {nb} (he moved {moved:0} ft in 4 s; camera {(cam - bike).magnitude:0} ft away), of the camera {n8}, within 25 {n25}; frames " + string.Join(" ", hist.OrderByDescending(x => x.Value).Take(6).Select(x => x.Key + ":" + x.Value)));
                    }
                    if (s % 4 == 3) Log($"{key} t{s + 1}: live {p.Live} normal {p.Normal} additive {p.Additive} count {p.LastCount} atlas '{p.TextureName}' visible {p.visible} enabled {p.isActiveAndEnabled} race_state {rt.EventUi.race_state}");
                }
                Log($"RESULT {key} riders {riders}: max live {maxLive}, max drawn {maxDrawn}");
                Pad(false, 0);
                OriginalPauseMenu.QuitToFrontEnd();
                for (float t = 0; t < 60f && FindAnyObjectByType<OriginalWorldRuntime>(); t += Time.unscaledDeltaTime) yield return null;
                yield return Seconds(2.5f);
            }
            Application.Quit(0);
        }
    }
}
