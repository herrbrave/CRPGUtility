"""Writes BarrowsEnd/Assets/Content/Creator/CreatorPicks.json (Phase 4, Tasks 4.0c and 4.2b): what the character creator offers.

Decisions (user): skin = column 1 only (4 tones); weapons = EVERY weapon in the catalog (each is a real item, so they double as loot); hair is FILTERED BY COLOUR
(2026-09-21): a Hair Colour row with a swatch, then a Hair Style row of the 16 shapes (12 head styles + 4 beards), the same shape in every colour; the OUTFIT comes from
the class (each class wears one generated armor, see Assets/Art/Source/make_class_armor.py), so there is no outfit list.

The hair sheet (measured): 5 colour blocks x 16 identical shapes -
    brown  cols 19-22 rows 0-3      ginger cols 23-26 rows 0-3
    blond  cols 19-22 rows 4-7      ash    cols 23-26 rows 4-7
    silver cols 19-22 rows 8-11
Within a block, rows 0-2 are the 12 head styles and row 3 is the 4 beards. Each colour's 16 options are listed row-major, so a style has the same index in every colour.
(An earlier version of this script assumed three colour bands and mislabelled ginger as brown and ash as blond.)

Option ids are the permanent catalog ids; item ids are permanent too (item-weapon-<col>-<row>, item-short-sword for the existing 42-0, item-armor-be-<n>-0).
Names are placeholders except where NAMED says otherwise; edit the JSON freely (names only: never an option or item id).
Refuses to overwrite an existing file (it would discard hand-edited names) unless --force is given.

Usage:  python make_default_picks.py [--force]
"""
import json
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
SHEET = os.path.join(ROOT, 'Assets', 'Art', 'Source', 'roguelikeChar_transparent.png')
OUT = os.path.join(ROOT, 'Assets', 'Content', 'Creator', 'CreatorPicks.json')
PITCH, CELL = 17, 16

SKIN_NAMES = {0: 'Fair', 1: 'Tan', 2: 'Brown', 3: 'Green'}
# (id, name, swatch, first col, first row): the swatch is the block's dominant hair pixel colour. "Ash" is really a dark grey, so it is not called black.
HAIR_COLORS = [
    ('brown', 'Brown', '#A17D52', 19, 0),
    ('ginger', 'Ginger', '#C0751C', 23, 0),
    ('blond', 'Blond', '#E2D4A8', 19, 4),
    ('ash', 'Ash', '#5C5C5C', 23, 4),
    ('silver', 'Silver', '#E8E8E8', 19, 8),
]
HAIR_STYLE_NAMES = [f'Style {i}' for i in range(1, 13)] + [f'Beard {i}' for i in range(1, 5)]
# The class armors, in class order (make_class_armor.py cells 0-4): (option, item, name).
ARMORS = [
    ('clothes-be-0-0', 'item-armor-be-0-0', 'Barbarian Studded Leathers'),
    ('clothes-be-1-0', 'item-armor-be-1-0', 'Cleric Vestments'),
    ('clothes-be-2-0', 'item-armor-be-2-0', 'Fighter Padded Mail'),
    ('clothes-be-3-0', 'item-armor-be-3-0', 'Rogue Dark Leathers'),
    ('clothes-be-4-0', 'item-armor-be-4-0', 'Wizard Mantle'),
]
# (col,row) -> display name for the weapons a class actually starts with; everything else is a placeholder to be named when it becomes loot.
NAMED = {(44, 6): 'Iron Sword', (51, 4): 'Iron Axe', (47, 4): 'Iron Mace', (42, 1): 'Apprentice Staff'}


def non_empty(sheet, col, row):
    x, y = col * PITCH, row * PITCH
    return sheet.crop((x, y, x + CELL, y + CELL)).getchannel('A').getbbox() is not None


def main():
    if os.path.exists(OUT) and '--force' not in sys.argv:
        print(f'{OUT} exists: not overwriting (hand-edited names would be lost). Use --force to regenerate.')
        return
    sheet = Image.open(SHEET).convert('RGBA')

    skin = [{'option': f'body-kenney-1-{r}', 'name': SKIN_NAMES[r], 'item': ''} for r in range(4)]

    hair_colors = []
    for cid, name, swatch, c0, r0 in HAIR_COLORS:
        options = [f'hair-kenney-{c}-{r}' for r in range(r0, r0 + 4) for c in range(c0, c0 + 4) if non_empty(sheet, c, r)]
        assert len(options) == 16, f'{cid}: expected 16 options, found {len(options)}'
        hair_colors.append({'id': cid, 'name': name, 'swatch': swatch, 'options': options})

    armors = [{'option': option, 'name': name, 'item': item} for option, item, name in ARMORS]

    weapons = []
    for r in range(0, 12):
        for c in range(42, 54):
            if not non_empty(sheet, c, r):
                continue
            item = 'item-short-sword' if (c, r) == (42, 0) else f'item-weapon-{c}-{r}'
            name = NAMED.get((c, r), 'Short Sword' if (c, r) == (42, 0) else f'Weapon {c}-{r}')
            weapons.append({'option': f'weapon-kenney-{c}-{r}', 'name': name, 'item': item})

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'skin': skin, 'hairStyleNames': HAIR_STYLE_NAMES, 'hairColors': hair_colors, 'armors': armors, 'weapons': weapons}, f, indent=2)
        f.write('\n')
    print(f'wrote {OUT}: skin {len(skin)}, hair {len(hair_colors)} colours x {len(HAIR_STYLE_NAMES)} styles, armors {len(armors)}, weapons {len(weapons)}')


if __name__ == '__main__':
    main()
