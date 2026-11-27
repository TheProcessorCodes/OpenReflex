// STICK RESPONSE (OPTIONS > CONTROLS, 2026-11-16): the pad's sticks before the game reads them. A key is 0 or 100 % at once and a stick has to
// travel, through the game's own dead zone (7864 / 32767 = 24 % per axis, rescaled beyond it: rx_pad_axis 008156e0); players already close that
// gap with controller software that turns a stick into key presses. Per stick (LEFT / RIGHT, as held: STICK LAYOUT decides what each does):
//   DEAD ZONE    how far the stick moves before anything happens (the game's 24 % by default; 0 .. 50 %)
//   FULL AT      the travel that already gives 100 % (100 % by default; a worn stick that never reaches its end, or a shorter throw)
//   SENSITIVITY  the response multiplied, clamped at 100 % (100 .. 300 %: at 200 % half of the remaining travel is full lean)
//   CURVE        the response bent: PRECISE (fine control near the centre) .. QUICK (most of the response early); LINEAR by default
// The stick's value is shaped per axis, as the game's dead zone is, and handed over so that the game's own dead zone and rescale give exactly
// the shaped response: never more than 100 %, and with the defaults the value is not touched at all (bit for bit the pad's). Drawing-side only:
// the core reads an ordinary pad (OriginalPadConvert), so it works everywhere a pad does.
using UnityEngine;

namespace Reflex.Port.Options
{
    public static class ReflexStickResponse
    {
        /// The game's own stick dead zone, per axis (0x1eb8 of 32767).
        public const float GameDeadZone = 7864f / 32767f;
        public const float DeadZoneMax = 0.5f, FullMin = 0.5f, SensitivityMax = 3f;

        public struct Tune
        {
            public float deadZone, full, sensitivity, curve;
            public bool IsDefault => Mathf.Abs(deadZone - GameDeadZone) < 0.004f && full >= 0.999f && Mathf.Abs(sensitivity - 1f) < 0.004f && Mathf.Abs(curve) < 0.004f;
        }
        public static readonly Tune Default = new Tune { deadZone = GameDeadZone, full = 1f, sensitivity = 1f, curve = 0f };

        static Tune s_left = Default, s_right = Default; static bool s_loaded;
        public static Tune Left { get { Load(); return s_left; } }
        public static Tune Right { get { Load(); return s_right; } }
        static void Load() { if (!s_loaded) Reload(); }
        /// The settings changed (or were loaded): what the pad's conversion uses from the next frame.
        public static void Reload()
        {
            s_left = Read("Left"); s_right = Read("Right"); s_loaded = true;
        }
        static string Key(string stick, string what) => "Input." + stick + "Stick." + what;
        static Tune Read(string stick) => new Tune
        {
            deadZone = Mathf.Clamp(ReflexSave.GetFloat(Key(stick, "DeadZone"), GameDeadZone), 0f, DeadZoneMax),
            full = Mathf.Clamp(ReflexSave.GetFloat(Key(stick, "Full"), 1f), FullMin, 1f),
            sensitivity = Mathf.Clamp(ReflexSave.GetFloat(Key(stick, "Sensitivity"), 1f), 1f, SensitivityMax),
            curve = Mathf.Clamp(ReflexSave.GetFloat(Key(stick, "Curve"), 0f), -1f, 1f),
        };
        /// Saves one stick's tune (left: the left stick).
        public static void Set(bool left, Tune t) { SetOne(left, t); ReflexSave.Save(); Reload(); Changed?.Invoke(); }
        static void SetOne(bool left, Tune t)
        {
            string stick = left ? "Left" : "Right";
            ReflexSave.SetFloat(Key(stick, "DeadZone"), Mathf.Clamp(t.deadZone, 0f, DeadZoneMax));
            ReflexSave.SetFloat(Key(stick, "Full"), Mathf.Clamp(t.full, FullMin, 1f));
            ReflexSave.SetFloat(Key(stick, "Sensitivity"), Mathf.Clamp(t.sensitivity, 1f, SensitivityMax));
            ReflexSave.SetFloat(Key(stick, "Curve"), Mathf.Clamp(t.curve, -1f, 1f));
        }

        /// The settings changed through Set (the online look carries them: Online/OnlineService.cs).
        public static event System.Action Changed;

