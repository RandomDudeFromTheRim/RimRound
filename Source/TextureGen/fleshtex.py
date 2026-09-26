"""Procedural textures for RimRound's flesh dimension.

Linked wall atlases follow RimWorld's MaterialAtlasPool layout: a 4x4 grid of
80px cells in a 320px texture; cell i = Up(1)|Right(2)|Down(4)|Left(8) sits at
column i%4, UV row i//4 (image row 3 - i//4, since UV v=0 is the image bottom).
Only the central 60px of each cell is shown (1/32 padding each side); the rest
is bleed. All surface patterns are periodic in 60px, so linked neighbours meet
seamlessly whichever cells they come from.
"""
import numpy as np
from PIL import Image

SS = 2  # render at 2x, downsample for anti-aliasing
CELL, VIS, PAD = 80 * SS, 60 * SS, 10 * SS
TAU = 2 * np.pi


def periodic_noise(shape, period, seed, octaves=((2, 1.0), (4, 0.5), (7, 0.3), (11, 0.18))):
    """Smooth noise that tiles every `period` px: a sum of integer-frequency waves."""
    rng = np.random.default_rng(seed)
    h, w = shape
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    out = np.zeros(shape, np.float32)
    for freq, amp in octaves:
        for _ in range(6):
            fx, fy = rng.integers(-freq, freq + 1, 2)
            if fx == 0 and fy == 0:
                continue
            phase = rng.uniform(0, TAU)
            out += amp * np.sin(TAU * (fx * x + fy * y) / period + phase)
    out -= out.min()
    return out / max(out.max(), 1e-6)


def periodic_cells(shape, period, seed, count):
    """Toroidal Voronoi: (distance to nearest site, distance to 2nd nearest)."""
    rng = np.random.default_rng(seed)
    pts = rng.uniform(0, period, (count, 2))
    h, w = shape
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    d = []
    for px, py in pts:
        dx = np.abs((x - px + period / 2) % period - period / 2)
        dy = np.abs((y - py + period / 2) % period - period / 2)
        d.append(np.sqrt(dx * dx + dy * dy))
    d = np.sort(np.stack(d), axis=0)
    return d[0], d[1]


def rounded_box_sdf(x, y, x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    hx, hy = (x1 - x0) / 2 - r, (y1 - y0) / 2 - r
    qx, qy = np.abs(x - cx) - hx, np.abs(y - cy) - hy
    outside = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2)
    inside = np.minimum(np.maximum(qx, qy), 0)
    return outside + inside - r


def lerp(a, b, t):
    t = np.clip(t, 0, 1)[..., None]
    return a * (1 - t) + b * t


def smooth_line(dist, width):
    """Anti-aliased line mask from a distance-like field."""
    return np.clip(1 - dist / width, 0, 1) ** 1.5


def warp(field_a, field_b, x, y, amount):
    """Domain warp: offset sample positions by two noise fields."""
    return x + amount * (sample(field_a, x, y) - 0.5), y + amount * (sample(field_b, x, y) - 0.5)


def cell_shape(mask, margin, radius, wobble, wobble_amp):
    """Signed distance (px, negative inside) of one linked cell, in local coords
    where 0..60 is the visible tile and the linked sides run off past the bleed."""
    y, x = np.mgrid[0:CELL, 0:CELL].astype(np.float32) - PAD
    margin, radius, far = margin * SS, radius * SS, 40 * SS
    x0 = -far if mask & 8 else margin
    x1 = VIS + far if mask & 2 else VIS - margin
    y0 = -far if mask & 1 else margin
    y1 = VIS + far if mask & 4 else VIS - margin
    d = rounded_box_sdf(x, y, x0, y0, x1, y1, radius)
    return d + wobble_amp * SS * (wobble - 0.5)


