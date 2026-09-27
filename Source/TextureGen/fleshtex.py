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


# ------------------------------------------------------------------ gorge maw
def gorge_maw(size=128):
    """A hidden floor trap: a low flesh pucker in floor colours around a
    glistening lens-shaped mouth. Meant to be easy to miss on flesh floor."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    dx, dy = (x - n / 2) / n, (y - n / 2) / n
    wob = periodic_noise(n, 77, ((2, 1.0), (3, 0.5)))
    r = np.sqrt(dx * dx + (dy * 1.25) ** 2) + 0.04 * (wob - 0.5)
    outer = 0.43
    alpha = np.clip((outer - r) * n / 1.5, 0, 1) * 0.92
    # pucker: floor-coloured, lit from above, darker lower half
    rgb = lerp((176, 118, 126), (204, 150, 152), np.clip(-dy * 3 + 0.3, 0, 1))
    rgb = lerp(rgb, (138, 82, 94), np.clip(dy * 3 - 0.2, 0, 1))
    # radial wrinkles drawn in toward the mouth
    ang = np.arctan2(dy, dx)
    wrinkle = (np.cos(ang * 9 + 4 * (wob - 0.5)) > 0.6) * np.clip(1 - r / outer, 0, 1)
    rgb = lerp(rgb, (132, 72, 86), wrinkle * 0.55)
    # the mouth: a lens-shaped slit with swollen lips and a wet highlight
    lens = np.abs(dy) / np.maximum(0.001, 0.075 * np.clip(1 - (dx / 0.24) ** 2, 0, 1))
    lips = np.clip(1.6 - lens, 0, 1) * (np.abs(dx) < 0.24)
    rgb = lerp(rgb, (170, 70, 92), lips * 0.9)
    mouth = np.clip((1 - lens) * 6, 0, 1) * (np.abs(dx) < 0.23)
    rgb = lerp(rgb, (48, 14, 26), mouth)
    shine = np.clip(1 - np.sqrt(((dx + 0.07) / 0.06) ** 2 + ((dy + 0.05) / 0.018) ** 2), 0, 1)
    rgb = lerp(rgb, (255, 226, 236), shine * 0.85)
    rgb = lerp(rgb, (24, 10, 14), np.clip((r - (outer - 0.02)) * n / 1.5, 0, 1))  # thick outline
    return to_image(rgb, alpha, size)


def gorge_maw_full(size=128, seam=True):
    """The gorge maw with someone inside: a taut, glossy sack sealed along a
    puckered seam, veined and stretched. Drawn scaled up in game as it swells.
    Without the seam it is the lump of a limb pushing out against the sack."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    dx, dy = (x - n / 2) / n, (y - n / 2) / n
    wob = periodic_noise(n, 91, ((2, 1.0), (3, 0.5)))
    r = np.sqrt(dx * dx + (dy * 1.08) ** 2) + 0.03 * (wob - 0.5)
    outer = 0.44
    alpha = np.clip((outer - r) * n / 1.5, 0, 1)
    # taut dome: brighter and pinker than the open maw, strongly lit from above
    rgb = lerp((196, 116, 132), (238, 176, 184), np.clip(-dy * 2.6 + 0.35, 0, 1))
    rgb = lerp(rgb, (146, 72, 92), np.clip(dy * 3 - 0.15, 0, 1))
    # stretched veins
    ang = np.arctan2(dy, dx)
    veins = (np.abs(np.sin(ang * 5 + 6 * r + 3 * (wob - 0.5))) < 0.08) * np.clip((r - 0.12) / 0.2, 0, 1)
    rgb = lerp(rgb, (120, 52, 78), veins * 0.6)
    # the sealed seam, puckered shut
    if seam:
        line = np.clip(1 - np.abs(dy - 0.02 * np.sin(dx * 40)) / 0.012, 0, 1) * (np.abs(dx) < 0.2)
        rgb = lerp(rgb, (70, 22, 38), line)
    # glossy highlight on the dome
    shine = np.clip(1 - np.sqrt(((dx + 0.12) / 0.1) ** 2 + ((dy + 0.17) / 0.05) ** 2), 0, 1)
    rgb = lerp(rgb, (255, 236, 242), shine * 0.8)
    rgb = lerp(rgb, (24, 10, 14), np.clip((r - (outer - 0.02)) * n / 1.5, 0, 1))  # thick outline
    return to_image(rgb, alpha, size)


