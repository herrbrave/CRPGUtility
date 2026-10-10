# Viking Raid -- Implementation Plan (v2: Opus-reviewed, awaiting user approval)

Status: v2 2026-10-04. Opus review folded in (Section 9 SUPERSEDES conflicting text above). Not yet approved by the user. No project, import or code exists yet.

## 1. Goal and user-confirmed scope

Four Viking warriors travel the dangerous woods to destroy a goblin camp full of nasty enemies. A "quick game"
that dogfoods the CRPG Toolkit in **3D** with a real Synty art set (the user bought it in a Humble Bundle).

User decisions (2026-10-04, answered via AskUserQuestion; all three were the recommended option):
1. **Scope: ONE scene** -- a pine-forest path leading to the goblin war camp. About 10-15 minutes of play.
2. **Party: four distinct archetypes** on the existing SRD5 content, differing by data-authored abilities.
3. **UI: the toolkit's default HUD.** The Apocalypse HUD package is NOT used (and so is not imported).

Standing process (memory `feedback-design-opus-review-workflow`): plan -> Opus hostile review -> user -> implement one
task at a time, tests first, verify with real Unity runs, stop at each gate. **Commit only when the user asks.**

## 2. Source assets (surveyed, NOT yet imported)

Packages live in `thirdpartyassets/` (untracked). Survey method: extracted the `pathname` entries of each
`.unitypackage` (scratch, outside the repo). Counts are prefab/model counts from those listings.

| Package | Relevant content |
|---|---|
| POLYGON Viking Realm | `Prefabs/Characters/SM_Chr_Warrior_Male_01/02`, `Warrior_Female_01/02` (+ Leader, Peasant); `Chr_Attach` (40); weapons (Axe 1-4, Sword 1-4, Short sword, Spear, Hammer, Knife, Bow_Rigged, Shield + ~80 shield sets); env: `SM_Env_Tree_Pine_01..06` + `Pine_Group_01..03`, Rock_Cliff, Stone, Stone_Engraved, Mountain, Snow_Hill/Mound/Edge, Bushes, Icicles; Buildings 103; Props 219. |
| POLYGON Goblin War Camp | Buildings: Log_Wall (14), Log_Wall_Stake, Gate (2), Tower (+Base/Top), Tents (Basic/Leader/Medium/Small/Fish/Platform), HeadQuarters, Mess_Hall, Barracks, Shaman hut, Workshop, Mine; Props 209 (fire pits, effigies, head spikes, defence spikes, drums, crates, cages); Env 153 (Road_Planks tiles, Ground_Dirt/Grass/Marsh, Rock, Tree_Marsh, Mud_Cliff, Bones, Mushrooms); Vehicles (Cart, Ballista, Trebuchet, BatteringRam, Wagon_Cage); Characters (15 prefabs: Archer_F/M, BeastTamer, Cook, King_01/02, Knight, Prisoner x3, Ranger, Shaman, Troll, Warrior_Male, Wizard); Weapons 30 (Cleaver, Club x4, Dagger, Pickaxe, Spear, Staff, Sword, Bow, Bomb, Shield). Whether these 15 are goblin-faced is UNVERIFIED (names only) -- must be rendered. |
| SIDEKICK Goblin Fighters | 10 ready prefabs (`GoblinFighter_01..05`, `GoblinSpecies_01..05`), each with a generated Humanoid avatar asset + baked mesh asset + material. Definitely goblins. |
| ANIMATION Goblin Locomotion | Humanoid (`animationType: 3`) FBX clips only: Idles, Walk/Run/Crouch/Shuffle/Turn/Jump, Idle-to-Walk/Run transitions, Look additives; Polygon (`A_POLY_GBL_*`) and Sidekick (`A_MOD_GBL_*`) variants; `_RM` = root-motion twins. **No attack / hit / death clips.** |
| POLYGON Particle FX | `FX_SwordSlash_01`, `FX_SwordStab_01`, `FX_Slash_01/Large`, `FX_Impact_*`, `FX_BloodSplat_*`, `FX_Heal_*`, `FX_Fireball*`, `FX_Fire_*`, `FX_Embers_01`, `FX_Snow_01`, `FX_Blizzard_Snow_01`, `FX_Fog_*`, `FX_Fireflies_01`, `FX_Leaves_*`, `FX_Smoke_*`, shards, etc. Many modern items (guns, nukes) are irrelevant. |
| INTERFACE Apocalypse HUD | uGUI/TMP, post-apocalyptic. **Not used.** |

Shared `PolygonGeneric` content (shaders, skybox, generic props) is duplicated across the three Polygon packs.
Rig facts (from `.meta`): Viking `VikingRealm_Characters.fbx` and Goblin `Characters.fbx` are `animationType 3`
(Humanoid), `avatarSetup 1` (create from this model). Polygon materials use Shader Graph shaders
(`Generic_Basic`, `PolygonShader`, ...), authored for Unity 2022.3 -- URP-compatibility under Unity 6.6 / URP 17.6 is UNVERIFIED.

## 3. Toolkit facts this plan leans on (read from source 2026-10-04 -- reviewer: re-verify)

* `crpg-toolkit/Samples~/TacticalEncounter` is a working 3D turn-based fight: `TacticalEncounterBootstrap`
  (builds `GameSession` via `GameSessionHost.StartSession(SRD5RulesetProvider, ...)`, spawns combatants through
  `WorldSpawnService.BindRestoredView`, wires `AbilityResolutionService`, `ReactionService`,
  `MovementResolutionService`, `CombatEncounterService`, `TacticalEncounterController`, `TacticalAiTurnDriver`
  with `UtilityAIDecisionProvider`, `AbilityTargetingGate`, `TacticalPlayerMoveAdapter`, `TacticalCombatUIBridge`,
  `TurnOrderView`, `AbilityBarView`, `CombatLogPanel`). Foes' "Melee Strike Nearest"/"Close Distance" behavior
  curves are authored in `TacticalEncounterContentAuthor` (attack must structurally outscore movement).
* Combatant prefab shape (`TacticalEncounterPrefabAuthor`): CapsuleCollider + kinematic Rigidbody + NavMeshAgent +
  `NavMeshCharacterMotor` + `FacingController` + `CharacterAgent`, child `VisualRoot` with `CharacterView`, child `Model`.
* `BasicCRPG` sample: party spawn via `CharacterAgent.Bind`, `PartyMovementService` + `PartyExplorationController`
  + `WedgeFormationSolver`, `InteractionControllerBehaviour`. `Runtime/Camera` has `IsometricCameraRig`;
  `Runtime/Exploration` has `PartyCameraFollow(+Behaviour)`.
* BarrowsEnd (2D campaign, project-local code in `BarrowsEnd/Assets/Runtime/Gate/`): `EncounterTrigger`
  (one-shot trigger that spawns hostile `NpcDefinition`s via `WorldSpawnService.SpawnNpc`, checkpoints, starts
  `TacticalCombatSession`, records Timeline flags) and `TacticalCombatSession` (a multi-encounter wrapper over the
  same stack as the sample, with MovementPerTurn, Action/BonusAction resources, death-save handling for downed
  party members, win-condition polling, `StartBlocker`). Both are tied to BarrowsEnd's `GateCommandSource`.
* `NpcDefinition` is a complete flat stat block (ability scores, resources, features, abilities, spellcasting stat,
  faction, `VisualPrefab`) usable for monsters AND for pre-built "PC-like" characters.
* Animation path: `AbilityDefinition.AnimationCueId` -> `CharacterAnimationPresentationHook.RequestActionCue` ->
  `CharacterPlayableAnimator.RequestAction`, with `CharacterAnimationProfile` (cue ids -> clips) per character.
* Death: `TacticalEncounterController` tears the live agent down on `CreatureDied` (dead = gone). The sample treats
  0 HP as instant death; BarrowsEnd uses `DeathSaveService` + a no-permadeath rule.
* `HealEffect`, `ApplyConditionEffect`, `RestoreResourceEffect`, `AttackRollEffect`, `SavingThrowEffect` exist.
  Extra Attack does NOT exist (one attack per Action). Advantage/disadvantage sources and real `ConditionDefinition`s
  work (Tier 1/2 rules phases).
* `TestProject/Assets/ThirdPartyAssets/Universal Animation Library[Standard]/.../Unity/UAL1_Standard.fbx` (Quaternius,
  CC0, tracked in git) has `Armature|Sword_Idle`, `Idle_Loop`, `Walk_Loop` etc. but is imported **Generic**
  (`animationType 2`) in TestProject. Whether it has hit/death clips and whether it retargets as Humanoid is UNVERIFIED.

## 4. Design decisions (proposed)

**D1. New sibling project `VikingRaid/`** (Unity 6000.6.0f1), referencing the toolkit as a local package exactly like
BarrowsEnd/ConsumerGame (`file:../../crpg-toolkit`). Skeleton copied from `ConsumerGame` (`Packages/manifest.json`,
`ProjectSettings`, URP global settings, default volume profile), stripped of Fate-specific content. Rationale: keeps ~1 GB of
Synty art out of TestProject; mirrors the established consumer-project pattern.

**D2. Synty art is never committed.** Add `VikingRaid/Assets/Synty/` and `thirdpartyassets/` to `.gitignore`. A committed
Editor menu item `Viking Raid/0. Import Synty Packages` imports the five used packages from `thirdpartyassets/` with
`AssetDatabase.ImportPackage(path, false)`, so the project is reproducible from the user's own copies. (Apocalypse HUD
skipped. Repo remote is GitHub; Synty licences forbid redistributing raw assets.)