def shade_cell(mask, surface, palette, margin=5, radius=11, wobble_amp=5.0, outline=2.2):
    """surface(local_x, local_y) -> (rgb float array, extra alpha-safe info)."""
    y, x = np.mgrid[0:CELL, 0:CELL].astype(np.float32) - PAD
    wob = palette['wobble'][(y.astype(int) % VIS), (x.astype(int) % VIS)]
    d = cell_shape(mask, margin, radius, wob, wobble_amp)
    depth = -d
    # light from the top: brighter where the outline faces up
    up = np.roll(d, 3 * SS, axis=0) - np.roll(d, -3 * SS, axis=0)
    rgb = surface(x, y, depth / SS)
    rim = np.clip(depth / (9.0 * SS), 0, 1)
    rgb = lerp(palette['shadow'], rgb, 0.35 + 0.65 * rim)
    rgb = rgb + np.clip(-up / (6.0 * SS), 0, 1)[..., None] * palette['toplight'] * (1 - rim)[..., None]
    rgb = lerp(rgb, palette['outline'], np.clip(outline * SS - depth + 0.5, 0, 1))
    alpha = np.clip(depth + 1.0, 0, 1)
    return np.concatenate([np.clip(rgb, 0, 255), (alpha * 255)[..., None]], axis=-1)


