# CRPG Toolkit: Production Foundation Design

Status: proposed design for user review and Sonnet implementation planning. Not a claim that these capabilities already exist.

## 1. Product contract

A game team supplies UI styling/layout, art, animation, audio, story/content, and a small composition/custom-gameplay layer. The toolkit supplies the reusable game loop: point-and-click control, camera, party, character lifecycle, equipment/inventory, interactions, dialogue, narrative timeline, quests, saving, tactical action orchestration, and replaceable tabletop rules.

“Some code” should mean adding a bespoke rule, interaction, presenter, or game-specific condition—not rebuilding spawning, reconnecting inventory after load, or writing another demo bootstrap to wire ordinary NPCs.

Target first: a reliable framework for small production-quality test games. Public distribution/Asset Store readiness is a separate gate. Do not promise every tabletop system, arbitrary rig compatibility, or complete SRD compliance.

### Initial supported envelope

- Desktop mouse/keyboard, point-and-click movement; controller/touch remain adapter opportunities, not initial acceptance requirements.
- NavMesh-based 3D exploration and turn-based tactical encounters. No networking, RTwP, custom navigation engine, or general cinematic editor.
- Reference Humanoid animation backend using Unity Playables. Generic rigs supported by explicit profiles and compatibility checks, not universal retargeting.
- Modular skinned clothing only for validated compatible skeleton families; rigid equipment via explicit sockets.
- Small parties, scene-based maps, bounded encounters. Initial acceptance fixture: four party characters and eight enemies; larger capacities require measurements.
- Safe-point saving outside active actions, combat, dialogue, and scene transitions initially. Unsupported saves are rejected with a reason, never silently partial.
- SRD5 reference subset plus a deliberately different small test ruleset to prove separation. Rule coverage is explicit and test-backed.

## 2. Repository-grounded baseline

Inspected package and TestProject source. An earlier live Unity run in this conversation returned 367/367 EditMode tests passing; this is useful regression evidence, not production certification or a current PlayMode result.

Keep these foundations:
- `crpg-toolkit/Runtime/Core/DefinitionAsset.cs`, `RulesDatabase.cs`: authored definitions and independent stable IDs, duplicate registration detection.
- `Runtime/Actors/CharacterState.cs`: mutable state separated from authored assets.
- `Rules/Core/`: modifier/query mechanism; keep it small and inspectable.
- `Runtime/Party/`, `Navigation/`, `Camera/`: useful seams and reference implementations.
- `Runtime/Dialogue/`, `Quests/`, `Core/Timeline/`: actual narrative runtime, not a replacement candidate.
- `Runtime/UI/`: presenter/view separation worth extending.

Specific gaps observed:

| Evidence | Production consequence |
|---|---|
| `Actors/CharacterPlayableAnimator.cs` directly polls a same-object NavMeshAgent, uses two mixer ports, caches baseline during a transient, and has only destruction-time unsubscribe | Imported model structure, movement, animation arbitration and binding lifetime are coupled; rapid interruption and resume need defined behavior |
| `Inventory/CharacterVisualEquipment.cs` indexes all descendant transforms by name, leaves unmatched garment bones unchanged, and builds visuals only from equip events | Duplicate names and incompatible outfits can silently deform wrong; binding after load/equip misses current appearance |
| `Actors/Npc/NpcSpawnService.cs` builds state only; visual, equipment, faction and dialogue composition are caller responsibilities | Every game/demo has to rebuild ordinary spawn integration |
| `TestProject/Assets/Editor/BaseHumanMaleCharacterAuthor.cs` adds navigation/physics to the imported model root and deletes the existing prefab before recreating it | Art replacement can disturb gameplay setup; regenerated assets risk broken identity/references |
| `Exploration/PartyExplorationController.cs` subscribes in its constructor with no disposal path | Session rebuild can retain command handlers |
| `TestProject/Assets/FighterDemo/Scripts/MouseClickCommandSource.cs` is demo-only and raises Move, not Select | Package-level point-and-click party control is not yet a complete consumer experience |
| `Actors/ProficiencyBonus.cs`, `Combat/DefaultArmorClassRule.cs`, `Dialogue/DialogueRunner.cs` include 5e formula/default d20 behavior in generic runtime | Physical SRD asset separation is not yet behavioral ruleset independence |
| `Abilities/AbilityResolutionService.cs` target validation checks counts; ritual/upcast branches bypass normal cost handling | Preview/commit legality and alternate cost policies need one authoritative route |
| `Combat/MovementResolutionService.cs` resolves movement synchronously and checks opportunity attacks after movement | Real moving characters need path-segment resolution and interruptible orchestration |
| `Saving/SaveService.cs` captures party characters but omits Level, known/prepared abilities, spellcasting stat and other current CharacterState fields | Existing round-trip tests do not establish full gameplay-equivalent restore |
| `Dialogue/DialogueRunner.cs` automatically walks branch/action nodes without a step bound | Invalid authored cycles can hang a conversation/editor |
| `Quests/QuestService.cs` uses absolute event counts for every objective and emits completion without quest/objective identity | Richer objective semantics and reliable targeted integration are missing |

