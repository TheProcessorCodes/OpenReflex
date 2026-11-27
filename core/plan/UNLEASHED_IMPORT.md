# Unleashed imported library (experimental)

Unleashed levels are built-in imported game content, not TrackMods. Each owns a
`UNL_<source id>` level and is listed by `UnleashedLevels`, which reads the normal
`RxFiles` overlay. `ReflexEvents` adds its Free Ride event and the front end gives
it the **MX vs ATV Unleashed** filter alongside Alive.

The source converter is `tools/unleashed/import_library.py`. It currently uses
the reference TRN, DXG, COL, ESB and TDF parsers and texture decoder from the
player's local Open Unleashed checkout. It requires Python/numpy and that
checkout's built `libmxacore`; it is not yet a standalone in-client PAK importer.
It never writes into the source checkout.

Per-level library paths:

- `Database/UNL_<id>.dx9.level`, `.database`, `.scene`
- `Assets/unleashed/UNL_<id>/level.json` (identity, name, source hash, conversion report)
- `terrain.rxheight`: RXM1 v1, 2048-square float heights in feet, spacing 2/4/8/16,
  start position and heading. Grid centered on the original terrain.
- `scenery.rxscenery`: RXSC v1 meshes, textures, material states, vegetation billboards.
- `collision.rxcollision`: RXCO v1 static triangle chunks.
- `colour.rxcolour`: RXCM v1, 2048-square RGBA terrain colour.

No RXPK is nested in an archive, and no custom-track mounting API is called.
The session loads physics assets through the library VFS before opening the
level. Portable terrain rendering/physics is shared with the experimental MTX
implementation; that shared code's historical `fmt_modtrack` name does not
classify imported levels as mods.

The level references the player's Reflex terrain material resources. Its BXML
database's optional `assetSource="NAT_Concord"` identifies the existing extracted
resource namespace while retaining a unique level identity. Readers accept only
bounded alphanumeric/underscore source names. The terrain and scenery geometry
come from the Unleashed assets, not the donor scene.

`UnleashedImportBuild.PackBatch` invokes **ReflexDataPack.Pack**, including its
read-back validation. Each invocation stages one level to create a self-contained
`Tracks/UNL_<id>.rxtrack`. This avoids overwriting an existing library's
`Assets.rxdata`. A later normal full library repack can deduplicate these tracks
into the library's shared pool. Track metadata records `source: unleashed` and
the per-level registry entry.

## Current limits

The 57 authored closed circuits expose regular race events with native lap gates,
AI paths and start chutes. FS09–FS13 and MC01/MC03 retain their original Free Ride
layouts by user choice. Source machine-challenge objectives are not ported.
Representative race verification covers Austin, Tucson and Keweni Falls, not every venue.
Scenery uses the first material pass and first frame of animated textures;
stadium render targets remain inactive displays. Ecosystem collision is pending; the
animated props are converted (see "Animated props" below). Missing references are recorded in conversion reports.

## Reflex appearance pass

`tools/unleashed/appearance.py` supplements each regular level archive with:
- The original UI preview (`preview.rximage`) and an optional source map (`map.rximage`), decoded from the player's UI.pak. RXIM v1 stores width, height and top-first RGBA pixels.
- `surface.rxsurface`: RXSM v1, 2048 square, RGBA weights. The source terrain's material IDs place Reflex grass, loose dirt and stone. Many original maps use ID 0 everywhere; those regions use source-color green detection and slope/desaturation to recover grass and cliff placement, with dirt as the fallback. Both native softness and rendering read this same map.
- `ruts.json`: independent source TDF route segments, with width from the authored left/right edges. These affect visual rut placement only, not race rules.

Unleashed ground no longer displays its baked source color map. It uses Reflex dirt, grass and slate textures, matching bump layers, course rut overlays and the native deformation reveal. Texture repetition and bump coverage account for each map's grid spacing. Levels without routes still receive base relief and live tire deformation.

