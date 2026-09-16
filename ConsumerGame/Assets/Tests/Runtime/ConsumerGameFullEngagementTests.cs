using System.Collections;
using System.IO;
using System.Linq;
using CRPG.Consumer.Game;
using CRPG.Core;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Consumer.Fate.Tests
{
    /// <summary>
    /// Drives the actual authored scene through the whole compact loop -- walk to the hostile, fight it with
    /// real Fate combat resolution to a genuine win, autosave, then an independent load -- the same
    /// "real yielded frames, not manual off-thread ticking" discipline this project settled on for
    /// crpg-toolkit's own FullEngagementTests (Task 4.5). Uses <see cref="ConsumerGameBootstrap.RequestStrike"/>/
    /// <see cref="ConsumerGameBootstrap.RequestLoad"/> instead of simulating real mouse clicks, mirroring
    /// <c>TacticalPlayerMoveAdapter.TryHandleMove</c>'s own public-for-testability precedent -- movement itself
    /// still goes through the real NavMeshAgent/IMovable path, not a teleport.
    /// </summary>
    public class ConsumerGameFullEngagementTests
    {
        [UnityTest]
        public IEnumerator WalkingToTheHostileStartsCombatWhichResolvesToARealWinThenSavesAndLoads()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/ConsumerGame.unity", LoadSceneMode.Single);

            ConsumerGameBootstrap bootstrap = null;
            var elapsed = 0f;
            while (elapsed < 5f)
            {
                bootstrap = Object.FindAnyObjectByType<ConsumerGameBootstrap>();
                if (bootstrap != null && bootstrap.Host?.Session != null) break;
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsNotNull(bootstrap?.Host?.Session, "The scene must boot a real session.");

            var heroAgent = Object.FindObjectsByType<CRPG.Gameplay.CharacterAgent>(FindObjectsSortMode.None)
                .First(a => a.EntityId == bootstrap.HeroAId);
            heroAgent.Motor.SetDestination(bootstrap.HostileSpawnPosition);

            elapsed = 0f;
            while (elapsed < 20f && !bootstrap.CombatStarted)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(bootstrap.CombatStarted, "Walking into range must start combat via the real trigger zone.");

            elapsed = 0f;
            while (elapsed < 30f && !bootstrap.CombatWon)
            {
                bootstrap.RequestStrike(false); // no-ops if it isn't a party member's turn; the hostile's own turn auto-resolves in Update()
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(bootstrap.CombatWon, "A real Fate combat resolution against a single weak hostile must reach a win within the budget.");

            // The bootstrap auto-saves on victory -- confirm the file actually landed before proving load.
            var savePath = Path.Combine(Application.persistentDataPath, "consumer-fate-save.json");
            elapsed = 0f;
            while (elapsed < 3f && !File.Exists(savePath))
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(File.Exists(savePath), "Victory must have triggered a real autosave to disk.");

            var fileResult = new SaveFileStore().Load(savePath);
            Assert.AreEqual(SaveLoadOutcome.Success, fileResult.Outcome);
            Assert.AreEqual("fate-condensed", fileResult.Data.Header.RulesetId, "The save must record the real Fate ruleset id.");

            bootstrap.RequestLoad();
            yield return null;
            yield return null;

            Assert.IsTrue(bootstrap.Host.Session.Characters.TryGetCharacter(bootstrap.HeroAId, out _), "Hero A must still resolve after a real load.");
            Assert.IsTrue(bootstrap.Host.Session.Policies.TryGet<CRPG.Actors.ISkillCheckRule>(out _), "The Fate check rule must still be registered after a real load.");
        }
    }
}
