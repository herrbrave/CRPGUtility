using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Gate;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.InputSystem;
using CRPG.Navigation;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 2.7b: the authored Phase 2 gate scene, driven through the REAL click stack (the router InputSystemCommandSource builds, fed a
    /// camera ray exactly as OnClick does), the real spawn service, real damage/death saves and a real save/load round trip.
    /// </summary>
    public class GateSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Gate/PhaseTwoGate.unity";
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Helmet = new("layer-helmet");

        private GateBootstrap gate;
        private string savePath;

        [UnitySetUp]
        public IEnumerator LoadGate()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-gate-test-{System.Guid.NewGuid():N}.json");
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            gate = Object.FindFirstObjectByType<GateBootstrap>();
            Assert.IsNotNull(gate, "the gate scene has a GateBootstrap");
            gate.SavePath = savePath;
            yield return null; // Start() builds the gate
            yield return null;
            Assert.IsTrue(gate.IsReady, "the bootstrap built the session");
        }

        [TearDown]
        public void Cleanup()
        {
            if (File.Exists(savePath)) File.Delete(savePath);
            Time.timeScale = 1f;
        }

        private GateSessionServices Services => gate.Services;
        private RuntimeEntityId Member(int index) => Services.Session.Party.Members[index];
        private CharacterAgent Agent(RuntimeEntityId id) => GateSessionServices.FindAgent(id);

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Mathf.Min(Time.deltaTime, 0.1f);
                yield return null;
            }
        }

        /// <summary>Feeds the real router exactly what InputSystemCommandSource.OnClick does for a click on a world point.</summary>
        private void Click(Vector3 world)
        {
            var source = Object.FindFirstObjectByType<InputSystemCommandSource>();
            var routerProperty = typeof(InputSystemCommandSource).GetProperty("Router", BindingFlags.Instance | BindingFlags.NonPublic);
            var router = routerProperty.GetValue(source);
            var routerType = router.GetType();
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(world);
            var ray = cam.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            routerType.GetMethod("HandlePointerDown").Invoke(router, new object[] { screen, ray });
            routerType.GetMethod("HandlePointerUp").Invoke(router, new object[] { screen, ray, cam, false });
        }

        private Vector3 CellCentre(int x, int z) => GateLayout.Origin + new Vector3(x + 0.5f, 0f, z + 0.5f);

        private static SpriteLayerCompositor Compositor(CharacterAgent agent) => agent.GetComponentInChildren<SpriteLayerCompositor>(true);
        private static EffectiveLayer Layer(CharacterAgent agent, DefinitionId slot) => Compositor(agent).State.Layers.First(l => l.Slot == slot);

        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheGateSpawnsAPartyOfFourAndAHostileSkeletonWithEverythingBound()
        {
            yield return null;
            Assert.AreEqual(4, Services.Session.Party.Members.Count);
            Assert.AreEqual(5, CharacterAgent.AllLive.Count(a => a != null), "four heroes and the skeleton");

            foreach (var id in Services.Session.Party.Members)
            {
                var cues = Agent(id).GetComponent<SpriteActionCues>();
                Assert.IsTrue(cues.IsBound, "combat cues initialized on every member");
                Assert.IsNotNull(Agent(id).Motor);
            }

            var skeleton = CharacterAgent.AllLive.First(a => a != null && !Services.Session.Party.IsMember(a.EntityId));
            Assert.IsTrue(Services.Session.Characters.TryGetCharacter(skeleton.EntityId, out var skeletonState));
            Assert.IsTrue(skeletonState.TryGetResource(GateSessionServices.HitPoints, out var hp));
            Assert.AreEqual(13, hp.Max);
            Assert.IsTrue(Services.Spawner.TryGetFaction(skeleton.EntityId, out var faction));
            Assert.AreEqual("faction-undead", faction.Value);
            yield return null;
            Assert.IsNotNull(Layer(skeleton, Body).Sprite, "the skeleton body shows");
            Assert.IsTrue(Layer(skeleton, Body).Sprite.name.Contains("barrowsend_bodies"), "and it is the custom skeleton body, not a Kenney body");
        }

        [UnityTest]
        public IEnumerator GearWornAtSpawnShowsAndTheHelmetHidesHair()
        {
            yield return Seconds(0.1f);
            var aldric = Agent(Member(0));
            Assert.IsNotNull(Layer(aldric, Body).Sprite);
            Assert.IsNotNull(Layer(aldric, Helmet).Sprite, "Aldric wears the helm");
            Assert.IsFalse(Layer(aldric, Hair).Visible, "and it hides his hair");

            var dessa = Agent(Member(3));
            Assert.IsNull(Layer(dessa, Helmet).Sprite, "Dessa wears nothing");
            Assert.IsTrue(Layer(dessa, Hair).Visible);
        }

        [UnityTest]
        public IEnumerator ClickingAPartyMemberSelectsOnlyThemAndClickingTheSkeletonDoesNothing()
        {
            yield return Seconds(0.05f);
            Assert.AreEqual(Member(0), Services.Session.Party.ActiveCharacter);

            Click(Agent(Member(2)).transform.position);
            Assert.AreEqual(Member(2), Services.Session.Party.ActiveCharacter);
            Assert.AreEqual(1, Services.Session.Party.Selected.Count, "single selection");

            var skeleton = CharacterAgent.AllLive.First(a => a != null && !Services.Session.Party.IsMember(a.EntityId));
            Click(skeleton.transform.position); // PartyState.Select would throw for a non-member; the gate filters it
            Assert.AreEqual(Member(2), Services.Session.Party.ActiveCharacter, "selection is unchanged");
        }

        [UnityTest]
        public IEnumerator ClickingGroundMovesTheSelectedMemberThereOnTheGrid()
        {
            yield return Seconds(0.05f);
            var agent = Agent(Member(0));
            var target = CellCentre(12, 4);

            Click(target);
            yield return Seconds(8f);

            Assert.Less(Vector3.Distance(agent.transform.position, target), 0.6f, "walked to the clicked cell");
            Assert.AreEqual(MoveStatus.Arrived, agent.Motor.Snapshot.Status);
        }

        [UnityTest]
        public IEnumerator TheWallBlocksUntilTheDoorIsClickedOpenThenTheMemberWalksThrough()
        {
            yield return Seconds(0.05f);
            var agent = Agent(Member(0));
            var door = gate.DoorObject.GetComponent<UseDoorInteractionAdapter>();
            var north = CellCentre(12, 13);

            Click(north);
            yield return Seconds(1f);
            Assert.Less(agent.transform.position.z, GateLayout.WallRow, "closed door: the wall holds the member south");

            Click(gate.DoorObject.transform.position); // the door's own collider is hit: an interact command
            yield return Seconds(8f);
            Assert.IsTrue(door.IsOpen, "the member walked to the door's anchor cell and opened it");

            Click(north);
            yield return Seconds(10f);
            Assert.Greater(agent.transform.position.z, GateLayout.WallRow + 0.5f, "through the open door");
            Assert.Less(Vector3.Distance(agent.transform.position, north), 0.6f);
        }

        [UnityTest]
        public IEnumerator RealDamageDropsAMemberDownAndSmashKillsThemAndTheCorpseIsRemoved()
        {
            yield return Seconds(0.05f);
            var id = Member(3);
            var agent = Agent(id);
            Assert.IsTrue(Services.Session.Characters.TryGetCharacter(id, out var character));
            character.TryGetResource(GateSessionServices.HitPoints, out var hp);
            hp.Current = 3;

            StringAssert.Contains("Debug Hit", Services.Damage(id, "ability-debug-hit"));
            Assert.AreEqual(0, hp.Current);
            Assert.IsNotNull(character.DeathSaves, "0 HP starts death saves");
            yield return Seconds(0.5f);
            Assert.IsTrue(agent.GetComponent<SpriteActionCues>().State.IsDown);

            Services.HealAndStand(id);
            yield return Seconds(0.5f);
            Assert.IsFalse(agent.GetComponent<SpriteActionCues>().State.IsDown, "heal & stand recovers the sprite");
            Assert.AreEqual(hp.Max, hp.Current);
            Assert.IsNull(character.DeathSaves);

            Services.Damage(id, "ability-debug-smash");
            Assert.IsTrue(agent != null, "the corpse lingers for the death cue");
            yield return Seconds(3f);
            Assert.IsTrue(agent == null, "and is destroyed after the fade");
            Assert.IsFalse(CharacterAgent.AllLive.Any(a => a != null && a.EntityId == id));
        }

        [UnityTest]
        public IEnumerator EquipButtonsToggleGearLive()
        {
            yield return Seconds(0.05f);
            var id = Member(3);
            var agent = Agent(id);

            StringAssert.Contains("equipped", Services.ToggleEquip(id, "item-iron-helm"));
            yield return Seconds(0.1f);
            Assert.IsNotNull(Layer(agent, Helmet).Sprite);
            Assert.IsFalse(Layer(agent, Hair).Visible);

            StringAssert.Contains("unequipped", Services.ToggleEquip(id, "item-iron-helm"));
            yield return Seconds(0.1f);
            Assert.IsNull(Layer(agent, Helmet).Sprite);
            Assert.IsTrue(Layer(agent, Hair).Visible);
        }

        [UnityTest]
        public IEnumerator ACharacterSouthOfTheWallDrawsAboveItAndOneNorthOfItDrawsBelow()
        {
            yield return Seconds(0.05f);
            var agent = Agent(Member(0));
            var wall = GameObject.Find("Wall 5").GetComponent<SpriteRenderer>();
            var renderers = Compositor(agent).Renderers;

            agent.Motor.Warp(CellCentre(5, GateLayout.WallRow - 1));
            yield return Seconds(0.1f);
            Assert.Greater(renderers.Min(r => r.sortingOrder), wall.sortingOrder, "south of the wall (lower Z) is in front of it");

            agent.Motor.Warp(CellCentre(5, GateLayout.WallRow + 2));
            yield return Seconds(0.1f);
            Assert.Less(renderers.Max(r => r.sortingOrder), wall.sortingOrder, "north of the wall (higher Z) is behind it");
        }

        [UnityTest]
        public IEnumerator TwoMembersSortAgainstEachOtherByZ()
        {
            yield return Seconds(0.05f);
            var a = Agent(Member(0));
            var b = Agent(Member(1));
            a.Motor.Warp(CellCentre(5, 5));
            b.Motor.Warp(CellCentre(5, 7));
            yield return Seconds(0.1f);
            Assert.Greater(Compositor(a).Renderers.Min(r => r.sortingOrder), Compositor(b).Renderers.Max(r => r.sortingOrder), "lower Z draws in front");
        }

        [UnityTest]
        public IEnumerator TenSimulatedSecondsOfEverythingLogsNoErrorsOrExceptions()
        {
            yield return Seconds(0.05f);
            Click(CellCentre(10, 5));
            Services.ToggleEquip(Member(1), "item-iron-helm");
            Services.PlayAttack(Member(1));
            Services.Damage(Member(2), "ability-debug-hit");
            yield return Seconds(2f);
            Services.ToggleDoor(gate.DoorObject.transform);
            Services.RollDeathSave(Member(2));
            Services.HealAndStand(Member(2));
            Camera.main.GetComponent<GateCamera>().ZoomIn();
            Camera.main.GetComponent<GateCamera>().ZoomOut();
            Assert.IsTrue(gate.QuickSave());
            yield return Seconds(2f);
            Assert.IsTrue(gate.QuickLoad());
            Click(Agent(Member(3)).transform.position);
            Services.Damage(Member(3), "ability-debug-smash");
            yield return Seconds(6f);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator QuickSaveThenLoadRestoresAppearanceGearPositionAndDownedStateAndRewiresEverything()
        {
            yield return Seconds(0.05f);
            var aldric = Member(0);
            var dessa = Member(3);

            // Move Aldric, put Dessa down and give her a helm, then save.
            Click(CellCentre(10, 5));
            yield return Seconds(6f);
            var aldricPos = Agent(aldric).transform.position;
            Services.ToggleEquip(dessa, "item-iron-helm");
            Services.Session.Characters.TryGetCharacter(dessa, out var dessaState);
            dessaState.TryGetResource(GateSessionServices.HitPoints, out var dessaHp);
            dessaHp.Current = 2;
            Services.Damage(dessa, "ability-debug-hit");
            yield return Seconds(0.5f);
            Assert.IsNotNull(dessaState.DeathSaves);
            Assert.IsTrue(gate.QuickSave());

            // Change everything, then load.
            Services.ToggleEquip(dessa, "item-iron-helm");
            Services.HealAndStand(dessa);
            Click(CellCentre(3, 12));
            yield return Seconds(1f);
            var sessionBefore = Services.Session;

            Assert.IsTrue(gate.QuickLoad(), gate.LastMessage);
            yield return Seconds(0.3f);

            Assert.AreNotSame(sessionBefore, Services.Session, "a brand-new session");
            Assert.AreEqual(2, Services.RebindCount, "the session consumers were rebuilt");
            Assert.AreEqual(4, Services.Session.Party.Members.Count);
            Assert.AreEqual(5, CharacterAgent.AllLive.Count(a => a != null), "party and skeleton are back, none duplicated");

            Assert.AreEqual(0f, Vector3.Distance(Agent(aldric).transform.position, aldricPos), 0.05f, "position restored");
            Assert.IsNotNull(Layer(Agent(aldric), Helmet).Sprite, "gear restored");
            Assert.IsNotNull(Layer(Agent(dessa), Helmet).Sprite, "the helm Dessa wore at save time is back");
            Assert.IsTrue(Services.Session.Characters.TryGetCharacter(dessa, out var restored));
            Assert.IsNotNull(restored.DeathSaves, "downed state restored");
            Assert.IsTrue(Agent(dessa).GetComponent<SpriteActionCues>().State.IsDown, "and she is drawn down");

            // The rebuilt click stack works against the NEW session.
            Click(Agent(Member(1)).transform.position);
            Assert.AreEqual(Member(1), Services.Session.Party.ActiveCharacter);
            var target = CellCentre(12, 6);
            Click(target);
            yield return Seconds(8f);
            Assert.Less(Vector3.Distance(Agent(Member(1)).transform.position, target), 0.6f, "movement works after a load");
        }
    }
}
