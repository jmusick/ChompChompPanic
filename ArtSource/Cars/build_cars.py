"""Draw the car prey tier for Chomp Chomp Panic! procedurally.

Run: python build_cars.py
Writes to Assets/Art/Cars (imported as sliced 64 x 32 sprites at 64 px per unit, like the people):
  car_<variant>_side.png  4-frame drive loop seen from the side, facing right (flipped in game)
  car_<variant>_up.png    4-frame drive loop seen from behind (driving up the screen)
  car_<variant>_down.png  4-frame drive loop seen from the front (driving down the screen)
plus sprite_frames.json (frame size per strip, read by the editor's art importer),
and ArtSource/Cars/cars_preview_8x.png, which lines the cars up next to a person and the kaiju.

Cars sit on the same foot baseline (row 26) and pixel scale as the people and are about twice
their size. Outline, skin and glass colors are shared with the people palette. Requires Pillow.
Output is deterministic.
"""
from pathlib import Path
import json
import math
from PIL import Image

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
GAME = PROJECT / "Assets" / "Art" / "Cars"
PERSON = PROJECT / "Assets" / "Art" / "People" / "person_office_run.png"
KAIJU = PROJECT / "Assets" / "Art" / "Kaiju" / "kaiju_idle.png"
FRAME_W, FRAME_H = 64, 32
FRAMES = 4
BASELINE = 26
BOB = (0, 0, 1, 0)  # body bounce on its springs, per frame
PREVIEW_BG = (36, 34, 46)  # the city's asphalt


def hexc(h):
    return tuple(bytes.fromhex(h[1:])) + (255,)


CLEAR = (0, 0, 0, 0)
OUT = hexc("#10152D")
GLASS = hexc("#34324A")
GLASS_SHINE = hexc("#71677C")
TIRE = hexc("#34324A")
HUB = hexc("#9C97AE")
HUB_HI = hexc("#F1FCFF")
SKIN = hexc("#EDB58C")
HAIR = hexc("#402B34")
HEADLIGHT = hexc("#FFF6C8")
HEADLIGHT_RIM = hexc("#FFD8AF")
TAIL = hexc("#FF4A4A")
TAIL_DARK = hexc("#B0203A")
PLATE = hexc("#FFF0DA")
GRILLE = hexc("#34324A")

# Body colors per variant: dark, mid, light.
VARIANTS = {
    "sedan": (hexc("#8E2434"), hexc("#D0393F"), hexc("#F4726A")),
    "taxi": (hexc("#B8741E"), hexc("#F2B33D"), hexc("#FFE08A")),
    "kei": (hexc("#AE466E"), hexc("#E9719B"), hexc("#FFA9BE")),
    "van": (hexc("#9C97AE"), hexc("#D6D4E2"), hexc("#F1FCFF")),
}

# Side profiles, facing right (front at the right). body: silhouette polygon without wheels.
# windows: glass polygons. wheels: centers (radius 4.5, bottoms on the baseline). driver: head center.
SIDE = {
    "sedan": dict(
        body=[(5, 15), (8, 13), (18, 13), (24, 6), (40, 6), (47, 13), (56, 14), (58, 16), (58, 23), (5, 23)],
        windows=[[(20, 13), (25, 8), (31, 8), (31, 13)], [(33, 8), (39, 8), (44, 13), (33, 13)]],
        doors=[32], wheels=[15, 48], driver=(37, 10), bottom=23),
    "taxi": dict(
        body=[(5, 15), (8, 13), (18, 13), (24, 6), (40, 6), (47, 13), (56, 14), (58, 16), (58, 23), (5, 23)],
        windows=[[(20, 13), (25, 8), (31, 8), (31, 13)], [(33, 8), (39, 8), (44, 13), (33, 13)]],
        doors=[32], wheels=[15, 48], driver=(37, 10), bottom=23,
        sign=[(29, 6), (29, 3), (35, 3), (35, 6)]),
    "kei": dict(
        body=[(9, 15), (10, 7), (13, 5), (41, 5), (47, 12), (53, 14), (55, 16), (55, 23), (9, 23)],
        windows=[[(13, 12), (13, 8), (15, 7), (26, 7), (26, 12)], [(28, 7), (40, 7), (44, 12), (28, 12)]],
        doors=[27], wheels=[18, 46], driver=(35, 9), bottom=23),
    "van": dict(
        body=[(5, 6), (49, 6), (52, 8), (57, 14), (58, 16), (58, 23), (5, 23)],
        windows=[[(46, 8), (50, 8), (54, 13), (46, 13)]],
        doors=[45], wheels=[15, 48], driver=(48, 10), bottom=23, stripe=(16, 17)),
}

