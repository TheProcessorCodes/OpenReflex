#!/usr/bin/env python3
"""build.py <abs out dir> -- builds the RIDER MOTION test player (test only, never shipped).

Stages a copy of Unity/Reflex (builds/ridermotion/stage/Reflex; the working tree is not modified; its Library is cloned from another stage
when missing), adds fixture/RiderMotionProbe.cs and builds <out>/RiderMotion.app with OpenReflexBuild.BuildMacBatch and core/build/librxcore.dylib."""
import os, re, shutil, subprocess, sys
from pathlib import Path

R = Path(__file__).resolve().parents[2]
UNITY = "/Applications/Unity/Hub/Editor/6000.6.1f1/Unity.app/Contents/MacOS/Unity"
out = Path(sys.argv[1]).resolve(); out.mkdir(parents=True, exist_ok=True)
stage = R / "builds/ridermotion/stage/Reflex"
core = R / "core/build/librxcore.dylib"
assert core.is_file(), "build the core first: core/build.sh"
stage.parent.mkdir(parents=True, exist_ok=True)
subprocess.run(["rsync", "-a", "--delete", "--exclude", "Temp/", "--exclude", "Logs/", "--exclude", "Builds/", "--exclude", "Library/",
                str(R / "Unity/Reflex") + "/", str(stage) + "/"], check=True)
if not (stage / "Library").is_dir():
    seeds = sorted((R / "builds/lighting").glob("stage-*/Reflex/Library"), key=lambda p: p.stat().st_mtime, reverse=True)
    if seeds: subprocess.run(["cp", "-c", "-R", str(seeds[0]), str(stage / "Library")], check=True); print("Library cloned from", seeds[0])
dest = stage / "Assets/Scripts/Platform/RiderMotionProbe"
if dest.exists(): shutil.rmtree(dest)
dest.mkdir(parents=True)
for f in sorted((Path(__file__).resolve().parent / "fixture").glob("*.cs")): shutil.copy2(f, dest / f.name)
app = out / "RiderMotion.app"
env = dict(os.environ, REFLEX_RXCORE_PATH=str(core), REFLEX_OPEN_PLAYER=str(app))
r = subprocess.run(["perl", "-e", "alarm shift; exec @ARGV", "7200", UNITY, "-batchmode", "-nographics", "-quit",
                    "-projectPath", str(stage), "-executeMethod", "Reflex.Port.EditorTools.OpenReflexBuild.BuildMacBatch",
                    "-logFile", str(out / "build.log")], env=env)
log = (out / "build.log").read_text(errors="replace") if (out / "build.log").exists() else ""
for e in sorted(set(re.findall(r".*(?:error CS\d+|Shader error).*", log)))[:60]: print(e)
ok = r.returncode == 0 and "[open-build] Succeeded" in log and app.is_dir()
print(("BUILT " if ok else "BUILD FAILED ") + str(app))
sys.exit(0 if ok else 1)
