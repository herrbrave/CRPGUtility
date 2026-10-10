using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.PlayMode
{
    /// <summary>The conversation lifecycle of a talkable NPC (design <c>Docs/Plans/Toolkit-NpcDialogue.md</c>): the adapter's events on every ending, a conversation that fails to start leaving nothing locked, and the NPC turning to
    /// its speaker and always letting go again.</summary>
    public class NpcDialoguePlayTests
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

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
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

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }

            throw new System.Exception($"{name} not found on {target.GetType().Name}");
        }

        private static SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

        private static DialogueDefinition LineThenEnd()
        {
            var end = new EndNode();
            SetField(end, "nodeId", Id("end"));
            var line = new LineNode();
            SetField(line, "nodeId", Id("start"));
            SetField(line, "speakerName", "Elder");
            SetField(line, "text", "Welcome, traveller.");
            SetField(line, "nextNodeId", Id("end"));
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-test");
            SetField(dialogue, "startNodeId", Id("start"));
            SetField(dialogue, "nodes", new List<DialogueNode> { line, end });
            return dialogue;
        }

        private static DialogueDefinition BrokenStart()
        {
            var end = new EndNode();
            SetField(end, "nodeId", Id("end"));
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-broken");
            SetField(dialogue, "startNodeId", Id("nowhere"));   // DialogueRunner throws on a start node that does not exist
            SetField(dialogue, "nodes", new List<DialogueNode> { end });
            return dialogue;
        }

        /// <summary>A talkable NPC and a performer standing 3 m along +X; returns the NPC's adapter and the performer's id.</summary>
        private (TalkInteractionAdapter adapter, CharacterAgent npc, RuntimeEntityId performer) TalkableNpc(DialogueDefinition dialogue)
        {
            var definition = ScriptableObject.CreateInstance<CRPG.Actors.NpcDefinition>();
            SetField(definition, "id", "npc-elder");
            SetField(definition, "displayName", "Elder");
            var visual = new GameObject("elder-visual");
            visual.AddComponent<FacingController>();
            toDestroy.Add(visual);
            SetField(definition, "visualPrefab", visual);
            toDestroy.Add(definition);
            toDestroy.Add(dialogue);

            var npc = spawner.SpawnNpc("elder", definition, Vector3.zero, Quaternion.identity);
            toDestroy.Add(npc.gameObject);

            var content = new Content();
            content.Points.Add(new NpcSpawnPoint { SpawnId = "elder", Definition = definition, Anchor = npc.transform, Dialogue = dialogue });
            NpcDialogues.AttachAll(content, spawner, session, new NpcDialogueOptions { PanelSettings = panel });

            var performerGo = new GameObject("Performer") { transform = { position = new Vector3(3f, 0f, 0f) } };
            toDestroy.Add(performerGo);
            var agent = performerGo.AddComponent<CharacterAgent>();
            var performerId = RuntimeEntityId.NewId();
            agent.Bind(performerId, session);
            return (npc.GetComponent<TalkInteractionAdapter>(), npc, performerId);
        }

        [UnityTest]
        public IEnumerator AConversationReportsItsStart_AndItsCancellation_ExactlyOnce()
        {
            var (adapter, _, performer) = TalkableNpc(LineThenEnd());
            var started = new List<RuntimeEntityId>();
            var ended = new List<RuntimeEntityId>();
            adapter.ConversationStarted += started.Add;
            adapter.ConversationEnded += ended.Add;

            var execution = adapter.BeginExecute(performer, _ => { });
            yield return null;
            CollectionAssert.AreEqual(new[] { performer }, started);
            Assert.IsEmpty(ended);
            Assert.IsFalse(adapter.IsAvailable(performer), "one conversation at a time");

            execution.Cancel();
            execution.Cancel();   // a second cancel is harmless
            CollectionAssert.AreEqual(new[] { performer }, ended, "cancellation ends it, once");
            Assert.IsTrue(adapter.IsAvailable(performer), "and the NPC can be talked to again");
        }

        [UnityTest]
        public IEnumerator AConversationThatFailsToStart_LeavesNeitherTheNpcNorTheWorldLocked()
        {
            var (adapter, _, performer) = TalkableNpc(BrokenStart());
            var started = 0;
            var ended = 0;
            adapter.ConversationStarted += _ => started++;
            adapter.ConversationEnded += _ => ended++;

            Assert.Catch(() => adapter.BeginExecute(performer, _ => { }));
            yield return null;

            Assert.AreEqual(1, started);
            Assert.AreEqual(1, ended, "a start that failed still ends");
            Assert.IsTrue(adapter.IsAvailable(performer), "the NPC is not stuck 'in conversation'");
            Assert.IsFalse(new UIToolkitPointerCapture().IsPointerOverUI(Vector2.zero), "no modal pointer lease was leaked: world clicks still work");
        }

        [UnityTest]
        public IEnumerator TheNpcTurnsToItsSpeakerForTheConversation_AndLetsGoWhenItEnds()
        {
            var (adapter, npc, performer) = TalkableNpc(LineThenEnd());
            npc.transform.rotation = Quaternion.LookRotation(Vector3.back);   // facing away from the performer at +X

            var execution = adapter.BeginExecute(performer, _ => { });
            var elapsed = 0f;
            while (elapsed < 2f && Vector3.Dot(npc.transform.forward, Vector3.right) < 0.99f) { elapsed += Time.deltaTime; yield return null; }
            Assert.Greater(Vector3.Dot(npc.transform.forward, Vector3.right), 0.99f, "turned to face the speaker");

            execution.Cancel();
            npc.transform.rotation = Quaternion.LookRotation(Vector3.back);
            for (var i = 0; i < 20; i++) yield return null;
            Assert.Greater(Vector3.Dot(npc.transform.forward, Vector3.back), 0.99f, "the facing claim was released: nothing turns it back");
        }

        [UnityTest]
        public IEnumerator ARecruitQueuedMidConversation_JoinsThePartyWhenTheConversationEnds_NotBefore()
        {
            var (adapter, npc, performer) = TalkableNpc(LineThenEnd());
            var spawner2 = spawner;
            var joined = new List<CharacterAgent>();
            var recruitment = new NpcRecruitment(spawner2, session, joined.Add);
            var execution = adapter.BeginExecute(performer, _ => { });
            yield return null;

            Assert.IsTrue(recruitment.Queue("elder"), "the Recruit action runs while the conversation is open");
            yield return null;
            Assert.IsFalse(session.Party.IsMember(npc.EntityId), "still talking: nothing torn down yet");
            Assert.IsNotNull(npc.GetComponent<TalkInteractionAdapter>());

            execution.Cancel();   // the conversation ends
            Assert.IsTrue(session.Party.IsMember(npc.EntityId), "now");
            CollectionAssert.AreEqual(new[] { npc }, joined);
            yield return null;
            Assert.IsNull(npc.GetComponent<TalkInteractionAdapter>(), "the townsperson parts are gone by the next frame");
            Assert.IsNull(npc.GetComponent<WorldInteractable>());
            recruitment.Dispose();
        }

        [UnityTest]
        public IEnumerator RecruitingARoutineNpc_GivesBackTheAvoidancePriorityTheRoutineLowered()
        {
            var definition = ScriptableObject.CreateInstance<CRPG.Actors.NpcDefinition>();
            SetField(definition, "id", "npc-walker");
            SetField(definition, "displayName", "Walker");
            var visual = new GameObject("walker-visual");
            var nav = visual.AddComponent<UnityEngine.AI.NavMeshAgent>();
            nav.avoidancePriority = 50;
            visual.AddComponent<CRPG.Navigation.NavMeshCharacterMotor>();
            SetField(definition, "visualPrefab", visual);
            toDestroy.Add(visual);
            toDestroy.Add(definition);
            var walker = spawner.SpawnNpc("walker", definition, Vector3.zero, Quaternion.identity);
            toDestroy.Add(walker.gameObject);
            var spec = new CRPG.Gameplay.Ambient.NpcRoutineSpec { Kind = CRPG.Gameplay.Ambient.NpcRoutineKind.Wander };
            CRPG.Gameplay.Ambient.NpcRoutines.Attach(walker, spec, walker.transform, "walker");
            yield return null;
            yield return null;
            var agent = walker.GetComponent<UnityEngine.AI.NavMeshAgent>();
            Assert.AreEqual(CRPG.Gameplay.Ambient.NpcRoutineRunner.RoutineAvoidancePriority, agent.avoidancePriority, "the routine made it less important to the avoidance solver");

            new NpcRecruitment(spawner, session).Recruit("walker");

            Assert.AreEqual(50, agent.avoidancePriority, "a companion is as important as the rest of the party again");
        }

        [UnityTest]
        public IEnumerator TheNpcLetsGoOfItsFacing_EvenWhenTheConversationIsCancelledByTeardown()
        {
            var (adapter, npc, performer) = TalkableNpc(LineThenEnd());
            var execution = adapter.BeginExecute(performer, _ => { });
            yield return null;

            Object.DestroyImmediate(npc.gameObject);   // scene unload: the NPC is simply gone
            yield return null;

            Assert.IsTrue(npc == null);   // and nothing threw while the facing component released itself
            execution.Cancel();           // what InteractionControllerBehaviour.OnDisable does at teardown; also releases the static modal pointer lease so later tests are not affected
        }
    }
}
