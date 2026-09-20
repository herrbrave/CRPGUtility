using System;
using System.Collections.Generic;
using System.Linq;
using BarrowsEnd.Editor;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Validation;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using CRPG.Saving;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 2.7a: the gear, debug abilities and ruleset provider the gate scene needs. Everything runs against a REAL GameSession built
    /// from the provider through GameSessionHost.StartSession (the ruleset-aware path), so a missing registration fails here.
    /// </summary>
    public class GearAndRulesetContentTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");
        private static readonly DefinitionId Unconscious = new("condition-unconscious");

        private GameSession session;
        private GameObject hostObject;

        [OneTimeSetUp]
        public void Author()
        {
            BarrowsEndCatalogAuthor.Author();
            BarrowsEndGearAuthor.Author();
        }

        [SetUp]
        public void SetUp()
        {
            var provider = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            Assert.IsNotNull(provider, "the ruleset asset is authored");
            hostObject = new GameObject("GearTestsHost");
            session = hostObject.AddComponent<GameSessionHost>().StartSession(provider, new DiceService(new SeededRandomSource(7)), Array.Empty<Type>());
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
        }

        private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path);

        [Test]
        public void TheProviderRegistersSrd5PlusTheCampaignContent()
        {
            var provider = Load<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            Assert.IsNotNull(provider.Srd5);
            // 7 catalogs + head slot + (profile + item) per gear + the debug abilities
            var expected = BarrowsEndCatalogAuthor.Specs.Length + 1 + BarrowsEndGearAuthor.Gear.Length * 2 + BarrowsEndGearAuthor.DebugAbilities.Length;
            Assert.AreEqual(expected, provider.ExtraContent.Count);
            Assert.AreEqual(provider.ExtraContent.Count, provider.ExtraContent.Select(c => c.Id.Value).Distinct().Count(), "no duplicate ids");
            Assert.IsTrue(session.RulesDatabase.TryResolve<ResourceDefinition>(HitPoints, out _), "SRD5 content is still there");
            Assert.IsTrue(session.RulesDatabase.TryResolve<ConditionDefinition>(Unconscious, out _));
        }

        [Test]
        public void EveryGearItemSlotAndProfileResolvesThroughTheSession()
        {
            var rules = session.RulesDatabase;
            Assert.IsTrue(rules.TryResolve<EquipmentSlotDefinition>(new DefinitionId(BarrowsEndGearAuthor.HeadSlotId), out _), "SRD5 has no head slot: the campaign adds one");
            foreach (var gear in BarrowsEndGearAuthor.Gear)
            {
                Assert.IsTrue(rules.TryResolve<ItemDefinition>(new DefinitionId(gear.ItemId), out var item), gear.ItemId);
                Assert.AreEqual(gear.SlotId, item.EquipmentSlotId.Value);
                Assert.IsTrue(rules.TryResolve<EquipmentSlotDefinition>(item.EquipmentSlotId, out _), $"{gear.ItemId}'s slot resolves");
                Assert.IsTrue(rules.TryResolve<EquipmentVisualProfile>(item.EquipmentVisualProfileId, out var profile), $"{gear.ItemId}'s profile resolves");
                Assert.AreEqual(EquipmentVisualKind.Sprite, profile.Kind);
                Assert.AreEqual(1, profile.SpriteLayers.Count);
                Assert.AreEqual(gear.LayerSlot, profile.SpriteLayers[0].layerSlot.Value);
                Assert.IsNotNull(profile.SpriteLayers[0].sprite, $"{gear.ItemId} paints a sprite");
            }
        }

        [Test]
        public void EachProfilePaintsTheSpriteOfItsCatalogOptionAndTheHelmetHidesHair()
        {
            foreach (var gear in BarrowsEndGearAuthor.Gear)
            {
                var spec = BarrowsEndCatalogAuthor.Specs.First(s => s.ShortName == gear.CatalogShortName);
                var catalog = Load<SpriteLayerCatalog>(BarrowsEndCatalogAuthor.CatalogPath(spec));
                var option = catalog.Options.First(o => o.optionId.Value == gear.OptionId);
                var profile = Load<EquipmentVisualProfile>(BarrowsEndGearAuthor.ProfilePath(gear));
                Assert.AreSame(option.sprite, profile.SpriteLayers[0].sprite, gear.ItemId);
            }

            var helm = Load<EquipmentVisualProfile>(BarrowsEndGearAuthor.ProfilePath(BarrowsEndGearAuthor.Gear.First(g => g.ItemId == "item-iron-helm")));
            CollectionAssert.AreEqual(new[] { "layer-hair" }, helm.HiddenLayerSlots.Select(s => s.Value).ToArray());
        }

        [Test]
        public void TheSpriteProfileValidatorIsCleanOnEveryProfile()
        {
            var layerSlots = SpriteLayerCompositor.DefaultSlots();
            foreach (var gear in BarrowsEndGearAuthor.Gear)
            {
                var results = SpriteEquipmentProfileValidator.Validate(Load<EquipmentVisualProfile>(BarrowsEndGearAuthor.ProfilePath(gear)), BarrowsEndGearAuthor.ProfilePath(gear), layerSlots);
                Assert.IsEmpty(results.Where(r => r.Severity == ValidationSeverity.Error).Select(r => r.Message).ToList(), gear.ItemId);
            }
        }

        [Test]
        public void EquippingEveryPieceThroughARealEquipmentServiceWorksIncludingTheHeadSlot()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            session.Characters.Register(character);
            var service = new EquipmentService(session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);

            foreach (var gear in BarrowsEndGearAuthor.Gear)
            {
                var item = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId(gear.ItemId));
                session.Inventory.AddItem(item);
                Assert.DoesNotThrow(() => service.Equip(item.InstanceId, character.Id, new DefinitionId(gear.SlotId)), gear.ItemId);
            }
        }

        private SaveData SaveWithEquippedHelm()
        {
            var data = new SaveData();
            var characterId = RuntimeEntityId.NewId().ToString();
            data.Characters.Add(new CharacterSaveData { Id = characterId });
            data.InventoryItems.Add(new ItemSaveData
            {
                InstanceId = RuntimeEntityId.NewId().ToString(), DefinitionId = "item-iron-helm", Quantity = 1,
                EquippedCharacterId = characterId, EquippedSlotId = BarrowsEndGearAuthor.HeadSlotId,
            });
            return data;
        }

        [Test]
        public void ASaveWithAnEquippedHelmPassesTheLoadValidatorAgainstTheCampaignRulesetButNotAgainstBareSrd5()
        {
            Assert.IsTrue(ContentValidator.Validate(SaveWithEquippedHelm(), session.RulesDatabase).IsValid);

            // The hard 2.7a -> 2.7b dependency: without the head slot registered, every save with a helmet on fails to load.
            var bare = new RulesDatabase();
            foreach (var definition in Load<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath).Srd5.Content) bare.Register(definition);
            var extra = Load<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath).ExtraContent
                .Where(c => c.Id.Value != BarrowsEndGearAuthor.HeadSlotId);
            foreach (var definition in extra) bare.Register(definition);
            var result = ContentValidator.Validate(SaveWithEquippedHelm(), bare);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains(BarrowsEndGearAuthor.HeadSlotId, string.Join("\n", result.Errors));
        }

        // ------------------------------------------------------------------------------------------------
        // Debug abilities, run through the real resolution + damage + death-save path
        // ------------------------------------------------------------------------------------------------

        private CharacterState NewFighter(int hp = 20)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(HitPoints, hp));
            session.Characters.Register(character);
            return character;
        }

        private AbilityExecutionResult Cast(string abilityId, RuntimeEntityId source, RuntimeEntityId target)
        {
            var ability = Load<AbilityDefinition>($"{BarrowsEndGearAuthor.AbilityDir}/{abilityId}.asset");
            Assert.IsNotNull(ability);
            // Null faction provider and positions: the ability must not depend on either (the gate bootstrap passes neither).
            var resolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus, rulesDatabase: session.RulesDatabase);
            return resolution.Execute(source, ability, new[] { target });
        }

        [Test]
        public void DebugHitDealsThreeOrFourDamageToAnyoneWithoutCostsFactionsOrRange()
        {
            var attacker = NewFighter();
            var target = NewFighter();
            target.TryGetResource(HitPoints, out var hp);
            for (var i = 0; i < 6; i++)
            {
                var before = hp.Current;
                var result = Cast("ability-debug-hit", attacker.Id, target.Id);
                Assert.IsTrue(result.Succeeded, "the debug ability executes: " + result);
                var dealt = before - hp.Current;
                Assert.That(dealt, Is.InRange(3, 4));
                hp.Current = 20;
            }
        }

        private sealed class SameFactionProvider : IFactionProvider
        {
            public bool TryGetFaction(RuntimeEntityId entity, out DefinitionId faction) { faction = new DefinitionId("faction-party"); return true; }
        }

        [Test]
        public void DebugHitStillWorksBetweenAlliesIfABootstrapDoesPassAFactionProvider()
        {
            var attacker = NewFighter();
            var ally = NewFighter();
            var ability = Load<AbilityDefinition>($"{BarrowsEndGearAuthor.AbilityDir}/ability-debug-hit.asset");
            var resolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus,
                rulesDatabase: session.RulesDatabase, factionProvider: new SameFactionProvider());

            Assert.IsTrue(resolution.Execute(attacker.Id, ability, new[] { ally.Id }).Succeeded, "target relation Any: no WrongRelation between same-faction characters");
        }

        [Test]
        public void DebugHitCanTargetTheCasterItself()
        {
            var self = NewFighter();
            Assert.IsTrue(Cast("ability-debug-hit", self.Id, self.Id).Succeeded);
        }

        [Test]
        public void DebugSmashKillsOutrightThroughTheRealDeathSaveService()
        {
            var deathSaves = new DeathSaveService(session.Characters, session.DiceService, session.EventBus, HitPoints, Unconscious);
            var attacker = NewFighter();
            var target = NewFighter(hp: 20);
            var died = false;
            session.EventBus.Subscribe(GameEvent.CreatureDied, c => { if (c.Target == target.Id) died = true; });

            Assert.IsTrue(Cast("ability-debug-smash", attacker.Id, target.Id).Succeeded);

            Assert.IsTrue(died, "41-42 damage is massive damage against 20 max HP");
            Assert.IsNull(target.DeathSaves);
            GC.KeepAlive(deathSaves);
        }

        [Test]
        public void AnOrdinaryDebugHitDroppingToZeroStartsDeathSavesInsteadOfKilling()
        {
            var deathSaves = new DeathSaveService(session.Characters, session.DiceService, session.EventBus, HitPoints, Unconscious);
            var attacker = NewFighter();
            var target = NewFighter(hp: 3);
            target.TryGetResource(HitPoints, out var hp);
            var died = false;
            session.EventBus.Subscribe(GameEvent.CreatureDied, _ => died = true);

            Assert.IsTrue(Cast("ability-debug-hit", attacker.Id, target.Id).Succeeded);

            Assert.IsFalse(died);
            Assert.AreEqual(0, hp.Current);
            Assert.IsNotNull(target.DeathSaves);
            Assert.IsTrue(target.HasCondition(Unconscious));
            GC.KeepAlive(deathSaves);
        }

        // ------------------------------------------------------------------------------------------------
        // The author
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void AuthoringTwiceKeepsAssetGuidsAndDefinitionIdsAndAddsNothing()
        {
            var paths = BarrowsEndGearAuthor.Gear.SelectMany(g => new[] { BarrowsEndGearAuthor.ItemPath(g), BarrowsEndGearAuthor.ProfilePath(g) })
                .Concat(BarrowsEndGearAuthor.DebugAbilities.Select(BarrowsEndGearAuthor.AbilityPath))
                .Concat(new[] { BarrowsEndGearAuthor.SlotPath, BarrowsEndGearAuthor.RulesetPath }).ToList();
            var guids = paths.Select(AssetDatabase.AssetPathToGUID).ToList();
            var ids = BarrowsEndGearAuthor.Gear.Select(g => Load<ItemDefinition>(BarrowsEndGearAuthor.ItemPath(g)).Id.Value).ToList();
            var count = Load<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath).ExtraContent.Count;

            BarrowsEndGearAuthor.Author();

            CollectionAssert.AreEqual(guids, paths.Select(AssetDatabase.AssetPathToGUID).ToList());
            CollectionAssert.AreEqual(ids, BarrowsEndGearAuthor.Gear.Select(g => Load<ItemDefinition>(BarrowsEndGearAuthor.ItemPath(g)).Id.Value).ToList());
            Assert.AreEqual(count, Load<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath).ExtraContent.Count);
            Assert.AreEqual(paths.Count, paths.Count(p => Load<UnityEngine.Object>(p) != null), "every authored asset exists");
        }

        [Test]
        public void TheAuthoredEffectsSurviveAReloadFromDisk()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            var ability = Load<AbilityDefinition>($"{BarrowsEndGearAuthor.AbilityDir}/ability-debug-smash.asset");
            Assert.AreEqual(1, ability.Effects.Count);
            Assert.IsInstanceOf<DamageEffect>(ability.Effects[0], "a [SerializeReference] effect written by reflection persists");
            Assert.AreEqual(0, ability.Costs.Count);
        }
    }
}
