// TEST ONLY (tests/menus_showcase fixtures, run with tests/rxmod/rxmod_ride.py): a .rxmod bike (core/plan/RXMOD.md) ridden in a race with AI.
// With REFLEX_RXMOD_PROBE=1 the player launches REFLEX_RXMOD_EVENT (default National/NAT_Concord) with REFLEX_RXMOD_AI computer riders (5) and
// REFLEX_RXMOD_LAPS laps (1) on a pro's bike of VehicleModelId REFLEX_RXMOD_MODEL (the scratch save chooses the mod for it: BikeModel.Model.<id>),
// skips the intro with a virtual pad, rides the race line with GhostRideProbe's autopilot to the finish and:
//   1. checks the local bike is the .rxmod bike (ReflexBikeModel.rxFit) and logs its fit, parts and the original it rides as
//   2. takes shots: the chase camera on the gate, riding, close-ups of the bike from the right / left / front / rear-right (wheels on the ground,
//      the forks and swingarm against the frame), in the air when it jumps, at the finish
//   3. logs the suspension's travel the pose gave the mod's parts (the front wheel along the forks, the rear wheel's height under the frame), min .. max,
//      each airtime's drawn tyres (their height in the bike frame: range, largest step) and the riding frames (no wreck) the rider's elbows /
//      knees bent the wrong way
// Result lines "[RxModRide] ..." with "[RxModRide] PASS" / "FAIL"; the player quits at the end.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.OriginalWorld;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Reflex.Port.Showcase
{
    public sealed class RxModRideProbe : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        string shots; Gamepad pad; OriginalWorldRuntime rt; int fails;
        Vector3 bikePos, bikeFwd; float speed; readonly float[] line = new float[3 * 64]; int points; bool have, drive;
        float stuck, reverse; OriginalSimulation.Results last; int airFrames, airShots;
        bool overrideCam; Vector3 camOffset; float camFov = 40f; string pendingShot;
        float frontMin = float.MaxValue, frontMax = float.MinValue, rearMin = float.MaxValue, rearMax = float.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Guest() { if (Environment.GetEnvironmentVariable("REFLEX_RXMOD_PROBE") == "1" && !Own) ReflexProfile.Disabled = true; }
        /// REFLEX_RXMOD_OWN=1: the player's own rider and vehicle (the profile's: the chosen "gear" .rxmod pieces are worn, Content/RxGear.cs) instead of a pro's.
        static bool Own => Environment.GetEnvironmentVariable("REFLEX_RXMOD_OWN") == "1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Environment.GetEnvironmentVariable("REFLEX_RXMOD_PROBE") != "1" || FindAnyObjectByType<RxModRideProbe>()) return;
            var g = new GameObject("rxmod ride probe (test only)"); DontDestroyOnLoad(g); g.AddComponent<RxModRideProbe>();
        }

        void Start()
        {
            shots = Environment.GetEnvironmentVariable("REFLEX_PREVIEW_SHOTS");
            if (!string.IsNullOrEmpty(shots)) Directory.CreateDirectory(shots);
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            OriginalWorldRuntime.ViewApplied += OnView;
            StartCoroutine(Run());
        }
        void OnDestroy() { OriginalWorldRuntime.ViewApplied -= OnView; }

        static void Log(string s) => Debug.Log("[RxModRide] " + s);
        void Fail(string s) { fails++; Debug.LogError("[RxModRide] FAIL " + s); }
        static string F(float v) => v.ToString("0.000", Inv);
        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        IEnumerator Seconds(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
        static int Env(string k, int d) => int.TryParse(Environment.GetEnvironmentVariable(k), NumberStyles.Integer, Inv, out int v) ? v : d;

        void OnView(OriginalWorldRuntime world, OriginalSimulation.Results v)
        {
            if (v.bikePose == null || v.bikeNodes < 1) return;
            last = v;
            bikePos = new Vector3(v.bikePose[12], v.bikePose[13], v.bikePose[14]); bikeFwd = new Vector3(v.bikePose[8], v.bikePose[9], v.bikePose[10]);
            speed = v.bike.speed; points = Math.Min(64, v.raceLinePoints); Array.Copy(v.raceLine, line, points * 3); have = true;
            // the suspension as the pose moves the mod's wheels: the front wheel along the forks from the steering head, the rear wheel below the frame
            var bm = rt ? rt.BikeModel : null;
            if (bm != null && bm.hierarchyNodes != null && drive)
            {
                int f = Array.IndexOf(bm.hierarchyNodes, "OBJ_FRT_Tire"), r = Array.IndexOf(bm.hierarchyNodes, "OBJ_BCK_Tire"), u = Array.IndexOf(bm.hierarchyNodes, "OBJ_UPR_Forks");
                if (f < 0)
                {   // an ATV (ATV_Hierarchy): the front right wheel from its upper A-arm's pivot, the rear axle below the frame
                    f = Array.IndexOf(bm.hierarchyNodes, "SWP_FRT_RHT_Tire"); r = Array.IndexOf(bm.hierarchyNodes, "OBJ_Axle"); u = Array.IndexOf(bm.hierarchyNodes, "OBJ_FRT_RHT_UPR_AArm");
                }
                if (f >= 0 && r >= 0 && u >= 0 && v.bikeNodes > Math.Max(f, Math.Max(r, u)))
                {
                    var frame = OriginalBikePose.FromRows(v.bikePose, 0); var inv = frame.inverse;
                    Vector3 P(int n) => inv.MultiplyPoint3x4(OriginalBikePose.FromRows(v.bikePose, n * 16).GetColumn(3));
                    float fl = (P(f) - P(u)).magnitude, rh = P(r).y;
                    frontMin = Mathf.Min(frontMin, fl); frontMax = Mathf.Max(frontMax, fl); rearMin = Mathf.Min(rearMin, rh); rearMax = Mathf.Max(rearMax, rh);
                }
            }
            bool air = v.onboardState.valid != 0 && v.onboardState.front_grounded == 0 && v.onboardState.rear_grounded == 0;
            if (drive && bm != null && v.bikeNodes > 0) Watch(bm, v, air);
            airFrames = air ? airFrames + 1 : 0;
            if (drive && airFrames == 20 && airShots < 2) { airShots++; StartCoroutine(Shot("air-" + airShots)); }
        }

        // the drawn rider's limbs (MXRider_Hierarchy upper / lower / end: right arm, left arm, right leg, left leg) and the side each joint
        // bends to in the bike frame (elbows out and back, knees forward): a joint on the other side by over 0.03 ft is "inverted"
        static readonly int[] LimbBones = { 10, 11, 12, 39, 40, 41, 60, 61, 62, 63, 64, 65 };
        static readonly Vector3[] LimbSide = { new Vector3(0.6f, 0f, -0.8f), new Vector3(-0.6f, 0f, -0.8f), Vector3.forward, Vector3.forward };
        readonly int[] inverted = new int[4]; int limbFrames, wreckFrames, remount;
        // the drawn wheels (their tyres' renderers' centres in the bike frame) through each airtime: their range and largest step
        Renderer[] frontTyres, rearTyres; int airs; float airFrontMin, airFrontMax, airRearMin, airRearMax, airStep; Vector3 lastFront; int airLen;
        void Watch(ReflexBikeModel bm, OriginalSimulation.Results v, bool air)
        {
            var inv = OriginalBikePose.FromRows(v.bikePose, 0).inverse;
            var bones = rt.rider ? rt.rider.bones : null;
            bool wreck = v.onboardState.valid != 0 && v.onboardState.wrecked != 0;
            if (wreck) { wreckFrames++; remount = 90; } else if (remount > 0) remount--;   // (a wreck's ragdoll and the remount after it: not riding)
            if (bones != null && bones.Length > 65 && rt.rider.HasOriginalPose && !wreck && remount == 0)
            {
                limbFrames++;
                for (int k = 0; k < 4; k++)
                {
                    Transform a = bones[LimbBones[3 * k]], b = bones[LimbBones[3 * k + 1]], c = bones[LimbBones[3 * k + 2]];
                    if (!a || !b || !c) continue;
                    Vector3 A = inv.MultiplyPoint3x4(a.position), B = inv.MultiplyPoint3x4(b.position), C = inv.MultiplyPoint3x4(c.position);
                    Vector3 dir = (C - A).normalized, off = (B - A) - dir * Vector3.Dot(B - A, dir);
                    if (Vector3.Dot(off, LimbSide[k].normalized) < -0.03f) inverted[k]++;
                }
            }
            if (frontTyres == null)
            {
                Renderer[] Of(string node) => bm.parts.Where(p => p.part && p.name.EndsWith("_" + node, StringComparison.Ordinal)).Select(p => p.part.GetComponent<Renderer>()).Where(r => r).ToArray();
                frontTyres = Of("OBJ_FRT_Tire"); rearTyres = Of("OBJ_BCK_Tire");
            }
            Vector3 Centre(Renderer[] rs) { if (rs.Length == 0) return Vector3.zero; var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); return inv.MultiplyPoint3x4(bb.center); }
            if (air && frontTyres.Length > 0 && rearTyres.Length > 0)
            {
                Vector3 f = Centre(frontTyres), r = Centre(rearTyres);
                if (airLen == 0) { airFrontMin = airFrontMax = f.y; airRearMin = airRearMax = r.y; airStep = 0f; }
                else airStep = Mathf.Max(airStep, (f - lastFront).magnitude);
                airFrontMin = Mathf.Min(airFrontMin, f.y); airFrontMax = Mathf.Max(airFrontMax, f.y); airRearMin = Mathf.Min(airRearMin, r.y); airRearMax = Mathf.Max(airRearMax, r.y);
                lastFront = f; airLen++;
            }
            else if (airLen > 0)
            {
                if (airLen >= 20 && airs < 12)
                {   // (the first frames after take-off: the suspension still extending)
                    airs++;
                    Log($"airtime {airs}: {airLen} frames, the drawn front tyre's height in the frame {F(airFrontMin)} .. {F(airFrontMax)} ft (largest step {F(airStep)}), rear {F(airRearMin)} .. {F(airRearMax)}");
                }
                airLen = 0;
            }
        }

        IEnumerator Run()
        {
            pad = InputSystem.AddDevice<Gamepad>("rxmod probe pad");
            OpenShell shell = null;
            for (float t = 0; t < 120f && !(shell = OpenShell.Instance); t += Time.unscaledDeltaTime) yield return null;
            if (!shell) { Fail("the shell did not start"); Quit(); yield break; }
            yield return Seconds(2f);
            int model = Env("REFLEX_RXMOD_MODEL", -1);
            var rxMods = BikeMods.All.Where(m => m.rx != null).ToList();
            Log("installed .rxmod mods: " + string.Join(", ", rxMods.Select(m => m.id + " (" + m.rx.hash + ")")) + "; choice for model " + model + ": \"" + BikeMods.ChoiceFor(model) + "\"");
            var chosen = BikeMods.ChosenFor(model);
            var skin = chosen?.rx?.skins.FirstOrDefault(k => k.For(model));   // (a skin mod: its bike, painted)
            var bike = skin?.target ?? chosen?.rx?.bikes.FirstOrDefault(b => b.vehicleModelId == model);
            if (Environment.GetEnvironmentVariable("REFLEX_RXMOD_EXPECT") != "none" && bike == null && skin == null) { Fail("no .rxmod bike or skin chosen for model " + model); }
            // a pro's vehicle of the model (AIRiderVehicle -> AIVehicle.VehicleModelId), his default gear
            var T = ReflexEvents.Tables;
            int I(Dictionary<string, object> r, string k) { try { return r != null && r.TryGetValue(k, out var o) ? Convert.ToInt32(o, Inv) : -1; } catch { return -1; } }
            ReflexWorldFiles.Garage garage = null;
            string evName = Environment.GetEnvironmentVariable("REFLEX_RXMOD_EVENT") ?? "National/NAT_Concord";
            var ev = ReflexEvents.Find(evName) ?? ReflexEvents.All.First(e => e.spec.type == "National" && e.location == "NAT_Concord");
            foreach (var arv in T["AIRiderVehicle"])
            {
                var av = T["AIVehicle"].FirstOrDefault(x => I(x, "AIVehicleId") == I(arv, "AIVehicleId"));
                if (I(av, "VehicleModelId") != model) continue;
                int riderId = I(arv, "AIRiderId"), gear = I(T["AIRider"].FirstOrDefault(x => I(x, "AIRiderId") == riderId), "DefaultRiderGearSetId");
                int type = I(T["VehicleModel"].FirstOrDefault(x => I(x, "VehicleModelId") == model), "VehicleTypeId");
                garage = new ReflexWorldFiles.Garage { riderOwner = ReflexWorldFiles.Garage.AI, riderId = riderId, riderGearSet = gear, vehicleOwner = ReflexWorldFiles.Garage.AI, vehicleId = I(av, "AIVehicleId"), vehicleTypeId = type, vehicleClass = -1 };
                var g2 = type >= 0 ? ev.GarageFor(type) : null; if (g2 != null) garage.vehicleClass = g2.vehicleClass;
                break;
            }
            if (garage == null) { Fail("no pro rides model " + model); Quit(); yield break; }
            // REFLEX_RXMOD_KITS=3,1,4: a gear / kit pack piece (Mods/Gear: a custom gear skin) chosen for each of these gear types before the race
            // (the first piece of the type of the installed packs), as GARAGE > RIDER chooses it: the Alive gear of the save is worn over it
            foreach (string ks in (Environment.GetEnvironmentVariable("REFLEX_RXMOD_KITS") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int kt = int.Parse(ks, Inv);
                var kit = Reflex.Port.Content.KitPacks.Current.byId.Values.Where(k => !k.bike && k.type == kt && !k.hidden).OrderBy(k => k.pack.name).ThenBy(k => k.rowId).FirstOrDefault();
                if (kit == null) { Fail("no gear pack piece of gear type " + kt + " is installed (Mods/Gear)"); continue; }
                Reflex.Port.Content.KitPacks.SetGear(kt, kit);
                Log($"custom gear chosen for type {kt}: {kit.lineTitle} {kit.title} ({kit.pack.name}, {kit.Token}), model {kit.model?.pkg}, materials {kit.material?.pkg}");
            }
            int ai = Env("REFLEX_RXMOD_AI", 5), laps = Env("REFLEX_RXMOD_LAPS", 1);
            Log($"launching {ev.key} with {ai} AI, {laps} lap(s), garage {garage}");
            ReflexEventSelect.Launch(ev.key, ai, false, "Reflex", laps, Own ? null : garage);
            for (float t = 0; t < 400f; t += Time.unscaledDeltaTime)
            {
                rt = FindAnyObjectByType<OriginalWorldRuntime>();
                if (rt && rt.FailureMessage != null) { Fail("world: " + rt.FailureMessage); Quit(); yield break; }
                if (rt && rt.Running && (rt.IntroCardShown || rt.EventUi.race_state >= 1)) break;
                yield return null;
            }
            if (!(rt && rt.Running)) { Fail("the world does not run"); Quit(); yield break; }
            if (rt.IntroCardShown) { yield return Seconds(1f); yield return Tap(GamepadButton.South); }
            for (float t = 0; t < 30f && !(rt.BikeModel != null); t += Time.unscaledDeltaTime) yield return null;
            var bm = rt.BikeModel;
            {   // the local rider's gear: the Alive pieces worn and Reflex's body (skin, gloves) under them
                var lr = rt.Roster?.Local?.rider;
                Log("the local rider's kit pieces: " + string.Join(", ", rt.Roster?.Local?.kits ?? new string[0]) + "; materials offered: " + string.Join(", ", lr?.parts.Select(p => p.key + ":" + p.matsPkg) ?? new string[0]));
                Log("the local rider: owner " + rt.Roster?.Local?.riderOwner + ", parts " + string.Join(", ", lr?.parts.Select(p => p.key + "=" + p.modelPkg + (p.rx != null ? " + ALIVE " + p.rx.line + " " + p.rx.name : "")) ?? new string[0]));
                var wantGear = Reflex.Port.Content.RxGear.Chosen();
                if (Own && wantGear.Count > 0)
                {
                    foreach (var w in wantGear) if (!(lr?.parts.Any(p => p.rx == w) ?? false)) Fail("the race's rider does not wear the chosen Alive " + w.slot + " " + w.name);
                    var rs = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var riderRoot in rs.Where(r => r.name.StartsWith("rxgear_")).Select(r => r.transform.root).Distinct())
                    {
                        var own = riderRoot.GetComponentsInChildren<Renderer>(true);
                        int skinN = own.Count(r => r.name.IndexOf("MXRider_Skin", StringComparison.OrdinalIgnoreCase) >= 0), glovesN = own.Count(r => r.name.EndsWith("MXRider_Jersey", StringComparison.OrdinalIgnoreCase) && !r.name.StartsWith("rxgear_"));
                        Log($"rider \"{riderRoot.name}\": Alive [{string.Join(", ", own.Where(r => r.name.StartsWith("rxgear_")).Select(r => r.name))}], {skinN} skin surfaces, {glovesN} glove / jersey surfaces of the game's");
                        if (skinN < 2 || glovesN < 1) Fail("the race's rider has no body under the Alive gear (skin " + skinN + ", gloves " + glovesN + ")");
                    }
                    if (!rs.Any(r => r.name.StartsWith("rxgear_"))) Fail("no Alive gear is drawn in the race");
                }
            }
            Log($"the local bike: {(bm?.chassis ? bm.chassis.name : "none")}, {bm?.parts.Count} parts, {bm?.bones.Count} skin bones; rx fit: {bm?.rxFit ?? "(not a .rxmod bike)"}; riders {rt.Roster?.entries.Count}");
            if (bike != null && (bm == null || bm.rxFit == null)) Fail("the race's bike is not the .rxmod bike");
            if (skin != null && rt.Roster?.Local?.bike != null && BikeMods.ForPlayer(rt.Roster.Local.bike)?.rxSkin != skin) Fail("the race's bike is not painted by the chosen skin");
            if (bike == null && Environment.GetEnvironmentVariable("REFLEX_RXMOD_EXPECT") == "none" && bm?.rxFit != null) Fail("a .rxmod bike was drawn without one chosen");
            yield return Seconds(1f);
            if (Own && Environment.GetEnvironmentVariable("REFLEX_RXMOD_TWINS") == "1") BuildTwins();
            rt.SetCameraMode(2, false);
            yield return Shot("gate-chase");
            yield return CloseUps("gate");
            for (float t = 0; t < 60f && !(rt.Race.state == 2 || rt.Race.valid == 0); t += Time.unscaledDeltaTime) yield return null;
            drive = true;
            yield return Seconds(6f);
            yield return Shot("riding-chase");
            yield return CloseUps("riding");
            rt.SetCameraMode(1, false); yield return Seconds(1.5f); yield return Shot("riding-onboard"); rt.SetCameraMode(2, false);
            int timeout = Env("REFLEX_RXMOD_TIMEOUT", 480);
            float start = Time.realtimeSinceStartup; bool finished = false; int lastLap = -1;
            while (Time.realtimeSinceStartup - start < timeout)
            {
                var race = rt.Race;
                if (race.laps_completed != lastLap) { lastLap = race.laps_completed; Log($"lap {race.laps_completed}/{race.num_laps} at {Time.realtimeSinceStartup - start:0} s, position {race.place}"); }
                if (race.num_laps > 0 && race.laps_completed >= race.num_laps) { finished = true; break; }
                if (rt.FailureMessage != null) { Fail("world: " + rt.FailureMessage); break; }
                yield return Seconds(0.5f);
            }
            drive = false; InputSystem.QueueStateEvent(pad, new GamepadState());
            Log($"suspension (the pose of the mod's parts): front wheel {F(frontMin)} .. {F(frontMax)} ft from the steering head ({F((frontMax - frontMin) * 12f)} in of travel), rear axle {F(rearMin)} .. {F(rearMax)} ft under the frame origin ({F((rearMax - rearMin) * 12f)} in)");
            if (frontMax - frontMin < 0.05f || rearMax - rearMin < 0.05f) Fail("the suspension did not move the mod's wheels");
            Log($"rider limbs over {limbFrames} riding frames ({wreckFrames} wrecked frames and the remounts after them left out): inverted (the joint on the wrong side) right arm {inverted[0]}, left arm {inverted[1]}, right leg {inverted[2]}, left leg {inverted[3]}");
            if (finished) { Log($"FINISHED {rt.Race.laps_completed}/{rt.Race.num_laps} laps, position {rt.Race.place}, time {rt.Race.race_time:0.00}"); yield return Seconds(2f); yield return Shot("finish"); }
            else Fail($"not finished in {timeout} s (laps {rt.Race.laps_completed}/{rt.Race.num_laps})");
            Log(fails == 0 ? "PASS" : "FAIL " + fails + " check(s)");
            yield return Frames(30);
            Quit();
        }

        IEnumerator CloseUps(string when)
        {
            overrideCam = true;
            foreach (var (name, off, fov) in new[] { ("right", new Vector3(5.5f, 1.2f, 0.2f), 38f), ("left", new Vector3(-5.5f, 1.2f, 0.2f), 38f),
                                                     ("front", new Vector3(1.5f, 1.5f, 5.0f), 40f), ("rear-right", new Vector3(3.2f, 1.8f, -3.6f), 40f) })
            {
                if (when == "gate" && name == "left") continue;   // (on the gate the left side is the next rider's)
                camOffset = off; camFov = fov; yield return Frames(3); yield return Shot(when + "-" + name);
            }
            if (Own)   // the whole rider from four sides (the gear: REFLEX_RXMOD_OWN)
                foreach (var (name, off, fov) in new[] { ("rider-front", new Vector3(3.5f, 4.5f, 7.5f), 40f), ("rider-front-left", new Vector3(-3.5f, 4.5f, 7.5f), 40f),
                                                         ("rider-back", new Vector3(3.0f, 5.0f, -7.5f), 40f), ("rider-back-left", new Vector3(-3.0f, 5.0f, -7.5f), 40f), ("rider-above", new Vector3(2.5f, 9f, 0.5f), 40f) })
                { camOffset = off; camFov = fov; yield return Frames(when == "gate" ? 40 : 3); yield return Shot(when + "-" + name); }   // (at the gate: the cut's motion blur and the TAA settle)
            overrideCam = false;
        }

        void Update()
        {
            if (pad == null || !pad.added || !drive || !have || points < 2) return;
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

        void LateUpdate()
        {
            if (overrideCam && rt && rt.chaseCamera && rt.chassis)
            {
                var cam = rt.chaseCamera; var aim = rt.chassis.position + rt.chassis.up * 0.6f;
                cam.transform.position = aim + rt.chassis.rotation * camOffset;
                cam.transform.rotation = Quaternion.LookRotation(aim - cam.transform.position, Vector3.up);
                cam.fieldOfView = camFov; cam.nearClipPlane = 0.05f;
            }
            Follow();
            if (pendingShot != null)
            {
                OffscreenShot(rt ? rt.chaseCamera : Camera.main, pendingShot);
                string n = Path.GetFileNameWithoutExtension(pendingShot);
                if (twins.Count > 0 && rt && Src() && (n.Contains("rider-") || n.EndsWith("-right") || n.EndsWith("-chase") || n.EndsWith("-rear-right")))
                {   // the same frame again with each twin in the rider's place, and with no rider at all (".none": what a rider covers)
                    foreach (var (tag, m) in twins) { ShowTwin(m); OffscreenShot(rt.chaseCamera, pendingShot.Substring(0, pendingShot.Length - 4) + "." + tag + ".png"); }
                    ShowTwin(null);
                    var own = Src().GetComponentsInChildren<Renderer>(true); var st = own.Select(r => r.forceRenderingOff).ToArray();
                    foreach (var r in own) r.forceRenderingOff = true;
                    OffscreenShot(rt.chaseCamera, pendingShot.Substring(0, pendingShot.Length - 4) + ".none.png");
                    for (int i = 0; i < own.Length; i++) own[i].forceRenderingOff = st[i];
                }
                pendingShot = null;
            }
        }

        // REFLEX_RXMOD_TWINS=1 (with REFLEX_RXMOD_OWN and Alive gear worn): the same frame shot again with the rider's gear swapped, on the
        // same pose, bike and camera: "<shot>.stock" (the profile's own Reflex gear, no Alive piece) and "<shot>.unshaped" (the Alive gear
        // as it was fitted before the pieces were shaped to Reflex's rider: RxGearRig.ShapeToReflex off).
        readonly List<(string tag, ReflexRiderModel model)> twins = new List<(string, ReflexRiderModel)>();
        void BuildTwins()
        {
            var lr = rt.Roster?.Local?.rider; if (lr == null || !lr.parts.Any(p => p.rx != null)) { Log("twins: the local rider wears no Alive gear"); return; }
            RiderSpec Copy(bool alive)
            {
                var o = new RiderSpec { hierarchyDb = lr.hierarchyDb, hierarchyPkg = lr.hierarchyPkg, hierarchyAsset = lr.hierarchyAsset, skinDb = lr.skinDb, skinPkg = lr.skinPkg, gearIds = lr.gearIds, plateRider = lr.plateRider };
                foreach (var p in lr.parts) { if (!alive && p.modelPkg == null) continue; o.parts.Add(new RiderSpec.Part { key = p.key, modelDb = p.modelDb, modelPkg = p.modelPkg, asset = p.asset, matsDb = p.matsDb, matsPkg = p.matsPkg, rx = alive ? p.rx : null }); }
                o.extraMaterials.AddRange(lr.extraMaterials);
                return o;
            }
            foreach (var (tag, alive) in new[] { ("stock", false), ("unshaped", true) })
            {
                bool was = RxGearRig.ShapeToReflex;
                try
                {
                    if (alive) RxGearRig.ShapeToReflex = false;
                    var m = ReflexRiderModel.Build(Copy(alive), null, false, true);
                    m.rider.enabled = false;
                    foreach (var r in m.root.GetComponentsInChildren<Renderer>(true)) { r.enabled = true; r.forceRenderingOff = true; }
                    twins.Add((tag, m));
                    Log($"twin {tag}: {m.root.GetComponentsInChildren<Renderer>(true).Length} renderers");
                }
                catch (Exception e) { Log("twin " + tag + " failed: " + e.Message); }
                finally { RxGearRig.ShapeToReflex = was; }
            }
        }
        /// Every frame: the twins take the rider's pose (they are skinned by the frame like any rider); the one shown takes the rider's place.
        ReflexRiderModel shownTwin; Renderer[] ownRenderers; bool[] ownState;
        /// The rider now drawn as the local one: the runtime's own, or (on the gate, where its object is off) the field's rider that wears the Alive gear.
        OriginalRider fieldRider;
        OriginalRider Src()
        {
            if (rt && rt.rider && rt.rider.gameObject.activeInHierarchy) return rt.rider;
            if (!fieldRider || !fieldRider.gameObject.activeInHierarchy)
                fieldRider = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.name.StartsWith("rxgear_") && !twins.Any(t => r.transform.IsChildOf(t.model.root.transform)))
                             .Select(r => r.GetComponentInParent<OriginalRider>()).FirstOrDefault(r => r && r != rt.rider);
            return fieldRider ? fieldRider : rt ? rt.rider : null;
        }
        void Follow()
        {
            if (twins.Count == 0 || !rt || !rt.rider) return;
            var src = Src(); if (!src) return;
            foreach (var (tag, m) in twins)
            {
                if (!m.root) continue;
                m.root.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                for (int i = 0; i < src.bones.Length && i < m.rider.bones.Length; i++) m.rider.bones[i].SetPositionAndRotation(src.bones[i].position, src.bones[i].rotation);
            }
        }
        void ShowTwin(ReflexRiderModel m)
        {
            if (shownTwin != null) foreach (var r in shownTwin.root.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
            if (shownTwin == null && m != null) { ownRenderers = Src().GetComponentsInChildren<Renderer>(true); ownState = ownRenderers.Select(r => r.forceRenderingOff).ToArray(); foreach (var r in ownRenderers) r.forceRenderingOff = true; }
            if (m == null && ownRenderers != null) { for (int i = 0; i < ownRenderers.Length; i++) if (ownRenderers[i]) ownRenderers[i].forceRenderingOff = ownState[i]; ownRenderers = null; }
            if (m != null) foreach (var r in m.root.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = false;
            shownTwin = m;
        }

        static void OffscreenShot(Camera cam, string file)
        {
            if (!cam) { Log("OFFSCREEN no camera for " + file); return; }
            int W = 1600, H = 900;
            var sz = Environment.GetEnvironmentVariable("REFLEX_SHOT_SIZE");
            if (!string.IsNullOrEmpty(sz) && sz.Split('x') is var p && p.Length == 2) { int.TryParse(p[0], out W); int.TryParse(p[1], out H); }
            var rtx = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var prior = cam.targetTexture;
            try
            {
                cam.targetTexture = rtx;
                for (int i = 0; i < 8; i++) cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rtx;
                var t = new Texture2D(W, H, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(file, t.EncodeToPNG()); Destroy(t);
            }
            catch (Exception e) { Log("OFFSCREEN failed: " + e.Message); }
            finally { cam.targetTexture = prior; RenderTexture.ReleaseTemporary(rtx); }
        }

        IEnumerator Shot(string name)
        {
            if (Application.isBatchMode)
            {
                if (!string.IsNullOrEmpty(shots)) { pendingShot = Path.Combine(shots, name + ".png"); while (pendingShot != null) yield return null; }
                Log("shot " + name);
                yield break;
            }
            yield return new WaitForEndOfFrame();
            if (!string.IsNullOrEmpty(shots)) ScreenCapture.CaptureScreenshot(Path.Combine(shots, name + ".png"));
            yield return Frames(2);
            Log("shot " + name);
        }
        IEnumerator Tap(GamepadButton b)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState(b)); yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return Frames(4);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
