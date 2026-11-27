// tests/unleashed_import/scenery: the scenery side of the MX vs ATV Unleashed converter (UnScenery.Convert) against the Python
// reference library, byte for byte.
//   Program <reference library> <Unleashed data folder> <Reflex library> <tools/unleashed> [--set-vectors <file>] <track ids...>
// Per track: the terrain centre / extent from the track's .dx.trn (import_library.py's numbers), the level's own terrain.rxheight and
// race.rxrace read from the reference folder (the terrain side's outputs, inputs here), then every file the scenery side owns compared
// with the reference folder's and the report / start gate compared with level.json's "scenery" / "start_gate".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Reflex.Port.Content.AliveImport;
using Reflex.Port.Content.UnleashedImport;

static class Program
{
    static readonly string[] Owned = { "scenery.rxscenery", "collision.rxcollision", "vegetation.json", "scenery-fx.json", "scenery-manifest.json", "toys.rxtoys", "funnel.rxfunnel",
        "reflex-forest.bin", "reflex-vegetation.json", "startgate.rxgate", "stadium-lights.json" };

    static bool IsOwned(string name) => Array.IndexOf(Owned, name) >= 0 || name.StartsWith("reflex-resource-", StringComparison.Ordinal);

    static int fails;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS  " : "DIFF  ") + what + (detail.Length > 0 ? "  " + detail : ""));
        if (!ok) fails++;
    }

    static void Compare(string what, byte[] got, byte[] want)
    {
        int n = Math.Min(got.Length, want.Length), at = -1;
        for (int i = 0; i < n; i++) if (got[i] != want[i]) { at = i; break; }
        if (at < 0 && got.Length != want.Length) at = n;
        if (at < 0) { Check(what, true, got.Length + " bytes"); return; }
        int diffs = 0; for (int i = 0; i < n; i++) if (got[i] != want[i]) diffs++;
        Check(what, false, "first difference at " + at + ", " + diffs + " bytes differ; sizes " + got.Length + " (C#) / " + want.Length + " (reference)");
    }

    static int SelfTests(string setVectors)
    {
        int bad = 0;
        // the portable fused multiply-add against the runtime's
        var rng = new Random(12345); int n = 2000000;
        for (int i = 0; i < n; i++)
        {
            double a = (rng.NextDouble() - 0.5) * Math.Pow(2, rng.Next(-40, 40)), b = (rng.NextDouble() - 0.5) * Math.Pow(2, rng.Next(-40, 40));
            double c = i % 3 == 0 ? -a * b * (1 + (rng.NextDouble() - 0.5) * 1e-9) : (rng.NextDouble() - 0.5) * Math.Pow(2, rng.Next(-80, 80));
            if (i % 7 == 0) { a = Math.Round(a * 1024) / 1024; b = Math.Round(b * 16); c = Math.Round(c * 4096) / 4096; }
            double want = Math.FusedMultiplyAdd(a, b, c), got = UnNum.Fma(a, b, c);
            if (BitConverter.DoubleToInt64Bits(want) != BitConverter.DoubleToInt64Bits(got)) { if (bad < 5) Console.WriteLine("      fma(" + a.ToString("R") + ", " + b.ToString("R") + ", " + c.ToString("R") + ") = " + got.ToString("R") + ", runtime " + want.ToString("R")); bad++; }
        }
        Check("UnNum.Fma = Math.FusedMultiplyAdd (" + n + " cases)", bad == 0, bad == 0 ? "" : bad + " differ");
        if (setVectors != null && File.Exists(setVectors))
        {
            int cases = 0, wrong = 0;
            foreach (string line in File.ReadLines(setVectors))
            {
                var halves = line.Split('|'); if (halves.Length != 2) continue;
                var ins = halves[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                var want = halves[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                cases++;
                if (!UnStadiumLights.PySetOrder(ins).SequenceEqual(want)) wrong++;
            }
            Check("UnStadiumLights.PySetOrder = Python's set order (" + cases + " cases)", wrong == 0 && cases > 0, wrong == 0 ? "" : wrong + " differ");
        }
        return bad;
    }

    static int Main(string[] args)
    {
        var list = new List<string>(args); string setVectors = null;
        int sv = list.IndexOf("--set-vectors");
        if (sv >= 0) { setVectors = list[sv + 1]; list.RemoveRange(sv, 2); }
        if (list.Count < 5) { Console.Error.WriteLine("usage: <reference library> <Unleashed data folder> <Reflex library> <tools/unleashed> [--set-vectors <file>] <track ids...>"); return 2; }
        string reference = list[0], data = list[1], reflex = list[2], tools = list[3];
        UnData.Loader = name => { string p = Path.Combine(tools, name); return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null; };
        SelfTests(setVectors);
        foreach (string track in list.Skip(4))
        {
            string folder = Path.Combine(reference, "Assets", "unleashed", "UNL_" + track);
            Console.WriteLine("== " + track);
            if (!File.Exists(Path.Combine(folder, "level.json"))) { Check(track + ": reference level", false, "no " + folder + "/level.json"); continue; }
            try
            {
                var input = new UnSceneryInput { sourceDataDir = data, reflexRoot = reflex, trackId = track };
                using (var pak = UnPak.Open(Path.Combine(data, track + ".pak")))
                {
                    var trn = UnTrn.Parse(pak.Read("Data/Env/" + track + "/" + track + ".dx.trn"));
                    trn.GridSize(out int w, out int h);
                    double cell = trn.cellSize, ex = (w - 1) * cell, ez = (h - 1) * cell;
                    input.centerX = ex / 2; input.centerZ = ez / 2; input.maxExtent = Math.Max(ex, ez);
                    var scn = pak.Read("Data/Env/" + track + "/" + track + ".scn");
                    var sb = new StringBuilder(scn.Length); foreach (byte c in scn) sb.Append((char)c);
                    input.sceneText = sb.ToString();
                }
                input.terrainRxheight = File.ReadAllBytes(Path.Combine(folder, "terrain.rxheight"));
                string race = Path.Combine(folder, "race.rxrace");
                input.raceRxrace = File.Exists(race) ? File.ReadAllBytes(race) : null;
                int foreign = UnToys.ForeignCornersSeen;
                var clock = Stopwatch.StartNew();
                var result = UnScenery.Convert(input, null, CancellationToken.None);
                clock.Stop();
                var names = new SortedSet<string>(result.files.Keys, StringComparer.Ordinal);
                foreach (string f in Directory.GetFiles(folder)) if (IsOwned(Path.GetFileName(f))) names.Add(Path.GetFileName(f));
                int resources = 0, resourcesOk = 0; long resourceBytes = 0;
                foreach (string name in names)
                {
                    string p = Path.Combine(folder, name);
                    bool have = result.files.TryGetValue(name, out byte[] got), want = File.Exists(p);
                    if (!have) { Check(track + " " + name, false, "the reference has it, the C# converter did not write it"); continue; }
                    if (!want) { Check(track + " " + name, false, "the C# converter wrote it (" + got.Length + " bytes), the reference has none"); continue; }
                    if (name.StartsWith("reflex-resource-", StringComparison.Ordinal))
                    {
                        byte[] refBytes = File.ReadAllBytes(p); resources++; resourceBytes += got.Length;
                        if (got.AsSpan().SequenceEqual(refBytes)) resourcesOk++; else Compare(track + " " + name, got, refBytes);
                        continue;
                    }
                    Compare(track + " " + name, got, File.ReadAllBytes(p));
                }
                if (resources > 0) Check(track + " reflex-resource-* (" + resources + " files)", resources == resourcesOk, resourceBytes + " bytes" + (resources == resourcesOk ? "" : ", " + (resources - resourcesOk) + " differ"));
                var level = UnJson.Obj(UnJson.Loads(File.ReadAllText(Path.Combine(folder, "level.json"), Encoding.UTF8)));
                string wantReport = UnJson.Dumps(level.Get("scenery"), 2), gotReport = UnJson.Dumps(result.report, 2);
                Check(track + " level.json \"scenery\"", wantReport == gotReport, wantReport == gotReport ? gotReport.Length + " chars" : FirstTextDifference(gotReport, wantReport));
                string wantGate = level.Has("start_gate") ? UnJson.Dumps(level.Get("start_gate"), 2) : "(absent)", gotGate = result.startGate != null ? UnJson.Dumps(result.startGate, 2) : "(absent)";
                Check(track + " level.json \"start_gate\"", wantGate == gotGate, wantGate == gotGate ? (gotGate.Length > 20 ? gotGate.Length + " chars" : gotGate) : FirstTextDifference(gotGate, wantGate));
                Console.WriteLine("      " + track + ": " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s" + (UnToys.ForeignCornersSeen != foreign ? "; " + (UnToys.ForeignCornersSeen - foreign) + " toy corners went through the inverse bind frame (not bit-exact by construction)" : ""));
            }
            catch (Exception ex)
            {
                Check(track + ": conversion", false, ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }
        Console.WriteLine(fails == 0 ? "UNLEASHED SCENERY TESTS PASS" : "UNLEASHED SCENERY TESTS FAIL (" + fails + ")");
        return fails == 0 ? 0 : 1;
    }

    static string FirstTextDifference(string got, string want)
    {
        int n = Math.Min(got.Length, want.Length), at = 0;
        while (at < n && got[at] == want[at]) at++;
        string Cut(string s) => s.Substring(Math.Max(0, at - 60), Math.Min(s.Length - Math.Max(0, at - 60), 160)).Replace("\n", "\\n");
        return "first difference at char " + at + "\n      C#:        " + Cut(got) + "\n      reference: " + Cut(want);
    }
}
