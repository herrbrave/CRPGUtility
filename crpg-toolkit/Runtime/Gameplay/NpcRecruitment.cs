using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Turns a spawned NPC into a member of the party: the NPC's own character joins <see cref="GameSession.Party"/> (no second character is built), and everything that made it a townsperson is removed (its routine, its
    /// talk adapter and interactable, the conversation facing). The game's <c>recruited</c> callback does what only the game can: register the NPC's motor with its exploration controller, add its name to the HUD, move
    /// the object to a scene that outlives the location.
    /// <para>
    /// A dialogue's Recruit action runs while the conversation is still in progress; tearing the NPC's interactable down then would end the interaction as "target lost" and cancel the conversation before its closing
    /// line. So <see cref="Queue"/> only remembers the decision and the recruit happens when that NPC's conversation ENDS, however it ends. <c>NpcDialogues.AttachAll</c> and <c>NpcRoutines.AttachAll</c> skip a
    /// spawn point whose character is a party member, so re-running them (after a load) never makes a companion a townsperson again.
    /// </para>
    /// Limitations: the spawn record still carries the NPC's definition id, so location travel, defeat and a save/load treat the recruit as an NPC unless the game moves and restores it as a party member.
    /// </summary>
    public sealed class NpcRecruitment : IDisposable
    {
        private readonly WorldSpawnService spawner;
        private readonly GameSession session;
        private readonly Action<CharacterAgent> recruited;
        private readonly DefinitionId partyFaction;
        private readonly Dictionary<string, (TalkInteractionAdapter adapter, Action<RuntimeEntityId> handler)> pending = new();

        /// <param name="recruited">Called after the NPC joined the party and was stripped of its townsperson parts.</param>
        /// <param name="partyFaction">The faction the recruit takes (none = it keeps its own).</param>
        public NpcRecruitment(WorldSpawnService spawner, GameSession session, Action<CharacterAgent> recruited = null, DefinitionId partyFaction = default)
        {
            this.spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.recruited = recruited;
            this.partyFaction = partyFaction;
        }

        /// <summary>Whether a recruit is waiting for its conversation to end.</summary>
        public bool IsPending(string spawnId) => pending.ContainsKey(spawnId);

        /// <summary>Recruit this spawn point's NPC when its current conversation ends (at once if it is not in one).</summary>
        /// <returns>False when there is no such live NPC or it is already a member.</returns>
        public bool Queue(string spawnId)
        {
            var agent = Find(spawnId);
            if (agent == null || session.Party.IsMember(agent.EntityId)) return false;
            if (pending.ContainsKey(spawnId)) return true;

            if (!agent.TryGetComponent<TalkInteractionAdapter>(out var adapter)) return Recruit(spawnId);

            Action<RuntimeEntityId> handler = null;
            handler = _ =>
            {
                adapter.ConversationEnded -= handler;
                pending.Remove(spawnId);
                Recruit(spawnId);
            };
            pending[spawnId] = (adapter, handler);
            adapter.ConversationEnded += handler;
            return true;
        }

        /// <summary>Recruits now. Safe to call only when no conversation with the NPC is in progress (see the class remarks).</summary>
        public bool Recruit(string spawnId)
        {
            var agent = Find(spawnId);
            if (agent == null || !agent.IsBound) return false;

            var id = agent.EntityId;
            if (session.Party.IsMember(id)) return false;

            if (agent.TryGetComponent<NpcRoutineRunner>(out var runner))
            {
                runner.Detach();   // stops the routine and gives back the avoidance priority it lowered
                Remove(runner);
            }

            if (agent.TryGetComponent<NpcConversationFacing>(out var facing)) Remove(facing);
            if (agent.TryGetComponent<TalkInteractionAdapter>(out var talk)) Remove(talk);
            if (agent.TryGetComponent<WorldInteractable>(out var interactable)) Remove(interactable);

            if (partyFaction.IsValid) spawner.SetFaction(id, partyFaction);
            session.Party.AddMember(id);
            recruited?.Invoke(agent);
            return true;
        }

        public void Dispose()
        {
            foreach (var entry in pending.Values)
            {
                if (entry.adapter != null) entry.adapter.ConversationEnded -= entry.handler;
            }

            pending.Clear();
        }

        private CharacterAgent Find(string spawnId)
        {
            if (string.IsNullOrEmpty(spawnId) || !spawner.TryGetCharacterId(spawnId, out var characterId, out _, out var defeated) || defeated) return null;
            foreach (var owned in spawner.OwnedAgents)
            {
                if (owned != null && owned.EntityId == characterId) return owned;
            }

            return null;
        }

        private static void Remove(Component component)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(component);
            else UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