# ------------------------------------------------------------------ entry scar
def entry_scar(size=128):
    """Where pawns arrive in the void maze: a long tear in the floor that has
    knitted itself shut, pale and raw, with a faint purple glow in the seam."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    dx, dy = (x - n / 2) / n, (y - n / 2) / n
    wob = periodic_noise(n, 13, ((2, 1.0), (4, 0.4)))
    bend = 0.05 * np.sin(dx * 9) + 0.03 * (wob - 0.5)
    across = np.abs(dy - bend)
    length = np.clip(1 - np.abs(dx) / 0.42, 0, 1)
    width = 0.09 * np.sqrt(length)
    body = np.clip((width - across) * n / 1.5, 0, 1)
    halo = np.clip((width * 1.8 - across) / (width * 0.8 + 1e-3), 0, 1) * length
    alpha = np.maximum(body, halo * 0.55)
    # raw pink scar tissue, paler than the floor, with a darker halo of bruising
    rgb = lerp((150, 80, 98), (236, 190, 196), body)
    stitches = (np.cos(dx * 70) > 0.7) * body * np.clip(1 - across / (width * 0.9 + 1e-3), 0, 1)
    rgb = lerp(rgb, (170, 110, 124), stitches * 0.7)
    seam = np.clip(1 - across / 0.012, 0, 1) * length
    rgb = lerp(rgb, (210, 120, 255), seam * 0.9)
    return to_image(rgb, alpha, size)


# ------------------------------------------------------------------ void seam
def void_seam(size=256):
    """The void portal: a huge mouth that has opened in the ground. Swollen,
    wet lips around a dim purple throat, ringed by bruised flesh that fades out
    into whatever ground it has broken through."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    dx, dy = (x - n / 2) / n, (y - n / 2) / n
    wob = periodic_noise(n, 29, ((2, 1.0), (4, 0.4)))
    bend = 0.03 * np.sin(dx * 7) + 0.02 * (wob - 0.5)
    across = np.abs(dy - bend)
    along = np.clip(1 - (dx / 0.4) ** 2, 0, 1)
    mouth_w = 0.085 * np.sqrt(along)                # half-height of the opening
    lip_w = mouth_w + 0.05 * np.sqrt(along) + 0.012
    # bruised flesh halo fading out into the ground
    r = np.sqrt((dx / 0.46) ** 2 + (dy / 0.3) ** 2) + 0.06 * (wob - 0.5)
    halo = np.clip((1 - r) * 3, 0, 1)
    alpha = np.maximum(halo * 0.85, np.clip((lip_w - across) * n / 1.5, 0, 1) * (along > 0))
    rgb = lerp((120, 70, 84), (168, 104, 118), np.clip(-dy * 2 + 0.4, 0, 1))
    rgb = lerp(rgb, (96, 48, 66), np.clip((r - 0.55) * 2.2, 0, 1))          # darker toward the rim
    ang = np.arctan2(dy, dx)
    creases = (np.cos(ang * 11 + 5 * (wob - 0.5)) > 0.7) * halo * np.clip(r * 1.6 - 0.3, 0, 1)
    rgb = lerp(rgb, (104, 54, 72), creases * 0.6)
    # swollen lips, lit from above: the upper lip catches the light
    lips = np.clip((lip_w - across) * n / 1.5, 0, 1) * (along > 0)
    upper = dy < bend
    lip_col = np.where(upper[..., None], np.array((206, 128, 138), np.float32), np.array((164, 88, 104), np.float32))
    rgb = lerp(rgb, lip_col, lips)
    # the throat: deep, dark, a faint purple glow far down
    throat = np.clip((mouth_w - across) * n / 1.5, 0, 1) * (along > 0)
    rgb = lerp(rgb, (38, 16, 34), throat)
    glow = np.clip(1 - across / (mouth_w * 0.6 + 1e-3), 0, 1) * np.clip(along * 1.4 - 0.2, 0, 1)
    rgb = lerp(rgb, (150, 70, 170), glow * throat * 0.7)
    # wet shine along the upper lip, and a dark rim line where lip meets throat
    shine = np.clip(1 - np.abs(across - (mouth_w + 0.02)) / 0.008, 0, 1) * upper * np.clip(along * 2 - 0.6, 0, 1)
    rgb = lerp(rgb, (246, 214, 222), shine * 0.7)
    rim = np.clip(1 - np.abs(across - mouth_w) / 0.006, 0, 1) * (along > 0)
    rgb = lerp(rgb, (40, 18, 30), rim * 0.9)
    return to_image(rgb, alpha, size)


def silhouette(img):
    """A codex silhouette: the same shape, filled flat white (RimWorld tints it)."""
    import numpy as _np
    from PIL import Image as _Image
    a = _np.asarray(img.convert('RGBA')).copy()
    a[..., :3] = 255
    return _Image.fromarray(a, 'RGBA')


# ------------------------------------------------------------------ gorge constrictor
# measured off Anomaly's fleshbeasts: desaturated brick-rose, near-black outline
CON_BASE, CON_LIGHT, CON_DARK = (160, 104, 106), (204, 150, 148), (104, 64, 64)
CON_OUTLINE = (18, 10, 10)


def _shard(px, py, cx, cy, s, n):
    """A jagged sliver of dark archotechnology: black glassy facets, a violet glint."""
    ax, ay = (px - cx) / s, (py - cy) / s
    # a tall, leaning, irregular diamond
    body = np.maximum(np.abs(ax + 0.25 * ay) * 1.9 + np.abs(ay) * 0.55, np.abs(ay) * 1.0) - 0.5
    body = np.maximum(body, -(ay + 0.55 - 0.35 * ax))          # chipped lower edge
    fill = np.clip(-body * s * n / 1.5, 0, 1)
    edge = np.clip((0.06 - np.abs(body)) * s * n / 1.5, 0, 1)
    facet = (ax + 0.25 * ay) > 0                               # right face catches the light
    rgb = np.where(facet[..., None], np.array((78, 72, 94), np.float32), np.array((30, 26, 38), np.float32))
    glint = np.clip(1 - np.abs(ax + 0.25 * ay - 0.02) / 0.05, 0, 1) * np.clip(0.4 - ay, 0, 1)
    rgb = lerp(rgb, (170, 110, 220), glint * 0.8)
    return fill, edge, rgb


