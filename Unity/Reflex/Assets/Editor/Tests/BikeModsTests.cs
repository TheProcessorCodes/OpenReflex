using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Reflex.Port.Content;
using UnityEditor;
using UnityEngine;

namespace Reflex.Port.Editor
{
    // The bike mods' folder rules (Content/ReflexMods.cs, Content/BikeMods.cs): the client root of a macOS / Windows / Linux player, Windows
    // and mixed separators, junk names, mod.ini, the recursive scan of a scratch Mods/Bikes tree (nested user folders, spaces, Unicode,
    // upper-case files, AppleDouble / __MACOSX / .DS_Store / Thumbs.db junk, a database without its package, two mods of one name), the
    // namespaces, shared .rxmod bikes (two packs' bikes of one model as one card with both packs' kits). With REFLEX_BIKEMOD_ROOT (a Mods/Bikes folder holding the user's "New Bike Models"): the real mod's packages are read.
    // Run: Unity -batchmode -projectPath <copy> -executeMethod Reflex.Port.Editor.BikeModsTests.Verify -quit
    public static class BikeModsTests
    {
        static readonly StringBuilder report = new StringBuilder();
        static int bad;
        static void Log(string s) { report.AppendLine(s); Debug.Log("[BIKEMODS] " + s); }
        static void Check(bool ok, string what) { if (!ok) bad++; Log((ok ? "ok   " : "FAIL ") + what); }
        static void Eq(string got, string want, string what) => Check(got == want, what + ": \"" + got + "\"" + (got == want ? "" : " (want \"" + want + "\")"));

