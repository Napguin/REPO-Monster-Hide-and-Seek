"""Turns the drawings in pictures/monster-showcase (GIF / PNG) into single PNG frames + an index for the
in-game Monster Showcase. Output: showcase/ (embedded into the DLL by the .csproj).
Run:  python3 tools/make_showcase.py   (needs Pillow)"""
import glob, os, re, sys
from PIL import Image

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src = os.path.join(root, "pictures", "monster-showcase")
out = os.path.join(root, "showcase")
os.makedirs(out, exist_ok=True)
for f in glob.glob(os.path.join(out, "*")):
    os.remove(f)

lines = []
for path in sorted(glob.glob(os.path.join(src, "*"))):
    name = os.path.basename(path)
    m = re.match(r"monster_rulz_(\d+)_(.+)\.(gif|png)$", name, re.I)
    if not m:
        print("skipped", name); continue
    key = m.group(2).lower().replace("-", "").replace(" ", "")
    im = Image.open(path)
    n = getattr(im, "n_frames", 1)
    durs = []
    prev = None
    for i in range(n):
        im.seek(i)
        fr = im.convert("RGB")
        d = im.info.get("duration", 100) or 100
        if prev is not None and list(fr.getdata()) == list(prev.getdata()):
            durs[-1] += d   # identical frame: just show the previous one longer
            continue
        prev = fr
        idx = len(durs)
        # palette PNG: the drawings are line art on black, this keeps the files small
        fr.quantize(colors=64, method=Image.Quantize.MEDIANCUT).save(os.path.join(out, f"{key}_{idx}.png"), optimize=True)
        durs.append(d if n > 1 else 0)
    lines.append(f"{key}|{len(durs)}|{','.join(str(d) for d in durs)}")
    print(key, len(durs), "frames")

with open(os.path.join(out, "index.txt"), "w") as fh:
    fh.write("\n".join(lines) + "\n")
