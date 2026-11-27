// The options pages, modelled on Open Unleashed's MxaOptionsMenu (Scripts/UI/MxaOptionsMenu*.cs, _documentation/options-menu.md):
// eight tabs, value rows (spinners, sliders, toggles) in a scrolling panel, the focused row's help in one line under the page
// (CollapseNotes), Q / E the tabs, Esc back.
//   CONTROLS      RUMBLE (the game's Vibration) + RUMBLE STRENGTH (the game's motors scaled: ReflexRumbleScale), MOUSE SENSITIVITY
//                 (the game's), CONTROLLER REMAPPING (the game's own ControllerRemapping table through the profile session:
//                 ReflexRemap, as the original front end's Controller Remap Page)        ReflexOptionsMenu.Controls.cs
//   AUDIO         EFFECTS, MUSIC (on / volume), ENGINE: the game's own volumes; YOUR MUSIC, NOW PLAYING POP-UP (ReflexOptionsMenu.Music.cs)
//   VIDEO         DISPLAY, QUALITY PRESET, LIGHTING AND EFFECTS, TEXTURES AND DETAIL, ANTI-ALIASING AND SCALING, BRIGHTNESS, ADVANCED
//                 (graphics.cfg = ReflexLaunchSettings for the original game's own rows; the lighting is always ENHANCED)
//   GAMEPLAY      UNITS, CAREER DIFFICULTY, NATIVE GAME (OFF / NATIVE / VERIFY)
//   CAMERA & HUD  CAMERA (the camera in use, the camera type, CHASE CAMERA FOV), HUD (HUD STYLE MODERN / ORIGINAL, HUD on / off, the MODERN
//                 HUD's rows, CAMERA NAME, CLIENT INFO), then the long lists: CAMERA CYCLE and MY CAMERAS (the player's own presets and
//                 their editor: ReflexOptionsMenu.Cameras.cs), FIRST-PERSON VIEW (the Dynamic FPV camera: OriginalOnboardCamera)
//   INTERFACE     MENUS (UI SCALE, CONTROLLER GLYPHS, REDUCE MOTION), LANGUAGE of the game's texts      ReflexOptionsMenu.Interface.cs
//   SYSTEM        GAME FOLDER (the shell's Game Library), SAVE DATA (backup / restore / open folder), SUPPORT (the platform's
//                 report)                                                                ReflexOptionsMenu.System.cs
//   OPEN REFLEX   the settings of OPEN REFLEX itself that change the game: RIDING (FREER AIR and its two finer choices)
//                                                                                        ReflexOptionsMenu.OpenReflex.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.OriginalWorld;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Options
{
    public sealed partial class ReflexOptionsMenu
    {
        public static readonly string[] OptionTabs = { "CONTROLS", "AUDIO", "VIDEO", "GAMEPLAY", "CAMERA & HUD", "INTERFACE", "SYSTEM", "OPEN REFLEX" };
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        readonly MonoBehaviour _host;
        readonly VisualElement _layer, _hints;
        readonly OptNav _nav;
        readonly Action _back;
        int _tab;
        public int Tab => _tab;
        Dictionary<string, string> _strings;

        public ReflexOptionsMenu(MonoBehaviour host, VisualElement layer, VisualElement hints, OptNav nav, Action back)
        { _host = host; _layer = layer; _hints = hints; _nav = nav; _back = back; }

        /// The game's own text for a key (the front end's string table; fallback when the install has none).
        string Str(string key, string fallback)
        {
            if (_strings == null)
            {
                _strings = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (pkg, table) in new[] { ("FrontEnd", "MXRaven_Default_Strings"), ("InGame", "MXRaven_Default_Strings") })
                    try { foreach (var kv in OriginalUiAssets.Data.Strings(pkg, table)) if (!_strings.ContainsKey(kv.Key)) _strings[kv.Key] = kv.Value; }
                    catch (Exception) { }
            }
            return _strings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v.ToUpperInvariant() : fallback;
        }

        /// Called every frame while open (the CONTROLS page's listening and loading).
        public void Tick() { TickControls(); }

        public void Closing() { CloseControls(); EndCameraEditor(); }

        public void ShowOptions(int tab)
        {
            if (_tab == 0 && tab != 0) CloseControls();
            EndCameraEditor();
            _tab = (tab % OptionTabs.Length + OptionTabs.Length) % OptionTabs.Length;
            _layer.Clear();
            _layer.AddToClassList("mxa-dim");
            _layer.AddToClassList("mxa-options-open");
            var head = OptUi.Div("mxa-head", _layer);
            OptUi.Text(Str("STR_OPTIONS", "OPTIONS"), "mxa-screen-title", head);
            var tabs = OptUi.Div("mxa-tabs", head);
            for (int i = 0; i < OptionTabs.Length; i++)
            {
                int ii = i;
                var t = OptUi.Button(OptionTabs[i], "mxa-tab", () => ShowOptions(ii), tabs);
                t.EnableInClassList("mxa-tab-on", i == _tab);
                t.focusable = false;
                t.name = "tab-" + i;
            }
            var body = OptUi.Div("ou-opt-body", _layer);
            var page = OptUi.Div("mxa-panel mxa-options-page", body);
            OptUi.Text(OptionTabs[_tab], "ou-bar-title ou-opt-page-bar ou-bar", page).enableRichText = false;   // the career pages' orange title bar: the page's name
            var side = OptUi.Div("mxa-panel ou-opt-side", body);
            var sideTag = OptUi.Text("", "ou-pill ou-label", side);
            var sideTitle = OptUi.Text("", "ou-opt-side-title ou-display", side);
            var sideHelp = OptUi.Text("", "ou-opt-side-help", side);
            var sideWhen = OptUi.Text("", "ou-opt-side-when", side);   // APPLIES AT ONCE / FROM THE NEXT EVENT / FROM THE NEXT START
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.AddToClassList("mxa-options-scroll");
            sv.verticalScrollerVisibility = ScrollerVisibility.Auto;
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            page.Add(sv);
            VisualElement panel = sv.contentContainer;
            VisualElement first = null;
            switch (_tab)
            {
                case 0: first = BuildControls(panel); break;
                case 1: first = BuildAudio(panel); break;
                case 2: first = BuildVideo(panel); break;
                case 3: first = BuildGameplay(panel); break;
                case 4: first = BuildCameraHud(panel); break;
                case 5: first = BuildInterface(panel); break;
                case 6: first = BuildSystem(panel); break;
                case 7: first = BuildOpenReflex(panel); break;
            }
            CollapseNotes(panel, sideHelp, sideTitle, sideTag, sideWhen);
            _nav.OnTab = d => ShowOptions(_tab + d);
            _nav.OnCancel = () => { if (ControlsListening) return false; _back(); return true; };
            _nav.Suspended = () => ControlsListening;
            var f0 = first;
            _nav.DefaultItem = () => f0;
            OptUi.Hints(_hints, (OptUi.KeySubmit, Str("STR_SELECT", "SELECT")), (OptUi.KeyCancel, Str("STR_BACK", "BACK")), (OptUi.KeyTabs, "PAGE"));
            if (first != null) _nav.Focus(first);
        }

        // ------------------------------------------------------------------------------------------------ the pages' own conventions
        // One voice for every row (w-optcoherent, 2026-11-09):
        //   Section   a group of rows that belong together; the everyday rows first, a row that depends on another right under it.
        //   Help      the focused row's text in the side card: one or two sentences, what it does and what its values mean. When it applies
        //             is never written into the text: it is the tag under it (APPLIES AT ONCE / FROM THE NEXT EVENT / FROM THE NEXT START).
        //             The card's bar says whose option it is: the ORIGINAL GAME's own setting or an OPEN REFLEX one.
        //   Needs     a row that only works with another row's value is shown disabled with the reason on the row itself.
        //   Values    ON / OFF for switches; LOW / MEDIUM / HIGH for cost; AUTO (HIGH) = chosen for you, with what it chose; ORIGINAL = as
        //             the original game.
        // Note / Keep are the older calls (the camera editor, the first-person setups, CONTROLS' table): a note whose text ends in " Ours."
        // is an OPEN REFLEX option, one that names "the game's" setting an original one.
        enum When { None, Now, NextEvent, NextStart }
        sealed class NoteTag { public When when; public bool game; }

        static void Section(VisualElement parent, string title) => OptUi.Text(title, "mxa-panel-title mxa-options-section", parent);
        static void Note(VisualElement parent, string text) => OptUi.Text(text, "mxa-note", parent);
        /// The help of the rows above it (since the last help): text, when a change applies, and whether it is the original game's own setting.
        static void Help(VisualElement parent, string text, When when = When.Now, bool game = false) => OptUi.Text(text, "mxa-note", parent).userData = new NoteTag { when = when, game = game };
        static Label Keep(VisualElement parent, string text) { var l = OptUi.Text(text, "mxa-note mxa-keep", parent); l.enableRichText = false; return l; }
        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
        static T Named<T>(T e, string name) where T : VisualElement { e.name = name; return e; }

        /// A row that only works with another row's value: disabled while ok() is false, with the reason on the row (a disabled row takes
        /// no focus, so its help could not say why). Followed while the page is shown, since the other row changes it.
        static VisualElement Needs(VisualElement row, Func<bool> ok, string reason)
        {
            var why = OptUi.Text(reason, "ou-opt-needs", null); why.enableRichText = false;
            row.Insert(Mathf.Min(1, row.childCount), why);
            var track = row.Q<VisualElement>(className: "mxa-slider");
            void Sync()
            {
                bool on; try { on = ok(); } catch (Exception) { on = true; }
                if (row.enabledSelf != on) row.SetEnabled(on);
                why.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
                if (track != null) track.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;   // (a slider's track leaves the reason its room at UI SCALE 130 %)
            }
            Sync();
            row.schedule.Execute(Sync).Every(200);
            return row;
        }

        static readonly string[] WhenText = { "", "APPLIES AT ONCE", "APPLIES FROM THE NEXT EVENT", "APPLIES FROM THE NEXT START" };

        /// The notes under the rows become the help of those rows (shown for the focused row in the side card: its name, whether it is the
        /// original game's own setting or ours, the note and when a change applies).
        static void CollapseNotes(VisualElement page, Label help, Label title, Label tag, Label when = null)
        {
            var helpOf = new Dictionary<VisualElement, string>();
            var tagOf = new Dictionary<VisualElement, NoteTag>();
            void Walk(VisualElement parent)
            {
                var pending = new List<VisualElement>();
                foreach (var c in parent.Children().ToList())
                {
                    if (c.ClassListContains("mxa-keep")) continue;
                    if (c.ClassListContains("mxa-note"))
                    {
                        if (pending.Count == 0 || !(c is Label l)) continue;
                        foreach (var item in pending)
                        {
                            helpOf[item] = helpOf.TryGetValue(item, out var t) ? t + " " + l.text : l.text;
                            if (l.userData is NoteTag nt && !tagOf.ContainsKey(item)) tagOf[item] = nt;
                        }
                        c.style.display = DisplayStyle.None;
                        pending.Clear();
                        continue;
                    }
                    if (c.ClassListContains("mxa-panel-title")) { pending.Clear(); continue; }
                    if (c.ClassListContains("mxa-item")) { pending.Add(c); continue; }
                    var inner = c.Query<VisualElement>(className: "mxa-item").ToList();
                    if (inner.Count > 0 && !(c is ScrollView)) pending.AddRange(inner);
                    else if (c is ScrollView sv) Walk(sv.contentContainer);
                }
            }
            Walk(page);
            help.enableRichText = false; title.enableRichText = false; tag.enableRichText = false; if (when != null) when.enableRichText = false;
            void Show(string t, string name, string whose, bool ours, When w)
            {
                help.text = t; title.text = name; tag.text = whose;
                tag.EnableInClassList("ou-pill-gold", ours);
                if (when == null) return;   // (the camera and first-person setup editors: no tag)
                when.text = WhenText[(int)w];
                when.style.display = w == When.None ? DisplayStyle.None : DisplayStyle.Flex;
                when.EnableInClassList("ou-opt-side-when-later", w == When.NextEvent || w == When.NextStart);
            }
            Show("", "", "", false, When.None);
            page.RegisterCallback<FocusInEvent>(e =>
            {
                for (var v = e.target as VisualElement; v != null; v = v.parent)
                    if (helpOf.TryGetValue(v, out var t))
                    {
                        var label = v.Q<Label>(className: "mxa-row-label") ?? v.Q<Label>(className: "mxa-button-label") ?? v.parent?.Q<Label>(className: "mxa-row-label");   // (a cell of CONTROLS' table: its action)
                        string name = label != null ? label.text : "";
                        if (tagOf.TryGetValue(v, out var nt)) { Show(t, name, nt.game ? "ORIGINAL GAME OPTION" : "OPEN REFLEX OPTION", !nt.game, nt.when); return; }
                        bool ours = t.EndsWith(" Ours.", StringComparison.Ordinal);
                        if (ours) t = t.Substring(0, t.Length - 6);
                        bool game = !ours && (t.StartsWith("The game's", StringComparison.Ordinal) || t.Contains("the game's"));
                        Show(t, name, game ? "ORIGINAL GAME OPTION" : "OPEN REFLEX OPTION", !game, When.None);
                        return;
                    }
                // a row without help (rows another part adds): its name, so the card is never an empty bar
                for (var v = e.target as VisualElement; v != null; v = v.parent)
                    if (v.ClassListContains("mxa-item"))
                    {
                        var label = v.Q<Label>(className: "mxa-row-label") ?? v.Q<Label>(className: "mxa-button-label") ?? v.parent?.Q<Label>(className: "mxa-row-label");
                        Show("", label != null ? label.text : "", "OPTION", false, When.None);
                        return;
                    }
                Show("", "", "", false, When.None);
            }, TrickleDown.TrickleDown);
        }

        static string Cycle<T>(T[] values, T current, int d, out T next)
        {
            int i = Array.IndexOf(values, current); if (i < 0) i = 0;
            next = values[((i + d) % values.Length + values.Length) % values.Length];
            return null;
        }

        // ------------------------------------------------------------------------------------------------ AUDIO
        VisualElement BuildAudio(VisualElement panel)
        {
            Section(panel, "VOLUME");
            var first = Named(OptUi.Slider("MASTER VOLUME", () => ReflexSettings.MasterVolume, ReflexClientLive.MasterVolume, panel, 0f, 1f, 0.1f), "master-volume");
            Help(panel, "Every sound at once, over the volumes below.");
            Named(OptUi.Slider(Str("STR_OPT_EFFECTS_VOLUME", "EFFECTS VOLUME"), () => Mathf.Clamp01(ReflexSettings.EffectsVolume), ReflexClientLive.EffectsVolume, panel, 0f, 1f, 0.1f), "effects-volume");
            Help(panel, "Crashes, riders, the crowd, the race and menu sounds.", When.Now, true);
            Named(OptUi.Slider(Str("STR_OPT_ENGINE_VOLUME", "ENGINE VOLUME"), () => ReflexSettings.EngineVolume / 10f, v => { ReflexSettings.EngineVolume = Mathf.RoundToInt(v * 10f); ReflexSettings.Save(); }, panel, 0f, 1f, 0.1f), "engine-volume");
            Help(panel, "The engine of every bike, ATV and UTV.", When.Now, true);
            Named(OptUi.Slider("CROWD VOLUME", () => ReflexSettings.CrowdVolume, v => { ReflexSettings.CrowdVolume = v; ReflexSettings.Save(); }, panel, 0f, 1f, 0.1f), "crowd-volume");
            Help(panel, "The crowd's share of EFFECTS VOLUME.");
            Section(panel, "MUSIC");
            Named(OptUi.Toggle("STREAMER MODE", () => ReflexSettings.StreamerMode, ReflexClientLive.StreamerMode, panel), "streamer-mode");
            Help(panel, "For streams and recordings: no music at all (the soundtrack in the menus, races and replays, YOUR MUSIC, the videos' sound), whatever MUSIC says. Engines, effects and the crowd stay.", When.Now, true);
            Named(OptUi.Toggle(Str("STR_OPT_MUSIC", "MUSIC"), () => ReflexSettings.MusicOn, ReflexClientLive.MusicOn, panel), "music-on");
            Help(panel, "The soundtrack in the menus and the races. M switches it in a race.", When.Now, true);
            Needs(Named(OptUi.Slider(Str("STR_OPT_MUSIC_VOLUME", "MUSIC VOLUME"), () => ReflexSettings.MusicVolume, ReflexClientLive.MusicVolume, panel, 0f, 1f, 0.1f), "music-volume"),
                  () => ReflexSettings.MusicOn, Str("STR_OPT_MUSIC", "MUSIC") + " IS OFF");
            Help(panel, "How loud the soundtrack plays.", When.Now, true);
            MusicRows(panel);
            Section(panel, "ACOUSTICS");
            Named(OptUi.Toggle("REVERB", () => ReflexSettings.AudioReverb, v => { ReflexSettings.AudioReverb = v; ReflexSettings.Save(); }, panel), "audio-reverb");
            Help(panel, "The original's echo in stadiums, arenas, caves, pipes and warehouses. OFF plays every sound dry.");
            Named(OptUi.Toggle("FIRST-PERSON REVERB", () => ReflexSettings.AudioReverbFirstPerson, v => { ReflexSettings.AudioReverbFirstPerson = v; ReflexSettings.Save(); }, panel), "audio-reverb-first-person");
            Help(panel, "The original's short, muffled helmet reverb in the first-person camera. OFF keeps only the track's own reverb.");
            if (ReflexOptionsHooks.AudioExtras != null)
            {
                Section(panel, "ENGINE SOUNDS");
                try { ReflexOptionsHooks.AudioExtras(panel); } catch (Exception e) { Debug.LogWarning("[Options] audio extras: " + e.Message); }
            }
            ResetSection(panel, "AUDIO", "reset-audio", "Puts every row of this page back to its default: the volumes, STREAMER MODE, the music and the reverb. Your engine sound picks stay.",
                         "Every volume, STREAMER MODE, the music rows and the reverb go back to their defaults. Your engine sound picks stay.", ResetAudio);
            return first;
        }

        // ------------------------------------------------------------------------------------------------ VIDEO
        // DISPLAY                    resolution, display mode, aspect ratio; frame rate cap, vsync, frame pacing, low latency
        // QUALITY PRESET             PRESET: sets the rows of the three sections under it; changing one of those rows shows CUSTOM
        // LIGHTING AND EFFECTS       lighting quality (+ the original's shadow quality), volumetric light (+ its quality, fog density), speed blur,
        //                            the original's motion blur, vehicle materials, mud buildup
        // TEXTURES AND DETAIL        texture quality, texture filtering, terrain detail, draw distance
        // ANTI-ALIASING AND SCALING  anti-aliasing (+ TAA's sharpening and upscaling), render scale        ReflexOptionsMenu.AntiAliasing.cs
        // BRIGHTNESS                 the original's brightness, gamma and screen adjustment page
        // ADVANCED                   performance overlay, engine quality level, where graphics.cfg is
        // The lighting is always ENHANCED (Rendering/ReflexEnhancedLighting): there is no LIGHTING: ORIGINAL row any more (2026-11-09).
        public static readonly string[] PresetNames = { "CUSTOM", "AUTO", "PERFORMANCE", "BALANCED", "QUALITY" };
        /// PRESET (OPTIONS > VIDEO): 0 AUTO (PERFORMANCE / BALANCED / QUALITY by ReflexEnhancedLighting.AutoTier, with LIGHTING QUALITY left on
        /// AUTO), 1 PERFORMANCE, 2 BALANCED, 3 QUALITY; applied at once and saved.
        ///                 LIGHTING QUALITY  VOLUMETRIC LIGHT  ANTI-ALIASING  TEXTURE FILTERING  DRAW DISTANCE  SPEED BLUR
        ///   PERFORMANCE   LOW               OFF               OFF            ORIGINAL           ORIGINAL       OFF
        ///   BALANCED      MEDIUM            ON                OFF            8X                 HIGH           (kept)
        ///   QUALITY       HIGH              ON                TAA            16X                HIGH           (kept)
        /// (Until 2026-11-09 PERFORMANCE meant LIGHTING: ORIGINAL; ENHANCED is the only lighting now, so it is ENHANCED at LOW.)
        public static void ApplyPreset(int preset)
        {
            preset = Mathf.Clamp(preset, 0, 3);
            int kind = preset == 0 ? 3 - Rendering.ReflexEnhancedLighting.AutoTier : preset;   // AUTO: HIGH tier -> QUALITY ... LOW -> PERFORMANCE
            ReflexSettings.EnhancedQuality = preset == 0 ? -1 : kind - 1;
            Rendering.ReflexVolumetricLight.Option = kind == 1 ? 0 : 1;
            Rendering.ReflexVolumetricLight.QualityOption = -1;
            if (kind == 1) Rendering.ReflexSpeedBlur.Setting = 0;
            ReflexSettings.DrawDistance = kind == 1 ? 0 : 1;
            ReflexSettings.TextureFiltering = kind == 1 ? 0 : kind == 2 ? 2 : 3; Rendering.ReflexTextureFiltering.Refresh();
            Rendering.ReflexAntiAliasing.Set(kind == 3 ? Rendering.ReflexAntiAliasing.Mode.Taa : Rendering.ReflexAntiAliasing.Mode.Off);   // (saves graphics.cfg, applies at once)
            ReflexSave.SetInt("Options.Preset", preset);
            ReflexSettings.Save();
        }
        /// A row the PRESET sets was changed by hand: the PRESET row shows CUSTOM.
        static void PresetCustom() { if (ReflexSave.GetInt("Options.Preset", -1) != -1) ReflexSave.SetInt("Options.Preset", -1); }

        VisualElement BuildVideo(VisualElement panel)
        {
            var ls = ReflexLaunchSettings.Current;
            void SaveCfg(Action<ReflexLaunchSettings> set, bool applyNow, bool applyGraphics = false)
            {
                var s = ReflexLaunchSettings.Current; set(s);
                try { s.Save(); } catch (Exception e) { Debug.LogWarning("[Options] graphics.cfg not written: " + e.Message); }
                if (applyNow) ReflexClientLive.ApplyDisplay();
                if (applyGraphics) ReflexClientLive.ApplyGraphicsLive();
            }
            string later = ReflexClientLive.CanApplyDisplay ? "" : " (This run only stores it.)";
            string[] lmh = { "LOW", "MEDIUM", "HIGH" };
            string Auto(int option, int inEffect) => option < 0 ? "AUTO (" + lmh[Mathf.Clamp(inEffect, 0, 2)] + ")" : lmh[Mathf.Clamp(option, 0, 2)];

            // ---------------------------------------------------------------- DISPLAY
            Section(panel, "DISPLAY");
            var res = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct().OrderBy(r => r.x * 10000 + r.y).ToList();
            foreach (var std in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) }) if (!res.Contains(std)) res.Add(std);
            res = res.OrderBy(r => r.x * 10000 + r.y).ToList();
            var first = Named(OptUi.Spinner("RESOLUTION", () => { var c = ReflexLaunchSettings.Current; return $"{c.width} × {c.height}"; }, d =>
            {
                var c = ReflexLaunchSettings.Current; var cur = new Vector2Int(c.width, c.height);
                int i = res.IndexOf(cur); if (i < 0) i = res.FindLastIndex(r => r.x * 10000 + r.y <= cur.x * 10000 + cur.y); if (i < 0) i = 0;
                var n = res[((i + d) % res.Count + res.Count) % res.Count];
                SaveCfg(s => { s.width = n.x; s.height = n.y; }, true);
            }, panel), "resolution");
            Help(panel, "The size the game draws at. In a window with ASPECT RATIO: ORIGINAL 16:9 it is the largest 16:9 picture that fits." + later, When.Now, true);
            Named(OptUi.Toggle("DISPLAY MODE", () => ReflexLaunchSettings.Current.fullscreen, v => SaveCfg(s => s.fullscreen = v, true), panel, "FULLSCREEN", "WINDOWED"), "display-mode");
            Help(panel, "FULLSCREEN uses the whole screen. WINDOWED runs the game in a window." + later, When.Now, true);
            Named(OptUi.Toggle("ASPECT RATIO", () => ReflexSettings.AspectFill == 1, v =>
            {
                ReflexSettings.AspectFill = v ? 1 : 0; ReflexSettings.Save();
                ReflexClientLive.ApplyDisplay(); Reflex.Port.Rendering.ReflexRenderPipeline.ReapplyViewports();
            }, panel, "FILL SCREEN", "ORIGINAL 16:9"), "aspect-ratio");
            Help(panel, "FILL SCREEN uses all of an ultrawide or other non-16:9 screen: the same height of view, wider at the sides. ORIGINAL 16:9 keeps the original's picture with black bars." + later);
            double hz = Screen.currentResolution.refreshRateRatio.value;
            string monitor = hz > 1 ? $" ({hz:0} Hz)" : "";
            int launchCap = ReflexClientLive.LaunchFrameRateCap;
            var capRow = Named(OptUi.Spinner("FRAME RATE CAP", () => launchCap > 0 ? launchCap + " FPS (LAUNCH)" : ReflexSettings.FpsCap > 0 ? ReflexSettings.FpsCap + " FPS" : ReflexSettings.FpsCap == ReflexSettings.DisplayRate ? "DISPLAY RATE" : "OFF", d =>
            {
                Cycle(ReflexSettings.FpsCaps, ReflexSettings.FpsCap, d, out int n);
                ReflexSettings.FpsCap = n; ReflexSettings.Save(); ReflexClientLive.ApplyFrameRate();
            }, panel), "frame-rate-cap");
            Needs(capRow, () => launchCap == 0, "SET AT LAUNCH: REFLEX_FPS_CAP");
            Help(panel, $"DISPLAY RATE draws one frame for each refresh of your monitor{monitor}. A number caps the frame rate. OFF leaves it to VSYNC.");
            Needs(Named(OptUi.Toggle("VSYNC", () => ReflexLaunchSettings.Current.vsync, v => SaveCfg(s => s.vsync = v, true), panel), "vsync"),
                  () => launchCap == 0 && ReflexSettings.FpsCap == 0, "FRAME RATE CAP DECIDES");
            Help(panel, "Waits for your monitor's refresh, so the picture never tears. Used while FRAME RATE CAP is OFF; your choice is kept." + later, When.Now, true);
            Named(OptUi.Toggle("FRAME PACING", () => ReflexSettings.FramePacing == 1, v => { ReflexSettings.FramePacing = v ? 1 : 0; ReflexSettings.Save(); }, panel, "SMOOTH", "ORIGINAL"), "frame-pacing");
            Help(panel, "ORIGINAL runs the game's own 60 Hz loop: the lowest input delay. SMOOTH blends its frames to your display's refresh rate, one game frame later.", When.NextEvent);
            Named(OptUi.Toggle("LOW LATENCY", () => ReflexSettings.LowLatency == 1, v => { ReflexSettings.LowLatency = v ? 1 : 0; ReflexSettings.Save(); ReflexClientLive.ApplyLatency(); }, panel), "low-latency");
            Help(panel, "ON lets the graphics driver hold one finished frame instead of two: up to one refresh less input delay with VSync. If that would halve the frame rate, the game steps back by itself.");

            // ---------------------------------------------------------------- QUALITY PRESET
            Section(panel, "QUALITY PRESET");
            Named(OptUi.Spinner("PRESET", () => PresetNames[Mathf.Clamp(ReflexSave.GetInt("Options.Preset", -1) + 1, 0, PresetNames.Length - 1)], d =>
            {
                int cur = Mathf.Clamp(ReflexSave.GetInt("Options.Preset", -1), -1, PresetNames.Length - 2);
                int next = ((cur + d) % (PresetNames.Length - 1) + (PresetNames.Length - 1)) % (PresetNames.Length - 1);   // AUTO .. QUALITY (not CUSTOM)
                ApplyPreset(next);
            }, panel), "preset");
            Help(panel, "Sets LIGHTING QUALITY, VOLUMETRIC LIGHT, ANTI-ALIASING, TEXTURE FILTERING and DRAW DISTANCE together. AUTO chooses for your graphics card. PERFORMANCE is the fastest and also turns SPEED BLUR off.");

            // ---------------------------------------------------------------- LIGHTING AND EFFECTS
            Section(panel, "LIGHTING AND EFFECTS");
            Named(OptUi.Spinner("LIGHTING QUALITY", () => Auto(ReflexSettings.EnhancedQuality, Rendering.ReflexEnhancedLighting.Tier), d =>
            {
                int n = ((ReflexSettings.EnhancedQuality + 1 + d) % 4 + 4) % 4 - 1;   // AUTO, LOW, MEDIUM, HIGH
                ReflexSettings.EnhancedQuality = n; PresetCustom(); ReflexSettings.Save();   // (ReflexEnhancedLighting follows the tier)
            }, panel), "enhanced-quality");
            Help(panel, "The cost of the sun shadows, ambient occlusion and contact shadows. LOW: shorter shadows, no ambient occlusion. AUTO follows SHADOW QUALITY, never above what your graphics card suits.");
            Named(OptUi.Spinner("SHADOW QUALITY", () => lmh[Mathf.Clamp(ReflexLaunchSettings.Current.shadowQuality, 0, 2)], d => SaveCfg(s => s.shadowQuality = ((s.shadowQuality + d) % 3 + 3) % 3, false), panel), "shadow-quality");
            Help(panel, "The original game's shadow map quality. LIGHTING QUALITY: AUTO follows it at once; the shadow maps themselves are made when the game starts.", When.NextStart, true);
            Named(OptUi.Toggle("VOLUMETRIC LIGHT", () => Rendering.ReflexVolumetricLight.Option == 1, v => { Rendering.ReflexVolumetricLight.Option = v ? 1 : 0; PresetCustom(); ReflexSave.Save(); }, panel), "volumetric-light");
            Help(panel, "Real light shafts and haze: sunlight through trees and grandstands, the stadium spotlights as cones of light. OFF shows the original's painted beams.");
            Needs(Named(OptUi.Spinner("VOLUMETRIC QUALITY", () => Auto(Rendering.ReflexVolumetricLight.QualityOption, Rendering.ReflexVolumetricLight.Tier), d =>
            {
                Rendering.ReflexVolumetricLight.QualityOption = ((Rendering.ReflexVolumetricLight.QualityOption + 1 + d) % 4 + 4) % 4 - 1; ReflexSave.Save();
            }, panel), "volumetric-quality"), () => Rendering.ReflexVolumetricLight.Option == 1, "VOLUMETRIC LIGHT IS OFF");
            Help(panel, "The cost of VOLUMETRIC LIGHT. AUTO follows LIGHTING QUALITY.");
            Needs(Named(OptUi.Spinner("FOG DENSITY", () => lmh[Rendering.ReflexVolumetricLight.DensityOption], d =>
            {
                Rendering.ReflexVolumetricLight.DensityOption = ((Rendering.ReflexVolumetricLight.DensityOption + d) % 3 + 3) % 3; ReflexSave.Save();
            }, panel), "fog-density"), () => Rendering.ReflexVolumetricLight.Option == 1, "VOLUMETRIC LIGHT IS OFF");
            Help(panel, "How thick the air is in VOLUMETRIC LIGHT: more haze and stronger shafts at HIGH.");
            Named(OptUi.Spinner("SPEED BLUR", () => Rendering.ReflexSpeedBlur.Names[Rendering.ReflexSpeedBlur.Setting], d => { Rendering.ReflexSpeedBlur.Setting = ((Rendering.ReflexSpeedBlur.Setting + d) % 3 + 3) % 3; PresetCustom(); ReflexSave.Save(); }, panel), "speed-blur");
            Help(panel, "Motion blur that grows with your speed, from about 15 mph to full at 65 mph: the ground and the wheels streak, the bike stays sharp. Replays and photo mode have their own.");
            Named(OptUi.Toggle("MOTION BLUR", () => ReflexLaunchSettings.Current.motionBlur, v => SaveCfg(s => s.motionBlur = v, false, true), panel), "motion-blur");
            Help(panel, "The original game's own motion blur.", When.Now, true);
            Named(OptUi.Toggle("VEHICLE MATERIALS", () => Rendering.ReflexEnhancedMaterials.MaterialsShown, v => Rendering.ReflexEnhancedMaterials.PickMaterials(v), panel, "ENHANCED", "ORIGINAL"), "materials");
            Help(panel, "ENHANCED shades bikes, ATVs, UTVs and riders as real surfaces: clear-coated plastics, metal and chrome that reflect the sky, rubber, fabric. ORIGINAL keeps the original's shading.");
            Named(OptUi.Toggle("MUD BUILDUP", () => Rendering.ReflexEnhancedMaterials.MudSetting == 1, v => Rendering.ReflexEnhancedMaterials.PickMud(v), panel), "mud-buildup");
            Help(panel, "ON: mud collects on the wheels, frame, boots and legs as you ride on dirt and washes off in water; a restart cleans it. OFF: bikes and riders stay clean.");

            // ---------------------------------------------------------------- TEXTURES AND DETAIL
            Section(panel, "TEXTURES AND DETAIL");
            Named(OptUi.Spinner("TEXTURE QUALITY", () => lmh[Mathf.Clamp(ReflexLaunchSettings.Current.textureQuality, 0, 2)], d => SaveCfg(s => s.textureQuality = ((s.textureQuality + d) % 3 + 3) % 3, false), panel), "texture-quality");
            Help(panel, "The original game's texture resolution. The textures are made when the game starts.", When.NextStart, true);
            Named(OptUi.Spinner("TEXTURE FILTERING", () => Rendering.ReflexTextureFiltering.Names[ReflexSettings.TextureFiltering], d =>
            {
                ReflexSettings.TextureFiltering = ((ReflexSettings.TextureFiltering + d) % 4 + 4) % 4; PresetCustom(); ReflexSettings.Save(); Rendering.ReflexTextureFiltering.Refresh();
            }, panel), "texture-filtering");
            Help(panel, "Anisotropic filtering: keeps the ground, banners and fences sharp at shallow angles. ORIGINAL is the original's sampling. Almost free on a modern graphics card.");
            Named(OptUi.Toggle("TERRAIN DETAIL", () => ReflexSettings.TerrainDetail == 1, v =>
            {
                ReflexSettings.TerrainDetail = v ? 1 : 0; ReflexSettings.Save();
                foreach (var t in Terrain.activeTerrains) if (t) t.heightmapPixelError = ReflexSettings.TerrainPixelError;
            }, panel, "HIGH", "STANDARD"), "terrain-detail");
            Help(panel, "HIGH keeps the terrain at full resolution much further from the camera, as the original drew it. STANDARD draws far less terrain geometry: faster on a slow graphics card.");
            Named(OptUi.Toggle("TERRAIN TESSELLATION", () => ReflexSettings.TerrainTessellation == 1, v => { ReflexSettings.TerrainTessellation = v ? 1 : 0; ReflexSettings.Save(); }, panel), "terrain-tessellation");
            Help(panel, "Draws the ground around you on the original game's fine grid, so ruts, tyre marks and the dirt's bumps stand up from the ground instead of being flat shading. Costs a little on a slow graphics card.");
            string[] bd = { "ORIGINAL", "HIGH", "HIGH: ALL RIDERS" };
            Named(OptUi.Spinner("BIKE AND RIDER DETAIL", () => bd[ReflexSettings.BikeDetail], d => { ReflexSettings.BikeDetail = ((ReflexSettings.BikeDetail + d) % 3 + 3) % 3; ReflexSettings.Save(); }, panel), "bike-detail");
            Help(panel, "ORIGINAL draws the race models, as the original game does. HIGH draws your own vehicle and rider with the garage's high-detail models on the track. HIGH: ALL RIDERS does the same for riders near the camera; costs more in a full gate. Takes effect from the next race.");
            string[] dd = { "ORIGINAL", "HIGH", "ULTRA" };
            Named(OptUi.Spinner("DRAW DISTANCE", () => dd[ReflexSettings.DrawDistance], d => { ReflexSettings.DrawDistance = ((ReflexSettings.DrawDistance + d) % 3 + 3) % 3; PresetCustom(); ReflexSettings.Save(); }, panel), "draw-distance");
            Help(panel, "How far trees, scenery, grass and shadows are drawn in detail. ORIGINAL is the original's distances and hard detail switches. HIGH and ULTRA reach further and cross-fade; ULTRA costs the most.");

            // ---------------------------------------------------------------- ANTI-ALIASING AND SCALING
            Section(panel, "ANTI-ALIASING AND SCALING");
            BuildAntiAliasing(panel);   // ANTI-ALIASING (OFF / MSAA 2X / MSAA 4X / TAA), SHARPENING, UPSCALING: ReflexOptionsMenu.AntiAliasing.cs
            Named(OptUi.Spinner("RENDER SCALE", () => ReflexSettings.RenderScale + "%", d =>
            {
                Cycle(ReflexSettings.RenderScales, ReflexSettings.RenderScale, d, out int n);
                ReflexSettings.RenderScale = n; ReflexSettings.Save(); ReflexClientLive.ApplyQuality();
            }, panel), "render-scale");
            Help(panel, "Draws the 3D picture at this share of the resolution: below 100% is faster and softer, above it sharper and slower." + later);

            // ---------------------------------------------------------------- BRIGHTNESS
            Section(panel, "BRIGHTNESS");
            Named(OptUi.Slider(Str("STR_OPT_BRIGHTNESS", "BRIGHTNESS"), () => (ReflexSave.GetInt("ReflexUI.Brightness", 50) - 45) / 10f,
                v => { ReflexSave.SetInt("ReflexUI.Brightness", 45 + Mathf.RoundToInt(v * 10f)); ReflexSave.Save(); Reflex.Port.FrontEnd.ReflexScreenAdjustment.Apply(); }, panel, 0f, 1f, 0.1f), "brightness");
            Help(panel, "How bright the whole picture is: the original game's screen adjustment.", When.Now, true);
            Named(OptUi.Toggle(Str("STR_OPT_GAMMA", "GAMMA"), () => ReflexSave.GetInt("ReflexUI.Gamma", 0) != 0,
                v => { ReflexSave.SetInt("ReflexUI.Gamma", v ? 1 : 0); ReflexSave.Save(); Reflex.Port.FrontEnd.ReflexScreenAdjustment.Apply(); }, panel, Str("STR_OPT_GAMMA_LCD", "LCD"), Str("STR_OPT_GAMMA_CRT", "CRT")), "gamma");
            Help(panel, "The original game's gamma curve for the kind of screen you have.", When.Now, true);
            Named(OptUi.Button(Str("STR_SCREENADJUSTMENT_TITLE", "SCREEN ADJUSTMENT"), "mxa-link", () => ShowScreenAdjustment(false), panel), "screen-adjustment");
            Help(panel, "The original game's adjustment page: grey steps to set BRIGHTNESS and GAMMA against.", When.None, true);

            // ---------------------------------------------------------------- ADVANCED
            Section(panel, "ADVANCED");
            Named(OptUi.Toggle("PERFORMANCE OVERLAY", () => ReflexSettings.PerfOverlay, v => { ReflexSettings.PerfOverlay = v; ReflexSettings.Save(); Platform.ReflexPerfOverlay.Apply(v); }, panel), "perf-overlay");
            Help(panel, "A corner panel with the frame rate and frame time, the CPU and GPU times, the game's own frame rate, the draw calls and the lighting quality in use.");
            var qnames = QualitySettings.names;
            Named(OptUi.Spinner("ENGINE QUALITY LEVEL", () => ReflexSettings.Quality < 0 || ReflexSettings.Quality >= qnames.Length ? "DEFAULT" : qnames[ReflexSettings.Quality].ToUpperInvariant(), d =>
            {
                int n = qnames.Length + 1, i = Mathf.Clamp(ReflexSettings.Quality, -1, qnames.Length - 1) + 1;
                ReflexSettings.Quality = ((i + d) % n + n) % n - 1; ReflexSettings.Save(); ReflexClientLive.ApplyQuality();
            }, panel), "quality");
            Help(panel, "The engine's own quality level under every row above. DEFAULT is the one the game is built and tuned for; leave it there unless you are told otherwise." + later);
            Keep(panel, "Your graphics.cfg: " + (ls.loaded ? ReflexSave.Redact(ls.source) : "none yet (the game's defaults)") + ". The install's own file is never changed.");
            ResetSection(panel, "VIDEO", "reset-video", "Puts every row of this page back to its default, except RESOLUTION and DISPLAY MODE. The original game's own rows go back to your install's graphics.cfg; TEXTURE QUALITY and SHADOW QUALITY from the next start.",
                         "Every video option goes back to its default, except RESOLUTION and DISPLAY MODE. The original game's own rows take your install's graphics.cfg again; TEXTURE QUALITY and SHADOW QUALITY change from the next start.", ResetVideo);
            return first;
        }

        // ------------------------------------------------------------------------------------------------ GAMEPLAY
        VisualElement BuildGameplay(VisualElement panel)
        {
            Section(panel, "GAME");
            var first = Named(OptUi.Toggle(Str("STR_OPT_MEASUREMENT_SYSTEM", "UNITS"), () => ReflexSettings.Metric, v => { ReflexSettings.Metric = v; ReflexSettings.Save(); }, panel,
                Str("STR_OPT_MS_METRIC", "METRIC") + "  (KM/H)", Str("STR_OPT_MS_US", "US") + "  (MPH)"), "units");
            Help(panel, "The speeds and distances in the game's pages and on the HUD.", When.Now, true);
            string[] diffKeys = { "STR_AI_LEVEL1", "STR_AI_LEVEL2", "STR_AI_LEVEL3", "STR_AI_LEVEL4" }, diffNames = { "ROOKIE", "AMATEUR", "PRO", "ALL TIME" };
            Named(OptUi.Spinner(Str("STR_OPT_CAREER_DIFFICULTY", "CAREER DIFFICULTY"), () =>
            {
                int i = Array.IndexOf(ReflexSettings.CareerDifficulties, ReflexSettings.CareerDifficulty); if (i < 0) i = 0;
                return Str(diffKeys[i], diffNames[i]);
            }, d =>
            {
                Cycle(ReflexSettings.CareerDifficulties, ReflexSettings.CareerDifficulty, d, out int n);
                ReflexSettings.CareerDifficulty = n; ReflexSettings.Save();
            }, panel), "career-difficulty");
            Help(panel, "How fast the computer riders of the MOTOCAREER are.", When.NextEvent, true);
            Named(OptUi.Toggle(Str("STR_OPT_ONLINE_PLAYER_NAME", "ONLINE PLAYER NAMES"), () => ReflexSettings.OnlinePlayerName, v =>
            {
                ReflexSettings.OnlinePlayerName = v; ReflexClientLive.GameSetting("OnlinePlayerName", v ? 0 : 1); ReflexSettings.Save();
            }, panel), "online-player-name");
            Help(panel, "Shows the other riders' names over their bikes in an online race. No effect offline.", When.Now, true);
            if (Reflex.Port.RxCore.Bridge.OpenReflexSelector.UseOpenReflex) { GameplayReset(panel); return first; }   // the game's functions run natively: nothing to choose
            Section(panel, "NATIVE GAME");
            string[] modes = { "OFF", "NATIVE", "VERIFY" };
            Named(OptUi.Spinner("NATIVE GAME", () =>
            {
                int v = ReflexSettings.NativeGame;
                var rt = ReflexClientLive.Runtime;
                int eff = v >= 0 ? v : rt ? (int)rt.nativeGame : (int)OriginalWorldRuntime.NativeGameMode.Native;
                return modes[Mathf.Clamp(eff, 0, 2)] + (v < 0 ? "  (DEFAULT)" : "");
            }, d =>
            {
                var rt = ReflexClientLive.Runtime;
                int v = ReflexSettings.NativeGame; if (v < 0) v = rt ? (int)rt.nativeGame : 1;
                ReflexSettings.NativeGame = ((v + d) % 3 + 3) % 3; ReflexSettings.Save();
            }, panel), "native-game");
            Help(panel, "The game's functions rewritten in C++. OFF runs the original code everywhere, NATIVE the rewrites, VERIFY both and counts the differences in the log. REFLEX_NATIVE_GAME overrides it.", When.NextEvent);
            Named(OptUi.Button("USE THE DEFAULT", "mxa-link", () => { ReflexSettings.NativeGame = -1; ReflexSettings.Save(); ShowOptions(3); }, panel), "native-default");
            Help(panel, "Forgets the choice: the race's own setting (NATIVE).", When.None);
            GameplayReset(panel);
            return first;
        }
        void GameplayReset(VisualElement panel) =>
            ResetSection(panel, "GAMEPLAY", "reset-gameplay", "Puts every row of this page back to its default: UNITS (US), CAREER DIFFICULTY (ROOKIE), ONLINE PLAYER NAMES (ON).",
                         "UNITS, CAREER DIFFICULTY and ONLINE PLAYER NAMES go back to their defaults. A career's progress is not touched.", ResetGameplay);

        // ------------------------------------------------------------------------------------------------ CAMERA & HUD
        // CAMERA (the camera in use), HUD, then the long lists: CAMERA CYCLE, MY CAMERAS and CAMERA SHARING, FIRST-PERSON VIEW.
        VisualElement BuildCameraHud(VisualElement panel)
        {
            Section(panel, "CAMERA");
            var first = Named(OptUi.Spinner(Str("STR_OPT_DRIVING_CAMERA", "DRIVING CAMERA"), () => CameraEntryName(ReflexCameraPresets.Current), d => ReflexClientLive.CycleCamera(d), panel), "driving-camera");
            Help(panel, "The camera in use, and the one every event starts on. In a race the Camera action and F7 go through the cameras that are ON in CAMERA CYCLE below.");
            string[] cams = { "GAME CAMERA", "DYNAMIC FPV", "FOLLOW CAMERA" };
            Named(OptUi.Spinner("CAMERA TYPE", () => { var rt = ReflexClientLive.Runtime; return cams[Mathf.Clamp(rt ? rt.cameraMode : ReflexSettings.CameraMode, 0, 2)]; }, d =>
            {
                var rt = ReflexClientLive.Runtime; int m = rt ? rt.cameraMode : ReflexSettings.CameraMode;
                ReflexClientLive.CameraMode(((m + d) % 3 + 3) % 3);
            }, panel), "camera-mode");
            Help(panel, "GAME CAMERA: the original's views and your own cameras over them. DYNAMIC FPV: the first-person view set up below. FOLLOW CAMERA: a plain chase view.");
            Named(OptUi.Spinner("CHASE CAMERA FOV", () =>
            {
                int p = ReflexSettings.ChaseFov;
                return p == 100 ? "100%  (ORIGINAL)" : p + "%  (" + (p > 100 ? "+" : "") + (p - 100) + ")";
            }, d => { ReflexSettings.ChaseFov = ReflexSettings.ChaseFov + d; ReflexSettings.Save(); }, panel), "chase-fov");
            Help(panel, "How wide the original's views see; 100% is the original field of view. Your own cameras and the first-person view have their own.");
            DefaultsRow(panel, "CAMERA", "reset-camera", "Puts the three rows above back to their defaults: the game's VISUAL camera and CHASE CAMERA FOV 100%. MY CAMERAS and the CAMERA CYCLE stay.",
                     "The driving camera goes back to the game's VISUAL camera and CHASE CAMERA FOV to 100%. Your own cameras, the camera cycle and the first-person setup stay.", ResetCamera);

            Section(panel, "HUD");
            bool Modern() => ReflexSettings.HudStyle == HudStyle.Modern;
            Named(OptUi.Spinner("HUD STYLE", () => ReflexSettings.HudStyleNames[(int)ReflexSettings.HudStyle], d => { ReflexSettings.HudStyle = ReflexSettings.HudStyle == HudStyle.Modern ? HudStyle.Original : HudStyle.Modern; ReflexSettings.Save(); }, panel), "hud-style");
            Help(panel, "MODERN: the position board, lap and time panels, speed and gear, and a panel for every event type. ORIGINAL: the original game's HUD. H hides either in a race.");
            Named(OptUi.Toggle(Str("STR_OPT_HUD_VISIBILITY", "HUD VISIBILITY"), () => { var rt = ReflexClientLive.Runtime; return rt ? rt.OriginalHudVisible : ReflexSettings.HudVisible; }, ReflexClientLive.HudVisible, panel), "hud-visible");
            Help(panel, "Shows or hides the HUD, in either style.", When.Now, true);
            Needs(Named(OptUi.Spinner("HUD SCALE", () => ReflexSettings.HudScale + "%", d => { ReflexSettings.HudScale = ReflexSettings.HudScale + d * 5; ReflexSettings.Save(); }, panel), "hud-scale"), Modern, "MODERN HUD ONLY");
            Help(panel, "The size of the MODERN HUD, 70% to 150%.");
            Needs(Named(OptUi.Spinner("HUD OPACITY", () => ReflexSettings.HudOpacity + "%", d => { ReflexSettings.HudOpacity = ReflexSettings.HudOpacity + d * 10; ReflexSettings.Save(); }, panel), "hud-opacity"), Modern, "MODERN HUD ONLY");
            Help(panel, "How solid the MODERN HUD's panels are, 40% to 100%. The numbers stay bright.");
            Needs(Named(OptUi.Toggle("STANDINGS BOARD", () => ReflexSettings.HudStandings, v => { ReflexSettings.HudStandings = v; ReflexSettings.Save(); }, panel), "hud-standings"), Modern, "MODERN HUD ONLY");
            Help(panel, "Your position and the riders around you, top left.");
            Needs(Named(OptUi.Toggle("SPEED AND GEAR", () => ReflexSettings.HudGauge, v => { ReflexSettings.HudGauge = v; ReflexSettings.Save(); }, panel), "hud-gauge"), Modern, "MODERN HUD ONLY");
            Help(panel, "The speed and gear gauge, bottom right. Its units follow GAMEPLAY > UNITS.");
            Needs(Named(OptUi.Toggle("GAUGE ON ORIGINAL HUD", () => ReflexSettings.ClassicSpeedometer, value => { ReflexSettings.ClassicSpeedometer = value; ReflexSettings.Save(); }, panel), "classic-speedometer"), () => !Modern(), "ORIGINAL HUD ONLY");
            Help(panel, "Adds the MODERN speed and gear gauge to the ORIGINAL HUD.");
            Needs(Named(OptUi.Spinner("REV GAUGE", () => ReflexSettings.RevGaugeNames[ReflexSettings.RevGauge], d => { ReflexSettings.RevGauge = ((ReflexSettings.RevGauge + d) % 3 + 3) % 3; ReflexSettings.Save(); }, panel), "hud-rev-gauge"),
                  () => Modern() ? ReflexSettings.HudGauge : ReflexSettings.ClassicSpeedometer, "THE GAUGE IS OFF");
            Help(panel, "The gauge's ring. RPM: the engine's revs up to the limiter, amber near the top, flashing red on it. SPEED: your speed, up to 90 mph. OFF: only the gear and the speed.");
            Named(OptUi.Toggle("CAMERA NAME", () => ReflexSettings.CameraToast, v => { ReflexSettings.CameraToast = v; ReflexSettings.Save(); }, panel), "camera-toast");
            Help(panel, "Shows the camera's name for a moment when you switch cameras in a race, also with the HUD hidden.");
            Named(OptUi.Toggle("CLIENT INFO", () => { var rt = ReflexClientLive.Runtime; return rt ? rt.HudVisible : ReflexSettings.ClientInfo; }, ReflexClientLive.ClientInfo, panel), "client-info");
            Help(panel, "A diagnostic overlay in a race: status, telemetry, the race line and the keys.");
            DefaultsRow(panel, "HUD", "reset-hud", "Puts every HUD row above back to its default: the default HUD STYLE, shown, at 100% size and opacity, with its panels on.",
                     "Every HUD option goes back to its default: HUD STYLE, visibility, size, opacity, the panels, the gauge, CAMERA NAME and CLIENT INFO.", ResetHud);

            CameraPresetRows(panel);   // CAMERA CYCLE, MY CAMERAS, CAMERA SHARING (ReflexOptionsMenu.Cameras.cs / .CameraShare.cs)

            FpvRows(panel);   // FIRST-PERSON VIEW (ReflexOptionsMenu.Fpv.cs; it ends with FpvShareRows: CAMERA SHARING's FIRST-PERSON SETUPS)
            return first;
        }
    }
}
