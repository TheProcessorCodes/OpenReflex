// TEXTURE FILTERING: ORIGINAL / 4X / 8X / 16X (OPTIONS > VIDEO, ReflexSettings.TextureFiltering). Not part of the original renderer.
//
// The original samples its surfaces isotropically (MAXANISOTROPY 1), most scenery with a point mip filter: ground, track-side banners,
// fences and roads smear at a grazing angle, and a line shows where the mip level steps. ORIGINAL keeps every texture exactly as its
// sampler state says. 4X / 8X / 16X give the registered surface textures (the scenery's and its animated flipbooks', the vehicles' and
// riders' with their number plates, the .rxmod / MX vs ATV Alive bikes', parts' and gear's maps, the ambient models', the water's, the
// grass's colour map and filtered tuft atlases, the terrain's tiled detail layers) anisotropic filtering of that level and a linear mip
// filter. Sampler state only: no texture data, shader or pass changes, and point-filtered textures (lookup tables, the forest atlas,
// the original's grass atlas) are never touched.
// REFLEX_TEXTURE_FILTERING=original|4|8|16 sets it at start (tests); REFLEX_TEXTURE_FILTERING_LOG=1 logs every texture registered and the
// state it then has.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reflex.Port.Rendering
{
    public static class ReflexTextureFiltering
    {
        public static readonly string[] Names = { "ORIGINAL", "4X", "8X", "16X" };
        static readonly int[] Levels = { 1, 4, 8, 16 };
        /// Tests: forces the setting (-1: the option).
        public static int Override = -1;
        public static int Setting { get { if (Override >= 0) return Override; if (!Application.isPlaying) return 0; try { return Options.ReflexSettings.TextureFiltering; } catch { return 0; } } }
        /// The anisotropy of the setting now in force (1: the original's isotropic filtering).
        public static int Aniso => Levels[Mathf.Clamp(Setting, 0, Levels.Length - 1)];

        struct Entry { public Texture texture; public int aniso; public FilterMode filter; }
        static readonly List<Entry> entries = new List<Entry>();
        sealed class ByReference : IEqualityComparer<Texture>
        {
            public bool Equals(Texture a, Texture b) => ReferenceEquals(a, b);
            public int GetHashCode(Texture t) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t);
        }
        /// Each registered texture's entry (a texture is registered once; registered again, its entry takes its state anew).
        static readonly Dictionary<Texture, int> index = new Dictionary<Texture, int>(new ByReference());
        static int purgeAt = 256;
        static bool log;
        static void Purge()
        {
            entries.RemoveAll(e => !e.texture);
            index.Clear(); for (int i = 0; i < entries.Count; i++) index[entries[i].texture] = i;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ReadEnvironment()
        {
            entries.Clear(); index.Clear(); purgeAt = 256; applied = -1;
            UnityEngine.Rendering.RenderPipelineManager.beginContextRendering -= Follow;
            UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += Follow;
            log = Environment.GetEnvironmentVariable("REFLEX_TEXTURE_FILTERING_LOG") == "1";
            string v = Environment.GetEnvironmentVariable("REFLEX_TEXTURE_FILTERING");
            if (string.Equals(v, "original", StringComparison.OrdinalIgnoreCase) || v == "1") Override = 0;
            else if (v == "4") Override = 1;
            else if (v == "8") Override = 2;
            else if (v == "16") Override = 3;
        }

        static int applied = -1;
        /// Before each frame's rendering: a changed setting (the option, a preset, a test's Override) reaches the live textures.
        static void Follow(UnityEngine.Rendering.ScriptableRenderContext context, List<Camera> cameras) { if (Setting != applied) Refresh(); }

        /// A surface texture as its sampler state made it: follows the option from now on (its own state is what ORIGINAL restores).
        /// A texture whose owner sets its sampler state again later (the number plates, bound with each material's states) registers it
        /// again after setting its filter mode and anisotropy: the state it has then is its own from there on.
        /// Main thread. Returns the texture.
        public static T Register<T>(T texture) where T : Texture
        {
            if (!texture) return texture;
            bool follows = texture.filterMode != FilterMode.Point && texture.mipmapCount > 1;
            var e2 = new Entry { texture = texture, aniso = texture.anisoLevel, filter = texture.filterMode };
            if (index.TryGetValue(texture, out int at))
            {
                if (!follows) { entries[at] = default; index.Remove(texture); return texture; }   // (point-filtered now: left alone; the empty entry goes with the next purge)
                entries[at] = e2;
            }
            else
            {
                if (!follows) return texture;
                if (entries.Count >= purgeAt) { Purge(); purgeAt = Math.Max(256, entries.Count * 2); }
                index[texture] = entries.Count; entries.Add(e2);
            }
            Apply(e2, Aniso);
            if (log) Debug.Log($"[texfilter] {Names[Mathf.Clamp(Setting, 0, Names.Length - 1)]}: {texture.name} {texture.width} x {texture.height}: its own {e2.filter} aniso {e2.aniso} -> {texture.filterMode} aniso {texture.anisoLevel}");
            return texture;
        }

        static void Apply(in Entry e, int aniso)
        {
            int want = Math.Max(e.aniso, aniso);
            var filter = aniso > 1 ? FilterMode.Trilinear : e.filter;
            if (e.texture.anisoLevel != want) e.texture.anisoLevel = want;
            if (e.texture.filterMode != filter) e.texture.filterMode = filter;
        }

        /// The option changed: every live registered texture takes the new level at once.
        public static void Refresh()
        {
            int aniso = Aniso; applied = Setting;
            Purge();
            foreach (var e in entries) Apply(e, aniso);
            if (log) Debug.Log($"[texfilter] {Names[Mathf.Clamp(Setting, 0, Names.Length - 1)]}: {entries.Count} registered textures set");
        }
    }
}
