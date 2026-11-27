// TEST ONLY (tests/menus_showcase fixtures): B.R.A.A.P. in a real race. REFLEX_BRAAP_RACE=<mode>[,<mode>] (national, supercross ... or an event key)
// with REFLEX_BRAAP_USE=<roster id> (Content/ReflexBraap.cs): starts each event through the shell's own launch with 11 computer riders, waits
// for the world's roster and checks that every computer rider is a rider of the roster in use (name, plate name, number), that nobody is in the
// field twice, and that the riders were built; REFLEX_BRAAP_SHOTS=<dir> takes pictures at the gate. Muted. "[braap-race] PASS|FAIL", "RESULT ...".
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.OriginalWorld;
using UnityEngine;

namespace Reflex.Port.Showcase
{
    public sealed class BraapRaceProbe : MonoBehaviour
    {
        static string Script => Environment.GetEnvironmentVariable("REFLEX_BRAAP_RACE");
        int fails; string shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (string.IsNullOrEmpty(Script)) return;
            OpenShell.SkipIntro = true;
            var g = new GameObject("braap race probe (test only)"); DontDestroyOnLoad(g); g.AddComponent<BraapRaceProbe>();
        }

        void Start() { Application.runInBackground = true; shots = Environment.GetEnvironmentVariable("REFLEX_BRAAP_SHOTS"); if (!string.IsNullOrEmpty(shots)) Directory.CreateDirectory(shots); StartCoroutine(Run()); }
        void LateUpdate() { if (AudioListener.volume != 0f) AudioListener.volume = 0f; }   // test players are muted

        void Log(string s) => Debug.Log("[braap-race] " + s);
        bool Check(bool ok, string what) { Log((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; return ok; }
        IEnumerator Seconds(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
        IEnumerator Shot(string name)
        {
            if (string.IsNullOrEmpty(shots)) yield break;
            string path = Path.Combine(shots, name + ".png");
            if (Application.isBatchMode) yield return UiShowcaseOffscreen.Shot(path);
            else { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(path); yield return null; yield return null; }
            Log("shot " + name);
        }

        IEnumerator Run()
        {
            OpenShell shell = null;
            for (float t = 0; t < 120f && !(shell = OpenShell.Instance); t += Time.unscaledDeltaTime) yield return null;
            if (!Check(shell, "the shell started")) { Quit(); yield break; }
            yield return Seconds(2f);
            var roster = ReflexBraap.Active;
            Check(!ReflexBraap.UsingDefault, "the roster in use: " + roster.name + " (" + roster.EnabledCount + " in the field)");
            foreach (var mode in Script.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0))
            {
                string key;
                try { key = ReflexEvents.All.FirstOrDefault(e => string.Equals(e.spec.type, mode, StringComparison.OrdinalIgnoreCase))?.key ?? mode; }
                catch (Exception e) { Check(false, mode + ": " + e.Message); continue; }
                Log("== " + mode + " (" + key + ")");
                ReflexEventSelect.Launch(key, 2, false, "Reflex", 0, null, riders: 12);
                OriginalWorldRuntime rt = null;
                for (float t = 0; t < 240f && !((rt = FindAnyObjectByType<OriginalWorldRuntime>()) && rt.Roster != null && rt.Roster.entries.Count > 1); t += Time.unscaledDeltaTime) yield return null;
                if (!Check(rt && rt.Roster != null, mode + ": the world's roster was read")) continue;
                var ai = rt.Roster.entries.Where(e => !e.Local).ToList();
                foreach (var e in rt.Roster.entries) Log("  slot " + e.slot.slot + (e.Local ? " (you) " : " ") + "'" + e.name + "' plate '" + e.plate?.name + "' #" + e.plate?.number + " rider " + (e.rider == null ? "none" : string.Join(",", e.rider.parts.Select(p => p.matsPkg))));
                Check(ReflexBraap.EventOn && ReflexBraap.EventRoster == roster.name, mode + ": the event took the roster");
                Check(ai.Count >= 5 && ai.All(e => roster.riders.Any(r => r.enabled && r.name == e.name && ReflexBraap.PlateName(r) == e.plate.name && r.number.ToString() == e.plate.number)),
                      mode + ": all " + ai.Count + " computer riders are riders of the roster (name, jersey name, number)");
                Check(ai.Select(e => e.name).Distinct().Count() == Math.Min(ai.Count, roster.EnabledCount), mode + ": nobody lines up twice");
                if (roster.draw == 1) Check(ai.Select(e => e.name).OrderBy(n => n).SequenceEqual(roster.riders.Where(r => r.enabled).Take(ai.Count).Select(r => r.name).OrderBy(n => n)), mode + ": FROM THE TOP: the roster's first " + ai.Count);
                Check(ai.All(e => e.rider != null && e.bike != null), mode + ": every rider has his gear and his bike");
                if (int.TryParse(Environment.GetEnvironmentVariable("REFLEX_BRAAP_SKILL"), out int sk) && sk >= 1 && sk <= 4)
                    Check(ai.All(e => e.aiTier == 4 - sk), mode + ": every computer rider asks for skill tier " + (4 - sk) + " (" + ReflexBraap.SkillNames[sk] + "); the core's answer is the log's AI TIERS line");
                var opp = FindAnyObjectByType<OriginalOpponents>();
                for (float t = 0; t < 120f && !(opp && opp.Built >= ai.Count); t += Time.unscaledDeltaTime) { if (!opp) opp = FindAnyObjectByType<OriginalOpponents>(); yield return null; }
                Check(opp && opp.Built == ai.Count, mode + ": " + (opp ? opp.Built : 0) + "/" + ai.Count + " riders built");
                for (float t = 0; t < 90f && !rt.Running; t += Time.unscaledDeltaTime) yield return null;
                yield return Seconds(4f);
                yield return Shot("braap-" + mode + "-gate");
                yield return Seconds(6f);
                yield return Shot("braap-" + mode + "-start");
                OriginalPauseMenu.QuitToFrontEnd();
                for (float t = 0; t < 60f && FindAnyObjectByType<OriginalWorldRuntime>(); t += Time.unscaledDeltaTime) yield return null;
                yield return Seconds(2.5f);
            }
            Log(fails == 0 ? "RESULT PASS" : "RESULT FAIL (" + fails + ")");
            Quit();
        }

        void Quit() { Application.Quit(fails == 0 ? 0 : 1); }
    }
}
