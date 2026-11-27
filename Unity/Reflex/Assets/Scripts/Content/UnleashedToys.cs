using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Reflex.Port.Content
{
    // The animated props of an imported MX vs ATV Unleashed level: the source .toy's [AnimatedToys] (Oceanside's ferris wheel, the
    // stadiums' finish arch, windmills, the mill wheel, swinging signs, bells, the tyre swing ...). The .scn does not list them, so
    // the static scenery has none of them. tools/unleashed/toys.py writes each model's rigid parts (one per node) as hidden RXSC
    // groups "UnleashedToy:<model>:<node>" and bakes the prop's motion, sampled with the original's key math, into toys.rxtoys:
    //   'RXTY' v1, toys x { name, model, looping, duration, initial time, samples, parts, part group names,
    //                      samples x parts x (position, rotation) in level space }
    // Here every prop gets its own copies of its part groups; each frame the parts are set to the motion's pose (sample k is at
    // k x duration / (samples - 1); positions blend linearly, rotations by a normalised blend). A looping motion wraps, another
    // stops on its last pose. The clock stops with the game (Time.deltaTime). No collision: the props are drawn only.
    public sealed class UnleashedToys : MonoBehaviour
    {
        sealed class Toy { public string name; public bool looping; public float duration, time; public int samples; public Transform[] parts; public Vector3[] positions; public Quaternion[] rotations; }
        readonly List<Toy> toys = new List<Toy>();
        public int Count => toys.Count;
        public static int LastCount { get; private set; }

        static string Str(BinaryReader r)
        {
            int n = r.ReadUInt16(); if (n > 256 || r.BaseStream.Length - r.BaseStream.Position < n) throw new InvalidDataException("Invalid Unleashed animated prop name");
            return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n));
        }

        internal static void Attach(Transform root, List<UnityEngine.Object> owned)
        {
            LastCount = 0;
            if (UnleashedLevels.Bound == null) return;
            string path = Path.Combine(ExperimentalTerrain.Path, "toys.rxtoys");
            if (!RxFiles.Exists(path)) return;
            using var r = new BinaryReader(new MemoryStream(RxFiles.ReadAllBytes(path), false));
            if (r.BaseStream.Length < 12 || r.ReadInt32() != 0x59545852 || r.ReadInt32() != 1) throw new InvalidDataException("Invalid Unleashed animated props");
            int count = r.ReadInt32(); if (count < 0 || count > 256) throw new InvalidDataException("Invalid Unleashed animated prop count");
            UnleashedToys component = null; int moving = 0;
            for (int i = 0; i < count; i++)
            {
                var toy = new Toy { name = Str(r) }; Str(r);
                toy.looping = r.ReadByte() != 0; toy.duration = r.ReadSingle(); float t0 = r.ReadSingle(); toy.samples = r.ReadInt32(); int parts = r.ReadInt32();
                if (!float.IsFinite(toy.duration) || toy.duration < 0 || !float.IsFinite(t0) || toy.samples < 1 || toy.samples > 100000 || parts < 1 || parts > 256
                    || (toy.samples > 1 && toy.duration <= 0)) throw new InvalidDataException("Invalid Unleashed animated prop " + toy.name);
                var groups = new string[parts]; for (int p = 0; p < parts; p++) groups[p] = Str(r);
                long n = (long)toy.samples * parts;
                if (n * 28 > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("Truncated Unleashed animated prop " + toy.name);
                toy.positions = new Vector3[n]; toy.rotations = new Quaternion[n];
                for (long k = 0; k < n; k++)
                {
                    toy.positions[k] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    var q = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    float len = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                    if (!float.IsFinite(toy.positions[k].x + toy.positions[k].y + toy.positions[k].z) || !float.IsFinite(len) || len < .5f || len > 2f)
                        throw new InvalidDataException("Invalid Unleashed animated prop pose " + toy.name);
                    toy.rotations[k] = new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len);
                }
                // its own copies of the model's parts (the hidden groups stay as the templates: several props share a model)
                var holder = new GameObject("Animated prop: " + toy.name); holder.transform.SetParent(root, false);
                toy.parts = new Transform[parts];
                for (int p = 0; p < parts; p++)
                {
                    var template = root.Find(groups[p]); if (!template) throw new InvalidDataException("Missing animated prop part " + groups[p]);
                    var go = Instantiate(template.gameObject, holder.transform); go.name = groups[p]; go.SetActive(true); toy.parts[p] = go.transform;
                }
                toy.time = toy.looping && toy.duration > 0 ? Mathf.Repeat(t0, toy.duration) : Mathf.Clamp(t0, 0, toy.duration);
                if (!component) component = root.gameObject.AddComponent<UnleashedToys>();
                component.toys.Add(toy); component.Pose(toy); if (toy.samples > 1) moving++;
            }
            if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Invalid Unleashed animated props end");
            LastCount = count;
            if (count > 0) Debug.Log($"[Unleashed] animated props: {count} placed, {moving} in motion");
        }

        void Update()
        {
            float dt = Time.deltaTime; if (dt <= 0) return;
            foreach (var toy in toys)
            {
                if (toy.samples < 2) continue;
                if (toy.looping) toy.time = Mathf.Repeat(toy.time + dt, toy.duration);
                else { if (toy.time >= toy.duration) continue; toy.time = Mathf.Min(toy.time + dt, toy.duration); }
                Pose(toy);
            }
        }

        void Pose(Toy toy)
        {
            int parts = toy.parts.Length, a = 0, b = 0; float u = 0;
            if (toy.samples > 1)
            {
                float f = Mathf.Clamp(toy.time / toy.duration, 0, 1) * (toy.samples - 1);
                a = Mathf.Min((int)f, toy.samples - 2); b = a + 1; u = f - a;
            }
            for (int p = 0; p < parts; p++)
            {
                var t = toy.parts[p]; if (!t) continue;
                int i = a * parts + p, j = b * parts + p;
                t.localPosition = Vector3.LerpUnclamped(toy.positions[i], toy.positions[j], u);
                t.localRotation = Quaternion.Lerp(toy.rotations[i], toy.rotations[j], u);
            }
        }
    }
}
