// RACE / FREE RIDE's setup page (Resources/OpenFrontEnd/FrontEnd/Setup.uxml), after Open Unleashed's MxaMainMenu.SetupPage.cs /
// EventSetup.cs: the step after the track browser (SET UP EVENT, or Cross / Enter on a card). The location large on the left (its
// picture, the track map, the event and the location) over what the setup comes to; the settings on the right in tabs:
//   EVENT   what Reflex's LocationSelect offers today: EVENT DURATION (the type's EventTypeDurations: laps), AI OPPONENTS (OFF /
//           ROOKIE 80 / AMATEUR 95 / PRO 105 / ALL TIME 120; the world image fixes the field at 11 AI when on); free ride: none
//           CONDITIONS (race and Free Ride, not practice): WEATHER (CLEAR / RAIN / SNOW / DYNAMIC) and WEATHER INTENSITY for this ride
//           (Rendering/ReflexWeather.cs; rain and snow also take grip off every tyre)
//   RIDER   Vehicle Select's choice: VEHICLE (the types of the event's raceable classes), CLASS (LocationSelect's classes for the
//           type), RIDER (the player's own on the profile's MX Lite, or a pro with his vehicle of the type)
//           ANY VEHICLE ON ANY TRACK (core/plan/ANY_VEHICLE.md, ON by default; the toggle under RIDER): VEHICLE offers every vehicle and CLASS
//           every class on every event; a choice the event never had (OpenSetup.OffEvent) says so here, on the summary and in place of the
//           LEADERBOARD block: the computer riders ride the CLASS's vehicles, no records are kept and no laps posted.
// START EVENT launches (OpenSetup.Launch -> ReflexEventSelect.Launch); Circle / B, CHANGE EVENT: the browser again, on the card.
// PRACTICE: the same page with the practice setup (OpenHistory.PracticeSetupOf): EVENT has no duration and no AI, only MAP SCALE (laps at
// another scale are not counted); RIDER as the race's; START PRACTICE launches the event as an endless lap race for one rider.
using System;
using System.Collections.Generic;
using System.Linq;
using Reflex.Port.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.OpenFrontEnd
{
    public sealed partial class OpenShell
    {
        static readonly string[] SetupTabs = { "EVENT", "RIDER", "PLAYERS" };
        const int PlayersTab = 2;   // SPLIT SCREEN (w-split): the tab a second controller joins on
        sealed class SetupPage
        {
            public VisualElement root, hero, map, heroCopy, cards, actions, summary;
            public ScrollView rows;
            public Label badge, title, sub;
            public readonly List<VisualElement> pages = new List<VisualElement>(), tabItems = new List<VisualElement>();
            public int tab;
        }
        SetupPage _sp;
        int _spTab;
        VisualElement _startButton;

        /// The setup page's START EVENT (tests).
        public VisualElement StartButton => _startButton;

        void ReleaseSetupPage() { if (OuMenuInput.PadPress == _padHook) OuMenuInput.PadPress = null; _sp = null; _startButton = null; _spPreviewOn = false; _spSpecsValid = false; _layer?.RemoveFromClassList("ou-setup-3d-layer"); }

        /// The setup page of the browser's event.
        public void ShowSetupPage()
        {
            var ev = _browseEvent;
            if (ev == null || ev.Broken) return;   // (a broken custom track is never started: the browser says why)
            if (_setup == null || _setup.practice != _practice) _setup = _practice ? OpenHistory.PracticeSetupOf(ev.key) : OpenHistory.SetupOf(ev.key);
            _setup.eventKey = ev.key;
            Clear(Screen.Setup);
            _spSpecsValid = false;
            _layer.AddToClassList("mxa-dim");
            MusicTier(5);   // LocationSelect:Start
            var right = OuTheme.Header(_layer, _practice ? "PRACTICE" : _freeRide ? "FREE RIDE" : "RACE", ev.Label);
            var change = OuUi.Button(_practice ? "CHANGE TRACK" : _freeRide ? "CHANGE LOCATION" : "CHANGE EVENT", "ou-chip", BackToBrowser, right);
            change.name = "ou-setup-change";

            _sp = BuildSetupPage(ev.ev != null && !_setup.Rules(ev.ev) ? 1 : _spTab);   // free ride: nothing to set on EVENT, its RIDER first
            ShowPreview(ev, _sp.hero, _sp.map);
            _sp.badge.text = ev.typeName;
            _sp.title.text = ev.locationName;
            _sp.sub.text = ev.Label + "  ·  " + ev.Where;
            OuTheme.Section(_sp.cards, "THE EVENT");
            if (!string.IsNullOrEmpty(ev.description)) OuUi.Text(ev.description, "mxa-note ou-setup-text", _sp.cards);
            _sp.summary = OuUi.Div("ou-setup-summary", _sp.cards);
            if (!string.IsNullOrEmpty(ev.typeDescription))
            {
                OuTheme.Section(_sp.cards, ev.typeName);
                OuUi.Text(ev.typeDescription, "mxa-note ou-setup-text", _sp.cards);
                if (!string.IsNullOrEmpty(ev.typeVehicles)) OuUi.Text(ev.typeVehicles, "mxa-note ou-setup-text ou-faint", _sp.cards);
            }
            _startButton = OuUi.Button(SetupStartText(ev), "mxa-start ou-cta", StartFromSetup, _sp.actions);
            _startButton.name = "ou-setup-start";
            OuTheme.Backdrop(_startButton);
            SetupRulesButton(ev, _sp.actions);   // the event type's rules (OpenShell.Popups.cs: RulesPopup), where the original shows them
            BuildSetupRows();
            _padHook ??= SetupPadPress; OuMenuInput.PadPress = _padHook;   // SPLIT SCREEN: a second controller's A / B on the PLAYERS tab
            _nav.OnTab = d => { StepSetupTab(d); if (_setup.Rules(ev.ev)) _spTab = _sp.tab; };
            _nav.OnCancel = () => { BackToBrowser(); return true; };
            _nav.OnAlt = null; _nav.OnFind = null;
            _nav.DefaultItem = () => OuUi.FirstItem(_sp.pages[_sp.tab]) ?? _startButton;
            SetupHints();
            _nav.Focus(_startButton);
        }

        /// START EVENT: the event type's rules page first when the original would show it (Freestyle, Section Race: RulesPopup until "do not show again").
        void StartFromSetup()
        {
            var ev = _browseEvent; if (ev == null) return;
            if (ChampFormatOn(ev)) { ShowChampWeekendSetup(); return; }   // RESTORED CONTENT: FORMAT CHAMPIONSHIP WEEKEND: its setup page (OpenShell.ChampWeekend.cs)
            string rules = ArcadeRulesType(ev.ev);
            if (rules != null && RulesDue(rules)) { ShowRulesPopup(rules, () => StartEvent(_browseEvent, _setup)); return; }
            StartEvent(_browseEvent, _setup);
        }

        /// A RULES chip under START EVENT for the event types that have rules (STR_RULES): the page again, whenever the player wants it.
        void SetupRulesButton(OpenEvent ev, VisualElement actions)
        {
            string rules = ArcadeRulesType(ev.ev);
            if (rules == null) return;
            var chip = OuUi.Button(OpenContent.Str("STR_RULES", "RULES").ToUpperInvariant(), "ou-chip ou-setup-rules", () => ShowRulesPopup(rules, null, true), actions);
            chip.name = "ou-setup-rules";
        }

        void BackToBrowser()
        {
            if (_sp != null && _browseEvent != null && _setup != null && _setup.Rules(_browseEvent.ev)) _spTab = _sp.tab;
            if (_setup != null) OpenHistory.SaveSetup(_setup);
            ShowBrowser(BrowseMode);
        }

        SetupPage BuildSetupPage(int tab)
        {
            var p = new SetupPage();
            p.root = OuTheme.Clone("Setup", _layer) ?? OuUi.Div("ou-setup", _layer);
            var info = OuTheme.Part(p.root, "setup-info", "ou-setup-info");
            p.hero = OuTheme.Part(info, "setup-hero", "ou-setup-hero");
            OuTheme.Part(p.hero, "setup-hero-shade", "ou-setup-hero-shade").style.backgroundImage = OuUi.ScrimBottom;
            p.map = OuTheme.Part(p.hero, "setup-map", "ou-setup-map");
            p.heroCopy = OuTheme.Part(p.hero, "setup-hero-copy", "ou-setup-hero-copy");
            var badges = OuUi.Div("ou-setup-badges", p.heroCopy);
            p.badge = OuUi.Text("", "ou-badge", badges);
            p.title = OuUi.Text("", "ou-setup-title", p.heroCopy);
            p.sub = OuUi.Text("", "ou-setup-sub", p.heroCopy);
            var cards = p.root.Q<ScrollView>("setup-cards") ?? AddScroll(info, "ou-setup-cards");
            cards.verticalScrollerVisibility = ScrollerVisibility.Auto;
            cards.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            p.cards = cards.contentContainer;

            var panel = OuTheme.Part(p.root, "setup-panel", "ou-panel ou-setup-panel");
            var tabs = OuTheme.Part(panel, "setup-tabs", "ou-gtabs ou-setup-tabs");
            tabs.Clear();
            string[] keys = OuUi.KeyTabs.Split('/');
            OuUi.Text(keys[0].Trim(), "mxa-key ou-gtabs-key", tabs);
            for (int i = 0; i < SetupTabs.Length; i++)
            {
                int t = i;
                var b = OuUi.Button(SetupTabs[i], "ou-gtab", () => SetSetupTab(t, true), tabs);
                b.name = "ou-stab-" + SetupTabs[i].ToLowerInvariant();
                OuUi.Div("ou-gtab-bar", b);
                p.tabItems.Add(b);
            }
            OuUi.Text(keys.Length > 1 ? keys[1].Trim() : "", "mxa-key ou-gtabs-key", tabs);
            p.rows = p.root.Q<ScrollView>("setup-rows") ?? AddScroll(panel, "mxa-option-rows ou-setup-rows");
            p.rows.verticalScrollerVisibility = ScrollerVisibility.Auto;
            p.rows.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            for (int i = 0; i < SetupTabs.Length; i++)
            {
                int t = i;
                var page = OuUi.Div("ou-spage", p.rows.contentContainer);
                page.name = "ou-spage-" + SetupTabs[i].ToLowerInvariant();
                page.RegisterCallback<FocusInEvent>(_ => { if (_sp != null && _sp.tab != t) SetSetupTab(t, false); });
                p.pages.Add(page);
            }
            p.actions = OuTheme.Part(panel, "setup-actions", "ou-setup-actions");
            p.tab = Mathf.Clamp(tab, 0, SetupTabs.Length - 1);
            return p;
        }

        void SetSetupTab(int tab, bool focus)
        {
            var p = _sp; if (p == null) return;
            p.tab = Mathf.Clamp(tab, 0, p.pages.Count - 1);
            for (int i = 0; i < p.pages.Count; i++)
            {
                p.pages[i].style.display = i == p.tab ? DisplayStyle.Flex : DisplayStyle.None;
                p.tabItems[i].EnableInClassList("ou-gtab-on", i == p.tab);
            }
            p.rows.scrollOffset = Vector2.zero;
            SetupPreviewSync();
            if (!focus) return;
            var page = p.pages[p.tab];
            page.schedule.Execute(() => { if (page.panel != null && _sp == p && p.pages[p.tab] == page) _nav.Focus(OuUi.FirstItem(page) ?? _startButton); }).ExecuteLater(1);
        }

        void StepSetupTab(int d)
        {
            var p = _sp; if (p == null) return;
            int n = p.pages.Count;
            for (int k = 1; k <= n; k++)
            {
                int t = ((p.tab + d * k) % n + n) % n;
                if (p.tabItems[t].style.display != DisplayStyle.None) { SetSetupTab(t, true); return; }
            }
        }

        /// The setup's rows on their tabs, rebuilt when a choice changes what the others offer (keeps the focused row).
        void BuildSetupRows()
        {
            var p = _sp; if (p == null) return;
            string focused = _nav.Focused != null && p.rows.Contains(_nav.Focused) ? _nav.Focused.name : null;
            foreach (var pg in p.pages) pg.Clear();
            var ev = _browseEvent; var e = ev.ev; var s = _setup;
            bool rules = s.Rules(e);

            // ---- EVENT
            var page = p.pages[0];
            if (s.practice)
            {   // PRACTICE: no duration, no field
                OuTheme.Section(page, "PRACTICE");
                OuUi.Text("Unlimited laps on your own. The first lap, from the gate, is timed but never counts: every lap after it starts at the line. A clean lap (no reset, no cut, at most 2 s off the track) is your lap time.", "mxa-note", page);
            }
            else if (rules)
            {
                // RESTORED CONTENT: FORMAT SINGLE RACE / CHAMPIONSHIP WEEKEND (OpenShell.ChampWeekend.cs); a weekend's National / Supercross stages
                // have their own clocks and laps (no EVENT DURATION here)
                bool weekend = SetupFormatRow(ev, page), stageTimes = weekend && Reflex.Port.RxCore.RxChampWeekend.DisciplineOf(ev.activity) <= Reflex.Port.RxCore.RxChampWeekend.Supercross;
                OuTheme.Section(page, ev.activity == "Freestyle" ? "FREESTYLE" : "RACE");
                var d = s.Durations(e, out bool laps, out string singular, out string plural);
                if (stageTimes) d = new List<int>();
                if (d.Count > 0)
                {
                    OuUi.Spinner("EVENT DURATION", () => { int i = s.DurationIndex(d); return d[i] + " " + OpenContent.Str(d[i] > 1 ? plural : singular, laps ? (d[i] > 1 ? "LAPS" : "LAP") : "").ToUpperInvariant(); },
                        k => { s.duration = (s.DurationIndex(d) + k + d.Count) % d.Count; SetupChanged(false); }, page).name = "ou-setup-duration";
                    if (!laps) OuUi.Text("The game's own duration for this event type.", "mxa-note", page);
                }
                OuTheme.Section(page, "THE FIELD");
                OuUi.Spinner("AI OPPONENTS", () => OpenContent.AiName(s.Ai(e)), k => { bool was = s.Ai(e) > 0; s.ai = (s.Ai(e) + k + 5) % 5; SetupChanged(was != s.Ai(e) > 0); }, page).name = "ou-setup-ai";   // (AI on / off: the FIELD SIZE row follows)
                OpenFieldSize.Row(page, e, () => s.Ai(e) > 0, () => s.riders, n => { s.riders = n; SetupChanged(false); });   // RIDER CAP (w-rc-client)
                // AI ROSTER: the B.R.A.A.P. roster the computer riders' names, numbers and looks come from (OpenShell.Braap.cs edits them)
                if (s.Ai(e) > 0)
                    OuUi.Spinner("AI ROSTER", () => ReflexBraap.Active.name.ToUpperInvariant(), k =>
                    {
                        var all = ReflexBraap.All().Where(x => x.IsDefault || x.EnabledCount > 0).ToList();
                        int i = Math.Max(0, all.FindIndex(x => x.id == ReflexBraap.ActiveId));
                        ReflexBraap.ActiveId = all[((i + k) % all.Count + all.Count) % all.Count].id;
                    }, page).name = "ou-setup-roster";
            }
            else
            {
                OuTheme.Section(page, "FREE RIDE");
                OuUi.Text("The whole location, no laps and no rivals. Pick your vehicle and rider on RIDER.", "mxa-note", page);
            }
            SetupMapScaleRow(e, page);
            if (!s.practice) SetupWeatherRows(s, page);   // (PRACTICE rides CLEAR)

            // ---- RIDER
            page = p.pages[1];
            OuTheme.Section(page, "YOUR RIDE");
            var types = OpenSetup.VehicleTypes(e);
            if (types.Count > 0)
                OuUi.Spinner("VEHICLE", () => OpenContent.VehicleTypeName(s.Type(e)), k =>
                {
                    int i = Math.Max(0, types.IndexOf(s.Type(e)));
                    s.vehicleChoiceExplicit = true; s.vehicleType = types[(i + k + types.Count) % types.Count]; s.vehicleClass = 0; s.rider = -1;
                    SetupChanged(true);
                }, page).name = "ou-setup-vehicle";
            var classes = s.Classes(e);
            if (rules && classes.Length > 0)
                OuUi.Spinner("CLASS", () => OpenContent.ClassName(s.Class(e)), k => { s.vehicleClass = (Mathf.Clamp(s.vehicleClass, 0, classes.Length - 1) + k + classes.Length) % classes.Length; SetupChanged(false); }, page).name = "ou-setup-class";
            var riders = OpenSetup.Riders(e, s.Type(e));
            if (riders.Count > 0)
                OuUi.Spinner("RIDER", () => riders.FirstOrDefault(r => r.id == s.Rider(e)).name ?? "-", k =>
                {
                    int i = Math.Max(0, riders.FindIndex(r => r.id == s.Rider(e)));
                    s.vehicleChoiceExplicit = true; s.rider = riders[(i + k + riders.Count) % riders.Count].id; SetupChanged(false);
                }, page).name = "ou-setup-rider";
            // ANY VEHICLE ON ANY TRACK (core/plan/ANY_VEHICLE.md): the toggle (also OPTIONS > OPEN REFLEX), and what an off-event choice means
            if (rules && Reflex.Port.RxCore.Bridge.OpenReflexSelector.UseOpenReflex && OpenSetup.AnyVehicleCould(e))
            {
                OuUi.Toggle("ANY VEHICLE ON ANY TRACK", () => Reflex.Port.Options.ReflexSettings.AnyVehicle, v =>
                {
                    Reflex.Port.Options.ReflexSettings.AnyVehicle = v; Reflex.Port.Options.ReflexSettings.Save();
                    s.vehicleClass = 0; SetupChanged(true);
                }, page).name = "ou-setup-anyvehicle";
                var note = OuUi.Text(AnyVehicleNote(e, s), "mxa-note", page); note.name = "ou-setup-anyvehicle-note";
            }
            OuUi.Text(Reflex.Port.RxCore.Bridge.OpenReflexSelector.UseOpenReflex ? "Your saved GARAGE selection is used until you choose a vehicle or rider here. GARAGE on the hub saves your gear, vehicles and tuning." : "As the game's VEHICLE SELECT: your own rider races the MOTOCARD's MX Lite; the pros race their own vehicles of the type. GARAGE (on the hub) changes your rider's gear and vehicles.", "mxa-note", page);
            OuTheme.Section(page, "THE WORLD");
            OuUi.Text("Every setup is its own world, made once from your install (about 15 to 40 s) and kept in the world cache (GAME LIBRARY).", "mxa-note", page);

            // ---- PLAYERS (SPLIT SCREEN, w-split): who rides on this screen; hidden when the player hid it (OPTIONS too)
            if (PlayersOffered) SetupPlayersPage(p.pages[PlayersTab]);

            for (int i = 0; i < p.pages.Count; i++) p.tabItems[i].style.display = OuUi.FirstItem(p.pages[i]) != null || i == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (p.tabItems[p.tab].style.display == DisplayStyle.None) p.tab = 0;
            SetSetupTab(p.tab, false);
            FillSummary(p.summary, ev, s);
            RefreshSetupPlayers();
            if (focused != null) { var again = p.rows.Q<VisualElement>(focused); if (again != null) _nav.Focus(again); }
        }

        /// The RIDER tab's line under ANY VEHICLE ON ANY TRACK: what the choice comes to.
        static string AnyVehicleNote(ReflexEvents.Event e, OpenSetup s)
        {
            if (!Reflex.Port.Options.ReflexSettings.AnyVehicle) return "OFF: VEHICLE and CLASS offer what the original game allows at this event. ON: every vehicle and every class, here and on every track.";
            if (!s.OffEvent(e)) return "Every vehicle and every class can be chosen on every track. This choice is one of the event's own: records and lap times count as usual.";
            int cls = s.Class(e);
            string field = cls < 0 ? "" : " The computer riders ride " + OpenContent.ClassName(cls) + " (" + string.Join(", ", ReflexAtv.TypesOfClass(cls).Select(OpenContent.VehicleTypeName)) + ").";
            return (OpenSetup.EventHasType(e, s.Type(e)) ? OpenContent.ClassName(cls) + " is not one of this event's own classes." : OpenContent.VehicleTypeName(s.Type(e)) + " is not one of this event's own vehicles.")
                   + field + " No records, best laps or highlights are kept and no lap is posted. The track and its start gate were built for the event's own vehicles: expect a tight fit.";
        }

        /// MAP SCALE (core/plan/MAP_SCALE.md, after Open Unleashed's EventSetup row): the whole track scaled about its centre, 50 % to 200 % in
        /// 5 % steps, the vehicles and riders real size. Left / right step it (pad, keys, the arrows with the mouse), SELECT puts it back to
        /// 100 %. Kept per track in the client's settings (ReflexSettings.MapScalePercent; 100 % removes it), not in this setup or the save.
        /// BASE SCALE (w-alivegates): on a track with a default scale of its own (an Alive race level) 100 % (ORIGINAL) is that default and the
        /// percent is relative to it (OpenSetup.MapScaleOf).
        void SetupMapScaleRow(ReflexEvents.Event e, VisualElement page)
        {
            if (!OpenSetup.HasMapScale(e)) return;
            OuTheme.Section(page, "THE TRACK");
            var row = OuUi.Spinner("MAP SCALE", () => MapScaleText(OpenSetup.MapScalePercent(e)), k =>
            {
                int was = OpenSetup.MapScalePercent(e), now = Reflex.Port.RxCore.RxMapScale.ClampPercent(was + k * Reflex.Port.RxCore.RxMapScale.StepPercent);
                if (now == was) return;   // at a limit: no wrap
                OpenSetup.SetMapScalePercent(e, now);
                SetupChanged(false);
                UpdateMapScaleHint(e);
            }, page, () => { OpenSetup.SetMapScalePercent(e, Reflex.Port.RxCore.RxMapScale.DefaultPercent); SetupChanged(false); UpdateMapScaleHint(e); });
            row.name = "ou-setup-mapscale";
            _mapScaleHint = OuUi.Text("", "mxa-note", page);
            _mapScaleHint.name = "ou-setup-mapscale-hint";
            UpdateMapScaleHint(e);
        }
        Label _mapScaleHint;

        // ---- PLAYERS (SPLIT SCREEN, w-split; RxCore/RxSplitScreen.cs): a second controller joins here with A (B on it leaves); the two riders then
        // share the screen, top and bottom, player 2 on the joined controller with player 1's rider, gear and vehicle. The joined controller is
        // remembered for the session (LocalGamepadRouting.Player2Pad), not per event. HIDE THIS TAB (also OPTIONS > OPEN REFLEX > RACE SETUP >
        // PLAYERS TAB) takes the tab away for players who only ride solo.
        Func<UnityEngine.InputSystem.Gamepad, bool, bool> _padHook;
        int _padFrame = -1; UnityEngine.InputSystem.Gamepad _padLast; bool _padCancel, _padTaken;
        Label _playersNote;

        bool PlayersOffered => _setup != null && Reflex.Port.Options.ReflexSettings.PlayersTab;

        /// OuMenuInput.PadPress while the setup page is up: the same answer for every read of a frame.
        bool SetupPadPress(UnityEngine.InputSystem.Gamepad pad, bool cancel)
        {
            if (Time.frameCount == _padFrame && pad == _padLast && cancel == _padCancel) return _padTaken;
            _padFrame = Time.frameCount; _padLast = pad; _padCancel = cancel;
            return _padTaken = SetupPadPressNow(pad, cancel);
        }
        bool SetupPadPressNow(UnityEngine.InputSystem.Gamepad pad, bool cancel)
        {
            if (_sp == null || Current != Screen.Setup || !PlayersOffered) return false;
            var p2 = Reflex.Port.OriginalWorld.LocalGamepadRouting.Player2Pad;
            if (cancel)
            {   // B on player 2's controller: player 2 leaves (never CHANGE EVENT)
                if (p2 == null || pad != p2) return false;
                SetPlayer2(null); return true;
            }
            if (p2 != null || _sp.tab != PlayersTab) return false;
            // A on the PLAYERS tab from a controller that is not player 1's (the one that drove the menus): player 2 joins
            if (pad == Reflex.Port.OriginalWorld.LocalGamepadRouting.MenuPad) return false;
            SetPlayer2(pad); return true;
        }

        void SetPlayer2(UnityEngine.InputSystem.Gamepad pad)
        {
            Reflex.Port.OriginalWorld.LocalGamepadRouting.Player2Pad = pad;
            OuSound.Play(pad != null ? "Accept" : "Back");
            Debug.Log(pad != null ? "[OpenReflex] SPLIT SCREEN: " + pad.displayName + " joined as player 2" : "[OpenReflex] SPLIT SCREEN: player 2 left");
            if (_sp != null) BuildSetupRows();
        }

        static void SetupInfo(VisualElement parent, string label, string value)
        {
            var row = OuUi.Div("mxa-row ou-online-info", parent);
            OuUi.Text(label, "mxa-row-label", row);
            OuUi.Text(value, "mxa-value ou-online-value", row).enableRichText = false;
        }

        void SetupPlayersPage(VisualElement page)
        {
            var p2 = Reflex.Port.OriginalWorld.LocalGamepadRouting.Player2Pad;
            OuTheme.Section(page, "ON THIS SCREEN");
            SetupInfo(page, "PLAYER 1", PlayerOneName() + (p2 != null ? "  ·  TOP" : ""));
            var row = OuUi.Spinner("PLAYER 2", () => p2 != null ? "JOINED  ·  BOTTOM" : "PRESS A ON ANOTHER CONTROLLER", _ => { }, page, () =>
            {
                if (Reflex.Port.OriginalWorld.LocalGamepadRouting.Player2Pad != null) SetPlayer2(null);
                else if (_playersNote != null) _playersNote.text = "Press A on the OTHER controller: that one becomes player 2.";
            });
            row.name = "ou-setup-player2";
            foreach (var arrow in row.Query<Label>(className: "mxa-arrow").ToList()) arrow.style.display = DisplayStyle.None;
            int pads = UnityEngine.InputSystem.Gamepad.all.Count;
            _playersNote = OuUi.Text(p2 != null
                ? "Player 2 rides on " + p2.displayName + " with your rider, gear and vehicle, on the bottom half of the screen. B on that controller, or SELECT here, and player 2 leaves."
                : pads < 2 ? "Split screen: two riders on one screen. Connect a second controller, then press A on it here (" + pads + " connected now)."
                : "Split screen: two riders on one screen. Press A on the second controller to join; B on it leaves again.", "mxa-note", page);
            _playersNote.name = "ou-setup-players-note";
            if (_setup.practice) OuUi.Text("PRACTICE: only player 1's laps are timed, kept, posted to the leaderboards and recorded as ghosts. Player 2 rides along: nothing of player 2's laps is saved.", "mxa-note", page);
            OuUi.Text("Each rider gets half the screen with a full HUD. The finish, the replay and the results show player 1's view, and either controller can leave the race.", "mxa-note", page);
            OuTheme.Section(page, "RIDING SOLO?");
            OuUi.Button("HIDE THIS TAB", "ou-chip ou-setup-hide-players", ConfirmHidePlayersTab, page).name = "ou-setup-hide-players";
            OuUi.Text("Hides PLAYERS from every race, Free Ride and practice setup. You can show it again any time in OPTIONS  ›  OPEN REFLEX  ›  PLAYERS TAB.", "mxa-note", page);
        }

        static string PlayerOneName()
        {
            string n = null;
            try { n = Reflex.Port.Content.ReflexProfile.Persona; } catch (Exception) { }
            return string.IsNullOrWhiteSpace(n) ? "YOU" : n.ToUpperInvariant();
        }

        void ConfirmHidePlayersTab()
        {
            OpenPopup("popup-hide-players", "HIDE THE PLAYERS TAB?", null,
                "The race, Free Ride and practice setup will show only EVENT and RIDER, for riding solo. You can turn the PLAYERS tab back on any time in OPTIONS  ›  OPEN REFLEX  ›  PLAYERS TAB.",
                () => { }, 0, false,
                ("HIDE IT", "popup-hide", () =>
                {
                    Reflex.Port.Options.ReflexSettings.PlayersTab = false; Reflex.Port.Options.ReflexSettings.Save();
                    Reflex.Port.OriginalWorld.LocalGamepadRouting.Player2Pad = null;
                    if (_sp == null) return;
                    _sp.tab = 0; BuildSetupRows(); SetSetupTab(0, true);
                }),
                ("KEEP IT", "popup-keep", null));
        }

        /// The PLAYERS tab's name (with the riders on this screen once a second joined), START's label and the hint bar.
        void RefreshSetupPlayers()
        {
            if (_sp == null) return;
            bool two = _setup != null && _setup.SplitScreen;
            var tab = _sp.tabItems[PlayersTab].Q<Label>(className: "mxa-button-label");
            if (tab != null) tab.text = two ? "PLAYERS  ·  2" : "PLAYERS";
            var start = _startButton?.Q<Label>(className: "mxa-button-label");
            if (start != null && _browseEvent != null) start.text = SetupStartText(_browseEvent);
            SetupHints();
        }

        /// START's label: START EVENT / START RIDE ..., "· 2 PLAYERS" with a second controller joined.
        string SetupStartText(OpenEvent ev) => SetupStartLabel(ev) + (_setup != null && _setup.SplitScreen ? "  ·  2 PLAYERS" : "");

        void SetupHints()
        {
            var tabs = new List<string> { SetupTabs[0], SetupTabs[1] };
            if (PlayersOffered) tabs.Add(SetupTabs[PlayersTab]);
            Hints((OuUi.KeySubmit, "SELECT"), (OuUi.KeyCancel, _practice ? "CHANGE TRACK" : _freeRide ? "CHANGE LOCATION" : "CHANGE EVENT"), (OuUi.KeyTabs, string.Join(" · ", tabs)));
        }

        /// WEATHER and WEATHER INTENSITY (Rendering/ReflexWeather.cs), per ride: this setup's choice (OpenSetup.weather / weatherIntensity), the
        /// last pick also the default of setups that never chose. Weather needs LIGHTING: ENHANCED, so a pick other than CLEAR switches it on
        /// (as the old OPTIONS > VIDEO rows did). SELECT puts WEATHER back to CLEAR / INTENSITY to MEDIUM.
        void SetupWeatherRows(OpenSetup s, VisualElement page)
        {
            OuTheme.Section(page, "CONDITIONS");
            var names = Reflex.Port.Rendering.ReflexWeather.Names; var inames = Reflex.Port.Rendering.ReflexWeather.IntensityNames;
            void PickWeather(int w)
            {
                s.weather = w; Reflex.Port.Rendering.ReflexWeather.PickDefault(w);
                SetupChanged(false); UpdateWeatherHint(s);
            }
            void PickIntensity(int i)
            {
                s.weatherIntensity = i; Reflex.Port.Rendering.ReflexWeather.PickDefaultIntensity(i);
                SetupChanged(false); UpdateWeatherHint(s);
            }
            OuUi.Spinner("WEATHER", () => names[s.Weather], k => PickWeather(((s.Weather + k) % 4 + 4) % 4), page, () => PickWeather(0)).name = "ou-setup-weather";
            OuUi.Spinner("WEATHER INTENSITY", () => inames[s.WeatherIntensity], k => PickIntensity(((s.WeatherIntensity + k) % 3 + 3) % 3), page, () => PickIntensity(1)).name = "ou-setup-weather-intensity";
            _weatherHint = OuUi.Text("", "mxa-note", page);
            _weatherHint.name = "ou-setup-weather-hint";
            UpdateWeatherHint(s);
        }
        Label _weatherHint;
        void UpdateWeatherHint(OpenSetup s)
        {
            if (_weatherHint == null) return;
            int w = s.Weather;
            _weatherHint.text = w == 0
                ? "CLEAR: the track and its grip as the original game has them. RAIN, SNOW and DYNAMIC are not drawn in the supercross stadiums."
                : (w == 1 ? "RAIN: lit, wind-blown rain, a grey sky and mist, the ground dark and glossy with puddles in the ruts. The wet ground takes grip off every tyre, yours and the AI's (about 15 % at HEAVY)."
                   : w == 2 ? "SNOW: falling snow, the ground snowed over, every rider's ruts and tyre tracks cut through it. The snow takes grip off every tyre, yours and the AI's (about 30 % at HEAVY)."
                   : "DYNAMIC: showers come and go, the ground wets and dries, snow settles and melts; the grip follows the ground (less when wet, least on snow).")
                  + " INTENSITY sets how hard it rains or snows and how much grip it takes (LIGHT about 0.4x, MEDIUM about 0.7x of HEAVY).";
        }

        static string MapScaleText(int percent) => percent == Reflex.Port.RxCore.RxMapScale.DefaultPercent ? "100 %  (ORIGINAL)" : Reflex.Port.RxCore.RxMapScale.Label(percent);
        void UpdateMapScaleHint(ReflexEvents.Event e)
        {
            if (_mapScaleHint == null) return;
            int p = OpenSetup.MapScalePercent(e);
            // BASE SCALE: an Alive race level's 100 % is its own default (110 / 130 % of Alive's layout: AliveLevels.MapScaleBase)
            int b = Reflex.Port.RxCore.RxMapScale.Percent(OpenSetup.MapScaleBase(e));
            string based = b != 100 ? "  100\u00A0% here is " + b + "\u00A0% of the MX vs ATV Alive layout (this track's default)." : "";
            _mapScaleHint.text = p == Reflex.Port.RxCore.RxMapScale.DefaultPercent
                ? "Scales the whole track; bikes and riders stay real size. SELECT: 100\u00A0%." + based + (_setup?.practice == true ? "  Laps at another scale are not counted." : "")
                : (p < 100 ? "Smaller track, more realistic" : "Bigger track, more arcade") + (_setup?.practice == true ? "  ·  laps not counted" : "  ·  no records kept") + "  ·  SELECT: 100\u00A0%.";
        }

        /// A choice changed: saved, the summary updated (rebuild: the rows that depend on it too).
        void SetupChanged(bool rebuild)
        {
            OpenHistory.SaveSetup(_setup);
            _spSpecsValid = false;      // (the choice's rider and vehicle are looked up again)
            _worldScreenDirty = true;   // the vehicle / rider preview follows the choice
            if (rebuild) BuildSetupRows();
            else if (_sp != null) FillSummary(_sp.summary, _browseEvent, _setup);
        }

        float _summaryAt;
        void UpdateSetupPage()
        {
            SetupPreviewSync();
            // the world's state (a build in GAME LIBRARY finishing) every second
            if (_sp == null || Time.unscaledTime < _summaryAt) return;
            _summaryAt = Time.unscaledTime + 1f;
            // (the chips are made again only when the world's state changed: a rebuild of the summary every second re-laid out the page; w-menufps)
            if (_browseEvent?.ev == null || _setup == null || WorldText(_browseEvent.ev, _setup) != _summaryWorld) FillSummary(_sp.summary, _browseEvent, _setup);
        }

        // ---- the vehicle preview (the original's Vehicle Select shows the chosen vehicle in 3D: VehicleSelect_Cameras.lua over the UI world)
        // On the RIDER tab the location picture gives way to a window on the 3D menu world (the compound's hero spot): the vehicle and rider the
        // choices come to (YOUR RIDER on the garage's vehicle of the type with the equipped gear; a pro on his own vehicle of it), built where it
        // is chosen (the HR models read on a worker thread: navigation is never blocked), the world otherwise as every other page leaves it.
        bool _spPreviewOn;

        /// The vehicle and rider of the current VEHICLE / RIDER choice, as the garage would put them on the stage.
        /// Kept until a choice changes or the page goes (w-menufps: SetupPreviewSync asks every frame on the RIDER tab, and the lookup walks the
        /// garage's vehicle list: 70 ms a frame on the developer's Mac, the RIDER tab at 12 fps).
        bool SetupPreviewSpecs(out BikeSpec bike, out RiderSpec rider, out string key)
        {
            var ev = _browseEvent?.ev;
            if (!_spSpecsValid || _spSpecsEvent != ev || _spSpecsSetup != _setup)
            {
                using (Platform.ReflexMenuPerf.Measure("setup hero lookup"))
                    _spSpecsOk = EventHeroSpecs(ev, _setup, out _spSpecsBike, out _spSpecsRider, out _spSpecsKey);
                _spSpecsValid = true; _spSpecsEvent = ev; _spSpecsSetup = _setup;
            }
            bike = _spSpecsBike; rider = _spSpecsRider; key = _spSpecsKey;
            return _spSpecsOk;
        }
        bool _spSpecsValid, _spSpecsOk; ReflexEvents.Event _spSpecsEvent; OpenSetup _spSpecsSetup; BikeSpec _spSpecsBike; RiderSpec _spSpecsRider; string _spSpecsKey;

        /// The rider and the vehicle an event's setup rides (a pro's, or the player's own of the setup's vehicle type), for the 3D menu world.
        bool EventHeroSpecs(ReflexEvents.Event e, OpenSetup s, out BikeSpec bike, out RiderSpec rider, out string key)
        {
            bike = null; rider = null; key = null;
            if (e == null || s == null || !Reflex.Port.RxCore.Bridge.OpenReflexSelector.UseOpenReflex || ReflexProfile.Disabled) return false;
            try
            {
                var open = ReflexProfile.Open();
                if (!open.IsCompletedSuccessfully || !open.Result || OpenGarageData.P == null) return false;
                int type = s.Type(e), who = s.Rider(e);
                if (who >= 0)
                {
                    var ride = OpenGarageData.ProRides().FirstOrDefault(r => r.rider == who && r.vehicleType == type);
                    if (ride == null) return false;
                    (bike, rider) = OpenGarageData.ProSpecs(ride);
                    key = "setup:pro:" + who + ":" + ride.vehicle;
                    return bike != null;
                }
                var ownType = OpenSetup.OwnTypeOf(type);
                if (ownType != null)
                {   // an Alive class: its base model drawn with its .rxmod bike
                    var g0 = OpenGarageData.EquippedGearIds();
                    bike = OpenGarageData.BikeOfModelAs(ownType.model, ownType.modId); rider = OpenGarageData.RiderWithGear(g0);
                    key = "setup:alive:" + ownType.model + ":" + ownType.modId + ":" + string.Join(",", g0) + ":m" + BikeMods.Version;
                    return bike != null;
                }
                var own = OpenGarageData.Vehicles().Where(v => v.owned && v.type == type).OrderBy(v => v.equipped ? 0 : 1).FirstOrDefault();
                if (own == null) return false;
                var gear = OpenGarageData.EquippedGearIds();
                bike = OpenGarageData.BikeOfModel(own.id); rider = OpenGarageData.RiderWithGear(gear);
                key = "setup:own:" + own.id + ":" + string.Join(",", gear) + ":m" + BikeMods.Version;
                return bike != null;
            }
            catch (Exception ex) { Debug.LogWarning("[Setup] vehicle preview: " + ex.Message); return false; }
        }

        /// The picture / 3D window of the setup's hero follows the tab (and the world becoming ready).
        void SetupPreviewSync()
        {
            var p = _sp; if (p == null || _browseEvent == null) return;
            bool on = p.tab == 1 && MenuWorld != null && Current == Screen.Setup && SetupPreviewSpecs(out _, out _, out _);
            if (on == _spPreviewOn) return;
            _spPreviewOn = on;
            p.hero.EnableInClassList("ou-setup-hero-3d", on);
            p.root.EnableInClassList("ou-setup-3d", on);
            _layer.EnableInClassList("ou-setup-3d-layer", on);   // the page's dim stage lets the world through (OuCareer.uss)
            if (on) { p.hero.style.backgroundImage = new StyleBackground(StyleKeyword.None); p.map.style.display = DisplayStyle.None; }
            else ShowPreview(_browseEvent, p.hero, p.map);
            _worldScreenDirty = true;
        }

        /// WorldScreen's setup case: true when the window is the page's (the hero stands in it); the dim lets it through.
        bool SetupPreviewWorld(OpenMenuWorld w)
        {
            if (!_spPreviewOn || _sp == null) { _worldDim.style.opacity = 1f; return false; }
            var full = Root.worldBound; var r = _sp.hero.worldBound;
            if (float.IsNaN(r.width) || r.width < 16 || r.height < 16 || full.width < 16) return true;   // not laid out: next frame
            if (!SetupPreviewSpecs(out var bike, out var rider, out var key)) { _worldDim.style.opacity = 1f; return false; }
            _worldDim.style.opacity = 0.2f;
            var center = new Vector2((r.center.x - full.center.x) / (full.width * 0.5f), -(r.center.y - full.center.y) / (full.height * 0.5f));
            w.HubMode(center, r.height / full.height, full.width / full.height, r.width / r.height, true);
            w.ShowHeroAsync(bike, rider, key, false);
            _worldScreenDirty = false;
            return true;
        }
    }
}
