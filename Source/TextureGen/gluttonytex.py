"""
Textures for RimRound's gluttonous Anomaly content (procedural):

    Textures/Things/Item/Special/RR_BottomlessBowl.png   the bottomless bowl: a dented old bowl brimming with dark slop
    Textures/UI/PsychicRituals/RR_Lipophagy.png          lipophagy's icon: fat drawn from one body into another

    python gluttonytex.py
"""
import os

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


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


def ellipse(x, y, cx, cy, rx, ry):
    return ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2


def bowl():
    n = 256
    rng = np.random.default_rng(0xB0E1)
    x, y = grid(n)
    dent = 0.025 * (fbm(n, rng, (3, 6)) - 0.5)

    # the bowl's body: a squat rounded shape below the rim, in a three-quarter view
    body = ellipse(x, y, 0.0, -0.05, 0.86 + dent, 0.62)
    body_mask = (body < 1) & (y < 0.1)
    rim = ellipse(x, y, 0.0, 0.1, 0.9 + dent, 0.36)
    rim_mask = rim < 1
    inner = ellipse(x, y, 0.0, 0.12, 0.8 + dent, 0.29)
    inner_mask = inner < 1
    alpha = np.clip(np.where(body_mask | rim_mask, 1.0, 0.0), 0, 1)

    # battered tin: dull grey-brown, lit from the top left, dark underneath, a few dents
    metal = np.array([0.46, 0.42, 0.38])
    shade = 0.75 + 0.35 * np.clip(-x * 0.4 + (y + 0.6) * 0.5, -0.5, 1)
    dents = 0.85 + 0.25 * fbm(n, rng, (8, 16))
    col = metal * (shade * dents)[..., None]
    lip = np.clip(1 - np.abs(rim - 1) / 0.12, 0, 1) * rim_mask
    col = col * (1 - lip[..., None] * 0.4) + np.array([0.78, 0.74, 0.68]) * lip[..., None] * 0.4

    # the slop: thick, dark violet, glossy, heaped a little over the rim, flecked with gluttonium
    heap = inner_mask | ((ellipse(x, y, 0.0, 0.2, 0.7, 0.3) < 1) & (y > 0.1))
    slop = np.array([0.24, 0.12, 0.30]) * (0.8 + 0.4 * fbm(n, rng, (6, 12, 24)))[..., None]
    gloss = np.clip(1 - ellipse(x, y, -0.25, 0.3, 0.28, 0.08), 0, 1) ** 2
    slop = slop + np.array([0.75, 0.6, 0.85]) * gloss[..., None] * 0.6
    specks = np.zeros((n, n))
    for _ in range(45):
        px, py = rng.uniform(-0.7, 0.7), rng.uniform(0.0, 0.38)
        r = rng.uniform(0.012, 0.024)
        specks = np.maximum(specks, np.clip(1 - np.hypot(x - px, (y - py) * 1.6) / r, 0, 1) ** 0.6)
    specks *= 0.95
    slop = slop * (1 - specks[..., None]) + np.array([0.35, 0.75, 1.0]) * specks[..., None]
    col = np.where(heap[..., None], slop, col)
    alpha = np.maximum(alpha, heap.astype(float))

    # a few wisps of steam above it
    steam = np.zeros((n, n))
    for cx, ph in ((-0.3, 0.0), (0.05, 1.7), (0.35, 3.1)):
        wob = cx + 0.08 * np.sin(y * 9 + ph)
        d = np.abs(x - wob)
        steam = np.maximum(steam, np.clip(1 - d / 0.05, 0, 1) * np.clip((y - 0.35) / 0.15, 0, 1) * np.clip((0.95 - y) / 0.4, 0, 1))
    steam *= 0.45
    col = col * (1 - steam[..., None]) + np.array([0.95, 0.92, 1.0]) * steam[..., None]
    alpha = np.maximum(alpha, steam)

    # soft antialiased edge
    from PIL import ImageFilter
    a_img = Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
    alpha = np.minimum(alpha, np.asarray(a_img) / 255 + 0.0)
    save(np.dstack([col, alpha]), "Textures/Things/Item/Special/RR_BottomlessBowl.png")


def lipophagy_icon():
    n = 256
    x, y = grid(n)
    # two figures: a gaunt one on the left, a swollen one on the right, joined by a stream
    def figure(cx, belly):
        head = ellipse(x, y, cx, 0.42, 0.13, 0.13) < 1
        torso = ellipse(x, y, cx, -0.12, 0.16 + belly, 0.38) < 1
        return head | torso

    left = figure(-0.55, 0.0)
    right = figure(0.5, 0.22)
    t = np.clip((x + 0.4) / 0.75, 0, 1)
    path_y = -0.05 + 0.32 * np.sin(t * np.pi)
    width = 0.07 - 0.03 * t
    stream = (np.abs(y - path_y) < width) & (x > -0.42) & (x < 0.33)
    drops = np.zeros((n, n), bool)
    for tx in (0.15, 0.45, 0.75):
        px = -0.4 + 0.75 * tx
        py = -0.05 + 0.32 * np.sin(tx * np.pi)
        drops |= ellipse(x, y, px, py, 0.075, 0.075) < 1
    shape = left | right | stream | drops
    glow = np.clip(1 - np.abs(y - path_y) / 0.25, 0, 1) * ((x > -0.5) & (x < 0.45)) * 0.35
    alpha = np.maximum(shape.astype(float), glow)
    col = np.ones((n, n, 3)) * np.array([0.92, 0.88, 0.95])
    col = np.where(left[..., None], np.array([0.62, 0.6, 0.66]), col)
    from PIL import ImageFilter
    a_img = Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.0))
    alpha = np.asarray(a_img) / 255
    save(np.dstack([col, alpha]), "Textures/UI/PsychicRituals/RR_Lipophagy.png")


if __name__ == "__main__":
    bowl()
    lipophagy_icon()
