"""Draw Mecha-Chomp, the unlockable robot kaiju for Chomp Chomp Panic!, pixel by pixel.

Run: python build_mecha.py
Writes Assets/Art/Mecha/mecha_{idle,walk,chomp,death}.png (64 x 64 frames, facing right, same layout as the
teal kaiju), sprite_frames.json for Chomp Chomp Panic > Import Art, and ArtSource/Mecha/mecha_preview_8x.png.
The robot is built from flat-shaded plates (ArtSource/kaiju_kit.py), each with its own navy outline, painted back
to front. Requires Pillow and numpy. Output is deterministic.
"""
from pathlib import Path
import math
import random
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from kaiju_kit import (OUTLINE, SIZE, Frame, chamfer, edge, ellipse, line, plate, poly, puff, rect, rgb,
                       write_clips)

HERE = Path(__file__).resolve().parent

# Gunmetal plating, darkest to lightest.
STEEL = [rgb(h) for h in ("#343B5E", "#56608A", "#8590B8", "#B6C0DE", "#E6EBF7")]
# The far leg sits in shadow.
STEEL_FAR = [STEEL[0], STEEL[0], STEEL[1], STEEL[2], STEEL[3]]
# Hazard-orange trim.
TRIM = [rgb(h) for h in ("#5E2414", "#9A3A16", "#D8621E", "#F59A3A", "#FFD08A")]
# Hot-pink glow: visor, reactor and the furnace in the throat.
GLOW = [rgb(h) for h in ("#5A1648", "#A3286E", "#E8479A", "#FF8CC6", "#FFE0F0")]
MOUTH = rgb("#241534")
TOOTH = [rgb("#9AA4C4"), rgb("#F4F6FC")]
SMOKE = [rgb(h) for h in ("#2C3048", "#4A4F68", "#6E7390")]
FIRE = [rgb(h) for h in ("#9A3A16", "#F06A1E", "#FFB23A", "#FFF1B0")]


# ------------------------------------------------------------------ the robot

def leg(frame, x, lift, ramp, squash=0):
    """Thigh, piston shin and a wide clawed foot. `x` is the foot's left edge, `lift` raises it off the ground."""
    ground = 59 - lift
    foot = chamfer(x - 1, ground - 5, x + 14, ground, 2)
    shin = chamfer(x + 2, min(ground - 11 + squash, ground - 5), x + 10, ground - 3, 1)
    thigh = chamfer(x - 1, ground - 20 + squash, x + 12, ground - 8 + squash, 3)
    plate(frame, shin, ramp)
    # Hydraulic piston on the shin.
    line(frame, x + 6, ground - 8 + squash, x + 6, ground - 4, ramp[4])
    plate(frame, thigh, ramp, light=2)
    plate(frame, foot, ramp)
    # Toe claws.
    for tx in (x + 9, x + 12):
        frame.put(tx + 1, ground, OUTLINE)
        frame.put(tx + 1, ground - 1, TOOTH[1])
    # Knee bolt.
    frame.fill(rect(x + 5, ground - 15 + squash, x + 6, ground - 14 + squash), TRIM[3])


