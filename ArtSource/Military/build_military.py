"""Draw the military prey tiers and their weapon effects for Chomp Chomp Panic! procedurally.

Run: python build_military.py
Writes to Assets/Art/Military (sliced by the frame sizes in sprite_frames.json, 64 px per unit):
  soldier_<variant>_{run,idle,shoot}.png  32 x 32 chibi soldiers, side view facing right (like the people)
  jeep_army_{side,up,down}.png            64 x 32, same layout as the cars
  tank_army_{side,up,down}.png            128 x 64, twice a car
  plane_jet_fly.png                       176 x 176 fighter seen from above, nose to the right (rotated in game)
  fx_{bullet,rocket,shell,missile}.png    projectiles, pointing right (rotated in game)
  fx_muzzle.png, fx_explosion.png         one-shot effects
and ArtSource/Military/military_preview_4x.png. Shares the outline and drawing helpers of
ArtSource/Cars/build_cars.py. Requires Pillow. Output is deterministic.
"""
from pathlib import Path
import json
import math
import sys
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "Cars"))
from build_cars import (Canvas, hexc, CLEAR, OUT, GLASS, GLASS_SHINE, TIRE, HUB, HUB_HI, SKIN, HEADLIGHT,  # noqa: E402
                        HEADLIGHT_RIM, TAIL, TAIL_DARK, GRILLE, glass)

GAME = HERE.parents[1] / "Assets" / "Art" / "Military"
BASELINE = 26

OLIVE_DARK = hexc("#3B4526")
OLIVE = hexc("#5A6B34")
OLIVE_LIGHT = hexc("#86994A")
OLIVE_SHINE = hexc("#A7B86A")
BELT = hexc("#402B34")
BOOT = hexc("#34324A")
GUN = hexc("#34324A")
GUN_LIGHT = hexc("#71677C")
STAR = hexc("#F1FCFF")
FLASH_CORE = hexc("#FFF6C8")
FLASH = hexc("#FFD24A")
FLASH_EDGE = hexc("#EF8544")
FIRE_DARK = hexc("#B0203A")
SMOKE_LIGHT = hexc("#9C97AE")
SMOKE = hexc("#71677C")
SMOKE_DARK = hexc("#4B4760")
STEEL_DARK = hexc("#4B4760")
STEEL = hexc("#8C88A0")
STEEL_LIGHT = hexc("#C4C1D4")
RED = hexc("#D0393F")
SKINS = {"rifle": (hexc("#EDB58C"), hexc("#CB8A60")), "bazooka": (hexc("#8D5C47"), hexc("#563B34"))}

frames_manifest = {}


def save_strip(name, frames):
    w, h = frames[0].size
    out = Image.new("RGBA", (w * len(frames), h))
    for i, f in enumerate(frames):
        assert f.size == (w, h)
        out.paste(f, (i * w, 0))
    assert len({f.tobytes() for f in frames}) == len(frames), (name, "duplicate frames")
    for f in frames:
        assert {a for *_, a in f.get_flattened_data()} <= {0, 255}, (name, "binary alpha")
    out.save(GAME / name)
    frames_manifest[name] = [w, h]
    return frames


def layer(base, top):
    """Paste an already-outlined part over the frame."""
    base.img.alpha_composite(top.img)
    base.px = base.img.load()


def outlined(draw, w=32, h=32):
    part = Canvas(w, h)
    draw(part)
    part.outline()
    return part


# ---------------------------------------------------------------- soldiers (32 x 32)

# Helmeted head, 14 x 12, facing right. H helmet dark, h helmet, L helmet shine, s/S skin/shade, o outline.
HEAD = [
    "....oooooo....",
    "..ooLLhhhhoo..",
    ".oLhhhhhhhhHo.",
    "ohhhhhhhhhhhHo",
    "ohhhhhhhhhhhHo",
    "oHHHHHHHHHHHHHo",
    ".oSssssssssso.",
    ".oSsSssssosso.",
    ".oSsSssssossso",
    ".oSssssssssoo.",
    "..oSSssssssso.",
    "...ooooooooo..",
]


