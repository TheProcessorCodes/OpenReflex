# MX vs ATV Alive bikes and ATVs

Status: **experimental** (branch `experimental`, 2026-11-09). The in-client Alive importer (GAME LIBRARY > MX vs ATV ALIVE) imports Alive's
motorcycles next to its levels: each one becomes a `.rxmod` bike folder (`core/plan/RXMOD.md`) in the client's content folder,
`<content>/AliveBikes/<slug>/`, made on the player's machine from their own disc image and LIVE packages. Nothing of Alive ships with the
game; an imported bike is the player's own, like an imported level.

Code: `Unity/Reflex/Assets/Scripts/Content/AliveImport/AliveBikes.cs` (tables, bike list, conversion, install),
`AliveImport/AliveBikeMaps.cs` (texture decode, the bakes, a deterministic PNG writer), `OpenFrontEnd/OpenShell.AliveImport.cs` (the
ALIVE BIKES rows of the import list, the results), `Content/BikeMods.cs` (`AliveFolder`: scanned next to `Mods/Bikes`, mod ids
`AliveBikes/<slug>`). Tests: `tests/alive_bikes/run.sh` (the importer outside Unity, every bike converted and checked),
`tests/menus_showcase` page `alive-bikes` (the real screens: scan, list, import, results, BikeMods) and `rxmod-garage` (the MOD cards),
`tests/rxmod/rxmod_ride.py` (a race on one).

## What Alive ships (survey)

Sources: the disc (`MX vs. ATV Alive (USA, Europe).iso`) and the title's content folder `5451087B/00000002`: 4 level packs and 10 more
LIVE packages: **Honda Vehicle Pack**, **Yamaha Vehicle Pack**, **KTM Vehicle Pack 1 / 2**, **Suzuki Vehicle Pack 1 / 2** (each brand's MX
bikes and an ATV), **Rider Gear Pack 1 / 2 / 3** (rider gear and graphic kits), **MotoClub Pro** (tables only). Extracted for study under
`_re/out/alive_dlc/<pack>` (alive_stfs.py).

Every Alive motorcycle (the `VehicleModel` rows of `MXTables_Default` + `MXTables_DLC*` whose `VehicleType` is a `MotocrossBike`; ATVs left
out):

| Alive # | bike | pack | VehicleType | physics file | chassis package | rides as (Reflex #) |
|---:|---|---|---|---|---|---|
| 23 | Blaze 450F | disc | MX450 | mx450f | MX / MX_MX1 (MX_NeptuneF) | Blaze 450F (1) |
| 24 | Blaze 250F | disc | MX250 | mx250f | MX_MX2 (NeptuneF_Lite) | Blaze 250F (11) |
| 28 | Blaze 125 | disc | MX125 | mx125 | MX_MX6 (Neptune_Lite) | Blaze 125S (12) |
| 26 | Takashi 450F | disc | MX450 | mx450f | MX_MX4 (EarthF) | Takashi 450F (32) |
| 27 | Takashi 250F | disc | MX250 | mx250f | MX_MX5 (EarthF_Lite) | Takashi 250F (31) |
| 25 | Takashi 125 | disc | MX125 | mx125 | MX_MX3 (Earth_Lite) | Takashi 125R (29) |
| 35 / 36 / 41 | Honda CR125R / CRF250R / CRF450R | Honda Vehicle Pack | MX125 / MX250 / MX450 | class files | MXHonDLC1_MX1..3 | Blaze 125S (12) / 250F (11) / 450F (1) |
| 33 / 34 / 43 | Yamaha YZ125 / YZ250F / YZ450F | Yamaha Vehicle Pack | same | class files | MXYamDLC1 | Takashi 125R (29) / 250F (31) / 450F (32) |
| 31 / 32 | Suzuki RM125 / RMZ250 | Suzuki Vehicle Pack 1 | MX125 / MX250 | class files | MXSUZLDLC1 | Shima 125R (6) / 250F (17) |
| 49 | Suzuki RMZ450 | Suzuki Vehicle Pack 2 | MX450 | mx450f | MXSUZDLC2 | Shima 450F (10) |
| 39 / 40 | KTM SX150 / 250SXF | KTM Vehicle Pack 1 | MX125 / MX250 | class files | MXKTMLDLC1 | KTM 125 SX (36) / 250 SX-F (18) |
| 47 | KTM 350SXF | KTM Vehicle Pack 2 | MX450 | mx450f | MXKTMDLC2 | KTM 450 SX-F (28) |
| 51 | MX250 FMX (the Sliderock freestyle 250) | Sliderock Canyon | MX250 | **mx250** (2-stroke) | MX250FMX_MX1 | Dakumoto 250S (16): the brand no other Alive bike takes |
| 30 | **50cc mini** | Stewart Compound | MX50 | **mx50** | MX50DLC1_50 (MX_FR50, MX_Hierarchy_50) | none: its own class (below) |
| 22 | **MXFR650** (the enduro-style big bore; AllowAI 0) | disc | MX650 | **mx650f** | MXFR_MXFR650 (MX_EarthF_650) | none: its own class (below) |

