"""
Textures for the Gorge World's gullets (procedural):

    Textures/Things/Building/GorgeWorld/RR_Gullet.png          a gullet, open: a ringed, wet red throat in the flesh
    Textures/Things/Building/GorgeWorld/RR_Gullet_Closed.png   the same gullet, clenched shut while it digests

    python guttex.py
"""
import os

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

FLESH = np.array([0.72, 0.26, 0.28])
CREASE = np.array([0.36, 0.08, 0.11])
LIP = np.array([0.88, 0.45, 0.47])
SLIME = np.array([0.95, 0.80, 0.62])


def value_noise(n, scale, rng):
    g = rng.random((scale + 1, scale + 1))
    x = np.linspace(0, scale, n, endpoint=False)
    x0 = np.floor(x).astype(int)
    t = x - x0
    t = t * t * (3 - 2 * t)
    a = g[x0][:, x0] * (1 - t)[None, :] + g[x0][:, x0 + 1] * t[None, :]
    b = g[x0 + 1][:, x0] * (1 - t)[None, :] + g[x0 + 1][:, x0 + 1] * t[None, :]
    return a * (1 - t)[:, None] + b * t[:, None]


def fbm(n, rng, octaves=(4, 8, 16, 32)):
    out, amp, tot = np.zeros((n, n)), 1.0, 0.0
    for s in octaves:
        out += value_noise(n, s, rng) * amp
        tot += amp
        amp *= 0.5
    return out / tot


def grid(n):
    y, x = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
    return x, -y  # +y up


def save(rgba, rel):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray((np.clip(rgba, 0, 1) * 255).astype(np.uint8), "RGBA").save(path)
    print(rel)


def gullet(closed):
    n = 384
    rng = np.random.default_rng(0x6E11)
    x, y = grid(n)
    r = np.hypot(x, y)
    a = np.arctan2(y, x)
    wobble = 1 + 0.07 * (fbm(n, rng, (3, 6)) - 0.5) * 2
    alpha = np.clip((0.96 * wobble - r) / 0.06, 0, 1)

    # a raised, folded ring of flesh around the opening
    folds = np.abs(fbm(n, rng, (4, 8, 16)) - 0.5) * 2
    crease = np.clip(1 - folds, 0, 1) ** 8
    col = FLESH * (1 - crease[..., None] * 0.5) + CREASE * (crease[..., None] * 0.5)
    col *= (0.88 + 0.14 * fbm(n, rng))[..., None]
    rim = np.clip(1 - np.abs(r - 0.55) / 0.22, 0, 1)
    col = col * (1 - rim[..., None] * 0.35) + LIP * (rim[..., None] * 0.35)
    shade = 1 - 0.25 * np.clip(r / 0.96, 0, 1) ** 2 + 0.08 * (y - x) / 2
    col *= shade[..., None]

    # radial pleats drawn in towards the throat
    hole_r = 0.06 if closed else 0.30
    swirl = a * 14 + 1.6 * fbm(n, rng, (3, 6))
    pleat = np.clip(np.cos(swirl), 0, 1) ** 4
    band = np.clip(1 - np.abs(r - (hole_r + 0.16)) / (0.30 if closed else 0.20), 0, 1)
    col = col * (1 - (pleat * band * 0.6)[..., None]) + CREASE * (pleat * band * 0.6)[..., None]

    if closed:
        # clenched: the pleats meet in a tight, dark knot
        knot = np.clip((0.12 - r) / 0.08, 0, 1)[..., None]
        col = col * (1 - knot * 0.8) + np.array([0.18, 0.03, 0.05]) * knot * 0.8
    else:
        # open: rings of muscle falling away down the throat into the dark
        hole = np.clip((hole_r - r) / 0.05, 0, 1)
        depth = np.clip(r / hole_r, 0, 1)
        rings = 0.5 + 0.5 * np.cos(depth * 22)
        inner = np.array([0.40, 0.07, 0.10])[None, None, :] * (0.55 + 0.45 * rings[..., None])
        inner = inner * depth[..., None] ** 1.4 + np.array([0.03, 0.0, 0.01]) * (1 - depth[..., None] ** 1.4)
        col = col * (1 - hole[..., None]) + inner * hole[..., None]

    # a sheen of slime on the lip
    lip = np.clip(1 - np.abs(r - (hole_r + 0.03)) / 0.04, 0, 1) * (0.4 + 0.6 * fbm(n, rng, (6, 12)))
    col = col * (1 - lip[..., None] * 0.45) + SLIME * lip[..., None] * 0.45
    name = "RR_Gullet_Closed" if closed else "RR_Gullet"
    save(np.dstack([col, alpha]), f"Textures/Things/Building/GorgeWorld/{name}.png")


if __name__ == "__main__":
    gullet(False)
    gullet(True)
