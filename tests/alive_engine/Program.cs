// TEST ONLY (tests/alive_engine): drives Content/AliveImport/AliveEngineSounds.cs outside Unity (core/plan/ALIVE_ENGINE_SOUNDS.md "Tests").
//   AliveEngine.dll list SOURCE...                                                 every engine sound set found and its vehicles
//   AliveEngine.dll convert --out CONTENT [--sets all|MX_450_TRACK,...] [--ffmpeg F] SOURCE...
//                                                                                  AliveEngineSounds.Import into CONTENT/AliveEngineSounds
//   AliveEngine.dll check CONTENT [--twin CONTENT2] [--sets ...] SOURCE...         verifies every converted set against the sources:
//       the manifest (every file listed with its sha256, nothing else in the folder); set.json (format, project, the vehicles, the calibration trim); the FEV
//       is the .sound's FEV1 byte for byte and parses (ReflexFmod.ParseFev, v0x40), its wave entries point into the bank; the FSB parses
//       (ReflexFmod.ParseFsb) with the disc bank's sample count, per sample its length, rate, loop points, name and SYNC points (count,
//       positions, names), PCM16 mono, the PCM not silent; both XML files load (roots audio_engine / engineactionlists, sound = the
//       project); --twin: a second conversion's set folders are byte-identical (determinism).
// ffmpeg: --ffmpeg, else REFLEX_FFMPEG. Exit 0: ALIVE ENGINE PASS.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;
using Reflex.Port.Content;
using Reflex.Port.Content.AliveImport;

static class Program
{
    static int failures;
    static void Fail(string s) { failures++; Console.WriteLine("FAIL " + s); }

    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: list|convert|check ... SOURCE..."); return 2; }
        string cmd = args[0], outDir = null, sets = "all", ffmpeg = Environment.GetEnvironmentVariable("REFLEX_FFMPEG"), twin = null, content = null;
        var sources = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--out") outDir = args[++i];
            else if (args[i] == "--sets") sets = args[++i];
            else if (args[i] == "--ffmpeg") ffmpeg = args[++i];
            else if (args[i] == "--twin") twin = args[++i];
            else if (cmd == "check" && content == null) content = args[i];
            else sources.Add(args[i]);
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var found = AliveImporter.Scan(sources, s => { }, CancellationToken.None);
        Console.WriteLine($"scan {clock.Elapsed.TotalSeconds:0.0} s: {found.bikes.Count} vehicles, {found.engineSounds.Count} engine sound sets, {found.volumes.Count} sources");
        foreach (var w in found.warnings) Console.WriteLine("warning: " + w);
        var want = sets == "all" ? found.engineSounds.ToList() : sets.Split(',').Select(p => found.engineSounds.FirstOrDefault(s => s.project == p) ?? throw new ArgumentException("no set " + p)).ToList();

