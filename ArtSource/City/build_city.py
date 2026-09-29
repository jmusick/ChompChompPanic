"""Draw the night-time Tokyo city art for Chomp Chomp Panic! procedurally, pixel by pixel.

Run: python build_city.py
Writes Assets/Art/City/city_atlas.png and city_atlas.json (sprite rects, read at runtime by CityMap.cs).
Requires Pillow only. Output is deterministic: the same script always draws the same art.

Scale: 32 px per world unit (the same pixel size as the people, who are drawn at 2x).
One chunk is a 512 px (16 unit) city block. Its ground is a base (pavement, park, shrine or plaza)
with street pieces laid over each edge: half a road, half a canal, half an alley, or nothing when
the block merges with its neighbour. Neighbouring chunks' halves join into full streets.
View is top-down 3/4: buildings show their roof plus their front (south) face.
"""
from pathlib import Path
import json
import math
import random
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
GAME = HERE.parents[1] / "Assets" / "Art" / "City"

PPU = 32
CHUNK = 512
ROAD = 48          # half-road width at each chunk edge
SIDEWALK = 20
INNER = ROAD + SIDEWALK  # first interior pixel (68); interior is 376 px across
ATLAS_WIDTH = 2048
PAD = 2


def hexc(h):
    return tuple(bytes.fromhex(h[1:])) + (255,)


OUT = hexc("#10152D")  # shared navy outline, same as the kaiju and people
CLEAR = (0, 0, 0, 0)

# Night palette: the city stays dark and fairly desaturated so the kaiju (teal) and people (warm) pop.
ASPHALT = hexc("#24222E")
ASPHALT_DARK = hexc("#1D1B26")
ASPHALT_LIGHT = hexc("#2C2A37")
ROAD_LINE = hexc("#BDB8C6")
CURB = hexc("#4D4959")
SIDEWALK_C = hexc("#383544")
SIDEWALK_JOINT = hexc("#312E3C")
TACTILE = hexc("#A88C3A")
TACTILE_DOT = hexc("#8C7430")
PAVE = hexc("#2F2C39")
PAVE_JOINT = hexc("#2A2733")
LAMP_POOL = hexc("#4A4552")
LAMP_POOL_ROAD = hexc("#2E2B33")

GRASS = [hexc("#2B4634"), hexc("#30503A"), hexc("#274030")]
GRAVEL = [hexc("#4C4858"), hexc("#555161"), hexc("#44404F")]
PATH = hexc("#5A5462")
PATH_EDGE = hexc("#48434F")
STONE = [hexc("#6E6A7A"), hexc("#8A8698"), hexc("#55516A")]
WATER = [hexc("#26314D"), hexc("#2F3C5C"), hexc("#3C4C70")]

WIN_LIT = [hexc("#FFD27A"), hexc("#FFBE66"), hexc("#FFE4A8")]
WIN_DIM = hexc("#1A1826")
WIN_OFF = hexc("#24212F")
NEON = [hexc("#FF4F9A"), hexc("#B35CFF"), hexc("#FF8A3D"), hexc("#FFE066"), hexc("#FF5E5E")]
SIGN_BG = hexc("#16142A")
LANTERN = hexc("#E0463C")
LANTERN_HI = hexc("#FF9C6A")

TREE_GREEN = [hexc("#2F5A3A"), hexc("#3D7148"), hexc("#23452D")]
SAKURA = [hexc("#D980AC"), hexc("#F4A8CB"), hexc("#A95B86")]
TRUNK = hexc("#4A3230")
CONCRETE = [hexc("#5A5666"), hexc("#6E6A7E"), hexc("#4A4656"), hexc("#3F3B4A")]


# ---------------------------------------------------------------- helpers

def new(w, h, fill=CLEAR):
    return Image.new("RGBA", (w, h), fill)


def rect(d, x, y, w, h, c):
    if w > 0 and h > 0:
        d.rectangle([x, y, x + w - 1, y + h - 1], fill=c)


def boxed(d, x, y, w, h, c):
    """Filled box with a 1 px navy outline around it (outline drawn outside the box)."""
    rect(d, x - 1, y - 1, w + 2, h + 2, OUT)
    rect(d, x, y, w, h, c)


def shade(c, amount):
    """Lighten (amount > 0, towards ivory) or darken (amount < 0, towards navy)."""
    target = (255, 244, 226) if amount > 0 else OUT[:3]
    a = abs(amount)
    return tuple(round(c[i] + (target[i] - c[i]) * a) for i in range(3)) + (255,)


def outline(img):
    """Turn every opaque pixel that touches transparency (or the canvas edge) into navy outline."""
    px = img.load()
    w, h = img.size
    edge = []
    for y in range(h):
        for x in range(w):
            if px[x, y][3] == 0:
                continue
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if not (0 <= nx < w and 0 <= ny < h) or px[nx, ny][3] == 0:
                    edge.append((x, y))
                    break
    for p in edge:
        px[p] = OUT
    return img


def speckle(img, x0, y0, w, h, colors, density, rng, mask=None):
    px = img.load()
    for _ in range(int(w * h * density)):
        x = rng.randrange(x0, x0 + w)
        y = rng.randrange(y0, y0 + h)
        if mask is None or px[x, y] == mask:
            px[x, y] = rng.choice(colors)


def place(occupied, rng, x0, y0, w, h, pw, ph, margin=3, tries=40):
    """Find a spot for a pw x ph prop inside the area that doesn't overlap earlier props."""
    for _ in range(tries):
        if w - pw - 2 * margin < 0 or h - ph - 2 * margin < 0:
            return None
        x = rng.randint(x0 + margin, x0 + w - pw - margin)
        y = rng.randint(y0 + margin, y0 + h - ph - margin)
        if all(x + pw + 2 <= ox or ox + ow + 2 <= x or y + ph + 2 <= oy or oy + oh + 2 <= y
               for ox, oy, ow, oh in occupied):
            occupied.append((x, y, pw, ph))
            return x, y
    return None


# ---------------------------------------------------------------- roof and facade details

def ac_unit(d, x, y):
    boxed(d, x, y, 8, 6, hexc("#6E6A7E"))
    rect(d, x, y, 8, 2, hexc("#8A8699"))
    rect(d, x + 4, y + 2, 3, 3, hexc("#2A2836"))


def water_tank(d, x, y):
    boxed(d, x, y, 9, 11, hexc("#77738A"))
    rect(d, x, y, 9, 3, hexc("#9894A8"))
    rect(d, x, y + 6, 9, 1, hexc("#5E5A70"))
    rect(d, x + 7, y + 3, 2, 8, hexc("#625E74"))


def stair_box(d, x, y, w=12, h=10):
    boxed(d, x, y, w, h, hexc("#5E5A6E"))
    rect(d, x, y, w, h - 4, hexc("#6E6A80"))
    rect(d, x, y + h - 4, w, 1, OUT)
    rect(d, x + w // 2 - 1, y + h - 3, 3, 3, WIN_LIT[0])


def helipad(d, cx, cy, r):
    d.ellipse([cx - r - 1, cy - r - 1, cx + r + 1, cy + r + 1], fill=OUT)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=hexc("#2E2B3A"))
    d.ellipse([cx - r + 2, cy - r + 2, cx + r - 2, cy + r - 2], outline=hexc("#D8B048"))
    y = hexc("#D8B048")
    rect(d, cx - 3, cy - 4, 2, 9, y)
    rect(d, cx + 2, cy - 4, 2, 9, y)
    rect(d, cx - 1, cy, 3, 2, y)


