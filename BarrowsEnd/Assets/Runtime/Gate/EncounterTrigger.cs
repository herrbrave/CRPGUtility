using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using UnityEngine;
using UnityEngine.Serialization;

namespace BarrowsEnd.Gate
{
    /// <summary>A one-shot tactical encounter: the first time a real party member enters the trigger volume it
    /// spawns its hostiles and starts a real encounter through <see cref="TacticalCombatSession"/>. Data-driven
    /// (Task 6.2) -- the palisade rats are just the default configuration, so the Town scene authored for 6.1 keeps
    /// working unchanged (the serialized fields keep their old names via <see cref="FormerlySerializedAsAttribute"/>
    /// and default to the palisade values).
    /// <para>Safety rules carried from 6.1 plus the 6.2 review's fixes: the "already fired" state is a persisted
    /// Timeline flag checked on enable; only a real party member's own agent can trigger it; entry while another
    /// encounter is active is ignored and retried (<c>OnTriggerStay</c>), and the fired flag is recorded only after
    /// the encounter actually started, so a busy session can never silently eat an encounter.</para></summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class EncounterTrigger : MonoBehaviour
    {
        public const string PalisadeFiredKey = "palisade-breach-triggered";
        public const string PalisadeWonKey = "palisade-rats-defeated";

        [SerializeField] private string firedKey = PalisadeFiredKey;
        [SerializeField] private string wonKey = PalisadeWonKey;
        [Tooltip("Optional shared key recorded on every win, for a quest objective counting several encounters.")]
        [SerializeField] private string progressKey = "";
        [SerializeField] private string npcDefinitionId = "npc-giant-rat";
        [FormerlySerializedAs("rats")] [SerializeField] private int count = 3;
        [SerializeField] private string secondaryNpcDefinitionId = "";
        [SerializeField] private int secondaryCount;
        [FormerlySerializedAs("ratSpawnScatter")] [SerializeField] private float spawnScatter = 2.5f;
        [Tooltip("Optional: hostiles spawn around this point instead of the trigger's own centre (a boss's ritual circle).")]
        [SerializeField] private Transform spawnAnchor;
        [Tooltip("Optional Timeline key that must have occurred before this can fire (e.g. a gate opened by a lever).")]
        [SerializeField] private string requiredKey = "";

        public GameSession Session { get; set; }
        public WorldSpawnService Spawner { get; set; }
        public TacticalCombatSession CombatSession { get; set; }

        /// <summary>Asks the shell to re-check every active quest after this trigger records its win keys.</summary>
        public Action RequestQuestRefresh { get; set; }

        /// <summary>Asks the shell to write a checkpoint save BEFORE hostiles spawn and the fired flag is recorded,
        /// so a lost fight can reload to exactly the moment before it began.</summary>
        public Func<bool> RequestCheckpoint { get; set; }

        /// <summary>Tells the shell the party was wiped; the shell reloads the checkpoint.</summary>
        public Action RequestDefeat { get; set; }

        /// <summary>Shows the player a one-line message (wired to the shell's Say).</summary>
        public Action<string> Notify { get; set; }

        public string FiredKey => firedKey;
        public string WonKey => wonKey;

        private bool fired;
        private bool awaitingExit;

        private void OnEnable() => RefreshFiredState();

        /// <summary>Called by ShellBootstrap.WireCombatTriggers once Session is wired (boot, every scene load, after
        /// a session load) -- OnEnable alone only covers a same-session revisit.</summary>
        public void RefreshFiredState()
        {
            if (Session == null) return;

            // Re-derived from the (possibly just-loaded) Timeline both ways: a same-scene load keeps this component
            // alive, so a fight lost and reloaded must re-arm the trigger, not leave the old in-memory flag set.
            var hasFired = Session.Timeline.HasOccurred(firedKey);
            fired = hasFired;
            if (gameObject.activeSelf == hasFired) gameObject.SetActive(!hasFired);
        }

        /// <summary>After a load (a lost fight's checkpoint reload puts the party back INSIDE the zone) the trigger would
        /// re-fire at once, trapping the player in a retry loop. If any party member is inside, hold fire until the whole
        /// party has stepped out; the player chooses when to go back in.</summary>
        public void DisarmUntilPartyLeaves()
        {
            if (fired || Session == null || !AnyPartyMemberInside()) return;

            awaitingExit = true;
            Notify?.Invoke("The danger remains. Step out and back in when you are ready.");
        }

        private void Update()
        {
            if (awaitingExit && !AnyPartyMemberInside()) awaitingExit = false;
        }

        private bool AnyPartyMemberInside()
        {
            if (Session == null) return false;

            var sphere = GetComponent<SphereCollider>();
            var scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            var reach = (sphere != null ? sphere.radius * scale : 2.5f) + 0.75f;
            foreach (var member in Session.Party.Members)
            {
                var agent = GateSessionServices.FindAgent(member);
                if (agent == null) continue;

                var delta = agent.transform.position - transform.position;
                delta.y = 0f;
                if (delta.magnitude <= reach) return true;
            }

            return false;
        }

        private void OnTriggerEnter(Collider other) => TryFire(other);

        // A party member standing in the volume while another encounter finishes must still trigger this one.
        private void OnTriggerStay(Collider other) => TryFire(other);

        private void TryFire(Collider other)
        {
            if (fired || awaitingExit || Session == null || Spawner == null || CombatSession == null || !CombatSession.CanStart) return;
            if (!string.IsNullOrEmpty(requiredKey) && !Session.Timeline.HasOccurred(requiredKey)) return;

            // During travel the destination scene is loaded ADDITIVELY while the old one is still active, and locations
            // share world coordinates -- so a trigger in the next level can sit right on the party's path out of this
            // one. Firing then would spawn the hostiles into the scene about to unload (a fight with vanished enemies)
            // and burn the encounter for good. Only the active location's triggers may fire; OnTriggerStay retries.
            if (gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) return;

            var agent = other.GetComponentInParent<CharacterAgent>();
            if (agent == null || !agent.IsBound) return;
            if (!Session.Party.Members.Contains(agent.EntityId)) return; // only a real party member triggers this, not a stray NPC

            Fire();
        }

        private void Fire()
        {
            var hostiles = new List<(string NpcId, int Count)> { (npcDefinitionId, count) };
            if (!string.IsNullOrEmpty(secondaryNpcDefinitionId) && secondaryCount > 0) hostiles.Add((secondaryNpcDefinitionId, secondaryCount));

            foreach (var (npcId, _) in hostiles)
            {
                if (!Session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId(npcId), out _))
                {
                    Debug.LogError($"[EncounterTrigger] {npcId} is missing -- author gear/ruleset content first.");
                    fired = true; // misconfigured: never retry every physics tick
                    return;
                }
            }

            RequestCheckpoint?.Invoke();

            var claimed = new List<Vector3>();
            foreach (var member in Session.Party.Members)
            {
                var memberAgent = GateSessionServices.FindAgent(member);
                if (memberAgent != null) claimed.Add(memberAgent.transform.position);
            }

            var hostileFaction = new DefinitionId("faction-hostile");
            var combatants = new List<(RuntimeEntityId Id, DefinitionId Faction)>();
            foreach (var member in Session.Party.Members) combatants.Add((member, TacticalCombatSession.PartyFaction));

            var brains = new Dictionary<RuntimeEntityId, CRPG.AI.BehaviorSetDefinition>();
            var names = new Dictionary<RuntimeEntityId, string>();
            var random = new System.Random();
            var index = 0;
            foreach (var (npcId, hostileCount) in hostiles)
            {
                Session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId(npcId), out var definition);
                var brain = HostileBrains.For(npcId, Session.RulesDatabase);
                for (var i = 0; i < hostileCount; i++, index++)
                {
                    var offset = new Vector3((float)(random.NextDouble() * 2 - 1), 0f, (float)(random.NextDouble() * 2 - 1)) * spawnScatter;
                    var spot = (spawnAnchor != null ? spawnAnchor.position : transform.position) + offset;
                    if (Spawner.GridNavigation != null && Spawner.GridNavigation.TrySamplePositionAvoiding(spot, out var sampled, spawnScatter * 2f, claimed))
                        spot = sampled;
                    claimed.Add(spot);

                    var hostileAgent = Spawner.SpawnNpc($"{firedKey}-{index + 1}", definition, spot, Quaternion.identity);
                    combatants.Add((hostileAgent.EntityId, hostileFaction));
                    brains[hostileAgent.EntityId] = brain;
                    names[hostileAgent.EntityId] = hostileCount == 1 ? definition.DisplayName : $"{definition.DisplayName} {i + 1}";
                }
            }

            if (!CombatSession.StartEncounter(combatants, hostileFaction, id => brains[id], OnEncounterEnded, id => names.TryGetValue(id, out var n) ? n : null))
            {
                Debug.LogError("[EncounterTrigger] the combat session refused to start; the spawned hostiles are left idle.");
                return;
            }

            fired = true;
            Session.Timeline.Record(firedKey, TimelineValue.Flag);
        }

        private void OnEncounterEnded(bool partyWon)
        {
            if (!partyWon) { RequestDefeat?.Invoke(); return; }
            Session.Timeline.Record(wonKey, TimelineValue.Flag);
            if (!string.IsNullOrEmpty(progressKey)) Session.Timeline.Record(progressKey, TimelineValue.Flag);
            RequestQuestRefresh?.Invoke();
        }
    }
}
