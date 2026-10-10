using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Gameplay.Ambient
{
    /// <summary>
    /// "Please leave this NPC alone": the owners (an interaction walking up to it, a conversation, a menu, a fight)
    /// that currently need an NPC's ambient routine to stop. An NPC is suspended while any owner holds it, so two
    /// systems cannot resume each other's hold. Owners are compared by reference.
    /// </summary>
    public sealed class AmbientSuspension
    {
        private readonly Dictionary<RuntimeEntityId, List<object>> owners = new();

        public void Suspend(RuntimeEntityId npc, object owner)
        {
            if (!npc.IsValid || owner == null) return;
            if (!owners.TryGetValue(npc, out var list)) owners[npc] = list = new List<object>();
            if (!Contains(list, owner)) list.Add(owner);
        }

        public void Resume(RuntimeEntityId npc, object owner)
        {
            if (!owners.TryGetValue(npc, out var list)) return;

            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(list[i], owner)) list.RemoveAt(i);
            }

            if (list.Count == 0) owners.Remove(npc);
        }

        public bool IsSuspended(RuntimeEntityId npc) => owners.ContainsKey(npc);

        private static bool Contains(List<object> list, object owner)
        {
            foreach (var o in list)
            {
                if (ReferenceEquals(o, owner)) return true;
            }

            return false;
        }
    }
}
