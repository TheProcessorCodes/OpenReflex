// TEST ONLY: STREAMER MODE (Options/ReflexSettings.StreamerMode; OPTIONS > AUDIO > MUSIC). Page "streamer" (a scratch save): on the hub the
// menu music is audible (a playing AudioSource with a volume); STREAMER MODE on: no playing source named as music has a volume, MUSIC itself
// stays ON in the save; off again: the music is back. The options page shows the row (streamer-options). "[streamer-test] PASS|FAIL".
using System.Collections;
using System.Linq;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.Options;
using UnityEngine;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseStreamer
    {
        const string Tag = "[streamer-test] ";
        static int fails;
        static void Check(bool ok, string what) { Debug.Log(Tag + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { UiShowcase.Register("streamer", Page); }

        static string Heard()
        {
            var on = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(s => s.isPlaying && !s.mute && s.volume > 0.0005f).ToList();
            return on.Count + " audible: " + string.Join(", ", on.Select(s => s.gameObject.name + "/" + (s.clip ? s.clip.name : "-") + " " + s.volume.ToString("0.00")));
        }
        static int Audible() => Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(s => s.isPlaying && !s.mute && s.volume > 0.0005f);

        static IEnumerator Page()
        {
            fails = 0;
            float listener = AudioListener.volume; AudioListener.volume = 0f;   // test players are muted (the sources' own volumes are what is checked)
            var shell = OpenShell.Instance;
            shell.ShowMain();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 25f && Audible() == 0) yield return null;
            yield return new WaitForSecondsRealtime(4f);   // (the menu music's fade-in)
            Debug.Log(Tag + "before: " + Heard());
            int before = Audible();
            Check(!ReflexSettings.StreamerMode && ReflexSettings.MusicOn, "a new save: STREAMER MODE off, MUSIC on");
            Check(before > 0, "the menu music is audible before");
            ReflexClientLive.StreamerMode(true);
            yield return new WaitForSecondsRealtime(2f);
            Debug.Log(Tag + "streamer mode: " + Heard());
            Check(ReflexSettings.StreamerMode && ReflexSettings.MusicOn, "STREAMER MODE on leaves the MUSIC setting alone");
            Check(Audible() == 0, "STREAMER MODE: no music source is audible in the menus");
            shell.ShowOptions(1); yield return UiShowcase.Frames(60);   // AUDIO
            yield return UiShowcase.Shot("streamer-options");
            shell.ShowMain(); yield return UiShowcase.Frames(30);
            ReflexClientLive.StreamerMode(false);
            yield return new WaitForSecondsRealtime(4f);
            Debug.Log(Tag + "off again: " + Heard());
            Check(Audible() > 0, "STREAMER MODE off: the music is back");
            AudioListener.volume = listener;
            Debug.Log(Tag + (fails == 0 ? "RESULT PASS" : "RESULT FAIL (" + fails + ")"));
        }
    }
}
