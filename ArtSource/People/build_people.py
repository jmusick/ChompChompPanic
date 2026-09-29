"""Clean generated people atlases, assemble strips, and verify all deliverables.
Run: python build_people.py
Verify existing deliverables: python build_people.py --verify
Requires Pillow and numpy. Writes only to the two user-specified People folders.
"""
from pathlib import Path
from collections import deque
import json
import sys
import numpy as np
from PIL import Image

PROJECT = Path(r"C:\Users\JD\source\ChompChompPanic")
GAME = PROJECT / "Assets/Art/People"
SOURCE = PROJECT / "ArtSource/People"
KAIJU = PROJECT / "Assets/Art/Kaiju/kaiju_idle.png"
VARIANTS = ("office", "jogger", "tourist", "kid")
COUNTS = {"run": 6, "idle": 4}
HEX = [
    "#10152D", "#34324A", "#402B34", "#704337",
    "#A76348", "#CB8A60", "#EDB58C", "#FFD8AF",
    "#FFF0DA", "#D8C4A7", "#B95D32", "#EF8544",
    "#FFB666", "#593C78", "#8857A6", "#BD86D1",
    "#AE466E", "#E9719B", "#FFA9BE", "#71677C",
    "#F1FCFF", "#563B34", "#8D5C47",
]
PAL = np.array([list(bytes.fromhex(h[1:])) for h in HEX], dtype=np.uint8)
OUTLINE = PAL[0]
NEAREST = Image.Resampling.NEAREST
BASELINE = 26
HEIGHT = 22
BACKGROUND = (18, 20, 28)

def components(mask):
    seen = np.zeros_like(mask)
    height, width = mask.shape
    groups = []
    for y, x in zip(*np.where(mask)):
        if seen[y, x]:
            continue
        queue = deque([(y, x)])
        seen[y, x] = True
        points = []
        while queue:
            cy, cx = queue.popleft()
            points.append((cy, cx))
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                ny, nx = cy + dy, cx + dx
                if 0 <= ny < height and 0 <= nx < width and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    queue.append((ny, nx))
        groups.append(points)
    return groups

def main_component(mask):
    result = np.zeros_like(mask)
    group = max(components(mask), key=len)
    ys, xs = zip(*group)
    result[ys, xs] = True
    return result

