// GARAGE > VEHICLE > CUSTOMIZE on a .rxmod bike (an imported MX vs ATV Alive vehicle, a bike mod): the parts list is the bike's OWN
// (Content/RxParts.cs: its other graphic kits, exhausts, handlebars, tyres ... declared for it in its mod's manifest or in a "parts" .rxmod
// that names it), never the original game's:
//   * each of the bike's slots is a card (GRAPHIC KIT, EXHAUST ...) leading to its options; A wears one (the client save, RxParts.Set);
//   * a bike with no parts of its own says so (no list of parts that would not show on it);
//   * the original vehicle's part types that still WORK on the bike stay under PERFORMANCE: the ones that bring tuning points (the bike rides
//     as that vehicle, so they are earned and spent exactly as on it: the profile's PlayerVehicleParts / PlayerVehicleTuning are not
//     touched by anything here), and its tyres / wheels when the bike has none of its own (RxParts.OriginalTypeFits).
// An original bike lists the original parts and the kit packs as before: a .rxmod part has no entry there.
// Names: garage-rxslot-<n> (a slot's card), garage-rxpart-<n>-<k> (an option's card).
using System;
using System.Collections.Generic;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.FrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.OpenFrontEnd
{
    public sealed partial class OpenShell
    {
        /// A part level's type id of a .rxmod bike's slot (GKind.PartSection's a): RxSlotBase + the slot's index.
        public const int RxSlotBase = 1000000;

        /// The .rxmod bike the equipped vehicle is ridden as (the chosen mod's bike, or the one a chosen skin paints), null: the original / a classic mod.
        static RxModBike GarageRxBike(int model) => OpenGarageData.Pro || model < 0 ? null : BikeMods.RxBikeFor(model);
        static bool GarageRxAlive(RxModBike b) => (BikeMods.All.FirstOrDefault(m => m.rx == b.mod)?.id ?? "").StartsWith(Reflex.Port.Content.AliveImport.AliveBikes.Folder + "/", StringComparison.Ordinal);
        static string GarageRxBikeName(RxModBike b) => (b.name?.Full ?? b.mod.name).ToUpperInvariant();

        /// The original part types the garage lists for a vehicle as it is ridden: all of them on the original bike (or a classic .dx9 mod, whose
        /// packages take the original parts); on a .rxmod bike only those that still do something on it (RxParts.OriginalTypeFits).
        internal static List<(int id, string factory, string tuning, string name)> GarageListedPartTypes(int vehicle, int model)
        {
            var types = OpenGarageData.PartTypes(vehicle);
            var rx = GarageRxBike(model);
            if (rx == null) return types;
            bool wheels = RxParts.HasOwnWheels(rx);
            return types.Where(t => RxParts.OriginalTypeFits(t.factory, t.tuning, wheels)).ToList();
        }

        /// The bike's own slots (or that it has none), then the PERFORMANCE header for the original types that follow. Returns those types.
        List<(int id, string factory, string tuning, string name)> GarageRxCustomize(RxModBike bike, int vehicle, int model)
        {
            string name = GarageRxBikeName(bike);
            bool alive = GarageRxAlive(bike);
            if (bike.importOutdated)
            {
                var old = OuUi.Text(name + " WAS IMPORTED BY AN OLDER VERSION. Import it again for its high-res garage model and its number plate, rim and hub colours: GAME LIBRARY > MX vs ATV ALIVE, "
                    + "with the source it came from (the disc for Alive's own bikes, your downloaded content folder for the Honda, Yamaha, Suzuki and KTM packs).", "mxa-note ou-garage-note", garageRows);
                old.name = "garage-reimport-note";
            }
            if (bike.slots.Count == 0)
                GarageNote(name + " HAS NO PARTS OF ITS OWN. " + (alive ? "MX vs ATV Alive has no other kit or part for it." : "Its mod carries none, and nothing installed is made for it.")
                    + " Parts of the original bike it rides as do not fit it, so they are not listed.");
            for (int i = 0; i < bike.slots.Count; i++)
            {
                var slot = bike.slots[i]; int index = i;
                var worn = RxParts.Chosen(bike, slot);
                var card = GarageCard("garage-rxslot-" + i, slot.name, (worn?.Title ?? "STOCK").ToUpperInvariant(), true, null, () => GarageEnter(GKind.PartSection, RxSlotBase + index),
                    (slot.options.Count + " OPTIONS", null), (alive ? "ALIVE" : "MOD", "mod"));
                card.RegisterCallback<FocusInEvent>(_ => { GaragePartEquippedPreview(slot.id, true); GarageRxSubject(bike, slot, worn, true); });
            }
            var types = GarageListedPartTypes(vehicle, model);
            if (types.Count > 0)
            {
                OuTheme.Section(garageRows, "PERFORMANCE  ·  TUNING POINTS");
                GarageNote(name + " rides as the " + GarageOriginalTitle(model) + ": that bike's performance parts still bring their tuning points here (VEHICLE TUNING), bought and fitted as on it. They are not drawn on this bike.");
            }
            return types;
        }

        static string GarageOriginalTitle(int model)
        {
            var row = FeGarageData.Unique("VehicleModel", "VehicleModelId", model);
            return row == null ? "ORIGINAL BIKE" : OpenGarageData.Title(FeGarageData.ProductOfAsset(FeTables.I(row, "StoreAssetId")), "ORIGINAL BIKE").ToUpperInvariant();
        }

        string GarageRxSlotName(int type)
        {
            var bike = GarageRxBike(OpenGarageData.EquippedModel); int i = type - RxSlotBase;
            return bike != null && i >= 0 && i < bike.slots.Count ? bike.slots[i].name : "PART";
        }

        /// A slot's options: the default first, then by line and name; a line's options under its header.
        bool GarageRxPartSection(int type)
        {
            if (OpenGarageData.Pro) return false;
            int vehicle = OpenGarageData.Equipped, model = OpenGarageData.EquippedModel, index = type - RxSlotBase;
            var bike = GarageRxBike(model);
            if (bike == null || index < 0 || index >= bike.slots.Count) return false;
            var slot = bike.slots[index];
            var worn = RxParts.Chosen(bike, slot);
            bool alive = GarageRxAlive(bike);
            var ordered = slot.options.Select((o, k) => (o, k)).OrderBy(q => q.o.isDefault ? 0 : 1).ThenBy(q => q.o.line, StringComparer.OrdinalIgnoreCase).ThenBy(q => q.o.name, StringComparer.OrdinalIgnoreCase).ThenBy(q => q.k).ToList();
            string line = null;
            foreach (var (o, k) in ordered)
            {
                var option = o;
                if (!o.isDefault && o.line.Length > 0 && !string.Equals(o.line, line, StringComparison.OrdinalIgnoreCase)) OuTheme.Section(garageRows, o.line.ToUpperInvariant());
                if (!o.isDefault) line = o.line;
                string cardName = "garage-rxpart-" + index + "-" + k;
                bool on = o == worn;
                string sub = o.isDefault ? "AS THE BIKE COMES" : o.line.Length > 0 ? null : o.brand.Length > 0 ? o.brand.ToUpperInvariant() : null;
                var card = GarageCard(cardName, (o.line.Length > 0 && !o.isDefault ? o.name : o.Title).ToUpperInvariant(), sub, false, null, () =>
                {
                    if (RxParts.Chosen(bike, slot) == option) { GarageStatus("ALREADY EQUIPPED  ·  " + option.Title.ToUpperInvariant()); return; }
                    RxParts.Set(bike, option);
                    WorldPicked();
                    GarageChanged(true, "EQUIPPED  ·  " + slot.name + "  ·  " + option.Title.ToUpperInvariant(), cardName);
                }, on ? ("EQUIPPED", "good") : ("OWNED", null), o.isDefault ? default : (alive ? "ALIVE" : "MOD", "mod"));
                card.RegisterCallback<FocusInEvent>(_ =>
                {
                    var wornNow = RxParts.Chosen(bike);
                    var parts = OpenGarageData.EquippedPartIds(vehicle);
                    GarageShow("rxpart:" + model + ":" + string.Join(",", parts) + ":" + RxParts.Signature(RxParts.With(wornNow, option)), () =>
                    {
                        var spec = OpenGarageData.BikeWithPart(model, parts, -1);
                        if (spec?.rx == null) return (spec, null, false);
                        var s = spec.Clone(); s.rxParts = RxParts.With(spec.rxParts, option);
                        return (s, null, false);
                    }, PartShot(slot.id), false);
                    GarageRxSubject(bike, slot, option, RxParts.Chosen(bike, slot) == option);
                });
            }
            GarageNote((alive ? "MX vs ATV Alive's " : "The mod's ") + slot.name + " options for " + GarageRxBikeName(bike) + ": they fit this bike only and are listed on no other. They change its look; tuning points come from PERFORMANCE.");
            return true;
        }

        /// The stage's title block for a .rxmod part in view (the vehicle's bars stay).
        void GarageRxSubject(RxModBike bike, RxPartSlot slot, RxPartOption o, bool equipped)
        {
            if (garageMake == null || o == null) return;
            GarageSubject(OpenGarageData.EquippedModel, OpenGarageData.Equipped);
            garageBadges.Clear();
            garageMake.text = (o.isDefault ? GarageRxBikeName(bike) : o.line.Length > 0 ? o.line : o.brand.Length > 0 ? o.brand : slot.name).ToUpperInvariant();
            garageModelName.text = (o.line.Length > 0 && !o.isDefault ? o.name : o.Title).ToUpperInvariant();
            OuUi.Text(slot.name, "ou-badge", garageBadges);
            if (equipped) OuUi.Text("EQUIPPED", "ou-badge ou-badge-dark", garageBadges);
            else { var tag = OuUi.Text("PREVIEW", "ou-badge ou-badge-preview", garageBadges); tag.name = "garage-preview-tag"; }
            OuUi.Text(GarageRxAlive(bike) ? "ALIVE" : "MOD", "ou-badge", garageBadges);
        }
    }
}
