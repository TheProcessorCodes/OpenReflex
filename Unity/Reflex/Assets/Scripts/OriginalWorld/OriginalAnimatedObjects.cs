using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    // Scenery objects the original game moves at run time (e.g. the national start gate, DropGate,
    // _re/notes/start-gate.md): each is located once in the running original world by its vtable and
    // entity id, then its world matrix (+matrixOffset, row vectors, native axes = Unity axes) is read
    // every frame on the simulation thread and applied to the exported Unity object.
    [DisallowMultipleComponent]
    public sealed class OriginalAnimatedObjects : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public Transform target;
            public uint vtable;          // e.g. DropGate 0x95fc24
            public uint idOffset;        // dword identifying the instance (entity id at +0x14)
            public uint id;
            public uint matrixOffset;    // world matrix (DropGate +0x30)
        }

        public Entry[] entries = new Entry[0];

        // matrices: entries.Length * 16 floats; valid[i] false when the object was not found.
        public void Apply(float[] matrices, bool[] valid)
        {
            if (matrices == null || valid == null) return;
            if (heldDown)
            {   // (held: the game's latest poses kept for the release)
                if (lateMatrices == null || lateMatrices.Length != matrices.Length) lateMatrices = new float[matrices.Length];
                if (lateValid == null || lateValid.Length != valid.Length) lateValid = new bool[valid.Length];
                Array.Copy(matrices, lateMatrices, matrices.Length); Array.Copy(valid, lateValid, valid.Length);
                haveLate = true;
                return;
            }
            for (int i = 0; i < entries.Length && i < valid.Length; i++)
            {
                if (!valid[i] || !entries[i].target) continue;
                int o = i * 16;
                var x = new Vector3(matrices[o], matrices[o + 1], matrices[o + 2]);
                var y = new Vector3(matrices[o + 4], matrices[o + 5], matrices[o + 6]);
                var z = new Vector3(matrices[o + 8], matrices[o + 9], matrices[o + 10]);
                var p = new Vector3(matrices[o + 12], matrices[o + 13], matrices[o + 14]);
                if (!(z.sqrMagnitude > 1e-10f) || !(y.sqrMagnitude > 1e-10f) || float.IsNaN(p.x) || float.IsInfinity(p.x)) continue;
                entries[i].target.SetPositionAndRotation(p, Quaternion.LookRotation(z.normalized, y.normalized));
            }
        }

        // ---- WATCH GHOST's racing layout (Leaderboards/RxGhostWatch, with OriginalProps.HoldRaceLayout): the start gates drawn down, as
        // they stand once the race is under way. A held watch world stops before the drop (the gate is up through the countdown), so the
        // gate's open pose is drawn here: rx_drop_gate_reset's "down" branch, the rest matrix R (+0x1b0, the scene matrix) turned about its
        // own x axis by the down angle +0x1f0 (0.53 rad, 0x00977d28; _re/notes/start-gate.md): y' = cos a y - sin a z, z' = sin a y + cos a z,
        // the position kept. Drawing only; the release puts back what was drawn and then the game's latest poses.
        public const float GateDownAngle = 0.53f;
        bool heldDown, haveLate; float[] lateMatrices; bool[] lateValid;
        readonly List<(Transform t, Vector3 p, Quaternion r)> heldPoses = new List<(Transform, Vector3, Quaternion)>();

        /// The gates held down (on: rest finds an entry's scene matrix by entity id, 16 floats, null: none; mapScale the level's MAP SCALE,
        /// OriginalLevel.MapScale, as the scene matrix is in the level's own feet and the game's +0x30 in the scaled world) or released. Main thread.
        public int HoldDown(bool on, Func<uint, float[]> rest, float mapScale = 1f)
        {
            if (!on)
            {
                if (!heldDown) return 0;
                heldDown = false;
                foreach (var (t, p, r) in heldPoses) if (t) t.SetPositionAndRotation(p, r);
                heldPoses.Clear();
                if (haveLate) { haveLate = false; Apply(lateMatrices, lateValid); }
                return 0;
            }
            if (heldDown) return heldPoses.Count;
            heldDown = true; haveLate = false;
            float c = Mathf.Cos(GateDownAngle), s = Mathf.Sin(GateDownAngle);
            foreach (var e in entries)
            {
                var t = e.target; var m = t && rest != null ? rest(e.id) : null;
                if (m == null || m.Length < 16) continue;
                var y = new Vector3(m[4], m[5], m[6]); var z = new Vector3(m[8], m[9], m[10]);
                var y2 = c * y - s * z; var z2 = s * y + c * z;
                if (!(y2.sqrMagnitude > 1e-10f) || !(z2.sqrMagnitude > 1e-10f)) continue;
                heldPoses.Add((t, t.position, t.rotation));
                t.SetPositionAndRotation(new Vector3(m[12], m[13], m[14]) * mapScale, Quaternion.LookRotation(z2.normalized, y2.normalized));   // (MAP SCALE: the position only)
            }
            return heldPoses.Count;
        }
    }
}
