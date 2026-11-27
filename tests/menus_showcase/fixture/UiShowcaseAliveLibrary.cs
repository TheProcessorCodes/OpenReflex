// TEST ONLY: the MX vs ATV Alive import screens of GAME LIBRARY (OpenShell.AliveImport.cs). Page:
//   alive-library   REFLEX_ALIVE_SOURCES = "<disc image>;<5451087B folder>". Shots: the library card (al-card), the sources screen with
//                   nothing added (al-sources-empty), with the disc alone (al-sources-disc) and with both (al-sources-both); the content
//                   list after a scan of the disc alone (al-list-disc: the missing-source banner) and of both (al-list-both, scrolled:
//                   al-list-both-gear, al-list-both-levels). Nothing is imported.
// Checks ("FAIL alib: ..."): each source lands in its slot; the disc alone finds fewer vehicles than both and shows the banner, both show
// none; what is ticked is exactly what is not installed and up to date; the IMPORT button says what is ticked.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Reflex.Port.Content;
using Reflex.Port.Content.AliveImport;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseAliveLibrary
    {
        const string Tag = "[ui-showcase] ";
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => UiShowcase.Register("alive-library", Page);

        static void Log(string s) => Debug.Log(Tag + "alib " + s);
        static void Fail(string s) => Debug.LogError(Tag + "FAIL alib: " + s);
        static OpenShell Shell => OpenShell.Instance;
        static IEnumerator Wait(int n) { float until = Time.realtimeSinceStartup + n / 60f; for (int i = 0; i < n || Time.realtimeSinceStartup < until; i++) yield return null; }
        static T Get<T>(string f) => (T)typeof(OpenShell).GetField(f, All).GetValue(Shell);
        static void Call(string m) => typeof(OpenShell).GetMethod(m, All).Invoke(Shell, null);
        static VisualElement El(string name) => Shell.Layer.Q<VisualElement>(name);
        static string TextOf(string name) => El(name) is Label l ? l.text : El(name)?.Q<Label>()?.text;

        static IEnumerator Scan()
        {
            Call("BeginAliveScan");
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 300f && Get<System.Threading.Tasks.Task>("_aliveTask") != null;) yield return null;
            yield return Wait(40);
        }

        static IEnumerator Page()
        {
            string[] src = (Environment.GetEnvironmentVariable("REFLEX_ALIVE_SOURCES") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (src.Length < 2) { Fail("REFLEX_ALIVE_SOURCES needs the disc image and the 5451087B folder"); yield break; }
            string disc = src[0], downloads = src[1];
            Shell.ShowLibrary(false); yield return Wait(60);
            var card = El("ou-library-alive-import");
            if (card != null) { Shell.Nav.Focus(card); yield return Wait(30); }
            Log("library card: " + TextOf("ou-library-alive-state") + " | " + (TextOf("ou-library-alive-update") ?? "(no update line)"));
            yield return UiShowcase.Shot("al-card");

            var paths = Get<List<string>>("_aliveSourcePaths");
            paths.Clear(); Shell.ShowAliveSources(); yield return Wait(40);
            if (El("ou-alive-find").enabledSelf) Fail("FIND CONTENT is on with no source");
            yield return UiShowcase.Shot("al-sources-empty");
            paths.Add(disc); Shell.ShowAliveSources(); yield return Wait(40);
            Log("disc alone: slot 1 \"" + TextOf("ou-alive-slot-state-disc") + "\", slot 2 \"" + TextOf("ou-alive-slot-state-downloads") + "\"");
            if (!(TextOf("ou-alive-slot-state-disc") ?? "").Contains("ADDED  ·") || (TextOf("ou-alive-slot-state-downloads") ?? "") != "NOT ADDED YET") Fail("the disc is not in the disc's slot alone");
            yield return UiShowcase.Shot("al-sources-disc");
            yield return Scan();
            var found = Get<AliveSources>("_aliveFound");
            if (found == null) { Fail("the scan of the disc found nothing"); yield break; }
            int discBikes = found.bikes.Count;
            Log($"disc alone: {found.bikes.Count} vehicles, {found.levels.Count} levels, {found.gear.Count} gear models; banner \"{El("ou-alive-missing")?.Q<Label>()?.text}\"; IMPORT \"{El("ou-alive-import")?.Q<Label>()?.text}\"");
            if (El("ou-alive-missing") == null || El("ou-alive-add-missing") == null) Fail("no banner for the missing downloaded content");
            yield return UiShowcase.Shot("al-list-disc");

            paths.Clear(); paths.Add(disc); paths.Add(downloads); Shell.ShowAliveSources(); yield return Wait(40);
            if (!(TextOf("ou-alive-slot-state-downloads") ?? "").Contains("ADDED  ·")) Fail("the downloaded content is not in its slot");
            yield return UiShowcase.Shot("al-sources-both");
            yield return Scan();
            found = Get<AliveSources>("_aliveFound");
            if (found == null) { Fail("the scan of both found nothing"); yield break; }
            var chosen = Get<HashSet<string>>("_aliveChosen");
            string content = ReflexInstall.Dir;
            bool valid = AliveImporter.ContentProblem(content) == null;
            var want = found.bikes.Where(b => b.Importable && (!valid || AliveBikes.ImportState(content, b) != 1)).Select(b => "bike:" + b.aliveId).ToList();
            var ticked = chosen.Where(k => k.StartsWith("bike:", StringComparison.Ordinal)).ToList();
            Log($"both: {found.bikes.Count} vehicles (the disc alone {discBikes}), {found.levels.Count} levels, {found.gear.Count} gear models; states new {found.bikes.Count(b => !valid || AliveBikes.ImportState(content, b) == 0)}, update {found.bikes.Count(b => valid && AliveBikes.ImportState(content, b) == 2)}, up to date {found.bikes.Count(b => valid && AliveBikes.ImportState(content, b) == 1)}; ticked {ticked.Count} vehicles, gear {chosen.Contains("gear:all")}, levels {chosen.Count(k => !k.Contains(":"))}; IMPORT \"{El("ou-alive-import")?.Q<Label>()?.text}\"");
            if (found.bikes.Count <= discBikes) Fail("both sources find no more vehicles than the disc alone");
            if (El("ou-alive-missing") != null) Fail("the missing-source banner shows with both sources added");
            if (ticked.Count != want.Count || ticked.Except(want).Any()) Fail($"ticked vehicles {ticked.Count}, expected the {want.Count} that are new or out of date");
            yield return UiShowcase.Shot("al-list-both");
            var gear = El("ou-alive-gear"); if (gear != null) { Shell.Nav.Focus(gear); (gear.parent?.parent as ScrollView ?? gear.GetFirstAncestorOfType<ScrollView>())?.ScrollTo(gear); yield return Wait(30); yield return UiShowcase.Shot("al-list-both-gear"); }
            var level = Shell.Layer.Query<VisualElement>().ToList().LastOrDefault(e => e.name != null && e.name.StartsWith("ou-alive-level-"));
            if (level != null) { Shell.Nav.Focus(level); level.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(level); yield return Wait(30); yield return UiShowcase.Shot("al-list-both-levels"); }
            Log("done");
            Shell.ShowMain(); yield return Wait(5);
        }
    }
}
