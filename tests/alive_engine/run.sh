#!/bin/sh
# tests/alive_engine/run.sh <scratch dir> list|convert|check ... SOURCE... -- builds and runs the Alive engine sound importer outside Unity
# (Program.cs: list the sets, convert --out CONTENT [--sets ...] [--ffmpeg F], check CONTENT [--twin CONTENT2]). The full test: convert all
# sets twice (two CONTENT folders), then check the first with --twin the second; exit 0 = ALIVE ENGINE PASS.
set -eu
HERE=$(cd "$(dirname "$0")" && pwd); S=${1:?scratch dir}; shift
D=/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/Resources/Scripting/DotNetSdk
"$HERE/build.sh" "$S" > /dev/null
DOTNET_CLI_TELEMETRY_OPTOUT=1 "$D/dotnet" "$S/bin/AliveEngine.dll" "$@"