def _flesh_mass(px, py, n, lumps, wob):
    """Paints lumps as ONE merged mass of flesh, like vanilla fleshmass: a
    metaball field gives a single lumpy outline, shading follows the combined
    surface (lit from the top-left), and dark creases form where lobes meet.
    lumps: list of (cx, cy, r, aspect, angle). Returns rgb, alpha."""
    total = np.zeros((n, n), np.float32)
    strongest = np.zeros((n, n), np.float32)
    for cx, cy, r, asp, rot in lumps:
        c, s_ = np.cos(rot), np.sin(rot)
        u = ((px - cx) * c + (py - cy) * s_) / (r * asp)
        v = (-(px - cx) * s_ + (py - cy) * c) / (r / asp)
        # compact falloff: exactly zero 1.6 radii out, so lobes only merge with neighbours
        q = np.clip((u * u + v * v) / 2.56, 0, 1)
        f = (1 - q) ** 2
        total += f
        strongest = np.maximum(strongest, f)
    field = total * (0.9 + 0.2 * wob)                           # imperfect, knobbly surface
    inside = np.clip((field - 0.25) * 40, 0, 1)
    outline = np.clip((field - 0.11) * 40, 0, 1)              # bold outline, like vanilla fleshbeasts
    height = np.sqrt(np.clip(field - 0.25, 0, 2))
    gy, gx = np.gradient(height)
    lit = np.clip(-(gx * 0.55 + gy * 0.85) * n * 0.035 + 0.15, -1, 1)
    col = lerp(CON_BASE, CON_LIGHT, np.clip(lit, 0, 1))
    col = lerp(col, CON_DARK, np.clip(-lit, 0, 1) * 0.85)
    # creases where two lobes press together: no single lobe dominates
    dom = strongest / np.maximum(total, 1e-6)
    # only in the valleys: where lobes pile up deep, the surface stays lit
    crease = np.clip((0.62 - dom) * 6, 0, 1) * inside * np.clip((1.15 - field) * 2.5, 0, 1)
    col = lerp(col, (84, 54, 54), crease * 0.75)
    # each lobe's crown catches the light, so a deep pile keeps its lumpy relief
    crown = np.clip((strongest - 0.55) * 3, 0, 1) * np.clip(lit + 0.4, 0, 1)
    col = lerp(col, CON_LIGHT, crown * 0.45)
    # a few wet highlights near the lit tops of the biggest lobes
    shine = np.clip((lit - 0.75) * 5, 0, 1) * (wob > 0.5)
    col = lerp(col, (226, 184, 180), shine * 0.6)
    rgb = np.zeros((n, n, 3), np.float32)
    rgb[:] = CON_OUTLINE
    rgb = lerp(rgb, col, inside)
    return rgb, outline


def _constrictor_lumps(stage):
    """Lobes of the constrictor wrapped around a torso (the body sprite fills
    about the middle half of the canvas). Lobes persist from stage to stage and
    new ones bud off existing ones, so it spreads over the pawn as it feeds."""
    rng = np.random.default_rng(7)
    grow = (1.0, 1.12, 1.25, 1.38, 1.5)[stage]
    lumps = []
    # two loops across the belly: only their front halves show
    for cy, a_, count in ((0.0, 0.24, 12), (0.14, 0.25, 12)):
        for i in range(count):
            t = (i + 0.5) / count
            ang = np.pi * (0.06 + 0.88 * t)
            x_ = -np.cos(ang) * a_ + rng.uniform(-0.008, 0.008)
            y_ = cy + np.sin(ang) * 0.09 + rng.uniform(-0.008, 0.008)
            # lobes long along the loop, so neighbours always fuse into one band
            lumps.append([x_, y_, rng.uniform(0.032, 0.042), rng.uniform(1.3, 1.6), ang + np.pi / 2])
    # the worm's head end heaving up over the right shoulder
    for x_, y_, r_ in ((0.22, -0.03, 0.036), (0.235, -0.085, 0.03), (0.245, -0.13, 0.025)):
        lumps.append([x_, y_, r_, 1.15, 1.2])
    # it buds new lobes off its existing ones: creeping up the chest and down
    # the hips, then engulfing the torso
    buds = (0, 8, 16, 26, 36)[stage]
    for i in range(36):
        parent = lumps[rng.integers(0, 16)] if i < 10 else lumps[rng.integers(0, len(lumps))]
        ang = rng.uniform(0, 2 * np.pi)
        dist = parent[2] * rng.uniform(1.1, 1.8)
        bud = [parent[0] + np.cos(ang) * dist, parent[1] + np.sin(ang) * dist * 1.2,
               rng.uniform(0.028, 0.045), rng.uniform(1.0, 1.6), rng.uniform(0, np.pi)]
        if i < buds:
            lumps.append(bud)
    for l in lumps:
        l[2] *= grow
    return [tuple(l) for l in lumps]


