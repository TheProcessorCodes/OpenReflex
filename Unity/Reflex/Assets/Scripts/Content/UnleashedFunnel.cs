using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Reflex.Port.Content
{
    // The funnel blockers of an imported MX vs ATV Unleashed race level: the bales (supermoto: barrels) that shape the start. The source's
    // .aty [FunnelBlockDown] / [FunnelBlockUp] name two sets: "down" stands at the start, "up" is placed 20 ft under the ground; when a
    // racer first passes the source's trigger gate the down set sinks and the up set rises (40 ft/s), which closes the lap off the start
    // straight. tools/unleashed/funnel.py puts the sets' meshes in the scenery groups "UnleashedFunnel:down" / "UnleashedFunnel:up" at
    // their source poses and writes funnel.rxfunnel ('RXFN' v1: the gate, then per set the move direction, distance and its collision).
    // The core owns the switch and the collision (rx_session_funnel: 0 the start layout .. 1 raced; a free ride stays at the start
    // layout, as the original's does); here the two groups are moved along their direction by that progress and a set that is fully
    // under the ground is not drawn.
    public sealed class UnleashedFunnel : MonoBehaviour
    {
        Transform[] sets = new Transform[2]; Vector3[] move = new Vector3[2]; float shown = -2f;

        internal static void Attach(Transform root)
        {
            Reflex.Port.RxCore.RxApiNative.FunnelProgress = -1f;
            if (UnleashedLevels.Bound == null) return;
            string path = Path.Combine(ExperimentalTerrain.Path, "funnel.rxfunnel");
            if (!RxFiles.Exists(path)) return;
            var d = RxFiles.ReadAllBytes(path);
            if (d.Length < 44 + 40 || BitConverter.ToUInt32(d, 0) != 0x4e465852 || BitConverter.ToInt32(d, 4) != 1) throw new InvalidDataException("Invalid Unleashed funnel");
            var f = root.gameObject.AddComponent<UnleashedFunnel>(); int at = 44;
            for (int k = 0; k < 2; k++)
            {
                if (at + 20 > d.Length) throw new InvalidDataException("Truncated Unleashed funnel");
                var dir = new Vector3(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8)); float dist = BitConverter.ToSingle(d, at + 12);
                long n = BitConverter.ToUInt32(d, at + 16);
                if (!float.IsFinite(dir.x + dir.y + dir.z + dist) || Mathf.Abs(dir.magnitude - 1) > .01f || dist <= 0 || dist > 200 || n > 60000 || at + 20 + n * 12 > d.Length) throw new InvalidDataException("Invalid Unleashed funnel set");
                f.move[k] = dir * dist; at += 20 + (int)n * 12;
                f.sets[k] = root.Find(k == 0 ? "UnleashedFunnel:down" : "UnleashedFunnel:up");
            }
            f.Apply(0);
            Debug.Log($"[Unleashed] funnel blockers: {(f.sets[0] ? f.sets[0].childCount : 0)} down + {(f.sets[1] ? f.sets[1].childCount : 0)} up meshes (start layout until a racer passes the gate)");
        }

        void LateUpdate() { float p = Reflex.Port.RxCore.RxApiNative.FunnelProgress;   // (the core's progress: 0 .. 1; -1 none)
            Apply(p < 0 ? 0 : Mathf.Clamp01(p)); }

        void Apply(float p)
        {
            if (p == shown) return;
            shown = p;
            for (int k = 0; k < 2; k++)
            {
                if (!sets[k]) continue;
                sets[k].localPosition = move[k] * p;
                // under the ground: down once it has sunk, up until it starts to rise
                bool buried = k == 0 ? p >= 1f : p <= 0f;
                if (sets[k].gameObject.activeSelf == buried) sets[k].gameObject.SetActive(!buried);
            }
        }
    }
}
