using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    public sealed class CharacterRegistry : ICharacterRegistry
    {
        private readonly Dictionary<RuntimeEntityId, CharacterState> charactersById = new();
        private readonly IRulesDatabase rulesDatabase;

        public CharacterRegistry(IRulesDatabase rulesDatabase = null) => this.rulesDatabase = rulesDatabase;

        public void Register(CharacterState character)
        {
            if (rulesDatabase != null) UniversalFeatures.Grant(character, rulesDatabase);
            charactersById[character.Id] = character;
        }
        public void Unregister(RuntimeEntityId id) => charactersById.Remove(id);
        public bool TryGetCharacter(RuntimeEntityId id, out CharacterState character) => charactersById.TryGetValue(id, out character);
    }
}
