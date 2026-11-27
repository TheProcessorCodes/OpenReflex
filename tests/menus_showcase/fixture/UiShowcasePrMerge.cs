// TEST ONLY: page "prmerge": the jersey name's 25 letters (CoreGarageProfile.SetRiderText, the race's plate: ReflexPlateText.FullName) and
// DISABLE COMPOUND (ReflexSettings.DisableCompound): START EVENT through the shell loads behind the full-screen loading screen, no test-ride
// arena is ever ridden or shown, and the race runs. REFLEX_PRMERGE_EVENT=<event key> (default National/NAT_Concord). "[prmerge] PASS|FAIL".
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.Options;
using Reflex.Port.OriginalWorld;
using UnityEngine;

namespace Reflex.Port.Showcase
{
    static class UiShowcasePrMerge
    {
        const string Tag = "[ui-showcase] [prmerge] ";
        const string Long = "LONGJERSEYNAME OVERFLOW X";   // 25
        static int fails;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { UiShowcase.Register("prmerge", Run); }

        static void Check(bool ok, string what) { Debug.Log(Tag + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

        static IEnumerator Run()
        {
            fails = 0;
            ReflexProfile.Disabled = false;
            if (!CoreGarageProfile.Open()) throw new InvalidOperationException(CoreGarageProfile.Error);
            ReflexProfile.Open();
            // ---- the jersey name
            Check(Long.Length == ReflexPlateRaster.MaxName, "the test name is " + ReflexPlateRaster.MaxName + " letters");
            Check(!CoreGarageProfile.SetRiderText("Name", Long + "Y"), "26 letters are refused: " + CoreGarageProfile.Error);
            Check(!CoreGarageProfile.SetRiderText("Name", "BAD-NAME"), "a hyphen is refused");
            Check(CoreGarageProfile.SetRiderText("Name", "abc   def"), "letters with several spaces are taken");
            ReflexProfile.Open();
            Check(ReflexProfile.Current?.T("PlayerRider")?.Str("Name", 0) == "ABC DEF", "... as ABC DEF: '" + ReflexProfile.Current?.T("PlayerRider")?.Str("Name", 0) + "'");
            Check(CoreGarageProfile.SetRiderText("Name", Long), "25 letters are taken");
            ReflexProfile.Open();
            Check(ReflexProfile.Current?.T("PlayerRider")?.Str("Name", 0) == Long, "the profile keeps all 25: '" + ReflexProfile.Current?.T("PlayerRider")?.Str("Name", 0) + "'");

            // ---- DISABLE COMPOUND
            var shell = OpenShell.Instance;
            string key = Environment.GetEnvironmentVariable("REFLEX_PRMERGE_EVENT");
            if (string.IsNullOrEmpty(key)) key = "National/NAT_Concord";
            var ev = OpenContent.Find(key);
            Check(ev != null && shell, "the event " + key + " and the shell");
            if (ev == null || !shell) yield break;
            Check(!ReflexSettings.DisableCompound, "DISABLE COMPOUND is off by default");
            ReflexSettings.DisableCompound = true;
            OpenArenaWarm.Release("prmerge test");
            Check(!OpenArenaWarm.Enabled, "no arena is kept warm with it on");
            yield return new WaitForSecondsRealtime(4f);
            Check(!OpenArenaWarm.Current, "no warm arena session after 4 s in the menus");
            var setup = new OpenSetup { eventKey = ev.key, ai = 0 };
            float t0 = Time.realtimeSinceStartup;
            typeof(OpenShell).GetMethod("StartEvent", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(shell, new object[] { ev, setup });
            bool arena = false, loading = false, shotLoading = false; OriginalWorldRuntime rt = null;
            while (Time.realtimeSinceStartup - t0 < 180f)
            {
                rt = UnityEngine.Object.FindAnyObjectByType<OriginalWorldRuntime>();
                var sh = OpenShell.Instance;
                if (OpenLoadArena.Riding || rt && rt.loadArena || sh && sh.LoadArenaPhase != null) arena = true;
                if (OpenLoading.Current) loading = true;
                if (loading && !shotLoading && Time.realtimeSinceStartup - t0 > 1.2f) { shotLoading = true; yield return UiShowcase.Shot("prmerge-1-loading"); }
                if (rt && rt.Running && rt.Roster != null) break;
                yield return null;
            }
            Check(rt && rt.Running, $"the race runs {Time.realtimeSinceStartup - t0:F1} s after START EVENT");
            Check(!arena, "no test-ride compound was ridden or shown");
            Check(loading, "the full-screen loading screen was shown");
            if (rt && rt.Roster != null)
            {
                var me = rt.Roster.entries.FirstOrDefault(e => e.Local);
                Check(me != null && me.plate != null && me.plate.name == Long, "the race's jersey name is the whole name: '" + me?.plate?.name + "'");
            }
            yield return new WaitForSecondsRealtime(5f);
            yield return UiShowcase.Shot("prmerge-2-gate");
            yield return new WaitForSecondsRealtime(22f);   // (the event's intro is over: the chase camera behind the rider, the jersey's name in view)
            yield return UiShowcase.Shot("prmerge-3-chase");
            OriginalPauseMenu.QuitToFrontEnd();
            for (float t = 0; t < 60f && UnityEngine.Object.FindAnyObjectByType<OriginalWorldRuntime>(); t += Time.unscaledDeltaTime) yield return null;
            yield return new WaitForSecondsRealtime(2.5f);
            Check(OpenShell.Instance, "back in the menus");
            ReflexSettings.DisableCompound = false;
            Debug.Log(Tag + (fails == 0 ? "RESULT PASS" : "RESULT FAIL (" + fails + ")"));
        }
    }
}
