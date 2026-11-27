// GARAGE > VEHICLE > MY RIDES: the vehicle list's filters (the track browser's chips are the model: OpenShell.Browser.cs).
//   SOURCE   ALL / ORIGINAL / MODS / ALIVE. ORIGINAL: the game's own vehicle (a card without a mod). ALIVE: a card of an imported
//            MX vs ATV Alive vehicle: a .rxmod the in-client importer wrote into <content>/AliveBikes, which BikeMods.Scan lists under the
//            id "AliveBikes/<slug>" (GarageSourceOf). MODS: every other bike mod of Mods/Bikes (.rxmod packs, skins, the classic .dx9 mods).
//   CLASS    ALL, then one chip per class the list has. A class is the game's VehicleType of the model the card rides as (VehicleModel.
//            VehicleTypeId: MX Lite = MX2, MX = MX1, ATV, UTV, ...) unless the card's .rxmod bike names a class of its own (RxModBike.garageClass,
//            "class" in mod.json: the imported Alive 50CC, 650 and ATV 1000, the same ones OpenSetup.OwnTypes offers as VEHICLE choices).
//            Order: the garage's own class order (OpenGarageData.TypeRank), an own class right after the class whose slot it rides in.
//   SORT     CLASS (the page's order: a class at a time, owned first, then by name, a bike's mods under it) or NAME (A-Z, no sections).
// Each chip shows how many vehicles it would list with the other filter as it is; the line under the title says "SHOWING 12 OF 47"; nothing listed: why, and
// SHOW ALL VEHICLES. The filters are the client save's (Garage.Filter.Class / .Source / .Sort): the list opens as it was left. A class card
// of MY RIDES (and MODDED BIKES / ALIVE BIKES) is a shortcut that sets them. Y / F jumps between the list and the filters.
// Nothing here changes what is owned, priced, locked or ridden: the cards and what A does on them are the list's as before.
// The bar is the list's first rows and scrolls with it. Test hooks: garage-filter-source-<all|original|mods|alive>,
// garage-filter-class-<all|type-N|own-slug>, garage-filter-sort, garage-filter-empty, garage-filter-clear; GarageSetFilters / GarageFilterState / GarageFilterClasses.
using System;
using System.Collections.Generic;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.OpenFrontEnd
{
    /// One card of the vehicle list with what the filters ask of it.
    internal sealed class GarageEntry
    {
        public GarageItem item;
        public string cls;      // "type:<VehicleTypeId>" or "own:<garageClass>"
        public int rank;        // the class's place in the list
        public int source;      // OpenShell.GSourceOriginal / GSourceMods / GSourceAlive
    }

    public sealed partial class OpenShell
    {
        public const string GarageFilterClassKey = "Garage.Filter.Class", GarageFilterSourceKey = "Garage.Filter.Source", GarageFilterSortKey = "Garage.Filter.Sort";
        public const int GSourceAll = 0, GSourceOriginal = 1, GSourceMods = 2, GSourceAlive = 3;
        public const int GSortClass = 0, GSortName = 1;
        static readonly string[] GSourceNames = { "ALL", "ORIGINAL", "MODS", "ALIVE" };
        static readonly string[] GSourceCodes = { "all", "original", "mods", "alive" };
        static readonly string[] GSortNames = { "CLASS", "NAME" };

        string gfClass = ""; int gfSource, gfSort; bool gfLoaded;
        List<GarageEntry> gfEntries = new List<GarageEntry>();
        VisualElement garageFilters, gfSortChip;
        int gfShown;
        readonly Dictionary<string, VisualElement> gfClassChips = new Dictionary<string, VisualElement>();
        readonly VisualElement[] gfSourceChips = new VisualElement[4];

        // ------------------------------------------------------------------------------------------------ classes and sources

        static string GarageClassKey(GarageItem v) { string own = OwnClassOf(v); return own != null ? "own:" + own : "type:" + v.type; }
        static int GarageClassType(string key) => key != null && key.StartsWith("type:", StringComparison.Ordinal) && int.TryParse(key.Substring(5), out int t) ? t : -1;

        /// The chip's word for a class: MX2 (the game's MX Lite: 125 two-strokes and 250Fs), MX1 (the game's MX: 250 two-strokes and 450Fs), the
        /// game's own name for every other type (ATV, UTV ...), an own class's name as its mod gives it (50CC, 650, ATV 1000).
        static string GarageClassLabel(string key)
        {
            if (string.IsNullOrEmpty(key)) return "ALL";
            if (key.StartsWith("own:", StringComparison.Ordinal)) return key.Substring(4);
            int t = GarageClassType(key);
            return t == 2 ? "MX2" : t == 3 ? "MX1" : OpenGarageData.TypeName(t).ToUpperInvariant();
        }

        /// A class's heading in the list: the chip's word, with the game's own name where they differ ("MX2  ·  MX LITE").
        static string GarageClassHeading(string key)
        {
            string label = GarageClassLabel(key);
            int t = GarageClassType(key);
            string game = t >= 0 ? OpenGarageData.TypeName(t).ToUpperInvariant() : label;
            return game == label ? label : label + "  ·  " + game;
        }

        static string GarageClassSlug(string key) =>
            string.IsNullOrEmpty(key) ? "all" : new string(key.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

        /// Where a card's vehicle comes from: the game (no mod), the MX vs ATV Alive import (BikeMods.Scan's "AliveBikes/<slug>" ids: the
        /// .rxmod folders of <content>/AliveBikes) or any other bike mod.
        internal static int GarageSourceOf(GarageItem v)
        {
            if (v.mod == null) return GSourceOriginal;
            return v.mod.id != null && v.mod.id.StartsWith(Reflex.Port.Content.AliveImport.AliveBikes.Folder + "/", StringComparison.Ordinal) ? GSourceAlive : GSourceMods;
        }

        /// Every card the list can show, in the page's order: the game's vehicles (OpenGarageData.Vehicles: a class at a time, owned first, then
        /// by name), under each one a card per installed bike mod that has its chassis.
        static List<GarageEntry> GarageAllEntries(List<GarageItem> all)
        {
            var list = new List<GarageEntry>();
            void Add(GarageItem it) => list.Add(new GarageEntry
            {
                item = it, cls = GarageClassKey(it), source = GarageSourceOf(it),
                rank = OpenGarageData.TypeRank(it.type) * 2 + (OwnClassOf(it) != null ? 1 : 0),
            });
            foreach (var v in all)
            {
                Add(v);
                foreach (var m in OpenGarageData.ModsOfModel(v.id)) Add(ModCard(v, m));
            }
            return list;
        }

        /// The classes of a list, in the list's order.
        static List<string> GarageClassesOf(List<GarageEntry> entries) =>
            entries.GroupBy(e => e.cls).OrderBy(g => g.First().rank).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).ToList();

        // ------------------------------------------------------------------------------------------------ the saved filters

        void GarageFiltersLoad()
        {
            if (gfLoaded) return;
            gfLoaded = true;
            gfClass = ReflexSave.GetString(GarageFilterClassKey, "") ?? "";
            gfSource = Mathf.Clamp(ReflexSave.GetInt(GarageFilterSourceKey, GSourceAll), 0, GSourceNames.Length - 1);
            gfSort = Mathf.Clamp(ReflexSave.GetInt(GarageFilterSortKey, GSortClass), 0, GSortNames.Length - 1);
        }

        void GarageFiltersSave()
        {
            ReflexSave.SetString(GarageFilterClassKey, gfClass);
            ReflexSave.SetInt(GarageFilterSourceKey, gfSource);
            ReflexSave.SetInt(GarageFilterSortKey, gfSort);
            ReflexSave.Save();
        }

        bool GarageFilterMatch(GarageEntry e, string cls, int source) => (cls.Length == 0 || e.cls == cls) && (source == GSourceAll || e.source == source);

        /// The filters now ("type:3|2|0": class key, source, sort), the list's count line ("12 OF 47") and its class keys: tests.
        public string GarageFilterState => gfClass + "|" + gfSource + "|" + gfSort;
        public string GarageFilterCount => gfShown + " OF " + gfEntries.Count;
        /// The line under the list's title: how many of the vehicles the filters list.
        string GarageListSub() => "SHOWING " + GarageFilterCount + "  ·  " + OuGlyphs.Retarget(OuUi.KeySubmit).ToUpperInvariant() + " TO RIDE IT";
        public List<string> GarageFilterClasses => GarageClassesOf(gfEntries);

        /// Sets the filters (cls: "" every class, "type:<VehicleTypeId>", "own:<class>"; source: GSourceAll ...; sort -1: as it is) and, on the
        /// vehicle list, shows the list again. The chips, the list's class cards and tests.
        public void GarageSetFilters(string cls, int source, int sort = -1)
        {
            GarageFiltersLoad();
            gfClass = cls ?? "";
            gfSource = Mathf.Clamp(source, 0, GSourceNames.Length - 1);
            if (sort >= 0) gfSort = Mathf.Clamp(sort, 0, GSortNames.Length - 1);
            GarageFiltersSave();
            if (!garageBuilt || GTop?.kind != GKind.Models || garageRows == null || garageFilters == null) return;
            bool onBar = _nav.Focused != null && garageFilters.Contains(_nav.Focused);
            if (gfClass.Length > 0 && !gfEntries.Any(e => e.cls == gfClass)) gfClass = "";
            GTop.focus = null;
            garageScroll.scrollOffset = Vector2.zero;
            GarageFillModels();
            GarageHeadText();
            if (!onBar) { var first = GarageFirstListItem(); if (first != null) _nav.Focus(first); }   // (the empty state's button, a test: the list's first card)
        }

        /// A class card of MY RIDES (MODDED BIKES, ALIVE BIKES): the list with that filter set.
        void GarageOpenList(string cls, int source)
        {
            GarageSetFilters(cls, source);
            GarageEnter(GKind.Models, -1);
        }

        /// The list's title: what the filters leave ("ALL VEHICLES", "MX1", "MODDED BIKES", "MX1  ·  MODS").
        string GarageListTitle() { GarageFiltersLoad(); return GarageListTitle(gfClass, gfSource); }
        static string GarageListTitle(string cls, int source)
        {
            if (cls.Length == 0)
                return source == GSourceOriginal ? "ORIGINAL VEHICLES" : source == GSourceMods ? "MODDED BIKES" : source == GSourceAlive ? "ALIVE VEHICLES" : "ALL VEHICLES";
            return GarageClassLabel(cls) + (source == GSourceAll ? "" : "  ·  " + GSourceNames[source]);
        }

        // ------------------------------------------------------------------------------------------------ MY RIDES (the classes)

        /// BROWSE (the whole list as its filters were left), the classes (the game's types in GarageMyRides' order, a class of its own after the
        /// one it rides in), then the modded and the Alive bikes: each a shortcut into the one list with its filter set.
        void GarageClassesLevel()
        {
            GarageFiltersLoad();
            bool pro = OpenGarageData.Pro;
            var all = OpenGarageData.Vehicles();
            if (BikeMods.All.Count > 0) OpenGarageData.LogModCoverage(all);
            var entries = GarageAllEntries(all);
            int equippedType = pro ? -1 : all.FirstOrDefault(v => v.equipped)?.type ?? -1;
            void Idle(VisualElement card) => card.RegisterCallback<FocusInEvent>(_ => GarageEquippedPreview(OpenGarageStage.Shot.Overview, false));

            // (the list opens as its filters were left: the card says so)
            string leftClass = entries.Any(e => e.cls == gfClass) ? gfClass : "";
            string last = leftClass.Length > 0 || gfSource != GSourceAll ? "  ·  FILTERED: " + GarageListTitle(leftClass, gfSource) : "  ·  FILTER BY CLASS / SOURCE";
            Idle(GarageCard("garage-class-all", "ALL VEHICLES", all.Count(v => v.owned) + " OWNED  ·  " + entries.Count + " IN ALL" + last, true, null, () => GarageEnter(GKind.Models, -1)));

            OuTheme.Section(garageRows, "CLASSES");
            foreach (string key in GarageClassesOf(entries))
            {
                string k = key; int t = GarageClassType(k);
                var of = entries.Where(e => e.cls == k).ToList();
                if (t >= 0)
                {   // a class of the game: its own vehicles are what is owned / ridden (the mods' cards under them ride the same vehicles)
                    var own = of.Where(e => e.item.mod == null).Select(e => e.item).ToList();
                    var eq = own.FirstOrDefault(v => v.equipped);
                    int owned = own.Count(v => v.owned);
                    string sub = (eq != null ? "RIDING  " + GarageBikeTitle(eq.id, eq.playerVehicle) + "  ·  " : "") + owned + " OWNED  ·  " + own.Count + " IN ALL" + (of.Count > own.Count ? "  ·  " + (of.Count - own.Count) + " MODS" : "");
                    var pills = new List<(string, string)>();
                    if (t == equippedType) pills.Add(("EQUIPPED", "good"));
                    if (own.Any(v => v.isNew && !v.locked)) pills.Add(("NEW", "warn"));
                    var card = GarageCard("garage-class-" + t, GarageClassHeading(k), sub, true, null, () => GarageOpenList(k, GSourceAll), pills.ToArray());
                    card.EnableInClassList("ou-gcard-off", owned == 0);
                    Idle(card);
                }
                else
                {   // a class of its own (an imported Alive 50CC / 650 / ATV 1000)
                    bool alive = of.All(e => e.source == GSourceAlive);
                    var card = GarageCard("garage-class-" + GarageClassSlug(k), GarageClassHeading(k), of.Count + (of.Count == 1 ? " VEHICLE" : " VEHICLES") + "  ·  ITS OWN PHYSICS" + (alive ? "  ·  MX vs ATV ALIVE" : ""),
                        true, null, () => GarageOpenList(k, GSourceAll), ("MOD", "mod"));
                    Idle(card);
                }
            }

            int mods = entries.Count(e => e.source == GSourceMods), aliveBikes = entries.Count(e => e.source == GSourceAlive);
            if (mods + aliveBikes > 0) OuTheme.Section(garageRows, "MORE");
            if (mods > 0)
                Idle(GarageCard("garage-class-mods", "MODDED BIKES", mods + " BIKE MOD MODELS  ·  MODS/BIKES", true, null, () => GarageOpenList("", GSourceMods), ("MOD", "mod")));
            if (aliveBikes > 0)
                Idle(GarageCard("garage-class-alive", "ALIVE BIKES", aliveBikes + " IMPORTED VEHICLES  ·  MX vs ATV ALIVE", true, null, () => GarageOpenList("", GSourceAlive), ("MOD", "mod")));
        }

        // ------------------------------------------------------------------------------------------------ the vehicle list

        /// The vehicle list: every vehicle and bike mod card the filters leave. l.a from before the filters (a class's VehicleTypeId, ModdedOnly:
        /// a path kept across a test ride, a test) sets them once.
        bool GarageModelsLevel(GLevel l)
        {
            GarageFiltersLoad();
            if (l.a >= 0) { gfClass = "type:" + l.a; gfSource = GSourceAll; l.a = -1; GarageFiltersSave(); }
            else if (l.a == ModdedOnly) { gfClass = ""; gfSource = GSourceMods; l.a = -1; GarageFiltersSave(); }
            var all = OpenGarageData.Vehicles();
            if (BikeMods.All.Count > 0) OpenGarageData.LogModCoverage(all);
            gfEntries = GarageAllEntries(all);
            if (gfClass.Length > 0 && !gfEntries.Any(e => e.cls == gfClass)) gfClass = "";   // (the class went away: its mod was removed)
            GarageBuildFilterBar();
            GarageFillModels();
            return true;
        }

        /// The filter bar, the list's first rows (it scrolls with the list: the cards get the whole height back): the SOURCE chips with SORT at
        /// their right, then the CLASS chips.
        void GarageBuildFilterBar()
        {
            GarageFiltersReset();
            garageFilters = OuUi.Div("ou-gfilters", garageRows); garageFilters.name = "garage-filters";
            // a chip takes the focus: the whole bar in view (the list's own scroll would show just the chip's row)
            garageFilters.RegisterCallback<FocusInEvent>(_ => { if (garageScroll != null) garageScroll.scrollOffset = Vector2.zero; });
            VisualElement Chip(VisualElement parent, string name, string text, Action submit)
            {
                var c = OuUi.Button(text, "ou-gfchip", submit, parent);
                c.name = name;
                OuUi.Text("", "ou-gfchip-n", c).pickingMode = PickingMode.Ignore;
                return c;
            }
            var src = OuUi.Div("ou-gfilter-row", garageFilters);
            for (int i = 0; i < GSourceNames.Length; i++)
            {
                int s = i;
                gfSourceChips[i] = Chip(src, "garage-filter-source-" + GSourceCodes[i], GSourceNames[i], () => GarageSetFilters(gfClass, s));
            }
            OuUi.Div("ou-spacer", src);
            gfSortChip = OuUi.Button("", "ou-gfchip ou-gfchip-sort", () => GarageSetFilters(gfClass, gfSource, (gfSort + 1) % GSortNames.Length), src);
            gfSortChip.name = "garage-filter-sort";
            var cls = OuUi.Div("ou-gfilter-row", garageFilters);
            gfClassChips[""] = Chip(cls, "garage-filter-class-all", "ALL", () => GarageSetFilters("", gfSource));
            foreach (string key in GarageClassesOf(gfEntries))
            {
                string k = key;
                gfClassChips[k] = Chip(cls, "garage-filter-class-" + GarageClassSlug(k), GarageClassLabel(k), () => GarageSetFilters(k, gfSource));
            }
        }

        /// The bar's elements dropped (the level is built again, or the garage closed).
        void GarageFiltersReset()
        {
            garageFilters = null; gfSortChip = null; gfClassChips.Clear();
            Array.Clear(gfSourceChips, 0, gfSourceChips.Length);
        }

        /// The first card of the list under the filter bar (null: not the vehicle list, or nothing listed and no button).
        VisualElement GarageFirstListItem()
        {
            if (garageFilters == null || garageRows == null) return null;
            return garageRows.Query<VisualElement>(className: "mxa-item").ToList().FirstOrDefault(e => e.userData is OuNavItem && !garageFilters.Contains(e));
        }

        /// The chips as the filters are: the one on, how many vehicles each would list (the other filter kept), the count line.
        void GarageFiltersRefresh(int shown)
        {
            void Set(VisualElement chip, bool on, int n)
            {
                if (chip == null) return;
                chip.EnableInClassList("ou-gfchip-on", on);
                chip.EnableInClassList("ou-gfchip-none", n == 0 && !on);
                var count = chip.Q<Label>(className: "ou-gfchip-n"); if (count != null) count.text = n.ToString();
            }
            for (int i = 0; i < gfSourceChips.Length; i++) Set(gfSourceChips[i], i == gfSource, gfEntries.Count(e => GarageFilterMatch(e, gfClass, i)));
            foreach (var kv in gfClassChips) Set(kv.Value, kv.Key == gfClass, gfEntries.Count(e => GarageFilterMatch(e, kv.Key, gfSource)));
            if (gfSortChip != null) gfSortChip.Q<Label>(className: "mxa-button-label").text = "SORT: " + GSortNames[gfSort];
            gfShown = shown;
        }

        /// The list's cards for the filters and the sort (the bar stays: the focus can be on one of its chips).
        void GarageFillModels()
        {
            for (int i = garageRows.childCount - 1; i >= 0; i--) if (garageRows[i] != garageFilters) garageRows.RemoveAt(i);
            var shown = gfEntries.Where(e => GarageFilterMatch(e, gfClass, gfSource)).ToList();
            shown = gfSort == GSortName
                ? shown.OrderBy(e => ShownModel(e.item), StringComparer.OrdinalIgnoreCase).ThenBy(e => ShownBrand(e.item) ?? "", StringComparer.OrdinalIgnoreCase).ToList()
                : shown.OrderBy(e => e.rank).ToList();   // (a stable sort: inside a class the page's own order)
            GarageFiltersRefresh(shown.Count);
            if (shown.Count == 0) { GarageListEmpty(); return; }
            bool sections = gfSort == GSortClass && gfClass.Length == 0;   // (one class listed: the chip names it)
            string last = null;
            foreach (var e in shown)
            {
                if (sections && e.cls != last) { OuTheme.Section(garageRows, GarageClassHeading(e.cls)); last = e.cls; }
                GarageVehicleCard(e.item, gfClass.Length == 0 && !sections ? GarageClassLabel(e.cls) : null);
            }
        }

        /// A vehicle's card (a bike mod's: the base bike as the mod shows it): EQUIPPED / OWNED / its price / LOCKED; cls: its class named in
        /// the line under the title (a list without class headings).
        void GarageVehicleCard(GarageItem item, string cls)
        {
            string choice = BikeMods.ChoiceFor(item.id);
            string brand = ShownBrand(item);
            bool here = item.equipped && choice == item.modId;   // the card that matches the choice made for the bike
            var pills = new List<(string, string)>();
            if (here) pills.Add(("EQUIPPED", "good"));
            else if (item.owned) pills.Add(("OWNED", null));
            else pills.Add(item.locked ? ("LOCKED", "bad") : (item.price > 0 ? item.price.ToString("N0") : "FREE", "gold"));
            if (item.mod != null) pills.Add(GarageSourceOf(item) == GSourceAlive ? ("ALIVE", "mod") : ("MOD", "mod"));
            if (item.mod?.rx != null && BikeMods.Bikes(item.mod).Any(b => b.importOutdated)) pills.Add(("RE-IMPORT", "warn"));   // (an Alive bike of an older import: no high-res model / colours yet)
            if (item.isNew && !item.locked) pills.Add(("NEW", "warn"));
            if (item.dlc) pills.Add(("DLC", null));
            string by = BikeMods.AuthorFor(item.mod, item.id);   // (a shared bike: the authors of every mod joined into it)
            string sub = (cls != null ? cls + "  ·  " : "") + (brand ?? OpenGarageData.TypeName(item.type)) + (by.Length > 0 ? "  ·  BY " + by : "");
            var card = GarageCard(GarageVehicleCardName(item), ShownModel(item).ToUpperInvariant(), sub.ToUpperInvariant(), false, item.mod == null ? GarageBrandLogo(item.product) : null, () => GarageVehicleAccept(item), pills.ToArray());
            card.EnableInClassList("ou-gcard-off", !item.owned);
            card.RegisterCallback<FocusInEvent>(_ => GarageVehicleShow(item, false));
        }

        /// Nothing listed: why (no mods installed, no Alive vehicles imported, or the two filters share nothing) and the way back to every vehicle.
        void GarageListEmpty()
        {
            string title, text;
            if (gfSource == GSourceAlive && !gfEntries.Any(e => e.source == GSourceAlive))
            {
                title = "NO ALIVE VEHICLES YET";
                text = "GAME LIBRARY > MX vs ATV ALIVE > IMPORT ALIVE CONTENT brings the vehicles of your own copy of MX vs ATV Alive into this garage.";
            }
            else if (gfSource == GSourceMods && !gfEntries.Any(e => e.source == GSourceMods))
            {
                title = "NO BIKE MODS INSTALLED";
                text = "Put .rxmod bike packs (or classic bike mod folders) into Mods/Bikes next to the game, then open the garage again.";
            }
            else
            {
                title = "NO VEHICLES MATCH";
                text = "There is no " + GarageClassLabel(gfClass) + " vehicle" + (gfSource == GSourceOriginal ? " of the original game" : gfSource == GSourceMods ? " among the bike mods" : gfSource == GSourceAlive ? " among the Alive vehicles" : "")
                       + ". Pick another class or source above.";
            }
            var box = OuUi.Div("ou-gempty", garageRows); box.name = "garage-filter-empty";
            OuUi.Text(title, "ou-gempty-title", box);
            OuUi.Text(text, "mxa-note ou-garage-note ou-gempty-text", box);
            var clear = OuUi.Button("SHOW ALL VEHICLES", "mxa-main-button ou-gempty-clear", () => GarageSetFilters("", GSourceAll), box);
            clear.name = "garage-filter-clear";
            clear.RegisterCallback<FocusInEvent>(_ => GarageLevelIdle());
        }

        /// Y / F on the vehicle list: from the list to the filters (the class chip that is on), from the filters back to the list.
        void GarageFilterJump()
        {
            if (!garageBuilt || garageDialog != null || osk != null || GTop?.kind != GKind.Models || garageFilters == null || garageRows == null) return;
            var f = _nav.Focused;
            if (f != null && garageFilters.Contains(f))
            {
                var back = GTop.focus != null ? garageRows.Q<VisualElement>(GTop.focus) : null;
                if (back == null || garageFilters.Contains(back)) back = GarageFirstListItem();
                if (back != null) _nav.Focus(back);
                return;
            }
            if (gfClassChips.TryGetValue(gfClass, out var chip) || gfClassChips.TryGetValue("", out chip)) _nav.Focus(chip);
        }
    }
}
