using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Reflex.Port.Content
{
    // Converted game content belongs to the regular library, including its RXAR overlay.
    public static class UnleashedLevels
    {
        [Serializable] public sealed class Entry
        {
            public int version, spacing, material_rules;   // material_rules 2: the scenery carries the source's alpha tests, stored vertex colours (0x80 = 1.0), layers
            public string level, display_name, kind, source_id, source_sha256, race_type, environment_mode, environment_donor, environment_sky;
            public string[] limitations;
        }
        [Serializable] public sealed class TerrainLayer
        {
            public string donor, albedo, bump;
            public string albedo_file;   // the albedo's picture in the level's own folder (RXIM v1) instead of the donor's texture `albedo`
            public float[] mean;
            public float tile;
            public Color Mean => new Color(mean[0], mean[1], mean[2], 1);
        }
        [Serializable] public sealed class TerrainStyle
        {
            public int version;
            public string name, cover;
            public float rut_strength;
            public TerrainLayer[] layers;
        }
        static readonly Dictionary<string, TerrainStyle> styles = new Dictionary<string, TerrainStyle>();
        public static TerrainStyle Style
        {
            get
            {
                if (Bound == null) return null;
                string path = Path.Combine(Folder(Bound.level), "terrain-style.json"), key = RxFiles.Generation + ":" + path;
                if (styles.TryGetValue(key, out var style)) return style;
                if (!RxFiles.Exists(path)) return null; // older imports retain their legacy palette
                style = JsonUtility.FromJson<TerrainStyle>(System.Text.Encoding.UTF8.GetString(RxFiles.ReadAllBytes(path)));
                if (style == null || style.version != 1 || style.layers == null || style.layers.Length != 4 || style.rut_strength < 0 || style.rut_strength > 1)
                    throw new InvalidDataException("Invalid Unleashed terrain style: " + Bound.level);
                foreach (var l in style.layers)
                    if (l == null || string.IsNullOrWhiteSpace(l.donor) || string.IsNullOrWhiteSpace(l.albedo) || string.IsNullOrWhiteSpace(l.bump)
                        || l.mean == null || l.mean.Length != 3 || l.mean.Any(v => !float.IsFinite(v) || v < 0 || v > 1) || !float.IsFinite(l.tile) || l.tile <= 0)
                        throw new InvalidDataException("Invalid Unleashed terrain layer: " + Bound.level);
                return styles[key] = style;
            }
        }
        static List<Entry> cached;
        static string root;
        static int generation = -1;
        public static Entry Bound { get; private set; }
        public static string Folder(string level) => Path.Combine(ReflexInstall.Require(), "Assets", "unleashed", level);
        public static IReadOnlyList<Entry> All
        {
            get
            {
                string dir = ReflexInstall.DatabaseDir;
                if (cached != null && root == dir && generation == RxFiles.Generation) return cached;
                var result = new List<Entry>();
                foreach (string path in RxFiles.GetFiles(dir))
                {
                    string name = Path.GetFileName(path);
                    if (!name.StartsWith("UNL_", StringComparison.Ordinal) || !name.EndsWith(".dx9.database", StringComparison.Ordinal)) continue;
                    string level = name.Substring(0, name.Length - ".dx9.database".Length);
                    try
                    {
                        string json = System.Text.Encoding.UTF8.GetString(RxFiles.ReadAllBytes(Path.Combine(Folder(level), "level.json")));
                        var entry = JsonUtility.FromJson<Entry>(json);
                        if (entry == null || entry.version != 1 || entry.level != level || string.IsNullOrWhiteSpace(entry.display_name)) throw new InvalidDataException("Invalid level registry");
                        if (!RxFiles.Exists(Path.Combine(Folder(level), "terrain.rxheight"))) throw new InvalidDataException("Missing terrain");
                        result.Add(entry);
                    }
                    catch (Exception ex) { Debug.LogWarning("[Unleashed] " + level + ": " + ex.Message); }
                }
                root = dir; generation = RxFiles.Generation;
                return cached = result.OrderBy(e => e.display_name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        static readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();
        public static Texture2D Picture(string level, bool map = false)
        {
            string path = Path.Combine(Folder(level), map ? "map.rximage" : "preview.rximage");
            string key = RxFiles.Generation + ":" + path;
            if (pictures.TryGetValue(key, out var cached)) return cached;
            if (!RxFiles.Exists(path)) return null;
            try
            {
                using var r = new BinaryReader(RxFiles.OpenRead(path));
                if (r.ReadUInt32() != 0x4d495852 || r.ReadInt32() != 1) throw new InvalidDataException("Invalid picture header");
                int w = r.ReadInt32(), h = r.ReadInt32();
                if (w < 1 || h < 1 || w > 4096 || h > 4096 || r.BaseStream.Length != 16L + w * h * 4L) throw new InvalidDataException("Invalid picture size");
                byte[] pixels = r.ReadBytes(w * h * 4), flipped = new byte[pixels.Length];
                for (int y = 0; y < h; y++) Buffer.BlockCopy(pixels, y*w*4, flipped, (h-1-y)*w*4, w*4);
                var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = level + (map ? " map" : " preview"), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
                t.SetPixelData(flipped, 0); t.Apply(true, false);
                return pictures[key] = t;
            }
            catch (Exception ex) { Debug.LogWarning("[Unleashed] Picture " + level + ": " + ex.Message); return pictures[key] = null; }
        }
        public static Entry Get(string level) => string.IsNullOrEmpty(level) || !level.StartsWith("UNL_", StringComparison.Ordinal) ? null : All.FirstOrDefault(e => string.Equals(e.level, level, StringComparison.OrdinalIgnoreCase));
        /// A terrain layer's own picture (TerrainLayer.albedo_file, RXIM v1: width, height, top-first RGBA) as a level texture with a
        /// full mip chain (Unity row order), under the layer's albedo name. Null if the level has no such file.
        internal static LevelTexture LayerPicture(TerrainLayer layer)
        {
            if (Bound == null || string.IsNullOrEmpty(layer?.albedo_file) || layer.albedo_file.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return null;
            string path = Path.Combine(Folder(Bound.level), layer.albedo_file); if (!RxFiles.Exists(path)) return null;
            using var r = new BinaryReader(RxFiles.OpenRead(path));
            if (r.ReadUInt32() != 0x4d495852 || r.ReadInt32() != 1) throw new InvalidDataException("Invalid ground picture header");
            int w = r.ReadInt32(), h = r.ReadInt32();
            if (w < 4 || h < 4 || w > 4096 || h > 4096 || (w & (w - 1)) != 0 || (h & (h - 1)) != 0 || r.BaseStream.Length != 16L + w * h * 4L) throw new InvalidDataException("Invalid ground picture size");
            var top = r.ReadBytes(w * h * 4); var level = new byte[top.Length];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(top, y * w * 4, level, (h - 1 - y) * w * 4, w * 4);
            var t = new LevelTexture { name = layer.albedo, width = w, height = h, format = TextureFormat.RGBA32 };
            t.mips.Add(level);
            while (w > 1 || h > 1)
            {
                int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2); var next = new byte[nw * nh * 4];
                for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++) for (int k = 0; k < 4; k++)
                {
                    int x0 = Math.Min(w - 1, x * 2), x1 = Math.Min(w - 1, x * 2 + 1), y0 = Math.Min(h - 1, y * 2), y1 = Math.Min(h - 1, y * 2 + 1);
                    next[(y * nw + x) * 4 + k] = (byte)((level[(y0 * w + x0) * 4 + k] + level[(y0 * w + x1) * 4 + k] + level[(y1 * w + x0) * 4 + k] + level[(y1 * w + x1) * 4 + k] + 2) / 4);
                }
                t.mips.Add(next); level = next; w = nw; h = nh;
            }
            t.levels = t.mips.Count;
            return t;
        }
        public static bool Is(string level) => Get(level) != null;
        public static void Bind(string level) { Bound = Get(level); }
    }
}
