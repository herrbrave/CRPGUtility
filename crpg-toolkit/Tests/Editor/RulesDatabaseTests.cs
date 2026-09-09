using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class RulesDatabaseTests
    {
        private sealed class FakeDefinition : IDefinition
        {
            public FakeDefinition(string id) => Id = new DefinitionId(id);
            public DefinitionId Id { get; }
        }

        [Test]
        public void RegisteredDefinitionResolvesById()
        {
            var database = new RulesDatabase();
            var definition = new FakeDefinition("stat-strength");
            database.Register(definition);

            var resolved = database.Resolve<FakeDefinition>(definition.Id);

            Assert.AreSame(definition, resolved);
        }

        [Test]
        public void TryResolveFailsForUnknownId()
        {
            var database = new RulesDatabase();

            var found = database.TryResolve<FakeDefinition>(new DefinitionId("does-not-exist"), out var definition);

            Assert.IsFalse(found);
            Assert.IsNull(definition);
        }

        [Test]
        public void RegisteringDuplicateIdThrows()
        {
            var database = new RulesDatabase();
            database.Register(new FakeDefinition("stat-strength"));

            Assert.Throws<System.InvalidOperationException>(() => database.Register(new FakeDefinition("stat-strength")));
        }

        [Test]
        public void AllReturnsEveryRegisteredDefinitionOfType()
        {
            var database = new RulesDatabase();
            database.Register(new FakeDefinition("stat-strength"));
            database.Register(new FakeDefinition("stat-dexterity"));

            var all = database.All<FakeDefinition>();

            Assert.AreEqual(2, System.Linq.Enumerable.Count(all));
        }
    }
}
