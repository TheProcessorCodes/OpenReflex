#!/usr/bin/env python3
"""zoom.py <run dir> <out png> <cam> <t0> [n=6] [step=1] [crop=0.5] -- a close look: n frames from t0, ORIGINAL over LOOSE, cropped tighter about the rider and enlarged (test only)."""
import csv, sys
from pathlib import Path
from PIL import Image, ImageDraw
run, out, cam, t0 = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3], float(sys.argv[4])
n = int(sys.argv[5]) if len(sys.argv) > 5 else 6; step = int(sys.argv[6]) if len(sys.argv) > 6 else 1; crop = float(sys.argv[7]) if len(sys.argv) > 7 else 0.5
F = run / "frames"
idx = [(int(r["index"]), float(r["t"])) for r in csv.DictReader((F / "index.csv").open())]
pick = [p for p in idx if p[1] >= t0 - 1e-3][::step][:n]
S = 560; sheet = Image.new("RGB", (n * S, 2 * S + 16), (18, 18, 18)); d = ImageDraw.Draw(sheet)
for k, (i, t) in enumerate(pick):
    for r, mode in enumerate(("original", "loose")):
        im = Image.open(F / f"f{i:04d}_{cam}_{mode}.jpg"); s = int(im.height * crop); x0 = (im.width - s) // 2; y0 = int((im.height - s) * 0.38)
        sheet.paste(im.crop((x0, y0, x0 + s, y0 + s)).resize((S, S), Image.LANCZOS), (k * S, r * S))
        d.text((k * S + 4, r * S + 4), f"{mode.upper()} t {t:.1f}", fill=(255, 255, 120))
sheet.save(out); print(out, sheet.size)
