// OPTIONS > AUDIO > MUSIC: SOUNDTRACK (ALL SONGS / REFLEX / ALIVE / UNTAMED SONGS ONLY: ReflexSongLibrary.Pool), YOUR MUSIC (OFF / MIX IN / ONLY), the music folder and the NOW PLAYING pop-up, after Open Unleashed's MxaOptionsMenu rows of the same names
// (Content/ReflexCustomMusic.cs, Hud/ReflexNowPlaying.cs).
using Reflex.Port.Content;
using UnityEngine.UIElements;

namespace Reflex.Port.Options
{
    public sealed partial class ReflexOptionsMenu
    {
        static string SongCount()
        {
            if (!ReflexCustomMusic.Scanned) return "SCANNING";
            int n = ReflexCustomMusic.CountOf(ReflexSong.SourceCustom);   // (your own songs: not the imported soundtracks)
            return n + (n == 1 ? " SONG" : " SONGS");
        }

        /// "  (23 SONGS)" for ALIVE / UNTAMED SONGS ONLY, "  (NOT IMPORTED: REFLEX'S PLAY)" when that game's songs are not there.
        static string SoundtrackCount()
        {
            int st = ReflexSettings.Soundtrack;
            if (st != ReflexSongLibrary.SoundtrackAlive && st != ReflexSongLibrary.SoundtrackUntamed || !ReflexCustomMusic.Scanned) return "";
            int n = ReflexCustomMusic.CountOf(st == ReflexSongLibrary.SoundtrackAlive ? ReflexSong.SourceAlive : ReflexSong.SourceUntamed);
            return n > 0 ? "  (" + n + " SONGS)" : "  (NOT IMPORTED: REFLEX'S PLAY)";
        }

        static void MusicRows(VisualElement panel)
        {
            ReflexCustomMusic.RescanIfOld(2f);   // (songs dropped in since the last look)
            Named(OptUi.Spinner("SOUNDTRACK", () => ReflexSongLibrary.SoundtrackNames[ReflexSettings.Soundtrack] + SoundtrackCount(), d =>
            {
                ReflexSettings.Soundtrack = (ReflexSettings.Soundtrack + d + 4) % 4;
                ReflexSettings.Save();
                ReflexCustomMusic.SettingsChanged();
            }, panel), "soundtrack");
            Help(panel, "Which games' songs play. ALL SONGS mixes Reflex's tracks with the MX vs ATV Alive and Untamed soundtracks imported in GAME LIBRARY. A soundtrack that is not imported plays Reflex's tracks instead.");
            Named(OptUi.Spinner("YOUR MUSIC", () => ReflexCustomMusic.ModeNames[ReflexSettings.CustomMusic] + "  (" + SongCount() + ")", d =>
            {
                ReflexSettings.CustomMusic = (ReflexSettings.CustomMusic + d + 3) % 3;
                ReflexSettings.Save();
                ReflexCustomMusic.SettingsChanged();
            }, panel), "custom-music");
            Help(panel, "Your own songs (.mp3, .ogg, .wav) from the Mods/Music folder. MIX IN plays them among the SOUNDTRACK's songs in the races. ONLY plays them instead of any soundtrack, in the menus too.");
            Named(OptUi.Button("OPEN MUSIC FOLDER", "mxa-link", ReflexCustomMusic.OpenFolder, panel), "music-folder");
            Help(panel, "Opens Mods/Music in the file browser. Title and artist come from a song's tags, or from a file name like \"Artist - Title.mp3\".", When.None);
            Named(OptUi.Toggle("NOW PLAYING POP-UP", () => ReflexSettings.NowPlayingPopup, v => { ReflexSettings.NowPlayingPopup = v; ReflexSettings.Save(); }, panel), "now-playing");
            Help(panel, "Shows a song's title and artist when it starts. N skips to the next song.");
        }
    }
}