The README's “Phase 1” status and some comments are historical. Source and verified behavior take precedence; update documentation as slices land.

## 3. Architectural boundaries

Retain current subsystem assemblies; do not rewrite everything behind an enormous universal engine interface.

Introduce two upper-layer assemblies:

- `CRPG.Presentation` (`Runtime/Presentation/`): character view binding, Playables backend, rig/socket mapping, visual equipment, typed visual requests. May depend on Actors/Inventory/Core, never the reverse.
- `CRPG.Gameplay` (`Runtime/Gameplay/`): shipped session composition, world spawning, action orchestration, interaction routing, save/load coordination. Depends on subsystem interfaces and presentation adapters, never a concrete SRD implementation.

Input System code belongs in `Integrations/InputSystem/`, with its own assembly. UITK/uGUI pointer capture adapters belong at their UI integration boundary, not in Core. Keep the existing Camera reference backend; do not add Cinemachine just to change ownership.

Rules contracts belong at the lowest already-valid dependency layer. Existing `Core/Combat/` interfaces can remain while their implementation moves. Do not make `CRPG.Rules` reference Actors: Actors already references Rules. Contracts requiring CharacterState belong in Actors or another deliberately acyclic contract layer.

A ruleset entrypoint receives a typed registration builder and registers services during session construction. The shipped composition root accepts that entrypoint via a Unity-serializable provider adapter. No runtime global singleton, reflection plugin scanning, or service locator in gameplay code. Consumers receive explicit dependencies. Validate required registrations before enabling input.

### Ownership invariants

1. Definitions own immutable authored data; state owns authoritative mutable data.
2. The session owns registration, teardown, clock and command admission.
3. The motor alone writes the gameplay root position. A facing controller alone writes its orientation; disable competing NavMeshAgent rotation.
4. Animation never decides damage, target legality, movement budget, death or quest completion.
5. EquipmentService owns equipped state; visuals reconcile from it, not vice versa.
6. The action resolver owns commit/cancel and authoritative results; UI and AI share its validation.
7. Narrative timeline records deliberate story facts, not every transient animation/combat event.
8. Required missing dependencies are validation failures, not silent no-ops. Optional cosmetic omissions use a documented fallback plus diagnostic.

## 4. Character design

### 4.1 Prefab contract

```text
CharacterRoot                       gameplay position, identity, lifetime
  CharacterAgent                    session/entity binding; not a stat container
  NavMeshAgent + NavMeshCharacterMotor
  CapsuleCollider + kinematic Rigidbody (reference trigger configuration)
  FacingController
  SelectionProxy                    selectable volume/selection anchor
  InteractionAnchor
  VisualRoot                        authored art scale/orientation offset only
    CharacterView                   bind/unbind; pose/equipment synchronization
    Model                           replaceable nested art prefab
      Animator                      exactly one reference animation output
      Skeleton
      BodyRenderers
    PresentationAnchors             overhead UI/audio/VFX anchors as appropriate
```

Socket transforms may live beneath animated bones; the binding asset maps semantic socket IDs to those transforms. Never infer that `VisualRoot/RightHand` is an animated bone. Root stays unit scale; art offsets do not change movement speed/range units. Author capsule/agent dimensions separately from visual bounds.

Gameplay prefab is not an FBX root. Model replacement must preserve root components, entity binding and scene references. Both playable characters and NPCs use the same CharacterAgent/View lifecycle. `NpcDefinition` still describes NPC gameplay; its visual reference migrates to a rig/view definition or supported wrapper prefab without discarding identity.

### 4.2 Session and binding lifecycle

State lifetime: definition/build data → allocate entity ID → construct/register state → establish inventory/faction/story associations → instantiate/bind view → enable commands.

View lifetime: Unbound → Bound → Unbound/Disposed. Binding with a different entity/session without first unbinding is an error. Rebinding the identical tuple is a no-op. Unbind cancels presentation requests, releases subscriptions/visual instances, and removes movement/selection registrations. Disabling a view pauses/cleans its presentation subscription according to one documented policy; re-enable reconciles state before listening again. Test both disable/enable and actual destruction. Pooling is not required, but the lifecycle must not prohibit it.

