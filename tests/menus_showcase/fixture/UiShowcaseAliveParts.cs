// TEST ONLY: the parts lists of GARAGE > VEHICLE > CUSTOMIZE per bike (Content/RxParts.cs, OpenShell.GarageRxParts.cs) and the tuning points.
// Page:
//   alive-parts   Run with a content folder that has imported MX vs ATV Alive bikes with their kits / parts (<game>/AliveBikes/<slug> and
//                 <slug>-parts) and a client root whose Mods/Bikes has a third-party .rxmod bike. For one vehicle model (REFLEX_AP_MODEL,
//                 default 1: the Blaze 450F) ridden as: the ORIGINAL bike, an Alive bike WITH parts, a third-party .rxmod bike, and (model
//                 REFLEX_AP_EMPTY, default 35: the 50cc's slot) an Alive bike WITHOUT parts, the vehicle level's cards are listed and checked:
//                   original       every original part type, no .rxmod slot;
//                   Alive          its own slots (GRAPHIC KIT, EXHAUST ...), then only the original types that work on it (a tuning attribute);
//                   third party    no slot of its own: the empty state, no original kit / handlebar ... type;
//                   Alive, none    the empty state.
//                 Then, on the Alive bike: a kit, an exhaust and tyres are worn through their cards (ap-kit / ap-exhaust / ap-tires shots, the
//                 stage from two angles: ap-worn-side / ap-worn-front); the profile's PlayerVehicleParts and PlayerVehicleTuning rows are
//                 compared before and after (unchanged); a performance part of the original vehicle is fitted from the PERFORMANCE list
//                 (the tuning row takes its bonus), the bonus is moved in VEHICLE TUNING's way (SetTuning: saved), and the rows are the same
//                 when the original bike's card is ridden again.
// Lines "[ui-showcase] aparts ..."; "FAIL aparts: ..." on a failure. A scratch profile with everything unlocked.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Reflex.Port.Content;
using Reflex.Port.FrontEnd;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseAliveParts
    {
        const string Tag = "[ui-showcase] ";
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => UiShowcase.Register("alive-parts", Page);

        static void Log(string s) => Debug.Log(Tag + "aparts " + s);
        static void Fail(string s) => Debug.LogError(Tag + "FAIL aparts: " + s);
        static OpenShell Shell => OpenShell.Instance;
        /// n frames and at least n / 60 s of real time (a batch player's frames are not paced: the stage's camera and the TAA settle by time).
        static IEnumerator Wait(int n) { float until = Time.realtimeSinceStartup + n / 60f; for (int i = 0; i < n || Time.realtimeSinceStartup < until; i++) yield return null; }
        static VisualElement El(string name) => Shell.Layer.Q<VisualElement>(name);
        static List<VisualElement> Cards() => El("garage-rows").Query<VisualElement>(className: "ou-gcard").ToList();
        static string Text(VisualElement e) => string.Join(" | ", e.Query<Label>().ToList().Select(l => l.text).Where(t => !string.IsNullOrWhiteSpace(t)));
        static List<string> Notes() => El("garage-rows").Query<Label>(className: "ou-garage-note").ToList().Select(l => l.text).ToList();
        static int Env(string k, int d) => int.TryParse(Environment.GetEnvironmentVariable(k), out int v) ? v : d;

        static void Angles(float yaw, float pitch)
        {
            var stage = typeof(OpenShell).GetField("garageStage", All).GetValue(Shell);
            stage?.GetType().GetMethod("SetAngles").Invoke(stage, new object[] { yaw, pitch });
        }

        /// The profile's rows of a player vehicle: its parts and its tuning (what awards and holds the tuning points).
        static string Rows(int pv)
        {
            var parts = OpenGarageData.Tab("PlayerVehicleParts");
            var ids = Enumerable.Range(0, parts.rows).Where(i => parts.Int("PlayerVehicleId", i) == pv).Select(i => parts.Int("VehiclePartId", i)).OrderBy(x => x);
            var tt = OpenGarageData.Tab("PlayerVehicleTuning"); int r = tt.Find("PlayerVehicleId", pv);
            string tune = r < 0 ? "-" : string.Join(",", new[] { "Engine1", "Engine2", "Suspension1", "Suspension2", "Tire1", "Tire2", "Braking", "Steering" }.Select(c => tt.Int(c, r, 0)));
            return "parts [" + string.Join(",", ids) + "] tuning [" + tune + "]";
        }

        /// Rides the model as a mod ("" the original) and opens GARAGE > VEHICLE on its parts.
        static IEnumerator Ride(int model, string modId)
        {
            int pv = OpenGarageData.PlayerVehicleOfModel(model);
            if (pv < 0) { Fail("model " + model + " is not owned in the unlocked profile"); yield break; }
            if (!OpenGarageData.SelectVehicle(pv)) Fail("select vehicle " + pv + ": " + CoreGarageProfile.Error);
            BikeMods.SetChoice(model, modId);
            ReflexProfile.Open();
            Shell.ShowMain(); yield return Wait(5);
            Shell.ShowGarage(OpenShell.GarageParts);
            yield return Wait(150);
        }

        /// The vehicle level's part cards: (own slots, original types) as "NAME = worn".
        static (List<string> own, List<string> original) Listed()
        {
            var own = new List<string>(); var orig = new List<string>();
            foreach (var c in Cards())
            {
                if (c.name == null) continue;
                var labels = c.Query<Label>().ToList().Select(l => l.text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                string line = labels.Count > 0 ? labels[0] + (labels.Count > 1 ? " = " + labels[1] : "") : c.name;
                if (c.name.StartsWith("garage-rxslot-")) own.Add(line);
                else if (c.name.StartsWith("garage-part-")) orig.Add(line);
            }
            return (own, orig);
        }

        static IEnumerator Page()
        {
            int model = Env("REFLEX_AP_MODEL", 1), emptyModel = Env("REFLEX_AP_EMPTY", 35);
            yield return UiShowcaseGarage.Open(OpenShell.GarageVehicle, true);
            var mods = BikeMods.All;
            Log("mods: " + string.Join("; ", mods.Where(m => m.rx != null).Select(m => m.id + " (" + m.rx.type + (m.rx.type == "bikes" ? ": " + string.Join(",", m.rx.bikes.Select(b => "#" + b.vehicleModelId + " " + b.slots.Count + " slots")) : "") + ")")));
            bool Alive(BikeMod m) => m.id.StartsWith("AliveBikes/", StringComparison.Ordinal);
            BikeMod Of(int mdl, Func<BikeMod, RxModBike, bool> want) => mods.FirstOrDefault(m => m.rx != null && m.rx.type == "bikes" && m.rx.bikes.Any(b => b.vehicleModelId == mdl && want(m, b)));
            var alive = Of(model, (m, b) => Alive(m) && b.slots.Count > 0);
            var third = Of(model, (m, b) => !Alive(m));
            var aliveEmpty = Of(emptyModel, (m, b) => Alive(m) && b.slots.Count == 0);
            if (alive == null) { Fail("no imported Alive bike with parts rides as model " + model); yield break; }
            if (third == null) Fail("no third-party .rxmod bike rides as model " + model + " (the client root's Mods/Bikes)");
            int pv = OpenGarageData.PlayerVehicleOfModel(model);
            var allTypes = OpenGarageData.PartTypes(pv);
            Log($"model {model}: player vehicle {pv}; the original part types: " + string.Join(", ", allTypes.Select(t => t.name + (string.IsNullOrEmpty(t.tuning) ? "" : " [" + t.tuning + "]"))));
            var tuned = allTypes.Where(t => !string.IsNullOrEmpty(t.tuning)).Select(t => t.name).ToList();
            var visual = allTypes.Where(t => string.IsNullOrEmpty(t.tuning)).Select(t => t.name).ToList();

            // ---- 1. the original bike: every original type, nothing of a .rxmod
            yield return Ride(model, "");
            string rows0 = Rows(pv);
            var (own, orig) = Listed();
            Log($"ORIGINAL (model {model}): own slots [{string.Join("; ", own)}]  original types [{string.Join("; ", orig)}]");
            Log("ORIGINAL profile rows: " + rows0);
            if (own.Count != 0) Fail("the original bike lists .rxmod slots: " + string.Join("; ", own));
            if (orig.Count != allTypes.Count) Fail($"the original bike lists {orig.Count} of its {allTypes.Count} part types");
            yield return UiShowcase.Shot("ap-original");

            // ---- 2. the Alive bike: its own slots, then only the original types that work on it
            yield return Ride(model, alive.id);
            var bike = BikeMods.RxBikeFor(model);
            if (bike == null) { Fail("the Alive bike is not ridden after its choice"); yield break; }
            (own, orig) = Listed();
            Log($"ALIVE {bike.name?.Full} ({alive.id}): own slots [{string.Join("; ", own)}]  original types [{string.Join("; ", orig)}]");
            foreach (var s in bike.slots) Log($"   slot {s.id} \"{s.name}\": {s.options.Count} options: " + string.Join(", ", s.options.Take(8).Select(o => (o.isDefault ? "*" : "") + o.Title)) + (s.options.Count > 8 ? " ..." : ""));
            if (own.Count != bike.slots.Count || own.Count == 0) Fail($"the Alive bike lists {own.Count} of its {bike.slots.Count} slots");
            foreach (string v in visual) if (orig.Any(o => o.StartsWith(v + " =") || o == v)) Fail("the Alive bike lists the original's " + v + " (it does not fit)");
            foreach (string t in tuned) if (!orig.Any(o => o.StartsWith(t + " =") || o == t)) Fail("the Alive bike does not list the original's " + t + " (its tuning points)");
            if (Rows(pv) != rows0) Fail("riding the Alive bike changed the profile rows: " + Rows(pv));
            var first = Cards().FirstOrDefault(c => c.name == "garage-rxslot-0");
            if (first != null) Shell.Nav.Focus(first);
            yield return Wait(240);
            yield return UiShowcase.Shot("ap-alive-list");

            // wear a kit, an exhaust and tyres through their cards
            foreach (var (slotId, shot) in new[] { ("GraphicKit", "ap-kit"), ("Exhaust", "ap-exhaust"), ("Tires", "ap-tires"), ("Handlebar", "ap-bars") })
            {
                int si = bike.slots.FindIndex(s => string.Equals(s.id, slotId, StringComparison.OrdinalIgnoreCase));
                if (si < 0) { Log(slotId + ": the bike has no such slot"); continue; }
                var slotCard = Cards().FirstOrDefault(c => c.name == "garage-rxslot-" + si);
                if (slotCard == null) { Fail("no card for slot " + slotId); continue; }
                (slotCard.userData as OuNavItem)?.submit?.Invoke();
                yield return Wait(40);
                var options = Cards().Where(c => c.name != null && c.name.StartsWith("garage-rxpart-" + si + "-")).ToList();
                Log($"{slotId}: level \"{Shell.GarageTitle}\": {options.Count} option cards: " + string.Join("; ", options.Take(6).Select(Text)));
                if (options.Count != bike.slots[si].options.Count) Fail($"{slotId}: {options.Count} cards for {bike.slots[si].options.Count} options");
                // the third option that is not the default (a different look), focused: its preview; then A
                var pick = options.Where(c => Text(c).IndexOf("AS THE BIKE COMES", StringComparison.Ordinal) < 0).Skip(2).FirstOrDefault() ?? options.Last();
                Shell.Nav.Focus(pick);
                yield return Wait(300);
                yield return UiShowcase.Shot(shot + "-preview");
                string picked = Text(pick);
                (pick.userData as OuNavItem)?.submit?.Invoke();
                yield return Wait(200);
                var worn = RxParts.Chosen(bike, bike.slots[si]);
                Log($"{slotId}: A on \"{picked}\" -> worn {worn?.Title} ({worn?.id}); the bike wears: " + string.Join(", ", RxParts.Chosen(bike).Select(o => o.slot.name + " " + o.Title)));
                if (worn == null || worn.isDefault) Fail(slotId + ": A did not wear the option");
                yield return UiShowcase.Shot(shot + "-worn");
                typeof(OpenShell).GetMethod("GarageUp", All).Invoke(Shell, null);
                yield return Wait(60);
            }
            if (Rows(pv) != rows0) Fail("wearing the Alive bike's parts changed the profile rows: " + Rows(pv) + " (were " + rows0 + ")");
            else Log("ALIVE parts worn: the profile rows are unchanged: " + Rows(pv));
            var spec = OpenGarageData.BikeOfModel(model);
            Log("the player's bike: " + spec?.name + " rx " + spec?.rx?.name?.Full + " parts " + RxParts.Signature(spec?.rxParts));
            if (spec?.rxParts == null || spec.rxParts.Count == 0) Fail("the player's bike is built without its worn parts");
            var tune = Cards().FirstOrDefault(c => c.name == "garage-tuning"); if (tune != null) Shell.Nav.Focus(tune);
            yield return Wait(300);
            Angles(90f, 6f); yield return Wait(60); yield return UiShowcase.Shot("ap-worn-side");
            Angles(-40f, 12f); yield return Wait(60); yield return UiShowcase.Shot("ap-worn-front");
            Angles(150f, 10f); yield return Wait(60); yield return UiShowcase.Shot("ap-worn-rear");

            // ---- 3. the tuning points on the Alive bike: a performance part of the original vehicle, fitted here, brings its bonus
            var perf = allTypes.FirstOrDefault(t => string.Equals(t.tuning, "engine", StringComparison.OrdinalIgnoreCase));
            if (perf.name == null) Fail("the vehicle has no engine part type");
            else
            {
                var items = OpenGarageData.PartItems(model, pv, perf.id);
                var part = items.Select(it => (it, row: FeGarageData.Unique("VehiclePart", "VehiclePartId", it.id))).Where(x => x.row != null && FeTables.I(x.row, "DefaultTuneAttribute1", 0) + FeTables.I(x.row, "DefaultTuneAttribute2", 0) > 0 && !x.it.equipped && x.it.Available).FirstOrDefault();
                if (part.it == null) Fail("no performance " + perf.name + " to fit");
                else
                {
                    int a1 = FeTables.I(part.row, "DefaultTuneAttribute1", 0), a2 = FeTables.I(part.row, "DefaultTuneAttribute2", 0);
                    bool ok = OpenGarageData.EquipPart(pv, part.it, perf.tuning); ReflexProfile.Open();
                    var tt = OpenGarageData.Tab("PlayerVehicleTuning"); int r = tt.Find("PlayerVehicleId", pv);
                    Log($"PERFORMANCE on the Alive bike: fitted {perf.name} \"{part.it.title}\" (bonus {a1} / {a2}): {(ok ? "ok" : "REFUSED " + CoreGarageProfile.Error)}; rows " + Rows(pv));
                    if (!ok || tt.Int("Engine1", r, -1) != a1 || tt.Int("Engine2", r, -1) != a2) Fail($"the part's tuning points were not awarded on the Alive bike (engine {tt.Int("Engine1", r, -1)} / {tt.Int("Engine2", r, -1)}, want {a1} / {a2})");
                    // spend them: 5 points from one side to the other, as VEHICLE TUNING does
                    var modelRow = FeGarageData.Unique("VehicleModel", "VehicleModelId", model);
                    int b1 = FeTables.I(modelRow, "DefaultEngine1", 0), b2 = FeTables.I(modelRow, "DefaultEngine2", 0);
                    if (OpenShell.MoveGarageTuning(b1, b2, a1, a2, 1, out int n1, out int n2) || OpenShell.MoveGarageTuning(b1, b2, a1, a2, -1, out n1, out n2))
                    {
                        var t = new ReflexGarage.Tuning { engine1 = n1, engine2 = n2, suspension1 = tt.Int("Suspension1", r, 0), suspension2 = tt.Int("Suspension2", r, 0), tire1 = tt.Int("Tire1", r, 0), tire2 = tt.Int("Tire2", r, 0), braking = tt.Int("Braking", r, 50), steering = tt.Int("Steering", r, 50) };
                        bool saved = ReflexProfile.SetTuning(pv, t).Result; ReflexProfile.Open();
                        tt = OpenGarageData.Tab("PlayerVehicleTuning"); r = tt.Find("PlayerVehicleId", pv);
                        Log($"TUNING on the Alive bike: engine {a1} / {a2} -> {n1} / {n2}: {(saved ? "saved" : "REFUSED " + ReflexProfile.Error)}; rows " + Rows(pv));
                        if (!saved || tt.Int("Engine1", r, -1) != n1 || tt.Int("Engine2", r, -1) != n2) Fail("the tuning points could not be spent on the Alive bike");
                    }
                    else Log("TUNING: the bonus cannot be moved on this model (base at its limits)");
                }
            }
            string rowsTuned = Rows(pv);
            Shell.ShowMain(); yield return Wait(5);
            Shell.ShowGarage(OpenShell.GarageTuning); yield return Wait(200);
            yield return UiShowcase.Shot("ap-alive-tuning");

            // ---- 4. the third-party .rxmod bike: nothing of its own, no original kit / bar; the tuning types stay
            if (third != null)
            {
                yield return Ride(model, third.id);
                var tb = BikeMods.RxBikeFor(model);
                (own, orig) = Listed();
                var notes = Notes();
                bool wheels = RxParts.HasOwnWheels(tb);
                Log($"THIRD PARTY {tb?.name?.Full} ({third.id}, own wheels {wheels}): own slots [{string.Join("; ", own)}]  original types [{string.Join("; ", orig)}]");
                Log("   notes: " + string.Join(" // ", notes));
                if (own.Count != 0) Fail("the third-party bike lists slots it does not have");
                if (!notes.Any(n => n.IndexOf("HAS NO PARTS OF ITS OWN", StringComparison.Ordinal) >= 0)) Fail("no empty state on the third-party bike");
                foreach (var t in allTypes)
                {
                    bool fits = RxParts.OriginalTypeFits(t.factory, t.tuning, wheels), listed = orig.Any(o => o.StartsWith(t.name + " =") || o == t.name);
                    if (fits != listed) Fail($"third party: {t.name} (tuning '{t.tuning}') listed {listed}, fits {fits}");
                }
                if (Rows(pv) != rowsTuned) Fail("riding the third-party bike changed the profile rows: " + Rows(pv));
                yield return Wait(200);
                yield return UiShowcase.Shot("ap-thirdparty");
            }

            // ---- 5. back on the original bike: the same parts and tuning points
            yield return Ride(model, "");
            Log("ORIGINAL again: " + Rows(pv));
            if (Rows(pv) != rowsTuned) Fail("the profile rows differ on the original bike: " + Rows(pv) + " (were " + rowsTuned + ")");
            (own, orig) = Listed();
            if (own.Count != 0 || orig.Count != allTypes.Count) Fail("the original bike's list changed: " + own.Count + " own, " + orig.Count + " original");

            // ---- 6. an Alive bike without parts: the empty state
            if (aliveEmpty != null)
            {
                yield return Ride(emptyModel, aliveEmpty.id);
                var eb = BikeMods.RxBikeFor(emptyModel);
                (own, orig) = Listed();
                var notes = Notes();
                Log($"ALIVE WITHOUT PARTS {eb?.name?.Full} ({aliveEmpty.id}): own slots [{string.Join("; ", own)}]  original types [{string.Join("; ", orig)}]");
                Log("   notes: " + string.Join(" // ", notes));
                if (own.Count != 0 || !notes.Any(n => n.IndexOf("HAS NO PARTS OF ITS OWN", StringComparison.Ordinal) >= 0)) Fail("no empty state on the Alive bike without parts");
                yield return Wait(200);
                yield return UiShowcase.Shot("ap-alive-empty");
                BikeMods.SetChoice(emptyModel, "");
            }
            else Log("no Alive bike without parts rides as model " + emptyModel + " (skipped)");

            // leave the Alive bike with its parts on for the next page / a look at the hub
            yield return Ride(model, alive.id);
            Log("done");
            Shell.ShowMain(); yield return Wait(5);
        }
    }
}
