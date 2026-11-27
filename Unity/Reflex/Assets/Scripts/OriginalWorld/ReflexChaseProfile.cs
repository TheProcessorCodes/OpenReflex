// ReflexChaseProfile (w-watchghost): what the game's own chase camera looks like, measured live while the player rides, so a camera that is not the
// core's (WATCH GHOST's: Leaderboards/RxGhostWatchCamera.cs, which follows a C#-posed ghost the core knows nothing about; rx_session_camera has no
// target) can stand where the game's would. OriginalWorldRuntime.ApplyGameCamera feeds it the game camera's pose and the bike's every frame the
// game's camera shows (camera mode 0); it learns only while riding settled:
//   when      the race runs (InProgress, or no race state: Free Ride), the intro card is down, the camera chases (within 60 ft of the bike and looking
//             at it, ReflexCameraRig's test), the rider is on (not wrecked), over 15 ft/s, not paused, a third-person BASE view (DrivingCamera 0..2),
//             and all of that for SettleTime in a row (the gate drop's 26 ft catch-up and a wreck's recovery are not the steady camera)
//   what      in the travel's flat heading frame (the horizontal velocity; under 3 ft/s the bike's forward): the camera's offset from the bike (BACK
//             along the heading, UP, SIDE to the right), the height above the bike where the view ray crosses the bike's cross-section (AIM), and the
//             game view's vertical field of view (radians, before CHASE CAMERA FOV)
//   per key   ReflexCameraRig.HoldKey(vehicle type, BASE view): a running mean over the riding time, its weight capped at MaxWeight s (after that an
//             exponential mean of that time constant: a later game change is followed, slowly); used once Known s are in
//   kept      PlayerPrefs (ReflexChase.<key>.<value>; not the settings store: nothing to back up or share, no ReflexSave.Changed), written every
//             SaveEvery s of learning and when a world goes (Flush); read once per key on first use
//   cost      a few float operations a frame, no allocation (the key names are made once, at the first read or write)
// Before anything is learned: the defaults below (ESTIMATES: BACK is ReflexCameraRig's measured steady distance, the rest is eyeballed from the
// game's GAME PLAY camera, not measured).
using System;
using UnityEngine;

namespace Reflex.Port.OriginalWorld
{
    public static class ReflexChaseProfile
    {
        /// One key's camera: BACK / UP / SIDE (ft, in the heading frame), AIM (ft above the bike), FOV (the game view's vertical, radians); Learned:
        /// the seconds of settled riding behind it (0: the defaults).
        public struct Profile { public float back, up, side, aim, fov, learned; }

        const int Keys = 32;   // (ReflexCameraRig.HoldKey's range)
        const float SettleTime = 1.5f, MinSpeed = 15f, NearBike = 60f, MaxWeight = 60f, Known = 2f, SaveEvery = 10f;
        // ESTIMATES (not measured; the learned values replace them after Known s of riding): 4.5 ft up, the aim 2.5 ft over the bike, centred, a 55
        // degree view (the online spectator's chase camera's field of view)
        const float DefaultUp = 4.5f, DefaultAim = 2.5f, DefaultSide = 0f, DefaultFovDeg = 55f, DefaultBackOther = 13.1f;

        static readonly float[] s_back = new float[Keys], s_up = new float[Keys], s_side = new float[Keys], s_aim = new float[Keys], s_fov = new float[Keys], s_weight = new float[Keys];
        static readonly bool[] s_read = new bool[Keys];
        static string[] s_names;   // per key: back, up, side, aim, fov, weight
        static int s_settleKey = -1; static float s_settled, s_unsaved;
        static bool s_dirty;

        static bool Valid(int key) => key >= 0 && key < Keys;

        /// The camera for a key: the learned one once Known s are in, else the defaults (BACK: the rig's measured steady distance).
        public static Profile Get(int key)
        {
            if (!Valid(key)) return Defaults(key);
            Read(key);
            if (s_weight[key] < Known) return Defaults(key);
            return new Profile { back = s_back[key], up = s_up[key], side = s_side[key], aim = s_aim[key], fov = s_fov[key], learned = s_weight[key] };
        }

        /// The defaults (estimates; see the file's header).
        public static Profile Defaults(int key)
        {
            float back = Valid(key) ? ReflexCameraRig.MeasuredHold(key) : 0f;
            return new Profile { back = back > 0f ? back : DefaultBackOther, up = DefaultUp, side = DefaultSide, aim = DefaultAim, fov = DefaultFovDeg * Mathf.Deg2Rad, learned = 0f };
        }

        /// The heading frame's forward: the horizontal velocity over 3 ft/s, else the bike's forward flattened, else fallback (unit, y = 0).
        public static Vector3 Heading(Vector3 velocity, Vector3 bikeForward, Vector3 fallback)
        {
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 3f * 3f) return velocity.normalized;
            bikeForward.y = 0f;
            if (bikeForward.sqrMagnitude > 1e-4f) return bikeForward.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 1e-4f ? fallback.normalized : Vector3.forward;
        }

        /// The right of a flat heading (Vector3.Cross(Vector3.up, heading)).
        public static Vector3 Right(Vector3 heading) => new Vector3(heading.z, 0f, -heading.x);

