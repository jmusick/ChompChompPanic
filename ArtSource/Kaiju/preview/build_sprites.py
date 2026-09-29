"""Pixel-grid cleanup, shared-palette conversion, and strict asset validation.

Usage: python build_sprites.py <generated-atlas.png>
Sprites are written to Assets/Art/Kaiju; preview and validation stay in this folder. Requires Pillow and numpy.
"""
from pathlib import Path
import json
import sys
from collections import deque
import numpy as np
from PIL import Image

PREVIEW = Path(__file__).resolve().parent
ROOT = PREVIEW.parents[2] / 'Assets' / 'Art' / 'Kaiju'  # game-ready sprites live in the Unity project
HEX = [
    '#10152D', '#203353', '#075275', '#066C96', '#0788B2',
    '#159FC5', '#39B4D7', '#65CBE5', '#99E0EF', '#C2EFF7',
    '#F1FCFF', '#241534', '#402050', '#643473', '#9251A1',
    '#C37CD0', '#E8ACEA', '#80B8DB', '#B2D7ED',
]
PAL = np.array([tuple(bytes.fromhex(h[1:])) for h in HEX], dtype=np.uint8)
OUTLINE = PAL[0]
NEAREST = Image.Resampling.NEAREST
COUNTS = {'idle': 4, 'walk': 6, 'chomp': 4, 'death': 6}


def keep_components(mask, minimum):
    result = np.zeros_like(mask)
    seen = np.zeros_like(mask)
    height, width = mask.shape
    for y, x in zip(*np.where(mask)):
        if seen[y, x]:
            continue
        points = []
        queue = deque([(y, x)])
        seen[y, x] = True
        while queue:
            cy, cx = queue.popleft()
            points.append((cy, cx))
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                ny, nx = cy + dy, cx + dx
                if 0 <= ny < height and 0 <= nx < width and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    queue.append((ny, nx))
        if len(points) >= minimum:
            for py, px in points:
                result[py, px] = True
    return result


