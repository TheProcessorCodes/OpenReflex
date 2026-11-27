# tests/alive_engine/level/build.ps1 <scratch dir> -Asm <folder with Assembly-CSharp.dll and Reflex.OpenReflex.dll> [-Unity <editor Data folder>]
# Builds <scratch>\bin\AliveLevel.dll (Dump.cs) against the Unity assemblies that unity_compile.py builds, with the editor's bundled csc
# (.NET 8). Then (level.py runs the measurement on its output):
#   & "<Data>\NetCoreRuntime\dotnet.exe" "<scratch>\bin\AliveLevel.dll" --game <Reflex game files> --content <folder holding AliveEngineSounds> --out <dir>
#   python -I level.py <dir> [--ffmpeg F]
param([Parameter(Mandatory = $true)][string]$Scratch, [Parameter(Mandatory = $true)][string]$Asm, [string]$Unity = '')
$ErrorActionPreference = 'Stop'
if (-not $Unity) { $Unity = @('C:\Program Files\Unity\Hub\Editor\6000.6.1f1\Editor\Data', 'D:\Unity\Hub\Editor\6000.6.1f1\Editor\Data') | Where-Object { Test-Path "$_\DotNetSdk" } | Select-Object -First 1 }
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$U = $Unity
if (-not (Test-Path "$U\DotNetSdk")) { throw "No Unity editor at $U (pass -Unity <...\Editor\Data>)" }
$D = "$U\DotNetSdk"
$csc = (Get-ChildItem "$D\sdk\*\Roslyn\bincore\csc.dll" | Select-Object -First 1).FullName
$ref = (Get-ChildItem "$D\packs\Microsoft.NETCore.App.Ref" -Filter '8.*' | Select-Object -First 1).FullName + '\ref\net8.0'
$fw = (Get-ChildItem "$D\shared\Microsoft.NETCore.App" | Select-Object -First 1).Name
$bin = "$Scratch\bin"; New-Item -ItemType Directory -Force $bin | Out-Null
$rsp = @('-nologo', '-nostdlib+', '-target:exe', '-langversion:9', '-optimize+', '-nowarn:1701,1702')
Get-ChildItem "$ref\*.dll" | Where-Object { $_.Name -notlike 'System.*.Native.dll' } | ForEach-Object { $rsp += "-r:`"$($_.FullName)`"" }
$rsp += "-r:`"$Asm\Assembly-CSharp.dll`""; $rsp += "-r:`"$Asm\Reflex.OpenReflex.dll`""
$rsp += "-r:`"$U\Managed\UnityEngine\UnityEngine.CoreModule.dll`""
$rsp += "-out:`"$bin\AliveLevel.dll`""; $rsp += "`"$here\Dump.cs`""
$rsp | Set-Content -Encoding ascii "$Scratch\AliveLevel.rsp"
& "$U\NetCoreRuntime\dotnet.exe" exec $csc "@$Scratch\AliveLevel.rsp"
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
Copy-Item "$Asm\Assembly-CSharp.dll", "$Asm\Reflex.OpenReflex.dll" $bin -Force
Copy-Item "$U\Managed\UnityEngine\*.dll" $bin -Force
"{`"runtimeOptions`":{`"tfm`":`"net8.0`",`"framework`":{`"name`":`"Microsoft.NETCore.App`",`"version`":`"$fw`"}}}" | Set-Content -Encoding ascii "$bin\AliveLevel.runtimeconfig.json"
"$bin\AliveLevel.dll"
