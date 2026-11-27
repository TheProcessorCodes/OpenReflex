// The converter's own data files (tools/unleashed/tracks.json, terrain_profiles.json, scenery_profiles.json), shipped with the game as
// Assets/Resources/UnleashedImport/<name> and read through one provider, so the converter itself stays free of the engine: the game
// sets Loader to Resources.Load (UnDataUnity.cs), a test sets it to read the files.
using System;
using System.Collections.Generic;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnData
    {
        public const string Tracks = "tracks.json", TerrainProfiles = "terrain_profiles.json", SceneryProfiles = "scenery_profiles.json";

        /// name ("tracks.json") -> the file's text, or null when there is no such file.
        public static Func<string, string> Loader;

        static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// The text of one data file (cached). InvalidOperationException when no loader is set or the file is missing.
        public static string Text(string name)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(name, out var t)) return t;
                var load = Loader ?? throw new InvalidOperationException("UnData.Loader is not set: the Unleashed converter's data (" + name + ") cannot be read");
                t = load(name) ?? throw new InvalidOperationException("The Unleashed converter's data file " + name + " is missing");
                if (t.Length > 0 && t[0] == (char)0xFEFF) t = t.Substring(1);
                _cache[name] = t;
                return t;
            }
        }

        /// Forget what was read (after Loader changed).
        public static void Reset() { lock (_cache) _cache.Clear(); }
    }
}
