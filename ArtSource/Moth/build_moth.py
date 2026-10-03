"""Draw Moth-Chomp, the unlockable moth kaiju for Chomp Chomp Panic!, pixel by pixel.

Run: python build_moth.py
Writes Assets/Art/Moth/moth_{idle,walk,chomp,death}.png (64 x 64 frames, facing right, same layout as the
teal kaiju), sprite_frames.json for Chomp Chomp Panic > Import Art, and ArtSource/Moth/moth_preview_8x.png.
A pale green luna moth with eyespots and long tail streamers. It hovers and flaps instead of walking, chomps
with a pair of fanged mandibles and dies in a burst of wing dust.
Built with ArtSource/kaiju_kit.py. Requires Pillow and numpy. Output is deterministic.
"""
from pathlib import Path
import math
import random
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from kaiju_kit import OUTLINE, Frame, edge, ellipse, line, plate, poly, puff, ramp, rgb, write_clips

HERE = Path(__file__).resolve().parent

# Luna-moth green wings, darkest to lightest; the far pair sits in shadow.
WING = ramp("#1F4A3A", "#2F7A55", "#4FB070", "#94DC8E", "#D8F6C0")
WING_FAR = ramp("#16362C", "#1F4A3A", "#2F7A55", "#4FB070", "#94DC8E")
TRIM = ramp("#4A1E3A", "#7A3458")
SPOT = ramp("#4A1E3A", "#F0A830", "#FFF4D0")
# Fluffy body.
FUR = ramp("#6E6280", "#A79CB8", "#D8D0E6", "#F2EEF8", "#FFFFFF")
EYE = ramp("#6A0E24", "#C8243C", "#FF7A8A", "#FFD0D6")
ANTENNA = ramp("#A8862E", "#E8C76A")
CHITIN = rgb("#3A2A40")
FANG = ramp("#B6C0DE", "#F4F6FC")
SHADOW = rgb("#0B0E1C")
DUST = ramp("#2F7A55", "#94DC8E", "#D8F6C0")


def wing_pair(f, flap, colors, cx, cy, side, scale=1.0, droop=0):
    """Forewing and tailed hindwing on one side. `flap` 1 = raised, -1 = lowered; `side` -1 left, 1 right."""
    def p(x, y):
        return cx + side * x * scale, cy + y * scale

    tip_y = -20 - 8 * flap + droop
    fore = poly([p(0, -4), p(7, tip_y + 6), p(16, tip_y), p(26, tip_y - 1), p(30, tip_y + 4), p(30 - 2 * flap, tip_y + 13),
                 p(22, 3), p(8, 7)])
    hind = poly([p(0, 2), p(14, 2 - 2 * flap + droop), p(23, 7 + droop), p(22, 15 + droop), p(15, 19 + droop),
                 p(4, 15 + droop), p(0, 9)])
    # Long luna-moth tail streamer off the hindwing.
    tail = poly([p(11, 16 + droop), p(16, 17 + droop), p(15, 27 + droop), p(12, 29 + droop), p(10, 25 + droop)])
    for m in (hind | tail, fore):
        plate(f, m, colors, light=2)
        inner = m & ~edge(m)
        f.fill(edge(inner) & ~edge(inner & ~edge(inner)) & ~shifted_up(m), TRIM[1])
    # Eyespot on the forewing.
    sx, sy = p(20, tip_y + 6)
    r = 3 * scale
    f.fill(ellipse(sx, sy, r, r), SPOT[0])
    f.fill(ellipse(sx, sy, r - 1, r - 1), SPOT[1])
    f.put(round(sx), round(sy), SPOT[2])
    f.put(round(sx) - 1, round(sy) - 1, SPOT[2])


def shifted_up(mask):
    """Pixels on the top rim of a mask (kept free of trim so the light edge reads)."""
    out = mask.copy()
    out[1:] &= ~mask[:-1]
    return out


