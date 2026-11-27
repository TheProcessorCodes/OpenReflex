# tests/alive_engine/build.ps1 <scratch dir> [-Unity <editor Data folder>] -- build.sh for Windows: builds <scratch>\bin\AliveEngine.dll with the
# Unity editor's bundled csc (Editor\Data\DotNetSdk) against .NET 8, the same sources as build.sh. Run it with the editor's dotnet:
#   & "<Data>\NetCoreRuntime\dotnet.exe" "<scratch>\bin\AliveEngine.dll" list|convert|check ... SOURCE...
param([Parameter(Mandatory = $true)][string]$Scratch, [string]$Unity = '')
$ErrorActionPreference = 'Stop'
if (-not $Unity) { $Unity = @('C:\Program Files\Unity\Hub\Editor\6000.6.1f1\Editor\Data', 'D:\Unity\Hub\Editor\6000.6.1f1\Editor\Data') | Where-Object { Test-Path "$_\DotNetSdk" } | Select-Object -First 1 }
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path "$here\..\..").Path
$U = $Unity
if (-not (Test-Path "$U\DotNetSdk")) { throw "No Unity editor at $U (pass -Unity <...\Editor\Data>)" }
$D = "$U\DotNetSdk"
$csc = (Get-ChildItem "$D\sdk\*\Roslyn\bincore\csc.dll" | Select-Object -First 1).FullName
$ref = (Get-ChildItem "$D\packs\Microsoft.NETCore.App.Ref" -Filter '8.*' | Select-Object -First 1).FullName + '\ref\net8.0'
$fw = (Get-ChildItem "$D\shared\Microsoft.NETCore.App" | Select-Object -First 1).Name
$C = "$root\Unity\Reflex\Assets\Scripts\Content"
$bin = "$Scratch\bin"; New-Item -ItemType Directory -Force $bin | Out-Null
$rsp = @('-nologo', '-noconfig', '-nostdlib+', '-target:exe', '-langversion:9', '-unsafe+', '-optimize+', '-nowarn:1701,1702,0618,0649,0169,0414')
Get-ChildItem "$ref\*.dll" | Where-Object { $_.Name -notlike 'System.*.Native.dll' } | ForEach-Object { $rsp += "-r:`"$($_.FullName)`"" }
$rsp += "-r:`"$U\Managed\UnityEngine\UnityEngine.CoreModule.dll`""
$rsp += "-r:`"$U\Managed\UnityEngine\UnityEngine.ScriptingModule.dll`""
$rsp += "-out:`"$bin\AliveEngine.dll`""
$files = @("$C\ReflexAssetImport.cs", "$C\ReflexAssetImport.Space.cs", "$C\ImportModScan.cs", "$C\ReflexStockDatabase.cs", "$C\RfxData.cs", "$C\Bxml.cs", "$C\LzxDecoder.cs",
           "$C\FresnoBRepair.cs", "$C\RxFiles.cs", "$C\ReflexDataPack.cs", "$C\..\OpenReflex\RxNativeLoader.cs", "$C\..\OpenReflex\RxArchiveNative.cs",
           "$C\ReflexPaths.cs", "$C\ReflexDrives.cs", "$C\ReflexMods.cs", "$C\ReflexFmod.cs", "$root\tests\importflow\Stubs.cs", "$here\Program.cs")
$files += (Get-ChildItem "$C\AliveImport\*.cs" | ForEach-Object { $_.FullName })
foreach ($f in $files) { $rsp += "`"$f`"" }
$rsp | Set-Content -Encoding ascii "$Scratch\AliveEngine.rsp"
& "$U\NetCoreRuntime\dotnet.exe" exec $csc "@$Scratch\AliveEngine.rsp"
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
Copy-Item "$U\Managed\UnityEngine\UnityEngine.CoreModule.dll" $bin -Force
"{`"runtimeOptions`":{`"tfm`":`"net8.0`",`"framework`":{`"name`":`"Microsoft.NETCore.App`",`"version`":`"$fw`"}}}" | Set-Content -Encoding ascii "$bin\AliveEngine.runtimeconfig.json"
"$bin\AliveEngine.dll"
