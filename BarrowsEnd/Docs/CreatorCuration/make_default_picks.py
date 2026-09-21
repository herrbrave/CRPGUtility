"""Writes the initial BarrowsEnd/Assets/Content/Creator/CreatorPicks.json (Phase 4, Task 4.0c): every skin/hair/outfit/weapon the creator offers.

Decision (user, 2026-09-20): skin = column 1 only (4 tones); hair, outfit and weapons = EVERY option in their catalogs (weapons double as loot).
Option ids are the permanent catalog ids; weapon ITEM ids are item-weapon-<col>-<row> (item-short-sword for the existing 42-0), also permanent.
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
HAIR_BANDS = ['Brown', 'Blond', 'Grey']  # rows 0-3, 4-7, 8-11; row 3 of each band is a beard
# (col,row) -> display name for the weapons a class actually starts with; everything else is a placeholder to be named when it becomes loot.
NAMED = {(44, 6): 'Iron Sword', (51, 4): 'Iron Axe', (47, 4): 'Iron Mace', (42, 1): 'Apprentice Staff'}


def non_empty(sheet, col, row):
    x, y = col * PITCH, row * PITCH
    return sheet.crop((x, y, x + CELL, y + CELL)).getchannel('A').getbbox() is not None


def cells(sheet, cols, rows):
    """Row-major, matching the catalog order."""
    return [(c, r) for r in rows for c in cols if non_empty(sheet, c, r)]


def main():
    if os.path.exists(OUT) and '--force' not in sys.argv:
        print(f'{OUT} exists: not overwriting (hand-edited names would be lost). Use --force to regenerate.')
        return
    sheet = Image.open(SHEET).convert('RGBA')

    skin = [{'option': f'body-kenney-1-{r}', 'name': SKIN_NAMES[r], 'item': ''} for r in range(4)]

    hair = []
    for c, r in cells(sheet, range(19, 27), range(0, 12)):
        band, kind = HAIR_BANDS[r // 4], r % 4
        if kind == 3:
            name = f'{band} Beard {c - 18}'
        else:
            name = f'{band} Hair {(c - 19) * 3 + kind + 1}'
        hair.append({'option': f'hair-kenney-{c}-{r}', 'name': name, 'item': ''})

    outfit = []
    for c, r in cells(sheet, range(6, 18), range(0, 10)):
        garment = (c - 6) * 5 + (r % 5) + 1
        outfit.append({'option': f'clothes-kenney-{c}-{r}', 'name': f'Garment {garment}{"a" if r < 5 else "b"}', 'item': ''})

    weapons = []
    for c, r in cells(sheet, range(42, 54), range(0, 12)):
        item = 'item-short-sword' if (c, r) == (42, 0) else f'item-weapon-{c}-{r}'
        name = NAMED.get((c, r), 'Short Sword' if (c, r) == (42, 0) else f'Weapon {c}-{r}')
        weapons.append({'option': f'weapon-kenney-{c}-{r}', 'name': name, 'item': item})

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'skin': skin, 'hair': hair, 'outfit': outfit, 'weapons': weapons}, f, indent=2)
        f.write('\n')
    print(f'wrote {OUT}: skin {len(skin)}, hair {len(hair)}, outfit {len(outfit)}, weapons {len(weapons)}')


if __name__ == '__main__':
    main()
