// MX vs ATV Unleashed (PC): the tracks of the player's own Unleashed imported as levels of the game library (core/plan/UNLEASHED_IMPORT.md).
// The facade of GAME LIBRARY > MX vs ATV UNLEASHED (OpenShell.UnleashedImport.cs); the converter is UnConvert (the other files of this folder).
//   sources    only read: the folder the player picks - the game folder (data/<id>.pak), an Open Unleashed install (Game Files/data/<id>.pak),
//              its Game Files folder or the data folder itself (DataFolder) - and the Reflex game library (the converter's donors)
//   scan       the catalogue's 64 locations (UnConvert.Catalog): which the folder has (<id>.pak) and which are installed already
//              (Database/UNL_<id>.dx9.database of the library, loose or in Tracks/UNL_<id>.rxtrack). Writes nothing
//   import     each chosen track, one after another on the caller's (a worker) thread: UnConvert.ConvertLevel -> its files written into a
//              staging library <library>/.unleashed-<id>/<level>/ laid out as the editor's UnleashedImportBuild.Pack lays it out
//              (Database/<level>.dx9.{database,level,scene}, Assets/unleashed/<level>/*, the extracted marker) -> ReflexDataPack.Pack
//              (every file read back and compared) -> Tracks/<level>.rxtrack of the staging library
//   publish    that archive moved into the library's Tracks folder (never Mods/Tracks) under a temporary name, then renamed: a level is
//              there complete or not at all, and a copy installed before is replaced only by a complete new one (its open handles
//              released first: RxFiles.Release). RxFiles.Reset() so the library's readers see it; the menu then reads its event list again
//   staging    a dot name on the library's own volume (the publish is a rename); 'lock' held open while the import runs. Removed when
//              the import ends, however it ends; one a crashed import left is removed by the next scan or import (CleanStaging)
//   failures   a track that fails or is cancelled leaves nothing behind and the others go on; what holds for every track (the library,
//              the source folder, disk space, a pack of the library in progress) is checked before anything is written and throws
//              UnleashedImportException
// Nothing here uses Unity.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Reflex.Port.RxCore;

namespace Reflex.Port.Content.UnleashedImport
{
    /// A precondition of the whole import failed (nothing was written): the message is for the player.
    public sealed class UnleashedImportException : Exception
    {
        public UnleashedImportException(string message) : base(message) { }
        public UnleashedImportException(string message, Exception inner) : base(message, inner) { }
    }

    public enum UnleashedImportState
    {
        /// Not reached (only inside a running import).
        Pending,
        /// Converted and installed.
        Imported,
        /// Not installed: error says why. Nothing of it was left behind.
        Failed,
        /// The import was cancelled before or while this track was converted. Nothing of it was left behind.
        Cancelled,
    }

    /// One location of the catalogue, as the picked folder has it.
    public sealed class UnleashedTrackSource
    {
        public UnCatalogEntry entry;
        /// The level it is installed as ("UNL_Nat04").
        public string level;
        /// Its pak in the data folder (null: not there) and that file's size.
        public string pak; public long bytes;
        /// Why it can't be imported (null: it can).
        public string problem;
        /// The library has this level already (loose, or Tracks/<level>.rxtrack): importing it again replaces the archive.
        public bool installed;
        public bool Ok => problem == null;
        public string Label => string.IsNullOrEmpty(entry.name) ? entry.id : entry.name;
        /// A freestyle arena or a machine challenge course: a Free Ride map (no lap race: ReflexEvents).
        public bool FreeRideOnly => UnleashedImporter.FreeRideOnly(entry.category);
    }

    /// The catalogue against the picked folder.
    public sealed class UnleashedSources
    {
        /// The folder the player picked and the data folder found in it (null: none).
        public string picked, dataDir;
        /// Every location of the catalogue, grouped (UnleashedImporter.GroupOf) and by name.
        public readonly List<UnleashedTrackSource> tracks = new List<UnleashedTrackSource>();
        public readonly List<string> warnings = new List<string>();
        public IEnumerable<UnleashedTrackSource> Ready => tracks.Where(t => t.Ok);
    }

