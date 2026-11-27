// RESET TO DEFAULTS (issue #22): one button per page or group that puts its options back to their defaults, after a confirmation, applied
// at once and saved.
//   How a reset works: every option is a key in save.json (ReflexSave) whose getter in ReflexSettings / its owner carries the default, so a
//   reset removes the page's keys and then applies the live state again (volumes, the display, the camera...). The display settings the game
//   reads at start (graphics.cfg: VSync, motion blur, texture / shadow quality, MSAA) go back to the install's own graphics.cfg; RESOLUTION and
//   DISPLAY MODE stay, so a reset never leaves the screen in a mode the player did not pick.
//   What stays: the player's own things are not options: MY CAMERAS and CAMERA CYCLE, camera / first-person share codes, the save backups,
//   the GAME FOLDER, the per-track MAP SCALE, PHYSICS STUDIO configs (its own RESET rows ask with a second press).
//   CONTROLS     RUMBLE, RUMBLE STRENGTH, STICK LAYOUT, INVERT LEAN, both sticks' response, MOUSE LEAN / SENSITIVITY and the whole mapping (as DEFAULTS)
//   AUDIO        every volume, STREAMER MODE, MUSIC, SOUNDTRACK, YOUR MUSIC, NOW PLAYING, REVERB
//   VIDEO        every row but RESOLUTION and DISPLAY MODE
//   GAMEPLAY     UNITS, CAREER DIFFICULTY, ONLINE PLAYER NAMES, NATIVE GAME
//   CAMERA & HUD two groups: CAMERA (the driving camera, CAMERA TYPE, CHASE CAMERA FOV) and HUD (every HUD row); FIRST-PERSON VIEW keeps
//                its own RESET TO DEFAULTS (it asks too)
//   INTERFACE    UI SCALE, CONTROLLER GLYPHS, REDUCE MOTION, LANGUAGE
//   OPEN REFLEX  every row of the page (RIDING, RACE SETUP, LOADING, FREE RIDE, PHYSICS STUDIO EVERYWHERE, LEADERBOARDS, ONLINE, REPLAY)
using System;
using System.Collections.Generic;
using Reflex.Port.Content;
using Reflex.Port.OpenFrontEnd;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Options
{
    public sealed partial class ReflexOptionsMenu
    {
        /// The confirmation popup that is open (tests); null when none is.
        public VisualElement ResetPopup { get; private set; }

        static void Forget(params string[] keys) { foreach (var k in keys) ReflexSave.Delete(k); }

        /// A RESET row (DefaultsRow): "RESET <what> TO DEFAULTS"; SELECT asks first (ConfirmReset), then reset() runs, the save is written and the page drawn
        /// again with the new values, the focus back on this row.
        void DefaultsRow(VisualElement panel, string what, string id, string help, string confirm, Action reset)
        {
            var row = Named(OptUi.Button("RESET " + what + " TO DEFAULTS", "mxa-link rx-opt-reset", null, panel), id);   // (rx-opt-reset: the value rows' gold focus, OuRestyle.uss)
            ((OptNavItem)row.userData).submit = () => ConfirmReset("RESET " + what, confirm, reset, id);
            Help(panel, help, When.Now);
        }

        /// The page's own RESET section, the last of the page.
        void ResetSection(VisualElement panel, string what, string id, string help, string confirm, Action reset)
        {
            Section(panel, "RESET");
            DefaultsRow(panel, what, id, help, confirm, reset);
        }

        /// Asks before a reset (the camera editor's DELETE popup): RESET / CANCEL, CANCEL focused; Back cancels.
        void ConfirmReset(string title, string text, Action reset, string rowId)
        {
            int tab = _tab;
            OuKeyboard.EnsureStyles(_layer);
            var root = OptUi.Div("ou-gdialog-root p2-popup", _layer); root.name = "reset-popup";
            var card = OuWidgets.Dialog(root, "p2");
            OptUi.Text(title, "ou-dialog-title ou-display", card).enableRichText = false;
            OptUi.Text(text, "ou-dialog-text p2-popup-text", card).enableRichText = false;
            var buttons = OptUi.Div("p2-popup-buttons", card);
            var savedCancel = _nav.OnCancel;
            void Close() { _nav.Modal = null; _nav.OnCancel = savedCancel; root.RemoveFromHierarchy(); ResetPopup = null; }
            var yes = Named(OptUi.Button("RESET", "mxa-main-button p2-popup-button", () =>
            {
                Close();
                try { reset(); } catch (Exception e) { Debug.LogWarning("[Options] reset: " + e.Message); }
                ReflexSettings.Save();
                ShowOptionsAt(tab, rowId);
            }, buttons), "reset-yes");
            var no = Named(OptUi.Button("CANCEL", "mxa-main-button p2-popup-button", () => { Close(); ShowOptionsAt(tab, rowId); }, buttons), "reset-no");
            OuWidgets.LinkRow(new List<VisualElement> { yes, no });
            OptUi.Hints(_hints, (OptUi.KeySubmit, Str("STR_SELECT", "SELECT")), (OptUi.KeyCancel, "CANCEL"));
            _nav.OnCancel = () => { Close(); ShowOptionsAt(tab, rowId); return true; };
            _nav.Modal = card;
            _nav.Focus(no);
            ResetPopup = root;
        }

        // ------------------------------------------------------------------------------------------------ the pages' resets
        // (public: the tests run them without the popup)

        /// CONTROLS: the controller, INVERT LEAN, both sticks, the mouse, and the mapping table's DEFAULTS.
        public void ResetControls()
        {
            Forget("ReflexUI.Vibration", "Options.RumbleStrength", "ReflexUI.ControllerHandedness", "Options.InvertLeanX", "Options.InvertLeanY", "Options.MouseLean", "ReflexUI.MouseSensitivity");
            ReflexClientLive.GameSetting("Vibration", ReflexSettings.Vibration ? 1 : 0);
            ReflexClientLive.GameSetting("ControllerHandedness", ReflexSettings.RightHanded ? 1 : 0);
            ReflexClientLive.GameSetting("MouseSensitivity", ReflexSettings.MouseSensitivity);
            ReflexStickResponse.Set(ReflexStickResponse.Default, ReflexStickResponse.Default);
            RemapDefaults();
        }

        /// AUDIO: every volume, the music rows, the reverb.
        public static void ResetAudio()
        {
            Forget("Options.MasterVolume", "ReflexAudio.master", "ReflexUI.EngineVolume", "Options.CrowdVolume", "ReflexAudio.musicOn", "ReflexAudio.music",
                   "Options.Soundtrack", "Options.CustomMusic", "Options.NowPlaying", "OpenReflex.AudioReverb", "OpenReflex.AudioReverbFirstPerson");
            ReflexClientLive.MasterVolume(ReflexSettings.MasterVolume);
            ReflexClientLive.EffectsVolume(ReflexSettings.EffectsVolume);
            ReflexClientLive.StreamerMode(false);
            ReflexClientLive.MusicOn(ReflexSettings.MusicOn);
            ReflexClientLive.MusicVolume(ReflexSettings.MusicVolume);
            ReflexCustomMusic.SettingsChanged();
        }

        /// VIDEO: every row but RESOLUTION and DISPLAY MODE; graphics.cfg's other values from the install's own file.
        public static void ResetVideo()
        {
            var def = ReflexLaunchSettings.InstallDefaults();
            var s = ReflexLaunchSettings.Current;
            s.vsync = def.vsync; s.motionBlur = def.motionBlur; s.textureQuality = def.textureQuality; s.shadowQuality = def.shadowQuality; s.antialiasing = def.antialiasing;
            try { s.Save(); } catch (Exception e) { Debug.LogWarning("[Options] graphics.cfg not written: " + e.Message); }
            Forget("Options.AspectFill", "Options.FpsCap", "Options.FramePacing", "Options.LowLatency", "Options.Preset", "Options.EnhancedQuality",
                   "Options.Volumetric", "Options.VolumetricQuality", "Options.VolumetricDensity", "OpenReflex.SpeedBlur", "Options.Materials", "Options.MudBuildup",
                   "Options.TextureFiltering", "Options.TerrainDetail", "Options.TerrainTessellation", "Options.BikeDetail", "Options.DrawDistance",
                   "Options.TemporalAA", "Options.Sharpening", "Options.Upscaling", "Options.DynamicResolution", "Options.DynamicResolutionMin", "Options.DynamicResolutionTarget",
                   "Options.RenderScale", "ReflexUI.Brightness", "ReflexUI.Gamma", "Options.PerfOverlay", "Options.Quality");
            // (the setters with side effects, at their defaults: the A/B overrides they clear)
            Rendering.ReflexVolumetricLight.Option = Rendering.ReflexVolumetricLight.Option;
            Rendering.ReflexEnhancedMaterials.PickMaterials(Rendering.ReflexEnhancedMaterials.MaterialsShown);
            Rendering.ReflexEnhancedMaterials.PickMud(Rendering.ReflexEnhancedMaterials.MudSetting == 1);
            Rendering.ReflexAntiAliasing.Set(Rendering.ReflexAntiAliasing.Current);   // (TAA by default: graphics.cfg's Antialiasing 0; applies the graphics at once)
            Rendering.ReflexTextureFiltering.Refresh();
            foreach (var t in Terrain.activeTerrains) if (t) t.heightmapPixelError = ReflexSettings.TerrainPixelError;
            ReflexClientLive.ApplyDisplay(); Rendering.ReflexRenderPipeline.ReapplyViewports();
            ReflexClientLive.ApplyFrameRate(); ReflexClientLive.ApplyLatency(); ReflexClientLive.ApplyQuality();
            Reflex.Port.FrontEnd.ReflexScreenAdjustment.Apply();
            Platform.ReflexPerfOverlay.Apply(ReflexSettings.PerfOverlay);
        }

        /// GAMEPLAY: UNITS, CAREER DIFFICULTY, ONLINE PLAYER NAMES, NATIVE GAME (the game's settings follow the ReflexUI.* keys by themselves).
        public static void ResetGameplay()
        {
            Forget("ReflexUI.MeasurementSystem", "ReflexUI.CareerDifficulty", "ReflexUI.OnlinePlayerName");
            ReflexClientLive.GameSetting("OnlinePlayerName", ReflexSettings.OnlinePlayerName ? 0 : 1);
            ReflexSettings.NativeGame = -1;
        }

        /// CAMERA & HUD > CAMERA: the game's first view (the default driving camera, the game's camera), CHASE CAMERA FOV 100 %.
        public static void ResetCamera()
        {
            Forget("Options.ChaseFov");
            ReflexClientLive.SelectCamera(0);
        }

        /// CAMERA & HUD > HUD: every HUD row.
        public static void ResetHud()
        {
            Forget("Hud.Style", "Options.HudScale", "Options.HudOpacity", "Options.HudStandings", "Options.HudGauge", "Hud.ClassicSpeedometer", "Options.HudRevGauge", "Hud.CameraToast");
            ReflexClientLive.HudVisible(true);
            ReflexClientLive.ClientInfo(false);
        }

        /// INTERFACE: UI SCALE, CONTROLLER GLYPHS, REDUCE MOTION, LANGUAGE (the install's, from the next start).
        public static void ResetInterface() => Forget("Options.UiScale", "Options.GlyphStyle", "Options.ReduceMotion", "Options.Language");

        /// OPEN REFLEX: every row of the page, the replay's too.
        public static void ResetOpenReflex()
        {
            Forget("OpenReflex.AirAutoLevel", "OpenReflex.AirSnap", "OpenReflex.AirLooseness2", "OpenReflex.AirLooseness", "OpenReflex.FreerAir", "OpenReflex.FreerAirKeepSnap",
                   "OpenReflex.FreerAirKeepLevel", "OpenReflex.LipPull", "OpenReflex.RiderMotion", "OpenReflex.RiderMotionStrength", "OpenReflex.AnyVehicle",
                   "OpenReflex.DisableCompound", "OpenReflex.PlayersTab", "OpenReflex.FreeRideNoBorders", "OpenReflex.AliveLookReflex", "OpenReflex.SpawnMarkers", "OpenReflex.PhysicsEverywhere",
                   "OpenReflex.ReplaySeconds", "OpenReflex.ReplayEffects", "OpenReflex.ReplaySound", "OpenReflex.ReplayCinematic");
            ReflexClientLive.ApplyFreeAir();
            ReflexClientLive.ApplyFreeRideBorders();
            Reflex.Port.RxCore.RxPhysicsStudio.Everywhere = ReflexSettings.PhysicsEverywhere;
        }
    }
}