def tail(frame, bob, sway):
    # Three segments, thick at the hip and tapering to a spike, drawn tip first so the root overlaps.
    plate(frame, poly([(0, 45 + bob + sway), (6, 39 + bob + sway), (7, 46 + bob + sway)]), TRIM)
    plate(frame, chamfer(3, 37 + bob + sway, 12, 46 + bob + sway, 2), STEEL, light=2)
    plate(frame, chamfer(8, 34 + bob + sway // 2, 19, 46 + bob + sway // 2, 2), STEEL, light=2)
    for sx, dy in ((7, sway), (13, sway // 2)):
        frame.put(sx, 40 + bob + dy, TRIM[3])


def fins(frame, bob, glow):
    """Three exhaust fins along the back, orange-tipped, with a pink vent glow between them."""
    for i, (bx, by, h) in enumerate(((9, 23, 11), (15, 15, 11), (22, 10, 11))):
        by += bob
        fin = poly([(bx, by + h), (bx + 2, by), (bx + 6, by - 1), (bx + 8, by + h)])
        plate(frame, fin, TRIM, light=2)
        frame.put(bx + 4, by + 1, TRIM[4])


def torso(frame, bob, glow):
    body = poly([(14, 31 + bob), (22, 21 + bob), (40, 18 + bob), (49, 26 + bob), (50, 43 + bob), (43, 49 + bob),
                 (21, 49 + bob), (14, 43 + bob)])
    plate(frame, body, STEEL, light=2)
    # Belly armour: a lighter plate with ribbed seams.
    belly = poly([(30, 38 + bob), (46, 34 + bob), (48, 44 + bob), (42, 49 + bob), (30, 49 + bob)]) & body
    belly &= ~edge(body)
    plate(frame, belly, [STEEL[2], STEEL[2], STEEL[3], STEEL[4], STEEL[4]], outline=False)
    for sy in (41, 45):
        for sx in range(31, 48):
            if belly[sy + bob, sx]:
                frame.put(sx, sy + bob, STEEL[1])
    # Hazard stripe across the shoulder.
    for i in range(8):
        sx, sy = 24 + i * 2, 24 + bob - i // 2
        if body[sy, sx] and not edge(body)[sy, sx]:
            frame.put(sx, sy, TRIM[2])
            frame.put(sx + 1, sy, TRIM[3])
    # Reactor core.
    cx, cy = 25, 36 + bob
    plate(frame, ellipse(cx, cy, 4, 4), STEEL, light=2)
    core = ellipse(cx, cy, 2, 2)
    frame.fill(core, GLOW[2 + glow])
    frame.put(cx - 1, cy - 1, GLOW[4])
    # Rivets.
    for rx, ry in ((18, 34), (18, 42), (36, 22), (44, 28)):
        frame.put(rx, ry + bob, STEEL[4])
        frame.put(rx + 1, ry + 1 + bob, STEEL[0])


def head(frame, bob, open_, glow, visor=True, droop=0):
    """Boxy head with a piston jaw. `open_` (pixels) drops the jaw and lifts the skull."""
    lift = open_ // 3
    y = bob - lift + droop
    jy = bob + open_ + droop
    skull = poly([(32, 18 + y), (36, 9 + y), (52, 7 + y), (60, 11 + y), (62, 17 + y), (61, 25 + y), (36, 26 + y),
                  (32, 23 + y)])
    jaw = poly([(37, 27 + jy), (60, 27 + jy), (59, 32 + jy), (54, 35 + jy), (39, 35 + jy), (36, 31 + jy)])
    # Throat: dark, with the furnace glowing at the back. Spans the gap between skull and jaw.
    if open_ > 0:
        throat = poly([(37, 22 + y), (60, 22 + y), (60, 30 + jy), (37, 30 + jy)])
        frame.fill(throat, MOUTH)
        frame.fill(ellipse(41, (26 + y + 28 + jy) / 2, 3, max(1, open_ // 2)) & throat, GLOW[2])
        frame.put(40, (26 + y + 28 + jy) // 2, GLOW[4])
    plate(frame, jaw, STEEL)
    frame.fill(edge(jaw), OUTLINE)
    plate(frame, skull, STEEL, light=2)
    # Teeth: upper fangs hang from the skull, lower ones stand on the jaw.
    for tx in range(40, 60, 4):
        top = 26 + y
        for k, half in enumerate((1, 1, 0)):
            for dx in range(-half, half + 1):
                frame.put(tx + dx, top + k, TOOTH[1] if dx <= 0 else TOOTH[0])
        bottom = 27 + jy
        for k, half in enumerate((1, 1, 0)):
            for dx in range(-half, half + 1):
                frame.put(tx + 2 + dx, bottom - k, TOOTH[1] if dx <= 0 else TOOTH[0])
    # Visor slit.
    vy = 13 + y
    frame.fill(rect(44, vy, 57, vy + 3), OUTLINE)
    if visor:
        frame.fill(rect(45, vy + 1, 56, vy + 2), GLOW[1 + glow])
        frame.fill(rect(50, vy + 1, 54, vy + 1), GLOW[3 + min(glow, 1)])
    # Brow trim and a cheek vent.
    line(frame, 38, 11 + y, 50, 9 + y, TRIM[3])
    line(frame, 38, 12 + y, 50, 10 + y, TRIM[2])
    for vx in (37, 39, 41):
        frame.put(vx, 20 + y, OUTLINE)
        frame.put(vx, 21 + y, OUTLINE)
    # Antenna with a blinking tip.
    line(frame, 40, 9 + y, 38, 3 + y, OUTLINE)
    line(frame, 41, 9 + y, 39, 4 + y, STEEL[3])
    tip = ellipse(38, 2 + y, 1, 1)
    frame.fill(tip, GLOW[3] if glow else GLOW[1])
    frame.fill(edge(ellipse(38, 2 + y, 2, 2)) & ~tip, OUTLINE)


def arm(frame, bob, raise_=0):
    y = bob - raise_
    upper = chamfer(39, 36 + y, 47, 43 + y, 2)
    fore = chamfer(44, 40 + y, 52, 45 + y, 1)
    plate(frame, upper, STEEL, light=2)
    plate(frame, fore, STEEL)
    for cx in (53, 54):
        frame.put(cx, 40 + y + (cx - 53), OUTLINE)
        frame.put(cx, 45 + y - (cx - 53), OUTLINE)
    frame.put(53, 41 + y, TOOTH[1])
    frame.put(53, 44 + y, TOOTH[1])


def robot(bob=0, back=(18, 0), front=(31, 0), sway=0, open_=2, glow=1, visor=True, raise_=0, droop=0, squash=0):
    f = Frame()
    leg(f, back[0], back[1], STEEL_FAR, squash)
    tail(f, bob, sway)
    fins(f, bob, glow)
    torso(f, bob, glow)
    leg(f, front[0], front[1], STEEL, squash)
    head(f, bob, open_, glow, visor, droop)
    arm(f, bob, raise_)
    return f


# ------------------------------------------------------------------ effects for the death animation

def sparks(frame, rng, cx, cy, count, reach):
    for _ in range(count):
        a = rng.uniform(0, math.tau)
        r = rng.uniform(1, reach)
        x, y = round(cx + math.cos(a) * r), round(cy + math.sin(a) * r)
        frame.put(x, y, FIRE[3])
        frame.put(x + (1 if math.cos(a) > 0 else -1), y, FIRE[2])


def scrap(frame, x, y, kind):
    """Loose bolts, plates and a little gear."""
    if kind == 0:
        frame.fill(rect(x, y, x + 2, y + 1), STEEL[3])
        frame.fill(edge(rect(x - 1, y - 1, x + 3, y + 2)), OUTLINE)
    elif kind == 1:
        frame.fill(ellipse(x, y, 2, 2), STEEL[2])
        frame.put(x, y, OUTLINE)
        for dx, dy in ((0, -3), (3, 0), (0, 3), (-3, 0)):
            frame.put(x + dx, y + dy, STEEL[3])
    else:
        frame.put(x, y, TRIM[3])
        frame.put(x + 1, y, TRIM[2])


def death_frames():
    rng = random.Random(7)
    frames = []
    # 0: short-circuit. Visor dies, jaw hangs, sparks off the core.
    f = robot(open_=5, glow=0, visor=False, raise_=3)
    sparks(f, rng, 26, 38, 14, 10)
    sparks(f, rng, 50, 14, 8, 6)
    frames.append(f)
    # 1-2: buckling at the knees, head dropping.
    f = robot(bob=6, open_=4, glow=0, visor=False, droop=3, squash=6)
    sparks(f, rng, 30, 40, 10, 12)
    frames.append(f)
    f = robot(bob=13, open_=3, glow=0, visor=False, droop=6, squash=11)
    sparks(f, rng, 26, 50, 8, 10)
    frames.append(f)
    # 3: the reactor goes up.
    f = robot(bob=13, open_=3, glow=0, visor=False, droop=6, squash=11)
    for r, color in ((19, FIRE[0]), (15, FIRE[1]), (10, FIRE[2]), (5, FIRE[3])):
        m = ellipse(30, 42, r, r * 0.85)
        f.fill(m, color)
    f.fill(edge(ellipse(30, 42, 19, 19 * 0.85)), OUTLINE)
    sparks(f, rng, 30, 42, 24, 26)
    frames.append(f)
    # 4: smoke and flying scrap.
    f = Frame()
    for cx, cy, r in ((22, 44, 8), (36, 40, 9), (30, 30, 7), (44, 48, 6), (16, 52, 5)):
        puff(f, cx, cy, r, SMOKE)
    for x, y, k in ((6, 30, 0), (54, 26, 1), (12, 14, 2), (58, 44, 0), (40, 16, 2), (4, 50, 1)):
        scrap(f, x, y, k)
    frames.append(f)
    # 5: a few wisps over scattered parts.
    f = Frame()
    for cx, cy, r in ((26, 46, 4), (38, 42, 3)):
        puff(f, cx, cy, r, SMOKE)
    for x, y, k in ((10, 56, 0), (20, 58, 1), (34, 57, 2), (46, 56, 0), (54, 58, 1), (28, 54, 2)):
        scrap(f, x, y, k)
    frames.append(f)
    return frames


# ------------------------------------------------------------------ animations

def idle_frames():
    # Breathing hum: the body bobs, the core pulses and the antenna blinks.
    return [robot(bob=b, open_=2, glow=g) for b, g in ((0, 1), (1, 2), (1, 1), (0, 0))]


def walk_frames():
    # Heavy stride: each foot lifts and swings forward while the body dips on the planted leg.
    poses = [((16, 0), (33, 3), 0, -1), ((18, 0), (30, 1), 1, 0), ((20, 0), (28, 0), 1, 1),
             ((19, 3), (29, 0), 0, 1), ((17, 1), (31, 0), 1, 0), ((15, 0), (33, 0), 1, -1)]
    return [robot(bob=b, back=bk, front=fr, sway=s, glow=1 + (i % 2)) for i, (bk, fr, b, s) in enumerate(poses)]


def chomp_frames():
    # Gape, snap shut hard (body jolts forward), then settle.
    return [robot(open_=9, glow=2, raise_=2), robot(open_=12, glow=2, raise_=3), robot(bob=1, open_=0, glow=2),
            robot(open_=2, glow=1)]


CLIPS = {"idle": idle_frames, "walk": walk_frames, "chomp": chomp_frames, "death": death_frames}


def build():
    write_clips("mecha", "Mecha", CLIPS, HERE)


if __name__ == "__main__":
    build()
