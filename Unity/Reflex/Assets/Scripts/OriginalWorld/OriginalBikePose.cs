using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    // Drives the recovered MarsF Lite parts (frame, upper/lower forks, bars and levers, swingarm,
    // tyres, fenders) from the original bike model's node world matrices (reflex_world_bike_pose):
    // the game's own suspension travel, steering and wheel spin. Each part mesh is stored in model
    // (bind) space, so its transform is liveWorld(node) * inverse(bindWorld(node)).
    [DisallowMultipleComponent]
    public sealed class OriginalBikePose : MonoBehaviour
    {
        public const int NodeCount = 18;   // MX_MarsF_Lite hierarchy

        public Transform[] parts = new Transform[0];
        public int[] nodes = new int[0];
        public Matrix4x4[] inverseBind = new Matrix4x4[0];   // Unity convention (column vectors)

        public bool HasPose { get; private set; }

        /// A bike of its own size and hierarchy (an imported Alive 50cc, core/plan/ALIVE_BIKES.md) whose physics rake is not its model's: the
        /// core places the lower forks and the front wheel by the physics (the parameter file's rake and fork attachment), the upper forks by
        /// the model's rest pose, so the two halves of the fork part. ForkFix turns the steering assembly (the upper forks and every node under
        /// them) about the steering head until its fork points at the physics axle, then slides the lower forks along it onto that axle:
        /// the fork, the fender, the bars and the wheel stay one assembly, the wheel stays where the physics rolls it. Null: none.
        public sealed class ForkFix
        {
            public int upr, lwr, frt; public int[] subtree;           // pose indices; subtree: the nodes under the upper forks (not lwr / frt)
            public Matrix4x4 restUL, restLT;                          // the model's rest locals: lower forks in upper, wheel in lower
            public void Apply(float[] m, int count)
            {
                if (upr >= count || lwr >= count || frt >= count) return;
                var U = FromRows(m, upr * 16); var T = FromRows(m, frt * 16);
                Vector3 H = U.GetColumn(3), A = T.GetColumn(3);
                float L = (A - H).magnitude; if (L < 0.1f) return;
                // the slide s along the lower forks' axis (their local Y) that puts the axle at L from the head
                Vector3 p0 = (restUL * restLT).GetColumn(3);
                Vector3 ty = ((Vector3)restUL.MultiplyVector(Vector3.up)).normalized;
                float b = Vector3.Dot(p0, ty), c = p0.sqrMagnitude - L * L, disc = b * b - c;
                if (disc < 0f) return;
                float s = -b + Mathf.Sqrt(disc);   // (p0 + s ty) has length L; the root nearer zero
                float s2 = -b - Mathf.Sqrt(disc); if (Mathf.Abs(s2) < Mathf.Abs(s)) s = s2;
                if (Mathf.Abs(s) > 1.5f) return;
                Vector3 a = p0 + s * ty;
                var R = Quaternion.FromToRotation(U.MultiplyVector(a).normalized, (A - H) / L);
                var about = Matrix4x4.Translate(H) * Matrix4x4.Rotate(R) * Matrix4x4.Translate(-H);
                var U2 = about * U;
                ToRows(U2, m, upr * 16);
                if (subtree != null) foreach (int i in subtree) if (i < count) ToRows(about * FromRows(m, i * 16), m, i * 16);
                var L2 = U2 * restUL * Matrix4x4.Translate(new Vector3(0f, s, 0f));   // (ty is the lower forks' own Y)
                ToRows(L2, m, lwr * 16);
                var T2 = Matrix4x4.TRS(A, (about * T).rotation, Vector3.one);
                ToRows(T2, m, frt * 16);
            }
        }
        public ForkFix forkFix;

        /// A bike of its own size (.rxmod "pose": "physics"): the swing arm aimed at the simulation's rear wheel. The core turns the arm by the
        /// original's solve, made for the original's arm: on the Alive 50cc's 1.0 ft arm a rear wheel hanging past 45 degrees hit its fold-over
        /// guard and the arm flipped up and back from frame to frame (the wheel "bouncing" in the air, 2026-11-09; the core's tune now hangs
        /// it higher: ALIVE_BIKES.md "Playtest fixes"). A backstop: the arm and the nodes under it turn about its hinge (its X) onto the rear
        /// wheel body; the tyre sits on that line at the wheel's distance, held to minReach .. maxReach of the arm (the dropouts: within the
        /// arm's range on the ground, 0.97 - 1.08 of it).
        public sealed class SwingFix
        {
            public int arm, tyre; public int[] subtree;   // pose indices; subtree: the nodes under the arm (not the tyre)
            public Vector3 restTyre;                      // the tyre's rest position in the arm's frame
            public float minReach = 0.9f, maxReach = 1.08f;
            public void Apply(float[] m, int count, Vector3 axle)
            {
                if (arm >= count || tyre >= count) return;
                var A = FromRows(m, arm * 16); var T = FromRows(m, tyre * 16);
                Vector3 P = A.GetColumn(3), hinge = ((Vector3)A.GetColumn(0)).normalized;
                float len = restTyre.magnitude; if (len < 0.1f || hinge.sqrMagnitude < 0.5f) return;
                Vector3 cur = Vector3.ProjectOnPlane(A.MultiplyVector(restTyre), hinge), want = Vector3.ProjectOnPlane(axle - P, hinge);
                if (cur.sqrMagnitude < 1e-4f || want.sqrMagnitude < 1e-4f) return;
                var about = Matrix4x4.Translate(P) * Matrix4x4.Rotate(Quaternion.AngleAxis(Vector3.SignedAngle(cur, want, hinge), hinge)) * Matrix4x4.Translate(-P);
                var A2 = about * A;
                ToRows(A2, m, arm * 16);
                if (subtree != null) foreach (int i in subtree) if (i < count) ToRows(about * FromRows(m, i * 16), m, i * 16);
                Vector3 dir = A2.MultiplyVector(restTyre).normalized;
                float reach = Mathf.Clamp(Vector3.Dot(axle - P, dir), len * minReach, len * maxReach);
                ToRows(Matrix4x4.TRS(P + dir * reach, (about * T).rotation, Vector3.one), m, tyre * 16);
            }
        }
        public SwingFix swingFix;
        /// The simulation's rear wheel this frame (its render body; SwingFix), set before Apply.
        public Vector3? rearAxle;

        /// Unity column-vector matrix -> the game's rows (FromRows' inverse; column 3 written 0, 0, 0, 1).
        public static void ToRows(Matrix4x4 r, float[] m, int o)
        {
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 4; col++)
                    m[o + col * 4 + row] = r[row, col];
            m[o + 3] = 0; m[o + 7] = 0; m[o + 11] = 0; m[o + 15] = 1;
        }

        // Game row-vector matrix (v' = v * M, translation in 12..14) -> Unity column-vector matrix.
        public static Matrix4x4 FromRows(float[] m, int o)
        {
            var r = new Matrix4x4();
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 4; col++)
                    r[row, col] = m[o + col * 4 + row];
            r.SetRow(3, new Vector4(0, 0, 0, 1));   // affine: the game's column 3 (elements 3, 7, 11, 15) can be padding
            return r;
        }

        /// The game's node flags (model node record +0xc; bit 0x10: the renderer 007f2740 skips the node: a machine's damage panels):
        /// each part shown as its node is (count 0: no flags, nothing changes).
        public void ApplyFlags(byte[] flags, int count)
        {
            if (flags == null || count <= 0 || parts == null) return;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!parts[i] || nodes[i] >= count) continue;
                bool show = (flags[nodes[i]] & 0x10) == 0;
                if (parts[i].gameObject.activeSelf != show) parts[i].gameObject.SetActive(show);
            }
        }

        public bool Apply(float[] matrices, int count)
        {
            if (matrices == null || count < NodeCount || parts == null) return false;
            // Only the affine part is used (elements 3, 7, 11, 15 are not read).
            for (int i = 0; i < count * 16; i++) if ((i & 3) != 3 && (float.IsNaN(matrices[i]) || float.IsInfinity(matrices[i]))) return false;
            forkFix?.Apply(matrices, count);   // (in place: the rider's limb offsets and grips read the same rows after)
            if (swingFix != null && rearAxle is Vector3 axle) swingFix.Apply(matrices, count, axle);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!parts[i]) continue;
                var w = FromRows(matrices, nodes[i] * 16) * inverseBind[i];
                Vector3 forward = w.GetColumn(2), up = w.GetColumn(1);
                if (forward.sqrMagnitude < 1e-10f || up.sqrMagnitude < 1e-10f) continue;
                parts[i].SetPositionAndRotation(w.GetColumn(3), Quaternion.LookRotation(forward, up));
            }
            HasPose = true;
            return true;
        }
    }
}
