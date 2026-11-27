#!/bin/sh
# tests/unleashed_import/scenery/run.sh <scratch dir> [track ids...]     (default tracks: Nat04 SX01 FS01; "all" = every track of tracks.json)
# The scenery side of the MX vs ATV Unleashed converter (UnScenery.Convert and what it calls: sources.txt, built outside Unity against
# .NET 8 as tests/untamed_convert/build.sh builds) against the Python pipeline, byte for byte. Per track it prints PASS or DIFF (first
# differing offset, differing byte count, both sizes) for:
#   scenery.rxscenery  collision.rxcollision  vegetation.json  scenery-fx.json  scenery-manifest.json  toys.rxtoys  funnel.rxfunnel
#   reflex-forest.bin  reflex-vegetation.json  reflex-resource-* (one line)  startgate.rxgate  stadium-lights.json
#   level.json's "scenery" (the report) and "start_gate"
# and first two self checks: the portable fused multiply-add against the runtime's, and the CPython set order the stadium lights need
# against Python's own (python3 writes the cases).
# The reference library is <scratch>/pyref: tools/unleashed/import_library.py run fresh for the tracks it does not hold yet (python3
# with numpy, the Unleashed checkout's native/build/libmxacore.dylib). UNL_REFERENCE=<library> compares against that one instead and
# runs no Python conversion. The level's terrain.rxheight and race.rxrace (the terrain side's files) are read from the reference.
#   UNL_SOURCE   the Open Unleashed checkout (holds "Game Files/data", _re, native)   default: ../MX vs ATV RE beside the repo
#   UNL_REFLEX   the Reflex library the donor vegetation is read from                 default: <repo>/game files/Archive
#   PYTHON       default python3
# Nothing outside <scratch> is written. Exit 0: UNLEASHED SCENERY TESTS PASS.
set -eu
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(cd "$HERE/../../.." && pwd)
S=${1:?scratch dir}; shift
mkdir -p "$S"; S=$(cd "$S" && pwd)
SRC=${UNL_SOURCE:-"$(dirname "$ROOT")/MX vs ATV RE"}
BASE=${UNL_REFLEX:-"$ROOT/game files/Archive"}
PY=${PYTHON:-python3}
[ -d "$SRC/Game Files/data" ] || { echo "no Unleashed checkout at $SRC (set UNL_SOURCE)" >&2; exit 2; }
[ -d "$BASE/Database" ] || { echo "no Reflex library at $BASE (set UNL_REFLEX)" >&2; exit 2; }
if [ $# -eq 0 ]; then set -- Nat04 SX01 FS01; fi
if [ "$1" = all ]; then
  set -- $("$PY" -c 'import json,sys,os;print(" ".join(e["id"] for e in json.load(open(sys.argv[1])) if os.path.isfile(os.path.join(sys.argv[2],e["id"]+".pak"))))' "$ROOT/tools/unleashed/tracks.json" "$SRC/Game Files/data")
fi
# ---- build (Unity's bundled csc and dotnet on macOS; a Roslyn csc.exe and the installed .NET 8 on Windows)
NAME=UnleashedScenery
FLAGS="-nologo -nostdlib+ -target:exe -langversion:9 -optimize+ -nowarn:1701,1702,0618,0649,0169,0414"
mkdir -p "$S/bin"
LIST="$S/$NAME.sources"; : > "$LIST"
tr -d '\r' < "$HERE/sources.txt" | while IFS= read -r line; do
  case "$line" in ''|'#'*) continue ;; esac
  [ -f "$ROOT/$line" ] || { echo "sources.txt: no such file: $line" >&2; exit 2; }
  printf '%s\n' "$ROOT/$line" >> "$LIST"
done
printf '%s\n' "$HERE/Program.cs" >> "$LIST"
case "$(uname -s)" in
MINGW*|MSYS*|CYGWIN*)
  CSC=${RX_CSC:-"C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe"}
  R=${RX_DOTNET_SHARED:-$(ls -d "C:/Program Files/dotnet/shared/Microsoft.NETCore.App"/8.* | tail -1)}
  FW=$(basename "$R"); DN=${RX_DOTNET:-"C:/Program Files/dotnet/dotnet.exe"}
  { echo "$FLAGS"
    for f in "$R"/*.dll; do
      case "$(basename "$f")" in *Native*) ;; System.*|Microsoft.CSharp.dll|Microsoft.VisualBasic*.dll|Microsoft.Win32.*|mscorlib.dll|netstandard.dll|WindowsBase.dll) echo "-r:\"$(cygpath -w "$f")\"";; esac
    done
    echo "-out:\"$(cygpath -w "$S/bin/$NAME.dll")\""
    while IFS= read -r f; do echo "\"$(cygpath -w "$f")\""; done < "$LIST"
  } > "$S/$NAME.rsp"
  "$CSC" -noconfig "@$(cygpath -w "$S/$NAME.rsp")"
  ;;
*)
  U=/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/Resources/Scripting; D=$U/DotNetSdk
  CSC=$(ls "$D"/sdk/*/Roslyn/bincore/csc.dll | head -1)
  REF=$(ls -d "$D"/packs/Microsoft.NETCore.App.Ref/8.*/ref/net8.0 | head -1)
  FW=$(ls "$D"/shared/Microsoft.NETCore.App | head -1); DN=$D/dotnet
  { echo "$FLAGS"
    for f in "$REF"/*.dll; do echo "-r:\"$f\""; done
    echo "-out:\"$S/bin/$NAME.dll\""
    while IFS= read -r f; do echo "\"$f\""; done < "$LIST"
  } > "$S/$NAME.rsp"
  "$DN" "$CSC" -noconfig "@$S/$NAME.rsp"
  ;;
esac
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"},"configProperties":{"System.GC.Server":false,"System.GC.Concurrent":false}}}\n' "$FW" > "$S/bin/$NAME.runtimeconfig.json"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# ---- the reference
if [ -n "${UNL_REFERENCE:-}" ]; then REFLIB=$UNL_REFERENCE
else
  REFLIB="$S/pyref"; NEED=""
  for t in "$@"; do [ -f "$REFLIB/Assets/unleashed/UNL_$t/level.json" ] || NEED="$NEED --track $t"; done
  if [ -n "$NEED" ]; then
    echo "converting the reference with Python:$NEED"
    # shellcheck disable=SC2086
    nice -n 15 "$PY" "$ROOT/tools/unleashed/import_library.py" "$SRC" "$BASE" "$REFLIB" $NEED > "$S/pyref.log" 2>&1 || { echo "the Python reference conversion failed: $S/pyref.log" >&2; }
  fi
fi
# ---- Python's own set order for random index sequences (the stadium lights walk a set of vertex indices)
VEC="$S/set-vectors.txt"
"$PY" -c '
import random
random.seed(7)
for case in range(400):
    n = random.choice([1, 3, 5, 8, 20, 60, 200, 1000, 5000]); top = random.choice([n, n * 3, 40, 65535])
    ins = [random.randrange(max(1, top)) for _ in range(n)] if case % 3 else list(range(n))
    print(" ".join(map(str, ins)) + "|" + " ".join(map(str, {j for j in ins})))
' > "$VEC" 2>/dev/null || rm -f "$VEC"
nice -n 15 "$DN" "$S/bin/$NAME.dll" "$REFLIB" "$SRC/Game Files/data" "$BASE" "$ROOT/tools/unleashed" --set-vectors "$VEC" "$@"
