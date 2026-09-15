# SRD5e2014 Supported Subset

This is the coverage ledger `Docs/Design/Production-Foundation.md` sec.8 (Gate C: "known unsupported rules are
disabled in content/validator") asks for. It consolidates the caveats that were previously scattered across 12
individual content assets' own `description` fields into one place, and states plainly what the Task 4.5
tactical slice fixture actually exercises versus what exists in the package but isn't wired into that fixture.

This engine implements a subset of SRD5.1 sufficient for a small turn-based tactical/adventure game, not a
complete ruleset. Nothing here is a claim of blanket SRD5 compliance.

## By capability area

### Attack rolls, saves, damage
- **Supported**: attack roll vs. AC (`AttackRollEffect`), advantage/disadvantage, critical hit (double dice, not
  modifier), critical miss (natural 1 always misses), Extra Attack (`IAttackBonusRule.ComputeAttackCount`),
  saving throws vs. a DC (`SavingThrowEffect`), auto-fail saves (Paralyzed/Petrified/Unconscious-style),
  spell save DC computed from the caster's `SpellcastingStatId`.
- **Not modeled**: damage type resistance/immunity/vulnerability requires an explicit `damageTypeId` on
  `DamageEffect` and a wired `IDamageResistanceRule` -- most authored spells (`BurningHands`, `Web`, ...) use
  untyped/generic damage and skip this entirely, by original-authoring choice, not a Task 4.5 change.
  Melee-critical-hit-within-5ft-of-an-incapacitated-target (Paralyzed/Petrified/Unconscious's real SRD rule) has
  no range math in `AttackRollEffect` and is not modeled.

### Conditions and durations
- **Supported** (Task 4.4): per-application `DurationBoundary` (None/StartOfTargetTurn/EndOfTargetTurn/
  StartOfSourceTurn/EndOfSourceTurn) ticked by `ConditionDurationService`; a source-scoped condition goes
  dormant (not removed) once its source leaves the encounter; cure-by-type (`RemoveConditionEffect`) vs.
  owner-scoped removal (`RemoveConditionsWithOwner`) are distinct operations.
- **Exercised by the tactical slice**: `AbilityDefinition_Command`'s `ApplyConditionEffect` now carries a real
  `EndOfTargetTurn` boundary (Task 4.5 -- the first real content asset to use this field; every other
  `ApplyConditionEffect`-using spell in the package still applies its condition with an indefinite duration,
  unchanged).
- **Not modeled**: escape checks (Web's Restrained), auto-removal on the spell ending or on the target taking an
  action (Invisibility), difficult terrain/light obscurement/flammability (Web), the non-attack-roll parts of
  Frightened (ability-check disadvantage, movement restriction).

### Concentration
- **Supported** (Task 4.4, real content, not just test fixtures): casting a second Concentration-tagged spell
  breaks the first (`AbilityResolutionService.Execute`'s break-before-new-effects ordering); a failed
  Constitution save on taking damage breaks concentration (`ConcentrationService`); the caster's death breaks
  it too (`ConcentrationService`'s `CreatureDied` subscription); `ConcentrationEffectCleanup.Break` is the one
  real "break concentration" operation, shared by every trigger.
- **Exercised by the tactical slice**: `AbilityDefinition_Bless` recast on a different target demonstrates
  replacement; a concentrating caster dying in real combat demonstrates the source-death path. `Bless` is
  otherwise unmodified, real SRD5e2014 content.

### Reactions
- **Supported** (Task 4.3 pipeline, Task 4.5 first real content + wiring): `MovementResolutionService.
  ResolveReactionForCrossing` fires when a mover leaves a reactor's threatened area mid-segment (not merely at
  path corners); `ReactionService.ResolveOpportunity` filters to `AbilityTag.Reaction`-tagged, currently-usable
  candidates and executes the chosen one through the same `AbilityResolutionService` as everything else.
- **Exercised by the tactical slice**: `AbilityDefinition_OpportunityAttack` (new, tactical-slice-owned),
  granted via `FeatureDefinition_ReactiveStrikes` to every melee-capable combatant. `AbilityDefinition_Shield`
  (a real SRD5e2014 Reaction-tagged asset) exists in the package but is **not** used by this fixture -- its
  trigger ("targeted by an attack roll") has no wired trigger source anywhere yet; only the movement-based
  opportunity-attack trigger is real end-to-end.
- **Not modeled**: reaction choice is always the AI's own `FirstEligibleReactionProvider` (first eligible
  candidate), for both AI-controlled and player-controlled reactors -- there is no real "ask the player which
  reaction to take" UI. Reactions beyond depth 1 (a reaction triggering another reaction) are out of scope,
  matching `ReactionService`'s own existing documented bound.

### Movement and opportunity attacks
- **Supported**: real per-segment NavMesh traversal (`MoveActionExecution`), cumulative (not per-corner)
  resource rounding, live per-leg budget clipping, exact geometric segment/circle threat-boundary crossings.
- **Not modeled**: difficult terrain, flanking, cover, reach weapons (melee range is a flat authored `range` on
  `TargetingDefinition`, not derived from equipment).

### Spellcasting, slots, upcasting
- **Supported**: known/prepared ability gating (`CharacterState.KnownAbilities`/`PreparedAbilities`), spell-slot
  resources, upcasting via `AbilityCostPlan`'s slot-substitution + `extraDiceCountPerSlotLevel` scaling, ritual
  casting (the `Ritual` tag + cost-bypass), cantrip level-scaling (`DamageEffect.scalesWithCasterLevel`).
- **Not modeled**: `GuidingBolt`/`RayOfFrost`'s secondary on-hit effects ("advantage until next turn," speed
  reduction) have no timer mechanism yet; `ComprehendLanguages`/`DetectMagic`'s ritual-casting option has no
  ritual-without-a-slot mechanism (they're playable as ordinary casts only).

### Rest, death, equipment
- **Supported**: death saves (`DeathSaveService`), unconscious-vs-dead as separate real transitions
  (`CreatureDroppedToZeroHP` vs. `CreatureDied`), equipment slot restrictions.
- **Not modeled**: hit-dice recovery on rest (content gap, not a hardcoded-logic gap -- `RestService` is already
  data-driven).

## What the Task 4.5 tactical slice's own roster actually contains

Every ability/condition/feature the fixture grants a combatant is one of:

| Content | Kind | Source |
|---|---|---|
| `AbilityDefinition_MeleeStrike` | Melee attack | New, tactical-slice-owned |
| `AbilityDefinition_OpportunityAttack` | Melee attack, Reaction | New, tactical-slice-owned |
| `FeatureDefinition_ReactiveStrikes` | Grants the above reaction | New, tactical-slice-owned |
| `AbilityDefinition_FireBolt` | Ranged spell attack | Real SRD5e2014, unmodified |
| `AbilityDefinition_HealingWord` | Support/heal | Real SRD5e2014, unmodified |
| `AbilityDefinition_Bless` | Concentration buff | Real SRD5e2014, unmodified |
| `AbilityDefinition_Command` | Temporary effect (boundary added, Task 4.5) | Real SRD5e2014, one field added |

Nothing else from the wider SRD5e2014 catalog is part of this fixture's roster -- an ability not listed above is
neither disabled by a validator nor claimed to work in this fixture; it is simply not part of what this fixture
demonstrates. `Tests/Editor/TacticalSliceContentAuditTests.cs` enforces this allowlist automatically: it fails
if the tactical-slice content bundle ever references an ability/condition/feature not listed in this document.
