// An imported MX vs ATV Alive vehicle's HIGH-RES set: Alive, as Reflex, keeps a second model of every vehicle and part for its garage and
// menus in "HR_" databases (HR_MXSUZDLC2/HR_MXSUZDLC2_MX1, HR_MX_exhaust, HR_MX_tire, HR_MX_graphickit ...: one LOD, about four times the
// race model's vertices - the Suzuki RMZ450's chassis 35,060 against 9,322, its stock exhaust 2,253 against 304, a tyre 18,082 against
// 1,918 - with the cables, levers and hubs modelled, and its own maps). The race model is what the bike's own folder holds (and what a
// race draws); this set goes into a folder of its own beside it, <content>/AliveBikes/<slug>-hr/:
//   mod.json   "type": "hr", naming the bike as a parts mod does ("mod" / "bike")
//   bike/      the high-res bike, made exactly as the bike was (AliveBikes.ConvertBike) from the HR twins of its packages
//   parts/     its kits and parts (AliveBikeParts.ConvertParts) from the HR twins of theirs (a part without a twin: its race model)
// The client draws it where the game draws its own HR twins: the garage and the menus (ReflexBikeModel.RxMod.cs). The bike's own folder is
// not touched (its content hash is the online token), so a player without the HR set rides the same bike.
// Vehicles without HR databases in Alive's data have none: the 50cc, the MXFR 650, the ATV 1000 and the MX250 FMX.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Reflex.Port.Content.AliveImport
{
    public static partial class AliveBikes
    {
        public const string HrSuffix = "-hr";

        /// A package's HR twin ("HR_<database>" / "HR_<package>", or the package's own name there); null: none in the sources.
        static (string db, string pkg)? HrTwin(PackageIndex index, (string db, string pkg)? p, CancellationToken cancel)
        {
            if (p == null || p.Value.db == null || p.Value.pkg == null || p.Value.db.StartsWith("HR_", StringComparison.OrdinalIgnoreCase) || !index.Has("HR_" + p.Value.db)) return null;
            foreach (var c in new[] { ("HR_" + p.Value.db, "HR_" + p.Value.pkg), ("HR_" + p.Value.db, p.Value.pkg) })
            {
                var a = index.Read(c.Item1, c.Item2, cancel);
                if (a != null && a.assets.Count > 0) return c;
            }
            return null;
        }

        /// The bike with the HR twins of its packages (a package without one: its own); null: its chassis has no twin.
        static AliveBikeSource HrSource(AliveBikeSource b, PackageIndex index, CancellationToken cancel)
        {
            var chassis = HrTwin(index, b.chassis, cancel);
            if (chassis == null) return null;
            var h = new AliveBikeSource
            {
                aliveId = b.aliveId, product = b.product, brand = b.brand, model = b.model, pack = b.pack, vehicleType = b.vehicleType, physics = b.physics, hierarchy = b.hierarchy, atv = b.atv,
                ridesAs = b.ridesAs, ridesAsName = b.ridesAsName, ownClass = b.ownClass, problem = b.problem, chassis = chassis.Value, kit = HrTwin(index, b.kit, cancel) ?? b.kit,
            };
            // the race frame's lowest point: what the HR frame mesh has wholly below it is Alive's garage stand's base (AliveBikeSource.hrFloor)
            var race = index.Read(b.chassis.db, b.chassis.pkg, cancel);
            if (race != null)
            {
                var rm = ReadModel(race, ModelName(race, b.hierarchy), out var rs);
                float floor = float.PositiveInfinity;
                foreach (var bd in rm.Lod(0))
                    if (!rs[bd.surface].skinned && string.Equals(rm.nodes[bd.node].name, "OBJ_Frame", StringComparison.OrdinalIgnoreCase))
                        foreach (var p in rs[bd.surface].positions) floor = Math.Min(floor, rm.nodes[bd.node].world.MultiplyPoint3x4(p).y);
                if (!float.IsInfinity(floor)) h.hrFloor = floor;
            }
            foreach (var kv in b.colors) h.colors[kv.Key] = kv.Value;
            h.palette.AddRange(b.palette);
            foreach (var p in b.parts)
                h.parts.Add(new AlivePart { partId = p.partId, type = p.type, model = p.type == "GraphicKits" ? chassis : HrTwin(index, p.model, cancel) ?? p.model,
                                            materials = HrTwin(index, p.materials, cancel) ?? p.materials });
            foreach (var s in b.slots)
            {
                var hs = new AlivePartSlot { type = s.type, factory = s.factory, tuning = s.tuning, ordinal = s.ordinal };
                foreach (var o in s.options)
                {
                    var ho = new AlivePartOption { partId = o.partId, name = o.name, line = o.line, brand = o.brand, isDefault = o.isDefault, tune1 = o.tune1, tune2 = o.tune2,
                                                   model = s.type == "GraphicKits" ? chassis : HrTwin(index, o.model, cancel) ?? o.model, materials = HrTwin(index, o.materials, cancel) ?? o.materials };
                    foreach (var kv in o.colors) ho.colors[kv.Key] = kv.Value;
                    hs.options.Add(ho);
                }
                h.slots.Add(hs);
            }
            return h;
        }

        /// The bike's high-res set into `folder` (see the head of this file). False: the vehicle has no HR model in the sources (nothing written).
        static bool ConvertHr(AliveBikeSource b, PackageIndex index, string folder, AliveBikeResult res, CancellationToken cancel)
        {
            var hb = HrSource(b, index, cancel);
            if (hb == null) return false;
            var r = new AliveBikeResult { bike = hb };
            var build = ConvertBike(hb, index, Path.Combine(folder, "bike"), r, cancel);
            try { ConvertParts(build, Path.Combine(folder, "parts"), r); }
            catch (Exception e) when (e is AliveImportException || e is InvalidDataException || e is IOException || e is ArgumentException || e is IndexOutOfRangeException)
            { res.hrNotes.Add("its kits and parts: " + e.Message); try { if (Directory.Exists(Path.Combine(folder, "parts"))) Directory.Delete(Path.Combine(folder, "parts"), true); } catch (IOException) { } }
            var manifest = new JObj().Set("format", "reflex-mod").Set("version", 1L).Set("type", "hr").Set("id", b.Slug + HrSuffix)
                .Set("name", b.Label + " high-res model").Set("author", Author)
                .Set("description", $"MX vs ATV Alive's high-res garage model of the {b.Label} with its kits and parts, imported from your own copy; drawn in the garage and the menus")
                .Set("mod", b.Slug).Set("bike", BikeId(b));
            var text = Encoding.UTF8.GetBytes(Json.Dumps(manifest, 1) + "\n");
            File.WriteAllBytes(Path.Combine(folder, "mod.json"), text);
            res.hrFolder = folder; res.hrVertices = r.vertices; res.hrSurfaces = r.surfaces; res.hrOptions = r.partOptions; res.hrBytes = r.bytes + r.partBytes + text.Length;
            foreach (var n in r.notes.Where(n => !n.StartsWith("material ", StringComparison.Ordinal))) res.hrNotes.Add(n);
            foreach (var n in r.partNotes) res.hrNotes.Add(n);
            res.hrMaterialNotes.AddRange(r.notes.Where(n => n.StartsWith("material ", StringComparison.Ordinal)));
            return true;
        }
    }
}
