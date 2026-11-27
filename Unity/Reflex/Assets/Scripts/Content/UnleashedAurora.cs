using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reflex.Port.Content
{
    // The northern lights over an imported MX vs ATV Unleashed level whose source has a camera-following sky dome (retail: Nat16
    // "Northern Lights", SLT NorthernLights: two scrolling aurora textures added over the sky). OPEN REFLEX draws its own instead:
    // a procedural dome (Resources/ReflexAurora.shader) around the camera, behind the world. The source dome stays in the package
    // and is hidden; REFLEX_AURORA=source shows it (the original's look), REFLEX_AURORA=off neither.
    public sealed class UnleashedAurora : MonoBehaviour
    {
        Material material; Mesh mesh;

        internal static void Attach(Transform root, List<Transform> sourceDomes, List<UnityEngine.Object> owned)
        {
            if (UnleashedLevels.Bound == null || sourceDomes == null || sourceDomes.Count == 0) return;
            string mode = Environment.GetEnvironmentVariable("REFLEX_AURORA") ?? "";
            if (mode == "source") return;
            foreach (var dome in sourceDomes) if (dome) dome.gameObject.SetActive(false);
            if (mode == "off") return;
            var shader = UnityEngine.Resources.Load<Shader>("ReflexAurora");
            if (!shader) { Debug.LogWarning("[Unleashed] aurora shader missing"); return; }
            var go = new GameObject("Northern lights (OPEN REFLEX)"); go.transform.SetParent(root, false);
            var a = go.AddComponent<UnleashedAurora>();
            a.mesh = Dome(); a.material = new Material(shader) { name = "Aurora" };
            owned.Add(a.mesh); owned.Add(a.material);
            if (float.TryParse(Environment.GetEnvironmentVariable("REFLEX_AURORA_INTENSITY"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float k)) a.material.SetFloat("_Intensity", k);
            // tuning: REFLEX_AURORA_SHAPE=width,lean,fold,rays  REFLEX_AURORA_DETAIL=ray frequency,fold frequency,ribbon spacing,fine fold
            foreach (var (name, property) in new[] { ("REFLEX_AURORA_SHAPE", "_Shape"), ("REFLEX_AURORA_DETAIL", "_Detail") })
            {
                var parts = (Environment.GetEnvironmentVariable(name) ?? "").Split(','); if (parts.Length != 4) continue;
                var v = Vector4.zero; bool ok = true;
                for (int i = 0; i < 4; i++) { ok &= float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f); v[i] = f; }
                if (ok) a.material.SetVector(property, v);
            }
            go.AddComponent<MeshFilter>().sharedMesh = a.mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = a.material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Debug.Log("[Unleashed] northern lights: the procedural dome (the source's camera-following dome is hidden)");
        }

        /// The upper hemisphere (a little below the horizon), unit radius: the shader places it around the camera at the far plane.
        static Mesh Dome()
        {
            const int Rings = 24, Segments = 48;
            var v = new Vector3[(Rings + 1) * (Segments + 1)]; var t = new int[Rings * Segments * 6]; int n = 0;
            for (int y = 0; y <= Rings; y++)
            {
                float elevation = Mathf.Lerp(-.05f, Mathf.PI / 2, y / (float)Rings), c = Mathf.Cos(elevation), s = Mathf.Sin(elevation);
                for (int x = 0; x <= Segments; x++) { float az = x * 2 * Mathf.PI / Segments; v[y * (Segments + 1) + x] = new Vector3(Mathf.Cos(az) * c, s, Mathf.Sin(az) * c); }
            }
            for (int y = 0; y < Rings; y++) for (int x = 0; x < Segments; x++)
            {
                int a = y * (Segments + 1) + x, b = a + 1, c = a + Segments + 1, d = c + 1;
                t[n++] = a; t[n++] = c; t[n++] = b; t[n++] = b; t[n++] = c; t[n++] = d;
            }
            var m = new Mesh { name = "Aurora dome", vertices = v, triangles = t };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);   // never culled: it sits wherever the camera is
            return m;
        }
    }
}
