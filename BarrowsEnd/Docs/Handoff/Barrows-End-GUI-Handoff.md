# UI & Framework Testing Handoff: Barrow's End

## 1. Asset Manifest
*   **UI Spritesheet:** `uipack_rpg_sheet.png`[cite: 6]
*   **Primary Font:** `KenPixel.ttf`[cite: 7]
*   **Design Language:** Heavy wood and stone textures with colored accent pills and circular toggle buttons[cite: 6]. High-contrast pixel font for all numeric stats and menu items[cite: 7].

## 2. Interface Layouts & Wireframes

### A. Basic Screen HUD (Main Gameplay)
The tactical view is kept clear for combat and exploration, anchoring controls to the perimeter.

*   **Party Status (Top-Left):** Stacked thin tan/grey horizontal panels[cite: 6]. HP/Mana tracking utilizes the green, blue, and red pill-shaped bar assets[cite: 6].
*   **Action Bar (Bottom-Center):** Wide brown horizontal panel housing empty square button slots[cite: 6]. Populated with grey, yellow, and brown sword icons[cite: 6] representing abilities.
*   **System Menu (Top-Right):** Small circular buttons utilizing the grey cross (X) and cursor hand icons[cite: 6].

```text
+-----------------------------------------------------------------------+
|  [=== Garrick  [++++++] [····] ]                  (Hand) (Eye)  ( X ) |
|  [=== Elara    [++++++] [····] ]                                      |
|  [=== Silas    [++++++] [····] ]                                      |
|  [=== Player   [++++++] [····] ]                                      |
|                                                                       |
|                                                                       |
|                                                                       |
|                                                                       |
|                                                                       |
|                     +---------------------------+                     |
|                     | [Swd] [Axe] [Wand] [ ] [ ]|                     |
|                     +---------------------------+                     |
+-----------------------------------------------------------------------+
```

### B. Character Creator

A split-screen configuration separating visual rendering from category text.

* **Preview Container (Left):** Large, tall tan panel with corner rivets. Centers the modular paper doll sprite.
* **Options Menu (Right):** Large, tall grey panel. Utilizes medium-width white/light tan panels for category rows (Head, Body, Weapon). Selection cycling uses the beige/grey triangular arrow buttons.
* **Footer Controls:** Green circular checkmark button (Confirm) and grey/red circular 'X' button (Cancel).

```text
+-----------------------------------------------------------------------+
|                                                                       |
|    +-------------------------+       +-------------------------+      |
|    |                         |       |                         |      |
|    |                         |       |  <  Head:   Type 3   >  |      |
|    |      [ Assembled ]      |       |                         |      |
|    |      [ Character ]      |       |  <  Body:   Armor 2  >  |      |
|    |      [  Sprite   ]      |       |                         |      |
|    |                         |       |  <  Weapon: Sword    >  |      |
|    |                         |       |                         |      |
|    |                         |       |  <  Class:  Fighter  >  |      |
|    |                         |       |                         |      |
|    |                         |       |               ( X ) ( V )      |
|    +-------------------------+       +-------------------------+      |
|                                                                       |
+-----------------------------------------------------------------------+
```

### C. Paper Doll & Inventory

A unified equipment and stat management screen utilizing the heaviest textured assets.

* **Background:** The largest square brown panel, scaled to fit the screen.
* **Equipment Slots (Left):** Indented square panels arranged anatomically around the character. Sword icons act as watermark placeholders for empty slots.
* **Stat Block (Bottom-Left):** Light-colored square panel rendering STR, DEX, AC, and HP in the KenPixel font.
* **Backpack Grid (Right):** A 4x4 or 5x5 grid of small, empty square button graphics. Bordered by a scrollbar made from the vertical slider track and pill-shaped handle.
* **Close Toggle:** Small circular grey button with white 'X' at the top right.

```text
+-----------------------------------------------------------------------+
| +-------------------------------------------------------------------+ |
| | INVENTORY                                                   ( X ) | |
| |                                                                   | |
| |          [Head]                 [ Swd ] [ Lth ] [ Ptn ] [ Ptn ] [|] |
| |                                 [ Key ] [     ] [     ] [     ] [|] |
| | [L-Wpn]  [Body]  [R-Wpn]        [     ] [     ] [     ] [     ] [|] |
| |                                 [     ] [     ] [     ] [     ] [O] |
| |          [Feet]                 [     ] [     ] [     ] [     ] [|] |
| |                                 [     ] [     ] [     ] [     ] [|] |
| |  +-------------------+                                            | |
| |  | STR: 16   AC: 14  |                                            | |
| |  | DEX: 14   HP: 28  |                                            | |
| |  +-------------------+                                            | |
| +-------------------------------------------------------------------+ |
+-----------------------------------------------------------------------+
```

## 3. Framework Testing & Execution Notes

* **Directory Resolution:** Ensure all relative asset paths for the sprite sheets and the `.ttf` font resolve correctly within the Windows Subsystem for Linux (WSL) directory structure during local test builds.
* **Logic Pipeline:** If routing the multi-condition dialogue dependencies (e.g., Mayor Thaddeus' boolean and inventory checks) through local language models via Ollama or the Hermes Agent harness, verify that the custom Modelfiles correctly parse the integer arithmetic (`tempo_points`) and state string trackers without hallucinating invalid JSON payloads.

<!--
Claude's note (2026-09-18): the two bullets above (WSL directory resolution, Ollama/Hermes Agent
harness/Modelfiles) do not match this project -- native Windows, no LLM-in-the-loop dialogue anywhere
in the actual engine (CRPG.Dialogue is deterministic BranchNode/ITimeline condition evaluation). Kept
verbatim here since this is the source handoff doc; not acted on. Flagged to the user in-session.
-->
