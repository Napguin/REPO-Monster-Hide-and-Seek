import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
FONTS = sys.argv[1]; OUT = sys.argv[2]
Wd, Hd = 1800, 600
img = Image.new("RGB", (Wd, Hd), (8, 6, 8))
RED, BLUE, WHITE, ORANGE, GREY = (226, 38, 32), (52, 92, 255), (240, 236, 230), (255, 160, 20), (150, 140, 144)
LW = 12

# glows
glow = Image.new("RGB", (Wd, Hd), (0, 0, 0)); g = ImageDraw.Draw(glow)
g.ellipse([150, 60, 650, 560], fill=(80, 12, 12)); g.ellipse([1200, 140, 1650, 560], fill=(14, 22, 70))
glow = glow.filter(ImageFilter.GaussianBlur(110))
img = Image.composite(glow, img, Image.new("L", (Wd, Hd), 160)) if False else Image.blend(img, glow, 1.0)
d = ImageDraw.Draw(img)

def head(cx, cy, r, col, angry=False, peek=False):
    d.pieslice([cx - r, cy - r, cx + r, cy + r], 180, 360, outline=col, width=LW)
    band = int(r * 0.42)
    d.rounded_rectangle([cx - r, cy, cx + r, cy + band], radius=12, outline=col, width=LW)
    er = int(r * 0.30)
    for ex in (cx - int(r * 0.42), cx + int(r * 0.30)):
        ey = cy - int(r * 0.36)
        d.ellipse([ex - er, ey - er, ex + er, ey + er], outline=col, width=LW)
        pr = int(er * 0.42); px = ex + (int(er * 0.35) if peek else 0)
        d.ellipse([px - pr, ey - pr, px + pr, ey + pr], fill=col)
    if angry:
        d.line([cx - int(r * 0.78), cy - int(r * 0.80), cx - int(r * 0.10), cy - int(r * 0.58)], fill=col, width=LW)
        d.line([cx + int(r * 0.62), cy - int(r * 0.80), cx + int(r * 0.02), cy - int(r * 0.58)], fill=col, width=LW)
        n = 6; x0, x1 = cx - int(r * 0.8), cx + int(r * 0.8); step = (x1 - x0) / n
        for i in range(n):
            a = x0 + i * step
            d.polygon([(a, cy + 6), (a + step, cy + 6), (a + step / 2, cy + band - 8)], outline=col, width=5)

# hunter left
head(400, 340, 190, RED, angry=True)
# hider right, peeking over a crate
head(1510, 330, 105, BLUE, peek=True)
cx0, cy0, cx1, cy1 = 1360, 330, 1660, 510
d.rectangle([cx0, cy0, cx1, cy1], fill=(8, 6, 14))
d.rectangle([cx0, cy0, cx1, cy1], outline=ORANGE, width=LW)
d.line([cx0, cy0, cx1, cy1], fill=ORANGE, width=8); d.line([cx0, cy1, cx1, cy0], fill=ORANGE, width=8)

# text in the middle
fbig = ImageFont.truetype(f"{FONTS}/RubikMonoOne.ttf", 104)
fsm = ImageFont.truetype(f"{FONTS}/RubikMonoOne.ttf", 46)
fsub = ImageFont.truetype(f"{FONTS}/RubikBold.ttf", 40)
def ctext(x, y, text, font, fill):
    w = d.textlength(text, font=font); d.text((x - w / 2, y), text, font=font, fill=fill)
mid = 935
ctext(mid, 120, "R.E.P.O.", fsm, WHITE)
ctext(mid, 190, "HIDE’N", fbig, WHITE)
ctext(mid, 310, "SEEK", fbig, RED)
ctext(mid, 450, "MONSTER MODE  ·  CLASSIC MODE", fsub, GREY)
img.save(OUT)
