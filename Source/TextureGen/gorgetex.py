"""Procedural art for the Gorge World's flesh biomes (1.6/ExternalMods/GorgeWorld), in the
same manner as fleshtex.py: muted colours, big simple shapes lit from the top left, a thick
dark outline on things, nearly flat floors.

    python gorgetex.py        (needs numpy + pillow; writes straight into Textures/)

Floors (1024px, seamless):   Terrain/RR_FleshVeined, RR_ScarTissue, RR_Tallow, RR_GelatinMire
Liquid ramps (64px):         Terrain/RR_Blood(Deep)Ramp, RR_Tallow(Deep)Ramp, RR_Bile(Deep)Ramp
Plants (256px, 3 variants):  Things/Plant/GorgeWorld/<plant>/<plant>A..C
Tallow (128px):              Things/Item/Resource/RR_TallowLump
Gristle rock (atlas):        Things/Building/GorgeWorld/RR_Gristle_Atlas
World tiles (512px):         World/Biomes/RR_Fleshwood, RR_TallowMarsh, RR_MawWastes, RR_BloodSea
"""
import os

import numpy as np
from PIL import Image

from fleshtex import VIS, build_atlas, bulbs, lerp, periodic_noise, shaded, to_image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEX = os.path.join(ROOT, "Textures")
SS = 2
OUTLINE = (24, 10, 14)


