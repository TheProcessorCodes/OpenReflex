// tests/alive_engine/emitter: MX vs ATV Alive's engine state machine (OriginalWorld/AliveEngineAudio.cs) outside Unity, on the real data.
//
// Input: a folder with the engine audio of Alive's EngineAudio material packages as extracted from the disc / LIVE packages (any depth:
// every folder holding an AudioEngineConfig_*.bxml, its EngineActions*.bxml, <PROJECT>.sound and <bank>.soundbnk files). Per project
// (the first folder that has it): the two bxml become the XML the importer installs (AliveBxml's element tree, as
// <set>/AudioEngineConfig.xml, EngineActions.xml), the FEV and FSB4 are read with ReflexFmod (events, sound definitions, recordings,
// sync points) into the fake OriginalAudio of Stubs.cs, and AliveEngineAudio runs on synthetic telemetry at 60 Hz:
//   idle -> full throttle (the recordings' markers fire at their sample positions) -> off throttle -> airborne -> landing -> clutch ->
//   whoops -> wreck -> the start line (cues, the core's StartLine / Rev serials) -> a computer rider (AIFX, the field trim).
// Checks: every action / test / member / comparison of every set is understood, every marker of every recording names a list or a state
// of the set, every state event exists except the five Alive names without one (Choke, Flooding, EnterAir, BarBangBog1, BarBangBog2),
// and the expected transitions per phase. Prints the state sequence (ALIVE_EMITTER_VERBOSE=1: every play, marker and end too). Exit code 0 = all passed.
//
// Or, from an importer's output (the content folder holding AliveEngineSounds/<PROJECT>/, as tests/alive_engine convert writes it): the
// installed sets as the game reads them (set.json, <PROJECT>.fev, <PROJECT>.fsb and the importer's XML), nothing converted here.
//
// Usage: dotnet exec AliveEngineEmitter.dll <extracted engine audio folder> <work folder>
//        dotnet exec AliveEngineEmitter.dll --installed <content folder>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Reflex.Port.Content;
using Reflex.Port.Content.AliveImport;
using Reflex.Port.OriginalWorld;
using Reflex.Port.RxCore;

