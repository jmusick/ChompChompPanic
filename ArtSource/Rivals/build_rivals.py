"""Recolor the player kaiju into rival kaiju for Chomp Chomp Panic!

Run: python build_rivals.py
Reads Assets/Art/Kaiju/kaiju_{idle,walk,chomp}.png and writes Assets/Art/Rivals/rival_<variant>_<clip>.png
(64 x 64 frames, same layout and import settings as the kaiju) plus sprite_frames.json and
ArtSource/Rivals/rivals_preview_4x.png. The kaiju's blue body ramp is swapped color for color,
so shading and outlines stay identical. Requires Pillow. Output is deterministic.
"""
from pathlib import Path
import json
from PIL import Image

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
KAIJU = PROJECT / "Assets" / "Art" / "Kaiju"
GAME = PROJECT / "Assets" / "Art" / "Rivals"
CLIPS = ("idle", "walk", "chomp")

# The kaiju's blue ramp, darkest to lightest (belly and spine tones last).
BLUE = ["#203353", "#075275", "#066C96", "#0788B2", "#159FC5", "#39B4D7", "#65CBE5", "#99E0EF", "#C2EFF7",
        "#80B8DB", "#B2D7ED"]
# Mouth purples.
PURPLE = ["#241534", "#402050", "#643473", "#9251A1", "#C37CD0", "#E8ACEA"]

VARIANTS = {
    # Crimson: red hide, orange belly.
    "crimson": (["#3A1426", "#6E1024", "#8E2434", "#B0283A", "#D0393F", "#EE5A4E", "#F4826A", "#FFB08E", "#FFD4BE",
                 "#E08A7A", "#F4BCA8"], PURPLE),
    # Toxic: acid green hide with a sickly yellow-green belly and a dark red mouth.
    "toxic": (["#1E3322", "#1F5230", "#2A6E36", "#3A8A3C", "#56A544", "#7CC04E", "#A4D86A", "#CBEB94", "#E6F7C2",
               "#9CC48A", "#C4E0B0"], ["#2A0F16", "#4A1420", "#6E1024", "#A0243A", "#D0474F", "#F08A8A"]),
}


def rgb(h):
    return tuple(bytes.fromhex(h[1:]))


def build():
    GAME.mkdir(parents=True, exist_ok=True)
    manifest = {}
    rows = []
    for variant, (body, mouth) in VARIANTS.items():
        swap = {rgb(a): rgb(b) for a, b in zip(BLUE + PURPLE, body + mouth)}
        for clip in CLIPS:
            src = Image.open(KAIJU / f"kaiju_{clip}.png").convert("RGBA")
            px = src.load()
            for y in range(src.height):
                for x in range(src.width):
                    r, g, b, a = px[x, y]
                    if a and (r, g, b) in swap:
                        px[x, y] = swap[(r, g, b)] + (a,)
            name = f"rival_{variant}_{clip}.png"
            src.save(GAME / name)
            manifest[name] = [64, 64]
            rows.append(src)
    (GAME / "sprite_frames.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    width = max(r.width for r in rows)
    preview = Image.new("RGB", (width, 64 * len(rows)), (36, 34, 46))
    for i, r in enumerate(rows):
        preview.paste(r, (0, i * 64), r)
    preview.resize((width * 4, preview.height * 4), Image.Resampling.NEAREST).save(HERE / "rivals_preview_4x.png")
    print("wrote", len(manifest), "strips to", GAME)


if __name__ == "__main__":
    build()
