#!/bin/sh
# tests/alive_engine/emitter/build.sh <scratch dir> -- builds the Alive engine emitter test (macOS; build.ps1 on Windows): OriginalWorld/
# AliveEngineAudio.cs with the fake OriginalAudio of Stubs.cs, ReflexFmod.cs (FEV v0x40, FSB4 sync points), RxVehicleAudio.cs and the
# Alive importer's BXML reader, with Unity 6000.6.1f1's bundled csc against .NET 8 (UnityEngine.CoreModule for Vector3 / Mathf only).
# Run: "$D/dotnet" <scratch>/bin/AliveEngineEmitter.dll <extracted engine audio folder> <work folder>
set -eu
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(cd "$HERE/../../.." && pwd); S=${1:?scratch dir}
U=/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/Resources/Scripting; D=$U/DotNetSdk
CSC=$(ls "$D"/sdk/*/Roslyn/bincore/csc.dll | head -1)
REF=$(ls -d "$D"/packs/Microsoft.NETCore.App.Ref/8.*/ref/net8.0 | head -1)
FW=$(ls "$D"/shared/Microsoft.NETCore.App | head -1)
A="$ROOT/Unity/Reflex/Assets/Scripts"
mkdir -p "$S/bin"
"$D/dotnet" "$CSC" -nologo -noconfig -nostdlib+ -target:exe -langversion:9 -unsafe+ -optimize+ -nowarn:1701,1702,0618,0649,0169,0414 $(for f in "$REF"/*.dll; do printf -- '-r:%s ' "$f"; done) \
  -r:"$U/Managed/UnityEngine/UnityEngine.CoreModule.dll" -out:"$S/bin/AliveEngineEmitter.dll" \
  "$A/OriginalWorld/AliveEngineAudio.cs" "$A/Content/ReflexFmod.cs" "$A/OpenReflex/RxVehicleAudio.cs" "$A/OpenReflex/RxNativeLoader.cs" \
  "$A/Content/AliveImport/AliveBxml.cs" "$A/Content/AliveImport/AliveImportCore.cs" "$HERE/Stubs.cs" "$HERE/Program.cs"
cp "$U/Managed/UnityEngine/UnityEngine.CoreModule.dll" "$S/bin/"
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}\n' "$FW" > "$S/bin/AliveEngineEmitter.runtimeconfig.json"
echo "$S/bin/AliveEngineEmitter.dll"