def constrictor_coil(size=256, stage=0):
    """The gorge constrictor wrapped around a victim: one knotted mass of flesh
    with a shard of dark archotechnology driven into its back. Three stages:
    latched round the waist, swelling up the chest, engulfing the torso."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    wob = periodic_noise(n, 61, ((4, 1.0), (7, 0.5)))
    rgb, alpha = _flesh_mass(px, py, n, _constrictor_lumps(stage), wob)
    # dark plum pustules dotted over the flesh, like a bulbfreak's
    rng = np.random.default_rng(19)
    inside = alpha > 0.99
    for i in range((3, 4, 6, 8, 11)[stage]):
        cx, cy = rng.uniform(-0.24, 0.24), rng.uniform(-0.05 - 0.025 * stage, 0.2 + 0.02 * stage)
        r = rng.uniform(0.016, 0.028)
        d = np.sqrt((px - cx) ** 2 + (py - cy) ** 2) / r
        pus = np.clip((1 - d) * r * n / 1.5, 0, 1) * inside
        lit = np.clip(-(px - cx + (py - cy)) / r * 0.6 + 0.2, 0, 1)
        rgb = lerp(rgb, CON_OUTLINE, np.clip((1.18 - d) * r * n / 1.5, 0, 1) * inside * 0.8)
        rgb = lerp(rgb, lerp((120, 70, 92), (176, 120, 138), lit), pus)
    sfill, sedge, scol = _shard(px, py, 0.25 + 0.008 * stage, -0.17 - 0.008 * stage, 0.09 + 0.008 * stage, n)
    rgb = lerp(rgb, CON_OUTLINE, sedge)
    rgb = lerp(rgb, scol, sfill)
    alpha = np.maximum(alpha, np.maximum(sfill, sedge))
    return to_image(rgb, alpha, size)


def _cap(px, py, ax, ay, bx, by):
    """Distance to a line segment (a stroke with round caps)."""
    abx, aby = bx - ax, by - ay
    t = np.clip(((px - ax) * abx + (py - ay) * aby) / max(abx * abx + aby * aby, 1e-9), 0, 1)
    return np.sqrt((px - ax - t * abx) ** 2 + (py - ay - t * aby) ** 2)


def _arc(px, py, cx, cy, r, a0, a1):
    """Distance to a circular arc from angle a0 to a1 (radians, y down)."""
    ang = np.arctan2(py - cy, px - cx)
    inside = ((ang - a0) % TAU) <= ((a1 - a0) % TAU)
    ring = np.abs(np.sqrt((px - cx) ** 2 + (py - cy) ** 2) - r)
    ends = np.minimum(np.sqrt((px - cx - r * np.cos(a0)) ** 2 + (py - cy - r * np.sin(a0)) ** 2),
                      np.sqrt((px - cx - r * np.cos(a1)) ** 2 + (py - cy - r * np.sin(a1)) ** 2))
    return np.where(inside, ring, ends)


def _face_strokes(px, py, mood):
    """The emoticon, as a distance field, by mood (0..4). The constrictor is
    biggest when it latches and shrinks as it empties, so the face runs from a
    plain smile at the biggest size to utter bliss once it has given everything:
    0 :)   1 ^_^   2 ^w^   3 >w<   4 blissed out - happy-shut eyes, open grin,
    tongue out, heavy blush and a heart."""
    d = np.full(px.shape, 9.0, np.float32)
    add = lambda e: np.minimum(d, e)
    ex, ey = 0.17, -0.06                              # eye positions (mirrored)
    smile = lambda: _arc(px, py, 0.0, 0.02, 0.075, 0.45, np.pi - 0.45)
    w_mouth = lambda: np.minimum(_arc(px, py, -0.035, 0.07, 0.035, 0.0, np.pi), _arc(px, py, 0.035, 0.07, 0.035, 0.0, np.pi))
    for sx in (-1, 1):
        x0 = sx * ex
        if mood == 0:
            d = add(np.sqrt((px - x0) ** 2 + (py - ey) ** 2) - 0.022)
        elif mood in (1, 2):
            d = add(_cap(px, py, x0 - 0.045, ey + 0.02, x0, ey - 0.03))
            d = add(_cap(px, py, x0, ey - 0.03, x0 + 0.045, ey + 0.02))
        elif mood == 3:
            tip = x0 - sx * 0.05
            d = add(_cap(px, py, x0 + sx * 0.04, ey - 0.05, tip, ey))
            d = add(_cap(px, py, tip, ey, x0 + sx * 0.04, ey + 0.05))
        else:
            # squeezed happily shut: upturned arcs
            d = add(_arc(px, py, x0, ey + 0.02, 0.045, np.pi + 0.35, TAU - 0.35))
    if mood <= 1:
        d = add(smile())
    elif mood < 4:
        d = add(w_mouth())
    else:
        # a wide open grin with the tongue lolling out
        d = add(_cap(px, py, -0.075, 0.04, 0.075, 0.04))
        d = add(_arc(px, py, 0.0, 0.04, 0.075, 0.0, np.pi))
        d = add(_arc(px, py, 0.0, 0.1, 0.03, np.pi + 0.3, TAU - 0.3))   # the tongue
        # hearts, top right and top left
        for hx0, hy0, k in ((0.27, -0.14, 1.6), (-0.28, -0.12, 1.2)):
            hx, hy = (px - hx0) / k, (py - hy0) / k
            heart = np.minimum(np.sqrt((hx + 0.012) ** 2 + hy ** 2), np.sqrt((hx - 0.012) ** 2 + hy ** 2)) - 0.014
            heart = np.minimum(heart, np.maximum(np.abs(hx) + (hy - 0.005) * 0.9 - 0.025, -(hy)))
            d = add(np.maximum(heart, 0) * k)
    if mood >= 2:
        for sx in (-1, 1):                             # blush lines under the eyes, more as it gets happier
            for k in range(2 if mood == 2 else 3):
                bx = sx * (0.2 + 0.03 * k) - sx * 0.03
                d = add(_cap(px, py, bx, 0.03, bx - sx * 0.015, 0.07))
    return d


Stages_MAX = 4


def constrictor_face(size=128, stage=0):
    """A cracked, bloodied monitor embedded in the constrictor's flesh, glowing
    with a neon-blue emoticon. Flesh lips creep over its bezel. stage is the
    coil's size stage (4 biggest, when it latches): the smaller it gets, the
    happier the face."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    wob = periodic_noise(n, 97, ((4, 1.0), (7, 0.5)))
    bezel = rounded_box_sdf(px, py, -0.42, -0.3, 0.42, 0.3, 0.07)
    screen = rounded_box_sdf(px, py, -0.36, -0.24, 0.36, 0.24, 0.05)
    rgb = np.zeros((n, n, 3), np.float32)
    rgb[:] = CON_OUTLINE
    body = np.clip(-bezel * n / 1.5, 0, 1)
    alpha = np.clip((0.02 - bezel) * n / 1.5, 0, 1)
    rgb = lerp(rgb, lerp((64, 60, 70), (96, 92, 104), np.clip(-py * 2 + 0.4, 0, 1)), body)
    # the screen: near black, with a faint scanline sheen
    scr = np.clip(-screen * n / 1.5, 0, 1)
    rgb = lerp(rgb, (6, 8, 16), scr)
    rgb = lerp(rgb, (14, 22, 40), scr * (np.sin(py * 180) > 0.6) * 0.5)
    # the glowing emoticon: a soft halo under a crisp stroke
    d = _face_strokes(px, py, Stages_MAX - stage)
    glow = np.clip(1 - d / 0.06, 0, 1) ** 2 * scr
    rgb = lerp(rgb, (0, 70, 140), glow * 0.8)
    stroke = np.clip((0.016 - d) * n / 1.5, 0, 1) * scr
    rgb = lerp(rgb, (40, 170, 255), stroke)
    # a crack across one corner, and blood smeared over the bezel
    crack = np.clip(1 - _cap(px, py, 0.18, -0.24, 0.34, -0.02) / 0.006, 0, 1) * scr
    rgb = lerp(rgb, (120, 150, 190), crack * 0.6)
    blood = np.clip((wob - 0.62) * 5, 0, 1) * body * (1 - scr * 0.7)
    rgb = lerp(rgb, (96, 20, 24), blood * 0.85)
    # flesh lips creeping over the bezel's edge: it is embedded, not stuck on
    lip = np.clip((0.07 - np.abs(bezel + 0.01 + 0.03 * (wob - 0.5))) * n / 1.5, 0, 1) * (wob > 0.35)
    rgb = lerp(rgb, lerp(CON_DARK, CON_BASE, np.clip(-py * 2 + 0.5, 0, 1)), lip)
    alpha = np.maximum(alpha, lip)
    return to_image(rgb, alpha, size)


