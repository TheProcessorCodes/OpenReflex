// Where Reflex's own start gate stands on an imported Unleashed race level: port of tools/unleashed/startgate.py.
//   startgate.rxgate   'RXSG' u32 version (1), f32 x, y, z (the drop bar's hinge line, level space), f32 dx, dy (0), dz (riding direction)
// A national's gate is the .scn's NatStartGate / NatDropGate, a supercross' THQsxgate / THQsxdropgate (and the *Split pair). Supermoto
// has no gate in the source: the bar goes 39 ft down the start chute of race.rxrace. The ground under the gate is read from the level's
// own terrain.rxheight, in single precision as numpy 2 computes it.
using System;
using System.Collections.Generic;
using Reflex.Port.Content.AliveImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnStartGate
    {
        public static readonly string[] Gates = { "natstartgate", "thqsxgate", "thqsxgatesplit" };
        public static readonly string[] Drops = { "natdropgate", "thqsxdropgate", "thqsxdropgatesplit" };
        public static readonly string[] SourceModels = { "natstartgate", "thqsxgate", "thqsxgatesplit", "natdropgate", "thqsxdropgate", "thqsxdropgatesplit" };
        const double Lift = 0.75;   // ft: the gate's steel base stands on the dirt

        /// races(track): the levels that race from a gate - supercross, nationals, supermoto.
        public static bool Races(string track) => track.StartsWith("SX", StringComparison.Ordinal) || track.StartsWith("Nat", StringComparison.Ordinal) || track.StartsWith("SM", StringComparison.Ordinal);

        /// ground(folder): the height sampler of terrain.rxheight (numpy float32 arithmetic with the weights rounded to single).
        sealed class Ground
        {
            readonly byte[] _raw; readonly double _spacing, _half;
            public Ground(byte[] raw)
            {
                if (raw == null || raw.Length < 40 + 2048 * 2048 * 4) throw new UnValueError("terrain.rxheight is not the level's height grid");
                _raw = raw; _spacing = BitConverter.ToSingle(raw, 12); _half = 1023.5 * _spacing;
            }
            float G(int iz, int ix) => BitConverter.ToSingle(_raw, 40 + (iz * 2048 + ix) * 4);

            /// The grid values are numpy float32 scalars. A weight that comes from the position is a float64 scalar, so the products
            /// are double; a weight of a clamped coordinate (Python's own 2046.999 or 0) is a Python number, which numpy 2 rounds to
            /// single first and then multiplies in single precision.
            public double Sample(double x, double z)
            {
                double vx = (x + _half) / _spacing, vz = (_half - z) / _spacing;
                if (double.IsNaN(vx) || double.IsNaN(vz)) throw new UnValueError("cannot convert float NaN to integer");
                bool strongX = vx > 0 && vx < 2046.999, strongZ = vz > 0 && vz < 2046.999;   // min(2046.999, max(0, v)) kept the numpy value
                double gx = vx > 0 ? (vx < 2046.999 ? vx : 2046.999) : 0, gz = vz > 0 ? (vz < 2046.999 ? vz : 2046.999) : 0;
                int ix = (int)gx, iz = (int)gz; double fx = gx - ix, fz = gz - iz;
                double Row(int row, out bool single)
                {
                    float a = G(row, ix), b = G(row, ix + 1);
                    if (strongX) { single = false; return (double)a * (1 - fx) + (double)b * fx; }
                    single = true;
                    return (float)((double)(float)((double)a * (float)(1 - fx)) + (float)((double)b * (float)fx));
                }
                double Weigh(double v, bool single, double w, out bool outSingle)
                {
                    if (!single || strongZ) { outSingle = false; return v * w; }
                    outSingle = true; return (float)(v * (float)w);
                }
                double top = Weigh(Row(iz, out bool s0), s0, 1 - fz, out bool t0), bottom = Weigh(Row(iz + 1, out bool s1), s1, fz, out bool t1);
                return t0 && t1 ? (float)(top + bottom) : top + bottom;
            }
        }

        /// seat(x, z, look, sample): the bar's height - the gate's base clear of the highest ground under it.
        static double Seat(double x, double z, double[] look, Ground g)
        {
            double sx = look[2], sz = -look[0], top = -1e9;
            for (int a = 0; a < 42; a++)
            {
                double across = a == 41 ? 41.0 : a * 2.0 + -41.0;           // np.linspace(-41, 41, 42)
                for (int b = 0; b < 6; b++)
                {
                    double along = b == 5 ? 2.0 : b * (11.0 / 5) + -9.0;    // np.linspace(-9, 2, 6)
                    double v = g.Sample(x + sx * across + look[0] * along, z + sz * across + look[2] * along);
                    if (v > top) top = v;
                }
            }
            return top + Lift;
        }

        /// write(folder, scene_text, center, track): the report entry (level.json "start_gate"), or null; file = startgate.rxgate or null.
        public static JObj Write(string sceneText, double cx, double cz, string track, byte[] terrainRxheight, byte[] race, out byte[] file)
        {
            file = null;
            if (!Races(track) || race == null) return null;
            var scn = UnIni.Parse(sceneText, true);
            UnIniSection gate = null, drop = null;
            foreach (var obj in scn.Sections)
            {
                string name = UnIni.Lower(UnIni.Strip(obj.Get("slt") ?? ""));
                if (!UnIni.Lower(obj.Name).StartsWith("model", StringComparison.Ordinal) || !obj.Has("position")) continue;
                if (Array.IndexOf(Gates, name) >= 0 && gate == null) gate = obj;
                if (Array.IndexOf(Drops, name) >= 0 && drop == null) drop = obj;
            }
            double[] at, look; string why; bool numpyXz;
            if (gate != null)
            {
                var p = UnNum.Floats((drop ?? gate)["position"] ?? throw new UnValueError("Unleashed scene: empty gate position"));
                look = UnNum.Floats(gate.Get("lookvector", "0,0,1") ?? throw new UnValueError("Unleashed scene: empty gate LookVector"));
                if (p.Length < 3 || look.Length < 3) throw new UnValueError("Unleashed scene: the start gate's position or look vector is incomplete");
                look = new[] { look[0], 0.0, look[2] };   // (a longer vector would fail in Python's norm of the same length only; 3 are stored)
                double n = UnNum.Norm3(look[0], look[1], look[2]);
                if (n < .01) return null;
                look[0] /= n; look[1] /= n; look[2] /= n;
                double x = p[0] - cx, z = p[2] - cz;
                at = new[] { x, Seat(x, z, look, new Ground(terrainRxheight)), z }; why = "the source gate " + gate["slt"]; numpyXz = true;
            }
            else
            {
                if (race.Length < 100 || race[0] != 'R' || race[1] != 'X' || race[2] != 'R' || race[3] != 'A') return null;
                var g = new double[6]; for (int i = 0; i < 6; i++) g[i] = BitConverter.ToSingle(race, 20 + i * 4);
                look = new[] { g[3], 0.0, g[5] };
                double n = UnNum.Norm3(look[0], look[1], look[2]);
                if (n < .01) return null;
                look[0] /= n; look[1] /= n; look[2] /= n;
                double x = g[0] + look[0] * 39, z = g[2] + look[2] * 39;
                at = new[] { x, Seat(x, z, look, new Ground(terrainRxheight)), z }; why = "no source gate: 39 ft down the start chute"; numpyXz = true;
            }
            foreach (double v in at) if (!UnNum.Finite(v)) throw new UnValueError("Invalid start gate position");
            file = new byte[32]; file[0] = (byte)'R'; file[1] = (byte)'X'; file[2] = (byte)'S'; file[3] = (byte)'G'; UnNum.PutI32(file, 4, 1);
            UnNum.PutF32(file, 8, at[0]); UnNum.PutF32(file, 12, at[1]); UnNum.PutF32(file, 16, at[2]); UnNum.PutF32(file, 20, look[0]); UnNum.PutF32(file, 24, 0); UnNum.PutF32(file, 28, look[2]);
            // x and z are numpy float64 scalars in the reference (round = rint(x * 100) / 100); the height is a Python float
            var position = new List<object> { numpyXz ? UnNum.NpRound(at[0], 2) : UnJson.Round(at[0], 2), UnJson.Round(at[1], 2), numpyXz ? UnNum.NpRound(at[2], 2) : UnJson.Round(at[2], 2) };
            return new JObj().Set("position", position).Set("direction", new List<object> { UnJson.Round(look[0], 4), UnJson.Round(look[2], 4) }).Set("source", why);
        }
    }
}
