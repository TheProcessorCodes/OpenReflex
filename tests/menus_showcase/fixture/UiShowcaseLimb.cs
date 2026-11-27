// TEST ONLY: RiderLimbRetarget.TwoBone's bend side (the legs folding into a bike mod as the rider stood or leaned back, 2026-11-16).
// Page "limb": a nearly straight leg (hip, knee 0.03 ft forward of the line, foot) whose foot moves to a bike mod's peg (0.13 ft in,
// 0.13 ft up, the Skin Builder bikes' fitted offset), for a sweep of leg stretches and both sides: the knee must stay on its own side
// (forward), not swing sideways. "[limb-test] PASS|FAIL".
using System.Collections;
using Reflex.Port.OriginalWorld;
using UnityEngine;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseLimb
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { UiShowcase.Register("limb", Page); }

        static IEnumerator Page()
        {
            int fails = 0, cases = 0; float worst = 1f, worstSide = 0f;
            var a = new GameObject("limb a").transform; var b = new GameObject("limb b").transform; var c = new GameObject("limb c").transform;
            b.SetParent(a, true); c.SetParent(b, true);
            // (the seated leg first, as every ride starts: the knee well forward shows the hinge's direction)
            a.SetPositionAndRotation(new Vector3(0.35f, 3f, 0f), Quaternion.identity); b.SetPositionAndRotation(new Vector3(0.4f, 1.6f, 0.9f), Quaternion.identity); c.SetPositionAndRotation(new Vector3(0.45f, 0.2f, 0.3f), Quaternion.identity);
            RiderLimbRetarget.TwoBone(a, b, c, c.position + new Vector3(0f, 0.1f, 0f));
            // a nearly straight leg whose knee sits a hundredth of a foot OUTWARD (the knees that snapped out wide): it must bend forward
            foreach (float outward in new[] { 0.005f, 0.01f, 0.03f })
            {
                Vector3 hip = new Vector3(0.35f, 3f, 0f), foot = new Vector3(0.45f, 0.2f, 0.3f), mid = (hip + foot) * 0.5f + Vector3.right * outward + Vector3.forward * 0.002f;
                a.SetPositionAndRotation(hip, Quaternion.identity); b.SetPositionAndRotation(mid, Quaternion.identity); c.SetPositionAndRotation(foot, Quaternion.identity);
                RiderLimbRetarget.TwoBone(a, b, c, foot + new Vector3(-0.13f, 0.13f, -0.04f));
                Vector3 line = (c.position - a.position).normalized, off = (b.position - a.position) - line * Vector3.Dot(b.position - a.position, line);
                float fwd = Vector3.Dot(off.normalized, Vector3.forward);
                cases++;
                if (fwd < 0.85f) { fails++; Debug.Log($"[limb-test] FAIL a straight leg with the knee {outward} ft outward bends {fwd:0.00} forward ({Mathf.Abs(off.x):0.00} ft sideways)"); }
                else Debug.Log($"[limb-test] ok   a straight leg with the knee {outward} ft outward bends {fwd:0.00} forward ({Mathf.Abs(off.x):0.00} ft sideways)");
            }
            foreach (float knee in new[] { 0.01f, 0.03f, 0.08f, 0.2f, 0.5f })
                foreach (float sign in new[] { -1f, 1f })
                    foreach (var move in new[] { new Vector3(0.13f, 0.13f, -0.04f), new Vector3(-0.13f, 0.10f, -0.03f), new Vector3(0.2f, 0.02f, 0f), new Vector3(0f, 0.15f, 0f) })
                    {
                        Vector3 hip = new Vector3(0.35f * sign, 3f, 0f), foot = new Vector3(0.45f * sign, 0.2f, 0.3f);
                        Vector3 mid = (hip + foot) * 0.5f + Vector3.forward * knee;   // the knee forward of the leg's line
                        a.SetPositionAndRotation(hip, Quaternion.identity); b.SetPositionAndRotation(mid, Quaternion.identity); c.SetPositionAndRotation(foot, Quaternion.identity);
                        Vector3 target = foot + new Vector3(move.x * -sign, move.y, move.z);
                        RiderLimbRetarget.TwoBone(a, b, c, target);
                        Vector3 line = (c.position - a.position).normalized, off = (b.position - a.position) - line * Vector3.Dot(b.position - a.position, line);
                        float fwd = Vector3.Dot(off.normalized, Vector3.forward), side = Mathf.Abs(off.x);
                        float limb = Vector3.Distance(hip, mid) + Vector3.Distance(mid, foot);
                        float reach = Vector3.Distance(target, hip) > limb * RiderLimbRetarget.MaxReach ? 0f : Vector3.Distance(c.position, target);   // (past MaxReach the foot stays short by design)
                        cases++;
                        if (fwd < worst) { worst = fwd; worstSide = side; }
                        if (fwd < 0.9f || reach > 0.02f) { fails++; Debug.Log($"[limb-test] FAIL knee {knee} side {sign} move {move}: the knee's bend is {fwd:0.00} forward ({side:0.00} ft sideways), the foot {reach:0.000} ft from its peg"); }
                    }
            Debug.Log($"[limb-test] {(fails == 0 ? "PASS" : "FAIL")} {cases - fails}/{cases} legs keep the knee forward (worst {worst:0.00} forward, {worstSide:0.00} ft sideways)");
            Object.Destroy(a.gameObject);
            yield return null;
        }
    }
}
