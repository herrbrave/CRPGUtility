using System;
using System.Collections.Generic;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class PolicyRegistryTests
    {
        private interface IFakePolicy
        {
        }

        private sealed class FakePolicy : IFakePolicy
        {
        }

        private interface IOtherFakePolicy
        {
        }

        [Test]
        public void RegisteredPolicyResolvesByInterface()
        {
            var registry = new PolicyRegistry();
            var policy = new FakePolicy();

            registry.Register<IFakePolicy>(policy);

            Assert.AreSame(policy, registry.Get<IFakePolicy>());
        }

        [Test]
        public void TryGetFailsForUnregisteredPolicy()
        {
            var registry = new PolicyRegistry();

            var found = registry.TryGet<IFakePolicy>(out var policy);

            Assert.IsFalse(found);
            Assert.IsNull(policy);
        }

        [Test]
        public void GetThrowsForUnregisteredPolicy()
        {
            var registry = new PolicyRegistry();

            Assert.Throws<InvalidOperationException>(() => registry.Get<IFakePolicy>());
        }

        [Test]
        public void RegisteringDuplicatePolicyThrows()
        {
            var registry = new PolicyRegistry();
            registry.Register<IFakePolicy>(new FakePolicy());

            Assert.Throws<InvalidOperationException>(() => registry.Register<IFakePolicy>(new FakePolicy()));
        }

        [Test]
        public void ValidateRequiredSucceedsWhenEveryTypeIsRegistered()
        {
            var registry = new PolicyRegistry();
            registry.Register<IFakePolicy>(new FakePolicy());

            Assert.DoesNotThrow(() => registry.ValidateRequired(new List<Type> { typeof(IFakePolicy) }));
        }

        [Test]
        public void ValidateRequiredThrowsNamingEveryMissingTypeAtOnce()
        {
            var registry = new PolicyRegistry();

            var ex = Assert.Throws<RulesetValidationException>(() =>
                registry.ValidateRequired(new List<Type> { typeof(IFakePolicy), typeof(IOtherFakePolicy) }));

            StringAssert.Contains(nameof(IFakePolicy), ex.Message);
            StringAssert.Contains(nameof(IOtherFakePolicy), ex.Message);
        }

        [Test]
        public void ValidateRequiredWithNoRequiredTypesNeverThrows()
        {
            var registry = new PolicyRegistry();

            Assert.DoesNotThrow(() => registry.ValidateRequired(null));
            Assert.DoesNotThrow(() => registry.ValidateRequired(new List<Type>()));
        }
    }
}
