# tests/alive_engine/emitter/build.ps1 <scratch dir> -- builds the Alive engine emitter test (Windows): OriginalWorld/AliveEngineAudio.cs
# with the fake OriginalAudio of Stubs.cs, ReflexFmod.cs (FEV v0x40, FSB4 sync points), RxVehicleAudio.cs and the Alive importer's BXML
# reader, with Unity 6000.6.1f1's bundled csc against .NET 8 (UnityEngine.CoreModule for Vector3 / Mathf only; nothing native is called).
# Run: & "<Unity>\Editor\Data\NetCoreRuntime\dotnet.exe" exec <scratch>\bin\AliveEngineEmitter.dll <extracted engine audio> <work dir>
param([Parameter(Mandatory = $true)][string]$Scratch, [string]$Unity = '')
$ErrorActionPreference = 'Stop'
if (-not $Unity) { $Unity = @('C:\Program Files\Unity\Hub\Editor\6000.6.1f1\Editor\Data', 'D:\Unity\Hub\Editor\6000.6.1f1\Editor\Data') | Where-Object { Test-Path "$_\DotNetSdk" } | Select-Object -First 1 }
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path "$here\..\..\..").Path
$D = "$Unity\DotNetSdk"
$csc = (Get-ChildItem "$D\sdk\*\Roslyn\bincore\csc.dll" | Select-Object -First 1).FullName
$ref = (Get-ChildItem "$D\packs\Microsoft.NETCore.App.Ref" | Select-Object -First 1).FullName + '\ref\net8.0'
$fw = (Get-ChildItem "$D\shared\Microsoft.NETCore.App" | Select-Object -First 1).Name
$S = "$root\Unity\Reflex\Assets\Scripts"
$bin = "$Scratch\bin"; New-Item -ItemType Directory -Force $bin | Out-Null
$rsp = @('-nologo', '-noconfig', '-nostdlib+', '-target:exe', '-langversion:9', '-unsafe+', '-optimize+', '-nowarn:1701,1702,0618,0649,0169,0414')
Get-ChildItem "$ref\*.dll" | Where-Object { $_.Name -notlike 'System.*.Native.dll' } | ForEach-Object { $rsp += "-r:`"$($_.FullName)`"" }
$rsp += "-r:`"$Unity\Managed\UnityEngine\UnityEngine.CoreModule.dll`""
$rsp += "-out:`"$bin\AliveEngineEmitter.dll`""
$files = @("$S\OriginalWorld\AliveEngineAudio.cs", "$S\Content\ReflexFmod.cs", "$S\OpenReflex\RxVehicleAudio.cs", "$S\OpenReflex\RxNativeLoader.cs",
    "$S\Content\AliveImport\AliveBxml.cs", "$S\Content\AliveImport\AliveImportCore.cs", "$here\Stubs.cs", "$here\Program.cs")
foreach ($f in $files) { $rsp += "`"$f`"" }
$rsp | Set-Content -Encoding ascii "$Scratch\emitter.rsp"
& "$Unity\NetCoreRuntime\dotnet.exe" exec $csc "@$Scratch\emitter.rsp"
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
Copy-Item "$Unity\Managed\UnityEngine\UnityEngine.CoreModule.dll" $bin -Force
"{`"runtimeOptions`":{`"tfm`":`"net8.0`",`"framework`":{`"name`":`"Microsoft.NETCore.App`",`"version`":`"$fw`"}}}" | Set-Content -Encoding ascii "$bin\AliveEngineEmitter.runtimeconfig.json"
"built $bin\AliveEngineEmitter.dll"
