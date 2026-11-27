// What the MODERN HUD's model (ReflexHudModel.Fill) reads of one local rider. The first rider's source is the world runtime itself
// (OriginalWorldRuntime: the members' names are its own); SPLIT SCREEN (w-split) adds a second one, SplitHudSource below, fed from the
// second rider's bundle of the last completed frame (OriginalSimulation.View2: the core's getters under rx_session_set_view_player 1),
// so the second rider gets the same HUD from the same model and view (ReflexModernHud's second pair, in the bottom half).
using Reflex.Port.Content;
using Reflex.Port.OriginalWorld;
using Reflex.Port.RxCore;

namespace Reflex.Port.Hud
{
    public interface IReflexHudSource
    {
        /// The source still stands (a destroyed runtime is not: Unity's null).
        bool Alive { get; }
        ReflexRaceState Race { get; }
        ReflexHudState HudState { get; }
        bool HasHud { get; }
        bool IsWaypoint { get; }
        bool HasWaypoint { get; }
        RxWaypointState Waypoint { get; }
        ReflexBikeState Bike { get; }
        bool TryGetEngine(out float rpm, out float idleRpm, out float maxRpm);
        bool EngineDead { get; }
        ReflexDriverResult[] Drivers { get; }
        int DriverCount { get; }
        ReflexRoster Roster { get; }
        bool HasRaceFormat { get; }
        RxRaceFormatState RaceFormat { get; }
        bool HasRaceHud { get; }
        RxRaceHudState RaceHud { get; }
        bool HasChallenge { get; }
        RxChallengeState Challenge { get; }
        bool HasPowercross { get; }
        RxPowercrossHudState Powercross { get; }
        string EventIdentity { get; }
        /// The session is a PRACTICE (no countdown, the gate starts down: the HUD shows no GO); the event's, the same for both riders.
        bool IsPractice { get; }
        bool HasCompass { get; }
        RxCompassState Compass { get; }
        bool HasStuntHud { get; }
        RxStuntHudState StuntHud { get; }
        bool HasReflexHud { get; }
        RxReflexHudState ReflexHud { get; }
        /// The rider's camera (ReflexCameraPresets' key: a change shows the camera's name).
        int CameraMode { get; }
        /// The camera toast: a number that changes with this rider's view (ReflexCameraPresets.ViewKey) and the name it then shows.
        int CameraViewKey { get; }
        string CameraDisplayName { get; }
        /// The rider's seat slot in the standings; -1: the row the core flags local (the first rider).
        int LocalSlot { get; }
        /// The rider's name in the standings; null: the profile's persona (the first rider).
        string LocalName { get; }
        /// The rider plays on a pad whatever CONTROLLER GLYPHS says (the wreck prompt never names keys).
        bool PadOnly { get; }
    }

    /// SPLIT SCREEN (w-split): the second local rider's HUD source. Take copies the rider's bundle out of the completed frame (the frame
    /// objects are recycled: nothing of theirs is kept); the field (Drivers, Roster) and the event are the runtime's own.
    public sealed class SplitHudSource : IReflexHudSource
    {
        readonly OriginalWorldRuntime rt;
        ReflexBikeState bike = ReflexBikeState.Create();
        ReflexRaceState race; ReflexHudState hud; bool hasHud, hasWaypoint, hasRaceFormat, hasRaceHud, hasChallenge, hasPowercross, hasCompass, hasStuntHud, hasReflexHud;
        RxWaypointState waypoint; RxRaceFormatState raceFormat; RxRaceHudState raceHud; RxChallengeState challenge; RxPowercrossHudState powercross;
        RxCompassState compass; RxStuntHudState stuntHud; RxReflexHudState reflexHud;
        float engineRpm, engineIdleRpm, engineMaxRpm; bool hasEngine, engineDead;
        int slot = -1;

        public SplitHudSource(OriginalWorldRuntime runtime) { rt = runtime; }

        /// A second rider's frame was taken in this world (cleared with the world).
        public bool Valid { get; private set; }
        /// The second rider crossed the line (the standings row of its slot, read under its view).
        public bool Finished { get; private set; }

        public void Clear() { Valid = Finished = hasHud = hasWaypoint = hasRaceFormat = hasRaceHud = hasChallenge = hasPowercross = hasCompass = hasStuntHud = hasReflexHud = hasEngine = false; race = default; slot = -1; }