Alive's base bikes reuse Reflex's fictional chassis names (NeptuneF, EarthF ...) but are reworked models (other surface / vertex counts).
The graphic kits: 121 on the disc, 10-31 per brand pack, 126 in the Rider Gear packs (only each bike's default kit is imported today).

## The ATVs (2026-11-09): every Alive vehicle imported

Alive's VehicleModel rows are motorcycles and ATVs only (no trucks, buggies or UTVs): 21 motorcycles + 9 ATVs = **30 vehicles, all
imported**. The importer finds ATVs by VehicleType FactoryName "Atv". Alive's `ATV_Hierarchy` is node for node Reflex's (50 nodes, the
same rest positions within 0.001 ft but the hand locators), so an Alive ATV rides as the Reflex ATV of its brand (one Reflex ATV class:
`atv_vehicle`) drawn on its own nodes (`"fit": "nodes"`: unscaled, bound to its own rest pose; `BikeMods.ModsFor` / `ApplyRx` now take
an ATV for such a bike), its hands and feet on its own grips / pegs.

| Alive # | ATV | pack | physics file | rides as (Reflex #) |
|---:|---|---|---|---|
| 13 / 15 | Blaze ATV 250F / ATV 450F | disc | atv250f / atv450f | Blaze ATV (0) |
| 12 / 14 | Takashi ATV 250F / ATV 450F | disc | atv250f / atv450f | Takashi ATV (21) |
| 42 | Honda TRX450R | Honda Vehicle Pack | atv450f | Blaze ATV (0) |
| 44 | Yamaha YFZ450 | Yamaha Vehicle Pack | atv450f | Takashi ATV (21) |
| 48 | KTM 450SX | KTM Vehicle Pack 2 | atv450f | KTM 450 SX ATV (74) |
| 50 | Suzuki LT450R | Suzuki Vehicle Pack 2 | atv450f | Shima ATV (20) |
| 29 | **ATV 1000 Dune** (the sand quad; product "DO NOT LOCALIZE") | disc | **atvDune1000** | own class 11 in the Raven ATV's slot (23) |

The ATV 1000 has no Reflex equivalent (a 2.3 ft swingarm against 1.7, its own `ATVFR_Hierarchy`), so it follows the 50cc: Alive's
`atvDune1000_vehicle` (93 of the ATV table's 103 records) over the ATV class file for the local rider (`rx_session_vehicle_physics` class
11: the core now takes ATV files, root type "atv"), its 49 hierarchy nodes' rest pose (`_nodes`), garage section "ATV 1000", leaderboard
board **`alive_atv1000`** (client, Worker `VEHICLES`: **not deployed**; the site shows unknown ids as they are). Native check
(`alive_class_probe`, Free Ride FR_Alpine, Raven ATV): stock 53.0 ft/s top, with the dune file 64.5 ft/s, class 11.

ATV colours: Alive colours ATV frames, swing arms, A-arms, rims and springs at run time from the parts' own table rows
(`VehiclePartCustomColor` -> `CustomColor.ConstantName` -> `CustomDynamicColor` RGB: the TRX450R's frame 0.90 grey, swing arm brown, A-arms
and rims black). The importer reads those rows for every vehicle and bakes `Template_ATV_FrameRefl_FNL` / `Template_Aarms_FNL` /
`Template_Vehicle_Wheel_FNL` / `Template_ATV_Spring_FNL` (a combo map's R times the colour) into diffuse maps.

Checks: all 30 through the real import screens (showcase `alive-bikes`), all 30 MOD cards (`rxmod-garage`, every shot opened full size);
races (`rxmod_ride.py`, NAT_Concord, 5 AI): Honda TRX450R PASS, ATV 1000 Dune PASS (its own physics in the race log).

## How Alive stores a bike

The same engine family as Reflex. A bike is its `VehicleModelParts` rows marked `Default`, each a `VehiclePart` naming a model and a
material package (`AssetPackageDatabase` -> package, database):

* **GraphicKits**: the model package is the **chassis** (`MXHonDLC1/MXHonDLC1_MX3`: the textures, surfaces, materials and models of the
  bike, `MX_Hierarchy`, `MXConvex`, and the physics file `mx450f_vehicle` as a bxml), the material package the **kit**
  (`MXHonDLC1_graphickit_MX3_Stock_Default`: the plastics material and maps).
* **Handlebars**, **Exhausts**, **Tires** (front + rear tyre models with rim / spokes / hub / tyre surfaces), **Wheels**, **Forks**
  (only fork stickers and tuning bxml), **Frame** / **Brakes** (tuning bxml), **EngineAudio**.
* `MX_Hierarchy` is node for node Reflex's (18 nodes: OBJ_Frame, OBJ_UPR_Forks, OBJ_LWR_Forks, OBJ_FRT_Tire, OBJ_Swingarm, OBJ_BCK_Tire,
  levers, NDE_ locators), so a full-size Alive bike fits a Reflex hierarchy exactly (CRF450R on the Blaze 450F: scale 1.0000, steering
  axis 0.00 in off, tyres 13.5 / 13.2 in against 13.5 / 13.3).
* Xbox resources (big-endian; tiled textures): the level importer's `Converters.Model` / `Converters.Surfaces` / `Textures.Convert` give
  the PC resources `Rfx.ParseModel` / `ParseSurfaces` read. Materials are read from Alive's own material documents (their sampler states
  carry ids the level materials never had).
