using System.Collections;
using CRPG.Actors;
using CRPG.Consumer.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Consumer.Fate.Tests
{
    /// <summary>
    /// Loads the real authored ConsumerGame scene and lets it run for a few real seconds -- proves the whole
    /// composition root (session start through the FateRulesetProvider, primitive-character spawning,
    /// exploration wiring, trigger zones, HUD) boots with zero console errors, which a pure EditMode test
    /// cannot observe (MonoBehaviour.Start/Update, NavMeshAgent binding, and UI Toolkit runtime panels all
    /// need a real Play session -- this project's own established rule).
    /// </summary>
    public class ConsumerGameSceneSmokeTests
    {
        [UnityTest]
        public IEnumerator TheAuthoredSceneBootsWithTwoPartyMembersAndNoConsoleErrors()
        {
            LogAssert.ignoreFailingMessages = false;

            yield return SceneManager.LoadSceneAsync("Assets/Scenes/ConsumerGame.unity", LoadSceneMode.Single);

            var elapsed = 0f;
            ConsumerGameBootstrap bootstrap = null;
            while (elapsed < 5f)
            {
                bootstrap = Object.FindAnyObjectByType<ConsumerGameBootstrap>();
                if (bootstrap != null && bootstrap.Host?.Session != null) break;
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsNotNull(bootstrap, "ConsumerGameBootstrap must exist in the authored scene.");
            Assert.IsNotNull(bootstrap.Host?.Session, "The session must have started.");

            var session = bootstrap.Host.Session;
            Assert.AreEqual(2, session.Party.Members.Count, "The compact party is exactly 2 members.");
            Assert.IsTrue(session.Party.IsSelected(bootstrap.HeroAId), "SelectAll must have selected both party members.");
            Assert.IsTrue(session.Party.IsSelected(bootstrap.HeroBId));

            Assert.IsTrue(session.Characters.TryGetCharacter(bootstrap.HostileId, out _), "The hostile must be a real registered character.");
            Assert.IsTrue(session.Characters.TryGetCharacter(bootstrap.NpcId, out _), "The dialogue NPC must be a real registered character.");
            Assert.IsTrue(session.Policies.TryGet<ISkillCheckRule>(out _), "The Fate check rule must be registered.");

            Assert.IsFalse(bootstrap.CombatStarted, "Combat must not auto-start before a party member walks into range.");
        }
    }
}
