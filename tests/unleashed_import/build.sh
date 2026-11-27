#!/bin/sh
# tests/unleashed_import/build.sh <scratch dir> -- builds the MX vs ATV Unleashed converter (terrain side: sources.txt) with Program.cs
# outside Unity, against .NET 8, as tests/untamed_convert/build.sh does:
#   macOS    Unity 6000.6.1f1's bundled csc and dotnet
#   Windows  (Git Bash) a Roslyn csc.exe (RX_CSC overrides the path) against the installed .NET 8 runtime (RX_DOTNET_SHARED)
# Language version 9, unsafe code off: what compiles here compiles in Unity 6. Output: <scratch>/bin/UnleashedImport.dll (path printed).
set -eu
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(cd "$HERE/../.." && pwd); S=${1:?scratch dir}
NAME=UnleashedImport
FLAGS="-nologo -nostdlib+ -target:exe -langversion:9 -optimize+ -nowarn:1701,1702,0618,0649,0169,0414"
mkdir -p "$S/bin"
LIST="$S/$NAME.sources"
: > "$LIST"
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
  FW=$(basename "$R")
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
  FW=$(ls "$D"/shared/Microsoft.NETCore.App | head -1)
  { echo "$FLAGS"
    for f in "$REF"/*.dll; do echo "-r:\"$f\""; done
    echo "-out:\"$S/bin/$NAME.dll\""
    while IFS= read -r f; do echo "\"$f\""; done < "$LIST"
  } > "$S/$NAME.rsp"
  "$D/dotnet" "$CSC" -noconfig "@$S/$NAME.rsp"
  ;;
esac
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"},"configProperties":{"System.GC.Server":false,"System.GC.Concurrent":false}}}\n' "$FW" > "$S/bin/$NAME.runtimeconfig.json"
echo "$S/bin/$NAME.dll"
