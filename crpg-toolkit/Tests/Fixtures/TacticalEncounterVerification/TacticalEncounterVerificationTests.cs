using System.Collections;
using System.Linq;
using CRPG.AI;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.Fixtures.TacticalEncounterVerification
{
    /// <summary>
    /// Task 5.3's scene smoke check for the TacticalEncounter sample -- NOT shipped inside
    /// Samples~/TacticalEncounter itself, same convention as the other two Verification fixtures.
    /// Deliberately does NOT reference the sample's own "CRPG.Samples.TacticalEncounter" assembly. Read-only:
    /// confirms the scene's real GameSessionHost started via the ruleset-aware path with real SRD5 policies AND
    /// the sample's own authored content registered -- no combat is started or mutated by this test. The real
    /// combat/AI/reaction mechanics are proven in isolation (genuinely separate sessions/event buses, never
    /// reusing this loaded scene's own live one) by TacticalAiScoringTests/TacticalReactionTests/
    /// TacticalFullEncounterTests; the interactive UI/click-to-attack/HUD are closed only by the live human
    /// review (see Docs/Verification/Task-5.3-TacticalEncounter.md).
    /// </summary>
    public class TacticalEncounterVerificationTests
    {
        /// <summary>Confirmed empirically: leaving the real TacticalEncounter scene loaded/active after this
        /// test (a full 40x40 ground + wall + baked NavMesh + TacticalEncounterBootstrap's own spawned
        /// CharacterAgents, none of which this test itself created or can individually clean up) made
        /// TacticalFullEncounterTests/TacticalReactionTests fail when run afterward in the same batch (both
        /// pass in isolation) -- their own from-scratch NavMeshSurface bakes and CharacterAgent.AllLive-based
        /// lookups got confused by the still-live scene content. Loading a genuinely empty scene afterward
        /// clears both the stale NavMesh and every scene-owned GameObject this test's SceneManager.LoadSceneAsync
        /// call brought in, restoring real isolation for whichever test runs next.</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var loadedScene = SceneManager.GetActiveScene();
            // Only clean up if this test's own scene is still the active one -- the Ignore path (no matching
            // scene registered) never loads anything, and this must never touch a scene some OTHER test left
            // active.
            if (!loadedScene.IsValid() || !loadedScene.path.Contains("TacticalEncounter")) yield break;

            var emptyScene = SceneManager.CreateScene($"TacticalEncounterVerification_Empty_{System.Guid.NewGuid():N}");
            SceneManager.SetActiveScene(emptyScene);
            yield return SceneManager.UnloadSceneAsync(loadedScene);
        }

        [UnityTest]
        public IEnumerator SceneStartsARealRulesetAwareSessionWithTheSamplesOwnContentRegistered()
        {
            var buildIndex = FindTacticalEncounterSceneBuildIndex();
            if (buildIndex < 0)
            {
                Assert.Ignore("No scene whose path contains \"TacticalEncounter\" is registered in Build " +
                               "Settings -- this fixture only runs inside the Task 5.3 clean-install harness.");
                yield break;
            }

            yield return SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            for (var i = 0; i < 5; i++) yield return null; // let TacticalEncounterBootstrap.Start() settle.

            var host = Object.FindAnyObjectByType<GameSessionHost>();
            Assert.IsNotNull(host, "Expected a live GameSessionHost in the TacticalEncounter scene.");
            var session = host.Session;
            Assert.IsNotNull(session, "GameSessionHost must already have started a session.");

            Assert.IsTrue(session.Policies.TryGet<IArmorClassRule>(out _), "Expected the real SRD5 ArmorClassRule registered via the ruleset-aware StartSession path.");
            Assert.IsTrue(session.Policies.TryGet<IAttackBonusRule>(out _), "Expected the real SRD5 AttackBonusRule registered.");
            Assert.IsTrue(session.Policies.TryGet<IInitiativeRule>(out _), "Expected the real SRD5 InitiativeRule registered.");
            Assert.IsTrue(session.Policies.TryGet<ISavingThrowRule>(out _), "Expected the real SRD5 SavingThrowRule registered.");

            Assert.IsTrue(session.RulesDatabase.All<BehaviorSetDefinition>().Any(),
                "Expected the sample's own authored Behavior content (Foe Warrior) registered into the session's RulesDatabase.");
        }

        private static int FindTacticalEncounterSceneBuildIndex()
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                if (SceneUtility.GetScenePathByBuildIndex(i).Contains("TacticalEncounter")) return i;
            }

            return -1;
        }
    }
}