* **Physics files** are Reflex's schema (the core reads `mx125 / mx250 / mx250f / mx450f_vehicle`, 108 records; Alive's add rider-air
  fields: mx50 shares 100 of Reflex's 108). Alive's brand bikes share their class's file.

### Alive's dynamic colours (baked)

Alive colours parts at run time (the Xenos programs, read with `_re/formats/xenos_disasm.py`):

* `Template_MX_ATV_DynPlastic_FNL` (plastics): the diffuse is grey; where its **alpha** is set the program draws `usr_userdyncolor`
  (0.4 + 0.6 (N.V)^1.5 of it), the combo map's **R** blends to the plate colour `usr_dynnumplate`, **G** is the mud mask, **B** the shine,
  the `Numbers` texture (the rider's number) on the second UV set. Baked: diffuse = the dyn colour where alpha, the plate colour where R
  (the facing value); SFX = (0, G, B) for Reflex's plastics program; `"numbers": true` keeps the rider's plate (RXMOD.md).
* `Template_MX_Wheel_Fnl` (rims, hubs): no diffuse; combo R the shading, B where `usr_mxrimdyncolor` x 0.92 tints; the hub
  `usr_mxhubdyncolor`. Baked into a diffuse per material.
* `Template_MX_FrameRefl_FNL` (frames): SFX R the shine, B flat: B := R for Reflex's frame program.

## The conversion (`AliveBikes.Convert`)

1. The chassis model's first LOD (static surfaces placed by their node's world matrix, skinned ones as stored, bones mapped by name), the
   part models on their nodes (a tyre model in its node's space on the chassis' tyre node; a bar / exhaust model in its node's space).
2. Each binding's material as the game resolves it: the first material of the bike's material packages (the kit first) whose
   `MaterialKey` prefixes the binding's; else the model package's own.
3. Its template by program: DynPlastic -> plastic, FrameRefl / Hbars / T_Diffuse -> frame, RavEngine / Exhst -> engine, DifSpecAlpha /
   AlphaMtl -> chain (alpha tested: the chain, the spokes), MX_Wheel -> wheel, TireBlur -> tire. Maps by name (`_SFX` / `Combo` the SFX,
   `Nrml` the normal map, the rest the diffuse; Mud_Noise, Numbers, the reflection cube, the blur strip and the fork sticker left out).
4. Written: `bike.rxmesh`, `maps/*.png` (19 per bike), `mod.json` (`"bike"` the Reflex VehicleModelId, brand / model from the store product,
   author "MX vs ATV Alive", `"fit": "axles"`). ~2.3 MB a bike, all 19 in about 4 s.
5. Installed: staged under `<content>/AliveBikes/.ab-*`, moved to `<content>/AliveBikes/<slug>`, with `<slug>.import.json` beside it (the
   files and their SHA-256; a folder without one is never replaced).

The PNGs are written by the importer's own deflate (fixed Huffman), not the runtime's: the same install gives byte-identical folders on
every machine, so two players who imported the same bike hold the same `.rxmod` content hash and see each other's bike online (the look's
`b:<hash>:<id>` token, RXMOD.md "Online"); a player without it sees the original bike.

## Status (2026-11-09)

* **Importer outside Unity** (`tests/alive_bikes/run.sh <scratch> --out DIR --bikes all <iso> <5451087B>`): 21 motorcycles found,
  19 converted (every one ok, 26-28 surfaces, 19 maps, 2.1-2.7 MB each), the 50cc and the MXFR650 listed as needing their own class.
* **In the client** (showcase `alive-bikes`, the Mono player, real screens): FIND LEVELS lists the levels, then ALIVE BIKES (19 of 21
  ticked, the two held back say why), IMPORT 19 BIKES, the results (19 installed), BikeMods lists all 19 as `AliveBikes/<slug>` riding as
  their Reflex bikes. The maps it writes are byte-identical to the .NET run's; the meshes differ only in the last float bits (the two
  runtimes' arithmetic) at identical sizes - a folder mod's content hash is over its entries' names and sizes, so the online token is the
  same on every machine.