Imported scenery uses raw Reflex albedo sampling, the Reflex sun/fill rig and fog, enhanced ambient occlusion and sky ambient, plus alpha-tested foliage shadows. Tree cards use rounded canopy normals to avoid black backs. The normal Reflex sky builder now runs after imported scenery; enhanced lighting selects the Concord partly-cloudy HDRI profile. Original/enhanced settings remain user-controlled. Animated waterfalls are not recreated. The source ecosystem model upgrade and selective Reflex donors below supersede the earlier billboard-only fallback.

### Track-specific terrain profiles

`tracks.json` now assigns every location an explicitly reviewed `terrain_profile`; `terrain_profiles.json` defines the four Reflex donor albedo/bump pairs, their decoded mean colors, physical texture scale and rut strength. `TERRAIN_REVIEW.md` records the 64-map review against source previews. `appearance.py` packs each selected profile as `terrain-style.json` alongside its surface map. The renderer preloads the referenced Reflex packages and uses the selected albedos, matching bumps and the same palette for distant terrain. Snow and dune profiles disable the old universal soil-grain treatment.

RXSM's fourth byte is 255 for snow-cover semantics, otherwise zero. This is interpreted only for regular imported library terrain; existing mod surface behavior stays unchanged. Snow retains soft native deformation, asphalt/rock remain hard, and snow route overlays are no longer suppressed by the vegetation mask. This profile selection supersedes the earlier universal Bedford dirt/Concord grass/slate palette.

### Source 3D ecosystem vegetation

The scenery converter now exports each declared ecosystem species' DXG mesh once into a hidden RXSC template group and emits `vegetation.json` with the original plant positions, height, and radius. Snap-flagged plants use the source terrain height, matching the source loader. Orphan ESBs that the SCN never references are not drawn. Existing placed-object collision is unchanged.

`UnleashedVegetation` shares those meshes/materials and pools scene objects only for nearby plants. Models blend to the existing source atlas billboards between 900 and 1100 map units (scaled with the map). Both passes use complementary distance dithering; nearby models receive the enhanced light rig and cast cutout shadows. The scenery shader supports GPU instancing. Unmatched trees use the source game's geometry, including its leaf cards. Ecosystem collision and wind remain outside this visual update.

### Reviewed Reflex vegetation donors

The regular importer applies `tools/unleashed/scenery_profiles.json` to selected
species. Native Reflex resources are stored flat as `reflex-resource-NAME.TYPE`
inside each level's normal archive, with `reflex-forest.bin` and
`reflex-vegetation.json`. Only UNL levels can resolve these overrides. Source
billboards and near instances are omitted for replaced species, avoiding doubled
foliage. Runtime clusters use source ground positions and original heights;
Reflex renders its own SpeedTree geometry, normals and LODs. Seven reviewed venues
currently replace 12,273 plants; unmatched species retain the source model path.

## Race and environment cleanup

`scene_rules.py` applies source visibility before export: inactive camera flashes,
challenge markers, hidden models, and duplicate empty-seat alternatives are omitted.
Challenge-only collision is removed together with its hidden object. Source vertex
colors preserve baked stadium shading. Additive glows and soft alpha blending retain
their original blend and alpha-test states instead of becoming hard cutouts.

`environment.py` records reviewed day, dusk, night and indoor profiles. Geometry
and native terrain descriptors must retain Concord infrastructure: swapping them to
Kingston changes native terrain size. `UnleashedEnvironment` instead loads only the
Reflex donor sky and lighting, with separate outdoor moonlight tuning.
`stadium_lights.py` locates source lamp surfaces and exports packed shaft positions
for the enhanced Reflex stadium renderer. Austin has 22 source-positioned fixtures.

Unleashed terrain uses smooth stochastic detail offsets shared by albedo and bump
sampling, plus broad color variation. Source ecosystem models remain 3D through
900 map units, blending to distance billboards by 1100; native Reflex donors retain
geometry to at least 600 and transition by 1000. Leaf cards within 3D models and
far-distance billboards remain intentional rendering optimizations.