        // ---- sharing: eight whole numbers, the left stick's then the right's: SENSITIVITY %, DEAD ZONE %, FULL AT %, CURVE + 100 (0 PRECISE
        // 100% .. 100 LINEAR .. 200 QUICK 100%). As a share code: RXSTICK1:200.24.100.100.100.5.100.130 (found anywhere in pasted text).
        public const string ShareTag = "RXSTICK1:";
        static int Pct(float v) => Mathf.RoundToInt(v * 100f);
        static string Part(in Tune t) => Pct(t.sensitivity) + "." + Pct(t.deadZone) + "." + Pct(t.full) + "." + (Pct(t.curve) + 100);
        /// The two sticks as the eight numbers (what a rider's online look carries).
        public static string Numbers(in Tune left, in Tune right) => Part(left) + "." + Part(right);
        public static string Numbers() => Numbers(Left, Right);
        public static string ShareCode() => ShareTag + Numbers();
        /// The two sticks of eight numbers ("200.24.100.100.100.5.100.130"); false when they are not that. Every value is clamped to its range.
        public static bool TryNumbers(string numbers, out Tune left, out Tune right)
        {
            left = right = Default;
            var f = (numbers ?? "").Split('.');
            if (f.Length != 8) return false;
            var v = new int[8];
            for (int i = 0; i < 8; i++) if (f[i].Length == 0 || f[i].Length > 3 || !int.TryParse(f[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return false;
            Tune One(int o) => new Tune
            {
                sensitivity = Mathf.Clamp(v[o] / 100f, 1f, SensitivityMax),
                deadZone = v[o + 1] == 24 ? GameDeadZone : Mathf.Clamp(v[o + 1] / 100f, 0f, DeadZoneMax),   // (24: the game's own, exactly)
                full = Mathf.Clamp(v[o + 2] / 100f, FullMin, 1f),
                curve = Mathf.Clamp((v[o + 3] - 100) / 100f, -1f, 1f),
            };
            left = One(0); right = One(4);
            return true;
        }
        /// A share code anywhere in text (or the bare numbers); false with why.
        public static bool TryShareText(string text, out Tune left, out Tune right, out string error)
        {
            left = right = Default; error = null;
            string t = text ?? ""; int at = t.IndexOf(ShareTag, System.StringComparison.OrdinalIgnoreCase);
            string rest = at >= 0 ? t.Substring(at + ShareTag.Length) : t.Trim();
            int end = 0; while (end < rest.Length && (char.IsDigit(rest[end]) || rest[end] == '.')) end++;
            if (end == 0 || !TryNumbers(rest.Substring(0, end).TrimEnd('.'), out left, out right)) { error = at >= 0 ? "the stick code is broken" : "no stick code on the clipboard (" + ShareTag + "...)"; return false; }
            return true;
        }
        /// "LEFT 200% · DEAD ZONE 24% · FULL AT 100% · LINEAR" for one stick.
        public static string Describe(in Tune t) =>
            Pct(t.sensitivity) + "%  ·  DEAD ZONE " + Pct(t.deadZone) + "%  ·  FULL AT " + Pct(t.full) + "%  ·  " + (Mathf.Abs(t.curve) < 0.05f ? "LINEAR" : (t.curve > 0 ? "QUICK " : "PRECISE ") + Pct(Mathf.Abs(t.curve)) + "%");
        public static string Describe(in Tune left, in Tune right) => "LEFT " + Describe(left) + "   RIGHT " + Describe(right);
        /// Both sticks at once (a pasted code, another rider's config).
        public static void Set(in Tune left, in Tune right) { SetOne(true, left); SetOne(false, right); ReflexSave.Save(); Reload(); Changed?.Invoke(); }

        /// The response the game ends up with (0 .. 1) for an axis deflection of 0 .. 1.
        public static float Response(float deflection, in Tune t)
        {
            float m = Mathf.Abs(deflection);
            if (m <= t.deadZone) return 0f;
            float x = Mathf.Clamp01((m - t.deadZone) / Mathf.Max(0.02f, t.full - t.deadZone));
            if (t.curve != 0f) x = Mathf.Pow(x, Mathf.Pow(3f, -t.curve));   // (+1 QUICK: x^(1/3); -1 PRECISE: x^3)
            return Mathf.Min(1f, x * t.sensitivity);
        }

        /// One axis of a stick (-1 .. 1, as the pad reports it) as it is handed to the game: its own dead zone and rescale then give Response.
        public static float Apply(float axis, in Tune t)
        {
            if (t.IsDefault) return axis;   // (the pad's own value, untouched)
            float x = Response(axis, t);
            if (x <= 0f) return 0f;
            return Mathf.Sign(axis) * Mathf.Min(1f, GameDeadZone + x * (1f - GameDeadZone) + 1e-4f);   // (just past the dead zone at the smallest response)
        }
    }
}
