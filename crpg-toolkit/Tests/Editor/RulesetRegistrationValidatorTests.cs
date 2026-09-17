using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Validation;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class RulesetRegistrationValidatorTests
    {
        private sealed class FakeDefinition : IDefinition
        {
            public FakeDefinition(string id) => Id = new DefinitionId(id);
            public DefinitionId Id { get; }
        }

        private sealed class CleanRulesetProvider : IRulesetDefinition
        {
            public string RulesetId => "test-clean";
            public void Register(IRulesetRegistrationBuilder builder) =>
                builder.RegisterDefinitions(new IDefinition[] { new FakeDefinition("clean-def-a"), new FakeDefinition("clean-def-b") });
        }

        private sealed class DuplicateIdRulesetProvider : IRulesetDefinition
        {
            public string RulesetId => "test-duplicate";
            public void Register(IRulesetRegistrationBuilder builder) =>
                builder.RegisterDefinitions(new IDefinition[] { new FakeDefinition("dup-id"), new FakeDefinition("dup-id") });
        }

        private sealed class ThrowingRulesetProvider : IRulesetDefinition
        {
            public string RulesetId => "test-throwing";
            public void Register(IRulesetRegistrationBuilder builder) => throw new InvalidOperationException("deliberately broken provider");
        }

        [Test]
        public void ACleanProviderProducesNoResults()
        {
            var results = RulesetRegistrationValidator.Validate(new CleanRulesetProvider(), "Assets/Fake/Clean.asset");
            Assert.IsEmpty(results);
        }

        [Test]
        public void ADuplicateIdWithinTheProvidersOwnContentIsReportedAsARealError()
        {
            var results = RulesetRegistrationValidator.Validate(new DuplicateIdRulesetProvider(), "Assets/Fake/Duplicate.asset");

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Error, results[0].Severity);
            Assert.AreEqual("Assets/Fake/Duplicate.asset", results[0].AssetPath);
            StringAssert.Contains("dup-id", results[0].Message);
        }

        [Test]
        public void AProviderWhoseRegisterMethodThrowsDirectlyIsCaughtAndReportedNotLeftToCrash()
        {
            var results = RulesetRegistrationValidator.Validate(new ThrowingRulesetProvider(), "Assets/Fake/Throwing.asset");

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Error, results[0].Severity);
            StringAssert.Contains("deliberately broken provider", results[0].Message);
        }

        [Test]
        public void ABrokenProviderDoesNotPoisonACleanProviderCheckedRightAfter()
        {
            var brokenResults = RulesetRegistrationValidator.Validate(new DuplicateIdRulesetProvider(), "Assets/Fake/Duplicate.asset");
            var cleanResults = RulesetRegistrationValidator.Validate(new CleanRulesetProvider(), "Assets/Fake/Clean.asset");

            Assert.AreEqual(1, brokenResults.Count);
            Assert.IsEmpty(cleanResults, "A prior broken provider must not leave any state that makes a later clean provider report false errors.");
        }

        [Test]
        public void RunningTheSameProviderTwiceLeavesNoLeakedGameObjects()
        {
            var before = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length;

            RulesetRegistrationValidator.Validate(new CleanRulesetProvider(), "Assets/Fake/Clean.asset");
            RulesetRegistrationValidator.Validate(new CleanRulesetProvider(), "Assets/Fake/Clean.asset");

            var after = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length;
            Assert.AreEqual(before, after, "Each Validate() call must destroy its own temporary GameObject -- running it repeatedly must not accumulate scene objects.");
        }
    }
}
