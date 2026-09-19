# Project Handoff: Barrow's End CRPG
**Target System:** Custom Framework testing / D&D 5.1e SRD (OGL)
**Level Range:** 1 to 3
**Asset Pack:** Kenney modular 2D pixel art (Town, Characters, Cave, Interiors)

## 1. Project Overview
This is a sample project for a short CRPG designed to test a new framework, particularly complex dialogue tree dependencies. The game features a hub town ("Barrow's End"), modular character creation, party recruitment, and a three-act subterranean dungeon crawl.

## 2. Party Composition (Max Party Size: 4)
*   **The Protagonist (Player-Created):** Highly customizable using modular sprite assets (base bodies + layered armor/weapons/hair).
*   **Garrick the Stalwart (Fighter):** Found in the tavern. Heavy armor, broadsword, wooden shield. Acts as the frontline tank.
*   **Elara the Swift (Rogue):** Found near market tents. Light leather tunics, dual daggers. High burst damage and lockpicking utility.
*   **Brother Silas (Cleric):** Found at the shrine. Simple robes, mace. Life Domain for party sustain and healing.

## 3. Hub Town: Barrow's End
A rugged, frontier settlement acting as the game's primary hub. Visually composed of dirt paths, grassy squares, and wooden palisades, with detailed building interiors.

**Key NPCs & Services:**
1.  **Mayor Thaddeus (Town Hall):** Main quest giver. Uses the complex dialogue tree for framework testing. 
2.  **Barnaby (The Iron Anvil):** Blacksmith (weapons/heavy armor).
3.  **Elida (Restful Hearth Inn):** Innkeeper (rests/save point).
4.  **Fendrick (Apothecary):** Alchemist (healing potions/antitoxins).
5.  **Marjorie (General Store):** Adventuring gear (rope, torches, rations).
6.  **Captain Kael (Guard Barracks):** Bounties and defense coordinator.
7.  **Lyra (Arcanist's Study):** Scholar (scrolls/magic identification).
8.  **Old Man Jenkins (Town Square):** Rumor mill/lore provider.
9.  **Grumble (Tavern Cook):** Side quest giver (harvesting giant mushrooms).
10. **Silvan (Fletcher/Bowyer):** Ranged weapons and ammunition.

## 4. Campaign Flow
*   **Act 1: The Gathering (Level 1):** The player arrives in Barrow's End. Objective: Recruit Garrick, Elara, and Silas. Combat Tutorial: Clear out giant rats/beasts breaching the town's wooden palisade fencing. 
*   **Act 2: The Descent (Level 2):** The party enters the Glimmering Caverns. Environment features rocky outcroppings, dirt paths, and mine tracks. Enemies: Subterranean goblins utilizing cover/barricades. Side quest: Mushroom harvesting for Grumble.
*   **Act 3: The Deep Dark (Level 3):** The deepest cave sector. Heavy stonework, skeletal remains, and an underground river. Final Boss: An aspiring necromancer raising an undead thrall army. Tests party synergy, AoE management, and level 3 subclass features.

## 5. Dialogue System Test Framework
This dialogue tree is specifically designed to test the framework's logic processing (booleans, inventory checks, integer thresholds, state tracking).

### Conversation Target: Mayor Thaddeus

**Node_01: The Briefing (Root)**
*   **Speaker:** Mayor Thaddeus
*   **Text:** "I see the new mercenary squad has finally arrived. The caverns beneath Barrow's End are crawling with hostiles. Are you properly equipped to handle this, or are you just here to waste my time?"
*   **Choices:**
    *   **Option 1:** "We have our squad and our supplies. We are ready to descend."
        *   **Dependencies:** `has_recruited_silas == true` AND `inventory_qty("healing_potion") >= 4`
        *   **Target:** `Node_02_Approved`
        *   **Actions:** `set quest_caverns_active = true`
    *   **Option 2:** "We need more time to reposition and gather our strength in town."
        *   **Dependencies:** `current_tactical_phase == "Reposition"`
        *   **Target:** `Node_03_Delay`
        *   **Actions:** `add tempo_points += 1`
    *   **Option 3:** "Just point us to the target. We'll expose their defenses and exploit the opening."
        *   **Dependencies:** `current_tactical_phase == "Pressure"` AND `tempo_points >= 3`
        *   **Target:** `Node_04_Aggressive`
        *   **Actions:** `set current_tactical_phase = "Expose"` ; `subtract tempo_points -= 2`
    *   **Option 4:** "I need more information. What exactly is down there?"
        *   **Dependencies:** None
        *   **Target:** `Node_05_Exposition`

**Node_02_Approved**
*   **Speaker:** Mayor Thaddeus
*   **Text:** "Excellent. Head past the northern barricades. The guards have orders to let you through."
*   **Choices:**
    *   **Option 1:** "[End Conversation] We won't let you down."
        *   **Dependencies:** None
        *   **Target:** `Exit_Dialogue`

**Node_03_Delay**
*   **Speaker:** Mayor Thaddeus
*   **Text:** "Don't take too long. Every hour you spend resting, they dig deeper."
*   **Choices:**
    *   **Option 1:** "Let's review the local bounties in the meantime."
        *   **Dependencies:** `read_bounty_board == true`
        *   **Target:** `Node_06_Bounties`
    *   **Option 2:** "[End Conversation] We will return shortly."
        *   **Dependencies:** None
        *   **Target:** `Exit_Dialogue`

**Node_04_Aggressive**
*   **Speaker:** Mayor Thaddeus
*   **Text:** "I appreciate the ferocity, but don't let it blind you. Take this key; it opens the old mining lift. It skips the upper tunnels."
*   **Choices:**
    *   **Option 1:** "[End Conversation] Consider it done."
        *   **Dependencies:** None
        *   **Target:** `Exit_Dialogue`
        *   **Actions:** `grant_item("mining_lift_key")`

**Node_05_Exposition**
*   **Speaker:** Mayor Thaddeus
*   **Text:** "Goblins, mostly. But they are organized. They've built heavy barricades and seem to be guarding something deeper in the stone."
*   **Choices:**
    *   **Option 1:** "Return to previous questions."
        *   **Dependencies:** None
        *   **Target:** `Node_01`