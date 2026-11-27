#!/usr/bin/env python3
"""sheet.py <run dir> <out dir> [--name N] [--seg label:t0 ...] [--frames 16] -- contact sheets of a RIDER MOTION probe run (test only).

Reads <run>/frames (fNNNN_<cam>_<loose|original>.jpg, index.csv, telemetry.csv). Without --seg it picks the segments from the followed
bike's motion: the hardest braking, a jump (take-off to landing), the tightest corner, the roughest ground. Each sheet: the same frames in
rows of 8, ORIGINAL over LOOSE, cropped about the rider, with the time and the layer's lag (feet, bike frame) under each LOOSE tile."""
import argparse, csv, math, sys
from pathlib import Path
from PIL import Image, ImageDraw

ap = argparse.ArgumentParser()
ap.add_argument("run", type=Path); ap.add_argument("out", type=Path); ap.add_argument("--name", default="")
ap.add_argument("--seg", action="append", default=[]); ap.add_argument("--frames", type=int, default=16); ap.add_argument("--cams", default="side,rear")
ap.add_argument("--crop", type=float, default=0.8, help="share of the frame's height kept (a square about the centre)")
a = ap.parse_args()
F = a.run / "frames"
idx = [(int(r["index"]), float(r["t"])) for r in csv.DictReader((F / "index.csv").open())]
tel = [{k: float(v) for k, v in r.items()} for r in csv.DictReader((F / "telemetry.csv").open())]
a.out.mkdir(parents=True, exist_ok=True)

def at(t):   # the telemetry row nearest t
    return min(tel, key=lambda r: abs(r["t"] - t))

# the bike's motion per telemetry row: speed, acceleration along its nose, vertical acceleration, yaw rate
def motion():
    out = []
    W = 6
    for i in range(W, len(tel) - W):
        p, q, n = tel[i - W], tel[i], tel[i + W]
        dt1, dt2 = q["t"] - p["t"], n["t"] - q["t"]
        if dt1 <= 0 or dt2 <= 0: continue
        v1 = [(q[k] - p[k]) / dt1 for k in "xyz"]; v2 = [(n[k] - q[k]) / dt2 for k in "xyz"]
        acc = [(v2[j] - v1[j]) / ((dt1 + dt2) / 2) for j in range(3)]
        f = (q["fx"], q["fy"], q["fz"])
        yaw = math.degrees(math.atan2(n["fx"], n["fz"]) - math.atan2(p["fx"], p["fz"])); yaw = (yaw + 180) % 360 - 180
        out.append(dict(t=q["t"], speed=math.sqrt(sum(c * c for c in v2)), along=sum(acc[j] * f[j] for j in range(3)), vert=acc[1], yaw=yaw / (dt1 + dt2), vy=v2[1]))
    return out
mo = motion()
span = a.frames * 0.1
def window(score, taken):
    best = None
    ts = [m["t"] for m in mo]
    t = ts[0]
    while t + span < ts[-1]:
        w = [m for m in mo if t <= m["t"] < t + span]
        if w and all(abs(t - u) > span for u in taken):
            s = score(w)
            if best is None or s > best[0]: best = (s, t)
        t += 0.2
    return best
segs = []
for s in a.seg:
    label, t0 = s.split(":"); segs.append((label, float(t0), ""))
if not segs and mo:
    taken = []
    def add(label, score, note):
        b = window(score, taken)
        if b: taken.append(b[1]); segs.append((label, b[1], note % b[0]))
    add("braking", lambda w: -sorted(m["along"] for m in w)[len(w) // 4], "lower-quartile acceleration along the bike %.0f ft/s2 (negated)")
    add("jump", lambda w: sum(1 for m in w if m["vert"] < -22) / len(w) + (0.5 if max(m["vert"] for m in w) > 40 else 0), "airborne share + landing %.2f")
    add("corner", lambda w: abs(sum(m["yaw"] for m in w) / len(w)), "mean yaw rate %.0f deg/s")
    add("rough", lambda w: (sum(m["vert"] ** 2 for m in w) / len(w)) ** 0.5 - 30 * sum(1 for m in w if m["vert"] < -22) / len(w), "rms vertical acceleration (less the air) %.0f")

rows = []
for label, t0, note in segs:
    pick = [(i, t) for i, t in idx if t0 - 1e-3 <= t][:a.frames]
    if len(pick) < 4: continue
    for cam in a.cams.split(","):
        if not (F / f"f{pick[0][0]:04d}_{cam}_loose.jpg").exists(): continue
        tiles = []
        for i, t in pick:
            pair = []
            for mode in ("original", "loose"):
                im = Image.open(F / f"f{i:04d}_{cam}_{mode}.jpg")
                s = int(im.height * a.crop); x0 = (im.width - s) // 2; y0 = (im.height - s) // 2
                pair.append(im.crop((x0, y0, x0 + s, y0 + s)))
            tiles.append((t, pair))
        s = tiles[0][1][0].width; cols = 8; nrow = (len(tiles) + cols - 1) // cols; lab = 16
        sheet = Image.new("RGB", (cols * s + 70, nrow * (2 * s + lab) + 22), (18, 18, 18)); d = ImageDraw.Draw(sheet)
        d.text((4, 4), f"{a.name} {label} ({cam} camera) from t = {t0:.1f} s, 0.1 s steps. {note}", fill=(255, 255, 255))
        for k, (t, pair) in enumerate(tiles):
            r, c = divmod(k, cols); y = 22 + r * (2 * s + lab); x = 70 + c * s
            sheet.paste(pair[0], (x, y)); sheet.paste(pair[1], (x, y + s))
            q = at(t)
            d.text((x + 3, y + 2 * s + 2), f"t {t:.1f}  lag x{q['lagx']:+.2f} y{q['lagy']:+.2f} z{q['lagz']:+.2f}  w {q['weight']:.2f}", fill=(200, 200, 200))
            if c == 0: d.text((4, y + s // 2), "ORIGINAL", fill=(255, 220, 120)); d.text((4, y + s + s // 2), "LOOSE", fill=(120, 220, 255))
        f = a.out / f"{a.name}_{label}_{cam}.png"; sheet.save(f); print(f, sheet.size)
    rows.append((label, t0, note))
# the whole run's numbers
mx = lambda k: max(r[k] for r in tel)
print(f"telemetry {len(tel)} frames: hands max {mx('handerr'):.5f} ft, feet max {mx('footerr'):.5f} ft, hand / foot rotation max {mx('roterr'):.3f} deg, "
      f"arm extension max {mx('armmax'):.4f} (original {mx('armorig'):.4f}), leg {mx('legmax'):.4f} (original {mx('legorig'):.4f}), torso move max {mx('torsomove'):.3f} ft, riders with the layer {sum(r['loose'] for r in tel) / len(tel):.1f}")
for label, t0, note in rows: print(f"  {label}: t {t0:.1f} .. {t0 + span:.1f}  {note}")