* **Garage** (showcase `rxmod-garage`, all 19): each a MOD card under the bike it rides as ("CRF450R | HONDA · BY MX VS ATV ALIVE"), A
  equips it, the stage and the race setup's hero draw it; the rider's number on Alive's plates. Every shot opened full size.
* **Races** (`rxmod_ride.py`, one-lap National at NAT_Concord with 5 AI): Honda CRF450R on the Blaze 450F and KTM 350SXF on the KTM 450 SX-F
  (from `<content>/AliveBikes`): both PASS, finished; fit scale 1.0000, steering axis 0.00 in off the original's, 16 / 15 in of fork /
  swingarm travel on the mod's parts; the online look's token resolves to the bike (an unknown token: the original).
* Compile checks: Mac and Windows, 0 failed assemblies.

Known limits: only each bike's default graphic kit (Alive's other kits - 10-31 per brand pack, 126 in the Rider Gear packs - could be
generated as `.rxmod` skins); one LOD (the race model also in the garage: no HR twin); Alive's per-part custom colours are its defaults;
the bikes play their own Alive engine once its sound set is imported (ALIVE_ENGINE_SOUNDS.md), else Reflex's engine sound of the
bike they ride as.

## The 50cc and the MXFR650: their own classes (built 2026-11-09)

The user's choice (2026-11-09): both ride on **their own physics**, from Alive's own files in the player's install, not on a Reflex bike's.

Why: the 50cc's tyres are 0.6 ft (Reflex 1.17 / 1.04), its swingarm 1.0 ft (1.78), its fork attachment (0, 2.5, 0.1), torque about half a
125's; it has its own hierarchy (`MX_Hierarchy_50`, 14 nodes, the parent tree of Reflex's 18). The 650 has a 450F's geometry but body mass
9.6 (6.8), springs 1600 / 1500 (1100 / 950), an 8000 rpm limit, more low-end torque, final drive 3.23.

How:
* **Core** (`game/phys_studio.c` "ALIVE CLASSES", `rx_session_vehicle_physics` / `rx_session_vehicle_physics_nodes` in `rx_api.h`): before the
  event loads the client hands the core the bike's parameter file (Alive's bxml as it is: the `mx*_vehicle` schema, root type `mx`) for a
  VehicleModelId. When a LOCAL player's vehicle of that model is made, its class file is read as always (0043a3f0), then the Alive file's
  records (100 of the 108; the other 8 stay the base class's: the 125's for the 50cc, the 450F's for the 650) are written over it, at the
  hook PHYSICS STUDIO uses (`rx_ps_params_loaded`), before the masses are derived. `rx_session_local_phys_class` reports 9 (Alive 50cc) /
  10 (Alive 650). With nodes (the 50cc) the made vehicle's model hierarchy (V+0x1328) takes the bike's own rest locals, so the model pose
  (`rx_session_bike_pose`) has its geometry. Computer riders and the other online riders keep their own. Nothing changes without a call.
* **Importer**: the 50cc rides in the Dakumoto 125's garage slot (ownership, price), the 650 in the Dakumoto 450F's; each `.rxmod` folder
  gets `physics/<file>.bxml` (and the 50cc `physics/nodes.txt`, its hierarchy) and `"physics": {"file", "class", "nodes"}`, `"class"` (the
  garage section "50CC" / "650"), the 50cc `"pose": "physics"` + `"fit": "keep"`.
* **Client**: `BikeMods.OwnPhysics` (the bikes the player has chosen) -> `OpenReflexSimulation` calls the core before the load. A `"pose":
  "physics"` bike is drawn at its own size on its own rest pose; `OriginalBikePose.ForkFix` turns the steering assembly onto the axle the
  physics places (Alive's file has a 125's rake: without it the upper and lower forks parted and the fender floated off the legs); the
  rider's hands and feet go to the mini's own grips and pegs (`RiderLimbRetarget` offsets from its locators). The garage lists the two in
  sections of their own (50CC, 650) with their class on the info panel.

Checks (`core/tools/alive_class_probe.sh`: a full-throttle run on the reference National, 1 rider): Blaze 125S stock 85.3 ft/s top, with
Alive's mx50 61.6 ft/s (12,500 rpm in 5th, its own gearing), class 9; Blaze 450F stock 81.6 ft/s, with mx650f 76.1 ft/s (slower to the top
and steadier at 8000 rpm), class 10. Races (`rxmod_ride.py`, 5 AI): the 50cc PASS (12.4 in fork / 10.7 in rear travel, wheels on the ground,
the fork and fender one piece, hands on the bars), the 650 PASS (15.8 / 14.4 in). Garage pages: both cards, their sections, the stage at
the mini's own size.

