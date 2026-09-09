using CRPG.Combat;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class CombatEncounterTests
    {
        [Test]
        public void StartSortsCombatantsByInitiativeDescending()
        {
            var low = RuntimeEntityId.NewId();
            var high = RuntimeEntityId.NewId();
            var mid = RuntimeEntityId.NewId();
            var encounter = new CombatEncounter();
            encounter.AddCombatant(low, 5);
            encounter.AddCombatant(high, 18);
            encounter.AddCombatant(mid, 10);

            encounter.Start();

            Assert.AreEqual(high, encounter.Combatants[0].Id);
            Assert.AreEqual(mid, encounter.Combatants[1].Id);
            Assert.AreEqual(low, encounter.Combatants[2].Id);
            Assert.AreEqual(high, encounter.CurrentTurn.Id);
        }

        [Test]
        public void AdvanceTurnWrapsToNextRoundAfterLastCombatant()
        {
            var a = RuntimeEntityId.NewId();
            var b = RuntimeEntityId.NewId();
            var encounter = new CombatEncounter();
            encounter.AddCombatant(a, 20);
            encounter.AddCombatant(b, 10);
            encounter.Start();

            encounter.AdvanceTurn(); // -> b
            Assert.AreEqual(b, encounter.CurrentTurn.Id);
            Assert.AreEqual(1, encounter.Round);

            encounter.AdvanceTurn(); // wraps -> a, round 2
            Assert.AreEqual(a, encounter.CurrentTurn.Id);
            Assert.AreEqual(2, encounter.Round);
        }

        [Test]
        public void RemoveCombatantDropsThemFromTurnOrder()
        {
            var a = RuntimeEntityId.NewId();
            var b = RuntimeEntityId.NewId();
            var c = RuntimeEntityId.NewId();
            var encounter = new CombatEncounter();
            encounter.AddCombatant(a, 20);
            encounter.AddCombatant(b, 10);
            encounter.AddCombatant(c, 5);
            encounter.Start();

            encounter.RemoveCombatant(b);

            Assert.AreEqual(2, encounter.Combatants.Count);
            Assert.AreEqual(a, encounter.CurrentTurn.Id);
        }

        [Test]
        public void GetEnemiesOfReturnsCombatantsWithADifferentFaction()
        {
            var heroes = new DefinitionId("faction-heroes");
            var goblins = new DefinitionId("faction-goblins");
            var aldric = RuntimeEntityId.NewId();
            var brynn = RuntimeEntityId.NewId();
            var grik = RuntimeEntityId.NewId();
            var encounter = new CombatEncounter();
            encounter.AddCombatant(aldric, 20, heroes);
            encounter.AddCombatant(brynn, 15, heroes);
            encounter.AddCombatant(grik, 10, goblins);
            encounter.Start();

            var enemies = encounter.GetEnemiesOf(aldric);

            Assert.AreEqual(1, enemies.Count);
            Assert.AreEqual(grik, enemies[0]);
        }

        [Test]
        public void GetAlliesOfReturnsCombatantsWithTheSameFactionExcludingSelf()
        {
            var heroes = new DefinitionId("faction-heroes");
            var goblins = new DefinitionId("faction-goblins");
            var aldric = RuntimeEntityId.NewId();
            var brynn = RuntimeEntityId.NewId();
            var grik = RuntimeEntityId.NewId();
            var encounter = new CombatEncounter();
            encounter.AddCombatant(aldric, 20, heroes);
            encounter.AddCombatant(brynn, 15, heroes);
            encounter.AddCombatant(grik, 10, goblins);
            encounter.Start();

            var allies = encounter.GetAlliesOf(aldric);

            Assert.AreEqual(1, allies.Count);
            Assert.AreEqual(brynn, allies[0]);
        }

        [Test]
        public void GetEnemiesOfAndGetAlliesOfExcludeCombatantsAlreadyRemoved()
        {
            var heroes = new DefinitionId("faction-heroes");
            var goblins = new DefinitionId("faction-goblins");
            var aldric = RuntimeEntityId.NewId();
            var grik = RuntimeEntityId.NewId();
            var snarl = RuntimeEntityId.NewId();
            var encounter = new CombatEncounter();
            encounter.AddCombatant(aldric, 20, heroes);
            encounter.AddCombatant(grik, 10, goblins);
            encounter.AddCombatant(snarl, 5, goblins);
            encounter.Start();

            encounter.RemoveCombatant(grik);

            var enemies = encounter.GetEnemiesOf(aldric);
            Assert.AreEqual(1, enemies.Count);
            Assert.AreEqual(snarl, enemies[0]);
        }

        [Test]
        public void GetEnemiesOfAndGetAlliesOfAreEmptyForAnUnknownCombatant()
        {
            var encounter = new CombatEncounter();
            encounter.AddCombatant(RuntimeEntityId.NewId(), 20);
            encounter.Start();

            Assert.AreEqual(0, encounter.GetEnemiesOf(RuntimeEntityId.NewId()).Count);
            Assert.AreEqual(0, encounter.GetAlliesOf(RuntimeEntityId.NewId()).Count);
        }
    }
}
