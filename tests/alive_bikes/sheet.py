#!/usr/bin/env python3
"""sheet.py <screenshots dir> <out.png> [--suffix stock,kit1,kit2] [--crop x0,y0,x1,y1] [--cols N] -- lays the alive-sheet page's shots
(tests/menus_showcase/fixture/UiShowcaseAliveSheet.cs: as-<slug>-<suffix>.png) out as one contact sheet: a row per vehicle, a column per
suffix, each cell the garage stage's part of the shot with the vehicle's slug under it. TEST ONLY."""
import argparse, glob, os, re
from PIL import Image, ImageDraw
ap = argparse.ArgumentParser(); ap.add_argument("shots"); ap.add_argument("out")
ap.add_argument("--suffix", default="stock,kit1,kit2"); ap.add_argument("--crop", default="760,170,1830,820"); ap.add_argument("--cell", type=int, default=420)
ap.add_argument("--pairs", type=int, default=2, help="vehicles side by side in a row")
a = ap.parse_args()
suffixes = a.suffix.split(","); box = tuple(int(v) for v in a.crop.split(","))
slugs = sorted({re.match(r"as-(.*)-" + re.escape(suffixes[0]) + r"\.png$", os.path.basename(f)).group(1) for f in glob.glob(os.path.join(a.shots, "as-*-" + suffixes[0] + ".png"))},
               key=lambda s: int(re.match(r"alive-(\d+)", s).group(1)) if re.match(r"alive-(\d+)", s) else 0)
cw = a.cell; ch = int(cw * (box[3] - box[1]) / (box[2] - box[0])); label = 18
rows = (len(slugs) + a.pairs - 1) // a.pairs
sheet = Image.new("RGB", (cw * len(suffixes) * a.pairs, rows * (ch + label)), (16, 16, 16)); d = ImageDraw.Draw(sheet)
for i, slug in enumerate(slugs):
    x0 = (i % a.pairs) * cw * len(suffixes); y0 = (i // a.pairs) * (ch + label)
    for k, s in enumerate(suffixes):
        f = os.path.join(a.shots, "as-%s-%s.png" % (slug, s))
        if os.path.exists(f): sheet.paste(Image.open(f).convert("RGB").crop(box).resize((cw, ch), Image.LANCZOS), (x0 + k * cw, y0))
        d.text((x0 + k * cw + 4, y0 + ch + 2), slug.replace("alive-", "#") + "  " + s, fill=(230, 230, 230))
sheet.save(a.out); print(a.out, sheet.size, len(slugs), "vehicles")
