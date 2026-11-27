// GAME LIBRARY  ·  MX vs ATV UNLEASHED: the tracks of the player's own MX vs ATV Unleashed (the PC game, or an Open Unleashed install) imported
// as levels of the game library by the in-client converter (Content/UnleashedImport; plan: core/plan/UNLEASHED_IMPORT.md).
//   folder    the folder browser of the Untamed import (a gamepad works): the game folder, its Game Files folder or the data folder (the
//             one that holds Nat01.pak, SX01.pak ...); USE THIS FOLDER. The folder used last is kept in the client save
//   tracks    the folder is read on a worker thread (UnleashedImporter.Scan: which of the catalogue's 64 locations it has, which the
//             library has already) and listed by category: choose them, IMPORT n TRACKS. Ready ones are chosen; one installed already
//             is marked and not chosen: choosing it imports it again (replaces it). Freestyle arenas and machine challenge courses
//             are Free Ride maps (no lap race)
//   import    the chosen tracks converted, packed and installed one after another on a worker thread (UnleashedImporter.Import: the
//             current track and its step, a bar for the track and one for the whole import, CANCEL), then each track's result;
//             RxFiles.Reset() / ReflexEvents.Refresh() / OpenContent.Reset() so FREE RIDE and RACE list them at once. The Unleashed
//             folder and the Reflex game files are only read; the tracks are new files in the game files' Tracks folder.
//   leaving   the shell destroyed while a scan or an import runs (a ride starts, the game quits) cancels it (StopUnleashedImport); what an
//             import that did not end left in the game files is removed when the shell first starts (CleanUnleashedStaging) and by a scan
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Reflex.Port.Content;
using Reflex.Port.Content.UnleashedImport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.OpenFrontEnd
{
    public sealed partial class OpenShell
    {
        /// The folder used last is kept between sessions (the client save), as the Alive import keeps its sources.
        const string UnleashedFolderKey = "UnleashedImport.Folder";
        string _unleashedDir;
        UnleashedSources _unleashedFound;
        /// The tracks chosen for the import (their level names).
        readonly HashSet<string> _unleashedChosen = new HashSet<string>(StringComparer.Ordinal);
        Task _unleashedTask; CancellationTokenSource _unleashedCancel;
        string _unleashedStatus = "";
        UnleashedProgress _unleashedProgress;
        /// The progress screen: the step, the bar of the track in progress; importing, the whole import's line and bar.
        Label _unleashedStatusLabel, _unleashedAllLabel; VisualElement _unleashedFill, _unleashedAllFill;
        Exception _unleashedError; string _unleashedNotice;
        bool _unleashedImporting; UnleashedImportResult _unleashedResult;
        bool UnleashedBusy => _unleashedTask != null && !_unleashedTask.IsCompleted;
        public bool UnleashedScanRunning => UnleashedBusy && !_unleashedImporting;
        public bool UnleashedImportRunning => UnleashedBusy && _unleashedImporting;
        /// The last scan's tracks (null before / during a scan or an import; tests read them).
        public IReadOnlyList<UnleashedTrackSource> UnleashedTracks => UnleashedBusy ? null : _unleashedFound?.tracks;
        /// The last import's per-track results (null before / during one; tests read them).
        public IReadOnlyList<UnleashedTrackResult> UnleashedResults => UnleashedBusy ? null : _unleashedResult?.tracks;

        /// The library card (ShowLibrary): the Unleashed tracks installed, CHOOSE UNLEASHED FOLDER.
        void UnleashedLibraryCard(VisualElement list)
        {
            var card = OuUi.Div("mxa-library-card", list);
            card.style.flexWrap = Wrap.Wrap;
            var copy = OuUi.Div("mxa-library-copy", card);
            copy.style.flexGrow = 1; copy.style.flexShrink = 1; copy.style.minWidth = 240;
            OuUi.Text("OPTIONAL  ·  PC GAME FOLDER", "mxa-library-platform", copy);
            OuUi.Text("MX vs ATV UNLEASHED", "mxa-track-title", copy);
            string problem = UnleashedImporter.LibraryProblem(ReflexInstall.Dir);
            OuUi.Text(problem ?? "Import the nationals, supercross and supermoto tracks, the freestyle arenas and the machine challenge courses of your own MX vs ATV Unleashed " +
                                 "(the PC game's folder, or your Open Unleashed install) as tracks of your game library. Your own game files are only read; nothing from Unleashed ships with " + OpenReflex.Name + ".",
                      "mxa-library-description", copy).style.whiteSpace = WhiteSpace.Normal;
            int installed = 0;
            try { installed = UnleashedLevels.All.Count; } catch (Exception) { }
            var state = OuUi.Text(installed > 0 ? $"{installed} UNLEASHED {(installed == 1 ? "TRACK" : "TRACKS")} INSTALLED" : "NO UNLEASHED TRACKS INSTALLED", "mxa-library-state", copy);
            state.name = "ou-library-unleashed-state"; state.style.whiteSpace = WhiteSpace.Normal;
            var actions = OuUi.Div("ou-library-actions", card);
            actions.style.flexDirection = FlexDirection.Column; actions.style.width = 300; actions.style.flexShrink = 0;
            var b = OuUi.Button(UnleashedImportRunning ? "IMPORT IN PROGRESS" : UnleashedScanRunning ? "CHECK IN PROGRESS" : "CHOOSE UNLEASHED FOLDER", "mxa-secondary mxa-library-action", () => { if (UnleashedBusy) ShowUnleashedProgress(); else ShowUnleashedSources(); }, actions);
            b.name = "ou-library-unleashed-import";
            b.style.width = 300; b.style.height = 54; b.style.marginTop = 6;
            var label = b.Q<Label>(); if (label != null) { label.style.fontSize = 26; label.style.letterSpacing = 2; }
            b.SetEnabled(problem == null || UnleashedBusy);
        }

        // --------------------------------------------------------------------------------------------- the folder

        public void ShowUnleashedSources()
        {
            Clear(Screen.Install, "import");
            SetBackground(null);
            DriveScanner.Default.BeginRefresh();
            if (string.IsNullOrEmpty(_unleashedDir)) _unleashedDir = Options.ReflexSave.GetString(UnleashedFolderKey, "");   // (the folder used last)
            if (string.IsNullOrEmpty(_unleashedDir) || !ReflexFs.Within(2500, () => Directory.Exists(_unleashedDir), out _))
                _unleashedDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var box = OuUi.Div("mxa-panel mxa-install", _layer);
            box.name = "ou-unleashed-sources";
            OuUi.Text("MX vs ATV UNLEASHED", "mxa-screen-title", box);
            OuUi.Text("Choose the folder of your own MX vs ATV Unleashed (the PC game), or your Open Unleashed install: the game folder, its Game Files folder or its data folder " +
                      "(the one that holds Nat01.pak, SX01.pak ...). Then you choose the tracks to import. Your own game files are only read; nothing from Unleashed ships with " + OpenReflex.Name + ".",
                      "mxa-install-text", box).style.whiteSpace = WhiteSpace.Normal;
            var field = new TextField { value = _unleashedDir }; field.name = "ou-unleashed-path"; field.AddToClassList("mxa-path"); box.Add(field);
            var status = OuUi.Text("", "mxa-install-status", box); status.name = "ou-unleashed-status"; status.style.whiteSpace = WhiteSpace.Normal;
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.AddToClassList("mxa-dir-list"); box.Add(sv);
            var buttons = OuUi.Div("mxa-install-buttons", box);
            VisualElement use = null;

            void Refresh(string dir)
            {
                _unleashedDir = dir; field.SetValueWithoutNotify(dir);
                bool ok = LooksLikeUnleashedFolder(dir);
                status.text = ok ? "✓  MX vs ATV Unleashed tracks found here" : "No MX vs ATV Unleashed tracks here: open the game folder, its Game Files folder or its data folder.";
                status.EnableInClassList("mxa-ok", ok);
                sv.contentContainer.Clear();
                OuUi.Text("DRIVES & PLACES", "mxa-dir-header", sv.contentContainer);
                foreach (var (path, label) in InstallPlaces(false))
                {
                    var pp = path;
                    OuUi.Item(OuUi.Text(label, "mxa-dir", sv.contentContainer), () => Refresh(pp));
                }
                OuUi.Text("IN " + ShortPath(dir).ToUpperInvariant(), "mxa-dir-header", sv.contentContainer);
                string parent = ReflexPaths.Parent(dir);
                if (parent != null) OuUi.Item(OuUi.Text("←  ..", "mxa-dir", sv.contentContainer), () => Refresh(parent));
                try
                {
                    foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        string name = Path.GetFileName(d);
                        if (name.StartsWith(".") || ReflexMods.IsJunk(name)) continue;
                        var dd = d; bool unleashed = LooksLikeUnleashedFolder(d);
                        var it = OuUi.Item(OuUi.Text(name + (unleashed ? "   ✓ UNLEASHED" : ""), "mxa-dir", sv.contentContainer), () => Refresh(dd));
                        it.EnableInClassList("mxa-ok", unleashed);
                    }
                }
                catch (Exception e) { OuUi.Text("This folder can't be read: " + e.Message, "mxa-error", sv.contentContainer); }
            }
            field.isDelayed = true;
            field.RegisterValueChangedCallback(e =>
            {
                // any drive letter, a network path; a pasted file (a pak, the game's exe) stands for its folder
                string target = ResolveTypedFolder(e.newValue, out string why);
                if (target != null) Refresh(target);
                else { status.text = why; status.RemoveFromClassList("mxa-ok"); field.SetValueWithoutNotify(_unleashedDir); }
            });
            use = OuUi.Button("USE THIS FOLDER", "mxa-start", () =>
            {
                if (LooksLikeUnleashedFolder(_unleashedDir)) BeginUnleashedScan();
                else { status.text = "No MX vs ATV Unleashed tracks here: open the game folder, its Game Files folder or its data folder."; status.RemoveFromClassList("mxa-ok"); OuSound.Play("Locked"); }
            }, buttons);
            use.name = "ou-unleashed-use";
            OuUi.Button("BACK", "mxa-secondary", LeaveUnleashed, buttons);
            Refresh(_unleashedDir);
            if (_unleashedNotice != null) { status.text = _unleashedNotice; status.RemoveFromClassList("mxa-ok"); }
            _unleashedNotice = null;
            DrivesChanged();
            ScreenUpdate = () => { if (DrivesChanged()) Refresh(_unleashedDir); };
            _nav.OnCancel = () => { LeaveUnleashed(); return true; };
            _nav.DefaultItem = () => use;
            Hints((OuUi.KeySubmit, "OPEN / SELECT"), (OuUi.KeyCancel, "BACK"));
            _nav.Focus(use);
        }

        /// Back to the library.
        void LeaveUnleashed()
        {
            if (UnleashedBusy) return;
            _unleashedFound = null;
            ShowLibrary(_libraryFromMain);
        }

        /// The shell goes (a ride starts, the game quits) while a scan or an import runs: cancelled, so no track is converted behind a
        /// ride (OnDestroy). The import ends within moments, removes the track in progress and its staging folder; the tracks finished
        /// before stay.
        void StopUnleashedImport()
        {
            try { _unleashedCancel?.Cancel(); } catch (ObjectDisposedException) { }
        }

        static bool s_unleashedStagingChecked;
        /// Once a run, when the shell first starts: what an Unleashed import that did not end left in the game files (the game was closed,
        /// or crashed, while it ran: a hidden staging folder, an archive on its way into Tracks) is removed on a worker thread. A staging
        /// folder whose lock is held belongs to an import that is running and stays (UnleashedImporter.CleanStaging).
        void CleanUnleashedStaging()
        {
            if (s_unleashedStagingChecked) return;
            s_unleashedStagingChecked = true;
            string library = ReflexInstall.Dir;   // (main thread: the game folder comes from Unity's paths and settings)
            if (UnleashedImporter.LibraryProblem(library) != null) return;
            Task.Run(() =>
            {
                try { UnleashedImporter.CleanStaging(library, s => Debug.Log("[OpenReflex] " + s)); }
                catch (Exception ex) { Debug.LogWarning("[OpenReflex] Unleashed import, staging folders: " + ex.Message); }
            });
        }

        /// A folder the importer finds Unleashed tracks in: the game folder, its Game Files folder or the data folder (cheap: names only).
        static bool LooksLikeUnleashedFolder(string d)
        {
            try { return UnleashedImporter.DataFolder(d) != null; }
            catch (Exception) { return false; }
        }

        // --------------------------------------------------------------------------------------------- scan / import (worker thread)

        void BeginUnleashedScan()
        {
            if (UnleashedBusy || string.IsNullOrEmpty(_unleashedDir)) return;
            _unleashedFound = null; _unleashedError = null; _unleashedImporting = false;
            _unleashedCancel = new CancellationTokenSource();
            string picked = _unleashedDir; var token = _unleashedCancel.Token;
            string library = ReflexInstall.Dir;   // (main thread: the game folder comes from Unity's paths and settings)
            _unleashedStatus = "Reading your Unleashed folder…";
            _unleashedTask = Task.Run(() =>
            {
                // what an import that did not end left in the game files (the game closed, a crash): its staging folder goes now
                try { if (UnleashedImporter.LibraryProblem(library) == null) UnleashedImporter.CleanStaging(library, s => Debug.Log("[OpenReflex] " + s)); }
                catch (Exception ex) { Debug.LogWarning("[OpenReflex] Unleashed import, staging folders: " + ex.Message); }
                _unleashedFound = UnleashedImporter.Scan(picked, library, token);
            }, token);
            ShowUnleashedProgress();
        }

        void BeginUnleashedImport()
        {
            if (UnleashedBusy || _unleashedFound == null) return;
            string library = ReflexInstall.Dir;   // (main thread)
            var found = _unleashedFound;
            var chosen = found.tracks.Where(t => t.Ok && _unleashedChosen.Contains(t.level)).ToList();
            if (chosen.Count == 0 || library == null) return;
            _unleashedCancel = new CancellationTokenSource(); _unleashedResult = null; _unleashedError = null; _unleashedImporting = true; _unleashedProgress = null;
            var token = _unleashedCancel.Token;
            var options = new UnleashedImportOptions { freeBytes = ReflexAssetImport.FreeBytes, log = s => Debug.Log("[OpenReflex] " + s) };
            _unleashedStatus = "Starting…";
            Debug.Log("[OpenReflex] Unleashed import: " + string.Join(", ", chosen.Select(c => c.level)) + " -> " + Path.Combine(library, ReflexDataPack.TracksDir) + " (from " + found.dataDir + ")");
            _unleashedTask = Task.Run(() =>
            {
                _unleashedResult = UnleashedImporter.Import(found, chosen, library, options, p => Interlocked.Exchange(ref _unleashedProgress, p), token);
            }, token);
            ShowUnleashedProgress();
        }

        /// The running scan / import (CANCEL).
        void ShowUnleashedProgress()
        {
            Clear(Screen.Install, "import");
            var box = OuUi.Div("mxa-panel mxa-install", _layer);
            box.name = "ou-unleashed-progress";
            OuUi.Text(_unleashedImporting ? "IMPORTING UNLEASHED TRACKS" : "CHECKING YOUR UNLEASHED FOLDER", "mxa-screen-title", box);
            string library = ReflexInstall.Dir;
            OuUi.Text(_unleashedImporting ? "Converting each track on this computer and installing it into " + (library != null ? Path.Combine(library, ReflexDataPack.TracksDir) : "your game files") +
                                            ". Your Unleashed folder and your Reflex game files are only read. CANCEL removes the track in progress; tracks finished before stay."
                                          : "Looking for the tracks in your folder. Your files are only read.", "mxa-install-text", box).style.whiteSpace = WhiteSpace.Normal;
            _unleashedStatusLabel = OuUi.Text(_unleashedStatus, "mxa-install-status", box); _unleashedStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            var bar = OuUi.Div("ou-progress", box);
            bar.name = "ou-unleashed-bar";
            _unleashedFill = OuUi.Div("ou-progress-fill", bar); OuTheme.Backdrop(_unleashedFill);
            _unleashedFill.style.width = Length.Percent(0);
            _unleashedAllLabel = null; _unleashedAllFill = null;
            if (_unleashedImporting)
            {   // the whole import under the track in progress
                _unleashedAllLabel = OuUi.Text("", "mxa-note", box); _unleashedAllLabel.style.whiteSpace = WhiteSpace.Normal;
                var all = OuUi.Div("ou-progress", box);
                all.name = "ou-unleashed-bar-all";
                _unleashedAllFill = OuUi.Div("ou-progress-fill", all); OuTheme.Backdrop(_unleashedAllFill);
                _unleashedAllFill.style.width = Length.Percent(0);
            }
            var cancel = OuUi.Button("CANCEL", "mxa-secondary", () => _unleashedCancel?.Cancel(), box);
            cancel.name = "ou-unleashed-cancel";
            _nav.DefaultItem = () => cancel; _nav.OnCancel = () => { _unleashedCancel?.Cancel(); return true; };
            Hints((OuUi.KeySubmit, "SELECT"), (OuUi.KeyCancel, "CANCEL"));
            _nav.Focus(cancel);
        }

        /// Called every frame (UpdateBuild): the worker's progress, then the tracks found or the import's results.
        void UpdateUnleashedImport()
        {
            if (_unleashedTask == null) return;
            if (!_unleashedTask.IsCompleted)
            {
                if (Current != Screen.Install || _layer.Q("ou-unleashed-progress") == null || _unleashedStatusLabel == null || _unleashedFill == null) return;
                var p = _unleashedImporting ? Interlocked.CompareExchange(ref _unleashedProgress, null, null) : null;
                if (p != null)
                {
                    _unleashedStatusLabel.text = (p.name ?? p.level ?? "").ToUpperInvariant() + "  ·  " + (p.stage ?? "").ToUpperInvariant();
                    _unleashedFill.style.width = Length.Percent(Mathf.Clamp01(p.track) * 100f);
                    if (_unleashedAllLabel != null) _unleashedAllLabel.text = $"TRACK {p.index + 1} OF {p.count}  ·  {Mathf.Clamp01(p.overall) * 100f:0}% OF THE IMPORT";
                    if (_unleashedAllFill != null) _unleashedAllFill.style.width = Length.Percent(Mathf.Clamp01(p.overall) * 100f);
                }
                else _unleashedStatusLabel.text = _unleashedStatus;
                return;
            }
            var t = _unleashedTask; _unleashedTask = null; _unleashedCancel?.Dispose(); _unleashedCancel = null;
            if (t.IsFaulted) _unleashedError = t.Exception?.GetBaseException();
            bool cancelled = t.IsCanceled || _unleashedError is OperationCanceledException;
            if (_unleashedImporting)
            {
                _unleashedImporting = false;
                if (_unleashedResult != null && _unleashedResult.cancelled) cancelled = true;
                // the new levels: the library's archives, the event list and the shell's content read again, so FREE RIDE / RACE list them
                // without a restart (UnleashedLevels follows RxFiles.Generation)
                try { RxFiles.Reset(); ReflexEvents.Refresh(); OpenContent.Reset(); }
                catch (Exception e) { Debug.LogWarning("[OpenReflex] Unleashed import, the event list: " + e.Message); }
                if (_unleashedError != null && !cancelled)
                {
                    if (_unleashedError is UnleashedImportException) Debug.LogWarning("[OpenReflex] Unleashed import: " + _unleashedError.Message);
                    else Debug.LogException(_unleashedError);
                }
                if (_unleashedResult != null)
                    foreach (var r in _unleashedResult.tracks)
                    {
                        // (a level the game does not list after all: UnleashedLevels' own checks of level.json and the terrain say why in the log)
                        if (r.Ok) { try { if (UnleashedLevels.Get(r.level) == null) r.warnings.Add("installed, but the game does not list it (see the log)"); } catch (Exception) { } }
                        Debug.Log("[OpenReflex] Unleashed import " + r + (r.Ok ? $" ({r.bytes >> 20} MB, {r.files} files, {r.seconds:0.0} s)" : "") + (r.warnings.Count > 0 ? "  [" + string.Join("; ", r.warnings) + "]" : ""));
                    }
                RefreshUnleashedInstalled();
                ShowUnleashedResults(cancelled);
                return;
            }
            if (_unleashedError != null && !cancelled) Debug.LogWarning("[OpenReflex] Unleashed folder: " + _unleashedError.Message);
            if (cancelled || _unleashedFound == null || _unleashedFound.dataDir == null)
            {
                _unleashedNotice = cancelled ? "Cancelled." : _unleashedFound != null ? _unleashedFound.warnings.FirstOrDefault() : "This folder could not be read: " + _unleashedError?.Message;
                _unleashedFound = null;
                ShowUnleashedSources(); return;
            }
            // the folder is kept for the next time (the client save)
            try { Options.ReflexSave.SetString(UnleashedFolderKey, _unleashedDir); Options.ReflexSave.Save(); } catch (Exception e) { Debug.LogWarning("[OpenReflex] Unleashed folder not saved: " + e.Message); }
            Debug.Log("[OpenReflex] Unleashed folder " + _unleashedFound.dataDir + ": " + _unleashedFound.Ready.Count() + " of " + _unleashedFound.tracks.Count + " tracks found, " + _unleashedFound.tracks.Count(x => x.installed) + " installed already");
            // chosen: every track that can be imported and is not installed yet (one installed is chosen by the player: a re-import)
            _unleashedChosen.Clear();
            foreach (var tr in _unleashedFound.tracks) if (tr.Ok && !tr.installed) _unleashedChosen.Add(tr.level);
            ShowUnleashedTracks();
        }

        /// After an import: what is installed now, for the list (MORE TRACKS); the tracks just imported are not chosen again.
        void RefreshUnleashedInstalled()
        {
            if (_unleashedFound == null) return;
            string library = ReflexInstall.Dir;
            foreach (var tr in _unleashedFound.tracks) tr.installed = UnleashedImporter.Installed(library, tr.level);
            if (_unleashedResult != null)
                foreach (var r in _unleashedResult.tracks) if (r.Ok) _unleashedChosen.Remove(r.level);
        }

        // --------------------------------------------------------------------------------------------- tracks / results

        static readonly string[] UnleashedGroups = { "NATIONALS", "SUPERCROSS", "SUPERMOTO", "FREESTYLE  ·  FREE RIDE MAPS", "MACHINE CHALLENGE  ·  FREE RIDE MAPS", "OTHER" };
        static string UnleashedKindLabel(UnCatalogEntry e)
        {
            switch (UnleashedImporter.GroupOf(e.category))
            {
                case 0: return "NATIONAL";
                case 1: return "SUPERCROSS";
                case 2: return "SUPERMOTO";
                case 3: return "FREESTYLE  ·  FREE RIDE MAP (NO LAP RACE)";
                case 4: return "MACHINE CHALLENGE  ·  FREE RIDE MAP (NO LAP RACE)";
                default: return (e.category ?? "").ToUpperInvariant();
            }
        }

        void ShowUnleashedTracks()
        {
            Clear(Screen.Install, "import");
            var box = OuUi.Div("mxa-panel mxa-install mxa-library", _layer);
            box.name = "ou-unleashed-tracks";
            OuUi.Text("UNLEASHED TRACKS", "mxa-screen-title", box);
            var found = _unleashedFound;
            string library = ReflexInstall.Dir;
            string problem = UnleashedImporter.LibraryProblem(library);
            int ready = found.Ready.Count();
            OuUi.Text(ready == 0 ? "No MX vs ATV Unleashed track was found in this folder."
                      : $"{ready} of {found.tracks.Count} tracks found in your folder are ready. Choose the ones to import; each is converted on this computer and installed as a track of your game library: " +
                        "the nationals, supercross and supermoto tracks race, the freestyle arenas and machine challenge courses are Free Ride maps.",
                      ready == 0 ? "mxa-error" : "mxa-install-text", box).style.whiteSpace = WhiteSpace.Normal;
            OuUi.Text("Converted from your own game files in " + found.dataDir + ", which are only read; nothing from Unleashed ships with " + OpenReflex.Name + ".", "mxa-note", box).style.whiteSpace = WhiteSpace.Normal;
            if (problem != null) OuUi.Text(problem, "mxa-error", box).style.whiteSpace = WhiteSpace.Normal;
            foreach (var w in found.warnings.Take(6)) OuUi.Text(w, "mxa-note", box).style.whiteSpace = WhiteSpace.Normal;
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("mxa-library-list"); box.Add(scroll);
            var buttons = OuUi.Div("mxa-install-buttons", box);
            VisualElement import = null, first = null;
            void Count()
            {
                int n = found.tracks.Count(t => t.Ok && _unleashedChosen.Contains(t.level));
                var lbl = import?.Q<Label>(); if (lbl != null) lbl.text = n > 0 ? $"IMPORT {n} {(n == 1 ? "TRACK" : "TRACKS")}" : "IMPORT";
                import?.SetEnabled(n > 0 && problem == null);
            }
            int group = -1;
            foreach (var t in found.tracks)
            {
                int g = UnleashedImporter.GroupOf(t.entry.category);
                if (g != group)
                {   // the category's header, with what it has
                    group = g;
                    int of = found.tracks.Count(x => UnleashedImporter.GroupOf(x.entry.category) == g), have = found.tracks.Count(x => x.Ok && UnleashedImporter.GroupOf(x.entry.category) == g);
                    OuUi.Text(UnleashedGroups[g] + "  ·  " + have + " OF " + of, "mxa-dir-header", scroll.contentContainer);
                }
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                row.name = "ou-unleashed-track-" + t.entry.id;
                var c = OuUi.Div("ou-cache-copy", row);
                var title = OuUi.Text("", "mxa-track-name ou-cache-name", c);
                string blocked = t.problem;
                // installed already: marked, and not chosen unless the player chooses it
                string state = t.installed ? "INSTALLED  ·  CHOOSE IT TO IMPORT IT AGAIN (REPLACES IT)" : "READY";
                OuUi.Text(blocked ?? $"{UnleashedKindLabel(t.entry)}  ·  {OpenWorldCache.SizeText(t.bytes)}  ·  {state}",
                          blocked == null ? "ou-card-sub ou-cache-sub" : "mxa-error", c).style.whiteSpace = WhiteSpace.Normal;
                var source = t; string level = t.level;
                void Title() => title.text = (blocked == null ? (_unleashedChosen.Contains(level) ? "☑  " : "☐  ") : "✕  ") + source.Label.ToUpperInvariant();
                Title();
                if (blocked == null)
                {
                    OuUi.Item(row, () => { if (!_unleashedChosen.Remove(level)) _unleashedChosen.Add(level); Title(); Count(); });
                    if (first == null) first = row;
                }
            }
            import = OuUi.Button("IMPORT", "mxa-start", BeginUnleashedImport, buttons); import.name = "ou-unleashed-import";
            OuUi.Button("ALL", "mxa-secondary", () => { foreach (var t in found.tracks) if (t.Ok) _unleashedChosen.Add(t.level); ShowUnleashedTracks(); }, buttons).name = "ou-unleashed-all";
            OuUi.Button("NONE", "mxa-secondary", () => { _unleashedChosen.Clear(); ShowUnleashedTracks(); }, buttons).name = "ou-unleashed-none";
            OuUi.Button("BACK", "mxa-secondary", ShowUnleashedSources, buttons).name = "ou-unleashed-other";
            Count();
            _nav.OnCancel = () => { ShowUnleashedSources(); return true; };
            _nav.DefaultItem = () => import.enabledInHierarchy ? import : first ?? import;
            Hints((OuUi.KeySubmit, "CHOOSE"), (OuUi.KeyCancel, "BACK"));
            _nav.Focus(first ?? import);
        }

        void ShowUnleashedResults(bool cancelled)
        {
            Clear(Screen.Install, "import");
            var box = OuUi.Div("mxa-panel mxa-install mxa-library", _layer);
            box.name = "ou-unleashed-results";
            OuUi.Text("UNLEASHED IMPORT", "mxa-screen-title", box);
            var results = _unleashedResult != null ? _unleashedResult.tracks : new List<UnleashedTrackResult>();
            int ok = results.Count(r => r.Ok);
            string root = _unleashedResult?.root ?? ReflexInstall.Dir;
            string listed = "They are listed in FREE RIDE and in RACE under MX vs ATV Unleashed.";
            string head = cancelled ? "Import cancelled. The track in progress was removed" + (ok > 0 ? $"; the {ok} finished before {(ok == 1 ? "stays" : "stay")} installed. " + listed : ".")
                        : _unleashedError != null ? "Import stopped: " + _unleashedError.Message
                        : ok == results.Count ? $"{ok} {(ok == 1 ? "track" : "tracks")} installed. " + listed
                        : $"{ok} of {results.Count} tracks installed." + (ok > 0 ? " " + listed : "");
            bool bad = cancelled || _unleashedError != null || ok < results.Count;
            OuUi.Text(head, bad ? "mxa-error" : "mxa-install-text", box).style.whiteSpace = WhiteSpace.Normal;
            if (ok > 0) OuUi.Text("Installed in " + (root != null ? Path.Combine(root, ReflexDataPack.TracksDir) : "your game files") + ".", "mxa-note", box).style.whiteSpace = WhiteSpace.Normal;
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("mxa-library-list"); box.Add(scroll);
            foreach (var r in results)
            {
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                row.name = "ou-unleashed-result-" + r.entry.id;
                var c = OuUi.Div("ou-cache-copy", row);
                OuUi.Text((r.Ok ? "✓  " : "✕  ") + r.displayName.ToUpperInvariant(), "mxa-track-name ou-cache-name", c);
                string detail;
                switch (r.state)
                {
                    case UnleashedImportState.Imported:
                        detail = $"{UnleashedKindLabel(r.entry)}  ·  {OpenWorldCache.SizeText(r.bytes)}  ·  {r.seconds:0} S  ·  {r.archive}"
                                 + (r.replaced ? "  ·  REPLACED THE TRACK INSTALLED BEFORE" : "")
                                 + (r.warnings.Count > 0 ? "  ·  " + string.Join("  ·  ", r.warnings).ToUpperInvariant() : "");
                        break;
                    case UnleashedImportState.Cancelled: detail = "CANCELLED  ·  NOT INSTALLED"; break;
                    default: detail = string.IsNullOrEmpty(r.error) ? "Not installed" : r.error; break;
                }
                OuUi.Text(detail, r.state == UnleashedImportState.Failed ? "mxa-error" : "ou-card-sub ou-cache-sub", c).style.whiteSpace = WhiteSpace.Normal;
            }
            var buttons = OuUi.Div("mxa-install-buttons", box);
            var done = OuUi.Button("DONE", "mxa-start", LeaveUnleashed, buttons);
            done.name = "ou-unleashed-done";
            OuUi.Button("MORE TRACKS", "mxa-secondary", () => { if (_unleashedFound != null) ShowUnleashedTracks(); else ShowUnleashedSources(); }, buttons).name = "ou-unleashed-more";
            _nav.OnCancel = () => { LeaveUnleashed(); return true; };
            _nav.DefaultItem = () => done;
            Hints((OuUi.KeySubmit, "SELECT"), (OuUi.KeyCancel, "BACK"));
            _nav.Focus(done);
        }
    }
}