def _to_image_rect(rgb, alpha, w, h):
    arr = np.concatenate([np.clip(rgb, 0, 255), (np.clip(alpha, 0, 1) * 255)[..., None]], -1)
    return Image.fromarray(arr.astype(np.uint8), 'RGBA').resize((w, h), Image.LANCZOS)


def constrictor_proboscis(w=64, h=256):
    """The feeding tube, hanging from the victim's mouth down to the coil:
    ringed and fleshy, flaring into a sucker at the top."""
    nw, nh = w * SS, h * SS
    y, x = np.mgrid[0:nh, 0:nw].astype(np.float32)
    px, py = (x - nw / 2) / nw, y / nh                          # py 0 at the mouth, 1 at the coil
    half = 0.2 + 0.2 * np.clip(1 - py / 0.12, 0, 1)             # flares into the sucker at the top
    d = np.abs(px) - half
    fill = np.clip(-d * nw / 1.5, 0, 1) * (py > 0.02) * (py < 0.98)
    out = np.clip((0.06 - d) * nw / 1.5, 0, 1) * (py > 0.01) * (py < 0.99)
    col = lerp(CON_BASE, CON_LIGHT, np.clip(-px / half + 0.2, 0, 1))
    col = lerp(col, CON_DARK, np.clip(px / half, 0, 1) * 0.7)
    rings = (np.abs(np.sin(py * 60)) < 0.2) * fill
    col = lerp(col, CON_DARK, rings * 0.5)
    rgb = np.zeros((nh, nw, 3), np.float32)
    rgb[:] = CON_OUTLINE
    rgb = lerp(rgb, col, fill)
    return _to_image_rect(rgb, np.maximum(fill, out), w, h)


def constrictor_bolus(size=64):
    """A swallowed gob bulging its way up the proboscis."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    d = np.sqrt((px / 0.3) ** 2 + (py / 0.38) ** 2) - 1
    fill = np.clip(-d * 0.3 * n / 1.5, 0, 1)
    out = np.clip((0.2 - d) * 0.3 * n / 1.5, 0, 1)
    col = lerp(CON_BASE, CON_LIGHT, np.clip(-(px + py) * 2 + 0.3, 0, 1))
    col = lerp(col, (236, 196, 206), np.clip(1 - np.sqrt(((px + 0.1) / 0.08) ** 2 + ((py + 0.12) / 0.06) ** 2), 0, 1) * 0.8)
    rgb = np.zeros((n, n, 3), np.float32)
    rgb[:] = CON_OUTLINE
    rgb = lerp(rgb, col, fill)
    return to_image(rgb, np.maximum(fill, out), size)


def constrictor_body(size=128):
    """The gorge constrictor loose: a small, knobbly flesh worm curled up on
    itself (the same merged-lobe flesh as when it is wrapped around someone),
    its sucker-mouth raised, a shard of dark archotechnology in its back."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    wob = periodic_noise(n, 83, ((4, 1.0), (7, 0.5)))
    rng = np.random.default_rng(11)
    lumps = []
    # lobes along a loose spiral, fattening from the tail toward the head
    for i in range(20):
        t = i / 19
        ang = 0.6 + t * 1.35 * TAU
        rad = 0.02 + 0.27 * t
        lumps.append((np.cos(ang) * rad, np.sin(ang) * rad * 0.85 + 0.03,
                      0.026 + 0.026 * t + rng.uniform(-0.004, 0.004), rng.uniform(1.0, 1.35), ang + 1.57))
    rgb, alpha = _flesh_mass(px, py, n, lumps, wob)
    # the raised sucker-mouth at the head end
    hx, hy = lumps[-1][0], lumps[-1][1]
    mouth = np.sqrt(((px - hx) / 0.05) ** 2 + ((py - hy + 0.02) / 0.038) ** 2)
    ring = np.clip((1 - np.abs(mouth - 0.75) / 0.25), 0, 1)
    rgb = lerp(rgb, CON_LIGHT, ring * 0.8)
    rgb = lerp(rgb, (72, 50, 50), np.clip((0.55 - mouth) * 8, 0, 1))
    # the shard in its back, planted in the middle of the coil
    back = lumps[13]                                             # a lobe on the thick outer turn
    sfill, sedge, scol = _shard(px, py, back[0], back[1] - 0.05, 0.13, n)
    rgb = lerp(rgb, CON_OUTLINE, sedge)
    rgb = lerp(rgb, scol, sfill)
    alpha = np.maximum(alpha, np.maximum(sfill, sedge))
    return to_image(rgb, alpha, size)


