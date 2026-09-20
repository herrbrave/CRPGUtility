using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Combat;
using CRPG.Gameplay;
using CRPG.Gameplay.Combat;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Presentation;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// 2D campaign Task 2.6: SpriteActionCues + SpriteActionPresentationHook on the REAL Grid + Sprite fixture prefab bound through the real
    /// WorldSpawnService, and the deferred-destroy (IDefeatPresentation) path of WorldSpawnService.MarkDefeated.
    /// </summary>
    public class SpriteActionCuesTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");
        private static readonly int BodyIndex = SpriteLayerCompositor.DefaultSlots().IndexOf(new DefinitionId("layer-body"));
        private static readonly Vector3 Origin = new(6000f, 0f, 10f);

        private readonly List<Object> made = new();
        private GameSession session;
        private WorldSpawnService spawner;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            spawner = new WorldSpawnService(session);
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object { made.Add(o); return o; }

        private sealed class Rig
        {
            public GameObject Go;
            public SpriteActionCues Cues;
            public SpriteLayerCompositor Compositor;
            public CharacterState Character;
            public ResourceState Hp;
            public RuntimeEntityId Id;
            public Transform LayersRoot => Compositor.LayersRoot;
        }

        private static IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++) yield return null;
        }

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
        }

        private static GameObject Fixture(string name)
        {
            var prefab = Resources.Load<GameObject>("Fixtures/" + name);
            Assert.IsNotNull(prefab, $"Resources/Fixtures/{name}.prefab is missing -- run 'CRPG Toolkit/Characters/Author Sprite Character Test Fixtures'.");
            return prefab;
        }

        private static void FastSettings(SpriteCueSettings s)
        {
            s.attackSeconds = 0.05f;
            s.hitSeconds = 0.05f;
            s.tipSeconds = 0.05f;
            s.dieFadeSeconds = 0.05f;
            s.defeatHoldSeconds = 0.05f;
        }

        private Rig Spawn(string spawnId = "hero", bool hpId = true, int hp = 20, bool deathSaves = false, Action<SpriteCueSettings> settings = null, bool withCues = true)
        {
            var template = Object.Instantiate(Fixture("SpriteCharacter"), new Vector3(6000f, 0f, 10f), Quaternion.identity);
            template.SetActive(false);
            var templateCues = template.GetComponent<SpriteActionCues>();
            if (!withCues) Object.DestroyImmediate(templateCues);
            else
            {
                if (hpId) templateCues.HitPointResourceId = HitPoints;
                (settings ?? FastSettings)(templateCues.Settings);
            }

            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(HitPoints, max: 20));
            character.TryGetResource(HitPoints, out var hpState);
            hpState.Current = hp;
            if (deathSaves) character.DeathSaves = new DeathSaveState();
            session.Characters.Register(character);

            var go = spawner.BindRestoredView(spawnId, character.Id, default, template, Origin + new Vector3(2.5f, 0f, 2.5f), Quaternion.identity, default, startInactive: false);
            Object.DestroyImmediate(template);
            go.SetActive(true); // the clone of an inactive template is inactive
            Track(go);
            var compositor = go.GetComponentInChildren<SpriteLayerCompositor>(true);
            // The fixture's layers have no sprites (a layer with none has a transparent tint), so give the body one to make colours observable.
            var texture = Track(new Texture2D(4, 4));
            compositor.State.SetAppearance(new DefinitionId("layer-body"), new SpriteOption(Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f))), Color.white));
            return new Rig
            {
                Go = go,
                Cues = go.GetComponent<SpriteActionCues>(),
                Compositor = compositor,
                Character = character,
                Hp = hpState,
                Id = character.Id,
            };
        }

        private void Publish(GameEvent e, RuntimeEntityId target) =>
            session.EventBus.Publish(new GameEventContext(e, RuntimeEntityId.NewId(), target));

        private static AbilityDefinition Ability(AbilityTag tags)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            typeof(AbilityDefinition).GetField("tags", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ability, tags);
            return ability;
        }

        // ---------------------------------------------------------------------------------------------
        // Binding / contract
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BindViewInitializesTheCuesOnTheRealPrefab()
        {
            var rig = Spawn();
            yield return null;
            Assert.IsNotNull(rig.Cues);
            Assert.IsTrue(rig.Cues.IsBound);
            Assert.AreEqual(HitPoints, rig.Cues.HitPointResourceId);
        }

        [UnityTest]
        public IEnumerator InitializeContract()
        {
            var rig = Spawn();
            yield return null;

            Assert.DoesNotThrow(() => rig.Cues.Initialize(rig.Id, session.EventBus, session.Characters), "an identical re-init is a no-op");
            Assert.Throws<InvalidOperationException>(() => rig.Cues.Initialize(RuntimeEntityId.NewId(), session.EventBus, session.Characters));

            rig.Cues.Unbind();
            Assert.IsFalse(rig.Cues.IsBound);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Cannot bind"));
            rig.Cues.Initialize(RuntimeEntityId.None, session.EventBus, session.Characters);
            Assert.IsFalse(rig.Cues.IsBound);
        }

        [UnityTest]
        public IEnumerator UnboundCuesIgnoreEvents()
        {
            var rig = Spawn();
            rig.Cues.Unbind();
            Publish(GameEvent.AfterDamage, rig.Id);
            yield return Frames(2);
            Assert.AreEqual(Color.white, rig.Compositor.Renderers[BodyIndex].color);
        }

        // ---------------------------------------------------------------------------------------------
        // Hit / attack
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator HitFlashesRedThenReturnsToNormal()
        {
            var rig = Spawn(settings: s => { FastSettings(s); s.hitSeconds = 1f; }); // long enough that two slow frames (each capped at 0.1 s) cannot end the flash early
            yield return Frames(2);
            var normal = rig.Compositor.Renderers[BodyIndex].color;

            Publish(GameEvent.AfterDamage, rig.Id);
            yield return Frames(2);
            var flash = rig.Compositor.Renderers[BodyIndex].color;
            Assert.Less(flash.g, normal.g - 0.1f, "reddened");
            Assert.AreEqual(normal.r, flash.r, 0.05f);

            yield return Seconds(1.3f);
            yield return Frames(2);
            Assert.AreEqual(normal, rig.Compositor.Renderers[BodyIndex].color, "and back to normal");
        }

        [UnityTest]
        public IEnumerator OtherCharactersEventsAreIgnored()
        {
            var rig = Spawn();
            yield return Frames(2);
            Publish(GameEvent.AfterDamage, RuntimeEntityId.NewId());
            Publish(GameEvent.CreatureDroppedToZeroHP, RuntimeEntityId.NewId());
            yield return Frames(3);
            Assert.AreEqual(Color.white, rig.Compositor.Renderers[BodyIndex].color);
            Assert.IsFalse(rig.Cues.State.IsDown);
        }

        [UnityTest]
        public IEnumerator AttackViaTheHookHopsAndReturnsToTheExactBaseline()
        {
            var rig = Spawn(settings: s => { FastSettings(s); s.attackSeconds = 0.3f; });
            yield return Frames(2);
            var basePos = rig.LayersRoot.localPosition;
            var baseScale = rig.LayersRoot.localScale;
            var baseRot = rig.LayersRoot.localRotation;
            var ability = Track(Ability(AbilityTag.Attack));

            new SpriteActionPresentationHook().RequestActionCue(rig.Id, ability);

            var maxHop = 0f;
            var maxScale = 0f;
            var elapsed = 0f;
            while (elapsed < 0.6f)
            {
                elapsed += Mathf.Min(Time.deltaTime, 0.1f);
                yield return null;
                maxHop = Mathf.Max(maxHop, rig.LayersRoot.localPosition.y - basePos.y);
                maxScale = Mathf.Max(maxScale, rig.LayersRoot.localScale.x);
            }

            Assert.Greater(maxHop, 0.05f);
            Assert.Greater(maxScale, baseScale.x * 1.05f);
            Assert.AreEqual(0f, Vector3.Distance(basePos, rig.LayersRoot.localPosition), 1e-4f, "exact baseline afterwards");
            Assert.AreEqual(baseScale, rig.LayersRoot.localScale);
            Assert.Less(Quaternion.Angle(baseRot, rig.LayersRoot.localRotation), 0.01f);
        }

        [UnityTest]
        public IEnumerator TheHookOnlyPlaysForAttackOrHostileAbilitiesAndNeverThrows()
        {
            var rig = Spawn();
            yield return Frames(2);
            var hook = new SpriteActionPresentationHook();

            hook.RequestActionCue(rig.Id, Track(Ability(AbilityTag.Healing)));
            Assert.AreEqual(0f, rig.Cues.State.HopOffset);
            hook.RequestActionCue(rig.Id, Track(Ability(AbilityTag.None)));
            hook.RequestActionCue(rig.Id, null);
            hook.RequestActionCue(RuntimeEntityId.NewId(), Track(Ability(AbilityTag.Attack)));
            hook.RequestActionCue(RuntimeEntityId.None, Track(Ability(AbilityTag.Attack)));

            hook.RequestActionCue(rig.Id, Track(Ability(AbilityTag.Hostile)));
            yield return Frames(3);
            Assert.Greater(rig.Cues.State.Scale, 1f, "Hostile alone plays it");
        }

        [UnityTest]
        public IEnumerator TheHookIsANoOpForACharacterWithoutCues()
        {
            var rig = Spawn(withCues: false);
            yield return null;
            Assert.IsNull(rig.Cues);
            Assert.DoesNotThrow(() => new SpriteActionPresentationHook().RequestActionCue(rig.Id, Track(Ability(AbilityTag.Attack))));
        }

        // ---------------------------------------------------------------------------------------------
        // Down / recover / restore / swap
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator DownTipsAboutTheFeetLeavingColliderRockPivotAndRootUntouched()
        {
            var rig = Spawn();
            yield return Frames(2);
            var box = rig.Go.GetComponent<BoxCollider>();
            var boxSize = box.size;
            var boxCenter = box.center;
            var rootPos = rig.Go.transform.position;
            var rockPivot = rig.Go.GetComponentInChildren<RockingMotionDriver>().RockPivot;
            var pivotPos = rockPivot.localPosition;
            var basePos = rig.LayersRoot.localPosition;

            hpToZero(rig);
            Publish(GameEvent.CreatureDroppedToZeroHP, rig.Id);
            yield return Seconds(0.3f);
            yield return Frames(2);

            var layers = rig.LayersRoot;
            Assert.AreEqual(90f, Mathf.Abs(Mathf.DeltaAngle(0f, layers.localEulerAngles.z)), 0.5f, "tipped a quarter turn");
            var expected = Quaternion.Euler(0f, 0f, -90f) * basePos;
            Assert.AreEqual(0f, Vector3.Distance(expected, layers.localPosition), 1e-3f, "rotated about the feet, not the centre");
            Assert.AreEqual(boxSize, box.size);
            Assert.AreEqual(boxCenter, box.center);
            Assert.AreEqual(rootPos, rig.Go.transform.position);
            Assert.AreEqual(pivotPos, rockPivot.localPosition);
            Assert.Less(Quaternion.Angle(Quaternion.identity, rockPivot.localRotation), 0.01f, "RockPivot is still level (the rocking driver owns it, cues never touch it)");
        }

        private static void hpToZero(Rig rig)
        {
            rig.Hp.Current = 0;
            rig.Character.DeathSaves = new DeathSaveState();
        }

        [UnityTest]
        public IEnumerator RecoverStandsBackUpWhenHpIsAboveZero()
        {
            var rig = Spawn();
            yield return Frames(2);
            var basePos = rig.LayersRoot.localPosition;
            hpToZero(rig);
            Publish(GameEvent.CreatureDroppedToZeroHP, rig.Id);
            yield return Seconds(0.2f);

            Publish(GameEvent.CreatureStabilized, rig.Id); // still 0 HP: stays down
            yield return Seconds(0.2f);
            Assert.IsTrue(rig.Cues.State.IsDown);

            rig.Hp.Current = 1;
            rig.Character.DeathSaves = null;
            Publish(GameEvent.CreatureStabilized, rig.Id);
            yield return Seconds(0.3f);
            yield return Frames(2);

            Assert.IsFalse(rig.Cues.State.IsDown);
            Assert.AreEqual(0f, Vector3.Distance(basePos, rig.LayersRoot.localPosition), 1e-3f);
            Assert.Less(Quaternion.Angle(Quaternion.identity, rig.LayersRoot.localRotation), 0.1f);
        }

        [UnityTest]
        public IEnumerator DieFadesToCorpseAlphaAndStaysDeadDespiteLaterRecoverEvents()
        {
            var rig = Spawn(settings: s => { FastSettings(s); s.deadAlpha = 0.4f; });
            yield return Frames(2);

            Publish(GameEvent.CreatureDied, rig.Id);
            yield return Seconds(0.4f);
            yield return Frames(2);
            Assert.AreEqual(0.4f, rig.Compositor.Renderers[BodyIndex].color.a, 0.02f);

            rig.Hp.Current = 5;
            Publish(GameEvent.CreatureStabilized, rig.Id);
            Publish(GameEvent.ConditionRemoved, rig.Id);
            yield return Seconds(0.2f);
            Assert.IsTrue(rig.Cues.State.IsDead);
            Assert.AreEqual(0.4f, rig.Compositor.Renderers[BodyIndex].color.a, 0.02f);
        }

        [UnityTest]
        public IEnumerator ARestoredUnconsciousCharacterAppearsDownFromTheFirstFrame()
        {
            var rig = Spawn(hp: 0, deathSaves: true);
            yield return Frames(2);
            Assert.IsTrue(rig.Cues.State.IsDown);
            Assert.AreEqual(90f, Mathf.Abs(Mathf.DeltaAngle(0f, rig.LayersRoot.localEulerAngles.z)), 0.5f);
        }

        [UnityTest]
        public IEnumerator ARestoredStableAtZeroHpCharacterAppearsDownWithoutDeathSaveState()
        {
            var rig = Spawn(hp: 0, deathSaves: false);
            yield return Frames(2);
            Assert.IsTrue(rig.Cues.State.IsDown);
        }

        [UnityTest]
        public IEnumerator WithoutAConfiguredHitPointIdARestoredZeroHpCharacterOnlyShowsDownWithDeathSaves()
        {
            var noSaves = Spawn("a", hpId: false, hp: 0, deathSaves: false);
            var saves = Spawn("b", hpId: false, hp: 0, deathSaves: true);
            yield return Frames(2);
            Assert.IsFalse(noSaves.Cues.State.IsDown);
            Assert.IsTrue(saves.Cues.State.IsDown);
        }

        [UnityTest]
        public IEnumerator AModelSwapWhileDownKeepsTheCorpseDown()
        {
            var rig = Spawn();
            yield return Frames(2);
            hpToZero(rig);
            Publish(GameEvent.CreatureDroppedToZeroHP, rig.Id);
            yield return Seconds(0.3f);

            rig.Go.GetComponentInChildren<CharacterView>().SetModel(Fixture("SpriteCard"));
            yield return Frames(4);

            var compositors = rig.Go.GetComponentsInChildren<SpriteLayerCompositor>(true);
            Assert.AreEqual(1, compositors.Length);
            Assert.AreEqual(90f, Mathf.Abs(Mathf.DeltaAngle(0f, compositors[0].LayersRoot.localEulerAngles.z)), 0.5f, "the new card is tipped too");
        }

        [UnityTest]
        public IEnumerator TimeScaleZeroFreezesTheCues()
        {
            var rig = Spawn(settings: s => { FastSettings(s); s.hitSeconds = 0.5f; });
            yield return Frames(2);
            Time.timeScale = 0f;
            Publish(GameEvent.AfterDamage, rig.Id);
            yield return Frames(2);
            var frozen = rig.Compositor.Renderers[BodyIndex].color;
            yield return Frames(20);
            Assert.AreEqual(frozen, rig.Compositor.Renderers[BodyIndex].color);
            Time.timeScale = 1f;
        }

        // ---------------------------------------------------------------------------------------------
        // Deferred defeat (WorldSpawnService.MarkDefeated + IDefeatPresentation)
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MarkDefeatedWithCuesKeepsTheCorpseInertThenDestroysItAfterTheFadeAndHold()
        {
            var rig = Spawn("goblin", settings: s => { FastSettings(s); s.dieFadeSeconds = 0.4f; });
            var go = rig.Go;
            yield return Frames(2);
            Assert.IsTrue(CharacterAgent.AllLive.Contains(go.GetComponent<CharacterAgent>()));

            spawner.MarkDefeated(rig.Id);

            Assert.IsTrue(go != null, "still alive for the death cue");
            Assert.IsTrue(go.activeSelf);
            Assert.IsFalse(CharacterAgent.AllLive.Contains(go.GetComponent<CharacterAgent>()), "out of AllLive at once");
            Assert.IsFalse(go.GetComponent<BoxCollider>().enabled, "not clickable");
            Assert.IsFalse(go.GetComponent<GridCharacterMotor>().enabled);
            Assert.IsTrue(spawner.TryGetCharacterId("goblin", out _, out _, out var defeated));
            Assert.IsTrue(defeated, "tombstoned");
            Assert.IsTrue(rig.Cues.State.IsDead);

            yield return Seconds(0.15f);
            yield return Frames(2);
            Assert.IsTrue(go != null, "the hold has not fully elapsed, or the fade is still running");

            var waited = 0f;
            while (go != null && waited < 3f) { waited += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            yield return null;
            Assert.IsTrue(go == null, "destroyed once the fade and hold finish");
        }

        [UnityTest]
        public IEnumerator MarkDefeatedWithoutCuesDestroysImmediatelyAsBefore()
        {
            var rig = Spawn("goblin", withCues: false);
            var go = rig.Go;
            yield return Frames(2);

            spawner.MarkDefeated(rig.Id);
            yield return null;

            Assert.IsTrue(go == null);
        }

        private sealed class ThrowingDefeat : MonoBehaviour, IDefeatPresentation
        {
            public void BeginDefeat(Action onFinished) => throw new InvalidOperationException("boom");
        }

        [UnityTest]
        public IEnumerator AThrowingBeginDefeatFallsBackToTheImmediateDestroy()
        {
            var rig = Spawn("goblin", withCues: false);
            var go = rig.Go;
            go.AddComponent<ThrowingDefeat>();
            yield return Frames(2);

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            spawner.MarkDefeated(rig.Id);
            yield return null;

            Assert.IsTrue(go == null);
        }

        private sealed class FixedInitiativeRule : IInitiativeRule
        {
            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, 10, true) }, 0);
        }

        [UnityTest]
        public IEnumerator ACreatureDiedThroughARealEncounterControllerPlaysTheDeathCueBeforeTheViewIsDestroyed()
        {
            var action = TacticalTestFactory.CreateResourceDefinition("resource-action-cues", "Action");
            made.Add(action);
            var hero = Spawn("hero");
            var goblin = Spawn("goblin", settings: s => { FastSettings(s); s.dieFadeSeconds = 0.3f; });
            var positions = new TestPositionProvider();
            positions.Set(hero.Id, Vector3.zero);
            positions.Set(goblin.Id, Vector3.zero);
            var resolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus);
            var movement = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var encounter = new CombatEncounterService(session.Characters, new FixedInitiativeRule(), new TurnResourceSet(new[] { action }), session.EventBus);
            var heroes = new DefinitionId("faction-heroes-cues");
            var goblins = new DefinitionId("faction-goblins-cues");
            var controller = new TacticalEncounterController(
                session.Characters, session.EventBus, positions, resolution, movement, new NavMeshService(), encounter, spawner,
                new[] { (hero.Id, heroes), (goblin.Id, goblins) });
            yield return Frames(2);

            session.EventBus.Publish(new GameEventContext(GameEvent.CreatureDied, hero.Id, goblin.Id));

            Assert.IsTrue(goblin.Go != null, "the controller's synchronous MarkDefeated no longer destroys the view mid-publish");
            Assert.IsTrue(goblin.Cues.State.IsDead);
            Assert.IsTrue(hero.Go != null && hero.Go.activeSelf, "the other combatant is untouched");
            Assert.IsTrue(spawner.TryGetCharacterId("goblin", out _, out _, out var defeated));
            Assert.IsTrue(defeated);

            var waited = 0f;
            while (goblin.Go != null && waited < 3f) { waited += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            yield return null;
            Assert.IsTrue(goblin.Go == null, "destroyed after the fade and hold");
            controller.Dispose();
        }

        [UnityTest]
        public IEnumerator TheCorpsesGridCellIsFreedAtOnce()
        {
            var rig = Spawn("goblin");
            yield return Frames(2);
            var motor = rig.Go.GetComponent<GridCharacterMotor>();

            spawner.MarkDefeated(rig.Id);
            yield return null;

            Assert.IsFalse(motor.enabled, "GridCharacterMotor.OnDisable releases the cell it held");
        }
    }
}
