using System;
using UnityEngine;

namespace CRPG.Saving
{
    /// <summary>
    /// Plain data SaveService.Capture takes as INPUT for one world entity's placement -- deliberately has zero
    /// reference to CRPG.Gameplay/WorldSpawnService/CharacterAgent (Task 3.5: CRPG.Saving must never reference
    /// CRPG.Gameplay, or CRPG.Gameplay referencing CRPG.Saving for SaveService/SaveFileStore/
    /// SessionLoadCoordinator would create an assembly cycle). CRPG.Gameplay's WorldSpawnService assembles
    /// these from its own spawn records + live CharacterAgent transforms and hands them to SaveService --
    /// SaveService itself never touches a GameObject.
    /// <para>CharacterId is always populated (party member or NPC) and links back to a CharacterSaveData.Id in
    /// the same save. SpawnId is always non-empty (WorldSpawnService keys every record by it) -- for a party
    /// member, the caller conventionally uses the character's own id as its spawn id, since there's no separate
    /// authored placement identity to give it. NpcDefinitionId is empty for a party member -- its view is
    /// rebuilt directly from a caller-supplied prefab on load, not respawned from a definition, and it has no
    /// defeat semantics.</para>
    /// </summary>
    public readonly struct WorldSpawnSnapshot
    {
        public WorldSpawnSnapshot(string spawnId, string characterId, string npcDefinitionId, Vector3 position, float facingYaw, bool defeated)
        {
            SpawnId = spawnId;
            CharacterId = characterId;
            NpcDefinitionId = npcDefinitionId;
            Position = position;
            FacingYaw = facingYaw;
            Defeated = defeated;
        }

        public string SpawnId { get; }
        public string CharacterId { get; }
        public string NpcDefinitionId { get; }
        public Vector3 Position { get; }
        public float FacingYaw { get; }
        public bool Defeated { get; }
    }

    [Serializable]
    public sealed class WorldSpawnSaveEntry
    {
        public string SpawnId;
        public string CharacterId;
        public string NpcDefinitionId;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float FacingYaw;
        public bool Defeated;
    }
}
