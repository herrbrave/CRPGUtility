using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>Slice 4 of NPC services (<c>Docs/Plans/Toolkit-NpcServices.md</c>): recruiting a spawned NPC into the party.</summary>
    public sealed class NpcRecruitmentTests
    {
        private sealed class Content : ILocationContent
        {
            public readonly List<NpcSpawnPoint> Points = new();
            public IReadOnlyList<NpcSpawnPoint> SpawnPoints => Points;
            public IReadOnlyList<StoreContentPoint> Stores => new List<StoreContentPoint>();
            public IReadOnlyList<LootContentPoint> LootContainers => new List<LootContentPoint>();
            public Rect? CameraBoundsXZ => null;
        }

        private readonly List<Object> toDestroy = new();
        private GameSession session;
        private WorldSpawnService spawner;
        private PanelSettings panel;
        private Content content;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            spawner = new WorldSpawnService(session);
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            toDestroy.Add(panel);
            content = new Content();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in toDestroy) if (o != null) Object.DestroyImmediate(o);
            toDestroy.Clear();
            session.Dispose();
        }

        private DialogueDefinition AnyDialogue()
        {
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            toDestroy.Add(dialogue);
            TestDefinitionFactory.SetPrivateField(dialogue, "id", "dlg-any");
            return dialogue;
        }

        private CharacterAgent Villager(string spawnId, bool routine = true, bool dialogue = true)
        {
            var definition = TestDefinitionFactory.CreateNpcDefinition($"npc-{spawnId}", spawnId);
            var visual = new GameObject($"{spawnId}-visual");
            toDestroy.Add(visual);
            TestDefinitionFactory.SetPrivateField(definition, "visualPrefab", visual);
            TestDefinitionFactory.SetPrivateField(definition, "faction", new DefinitionId("faction-villagers"));
            toDestroy.Add(definition);
            var anchor = new GameObject($"{spawnId}-anchor");
            toDestroy.Add(anchor);
            var agent = spawner.SpawnNpc(spawnId, definition, Vector3.zero, Quaternion.identity);
            toDestroy.Add(agent.gameObject);
            content.Points.Add(new NpcSpawnPoint
            {
                SpawnId = spawnId, Definition = definition, Anchor = anchor.transform,
                Dialogue = dialogue ? AnyDialogue() : null,
                Routine = routine ? new NpcRoutineSpec { Kind = NpcRoutineKind.Wander } : new NpcRoutineSpec(),
            });
            return agent;
        }

        private void MakeTownspeople() => NpcRoutines.AttachAll(content, spawner);

        private void MakeTalkable() => NpcDialogues.AttachAll(content, spawner, session, new NpcDialogueOptions { PanelSettings = panel });

        [Test]
        public void Recruit_PutsTheNpcsOwnCharacterInTheParty_AndStripsEverythingThatMadeItATownsperson()
        {
            var ragna = Villager("ragna");
            MakeTownspeople();
            MakeTalkable();
            Assert.IsNotNull(ragna.GetComponent<NpcRoutineRunner>());
            Assert.IsNotNull(ragna.GetComponent<TalkInteractionAdapter>());
            CharacterAgent joined = null;
            var recruitment = new NpcRecruitment(spawner, session, a => joined = a);

            Assert.IsTrue(recruitment.Recruit("ragna"));

            Assert.IsTrue(session.Party.IsMember(ragna.EntityId), "the same character, not a second one");
            Assert.AreSame(ragna, joined, "the game's callback got the agent");
            Assert.IsNull(ragna.GetComponent<NpcRoutineRunner>());
            Assert.IsNull(ragna.GetComponent<TalkInteractionAdapter>());
            Assert.IsNull(ragna.GetComponent<WorldInteractable>());
            Assert.IsNull(ragna.GetComponent<NpcConversationFacing>());
        }

        [Test]
        public void Recruit_TakesThePartysFaction_WhenGiven_AndKeepsItsOwnOtherwise()
        {
            var ragna = Villager("ragna");
            var keeps = new NpcRecruitment(spawner, session);
            var other = Villager("sigrid");
            var takes = new NpcRecruitment(spawner, session, partyFaction: new DefinitionId("faction-party"));

            takes.Recruit("sigrid");
            keeps.Recruit("ragna");

            spawner.TryGetFaction(other.EntityId, out var otherFaction);
            spawner.TryGetFaction(ragna.EntityId, out var ragnaFaction);
            Assert.AreEqual(new DefinitionId("faction-party"), otherFaction);
            Assert.AreEqual(new DefinitionId("faction-villagers"), ragnaFaction);
        }

        [Test]
        public void Recruit_IsRefusedForAnUnknownNpc_ADefeatedOne_AndOneAlreadyInTheParty()
        {
            var ragna = Villager("ragna");
            var recruitment = new NpcRecruitment(spawner, session);
            var calls = 0;
            recruitment = new NpcRecruitment(spawner, session, _ => calls++);

            Assert.IsFalse(recruitment.Recruit("nobody"));
            Assert.IsFalse(recruitment.Recruit(null));
            Assert.IsTrue(recruitment.Recruit("ragna"));
            Assert.IsFalse(recruitment.Recruit("ragna"), "already a member");
            Assert.AreEqual(1, calls);

            var fallen = Villager("fallen");
            spawner.MarkDefeated(fallen.EntityId);
            Assert.IsFalse(recruitment.Recruit("fallen"));
        }

        [Test]
        public void RunningTheAttachCallsAgain_NeverMakesACompanionATownspersonAgain()
        {
            var ragna = Villager("ragna");
            MakeTownspeople();
            MakeTalkable();
            new NpcRecruitment(spawner, session).Recruit("ragna");

            Assert.AreEqual(0, NpcRoutines.AttachAll(content, spawner), "no routine for a party member (a load re-runs this)");
            Assert.AreEqual(0, NpcDialogues.AttachAll(content, spawner, session, new NpcDialogueOptions { PanelSettings = panel }));
            Assert.IsNull(ragna.GetComponent<NpcRoutineRunner>());
            Assert.IsNull(ragna.GetComponent<TalkInteractionAdapter>());
        }

        [Test]
        public void Queue_RecruitsAtOnce_WhenTheNpcIsNotInAConversation()
        {
            var ragna = Villager("ragna", routine: false, dialogue: false);
            var recruitment = new NpcRecruitment(spawner, session);

            Assert.IsTrue(recruitment.Queue("ragna"));

            Assert.IsTrue(session.Party.IsMember(ragna.EntityId));
            Assert.IsFalse(recruitment.IsPending("ragna"));
        }

        [Test]
        public void Queue_WithATalkAdapter_WaitsForTheConversationToEnd_InsteadOfTearingItDown()
        {
            var ragna = Villager("ragna");
            MakeTalkable();
            var recruitment = new NpcRecruitment(spawner, session);

            Assert.IsTrue(recruitment.Queue("ragna"));

            Assert.IsTrue(recruitment.IsPending("ragna"));
            Assert.IsFalse(session.Party.IsMember(ragna.EntityId), "not yet: the conversation is still on");
            Assert.IsNotNull(ragna.GetComponent<TalkInteractionAdapter>(), "and the adapter that is running it is untouched");

            recruitment.Dispose();
            Assert.IsFalse(recruitment.IsPending("ragna"));
        }

        [Test]
        public void Queue_ForAnNpcAlreadyInTheParty_OrUnknown_IsRefused()
        {
            var ragna = Villager("ragna", routine: false, dialogue: false);
            var recruitment = new NpcRecruitment(spawner, session);
            recruitment.Recruit("ragna");

            Assert.IsFalse(recruitment.Queue("ragna"));
            Assert.IsFalse(recruitment.Queue("nobody"));
        }

        [Test]
        public void TheDialoguesRecruitAction_ReachesTheGameWithTheSpawnIdOfTheNpcThatIsTalking()
        {
            Villager("ragna");
            Villager("sigrid");
            var seen = new List<(string spawn, string key)>();
            var options = new NpcDialogueOptions { PanelSettings = panel, RequestRecruitSpawn = (spawn, key) => seen.Add((spawn, key)) };

            NpcDialogues.AttachAll(content, spawner, session, options);
            foreach (var agent in spawner.OwnedAgents)
            {
                var adapter = agent.GetComponent<TalkInteractionAdapter>();
                adapter.RequestRecruit("join");
            }

            CollectionAssert.AreEquivalent(new[] { ("ragna", "join"), ("sigrid", "join") }, seen, "each adapter knows which NPC it belongs to");
        }

        [Test]
        public void AGamesOwnRecruitDelegate_StillWorks_WhenNoSpawnAwareOneIsGiven()
        {
            Villager("ragna");
            string key = null;
            var options = new NpcDialogueOptions { PanelSettings = panel, RequestRecruit = k => key = k };

            NpcDialogues.AttachAll(content, spawner, session, options);
            spawner.OwnedAgents.First().GetComponent<TalkInteractionAdapter>().RequestRecruit("companion-garrick");

            Assert.AreEqual("companion-garrick", key, "Barrow's End's separate-companion path is unchanged");
        }

        [Test]
        public void ACompanionKeyIsReadableForValidation()
        {
            var action = new RecruitToPartyAction();
            TestDefinitionFactory.SetPrivateField(action, "companionKey", "join");
            Assert.AreEqual("join", action.CompanionKey);
        }
    }
}
