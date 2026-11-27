// B.R.A.A.P. (the hub's B.R.A.A.P. chip; after Open Unleashed's MxaMainMenu.Braap): the player's race AI (Content/ReflexBraap.cs).
//   ROSTERS  DEFAULT (the game's own riders), the season rosters (the real fields of a season's series) and the player's own; NEW ROSTER,
//            NEW EMPTY ROSTER, PASTE SHARE CODE; ROSTER FILES (the .braap files of the roster folder: SELECT imports one), OPEN ROSTER FOLDER
//   RIDERS   the chosen roster: USE FOR RACES, NAME, THE FIELD (a random draw / from the top), DUPLICATE, COPY SHARE CODE, EXPORT FILE, DELETE;
//            ADD RIDER, SORT BY NUMBER, ALL IN, ALL OUT and a row per rider (OUT when he does not line up, SAME # for a shared number)
//   RIDER    the chosen rider: NAME, JERSEY NAME, NUMBER, IN THE FIELD, TEAM, his LOOK (AUTO, one of the game's riders' gear sets, a RIDER
//            PRESET of the garage, a gear pack's or an Alive gear line) and every piece of it, his BIKE GRAPHICS (a kit pack's kit), RANDOM
//            LOOK, DUPLICATE, DELETE; the side panel pictures him. The triggers step through the riders.
// A built-in roster is shown read-only (EDIT A COPY). The race setup has AI ROSTER (OpenShell.Setup.cs) to pick the roster in use there too.
using System;
using System.Collections.Generic;
using System.Linq;
using Reflex.Port.Content;
using Reflex.Port.Options;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.OpenFrontEnd
{
    public sealed partial class OpenShell
    {
        static readonly string[] BrTabs = { "ROSTERS", "RIDERS", "RIDER" };
        static readonly Color BrAccent = new Color(0.886f, 0.675f, 0.251f);

        int _brTab, _brRider;
        string _brRosterId, _brArmed;
        VisualElement _brRows, _brSide, _brPic;
        Label _brStatus, _brSub, _brHelpTitle, _brHelpText, _brActive;
        IVisualElementScheduledItem _brPreviewJob;
        readonly Dictionary<int, List<GarageItem>> _brItems = new Dictionary<int, List<GarageItem>>();
        List<(string label, List<BraapPick> picks)> _brLooks;
        List<(string label, List<string> tokens)> _brKits;

        BraapRoster BrRoster => ReflexBraap.Find(_brRosterId) ?? ReflexBraap.Default;
        BraapRider BrRider { get { var r = BrRoster; return _brRider >= 0 && _brRider < r.riders.Count ? r.riders[_brRider] : null; } }

        /// Leaving the screen: the portrait pictures the player's own rider again.
        void ReleaseBraap()
        {
            _brPreviewJob?.Pause(); _brPreviewJob = null;
            _brRows = _brSide = _brPic = null;
            _brItems.Clear(); _brLooks = null; _brKits = null;
            if (_portrait != null && _portrait.Override != null) { _portrait.Override = null; }
        }

        public void ShowBraap() => ShowBraap(0);

        public void ShowBraap(int tab, string focus = null, string status = null)
        {
            Clear(Screen.Braap);
            SetBackground(null);
            var uss = Resources.Load<StyleSheet>(OuUi.ResourceRoot + "OuGarage");
            if (uss && !Root.styleSheets.Contains(uss)) Root.styleSheets.Add(uss);
            OuTheme.Header(_layer, "B.R.A.A.P.", "BUILD RIDERS · AI · APPEARANCE · PLATES  —  YOUR OWN RACE FIELD");
            _brRosterId ??= ReflexBraap.ActiveId;
            if (ReflexBraap.Find(_brRosterId) == null) _brRosterId = ReflexBraap.DefaultId;
            _brTab = Mathf.Clamp(tab, 0, BrTabs.Length - 1);
            _brRider = Mathf.Clamp(_brRider, 0, Math.Max(0, BrRoster.riders.Count - 1));
            if (_brTab == 2 && BrRider == null) _brTab = 1;

            var page = OuUi.Div("ou-garage ou-physics", _layer);
            var panel = OuUi.Div("ou-panel ou-garage-panel", page);
            var tabs = OuUi.Div("ou-gtabs", panel);
            for (int i = 0; i < BrTabs.Length; i++)
            {
                int t = i;
                var b = OuUi.Button(BrTabs[i], "ou-gtab" + (i == _brTab ? " ou-gtab-on" : ""), () => ShowBraap(t), tabs);
                b.name = "br-tab-" + i; OuUi.Div("ou-gtab-bar", b);
                if (i == _brTab) b.AddToClassList("ou-selected");
            }
            _brSub = OuUi.Text("", "mxa-note", panel);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Auto };
            scroll.AddToClassList("ou-garage-rows"); scroll.AddToClassList("mxa-option-rows");
            panel.Add(scroll);
            _brRows = scroll.contentContainer;
            _brStatus = OuUi.Text(status ?? "", "mxa-note ou-garage-status", panel);
            var actions = OuUi.Div("ou-garage-actions", panel);
            var use = OuUi.Button("USE FOR RACES", "mxa-start ou-cta ou-garage-ride", BrUse, actions); use.name = "br-use";
            var done = OuUi.Button("DONE", "mxa-start ou-cta", BrBack, actions); done.name = "br-done";
            OuTheme.Backdrop(done);
            OuWidgets.LinkRow(new[] { use, done });

            _brSide = OuUi.Div("ou-panel ou-ps-side", page);
            _brSide.style.flexGrow = 1; _brSide.style.marginLeft = 28; _brSide.style.paddingLeft = 28; _brSide.style.paddingRight = 28; _brSide.style.paddingTop = 22;
            _brActive = OuUi.Text("", "ou-eyebrow", _brSide);
            _brPic = null;
            if (_brTab == 2)
            {
                var frame = PortraitFrame(_brSide, "br-portrait");
                frame.style.width = 240; frame.style.height = 300; frame.style.marginTop = 12; frame.style.marginBottom = 8; frame.style.flexShrink = 0;
                _brPic = frame;
            }
            var help = OuUi.Div("ou-ps-help", _brSide);
            _brHelpTitle = OuUi.Text("", "mxa-track-title", help); _brHelpTitle.style.whiteSpace = WhiteSpace.Normal;
            _brHelpText = OuUi.Text("", "mxa-note", help); _brHelpText.style.whiteSpace = WhiteSpace.Normal;
            var note = OuUi.Text("The roster in use names and dresses the computer riders of your offline races: their names on the results and the jerseys, " +
                                 "their numbers on the plates, their gear and bike graphics. The riding is the game's own AI on the game's own bikes. " +
                                 "Not in the career (its standings keep the game's riders) and not online. Share a roster as a code (paste it in a message) " +
                                 "or as a .braap file; gear a friend does not have is drawn as the game's.", "mxa-note", _brSide);
            note.style.whiteSpace = WhiteSpace.Normal; note.style.marginTop = 18;

            _nav.OnTab = d => ShowBraap((_brTab + d + BrTabs.Length) % BrTabs.Length);
            _nav.OnCancel = () => { BrBack(); return true; };
            if (_brTab == 2) _nav.OnPage = d => BrStepRider(d);
            _brItems.Clear();
            if (_brTab == 0) BrRostersPage();
            else if (_brTab == 1) BrRidersPage();
            else BrRiderPage();
            BrSubLine();
            if (_brTab == 2) Hints((OuUi.KeySubmit, "SELECT"), ("←→", "CHANGE"), (OuUi.KeyTabs, "PAGE"), (OuUi.KeyCancel, "BACK"));
            else Hints((OuUi.KeySubmit, "SELECT"), (OuUi.KeyTabs, "PAGE"), (OuUi.KeyCancel, "BACK"));
            var first = (focus != null ? _brRows.Q(focus) : null) ?? OuUi.FirstItem(_brRows);
            _nav.DefaultItem = () => OuUi.FirstItem(_brRows);
            if (first != null)
            {
                _nav.Focus(first);
                first.schedule.Execute(() => first.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(first)).ExecuteLater(50);   // (a long roster: the row in view)
            }
        }

        void BrStatus(string s) { if (_brStatus != null) _brStatus.text = (s ?? "").ToUpperInvariant(); }
        void BrHelp(string title, string text) { if (_brHelpTitle != null) { _brHelpTitle.text = title; _brHelpText.text = text; } }

        void BrSubLine()
        {
            var r = BrRoster; var act = ReflexBraap.Active;
            if (_brSub != null) _brSub.text = r.name.ToUpperInvariant() + "  ·  " + BrRosterLine(r) + (r.BuiltIn ? "  ·  BUILT-IN" : "");
            if (_brActive != null) _brActive.text = "YOUR RACES USE  " + (ReflexBraap.UsingDefault ? "THE GAME'S OWN RIDERS" : act.name.ToUpperInvariant());
        }

        static string BrRosterLine(BraapRoster r)
        {
            string s = $"{r.riders.Count} RIDERS  ·  {r.EnabledCount} IN THE FIELD";
            return string.IsNullOrEmpty(r.author) ? s : s + "  ·  " + (r.BuiltIn ? "" : "BY ") + r.author.ToUpperInvariant();
        }

        VisualElement BrButton(string label, string name, Action submit, string help = null)
        {
            var b = OuUi.Button(label, "mxa-secondary mxa-library-action", submit, _brRows);
            b.name = name; b.style.marginTop = 4;
            if (help != null) b.RegisterCallback<FocusInEvent>(_ => BrHelp(label, help));
            return b;
        }

        /// A row that opens something: the label, a state on the right (gold when marked).
        VisualElement BrRow(string label, string state, string name, Action submit, bool marked = false)
        {
            var row = OuUi.Spinner(label, () => state, _ => { }, _brRows, submit);
            row.name = name;
            ((OuNavItem)row.userData).adjust = null;
            foreach (var a in row.Query<Label>(className: "mxa-arrow").ToList()) a.style.display = DisplayStyle.None;
            if (marked)
            {
                var v = row.Q<Label>(className: "mxa-value"); v.style.color = BrAccent;
                row.RegisterCallback<FocusInEvent>(_ => v.style.color = new StyleColor(StyleKeyword.Null));
                row.RegisterCallback<FocusOutEvent>(_ => v.style.color = BrAccent);
            }
            return row;
        }

        void BrTextRow(string label, string name, Func<string> get, Action<string> set, int max, Func<string, string> clean, string help)
        {
            var row = OuUi.Div("mxa-row", _brRows); row.name = name;
            OuUi.Text(label, "mxa-row-label", row);
            var field = new TextField { value = get() ?? "", maxLength = max };
            field.AddToClassList("mxa-path"); field.style.flexGrow = 1;
            row.Add(field);
            void Commit(string text)
            {
                string v = clean(text);
                if (v != (get() ?? "")) set(v);
                field.SetValueWithoutNotify(get() ?? "");
            }
            field.RegisterCallback<FocusOutEvent>(_ => Commit(field.value));
            field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) { row.Focus(); e.StopPropagation(); } }, TrickleDown.TrickleDown);
            field.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            OuUi.Item(row, () =>
            {
                if (OuMenuInput.GamepadLast) OpenKeyboard(label, "UP TO " + max + " LETTERS, DIGITS AND SPACES", get() ?? "", max, false, true, row, Commit);
                else field.Q("unity-text-input")?.Focus();
            });
            row.RegisterCallback<FocusInEvent>(_ => BrHelp(label, help));
        }

        void BrUse()
        {
            var r = BrRoster;
            if (!r.IsDefault && r.EnabledCount == 0) { BrStatus("Nobody in this roster lines up: put a rider in the field first"); return; }
            ReflexBraap.ActiveId = r.id;
            BrSubLine();
            if (_brTab == 0) ShowBraap(0, "br-roster-" + r.id, r.IsDefault ? "Races use the game's own riders" : "Races use " + r.name + ": from the next event");
            else BrStatus(r.IsDefault ? "Races use the game's own riders" : "Races use " + r.name + ": from the next event");
        }

        void BrBack()
        {
            if (_brTab == 2) ShowBraap(1, "br-rider-" + _brRider);
            else if (_brTab == 1) ShowBraap(0, "br-roster-" + _brRosterId);
            else ShowMain();
        }

        static string BrAuthor => ReflexProfile.HasPersona ? ReflexProfile.Persona : "";

        /// A copy of a roster as the player's own, shown on RIDERS.
        void BrCopy(BraapRoster from, string name, string status)
        {
            var d = ReflexBraap.Create(name, from, BrAuthor);
            if (d == null) { BrStatus("Too many rosters (" + ReflexBraap.MaxRosters + ")"); return; }
            _brRosterId = d.id;
            ShowBraap(1, null, status ?? "Created " + d.name);
        }

        // ------------------------------------------------------------------------------------------- ROSTERS

        void BrRostersPage()
        {
            string active = ReflexBraap.ActiveId;
            void Card(BraapRoster r)
            {
                var ro = r; bool inUse = ro.id == active;
                var row = BrRow(ro.name.ToUpperInvariant(), inUse ? "IN USE" : ro.riders.Count + " RIDERS", "br-roster-" + ro.id, () => { _brRosterId = ro.id; ShowBraap(1); }, inUse);
                row.RegisterCallback<FocusInEvent>(_ =>
                {
                    _brRosterId = ro.id; BrSubLine();
                    var sample = ro.riders.Where(x => x.enabled).Take(10).Select(x => "#" + x.number + " " + x.name.ToUpperInvariant());
                    BrHelp(ro.name.ToUpperInvariant(), BrRosterLine(ro) + (string.IsNullOrEmpty(ro.note) ? "" : "\n" + ro.note) + "\n" + string.Join("  ·  ", sample) + (ro.EnabledCount > 10 ? "  ·  …" : "") +
                                                      "\nSELECT shows its riders. USE FOR RACES puts it in every offline race.");
                });
            }
            OuTheme.Section(_brRows, "THE GAME'S");
            Card(ReflexBraap.Default);
            foreach (int year in ReflexBraap.SeasonYears)
            {
                OuTheme.Section(_brRows, year + " SEASON");
                foreach (var r in ReflexBraap.Seasons.Where(x => ReflexBraap.SeasonOf(x) == year)) Card(r);
            }
            OuTheme.Section(_brRows, "YOUR ROSTERS");
            foreach (var r in ReflexBraap.Rosters) Card(r);
            if (ReflexBraap.Rosters.Count == 0) OuUi.Text("None yet. Make one below, or open a built-in roster and EDIT A COPY.", "mxa-note", _brRows);

            OuTheme.Section(_brRows, "NEW / IMPORT");
            BrButton("NEW ROSTER (A COPY OF THE ONE ABOVE)", "br-new", () => BrCopy(BrRoster, BrRoster.BuiltIn ? "My " + BrShort(BrRoster.name) : BrRoster.name + " copy", null),
                     "A roster of your own with the riders of the roster you last pointed at: change any of them.");
            BrButton("NEW EMPTY ROSTER", "br-new-empty", () =>
            {
                var r = ReflexBraap.Create("My riders", null, BrAuthor);
                if (r == null) { BrStatus("Too many rosters (" + ReflexBraap.MaxRosters + ")"); return; }
                _brRosterId = r.id; BrAddRider();
            }, "Starts from nothing: add your own riders one by one.");
            BrButton("PASTE SHARE CODE", "br-import", () =>
            {
                var r = ReflexBraap.Import(GUIUtility.systemCopyBuffer, out string err);
                if (r == null) { BrStatus(err ?? "Nothing to import"); return; }
                _brRosterId = r.id;
                ShowBraap(0, "br-roster-" + r.id, "Imported " + r.name + " (" + r.riders.Count + " riders)" + BrMissing(r));
            }, "Reads a roster's share code (" + ReflexBraap.ShareTag + "...) from the clipboard, wherever it is in the copied text. Open Unleashed's codes read too: their names, numbers and who lines up.");

            // ROSTER FILES: the .braap files of the roster folder (a friend's, from Discord); SELECT imports one
            OuTheme.Section(_brRows, "ROSTER FILES");
            var files = ReflexBraap.Files();
            for (int i = 0; i < files.Count && i < 40; i++)
            {
                string path = files[i], file = System.IO.Path.GetFileName(path);
                var fr = ReflexBraap.ReadFile(path, out string ferr);
                var row = BrRow(fr != null ? fr.name.ToUpperInvariant() : file.ToUpperInvariant(), fr != null ? "IMPORT" : "UNREADABLE", "br-file-" + i, () =>
                {
                    var r = ReflexBraap.ImportFile(path, out string err);
                    if (r == null) { BrStatus("Can't import " + file + ": " + err); return; }
                    _brRosterId = r.id;
                    ShowBraap(0, "br-roster-" + r.id, "Imported " + r.name + " (" + r.riders.Count + " riders) from " + file + BrMissing(r));
                });
                string when = System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                row.RegisterCallback<FocusInEvent>(_ => BrHelp(file, fr != null ? $"{fr.riders.Count} riders  ·  {when}\nSELECT imports it as a new roster of yours." : "Not a roster: " + ferr));
            }
            if (files.Count == 0) OuUi.Text("No roster files yet. EXPORT FILE (on RIDERS) saves a roster as a .braap file to send to friends; put theirs in the roster folder to import them here.", "mxa-note", _brRows);
            BrButton("OPEN ROSTER FOLDER", "br-folder", () =>
            {
                if (!ReflexBraap.FilesWritable) { BrStatus("Test runs don't open folders"); return; }
                Reveal(ReflexBraap.Folder);
            }, "The .braap files live in " + ReflexSave.Redact(ReflexBraap.Folder) + ".");
        }

        static string BrShort(string name, int max = 20) => ReflexBraap.CleanName(name.Length > max ? name.Substring(0, max) : name);
        static string BrMissing(BraapRoster r) { int n = ReflexBraap.MissingContent(r); return n == 0 ? "" : ": " + n + " rider(s) wear gear this game does not have (drawn as the game's)"; }

        // ------------------------------------------------------------------------------------------- RIDERS

        void BrRidersPage()
        {
            var r = BrRoster; bool editable = !r.BuiltIn;
            OuTheme.Section(_brRows, "THIS ROSTER");
            if (editable)
            {
                BrTextRow("NAME", "br-rename", () => r.name, v => { if (v.Length > 0) { r.name = v; ReflexBraap.Save(r); BrSubLine(); } }, ReflexBraap.MaxNameLength, ReflexBraap.CleanName, "What the roster is called.");
                var draw = OuUi.Toggle("THE FIELD", () => r.draw == 1, v => { r.draw = v ? 1 : 0; ReflexBraap.Save(r); }, _brRows, "FROM THE TOP", "A RANDOM DRAW");
                draw.name = "br-draw";
                draw.RegisterCallback<FocusInEvent>(_ => BrHelp("THE FIELD", "A RANDOM DRAW: every event draws its computer riders from the riders in the field. FROM THE TOP: the first riders of the list, in order (a field of 11 is the roster's first 11). With more computer riders than riders, the draw starts over."));
            }
            else
                BrButton("EDIT A COPY", "br-copy", () => BrCopy(r, "My " + BrShort(r.name), "Your copy of " + r.name + ": change any rider"), "A built-in roster cannot be changed: this makes a roster of yours with its riders.");
            if (editable) BrButton("DUPLICATE", "br-duplicate", () => BrCopy(r, r.name + " copy", null), "Another roster of yours with these riders.");
            BrButton("COPY SHARE CODE", "br-share", () => { GUIUtility.systemCopyBuffer = ReflexBraap.ShareCode(r); BrStatus("Share code copied: paste it in chat, a forum, a Discord message ..."); },
                     "One line of text (" + ReflexBraap.ShareTag + "...) that carries every rider: the other player uses PASTE SHARE CODE.");
            BrButton("EXPORT FILE", "br-export", () =>
            {
                string path = ReflexBraap.ExportFile(r, out string err);
                if (path == null) { BrStatus("Can't export: " + err); return; }
                BrStatus("Saved " + System.IO.Path.GetFileName(path) + " in the roster folder: send it to a friend");
                if (!Application.isBatchMode) Reveal(ReflexBraap.Folder);
            }, "Writes the roster as a .braap file into the roster folder and opens it.");
            if (editable)
                BrButton(_brArmed == "roster:" + r.id ? "PRESS AGAIN TO DELETE" : "DELETE ROSTER", "br-delete", () =>
                {
                    if (_brArmed != "roster:" + r.id) { _brArmed = "roster:" + r.id; ShowBraap(1, "br-delete", "Press delete again to delete " + r.name); return; }
                    _brArmed = null; ReflexBraap.Delete(r); _brRosterId = ReflexBraap.DefaultId;
                    ShowBraap(0, null, "Deleted");
                }, "Removes the roster (races go back to the game's own riders when it was in use).");

            OuTheme.Section(_brRows, $"RIDERS  ·  {r.riders.Count}");
            if (editable)
            {
                BrButton("ADD RIDER", "br-add", BrAddRider, "A new rider with the roster's next free number.");
                BrButton("SORT BY NUMBER", "br-sort", () =>
                {
                    var cur = BrRider;
                    r.riders = r.riders.OrderBy(x => x.number).ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToList();
                    ReflexBraap.Save(r); _brRider = Math.Max(0, r.riders.IndexOf(cur));
                    ShowBraap(1, "br-sort", "Sorted by number");
                }, "Orders the list by number (FROM THE TOP then draws the lowest numbers first).");
                var all = OuUi.Spinner("EVERY RIDER'S SKILL", () => { var k = r.riders.Select(q => q.skill).Distinct().ToList(); return k.Count == 0 ? "-" : k.Count > 1 ? "MIXED" : ReflexBraap.SkillNames[Mathf.Clamp(k[0], 0, 4)]; }, d =>
                {
                    var k = r.riders.Select(q => q.skill).Distinct().ToList();
                    int now = k.Count == 1 ? k[0] : d > 0 ? -1 : 0, next = ((now + d) % 5 + 5) % 5;
                    foreach (var q in r.riders) q.skill = next;
                    ReflexBraap.Save(r);
                }, _brRows);
                all.name = "br-all-skill";
                all.RegisterCallback<FocusInEvent>(_ => BrHelp("EVERY RIDER'S SKILL", "Sets every rider of the roster at once. ALL TIME: the whole field rides at the game's top AI skill, whatever the event's AI OPPONENTS says. EVENT'S: the race setup decides. Each rider's own SKILL row changes one."));
                BrButton("ALL IN", "br-all-in", () => { foreach (var x in r.riders) x.enabled = true; ReflexBraap.Save(r); ShowBraap(1, "br-all-in", "Everybody lines up"); });
                BrButton("ALL OUT", "br-all-out", () => { foreach (var x in r.riders) x.enabled = false; ReflexBraap.Save(r); ShowBraap(1, "br-all-out", "Nobody lines up: put riders in the field"); });
            }
            var dup = ReflexBraap.Duplicates(r);
            for (int i = 0; i < r.riders.Count; i++)
            {
                int idx = i; var x = r.riders[i];
                string state = !x.enabled ? "OUT" : dup.Contains(x) ? "SAME #" : !string.IsNullOrEmpty(x.team) ? x.team.ToUpperInvariant() : BrLookLabel(x);
                var row = BrRow("#" + x.number + "   " + x.name.ToUpperInvariant(), state, "br-rider-" + i, () => { _brRider = idx; ShowBraap(2); }, dup.Contains(x) && x.enabled);
                if (!x.enabled) row.style.opacity = 0.5f;
                row.RegisterCallback<FocusInEvent>(_ => { _brRider = idx; BrHelp("#" + x.number + "  " + x.name.ToUpperInvariant(), BrRiderText(x) + "\nSELECT " + (editable ? "edits him." : "shows him.")); });
            }
            if (r.riders.Count == 0) OuUi.Text(editable ? "No riders yet: ADD RIDER." : "No riders.", "mxa-note", _brRows);
        }

        static string BrLookLabel(BraapRider x) => x.look.Count == 0 ? "AUTO" : string.IsNullOrEmpty(x.lookName) ? "CUSTOM" : x.lookName.ToUpperInvariant();
        static string BrKitLabel(BraapRider x) => x.kits.Count == 0 ? "AUTO" : string.IsNullOrEmpty(x.kitName) ? "CUSTOM" : x.kitName.ToUpperInvariant();
        static string BrRiderText(BraapRider x) =>
            "JERSEY  " + ReflexBraap.PlateName(x) + (string.IsNullOrEmpty(x.team) ? "" : "  ·  " + x.team.ToUpperInvariant()) + "\nSKILL  " + ReflexBraap.SkillNames[Mathf.Clamp(x.skill, 0, 4)] + "  ·  GEAR  " + BrLookLabel(x) + "  ·  BIKE GRAPHICS  " + BrKitLabel(x) + (x.enabled ? "" : "\nOUT OF THE FIELD");

        void BrAddRider()
        {
            var r = BrRoster;
            if (r.BuiltIn) return;
            if (r.riders.Count >= ReflexBraap.MaxRiders) { BrStatus("A roster holds up to " + ReflexBraap.MaxRiders + " riders"); return; }
            int n = ReflexBraap.FreeNumber(r);
            r.riders.Add(new BraapRider { name = "Rider " + n, number = n });
            ReflexBraap.Save(r);
            _brRider = r.riders.Count - 1;
            ShowBraap(2, "br-name", "New rider #" + n + ": name him, pick his gear");
        }

        void BrStepRider(int d)
        {
            int n = BrRoster.riders.Count; if (n == 0) return;
            string at = _nav.Focused?.name;
            _brRider = ((_brRider + d) % n + n) % n;
            ShowBraap(2, at);
        }

        // ------------------------------------------------------------------------------------------- RIDER

        List<GarageItem> BrItemsOf(int type)
        {
            if (_brItems.TryGetValue(type, out var l)) return l;
            try { l = OpenGarageData.GearItems(type); } catch (Exception e) { Debug.LogWarning("[Braap] gear type " + type + ": " + e.Message); l = new List<GarageItem>(); }
            return _brItems[type] = l;
        }

        static string BrItemLabel(GarageItem it) => ((string.IsNullOrEmpty(it.line) || it.title.StartsWith(it.line, StringComparison.OrdinalIgnoreCase) ? "" : it.line + " ") + it.title).ToUpperInvariant();

        /// The looks GEAR offers: AUTO, the game's riders' gear sets, the garage's rider presets, the gear packs' and Alive gear's lines.
        List<(string label, List<BraapPick> picks)> BrLooks()
        {
            if (_brLooks != null) return _brLooks;
            var l = new List<(string, List<BraapPick>)> { ("AUTO", new List<BraapPick>()) };
            try
            {
                foreach (var p in GaragePresets.Riders)
                    l.Add(("PRESET " + p.name, p.gear.Select(g => string.IsNullOrEmpty(g.token) ? ReflexBraap.PickOf(g.id) : new BraapPick { type = g.type, token = g.token }).Where(x => x != null).ToList()));
                foreach (var pack in KitPacks.All.Where(p => p.HasGear))
                    foreach (var line in pack.items.Where(i => !i.bike && !i.hidden).GroupBy(i => string.IsNullOrEmpty(i.lineTitle) ? pack.name : i.lineTitle))
                        l.Add((line.Key, line.GroupBy(i => i.type).Select(g => new BraapPick { type = g.Key, token = g.First().Token }).ToList()));
                foreach (var line in RxGear.All.GroupBy(p => string.IsNullOrEmpty(p.line) ? p.mod.name : p.line))
                    l.Add((line.Key, line.GroupBy(p => p.type).Select(g => new BraapPick { type = g.Key, token = g.First().Token }).ToList()));
                foreach (var s in ReflexBraap.StockSets().OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase))
                    l.Add((s.name, ReflexBraap.PicksOf(ReflexBraap.SetGear(s.gearSet))));
            }
            catch (Exception e) { Debug.LogWarning("[Braap] looks: " + e.Message); }
            l.RemoveAll(x => x.Item1 != "AUTO" && x.Item2.Count == 0);
            return _brLooks = l;
        }

        /// The bike graphics BIKE GRAPHICS offers: AUTO and every kit of the kit packs (a kit's pieces for every bike it fits).
        List<(string label, List<string> tokens)> BrKits()
        {
            if (_brKits != null) return _brKits;
            var l = new List<(string, List<string>)> { ("AUTO", new List<string>()) };
            try
            {
                foreach (var pack in KitPacks.All.Where(p => p.HasKits))
                    foreach (var kit in pack.items.Where(i => i.bike && !i.hidden).GroupBy(i => string.IsNullOrEmpty(i.title) ? pack.name : i.title))
                        l.Add((kit.Key, kit.Select(i => i.Token).Distinct().Take(ReflexBraap.MaxKits).ToList()));
            }
            catch (Exception e) { Debug.LogWarning("[Braap] kits: " + e.Message); }
            return _brKits = l;
        }

        static List<(int id, string name)> BrGearTypes()
        {
            int[] order = { 1, 2, 3, 4, 9, 11 };
            var l = new List<(int, string)>();
            try
            {
                if (!ReflexEvents.Tables.TryGetValue("RiderGearType", out var rows)) return l;
                foreach (var r in rows)
                {
                    int id = Rfx.Int(r, "RiderGearTypeId", -1); string factory = Rfx.Str(r, "FactoryName") ?? "GEAR";
                    if (id < 0) continue;
                    l.Add((id, OpenContent.Str("STR_RIDERGEARTYPE_" + factory.ToUpperInvariant(), factory).ToUpperInvariant()));
                }
            }
            catch (Exception) { }
            return l.OrderBy(x => { int k = Array.IndexOf(order, x.Item1); return k < 0 ? 100 + x.Item1 : k; }).ToList();
        }

        void BrRiderPage()
        {
            var r = BrRoster; var x = BrRider; bool editable = !r.BuiltIn;
            OuTheme.Section(_brRows, $"RIDER {_brRider + 1} OF {r.riders.Count}");
            if (!editable)
            {
                BrRow("NAME", x.name.ToUpperInvariant(), "br-name", () => { });
                BrRow("JERSEY NAME", ReflexBraap.PlateName(x), "br-plate", () => { });
                BrRow("NUMBER", "#" + x.number, "br-number", () => { });
                BrRow("SKILL", ReflexBraap.SkillNames[Mathf.Clamp(x.skill, 0, 4)], "br-skill", () => { });
                if (!string.IsNullOrEmpty(x.team)) BrRow("TEAM", x.team.ToUpperInvariant(), "br-team", () => { });
                BrRow("GEAR", BrLookLabel(x), "br-look", () => { });
                BrRow("BIKE GRAPHICS", BrKitLabel(x), "br-kit", () => { });
                BrButton("EDIT A COPY", "br-copy", () => BrCopy(r, "My " + BrShort(r.name), "Your copy of " + r.name + ": change any rider"), "A built-in roster cannot be changed: this makes a roster of yours with its riders.");
                BrHelp("#" + x.number + "  " + x.name.ToUpperInvariant(), BrRiderText(x));
                BrSchedulePreview();
                return;
            }

            void Changed() { ReflexBraap.Save(r); BrSchedulePreview(); }
            void Rebuild(string focus) => ShowBraap(2, focus);
            BrTextRow("NAME", "br-name", () => x.name, v => { if (v.Length > 0) { x.name = v; Changed(); } }, ReflexBraap.MaxNameLength, ReflexBraap.CleanName,
                      "The rider's name on the results and the standings. AUTO gear follows the name: the same name always wears the same set.");
            BrTextRow("JERSEY NAME", "br-plate", () => x.plate, v => { x.plate = v; Changed(); }, ReflexBraap.MaxPlateLength, ReflexBraap.CleanPlate,
                      "The name on the jersey (up to " + ReflexBraap.MaxPlateLength + " capitals). Empty: his family name (" + ReflexBraap.PlateName(new BraapRider { name = x.name }) + ").");
            var num = OuUi.Spinner("NUMBER", () => "#" + x.number, d => { x.number = ReflexBraap.NextFreeNumber(r, x, x.number, BrNumberStep(d)); Changed(); }, _brRows, () =>
            {
                OpenKeyboard("NUMBER", "0 TO 999", x.number.ToString(), 3, true, false, _brRows.Q("br-number"), text => { if (int.TryParse(text, out int n)) { x.number = Mathf.Clamp(n, 0, 999); Changed(); Rebuild("br-number"); } });
            });
            num.name = "br-number";
            num.RegisterCallback<FocusInEvent>(_ => BrHelp("NUMBER", "On the bike's plates and the jersey. Left / right skip the numbers the roster's other riders have (hold to go faster); SELECT types one."));
            var inField = OuUi.Toggle("IN THE FIELD", () => x.enabled, v => { x.enabled = v; Changed(); }, _brRows, "YES", "NO");
            inField.name = "br-enabled";
            inField.RegisterCallback<FocusInEvent>(_ => BrHelp("IN THE FIELD", "NO keeps the rider in the roster but never on the gate."));
            var skill = OuUi.Spinner("SKILL", () => ReflexBraap.SkillNames[Mathf.Clamp(x.skill, 0, 4)], d => { x.skill = ((x.skill + d) % 5 + 5) % 5; Changed(); }, _brRows);
            skill.name = "br-skill";
            skill.RegisterCallback<FocusInEvent>(_ => BrHelp("SKILL", "How well he rides. EVENT'S: whatever the race setup's AI OPPONENTS gives him (the game spreads a field over two skills). ROOKIE, AMATEUR, PRO, ALL TIME: always that, whatever the event is set to. These are the game's own four AI skills."));
            BrTextRow("TEAM", "br-team", () => x.team, v => { x.team = v; Changed(); }, ReflexBraap.MaxNameLength, ReflexBraap.CleanName, "A note for the roster's list: his team or his bike's make.");

            OuTheme.Section(_brRows, "GEAR");
            var looks = BrLooks();
            var look = OuUi.Spinner("LOOK", () => BrLookLabel(x), d =>
            {
                int i = x.look.Count == 0 ? 0 : Math.Max(0, looks.FindIndex(q => string.Equals(q.label, x.lookName, StringComparison.OrdinalIgnoreCase)));
                var pick = looks[((i + d) % looks.Count + looks.Count) % looks.Count];
                x.look = pick.picks.Select(p => p.Clone()).ToList(); x.lookName = pick.picks.Count == 0 ? "" : ReflexBraap.CleanName(pick.label);
                Changed(); Rebuild("br-look");
            }, _brRows);
            look.name = "br-look";
            look.RegisterCallback<FocusInEvent>(_ => BrHelp("LOOK", "A whole outfit at once. AUTO: the gear set of one of the game's riders, picked by his name. Then: your garage's RIDER PRESETS, the lines of your gear packs and Alive gear, and every game rider's own set. The rows below change single pieces."));
            foreach (var (type, tname) in BrGearTypes())
            {
                int ty = type; var items = BrItemsOf(ty);
                if (items.Count == 0) continue;
                string Label()
                {
                    var p = x.look.FirstOrDefault(q => q.type == ty); if (p == null) return "AUTO";
                    int id = ReflexBraap.Resolve(p); if (id < 0) return "NOT INSTALLED";
                    var it = items.FirstOrDefault(q => q.id == id); return it != null ? BrItemLabel(it) : "#" + id;
                }
                var row = OuUi.Spinner(tname, Label, d =>
                {
                    var p = x.look.FirstOrDefault(q => q.type == ty);
                    int cur = p == null ? -1 : items.FindIndex(q => q.id == ReflexBraap.Resolve(p));
                    int n = items.Count + 1, next = ((cur + 1 + d) % n + n) % n - 1;   // (-1: AUTO)
                    x.look.RemoveAll(q => q.type == ty);
                    if (next >= 0)
                    {
                        var np = ReflexBraap.PickOf(items[next].id);
                        if (np != null)
                        {
                            x.look.Add(np);
                            // a real chest protector takes the neck brace off and the other way round, as the garage does (CoreGarageProfile.EquipGear)
                            if (ty == 9 || ty == 11)
                            {
                                int other = ty == 9 ? 11 : 9, none = ty == 9 ? 322 : 13;
                                x.look.RemoveAll(q => q.type == other);
                                if (ReflexBraap.GearTypeOf(none) == other) x.look.Add(new BraapPick { type = other, id = none });
                            }
                        }
                    }
                    x.lookName = x.look.Count == 0 ? "" : "Custom";
                    Changed();
                    var lv = _brRows.Q("br-look")?.Q<Label>(className: "mxa-value"); if (lv != null) lv.text = BrLookLabel(x);
                }, _brRows);
                row.name = "br-gear-" + ty;
                row.RegisterCallback<FocusInEvent>(_ => BrHelp(tname, items.Count + " pieces: the game's, your gear packs' and your Alive gear. AUTO leaves the piece of his AUTO set."));
            }

            OuTheme.Section(_brRows, "BIKE");
            var kits = BrKits();
            var kitRow = OuUi.Spinner("BIKE GRAPHICS", () => BrKitLabel(x), d =>
            {
                int i = x.kits.Count == 0 ? 0 : Math.Max(0, kits.FindIndex(q => string.Equals(ReflexBraap.CleanName(q.label), x.kitName, StringComparison.OrdinalIgnoreCase)));
                var pick = kits[((i + d) % kits.Count + kits.Count) % kits.Count];
                x.kits = new List<string>(pick.tokens); x.kitName = pick.tokens.Count == 0 ? "" : ReflexBraap.CleanName(pick.label);
                Changed();
            }, _brRows);
            kitRow.name = "br-kit";
            kitRow.RegisterCallback<FocusInEvent>(_ => BrHelp("BIKE GRAPHICS", kits.Count > 1
                ? "A kit of your kit packs. The game picks each computer rider's bike for the event's class; the kit is drawn when it fits that bike, else the bike stays as the game dressed it."
                : "No kit packs installed (Mods/Gear): the bikes stay as the game dressed them. The game picks each computer rider's bike for the event's class."));

            OuTheme.Section(_brRows, "THIS RIDER");
            BrButton("RANDOM LOOK", "br-random", () =>
            {
                var sets = looks.Where(q => q.picks.Count > 0).ToList(); if (sets.Count == 0) return;
                var pick = sets[new System.Random(Environment.TickCount).Next(sets.Count)];
                x.look = pick.picks.Select(p => p.Clone()).ToList(); x.lookName = ReflexBraap.CleanName(pick.label);
                Changed(); Rebuild("br-random");
            }, "One of the looks, at random.");
            BrButton("DUPLICATE", "br-dup", () =>
            {
                if (r.riders.Count >= ReflexBraap.MaxRiders) { BrStatus("A roster holds up to " + ReflexBraap.MaxRiders + " riders"); return; }
                var d = x.Clone(); d.number = ReflexBraap.FreeNumber(r); d.name = ReflexBraap.CleanName(x.name + " II");
                r.riders.Insert(_brRider + 1, d); ReflexBraap.Save(r); _brRider++;
                ShowBraap(2, "br-name", "Duplicated as #" + d.number);
            }, "Another rider with this look and the roster's next free number.");
            BrButton(_brArmed == "rider:" + r.id + "/" + _brRider ? "PRESS AGAIN TO DELETE" : "DELETE RIDER", "br-delete-rider", () =>
            {
                string key = "rider:" + r.id + "/" + _brRider;
                if (_brArmed != key) { _brArmed = key; ShowBraap(2, "br-delete-rider", "Press delete again to delete " + x.name); return; }
                _brArmed = null; r.riders.RemoveAt(_brRider); ReflexBraap.Save(r);
                _brRider = Math.Min(_brRider, r.riders.Count - 1);
                ShowBraap(r.riders.Count > 0 ? 1 : 1, r.riders.Count > 0 ? "br-rider-" + _brRider : "br-add", "Deleted");
            });
            BrSchedulePreview();
        }

        int _brNumStreak; float _brNumLast;
        int BrNumberStep(int d)
        {
            float now = Time.unscaledTime;
            _brNumStreak = now - _brNumLast < 0.2f ? _brNumStreak + 1 : 0;
            _brNumLast = now;
            return d * (_brNumStreak >= 20 ? 10 : _brNumStreak >= 8 ? 5 : 1);
        }

        /// The rider's picture, a moment after the last change (the spinners step fast).
        void BrSchedulePreview()
        {
            _brPreviewJob?.Pause();
            if (_brPic == null) return;
            _brPreviewJob = _brPic.schedule.Execute(BrShowPreview);
            _brPreviewJob.ExecuteLater(220);
        }

        void BrShowPreview()
        {
            var x = BrRider;
            if (x == null || _brPic == null || _brPic.panel == null || Current != Screen.Braap) return;
            if (Environment.GetEnvironmentVariable("REFLEX_PORTRAIT") == "0") return;
            try
            {
                int[] gear = ReflexBraap.GearIds(x, null);
                var spec = gear.Length > 0 ? OpenGarageData.RiderWithGear(gear) : null;
                if (spec == null) return;
                Portrait.Override = (spec, "braap:" + string.Join(",", gear));
                PortraitRefresh();
            }
            catch (Exception e) { Debug.LogWarning("[Braap] preview " + x.name + ": " + e.Message); }
        }
    }
}
