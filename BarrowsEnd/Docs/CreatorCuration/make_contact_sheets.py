"""Renders the character-creator curation sheets (Phase 4, Task 4.0).

Reads the copied Kenney character sheet in BarrowsEnd/Assets/Art/Source and writes, next to this script:

  skin.png, hair.png, outfit.png, weapon.png   one grid per creator slot; every option composited over a fixed reference character so it is judged
                                               as worn. Column numbers run along the top, row numbers down the left, so a pick is "hair, col 21, row 3".
  cells.csv                                    slot,option_id,col,row for every non-empty cell shown (the option ids the catalogs use)
  assembled.png                                the five class kits (generated armor + weapon) in every hair colour, built from CreatorPicks.json and
                                               CreatorClassTable.json: judge the looks before they are baked into items

Structure of the sheet (measured, see Docs/Plans/2D-Campaign-Phase4-CharacterCreator.md): 16px cells on a 17px pitch, margin 0.
  body  cols 0-1 x rows 0-3   4 skin tones (col 1 only adds a mouth pixel)
  hair  cols 19-26            shapes repeat with row mod 4: rows 0-3 / 4-7 / 8-11 are three colour bands, and row 3 of each band is a BEARD
  cloth cols 6-17             shapes repeat with row mod 5: rows 0-4 and 5-9 are two colourways, rows 10-11 are empty
  weapon cols 42-53           heterogeneous; judge by eye

Layers share one 16x16 canvas and pivot, so a straight alpha composite in draw order is exactly what the world draws. Draw order copies
SpriteLayerCompositor.DefaultSlots(): body, feet, clothes, hair, helmet, shield, weapon.

Usage:  python make_contact_sheets.py [scale]        (default 4; needs Pillow)
"""
import csv
import json
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
SHEET = os.path.join(ROOT, 'Assets', 'Art', 'Source', 'roguelikeChar_transparent.png')
PICKS = os.path.join(ROOT, 'Assets', 'Content', 'Creator', 'CreatorPicks.json')
CELL, PITCH = 16, 17

# (slot key, catalog short name, col range, row range, reference layers drawn UNDER the option, description)
BODY_REF = ('body', 0, 0)
CLOTHES_REF = ('clothes', 6, 0)


def load_sheet():
    return Image.open(SHEET).convert('RGBA')


def cell(sheet, col, row):
    x, y = col * PITCH, row * PITCH
    return sheet.crop((x, y, x + CELL, y + CELL))


def is_empty(img):
    return img.getchannel('A').getbbox() is None


def composite(sheet, layers):
    """layers: iterable of (col, row) drawn in order over a transparent 16x16 canvas."""
    canvas = Image.new('RGBA', (CELL, CELL), (0, 0, 0, 0))
    for col, row in layers:
        canvas = Image.alpha_composite(canvas, cell(sheet, col, row))
    return canvas


def option_of(option_id):
    """'hair-kenney-21-3' -> ('hair', 'kenney', 21, 3)."""
    slot, sheet, col, row = option_id.split('-')
    return slot, sheet, int(col), int(row)


SHEETS = [
    # name, catalog prefix, cols, rows, reference cells under the option, mark row predicate
    ('skin', 'body', range(0, 2), range(0, 4), [], None),
    ('hair', 'hair', range(19, 27), range(0, 12), [(0, 0), (6, 0)], lambda c, r: r % 4 == 3),
    ('outfit', 'clothes', range(6, 18), range(0, 10), [(0, 0)], None),
    ('weapon', 'weapon', range(42, 54), range(0, 12), [(0, 0)], None),
]


