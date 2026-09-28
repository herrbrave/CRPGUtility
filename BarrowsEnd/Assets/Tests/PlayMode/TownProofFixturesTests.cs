using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Shell;
using CRPG.Core;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.InputSystem;
using CRPG.Inventory;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.0's own end-to-end proof (plan item 7): the real Town's two proof-fixture NPCs
    /// (ProofFixture_TownCrier, ProofFixture_Peddler), driven through the REAL click stack (matching
    /// ShellTests.cs's own Click helper) against the REAL Core scene -- no StartingSceneOverride here on
    /// purpose, since exercising Town.unity as ShellBootstrap actually uses it is the whole point.</summary>
    public class TownProofFixturesTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private ShellBootstrap shell;
        private string savePath;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-town-proof-test-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty; // skip the title screen
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private static Vector3 Centre(int x, int z) => new(x + 0.5f, 0f, z + 0.5f);

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

        private static int GetListingQuantity(StoreInventoryState store, DefinitionId itemId)
        {
            store.TryGetListing(itemId, out var listing);
            return listing.Quantity;
        }

        [UnityTest]
        public IEnumerator TalkingToTheTownCrierRestoresAPartiallyDamagedPartysHpAndWritesARealSaveFile()
        {
            var session = shell.Services.Session;
            var memberId = session.Party.Members[0];
            session.Characters.TryGetCharacter(memberId, out var character);
            character.TryGetResource(BarrowsEnd.Gate.GateSessionServices.HitPoints, out var hp);
            var maxHp = hp.Max;
            hp.Current = Mathf.Max(1, maxHp - 3); // a partial hit -- never downs the member (RequestLongRestAction's own documented limit)
            Assert.Less(hp.Current, maxHp);

            Assert.IsFalse(File.Exists(savePath));
            Click(Centre(33, 20)); // ProofFixture_TownCrier's cell (BarrowsEndTownAuthor.AuthorProofFixtures)

            yield return WaitUntil(() =>
            {
                character.TryGetResource(BarrowsEnd.Gate.GateSessionServices.HitPoints, out var current);
                return current.Current == maxHp;
            }, 30f, "RequestLongRestAction restored HP through a real walk-and-talk interaction");

            yield return WaitUntil(() => File.Exists(savePath), 5f, "RequestSaveAction wrote a real save file");
        }

        [UnityTest]
        public IEnumerator TalkingToThePeddlerOpensARealStoreThatSurvivesASaveAndLoadRoundTrip()
        {
            var vendorAdapter = UnityEngine.Object.FindObjectsByType<TalkInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(a => a.name == "ProofFixture_Peddler");
            // Already bound by the time the shell is ready -- LocationInitializer.Prepare runs during Boot(),
            // well before IsReady flips true. This just confirms it, doesn't wait for the click to bind it.
            Assert.IsNotNull(vendorAdapter.Store, "LocationInitializer.PrepareStore already bound the StoreContentPoint at boot");

            Click(Centre(32, 20)); // ProofFixture_Peddler's cell -- opens the store view (OpenStoreAction)
            yield return null;

            var session = shell.Services.Session;
            var helmId = new DefinitionId("item-iron-helm");
            var goldId = new DefinitionId("item-gold");
            Assert.IsTrue(session.RulesDatabase.TryResolve<ItemDefinition>(goldId, out var gold), "the currency item is really registered in the ruleset");
            session.Inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), gold.Id, 100));

            var storeService = new StoreService(session.RulesDatabase, session.Inventory, gold.Id);
            Assert.IsTrue(storeService.TryBuy(vendorAdapter.Store, helmId, 1));
            var stockAfterBuy = GetListingQuantity(vendorAdapter.Store, helmId);
            Assert.Less(stockAfterBuy, 5, "the initial listing (5) really depleted");

            Assert.IsTrue(shell.SaveNow());

            LoadOutcome? outcome = null;
            shell.LoadNow(o => outcome = o);
            yield return WaitUntil(() => outcome.HasValue, 20f, "the load finished");
            Assert.AreEqual(LoadOutcome.Success, outcome.Value);

            Assert.IsNotNull(vendorAdapter.Store, "the SAME adapter instance is rebound after a load, not left dangling on the disposed session");
            Assert.AreEqual(stockAfterBuy, GetListingQuantity(vendorAdapter.Store, helmId), "depleted stock survives the load -- not reseeded");
        }
    }
}
