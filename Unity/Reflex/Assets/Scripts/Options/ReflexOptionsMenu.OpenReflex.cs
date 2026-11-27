// OPTIONS > OPEN REFLEX: the settings of OPEN REFLEX itself that change how the game plays (ours, not the original game's).
//   RIDING   AUTO LEVEL and COUNTER-STEER SNAP (core rx_session_set_air_helpers, core/notes/bike.md "FREER AIR"): 0 .. 100 %, how much of the original's
//            two in-air helpers that turn the bike back toward its take-off angle the player's bike / ATV keeps (never the computer riders'): 100 % =
//            the original game (the default), 0 % = removed, after Open Unleashed's FREER AIR ROTATION, in between they work that much slower. They
//            replace the earlier FREER AIR toggle (a saved FREER AIR on reads as both at 0 %, its KEEP choices as 100 %). AIR LOOSENESS (w-freeair2,
//            core rx_session_set_free_air_looseness): 0 .. 100 %, the air damping while coasting, the stick lag and authority eased; off at 0 %.
//            LIP PULL (w-lippull, core rx_session_set_lip_pull): 0 .. 100 %, how much of the original's rear-wheel-only wheelie limiter is kept on a
//            steep face (it holds the bike down to a steep lip); 100 % = the original (the default). All apply at once, in a race too (the next step).
//            RIDER MOTION (a prototype; OriginalWorld/RiderLooseMotion.cs, ReflexSettings.RiderMotion / RiderMotionStrength): ORIGINAL (the default) /
//            LOOSE and its LOOSENESS 0 .. 100 %: secondary motion on the drawn riders, applied at once. Nothing of the ride changes.
//   RACE SETUP  ANY VEHICLE ON ANY TRACK (core/plan/ANY_VEHICLE.md; ReflexSettings.AnyVehicle, OpenSetup.AnyVehicle): ON by default.
//               PLAYERS TAB (w-split; ReflexSettings.PlayersTab): SHOW by default; the setup page's split-screen tab (OpenShell.Setup.cs).
//   LOADING  DISABLE COMPOUND (ReflexSettings.DisableCompound): OFF by default; ON: START EVENT loads behind the full-screen loading screen (no
//            test-ride compound while it loads, none kept warm in the menus), as WATCH GHOST does.
//   FREE RIDE  SPAWN MARKERS (w-spawnmarker, w-markerbtns; OriginalWorld/OriginalSpawnMarker.cs, core rx_session_spawn_at_instant): ON by default; hold Y / F to set, B / V to respawn.
//   FREE RIDE  NO INVISIBLE BORDERS (w-frnowalls, w-reyesborders; core rx_session_set_free_ride_borders, game/free_ride_borders.h): ON by default: no
//            reset zones, no reset triggers but deep water, no cannonball band (the FR maps' white-out boundary), the terrain's edge only as a fall net,
//            in Free Ride only. Collision is never left out (w-dfcollide: the DoverB "panel" w-frnowalls left out was the JumboTron's screen).
//   FREE RIDE  ALIVE LOOK (w-alivereflex experiment; Content/AliveLook.cs): REFLEX / ALIVE, how the MX vs ATV Alive tracks are drawn; read when a track loads.
//   PHYSICS STUDIO   PHYSICS STUDIO EVERYWHERE (w-physstudio2; RxPhysicsStudio.Everywhere): the player's config also in the career,
//            MotoSkills and the challenges (practice there).
using UnityEngine;
using UnityEngine.UIElements;

