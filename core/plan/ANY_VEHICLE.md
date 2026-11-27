# Any vehicle on any track

2026-11-16, branch `experimental`. PORT ADDITION, after Open Unleashed's MACHINES ON ANY TRACK (`MX vs ATV RE`:
`Unity/MXvsATV/Assets/MXA/Scripts/UI/MxaAnyTrack.cs`, `RideGame.Machine.cs`).

## The rule

The original's LocationSelect offers a vehicle only where the event type lists a class with it (`EventTypeVehicleClasses` ->
`VehicleClass` / `VehicleClassType`): bikes and ATVs at Nationals and Supercross, the trucks, buggies and UTVs at Powercross and so on.
With **ANY VEHICLE ON ANY TRACK** on (the default) the open front end's race setup lifts that:

- **VEHICLE** offers every vehicle type the port drives that has a rider (your own of that type in the GARAGE, or a pro with one): MX Lite,
  MX, ATV, UTV, Pro Lite, Super Buggy, Pro 2, and after them the imported Alive classes (`.rxmod` bikes on physics of their own: 50CC,
  650, ATV 1000), on every arcade event: Nationals, Supercross, Omnicross, Waypoint, Freestyle, Section Race, Powercross, the Alive and
  Unleashed races, the restored levels and custom tracks (`Mods/Tracks`). Free Ride always offered every vehicle.
- **CLASS** offers, in this order: the event's own classes for the vehicle (the original's list, unchanged), then the game's other
  classes that have the vehicle (fewest vehicle types first, so its own Single class leads), then every other class the port races.
- **What the AI field rides: the CLASS.** The core draws each computer rider's vehicle from the class's `VehicleClassType` rows, as it
  always did (`rx_ai_roster_build` / `rx_ai_rider_add_of_class`, the transliterated MXUIManager_BuildAIRoster / AddAIRiderOfClass); the
  player's vehicle is a row of its own and never had to be one of them. So:
  - a vehicle the event never had starts on its own Single class: **the field rides what you ride** (a Pro Lite truck at a Supercross races
    eleven Pro Lite trucks);
  - any other class may be chosen: a mixed one (Moto, Machines, Unlimited), or one without your vehicle at all (a bike against buggies).
- The option is `ReflexSettings.AnyVehicle` (`OpenReflex.AnyVehicle`, ON by default): a toggle on the setup's RIDER tab and in OPTIONS >
  OPEN REFLEX > RACE SETUP. OFF gives the original's lists back.

Open Unleashed's design, and where this follows it:

| Open Unleashed | here |
| --- | --- |
| one option, a toggle in the options and on the setup page | the same |
| VEHICLE gets every machine on every tab | VEHICLE gets every vehicle type on every event |
| OPP. VEHICLE: SAME = the machine's own group, or any group | CLASS: the vehicle's own class first, or any class |
| single player only; online the host's allowed vehicles decide | single player only; online is unchanged |
| a lap's board comes from what the ride loaded | the same, and an off-event ride is not kept or posted at all |
| off by default | **on by default** (the request: no limits) |

## Records and leaderboards

A setup is **off-event** (`OpenSetup.OffEvent`) when the vehicle's type is not one of the event type's own, or the class is not one of
its own classes. Such a ride is told to the core before the event loads (`rx_session_set_any_vehicle`, `RxAnyVehicle`), and then, like
a scaled or a custom track:

- no offline records, best laps or highlight progress (`rx_records_join`, `rx_highlight_add_progress`: the same gates as MAP SCALE);
- every lap of the lap log carries mods bit 32 (`RX_LAP_MOD_ANY_VEHICLE`); `RxPractice.Invalid` refuses the lap as `ANY VEHICLE`, so it
  is not YOUR BEST, keeps no ghost and is never posted (`RxLeaderboard.OnLap` returns on an invalid lap before posting). PRACTICE too.
- the setup page says so: a line under the toggle, an `ANY VEHICLE · NO RECORDS` chip on the summary, and the LEADERBOARD block gives way
  to a note. The LEADERBOARDS page's RACE GHOST / WATCH GHOST still take only the event's own vehicles (`OpenSetup.EventVehicleTypes`).

A choice that is one of the event's own (every setup the original could make) is untouched: same lists first, same default, same
records, same boards. The flag changes nothing of how the game runs; off (the default of the core) is the original bit for bit.

## Verified (2026-11-16, headless test player, `AnyVehicleProbe`)