def stamp(c, rows, x0, y0, palette):
    for dy, row in enumerate(rows):
        for dx, ch in enumerate(row):
            if ch != ".":
                c.set(x0 + dx, y0 + dy, palette[ch])


def legs(c, hip_y, front, back, lift_front=0, lift_back=0):
    """Two stubby legs from the hips (x 14 and 17) to feet offset by front/back; boots on the baseline."""
    for hip_x, dx, lift, color in ((14, back, lift_back, OLIVE_DARK), (17, front, lift_front, OLIVE)):
        bottom = BASELINE - lift
        for y in range(hip_y, bottom):
            t = (y - hip_y) / max(1, bottom - 1 - hip_y)
            x = round(hip_x + dx * t)
            c.rect(x, y, x + 2, y, color)
        foot_x = hip_x + dx
        c.rect(foot_x, bottom - 1, foot_x + 3, bottom, BOOT)


def soldier_frame(variant, pose, frame):
    skin, skin_shade = SKINS[variant]
    bob = 0
    recoil = 0
    if pose == "run":
        fronts = [4, 2, 0, -3, -1, 1]
        backs = [-3, -1, 1, 4, 2, 0]
        lifts = [(0, 0), (1, 0), (0, 0), (0, 0), (0, 1), (0, 0)]
        front, back = fronts[frame], backs[frame]
        lift_front, lift_back = lifts[frame]
        bob = 1 if frame in (1, 4) else 0
    else:
        front, back, lift_front, lift_back = 2, -2, 0, 0
        if pose == "idle":
            bob = frame  # breathing
        else:
            recoil = (1, 1, 0)[frame]
    top = 5 - bob

    c = Canvas(32, 32)
    layer(c, outlined(lambda p: legs(p, top + 16, front, back, lift_front, lift_back)))

    def torso(p):
        p.rect(12, top + 12, 20, top + 15, OLIVE)
        p.rect(11, top + 13, 21, top + 15, OLIVE)
        p.rect(11, top + 16, 21, top + 16, BELT)
    body = outlined(torso)
    for x in range(12, 21):
        if body.get(x, top + 13) == OLIVE:
            body.set(x, top + 13, OLIVE_LIGHT)
    body.set(15, top + 14, OLIVE_DARK)
    layer(c, body)

    head = Canvas(32, 32)
    stamp(head, HEAD, 9, top, {"o": OUT, "H": OLIVE_DARK, "h": OLIVE, "L": OLIVE_SHINE, "s": skin, "S": skin_shade})
    layer(c, head)

    if variant == "rifle":
        # Held low across the belly. Only the parts sticking out past the body get an outline,
        # so the gun doesn't draw a dark bar through the torso.
        y = top + 14
        x0 = 9 - recoil
        c.rect(x0, y, x0 + 3, y + 1, BELT)             # stock
        c.rect(x0 + 4, y, x0 + 14, y, GUN)             # receiver
        c.rect(x0 + 6, y - 1, x0 + 12, y - 1, GUN_LIGHT)
        c.rect(x0 + 7, y + 1, x0 + 8, y + 2, GUN)      # magazine
        c.rect(x0 + 15, y, x0 + 19, y, GUN)            # barrel
        for x in range(x0 + 15, x0 + 20):
            if c.get(x, y - 1)[3] == 0:
                c.set(x, y - 1, OUT)
            if c.get(x, y + 1)[3] == 0:
                c.set(x, y + 1, OUT)
        c.set(x0 + 20, y, OUT)
        c.set(x0 - 1, y, OUT)
        c.set(x0 - 1, y + 1, OUT)
        c.rect(x0 + 11, y + 1, x0 + 12, y + 1, skin)    # front hand on the grip
        muzzle = (x0 + 21, y)
    else:
        def tube(p):
            y = top + 12
            x0 = 5 - recoil
            p.rect(x0, y, x0 + 21, y + 2, OLIVE_DARK)
            p.rect(x0 + 22, y - 1, x0 + 23, y + 3, OLIVE_DARK)  # flared muzzle
        launcher = outlined(tube)
        for x in range(4, 28):
            if launcher.get(x - recoil, top + 12) == OLIVE_DARK:
                launcher.set(x - recoil, top + 12, OLIVE)
        launcher.set(12 - recoil, top + 14, BELT)
        launcher.set(20 - recoil, top + 14, BELT)
        layer(c, launcher)
        c.rect(19 - recoil, top + 15, 20 - recoil, top + 15, skin)
        muzzle = (30 - recoil, top + 13)

    if pose == "shoot" and frame == 0:
        mx, my = muzzle
        c.rect(mx - 1, my, mx, my, FLASH_CORE)
        c.set(mx + 1, my, FLASH)
        c.set(mx, my - 1, FLASH)
        c.set(mx, my + 1, FLASH)
        if variant == "bazooka":
            # Back-blast out of the rear of the tube.
            c.disc(3, top + 13.5, 2.2, SMOKE_LIGHT)
            c.set(3, top + 13, FLASH)
    elif pose == "shoot" and frame == 1:
        mx, my = muzzle
        c.set(mx, my - 1, SMOKE_LIGHT)
        c.set(mx + 1, my - 2, SMOKE_LIGHT)
    return c.img


