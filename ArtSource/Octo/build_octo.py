"""Draw Octo-Chomp, the unlockable octopus kaiju for Chomp Chomp Panic!, pixel by pixel.

Run: python build_octo.py
Writes Assets/Art/Octo/octo_{idle,walk,chomp,death}.png (64 x 64 frames, facing right, same layout as the
teal kaiju), sprite_frames.json for Chomp Chomp Panic > Import Art, and ArtSource/Octo/octo_preview_8x.png.
A violet deep-sea octopus with glowing cyan spots that walks on its tentacles and dies in a burst of ink.
Built with ArtSource/kaiju_kit.py. Requires Pillow and numpy. Output is deterministic.
"""
from pathlib import Path
import math
import random
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from kaiju_kit import OUTLINE, Frame, blobs, edge, ellipse, plate, poly, puff, ramp, rect, rgb, write_clips

HERE = Path(__file__).resolve().parent

# Violet hide, darkest to lightest; the far tentacles sit in shadow.
HIDE = ramp("#36205E", "#5A3A98", "#8058C8", "#A888E6", "#D8C4FA")
HIDE_FAR = [HIDE[0], HIDE[0], HIDE[1], HIDE[2], HIDE[3]]
# Bioluminescent spots.
GLOW = ramp("#1A6E7E", "#1FA3B5", "#5FE3E0", "#B4FFF6", "#E8FFFC")
SUCKER = ramp("#B0648E", "#E8A0C8")
EYE = ramp("#B07A1A", "#FFD45A", "#FFF2B8")
MOUTH = rgb("#241534")
TONGUE = rgb("#C37CD0")
TOOTH = ramp("#B6C0DE", "#F4F6FC")
INK = ramp("#120E24", "#2A1F4A", "#47367A")
BUBBLE = rgb("#8FD8E8")