Known limits: the rider is Reflex's full-size rider (MX animations) fitted to the mini's grips and pegs, not Alive's 50cc animation set; a
remote 50cc is drawn as the original. Both play their own Alive engine (MX_50cc, the 650's MX_400_TRACK) once its sound set is
imported (ALIVE_ENGINE_SOUNDS.md), else the engine sound of their base slot; a remote rider keeps its base slot's engine.

## Playtest fixes (2026-11-09 evening)

The user's report: the 50cc was not in the VEHICLE choices of practice / free ride / races; its wheel bounced in the air; the rider's
limbs "inverted" as the suspension compressed; the 50cc was hard to control in the air and too weak; the ATV 1000 underpowered.

* **Class select**: the own classes are VEHICLE choices of their own on the setup page (`OpenSetup.OwnTypes`, ids from 1000: the 50cc, the
  650, the ATV 1000 where the event takes their base model's VehicleType). The classes are the base type's; RIDER is YOUR RIDER only; the
  ride is the player's own vehicle of the base model, else a pro's vehicle of it under the player's rider; the launch forces the `.rxmod`
  bike for that ride (`BikeMods.RideModel` / `RideMod`, cleared when the front end comes back), so its own physics and board come along.
  Showcase page `alive-class` (PRACTICE, RACE, FREE RIDE).
* **The wheel in the air** was the rear suspension: the rear spring runs from a fixed point 3 ft up (z -2.6) to the wheel, and in the air
  the wheel hangs where that is the spring's rest length; Alive's 50cc keeps the 125's 4.2 ft (made for a 1.78 ft arm), which hung its
  1.0 ft arm 45 degrees down, right at the original's fold-over guard (`bike_slot16` 0043e200: an arm direction past z -0.7 snaps to a
  fixed one, the rear anchor with it). So in the air the arm and the rear wheel fluttered every other frame (the posed rear tyre
  -0.87 / -0.26 / -0.87 ft, the wheel body +-0.1 ft fore and aft); the physics' front and the fork were steady (`alive_class_probe`
  ALIVE_TRACE=1 / 2 / 3, ALIVE_RENDER, ALIVE_PAD: the bodies, the fork nodes, every node and rider bone, the sticks swinging). The tune
  sets the 50cc's rear spring rest length to 4.1 (below): the arm hangs at -0.75 at most, no flips in three runs (19 before). Backstop
  in the client: `OriginalBikePose.SwingFix` ("pose": "physics" bikes) turns the drawn arm about its hinge onto the simulation's rear
  wheel body, the tyre on that line at the wheel's distance held to 0.9 - 1.08 of the arm (in the dropouts).
* **The rider**: the core seats its rider as on the 125 (its animations, its locators): on the 50cc that is 0.17 ft further back and at the
  125's height over a seat 0.3 ft lower, so the arms could not reach the low bars and the IK locked them straight; a straight limb has no
  bend side and the grip IK after it read one from noise, so the elbows flipped inside out. Now: `"riderOffset": [0, -0.28, 0.15]` in the
  50cc's mod.json (the drawn rider moved onto its seat: `OriginalWorldRuntime.ApplyRider`, on a copy of the core's pose); the limb offsets
  of a "pose": "physics" bike are the locators' change in the frame's space (the parents move too on a bike of another size) less that
  move; `RiderLimbRetarget.TwoBone` stops at 98% of a limb's length (`MaxReach`). Arms at 0.75 - 0.8 of their reach (the 125's: 0.8),
  knees up; `rxmod_ride.py` logs the frames an elbow / knee bent the wrong way (0 of 6339 in a race).
* **Tune** (core `k_vp_tune` in `game/phys_studio.c`, applied as Alive's file is read, so every client rides the same and the boards
  `alive_mx50` / `alive_atv1000` time it): the 50cc and the ATV 1000 keep the Reflex class file's airborne records (`AirBorne_*`,
  `VehicleSim_AirBorne*`: Alive's take-off damping kept nearly all of the take-off spin, 0.98 / 0.8 / 1 against Reflex's 0.85 / 0.5 / 0.8,
  and its air drag is ~6x Reflex's; both go with Alive's own air control, `AirBorne_Pos_*` / `_Vel_*`, which Reflex has not); the 50cc's
  torque x1.2, final drive x0.85 (4.0 -> 3.4: it ran out of revs at 62 ft/s in 5th) and rear spring rest length 4.1 (the
  wheel in the air, above; it sits 0.1 ft lower on the ground); the ATV 1000's torque x1.35 (its peak 35.6 to
  Reflex's ATV's 48). The 650 is Alive's as it is. Native checks: 50cc top 62.0 -> 71.8 ft/s with the same launch (15.3 ft/s at frame
  31); ATV 1000 on FR_Alpine 16.7 -> 25.1 ft/s at frame 61 (the Raven 25.2), 49.5 at 121 (Raven 46.5).

## Liveries and parts (2026-11-10)

