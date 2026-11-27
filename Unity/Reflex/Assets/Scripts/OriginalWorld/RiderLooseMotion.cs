using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Reflex.Port.Options;
using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    // OPEN REFLEX: RIDER MOTION: LOOSE (a prototype; OPTIONS > OPEN REFLEX > RIDER MOTION, ORIGINAL by default). Drawing only.
    //
    // The core runs the original game's rider animation and hands over the finished pose every frame (69 bone world matrices,
    // OriginalRider.ApplyBoneMatrices): a rider bolted to the bike. This adds secondary motion on top of that pose, after it (and the
    // hands' fit to the grips, RiderGripIK) is applied and before anything draws or records it:
    //   torso   a mass on a damped spring that follows a point of the bike at the rider's chest. Where the mass lags the point is what the
    //           bike's acceleration does to a body: forward under braking, back under power, down on a landing (then back up), up in the
    //           air. The pelvis takes a share of it (it drops and rises over the pegs), the spine leans by the rest.
    //   head    counters most of the torso's lean (the eyes stay ahead), lags the bike's quick pitch and roll (steadier than the bike over
    //           bumps), turns a little into the turn (by the bike's yaw rate) and rolls a little toward the horizon in a lean; all through
    //           its own softer spring.
    //   limbs   HANDS AND FEET DO NOT MOVE: each arm (upper arm, forearm, wrist) and leg (thigh, shin, foot) is solved again by a two-bone
    //           IK to the hand's / foot's transform of the ORIGINAL pose of that frame, the elbow / knee on the original's side (the elbows
    //           nudged out and up a little). So a hand on the grip stays on it, and a hand or foot the original takes off (a leg out in a
    //           corner, a stunt, the gate stance) goes exactly where the original puts it. A limb that would have to stretch is given its
    //           length back by its shoulder / hip (and the torso is not let that far in the first place).
    // Nothing here is read by the core, the physics, the ghosts' recorder or the online records (they all take the core's pose arrays);
    // every client adds its own layer to the riders it draws. The replay records the bones after this ran (ReflexReplay, LateUpdate), so
    // a replay / photo shows the ride as it looked and the layer never runs on playback. Ghosts and online riders get the layer when they
    // are posed. Not called for the garage / menu / podium riders: those stay the original's.
    // It fades to exactly the original pose: wrecked / off the bike, in the first-person view (the camera rides the original head), far
    // from the camera, after a teleport / reset / time jump (the springs start again), and it holds still while the game's clock does.
    public static class RiderLooseMotion
    {
        // ------------------------------------------------------------------------------------------------------------------ tunables
        // (feet, seconds, degrees. REFLEX_RIDER_MOTION_TUNE="ChestHz=2.0;HeadLook=0.8" sets any of them in a test player.)

        /// The torso spring: its frequency (lower = lazier, larger moves) and damping ratio (1 = no overshoot, under 0.7 = bouncy).
        public static float ChestHz = 2.3f, ChestDamping = 0.8f;
        /// How much of the spring's lag the body shows, per bike axis: fore / aft (braking, power), up / down (landings, the air), sideways.
        public static float GainLong = 1.0f, GainVert = 1.0f, GainSide = 0.45f;
        /// The lag never exceeds this (feet at the chest).
        public static float MaxOffset = 0.45f;
        /// A leaned bike in a balanced turn pushes nothing sideways on its rider: this much of that is taken out of the sideways lag.
        public static float LeanCancel = 1f;
        /// The pelvis' share of the chest's move, per axis (the spine leans by the rest). Seated (legs folded), a drop is cut to SeatedDrop of it.
        public static float HipVert = 0.55f, HipFore = 0.2f, HipSide = 0.2f, SeatedDrop = 0.45f;
        /// Legs shorter than this share of their length (hip to foot) count as seated, longer than StandingRatio as standing.
        public static float SeatedRatio = 0.80f, StandingRatio = 0.90f;
        /// A compression the pelvis did not take leans the chest forward by this much of it (the crouch of an absorbed landing).
        public static float CrouchPitch = 0.7f;
        /// The spine's lean on its lower / middle joint (the chest joint takes the rest).
        public static float SpineLow = 0.3f, SpineMid = 0.3f;
        /// The head: how much of the torso's added lean it undoes; the time it lags the bike's rotation by; how much of that lag it shows
        /// in pitch / roll (steadier than the bike), how far it looks into a turn (times the yaw lag), how far it rolls to the horizon.
        public static float HeadCounter = 0.7f, HeadLagTime = 0.22f, HeadStab = 0.45f, HeadLook = 0.55f, HeadLevel = 0.25f;
        /// The head's own spring and its limits (degrees).
        public static float HeadHz = 1.7f, HeadPitchMax = 12f, HeadYawMax = 16f, HeadRollMax = 9f, HeadLevelMax = 9f;
        /// The elbows / knees: pushed out (and the elbows up) by this share of the upper bone's length.
        public static float ElbowOut = 0.14f, ElbowUp = 0.07f, KneeOut = 0f;
        /// A limb is straight at this share of its length (as RiderLimbRetarget.MaxReach: a straight limb has no bend side).
        public static float Reach = 0.985f;
        /// Fades: per second in / out; the weight in a stunt; the springs start again past MaxDt between poses or a jump of TeleportFeet.
        public static float FadeRate = 5f, TrickWeight = 0.35f, MaxDt = 0.25f, TeleportFeet = 10f;
        /// Other riders: full within NearFeet of the camera, none past FarFeet.
        public static float NearFeet = 110f, FarFeet = 160f;
        /// On the bike: the pelvis within SeatNear feet of SeatCentre (the bike body's frame) = full, past SeatFar = none (a rider thrown off,
        /// far out in a stunt); and the torso within UprightNone / UprightFull (the cosine to the bike's up) of the bike.
        public static Vector3 SeatCentre = new Vector3(0f, 1.95f, -0.7f);
        public static float SeatNear = 2.0f, SeatFar = 3.2f, UprightNone = -0.1f, UprightFull = 0.25f;
        /// A bike on its side or its back (its up axis' height: none at BikeDownNone, full from BikeDownFull): a crash of a rider the client has no wreck flag for.
        public static float BikeDownNone = -0.05f, BikeDownFull = 0.25f;
        /// The anchor point follows the rider's own chest (in the bike's frame) this slowly: the layer answers the bike, not the animation.
        public static float AnchorFollow = 0.6f;

        const float Gravity = 32.17f;        // ft/s^2
        const float Step = 1f / 120f;        // the spring's longest sub-step

        // ------------------------------------------------------------------------------------------------------------------ the option
        /// -1: the saved option (ReflexSettings.RiderMotion); 0 ORIGINAL / 1 LOOSE forced (REFLEX_RIDER_MOTION=original|loose, tests).
        public static int ModeOverride = EnvMode();
        /// Under 0: the saved strength (ReflexSettings.RiderMotionStrength); else forced (REFLEX_RIDER_MOTION_STRENGTH=0..1, tests).
        public static float StrengthOverride = EnvStrength();
        static int savedMode = -1; static float savedStrength; static bool listening;

        public static bool Loose { get { Sync(); return (ModeOverride >= 0 ? ModeOverride : savedMode) == 1 && Strength > 0f; } }
        public static float Strength { get { Sync(); return StrengthOverride >= 0f ? Mathf.Clamp01(StrengthOverride) : savedStrength; } }
        static void Sync()
        {
            if (savedMode >= 0) return;
            if (!listening) { listening = true; ReflexSettings.Changed += key => { if (key == null || key.StartsWith("OpenReflex.RiderMotion")) savedMode = -1; }; Tune(Environment.GetEnvironmentVariable("REFLEX_RIDER_MOTION_TUNE")); }
            savedStrength = ReflexSettings.RiderMotionStrength; savedMode = ReflexSettings.RiderMotion;
        }
        static int EnvMode()
        {
            string e = Environment.GetEnvironmentVariable("REFLEX_RIDER_MOTION");
            return string.Equals(e, "loose", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(e, "original", StringComparison.OrdinalIgnoreCase) ? 0 : -1;
        }
        static float EnvStrength() =>
            float.TryParse(Environment.GetEnvironmentVariable("REFLEX_RIDER_MOTION_STRENGTH"), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? Mathf.Clamp01(v) : -1f;
        /// "Name=value;Name=value": the float tunables above by name (tests).
        public static void Tune(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return;
            foreach (var kv in spec.Split(';', ','))
            {
                int eq = kv.IndexOf('='); if (eq <= 0) continue;
                var f = typeof(RiderLooseMotion).GetField(kv.Substring(0, eq).Trim());
                if (f != null && f.FieldType == typeof(float) && float.TryParse(kv.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) f.SetValue(null, v);
                else Debug.LogWarning("[rider-motion] unknown tunable: " + kv);
            }
        }

        // ------------------------------------------------------------------------------------------------------------------ state
        public sealed class State
        {
            public bool primed, needVelocity;
            public double time;
            public Vector3 bikePos, anchorLocal, anchor, p, v;   // the anchor (bike frame, world), the mass (world)
            public Vector3 lag;                                  // the mass from the anchor, in the bike's frame (feet)
            public Quaternion lagRot = Quaternion.identity;      // the bike's rotation, lagged
            public Vector3 head, headVel;                        // the head's pitch / yaw / roll about the bike's axes (degrees) and their rates
            public float weight;
            // tests (Capture): the frame's pose before and after the layer
            public OriginalRider rider; public Vector3[] pos0, pos1; public Quaternion[] rot0, rot1; public bool captured; public int frame;
        }
        static readonly ConditionalWeakTable<OriginalRider, State> states = new ConditionalWeakTable<OriginalRider, State>();

        // MXRider_Hierarchy
        const int Root = 0, SpineA = 1, SpineB = 2, Chest = 6, Neck = 30, Head = 31, MeshNodes = 66;
        const int RArm = 10, LArm = 39, RLeg = 60, LLeg = 63;

        /// Other riders by their distance from the camera: 1 near, 0 far (not posed loose at all).
        public static float DistanceWeight(Vector3 position, Camera viewer)
        {
            if (!viewer) return 1f;
            float d = Mathf.Sqrt(ReflexWorldViews.SqrDistance(viewer, position));   // (SPLIT SCREEN, w-split: the nearer of the two viewers)
            return d <= NearFeet ? 1f : d >= FarFeet ? 0f : 1f - (d - NearFeet) / (FarFeet - NearFeet);
        }

        /// After the game's pose of this frame is on the rider's bones (and RiderLimbRetarget / RiderGripIK ran).
        /// bikeRows: the rider's vehicle nodes as the core gives them (row 0 = its body), time: the clock the pose belongs to (seconds: the
        /// game's presented time; it standing still holds the layer still), weight: 0 .. 1 from the caller (wrecked 0, a stunt, the distance).
        public static void Apply(OriginalRider rider, float[] bikeRows, double time, float weight = 1f)
        {
            if (!Loose) return;
            if (rider == null || !rider.HasOriginalPose || bikeRows == null || bikeRows.Length < 16) return;
            var b = rider.bones;
            if (b == null || b.Length < OriginalRider.OriginalBoneCount) return;
            long t0 = Timing ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            ApplyCore(rider, b, bikeRows, time, weight);
            if (Timing) { TimingTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; TimingCalls++; }
        }

        static void ApplyCore(OriginalRider rider, Transform[] b, float[] m, double time, float weight)
        {
            Transform root = b[Root], spA = b[SpineA], spB = b[SpineB], chest = b[Chest], neck = b[Neck], head = b[Head];
            if (!root || !spA || !spB || !chest || !neck || !head) return;
            Vector3 right = new Vector3(m[0], m[1], m[2]), up = new Vector3(m[4], m[5], m[6]), fwd = new Vector3(m[8], m[9], m[10]), bikePos = new Vector3(m[12], m[13], m[14]);
            if (!(fwd.sqrMagnitude > 1e-6f) || !(up.sqrMagnitude > 1e-6f) || float.IsNaN(bikePos.x + bikePos.y + bikePos.z)) return;
            Quaternion bikeRot = Quaternion.LookRotation(fwd, up), inv = Quaternion.Inverse(bikeRot);
            right = bikeRot * Vector3.right; up = bikeRot * Vector3.up; fwd = bikeRot * Vector3.forward;
            if (!states.TryGetValue(rider, out var s)) { s = new State { rider = rider }; states.Add(rider, s); }

            Vector3 rootPos = root.position, neckPos = neck.position;
            Vector3 chestLocal = inv * (chest.position - bikePos);
            Vector3 torso = neckPos - rootPos; float torsoLen = torso.magnitude;

            // ---- on the bike? (a rider thrown off, lying, far out of the seat: the original alone)
            float target = Mathf.Clamp01(weight);
            if (target > 0f)
            {
                float seat = (inv * (rootPos - bikePos) - SeatCentre).magnitude;
                target *= 1f - Smooth((seat - SeatNear) / Mathf.Max(1e-3f, SeatFar - SeatNear));
                float upright = torsoLen > 1e-3f ? Vector3.Dot(torso, up) / torsoLen : 0f;
                target *= Smooth((upright - UprightNone) / Mathf.Max(1e-3f, UprightFull - UprightNone));
                target *= Smooth((up.y - BikeDownNone) / Mathf.Max(1e-3f, BikeDownFull - BikeDownNone));
            }

            // ---- the springs (the game's clock; a jump of it or of the bike starts them again)
            float dt = (float)(time - s.time);
            bool restart = !s.primed || !(dt >= 0f) || dt > MaxDt || (bikePos - s.bikePos).sqrMagnitude > Sq(TeleportFeet + 400f * Mathf.Max(dt, 0f));
            if (restart)
            {
                s.primed = true; s.needVelocity = true; s.time = time; s.bikePos = bikePos;
                s.anchorLocal = chestLocal; s.anchor = s.p = bikePos + bikeRot * chestLocal; s.v = Vector3.zero; s.lag = Vector3.zero;
                s.lagRot = bikeRot; s.head = s.headVel = Vector3.zero; s.weight = 0f;
            }
            else if (dt > 1e-5f)
            {
                s.anchorLocal = Vector3.Lerp(s.anchorLocal, chestLocal, 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, AnchorFollow)));
                Vector3 anchor = bikePos + bikeRot * s.anchorLocal, from = s.anchor, va = (anchor - from) / dt;
                if (s.needVelocity) { s.needVelocity = false; s.p = anchor; s.v = va; }
                else
                {
                    float w0 = 2f * Mathf.PI * ChestHz, k = w0 * w0, c = 2f * ChestDamping * w0;
                    Vector3 g = right * (-Gravity * right.y * LeanCancel);   // (a balanced lean: gravity's share along the bike's side axis)
                    int n = Mathf.Clamp(Mathf.CeilToInt(dt / Step), 1, 32); float h = dt / n;
                    Vector3 p = s.p, v = s.v;
                    for (int i = 1; i <= n; i++)
                    {
                        Vector3 a = from + va * (h * (i - 1));   // (the anchor at the sub-step's start: a mass riding along sees no force)
                        v += (-k * (p - a) - c * (v - va) + g) * h;
                        p += v * h;
                    }
                    Vector3 d = p - anchor; float dm = d.magnitude;
                    if (dm > MaxOffset)
                    {   // at the limit: held there, the speed outward taken off
                        d *= MaxOffset / dm; p = anchor + d;
                        Vector3 rel = v - va, nrm = d / MaxOffset; float o = Vector3.Dot(rel, nrm);
                        if (o > 0f) v -= nrm * o;
                    }
                    s.p = p; s.v = v;
                }
                s.anchor = anchor; s.lag = inv * (s.p - anchor);

                // the head: the bike's rotation lagged; the lag about the bike's axes is what the head shows (through its own spring)
                s.lagRot = Quaternion.Slerp(s.lagRot, bikeRot, 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, HeadLagTime)));
                Quaternion rel2 = inv * s.lagRot; if (rel2.w < 0f) rel2 = new Quaternion(-rel2.x, -rel2.y, -rel2.z, -rel2.w);
                rel2.ToAngleAxis(out float ang, out Vector3 axis);
                Vector3 lagAngles = float.IsNaN(axis.x) || float.IsInfinity(axis.x) ? Vector3.zero : axis * Mathf.Min(ang, 60f);
                Vector3 want = new Vector3(Mathf.Clamp(lagAngles.x * HeadStab, -HeadPitchMax, HeadPitchMax),
                                           Mathf.Clamp(-lagAngles.y * HeadLook, -HeadYawMax, HeadYawMax),
                                           Mathf.Clamp(lagAngles.z * HeadStab, -HeadRollMax, HeadRollMax));
                {   // critically damped, exact for the step
                    float w0 = 2f * Mathf.PI * HeadHz, e = Mathf.Exp(-w0 * dt);
                    Vector3 x = s.head - want, tmp = (s.headVel + w0 * x) * dt;
                    s.headVel = (s.headVel - w0 * tmp) * e; s.head = want + (x + tmp) * e;
                }
                s.weight = Mathf.MoveTowards(s.weight, target, FadeRate * dt);
                s.time = time; s.bikePos = bikePos;
            }
            // (dt == 0: the same moment posed again, e.g. a held ghost: the layer as it stands)

            float W = s.weight * Strength;
            if (Capture) Keep(s, b, false);
            if (W < 1e-3f) { if (Capture) Keep(s, b, true); return; }

            // ---- the frame's original limbs
            Transform shR = b[RArm], elR = b[RArm + 1], wrR = b[RArm + 2], shL = b[LArm], elL = b[LArm + 1], wrL = b[LArm + 2];
            Transform hpR = b[RLeg], knR = b[RLeg + 1], ftR = b[RLeg + 2], hpL = b[LLeg], knL = b[LLeg + 1], ftL = b[LLeg + 2];
            if (!shR || !elR || !wrR || !shL || !elL || !wrL || !hpR || !knR || !ftR || !hpL || !knL || !ftL) return;
            Vector3 oShR = shR.position, oElR = elR.position, oWrR = wrR.position, oShL = shL.position, oElL = elL.position, oWrL = wrL.position;
            Vector3 oHpR = hpR.position, oKnR = knR.position, oFtR = ftR.position, oHpL = hpL.position, oKnL = knL.position, oFtL = ftL.position;
            Quaternion qWrR = wrR.rotation, qWrL = wrL.rotation, qFtR = ftR.rotation, qFtL = ftL.rotation;
            Transform n0 = b[MeshNodes], n1 = b[MeshNodes + 1], n2 = b[MeshNodes + 2];   // Jersey / Gloves / Boots: mesh nodes under the pelvis, left where they are
            Vector3 pn0 = n0 ? n0.position : default, pn1 = n1 ? n1.position : default, pn2 = n2 ? n2.position : default;
            Quaternion qn0 = n0 ? n0.rotation : default, qn1 = n1 ? n1.rotation : default, qn2 = n2 ? n2.rotation : default;

            float armR = (oElR - oShR).magnitude + (oWrR - oElR).magnitude, armL = (oElL - oShL).magnitude + (oWrL - oElL).magnitude;
            float legR = (oKnR - oHpR).magnitude + (oFtR - oKnR).magnitude, legL = (oKnL - oHpL).magnitude + (oFtL - oKnL).magnitude;
            float dArmR = (oShR - oWrR).magnitude, dArmL = (oShL - oWrL).magnitude, dLegR = (oHpR - oFtR).magnitude, dLegL = (oHpL - oFtL).magnitude;
            if (armR < 1e-3f || armL < 1e-3f || legR < 1e-3f || legL < 1e-3f) return;

            // ---- the torso: the chest's move (bike frame), the pelvis' share of it, the spine's lean for the rest
            Vector3 lag = new Vector3(s.lag.x * GainSide, s.lag.y * GainVert, s.lag.z * GainLong) * W;
            float standing = Mathf.Clamp01((Mathf.Min(dLegR / legR, dLegL / legL) - SeatedRatio) / Mathf.Max(1e-3f, StandingRatio - SeatedRatio));
            float hv = HipVert * (lag.y < 0f ? Mathf.Lerp(SeatedDrop, 1f, standing) : 1f);
            Vector3 hip = bikeRot * new Vector3(lag.x * HipSide, lag.y * hv, lag.z * HipFore);
            Vector3 lean = bikeRot * new Vector3(lag.x * (1f - HipSide), lag.y * (1f - hv), lag.z * (1f - HipFore));
            // (the pelvis no farther from the feet than the legs' slack)
            Vector3 uLeg = ((oHpR - oFtR) / Mathf.Max(1e-3f, dLegR) + (oHpL - oFtL) / Mathf.Max(1e-3f, dLegL)).normalized;
            hip = LimitAway(hip, Vector3.zero, uLeg, Mathf.Min(legR * Reach - dLegR, legL * Reach - dLegL));
            Quaternion spine = Quaternion.identity;
            if (torsoLen > 0.3f)
            {
                Vector3 tdir = torso / torsoLen; float axial = Vector3.Dot(lean, tdir);
                lean += -axial * tdir + fwd * (-axial * CrouchPitch);   // (a spine does not shorten: a compression leans it forward)
                // (the shoulders no farther from the hands than the arms' slack)
                Vector3 uArm = ((oShR - oWrR) / Mathf.Max(1e-3f, dArmR) + (oShL - oWrL) / Mathf.Max(1e-3f, dArmL)).normalized;
                lean = LimitAway(lean, hip, uArm, Mathf.Min(armR * Reach - dArmR, armL * Reach - dArmL));
                spine = Quaternion.FromToRotation(torso, torso + lean);
            }
            root.position = rootPos + hip;
            spA.rotation = Quaternion.Slerp(Quaternion.identity, spine, SpineLow) * spA.rotation;
            spB.rotation = Quaternion.Slerp(Quaternion.identity, spine, SpineMid) * spB.rotation;
            chest.rotation = Quaternion.Slerp(Quaternion.identity, spine, 1f - SpineLow - SpineMid) * chest.rotation;

            // ---- the head
            Vector3 ha = s.head * W; float ham = ha.magnitude;
            Quaternion look = ham > 1e-4f ? bikeRot * Quaternion.AngleAxis(ham, ha / ham) * inv : Quaternion.identity;
            Quaternion level = Quaternion.identity;
            Vector3 flat = Vector3.up - fwd * fwd.y;   // the world's up across the bike's nose: a roll about it
            if (up.y > 0f && flat.sqrMagnitude > 0.05f)
            {
                float roll = Vector3.Angle(up, flat);
                if (roll > 1e-3f) level = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(up, flat.normalized), Mathf.Min(HeadLevel * W * Mathf.Clamp01(up.y * 2f), HeadLevelMax / roll));
            }
            Quaternion headTurn = level * look * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(spine), HeadCounter);
            neck.rotation = Quaternion.Slerp(Quaternion.identity, headTurn, 0.4f) * neck.rotation;
            head.rotation = Quaternion.Slerp(Quaternion.identity, headTurn, 0.6f) * head.rotation;

            if (n0) n0.SetPositionAndRotation(pn0, qn0);
            if (n1) n1.SetPositionAndRotation(pn1, qn1);
            if (n2) n2.SetPositionAndRotation(pn2, qn2);

            // ---- hands and feet back where the original has them
            Vector3 elbow = (right * ElbowOut + up * ElbowUp) * W, elbowL = (-right * ElbowOut + up * ElbowUp) * W;
            Limb(shR, elR, wrR, oElR, oWrR, qWrR, elbow, dArmR);
            Limb(shL, elL, wrL, oElL, oWrL, qWrL, elbowL, dArmL);
            Limb(hpR, knR, ftR, oKnR, oFtR, qFtR, right * (KneeOut * W), dLegR);
            Limb(hpL, knL, ftL, oKnL, oFtL, qFtL, -right * (KneeOut * W), dLegL);
            if (Capture) Keep(s, b, true);
        }

        static float Sq(float x) => x * x;
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// move, with what it has along `away` (together with `other`'s) eased to stay inside `slack` (a soft stop: no kink as it is reached).
        static Vector3 LimitAway(Vector3 move, Vector3 other, Vector3 away, float slack)
        {
            float a = Vector3.Dot(move + other, away);
            if (a <= 0f) return move;
            float allowed = slack > 1e-3f ? slack * (1f - Mathf.Exp(-a / slack)) : 0f;
            return move - away * (a - allowed);
        }

        /// A two-bone IK: c to `target` with the rotation `endRotation`, b on the side of `bendAt` (a world point: the joint's place in the
        /// original pose) pushed by `nudge` (a share of the upper bone's length). A target out of reach: a slides to it (the limb is never
        /// stretched and c never left short); `was` = the distance a to c in the original pose (a limb the original has straight stays so).
        static void Limb(Transform a, Transform b, Transform c, Vector3 bendAt, Vector3 target, Quaternion endRotation, Vector3 nudge, float was)
        {
            Vector3 A = a.position, B = b.position, C = c.position;
            float l1 = (B - A).magnitude, l2 = (C - B).magnitude;
            if (l1 < 1e-4f || l2 < 1e-4f) return;
            Vector3 toT = target - A; float dist = toT.magnitude;
            if (dist < 1e-5f) return;
            Vector3 dir = toT / dist;
            float reach = Mathf.Max((l1 + l2) * Reach, Mathf.Min(was, l1 + l2 - 1e-4f));
            if (dist > reach) { A += dir * (dist - reach); a.position = A; B = b.position; dist = reach; }
            float least = Mathf.Abs(l1 - l2) + 1e-3f;
            if (dist < least) { A -= dir * (least - dist); a.position = A; B = b.position; dist = least; }
            Vector3 pole = bendAt - A + nudge * l1, bend = pole - dir * Vector3.Dot(pole, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = (B - A) - dir * Vector3.Dot(B - A, dir);
            if (bend.sqrMagnitude < 1e-10f) { c.rotation = endRotation; return; }
            bend.Normalize();
            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            Vector3 B2 = A + l1 * (cosA * dir + Mathf.Sqrt(1f - cosA * cosA) * bend);
            a.rotation = Quaternion.FromToRotation(B - A, B2 - A) * a.rotation;
            Vector3 Bn = b.position;
            b.rotation = Quaternion.FromToRotation(c.position - Bn, target - Bn) * b.rotation;
            c.rotation = endRotation;
        }

        // ------------------------------------------------------------------------------------------------------------------ tests
        /// Tests: the cost of the layer (Stopwatch ticks, calls) since they were last cleared.
        public static bool Timing; public static long TimingTicks; public static int TimingCalls;
        /// Tests: every rider's pose is kept before and after the layer, so that one held frame can be drawn both ways (Show).
        public static bool Capture;
        static readonly List<State> kept = new List<State>(); static int keptFrame;
        static void Keep(State s, Transform[] b, bool after)
        {
            int n = OriginalRider.OriginalBoneCount;
            if (s.pos0 == null) { s.pos0 = new Vector3[n]; s.pos1 = new Vector3[n]; s.rot0 = new Quaternion[n]; s.rot1 = new Quaternion[n]; kept.Add(s); }
            var p = after ? s.pos1 : s.pos0; var r = after ? s.rot1 : s.rot0;
            for (int i = 0; i < n; i++) if (b[i]) { p[i] = b[i].position; r[i] = b[i].rotation; }
            s.captured = after; s.frame = Time.frameCount; keptFrame = s.frame;
        }
        /// Tests (Capture, the world held): every rider drawn in its original pose of the held frame (true) or with the layer again (false).
        public static void Show(bool original)
        {
            for (int k = kept.Count - 1; k >= 0; k--)
            {
                var s = kept[k];
                if (!s.rider) { kept.RemoveAt(k); continue; }
                if (!s.captured || s.frame != keptFrame) continue;   // (a rider not posed in the held frame, a far one: left as it is)
                var b = s.rider.bones; var p = original ? s.pos0 : s.pos1; var r = original ? s.rot0 : s.rot1;
                for (int i = 0; i < OriginalRider.OriginalBoneCount && i < b.Length; i++) if (b[i]) b[i].SetPositionAndRotation(p[i], r[i]);
            }
        }
        /// Tests: a rider's layer as it stands (the lag in the bike's frame, the head's angles, the weight).
        public static bool TryGet(OriginalRider rider, out State state) { state = null; return rider != null && states.TryGetValue(rider, out state); }
    }
}
