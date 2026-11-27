using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Reflex.Port.Content
{
    // Reflex's own start gate on an imported MX vs ATV Unleashed race level. The level's scene is the donor's (NAT_Concord) and only its
    // terrain, light and sky are used; with `startgate.rxgate` beside the level's terrain (tools/unleashed/startgate.py: 'RXSG' v1, the
    // drop bar's place and the riding direction where the source's own gate stood) the donor's start gate kit is kept as well and moved
    // there as one rigid group: TO_StartingGate (the gate and its 16 slots), DropGate (the bar the game drops at the start) and the
    // ThirtySecondBoard (the start board girl, drawn by OriginalAmbient from the game's own pose). The core does the same to its
    // entities (core/src/fmt_modtrack.h fmt_modtrack_kit_*), so the race starts from the gate, held by the board: no numeric countdown.
    // The source's gate is left out of the converted scenery for those levels. Without the file a level starts as before.
    internal static class UnleashedStartGate
    {
        static bool Kit(SceneEntity e) => e.Type == "DropGateRSData" || e.Type == "ThirtySecondBoardRSData" || e.Type == "CollisionRSData" && e.String("Name") == "TO_StartingGate";

        /// The bar's place and the riding direction of the bound level, or null (no file, or not an Unleashed level).
        static float[] Pose()
        {
            if (UnleashedLevels.Bound == null) return null;
            string path = Path.Combine(ExperimentalTerrain.Path, "startgate.rxgate");
            if (!RxFiles.Exists(path)) return null;
            var d = RxFiles.ReadAllBytes(path);
            if (d.Length != 32 || BitConverter.ToUInt32(d, 0) != 0x47535852 || BitConverter.ToInt32(d, 4) != 1) throw new InvalidDataException("Invalid Unleashed start gate");
            var p = new float[6]; for (int i = 0; i < 6; i++) { p[i] = BitConverter.ToSingle(d, 8 + 4 * i); if (!float.IsFinite(p[i]) || Mathf.Abs(p[i]) > 100000) throw new InvalidDataException("Invalid Unleashed start gate"); }
            if (Mathf.Abs(Mathf.Sqrt(p[3] * p[3] + p[5] * p[5]) - 1) > .01f || Mathf.Abs(p[4]) > .01f) throw new InvalidDataException("Invalid Unleashed start gate direction");
            return p;
        }

        /// The kit's entities of the donor scene at this level's start (their Matrix replaced), or none.
        internal static List<SceneEntity> Keep(List<SceneEntity> all)
        {
            var kept = new List<SceneEntity>(); var pose = Pose(); if (pose == null) return kept;
            var gate = all.FirstOrDefault(e => e.Type == "CollisionRSData" && Kit(e)); var drop = all.FirstOrDefault(e => e.Type == "DropGateRSData");
            float[] g = gate?.Matrix, d = drop?.Matrix; if (g == null || d == null || g.Length != 16 || d.Length != 16) return kept;
            // row vectors (rows 0..2 the axes, row 3 the translation): m' = m x inverse(from) x to; from = the donor gate's axes at the donor bar
            var from = (float[])g.Clone(); from[12] = d[12]; from[13] = d[13]; from[14] = d[14];
            float x = pose[3], z = pose[5], n = Mathf.Sqrt(x * x + z * z); x /= n; z /= n;
            var to = new float[] { z, 0, -x, 0, 0, 1, 0, 0, x, 0, z, 0, pose[0], pose[1], pose[2], 1 };
            var inv = new float[] { from[0], from[4], from[8], 0, from[1], from[5], from[9], 0, from[2], from[6], from[10], 0, 0, 0, 0, 1 };
            for (int c = 0; c < 3; c++) inv[12 + c] = -(from[12] * inv[c] + from[13] * inv[4 + c] + from[14] * inv[8 + c]);
            float[] Mul(float[] a, float[] b) { var r = new float[16]; for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { float s = 0; for (int k = 0; k < 4; k++) s += a[4 * i + k] * b[4 * k + j]; r[4 * i + j] = s; } return r; }
            bool seenGate = false, seenDrop = false;
            foreach (var e in all)
            {
                if (!Kit(e) || e.Matrix == null || e.Matrix.Length != 16) continue;
                if (e.Type == "CollisionRSData") { if (seenGate) continue; seenGate = true; }
                if (e.Type == "DropGateRSData") { if (seenDrop) continue; seenDrop = true; }
                e.matrixOverride = Mul(Mul(e.Matrix, inv), to); kept.Add(e);
            }
            Debug.Log($"[Unleashed] Reflex start gate: {kept.Count} donor entities at ({pose[0]:0.0}, {pose[1]:0.0}, {pose[2]:0.0}), heading ({x:0.00}, {z:0.00})");
            return kept;
        }
    }
}
