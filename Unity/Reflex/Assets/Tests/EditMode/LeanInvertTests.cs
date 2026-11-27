using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Reflex.Port.RxCore.Tests
{
    /// INVERT LEAN (issue #46): which stick axes Options/ReflexLeanInvert negates (bits: 1 left X, 2 left Y, 4 right X, 8 right Y).
    public class LeanInvertTests
    {
        // Options lives in Assembly-CSharp; the pure overload takes every input, so no player's save is read.
        static int Mask(bool x, bool y, bool rightHanded, int[] padRow, ushort buttons = 0, float lt = 0f, float rt = 0f)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Reflex.Port.Options.ReflexLeanInvert")).First(t => t != null);
            var method = type.GetMethod("Mask", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(bool), typeof(bool), typeof(bool), typeof(int[]), typeof(ushort), typeof(float), typeof(float) }, null);
            return (int)method.Invoke(null, new object[] { x, y, rightHanded, padRow, buttons, lt, rt });
        }
        // the pad's default row: rt lt ls_x ls_x lb rb rs_x rs_x rs_y rs_y back y x b a + the camera tweaks' keys
        static int[] Defaults() => new[] { 20, 19, 1, 1, 13, 14, 3, 3, 4, 4, 16, 12, 11, 10, 9, 44, 46, 43, 45 };

        [Test] public void OffTouchesNothing() => Assert.AreEqual(0, Mask(false, false, true, null));

        [Test] public void RightHandedInvertsTheRightStick()
        {
            Assert.AreEqual(4, Mask(true, false, true, null));
            Assert.AreEqual(8, Mask(false, true, true, null));
            Assert.AreEqual(12, Mask(true, true, true, Defaults()));
        }

        [Test] public void LeftHandedInvertsTheLeftStick()
        {
            Assert.AreEqual(1, Mask(true, false, false, null));
            Assert.AreEqual(3, Mask(true, true, false, Defaults()));
        }

        [Test] public void FollowsTheMappingTable()
        {
            var row = Defaults(); row[6] = row[7] = 1; row[8] = row[9] = 2; row[2] = row[3] = 3;   // lean on the left stick, steering on the right
            Assert.AreEqual(3, Mask(true, true, true, row));
            row[6] = 21;   // lean left / right on the d-pad: a button, no axis to negate
            Assert.AreEqual(2, Mask(true, true, true, row));
        }

        [Test] public void StuntsKeepTheirDirections()
        {
            Assert.AreEqual(0, Mask(true, true, true, null, 0x100));          // LB, the default TRICK MODIFIER
            Assert.AreEqual(12, Mask(true, true, true, null, 0x200));         // RB is not
            var row = Defaults(); row[4] = 19;                                // TRICK MODIFIER on the left trigger
            Assert.AreEqual(12, Mask(true, true, true, row, 0x100));
            Assert.AreEqual(0, Mask(true, true, true, row, 0, 0.5f));
            Assert.AreEqual(12, Mask(true, true, true, row, 0, 0.05f));       // under the game's trigger threshold
        }
    }
}
