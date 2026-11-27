// TEST ONLY (issue #22; tests/menus_showcase, run.py --batch): OPTIONS' RESET TO DEFAULTS rows.
//   resets   every page's RESET row, focused (its help in the side card), one shot per page; the confirmation popup (CAMERA & HUD > HUD);
//            CANCEL keeps the values; RESET puts them back and writes the save (the keys are gone from save.json); the same for CAMERA,
//            GAMEPLAY, INTERFACE and OPEN REFLEX; FIRST-PERSON VIEW's RESET TO DEFAULTS asks too. AUDIO, VIDEO and CONTROLS are shown and
//            asked, never pressed here (they would touch the volume, graphics.cfg and the controller table of the test player).
// Result lines "[ui-showcase] [resets] PASS|FAIL ...".
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Showcase
{
    static class UiShowcaseResets
    {
        const string Tag = "[ui-showcase] [resets] ";
        static int fails;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => UiShowcase.Register("resets", Run);

        sealed class Mute : MonoBehaviour { void LateUpdate() { AudioListener.volume = 0f; } }   // test players are muted

        static void Check(bool ok, string what) { Debug.Log(Tag + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
        static VisualElement Root => ReflexOptions.Root;
        /// The save written and read back from disk (ReflexSave.ReloadForTest): the getters then say what a new start would see.
        static void Reload() { ReflexSave.Save(); ReflexSave.ReloadForTest(); }

        static IEnumerator Open(int tab)
        {
            if (ReflexOptions.IsOpen) { ReflexOptions.Close(); yield return UiShowcase.Frames(8); }
            if (OpenShell.Instance != null) OpenShell.Instance.ShowOptions(tab); else ReflexOptions.Open(tab);
            yield return UiShowcase.Frames(30);
        }

        /// The row in view and focused (the side card shows its help).
        static IEnumerator FocusRow(string id)
        {
            var row = Root?.Q(id);
            if (row == null) { Check(false, "row " + id + " is on the page"); yield break; }
            var sv = row.GetFirstAncestorOfType<ScrollView>();
            try { row.Focus(); } catch (Exception) { }
            for (int f = 0; f < 16; f++) { yield return null; sv?.ScrollTo(row); }
        }

        static IEnumerator Submit(string id) { var row = Root?.Q(id); ((OuNavItem)row.userData).submit(); yield return UiShowcase.Frames(8); }
        static bool PopupOpen => Root?.Q("reset-popup") != null;

        /// RESET row id: asks (the popup), CANCEL (nothing changes) when cancel, else RESET.
        static IEnumerator Answer(string id, bool yes)
        {
            yield return Submit(id);
            Check(PopupOpen, id + " asks first");
            var b = Root?.Q(yes ? "reset-yes" : "reset-no");
            if (b == null) { Check(false, id + ": the popup's " + (yes ? "RESET" : "CANCEL")); yield break; }
            ((OuNavItem)b.userData).submit();
            yield return UiShowcase.Frames(10);
            Check(!PopupOpen, id + ": the popup closes");
        }

        static IEnumerator Run()
        {
            var g = new GameObject("resets mute (test only)"); UnityEngine.Object.DontDestroyOnLoad(g); g.AddComponent<Mute>();
            // values away from their defaults
            ReflexSettings.ChaseFov = 125; ReflexSettings.HudScale = 130; ReflexSettings.HudOpacity = 60; ReflexSettings.HudStandings = false; ReflexSettings.CameraToast = false;
            ReflexSettings.Metric = true; ReflexSettings.CareerDifficulty = 105; ReflexSettings.ReduceMotion = true; ReflexSettings.GlyphStyle = 2;
            ReflexSettings.AirLooseness = 0.5f; ReflexSettings.LipPull = 0.3f; ReflexSettings.AnyVehicle = false; ReflexSettings.OnboardFov = 117;
            ReflexSettings.Save();
            yield return UiShowcase.Frames(10);

            // every page's row
            var rows = new (int tab, string id)[] { (0, "reset-controls"), (1, "reset-audio"), (2, "reset-video"), (3, "reset-gameplay"), (4, "reset-camera"), (4, "reset-hud"), (4, "fpv-reset"), (5, "reset-interface"), (7, "reset-open-reflex") };
            foreach (var (tab, id) in rows)
            {
                yield return Open(tab);
                yield return FocusRow(id);
                var help = Root?.Q<Label>(className: "ou-opt-side-help");
                Check(help != null && help.text.Length > 20, id + " has its help: " + help?.text);
                yield return UiShowcase.Shot("reset-" + id);
                // the same row at rest: the focus on the item before it, the row kept in view
                var row = Root.Q(id); var sv = row.GetFirstAncestorOfType<ScrollView>();
                var items = sv.contentContainer.Query(className: "mxa-item").ToList().FindAll(e => e.resolvedStyle.display != DisplayStyle.None && e.enabledInHierarchy);
                int at = items.IndexOf(row);
                if (at > 0) { try { items[at - 1].Focus(); } catch (Exception) { } }
                for (int f = 0; f < 16; f++) { yield return null; sv.ScrollTo(row); }
                Check(row.focusController?.focusedElement != row, id + " at rest (focus on the row before it)");
                yield return UiShowcase.Shot("reset-" + id + "-unfocused");
            }
            ReflexOptions.Close(); yield return UiShowcase.Frames(8);

            // HUD: the popup (shot), CANCEL keeps, RESET puts back and saves
            yield return Open(4); yield return FocusRow("reset-hud");
            yield return Submit("reset-hud");
            Check(PopupOpen, "reset-hud asks first");
            yield return UiShowcase.Shot("reset-confirm-hud");
            ((OuNavItem)Root.Q("reset-no").userData).submit(); yield return UiShowcase.Frames(10);
            Check(!PopupOpen && ReflexSettings.HudScale == 130 && !ReflexSettings.HudStandings, "CANCEL keeps the HUD (" + ReflexSettings.HudScale + "%)");
            yield return Answer("reset-hud", true);
            Check(ReflexSettings.HudScale == 100 && ReflexSettings.HudOpacity == 100 && ReflexSettings.HudStandings && ReflexSettings.CameraToast, "RESET HUD: 100% / 100% / standings / camera name back");
            Reload();
            Check(ReflexSettings.HudScale == 100 && ReflexSettings.HudStandings && !ReflexSave.Has("Options.HudScale") && ReflexSettings.ChaseFov == 125, "RESET HUD is saved (read back from save.json; CHASE FOV still 125%)");
            Check(ReflexSettings.ChaseFov == 125, "RESET HUD leaves the CAMERA rows (" + ReflexSettings.ChaseFov + "%)");
            Check(Root.Q("reset-hud") != null, "the page is drawn again");
            yield return FocusRow("reset-hud");
            yield return UiShowcase.Shot("reset-after-hud");
            yield return Answer("reset-camera", true);
            Check(ReflexSettings.ChaseFov == 100, "RESET CAMERA: CHASE CAMERA FOV " + ReflexSettings.ChaseFov + "%");
            Reload(); Check(ReflexSettings.ChaseFov == 100, "RESET CAMERA is saved (read back: " + ReflexSettings.ChaseFov + "%)");
            yield return Answer("fpv-reset", false);
            Check(Mathf.Approximately(ReflexSettings.OnboardFov, 117), "FIRST-PERSON CANCEL keeps its FOV");
            yield return Answer("fpv-reset", true);
            Check(!Mathf.Approximately(ReflexSettings.OnboardFov, 117), "FIRST-PERSON RESET puts its FOV back (" + ReflexSettings.OnboardFov + ")");

            yield return Open(3); yield return FocusRow("reset-gameplay"); yield return Answer("reset-gameplay", true);
            Reload(); Check(!ReflexSettings.Metric && ReflexSettings.CareerDifficulty == 80, "RESET GAMEPLAY: US units, ROOKIE (read back)");
            yield return Open(5); yield return FocusRow("reset-interface"); yield return Answer("reset-interface", true);
            Check(!ReflexSettings.ReduceMotion && ReflexSettings.GlyphStyle == 0 && ReflexSettings.UiScale == 100, "RESET INTERFACE: motion, glyphs AUTO, 100%");
            yield return Open(7); yield return FocusRow("reset-open-reflex"); yield return Answer("reset-open-reflex", true);
            Reload();
            Check(ReflexSettings.AirLooseness == 0f && ReflexSettings.LipPull == 1f && ReflexSettings.AnyVehicle, "RESET OPEN REFLEX: the original's air and ANY VEHICLE on, read back from the save");
            // the pages that are only asked here: the popup comes, CANCEL changes nothing
            foreach (var (tab, id) in new[] { (0, "reset-controls"), (1, "reset-audio"), (2, "reset-video") })
            {
                yield return Open(tab); yield return FocusRow(id);
                yield return Submit(id);
                Check(PopupOpen, id + " asks first");
                if (id == "reset-video") yield return UiShowcase.Shot("reset-confirm-video");
                ((OuNavItem)Root.Q("reset-no").userData).submit(); yield return UiShowcase.Frames(8);
                Check(!PopupOpen, id + ": CANCEL closes it");
            }
            ReflexOptions.Close(); yield return UiShowcase.Frames(8);
            Debug.Log(Tag + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        }
    }
}