        /// One frame of the game's camera while it shows (OriginalWorldRuntime.ApplyGameCamera, camera mode 0). settled: the race runs, the intro card is
        /// down, the BASE view is third person; dt: the frame's seconds (0 while paused).
        public static void Observe(int key, Vector3 camPos, Vector3 camForward, float fovRad, Vector3 bike, Vector3 bikeForward, Vector3 velocity, float speed,
                                   bool settled, bool wrecked, float dt)
        {
            if (!Valid(key) || !(dt > 0f) || dt > 0.25f) return;
            Vector3 toBike = bike - camPos;
            bool chasing = toBike.sqrMagnitude < NearBike * NearBike && Vector3.Dot(toBike, camForward) > 0f;
            bool ok = settled && chasing && !wrecked && speed > MinSpeed && fovRad > 0.05f && fovRad < 3f && camForward.sqrMagnitude > 0.5f
                      && !float.IsNaN(camPos.x + camPos.y + camPos.z + bike.x + bike.y + bike.z);
            if (!ok || key != s_settleKey) { s_settled = 0f; s_settleKey = ok ? key : -1; return; }
            s_settled += dt;
            if (s_settled < SettleTime) return;
            Vector3 h = Heading(velocity, bikeForward, camForward), r = Right(h), f = camForward.normalized;
            Vector3 rel = camPos - bike;
            float back = -Vector3.Dot(rel, h), up = rel.y, side = Vector3.Dot(rel, r);
            // the view ray crossing the bike's cross-section (the plane through the bike across the heading): its height over the bike
            float along = Vector3.Dot(f, h);
            if (along < 0.2f) return;
            float s = Vector3.Dot(bike - camPos, h) / along;
            float aim = camPos.y + f.y * s - bike.y;
            if (back < 3f || back > 60f || up < -5f || up > 30f || Mathf.Abs(side) > 15f || aim < -5f || aim > 15f) return;   // (not a chase camera's place)
            Read(key);
            float w = Mathf.Min(s_weight[key] + dt, MaxWeight), k = dt / w;
            if (s_weight[key] <= 0f) k = 1f;
            s_back[key] += (back - s_back[key]) * k; s_up[key] += (up - s_up[key]) * k; s_side[key] += (side - s_side[key]) * k;
            s_aim[key] += (aim - s_aim[key]) * k; s_fov[key] += (fovRad - s_fov[key]) * k;
            s_weight[key] = w; s_dirty = true;
            s_unsaved += dt;
            if (s_unsaved >= SaveEvery) Flush();
        }

        /// Writes what was learned since the last write (every SaveEvery s of learning; OriginalWorldRuntime.OnDestroy). Main thread.
        public static void Flush()
        {
            s_unsaved = 0f;
            if (!s_dirty) return;
            s_dirty = false;
            try
            {
                Names();
                for (int key = 0; key < Keys; key++)
                {
                    if (!s_read[key] || s_weight[key] <= 0f) continue;
                    int o = key * 6;
                    PlayerPrefs.SetFloat(s_names[o], s_back[key]); PlayerPrefs.SetFloat(s_names[o + 1], s_up[key]); PlayerPrefs.SetFloat(s_names[o + 2], s_side[key]);
                    PlayerPrefs.SetFloat(s_names[o + 3], s_aim[key]); PlayerPrefs.SetFloat(s_names[o + 4], s_fov[key]); PlayerPrefs.SetFloat(s_names[o + 5], s_weight[key]);
                }
            }
            catch (Exception e) { Debug.LogWarning("[camera] the chase camera profile was not saved: " + e.Message); }
        }

        static void Read(int key)
        {
            if (s_read[key]) return;
            s_read[key] = true;
            try
            {
                Names();
                int o = key * 6;
                float w = PlayerPrefs.GetFloat(s_names[o + 5], 0f);
                if (!(w > 0f) || float.IsNaN(w)) return;
                s_back[key] = PlayerPrefs.GetFloat(s_names[o], 0f); s_up[key] = PlayerPrefs.GetFloat(s_names[o + 1], 0f); s_side[key] = PlayerPrefs.GetFloat(s_names[o + 2], 0f);
                s_aim[key] = PlayerPrefs.GetFloat(s_names[o + 3], 0f); s_fov[key] = PlayerPrefs.GetFloat(s_names[o + 4], 0f);
                s_weight[key] = Mathf.Min(w, MaxWeight);
                if (float.IsNaN(s_back[key] + s_up[key] + s_side[key] + s_aim[key] + s_fov[key])) s_weight[key] = 0f;   // (a broken entry: learned again)
            }
            catch (Exception e) { Debug.LogWarning("[camera] the chase camera profile was not read: " + e.Message); }
        }

        static void Names()
        {
            if (s_names != null) return;
            var n = new string[Keys * 6];
            string[] v = { "back", "up", "side", "aim", "fov", "weight" };
            for (int key = 0; key < Keys; key++)
                for (int i = 0; i < 6; i++) n[key * 6 + i] = "ReflexChase." + key.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + v[i];
            s_names = n;
        }
    }
}
