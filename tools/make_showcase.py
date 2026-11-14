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
paths = sorted(glob.glob(os.path.join(src, "*")))
# an animated GIF wins over a still PNG of the same drawing (e.g. clown.gif and clown.png)
gifs = {os.path.splitext(os.path.basename(p))[0].lower() for p in paths if p.lower().endswith(".gif")}
for path in paths:
    if path.lower().endswith(".png") and os.path.splitext(os.path.basename(path))[0].lower() in gifs:
        print("skipped", os.path.basename(path), "(gif exists)"); continue
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
        if prev is not None and fr.tobytes() == prev.tobytes():
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


# ---- How To Play pages (pictures/how-to-play/*<number>.png -> howtoplay/page_<n>.png, in page order) ----
hsrc = os.path.join(root, "pictures", "how-to-play")
hout = os.path.join(root, "howtoplay")
os.makedirs(hout, exist_ok=True)
for f in glob.glob(os.path.join(hout, "*")):
    os.remove(f)
pages = []
for path in glob.glob(os.path.join(hsrc, "*.png")):
    m = re.search(r"(\d+)\.png$", path)
    if m: pages.append((int(m.group(1)), path))
pages.sort()
for i, (_, path) in enumerate(pages, 1):
    Image.open(path).convert("RGB").quantize(colors=64, method=Image.Quantize.MEDIANCUT).save(os.path.join(hout, f"page_{i}.png"), optimize=True)
with open(os.path.join(hout, "index.txt"), "w") as fh:
    fh.write(str(len(pages)) + "\n")
print("how to play:", len(pages), "pages")
