// TEST ONLY: the garage vehicle list's filters (OpenShell.GarageFilters.cs). Page:
//   garage-filters   GARAGE > VEHICLE > MY RIDES: the classes (gf-classes), then the list under each filter: every vehicle (gf-all), MX1 (gf-mx1),
//                    MODS (gf-mods), ALIVE (gf-alive), an own class (gf-own: the first "own:" class, e.g. 50CC), MX2 + MODS (gf-mx2-mods), a pair
//                    that lists nothing (gf-empty: UTV + ALIVE), sorted by name (gf-name), the focus on a chip (gf-chip). REFLEX_GF_SCALE = the
//                    UI SCALE (100 .. 130), REFLEX_GF_TAG a suffix of the shots' names. Run with a client root that has bike mods and a content
//                    folder that has imported Alive bikes to see MODS / ALIVE filled.
// Checks (lines "[ui-showcase] gfilters ..."; "FAIL gfilters: ..."): every listed card passes the filter, the count line is "<cards> OF <all>",
// the chips' numbers add up, up from the first card reaches the filter bar and down from it comes back, the filters are saved keys.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseGarageFilters
    {
        const string Tag = "[ui-showcase] ";
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => UiShowcase.Register("garage-filters", Page);

        static void Log(string s) => Debug.Log(Tag + "gfilters " + s);
        static void Fail(string s) => Debug.LogError(Tag + "FAIL gfilters: " + s);
        static OpenShell Shell => OpenShell.Instance;
        static VisualElement El(string name) => Shell.Layer.Q<VisualElement>(name);
        static List<VisualElement> Cards() => El("garage-rows").Query<VisualElement>(className: "ou-gcard").ToList();
        static string TagName => Environment.GetEnvironmentVariable("REFLEX_GF_TAG") ?? "";

        static IEnumerator Set(string shot, string cls, int source, int sort = 0)
        {
            Shell.GarageSetFilters(cls, source, sort);
            yield return UiShowcase.Frames(10);
            var cards = Cards();
            string count = Shell.GarageFilterCount ?? "";
            Log($"{shot}: {Shell.GarageFilterState} title \"{Shell.GarageTitle}\" count \"{count}\" cards {cards.Count}: " + string.Join(", ", cards.Take(6).Select(c => c.name)));
            if (!count.StartsWith(cards.Count + " OF ")) Fail(shot + ": the count line \"" + count + "\" does not say " + cards.Count + " cards");
            if (source == OpenShell.GSourceOriginal && cards.Any(c => c.name.StartsWith("garage-mod-"))) Fail(shot + ": a mod card under ORIGINAL");
            if ((source == OpenShell.GSourceMods || source == OpenShell.GSourceAlive) && cards.Any(c => !c.name.StartsWith("garage-mod-"))) Fail(shot + ": an original card under MODS / ALIVE");
            if (cards.Count == 0 && El("garage-filter-empty") == null) Fail(shot + ": nothing listed and no empty state");
            if (cards.Count > 0 && El("garage-filter-empty") != null) Fail(shot + ": the empty state over a list");
            var first = cards.FirstOrDefault() ?? El("garage-filter-clear");
            if (Shell.Nav.Focused != first) Fail(shot + ": the focus is on " + (Shell.Nav.Focused?.name ?? "nothing") + ", not the list's first entry");
            if (first != null) Shell.Nav.Focus(first);
            yield return UiShowcase.Frames(110);
            yield return UiShowcase.Shot(shot + TagName);
        }

        static IEnumerator Page()
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("REFLEX_GF_SCALE"), out int scale)) ReflexSettings.UiScale = scale;
            yield return UiShowcaseGarage.Open(OpenShell.GarageVehicle, false);
            Log("opened: " + Shell.GarageCrumb + " > " + Shell.GarageTitle + ", filters " + Shell.GarageFilterState + ", classes " + string.Join(" ", Shell.GarageFilterClasses));
            var classes = Shell.GarageFilterClasses;

            // the classes level (one up), then back into the list
            typeof(OpenShell).GetMethod("GarageUp", All).Invoke(Shell, null);
            yield return UiShowcase.Frames(100);
            Log("classes level: " + string.Join(", ", Shell.Nav.Items().Where(e => e.name != null && e.name.StartsWith("garage-class-")).Select(e => e.name)));
            yield return UiShowcase.Shot("gf-classes" + TagName);
            var all = Shell.Nav.Items().FirstOrDefault(e => e.name == "garage-class-all");
            if (all == null) { Fail("no ALL VEHICLES card"); yield break; }
            (all.userData as OuNavItem)?.submit?.Invoke();
            yield return UiShowcase.Frames(60);

            yield return Set("gf-all", "", OpenShell.GSourceAll);
            // the chips' numbers: the sources add up to ALL, the classes to ALL
            int N(string chip) => int.TryParse(El(chip)?.Q<Label>(className: "ou-gfchip-n")?.text, out int n) ? n : -1;
            int total = N("garage-filter-source-all");
            if (N("garage-filter-source-original") + N("garage-filter-source-mods") + N("garage-filter-source-alive") != total) Fail("the source chips do not add up to " + total);
            int byClass = El("garage-filters").Query<VisualElement>(className: "ou-gfchip").ToList().Where(c => c.name.StartsWith("garage-filter-class-") && c.name != "garage-filter-class-all").Sum(c => N(c.name));
            if (byClass != total || N("garage-filter-class-all") != total) Fail("the class chips add up to " + byClass + ", not " + total);
            Log($"chips: all {total} = original {N("garage-filter-source-original")} + mods {N("garage-filter-source-mods")} + alive {N("garage-filter-source-alive")}; classes " +
                string.Join(" ", El("garage-filters").Query<VisualElement>(className: "ou-gfchip").ToList().Where(c => c.name.StartsWith("garage-filter-class-")).Select(c => c.name.Substring(20) + "=" + N(c.name))));

            // the pad's way: up from the first card is the filter bar, down from there comes back to the list
            var firstCard = Cards().First();
            var up = Shell.Nav.Neighbour(firstCard, new Vector2Int(0, 1));
            if (up == null || !El("garage-filters").Contains(up)) Fail("up from the first card is " + (up?.name ?? "nothing") + ", not the filter bar");
            else
            {
                var down = Shell.Nav.Neighbour(up, new Vector2Int(0, -1));
                if (down != firstCard) Fail("down from " + up.name + " is " + (down?.name ?? "nothing") + ", not the first card");
                var src = El("garage-filter-source-all");
                var right = Shell.Nav.Neighbour(src, new Vector2Int(1, 0));
                if (right?.name != "garage-filter-source-original") Fail("right of ALL is " + (right?.name ?? "nothing"));
                Log("nav: up from the first card -> " + up.name + ", down -> " + down?.name + ", right of source ALL -> " + right?.name);
            }

            yield return Set("gf-mx1", "type:3", OpenShell.GSourceAll);
            yield return Set("gf-mods", "", OpenShell.GSourceMods);
            yield return Set("gf-alive", "", OpenShell.GSourceAlive);
            string own = classes.FirstOrDefault(c => c.StartsWith("own:"));
            if (own != null) yield return Set("gf-own", own, OpenShell.GSourceAll);
            yield return Set("gf-mx2-mods", "type:2", OpenShell.GSourceMods);
            yield return Set("gf-original", "", OpenShell.GSourceOriginal);
            yield return Set("gf-empty", "type:11", OpenShell.GSourceAlive);
            if (Cards().Count != 0) Fail("UTV + ALIVE lists " + Cards().Count + " cards");
            // the empty state's button brings every vehicle back
            (El("garage-filter-clear")?.userData as OuNavItem)?.submit?.Invoke();
            yield return UiShowcase.Frames(20);
            if (Shell.GarageFilterState != "|0|0" || Cards().Count != total) Fail("SHOW ALL VEHICLES left " + Shell.GarageFilterState + " with " + Cards().Count + " cards");
            yield return Set("gf-name", "", OpenShell.GSourceAll, OpenShell.GSortName);

            // Y / F: the focus on the class chip that is on; the saved keys
            yield return Set("gf-mx2", "type:2", OpenShell.GSourceAll, OpenShell.GSortClass);
            typeof(OpenShell).GetMethod("GarageFilterJump", All).Invoke(Shell, null);
            yield return UiShowcase.Frames(30);
            if (Shell.Nav.Focused?.name != "garage-filter-class-type-2") Fail("the filter jump focused " + (Shell.Nav.Focused?.name ?? "nothing"));
            yield return UiShowcase.Shot("gf-chip" + TagName);
            typeof(OpenShell).GetMethod("GarageFilterJump", All).Invoke(Shell, null);
            yield return UiShowcase.Frames(10);
            if (Shell.Nav.Focused == null || !El("garage-rows").Contains(Shell.Nav.Focused)) Fail("the second filter jump did not come back to the list (" + (Shell.Nav.Focused?.name ?? "nothing") + ")");
            if (ReflexSave.GetString(OpenShell.GarageFilterClassKey, "") != "type:2" || ReflexSave.GetInt(OpenShell.GarageFilterSourceKey, -1) != 0) Fail("the filters were not saved");
            // left and come back: the list opens as it was left
            Shell.ShowMain(); yield return UiShowcase.Frames(20);
            Shell.ShowGarage(OpenShell.GarageVehicle); yield return UiShowcase.Frames(120);
            if (Shell.GarageFilterState != "type:2|0|0" || Shell.GarageTitle != "MX2") Fail("the garage came back with " + Shell.GarageFilterState + " \"" + Shell.GarageTitle + "\"");
            Log("came back: " + Shell.GarageFilterState + " \"" + Shell.GarageTitle + "\" " + Shell.GarageFilterCount);
            Shell.GarageSetFilters("", OpenShell.GSourceAll, OpenShell.GSortClass);
            Log("done");
            Shell.ShowMain(); yield return UiShowcase.Frames(5);
        }
    }
}