**D3. Heroes and goblins are all `NpcDefinition`s**, spawned through `WorldSpawnService.SpawnNpc` and bound as party
members -- one authoring path for both sides. Heroes use the SRD5 ruleset provider (AC / attack bonus / saving throw /
damage rules) and the SRD5 Strength/Dexterity/HitPoints/Action/Reaction content, like the TacticalEncounter sample.
No `CharacterBuildService` (full Fighter build) -- not needed for pre-made heroes.

**D4. Four hero archetypes (Viking Realm warriors), level-3-ish numbers, one attack per turn:**
 * *Bjorn* (Male_01, sword + shield): highest AC and HP; Strike (d8+3); Second Wind-style self-heal.
 * *Ulf* (Male_02, great axe): lowest AC; biggest hit (d12+4); **Reckless Attack** (own advantage + advantage against
   him, via a real `ConditionDefinition` -- shows off Tier 1/2 rules).
 * *Sigrid* (Female_01, throwing axes/bow): ranged Strike (range ~12).
 * *Astrid* (Female_02, skald): weak Strike + **Rallying Verse** (`HealEffect` on an ally in range).

**D5. Goblin roster (Sidekick prefabs for the grunts; Goblin War Camp characters only if render-verified as goblins):**
 Goblin Warrior (melee), Goblin Archer (ranged), Goblin Shaman (ranged "Firebolt" via spell attack), Troll (mini-boss,
 high HP, single big hit -- no multiattack), Goblin King (boss). SRD-flavoured numbers, tuned by headless simulation.

**D6. Enemy AI = existing `UtilityAIDecisionProvider` + authored `BehaviorSetDefinition`s** (Melee set like the sample;
Ranged set where the Move behavior's distance curve bottoms out at the ranged ability's range -- the Move-vs-Act
lesson from the AI Combat Demo, memory `project-crpg-toolkit-spec`). No new AI code.

**D7. Encounters (4 fights, one scene):** E1 woods ambush (3 warriors), E2 woods ambush (2 warriors + archer),
E3 camp gate (warriors + 2 archers), E4 camp heart (King + Troll + Shaman + 2 warriors). Triggered by proximity volumes
along the path. Exploration is free click-to-move party control; combat is the toolkit's turn-based tactical mode.

