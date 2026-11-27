// TEST ONLY (tests/menus_showcase fixtures): ANY VEHICLE ON ANY TRACK (core/plan/ANY_VEHICLE.md) in real races with AI.
// REFLEX_ANYVEHICLE=<event type or key>:<VehicleTypeId>[:<VehicleClassId>][,...] (e.g. Supercross:14,National:11,Powercross:3): for each combination the
// probe makes the shell's own setup (OpenSetup) with that VEHICLE (and CLASS; default the list's first), checks that the setup offers it although
// the event type never did (OpenSetup.OffEvent), launches it with 11 computer riders and the shortest duration, checks the world's roster (the
// player on a vehicle of the type, every computer rider on a vehicle of the class's types, all built), skips the intro with a virtual pad, rides
// the race line with GhostRideProbe's autopilot to the finish (a type without laps: 45 s) and checks that the event's laps are marked ANY VEHICLE
// (not kept, not posted). REFLEX_ANYVEHICLE_SHOTS=<dir>: pictures at the gate, after the start and at the finish. REFLEX_ANYVEHICLE_TIMEOUT: s per race (600).
// Muted. "[any-vehicle] PASS|FAIL ...", "[any-vehicle] RESULT PASS|FAIL".
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.FrontEnd;
using Reflex.Port.Practice;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.OriginalWorld;
using Reflex.Port.RxCore;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Reflex.Port.Showcase
{
    [DefaultExecutionOrder(32000)]
    public sealed class AnyVehicleProbe : MonoBehaviour
    {
        static string Script => Environment.GetEnvironmentVariable("REFLEX_ANYVEHICLE");
        int fails; string shots; Gamepad pad; OriginalWorldRuntime rt;
        // the autopilot's view of the presented frame (GhostRideProbe's)
        Vector3 bikePos, bikeFwd; float speed; readonly float[] line = new float[3 * 64]; int points; bool have;
        float stuck, reverse; bool drive; int driverCount; readonly ReflexDriverResult[] drivers = new ReflexDriverResult[64];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (string.IsNullOrEmpty(Script)) return;
            OpenShell.SkipIntro = true;
            var g = new GameObject("any vehicle probe (test only)"); DontDestroyOnLoad(g); g.AddComponent<AnyVehicleProbe>();
        }

        void Start()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            shots = Environment.GetEnvironmentVariable("REFLEX_ANYVEHICLE_SHOTS"); if (!string.IsNullOrEmpty(shots)) Directory.CreateDirectory(shots);
            OriginalWorldRuntime.ViewApplied += OnView;
            StartCoroutine(Run());
        }
        void OnDestroy() { OriginalWorldRuntime.ViewApplied -= OnView; }
        void LateUpdate() { if (AudioListener.volume != 0f) AudioListener.volume = 0f; }   // test players are muted

        void Log(string s) => Debug.Log("[any-vehicle] " + s);
        bool Check(bool ok, string what) { Log((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; return ok; }
        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        IEnumerator Seconds(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
        IEnumerator Shot(string name)
        {
            if (string.IsNullOrEmpty(shots)) yield break;
            string path = Path.Combine(shots, name + ".png");
            if (Application.isBatchMode) yield return UiShowcaseOffscreen.Shot(path);
            else { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(path); yield return null; yield return null; }
            Log("shot " + name);
        }
        IEnumerator Tap(GamepadButton b)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState(b)); yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return Frames(4);
        }

        void OnView(OriginalWorldRuntime world, OriginalSimulation.Results v)
        {
            if (v.bikePose == null || v.bikeNodes < 1) return;
            bikePos = new Vector3(v.bikePose[12], v.bikePose[13], v.bikePose[14]); bikeFwd = new Vector3(v.bikePose[8], v.bikePose[9], v.bikePose[10]);
            speed = v.bike.speed; points = Math.Min(64, v.raceLinePoints); Array.Copy(v.raceLine, line, points * 3); have = true;
            driverCount = Math.Min(v.driverCount, drivers.Length); Array.Copy(v.drivers, drivers, driverCount);
        }

        // the race-line autopilot (pad 0: left stick steering, right trigger throttle, left trigger brake)
        void Update()
        {
            if (pad == null || !pad.added) return;
            if (!drive || !have || points < 2) return;
            int nearest = 0; float best = float.MaxValue;
            for (int i = 0; i < points && i < 6; i++) { float dx = line[3 * i] - bikePos.x, dz = line[3 * i + 2] - bikePos.z, d = dx * dx + dz * dz; if (d < best) { best = d; nearest = i; } }
            float ahead = 18f + speed * 0.6f; int target = nearest;
            for (int i = nearest; i < points; i++) { float dx = line[3 * i] - bikePos.x, dz = line[3 * i + 2] - bikePos.z; target = i; if (dx * dx + dz * dz >= ahead * ahead) break; }
            float err = Mathf.DeltaAngle(Mathf.Atan2(bikeFwd.x, bikeFwd.z) * Mathf.Rad2Deg, Mathf.Atan2(line[3 * target] - bikePos.x, line[3 * target + 2] - bikePos.z) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float steer = Mathf.Clamp(err * 2.2f, -1f, 1f), turn = Mathf.Abs(err);
            float desired = turn > 1.2f ? 8f : turn > 0.6f ? 14f : turn > 0.3f ? 20f : 30f;
            float dt = Time.unscaledDeltaTime;
            if (speed < 1f) stuck += dt; else stuck = 0;
            if (stuck > 4f) { reverse = 1.5f; stuck = 0; }
            var s = new GamepadState();
            if (reverse > 0) { reverse -= dt; s.leftTrigger = 1f; s.leftStick = new Vector2(-steer, 0); }
            else { s.leftStick = new Vector2(steer, 0); if (speed < desired) s.rightTrigger = 1f; else if (speed > desired + 6f) s.leftTrigger = 0.63f; else s.rightTrigger = 0.47f; }
            InputSystem.QueueStateEvent(pad, s);
        }

        static int TypeOfModel(int model) => FeTables.I(RowIndex.First(FeTables.Shared.Rows("VehicleModel"), "VehicleModelId", model), "VehicleTypeId");

        IEnumerator Run()
        {
            pad = InputSystem.AddDevice<Gamepad>("any vehicle probe pad");
            OpenShell shell = null;
            for (float t = 0; t < 120f && !(shell = OpenShell.Instance); t += Time.unscaledDeltaTime) yield return null;
            if (!Check(shell, "the shell started")) { Quit(); yield break; }
            yield return Seconds(2f);
            Check(Reflex.Port.Options.ReflexSettings.AnyVehicle, "ANY VEHICLE ON ANY TRACK is on by default");
            int timeout = int.TryParse(Environment.GetEnvironmentVariable("REFLEX_ANYVEHICLE_TIMEOUT"), out int to) ? to : 600;
            foreach (var combo in Script.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0))
            {
                var bits = combo.Split(':');
                string mode = bits[0]; int type = bits.Length > 1 && int.TryParse(bits[1], out int ty) ? ty : -1, wantClass = bits.Length > 2 && int.TryParse(bits[2], out int wc) ? wc : -1;
                string tag = combo.Replace(':', '-').Replace('/', '_');
                ReflexEvents.Event e = null;
                try { e = ReflexEvents.All.FirstOrDefault(x => !x.Dlc && string.Equals(x.spec.type, mode, StringComparison.OrdinalIgnoreCase)) ?? ReflexEvents.Find(mode); }
                catch (Exception ex) { Check(false, combo + ": " + ex.Message); continue; }
                if (!Check(e != null && type >= 0, combo + ": the event and the vehicle type")) continue;
                Log("== " + combo + " (" + e.key + ", " + OpenContent.VehicleTypeName(type) + ")");

                // ---- the setup: the lists with the option off (the original's) and on
                var s = new OpenSetup { eventKey = e.key, ai = 2, riders = 12, duration = 0, vehicleType = type, vehicleChoiceExplicit = true };
                Reflex.Port.Options.ReflexSettings.AnyVehicle = false;
                var typesOff = OpenSetup.VehicleTypes(e); var classesOff = s.Classes(e);
                Reflex.Port.Options.ReflexSettings.AnyVehicle = true;
                var typesOn = OpenSetup.VehicleTypes(e);
                Log("  the event's own vehicles: " + string.Join(", ", typesOff.Select(OpenContent.VehicleTypeName)) + "; with ANY VEHICLE: " + string.Join(", ", typesOn.Select(OpenContent.VehicleTypeName)));
                Check(typesOff.SequenceEqual(OpenSetup.EventVehicleTypes(e)), combo + ": OFF, VEHICLE is the event's own list");
                Check(ReflexEvents.DrawableVehicleTypes.All(t => typesOn.Contains(t) || OpenSetup.Riders(e, t).Count == 0), combo + ": ON, VEHICLE offers every vehicle type that has a rider");
                Check(typesOn.Contains(type), combo + ": ON, VEHICLE offers " + OpenContent.VehicleTypeName(type));
                var classes = s.Classes(e);
                Log("  CLASS, OFF (" + OpenContent.VehicleTypeName(new OpenSetup { eventKey = e.key }.Type(e)) + " rides when the type is not the event's): " + string.Join(", ", classesOff.Select(c => c + " " + OpenContent.ClassName(c))));
                Log("  CLASS, ON: " + string.Join(", ", classes.Select(c => c + " " + OpenContent.ClassName(c))));
                Check(classes.Length > 0 && ReflexEvents.RaceableClasses.All(classes.Contains), combo + ": ON, CLASS offers every class the port races (" + classes.Length + ")");
                if (wantClass >= 0) { int ci = Array.IndexOf(classes, wantClass); if (Check(ci >= 0, combo + ": CLASS offers " + wantClass)) s.vehicleClass = ci; }
                int cls = s.Class(e); var classTypes = ReflexAtv.TypesOfClass(cls);
                bool offType = !OpenSetup.EventHasType(e, type);
                Log("  rides " + OpenContent.VehicleTypeName(s.Type(e)) + " in class " + cls + " " + OpenContent.ClassName(cls) + " (" + string.Join(", ", classTypes.Select(OpenContent.VehicleTypeName)) + "); the event's own type: " + !offType + ", own class: " + e.vehicleClasses.Contains(cls));
                Check(s.Type(e) == type, combo + ": the setup rides the chosen type");
                Check(s.OffEvent(e), combo + ": a choice the event type never offered (OffEvent)");
                if (wantClass < 0 && offType) Check(classTypes.Length == 1 && classTypes[0] == type, combo + ": the default CLASS of a vehicle the event never had is its own (the field rides the same vehicle)");
                var g = s.Garage(e);
                if (!Check(g != null && g.vehicleTypeId == type && g.vehicleClass == cls, combo + ": the launch's vehicle select choice: " + g)) continue;

                // ---- the race
                s.Launch();
                rt = null; have = false;
                for (float t = 0; t < 300f; t += Time.unscaledDeltaTime)
                {
                    rt = FindAnyObjectByType<OriginalWorldRuntime>();
                    if (rt && rt.FailureMessage != null) break;
                    if (rt && rt.Running && rt.Roster != null && rt.Roster.entries.Count > 1 && (rt.IntroCardShown || rt.EventUi.race_state >= 1)) break;
                    yield return null;
                }
                if (!Check(rt && rt.FailureMessage == null && rt.Running && rt.Roster != null, combo + ": the event loaded" + (rt && rt.FailureMessage != null ? " (" + rt.FailureMessage + ")" : ""))) { yield return Back(); continue; }
                Check(RxAnyVehicle.Current, combo + ": the session is flagged ANY VEHICLE (no records)");
                var local = rt.Roster.Local; var ai = rt.Roster.entries.Where(x => !x.Local).ToList();
                foreach (var x in rt.Roster.entries) Log("  slot " + x.slot.slot + (x.Local ? " (you) " : " ") + "'" + x.name + "' model " + x.vehicleModelId + " = " + OpenContent.VehicleTypeName(TypeOfModel(x.vehicleModelId)) + (x.bike == null ? " (no bike spec)" : ""));
                Check(local != null && TypeOfModel(local.vehicleModelId) == type, combo + ": you ride a " + OpenContent.VehicleTypeName(type));
                Check(ai.Count == 11, combo + ": 11 computer riders (" + ai.Count + ")");
                Check(ai.All(x => classTypes.Contains(TypeOfModel(x.vehicleModelId))), combo + ": every computer rider rides a vehicle of the class (" + string.Join(", ", ai.Select(x => TypeOfModel(x.vehicleModelId)).Distinct().Select(OpenContent.VehicleTypeName)) + ")");
                var opp = FindAnyObjectByType<OriginalOpponents>();
                for (float t = 0; t < 120f && !(opp && opp.Built >= ai.Count); t += Time.unscaledDeltaTime) { if (!opp) opp = FindAnyObjectByType<OriginalOpponents>(); yield return null; }
                Check(opp && opp.Built == ai.Count, combo + ": " + (opp ? opp.Built : 0) + "/" + ai.Count + " computer riders built");
                if (rt.IntroCardShown) { yield return Seconds(1f); yield return Tap(GamepadButton.South); }
                yield return Seconds(2.5f);
                rt.SetCameraMode(2, false);
                yield return Seconds(0.5f);
                yield return Shot(tag + "-gate");
                for (float t = 0; t < 90f && !(rt.Race.state == 2 || rt.Race.valid == 0); t += Time.unscaledDeltaTime) yield return null;
                Check(rt.Race.valid == 0 || rt.Race.state == 2, combo + ": the race is in progress (state " + rt.Race.state + ", " + rt.Race.num_laps + " laps, " + rt.Race.riders + " riders)");
                drive = true;
                yield return Seconds(7f);
                yield return Shot(tag + "-start");
                float start = Time.realtimeSinceStartup; bool finished = false; int lastLap = -1; bool lapRace = rt.Race.valid != 0 && rt.Race.num_laps > 0 && rt.Race.num_laps < 90;
                float limit = lapRace ? timeout : 45f; float shotAt = 40f, traceAt = 20f;
                while (Time.realtimeSinceStartup - start < limit)
                {
                    var race = rt.Race;
                    if (race.laps_completed != lastLap) { lastLap = race.laps_completed; Log($"  lap {race.laps_completed}/{race.num_laps} at {Time.realtimeSinceStartup - start:0} s, position {race.place} of {race.riders}"); }
                    if (lapRace && race.laps_completed >= race.num_laps) { finished = true; break; }
                    if (rt.FailureMessage != null) break;
                    if (Time.realtimeSinceStartup - start > shotAt) { shotAt = float.MaxValue; yield return Shot(tag + "-riding"); }
                    if (Time.realtimeSinceStartup - start > traceAt)
                    {   // where the autopilot is (a stalled ride shows here)
                        traceAt += 20f;
                        Log($"    {Time.realtimeSinceStartup - start:0} s: at ({bikePos.x:0}, {bikePos.y:0}, {bikePos.z:0}) {speed:0.0} ft/s, lap progress {race.lap_progress:0.000}, lap clock {race.lap_time:0.0}, reset timer {race.reset_timer:0.0}, race line points {points}");
                    }
                    yield return Seconds(0.5f);
                }
                drive = false; InputSystem.QueueStateEvent(pad, new GamepadState());
                Check(rt.FailureMessage == null, combo + ": the world ran without a failure" + (rt.FailureMessage != null ? " (" + rt.FailureMessage + ")" : ""));
                if (lapRace) Check(finished, combo + $": finished {rt.Race.laps_completed}/{rt.Race.num_laps} laps in {Time.realtimeSinceStartup - start:0} s, position {rt.Race.place} of {rt.Race.riders}");
                else Log($"  no laps in this event type: rode {limit:0} s (state {rt.Race.state})");
                // the field's race: how far every rider got (the computer riders on a track that was not built for their vehicle)
                int moved = 0;
                for (int i = 0; i < driverCount; i++) { var d = drivers[i]; if (d.local == 0 && (d.laps_completed > 0 || d.finished != 0)) moved++; }
                Log("  standings: " + string.Join("; ", Enumerable.Range(0, driverCount).Select(i => $"#{drivers[i].place} slot {drivers[i].slot}{(drivers[i].local != 0 ? " (you)" : "")} laps {drivers[i].laps_completed}{(drivers[i].finished != 0 ? " finished" : "")}")));
                if (lapRace) Check(moved >= ai.Count / 2, combo + ": the computer riders race the track (" + moved + " of " + ai.Count + " completed a lap)");
                // the laps: timed, never kept or posted
                var laps = RxPractice.Session;
                Log("  laps logged: " + string.Join(", ", laps.Select(l => RxPractice.LapText(l.ms) + (l.invalid != null ? " [" + l.invalid + "]" : " [CLEAN]"))));
                if (lapRace) Check(laps.Length > 0 && laps.All(l => l.invalid != null) && laps.Where(l => l.flying).All(l => (l.record.mods & RxLapRecord.ModAnyVehicle) != 0),
                    combo + ": every lap is refused (the core's ANY VEHICLE bit on the flying laps), none is clean");
                yield return Seconds(2f);
                yield return Shot(tag + "-finish");
                yield return Back();
            }
            Log(fails == 0 ? "RESULT PASS" : "RESULT FAIL (" + fails + ")");
            Quit();
        }

        IEnumerator Back()
        {
            drive = false; InputSystem.QueueStateEvent(pad, new GamepadState());
            if (FindAnyObjectByType<OriginalWorldRuntime>()) OriginalPauseMenu.QuitToFrontEnd();
            for (float t = 0; t < 60f && FindAnyObjectByType<OriginalWorldRuntime>(); t += Time.unscaledDeltaTime) yield return null;
            yield return Seconds(2.5f);
        }

        void Quit() { Application.Quit(fails == 0 ? 0 : 1); }
    }
}