def build_atlas(surface, palette, **kw):
    atlas = np.zeros((4 * CELL, 4 * CELL, 4), np.float32)
    for i in range(16):
        col, row = i % 4, 3 - i // 4
        atlas[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL] = shade_cell(i, surface, palette, **kw)
    img = Image.fromarray(atlas.astype(np.uint8), 'RGBA')
    return img.resize((4 * CELL // SS, 4 * CELL // SS), Image.LANCZOS)


def sample(field, x, y):
    n = field.shape[0]
    return field[(np.round(y).astype(int) % n), (np.round(x).astype(int) % n)]


LOW = ((1, 1.0), (2, 0.6), (3, 0.3))


# ---------------------------------------------------------------- flesh wall
def flesh_wall():
    lumps = periodic_noise((VIS, VIS), VIS, 11, ((1, 1.0), (2, 0.8), (3, 0.45), (5, 0.2)))
    wa = periodic_noise((VIS, VIS), VIS, 17, LOW)
    wb = periodic_noise((VIS, VIS), VIS, 18, LOW)
    folds = periodic_noise((VIS, VIS), VIS, 12, ((1, 1.0), (2, 0.5)))
    veins = periodic_noise((VIS, VIS), VIS, 13, ((2, 1.0), (3, 0.5), (4, 0.25)))
    grain = periodic_noise((VIS, VIS), VIS, 14, ((9, 1.0), (14, 0.6), (21, 0.4)))
    base, light = np.array([150, 36, 58.]), np.array([232, 112, 124.])

    def surface(x, y, depth):
        wx, wy = warp(wa, wb, x, y, 22 * SS)
        l = sample(lumps, wx, wy)
        rgb = lerp(base, light, l ** 1.3)
        rgb = rgb * (0.94 + 0.12 * sample(grain, x, y))[..., None]
        # a few soft creases between the lumps
        crease = smooth_line(np.abs(sample(folds, wx, wy) - 0.5) * 60, 1.6 * SS)
        rgb = lerp(rgb, np.array([96, 16, 38.]), crease * 0.7)
        # veins: thin, anti-aliased, bluish purple under the skin
        vein = smooth_line(np.abs(sample(veins, x, y) - 0.5) * 60, 0.9 * SS) * (depth > 3)
        rgb = lerp(rgb, np.array([104, 42, 118.]), vein * 0.75)
        # wet sheen on the lump tops
        sheen = np.clip((l - 0.78) / 0.14, 0, 1) * (depth > 4)
        return lerp(rgb, np.array([255, 208, 214.]), sheen * 0.65)

    palette = dict(shadow=np.array([70, 10, 26.]), toplight=np.array([70, 36, 40.]),
                   outline=np.array([34, 6, 14.]), wobble=periodic_noise((VIS, VIS), VIS, 16, LOW))
    return build_atlas(surface, palette)


# ------------------------------------------------------------ hardened flesh
def hardened_flesh():
    near, near2 = periodic_cells((VIS, VIS), VIS, 21, 5)
    tone = periodic_noise((VIS, VIS), VIS, 22, LOW)
    wa = periodic_noise((VIS, VIS), VIS, 25, LOW)
    wb = periodic_noise((VIS, VIS), VIS, 26, LOW)
    grit = periodic_noise((VIS, VIS), VIS, 23, ((11, 1.0), (17, 0.7), (25, 0.5)))
    scab, scab_light = np.array([92, 40, 40.]), np.array([150, 84, 74.])

    def surface(x, y, depth):
        wx, wy = warp(wa, wb, x, y, 10 * SS)
        edge = sample(near2 - near, wx, wy) / SS           # ~0 on plate borders
        dome = np.clip(1 - sample(near, wx, wy) / (22.0 * SS), 0, 1)
        rgb = lerp(scab, scab_light, 0.25 + 0.75 * dome ** 1.5 * sample(tone, x, y) ** 0.5)
        rgb = rgb * (0.86 + 0.24 * sample(grit, x, y))[..., None]
        glint = np.clip((dome - 0.82) / 0.12, 0, 1)
        rgb = lerp(rgb, np.array([214, 170, 150.]), glint * 0.5)
        # cracks between scab plates show raw flesh
        crack = smooth_line(edge, 2.2)
        rgb = lerp(rgb, np.array([176, 34, 52.]), crack)
        return lerp(rgb, np.array([60, 6, 16.]), smooth_line(edge, 0.8))

    palette = dict(shadow=np.array([40, 14, 16.]), toplight=np.array([50, 36, 32.]),
                   outline=np.array([20, 6, 8.]), wobble=periodic_noise((VIS, VIS), VIS, 24, LOW))
    return build_atlas(surface, palette, margin=3, radius=7, wobble_amp=3.0)


# ------------------------------------------- void blob wall (gluttonium vein)
def void_vein():
    blobs = periodic_noise((VIS, VIS), VIS, 31)
    near, near2 = periodic_cells((VIS, VIS), VIS, 32, 6)
    base, light = np.array([120, 46, 110.]), np.array([196, 110, 176.])

    def surface(x, y, depth):
        rgb = lerp(base, light, sample(blobs, x, y) ** 1.3)
        r = sample(near, x, y) / SS
        nodule = np.clip(1 - r / 6.5, 0, 1) * (depth > 4)
        rgb = lerp(rgb, np.array([70, 20, 90.]), smooth_line(np.abs(r - 7.0), 1.4) * (depth > 4))  # socket rim
        rgb = lerp(rgb, np.array([212, 132, 255.]), nodule)
        return lerp(rgb, np.array([255, 236, 255.]), np.clip(nodule * 2.2 - 1.3, 0, 1))  # glowing core

    palette = dict(shadow=np.array([50, 14, 58.]), toplight=np.array([60, 40, 70.]),
                   outline=np.array([26, 6, 30.]), wobble=periodic_noise((VIS, VIS), VIS, 33, LOW))
    return build_atlas(surface, palette)


# ------------------------------------------------------------------- floor
def flesh_floor(size=512):
    lumps = periodic_noise((size, size), size, 41, ((2, 1.0), (3, 0.7), (5, 0.4), (8, 0.2)))
    wa = periodic_noise((size, size), size, 44, ((2, 1.0), (3, 0.5)))
    wb = periodic_noise((size, size), size, 45, ((2, 1.0), (3, 0.5)))
    folds = periodic_noise((size, size), size, 42, ((3, 1.0), (5, 0.6), (7, 0.3)))
    veins = periodic_noise((size, size), size, 46, ((4, 1.0), (6, 0.5)))
    fine = periodic_noise((size, size), size, 43, ((24, 1.0), (37, 0.6), (53, 0.4)))
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    wx, wy = x + 90 * (sample(wa, x, y) - 0.5), y + 90 * (sample(wb, x, y) - 0.5)
    l = sample(lumps, wx, wy)
    rgb = lerp(np.array([150, 60, 76.]), np.array([206, 116, 126.]), l)
    rgb = rgb * (0.93 + 0.14 * fine)[..., None]
    fold = smooth_line(np.abs(sample(folds, wx, wy) - 0.5) * 200, 2.5)
    rgb = lerp(rgb, np.array([118, 38, 58.]), fold * 0.55)
    vein = smooth_line(np.abs(sample(veins, x, y) - 0.5) * 200, 1.4)
    rgb = lerp(rgb, np.array([128, 58, 110.]), vein * 0.35)
    a = np.full(rgb.shape[:2] + (1,), 255, np.float32)
    return Image.fromarray(np.concatenate([np.clip(rgb, 0, 255), a], -1).astype(np.uint8), 'RGBA')




# ------------------------------------------------------------------ geyser
def bloat_geyser(size=256):
    """A puckered flesh vent: lumpy swollen mound, radial folds into a glowing throat."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    cx = cy = n / 2
    dx, dy = (x - cx) / n, (y - cy) / n
    r, ang = np.sqrt(dx * dx + dy * dy), np.arctan2(dy, dx)
    wob = periodic_noise((n, n), n, 51, ((2, 1.0), (3, 0.7), (5, 0.35)))
    lumps = periodic_noise((n, n), n, 52, ((4, 1.0), (7, 0.6), (11, 0.3)))
    veins = periodic_noise((n, n), n, 53, ((5, 1.0), (7, 0.5)))
    rr = r + 0.07 * (wob - 0.5)
    outer, rim_r, throat = 0.45, 0.2, 0.085
    alpha = np.clip((outer - rr) * n * 0.7, 0, 1)
    # mound: swells up to the rim, slopes down outside it
    mound = np.clip(1 - np.abs(rr - rim_r) / (outer - rim_r), 0, 1)
    rgb = lerp(np.array([118, 28, 50.]), np.array([232, 114, 130.]), mound ** 1.1 * (0.55 + 0.45 * lumps))
    # veins over the outer slope
    vein = smooth_line(np.abs(veins - 0.5) * 160, 1.3 * SS) * np.clip((rr - rim_r) / 0.04, 0, 1)
    rgb = lerp(rgb, np.array([104, 40, 116.]), vein * 0.7)
    # radial pucker folds from the rim into the throat
    inner = np.clip((rim_r - rr) / (rim_r - throat), 0, 1)
    folds = (np.cos(ang * 9 + 5 * (wob - 0.5)) * 0.5 + 0.5) ** 4 * np.clip(inner * 1.4, 0, 1)
    rgb = lerp(rgb, np.array([226, 108, 128.]), (1 - folds) * inner * 0.25)
    rgb = lerp(rgb, np.array([84, 12, 34.]), folds * 0.85)
    # pustules clustered on the rim
    rng = np.random.default_rng(54)
    for _ in range(14):
        a0 = rng.uniform(-np.pi, np.pi); rad = rim_r + rng.uniform(0.01, 0.09); pr = rng.uniform(0.012, 0.026)
        px, py = np.cos(a0) * rad, np.sin(a0) * rad
        d = np.sqrt((dx - px) ** 2 + (dy - py) ** 2) / pr
        body = np.clip(1 - d, 0, 1)
        rgb = lerp(rgb, np.array([246, 150, 178.]), np.clip(body * 3, 0, 1))
        rgb = lerp(rgb, np.array([255, 232, 238.]), np.clip(1 - np.sqrt((dx - px + pr * 0.3) ** 2 + (dy - py + pr * 0.35) ** 2) / (pr * 0.35), 0, 1))
        rgb = lerp(rgb, np.array([110, 24, 52.]), smooth_line(np.abs(d - 1) * pr * n, 1.2 * SS))
    # wet highlight along the upper rim
    sheen = np.clip(1 - np.abs(rr - rim_r * 1.1) / 0.02, 0, 1) * np.clip(-dy / r.clip(1e-3), 0, 1)
    rgb = lerp(rgb, np.array([255, 214, 222.]), sheen * 0.6)
    # the throat: dark, with a bloatgas glow in its depths
    t = np.clip(1 - rr / throat, 0, 1)
    rgb = lerp(rgb, np.array([40, 4, 20.]), np.clip(t * 3, 0, 1))
    rgb = lerp(rgb, np.array([255, 110, 186.]), t ** 1.8)
    rgb = lerp(rgb, np.array([30, 6, 12.]), smooth_line(np.abs(rr - outer + 0.006) * n, 1.6 * SS))
    img = np.concatenate([np.clip(rgb, 0, 255), (alpha * 255)[..., None]], -1).astype(np.uint8)
    return Image.fromarray(img, 'RGBA').resize((size, size), Image.LANCZOS)


if __name__ == '__main__':
    # usage: python3 fleshtex.py <out dir>   (needs numpy + pillow)
    # Outputs go to Textures/Things/Building/FleshDimension/ and Textures/Terrain/.
    import sys
    out = sys.argv[1]
    flesh_wall().save(f'{out}/RR_FleshWall_Atlas.png')
    hardened_flesh().save(f'{out}/RR_HardenedFlesh_Atlas.png')
    void_vein().save(f'{out}/RR_VoidBlobWall_Atlas.png')
    flesh_floor().save(f'{out}/RR_FleshFloor.png')
    bloat_geyser().save(f'{out}/RR_BloatGeyser.png')
