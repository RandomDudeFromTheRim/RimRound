"""
Fur for RimRound bodies.

Vanilla fur genes (Furskin) and fur mods (Erin's Experiments' expies) only ship
textures for the vanilla body types, so RimRound used to hide fur entirely on its
own bodies. This redraws every RimRound body texture as a fur coat in the same
style as those textures: a light grey fill (the game tints it with the fur colour),
a thick black outline and little fur tufts - keeping RimRound's rolls and folds
visible through it.

Output mirrors the body folders:
    Textures/Things/Pawn/Humanlike/Bodies/Fur/<Style>/[BW/]Naked_<body>_<rot>.png
The FurDef patch (FurDef_GetFurBodyGraphicPath_ReturnTransparentForRR.cs) swaps
"Bodies/" for "Bodies/Fur/<Style>/" in the pawn's own body path.

    python furgen.py            generate everything
    python furgen.py preview    write a preview sheet only
"""
import os
import re
import sys
import zlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BODIES = os.path.join(ROOT, "Textures", "Things", "Pawn", "Humanlike", "Bodies")
OUT = os.path.join(BODIES, "Fur")

# world units per canvas = 1.5 x the body type's mesh size (RacialBodyTypeInfoUtility)
MESH = {"002": 1.0, "003": 1.0, "004": 1.0, "005": 0.875, "006": 1.125, "010": 1.25,
        "020": 1.375, "030": 1.375, "040": 1.375, "050": 1.375, "060": 2.75,
        "070": 2.5, "080": 5.5, "090": 5.25, "100": 7.5}

MAX_RES = 512  # fur is soft; the biggest bodies don't need 1024

STYLES = {
    # vanilla Biotech furskin: mid-grey fill, bold black V tufts
    "Furskin": dict(outline=0.046, fill=(0.58, 0.42), ink=0.30, rim=0.80,
                    tufts=14.0, tuft_len=0.1, tuft_ink=0.06, tuft_width=0.012, tuft_shape="v"),
    # Erin's expies: paler, smoother, with a few faint little w-tufts
    "Expie": dict(outline=0.052, fill=(0.64, 0.34), ink=0.45, rim=0.86,
                  tufts=5.0, tuft_len=0.06, tuft_ink=0.55, tuft_width=0.008, tuft_shape="w"),
}


def mesh_for(name):
    m = re.search(r"_(\d{3})a?_", name)
    return MESH.get(m.group(1), 1.0) if m else 1.0


def grow(mask_img, px):
    """Dilate (px > 0) or erode (px < 0) an L-mode mask by about |px| pixels."""
    f = ImageFilter.MaxFilter if px > 0 else ImageFilter.MinFilter
    px = abs(int(round(px)))
    while px > 0:
        step = min(px, 5)
        mask_img = mask_img.filter(f(2 * step + 1))
        px -= step
    return mask_img


def furify(src, style, mesh, rng):
    s = STYLES[style]
    im = src.convert("RGBA")
    n = im.width
    if n > MAX_RES:
        im = im.resize((MAX_RES, MAX_RES), Image.LANCZOS)
        n = MAX_RES
    arr = np.asarray(im, np.float32) / 255.0
    alpha = arr[..., 3]
    body = alpha > 0.5
    if body.sum() < 20:
        return None

    world = 1.5 * mesh
    px = n / world                      # pixels per world unit

    # shading: the body texture's own light and folds, normalised
    lum = arr[..., 0] * 0.3 + arr[..., 1] * 0.59 + arr[..., 2] * 0.11
    top = np.percentile(lum[body], 95) if body.any() else 1.0
    shade = np.clip(lum / max(top, 1e-3), 0, 1)
    lo, span = s["fill"]
    v = lo + span * shade
    # the fold lines stay as ink so the rolls still read
    v = np.where(shade < 0.45, v * s["ink"] + (1 - s["ink"]) * v * shade, v)

    mask = Image.fromarray((body * 255).astype(np.uint8), "L")
    # outline and tufts grow a little with the body, or the biggest ones read as speckle
    half = max(1.0, s["outline"] * mesh ** 0.3 * px / 2)
    inner = np.asarray(grow(mask, -half)) > 127
    near = np.asarray(grow(mask, -half * 2.2)) > 127
    outer = grow(mask, half)
    # a little darker toward the edge, like the vanilla coats
    v = np.where(body & ~near, v * s["rim"], v)
    v = np.where(inner, v, 0.05)        # the outline band

    grey = Image.fromarray((np.clip(v, 0, 1) * 255).astype(np.uint8), "L")

    # fur tufts, scattered by area so bigger bodies get more of them
    d = ImageDraw.Draw(grey)
    L = s["tuft_len"] * mesh ** 0.55 * px
    safe = np.asarray(grow(mask, -(half + L))) > 127
    ys, xs = np.nonzero(safe)
    area_world = body.sum() / (px * px)
    count = int(s["tufts"] * area_world / mesh ** 1.1)
    if len(xs) and count:
        width = max(1, int(round(s["tuft_width"] * px)))
        ink = int(s["tuft_ink"] * 255)
        for i in rng.choice(len(xs), size=min(count, len(xs)), replace=False):
            x, y = float(xs[i]), float(ys[i])
            ang = rng.uniform(-0.5, 0.5)
            ca, sa = np.cos(ang), np.sin(ang)
            def pt(dx, dy):
                return (x + dx * ca - dy * sa, y + dx * sa + dy * ca)
            if s["tuft_shape"] == "v":
                l = L * rng.uniform(0.7, 1.2)
                d.line([pt(-0.45 * l, -0.5 * l), pt(0, 0.5 * l), pt(0.15 * l, -0.35 * l)], fill=ink, width=width)
                if rng.random() < 0.5:
                    d.line([pt(0.15 * l, -0.35 * l), pt(0.35 * l, 0.15 * l), pt(0.55 * l, -0.45 * l)], fill=ink, width=width)
            else:
                l = L * rng.uniform(0.7, 1.1)
                d.line([pt(-0.5 * l, -0.3 * l), pt(-0.25 * l, 0.3 * l), pt(0, -0.1 * l),
                        pt(0.25 * l, 0.3 * l), pt(0.5 * l, -0.3 * l)], fill=ink, width=width)

    a = outer.filter(ImageFilter.GaussianBlur(0.6))
    out = Image.merge("RGBA", (grey, grey, grey, a))
    # keep tufts off the transparent area
    return out