World spawns have stable authored spawn IDs independent of their per-session entity IDs. Loading preserves saved entity IDs and resolves spawn state before creating views. Never replay a spawn as a new story event on load.

### 4.3 Motion and facing

Expose a read-only motion snapshot from the motor: velocity, desired velocity, grounded/on-mesh validity, remaining path distance, move status and request ID. Animation consumes this, not NavMeshAgent directly.

Movement results distinguish Pending, Moving, Arrived, Blocked, Invalid, Cancelled. Partial paths do not count as successful arrival at the requested destination. Validate destination/path before superseding a valid order. Moving/off-mesh/disabled cases return structured command failures, not exceptions leaking to a player click handler.

Facing priority: terminal/incapacitated policy → committed action facing → interaction facing → motion facing → idle facing. These are policy decisions of the action controller, not arbitrary transform writers. Turning uses authored angular speed and a clear facing-completion tolerance. Retargeting a move cancels only the replaced request; stale completion cannot stop a newer order.

Initial locomotion is in-place, motor-driven, with root motion disabled. Walk/run blend uses measured velocity and authored clip reference speeds with bounded playback-rate correction. Add start/stop hysteresis to avoid threshold flicker. Root-motion displacement, traversal, and foot IK are later opt-in capabilities, not prerequisites.

### 4.4 Animation responsibilities

Retain Playables as the reference backend; do not switch to AnimatorController without evidence that Playables cannot meet the agreed needs. Replace the monolithic two-port cue component with:

- `CharacterPresentationState`: current locomotion, stance, action token and terminal pose intent.
- `CharacterAnimationProfile`: validated semantic motions/actions, clip references, reference speeds, loop policy, blend timings, optional AvatarMask/layer settings.
- `CharacterAnimationDriver`: small adapter contract for binding, ticking and requests.
- `PlayableCharacterAnimationDriver`: graph/layer/clip lifetime implementation.

Baseline locomotion updates continuously even while temporarily masked by an action. Combat stance chooses a locomotion set; it does not freeze movement animation. Full-body action channel is exclusive. Optional upper-body actions require a compatible mask and permit locomotion underneath. Death/terminal pose cannot be displaced by a move or cosmetic hit request. Incapacitation, recovery and resume come from gameplay intent.

Every transient has a request token, cancellation policy, playback rate, elapsed phase, and completion/cancel notification exactly once. A late callback for a previous token cannot finish the current action. Repeated requests do not reset a loop unless explicitly requested. Clip looping and zero-length/missing clip behavior are validated, not inferred only from a boolean label.

Graph interruption must maintain normalized output weights and continuity during attack→hit→death chains. A bounded outgoing blend group is acceptable; an ever-growing graph is not. Verify the actual graph implementation rather than trusting existing comments describing its blending.

Semantic action phases: Prepare → Commit → Recover → Complete, with explicit Cancelled/Failed outcomes. Simulation time and authoritative commit schedule belong to action orchestration. Animation/footstep/audio markers are presentation synchronization hints, never the only route to gameplay commit. Missing art must not soft-lock an action. Pause freezes the shared gameplay/presentation clock; unscaled UI transitions are a separate concern.

### 4.5 Equipment and body compatibility

An equipment visual profile declares one of:
- Rigid attachment: socket ID, local position/rotation/scale and optional drawn/sheathed presentation.
- Skinned attachment: compatible rig-family ID plus explicit source→target relative bone paths or validated mapping asset, root bone, and body regions hidden.
- No visual: legitimate for invisible/non-worn items.

Do not equate Humanoid animation retargeting with interchangeable skin bind poses. Identical bone names are insufficient: validate duplicate paths, missing bones, rest/bind-pose assumptions, mesh scale and root alignment. Unsupported garments fail validation and do not replace a known-good visual with a half-bound mesh. Rigid attachments should work across different Humanoid skeletons through semantic socket mapping.

Equipment visuals perform initial full reconciliation from inventory on bind/load, then targeted updates. Shared body region hiding is reference-counted across garments; removing one piece cannot expose a region still covered by another. Instantiate owned materials only if required and dispose them. Never mutate shared meshes/materials/bind poses at runtime.

### 4.6 Character authoring workflow

A Character Setup editor accepts a model prefab, rig family, animation profile, capsule/navigation settings and socket mapping. It creates a wrapper prefab in a user-selected folder. Existing assets are updated in place with Undo and stable GUIDs; never delete/recreate as an “update.” Source FBX import changes require a visible preview and explicit user action.

