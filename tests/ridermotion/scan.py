#!/usr/bin/env python3
"""scan.py <run dir> -- the followed bike's motion per half second (speed, acceleration along the bike, yaw rate, vertical, the layer): to pick segments for sheet.py --seg (test only)."""
import csv, math, sys
from pathlib import Path
tel = [{k: float(v) for k, v in r.items()} for r in csv.DictReader((Path(sys.argv[1]) / "frames/telemetry.csv").open())]
W = 6; mo = []
for i in range(W, len(tel) - W):
    p, q, n = tel[i - W], tel[i], tel[i + W]; d1, d2 = q["t"] - p["t"], n["t"] - q["t"]
    if d1 <= 0 or d2 <= 0: continue
    v1 = [(q[k] - p[k]) / d1 for k in "xyz"]; v2 = [(n[k] - q[k]) / d2 for k in "xyz"]; acc = [(v2[j] - v1[j]) / ((d1 + d2) / 2) for j in range(3)]
    yaw = math.degrees(math.atan2(n["fx"], n["fz"]) - math.atan2(p["fx"], p["fz"])); yaw = (yaw + 180) % 360 - 180
    mo.append((q["t"], math.sqrt(sum(c * c for c in v2)), acc[0] * q["fx"] + acc[1] * q["fy"] + acc[2] * q["fz"], yaw / (d1 + d2), acc[1], q))
t = 0
while t < mo[-1][0]:
    w = [m for m in mo if t <= m[0] < t + 0.5]
    if w:
        a = lambda k: sum(m[k] for m in w) / len(w)
        air = sum(1 for m in w if m[4] < -22) / len(w)
        print(f"t {t:5.1f} speed {a(1):5.1f} along {a(2):6.1f} yaw {a(3):6.1f} vert {a(4):6.1f} air {air:.1f} lag ({sum(m[5]['lagx'] for m in w)/len(w):+.2f} {sum(m[5]['lagy'] for m in w)/len(w):+.2f} {sum(m[5]['lagz'] for m in w)/len(w):+.2f}) w {min(m[5]['weight'] for m in w):.2f} uy {min(m[5]['uy'] for m in w):.2f} chest {max(m[5]['chestmove'] for m in w):.2f} head {max(m[5]['headturn'] for m in w):.0f}")
    t += 0.5
