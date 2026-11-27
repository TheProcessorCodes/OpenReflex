# B.R.A.A.P. — the player's race AI (2026-11-16)

**B.R.A.A.P.** (*Build Riders: AI, Appearance & Plates*), after Open Unleashed's feature of the same name: rosters of AI riders with
their names, jersey names, numbers, gear and bike graphics. Hub chip **B.R.A.A.P.**; the race setup's **AI ROSTER** row picks the
roster in use too.

## What a roster changes

- The roster **in use** renames and redresses the **computer riders of offline races**: the name on the HUD / results / podium, the
  jersey and plate text, the gear, the bike graphics. `ReflexRoster.ReadOpen` → `ApplyBraap` (`OriginalWorld/ReflexRoster.Braap.cs`).
- The core is not touched. It still runs its own `AIRider` rows on their own `AIVehicle`s; only the entry Unity draws and names
  changes (as a gear / kit pack only changes the drawing), so the bike **model** is the game's pick for the event's class. The one
  thing given to the core is each rider's SKILL tier (below).
- **Not in the career** (its standings name the game's riders) and **not in an online session**. `ReflexBraap.BeginEvent(allowed)`
  in `OriginalWorldRuntime` decides per event.
- `DEFAULT` in use = nothing is changed at all.

## Rosters (`Content/ReflexBraap.cs`)

- **DEFAULT**: the game's `AIRider` rows, read-only. **Season rosters** (`ReflexBraap.Seasons.cs`): built-in, read-only, the real
  fields of a season's series in final standings order (2026 and 2025: AMA Supercross 450 / 250 East / 250 West, Pro Motocross
  450 / 250, SuperMotocross 450 / 250, MXGP, MX2, WMX), with the number each rider ran in that series and his bike's make as the
  card's note. Names, numbers and makes only. **The player's**: copies of those (EDIT A COPY) or new ones.
- A rider: NAME (24), JERSEY NAME (25 capitals, ReflexPlateRaster.MaxName; a longer one than the jersey holds runs off both ends; empty = his family name), NUMBER (0..999), IN THE FIELD, SKILL, TEAM (a note),
  LOOK, BIKE GRAPHICS.
- **SKILL**: EVENT'S (the race setup's AI OPPONENTS, as the game spreads it over the field: the default) or ROOKIE / AMATEUR / PRO /
  ALL TIME, always. These are the game's own four tiers: the Player List's AIDifficulty cell that `MXUIManager_BuildAIRoster`
  buckets the field into, player `+0x8f4` (0 All Time .. 3 Rookie), which the AI code reads every frame. Unity gives each roster
  rider's tier to the core once the event's riders exist (`rx_session_set_ai_tier`, `OpenReflexSimulation.SetAiTiers`; again for
  the next heat). RIDERS has EVERY RIDER'S SKILL to set the whole roster at once.
- **LOOK** = picks, one per gear type: a stock `RiderGearId`, a gear pack piece's token (`g:<hash>:<row>`) or an Alive gear piece's
  token (`<mod>/<piece>`). No picks = **AUTO**: the gear set of one of the game's riders, picked by the rider's name (the same every
  race). The LOOK row sets a whole outfit (the garage's RIDER PRESETS, gear pack lines, Alive gear lines, every game rider's set);
  the rows under it change single pieces. A pick this game does not have leaves AUTO's piece of that type.
- **BIKE GRAPHICS** = a kit pack's kit (its tokens for every bike it fits); drawn when it fits the bike the core gave the rider.
- **THE FIELD**: A RANDOM DRAW (per event, nobody twice before everybody raced) or FROM THE TOP (the list's first riders, in
  order). The draw is keyed by `AIRiderId`, so the next heat keeps each rider.
- Storage: the client save, key `Braap.Rosters` (JSON).

## Sharing

- **Share code**: `RXBRAAP1:` + base64 of the gzipped JSON; found anywhere in pasted text.
- **Roster file**: `<name>.braap` (pretty JSON `{format, version, roster}`) in `<save>/Braap`; ROSTER FILES lists the folder.
- Open Unleashed's codes (`BRAAP1:`) and `.braap` files read here, and these read there: names, numbers and who lines up (the
  gear and bikes are another game's, so AUTO).

## Tests

- `tests/menus_showcase` page `braap` (`fixture/UiShowcaseBraap.cs`): the rosters, editing and the save, share codes and files,
  the draw, a look that builds, the screens. Log lines `[braap-test] PASS|FAIL`.
- A race: `REFLEX_BRAAP_USE=<roster id>` (e.g. `season:mx450-2026`) puts a roster in use without a save; `REFLEX_BRAAP_SEED`
  fixes the random draw; `REFLEX_BRAAP_SKILL=1..4` puts every rider at one skill (the log's `AI TIERS` line is the core's answer). The player log's `Reflex opponents:` lines name every rider.