# ---------------------------------------------------------------- jeep (64 x 32)

def star(c, cx, cy, color=STAR):
    for x, y in ((0, -2), (-1, -1), (0, -1), (1, -1), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (-1, 1), (1, 1)):
        c.set(cx + x, cy + y, color, only_opaque=True)


def helmet_head(c, x, y, skin=SKIN, facing_right=True):
    c.disc(x, y, 2, skin)
    c.rect(x - 2, y - 2, x + 1, y - 2, OLIVE)
    c.rect(x - 2, y - 1, x + 2, y - 1, OLIVE_DARK)
    c.set(x + (1 if facing_right else -1), y + 1, OUT)


def jeep_side(frame):
    bob = (0, 0, 1, 0)[frame]
    c = Canvas()
    # Open body, flat hood, spare wheel on the back.
    c.polygon([(6, 12 + bob), (40, 12 + bob), (42, 13 + bob), (58, 13 + bob), (58, 22 + bob), (6, 22 + bob)], OLIVE)
    c.disc(5.5, 16.5 + bob, 3.5, TIRE)
    for wx in (15, 48):
        c.disc(wx + 0.5, 22.5, 4.5, TIRE)
    c.outline()
    c.shade(OLIVE_DARK, OLIVE, OLIVE_LIGHT, 21 + bob)
    star(c, 29, 17 + bob)
    c.rect(56, 15 + bob, 57, 16 + bob, HEADLIGHT)
    c.rect(7, 20 + bob, 8, 20 + bob, TAIL)
    # Windshield frame, the driver and the standing gunner behind a pintle gun.
    frame_parts = outlined(lambda p: (p.rect(40, 5 + bob, 41, 12 + bob, OLIVE_DARK),
                                      p.rect(21, 7 + bob, 22, 12 + bob, GUN),
                                      p.rect(17, 5 + bob, 33, 6 + bob, GUN)), 64, 32)
    layer(c, frame_parts)
    c.set(18, 5 + bob, GUN_LIGHT)
    helmet_head(c, 36, 10 + bob)
    helmet_head(c, 16, 4 + bob)
    for wx in (15, 48):
        cx, cy = wx + 0.5, 22.5
        c.disc(cx, cy, 5.5, OUT, only_opaque=True)
        c.disc(cx, cy, 4.5, OUT)
        c.disc(cx, cy, 3.5, TIRE)
        c.disc(cx, cy, 1.6, OLIVE_DARK)
        angle = frame * math.tau / 4
        c.set(round(cx - 0.5 + 2.6 * math.cos(angle)), round(cy - 0.5 + 2.6 * math.sin(angle)), GUN_LIGHT)
    return c.img


