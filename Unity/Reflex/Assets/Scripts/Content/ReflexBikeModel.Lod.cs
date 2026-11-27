// Distance LODs of a bike (the other riders of a race: OriginalOpponents, OnlineRide). The original models carry further LOD records
// (Rfx.Model.lods: count, first binding, distance in feet; the stock bikes, tyres and part models: 0 / 40 / 120 ft, about the same
// bindings with 1/8 and 1/40 of LOD 0's vertices). ReflexBikeModel.Build(lods: true) keeps, per model of the bike (the chassis, each
// tyre, each part model), what builds a further LOD's parts; SelectLods builds a set the first time its distance is reached and shows
// it instead of the model's current one (Renderer.enabled).
//   materials  LOD 0's own, by binding name: the mud, the number plates and the run-time constants written to them reach every LOD
//   pose       a further LOD's part is a child (identity) of the LOD 0 part on the same hierarchy node: OriginalBikePose places a part by
//              its node only (live world * inverse bind), so the LOD 0 part's pose is the LOD part's, and the pose list, the replay's
//              recorded transforms and the damage panels' flags (the LOD 0 part's object switched off hides its children) stay as built.
//              No part is posed twice: the LOD parts have no pose of their own. (Every further LOD of the install's 336 vehicle models
//              uses nodes its LOD 0 uses; one that does not is not built, and the model keeps the LODs before it.)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Reflex.Port.Content
{
    public sealed partial class ReflexBikeModel
    {
        sealed class LodModel
        {
            public string name; public int[] distances;        // each LOD's distance (feet), [0]: 0
            public int first, count;                           // LOD 0: parts[first .. first + count)
            public Action<int, Transform> build;               // makes a further LOD's parts (AddPart -> lodSink)
            public Renderer[][] sets; public int current;
        }
        readonly List<LodModel> lodModels = new List<LodModel>();
        List<Renderer> lodSink; int lodMaterials; Func<bool> LodNewMaterials;
        /// The bike has models with further LODs (Build(lods: true)).
        public bool HasLods => lodModels.Count > 0;
        /// Diagnostics: each model's LOD now drawn ("MX_MarsF_Lite 1, OBJ_FRT_Tire_THQ 1 ..."), the parts made for further LODs.
        public string LodState => string.Join(", ", lodModels.Select(m => m.name + " " + m.current));
        public int LodPartsBuilt { get; private set; }
        public int MaxLod { get { int k = 0; foreach (var m in lodModels) if (m.current > k) k = m.current; return k; } }

        void AddLodModel(string name, (int count, int first, int distance)[] lods, int first, int count, Action<int, Transform> build)
        {
            if (lods == null || lods.Length < 2 || count <= 0) return;
            var m = new LodModel { name = name, distances = lods.Select(l => l.distance).ToArray(), first = first, count = count, build = build, sets = new Renderer[lods.Length][] };
            m.sets[0] = new Renderer[count];
            for (int i = 0; i < count; i++) m.sets[0][i] = parts[first + i].part.GetComponent<Renderer>();
            lodModels.Add(m);
        }

        /// The bike is a high-res twin whose further LODs are the race models' (Build(highRes: true, lods: true); BIKE DETAIL HIGH: ALL RIDERS).
        public bool highResLods;
        /// Diagnostics: the materials a twin's far LODs took from the race packages (no namesake among the twin's).
        public Func<int> FarOwnMaterials;

        // AddPart for a further LOD: the part under the LOD 0 part of the same pose node and bind (its pose). A twin's race LODs
        // (highResLods) also hang under a part of the same node with another bind, or under the node's empty carrier (Build): the
        // carrier is at live * inverse(its bind), so the part's place under it is its bind * inverse(the part's own bind).
        Transform AddLodPart(GameObject go, Renderer r, int node, Matrix4x4 bind)
        {
            Transform carrier = null; Matrix4x4 carrierBind = bind; bool exact = false;
            for (int i = 0; i < parts.Count && !carrier; i++)
                if (parts[i].node == node && parts[i].part && Same(binds[i], bind)) { carrier = parts[i].part; exact = true; }
            if (!carrier && highResLods)
            {
                for (int i = 0; i < parts.Count && !carrier; i++)
                    if (parts[i].node == node && parts[i].part) { carrier = parts[i].part; carrierBind = binds[i]; }
                for (int i = 0; i < bones.Count && !carrier; i++)
                    if (bones[i].node == node && bones[i].bone) { carrier = bones[i].bone; carrierBind = bones[i].bind; }
            }
            if (!carrier) { UnityEngine.Object.Destroy(go.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.Destroy(go); throw new InvalidDataException("no LOD 0 part on node " + (node < hierarchyNodes.Length ? hierarchyNodes[node] : node.ToString())); }
            go.transform.SetParent(carrier, false);
            if (!exact && !Same(carrierBind, bind))
            {
                var local = carrierBind * bind.inverse;   // (rigid: the node's bind worlds)
                go.transform.localPosition = local.GetColumn(3); go.transform.localRotation = local.rotation;
            }
            lodSink.Add(r);
            return go.transform;
        }
        static bool Same(Matrix4x4 a, Matrix4x4 b) { for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-5f) return false; return true; }

        /// Each model's LOD for a camera at `distance` (feet): the last record whose distance times `scale` is reached; a record already
        /// in use is kept until the camera is `hysteresis` (a share) nearer than its distance. distance 0: LOD 0. True: something changed.
        public bool SelectLods(float distance, float scale, float hysteresis)
        {
            bool changed = false;
            foreach (var m in lodModels)
            {
                int target = 0;
                for (int k = 1; k < m.distances.Length; k++)
                    if (m.distances[k] > 0 && distance >= m.distances[k] * scale * (k <= m.current ? 1f - hysteresis : 1f)) target = k;
                if (target != m.current && Switch(m, target)) changed = true;
            }
            return changed;
        }

        bool Switch(LodModel m, int target)
        {
            if (!chassis) return false;
            if (m.sets[target] == null)
            {
                var sink = new List<Renderer>(); lodSink = sink;
                try { m.build(target, null); }
                catch (Exception e) when (e is InvalidDataException || e is IOException || e is ArgumentException || e is IndexOutOfRangeException)
                {
                    // (a LOD record that does not build: the model keeps the LODs before it)
                    Debug.LogWarning($"Reflex bike LOD: {m.name} LOD {target}: {e.Message}");
                    foreach (var r in sink) if (r) { if (r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh) UnityEngine.Object.Destroy(f.sharedMesh); UnityEngine.Object.Destroy(r.gameObject); }
                    m.distances = m.distances.Take(target).ToArray();
                    return false;
                }
                finally { lodSink = null; }
                m.sets[target] = sink.ToArray(); LodPartsBuilt += sink.Count;
                // a material only a further LOD names (an ATV's ATV_Grips): the mud record's materials and the dirt's (MUD BUILDUP) are collected again
                if (LodNewMaterials != null && LodNewMaterials())
                    foreach (var g in new[] { chassis, frontWheel ? frontWheel.gameObject : null, rearWheel ? rearWheel.gameObject : null })
                    {
                        if (g && g.TryGetComponent<ReflexMudMaterials>(out var mud)) mud.Recollect();
                        ReflexDirt.Recollect(g);
                        if (g == chassis) ReflexPlates.Rebind(g);   // (the rider's number on a material only a further LOD has)
                    }
            }
            foreach (var r in m.sets[m.current]) if (r) r.enabled = false;
            foreach (var r in m.sets[target]) if (r) r.enabled = true;
            m.current = target;
            return true;
        }
    }
}
