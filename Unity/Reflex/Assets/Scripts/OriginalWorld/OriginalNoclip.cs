// NOCLIP: a free-flying camera in a Free Ride or PRACTICE. L3 + R3 (both stick clicks; on the keyboard Left Ctrl + Backspace) starts it: the
// camera flies from where it is. The same chord again ends it and puts the bike exactly where the camera is, facing the camera's heading,
// to fall from there (core rx_session_spawn_at_air: the game's own instant reset, as RIDE TO SPAWN MARKER, so upright, velocity gone, the
// wreck and trick state cleared, then the vehicle left at the spot instead of on the ground under it; an older core: on the ground).
//   where   where the core's instant reset works (OriginalSpawnMarker.Unavailable): Free Ride (not a challenge, the test ride, the tutorial,
//           the career; not on the hovercraft) and PRACTICE. Not over
//           a menu, the pause page, the intro card or the replay viewer.
//   practice the lap in progress does not count: placing the bike is a marker respawn for the lap rules (core rx_lap_log_marker: MARKER in
//           the lap list, the timing restarts from the finish line), as RIDE TO SPAWN MARKER.
//   while   the world holds (OriginalWorldRuntime.paused + ViewerActive, as REPLAY / PHOTO MODE holds it: no game frames) and the
//           sound is off; the bike waits where it was left. The HUD is off.
//   flying  pad: left stick moves along the view (forward / back, strafe), right stick looks, RT rises, LT sinks (world up / down), A held
//           is fast (x6). Keyboard and mouse: W A S D move, Space / E rise, C / Q sink, Shift fast (x6), Alt slow (x0.15), the mouse looks.
//           25 m/s base (in the world's feet), sticks' dead zone 0.15, look 2.4 rad/s on the stick and 0.0022 rad a mouse count, the pitch
//           held inside +-1.55 rad, no roll.
//   chord   in the editor and development builds the same chord opens URP's Rendering Debugger; in a ride where noclip works its runtime
//           window is switched off (DebugManager.enableRuntimeUI) so the chord is noclip's alone, and switched on again outside it once the
//           chord is let go (an action enabled over a held chord does not fire). A release build has no Rendering Debugger.
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Reflex.Port.OriginalWorld
{
    [DefaultExecutionOrder(29500)]   // after the runtime's camera pass and the replay viewer (29000): the flying camera is the last word
    public sealed class OriginalNoclip : MonoBehaviour
    {
        public const float BaseSpeed = 25f / 0.3048f;   // 25 m/s in the world's feet
        public const float FastScale = 6f, SlowScale = 0.15f, StickDeadZone = 0.15f, StickLook = 2.4f, MouseLook = 0.0022f, PitchLimit = 1.55f;
        public static OriginalNoclip Instance { get; private set; }
        /// Flying now.
        public static bool Active => Instance && Instance.active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var go = new GameObject("Noclip") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<OriginalNoclip>();
        }

        OriginalWorldRuntime world; long worldGeneration;
        bool active, held, pausedBefore, audioBefore, hudHiddenBefore, practice;
        Vector3 camPosBefore; Quaternion camRotBefore;
        float yaw, pitch;
        volatile int pendingResult = int.MinValue;
        Vector3 placedAt;
        SpawnMarkerNotice notice;

        void OnDestroy()
        {
            if (Instance != this) return;
            if (active) Stop(false);
            Debugger(false, true);
            Instance = null;
        }

        // ------------------------------------------------------------------------------------------------ availability
        /// This ride is one noclip is for (the transient menus aside): a Free Ride (offline or a session's) or PRACTICE.
        public static bool RideHere(OriginalWorldRuntime rt)
        {
            if (!rt || !rt.Running) return false;
            string why = OriginalSpawnMarker.Unavailable(rt);
            return why.Length == 0 || why == "NOT ON THE HOVERCRAFT";
        }
        /// Why noclip cannot start now ("" = it can; null = silently not: not a ride noclip is for, or a menu is up).
        public static string Unavailable(OriginalWorldRuntime rt)
        {
            if (!RideHere(rt)) return null;
            if (rt.paused || rt.OwnMenuActive || rt.IntroCardShown || rt.pauseMenu && rt.pauseMenu.IsOpen || rt.optionsMenu && rt.optionsMenu.IsOpen || ReflexOptions.IsOpen
                || Reflex.Port.Replay.ReflexReplay.ViewerOpen) return null;
            if (rt.hovercraft && rt.hovercraft.Driving) return "NOCLIP: NOT ON THE HOVERCRAFT";
            if (!rt.chaseCamera) return "NOCLIP: NO CAMERA";
            if (rt.SimDriver?.Open == null) return "NOCLIP NEEDS THE OPEN REFLEX CORE";
            return "";
        }

        // ------------------------------------------------------------------------------------------------ per frame
        void Update()
        {
            int result = pendingResult;
            if (result != int.MinValue) { pendingResult = int.MinValue; Answer(result); }

            var rt = ReflexClientLive.Runtime;
            if (active && (!rt || rt != world || rt.WorldGeneration != worldGeneration)) Stop(false);   // the world went away under the flight

            var kb = Keyboard.current; var g = Pad(rt);
            bool chordHeld = g != null && (g.leftStickButton.isPressed || g.rightStickButton.isPressed) || kb != null && (kb.leftCtrlKey.isPressed || kb.backspaceKey.isPressed);
            bool chord = g != null && g.leftStickButton.isPressed && g.rightStickButton.isPressed && (g.leftStickButton.wasPressedThisFrame || g.rightStickButton.wasPressedThisFrame)
                      || kb != null && kb.leftCtrlKey.isPressed && kb.backspaceKey.isPressed && (kb.leftCtrlKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame);
            Debugger(active || RideHere(rt), !chordHeld);

            if (!chord) return;
            if (active) { Stop(true); return; }
            string why = Unavailable(rt);
            if (why == null) return;
            if (why.Length > 0) Toast(why, "");
            else Begin(rt);
        }

        /// The Rendering Debugger (editor and development builds) gives its L3 + R3 to noclip where noclip works; back once the chord is let go.
        void Debugger(bool ours, bool released)
        {
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
            var dm = UnityEngine.Rendering.DebugManager.instance;
            if (ours && !held) { held = true; dm.displayRuntimeUI = false; dm.enableRuntimeUI = false; }
            else if (!ours && held && released) { held = false; dm.enableRuntimeUI = true; }
#endif
        }

        static Gamepad Pad(OriginalWorldRuntime rt) => (rt ? rt.PadRouting.Pad(0) : null) ?? Gamepad.current;

        void LateUpdate()
        {
            if (!active || !world || !world.chaseCamera) return;
            Fly(world.chaseCamera.transform, Pad(world), Keyboard.current, Mouse.current, Time.unscaledDeltaTime);
        }

        void Fly(Transform cam, Gamepad g, Keyboard kb, Mouse mouse, float dt)
        {
            dt = Mathf.Min(dt, 0.1f);   // a hitch is not a teleport
            // look
            Vector2 turn = Vector2.zero;
            if (g != null)
            {
                var r = g.rightStick.ReadValue();
                if (r.magnitude > StickDeadZone) turn += r * (StickLook * dt);
            }
            if (mouse != null && Application.isFocused) turn += mouse.delta.ReadValue() * MouseLook;
            yaw += turn.x;
            pitch = Mathf.Clamp(pitch - turn.y, -PitchLimit, PitchLimit);
            cam.rotation = Quaternion.Euler(pitch * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, 0f);

            // move: x right, y world up, z forward along the view
            Vector3 wish = Vector3.zero;
            bool fast = false, slow = false;
            if (kb != null)
            {
                wish.x += Axis(kb.aKey, kb.dKey); wish.z += Axis(kb.sKey, kb.wKey);
                wish.y += Axis(kb.cKey, kb.spaceKey) + Axis(kb.qKey, kb.eKey);
                fast |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                slow |= kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
            }
            if (g != null)
            {
                var l = g.leftStick.ReadValue();
                if (l.magnitude > StickDeadZone) { wish.x += l.x; wish.z += l.y; }
                wish.y += g.rightTrigger.ReadValue() - g.leftTrigger.ReadValue();
                fast |= g.buttonSouth.isPressed;
            }
            if (wish == Vector3.zero) return;
            wish = Vector3.ClampMagnitude(wish, 1f);
            float speed = BaseSpeed * (fast ? FastScale : slow ? SlowScale : 1f);
            cam.position += (cam.right * wish.x + Vector3.up * wish.y + cam.forward * wish.z) * (speed * dt);
        }
        static float Axis(KeyControl neg, KeyControl pos) => (pos.isPressed ? 1f : 0f) - (neg.isPressed ? 1f : 0f);

        // ------------------------------------------------------------------------------------------------ start / stop
        void Begin(OriginalWorldRuntime rt)
        {
            world = rt; worldGeneration = rt.WorldGeneration;
            practice = rt.IsPractice;
            var cam = rt.chaseCamera.transform;
            camPosBefore = cam.position; camRotBefore = cam.rotation;
            var f = cam.forward;
            yaw = Mathf.Atan2(f.x, f.z);
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)), -PitchLimit, PitchLimit);
            pausedBefore = rt.paused; audioBefore = AudioListener.pause; hudHiddenBefore = ReflexSettings.HudHidden;
            rt.GetComponent<OriginalOnboardAdjust>()?.End();
            if (rt.onboard) rt.onboard.Deactivate();
            rt.paused = true; AudioListener.pause = true;
            rt.ClearMenuInput();
            rt.ViewerActive = true;
            ReflexSettings.HudHidden = true;
            active = true;
            Debug.Log($"[Noclip] on at ({camPosBefore.x:0.0}, {camPosBefore.y:0.0}, {camPosBefore.z:0.0}){(practice ? ", PRACTICE" : "")}");
        }

        /// Ends the flight; place: the bike goes where the camera is (else the world just carries on, the camera back where it was).
        void Stop(bool place)
        {
            if (!active) return;
            active = false;
            var rt = world; world = null;
            AudioListener.pause = audioBefore;
            ReflexSettings.HudHidden = hudHiddenBefore;
            if (!rt) return;
            var cam = rt.chaseCamera ? rt.chaseCamera.transform : null;
            Vector3 pos = cam ? cam.position : camPosBefore;
            Vector3 f = cam ? cam.forward : Vector3.forward;
            Vector3 up = cam ? cam.up : Vector3.up;
            if (cam) { cam.position = camPosBefore; cam.rotation = camRotBefore; }   // the game's camera takes over from its own place
            rt.ViewerActive = false;
            rt.paused = pausedBefore; rt.ClearMenuInput();
            if (!place) { Debug.Log("[Noclip] off"); return; }

            var dir = new Vector2(f.x, f.z);
            if (dir.sqrMagnitude < 1e-4f) dir = new Vector2(up.x, up.z);   // looking straight down / up: the screen's top is ahead
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
            dir.Normalize();
            var open = rt.SimDriver?.Open;
            if (open == null) { Toast("NOCLIP NEEDS THE OPEN REFLEX CORE", ""); return; }
            pendingResult = int.MinValue;
            placedAt = pos;
            open.SpawnAt(pos.x, pos.y, pos.z, dir.x, dir.y, rc => pendingResult = rc, instant: true, air: true);
        }

        void Answer(int rc)
        {
            if (rc == 0)
            {
                Debug.Log($"[Noclip] off, the bike placed at ({placedAt.x:0.0}, {placedAt.y:0.0}, {placedAt.z:0.0})");
                if (practice)
                {   // the lap is the marker's (core rx_lap_log_marker): its list row and the timing from the line, as RIDE TO SPAWN MARKER
                    Reflex.Port.Practice.RxPractice.MarkerUsed();
                    Toast("LAP NOT COUNTED  ·  NOCLIP", "TIMING STARTS AGAIN AT THE FINISH LINE");
                }
                return;
            }
            Debug.Log($"[Noclip] off, the bike could not be placed (rc {rc})");
            Toast(rc == 3 ? "NOCLIP: THE BIKE COULD NOT BE PLACED NOW" : rc == 2 ? "NOCLIP: NOT IN THIS RIDE" : "NOCLIP NEEDS THE CURRENT CORE", "");
        }

        void Toast(string title, string sub)
        {
            if (ReflexSettings.HudHidden) return;
            if (!notice) notice = SpawnMarkerNotice.Create(transform);
            notice.Show(title, sub);
        }
    }
}
