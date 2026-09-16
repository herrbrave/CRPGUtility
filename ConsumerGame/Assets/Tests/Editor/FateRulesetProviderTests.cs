using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CRPG.Actors;
using CRPG.Consumer.Fate;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Consumer.Fate.Tests
{
    /// <summary>
    /// FateRulesetProvider's registration, and -- the actual point of Gate D's Gap 1 package fix -- that a
    /// real consumer ruleset genuinely survives a save/load round trip through
    /// GameSessionHost.StartSession/SessionLoadCoordinator.Load's ruleset-aware overloads. Builds its own
    /// minimal FateRulesetProvider via reflection rather than loading the authored asset, so this test doesn't
    /// drift if the authored content changes.
    /// </summary>
    public class FateRulesetProviderTests
    {
        private string tempDir;
        private string savePath;
        private GameObject hostGo;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "consumer-fate-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            savePath = Path.Combine(tempDir, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }

        /// <summary>Private fields declared on a BASE type (e.g. DefinitionAsset.id) are invisible to a bare
        /// GetField call on the derived type -- must walk BaseType explicitly.</summary>
        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        private static FateRulesetProvider CreateMinimalProvider()
        {
            var notice = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetField(notice, "id", "resource-fate-test-notice");

            var provider = ScriptableObject.CreateInstance<FateRulesetProvider>();
            SetField(provider, "content", new List<DefinitionAsset> { notice });

            object boxedSkill = new FateRulesetProvider.SkillEntry();
            SetField(boxedSkill, "queryKey", "Skill:Notice");
            SetField(boxedSkill, "resource", notice);
            SetField(provider, "skills", new List<FateRulesetProvider.SkillEntry> { (FateRulesetProvider.SkillEntry)boxedSkill });

            return provider;
        }

        [Test]
        public void RegisterPublishesTheSkillCheckPolicyAndItsContent()
        {
            hostGo = new GameObject("FateHost");
            var host = hostGo.AddComponent<GameSessionHost>();
            var provider = CreateMinimalProvider();

            var session = host.StartSession(provider, new DiceService(new SeededRandomSource(1)), new[] { typeof(ISkillCheckRule) });

            Assert.IsTrue(session.Policies.TryGet<ISkillCheckRule>(out var rule));
            Assert.AreEqual(4, rule.CheckDie.Count);
            Assert.IsTrue(session.RulesDatabase.TryResolve<ResourceDefinition>(new DefinitionId("resource-fate-test-notice"), out _));
        }

        [Test]
        public void ARealSaveLoadRoundTripKeepsTheFateSkillCheckRuleRegistered()
        {
            hostGo = new GameObject("FateHost");
            var host = hostGo.AddComponent<GameSessionHost>();
            var provider = CreateMinimalProvider();
            var session = host.StartSession(provider, new DiceService(new SeededRandomSource(1)), new[] { typeof(ISkillCheckRule) });
            var spawner = new WorldSpawnService(session);

            var saveResult = new SaveRequestService(session, spawner, new SafePointGate(), rulesetId: provider.RulesetId).RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome);

            var coordinator = new SessionLoadCoordinator();
            var result = coordinator.Load(savePath, provider, new DiceService(new SeededRandomSource(2)), new[] { typeof(ISkillCheckRule) }, host, spawner);

            Assert.AreEqual(LoadOutcome.Success, result.Outcome, result.Message);
            Assert.IsTrue(result.Session.Policies.TryGet<ISkillCheckRule>(out _), "The Fate check rule must still be registered after a real save/load round trip.");
        }
    }
}
