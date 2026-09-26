"""Procedural textures for RimRound's flesh dimension, in RimWorld's art style:
muted colours, a few big simple shapes with soft light-from-above shading,
and a thick dark outline - no busy surface noise.

Linked wall atlases follow RimWorld's MaterialAtlasPool layout: a 4x4 grid of
cells; cell i = Up(1)|Right(2)|Down(4)|Left(8) sits at column i%4, UV row i//4
(image row 3 - i//4, since UV v=0 is the image bottom). Only the central 3/4 of
each cell is shown (1/8 padding per side is bleed). Everything inside a wall is
periodic per tile, so linked neighbours meet seamlessly.

usage: python3 fleshtex.py <out dir>   (needs numpy + pillow)
Outputs go to Textures/Things/Building/FleshDimension/ and Textures/Terrain/.
"""
import numpy as np
from PIL import Image

OUT = 2          # output scale: 2 -> 640px atlases (160px cells), like vanilla fleshmass
SS = 2           # supersampling for anti-aliasing
U = OUT * SS     # pixels per "design pixel" (design units: 60 per visible tile)
CELL, VIS, PAD = 80 * U, 60 * U, 10 * U
TAU = 2 * np.pi
LOW = ((1, 1.0), (2, 0.6), (3, 0.3))


# ------------------------------------------------------------------ helpers
def periodic_noise(n, seed, octaves=((2, 1.0), (4, 0.5), (7, 0.3))):
    """Smooth noise that tiles every n px: a sum of integer-frequency waves."""
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    out = np.zeros((n, n), np.float32)
    for freq, amp in octaves:
        for _ in range(6):
            fx, fy = rng.integers(-freq, freq + 1, 2)
            if fx or fy:
                out += amp * np.sin(TAU * (fx * x + fy * y) / n + rng.uniform(0, TAU))
    out -= out.min()
    return out / max(out.max(), 1e-6)


def lerp(a, b, t):
    t = np.clip(t, 0, 1)[..., None]
    return np.asarray(a, np.float32) * (1 - t) + np.asarray(b, np.float32) * t


def rounded_box_sdf(x, y, x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    hx, hy = (x1 - x0) / 2 - r, (y1 - y0) / 2 - r
    qx, qy = np.abs(x - cx) - hx, np.abs(y - cy) - hy
    return (np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2)
            + np.minimum(np.maximum(qx, qy), 0) - r)


def torus_delta(a, b, period):
    return (a - b + period / 2) % period - period / 2


def bulbs(x, y, spec, period):
    """Paint periodic sphere-like bulbs. spec: list of (cx, cy, r) in design px.
    Returns (coverage 0..1, shade -1..1 light-from-above) of the top-most bulb."""
    cover = np.zeros(x.shape, np.float32)
    shade = np.zeros(x.shape, np.float32)
    for cx, cy, r in spec:
        dx = torus_delta(x, cx * U, period) / (r * U)
        dy = torus_delta(y, cy * U, period) / (r * U)
        d = np.sqrt(dx * dx + dy * dy)
        c = np.clip((1 - d) * r * U / 1.5, 0, 1)            # anti-aliased disc
        s = np.clip(-dy * 0.9 - dx * 0.25 - d * 0.35 + 0.2, -1, 1)  # lit from the upper left
        shade = np.where(c > 0, shade * (1 - c) + s * c, shade)
        cover = np.maximum(cover, c)
    return cover, shade


def to_image(rgb, alpha, size):
    arr = np.concatenate([np.clip(rgb, 0, 255), (np.clip(alpha, 0, 1) * 255)[..., None]], -1)
    return Image.fromarray(arr.astype(np.uint8), 'RGBA').resize((size, size), Image.LANCZOS)


