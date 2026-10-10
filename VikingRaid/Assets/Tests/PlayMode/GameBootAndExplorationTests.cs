using System.Collections;
using System.Linq;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>The first half of the game loop on the real scene: the game boots with the right cast in the right places, and exploration works through the real click router.</summary>
    public class GameBootAndExplorationTests : GameTestBase
    {
        [UnityTest]
        public IEnumerator TheGameBootsWithFourHeroesOnTheStartSlotsAndFourteenGoblinsIdleOnTheirAnchors()
        {
            Assert.AreEqual(GameState.Exploring, Game.State);
            Assert.AreEqual(4, Game.Heroes.Count);
            for (var i = 0; i < 4; i++)
            {
                Assert.IsTrue(Game.Session.Party.IsMember(Game.Heroes[i]));
                Assert.Less(Vector3.Distance(Position(Game.Heroes[i]), Game.Anchors.startSlots[i].position), 0.5f, $"hero {i} is not on its start slot");
                Assert.IsTrue(NavMesh.SamplePosition(Position(Game.Heroes[i]), out _, 0.5f, NavMesh.AllAreas));
            }

            var total = 0;
            for (var e = 0; e < 4; e++)
            {
                var goblins = Game.GoblinsOf(e);
                Assert.AreEqual(Game.Anchors.encounters[e].goblinUnitIds.Length, goblins.Count, $"encounter {e + 1}");
                for (var j = 0; j < goblins.Count; j++)
                {
                    Assert.Less(Vector3.Distance(Position(goblins[j].Id), Game.Anchors.encounters[e].goblinSpawns[j].position), 0.5f, $"{goblins[j].UnitId} is not on its spawn anchor");
                    total++;
                }
            }

            Assert.AreEqual(14, total);
            Assert.IsFalse(Game.Combat.IsActive, "no fight yet");
            Assert.AreEqual(0, Checkpoint.Index);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickingTheGroundAheadWalksTheWholePartyThereInFormationOnTheNavMesh()
        {
            Time.timeScale = 4f;
            var before = PartyCentre();
            var target = WorldLayout.PointAt(WorldLayout.Arenas[0].WestS - 40f);       // on the road ahead, outside every trigger
            Click(target);

            yield return WaitUntil(() => Vector3.Distance(PartyCentre(), target) < 4f, 40f, "the party to reach the clicked point");
            foreach (var hero in Game.Heroes)
            {
                Assert.IsTrue(NavMesh.SamplePosition(Position(hero), out var hit, 0.3f, NavMesh.AllAreas), "off the NavMesh");
                Assert.Less(Vector3.Distance(Position(hero), target), 6f, "the formation is spread too far");
            }

            Assert.Greater(Vector3.Distance(PartyCentre(), before), 20f);
            Assert.AreEqual(GameState.Exploring, Game.State);
        }

        [UnityTest]
        public IEnumerator ClickingAGoblinInExplorationNeitherThrowsNorChangesTheSelection()
        {
            var selectionBefore = Game.Session.Party.Members.Where(Game.Session.Party.IsSelected).ToList();
            var goblin = Game.GoblinsOf(0)[0].Id;
            // the router offers every live agent (goblins included) to Select; the party-only filter must swallow it instead of letting PartyState.Select throw
            Click(Position(goblin) + Vector3.up * 1f);
            Click(Position(goblin) + Vector3.up * 1f);
            yield return null;
            AssertNoErrorsLogged();
            CollectionAssert.AreEquivalent(selectionBefore, Game.Session.Party.Members.Where(Game.Session.Party.IsSelected).ToList());
            Assert.AreEqual(GameState.Exploring, Game.State);
        }
    }
}
