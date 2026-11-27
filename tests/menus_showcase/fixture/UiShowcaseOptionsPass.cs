// TEST ONLY: every OPTIONS tab from top to bottom (page "optpass"): one screenshot per screenful of the scrolling page, a row of that
// screenful focused so the side card shows its help. REFLEX_OPT_UISCALE=130 sets UI SCALE first, REFLEX_OPT_GLYPHS=0..3 CONTROLLER GLYPHS
// (the scratch save). The log lists every row (section, label, value, enabled) and the help the side card shows for it.
using System;
using System.Collections;
using System.Collections.Generic;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseOptionsPass
    {
        const string Tag = "[ui-showcase] optpass: ";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => UiShowcase.Register("optpass", Run);

        static string Slug(int tab) => ReflexOptions.Tabs[tab].ToLowerInvariant().Replace(" & ", "-").Replace(' ', '-');

        static IEnumerator Run()
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("REFLEX_OPT_UISCALE"), out int scale)) ReflexSettings.UiScale = scale;
            if (int.TryParse(Environment.GetEnvironmentVariable("REFLEX_OPT_GLYPHS"), out int glyphs)) ReflexSettings.GlyphStyle = glyphs;
            if (Environment.GetEnvironmentVariable("REFLEX_OPT_DEPS") == "1")
            {   // every row that depends on another, switched off: the rows show why
                Reflex.Port.Rendering.ReflexAntiAliasing.Set(Reflex.Port.Rendering.ReflexAntiAliasing.Mode.Msaa2);
                Reflex.Port.Rendering.ReflexVolumetricLight.Option = 0;
                ReflexSettings.HudStyle = HudStyle.Original; ReflexSettings.MusicOn = false; ReflexSettings.MouseLean = false;
                ReflexSettings.FpsCap = 60; ReflexSettings.Vibration = false;
            }
            yield return UiShowcase.Frames(10);
            for (int tab = 0; tab < ReflexOptions.Tabs.Length; tab++)
            {
                if (OpenShell.Instance != null) OpenShell.Instance.ShowOptions(tab); else ReflexOptions.Open(tab);
                yield return UiShowcase.Frames(40);
                var root = ReflexOptions.Root;
                var sv = root?.Q<ScrollView>(className: "mxa-options-scroll");
                if (sv == null) { Debug.Log(Tag + Slug(tab) + ": no scroll view"); continue; }
                var help = root.Q<Label>(className: "ou-opt-side-help");
                // the rows, in order, with the help of each
                string section = ""; int overflows = 0;
                var all = new List<VisualElement>();
                void Collect(VisualElement p)
                {
                    foreach (var c in p.Children())
                    {
                        if (c.resolvedStyle.display == DisplayStyle.None) continue;
                        if (c.ClassListContains("mxa-panel-title") && c is Label t) { section = t.text; Debug.Log(Tag + Slug(tab) + " == " + section); continue; }
                        if (c.ClassListContains("mxa-item")) { all.Add(c); continue; }
                        if (c.ClassListContains("mxa-note") && c is Label keep) { Debug.Log(Tag + Slug(tab) + "    (text) " + keep.text); continue; }
                        Collect(c);
                    }
                }
                Collect(sv.contentContainer);
                foreach (var item in all)
                {
                    try { item.Focus(); } catch (Exception) { }
                    yield return null;
                    var label = item.Q<Label>(className: "mxa-row-label") ?? item.Q<Label>(className: "mxa-button-label");
                    var val = item.Q<Label>(className: "mxa-value");
                    var side = root.Q<VisualElement>(className: "ou-opt-side"); var pageBox = root.Q<VisualElement>(className: "mxa-options-page");
                    var needs = item.Q<Label>(className: "ou-opt-needs"); var when = root.Q<Label>(className: "ou-opt-side-when"); var bar = side?.Q<Label>(className: "ou-pill");
                    bool over = side != null && pageBox != null && side.worldBound.yMax > pageBox.worldBound.yMax + 1f;   // the card grew past the page's bottom edge (towards the hint bar)
                    if (over) overflows++;
                    Debug.Log(Tag + Slug(tab) + "  " + (label != null ? label.text : item.name) + " = " + (val != null ? val.text : "-")
                              + (item.enabledInHierarchy ? "" : "  [DISABLED" + (needs != null ? ": " + needs.text : "") + "]")
                              + "  | " + (bar != null ? "[" + bar.text + "] " : "") + (when != null && when.resolvedStyle.display != DisplayStyle.None ? "[" + when.text + "] " : "")
                              + (help != null ? help.text.Length + " chars: " + help.text : "") + (over ? "  <<< CARD OVERFLOWS" : ""));
                }
                // one shot per screenful
                float view = sv.contentViewport.layout.height, total = sv.contentContainer.layout.height;
                int n = 0;
                for (float y = 0; n < 12; y += Mathf.Max(60f, view - 60f), n++)
                {
                    float at = Mathf.Min(y, Mathf.Max(0f, total - view));
                    sv.scrollOffset = new Vector2(0, at);
                    yield return UiShowcase.Frames(3);
                    // (the navigation keeps the focused row in view, so the screenful is chosen by its focused row: the first row of the
                    // first screenful, then the last row that fits each next one, which the page scrolls up to)
                    float top = sv.contentContainer.worldBound.y;
                    VisualElement pick = null;
                    foreach (var item in all)
                    {
                        if (!item.enabledInHierarchy) continue;
                        float y0 = item.worldBound.yMin - top, y1 = item.worldBound.yMax - top;
                        if (y0 >= at && y1 <= at + view) { if (n == 0) { pick = item; break; } pick = item; }
                    }
                    if (pick != null) { try { pick.Focus(); } catch (Exception) { } }
                    for (int f = 0; f < 14; f++) { yield return null; sv.scrollOffset = new Vector2(0, at); }   // (the focus scrolls to its row over a few frames: hold the screenful)
                    yield return null;
                    yield return UiShowcase.Shot("opt-" + tab + "-" + Slug(tab) + "-" + (n + 1));
                    if (at >= total - view - 1f) break;
                }
                Debug.Log(Tag + Slug(tab) + ": " + overflows + " rows whose help card overflows");
                Debug.Log(Tag + Slug(tab) + ": " + all.Count + " rows, page " + total.ToString("0") + " px in a " + view.ToString("0") + " px view, " + (n + 1) + " shots");
                ReflexOptions.Close();
                yield return UiShowcase.Frames(8);
            }
        }
    }
}