# Front/back views: half width of the body, roof row, cabin inset at the roof.
FACE = {
    "sedan": dict(half=13, roof=8, inset=4, belt=14),
    "taxi": dict(half=13, roof=8, inset=4, belt=14, sign=True),
    "kei": dict(half=12, roof=6, inset=2, belt=13),
    "van": dict(half=13, roof=6, inset=1, belt=14, stripe=(16, 17)),
}


# ---------------------------------------------------------------- pixel helpers

class Canvas:
    def __init__(self, width=FRAME_W, height=FRAME_H):
        self.w, self.h = width, height
        self.img = Image.new("RGBA", (width, height))
        self.px = self.img.load()

    def inside(self, x, y):
        return 0 <= x < self.w and 0 <= y < self.h

    def get(self, x, y):
        return self.px[x, y] if self.inside(x, y) else CLEAR

    def set(self, x, y, color, only_opaque=False):
        if self.inside(x, y) and (not only_opaque or self.px[x, y][3]):
            self.px[x, y] = color

    def rect(self, x0, y0, x1, y1, color, only_opaque=False):
        """Inclusive rectangle."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, color, only_opaque)

    def polygon(self, points, color, dy=0):
        pts = [(x, y + dy) for x, y in points]
        ys = [y for _, y in pts]
        xs = [x for x, _ in pts]
        for y in range(max(0, math.floor(min(ys))), min(self.h, math.ceil(max(ys)) + 1)):
            for x in range(max(0, math.floor(min(xs))), min(self.w, math.ceil(max(xs)) + 1)):
                if point_in_polygon(x + 0.5, y + 0.5, pts) or on_polygon_edge(x, y, pts):
                    self.set(x, y, color)

    def polygon_outline(self, points, color, dy=0):
        pts = [(x, y + dy) for x, y in points]
        for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
            steps = max(abs(x1 - x0), abs(y1 - y0), 1)
            for i in range(steps + 1):
                self.set(round(x0 + (x1 - x0) * i / steps), round(y0 + (y1 - y0) * i / steps), color)

    def disc(self, cx, cy, r, color, only_opaque=False):
        for y in range(math.floor(cy - r), math.ceil(cy + r) + 1):
            for x in range(math.floor(cx - r), math.ceil(cx + r) + 1):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.set(x, y, color, only_opaque)

    def outline(self):
        """Every opaque pixel that touches transparency becomes navy outline."""
        edge = [(x, y) for y in range(self.h) for x in range(self.w) if self.px[x, y][3] and any(
            self.get(nx, ny)[3] == 0 for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))]
        for p in edge:
            self.px[p] = OUT

    def shade(self, dark, mid, light, dark_from):
        """Highlight body pixels just under an outline, darken the bottom rows."""
        for y in range(self.h):
            for x in range(self.w):
                if self.px[x, y] != mid:
                    continue
                if self.get(x, y - 1) == OUT:
                    self.px[x, y] = light
                elif y >= dark_from:
                    self.px[x, y] = dark


def point_in_polygon(x, y, pts):
    inside = False
    for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
        if (y0 > y) != (y1 > y) and x < x0 + (y - y0) * (x1 - x0) / (y1 - y0):
            inside = not inside
    return inside


def on_polygon_edge(x, y, pts):
    """Include the pixels the polygon's own corners sit on, so shapes reach their listed extents."""
    for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
        steps = max(abs(x1 - x0), abs(y1 - y0), 1)
        for i in range(steps + 1):
            if (round(x0 + (x1 - x0) * i / steps), round(y0 + (y1 - y0) * i / steps)) == (x, y):
                return True
    return False


def glass(c, points, dy):
    c.polygon(points, GLASS, dy)
    c.polygon_outline(points, OUT, dy)
    # One diagonal glint across the pane.
    xs = [x for x, _ in points]
    ys = [y + dy for _, y in points]
    x0, y1 = min(xs) + 2, max(ys) - 1
    for i in range(3):
        if c.get(x0 + i + 1, y1 - i) == GLASS:
            c.set(x0 + i + 1, y1 - i, GLASS_SHINE)


def panicking_head(c, x, y, facing_right=True):
    """A tiny driver: round head, dark hair and a wide-open screaming mouth."""
    c.disc(x, y, 2, SKIN, only_opaque=True)
    c.rect(x - 2, y - 2, x + 1, y - 2, HAIR, only_opaque=True)
    c.set(x - 2, y - 1, HAIR, only_opaque=True)
    mouth = x + 1 if facing_right else x
    c.set(mouth, y + 1, OUT, only_opaque=True)


# ---------------------------------------------------------------- side view

