// tests/unleashed_import: the Unleashed converter's terrain side against the Python pipeline, byte for byte (run.sh).
//   UnleashedImport.dll convert <data files dir> <source data dir> <reflex library> <reference library> <track id>...
//       UnConvert.ConvertLevel for each track, every file it returns compared with <reference>/<path>: PASS, or DIFF with the first
//       differing offset and both sizes. The scenery side is not this test's: level.json is built from the reference level.json's
//       "scenery" report and "start_gate" (the two inputs the scenery side supplies), and the reference's other files are counted only.
//   UnleashedImport.dll ini <source data dir> <track id>...
//       each track's .scn through UnIni (allow_no_value, as scenery.py reads it) as one JSON line per track, for run.sh to compare
//       with Python's configparser.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Reflex.Port.Content.AliveImport;
using Reflex.Port.Content.UnleashedImport;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "ini") return Ini(args);
        if (args.Length < 6 || args[0] != "convert") { Console.Error.WriteLine("usage: convert <data files dir> <source data dir> <reflex library> <reference library> <track id>... | ini <source data dir> <track id>..."); return 2; }
        string data = args[1], source = args[2], reflex = args[3], reference = args[4];
        UnData.Loader = name => { string p = Path.Combine(data, name); return File.Exists(p) ? File.ReadAllText(p) : null; };
        int pass = 0, diff = 0, missing = 0, failed = 0;
        var byKind = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        for (int a = 5; a < args.Length; a++)
        {
            string id = args[a], level = "UNL_" + id, folder = "Assets/unleashed/" + level + "/";
            var entry = UnConvert.Find(id);
            if (entry == null) { Console.WriteLine("FAIL  " + id + ": not in tracks.json"); failed++; continue; }
            string refJson = Path.Combine(reference, folder + "level.json");
            if (!File.Exists(refJson)) { Console.WriteLine("FAIL  " + id + ": no reference (" + refJson + ")"); failed++; continue; }
            var refInfo = (JObj)UnJson.Loads(File.ReadAllText(refJson));
            var sw = Stopwatch.StartNew();
            Dictionary<string, byte[]> files;
            try { files = UnConvert.ConvertLevel(source, reflex, entry, null, CancellationToken.None); }
            catch (Exception e) { Console.WriteLine("FAIL  " + id + ": " + e.GetType().Name + ": " + e.Message); Console.WriteLine(e.StackTrace); failed++; continue; }
            Console.WriteLine("== " + id + " (" + entry.name + "): " + files.Count + " files in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
            var names = new List<string>(files.Keys); names.Sort(StringComparer.Ordinal);
            foreach (var path in names)
            {
                string kind = path.StartsWith("Database/") ? path.Substring(path.IndexOf('.') + 1) : Path.GetFileName(path);
                if (!byKind.TryGetValue(kind, out var c)) byKind[kind] = c = new int[3];
                byte[] mine = files[path];
                string rp = Path.Combine(reference, path);
                if (!File.Exists(rp)) { Console.WriteLine("NOREF " + path + " (" + mine.Length + " bytes; the reference has no such file)"); missing++; c[2]++; continue; }
                byte[] theirs = File.ReadAllBytes(rp);
                int n = Math.Min(mine.Length, theirs.Length), at = -1;
                for (int i = 0; i < n; i++) if (mine[i] != theirs[i]) { at = i; break; }
                if (at < 0 && mine.Length == theirs.Length) { Console.WriteLine("PASS  " + path + " (" + mine.Length + " bytes)"); pass++; c[0]++; }
                else
                {
                    int count = 0; for (int i = 0; i < n; i++) if (mine[i] != theirs[i]) count++;
                    Console.WriteLine("DIFF  " + path + ": first difference at offset " + (at < 0 ? n : at) + ", " + count + " differing bytes, sizes " + mine.Length + " (C#) / " + theirs.Length + " (reference)");
                    diff++; c[1]++;
                }
            }
            // what the reference has and this side does not make: the scenery side's files (and anything this port forgot)
            int other = 0; var forgot = new List<string>();
            string rf = Path.Combine(reference, folder);
            if (Directory.Exists(rf))
                foreach (var f in Directory.GetFiles(rf))
                {
                    string name = Path.GetFileName(f);
                    if (files.ContainsKey(folder + name)) continue;
                    forgot.Add(name);   // (the whole level is this test's now: every reference file must be written)
                }
            foreach (var name in forgot) { Console.WriteLine("DIFF  " + folder + name + ": the reference has it, the C# converter did not write it"); diff++; }
            Console.WriteLine("      (" + other + " reference files of the scenery side not compared)");
        }
        Console.WriteLine();
        Console.WriteLine("by file: PASS / DIFF / NOREF");
        foreach (var kv in byKind) Console.WriteLine("  " + kv.Key.PadRight(22) + kv.Value[0] + " / " + kv.Value[1] + " / " + kv.Value[2]);
        Console.WriteLine((diff == 0 && failed == 0 && missing == 0 ? "UNLEASHED TERRAIN PARITY PASS" : "UNLEASHED TERRAIN PARITY FAIL") + ": " + pass + " PASS, " + diff + " DIFF, " + missing + " NOREF, " + failed + " tracks failed");
        return diff == 0 && failed == 0 && missing == 0 ? 0 : 1;
    }

    static int Ini(string[] args)
    {
        for (int a = 2; a < args.Length; a++)
        {
            string id = args[a];
            using (var p = UnPak.Open(Path.Combine(args[1], id + ".pak")))
            {
                byte[] b = p.Read("Data/Env/" + id + "/" + id + ".scn");
                var ini = UnIni.Parse(UnPak.Latin1(b, 0, b.Length), true);
                var o = new JObj();
                foreach (var s in ini.Sections)
                {
                    var so = new JObj();
                    foreach (var kv in s.Items) so.Set(kv.Key, kv.Value);
                    o.Set(s.Name, so);
                }
                Console.WriteLine(UnJson.Dumps(new JObj().Set(id, o)));
            }
        }
        return 0;
    }
}
