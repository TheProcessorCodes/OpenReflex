// Reviewed Reflex ecosystem substitutions, packed alongside an imported level: port of tools/unleashed/reflex_scenery.py.
// For the tracks of scenery_profiles.json the listed source species become trees of a donor Reflex level's forest: the donor's forest
// (reflex-forest.bin), the tree / material / shader / texture resources its tree types name (reflex-resource-<name>.<type>) and the
// substituted instances (reflex-vegetation.json) go into the level's folder. The donor resources are read from the player's own Reflex
// library (an original install, or the client's game files) the way the other importers read stock content (UxDirectoryLibrary).
using System;
using System.Collections.Generic;
using System.Threading;
using Reflex.Port.Content.AliveImport;
using Reflex.Port.Content.UntamedImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnReflexScenery
    {
        public sealed class Plan
        {
            public string donor;
            /// lower-case source mesh -> the donor forest's TreeAsset.
            public readonly Dictionary<string, string> species = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// plan(track): the track's entry of scenery_profiles.json, or null.
        public static Plan PlanOf(string track)
        {
            var all = UnJson.Obj(UnJson.Loads(UnData.Text(UnData.SceneryProfiles)));
            if (!(all.Get(track) is JObj entry) || entry.items.Count == 0) return null;
            var p = new Plan { donor = (string)entry.Get("donor") };
            foreach (var kv in UnJson.Obj(entry.Get("species")).items) p.species[kv.Key] = (string)kv.Value;
            return p;
        }

        /// The string attributes of a BXML node (name -> value).
        static Dictionary<string, string> StringAttributes(UxBxml doc, int node)
        {
            var r = new Dictionary<string, string>(StringComparer.Ordinal);
            long[] n = doc.nodes[node];
            for (long a = n[6]; a < n[6] + n[7]; a++)
            {
                long[] at = doc.attrs[(int)a];
                if (at[3] == 1) r[doc.strings[(int)at[0]]] = doc.strings[(int)at[1]];
            }
            return r;
        }

        /// donor_assets(archive, donor): the forest's bytes, its tree types (TreeAsset names in order) and the resources they need.
        static void DonorAssets(string reflexRoot, string donor, out byte[] forest, out List<string> treeAssets, out List<KeyValuePair<KeyValuePair<string, string>, byte[]>> assets, CancellationToken ct)
        {
            using (var library = new UxDirectoryLibrary(reflexRoot))
            {
                var set = library.OpenLevel(donor);
                // available[(name, type)]: the last package listing the asset (Python's dict)
                var available = new Dictionary<KeyValuePair<string, string>, UxPcAsset>();
                var forests = new List<string>();
                foreach (var a in set.assets)
                {
                    var key = new KeyValuePair<string, string>(a.name, a.type);
                    if (!available.ContainsKey(key) && a.type == "forest") forests.Add(a.name);
                    available[key] = a;
                }
                byte[] Read(string name, string kind)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!available.TryGetValue(new KeyValuePair<string, string>(name, kind), out var a)) throw new KeyNotFoundException(name + "." + kind + " is not in the Reflex level " + donor);
                    return set.ReadLogical(a);
                }
                if (forests.Count != 1) throw new UnValueError("The Reflex level " + donor + " has " + forests.Count + " forests");
                forest = Read(forests[0], "forest");
                var doc = new UxBxml(forest);
                var types = new List<Dictionary<string, string>>(); var textures = new List<string>(); var seenTex = new HashSet<string>(StringComparer.Ordinal);
                void Tex(string name) { if (seenTex.Add(name)) textures.Add(name); }
                for (int i = 0; i < doc.nodes.Count; i++)
                {
                    string tag = doc.strings[(int)doc.nodes[i][0]];
                    if (tag == "Type") types.Add(StringAttributes(doc, i));
                    else if (tag == "BillboardAtlas") foreach (var v in StringAttributes(doc, i).Values) Tex(v);
                }
                var list = new List<KeyValuePair<KeyValuePair<string, string>, byte[]>>();
                var have = new HashSet<KeyValuePair<string, string>>();
                void Add(string name, string kind, byte[] data)
                {
                    var key = new KeyValuePair<string, string>(name, kind);
                    if (have.Add(key)) list.Add(new KeyValuePair<KeyValuePair<string, string>, byte[]>(key, data));
                    else for (int i = 0; i < list.Count; i++) if (list[i].Key.Equals(key)) list[i] = new KeyValuePair<KeyValuePair<string, string>, byte[]>(key, data);
                }
                treeAssets = new List<string>();
                foreach (var t in types)
                {
                    if (!t.TryGetValue("TreeAsset", out string tree)) throw new KeyNotFoundException("TreeAsset");
                    treeAssets.Add(tree);
                    Add(tree, "tree", Read(tree, "tree"));
                    foreach (string key in new[] { "BranchMaterial", "FrondMaterial", "LeafMaterial", "3DLeafMaterial" })
                    {
                        if (!t.TryGetValue(key, out string name) || name.Length == 0 || have.Contains(new KeyValuePair<string, string>(name, "material"))) continue;
                        byte[] data = Read(name, "material"); Add(name, "material", data);
                        var mat = new UxBxml(data);
                        for (int i = 0; i < mat.nodes.Count; i++)
                        {
                            string tag = mat.strings[(int)mat.nodes[i][0]];
                            if (tag == "Texture")
                            {
                                if (!StringAttributes(mat, i).TryGetValue("textureName", out string tn)) throw new KeyNotFoundException("textureName");
                                Tex(tn);
                            }
                            else if (tag == "PixelShader" || tag == "VertexShader")
                            {
                                if (StringAttributes(mat, i).TryGetValue("asset", out string shader) && available.ContainsKey(new KeyValuePair<string, string>(shader, "shader")))
                                    Add(shader, "shader", Read(shader, "shader"));
                            }
                        }
                    }
                }
                foreach (string name in textures) Add(name, "texture", Read(name, "texture"));
                assets = list;
            }
        }

        /// write(archive, folder, track, plants, species): the level's reflex-* files into `files`; the replaced species indices.
        public static HashSet<int> Write(string reflexRoot, string track, List<JObj> plants, List<int> plantSpecies, List<UnEsbSpecies> species, Dictionary<string, byte[]> files, CancellationToken ct)
        {
            var selected = PlanOf(track);
            if (selected == null) return new HashSet<int>();
            DonorAssets(reflexRoot, selected.donor, out byte[] forest, out var treeAssets, out var assets, ct);
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < treeAssets.Count; i++) byName[treeAssets[i]] = i;
            var mapping = new Dictionary<int, int>();
            for (int i = 0; i < species.Count; i++)
            {
                if (!selected.species.TryGetValue(UnIni.Lower(species[i].mesh), out string tree)) continue;
                if (!byName.TryGetValue(tree, out int type)) throw new KeyNotFoundException("The Reflex level " + selected.donor + " has no tree " + tree);
                mapping[i] = type;
            }
            if (mapping.Count == 0) throw new UnValueError("Reviewed substitutions did not match " + track);
            var instances = new List<object>();
            for (int i = 0; i < plants.Count; i++)
            {
                if (!mapping.TryGetValue(plantSpecies[i], out int type)) continue;
                var o = new JObj();
                foreach (var kv in plants[i].items) o.Set(kv.Key, kv.Key == "species" ? (object)(long)type : kv.Value);
                instances.Add(o);
            }
            files["reflex-forest.bin"] = forest;
            foreach (var a in assets)
            {
                string name = a.Key.Key;
                if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.Length == 0 || name == "." || name == "..") throw new UnValueError("Reflex resource name: " + name);
                files["reflex-resource-" + name + "." + a.Key.Value] = a.Value;
            }
            files["reflex-vegetation.json"] = UnJson.DumpsBytes(new JObj().Set("version", 1L).Set("donor", selected.donor).Set("instances", instances));
            return new HashSet<int>(mapping.Keys);
        }
    }
}
