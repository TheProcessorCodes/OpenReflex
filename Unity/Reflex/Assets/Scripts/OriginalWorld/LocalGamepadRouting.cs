using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Reflex.Port.OriginalWorld
{
    // Unity device identity is distinct from the native session's controller index.
    // One single-player world owns this main-thread map; guest/core state is untouched.
    public sealed class LocalGamepadRouting : IDisposable
    {
        static Gamepad menuSubmitPad;
        static LocalGamepadRouting active;
        readonly Gamepad[] slots = new Gamepad[4];
        readonly float[] motors = new float[8];
        bool begun, recovering;
        double recoveryAfter;
        public static Gamepad ActiveLocalPad => active == null ? null : active.Pad(0);
        /// SPLIT SCREEN (w-split): the controller that joined as player 2 on the setup page's PLAYERS tab (null: none, or it went away). A split
        /// world puts it in slot 1 (the core's controller 1) and never gives it to player 1; its menu presses are never player 1's pad.
        public static Gamepad Player2Pad { get => Usable(player2Pad) ? player2Pad : null; set { player2Pad = value; if (value != null && menuSubmitPad == value) menuSubmitPad = null; } }
        static Gamepad player2Pad;
        /// The pad player 1 last pressed a menu submit with (null: none yet, or the keyboard).
        public static Gamepad MenuPad => Usable(menuSubmitPad) ? menuSubmitPad : null;
        public static void RememberMenuSubmit(Gamepad pad) { if (pad != null && pad == Player2Pad) return; menuSubmitPad = Usable(pad) ? pad : null; }
        static bool Usable(Gamepad pad) => pad != null && pad.added && pad.enabled;
        static bool Intent(Gamepad pad) => pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed || pad.buttonNorth.isPressed || pad.startButton.isPressed || pad.selectButton.isPressed || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed || pad.dpad.ReadValue().sqrMagnitude > .1f || pad.leftTrigger.ReadUnprocessedValue() > .1f || pad.rightTrigger.ReadUnprocessedValue() > .1f || pad.leftStick.ReadUnprocessedValue().sqrMagnitude > .0625f || pad.rightStick.ReadUnprocessedValue().sqrMagnitude > .0625f;
        public void BeginSession()
        {
            if (begun) return; // World rebuild/F5 never silently reselects a different pad.
            begun = true; active = this;
            // SPLIT SCREEN (w-split): player 2's joined pad is slot 1 and is never chosen for player 1
            var p2 = Reflex.Port.RxCore.RxSplitScreen.Requested ? Player2Pad : null;
            Gamepad chosen = Usable(menuSubmitPad) && menuSubmitPad != p2 ? menuSubmitPad : Usable(Gamepad.current) && Gamepad.current != p2 ? Gamepad.current : null;
            if (chosen == null) foreach (var pad in Gamepad.all) if (Usable(pad) && pad != p2) { chosen = pad; break; }
            Assign(0, chosen); recovering = chosen == null;
            if (p2 != null) Assign(1, p2);
            recoveryAfter = double.NegativeInfinity;
            Refresh();
        }
        public Gamepad Pad(int slot) { if ((uint)slot >= 4) throw new ArgumentOutOfRangeException(nameof(slot)); if (!begun) BeginSession(); Refresh(); return slots[slot]; }
        public void Refresh()
        {
            if (!begun) return;
            if (slots[0] != null && !Usable(slots[0]))
            {
                // Wait for actual intent instead of binding an idle virtual/duplicate pad.
                recoveryAfter = slots[0].lastUpdateTime; Assign(0, null); recovering = true;
            }
            for (int i = 1; i < 4; i++) if (!Usable(slots[i])) Assign(i, null);
            if (recovering)
            {
                Gamepad replacement = null;
                // SPLIT SCREEN (w-split): the second rider's pad (slot 1) is never taken for the first rider; slot 0 waits for its own pad or a free one.
                var held = Reflex.Port.RxCore.RxSplitScreen.Active ? slots[1] : null;
                foreach (var pad in Gamepad.all)
                    if (pad != held && Usable(pad) &&pad.lastUpdateTime > recoveryAfter && Intent(pad) && (replacement == null || pad.lastUpdateTime > replacement.lastUpdateTime)) replacement = pad;
                if (replacement != null)
                {
                    for (int i = 1; i < 4; i++) if (slots[i] == replacement) Assign(i, null);
                    Assign(0, replacement); recovering = false;
                }
            }
            foreach (var pad in Gamepad.all)
            {
                if (!Usable(pad) || Array.IndexOf(slots, pad) >= 0) continue;
                for (int i = 1; i < 4; i++) if (slots[i] == null) { Assign(i, pad); break; }
            }
        }
        void Assign(int slot, Gamepad pad)
        {
            if (slots[slot] == pad) return;
            var old = slots[slot];
            if (old != null && (motors[slot * 2] != 0 || motors[slot * 2 + 1] != 0)) old.SetMotorSpeeds(0, 0);
            motors[slot * 2] = motors[slot * 2 + 1] = 0; slots[slot] = pad;
        }
        public void SetRumble(int slot, float low, float high)
        {
            var pad = Pad(slot); low = Mathf.Clamp01(low); high = Mathf.Clamp01(high);
            if (pad == null || motors[slot * 2] == low && motors[slot * 2 + 1] == high) return;
            pad.SetMotorSpeeds(low, high); motors[slot * 2] = low; motors[slot * 2 + 1] = high;
        }
        public void StopRumble()
        {
            for (int i = 0; i < 4; i++)
            {
                if (slots[i] != null && (motors[i * 2] != 0 || motors[i * 2 + 1] != 0)) slots[i].SetMotorSpeeds(0, 0);
                motors[i * 2] = motors[i * 2 + 1] = 0;
            }
        }
        public void Dispose()
        { StopRumble(); Array.Clear(slots, 0, slots.Length); begun = false; if (active == this) active = null; }
    }
}
