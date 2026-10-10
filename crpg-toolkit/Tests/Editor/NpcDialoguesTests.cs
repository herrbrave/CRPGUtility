using System.Collections.Generic;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>Making spawned NPCs talkable (design <c>Docs/Plans/Toolkit-NpcDialogue.md</c>): the components <see cref="NpcDialogues.AttachAll"/> adds, its idempotent re-wiring, dialogue resolution, and the
    /// <see cref="WorldInteractable"/> changes it relies on. The conversation itself (UI) is in the PlayMode tests.</summary>
    public sealed class NpcDialoguesTests
    {
        private sealed class Content : ILocationContent
        {
            public readonly List<NpcSpawnPoint> Points = new();
            public IReadOnlyList<NpcSpawnPoint> SpawnPoints => Points;
            public IReadOnlyList<StoreContentPoint> Stores => new List<StoreContentPoint>();
            public IReadOnlyList<LootContentPoint> LootContainers => new List<LootContentPoint>();
            public Rect? CameraBoundsXZ => null;
        }

        private sealed class OtherAdapter : MonoBehaviour, IInteractionAdapter
        {
            public DefinitionId ActionId { get; } = new("other");
            public bool IsAvailable(RuntimeEntityId performer) => true;
            public IInteractionExecution BeginExecute(RuntimeEntityId performer, System.Action<InteractionExecutionResult> onFinished) => null;
        }

        private readonly List<Object> toDestroy = new();
        private RulesDatabase rules;
        private GameSession session;
        private WorldSpawnService spawner;
        private PanelSettings panel;

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase();
            session = new GameSession(rules, new DiceService(new SeededRandomSource(1)));
            spawner = new WorldSpawnService(session);
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            toDestroy.Add(panel);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in toDestroy) if (o != null) Object.DestroyImmediate(o);
            toDestroy.Clear();
            session.Dispose();
        }

        private NpcDialogueOptions Options(PanelSettings settings = null) => new() { PanelSettings = settings != null ? settings : panel };

        private DialogueDefinition Dialogue(string id)
        {
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            TestDefinitionFactory.SetPrivateField(dialogue, "id", id);
            toDestroy.Add(dialogue);
            return dialogue;
        }

        private NpcSpawnPoint Npc(string spawnId, DialogueDefinition dialogue, string dialogueId = null, bool spawn = true)
        {
            var definition = TestDefinitionFactory.CreateNpcDefinition($"npc-{spawnId}", spawnId);
            var visual = new GameObject($"{spawnId}-visual");
            toDestroy.Add(visual);
            TestDefinitionFactory.SetPrivateField(definition, "visualPrefab", visual);
            if (dialogueId != null) TestDefinitionFactory.SetPrivateField(definition, "dialogueId", new DefinitionId(dialogueId));
            toDestroy.Add(definition);

            var anchor = new GameObject($"{spawnId}-anchor");
            toDestroy.Add(anchor);
            if (spawn) toDestroy.Add(spawner.SpawnNpc(spawnId, definition, Vector3.zero, Quaternion.identity).gameObject);
            return new NpcSpawnPoint { SpawnId = spawnId, Definition = definition, Anchor = anchor.transform, Dialogue = dialogue };
        }

        private CharacterAgent AgentOf(string spawnId)
        {
            spawner.TryGetCharacterId(spawnId, out var id, out _, out _);
            foreach (var agent in spawner.OwnedAgents) if (agent.EntityId == id) return agent;
            return null;
        }

        [Test]
        public void AttachAll_MakesAnNpcWithADialogueTalkable()
        {
            var content = new Content();
            var dialogue = Dialogue("dlg-smith");
            content.Points.Add(Npc("smith", dialogue));

            Assert.AreEqual(1, NpcDialogues.AttachAll(content, spawner, session, Options()));

            var agent = AgentOf("smith");
            var interactable = agent.GetComponent<WorldInteractable>();
            var adapter = agent.GetComponent<TalkInteractionAdapter>();
            Assert.IsNotNull(interactable);
            Assert.IsNotNull(adapter);
            Assert.IsTrue(interactable.IsConfigured, "the adapter was found after Configure");
            Assert.AreSame(adapter, interactable.Adapter);
            Assert.IsTrue(interactable.TakesClickOverSelection);
            Assert.IsTrue(interactable.CompleteApproachInRange);
            Assert.AreEqual(1.8f, interactable.InteractionRange, 0.001f);
            Assert.AreSame(agent.transform, interactable.transform, "its own transform is the anchor");
            Assert.AreSame(dialogue, adapter.Dialogue);
            Assert.AreSame(panel, adapter.PanelSettings);
            Assert.AreSame(session.Party, adapter.Party);
            Assert.IsTrue(adapter.IsAvailable(agent.EntityId), "a configured adapter with a dialogue is available");
        }

        [Test]
        public void AttachAll_LeavesNpcsWithoutADialogueAlone()
        {
            var content = new Content();
            content.Points.Add(Npc("mute", null));

            Assert.AreEqual(0, NpcDialogues.AttachAll(content, spawner, session, Options()));
            Assert.IsNull(AgentOf("mute").GetComponent<WorldInteractable>());
        }

        [Test]
        public void AttachAll_AddsTheComponentsOnce_ButRewiresEveryTime()
        {
            var content = new Content();
            content.Points.Add(Npc("smith", Dialogue("dlg-smith")));
            Assert.AreEqual(1, NpcDialogues.AttachAll(content, spawner, session, Options()));
            var agent = AgentOf("smith");
            var adapter = agent.GetComponent<TalkInteractionAdapter>();

            var otherPanel = ScriptableObject.CreateInstance<PanelSettings>();
            toDestroy.Add(otherPanel);
            var saved = 0;
            var options = Options(otherPanel);
            options.RequestSave = () => saved++;
            Assert.AreEqual(0, NpcDialogues.AttachAll(content, spawner, session, options), "nothing new was made talkable");

            Assert.AreEqual(1, agent.GetComponents<WorldInteractable>().Length);
            Assert.AreEqual(1, agent.GetComponents<TalkInteractionAdapter>().Length);
            Assert.AreSame(adapter, agent.GetComponent<TalkInteractionAdapter>());
            Assert.AreSame(otherPanel, adapter.PanelSettings, "re-wired");
            adapter.RequestSave();
            Assert.AreEqual(1, saved, "the new hook is in place");
        }

        [Test]
        public void AttachAll_RewiresToANewSession_SoAnAdapterNeverPointsAtADisposedOne()
        {
            var content = new Content();
            content.Points.Add(Npc("smith", Dialogue("dlg-smith")));
            NpcDialogues.AttachAll(content, spawner, session, Options());
            var adapter = AgentOf("smith").GetComponent<TalkInteractionAdapter>();

            var next = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(2)));
            try
            {
                NpcDialogues.AttachAll(content, spawner, next, Options());
                Assert.AreSame(next.Party, adapter.Party);
                Assert.AreSame(next.Timeline, adapter.Timeline);
            }
            finally
            {
                next.Dispose();
            }
        }

        [Test]
        public void ResolveDialogue_PrefersTheSpawnPointsOwn_ThenTheDefinitionsIdThroughTheRuleset()
        {
            var own = Dialogue("dlg-own");
            var registered = Dialogue("dlg-registered");
            rules.Register(registered);

            var withOwn = Npc("a", own, dialogueId: "dlg-registered", spawn: false);
            Assert.AreSame(own, NpcDialogues.ResolveDialogue(withOwn, session), "the spawn point's reference wins");

            var viaId = Npc("b", null, dialogueId: "dlg-registered", spawn: false);
            Assert.AreSame(registered, NpcDialogues.ResolveDialogue(viaId, session), "else the id, if the ruleset has it");

            var unregistered = Npc("c", null, dialogueId: "dlg-unknown", spawn: false);
            Assert.IsNull(NpcDialogues.ResolveDialogue(unregistered, session));
            Assert.IsNull(NpcDialogues.ResolveDialogue(Npc("d", null, spawn: false), session));
            Assert.IsNull(NpcDialogues.ResolveDialogue(null, session));
        }

        [Test]
        public void AttachAll_UsesTheRulesetFallbackForAnNpcWhoseDialogueIsRegistered()
        {
            var registered = Dialogue("dlg-registered");
            rules.Register(registered);
            var content = new Content();
            content.Points.Add(Npc("elder", null, dialogueId: "dlg-registered"));

            Assert.AreEqual(1, NpcDialogues.AttachAll(content, spawner, session, Options()));
            Assert.AreSame(registered, AgentOf("elder").GetComponent<TalkInteractionAdapter>().Dialogue);
        }

        [Test]
        public void AttachAll_WithoutPanelSettings_ReportsItAndMakesNobodyTalkable()
        {
            var content = new Content();
            content.Points.Add(Npc("smith", Dialogue("dlg-smith")));

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("PanelSettings is required"));
            Assert.AreEqual(0, NpcDialogues.AttachAll(content, spawner, session, new NpcDialogueOptions()));
            Assert.IsNull(AgentOf("smith").GetComponent<WorldInteractable>());
        }

        [Test]
        public void AttachAll_SkipsAnNpcThatAlreadyHasAnotherInteractionAdapter()
        {
            var content = new Content();
            content.Points.Add(Npc("trader", Dialogue("dlg-trader")));
            AgentOf("trader").gameObject.AddComponent<OtherAdapter>();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("already has an interaction adapter"));
            Assert.AreEqual(0, NpcDialogues.AttachAll(content, spawner, session, Options()));
            Assert.IsNull(AgentOf("trader").GetComponent<TalkInteractionAdapter>());
        }

        [Test]
        public void AttachAll_SkipsDefeatedNpcs_NeverSpawnedOnes_AndNullArguments()
        {
            var content = new Content();
            content.Points.Add(Npc("fallen", Dialogue("dlg-1")));
            content.Points.Add(Npc("ghost", Dialogue("dlg-2"), spawn: false));
            content.Points.Add(null);
            spawner.TryGetCharacterId("fallen", out var id, out _, out _);
            spawner.MarkDefeated(id);

            Assert.AreEqual(0, NpcDialogues.AttachAll(content, spawner, session, Options()));
            Assert.AreEqual(0, NpcDialogues.AttachAll(null, spawner, session, Options()));
            Assert.AreEqual(0, NpcDialogues.AttachAll(content, null, session, Options()));
            Assert.AreEqual(0, NpcDialogues.AttachAll(content, spawner, null, Options()));
        }

        [Test]
        public void AttachAll_WorksOnAnInactiveStagedView_WithoutActivatingIt()
        {
            var content = new Content();
            content.Points.Add(Npc("staged", Dialogue("dlg-staged")));
            var agent = AgentOf("staged");
            agent.gameObject.SetActive(false);

            Assert.AreEqual(1, NpcDialogues.AttachAll(content, spawner, session, Options()));

            Assert.IsFalse(agent.gameObject.activeSelf, "a staged location's NPCs stay inactive until the caller commits");
            Assert.IsTrue(agent.GetComponent<WorldInteractable>().IsConfigured);
        }

        [Test]
        public void ARoutineAndADialogueCoexistOnOneNpc()
        {
            var content = new Content();
            var point = Npc("walker", Dialogue("dlg-walker"));
            point.Routine = new NpcRoutineSpec { Kind = NpcRoutineKind.Wander };
            content.Points.Add(point);

            Assert.AreEqual(1, NpcRoutines.AttachAll(content, spawner));
            Assert.AreEqual(1, NpcDialogues.AttachAll(content, spawner, session, Options()));
            Assert.IsNotNull(AgentOf("walker").GetComponent<NpcRoutineRunner>());
            Assert.IsNotNull(AgentOf("walker").GetComponent<TalkInteractionAdapter>());
        }

        // ---- WorldInteractable: configurable after Awake

        [Test]
        public void AnInteractableConfiguredAfterItsAdapterWasAddedFindsTheAdapter()
        {
            var go = new GameObject("npc");
            toDestroy.Add(go);
            var interactable = go.AddComponent<WorldInteractable>();   // Awake runs now: no adapter, no anchor yet
            Assert.IsFalse(interactable.IsConfigured);

            go.AddComponent<OtherAdapter>();
            interactable.Configure(go.transform, 1.5f);

            Assert.IsTrue(interactable.IsConfigured, "Configure looks the adapter up again");
        }

        [Test]
        public void ConfigureForCharacter_SetsBothFlags_AndAPlainConfigureLeavesThemOff()
        {
            var go = new GameObject("thing");
            toDestroy.Add(go);
            var interactable = go.AddComponent<WorldInteractable>();
            interactable.Configure(go.transform, 1.5f);
            Assert.IsFalse(interactable.TakesClickOverSelection);
            Assert.IsFalse(interactable.CompleteApproachInRange);

            interactable.ConfigureForCharacter(go.transform, 1.5f);
            Assert.IsTrue(interactable.TakesClickOverSelection);
            Assert.IsTrue(interactable.CompleteApproachInRange);
        }

        // ---- the spawn point itself

        [Test]
        public void ANewSpawnPointHasNoDialogue_SoOldScenesAreUnchanged()
        {
            Assert.IsNull(new NpcSpawnPoint().Dialogue);
        }
    }
}
