using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>Task 6.1 (T6.1-D6): the palisade-breach combat tutorial -- fires exactly once, the first time a
    /// real party member enters its trigger volume, spawning 2-3 Giant Rats and starting a real tactical
    /// encounter through <see cref="TacticalCombatSession"/>. Fixes the review's four real gaps in the v1 sketch
    /// (fact 15): (a) the "already fired" state is a persisted Timeline flag checked on enable, not an in-memory
    /// bool that would reset on scene reload; (b) each fire uses fresh spawn ids so a (should-never-happen)
    /// second fire can't collide; (c) only a real party member's own agent can trigger it, via
    /// <see cref="WorldInteractable"/>-style entity resolution + <c>PartyState.IsMember</c> -- not any
    /// CharacterAgent; (d) placed by <c>BarrowsEndTownAuthor</c> clearly offset from both the TravelToCaverns and
    /// FromCavernsDestination cells, not sitting on top of either.</summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class PalisadeBreachTrigger : MonoBehaviour
    {
        public const string FiredTimelineKey = "palisade-breach-triggered";
        public const string RatsDefeatedTimelineKey = "palisade-rats-defeated";

        // Duplicated from BarrowsEndGearAuthor.GatheringStormQuestId (Editor-only, not reachable from Runtime) --
        // same Editor/Runtime id-duplication convention this project already uses elsewhere.
        private const string GatheringStormQuestId = "quest-gathering-storm";

        [SerializeField] private int rats = 3;
        [SerializeField] private float ratSpawnScatter = 2.5f;

        public GameSession Session { get; set; }
        public WorldSpawnService Spawner { get; set; }
        public TacticalCombatSession CombatSession { get; set; }

        /// <summary>Task 6.1 (T6.1-D4): refreshes the Act 1 quest's progress after this trigger records the
        /// Kill objective's own Timeline key -- QuestService.RefreshProgress is never called automatically by
        /// recording a Timeline event, so whoever records it is responsible for asking the quest to re-check.</summary>
        public Action<string> RequestQuestRefresh { get; set; }

        private bool fired;

        private void OnEnable()
        {
            // Session isn't wired yet on the very first Awake/OnEnable (authoring time) -- ShellBootstrap.
            // WireCombatTriggers sets Session after the scene loads, then calls this same check again.
            RefreshFiredState();
        }

        /// <summary>Called by ShellBootstrap.WireCombatTriggers once Session is actually wired (boot, every
        /// scene load, and after a session load) -- the OnEnable call above only covers a same-session revisit
        /// where Session was already set from a previous wiring pass.</summary>
        public void RefreshFiredState()
        {
            if (Session != null && Session.Timeline.HasOccurred(FiredTimelineKey)) gameObject.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (fired || Session == null || Spawner == null || CombatSession == null) return;

            var agent = other.GetComponentInParent<CharacterAgent>();
            if (agent == null || !agent.IsBound) return;
            if (!Session.Party.Members.Contains(agent.EntityId)) return; // only a real party member triggers this, not a stray NPC

            Fire();
        }

        private void Fire()
        {
            fired = true;
            Session.Timeline.Record(FiredTimelineKey, TimelineValue.Flag);

            if (!Session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId("npc-giant-rat"), out var ratDefinition))
            {
                Debug.LogError("[PalisadeBreachTrigger] npc-giant-rat is missing -- author gear/ruleset content first.");
                return;
            }

            var claimed = new List<Vector3>();
            foreach (var member in Session.Party.Members)
            {
                var memberAgent = GateSessionServices.FindAgent(member);
                if (memberAgent != null) claimed.Add(memberAgent.transform.position);
            }

            var hostileFaction = new DefinitionId("faction-hostile");
            var combatants = new List<(RuntimeEntityId Id, DefinitionId Faction)>();
            foreach (var member in Session.Party.Members) combatants.Add((member, TacticalCombatSession.PartyFaction));

            var random = new System.Random();
            for (var i = 0; i < rats; i++)
            {
                var offset = new Vector3((float)(random.NextDouble() * 2 - 1), 0f, (float)(random.NextDouble() * 2 - 1)) * ratSpawnScatter;
                var spot = transform.position + offset;
                if (Spawner.GridNavigation != null && Spawner.GridNavigation.TrySamplePositionAvoiding(spot, out var sampled, ratSpawnScatter * 2f, claimed))
                    spot = sampled;
                claimed.Add(spot);

                var ratAgent = Spawner.SpawnNpc($"palisade-rat-{i + 1}", ratDefinition, spot, Quaternion.identity);
                combatants.Add((ratAgent.EntityId, hostileFaction));
            }

            var brain = GiantRatBrain.BuildAndRegister(Session.RulesDatabase);
            CombatSession.StartEncounter(combatants, hostileFaction, brain, OnEncounterEnded);
        }

        private void OnEncounterEnded(bool partyWon)
        {
            if (!partyWon) return;
            Session.Timeline.Record(RatsDefeatedTimelineKey, TimelineValue.Flag);
            RequestQuestRefresh?.Invoke(GatheringStormQuestId);
        }
    }
}
