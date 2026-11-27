// OPTIONS > INTERFACE: how the client's menus look and read (ours), and the language of the game's own texts.
//   MENUS      UI SCALE (every menu page, 80% .. 130%: the panels' reference resolution), CONTROLLER GLYPHS (the button hints: AUTO follows
//              the device used last; KEYBOARD, XBOX, PLAYSTATION), REDUCE MOTION (the menus' transitions and the HUD's flourishes off)
//   LANGUAGE   the game's texts (its own string tables: event names, rules, the career's pages): the install's ENG and whatever other
//              Latin-script languages it carries; applies at the next start (the shell's type does not draw Japanese, Korean or Chinese)
using System;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Options
{
    public sealed partial class ReflexOptionsMenu
    {
        static readonly string[] LanguageNames = { "ENG:ENGLISH", "GER:DEUTSCH", "FRE:FRANÇAIS", "SPA:ESPAÑOL", "ITA:ITALIANO" };
        static string LanguageName(string code) { foreach (var n in LanguageNames) if (n.StartsWith(code + ":", StringComparison.Ordinal)) return n.Substring(4); return code; }

        VisualElement BuildInterface(VisualElement panel)
        {
            Section(panel, "MENUS");
            var first = Named(OptUi.Spinner("UI SCALE", () => ReflexSettings.UiScale + "%", d => { ReflexSettings.UiScale = ReflexSettings.UiScale + d * 10; ReflexSettings.Save(); }, panel), "ui-scale");
            Help(panel, "The size of every menu page, 80% to 130%. A very wide screen keeps the pages in the middle.");
            Named(OptUi.Spinner("CONTROLLER GLYPHS", () => ReflexSettings.GlyphStyleNames[ReflexSettings.GlyphStyle], d =>
            {
                ReflexSettings.GlyphStyle = ((ReflexSettings.GlyphStyle + d) % 4 + 4) % 4; ReflexSettings.Save(); ShowOptions(_tab);
            }, panel), "glyph-style");
            Help(panel, "The button names in the hints. AUTO follows the keyboard or controller you used last; or always show KEYBOARD, XBOX or PLAYSTATION names.");
            Named(OptUi.Toggle("REDUCE MOTION", () => ReflexSettings.ReduceMotion, v => { ReflexSettings.ReduceMotion = v; ReflexSettings.Save(); ShowOptions(_tab); }, panel), "reduce-motion");
            Help(panel, "Turns off the menus' transitions and the HUD's animated flourishes. Values and colours stay.");

            Section(panel, "LANGUAGE");
            string[] codes; try { codes = ReflexUiData.AvailableLanguages(); } catch (Exception) { codes = new[] { "ENG" }; }
            Named(OptUi.Spinner("LANGUAGE", () =>
            {
                string chosen = ReflexSettings.Language;
                string show = string.IsNullOrEmpty(chosen) ? ReflexUiData.Shared.language : chosen;
                return LanguageName(show) + (string.IsNullOrEmpty(chosen) ? "  (INSTALL)" : "");
            }, d =>
            {
                var order = new[] { "" }.Concat(codes).ToArray();
                int i = Array.IndexOf(order, ReflexSettings.Language); if (i < 0) i = 0;
                ReflexSettings.Language = order[((i + d) % order.Length + order.Length) % order.Length]; ReflexSettings.Save();
            }, panel), "language");
            Help(panel, codes.Length > 1
                ? "The language of the game's own texts: event names, rules, the career's pages and the HUD's words. INSTALL is the language your install was set up in."
                : "The language of the game's own texts. This install carries one language only.", codes.Length > 1 ? When.NextStart : When.None, true);
            ResetSection(panel, "INTERFACE", "reset-interface", "Puts every row of this page back to its default: UI SCALE 100%, CONTROLLER GLYPHS AUTO, REDUCE MOTION OFF, the install's LANGUAGE (from the next start).",
                         "UI SCALE, CONTROLLER GLYPHS, REDUCE MOTION and LANGUAGE go back to their defaults. A language change shows from the next start.", ResetInterface);
            return first;
        }
    }
}
