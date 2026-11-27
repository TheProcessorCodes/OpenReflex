using System;
using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    // The rider's hands and feet on a bike mod's grips and pegs (w-bikemods; core/plan/QUEUE.md "w-bikemods").
    //
    // A bike mod's only change that the core would see is where its hand / foot locators sit: its MX_Hierarchy moves NDE_RHT_Hand /
    // NDE_LFT_Hand / NDE_RHT_Foot / NDE_LFT_Foot (the rider's IK targets: RavenVehicleMotocross::vf28's V+0x1ac / +0x1b0 and the IK
    // animator's +0x1c0 .. +0x1f0) and its six MXVehicle_RD_LeftRight* lean animations move the same four tracks by the same constant
    // (the frame / fork tracks and every rotation are unchanged; the user's "New Bike Models": hands +0.10 ft forward, +0.03 up, feet
    // 0.11 ft back, 0.10 down). The core keeps the original data (the verified path, every rider, the oracle), so its rider reaches for
    // the original locators; for the local rider on a mod bike this moves each limb's end by the locator's offset, carried by the live
    // pose of the locator's parent node (the offset is a translation in that node's frame), with a two-bone IK (upper / lower bone, the
    // bend side and the end bone's own rotation kept). Only while the hand / foot is at its locator (within 0.75 ft, fading out by 1.5 ft):
    // a foot put down at the start gate, a hand off the bar in a trick and a wreck stay as the core poses them.
    public static class RiderLimbRetarget
    {
        /// One limb's offset: the bike hierarchy's locator node (the pose index), its parent node, the locator's translation change in the
        /// parent's frame, and the rider bones of the limb (MXRider_Hierarchy: upper, lower, end).
        public struct Offset { public string locator; public int node, parent; public Vector3 delta; public int upper, lower, end; }

        /// The rider bones that reach for each locator (MXRider_Hierarchy order: RArmA / RArmB / RWrist ..., RLegA / RLegB / RFoot ...).
        public static bool Chain(string locator, out int upper, out int lower, out int end)
        {
            switch (locator)
            {
                case "NDE_RHT_Hand": upper = 10; lower = 11; end = 12; return true;
                case "NDE_LFT_Hand": upper = 39; lower = 40; end = 41; return true;
                case "NDE_RHT_Foot": upper = 60; lower = 61; end = 62; return true;
                case "NDE_LFT_Foot": upper = 63; lower = 64; end = 65; return true;
            }
            upper = lower = end = -1; return false;
        }
        const float Near = 0.75f, Far = 1.5f;   // feet: the end bone's distance to the original locator for the full / no offset

        /// After the core's rider pose is applied (OriginalRider.ApplyBoneMatrices): bikePose = the core's bike node worlds (rows, 16 floats each).
        public static void Apply(OriginalRider rider, Offset[] offsets, float[] bikePose, int bikeNodes)
        {
            if (rider == null || offsets == null || offsets.Length == 0 || bikePose == null || !rider.HasOriginalPose || rider.bones == null) return;
            foreach (var o in offsets)
            {
                if (o.node < 0 || o.node >= bikeNodes || o.parent < 0 || o.parent >= bikeNodes || bikePose.Length < bikeNodes * 16) continue;
                if (o.end >= rider.bones.Length) continue;
                Transform a = rider.bones[o.upper], b = rider.bones[o.lower], c = rider.bones[o.end];
                if (!a || !b || !c) continue;
                var locator = OriginalBikePose.FromRows(bikePose, o.node * 16).GetColumn(3);
                var parent = OriginalBikePose.FromRows(bikePose, o.parent * 16);
                float dist = Vector3.Distance(c.position, locator);
                float w = dist <= Near ? 1f : dist >= Far ? 0f : 1f - (dist - Near) / (Far - Near);
                if (w <= 0f) continue;
                Vector3 d = parent.MultiplyVector(o.delta) * w;
                if (!(d.sqrMagnitude > 1e-10f) || float.IsNaN(d.x)) continue;
                TwoBone(a, b, c, c.position + d);
            }
        }

        /// A limb never straightens past this share of its length: a straight limb has no bend side, and the next solve (RiderGripIK after
        /// these offsets) read one from noise, so the elbow / knee flipped inside out from frame to frame (the Alive 50cc's rider as the fork
        /// compressed, 2026-11-09). 0.98: a 0.17 ft bend in the rider's arm; a target past it stays short by at most 2% of the limb.
        public const float MaxReach = 0.98f;

        /// A limb bent by more than this share of its upper bone's length off its own line shows its hinge's direction (TwoBone learns it).
        public const float HingeBend = 0.12f;
        static readonly System.Collections.Generic.Dictionary<Transform, Vector3> s_hinge = new System.Collections.Generic.Dictionary<Transform, Vector3>();

        /// Moves c towards target by turning a and b (the bend plane's side of b and c's world rotation kept).
        public static void TwoBone(Transform a, Transform b, Transform c, Vector3 target)
        {
            Vector3 A = a.position, B = b.position, C = c.position;
            float l1 = Vector3.Distance(A, B), l2 = Vector3.Distance(B, C);
            if (l1 < 1e-4f || l2 < 1e-4f) return;
            var endRotation = c.rotation;
            Vector3 toT = target - A;
            float dist = Mathf.Clamp(toT.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, (l1 + l2) * MaxReach);
            if (toT.sqrMagnitude < 1e-8f) return;
            Vector3 dir = toT.normalized;
            // The side the joint bends to, measured against the limb's OWN line (A to C) and carried to the new line. Two mistakes this
            // replaces (2026-11-16, the legs on a bike mod's pegs as the rider stood or leaned back):
            //  - measured against the NEW line, a nearly straight limb whose end moves sideways read its bend side from the move itself
            //    (the foot 0.13 ft in and up: a "bend side" pointing sideways, the knee folded half a foot into the bike);
            //  - a nearly straight limb's own side is a hundredth of a foot in whatever direction the animation left it (a knee 0.01 ft
            //    outward), and a shorter reach then bends the joint 0.4 ft THAT way (the knees snapping out wide).
            // A knee / elbow is a hinge: its bend direction is fixed in the upper bone's own frame. It is learnt from the frames where the
            // limb is clearly bent (the game's own pose, before this changes it) and used as the limb straightens, blended in by how bent
            // the limb is now.
            Vector3 bend = Vector3.zero, own = C - A;
            if (own.sqrMagnitude > 1e-8f)
            {
                own.Normalize();
                Vector3 side = (B - A) - own * Vector3.Dot(B - A, own);
                float mag = side.magnitude, strong = HingeBend * l1;
                if (mag > strong) { if (s_hinge.Count > 512) s_hinge.Clear(); s_hinge[a] = Quaternion.Inverse(a.rotation) * (side / mag); }   // (512: riders long gone)
                Vector3 use = mag > 1e-5f ? side / mag : Vector3.zero;
                if (mag < strong && s_hinge.TryGetValue(a, out var local))
                {
                    Vector3 hinge = a.rotation * local; hinge -= own * Vector3.Dot(hinge, own);
                    if (hinge.sqrMagnitude > 1e-6f) { hinge.Normalize(); float t = Mathf.Clamp01(mag / strong); use = Vector3.Slerp(hinge, use.sqrMagnitude > 0f ? use : hinge, t * t); }
                }
                if (use.sqrMagnitude > 1e-10f) { bend = Quaternion.FromToRotation(own, dir) * use; bend -= dir * Vector3.Dot(bend, dir); }
            }
            if (bend.sqrMagnitude < 1e-10f) bend = (B - A) - dir * Vector3.Dot(B - A, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(Vector3.Cross(dir, C - A), dir);
            if (bend.sqrMagnitude < 1e-8f) return;
            bend.Normalize();
            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            Vector3 B2 = A + l1 * (cosA * dir + Mathf.Sqrt(1f - cosA * cosA) * bend);
            a.rotation = Quaternion.FromToRotation(B - A, B2 - A) * a.rotation;
            Vector3 C1 = c.position, Bn = b.position;
            Vector3 T = A + dir * dist;
            b.rotation = Quaternion.FromToRotation(C1 - Bn, T - Bn) * b.rotation;
            c.rotation = endRotation;
        }
    }
}