Alive's tables list, per vehicle, every part it takes (`VehicleModelParts`, 3153 rows; `VehiclePart` 879; by `VehiclePartType`). The
importer now makes them with the bike: a **parts** `.rxmod` folder beside it, `<content>/AliveBikes/<slug>-parts/` (RXMOD.md "Parts":
`"type": "parts"`, naming the bike by its mod and bike id), so the bike's own folder stays byte for byte what it was (its content hash
is the online token; a player without the Rider Gear packs' kits holds the same bike). Code: `AliveImport/AliveBikeParts.cs`
(`ConvertParts`), `AliveBikes.cs` (`AliveBikeSource.slots`, `BikeBuild`: the conversion split so an option is made exactly as the bike).

What Alive has, by type (motorcycles / ATVs), and what becomes of it:

| Alive type | options a vehicle | made | how |
|---|---|---|---|
| GraphicKits (the liveries) | 16 - 27 / 10 - 17 | all | the bike's materials the kit changes (its plastics: the dynamic colour and plate baked as the bike's own) |
| Handlebars | 50 - 51 / 44 - 45 | all | a mesh per bar model in the default bar's place + the colour's material |
| Exhausts | 5 - 13 / 17 - 18 | all | a mesh in the default exhaust's place |
| Tires | 6 / 9 | all | meshes (front + rear: tyre, rim, spokes, hub) in the default tyres' place |
| Wheels, A-Arms, Bumpers, NerfBars, Suspensions (ATV) | 16 / 10 / 12 / 10 / 8 | all | meshes |
| Frame (ATV "Factory" frame colour) | 1 | where its package is in the sources | materials |
| Forks, Frame, Brakes, Hub, Wheels (motorcycles) | 8 / 2 / 2 / 1 / 1 | **none** | tuning files and stickers only: nothing drawn changes |
| NumberPlate (blue / yellow / red) | 3 | **none** | they recolour the kit's plastics (a bake per kit and colour) |
| EngineAudio, Performance | - | none | not customizable in Alive |

26 of the 30 vehicles have options; the MXFR 650, the ATV 1000, the 50cc and the MX250 FMX have only their default parts in Alive's data
(the garage says so). The 26 with options: 2,530 options, 330 MB; all 30 bikes with their parts in 57 s (`tests/alive_bikes/run.sh <scratch> --out DIR --bikes all --parts`); the 30
bikes are byte-identical to the previous importer's (691 files compared). A motorcycle's parts are 12 - 16 MB (75 - 92 options), an
ATV's 9 - 12 MB (124 - 136).

Not carried over: Alive's per-part tuning bonus (written as `"aliveTune"` for reference, not applied: the tuning points are the Reflex
vehicle's, below), the user-picked rim / hub / frame colours (the parts' default colours are baked), Alive's vehicle levels that unlock
parts (every imported part is owned).

**In the garage** (RXMOD.md "Parts", `OpenShell.GarageRxParts.cs`): on an Alive bike CUSTOMIZE VEHICLE lists its own slots (GRAPHIC
KIT, HANDLEBARS, EXHAUST, TIRES ...; an ATV: + BUMPER, NERF BARS, A-ARMS, SHOCKS, WHEELS), tagged ALIVE; the original bike's graphics
kits, handlebars, number plates ... are not listed there (they do not fit), and no Alive part is listed on a Reflex bike. Under
PERFORMANCE stay the Reflex vehicle's tuning parts.

