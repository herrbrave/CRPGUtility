# Viking Raid -- Task 2: content + balance (v2: Opus-reviewed; Section 12 SUPERSEDES conflicting text above)

Status: v2 2026-10-05, review folded in (Section 12). Part of `Docs/Plans/VikingRaid-Implementation.md` (read its Sections 9-12 first: Opus review findings, Task 0/1 results, bow
animation). Task 2 = **stats, abilities, enemy behaviour and balance**. Pure content + an EditMode balance simulation. NOT in scope: scene/NavMesh (Task 3),
the game loop / combat session / encounter triggers / HUD (Task 4), VFX, projectiles.

## 1. Goal

Author every definition the four fights need, data only, on the real SRD5 ruleset, and PROVE by simulation that the four planned fights land in their intended
difficulty bands -- before any scene exists. Deliverables:
1. A project-local `VikingRaidRulesetProvider` (SRD5 provider + the game's extra content) -- same shape as BarrowsEnd's `BarrowsEndRulesetProvider`.
2. A re-runnable Editor authoring script producing assets under `VikingRaid/Assets/Content/`: resources, conditions, features, abilities, 4 hero + 6 goblin-type
   `NpcDefinition`s, behaviour sets.
3. `VikingRaid.EditModeTests`: content-integrity tests, ability/AI tests, and the balance simulation (E1-E4, Monte-Carlo).
4. A results table (win rate, survivors, HP lost, rounds) appended to this doc, and the tuned numbers.

## 2. Engine facts this design leans on (from reading source 2026-10-05 -- reviewer: re-verify every one)

* **Attack bonus** = ability modifier of the ability's `governingStatId` + proficiency bonus by `CharacterState.Level` + gathered `"AttackBonus"` modifiers;
  *every attack is proficient* (no weapon-proficiency gate). `crpg-toolkit/Rules/SRD5e2014/Runtime/Combat/DefaultAttackBonusRule.cs`.
* **Armor class** = 10 + Dexterity modifier + gathered `"ArmorClass"` modifiers (armor/shield are FeatureModifiers; there is no armor-base-AC concept), `DefaultArmorClassRule.cs`.
* **Extra attacks**: `ComputeAttackCount` = 1 + sum of `"AttackCount"` modifiers; `AttackRollEffect` loops it, each pass an independent roll against the SAME targets.
* **Damage** is the authored `DiceExpression` (count, sides, FLAT modifier) -- the ability modifier is NOT added automatically; crit doubles dice count only.
  `DamageEffect` also has optional `damageTypeId` (resistance), `scalesWithCasterLevel` (cantrip scaling), `extraDiceCountPerSlotLevel`.
* `AttackRollEffect { governingStatId (None => the caster's SpellcastingStatId = spell attack), onHit }`; `HealEffect { hitPointResource, amount }`;
  `ApplyConditionEffect { condition, duration, boundary (DurationBoundary None/StartOfTargetTurn/EndOfTargetTurn/StartOfSourceTurn/EndOfSourceTurn) }`.
* `AbilityDefinition` fields: displayName, description, tags (Attack/Spell/Melee/Ranged/Weapon/Healing/Reaction/Hostile/...), costs (resource + amount),
  `TargetingDefinition` (mode, relation Self/Ally/Enemy/Any, min/maxTargets, range, requireLineOfSight/-Effect/-VisibleTarget), `[SerializeReference] effects`,
  prepareSeconds, recoverSeconds, `animationCueId`.
* `NpcDefinition` is a full flat sheet: faction, level, abilityScores (`StatScoreEntry`), proficiencies, resources (`ResourceGrant{resource,maxAmount}`), featureIds,
  abilityIds (KnownAbilities -- once non-empty, unknown abilities are REJECTED at cast time, feature-granted ones included), spellcastingStatId, visualPrefab.
* SRD5 content shipped: stats (Str/Dex/Con/Int/Wis/Cha), resources HitPoints, Action, BonusAction, Reaction, SecondWindUses, ActionSurgeUses, RageUses, spell slots 1-2;
  19 conditions incl. Prone/Unconscious/Restrained/Reckless (the shipped Reckless has the advantage sign backwards -- attackers get DISadvantage -- and never expires: author our own).
  No Movement resource ships (BarrowsEnd authors `resource-movement`).
* AI: `UtilityAIDecisionProvider(behaviorSet, rulesDb, characters, abilityResolution, positions, random)`; `GetDecision(TurnContext{self, potentialEnemies, potentialAllies})` -> `TurnDecision`
  (`Kind` UseAbility with `Ability`+`Targets`, or Move with `MovementResourceId`+`StopDistance`+`Targets`). Per-behaviour `Consideration{inputKey, curve, weight}`; a vetoed 0-score
  decision still wins if it is the only one; the first of equal scores wins. `PerCombatantDecisionProvider` (BarrowsEnd, ~30 lines) maps combatant -> behaviour set.
  Ranged precedent: `BarrowsEnd/Assets/Runtime/Gate/HostileBrainKit.cs` (`BuildMeleeSet`, `BuildRangedSet`: Move 0.6->0.1, melee 1->0.95, ranged 0.9->0.86, no kiting).
* Pure-C# AI/combat testing precedent: `crpg-toolkit/Tests/Fixtures/TacticalEncounterVerification/TacticalAiScoringTests.cs` (real `UtilityAIDecisionProvider`, a fake `IPositionProvider`,
  real `AbilityResolutionService` with `DefaultAttackBonusRule`/`DefaultArmorClassRule`).
