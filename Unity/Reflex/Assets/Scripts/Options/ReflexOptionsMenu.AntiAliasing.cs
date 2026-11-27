// OPTIONS > VIDEO > ANTI-ALIASING AND SCALING: ANTI-ALIASING (OFF / MSAA 2X / MSAA 4X: graphics.cfg Antialiasing as the game's own option;
// TAA: ours, the default, Rendering/ReflexTemporalAA.cs), SHARPENING (the TAA image's contrast-adaptive sharpening) and UPSCALING (TAA
// renders the world below the screen's resolution and rebuilds the full image over frames); both need TAA and say so on the row.
// DYNAMIC RESOLUTION / DYNAMIC MINIMUM / DYNAMIC TARGET (Rendering/ReflexDynamicResolution.cs): the same upscaling, its share following the frame time.
// (CTRL+F9 in a race cycles ANTI-ALIASING: a developers' A/B key, not in the player's help.)
using UnityEngine;
using UnityEngine.UIElements;
using Reflex.Port.Rendering;

namespace Reflex.Port.Options
{
    public sealed partial class ReflexOptionsMenu
    {
        void BuildAntiAliasing(VisualElement panel)
        {
            bool Taa() => ReflexAntiAliasing.Current == ReflexAntiAliasing.Mode.Taa;
            Named(OptUi.Spinner("ANTI-ALIASING", () => ReflexAntiAliasing.Label, d => { ReflexAntiAliasing.Cycle(d); PresetCustom(); ReflexSave.Save(); }, panel), "antialiasing");
            Help(panel, "Smooths jagged edges and the shimmer of fences, cables and grass. TAA blends several frames into one clean picture. MSAA 2X and 4X are the original game's and smooth geometry edges only.");
            Needs(Named(OptUi.Slider("SHARPENING", () => ReflexAntiAliasing.Sharpening / 100f, v => ReflexAntiAliasing.Sharpening = Mathf.RoundToInt(v * 100f), panel, 0f, 1f, 0.1f), "sharpening"), Taa, "NEEDS TAA");
            Help(panel, "Brings the fine texture detail back after TAA. 0% is none, 50% the default.");
            Needs(Named(OptUi.Spinner("UPSCALING", () => ReflexAntiAliasing.UpscalingNames[ReflexAntiAliasing.Upscaling]
                                                 + (ReflexAntiAliasing.Upscaling > 0 ? $"  ({Mathf.RoundToInt(ReflexAntiAliasing.UpscalingScales[ReflexAntiAliasing.Upscaling] * 100f)}%)" : ""),
                d => ReflexAntiAliasing.Upscaling = ((ReflexAntiAliasing.Upscaling + d) % 3 + 3) % 3, panel), "upscaling"), Taa, "NEEDS TAA");
            Help(panel, "Draws the world at 67% (QUALITY) or 58% (BALANCED) of the resolution and lets TAA rebuild the full picture over a few frames: faster, a little softer.");
            // DYNAMIC RESOLUTION (Rendering/ReflexDynamicResolution.cs): rides on the TAA's upscaling, so it needs TAA as UPSCALING does
            bool Dyn() => Taa() && ReflexDynamicResolution.Enabled;
            Needs(Named(OptUi.Toggle("DYNAMIC RESOLUTION", () => ReflexDynamicResolution.Enabled, v => { ReflexDynamicResolution.Enabled = v; PresetCustom(); }, panel), "dynamic-resolution"), Taa, "NEEDS TAA");
            Help(panel, "ON: when the graphics card cannot hold the frame rate, the world is drawn at a lower resolution until it can, and back at the full one when there is room. Menus and the HUD always stay sharp. Nothing changes while the frame rate holds.");
            Needs(Named(OptUi.Spinner("DYNAMIC MINIMUM", () => ReflexDynamicResolution.Minimum + "%", d =>
            {
                Cycle(ReflexDynamicResolution.Minimums, ReflexDynamicResolution.Minimum, d, out int n); ReflexDynamicResolution.Minimum = n;
            }, panel), "dynamic-minimum"), Dyn, "NEEDS DYNAMIC RESOLUTION");
            Help(panel, "The lowest resolution DYNAMIC RESOLUTION may go to, as a share of the full one. Lower holds the frame rate on slower graphics cards, softer when it gets there.");
            Needs(Named(OptUi.Spinner("DYNAMIC TARGET", () => ReflexDynamicResolution.Target + " FPS", d =>
            {
                Cycle(ReflexDynamicResolution.Targets, ReflexDynamicResolution.Target, d, out int n); ReflexDynamicResolution.Target = n;
            }, panel), "dynamic-target"), () => Dyn() && !ReflexDynamicResolution.TargetIsFixed, "FOLLOWS THE FRAME RATE CAP");
            Help(panel, "The frame rate DYNAMIC RESOLUTION holds when FRAME RATE CAP is OFF and VSync is off. With DISPLAY RATE, VSync or a cap it holds that rate.");
        }
    }
}