def constrictor_lure(size=128):
    """Constrictor lure: a heap of twisted meat, darker and redder than the
    constrictor's own flesh, with a shard of dark archotechnology stuck in it."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    wob = periodic_noise(n, 97, ((4, 1.0), (8, 0.5)))
    rng = np.random.default_rng(23)
    lumps = []
    for i in range(14):
        ang = rng.uniform(0, TAU)
        rad = rng.uniform(0.0, 0.2)
        lumps.append((np.cos(ang) * rad, np.sin(ang) * rad * 0.6 + 0.1,
                      rng.uniform(0.05, 0.08), rng.uniform(1.0, 1.5), rng.uniform(0, np.pi)))
    rgb, alpha = _flesh_mass(px, py, n, lumps, wob)
    # twisted meat: pull the constrictor palette toward a deep, bruised red
    inside = alpha > 0.99
    rgb = np.where(inside[..., None], lerp(rgb, rgb * np.array((0.82, 0.42, 0.48), np.float32), 1.0), rgb)
    sfill, sedge, scol = _shard(px, py, 0.03, -0.12, 0.3, n)
    rgb = lerp(rgb, CON_OUTLINE, sedge)
    rgb = lerp(rgb, scol, sfill)
    alpha = np.maximum(alpha, np.maximum(sfill, sedge))
    return to_image(rgb, alpha, size)


def bound_constrictor(size=128):
    """A bound gorge constrictor: the curled worm cinched with bands of
    bioferrite, the shard in its back holding it docile."""
    n = size * SS
    # upsample back to the supersampled grid to paint the bands at full quality
    from PIL import Image as _Image
    big = np.asarray(constrictor_body(size).convert('RGBA').resize((n, n), _Image.LANCZOS), np.float32)
    rgb, alpha = big[..., :3], big[..., 3] / 255
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    light = np.clip(0.5 - (px + py) * 1.1, 0, 1)
    for off in (-0.02,):
        # a slightly tilted strap across the coil, only where there is flesh under it
        d = np.abs((py - off) - 0.18 * px)
        band = np.clip((0.04 - d) * n / 1.5, 0, 1) * (alpha > 0.5)
        edge = np.clip((0.054 - d) * n / 1.5, 0, 1) * (alpha > 0.5)
        rgb = lerp(rgb, CON_OUTLINE, edge)
        rgb = lerp(rgb, lerp((70, 40, 44), (140, 86, 84), light), band)
        rivet = np.clip(1 - np.sqrt((px - 0.12) ** 2 + (py - off - 0.18 * 0.12) ** 2) / 0.014, 0, 1) * band
        rgb = lerp(rgb, (200, 150, 140), rivet)
    return to_image(rgb, alpha, size)


BIOFERRITE, BIOFERRITE_LIGHT, BIOFERRITE_DARK = (92, 52, 56), (138, 86, 88), (52, 28, 32)


def constrictor_vat(size=256, top=False):
    """Constrictor vat, 2x2, seen from above: a round tank ringed in bioferrite,
    full of cloudy pink slurry, with four riveted feet. top=True paints only the
    overlay drawn over the floating constrictor: a glass sheen and the front lip."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    fill = lambda sdf: np.clip((-sdf) * n / 1.5, 0, 1)
    r = np.sqrt(px ** 2 + (py * 1.08) ** 2)
    rim_outer, rim_inner = 0.44, 0.37
    light = np.clip(0.5 - (px + py) * 1.1, 0, 1)
    rgb = np.zeros((n, n, 3), np.float32)
    if top:
        rgb[:] = (240, 230, 236)
        # the glass: a broad soft sheen top-left and a thin bright streak
        sheen = np.clip(1 - np.sqrt(((px + 0.14) / 0.2) ** 2 + ((py + 0.16) / 0.1) ** 2), 0, 1) * 0.35
        streak = np.clip(1 - np.abs((px + py * 0.6) + 0.2) / 0.012, 0, 1) * fill(r - rim_inner + 0.03) * 0.5
        alpha = np.maximum(sheen, streak) * fill(r - rim_inner)
        # the front half of the rim, so the constrictor sits inside the tank
        lip = fill(r - rim_outer) * (1 - fill(r - rim_inner)) * (py > 0.02)
        rgb = lerp(rgb, lerp(BIOFERRITE_DARK, BIOFERRITE_LIGHT, light), lip)
        alpha = np.maximum(alpha, lip)
        return to_image(rgb, alpha, size)
    wob = periodic_noise(n, 131, ((3, 1.0), (6, 0.5)))
    feet = np.full((n, n), 9.0, np.float32)
    for fx, fy in ((-0.33, -0.33), (0.33, -0.33), (-0.33, 0.33), (0.33, 0.33)):
        feet = np.minimum(feet, np.maximum(np.abs(px - fx), np.abs(py - fy)) - 0.07)
    body = np.minimum(r - rim_outer, feet)
    alpha = np.clip((0.022 - body) * n / 1.5, 0, 1)
    rgb[:] = (26, 14, 18)                                           # dark outline
    rgb = lerp(rgb, lerp(BIOFERRITE_DARK, BIOFERRITE, light), fill(feet + 0.02))
    rgb = lerp(rgb, lerp(BIOFERRITE_DARK, BIOFERRITE_LIGHT, light), fill(r - rim_outer + 0.018))
    # rivets round the rim
    ang = np.arctan2(py, px)
    rivet = np.clip(1 - np.abs(r - (rim_outer + rim_inner) / 2) / 0.012, 0, 1) * np.clip((np.cos(ang * 12) - 0.9) * 10, 0, 1)
    rgb = lerp(rgb, (180, 130, 128), rivet * 0.8)
    # the slurry: cloudy pink, darker at the edges and swirling
    inner = fill(r - rim_inner)
    depth = np.clip(r / rim_inner, 0, 1)
    slurry = lerp((226, 150, 170), (150, 78, 100), depth ** 2)
    slurry = lerp(slurry, (240, 190, 204), np.clip((wob - 0.6) * 3, 0, 1) * 0.5)
    rgb = lerp(rgb, (40, 20, 26), fill(r - rim_inner - 0.012))       # inner lip shadow
    rgb = lerp(rgb, slurry, inner)
    return to_image(rgb, alpha, size)


