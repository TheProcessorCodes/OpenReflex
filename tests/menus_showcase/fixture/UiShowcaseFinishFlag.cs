// TEST ONLY: the FINISH banner (w-finishflag: Hud/ReflexFinishBanner.cs). Synthetic finishes only, no race is ridden.
//   "finishflag"   the banner over the HUD showcase's backdrop and a MODERN race HUD (place 3 of 12, the last lap): before (the HUD alone),
//                  the entrance (0.10 s, 0.25 s, 0.45 s), the hold (1.2 s, 1.6 s: the wave moved on), the exit (3.15 s); then 1ST (RACE WINNER),
//                  2ND, 11TH, a rider alone (no place), a watched rider's name (a spectator), REDUCE MOTION, HUD SCALE 130 %, and that a
//                  block (a menu) ends it within its quick fade.
using System.Collections;
using Reflex.Port.Hud;
using UnityEngine;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseFinishFlag
    {
        const string Tag = "[ui-showcase] [finishflag] ";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { UiShowcase.Register("finishflag", Page); }

        sealed class KeepMuted : MonoBehaviour { void LateUpdate() { AudioListener.volume = 0f; AudioListener.pause = true; } }
        static void Check(bool ok, string what) { UiShowcaseNav.Check(ok, "finishflag", what); if (ok) Debug.Log(Tag + "ok " + what); }

        static ReflexHudModel Model(int place, int riders)
        {
            var m = new ReflexHudModel { mode = HudMode.Race, valid = true, inRace = true, racing = true, state = 2, place = place, riders = riders, laps = 3, lap = 3, lapsCompleted = 3, finished = true };
            m.haveHud = true; m.onTrack = true; m.speedMph = 41f; m.gear = 2; m.heading = 52f;
            m.hasRpm = true; m.idleRpm = 1500f; m.maxRpm = 14000f; m.rpm = 8200f; m.rpmGauge = ReflexHudModel.RpmGauge(m.rpm, m.idleRpm, m.maxRpm);
            m.hasBest = true; m.bestLap = 72.34f; m.lastLap = 73.21f; m.lapTime = 0f; m.shownLapTime = 73.21f; m.timeBehind = 2.45f; m.timeAhead = 1.12f;
            m.standings.Clear();
            for (int i = 0; i < riders; i++)
            {
                bool you = i + 1 == place;
                m.standings.Add(new HudStanding { position = i + 1, slot = i, laps = 3, you = you, ai = !you, finished = i < place, name = you ? "Player 1" : "Rider " + (i + 1), number = (10 + i * 7 % 90).ToString(), gapText = you ? "---" : i < place ? "FIN" : "L3", raceTime = 220f + i, bestLap = 70f + i * 0.3f });
                if (you) m.youRow = i;
            }
            m.MarkStandings();
            return m;
        }

        static IEnumerator Page()
        {
            AudioListener.volume = 0f; AudioListener.pause = true;
            var mute = new GameObject("finishflag mute TEST ONLY"); Object.DontDestroyOnLoad(mute); mute.AddComponent<KeepMuted>();
            var backdrop = HudShowcase.Backdrop();
            var hud = ReflexHudView.Create(null, "HUD gallery finishflag", 30001);
            hud.Options.scale = 1f; hud.Options.opacity = 1f; hud.Options.standings = true; hud.Options.gauge = true; hud.Options.reduceMotion = false; hud.Options.metric = false;
            hud.Visible = true;
            var model = Model(3, 12);
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime < t0 + 1.0f) { hud.Apply(model, Time.unscaledDeltaTime, 2); yield return null; }
            yield return UiShowcase.Shot("finishflag-0-before");

            var view = new ReflexFinishBannerView(null, 30002);
            Check(!view.Playing, "not playing until a finish");
            view.Play(3, 12, 221.47f);
            Check(view.Playing && view.Title == "FINISHED" && view.Place == "3RD" && view.Label.StartsWith("CHECKERED FLAG"), "3 of 12: FINISHED, 3RD, CHECKERED FLAG (" + view.Label + ")");
            yield return At(view, 0.10f, "finishflag-1-in-010");
            yield return At(view, 0.25f, "finishflag-2-in-025");
            yield return At(view, 0.45f, "finishflag-3-in-045");
            yield return At(view, 1.20f, "finishflag-4-hold-120");
            yield return At(view, 1.60f, "finishflag-5-hold-160");
            yield return At(view, 3.15f, "finishflag-6-out-315");

            view.Play(1, 12, 214.02f);
            Check(view.Place == "1ST" && view.Label.StartsWith("RACE WINNER"), "1 of 12: 1ST, RACE WINNER");
            yield return At(view, 1.40f, "finishflag-7-first");
            view.Play(2, 8, 216.9f);
            yield return At(view, 1.40f, "finishflag-8-second");
            view.Play(11, 12, 251.33f);
            Check(view.Place == "11TH", "11 of 12: 11TH");
            yield return At(view, 1.40f, "finishflag-9-eleventh");
            view.Play(1, 1, 80.5f);
            Check(view.Place == "", "a rider alone: no place");
            yield return At(view, 1.40f, "finishflag-10-alone");
            view.Play(2, 9, 0f, "KICKSTART_42");
            Check(view.Label == "KICKSTART_42" && view.Place == "2ND", "a watched rider: its name, no time");
            yield return At(view, 1.40f, "finishflag-11-spectator");
            view.Play(3, 12, 221.47f);
            yield return At(view, 1.40f, "finishflag-12-reduce-motion", true);

            ReflexHudKit.Scale = 1.3f; hud.Options.scale = 1.3f;
            t0 = Time.unscaledTime;
            while (Time.unscaledTime < t0 + 0.5f) { hud.Apply(model, Time.unscaledDeltaTime, 2); yield return null; }
            view.Play(3, 12, 221.47f);
            yield return At(view, 1.40f, "finishflag-13-hud-scale-130");
            ReflexHudKit.Scale = 1f; hud.Options.scale = 1f;

            // the whole run on the real clock, and a block's quick end
            view.Play(3, 12, 221.47f);
            t0 = Time.unscaledTime; int frames = 0;
            while (view.Playing && Time.unscaledTime < t0 + 6f) { view.Tick(Time.unscaledDeltaTime, false, false); frames++; yield return null; }
            float ran = Time.unscaledTime - t0;
            Check(!view.Playing && ran > ReflexFinishBannerView.Duration - 1.0f && ran < ReflexFinishBannerView.Duration + 0.6f, "plays " + ran.ToString("F2") + " s (" + frames + " frames), then gone");
            view.Play(3, 12, 221.47f);
            view.Tick(1.0f, false, false);
            t0 = Time.unscaledTime;
            while (view.Playing && Time.unscaledTime < t0 + 3f) { view.Tick(Time.unscaledDeltaTime, true, false); yield return null; }
            Check(!view.Playing && Time.unscaledTime - t0 < 0.8f, "a block (a menu, the results) ends it in " + (Time.unscaledTime - t0).ToString("F2") + " s");
            Check(!view.Canvas.enabled, "its canvas is off when it is not playing");

            view.Destroy(); hud.Destroy(); Object.Destroy(backdrop);
            ReflexHudKit.Scale = 1f; ReflexHudKit.PanelOpacity = 1f;
            yield return UiShowcase.Frames(3);
        }

        static IEnumerator At(ReflexFinishBannerView view, float seconds, string shot, bool reduceMotion = false)
        {
            view.Seek(seconds, reduceMotion);
            yield return null;
            view.Seek(seconds, reduceMotion);
            Debug.Log(Tag + shot + ": " + seconds.ToString("F2") + " s, \"" + view.Label + "\" / " + view.Title + " " + view.Place);
            yield return UiShowcase.Shot(shot);
        }
    }
}
