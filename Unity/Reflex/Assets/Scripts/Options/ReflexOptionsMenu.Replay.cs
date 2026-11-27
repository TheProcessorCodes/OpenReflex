// OPTIONS > OPEN REFLEX > REPLAY: REPLAY / PHOTO MODE's recording (Reflex.Port.Replay.ReflexReplay, w-replayphoto). REPLAY LENGTH: the
// seconds kept (30 s .. 5 min); a field that would not fit the memory budget at 60 frames a second keeps 30. REPLAY EFFECTS: the
// particles (dust, roost, splashes) are recorded too (their own ring, 32 .. 128 MB). Both apply from the next race start.
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Options
{
    public sealed partial class ReflexOptionsMenu
    {
        void BuildReplayOptions(VisualElement panel)
        {
            Section(panel, "REPLAY & PHOTO MODE");
            var choices = Reflex.Port.Replay.ReflexReplay.LengthChoices;
            Named(OptUi.Spinner("REPLAY LENGTH", () => Reflex.Port.Replay.ReflexReplay.LengthLabel(Reflex.Port.Replay.ReflexReplay.LengthSeconds), d =>
            {
                int i = Mathf.Max(0, System.Array.IndexOf(choices, Reflex.Port.Replay.ReflexReplay.LengthSeconds));
                Reflex.Port.Replay.ReflexReplay.LengthSeconds = choices[(i + d + choices.Length) % choices.Length];
            }, panel), "replay-length");
            Help(panel, "How much of every ride is kept for REPLAY and PHOTO MODE (the pause menu). A full 12-rider race takes about 70 MB a minute; a length that would not fit your memory records 30 frames a second.", When.NextEvent);
            Named(OptUi.Toggle("REPLAY EFFECTS", () => Reflex.Port.Replay.ReflexReplay.RecordEffects, v => Reflex.Port.Replay.ReflexReplay.RecordEffects = v, panel), "replay-effects");
            Help(panel, "Also records the dust, roost and splashes, the ruts as they were and the riders' mud. Up to about 300 MB more.", When.NextEvent);
            Named(OptUi.Toggle("REPLAY SOUND", () => Reflex.Port.Replay.ReflexReplay.RecordSound, v => Reflex.Port.Replay.ReflexReplay.RecordSound = v, panel), "replay-sound");
            Help(panel, "Plays the engines, landings, crashes, the gate and the crowd again in a replay, at 0.75x to 1.5x speed.", When.NextEvent);
            Named(OptUi.Toggle("CINEMATIC EFFECTS", () => Reflex.Port.Replay.ReflexReplay.CinematicEffects, v => Reflex.Port.Replay.ReflexReplay.CinematicEffects = v, panel), "replay-cinematic");
            Help(panel, "In replays only: depth of field that follows the rider, motion blur and subtle lens effects, set per camera. PHOTO MODE has its own settings; photos go to Pictures/Open Reflex.");
        }
    }
}
