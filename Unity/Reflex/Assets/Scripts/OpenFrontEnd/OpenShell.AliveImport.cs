// GAME LIBRARY  ·  MX vs ATV ALIVE: the player's own Alive content (Xbox 360) imported into the client's content folder by the in-client
// importer (Content/AliveImport: no Python, no native code, macOS and Windows).
//   sources   a folder / file browser that works with a gamepad: the disc image (.iso), an extracted disc folder, the 5451087B content
//             folder of an Xbox 360 drive (or its 00000002 folder of LIVE packages), single LIVE packages; ADD THIS FOLDER / a file adds it
//   levels    FIND LEVELS opens them on a worker thread (STFS hash trees, the original-release check, the game's level names), adds the
//             menu pictures of levels imported before they existed (AliveImporter.AddPictures) and lists every level found: choose them, IMPORT
//   import    the chosen levels converted and installed one after another on a worker thread (progress, CANCEL), then each level's
//             result; AliveLevels.Reset() so the MX vs ATV Alive category lists them at once. The player's sources are only read.
//   bikes     the Alive motorcycles found (AliveBikes: each rides as the Reflex bike of its class and brand) are listed under the
//             levels, all chosen; IMPORT converts the chosen ones into <content>/AliveBikes (.rxmod folders) after the levels and the
//             garage lists them as MOD cards (BikeMods.Refresh)
//   music     the disc's soundtrack (the 23 songs of the jukebox: AliveMusic) is one more row: chosen, its songs are converted with the
//             client's ffmpeg into the music pool, Mods/Music/MX vs ATV Alive (ReflexGameMusic), after the levels and bikes; the music folder
//             is scanned again at once. OPTIONS > AUDIO > SOUNDTRACK plays them (ALL SONGS, ALIVE SONGS ONLY)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Reflex.Port.Content;
using Reflex.Port.Content.AliveImport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.OpenFrontEnd
{
    public sealed partial class OpenShell
    {
        readonly List<string> _aliveSourcePaths = new List<string>();
        string _aliveDir;
        AliveSources _aliveFound;
        readonly HashSet<string> _aliveChosen = new HashSet<string>(StringComparer.Ordinal);
        Task _aliveTask; CancellationTokenSource _aliveCancel;
        string _aliveStatus = ""; AliveProgress _aliveProgress;
        Label _aliveStatusLabel; VisualElement _aliveFill;
        List<AliveLevelResult> _aliveResults; Exception _aliveError;
        List<AliveBikeResult> _aliveBikeResults;
        List<AliveGearResult> _aliveGearResults;   // (the rider gear: one row of the list, GearKey)
        const string GearKey = "gear:all";
        const string BikeKey = "bike:", AliveMusicKey = "music:";
        /// The disc's soundtrack (null: none in the sources), whether it is chosen, and its import's result.
        AliveMusicSource _aliveMusic; bool _aliveMusicChosen; ReflexGameMusicResult _aliveMusicResult;
        /// Why the soundtrack's row can't be chosen (null: it can).
        string AliveMusicBlocked(string musicFolder, string ffmpeg) => _aliveMusic == null ? "No soundtrack was found" : !_aliveMusic.Ok ? _aliveMusic.problem : _aliveMusic.status.inTheWay
            ?? (musicFolder == null ? "The music folder (Mods/Music) was not found." : ffmpeg == null ? "The Alive songs are converted with ffmpeg, which was not found: put an ffmpeg build in the ffmpeg folder beside the game (the Windows download has one) or set REFLEX_FFMPEG." : null);
        /// The last scan's soundtrack and the last import's result for it (tests read them).
        public AliveMusicSource AliveMusicFound => AliveImportRunning ? null : _aliveMusic;
        public ReflexGameMusicResult AliveMusicResult => AliveImportRunning ? null : _aliveMusicResult;
        bool _aliveImporting; string _aliveNotice;
        readonly List<string> _aliveOrder = new List<string>();
        public bool AliveImportRunning => _aliveTask != null && !_aliveTask.IsCompleted;
        /// The last import's per-level results (null before / during one; tests read them).
        public IReadOnlyList<AliveLevelResult> AliveResults => AliveImportRunning ? null : _aliveResults;

        /// The content folder the importer writes into: the connected install when it is an extracted Reflex folder (null otherwise).
        static string AliveContent()
        {
            string dir = ReflexInstall.Dir;
            return dir != null && AliveImporter.ContentProblem(dir) == null ? dir : null;
        }

        /// The library card (ShowLibrary): the Alive levels installed, IMPORT ALIVE CONTENT.
        void AliveLibraryCard(VisualElement list)
        {
            var card = OuUi.Div("mxa-library-card", list);
            card.style.flexWrap = Wrap.Wrap;
            var copy = OuUi.Div("mxa-library-copy", card);
            copy.style.flexGrow = 1; copy.style.flexShrink = 1; copy.style.minWidth = 240;
            OuUi.Text("OPTIONAL  ·  XBOX 360 CONTENT", "mxa-library-platform", copy);
            OuUi.Text("MX vs ATV ALIVE", "mxa-track-title", copy);
            string content = AliveContent();
            int count = 0; try { count = AliveLevels.All.Count; } catch (Exception) { }
            // what is installed: vehicles (and how many an older version imported: no high-res garage model / colours yet), gear pieces, levels
            int bikes = 0, old = 0, gear = 0;
            try { var alive = BikeMods.All.Where(m => m.rx != null && m.rx.type == "bikes" && m.only == null && m.id.StartsWith(AliveBikes.Folder + "/", StringComparison.Ordinal)).ToList(); bikes = alive.Count; old = alive.Count(m => m.rx.bikes.Any(x => x.importOutdated)); gear = RxGear.All.Count(); }
            catch (Exception) { }
            if (old > 0)
            {
                var upd = OuUi.Text($"{old} {(old == 1 ? "VEHICLE" : "VEHICLES")} NEED AN UPDATE  ·  IMPORT AGAIN WITH THE DISC AND YOUR DOWNLOADED CONTENT", "mxa-library-state", copy);
                upd.name = "ou-library-alive-update"; upd.style.whiteSpace = WhiteSpace.Normal; upd.AddToClassList("mxa-ok"); upd.style.marginTop = 2;   // (under the title: the card's lower lines scroll out of the list's view on a small window)
            }
            OuUi.Text(content == null ? (ReflexInstall.Dir == null ? "Connect the Reflex game folder first." : AliveImporter.ContentProblem(ReflexInstall.Dir))
                                      : "Import the vehicles, rider gear and levels of your own MX vs ATV Alive: the game disc and your downloaded content, and the disc's soundtrack into your music. Your files are only read.", "mxa-library-description", copy).style.whiteSpace = WhiteSpace.Normal;
            var have = new List<string>();
            if (bikes > 0) have.Add($"{bikes} {(bikes == 1 ? "VEHICLE" : "VEHICLES")}");
            if (gear > 0) have.Add($"{gear} RIDER GEAR PIECES");
            if (count > 0) have.Add($"{count} {(count == 1 ? "LEVEL" : "LEVELS")}");
            var state = OuUi.Text(have.Count > 0 ? "INSTALLED  ·  " + string.Join("  ·  ", have) : "NOTHING OF ALIVE INSTALLED YET", "mxa-library-state", copy);
            state.name = "ou-library-alive-state"; state.style.whiteSpace = WhiteSpace.Normal;
            var actions = OuUi.Div("ou-library-actions", card);
            actions.style.flexDirection = FlexDirection.Column; actions.style.width = 300; actions.style.flexShrink = 0;
            var b = OuUi.Button(AliveImportRunning ? "IMPORT IN PROGRESS" : "IMPORT ALIVE CONTENT", "mxa-secondary mxa-library-action", () => { if (AliveImportRunning) ShowAliveProgress(); else ShowAliveSources(); }, actions);
            b.name = "ou-library-alive-import";
            b.style.width = 300; b.style.height = 54; b.style.marginTop = 6;
            var label = b.Q<Label>(); if (label != null) { label.style.fontSize = 26; label.style.letterSpacing = 2; }
            b.SetEnabled(content != null);
        }

        // --------------------------------------------------------------------------------------------- sources

        public void ShowAliveSources()
        {
            Clear(Screen.Install, "import");
            SetBackground(null);
            LoadAliveSources();
            DriveScanner.Default.BeginRefresh();
            if (string.IsNullOrEmpty(_aliveDir) || !ReflexFs.Within(2500, () => Directory.Exists(_aliveDir), out _))
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                _aliveDir = ReflexFs.Within(2500, () => Directory.Exists(downloads), out _) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            var box = OuUi.Div("mxa-panel mxa-install", _layer);
            box.name = "ou-alive-sources";
            OuUi.Text("IMPORT MX vs ATV ALIVE  ·  1 OF 2: YOUR FILES", "mxa-screen-title", box);
            OuUi.Text("Alive's content is in two places. Add both to get everything, then FIND CONTENT. Your files are only read, nothing in them is changed.", "mxa-install-text", box).style.whiteSpace = WhiteSpace.Normal;
            // the two sources, each with what it holds and whether it is added (a source is told apart by what it is: AliveSourceKind)
            var slots = OuUi.Div("ou-alive-slots", box); slots.style.flexDirection = FlexDirection.Row; slots.style.flexShrink = 0; slots.style.marginBottom = 8;
            VisualElement find = null;
            void ShowChosen()
            {
                slots.Clear();
                foreach (var (kind, title, what) in new[] {
                    ("disc", "1  ·  THE GAME DISC", "The disc image (.iso) or an extracted disc folder. It holds Alive's own vehicles (the Blaze and Takashi bikes and ATVs, the 650, the ATV 1000), its levels and its rider gear."),
                    ("downloads", "2  ·  DOWNLOADED CONTENT", "The 5451087B folder of an Xbox 360 Content folder (or its 00000002 folder), or single downloaded packs. It holds the Honda, Yamaha, Suzuki and KTM vehicles, the 50cc, the MX250 FMX and the extra levels and rider gear.") })
                {
                    string k = kind;
                    var mine = _aliveSourcePaths.Where(p => AliveSourceKind(p) == k).ToList();
                    var card = OuUi.Div("ou-cache-row", slots); card.name = "ou-alive-slot-" + kind;
                    card.style.flexGrow = 1; card.style.flexBasis = 0; card.style.flexShrink = 1; if (kind == "disc") card.style.marginRight = 10;
                    var c = OuUi.Div("ou-cache-copy", card);
                    OuUi.Text(title, "mxa-track-name ou-cache-name", c);
                    OuUi.Text(what, "ou-card-sub ou-cache-sub", c).style.whiteSpace = WhiteSpace.Normal;
                    var st = OuUi.Text(mine.Count == 0 ? "NOT ADDED YET" : "✓  ADDED  ·  " + string.Join("  ·  ", mine.Select(p => ReflexPaths.LabelOf(p))), mine.Count == 0 ? "mxa-install-status" : "mxa-install-status mxa-ok", c);
                    st.name = "ou-alive-slot-state-" + kind; st.style.whiteSpace = WhiteSpace.Normal;
                    if (mine.Count > 0)
                        OuUi.Item(OuUi.Text("✕  REMOVE", "mxa-dir", c), () => { _aliveSourcePaths.RemoveAll(p => AliveSourceKind(p) == k); SaveAliveSources(); ShowChosen(); find?.SetEnabled(_aliveSourcePaths.Count > 0); }).name = "ou-alive-slot-remove-" + kind;
                }
            }
            ShowChosen();
            OuUi.Text("FIND THEM ON THIS COMPUTER  ·  OPEN FOLDERS, CHOOSE WHAT IS MARKED  +", "mxa-dir-header", box);
            var field = new TextField { value = _aliveDir }; field.name = "ou-alive-path"; field.AddToClassList("mxa-path"); box.Add(field);
            var status = OuUi.Text(_aliveNotice ?? "", "mxa-install-status", box); status.style.whiteSpace = WhiteSpace.Normal;
            _aliveNotice = null;
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.AddToClassList("mxa-dir-list"); box.Add(sv);
            var buttons = OuUi.Div("mxa-install-buttons", box);

            void Add(string path)
            {
                try { path = Path.GetFullPath(path); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { status.text = "That path can't be used: " + e.Message; return; }
                if (!_aliveSourcePaths.Any(p => ReflexPaths.SamePath(p, path))) _aliveSourcePaths.Add(path);
                SaveAliveSources();
                bool disc = _aliveSourcePaths.Any(p => AliveSourceKind(p) == "disc"), downloads = _aliveSourcePaths.Any(p => AliveSourceKind(p) == "downloads");
                status.text = "Added " + ReflexPaths.LabelOf(path) + " as " + (AliveSourceKind(path) == "disc" ? "THE GAME DISC" : "DOWNLOADED CONTENT") + ". "
                              + (disc && downloads ? "Both are added: FIND CONTENT." : disc ? "Now add your downloaded content (the 5451087B folder) if you have it, or FIND CONTENT." : "Now add the game disc (.iso) if you have it, or FIND CONTENT.");
                ShowChosen(); find?.SetEnabled(true);
            }
            void Refresh(string dir)
            {
                _aliveDir = dir; field.SetValueWithoutNotify(dir);
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
                        var dd = d; bool alive = LooksLikeAliveFolder(d), source = alive && IsAliveContentFolder(d);
                        // a content folder itself (5451087B, an extracted disc) is added by choosing it; a folder that only holds one is opened
                        var it = OuUi.Item(OuUi.Text(source ? "+  " + name + "   ✓ ALIVE CONTENT  ·  CHOOSE TO ADD" : name + (alive ? "   ✓ ALIVE CONTENT INSIDE  ·  OPEN" : ""), "mxa-dir", sv.contentContainer), () => { if (source) Add(dd); else Refresh(dd); });
                        it.EnableInClassList("mxa-ok", alive);
                    }
                    int shown = 0;
                    foreach (var f in Directory.GetFiles(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        if (shown >= 200) break;
                        string name = Path.GetFileName(f);
                        if (name.StartsWith(".") || !LooksLikeAliveFile(f)) continue;
                        shown++;
                        var ff = f;
                        var it = OuUi.Item(OuUi.Text("+  " + name + "   " + OpenWorldCache.SizeText(new FileInfo(f).Length), "mxa-dir", sv.contentContainer), () => Add(ff));
                        it.EnableInClassList("mxa-ok", true);
                    }
                }
                catch (Exception e) { OuUi.Text("This folder can't be read: " + e.Message, "mxa-error", sv.contentContainer); }
            }
            field.isDelayed = true;
            field.RegisterValueChangedCallback(e =>
            {
                // any drive letter, a network path, a disc image anywhere; clear sentences for what is not a path or not there
                string s = CleanPath(e.newValue), why = ReflexPaths.Validate(s, ReflexInstall.WindowsHost);
                try
                {
                    int look = 0; bool timedOut = false;
                    if (why == null) look = ReflexFs.Within(3000, () => File.Exists(s) ? 2 : Directory.Exists(s) ? 1 : 0, out timedOut);
                    if (why != null) { }
                    else if (timedOut) why = "That location did not answer (a network drive that is offline?): " + s;
                    else if (look == 2) { Add(s); Refresh(Path.GetDirectoryName(Path.GetFullPath(s))); return; }
                    else if (look == 1) { Refresh(Path.GetFullPath(s)); return; }
                    else why = ReflexPaths.NotFound(s, PeekRoots());
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is NotSupportedException || ex is UnauthorizedAccessException) { why = "That path can't be opened: " + ex.Message; }
                status.text = why; field.SetValueWithoutNotify(_aliveDir);
            });
            find = OuUi.Button("FIND CONTENT", "mxa-start", BeginAliveScan, buttons); find.name = "ou-alive-find";
            var add = OuUi.Button("ADD THE OPEN FOLDER", "mxa-secondary", () => Add(_aliveDir), buttons); add.name = "ou-alive-add";
            find.SetEnabled(_aliveSourcePaths.Count > 0);
            OuUi.Button("CLEAR", "mxa-secondary", () => { _aliveSourcePaths.Clear(); SaveAliveSources(); ShowChosen(); find.SetEnabled(false); status.text = ""; }, buttons);
            OuUi.Button("BACK", "mxa-secondary", LeaveAlive, buttons);
            Refresh(_aliveDir);
            DrivesChanged();
            ScreenUpdate = () => { if (DrivesChanged()) Refresh(_aliveDir); };
            _nav.OnCancel = () => { LeaveAlive(); return true; };
            _nav.DefaultItem = () => _aliveSourcePaths.Count > 0 ? find : (OuUi.FirstItem(sv.contentContainer) ?? add);
            Hints((OuUi.KeySubmit, "OPEN / ADD"), (OuUi.KeyCancel, "BACK"));
            _nav.Focus(_aliveSourcePaths.Count > 0 ? find : (OuUi.FirstItem(sv.contentContainer) ?? add));
        }

        /// Back to the library: the sources found are closed (their files are not held open).
        void LeaveAlive()
        {
            if (AliveImportRunning) return;
            _aliveFound?.Dispose(); _aliveFound = null;
            ShowLibrary(_libraryFromMain);
        }

        /// What a source is: "disc" (a disc image, an extracted disc folder) or "downloads" (the 5451087B folder, a downloaded pack).
        static string AliveSourceKind(string p)
        {
            try
            {
                if (p.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return "disc";
                if (Directory.Exists(p) && (Directory.EnumerateFiles(p, "*.xenon.level").Any() || Directory.Exists(Path.Combine(p, "Database")))) return "disc";
            }
            catch (Exception) { }
            return "downloads";
        }

        /// A folder that is a source itself: named 5451087B (or its 00000002 folder), or an extracted disc.
        static bool IsAliveContentFolder(string d)
        {
            try
            {
                string n = Path.GetFileName(d), parent = Path.GetFileName(Path.GetDirectoryName(d) ?? "");
                if (string.Equals(n, "5451087B", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(n, "00000002", StringComparison.OrdinalIgnoreCase) && string.Equals(parent, "5451087B", StringComparison.OrdinalIgnoreCase)) return true;
                return Directory.EnumerateFiles(d, "*.xenon.level").Any() || Directory.Exists(Path.Combine(d, "Database")) && Directory.EnumerateFiles(Path.Combine(d, "Database"), "*.xenon.level").Any();
            }
            catch (Exception) { return false; }
        }

        /// The sources are kept between sessions (the client save): the ones still there are offered again.
        const string AliveSourcesKey = "AliveImport.Sources";
        bool _aliveSourcesLoaded;
        void SaveAliveSources() { Options.ReflexSave.SetString(AliveSourcesKey, string.Join("\n", _aliveSourcePaths)); Options.ReflexSave.Save(); }
        void LoadAliveSources()
        {
            if (_aliveSourcesLoaded) return;
            _aliveSourcesLoaded = true;
            if (_aliveSourcePaths.Count > 0) return;
            foreach (string p in Options.ReflexSave.GetString(AliveSourcesKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (ReflexFs.Within(1500, () => File.Exists(p) || Directory.Exists(p), out _) && !_aliveSourcePaths.Any(q => ReflexPaths.SamePath(q, p))) _aliveSourcePaths.Add(p);
        }

        /// A folder that holds Alive data (cheap: names only).
        static bool LooksLikeAliveFolder(string d)
        {
            try
            {
                string n = Path.GetFileName(d);
                if (string.Equals(n, "5451087B", StringComparison.OrdinalIgnoreCase)) return true;
                if (Directory.Exists(Path.Combine(d, "5451087B"))) return true;
                return Directory.EnumerateFiles(d, "*.xenon.level").Any() || Directory.Exists(Path.Combine(d, "Database")) && Directory.EnumerateFiles(Path.Combine(d, "Database"), "*.xenon.level").Any();
            }
            catch (Exception) { return false; }
        }

        /// A disc image or a LIVE / PIRS / CON package.
        static bool LooksLikeAliveFile(string f)
        {
            if (f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                if (new FileInfo(f).Length < 0x971a) return false;
                using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var b = new byte[4]; if (s.Read(b, 0, 4) != 4) return false;
                    string m = System.Text.Encoding.ASCII.GetString(b);
                    return m == "LIVE" || m == "PIRS" || m == "CON ";
                }
            }
            catch (Exception) { return false; }
        }

        // --------------------------------------------------------------------------------------------- scan / import (worker thread)

        void BeginAliveScan()
        {
            if (AliveImportRunning || _aliveSourcePaths.Count == 0) return;
            _aliveFound?.Dispose(); _aliveFound = null; _aliveError = null; _aliveImporting = false; _aliveMusic = null;
            _aliveCancel = new CancellationTokenSource();
            var paths = _aliveSourcePaths.ToList(); var token = _aliveCancel.Token;
            string content = AliveContent(), music = MusicFolderOrNull();
            AliveSoundsLocate();
            _aliveStatus = "Opening your sources…";
            _aliveTask = Task.Run(() =>
            {
                var found = AliveImporter.Scan(paths, s => _aliveStatus = s, token);
                AliveSoundsCheckDecoder();
                // the menu pictures of levels imported before they existed, from these sources (no new import; AlivePictures)
                if (content != null)
                {
                    _aliveStatus = "Adding the levels' pictures";
                    try { AliveImporter.AddPictures(found, content, token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { found.warnings.Add("Level pictures: " + ex.Message); }
                }
                // the soundtrack on the disc (Database/Music.xenon.*), and what of it is in the music folder already
                _aliveStatus = "Checking the soundtrack";
                try { _aliveMusic = AliveMusic.Find(found.volumes, music, token); }
                catch (OperationCanceledException) { found.Dispose(); throw; }
                catch (Exception ex) { _aliveMusic = null; found.warnings.Add("Soundtrack: " + ex.Message); }
                _aliveFound = found;
            }, token);
            ShowAliveProgress();
        }

        void BeginAliveImport()
        {
            string content = AliveContent(), music = MusicFolderOrNull(), ffmpeg = FfmpegBinkSource.Relocate();   // (main thread: Unity's paths)
            if (AliveImportRunning || _aliveFound == null) return;
            var chosen = content == null ? new List<AliveLevelSource>() : _aliveFound.levels.Where(l => l.recognized && _aliveChosen.Contains(l.level)).ToList();
            var bikes = content == null ? new List<AliveBikeSource>() : _aliveFound.bikes.Where(b => b.Importable && _aliveChosen.Contains(BikeKey + b.aliveId)).ToList();
            var gear = content != null && _aliveChosen.Contains(GearKey) ? _aliveFound.gear.ToList() : new List<AliveGearModel>();
            var sounds = content == null ? new List<AliveEngineSoundSource>() : AliveSoundsChosen(content); string soundFfmpeg = _aliveFfmpeg;   // (the ffmpeg the scan checked for xma2)
            var song = _aliveMusicChosen && AliveMusicBlocked(music, ffmpeg) == null ? _aliveMusic : null;
            if (chosen.Count == 0 && bikes.Count == 0 && gear.Count == 0 && sounds.Count == 0 && song == null) return;
            AliveSoundsBegin(sounds);
            _aliveCancel = new CancellationTokenSource(); _aliveResults = null; _aliveBikeResults = null; _aliveGearResults = null; _aliveMusicResult = null; _aliveError = null; _aliveImporting = true; _aliveProgress = null;
            _aliveOrder.Clear(); _aliveOrder.AddRange(chosen.Select(c => c.level));
            var token = _aliveCancel.Token;
            _aliveStatus = "Starting…";
            Debug.Log("[OpenReflex] Alive import: " + string.Join(", ", chosen.Select(c => c.level)) + " -> " + content);
            _aliveTask = Task.Run(() =>
            {
                _aliveResults = chosen.Count == 0 ? new List<AliveLevelResult>() : AliveImporter.Import(chosen, content, new AliveImportOptions(), p => Interlocked.Exchange(ref _aliveProgress, p), token);
                if (chosen.Count > 0) PackAliveLevels(content, token);   // (bikes / rider gear alone: no level was written)
                if (bikes.Count > 0 && !token.IsCancellationRequested)
                {
                    var found = _aliveFound; int n = 0;
                    _aliveBikeResults = AliveBikes.Import(bikes, found, content, s => { _aliveStatus = s; Interlocked.Exchange(ref _aliveProgress, new AliveProgress { level = BikeKey, stage = s, done = n++, total = bikes.Count }); }, token);
                    foreach (var r in _aliveBikeResults) Debug.Log("[OpenReflex] Alive bike " + (r.ok ? "OK " : "FAILED ") + r.bike.Label + (r.ok ? $" -> {r.folder} ({r.surfaces} surfaces, {r.maps} maps, {r.bytes >> 10} KB, {r.seconds:0.0} s)"
                        + (r.partsFolder != null ? $" + its kits and parts: {r.partSlots} types, {r.partOptions} options, {r.partBytes >> 10} KB" : r.partsError != null ? " (its kits and parts FAILED: " + r.partsError + ")" : " (no other kit or part in Alive)")
                          + (r.hrFolder != null ? $" + its high-res garage model: {r.hrVertices} vertices, {r.hrBytes >> 20} MB" : r.hrError != null ? " (its high-res model FAILED: " + r.hrError + ")" : " (no high-res model in Alive)") : ": " + r.error));
                    BikeMods.Refresh();
                }
                if (gear.Count > 0 && !token.IsCancellationRequested)
                {   // the rider gear (Content/AliveImport/AliveGear.cs): one folder per gear model in <content>/AliveGear
                    var found = _aliveFound; int n = 0;
                    _aliveGearResults = AliveBikes.ImportGear(gear, found, content, s => { _aliveStatus = s; Interlocked.Exchange(ref _aliveProgress, new AliveProgress { level = GearKey, stage = s, done = n++, total = gear.Count }); }, token, found.gear);
                    Debug.Log($"[OpenReflex] Alive rider gear: {_aliveGearResults.Count(r => r.ok)} of {gear.Count} models, {_aliveGearResults.Sum(r => r.pieces)} pieces, {_aliveGearResults.Sum(r => r.bytes) >> 20} MB"
                        + string.Concat(_aliveGearResults.Where(r => !r.ok).Take(8).Select(r => "; FAILED " + r.model.Title + ": " + r.error)));
                    RxGear.Refresh();
                }
                AliveSoundsImport(sounds, content, soundFfmpeg, token);   // (Alive's engine sound sets: Content/AliveImport/AliveEngineSounds.cs)
                // the soundtrack last (about a minute: every song decoded and encoded by ffmpeg)
                if (song != null && !token.IsCancellationRequested)
                {
                    Debug.Log("[OpenReflex] Alive import: the soundtrack -> " + Path.Combine(music, ReflexSongLibrary.Alive.folder) + " (ffmpeg " + ffmpeg + ")");
                    _aliveMusicResult = AliveMusic.Import(song, music, ffmpeg, ReflexAssetImport.FreeBytes, s => Debug.Log("[OpenReflex] " + s),
                        (s, f) => Interlocked.Exchange(ref _aliveProgress, new AliveProgress { level = AliveMusicKey, stage = s, done = (int)(f * 1000), total = 1000 }), token);
                    Debug.Log("[OpenReflex] Alive import " + _aliveMusicResult);
                    AliveMusic.RefreshStatus(song, music);   // (hashes the songs: here, not on the main thread)
                }
                else if (song != null) _aliveMusicResult = new ReflexGameMusicResult { state = ReflexGameMusicState.Cancelled };   // (cancelled before its turn: the results say so)
            }, token);
            ShowAliveProgress();
        }

        /// A packed library stays packed: the importer writes the new levels loose (and retires a level's old archive), so they are packed here
        /// as the game import packs (ReflexDataPack: only the loose levels; the rest of the library is already packed) instead of the startup
        /// prompt asking at the next start. A library that was never packed (or REFLEX_IMPORT_LOOSE=1) keeps them loose. A pack that stops
        /// leaves the rest loose and working: GAME LIBRARY > PACK GAME FILES finishes it.
        void PackAliveLevels(string content, CancellationToken token)
        {
            if (_aliveResults == null || !_aliveResults.Any(r => r.ok) || token.IsCancellationRequested || ReflexDataPack.Running) return;
            if (!ReflexAssetImport.PackOnImport(null)) return;
            bool packed;
            try { packed = File.Exists(Path.Combine(content, "Assets.rxdata")) || Directory.Exists(Path.Combine(content, "Tracks")) && Directory.EnumerateFiles(Path.Combine(content, "Tracks"), "*.rxtrack").Any(); }
            catch (Exception) { packed = false; }
            if (!packed || !ReflexDataPack.Worth(content)) return;
            string level = _aliveResults.Last(r => r.ok).level;
            Interlocked.Exchange(ref _aliveProgress, new AliveProgress { level = level, stage = "Packing" });
            try
            {
                var r = ReflexDataPack.PackNow(content, pp => Interlocked.Exchange(ref _aliveProgress, new AliveProgress { level = level, stage = "Packing", done = (int)(pp.done >> 20), total = (int)Math.Max(1, pp.total >> 20) }), token);
                Debug.Log($"[pack] after the Alive import: {r.levels} levels, {r.files} files: {r.looseBytes >> 20} MB loose -> {r.packedBytes >> 20} MB packed in {r.seconds:0.0} s");
            }
            catch (OperationCanceledException) { Debug.Log("[pack] after the Alive import: stopped; what was packed stays packed, the rest loose"); }
            catch (Exception e) { Debug.LogWarning("[pack] after the Alive import: stopped, the rest stays loose (GAME LIBRARY > PACK GAME FILES): " + e); }
            finally { RxFiles.Reset(); }
        }

        /// The running scan / import (CANCEL).
        void ShowAliveProgress()
        {
            Clear(Screen.Install, "import");
            var box = OuUi.Div("mxa-panel mxa-install", _layer);
            box.name = "ou-alive-progress";
            OuUi.Text(_aliveImporting ? "IMPORTING ALIVE CONTENT" : "FINDING ALIVE LEVELS AND BIKES", "mxa-screen-title", box);
            OuUi.Text(_aliveImporting ? "Converting and installing into " + AliveContent() + ". Your source files are only read." : "Checking your sources (downloads: every hash of the package).", "mxa-install-text", box).style.whiteSpace = WhiteSpace.Normal;
            _aliveStatusLabel = OuUi.Text(_aliveStatus, "mxa-install-status", box); _aliveStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            var bar = OuUi.Div("ou-progress", box);
            _aliveFill = OuUi.Div("ou-progress-fill", bar); OuTheme.Backdrop(_aliveFill);
            _aliveFill.style.width = Length.Percent(0);
            var cancel = OuUi.Button("CANCEL", "mxa-secondary", () => _aliveCancel?.Cancel(), box);
            cancel.name = "ou-alive-cancel";
            _nav.DefaultItem = () => cancel; _nav.OnCancel = () => { _aliveCancel?.Cancel(); return true; };
            Hints((OuUi.KeySubmit, "SELECT"), (OuUi.KeyCancel, "CANCEL"));
            _nav.Focus(cancel);
        }

        /// Called every frame (UpdateBuild): the worker's progress, then its result screen.
        void UpdateAliveImport()
        {
            if (_aliveTask == null) return;
            bool onScreen = Current == Screen.Install && _layer.Q("ou-alive-progress") != null;
            if (!_aliveTask.IsCompleted)
            {
                if (!onScreen || _aliveStatusLabel == null) return;
                var p = Interlocked.CompareExchange(ref _aliveProgress, null, null);
                if (_aliveImporting && p != null)
                {
                    if (AliveSoundsProgress(p)) return;
                    var lv = _aliveFound?.levels.FirstOrDefault(l => l.level == p.level);
                    if (p.level == AliveMusicKey)
                    {
                        _aliveStatusLabel.text = "SOUNDTRACK  ·  " + (p.stage ?? "").ToUpperInvariant() + $"  {p.Fraction * 100:0}%";
                        _aliveFill.style.width = Length.Percent((float)(100.0 * p.Fraction));
                        return;
                    }
                    if (p.level == BikeKey)
                    {
                        _aliveStatusLabel.text = p.stage.ToUpperInvariant();
                        _aliveFill.style.width = Length.Percent((float)(100.0 * p.done / Math.Max(1, p.total)));
                        return;
                    }
                    if (p.stage == "Packing")
                    {   // (PackAliveLevels: the whole bar again, for the new levels' packing)
                        _aliveStatusLabel.text = "PACKING THE NEW LEVELS" + (p.total > 1 ? $"  ·  {p.Fraction * 100:0}%" : "");
                        _aliveFill.style.width = Length.Percent((float)(100.0 * p.Fraction));
                        return;
                    }
                    _aliveStatusLabel.text = (lv?.Label ?? p.level).ToUpperInvariant() + "  ·  " + p.stage.ToUpperInvariant() + (p.total > 1 ? $"  {p.Fraction * 100:0}%" : "");
                    int i = Math.Max(0, _aliveOrder.IndexOf(p.level));
                    double stageWeight = p.stage == "Converting" ? 0.85 * p.Fraction : p.stage == "Reading" ? 0 : p.stage == "Installing" ? 0.88 : p.stage == "Publishing" ? 0.9 + 0.1 * p.Fraction : 1;
                    _aliveFill.style.width = Length.Percent((float)(100.0 * (i + stageWeight) / Math.Max(1, _aliveOrder.Count)));
                }
                else _aliveStatusLabel.text = _aliveStatus;
                return;
            }
            var t = _aliveTask; _aliveTask = null; _aliveCancel?.Dispose(); _aliveCancel = null;
            if (t.IsFaulted) _aliveError = t.Exception?.GetBaseException();
            bool cancelled = t.IsCanceled || _aliveError is OperationCanceledException || (_aliveResults?.Any(r => r.error == "Cancelled") ?? false)
                             || (_aliveBikeResults?.Any(r => r.error == "Cancelled") ?? false) || AliveSoundsCancelled()
                             || _aliveMusicResult?.state == ReflexGameMusicState.Cancelled;
            if (_aliveImporting)
            {
                // the new levels: the registry, the event list and the shell's content read again (the hub's MX vs ATV Alive entry)
                AliveLevels.Reset();
                if (_aliveResults != null && _aliveResults.Any(r => r.ok)) { ReflexEvents.Refresh(); OpenContent.Reset(); }
                if (_aliveBikeResults != null && _aliveBikeResults.Any(r => r.ok)) BikeMods.Refresh();   // (the garage's MOD cards)
                if (_aliveMusicResult != null && _aliveMusicResult.Ok) { _aliveMusicChosen = false; try { ReflexCustomMusic.Rescan(); } catch (Exception e) { Debug.LogWarning("[OpenReflex] Alive import, music: " + e.Message); } }
                AliveSoundsFinished();
                if (_aliveError != null && !cancelled) Debug.LogException(_aliveError);
                foreach (var r in _aliveResults ?? new List<AliveLevelResult>()) Debug.Log("[OpenReflex] Alive import " + (r.ok ? "OK " : "FAILED ") + r);
                ShowAliveResults(cancelled);
            }
            else
            {
                if (_aliveError != null && !cancelled) Debug.LogWarning("[OpenReflex] Alive sources: " + _aliveError.Message);
                if (cancelled || _aliveFound == null) { _aliveNotice = cancelled ? "Cancelled." : "These sources could not be read: " + _aliveError?.Message; ShowAliveSources(); return; }
                if (_aliveFound.picturesAdded.Count > 0)
                {
                    Debug.Log("[OpenReflex] Alive pictures added: " + string.Join(", ", _aliveFound.picturesAdded));
                    AliveLevels.Reset();
                }
                AliveDefaultChoice();
                AliveSoundsPick();   // (the engine sound sets not installed yet)
                // the soundtrack: chosen unless every song is in the music folder already
                _aliveMusicChosen = _aliveMusic != null && _aliveMusic.Ok && _aliveMusic.status.inTheWay == null && !_aliveMusic.Complete;
                Debug.Log("[OpenReflex] Alive soundtrack: " + (_aliveMusic == null ? "not found" : (_aliveMusic.problem ?? _aliveMusic.status.inTheWay ?? "ready") + (_aliveMusic.status.imported != null ? $" (imported: {_aliveMusic.status.present} of {AliveMusic.Songs.Length} songs)" : "")));
                ShowAliveLevels();
            }
        }

        // --------------------------------------------------------------------------------------------- levels / results

        /// What is ticked when the list opens: what is new here, and what an older version imported (a vehicle without its high-res garage
        /// model and colours). What is installed and up to date is left alone (ticking it imports it again).
        void AliveDefaultChoice()
        {
            _aliveChosen.Clear();
            var found = _aliveFound; if (found == null) return;
            string content = AliveContent();
            foreach (var l in found.levels) if (l.recognized && l.kind.StartsWith("Free Ride", StringComparison.Ordinal) && !(content != null && AliveImporter.Installed(content, l.level))) _aliveChosen.Add(l.level);
            foreach (var b in found.bikes) if (b.Importable && (content == null || AliveBikes.ImportState(content, b) != 1)) _aliveChosen.Add(BikeKey + b.aliveId);
            if (found.gear.Count > 0 && (content == null || found.gear.Any(g => !AliveBikes.GearInstalled(content, g)))) _aliveChosen.Add(GearKey);
        }

        void ShowAliveLevels()
        {
            Clear(Screen.Install, "import");
            var box = OuUi.Div("mxa-panel mxa-install mxa-library", _layer);
            box.name = "ou-alive-levels";
            OuUi.Text("IMPORT MX vs ATV ALIVE  ·  2 OF 2: CHOOSE", "mxa-screen-title", box);
            string content = AliveContent(), musicFolder = MusicFolderOrNull(), ffmpeg = FfmpegBinkSource.Relocate();
            var found = _aliveFound;
            int gearPieces = found.gear.Sum(g => g.pieces.Count);
            bool nothing = found.levels.Count == 0 && found.bikes.Count == 0 && found.gear.Count == 0 && _aliveMusic == null;
            OuUi.Text(nothing ? "No MX vs ATV Alive content was found in these files. Go back and add the game disc (.iso) or the 5451087B folder."
                      : $"FOUND  ·  {found.bikes.Count} {(found.bikes.Count == 1 ? "VEHICLE" : "VEHICLES")}  ·  {gearPieces} RIDER GEAR PIECES  ·  {found.levels.Count} {(found.levels.Count == 1 ? "LEVEL" : "LEVELS")}" + (_aliveMusic != null ? "  ·  SOUNDTRACK" : ""),
                      nothing ? "mxa-error" : "mxa-track-name", box).style.whiteSpace = WhiteSpace.Normal;
            if (!nothing)
                OuUi.Text("What is ticked is new here or was imported by an older version. Vehicles get an ALIVE card in the garage, rider gear is listed in GARAGE > RIDER, levels become Free Ride maps under MX vs ATV Alive.",
                          "mxa-install-text", box).style.whiteSpace = WhiteSpace.Normal;
            // a source that was not added: say what it would bring, and offer the way back
            bool hasDisc = _aliveSourcePaths.Any(p => AliveSourceKind(p) == "disc"), hasDownloads = _aliveSourcePaths.Any(p => AliveSourceKind(p) == "downloads");
            VisualElement missingButton = null;
            if (!hasDisc || !hasDownloads)
            {
                var banner = OuUi.Div("ou-cache-row", box); banner.name = "ou-alive-missing"; banner.style.flexShrink = 0;
                var bc = OuUi.Div("ou-cache-copy", banner);
                OuUi.Text(!hasDownloads ? "ONLY THE GAME DISC WAS ADDED" : "ONLY DOWNLOADED CONTENT WAS ADDED", "mxa-track-name ou-cache-name", bc);
                OuUi.Text(!hasDownloads ? "The Honda, Yamaha, Suzuki and KTM vehicles, the 50cc, the MX250 FMX and the extra levels and rider gear are not on the disc: they are in your downloaded content (the 5451087B folder). Add it to see them here."
                                        : "Alive's own vehicles (the Blaze and Takashi bikes and ATVs, the 650, the ATV 1000), its levels and most of its rider gear are on the game disc (.iso). Add it to see them here.",
                          "ou-card-sub ou-cache-sub", bc).style.whiteSpace = WhiteSpace.Normal;
                missingButton = OuUi.Button(!hasDownloads ? "ADD DOWNLOADED CONTENT" : "ADD THE GAME DISC", "mxa-secondary", ShowAliveSources, banner);
                missingButton.name = "ou-alive-add-missing"; missingButton.style.flexShrink = 0; missingButton.style.alignSelf = Align.Center;
            }
            OuUi.Text("Converted on this computer from your own files, as new files in " + (content ?? "the client's content folder") + ".", "mxa-note", box).style.whiteSpace = WhiteSpace.Normal;
            foreach (var w in found.warnings.Take(6)) OuUi.Text(w, "mxa-note", box).style.whiteSpace = WhiteSpace.Normal;
            if (found.picturesAdded.Count > 0)
                OuUi.Text($"Menu pictures added for {found.picturesAdded.Count} {(found.picturesAdded.Count == 1 ? "level" : "levels")} imported before (no new import needed).", "mxa-note", box).style.whiteSpace = WhiteSpace.Normal;
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("mxa-library-list"); box.Add(scroll);
            var buttons = OuUi.Div("mxa-install-buttons", box);
            VisualElement import = null;
            void Count()
            {
                int n = found.levels.Count(l => l.recognized && _aliveChosen.Contains(l.level)), nb = found.bikes.Count(b => b.Importable && _aliveChosen.Contains(BikeKey + b.aliveId));
                var parts = new List<string>();
                if (n > 0) parts.Add($"{n} {(n == 1 ? "LEVEL" : "LEVELS")}");
                if (nb > 0) parts.Add($"{nb} {(nb == 1 ? "VEHICLE" : "VEHICLES")}");
                bool gearChosen = found.gear.Count > 0 && _aliveChosen.Contains(GearKey);
                bool m = _aliveMusicChosen && AliveMusicBlocked(musicFolder, ffmpeg) == null;
                if (content == null) { n = 0; nb = 0; gearChosen = false; parts.Clear(); }   // (no content folder: the soundtrack alone can be imported)
                if (gearChosen) parts.Add("RIDER GEAR");
                int ns = content == null ? 0 : AliveSoundsCount(content, parts);
                if (m) parts.Add("SOUNDTRACK");
                var lbl = import?.Q<Label>(); if (lbl != null) lbl.text = parts.Count > 0 ? "IMPORT " + string.Join(" + ", parts) : "NOTHING TICKED";
                import?.SetEnabled(n + nb + ns > 0 || gearChosen || m);   // (anything chosen: rider gear, bikes or the soundtrack alone import without a level)
            }
            VisualElement first = null;
            if (_aliveMusic != null)
            {   // the soundtrack: one row, into the music pool
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                row.name = "ou-alive-music";
                var c = OuUi.Div("ou-cache-copy", row);
                var title = OuUi.Text("", "mxa-track-name ou-cache-name", c);
                string blocked = AliveMusicBlocked(musicFolder, ffmpeg);
                var mu = _aliveMusic; int all = AliveMusic.Songs.Length;
                string state = mu.Complete ? $"IMPORTED (Mods/Music/{mu.status.imported})  ·  CHOOSE IT TO CHECK AND IMPORT IT AGAIN"
                             : mu.status.imported != null ? $"{mu.status.present} OF {all} SONGS AS IMPORTED IN Mods/Music/{mu.status.imported}  ·  CHOOSE IT TO PUT BACK THE MISSING ONES (SONGS YOU CHANGED STAY AS THEY ARE)"
                             : "READY";
                OuUi.Text(blocked ?? $"{all} SONGS ({AliveMusic.TotalSeconds / 60:0} MIN), CONVERTED ON THIS COMPUTER INTO THE MUSIC FOLDER (Mods/Music/{ReflexSongLibrary.Alive.folder}, ABOUT {OpenWorldCache.SizeText(AliveMusic.MusicBytes)}, A MINUTE OR SO): OPTIONS > AUDIO > SOUNDTRACK PLAYS THEM  ·  {state}",
                          blocked == null ? "ou-card-sub ou-cache-sub" : "mxa-error", c).style.whiteSpace = WhiteSpace.Normal;
                void Title() => title.text = (blocked == null ? (_aliveMusicChosen ? "☑  " : "☐  ") : "✕  ") + "SOUNDTRACK  ·  " + all + " SONGS";
                Title();
                if (blocked == null)
                {
                    OuUi.Item(row, () => { _aliveMusicChosen = !_aliveMusicChosen; Title(); Count(); });
                    first = row;
                }
            }
            // the bikes: each rides as the Reflex bike of its class and brand (its physics), drawn with Alive's own model
            if (found.bikes.Count > 0)
            {
                int[] states = found.bikes.Select(b => !b.Importable ? -1 : content == null ? 0 : AliveBikes.ImportState(content, b)).ToArray();
                OuUi.Text($"VEHICLES  ·  {found.bikes.Count} FOUND  ·  {states.Count(s => s == 0)} NEW  ·  {states.Count(s => s == 2)} NEED AN UPDATE  ·  {states.Count(s => s == 1)} UP TO DATE"
                          + (states.Any(s => s < 0) ? $"  ·  {states.Count(s => s < 0)} CANNOT BE IMPORTED" : ""), "mxa-dir-header", scroll.contentContainer).style.whiteSpace = WhiteSpace.Normal;
                foreach (var b in found.bikes)
                {
                    var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                    var c = OuUi.Div("ou-cache-copy", row);
                    var title = OuUi.Text("", "mxa-track-name ou-cache-name", c);
                    int state = !b.Importable || content == null ? 0 : AliveBikes.ImportState(content, b);
                    string rides = b.ownClass != null ? "ITS OWN ALIVE PHYSICS (" + (b.ownClass == "mx50" ? "50CC" : b.ownClass == "atv1000" ? "ATV 1000" : "650") + ")" : "RIDES AS THE " + b.ridesAsName.ToUpperInvariant();
                    string detail = b.Importable ? (state == 0 ? "NEW" : state == 2 ? "UPDATE  ·  ADDS ITS HIGH-RES GARAGE MODEL AND COLOURS" : "UP TO DATE  ·  TICK TO IMPORT IT AGAIN") + "  ·  " + rides + (string.IsNullOrEmpty(b.pack) ? "" : "  ·  " + b.pack)
                                  : b.problem;
                    OuUi.Text(detail, "ou-card-sub ou-cache-sub", c).style.whiteSpace = WhiteSpace.Normal;
                    string key = BikeKey + b.aliveId;
                    void Title() => title.text = (b.Importable ? (_aliveChosen.Contains(key) ? "☑  " : "☐  ") : "✕  ") + b.Label.ToUpperInvariant();
                    Title();
                    if (b.Importable)
                    {
                        OuUi.Item(row, () => { if (!_aliveChosen.Remove(key)) _aliveChosen.Add(key); Title(); Count(); });
                        row.name = "ou-alive-bike-" + b.aliveId;
                        if (first == null) first = row;
                    }
                }
            }
            // Alive's engine sound sets (OpenShell.AliveEngineSounds.cs), after the vehicles that play them
            var soundRow = AliveSoundsSection(scroll.contentContainer, content, Count); if (first == null) first = soundRow;
            // the rider gear: one row (every helmet, goggle, suit, boot and neck brace Alive's tables list)
            if (found.gear.Count > 0)
            {
                int pieces = found.gear.Sum(g => g.pieces.Count), have = content == null ? 0 : found.gear.Count(g => AliveBikes.GearInstalled(content, g));
                OuUi.Text($"RIDER GEAR  ·  {pieces} PIECES FOUND  ·  " + (have == 0 ? "NEW" : have >= found.gear.Count ? "INSTALLED" : $"{found.gear.Count - have} OF {found.gear.Count} MODELS NEW"), "mxa-dir-header", scroll.contentContainer).style.whiteSpace = WhiteSpace.Normal;
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                var c = OuUi.Div("ou-cache-copy", row);
                var title = OuUi.Text("", "mxa-track-name ou-cache-name", c);
                OuUi.Text(string.Join("  ·  ", found.gear.GroupBy(g => g.slot).Select(g => g.Sum(x => x.pieces.Count) + " " + (g.Key == "jersey" ? "SUITS (JERSEY + PANTS)" : g.Key == "neck_brace" ? "NECK BRACES" : g.Key == "boots" ? "BOOTS" : g.Key == "goggles" ? "GOGGLES" : "HELMETS")))
                    + (have >= found.gear.Count ? "  ·  TICK TO IMPORT IT AGAIN" : ""), "ou-card-sub ou-cache-sub", c).style.whiteSpace = WhiteSpace.Normal;
                void GearTitle() => title.text = (_aliveChosen.Contains(GearKey) ? "☑  " : "☐  ") + "RIDER GEAR  ·  " + pieces + " PIECES";
                GearTitle();
                OuUi.Item(row, () => { if (!_aliveChosen.Remove(GearKey)) _aliveChosen.Add(GearKey); GearTitle(); Count(); });
                row.name = "ou-alive-gear";
                if (first == null) first = row;
            }
            if (found.levels.Count > 0)
            {
                int lNew = found.levels.Count(l => l.recognized && !(content != null && AliveImporter.Installed(content, l.level))), lHave = found.levels.Count(l => l.recognized) - lNew;
                OuUi.Text($"LEVELS  ·  {found.levels.Count} FOUND  ·  {lNew} NEW  ·  {lHave} INSTALLED", "mxa-dir-header", scroll.contentContainer).style.whiteSpace = WhiteSpace.Normal;
            }
            foreach (var l in found.levels)
            {
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                var c = OuUi.Div("ou-cache-copy", row);
                var title = OuUi.Text("", "mxa-track-name ou-cache-name", c);
                bool installed = content != null && AliveImporter.Installed(content, l.level);
                string detail = (installed ? "INSTALLED" : "NEW") + "  ·  " + string.Join("  ·  ", new[] { l.kind, l.packDisplay ?? l.volume.displayName, OpenWorldCache.SizeText(l.bytes) }.Where(x => !string.IsNullOrEmpty(x)))
                                + (installed ? "  ·  TICK TO IMPORT IT AGAIN" : "");
                OuUi.Text(l.recognized ? detail : l.problem, l.recognized ? "ou-card-sub ou-cache-sub" : "mxa-error", c).style.whiteSpace = WhiteSpace.Normal;
                var level = l.level;
                void Title() => title.text = (l.recognized ? (_aliveChosen.Contains(level) ? "☑  " : "☐  ") : "✕  ") + l.Label.ToUpperInvariant();
                Title();
                if (l.recognized)
                {
                    OuUi.Item(row, () => { if (!_aliveChosen.Remove(level)) _aliveChosen.Add(level); Title(); Count(); });
                    row.name = "ou-alive-level-" + level;
                    if (first == null) first = row;
                }
            }
            import = OuUi.Button("IMPORT", "mxa-start", BeginAliveImport, buttons); import.name = "ou-alive-import";
            OuUi.Button("ALL", "mxa-secondary", () => { foreach (var l in found.levels.Where(x => x.recognized)) _aliveChosen.Add(l.level); foreach (var b in found.bikes.Where(x => x.Importable)) _aliveChosen.Add(BikeKey + b.aliveId); AliveSoundsAll(); if (found.gear.Count > 0) _aliveChosen.Add(GearKey); _aliveMusicChosen = AliveMusicBlocked(musicFolder, ffmpeg) == null; ShowAliveLevels(); }, buttons).name = "ou-alive-all";
            OuUi.Button("NONE", "mxa-secondary", () => { _aliveChosen.Clear(); _aliveMusicChosen = false; ShowAliveLevels(); }, buttons).name = "ou-alive-none";
            OuUi.Button("NEW & UPDATES", "mxa-secondary", () => { AliveDefaultChoice(); AliveSoundsPick(); _aliveMusicChosen = _aliveMusic != null && _aliveMusic.Ok && _aliveMusic.status.inTheWay == null && !_aliveMusic.Complete && AliveMusicBlocked(musicFolder, ffmpeg) == null; ShowAliveLevels(); }, buttons).name = "ou-alive-default";
            OuUi.Button("BACK", "mxa-secondary", ShowAliveSources, buttons);
            Count();
            _nav.OnCancel = () => { ShowAliveSources(); return true; };
            _nav.DefaultItem = () => import.enabledInHierarchy ? import : first ?? import;
            Hints((OuUi.KeySubmit, "CHOOSE"), (OuUi.KeyCancel, "BACK"));
            _nav.Focus(first ?? import);
        }

        void ShowAliveResults(bool cancelled)
        {
            Clear(Screen.Install, "import");
            var box = OuUi.Div("mxa-panel mxa-install mxa-library", _layer);
            box.name = "ou-alive-results";
            OuUi.Text("ALIVE IMPORT", "mxa-screen-title", box);
            var results = _aliveResults ?? new List<AliveLevelResult>();
            var bikeResults = _aliveBikeResults ?? new List<AliveBikeResult>();
            var gearResults = _aliveGearResults ?? new List<AliveGearResult>();
            int ok = results.Count(r => r.ok), okb = bikeResults.Count(r => r.ok);
            var mr = _aliveMusicResult;
            string head = cancelled ? "Import cancelled. What was in progress was removed; what finished before stays installed."
                        : _aliveError != null ? "Import stopped: " + _aliveError.Message
                        : results.Count == 0 && bikeResults.Count == 0 && gearResults.Count == 0 && mr != null ? (mr.Ok ? "The soundtrack is in your music." : "The soundtrack was not imported.")
                        : ok == results.Count && okb == bikeResults.Count ? string.Join(" ", new[] { results.Count > 0 ? $"{ok} {(ok == 1 ? "level" : "levels")} installed. They are listed under MX vs ATV Alive." : null,
                                                                                                    bikeResults.Count > 0 ? $"{okb} {(okb == 1 ? "bike" : "bikes")} installed: GARAGE > VEHICLE lists each as a card under the bike it rides as, its own kits and parts under CUSTOMIZE VEHICLE." : null,
                                                                                                    gearResults.Count > 0 ? $"{gearResults.Sum(r => r.pieces)} pieces of rider gear installed: GARAGE > RIDER lists them in each gear type." : null }.Where(x => x != null))
                        : $"{ok} of {results.Count} levels, {okb} of {bikeResults.Count} bikes installed.";
            if (head.Length > 0) OuUi.Text(head, cancelled || _aliveError != null || ok < results.Count || okb < bikeResults.Count || (mr != null && !mr.Ok) ? "mxa-error" : "mxa-install-text", box).style.whiteSpace = WhiteSpace.Normal;
            AliveSoundsHeadline(box);
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("mxa-library-list"); box.Add(scroll);
            if (mr != null)
            {
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                row.name = "ou-alive-result-music";
                var c = OuUi.Div("ou-cache-copy", row);
                OuUi.Text((mr.Ok ? "✓  " : "✕  ") + "SOUNDTRACK  ·  " + AliveMusic.Songs.Length + " SONGS", "mxa-track-name ou-cache-name", c);
                OuUi.Text(GameMusicDetail(mr, ReflexSongLibrary.SoundtrackAlive), mr.state == ReflexGameMusicState.Failed ? "mxa-error" : "ou-card-sub ou-cache-sub", c).style.whiteSpace = WhiteSpace.Normal;
            }
            foreach (var r in results)
            {
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                var c = OuUi.Div("ou-cache-copy", row);
                OuUi.Text((r.ok ? "✓  " : "✕  ") + r.displayName.ToUpperInvariant(), "mxa-track-name ou-cache-name", c);
                OuUi.Text(r.ok ? $"{r.resources} RESOURCES  ·  {r.starts} FREE RIDE STARTS  ·  {r.pending} NOT CONVERTED YET  ·  {OpenWorldCache.SizeText(r.bytes)}  ·  {r.seconds:0} S"
                               : r.error, r.ok ? "ou-card-sub ou-cache-sub" : "mxa-error", c).style.whiteSpace = WhiteSpace.Normal;
            }
            foreach (var r in bikeResults)
            {
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                var c = OuUi.Div("ou-cache-copy", row);
                OuUi.Text((r.ok ? "✓  " : "✕  ") + r.bike.Label.ToUpperInvariant(), "mxa-track-name ou-cache-name", c);
                OuUi.Text(r.ok ? $"BIKE  ·  {(r.bike.ownClass != null ? "ITS OWN ALIVE PHYSICS" : "RIDES AS THE " + r.bike.ridesAsName.ToUpperInvariant())}  ·  {r.surfaces} SURFACES  ·  {r.maps} MAPS  ·  {OpenWorldCache.SizeText(r.bytes)}"
                                 + (r.partsFolder != null ? $"  ·  ITS KITS AND PARTS: {r.partOptions} OPTIONS OF {r.partSlots} TYPES, {OpenWorldCache.SizeText(r.partBytes)}" : r.partsError != null ? "  ·  ITS KITS AND PARTS FAILED: " + r.partsError : "  ·  ALIVE HAS NO OTHER KIT OR PART FOR IT")
                                 + (r.hrFolder != null ? $"  ·  HIGH-RES GARAGE MODEL, {OpenWorldCache.SizeText(r.hrBytes)}" : r.hrError != null ? "  ·  HIGH-RES MODEL FAILED: " + r.hrError : "  ·  NO HIGH-RES MODEL IN ALIVE")
                               : r.error, r.ok ? "ou-card-sub ou-cache-sub" : "mxa-error", c).style.whiteSpace = WhiteSpace.Normal;
            }
            if (gearResults.Count > 0)
            {
                var row = OuUi.Div("ou-cache-row", scroll.contentContainer);
                var c = OuUi.Div("ou-cache-copy", row);
                int okg = gearResults.Count(r => r.ok);
                OuUi.Text((okg == gearResults.Count ? "✓  " : "✕  ") + "RIDER GEAR", "mxa-track-name ou-cache-name", c);
                OuUi.Text($"{gearResults.Sum(r => r.pieces)} PIECES OF {okg} MODELS  ·  " + string.Join("  ·  ", gearResults.Where(r => r.ok).GroupBy(r => r.model.slot).Select(g => g.Sum(x => x.pieces) + " " + g.Key.Replace('_', ' ').ToUpperInvariant()))
                    + "  ·  " + OpenWorldCache.SizeText(gearResults.Sum(r => r.bytes)) + (okg < gearResults.Count ? "  ·  NOT CONVERTED: " + string.Join(", ", gearResults.Where(r => !r.ok).Take(6).Select(r => r.model.Title + " (" + r.error + ")")) : ""),
                    okg == gearResults.Count ? "ou-card-sub ou-cache-sub" : "mxa-error", c).style.whiteSpace = WhiteSpace.Normal;
            }
            AliveSoundsResultRows(scroll.contentContainer);
            var buttons = OuUi.Div("mxa-install-buttons", box);
            var done = OuUi.Button("DONE", "mxa-start", LeaveAlive, buttons);
            done.name = "ou-alive-done";
            OuUi.Button("MORE LEVELS", "mxa-secondary", () => { if (_aliveFound != null) ShowAliveLevels(); else ShowAliveSources(); }, buttons);
            if (mr != null && mr.Ok) OuUi.Button("OPEN MUSIC FOLDER", "mxa-secondary", ReflexCustomMusic.OpenFolder, buttons).name = "ou-alive-open-music";
            _nav.OnCancel = () => { LeaveAlive(); return true; };
            _nav.DefaultItem = () => done;
            Hints((OuUi.KeySubmit, "SELECT"), (OuUi.KeyCancel, "BACK"));
            _nav.Focus(done);
        }
    }
}