**Tuning points** (checked by the test page `alive-parts`): they are granted by `CoreGarageProfile.EquipPart` (a part of a type with a
`TuningAttribute` writes its `DefaultTuneAttribute1 / 2` into the vehicle's `PlayerVehicleTuning` row) and spent by `SetTuning`
(VEHICLE TUNING), both keyed by the **PlayerVehicle** - the Reflex vehicle an Alive / `.rxmod` bike rides as. The parts list of a `.rxmod`
bike is a client-side view; wearing its parts writes only the client save. So on an Alive or modded bike the points are earned (fit a
performance part under PERFORMANCE) and spent (VEHICLE TUNING) exactly as on the Reflex bike, and are the same when the original's card
is ridden again.

## Rider gear (2026-11-10)

Alive's `RiderGear` (604 rows) by `RiderGearType`: Helmet 148, Goggles 97, Jersey 159 (the whole suit: Alive has no separate pants or
gloves; the suit's atlas holds jersey, pants and gloves), Boots 56, Neckbrace 4, Butt Patch 136, Skin 4. Imported: **454 pieces of 107
gear models** (141 helmets, 97 goggles, 157 suits, 56 boots, 3 neck braces: the visible, named store products), 161 MB, 14 s, into
`<content>/AliveGear/<slug>/` as `"type": "gear"` folders (RXMOD.md "Gear"; `AliveImport/AliveGear.cs`), one per gear model with its
colours as pieces. Not imported: the butt patches (a texture on the pants) and Alive's skin tones.

Alive's rider is another rig than Reflex's (`Rider_M_Hierarchy`, 55 nodes: a T pose, a longer spine chain, other limb lengths, a head
joint 0.3 ft higher; `MXRider_Hierarchy` has 69). `RxGearRig.Make` fits it: the torso by a translation that lays hips, shoulders and neck
base on Reflex's (0.18 - 0.22 ft), each limb segment turned and stretched onto Reflex's joints, the feet by the bone frames; helmets and
goggles by a reference helmet box laid on a Reflex helmet's (so Alive's goggles keep their place in Alive's helmets). A suit draws
Alive's jersey and pants; the gloves and the skin stay Reflex's (the stock jersey model's glove and skin surfaces: Alive's seven finger
bones do not map onto Reflex's fourteen). Materials borrow the Reflex program of their role (Jersey_FNL, Helmet_FNL, Boots_FNL,
Goggle_FNL, Lens_Clr_FNL) from the rider's stock piece.

**Playtest fixes (2026-11-10, after 067f0b88).** (1) Reflex's rider has no body of its own: head, neck, wrists (skin) and gloves are
surfaces of the jersey model. In the garage the suit's id took the stock jersey's place, so no jersey model was named and the rider had
no head, neck or hands; and the garage's high-res rider copy (`FrontEndVehicleSelect.HighResRider`) dropped the Alive pieces whenever a
Reflex piece with an HR twin was worn too. Now a suit always names a Reflex jersey for the body (`RxGear.Body`: the player's own, else
the reference outfit's) and the HR copy keeps `rx`. The suit's own gloves (its surface on the `Gloves` node) are not drawn (Reflex's
are), and the forearm is drawn out by `RxGearRig.SleeveReach` (0.877 / 0.695: Alive's sleeve ends at 0.695 of its forearm, above a bare
forearm and a long glove cuff that are not drawn; Reflex's ends at 0.877) so the sleeve meets the wrist. A neck brace rides the chest
(the game's clips leave `NeckBrace` at the chest's frame; by the node's bind it hung beside the arm). (2) Shading: the importer's
`.rxmesh` files carry no tangents and the gear programs light through the tangent frame: `GearTangents` makes them (unit dP/du, square
to the normal: 0.96 - 0.99 against the game's own on its jersey, helmet and boots); and Jersey_FNL reads its G16R16F normal map as
0 .. 1 (2 v - 1; the stock maps' mean is 0.5), not signed as the tyres': `RxModBikes.Normal(.., biased: true)`, flat = (0.5, 0.5).
No re-import is needed for either. (3) GAME LIBRARY > MX vs ATV ALIVE: IMPORT was only enabled with a level or a bike ticked; rider gear
(or bikes) alone now import. Tests: page `alive-gear` (five mixes of Alive and Reflex pieces, body checks), `alive-gear-import`,
`rxmod_ride.py --env REFLEX_RXMOD_OWN=1 --save RxGear.<type>=...` (the player's own rider in a race).

**Custom gear skin under Alive gear (2026-11-10, after v1.1.0).** With a gear / kit pack piece (Mods/Gear: a custom gear skin) chosen for
a gear type and an Alive piece then worn for it, the RACE drew the custom skin's maps on the Alive mesh (and its body under a suit); the
garage was right. The race put the Alive piece on the spec's part of the slot after `ReflexRoster.ApplyKits` had made that part the
pack's (its model and material package), and `RxGearTemplate` borrowed the material of "the rider's own piece of the slot": the pack's,
whose map names are its own, so the Alive maps went into the wrong slots and the pack's stayed in the others. The garage builds the same
outfit with no pack part for that type. Now `RxGear.ApplyChosen(spec, stock)` sets an Alive piece's part back to the profile's own
piece (the pack's piece of that type is neither drawn nor offered; the types without an Alive piece keep their custom skin; taking the
Alive piece off brings it back: the pack's choice is kept in the save), and the borrowed material is always the reference outfit's.
Online riders and ghosts are built from look tokens (`ReflexRoster.ForLook`): they never carry Alive gear (they show the custom skin).
Tests: page `alive-gear-kits`, `rxmod_ride.py --env REFLEX_RXMOD_OWN=1 --env REFLEX_RXMOD_KITS=3,1,4 --save RxGear.<type>=...`.

**The size of a rider in Alive gear (2026-11-16, after v1.4.1; uncommitted).** A player: the Alive rider "is a lot bigger than the Reflex
model so it doesn't suit the Reflex bikes". Measured: the gear was already on Reflex's skeleton (`RxGearRig.Make`: same height, limb
lengths, helmet size), but kept Alive's girth. In Reflex's rest space against the stock jersey: upper arm 1.1 - 1.45 x as thick, forearm
1.2 - 1.25, shin 1.15 - 1.2, waist 1.1 - 1.25, shoulders 1.18 x as wide (all 157 suits share one mesh), and every one of the 16 boot
models hung 0.13 - 0.17 ft under Reflex's sole. On screen (the same frame shot with each outfit, `REFLEX_RXMOD_TWINS=1`) the Alive rider
covered 102 - 110 % of the stock rider's area at the gate. Now a suit and boots are also SHAPED to Reflex's rider (`RxGearRig.cs` "the
size", `ReflexRiderModel.RxGear.cs` `ShapedPositions`): the suit's outline is laid on the stock jersey's (nine segments of Reflex's rest
skeleton, steps along each and sectors around it, the outer radius per cell; scales 0.6 .. 1, never larger), each boot's box on the stock
boot's; measured from the meshes when a piece is first built (no constants, no re-import), written back through each vertex's blended
alignment so the skinning is unchanged. After: 96 - 106 % of the stock rider's area at the gate. Helmets, goggles and neck braces were
already sized by the head box. `REFLEX_RXGEAR_SHAPE=0` gives the old fit (tests). One path for the garage, the player, AI (B.R.A.A.P.)
and any other rider (`BuildRxGear`); stock Reflex gear is not touched.

## Playtest: bikes and parts (2026-11-11, after v1.1.0)
A tester's garage report (low-poly exhausts / tyres / wheels, no model detail, kits wrong, suspension parts that change nothing, no plate /
rim / hub colour). What the data says and what changed:
* **High-res garage models.** Alive has `HR_` databases for 26 of the 30 vehicles and for their exhausts, handlebars, tyres, wheels, kits
  and an ATV's bumpers, nerf bars, A-arms and shocks: one LOD with the cables, levers, bolts and hubs modelled and its own maps (Suzuki
  RMZ450: chassis 35,060 vertices against the race model's 9,322 LOD 0; stock exhaust 2,253 / 304; front wheel 18,082 / 1,918; the whole
  bike 69,340 / 13,288). The importer took the race models only (their LOD 0: it never took a lower LOD), which is what a race should
  draw and what the garage should not. Now each such bike also gets `<slug>-hr/` (`AliveBikeHr.cs`; RXMOD.md "High-res twin"), made by the
  same code from the HR twins of its packages, and the garage draws it. No HR in Alive: the 50cc, the MXFR 650, the ATV 1000, the MX250 FMX.
  The bike's own folder and its online token are unchanged (checked byte for byte); the 30 bikes grow from 0.39 GB to 1.9 GB on disk.
  Two importer faults the HR set showed: an HR exhaust package repeats the chassis' engine material, which made every kit and part look
  like a repaint of the engine and dropped the ATV's parts (`SameMaterial`); the HR frame's fork sticker and the HR ATV tyre's mud map
  were taken for the diffuse (`Roles`).
* **Colours** (RXMOD.md "Colours"). Alive's tables hold them: `CustomColor` (UserCustomizable: the rims, the hubs, an ATV's rims, frame,
  swing arm and A-arms), a 24-colour palette (`CustomDynamicColor`, UserSelectable) and the `NumberPlate` parts (Stock / Blue / Red /
  Yellow: `DynNumPlate`). They are now slots of the bike: NUMBER PLATE, RIM COLOR, HUB COLOR (ATV: RIM / FRAME / SWINGARM / A-ARM COLOR),
  painted at run time from the source maps the parts folder now carries. The palette rows have no names: the importer names them by hue.
  Not offered: the plastics' own colour (`UserDynColor`: it is baked per kit through the kit's alpha) and the number's font colour.
  A 125's green plate (Suzuki RM125, top view) is Alive's own default for that class (`DynNumPlate` 0, 0.58, 0.19), not a fault.
* **Kits.** Every kit of a bike shares its plastics' layout; the contact sheet (tests: page `alive-sheet`, `tests/alive_bikes/sheet.py`)
  shows them per vehicle. A kit that is mostly the bike's plain colour with small decals is that kit.
* **PERFORMANCE parts** (the Reflex vehicle's exhaust / tires / suspension on an Alive bike) bring tuning points and draw nothing, by
  design: the stage no longer tags them PREVIEW, it says PERFORMANCE ONLY - NOT DRAWN ON THIS BIKE.
* **Reflex tyres / wheels on an Alive bike**: not done. The complaint was the race model in the garage, which the HR set answers with
  Alive's own wheels. Drawing Reflex's instead means hiding the Alive wheel surfaces and fitting Reflex's hub, disc and sprocket to
  another fork and swing arm; whether that fits was not measured, so it is left as a possible option rather than guessed at.
Players must import again to get the HR sets and the colours (the parts folder is rewritten). The bike folder comes out byte for byte
the same for 29 of the 30 vehicles; the **KTM 250SXF**'s changes (and so its online token): its race frame material lists the fork
sticker `HR_MX_Fork` before its frame map `HR_MX_KTM_Frame`, the sticker (a black map) was taken for the frame's diffuse and the frame
was drawn black; it now takes the frame map. A player who has not imported again sees another player's KTM 250SXF as the original bike.

