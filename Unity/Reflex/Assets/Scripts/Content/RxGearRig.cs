// Fitting gear made on MX vs ATV Alive's rider to Reflex's rider (Content/RxGear.cs; core/plan/ALIVE_BIKES.md "Rider gear").
//
// Alive's rider (Rider_M_Hierarchy, 55 nodes) and Reflex's (MXRider_Hierarchy, 69) name their bones alike but are different rigs: Alive's
// stands in a T pose with a longer spine chain (Pelvis, SpineA..D, Chest at the shoulders; Reflex: Root, SpineA, SpineB, Chest at mid torso),
// its arms straight out (Reflex's hang 45 degrees down, the forearms forward), its limbs of other lengths, its bone frames of other
// conventions on the left hand. So a vertex cannot be skinned by "the bone of the same name with Alive's bind pose". Instead each Alive
// bone gets a REST ALIGNMENT G (Alive's rest space -> Reflex's rest space, for the part of the body that bone carries) and a DRIVER (the
// Reflex bone that moves that part); a vertex is then skinned as  sum w * pose[driver] * inverseBind[driver] * G * v :
//   torso / neck   a translation: the hips (the leg joints), the shoulders (the arm joints) and the neck base of the two rests are laid on
//                  each other, interpolated by height between them (the three differ by 0.18 - 0.22 ft); SpineC / SpineD ride Reflex's Chest
//   limbs          each segment (collar, upper arm, forearm, thigh, shin) turned by the smallest rotation that lays Alive's joint-to-joint
//                  direction on Reflex's and stretched along it to Reflex's length, so both ends sit on Reflex's joints
//   hands          with the forearm's rotation at the wrist (Alive's seven finger bones follow the hand: the suit's gloves are not drawn,
//                  Reflex's own gloves are); feet: the bone frames' own difference (the two rigs agree on the feet: 10 degrees of pitch)
// A rigid piece (helmet, goggles on the Helmet / Goggles node; a neck brace) is placed by a HEAD alignment: a reference box of Alive's
// helmets in Alive's rest space laid on the box of a Reflex helmet in Reflex's rest space (centre and size), so an Alive helmet sits where
// and as large as a Reflex one, and Alive's goggles keep their place in Alive's helmets.
// Pure managed math (no native Unity calls): tests/alive_bikes builds it outside Unity.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reflex.Port.Content
{
    internal static class RxGearRig
    {
        // ------------------------------------------------------------------------------------------------ small math (managed)

        public static Vector3 Pos(Matrix4x4 m) => new Vector3(m.m03, m.m13, m.m23);
        public static Matrix4x4 Translation(Vector3 t) { var m = Matrix4x4.identity; m.m03 = t.x; m.m13 = t.y; m.m23 = t.z; return m; }
        static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        static float Len(Vector3 a) => (float)Math.Sqrt(Dot(a, a));
        static Vector3 Norm(Vector3 a) { float l = Len(a); return l > 1e-9f ? new Vector3(a.x / l, a.y / l, a.z / l) : new Vector3(0, 1, 0); }

        /// The smallest rotation that turns unit a onto unit b (Rodrigues).
        public static Matrix4x4 FromTo(Vector3 a, Vector3 b)
        {
            a = Norm(a); b = Norm(b);
            var v = Cross(a, b); float c = Dot(a, b), s = Len(v);
            var m = Matrix4x4.identity;
            if (s < 1e-6f)
            {
                if (c > 0) return m;
                // opposite: half a turn about any axis across a
                var axis = Norm(Cross(a, Math.Abs(a.x) < 0.9f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0)));
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = 2f * axis[i] * axis[j] - (i == j ? 1f : 0f);
                return m;
            }
            var k = new Vector3(v.x / s, v.y / s, v.z / s); float t = 1f - c;
            m.m00 = c + k.x * k.x * t; m.m01 = k.x * k.y * t - k.z * s; m.m02 = k.x * k.z * t + k.y * s;
            m.m10 = k.y * k.x * t + k.z * s; m.m11 = c + k.y * k.y * t; m.m12 = k.y * k.z * t - k.x * s;
            m.m20 = k.z * k.x * t - k.y * s; m.m21 = k.z * k.y * t + k.x * s; m.m22 = c + k.z * k.z * t;
            return m;
        }

        /// A stretch by s along unit d (1 across it).
        static Matrix4x4 Stretch(Vector3 d, float s)
        {
            var m = Matrix4x4.identity;
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = (i == j ? 1f : 0f) + (s - 1f) * d[i] * d[j];
            return m;
        }

        /// The rotation part of a node matrix with its columns normalized (a node may carry a scale).
        static Matrix4x4 Rot(Matrix4x4 w)
        {
            var m = Matrix4x4.identity;
            for (int c = 0; c < 3; c++) { var v = Norm(new Vector3(w[0, c], w[1, c], w[2, c])); m[0, c] = v.x; m[1, c] = v.y; m[2, c] = v.z; }
            return m;
        }
        static Matrix4x4 Transpose3(Matrix4x4 r) { var m = Matrix4x4.identity; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = r[j, i]; return m; }

        /// The inverse of an affine matrix (a general 3x3 with a translation).
        public static Matrix4x4 AffineInverse(Matrix4x4 m)
        {
            float a = m.m00, b = m.m01, c = m.m02, d = m.m10, e = m.m11, f = m.m12, g = m.m20, h = m.m21, i = m.m22;
            float A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g, det = a * A + b * B + c * C;
            var o = Matrix4x4.identity;
            if (Math.Abs(det) < 1e-12f) return o;
            float id = 1f / det;
            o.m00 = A * id; o.m01 = -(b * i - c * h) * id; o.m02 = (b * f - c * e) * id;
            o.m10 = B * id; o.m11 = (a * i - c * g) * id; o.m12 = -(a * f - c * d) * id;
            o.m20 = C * id; o.m21 = -(a * h - b * g) * id; o.m22 = (a * e - b * d) * id;
            var t = Pos(m);
            o.m03 = -(o.m00 * t.x + o.m01 * t.y + o.m02 * t.z); o.m13 = -(o.m10 * t.x + o.m11 * t.y + o.m12 * t.z); o.m23 = -(o.m20 * t.x + o.m21 * t.y + o.m22 * t.z);
            return o;
        }

        // ------------------------------------------------------------------------------------------------ the fit

        public sealed class Fit
        {
            /// Per Alive node: the Reflex node that drives it (-1: none found; the root is used) and its rest alignment G.
            public int[] driver; public Matrix4x4[] G;
            /// The alignment of the head region (helmets, goggles) and of the neck base (neck braces), Alive's rest -> Reflex's.
            public Matrix4x4 head = Matrix4x4.identity, neck = Matrix4x4.identity;
        }

        static int Index(string[] names, string n) { for (int i = 0; i < names.Length; i++) if (string.Equals(names[i], n, StringComparison.OrdinalIgnoreCase)) return i; return -1; }

        /// The forearm's extra stretch (from the elbow). An Alive suit's sleeve ends at 0.695 of Alive's forearm (measured: every suit shares
        /// one mesh): below it Alive draws its rider's bare forearm and a long glove cuff, neither of which is drawn here. Reflex's own sleeve
        /// ends at 0.877 of its forearm, where Reflex's wrist skin (0.83 .. 1.02) and gloves (from 1.0) take over. So the sleeve is drawn
        /// out to Reflex's sleeve end: without it there was a gap of bare air between the sleeve and the glove.
        public const float SleeveReach = 0.877f / 0.695f;

        /// The fit of Alive's rest skeleton (names, world matrices) to Reflex's.
        public static Fit Make(string[] an, Matrix4x4[] aw, string[] rn, Matrix4x4[] rw)
        {
            var fit = new Fit { driver = new int[an.Length], G = new Matrix4x4[an.Length] };
            bool A(string n, out Vector3 p) { int i = Index(an, n); p = i >= 0 ? Pos(aw[i]) : default; return i >= 0; }
            bool R(string n, out Vector3 p) { int i = Index(rn, n); p = i >= 0 ? Pos(rw[i]) : default; return i >= 0; }
            int rootR = Math.Max(0, Index(rn, "MXRider_Root"));

            // the torso's anchors: hips, shoulders, neck base (Alive height -> the translation there)
            var anchors = new List<(float y, Vector3 d)>();
            void Anchor(params string[] names)
            {
                Vector3 pa = default, pr = default; int n = 0;
                foreach (string s in names) if (A(s, out var x) && R(s, out var y)) { pa += x; pr += y; n++; }
                if (n > 0) anchors.Add((pa.y / n, (pr - pa) / n));
            }
            Anchor("MXRider_RLegA", "MXRider_LLegA"); Anchor("MXRider_RArmA", "MXRider_LArmA"); Anchor("MXRider_Neck");
            anchors.Sort((x, y) => x.y.CompareTo(y.y));
            Vector3 Delta(float y)
            {
                if (anchors.Count == 0) return default;
                if (y <= anchors[0].y) return anchors[0].d;
                for (int i = 1; i < anchors.Count; i++)
                    if (y <= anchors[i].y) { float t = (y - anchors[i - 1].y) / Math.Max(1e-5f, anchors[i].y - anchors[i - 1].y); return anchors[i - 1].d + (anchors[i].d - anchors[i - 1].d) * t; }
                return anchors[anchors.Count - 1].d;
            }

            // a limb segment: Alive's start -> end laid on Reflex's, stretched to its length
            bool Segment(string start, string end, out Matrix4x4 g, out Matrix4x4 rot, float reach = 1f)
            {
                g = rot = Matrix4x4.identity;
                if (!A(start, out var ps) || !A(end, out var pe) || !R(start, out var qs) || !R(end, out var qe)) return false;
                Vector3 da = pe - ps, dr = qe - qs; float la = Len(da), lr = Len(dr);
                if (la < 1e-4f || lr < 1e-4f) return false;
                rot = FromTo(da, dr);
                g = Translation(qs) * Stretch(Norm(dr), lr / la * reach) * rot * Translation(-ps);
                return true;
            }

            var segG = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase); var segRot = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            foreach (string side in new[] { "R", "L" })
            {
                void Seg(string a, string b, float reach = 1f) { if (Segment("MXRider_" + side + a, "MXRider_" + side + b, out var g, out var r, reach)) { segG["MXRider_" + side + a] = g; segRot["MXRider_" + side + a] = r; } }
                Seg("Collar", "ArmA"); Seg("ArmA", "ArmB"); Seg("ArmB", "Wrist", SleeveReach); Seg("LegA", "LegB"); Seg("LegB", "Foot");
            }

            for (int i = 0; i < an.Length; i++)
            {
                string n = an[i]; Vector3 p = Pos(aw[i]);
                string side = n.StartsWith("MXRider_R", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("MXRider_Root", StringComparison.OrdinalIgnoreCase) ? "R" : n.StartsWith("MXRider_L", StringComparison.OrdinalIgnoreCase) ? "L" : null;
                string rest = side != null ? n.Substring(9) : null;   // after "MXRider_R" / "MXRider_L"
                string drive = n; Matrix4x4 g = Translation(Delta(p.y));
                if (side != null && segG.TryGetValue(n, out var sg)) g = sg;                                                         // collar, arms, legs
                else if (side != null && rest == "Collar_Flap" && segG.TryGetValue("MXRider_" + side + "Collar", out var cg)) g = cg;
                else if (side != null && rest.StartsWith("Arm_Flap", StringComparison.OrdinalIgnoreCase) && segG.TryGetValue("MXRider_" + side + "ArmA", out var ag)) g = ag;
                else if (side != null && (rest == "Wrist" || rest.StartsWith("Finger", StringComparison.OrdinalIgnoreCase)))
                {   // the hand: the forearm's rotation about the wrist
                    drive = "MXRider_" + side + "Wrist";
                    if (segRot.TryGetValue("MXRider_" + side + "ArmB", out var fr) && A(drive, out var pw) && R(drive, out var qw)) g = Translation(qw) * fr * Translation(-pw);
                }
                else if (side != null && rest == "Foot")
                {   // the foot: the two rigs' own frames (they agree on the feet)
                    int ri = Index(rn, n);
                    if (ri >= 0) g = Translation(Pos(rw[ri])) * Rot(rw[ri]) * Transpose3(Rot(aw[i])) * Translation(-p);
                }
                else
                {   // the torso: who carries it in Reflex
                    switch (n.ToLowerInvariant())
                    {
                        case "mxrider_pelvis": case "rider": case "gloves": case "boots": drive = "MXRider_Root"; break;
                        case "mxrider_spinec": case "mxrider_spined": drive = "MXRider_Chest"; break;
                        case "mxrider_neckb": drive = "MXRider_Neck"; break;
                        // a neck brace rides the chest: every clip of the game leaves NeckBrace at the chest's frame (local identity under
                        // MXRider_Chest), not at MXRider_Hierarchy's bind NeckBrace (turned 90 degrees: ReflexRiderModel.Build's note on the
                        // game's own braces), so driven by that node's bind the brace hung in the air beside the rider's arm
                        case "neckbrace": drive = "MXRider_Chest"; break;
                    }
                }
                int d = Index(rn, drive);
                fit.driver[i] = d >= 0 ? d : rootR;
                fit.G[i] = g;
            }
            if (A("MXRider_Neck", out var an2)) fit.neck = Translation(Delta(an2.y));
            fit.head = fit.neck;
            return fit;
        }

        /// The head alignment from two reference boxes (a helmet's bounds in each rest space: min, max): centre on centre, sized by the boxes' heights
        /// (kept within 15 %).
        public static Matrix4x4 Head(Vector3 aMin, Vector3 aMax, Vector3 rMin, Vector3 rMax)
        {
            Vector3 ca = (aMin + aMax) * 0.5f, cr = (rMin + rMax) * 0.5f;
            float ha = aMax.y - aMin.y, hr = rMax.y - rMin.y, s = ha > 1e-4f ? hr / ha : 1f;
            s = Math.Max(0.85f, Math.Min(1.15f, s));
            var S = Matrix4x4.identity; S.m00 = S.m11 = S.m22 = s;
            return Translation(cr) * S * Translation(-ca);
        }
        // ------------------------------------------------------------------------------------------------ the size (2026-11-16)
        //
        // The fit above lays Alive's joints on Reflex's, so a suit is as tall as Reflex's rider and its sleeves and legs as long. Its girth
        // stayed Alive's, and Alive's rider is the bulkier one: measured in Reflex's rest space against the stock jersey (Jerseys_BaseModel)
        // an Alive suit's upper arm is 1.1 - 1.45 times as thick, its forearm 1.2 - 1.25, its shin 1.15 - 1.2, its waist 1.1 - 1.25, its
        // shoulders 1.18 times as wide (square: Alive's rider stands in a T pose), and an Alive boot's sole hangs 0.14 ft under Reflex's
        // (Alive's ankle joint sits 0.38 ft over its sole, Reflex's 0.25). On a Reflex bike the rider looked a size too big for it. So a
        // piece is also SHAPED to Reflex's rider:
        //   a suit    its OUTLINE laid on the stock jersey's. The body is nine segments of Reflex's rest skeleton (Frame: the trunk's
        //             upright line from under the hips to the neck; upper arm, forearm, thigh and shin of each side, joint to joint and a
        //             little past the upper joint: the shoulder cap, the hip). Each segment is cut in steps along its line and in sectors
        //             around it; in each cell the outline is the outer radius (OuterQuantile) of the vertices nearest that segment (Body:
        //             by geometry, not by bone: the two rigs' weights are painted differently). A vertex is moved across its segment's
        //             line by the ratio of the stock jersey's outline to the suit's there (Shape: between the cells' middles, never
        //             larger than it was, never under MinScale of it), blended over the segments near it.
        //   boots     each boot's box laid on the stock boot's of its side (the top stays on the shin, the sole comes up to Reflex's).
        // The shape is a function of the FITTED position P (Reflex's rest) and is written back into the mesh's own space through the
        // vertex's blended alignment (P = M p, M = sum w G: the new p is M^-1 P'), so the skinning above is unchanged. Helmets, goggles and
        // neck braces are not shaped (the head alignment sizes them).

        /// REFLEX_RXGEAR_SHAPE=0 (tests: the fit before 2026-11-16): the pieces keep Alive's girth.
        public static bool ShapeToReflex = Environment.GetEnvironmentVariable("REFLEX_RXGEAR_SHAPE") != "0";
        public const float MinScale = 0.6f, MaxScale = 1f, OuterQuantile = 0.85f;
        public const int Segments = 9;   // 0 the trunk, 1 .. 4 the right upper arm, forearm, thigh, shin, 5 .. 8 the left ones
        static readonly string[] LimbStart = { "ArmA", "ArmB", "LegA", "LegB" }, LimbEnd = { "ArmB", "Wrist", "LegB", "Foot" };
        static readonly float[] LimbGirth = { 0.25f, 0.18f, 0.33f, 0.25f };   // a segment's usual radius (feet): how near a vertex is to it, in its own measure

        /// Reflex's rest skeleton as the shape reads it: each segment's line (a + d t len, t in tMin .. tMax), the two directions across
        /// it the sectors are counted from, and its steps and sectors.
        public sealed class Frame
        {
            public readonly Vector3[] a = new Vector3[Segments], d = new Vector3[Segments], u = new Vector3[Segments], v = new Vector3[Segments];
            public readonly float[] len = new float[Segments], girth = new float[Segments], tMin = new float[Segments], tMax = new float[Segments];
            public readonly int[] steps = new int[Segments], sectors = new int[Segments];
            public bool ok;
            public Frame(string[] rn, Matrix4x4[] rw)
            {
                ok = true;
                bool P(string n, out Vector3 p) { int i = Index(rn, n); p = i >= 0 ? Pos(rw[i]) : default; if (i < 0) ok = false; return i >= 0; }
                // the trunk: upright through the middle of hips and neck, from under the crotch (a quarter of the trunk under the hips) to the neck base
                if (P("MXRider_RLegA", out var hip) & P("MXRider_Neck", out var neck))
                {
                    float y0 = hip.y - (neck.y - hip.y) * 0.25f;
                    a[0] = new Vector3(0f, y0, (hip.z + neck.z) * 0.5f); d[0] = new Vector3(0, 1, 0); len[0] = neck.y - y0; u[0] = new Vector3(1, 0, 0); v[0] = new Vector3(0, 0, 1);
                    girth[0] = 0.5f; tMin[0] = 0f; tMax[0] = 1f; steps[0] = 8; sectors[0] = 8;
                    if (len[0] < 1e-3f) ok = false;
                }
                for (int k = 1; k < Segments; k++)
                {
                    int limb = (k - 1) % 4; string side = k <= 4 ? "R" : "L";
                    if (!P("MXRider_" + side + LimbStart[limb], out var s) | !P("MXRider_" + side + LimbEnd[limb], out var e)) continue;
                    a[k] = s; len[k] = Len(e - s); d[k] = Norm(e - s); if (len[k] < 1e-4f) { ok = false; continue; }
                    var up = Math.Abs(d[k].y) < 0.9f ? new Vector3(0, 1, 0) : new Vector3(0, 0, 1);
                    u[k] = Norm(up - d[k] * Dot(up, d[k])); v[k] = Cross(d[k], u[k]);
                    girth[k] = LimbGirth[limb]; tMin[k] = limb == 0 || limb == 2 ? -0.3f : 0f; tMax[k] = 1f; steps[k] = 4; sectors[k] = 4;   // (an upper arm / thigh reaches past its joint: the shoulder cap, the hip)
                }
            }

            /// p against segment k: along its line (t), across it (radius, angle 0 .. 1 of a turn from u towards v) and how far it is from
            /// the segment in the segment's own girths (from the line's part tMin .. tMax).
            public void At(int k, Vector3 p, out float t, out float radius, out float turn, out float near)
            {
                var q = p - a[k]; float along = Dot(q, d[k]); t = along / len[k];
                var across = q - d[k] * along; radius = Len(across);
                turn = (float)(Math.Atan2(Dot(across, v[k]), Dot(across, u[k])) / (2 * Math.PI)); if (turn < 0f) turn += 1f;
                float tc = Math.Max(tMin[k], Math.Min(tMax[k], t));
                near = Len(q - d[k] * (tc * len[k])) / girth[k];
            }
            /// Whether segment k is one of p's side (the trunk: always; a limb: not across the body's middle).
            public bool Side(int k, Vector3 p) => k == 0 || (k <= 4 ? p.x > -0.02f : p.x < 0.02f);
            public int Cells(int k) => steps[k] * sectors[k];
        }

        /// A body's outline in Reflex's rest space: per segment, step and sector the outer radius of the vertices nearest that segment
        /// (NaN: too few there), and per segment and step the outer radius all around.
        public sealed class Body
        {
            public readonly float[][] radius = new float[Segments][], ring = new float[Segments][];
            readonly List<float>[][] cell = new List<float>[Segments][], all = new List<float>[Segments][];
            readonly Frame f;
            public Body(Frame frame)
            {
                f = frame;
                for (int k = 0; k < Segments; k++)
                {
                    radius[k] = new float[f.Cells(k)]; ring[k] = new float[f.steps[k]];
                    cell[k] = new List<float>[f.Cells(k)]; all[k] = new List<float>[f.steps[k]];
                    for (int i = 0; i < cell[k].Length; i++) cell[k][i] = new List<float>();
                    for (int i = 0; i < all[k].Length; i++) all[k][i] = new List<float>();
                }
            }
            public void Add(Vector3 p)
            {
                int best = -1; float bn = float.MaxValue, bt = 0, br = 0, ba = 0;
                for (int k = 0; k < Segments; k++)
                {
                    if (f.len[k] <= 0f || !f.Side(k, p)) continue;
                    f.At(k, p, out float t, out float r, out float turn, out float near);
                    if (near < bn) { bn = near; best = k; bt = t; br = r; ba = turn; }
                }
                if (best < 0 || bn > 3f) return;
                float x = (bt - f.tMin[best]) / (f.tMax[best] - f.tMin[best]); if (x < 0f || x >= 1f) return;
                int step = (int)(x * f.steps[best]), sector = (int)(ba * f.sectors[best] + 0.5f) % f.sectors[best];
                cell[best][step * f.sectors[best] + sector].Add(br); all[best][step].Add(br);
            }
            static float Outer(List<float> l, int least) { if (l.Count < least) return float.NaN; l.Sort(); return l[Math.Min(l.Count - 1, (int)(OuterQuantile * l.Count))]; }
            public Body Done()
            {
                for (int k = 0; k < Segments; k++)
                {
                    for (int i = 0; i < radius[k].Length; i++) radius[k][i] = Outer(cell[k][i], 3);
                    for (int i = 0; i < ring[k].Length; i++) ring[k][i] = Outer(all[k][i], 6);
                }
                return this;
            }
        }

        /// What turns a piece's outline into the reference's: per segment, step and sector the scale across the segment's line.
        public sealed class Shape
        {
            readonly Frame f;
            public readonly float[][] scale = new float[Segments][];

            public Shape(Frame frame, Body reference, Body piece)
            {
                f = frame;
                for (int k = 0; k < Segments; k++)
                {
                    int ns = f.sectors[k], nt = f.steps[k]; var s = scale[k] = new float[nt * ns];
                    // a cell's ratio; without a measure in it, its step's all around; without that, the nearest step's that has one; else 1
                    var ring = new float[nt];
                    for (int i = 0; i < nt; i++) ring[i] = piece.ring[k][i] > 1e-3f ? reference.ring[k][i] / piece.ring[k][i] : float.NaN;
                    for (int i = 0; i < nt; i++)
                    {
                        float r = float.NaN;
                        for (int o = 0; o < nt && float.IsNaN(r); o++) { if (i - o >= 0 && !float.IsNaN(ring[i - o])) r = ring[i - o]; else if (i + o < nt && !float.IsNaN(ring[i + o])) r = ring[i + o]; }
                        if (float.IsNaN(r)) r = 1f;
                        for (int j = 0; j < ns; j++)
                        {
                            float c = piece.radius[k][i * ns + j] > 1e-3f ? reference.radius[k][i * ns + j] / piece.radius[k][i * ns + j] : float.NaN;
                            s[i * ns + j] = Math.Max(MinScale, Math.Min(MaxScale, float.IsNaN(c) ? r : c));
                        }
                    }
                    // evened along the line and around it (1-2-1 each way)
                    var e = new float[s.Length];
                    for (int i = 0; i < nt; i++) for (int j = 0; j < ns; j++)
                    {
                        float along = (s[Math.Max(0, i - 1) * ns + j] + 2f * s[i * ns + j] + s[Math.Min(nt - 1, i + 1) * ns + j]) * 0.25f;
                        float around = (s[i * ns + (j + ns - 1) % ns] + 2f * s[i * ns + j] + s[i * ns + (j + 1) % ns]) * 0.25f;
                        e[i * ns + j] = (along + around) * 0.5f;
                    }
                    Array.Copy(e, s, s.Length);
                }
            }

            /// Segment k's scale at t along it and `turn` around it (between the cells' middles).
            float At(int k, float t, float turn)
            {
                int ns = f.sectors[k], nt = f.steps[k]; var s = scale[k];
                float x = (t - f.tMin[k]) / (f.tMax[k] - f.tMin[k]) * nt - 0.5f; x = Math.Max(0f, Math.Min(nt - 1, x));
                int i0 = Math.Min(nt - 1, (int)x), i1 = Math.Min(nt - 1, i0 + 1); float fx = x - i0;
                float y = turn * ns; int j0 = (int)y % ns, j1 = (j0 + 1) % ns; float fy = y - (int)y;
                float lo = s[i0 * ns + j0] + (s[i0 * ns + j1] - s[i0 * ns + j0]) * fy, hi = s[i1 * ns + j0] + (s[i1 * ns + j1] - s[i1 * ns + j0]) * fy;
                return lo + (hi - lo) * fx;
            }

            /// Where a vertex at p (Reflex's rest) goes: across each segment near it by that segment's scale there, blended by nearness.
            public Vector3 Move(Vector3 p)
            {
                Span<float> near = stackalloc float[Segments]; Span<float> t = stackalloc float[Segments]; Span<float> turn = stackalloc float[Segments];
                float least = float.MaxValue;
                for (int k = 0; k < Segments; k++)
                {
                    near[k] = float.MaxValue;
                    if (f.len[k] <= 0f || !f.Side(k, p)) continue;
                    f.At(k, p, out t[k], out _, out turn[k], out near[k]);
                    if (near[k] < least) least = near[k];
                }
                if (least > 3f) return p;
                Vector3 to = default; float sum = 0f;
                for (int k = 0; k < Segments; k++)
                {
                    float w = near[k] == float.MaxValue ? 0f : (float)Math.Exp(-(near[k] - least) * 6f); if (w < 1e-3f) continue;
                    var on = f.a[k] + f.d[k] * (t[k] * f.len[k]);
                    to += (on + (p - on) * At(k, t[k], turn[k])) * w; sum += w;
                }
                return sum > 0f ? to / sum : p;
            }

            public string Note(int k)
            {
                int ns = f.sectors[k], nt = f.steps[k]; var parts = new string[nt];
                for (int i = 0; i < nt; i++) { float m = 0; for (int j = 0; j < ns; j++) m += scale[k][i * ns + j]; parts[i] = (m / ns).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }
                return string.Join(" ", parts);
            }
        }

        /// A boot's vertex: its side's box (lo .. hi) laid on the reference's (rl .. rh), each axis within 0.8 .. 1.15; the width and
        /// length about the middle, the height from the top down.
        public static Vector3 MoveBoot(Vector3 p, Vector3[] bootMin, Vector3[] bootMax, Vector3[] refMin, Vector3[] refMax)
        {
            int s = p.x >= 0f ? 0 : 1;
            Vector3 lo = bootMin[s], hi = bootMax[s], rl = refMin[s], rh = refMax[s];
            float K(float a, float b) => a > 1e-3f ? Math.Max(0.8f, Math.Min(1.15f, b / a)) : 1f;
            float kx = K(hi.x - lo.x, rh.x - rl.x), ky = K(hi.y - lo.y, rh.y - rl.y), kz = K(hi.z - lo.z, rh.z - rl.z);
            float cx = (lo.x + hi.x) * 0.5f, cz = (lo.z + hi.z) * 0.5f;
            return new Vector3((rl.x + rh.x) * 0.5f + (p.x - cx) * kx, rh.y + (p.y - hi.y) * ky, (rl.z + rh.z) * 0.5f + (p.z - cz) * kz);
        }

        /// The boxes of a pair of boots (0: x > 0, 1: x < 0); false: a side has no vertex.
        public static bool BootBoxes(IEnumerable<Vector3> points, out Vector3[] min, out Vector3[] max)
        {
            min = new[] { new Vector3(1e9f, 1e9f, 1e9f), new Vector3(1e9f, 1e9f, 1e9f) }; max = new[] { new Vector3(-1e9f, -1e9f, -1e9f), new Vector3(-1e9f, -1e9f, -1e9f) };
            var n = new int[2];
            foreach (var p in points)
            {
                int s = p.x >= 0f ? 0 : 1; n[s]++;
                min[s] = new Vector3(Math.Min(min[s].x, p.x), Math.Min(min[s].y, p.y), Math.Min(min[s].z, p.z)); max[s] = new Vector3(Math.Max(max[s].x, p.x), Math.Max(max[s].y, p.y), Math.Max(max[s].z, p.z));
            }
            return n[0] > 0 && n[1] > 0;
        }

        static Vector3 Mul(Matrix4x4 m, Vector3 v) => new Vector3(m.m00 * v.x + m.m01 * v.y + m.m02 * v.z + m.m03, m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13, m.m20 * v.x + m.m21 * v.y + m.m22 * v.z + m.m23);

        /// A skinned surface's vertices in Reflex's rest space (sum w G p).
        public static Vector3[] Fitted(Vector3[] positions, int[] bones, float[] weights, Matrix4x4[] G)
        {
            int n = positions.Length; var o = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 acc = default; float tw = 0f;
                for (int k = 0; k < 4; k++)
                {
                    int b = bones[i * 4 + k]; float w = weights[i * 4 + k]; if (b < 0 || w <= 0f) continue;
                    acc += Mul(G[b], positions[i]) * w; tw += w;
                }
                o[i] = tw > 1e-6f ? acc / tw : positions[i];
            }
            return o;
        }

        /// A skinned surface's positions (the mesh's own space) shaped: `move` is where a fitted position goes; the result is taken back
        /// through the vertex's blended alignment.
        public static Vector3[] Shaped(Vector3[] positions, int[] bones, float[] weights, Matrix4x4[] G, Func<Vector3, Vector3> move)
        {
            int n = positions.Length; var o = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var M = new Matrix4x4(); float tw = 0f;
                for (int k = 0; k < 4; k++)
                {
                    int b = bones[i * 4 + k]; float w = weights[i * 4 + k]; if (b < 0 || w <= 0f) continue;
                    for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) M[r, c] += G[b][r, c] * w;
                    tw += w;
                }
                if (tw <= 1e-6f) { o[i] = positions[i]; continue; }
                for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) M[r, c] /= tw;
                M.m33 = 1f;
                var P = Mul(M, positions[i]); var to = move(P);
                o[i] = (to - P).sqrMagnitude < 1e-12f ? positions[i] : Mul(AffineInverse(M), to);
            }
            return o;
        }
    }
}