Validation: source visibility and branching/point-to-point routing unit tests;
Austin native race start, lap and finish; Keweni Falls and Tucson native AI races (3/3 finish each).
The simple human test autopilot stalls on Keweni, while the actual native AI completes
it. Unity captures check stadium and outdoor night rendering on original terrain.

## Track audit and touch-up (2026-11-12, in progress)

Goal: every imported level has all the props the original draws, placed as the original places them, with the original's
materials; the ground keeps the Reflex textures chosen above (the project's deliberate update: not reverted).

Reference for "what the original draws": the source data itself, read by the rules of Open Unleashed's reverse-engineered
scene loader (`native/src/scene_api.c`, `TrackViewer.cs`, `MxaMaterialFx.cs`, `_re/notes/formats/dxm.md` in that checkout):
StaticModel always; ActivitySpecific / Waypoint only inside their activity; StartZone only in Free Ride (challenge start
markers); 3 of 4 `cameraflash` models never exist and the rest are hidden until the crowd's flash manager lights them;
`seatsNN` is hidden while `crowdNN` shows (full crowd detail); CollisionOnly has no render model; every `[ShaderPass_N]` of
a `.dxm` is its own draw with the template states of `DfltMT.dxm` (prelit, alpha test "> 0", two-sided, no blend).
No screenshots or video of the original were used: where a judgement rests on something else it says so.

### Tools (one command re-checks everything)

- `python3 tools/trackaudit/unleashed_audit.py "<Open Unleashed checkout>" <stage> [--drawn <dir>]` writes
  `builds/trackaudit/unleashed-after/SUMMARY.md` (+ `<id>/audit.json`): per level and per model the source instances, what
  the original shows on a ride, what the package holds and why anything is left out; every converted draw's vertices are
  compared with a transform rebuilt independently (Unity's `LookRotation(look, up)`, as the reference places models);
  material passes, alpha tests, animators and vertex colours are compared with the `.dxm` / `.dxg`; plants are counted
  against the `.esb`. Exit code 1 when anything is unexplained. `--drawn`: the client's own counts
  (`REFLEX_SCENERY_AUDIT=<dir>` makes `ExperimentalMtxScenery` write surfaces / triangles / textures / animated passes).
- `python3 tools/trackaudit/shoot.py <Lighting.app> <label> --game <library> --out builds/trackaudit/unleashed
  FreeRide/UNL_<id>=<id> ...`: seven fixed cameras per level (chase, ground, four quarter views 220 ft out, top), batch
  mode, muted scratch save, under the Unity lock. Shots: `builds/trackaudit/unleashed/<id>/{before,after}/screenshots/`.
- Working copies (never the earlier staged library): `builds/trackaudit/stage` (reconverted scenery), `library-before`
  and `library` (loose-file libraries for the player).

### Result over the 64 levels (after reconversion)

| | Before | After |
|---|---|---|
| Props: source [ModelN] instances / shown by the original on a ride / in the package | 94,656 / 11,284 / 11,284 | 94,656 / 11,284 / 11,283 (Killington's rain model, by design) |
| Plants (.esb instances) drawn as 3D model + distance card, or as a Reflex donor | 215,666 of 215,666 | same |
| Placement: converted draws checked / worst vertex error | 18,684 / 0.0001 ft | same (no draw misplaced; no mirrored or re-scaled model) |
| Models the original shows that the package lacks | 0 (the audit's first run flagged the 62 empty-seat twins; the original hides them too at full crowd detail) | 0 |
| Missing textures | 0 | 0 |
| Materials cut out at alpha 128 where the source only drops clear texels | 61 (holes in the medic mule, prize pick-up, Anaheim / Las Vegas buildings, Apophis' finish head, mezzanine boxes ...) | 0: each pass carries the source's own test (ref + func + enable) |
| Vertex colours | doubled and clamped at 255 (27 % of a stadium's baked vertices lost their highlights) | stored as authored (0x80 = 1.0), x2 in the material, unclamped |
| Second material passes (water + sky reflection, waterfalls, windows, the ocean wave, the aurora) | 17 materials dropped them | layers over the base pass |
| Animated passes | 156 materials static | 56 passes run (UV scroll of water / falls / ocean; the crowd's and fountains' texture frames); 103 race-driven lights (holeshot / leader / triple) stay on their first frame |
| Stadium screens (`&RenderTargetBuffer`, 31 levels) | grey panel | the screen's own still image (the original shows the live race camera) |
| Camera-following models | placed static where the .scn puts them | Killington's rain cylinder is not drawn (the weather draws rain); Oceanside's 59 light flares are camera-facing additive cards; the Northern Lights dome is replaced by OPEN REFLEX's own procedural aurora (`ReflexAurora.shader`, by the owner's request) |

Format additions (backward compatible; older packages read as before): `level.json` `material_rules: 2`; RXSC materials
with layers and pass colour 2; `scenery-fx.json` (UV / frame animators by material index and stage) and
`scenery-manifest.json` (placed draws, camera-following models, converter notes) beside the scenery. Client:
`ExperimentalMtxScenery` (exact alpha test, plants keep their 0.4 silhouettes, sky-flagged domes over Reflex's sky),
`UnleashedSceneryFx.cs`, `UnleashedAurora.cs`, `ReflexMtxScenery.shader` (`_BaseUvU/V`, `_L1UvU/V`).

### Known differences that remain (every level unless noted)

- Lighting: every scenery material is lit by Reflex's ENHANCED rig; the original lights only the `DXVDiffuse*` materials
  (ambient + sun) and draws `DXVPrelit` ones with their baked vertex colours alone. Kept: ENHANCED is the project's only
  lighting. The x2 vertex colour scale follows the data (authored around 0x80); the PC build modulates at 1x.
- Sky, fog and light colours come from the reviewed Reflex donor environment, not from the level's `.cub` sky cube and
  `[Fog]` / `[Light]` values. DECIDED by the owner 2026-11-12: keep the Reflex skies (the cubes are 512 px faces).
- Camera flashes in the stands (20,129 models over the stadium levels) are an animated crowd effect: not drawn.
- The 133 TV camera men stand as placed; the original turns them to the player. Start-zone markers and the objects of
  hill climbs / waypoint races / machine challenges belong to activities that are not ported.
- 9 bridge materials are culled CCW in the source and drawn two-sided here; `nat17_rock` uses its near material pass at
  all distances; ecosystem collision and wind are still not converted; no sun flare (`.toy`), no particle emitters (`.pco`).
- Killington rains permanently in the original (its camera-following rain model). OPEN: should UNL_MC01 default to RAIN?

Per-level tables (props by model, material notes, below-ground list): `builds/trackaudit/unleashed-after/SUMMARY.md`.

### Untamed pavement and concrete on the hard ground layer (owner's request, 2026-11-12)

"Use Untamed road textures: they look better on supermoto and anywhere concrete is." The hard layer (RXSM blue: pavement,
stone) of the `supermoto` profile (SM01-SM03) takes Untamed's `gf_airportpavement` (Greenfield, 512 px asphalt) and of the
`stadium` profile (31 levels) Untamed's `kp_concrete` (Kittery Point, 256 px). The disc's other road textures
(`cw_smpavement`, `cw_road`, `ds_asphalt01`, `cr_roadasphalt`) are road strips with painted lines: not tileable as ground.
`tools/unleashed/untamed_ground.py` (removed 2026-11-14 with the Untamed importer, whose texture extractor it needed) wrote
`ground-asphalt.rximage` / `ground-concrete.rximage` into the level and set the layer's `albedo`, `albedo_file`, `mean`,
`tile` in `terrain-style.json`; the client (`UnleashedLevels.LayerPicture`, `ReflexTerrainMaterial`) loads that picture in
place of the donor's albedo. The layer keeps its Reflex bump. Without the file a level keeps its Reflex texture; the
pictures come from the player's own Untamed disc (nothing shipped). Natural profiles keep Reflex rock on the hard layer.
Levels already staged keep their pictures; new ones cannot be generated any more.

### Shot review (2026-11-12)

Before / after shots of all 64 levels: `builds/trackaudit/unleashed/<id>/{before,after}/screenshots/`, contact sheets
`<id>/sheet.png`; the client's own counts agree with the packages on all 64 (`--drawn`). Levels ranked by how much the shots
changed; opened at full size or as sheets: SX19, SX13, SX03 (most changed), FS05, MC06, SX05, Nat17, Nat16, Nat04, MC01,
FS01, FS09, SM01. What the changes are: stadiums read brighter where the baked vertex colours exceed 0x80 (seats, the sunlit
tarps of SX13 turn from orange to pale yellow: the source's own baked sun; the albedo is clamped at white as the original's
frame buffer was), SX19's roof no longer has holes (its texture's alpha was cut at 128), crowds animate. Not yet looked at
one by one: the other 51 levels (their sheets differ little from before by the pixel ranking). Seen, not fixed: FS05 New
Orleans FMX and SX19 Raleigh start with the chase camera inside a ramp / the start gate.

### Animated props (2026-11-14)

The Oceanside ferris wheel was missing because no `.scn` lists a level's animated props: they are the `.toy`'s
`[AnimatedToys]` (Open Unleashed `native/src/scene_api.c` `toys_load`), each a model + motions under
`Data\Env\<track>\Anim\` (`.mcf` -> `.dxg`, `.mot`), placed by Position / LookVector / UpVector and playing
`InitialMotion`. The importer read only the `.scn`.

- `tools/unleashed/toys.py` (called by `scenery.py`): every toy's model split into its rigid parts (a vertex belongs to
  one node and is stored in that node's frame), written as hidden RXSC groups `UnleashedToy:<model>:<node>`; the motion
  sampled at 30 Hz with the game's own key math (`_re/tools/mot.py`) into `toys.rxtoys` (`RXTY` v1: per toy the looping
  flag, duration, initial time and samples x parts x position + rotation in level space). `level.json`
  `scenery.animatedProps` lists them. A level without toys has no `toys.rxtoys`.
- Client: `UnleashedToys.cs` (attached by `ReflexScenery` after the vegetation) copies the part groups per prop and
  poses them every frame; a looping motion wraps, another stops on its last pose; the clock stops with the game.
  `ExperimentalMtxScenery.MarkPlants` no longer treats the `UnleashedToy:` groups as plants.
- The 64 levels hold 50 toys in 26 levels: Nat04 ferris wheel (15 parts: the wheel and 14 chairs that stay upright);
  Nat05 4 courthouse signs, bank sign, 2 gallows doors, 2 school bells, 2 nooses; Nat10 mill wheel, tyre swing, rustic
  windmill; MC01 3 windmill blades; MC07 3 and MC08 7 score-tower people; the `THQFinishGate` finish arch of SX01-SX20,
  MC07, MC08 (22: placed, static - its motion moves only the undrawn fireworks nodes). 28 move, 22 stand.
- Not converted: the toys' collision (`HasCollision`: the wheel can be ridden through), their sounds, the
  `[BirdSplines]` flocks of Nat09 / Nat10 / Nat13 / Nat16, `.pco` physics props, particle emitters. An existing staged
  level gets its props by re-running `upgrade_vegetation.py SOURCE STAGE --track <id>` and repacking.

### Reflex start gate, card girl, no countdown (2026-11-15)

Before: an Unleashed race drew the source's own gate (static scenery), staged the riders in rows 32 ft down the start
chute and showed the numeric 3-2-1. Cause: the level's scene is the donor's (NAT_Concord) with everything but terrain,
light and sky filtered out (`fmt_scene.c`, `ReflexLevelContent.ReadScene`), so the race activity found no start board
(`+0x2cc` = 0) and ran Activity's own countdown, which the HUD shows (`rx_race_state.countdown_step`).

Now, for a level with `startgate.rxgate` (`tools/unleashed/startgate.py`: `RXSG` v1, the drop bar's place and the riding
direction; supercross and nationals: where the source's `THQsxgate` / `NatStartGate` drop bar stands, raised so the gate's
base clears the highest ground under it + 0.75 ft; supermoto has no source gate: 39 ft down the start chute):
- **Core** (`fmt_modtrack.c` `fmt_modtrack_kit_*`): the donor's `TO_StartingGate`, `DropGate` and `ThirtySecondBoard`
  ("30 seconds" girl) survive the scene filter and are moved as one rigid group to that pose (`fmt_entity_matrix`,
  `rs_matrix`; the board's `ShowSignPosition` too, snapped to the level's ground). The grid is then the gate's own 16
  slots (`rx_race_grid_from_gate` falls through to Reflex's path), the board is the activity's start board, the bar drops
  on "Countdown Step" 0 exactly as on Concord, and `countdown_step` stays -1: no number on the HUD.
- **Board sequence** (`race_activity.c` `rx_race_countdown`, PORT ADDITION): Reflex's race intro scenario normally steps
  the board (shown, then walking off with the hold timer running). Imported levels have no intro, so the Countdown state
  does it: shown 3 s, walks off, the board's own timer (4.07 s) starts the race: about 7.1 s from load to GO.
- **Client**: `UnleashedStartGate.cs` keeps the same three donor entities with the same transform
  (`SceneEntity.matrixOverride`); the gate and bar are drawn by the regular scenery path (`OriginalAnimatedObjects`
  swings the bar), the girl by `OriginalAmbient`.
- **Importer**: `scene_rules.hidden_models(scene, race_gate=True)` leaves the source gate models and their collision
  (`THQsxgate*`, `NatStartGate*`, `*dropgate*`) out of supercross / national / supermoto levels. Free Ride of those
  levels therefore has no gate at all (the Reflex kit is in the scene but a free ride shows the bar down, as Reflex does).
- Checked headless (`core/tools/alive_gate_test.c`, `tools/unleashed/validate_race.c`): UNL_Nat04 and UNL_SX01: 12 riders
  on the gate's slots, held (1.2 ft creep), GO at frame 437, 12 / 12 away; bar 0 -> 30.4 deg with the bounce in 12 frames
  from GO; no countdown step exported; the autopilot finishes a lap race on both. Renders: gate, riders, the girl with
  her board (Nat04), the dropped bar (Nat04, SX01).
- Not done: no crowd / pit people around the gate, no gate selection page, the girl's walk path is not checked against
  each level's obstacles, the gate sits on a flat base even where the start is sloped.

### Start bales: the funnel blockers (2026-11-15)

Source: the track's `.aty` `[FunnelBlockDown]` / `[FunnelBlockUp]` (SX01, SX03-SX20, SM03, MC07, MC08; SX02 has none): a
physics collection each (`DownTripleBale` / `UpTripleBale`, supermoto and the truck stadium `Down_SMbarrell` /
`UP_SMbarrell`), one trigger gate (position, look, width 100 or 200 ft) and a move (20 ft at 40 ft/s, down / up). The
`.scn` places the Down set standing and the Up set 19-20 ft under the ground: the start layout. A racer passing the gate
sinks the Down set and raises the Up set, which closes the lap off the start straight. (Open Unleashed
`_re/notes/pco_props.md`: in the original's decompile the movers exist but what fires them in a race was not found; its
port fires on the first pass of the gate, as here.) The bales have no `.scn` collision objects (they are physics props
with `<model>f.col`), so before this change no bale was solid, the Down set stood for the whole race and the Up set was
drawn 20 ft under the ground for ever.

- Importer (`tools/unleashed/funnel.py`, `scenery.py`; race levels only): the two sets leave the static scenery for the
  RXSC groups `UnleashedFunnel:down` / `UnleashedFunnel:up`; `funnel.rxfunnel` (`RXFN` v1) holds the gate, the moves and
  each set's collision triangles from `<model>f.col`.
- Core (`fmt_modtrack.c` reader, `build_collision.c` two switchable static shapes, `rx_session.c`
  `funnel_after_frame` / `rx_session_funnel`): start layout (Down solid) outside a race in progress; in one, the first
  vehicle whose step crosses the gate inside its width starts the move; nothing collides during the 0.5 s move; then the
  Up set is solid. A restart puts the start layout back. A free ride keeps the start layout (the original's free ride
  leaves the movers disarmed too).
- Client (`UnleashedFunnel.cs`, `RxApiNative.FunnelProgress`): the two groups follow the core's progress; a set fully
  under the ground is not drawn.
- Checked: probes (`validate_race.c` + `rx_session_funnel`): SX01 switch at race 39.7 s, SX10 19.5 s, SM03 44.7 s, the
  solid set 0 -> none -> 1, the autopilot finishes each race. Render SX01 from above: at the start 2 Down bales stand and
  the start straight is clear; after the switch they are gone and 3 Up bales stand across it.
- Not done: the bales are static once raised (the original's can be knocked about); the other loose bales
  (`TripleBale*`, `SingleBale*`, `TriangleBale*`) are still drawn but not solid; MC07 / MC08 are Free Ride only and keep
  their bales as static scenery.

### The in-game importer (2026-11-15)

GAME LIBRARY > MX vs ATV UNLEASHED converts the player's own Unleashed (PC) files, or an Open Unleashed install, into `Tracks/UNL_<id>.rxtrack`
of the game library. It is the Python pipeline of `tools/unleashed` (a fresh `import_library.py` run) in C#, byte for byte:

- **Converter** (`Content/UnleashedImport/Un*.cs`, engine-free): `UnPak`, `UnTrn`, `UnTdf`, `UnDxg`, `UnCol`, `UnEsb`, `UnMot`, `UnDxt` read the
  source; `UnTerrain`, `UnRace`, `UnDescriptors`, `UnAppearance` and `UnScenery` (+ `UnToys`, `UnStartGate`, `UnFunnel`, `UnStadiumLights`,
  `UnReflexScenery`) write a level; `UnConvert.ConvertLevel` is the entry. Its data (`tracks.json`, `terrain_profiles.json`,
  `scenery_profiles.json`) ships as `Resources/UnleashedImport/*.json`: copies of `tools/unleashed/*.json`, keep them equal.
- **Installer** (`UnleashedImporter.cs`) and screens (`OpenShell.UnleashedImport.cs`): scan, disk space check, per track convert, pack with
  `ReflexDataPack`, move into `Tracks`, refresh. A re-import replaces a track; there is no removal.
- **Tests**: `sh tests/unleashed_import/run.sh <scratch> all` (whole levels against fresh Python: 64 tracks, 1427 files, all equal) and
  `tests/unleashed_import/scenery/run.sh`. In the player: `REFLEX_UNLEASHED_IMPORT="<folder>|all"` imports without the screens and logs
  each level's content hash (64 of 64 in 196 s on this Mac, every hash equal to the Python reference's).
- **Not the same as the developer's touched-up levels**: the importer writes what current tools write. The 34 stadium and supermoto levels
  of the developer's install carry Untamed ground pictures (`ground-*.rximage`, from the deleted track audit tools) and `level.json` differs
  on all 64, so their content hashes differ; terrain, collision and race line are equal. The signed track list holds both.
- A change of the converter changes the hashes: rerun the test, hash the output (`tests/rxarchive/build.sh <scratch> tch`), sign and publish.
