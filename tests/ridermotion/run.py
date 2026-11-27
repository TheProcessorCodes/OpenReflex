#!/usr/bin/env python3
"""run.py <RiderMotion.app> --plan plan.json --out <dir> [--seconds N] [--env K=V ...] -- one batch player run of the RIDER MOTION probe (test only).

-batchmode (no window, never the focus), muted (Options.MasterVolume 0 in the scratch save, REFLEX_MUTE), sandboxed like
tests/lighting_showcase (a bundle id of its own, a scratch save, writes to the user's preferences / application support denied).
The probe's frames and telemetry.csv go to <out>/frames."""
import argparse, json, os, plistlib, re, shutil, subprocess, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1])); import nofocus

R = Path(__file__).resolve().parents[2]
ap = argparse.ArgumentParser()
ap.add_argument("app", type=Path); ap.add_argument("--plan", type=Path, required=True); ap.add_argument("--out", type=Path, required=True)
ap.add_argument("--seconds", type=int, default=900); ap.add_argument("--game", default=str(R / "game files/Archive")); ap.add_argument("--env", action="append", default=[])
a = ap.parse_args()
A = a.out.resolve(); A.mkdir(parents=True)
app = A / "RiderMotion.app"; shutil.copytree(a.app, app, symlinks=True)
info = plistlib.loads((app / "Contents/Info.plist").read_bytes())
exe = app / "Contents/MacOS" / info["CFBundleExecutable"]; shipped = info["CFBundleIdentifier"]
info.update(CFBundleIdentifier="local.reflex.ridermotion.test-" + re.sub(r"[^a-z0-9]", "", A.name.lower()), CFBundleName="RiderMotion Test")
(app / "Contents/Info.plist").write_bytes(plistlib.dumps(info))
home = Path.home(); prefs = home / "Library/Preferences"; support = home / "Library/Application Support"
sb = "(version 1)\n(allow default)\n"
for d in sorted({shipped, "unity.DefaultCompany.Reflex", "unity.DefaultCompany.OpenReflex"}):
    sb += "(deny file-write* (subpath %s))\n" % json.dumps(str(support / d))
    for f in [prefs, prefs / "ByHost"]: sb += "(deny file-write* (regex %s))\n" % json.dumps("^" + re.escape(str(f / d)) + r"(\..*)?$")
for n in ["Reflex", "OpenReflex"]: sb += "(deny file-write* (subpath %s))\n" % json.dumps(str(support / "DefaultCompany" / n))
(A / "protection.sb").write_text(sb)
subprocess.run(["codesign", "--force", "--sign", "-", str(app)], check=True, capture_output=True)
save = A / "save"; save.mkdir(); (save / "ReflexCache").mkdir()
(save / "save.json").write_text(json.dumps({"format": "reflex-client-save", "version": 1, "ints": {}, "floats": {"Options.MasterVolume": 0.0}, "strings": {}}))
frames = A / "frames"; frames.mkdir(); log = A / "player.log"
env = {k: v for k, v in os.environ.items() if not k.startswith(("REFLEX_", "RX_", "DYLD_"))}
env.update(REFLEX_SIM="open", REFLEX_FRONTEND="open", REFLEX_SAVE_DIR=str(save), REFLEX_GAME_DIR=a.game, REFLEX_SKIP_INTRO="1", REFLEX_MUTE="1",
           REFLEX_FPS_CAP="240", REFLEX_RM_PLAN=str(a.plan.resolve()), REFLEX_RM_OUT=str(frames))
env.update(dict(kv.split("=", 1) for kv in a.env))
args = ["/usr/bin/sandbox-exec", "-f", str(A / "protection.sb"), str(exe), "-batchmode", "-reflexTestWindow", "-screen-fullscreen", "0",
        "-screen-width", "1280", "-screen-height", "720", "-logFile", str(log)]
with (A / "console.log").open("w") as clog:
    front = nofocus.frontmost()
    child = subprocess.Popen(args, cwd=R, env=env, stdout=clog, stderr=subprocess.STDOUT)
    nofocus.hand_back(front)
    try: code = child.wait(timeout=a.seconds)
    except subprocess.TimeoutExpired:
        child.terminate()
        try: code = child.wait(timeout=10)
        except subprocess.TimeoutExpired: child.kill(); code = child.wait()
        code = "timeout"
text = log.read_text(errors="replace") if log.exists() else ""
for l in text.splitlines():
    if "[rider-motion" in l or "error CS" in l: print(l[:500])
print("exit", code, "frames", len(list(frames.glob("*.jpg"))), A)
