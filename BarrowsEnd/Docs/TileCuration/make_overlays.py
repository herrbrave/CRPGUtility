"""Renders colour-coded review overlays of the tile semantics.

Reads BarrowsEnd/Assets/Content/Tiles/TileSemantics.json (the single source of truth for what each Kenney tile means) and the copied source sheets in
BarrowsEnd/Assets/Art/Source, and writes one PNG per sheet next to this script: every sprite cell tinted by its semantic (green = Ground, blue =
Blocked, red = Wall, orange = Prop, yellow = Decor, magenta = Door) with column numbers along the top and row numbers down the left. Cells NO rule
covers are drawn dimmed with no tint: they have no tile and cannot be painted. Edit the JSON, re-run this, look again.

Usage:  python make_overlays.py [scale]        (default scale 3; needs Pillow)
Optional crop for a closer look at one part of a sheet (writes <key>_crop.png):
        python make_overlays.py 4 town 19 37 0 15      (sheet key, colMin, colMax, rowMin, rowMax)
"""
import json
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
JSON_PATH = os.path.join(ROOT, 'Assets', 'Content', 'Tiles', 'TileSemantics.json')
PITCH = 17  # the Kenney sheets are 16px sprites on a 17px pitch

COLORS = {
    'Ground': (60, 200, 80),
    'Blocked': (70, 120, 240),
    'Wall': (230, 60, 60),
    'Prop': (240, 150, 40),
    'Decor': (240, 220, 50),
    'Door': (230, 70, 220),
}


def resolve(rules, col, row):
    semantic = None
    for rule in rules:
        if rule['cols'][0] <= col <= rule['cols'][1] and rule['rows'][0] <= row <= rule['rows'][1]:
            semantic = rule['semantic']
    return semantic


def render(sheet, scale, crop=None):
    image = Image.open(os.path.join(ROOT, sheet['path'])).convert('RGBA')
    cols, rows = image.width // PITCH, image.height // PITCH
    c0, c1, r0, r1 = crop if crop else (0, cols - 1, 0, rows - 1)
    cell = PITCH * scale
    left, top, legend = 44, 30, 34
    canvas = Image.new('RGBA', (left + (c1 - c0 + 1) * cell, legend + top + (r1 - r0 + 1) * cell), (40, 46, 40, 255))
    draw = ImageDraw.Draw(canvas)

    x = 6
    for name, color in COLORS.items():
        draw.rectangle([x, 8, x + 16, 24], fill=color)
        draw.text((x + 22, 10), name, fill=(255, 255, 255))
        x += 110
    draw.rectangle([x, 8, x + 16, 24], outline=(160, 160, 160))
    draw.text((x + 22, 10), 'no tile', fill=(200, 200, 200))

    for r in range(r0, r1 + 1):
        for c in range(c0, c1 + 1):
            px, py = left + (c - c0) * cell, legend + top + (r - r0) * cell
            tile = image.crop((c * PITCH, r * PITCH, c * PITCH + 16, r * PITCH + 16)).resize((16 * scale, 16 * scale), Image.NEAREST)
            semantic = resolve(sheet['rules'], c, r)
            base = Image.new('RGBA', tile.size, (60, 90, 60, 255))
            base.alpha_composite(tile)
            if semantic:
                tint = Image.new('RGBA', tile.size, COLORS[semantic] + (95,))
                base.alpha_composite(tint)
            else:
                base = Image.blend(base, Image.new('RGBA', tile.size, (30, 34, 30, 255)), 0.6)
            canvas.alpha_composite(base, (px, py))
            draw.rectangle([px - 1, py - 1, px + 16 * scale, py + 16 * scale], outline=(255, 255, 255, 40))

    for c in range(c0, c1 + 1):
        draw.text((left + (c - c0) * cell + 4, legend + 8), str(c), fill=(255, 255, 0))
    for r in range(r0, r1 + 1):
        draw.text((6, legend + top + (r - r0) * cell + cell // 3), str(r), fill=(255, 255, 0))
    return canvas


def main():
    scale = int(sys.argv[1]) if len(sys.argv) > 1 else 3
    data = json.load(open(JSON_PATH))
    if len(sys.argv) >= 7:
        key = sys.argv[2]
        crop = tuple(int(v) for v in sys.argv[3:7])
        sheet = next(s for s in data['sheets'] if s['key'] == key)
        out = os.path.join(HERE, f'{key}_crop.png')
        render(sheet, scale, crop).convert('RGB').save(out)
        print(out)
        return

    for sheet in data['sheets']:
        out = os.path.join(HERE, f"{sheet['key']}_semantics.png")
        render(sheet, scale).convert('RGB').save(out)
        print(out)


if __name__ == '__main__':
    main()
