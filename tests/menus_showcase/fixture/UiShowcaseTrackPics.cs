// TEST ONLY: page "trackpics": the pictures the game draws of custom tracks (Content/TrackPictures.cs) in the track browser. Run with a
// client root whose Mods/Tracks holds tracks (run.py --client-root) and --env REFLEX_TRACK_PICTURES_DIR=<a scratch folder> (the cache):
//   trackpics-stock          RACE > NATIONAL, a stock track selected (its location picture and track map: the comparison)
//   trackpics-race           RACE > CUSTOM TRACKS, every card's picture
//   trackpics-race-<n>       ... each race track selected in turn (the preview: its picture, its map in the corner)
//   trackpics-freeride       FREE RIDE > CUSTOM TRACKS
//   trackpics-freeride-<n>   ... each track selected whose race is not in RACE (the Free Ride maps)
//   trackpics-setup          a custom race track's setup page (the hero and its map)
// and every drawn picture as it is, <shots>/trackpics/<track>-card.png / -map.png. Logs what each track's pictures were made from and
// whether they came from the cache ("[trackpics] PASS|FAIL"). Nothing is ridden.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseTrackPics
    {
        const string Tag = "[ui-showcase] [trackpics] ";
        static int fails;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => UiShowcase.Register("trackpics", Run);

        static OpenShell Shell => OpenShell.Instance;
        static void Log(string s) => Debug.Log(Tag + s);
        static void Check(bool ok, string what) { Debug.Log(Tag + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
        static string Safe(string s) => new string((s ?? "").Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray());

        /// Asks for every card's pictures and waits until they are made (the browser itself asks only for the cards in view).
        static IEnumerator Wait(List<OpenEvent> cards, string what)
        {
            float t0 = Time.realtimeSinceStartup; int frames = 0; float worst = 0f;
            for (;;)
            {
                bool pending = false;
                foreach (var c in cards)
                {
                    OpenContent.PictureLater(c, out bool p0); OpenContent.MapPictureLater(c, out bool p1);
                    pending |= p0 || p1;
                }
                if (!pending && !TrackPictures.Busy) break;
                if (Time.realtimeSinceStartup - t0 > 600f) { Check(false, what + ": the pictures were not made in 600 s"); break; }
                frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                yield return null;
            }
            Log($"{what}: every picture made after {Time.realtimeSinceStartup - t0:F1} s, {frames} frames meanwhile (the longest {worst * 1000f:F0} ms)");
        }

        static IEnumerator Run()
        {
            fails = 0;
            foreach (var m in TrackMods.All) Log($"track {m.id}: \"{m.name}\" {m.kindLabel} {m.level} {(m.Ok ? "ok" : "BROKEN " + m.problem)} own picture: {(m.thumbnail != null ? Path.GetFileName(m.thumbnail) : "none")}");
            string shots = Environment.GetEnvironmentVariable("REFLEX_PREVIEW_SHOTS");
            string dump = string.IsNullOrEmpty(shots) ? null : Path.Combine(shots, "trackpics");
            if (dump != null) Directory.CreateDirectory(dump);

            // ---- a stock track: its pictures are the tables' (the comparison)
            Shell.ShowMain(); yield return UiShowcase.Frames(8);
            Shell.ShowBrowser(false); yield return UiShowcase.Frames(30);
            Shell.SelectBrowse("National"); yield return UiShowcase.Frames(30);
            var stock = Shell.CardEvents.FirstOrDefault(c => !c.Custom && c.ev != null && c.ev.location == "NAT_Concord") ?? Shell.CardEvents.FirstOrDefault(c => !c.Custom);
            Check(stock != null, "a stock National is listed");
            if (stock != null)
            {
                Shell.SelectEvent(stock.key); yield return UiShowcase.Frames(20);
                Shell.LoadAllThumbnails(); yield return UiShowcase.Frames(20);
                var sp = OpenContent.Picture(stock); var sm = OpenContent.MapPicture(stock);
                Check(sp && sp.name.StartsWith("ou_icon", StringComparison.Ordinal), "the stock track's picture is the tables' (" + (sp ? sp.name + " " + sp.width + "x" + sp.height : "none") + ")");
                Check(sm && sm.name.StartsWith("ou_icon", StringComparison.Ordinal), "the stock track's map is the tables' (" + (sm ? sm.name + " " + sm.width + "x" + sm.height : "none") + ")");
                if (dump != null && sm) File.WriteAllBytes(Path.Combine(dump, "stock-" + Safe(stock.ev.location) + "-map.png"), sm.EncodeToPNG());
                if (dump != null && sp) File.WriteAllBytes(Path.Combine(dump, "stock-" + Safe(stock.ev.location) + "-picture.png"), sp.EncodeToPNG());
                yield return UiShowcase.Shot("trackpics-stock");
            }

            // ---- RACE > CUSTOM TRACKS
            Shell.SelectBrowse(OpenContent.CustomGroup); yield return UiShowcase.Frames(30);
            var races = Shell.CardEvents.Where(c => c.Custom && !c.Broken).ToList();
            yield return Wait(races, "RACE > CUSTOM TRACKS");
            Shell.LoadAllThumbnails(); yield return UiShowcase.Frames(20);
            yield return UiShowcase.Shot("trackpics-race");
            var seen = new HashSet<TrackMod>();
            int n = 0;
            foreach (var c in races)
            {
                if (!seen.Add(c.mod)) continue;
                Report(c, dump);
                if (n >= 12) continue;
                Shell.SelectEvent(c.key); yield return UiShowcase.Frames(20);
                yield return UiShowcase.Shot("trackpics-race-" + (++n) + "-" + Safe(c.mod.name));
            }

            // ---- RACE > MX vs ATV Untamed (the importer's tracks: custom tracks in a section of their own, each with its own preview picture)
            Shell.SelectBrowse(OpenContent.UntamedGroup); yield return UiShowcase.Frames(30);
            var untamed = Shell.CardEvents.Where(c => c.Custom && !c.Broken).ToList();
            if (untamed.Count > 0)
            {
                yield return Wait(untamed, "RACE > MX vs ATV Untamed");
                Shell.LoadAllThumbnails(); yield return UiShowcase.Frames(20);
                foreach (var c in untamed) if (seen.Add(c.mod)) Report(c, dump);
                Shell.SelectEvent(untamed[0].key); yield return UiShowcase.Frames(20);
                yield return UiShowcase.Shot("trackpics-untamed");
                Shell.SelectBrowse(OpenContent.CustomGroup); yield return UiShowcase.Frames(30);
            }

            // ---- a race track's setup page
            var first = races.FirstOrDefault(c => TrackPictures.Source(c.mod) != null && TrackPictures.Source(c.mod).StartsWith("line", StringComparison.Ordinal));
            if (first != null)
            {
                Shell.SelectEvent(first.key); yield return UiShowcase.Frames(10);
                yield return UiShowcaseNav.PadTap(UnityEngine.InputSystem.LowLevel.GamepadButton.South); yield return UiShowcase.Frames(40);
                Check(Shell.Current == OpenShell.Screen.Setup, "the custom track's setup page opens (" + Shell.Current + ")");
                yield return UiShowcase.Shot("trackpics-setup");
                yield return UiShowcaseNav.PadTap(UnityEngine.InputSystem.LowLevel.GamepadButton.East); yield return UiShowcase.Frames(20);
            }

            // ---- FREE RIDE > CUSTOM TRACKS
            Shell.ShowMain(); yield return UiShowcase.Frames(8);
            Shell.ShowBrowser(true); yield return UiShowcase.Frames(30);
            Shell.SelectBrowse(OpenContent.CustomGroup); yield return UiShowcase.Frames(30);
            var rides = Shell.CardEvents.Where(c => c.Custom && !c.Broken).ToList();
            yield return Wait(rides, "FREE RIDE > CUSTOM TRACKS");
            Shell.LoadAllThumbnails(); yield return UiShowcase.Frames(20);
            yield return UiShowcase.Shot("trackpics-freeride");
            n = 0;
            foreach (var c in rides)
            {
                if (!seen.Add(c.mod)) continue;
                Report(c, dump);
                if (n >= 12) continue;
                Shell.SelectEvent(c.key); yield return UiShowcase.Frames(20);
                yield return UiShowcase.Shot("trackpics-freeride-" + (++n) + "-" + Safe(c.mod.name));
            }
            Check(seen.Count > 0, "custom tracks were listed (" + seen.Count + ")");
            Check(seen.All(m => TrackPictures.Source(m) != null), "every track's pictures were made or found to be none");
            int own = seen.Count(m => m.thumbnail != null), drawn = seen.Count(m => m.thumbnail == null && TrackPictures.Card(m, out _)), maps = seen.Count(m => TrackPictures.Map(m, out _)),
                none = seen.Count(m => m.thumbnail == null && !TrackPictures.Card(m, out _)), cached = seen.Count(TrackPictures.FromCache);
            Log($"{seen.Count} tracks: {own} with their own picture, {drawn} with a drawn one, {none} with none; {maps} with a drawn map; {cached} from the cache");
            Debug.Log(Tag + (fails == 0 ? "RESULT PASS" : "RESULT FAIL (" + fails + ")"));
        }

        static void Report(OpenEvent c, string dump)
        {
            var m = c.mod;
            var card = TrackPictures.Card(m, out _); var map = TrackPictures.Map(m, out _);
            var shown = OpenContent.Picture(c);
            Log($"pictures {m.id}: {TrackPictures.Source(m) ?? "not made"}{(TrackPictures.FromCache(m) ? " (cache)" : "")}; the card shows {(m.thumbnail != null ? "its own " + Path.GetFileName(m.thumbnail) : card ? "the drawn picture" : "nothing")}; the map {(map ? "the drawn line" : "none")}");
            Check(m.thumbnail == null || shown && shown != card, m.id + ": a track's own picture stays the card's");
            if (dump == null) return;
            try
            {
                if (card) File.WriteAllBytes(Path.Combine(dump, Safe(m.id) + "-card.png"), card.EncodeToPNG());
                if (map) File.WriteAllBytes(Path.Combine(dump, Safe(m.id) + "-map.png"), map.EncodeToPNG());
            }
            catch (Exception e) { Log("dump " + m.id + ": " + e.Message); }
        }
    }
}
