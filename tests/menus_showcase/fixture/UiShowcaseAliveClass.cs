// TEST ONLY: the Alive classes of their own (core/plan/ALIVE_BIKES.md "Class select") on the setup page's VEHICLE row, from the product's
// own code. Page "alive-class": the content folder (REFLEX_GAME_DIR) has the imported Alive vehicles (<content>/AliveBikes); for PRACTICE,
// RACE and FREE RIDE a National's setup (a Free Ride's on FREE RIDE) lists 50CC among its vehicles (OpenSetup.OwnTypes: the 50cc, the 650,
// the ATV 1000 where the event takes their base model's type), the row steps onto it, the setup's garage is a vehicle of the 50cc's base
// model (the Dakumoto 125, VehicleModelId 35) under the player's rider, RIDER is YOUR RIDER, and the shots show the row (alive-class-<mode>).
// Result lines "[ui-showcase] aliveclass ..."; "FAIL" lines through UiShowcaseNav.Check.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseAliveClass
    {
        const string Tag = "[ui-showcase] aliveclass ";
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => UiShowcase.Register("alive-class", Page);

        static OpenShell Shell => OpenShell.Instance;
        static VisualElement Q(string name) => Shell.Root.Q(name);
        static void Check(bool ok, string what) { UiShowcaseNav.Check(ok, "aliveclass", what); Debug.Log(Tag + (ok ? "ok   " : "FAIL ") + what); }
        static string Value(string row) => Q(row)?.Q<Label>(className: "mxa-value")?.text ?? "(no row)";

        static IEnumerator Page()
        {
            var own = OpenSetup.OwnTypes();
            Debug.Log(Tag + "own classes: " + string.Join(", ", own.Select(o => $"{o.name} (type {o.id}: base type {o.baseType}, model {o.model}, {o.modId})")));
            var fifty = own.FirstOrDefault(o => o.name == "50CC");
            Check(fifty != null, "the imported 50cc is a class of its own");
            if (fifty == null) yield break;
            foreach (var mode in new[] { OpenBrowse.Practice, OpenBrowse.Race, OpenBrowse.FreeRide })
            {
                Shell.ShowMain(); yield return UiShowcase.Frames(10);
                Shell.ShowBrowser(mode); yield return UiShowcase.Frames(40);
                var cards = Shell.CardEvents.Where(c => c.ev != null && c.mod == null).ToList();
                var ev = cards.FirstOrDefault(c => (mode == OpenBrowse.FreeRide || c.activity == "National") && OpenSetup.VehicleTypes(c.ev).Contains(fifty.id))
                         ?? cards.FirstOrDefault(c => OpenSetup.VehicleTypes(c.ev).Contains(fifty.id));
                Check(ev != null, $"{mode}: an event lists 50CC among its vehicles ({cards.Count} cards)");
                if (ev == null) continue;
                Shell.SelectBrowse(ev.activity); yield return UiShowcase.Frames(10);
                if (!Shell.SelectEvent(ev.key)) typeof(OpenShell).GetField("_browseEvent", All).SetValue(Shell, ev);
                yield return UiShowcase.Frames(10);
                Shell.ShowSetupPage(); yield return UiShowcase.Frames(40);
                var s = Shell.Setup; var e = s?.Event;
                Check(e != null, $"{mode}: {ev.key}'s setup is up");
                if (e == null) continue;
                var types = OpenSetup.VehicleTypes(e);
                Debug.Log(Tag + $"{mode} {ev.key}: VEHICLE choices " + string.Join(", ", types.Select(t => OpenContent.VehicleTypeName(t))));
                typeof(OpenShell).GetMethod("SetSetupTab", All)?.Invoke(Shell, new object[] { 1, false }); yield return UiShowcase.Frames(20);
                s.vehicleChoiceExplicit = true; s.vehicleType = fifty.id; s.vehicleClass = 0; s.rider = -1;
                typeof(OpenShell).GetMethod("SetupChanged", All).Invoke(Shell, new object[] { true }); yield return UiShowcase.Frames(30);
                var g = s.Garage(e);
                Check(s.Type(e) == fifty.id && Value("ou-setup-vehicle").Contains("50CC"), $"{mode}: VEHICLE shows 50CC ('{Value("ou-setup-vehicle")}')");
                Check(g != null && g.vehicleTypeId == fifty.baseType, $"{mode}: the ride is a vehicle of the base type {fifty.baseType} ({g})");
                var riders = OpenSetup.Riders(e, fifty.id);
                Check(riders.Count == 1 && riders[0].id == -1, $"{mode}: RIDER is YOUR RIDER only");
                yield return UiShowcase.Shot("alive-class-" + mode.ToString().ToLowerInvariant());
            }
            Shell.ShowMain(); yield return UiShowcase.Frames(5);
        }
    }
}
