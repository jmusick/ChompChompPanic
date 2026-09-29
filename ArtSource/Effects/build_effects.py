"""Draw small pixel-art effects for Chomp Chomp Panic! procedurally.

Run: python build_effects.py
Writes to Assets/Art/Effects (imported as sliced 32 x 32 sprites, 32 px per unit like the people):
  blood_burst.png   6-frame cartoon blood spurt played when the kaiju eats a person
  blood_stains.png  4 ground splat variants left behind (faded out in game)
Requires Pillow only. Output is deterministic.
"""
from pathlib import Path
import math
import random
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
GAME = HERE.parents[1] / "Assets" / "Art" / "Effects"
FRAME = 32


def hexc(h):
    return tuple(bytes.fromhex(h[1:])) + (255,)


OUT = hexc("#10152D")  # shared navy outline
DARK = hexc("#6E1024")
MID = hexc("#B41E36")
LIGHT = hexc("#E84A5C")
SHINE = hexc("#FFB0B8")


def outline(img):
    """Turn every opaque pixel that touches transparency into navy outline."""
    px = img.load()
    w, h = img.size
    edge = [(x, y) for y in range(h) for x in range(w) if px[x, y][3] and any(
        not (0 <= nx < w and 0 <= ny < h) or px[nx, ny][3] == 0
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))]
    for p in edge:
        px[p] = OUT
    return img


def blob(d, x, y, r, color):
    d.ellipse([x - r, y - r, x + r, y + r], fill=color)


def droplet(d, x, y, r):
    """A shaded drop: dark body, lighter upper-left, a single shine pixel on bigger drops."""
    blob(d, x, y, r, MID)
    if r >= 2:
        blob(d, x - 0.6, y - 0.6, r - 1, LIGHT)
    if r >= 3:
        d.point((round(x - r * 0.4), round(y - r * 0.4)), fill=SHINE)


def burst_frames(rng):
    # Droplets fly out from the center and arc downwards as they slow.
    drops = []
    for k in range(12):
        angle = (k + rng.uniform(-0.3, 0.3)) * math.tau / 12
        drops.append((angle, rng.uniform(8, 14), rng.uniform(1.4, 3)))
    frames = []
    for i in range(6):
        img = Image.new("RGBA", (FRAME, FRAME))
        d = ImageDraw.Draw(img)
        c = FRAME / 2
        t = i / 5
        # Central splash: pops big, then shrinks away
        core = [4, 6.5, 5, 3.5, 2, 0][i]
        if core:
            blob(d, c, c, core, DARK)
            blob(d, c - 0.5, c - 0.5, core - 1, MID)
            if core > 3:
                blob(d, c - 1.5, c - 1.5, core - 3, LIGHT)
        if i >= 1:
            spread = 1 - (1 - t) ** 3
            for angle, dist, r in drops:
                x = c + math.cos(angle) * dist * spread
                y = c + math.sin(angle) * dist * spread * 0.85 + 5 * t * t  # droplets arc down
                size = r * (1.1 - 0.55 * t)
                if size >= 1:
                    droplet(d, x, y, size)
        frames.append(outline(img))
    return frames


def stain(rng):
    """Flat splat on the ground: a lumpy pool plus flung droplets, darker than the burst."""
    img = Image.new("RGBA", (FRAME, FRAME))
    d = ImageDraw.Draw(img)
    c = FRAME / 2
    for _ in range(5):
        blob(d, c + rng.uniform(-3, 3), c + rng.uniform(-2, 2), rng.uniform(3, 5), DARK)
    for _ in range(3):
        blob(d, c + rng.uniform(-2, 2), c + rng.uniform(-2, 1.5), rng.uniform(1.5, 3), MID)
    for _ in range(rng.randint(5, 8)):
        angle = rng.uniform(0, math.tau)
        dist = rng.uniform(8, 13)
        x, y = c + math.cos(angle) * dist, c + math.sin(angle) * dist * 0.8
        blob(d, x, y, rng.uniform(1, 2.2), DARK)
        # a smear connecting some drops back to the pool
        if rng.random() < 0.4:
            d.line([(c, c), (x, y)], fill=DARK, width=2)
    return outline(img)


def strip(frames):
    out = Image.new("RGBA", (FRAME * len(frames), FRAME))
    for i, f in enumerate(frames):
        out.paste(f, (i * FRAME, 0))
    return out


def build():
    GAME.mkdir(parents=True, exist_ok=True)
    strip(burst_frames(random.Random(7))).save(GAME / "blood_burst.png")
    rng = random.Random(8)
    strip([stain(rng) for _ in range(4)]).save(GAME / "blood_stains.png")
    print("wrote", GAME / "blood_burst.png", "and", GAME / "blood_stains.png")


if __name__ == "__main__":
    build()