**D8. Combat/exploration glue is project-local in `VikingRaid/Assets/Game/`** (small `VikingBootstrap`,
`VikingEncounterTrigger`, `VikingCombatSession` adapted from the TacticalEncounter sample's wiring and BarrowsEnd's
`TacticalCombatSession`/`EncounterTrigger`, minus the 2D `Gate*` coupling). Whether to later promote the shared parts to
`crpg-toolkit` is a follow-up, not part of this game. (BarrowsEnd's copies are NOT referenced -- different project.)

**D9. Weapons are attached statically** in the character prefabs (weapon prefab parented under the hand bone) rather than
through the equipment-visual system (`EquipmentVisualBinder` Rigid sockets have no real content yet; not worth being the
first consumer in a "quick" game).

**D10. Animation:** locomotion (idle/walk/run) from the Goblin Locomotion pack (Humanoid, retargets to Vikings and goblins).
Combat cues (attack / hit / death) from the Quaternius UAL re-imported **Humanoid** from a *copy* of the FBX in
`VikingRaid` (CC0, copied not modified in place). **Spike 0 decides go/no-go.** Fallback if retarget fails: no combat
clips -- a code-driven lunge toward the target + `FX_SwordSlash_01`/impact FX + a topple-over death (still driven through
the same `AnimationCueId` hook where possible).

**D11. Defeat = reload the pre-encounter checkpoint** using the toolkit's existing save/checkpoint path as BarrowsEnd does
(`RequestCheckpoint` before an encounter; wipe -> reload). Party is downed-not-dead at 0 HP (BarrowsEnd's no-permadeath
rule, `DeathSaveService`) so a fight is lost only on a wipe. **Open question for review** (see section 8).

**D12. World:** flat ground plane + Polygon ground/road pieces for the path, procedurally scattered pines and rocks along
the path edges, NavMesh via `NavMeshSurface`, a camp built from goblin-pack buildings/props, dusk lighting, Synty skydome,
`FX_Snow_01` / fog / embers / fire at the camp. Everything produced by **one** Editor authoring script (project convention:
no long chains of live `eval_file` calls).

**D13. Verification:** batch EditMode/PlayMode tests in `VikingRaid.*Tests` assemblies; a headless auto-play simulation that
drives all four encounters with a scripted hero brain to prove each is winnable and not trivial (the throwaway-sim lesson);
RenderTexture captures (no `-nographics`) for visual review; a final user playthrough gate. Standing batch rules from
`project-crpg-unity-verification-setup` apply (explicit `-projectPath`, never kill a user Editor, no `-quit` with `-runTests`).

## 5. Task breakdown

**Task 0 -- Project + import + spikes.** Create `VikingRaid/` from the ConsumerGame skeleton; import the five packages via the
committed menu script; fix whatever the import breaks. Spikes with go/no-go: (a) render the 4 Viking warriors, 10 Sidekick
goblins and the 15 War Camp characters into a contact-sheet PNG (answers "are those goblins?" and "do Synty shaders render
under URP 17.6"); (b) re-import a COPY of `UAL1_Standard.fbx` as Humanoid -- does the avatar configure, do Sword attack /
hit / death clips exist, do they play correctly on a Viking and a Sidekick goblin; (c) confirm Goblin-locomotion clips play
on a Viking. GATE: user looks at the contact sheet + a retarget capture.

**Task 1 -- Characters.** Combatant prefabs (sample's component stack + Synty Model + static weapons) for 4 heroes and
the goblin roster; `CharacterAnimationProfile`s (idle/moving + attack/hit/death cues); `AnimationCueId` on abilities.
Test: PlayMode -- each prefab spawns, binds, plays idle/move/attack cue without exceptions; capture of each.

**Task 2 -- Content + balance.** `NpcDefinition`s, hero/goblin abilities (Strike variants, Reckless Attack, Rallying Verse,
Firebolt, ranged shots), conditions, `BehaviorSetDefinition`s, Movement resource. Headless simulation of E1-E4 with a scripted
hero brain; tune until all four are winnable but E3/E4 are genuinely dangerous. Test-first for each ability's numbers.

**Task 3 -- World.** One authoring script builds the ground, path, forest scatter, clearings, camp, lighting, FX, NavMesh,
spawn anchors and trigger volumes. Test: NavMesh path start -> each clearing -> camp heart exists and is complete.
GATE: user reviews renders of start, ambush clearing, camp gate, camp heart.

**Task 4 -- Game loop.** `VikingBootstrap` (session, party spawn, exploration controller, camera, HUD regions),
`VikingEncounterTrigger` + `VikingCombatSession`, victory flow (boss dead -> "Camp destroyed" screen), defeat reload,
combat FX hooks (slash/impact on hit, heal FX, death). Test: scripted PlayMode run walks the party, fires E1, wins it,
continues; defeat path reloads.

**Task 5 -- Playthrough gate.** Full scripted run through all four encounters in PlayMode; user plays the scene start to
finish; fix what they find. Append Results to this doc.

## 6. Risks and unknowns (each has an owner task)

1. Synty shader graphs under URP 17.6 / Unity 6.6 may render pink or need material fixes (Task 0).
2. UAL as Humanoid may not retarget cleanly; no combat anims otherwise (Task 0, fallback in D10).
3. The 15 War Camp characters may not be goblins (Task 0); Sidekick set is the safe goblin source.
4. Import time and Library size (~1 GB compressed packages, three overlapping PolygonGeneric copies) (Task 0).
5. Generic `CharacterView`/`CharacterPlayableAnimator` has only been exercised with Quaternius Superhero models (Task 1).
6. Ranged AI (archer/shaman) has never been authored against `UtilityAIDecisionProvider` in this repo (Task 2 -- sim).
7. `TacticalEncounterController` / `AbilityTargetingGate` on a 40m+ forest NavMesh with many tree obstacles: NavMesh
   baking cost and agent pathing around trees are unproven at this scale (Task 3/4).
8. No Extra Attack: Troll/King cannot multiattack; compensated with HP and damage (Task 2).

## 7. Out of scope

Save/load UI, inventory/loot, dialogue, quests, character creation, multiple scenes/travel, Apocalypse HUD reskin, equipment
visual system, sound, bespoke attack animations beyond UAL, any change to `crpg-toolkit` unless a genuine toolkit bug is
found (then fix at the root with a test, and flag it).

## 8. Questions for the reviewer

* Is D11 (checkpoint-reload on wipe + downed-not-dead) the cheapest correct choice, or is "restart the encounter from an
  in-memory snapshot" or "just reload the scene" simpler and safer? What does the real save/checkpoint API need in a
  one-scene 3D game that has no `SessionLoadCoordinator` wiring yet?
* Does copying the ConsumerGame project skeleton carry hidden Fate/consumer coupling?
* Is anything in Section 3 wrong or stale? Anything in the task order that will bite?

## 9. Opus review record and v2 revisions (2026-10-04) -- SUPERSEDES Sections 3-6 where they conflict

Review was read-only and source-verified. I independently re-checked three of its claims before accepting them: the
12-package count (TRUE -- six more packages appeared in `thirdpartyassets/` after my first survey), the
`CharacterRigValidator` Humanoid hard-error (TRUE, `Editor/Validation/CharacterRigValidator.cs:152-167`), and the shipped
`condition-reckless` advantage sign (TRUE -- AttacksAgainst modifier is op 7 = Disadvantage, so attackers get disadvantage).
The rest is the reviewer's, cited to file:line in its report, and gets re-verified at implementation time.

### 9.1 Blockers found, and the fix adopted
1. **ConsumerGame skeleton is wrong (D1 replaced).** Dangling URP asset GUID (lives only in TestProject), no particle-system /
   imageconversion / audio / terrain / wind modules, Gamma colour space, ConsumerGame product name + build scene.
   NEW D1: start from BarrowsEnd's full manifest minus 2D packages, pin `com.unity.shadergraph 17.6.0`, create a fresh URP
   asset + 3D UniversalRenderer (or copy TestProject's `CRPG_URPAsset`/`CRPG_UniversalRenderer` with `.meta`), **Linear**
   colour space, `VikingRaid.unity` in Build Settings, `activeInputHandler: 2`, own productName.
2. **Animation is not wired in 3D.** The sample passes no `IActionPresentationHook`; only the source attack cue ever fires,
   after damage is applied; nothing drives hit/down/death/recover. Fix: pass `CharacterAnimationPresentationHook` to
   `TacticalEncounterController`; write a project-local `ModelActionCues` (AfterDamage -> hit, CreatureDroppedToZeroHP ->
   `RequestTerminal`, stabilised/HP>0 -> `Recover()`) plus an `IDefeatPresentation` (death clip, corpse stays); delay
   hit/death ~0.35 s so the swing lands; set ability `RecoverSeconds` >= attack clip length.
3. **Humanoid vs the toolkit own finding (D10 reworked).** `CharacterRigValidator` and Gate A document that Humanoid
   avatars through `PlayableCharacterAnimationDriver` corrupted a pose -- but the root cause was GENERIC clips on a Humanoid
   Animator. Humanoid clip -> Humanoid avatar through `AnimationClipPlayable` is standard Unity retargeting, but this repo has
   NEVER run it, and every Synty source here is Humanoid. Spike 0 must play clips through the REAL `CharacterPlayableAnimator`
   in a PlayMode test with continuous `graph.Play()` and an upright-pose bounds check (single-frame samples do not count). Never run
   `CharacterSetupWindow` validation on these wrappers (it hard-errors); the validator rule is over-broad -- flag as a toolkit
   follow-up, out of scope. UAL clips confirmed from the FBX: `Sword_Attack`, `Hit_Chest`, `Hit_Head`, `Death01`,
   `Spell_Simple_Shoot`, `Punch_*`, locomotion. **No bow or throw clips** -- Sigrid ranged shot reuses `Spell_Simple_Shoot` or
   the attack clip.
4. **BarrowsEnd `TacticalCombatSession` is not "the same stack as the sample".** It builds `AbilityResolutionService` with no
   attack/AC/save/resistance rules (so every `AttackRollEffect` ability is rejected), no `ReactionService`, explicit turn order.
   D8 revised: take rules wiring from the sample (`TacticalEncounterBootstrap.cs:117-136`) plus `EncounterFactionProvider`
   from BarrowsEnd.
5. **BarrowsEnd downed-not-dead logic can soft-lock** (a stable 0-HP hero still counts as alive and cannot be damaged;
   `HealEffect` never clears DeathSaves/Unconscious). Fix: "down" means `HP <= 0`; each tick, any party member with HP > 0 and
   (DeathSaves != null or Unconscious) is cleared + `ConditionRemoved` + `Recover()`; full-heal/revive everyone at victory;
   construct `DeathSaveService` ONCE per session (no Dispose).
6. **D11 checkpoint-reload is far too heavy** (SaveService + SaveFileStore + SessionLoadCoordinator + rebind of every consumer
   + disarm-on-load). Replaced by a **scene reload with a static checkpoint index** (reset in
   `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`): wipe -> banner -> `SceneManager.LoadScene` -> boot spawns heroes
   at checkpoint anchor k at full HP with encounters < k disabled. No save plumbing.

### 9.2 Should-fix items adopted
* Author our OWN Reckless condition (AttackBonus Advantage + AttacksAgainst **Advantage**, `StartOfTargetTurn`, duration 1);
  do not use the shipped one. The shipped asset is a toolkit content bug -- flag it, fix with a test only if the user wants.
* Put EVERY usable ability (reactions included) in `NpcDefinition.abilityIds`; feature-granted abilities are rejected as
  `AbilityNotKnown` once KnownAbilities is non-empty.
* Per-combatant AI brains: copy BarrowsEnd ~30-line `PerCombatantDecisionProvider`. "No new AI code" was false.
  Ranged precedent exists: `HostileBrainKit.BuildRangedSet` (Move 0.6->0.1, melee 1->0.95, ranged 0.9->0.86, stopDistance 5 /
  range 6). No kiting/retreat is possible without code. Add a `TargetResourcePercent:resource-hp` consideration so the AI stops
  hitting downed heroes. Heals cannot target self.
* Wire `EncounterFactionProvider` and a project-local line-of-sight wrapper (`PhysicsLineOfSightProvider`, ~1.4 m Y offset,
  mask = obstacle colliders only); `requireLineOfSight=false` on melee.
* Input arbitration is required: port a ~90-line `GateCommandSource`-equivalent (party-only Select, combat Move override,
  Interact dropped in combat) or clicking a goblin throws in `PartyState.EnsureMember`.
* Extra Attack EXISTS (an `"AttackCount"` `FeatureModifier`, 1 + sum): Troll/King CAN multiattack (same-target swings).
  Section 3/6/D5 statements to the contrary are withdrawn.
* Prefab: the sample prefab lacks `CharacterPlayableAnimator` (must be on CharacterRoot). Use the existing
  `CharacterWrapperBuilder.BuildOrUpdate(CharacterSetupSpec, path)` (`Editor/Character/`) instead of hand-rolling; Model must be
  the FIRST child of VisualRoot; tests wait one frame for the nested-prefab `Start()` fallback.
* Viking warrior prefab Animator has `applyRootMotion=1`, `cullingMode=CullUpdateTransforms`: override to no root motion; the
  prefab is the whole FBX with ~20 variant SkinnedMeshRenderers toggled (hair/beards/helmets are toggles). Strip colliders and
  Animators from attached weapons (`Bow_Rigged` has both). Find hands via `Animator.GetBoneTransform(HumanBodyBones.RightHand)`.
* Multi-encounter safety: dispose `TacticalCombatUIBridge`, unsubscribe `inputSource.Move`, create HUD regions once and reuse
  (BarrowsEnd `TacticalCombatSession.cs:330-363` is the teardown model); halt party motors when combat starts.
  `CombatLogPanel`/`SampleCombatLog` are SAMPLE-ONLY (`Samples~`), not toolkit; all sample content assets live in `Samples~`
  and are invisible to a consumer -- author everything locally. Toolkit HUD = `HudRoot` + `AbilityBarView` + `TurnOrderView`.
* Synty import side effects: `SyntyPackageHelper` is `[InitializeOnLoad]` and shows modal dialogs / edits the manifest -- pre-pin
  shadergraph to suppress; Sidekick ships `ToolDownloader` (ignore); Goblin Locomotion sample scripts `GBL_*.cs` (Input System)
  compile into Assembly-CSharp -- exclude if they break; import packages OLDEST-first (PolygonGeneric/helper are duplicated with
  the same GUIDs across 2020/2021/2022 builds).
* NavMesh: bake with `NavMeshSurface` (`useGeometry=PhysicsColliders`), trunk-only capsule colliders on an Obstacle layer, canopies
  collider-free; NO `NavMeshObstacle` carving for hundreds of trees. Verify with `SamplePosition`/`CalculatePath`.
* Pre-spawn each encounter goblins idle at boot (stable spawnIds) so they do not pop in at trigger time.
* Include BonusAction in the `TurnResourceSet` and grant it (Second Wind costs it). Author a shorter Shaman bolt, set
  `spellcastingStatId` + `level`.
* Decide between-fight healing BEFORE Task 2 balance: **full heal + revive after every win.**
* Verification: balance sim = EditMode math sim (resolution + encounter + death saves, positions ignored) + one PlayMode NavMesh
  smoke test; scene in Build Settings for PlayMode loads; throwaway-copy verification would re-import ~1 GB unless `Library/` is
  copied too, so prefer verifying in the real `VikingRaid` project (no other Editor holds it) and avoid the copy dance.

### 9.3 New assets noticed (not in the original survey)
`thirdpartyassets/` now holds 12 packages. New: `POLYGON_NatureBiomes_AlpineMountain` (300 MB: pines `SM_Env_Pine_*`, stumps,
background trees, terrain layers -- directly useful for the woods; needs terrain + wind modules), `POLYGON_Prototype`,
`POLYGON_Shops`, `POLYGON_Shopping_Plaza_Map`, `SIMPLE_Buildings`, `SIMPLE_Props_Items_Icons`. Proposal: import Alpine Mountain
for the forest (alongside Viking Realm pines); ignore the rest.

### 9.4 Revised task order
0. Project (fixed skeleton per 9.1.1) + oldest-first import + spikes (render contact sheet; real-driver Humanoid playback incl.
   UAL clips on a Viking and a Sidekick goblin; locomotion clips on a Viking). GATE: user sees contact sheet + retarget capture.
1. Characters via `CharacterWrapperBuilder` + static weapons + animation profiles + **`ModelActionCues` / defeat presentation**.
2. Content: NpcDefinitions, abilities (own Reckless), conditions, brains (ranged set precedent), full-heal rule; EditMode
   balance sim; tune E1-E4.
3. World (one authoring script) + NavMesh checks. GATE: renders.
4. Game loop: bootstrap, per-encounter `StartEncounter` (hook, faction provider, LoS, per-combatant AI, command filter,
   down/revive sweep, victory heal, wipe = scene reload with checkpoint index).
5. Scripted full run + user playthrough gate; append Results.



## 10. Task 0 Results (2026-10-04) -- project, import, spikes: DONE, all three spikes GO

**Project.** `VikingRaid/` (Unity 6000.6.0f1) created from a hand-written manifest (BarrowsEnd modules minus 2D, URP 17.6, shadergraph
pinned 17.6.0, Input System 1.20.0, AI Navigation 2.0.14, toolkit via `file:../../crpg-toolkit`, `testables` for the toolkit).
`Assets/Editor/VikingRaidProjectSetup.cs` (menu `Viking Raid/Setup Project`) set Linear colour space and created
`VikingRaid_URPAsset` + `VikingRaid_UniversalRenderer`, assigned to GraphicsSettings and every quality level. `activeInputHandler`
was 0 (legacy) on a fresh project; set to 2 (Both) by editing `ProjectSettings.asset` while Unity was closed. No ConsumerGame
coupling. `.gitignore` now ignores `VikingRaid/{Library,Temp,Logs,UserSettings,...}`, `VikingRaid/Assets/Synty/`, `/thirdpartyassets/`
and `VikingRaid_Logs/`.

**Import.** Six packages (Sidekick, War Camp, Viking Realm, Alpine Mountain, Particle FX, Goblin Locomotion; ~1.5 GB on disk, ~16k files)
imported oldest-first with zero compile errors. Apocalypse HUD and the other five new packages NOT imported. Alpine Mountain is imported but
not yet used. `ImportSyntyPackages.RunBatch` imports ONE package per Unity launch (`-pkgIndex N`) and exits when the editor has been idle
45 s -- see gotchas below. ~2-4 min per package.

**Spike (a): render check -- GO.** `Viking Raid/Spikes/Render Character Contact Sheets` -> `VikingRaid_Logs/spikes/contact_*.png`
+ `contact_report.txt`. Shader graphs render under URP 17.6 with 0.0% magenta pixels on all 35 characters (one exception:
`SM_Chr_Leader_Female_01` renders black; unused). The War Camp characters ARE goblins: Archer_F/M, BeastTamer, Cook, King_01 (tall,
caped, crown), King_02 (squat), Shaman (horned), Troll (dark, squat, wide), Warrior_Male_01 are goblin-faced; Knight, Prisoner x3, Ranger
and Wizard are human. Sidekick: GoblinFighter_01-05 are armoured (properly nasty), GoblinSpecies_01-05 are bare grunts. Viking warriors
are idx Warrior_Female_01/02 and Warrior_Male_01 (wolf helm, red beard) / Male_02 (horned helm).

**Spike (b)/(c): Humanoid playback through the real driver -- GO.** `Viking Raid/Spikes/Author Animation Spike` + PlayMode
`VikingRaid.PlayModeTests.HumanoidPlaybackSpikeTests` (3 tests, 3/3 green): wrappers built by the toolkit's own
`CharacterWrapperBuilder`, driven through the real `CharacterPlayableAnimator`/`PlayableCharacterAnimationDriver` with continuous graph
evaluation in a real PlayMode session. Asserts, per character (Viking, Sidekick goblin, War Camp goblin): presentation binds, avatar is
Humanoid, standing head height, no root-motion drift, facing forward (shoulder-line dot, numeric), attack action completes and the hand
travels > 0.25 m (1.07-1.38 m measured), hit completes, walk enters moving with feet swinging 1.2-1.6 m, death lays the character flat
(head 7-10 cm above feet), Recover() stands them back up. Captures in `VikingRaid_Logs/spikes/anim_*.png`.

Findings (all fixed or recorded):
1. **Humanoid retargeting works through `AnimationClipPlayable`** -- the toolkit validator's "Humanoid corrupts this driver" error was
   about Generic clips on a Humanoid Animator. Do not run `CharacterSetupWindow` validation on these wrappers (it hard-errors).
2. **The UAL (Quaternius, CC0) imports as a valid Humanoid** from a COPY (`Assets/ThirdParty/UAL/`, meta copied then patched). Needed
   clip import settings or three things were wrong: root height must be baked (`lockRootHeightY` + `keepOriginalPositionY`) or a dead
   character floats at standing hip height; orientation must be `keepOriginalOrientation=false` (Body Orientation) or UAL clips turn
   Polygon-rig characters to face backwards; also baked rotation + XZ. Set in `AnimationSpikeAuthor.CopyUalAsHumanoid`.
3. **All Polygon-family characters (Viking, War Camp) share one skeleton** whose Humanoid `Hips` maps to a bone named `Root`
   (hips world y ~ 0). Posture checks use head height above the lowest foot, never the Hips bone. Sidekick uses a normal `pelvis`.
4. **The Polygon goblin idle/walk is a hunched goblin stance and plays unchanged on a Viking** -> heroes use the UAL `Idle_Loop` /
   `Walk_Loop`; goblins use the goblin pack (Polygon `A_POLY_GBL_*` clips work on Sidekick rigs too). Combat clips (`Sword_Attack`,
   `Hit_Chest`, `Death01`) come from the UAL for both. Two profiles: `SpikeHeroProfile`, `SpikeGoblinProfile` (Task 1 makes the real ones).
5. The Synty prefabs ship `applyRootMotion=1`, `cullingMode=CullUpdateTransforms`; the spike patches the wrapper Animators to no root
   motion + AlwaysAnimate (Task 1 keeps this).
6. The Viking prefab's Animator is on the model root with a standard Humanoid avatar; the wrapper binds it on frame 1 via the
   `CharacterView.Start()` fallback (tests wait 2 frames).
7. A sword lunge legitimately drops the head to ~70% of standing height (attack/hit posture check uses 50%).
8. UAL clips present: Sword_Attack, Hit_Chest, Hit_Head, Death01, Spell_Simple_Shoot (+Enter/Exit), Punch_Jab/Cross, Pistol_*, locomotion
   (Idle_Loop, Walk_Loop, Jog_Fwd_Loop, Sprint_Loop, Crouch_Idle_Loop, ...). No bow/throw clips (Sigrid reuses Spell_Simple_Shoot or the sword attack).

Gotchas learned (also recorded in project memory):
* `AssetDatabase.ImportPackage(path,false)` is deferred; `-quit` exits before it runs. An imported package with Editor scripts forces a
  recompile + domain reload that drops any managed `importPackageCompleted` handler, so the batch hangs forever. Fix: persist an
  idle-watcher via `SessionState` + `[InitializeOnLoadMethod]`, one package per launch.
* First render of a Synty shader-graph material in a fresh session is a flat WHITE placeholder (async shader compile; batch frames outrun
  real time). Do a discarded warm-up render and set `EditorSettings.asyncShaderCompilation=false` before capturing.
* `[UnityTest]` + `[TestCase]` fails ("Method has non-void return value"); write separate `[UnityTest]` methods sharing a coroutine.
* PowerShell treats `[Standard]` in the UAL folder name as a wildcard -- use `-LiteralPath`.
* Git Bash spawns too slowly for thousands of tiny files; use PowerShell for package-listing work.

**GATE (user): contact sheets and retarget captures are in `VikingRaid_Logs/spikes/` (`contact_vikings.png`, `contact_warcamp.png`,
`contact_sidekick_goblins.png`, `anim_Viking_*.png`, `anim_SidekickGoblin_*.png`, `anim_WarCampGoblin_*.png`).**



## 11. Task 1 Results (2026-10-05) -- characters: DONE (20/20 PlayMode green, no toolkit changes)

**Delivered** (all in `VikingRaid/`, nothing in `crpg-toolkit/` touched):
* `Assets/Characters/Prefabs/` -- **15 armed wrapper prefabs** built by the toolkit's own `CharacterWrapperBuilder`: heroes `Hero_Bjorn` (Viking Male_02: sword + shield), `Hero_Ulf` (Male_01 wolf hood: great axe), `Hero_Sigrid` (Female_01: bow), `Hero_Astrid` (Female_02: long spear/staff); goblins `Goblin_Grunt_A/B/C` (Sidekick species: club/dagger/cleaver), `Goblin_Warrior_A/B/C` (Sidekick fighters: sword/cleaver/axe + shield), `Goblin_Archer_M/F` (bow), `Goblin_Shaman` (skull staff), `Goblin_Troll` (club; capsule r0.5 h2.2), `Goblin_King` (sword + shield). Each: CharacterRoot (capsule, kinematic rigidbody, NavMeshAgent speed 4.0 heroes / 3.0 goblins, motor, FacingController, CharacterAgent, CharacterPlayableAnimator, EquipmentVisualBinder, `ModelActionCues`) -> VisualRoot (CharacterView) -> Model. Weapons are collider-free, Animator-free static clones under the hand bone (`Weapon_R` / `Weapon_L`), or under the Sidekick `prop_r` / `prop_l` sockets.
* `Assets/Characters/Animation/HeroAnimationProfile.asset`, `GoblinAnimationProfile.asset` -- cue ids: `anim-idle`, `anim-moving`, `anim-attack-melee` (UAL Sword_Attack), `anim-shoot` (UAL Spell_Simple_Shoot; bows AND spells), `anim-hit` (Hit_Chest), `anim-death` (Death01). Heroes: UAL Idle_Loop / Jog_Fwd_Loop (reference speed 4.83 m/s). Goblins: Polygon goblin Idle_Standing / Run_F (reference speed 3.0 m/s). **Task 2 abilities reference these cue ids via `AbilityDefinition.AnimationCueId`.**
* `Assets/Game/` (asmdef `VikingRaid.Game`): `ModelCueEventBinding` (pure event -> cue mapping; mirrors the toolkit's `SpriteCueEventBinding`) and `ModelActionCues` (MonoBehaviour + `IDefeatPresentation`): AfterDamage -> hit action after a 0.35 s delay (the attacker's swing lands first; repeated hits collapse; none once going down); DroppedToZeroHP/Died -> terminal pose after 0.35 s, cancels a pending hit; Stabilized/ConditionRemoved + HP > 0 -> Recover; `BeginDefeat` keeps the corpse (inert, from `WorldSpawnService.MarkDefeated`) unless `CorpseLingerSeconds` > 0; self-binds from a bound `CharacterAgent`; honours the toolkit binding contract; saved-while-down characters start down.
* `Assets/Editor/CharacterRosterAuthor.cs` (menu `Viking Raid/Task 1/Author Characters`, re-runnable, rebuilds from a fresh checkout via `AnimationSpikeAuthor.EnsureUalHumanoid`), plus tools `GripLab` (24 weapon orientations per hand, rendered), `LocomotionSpeeds`, `RigDump`, `ContactSheet`, `AnimationSpikeAuthor`.
* Tests (`VikingRaid.PlayModeTests`, 20/20): `ModelCueEventBindingTests` (6, pure), `ModelActionCuesTests` (10, real animator on the real `Hero_Bjorn` prefab with real GameEvents), `RosterPrefabTests` (1 gate test over all 15 prefabs: structure, Humanoid avatar, no root motion, AlwaysAnimate, no extra Animators/colliders under the Model, weapons under the right hand, standing, facing forward, attack/shoot/hit cues complete, death lies flat, recovery stands and faces forward), `HumanoidPlaybackSpikeTests` (3).

**Verification beyond green tests.**
* Mutation checks on `ModelActionCues`: hit delay removed, terminal delay removed, recover not reaching the animator, defeat callback never firing, down-at-bind ignored, pending hit not cancelled -- each caught by a named test. The sixth first slipped through (two redundant guards each sufficed alone, so a test could not tell them apart); a test with a longer terminal delay was added, the redundant Update-time guard was removed, and the mutation is now caught.
* Roster gate power: Hero_Ulf was deliberately corrupted four ways (collider on a weapon, root motion on, `ModelActionCues` removed, weapon moved to a foot) and the test named all four; the roster was re-authored and the test passes again.
* Visual: roster contact sheets (`VikingRaid_Logs/spikes/roster_idle.png`, `roster_attack.png`, order in `roster_order.txt`) reviewed; grips chosen from `GripLab` renders. Two fixes came from this review: Ulf's axe head hid behind his wolf hood (axe orientation changed to (0,0,90) plus a 90 deg twist about the shaft, new `Twist` grip parameter) and Astrid's 48 cm axe was invisible (she now carries a long spear).

**Measured numbers** (`VikingRaid_Logs/spikes/locomotion_speeds.txt`). Method: planted-foot slide speed on a sampled clip, validated within 7% against root-motion data (UAL Walk_Loop 0.88 measured vs 0.95 true, Jog_Fwd_Loop 5.11 vs 4.83). The Synty goblin root-motion twins report averageSpeed 0, which is why the method exists. Goblin Walk_F 1.6 m/s, Run_F 2.8-3.2 m/s.

**Findings / notes for later tasks.**
* The Polygon goblin run/idle is a deep hunch (head ~1.1 m above feet vs 1.47 m for heroes): all goblins read as scheming, short, nasty -- the Troll too (his huge mesh hunches the same way).
* Weapon metals (Viking axe heads, blades) render near-black in a bare test scene (no skybox / reflection probe): lighting and a reflection source belong in Task 3 or they will look wrong in the game too.
* Sword_Attack and Spell_Simple_Shoot are one-handed gestures: a bow is a vertical prop in the left hand while the right arm extends -- acceptable for "quick game"; no bow-draw clip exists in the UAL.
* `AnimationSpikeAuthor`'s spike wrappers (`Assets/Spike`) still back `HumanoidPlaybackSpikeTests` (walk/hand-travel coverage the roster test does not have); everything else uses the real prefabs.
* Promotion candidates for `crpg-toolkit` (the user wants these mechanisms to flow back into the utility): `ModelCueEventBinding` + `ModelActionCues` (a generic 3D combat-cue binder; shares every rule with the sprite one -- a single `CombatCueEventBinding` would serve both), the idempotent UAL-Humanoid import settings (root height/orientation baking), the grip-lab technique, and the `CharacterRigValidator` Humanoid rule, which is over-broad (it blocks a configuration this spike proved works).
* The Editor was open on `VikingRaid` at the start of Task 1; the user closed it on request (batch Unity cannot share a project). All verification ran as batch runs; `vr_tests.sh` aborts if any Unity instance has the project open.

**GATE (user): review `VikingRaid_Logs/spikes/roster_idle.png` and `roster_attack.png` (15 characters, order in `roster_order.txt`). Next: Task 2 (content + balance): NpcDefinitions, abilities (own Reckless condition), brains, EditMode balance sim.**



## 12. Bow animations (Blender) -- DONE 2026-10-05 (22/22 PlayMode green, no toolkit changes)

The UAL has no bow clips, so Sigrid and the goblin archers used a one-handed spell gesture. The user offered the Blender pipeline; we authored a real **`Bow_Draw`** (46 frames @30fps, 1.5 s: raise, nock, draw, hold, release, follow-through, lower; **release on frame 29** -- fire the projectile/VFX there) and **`Bow_Idle`** (91-frame / 3 s breathing loop; frame 1 and 91 are exactly the ready pose Bow_Draw starts and ends on).

**Source of truth (repo):** `Blender/Animations/Bow/` -- `bowlib.py` (rig-space conventions, IK rig, bake helpers), `bowanim.py` (key poses, timelines, stand-in bow, filmstrips), `01_build_bow_animations.py` (headless: UAL FBX -> two baked clips -> skeleton-only `export/BowAnimations.fbx`, 1.3 MB), `renders/` (review filmstrips). Run: `"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --python Blender/Animations/Bow/01_build_bow_animations.py`. Unity side: `VikingRaid/Assets/Editor/BowAnimationImport.cs` (copies the FBX to `Assets/Characters/Animation/Source/`, Humanoid, root height/XZ baked, root rotation Original + **180 deg offset**), `CharacterRosterAuthor` (two new profiles `HeroBowAnimationProfile` / `GoblinBowAnimationProfile`: `anim-shoot` = Bow_Draw; Hero_Sigrid also idles in Bow_Idle; Goblin_Archer_M/F keep the goblin hunch and straighten up for the shot), `Tests/PlayMode/BowAnimationTests.cs`.

**Method.** Hands are placed with IK in real rig-space coordinates (wrist target + hand orientation + elbow), then BAKED to plain FK keys (65 bones; baked vs live IK differ by 0.0 m), so the FBX carries nothing but bone rotations. The left-hand orientation keeps a bow vertical (fingers forward, palm to the midline, thumb up). Elbows are placed analytically (two-bone solve; at full draw the wrist is ~0.2 m from the shoulder, where an ordinary IK pole is wildly sensitive). Chest turns -45 deg at full draw and holds through release; the head counter-turns split 40/60 over neck and head; the bow hand sits on the SIGHT LINE (not straight out from the shoulder, which aimed ~22 deg off the head's direction).

**Review loop.** First pass (elbow floating over the head, forearm across the chest) fixed with analytic elbows + a turned torso. An Opus animator review of the baked filmstrips then found a blocker (bow plane 17 cm left of the string hand), a squared-up torso, a palm-up "ta-da" release, a dead hold and a static idle; revision 2 applied the geometry fixes, a hold that breathes, a quick 2-frame snap, staggered limb timelines with arc keys (a straight lerp shortens the arm ~5 cm mid-raise), a bow dip on release and a bigger, seam-free idle. Not applied: leg/knee softening, a quiver reach before the nock, clavicle retraction.

**Verification (Unity, real driver, real Synty characters).** Hold-pose measurements for Hero_Sigrid / Goblin_Archer_M: legs face forward 0.99 (min over the clip 0.99 / 0.94 while crossfading out of the hunch), arrow line (string hand -> bow hand) vs character forward **1.00**, string hand 0.16 / 0.24 m from the head bone, bow arm forward 0.63 m, bow near-vertical 1.00 / 0.99. The two original failures were the test's evidence, not guesses: with root offset 0 the legs read -0.99 (character turned 180 deg), and with the old hanging-hand grip the bow read 0.24-0.37 vertical. The grip itself was calibrated by the test (it computes the hand-relative rotation that makes the bow vertical at the hold): Polygon-rig left-hand bow grip is now Euler (13, 106, 97) -- also correct for the War Camp archers (same skeleton). Side strips: `VikingRaid_Logs/spikes/bow_Hero_Sigrid_side.png`, `bow_Goblin_Archer_M_side.png`.

**Known gaps (polish, not blockers).**
* The Synty rigged bow's STRING is not driven: the hand pulls to the jaw while the string stays at the bow. A small `LateUpdate` driver moving the string bone toward the right hand during frames ~9-29 would sell it (needs the bow's bone names and a time source for the shoot cue).
* No arrow, no projectile: Task 4 should spawn it on frame 29 (animation event or timer from `RequestAction`).
* The bow grip is calibrated for the bow-hold poses; during Jog / hit / death the left-hand orientation differs, so the bow tilts (a "bow-carry" jog could be authored with the same Blender pipeline).
* Elbow room: the drawing elbow (~1.55 m high, behind the head) may clip chunky hoods/helmets on some Synty characters; check in the real scene.
* Retarget risk noted by the review: hand-to-face distance depends on each character's head/beard; an optional IK-to-anchor on the string hand would remove it.



## 13. Task 2 Results (2026-10-05) -- stats, abilities, enemy behaviour, balance: DONE (46/46 EditMode green, no toolkit changes)

Design and the Opus review record: `VikingRaid-Task2-ContentAndBalance.md` (Section 12 supersedes the text above it). Everything below is verified by batch EditMode runs on the real engine (`vr_edit.sh`).

**What exists.**
* `Assets/Game/GameDesign.cs` -- the single design table (units, abilities, brains). `UnitSpec.ArmorBonus`, `ExpectedAttackBonus` etc. are derived from it, so the authoring script and the tests read the same numbers.
* `Assets/Game/VikingRaidRulesetProvider.cs` + `Assets/Content/Ruleset/VikingRaidRuleset.asset` -- the game's `IRulesetDefinition` (SRD5 + 58 extra definitions).
* `Assets/Content/Editor/GameContentAuthor.cs` (menu `Viking Raid/Task 2/Author Content`; batch: `-executeMethod VikingRaid.ContentEditor.GameContentAuthor.Run`) -- authors resources, `condition-vr-reckless`, armour + multiattack features, 14 abilities, 15 NpcDefinitions, 6 goblin behaviour sets and the ruleset. Re-run it after ANY change to `GameDesign.cs`.
* `Assets/Game/VetoZeroScoreDecisionProvider.cs` -- see "Toolkit finding" below.
* `Assets/Tests/EditMode/` (46 tests): `ContentIntegrityTests` (11), `DesignNumbersTests` (6: AC, HP, level, to-hit and attack count computed by the real SRD5 rules vs the table), `AbilityRulesTests` (7), `AiBehaviorTests` (21), `BalanceSimulationTests` (1, 1600 seeded fights); shared harness `Arena.cs` (real encounter/ability/movement/condition/death-save services, fake positions + mover, goblins driven by the EXACT `TacticalAiTurnDriver` loop, scripted hero policy).

**Final numbers (changes from the Section 12 starting values in bold).** Heroes L3: Bjorn 31 HP AC18, Ulf 34 HP AC14, Sigrid 26 HP AC15, Astrid 24 HP AC13. Goblins: Grunt L1 **9 HP** AC13 cleaver d6+2; Warrior L2 **20 HP** AC17 **Str 18** sword d8+**3**; Archer L1 9 HP AC14 shortbow d6+**3** (range 12) + dagger; Shaman L3 22 HP AC13 Fire Bolt **1d8** (spell attack, Wis 16, range 12); Troll L5 50 HP AC15 Rend 2d6+3 x2; King L5 45 HP AC17 greatsword 1d8+2 x2.

**Balance (N=400 seeded fights per encounter; downed hero = full HP lost; all Section 12 gates met).**

| Encounter | win | HP lost | P(>=1 downed) | mean rounds | gate |
|---|---|---|---|---|---|
| E1 3 Grunts | 100% | 6.3% | 0% | 2.9 | win >=99, lost 5-25, downed <=10 |
| E2 2 Warriors + Archer | 100% | 19.4% | 11.5% | 4.8 | win >=97, lost 15-40, downed 10-40 |
| E3 2 Warriors + 2 Archers + Shaman | 93.3% | 46.5% | 59.3% | 7.4 | win >=90, lost 35-60, downed 30-75 |
| E4 Troll + King + Shaman | 66.3% | 70.6% | 88.3% | 10.0 | win 55-80 |

HP lost is monotonic (6 < 19 < 47 < 71), no stalemates, no goblin ever attacks a downed hero while another stands, and no hero takes >60% of the damage in E2/E3 (Astrid is the AI's favourite at 32-41%; Sigrid 1-6%). Margins are thin in places (E1 HP lost 6.3 vs floor 5, E2 downed 11.5 vs floor 10): the sim is seed-deterministic so it will not flake, but any change to rules, policy or numbers should re-run it and may need a nudge. The hero policy is scripted and competent, not optimal; a human will do better or worse, so treat these as relative difficulty, and expect E4 to need a live-play pass in Task 5.

**Tuning history (what the sim drove).** Starting values missed E1-E3 slightly soft (HP lost 4.8 / 13.8 / 34.8%, E2 downed 4%). Grunt HP 7->9; Warrior HP 15->17->20, Str 14->16->18, sword +2->+3; shortbow +2->+3; Fire Bolt d10->d8 (to give E3 back win rate the warrior buffs took; E3 win had fallen to 91.3%).

**Toolkit finding (promote later).** `UtilityAIDecisionProvider.GetDecision` returns its best candidate even when that scores exactly 0 (`score > bestScore` from -infinity). In Viking Raid the HP consideration vetoes a downed target with a 0, but a goblin that has spent its movement and has only a downed hero in reach still attacked them: 77 of 400 E4 fights, found by the sim (`farmedDowned`). The game wraps the provider in `VetoZeroScoreDecisionProvider` (a zero-score decision = end turn); the Arena uses the same wrapper; `AiBehaviorTests.AGoblinWithNoMovementLeft...` is the regression test. The toolkit fix is either "never return a zero-score decision" or a configurable minimum score; check Barrow's End's behaviour sets before changing the default.

**Other things learnt (also candidates for toolkit docs/mechanisms).** Resources are only refilled, never granted: the NPC must list Action/BonusAction/Reaction/Movement (`ContentIntegrityTests`). A bonus-action ability can be cast once per turn (the test failed until it advanced a turn). `HealEffect` restores HP only, so the "downed, not dead" state (`DeathSaves`, unconscious condition) needs a revive sweep in the game loop (the Arena has `ReviveSweep`; Task 4 needs the same). A reckless-style "advantage both ways" condition is authored as two `Advantage` modifiers (`AttackBonus` on self, `AttacksAgainst` on the bearer) and needs the same bus on `ConditionDurationService` as on `CombatEncounterService` or it never expires.

**Mutation checks (each fails the right tests, all restored).** Zero-score veto disabled -> `AGoblinWithNoMovementLeft...` fails. Bjorn's AC drifted in the table -> `DesignNumbersTests` fails (names the unit and the arithmetic). HP consideration key mistyped in the authored assets -> 22 tests fail (AI behaviour, content integrity); the typo had previously been described as the likeliest silent failure (every goblin would walk and never attack).

**GATE (user): review the final numbers and the Balance table above; decide whether E1's gentleness and E4's ~66% win (with a scripted-but-competent party) are the difficulty you want before Task 3 (world, NavMesh, lighting).**



## 14. Task 3 Results (2026-10-05) -- the world: DONE (58/58 EditMode, 37/37 PlayMode green, no toolkit changes)

Design and the Opus review record: `VikingRaid-Task3-World.md` (Section 9 supersedes the text above it; 20 findings, all adopted or answered). One command builds everything: `Viking Raid/Task 3/Author World` (batch: `-executeMethod VikingRaid.EditorTools.WorldAuthor.Run`, **without** `-nographics`: baking reflection probes needs a graphics device). About 45 s.

**What exists.**
* `Assets/Game/WorldLayout.cs` -- the single layout table (pure C#): the road as straight legs and arcs (so every station distance is real arc length), the four arenas in their own frames, trigger/checkpoint/stage/spawn geometry, `PartyOffset`/`GoblinOffset` (the formation the balance simulation was run on; `BalanceSimulationTests` now calls them, so world and sim cannot drift), `EdgeDistance`, `InCamp`. `WorldAssets.cs` (full Synty prefab paths), `WorldLayers.cs` (layers 8-12 and the masks), `WorldAnchors.cs` (typed handles for Task 4).
* `Assets/Editor/WorldAuthor*.cs` -- ground and outer scenery meshes, forest, camp, lighting, probes, NavMesh; project-local prefab variants under `Assets/World/Prefabs` (every Synty collider stripped, ours added). Output: `Assets/Scenes/VikingRaid.unity` + `Assets/Scenes/VikingRaid/` (meshes, NavMesh, materials, post profile, 3 probe cubemaps).
* Also changed (Task 1/0 files): roster prefabs now on the `Character` layer (the review found they were on Default, so no goblin could ever have been clicked or targeted); `VikingRaidProjectSetup` names the layers and switches URP to Forward+ with 3 cascades, soft shadows, 70 m shadow distance.
* Tests: EditMode `WorldLayoutTests` (10), `WorldAssetsTests` (2); PlayMode `WorldSceneTests` (14, real scene + real baked NavMesh), `WorldCaptureTests` (the visual pass), roster test extended (every prefab on `Character` and a hittable-mask ray hits it).

**The world.** Road 374 m: start clearing (r 14), woods A, E1 clearing, woods B (a 5 m squeeze), E2 clearing, woods C (goblin work appears: bones, spikes, trophies, pole torches), E3 forecourt (44 x 34), a 12 m gatehouse passage, E4 yard (58 x 34). Every arena is an open 44 x 22 rectangle (ground runs 1.5 m past each end so the eroded NavMesh still covers it). Camp: palisade (71 pieces), `Gate_01` with its doors swung open and only two post colliders, two towers, HQ at half scale behind the boss fire, tents in the 6 m side strips, braziers and 6 m pole torches, ~30 point lights. Forest: 2,346 pines (288 with trunk capsules, only within 10 m of the road), 115 undergrowth, 13 rock banks, 26 mountains on the horizon, rolling non-walkable hills (a click there hits the `Scenery` layer, so `PartyMovementService` snaps to the road). Dusk: procedural sky, 28 degree sun, trilight ambient, exp2 fog matched to the horizon, bloom/ACES/vignette volume, three baked reflection probes.

**What the review and the tests caught (each was real).** Characters on layer Default (unclickable); the Synty "skyboxes" are mesh materials; the URP asset allowed only 4 lights per object and 1 shadow cascade; the HQ is 40 m wide (placed at half scale); the first yard had a fire pit inside the open arena; checkpoints computed on the arena's straight axis fall 2-3 m off a curved road (now on the real centreline); arena ground stopping exactly at the rectangle left its outer 0.5 m unwalkable; a 5 m squeeze leaves only 0.43 m of edge clearance at +-1.5 m slots (now +-1.2 m); `ReflectionProbe.bakedTexture` does not persist in a scene without lighting data (probes are `Custom` mode with the cubemap stored on the probe; a test asserts it); tent/rock/wall tops become walkable islands without a `NavMeshModifier` (M7 below proves it: vertices at y 1.9 on roofs); the Synty fog prefabs render as flat grey polyhedra (cut); the Alpine cliffs render a saturated blue (Viking Realm cliffs used instead); an 18 degree sun made 40 m shadows streak across every arena (28 now).

**Mutation checks (each fails the right tests, all restored and re-authored).** Gate posts moved into the opening -> "no walkable road at the E4 trigger" and "0.0 m walkable through the gate". Obstacle tops not marked unwalkable -> NavMesh vertices on roofs (islands). Trees allowed in the corridor verge -> 5 failures (start slot off the mesh, an arena not walkable, a blocked sight line, tree count, a solid object between camera and hero). Layout mutations: trigger 5 m from its arena -> `e1 trigger distance`; party line at 30 m -> goblins outside the arena.

**Captures (user gate):** `VikingRaid_Logs/spikes/world_overview.png` (map, arenas outlined), `world_view_start.png`, `world_view_e1.png`, `world_view_e2.png`, `world_view_gate.png`, `world_view_gate_close.png`, `world_view_camp.png`, `world_view_camp_hq.png`, `world_view_forest_walk.png`, `world_closeup_metal.png` (Bjorn: helmet, mail and sword now read as steel, not black cutouts). Re-run: `vr_tests.sh WorldCaptureTests`.

**Notes for Task 4.** Use `WorldAnchors` (no string finds). Hittable mask is `WorldLayers.Hittable` (Ground|Character|Scenery); line of sight is `PhysicsLineOfSightProvider(WorldLayers.SightBlocker)`. The scene camera is a placeholder: the real rig must enable post-processing on its `UniversalAdditionalCameraData`. On a trigger, walk the party to `partyStage` so the fight starts from the simulated formation; ignore triggers while a fight runs (E4's trigger stands in the gate, 5 m from where E3's goblins fight). A wipe returns the party to `checkpointSlots` (on the road, >= 8 m before the trigger). The gate is static and open; the boss fire and HQ sit outside the E4 rectangle, so a hero can retreat to its east edge but not further.

**Known gaps and unmeasured.** Frame rate is not measured (2.3k pines plus ~30 point lights under Forward+; batch has no meaningful GPU timing: measure in Task 5 on the user's machine). The ground is flat colours with a road band (stylised; no ground textures). Sun/sky tuning is by eye from the captures. No sound, no gate animation, no wind on the foliage.

**GATE (user): look at the ten captures above (the overview first), and say what to change before Task 4 (the game loop).**


### 14.1 Addendum: weapon and shield grips redone (2026-10-05, found by the user in the world renders)

The user saw the weapons and shields sitting about 90 degrees wrong. They were: Task 1 chose grips by looking at ONE idle frame ("blade straight up beside the body"), which needs the wrist bent 90 degrees and swings every blade, axe and spear backward or straight up through the whole attack (the Task 1 attack sheet shows it; I missed it then).

**Fix.** `Assets/Editor/Spikes/GripSolver.cs` (menu `Viking Raid/Task 3/Solve Weapon Grips`) walks the real roster and, for every melee weapon and shield, poses the actual character through the UAL idle and all 25 sampled frames of `Sword_Attack`, tries ~1,700 hand-local orientations (30 degree Euler grid) and scores them on what a hand can do: blades and shafts lead away from the elbow in the attack (mean angle to the forearm 33-52 degrees), flats face sideways so the edge lines up with the knuckles, and at rest the tip points forward not backward or up into the face; shields are strapped on face-out, long axis along the forearm, centred on the forearm (offset solved too). 17 grips solved, each against its own rig (Polygon `Hand_R/Hand_L`, Sidekick `prop_r/prop_l`), written to `Assets/Editor/Data/WeaponGrips.json`; `CharacterRosterAuthor.Attach` applies them (bows keep their hand-calibrated grip). Order of work: run the solver, then `Viking Raid/Task 1/Author Characters`.

**Result.** The attack sheet (`roster_attack.png`) now shows every weapon extending outward along the swing and every shield on the forearm facing outward; at rest weapons hang lowered and forward (`roster_idle.png`, `world_closeup_metal.png`). 37/37 PlayMode still green. Lesson recorded: judge a grip on the animation, never on one pose.


## 15. Task 4 Results (2026-10-05) -- the game loop: DONE (78 EditMode, 54 PlayMode green, no toolkit changes)

Design and the Opus review record: `VikingRaid-Task4-GameLoop.md` (Section 6 supersedes the text above it; 18 findings, all adopted). **The game is playable: open `Assets/Scenes/VikingRaid.unity` and press Play** (left click the ground to move the party; in a fight press an ability on the bar, then click a target; End Turn ends yours; mouse wheel zooms).

**What exists (all project-local, `Assets/Game/`; the toolkit is untouched).** `VikingGame` (composition root: a real ruleset session, 4 heroes + 14 pre-placed goblins through `WorldSpawnService`, input, exploration, HUD, the loop), `VikingCombatSession` (the tactical fight: real SRD5 policies, faction provider, eye-height line of sight, brains behind the zero-score veto, animation hook, per-tick rules), `VikingCommandSource` (party-only select, input locking, wedge facing), `VikingEncounterTrigger` (polls overlap), `VikingCamera`, `CombatFx` (impacts, heals, arrows and bolts released at the animation's release frame), plus the pure rules `PartyVitals`, `TurnEndRule`, `MoveWatchdog`, `Checkpoint`. `WorldAuthor.Game.cs` authors the `Game` object (input source with `WorldLayers.Hittable`, HUD, effects, camera). Shared logic is now one definition: `PartyVitals` is used by the game, the Arena simulation and the tests.

**The loop.** Boot -> explore (formation, follow camera) -> a trigger (polled, in order only) -> staging (heroes walk to the stage anchors, input locked, 9 s timeout, warps counted) -> fight -> won: full heal and revive, `Checkpoint.Index++`, the road continues; wiped: banner, reload at the checkpoint of the next unwon fight with full HP; last boss dead: "Camp destroyed" overlay + Play again.

**Rules the review forced, each tested.** "Down" = HP <= 0 everywhere (a hero STABILISED at 0 HP is still down: no turn, no walking, never revived by the sweep); the revive sweep clears DeathSaves/Unconscious only when a heal brought HP above 0; the turn auto-ends after the Action unless a bonus ability would do something (Second Wind wounded, Verse with a hurt or downed ally in 8 m); the ability bar refreshes after every ability and is hidden on goblin turns; the targeting gate disarms every turn; a move watchdog cancels a move that cannot arrive (no progress in 1 s, speed stall, or past budget/speed + 1 s); our own move handler samples clicks onto the NavMesh and keeps clear of other combatants; downed heroes have avoidance priority 0 and idle combatants 30 vs the mover's 60 (NavMesh avoidance: the LOWER number wins).

**What running it found (evidence, not speculation).** (1) A sign error of mine: idle heroes were shoved aside by the walking hero (priorities were the wrong way round) -- caught by "another hero moved". (2) A goblin walking past shoved a downed hero ~0.7 m -- priority 0 for bodies. (3) A real fight produced a natural stall (a goblin wobbling in a crowd for the full 3 s budget): the watchdog now also judges progress toward the destination, cancelling in about 1 s. (4) Two test premises were wrong and the game was right: a "downed" hero standing at 11-15 HP had been healed by the scripted Astrid (the revive sweep working as designed), and a downed hero is legitimately mid-recover of the PREVIOUS combatant's action when his turn starts. (5) A move into another agent's body ends as Succeeded by itself, so the watchdog test freezes the agent instead. (6) Screen capture never returns in a batch run; the HUD is captured by rendering the camera and the UI panel into render textures and compositing. (7) Running the WHOLE suite together exposed what file-by-file runs hid: the game tests left the world scene loaded, and the older Task 1 tests then built a NavMesh over its static-batched (unreadable) meshes (14 errors); every class that loads the world now leaves an empty scene behind (`SceneIsolation`), and the old world click test was corrected (goblins now stand in the arenas, so a click may land on a character, never on a canopy or wall). (8) The first fight camera put the party on the screen edge (the bottom ray landed on the party line): the tactical camera now looks between the party line and the goblins.

**Mutation checks (each fails the matching test, all restored).** No stage walk ordered -> "a warp would hide a broken walk"; checkpoint never advancing -> 3 flow tests; select not party-filtered -> `PartyState` throws in the goblin-click test; downed-turn skip off -> the downed-hero test; watchdog off -> the stuck-move test; characters not in the hittable mask -> the real-click test; encounters firing out of order -> the E4 trigger test.

**Visuals** (`VikingRaid_Logs/spikes/`): `game_1_explore.png`, `game_2_staging.png`, `game_3_fight_start.png`, `game_4_arrow.png`, `game_5_hit.png`, `game_6_victory.png`, `game_7_camp_destroyed.png`, `game_8_arrow_close.png` (HUD included; re-run `GameCaptureTests`).

**Not verified, flagged for the playthrough (Task 5).** The REAL `InputSystemCommandSource` (mouse through the Input System, UI-over-click suppression) is authored but never exercised by a test: tests inject a real `PlayerCommandRouter` through the same hittable mask instead (so clicks, layers and targeting priority are covered, the Input System device layer is not). Frame rate is unmeasured (batch has no GPU timing). A fight at normal speed takes about a minute (E1). The toolkit gate picks the NEAREST character under the click, so clicking a goblin standing behind a hero can be rejected (flagged in the review; revisit after a live play). The HUD uses the toolkit's placeholder style; there is no sound, no keyboard shortcuts and no camera rotation.

**GATE (user): play it** -- boot, walk the road, fight E1..E4, try a wipe (let the goblins win) and the victory screen -- and say what to change; then Task 5 (the scripted full run and fixes).


### 15.1 Addendum: camera control (2026-10-05, from the first playthrough)

The user played it ("combat works") and found the camera could only zoom, including during fights. `CameraController` (pure rules, 7 EditMode tests) + `VikingCamera` now give the toolkit default camera keys: **W/A/S/D pan, Q/E rotate, wheel zoom (not over the HUD), Space recenter**. Panning detaches the camera until Recenter (clamped to 45 m from the walkable ground); a fight starting or ending clears the pan so the arena is framed, but the player can pan and rotate DURING a fight (tested live) and Space reframes it. The camera follows the centre of the SELECTED heroes instead of the single active hero. A controls hint is shown top right. 4 PlayMode camera tests; mutation-checked (panning that never detaches, a fight that does not reframe). Test lesson: batch frames are very fast, so camera-easing assertions wait in real time, not frames. Totals now: 85 EditMode, 58 PlayMode.


### 15.2 Addendum: zoom, facing and run sync (2026-10-05, from the second playthrough)

The user asked for a little more zoom, said click-to-move left the heroes facing the wrong way, and noticed all four jog cycles were in lockstep.

- **Zoom.** `CameraController.MinZoom` 0.5 -> 0.28 (EditMode test pins it between 0.15 and 0.3).
- **Facing: measured, not guessed.** The hip line against the root's forward showed the borrowed UAL jog turning a hero's whole body about +30 degrees off the direction of travel; the War Camp goblin idle about +19 and run about +31 (+34 on the Sidekick rig); the combat clips were fine (-4..-10). A tolerance of cos > 0.7 in the earlier roster check had hidden it. `ModelYawCorrection` (data on each prefab, written by `CharacterRosterAuthor` from `MeasuredYawOffsets`) turns the model root against the offset while idle or moving and back to zero during actions and terminal poses (eased, so no pop). After the fix every prefab's hips sit within 12 degrees of its root in all three states. Separately, a stopped hero kept whatever way his last step pointed, so `VikingGame.UpdateIdleFacing` asks each idle hero, while exploring, to face the way the party was last sent (`VikingCommandSource.LastMoveFacing`, priority Interaction).
- **Run sync.** `AnimationPhaseOffset` gives each character its own point in its idle and run loops (golden-ratio phases in spawn order, so any run of 8 characters is at least 0.08 of a cycle apart). The toolkit's playable driver keeps its playables private, so this reaches them by reflection: the one fragile spot. A PlayMode test reads the cycle positions and fails loudly if the hook stops working.
- **Tests.** `GameFacingTests` (3 PlayMode: every prefab's hip yaw while idle and jogging, heroes face the order after arriving, cycle positions spread) and `PresentationFixesTests` (3 EditMode: yaw target rule, phase spread, zoom bound). The two diagnostic tests were removed.
- **Flaky tests fixed on the way.** The router-click test read a goblin's position after its own hit had killed and despawned it; the melee FX test retried a swing in the same turn although the Action ends the turn (it now waits for Bjorn's next turn per attempt). One intermittent failure, `ADownedHeroRolls...` "the fight ended before the downed hero had his turns", was seen once in a full run and not reproduced (six isolated runs, two more full runs); its message now carries the state and fight description if it recurs.


## 16. Task 5 Results: the scripted full run

`GameFullRunTests` plays the game from boot to the Camp Destroyed overlay as a player would: the party is walked with REAL pointer clicks through the real router (each waypoint aimed through the camera at a visible point on the road ahead, re-clicked whenever the party stops), fights are played by `ScriptedParty` through the real combat session, and a wipe is the game's own retry (banner, scene reload, checkpoint) because E4 is balanced to about two wins in three. It fails on: a party that cannot reach a trigger in 240 s or stalls 20 s, a fight that does not finish in 420 s, more than 8 wipes, a wipe that loses or skips progress, a win that does not advance the checkpoint, any logged error.

**Result.** Passed first time: 5 fights (E1, E2, E3 won first try, one wipe, then E4 won on retry), 46 walking clicks, 119 s real time at 4x, no logged errors, overlay shown. Nothing needed fixing in the game itself: the Task 4 flagged risks (the real router and mask walking a whole road, the checkpoint reload chain across encounters, the HUD not leaking over five fights) all held. Walking frame time was 6.3 ms in batch rendering, which is NOT a measure of the user's machine.

**Still unverified by tests (only a live play can).** The REAL `InputSystemCommandSource` (mouse through the Input System device layer, UI-over-click suppression); real frame rate on the user's GPU (2.3k pines, ~30 point lights, Forward+); how the facing, run-sync and zoom changes look; the toolkit gate picking the NEAREST character under a click (a goblin behind a hero can reject a click). No sound, no keyboard shortcuts.

**Totals.** 88 EditMode, 62 PlayMode.

**GATE (user): play it again** -- look at the facing while running and after stopping, the run cycles, the zoom, and the E1-E4 run -- and say what to change or whether to call Viking Raid done. Nothing is committed.


## 17. Toolkit promotion migration (2026-10-05)

Seven mechanisms Viking Raid had written or worked around now live in `crpg-toolkit/` (`Docs/Plans/Toolkit-Promotion-Batch1.md`): the zero-score veto (`UtilityAIDecisionProvider scoreFloor: 0f`), revive on heal
(`DeathSaveService reviveOnHeal: true`), the facing derivation (`PartyExplorationController facingSource`, read back through `VikingGame.LastOrderFacing`), `CombatCueEventBinding`, `PerCombatantDecisionProvider`,
`PartyCommandFilter` and the move adapter's `canMove` / `NavMeshDestinationAdjuster` / `provokesReactions: false`. Deleted from the game: `VetoZeroScoreDecisionProvider`, `PerCombatantDecisionProvider`,
`ModelCueEventBinding`, `VikingCommandSource`, `PartyVitals.NeedsRevive/Revive`, the per-frame revive sweep (the avoidance-priority half stays, renamed `UpdateDownedAvoidance`).
Behaviour changes worth knowing: the order's facing is now derived from the centre of the ORDERED heroes (it was all heroes, selected or not, downed or not), and `LastOrderFacing` starts at zero so a checkpoint spawn no
longer idle-turns the heroes to +X. Totals now: 87 EditMode, 56 PlayMode (the full scripted run passes: 4 fights, no wipes this time, 84 s at 4x); balance unchanged (E1-E4 100 / 100 / 93.3 / 66.5 %).
Two dice-dependent flakes fixed (a kill removes the target's agent before the test reads its position).
