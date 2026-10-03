using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Actors;
using CRPG.Core;
using CRPG.InputSystem;
using CRPG.Quests;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.1 (2D-Campaign-Phase6-CampaignContent.md, task breakdown item 6): the palisade-breach
    /// tutorial's full tactical combat stack, end to end, against the REAL Core scene -- the single riskiest new
    /// mechanism in this task (the toolkit's own combat stack, proven only against 3D NavMesh movement before
    /// now, wired here for the first time against BarrowsEnd's 2D grid). Drives the fight through the REAL
    /// ability-bar UI click and the REAL AbilityTargetingGate click (the same click router every other test in
    /// this project already exercises), not a mocked/direct scheduler call. Renders into a real-sized
    /// RenderTexture (ShellCaptureTests.cs's own established precedent): the batch game view is only 640x480, so
    /// a HUD region's fixed panel-unit width eats a disproportionately huge fraction of it, leaving almost no
    /// screen-safe click position at that resolution (found live while writing this test).</summary>
    public class TacticalCombatSessionTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private static readonly DefinitionId HitPoints = new("resource-hp");
        private static readonly DefinitionId HostileFaction = new("faction-hostile");

        private ShellBootstrap shell;
        private string savePath;
        private RenderTexture rt;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-combat-test-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty; // skip the title screen; real 1-member boot (no MembersOverride)
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");

            rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            yield return null;
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (rt != null) { rt.Release(); rt = null; }
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
        }

        // Mirrors TownProofFixturesTests.cs's/RecruitCompanionTests.cs's own Click helper (the real click stack).
        private void Click(Vector3 world)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<InputSystemCommandSource>();
            var router = typeof(InputSystemCommandSource).GetProperty("Router", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
            var routerType = router.GetType();
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(world);
            var ray = cam.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            routerType.GetMethod("HandlePointerDown").Invoke(router, new object[] { screen, ray });
            routerType.GetMethod("HandlePointerUp").Invoke(router, new object[] { screen, ray, cam, false });
        }

        private static bool IsShown(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e.style.display == DisplayStyle.None) return false;
            return true;
        }

        private VisualElement HudRootElement() =>
            UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement;

        [UnityTest]
        public IEnumerator AttackingAWeakenedRatThroughTheRealAbilityBarAndTargetingClickWinsAndEndsCombat()
        {
            var session = shell.Services.Session;
            var protagonistId = session.Party.Members[0];
            var protagonistAgent = GateSessionServices.FindAgent(protagonistId);
            Assert.IsNotNull(protagonistAgent);

            Assert.IsTrue(session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId("npc-giant-rat"), out var ratDefinition));
            var ratSpot = protagonistAgent.transform.position + new Vector3(1f, 0f, 0f); // within the 1.5-range ability
            var ratAgent = shell.Services.Spawner.SpawnNpc("combat-test-rat", ratDefinition, ratSpot, Quaternion.identity);

            // Weakened to 1 HP so the protagonist's first (always-hits, per this task's own damage-only ability
            // design) Strike is guaranteed to kill it deterministically, without depending on real dice rolls.
            Assert.IsTrue(session.Characters.TryGetCharacter(ratAgent.EntityId, out var ratCharacter));
            Assert.IsTrue(ratCharacter.TryGetResource(HitPoints, out var ratHp));
            ratHp.Current = 1;

            var brain = GiantRatBrain.BuildAndRegister(session.RulesDatabase);
            var combatants = new List<(RuntimeEntityId, DefinitionId)> { (protagonistId, TacticalCombatSession.PartyFaction), (ratAgent.EntityId, HostileFaction) };
            bool? partyWon = null;
            shell.CombatSession.StartEncounter(combatants, HostileFaction, brain, won => partyWon = won);

            Assert.IsTrue(shell.CombatSession.IsActive);
            yield return null;

            // While combat is active a ground click moves ONLY the current combatant (through the tactical move
            // adapter, spending Movement) -- never the exploration controller's whole-party move.
            var startPosition = protagonistAgent.transform.position;
            Click(startPosition + new Vector3(-2f, 0f, 0f));
            yield return WaitUntil(() => Vector3.Distance(startPosition, protagonistAgent.transform.position) > 1.9f, 5f, "the current combatant walked away via the tactical adapter");
            yield return Seconds(0.3f); // let the scheduler finish the move before issuing the next one
            Assert.IsTrue(session.Characters.TryGetCharacter(protagonistId, out var protagonist));
            Assert.IsTrue(protagonist.TryGetResource(new DefinitionId(TacticalCombatSession.MovementResourceId), out var movementLeft));
            Assert.Less(movementLeft.Current, TacticalCombatSession.MovementPerTurn, "the move spent Movement");

            Click(startPosition); // walk back into Strike range of the rat
            yield return WaitUntil(() => Vector3.Distance(startPosition, protagonistAgent.transform.position) < 0.3f, 5f, "the combatant walked back");
            yield return Seconds(0.5f);

            var strikeButton = HudRootElement().Query<Button>().ToList().FirstOrDefault(b => b.text == "Strike");
            Assert.IsNotNull(strikeButton, "the real AbilityBarView shows the protagonist's real Strike ability");
            UiClick.Pointer(strikeButton);
            yield return null;

            Click(ratAgent.transform.position); // the real AbilityTargetingGate click, through the same click router every other click test in this project uses
            yield return Seconds(2f);

            Assert.IsFalse(shell.CombatSession.IsActive, "combat ended after the only hostile died");
            var endTurnLeft = HudRootElement().Query<Button>().ToList().FirstOrDefault(b => b.text == "End Turn");
            Assert.IsTrue(endTurnLeft == null || !IsShown(endTurnLeft), "the combat ability bar (End Turn) is gone once the fight is over");
            var roundLabel = HudRootElement().Query<Label>().ToList().FirstOrDefault(l => l.text == "No combat in progress");
            Assert.IsTrue(roundLabel == null || !IsShown(roundLabel), "the turn-order tracker is hidden once the fight is over");
            Assert.IsTrue(partyWon.HasValue && partyWon.Value, "the party won");

            // Exploration movement is restored once combat ends.
            var beforeRestoredClick = protagonistAgent.transform.position;
            Click(protagonistAgent.transform.position + new Vector3(-3f, 0f, 0f));
            yield return Seconds(1.5f);
            Assert.Greater(Vector3.Distance(beforeRestoredClick, protagonistAgent.transform.position), 0.5f, "exploration movement works again after combat");
        }

        private IEnumerator StartFightWithRatAt(Vector3 offset, System.Action<RuntimeEntityId, RuntimeEntityId> ready)
        {
            var session = shell.Services.Session;
            var protagonistId = session.Party.Members[0];
            var protagonistAgent = GateSessionServices.FindAgent(protagonistId);
            Assert.IsTrue(session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId("npc-giant-rat"), out var ratDefinition));
            var ratAgent = shell.Services.Spawner.SpawnNpc("combat-test-rat-ai", ratDefinition, protagonistAgent.transform.position + offset, Quaternion.identity);

            var brain = GiantRatBrain.BuildAndRegister(session.RulesDatabase);
            var combatants = new List<(RuntimeEntityId, DefinitionId)> { (protagonistId, TacticalCombatSession.PartyFaction), (ratAgent.EntityId, HostileFaction) };
            shell.CombatSession.StartEncounter(combatants, HostileFaction, brain);
            Assert.IsTrue(shell.CombatSession.IsActive);
            yield return null;
            ready(protagonistId, ratAgent.EntityId);
        }

        private float HitPointsOf(RuntimeEntityId id)
        {
            Assert.IsTrue(shell.Services.Session.Characters.TryGetCharacter(id, out var character));
            Assert.IsTrue(character.TryGetResource(HitPoints, out var hp));
            return hp.Current;
        }

        [UnityTest]
        public IEnumerator AHostileAlreadyInMeleeRangeBitesInsteadOfShufflingItsMovement([ValueSource(nameof(RatOffsets))] float distance)
        {
            RuntimeEntityId protagonist = default, rat = default;
            yield return StartFightWithRatAt(new Vector3(distance, 0f, 0f), (p, r) => { protagonist = p; rat = r; });
            var before = HitPointsOf(protagonist);

            var endTurn = HudRootElement().Query<Button>().ToList().FirstOrDefault(b => b.text == "End Turn");
            Assert.IsNotNull(endTurn, "the ability bar offers End Turn on the party's turn");
            UiClick.Pointer(endTurn);

            yield return WaitUntil(() => HitPointsOf(protagonist) < before, 10f, $"the rat {distance} units away closed in and bit the protagonist");
        }

        private static readonly float[] RatOffsets = { 1f, 4f };

        [UnityTest]
        public IEnumerator SavingIsRefusedWhileCombatIsActive()
        {
            yield return StartFightWithRatAt(new Vector3(1f, 0f, 0f), (_, __) => { });

            Assert.IsFalse(shell.SaveNow(), "saving mid-fight would persist a fired encounter with no live encounter behind it");
            Assert.IsFalse(File.Exists(savePath));
        }

        [UnityTest]
        public IEnumerator AWholePartyThatIsDownedLosesTheEncounterInsteadOfStallingForever()
        {
            var session = shell.Services.Session;
            var protagonistId = session.Party.Members[0];
            Assert.IsTrue(session.Characters.TryGetCharacter(protagonistId, out var protagonist));
            protagonist.DeathSaves = new DeathSaveState();

            bool? partyWon = null;
            var protagonistAgent = GateSessionServices.FindAgent(protagonistId);
            Assert.IsTrue(session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId("npc-giant-rat"), out var ratDefinition));
            var ratAgent = shell.Services.Spawner.SpawnNpc("combat-test-rat-loss", ratDefinition, protagonistAgent.transform.position + new Vector3(1f, 0f, 0f), Quaternion.identity);
            var combatants = new List<(RuntimeEntityId, DefinitionId)> { (protagonistId, TacticalCombatSession.PartyFaction), (ratAgent.EntityId, HostileFaction) };
            Assert.IsTrue(shell.CombatSession.StartEncounter(combatants, HostileFaction, GiantRatBrain.BuildAndRegister(session.RulesDatabase), won => partyWon = won));

            yield return WaitUntil(() => partyWon.HasValue, 5f, "the encounter ended");
            Assert.IsFalse(partyWon.Value, "no conscious party member is left, so the party lost");
        }

        [UnityTest]
        public IEnumerator ADownedMemberTakesAutomaticDeathSavesAndIsStabilisedWhenTheFightIsWon()
        {
            shell.RecruitCompanion("garrick");
            yield return null;
            var session = shell.Services.Session;
            var protagonistId = session.Party.Members[0];
            var garrickId = CompanionRoster.IdFor("garrick");
            Assert.IsTrue(session.Characters.TryGetCharacter(protagonistId, out var protagonist));
            protagonist.DeathSaves = new DeathSaveState();
            Assert.IsTrue(protagonist.TryGetResource(HitPoints, out var protagonistHp));
            protagonistHp.Current = 0;

            var garrickAgent = GateSessionServices.FindAgent(garrickId);
            Assert.IsTrue(session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId("npc-giant-rat"), out var ratDefinition));
            var ratAgent = shell.Services.Spawner.SpawnNpc("combat-test-rat-stabilise", ratDefinition, garrickAgent.transform.position + new Vector3(1f, 0f, 0f), Quaternion.identity);
            Assert.IsTrue(session.Characters.TryGetCharacter(ratAgent.EntityId, out var ratCharacter));
            Assert.IsTrue(ratCharacter.TryGetResource(HitPoints, out var ratHp));
            ratHp.Current = 1;

            bool? partyWon = null;
            var combatants = new List<(RuntimeEntityId, DefinitionId)>
            {
                (protagonistId, TacticalCombatSession.PartyFaction), (garrickId, TacticalCombatSession.PartyFaction), (ratAgent.EntityId, HostileFaction),
            };
            Assert.IsTrue(shell.CombatSession.StartEncounter(combatants, HostileFaction, GiantRatBrain.BuildAndRegister(session.RulesDatabase), won => partyWon = won));

            // The downed protagonist's turn passes by itself; it becomes Garrick's turn, who strikes the rat.
            var strikeButton = (Button)null;
            yield return WaitUntil(() => (strikeButton = HudRootElement().Query<Button>().ToList().FirstOrDefault(b => b.text == "Strike")) != null, 5f, "Garrick's ability bar");
            UiClick.Pointer(strikeButton);
            yield return null;
            Click(ratAgent.transform.position);

            yield return WaitUntil(() => partyWon.HasValue, 5f, "the encounter ended");
            Assert.IsTrue(partyWon.Value);
            Assert.IsNull(protagonist.DeathSaves, "the downed protagonist is stabilised once the fight is won");
            Assert.AreEqual(1, protagonistHp.Current);
        }

        [Test]
        public void RefreshingAllActiveQuestsCompletesAQuestWhoseObjectiveWasMetBeforeAnyoneAskedForTheCheck()
        {
            var session = shell.Services.Session;
            Assert.IsTrue(session.RulesDatabase.TryResolve<QuestDefinition>(new DefinitionId("quest-gathering-storm"), out var quest));
            session.Timeline.Record(EncounterTrigger.PalisadeWonKey, TimelineValue.Flag); // rats died before the briefing
            new QuestService(session.Quests, session.Timeline, session.EventBus, session.Inventory).StartQuest(quest);
            Assert.IsTrue(session.Quests.TryGetQuest(quest.Id, out var state));
            Assert.AreEqual(QuestStatus.Active, state.Status, "starting a quest records nothing and re-checks nothing by itself");

            shell.RefreshAllActiveQuests();

            Assert.AreEqual(QuestStatus.Completed, state.Status);
        }

        [UnityTest]
        public IEnumerator ASelfTargetedAbilityLikeRageCastsAtOnceInsteadOfWaitingForATargetClick()
        {
            var session = shell.Services.Session;
            var protagonistId = session.Party.Members[0];
            Assert.IsTrue(session.Characters.TryGetCharacter(protagonistId, out var protagonist));
            protagonist.LearnAbility(new DefinitionId("ability-rage"));
            protagonist.AddResource(new ResourceState(new DefinitionId("resource-rage-uses"), 2));

            yield return StartFightWithRatAt(new Vector3(1f, 0f, 0f), (_, __) => { });

            Button rage = null;
            yield return WaitUntil(() => (rage = HudRootElement().Query<Button>().ToList().FirstOrDefault(b => b.text == "Rage")) != null && rage.enabledSelf, 5f, "an enabled Rage button");
            UiClick.Pointer(rage);

            yield return WaitUntil(() => protagonist.HasCondition(new DefinitionId("condition-raging")), 5f, "Rage took effect on a single button click");
        }

        [UnityTest]
        public IEnumerator AnOutOfRangeStrikeTellsThePlayerWhyNothingHappened()
        {
            RuntimeEntityId ratId = default;
            yield return StartFightWithRatAt(new Vector3(4f, 0f, 0f), (_, r) => ratId = r);
            Button strike = null;
            yield return WaitUntil(() => (strike = HudRootElement().Query<Button>().ToList().FirstOrDefault(b => b.text == "Strike")) != null, 5f, "Strike button");
            UiClick.Pointer(strike);
            yield return null;
            var rat = GateSessionServices.FindAgent(ratId);
            Click(rat.transform.position);
            yield return null;

            var feedback = HudRootElement().Query<Label>().ToList().FirstOrDefault(l => l.text != null && l.text.StartsWith("Strike:"));
            Assert.IsNotNull(feedback, "a rejected click shows a prominent message instead of silently doing nothing");
        }
    }
}
