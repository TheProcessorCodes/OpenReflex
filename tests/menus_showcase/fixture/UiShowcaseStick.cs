// TEST ONLY: STICK RESPONSE (Options/ReflexStickResponse.cs; OPTIONS > CONTROLS). Page "stick" (a scratch save): the defaults hand the pad's
// value over untouched; with a tune, the value handed over and the GAME's own dead zone + rescale (rx_pad_axis 008156e0: (v - 7864) / 24903
// beyond 7864) give the shaped response: 200 % sensitivity is full at half the travel past the dead zone, a 5 % dead zone answers at 6 %,
// FULL AT 80 % is full at 80 %, the curve bends, nothing passes 100 %, both signs; a real (virtual) pad through OriginalPadConvert; the page's
// rows (stick-options). "[stick-test] PASS|FAIL".
using System;
using System.Collections;
using System.Linq;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.Options;
using Reflex.Port.OriginalWorld;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseStick
    {
        const string Tag = "[stick-test] ";
        static int fails;
        static void Check(bool ok, string what) { Debug.Log(Tag + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { UiShowcase.Register("stick", Page); }

        /// The game's own axis value from the pad's short (loop_input.c rx_pad_axis).
        static float Game(short v) => v > 0x1eb8 ? (v - 0x1eb8) * 4.0155803e-05f : v < -0x1eb8 ? (v + 0x1eb8 + 1f) * 4.0155803e-05f : 0f;
        static float Through(float axis, ReflexStickResponse.Tune t) => Game(OriginalPadConvert.Stick(ReflexStickResponse.Apply(axis, t)));
        static bool Near(float a, float b, float eps = 0.01f) => Mathf.Abs(a - b) <= eps;

        static IEnumerator Page()
        {
            fails = 0;
            var d = ReflexStickResponse.Default;
            Check(ReflexStickResponse.Left.IsDefault && ReflexStickResponse.Right.IsDefault, "a new save: both sticks are the original's");
            bool same = true; for (int i = -100; i <= 100; i++) { float axis = i / 100f; same &= OriginalPadConvert.Stick(ReflexStickResponse.Apply(axis, d)) == OriginalPadConvert.Stick(axis); }   // (the short the game reads)
            Check(same, "the defaults hand the pad's value over untouched");
            float g = ReflexStickResponse.GameDeadZone;
            var s2 = d; s2.sensitivity = 2f;
            Check(Near(Through(g + (1 - g) * 0.5f, s2), 1f) && Near(Through(g + (1 - g) * 0.25f, s2), 0.5f) && Through(0.2f, s2) == 0f, $"SENSITIVITY 200%: full at half the travel past the dead zone ({Through(g + (1 - g) * 0.5f, s2):0.00}), half at a quarter ({Through(g + (1 - g) * 0.25f, s2):0.00}), nothing inside the dead zone");
            var dz = d; dz.deadZone = 0.05f;
            Check(Through(0.04f, dz) == 0f && Through(0.06f, dz) > 0f && Through(0.06f, dz) < 0.03f && Near(Through(1f, dz), 1f) && Near(Through(0.525f, dz), 0.5f), $"DEAD ZONE 5%: nothing at 4%, a little at 6% ({Through(0.06f, dz):0.000}), half at 52.5% ({Through(0.525f, dz):0.00}), full at the end");
            var fu = d; fu.full = 0.8f;
            Check(Near(Through(0.8f, fu), 1f) && Through(0.7f, fu) < 0.9f, $"FULL AT 80%: full at 80% ({Through(0.8f, fu):0.00}), not before ({Through(0.7f, fu):0.00})");
            var q = d; q.curve = 1f; var pr = d; pr.curve = -1f; float mid = g + (1 - g) * 0.5f;
            Check(Through(mid, q) > 0.75f && Through(mid, pr) < 0.15f && Near(Through(1f, q), 1f) && Near(Through(1f, pr), 1f), $"CURVE at half travel: QUICK {Through(mid, q):0.00}, LINEAR {Through(mid, d):0.00}, PRECISE {Through(mid, pr):0.00}; all full at the end");
            var all = new ReflexStickResponse.Tune { deadZone = 0f, full = 0.5f, sensitivity = 3f, curve = 1f };
            bool capped = true, odd = true, rising = true; float last = 0f;
            for (int i = 0; i <= 200; i++) { float a = i / 200f, r = Through(a, all), n = Through(-a, all); capped &= r <= 1.0001f; odd &= Near(r, -n, 0.002f); rising &= r >= last - 1e-4f; last = r; }
            Check(capped && odd && rising, "every setting at its end: never past 100%, the same both ways, never falling as the stick moves out");

            // a real pad through the production conversion
            ReflexStickResponse.Set(true, s2); ReflexStickResponse.Set(false, dz);
            var pad = InputSystem.AddDevice<Gamepad>("stick test pad");
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0.62f, 0f), rightStick = new Vector2(0f, -0.1f) });
            yield return UiShowcase.Frames(4);
            var p = OriginalPadConvert.Convert(pad, 0);
            Check(Near(Game(p.lx), 1f, 0.02f) && Game(p.ly) == 0f && Game(p.ry) < 0f && Game(p.rx) == 0f, $"a pad at 62% left and 10% down on the right: the game steers {Game(p.lx):0.00} (200%) and leans {Game(p.ry):0.000} (dead zone 5%; the original would not move)");
            ReflexStickResponse.Reload();
            Check(Near(ReflexStickResponse.Left.sensitivity, 2f) && Near(ReflexStickResponse.Right.deadZone, 0.05f), "the tune is kept in the save");
            InputSystem.RemoveDevice(pad);

            // ---- share codes
            var cl = new ReflexStickResponse.Tune { sensitivity = 2f, deadZone = 0.05f, full = 0.8f, curve = 0.3f }; var cr = new ReflexStickResponse.Tune { sensitivity = 1.5f, deadZone = ReflexStickResponse.GameDeadZone, full = 1f, curve = -0.5f };
            string numbers = ReflexStickResponse.Numbers(cl, cr), code = ReflexStickResponse.ShareTag + numbers;
            bool back = ReflexStickResponse.TryShareText("try mine " + code + " it rips", out var bl, out var br, out string why);
            Check(numbers == "200.5.80.130.150.24.100.50" && back && Near(bl.sensitivity, 2f) && Near(bl.deadZone, 0.05f) && Near(bl.full, 0.8f) && Near(bl.curve, 0.3f) && Near(br.sensitivity, 1.5f) && br.deadZone == ReflexStickResponse.GameDeadZone && Near(br.curve, -0.5f),
                  "a stick code round trip, found inside other text: " + code + (why != null ? " (" + why + ")" : ""));
            Check(!ReflexStickResponse.TryShareText("nothing here", out _, out _, out why) && why != null && !ReflexStickResponse.TryShareText("RXSTICK1:1.2.3", out _, out _, out _) && !ReflexStickResponse.TryNumbers("1.2.3.4.5.6.7.x", out _, out _), "no code and broken codes are refused");
            Check(ReflexStickResponse.TryNumbers("999.999.0.999.0.0.999.0", out var wl, out var wr) && wl.sensitivity <= ReflexStickResponse.SensitivityMax && wl.deadZone <= ReflexStickResponse.DeadZoneMax && wl.full >= ReflexStickResponse.FullMin && wl.curve <= 1f && wr.sensitivity >= 1f && wr.curve >= -1f, "values out of range are clamped, never taken as they are");
            ReflexStickResponse.Set(cl, cr);
            Check(ReflexStickResponse.ShareCode() == code, "both sticks set at once read back as the same code");
            ReflexStickResponse.Set(d, d);

            var shell = OpenShell.Instance;
            shell.ShowOptions(0); yield return UiShowcase.Frames(60);
            VisualElement Find(string name) => UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None).Select(u => u.rootVisualElement?.Q(name)).FirstOrDefault(e => e != null);   // (the options page has its own document)
            var row = Find("stick-left-sensitivity");
            Check(row != null && Find("stick-right-curve") != null && Find("stick-left-deadzone") != null && Find("stick-right-full") != null, "CONTROLS has the rows for both sticks");
            if (row != null) { row.Focus(); (row.GetFirstAncestorOfType<UnityEngine.UIElements.ScrollView>())?.ScrollTo(Find("stick-right-curve") ?? row); yield return UiShowcase.Frames(30); }
            Check(Find("stick-copy") != null && Find("stick-paste") != null && Find("stick-reset") != null, "CONTROLS has COPY / PASTE STICK CODE and RESET");
            { var e = Find("stick-copy"); if (e != null) { e.GetFirstAncestorOfType<UnityEngine.UIElements.ScrollView>()?.ScrollTo(Find("stick-reset") ?? e); yield return UiShowcase.Frames(20); } }
            yield return UiShowcase.Shot("stick-options");
            ReflexStickResponse.Set(true, d); ReflexStickResponse.Set(false, d);
            shell.ShowMain(); yield return UiShowcase.Frames(20);
            Debug.Log(Tag + (fails == 0 ? "RESULT PASS" : "RESULT FAIL (" + fails + ")"));
        }
    }
}