def render_grid(sheet, name, prefix, cols, rows, under, mark, scale, csv_rows):
    cell_px = CELL * scale
    pad = 6
    left, top = 44, 30
    step = cell_px + pad
    width = left + len(cols) * step + pad
    height = top + len(rows) * step + pad
    canvas = Image.new('RGBA', (width, height), (52, 58, 52, 255))
    draw = ImageDraw.Draw(canvas)

    for i, col in enumerate(cols):
        draw.text((left + i * step + cell_px // 2 - 6, 8), str(col), fill=(240, 240, 240, 255))
    for j, row in enumerate(rows):
        draw.text((10, top + j * step + cell_px // 2 - 5), str(row), fill=(240, 240, 240, 255))

    for j, row in enumerate(rows):
        for i, col in enumerate(cols):
            option = cell(sheet, col, row)
            if is_empty(option):
                continue
            x, y = left + i * step, top + j * step
            bg = (74, 82, 74, 255)
            if mark is not None and mark(col, row):
                bg = (110, 60, 60, 255)  # beard rows on the hair sheet
            draw.rectangle((x - 2, y - 2, x + cell_px + 1, y + cell_px + 1), fill=bg)
            layers = list(under) + [(col, row)] if prefix != 'body' else [(col, row)]
            img = composite(sheet, layers).resize((cell_px, cell_px), Image.NEAREST)
            canvas.alpha_composite(img, (x, y))
            csv_rows.append((name, f'{prefix}-kenney-{col}-{row}', col, row))

    if mark is not None:
        draw.text((left, height - 16), 'red cells = facial hair (beards)', fill=(240, 200, 200, 255))
    out = os.path.join(HERE, f'{name}.png')
    canvas.save(out)
    print(f'wrote {out}')


def render_assembled(sheet, scale):
    """The five class kits: one row per class, one column per hair colour, so the veto sees every class in its armor (with the generated trousers and boots) and weapon."""
    table_path = os.path.join(os.path.dirname(PICKS), 'CreatorClassTable.json')
    armor_path = os.path.join(ROOT, 'Assets', 'Art', 'Source', 'barrowsend_armor_transparent.png')
    if not (os.path.exists(PICKS) and os.path.exists(table_path) and os.path.exists(armor_path)):
        print('CreatorPicks.json, CreatorClassTable.json or the armor sheet is missing: skipping assembled.png')
        return
    with open(PICKS, encoding='utf-8') as f:
        picks = json.load(f)
    with open(table_path, encoding='utf-8') as f:
        table = json.load(f)
    armor_sheet = Image.open(armor_path).convert('RGBA')
    skins = picks['skin']
    colours = picks['hairColors']
    items = {p['item']: p for p in picks['armors'] + picks['weapons']}

    def item_cell(item_id):
        """(sheet image, col, row) of an item's sprite: the class armors live on the generated sheet, weapons on the Kenney one."""
        _, sheet_key, col, row = option_of(items[item_id]['option'])
        return (armor_sheet if sheet_key == 'be' else sheet), col, row

    classes = table['classes']
    cell_px = CELL * scale
    step_x, step_y = cell_px + 30, cell_px + 44
    left = 130
    canvas = Image.new('RGBA', (left + len(colours) * step_x + 20, len(classes) * step_y + 50), (52, 58, 52, 255))
    draw = ImageDraw.Draw(canvas)
    for j, colour in enumerate(colours):
        draw.text((left + j * step_x, 8), f"{colour['name']} {colour['swatch']}", fill=(240, 240, 240, 255))
    for i, entry in enumerate(classes):
        armor_id, weapon_id = entry['startingItems']
        y = 30 + i * step_y
        draw.text((10, y + cell_px // 2 - 10), entry['classId'].replace('class-', '').title(), fill=(240, 240, 240, 255))
        draw.text((10, y + cell_px // 2 + 6), items[armor_id]['name'], fill=(200, 220, 200, 255))
        draw.text((10, y + cell_px // 2 + 22), items[weapon_id]['name'], fill=(200, 220, 200, 255))
        for j, colour in enumerate(colours):
            _, _, hc, hr = option_of(colour['options'][(i * 2 + j) % 12])  # a head style (not a beard), varied per cell
            comp = Image.new('RGBA', (CELL, CELL), (0, 0, 0, 0))
            _, _, sc, sr = option_of(skins[(i + j) % len(skins)]['option'])
            comp = Image.alpha_composite(comp, cell(sheet, sc, sr))
            asheet, ac, ar = item_cell(armor_id)
            comp = Image.alpha_composite(comp, cell(asheet, ac, ar))
            comp = Image.alpha_composite(comp, cell(sheet, hc, hr))
            wsheet, wc, wr = item_cell(weapon_id)
            comp = Image.alpha_composite(comp, cell(wsheet, wc, wr))
            canvas.alpha_composite(comp.resize((cell_px, cell_px), Image.NEAREST), (left + j * step_x, y))
    out = os.path.join(HERE, 'assembled.png')
    canvas.save(out)
    print(f'wrote {out}')


def main():
    scale = int(sys.argv[1]) if len(sys.argv) > 1 else 4
    sheet = load_sheet()
    csv_rows = []
    for name, prefix, cols, rows, under, mark in SHEETS:
        render_grid(sheet, name, prefix, cols, rows, under, mark, scale, csv_rows)
    with open(os.path.join(HERE, 'cells.csv'), 'w', newline='', encoding='utf-8') as f:
        writer = csv.writer(f)
        writer.writerow(['sheet', 'option_id', 'col', 'row'])
        writer.writerows(csv_rows)
    print(f'wrote cells.csv ({len(csv_rows)} cells)')
    render_assembled(sheet, scale)


if __name__ == '__main__':
    main()
