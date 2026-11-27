// GAME LIBRARY  ·  MX vs ATV ALIVE  ·  ALIVE ENGINE SOUNDS: the engine sound sets of Alive's vehicles (AliveImport/AliveEngineSounds,
// core/plan/ALIVE_ENGINE_SOUNDS.md section 3) on the import screens of OpenShell.AliveImport.cs, which calls in here:
//   list      a section after ALIVE BIKES, one row per set ("450F ENGINE · CRF450R, YZ450F, BLAZE 450F +3", its recordings, the size it
//             takes), chosen by default when not installed (_aliveChosen key "sound:<PROJECT>"); a row is blocked (✕ and why) when the
//             client's ffmpeg is missing or has no xma2 decoder (it decodes Alive's XMA recordings) or a folder of that name is in the way
//   import    after the bikes on the import task: every chosen set decoded and installed into <install>/AliveEngineSounds/<PROJECT>
//             (progress per recording), its result rows, then AliveEngineSets.Refresh() (main thread) so GARAGE > ENGINE SOUND lists them
// ffmpeg's path is resolved on the main thread (FfmpegBinkSource.Locate reads Application.dataPath); its decoder list is read by the scan
// task (a process start: never on the main thread).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Reflex.Port.Content;
using Reflex.Port.Content.AliveImport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.OpenFrontEnd
{
    public sealed partial class OpenShell
    {
        const string SoundKey = "sound:";
        List<AliveEngineSoundResult> _aliveSoundResults;
        string _aliveFfmpeg; int _aliveXma = -1;   // ffmpeg's path (null: none); its xma2 decoder: -1 not checked, 0 no, 1 yes
        readonly List<string> _aliveSoundOrder = new List<string>();

        /// BeginAliveScan (main thread): where ffmpeg is.
        void AliveSoundsLocate() { _aliveFfmpeg = FfmpegBinkSource.Relocate(); _aliveXma = -1; }   // (searched again while missing: one put in place since)

        /// The scan task (worker thread): whether that ffmpeg decodes XMA2.
        void AliveSoundsCheckDecoder()
        {
            string f = _aliveFfmpeg;
            try { _aliveXma = f != null && AliveFfmpeg.HasDecoder(f, "xma2") ? 1 : 0; }
            catch (Exception) { _aliveXma = 0; }
        }

        /// Why a set cannot be imported now (null: it can).
        string AliveSoundBlocked(AliveEngineSoundSource s, string content)
        {
            if (s.problem != null) return s.problem;
            if (_aliveFfmpeg == null) return "ffmpeg was not found: it decodes Alive's recordings. Put an ffmpeg build in the ffmpeg folder beside the game, or set REFLEX_FFMPEG and restart the game.";
            if (_aliveXma == 0) return "This ffmpeg (" + _aliveFfmpeg + ") has no xma2 decoder: Alive's recordings need one (the ffmpeg bundled with the Windows client has it).";
            return content != null ? AliveEngineSounds.InstallProblem(content, s) : null;
        }

        /// After a scan: the sets not installed yet are chosen.
        void AliveSoundsPick()
        {
            string content = AliveContent();
            foreach (var s in _aliveFound.engineSounds)
            {
                s.installed = content != null && AliveEngineSounds.Installed(content, s);
                if (!s.installed && AliveSoundBlocked(s, content) == null) _aliveChosen.Add(SoundKey + s.project);
            }
        }

        /// ALL: every set that can be imported.
        void AliveSoundsAll() { string content = AliveContent(); foreach (var s in _aliveFound?.engineSounds ?? new List<AliveEngineSoundSource>()) if (AliveSoundBlocked(s, content) == null) _aliveChosen.Add(SoundKey + s.project); }

        /// The chosen sets that can be imported.
        List<AliveEngineSoundSource> AliveSoundsChosen(string content) =>
            (_aliveFound?.engineSounds ?? new List<AliveEngineSoundSource>()).Where(s => _aliveChosen.Contains(SoundKey + s.project) && AliveSoundBlocked(s, content) == null).ToList();

        /// The IMPORT label's part ("7 SOUND SETS"; null: none chosen) and its count.
        int AliveSoundsCount(string content, List<string> parts)
        {
            int n = AliveSoundsChosen(content).Count;
            if (n > 0) parts.Add($"{n} {(n == 1 ? "SOUND SET" : "SOUND SETS")}");
            return n;
        }

        /// The list section after ALIVE BIKES; returns its first choosable row (null: none).
        VisualElement AliveSoundsSection(VisualElement list, string content, Action count)
        {
            var found = _aliveFound;
            if (found == null || found.engineSounds.Count == 0) return null;
            OuUi.Text($"ALIVE ENGINE SOUNDS  ·  {found.engineSounds.Count} {(found.engineSounds.Count == 1 ? "SOUND SET" : "SOUND SETS")}  ·  ALIVE'S OWN RECORDINGS AND STATE MACHINE: EACH IMPORTED ALIVE BIKE PLAYS ITS OWN, ANY BIKE CAN PICK ONE IN GARAGE > ENGINE SOUND", "mxa-dir-header", list);
            VisualElement first = null;
            foreach (var s in found.engineSounds)
            {
                var row = OuUi.Div("ou-cache-row", list);
                var c = OuUi.Div("ou-cache-copy", row);
                var title = OuUi.Text("", "mxa-track-name ou-cache-name", c);
                string blocked = AliveSoundBlocked(s, content);
                string detail = blocked ?? string.Join("  ·  ", new[] { $"{s.samples} RECORDINGS", $"{s.aliveIds.Count} {(s.aliveIds.Count == 1 ? "VEHICLE" : "VEHICLES")}", "ABOUT " + OpenWorldCache.SizeText(s.Bytes), s.source }.Where(x => !string.IsNullOrEmpty(x)))
                                + (s.installed ? "  ·  INSTALLED (IMPORT AGAIN REPLACES IT)" : "");
                OuUi.Text(detail, blocked != null ? "mxa-error" : "ou-card-sub ou-cache-sub", c).style.whiteSpace = WhiteSpace.Normal;
                string key = SoundKey + s.project;
                void Title() => title.text = (blocked == null ? (_aliveChosen.Contains(key) ? "☑  " : "☐  ") : "✕  ") + s.Label;
                Title();
                if (blocked != null) continue;
                OuUi.Item(row, () => { if (!_aliveChosen.Remove(key)) _aliveChosen.Add(key); Title(); count(); });
                row.name = "ou-alive-sound-" + s.project;
                if (first == null) first = row;
            }
            return first;
        }

        /// The import task (worker thread), after the bikes: the chosen sets converted and installed.
        void AliveSoundsImport(List<AliveEngineSoundSource> sets, string content, string ffmpeg, CancellationToken token)
        {
            if (sets.Count == 0 || token.IsCancellationRequested) return;
            var found = _aliveFound;
            _aliveSoundResults = AliveEngineSounds.Import(sets, found, content, ffmpeg, p => Interlocked.Exchange(ref _aliveProgress, new AliveProgress { level = SoundKey + p.level, stage = p.stage, done = p.done, total = p.total }), token);
            foreach (var r in _aliveSoundResults) Debug.Log("[OpenReflex] Alive engine sounds " + (r.ok ? "OK " : "FAILED ") + r.set.project + (r.ok ? $" -> {r.folder} ({r.samples} recordings, {r.syncs} sync points, {r.bytes >> 10} KB, {r.seconds:0.0} s)" : ": " + r.error));
        }

        /// BeginAliveImport (main thread): the order of the sets for the progress bar; the last results cleared.
        void AliveSoundsBegin(List<AliveEngineSoundSource> sets) { _aliveSoundResults = null; _aliveSoundOrder.Clear(); _aliveSoundOrder.AddRange(sets.Select(s => s.project)); }

        /// UpdateAliveImport: the progress of a set (true: p was one).
        bool AliveSoundsProgress(AliveProgress p)
        {
            if (p.level == null || !p.level.StartsWith(SoundKey, StringComparison.Ordinal)) return false;
            string project = p.level.Substring(SoundKey.Length);
            var s = _aliveFound?.engineSounds.FirstOrDefault(x => x.project == project);
            _aliveStatusLabel.text = ((s?.name ?? project) + " ENGINE SOUNDS  ·  " + (p.stage == "Decoding" ? $"DECODING RECORDING {Math.Min(p.done + 1, p.total)} OF {p.total}" : p.stage)).ToUpperInvariant();
            int i = Math.Max(0, _aliveSoundOrder.IndexOf(project));
            _aliveFill.style.width = Length.Percent((float)(100.0 * (i + p.Fraction) / Math.Max(1, _aliveSoundOrder.Count)));
            return true;
        }

        /// UpdateAliveImport when the import ended (main thread): the installed sets listed again.
        void AliveSoundsFinished() { if (_aliveSoundResults != null && _aliveSoundResults.Any(r => r.ok)) AliveEngineSets.Refresh(); }

        /// UpdateAliveImport: a set's import was cancelled (Import ends normally with a "Cancelled" result).
        bool AliveSoundsCancelled() => _aliveSoundResults?.Any(r => r.error == "Cancelled") ?? false;

        /// ShowAliveResults: the sets' headline line.
        void AliveSoundsHeadline(VisualElement box)
        {
            var results = _aliveSoundResults;
            if (results == null || results.Count == 0) return;
            int ok = results.Count(r => r.ok);
            OuUi.Text(ok == results.Count ? $"{ok} engine sound {(ok == 1 ? "set" : "sets")} installed: imported Alive bikes play their own, GARAGE > ENGINE SOUND lists them for every bike."
                                          : $"{ok} of {results.Count} engine sound sets installed.", ok == results.Count ? "mxa-install-text" : "mxa-error", box).style.whiteSpace = WhiteSpace.Normal;
        }

        /// ShowAliveResults: one row per set.
        void AliveSoundsResultRows(VisualElement list)
        {
            foreach (var r in _aliveSoundResults ?? new List<AliveEngineSoundResult>())
            {
                var row = OuUi.Div("ou-cache-row", list);
                var c = OuUi.Div("ou-cache-copy", row);
                OuUi.Text((r.ok ? "✓  " : "✕  ") + r.set.Label, "mxa-track-name ou-cache-name", c);
                OuUi.Text(r.ok ? $"ENGINE SOUNDS  ·  {r.samples} RECORDINGS  ·  {r.syncs} SYNC POINTS  ·  {OpenWorldCache.SizeText(r.bytes)}  ·  {r.seconds:0} S"
                               : r.error, r.ok ? "ou-card-sub ou-cache-sub" : "mxa-error", c).style.whiteSpace = WhiteSpace.Normal;
            }
        }
    }
}