def side_frame(variant, frame):
    dark, mid, light = VARIANTS[variant]
    spec = SIDE[variant]
    dy = BOB[frame]
    wheel_y = BASELINE - 4 + 0.5  # radius 4.5 -> bottom pixel row on the baseline
    c = Canvas()
    c.polygon(spec["body"], mid, dy)
    if "sign" in spec:
        c.polygon(spec["sign"], PLATE, dy)
    for wx in spec["wheels"]:
        c.disc(wx + 0.5, wheel_y, 4.5, TIRE)
    c.outline()
    c.shade(dark, mid, light, spec["bottom"] - 1 + dy)

    if "stripe" in spec:
        y0, y1 = spec["stripe"]
        c.rect(0, y0 + dy, FRAME_W - 1, y1 + dy, VARIANTS["sedan"][1], only_opaque=True)
        # Keep the outline intact where the stripe crossed it.
        for y in (y0 + dy, y1 + dy):
            for x in range(FRAME_W):
                if c.get(x, y) == VARIANTS["sedan"][1] and any(
                        c.get(nx, y)[3] == 0 for nx in (x - 1, x + 1)):
                    c.set(x, y, OUT)
    if "sign" in spec:
        # Roof lamp: the lit "vacant" sign glows warm.
        x0 = min(x for x, _ in spec["sign"]) + 1
        c.rect(x0, 4 + dy, x0 + 4, 4 + dy, HEADLIGHT_RIM, only_opaque=True)
    for window in spec["windows"]:
        glass(c, window, dy)
    for door in spec["doors"]:
        for y in range(8 + dy, spec["bottom"] - 1 + dy):
            if c.get(door, y) in (dark, mid, light):
                c.set(door, y, OUT)
        c.set(door - 3, 15 + dy, dark)
        c.set(door - 2, 15 + dy, dark)
    panicking_head(c, *(spec["driver"][0], spec["driver"][1] + dy))

    # Lights, one pixel inside the outline at the front and back ends.
    front = max(x for x, _ in spec["body"])
    back = min(x for x, _ in spec["body"])
    c.rect(front - 2, 15 + dy, front - 1, 16 + dy, HEADLIGHT)
    c.set(front - 2, 16 + dy, HEADLIGHT_RIM)
    c.rect(back + 1, 15 + dy, back + 2, 16 + dy, TAIL)
    c.set(back + 2, 16 + dy, TAIL_DARK)
    # Bumpers
    c.rect(front - 3, spec["bottom"] - 1 + dy, front - 1, spec["bottom"] - 1 + dy, HUB)
    c.rect(back + 1, spec["bottom"] - 1 + dy, back + 3, spec["bottom"] - 1 + dy, HUB)

    # Wheels drawn over the body: dark arch, tire, hub with a spoke that turns each frame.
    for wx in spec["wheels"]:
        cx = wx + 0.5
        c.disc(cx, wheel_y, 5.5, OUT, only_opaque=True)
        c.disc(cx, wheel_y, 4.5, OUT)
        c.disc(cx, wheel_y, 3.5, TIRE)
        c.disc(cx, wheel_y, 2, HUB)
        angle = frame * math.tau / FRAMES
        c.set(round(cx - 0.5 + 1.4 * math.cos(angle)), round(wheel_y - 0.5 + 1.4 * math.sin(angle)), OUT)
        c.set(wx, round(wheel_y - 0.5), HUB_HI)
    return c.img


# ---------------------------------------------------------------- front and back views

