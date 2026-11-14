import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
D = sys.argv[1]  # output folder
FONTS = sys.argv[2] if len(sys.argv) > 2 else "fonts"  # folder with RubikMonoOne.ttf + RubikBold.ttf (Google Fonts)
S = 1024
img = Image.new("RGB", (S, S), (8, 6, 8))
d = ImageDraw.Draw(img)
RED, BLUE, WHITE, ORANGE, GREY = (226, 38, 32), (52, 92, 255), (240, 236, 230), (255, 160, 20), (70, 60, 64)
W = 16  # line width at 1024 (≈4 px at 256)

def head(cx, cy, r, col, angry=False, peek=False):
    # REPO robot head: dome + visor band + two round eyes
    d.pieslice([cx - r, cy - r, cx + r, cy + r], 180, 360, outline=col, width=W)
    d.line([cx - r, cy, cx + r, cy], fill=col, width=W)
    band = int(r * 0.42)
    d.rounded_rectangle([cx - r, cy, cx + r, cy + band], radius=14, outline=col, width=W)
    er = int(r * 0.30)
    for ex in (cx - int(r * 0.42), cx + int(r * 0.30)):
        ey = cy - int(r * 0.36)
        d.ellipse([ex - er, ey - er, ex + er, ey + er], outline=col, width=W)
        pr = int(er * 0.42)
        px = ex + (int(er * 0.35) if peek else 0)
        d.ellipse([px - pr, ey - pr, px + pr, ey + pr], fill=col)
    if angry:
        # angry brows
        d.line([cx - int(r * 0.78), cy - int(r * 0.80), cx - int(r * 0.10), cy - int(r * 0.58)], fill=col, width=W)
        d.line([cx + int(r * 0.62), cy - int(r * 0.80), cx + int(r * 0.02), cy - int(r * 0.58)], fill=col, width=W)
        # teeth in the visor band
        n = 6
        x0, x1 = cx - int(r * 0.8), cx + int(r * 0.8)
        step = (x1 - x0) / n
        for i in range(n):
            a = x0 + i * step
            d.polygon([(a, cy + 8), (a + step, cy + 8), (a + step / 2, cy + band - 10)], outline=col, fill=None, width=6)

# background: faint red glow behind the hunter
glow = Image.new("RGB", (S, S), (0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse([120, 110, 640, 630], fill=(70, 10, 10))
glow = glow.filter(ImageFilter.GaussianBlur(90))
img = Image.blend(img, glow, 0.0)
img.paste(Image.composite(glow, img, Image.new("L", (S, S), 255)), (0, 0))
d = ImageDraw.Draw(img)

# crate the hider hides behind
cx0, cy0, cx1, cy1 = 600, 405, 930, 615
# hider peeking (drawn first, crate covers the lower part)
head(790, 405, 125, BLUE, peek=True)
d.rectangle([cx0, cy0, cx1, cy1], fill=(8, 6, 8))
d.rectangle([cx0, cy0, cx1, cy1], outline=ORANGE, width=W)
d.line([cx0, cy0, cx1, cy1], fill=ORANGE, width=10)
d.line([cx0, cy1, cx1, cy0], fill=ORANGE, width=10)

# hunter (big, red, angry)
head(355, 455, 225, RED, angry=True)

# text
fbig = ImageFont.truetype(FONTS + "/RubikMonoOne.ttf", 138)
fsm = ImageFont.truetype(FONTS + "/RubikMonoOne.ttf", 64)
fsub = ImageFont.truetype(FONTS + "/RubikBold.ttf", 60)
def ctext(y, text, font, fill):
    w = d.textlength(text, font=font)
    d.text(((S - w) / 2, y), text, font=font, fill=fill)
ctext(70, "R.E.P.O.", fsm, WHITE)
# "HIDE 'N SEEK" on two lines: HIDE white, 'N SEEK red
ctext(672, "HIDE’N", fbig, WHITE)
ctext(812, "SEEK", fbig, RED)
img.save(f"{D}/logo-1024.png")
img.resize((256, 256), Image.LANCZOS).save(f"{D}/icon-256.png")
