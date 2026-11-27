using UnityEngine;
using UnityEngine.InputSystem;

namespace Reflex.Port.OriginalWorld
{
    /// A Unity gamepad as the XINPUT_GAMEPAD the original reads every frame (XInputGetState), which the core's pad poll (Pad_Update 008157a0)
    /// takes. Split out of OriginalWorldRuntime.ReadPads so the production conversion can be tested on every device layout.
    /// The Input System's own processing is bypassed on purpose (ReadUnprocessedValue): the stick deadzone (default 0.125..0.925, which
    /// rescales) would be applied on top of the game's own (7864 / 32767 = 0.24, rx_pad_axis 008156e0) and the triggers keep their byte
    /// resolution (the game's threshold is 30 / 255). The conversion is linear and per axis, as XInput hands the values over.
    public static class OriginalPadConvert
    {
        /// The buttons of the pad as the XINPUT_GAMEPAD wButtons word (the order OriginalWorldRuntime always used).
        public static ushort Buttons(Gamepad g)
        {
            ushort b = 0;
            if (g.dpad.up.isPressed) b |= 0x1; if (g.dpad.down.isPressed) b |= 0x2;
            if (g.dpad.left.isPressed) b |= 0x4; if (g.dpad.right.isPressed) b |= 0x8;
            if (g.startButton.isPressed) b |= 0x10; if (g.selectButton.isPressed) b |= 0x20;
            if (g.leftStickButton.isPressed) b |= 0x40; if (g.rightStickButton.isPressed) b |= 0x80;
            if (g.leftShoulder.isPressed) b |= 0x100; if (g.rightShoulder.isPressed) b |= 0x200;
            if (g.buttonSouth.isPressed) b |= 0x1000; if (g.buttonEast.isPressed) b |= 0x2000;
            if (g.buttonWest.isPressed) b |= 0x4000; if (g.buttonNorth.isPressed) b |= 0x8000;
            return b;
        }

        /// Sticks and triggers of the pad with the given (already filtered) button word.
        public static ReflexPad Convert(Gamepad g, ushort buttons)
        {
            var l = g.leftStick.ReadUnprocessedValue(); var r = g.rightStick.ReadUnprocessedValue();
            float lt = g.leftTrigger.ReadUnprocessedValue(), rt = g.rightTrigger.ReadUnprocessedValue();
            // INVERT LEAN (Options/ReflexLeanInvert.cs): the stick axes that lean the rider, negated (off by default: untouched)
            int invert = Reflex.Port.Options.ReflexLeanInvert.Mask(buttons, lt, rt);
            if (invert != 0)
            {
                if ((invert & 1) != 0) l.x = -l.x; if ((invert & 2) != 0) l.y = -l.y;
                if ((invert & 4) != 0) r.x = -r.x; if ((invert & 8) != 0) r.y = -r.y;
            }
            return new ReflexPad
            {
                buttons = buttons,
                leftTrigger = Trigger(lt),
                rightTrigger = Trigger(rt),
                // STICK RESPONSE (Options/ReflexStickResponse.cs): the player's dead zone, sensitivity and curve per stick (the defaults: untouched)
                lx = Stick(Tuned(l.x, true)), ly = Stick(Tuned(l.y, true)), rx = Stick(Tuned(r.x, false)), ry = Stick(Tuned(r.y, false))
            };
        }

        static float Tuned(float axis, bool left) => Reflex.Port.Options.ReflexStickResponse.Apply(axis, left ? Reflex.Port.Options.ReflexStickResponse.Left : Reflex.Port.Options.ReflexStickResponse.Right);

        /// -1..1 to XInput's sThumb (the Input System reports -32768 as -1 and +32767 as +1 for a 16-bit axis).
        public static short Stick(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767);
        /// 0..1 to XInput's bTrigger.
        public static byte Trigger(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255);
    }
}