        if (cmd == "list")
        {
            clock.Restart(); AliveEngineSounds.Find(found, CancellationToken.None);
            Console.WriteLine($"AliveEngineSounds.Find alone: {clock.Elapsed.TotalSeconds:0.0} s");
            foreach (var s in found.engineSounds)
            {
                Console.WriteLine($"{s.project,-14} {s.name,-9} {s.db}/{s.pkg}  actions {s.actions}  config {s.config}  bank {s.bank}  {s.samples} samples {s.seconds:0.0} s ~{s.Bytes / 1e6:0.0} MB  [{s.source}]" + (s.problem != null ? "  PROBLEM: " + s.problem : ""));
                Console.WriteLine("    " + s.Label);
                for (int i = 0; i < s.aliveIds.Count; i++) Console.WriteLine($"    #{s.aliveIds[i],-3} {s.vehicles[i]}");
            }
            return 0;
        }
        if (cmd == "convert")
        {
            if (outDir == null || string.IsNullOrEmpty(ffmpeg)) { Console.WriteLine("convert needs --out and --ffmpeg (or REFLEX_FFMPEG)"); return 2; }
            Console.WriteLine("xma2 decoder: " + AliveFfmpeg.HasDecoder(ffmpeg, "xma2"));
            Directory.CreateDirectory(outDir);
            string last = null;
            var rs = AliveEngineSounds.Import(want, found, outDir, ffmpeg, p => { if (p.level != last) { last = p.level; Console.WriteLine("  " + p.level + " " + p.stage + " " + p.total); } }, CancellationToken.None);
            foreach (var r in rs) Console.WriteLine($"{r.set.project}: " + (r.ok ? $"ok {r.samples} samples, {r.syncs} sync points, {r.bytes / 1e6:0.0} MB, {r.seconds:0.0} s -> {r.folder}" : "FAILED " + r.error));
            return rs.Count == want.Count && rs.All(r => r.ok) ? 0 : 1;
        }
        if (cmd == "check")
        {
            if (content == null) { Console.WriteLine("check needs CONTENT"); return 2; }
            foreach (var s in want) Check(s, found, content, twin);
            Console.WriteLine(failures == 0 ? $"ALIVE ENGINE PASS ({want.Count} sets)" : $"ALIVE ENGINE FAIL ({failures})");
            return failures == 0 ? 0 : 1;
        }
        Console.WriteLine("unknown command " + cmd); return 2;
    }

    static string Sha(string f) { using (var h = SHA256.Create()) using (var s = File.OpenRead(f)) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }

    static void Check(AliveEngineSoundSource s, AliveSources found, string content, string twin)
    {
        string P = s.project, dir = Path.Combine(AliveEngineSounds.Root(content), P);
        if (s.problem != null) { Fail($"{P}: {s.problem}"); return; }
        if (!Directory.Exists(dir)) { Fail($"{P}: {dir} missing"); return; }
        int before = failures;
        // the manifest
        string mf = Path.Combine(AliveEngineSounds.Root(content), P + ".import.json");
        if (!File.Exists(mf)) Fail($"{P}: no manifest");
        else
        {
            var m = (JObj)Json.Parse(File.ReadAllText(mf));
            var listed = new HashSet<string>();
            foreach (JObj f in (IEnumerable<object>)m.Get("files"))
            {
                string rel = (string)f.Get("path"); listed.Add(rel);
                string path = Path.Combine(dir, rel);
                if (!File.Exists(path)) Fail($"{P}: manifest lists missing {rel}");
                else if (Sha(path) != (string)f.Get("sha256")) Fail($"{P}: {rel} sha256 differs from the manifest");
            }
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                if (!listed.Contains(f.Substring(dir.Length + 1).Replace('\\', '/'))) Fail($"{P}: {f} not in the manifest");
        }
        // set.json
        var set = (JObj)Json.Parse(File.ReadAllText(Path.Combine(dir, "set.json")));
        if (!(set.Get("format") is long fm) || fm != 1) Fail($"{P}: set.json format");
        if ((string)set.Get("project") != P) Fail($"{P}: set.json project");
        if ((string)set.Get("actions") != s.actions) Fail($"{P}: set.json actions");
        var ids = ((IEnumerable<object>)set.Get("vehicles")).Cast<JObj>().Select(v => (int)(long)v.Get("alive_id")).ToList();
        if (!ids.SequenceEqual(s.aliveIds) || ids.Count == 0) Fail($"{P}: set.json vehicles {string.Join(",", ids)} != {string.Join(",", s.aliveIds)}");
        var trim = set.Get("trim"); var want = AliveEngineSounds.TrimOf(P);
        if (want is double wt ? !(trim is double t && t == wt) : trim != null) Fail($"{P}: set.json trim {trim ?? "none"} != {(object)want ?? "none"}");

        // the sources' files
        var pa = new AliveBikes.PackageIndex(found.volumes).Read(s.db, s.pkg, CancellationToken.None);
        byte[] sound = pa.Find(P, "sound"), bank = pa.Find(s.bank, "soundbnk");
        var orig = AliveFsb.Read(bank);

        // the FEV
        var fevBytes = File.ReadAllBytes(Path.Combine(dir, P + ".fev"));
        int fevAt = 0x44 + 64 * BitConverter.ToInt32(sound, 0x40) + 4;
        if (!sound.Skip(fevAt).Take(fevBytes.Length).SequenceEqual(fevBytes) || fevAt + fevBytes.Length != sound.Length) Fail($"{P}: the .fev is not the .sound's FEV1");
        var fev = ReflexFmod.ParseFev(fevBytes);
        int events = 0, entries = 0;
        if (fev == null) Fail($"{P}: ParseFev returned null");
        else
        {
            if (fev.version != ReflexFmod.FevVersionAlive) Fail($"{P}: FEV version {fev.version:x8}");
            events = fev.Events().Count();
            foreach (var sd in fev.soundDefs)
                foreach (var e in sd.entries.Where(e => e.kind == "wave"))
                {
                    entries++;
                    if (e.index >= orig.Count) Fail($"{P}: sound def {sd.name}: wave index {e.index} outside the bank ({orig.Count})");
                }
        }

        // the FSB
        var fsbBytes = File.ReadAllBytes(Path.Combine(dir, P + ".fsb"));
        ReflexFmod.Fsb fsb = null;
        try { fsb = ReflexFmod.ParseFsb(fsbBytes); } catch (Exception e) { Fail($"{P}: ParseFsb: {e.Message}"); }
        int syncs = 0, quiet = 0, notPcm = 0, loops = 0; double minPeak = 1;
        if (fsb != null)
        {
            if (fsb.samples.Length != orig.Count) Fail($"{P}: {fsb.samples.Length} samples, the disc's bank has {orig.Count}");
            for (int i = 0; i < Math.Min(fsb.samples.Length, orig.Count); i++)
            {
                var a = fsb.samples[i]; var o = orig[i];
                string w = $"{P}/{a.name}";
                if (a.name != o.name) Fail($"{w}: name differs from {o.name}");
                if (a.lengthSamples != o.samples || a.lengthBytes != 2 * o.samples) Fail($"{w}: length {a.lengthSamples} ({a.lengthBytes} B), the disc's {o.samples}");
                if (a.frequency != o.freq || a.channels != 1 || (a.mode & (ReflexFmod.M_16BITS | ReflexFmod.M_MONO)) != (ReflexFmod.M_16BITS | ReflexFmod.M_MONO)) Fail($"{w}: rate {a.frequency} channels {a.channels} mode {a.mode:x}");
                if (a.loopStart != o.loopStart || a.loopEnd != o.loopEnd) Fail($"{w}: loop {a.loopStart}..{a.loopEnd}, the disc's {o.loopStart}..{o.loopEnd}");
                bool loopsWhole = o.loopStart == 0 && o.loopEnd == o.samples - 1;
                if ((a.mode & 0x7) != (loopsWhole ? ReflexFmod.M_LOOP_NORMAL : ReflexFmod.M_LOOP_OFF)) Fail($"{w}: loop bits {a.mode & 7}");
                if (loopsWhole) loops++;
                if (a.syncs.Count != o.syncs.Count || !a.syncs.Select(x => ((long)x.sample, x.name)).SequenceEqual(o.syncs)) Fail($"{w}: {a.syncs.Count} sync points, the disc's {o.syncs.Count}");
                syncs += a.syncs.Count;
                if (ReflexFmod.CodecOf(a, fsb.Head(a)) != "pcm") notPcm++;
                int peak = 0; long sum = 0;
                for (int k = 0; k < a.lengthSamples; k++) { int v = Math.Abs((int)BitConverter.ToInt16(fsb.blob, a.dataOffset + 2 * k)); if (v > peak) peak = v; sum += v; }
                minPeak = Math.Min(minPeak, peak / 32768.0);
                if (peak == 0) Fail($"{w}: silent");
                else if (peak < 300) quiet++;
            }
        }

        // the XML
        try
        {
            var cfg = new XmlDocument(); cfg.Load(Path.Combine(dir, "AudioEngineConfig.xml"));
            if (cfg.DocumentElement.Name != "audio_engine" || cfg.DocumentElement.GetAttribute("sound") != P) Fail($"{P}: AudioEngineConfig.xml root {cfg.DocumentElement.Name} sound={cfg.DocumentElement.GetAttribute("sound")}");
            var act = new XmlDocument(); act.Load(Path.Combine(dir, "EngineActions.xml"));
            if (act.DocumentElement.Name != "engineactionlists") Fail($"{P}: EngineActions.xml root {act.DocumentElement.Name}");
            int states = cfg.DocumentElement.ChildNodes.OfType<XmlElement>().Count(), lists = act.DocumentElement.ChildNodes.OfType<XmlElement>().Count();
            int typed = act.SelectNodes("//@*").Cast<XmlAttribute>().Count(x => x.Value.StartsWith("_", StringComparison.Ordinal)) + cfg.SelectNodes("//@*").Cast<XmlAttribute>().Count(x => x.Value.StartsWith("_", StringComparison.Ordinal));
            if (typed > 0) Fail($"{P}: {typed} attribute values keep a bxml type prefix");
            Console.WriteLine($"{P}: {s.name}, {events} events, {entries} wave entries, {orig.Count} samples ({loops} whole loops), {syncs} sync points, {states} states, {lists} action lists, min peak {minPeak:0.000}, {quiet} quiet (<300), {notPcm} not read as pcm by CodecOf, {fsbBytes.Length / 1e6:0.0} MB");
        }
        catch (XmlException e) { Fail($"{P}: XML: {e.Message}"); }

        // determinism
        if (twin != null)
        {
            string other = Path.Combine(AliveEngineSounds.Root(twin), P);
            var a = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var b = Directory.Exists(other) ? Directory.GetFiles(other).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList() : new List<string>();
            if (!a.SequenceEqual(b)) Fail($"{P}: the twin conversion has other files");
            else foreach (var f in a) if (Sha(Path.Combine(dir, f)) != Sha(Path.Combine(other, f))) Fail($"{P}: {f} differs between the two conversions");
        }
        if (failures == before) Console.WriteLine($"{P}: ok");
    }
}