    public sealed class UnleashedImportOptions
    {
        /// The free bytes of the volume a folder is on, null when unknown (default: ReflexAssetImport.FreeBytes). Unknown skips the check.
        public Func<string, long?> freeBytes;
        /// The importer's own notes (a staging folder removed, a track's failure with its stack).
        public Action<string> log;
    }

    /// Where a running import is.
    public sealed class UnleashedProgress
    {
        /// The track in progress (its level and menu name), its place among the chosen (0-based) and their number.
        public string level, name; public int index, count;
        /// What is being done ("Converting: terrain", "Packing", "Installing").
        public string stage;
        /// 0..1 of this track, and of the whole import.
        public float track, overall;
    }

    public sealed class UnleashedTrackResult
    {
        public UnCatalogEntry entry;
        public string level, displayName;
        public UnleashedImportState state;
        /// Why it failed (state Failed).
        public string error;
        /// Where it is, relative to the library ("Tracks/UNL_Nat04.rxtrack"; Imported).
        public string archive;
        /// It replaced the copy installed before.
        public bool replaced;
        /// The archive's size and the number of files in it.
        public long bytes; public int files;
        public double seconds;
        public List<string> warnings = new List<string>();
        public bool Ok => state == UnleashedImportState.Imported;
        public override string ToString() => level + ": " + state + (error != null ? " (" + error + ")" : "") + (archive != null ? " -> " + archive : "");
    }

    public sealed class UnleashedImportResult
    {
        /// One per chosen track, in the order given.
        public readonly List<UnleashedTrackResult> tracks = new List<UnleashedTrackResult>();
        /// The library (the full path); the tracks are in its Tracks folder.
        public string root;
        public bool cancelled;
        public double seconds;
        public int Imported => tracks.Count(t => t.Ok);
    }

    public static class UnleashedImporter
    {
        /// A converted level's name: this and the catalogue id ("UNL_Nat04": UnleashedLevels lists the library's UNL_ levels).
        public const string LevelPrefix = "UNL_";
        /// The staging folders in the library: ".unleashed-" + 8 hex digits (a dot name: no reader or packer of the library takes it).
        public const string StagingPrefix = ".unleashed-";
        /// An archive being moved into Tracks (not *.rxtrack: RxFiles never opens it).
        public const string TempSuffix = ".importing";
        /// What a running import needs free besides the tracks themselves: one track's loose files and its archive in the staging
        /// folder, and the 256 MB the packer asks to be free before it writes (ReflexDataPack).
        public const long StagingBytes = 512L << 20;
        /// What an installed track takes on the disk, estimated (the 80 archives of the Python converter: 3 to 17 MB each).
        public const long TrackBytes = 24L << 20;
        const string LockName = "lock";

        public static string LevelOf(UnCatalogEntry e) => LevelPrefix + e.id;
        /// A level's archive in the library, relative to it.
        public static string ArchiveOf(string level) => ReflexDataPack.TracksDir + "/" + level + ".rxtrack";

        /// A freestyle arena or a machine challenge course: a Free Ride map only (ReflexEvents gives such a level no lap race).
        public static bool FreeRideOnly(string category) =>
            string.Equals(category, "Freestyle", StringComparison.OrdinalIgnoreCase) || (category ?? "").StartsWith("machine challenge", StringComparison.OrdinalIgnoreCase);

        /// The list's groups, in their order: 0 national, 1 supercross, 2 supermoto, 3 freestyle, 4 machine challenge, 5 anything else.
        public static int GroupOf(string category)
        {
            string c = (category ?? "").ToLowerInvariant();
            return c == "national" ? 0 : c == "supercross" ? 1 : c == "supermoto" ? 2 : c == "freestyle" ? 3 : c.StartsWith("machine challenge", StringComparison.Ordinal) ? 4 : 5;
        }

        // ------------------------------------------------------------------------------------------------ the picked folder

        static HashSet<string> s_ids;
        static HashSet<string> Ids => s_ids ?? (s_ids = new HashSet<string>(UnConvert.Catalog.Select(e => e.id), StringComparer.OrdinalIgnoreCase));

        static string Child(string dir, string name)
        {
            if (dir == null || !Directory.Exists(dir)) return null;
            return Directory.GetDirectories(dir).FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
        }

