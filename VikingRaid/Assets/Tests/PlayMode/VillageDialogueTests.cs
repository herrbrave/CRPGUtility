using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VikingRaid.Village;

namespace VikingRaid.Tests
{
    /// <summary>Talking to the villagers on the real village (design <c>Docs/Plans/Toolkit-NpcDialogue.md</c>): click an NPC, the party walks up, the conversation opens, the NPC holds still and faces the speaker, finishing releases everything.
    /// Time budgets are accumulated simulated seconds. <see cref="CaptureTheConversation"/> writes <c>VikingRaid_Logs/spikes/dialogue_*.png</c> so a human can judge how the panel looks.</summary>
    public class VillageDialogueTests : VillageTestBase
    {
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));

        private static void Drive(DialogueView view, string method, params object[] args) =>
            typeof(DialogueView).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, args);

        private static DialogueView CurrentView() => Object.FindFirstObjectByType<DialogueView>();

        private Vector3 BodyPoint(string spawnId) => Village.Npcs[spawnId].transform.position + Vector3.up * 1f;

        // True when a click on the NPC's body would hit THAT NPC: nothing (a rise, a wall) between the camera and it. The base fixture's check compares flat distances, which a hill in front of a person standing in a gully passes.
        private bool ClickHits(string spawnId)
        {
            var point = BodyPoint(spawnId);
            if (!OnScreen(point)) return false;
            var ray = Cam.ScreenPointToRay(Cam.WorldToScreenPoint(point));
            return Physics.Raycast(ray, out var hit, 1000f, VikingRaid.Game.WorldLayers.Hittable, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<CharacterAgent>() == Village.Npcs[spawnId];
        }

        /// <summary>Brings the party next to an NPC (a warp: walking to the dock would take a minute), holds the NPC still, and makes sure it can actually be clicked: if the camera cannot see it from where it stands (the village is
        /// hilly) it is brought out into the open beside the party. The interaction suspends it from the moment the click is accepted, so holding it here leaves no gap.</summary>
        private IEnumerator BringPartyNear(string spawnId)
        {
            var npcAgent = Village.Npcs[spawnId];
            var hold = new object();
            Village.Session.Ambient.Suspend(npcAgent.EntityId, hold);

            var npc = npcAgent.transform.position;
            var from = PartyCentre();
            var direction = Flat(from - npc).sqrMagnitude > 0.01f ? Flat(from - npc).normalized : Vector3.back;
            var i = 0;
            foreach (var id in Village.Party)
            {
                var wanted = npc + direction * (4.5f + 0.7f * i++);
                var agent = VillageBootstrap.FindAgent(id);
                agent.Motor.Warp(UnityEngine.AI.NavMesh.SamplePosition(wanted, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas) ? hit.position : wanted);
            }

            var elapsed = 0f;
            do { elapsed += Time.deltaTime; yield return null; } while (elapsed < 4f && !ClickHits(spawnId));

            // still hidden: bring her out beside the party, trying ground around the party's centre until the camera sees her
            for (var attempt = 0; attempt < 24 && !ClickHits(spawnId); attempt++)
            {
                var around = PartyCentre() + Quaternion.AngleAxis(attempt * 30f, Vector3.up) * Vector3.forward * (3f + attempt / 12 * 2f);
                if (UnityEngine.AI.NavMesh.SamplePosition(around, out var open, 2f, UnityEngine.AI.NavMesh.AllAreas)) npcAgent.Motor.Warp(open.position);
                yield return null;
                Physics.SyncTransforms();
                yield return null;
            }

            Village.Session.Ambient.Resume(npcAgent.EntityId, hold);
            Assert.IsTrue(ClickHits(spawnId), $"a click on {spawnId} would not land on her even after bringing her into the open");
        }

        [UnityTest]
        public IEnumerator EveryNpcWithADialogueIsTalkable()
        {
            var withDialogue = Village.Location.SpawnPoints.Where(p => p.Dialogue != null).ToList();
            Assert.AreEqual(4, withDialogue.Count, "Orm, Hild, the elder and Ragna have dialogue");
            foreach (var point in withDialogue)
            {
                var npc = Village.Npcs[point.SpawnId];
                Assert.IsNotNull(npc.GetComponent<TalkInteractionAdapter>(), $"{point.SpawnId} has no talk adapter");
                Assert.IsTrue(npc.GetComponent<WorldInteractable>().IsConfigured, $"{point.SpawnId}'s interactable is not configured");
                Assert.IsNotNull(npc.GetComponent<Collider>(), $"{point.SpawnId} has nothing to click");
            }

            yield return null;
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ClickingAVillager_WalksThePartyUp_OpensTheConversation_AndFinishingReleasesEverything()
        {
            Time.timeScale = 3f;
            var hild = Village.Npcs["village-hild"];
            yield return BringPartyNear("village-hild");

            Click(BodyPoint("village-hild"));

            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            var view = CurrentView();
            Assert.IsNotNull(view, "the conversation never opened (did the party reach her?)");
            Assert.IsTrue(Village.Session.Ambient.IsSuspended(hild.EntityId), "Hild held still for the conversation");
            Assert.IsFalse(hild.GetComponent<TalkInteractionAdapter>().IsAvailable(Village.Party[0]), "one conversation at a time");

            // she turns to the speaker
            elapsed = 0f;
            var speaker = Village.Session.Party.ActiveCharacter;
            var toSpeaker = Flat(Position(speaker) - hild.transform.position).normalized;
            while (elapsed < 3f && Vector3.Dot(hild.transform.forward, toSpeaker) < 0.9f) { elapsed += Time.deltaTime; yield return null; }
            Assert.Greater(Vector3.Dot(hild.transform.forward, toSpeaker), 0.9f, "Hild did not turn to the party member talking to her");

            // greet -> menu -> "Anything happening?" -> news -> "Anything else?" -> menu -> farewell
            Drive(view, "OnContinueClicked");
            Drive(view, "OnChoiceClicked", 2);
            Drive(view, "OnContinueClicked");
            Drive(view, "OnContinueClicked");
            Drive(view, "OnChoiceClicked", 4);

            yield return null;
            yield return null;
            Assert.IsNull(CurrentView(), "the conversation panel is gone");
            Assert.IsFalse(Village.Session.Ambient.IsSuspended(hild.EntityId), "Hild is released");
            Assert.IsFalse(new UIToolkitPointerCapture().IsPointerOverUI(Vector2.zero), "the modal pointer lease was released: the world is clickable again");
            Assert.IsTrue(hild.GetComponent<TalkInteractionAdapter>().IsAvailable(Village.Party[0]), "she can be talked to again");

            // and she goes back to wandering (after her short pause)
            var here = hild.transform.position;
            elapsed = 0f;
            while (elapsed < 40f && Vector3.Distance(here, hild.transform.position) < 1f) { elapsed += Time.deltaTime; yield return null; }
            Assert.Greater(Vector3.Distance(here, hild.transform.position), 1f, "Hild never went back to her routine");
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator TheAlehouseShop_SellsToThePartyForItsGold_AndTheConversationCarriesOnAfterwards()
        {
            Time.timeScale = 3f;
            var hild = Village.Npcs["village-hild"];
            var inventory = Village.Session.Inventory;
            var dagger = new CRPG.Core.DefinitionId("item-dagger");
            Assert.AreEqual(VillageBootstrap.StartingGold, inventory.CountShared(VillageBootstrap.GoldItemId), "the party starts with gold");
            yield return BringPartyNear("village-hild");

            Click(BodyPoint("village-hild"));
            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            var view = CurrentView();
            Assert.IsNotNull(view, "the conversation never opened");

            Drive(view, "OnContinueClicked");        // greeting -> the menu
            Drive(view, "OnChoiceClicked", 0);       // "Let me see your wares."
            yield return null;
            var store = Object.FindFirstObjectByType<StoreView>();
            Assert.IsNotNull(store, "the shop screen did not open");
            Assert.IsNull(CurrentView(), "the shop replaces the dialogue panel while it is open");

            typeof(StoreView).GetMethod("OnBuyClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(store, new object[] { dagger });
            Assert.AreEqual(1, inventory.CountShared(dagger), "bought a dagger");
            Assert.AreEqual(VillageBootstrap.StartingGold - 2, inventory.CountShared(VillageBootstrap.GoldItemId), "for its price in gold");
            Village.Session.Stores["village-hild"].TryGetListing(dagger, out var listing);
            Assert.AreEqual(1, listing.Quantity, "the shop had two");

            store.Leave();
            yield return null;
            view = CurrentView();
            Assert.IsNotNull(view, "leaving the shop resumes the conversation");
            Drive(view, "OnContinueClicked");        // "Anything else, then?" -> the menu
            Drive(view, "OnChoiceClicked", 4);       // "Farewell."
            yield return null;
            yield return null;
            Assert.IsNull(CurrentView());
            Assert.IsNull(Object.FindFirstObjectByType<StoreView>());
            Assert.IsFalse(new UIToolkitPointerCapture().IsPointerOverUI(Vector2.zero), "no modal lease left behind");
            AssertNoErrorsLogged();
        }

        private IEnumerator OpenHildsMenu()
        {
            yield return BringPartyNear("village-hild");
            Click(BodyPoint("village-hild"));
            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            Assert.IsNotNull(CurrentView(), "the conversation never opened");
            Drive(CurrentView(), "OnContinueClicked");   // greeting -> the menu
        }

        [UnityTest]
        public IEnumerator TheAlehouseInn_TakesTheFiveGold_AndRests_WhenThePartyCanPay()
        {
            Time.timeScale = 3f;
            var inventory = Village.Session.Inventory;
            var rested = 0;
            Village.Session.EventBus.Subscribe(CRPG.Core.GameEvent.LongRest, _ => rested++);
            yield return OpenHildsMenu();

            Drive(CurrentView(), "OnChoiceClicked", 1);   // "I need a bed"
            yield return null;

            Assert.AreEqual(VillageBootstrap.StartingGold - 5, inventory.CountShared(VillageBootstrap.GoldItemId), "five gold for the bed");
            Assert.AreEqual(1, rested, "and the party rested: the LongRest event is what a game hangs healing on");
            Assert.IsNotNull(CurrentView(), "Hild answers");
            Router.HandleCancel();
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator TheAlehouseInn_TurnsAwayAPartyThatCannotPay_WithoutRestingOrTakingCoin()
        {
            Time.timeScale = 3f;
            var inventory = Village.Session.Inventory;
            inventory.TryRemoveShared(VillageBootstrap.GoldItemId, VillageBootstrap.StartingGold - 4);   // four gold left
            var rested = 0;
            Village.Session.EventBus.Subscribe(CRPG.Core.GameEvent.LongRest, _ => rested++);
            yield return OpenHildsMenu();

            Drive(CurrentView(), "OnChoiceClicked", 1);
            yield return null;

            Assert.AreEqual(4, inventory.CountShared(VillageBootstrap.GoldItemId), "nothing taken");
            Assert.AreEqual(0, rested);
            Router.HandleCancel();
            AssertNoErrorsLogged();
        }

        // Talk to a villager and run a scripted conversation: every step is a (method, argument) the test drives, with the view looked up fresh each time (the shop and the next line replace it).
        private IEnumerator Converse(string spawnId, params (string method, int arg)[] steps)
        {
            yield return BringPartyNear(spawnId);
            Click(BodyPoint(spawnId));
            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            Assert.IsNotNull(CurrentView(), $"the conversation with {spawnId} never opened");

            foreach (var (method, arg) in steps)
            {
                var view = CurrentView();
                Assert.IsNotNull(view, $"no conversation panel at step {method}({arg}) with {spawnId}");
                if (method == "OnChoiceClicked") Drive(view, method, arg); else Drive(view, method);
            }

            yield return null;
            yield return null;
            Assert.IsNull(CurrentView(), $"the conversation with {spawnId} did not end");
        }

        private CRPG.Quests.QuestState Sacks()
        {
            Village.Session.Quests.TryGetQuest(new CRPG.Core.DefinitionId("quest-village-sacks"), out var q);
            return q;
        }

        [UnityTest]
        public IEnumerator TheSacksErrand_IsGivenByOrm_AdvancedAtHilds_AndPaidOutBackAtOrms()
        {
            Time.timeScale = 3f;
            var session = Village.Session;
            Assert.IsNull(Sacks(), "no quest yet");

            // Orm: greeting -> "Do you need anything?" -> the errand -> "I will tell her." -> "Off you go."
            yield return Converse("village-orm", ("OnContinueClicked", 0), ("OnChoiceClicked", 1), ("OnContinueClicked", 0), ("OnChoiceClicked", 0), ("OnContinueClicked", 0));
            Assert.IsNotNull(Sacks(), "the dialogue started the quest through the tracker");
            Assert.AreEqual(CRPG.Quests.QuestStatus.Active, Sacks().Status);
            Assert.IsTrue(session.Timeline.HasOccurred("orm-sacks-accepted"));
            Assert.IsTrue(session.Timeline.HasOccurred(CRPG.Gameplay.QuestTracker.StartedKey("quest-village-sacks")));
            StringAssert.Contains("Quest started", Village.Hud.ToastText);

            // Hild: only now does the extra option appear. greeting -> menu -> "Orm sent me" -> her answer -> "Anything else?" -> farewell
            yield return Converse("village-hild", ("OnContinueClicked", 0), ("OnChoiceClicked", 3), ("OnContinueClicked", 0), ("OnContinueClicked", 0), ("OnChoiceClicked", 4));
            Assert.IsTrue(session.Timeline.HasOccurred("hild-told-fish-arrived"));
            Assert.IsTrue(Sacks().Objectives["tell-hild"].IsComplete, "the first objective advanced when the conversation ended");
            Assert.AreEqual(CRPG.Quests.QuestStatus.Active, Sacks().Status);

            // Orm again: he now takes the report and pays
            var goldBefore = session.Inventory.CountShared(VillageBootstrap.GoldItemId);
            yield return Converse("village-orm", ("OnContinueClicked", 0), ("OnContinueClicked", 0));
            Assert.AreEqual(CRPG.Quests.QuestStatus.Completed, Sacks().Status);
            Assert.AreEqual(goldBefore + 15, session.Inventory.CountShared(VillageBootstrap.GoldItemId), "the quest's reward");
            Assert.IsTrue(session.Timeline.HasOccurred(CRPG.Gameplay.QuestTracker.CompletedKey("quest-village-sacks")));
            StringAssert.Contains("95", Village.Hud.GoldText.Replace("Gold: ", ""));
            StringAssert.Contains("Quest complete", Village.Hud.ToastText);

            // and the quest log shows it
            Village.Hud.ToggleQuestLog();
            Assert.IsTrue(Village.Hud.QuestLogVisible);
            yield return SnapPanel(Village.Hud.GetComponent<UnityEngine.UIElements.UIDocument>(), "questlog");
            Village.Hud.ToggleQuestLog();

            // Orm remembers
            var again = BodyPoint("village-orm");
            Click(again);
            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            Assert.IsNotNull(CurrentView());
            StringAssert.Contains("Good folk", AllLabelText(CurrentView()), "Orm remembers the errand");
            Router.HandleCancel();
            AssertNoErrorsLogged();
        }

        private static string AllLabelText(DialogueView view) =>
            string.Join(" ", view.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement.Query<UnityEngine.UIElements.Label>().ToList().Select(l => l.text));

        [UnityTest]
        public IEnumerator Ragna_JoinsThePartyWhenTheConversationEnds_NotWhenSheIsAskedToJoin_AndThenFollowsTheirOrders()
        {
            Time.timeScale = 3f;
            var session = Village.Session;
            var ragna = Village.Npcs["village-ragna"];
            Assert.AreEqual(4, session.Party.Members.Count);
            yield return BringPartyNear("village-ragna");
            Click(BodyPoint("village-ragna"));
            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            Assert.IsNotNull(CurrentView(), "the conversation never opened");

            Drive(CurrentView(), "OnContinueClicked");        // her greeting -> the question
            Drive(CurrentView(), "OnChoiceClicked", 0);       // "Join us."
            yield return null;
            Assert.IsNotNull(CurrentView(), "her closing line is still shown: the recruit did not tear the conversation down");
            Assert.IsFalse(session.Party.IsMember(ragna.EntityId), "not a member until the conversation ends");
            Assert.IsTrue(Village.Recruitment.IsPending("village-ragna"));

            Drive(CurrentView(), "OnContinueClicked");        // "Lead on." -> end
            yield return null;
            yield return null;

            Assert.IsTrue(session.Party.IsMember(ragna.EntityId), "she joined");
            Assert.AreEqual(5, session.Party.Members.Count);
            Assert.AreEqual(5, Village.Party.Count, "and the village counts her among its party");
            Assert.IsNull(ragna.GetComponent<TalkInteractionAdapter>(), "she is no longer a townsperson to talk to");
            Assert.IsNull(ragna.GetComponent<CRPG.Gameplay.Ambient.NpcRoutineRunner>(), "or one who wanders");
            StringAssert.Contains("Ragna joins", Village.Hud.ToastText);
            Assert.IsFalse(Village.Session.Ambient.IsSuspended(ragna.EntityId));

            // the party's orders move her too: send everyone well away and see whether she goes
            var destination = FarVisibleReachablePoint(ragna.transform.position);
            var before = Vector3.Distance(Flat(ragna.transform.position), Flat(destination));
            Click(destination);
            elapsed = 0f;
            while (elapsed < 30f && Vector3.Distance(Flat(ragna.transform.position), Flat(destination)) > 5f) { elapsed += Time.deltaTime; yield return null; }
            var after = Vector3.Distance(Flat(ragna.transform.position), Flat(destination));
            Assert.Less(after, before - 4f, $"Ragna did not follow the party (was {before:F1} m from the destination, is {after:F1} m)");
            AssertNoErrorsLogged();
        }

        // A walkable, visible, clickable point at least 14 m from the given position (a fixed 8-14 m from the party centre can be nearer than the party spacing allows a follower to prove anything).
        private Vector3 FarVisibleReachablePoint(Vector3 from)
        {
            var path = new UnityEngine.AI.NavMeshPath();
            for (var distance = 14f; distance <= 24f; distance += 2f)
                for (var angle = 0; angle < 360; angle += 20)
                {
                    var wanted = from + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * distance;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(wanted, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas) || !OnScreen(hit.position) || !ClickWouldLandOn(hit.position)) continue;
                    if (UnityEngine.AI.NavMesh.CalculatePath(from, hit.position, UnityEngine.AI.NavMesh.AllAreas, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) return hit.position;
                }

            Assert.Fail("no far, visible, reachable point to send the party to");
            return default;
        }

        [UnityTest]
        public IEnumerator HildDoesNotOfferTheOrmOption_UntilTheQuestIsAccepted()
        {
            Time.timeScale = 3f;
            yield return BringPartyNear("village-hild");
            Click(BodyPoint("village-hild"));
            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            Drive(CurrentView(), "OnContinueClicked");

            StringAssert.DoesNotContain("Orm sent me", AllLabelText(CurrentView()), "the option is hidden before the errand is accepted");
            Router.HandleCancel();
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ClickingAVillagerOutOfReachDoesNotLeaveThePartyOrTheVillagerFrozen_WhenTheOrderIsCancelled()
        {
            Time.timeScale = 3f;
            var hild = Village.Npcs["village-hild"];
            yield return BringPartyNear("village-hild");

            Click(BodyPoint("village-hild"));
            yield return null;
            Assert.IsTrue(Village.Session.Ambient.IsSuspended(hild.EntityId), "held from the moment the order was accepted");

            Router.HandleCancel();   // Escape
            yield return null;
            Assert.IsFalse(Village.Session.Ambient.IsSuspended(hild.EntityId), "a cancelled order releases her");
            Assert.IsNull(CurrentView());
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ClickingAPartyMemberStillSelectsIt()
        {
            var member = Village.Party[0];
            var point = Position(member) + Vector3.up * 1f;
            Assert.IsTrue(OnScreen(point), "the first hero is on screen at the start");

            Click(point);
            yield return null;

            Assert.IsNull(CurrentView(), "a hero is not a conversation");
            AssertNoErrorsLogged();
        }

        // ---------------------------------------------------------------- the visual gate

        private IEnumerator SnapPanel(UnityEngine.UIElements.UIDocument document, string name)
        {
            Time.timeScale = 1f;
            for (var i = 0; i < 6; i++) yield return null;

            var camera = Cam;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            // the panel renders through its PanelSettings: give it the same target as the world camera for a few frames
            var settings = document.panelSettings;
            var previousTarget = settings.targetTexture;
            settings.targetTexture = rt;
            camera.targetTexture = rt;
            for (var i = 0; i < 4; i++) yield return null;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, $"dialogue_{name}.png"), tex.EncodeToPNG());
            camera.targetTexture = null;
            settings.targetTexture = previousTarget;
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        }

        [UnityTest]
        public IEnumerator CaptureTheConversation()
        {
            Time.timeScale = 3f;
            yield return BringPartyNear("village-elder");
            Click(BodyPoint("village-elder"));

            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            var view = CurrentView();
            Assert.IsNotNull(view);
            yield return SnapPanel(view.GetComponent<UnityEngine.UIElements.UIDocument>(), "panel");

            Router.HandleCancel();
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator CaptureTheShop()
        {
            Time.timeScale = 3f;
            yield return BringPartyNear("village-hild");
            Click(BodyPoint("village-hild"));

            var elapsed = 0f;
            while (elapsed < 40f && CurrentView() == null) { elapsed += Time.deltaTime; yield return null; }
            var view = CurrentView();
            Assert.IsNotNull(view);
            Drive(view, "OnContinueClicked");
            Drive(view, "OnChoiceClicked", 0);
            yield return null;
            var store = Object.FindFirstObjectByType<StoreView>();
            Assert.IsNotNull(store);
            yield return SnapPanel(store.GetComponent<UnityEngine.UIElements.UIDocument>(), "shop");

            Router.HandleCancel();
            AssertNoErrorsLogged();
        }
    }
}
