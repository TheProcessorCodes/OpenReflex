// MX vs ATV Unleashed track definition (.tdf): port of the reference _re/tools/tdf.py (parse) as the converter uses it.
// A tag stream: one byte tag, then a payload of 0, 4, 8, 12 or 40 bytes (little-endian); unknown tags have no payload. Coordinates are
// "track units": world x = x * scale + x_offset, z likewise, y = y * scale. All arithmetic is Python's (doubles, one rounding per
// operation) on the file's single precision values.
// Not ported: the per-segment lengths and lap_length (the converter does not use them).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Reflex.Port.Content.UnleashedImport
{
    /// Python's ValueError where the converter's control flow depends on it (racing.py reports it as the level's race note).
    public class UnValueError : Exception
    {
        public UnValueError(string message) : base(message) { }
    }

    public sealed class UnTdfPoint
    {
        public int index; public uint flags;
        public double[] centre, left, right;      // null when the point has no such tag (Python: KeyError on use)
    }

    public sealed class UnTdfSection
    {
        public int index, nlinks; public uint flags = 0xc1; public int mask = -1; public double value;
        public readonly List<UnTdfPoint> points = new List<UnTdfPoint>();
        /// links[k] = section index; LinkOrder = the keys in the order they first appear (dict order).
        public readonly Dictionary<int, int> links = new Dictionary<int, int>();
        public readonly List<int> linkOrder = new List<int>();
    }

    public sealed class UnTdfGate
    {
        public double[] pos, normal, across; public double width, height = 150.0;
    }

    public sealed class UnTdf
    {
        public double scale = 1.0, xOffset, zOffset; public uint flags; public int? count; public int pointCount;
        /// Sections in the order their index first appears (Python dict order); a repeated index replaces the earlier section in place.
        public readonly List<UnTdfSection> sections = new List<UnTdfSection>();
        public readonly Dictionary<int, UnTdfSection> byIndex = new Dictionary<int, UnTdfSection>();
        public readonly Dictionary<string, UnTdfGate> gates = new Dictionary<string, UnTdfGate>(StringComparer.Ordinal);
        public readonly Dictionary<string, double> ai = new Dictionary<string, double>(StringComparer.Ordinal);

        public UnTdfGate Gate(string name) => gates.TryGetValue(name, out var g) ? g : null;

        // payload size in bytes by tag; -1 = unknown tag (no payload)
        static int Payload(int tag)
        {
            switch (tag)
            {
                case 0x01: case 0x02: case 0x03: case 0x04: case 0x29: case 0x05: case 0x06: case 0x2b: case 0x07: case 0x18: case 0x0d:
                case 0x17: case 0x0c: case 0x12: case 0x1a: case 0x1b: case 0x2a:
                case 0x13: case 0x14: case 0x15: case 0x16: case 0x28: case 0x2c:
                case 0x21: case 0x1e: case 0x1f: case 0x20: case 0x23: case 0x25: case 0x26: return 4;
                case 0x0e: return 8;
                case 0x09: case 0x0a: case 0x0b: case 0x24: return 12;
                case 0x0f: case 0x10: case 0x19: return 40;
                case 0x08: case 0x1d: case 0x22: return 0;
            }
            return -1;
        }

        static int I32(byte[] b, int o) => b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24;
        static double F(byte[] b, int o) => BitConverter.Int32BitsToSingle(I32(b, o));

        static InvalidDataException Bad(string what) => new InvalidDataException("Unleashed track definition: " + what);

        /// tdf.parse(b).
        public static UnTdf Parse(byte[] b)
        {
            var t = new UnTdf();
            UnTdfSection cur = null;
            int i = 0;
            while (i < b.Length)
            {
                int o = i, tag = b[i];
                i++;
                if (i >= b.Length) break;          // a tag byte that is the last byte of the file is not processed
                int n = Payload(tag);
                if (n < 0) continue;               // unknown tag: no payload, ignored
                if (i + n > b.Length) throw Bad("tag 0x" + tag.ToString("x2") + " at 0x" + o.ToString("x") + " runs past the end");
                int v = i;
                i += n;
                switch (tag)
                {
                    case 0x02: t.xOffset = F(b, v); break;
                    case 0x03: t.zOffset = F(b, v); break;
                    case 0x04: t.scale = F(b, v); break;
                    case 0x29: t.flags = (uint)I32(b, v); break;
                    case 0x01: t.count = I32(b, v); break;
                    case 0x05:
                    {
                        cur = new UnTdfSection { index = I32(b, v) };
                        if (t.byIndex.TryGetValue(cur.index, out var old)) t.sections[t.sections.IndexOf(old)] = cur; else t.sections.Add(cur);
                        t.byIndex[cur.index] = cur;
                        break;
                    }
                    case 0x06: Cur(cur, o).flags = (uint)I32(b, v) | 0xc1; break;
                    case 0x2b: Cur(cur, o).mask = I32(b, v); break;
                    case 0x18: Cur(cur, o).value = F(b, v); break;
                    case 0x0d: Cur(cur, o).nlinks = I32(b, v); break;
                    case 0x0e:
                    {
                        var s = Cur(cur, o); int k = I32(b, v);
                        if (!s.links.ContainsKey(k)) s.linkOrder.Add(k);
                        s.links[k] = I32(b, v + 4);
                        break;
                    }
                    case 0x08: Cur(cur, o).points.Add(new UnTdfPoint { index = t.pointCount }); t.pointCount++; break;
                    case 0x09: case 0x0a: case 0x0b:
                    {
                        var p = Last(cur, o);
                        double s = t.scale, ox = t.xOffset, oz = t.zOffset;
                        var w = new[] { F(b, v) * s + ox, F(b, v + 4) * s, F(b, v + 8) * s + oz };
                        if (tag == 0x09) p.centre = w; else if (tag == 0x0a) p.left = w; else p.right = w;
                        break;
                    }
                    case 0x17: Last(cur, o).flags = (uint)I32(b, v); break;
                    case 0x12: case 0x1a: case 0x1b: case 0x2a: Last(cur, o); break;
                    case 0x0f: case 0x10: case 0x19:
                    {
                        var g = new UnTdfGate();
                        double s = t.scale;
                        g.pos = new[] { F(b, v) * s + t.xOffset, F(b, v + 4) * s, F(b, v + 8) * s + t.zOffset };
                        g.normal = new[] { F(b, v + 12), F(b, v + 16), F(b, v + 20) };
                        double a0 = F(b, v + 24), a1 = F(b, v + 28), a2 = F(b, v + 32);
                        double l2 = a0 * a0 + a1 * a1 + a2 * a2;
                        if (l2 != 0) { double l = Math.Pow(l2, 0.5); g.across = new[] { a0 / l, a1 / l, a2 / l }; }
                        else g.across = new[] { a0, a1, a2 };
                        g.width = F(b, v + 36) * s;
                        t.gates[tag == 0x0f ? "start_gate" : tag == 0x10 ? "finish_gate" : "split_gate"] = g;
                        break;
                    }
                    case 0x13: t.ai["ai_13"] = F(b, v); break;
                    case 0x14: t.ai["ai_14"] = F(b, v); break;
                    case 0x15: t.ai["ai_15"] = F(b, v); break;
                    case 0x16: t.ai["ai_16"] = F(b, v); break;
                    case 0x28: t.ai["ai_28"] = F(b, v); break;
                    case 0x2c: t.ai["ai_2c"] = F(b, v); break;
                    case 0x07: case 0x0c: break;       // section_07, point_0c: read and ignored
                    default:                           // 0x1d..0x26: the object list the game's loader cannot read
                        throw new UnValueError("tag 0x" + tag.ToString("x2", CultureInfo.InvariantCulture) + " at 0x" + o.ToString("x", CultureInfo.InvariantCulture) + ": not supported by the game's loader");
                }
            }
            return t;
        }

        static UnTdfSection Cur(UnTdfSection cur, int o) => cur ?? throw Bad("section data before the first section (0x" + o.ToString("x") + ")");
        static UnTdfPoint Last(UnTdfSection cur, int o)
        {
            var s = Cur(cur, o);
            if (s.points.Count == 0) throw Bad("point data before the section's first point (0x" + o.ToString("x") + ")");
            return s.points[s.points.Count - 1];
        }
    }
}