        static void Touch(string path, int bytes = 16) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, new byte[bytes]); }

        public static void Verify()
        {
            report.Clear(); bad = 0;
            string tmp = null;
            try
            {
                // 1. the client root
                Eq(ReflexMods.ClientRootFor("/Users/ann/Games/Open Reflex/OpenReflex.app/Contents", true), "/Users/ann/Games/Open Reflex", "macOS player: the folder holding the .app");
                Eq(ReflexMods.ClientRootFor(@"C:\Games\Open Reflex\OpenReflex_Data", false), @"C:\Games\Open Reflex", "Windows player: the folder holding OpenReflex.exe");
                Eq(ReflexMods.ClientRootFor(@"D:\OpenReflex_Data\", false), @"D:\", "Windows player at a drive root");
                Eq(ReflexMods.ClientRootFor(@"\\server\share\Reflex\OpenReflex_Data", false), @"\\server\share\Reflex", "Windows UNC path");
                Eq(ReflexMods.ClientRootFor("/home/ann/reflex/OpenReflex_Data", false), "/home/ann/reflex", "Linux player");
                Eq(ReflexMods.ClientRootFor("/Users/ann/dev/REFLEX/Unity/Reflex/Assets", false), "/Users/ann/dev/REFLEX/Unity/Reflex", "editor: the project folder");
                Eq(ReflexMods.ClientRootFor(@"C:\Users\Zoë\Bikes Game\OpenReflex_Data", false), @"C:\Users\Zoë\Bikes Game", "Windows path with spaces and Unicode");

                // 2. relative ids with either separator
                Eq(ReflexMods.RelativeId(@"C:\Games\Mods\Bikes", @"C:\Games\Mods\Bikes\My Stuff\New Bike Models"), "My Stuff/New Bike Models", "Windows id");
                Eq(ReflexMods.RelativeId(@"C:\Games\Mods\Bikes\", @"c:/games/mods/bikes/Ünïcode Bikes\2010 pack"), "Ünïcode Bikes/2010 pack", "mixed separators and case");
                Eq(ReflexMods.RelativeId("/a/Mods/Bikes", "/a/Mods/Bikes2/x"), null, "a sibling folder is not under the root");
                Eq(ReflexMods.RelativeId("/a/Mods/Bikes", "/a/Mods/Bikes"), null, "the root itself has no id");

                // 3. junk
                foreach (var j in new[] { ".DS_Store", "._MXs.dx9.database", "Thumbs.db", "THUMBS.DB", "desktop.ini", "__MACOSX", "$RECYCLE.BIN", "" }) Check(ReflexMods.IsJunk(j), "junk: " + j);
                foreach (var j in new[] { "MXs.dx9.database", "New Bike Models", "mod.ini", "Thumbs" }) Check(!ReflexMods.IsJunk(j), "not junk: " + j);

                // 4. mod.ini
                var ini = BikeMods.ParseIni("\uFEFF; comment\r\n[Mod]\r\nName = \"Factory Bikes\"\r\n# other\r\nAUTHOR=Zoë\r\ndescription = Two = signs\r\nnonsense\r\n");
                Eq(ini.TryGetValue("name", out var n) ? n : null, "Factory Bikes", "ini name (BOM, CRLF, quotes, section)");
                Eq(ini.TryGetValue("author", out var au) ? au : null, "Zoë", "ini key case");
                Eq(ini.TryGetValue("description", out var de) ? de : null, "Two = signs", "ini value with '='");

                // 5. namespaces
                var (id, db) = BikeMods.Split("mod:My Stuff/New Bike Models>HR_MXs");
                Eq(id, "My Stuff/New Bike Models", "namespace id"); Eq(db, "HR_MXs", "namespace database");
                Eq(BikeMods.BaseDb("MXs"), "MXs", "an original database"); Eq(BikeMods.NamespaceOf("MXs"), "", "an original database has no namespace");

                // 6. the scan of a scratch tree
                tmp = Path.Combine(Path.GetTempPath(), "bikemods-test-" + Guid.NewGuid().ToString("N"));
                string bikes = Path.Combine(tmp, "Mods", "Bikes");
                string a = Path.Combine(bikes, "My Folder", "Sub folder", "New Bike Models ü");
                Touch(Path.Combine(a, "MXs.dx9.database")); Touch(Path.Combine(a, "MXs.dx9.package"));
                Touch(Path.Combine(a, "HR_MXS.DX9.DATABASE")); Touch(Path.Combine(a, "hr_mxs.dx9.package"));
                Touch(Path.Combine(a, "._MXs.dx9.database")); Touch(Path.Combine(a, ".DS_Store")); Touch(Path.Combine(a, "Thumbs.db"));
                Touch(Path.Combine(a, "Nested", "MX_GraphicKits.dx9.database")); Touch(Path.Combine(a, "Nested", "MX_GraphicKits.dx9.package"));   // inside a mod: part of it, not a mod
                string b = Path.Combine(bikes, "Kits only");
                Touch(Path.Combine(b, "MX_GraphicKits.dx9.database")); Touch(Path.Combine(b, "MX_GraphicKits.dx9.package"));
                File.WriteAllText(Path.Combine(b, "mod.ini"), "name = Kit Pack\nauthor = Someone\nthumbnail = missing.png\n");
                string c = Path.Combine(bikes, "Other", "New Bike Models ü");   // the same folder name as a
                Touch(Path.Combine(c, "MXs.dx9.database")); Touch(Path.Combine(c, "MXs.dx9.package"));
                File.WriteAllText(Path.Combine(c, "mod.json"), "{\"description\": \"json info\"}");
                Touch(Path.Combine(bikes, "__MACOSX", "New Bike Models", "MXs.dx9.database")); Touch(Path.Combine(bikes, "__MACOSX", "New Bike Models", "MXs.dx9.package"));
                Touch(Path.Combine(bikes, "Broken", "MXs.dx9.database"));   // no package: not a mod
                Touch(Path.Combine(bikes, "MXs.dx9.database")); Touch(Path.Combine(bikes, "MXs.dx9.package"));   // loose in Bikes: not a mod
                string deep = bikes; for (int i = 0; i < 9; i++) deep = Path.Combine(deep, "level " + i);
                Touch(Path.Combine(deep, "MXs.dx9.database")); Touch(Path.Combine(deep, "MXs.dx9.package"));
                var mods = BikeMods.Scan(bikes);
                Log("scan: " + string.Join(" | ", mods.Select(m => m.id + " = \"" + m.name + "\" [" + string.Join(",", m.databases) + "]")));
                Check(mods.Count == 4, "four mods (nested user folders, the deep one; not __MACOSX, the loose files, the broken one, the nested folder of a mod): " + mods.Count);
                var ma = mods.FirstOrDefault(m => m.id == "My Folder/Sub folder/New Bike Models ü");
                Check(ma != null, "the nested mod's id has '/' separators and keeps the Unicode name");
                if (ma != null)
                {
                    Check(ma.databases.SequenceEqual(new[] { "HR_MXS", "MXs" }), "its databases, the AppleDouble copy ignored: " + string.Join(",", ma.databases));
                    Eq(ma.name, "New Bike Models ü (My Folder/Sub folder)", "two mods of one folder name: the parent tells them apart");
                }
                var mb = mods.FirstOrDefault(m => m.id == "Kits only");
                Check(mb != null && mb.name == "Kit Pack" && mb.author == "Someone" && mb.thumbnail == null, "mod.ini names it (a missing thumbnail is dropped)");
                var mc = mods.FirstOrDefault(m => m.id == "Other/New Bike Models ü");
                Check(mc != null && mc.description == "json info", "mod.json describes it");
                Check(mods.Any(m => m.id.StartsWith("level 0/level 1/", StringComparison.Ordinal)), "a mod nine folders deep is found");
                Check(BikeMods.Scan(Path.Combine(tmp, "nothing here")).Count == 0, "a missing folder: no mods");

                // 6b. shared bikes (core/plan/RXMOD.md "Shared bikes"): two packs' bikes of one model (the same "share" and "bike") are one card
                // whose GRAPHIC KIT holds both packs' kits; another share key, or none, stays a card of its own
                SharedBikes(Path.Combine(tmp, "Shared", "Mods", "Bikes"));
                // 6c. kit packs: "parts" mods that name their bike by model ("share" + "bike"), carrying no mesh: their kits join the bike's
                // GRAPHIC KIT, whose default is the bike's own kit ("kit"); an unknown share is left out; mod-named parts are as before
                KitPacks(Path.Combine(tmp, "KitPacks", "Mods", "Bikes"));
                // 6d. one bikes mod with two bikes on one original: a card each ("<mod>", "<mod>#<bike>"), each its own bike, name and keys
                SplitBikes(Path.Combine(tmp, "Split", "Mods", "Bikes"));
                // 6e. shared bikes join only with the same name too; a share-named kit pack with a "model" goes on that name only; two cards
                // of one bike name show their packs' names
                SharedNames(Path.Combine(tmp, "Names", "Mods", "Bikes"));

                // 7. the real mod (optional): its packages through the namespace, the chassis model parsed
                string real = Environment.GetEnvironmentVariable("REFLEX_BIKEMOD_ROOT");
                if (!string.IsNullOrEmpty(real) && Directory.Exists(real))
                {
                    var rm = BikeMods.Scan(real).FirstOrDefault(m => m.databases.Any(d => d.Equals("MXs", StringComparison.OrdinalIgnoreCase)));
                    Check(rm != null, "the real mod is found under " + real);
                    if (rm != null)
                    {
                        Log("real mod " + rm.id + ": " + string.Join(",", rm.databases));
                        Check(rm.Has("MXs", "MarsF_Lite") && rm.ChassisModel("MXs", "MarsF_Lite", "MX_MarsF_Lite") == "MX_MarsF_Lite", "MXs/MarsF_Lite and its chassis MX_MarsF_Lite");
                        Check(rm.Has("MX_GraphicKits", "MX_MarsF_Lite_THQ_Red") && rm.Has("HR_MXs", "HR_MarsF_Lite"), "a graphic kit and the HR twin");
                        Check(!rm.Has("MX_Tires", "MX_Tire_THQ"), "no tyres in the mod (the originals stay)");
                        string d = BikeMods.Describe(rm, "MXs", "MarsF_Lite", "MX_MarsF_Lite");
                        Log("real chassis: " + d);
                        Check(d != null && d.Contains("MX_Plstc") && d.Contains("database mod:" + rm.id + ">MXs mod=True"), "the mod chassis parses through the namespace and binds the kit's MX_Plstc");
                        string hr = BikeMods.Describe(rm, "HR_MXs", "HR_MarsF_Lite", "HR_MX_MarsF_Lite");
                        Log("real HR chassis: " + hr);
                        Check(hr != null, "the HR chassis parses");
                        // every chassis (and its HR twin): each skinned surface follows the nodes its vertices are weighted to (a 30 degree
                        // steer through OriginalBikeSkin), whatever node the surface is bound to (VenusF's rear shock is bound to OBJ_UPR_Forks)
                        foreach (string family in new[] { "Earth", "Mars", "Mercury", "Neptune", "Venus" })
                            foreach (string pkg in new[] { family, family + "_Lite", family + "F", family + "F_Lite" })
                                foreach (var (cdb, p) in new[] { ("MXs", pkg), ("HR_MXs", "HR_" + pkg) })
                                {
                                    string model = rm.ChassisModel(cdb, p, (cdb == "HR_MXs" ? "HR_MX_" : "MX_") + pkg);
                                    if (model == null) continue;
                                    var lines = BikeMods.SkinCheck(rm, cdb, p, model);
                                    foreach (var l in lines.Where(l => l.Contains("SKINNED"))) Log("  " + cdb + "/" + model + " " + l);
                                    Check(lines.Last() == "0 bad", cdb + "/" + model + ": every skinned surface follows its weights under a steer");
                                }
                    }
                }
                else Log("(REFLEX_BIKEMOD_ROOT not set: the real mod is not read)");
            }
            catch (Exception e) { Check(false, "exception: " + e); }
            finally { try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception) { } }
            Log(bad == 0 ? "PASS" : "FAIL " + bad);
            if (Application.isBatchMode) EditorApplication.Exit(bad == 0 ? 0 : 1);
        }

        /// A bike .rxmod folder: its mod.json (the bikes' JSON as given) and every file it names (empty: the scan reads no mesh or map).
        static void RxBikeMod(string dir, string name, string author, string bikesJson, params string[] files)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "mod.json"), "{\"format\": \"reflex-mod\", \"version\": 1, \"type\": \"bikes\", \"name\": \"" + name + "\", \"author\": \"" + author + "\", \"bikes\": [" + bikesJson + "]}");
            foreach (var f in files) Touch(Path.Combine(dir, f.Replace('/', Path.DirectorySeparatorChar)));
        }

        static void SharedBikes(string root)
        {
            const int Yz = 40;   // (any VehicleModelId: the scan does not look the original up)
            const string Key = "sb1-3f9a0c1d2e4b5a67";
            string Bike(string share, string kit, string body, string parts = null)
                => "{\"bike\": " + Yz + ", \"brand\": \"Yamaha\", \"model\": \"YZ450F\", \"mesh\": \"yz.rxmesh\"" + (share != null ? ", \"share\": \"" + share + "\"" : "")
                 + (kit != null ? ", \"kit\": \"" + kit + "\", \"kitBrand\": \"Factory\", \"kitMaterials\": [\"body\", \"seat\"]" : "")
                 + ", \"materials\": {\"body\": {\"diffuse\": \"" + body + "\", \"spec\": \"body_spec.png\"}, \"seat\": {\"diffuse\": \"seat.png\"}, \"frame\": {\"template\": \"frame\", \"diffuse\": \"frame.png\"}}"
                 + (parts != null ? ", \"parts\": [" + parts + "]" : "") + "}";
            const string FactoryKits = "{\"slot\": \"GraphicKit\", \"name\": \"GRAPHIC KIT\", \"options\": [{\"id\": \"stock\", \"name\": \"Factory\", \"default\": true},"
                                     + " {\"id\": \"monster\", \"name\": \"Monster\", \"brand\": \"D'Cor\", \"materials\": {\"body\": {\"diffuse\": \"kits/monster.png\"}}}]}";
            RxBikeMod(Path.Combine(root, "Stock OEMs"), "Stock OEMs", "Ann", Bike(Key, "Stock", "stock_body.png"), "yz.rxmesh", "stock_body.png", "body_spec.png", "seat.png", "frame.png");
            RxBikeMod(Path.Combine(root, "Factory Bikes"), "Factory Bikes", "Bob", Bike(Key, null, "factory_body.png", FactoryKits), "yz.rxmesh", "factory_body.png", "body_spec.png", "seat.png", "frame.png", "kits/monster.png");
            RxBikeMod(Path.Combine(root, "Other Creator"), "Other Creator", "Cy", Bike("sb1-0000000000000001", "Other", "other_body.png"), "yz.rxmesh", "other_body.png");
            RxBikeMod(Path.Combine(root, "Old Mod"), "Old Mod", "Dee", Bike(null, null, "old_body.png"), "yz.rxmesh", "old_body.png");
            var mods = BikeMods.Scan(root);
            Log("shared scan: " + string.Join(" | ", mods.Select(m => m.id + " listed=" + (BikeMods.ListedBike(m, Yz) != null))));
            var factory = mods.FirstOrDefault(m => m.id == "Factory Bikes"); var stock = mods.FirstOrDefault(m => m.id == "Stock OEMs");
            var other = mods.FirstOrDefault(m => m.id == "Other Creator"); var old = mods.FirstOrDefault(m => m.id == "Old Mod");
            Check(factory != null && stock != null && other != null && old != null, "the four bike mods are scanned");
            if (factory == null || stock == null || other == null || old == null) return;
            var listed = mods.Where(m => BikeMods.ListedBike(m, Yz) != null).Select(m => m.id).ToList();
            Eq(string.Join(",", listed), "Factory Bikes,Old Mod,Other Creator", "one card for the two packs' YZ450F (the first by mod id), one for another model's, one for a bike without a share key");
            var b = BikeMods.ListedBike(factory, Yz); var j = stock.rx.bikes[0];
            Check(j.sharedInto == b && b.sharedInto == null, "Stock OEMs' bike is joined into Factory Bikes'");
            Eq(b.share, Key, "the share key is read"); Eq(j.kitName + "/" + j.kitBrand + "/" + string.Join("+", j.kitMaterials), "Stock/Factory/body+seat", "kit, kitBrand and kitMaterials are read");
            var kit = b.slots.FirstOrDefault(s => s.id == BikeMods.GraphicKitSlot);
            Check(kit != null && b.slots.Count(s => s.id == BikeMods.GraphicKitSlot) == 1, "the base has one GRAPHIC KIT slot");
            if (kit == null) return;
            Eq(string.Join(",", kit.options.Select(o => o.id + (o.isDefault ? "*" : ""))), "stock*,monster,Stock OEMs:stock", "both packs' kits, the other pack's ids namespaced by its mod id");
            var own = kit.Find("Stock OEMs:stock");
            Check(own != null && own.mod == stock.rx && own.name == "Stock" && own.brand == "Factory", "the joined pack's own kit: its name and brand, its mod");
            if (own != null)
            {
                Eq(string.Join(",", own.materials.Keys.OrderBy(k => k)), "body,seat", "it repaints the kit materials only (not the frame)");
                var body = RxParts.Material(new List<RxPartOption> { own }, "body", b.materials["body"]);
                Check(body.diffuse == "stock_body.png" && body.diffuseMod == stock.rx && body.spec == "body_spec.png" && body.specMod == stock.rx, "worn: the body's maps are read from Stock OEMs");
                var frame = RxParts.Material(new List<RxPartOption> { own }, "frame", b.materials["frame"]);
                Check(frame.diffuse == "frame.png" && frame.diffuseMod == factory.rx, "worn: the frame stays the base's");
            }
            var monster = kit.Find("monster");
            Check(monster != null && monster.mod == factory.rx && RxParts.Material(new List<RxPartOption> { monster }, "body", b.materials["body"]).diffuseMod == factory.rx, "the base's own other kit stays its own, read from its mod");
            Eq(BikeMods.AuthorFor(factory, Yz), "Bob, Ann", "the card names both packs' authors");
            Eq(BikeMods.AuthorFor(old, Yz), "Dee", "a bike that stands alone: its mod's author");
            Eq(RxParts.SaveKey(b, kit), "RxPart.share-" + Key + "-yz450f." + Yz + ".GraphicKit", "a shared bike's choice is kept by its share key and name");
            Check(RxParts.SaveKey(j, kit) == RxParts.SaveKey(b, kit), "the joined bike has the same save key (the choice survives the base changing)");
            var ob = BikeMods.ListedBike(other, Yz); var odb = BikeMods.ListedBike(old, Yz);
            Check(ob.sharedInto == null && ob.slots.Count == 0, "another model's bike: no kits of the others added");
            Check(odb.share == null && odb.kitName == null && odb.kitMaterials == null && odb.slots.Count == 0, "a bike without a share key: as before (nothing read, no slot)");
            var oldSlot = new RxPartSlot { id = "GraphicKit" };
            string oldKey = RxParts.SaveKey(odb, oldSlot);
            Check(oldKey.StartsWith(RxParts.KeyPrefix, StringComparison.Ordinal) && !oldKey.Contains(RxParts.SharePrefix) && oldKey.EndsWith("." + Yz + ".GraphicKit", StringComparison.Ordinal), "a bike without a share key keeps its mod's save key: " + oldKey);
            // the other pack removed: the one left stands alone again, with no GRAPHIC KIT added
            Directory.Delete(Path.Combine(root, "Factory Bikes"), true);
            var ab = BikeMods.ListedBike(BikeMods.Scan(root).FirstOrDefault(m => m.id == "Stock OEMs"), Yz);
            Check(ab != null && ab.sharedInto == null && ab.slots.Count == 0 && ab.sharedAuthors.Count == 0, "one pack alone: its card, no GRAPHIC KIT added");
        }

        /// A parts .rxmod folder: its mod.json ("parts" as given; "id" when given) and every file it names (empty).
        static void RxPartsMod(string dir, string id, string name, string partsJson, params string[] files) => RxPartsMod(dir, id, name, 0, partsJson, files);
        static void RxPartsMod(string dir, string id, string name, int importer, string partsJson, params string[] files)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "mod.json"), "{\"format\": \"reflex-mod\", \"version\": 1, \"type\": \"parts\"" + (id != null ? ", \"id\": \"" + id + "\"" : "") + (importer > 0 ? ", \"importer\": " + importer : "")
                                                           + ", \"name\": \"" + name + "\", \"author\": \"Kit Maker\", \"parts\": [" + partsJson + "]}");
            foreach (var f in files) Touch(Path.Combine(dir, f.Replace('/', Path.DirectorySeparatorChar)));
        }

        static void KitPacks(string root)
        {
            const int Yz = 10;
            const string Key = "sb1-b0cef2942e9d318e";
            string Kit(string id, string name, string png) => "{\"id\": \"" + id + "\", \"name\": \"" + name + "\", \"brand\": \"Factory\", \"materials\": {\"body\": {\"diffuse\": \"" + png + "\"}, \"seat\": {\"diffuse\": \"" + png + "\"}}}";
            string Set(string target, params string[] kits) => "{" + target + ", \"slots\": [{\"slot\": \"GraphicKit\", \"name\": \"GRAPHIC KIT\", \"options\": [" + string.Join(", ", kits) + "]}]}";
            string ByShare(string share) => "\"share\": \"" + share + "\", \"bike\": " + Yz + ", \"model\": \"YZ450F\"";
            // the bike pack: the YZ450F with its own kit "Stock", no GRAPHIC KIT of its own
            RxBikeMod(Path.Combine(root, "Stock OEMs"), "Stock OEMs", "Ann",
                      "{\"bike\": " + Yz + ", \"brand\": \"Yamaha\", \"model\": \"YZ450F\", \"mesh\": \"yz.rxmesh\", \"share\": \"" + Key + "\", \"kit\": \"Stock\", \"kitBrand\": \"Yamaha\", \"kitMaterials\": [\"body\", \"seat\"],"
                      + " \"materials\": {\"body\": {\"diffuse\": \"body.png\", \"spec\": \"body_spec.png\"}, \"seat\": {\"diffuse\": \"seat.png\"}}}",
                      "yz.rxmesh", "body.png", "body_spec.png", "seat.png");
            // a bike pack of the old kind (named by mod) with a mod-named parts pack: as before
            RxBikeMod(Path.Combine(root, "Old Bike"), "Old Bike", "Dee", "{\"bike\": 41, \"brand\": \"Honda\", \"model\": \"CRF450R\", \"mesh\": \"crf.rxmesh\", \"materials\": {\"body\": {\"diffuse\": \"crf.png\"}}}", "crf.rxmesh", "crf.png");
            RxPartsMod(Path.Combine(root, "Old Kits"), null, "Old Kits", Set("\"mod\": \"Old Bike\", \"bike\": \"CRF450R\"", Kit("red", "Red", "red.png")), "red.png");
            // two kit packs for the YZ450F by its share key, and one for a model nobody has
            RxPartsMod(Path.Combine(root, "Factory Kits"), "ktm6060-factory-bikes-kits", "Factory Bikes",
                       Set(ByShare(Key), Kit("star-yamaha", "Star Yamaha", "yz450f/star-yamaha.png"), Kit("champion", "Champion", "yz450f/champion.png")),
                       "yz450f/star-yamaha.png", "yz450f/champion.png");
            RxPartsMod(Path.Combine(root, "More Kits"), "more-kits", "More Kits", Set(ByShare(Key), Kit("star-yamaha", "Star Yamaha 2027", "star.png")), "star.png");
            RxPartsMod(Path.Combine(root, "Lost Kits"), "lost-kits", "Lost Kits", Set(ByShare("sb1-ffffffffffffffff"), Kit("blue", "Blue", "blue.png")), "blue.png");
            // a bike (no share key) with its own GRAPHIC KIT, and a mod-named kit pack for it
            RxBikeMod(Path.Combine(root, "Own Kits Bike"), "Own Kits Bike", "Eve",
                      "{\"bike\": 42, \"brand\": \"KTM\", \"model\": \"450 SX-F\", \"mesh\": \"sx.rxmesh\", \"materials\": {\"body\": {\"diffuse\": \"sx.png\"}, \"seat\": {\"diffuse\": \"sxs.png\"}},"
                      + " \"parts\": [{\"slot\": \"GraphicKit\", \"name\": \"GRAPHIC KIT\", \"options\": [{\"id\": \"stock\", \"name\": \"Stock\", \"default\": true}, " + Kit("blue", "Blue", "kits/blue.png") + "]}]}",
                      "sx.rxmesh", "sx.png", "sxs.png", "kits/blue.png");
            RxPartsMod(Path.Combine(root, "Extra Kits"), "extra-kits", "Extra Kits", Set("\"mod\": \"Own Kits Bike\", \"bike\": \"450 SX-F\"", Kit("green", "Green", "green.png")), "green.png");
            // an Alive-style bike: its kits in its own "-parts" mod (written by the importer), and a second mod-named kit pack (its id sorts first)
            RxBikeMod(Path.Combine(root, "Alive YZ"), "Alive YZ", "Alive", "{\"bike\": 43, \"brand\": \"Yamaha\", \"model\": \"YZ250F\", \"mesh\": \"ayz.rxmesh\", \"materials\": {\"body\": {\"diffuse\": \"ayz.png\"}, \"seat\": {\"diffuse\": \"ayzs.png\"}}}",
                      "ayz.rxmesh", "ayz.png", "ayzs.png");
            RxPartsMod(Path.Combine(root, "Alive YZ-parts"), null, "Alive YZ parts", 3,
                       Set("\"mod\": \"Alive YZ\", \"bike\": 43", "{\"id\": \"1000\", \"name\": \"Stock\", \"default\": true}", Kit("1001", "Monster", "k1001.png")), "k1001.png");
            RxPartsMod(Path.Combine(root, "Aaa Kits"), "aaa-kits", "Aaa Kits", Set("\"mod\": \"Alive YZ\", \"bike\": \"YZ250F\"", Kit("pink", "Pink", "pink.png")), "pink.png");
            var mods = BikeMods.Scan(root);
            Log("kit packs scan: " + string.Join(" | ", mods.Select(m => m.id + (m.rx != null ? " (" + m.rx.type + ")" : ""))));
            var stock = mods.FirstOrDefault(m => m.id == "Stock OEMs"); var factory = mods.FirstOrDefault(m => m.id == "Factory Kits");
            var more = mods.FirstOrDefault(m => m.id == "More Kits"); var oldBike = mods.FirstOrDefault(m => m.id == "Old Bike"); var oldKits = mods.FirstOrDefault(m => m.id == "Old Kits");
            Check(stock != null && factory != null && more != null && oldBike != null && oldKits != null, "the bike packs and the kit packs of installed bikes are scanned");
            Check(!mods.Any(m => m.id == "Lost Kits"), "a kit pack whose share no installed bike has is left out");
            if (stock == null || factory == null || more == null || oldBike == null || oldKits == null) return;
            var set = factory.rx.partSets[0];
            Eq(set.share + "/" + set.model + "/" + set.targetBike + "/" + set.targetMod, Key + "/YZ450F/" + Yz + "/", "share, model and bike are read; no mod needed");
            var b = BikeMods.ListedBike(stock, Yz);
            Check(b != null && set.target == b && more.rx.partSets[0].target == b, "both kit packs find the bike by its share key and VehicleModelId");
            Eq(string.Join(",", mods.Where(m => BikeMods.ListedBike(m, Yz) != null).Select(m => m.id)), "Stock OEMs", "one card for the YZ450F");
            if (b == null) return;
            Check(b.slots.Count(s => s.id == BikeMods.GraphicKitSlot) == 1, "the bike has one GRAPHIC KIT slot");
            var kit = b.slots.FirstOrDefault(s => s.id == BikeMods.GraphicKitSlot);
            if (kit == null) return;
            Eq(string.Join(",", kit.options.Select(o => o.id + (o.isDefault ? "*" : ""))), "*,ktm6060-factory-bikes-kits:star-yamaha,ktm6060-factory-bikes-kits:champion,more-kits:star-yamaha",
               "the bike's own kit as the default, then each pack's kits, ids namespaced by the pack's id");
            Eq(kit.Default?.name + "/" + kit.Default?.brand, "Stock/Yamaha", "the default is named after the bike's \"kit\" (not STOCK)");
            var star = kit.Find("ktm6060-factory-bikes-kits:star-yamaha");
            Check(star != null && star.mod == factory.rx && star.name == "Star Yamaha" && star.brand == "Factory" && star.mesh == null, "a pack's kit: its name, brand and mod, no mesh");
            if (star != null)
            {
                var body = RxParts.Material(new List<RxPartOption> { star }, "body", b.materials["body"]);
                Check(body.diffuse == "yz450f/star-yamaha.png" && body.diffuseMod == factory.rx && body.spec == "body_spec.png" && body.specMod == stock.rx, "worn: its diffuse read from the kit pack, the rest the bike's");
            }
            var star2 = kit.Find("more-kits:star-yamaha");
            Check(star2 != null && star2.mod == more.rx && RxParts.Material(new List<RxPartOption> { star2 }, "seat", b.materials["seat"]).diffuseMod == more.rx, "a second pack's kit of the same option id: its own, read from its pack");
            Eq(RxParts.SaveKey(b, kit), "RxPart.share-" + Key + "-yz450f." + Yz + ".GraphicKit", "the choice is kept by the share key and name");
            // the mod-named parts pack: its slot added as it is (ids and default unchanged)
            var ob = BikeMods.ListedBike(oldBike, 41); var okit = ob?.slots.FirstOrDefault(s => s.id == BikeMods.GraphicKitSlot);
            Check(oldKits.rx.partSets[0].target == ob && okit != null && !okit.shared && okit.mod == oldKits.rx, "a mod-named parts pack's slot is added as it is");
            if (okit != null) Eq(string.Join(",", okit.options.Select(o => o.id + (o.isDefault ? "*" : "") + "=" + o.name)), "*=STOCK,red=Red", "its options keep their ids, its default STOCK");
            // a bike with its own GRAPHIC KIT (no share key) + a mod-named kit pack: its kits join, the bike's keep their ids
            var own = BikeMods.ListedBike(mods.FirstOrDefault(m => m.id == "Own Kits Bike"), 42); var ownKit = own?.slots.FirstOrDefault(s => s.id == BikeMods.GraphicKitSlot);
            Check(own != null && ownKit != null && own.slots.Count(s => s.id == BikeMods.GraphicKitSlot) == 1, "a bike with its own GRAPHIC KIT keeps one slot");
            if (ownKit != null)
            {
                Eq(string.Join(",", ownKit.options.Select(o => o.id + (o.isDefault ? "*" : ""))), "stock*,blue,extra-kits:green", "a mod-named kit pack's kits join the bike's own (its ids unchanged, the pack's namespaced)");
                var green = ownKit.Find("extra-kits:green");
                Check(green != null && green.mod == mods.First(m => m.id == "Extra Kits").rx && ownKit.Find("blue")?.mod == own.mod, "each option read from its own mod");
                Eq(RxParts.SaveKey(own, ownKit), "RxPart.Own Kits Bike.42.GraphicKit", "a bike without a share key keeps its mod's save key");
            }
            // the Alive-style bike: its own parts mod's kits as they are (saved ids kept), the second pack's joined
            var ab = BikeMods.ListedBike(mods.FirstOrDefault(m => m.id == "Alive YZ"), 43); var aKit = ab?.slots.FirstOrDefault(s => s.id == BikeMods.GraphicKitSlot);
            Check(ab != null && aKit != null && ab.slots.Count(s => s.id == BikeMods.GraphicKitSlot) == 1, "the Alive-style bike has one GRAPHIC KIT slot");
            if (aKit != null)
            {
                Eq(string.Join(",", aKit.options.Select(o => o.id + (o.isDefault ? "*" : ""))), "1000*,1001,aaa-kits:pink", "both lists merged, the bike's own parts mod's ids unchanged");
                Check(aKit.mod == mods.First(m => m.id == "Alive YZ-parts").rx && aKit.Find("aaa-kits:pink")?.mod == mods.First(m => m.id == "Aaa Kits").rx, "the slot is its own parts mod's; the pack's kit read from the pack");
            }
            // the bike pack removed: the kit packs are left out
            Directory.Delete(Path.Combine(root, "Stock OEMs"), true);
            var left = BikeMods.Scan(root);
            Check(!left.Any(m => m.id == "Factory Kits" || m.id == "More Kits"), "without the bike pack the kit packs are left out");
        }

        static void SplitBikes(string root)
        {
            const int Ktm = 28;
            string Bike(string id, int vm, string brand, string model, string png)
                => "{\"id\": \"" + id + "\", \"bike\": " + vm + ", \"brand\": \"" + brand + "\", \"model\": \"" + model + "\", \"mesh\": \"" + id + ".rxmesh\", \"materials\": {\"body\": {\"diffuse\": \"" + png + "\"}}}";
            RxBikeMod(Path.Combine(root, "Stock OEMs"), "Stock OEMs", "Ann",
                      Bike("fc450", Ktm, "Husqvarna", "FC450", "fc.png") + ", " + Bike("450sx-f", Ktm, "KTM", "450SX-F", "sx.png") + ", " + Bike("yz250f", 29, "Yamaha", "YZ250F", "yz.png"),
                      "fc450.rxmesh", "450sx-f.rxmesh", "yz250f.rxmesh", "fc.png", "sx.png", "yz.png");
            // a mod-named kit pack for the second bike (by its model)
            RxPartsMod(Path.Combine(root, "SX Kits"), "sx-kits", "SX Kits", "{\"mod\": \"Stock OEMs\", \"bike\": \"450SX-F\", \"slots\": [{\"slot\": \"GraphicKit\", \"name\": \"GRAPHIC KIT\", \"options\": [{\"id\": \"orange\", \"name\": \"Orange\", \"materials\": {\"body\": {\"diffuse\": \"orange.png\"}}}]}]}", "orange.png");
            var mods = BikeMods.Scan(root);
            Log("split scan: " + string.Join(" | ", mods.Select(m => m.id)));
            var listed = mods.Where(m => BikeMods.ListedBike(m, Ktm) != null).ToList();
            Eq(string.Join(",", listed.Select(m => m.id)), "Stock OEMs,Stock OEMs#450sx-f", "two bikes of one mod on #28: two entries, the first under the mod's own id");
            if (listed.Count != 2) return;
            var (e1, e2) = (listed[0], listed[1]);
            var (b1, b2) = (BikeMods.BikeOf(e1, Ktm), BikeMods.BikeOf(e2, Ktm));
            Check(b1 != null && b2 != null && b1.id == "fc450" && b2.id == "450sx-f", "each entry is built from its own bike: " + b1?.id + ", " + b2?.id);
            Check(ReferenceEquals(e1.rx, e2.rx) && e2.only == b2 && e1.only == null, "both entries share the mod's RxMod (opened once); the second is restricted to its bike");
            Eq(BikeMods.NameFor(e1, Ktm, null)?.Full + " / " + BikeMods.NameFor(e2, Ktm, null)?.Full, "Husqvarna FC450 / KTM 450SX-F", "each card named as its own bike");
            Eq(e2.name, "Stock OEMs", "the second card's pack name is the mod's");
            Check(BikeMods.OwnerOf(b1, mods) == e1 && BikeMods.OwnerOf(b2, mods) == e2, "each bike's entry is its own");
            Check(BikeMods.ListedBike(e1, 29)?.id == "yz250f" && BikeMods.ListedBike(e2, 29) == null, "the mod's bike of another original stays on the mod's own entry");
            var slot = new RxPartSlot { id = BikeMods.GraphicKitSlot };
            Eq(RxParts.SaveKey(b2, slot), "RxPart.Stock OEMs#450sx-f." + Ktm + ".GraphicKit", "the second bike's choices are saved under its own id");
            Check(RxParts.SaveKey(b1, slot) == "RxPart.Stock OEMs." + Ktm + ".GraphicKit", "the first keeps the mod's id (saved choices stay)");
            Check(b1.Token != b2.Token && b1.Token == "b:" + b1.mod.hash + ":" + Ktm && System.Text.RegularExpressions.Regex.IsMatch(b2.Token, "^b:[0-9a-f]{12}:" + Ktm + "$"),
                  "online tokens: the first's as before, the second's its own in the same form: " + b1.Token + ", " + b2.Token);
            var sx = mods.FirstOrDefault(m => m.id == "SX Kits");
            Check(sx != null && sx.rx.partSets[0].target == b2 && b2.slots.Any(s => s.id == BikeMods.GraphicKitSlot) && !b1.slots.Any(), "a parts pack naming the second bike lands on it only");
        }

        static void SharedNames(string root)
        {
            const int Yz = 40;
            const string Key = "sb1-1111222233334444", Key2 = "sb1-5555666677778888";
            string Bike(string share, string model, string png)
                => "{\"bike\": " + Yz + ", \"brand\": \"Yamaha\", \"model\": \"" + model + "\", \"mesh\": \"yz.rxmesh\", \"share\": \"" + share + "\", \"materials\": {\"body\": {\"diffuse\": \"" + png + "\"}}}";
            RxBikeMod(Path.Combine(root, "A Pack"), "A Pack", "Ann", Bike(Key, "YZ450F", "a.png"), "yz.rxmesh", "a.png");
            RxBikeMod(Path.Combine(root, "B Pack"), "B Pack", "Bob", Bike(Key, "yz450f", "b.png"), "yz.rxmesh", "b.png");            // same name, another case: joins A
            RxBikeMod(Path.Combine(root, "C Pack"), "C Pack", "Cy", Bike(Key, "YZ450F Monster", "c.png"), "yz.rxmesh", "c.png");      // same model, another name: its own card
            RxBikeMod(Path.Combine(root, "D Pack"), "D Pack", "Dee", Bike(Key2, "YZ 450-F", "d.png"), "yz.rxmesh", "d.png");          // the name, another model: its own card
            string Kits(string share, string model, string id) => "{\"share\": \"" + share + "\", \"bike\": " + Yz + (model != null ? ", \"model\": \"" + model + "\"" : "")
                + ", \"slots\": [{\"slot\": \"GraphicKit\", \"name\": \"GRAPHIC KIT\", \"options\": [{\"id\": \"" + id + "\", \"name\": \"" + id + "\", \"materials\": {\"body\": {\"diffuse\": \"" + id + ".png\"}}}]}]}";
            RxPartsMod(Path.Combine(root, "Kits YZ"), "kits-yz", "Kits YZ", Kits(Key, "YZ450F", "k1"), "k1.png");
            RxPartsMod(Path.Combine(root, "Kits Monster"), "kits-monster", "Kits Monster", Kits(Key, "yz450f monster", "k2"), "k2.png");
            RxPartsMod(Path.Combine(root, "Kits Other"), "kits-other", "Kits Other", Kits(Key2, "YZ450F", "k3"), "k3.png");
            var mods = BikeMods.Scan(root);
            Log("names scan: " + string.Join(" | ", mods.Select(m => m.id + " listed=" + (BikeMods.ListedBike(m, Yz) != null))));
            BikeMod M(string id) => mods.FirstOrDefault(m => m.id == id);
            if (M("A Pack") == null || M("B Pack") == null || M("C Pack") == null || M("D Pack") == null) { Check(false, "the four bike packs are scanned"); return; }
            Eq(string.Join(",", mods.Where(m => BikeMods.ListedBike(m, Yz) != null).Select(m => m.id)), "A Pack,C Pack,D Pack", "same share + model + name join (A, B); another name (C) or another share (D) stay cards of their own");
            RxModBike a = BikeMods.ListedBike(M("A Pack"), Yz), c = BikeMods.ListedBike(M("C Pack"), Yz), d = BikeMods.ListedBike(M("D Pack"), Yz);
            Check(M("B Pack").rx.bikes[0].sharedInto == a && c.sharedInto == null && d.sharedInto == null, "B's YZ450F joined into A's");
            string Ids(RxModBike b) => string.Join(",", b.slots.Where(s => s.id == BikeMods.GraphicKitSlot).SelectMany(s => s.options).Select(o => o.id));
            Check(Ids(a).Contains("kits-yz:k1") && !Ids(a).Contains("k2") && !Ids(a).Contains("k3"), "the YZ450F kit pack on A's YZ450F only: " + Ids(a));
            Check(Ids(c).Contains("kits-monster:k2") && !Ids(c).Contains("k1") && !Ids(c).Contains("k3"), "the YZ450F Monster pack on C's only: " + Ids(c));
            Check(Ids(d).Contains("kits-other:k3") && !Ids(d).Contains("k1") && !Ids(d).Contains("k2"), "the other model's pack on D's only: " + Ids(d));
            var gk = new RxPartSlot { id = BikeMods.GraphicKitSlot };
            Check(RxParts.SaveKey(a, gk) != RxParts.SaveKey(c, gk), "two names of one share and model: their choices are saved apart (" + RxParts.SaveKey(c, gk) + ")");
            // the cards: A's and D's are both a "YZ450F" (names compared normalized): each shows its pack's name; C's is unique
            Eq(BikeMods.CardNameFor(M("A Pack"), Yz, null, mods)?.model, "YZ450F · A Pack", "two cards of one bike name: the pack's name after it");
            Eq(BikeMods.CardNameFor(M("D Pack"), Yz, null, mods)?.model, "YZ 450-F · D Pack", "the other one too");
            Eq(BikeMods.CardNameFor(M("C Pack"), Yz, null, mods)?.model, "YZ450F Monster", "a unique name stays as it is");
        }
    }
}