* Services for a pure-C# fight: `CombatEncounterService(characters, initiativeRule, TurnResourceSet, bus, log)`, `EncounterFactionProvider`, `AbilityResolutionService(... factionProvider ...)`,
  `MovementResolutionService(characters, positions, IMovementController, ...)` (`MoveToward` clamps to remaining Movement and `stopDistance`), `ConditionDurationService` (ticks conditions
  at turn boundaries, subscribed by `TacticalEncounterController`), `DeathSaveService`.

## 3. The party (level 3, proficiency +2) -- SRD5 numbers, one attack per Action unless noted

Abilities (hit = d20 + governing-stat mod + 2 vs AC; damage flat modifier authored). Turn economy per round: 1 Action, 1 Bonus Action, 1 Reaction, Movement 8 m.

| Hero | Role | Str/Dex/Con (+ others) | HP | AC | Abilities |
|---|---|---|---|---|---|
| **Bjorn** | shield-bearer | 16 / 10 / 15 | 31 | 18 (10 + Dex 0 + `ArmorClass +8`: chain mail + shield) | **Sword Strike** melee 2.0 m, 1d8+3 (+5 to hit); **Second Wind** (bonus action, self, 1d10+3, 1 use); Opportunity Attack (reaction) |
| **Ulf** | berserker | 17 / 12 / 16 | 34 | 14 (10 + 1 + 3 hide) | **Great Axe** melee 2.0 m, 1d12+3 (+5); **Reckless Attack** (bonus action, self: own attacks Advantage and attacks against him Advantage until the start of his next turn); Opportunity Attack |
| **Sigrid** | archer | 10 / 16 / 14 | 26 | 15 (10 + 3 + 2 leather) | **Longbow Shot** ranged 14 m, 1d8+3 (+5 to hit via Dex), uses `anim-shoot` (Bow_Draw); **Dagger Stab** melee 1d4+3 |
| **Astrid** | skald (support) | 10 / 12 / 13, Cha 16 | 24 | 13 (10 + 1 + 2 leather) | **Spear Thrust** melee 2.5 m, 1d6+1 (+3 to hit via Dex); **Rallying Verse** ranged 8 m ally, heal 1d8+3, Bonus Action, 3 uses/fight (a `resource-verse-uses`) |

## 4. The goblins (stat sheets; HP = SRD hit points)

| Type | Visual prefab(s) | Lvl | HP | AC | Attacks | Brain |
|---|---|---|---|---|---|---|
| **Grunt** | `Goblin_Grunt_A/B/C` | 1 | 7 | 13 (Dex 14 -> +2 mod, `ArmorClass +1`) | Cleaver melee 1d6+2 (+4) | melee |
| **Warrior** | `Goblin_Warrior_A/B/C` | 2 | 15 | 17 (chain + shield) | Sword melee 1d8+2 (+4) | melee |
| **Archer** | `Goblin_Archer_M/F` | 1 | 9 | 14 | Shortbow 12 m 1d6+2 (+4); Dagger melee 1d4+2 | ranged |
| **Shaman** | `Goblin_Shaman` | 3 | 22 | 13 | Fire Bolt 12 m 1d10 (spell attack +5, `spellcastingStatId` Wis 16); Staff melee 1d6 | ranged (caster) |
| **Troll** | `Goblin_Troll` | 5 | 70 | 15 | **Rend**: 2d6+4 (+7), **2 attacks per Action** (feature `AttackCount +1`) | melee |
| **Goblin King** | `Goblin_King` | 5 | 55 | 17 | Greatsword 1d10+3 (+6), **2 attacks per Action** | melee |

