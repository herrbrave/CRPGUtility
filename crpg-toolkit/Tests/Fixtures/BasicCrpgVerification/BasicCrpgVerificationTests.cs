using System.Collections;
using System.Linq;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Party;
using CRPG.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.Fixtures.BasicCrpgVerification
{
    /// <summary>
    /// Task 5.3's real behavioral acceptance check for the BasicCRPG sample -- NOT shipped inside
    /// Samples~/BasicCRPG itself (a shipped sample carrying test files would be clutter for a consumer's
    /// project); the clean-install harness copies this fixture into the scratch verification project
    /// alongside the imported sample and adds the imported scene to Build Settings at index 0 before running
    /// it. Deliberately does NOT reference the sample's own "CRPG.Samples.BasicCRPG" assembly (that assembly
    /// only exists once the sample is actually imported somewhere) -- everything here is read back off the
    /// generic, already-referenced package types the sample wires (CharacterAgent.AllLive,
    /// WorldInteractable.AllLive, CharacterAgent.Session), so this asmdef still compiles even in a project
    /// (like this package's own TestProject) that never imports the sample at all; see the SetUp guard below
    /// for that case. Ignored (not failed) when no scene is registered, so a normal EditMode/PlayMode run of
    /// crpg-toolkit's own test suite -- which never imports its own samples -- stays green.
    /// </summary>
    public class BasicCrpgVerificationTests
    {
        private static readonly DefinitionId TreasureItemId = new("item-treasure-coin-basiccrpg");

        private sealed class NoCapture : IPointerCaptureProvider
        {
            public bool IsPointerOverUI(Vector2 screenPosition) => false;
        }

        [UnityTest]
        public IEnumerator PartyMovesOnGroundClickAndCanLootTheChestOnInteractClick()
        {
            var buildIndex = FindBasicCrpgSceneBuildIndex();
            if (buildIndex < 0)
            {
                Assert.Ignore("No scene whose path contains \"BasicCRPG\" is registered in Build Settings -- " +
                               "this fixture only runs inside the Task 5.3 clean-install harness, which adds " +
                               "the imported BasicCRPG sample scene before running tests.");
                yield break;
            }

            yield return SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            for (var i = 0; i < 5; i++) yield return null; // let BasicCrpgBootstrap.Start()/NavMesh placement settle.

            // A just-loaded scene's colliders are not reliably queryable via Physics.Raycast after only a
            // rendered frame or two -- confirmed live elsewhere in this package (PointerCommandTests' own
            // WaitForColliders). FixedUpdate is what actually steps the physics world; SyncTransforms is a
            // second safety net rather than trusting frame timing alone.
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var agents = CharacterAgent.AllLive.ToList();
            Assert.That(agents.Count, Is.GreaterThanOrEqualTo(2), "Expected BasicCRPG's two party members to be live.");

            var session = agents[0].Session;
            Assert.IsNotNull(session, "Party members must already be bound to a real session.");

            yield return VerifyGroundClickMovesTheParty(agents, session);
            yield return VerifyChestClickLootsTheTreasure(session);
        }

        private static IEnumerator VerifyGroundClickMovesTheParty(System.Collections.Generic.List<CharacterAgent> agents, GameSession session)
        {
            var router = new PlayerCommandRouter(new SceneSelectableSource(), new NoCapture(), ~0);
            var movementService = new PartyMovementService(new NavMeshService(), new SceneSelectableSource());
            var exploration = new PartyExplorationController(session.Party, movementService, new WedgeFormationSolver(), router);
            foreach (var agent in agents)
            {
                if (agent.Motor != null) exploration.RegisterMotor(agent.EntityId, agent.Motor);
            }

            var before = agents.Select(a => a.transform.position).ToList();
            ClickWorldPoint(router, new Vector3(10f, 0f, -6f));

            // Batchmode -nographics runs frames far faster than real time (each yield return null can advance
            // simulated Time.deltaTime by a tiny fraction of a second) -- confirmed live: 300 frames accumulated
            // well under half a second of simulated time. A generous frame cap, not a generous WALL-clock guess,
            // is what actually lets a NavMeshAgent's real, speed-limited transit complete.
            var moved = false;
            for (var i = 0; i < 5000 && !moved; i++)
            {
                yield return null;
                moved = agents.Select((a, idx) => Vector3.Distance(a.transform.position, before[idx])).Any(d => d > 1f);
            }

            exploration.Dispose();
            Assert.IsTrue(moved, "No party member's position changed by more than 1 unit after a ground click -- click-to-move did not relocate the party.");
        }

        private static IEnumerator VerifyChestClickLootsTheTreasure(GameSession session)
        {
            var chest = WorldInteractable.AllLive.FirstOrDefault();
            Assert.IsNotNull(chest, "Expected BasicCRPG's one WorldInteractable (the treasure chest) to be live.");
            Assert.IsFalse(session.Inventory.AllItems.Any(item => item.DefinitionId == TreasureItemId),
                "Treasure must not already be in the party's inventory before interacting.");

            var router = new PlayerCommandRouter(new SceneSelectableSource(), new NoCapture(), ~0);
            var interactionHostGo = new GameObject("Test_InteractionHost");
            var interactionHost = interactionHostGo.AddComponent<InteractionControllerBehaviour>();
            interactionHost.Initialize(router, session.Party, session);

            // Clicks the chest's own body, NOT chest.InteractionPoint -- that's the approach ANCHOR standing
            // beside it (by design, so the anchor itself was never a valid click target for the chest).
            ClickWorldPoint(router, chest.transform.position);

            var looted = false;
            for (var i = 0; i < 8000 && !looted; i++)
            {
                yield return null;
                looted = session.Inventory.AllItems.Any(item => item.DefinitionId == TreasureItemId);
            }

            Object.DestroyImmediate(interactionHostGo);
            Assert.IsTrue(looted, "The treasure never appeared in the party's inventory after approaching and interacting with the chest.");
        }

        private static int FindBasicCrpgSceneBuildIndex()
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                if (SceneUtility.GetScenePathByBuildIndex(i).Contains("BasicCRPG")) return i;
            }

            return -1;
        }

        // Built as a direct downward ray, NOT via Camera.WorldToScreenPoint/ScreenPointToRay -- confirmed live
        // that camera-based screen-space round-tripping is unreliable in batchmode -nographics (Screen/viewport
        // dimensions are degenerate there). Matches PointerCommandTests' own identical RayFromAbove precedent;
        // the screenPosition argument only feeds the drag-threshold/UI-capture checks, not the raycast itself.
        private static void ClickWorldPoint(PlayerCommandRouter router, Vector3 worldPoint)
        {
            var ray = new Ray(new Vector3(worldPoint.x, worldPoint.y + 20f, worldPoint.z), Vector3.down);
            router.HandlePointerDown(Vector2.zero, ray);
            router.HandlePointerUp(Vector2.zero, ray, camera: null, shiftHeld: false);
        }
    }
}
