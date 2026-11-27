// TEST ONLY (tests/alive_engine/level): writes the engine event libraries the game builds -- Reflex's bike engine projects through
// ReflexAudioSet.BuildProjects and the installed Alive sets through AliveEngineSets.Scan / ReflexAudioSet.BuildAliveSet -- with every wave they
// play, for level.py (the A-weighted level a rider hears, core/plan/ALIVE_ENGINE_SOUNDS.md section 7). Runs on the Unity assemblies that
// unity_compile.py builds (build.ps1).
//   AliveLevel.dll --game <Reflex game files> --content <folder holding AliveEngineSounds> --out <dir> [--rxcore <rxcore.dll>]
//                  [--reflex MX_125R_01,MX_250R_TEST_05,...]
// --rxcore (the core library, for a packed game library: Assets.rxdata) is loaded before anything reads the install.
// <dir>/library.json: {"reflex": {"engines": [{size, project, states: {state: event path}}], "events": {...}},
//                      "alive": [{project, name, trim, start, states: {state: event path}, events: {...}}], "waves": {wave key: file}}
// Waves: <dir>/waves/<n>.wav (PCM16, the "pcm" / "ima_adpcm" samples) or <n>.mp3 (the "mpeg" samples' MPEG stream).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Reflex.Port.Content;
using Reflex.Port.OriginalWorld;

static class Program
{
    static readonly string[] DefaultReflex = { "MX_125R_01", "MX_250R_TEST_05", "MX_250F_01", "MX_450F_01" };

    static int Main(string[] args)
    {
        string game = null, content = null, outDir = null, rxcore = null; var reflex = DefaultReflex;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--game") game = args[++i];
            else if (args[i] == "--content") content = args[++i];
            else if (args[i] == "--out") outDir = args[++i];
            else if (args[i] == "--rxcore") rxcore = args[++i];
            else if (args[i] == "--reflex") reflex = args[++i].Split(',');
        }
        if (game == null || content == null || outDir == null) { Console.WriteLine("usage: --game DIR --content DIR --out DIR [--reflex P,P]"); return 2; }
        Environment.SetEnvironmentVariable("REFLEX_GAME_DIR", game);
        if (rxcore != null) Reflex.Port.RxCore.RxNativeLoader.Load(rxcore);
        Directory.CreateDirectory(Path.Combine(outDir, "waves"));
        var waves = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var sb = new StringBuilder("{\n");

        // Reflex: the engine sizes' projects and the emitter's state table (OriginalEngineAudio.StateTable)
        var lib = ReflexAudioSet.BuildProjects("level", reflex);
        foreach (var u in lib.unresolved) Console.WriteLine("reflex unresolved: " + u);
        var table = OriginalEngineAudio.StateTable.Load();
        sb.Append("\"reflex\": {\"engines\": [");
        bool first = true;
        foreach (var (size, project) in ReflexAudioSet.EngineSizes())
        {
            if (!reflex.Contains(project, StringComparer.OrdinalIgnoreCase)) continue;
            int si = Array.IndexOf(OriginalEngineAudio.SizeNames, size.Substring("MXVehicleEngineSize_".Length));
            sb.Append(first ? "\n" : ",\n"); first = false;
            sb.Append("  {\"size\": ").Append(S(size)).Append(", \"project\": ").Append(S(project)).Append(", \"states\": {");
            bool f2 = true;
            for (int k = 0; si >= 0 && k < OriginalEngineAudio.States; k++)
            {
                string p = table.path[si, k];
                if (string.IsNullOrEmpty(p)) continue;
                sb.Append(f2 ? "" : ", ").Append(S(OriginalEngineAudio.StateNames[k])).Append(": ").Append(S(p.ToLowerInvariant())); f2 = false;
            }
            sb.Append("}}");
        }
        sb.Append("],\n \"events\": ");
        Events(sb, lib, outDir, waves);
        sb.Append("},\n\"alive\": [");

