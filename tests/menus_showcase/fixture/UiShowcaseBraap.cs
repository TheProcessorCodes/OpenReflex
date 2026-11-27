// TEST ONLY: B.R.A.A.P. (Content/ReflexBraap.cs, OpenFrontEnd/OpenShell.Braap.cs). Page "braap" (a scratch save, REFLEX_SAVE_DIR; muted):
//   the rosters   DEFAULT is the game's riders with their gear sets; every season roster has riders, unique numbers and plate names that fit
//   editing       a copy of a season roster, a rider renamed / renumbered / dressed in a game rider's set, saved and read back from the save
//   sharing       the share code round trip, a code inside other text, an Open Unleashed code (names and numbers), a broken code refused;
//                 EXPORT FILE and the file read back, a junk file refused
//   the field     a 5-rider roster for 12 computer riders (the draw starts over), FROM THE TOP in order, the same AI rider keeps his rider
//   the look      a rider's gear resolves to a rider the game can build
//   the screens   braap-rosters, braap-riders-season, braap-rider-season, braap-riders-own, braap-rider-own, braap-hub
// Log lines "[braap-test] PASS|FAIL ...".
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseBraap
    {
        const string Tag = "[braap-test] ";
        static int fails;
        static void Check(bool ok, string what) { Debug.Log(Tag + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { UiShowcase.Register("braap", Page); }

        sealed class Mute : MonoBehaviour { void LateUpdate() { if (AudioListener.volume != 0f) AudioListener.volume = 0f; } }   // test players are muted

        static VisualElement Item(string name) => OpenShell.Instance.Nav.Items().FirstOrDefault(e => e.name == name);

        static IEnumerator Page()
        {
            new GameObject("braap mute").AddComponent<Mute>();
            var shell = OpenShell.Instance;
            fails = 0;

            // ---- the rosters
            var def = ReflexBraap.Default;
            Check(def.riders.Count >= 50 && def.riders.All(r => r.look.Count >= 4), $"DEFAULT: {def.riders.Count} game riders, each with his gear set");
            Check(ReflexBraap.Seasons.Count >= 10, ReflexBraap.Seasons.Count + " season rosters, seasons " + string.Join(", ", ReflexBraap.SeasonYears));
            foreach (var s in ReflexBraap.Seasons)
            {
                bool unique = s.riders.Select(r => r.number).Distinct().Count() == s.riders.Count;
                bool plates = s.riders.All(r => ReflexBraap.PlateName(r).Length > 0 && ReflexBraap.PlateName(r).Length <= ReflexBraap.MaxPlateLength && r.name == ReflexBraap.CleanName(r.name));
                Check(s.riders.Count >= 15 && unique && plates, $"{s.name}: {s.riders.Count} riders, unique numbers {unique}, plates fit {plates} (#{s.riders[0].number} {s.riders[0].name} -> {ReflexBraap.PlateName(s.riders[0])})");
            }
            Check(ReflexBraap.PlateName(new BraapRider { name = "Kay de Wolf" }) == "DE WOLF" && ReflexBraap.PlateName(new BraapRider { name = "Ken Roczen", plate = "kenny" }) == "KENNY", "plate names: the family name, or the rider's own");
            Check(ReflexBraap.UsingDefault, "a new save uses the game's own riders");

            // ---- editing
            var season = ReflexBraap.Find("season:sx450-2026");
            Check(season != null, "the 2026 450SX roster is there");
            var mine = ReflexBraap.Create("My 450SX", season, "Tester");
            Check(mine != null && !mine.BuiltIn && mine.riders.Count == season.riders.Count, "a copy of a season roster is the player's");
            var sets = ReflexBraap.StockSets();
            Check(sets.Count >= 20, sets.Count + " game gear sets");
            var first = mine.riders[0];
            first.name = "Test Rider"; first.plate = "TESTER"; first.number = ReflexBraap.NextFreeNumber(mine, first, 998, 1);
            first.look = ReflexBraap.PicksOf(ReflexBraap.SetGear(sets[3].gearSet)); first.lookName = sets[3].name;
            mine.riders[1].enabled = false;
            ReflexBraap.Save(mine);
            ReflexBraap.Reload();
            var back = ReflexBraap.Find(mine.id);
            Check(back != null && back.riders[0].name == "Test Rider" && back.riders[0].plate == "TESTER" && back.riders[0].number == first.number && back.riders[0].look.Count == first.look.Count && !back.riders[1].enabled,
                  "the edited roster reads back from the save");
            mine = back;
            Check(ReflexBraap.SkillTier(0) == -1 && ReflexBraap.SkillTier(1) == 3 && ReflexBraap.SkillTier(4) == 0 && mine.riders.All(r => r.skill == 0), "skill: EVENT'S by default, ROOKIE is the game's tier 3, ALL TIME tier 0");
            Check(ReflexBraap.Duplicates(mine).Count == 0 && ReflexBraap.FreeNumber(mine) >= 2, "numbers: no duplicates, a free one found");

            // ---- sharing
            string code = ReflexBraap.ShareCode(mine);
            var got = ReflexBraap.FromShareText("check this out " + code + " <- my roster", out string err);
            Check(code.StartsWith(ReflexBraap.ShareTag) && got != null && got.riders.Count == mine.riders.Count && got.riders[0].name == "Test Rider" && got.riders[0].look.Count == mine.riders[0].look.Count,
                  $"share code round trip ({code.Length} characters){(err != null ? ": " + err : "")}");
            string ouJson = "{\"id\":\"x\",\"name\":\"OU field\",\"author\":\"me\",\"riders\":[{\"name\":\"Ricky Carmichael\",\"number\":4,\"gear\":\"mx01\",\"bike250\":\"CB_1\",\"atv\":\"\",\"enabled\":true},{\"name\":\"James Stewart\",\"number\":7,\"gear\":\"\",\"enabled\":false}]}";
            var ou = ReflexBraap.FromShareText("BRAAP1:" + Gz(ouJson), out err);
            Check(ou != null && ou.riders.Count == 2 && ou.riders[0].name == "Ricky Carmichael" && ou.riders[0].number == 4 && ou.riders[0].look.Count == 0 && !ou.riders[1].enabled, "an Open Unleashed share code reads: names, numbers, who lines up" + (err != null ? ": " + err : ""));
            Check(ReflexBraap.FromShareText("RXBRAAP1:!!!!", out err) == null && err != null && ReflexBraap.FromShareText("nothing here", out err) == null, "a broken code and no code are refused");
            int before = ReflexBraap.Rosters.Count;
            var imp = ReflexBraap.Import(code, out err);
            Check(imp != null && ReflexBraap.Rosters.Count == before + 1 && imp.id != mine.id, "PASTE SHARE CODE adds a roster");
            string path = ReflexBraap.ExportFile(mine, out err);
            Check(path != null && File.Exists(path) && path.EndsWith(".braap"), "EXPORT FILE: " + (path != null ? Path.GetFileName(path) : err));
            if (path != null)
            {
                var fr = ReflexBraap.ReadFile(path, out err);
                Check(fr != null && fr.riders.Count == mine.riders.Count && fr.name == mine.name, "the roster file reads back");
                string junk = Path.Combine(ReflexBraap.Folder, "junk.braap"); File.WriteAllText(junk, "not a roster {");
                Check(ReflexBraap.ReadFile(junk, out err) == null, "a junk file is refused (" + err + ")");
                string codeFile = Path.Combine(ReflexBraap.Folder, "code.braap"); File.WriteAllText(codeFile, "here: " + code);
                Check(ReflexBraap.ReadFile(codeFile, out err)?.riders.Count == mine.riders.Count, "a file holding a share code reads");
                Check(ReflexBraap.Files().Count == 3, "the roster folder lists its files");
            }

            // ---- the field
            var five = ReflexBraap.Create("Five", null, "Tester");
            for (int i = 0; i < 6; i++) five.riders.Add(new BraapRider { name = "Rider " + (char)('A' + i), number = 10 + i, enabled = i != 5 });
            five.draw = 1; ReflexBraap.Save(five);
            ReflexBraap.ActiveId = five.id;
            ReflexBraap.BeginEvent(true);
            var field = Enumerable.Range(1, 12).Select(k => ReflexBraap.RiderFor(k)).ToList();
            Check(ReflexBraap.EventOn && field.All(r => r != null) && string.Join("", field.Select(r => r.name.Last())) == "ABCDEABCDEAB", "FROM THE TOP: 5 riders for 12 computer riders, in order, the draw starts over, the rider out of the field never lines up");
            Check(ReflexBraap.RiderFor(3) == field[2], "the same AI rider keeps his roster rider (the next heat)");
            five.draw = 0; ReflexBraap.Save(five);
            ReflexBraap.BeginEvent(true);
            var rnd = Enumerable.Range(1, 5).Select(k => ReflexBraap.RiderFor(k).name).ToList();
            Check(rnd.Distinct().Count() == 5, "A RANDOM DRAW: nobody twice before everybody raced (" + string.Join(", ", rnd) + ")");
            ReflexBraap.BeginEvent(false);
            Check(!ReflexBraap.EventOn && ReflexBraap.RiderFor(1) == null, "the career / online: the game's own riders");
            ReflexBraap.ActiveId = ReflexBraap.DefaultId;
            ReflexBraap.BeginEvent(true);
            Check(!ReflexBraap.EventOn, "DEFAULT in use: nothing is changed");

            // ---- the look
            int[] gear = ReflexBraap.GearIds(mine.riders[0], null), auto = ReflexBraap.GearIds(mine.riders[2], null), auto2 = ReflexBraap.GearIds(new BraapRider { name = mine.riders[2].name }, null);
            var spec = OpenGarageData.RiderWithGear(gear);
            Check(gear.Length >= 5 && spec != null && spec.parts.Count >= 4, $"a rider's look builds: {gear.Length} pieces, {spec?.parts.Count} parts");
            Check(auto.Length >= 5 && auto.SequenceEqual(auto2) && OpenGarageData.RiderWithGear(auto) != null, "AUTO: a game set by the name, the same every time");

            // ---- the screens
            ReflexBraap.ActiveId = mine.id;
            shell.ShowMain(); yield return UiShowcase.Frames(90);
            Check(Item("ou-hub-braap") != null, "the hub has the B.R.A.A.P. chip");
            yield return UiShowcase.Shot("braap-hub");
            shell.ShowBraap(0); yield return UiShowcase.Frames(60);
            Check(Item("br-roster-default") != null && Item("br-roster-season:sx450-2026") != null && Item("br-roster-" + mine.id) != null && Item("br-file-0") != null, "ROSTERS lists DEFAULT, the seasons, the player's and the files");
            yield return UiShowcase.Shot("braap-rosters");
            var seasonRow = Item("br-roster-season:mxgp-2026");
            if (seasonRow != null) { shell.Nav.Focus(seasonRow); yield return UiShowcase.Frames(30); (seasonRow.userData as OuNavItem)?.submit?.Invoke(); yield return UiShowcase.Frames(60); }
            Check(Item("br-copy") != null && Item("br-rider-0") != null && Item("br-add") == null, "a season roster is read-only: EDIT A COPY");
            yield return UiShowcase.Shot("braap-riders-season");
            var r0 = Item("br-rider-0");
            if (r0 != null) { shell.Nav.Focus(r0); yield return UiShowcase.Frames(20); (r0.userData as OuNavItem)?.submit?.Invoke(); yield return UiShowcase.Frames(120); }
            yield return UiShowcase.Shot("braap-rider-season");
            shell.ShowBraap(0); yield return UiShowcase.Frames(30);
            var own = Item("br-roster-" + mine.id);
            if (own != null) { shell.Nav.Focus(own); yield return UiShowcase.Frames(20); (own.userData as OuNavItem)?.submit?.Invoke(); yield return UiShowcase.Frames(60); }
            Check(Item("br-add") != null && Item("br-rename") != null && Item("br-share") != null && Item("br-export") != null, "the player's roster: NAME, ADD RIDER, COPY SHARE CODE, EXPORT FILE");
            yield return UiShowcase.Shot("braap-riders-own");
            r0 = Item("br-rider-0");
            if (r0 != null) { shell.Nav.Focus(r0); yield return UiShowcase.Frames(20); (r0.userData as OuNavItem)?.submit?.Invoke(); yield return UiShowcase.Frames(150); }
            Check(Item("br-name") != null && Item("br-number") != null && Item("br-skill") != null && Item("br-look") != null && Item("br-gear-1") != null && Item("br-kit") != null, "the rider editor: NAME, NUMBER, LOOK, the pieces, BIKE GRAPHICS");
            var look = Item("br-look");
            if (look != null)
            {
                shell.Nav.Focus(look); yield return UiShowcase.Frames(10);
                string was = look.Q<Label>(className: "mxa-value")?.text;
                (look.userData as OuNavItem)?.adjust?.Invoke(1); yield return UiShowcase.Frames(150);
                string now = Item("br-look")?.Q<Label>(className: "mxa-value")?.text;
                Check(now != null && now != was, $"LOOK steps to another outfit ({was} -> {now})");
            }
            var helmet = Item("br-gear-1");
            if (helmet != null)
            {
                shell.Nav.Focus(helmet); yield return UiShowcase.Frames(10);
                string was = helmet.Q<Label>(className: "mxa-value")?.text;
                (helmet.userData as OuNavItem)?.adjust?.Invoke(1); yield return UiShowcase.Frames(150);
                string now = helmet.Q<Label>(className: "mxa-value")?.text, lookNow = Item("br-look")?.Q<Label>(className: "mxa-value")?.text;
                Check(now != was && lookNow == "CUSTOM", $"a single piece changes ({was} -> {now}), the look is CUSTOM");
            }
            yield return UiShowcase.Shot("braap-rider-own");
            shell.ShowMain(); yield return UiShowcase.Frames(120);
            yield return UiShowcase.Shot("braap-hub-after");
            Debug.Log(Tag + (fails == 0 ? "RESULT PASS" : "RESULT FAIL (" + fails + ")"));
        }

        static string Gz(string json)
        {
            byte[] raw = Encoding.UTF8.GetBytes(json);
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
            return Convert.ToBase64String(ms.ToArray());
        }
    }
}