## 5. Encounters (party starts together; foes 18-22 m away; ranged units 4-6 m behind their melee line)

* **E1 woods ambush**: 3 Grunts. **E2 woods ambush**: 2 Warriors + 1 Archer. **E3 camp gate**: 2 Warriors + 2 Archers + 1 Shaman.
* **E4 camp heart**: Goblin King + Troll + Shaman + 2 Grunts.
* Between fights: full heal and revive (plan decision); each fight is simulated independently from full HP / full per-fight resources.

## 6. Conditions, features, resources

* `condition-reckless-viking` (own): modifiers `AttackBonus` Advantage + `AttacksAgainst` Advantage; applied by Reckless Attack with `duration 1`, `boundary StartOfTargetTurn`.
* Features (granted via `NpcDefinition.featureIds`): `feature-armor-N` style `ArmorClass Add N`; `feature-multiattack` (`AttackCount Add 1`) for Troll and King.
* Resources: `resource-movement` (Movement; 8 m heroes, 6 m goblins, 4 m Troll/King: spent by `MovementResolutionService`), `resource-verse-uses` (Astrid), SRD5 SecondWindUses for Bjorn.
* Cue ids on abilities: `anim-attack-melee`, `anim-shoot` (bows -> Bow_Draw; Fire Bolt -> the UAL shoot gesture).

## 7. Enemy behaviour (utility AI, data only)

* **Melee set** (Grunt, Warrior, Troll, King): Close Distance (Move) + Attack; curves as `HostileBrainKit.BuildMeleeSet` (Attack floor 0.85 > Move ceiling 0.6), plus a
  `TargetResourcePercent:resource-hp` consideration on Attack so downed/low-HP heroes are not farmed once others are standing.
* **Ranged set** (Archer, Shaman): Close Distance to `stopDistance` ~ 6 m, Ranged attack from anywhere in range, melee fallback when adjacent (BuildRangedSet curves), same HP% consideration.
* **Hero sim brains** (balance sim only; the player drives heroes in the game): Bjorn/Ulf melee set; Sigrid ranged set; Astrid: spear melee + a heal behaviour on allies below 50% HP.

## 8. Balance simulation (`VikingRaid.EditModeTests`)

Pure C# in EditMode against the real services (no GameObjects, no NavMesh): load the authored assets, build a `CharacterRegistry`, spawn `CharacterState`s from the `NpcDefinition`s
(replicating what `NpcSpawnService` does), a fake `IPositionProvider` + a fake `IMovementController` that really moves positions, `CombatEncounterService` for rolled initiative and turn resources,
`AbilityResolutionService` with `DefaultAttackBonusRule`/`DefaultArmorClassRule`/`EncounterFactionProvider`, `ConditionDurationService`, per-combatant `UtilityAIDecisionProvider`s. Each turn: decision ->
`Execute` / `MoveToward`; end turn when Action is spent and nothing useful remains. A hero at 0 HP is OUT for the rest of the fight (conservative: no death-save revival in the sim).
N = 400 seeded fights per encounter. Round cap 40 (a fight that hits the cap counts as a stalemate and fails the gate).
**Target bands:** E1 win >= 97%, E2 >= 88%, E3 75-92%, E4 55-80%; average hero HP lost across fights rises monotonically E1 < E2 < E3 < E4; no single hero is the focus of > 60% of damage in
E1-E3; no fight averages > 14 rounds.

## 9. Tests

* Content integrity: every `NpcDefinition` ability/feature/resource resolves in the ruleset database; every ability's costs reference a resource the NPC owns; every `AnimationCueId` exists in
  the matching profile (Hero/HeroBow/Goblin/GoblinBow); every visual prefab exists; ids unique; ruleset provider registers everything.
* Rules: Reckless Attack grants Advantage to the user's attack roll and to attackers against him, and EXPIRES at the start of his next turn (real `ConditionDurationService`); Second Wind restores
  1d10+3 once; Rallying Verse heals an ally but not above max and is refused on an enemy; Troll makes two attack rolls per Rend; crit doubles dice.
* AI: melee unit closes then attacks; archer shoots at distance and stabs when adjacent; no unit moves when a legal attack exists; no unit attacks a 0-HP target while another is standing; a caster prefers the bolt.
* Simulation gates as in Section 8.

## 10. Deliberate simplifications (flag to the user)

