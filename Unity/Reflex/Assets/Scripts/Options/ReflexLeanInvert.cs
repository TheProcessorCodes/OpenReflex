// INVERT LEAN (OPTIONS > CONTROLS, issue #46): the rider leans the other way from the stick and the mouse. LEFT / RIGHT and FORWARD / BACK are
// separate rows; both are off by default (the original's: stick left = lean left, stick up = lean forward).
// Drawing-side only, as STICK RESPONSE is: the pad's lean axes are negated before the core reads the pad (OriginalPadConvert), and the mouse's
// counts before they are sent (OriginalInput.ReadMouse). The lean keys are not touched: the mapping table swaps those.
//   Which axes lean: the pad inputs of the mapping table, as the game routes them (simulation-input.md). RIGHT-HANDED: RiderReflexStick, the
//   table's ReflexLeft / ReflexUp cells (the right stick's X / Y by default). LEFT-HANDED: the steering stick leans, the table's SteerLeft cell
//   (the left stick's X by default) and the left stick's Y. A lean cell bound to a button is not an axis: nothing to negate.
//   Stunts keep their directions: while the pad's TRICK MODIFIER input is held (LB by default) nothing is negated, so the stunt list's
//   stick moves stay as the game shows them.
using System;
using Reflex.Port.FrontEnd;
using Reflex.Port.RxCore.Bridge;

namespace Reflex.Port.Options
{
    public static class ReflexLeanInvert
    {
        // the mapping table's columns (CoreRemapping.Columns) and the pad's default inputs in them (RemapTable_InitDefaults 005b9340)
        const int ColSteerLeft = 2, ColTrick = 4, ColReflexLeft = 6, ColReflexUp = 8;
        const int PadLeftX = 1, PadLeftY = 2, PadRightX = 3, PadRightY = 4, PadLb = 13, PadLeftClick = 17;
        /// The game's trigger threshold (30 of 255): a trigger bound to TRICK MODIFIER counts as held beyond it.
        const float TriggerHeld = 30f / 255f;

        /// The stick axes to negate, as bits: 1 left X, 2 left Y, 4 right X, 8 right Y. padRow: the mapping table's 19 pad inputs (null: the
        /// defaults). buttons: the pad's XINPUT_GAMEPAD wButtons; lt / rt: its triggers, 0 .. 1.
        public static int Mask(bool invertX, bool invertY, bool rightHanded, int[] padRow, ushort buttons, float lt, float rt)
        {
            if (!invertX && !invertY) return 0;
            int Cell(int column, int fallback) => padRow != null && column < padRow.Length ? padRow[column] : fallback;
            if (Held(Cell(ColTrick, PadLb), rightHanded, buttons, lt, rt)) return 0;
            int x = rightHanded ? Cell(ColReflexLeft, PadRightX) : Cell(ColSteerLeft, PadLeftX);
            int y = rightHanded ? Cell(ColReflexUp, PadRightY) : PadLeftY;
            return (invertX ? Bit(x) : 0) | (invertY ? Bit(y) : 0);
        }
        static int Bit(int padInput) => padInput >= PadLeftX && padInput <= PadRightY ? 1 << (padInput - 1) : 0;

        /// A pad input of the mapping table (9 a .. 24 d-pad left: input-settings.md) as held or not.
        static bool Held(int padInput, bool rightHanded, ushort buttons, float lt, float rt)
        {
            switch (padInput)
            {
                case 9: return (buttons & 0x1000) != 0; case 10: return (buttons & 0x2000) != 0; case 11: return (buttons & 0x4000) != 0; case 12: return (buttons & 0x8000) != 0;
                case 13: return (buttons & 0x100) != 0; case 14: return (buttons & 0x200) != 0; case 15: return (buttons & 0x10) != 0; case 16: return (buttons & 0x20) != 0;
                // (a stick click as the TRICK MODIFIER is the steering stick's own for right-handed controls, else the rider stick's: 00548660)
                case PadLeftClick: return (buttons & (rightHanded ? 0x40 : 0x80)) != 0;
                case 18: return (buttons & 0x80) != 0;
                case 19: return lt > TriggerHeld; case 20: return rt > TriggerHeld;
                case 21: return (buttons & 0x1) != 0; case 22: return (buttons & 0x8) != 0; case 23: return (buttons & 0x2) != 0; case 24: return (buttons & 0x4) != 0;
                default: return false;
            }
        }

        /// The axes to negate with the player's settings and mapping table (0 with both rows off: the pad's values are not touched).
        public static int Mask(ushort buttons, float lt, float rt)
        {
            bool x = ReflexSettings.InvertLeanX, y = ReflexSettings.InvertLeanY;
            return x || y ? Mask(x, y, ReflexSettings.RightHanded, PadRow(), buttons, lt, rt) : 0;
        }

        // the player's pad inputs (the core's mapping file; read again when it is saved). Without the open core, or without a file: the defaults.
        static int[] s_padRow; static int s_generation = -1;
        static int[] PadRow()
        {
            if (!OpenReflexSelector.UseOpenReflex) return null;
            if (s_generation == CoreRemapping.Generation) return s_padRow;
            s_generation = CoreRemapping.Generation; s_padRow = null;
            try { var values = CoreRemapping.StartupValues(); if (values != null) { s_padRow = new int[19]; Array.Copy(values, 19, s_padRow, 0, 19); } }
            catch (Exception) { }   // (a broken file: the defaults, as the page shows them)
            return s_padRow;
        }
    }
}
