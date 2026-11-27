using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Reflex.Port.Content;

namespace Reflex.Port.OriginalWorld
{
    // B.R.A.A.P. (Content/ReflexBraap.cs): the roster in use over an event's computer riders. The core made each of them from its own AIRider
    // row, on its own AIVehicle; here the entry Unity draws and names takes the roster rider's name, plate text, gear and bike graphics. The
    // core's ids stay (partIds / gearIds), as with a gear / kit pack: only the drawing and the names change.
    public sealed partial class ReflexRoster
    {
        /// Never throws: whatever does not resolve leaves that part of the rider as the game made it.
        static void ApplyBraap(Entry e)
        {
            if (e == null || !e.Computer || e.riderOwner != 4) return;   // (a remote rider's row, a pro the player rides: not the AI's)
            var r = ReflexBraap.RiderFor(e.ownerRiderId >= 0 ? e.ownerRiderId : 100000 + e.slot.slot);
            if (r == null) return;
            string was = e.name;
            try
            {
                e.name = r.name;
                e.aiTier = ReflexBraap.SkillTier(r.skill);
                e.plate ??= new ReflexPlateText();
                e.plate.name = ReflexBraap.PlateName(r);
                e.plate.number = Mathf.Clamp(r.number, 0, 999).ToString();
                var t = KitPacks.Tables(new InstallSource());
                var used = new List<string>();

                // the gear: AUTO's set (by his name) with his picks; the plate text sits where this gear carries it (0054bf50)
                int[] gear = ReflexBraap.GearIds(r, e.gearIds);
                if (gear.Length > 0 && !gear.SequenceEqual(e.gearIds))
                {
                    var spec = ReflexVehicleSpecs.Rider(t, gear);
                    if (spec != null)
                    {
                        // under a "gear" .rxmod suit the head, neck, wrists and gloves are a Reflex jersey's: the one AUTO gives him
                        var suit = spec.parts.FirstOrDefault(p => p.rx != null && p.rx.slot == "jersey");
                        if (suit != null)
                        {
                            var stock = ReflexVehicleSpecs.Rider(t, gear.Where(id => !RxGear.IsId(id)).Concat(ReflexBraap.AutoGear(r.name).Where(id => ReflexBraap.GearTypeOf(id) == 3)));
                            RxGear.Body(suit, stock?.parts.FirstOrDefault(p => p.key == "jersey" && p.rx == null));
                        }
                        if (e.rider != null) spec.plateRider = e.rider.plateRider;
                        e.rider = spec;
                        var pt = ReflexPlateText.ForSpecs(null, spec);
                        e.plate.onJersey = pt.onJersey; e.plate.invert = pt.invert;
                        used.AddRange(gear.Select(KitPacks.Item).Where(k => k != null).Select(k => k.Token));
                    }
                }

                // the bike graphics: the kit pack pieces of his that fit the bike the core gave him
                if (r.kits != null && r.kits.Count > 0 && e.bike != null && e.vehicleModelId >= 0 && e.bike.rx == null && e.bike.rxSkin == null)
                {
                    var parts = KitPacks.PartIds(t, e.vehicleModelId, e.partIds, r.kits.ToArray());
                    if (!parts.SequenceEqual(e.partIds))
                    {
                        var b = ReflexVehicleSpecs.Bike(t, e.vehicleModelId, parts);
                        if (b != null) { e.bike = b; used.AddRange(parts.Select(KitPacks.Item).Where(k => k != null).Select(k => k.Token)); }
                    }
                }
                e.kits = used.ToArray();
            }
            catch (Exception ex) { Debug.LogWarning($"[Braap] slot {e.slot.slot} ({was} -> {r.name}): {ex.Message}: the rest of him stays the game's"); }
        }
    }
}
