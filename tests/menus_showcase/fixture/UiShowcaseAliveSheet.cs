// TEST ONLY: a contact sheet of the imported MX vs ATV Alive vehicles in the garage. Page:
//   alive-sheet   every Alive bike of <game>/AliveBikes (REFLEX_AS_ONLY: a comma list of slug substrings) is ridden in GARAGE > VEHICLE and
//                 shot from one angle with its stock kit (as-<slug>-stock) and with REFLEX_AS_KITS (2) of its graphic kits worn (the first
//                 and the last: as-<slug>-kit1 / -kit2); the bikes of REFLEX_AS_CLOSE (slug substrings) are also shot close on the
//                 handlebars and cables, the exhaust, the rear wheel and the front wheel (as-<slug>-close-*). tests/alive_bikes/sheet.py
//                 lays the shots out. The log says per bike whether the garage draws its high-res twin (RxModBike.hr).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseAliveSheet
    {
        const string Tag = "[ui-showcase] ";
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => UiShowcase.Register("alive-sheet", Page);

        static void Log(string s) => Debug.Log(Tag + "asheet " + s);
        static void Fail(string s) => Debug.LogError(Tag + "FAIL asheet: " + s);
        static OpenShell Shell => OpenShell.Instance;
        static IEnumerator Wait(int n) { float until = Time.realtimeSinceStartup + n / 60f; for (int i = 0; i < n || Time.realtimeSinceStartup < until; i++) yield return null; }
        static object Stage => typeof(OpenShell).GetField("garageStage", All).GetValue(Shell);
        static void Angles(float yaw, float pitch) => Stage?.GetType().GetMethod("SetAngles").Invoke(Stage, new object[] { yaw, pitch });
        static void Zoom(float amount) => Stage?.GetType().GetMethod("Zoom").Invoke(Stage, new object[] { amount });

        static IEnumerator Show(float yaw, float pitch, int settle)
        {
            Shell.ShowMain(); yield return Wait(5);
            Shell.ShowGarage(OpenShell.GarageParts);   // (CUSTOMIZE VEHICLE: the stage holds the equipped vehicle; the vehicle list would preview its first card)
            yield return Wait(settle);
            Angles(yaw, pitch); yield return Wait(50);
        }

        static IEnumerator Page()
        {
            yield return UiShowcaseGarage.Open(OpenShell.GarageVehicle, true);
            string[] only = (Environment.GetEnvironmentVariable("REFLEX_AS_ONLY") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string[] close = (Environment.GetEnvironmentVariable("REFLEX_AS_CLOSE") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int kits = int.TryParse(Environment.GetEnvironmentVariable("REFLEX_AS_KITS"), out int kv) ? kv : 2;
            var mods = BikeMods.All.Where(m => m.rx != null && m.rx.type == "bikes" && m.id.StartsWith("AliveBikes/", StringComparison.Ordinal)).OrderBy(m => m.id, StringComparer.Ordinal).ToList();
            Log($"{mods.Count} Alive bikes installed");
            if (mods.Count == 0) { Fail("no imported Alive bike (<game>/AliveBikes)"); yield break; }
            if (Environment.GetEnvironmentVariable("REFLEX_AS_EXPECT_OUTDATED") != null)
            {   // the vehicle list (its RE-IMPORT pills on the bikes of an older import)
                Shell.ShowMain(); yield return Wait(5);
                Shell.ShowGarage(OpenShell.GarageVehicle); yield return Wait(150);
                int pills = Shell.Layer.Query<UnityEngine.UIElements.Label>().ToList().Count(l => l.text == "RE-IMPORT");
                Log($"the vehicle list shows {pills} RE-IMPORT pills; {mods.Count(m => m.rx.bikes.Any(b => b.importOutdated))} of {mods.Count} Alive bikes are of an older import");
                yield return UiShowcase.Shot("as-list");
            }
            foreach (var m in mods)
            {
                string slug = m.id.Substring("AliveBikes/".Length);
                if (only.Length > 0 && !only.Any(o => slug.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                int model = m.rx.bikes[0].vehicleModelId;
                int pv = OpenGarageData.PlayerVehicleOfModel(model);
                if (pv < 0) { Fail(slug + ": model " + model + " is not owned in the unlocked profile"); continue; }
                if (!OpenGarageData.SelectVehicle(pv)) { Fail(slug + ": select vehicle " + pv); continue; }
                // REFLEX_AS_WEAR_PACK=1: a community kit pack's bike piece (KitPacks, Mods/Gear of the client root) worn on the ORIGINAL bike of
                // this model first, as a player who customised that bike before riding the Alive one (bikes without a fitting pack are skipped)
                if (Environment.GetEnvironmentVariable("REFLEX_AS_WEAR_PACK") == "1")
                {
                    string want = Environment.GetEnvironmentVariable("REFLEX_AS_PACK_NAME") ?? "";   // (a part of the pack's name: that pack first)
                    var pack = KitPacks.Current.byId.Values.Where(i => i.bike && !i.hidden && i.models.Contains(model) && !i.pack.builtIn).OrderBy(i => want.Length > 0 && i.pack.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1).ThenBy(i => i.type == 11 ? 0 : 1).FirstOrDefault();
                    if (pack == null) { Log(slug + ": no kit pack piece fits model " + model + " (skipped)"); continue; }
                    KitPacks.SetPart(model, pack.type, pack);
                    Log($"{slug}: the original bike of model {model} wears the pack piece '{pack.title}' ({pack.pack.name}, part type {pack.type})");
                }
                BikeMods.SetChoice(model, m.id);
                ReflexProfile.Open();
                var bike = BikeMods.RxBikeFor(model);
                if (bike == null) { Fail(slug + ": not the bike of model " + model); continue; }
                foreach (var s in bike.slots) if (!(RxParts.Chosen(bike, s)?.isDefault ?? true)) RxParts.Set(bike, s.Default);
                var spec = OpenGarageData.BikeOfModel(model);
                var garageSpec = ReflexBikeModel.HighRes(spec);
                Log($"{slug}: rides as model {model}; slots {string.Join(", ", bike.slots.Select(s => s.name + " " + s.options.Count))}; high-res twin {(bike.hr != null ? "yes: the garage draws " + (garageSpec?.rx == bike.hr ? "the twin" : "THE RACE MODEL") : "none")}");
                if (bike.hr != null && garageSpec?.rx != bike.hr) Fail(slug + ": the garage does not draw the bike's high-res twin");
                // REFLEX_AS_EXPECT_OUTDATED=1 (a content folder of an older import): a bike with parts is flagged, the CUSTOMIZE page says so
                bool wantOld = Environment.GetEnvironmentVariable("REFLEX_AS_EXPECT_OUTDATED") == "1";
                Log($"{slug}: import {(bike.importOutdated ? "OUT OF DATE" : "current")}");
                if (bike.slots.Count > 0 && bike.importOutdated != wantOld) Fail(slug + ": import flagged " + (bike.importOutdated ? "out of date" : "current") + ", expected the other");
                yield return Show(-40f, 12f, 200);
                if (bike.importOutdated != (Shell.Layer.Q<UnityEngine.UIElements.VisualElement>("garage-reimport-note") != null)) Fail(slug + ": the CUSTOMIZE page's re-import note does not match the flag");
                yield return UiShowcase.Shot("as-" + slug + "-stock");
                if (close.Any(o => slug.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    foreach (var (name, yaw, pitch, zoom) in new[] { ("bars", -25f, 38f, 1.2f), ("exhaust", 115f, 14f, 1.2f), ("rear", 90f, 6f, 1.2f), ("front", -70f, 8f, 1.2f) })
                    {
                        yield return Show(yaw, pitch, 120);
                        Zoom(zoom); yield return Wait(90);
                        yield return UiShowcase.Shot("as-" + slug + "-close-" + name);
                    }
                }
                var kit = bike.slots.FirstOrDefault(s => string.Equals(s.id, "GraphicKit", StringComparison.OrdinalIgnoreCase));
                var options = kit?.options.Where(o => !o.isDefault).ToList() ?? new List<RxPartOption>();
                var picks = new List<RxPartOption>();
                // REFLEX_AS_KIT_NAMES (a comma list of name parts, "Delta,Rockstar"): those kits instead of the first and the last, each also
                // shot from the rear left, the left and the front right (as-<slug>-kit<n>-rear / -left / -front)
                string[] named = (Environment.GetEnvironmentVariable("REFLEX_AS_KIT_NAMES") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (named.Length > 0) picks.AddRange(named.Select(n => options.FirstOrDefault(o => o.Title.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)).Where(o => o != null));
                else
                {
                    if (kits >= 1 && options.Count > 0) picks.Add(options[0]);
                    if (kits >= 2 && options.Count > 1) picks.Add(options[options.Count - 1]);
                }
                // (the mods may be scanned again while the garage opens: the bike and its options are looked up again by id before each choice,
                // a stale bike would save its choice under another key)
                RxModBike Now() => BikeMods.RxBikeFor(model);
                RxPartOption Option(string slotId, string id) => Now()?.slots.FirstOrDefault(s => s.id == slotId)?.options.FirstOrDefault(o => o.id == id);
                for (int k = 0; k < picks.Count; k++)
                {
                    bike = Now(); picks[k] = Option("GraphicKit", picks[k].id) ?? picks[k];
                    RxParts.Set(bike, picks[k]);
                    var worn = OpenGarageData.BikeOfModel(model);
                    var hs = ReflexBikeModel.HighRes(worn); bike = Now();
                    Log($"{slug}: kit{k + 1} = {picks[k].Title} ({picks[k].id}); garage: {(hs?.rx == bike.hr && bike.hr != null ? "the twin with " + string.Join(", ", hs.rxParts.Select(o => o.Title)) : "the race model")}");
                    if (bike.hr != null && hs?.rx != bike.hr)
                    {
                        var hsl = bike.hr.slots.FirstOrDefault(s => string.Equals(s.id, picks[k].slot.id, StringComparison.OrdinalIgnoreCase));
                        Fail($"{slug}: kit{k + 1}: the garage draws the race model: worn [{string.Join(", ", worn?.rxParts?.Select(o => o.slot.id + "/" + o.id) ?? new string[0])}], skin {(worn?.rxSkin != null)}, twin slots [{string.Join(", ", bike.hr.slots.Select(s => s.id + ":" + s.options.Count))}], twin has the option {(hsl?.Find(picks[k].id) != null)} (ids {string.Join(",", hsl?.options.Take(6).Select(o => o.id) ?? new string[0])})");
                    }
                    yield return Show(-40f, 12f, 200);
                    yield return UiShowcase.Shot("as-" + slug + "-kit" + (k + 1));
                    if (named.Length > 0)
                        foreach (var (name, yaw, pitch) in new[] { ("rear", 150f, 14f), ("left", 90f, 8f), ("front", -140f, 14f), ("top", -90f, 55f) })
                        { Angles(yaw, pitch); yield return Wait(60); yield return UiShowcase.Shot("as-" + slug + "-kit" + (k + 1) + "-" + name); }
                }
                bike = Now(); kit = bike.slots.FirstOrDefault(s => string.Equals(s.id, "GraphicKit", StringComparison.OrdinalIgnoreCase));
                if (kit != null) RxParts.Set(bike, kit.Default);
                // the colour slots (REFLEX_AS_COLORS=1, the bikes of REFLEX_AS_CLOSE): a number plate colour, a rim, hub, frame, swing arm and
                // A-arm colour worn at once (as-<slug>-colors, -colors-rear), then the defaults again
                if (Environment.GetEnvironmentVariable("REFLEX_AS_COLORS") == "1" && close.Any(o => slug.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var colours = bike.slots.Where(s => s.color != null).ToList();
                    Log($"{slug}: colour slots: " + string.Join(", ", colours.Select(s => s.name + " (" + s.color + ") " + s.options.Count)));
                    if (colours.Count == 0 && bike.slots.Count > 0) Fail(slug + ": no colour slot");
                    int pick = 5;
                    foreach (var s in colours)
                    {
                        var opts = s.options.Where(o => o.rgb != null).ToList(); if (opts.Count == 0) continue;
                        var o = opts[Math.Min(opts.Count - 1, s.color == "DynNumPlate" ? 0 : pick)]; pick += 4;
                        RxParts.Set(Now(), Option(s.id, o.id) ?? o);
                        Log($"{slug}: {s.name} = {o.name} ({string.Join(",", o.rgb.Select(v => v.ToString("0.00")))})");
                    }
                    var cs = ReflexBikeModel.HighRes(OpenGarageData.BikeOfModel(model)); bike = Now();
                    Log($"{slug}: with colours the garage draws {(cs?.rx == bike.hr && bike.hr != null ? "the twin" : "the race model")} wearing {string.Join(", ", cs?.rxParts?.Select(o => o.slot.name + " " + o.name) ?? new string[0])}");
                    if (cs?.rxParts == null || cs.rxParts.Count(o => o.rgb != null) != colours.Count(s => s.options.Any(o => o.rgb != null))) Fail(slug + ": a colour worn is not on the garage's bike");
                    yield return Show(-40f, 12f, 200); yield return UiShowcase.Shot("as-" + slug + "-colors");
                    yield return Show(90f, 6f, 120); Zoom(1.2f); yield return Wait(90); yield return UiShowcase.Shot("as-" + slug + "-colors-rear");
                    foreach (var s in Now().slots.Where(s => s.color != null)) RxParts.Set(Now(), s.Default);
                }
                BikeMods.SetChoice(model, "");
            }
            Log("done");
            Shell.ShowMain(); yield return Wait(5);
        }
    }
}