def clean_cell(source, row, col, size):
    cell = source.crop((col * 256, row * 256, (col + 1) * 256, (row + 1) * 256))
    a = np.array(cell)
    mask = keep_components(a[:, :, 3] >= 128, 24)
    a[:, :, 3] = mask * 255
    a[~mask] = 0
    cropped = Image.fromarray(a).crop(Image.fromarray(mask).getbbox())
    pix = np.array(cropped.resize(size, NEAREST))
    opaque = pix[:, :, 3] >= 128
    opaque = keep_components(opaque, 2)
    rgb = pix[:, :, :3].astype(np.int32)
    distance = ((rgb[:, :, None, :] - PAL.astype(np.int32)) ** 2).sum(axis=-1)
    indices = distance.argmin(axis=-1)
    # Purple belongs to the mouth. Reject isolated purple fringe pixels from
    # the generated art around white spikes and eyes, using the cool ramp.
    purple = (indices >= 11) & (indices <= 16) & opaque
    mouth = keep_components(purple, 12)
    cool = np.array(list(range(11)) + [17, 18])
    fringe = purple & ~mouth
    indices[fringe] = cool[distance[fringe][:, cool].argmin(axis=-1)]
    pix[:, :, :3] = PAL[indices]
    # Make the outermost silhouette exactly one grid pixel of dark ink.
    inside = np.pad(opaque, 1)
    interior = opaque & inside[:-2, 1:-1] & inside[2:, 1:-1] & inside[1:-1, :-2] & inside[1:-1, 2:]
    pix[opaque & ~interior, :3] = OUTLINE
    pix[:, :, 3] = opaque * 255
    pix[~opaque] = 0
    frame = Image.new('RGBA', (64, 64))
    frame.paste(Image.fromarray(pix), ((64 - size[0]) // 2, (64 - size[1]) // 2))
    return frame


def main():
    source = Image.open(sys.argv[1]).convert('RGBA')
    assert source.size == (1536, 1024)
    animations = {}
    for row, (name, count) in enumerate(COUNTS.items()):
        frames = []
        for col in range(count):
            if name == 'death' and col == 5:
                frames.append(Image.new('RGBA', (64, 64)))
                continue
            size = (54, 52)
            if name == 'chomp' and col == 0:
                size = (54, 54)
            if name == 'death':
                size = [(54, 52), (56, 30), (54, 24), (54, 48), (48, 36)][col]
            frames.append(clean_cell(source, row, col, size))
        animations[name] = frames
    # The final bite frame is exactly the idle rest pose, for a clean handoff.
    animations['chomp'][3] = animations['idle'][0].copy()
    for name, frames in animations.items():
        strip = Image.new('RGBA', (len(frames) * 64, 64))
        for i, frame in enumerate(frames):
            strip.paste(frame, (i * 64, 0))
        strip.save(ROOT / f'kaiju_{name}.png')
    palette = Image.new('RGB', (len(PAL), 1))
    palette.putdata([tuple(c) for c in PAL])
    palette.save(ROOT / 'kaiju_palette.png')
    preview = Image.new('RGB', (6 * 64, 4 * 64), '#12141C')
    for row, frames in enumerate(animations.values()):
        for col, frame in enumerate(frames):
            preview.paste(frame, (col * 64, row * 64), frame)
    preview.resize((3072, 2048), NEAREST).save(PREVIEW / 'kaiju_preview_8x.png')
    validate()


def validate():
    union = set()
    report = {}
    for name, count in COUNTS.items():
        path = ROOT / f'kaiju_{name}.png'
        im = Image.open(path)
        assert im.mode == 'RGBA' and im.size == (count * 64, 64)
        pixels = np.array(im)
        assert set(np.unique(pixels[:, :, 3])) <= {0, 255}
        colors = {tuple(p) for p in pixels[pixels[:, :, 3] == 255, :3]}
        union.update(colors)
        bounds = []
        for i in range(count):
            frame = im.crop((64 * i, 0, 64 * (i + 1), 64))
            box = frame.getbbox()
            bounds.append(box)
            if box:
                x0, y0, x1, y1 = box
                assert x0 >= 3 and y0 >= 3 and x1 <= 61 and y1 <= 61
                assert abs((x0 + x1) / 2 - 32) <= 0.5
                assert abs((y0 + y1) / 2 - 32) <= 0.5
                a = np.array(frame)
                mask = a[:, :, 3] == 255
                p = np.pad(mask, 1)
                inner = mask & p[:-2, 1:-1] & p[2:, 1:-1] & p[1:-1, :-2] & p[1:-1, 2:]
                assert np.all(a[mask & ~inner, :3] == OUTLINE)
        report[name] = {'frames': count, 'size': im.size, 'bounds': bounds, 'opaque_colors': len(colors)}
    assert len(union) <= 24
    assert union <= {tuple(c) for c in PAL}
    assert Image.open(ROOT / 'kaiju_palette.png').size == (len(PAL), 1)
    assert Image.open(PREVIEW / 'kaiju_preview_8x.png').size == (3072, 2048)
    preview = np.array(Image.open(PREVIEW / 'kaiju_preview_8x.png'))
    # Every enlarged logical pixel must be a perfectly uniform 8x8 block.
    assert np.array_equal(preview, np.repeat(np.repeat(preview[::8, ::8], 8, 0), 8, 1))
    assert tuple(preview[0, 0]) == (18, 20, 28)
    death = Image.open(ROOT / 'kaiju_death.png')
    assert death.crop((320, 0, 384, 64)).getbbox() is None
    assert Image.open(ROOT / 'kaiju_chomp.png').crop((192, 0, 256, 64)).tobytes() == Image.open(ROOT / 'kaiju_idle.png').crop((0, 0, 64, 64)).tobytes()
    report['total_opaque_colors'] = len(union)
    report['palette'] = HEX
    report['checks'] = 'PASS: dimensions, binary alpha, shared palette, centered bounds, margins, outline, transparent death end, neutral chomp end'
    (PREVIEW / 'validation.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    if len(sys.argv) == 2 and sys.argv[1] == '--verify':
        validate()
    else:
        main()
