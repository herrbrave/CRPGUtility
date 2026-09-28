using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Shell;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.InputSystem;
using CRPG.Presentation.Sprites;
using CRPG.Rules.SRD5;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 4.3: the creator in the real game, end to end. Title mode boots to the title without loading the town; New Game shows the creator (Cancel returns to the
    /// title); Confirm builds the protagonist and the shell spawns the party with them first, selected, with real hit points, wearing and wielding the class kit,
    /// equal slot for slot to the creator's preview; a save can be continued from the title with the creator skipped; and in AuthoredParty mode the companions'
    /// authored gear is finally worn and no screen leaks pointer capture. Clicks go through <see cref="UiClick"/> in panel coordinates. The batch game view is
    /// 640x480, so New Game / Continue / Confirm are clicked with a pointer and Cancel (about x=647, outside the view) with a submit; wide row selections use the model.
    /// </summary>
    public class ShellStartFlowTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private const string TownPath = "Assets/Scenes/Locations/Town_Slice.unity";
        private static readonly RuntimeEntityId ProtagonistId = RuntimeEntityId.FromString("ba000000000000000000000000000001");
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private ShellBootstrap shell;
        private string savePath;

        [SetUp]
        public void SetUp() => savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-start-test-{Guid.NewGuid():N}.json");

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            ShellBootstrap.StartingSceneOverride = null;
            if (File.Exists(savePath)) File.Delete(savePath);
            Time.timeScale = 1f;
        }

        // ---------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Mathf.Min(Time.deltaTime, 0.1f);
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds)
            {
                elapsed += Mathf.Min(Time.deltaTime, 0.1f);
                yield return null;
            }

            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private IEnumerator LoadCore(ShellStartMode mode)
        {
            ShellBootstrap.StartModeOverride = mode; // BEFORE the scene loads: Boot reads it in Start
            // Task 6.0: this file's assertions/click coordinates are written against Town_Slice's own small
            // geometry (including "the town is not loaded behind the title"), not the real Town -- keep booting
            // the demo slice on purpose.
            ShellBootstrap.StartingSceneOverride = (TownPath, "start");
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
        }

        private static VisualElement RootOf<T>() where T : Component => UnityEngine.Object.FindFirstObjectByType<T>().GetComponent<UIDocument>().rootVisualElement;
        private static Button TitleButton(string name) => RootOf<ShellTitleScreen>().Q<Button>(name);
        private static Button CreatorButton(string name) => RootOf<CharacterCreatorScreen>().Q<Button>(name);

        private GameSession Session => shell.Services.Session;
        private CharacterAgent Agent(RuntimeEntityId id) => CharacterAgent.AllLive.First(a => a != null && a.EntityId == id);
        private static SpriteLayerState LiveLayers(CharacterAgent agent) => agent.GetComponentInChildren<SpriteLayerCompositor>(true).State;

        private static Sprite LayerSprite(SpriteLayerState state, string slot)
        {
            Assert.IsTrue(state.TryGetSlotIndex(new DefinitionId(slot), out var i), slot);
            return state.Layers[i].Sprite;
        }

        private static Dictionary<string, (Sprite sprite, Color tint, bool visible)> Snapshot(SpriteLayerState state)
        {
            var snapshot = new Dictionary<string, (Sprite, Color, bool)>();
            for (var i = 0; i < state.SlotCount; i++)
            {
                var layer = state.Layers[i];
                snapshot[state.SlotAt(i).Value] = (layer.Sprite, layer.Tint, layer.Visible);
            }

            return snapshot;
        }

        /// <summary>New Game, choose a class/look through the model, Confirm through the real button, and wait for the shell to finish booting.</summary>
        private IEnumerator CreateCharacter(string classId, int colour, int style, int skin)
        {
            yield return LoadCore(ShellStartMode.Title);
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Title, 20f, "the title");
            yield return Seconds(0.3f);
            UiClick.Pointer(TitleButton("new-game-button"));
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Creating, 5f, "the creator");
            yield return Seconds(0.3f);

            var model = shell.CreatorModel;
            model.ClassRow.Select(new DefinitionId(classId));
            model.RowFor("hair-color").SelectIndex(colour);
            model.RowFor("layer-hair").SelectIndex(style);
            model.RowFor("layer-body").SelectIndex(skin);
            yield return null;

            UiClick.Pointer(CreatorButton("confirm-button"));
            yield return WaitUntil(() => shell.IsReady, 30f, "the shell finished booting");
            yield return Seconds(0.3f);
        }

        private void Click(Vector3 world)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<InputSystemCommandSource>();
            var router = typeof(InputSystemCommandSource).GetProperty("Router", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(world);
            var ray = cam.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            router.GetType().GetMethod("HandlePointerDown").Invoke(router, new object[] { screen, ray });
            router.GetType().GetMethod("HandlePointerUp").Invoke(router, new object[] { screen, ray, cam, false });
        }

        // ---------------------------------------------------------------------------------------------
        // Title, New Game, Cancel
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TitleModeBootsToTheTitleWithoutLoadingTheTownAndContinueNeedsASave()
        {
            yield return LoadCore(ShellStartMode.Title);
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Title, 20f, "the title");
            yield return Seconds(0.3f);

            Assert.IsFalse(shell.IsReady, "not playing yet");
            Assert.IsFalse(SceneManager.GetSceneByPath(TownPath).isLoaded, "the town is not loaded behind the title");
            Assert.IsNotNull(TitleButton("new-game-button"));
            var continueButton = TitleButton("continue-button");
            Assert.IsFalse(continueButton.enabledSelf, "no save yet");

            File.WriteAllText(savePath, "{}"); // any file: the title only asks whether a save exists
            yield return WaitUntil(() => continueButton.enabledSelf, 3f, "Continue enabled once a save exists");
        }

        [UnityTest]
        public IEnumerator NewGameShowsTheCreatorInPlaceOfTheTitleAndCancelReturnsToIt()
        {
            yield return LoadCore(ShellStartMode.Title);
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Title, 20f, "the title");
            yield return Seconds(0.3f);
            Assert.AreEqual(0, shell.CreatorRuns);

            UiClick.Pointer(TitleButton("new-game-button"));
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Creating, 5f, "the creator");
            yield return Seconds(0.3f);
            Assert.AreEqual(1, shell.CreatorRuns);
            Assert.AreEqual(DisplayStyle.Flex, RootOf<CharacterCreatorScreen>().resolvedStyle.display, "the creator is shown");
            Assert.AreEqual(DisplayStyle.None, RootOf<ShellTitleScreen>().resolvedStyle.display, "the title is hidden while the creator is open");
            Assert.IsFalse(shell.IsReady);

            UiClick.Submit(CreatorButton("cancel-button")); // Cancel is out of the 640x480 view: the submit floor
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Title, 5f, "back at the title");
            yield return Seconds(0.3f);
            Assert.AreEqual(DisplayStyle.None, RootOf<CharacterCreatorScreen>().resolvedStyle.display, "the creator is hidden again");
            Assert.AreEqual(DisplayStyle.Flex, RootOf<ShellTitleScreen>().resolvedStyle.display);

            UiClick.Pointer(TitleButton("new-game-button"));
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Creating, 5f, "the creator again");
            Assert.AreEqual(2, shell.CreatorRuns);
        }

        // ---------------------------------------------------------------------------------------------
        // The protagonist in the world
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ConfirmSpawnsTheProtagonistFirstSelectedWithHitPointsAndTheKitWornAndEqualToThePreview()
        {
            yield return LoadCore(ShellStartMode.Title);
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Title, 20f, "the title");
            yield return Seconds(0.3f);
            UiClick.Pointer(TitleButton("new-game-button"));
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Creating, 5f, "the creator");
            yield return Seconds(0.3f);

            var model = shell.CreatorModel;
            model.ClassRow.Select(new DefinitionId("class-wizard"));
            model.RowFor("hair-color").SelectIndex(1);
            model.RowFor("layer-hair").SelectIndex(3);
            model.RowFor("layer-body").SelectIndex(2);
            yield return null;
            var previewBefore = Snapshot(model.Preview);
            var chosenHair = model.RowFor("layer-hair").Selected.Id;
            var chosenSkin = model.RowFor("layer-body").Selected.Id;
            var kit = model.StartingItems.Select(i => i.Id).ToList();

            UiClick.Pointer(CreatorButton("confirm-button"));
            yield return WaitUntil(() => shell.IsReady, 30f, "the shell finished booting");
            yield return Seconds(0.3f);

            Assert.AreEqual(ShellStartPhase.Playing, shell.Phase);
            Assert.AreEqual(4, Session.Party.Members.Count);
            Assert.AreEqual(ProtagonistId, Session.Party.Members[0], "the protagonist takes slot 1");
            Assert.AreEqual(ProtagonistId, Session.Party.ActiveCharacter, "and is the selected member");

            Assert.IsTrue(Session.Characters.TryGetCharacter(ProtagonistId, out var character));
            Assert.AreEqual(chosenHair, character.Appearance[new DefinitionId("layer-hair")]);
            Assert.AreEqual(chosenSkin, character.Appearance[new DefinitionId("layer-body")]);
            Assert.IsTrue(character.TryGetResource(HitPoints, out var hp));
            Assert.IsTrue(Session.RulesDatabase.TryResolve<ClassDefinition>(new DefinitionId("class-wizard"), out var wizard));
            var expectedHp = wizard.HitDie.Count * wizard.HitDie.Sides + wizard.HitDie.Modifier + AbilityModifier.From(character.Stats.GetScore(new DefinitionId("stat-constitution")));
            Assert.AreEqual(expectedHp, hp.Max, "hit die + Constitution modifier");
            Assert.AreEqual(hp.Max, hp.Current);

            Assert.IsTrue(Session.Inventory.TryGetEquippedItem(ProtagonistId, new DefinitionId("slot-armor"), out var armor), "the class armor is worn");
            Assert.IsTrue(Session.Inventory.TryGetEquippedItem(ProtagonistId, new DefinitionId("slot-mainhand"), out var weapon), "the class weapon is wielded");
            CollectionAssert.AreEqual(kit, new[] { armor.DefinitionId, weapon.DefinitionId });

            var live = LiveLayers(Agent(ProtagonistId));
            Assert.IsNotNull(LayerSprite(live, "layer-clothes"), "the protagonist paints the clothes layer: the armor is their only outfit");
            var liveSnapshot = Snapshot(live);
            foreach (var slot in previewBefore.Keys)
            {
                Assert.AreSame(previewBefore[slot].sprite, liveSnapshot[slot].sprite, $"{slot}: the world draws the sprite the preview showed");
                Assert.AreEqual(previewBefore[slot].tint, liveSnapshot[slot].tint, $"{slot}: tint");
                Assert.AreEqual(previewBefore[slot].visible, liveSnapshot[slot].visible, $"{slot}: visibility");
            }

            var agent = Agent(ProtagonistId);
            var from = agent.transform.position;
            Click(new Vector3(from.x + 2f, 0f, from.z));
            yield return Seconds(1.5f);
            Assert.Greater(Vector3.Distance(from, agent.transform.position), 0.5f, "the protagonist walks when the ground is clicked (the real click stack)");
        }

        [UnityTest]
        public IEnumerator EveryPartyMembersAgentIsBoundAndTheCompanionsKeepTheirAuthoredIds()
        {
            yield return CreateCharacter("class-fighter", 0, 0, 0);

            var ids = Session.Party.Members.ToList();
            Assert.AreEqual(RuntimeEntityId.FromString("ba000000000000000000000000000002"), ids[1]);
            Assert.AreEqual(RuntimeEntityId.FromString("ba000000000000000000000000000004"), ids[3]);
            foreach (var id in ids) Assert.IsNotNull(Agent(id), $"an agent for {id}");
            Assert.AreEqual(4, CharacterAgent.AllLive.Count(a => a != null), "no duplicates");
        }

        // ---------------------------------------------------------------------------------------------
        // Save then Continue
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASavedGameContinuesFromTheTitleWithTheCreatorSkippedAndTheSavedProtagonistBack()
        {
            yield return CreateCharacter("class-fighter", 3, 5, 1);
            Assert.IsTrue(shell.SaveNow(), "saved");
            Assert.IsTrue(Session.Characters.TryGetCharacter(ProtagonistId, out var saved));
            var appearance = saved.Appearance.ToDictionary(p => p.Key, p => p.Value);
            saved.TryGetResource(HitPoints, out var savedHp);
            var savedMax = savedHp.Max;
            Assert.IsTrue(Session.Inventory.TryGetEquippedItem(ProtagonistId, new DefinitionId("slot-armor"), out var savedArmor));
            Assert.IsTrue(Session.Inventory.TryGetEquippedItem(ProtagonistId, new DefinitionId("slot-mainhand"), out var savedWeapon));

            yield return LoadCore(ShellStartMode.Title); // a fresh boot with a save present
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Title, 20f, "the title");
            yield return Seconds(0.3f);
            var continueButton = TitleButton("continue-button");
            yield return WaitUntil(() => continueButton.enabledSelf, 3f, "Continue enabled");

            UiClick.Pointer(continueButton);
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Playing && !shell.IsLoading && !shell.Gate.IsBusy, 40f, "the load finished");
            yield return Seconds(0.3f);

            Assert.AreEqual(0, shell.CreatorRuns, "the creator never ran");
            Assert.AreEqual(ProtagonistId, Session.Party.Members[0]);
            Assert.AreEqual(4, Session.Party.Members.Count);
            Assert.AreEqual(4, CharacterAgent.AllLive.Count(a => a != null), "the load replaced the authored default party: no duplicate agents");
            Assert.IsTrue(Session.Characters.TryGetCharacter(ProtagonistId, out var restored));
            foreach (var pair in appearance) Assert.AreEqual(pair.Value, restored.Appearance[pair.Key], pair.Key.Value);
            Assert.IsTrue(restored.TryGetResource(HitPoints, out var restoredHp));
            Assert.AreEqual(savedMax, restoredHp.Max, "hit points restored");
            Assert.IsTrue(Session.Inventory.TryGetEquippedItem(ProtagonistId, new DefinitionId("slot-armor"), out var armor));
            Assert.IsTrue(Session.Inventory.TryGetEquippedItem(ProtagonistId, new DefinitionId("slot-mainhand"), out var weapon));
            Assert.AreEqual(savedArmor.DefinitionId, armor.DefinitionId);
            Assert.AreEqual(savedWeapon.DefinitionId, weapon.DefinitionId);
            Assert.IsNotNull(LayerSprite(LiveLayers(Agent(ProtagonistId)), "layer-clothes"), "the restored protagonist is still dressed in the armor");
            Assert.IsNotNull(LayerSprite(LiveLayers(Agent(ProtagonistId)), "layer-weapon"));
        }

        // ---------------------------------------------------------------------------------------------
        // AuthoredParty mode: companions' gear, no capture leak
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator InAuthoredPartyModeTheCompanionsWearTheirAuthoredGearAndNoScreenCapturesThePointer()
        {
            yield return LoadCore(ShellStartMode.AuthoredParty);
            yield return WaitUntil(() => shell.IsReady, 30f, "the shell finished booting");
            yield return Seconds(0.5f);

            Assert.AreEqual(ShellStartPhase.Playing, shell.Phase);
            var members = Session.Party.Members;
            var source = new SpriteCatalogSource(Session.RulesDatabase);

            var aldric = LiveLayers(Agent(members[0])); // helm, short sword, round shield
            Assert.IsNotNull(LayerSprite(aldric, "layer-helmet"), "Aldric's helm is worn");
            Assert.IsNotNull(LayerSprite(aldric, "layer-weapon"), "Aldric's sword is wielded");
            Assert.IsTrue(aldric.TryGetSlotIndex(new DefinitionId("layer-hair"), out var hair));
            Assert.IsFalse(aldric.Layers[hair].Visible, "the helm hides his hair");

            Assert.IsNotNull(LayerSprite(LiveLayers(Agent(members[1])), "layer-weapon"), "Brenna's sword is wielded");

            Assert.IsTrue(source.TryGetOption(new DefinitionId("layer-clothes"), new DefinitionId("clothes-kenney-6-0"), out var tunic));
            Assert.AreSame(tunic.Sprite, LayerSprite(LiveLayers(Agent(members[2])), "layer-clothes"), "Corvin's tunic (clothes-kenney-6-0) replaces his authored outfit: equipment beats appearance");

            var capture = new UIToolkitPointerCapture();
            var emptyCorner = new Vector2(2f, Screen.height - 2f);
            Assert.IsFalse(capture.IsPointerOverUI(emptyCorner), "the hidden title and creator screens do not capture the pointer over the world");

            // Unbuilt screens capture nothing whether hidden or not, so that alone cannot tell whether Boot hid them. BUILD both now: a screen builds itself in whatever visibility
            // state it is in, so if Boot did not hide them they would come up full-screen and registered, and the world would be unclickable.
            var picks = File.ReadAllText("Assets/Content/Creator/CreatorPicks.json");
            var table = File.ReadAllText("Assets/Content/Creator/CreatorClassTable.json");
            var model = new CRPG.Presentation.Creator.CharacterCreatorModel(Session.RulesDatabase, CreatorContentLoader.Build(picks, table));
            UnityEngine.Object.FindFirstObjectByType<CharacterCreatorScreen>().Initialize(model, _ => { });
            UnityEngine.Object.FindFirstObjectByType<ShellTitleScreen>().Initialize(() => false, () => { }, () => { });
            yield return Seconds(0.3f);
            Assert.IsFalse(capture.IsPointerOverUI(emptyCorner), "and they stay hidden when they are built later: Boot hid them on this path too");
        }
    }
}
