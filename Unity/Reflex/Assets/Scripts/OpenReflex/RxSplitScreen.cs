namespace Reflex.Port.RxCore
{
    /// SPLIT SCREEN (port addition, w-split; the original's console split screen, whose pages are unreachable on PC): two local riders on
    /// one screen. The race / Free Ride setup's PLAYERS row asks for it (ReflexEventSelect.Launch's splitScreen: Requested for that launch,
    /// every other launch clears it); OpenReflexSimulation hands it to the core before the event loads (rx_session_set_local_players 2:
    /// the second human is controller 1, i.e. the second pad) and sets Active when the core took it. While Active the simulation reads the
    /// second player's camera / bike / race state each frame (rx_session_set_view_player) and the world draws two viewports
    /// (OriginalWorld/ReflexSplitScreenView.cs). Exhibitions only: not online, not a career; PRACTICE too (only player 1's laps are logged).
    /// SPLIT SCREEN RULE: player 2's laps are NEVER saved (not timed for records, posted, recorded as ghosts or kept): the core logs player 1's
    /// laps only and player 2's records go to a session-only player database (core/include/rx_api.h).
    public static class RxSplitScreen
    {
        static volatile bool requested, active, single;
        /// The next launch asks for two local players.
        public static bool Requested => requested;
        /// The running world has two local players (set by the world from its session: OpenReflexSimulation.SplitPlayers).
        public static bool Active { get => active; set => active = value; }
        /// One full-screen view although two riders are in the world (Post: the finish scenario, the replay and the results; the REPLAY /
        /// PHOTO MODE viewer). Main thread only (OriginalWorldRuntime.UpdateSplitViews decides once per frame); not read on the worker.
        public static bool SingleView { get => single; set => single = value; }
        /// The screen is cut in two this frame (the HUDs keep to their halves: HudSafeRoot).
        public static bool TwoViews => active && !single;
        /// The line between the two views as a fraction of the screen height from the bottom (ReflexSplitScreenView: the middle of the
        /// launch viewport, where the cameras are cut; the HUDs' halves follow it: HudSafeRoot). Main thread only.
        public static float MidY = 0.5f;
        public static void Set(bool on) { requested = on; if (!on) { active = false; single = false; } }
    }
}