        /// The folder holds a catalogue track's pak (names only).
        static bool HasTracks(string dir) => Directory.EnumerateFiles(dir, "*.pak").Any(f => Ids.Contains(Path.GetFileNameWithoutExtension(f)));

        /// The data folder (the one holding <id>.pak) of what the player picked: the folder itself, its data folder (the PC game's
        /// folder; an Open Unleashed install's Game Files folder) or its Game Files/data (an Open Unleashed install); a file picked
        /// (a pak, the game's exe) stands for its folder. null: no Unleashed track there, or it can't be read.
        public static string DataFolder(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return null;
            try
            {
                picked = Path.GetFullPath(picked);
                if (File.Exists(picked)) picked = Path.GetDirectoryName(picked);
                if (picked == null || !Directory.Exists(picked)) return null;
                foreach (string c in new[] { picked, Child(picked, "data"), Child(Child(picked, "Game Files"), "data") })
                    if (c != null && HasTracks(c)) return c;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { }
            return null;
        }

        // ------------------------------------------------------------------------------------------------ the library

        /// Why the Reflex game library can't take an Unleashed track (null: it can): it is not there, not an imported library, a link.
        public static string LibraryProblem(string reflexRoot)
        {
            if (string.IsNullOrEmpty(reflexRoot) || !Directory.Exists(reflexRoot)) return "Your MX vs ATV Reflex game files were not found. Import (or connect) the Reflex game first: GAME LIBRARY.";
            if (!File.Exists(Path.Combine(reflexRoot, ReflexAssetImport.ExtractedMarker)) || !Directory.Exists(Path.Combine(reflexRoot, "Database")))
                return "The Unleashed tracks are installed into your imported MX vs ATV Reflex game files; import the Reflex game first: GAME LIBRARY.";
            if (ReflexFs.IsLink(reflexRoot.TrimEnd('/', '\\'))) return "Your game files folder is a symbolic link to another folder; the importer writes nothing through a link: " + reflexRoot;
            string tracks = Path.Combine(reflexRoot, ReflexDataPack.TracksDir);
            if (File.Exists(tracks)) return ReflexDataPack.TracksDir + " in your game files is a file, not a folder; move or delete it to import the Unleashed tracks";
            if (Directory.Exists(tracks) && ReflexFs.IsLink(tracks)) return ReflexDataPack.TracksDir + " in your game files is a symbolic link to another folder; the importer writes nothing through a link";
            return null;
        }

        /// Whether the library has a level already (loose, or packed in Tracks/<level>.rxtrack). Any thread.
        public static bool Installed(string reflexRoot, string level)
        {
            if (string.IsNullOrEmpty(reflexRoot)) return false;
            try { return RxFiles.Exists(Path.Combine(reflexRoot, "Database", level + ".dx9.database")); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { return false; }
        }

        // ------------------------------------------------------------------------------------------------ the scan

        /// The catalogue against the picked folder: which tracks it has, which the library has already. reflexRoot: the game library
        /// (ReflexInstall.Dir: read on the main thread); null skips the installed test. Writes nothing. Throws what the file system
        /// throws when the data folder can't be read.
        public static UnleashedSources Scan(string picked, string reflexRoot, CancellationToken cancel)
        {
            var r = new UnleashedSources { picked = picked, dataDir = DataFolder(picked) };
            if (r.dataDir == null)
            {
                r.warnings.Add(ReflexPaths.LabelOf(picked ?? "") + ": no MX vs ATV Unleashed tracks found (choose the game folder, its Game Files folder or the data folder that holds Nat01.pak, SX01.pak ...)");
                return r;
            }
            var paks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(r.dataDir, "*.pak")) paks[Path.GetFileNameWithoutExtension(f)] = f;
            foreach (var e in UnConvert.Catalog)
            {
                cancel.ThrowIfCancellationRequested();
                var t = new UnleashedTrackSource { entry = e, level = LevelOf(e) };
                if (paks.TryGetValue(e.id, out string pak))
                {
                    t.pak = pak;
                    try { t.bytes = new FileInfo(pak).Length; if (t.bytes == 0) t.problem = Path.GetFileName(pak) + " is empty"; }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { t.problem = Path.GetFileName(pak) + " can't be read: " + ex.Message; }
                }
                else t.problem = e.id + ".pak is not in this folder";
                t.installed = Installed(reflexRoot, t.level);
                r.tracks.Add(t);
            }
            var sorted = r.tracks.OrderBy(t => GroupOf(t.entry.category)).ThenBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();
            r.tracks.Clear(); r.tracks.AddRange(sorted);
            return r;
        }

        // ------------------------------------------------------------------------------------------------ the import

        /// The bytes an import of n tracks needs free in the library's volume: the tracks and one track's staging.
        public static long NeededBytes(int tracks) => StagingBytes + tracks * TrackBytes;

        static string Mb(long bytes) => ((bytes + (1 << 20) - 1) >> 20).ToString(CultureInfo.InvariantCulture) + " MB";

        /// An exception as one sentence for the player (the first cause of a parallel loop's).
        static string Message(Exception e)
        {
            if (e is AggregateException a) e = a.Flatten().InnerExceptions.FirstOrDefault() ?? e;
            string m = (e.Message ?? "").Trim();
            if (m.Length == 0) m = e.GetType().Name;
            if (e is UnleashedImportException || e is IOException || e is UnauthorizedAccessException || e is InvalidDataException) return m;
            return e.GetType().Name + ": " + m;
        }

        /// Converts and installs the chosen tracks one after another into reflexRoot/Tracks/<level>.rxtrack (see the file header).
        ///   found       the scan the tracks come from (its data folder)
        ///   tracks      the chosen tracks (each Ok), in the order to import
        ///   reflexRoot  the game library (ReflexInstall.Dir: read on the main thread); read by the converter, written only in Tracks
        ///               and the staging folder
        ///   progress    called from this thread, the converter's and the packer's
        /// Returns every chosen track's result. Throws UnleashedImportException when nothing can be imported at all (nothing was written)
        /// and OperationCanceledException only when cancelled before the first track started; later a cancel ends in the result.
        public static UnleashedImportResult Import(UnleashedSources found, IEnumerable<UnleashedTrackSource> tracks, string reflexRoot,
                                                   UnleashedImportOptions options, Action<UnleashedProgress> progress, CancellationToken cancel)
        {
            options = options ?? new UnleashedImportOptions();
            var log = options.log ?? (s => { });
            var chosen = (tracks ?? new UnleashedTrackSource[0]).Where(t => t != null).ToList();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string problem = LibraryProblem(reflexRoot);
            if (problem != null) throw new UnleashedImportException(problem);
            try { reflexRoot = Path.GetFullPath(reflexRoot).TrimEnd('/', '\\'); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { throw new UnleashedImportException("Your game files folder can't be used: " + e.Message, e); }
            var result = new UnleashedImportResult { root = reflexRoot };
            foreach (var t in chosen)
                result.tracks.Add(new UnleashedTrackResult { entry = t.entry, level = t.level, displayName = t.Label, state = UnleashedImportState.Pending });
            if (chosen.Count == 0) return result;
            foreach (var t in chosen)
                if (!t.Ok) throw new UnleashedImportException(t.Label + " can't be imported: " + t.problem);
            if (chosen.Select(t => t.level).Distinct(StringComparer.OrdinalIgnoreCase).Count() != chosen.Count) throw new UnleashedImportException("A track was chosen twice.");
            if (found == null || string.IsNullOrEmpty(found.dataDir) || !Directory.Exists(found.dataDir)) throw new UnleashedImportException("Your MX vs ATV Unleashed folder is not there now; choose it again.");
            cancel.ThrowIfCancellationRequested();

            // ---- what holds for every track, before anything is written
            // (the packer is the library's own: one pack at a time, and PACK GAME FILES rewrites the library's archives)
            if (ReflexDataPack.Running) throw new UnleashedImportException("Your game files are being packed now; import the Unleashed tracks when that has finished.");
            try
            {
                RxFiles.EnsureCore?.Invoke();
                if (!RxArchiveNative.Available) throw new UnleashedImportException("This build's core library has no archive support, which the Unleashed tracks are installed with.");
            }
            catch (UnleashedImportException) { throw; }
            catch (Exception e) { throw new UnleashedImportException("The core library could not be loaded, which the Unleashed tracks are installed with: " + e.Message, e); }
            string stage, tracksDir = Path.Combine(reflexRoot, ReflexDataPack.TracksDir);
            try
            {
                CleanStaging(reflexRoot, log);
                stage = Path.Combine(reflexRoot, StagingPrefix + Guid.NewGuid().ToString("N").Substring(0, 8));
                long? free = (options.freeBytes ?? ReflexAssetImport.FreeBytes)(reflexRoot);
                long need = NeededBytes(chosen.Count);
                if (free != null && free.Value < need)
                    throw new UnleashedImportException("Not enough free disk space for " + chosen.Count + (chosen.Count == 1 ? " track" : " tracks") + " in " + reflexRoot + ": about " + Mb(need) +
                                                       " are needed (" + Mb(StagingBytes) + " of it only while importing), " + Mb(free.Value) + " are free. Free some space or choose fewer tracks.");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new UnleashedImportException("Your game files folder " + reflexRoot + " can't be written: " + e.Message, e);
            }

            FileStream lockFile = null;
            try
            {
                try
                {
                    Directory.CreateDirectory(stage);
                    lockFile = new FileStream(Path.Combine(stage, LockName), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                    Directory.CreateDirectory(tracksDir);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    throw new UnleashedImportException("Your game files folder " + reflexRoot + " can't be written: " + e.Message, e);
                }
                for (int i = 0; i < chosen.Count; i++)
                {
                    var res = result.tracks[i];
                    if (cancel.IsCancellationRequested) { res.state = UnleashedImportState.Cancelled; continue; }
                    var one = System.Diagnostics.Stopwatch.StartNew();
                    string work = Path.Combine(stage, res.level);
                    try
                    {
                        ImportTrack(found.dataDir, chosen[i], res, i, chosen.Count, reflexRoot, tracksDir, work, progress, cancel);
                    }
                    catch (Exception e)
                    {
                        // a cancel surfaces as OperationCanceledException, alone or inside the converter's or the packer's parallel loops
                        if (res.state == UnleashedImportState.Imported) log("[unleashed] " + res.level + " is installed; after that: " + e);   // (a progress callback that threw)
                        else if (cancel.IsCancellationRequested) { res.state = UnleashedImportState.Cancelled; res.error = null; res.archive = null; }
                        else { res.state = UnleashedImportState.Failed; res.error = Message(e); res.archive = null; log("[unleashed] " + res.level + " failed: " + e); }
                    }
                    finally
                    {
                        // nothing of a track that did not get in stays: its staging library (the packer's overlay of it closed first)
                        RxFiles.Release(work);
                        if (!ReflexAssetImport.DeleteTree(work)) log("[unleashed] " + work + " could not be removed now; it goes with the staging folder");
                    }
                    res.seconds = one.Elapsed.TotalSeconds;
                }
                result.cancelled = result.tracks.Any(t => t.state == UnleashedImportState.Cancelled);
            }
            finally
            {
                lockFile?.Dispose();
                RxFiles.Release(stage);
                if (!ReflexAssetImport.DeleteTree(stage)) log("[unleashed] the staging folder " + stage + " could not be removed; the next import removes it");
                RxFiles.Reset();   // (the library's archives as they are now, whatever the packer's own resets saw)
            }
            result.seconds = clock.Elapsed.TotalSeconds;
            return result;
        }

        static readonly string[] LevelFiles = { "database", "level", "scene" };
        static readonly Regex LevelRx = new Regex(@"^Database/([A-Za-z0-9_]+)\.dx9\.level$", RegexOptions.CultureInvariant);

        static void ImportTrack(string dataDir, UnleashedTrackSource src, UnleashedTrackResult res, int index, int count, string reflexRoot, string tracksDir, string work,
                                Action<UnleashedProgress> progress, CancellationToken cancel)
        {
            string level = src.level;
            void Report(string what, float f)
            {
                f = Math.Min(1f, Math.Max(0f, f));
                progress?.Invoke(new UnleashedProgress { level = level, name = src.Label, index = index, count = count, stage = what, track = f, overall = (index + f) / count });
            }
            Report("Reading your Unleashed files", 0f);

            // ---- convert (the source folder and the library are only read; everything comes back in memory)
            var files = UnConvert.ConvertLevel(dataDir, reflexRoot, src.entry, (text, f) => Report(StageText(text), 0.02f + 0.76f * f), cancel);
            cancel.ThrowIfCancellationRequested();
            if (files == null || files.Count == 0) throw new InvalidDataException("the converter wrote nothing");

            // ---- the staging library, laid out as UnleashedImportBuild.Pack lays it out; a path the packer would not take stops the track
            Report("Writing the level", 0.78f);
            var named = files.Keys.Select(k => LevelRx.Match(k.Replace('\\', '/'))).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
            if (named.Count != 1 || named[0] != level) throw new InvalidDataException("the converter wrote " + (named.Count == 0 ? "no level" : "the level " + string.Join(", ", named)) + ", not " + level);
            string own = "Assets/unleashed/" + level + "/";
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DeleteStaged(work);
            foreach (var kv in files)
            {
                cancel.ThrowIfCancellationRequested();
                string rel = kv.Key.Replace('\\', '/');
                bool database = LevelFiles.Any(x => rel == "Database/" + level + ".dx9." + x);
                // (a dot name is not packed: ReflexDataPack.Walk; ':' and '..' never leave the staging folder)
                bool asset = rel.StartsWith(own, StringComparison.Ordinal) && rel.Length > own.Length
                             && rel.Substring(own.Length).Split('/').All(p => p.Length > 0 && p[0] != '.' && p.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) < 0);
                if (!database && !asset) throw new InvalidDataException("the converter wrote a file the importer does not install: " + rel);
                if (kv.Value == null) throw new InvalidDataException("the converter wrote no bytes for " + rel);
                if (!written.Add(rel)) throw new InvalidDataException("two of the converter's files share the path " + rel + " (names differing only by letter case)");
                string path = Path.Combine(work, rel.Replace('/', Path.DirectorySeparatorChar));
                if (Path.DirectorySeparatorChar == '\\' && path.Length >= 260)
                    throw new UnleashedImportException("Your game files folder is too deeply nested for the track's files (" + reflexRoot + "); move it to a shorter path, such as " + ReflexPaths.ShortFolderExample(reflexRoot));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, kv.Value);
            }
            // what UnleashedLevels lists a level by, and the three files a level is
            foreach (string x in LevelFiles)
                if (!files.Keys.Any(k => k.Replace('\\', '/') == "Database/" + level + ".dx9." + x)) throw new InvalidDataException("the converter wrote no " + level + ".dx9." + x);
            foreach (string x in new[] { "level.json", "terrain.rxheight" })
                if (!written.Contains(own + x) || new FileInfo(Path.Combine(work, "Assets", "unleashed", level, x)).Length == 0) throw new InvalidDataException("the converter wrote no " + x);
            int n = files.Count;
            files = null;   // (tens of MB: not held while the level is packed)
            File.WriteAllText(Path.Combine(work, ReflexAssetImport.ExtractedMarker), "OPEN REFLEX extracted assets v1\n");

            // ---- pack: the library's own packer (compressed, every file read back and compared)
            Report("Packing", 0.80f);
            var packed = ReflexDataPack.Pack(work, p => Report("Packing", 0.80f + 0.16f * (p.total > 0 ? Math.Min(1f, (float)p.done / p.total) : 0f)), cancel, removeLoose: true).GetAwaiter().GetResult();
            string archive = Path.Combine(work, ReflexDataPack.TracksDir, level + ".rxtrack");
            if (!File.Exists(archive) || new FileInfo(archive).Length == 0) throw new InvalidDataException("the packer did not produce " + level + ".rxtrack");
            if (packed.archives.Count != 1) throw new InvalidDataException("the packer wrote " + packed.archives.Count + " archives for one level");   // (a self-contained level: no shared pool)
            cancel.ThrowIfCancellationRequested();

            // ---- publish: into the library's Tracks folder under a temporary name, then renamed
            Report("Installing", 0.97f);
            RxFiles.Release(work);
            long bytes = new FileInfo(archive).Length;
            bool replaced = Publish(archive, tracksDir, level);
            RxFiles.Reset();
            res.state = UnleashedImportState.Imported; res.archive = ArchiveOf(level); res.replaced = replaced; res.bytes = bytes; res.files = n;
            // loose files of the same level (a library converted by hand) are read before any archive: the import can't show through them
            if (File.Exists(Path.Combine(reflexRoot, "Database", level + ".dx9.database")))
                res.warnings.Add("loose files of " + level + " in your game files (Database/" + level + ".dx9.*) are read instead of the imported track");
            Report("Done", 1f);
        }

        /// The converter's step texts for the progress screen ("terrain" -> "Converting: terrain").
        static string StageText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Converting";
            return text.StartsWith("convert", StringComparison.OrdinalIgnoreCase) ? char.ToUpperInvariant(text[0]) + text.Substring(1) : "Converting: " + text;
        }