def cleanup(cell, variant):
    raw = np.array(cell.convert("RGBA"))
    # Ignore generated cell dividers and isolated alpha contamination.
    raw[:4, :, 3] = 0
    raw[-4:, :, 3] = 0
    raw[:, :4, 3] = 0
    raw[:, -4:, 3] = 0
    mask = main_component(raw[:, :, 3] >= 128)
    raw[:, :, 3] = mask * 255
    raw[~mask] = 0
    clean = Image.fromarray(raw).crop(Image.fromarray(mask).getbbox())
    width = min(22, max(14, round(clean.width / clean.height * HEIGHT)))
    # Keep the head near half of the 22px silhouette instead of letting the
    # generated chibi head consume two thirds of the playable sprite.
    split = round(clean.height * (0.67 if variant == "tourist" else 0.62))
    head_height = 12
    resized = Image.new("RGBA", (width, HEIGHT))
    resized.paste(clean.crop((0, 0, clean.width, split)).resize((width, head_height), NEAREST), (0, 0))
    resized.paste(clean.crop((0, split, clean.width, clean.height)).resize((width, HEIGHT-head_height), NEAREST), (0, head_height))
    if variant == "tourist":
        # The hat adds height; preserve the original face-to-body proportion.
        resized = clean.resize((width, HEIGHT), NEAREST)
    pixel = np.array(resized)
    opaque = pixel[:, :, 3] >= 128
    # A contact shoe must have a readable two-pixel sole at this scale.
    if opaque[-1].sum() == 1:
        contact = int(np.where(opaque[-1])[0][0])
        neighbor = contact + (1 if contact < width/2 else -1)
        opaque[-1, neighbor] = True
        pixel[-1, neighbor, :3] = OUTLINE
    # The output uses one palette with direct nearest-color mapping, no dithering.
    distance = ((pixel[:, :, None, :3].astype(np.int32) - PAL.astype(np.int32)) ** 2).sum(-1)
    indices = distance.argmin(-1)
    # Distinguish the jogger's dark curls from the silhouette outline and
    # keep the deep skin tone warmer and darker than the other variants.
    if variant == "jogger":
        yy, xx = np.indices(opaque.shape)
        hair = opaque & ((yy < 6) | ((yy < 12) & (xx < width*.65))) & (indices == 0)
        indices[hair] = 1
        highlight = opaque & (yy < 7) & np.isin(indices, [13, 14, 15])
        indices[highlight] = 19
        skin = opaque & np.isin(indices, [4, 5, 6, 7])
        indices[skin] = np.where(indices[skin] >= 6, 5, 22)
    pixel[:, :, :3] = PAL[indices]
    padded = np.pad(opaque, 1)
    interior = opaque & padded[:-2, 1:-1] & padded[2:, 1:-1] & padded[1:-1, :-2] & padded[1:-1, 2:]
    pixel[opaque & ~interior, :3] = OUTLINE
    pixel[:, :, 3] = opaque * 255
    pixel[~opaque] = 0
    frame = Image.new("RGBA", (32, 32))
    frame.paste(Image.fromarray(pixel), ((32-width)//2, BASELINE+1-HEIGHT))
    return frame

def build():
    GAME.mkdir(parents=True, exist_ok=True)
    SOURCE.mkdir(parents=True, exist_ok=True)
    all_frames = {}
    for variant in VARIANTS:
        atlas = Image.open(SOURCE / ("source_" + variant + ".png")).convert("RGBA")
        for row, (animation, count) in enumerate(COUNTS.items()):
            frames = []
            for col in range(count):
                box = (round(col*atlas.width/6), round(row*atlas.height/2),
                       round((col+1)*atlas.width/6), round((row+1)*atlas.height/2))
                frames.append(cleanup(atlas.crop(box), variant))
            name = f"person_{variant}_{animation}.png"
            strip = Image.new("RGBA", (32*count, 32))
            for index, frame in enumerate(frames):
                strip.paste(frame, (index*32, 0))
            strip.save(GAME / name)
            all_frames[name] = frames
    palette = Image.new("RGB", (len(PAL), 1))
    palette.putdata([tuple(color) for color in PAL])
    palette.save(GAME / "people_palette.png")
    preview = Image.new("RGB", (192, 320), BACKGROUND)
    for row, frames in enumerate(all_frames.values()):
        for col, frame in enumerate(frames):
            preview.paste(frame, (col*32, row*32), frame)
    kaiju = Image.open(KAIJU).convert("RGBA").crop((0, 0, 64, 64))
    preview.paste(kaiju, (0, 256), kaiju)
    # All five characters share the kaiju's foot line in the size comparison.
    kaiju_foot = kaiju.getbbox()[3]-1
    for i, variant in enumerate(VARIANTS):
        frame = all_frames[f"person_{variant}_run.png"][0]
        preview.paste(frame, (64+i*32, 256+kaiju_foot-BASELINE), frame)
    preview.resize((1536, 2560), NEAREST).save(SOURCE / "people_preview_8x.png")
    validate()

def validate():
    report = {
        "status": "PASS",
        "game_folder": str(GAME),
        "source_folder": str(SOURCE),
        "frame_size": [32, 32],
        "foot_baseline_zero_based": BASELINE,
        "outline": HEX[0],
        "palette": HEX,
        "files": {},
        "preview_rows": [],
    }
    union = set()
    rgba_union = set()
    for variant in VARIANTS:
        for animation, count in COUNTS.items():
            name = f"person_{variant}_{animation}.png"
            im = Image.open(GAME / name)
            assert im.mode == "RGBA"
            assert im.size == (32*count, 32), (name, im.size)
            pixels = np.array(im)
            assert set(np.unique(pixels[:, :, 3])) <= {0, 255}, name
            colors = {tuple(c) for c in pixels[pixels[:, :, 3] == 255, :3]}
            union.update(colors)
            rgba_union.update(tuple(c) for c in pixels.reshape(-1, 4))
            assert colors <= {tuple(c) for c in PAL}
            frames = []
            for i in range(count):
                f = im.crop((32*i, 0, 32*(i+1), 32))
                bounds = f.getbbox()
                assert bounds is not None
                x0, y0, x1, y1 = bounds
                assert x0 >= 3 and y0 >= 3 and x1 <= 29 and y1 <= 29, (name, i, bounds)
                assert y1-1 == BASELINE, (name, i, bounds)
                assert 18 <= y1-y0 <= 22, (name, i, bounds)
                assert abs((x0+x1)/2-16) <= .5
                assert abs((y0+y1)/2-16) <= .5
                a = np.array(f)
                mask = a[:, :, 3] == 255
                p = np.pad(mask, 1)
                inner = mask & p[:-2, 1:-1] & p[2:, 1:-1] & p[1:-1, :-2] & p[1:-1, 2:]
                assert np.all(a[mask & ~inner, :3] == OUTLINE)
                # The lowest occupied row has shoe pixels, not an isolated effect.
                assert mask[BASELINE, :].sum() >= 2, (name, i, "shoe contact")
                ys, xs = np.where(mask)
                frames.append({"bounds": bounds, "baseline": y1-1,
                               "alpha_centroid": [round(float(xs.mean()), 2), round(float(ys.mean()), 2)]})
            distinct = len({im.crop((32*i, 0, 32*(i+1), 32)).tobytes() for i in range(count)})
            assert distinct == count, (name, "duplicate frames")
            report["files"][name] = {"frames": count, "dimensions": im.size,
                                    "opaque_colors": len(colors), "unique_frames": distinct,
                                    "frame_checks": frames}
            report["preview_rows"].append(name)
    assert len(union) <= 24
    assert len(rgba_union) <= 24  # Also pass the stricter count including transparency.
    palette = Image.open(GAME / "people_palette.png")
    assert palette.size == (len(PAL), 1)
    assert np.array_equal(np.array(palette)[0], PAL)
    preview = np.array(Image.open(SOURCE / "people_preview_8x.png").convert("RGB"))
    assert preview.shape == (2560, 1536, 3)
    base = preview[::8, ::8]
    assert np.array_equal(preview, np.repeat(np.repeat(base, 8, 0), 8, 1))
    assert tuple(base[0, 0]) == BACKGROUND
    for row, name in enumerate(report["preview_rows"]):
        strip = Image.open(GAME / name)
        composed = Image.new("RGB", (strip.width, 32), BACKGROUND)
        composed.paste(strip, (0, 0), strip)
        assert np.array_equal(base[row*32:(row+1)*32, :strip.width], np.array(composed))
    report["preview_rows"].append("64x64 kaiju idle frame + office, jogger, tourist, kid at identical 8x scale")
    report["total_opaque_colors"] = len(union)
    report["total_colors_including_transparency"] = len(rgba_union)
    report["preview_dimensions"] = [1536, 2560]
    report["checks"] = ["exact strip dimensions", "binary alpha", "shared palette <=24 including transparency",
                        "three pixel margins", "18-22px character height", "centered bounds",
                        "foot baseline row 26", "dark one-pixel perimeter", "all frames unique",
                        "nearest-neighbor 8x preview", "preview matches game strips"]
    (SOURCE / "validation.json").write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"status": "PASS", "files": len(report["files"]),
                      "opaque_colors": len(union), "colors_including_transparency": len(rgba_union),
                      "checks": report["checks"]}, indent=2))

if __name__ == "__main__":
    if "--verify" in sys.argv:
        validate()
    else:
        build()

