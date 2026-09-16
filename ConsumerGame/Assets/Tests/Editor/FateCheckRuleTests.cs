using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Consumer.Fate;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Consumer.Fate.Tests
{
    public class FateCheckRuleTests
    {
        private static ResourceDefinition CreateResource(string id)
        {
            var resource = UnityEngine.ScriptableObject.CreateInstance<ResourceDefinition>();
            SetField(resource, "id", id);
            return resource;
        }

        /// <summary>Private fields declared on a BASE type (e.g. DefinitionAsset.id) are invisible to a bare
        /// GetField call on the derived type -- must walk BaseType explicitly.</summary>
        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        [Test]
        public void CheckDieIsFourFudgeDiceExpressedAsFourD3MinusEight()
        {
            var rule = new FateCheckRule(new CharacterRegistry(), new Dictionary<string, ResourceDefinition>());

            Assert.AreEqual(4, rule.CheckDie.Count);
            Assert.AreEqual(3, rule.CheckDie.Sides);
            Assert.AreEqual(-8, rule.CheckDie.Modifier);
        }

        [Test]
        public void ComputeSkillCheckBonusReadsTheMappedResourcesMaxAsTheRating()
        {
            var notice = CreateResource("notice");
            var characters = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(notice.Id, max: 3));
            characters.Register(character);

            var rule = new FateCheckRule(characters, new Dictionary<string, ResourceDefinition> { ["Skill:Notice"] = notice });

            Assert.AreEqual(3, rule.ComputeSkillCheckBonus(character.Id, "Skill:Notice"));
        }

        [Test]
        public void ComputeSkillCheckBonusIsZeroForAnUnmappedQueryKeyOrUnregisteredCharacter()
        {
            var notice = CreateResource("notice");
            var characters = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(notice.Id, max: 3));
            characters.Register(character);

            var rule = new FateCheckRule(characters, new Dictionary<string, ResourceDefinition> { ["Skill:Notice"] = notice });

            Assert.AreEqual(0, rule.ComputeSkillCheckBonus(character.Id, "Skill:Will")); // not mapped
            Assert.AreEqual(0, rule.ComputeSkillCheckBonus(RuntimeEntityId.NewId(), "Skill:Notice")); // not registered
        }
    }
}
