using System.Collections;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.Fixtures.CharacterCreatorVerification
{
    /// <summary>
    /// Task 5.3's real behavioral acceptance check for the CharacterCreator sample -- NOT shipped inside
    /// Samples~/CharacterCreator itself, same convention as BasicCrpgVerificationTests. Deliberately does NOT
    /// reference the sample's own "CRPG.Samples.CharacterCreator" assembly (only exists once the sample is
    /// imported) -- everything here is read back off generic, already-referenced package types
    /// (GameSessionHost.Session, CharacterAgent). Ignored (not failed) when no scene matching "CharacterCreator"
    /// is registered in Build Settings.
    /// <para>
    /// Scope, stated plainly: this proves the real SRD5 content -&gt; generic choice resolution -&gt; Build() -&gt;
    /// spawn -&gt; movement pipeline works, using its OWN CharacterBuildPlan built directly against the scene's
    /// real, ruleset-started RulesDatabase. It does NOT exercise CharacterCreatorView's UI, the shipped
    /// Hero.prefab, or CharacterCreatorBootstrap's own completion handler -- that gap is closed by a live human
    /// review (see Docs/Verification/Task-5.3-CharacterCreator.md), not by this fixture.
    /// </para>
    /// </summary>
    public class CharacterCreatorVerificationTests
    {
        private static readonly DefinitionId HumanRaceId = new("race-human");
        private static readonly DefinitionId DwarfRaceId = new("race-dwarf");
        private static readonly DefinitionId FighterClassId = new("class-fighter");
        private static readonly DefinitionId WizardClassId = new("class-wizard");
        private static readonly DefinitionId SoldierBackgroundId = new("background-soldier");
        private static readonly DefinitionId SageBackgroundId = new("background-sage");
        private static readonly DefinitionId SoldierFeatureId = new("feature-soldier-background");
        private static readonly DefinitionId SageFeatureId = new("feature-sage-background");

        // Str, Dex, Con, Int, Wis, Cha, in that fixed order -- matches the sample's own assignment order.
        private static readonly string[] AbilityStatIds =
            { "stat-strength", "stat-dexterity", "stat-constitution", "stat-intelligence", "stat-wisdom", "stat-charisma" };
        private static readonly int[] StandardArray = { 15, 14, 13, 12, 10, 8 };

        private sealed class NoCapture : IPointerCaptureProvider
        {
            public bool IsPointerOverUI(Vector2 screenPosition) => false;
        }

        [UnityTest]
        public IEnumerator RealSrd5ContentBuildsCorrectlyAndTheResultingCharacterIsPlayable()
        {
            var buildIndex = FindCharacterCreatorSceneBuildIndex();
            if (buildIndex < 0)
            {
                Assert.Ignore("No scene whose path contains \"CharacterCreator\" is registered in Build " +
                               "Settings -- this fixture only runs inside the Task 5.3 clean-install harness.");
                yield break;
            }

            yield return SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            for (var i = 0; i < 5; i++) yield return null; // let CharacterCreatorBootstrap.Start() settle.

            var host = Object.FindAnyObjectByType<GameSessionHost>();
            Assert.IsNotNull(host, "Expected a live GameSessionHost in the CharacterCreator scene.");
            var session = host.Session;
            Assert.IsNotNull(session, "GameSessionHost must already have started a session.");

            Assert.IsTrue(session.RulesDatabase.All<RaceDefinition>().Any(), "Expected real SRD5 races registered via the ruleset-aware StartSession path.");
            Assert.IsTrue(session.RulesDatabase.All<ClassDefinition>().Any(), "Expected real SRD5 classes registered.");
            Assert.IsTrue(session.RulesDatabase.All<BackgroundDefinition>().Any(), "Expected real SRD5 backgrounds registered.");

            var humanFighter = BuildCharacter(session.RulesDatabase, HumanRaceId, FighterClassId, SoldierBackgroundId);
            VerifyHumanFighterSoldier(humanFighter);

            var dwarfWizard = BuildCharacter(session.RulesDatabase, DwarfRaceId, WizardClassId, SageBackgroundId);
            VerifyDwarfWizardSage(dwarfWizard);

            yield return VerifyBuiltCharacterMovesOnGroundClick(session, humanFighter);
        }

        /// <summary>Builds a real character against real SRD5 content: sets Race/Class/Background, assigns the
        /// Standard Array in the fixed Str/Dex/Con/Int/Wis/Cha order, then generically resolves every pending
        /// choice by taking its first Count options (NOT "the first option" -- every reachable class-skill
        /// choice requires 2+ selections, so that would throw). Mirrors exactly what
        /// CharacterCreatorView's picker does when a player makes the first N offered choices.</summary>
        private static CharacterState BuildCharacter(IRulesDatabase rulesDatabase, DefinitionId raceId, DefinitionId classId, DefinitionId backgroundId)
        {
            Assert.IsTrue(rulesDatabase.TryResolve<RaceDefinition>(raceId, out var race), $"Race {raceId} not found.");
            Assert.IsTrue(rulesDatabase.TryResolve<ClassDefinition>(classId, out var characterClass), $"Class {classId} not found.");
            Assert.IsTrue(rulesDatabase.TryResolve<BackgroundDefinition>(backgroundId, out var background), $"Background {backgroundId} not found.");

            var plan = new CharacterBuildPlan { Race = race, Class = characterClass, Background = background, Level = 1 };
            for (var i = 0; i < AbilityStatIds.Length; i++)
                plan.AbilityScores.SetScore(new DefinitionId(AbilityStatIds[i]), StandardArray[i]);

            var buildService = new CharacterBuildService(rulesDatabase);
            buildService.RefreshPendingChoices(plan);
            while (plan.PendingChoices.Count > 0)
            {
                var requirement = plan.PendingChoices[0];
                Assert.IsTrue(requirement.Options.Count >= requirement.Count,
                    $"Choice '{requirement.Id}' needs {requirement.Count} option(s) but only has {requirement.Options.Count} -- not generically resolvable.");
                var selections = requirement.Options.Take(requirement.Count).ToList();
                plan.ResolveChoice(requirement.Id, selections);
                buildService.RefreshPendingChoices(plan);
            }

            return buildService.Build(plan, RuntimeEntityId.NewId());
        }

        private static void VerifyHumanFighterSoldier(CharacterState character)
        {
            // Fighting Style resolves via SelectFeature -- it's a granted FEATURE, not a proficiency.
            Assert.IsTrue(character.HasFeature(new DefinitionId("feature-fighting-style-defense")),
                "Expected the first offered Fighting Style (Defense) to be a granted feature.");
            Assert.IsTrue(character.HasFeature(SoldierFeatureId), "Expected the Soldier background's own feature.");
            Assert.IsTrue(character.HasProficiency(new DefinitionId("skill-acrobatics")), "Expected the first offered Fighter skill proficiency.");
            Assert.IsTrue(character.HasProficiency(new DefinitionId("skill-animal-handling")), "Expected the second offered Fighter skill proficiency.");

            // Human's real FeatureModifier grants +1 to every stat -- final scores are the Standard Array
            // PLUS that bonus, not the raw input. (Confirmed by reading FeatureDefinition_HumanTraits.asset.)
            AssertScore(character, "stat-strength", 16);
            AssertScore(character, "stat-dexterity", 15);
            AssertScore(character, "stat-constitution", 14);
            AssertScore(character, "stat-intelligence", 13);
            AssertScore(character, "stat-wisdom", 11);
            AssertScore(character, "stat-charisma", 9);
        }

        private static void VerifyDwarfWizardSage(CharacterState character)
        {
            Assert.IsTrue(character.KnowsAbility(new DefinitionId("spell-fire-bolt")), "Expected the first offered Wizard cantrip.");
            Assert.IsTrue(character.KnowsAbility(new DefinitionId("spell-mage-hand")), "Expected the second offered Wizard cantrip.");
            Assert.IsTrue(character.KnowsAbility(new DefinitionId("spell-minor-illusion")), "Expected the third offered Wizard cantrip.");
            Assert.IsTrue(character.HasFeature(SageFeatureId), "Expected the Sage background's own feature.");
            Assert.IsTrue(character.HasProficiency(new DefinitionId("skill-arcana")), "Expected the first offered Wizard skill proficiency.");
            Assert.IsTrue(character.HasProficiency(new DefinitionId("skill-history")), "Expected the second offered Wizard skill proficiency.");

            // Dwarf's real modifiers are +2 Constitution and (Hill Dwarf) +1 Wisdom -- NOT a flat +1-to-all
            // like Human. Confirmed by reading FeatureDefinition_DwarfTraits.asset/HillDwarfTraits.asset.
            AssertScore(character, "stat-strength", 15);
            AssertScore(character, "stat-dexterity", 14);
            AssertScore(character, "stat-constitution", 15);
            AssertScore(character, "stat-intelligence", 12);
            AssertScore(character, "stat-wisdom", 11);
            AssertScore(character, "stat-charisma", 8);
        }

        private static void AssertScore(CharacterState character, string statId, int expected) =>
            Assert.AreEqual(expected, character.Stats.GetScore(new DefinitionId(statId)), $"Unexpected final {statId} score.");

        private static IEnumerator VerifyBuiltCharacterMovesOnGroundClick(GameSession session, CharacterState character)
        {
            session.Characters.Register(character);
            session.Party.AddMember(character.Id);

            var go = new GameObject("Test_CreatedCharacter");
            go.transform.position = Vector3.zero;
            var navAgent = go.AddComponent<NavMeshAgent>();
            navAgent.speed = 3.5f;
            go.AddComponent<NavMeshCharacterMotor>();
            var agent = go.AddComponent<CharacterAgent>();
            navAgent.Warp(Vector3.zero);
            agent.Bind(character.Id, session);

            var router = new PlayerCommandRouter(new SceneSelectableSource(), new NoCapture(), ~0);
            var movementService = new PartyMovementService(new NavMeshService(), new SceneSelectableSource());
            var exploration = new PartyExplorationController(session.Party, movementService, new WedgeFormationSolver(), router);
            if (agent.Motor != null) exploration.RegisterMotor(character.Id, agent.Motor);

            var before = go.transform.position;
            ClickWorldPoint(router, new Vector3(5f, 0f, 5f));

            // Batchmode runs frames far faster than real time -- a generous frame cap (not a wall-clock
            // guess), matching BasicCrpgVerificationTests' own already-proven convention.
            var moved = false;
            for (var i = 0; i < 5000 && !moved; i++)
            {
                yield return null;
                moved = Vector3.Distance(go.transform.position, before) > 1f;
            }

            exploration.Dispose();
            Object.DestroyImmediate(go);
            Assert.IsTrue(moved, "The just-built character's position did not change by more than 1 unit after a ground click.");
        }

        private static int FindCharacterCreatorSceneBuildIndex()
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                if (SceneUtility.GetScenePathByBuildIndex(i).Contains("CharacterCreator")) return i;
            }

            return -1;
        }

        private static void ClickWorldPoint(PlayerCommandRouter router, Vector3 worldPoint)
        {
            var ray = new Ray(new Vector3(worldPoint.x, worldPoint.y + 20f, worldPoint.z), Vector3.down);
            router.HandlePointerDown(Vector2.zero, ray);
            router.HandlePointerUp(Vector2.zero, ray, camera: null, shiftHeld: false);
        }
    }
}
