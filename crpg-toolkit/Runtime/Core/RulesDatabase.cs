using System;
using System.Collections.Generic;
using System.Linq;

namespace CRPG.Core
{
    /// <summary>
    /// Default <see cref="IRulesDatabase"/>: an in-memory registry of definitions keyed by their own
    /// <see cref="DefinitionId"/> (which is already globally unique, so no type qualifier is needed in the key).
    /// How definitions get discovered and registered -- an editor-time asset scan, Addressables, a
    /// RulesetDefinition aggregate -- is deliberately left to later phases; this class only owns the lookup.
    /// </summary>
    public sealed class RulesDatabase : IRulesDatabase
    {
        private readonly Dictionary<string, IDefinition> definitionsById = new();

        public void Register(IDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!definition.Id.IsValid) throw new ArgumentException($"{definition} has no assigned DefinitionId.", nameof(definition));

            if (definitionsById.TryGetValue(definition.Id.Value, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate DefinitionId '{definition.Id}': already registered by {existing}, now also claimed by {definition}.");
            }

            definitionsById[definition.Id.Value] = definition;
        }

        public void RegisterRange(IEnumerable<IDefinition> definitions)
        {
            foreach (var definition in definitions) Register(definition);
        }

        public bool TryResolve<TDefinition>(DefinitionId id, out TDefinition definition) where TDefinition : class, IDefinition
        {
            definition = null;
            if (!id.IsValid) return false;

            if (definitionsById.TryGetValue(id.Value, out var found) && found is TDefinition typed)
            {
                definition = typed;
                return true;
            }

            return false;
        }

        public TDefinition Resolve<TDefinition>(DefinitionId id) where TDefinition : class, IDefinition
        {
            if (TryResolve<TDefinition>(id, out var definition)) return definition;
            throw new KeyNotFoundException($"No {typeof(TDefinition).Name} is registered with id '{id}'.");
        }

        public IEnumerable<TDefinition> All<TDefinition>() where TDefinition : class, IDefinition
            => definitionsById.Values.OfType<TDefinition>();
    }
}