Each race: the shell's own setup and launch, 11 computer riders, 2 laps, the player ridden by the probe's race-line autopilot.

| event | you | the field (CLASS) | result |
| --- | --- | --- | --- |
| Supercross, Springfield | Sport Truck (Pro Lite) | 11 Sport Trucks | finished 2/2, every AI finished |
| Supercross, Springfield | Sport Buggy | 11 Sport Buggies | finished 2/2, every AI finished |
| National, Bedford | UTV | 11 UTVs | finished 2/2, every AI finished |
| National, Bedford | Sport 2 Truck (Pro 2) | 11 Sport 2 Trucks | finished 2/2, every AI finished |
| Powercross, Laguna | ATV | 11 ATVs | finished 2/2, every AI finished |
| Powercross, Laguna | MX | 11 MX bikes | finished 2/2 (second run; see below), every AI finished |
| National, Bedford | MX | 11 Sport Buggies (class picked) | loads and races, every AI finished; the autopilot stalled after lap 1 |
| Freestyle, Buena Vista B | Sport Truck | 11 Sport Trucks | loads and runs (45 s ridden, no laps in this type) |

Control: National, Bedford on MX in the MX class (the event's own): not flagged, finished 2/2, its flying lap `CLEAN` (kept as usual).

In every lap race the flying laps carried the core's bit and were refused as `ANY VEHICLE`; none was kept. Two rides did not finish
because the probe's autopilot lost its race line and stopped (the computer riders finished both; the same Powercross ride finished on a
second run): a weakness of the test's autopilot, not of the event.

Not verified: an imported Alive class (`.rxmod`) on an event without its base type (it rides in its base model's slot, so it is the
MX / ATV case with that bike forced, but no run was made); custom and Alive / Unleashed tracks (they borrow the classes of the
original's first event of their type, so the lists are the same code path); Section Race, Omnicross, Waypoint; a human at the wheel.

## Known limits (follow-ups)

- **The start gate is a bike gate.** A National / Supercross gate's slots are a bike's width apart: UTVs, buggies and trucks stand
  inside each other until the gate drops (they separate as they pull away, and nothing wrecked in the test races). The grid code can put
  riders in rows behind the gate (`GRID_ROWS.md`) and takes start zones per rider (`rx_session_set_start_zones`); spreading a wide field
  over every second or third slot and the rows behind would use those.
- **The gate pose.** At a gate that drops, the drivers of UTVs / buggies / trucks stand up through the roof until the start (the bike
  riders' gate stance, it seems); seated as soon as the race runs.
- The track itself is not changed: a Supercross rhythm section is rough in a truck, and the AI's lines are the bikes'. The computer
  riders finished every test race all the same.
- A setup with no VEHICLE / RIDER choice of its own rides the GARAGE's selection, as before; when that is a vehicle the event never had
  (after racing a pro's truck, say) the page now shows it, with its class and the NO RECORDS chip, instead of the event's first type.

## Not changed

- **The career**: its events launch with the career's own vehicle rules (`OwnCareerLaunch`), never through this setup.
- Challenges, MotoSkills, the test ride and the party modes (`OpenSetup.AnyVehicleCould`).
- The original front end (`FrontEnd/`), and the legacy plugin (`UseOpenReflex` off).

## Where

- core: `game/map_scale.c|h` (`rx_ms_set_any_vehicle`), `game/build_activity_records.c`, `game/highlight_progress.c`, `game/practice.h`,
  `api/practice_session.inc`, `api/rx_session.c`, `include/rx_api.h` (`rx_session_set_any_vehicle` / `rx_session_any_vehicle`).
- Unity: `OpenFrontEnd/OpenContent.cs` (`OpenSetup.AnyVehicle`, `VehicleTypes`, `Classes`, `OffEvent`, `Launch`),
  `Content/ReflexEvents.cs` (`RaceableClasses`), `OpenFrontEnd/OpenShell.Setup.cs` (rows and notes), `OpenShell.Browser.cs` (summary chip),
  `OpenReflex/RxAnyVehicle.cs`, `OpenReflexSimulation.cs`, `RxApiNative.cs`, `OriginalWorld/ReflexEventSelect.cs`,
  `OpenFrontEnd/OpenEventPrestart.cs`, `Practice/RxPractice.cs`, `Options/ReflexSettings.cs`, `ReflexOptionsMenu.OpenReflex.cs`.
- test: `tests/menus_showcase/fixture/AnyVehicleProbe.cs` (`REFLEX_ANYVEHICLE=Supercross:14,National:11,...`).
