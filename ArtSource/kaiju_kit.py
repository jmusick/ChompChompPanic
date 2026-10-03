"""Shared pixel-art helpers for the hand-drawn kaiju scripts (ArtSource/Mecha, Octo and Moth).

Shapes are aliased boolean masks on a 64 x 64 frame. `plate` gives a mask flat cel shading lit from the top
left plus a one-pixel navy outline, so a character is built by painting its parts back to front.
`write_clips` saves the strips, the sprite_frames.json manifest for Chomp Chomp Panic > Import Art and an
8x preview. Requires Pillow and numpy.
"""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw

SIZE = 64
ART = Path(__file__).resolve().parents[1] / "Assets" / "Art"


def rgb(h):
    return tuple(bytes.fromhex(h[1:]))


def ramp(*hexes):
    return [rgb(h) for h in hexes]


OUTLINE = rgb("#10152D")


class Frame:
    def __init__(self):
        self.px = np.zeros((SIZE, SIZE, 4), np.uint8)

    def put(self, x, y, color):
        if 0 <= x < SIZE and 0 <= y < SIZE:
            self.px[y, x] = color + (255,)

    def fill(self, mask, color):
        self.px[mask] = color + (255,)

    def opaque(self):
        return self.px[:, :, 3] > 0

    def image(self):
        return Image.fromarray(self.px, "RGBA")


def mask_of(draw):
    """A boolean mask from a PIL drawing callback (aliased, so edges stay on the pixel grid)."""
    img = Image.new("1", (SIZE, SIZE), 0)
    draw(ImageDraw.Draw(img))
    return np.array(img, bool)


def poly(points):
    return mask_of(lambda d: d.polygon([(round(x), round(y)) for x, y in points], fill=1))


def ellipse(cx, cy, rx, ry):
    return mask_of(lambda d: d.ellipse((round(cx - rx), round(cy - ry), round(cx + rx), round(cy + ry)), fill=1))


def rect(x0, y0, x1, y1):
    return mask_of(lambda d: d.rectangle((round(x0), round(y0), round(x1), round(y1)), fill=1))


def chamfer(x0, y0, x1, y1, c=2):
    """A rectangle with its corners cut off: the basic armour plate."""
    return poly([(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c), (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)])


def blobs(points):
    """Union of circles (x, y, r): tentacles, limbs and other tapering curves."""
    m = np.zeros((SIZE, SIZE), bool)
    for x, y, r in points:
        m |= ellipse(x, y, r, r)
    return m


def shifted(mask, dx, dy):
    """mask value at (x + dx, y + dy), False outside the frame."""
    out = np.zeros_like(mask)
    h, w = mask.shape
    ys = slice(max(0, -dy), min(h, h - dy))
    xs = slice(max(0, -dx), min(w, w - dx))
    yd = slice(max(0, dy), min(h, h + dy))
    xd = slice(max(0, dx), min(w, w + dx))
    out[ys, xs] = mask[yd, xd]
    return out


def edge(mask):
    return mask & ~(shifted(mask, 1, 0) & shifted(mask, -1, 0) & shifted(mask, 0, 1) & shifted(mask, 0, -1))


def plate(frame, mask, colors, outline=True, light=1):
    """Flat cel shading lit from the top left: light rim on top, shadow underneath and on the right.
    `colors` is a five-step ramp, darkest first; light=2 adds a highlight in the top-left corner."""
    if not mask.any():
        return
    ys = np.where(mask.any(axis=1))[0]
    top, bottom = ys[0], ys[-1]
    rows = np.arange(SIZE)[:, None] * np.ones((1, SIZE), int)
    color = np.full(mask.shape, 2)
    color[(rows > top + (bottom - top) * 0.62) & mask] = 1
    color[mask & ~shifted(mask, 2, 0)] = 1
    color[mask & ~shifted(mask, 0, 2)] = 0
    color[mask & (~shifted(mask, 0, -2) | ~shifted(mask, -2, 0))] = 3
    if light > 1:
        color[mask & ~shifted(mask, 0, -2) & ~shifted(mask, -3, 0)] = 4
    for i in range(5):
        frame.fill(mask & (color == i), colors[i])
    if outline:
        frame.fill(edge(mask), OUTLINE)


def line(frame, x0, y0, x1, y1, color):
    steps = max(abs(x1 - x0), abs(y1 - y0), 1)
    for i in range(steps + 1):
        frame.put(round(x0 + (x1 - x0) * i / steps), round(y0 + (y1 - y0) * i / steps), color)


def puff(frame, cx, cy, r, colors):
    """A round cloud (smoke, dust, ink) shaded with colors[1] and colors[2]."""
    m = ellipse(cx, cy, r, r)
    frame.fill(m, colors[1])
    frame.fill(ellipse(cx - r * 0.3, cy - r * 0.3, r * 0.55, r * 0.55) & m, colors[2])
    frame.fill(edge(m), OUTLINE)


def write_clips(prefix, folder, clips, preview_dir):
    """Save <prefix>_<clip>.png strips under Assets/Art/<folder>, its sprite_frames.json and a preview."""
    out = ART / folder
    out.mkdir(parents=True, exist_ok=True)
    manifest = {}
    rows = []
    for clip, make in clips.items():
        frames = make()
        strip = Image.new("RGBA", (SIZE * len(frames), SIZE))
        for i, f in enumerate(frames):
            strip.paste(f.image(), (i * SIZE, 0))
        name = f"{prefix}_{clip}.png"
        strip.save(out / name)
        manifest[name] = [SIZE, SIZE]
        rows.append(strip)
    (out / "sprite_frames.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    width = max(r.width for r in rows)
    preview = Image.new("RGB", (width, SIZE * len(rows)), (17, 19, 31))
    for i, r in enumerate(rows):
        preview.paste(r, (0, i * SIZE), r)
    preview.resize((width * 8, preview.height * 8), Image.Resampling.NEAREST).save(Path(preview_dir) / f"{prefix}_preview_8x.png")
    print("wrote", len(manifest), "strips to", out)
