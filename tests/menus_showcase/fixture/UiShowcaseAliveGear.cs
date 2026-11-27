// TEST ONLY: the imported MX vs ATV Alive rider gear ("gear" .rxmods: Content/RxGear.cs) in GARAGE > RIDER. Page:
//   alive-gear   Run with a content folder that has <game>/AliveGear (the importer's folders). The stock rider is shot (ag-stock-*), each gear
//                type's list is opened (ag-list-<slot>: the Alive lines among the game's), then an Alive piece of each slot is worn through
//                the garage's own equip call (helmet, goggles, suit, boots, neck brace) and the rider is shot from the front, the side, the
//                back and close on the head (ag-worn-*); REFLEX_AG_SETS more outfits (default 2) are worn and shot (ag-set<n>-*).
//                Then the mixes of Alive and Reflex pieces (ag-mix-<name>-*): an Alive suit alone under the game's helmet, goggles and
//                boots; an Alive helmet alone; Alive helmet + goggles on the game's jersey; a whole Alive outfit without a neck brace. Each
//                must still have the rider's body: Reflex's skin (head, neck, wrists) and gloves, drawn and enabled on the rider's root.
//   alive-gear-import   REFLEX_ALIVE_SOURCES scanned as FIND LEVELS does; with nothing ticked IMPORT is off; with only RIDER GEAR ticked it
//                is on (agi-list) and imports the gear alone, no level and no bike (agi-results).
// Checks ("FAIL agear: ..."): every gear type lists its Alive pieces, a worn piece is the equipped one of its type and is on the rider's
// spec, the profile's PlayerRiderGear rows do not change, the stock choice comes back with the stock piece.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseAliveGear
    {
        const string Tag = "[ui-showcase] ";
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { UiShowcase.Register("alive-gear-kits", KitPage); UiShowcase.Register("alive-gear", Page); UiShowcase.Register("alive-gear-import", ImportPage); }

        static T Get<T>(string f) => (T)typeof(OpenShell).GetField(f, All).GetValue(Shell);
        static void Call(string m) => typeof(OpenShell).GetMethod(m, All).Invoke(Shell, null);

        /// GAME LIBRARY > MX vs ATV ALIVE with only the rider gear chosen: no level, no bike.
        static IEnumerator ImportPage()
        {
            string src = Environment.GetEnvironmentVariable("REFLEX_ALIVE_SOURCES");
            if (string.IsNullOrEmpty(src)) { Fail("REFLEX_ALIVE_SOURCES is not set"); yield break; }
            var paths = Get<List<string>>("_aliveSourcePaths"); paths.Clear(); paths.AddRange(src.Split(';').Where(p => p.Length > 0));
            Shell.ShowAliveSources(); yield return Wait(30);
            Call("BeginAliveScan");
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 300f && Get<System.Threading.Tasks.Task>("_aliveTask") != null;) yield return null;
            yield return Wait(30);
            var found = Get<Reflex.Port.Content.AliveImport.AliveSources>("_aliveFound");
            if (found == null) { Fail("the scan found nothing (" + Get<Exception>("_aliveError")?.Message + ")"); yield break; }
            Log($"import: found {found.levels.Count} levels, {found.bikes.Count} bikes, {found.gear.Count} gear models");
            if (found.gear.Count == 0) { Fail("no rider gear in the sources"); yield break; }
            var chosen = Get<HashSet<string>>("_aliveChosen");
            chosen.Clear(); Call("ShowAliveLevels"); yield return Wait(30);
            var import = El("ou-alive-import");
            if (import == null) { Fail("no IMPORT button"); yield break; }
            if (import.enabledSelf) Fail("IMPORT is on with nothing chosen");
            // tick the gear row as the player does
            var row = El("ou-alive-gear");
            if (row == null) { Fail("no RIDER GEAR row"); yield break; }
            (row.userData as OuNavItem)?.submit?.Invoke(); yield return Wait(10);
            import = El("ou-alive-import");
            Log($"import: only the gear ticked: chosen [{string.Join(", ", chosen)}], IMPORT {(import.enabledSelf ? "on" : "OFF")}, label \"{import.Q<Label>()?.text}\"");
            if (!import.enabledSelf) Fail("IMPORT is off with only RIDER GEAR chosen");
            Shell.Nav.Focus(row); yield return Wait(30);
            yield return UiShowcase.Shot("agi-list");
            (import.userData as OuNavItem)?.submit?.Invoke();
            yield return Wait(5);
            if (Get<System.Threading.Tasks.Task>("_aliveTask") == null) Fail("IMPORT did not start with only RIDER GEAR chosen");
            for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 900f && Get<System.Threading.Tasks.Task>("_aliveTask") != null;) yield return null;
            yield return Wait(30);
            yield return UiShowcase.Shot("agi-results");
            var levels = Get<List<Reflex.Port.Content.AliveImport.AliveLevelResult>>("_aliveResults");
            var bikes = Get<List<Reflex.Port.Content.AliveImport.AliveBikeResult>>("_aliveBikeResults");
            var gear = Get<List<Reflex.Port.Content.AliveImport.AliveGearResult>>("_aliveGearResults");
            Log($"import: results: levels {levels?.Count ?? -1}, bikes {bikes?.Count ?? -1}, gear {gear?.Count(r => r.ok) ?? -1} of {gear?.Count ?? -1} models, {gear?.Sum(r => r.pieces) ?? 0} pieces; error {Get<Exception>("_aliveError")?.Message}");
            if ((levels?.Count ?? 0) != 0 || (bikes?.Count ?? 0) != 0) Fail("a level or a bike was imported with only RIDER GEAR chosen");
            if (gear == null || gear.Count == 0 || gear.Any(r => !r.ok)) Fail("the rider gear did not import");
            Log($"import: RxGear now lists {RxGear.All.Count()} pieces");
            if (!RxGear.All.Any()) Fail("no gear piece after the import");
            Log("import done");
            Shell.ShowMain(); yield return Wait(5);
        }

        /// alive-gear-kits: a custom gear skin (a gear / kit pack piece of Mods/Gear) chosen for the suit, the helmet and the boots (agk-kit-*),
        /// then Alive gear over it: a suit (agk-suit-*), a helmet instead (agk-helmet-*), boots instead (agk-boots-*), all three (agk-all-*),
        /// and the custom skin again (agk-back-*). The same outfits are ridden by rxmod_ride.py REFLEX_RXMOD_KITS for the ride's side.
        static IEnumerator KitPage()
        {
            yield return UiShowcaseGarage.Open(OpenShell.GarageRider, true);
            var types = new[] { 3, 1, 4 };
            foreach (int ty in new[] { 1, 2, 3, 4, 11 }) RxGear.Clear(ty);
            foreach (int ty in types)
            {
                var kit = KitPacks.Current.byId.Values.Where(k => !k.bike && k.type == ty && !k.hidden).OrderBy(k => k.pack.name).ThenBy(k => k.rowId).FirstOrDefault();
                if (kit == null) { Fail("kits: no gear pack piece of gear type " + ty); continue; }
                var it = OpenGarageData.GearItems(ty).FirstOrDefault(i => i.kit == kit);
                if (it == null || !OpenGarageData.EquipGear(it)) { Fail("kits: " + kit.title + " cannot be worn"); continue; }
                Log($"kits: custom gear worn for type {ty}: {kit.lineTitle} {kit.title} ({kit.pack.name})");
            }
            void Spec(string tag)
            {
                var spec = OpenGarageData.RiderWithGear(OpenGarageData.EquippedGearIds());
                Log($"{tag}: ids {string.Join(",", OpenGarageData.EquippedGearIds())}; spec parts: " + string.Join(", ", spec?.parts.Select(p => p.key + "=" + p.modelPkg + "/" + p.matsPkg + (p.rx != null ? " + ALIVE " + p.rx.line + " " + p.rx.name : "")) ?? new string[0]));
            }
            GarageItem Alive(int type, string mod, string piece) => OpenGarageData.GearItems(type).FirstOrDefault(i => i.rx != null && i.rx.mod.id == mod && i.rx.pieceId == piece) ?? OpenGarageData.GearItems(type).FirstOrDefault(i => i.rx != null);
            var suit = Alive(3, "alive-jersey-riderdlc2-jersey-answersklcndy", "521"); var helmet = Alive(1, "alive-helmet-rider-helmet-flykineticflash", "169"); var boots = Alive(4, "alive-boots-rider-boot-onealclutch", "47");
            Spec("agk-kit"); yield return Shots("agk-kit");
            OpenGarageData.EquipGear(suit); Spec("agk-suit"); yield return Shots("agk-suit"); Body("agk-suit", true); RxGear.Clear(3);
            OpenGarageData.EquipGear(helmet); Spec("agk-helmet"); yield return Shots("agk-helmet"); RxGear.Clear(1);
            OpenGarageData.EquipGear(boots); Spec("agk-boots"); yield return Shots("agk-boots"); RxGear.Clear(4);
            OpenGarageData.EquipGear(suit); OpenGarageData.EquipGear(helmet); OpenGarageData.EquipGear(boots); Spec("agk-all"); yield return Shots("agk-all"); Body("agk-all", true);
            foreach (int ty in types) RxGear.Clear(ty);
            Spec("agk-back"); yield return Shots("agk-back");
            foreach (int ty in types) if (KitPacks.ChosenGear(ty) == null) Fail("kits: the custom gear of type " + ty + " is not the chosen one after the Alive piece was taken off");
            foreach (int ty in types) KitPacks.ClearGear(ty);
            Log("kits done");
            Shell.ShowMain(); yield return Wait(5);
        }

        /// The body of the rider that wears Alive gear on the stage: Reflex's skin and gloves surfaces on the root of the Alive pieces (of
        /// any rider's root with skin when no Alive suit is worn).
        static void Body(string tag, bool suit)
        {
            var rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var roots = rs.Where(r => r.name.StartsWith("rxgear_" + (suit ? "jersey" : ""))).Select(r => r.transform.root).Distinct().ToList();
            if (roots.Count == 0) { Fail($"{tag}: no rider with Alive gear in the scene"); return; }
            foreach (var root in roots)
            {
                var own = root.GetComponentsInChildren<Renderer>(true);
                var skin = own.Where(r => r.name.IndexOf("MXRider_Skin", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                var gloves = own.Where(r => r.name.EndsWith("MXRider_Jersey", StringComparison.OrdinalIgnoreCase) && !r.name.StartsWith("rxgear_")).ToList();   // (<asset>_lod0_s<n>_<material>: the jersey material = the gloves and the jersey)
                int shown = skin.Concat(gloves).Count(r => r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff);
                Log($"{tag}: rider \"{root.name}\": skin [{string.Join(", ", skin.Select(r => r.name))}], gloves / jersey [{string.Join(", ", gloves.Select(r => r.name))}], {shown} drawn; Alive [{string.Join(", ", own.Where(r => r.name.StartsWith("rxgear_")).Select(r => r.name))}]");
                if (skin.Count < 2) Fail($"{tag}: the rider has no skin (head, neck, wrists): {skin.Count} skin surfaces");
                if (gloves.Count == 0) Fail($"{tag}: the rider has no gloves");
                if (suit && gloves.Count > 1) Fail($"{tag}: the game's jersey is drawn under the Alive suit");
                if (suit && own.Count(r => r.name.StartsWith("rxgear_jersey")) != 2) Fail($"{tag}: the Alive suit draws {own.Count(r => r.name.StartsWith("rxgear_jersey"))} surfaces, not 2 (jersey + pants: its own gloves must not be drawn over Reflex's)");
                if (shown < skin.Count + gloves.Count) Fail($"{tag}: a body surface is not drawn");
            }
        }

        static void Log(string s) => Debug.Log(Tag + "agear " + s);
        static void Fail(string s) => Debug.LogError(Tag + "FAIL agear: " + s);
        static OpenShell Shell => OpenShell.Instance;
        /// n frames and at least n / 60 s of real time (a batch player's frames are not paced: the stage's camera and the TAA settle by time).
        static IEnumerator Wait(int n) { float until = Time.realtimeSinceStartup + n / 60f; for (int i = 0; i < n || Time.realtimeSinceStartup < until; i++) yield return null; }
        static VisualElement El(string name) => Shell.Layer.Q<VisualElement>(name);
        static List<VisualElement> Cards() => El("garage-rows").Query<VisualElement>(className: "ou-gcard").ToList();
        static string Text(VisualElement e) => string.Join(" | ", e.Query<Label>().ToList().Select(l => l.text).Where(t => !string.IsNullOrWhiteSpace(t)));
        static int Env(string k, int d) => int.TryParse(Environment.GetEnvironmentVariable(k), out int v) ? v : d;
        static void Angles(float yaw, float pitch)
        {
            var stage = typeof(OpenShell).GetField("garageStage", All).GetValue(Shell);
            stage?.GetType().GetMethod("SetAngles").Invoke(stage, new object[] { yaw, pitch });
        }
        static string ProfileGear()
        {
            var t = OpenGarageData.Tab("PlayerRiderGear");
            return string.Join(",", Enumerable.Range(0, t.rows).Select(i => t.Int("RiderGearId", i)));
        }

        /// The rider on the stage from four sides and close on the head.
        static IEnumerator Shots(string tag)
        {
            Shell.ShowMain(); yield return Wait(5);
            Shell.ShowGarage(OpenShell.GarageRider); yield return Wait(60);
            var id = Cards().FirstOrDefault(c => c.name == "garage-identity"); if (id != null) Shell.Nav.Focus(id);
            yield return Wait(360);
            yield return UiShowcase.Shot(tag + "-front");
            Angles(90f, 4f); yield return Wait(60); yield return UiShowcase.Shot(tag + "-side");
            Angles(180f, 6f); yield return Wait(60); yield return UiShowcase.Shot(tag + "-back");
            Angles(-35f, 8f); yield return Wait(60); yield return UiShowcase.Shot(tag + "-quarter");
            // close on the head: the helmet's section (its framing)
            var helmet = Cards().FirstOrDefault(c => c.name == "garage-gear-1");
            if (helmet != null) { Shell.Nav.Focus(helmet); yield return Wait(200); yield return UiShowcase.Shot(tag + "-head"); Angles(80f, 4f); yield return Wait(60); yield return UiShowcase.Shot(tag + "-head-side"); }
            // the suit's section: the torso, the arms and the hands (the sleeve's end on the glove)
            var suitCard = Cards().FirstOrDefault(c => c.name == "garage-gear-3");
            if (suitCard != null)
            {
                Shell.Nav.Focus(suitCard); yield return Wait(200); Angles(0f, 4f); yield return Wait(60); yield return UiShowcase.Shot(tag + "-torso");
                Angles(70f, 4f); yield return Wait(60); yield return UiShowcase.Shot(tag + "-torso-right"); Angles(-70f, 4f); yield return Wait(60); yield return UiShowcase.Shot(tag + "-torso-left");
            }
            var boots = Cards().FirstOrDefault(c => c.name == "garage-gear-4");
            if (boots != null) { Shell.Nav.Focus(boots); yield return Wait(200); yield return UiShowcase.Shot(tag + "-feet"); }
        }

        static IEnumerator Page()
        {
            yield return UiShowcaseGarage.Open(OpenShell.GarageRider, true);
            var all = RxGear.All.ToList();
            Log($"imported gear: {all.Count} pieces: " + string.Join(", ", all.GroupBy(p => p.slot).Select(g => g.Key + " " + g.Count() + " (" + g.Select(p => p.line).Distinct().Count() + " lines)")));
            if (all.Count == 0) { Fail("no imported Alive gear (<game>/AliveGear)"); yield break; }
            foreach (int ty in all.Select(p => p.type).Distinct()) if (RxGear.Chosen(ty) != null) RxGear.Clear(ty);
            string profile0 = ProfileGear();
            var types = OpenGarageData.GearTypes();
            Log("gear types: " + string.Join(", ", types.Select(t => t.id + " " + t.name + " (" + t.factory + ")")) + "; profile gear " + profile0);
            yield return Shots("ag-stock");

            // every type's list: the Alive pieces among the game's
            var slots = new[] { ("helmet", 1), ("goggles", 2), ("jersey", 3), ("boots", 4), ("neck_brace", 11) };
            foreach (var (slot, type) in slots)
            {
                var items = OpenGarageData.GearItems(type);
                int alive = items.Count(i => i.rx != null), want = all.Count(p => p.type == type);
                Log($"{slot} (type {type}): {items.Count} items listed, {alive} Alive (imported {want}), {OpenGarageData.Groups(items).Count} lines / singles");
                if (alive != want) Fail($"{slot}: {alive} Alive items listed, {want} imported");
                if (want == 0 || !types.Any(t => t.id == type)) continue;
                Shell.ShowMain(); yield return Wait(5);
                Shell.ShowGarage(OpenShell.GarageRider); yield return Wait(60);
                var card = Cards().FirstOrDefault(c => c.name == "garage-gear-" + type);
                if (card == null) { Fail("no card for gear type " + type); continue; }
                (card.userData as OuNavItem)?.submit?.Invoke();
                yield return Wait(60);
                // focus the first line that holds Alive pieces (its preview: what is worn)
                var lineCards = Cards();
                Log($"{slot}: level \"{Shell.GarageTitle}\": {lineCards.Count} cards: " + string.Join("; ", lineCards.Take(5).Select(Text)));
                yield return Wait(120);
                yield return UiShowcase.Shot("ag-list-" + slot);
            }

            // wear an outfit of Alive pieces, one per slot
            int sets = 1 + Math.Max(0, Env("REFLEX_AG_SETS", 2));
            for (int set = 0; set < sets; set++)
            {
                string tag = set == 0 ? "ag-worn" : "ag-set" + set;
                foreach (var (slot, type) in slots)
                {
                    var items = OpenGarageData.GearItems(type).Where(i => i.rx != null).ToList();
                    if (items.Count == 0) continue;
                    var lines = items.GroupBy(i => i.line).ToList();
                    var pick = lines[(set * 7 + 2) % lines.Count].Skip(set % 2).FirstOrDefault() ?? lines[(set * 7 + 2) % lines.Count].First();
                    bool ok = OpenGarageData.EquipGear(pick);
                    int worn = OpenGarageData.EquippedGear(type);
                    Log($"{tag}: {slot}: wore {pick.line} / {pick.title} (#{pick.id}, {pick.rx.mod.id}): {(ok ? "ok" : "REFUSED")}, equipped #{worn}");
                    if (!ok || worn != pick.id) Fail($"{tag}: {slot}: the piece is not the equipped one");
                }
                if (ProfileGear() != profile0) Fail($"{tag}: the profile's gear rows changed: {ProfileGear()} (were {profile0})");
                var ids = OpenGarageData.EquippedGearIds();
                var spec = OpenGarageData.RiderWithGear(ids);
                Log($"{tag}: the rider's gear ids {string.Join(",", ids)}; spec parts: " + string.Join(", ", spec?.parts.Select(p => p.key + (p.rx != null ? "=ALIVE " + p.rx.line + " " + p.rx.name : "=" + p.modelPkg)) ?? new string[0]));
                foreach (var (slot, type) in slots)
                    if (all.Any(p => p.type == type) && !(spec?.parts.Any(p => p.key == slot && p.rx != null) ?? false)) Fail($"{tag}: the rider's spec has no Alive {slot}");
                yield return Shots(tag);
                Body(tag, true);
                var rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r.name.StartsWith("rxgear_")).ToList();
                Log($"{tag}: {rs.Count} renderers of Alive gear in the scene: " + string.Join(", ", rs.Select(r => r.name + " [" + (r.sharedMaterial ? r.sharedMaterial.name + " / " + r.sharedMaterial.shader.name : "no material") + "]").Distinct().Take(14)));
                if (rs.Count == 0) Fail($"{tag}: no Alive gear renderer in the scene");
            }

            // the mixes of Alive and Reflex pieces: the rider's body must be there in each
            IEnumerator Stock()
            {
                foreach (var (slot, type) in slots) RxGear.Clear(type);
                yield return null;
            }
            GarageItem Pick(int type, int k) { var items = OpenGarageData.GearItems(type).Where(i => i.rx != null).ToList(); return items.Count == 0 ? null : items[k % items.Count]; }
            var mixes = new (string name, int[] types)[] { ("suit", new[] { 3 }), ("helmet", new[] { 1 }), ("head", new[] { 1, 2 }), ("nobrace", new[] { 1, 2, 3, 4 }), ("suitboots", new[] { 3, 4 }) };
            int mixNo = 0;
            foreach (var (name, mixTypes) in mixes)
            {
                yield return Stock();
                mixNo++;
                foreach (int type in mixTypes) { var it = Pick(type, 11 * mixNo + 5); if (it != null) { OpenGarageData.EquipGear(it); Log($"ag-mix-{name}: wore {it.line} / {it.title} ({it.rx.slot})"); } }
                if (ProfileGear() != profile0) Fail($"ag-mix-{name}: the profile's gear rows changed");
                var spec = OpenGarageData.RiderWithGear(OpenGarageData.EquippedGearIds());
                Log($"ag-mix-{name}: spec parts: " + string.Join(", ", spec?.parts.Select(p => p.key + "=" + p.modelPkg + "/" + p.matsPkg + (p.rx != null ? " + ALIVE " + p.rx.line + " " + p.rx.name : "")) ?? new string[0]));
                bool suit = mixTypes.Contains(3);
                if (suit && !(spec?.parts.Any(p => p.key == "jersey" && p.rx != null && p.modelPkg != null) ?? false)) Fail($"ag-mix-{name}: the Alive suit has no Reflex jersey under it for the body");
                yield return Shots("ag-mix-" + name);
                Body("ag-mix-" + name, suit);
            }
            yield return Stock();

            // back to the game's own pieces
            foreach (var (slot, type) in slots)
            {
                int stock = OpenGarageData.GearItems(type).Where(i => i.rx == null && i.kit == null && i.Available).Select(i => i.id).FirstOrDefault(i => profile0.Split(',').Contains(i.ToString()));
                var it = OpenGarageData.GearItems(type).FirstOrDefault(i => i.id == stock);
                if (it != null) { OpenGarageData.EquipGear(it); if (OpenGarageData.EquippedGear(type) != stock) Fail($"{slot}: the stock piece did not come back"); }
                else RxGear.Clear(type);
            }
            Log("stock again: equipped " + string.Join(",", OpenGarageData.EquippedGearIds()) + "; profile " + ProfileGear());
            if (OpenGarageData.EquippedGearIds().Any(RxGear.IsId)) Fail("an Alive piece is still worn after the stock pieces were chosen");
            Log("done");
            Shell.ShowMain(); yield return Wait(5);
        }
    }
}