Provide Validate, Preview and Update Wrapper controls—not a generator for arbitrary gameplay code. Report asset path, field and actionable fix for every error. A preview scene exercises idle/walk/run, turn, action, interruption, death/recovery, equip/unequip. Missing mandatory clips block the relevant capability; optional clips use a stated fallback. Generic creatures can have reduced capability profiles rather than faking Humanoid validity.

## 5. Complete consumer game loop

### Input, camera, party

Ship a default Input Actions asset and adapter for click select, shift-toggle, drag-box, click movement, cancel, interact, ability targeting and camera controls. Resolve pointer intent once: captured UI/modal → targeting → interactable/selectable → ground. Define click/drag threshold and suppress release-through after UI drags. Offer registered UITK and uGUI capture adapters; avoid scanning all UI documents every click.

PartyState remains authoritative for membership/selection/active member/groups. A command router checks context and entity eligibility before routing orders. A split/unlinked party does not implicitly move unselected groups. Formation paths handle narrow passages with practical follower destinations/repath limits, not perfect coordinated crowd simulation. Unreachable followers report failure without freezing the whole group.

Camera supports pan, edge-pan opt-out, zoom, rotate, focus/follow active or selected group, bounds and terrain/obstruction handling. Modal UI prevents camera/world input as specified. Use smoothed LateUpdate follow; teleport/load snaps intentionally rather than flying across the map. Provide the camera without requiring game-specific input code.

### Interaction

Shared interaction lifecycle: query available actions → select performer → approach valid interaction position → revalidate target/range → execute → release facing/input lease. Cancelling, target destruction, new move order or scene unload terminates the request safely. Ship talk, loot, use/door and scene-travel adapters. A custom interaction implements an explicit interface rather than editing a demo bootstrap.

### Inventory and UI contract

Keep shared party inventory and equipment services. Make equip validation, stack splitting/merging, charges, currency transactions and item ability use explicit and atomic on rejection. Starting equipment is built through the same service path as gameplay equipment.

Ship replaceable presenters for party/selection, resources, ability bar/targeting, turn order, inventory/equipment, dialogue/checks, store, quest journal and saves. Queries expose DTO snapshots and change notifications; commands return reason codes and arguments suitable for localization. Default functional UITK views are samples, not a required visual style. Provide UI-facing stable text keys with fallback strings and optional localization adapter; audio/VFX adapters consume typed cues with entity/action IDs. Do not embed licensed media in runtime.

### Narrative

Keep the append-only story timeline and its projections. Add durable event identity/deduplication for command retry/load integration; UI refresh must never record events. Timeline writes trigger targeted quest refresh through the composition layer. The narrative Timeline is distinct from Unity Timeline cinematics and the simulation clock.

Dialogue validation checks duplicate/missing IDs and unreachable nodes; runtime automatic traversal has a bounded step budget and diagnostic path to stop malformed loops. Checks delegate to the active ruleset—including roll, bonus, performer comparison and success—not a hardcoded d20. Add a conversation session/controller for input ownership, cancellation and store handoff. Mid-dialogue saves are not required initially.

Quest objectives explicitly choose historical count, count since activation, or current-state predicate. Preserve historical-count behavior for migrated assets. Store activation cursors for since-activation objectives. Optional/alternative objectives are added only if the first test game's authored quest requires them. Quest events carry quest/objective IDs. Reward execution uses a persisted claim marker so reload/repeated refresh cannot duplicate rewards.

## 6. Ruleset and action boundary

Introduce a validated `RulesetDefinition`/provider: identity/version, content catalog, supported capabilities, unit conversion and service registrations. Engine code never constructs SRD defaults.

Ruleset-owned: checks/dice policy, ability modifiers/proficiency, initiative, attack/save/defense, damage mitigation, action economy/cost alternatives, rest/progression, dying/death, concentration, equipment restrictions and movement cost. Existing data classes can retain generic descriptors; migrate SRD-only semantics through explicit extension data where needed, rather than deleting fields blindly.

Engine-owned: entity/world lifetime, selection, navigation geometry, requests/transactions, phase orchestration, presentation and story/save infrastructure. Tactical orchestration is reusable; actual reaction eligibility and timing rules come from the ruleset.