# ------------------------------------------------------------------ feedees meme icon
def feedees_icon(size=256):
    """Ideology meme icon for Feedees: a plump, heart-shaped belly with a soft
    navel crease, and a ladle pouring into it."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    line = 0.022
    fill = lambda sdf: np.clip((-sdf) * n / 1.5, 0, 1)
    # heart-shaped belly: two round lobes over a soft point
    lobes = np.minimum(np.sqrt((px + 0.13) ** 2 + (py - 0.02) ** 2), np.sqrt((px - 0.13) ** 2 + (py - 0.02) ** 2)) - 0.2
    point = np.maximum(np.abs(px) * 1.05 + (py - 0.02) * 0.9 - 0.3, -(py - 0.02))
    belly = np.minimum(lobes, point)
    # the ladle, tipped over the belly, and the stream pouring from it
    # a deep cup, tipped towards the belly (its rim faces down-right), on a long handle
    cup_c = (-0.2, -0.3)
    rr = np.sqrt((px - cup_c[0]) ** 2 + (py - cup_c[1]) ** 2)
    tip = (px - cup_c[0]) * 0.6 + (py - cup_c[1]) * 0.8          # toward the rim
    bowl = np.maximum(rr - 0.11, tip - 0.035)
    handle = _cap(px, py, -0.26, -0.38, -0.06, -0.47) - 0.024
    stream = _cap(px, py, -0.12, -0.22, -0.06, -0.1) - 0.03
    drop = np.sqrt((px + 0.04) ** 2 + (py + 0.05) ** 2) - 0.034
    shape = np.minimum.reduce([belly, bowl, handle, stream, drop])
    alpha = np.clip((line - shape) * n / 1.5, 0, 1)
    rgb = np.zeros((n, n, 3), np.float32)
    rgb[:] = (38, 22, 26)
    light = np.clip(0.5 - (px + py) * 1.1, 0, 1)
    rgb = lerp(rgb, lerp((170, 98, 104), (224, 162, 160), light), fill(belly + line * 0.9))
    navel = np.clip(1 - _arc(px, py, 0.0, 0.1, 0.06, 0.3, 2.84) / 0.012, 0, 1) * fill(belly)
    rgb = lerp(rgb, (120, 62, 70), navel * 0.9)
    shine = np.clip(1 - np.sqrt(((px + 0.17) / 0.07) ** 2 + ((py + 0.03) / 0.045) ** 2), 0, 1)
    rgb = lerp(rgb, (246, 214, 210), shine * 0.8 * fill(belly))
    rgb = lerp(rgb, lerp((150, 150, 158), (200, 200, 206), light), fill(np.minimum(bowl, handle) + line * 0.9))
    rgb = lerp(rgb, (238, 226, 206), fill(np.minimum(stream, drop) + line * 0.7))
    return to_image(rgb, alpha, size)


# ------------------------------------------------------------------ meld grenade
def meld_grenade(size=256):
    """Item art in RimWorld's style: muted colours, soft light from the top-left
    on every shape, and a dark tinted (not black) outline. A glass bulb packed
    with lumpy meld flesh under a dark cap with a spoon lever and pin ring."""
    n = size * SS
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    px, py = (x - n / 2) / n, (y - n / 2) / n
    line = 0.024                                    # outline thickness
    bulb = np.sqrt((px + 0.02) ** 2 + (py - 0.1) ** 2) - 0.29
    cap = np.maximum(np.abs(px + 0.02) - 0.15, np.abs(py + 0.22) - 0.07) - 0.02
    lever = np.maximum(np.abs(px - 0.2) - 0.035, np.abs(py + 0.04) - 0.2) - 0.015
    ring = np.abs(np.sqrt((px + 0.23) ** 2 + (py + 0.3) ** 2) - 0.07) - 0.018
    shape = np.minimum(np.minimum(bulb, cap), np.minimum(lever, ring))
    alpha = np.clip((line - shape) * n / 1.5, 0, 1)
    fill = lambda sdf: np.clip((-sdf) * n / 1.5, 0, 1)
    # soft light from the top-left, across the whole item
    light = np.clip(0.5 - (px + py) * 1.1, 0, 1)
    shade = lambda lit, dark: lerp(dark, lit, light)
    rgb = np.zeros((n, n, 3), np.float32)
    rgb[:] = (44, 30, 40)                           # dark plum outline under all fills
    # glass: faint, greyed lavender
    rgb = lerp(rgb, shade((214, 206, 220), (150, 140, 160)), fill(bulb))
    # meld flesh in the lower bulb: muted rose with a lumpy surface, darker lumps
    wob = periodic_noise(n, 5, ((3, 1.0), (5, 0.4)))
    level = 0.02 + 0.05 * (wob - 0.5) + 0.025 * np.sin(px * 26)
    meld = fill(bulb + 0.03) * np.clip((py - level) * n / 1.5, 0, 1)
    rgb = lerp(rgb, shade((190, 116, 150), (120, 62, 94)), meld)
    lumps = np.clip((wob - 0.55) * 6, 0, 1) * meld
    rgb = lerp(rgb, shade((150, 80, 116), (96, 44, 74)), lumps * 0.8)
    # a lighter meniscus where the flesh meets the glass
    surf = np.clip(1 - np.abs(py - level) / 0.012, 0, 1) * fill(bulb + 0.03)
    rgb = lerp(rgb, (208, 150, 176), surf * 0.7)
    # one soft highlight on the glass
    hl = np.clip(1 - np.sqrt(((px + 0.13) / 0.07) ** 2 + ((py + 0.0) / 0.13) ** 2), 0, 1)
    rgb = lerp(rgb, (240, 236, 242), hl * 0.75 * fill(bulb))
    # dark metal cap with a lighter band, the spoon lever and the pin ring
    rgb = lerp(rgb, shade((104, 102, 108), (58, 56, 62)), fill(cap + line * 0.9))
    band = fill(cap + line * 0.9) * (np.abs(py + 0.2) < 0.02)
    rgb = lerp(rgb, (132, 130, 136), band * 0.8)
    rgb = lerp(rgb, shade((112, 110, 116), (66, 64, 70)), fill(lever + line * 0.8))
    rgb = lerp(rgb, shade((168, 166, 170), (106, 104, 110)), fill(ring + line * 0.5))
    return to_image(rgb, alpha, size)


if __name__ == '__main__':
    import sys
    out = sys.argv[1]
    flesh_wall().save(f'{out}/RR_FleshWall_Atlas.png')
    hardened_flesh().save(f'{out}/RR_HardenedFlesh_Atlas.png')
    void_vein().save(f'{out}/RR_VoidBlobWall_Atlas.png')
    flesh_floor().save(f'{out}/RR_FleshFloor.png')
    bloat_geyser().save(f'{out}/RR_BloatGeyser.png')
    gorge_maw().save(f'{out}/RR_GorgeMaw.png')
    gorge_maw_full().save(f'{out}/RR_GorgeMaw_Full.png')
    gorge_maw_full(seam=False).save(f'{out}/RR_GorgeMaw_Bulge.png')
    entry_scar().save(f'{out}/RR_VoidMazeEntryScar.png')
    void_seam().save(f'{out}/RR_VoidSeam.png')
    # codex icon (goes to Textures/UI/CodexEntries)
    void_seam(128).save(f'{out}/RR_VoidSeam_Codex.png')
    silhouette(void_seam(128)).save(f'{out}/RR_VoidSeam_Silhouette.png')
    # gorge constrictor (goes to Textures/Things/Pawn/RR_GorgeConstrictor; the body is
    # saved as _south, _east, _north and _MenuIcon - it is the same curled worm from any side)
    for stage in range(5):
        constrictor_coil(256, stage).save(f'{out}/RR_ConstrictorCoil_{stage}.png')
        constrictor_face(128, stage).save(f'{out}/RR_ConstrictorFace_{stage}.png')
    constrictor_proboscis().save(f'{out}/RR_ConstrictorProboscis.png')
    constrictor_bolus().save(f'{out}/RR_ConstrictorBolus.png')
    constrictor_body(128).save(f'{out}/RR_GorgeConstrictor_south.png')
    # ideology (goes to Textures/UI/Memes)
    feedees_icon().save(f'{out}/RR_Feedees.png')
    # item art (goes to Textures/Things/Item/Equipment/WeaponRanged and Textures/Things/Projectile)
    meld_grenade().save(f'{out}/RR_MeldGrenade.png')
    meld_grenade(64).save(f'{out}/RR_MeldGrenadeThrown.png')
    # constrictor binding items (go to Textures/Things/Item/Special)
    constrictor_lure().save(f'{out}/RR_ConstrictorLure.png')
    bound_constrictor().save(f'{out}/RR_BoundConstrictor.png')
    # codex icon for the gorge constrictor (goes to Textures/UI/CodexEntries)
    constrictor_body(128).save(f'{out}/RR_GorgeConstrictor_Codex.png')
    silhouette(constrictor_body(128)).save(f'{out}/RR_GorgeConstrictor_Silhouette.png')
    # constrictor vat (goes to Textures/Things/Building/RR_ConstrictorVat)
    constrictor_vat().save(f'{out}/RR_ConstrictorVat.png')
    constrictor_vat(top=True).save(f'{out}/RR_ConstrictorVatTop.png')
