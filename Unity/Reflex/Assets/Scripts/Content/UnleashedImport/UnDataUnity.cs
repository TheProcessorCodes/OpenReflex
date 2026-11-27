// The game's hookup of UnData: the converter's data files are TextAssets under Assets/Resources/UnleashedImport/. Resources.Load must
// run on the main thread, and the converter runs on a worker, so the three files are read here once, before the first scene.
using System.Collections.Generic;
using UnityEngine;

namespace Reflex.Port.Content.UnleashedImport
{
    static class UnDataUnity
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            var texts = new Dictionary<string, string>();
            foreach (var name in new[] { UnData.Tracks, UnData.TerrainProfiles, UnData.SceneryProfiles })
            {
                var asset = Resources.Load<TextAsset>("UnleashedImport/" + System.IO.Path.GetFileNameWithoutExtension(name));
                if (asset != null) texts[name] = asset.text;
            }
            UnData.Loader = file => texts.TryGetValue(file, out var t) ? t : null;
            UnData.Reset();
        }
    }
}