# ------------------------------------------------------------ linked walls
def build_atlas(paint, margin=5.0, radius=12.0, wobble_seed=1, wobble_amp=4.0,
                outline=(24, 10, 14), inner_band=(0, 0, 0), band_strength=0.35):
    wob = periodic_noise(VIS, wobble_seed, LOW)
    atlas_rgb = np.zeros((4 * CELL, 4 * CELL, 3), np.float32)
    atlas_a = np.zeros((4 * CELL, 4 * CELL), np.float32)
    y, x = np.mgrid[0:CELL, 0:CELL].astype(np.float32) - PAD
    for i in range(16):
        far = 40 * U
        x0 = -far if i & 8 else margin * U
        x1 = VIS + far if i & 2 else VIS - margin * U
        y0 = -far if i & 1 else margin * U
        y1 = VIS + far if i & 4 else VIS - margin * U
        w = wob[np.round(y).astype(int) % VIS, np.round(x).astype(int) % VIS]
        d = rounded_box_sdf(x, y, x0, y0, x1, y1, radius * U) + wobble_amp * U * (w - 0.5)
        depth = -d / U                                        # design px inside the edge
        rgb = paint(x, y)
        # darker band just inside the outline, like vanilla fleshmass
        rgb = lerp(rgb, inner_band, np.clip(1 - (depth - 3.0) / 4.0, 0, 1) * band_strength)
        rgb = lerp(rgb, outline, np.clip(3.2 - depth, 0, 1))  # thick outline
        col, row = i % 4, 3 - i // 4
        atlas_rgb[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL] = rgb
        atlas_a[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL] = np.clip(-d + 0.5, 0, 1)
    return to_image(atlas_rgb, atlas_a, 4 * CELL // SS)


def shaded(base, light, dark, cover, shade, strength=1.0):
    """Base fill with bulbs shaded between dark and light."""
    rgb = lerp(base, light, np.clip(shade, 0, 1) * cover * strength)
    return lerp(rgb, dark, np.clip(-shade, 0, 1) * cover * strength)


def flesh_wall():
    spec = [(10, 12, 11), (34, 8, 9), (52, 26, 12), (22, 36, 13), (46, 50, 10), (6, 52, 8), (30, 58, 7)]
    base, light, dark = (196, 128, 134), (228, 172, 172), (150, 84, 96)

    def paint(x, y):
        cover, shade = bulbs(x, y, spec, VIS)
        return shaded(base, light, dark, cover, shade)
    return build_atlas(paint, wobble_seed=16, inner_band=(120, 56, 70))


def hardened_flesh():
    spec = [(14, 14, 14), (44, 12, 12), (30, 40, 15), (6, 46, 10), (54, 44, 11)]
    base, light, dark = (132, 88, 88), (168, 124, 116), (92, 56, 60)

    def paint(x, y):
        cover, shade = bulbs(x, y, spec, VIS)
        rgb = shaded(base, light, dark, cover, shade, 0.8)
        # plate seams: a thin dark line where the bulbs meet the base
        seam = np.clip(1 - np.abs(cover - 0.5) * 6, 0, 1)
        return lerp(rgb, (70, 36, 40), seam * 0.8)
    return build_atlas(paint, margin=3.5, radius=8.0, wobble_seed=24, wobble_amp=2.5,
                       outline=(20, 10, 12), inner_band=(70, 40, 42))


def void_vein():
    body = [(12, 14, 12), (46, 18, 11), (28, 44, 13), (54, 50, 9)]
    nodes = [(30, 18, 4.5), (10, 40, 3.5), (48, 36, 4), (40, 56, 3)]
    base, light, dark = (168, 118, 164), (204, 162, 198), (122, 78, 124)

    def paint(x, y):
        cover, shade = bulbs(x, y, body, VIS)
        rgb = shaded(base, light, dark, cover, shade)
        ncov, nshade = bulbs(x, y, nodes, VIS)
        glow = lerp((188, 120, 236), (252, 232, 255), np.clip(nshade + 0.3, 0, 1))
        rgb = lerp(rgb, (84, 40, 96), np.clip(bulbs(x, y, [(a, b, r + 1.2) for a, b, r in nodes], VIS)[0] - ncov, 0, 1))
        return lerp(rgb, glow, ncov)
    return build_atlas(paint, wobble_seed=33, inner_band=(104, 60, 110))


# ------------------------------------------------------------------- floor
def flesh_floor(size=1024):
    """Nearly flat and low-contrast, so pawns and items read clearly on it."""
    mottle = periodic_noise(size, 41, ((2, 1.0), (3, 0.7), (5, 0.4), (9, 0.2)))
    fine = periodic_noise(size, 42, ((20, 1.0), (31, 0.6)))
    folds = periodic_noise(size, 43, ((3, 1.0), (5, 0.5)))
    rgb = lerp((180, 132, 136), (200, 152, 154), mottle)
    rgb = rgb * (0.97 + 0.06 * fine)[..., None]
    fold = np.clip(1 - np.abs(folds - 0.5) * 220 / 3.0, 0, 1)
    rgb = lerp(rgb, (160, 110, 118), fold * 0.35)
    return to_image(rgb, np.ones(rgb.shape[:2], np.float32), size)


# ------------------------------------------------------------------ geyser
def bloat_geyser(size=256):
    """A puckered flesh vent: big flat shapes, thick outline, a glowing throat."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    dx, dy = (x - n / 2) / n, (y - n / 2) / n
    r, ang = np.sqrt(dx * dx + dy * dy), np.arctan2(dy, dx)
    wob = periodic_noise(n, 51, ((2, 1.0), (3, 0.6)))
    rr = r + 0.05 * (wob - 0.5)
    outer, rim, throat = 0.44, 0.22, 0.1
    alpha = np.clip((outer - rr) * n / 1.5, 0, 1)
    # mound: lit from above, one step darker toward the bottom
    mound = lerp((196, 128, 134), (226, 170, 170), np.clip(-dy * 3 + 0.3, 0, 1))
    mound = lerp(mound, (150, 84, 96), np.clip(dy * 3 - 0.2, 0, 1))
    # the puckered ring: a darker band with a few chunky folds
    ring = np.clip(1 - np.abs(rr - rim * 0.8) / (rim * 0.45), 0, 1)
    folds = (np.cos(ang * 7 + 3 * (wob - 0.5)) > 0.55) * np.clip((rim - rr) / (rim - throat), 0, 1)
    rgb = lerp(mound, (150, 84, 96), ring * 0.7)
    rgb = lerp(rgb, (118, 58, 72), folds * 0.8)
    # throat with a soft bloatgas glow
    t = np.clip((throat - rr) * n / 1.5, 0, 1)
    rgb = lerp(rgb, (64, 20, 34), t)
    rgb = lerp(rgb, (250, 150, 196), np.clip(1 - rr / (throat * 0.55), 0, 1) ** 1.2)
    # three chunky pustules on the rim
    for a0, rad, pr in ((-2.2, 0.3, 0.05), (0.4, 0.33, 0.042), (2.4, 0.29, 0.036)):
        px, py = np.cos(a0) * rad, np.sin(a0) * rad
        d = np.sqrt((dx - px) ** 2 + (dy - py) ** 2) / pr
        rgb = lerp(rgb, (24, 10, 14), np.clip((1.18 - d) * pr * n / 1.5, 0, 1))
        rgb = lerp(rgb, (236, 180, 196), np.clip((1 - d) * pr * n / 1.5, 0, 1))
        hl = np.sqrt((dx - px + pr * 0.3) ** 2 + (dy - py + pr * 0.3) ** 2) / (pr * 0.35)
        rgb = lerp(rgb, (255, 240, 244), np.clip((1 - hl) * 3, 0, 1))
    rgb = lerp(rgb, (24, 10, 14), np.clip((rr - (outer - 0.022)) * n / 1.5, 0, 1))  # thick outline
    return to_image(rgb, alpha, size)


if __name__ == '__main__':
    import sys
    out = sys.argv[1]
    flesh_wall().save(f'{out}/RR_FleshWall_Atlas.png')
    hardened_flesh().save(f'{out}/RR_HardenedFlesh_Atlas.png')
    void_vein().save(f'{out}/RR_VoidBlobWall_Atlas.png')
    flesh_floor().save(f'{out}/RR_FleshFloor.png')
    bloat_geyser().save(f'{out}/RR_BloatGeyser.png')