def jeep_face(frame, front):
    bob = (0, 0, 1, 0)[frame]
    c = Canvas()
    for wx in (19, 41):
        c.rect(wx, BASELINE - 4, wx + 3, BASELINE, TIRE)
    c.rect(18, 13 + bob, 45, 23 + bob, OLIVE)
    c.outline()
    c.shade(OLIVE_DARK, OLIVE, OLIVE_LIGHT, 22 + bob)
    # Windshield frame with glass (folded flat looks like a bar), crew behind it.
    parts = outlined(lambda p: (p.rect(19, 6 + bob, 44, 7 + bob, OLIVE_DARK), p.rect(19, 6 + bob, 20, 13 + bob, OLIVE_DARK),
                                p.rect(43, 6 + bob, 44, 13 + bob, OLIVE_DARK)), 64, 32)
    layer(c, parts)
    if front:
        c.rect(21, 8 + bob, 42, 12 + bob, GLASS)
        c.set(23, 11 + bob, GLASS_SHINE)
        c.set(24, 10 + bob, GLASS_SHINE)
        helmet_head(c, 37, 10 + bob, facing_right=False)
        helmet_head(c, 26, 4 + bob, facing_right=False)
        c.rect(24, 6 + bob, 28, 6 + bob, GUN)
        for x0 in (20, 40):
            c.rect(x0, 15 + bob, x0 + 2, 16 + bob, HEADLIGHT)
            c.set(x0 + 1, 16 + bob, HEADLIGHT_RIM)
        c.rect(25, 15 + bob, 38, 19 + bob, GRILLE)
        for x in range(26, 38, 2):
            c.rect(x, 15 + bob, x, 19 + bob, OLIVE_DARK)
    else:
        c.rect(21, 8 + bob, 42, 12 + bob, OLIVE_DARK)
        c.disc(37.5, 10 + bob, 2, OLIVE)
        c.disc(26.5, 4 + bob, 2, OLIVE)
        c.rect(25, 1 + bob, 27, 3 + bob, GUN)
        c.disc(31.5, 17.5 + bob, 3, TIRE)  # spare wheel
        c.disc(31.5, 17.5 + bob, 1.2, OLIVE_DARK)
        for x0 in (20, 41):
            c.rect(x0, 15 + bob, x0 + 1, 16 + bob, TAIL)
    star(c, 24 if front else 38, 20 + bob)
    for wx in (19, 41):
        x, y = wx + 1 + frame % 2, BASELINE - 1 - frame // 2
        if c.get(x, y) == TIRE:
            c.set(x, y, GLASS_SHINE)
    return c.img


# ---------------------------------------------------------------- tank (128 x 64)

TANK_BASE = 58


def tread_links(c, x0, x1, y, frame, step=4):
    for x in range(x0, x1 + 1):
        if (x + frame) % step == 0 and c.get(x, y) == TIRE:
            c.set(x, y, GUN_LIGHT)