A single validation result powers player preview, AI candidate filtering and final execution. At commit revalidate source/target existence, faction relation, range, LOS, target uniqueness/count, known/granted ability, state eligibility and aggregated costs. Alternative casting modes transform a cost plan; they do not bypass unrelated action costs. Reserve once if waiting is needed; commit once; release reservations on pre-commit cancellation. Post-commit cancellation never refunds already applied results by default. Unsupported effects fail preflight rather than charging and doing nothing.

Movement uses world meters. Ruleset policy converts to tabletop units and accumulated costs; rounding is applied to cumulative consumption, not charged independently on every tiny movement tick. Traverse path segments, stop before interrupt points, resolve eligible reactions, revalidate then resume/cancel. Do not teleport the actor to emulate movement. Persisted combat saves and arbitrarily nested reactions are deferred; initial nesting depth is bounded and documented.

Effects need source/action ownership and explicit duration clocks. Concentration loss removes only effects owned by that concentration instance, not all matching conditions globally. Test refresh/stack/remove and source death. This is necessary before advertising concentration-based SRD content as supported.

Prove isolation with a small ruleset using a non-d20 check and different action economy, without editing Runtime. If it requires a runtime change, refine the seam before broadening SRD content.

## 7. Save/load and world continuity

First fix full character state coverage before building more persistent content. Inventory all mutable fields and explicitly mark persisted, derived or transient. Derived animation pose/request state is not saved. Ruleset-owned state supplies versioned payloads rather than hidden live references.

Save header includes schema version, ruleset ID/version and content compatibility identifier. Persist world scene/spawn identity, position/facing, persistent NPC state, defeated/removed spawns, containers, store stock/modifiers, party inventory/character progression, quest claims and timeline. Persist RNG state if continued deterministic sequences are part of the ruleset contract.

Load transaction: parse/validate/migrate → build fresh session state → validate IDs/content and target scene → build/bind views → swap active session → dispose old session. A failed load leaves the existing session usable. Prevent duplicate registrations/events during restore. Atomic file replacement with last-known-good backup; malformed/newer unsupported data returns a user-facing error. No fabricated defaults for missing required content.

Safe-point save requests explain why combat/action/dialogue/transition blocks saving. The first test game must be able to save after an encounter, exit the application, load and observe the same world—not just reconstruct a subset of party DTOs.

## 8. Validation and test-game gates

Build a package validator for IDs/references, definition graphs, capability requirements, ruleset registrations, rig profiles, sockets, outfit compatibility, scene entry points and save schema. No automatic “fix all” changing design data.

Gate A — Character laboratory: two different real Humanoid art sources; one compatible modular outfit family; rigid equipment; locomotion/facing/interruption/death; bind/unbind/scene reload; inspect visuals in PlayMode. A Generic creature is an additional acceptance fixture before claiming generic-rig support. Do not redistribute private third-party assets.

Gate B — Adventure slice: four-character party, click/drag selection, bounded camera, walk to talk/loot/store, one branching dialogue, one quest, scene travel, save/exit/load. A game implementer supplies content and reference views but writes no replacement framework services.

Gate C — Tactical slice: four party members/eight opponents, actual navigation around an obstacle, melee/ranged/support actions, one reaction, a temporary effect and concentration cleanup, AI using shared validation, clear turns/death, post-combat save. Known unsupported rules are disabled in content/validator.

Gate D — Second game/ruleset: a fresh consuming project imports the package; different art and UI skin, alternate non-d20 ruleset. No package-source edits, TestProject script dependency, or copied monolithic bootstrap. Document exactly what consumer code was required.

“Production-quality test-game ready” means all four gates pass, not that the editor test count increased. Capture visual evidence at the agreed checkpoints, not hundreds of blind screenshots. Log target hardware/build/resolution, scene load and frame/GC measurements; agree numeric budgets before performance sign-off. Use an actual standalone development build as well as Editor PlayMode.

Separate release gate: supported Unity matrix, package install/uninstall, samples free of private assets, licenses and third-party notices included inside distributed artifacts, documentation/API compatibility and migration policy, asset-store legal review. `Licenses/TODO.md` remains a real blocker for distribution.

## 9. Scope guardrails

No ECS/DOTS rewrite, bespoke animation editor, universal DI framework, arbitrary scripting DSL, multiplayer, full ruleset catalog, cinematic timeline or general root-motion navigation in this initiative. Keep the tested core and migrate seams in small slices. Treat game-specific bootstrap code as evidence of missing reusable integration, not automatically as package-worthy code.

Proposal defaults requiring user review before their dependent slices: retain Playables; motor-driven in-place locomotion; turn-based first; safe-point saves; reference shared party inventory. Art style, final UI, target hardware budgets and commercial licensing are deliberately not guessed.
