#!/bin/sh
# tests/unleashed_import/run.sh <scratch dir> [track ids...]     (default tracks: Nat04 SX01 FS01; "all" = every track of tracks.json)
# The MX vs ATV Unleashed converter's terrain side (Content/UnleashedImport/Un*.cs, built outside Unity by build.sh) against the Python
# pipeline, byte for byte:
#   1. the data files the game ships (Assets/Resources/UnleashedImport/*.json) equal tools/unleashed/*.json
#   2. each track's .scn read by UnIni equals Python's configparser (strict=False, interpolation=None, allow_no_value=True)
#   3. every file UnConvert.ConvertLevel returns equals the reference library's: PASS, or DIFF with the first differing offset and sizes
# The reference library is <scratch>/pyref: tools/unleashed/import_library.py run fresh for the tracks it does not hold yet (python3
# with numpy, the Unleashed checkout's native/build/libmxacore.dylib). UNL_REFERENCE=<library> compares against that one instead and
# runs no Python conversion.
#   UNL_SOURCE   the Open Unleashed checkout (holds "Game Files/data", _re, native)   default: ../MX vs ATV RE beside the repo
#   UNL_REFLEX   the Reflex base library the levels are built on                     default: <repo>/game files/Archive
#   PYTHON       default python3
# Nothing outside <scratch> is written. Exit 0: UNLEASHED IMPORT TESTS PASS.
set -eu
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(cd "$HERE/../.." && pwd)
S=${1:?scratch dir}; shift
mkdir -p "$S"; S=$(cd "$S" && pwd)
SRC=${UNL_SOURCE:-"$(dirname "$ROOT")/MX vs ATV RE"}
BASE=${UNL_REFLEX:-"$ROOT/game files/Archive"}
PY=${PYTHON:-python3}
DATA="$ROOT/Unity/Reflex/Assets/Resources/UnleashedImport"
[ -d "$SRC/Game Files/data" ] || { echo "no Unleashed checkout at $SRC (set UNL_SOURCE)" >&2; exit 2; }
[ -f "$BASE/Database/NAT_Concord.dx9.level" ] || { echo "no Reflex base library at $BASE (set UNL_REFLEX)" >&2; exit 2; }
if [ $# -eq 0 ]; then set -- Nat04 SX01 FS01; fi
if [ "$1" = all ]; then
  # every catalogue track whose pak is there, as import_library.py chooses them
  set -- $("$PY" -c 'import json,sys,os;print(" ".join(e["id"] for e in json.load(open(sys.argv[1])) if os.path.isfile(os.path.join(sys.argv[2],e["id"]+".pak"))))' "$ROOT/tools/unleashed/tracks.json" "$SRC/Game Files/data")
fi
FAILS=0
for f in tracks terrain_profiles scenery_profiles; do
  if cmp -s "$DATA/$f.json" "$ROOT/tools/unleashed/$f.json"; then echo "ok    $f.json = tools/unleashed/$f.json"
  else echo "FAIL  Assets/Resources/UnleashedImport/$f.json differs from tools/unleashed/$f.json"; FAILS=$((FAILS + 1)); fi
done
sh "$HERE/build.sh" "$S" > /dev/null
case "$(uname -s)" in
MINGW*|MSYS*|CYGWIN*) DN=${RX_DOTNET:-"C:/Program Files/dotnet/dotnet.exe"} ;;
*) DN=/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet ;;
esac
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# 2. the .scn reader
"$DN" "$S/bin/UnleashedImport.dll" ini "$SRC/Game Files/data" "$@" > "$S/ini-cs.txt"
"$PY" - "$SRC" "$@" > "$S/ini-py.txt" <<'PYEOF'
import configparser, json, sys
sys.path.insert(0, sys.argv[1] + '/_re/formats')
import pak
for t in sys.argv[2:]:
    p = pak.Pak(sys.argv[1] + '/Game Files/data/' + t + '.pak')
    c = configparser.ConfigParser(strict=False, interpolation=None, allow_no_value=True)
    c.read_string(p.read('Data/Env/%s/%s.scn' % (t, t)).decode('latin1')); p.close()
    print(json.dumps({t: {s: dict(c[s].items()) for s in c.sections()}}))
PYEOF
if cmp -s "$S/ini-cs.txt" "$S/ini-py.txt"; then echo "ok    UnIni = configparser on $# scenes"
else echo "FAIL  UnIni differs from configparser ($S/ini-cs.txt, $S/ini-py.txt)"; FAILS=$((FAILS + 1)); fi
# 3. the files
if [ -n "${UNL_REFERENCE:-}" ]; then REF=$UNL_REFERENCE
else
  REF="$S/pyref"; NEED=""
  for t in "$@"; do [ -f "$REF/Assets/unleashed/UNL_$t/level.json" ] || NEED="$NEED --track $t"; done
  if [ -n "$NEED" ]; then
    echo "      converting with Python:$NEED"
    # shellcheck disable=SC2086
    nice -n 15 "$PY" "$ROOT/tools/unleashed/import_library.py" "$SRC" "$BASE" "$REF" $NEED > "$S/pyref.log" 2>&1 || { echo "FAIL  the Python pipeline failed ($S/pyref.log)"; FAILS=$((FAILS + 1)); }
  fi
fi
nice -n 15 "$DN" "$S/bin/UnleashedImport.dll" convert "$DATA" "$SRC/Game Files/data" "$BASE" "$REF" "$@" || FAILS=$((FAILS + 1))
if [ "$FAILS" -eq 0 ]; then echo "UNLEASHED IMPORT TESTS PASS"; else echo "UNLEASHED IMPORT TESTS FAIL ($FAILS)"; exit 1; fi
