// TEST ONLY (tests/alive_bikes): drives Content/AliveImport/AliveBikes.cs outside Unity. Scans the sources, lists the Alive motorcycles,
// converts the chosen ones (--bikes <Alive VehicleModelIds>|all) into --out DIR/<slug> (a .rxmod folder each), and checks every written
// map is a PNG and the mesh reads back (RxMesh layout).
//   AliveBikes.dll [--out DIR] [--bikes 41,36|all] [--dump] [--parts] [--install CONTENT] SOURCE...
// --parts: each bike's other graphic kits and parts too (AliveBikeParts.cs), into DIR/<slug>-parts (a "parts" .rxmod folder), listed per type.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Reflex.Port.Content.AliveImport;

static class Program
{
    static int Main(string[] args)
    {
        var sources = new List<string>(); string outDir = null, bikes = null, install = null; bool dump = false, parts = false, quiet = false, hr = false; string gear = null, installGear = null; int gearLimit = int.MaxValue;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out") outDir = args[++i];
            else if (args[i] == "--bikes") bikes = args[++i];
            else if (args[i] == "--dump") dump = true;
            else if (args[i] == "--parts") parts = true;
            else if (args[i] == "--hr") hr = true;       // each bike's high-res garage set too (AliveBikeHr.cs), into DIR/<slug>-hr
            else if (args[i] == "--quiet") quiet = true;
            else if (args[i] == "--gear") gear = args[++i];               // all | a slot list (helmet,goggles,jersey,boots,neck_brace): the rider gear into --out DIR/gear/<slug>
            else if (args[i] == "--gear-limit") gearLimit = int.Parse(args[++i]);   // at most N models per slot (a quick look)
            else if (args[i] == "--install-gear") installGear = args[++i];   // a content folder: AliveBikes.ImportGear into <content>/AliveGear
            else if (args[i] == "--install") install = args[++i];   // a content folder: AliveBikes.Import into <content>/AliveBikes, as the game's IMPORT does
            else sources.Add(args[i]);
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var found = AliveImporter.Scan(sources, s => { }, CancellationToken.None);
        Console.WriteLine($"scan {clock.Elapsed.TotalSeconds:0.0} s: {found.levels.Count} levels, {found.bikes.Count} bikes, {found.volumes.Count} sources");
        foreach (var w in found.warnings) Console.WriteLine("warning: " + w);
        foreach (var b in found.bikes)
        {
            Console.WriteLine($"  #{b.aliveId,-3} {b.Label,-24} {b.vehicleType,-6} {b.physics,-16} {b.pack,-30} " + (b.Importable ? $"rides as #{b.ridesAs} {b.ridesAsName}" : "NOT IMPORTED: " + b.problem));
            if (dump) foreach (var p in b.parts) Console.WriteLine("        " + p);
            if (dump) foreach (var s in b.slots) Console.WriteLine($"        slot {s.type} ({s.factory}, tuning '{s.tuning}'): " + string.Join(", ", s.options.Select(o => (o.isDefault ? "*" : "") + (o.line.Length > 0 ? o.line + " " : "") + o.name)));
        }
        if (gear != null || installGear != null)
        {
            var models = AliveBikes.FindGear(found, CancellationToken.None);
            Console.WriteLine($"gear: {models.Count} models, {models.Sum(m => m.pieces.Count)} pieces: " + string.Join(", ", models.GroupBy(m => m.slot).Select(g => $"{g.Key} {g.Count()} models / {g.Sum(m => m.pieces.Count)} pieces")));
            var slots = gear == null || gear == "all" ? null : gear.Split(',');
            var chosen = models.Where(m => slots == null || slots.Contains(m.slot)).GroupBy(m => m.slot).SelectMany(g => g.Take(gearLimit)).ToList();
            if (dump) foreach (var m in chosen) Console.WriteLine($"  {m.slot,-10} {m.Title,-34} {m.model.db}/{m.model.pkg}: " + string.Join(", ", m.pieces.Select(p => p.name)));
            var gclock = System.Diagnostics.Stopwatch.StartNew();
            List<AliveGearResult> grs;
            if (installGear != null) grs = AliveBikes.ImportGear(chosen, found, installGear, s => { }, CancellationToken.None, models);
            else
            {
                grs = new List<AliveGearResult>();
                foreach (var m in chosen)
                {
                    string dest = Path.Combine(outDir, "gear", m.Slug);
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    Directory.CreateDirectory(Path.Combine(outDir, "gear"));
                    grs.Add(AliveBikes.ConvertGear(m, found, dest, CancellationToken.None, models));
                }
            }
            foreach (var r in grs)
            {
                Console.WriteLine($"{r.model.slot} {r.model.Title}: " + (r.ok ? $"ok {r.pieces} of {r.model.pieces.Count} pieces, {r.maps} maps, {r.bytes / 1e6:0.0} MB" : "FAILED " + r.error));
                if (!quiet) foreach (var n in r.notes) Console.WriteLine("    note: " + n);
            }
            Console.WriteLine($"gear: {grs.Count(r => r.ok)} of {grs.Count} models, {grs.Sum(r => r.pieces)} pieces, {grs.Sum(r => r.bytes) / 1e6:0} MB, {gclock.Elapsed.TotalSeconds:0} s");
            return grs.All(r => r.ok) ? 0 : 1;
        }
        if (install != null)
        {
            var chosen = bikes == null || bikes == "all" ? found.bikes.Where(b => b.Importable).ToList() : bikes.Split(',').Select(int.Parse).Select(id => found.bikes.First(b => b.aliveId == id)).ToList();
            var rs = AliveBikes.Import(chosen, found, install, s => { }, CancellationToken.None, parts, hr);
            foreach (var r in rs) Console.WriteLine($"{r.bike.Label}: " + (r.ok ? $"installed {r.folder}" + (r.partsFolder != null ? $" + {r.partSlots} part types, {r.partOptions} options, {r.partBytes / 1e6:0.0} MB" : r.partsError != null ? " (parts FAILED: " + r.partsError + ")" : "") : "FAILED " + r.error));
            return rs.All(r => r.ok) ? 0 : 1;
        }
        if (outDir == null || bikes == null) return 0;
        var want = bikes == "all" ? found.bikes.Where(b => b.Importable).ToList() : bikes.Split(',').Select(int.Parse).Select(id => found.bikes.First(b => b.aliveId == id)).ToList();
        Directory.CreateDirectory(outDir);
        int bad = 0;
        foreach (var b in want)
        {
            string dest = Path.Combine(outDir, b.Slug), pdest = dest + AliveBikes.PartsSuffix, hdest = dest + AliveBikes.HrSuffix;
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            if (Directory.Exists(pdest)) Directory.Delete(pdest, true);
            if (Directory.Exists(hdest)) Directory.Delete(hdest, true);
            var r = AliveBikes.Convert(b, found, dest, CancellationToken.None, parts ? pdest : null, hr ? hdest : null);
            Console.WriteLine($"{b.Label}: " + (r.ok ? $"ok {r.surfaces} surfaces, {r.vertices} vertices, {r.triangles} triangles, {r.maps} maps, {r.bytes / 1e6:0.0} MB, {r.seconds:0.0} s" : "FAILED " + r.error));
            if (!quiet) foreach (var n in r.notes) Console.WriteLine("    note: " + n);
            if (parts) Console.WriteLine(r.partsFolder != null ? $"    parts: {r.partSlots} types, {r.partOptions} options, {r.partMeshes} meshes, {r.partMaps} maps, {r.partBytes / 1e6:0.0} MB" : "    parts: " + (r.partsError != null ? "FAILED " + r.partsError : "none"));
            foreach (var n in r.partNotes) Console.WriteLine("    parts note: " + n);
            if (hr) Console.WriteLine(r.hrFolder != null ? $"    hr: {r.hrSurfaces} surfaces, {r.hrVertices} vertices (the race model {r.vertices}), {r.hrOptions} options, {r.hrBytes / 1e6:0.0} MB" : "    hr: " + (r.hrError ?? "none (the vehicle has no HR model in the sources)"));
            if (hr && !quiet) foreach (var n in r.hrMaterialNotes) Console.WriteLine("    hr note: " + n);
            foreach (var n in r.hrNotes) Console.WriteLine("    hr note: " + n);
            if (!r.ok) { bad++; continue; }
            foreach (var f in Directory.GetFiles(Path.Combine(dest, "maps")))
            {
                var h = File.ReadAllBytes(f);
                if (h.Length < 8 || h[1] != 'P' || h[2] != 'N' || h[3] != 'G') { Console.WriteLine("    BAD PNG " + f); bad++; }
            }
        }
        return bad == 0 ? 0 : 1;
    }
}
