using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.Fixtures.TacticalEncounterVerification
{
    /// <summary>
    /// The first implementation gate for the TacticalEncounter sample (per the task plan): proves the
    /// authored "Foe Warrior" Behavior content's real, non-flat Considerations make a legal attack
    /// structurally dominant over movement, directly against the real UtilityAIDecisionProvider -- no
    /// scheduler/controller/NavMesh needed. Exists because the naive "empty Considerations" approach the
    /// TacticalSlice dogfood precedent uses is a real, confirmed defect: UtilityMath.CombineScores([]) is
    /// exactly 0, and UtilityAIDecisionProvider only replaces its best decision on a STRICTLY greater score,
    /// so every Behavior scoring a flat 0 ties, and whichever is listed first in BehaviorIds wins regardless
    /// of whether an attack is actually legal.
    /// </summary>
    public class TacticalAiScoringTests
    {
        private static (RulesDatabase RulesDatabase, BehaviorSetDefinition BehaviorSet, DefinitionId HitPointsId,
            DefinitionId ActionId, DefinitionId MovementId, DefinitionId StrengthId)
            BuildFoeWarriorContent()
        {
            var rulesDatabase = new RulesDatabase();

            var strength = TacticalEncounterTestContent.CreateStat("stat-strength", "Strength");
            var hitPoints = TacticalEncounterTestContent.CreateResource("resource-hitpoints", "Hit Points");
            var action = TacticalEncounterTestContent.CreateResource("resource-action", "Action");
            var movement = TacticalEncounterTestContent.CreateResource("resource-movement", "Movement");
            rulesDatabase.Register(strength);
            rulesDatabase.Register(hitPoints);
            rulesDatabase.Register(action);
            rulesDatabase.Register(movement);

            var meleeStrike = TacticalEncounterTestContent.CreateMeleeAbility(
                "ability-meleestrike", "Melee Strike", AbilityTag.Attack | AbilityTag.Melee,
                action, 1, strength.Id, hitPoints, new DiceExpression(1, 8, 3), prepareSeconds: 0f, recoverSeconds: 0f);
            rulesDatabase.Register(meleeStrike);

            var closeDistance = TacticalEncounterTestContent.CreateCloseDistanceBehavior(
                "behavior-closedistance", "Close Distance", movement.Id);
            var meleeStrikeNearest = TacticalEncounterTestContent.CreateMeleeStrikeNearestBehavior(
                "behavior-meleestrikenearest", "Melee Strike Nearest", meleeStrike.Id);
            rulesDatabase.Register(closeDistance);
            rulesDatabase.Register(meleeStrikeNearest);

            // Close Distance listed FIRST deliberately -- this is exactly the ordering that exposes the flat-
            // scoring tie-break bug if the Considerations above were ever accidentally left empty.
            var behaviorSet = TacticalEncounterTestContent.CreateBehaviorSet(
                "behaviorset-foewarrior", "Foe Warrior", closeDistance.Id, meleeStrikeNearest.Id);
            rulesDatabase.Register(behaviorSet);

            return (rulesDatabase, behaviorSet, hitPoints.Id, action.Id, movement.Id, strength.Id);
        }

        /// <summary>AttackRollEffect's own FindMissingRequiredEffectService check rejects an ability outright
        /// unless BOTH AttackBonusRule and ArmorClassRule are wired -- confirmed the hard way, this test file's
        /// first real run failed every attack-should-win assertion because only AttackBonusRule had been
        /// wired, silently leaving Attack with zero valid candidates in every scenario (so Move always "won" by
        /// being the only option, not by outscoring anything) -- exactly the kind of masked failure this test
        /// file exists to catch, just one level up the stack from where it was aimed.</summary>
        private static AbilityResolutionService CreateAbilityResolution(
            CharacterRegistry characters, RulesDatabase rulesDatabase, IPositionProvider positions, DefinitionId strengthId)
        {
            return new AbilityResolutionService(
                characters, new DiceService(new SeededRandomSource(1)), positions: positions,
                attackBonusRule: new DefaultAttackBonusRule(characters, rulesDatabase),
                armorClassRule: new DefaultArmorClassRule(characters, rulesDatabase, strengthId),
                rulesDatabase: rulesDatabase);
        }

        private static CharacterState CreateCombatant(CharacterRegistry registry, DefinitionId hitPointsId, DefinitionId actionId, DefinitionId movementId)
        {
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.AddResource(new ResourceState(hitPointsId, 20));
            character.AddResource(new ResourceState(actionId, 1));
            character.AddResource(new ResourceState(movementId, 6));
            registry.Register(character);
            return character;
        }

        [Test]
        public void AiPrefersAttackOverMovementWhenALegalAttackExists()
        {
            var (rulesDatabase, behaviorSet, hitPointsId, actionId, movementId, strengthId) = BuildFoeWarriorContent();

            var characters = new CharacterRegistry();
            var attacker = CreateCombatant(characters, hitPointsId, actionId, movementId);
            var target = CreateCombatant(characters, hitPointsId, actionId, movementId);

            var positions = new TestPositionProvider();
            positions.Set(attacker.Id, Vector3.zero);
            positions.Set(target.Id, new Vector3(1.5f, 0f, 0f)); // within melee range (2) -- attack is legal.

            var abilityResolution = CreateAbilityResolution(characters, rulesDatabase, positions, strengthId);

            var provider = new UtilityAIDecisionProvider(
                behaviorSet, rulesDatabase, characters, abilityResolution, positions, new SeededRandomSource(2));

            var decision = provider.GetDecision(new TurnContext(attacker.Id, new[] { target.Id }, System.Array.Empty<RuntimeEntityId>()));

            Assert.IsNotNull(decision, "Expected a decision when a legal attack exists.");
            Assert.AreEqual(BehaviorKind.UseAbility, decision.Kind, "A legal attack must beat a tied/flat-scored Move -- this is the exact defect the naive empty-Considerations approach has.");
            Assert.AreEqual(target.Id, decision.Targets[0]);
        }

        [Test]
        public void AiMovesTowardTheNearestEnemyWhenNoAttackIsLegal()
        {
            var (rulesDatabase, behaviorSet, hitPointsId, actionId, movementId, strengthId) = BuildFoeWarriorContent();

            var characters = new CharacterRegistry();
            var attacker = CreateCombatant(characters, hitPointsId, actionId, movementId);
            var farEnemy = CreateCombatant(characters, hitPointsId, actionId, movementId);
            var nearEnemy = CreateCombatant(characters, hitPointsId, actionId, movementId);

            var positions = new TestPositionProvider();
            positions.Set(attacker.Id, Vector3.zero);
            positions.Set(farEnemy.Id, new Vector3(15f, 0f, 0f));  // both out of melee range (2) -- no
            positions.Set(nearEnemy.Id, new Vector3(6f, 0f, 0f));  // attack is legal for either.

            var abilityResolution = CreateAbilityResolution(characters, rulesDatabase, positions, strengthId);

            var provider = new UtilityAIDecisionProvider(
                behaviorSet, rulesDatabase, characters, abilityResolution, positions, new SeededRandomSource(2));

            var decision = provider.GetDecision(new TurnContext(attacker.Id, new[] { farEnemy.Id, nearEnemy.Id }, System.Array.Empty<RuntimeEntityId>()));

            Assert.IsNotNull(decision, "Expected a Move decision when no attack is legal but movement resource remains.");
            Assert.AreEqual(BehaviorKind.Move, decision.Kind);
            Assert.AreEqual(nearEnemy.Id, decision.Targets[0], "Close Distance's decreasing-with-distance curve must prefer the nearer of two out-of-range enemies.");
        }

        [Test]
        public void AlreadyInRangeMoveDoesNotRepeatedlyWinOnceAttackBecomesLegal()
        {
            var (rulesDatabase, behaviorSet, hitPointsId, actionId, movementId, strengthId) = BuildFoeWarriorContent();

            var characters = new CharacterRegistry();
            var attacker = CreateCombatant(characters, hitPointsId, actionId, movementId);
            var target = CreateCombatant(characters, hitPointsId, actionId, movementId);

            var positions = new TestPositionProvider();
            positions.Set(attacker.Id, Vector3.zero);
            // Exactly at the Move behavior's own stopDistance (1.5) -- comfortably inside melee range (2), so
            // an attack is already legal. A correctly-scored AI must pick Attack, not "close the last bit of
            // distance" -- the real class of bug this whole test file exists to prevent.
            positions.Set(target.Id, new Vector3(TacticalEncounterTestContent.MoveStopDistance, 0f, 0f));

            var abilityResolution = CreateAbilityResolution(characters, rulesDatabase, positions, strengthId);

            var provider = new UtilityAIDecisionProvider(
                behaviorSet, rulesDatabase, characters, abilityResolution, positions, new SeededRandomSource(2));

            var decision = provider.GetDecision(new TurnContext(attacker.Id, new[] { target.Id }, System.Array.Empty<RuntimeEntityId>()));

            Assert.IsNotNull(decision);
            Assert.AreEqual(BehaviorKind.UseAbility, decision.Kind);
        }
    }
}