        // Alive: every installed set, its config's state -> event
        var problems = new List<string>();
        var sets = AliveEngineSets.Scan(Path.Combine(content, AliveEngineSets.FolderName), problems);
        foreach (var p in problems) Console.WriteLine("alive problem: " + p);
        first = true;
        foreach (var s in sets)
        {
            var a = ReflexAudioSet.BuildAliveSet(s);
            // the state -> event table as the emitter reads it
            var prog = AliveEngineMachine.Program.Parse(File.ReadAllText(s.ConfigPath), File.ReadAllText(s.ActionsPath), s.project);
            sb.Append(first ? "\n" : ",\n"); first = false;
            sb.Append("{\"project\": ").Append(S(s.project)).Append(", \"name\": ").Append(S(s.name)).Append(", \"trim\": ").Append(F(s.trim))
              .Append(", \"start\": ").Append(S(prog.StateName(prog.StartState))).Append(", \"states\": {");
            bool f2 = true;
            for (int k = 0; k < prog.StateCount; k++)
            {
                string p = prog.PathOf(k);
                if (p.Length == 0) continue;
                sb.Append(f2 ? "" : ", ").Append(S(prog.StateName(k))).Append(": ").Append(S(p)); f2 = false;
            }
            sb.Append("},\n \"events\": ");
            Events(sb, a, outDir, waves);
            sb.Append("}");
            Console.WriteLine($"alive {s.project}: {a.library.events.Length} events, {a.waves.Count} waves, trim {s.trim}");
        }
        sb.Append("],\n\"waves\": {");
        sb.Append(string.Join(",", waves.Select(kv => "\n  " + S(kv.Key) + ": " + S(kv.Value))));
        sb.Append("}\n}\n");
        File.WriteAllText(Path.Combine(outDir, "library.json"), sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"reflex {string.Join(",", lib.projects)}: {lib.library.events.Length} events; {waves.Count} waves written to {outDir}");
        return 0;
    }

    static void Events(StringBuilder sb, ReflexAudioSet set, string outDir, SortedDictionary<string, string> waves)
    {
        sb.Append("{");
        bool first = true;
        foreach (var e in set.library.events)
        {
            sb.Append(first ? "\n" : ",\n"); first = false;
            sb.Append("  ").Append(S(e.path)).Append(": {\"volume\": ").Append(F(e.volume)).Append(", \"volumeRand\": ").Append(F(e.volumeRand))
              .Append(", \"pitchOctaves\": ").Append(F(e.pitchOctaves)).Append(", \"channel\": ").Append(e.channel)
              .Append(", \"parameters\": [").Append(string.Join(", ", e.parameters.Select(p => "{\"name\": " + S(p.name) + ", \"min\": " + F(p.min) + ", \"max\": " + F(p.max) + "}"))).Append("]")
              .Append(", \"layers\": [").Append(string.Join(", ", e.layers.Select(l =>
                  "{\"param\": " + l.param + ", \"instances\": [" + string.Join(", ", l.instances.Select(x => "{\"def\": " + x.def + ", \"start\": " + F(x.start) + ", \"length\": " + F(x.length) +
                      ", \"gain\": " + F(x.gain) + ", \"fadeIn\": " + F(x.fadeIn) + ", \"fadeOut\": " + F(x.fadeOut) + ", \"loop\": " + (x.loop ? "true" : "false") + "}")) +
                  "], \"envelopes\": [" + string.Join(", ", l.envelopes.Select(v => "{\"kind\": " + S(v.kind) + ", \"param\": " + v.param + ", \"log\": " + (v.log ? "true" : "false") +
                      ", \"dspMin\": " + F(v.dspMin) + ", \"dspMax\": " + F(v.dspMax) + ", \"x\": [" + string.Join(", ", v.x.Select(F)) + "], \"y\": [" + string.Join(", ", v.y.Select(F)) +
                      "], \"shape\": [" + string.Join(", ", v.shape) + "]}")) + "]}"))).Append("]")
              .Append(", \"defs\": [").Append(string.Join(", ", e.defs.Select(d => "{\"name\": " + S(d.name) + ", \"mode\": " + S(d.mode) + ", \"volume\": " + F(d.volume) +
                  ", \"volumeRandMin\": " + F(d.volumeRandMin) + ", \"pitchOctaves\": " + F(d.pitchOctaves) + ", \"files\": [" + string.Join(", ", d.files.Select(S)) +
                  "], \"weights\": [" + string.Join(", ", (d.weights ?? new float[0]).Select(F)) + "]}"))).Append("]}");
            foreach (var d in e.defs) foreach (var f in d.files) if (f.Length > 0 && !waves.ContainsKey(f) && set.waves.TryGetValue(f, out var w)) waves[f] = Write(w, outDir, waves.Count);
        }
        sb.Append("}");
    }

    static string Write(ReflexWave w, string outDir, int n)
    {
        string name;
        if (w.codec == "mpeg") { name = "waves/" + n + ".mp3"; File.WriteAllBytes(Path.Combine(outDir, name), w.Mp3()); }
        else
        {
            name = "waves/" + n + ".wav";
            var pcm = w.Pcm();
            using var o = new BinaryWriter(File.Create(Path.Combine(outDir, name)));
            int bytes = pcm.Length * 2;
            o.Write(Encoding.ASCII.GetBytes("RIFF")); o.Write(36 + bytes); o.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); o.Write(16);
            o.Write((short)1); o.Write((short)w.channels); o.Write(w.rate); o.Write(w.rate * 2 * w.channels); o.Write((short)(2 * w.channels)); o.Write((short)16);
            o.Write(Encoding.ASCII.GetBytes("data")); o.Write(bytes);
            foreach (var v in pcm) o.Write(v);
        }
        w.DropBank();
        return name;
    }

    static string F(float v) => float.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : "0";
    static string S(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s ?? "") { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); }
        return b.Append('"').ToString();
    }
}