No death saves in the sim; no opportunity attacks in the sim (the game has them); no healing AI for the Shaman (heroes' healer only); no terrain, line-of-sight or cover in the sim; goblins never flee;
heroes' brains are crude (the player will play better or worse). The sim bounds the numbers; the Task 5 scripted playthrough and the user's playthrough are the real tests.

## 11. Questions for the reviewer

* Are the Section 2 facts right? Particularly: who refills Action/BonusAction/Movement each turn (`CombatEncounterService`/`TurnResourceSet`?) and what must the sim do itself; does `NpcDefinition` give
  heroes `Level` 3 so proficiency is +2; can `ResourceGrant` set current < max; does feature-granted `AttackCount` really apply to an NpcDefinition character.
* Can a heal ability target a specific ally in AI/sim (target selection, `TargetRelation.Ally` excluding self)? How would the sim's Astrid brain pick "ally below 50% HP"?
* Is the planned `TargetResourcePercent` consideration real (name, scale, what a target at 0 HP scores)?
* Is a pure-C# sim without `TacticalEncounterController` faithful enough, or does skipping `ActionScheduler`/`AbilityActionExecution` (prepare/recover timing, presentation hook order) change outcomes?
* Anything in the numbers/bands that is internally inconsistent (e.g. expected damage per round vs HP pools), or a better set of encounter compositions?


## 12. Opus review record and v2 decisions (2026-10-05) -- SUPERSEDES Sections 2-10 where they conflict

Review was read-only and source-verified (its report cites file:line throughout; the facts below are its, re-verified at implementation time by tests).

### 12.1 Blockers found, and the decision adopted
1. **Turn resources are only REFILLED, never granted** (`TurnResourceSet.RestoreFull` refills what a character already owns, at `BeginTurn`). Every `NpcDefinition` therefore carries
   `ResourceGrant`s for `resource-hp`, `resource-action` 1, `resource-bonus-action` 1, `resource-reaction` 1 and `resource-movement` (8 heroes / 6 goblins / 4 Troll+King); the
   `TurnResourceSet` is {action, bonus-action, reaction, movement}. A content test asserts all five exist on every NPC. `ResourceGrant` always spawns Current = Max.
2. **Death**: nothing removes a combatant at 0 HP; a 0-HP combatant keeps acting. The sim MIRRORS THE GAME (review option B): `DeathSaveService(chars, dice, bus, hp, unconscious, usesDeathSaves: isHero,
   canDie: id => !isHero(id))`, a `CreatureDied` subscriber calling `RemoveCombatant`, and a revive sweep (HP > 0 and DeathSaves != null -> clear). Downed = HP <= 0; a downed hero counts as full HP lost.
3. **The AI turn loop is not "end when Action is spent"**: `TacticalAiTurnDriver` keeps asking until null / 8 decisions / 3 rejected starts, so after attacking a goblin still Moves toward its stopDistance. The sim
   replicates that loop exactly. **Hero turn rule decided now**: bonus-action abilities BEFORE the Action; the turn ends when the player presses End Turn or, in the scripted policy, when no useful decision remains.
4. **Hero utility brains cannot gate Reckless/Second Wind/Verse by data** (Self abilities see only Distance 0 / RandomChance / own HP%; a 0-score decision still wins when it is the only one). The sim uses a **scripted C# hero
   policy** (heroes are player-driven in the game anyway). Goblins use authored `BehaviorSet`s.
5. **The draft bands were unreachable** (E1-E3 ~97-100% wins; E4 ~5-15%). A loss means a TOTAL wipe, so win rate is the wrong gate until foe strength approaches party strength. New gates are in 12.3.

### 12.2 Should-fix items adopted
* Author ALL SIX ability scores on every NPC (a missing stat = 0 = -5 modifier: AC 5, -5 to hit); a test asserts `ComputeArmorClass` and `ComputeAttackBonus` equal the design table for every NPC/ability.
* Every ability an AI may use costs Action or BonusAction (else it repeats up to 8x/turn); do NOT reuse the shipped Fire Bolt / Reckless Attack (`costs: []`) nor the shipped armour features (not SRD AC).
  Reuse the shipped `ability-second-wind` (BonusAction + `resource-second-wind`, Self, 1d10+3) and put it in `abilityIds` (feature-granted abilities are rejected as AbilityNotKnown).
* **HP% consideration arithmetic** (`UtilityMath.CombineScores` compensation `p + (1-p)(1-1/n)p`): with 2 considerations Move's ceiling rises 0.6 -> 0.72, so an Attack whose HP factor is < ~0.73 for a LIVING target loses to Move
  and the goblin shuffles forever. Rules: pure 0-HP veto keys (0,0),(0.01,1),(1,1) on BOTH Attack and Move (else a goblin beside a downed hero Moves toward it and idles); any focus-fire shaping keeps the Attack factor >= 0.85.
  The input key is exactly `TargetResourcePercent:resource-hp` (a typo silently returns 0 = "everyone is dead": every attack vetoed) -- a test resolves every behaviour input key.
* Archers: `stopDistance = range - 2` (10 m for 12 m bows), not 6 m (else they walk after every shot). The Shaman has the Fire Bolt only (melee beats ranged when adjacent in the shipped curve shape).
* **Opportunity attacks are dropped from the roster** (AI moves pass no reactors; only player moves do; reactions also need feature-granted ability + abilityIds + threatRange): Section 10's "the game has them" was false.
* Heal does not revive: `HealEffect` restores HP only; the revive sweep (Task 4 game loop) clears DeathSaves/Unconscious when HP > 0. Astrid's Verse can target self in the player UI (Validate counts self as ally); the AI cannot.
* One `visualPrefab` per `NpcDefinition` => 4 hero + 11 goblin definitions (Grunt A/B/C, Warrior A/B/C, Archer M/F, Shaman, Troll, King), one per prefab.
* Initiative is a flat d20 (no Dex): accepted. Multiattack swings both go at the same target (a few % wasted). Restoring per-fight resources (Second Wind, Verse uses) is part of "full heal" between fights.
* Targeting defaults are SingleUnit / Enemy / range 5 / LoS true: Self abilities need Mode=Self AND Relation=Self; Verse Relation=Ally; `requireVisibleTarget`/`requireLineOfEffect` are never read; LoS only with an `ILineOfSightProvider`.
* Wiring: use the faction overload `StartCombat((id, faction))`; `CombatEncounterService` and `ConditionDurationService` need the SAME non-null `GameEventBus` (else Reckless never expires); `AbilityResolutionService` needs the bus (death events) and `EncounterFactionProvider`.
* Assemblies: `VikingRaid.Game` gains CRPG.Rules/Rules.SRD5/Abilities/AI/Combat; the authoring script lives in a NEW asmdef'd editor folder `Assets/Content/Editor` (the existing `Assets/Editor` stays asmdef-less); new `VikingRaid.EditModeTests`
  (Editor-only, loads the authored assets with AssetDatabase); fake position provider + movement controller written locally.
* `FeatureModifier`, `ResourceGrant`, `StatScoreEntry` have no constructors/setters: boxed-reflection authoring (as BarrowsEndGearAuthor). Ids are global across types; a duplicate throws; an unresolvable featureId is silently skipped.

### 12.3 New gates (replace Section 8's bands)
Per encounter over N=400 seeded fights, party scripted policy, downed = full HP lost:
| Encounter | Win | Mean party HP lost | P(>=1 hero downed) |
|---|---|---|---|
| E1 3 Grunts | >= 99% | 5-25% | <= 10% |
| E2 2 Warriors + Archer | >= 97% | 15-40% | 10-40% |
| E3 2 Warriors + 2 Archers + Shaman | >= 90% | 35-60% | 30-75% |
| E4 boss (Troll + King + Shaman; the 2 Grunts dropped) | 55-80% | -- | -- |
Also: mean HP lost monotonic E1 < E2 < E3 < E4; mean rounds <= 14; cap 40 rounds counts as failure; "no hero takes > 60% of damage" measured aggregated over N and only for E2-E3.

### 12.4 Revised E4 starting numbers (tune with the sim)
Troll lvl 5: HP 50, AC 15, Str 16 (+6 to hit), Rend 2d6+3 x2 attacks; King lvl 5: HP 45, AC 17, Str 14 (+5), Greatsword 1d8+2 x2; Shaman as before. Review's hand estimate: foe HP 117, ~6.4 combat rounds, foe DPR ~26.5 vs a party pool of ~146 effective HP.

### 12.5 Review's hand calculation (kept for the record)
Party DPR vs AC 13/15/17 ~ 21.8 / 19.1 / 16.2; party effective HP ~146 (115 + Second Wind 8.5 + 3 Verses 22.5). E1 foe HP 21 (~1.3 rounds), E2 39 (~2.3), E3 70 (~3.9), E4 (original) 161 (~8.5).
Both sides close ~14 m a round, melee contact in round 2, ranged fire from round 1.