def save(img, rel):
    path = os.path.join(TEX, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    print(rel)


def ridges(n, seed, octaves, width):
    """Thin seamless lines where a smooth noise crosses its middle: veins, creases."""
    v = periodic_noise(n, seed, octaves)
    return np.clip(1 - np.abs(v - 0.5) / width, 0, 1)


# ------------------------------------------------------------------ floors
def floor(base_lo, base_hi, seed, lines=None, pits=None, gloss=None, size=1024):
    mottle = periodic_noise(size, seed, ((2, 1.0), (3, 0.7), (5, 0.4), (9, 0.2)))
    fine = periodic_noise(size, seed + 1, ((20, 1.0), (31, 0.6)))
    rgb = lerp(base_lo, base_hi, mottle)
    rgb = rgb * (0.97 + 0.06 * fine)[..., None]
    if lines:
        col, octs, width, strength = lines
        rgb = lerp(rgb, col, ridges(size, seed + 2, octs, width) * strength)
    if pits:
        col, strength = pits
        p = periodic_noise(size, seed + 3, ((11, 1.0), (17, 0.7)))
        rgb = lerp(rgb, col, np.clip((p - 0.78) / 0.12, 0, 1) * strength)
    if gloss:
        col, strength = gloss
        g = periodic_noise(size, seed + 4, ((6, 1.0), (9, 0.6)))
        rgb = lerp(rgb, col, np.clip((g - 0.7) / 0.2, 0, 1) * strength)
    return to_image(rgb, np.ones(rgb.shape[:2], np.float32), size)


def floors():
    # living flesh with dark veins under the skin: richer ground, where things grow best
    save(floor((178, 126, 130), (198, 148, 150), 61, lines=((134, 62, 76), ((3, 1.0), (5, 0.6), (8, 0.3)), 0.018, 0.55)),
         "Terrain/RR_FleshVeined")
    # scar tissue: pale, dry, tight, with faint stretch lines
    save(floor((196, 168, 160), (214, 190, 180), 71, lines=((170, 132, 128), ((6, 1.0), (10, 0.5)), 0.03, 0.35)),
         "Terrain/RR_ScarTissue")
    # tallow: rendered fat, waxy and creamy, pocked
    save(floor((222, 202, 150), (236, 220, 172), 81, pits=((196, 170, 112), 0.5), gloss=((246, 236, 204), 0.35)),
         "Terrain/RR_Tallow")
    # gelatin mire: wet amber jelly with glossy lumps
    save(floor((184, 128, 92), (206, 152, 108), 91, gloss=((236, 196, 150), 0.55), pits=((140, 88, 64), 0.45)),
         "Terrain/RR_GelatinMire")


def ramp(rel, shallow, deep):
    """64x64 colour ramp for the Map/WaterDepth shader."""
    n = 64
    t = np.linspace(0, 1, n)[:, None, None] * np.ones((1, n, 1))
    col = np.array(shallow) * (1 - t) + np.array(deep) * t
    save(Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA"), rel)


def ramps():
    ramp("Terrain/RR_BloodRamp", (0.56, 0.12, 0.14), (0.44, 0.07, 0.10))
    ramp("Terrain/RR_BloodDeepRamp", (0.38, 0.05, 0.08), (0.22, 0.02, 0.05))
    ramp("Terrain/RR_TallowRamp", (0.94, 0.86, 0.60), (0.87, 0.77, 0.50))
    ramp("Terrain/RR_TallowDeepRamp", (0.80, 0.68, 0.40), (0.64, 0.52, 0.30))
    ramp("Terrain/RR_BileRamp", (0.64, 0.68, 0.24), (0.53, 0.57, 0.17))
    ramp("Terrain/RR_BileDeepRamp", (0.42, 0.46, 0.13), (0.29, 0.33, 0.08))


# ------------------------------------------------------------------ drawing kit
class Canvas:
    """Paints signed-distance shapes (in px, negative inside) back to front, each with a
    thick dark outline, onto a supersampled canvas. y grows downwards."""

    def __init__(self, size):
        self.size = size
        self.n = size * SS
        y, x = np.mgrid[0:self.n, 0:self.n].astype(np.float32)
        self.x, self.y = x / self.n, y / self.n       # 0..1 coordinates
        self.rgb = np.zeros((self.n, self.n, 3), np.float32)
        self.a = np.zeros((self.n, self.n), np.float32)
        self.px = 1.0 / self.n

    def shape(self, sdf, fill, outline=0.012, outline_rgb=OUTLINE):
        """sdf in 0..1 units; fill is an rgb array (n, n, 3) or a colour."""
        k = 1.0 / (1.5 * self.px)
        out = np.clip((outline - sdf) * k, 0, 1)
        inn = np.clip(-sdf * k, 0, 1)
        self.rgb = lerp(self.rgb, outline_rgb, out)
        fill = np.broadcast_to(np.asarray(fill, np.float32), self.rgb.shape)
        self.rgb = self.rgb * (1 - inn[..., None]) + fill * inn[..., None]
        self.a = np.maximum(self.a, out)

    def image(self):
        return to_image(self.rgb, self.a, self.size)


def circle(c, cx, cy, r):
    return np.sqrt((c.x - cx) ** 2 + (c.y - cy) ** 2) - r


def tapered(c, ax, ay, bx, by, w0, w1):
    abx, aby = bx - ax, by - ay
    t = np.clip(((c.x - ax) * abx + (c.y - ay) * aby) / max(abx * abx + aby * aby, 1e-9), 0, 1)
    d = np.sqrt((c.x - ax - t * abx) ** 2 + (c.y - ay - t * aby) ** 2)
    return d - (w0 + (w1 - w0) * t)


def curve(c, pts, w0, w1):
    """A tapering stroke along a polyline."""
    sdf = np.full(c.x.shape, 9.0, np.float32)
    k = len(pts) - 1
    for i in range(k):
        a, b = pts[i], pts[i + 1]
        sdf = np.minimum(sdf, tapered(c, a[0], a[1], b[0], b[1], w0 + (w1 - w0) * i / k, w0 + (w1 - w0) * (i + 1) / k))
    return sdf


def blob(c, lumps):
    """Several round lumps merged into one smooth mass (a soft minimum)."""
    k = 0.03
    total = np.zeros(c.x.shape, np.float32)
    for cx, cy, r in lumps:
        total += np.exp(-np.maximum(circle(c, cx, cy, r), -0.5) / k)
    return -k * np.log(np.maximum(total, 1e-12))


def lit(c, base, light, dark, cx, cy, r):
    """Round shading for a lump centred at (cx, cy): bright to the top left, dark below."""
    dx, dy = (c.x - cx) / max(r, 1e-6), (c.y - cy) / max(r, 1e-6)
    s = np.clip(-dy * 0.8 - dx * 0.35 + 0.15, -1, 1)
    rgb = lerp(base, light, np.clip(s, 0, 1))
    return lerp(rgb, dark, np.clip(-s, 0, 1))


def vertical(c, top, bottom, y0, y1):
    return lerp(top, bottom, np.clip((c.y - y0) / max(y1 - y0, 1e-6), 0, 1))


def eye(c, cx, cy, r, look, rng):
    c.shape(circle(c, cx, cy, r), lit(c, (232, 222, 214), (250, 246, 240), (184, 170, 166), cx, cy, r), 0.008)
    ix, iy = cx + look[0] * r * 0.35, cy + look[1] * r * 0.35
    iris = [(176, 60, 70), (200, 150, 60), (120, 150, 90), (110, 90, 170)][rng.integers(4)]
    c.shape(circle(c, ix, iy, r * 0.55), iris, 0.0)
    c.shape(circle(c, ix, iy, r * 0.25), (20, 8, 10), 0.0)
    c.shape(circle(c, cx - r * 0.35, cy - r * 0.4, r * 0.16), (255, 255, 255), 0.0)


FLESH = ((196, 128, 134), (228, 172, 172), (150, 84, 96))
SINEW = ((170, 76, 84), (206, 112, 116), (118, 44, 56))


# ------------------------------------------------------------------ plants
def sinew_tree(seed):
    """A tree of twisted red sinew holding up a lumpy crown of flesh, a few eyes open in it."""
    rng = np.random.default_rng(seed)
    c = Canvas(256)
    base_x = 0.5 + rng.uniform(-0.03, 0.03)
    top = (0.5 + rng.uniform(-0.06, 0.06), 0.42)
    for s in range(4):
        ph = rng.uniform(0, 6.3)
        pts = [(base_x + (s - 1.5) * 0.03 + 0.035 * np.sin(ph + t * 7) * (1 - t) + (top[0] - base_x) * t,
                0.97 - t * (0.97 - top[1])) for t in np.linspace(0, 1, 9)]
        c.shape(curve(c, pts, 0.034, 0.018), vertical(c, SINEW[1], SINEW[2], 0.4, 1.0), 0.008)
    lumps = []
    for _ in range(rng.integers(6, 9)):
        a, d = rng.uniform(0, 6.3), rng.uniform(0, 0.17)
        lumps.append((top[0] + np.cos(a) * d * 1.2, top[1] - 0.12 + np.sin(a) * d * 0.8, rng.uniform(0.09, 0.14)))
    cx = np.mean([l[0] for l in lumps])
    cy = np.mean([l[1] for l in lumps])
    crown = blob(c, lumps)
    c.shape(crown, lit(c, *FLESH, cx, cy, 0.25), 0.014)
    # a darker underside where the crown sags over the trunk
    c.rgb = lerp(c.rgb, FLESH[2], np.clip((c.y - cy - 0.04) / 0.08, 0, 1) * (crown < 0) * 0.45)
    for _ in range(rng.integers(2, 4)):
        lx, ly, lr = lumps[rng.integers(len(lumps))]
        eye(c, lx + rng.uniform(-0.03, 0.03), ly + rng.uniform(-0.03, 0.03), rng.uniform(0.025, 0.04), rng.uniform(-1, 1, 2), rng)
    return c.image()


def eyestalk(seed):
    rng = np.random.default_rng(seed)
    c = Canvas(256)
    for _ in range(rng.integers(1, 4)):
        bx = 0.5 + rng.uniform(-0.12, 0.12)
        h = rng.uniform(0.35, 0.6)
        lean = rng.uniform(-0.12, 0.12)
        pts = [(bx + lean * t * t, 0.96 - h * t) for t in np.linspace(0, 1, 7)]
        c.shape(curve(c, pts, 0.026, 0.016), vertical(c, FLESH[0], FLESH[2], 0.3, 1.0), 0.006)
        ex, ey = pts[-1]
        eye(c, ex, ey - 0.02, rng.uniform(0.05, 0.07), rng.uniform(-1, 1, 2), rng)
    return c.image()


def tendril_grass(seed):
    rng = np.random.default_rng(seed)
    c = Canvas(256)
    for _ in range(rng.integers(7, 11)):
        bx = 0.5 + rng.uniform(-0.16, 0.16)
        h = rng.uniform(0.3, 0.55)
        curl = rng.uniform(-0.18, 0.18)
        ph = rng.uniform(0, 6.3)
        pts = [(bx + curl * t * t + 0.02 * np.sin(ph + t * 9), 0.97 - h * t) for t in np.linspace(0, 1, 8)]
        tone = rng.uniform(0, 1)
        col = lerp(lerp(SINEW[1], FLESH[1], np.full(c.x.shape, tone)), SINEW[2], np.clip((c.y - 0.6) / 0.4, 0, 1))
        c.shape(curve(c, pts, 0.018, 0.005), col, 0.0045)
    return c.image()


def tallow_bulb(seed):
    """Fat sacs on a short stalk: pale, waxy, translucent at the edges."""
    rng = np.random.default_rng(seed)
    c = Canvas(256)
    pts = [(0.5, 0.97), (0.5 + rng.uniform(-0.04, 0.04), 0.7)]
    c.shape(curve(c, pts, 0.03, 0.022), vertical(c, (206, 176, 120), (150, 120, 80), 0.6, 1.0), 0.008)
    sacs = []
    for _ in range(rng.integers(2, 5)):
        sacs.append((0.5 + rng.uniform(-0.17, 0.17), rng.uniform(0.38, 0.66), rng.uniform(0.09, 0.15)))
    sacs.sort(key=lambda s: s[1])
    for sx, sy, sr in sacs:
        c.shape(circle(c, sx, sy, sr), lit(c, (232, 212, 156), (250, 240, 206), (190, 160, 104), sx, sy, sr), 0.012)
        c.shape(circle(c, sx - sr * 0.4, sy - sr * 0.45, sr * 0.18), (255, 252, 236), 0.0)
        vein = np.abs(circle(c, sx + sr * 0.3, sy + sr * 0.2, sr * 0.6)) < 0.003
        c.rgb = lerp(c.rgb, (204, 150, 110), vein * (circle(c, sx, sy, sr) < -0.01) * 0.7)
    return c.image()


def gelatin_reed(seed):
    rng = np.random.default_rng(seed)
    c = Canvas(256)
    for _ in range(rng.integers(5, 9)):
        bx = 0.5 + rng.uniform(-0.15, 0.15)
        h = rng.uniform(0.4, 0.75)
        lean = rng.uniform(-0.08, 0.08)
        pts = [(bx + lean * t, 0.97 - h * t) for t in np.linspace(0, 1, 5)]
        c.shape(curve(c, pts, 0.015, 0.009), lerp((222, 160, 104), (176, 108, 72), np.clip((c.y - 0.4) / 0.6, 0, 1)), 0.0045)
        tx, ty = pts[-1]
        r = rng.uniform(0.028, 0.045)
        c.shape(circle(c, tx, ty, r), lit(c, (226, 160, 104), (250, 214, 166), (170, 104, 70), tx, ty, r), 0.006)
    return c.image()


def gristle_shrub(seed):
    """A low knot of grey-pink cartilage, knobbed and spurred."""
    rng = np.random.default_rng(seed)
    c = Canvas(256)
    base = ((180, 156, 158), (214, 196, 196), (132, 108, 114))
    lumps = [(0.5 + rng.uniform(-0.15, 0.15), rng.uniform(0.76, 0.88), rng.uniform(0.06, 0.09)) for _ in range(rng.integers(4, 7))]
    c.shape(blob(c, lumps), lit(c, *base, 0.5, 0.8, 0.2), 0.012)
    # hooked spurs of cartilage growing up out of the knot
    for _ in range(rng.integers(4, 7)):
        bx, by = 0.5 + rng.uniform(-0.13, 0.13), rng.uniform(0.74, 0.82)
        a = rng.uniform(-2.5, -0.65)
        l = rng.uniform(0.16, 0.3)
        hook = rng.uniform(-0.06, 0.06)
        pts = [(bx + np.cos(a) * l * t + hook * t * t, by + np.sin(a) * l * t) for t in np.linspace(0, 1, 5)]
        c.shape(curve(c, pts, 0.024, 0.004), vertical(c, base[1], base[0], 0.45, 0.85), 0.006)
    # knuckles on top
    for _ in range(rng.integers(3, 5)):
        kx, ky, kr = 0.5 + rng.uniform(-0.12, 0.12), rng.uniform(0.74, 0.84), rng.uniform(0.022, 0.034)
        c.shape(circle(c, kx, ky, kr), lit(c, *base, kx, ky, kr), 0.008)
    return c.image()


def tallow_lump():
    """A little heap of soft, pale fat lumps."""
    c = Canvas(128)
    for x, y, r in ((0.38, 0.6, 0.17), (0.62, 0.62, 0.15), (0.5, 0.44, 0.16)):
        c.shape(circle(c, x, y, r), lit(c, (232, 212, 156), (250, 240, 206), (190, 160, 104), x, y, r), 0.03)
        c.shape(circle(c, x - r * 0.35, y - r * 0.4, r * 0.18), (255, 252, 236), 0.0)
    return c.image()


def gristle_atlas():
    """The Gorge World's rock: pale, rubbery cartilage in knuckled plates."""
    spec = [(12, 12, 13), (42, 10, 11), (28, 36, 14), (6, 44, 9), (52, 42, 12), (34, 58, 8)]
    base, light, dark = (204, 186, 188), (232, 220, 220), (158, 136, 144)

    def paint(x, y):
        cover, shade = bulbs(x, y, spec, VIS)
        rgb = shaded(base, light, dark, cover, shade, 0.8)
        seam = np.clip(1 - np.abs(cover - 0.5) * 6, 0, 1)
        return lerp(rgb, (150, 120, 128), seam * 0.6)
    return build_atlas(paint, margin=3.5, radius=9.0, wobble_seed=71, wobble_amp=3.0,
                       outline=(58, 36, 44), inner_band=(150, 126, 134))


PLANTS = {
    "RR_SinewTree": sinew_tree,
    "RR_Eyestalk": eyestalk,
    "RR_TendrilGrass": tendril_grass,
    "RR_TallowBulb": tallow_bulb,
    "RR_GelatinReed": gelatin_reed,
    "RR_GristleShrub": gristle_shrub,
}


def plants():
    for name, fn in PLANTS.items():
        for i, v in enumerate("ABC"):
            save(fn(sum(map(ord, name)) * 7 + i * 101), f"Things/Plant/GorgeWorld/{name}/{name}{v}")


# ------------------------------------------------------------------ world tiles
def world(rel, lo, hi, line_col, seed, spots=None):
    size = 512
    rgb = np.asarray(floor(lo, hi, seed, lines=(line_col, ((3, 1.0), (5, 0.6), (8, 0.3)), 0.03, 0.6), size=size))[..., :3].astype(np.float32)
    if spots:
        col, strength = spots
        p = periodic_noise(size, seed + 9, ((5, 1.0), (8, 0.6)))
        rgb = lerp(rgb, col, np.clip((p - 0.72) / 0.12, 0, 1) * strength)
    save(Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").convert("RGBA"), rel)


def worlds():
    world("World/Biomes/RR_Fleshwood", (164, 96, 104), (196, 132, 136), (110, 44, 58), 101, spots=((120, 30, 40), 0.5))
    world("World/Biomes/RR_TallowMarsh", (214, 190, 136), (232, 212, 162), (176, 140, 96), 111, spots=((190, 130, 90), 0.4))
    sea = periodic_noise(512, 131, ((2, 1.0), (3, 0.6), (5, 0.3), (9, 0.15)))
    swell = ridges(512, 132, ((3, 1.0), (5, 0.5)), 0.04)
    rgb = lerp((88, 12, 20), (118, 22, 30), sea)
    rgb = lerp(rgb, (140, 40, 46), swell * 0.35)
    save(Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").convert("RGBA"), "World/Biomes/RR_BloodSea")
    world("World/Biomes/RR_MawWastes", (190, 160, 152), (210, 184, 174), (150, 90, 96), 121, spots=((120, 50, 64), 0.6))


if __name__ == "__main__":
    floors()
    ramps()
    plants()
    save(tallow_lump(), "Things/Item/Resource/RR_TallowLump")
    save(gristle_atlas(), "Things/Building/GorgeWorld/RR_Gristle_Atlas")
    worlds()