namespace Reflex.Port.Options
{
    public sealed partial class ReflexOptionsMenu
    {
        VisualElement BuildOpenReflex(VisualElement panel)
        {
            Section(panel, "RIDING");
            var first = Named(OptUi.Slider("AUTO LEVEL", () => ReflexSettings.AirAutoLevel, v =>
            {
                ReflexSettings.AirAutoLevel = v; ReflexSettings.Save(); ReflexClientLive.ApplyFreeAir();
            }, panel, 0f, 1f, 0.05f, v => Mathf.RoundToInt(v * 100f) + "%"), "air-auto-level");
            Help(panel, "How strongly the original's auto-level turns the bike back toward level once you let go of the sticks in the air. 100% is the original; 0% leaves the bike where you put it. Your vehicle only.");
            Named(OptUi.Slider("COUNTER-STEER SNAP", () => ReflexSettings.AirSnap, v =>
            {
                ReflexSettings.AirSnap = v; ReflexSettings.Save(); ReflexClientLive.ApplyFreeAir();
            }, panel, 0f, 1f, 0.05f, v => Mathf.RoundToInt(v * 100f) + "%"), "air-counter-steer-snap");
            Help(panel, "How hard the original snaps the bike back when you push the stick hard against a big whip. 100% is the original; 0% turns the snap off, so the counter-steer is yours.");
            Named(OptUi.Slider("AIR LOOSENESS", () => ReflexSettings.AirLooseness, v =>
            {
                ReflexSettings.AirLooseness = v; ReflexSettings.Save(); ReflexClientLive.ApplyFreeAir();
            }, panel, 0f, 1f, 0.05f, v => Mathf.RoundToInt(v * 100f) + "%"), "air-looseness");
            Help(panel, "How loose the bike feels in the air: higher lets a whip or spin carry on longer and answers the stick quicker. 0% is the original's air.");
            Named(OptUi.Slider("LIP PULL", () => ReflexSettings.LipPull, v =>
            {
                ReflexSettings.LipPull = v; ReflexSettings.Save(); ReflexClientLive.ApplyFreeAir();
            }, panel, 0f, 1f, 0.05f, v => Mathf.RoundToInt(v * 100f) + "%"), "lip-pull");
            Help(panel, "How hard the original's wheelie limiter holds the bike down to a steep lip. 100% is the original; lower leaves the lip with more rotation and pop. Flat-ground wheelies never change. Your vehicle only.");
            Named(OptUi.Toggle("RIDER MOTION", () => ReflexSettings.RiderMotion == 1, v =>
            {
                ReflexSettings.RiderMotion = v ? 1 : 0; ReflexSettings.Save();
            }, panel, "LOOSE", "ORIGINAL"), "rider-motion");
            Help(panel, "How the riders move on their bikes. ORIGINAL: exactly as the original game poses them. LOOSE: the body gives with the bike. It leans into braking and power, soaks up landings and keeps its head steadier. Looks only: the ride is the same.");
            Needs(Named(OptUi.Slider("LOOSENESS", () => ReflexSettings.RiderMotionStrength, v =>
            {
                ReflexSettings.RiderMotionStrength = v; ReflexSettings.Save();
            }, panel, 0f, 1f, 0.05f, v => Mathf.RoundToInt(v * 100f) + "%"), "rider-motion-strength"), () => ReflexSettings.RiderMotion == 1, "RIDER MOTION IS ORIGINAL");
            Help(panel, "How much the LOOSE rider moves. 100% is the full effect; lower is closer to the original.");
            Section(panel, "RACE SETUP");
            Named(OptUi.Toggle("ANY VEHICLE ON ANY TRACK", () => ReflexSettings.AnyVehicle, v =>
            {
                ReflexSettings.AnyVehicle = v; ReflexSettings.Save();
            }, panel), "any-vehicle");
            Help(panel, "ON: the race setup offers every vehicle and every class on every event (a truck at a Supercross, bikes against buggies). A choice the event never had keeps no records and posts no laps. OFF: the original's lists. Single player; the career and online play are not changed.");
            Named(OptUi.Toggle("PLAYERS TAB", () => ReflexSettings.PlayersTab, v =>
            {
                ReflexSettings.PlayersTab = v; ReflexSettings.Save();
                if (!v) Reflex.Port.OriginalWorld.LocalGamepadRouting.Player2Pad = null;
            }, panel, "SHOW", "HIDE"), "players-tab");
            Help(panel, "SHOW: the race, Free Ride and practice setup has a PLAYERS tab. Press A on a second controller there to ride split screen, two riders on one screen. HIDE: for riding solo only; the setup shows EVENT and RIDER.");
            Section(panel, "LOADING");
            Named(OptUi.Toggle("DISABLE COMPOUND", () => ReflexSettings.DisableCompound, v =>
            {
                ReflexSettings.DisableCompound = v; ReflexSettings.Save();
                if (v) Reflex.Port.OpenFrontEnd.OpenArenaWarm.Release("DISABLE COMPOUND is on");   // (its warm session and the memory it holds)
            }, panel), "disable-compound");
            Help(panel, "ON: races, practice and Free Ride load behind a full-screen loading screen instead of putting you on your bike in the compound while they load. Faster on slower PCs and uses less memory in the menus. The garage's TEST RIDE and MotoSkills still use the compound.");
            Section(panel, "FREE RIDE");
            Named(OptUi.Toggle("NO INVISIBLE BORDERS", () => ReflexSettings.FreeRideNoBorders, v =>
            {
                ReflexSettings.FreeRideNoBorders = v; ReflexSettings.Save(); ReflexClientLive.ApplyFreeRideBorders();
            }, panel), "free-ride-no-borders");
            Help(panel, "ON: Free Ride never resets you for where you ride. Walls, fences and the ground stay solid; deep water and the edge of the map still bring you back. OFF: the original's borders. Races always keep them.");
            Named(OptUi.Toggle("ALIVE LOOK", () => ReflexSettings.AliveLookReflex, v =>
            {
                ReflexSettings.AliveLookReflex = v; ReflexSettings.Save();
            }, panel, "REFLEX", "ALIVE"), "alive-look");
            Help(panel, "How MX vs ATV Alive tracks are drawn. REFLEX: with this game's dirt, ruts, sun and haze over the Alive layout. ALIVE: as MX vs ATV Alive drew them.", When.NextEvent);
            Named(OptUi.Toggle("SPAWN MARKERS", () => ReflexSettings.SpawnMarkers, v =>
            {
                ReflexSettings.SpawnMarkers = v; ReflexSettings.Save();
            }, panel), "spawn-markers");
            Help(panel, "In Free Ride and PRACTICE: hold SET SPAWN MARKER (Y or F) to keep your place, press RIDE TO SPAWN MARKER (B or V) to ride back there at once. Never in races. OFF: those buttons are the original's reset flag.");
            Section(panel, "PHYSICS STUDIO");
            Named(OptUi.Toggle("PHYSICS STUDIO EVERYWHERE", () => ReflexSettings.PhysicsEverywhere, v =>
            {
                ReflexSettings.PhysicsEverywhere = v; ReflexSettings.Save(); Reflex.Port.RxCore.RxPhysicsStudio.Everywhere = v;
            }, panel), "physics-everywhere");
            Help(panel, "OFF: your PHYSICS STUDIO config rides in races, Free Ride and the test ride only. ON: also in the career, MotoSkills and the challenges, as practice: nothing is saved there.", When.NextEvent);
            BuildReplayOptions(panel);   // REPLAY / PHOTO MODE (ReflexOptionsMenu.Replay.cs)
            ResetSection(panel, "OPEN REFLEX", "reset-open-reflex", "Puts every row of this page back to its default: the air sliders at the original's, RIDER MOTION ORIGINAL, the race setup, loading, Free Ride, leaderboard, online and replay rows as they ship.",
                         "Every OPEN REFLEX option on this page goes back to its default: the riding sliders (the original's air), RACE SETUP, LOADING, FREE RIDE, PHYSICS STUDIO EVERYWHERE, POST LAP TIMES, PARTY MODES, CHAT and the replay rows. Your Physics Studio configs stay.", ResetOpenReflex);
            return first;
        }
    }
}