def sources():
    for sub in ("", "BW"):
        folder = os.path.join(BODIES, sub)
        for f in sorted(os.listdir(folder)):
            if f.startswith("Naked_") and f.endswith(".png"):
                yield sub, f


def generate():
    total = 0
    for style in STYLES:
        for sub, f in sources():
            rng = np.random.default_rng(zlib.crc32(f"{style}/{sub}/{f}".encode()))
            src = Image.open(os.path.join(BODIES, sub, f))
            out = furify(src, style, mesh_for(f), rng)
            if out is None:
                continue
            dest = os.path.join(OUT, style, sub)
            os.makedirs(dest, exist_ok=True)
            out.save(os.path.join(dest, f), optimize=True)
            total += 1
    print("wrote", total)


def preview(path):
    names = ["Naked_F_010_Chubby", "Naked_F_040_Obese", "Naked_F_060_LardyAlt",
             "Naked_F_080_Gigantic", "Naked_F_100_Gelatinous", "Naked_M_040_Obese"]
    tints = {"Furskin": (0.55, 0.38, 0.25), "Expie": (0.92, 0.9, 0.95)}
    cell = 220
    rows = [(style, rot) for style in STYLES for rot in ("south", "east", "north")]
    sheet = Image.new("RGBA", (cell * (len(names) + 1), cell * len(rows)), (86, 128, 86, 255))
    for r, (style, rot) in enumerate(rows):
        for c, name in enumerate(names):
            sub = "BW" if os.path.exists(os.path.join(BODIES, "BW", f"{name}_{rot}.png")) else ""
            src = Image.open(os.path.join(BODIES, sub, f"{name}_{rot}.png"))
            out = furify(src, style, mesh_for(name), np.random.default_rng(c))
            tint = np.asarray(out, np.float32) / 255
            tint[..., :3] *= np.array(tints[style])
            img = Image.fromarray((tint * 255).astype(np.uint8), "RGBA").resize((cell, cell), Image.LANCZOS)
            sheet.alpha_composite(img, (c * cell, r * cell))
        # the mod's own texture for comparison
        if style == "Expie":
            ref = os.path.join(ROOT, "..", "..", "..", "..", "workshop", "content", "294100", "3683718165",
                               "Textures", "Things", "Pawn", "ERN_Expie", "Bodies", f"ERN_ExpieBody_{rot}.png")
        else:
            ref = rf"C:\Games\asr\biotec\Assets\data\biotech\textures\things\pawn\humanlike\bodies\FurCovered_Female_{rot}.png"
        if os.path.exists(ref):
            t = np.asarray(Image.open(ref).convert("RGBA"), np.float32) / 255
            t[..., :3] *= np.array(tints[style])
            img = Image.fromarray((t * 255).astype(np.uint8), "RGBA").resize((cell, cell), Image.LANCZOS)
            sheet.alpha_composite(img, (len(names) * cell, r * cell))
    sheet.save(path)
    print("preview", path)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "preview":
        preview(sys.argv[2] if len(sys.argv) > 2 else "fur_preview.png")
    else:
        generate()