def face_frame(variant, frame, front):
    """front=True: seen from the front (driving down the screen); False: from behind (driving up)."""
    dark, mid, light = VARIANTS[variant]
    spec = FACE[variant]
    dy = BOB[frame]
    half, roof, inset, belt = spec["half"], spec["roof"] + dy, spec["inset"], spec["belt"] + dy
    cx = FRAME_W // 2
    left, right = cx - half, cx + half - 1
    bottom = BASELINE - 3 + dy
    c = Canvas()
    # Wheels peek out below the body at the corners.
    for wx in (left + 1, right - 4):
        c.rect(wx, BASELINE - 4, wx + 3, BASELINE, TIRE)
    c.polygon([(left + inset, roof), (right - inset, roof), (right, belt), (right, bottom),
               (left, bottom), (left, belt)], mid)
    if spec.get("sign"):
        c.rect(cx - 3, roof - 3, cx + 2, roof - 1, PLATE)
    c.outline()
    c.shade(dark, mid, light, bottom - 1)

    if "stripe" in spec:
        y0, y1 = spec["stripe"]
        c.rect(left + 1, y0 + dy, right - 1, y1 + dy, VARIANTS["sedan"][1], only_opaque=True)
    if spec.get("sign"):
        c.rect(cx - 2, roof - 2, cx + 1, roof - 2, HEADLIGHT_RIM)

    # Windshield (front) or rear window (back), with the driver seen through the front one.
    pane = [(left + inset + 2, roof + 2), (right - inset - 2, roof + 2), (right - 2, belt - 1), (left + 2, belt - 1)]
    if front:
        glass(c, pane, 0)
        # Driver sits on the right-hand side (Japan).
        head_x = cx + 4
        panicking_head(c, head_x, belt - 3, facing_right=False)
        # Arms flung up in panic.
        c.set(head_x - 3, belt - 5, SKIN)
        c.set(head_x - 3, belt - 4, SKIN)
        c.set(head_x + 3, belt - 5, SKIN)
        c.set(head_x + 3, belt - 4, SKIN)
    else:
        glass(c, pane, 0)
        # Only the back of the driver's head shows.
        c.disc(cx + 4, belt - 3, 2, HAIR, only_opaque=True)

    lamp_y = belt + 2
    if front:
        for x0 in (left + 2, right - 4):
            c.rect(x0, lamp_y, x0 + 2, lamp_y + 1, HEADLIGHT)
            c.set(x0 + 1, lamp_y + 1, HEADLIGHT_RIM)
        c.rect(cx - 4, lamp_y + 1, cx + 3, lamp_y + 2, GRILLE)
    else:
        for x0 in (left + 2, right - 4):
            c.rect(x0, lamp_y, x0 + 2, lamp_y + 1, TAIL)
            c.set(x0 + 1, lamp_y + 1, TAIL_DARK)
        c.rect(cx - 3, lamp_y + 2, cx + 2, lamp_y + 3, PLATE)
        c.rect(cx - 2, lamp_y + 3, cx + 1, lamp_y + 3, GLASS)
    c.rect(left + 1, bottom - 1, right - 1, bottom - 1, HUB)
    # Tread glint that creeps around the visible part of each tire as it rolls.
    for wx in (left + 1, right - 4):
        x, y = wx + 1 + frame % 2, BASELINE - 1 - frame // 2
        if c.get(x, y) == TIRE:
            c.set(x, y, GLASS_SHINE)
    return c.img


# ---------------------------------------------------------------- build

def strip(frames):
    out = Image.new("RGBA", (FRAME_W * len(frames), FRAME_H))
    for i, f in enumerate(frames):
        out.paste(f, (i * FRAME_W, 0))
    return out


def validate(name, frames):
    for i, f in enumerate(frames):
        box = f.getbbox()
        assert box is not None, (name, i)
        x0, y0, x1, y1 = box
        assert y1 - 1 == BASELINE, (name, i, box, "wheels must sit on the people's foot baseline")
        assert x0 >= 2 and x1 <= FRAME_W - 2 and y0 >= 1, (name, i, box, "margins")
        assert {a for *_, a in f.get_flattened_data()} <= {0, 255}, (name, i, "binary alpha")
    assert len({f.tobytes() for f in frames}) == len(frames), (name, "duplicate frames")


def build():
    GAME.mkdir(parents=True, exist_ok=True)
    rows = []
    manifest = {}
    for variant in VARIANTS:
        clips = {
            "side": [side_frame(variant, i) for i in range(FRAMES)],
            "up": [face_frame(variant, i, front=False) for i in range(FRAMES)],
            "down": [face_frame(variant, i, front=True) for i in range(FRAMES)],
        }
        for clip, frames in clips.items():
            name = f"car_{variant}_{clip}.png"
            validate(name, frames)
            strip(frames).save(GAME / name)
            manifest[name] = [FRAME_W, FRAME_H]
            rows.append(frames)
    (GAME / "sprite_frames.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    # Preview: every strip, then one of each car beside a person and the kaiju at the same scale.
    width = FRAME_W * FRAMES
    preview = Image.new("RGB", (width, FRAME_H * (len(rows) + 2)), PREVIEW_BG)
    for r, frames in enumerate(rows):
        for i, f in enumerate(frames):
            preview.paste(f, (i * FRAME_W, r * FRAME_H), f)
    y = FRAME_H * len(rows)
    kaiju = Image.open(KAIJU).convert("RGBA").crop((0, 0, 64, 64))
    preview.paste(kaiju, (0, y), kaiju)
    person = Image.open(PERSON).convert("RGBA").crop((0, 0, 32, 32))
    kaiju_foot = kaiju.getbbox()[3] - 1
    preview.paste(person, (56, y + kaiju_foot - BASELINE), person)
    for i, variant in enumerate(VARIANTS):
        car = rows[i * 3][0]
        preview.paste(car, (80 + i * 44, y + kaiju_foot - BASELINE), car)
    preview.resize((preview.width * 8, preview.height * 8), Image.Resampling.NEAREST).save(HERE / "cars_preview_8x.png")
    print("wrote", len(rows), "strips to", GAME)


if __name__ == "__main__":
    build()
