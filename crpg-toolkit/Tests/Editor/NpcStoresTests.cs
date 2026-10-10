using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Editor.Location;
using CRPG.Editor.Validation;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.Inventory;
using CRPG.Saving;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>Slice 1 of NPC services (<c>Docs/Plans/Toolkit-NpcServices.md</c>): the shop an NPC's spawn point authors, created in the session where stores already live, bound to the NPC's talk adapter, surviving a
    /// save/load in the right order; and the editor's rules for it.</summary>
    public sealed class NpcStoresTests
    {
        private sealed class Content : ILocationContent
        {
            public readonly List<NpcSpawnPoint> Points = new();
            public readonly List<StoreContentPoint> TalkStores = new();
            public IReadOnlyList<NpcSpawnPoint> SpawnPoints => Points;
            public IReadOnlyList<StoreContentPoint> Stores => TalkStores;
            public IReadOnlyList<LootContentPoint> LootContainers => new List<LootContentPoint>();
            public Rect? CameraBoundsXZ => null;
        }

        private readonly List<Object> toDestroy = new();
        private RulesDatabase rules;
        private GameSession session;
        private WorldSpawnService spawner;
        private ItemDefinition dagger, potion;
        private PanelSettings panel;

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase();
            dagger = Item("item-dagger", "Dagger", 2f);
            potion = Item("item-potion", "Healing Potion", 50f);
            rules.Register(dagger);
            rules.Register(potion);
            session = new GameSession(rules, new DiceService(new SeededRandomSource(1)));
            spawner = new WorldSpawnService(session);
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            toDestroy.Add(panel);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in toDestroy) if (o != null && !EditorUtility.IsPersistent(o)) Object.DestroyImmediate(o);
            toDestroy.Clear();
            session.Dispose();
        }

        private ItemDefinition Item(string id, string name, float value)
        {
            var item = TestDefinitionFactory.CreateItemDefinition(id, name, baseValue: value);
            toDestroy.Add(item);
            return item;
        }

        private static NpcStoreSpec Shop(params (ItemDefinition item, int quantity)[] stock)
        {
            var spec = new NpcStoreSpec();
            foreach (var (item, quantity) in stock) spec.Listings.Add(new StoreListingSeed { Item = item, Quantity = quantity });
            return spec;
        }

        private NpcSpawnPoint Point(string spawnId, NpcStoreSpec store, DialogueDefinition dialogue = null, bool spawn = false)
        {
            var definition = TestDefinitionFactory.CreateNpcDefinition($"npc-{spawnId}", spawnId);
            var visual = new GameObject($"{spawnId}-visual");
            toDestroy.Add(visual);
            TestDefinitionFactory.SetPrivateField(definition, "visualPrefab", visual);
            toDestroy.Add(definition);
            var anchor = new GameObject($"{spawnId}-anchor");
            toDestroy.Add(anchor);
            if (spawn) toDestroy.Add(spawner.SpawnNpc(spawnId, definition, Vector3.zero, Quaternion.identity).gameObject);
            return new NpcSpawnPoint { SpawnId = spawnId, Definition = definition, Anchor = anchor.transform, Store = store, Dialogue = dialogue };
        }

        private DialogueDefinition ShopDialogue(bool opensStore = true)
        {
            var nodes = new List<DialogueNode>();
            var end = DialogueTestFactory.CreateEnd("end");
            if (opensStore)
            {
                var open = new ActionNode();
                TestDefinitionFactory.SetPrivateField(open, "nodeId", SerializableGuid.FromString("open"));
                TestDefinitionFactory.SetPrivateField(open, "action", new OpenStoreAction());
                TestDefinitionFactory.SetPrivateField(open, "nextNodeId", SerializableGuid.FromString("end"));
                nodes.Add(open);
            }
            else nodes.Add(DialogueTestFactory.CreateLine("start", "Hild", "Hello.", "end"));

            nodes.Add(end);
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            toDestroy.Add(dialogue);
            TestDefinitionFactory.SetPrivateField(dialogue, "id", "dlg-shop");
            TestDefinitionFactory.SetPrivateField(dialogue, "startNodeId", nodes[0].NodeId);
            TestDefinitionFactory.SetPrivateField(dialogue, "nodes", nodes);
            return dialogue;
        }

        // ---------------------------------------------------------------- creation

        [Test]
        public void Ensure_CreatesTheShopOnceWithTheAuthoredStockAndMultipliers()
        {
            var spec = Shop((dagger, -1), (potion, 3));
            spec.BuyMultiplier = 1.5f;
            spec.SellMultiplier = 0.25f;
            var point = Point("hild", spec);

            var store = NpcStores.Ensure(point, session);

            Assert.IsNotNull(store);
            Assert.AreEqual(2, store.Listings.Count);
            Assert.AreEqual(1.5f, store.BuyPriceMultiplier, "set inside the seed: GetOrCreateStore builds with the defaults");
            Assert.AreEqual(0.25f, store.SellPriceMultiplier);
            Assert.IsTrue(store.TryGetListing(potion.Id, out var listing) && listing.Quantity == 3);
            Assert.AreSame(session.Stores["hild"], store, "keyed by the spawn id when no StoreId is authored");
        }

        [Test]
        public void Ensure_IsIdempotent_AndAnExistingShopKeepsItsDepletedStock()
        {
            var point = Point("hild", Shop((potion, 3)));
            var store = NpcStores.Ensure(point, session);
            store.TryGetListing(potion.Id, out var listing);
            listing.Quantity = 1;   // the party bought two

            var again = NpcStores.Ensure(point, session);

            Assert.AreSame(store, again);
            Assert.AreEqual(1, again.Listings[0].Quantity, "not reseeded");
            Assert.AreEqual(1, session.Stores.Count);
        }

        [Test]
        public void Ensure_UsesTheAuthoredStoreId_WhenThereIsOne()
        {
            var spec = Shop((dagger, -1));
            spec.StoreId = "village-smithy";
            NpcStores.Ensure(Point("orm", spec), session);

            Assert.IsTrue(session.Stores.ContainsKey("village-smithy"));
            Assert.IsFalse(session.Stores.ContainsKey("orm"));
        }

        [Test]
        public void Ensure_NoShop_NoStore_AndNullsAreSafe()
        {
            Assert.IsNull(NpcStores.Ensure(Point("mute", new NpcStoreSpec()), session), "no listings, no shop");
            Assert.IsNull(NpcStores.Ensure(Point("mute2", null), session));
            Assert.IsNull(NpcStores.Ensure(null, session));
            Assert.IsNull(NpcStores.Ensure(Point("x", Shop((dagger, 1))), null));
            Assert.AreEqual(0, session.Stores.Count);
        }

        [Test]
        public void Ensure_LeavesOutAnItemTheRulesetCannotResolve_WithAWarning_SoTheShopScreenCannotThrow()
        {
            var stranger = Item("item-stranger", "Stranger", 5f);   // created, but never registered in the session's rules
            var point = Point("hild", Shop((dagger, -1), (stranger, -1)));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not in this session's ruleset"));
            var store = NpcStores.Ensure(point, session);

            Assert.AreEqual(1, store.Listings.Count);
            Assert.AreEqual(dagger.Id, store.Listings[0].ItemId);
        }

        [Test]
        public void Ensure_SkipsAListingWithNoItem()
        {
            var spec = Shop((dagger, -1));
            spec.Listings.Add(new StoreListingSeed { Item = null, Quantity = 2 });
            spec.Listings.Add(null);

            Assert.AreEqual(1, NpcStores.Ensure(Point("hild", spec), session).Listings.Count);
        }

        // ---------------------------------------------------------------- wiring

        private NpcDialogueOptions Options(bool withCurrency = true) => new()
        {
            PanelSettings = panel,
            CurrencyItemId = withCurrency ? new DefinitionId("item-gold") : DefinitionId.None,
        };

        [Test]
        public void AttachAll_BindsTheShopToTheNpcsAdapter_TheSameInstanceTheSessionHolds()
        {
            var content = new Content();
            content.Points.Add(Point("hild", Shop((dagger, -1)), ShopDialogue(), spawn: true));

            NpcDialogues.AttachAll(content, spawner, session, Options());

            spawner.TryGetCharacterId("hild", out var id, out _, out _);
            var adapter = spawner.OwnedAgents.First(a => a.EntityId == id).GetComponent<TalkInteractionAdapter>();
            Assert.IsNotNull(adapter.Store);
            Assert.AreSame(session.Stores["hild"], adapter.Store);
            Assert.AreEqual(new DefinitionId("item-gold"), adapter.CurrencyItemId);
        }

        [Test]
        public void AttachAll_RebindsTheSameShopOnEveryCall_AndClearsItWhenTheSpawnPointNoLongerHasOne()
        {
            var content = new Content();
            var point = Point("hild", Shop((dagger, -1)), ShopDialogue(), spawn: true);
            content.Points.Add(point);
            NpcDialogues.AttachAll(content, spawner, session, Options());
            spawner.TryGetCharacterId("hild", out var id, out _, out _);
            var adapter = spawner.OwnedAgents.First(a => a.EntityId == id).GetComponent<TalkInteractionAdapter>();
            var first = adapter.Store;

            NpcDialogues.AttachAll(content, spawner, session, Options());
            Assert.AreSame(first, adapter.Store, "re-wired to the same store, not a new one");

            point.Store = new NpcStoreSpec();
            NpcDialogues.AttachAll(content, spawner, session, Options());
            Assert.IsNull(adapter.Store, "no shop authored any more");
        }

        [Test]
        public void AttachAll_WarnsWhenAShopHasNoCurrency_BecauseEveryPurchaseWouldFail()
        {
            var content = new Content();
            content.Points.Add(Point("hild", Shop((dagger, -1)), ShopDialogue(), spawn: true));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("CurrencyItemId is not set"));
            NpcDialogues.AttachAll(content, spawner, session, Options(withCurrency: false));
        }

        [Test]
        public void AnNpcWithNoShop_GetsNoStoreOnItsAdapter()
        {
            var content = new Content();
            content.Points.Add(Point("mute", new NpcStoreSpec(), ShopDialogue(opensStore: false), spawn: true));

            NpcDialogues.AttachAll(content, spawner, session, Options());

            spawner.TryGetCharacterId("mute", out var id, out _, out _);
            Assert.IsNull(spawner.OwnedAgents.First(a => a.EntityId == id).GetComponent<TalkInteractionAdapter>().Store);
        }

        // ---------------------------------------------------------------- load order

        [Test]
        public void ADepletedShopSurvivesASaveAndLoad_WhenTheLocationRebindsBeforeTheStoresAreRestored()
        {
            var content = new Content();
            content.Points.Add(Point("hild", Shop((potion, 3)), ShopDialogue()));
            LocationInitializer.RebindStoresAndLoot(content, session);
            session.Stores["hild"].TryGetListing(potion.Id, out var listing);
            listing.Quantity = 1;
            var saved = SaveService.Capture(session.Party, session.Characters, session.Inventory, session.Timeline, session.Quests, stores: session.Stores);

            var loaded = new GameSession(rules, new DiceService(new SeededRandomSource(2)));
            try
            {
                LocationInitializer.RebindStoresAndLoot(content, loaded);   // what a game does after a load, exactly as it does for store talk points
                SaveService.RestoreStores(saved, loaded.Stores);

                loaded.Stores["hild"].TryGetListing(potion.Id, out var restored);
                Assert.AreEqual(1, restored.Quantity, "the stock the party left, not the seed");
            }
            finally
            {
                loaded.Dispose();
            }
        }

        [Test]
        public void RebindStoresAndLoot_CreatesTheNpcShopsToo()
        {
            var content = new Content();
            content.Points.Add(Point("hild", Shop((dagger, -1))));

            LocationInitializer.RebindStoresAndLoot(content, session);

            Assert.IsTrue(session.Stores.ContainsKey("hild"));
        }

        // ---------------------------------------------------------------- editor: placement + validator

        [Test]
        public void ANewSpawnPointNeverInheritsThePreviousNpcsShop()
        {
            var host = new GameObject("Location");
            toDestroy.Add(host);
            var location = host.AddComponent<LocationContent>();
            var npc = ScriptableObject.CreateInstance<NpcDefinition>();
            toDestroy.Add(npc);
            var so0 = new SerializedObject(npc);
            so0.FindProperty("id").stringValue = "npc-a";
            so0.FindProperty("visualPrefab").objectReferenceValue = new GameObject("v");
            so0.ApplyModifiedPropertiesWithoutUndo();

            LocationPlacement.AddSpawn(location, npc, Vector3.zero, Quaternion.identity, "a");
            location.SpawnPoints[0].Store = Shop((dagger, 4));
            location.SpawnPoints[0].Store.StoreId = "custom";
            location.SpawnPoints[0].Store.BuyMultiplier = 3f;
            LocationPlacement.AddSpawn(location, npc, new Vector3(5f, 0f, 0f), Quaternion.identity, "b");
            foreach (var p in location.SpawnPoints) if (p.Anchor != null && p.Anchor.parent != null) toDestroy.Add(p.Anchor.parent.gameObject);

            Assert.IsTrue(location.SpawnPoints[0].Store.HasStore);
            var second = location.SpawnPoints[1].Store;
            Assert.IsFalse(second.HasStore);
            Assert.IsTrue(string.IsNullOrEmpty(second.StoreId));
            Assert.AreEqual(1f, second.BuyMultiplier);
            Assert.AreEqual(0.5f, second.SellMultiplier);
        }

        private LocationContent LocationWith(params NpcSpawnPoint[] points)
        {
            var host = new GameObject("Location");
            toDestroy.Add(host);
            var location = host.AddComponent<LocationContent>();
            var so = new SerializedObject(location);
            var list = so.FindProperty("spawnPoints");
            list.arraySize = points.Length;
            so.ApplyModifiedPropertiesWithoutUndo();
            for (var i = 0; i < points.Length; i++) { var p = location.SpawnPoints[i]; p.SpawnId = points[i].SpawnId; p.Definition = points[i].Definition; p.Anchor = points[i].Anchor; p.Dialogue = points[i].Dialogue; p.Store = points[i].Store; }
            return location;
        }

        private static IEnumerable<ValidationResult> StoreFindings(LocationContent location) =>
            LocationValidator.Validate(location, null).Where(r => r.Field.EndsWith(".Store"));

        [Test]
        public void Validator_ACleanShopWithAnOpeningDialogue_IsSilent()
        {
            var location = LocationWith(Point("hild", Shop((dagger, -1), (potion, 5)), ShopDialogue()));
            Assert.IsEmpty(StoreFindings(location).ToList());
        }

        [Test]
        public void Validator_AListingWithNoItem_IsAnError_AFreeItemAndSoldOutStockAreWarnings()
        {
            var free = Item("item-free", "Free", 0f);
            var location = LocationWith(Point("hild", Shop((free, -1), (dagger, 0)), ShopDialogue()));
            location.SpawnPoints[0].Store.Listings.Add(new StoreListingSeed { Item = null });

            var findings = StoreFindings(location).ToList();

            Assert.IsTrue(findings.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("has no item")));
            Assert.IsTrue(findings.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("worth nothing")));
            Assert.IsTrue(findings.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("sold out")));
        }

        [Test]
        public void Validator_TwoShopsWithTheSameId_OrOneThatCollidesWithAStoreTalkPoint_IsAnError()
        {
            var a = Point("a", Shop((dagger, -1)), ShopDialogue());
            var b = Point("b", Shop((dagger, -1)), ShopDialogue());
            b.Store.StoreId = "a";   // collides with a's default id
            var location = LocationWith(a, b);
            Assert.IsTrue(StoreFindings(location).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("shares one stock") || r.Message.Contains("share one stock")));

            var c = Point("c", Shop((dagger, -1)), ShopDialogue());
            var location2 = LocationWith(c);
            var so = new SerializedObject(location2);
            so.FindProperty("stores").arraySize = 1;
            so.FindProperty("stores").GetArrayElementAtIndex(0).FindPropertyRelative("StoreId").stringValue = "c";
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(StoreFindings(location2).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("store talk point")), "ids are shared with the store talk points");
        }

        [Test]
        public void Validator_AShopNobodyCanReach_IsAWarning()
        {
            var noDialogue = LocationWith(Point("hild", Shop((dagger, -1))));
            Assert.IsTrue(StoreFindings(noDialogue).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("no dialogue")));

            var neverOpens = LocationWith(Point("hild", Shop((dagger, -1)), ShopDialogue(opensStore: false)));
            Assert.IsTrue(StoreFindings(neverOpens).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("never opens")));
        }

        [Test]
        public void Validator_AnOpenStoreActionIsFine_WhenTheSpawnPointHasAShop_AndAnErrorWhenItDoesNot()
        {
            var withShop = LocationWith(Point("hild", Shop((dagger, -1)), ShopDialogue()));
            Assert.IsFalse(LocationValidator.Validate(withShop, null).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("opens a store")));

            var without = LocationWith(Point("hild", new NpcStoreSpec(), ShopDialogue()));
            Assert.IsTrue(LocationValidator.Validate(without, null).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("opens a store")));
        }

        [Test]
        public void Drawer_ShowsAStoreField()
        {
            var host = new GameObject("Location");
            toDestroy.Add(host);
            var location = host.AddComponent<LocationContent>();
            var so0 = new SerializedObject(location);
            so0.FindProperty("spawnPoints").arraySize = 1;
            so0.ApplyModifiedPropertiesWithoutUndo();

            var element = new NpcSpawnPointDrawer().CreatePropertyGUI(new SerializedObject(location).FindProperty("spawnPoints").GetArrayElementAtIndex(0));

            Assert.IsNotNull(element.Q("spawn-store-field"));
        }
    }
}
