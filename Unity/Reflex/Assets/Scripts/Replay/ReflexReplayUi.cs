// The REPLAY / PHOTO MODE screens over the world (ReflexReplay, after Open Unleashed's MxaReplayUi), UI Toolkit in the OPEN REFLEX menu
// look (OuMenu / OuModern plus Resources/OpenFrontEnd/OuReplay.uss). The two faces are one layout, inside the Open front end's content
// frame (OuLayout.Insets: the same margins as the menus, centred on an ultrawide screen, following UI SCALE):
//   top left     the badge (REPLAY / PHOTO MODE), the track and the camera
//   top right    the followed rider's card (name, place, lap, speed)
//   bottom       the dock: the timeline (click or drag to scrub; the white tick is the live moment; the KEYFRAMES camera's keys are
//                diamonds on it), back 5 s / frame back / play / frame forward / forward 5 s, the time and how far before
//                the live moment it is, then (REPLAY) SPEED, CAMERA and RIDER, and on both faces the other face's button and EXIT.
//                REPLAY has the KEYFRAMES row above the timeline (ADD KEY / DELETE KEY / CLEAR / AIM); PHOTO MODE has no play button
//                (the moment is frozen) and the dock ends where the settings panel begins
//   right        PHOTO MODE's settings panel (CAMERA and RIDER first, as on the dock; D-pad / arrows move, left / right change a value,
//                A / Enter press; the mouse clicks the arrows or the bars) and TAKE PHOTO; the aspect mask, the composition grid and the
//                logo's place over the view; a flash and a toast with the saved shot's thumbnail
//   bottom right the hint bar (the Open front end's key caps; the keyboard's, the Xbox pad's or the PlayStation pad's): the face's own
//                keys first, then the same three on both faces: the other face, HIDE UI, EXIT (BACK when PHOTO MODE returns to the replay)
// H / X (Square) hides all of it (the aspect mask and the logo stay: they are the composition); a SHOW UI note fades after a moment.
// The keys are ReflexReplay's.
using System;
using System.Collections.Generic;
using Reflex.Port.OpenFrontEnd;
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Reflex.Port.Replay
{
    public sealed class ReflexReplayUi : MonoBehaviour
    {
        ReflexReplay r;
        VisualElement root, layer, top, dock, panel, hints, flash, toast, thumb, hiddenNote, grid, logo, playButton, chips, chipRow, controls, modeButton;
        readonly VisualElement[] mask = new VisualElement[4];
        Label badge, sub, riderName, riderInfo, time, offset, toastTitle, toastPath, postNote, modeLabel;
        VisualElement fill, headDot, keyMarks, keyRow;
        Label keyNote, aimValue;
        float keyNoteAt = -10f;
        readonly List<VisualElement> keyDiamonds = new List<VisualElement>();
        ReflexReplayIcon playIcon;
        Label speedValue, camValue, riderValue;
        OuMenuInput input;
        readonly OuNav nav = new OuNav();
        readonly OuStepper stepper = new OuStepper();
        readonly List<(VisualElement row, Action refresh)> rows = new List<(VisualElement, Action)>();
        float flashAt = -10f, toastAt = -10f, hiddenAt;
        bool hintsBuilt, hintsBack, hintsFit;
        OuGlyphs.Device hintsDevice;
        ReflexReplay.View hintsFace;
        (Vector4 insets, bool photo, float dockHeight, float topHeight, float width) laidOut;
        /// The settings panel's width and the gap between it and the dock (layout px).
        const float PanelWidth = 470f, Gap = 16f;
        /// The width the replay's controls row needs with SPEED / CAMERA / RIDER in it (layout px).
        const float ControlsWidth = 1700f;
        int gridShown = -1;
        Rect frameShown; float maskAspect = -1f; int logoShown = -1;
        bool dragging, wasPlaying;
        VisualElement shoot;

        public UIDocument Document { get; private set; }
        /// The mouse is over the PHOTO MODE panel (its wheel scrolls the list, not the lens).
        public bool PointerOverPanel { get; private set; }
        /// The UI's root (tests).
        public VisualElement Root => root;

        public void Build(ReflexReplay replay)
        {
            r = replay;
            OuUi.EnsureEventSystem();
            root = OuUi.CreateDocument(gameObject, 90);
            Document = GetComponent<UIDocument>();
            OuTheme.Apply(root);
            var uss = Resources.Load<StyleSheet>(OuUi.ResourceRoot + "OuReplay");
            if (uss && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);
            root.AddToClassList("ou-replay");
            layer = OuUi.Div("rp-layer", root);
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
            layer.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < 4; i++) { mask[i] = OuUi.Div("rp-mask", layer); mask[i].pickingMode = PickingMode.Ignore; }
            grid = OuUi.Div("rp-grid", layer);
            grid.pickingMode = PickingMode.Ignore;
            logo = OuUi.Div("rp-logo", layer);
            logo.pickingMode = PickingMode.Ignore;
            if (ReflexReplay.LogoTexture) logo.style.backgroundImage = ReflexReplay.LogoTexture;

            BuildTop();
            BuildDock();
            BuildPanel();

            hints = OuUi.Div("mxa-hints", layer);   // (in the content frame, with the legend band behind it: OuLayout)
            hiddenNote = OuUi.Div("rp-hidden-note", layer);
            hiddenNote.pickingMode = PickingMode.Ignore;

            toast = OuUi.Div("rp-toast", layer);
            toast.pickingMode = PickingMode.Ignore;
            thumb = OuUi.Div("rp-thumb", toast);
            var tt = OuUi.Div("rp-toast-text", toast);
            toastTitle = OuUi.Text("PHOTO SAVED", "rp-toast-title", tt);
            toastPath = OuUi.Text("", "rp-toast-path", tt);

            flash = OuUi.Div("rp-flash", layer);
            flash.pickingMode = PickingMode.Ignore;
            nav.Screen = panel;
            nav.OnCancel = null;   // ReflexReplay handles Esc / B
            Hide();
        }

        void OnDestroy() { input?.Dispose(); }

        public void Show()
        {
            root.style.display = DisplayStyle.Flex;
            if (input == null) { input = new OuMenuInput(); input.Enable(false); }   // (only what this screen injects: ReflexReplay owns the keys)
            FaceChanged();
        }

        public void Hide()
        {
            if (root != null) root.style.display = DisplayStyle.None;
            input?.Dispose();
            input = null;
            PointerOverPanel = false;
        }

        public void FaceChanged()
        {
            if (r == null) return;
            bool photo = r.Face == ReflexReplay.View.Photo, hidden = r.UiHidden;
            Show(top, !hidden);
            Show(dock, !hidden);
            Show(keyRow, !photo);       // (the KEYFRAMES camera is the replay's)
            Show(playButton, !photo);   // (the moment is frozen)
            Show(chips, !photo);        // (PHOTO MODE: the panel's CAMERA and RIDER rows, which the pad reaches)
            Show(chipRow, !photo && chips.parent == chipRow);
            dock.EnableInClassList("rp-dock-photo", photo);
            Show(panel, !hidden && photo);
            Show(hints, !hidden);
            Show(hiddenNote, hidden);
            if (hidden)
            {   // SHOW UI, on the key of the device in use
                hiddenAt = Time.unscaledTime; hiddenNote.RemoveFromClassList("rp-hidden-note-fade");
                var device = OuGlyphs.Current;
                string key = device == OuGlyphs.Device.Keyboard ? "H" : device == OuGlyphs.Device.PlayStation ? "Square" : "X";
                hiddenNote.Clear();
                var hk = OuUi.Div("mxa-hint", hiddenNote);
                OuUi.Text(key, "mxa-key " + (OuGlyphs.CapClass(key) ?? ""), hk);
                OuUi.Text("SHOW UI", "mxa-hint-label", hk);
            }
            badge.text = photo ? "PHOTO MODE" : "REPLAY";
            modeLabel.text = photo ? "REPLAY" : "PHOTO MODE";
            hintsBuilt = false;
            if (photo && !hidden) panel.schedule.Execute(() => { if (nav.Focused == null) nav.Focus(shoot); }).ExecuteLater(10);
            RefreshRows();
        }

        static void Show(VisualElement e, bool on) { if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }

        // ------------------------------------------------------------------------------------------------ top

        void BuildTop()
        {
            top = OuUi.Div("rp-top", layer);
            top.pickingMode = PickingMode.Ignore;
            var left = OuUi.Div("rp-top-left", top);
            var b = OuUi.Div("rp-badge", left);
            OuUi.Div("rp-badge-dot", b);
            badge = OuUi.Text("REPLAY", "rp-badge-text", b);
            sub = OuUi.Text("", "rp-sub", left);
            var rider = OuUi.Div("rp-rider", top);
            var text = OuUi.Div("rp-rider-text", rider);
            riderName = OuUi.Text("", "rp-rider-name", text);
            riderInfo = OuUi.Text("", "rp-rider-info", text);
            OuUi.Div("rp-rider-bar", rider);
        }

        // ----------------------------------------------------------------------------------------------- dock

        void BuildDock()
        {
            dock = OuUi.Div("rp-dock", layer);

            var timeline = OuUi.Div("rp-timeline", dock);
            var track = OuUi.Div("rp-track", timeline);
            fill = OuUi.Div("rp-track-fill", track);
            OuUi.Div("rp-live", timeline);
            keyMarks = OuUi.Div("rp-keymarks", timeline);
            keyMarks.pickingMode = PickingMode.Ignore;
            headDot = OuUi.Div("rp-head", timeline);
            Scrubber(timeline);

            // KEYFRAMES (REPLAY): camera keys along the timeline, flown by the KEYFRAMES camera
            keyRow = OuUi.Div("rp-keyrow", dock);
            OuUi.Text("KEYFRAMES", "rp-keyrow-title", keyRow);
            TextButton(keyRow, "ADD KEY", () => r.AddKey(), false).AddToClassList("rp-key-button");
            TextButton(keyRow, "DELETE KEY", () => r.DeleteKey(), false).AddToClassList("rp-key-button");
            TextButton(keyRow, "CLEAR", () => r.ClearKeys(), false).AddToClassList("rp-key-button");
            aimValue = Chip(keyRow, "AIM", 90, _ => r.KeyAimRider = !r.KeyAimRider);
            keyNote = OuUi.Text("", "rp-keynote", keyRow);

            chipRow = OuUi.Div("rp-chiprow", dock);   // (SPEED / CAMERA / RIDER sit here when the controls row has no room for them: Layout)
            var row = controls = OuUi.Div("rp-controls", dock);
            IconButton(row, ReflexReplayIcon.Kind.Back, () => r.Seek(r.PlayTime - 5f));
            IconButton(row, ReflexReplayIcon.Kind.StepBack, () => r.Step(-1));
            playButton = IconButton(row, ReflexReplayIcon.Kind.Play, () => r.TogglePlay());
            playButton.AddToClassList("rp-play");
            playIcon = playButton.Q<ReflexReplayIcon>();
            IconButton(row, ReflexReplayIcon.Kind.StepForward, () => r.Step(1));
            IconButton(row, ReflexReplayIcon.Kind.Forward, () => r.Seek(r.PlayTime + 5f));
            time = OuUi.Text("", "rp-time", row);
            offset = OuUi.Text("", "rp-time rp-time-dim", row);
            OuUi.Div("rp-spacer", row);
            chips = OuUi.Div("rp-chips", row);
            speedValue = Chip(chips, "SPEED", 64, d => r.ChangeSpeed(d));
            camValue = Chip(chips, "CAMERA", 190, d => r.CycleCamera(d));
            riderValue = Chip(chips, "RIDER", 160, d => r.CycleTarget(d));
            // the same two on both faces: the other face, EXIT
            var mode = modeButton = TextButton(row, "PHOTO MODE", () => { if (r.Face == ReflexReplay.View.Photo) r.SetFace(ReflexReplay.View.Replay); else r.PhotoFromReplay(); }, true);
            modeLabel = mode.Q<Label>(className: "mxa-button-label");
            TextButton(row, "EXIT", () => r.Close(), false);
        }

        /// The keys' diamonds on the timeline (a click jumps to the key; the one at the moment shown is lit), the AIM value and the
        /// last key action's note (3 s).
        void KeyFrame(ReflexReplayBuffer b, float span)
        {
            var keys = r.Keys;
            while (keyDiamonds.Count < keys.Count)
            {
                var d = OuUi.Div("rp-keymark", keyMarks);
                d.pickingMode = PickingMode.Position;
                int idx = keyDiamonds.Count;
                d.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0 || idx >= r.Keys.Count) return;
                    r.Pause(); r.Seek(r.Keys[idx].t); OuSound.Play("Accept"); e.StopPropagation();
                });
                keyDiamonds.Add(d);
            }
            int here = r.KeyAt(r.PlayTime, 0.25f);
            for (int i = 0; i < keyDiamonds.Count; i++)
            {
                bool on = i < keys.Count;
                keyDiamonds[i].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                keyDiamonds[i].style.left = Length.Percent(Mathf.Clamp01((keys[i].t - b.StartTime) / span) * 100f);
                keyDiamonds[i].EnableInClassList("rp-keymark-here", i == here);
            }
            aimValue.text = r.KeyAimRider ? "RIDER" : "VIEW";
            if (!string.IsNullOrEmpty(r.KeyNote)) { keyNote.text = r.KeyNote; r.KeyNote = null; keyNoteAt = Time.unscaledTime; }
            if (Time.unscaledTime - keyNoteAt > 3f) keyNote.text = keys.Count == 0 ? "FLY TO A SHOT, THEN ADD KEY" : "";
        }

        /// Click / drag on the timeline seeks.
        void Scrubber(VisualElement area)
        {
            area.pickingMode = PickingMode.Position;
            void SeekAt(Vector2 local)
            {
                float w = area.resolvedStyle.width;
                if (w <= 1f) return;
                var b = r.Buffer;
                r.Seek(Mathf.Lerp(b.StartTime, b.EndTime, Mathf.Clamp01(local.x / w)));
            }
            area.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true;
                wasPlaying = r.Playing;
                r.Pause();
                area.CapturePointer(e.pointerId);
                SeekAt(e.localPosition);
                e.StopPropagation();
            });
            area.RegisterCallback<PointerMoveEvent>(e => { if (dragging && area.HasPointerCapture(e.pointerId)) SeekAt(e.localPosition); });
            area.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                area.ReleasePointer(e.pointerId);
                if (wasPlaying && r.Face == ReflexReplay.View.Replay) r.TogglePlay();
            });
        }

        VisualElement IconButton(VisualElement parent, ReflexReplayIcon.Kind kind, Action click)
        {
            var b = OuUi.Div("rp-icon-button", parent);
            b.pickingMode = PickingMode.Position;
            var icon = new ReflexReplayIcon { kind = kind };
            icon.AddToClassList("rp-icon");
            icon.pickingMode = PickingMode.Ignore;
            b.Add(icon);
            b.RegisterCallback<ClickEvent>(e => { if (e.button == 0) { OuSound.Play("Accept"); click(); } });
            return b;
        }

        Label Chip(VisualElement parent, string label, float valueWidth, Action<int> change)
        {
            var c = OuUi.Div("rp-chip", parent);
            OuUi.Text(label, "rp-chip-label", c);
            var prev = OuUi.Text("‹", "rp-chip-arrow", c);
            var val = OuUi.Text("", "rp-chip-value", c);
            val.style.minWidth = valueWidth;
            var next = OuUi.Text("›", "rp-chip-arrow", c);
            prev.pickingMode = next.pickingMode = PickingMode.Position;
            prev.RegisterCallback<ClickEvent>(e => { OuSound.Play("LeftRight"); change(-1); e.StopPropagation(); });
            next.RegisterCallback<ClickEvent>(e => { OuSound.Play("LeftRight"); change(1); e.StopPropagation(); });
            c.pickingMode = PickingMode.Position;
            c.RegisterCallback<ClickEvent>(e => { if (e.button == 0) { OuSound.Play("LeftRight"); change(1); } });
            return val;
        }

        VisualElement TextButton(VisualElement parent, string text, Action click, bool accent)
        {
            var b = OuUi.Div("rp-text-button", parent);
            if (accent) b.AddToClassList("rp-text-accent");
            OuUi.Text(text, "mxa-button-label", b);
            b.pickingMode = PickingMode.Position;
            b.RegisterCallback<ClickEvent>(e => { if (e.button == 0) { OuSound.Play("Accept"); click(); } });
            return b;
        }

        // ------------------------------------------------------------------------------------- PHOTO MODE panel

        void BuildPanel()
        {
            panel = OuUi.Div("rp-panel", layer);
            panel.RegisterCallback<PointerEnterEvent>(_ => PointerOverPanel = true);
            panel.RegisterCallback<PointerLeaveEvent>(_ => PointerOverPanel = false);
            OuUi.Text("PHOTO SETTINGS", "rp-title", panel);
            OuUi.Text("THE MOMENT IS FROZEN · MOVE THE CAMERA, SET THE SHOT", "rp-sub rp-panel-sub", panel);
            var sv = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Auto, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            sv.AddToClassList("rp-scroll");
            panel.Add(sv);
            var c = sv.contentContainer;
            var p = ReflexReplay.Photo;

            Section(c, "CAMERA");
            Choice(c, "CAMERA", () => p.orbit ? "ORBIT" : "FREE", _ => r.SetPhotoOrbit(!p.orbit));
            Choice(c, "RIDER", () => r.TargetName(r.Target), d => r.CycleTarget(d));   // (the dock's order and names: CAMERA, RIDER)
            Number(c, "FIELD OF VIEW", () => r.UserFov, v => r.UserFov = v, 5f, 110f, 1f, v => $"{v:0}°", false);
            Number(c, "ROLL", () => p.roll, v => p.roll = v, -45f, 45f, 1f, v => $"{v:+0;-0;0}°", true);

            Section(c, "LENS");
            Choice(c, "FOCUS", () => ReflexReplay.FocusNames[p.focus], d => p.focus = (p.focus + d + 3) % 3);
            Number(c, "FOCUS DISTANCE", () => p.focusDistance, v => p.focusDistance = v, 1f, 600f, 0f, v => ReflexSettings.Metric ? $"{v * 0.3048f:0.#} M" : $"{v:0.#} FT", false,
                   () => p.focus == 2, true);
            Choice(c, "APERTURE", () => "f/" + ReflexReplay.Apertures[p.aperture].ToString("0.#"), d => p.aperture = Mathf.Clamp(p.aperture + d, 0, ReflexReplay.Apertures.Length - 1),
                   () => p.focus != 0);
            Number(c, "MOTION BLUR", () => p.motionBlur, v => p.motionBlur = v, 0f, 1f, 0.05f, MotionBlurLabel, false);

            postNote = OuUi.Text("FILTER, EXPOSURE, WHITE BALANCE AND GLOW NEED URP POST-PROCESSING ON THE GAME CAMERA (NOT AVAILABLE HERE).", "rp-note", c);
            Section(c, "COLOUR");
            Choice(c, "FILTER", () => ReflexReplay.FilterNames[p.filter], d => p.filter = (p.filter + d + ReflexReplay.FilterNames.Length) % ReflexReplay.FilterNames.Length, () => r.PostAvailable);
            Number(c, "EXPOSURE", () => p.exposure, v => p.exposure = v, -3f, 3f, 0.1f, v => $"{v:+0.0;-0.0;0.0} EV", true, () => r.PostAvailable);
            Number(c, "CONTRAST", () => p.contrast, v => p.contrast = v, -60f, 60f, 5f, v => $"{v:+0;-0;0}", true, () => r.PostAvailable);
            Number(c, "SATURATION", () => p.saturation, v => p.saturation = v, -100f, 100f, 5f, v => $"{v:+0;-0;0}", true, () => r.PostAvailable);
            Number(c, "TEMPERATURE", () => p.temperature, v => p.temperature = v, -60f, 60f, 5f, v => $"{v:+0;-0;0}", true, () => r.PostAvailable);
            Number(c, "TINT", () => p.tint, v => p.tint = v, -50f, 50f, 5f, v => $"{v:+0;-0;0}", true, () => r.PostAvailable);

            Section(c, "EFFECTS");
            Choice(c, "LENS EFFECTS", () => p.lens ? "ON" : "OFF", _ => p.lens = !p.lens);
            Number(c, "VIGNETTE", () => p.vignette, v => p.vignette = v, 0f, 1f, 0.05f, Pct, false);
            Number(c, "FILM GRAIN", () => p.grain, v => p.grain = v, 0f, 1f, 0.05f, Pct, false);
            Number(c, "CHROMATIC ABERRATION", () => p.chromatic, v => p.chromatic = v, 0f, 1f, 0.05f, Pct, false);
            Number(c, "GLOW", () => p.glow, v => p.glow = v, 0f, 1f, 0.05f, Pct, false, () => r.PostAvailable);

            Section(c, "FRAME");
            Choice(c, "ASPECT", () => ReflexReplay.AspectNames[p.aspect], d => p.aspect = (p.aspect + d + ReflexReplay.AspectNames.Length) % ReflexReplay.AspectNames.Length);
            Choice(c, "GRID", () => ReflexReplay.GridNames[p.grid], d => p.grid = (p.grid + d + ReflexReplay.GridNames.Length) % ReflexReplay.GridNames.Length);
            Choice(c, "LOGO", () => ReflexReplay.LogoNames[p.logo], d => p.logo = (p.logo + d + ReflexReplay.LogoNames.Length) % ReflexReplay.LogoNames.Length).name = "photo-logo";

            Section(c, "SCENE");
            Choice(c, "YOUR RIDER", () => p.showRider ? "SHOWN" : "HIDDEN", _ => p.showRider = !p.showRider);
            Choice(c, "YOUR MACHINE", () => p.showBike ? "SHOWN" : "HIDDEN", _ => p.showBike = !p.showBike);
            Choice(c, "OTHER RIDERS", () => p.showOthers ? "SHOWN" : "HIDDEN", _ => p.showOthers = !p.showOthers);

            Section(c, "OUTPUT");
            Choice(c, "RESOLUTION", () => ResolutionLabel(p.resolution), d => p.resolution = (p.resolution + d + ReflexReplay.ResolutionNames.Length) % ReflexReplay.ResolutionNames.Length);
            var buttons = OuUi.Div("rp-panel-buttons", c);
            OuUi.Button("RESET", "rp-text-button", () => { p.ResetLook(); r.UserFov = 55f; RefreshRows(); }, buttons);
            OuUi.Button("OPEN FOLDER", "rp-text-button", ReflexReplay.RevealPhotos, buttons);

            shoot = OuUi.Button("TAKE PHOTO", "rp-shoot", () => r.TakePhoto(), panel);
            shoot.name = "photo-take";
            nav.DefaultItem = () => shoot;
        }

        /// RESOLUTION's value with the picture's size on this screen ("4K · 3840 X 2160").
        string ResolutionLabel(int i)
        {
            var cam = r.ViewCamera;
            if (!cam) return ReflexReplay.ResolutionNames[i];
            var s = ReflexReplay.ShotSize(cam.pixelWidth, cam.pixelHeight, i);
            float a = ReflexReplay.FrameAspect(ReflexReplay.Photo.aspect);
            int w = s.x, h = s.y;
            if (a > 0f) { if (w / (float)h > a) w = Mathf.RoundToInt(h * a); else h = Mathf.RoundToInt(w / a); }
            return $"{ReflexReplay.ResolutionNames[i]} · {w} X {h}";
        }

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
        /// MOTION BLUR as the shutter time it is (the photo's shutter is open up to 1/20 s).
        static string MotionBlurLabel(float v) => v < 0.01f ? "OFF" : "1/" + Mathf.RoundToInt(20f / v) + " S";

        static void Section(VisualElement parent, string text) => OuUi.Text(text, "rp-section", parent);

        VisualElement Choice(VisualElement parent, string label, Func<string> value, Action<int> change, Func<bool> enabled = null)
        {
            var row = OuUi.Spinner(label, value, d => { change(d); RefreshRows(); }, parent);
            var val = row.Q<Label>(className: "mxa-value");
            rows.Add((row, () =>
            {
                if (val != null) val.text = value();
                row.EnableInClassList("rp-row-off", enabled != null && !enabled());
            }));
            return row;
        }

        /// A number row: label, a bar (bipolar: filled from the centre), the value; left / right step it (multiplicative when step is
        /// 0), the mouse sets it on the bar.
        void Number(VisualElement parent, string label, Func<float> get, Action<float> set, float min, float max, float step, Func<float, string> fmt,
                    bool bipolar, Func<bool> enabled = null, bool logBar = false)
        {
            var row = OuUi.Div("mxa-row", parent);
            OuUi.Text(label, "mxa-row-label", row);
            var right = OuUi.Div("mxa-row-value", row);
            var prev = OuUi.Text("‹", "mxa-arrow", right);
            var bar = OuUi.Div("rp-bar", right);
            var barFill = OuUi.Div("rp-bar-fill", bar);
            if (bipolar) OuUi.Div("rp-bar-zero", bar);
            var val = OuUi.Text("", "rp-num-value", right);
            var next = OuUi.Text("›", "mxa-arrow", right);
            float ToBar(float v) => logBar ? Mathf.InverseLerp(Mathf.Log(min), Mathf.Log(max), Mathf.Log(Mathf.Max(v, min))) : Mathf.InverseLerp(min, max, v);
            float FromBar(float f) => logBar ? Mathf.Exp(Mathf.Lerp(Mathf.Log(min), Mathf.Log(max), f)) : Mathf.Lerp(min, max, f);
            void Refresh()
            {
                float v = get();
                float f = ToBar(v);
                if (bipolar)
                {
                    float z = ToBar(0f);
                    barFill.style.left = Length.Percent(Mathf.Min(f, z) * 100f);
                    barFill.style.width = Length.Percent(Mathf.Abs(f - z) * 100f);
                }
                else
                {
                    barFill.style.left = 0;
                    barFill.style.width = Length.Percent(f * 100f);
                }
                val.text = fmt(v);
                row.EnableInClassList("rp-row-off", enabled != null && !enabled());
            }
            void Adjust(int d)
            {
                float v = get();
                v = step > 0f ? Mathf.Round((v + d * step) / step) * step : v * Mathf.Pow(1.08f, d);
                set(Mathf.Clamp(v, min, max));
                Refresh();
            }
            prev.pickingMode = next.pickingMode = PickingMode.Position;
            prev.RegisterCallback<ClickEvent>(e => { OuSound.Play("LeftRight"); Adjust(-1); e.StopPropagation(); });
            next.RegisterCallback<ClickEvent>(e => { OuSound.Play("LeftRight"); Adjust(1); e.StopPropagation(); });
            bar.pickingMode = PickingMode.Position;
            void SetAt(float x)
            {
                float w = bar.resolvedStyle.width;
                if (w <= 1f) return;
                float v = FromBar(Mathf.Clamp01(x / w));
                if (step > 0f) v = Mathf.Round(v / step) * step;
                set(Mathf.Clamp(v, min, max));
                Refresh();
            }
            bar.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; bar.CapturePointer(e.pointerId); SetAt(e.localPosition.x); e.StopPropagation(); });
            bar.RegisterCallback<PointerMoveEvent>(e => { if (bar.HasPointerCapture(e.pointerId)) SetAt(e.localPosition.x); });
            bar.RegisterCallback<PointerUpEvent>(e => { if (bar.HasPointerCapture(e.pointerId)) bar.ReleasePointer(e.pointerId); });
            OuUi.Item(row, null, Adjust);
            row.RegisterCallback<AttachToPanelEvent>(_ => Refresh());
            rows.Add((row, Refresh));
        }

        void RefreshRows()
        {
            foreach (var (_, refresh) in rows) refresh();
            if (postNote != null) Show(postNote, r != null && !r.PostAvailable);
        }

        // --------------------------------------------------------------------------------------------- per frame

        public void Frame()
        {
            if (r == null || root.style.display == DisplayStyle.None) return;
            bool photo = r.Face == ReflexReplay.View.Photo;
            if (input != null && photo && !r.UiHidden) NavStep();
            var b = r.Buffer;
            float span = Mathf.Max(0.001f, b.Duration);
            float frac = Mathf.Clamp01((r.PlayTime - b.StartTime) / span);
            // the timeline and the clock: the same on both faces
            fill.style.width = Length.Percent(frac * 100f);
            headDot.style.left = Length.Percent(frac * 100f);
            time.text = $"{Clock(r.PlayTime - b.StartTime)}  /  {Clock(span)}";
            float back = b.EndTime - r.PlayTime;
            offset.text = back < 0.005f ? "LIVE" : $"LIVE −{back:0.00} S";
            if (!photo)
            {
                playIcon.SetKind(r.Playing ? ReflexReplayIcon.Kind.Pause : ReflexReplayIcon.Kind.Play);
                speedValue.text = r.SpeedLabel;
                camValue.text = r.CameraLabel;
                riderValue.text = r.TargetName(r.Target);
                KeyFrame(b, span);
            }
            string track = r.Runtime ? (r.Runtime.level ?? "") : "";
            sub.text = track.Replace('_', ' ').ToUpperInvariant() + "   ·   " + (photo ? (ReflexReplay.Photo.orbit ? "ORBIT" : "FREE") : r.CameraLabel);
            riderName.text = r.TargetName(r.Target);
            riderInfo.text = r.TargetInfo();
            Layout(photo);
            Hints(photo);
            Frame(photo ? ReflexReplay.FrameAspect(ReflexReplay.Photo.aspect) : 0f, photo ? ReflexReplay.Photo.logo : 0);
            Grid(photo && !r.UiHidden ? ReflexReplay.Photo.grid : 0);
            // the flash and the toast
            float fa = Mathf.Clamp01(1f - (Time.unscaledTime - flashAt) / 0.35f);
            flash.style.opacity = fa * 0.85f;
            toast.EnableInClassList("rp-toast-on", Time.unscaledTime - toastAt < 3.2f);
            if (r.UiHidden && Time.unscaledTime - hiddenAt > 2.5f) hiddenNote.AddToClassList("rp-hidden-note-fade");
            if (photo && Time.frameCount % 15 == 0) RefreshRows();   // (the FOV changes with the wheel / triggers)
        }

        /// The panel's navigation: the D-pad / arrow keys only (the left stick and WASD fly the camera, Space takes the photo), A / Enter.
        void NavStep()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            Vector2 v = Vector2.zero;
            if (kb != null) v += new Vector2((kb.rightArrowKey.isPressed ? 1 : 0) - (kb.leftArrowKey.isPressed ? 1 : 0), (kb.upArrowKey.isPressed ? 1 : 0) - (kb.downArrowKey.isPressed ? 1 : 0));
            if (pad != null) v += pad.dpad.ReadValue();
            input.injectStep = stepper.Step(v, Time.unscaledTime);
            input.injectSubmit = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) || pad != null && pad.buttonSouth.wasPressedThisFrame;
            nav.Update(input);
        }

        public static string Clock(float s)
        {
            s = Mathf.Max(0f, s);
            int m = (int)(s / 60f);
            return $"{m}:{s - m * 60f:00.00}";
        }

        /// Everything inside the Open front end's content frame (OuLayout.Insets: the menus' margins, the 2:1 frame centred on an ultrawide
        /// screen, UI SCALE): the top row, the dock above the hint bar (PHOTO MODE: up to the panel), the panel beside it, the toast over it.
        void Layout(bool photo)
        {
            var i = OuLayout.Insets();
            float dockHeight = dock.resolvedStyle.height, topHeight = top.resolvedStyle.height, width = layer.resolvedStyle.width;
            if (float.IsNaN(dockHeight)) dockHeight = 0f;
            if (float.IsNaN(topHeight)) topHeight = 0f;
            if (float.IsNaN(width)) width = 0f;
            var now = (i, photo, dockHeight, topHeight, width);
            if (now.Equals(laidOut)) return;
            if (width != laidOut.width || i != laidOut.insets) hintsBuilt = false;   // (the hint bar is fitted to the frame again)
            laidOut = now;
            float hintsBottom = Mathf.Max(24f, i.w - 88f);                          // (OuLayout's hint bar)
            float dockBottom = Mathf.Max(84f, hintsBottom + 60f) + 12f;             // (above its legend band)
            top.style.left = i.x; top.style.right = i.z; top.style.top = i.y;
            dock.style.left = i.x; dock.style.bottom = dockBottom;
            dock.style.right = photo ? i.z + PanelWidth + Gap : i.z;
            // SPEED / CAMERA / RIDER: in the controls row, or in a row of their own above it when the frame is narrower than the row (a large UI SCALE)
            bool narrow = width > 1f && width - i.x - i.z < ControlsWidth;
            var home = narrow ? chipRow : controls;
            if (chips.parent != home) { if (narrow) chipRow.Add(chips); else controls.Insert(controls.IndexOf(modeButton), chips); }
            Show(chipRow, narrow && !photo);
            panel.style.width = PanelWidth;
            panel.style.right = i.z; panel.style.bottom = dockBottom;
            panel.style.top = i.y + Mathf.Max(44f, topHeight) + Gap;
            toast.style.bottom = dockBottom + dockHeight + Gap;
            hiddenNote.style.right = i.z; hiddenNote.style.bottom = hintsBottom;
        }

        /// The hint bar: the face's own keys first (the main action, moving in time or space, the camera, the rider), then the same three
        /// in the same order on both faces and every device: the other face, HIDE UI, EXIT (BACK when PHOTO MODE returns to the replay).
        void Hints(bool photo)
        {
            var device = OuGlyphs.Current;
            bool back = photo && r.PhotoReturnsToReplay;
            if (hintsBuilt && device == hintsDevice && hintsFace == r.Face && back == hintsBack)
            {
                if (!hintsFit) FitHints();
                return;
            }
            hintsBuilt = true; hintsDevice = device; hintsFace = r.Face; hintsBack = back; hintsFit = false;
            bool pad = device != OuGlyphs.Device.Keyboard, ps = device == OuGlyphs.Device.PlayStation;
            string P(string playstation, string xbox) => ps ? playstation : xbox;
            var list = new List<(string key, string label)>();
            if (!photo)
            {
                if (pad) list.AddRange(new[] { (P("Cross", "A"), "PLAY / PAUSE"), (P("L2 / R2", "LT / RT"), "SHUTTLE"), ("D-pad", "SKIP · SPEED"), (P("Triangle", "Y"), "CAMERA"),
                                               (P("L1 / R1", "LB / RB"), "RIDER"), (P("R3", "RS"), "KEY") });
                else list.AddRange(new[] { ("Space", "PLAY / PAUSE"), ("← →", "SKIP"), ("↑ ↓", "SPEED"), (", .", "FRAME"), ("C", "CAMERA"), ("[ ]", "RIDER"), ("K", "KEY") });
            }
            else
            {
                if (pad) list.AddRange(new[] { (P("Triangle", "Y"), "TAKE PHOTO"), ("L Stick", "MOVE"), ("R Stick", "LOOK"), (P("L1 / R1", "LB / RB"), "DOWN / UP"),
                                               (P("L2 / R2", "LT / RT"), "ZOOM"), ("D-pad", "SETTINGS") });
                else list.AddRange(new[] { ("Space", "TAKE PHOTO"), ("WASD", "MOVE"), ("Q / E", "DOWN / UP"), ("Right mouse", "LOOK"), ("Wheel", "ZOOM"), ("Arrows", "SETTINGS"),
                                           (", .", "FRAME") });
            }
            list.Add((pad ? P("Create", "View") : "P", photo ? "REPLAY" : "PHOTO MODE"));
            list.Add((pad ? P("Square", "X") : "H", "HIDE UI"));
            list.Add((pad ? P("Circle", "B") : "Esc", back ? "BACK" : "EXIT"));
            hints.RemoveFromClassList("rp-hints-compact");
            OuUi.Hints(hints, list.ToArray());
        }

        /// The hint bar wider than the content frame (a large UI SCALE, a narrow window): the compact caps.
        void FitHints()
        {
            float w = hints.resolvedStyle.width, room = layer.resolvedStyle.width - laidOut.insets.x - laidOut.insets.z;
            if (float.IsNaN(w) || w < 1f || float.IsNaN(room) || room < 1f) return;   // (not laid out yet)
            hintsFit = true;
            if (w > room) hints.AddToClassList("rp-hints-compact");
        }

        /// The aspect frame (inside the game camera's own part of the screen): the view outside it darkened, the grid and the logo's
        /// place inside it.
        void Frame(float aspect, int logoCorner)
        {
            float W = layer.resolvedStyle.width, H = layer.resolvedStyle.height;
            if (float.IsNaN(W) || W < 2f || H < 2f) return;
            var cam = r.ViewCamera;
            Rect vr = cam ? cam.rect : new Rect(0, 0, 1, 1);
            // the camera's viewport in panel units (its rect is from the bottom left, the panel's from the top left)
            float vx = vr.x * W, vy = (1f - vr.y - vr.height) * H, vw = vr.width * W, vh = vr.height * H;
            float cw = vw, ch = vh;
            if (aspect > 0f) { if (vw / vh > aspect) cw = vh * aspect; else ch = vw / aspect; }
            var f = new Rect(vx + (vw - cw) * 0.5f, vy + (vh - ch) * 0.5f, cw, ch);
            if (f == frameShown && Mathf.Approximately(aspect, maskAspect) && logoCorner == logoShown) return;
            frameShown = f; maskAspect = aspect; logoShown = logoCorner;
            Place(mask[0], 0, 0, W, f.y);                           // top
            Place(mask[1], 0, f.yMax, W, H - f.yMax);               // bottom
            Place(mask[2], 0, f.y, f.x, f.height);                  // left
            Place(mask[3], f.xMax, f.y, W - f.xMax, f.height);      // right
            Place(grid, f.x, f.y, f.width, f.height);
            gridShown = -1;
            var tex = ReflexReplay.LogoTexture;
            if (logoCorner > 0 && tex)
            {
                var lr = ReflexReplay.LogoRect(f.width, f.height, logoCorner, tex.width / (float)tex.height);
                Place(logo, f.x + lr.x, f.y + lr.y, lr.width, lr.height);
            }
            else logo.style.display = DisplayStyle.None;
        }

        static void Place(VisualElement e, float x, float y, float w, float h)
        {
            e.style.left = x; e.style.top = y; e.style.width = Mathf.Max(0f, w); e.style.height = Mathf.Max(0f, h);
            e.style.display = w > 0.5f && h > 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Grid(int kind)
        {
            if (kind == gridShown) return;
            gridShown = kind;
            grid.Clear();
            void V(float f) { var l = OuUi.Div("rp-grid-line", grid); l.style.left = Length.Percent(f * 100f); l.style.top = 0; l.style.bottom = 0; l.style.width = 1; }
            void Hz(float f) { var l = OuUi.Div("rp-grid-line", grid); l.style.top = Length.Percent(f * 100f); l.style.left = 0; l.style.right = 0; l.style.height = 1; }
            switch (kind)
            {
                case 1: V(1f / 3f); V(2f / 3f); Hz(1f / 3f); Hz(2f / 3f); break;
                case 2: V(0.5f); Hz(0.5f); break;
                case 3: { const float g = 0.382f; V(g); V(1f - g); Hz(g); Hz(1f - g); break; }
            }
        }

        // ---------------------------------------------------------------------------------------------- capture

        public void CaptureBegin() { }

        public void CaptureEnd(string path, string error)
        {
            toastAt = Time.unscaledTime;
            if (path == null)
            {
                toastTitle.text = "PHOTO NOT SAVED";
                toastPath.text = error ?? "";
                thumb.style.display = DisplayStyle.None;
                return;
            }
            flashAt = Time.unscaledTime;
            toastTitle.text = "PHOTO SAVED";
            toastPath.text = path;
            thumb.style.display = DisplayStyle.Flex;
            if (r.LastPhoto)
            {
                thumb.style.backgroundImage = new StyleBackground(r.LastPhoto);
                float a = r.LastPhoto.width / (float)Mathf.Max(1, r.LastPhoto.height);
                thumb.style.width = Mathf.Clamp(99f * a, 56f, 200f);
            }
        }
    }

    /// The transport's vector icons (the fonts have no media glyphs).
    public sealed class ReflexReplayIcon : VisualElement
    {
        public enum Kind { Play, Pause, Back, Forward, Start, End, StepBack, StepForward }
        public Kind kind;

        public ReflexReplayIcon() { generateVisualContent += Draw; }

        public void SetKind(Kind k) { if (k == kind) return; kind = k; MarkDirtyRepaint(); }

        void Draw(MeshGenerationContext mgc)
        {
            var rc = contentRect;
            float w = rc.width, h = rc.height;
            if (w < 1f || h < 1f) return;
            var p = mgc.painter2D;
            p.fillColor = Color.white;
            void Tri(float x0, float x1, bool right)
            {
                p.BeginPath();
                if (right) { p.MoveTo(new Vector2(x0, 0f)); p.LineTo(new Vector2(x1, h * 0.5f)); p.LineTo(new Vector2(x0, h)); }
                else { p.MoveTo(new Vector2(x1, 0f)); p.LineTo(new Vector2(x0, h * 0.5f)); p.LineTo(new Vector2(x1, h)); }
                p.ClosePath();
                p.Fill();
            }
            void Bar(float x0, float x1)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(x0, 0f)); p.LineTo(new Vector2(x1, 0f)); p.LineTo(new Vector2(x1, h)); p.LineTo(new Vector2(x0, h));
                p.ClosePath();
                p.Fill();
            }
            switch (kind)
            {
                case Kind.Play: Tri(w * 0.18f, w * 0.92f, true); break;
                case Kind.Pause: Bar(w * 0.16f, w * 0.40f); Bar(w * 0.60f, w * 0.84f); break;
                case Kind.Back: Tri(0f, w * 0.5f, false); Tri(w * 0.5f, w, false); break;
                case Kind.Forward: Tri(0f, w * 0.5f, true); Tri(w * 0.5f, w, true); break;
                case Kind.Start: Bar(0f, w * 0.16f); Tri(w * 0.16f, w * 0.92f, false); break;
                case Kind.End: Tri(w * 0.08f, w * 0.84f, true); Bar(w * 0.84f, w); break;
                case Kind.StepBack: Tri(w * 0.06f, w * 0.62f, false); Bar(w * 0.74f, w * 0.92f); break;
                case Kind.StepForward: Bar(w * 0.08f, w * 0.26f); Tri(w * 0.38f, w * 0.94f, true); break;
            }
        }
    }
}
