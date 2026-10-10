using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEditor;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>Loads the authored Viking Raid ruleset exactly as a game would (SRD5 content + the game's extra content) into a RulesDatabase, and
    /// spawns combatants through the REAL <see cref="NpcSpawnService"/>.</summary>
    public sealed class ContentFixture
    {
        public const string RulesetPath = "Assets/Content/Ruleset/VikingRaidRuleset.asset";
        public static readonly DefinitionId DexterityId = new(GameDesign.Dex);

        public VikingRaidRulesetProvider Provider { get; private set; }
        public RulesDatabase Db { get; } = new();
        public Dictionary<string, NpcDefinition> Npcs { get; } = new();

        public static ContentFixture Load()
        {
            var provider = AssetDatabase.LoadAssetAtPath<VikingRaidRulesetProvider>(RulesetPath);
            Assert.IsNotNull(provider, $"{RulesetPath} missing -- run 'Viking Raid/Task 2/Author Content'.");
            Assert.IsNotNull(provider.Srd5, "ruleset provider has no SRD5 reference");

            var fixture = new ContentFixture { Provider = provider };
            fixture.Db.RegisterRange(provider.Srd5.Content);      // throws on a duplicate id: the "ids unique" test
            fixture.Db.RegisterRange(provider.ExtraContent);
            foreach (var npc in provider.ExtraContent.OfType<NpcDefinition>()) fixture.Npcs[npc.Id.Value] = npc;
            return fixture;
        }

        public CharacterState Spawn(CharacterRegistry characters, string unitId)
        {
            var character = NpcSpawnService.Spawn(Npcs["npc-vr-" + unitId], Db, RuntimeEntityId.NewId());
            characters.Register(character);
            return character;
        }

        public T Resolve<T>(string id) where T : class, IDefinition
        {
            Assert.IsTrue(Db.TryResolve<T>(new DefinitionId(id), out var definition), $"'{id}' does not resolve as {typeof(T).Name}");
            return definition;
        }

        public DefaultAttackBonusRule AttackRule(ICharacterRegistry characters) => new(characters, Db);
        public DefaultArmorClassRule ArmorRule(ICharacterRegistry characters) => new(characters, Db, DexterityId);

        /// <summary>Reads a private serialized field (the authored ability/effect types expose no public setters, and some no getters).</summary>
        public static TField Field<TField>(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) return (TField)field.GetValue(target);
                type = type.BaseType;
            }

            throw new System.Exception($"no field '{name}' on {target.GetType().Name}");
        }
    }
}
