"""Generates the BulkPrint Manager icon, logo and installer artwork."""
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1]
NAVY = (20, 33, 61)
BLUE = (31, 95, 214)
BLUE_LIGHT = (74, 140, 245)
INTER = "/usr/share/fonts/opentype/inter/"

def font(weight, size):
    return ImageFont.truetype(INTER + f"Inter-{weight}.otf", size)

def gradient(size, top, bottom):
    w, h = size
    img = Image.new("RGB", size, top)
    px = img.load()
    for y in range(h):
        t = y / max(1, h - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    return img

def icon(size=1024):
    s = size
    base = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    # Rounded square with blue-to-navy gradient.
    grad = gradient((s, s), BLUE_LIGHT, NAVY).convert("RGBA")
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=255)
    base.paste(grad, (0, 0), mask)

    # Three stacked sheets of paper, fanned slightly.
    sheets = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(sheets)
    w, h = int(s * 0.46), int(s * 0.58)
    cx, cy = s // 2, int(s * 0.47)
    for i, (dx, dy, shade) in enumerate([(-0.10, 0.06, 205), (-0.05, 0.03, 228), (0.0, 0.0, 255)]):
        x0 = cx - w // 2 + int(dx * s) + int(0.04 * s)
        y0 = cy - h // 2 + int(dy * s)
        shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        ImageDraw.Draw(shadow).rounded_rectangle([x0 + 8, y0 + 14, x0 + w + 8, y0 + h + 14], radius=int(s * 0.03), fill=(0, 0, 0, 70))
        sheets = Image.alpha_composite(sheets, shadow.filter(ImageFilter.GaussianBlur(s * 0.012)))
        d = ImageDraw.Draw(sheets)
        d.rounded_rectangle([x0, y0, x0 + w, y0 + h], radius=int(s * 0.03), fill=(shade, shade, 255 if shade == 255 else shade + 15, 255))
        if i == 2:
            # Text lines on the front sheet.
            lx0, lx1 = x0 + int(w * 0.14), x0 + int(w * 0.86)
            ly = y0 + int(h * 0.16)
            for j in range(6):
                lw = lx1 if j % 3 != 2 else lx0 + int((lx1 - lx0) * 0.6)
                d.rounded_rectangle([lx0, ly, lw, ly + int(s * 0.022)], radius=int(s * 0.011), fill=(190, 205, 232, 255))
                ly += int(h * 0.105)
    base = Image.alpha_composite(base, sheets)

    # Badge: blue circle with a white check mark (ready to print).
    badge = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    bd = ImageDraw.Draw(badge)
    r = int(s * 0.15)
    bx, by = int(s * 0.70), int(s * 0.72)
    bd.ellipse([bx - r - 10, by - r - 10, bx + r + 10, by + r + 10], fill=(255, 255, 255, 255))
    bd.ellipse([bx - r, by - r, bx + r, by + r], fill=BLUE + (255,))
    lw = int(s * 0.034)
    bd.line([(bx - r * 0.45, by + r * 0.02), (bx - r * 0.1, by + r * 0.38), (bx + r * 0.5, by - r * 0.35)], fill=(255, 255, 255, 255), width=lw, joint="curve")
    base = Image.alpha_composite(base, badge)
    return base

big = icon(1024)
big.resize((256, 256), Image.LANCZOS).save(f"{OUT}/logo-256.png")
big.resize((512, 512), Image.LANCZOS).save(f"{OUT}/logo-512.png")
sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]
big.resize((256, 256), Image.LANCZOS).save(f"{OUT}/BulkPrintManager.ico", sizes=sizes)

def wizard_image(w, h, scale):
    img = gradient((w, h), (28, 74, 170), NAVY).convert("RGBA")
    logo = big.resize((int(96 * scale), int(96 * scale)), Image.LANCZOS)
    img.alpha_composite(logo, ((w - logo.width) // 2, int(46 * scale)))
    d = ImageDraw.Draw(img)
    def centered(text, y, f, fill):
        tw = d.textlength(text, font=f)
        d.text(((w - tw) / 2, y), text, font=f, fill=fill)
    centered("BulkPrint", int(160 * scale), font("Bold", int(22 * scale)), (255, 255, 255))
    centered("Manager", int(186 * scale), font("Bold", int(22 * scale)), (255, 255, 255))
    centered("Print hundreds of", int(222 * scale), font("Regular", int(11 * scale)), (205, 220, 245))
    centered("documents in one go", int(237 * scale), font("Regular", int(11 * scale)), (205, 220, 245))
    centered("Designed and Developed by", int(276 * scale), font("Regular", int(9.5 * scale)), (205, 220, 245))
    centered("Harish Vaishnav", int(290 * scale), font("SemiBold", int(11 * scale)), (255, 255, 255))
    return img.convert("RGB")

for scale, name in [(1, "wizard-image-164x314.bmp"), (1.25, "wizard-image-205x392.bmp"), (1.5, "wizard-image-246x471.bmp"), (2, "wizard-image-328x628.bmp")]:
    w, h = int(164 * scale), int(314 * scale)
    wizard_image(w, h, scale).save(f"{OUT}/{name}")

for px, name in [(55, "wizard-small-55.bmp"), (69, "wizard-small-69.bmp"), (83, "wizard-small-83.bmp"), (110, "wizard-small-110.bmp")]:
    bg = Image.new("RGBA", (px, px), (255, 255, 255, 255))
    bg.alpha_composite(big.resize((px, px), Image.LANCZOS))
    bg.convert("RGB").save(f"{OUT}/{name}")

# Preview sheet for review
sheet = Image.new("RGB", (900, 700), (246, 247, 250))
sheet.paste(big.resize((256, 256), Image.LANCZOS), (30, 30), big.resize((256, 256), Image.LANCZOS))
for i, sz in enumerate([16, 24, 32, 48, 64, 128]):
    im = big.resize((sz, sz), Image.LANCZOS)
    sheet.paste(im, (320 + sum([16, 24, 32, 48, 64, 128][:i]) + i * 16, 30), im)
wiz = Image.open(f"{OUT}/wizard-image-328x628.bmp")
sheet.paste(wiz, (540, 40))
sheet.save(f"{OUT}/../build/asset-preview.png")
print("assets written")
