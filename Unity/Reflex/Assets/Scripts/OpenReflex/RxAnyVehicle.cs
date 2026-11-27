namespace Reflex.Port.RxCore
{
    /// ANY VEHICLE (core/plan/ANY_VEHICLE.md): the event about to load rides a vehicle type or a vehicle class its event type never offered
    /// (a truck at a Supercross, a National against buggies): OpenSetup.OffEvent. Set where the launch is made (ReflexEventSelect.Launch, the
    /// front end's pre-started session) before the event's session is made; OpenReflexSimulation hands it to the core before the event loads
    /// (rx_session_set_any_vehicle: no offline records, best laps or highlights; the laps' mods bit 32), as RxPracticeMode. The front end
    /// clears it (OpenShell.Awake). Main thread writes; the simulation reads it at construction.
    public static class RxAnyVehicle
    {
        /// The world about to load / loaded is such an event: its laps are not kept or posted.
        public static bool Current { get; private set; }
        public static void Set(bool on) => Current = on;
        public static void Reset() => Current = false;
    }
}