        /// From the last completed (unblended) frame, as the first rider's race / HUD structs are (OriginalWorldRuntime.CollectCompletedFrame).
        public void Take(OriginalSimulation.View2 v, RxVehicleAudioState va)
        {
            if (v == null || !v.valid) { Clear(); return; }
            Valid = true; Finished = v.finished; slot = v.slot;
            race = v.hasRace ? v.race : default; hud = v.hud; hasHud = v.hasHud;
            if (v.hasBike)
            {
                bike.gear = v.bike.gear; bike.speed = v.bike.speed;
                Copy(v.bike.renderOrientation, bike.renderOrientation); Copy(v.bike.renderPosition, bike.renderPosition);
            }
            waypoint = v.waypoint; hasWaypoint = v.hasWaypoint; raceFormat = v.raceFormat; hasRaceFormat = v.hasRaceFormat;
            raceHud = v.raceHud; raceHud.activityType = RxChampWeekend.BaseType(raceHud.activityType); hasRaceHud = v.hasRaceHud;   // (as the first rider's)
            challenge = v.challenge; hasChallenge = v.hasChallenge; powercross = v.powercross; hasPowercross = v.hasPowercross;
            compass = v.compass; hasCompass = v.hasCompass; stuntHud = v.stuntHud; hasStuntHud = v.hasStuntHud; reflexHud = v.reflexHud; hasReflexHud = v.hasReflexHud;
            // the engine: the rider of the second player's vehicle (OriginalWorldRuntime.ReadEngine's rule; rx_session_vehicle_audio lists every rider)
            hasEngine = false;
            if (v.hasVehicle && va != null && va.valid)
                for (int i = 0; i < va.riderCount; i++)
                {
                    var r = va.riders[i]; if (r.vehicle != v.vehicle.vehicle) continue;
                    if (r.maxRpm > 0f) { engineRpm = r.rpm; engineIdleRpm = r.idleRpm; engineMaxRpm = r.maxRpm; engineDead = r.wrecked || r.drowned; hasEngine = true; }
                    break;
                }
        }
        static void Copy(float[] from, float[] to) { if (from != null && to != null) System.Array.Copy(from, to, System.Math.Min(from.Length, to.Length)); }

        public bool Alive => rt && Valid;
        public ReflexRaceState Race => race;
        public ReflexHudState HudState => hud;
        public bool HasHud => hasHud;
        public bool IsWaypoint => hasWaypoint || rt.EventIdentity?.StartsWith("WaypointRace/", System.StringComparison.Ordinal) == true;
        public bool HasWaypoint => hasWaypoint;
        public RxWaypointState Waypoint => waypoint;
        public ReflexBikeState Bike => bike;
        public bool TryGetEngine(out float rpm, out float idleRpm, out float maxRpm) { rpm = engineRpm; idleRpm = engineIdleRpm; maxRpm = engineMaxRpm; return hasEngine; }
        public bool EngineDead => hasEngine && engineDead;
        public ReflexDriverResult[] Drivers => rt.Drivers;
        public int DriverCount => rt.DriverCount;
        public ReflexRoster Roster => rt.Roster;
        public bool HasRaceFormat => hasRaceFormat;
        public RxRaceFormatState RaceFormat => raceFormat;
        public bool HasRaceHud => hasRaceHud;
        public RxRaceHudState RaceHud => raceHud;
        public bool HasChallenge => hasChallenge;
        public RxChallengeState Challenge => challenge;
        public bool HasPowercross => hasPowercross;
        public RxPowercrossHudState Powercross => powercross;
        public string EventIdentity => rt.EventIdentity;
        public bool IsPractice => rt.IsPractice;
        public bool HasCompass => hasCompass;
        public RxCompassState Compass => compass;
        public bool HasStuntHud => hasStuntHud;
        public RxStuntHudState StuntHud => stuntHud;
        public bool HasReflexHud => hasReflexHud;
        public RxReflexHudState ReflexHud => reflexHud;
        /// The second rider's own camera selection (OriginalWorldRuntime's second view, ReflexPlayerView): its mode, and its toast's key and name.
        public int CameraMode => rt.SecondCameraMode;
        public int CameraViewKey => rt.SecondCameraKey;
        public string CameraDisplayName => rt.SecondCameraName;
        /// -2 (no row) while the slot is unknown: never -1, which would be the core's local flag, the first rider's.
        public int LocalSlot => slot >= 0 ? slot : -2;
        /// Always "Player 2", on both riders' boards (ReflexHudModel.Standings names the second rider's row so too).
        public string LocalName => ReflexHudModel.SecondPlayerName;
        public bool PadOnly => true;
    }
}