        static void DeleteStaged(string work)
        {
            RxFiles.Release(work);
            if (Directory.Exists(work) && !ReflexAssetImport.DeleteTree(work)) throw new IOException("the staging folder " + work + " can't be emptied");
        }

        static string InUse(string level) => ArchiveOf(level) + " is in use (was the track ridden since the game started?): restart the game and import it again";

        /// The staged archive into Tracks: moved in under a temporary name (the same volume: a rename), then put in place. A copy
        /// installed before stays until then and is replaced in one step where the file system can (File.Replace), else deleted and
        /// the new one renamed. A file another program holds for a moment (a virus scanner) is waited for. True: it replaced a copy.
        static bool Publish(string archive, string tracksDir, string level)
        {
            string dest = Path.Combine(tracksDir, level + ".rxtrack"), temp = dest + TempSuffix;
            if (File.Exists(temp)) File.Delete(temp);
            File.Move(archive, temp);
            try
            {
                if (!File.Exists(dest)) { File.Move(temp, dest); return false; }
                RxFiles.Release(dest);   // (the menu read its pictures from it; on Windows an open archive can't be replaced)
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        try { File.Replace(temp, dest, null); }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException || e is NotSupportedException)
                        {
                            if (File.Exists(dest)) File.Delete(dest);
                            File.Move(temp, dest);
                        }
                        return true;
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        if (attempt < 5) { Thread.Sleep(120 * (attempt + 1)); continue; }
                        throw new UnleashedImportException(InUse(level) + " (" + e.Message + ")", e);
                    }
                }
            }
            catch
            {
                // (the old copy is gone and the new one did not get its name: one more try, so the level is not lost)
                try { if (!File.Exists(dest) && File.Exists(temp)) File.Move(temp, dest); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                throw;
            }
        }

        // ------------------------------------------------------------------------------------------------ staging folders

        static readonly Regex StagingRx = new Regex("^" + Regex.Escape(StagingPrefix) + "[0-9a-f]{8}$", RegexOptions.CultureInvariant);

        /// The staging folders of imports that did not end (a crash, the power) and the archives they were moving into Tracks: removed.
        /// A staging folder whose lock is held belongs to an import that is running (another client on the same library) and stays,
        /// with every temporary archive.
        public static int CleanStaging(string reflexRoot, Action<string> log = null)
        {
            log = log ?? (s => { });
            int removed = 0; bool running = false;
            if (string.IsNullOrEmpty(reflexRoot) || !Directory.Exists(reflexRoot)) return 0;
            foreach (string dir in Directory.GetDirectories(reflexRoot))
            {
                if (!StagingRx.IsMatch(Path.GetFileName(dir)) || ReflexFs.IsLink(dir)) continue;
                bool free;
                try { using (new FileStream(Path.Combine(dir, LockName), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } free = true; }
                catch (FileNotFoundException) { free = true; }
                catch (DirectoryNotFoundException) { continue; }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { free = false; }
                if (!free) { running = true; log("[unleashed] " + dir + " belongs to an import that is running: left"); continue; }
                RxFiles.Release(dir);
                if (ReflexAssetImport.DeleteTree(dir)) { removed++; log("[unleashed] removed the staging folder an earlier import left: " + dir); }
            }
            string tracks = Path.Combine(reflexRoot, ReflexDataPack.TracksDir);
            if (!running && Directory.Exists(tracks) && !ReflexFs.IsLink(tracks))
                foreach (string f in Directory.GetFiles(tracks, LevelPrefix + "*.rxtrack" + TempSuffix))
                {
                    try { File.Delete(f); log("[unleashed] removed the unfinished " + Path.GetFileName(f)); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { log("[unleashed] " + f + ": " + e.Message); }
                }
            return removed;
        }
    }
}
