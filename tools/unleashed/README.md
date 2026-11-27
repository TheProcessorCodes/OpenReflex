# Keweni Falls import (work in progress)

Run from the Reflex repository:

```sh
python3 tools/unleashed/import_keweni.py '../MX vs ATV RE' /path/to/KeweniFalls.rxtrack
```

Requires NumPy, the local Open Unleashed reference parsers and its built macOS
`native/build/libmxacore.dylib`. Source archives are read only. The output path
must not exist. Track assets remain outside Git in `builds/experimental`.

The experimental reader now accepts 2-foot and 4-foot spacing on its existing
2048-square grid. Four-foot spacing preserves Nat17's full 5,760-foot terrain
without shrinking the track. Rendering, native terrain, course masks and grid
start placement use the same spacing. Existing 2-foot packages retain their
layout and behavior.

Current package: `builds/experimental/keweni-review/KeweniFalls.rxtrack`.
It contains terrain colour, 279 placed models, 39 model textures plus the
vegetation atlas, 5,226 vegetation billboards, 64 object collision chunks
(11,210 triangles), and the authored start chute and lap route.

Validation completed:
- Core built successfully (`builds/experimental/keweni-native-build.log`).
- Native package reader accepted 4-foot spacing and reset it to 2 after clear.
- Product collision probe verified all 11,210 triangles in the loaded world.
- 1,200-frame free-ride probe travelled 637 feet, changed 472 deformation
  samples, and restored all changed samples on reset.
- Python syntax and Git whitespace checks passed.

Unfinished; do not ship as a completed import:
- Unity app build succeeded with zero errors after stopping the stale licensing
  helper from the initial failed build. The new app loaded the full-size terrain,
  536 scenery meshes and 40 textures successfully; rendered views were captured
  under `builds/experimental/keweni-review/view.*.png`.
- The race starts, but the autopilot stalls at progress 0.198; completion and AI
  behavior are not verified. The route export uses the nearest existing point
  to the authored finish gate and must be checked in the player.
- Ecosystem collision and waterfall UV animation are not converted. Vegetation
  currently uses batched source billboards throughout, with no near-mesh LOD.
- Material conversion currently retains the first source texture pass; original
  multipass effects, vertex lighting and scene sky/fog still need fidelity work.

Earlier preview packages were moved out of `Mods/Tracks` into
`builds/experimental/keweni-review/earlier` so existing players do not list
packages requiring the new reader. The latest package is now installed as
`builds/experimental/Mods/Tracks/KeweniFalls.rxtrack`. The separate updated
player is `builds/experimental/KeweniFalls.app`; use
`builds/experimental/Launch-Keweni-Falls.command` to launch directly into it.
No existing player bundle was replaced.

## Desktop shortcut installation

The desktop `OpenReflex (experimental).app` shortcut resolves to
`builds/player/OpenReflex.app`. That app is now updated with the verified build;
the previous app is preserved in `builds/player/backups/keweni-menu-fix/`.
KeweniFalls.rxtrack is installed in its `Mods/Tracks` folder, which links to the
shared REFLEX-lead player Mods folder. Find it under Free Ride → Custom Tracks.

## Regular imported-library path

`import_library.py` supersedes the Keweni RXPK prototype for built-in content:

```sh
python3 tools/unleashed/import_library.py "/path/to/Open Unleashed checkout" "/path/to/original Reflex install" "/path/to/staging-library"
```

Optional `--track Nat17` (repeatable) limits conversion. The committed catalogue
contains all 64 Unleashed locations. The destination is staging, not the live
library. Check `unleashed-import-report.json`; failures remain explicit.

Set `REFLEX_UNLEASHED_STAGE` to that staging directory and run Unity's batch
method `Reflex.Port.EditorTools.UnleashedImportBuild.PackBatch` (or
`PackAndBuildMacBatch`). It runs the regular Reflex packer and read-back checks.
Install the resulting `Tracks/UNL_*.rxtrack` into the game library's `Tracks`
folder, never `Mods/Tracks`. See `core/plan/UNLEASHED_IMPORT.md` for the format and
current race and Free Ride scope. The client source picker is not yet implemented.

### Updating existing imports with Reflex visuals

`appearance.py SOURCE STAGING_LIBRARY` adds original source previews, optional map pictures, surface weights and authored route segments to all converted levels. `import_library.py` also runs this step for new conversions. Repack with `UnleashedImportBuild.PackBatch` after updating; all added files remain inside each regular RXAR track archive. The source color map is retained for surface classification and reimports, but is no longer rendered as the terrain texture. The renderer uses installed Reflex textures and bump/rut/deformation passes, and uses the standard enhanced sky and lighting path when Enhanced lighting is selected.

Each stock track has a reviewed `terrain_profile` in `tracks.json`. `terrain_profiles.json` defines its Reflex donor albedos, matching bumps, distant color means, tiling, and rut strength; `TERRAIN_REVIEW.md` records the preview-based assignments. `appearance.py` emits the selected `terrain-style.json` into the packed level. Adding a new track requires an explicit profile assignment, rather than silently using the generic dirt palette. Snow-covered ground and snowy mud are separate profiles; blue nighttime desert images are not classified as snow.

`upgrade_vegetation.py SOURCE STAGING_LIBRARY` upgrades existing imports to source 3D ecosystem models. It removes hidden challenge-only collision, writes shared mesh templates into the regular scenery asset, and emits `vegetation.json` for the nearby-model/distant-billboard renderer. New imports include this automatically. Repack the updated library afterward.

### Selective Reflex scenery

`scenery_profiles.json` explicitly maps source species to matching Reflex trees
and shrubs on seven venues. `reflex_scenery.py` copies their native forest, tree,
material, shader and texture resources into the level's regular packed assets.
`UnleashedForest` builds spatial clusters at the original snapped positions and
matches original plant height; Reflex's forest renderer supplies near geometry,
distant multi-angle billboards, normal maps and enhanced lighting. Unmatched
species retain their source 3D models. Reimports apply the same mapping.

Current selection: Valhalla and Calgary Lodge use Cheyenne's snowy spruce;
Goblin Gulch uses Gibson's green/dry desert broom. Autumn foliage keeps its
source models because the green Reflex birch was not a seasonal match. Selected tropical palms use Laguna palms, selected cacti use Gibson saguaros, and
additional snowy pines use Cheyenne spruce. Distinctive landmarks and collision-bearing
rocks retain their original models. New ecosystem collision is not generated.

### Racing and environment upgrades

Run `upgrade_racing.py SOURCE STAGING_LIBRARY` to rebuild
race sidecars; closed authored routes become regular native race events, while the
seven non-circuit venues remain Free Ride. `environment.py STAGING_LIBRARY BASE_LIBRARY` refreshes reviewed sky
and lighting metadata without replacing native terrain infrastructure.
`stadium_lights.py SOURCE STAGING_LIBRARY` exports source fixture positions for enhanced stadium shafts.
Rebuild scenery with `upgrade_vegetation.py SOURCE STAGING_LIBRARY` to apply source
visibility, baked vertex shading, correct alpha/additive materials and the `.toy` animated props
(`toys.py` -> `toys.rxtoys`: the ferris wheel, finish arches, windmills ...), then repack.
The same step writes `startgate.rxgate` (`startgate.py`) for supercross, national and supermoto levels and leaves the
source's own start gate out: those races start from Reflex's gate with the card girl and no numeric countdown.
SOURCE is the Open Unleashed source directory; BASE_LIBRARY is the extracted Reflex library. Never install loose files into Mods.