def bezier(p0, p1, p2, p3, n):
    pts = []
    for i in range(n):
        t = i / (n - 1)
        a, b, c, d = (1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t ** 2, t ** 3
        pts.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return pts


def tentacle(frame, base, reach, lift, colors, suckers, thick=4.5, flat=0):
    """One arm from under the mantle down to the ground, curling out along it. `reach` is how far the tip
    lands from the base (negative = backwards), `lift` raises the curling tip, `flat` presses it to the ground."""
    bx, by = base
    side = 1 if reach >= 0 else -1
    ground = 59
    pts = bezier((bx, by), (bx + reach * 0.15, by + 14 - flat), (bx + reach * 0.8, ground + 1),
                 (bx + reach + side * 4, ground - 5 - lift + flat), 16)
    path = [(x, min(y, ground - r + 1), r) for (x, y), r in
            zip(pts, (thick - (thick - 1.2) * i / 15 for i in range(16)))]
    plate(frame, blobs(path), colors)
    if suckers:
        for x, y, r in path[5:14:2]:
            frame.put(round(x), round(y + r - 1), SUCKER[1])
            frame.put(round(x) + 1, round(y + r - 1), SUCKER[0])


def octo(bob=0, breathe=0, sway=(0,) * 6, lift=(0,) * 6, open_=3, glow=1, dizzy=False, sag=0, flat=0):
    """`sway`/`lift` per tentacle (back three, then front three), `open_` mouth gap, `sag` sinks the mantle."""
    f = Frame()
    top = bob + sag
    # Far tentacles first, then the mantle, then the near ones overlapping its underside.
    for i, (bx, reach) in enumerate(((14, -14), (24, -4), (34, 10))):
        tentacle(f, (bx, 34 + top), reach + sway[i], lift[i], HIDE_FAR, False, 4, flat)
    head = ellipse(30, 22 + top, 18, 15 + breathe - sag // 2) | ellipse(21, 14 + top, 13, 11 + breathe - sag // 3)
    plate(f, head, HIDE, light=2)
    # Glowing spots along the dome.
    for i, (sx, sy, r) in enumerate(((14, 12, 2), (21, 7, 1), (27, 11, 2), (12, 21, 1), (20, 18, 1), (33, 6, 1))):
        spot = ellipse(sx, sy + top, r, r) & head & ~edge(head)
        level = max(0, min(4, glow + (i % 2)))
        f.fill(spot, GLOW[level])
    for i, (bx, reach) in enumerate(((18, -16), (27, 3), (36, 15))):
        tentacle(f, (bx, 34 + top), reach + sway[3 + i], lift[3 + i], HIDE, True, 5, flat)

    # Eye: a big yellow ball with an octopus's sideways slit.
    ex, ey = 40, 19 + top
    ball = ellipse(ex, ey, 6, 5 + (0 if sag < 6 else -2))
    f.fill(ball, EYE[1])
    f.fill(ball & ~ellipse(ex - 1, ey - 1, 5, 4), EYE[0])
    f.fill(edge(ball), OUTLINE)
    if dizzy:
        for d in range(-2, 3):
            f.put(ex + d, ey + d, OUTLINE)
            f.put(ex + d, ey - d, OUTLINE)
    else:
        f.fill(rect(ex - 2, ey - 1, ex + 4, ey), OUTLINE)
        f.put(ex - 2, ey - 3, EYE[2])
        f.put(ex - 3, ey - 2, EYE[2])
    # Brow ridge.
    for d in range(-5, 5):
        f.put(ex + d, ey - 6 - (1 if abs(d) < 3 else 0), OUTLINE)

    # Mouth on the front of the mantle, teeth top and bottom.
    my = 29 + top
    mouth = poly([(33, my - 1), (51, my - 4), (52, my + 1 + open_), (46, my + 3 + open_ * 2), (37, my + 3 + open_)])
    f.fill(mouth, MOUTH)
    if open_ >= 2:
        f.fill(ellipse(44, my + 3 + open_, 5, max(1, open_ // 2 + 1)) & mouth, TONGUE)
    f.fill(edge(mouth), OUTLINE)
    # Fangs hang from the top lip; a lower row shows once the mouth opens.
    for tx in range(36, 51, 3):
        ty = my - 1 - (tx - 33) * 3 // 18
        for k, half in enumerate((1, 1, 0)):
            for dx in range(-half, half + 1):
                f.put(tx + dx, ty + 1 + k, TOOTH[1] if dx <= 0 else TOOTH[0])
        if open_ >= 2:
            by_ = my + 2 + open_ + (1 if tx < 44 else 0)
            for k, half in enumerate((1, 0)):
                for dx in range(-half, half + 1):
                    f.put(tx + 1 + dx, by_ - k, TOOTH[1] if dx <= 0 else TOOTH[0])
    return f


# ------------------------------------------------------------------ animations

def idle_frames():
    # Breathing: the dome swells, the arms ripple and the spots pulse.
    out = []
    for i in range(4):
        sway = tuple(round(math.sin(i * math.pi / 2 + k)) for k in range(6))
        out.append(octo(bob=(0, 1, 1, 0)[i], breathe=(0, 1, 1, 0)[i], sway=sway, glow=(1, 2, 3, 2)[i]))
    return out


def walk_frames():
    # A rolling crawl: arms reach and pull in a wave from back to front, the body bobbing over them.
    out = []
    for i in range(6):
        phase = i * math.tau / 6
        sway = tuple(round(4 * math.sin(phase + k * 2.1)) for k in range(6))
        lift = tuple(max(0, round(4 * math.sin(phase + k * 2.1 + 1.2))) for k in range(6))
        out.append(octo(bob=(0, 1, 1, 0, 1, 1)[i], sway=sway, lift=lift, glow=1 + i % 3))
    return out


def chomp_frames():
    # Rear back and gape with arms flared, snap shut, settle.
    flare = (4, 3, 3, 4, 2, 4)
    return [octo(bob=-1, open_=5, lift=flare, glow=3), octo(bob=-2, open_=8, lift=flare, glow=4),
            octo(bob=1, open_=0, glow=3), octo(open_=2, glow=2)]


def death_frames():
    rng = random.Random(11)
    frames = [
        # Seeing stars, mouth wailing.
        octo(open_=6, dizzy=True, glow=0, lift=(3,) * 6),
        # Deflating onto its arms.
        octo(open_=4, dizzy=True, glow=0, sag=7, flat=3),
        octo(open_=2, dizzy=True, glow=0, sag=14, flat=5),
    ]
    # Bursts in a cloud of ink.
    f = Frame()
    for cx, cy, r in ((30, 42, 13), (18, 34, 9), (42, 32, 9), (30, 24, 8), (14, 50, 7), (46, 50, 7)):
        puff(f, cx, cy, r, INK)
    for _ in range(14):
        a, d = rng.uniform(0, math.tau), rng.uniform(16, 26)
        f.put(round(30 + math.cos(a) * d), round(40 + math.sin(a) * d * 0.8), INK[2])
    frames.append(f)
    # A spreading puddle with bubbles rising off it.
    for size, bubbles in ((1.0, 7),):
        f = Frame()
        pool = ellipse(30, 56, 26 * size, 6 * size) | ellipse(18, 54, 10 * size, 4 * size) | ellipse(44, 57, 10 * size, 4 * size)
        f.fill(pool, INK[1])
        f.fill(pool & ellipse(26, 54, 14 * size, 3 * size), INK[2])
        f.fill(edge(pool), OUTLINE)
        for _ in range(bubbles):
            bx, by, r = rng.randint(12, 50), rng.randint(26, 48), rng.choice((1, 1, 2))
            ring = edge(ellipse(bx, by, r + 1, r + 1))
            f.fill(ring, BUBBLE)
            f.put(bx - 1, by - 1, GLOW[4])
        frames.append(f)
    # Last: just a few drifting bubbles over a small stain.
    f = Frame()
    pool = ellipse(30, 58, 14, 3)
    f.fill(pool, INK[1])
    f.fill(edge(pool), OUTLINE)
    for bx, by in ((20, 44), (36, 36), (44, 48)):
        f.fill(edge(ellipse(bx, by, 2, 2)), BUBBLE)
    frames.append(f)
    return frames


CLIPS = {"idle": idle_frames, "walk": walk_frames, "chomp": chomp_frames, "death": death_frames}

if __name__ == "__main__":
    write_clips("octo", "Octo", CLIPS, HERE)