static class Program
{
    static int failures;
    static readonly bool Verbose = Environment.GetEnvironmentVariable("ALIVE_EMITTER_VERBOSE") == "1";
    static void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("  FAIL " + what); } else Console.WriteLine("  ok   " + what); }

    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length < 2) { Console.WriteLine("usage: AliveEngineEmitter <extracted engine audio folder> <work folder> | --installed <content folder>"); return 2; }
        if (args[0] == "--installed")
        {
            int n = 0;
            foreach (var dir in Directory.GetDirectories(Path.Combine(args[1], "AliveEngineSounds")).OrderBy(d => d, StringComparer.Ordinal))
            {
                if (Path.GetFileName(dir).StartsWith(".", StringComparison.Ordinal)) continue;
                n++; RunSet(PrepareInstalled(dir));
            }
            Check(n == 7, "7 installed Alive engine sets (" + n + ")");
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }
        string src = args[0], work = args[1];
        Directory.CreateDirectory(work);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int sets = 0;
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories).Prepend(src).OrderBy(d => d, StringComparer.Ordinal))
        {
            var cfg = Directory.GetFiles(dir, "AudioEngineConfig_*.bxml").FirstOrDefault();
            if (cfg == null) continue;
            var set = Prepare(dir, cfg, work, done);
            if (set == null) continue;
            sets++;
            RunSet(set);
        }
        Check(sets == 7, "7 distinct Alive engine sets found (" + sets + ")");
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    sealed class Set { public AliveEngineSet set; public string actions; public Dictionary<string, OriginalAudio.Event> events; public bool atv; }

    // ---------------------------------------------------------------------------------------------- the data
    static Set Prepare(string dir, string cfgPath, string work, HashSet<string> done)
    {
        var cfg = Element(cfgPath);
        string project = cfg.Get("sound");
        if (string.IsNullOrEmpty(project) || !done.Add(project)) return null;
        var actionsPath = Directory.GetFiles(dir, "EngineActions*.bxml").Single();
        var sound = Directory.GetFiles(dir, "*.sound").Single(f => Path.GetFileNameWithoutExtension(f).Equals(project, StringComparison.OrdinalIgnoreCase));
        string folder = Path.Combine(work, project); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "AudioEngineConfig.xml"), Xml(cfg));
        File.WriteAllText(Path.Combine(folder, "EngineActions.xml"), Xml(Element(actionsPath)));
        var fev = ReflexFmod.ParseFev(File.ReadAllBytes(sound)) ?? throw new Exception(sound + ": not a FEV");
        // the banks by name (the .soundbnk's stem = the FEV's wave bank name); a wave entry names its bank and its sample index
        var banks = new Dictionary<string, ReflexFmod.Sample[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var bank in Directory.GetFiles(dir, "*.soundbnk")) banks[Path.GetFileNameWithoutExtension(bank)] = ReflexFmod.ParseFsb(File.ReadAllBytes(bank)).samples;
        var samples = banks.Values.SelectMany(b => b).ToList();
        var events = new Dictionary<string, OriginalAudio.Event>(StringComparer.OrdinalIgnoreCase);
        int unmapped = 0;
        foreach (var (gpath, ev) in fev.Events())
        {
            var e = new OriginalAudio.Event { path = ("alive_" + project + "/" + gpath + "/" + ev.name).ToLowerInvariant() };
            foreach (var layer in ev.layers)
                foreach (var inst in layer.instances)
                    foreach (var w in fev.soundDefs[inst.soundDef].entries)
                    {
                        ReflexFmod.Sample s = null;
                        if (w.bank != null && banks.TryGetValue(w.bank, out var b) && w.index < b.Length) s = b[w.index];
                        else if (banks.Count == 1 && w.index < banks.Values.First().Length) s = banks.Values.First()[w.index];
                        if (s == null || w.wave == null || !ReflexFmod.Stem(w.wave).StartsWith(ReflexFmod.Stem(s.name), StringComparison.OrdinalIgnoreCase)) { unmapped++; continue; }
                        e.recordings.Add(new OriginalAudio.Recording { name = s.name, length = s.lengthSamples, loop = inst.Loop, syncs = s.syncs });
                    }
            events[e.path] = e;
        }
        Check(unmapped == 0, project + ": every wave entry found in its bank (" + unmapped + " not)");
        var cls = Path.GetFileName(cfgPath);
        Console.WriteLine($"== {project} ({Path.GetFileName(dir)}, {Path.GetFileName(actionsPath)}): {events.Count} events, {samples.Count} recordings, {samples.Sum(s => s.syncs.Count)} sync points");
        return new Set
        {
            set = new AliveEngineSet { id = "alive:" + project, project = project, name = project, folder = folder },
            actions = Path.GetFileNameWithoutExtension(actionsPath), events = events, atv = cls.IndexOf("ATV", StringComparison.OrdinalIgnoreCase) >= 0
        };
    }

    // an installed set: set.json, the FEV and the one PCM16 FSB of the folder, the importer's XML as is
    static Set PrepareInstalled(string folder)
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "set.json")));
        string project = json.RootElement.GetProperty("project").GetString(), name = json.RootElement.GetProperty("name").GetString();
        string actions = json.RootElement.GetProperty("actions").GetString();
        // "trim" as AliveEngineSets.Read takes it: a positive number, else 1 (sets installed before the calibration have none)
        float trim = json.RootElement.TryGetProperty("trim", out var tj) && tj.ValueKind == System.Text.Json.JsonValueKind.Number && tj.GetDouble() > 0 ? (float)tj.GetDouble() : 1f;
        var fev = ReflexFmod.ParseFev(File.ReadAllBytes(Path.Combine(folder, project + ".fev"))) ?? throw new Exception(project + ".fev: not a FEV");
        var bank = ReflexFmod.ParseFsb(File.ReadAllBytes(Path.Combine(folder, project + ".fsb"))).samples;
        var events = new Dictionary<string, OriginalAudio.Event>(StringComparer.OrdinalIgnoreCase);
        int unmapped = 0;
        foreach (var (gpath, ev) in fev.Events())
        {
            var e = new OriginalAudio.Event { path = ("alive_" + project + "/" + gpath + "/" + ev.name).ToLowerInvariant() };
            foreach (var layer in ev.layers)
                foreach (var inst in layer.instances)
                    foreach (var w in fev.soundDefs[inst.soundDef].entries)
                    {
                        var s = w.index < bank.Length ? bank[w.index] : null;
                        if (s == null || w.wave == null || !ReflexFmod.Stem(w.wave).StartsWith(ReflexFmod.Stem(s.name), StringComparison.OrdinalIgnoreCase)) { unmapped++; continue; }
                        e.recordings.Add(new OriginalAudio.Recording { name = s.name, length = s.lengthSamples, loop = inst.Loop, syncs = s.syncs });
                    }
            events[e.path] = e;
        }
        Check(unmapped == 0, project + ": every wave entry found in the set's FSB (" + unmapped + " not)");
        Console.WriteLine($"== {project} (installed \"{name}\", {actions}): {events.Count} events, {bank.Length} recordings, {bank.Sum(s => s.syncs.Count)} sync points");
        return new Set
        {
            set = new AliveEngineSet { id = "alive:" + project, project = project, name = name, folder = folder, trim = trim },
            actions = actions, events = events, atv = project.StartsWith("ATV", StringComparison.OrdinalIgnoreCase)
        };
    }

    static Elem Element(string path)
    {
        var raw = File.ReadAllBytes(path);
        try { return Bxml.Parse(raw, scene: true).ToElement(); } catch (AliveImportException) { return Bxml.Parse(raw).ToElement(); }
    }

    static string Xml(Elem root)
    {
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
        {
            void Write(Elem e)
            {
                w.WriteStartElement(e.tag);
                foreach (var a in e.attrib) w.WriteAttributeString(a.Key, a.Value.Text);
                if (!string.IsNullOrEmpty(e.text)) w.WriteString(e.text);
                foreach (var c in e.children) Write(c);
                w.WriteEndElement();
            }
            Write(root);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------- the simulation
    sealed class Sim
    {
        public readonly OriginalAudio audio = new OriginalAudio();
        public readonly AliveEngineAudio aea;
        public readonly RxVehicleAudioState state = new RxVehicleAudioState();
        public RxVehicleAudioRider R => state.riders[0];
        public readonly AliveEngineSet set;
        public float time; public string last = "";
        public readonly List<(float t, string state)> trace = new List<(float, string)>();
        public readonly List<int> reported = new List<int>();
        readonly Func<RxVehicleAudioRider, AliveEngineSet> setOf;
        public float trim = 1f;
        public Sim(Set s)
        {
            set = s.set;
            foreach (var kv in s.events) audio.events[kv.Key] = kv.Value;
            aea = new AliveEngineAudio(audio);
            if (Verbose)
            {
                audio.HostSync += (w, sr, n) => Console.WriteLine($"    {time:0.00} marker {w:x}/{sr} {n}");
                audio.HostFinished += (w, sr) => Console.WriteLine($"    {time:0.00} finished {w:x}/{sr}");
            }
            aea.ReportFinished = slot => reported.Add(slot);
            setOf = r => set;
            state.valid = true; state.riderCount = 1;
            var r = R;
            r.slot = 0; r.vehicle = 1; r.flags = RxVehicleAudioRider.Valid | RxVehicleAudioRider.HasPlayer | RxVehicleAudioRider.LocalHuman | RxVehicleAudioRider.AudioSlot;
            r.vehicleClass = s.atv ? RxVehicleAudioRider.ClassAtv : RxVehicleAudioRider.ClassMx; r.vehicleKind = s.atv ? 1 : 0; r.audioSlot = 0; r.engineSize = 4;
        }
        int printed;
        public void Frame(float dt = 1f / 60f)
        {
            aea.Frame(state, dt, setOf, trim);
            if (Verbose) for (; printed < audio.plays.Count; printed++) Console.WriteLine($"    {time:0.00} play {audio.plays[printed]}");
            audio.Tick(dt);
            time += dt;
            string st = aea.StateOf(0);
            if (st != last) { trace.Add((time, st)); last = st; }
        }
        public void Run(float seconds, Action<RxVehicleAudioRider, float> drive = null)
        {
            float t0 = time;
            while (time - t0 < seconds - 1e-4f) { drive?.Invoke(R, time - t0); Frame(); }
        }
        public void Air(bool up) { var r = R; r.airborne = up; for (int i = 0; i < 4; i++) r.wheelAir[i] = up; }
        public void Cue(int kind) { var c = state.cues[(int)(++state.cueSerial % RxVehicleAudioState.Cues)]; c.serial = state.cueSerial; c.kind = kind; }
        public int Mark() => trace.Count;
        public List<string> Since(int mark) => trace.Skip(mark).Select(x => x.state).ToList();
        public string Path(int mark) => string.Join(" > ", trace.Skip(mark).Select(x => x.state + "@" + x.t.ToString("0.00")));
    }

    static IEnumerable<AliveEngineMachine.TestDef> Tests(AliveEngineMachine.ActionDef[] actions)
    {
        if (actions == null) yield break;
        foreach (var a in actions)
        {
            if (a.tests != null) foreach (var t in a.tests) yield return t;
            foreach (var t in Tests(a.passed)) yield return t;
            foreach (var t in Tests(a.failed)) yield return t;
        }
    }

    static void RunSet(Set s)
    {
        // the program: everything understood, every marker known, every state event present but the five Alive names without one
        var config = File.ReadAllText(Path.Combine(s.set.folder, "AudioEngineConfig.xml"));
        var actions = File.ReadAllText(Path.Combine(s.set.folder, "EngineActions.xml"));
        var prog = AliveEngineMachine.Program.Parse(config, actions, s.set.project);
        Check(prog.unknown.Count == 0, "every action type / test type / member / comparison understood" + (prog.unknown.Count > 0 ? ": " + string.Join(", ", prog.unknown.Distinct()) : ""));
        var badMarkers = s.events.Values.SelectMany(e => e.recordings).SelectMany(r => r.syncs).Select(x => x.name).Distinct().Where(n => !prog.Marker(n, out _, out _)).ToList();
        Check(badMarkers.Count == 0, $"every sync point names a list or a state ({s.events.Values.SelectMany(e => e.recordings).SelectMany(r => r.syncs).Select(x => x.name).Distinct().Count()} names)" + (badMarkers.Count > 0 ? ": " + string.Join(", ", badMarkers) : ""));
        var allowedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Choke", "Flooding", "EnterAir", "BarBangBog1", "BarBangBog2" };
        var missing = Enumerable.Range(0, prog.StateCount).Where(i => prog.PathOf(i).Length > 0 && !s.events.ContainsKey(prog.PathOf(i))).Select(prog.StateName).ToList();
        Check(missing.All(allowedMissing.Contains), "state events exist (missing: " + string.Join(", ", missing) + ")");
        int idle = prog.StateIndex("Idle");
        bool whoopsLanding = prog.lists.Any(l => Tests(l.actions).Any(d => d.type == AliveEngineMachine.TestData && d.member == AliveEngineMachine.Member.NumWhoops && d.value <= 2f));
        Check(prog.StartState == idle && idle >= 0, "start state Idle");

        var sim = new Sim(s);
        var A = s.set;
        // idle: 11 s, past two ends of every idle loop (their last markers halt OffThrottleCheck, the wrap's first ones start it again)
        sim.Run(11f);
        Check(sim.audio.AddSetCalls == 1, "the set added to OriginalAudio once");
        Check(sim.aea.StateOf(0) == "Idle", "idle: Idle (" + sim.Path(0) + ")");
        Check(sim.audio.range.Count >= 1 && sim.audio.range.Values.All(v => v.min == 1f && v.max == 300f), "Reflex's engine range 1 / 300");
        Check(sim.audio.RangePosts == sim.audio.Starts && sim.audio.VolumePosts == sim.audio.Starts, $"range and volume posted with every event ({sim.audio.Starts} events, {sim.audio.RangePosts} / {sim.audio.VolumePosts})");
        Check(sim.audio.aifx.Values.All(v => v == 0f), "AIFX 0 for the local human");
        // full throttle from rest, speed up to 70 ft/s
        int m = sim.Mark();
        sim.Run(6f, (r, t) => { r.throttle = 1f; r.speed = Math.Min(70f, 5f + 14f * t); r.load = 0.8f; });
        var seq = sim.Since(m);
        Console.WriteLine("  throttle: " + sim.Path(m));
        Check(seq.Count > 0 && (seq[0] == "SustainPower" || seq[0] == "AccelPwr") && sim.trace[m].t - (sim.time - 6f) < 0.5f, "throttle on after looped idle: Idle -> SustainPower / AccelPwr within 0.5 s");
        Check(seq.Contains("SustainHigh"), "a recording's Goto marker hands over to SustainHigh");
        Check(!seq.Contains("Idle") && !seq.Contains("Bogging"), "no Idle / Bogging while accelerating");
        // off throttle at speed: engine brake / decel, then idle once slow
        m = sim.Mark();
        sim.Run(9f, (r, t) => { r.throttle = 0f; r.speed = Math.Max(0f, 70f - 20f * t); r.load = 0.1f; });   // the engine-brake recordings run up to 8.6 s (125R)
        seq = sim.Since(m);
        Console.WriteLine("  off throttle: " + sim.Path(m));
        Check(seq.Count > 0 && (seq[0].StartsWith("EngineBrake") || seq[0].StartsWith("Decel")), "throttle off: EngineBrake / Decel");
        Check(sim.aea.StateOf(0) == "Idle", "back to Idle after the decel recording");
        // throttle, then airborne with the throttle held, then landing
        sim.Run(1f, (r, t) => { r.throttle = 1f; r.speed = 30f + 10f * t; });
        m = sim.Mark();
        sim.Air(true);
        sim.Run(4f, (r, t) => { r.throttle = 1f; r.speed = (float)Math.Sqrt(45f * 45f + (20f - 32f * t) * (20f - 32f * t)); r.vy = 20f - 32f * t; });
        seq = sim.Since(m);
        Console.WriteLine("  airborne: " + sim.Path(m));
        Check(seq.Count > 0 && seq[0] == "PanicStartInAir2nd", "airborne with the throttle held: PanicStartInAir2nd (the limiter)");
        Check(seq.Skip(1).All(x => x == "PanicLoopInAir2nd") && sim.aea.StateOf(0) == "PanicLoopInAir2nd", "a 4 s flight with the throttle held stays on the limiter loop");
        Check(!seq.Contains("PanicStartInAir") && !seq.Contains("PanicLoopInAir") && !seq.Contains("Idle") && !seq.Contains("DecelPanic"), "no blips, decel or idle while the throttle is held in the air");
        m = sim.Mark();
        sim.Air(false);
        sim.Run(1.5f, (r, t) => { r.throttle = 1f; r.speed = 45f; r.vy = 0f; });
        seq = sim.Since(m);
        Console.WriteLine("  landing: " + sim.Path(m));
        Check(seq.Count > 0 && (seq[0] == "SustainPower" || seq[0] == "AccelPwr" || seq[0] == "Landing"), "landing with throttle: SustainPower / AccelPwr / Landing");
        // clutch in with the throttle held, then released
        m = sim.Mark();
        sim.Run(1.5f, (r, t) => { r.throttle = 1f; r.clutch = true; r.speed = 30f; });
        seq = sim.Since(m);
        Console.WriteLine("  clutch: " + sim.Path(m));
        Check(seq.Count > 0 && seq[0] == "PanicStartClutch", "clutch: PanicStartClutch");
        m = sim.Mark();
        sim.Run(1.5f, (r, t) => { r.throttle = 1f; r.clutch = false; r.speed = 30f; });
        seq = sim.Since(m);
        Console.WriteLine("  clutch out: " + sim.Path(m));
        Check(seq.Count > 0 && (seq[0] == "SustainPower" || seq[0] == "AccelPwr"), "clutch out: SustainPower / AccelPwr");
        // whoops: short hops in a row at speed with the throttle on
        m = sim.Mark();
        for (int i = 0; i < 4; i++)
        {
            sim.Air(true); sim.Run(0.2f, (r, t) => { r.throttle = 1f; r.speed = 50f; });
            sim.Air(false); sim.Run(0.4f, (r, t) => { r.throttle = 1f; r.speed = 50f; });
        }
        seq = sim.Since(m);
        Console.WriteLine("  whoops: " + sim.Path(m));
        if (whoopsLanding) Check(seq.Contains("Landing"), "whoops (2+ short hops): Landing");
        else Check(!seq.Contains("Landing"), "whoops: no Landing (this set's numwhoops tests ask for 999)");
        Check(seq.Contains("PanicStartInAir2nd") && !seq.Contains("PanicStartInAir") && !seq.Contains("Idle") && !seq.Contains("DecelPanic"), "whoops with the throttle held: the limiter in each hop, no idle / decel");
        // the bike's rear wheel alone up (a whoop kicking the rear): Alive's reartiresairborne; the front alone up (a wheelie): still on the ground
        m = sim.Mark();
        sim.Run(1f, (r, t) => { r.throttle = 1f; r.speed = 50f; r.wheelAir[0] = false; r.wheelAir[1] = !s.atv; });
        seq = sim.Since(m);
        if (!s.atv) Check(!seq.Any(x => x.StartsWith("Panic")), "front wheel up (wheelie): no in-air state (" + sim.Path(m) + ")");
        sim.Air(false);
        // throttle released in the air: the limiter goes to DecelPanic (Alive's OffThrottlePanic)
        sim.Run(0.5f, (r, t) => { r.throttle = 1f; r.speed = 45f; });
        m = sim.Mark();
        sim.Air(true);
        sim.Run(0.6f, (r, t) => { r.throttle = 1f; r.speed = 45f; });
        sim.Run(0.6f, (r, t) => { r.throttle = 0f; r.speed = 45f; });
        seq = sim.Since(m);
        Console.WriteLine("  air, gas off: " + sim.Path(m));
        Check(seq.Count > 1 && seq.Last().StartsWith("Decel"), "throttle off in the air: Decel* (DecelPanic from the loop, DecelHigh / Low from a start's OffThrottle lists)");
        sim.Air(false); sim.Run(3f, (r, t) => { r.throttle = 0f; r.speed = 10f; });
        // wreck, then up again
        m = sim.Mark();
        sim.R.wrecked = true; sim.Run(2f, (r, t) => { r.throttle = 0f; r.speed = 0f; });
        Check(sim.aea.StateOf(0) == "Wreck" && sim.Since(m)[0] == "Wreck", "wrecked: Wreck");
        sim.R.wrecked = false; sim.Run(0.5f);
        Check(sim.aea.StateOf(0) == "Idle", "wreck over: Idle");
        // drowned under water: Choke (no event: silent), occlusion 0.6
        sim.R.underwater = true; sim.R.drowned = true; sim.Run(0.5f);
        Check(sim.aea.StateOf(0) == "Choke" && sim.aea.EventOf(0) == "", "drowned: Choke, nothing plays");
        sim.R.drowned = false; sim.Run(0.3f);
        Check(sim.aea.StateOf(0) == "Idle", "engine back: Idle");
        Check(sim.audio.occlusion.Values.Any(v => Math.Abs(v - 0.6f) < 1e-6f), "underwater occlusion 0.6");
        sim.R.underwater = false; sim.Run(0.2f);
        // the start line: the race intro's cue, the core's StartLine / Rev serials, each rev one REV event, back to Idle at the end
        m = sim.Mark();
        sim.Cue(RxVehicleAudioState.CueStartLineEnable); sim.Run(0.5f);
        Check(sim.aea.StateOf(0) == "StartLine" && sim.aea.EventOf(0) == prog.PathOf(idle), "start line: StartLine plays the idle");
        var r0 = sim.R; r0.flags |= RxVehicleAudioRider.StartLine; r0.startLineState = 2; r0.startLineSerial = 1;
        int playsBefore = sim.audio.plays.Count;
        sim.Run(0.2f, (r, t) => r.throttle = 0.5f);
        Check(sim.aea.StateOf(0) == "Rev" && sim.audio.plays.Count == playsBefore + 1 && sim.audio.plays.Last().Contains("/states/rev "), "core Rev serial: one REV event");
        sim.Run(8f, (r, t) => r.throttle = 0.5f);   // the 50cc revs are 7 s
        Check(sim.reported.Count == 1 && sim.reported[0] == 0, "the rev's end reported to the core once (" + sim.reported.Count + ")");
        r0.startLineState = 4; r0.startLineSerial = 2; sim.Run(0.5f);
        Check(sim.aea.StateOf(0) == "StartLine", "core StartLine serial: StartLine");
        r0.flags &= ~RxVehicleAudioRider.StartLine; sim.Cue(RxVehicleAudioState.CueStartLineDisable); sim.Run(0.5f, (r, t) => r.throttle = 0f);
        Check(sim.aea.StateOf(0) == "Idle", "start line over: Idle (" + sim.Path(m) + ")");
        // the set's calibration trim as the local rider's event volume; a computer rider: AIFX = its audio slot, trim x field trim
        float own = sim.set.trim;
        Check(sim.audio.volume.Count > 0 && sim.audio.volume.Values.All(v => v == own), $"local rider: event volume = the set's trim {own}");
        sim.set.trim = 0.8f; sim.Run(0.3f, (r, t) => { r.throttle = 1f; r.speed = 20f; });
        Check(sim.audio.volume.Values.Any(v => Math.Abs(v - 0.8f) < 1e-6f), "local rider: the set's trim 0.8 posted");
        var r1 = sim.R; r1.flags &= ~RxVehicleAudioRider.LocalHuman; r1.audioSlot = 5; sim.trim = 0.5f;
        sim.Run(0.5f, (r, t) => { r.throttle = 0f; r.speed = 20f; });      // a new state (AIFX is posted when an event starts)
        Check(sim.audio.aifx.Values.Contains(5f), "computer rider: AIFX 5");
        Check(sim.audio.volume.Values.Any(v => Math.Abs(v - 0.4f) < 1e-6f), "computer rider: trim 0.8 x field trim 0.5 posted");
        // the rider leaves (another emitter plays it): the emitter is removed
        sim.aea.Frame(sim.state, 1f / 60f, r => null, 1f);
        Check(sim.aea.EmitterCount == 0, "rider without a set: emitter removed");
        // back again (a culled computer rider heard again): a new emitter on the removed one's wrapper pair
        sim.Run(0.5f);
        Check(sim.aea.EmitterCount == 1 && sim.audio.range.Count <= 2, "rider back: the emitter's wrapper pair reused (" + sim.audio.range.Count + " wrappers)");
        sim.aea.Dispose();
        // a set whose library does not build: no emitter (the original engine plays), not a loop of failed starts
        var bad = new AliveEngineSet { id = A.id + "-unreadable", project = A.project, name = A.name, folder = A.folder, unreadable = true };
        var sim2 = new Sim(s); int starts = sim2.audio.Starts;
        sim2.aea.Frame(sim2.state, 1f / 60f, r => bad, 1f); sim2.aea.Frame(sim2.state, 1f / 60f, r => bad, 1f);
        Check(sim2.aea.EmitterCount == 0 && sim2.aea.StateOf(0) == "" && sim2.audio.Starts == starts && Debug.warnings.Count(w => w.Contains(bad.id + ":")) == 1,
              "a set whose sounds do not load: no emitter, one warning (" + (sim2.audio.Starts - starts) + " starts)");
        sim2.aea.Dispose();
        Console.WriteLine("  states: " + string.Join(" ", sim.trace.Select(x => x.state)));
    }
}