def billboard(d, rng, x, y, w, h):
    """Rooftop neon sign seen from the front: glowing frame around abstract lettering blocks."""
    neon = rng.choice(NEON)
    boxed(d, x, y, w, h, SIGN_BG)
    d.rectangle([x + 1, y + 1, x + w - 2, y + h - 2], outline=neon)
    other = rng.choice([c for c in NEON if c != neon])
    cx = x + 4
    while cx < x + w - 6:
        gw = rng.randint(3, 6)
        if cx + gw > x + w - 4:
            break
        col = rng.choice((neon, other, hexc("#F6EEF8")))
        # A few strokes per "glyph" so it reads as signage without being real text.
        rect(d, cx, y + 3, gw, 1, col)
        rect(d, cx + rng.randint(0, gw - 1), y + 3, 1, h - 6, col)
        if rng.random() < 0.6:
            rect(d, cx, y + h - 4, gw, 1, col)
        if rng.random() < 0.5:
            rect(d, cx, y + h // 2, gw, 1, col)
        cx += gw + 3
    # legs down to the roof
    rect(d, x + 3, y + h + 1, 1, 2, OUT)
    rect(d, x + w - 4, y + h + 1, 1, 2, OUT)


def vertical_sign(d, rng, x, y, h):
    """Tall narrow neon sign (tate-kanban) hanging on a facade."""
    neon = rng.choice(NEON)
    boxed(d, x, y, 7, h, SIGN_BG)
    rect(d, x, y, 7, h, neon)
    rect(d, x + 1, y + 1, 5, h - 2, SIGN_BG)
    cy = y + 3
    while cy < y + h - 5:
        col = rng.choice((neon, hexc("#F6EEF8"), rng.choice(NEON)))
        rect(d, x + 2, cy, 3, 1, col)
        rect(d, x + 2 + rng.randint(0, 2), cy, 1, 3, col)
        cy += 5


def lantern(d, x, y):
    """Red paper lantern (chochin)."""
    rect(d, x, y, 3, 1, OUT)
    boxed(d, x - 1, y + 1, 5, 5, LANTERN)
    rect(d, x, y + 2, 1, 2, LANTERN_HI)


def window_grid(d, rng, x0, y0, w, h, ww, wh, sx, sy, lit=0.45, margin=3):
    nx = max(1, (w - 2 * margin - ww) // sx + 1)
    ny = max(1, (h - 2 * margin - wh) // sy + 1)
    ox = x0 + (w - ((nx - 1) * sx + ww)) // 2
    oy = y0 + (h - ((ny - 1) * sy + wh)) // 2
    for j in range(ny):
        floor_lit = min(0.95, max(0.05, lit + rng.uniform(-0.3, 0.3)))
        for i in range(nx):
            x, y = ox + i * sx, oy + j * sy
            if rng.random() < floor_lit:
                c = rng.choice(WIN_LIT)
                rect(d, x, y, ww, wh, c)
                rect(d, x, y, ww, 1, shade(c, 0.4))
            else:
                rect(d, x, y, ww, wh, WIN_DIM if rng.random() < 0.7 else WIN_OFF)


def storefront(d, rng, x, y, w, h, glass=None):
    """Lit ground floor: glass band with a door."""
    glass = glass or rng.choice((hexc("#F2D59A"), hexc("#E8C58A"), hexc("#F6E2B8")))
    rect(d, x + 2, y + 1, w - 4, h - 2, glass)
    for mx in range(x + 6, x + w - 4, 9):
        rect(d, mx, y + 1, 1, h - 2, shade(glass, -0.35))
    door = x + w // 2 - 3
    rect(d, door, y + 2, 6, h - 2, shade(glass, -0.55))
    rect(d, door + 1, y + 3, 4, h - 4, shade(glass, 0.3))


# ---------------------------------------------------------------- buildings

ROOFS = [
    (hexc("#4A4757"), hexc("#625F74"), hexc("#3A3746")),
    (hexc("#4E4A52"), hexc("#67626C"), hexc("#3C3940")),
    (hexc("#524A4E"), hexc("#6C6266"), hexc("#3E383C")),
    (hexc("#45424F"), hexc("#5D596B"), hexc("#36333F")),
    (hexc("#57423F"), hexc("#705652"), hexc("#443230")),
    (hexc("#48493E"), hexc("#606253"), hexc("#383930")),
    (hexc("#4B3F52"), hexc("#63546C"), hexc("#3A3040")),
]
FACADES = [hexc("#2F2C3C"), hexc("#3A3444"), hexc("#35302F"), hexc("#3A3A40")]


def box_building(w, h, f, rng, roof=None, facade=None):
    """Canvas with a flat parapet roof on top and a front face of height f at the bottom."""
    img = new(w, h)
    d = ImageDraw.Draw(img)
    base, hi, lo = roof or rng.choice(ROOFS)
    rt = h - f
    rect(d, 0, 0, w, rt, base)
    d.rectangle([1, 1, w - 2, rt - 1], outline=hi)
    rect(d, 2, 2, w - 4, 1, lo)
    rect(d, 2, 2, 1, rt - 3, lo)
    speckle(img, 3, 3, w - 6, rt - 6, [shade(base, 0.08), shade(base, -0.08)], 0.03, rng)
    fc = facade or rng.choice(FACADES)
    rect(d, 0, rt, w, f, fc)
    rect(d, 1, rt, 1, f, shade(fc, 0.12))
    rect(d, w - 2, rt, 1, f, shade(fc, -0.25))
    rect(d, 0, rt, w, 1, OUT)
    rect(d, 0, rt + 1, w, 1, shade(fc, -0.3))
    return img, d, rt, (base, hi, fc)


def roof_props(d, rng, w, rt, big):
    occupied = []
    if big and rng.random() < 0.45:
        r = min(w, rt) // 4
        cx, cy = w // 2 + rng.randint(-6, 6), rt // 2
        occupied.append((cx - r - 2, cy - r - 2, 2 * r + 4, 2 * r + 4))
        helipad(d, cx, cy, r)
    if (p := place(occupied, rng, 3, 3, w - 6, rt - 6, 14, 12)):
        stair_box(d, p[0] + 1, p[1] + 1)
    if rng.random() < 0.7 and (p := place(occupied, rng, 3, 3, w - 6, rt - 6, 11, 13)):
        water_tank(d, p[0] + 1, p[1] + 1)
    for _ in range(max(1, w * rt // 1400) + rng.randint(0, 2)):
        if (p := place(occupied, rng, 3, 3, w - 6, rt - 6, 10, 8)):
            ac_unit(d, p[0] + 1, p[1] + 1)
    return occupied


def office(w, h, rng):
    f = round(h * (rng.uniform(0.40, 0.46) if h > w else rng.uniform(0.28, 0.36)))
    img, d, rt, colors = box_building(w, h, f, rng)
    roof_props(d, rng, w, rt, w >= 100)
    window_grid(d, rng, 2, rt + 2, w - 4, f - 10, 3, 4, 5, 6, lit=rng.uniform(0.3, 0.6))
    storefront(d, rng, 0, h - 8, w, 8)
    return outline(img), list(colors)


def apartment(w, h, rng):
    f = round(h * (rng.uniform(0.42, 0.48) if h > w else rng.uniform(0.32, 0.38)))
    wall = rng.choice((hexc("#6E6254"), hexc("#665A5E"), hexc("#5E5A52")))
    roof = (hexc("#5C5866"), hexc("#747080"), hexc("#4A4654"))
    img, d, rt, colors = box_building(w, h, f, rng, roof=roof, facade=wall)
    roof_props(d, rng, w, rt, False)
    rail = shade(wall, 0.3)
    y = rt + 3
    while y + 8 <= h - 3:
        window_grid(d, rng, 2, y, w - 4, 5, 5, 4, 8, 6, lit=rng.uniform(0.25, 0.6), margin=2)
        rect(d, 2, y + 5, w - 4, 1, rail)
        rect(d, 2, y + 6, w - 4, 1, shade(wall, -0.3))
        for _ in range(w // 14):  # laundry on the balcony rails
            if rng.random() < 0.5:
                rect(d, rng.randint(3, w - 5), y + 4, 2, 2, rng.choice(NEON[:3] + [hexc("#E8E4EC")]))
        y += 8
    return outline(img), list(colors) + [wall]


def neon_building(w, h, rng):
    f = round(h * (rng.uniform(0.40, 0.46) if h > w else rng.uniform(0.30, 0.36)))
    img, d, rt, colors = box_building(w, h, f, rng, facade=hexc("#28253A"))
    occupied = roof_props(d, rng, w, rt - 16, False)
    bw = min(w - 8, max(30, int(w * 0.8)))
    billboard(d, rng, (w - bw) // 2, rt - 16, bw, 12)
    window_grid(d, rng, 2, rt + 2, w - 4, f - 10, 3, 3, 5, 5, lit=0.35)
    for sx in ([4] if w < 70 else [4, w - 11]):
        vertical_sign(d, rng, sx, rt + 3, f - 12)
    storefront(d, rng, 0, h - 8, w, 8, glass=rng.choice(NEON))
    return outline(img), list(colors) + [rng.choice(NEON)]


def konbini(w, h, rng):
    f = 22
    roof = (hexc("#6A6778"), hexc("#838094"), hexc("#565366"))
    img, d, rt, colors = box_building(w, h, f, rng, roof=roof, facade=hexc("#D8D4DE"))
    roof_props(d, rng, w, rt, False)
    stripes = rng.choice(((hexc("#FF8A3D"), hexc("#3FA66A"), hexc("#E0463C")),
                          (hexc("#4F7FD8"), hexc("#E8E4EC"), hexc("#4F7FD8")),
                          (hexc("#3FA66A"), hexc("#E8E4EC"), hexc("#FFB23D"))))
    for i, c in enumerate(stripes):
        rect(d, 1, rt + 2 + i * 2, w - 2, 2, c)
    glass = hexc("#FFF2CC")
    rect(d, 3, rt + 9, w - 6, f - 11, glass)
    for sx in range(8, w - 6, 7):
        rect(d, sx, rt + 11, 1, f - 14, hexc("#B8A888"))
    rect(d, w // 2 - 5, rt + 10, 10, f - 10, hexc("#C8B890"))
    rect(d, w // 2 - 4, rt + 11, 8, f - 12, hexc("#FFFBEA"))
    return outline(img), list(colors) + list(stripes)


def shop_row(w, h, rng):
    f = 22
    img, d, rt, colors = box_building(w, h, f, rng, facade=hexc("#5A3A2E"))
    roof_props(d, rng, w, rt, False)
    n = rng.choice((2, 3))
    sw = w // n
    extra = []
    for i in range(n):
        x = i * sw
        wood = rng.choice((hexc("#6B4432"), hexc("#5A3A2E"), hexc("#4A3A44")))
        rect(d, x + 1, rt + 2, sw - 1, f - 2, wood)
        rect(d, x, rt + 1, 1, f - 1, OUT)
        awning = rng.choice(NEON[:3] + [hexc("#E8E4EC")])
        for ax in range(x + 1, x + sw):
            rect(d, ax, rt + 2, 1, 4, awning if (ax // 3) % 2 else shade(awning, -0.4))
        rect(d, x + 1, rt + 6, sw - 1, 1, OUT)
        glow = rng.choice(WIN_LIT)
        rect(d, x + 4, rt + 9, sw - 8, f - 10, glow)
        noren = rng.choice((hexc("#8E2A22"), hexc("#3A2E5A"), hexc("#2E2A2E")))
        for nx in range(x + 4, x + sw - 4, 4):
            rect(d, nx, rt + 9, 3, 5, noren)
        lantern(d, x + 2, rt + 8)
        extra.append(awning)
    return outline(img), list(colors) + extra


def kawara_roof(d, x, y, w, h, base):
    """Japanese clay-tile gabled roof seen from above: tile rows and a lighter ridge."""
    for ty in range(y, y + h):
        rect(d, x, ty, w, 1, base if (ty - y) % 3 else shade(base, -0.2))
    ridge = y + h // 2 - 1
    rect(d, x, ridge, w, 2, shade(base, 0.25))
    rect(d, x, ridge + 2, w, 1, shade(base, -0.35))


def house(w, h, rng):
    f = 16
    img = new(w, h)
    d = ImageDraw.Draw(img)
    rt = h - f
    tile = rng.choice((hexc("#4A4458"), hexc("#524A4A"), hexc("#3E4450"), hexc("#5A4038")))
    kawara_roof(d, 0, 0, w, rt, tile)
    wall = rng.choice((hexc("#9C8B76"), hexc("#8A8078"), hexc("#A08870")))
    rect(d, 0, rt, w, f, wall)
    rect(d, 0, rt, w, 1, OUT)
    rect(d, 0, rt + 1, w, 2, shade(wall, -0.4))  # eave shadow
    wx = rng.randint(4, 8)
    rect(d, wx, rt + 5, 12, 7, hexc("#3A3040"))
    rect(d, wx + 1, rt + 6, 10, 5, rng.choice(WIN_LIT) if rng.random() < 0.7 else WIN_DIM)
    rect(d, wx + 6, rt + 6, 1, 5, hexc("#3A3040"))
    dx = w - 16
    rect(d, dx, rt + 4, 10, f - 4, hexc("#4A3230"))
    rect(d, dx + 1, rt + 5, 8, f - 5, hexc("#6B4432"))
    rect(d, dx + 4, rt + 5, 1, f - 5, hexc("#4A3230"))
    if rng.random() < 0.5:  # potted plants by the door
        rect(d, dx - 4, h - 5, 3, 3, TREE_GREEN[1])
    return outline(img), [tile, wall, shade(tile, 0.25)]


def small_shop(w, h, rng):
    f = 20
    if rng.random() < 0.5:
        img = new(w, h)
        d = ImageDraw.Draw(img)
        tile = rng.choice((hexc("#4A4458"), hexc("#3E3A40")))
        kawara_roof(d, 0, 0, w, h - f, tile)
        colors = [tile]
    else:
        img, d, _, c = box_building(w, h, f, rng)
        roof_props(d, rng, w, h - f, False)
        colors = list(c)
    rt = h - f
    wood = rng.choice((hexc("#6B4432"), hexc("#5A3A2E")))
    rect(d, 0, rt, w, f, wood)
    rect(d, 0, rt, w, 1, OUT)
    sign = rng.choice(NEON)
    boxed(d, 4, rt + 2, w - 8, 5, SIGN_BG)
    for gx in range(6, w - 6, 5):
        rect(d, gx, rt + 3, 3, 3, sign if rng.random() < 0.8 else hexc("#F6EEF8"))
    rect(d, 5, rt + 9, w - 10, f - 9, rng.choice(WIN_LIT))
    noren = rng.choice((hexc("#8E2A22"), hexc("#3A2E5A")))
    for nx in range(8, w - 8, 5):
        rect(d, nx, rt + 9, 4, 6, noren)
        rect(d, nx, rt + 11, 4, 1, hexc("#E8E4EC"))
    lantern(d, 2, rt + 9)
    lantern(d, w - 5, rt + 9)
    return outline(img), colors + [wood, sign]


def temple(rng):
    w, h, f = 176, 112, 40
    img = new(w, h)
    d = ImageDraw.Draw(img)
    rt = h - f
    tile = hexc("#3E4A44")
    # Hipped roof: tile rows, ridge, and hip lines running to the corners.
    rect(d, 0, 0, w, rt, tile)
    for ty in range(rt):
        rect(d, 0, ty, w, 1, tile if ty % 3 else shade(tile, -0.2))
    ridge_y = 22
    rect(d, 40, ridge_y, w - 80, 3, shade(tile, 0.3))
    rect(d, 36, ridge_y - 2, 6, 7, hexc("#C9A64A"))       # gilded ridge ends
    rect(d, w - 42, ridge_y - 2, 6, 7, hexc("#C9A64A"))
    for t in range(40):
        rect(d, 40 - t, ridge_y + 1 + round(t * (rt - ridge_y - 4) / 40), 2, 1, shade(tile, 0.18))
        rect(d, w - 42 + t, ridge_y + 1 + round(t * (rt - ridge_y - 4) / 40), 2, 1, shade(tile, 0.18))
    rect(d, 0, rt - 4, w, 4, shade(tile, -0.35))  # deep eaves
    # Upturned eave corners
    for x, y in ((0, rt - 6), (1, rt - 5), (w - 1, rt - 6), (w - 2, rt - 5)):
        img.putpixel((x, y), shade(tile, 0.3))
    # Hall: vermilion posts, dark interior with lantern glow, white plaster band, stone base.
    x0, x1 = 12, w - 12
    red = hexc("#C8412A")
    rect(d, x0, rt, x1 - x0, f, hexc("#2A1A1E"))
    rect(d, x0, rt, x1 - x0, 4, hexc("#D8CFC0"))
    for px in range(x0, x1 - 3, 19):
        rect(d, px, rt + 4, 4, f - 10, red)
        rect(d, px, rt + 4, 1, f - 10, shade(red, 0.3))
    for gx in range(x0 + 10, x1 - 10, 19):
        rect(d, gx + 2, rt + 12, 5, 8, hexc("#FFB86A"))
    rect(d, x0 - 4, h - 6, x1 - x0 + 8, 6, STONE[0])
    rect(d, x0 - 4, h - 6, x1 - x0 + 8, 1, STONE[1])
    rect(d, w // 2 - 18, h - 6, 36, 6, STONE[1])
    for sy in range(h - 5, h, 2):
        rect(d, w // 2 - 18, sy, 36, 1, STONE[0])
    rect(d, w // 2 - 10, rt + 8, 20, 5, hexc("#8E6A3A"))  # offering box
    rect(d, w // 2 - 2, rt + 4, 4, 4, hexc("#C9A64A"))    # bell
    # Rope and paper streamers across the entrance
    rect(d, w // 2 - 24, rt + 5, 48, 1, hexc("#D8C8A0"))
    for sx in range(w // 2 - 20, w // 2 + 22, 8):
        rect(d, sx, rt + 6, 2, 3, hexc("#F1FCFF"))
    return outline(img), [tile, red, hexc("#D8CFC0"), STONE[0], hexc("#C9A64A")]


def tokyo_tower(rng):
    w, h = 112, 240
    img = new(w, h)
    px = img.load()
    cx = w // 2
    orange, white = hexc("#E8553A"), hexc("#E8E2EA")
    top, base = 34, h - 1

    def half_width(y):
        t = (y - top) / (base - top)
        return 5 + 47 * t * t

    for y in range(top, h):
        hw = half_width(y)
        band = orange if int((base - y) / 22) % 2 == 0 else white
        # Arch between the four legs
        arch = (hw - 11) * math.sqrt(max(0.0, (y - 196) / 43)) if y > 196 else -1
        for x in range(w):
            dx = x - cx + 0.5
            if abs(dx) > hw or abs(dx) < arch:
                continue
            lattice = (x + y) % 7 == 0 or (x - y) % 7 == 0 or abs(dx) > hw - 2
            c = band if lattice else shade(band, -0.45)
            if dx > hw * 0.35:
                c = shade(c, -0.2)
            px[x, y] = c
    d = ImageDraw.Draw(img)
    # Observation decks with lit windows
    for y, hw, hh in ((150, 30, 12), (84, 14, 8)):
        rect(d, cx - hw, y, 2 * hw, hh, hexc("#D8D0DA"))
        rect(d, cx - hw, y, 2 * hw, 2, hexc("#F1ECF2"))
        rect(d, cx - hw + 2, y + 4, 2 * hw - 4, hh - 7, hexc("#FFD27A"))
        for wx in range(cx - hw + 4, cx + hw - 3, 4):
            rect(d, wx, y + 4, 1, hh - 7, hexc("#B89050"))
        rect(d, cx - hw, y + hh - 1, 2 * hw, 1, shade(orange, -0.3))
    # Antenna
    for y in range(4, top + 2):
        rect(d, cx - 1, y, 2, 1, orange if (y // 5) % 2 else white)
    rect(d, cx - 1, 1, 2, 3, hexc("#FF3B3B"))
    outline(img)
    return img, [orange, white, hexc("#D8D0DA"), shade(orange, -0.45)]


# ---------------------------------------------------------------- rubble and dust

def rubble(size, colors, rng, pile_height=None):
    """A heap of smashed building bits filling the footprint, in the building's own colors."""
    w, h = size
    img = new(w, h)
    d = ImageDraw.Draw(img)
    px = img.load()
    ph = pile_height or h
    cx, cy = w / 2, h - ph * 0.5
    rx, ry = w / 2 - 2, ph * 0.47
    jag = [rng.uniform(-0.25, 0.1) for _ in range(w // 4 + 2)]
    # Squarish heap (superellipse) so the rubble covers the building's footprint.
    inside = lambda x, y: abs((x - cx) / rx) ** 4 + abs((y - cy) / ry) ** 4 <= 1 + jag[x // 4]
    base = CONCRETE[3]
    for y in range(h):
        for x in range(w):
            if inside(x, y):
                px[x, y] = base
    palette = colors + CONCRETE[:3]
    for _ in range(int(w * ph / 14)):
        x, y = rng.randrange(w), rng.randrange(h)
        if not inside(x, y):
            continue
        cw, ch = rng.randint(2, 6), rng.randint(2, 5)
        c = rng.choice(palette)
        for yy in range(y, min(h, y + ch)):
            for xx in range(x, min(w, x + cw)):
                if inside(xx, yy):
                    px[xx, yy] = c if yy > y else shade(c, 0.25)
        if rng.random() < 0.3:  # shadow under the chunk
            for xx in range(x, min(w, x + cw)):
                if y + ch < h and inside(xx, y + ch):
                    px[xx, y + ch] = shade(c, -0.5)
    # Broken wall stubs sticking out of the pile, with a couple of dead windows
    for _ in range(rng.randint(1, 3)):
        sw, sh = rng.randint(6, 12), rng.randint(8, 14)
        sx = rng.randint(int(cx - rx * 0.6), int(cx + rx * 0.6) - sw)
        sy = int(cy - ry * rng.uniform(0.2, 0.6))
        c = rng.choice(colors)
        rect(d, sx, sy, sw, sh, c)
        rect(d, sx, sy, sw, 1, shade(c, 0.25))
        for wy in range(sy + 3, sy + sh - 3, 5):
            rect(d, sx + 2, wy, 2, 2, WIN_DIM)
        for k in range(0, sw, 2):  # jagged top
            if rng.random() < 0.5:
                rect(d, sx + k, sy, 2, rng.randint(1, 3), CLEAR)
    # Bent rebar
    for _ in range(rng.randint(3, 6)):
        x, y = rng.randint(4, w - 5), rng.randint(int(h - ph * 0.8), h - 4)
        if inside(x, y):
            dx, dy = rng.choice((-1, 1)), -1
            for k in range(rng.randint(4, 9)):
                if 0 <= x < w and 0 <= y < h:
                    px[x, y] = hexc("#2A2836")
                x += dx if k % 2 else 0
                y += dy
    # Loose debris around the heap
    for _ in range(max(6, w // 6)):
        x, y = rng.randint(1, w - 5), rng.randint(max(1, h - ph), h - 5)
        if not inside(x, y):
            rect(d, x, y, 3, 3, rng.choice(palette))
    return outline(img)


def dust_frames(rng):
    """Six-frame pixel dust poof, 48 x 48, expanding then breaking up."""
    frames = []
    puffs = [(rng.uniform(0, math.tau), rng.uniform(0.6, 1.0), rng.uniform(0.8, 1.2)) for _ in range(9)]
    colors = (hexc("#8A8494"), hexc("#A8A2B2"), hexc("#6E6878"))
    for i in range(6):
        img = new(48, 48)
        d = ImageDraw.Draw(img)
        spread = 3 + i * 3.4
        size = [4, 6, 7, 7, 5, 3][i]
        for k, (a, dist, s) in enumerate(puffs):
            if i >= 4 and k % 2:
                continue
            x = 24 + math.cos(a) * spread * dist
            y = 26 + math.sin(a) * spread * dist * 0.8 - i * 1.2
            r = size * s
            d.ellipse([x - r, y - r, x + r, y + r], fill=colors[0])
        for k, (a, dist, s) in enumerate(puffs):
            if i >= 4 and k % 2:
                continue
            x = 24 + math.cos(a) * spread * dist - size * 0.35
            y = 26 + math.sin(a) * spread * dist * 0.8 - i * 1.2 - size * 0.35
            r = size * s * 0.5
            d.ellipse([x - r, y - r, x + r, y + r], fill=colors[1])
        frames.append(outline(img))
    return frames


# ---------------------------------------------------------------- ground props

def tree(rng, sakura=False, r=None):
    r = r or rng.randint(8, 12)
    shades = SAKURA if sakura else TREE_GREEN
    w, h = 2 * r + 4, 2 * r + 9
    img = new(w, h)
    d = ImageDraw.Draw(img)
    cx, cy = w // 2, r + 2
    rect(d, cx - 2, cy + r - 3, 4, 8, TRUNK)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=shades[0])
    d.ellipse([cx - r + 2, cy - r + 1, cx + r * 0.3, cy + r * 0.2], fill=shades[1])
    d.ellipse([cx, cy, cx + r - 1, cy + r - 1], fill=shades[2])
    d.ellipse([cx - r * 0.2, cy - r * 0.2, cx + r * 0.5, cy + r * 0.5], fill=shades[0])
    speckle(img, 2, 2, w - 4, 2 * r - 2, [shades[1], shades[2]], 0.08, rng, mask=shades[0])
    return outline(img)


def paste_tree(ground, rng, x, y, sakura, shadow_color):
    t = tree(rng, sakura)
    d = ImageDraw.Draw(ground)
    w, h = t.size
    d.ellipse([x - w // 2 + 2, y - 4, x + w // 2 + 2, y + 3], fill=shadow_color)
    ground.alpha_composite(t, (x - w // 2, y - h + 2))
    if sakura:  # fallen petals
        for _ in range(12):
            px_, py_ = x + rng.randint(-w, w), y + rng.randint(-4, 8)
            if 0 <= px_ < ground.width and 0 <= py_ < ground.height:
                ground.putpixel((px_, py_), SAKURA[1])


def lamp(ground, x, y, pool_color):
    """Street lamp with a soft pool of light: solid in the middle, checker-dithered at the rim."""
    d = ImageDraw.Draw(ground)
    mask = new(ground.width, ground.height)
    ImageDraw.Draw(mask).ellipse([x - 16, y - 7, x + 16, y + 9], fill=(255, 255, 255, 255))
    mpx, gpx = mask.load(), ground.load()
    for py in range(max(0, y - 7), min(ground.height, y + 10)):
        for px_ in range(max(0, x - 16), min(ground.width, x + 17)):
            if mpx[px_, py][3] and (px_ + py) % 2 == 0:
                gpx[px_, py] = pool_color
    d.ellipse([x - 10, y - 4, x + 10, y + 6], fill=pool_color)
    rect(d, x - 1, y - 14, 3, 15, OUT)
    rect(d, x, y - 13, 1, 13, hexc("#4A4656"))
    boxed(d, x - 2, y - 18, 5, 4, hexc("#FFE6A8"))


def bench(ground, x, y, horizontal=True):
    d = ImageDraw.Draw(ground)
    if horizontal:
        boxed(d, x, y, 14, 4, hexc("#7A5238"))
        rect(d, x, y, 14, 1, hexc("#9A6E4A"))
    else:
        boxed(d, x, y, 4, 14, hexc("#7A5238"))
        rect(d, x, y, 1, 14, hexc("#9A6E4A"))


def stone_lantern(ground, x, y):
    d = ImageDraw.Draw(ground)
    boxed(d, x - 1, y - 3, 4, 4, STONE[0])
    boxed(d, x - 3, y - 9, 8, 5, STONE[1])
    rect(d, x - 1, y - 8, 4, 3, hexc("#FFD27A"))
    boxed(d, x - 4, y - 12, 10, 2, STONE[0])


def vending_machine(d, x, y, body):
    boxed(d, x, y, 8, 13, body)
    rect(d, x + 1, y + 1, 6, 6, hexc("#F6EEF8"))
    for k in range(3):
        rect(d, x + 1 + k * 2, y + 2, 1, 2, rng_colors[k])
        rect(d, x + 1 + k * 2, y + 5, 1, 1, rng_colors[(k + 1) % 3])
    rect(d, x + 1, y + 9, 6, 2, hexc("#1A1826"))


rng_colors = (hexc("#E0463C"), hexc("#3FA66A"), hexc("#FFB23D"))


def car(d, rng, x, y, vertical):
    body = rng.choice((hexc("#C8C4D0"), hexc("#B83A3A"), hexc("#2E2A3A"), hexc("#D8B048"), hexc("#E8E4EC")))
    w, h = (12, 20) if vertical else (20, 12)
    boxed(d, x, y, w, h, body)
    if vertical:
        rect(d, x + 2, y + 4, 8, 4, hexc("#3A4058"))
        rect(d, x + 2, y + 13, 8, 3, hexc("#3A4058"))
        rect(d, x + 2, y + 8, 8, 5, shade(body, 0.2))
    else:
        rect(d, x + 4, y + 2, 4, 8, hexc("#3A4058"))
        rect(d, x + 13, y + 2, 3, 8, hexc("#3A4058"))
        rect(d, x + 8, y + 2, 5, 8, shade(body, 0.2))


# ---------------------------------------------------------------- ground chunks

def fill_pattern(img, x0, y0, w, h, base, joint, step):
    d = ImageDraw.Draw(img)
    rect(d, x0, y0, w, h, base)
    for x in range(x0 + step, x0 + w, step):
        rect(d, x, y0, 1, h, joint)
    for y in range(y0 + step, y0 + h, step):
        rect(d, x0, y, w, 1, joint)


EDGES = ("left", "right", "top", "bottom")
CANAL_WATER = 40   # water half-width at a canal edge; then stone wall, railing, promenade
PROMENADE = hexc("#44383C")
PROMENADE_JOINT = hexc("#3A3034")
ALLEY = hexc("#2A2731")
ALLEY_GUTTER = hexc("#1E1C25")
ALLEY_TILE = 32    # alley half-lanes are tiled along their length in Unity
ALLEY_WIDTH = 22   # half-lane (20) plus gutter (2), measured from the chunk edge


def edge_distance(x, y, edge):
    return {"left": x, "right": CHUNK - 1 - x, "top": y, "bottom": CHUNK - 1 - y}[edge]


def pavement_base(img):
    fill_pattern(img, 0, 0, CHUNK, CHUNK, PAVE, PAVE_JOINT, 16)


def road_canvas(edges, crosswalks, rng, lamps=()):
    """A transparent chunk with half-roads and sidewalks along the given edges. Every road piece is
    cropped out of one of these, so pieces line up pixel-exactly (joints, dashes) wherever they meet."""
    img = new(CHUNK, CHUNK)
    px = img.load()
    for y in range(CHUNK):
        for x in range(CHUNK):
            dist = min(edge_distance(x, y, e) for e in edges)
            if dist >= INNER:
                continue
            if dist < ROAD:
                c = ASPHALT
            elif dist == ROAD:
                c = CURB
            elif dist == ROAD + 1:
                c = shade(CURB, 0.15)
            elif 55 <= dist <= 58:
                c = TACTILE_DOT if dist == 56 and (x + y) % 2 == 0 else TACTILE
            else:
                c = SIDEWALK_JOINT if x % 8 == 0 or y % 8 == 0 else SIDEWALK_C
            px[x, y] = c
    speckle(img, 0, 0, CHUNK, CHUNK, [ASPHALT_DARK, ASPHALT_LIGHT], 0.05, rng, mask=ASPHALT)
    d = ImageDraw.Draw(img)
    # Dashed center line on the chunk edge (one pixel per chunk, two where chunks meet). It stops
    # short of intersections, which have crosswalks instead.
    lo, hi = (INNER, CHUNK - INNER) if crosswalks else (0, CHUNK)
    for e in edges:
        for a in range(lo, hi):
            if (a // 12) % 2 == 0:
                p = {"left": (0, a), "right": (CHUNK - 1, a), "top": (a, 0), "bottom": (a, CHUNK - 1)}[e]
                if px[p] in (ASPHALT, ASPHALT_DARK, ASPHALT_LIGHT):
                    px[p] = ROAD_LINE
    if crosswalks:
        for e in edges:
            for c0 in (ROAD + 2, CHUNK - ROAD - 18):
                for k in range(0, ROAD - 4, 6):
                    if e == "left":
                        rect(d, 2 + k, c0, 4, 16, ROAD_LINE)
                    elif e == "right":
                        rect(d, CHUNK - 6 - k, c0, 4, 16, ROAD_LINE)
                    elif e == "top":
                        rect(d, c0, 2 + k, 16, 4, ROAD_LINE)
                    else:
                        rect(d, c0, CHUNK - 6 - k, 16, 4, ROAD_LINE)
    for x, y in lamps:
        lamp(img, x, y, LAMP_POOL)
    return img


def canal_canvas(rng):
    """Canals along the top and bottom edges: water, stone embankment, railing and a brick promenade."""
    img = new(CHUNK, CHUNK)
    px = img.load()
    for y in list(range(INNER)) + list(range(CHUNK - INNER, CHUNK)):
        dist = min(y, CHUNK - 1 - y)
        for x in range(CHUNK):
            if dist < CANAL_WATER:
                c = WATER[0]
            elif dist < CANAL_WATER + 4:
                c = STONE[2] if x % 6 else shade(STONE[2], -0.3)
                if dist == CANAL_WATER + 3:
                    c = STONE[0]
            elif dist == CANAL_WATER + 4:
                c = OUT
            elif dist == CANAL_WATER + 5:
                c = STONE[1] if x % 8 else OUT
            else:
                brick = x % 8 == 0 or (y % 4 == 0 and (x // 8) % 2 == 0) or (y % 4 == 2 and (x // 8) % 2 == 1)
                c = PROMENADE_JOINT if brick else PROMENADE
            px[x, y] = c
    d = ImageDraw.Draw(img)
    # Ripples and reflected neon on the water
    for top in (0, CHUNK - CANAL_WATER):
        for _ in range(60):
            x, y = rng.randrange(CHUNK - 6), rng.randrange(top, top + CANAL_WATER)
            rect(d, x, y, rng.randint(3, 7), 1, WATER[1] if rng.random() < 0.7 else WATER[2])
        for _ in range(10):
            x, y = rng.randrange(CHUNK - 6), rng.randrange(top, top + CANAL_WATER)
            rect(d, x, y, rng.randint(2, 4), 1, rng.choice(NEON + WIN_LIT))
    # Sakura along the top promenade (blossoms hang over the water), lamps and benches along the bottom one.
    for x in range(32, CHUNK, 64):
        paste_tree(img, rng, x, 66, True, PROMENADE_JOINT)
    for x in range(64, CHUNK, 128):
        lamp(img, x, CHUNK - 1 - 48, LAMP_POOL)
        bench(img, x + 24, CHUNK - 1 - 60, True)
    return img


def bridge_canvas(rng):
    """Road decks crossing the canal bands on the left and right edges (overlays on the canal)."""
    img = new(CHUNK, CHUNK)
    px = img.load()
    for y in list(range(INNER)) + list(range(CHUNK - INNER, CHUNK)):
        dist = min(y, CHUNK - 1 - y)
        for x in list(range(INNER)) + list(range(CHUNK - INNER, CHUNK)):
            side = min(x, CHUNK - 1 - x)
            if side < ROAD:
                c = ROAD_LINE if side == 0 and (y // 12) % 2 == 0 else ASPHALT
            elif dist >= CANAL_WATER + 6:
                continue  # the promenade doubles as the sidewalk here
            elif side < ROAD + 16:
                c = SIDEWALK_JOINT if x % 8 == 0 or y % 8 == 0 else SIDEWALK_C
            elif side in (ROAD + 16, INNER - 1):
                c = OUT
            else:
                c = STONE[1] if y % 6 else OUT
            px[x, y] = c
    speckle(img, 0, 0, CHUNK, CHUNK, [ASPHALT_DARK, ASPHALT_LIGHT], 0.05, rng, mask=ASPHALT)
    return img


def deadend_canvas(rng):
    """Roads that stop at a canal: asphalt across the promenade, ending in a striped barrier."""
    img = new(CHUNK, CHUNK)
    px = img.load()
    for y in list(range(INNER)) + list(range(CHUNK - INNER, CHUNK)):
        dist = min(y, CHUNK - 1 - y)
        if dist < CANAL_WATER + 4:
            continue
        for x in list(range(ROAD)) + list(range(CHUNK - ROAD, CHUNK)):
            if dist < CANAL_WATER + 8:
                stripe = ((x + y) // 3) % 2 == 0
                edge = dist in (CANAL_WATER + 4, CANAL_WATER + 7)
                c = OUT if edge else (hexc("#E0B84A") if stripe else hexc("#22202A"))
            else:
                c = ASPHALT
            px[x, y] = c
    speckle(img, 0, 0, CHUNK, CHUNK, [ASPHALT_DARK, ASPHALT_LIGHT], 0.05, rng, mask=ASPHALT)
    return img


def alley_tile(edge, rng):
    """One 32 px repeat of a half-lane backstreet running along a chunk edge (tiled in Unity)."""
    horizontal = edge in ("top", "bottom")
    w, h = (ALLEY_TILE, ALLEY_WIDTH) if horizontal else (ALLEY_WIDTH, ALLEY_TILE)
    img = new(w, h)
    px = img.load()
    for y in range(h):
        for x in range(w):
            across = {"left": x, "right": w - 1 - x, "top": y, "bottom": h - 1 - y}[edge]
            along = x if horizontal else y
            if across >= ALLEY_WIDTH - 2:
                c = ALLEY_GUTTER
            elif across < 2 and along % 8 < 4:
                c = shade(ALLEY, 0.08)  # worn line down the middle of the lane
            else:
                c = ALLEY
            px[x, y] = c
    speckle(img, 0, 0, w, h, [shade(ALLEY, 0.1), shade(ALLEY, -0.15)], 0.06, rng, mask=ALLEY)
    # A paper lantern hanging at the lane edge, once per repeat
    d = ImageDraw.Draw(img)
    near = ALLEY_WIDTH - 7
    x, y = {"left": (near, 12), "right": (w - 1 - near - 2, 12), "top": (12, near - 3), "bottom": (12, h - near - 4)}[edge]
    lantern(d, x, y)
    return img


def road_pieces(rng):
    """All the street pieces a chunk is assembled from, keyed by name (tl/tr/bl/br are world corners)."""
    full = road_canvas(EDGES, True, rng, lamps=[(52, 56), (460, 56), (52, 462), (460, 462),
                                                (256, 58), (256, 462), (52, 256), (460, 256)])
    pieces = {}
    # Straight half-road strips between the corner regions (PIL y runs down, so "top" is PIL y = 0).
    pieces["road_top"] = full.crop((INNER, 0, CHUNK - INNER, INNER))
    pieces["road_bottom"] = full.crop((INNER, CHUNK - INNER, CHUNK - INNER, CHUNK))
    pieces["road_left"] = full.crop((0, INNER, INNER, CHUNK - INNER))
    pieces["road_right"] = full.crop((CHUNK - INNER, INNER, CHUNK, CHUNK - INNER))
    corners = {"tl": (0, 0), "tr": (CHUNK - INNER, 0), "bl": (0, CHUNK - INNER), "br": (CHUNK - INNER, CHUNK - INNER)}
    variants = {
        "corner": full,
        "sideV_cross": road_canvas(("left", "right"), True, rng),
        "sideV_plain": road_canvas(("left", "right"), False, rng),
        "sideH_cross": road_canvas(("top", "bottom"), True, rng),
        "sideH_plain": road_canvas(("top", "bottom"), False, rng),
    }
    for name, canvas in variants.items():
        for c, (x, y) in corners.items():
            pieces[f"{name}_{c}"] = canvas.crop((x, y, x + INNER, y + INNER))
    canal = canal_canvas(rng)
    pieces["canal_top"] = canal.crop((0, 0, CHUNK, INNER))
    pieces["canal_bottom"] = canal.crop((0, CHUNK - INNER, CHUNK, CHUNK))
    bridge, deadend = bridge_canvas(rng), deadend_canvas(rng)
    for c, (x, y) in corners.items():
        pieces[f"bridge_{c}"] = bridge.crop((x, y, x + INNER, y + INNER))
        dx = 0 if c[1] == "l" else CHUNK - ROAD
        pieces[f"deadend_{c}"] = deadend.crop((dx, y, dx + ROAD, y + INNER))
    for e in EDGES:
        pieces[f"alley_{e}"] = alley_tile(e, rng)
    return pieces


def ground_downtown(rng):
    img = new(CHUNK, CHUNK)
    pavement_base(img)
    return img


def grass(img, x0, y0, w, h, rng):
    d = ImageDraw.Draw(img)
    rect(d, x0, y0, w, h, GRASS[0])
    speckle(img, x0, y0, w, h, GRASS[1:], 0.12, rng)


def ground_park(rng):
    img = new(CHUNK, CHUNK)
    pavement_base(img)
    n = CHUNK - 2 * INNER
    grass(img, INNER, INNER, n, n, rng)
    d = ImageDraw.Draw(img)
    c = CHUNK // 2
    # Gravel paths in a cross with a round plaza in the middle
    for x0, y0, w, h in ((INNER, c - 12, n, 24), (c - 12, INNER, 24, n)):
        rect(d, x0, y0, w, h, PATH_EDGE)
        rect(d, x0 + (0 if w == n else 1), y0 + (1 if w == n else 0), w - (0 if w == n else 2), h - (2 if w == n else 0), PATH)
    d.ellipse([c - 42, c - 42, c + 42, c + 42], fill=PATH_EDGE)
    d.ellipse([c - 40, c - 40, c + 40, c + 40], fill=PATH)
    speckle(img, INNER, INNER, n, n, [GRAVEL[1], PATH_EDGE], 0.05, rng, mask=PATH)
    # Fountain
    d.ellipse([c - 15, c - 13, c + 15, c + 13], fill=OUT)
    d.ellipse([c - 14, c - 12, c + 14, c + 12], fill=STONE[0])
    d.ellipse([c - 11, c - 9, c + 11, c + 9], fill=WATER[1])
    d.ellipse([c - 3, c - 5, c + 3, c + 1], fill=hexc("#C8D8F0"))
    # Pond with koi in the upper right quadrant
    px_, py_ = c + 96, c - 96
    d.ellipse([px_ - 56, py_ - 38, px_ + 56, py_ + 38], fill=OUT)
    d.ellipse([px_ - 55, py_ - 37, px_ + 55, py_ + 37], fill=STONE[2])
    d.ellipse([px_ - 51, py_ - 33, px_ + 51, py_ + 33], fill=WATER[0])
    d.ellipse([px_ - 44, py_ - 28, px_ + 40, py_ + 20], fill=WATER[1])
    for _ in range(6):
        kx, ky = px_ + rng.randint(-35, 35), py_ + rng.randint(-20, 18)
        rect(d, kx, ky, 4, 2, rng.choice((hexc("#FF8A3D"), hexc("#F1ECF2"), hexc("#E0463C"))))
    for _ in range(8):
        lx, ly = px_ + rng.randint(-40, 40), py_ + rng.randint(-24, 22)
        d.ellipse([lx, ly, lx + 4, ly + 3], fill=TREE_GREEN[1])
    # Benches and lamps along the paths
    for off in (-120, -70, 70, 120):
        bench(img, c + off - 7, c - 18, True)
        bench(img, c + 16, c + off - 7, False)
    for off in (-100, 100):
        lamp(img, c + off, c + 20, hexc("#3E4E42"))
        lamp(img, c - 20, c + off, hexc("#3E4E42"))
    scatter_trees(img, rng, blocked=[(c - 30, INNER, 60, n), (INNER, c - 30, n, 60),
                                     (px_ - 64, py_ - 46, 128, 92)], count=34, sakura=0.5,
                  shadow=GRASS[2])
    return img


def scatter_trees(img, rng, blocked, count, sakura, shadow, area=None):
    x0, y0, w, h = area or (INNER + 12, INNER + 24, CHUNK - 2 * INNER - 24, CHUNK - 2 * INNER - 30)
    spots = []
    for _ in range(count * 30):
        if len(spots) >= count:
            break
        x, y = rng.randint(x0, x0 + w), rng.randint(y0, y0 + h)
        if any(bx - 10 <= x <= bx + bw + 10 and by - 4 <= y <= by + bh + 22 for bx, by, bw, bh in blocked):
            continue
        if all((x - sx) ** 2 + (y - sy) ** 2 > 22 ** 2 for sx, sy in spots):
            spots.append((x, y))
    for x, y in sorted(spots, key=lambda p: p[1]):
        paste_tree(img, rng, x, y, rng.random() < sakura, shadow)


TEMPLE_BOX = (168, 100, 176, 112)   # x, y (top-down), w, h of the temple in shrine chunks
TOWER_BOX = (200, 136, 112, 240)    # Tokyo Tower in plaza chunks (centered in the block)


def ground_shrine(rng):
    img = new(CHUNK, CHUNK)
    pavement_base(img)
    n = CHUNK - 2 * INNER
    d = ImageDraw.Draw(img)
    rect(d, INNER, INNER, n, n, GRAVEL[0])
    speckle(img, INNER, INNER, n, n, GRAVEL[1:], 0.2, rng)
    c = CHUNK // 2
    # Raked-gravel lines around the hall
    tx, ty, tw, th = TEMPLE_BOX
    for k in range(3):
        d.rectangle([tx - 10 - 4 * k, ty - 8 - 4 * k, tx + tw + 9 + 4 * k, ty + th + 5 + 4 * k], outline=GRAVEL[2])
    # Stone path from the street to the hall
    rect(d, c - 17, ty + th, 34, CHUNK - INNER - ty - th, OUT)
    rect(d, c - 16, ty + th, 32, CHUNK - INNER - ty - th, STONE[0])
    for y in range(ty + th + 2, CHUNK - INNER, 8):
        rect(d, c - 16, y, 32, 1, STONE[2])
        rect(d, c + (-8 if (y // 8) % 2 else 6), y, 1, 8, STONE[2])
    for y in (ty + th + 40, ty + th + 90, ty + th + 140):
        stone_lantern(img, c - 30, y)
        stone_lantern(img, c + 30, y)
    # Torii gate near the entrance
    gy = CHUNK - INNER - 44
    red, black = hexc("#C8412A"), hexc("#221E2A")
    for gx in (c - 26, c + 22):
        boxed(d, gx, gy, 5, 36, red)
        rect(d, gx, gy, 1, 36, shade(red, 0.3))
        boxed(d, gx - 1, gy + 34, 7, 3, black)
    boxed(d, c - 32, gy + 6, 64, 4, red)          # nuki (lower beam)
    boxed(d, c - 38, gy - 6, 76, 5, black)        # kasagi (top beam)
    rect(d, c - 38, gy - 6, 76, 1, hexc("#3E3848"))
    boxed(d, c - 34, gy - 1, 68, 3, red)
    img.putpixel((c - 39, gy - 7), OUT)
    img.putpixel((c + 38, gy - 7), OUT)
    boxed(d, c - 3, gy - 1, 6, 7, black)          # name plaque
    scatter_trees(img, rng, blocked=[(tx - 24, ty - 24, tw + 48, th + 40), (c - 50, ty + th, 100, CHUNK)],
                  count=26, sakura=0.3, shadow=GRAVEL[2])
    return img


def ground_plaza(rng):
    img = new(CHUNK, CHUNK)
    pavement_base(img)
    n = CHUNK - 2 * INNER
    fill_pattern(img, INNER, INNER, n, n, PAVE, PAVE_JOINT, 16)
    d = ImageDraw.Draw(img)
    c = CHUNK // 2
    tx, ty, tw, th = TOWER_BOX
    base_y = ty + th - 20
    for r, col in ((150, hexc("#34313F")), (146, PAVE), (120, hexc("#34313F")), (116, PAVE)):
        d.ellipse([c - r, base_y - r * 0.6, c + r, base_y + r * 0.6], outline=col, width=2)
    # Flower beds in the corners
    for bx, by in ((INNER + 10, INNER + 10), (CHUNK - INNER - 70, INNER + 10),
                   (INNER + 10, CHUNK - INNER - 50), (CHUNK - INNER - 70, CHUNK - INNER - 50)):
        boxed(d, bx, by, 60, 40, GRASS[0])
        speckle(img, bx, by, 60, 40, [SAKURA[1], hexc("#FFE066"), hexc("#E0463C"), GRASS[1]], 0.15, rng)
    for x, y in ((INNER + 20, c), (CHUNK - INNER - 20, c), (c - 90, CHUNK - INNER - 16), (c + 90, CHUNK - INNER - 16)):
        lamp(img, x, y, LAMP_POOL)
    for x in (INNER + 24, CHUNK - INNER - 24):
        for y in range(INNER + 80, CHUNK - INNER - 60, 44):
            paste_tree(img, rng, x, y, True, PAVE_JOINT)
    return img


# ---------------------------------------------------------------- decor lots (never smashed)

def decor_parking(rng):
    w = h = 112
    img = new(w, h)
    d = ImageDraw.Draw(img)
    rect(d, 0, 0, w, h, CURB)
    rect(d, 1, 1, w - 2, h - 2, ASPHALT)
    speckle(img, 1, 1, w - 2, h - 2, [ASPHALT_DARK, ASPHALT_LIGHT], 0.05, rng)
    for row_y in (4, 64):
        for k in range(8):
            x = 4 + k * 13
            rect(d, x, row_y, 1, 24, ROAD_LINE)
            if k < 7 and rng.random() < 0.6:
                car(d, rng, x + 1, row_y + 2, True)
        rect(d, 4, row_y + (24 if row_y == 4 else -1), 92, 1, ROAD_LINE)
    rect(d, 40, 50, 30, 1, ROAD_LINE)
    return img


def decor_garden(rng):
    w = h = 112
    img = new(w, h)
    d = ImageDraw.Draw(img)
    rect(d, 0, 0, w, h, STONE[2])
    grass(img, 2, 2, w - 4, h - 4, rng)
    rect(d, 48, 2, 16, h - 4, PATH)
    bench(img, 30, 60, False)
    bench(img, 70, 40, False)
    lamp(img, 56, 90, hexc("#3E4E42"))
    for x, y in ((20, 30), (20, 80), (92, 26), (92, 78), (30, 104), (84, 104)):
        paste_tree(img, rng, x, y, rng.random() < 0.5, GRASS[2])
    return img


def decor_vending(rng):
    w = h = 52
    img = new(w, h)
    fill_pattern(img, 0, 0, w, h, PAVE, PAVE_JOINT, 8)
    d = ImageDraw.Draw(img)
    rect(d, 0, 0, w, h, PAVE)
    d.ellipse([2, 14, w - 3, h - 6], fill=LAMP_POOL)
    for i, body in enumerate((hexc("#D8D4DE"), hexc("#C83A3A"), hexc("#3A6ED8"), hexc("#E8E4EC"))):
        vending_machine(d, 5 + i * 11, 12, body)
    bench(img, 18, 36, True)
    return img


# ---------------------------------------------------------------- catalogue

def buildings():
    """(name, size class, intact image, rubble image). Sizes: big 112x112, tall 52x112, wide 112x52,
    small 52x52, stall 44x44, plus the shrine temple and Tokyo Tower."""
    out = []
    styles = {
        "big": [(office, 3), (apartment, 2), (neon_building, 2)],
        "tall": [(office, 2), (apartment, 2), (neon_building, 2)],
        "wide": [(konbini, 2), (shop_row, 2), (office, 1)],
        "small": [(house, 3), (small_shop, 3)],
        "stall": [(small_shop, 4)],  # tiny yokocho stalls lining backstreet alleys
    }
    dims = {"big": (112, 112), "tall": (52, 112), "wide": (112, 52), "small": (52, 52), "stall": (44, 44)}
    seed = 1000
    for size, entries in styles.items():
        for fn, count in entries:
            for k in range(count):
                seed += 1
                rng = random.Random(seed)
                img, colors = fn(*dims[size], rng)
                out.append((f"{fn.__name__}_{size}_{k}", size, img, rubble(img.size, colors, rng)))
    rng = random.Random(77)
    img, colors = temple(rng)
    out.append(("temple", "temple", img, rubble(img.size, colors, rng)))
    img, colors = tokyo_tower(rng)
    out.append(("tokyo_tower", "tower", img, rubble(img.size, colors, rng, pile_height=90)))
    return out


# ---------------------------------------------------------------- atlas packing

def extrude(atlas, x, y, img):
    """Repeat the edge pixels of an opaque tile into its padding, so filtering never samples the gap."""
    w, h = img.size
    atlas.paste(img.resize((w + 2 * PAD, h + 2 * PAD), Image.Resampling.NEAREST), (x - PAD, y - PAD))
    atlas.paste(img, (x, y))


def pack(items):
    """Shelf-pack (key, image, opaque) items. Returns the atlas and {key: (x, y, w, h)} in top-down pixels."""
    order = sorted(items, key=lambda it: (-it[1].height, -it[1].width))
    placed = {}
    x = y = PAD
    shelf = 0
    for key, img, _ in order:
        w, h = img.size
        if x + w + PAD > ATLAS_WIDTH:
            x, y, shelf = PAD, y + shelf + 2 * PAD, 0
        placed[key] = (x, y, w, h)
        x += w + 2 * PAD
        shelf = max(shelf, h)
    height = (y + shelf + PAD + 3) // 4 * 4
    atlas = new(ATLAS_WIDTH, height)
    for key, img, opaque in items:
        px, py, _, _ = placed[key]
        if opaque:
            extrude(atlas, px, py, img)
        else:
            atlas.paste(img, (px, py))
    return atlas, placed


def to_rect(r, atlas_height):
    x, y, w, h = r
    return {"x": x, "y": atlas_height - y - h, "width": w, "height": h}


def build():
    GAME.mkdir(parents=True, exist_ok=True)
    grounds = {
        "downtown": ground_downtown(random.Random(1)),
        "park": ground_park(random.Random(2)),
        "shrine": ground_shrine(random.Random(3)),
        "plaza": ground_plaza(random.Random(4)),
    }
    decor = {
        "parking": ("big", decor_parking(random.Random(11))),
        "garden": ("big", decor_garden(random.Random(12))),
        "vending": ("small", decor_vending(random.Random(13))),
    }
    pieces = road_pieces(random.Random(5))
    blds = buildings()
    dust = dust_frames(random.Random(21))

    opaque = lambda img: img.getextrema()[3][0] == 255
    items = [(f"ground/{k}", img, True) for k, img in grounds.items()]
    items += [(f"piece/{k}", img, opaque(img)) for k, img in pieces.items()]
    items += [(f"decor/{k}", img, True) for k, (_, img) in decor.items()]
    for name, _, intact, broken in blds:
        items += [(f"building/{name}", intact, False), (f"rubble/{name}", broken, False)]
    items += [(f"dust/{i}", img, False) for i, img in enumerate(dust)]
    atlas, placed = pack(items)
    atlas.save(GAME / "city_atlas.png")

    H = atlas.height
    data = {
        "pixelsPerUnit": PPU,
        "chunkPixels": CHUNK,
        "grounds": [{"name": k, "rect": to_rect(placed[f"ground/{k}"], H)} for k in grounds],
        "pieces": [{"name": k, "rect": to_rect(placed[f"piece/{k}"], H)} for k in pieces],
        "decor": [{"name": k, "size": s, "rect": to_rect(placed[f"decor/{k}"], H)} for k, (s, _) in decor.items()],
        "buildings": [{"name": n, "size": s, "intact": to_rect(placed[f"building/{n}"], H),
                       "rubble": to_rect(placed[f"rubble/{n}"], H)} for n, s, _, _ in blds],
        "dust": [to_rect(placed[f"dust/{i}"], H) for i in range(len(dust))],
    }
    (GAME / "city_atlas.json").write_text(json.dumps(data, indent=1))
    print(f"atlas {atlas.width}x{atlas.height}, {len(blds)} buildings, {len(items)} sprites")


if __name__ == "__main__":
    build()