def tank_side(frame):
    c = Canvas(128, 64)
    c.polygon([(12, 44), (16, 34), (104, 34), (118, 40), (116, 44)], OLIVE)
    c.polygon([(38, 34), (44, 22), (80, 22), (90, 28), (90, 34)], OLIVE)
    c.rect(60, 19, 70, 22, OLIVE)                   # hatch
    c.polygon([(8, 50), (14, 44), (114, 44), (120, 50), (114, TANK_BASE), (14, TANK_BASE)], TIRE)
    c.rect(90, 26, 118, 29, OLIVE)                  # barrel
    c.rect(116, 25, 123, 30, OLIVE_DARK)            # muzzle brake
    c.outline()
    c.shade(OLIVE_DARK, OLIVE, OLIVE_LIGHT, 42)
    c.rect(17, 40, 103, 40, OLIVE_DARK)             # fender line
    for x, y in ((50, 29), (73, 29)):
        c.rect(x, y, x + 1, y + 1, OLIVE_DARK)      # bolts / vision ports
    star(c, 62, 28)
    helmet_head(c, 65, 16)                          # commander peeks out of the hatch
    # Road wheels, sprocket and idler, turning; tread teeth crawl along the top and bottom runs.
    for i, wx in enumerate((22, 38, 54, 70, 86, 102)):
        cy = 51.5
        c.disc(wx, cy, 5.5, OUT)
        c.disc(wx, cy, 4.5, STEEL_DARK)
        c.disc(wx, cy, 2, STEEL)
        angle = frame * math.tau / 4 + i
        c.set(round(wx - 0.5 + 3.2 * math.cos(angle)), round(cy - 0.5 + 3.2 * math.sin(angle)), STEEL_LIGHT)
    tread_links(c, 14, 114, 45, frame)
    tread_links(c, 14, 114, TANK_BASE - 1, 3 - frame)
    # Exhaust at the back.
    c.rect(14, 36, 15, 37, GUN)
    return c.img


def tank_face(frame, front):
    c = Canvas(128, 64)
    for x0 in (32, 84):
        c.rect(x0, 30, x0 + 11, TANK_BASE, TIRE)
    c.rect(42, 34, 85, 54, OLIVE)
    c.rect(50, 22, 77, 40, OLIVE)
    if front:
        c.rect(61, 38, 66, 56, OLIVE)                # barrel pointing down the screen at us
    else:
        c.rect(61, 6, 66, 22, OLIVE)                 # barrel pointing up the screen
    c.outline()
    c.shade(OLIVE_DARK, OLIVE, OLIVE_LIGHT, 52)
    c.rect(52, 40, 75, 40, OLIVE_DARK)               # turret ring shadow
    if front:
        # Barrel coming towards us over the hull: outline its sides so it reads against the hull.
        c.rect(60, 40, 60, 53, OUT)
        c.rect(67, 40, 67, 53, OUT)
        c.rect(61, 40, 61, 53, OLIVE_LIGHT)
        c.rect(64, 40, 66, 53, OLIVE_DARK)
        c.rect(60, 54, 67, 58, OLIVE_DARK)
        c.rect(62, 55, 65, 57, OUT)                  # muzzle
        for x0 in (44, 80):
            c.rect(x0, 44, x0 + 2, 45, HEADLIGHT)
        helmet_head(c, 70, 22, facing_right=False)
    else:
        c.rect(61, 5, 66, 8, OLIVE_DARK)
        for x in range(46, 82, 4):
            c.rect(x, 46, x + 1, 51, OLIVE_DARK)     # engine grille
        for x0 in (44, 81):
            c.rect(x0, 50, x0 + 1, 51, TAIL)
        c.disc(70.5, 22, 2, OLIVE)
    star(c, 57, 31)
    # Tread links running along each track.
    for x0 in (32, 84):
        for y in range(31, TANK_BASE):
            if (y + (frame if front else -frame)) % 4 == 0:
                for x in range(x0 + 1, x0 + 11):
                    if c.get(x, y) == TIRE:
                        c.set(x, y, GUN_LIGHT)
    return c.img


# ---------------------------------------------------------------- fighter jet (176 x 176, top-down)

