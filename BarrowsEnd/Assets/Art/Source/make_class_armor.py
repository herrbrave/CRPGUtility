"""Generates barrowsend_armor_transparent.png: the five class armors (Task 4.2b), custom 16x16 same-canvas CLOTHES layers (17px pitch, like the Kenney
Roguelike characters sheet and the generated skeleton bodies) so each class wears one complete outfit: a Kenney top composited OVER trousers and boots that this
script paints on the body's own leg pixels. The Kenney sheet has no pants layer (its clothes are tops that mostly stop at the hip, and the body's legs are bare skin),
so the trousers have to be generated. Original Kenney art is not modified.

Cells, in class order: 0 Barbarian, 1 Cleric, 2 Fighter, 3 Rogue, 4 Wizard  ->  catalog options clothes-be-N-0 -> items item-armor-be-N-0.
Edit the TOPS / COLOURS tables below and re-run to change a look; the item ids never change.

Usage:  python make_class_armor.py            regenerate barrowsend_armor_transparent.png
        python make_class_armor.py --check    regenerate in memory and exit 1 if the committed PNG's pixels differ (deterministic)
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
KENNEY = os.path.join(HERE, 'roguelikeChar_transparent.png')
OUT = os.path.join(HERE, 'barrowsend_armor_transparent.png')
PITCH, CELL = 17, 16

# (class, Kenney top cell (col,row), trousers RGB, boots RGB). Tops come from five different silhouettes so the classes read differently.
TOPS = [
    ('Barbarian', (6, 4), (114, 88, 57), (73, 55, 36)),     # brown studded shoulders, brown leather trousers
    ('Cleric', (16, 1), (226, 212, 168), (114, 88, 57)),    # white tabard with lavender trim, cream trousers, brown boots
    ('Fighter', (7, 4), (120, 120, 118), (70, 70, 70)),     # tan-grey padded shoulders, grey mail chausses
    ('Rogue', (15, 5), (60, 60, 64), (35, 35, 38)),         # charcoal with a belt, dark trousers and boots
    ('Wizard', (17, 2), (110, 90, 150), (60, 50, 80)),      # lavender draped mantle, purple trousers
]
LEG_TOP, BOOT_TOP = 11, 14  # body legs are rows 11-15: trousers on 11-13, boots on 14-15


def cell(sheet, col, row):
    x, y = col * PITCH, row * PITCH
    return sheet.crop((x, y, x + CELL, y + CELL))


def shade(rgb, k):
    return tuple(max(0, min(255, int(v * k))) for v in rgb) + (255,)


def leg_mask(sheet):
    """The body's leg pixels (rows 11-15) as a set of (x, y). Every skin tone (body cells 1-0..1-3) shares this silhouette; asserted, not assumed."""
    masks = []
    for skin in range(4):
        body = cell(sheet, 1, skin)
        masks.append({(x, y) for y in range(LEG_TOP, 16) for x in range(CELL) if body.getpixel((x, y))[3] > 0})
    assert all(m == masks[0] for m in masks), 'the skin tones no longer share a leg silhouette: the trousers would not fit them all'
    return masks[0]


def trousers(legs, colour, boots):
    layer = Image.new('RGBA', (CELL, CELL), (0, 0, 0, 0))
    xs = [x for x, _ in legs]
    mid = (min(xs) + max(xs)) / 2
    for x, y in legs:
        if y >= BOOT_TOP:
            layer.putpixel((x, y), shade(boots, 1.0))
        else:
            layer.putpixel((x, y), shade(colour, 0.7 if abs(x - mid) < 0.75 else 1.0))  # a darker centre seam gives the legs volume
    return layer


def generate():
    sheet = Image.open(KENNEY).convert('RGBA')
    legs = leg_mask(sheet)
    out = Image.new('RGBA', (PITCH * len(TOPS) - 1, CELL), (0, 0, 0, 0))
    for n, (name, top, colour, boots) in enumerate(TOPS):
        composed = Image.alpha_composite(trousers(legs, colour, boots), cell(sheet, *top))  # the top over the trousers: a long tunic still covers them
        out.paste(composed, (n * PITCH, 0))
    return out


def main():
    image = generate()
    if '--check' in sys.argv:
        if not os.path.exists(OUT):
            print(f'{OUT} does not exist')
            sys.exit(1)
        committed = Image.open(OUT).convert('RGBA')
        if committed.size != image.size or list(committed.getdata()) != list(image.getdata()):
            print('barrowsend_armor_transparent.png differs from what make_class_armor.py generates: re-run the script')
            sys.exit(1)
        print('barrowsend_armor_transparent.png matches the generator')
        return
    image.save(OUT)
    print(f'wrote {OUT} ({image.width}x{image.height}, {len(TOPS)} cells)')


if __name__ == '__main__':
    main()