def moth(bob=0, flap=0.0, lunge=0, open_=0, dizzy=False, dim=False, fall=0, droop=0, shadow=True):
    f = Frame()
    y = bob + fall
    if shadow:
        sw = 10 - fall // 3
        f.fill(ellipse(30, 61, max(3, sw), 1.5), SHADOW)
    # Far wings (right, behind), then near wings (left), then the body over both.
    wing_pair(f, flap, WING_FAR, 32, 30 + y, 1, 0.85, droop)
    wing_pair(f, flap, WING, 28, 30 + y, -1, 1.0, droop)

    # Dangling legs.
    for lx, dx in ((26, -3), (29, -1), (32, 2)):
        line(f, lx, 40 + y, lx + dx, 47 + y, OUTLINE)
        f.put(lx + dx, 48 + y, OUTLINE)
    abdomen = ellipse(27, 45 + y, 7, 8)
    plate(f, abdomen, FUR)
    for by in (42, 46, 50):
        for bx in range(23, 32):
            if abdomen[min(63, by + y), bx] and not edge(abdomen)[min(63, by + y), bx]:
                f.put(bx, by + y, FUR[1])
    # Fluffy thorax: a ball with tufts poking out of its outline.
    thorax = ellipse(30, 33 + y, 9, 8)
    for tx, ty in ((22, 30), (24, 26), (28, 24), (33, 24), (37, 27), (21, 35), (38, 33)):
        thorax |= ellipse(tx, ty + y, 1, 1)
    plate(f, thorax, FUR, light=2)
    for tx, ty in ((25, 28), (29, 27), (33, 28), (27, 31)):
        f.put(tx, ty + y, FUR[4])
        f.put(tx + 1, ty + 1 + y, FUR[1])

    # Head with a big compound eye, feathery antennae and mandibles.
    hx, hy = 39 + lunge, 25 + y
    for ax, ay, tx, ty in ((36, 20, 30, 5), (41, 20, 50, 6)):
        line(f, ax + lunge, ay + y, tx + lunge, ty + y, ANTENNA[0])
        for k in range(1, 6):
            bx = round(ax + (tx - ax) * k / 6) + lunge
            by = round(ay + (ty - ay) * k / 6) + y
            f.put(bx - 1, by, ANTENNA[1])
            f.put(bx + 1, by, ANTENNA[1])
    head = ellipse(hx, hy, 7, 6)
    plate(f, head, FUR, light=2)
    ex, ey = hx + 3, hy - 1
    eye = ellipse(ex, ey, 4, 4)
    f.fill(eye, EYE[0] if dim else EYE[1])
    if not dim:
        f.fill(ellipse(ex - 1, ey - 1, 2, 2), EYE[2])
        f.put(ex - 2, ey - 2, EYE[3])
    f.fill(edge(eye), OUTLINE)
    if dizzy:
        for d in range(-2, 3):
            f.put(ex + d, ey + d, OUTLINE)
            f.put(ex + d, ey - d, OUTLINE)
    # Mandibles: two hooked fangs that swing apart to bite.
    mx, my = hx + 3, hy + 5
    if open_ >= 2:
        f.fill(ellipse(mx + 3, my, 2, open_ - 1), rgb("#241534"))
    for direction in (-1, 1):
        tipx = mx + 8
        tipy = my + direction * (1 + open_)
        jaw = poly([(mx - 1, my - direction), (mx + 4, my + direction * (2 + open_ // 2)), (tipx, tipy),
                    (tipx - 1, tipy + 2 * direction), (mx + 3, my + direction * (4 + open_ // 2)), (mx - 1, my + direction * 3)])
        plate(f, jaw, ramp("#1E1626", "#2A1F33", "#3A2A40", "#5A4664", "#7A6488"))
        # Fangs along the inner edge.
        for k in (2, 5):
            f.put(mx + k, my + direction * (1 + open_ * k // 8), FANG[1])
        f.put(tipx, tipy, FANG[1])
    return f


# ------------------------------------------------------------------ animations

def idle_frames():
    # Lazy hover: slow wingbeats, the body rising on the downstroke.
    return [moth(bob=b, flap=w) for b, w in ((0, 1.0), (-1, 0.2), (-1, -0.7), (0, 0.2))]


def walk_frames():
    # Fast flutter while it travels.
    beats = (1.0, 0.3, -0.6, -1.0, -0.3, 0.6)
    return [moth(bob=(0, -1, -2, -1, 0, 1)[i], flap=w) for i, w in enumerate(beats)]


def chomp_frames():
    # Wings flare, mandibles gape, the head darts forward and snaps.
    return [moth(flap=1.0, open_=3, lunge=1), moth(flap=1.0, open_=5, lunge=2, bob=-1), moth(flap=-0.4, open_=0, lunge=3),
            moth(flap=0.2, open_=1, lunge=1)]


def death_frames():
    rng = random.Random(23)
    frames = [
        # Stunned mid-air, wings locked up.
        moth(flap=1.0, open_=4, dizzy=True),
        # Spiralling down with wings collapsing.
        moth(flap=-1.0, open_=2, dizzy=True, dim=True, fall=8, droop=4),
        moth(flap=-1.0, open_=1, dizzy=True, dim=True, fall=14, droop=8, shadow=False),
    ]
    # Bursts into a cloud of wing scales.
    f = Frame()
    for cx, cy, r in ((30, 44, 10), (20, 38, 7), (40, 36, 7), (30, 30, 6)):
        puff(f, cx, cy, r, DUST)
    for _ in range(40):
        a, d = rng.uniform(0, math.tau), rng.uniform(10, 28)
        f.put(round(30 + math.cos(a) * d), round(40 + math.sin(a) * d * 0.8), rng.choice((DUST[2], SPOT[1], WING[3])))
    frames.append(f)
    # The dust drifts down and settles.
    for count, spread, floor in ((26, 22, 40), (14, 26, 52)):
        f = Frame()
        for _ in range(count):
            x = round(30 + rng.uniform(-spread, spread))
            yy = rng.randint(floor, 60)
            c = rng.choice((DUST[1], DUST[2], SPOT[1]))
            f.put(x, yy, c)
            if rng.random() < 0.4:
                f.put(x + 1, yy, c)
        frames.append(f)
    return frames


CLIPS = {"idle": idle_frames, "walk": walk_frames, "chomp": chomp_frames, "death": death_frames}

if __name__ == "__main__":
    write_clips("moth", "Moth", CLIPS, HERE)