def plane_frame(frame):
    c = Canvas(176, 176)
    cy = 88
    wing = [(118, cy - 4), (72, 26), (56, 26), (66, cy - 4)]
    tail = [(42, cy - 4), (26, 58), (16, 58), (22, cy - 4)]
    for shape in (wing, tail):
        c.polygon(shape, STEEL)
        c.polygon([(x, 2 * cy - y) for x, y in shape], STEEL)
    c.polygon([(172, cy), (156, cy - 6), (130, cy - 9), (40, cy - 9), (16, cy - 6), (16, cy + 6),
               (40, cy + 9), (130, cy + 9), (156, cy + 6)], STEEL)
    # Wing-tip missiles.
    for y in (30, 2 * cy - 32):
        c.rect(56, y, 80, y + 2, STEEL_LIGHT)
    c.outline()
    c.shade(STEEL_DARK, STEEL, STEEL_LIGHT, 200)
    # Spine shadow down the fuselage, darker far wing and tail halves read as depth.
    for x in range(20, 150):
        for y in range(cy + 1, cy + 9):
            if c.get(x, y) == STEEL:
                c.set(x, y, STEEL_DARK)
    for shape in (wing, tail):
        for x in range(10, 130):
            for y in range(cy + 9, 160):
                if c.get(x, y) == STEEL_LIGHT:
                    c.set(x, y, STEEL)
    c.rect(160, cy - 2, 168, cy + 2, STEEL_DARK)   # nose cone
    glass(c, [(128, cy - 4), (148, cy - 3), (152, cy), (148, cy + 3), (128, cy + 4), (122, cy)], 0)
    for y in (31, 2 * cy - 31):
        c.rect(78, y, 80, y, RED)                   # missile tips
    for x, y in ((80, 54), (80, 2 * cy - 54)):      # roundels
        c.disc(x, y, 6, OUT)
        c.disc(x, y, 5, RED)
        c.disc(x, y, 2.5, STAR)
    # Afterburner: two exhaust flames that flicker between frames.
    length = (12, 9, 14, 10)[frame]
    for y0 in (cy - 5, cy + 1):
        c.rect(15 - length, y0, 14, y0 + 4, FLASH_EDGE)
        c.rect(15 - length * 2 // 3, y0 + 1, 14, y0 + 3, FLASH)
        c.rect(15 - length // 3, y0 + 2, 14, y0 + 2, FLASH_CORE)
    return c.img


# ---------------------------------------------------------------- projectiles and effects

def bullet():
    c = Canvas(8, 4)
    c.rect(0, 1, 7, 2, FLASH_EDGE)
    c.rect(3, 1, 7, 2, FLASH)
    c.rect(6, 1, 7, 2, FLASH_CORE)
    return c.img


def rocket(frame):
    c = Canvas(20, 8)
    c.rect(6, 3, 16, 4, OLIVE)
    c.rect(17, 3, 18, 4, RED)
    c.rect(6, 1, 8, 6, OLIVE_DARK)
    c.outline()
    flame = (4, 6)[frame]
    c.rect(6 - flame, 3, 5, 4, FLASH_EDGE)
    c.rect(6 - flame // 2, 3, 5, 4, FLASH)
    return c.img


def shell(frame):
    c = Canvas(16, 8)
    c.rect(6, 2, 13, 5, hexc("#CB8A60"))
    c.rect(12, 3, 14, 4, hexc("#FFD8AF"))
    c.outline()
    trail = (5, 4)[frame]
    c.rect(6 - trail, 3, 5, 4, SMOKE_LIGHT)
    c.set(5, 3, FLASH)
    return c.img


def missile(frame):
    c = Canvas(28, 10)
    c.rect(8, 3, 22, 6, STEEL_LIGHT)
    c.rect(23, 4, 25, 5, RED)
    c.polygon([(8, 3), (11, 0), (12, 0), (12, 3)], STEEL)
    c.polygon([(8, 6), (11, 9), (12, 9), (12, 6)], STEEL)
    c.outline()
    flame = (6, 8)[frame]
    c.rect(8 - flame, 4, 7, 5, FLASH_EDGE)
    c.rect(8 - flame // 2, 4, 7, 5, FLASH)
    c.set(7, 4, FLASH_CORE)
    return c.img


def muzzle(frame):
    c = Canvas(16, 16)
    r = (6, 4.5, 2.5)[frame]
    c.disc(8, 8, r, FLASH_EDGE)
    c.disc(8, 8, r * 0.7, FLASH)
    c.disc(8, 8, r * 0.35, FLASH_CORE)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        for k in range(int(r) + 1, int(r * 1.4) + 2):
            c.set(8 + dx * k, 8 + dy * k, FLASH)
    return c.img


def explosion(frame):
    """Fireball that blooms, then rolls into smoke that thins out."""
    c = Canvas(48, 48)
    fire = [8, 14, 17, 16, 12, 0, 0, 0][frame]
    smoke = [0, 0, 10, 15, 18, 19, 17, 12][frame]
    puffs = [(0, 0), (-7, -4), (7, -5), (-4, 6), (6, 5), (0, -9)]
    rise = frame * 1.2
    for i, (dx, dy) in enumerate(puffs):
        if smoke:
            r = smoke * (0.55 if i else 0.75)
            col = SMOKE_DARK if frame >= 6 else SMOKE
            c.disc(24 + dx * smoke / 12, 24 + dy * smoke / 12 - rise, r, col)
            if frame < 6:
                c.disc(24 + dx * smoke / 12 - 1, 24 + dy * smoke / 12 - rise - 1, r * 0.6, SMOKE_LIGHT)
    if fire:
        for dx, dy in puffs[:4]:
            c.disc(24 + dx * fire / 20, 24 + dy * fire / 20, fire * 0.6, FIRE_DARK)
        c.disc(24, 24, fire * 0.75, FLASH_EDGE)
        c.disc(24, 23, fire * 0.5, FLASH)
        c.disc(24, 23, fire * 0.25, FLASH_CORE)
    if frame == 7:
        # Break the last smoke up so it fades out rather than popping.
        for y in range(48):
            for x in range(48):
                if (x + y) % 2 and c.get(x, y)[3]:
                    c.set(x, y, CLEAR)
    else:
        c.outline()
    return c.img


# ---------------------------------------------------------------- build

def build():
    GAME.mkdir(parents=True, exist_ok=True)
    rows = []
    for variant in ("rifle", "bazooka"):
        for pose, count in (("run", 6), ("idle", 2), ("shoot", 3)):
            rows.append(save_strip(f"soldier_{variant}_{pose}.png", [soldier_frame(variant, pose, i) for i in range(count)]))
    rows.append(save_strip("jeep_army_side.png", [jeep_side(i) for i in range(4)]))
    rows.append(save_strip("jeep_army_up.png", [jeep_face(i, False) for i in range(4)]))
    rows.append(save_strip("jeep_army_down.png", [jeep_face(i, True) for i in range(4)]))
    rows.append(save_strip("tank_army_side.png", [tank_side(i) for i in range(4)]))
    rows.append(save_strip("tank_army_up.png", [tank_face(i, False) for i in range(4)]))
    rows.append(save_strip("tank_army_down.png", [tank_face(i, True) for i in range(4)]))
    rows.append(save_strip("plane_jet_fly.png", [plane_frame(i) for i in range(4)]))
    save_strip("fx_bullet.png", [bullet()])
    save_strip("fx_rocket.png", [rocket(i) for i in range(2)])
    save_strip("fx_shell.png", [shell(i) for i in range(2)])
    save_strip("fx_missile.png", [missile(i) for i in range(2)])
    save_strip("fx_muzzle.png", [muzzle(i) for i in range(3)])
    rows.append(save_strip("fx_explosion.png", [explosion(i) for i in range(8)]))
    (GAME / "sprite_frames.json").write_text(json.dumps(frames_manifest, indent=2) + "\n", encoding="utf-8")

    # Preview: every strip on asphalt at 4x.
    width = max(sum(f.width for f in r) for r in rows) + 8
    height = sum(r[0].height + 4 for r in rows)
    preview = Image.new("RGB", (width, height), (36, 34, 46))
    y = 0
    for r in rows:
        x = 0
        for f in r:
            preview.paste(f, (x, y), f)
            x += f.width
        y += r[0].height + 4
    preview.resize((width * 4, height * 4), Image.Resampling.NEAREST).save(HERE / "military_preview_4x.png")
    print("wrote", len(frames_manifest), "strips to", GAME)


if __name__ == "__main__":
    build()
