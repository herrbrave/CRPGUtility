using CRPG.Core;
using CRPG.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Gameplay.Ambient
{
    /// <summary>
    /// The thin Unity adapter around <see cref="AmbientRoutineMachine"/>: lives on a spawned NPC, feeds the machine
    /// the agent's real motor / animator / facing / hands, ticks it, and applies the session's suspension. Created by
    /// <see cref="NpcRoutines"/>; it stays on the view, so releasing the view releases the routine with it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcRoutineRunner : MonoBehaviour
    {
        /// <summary>Routine NPCs are less important to the avoidance solver than the party and combatants (lower
        /// number = more important, everything defaults to 50), so a villager standing at a work stop is walked
        /// around instead of becoming an immovable obstacle that traps a hero.</summary>
        public const int RoutineAvoidancePriority = 70;

        private CharacterAgent agent;
        private NavMeshAgent navAgent;
        private int originalPriority;
        private bool wasReady;
        private bool priorityApplied;

        public AmbientRoutineMachine Machine { get; private set; }

        internal void Initialise(CharacterAgent agent, RoutinePlan plan, int seed, INavMeshService nav, System.Action<string> log)
        {
            this.agent = agent;
            navAgent = agent.GetComponent<NavMeshAgent>();
            if (navAgent != null) originalPriority = navAgent.avoidancePriority;

            Machine = new AmbientRoutineMachine(
                plan,
                new AgentMotorProxy(agent),
                nav,
                new AnimatorRoutineAnimation(agent),
                new AgentRoutineFacing(agent),
                new HandRoutineProp(agent),
                () => agent.transform.position,
                seed,
                log);
        }

        private void Update()
        {
            if (Machine == null) return;

            var ready = agent != null && agent.IsBound && agent.isActiveAndEnabled && agent.Motor != null;
            if (!ready)
            {
                // Defeated or switched off: forget the step without touching a motor that is no longer active.
                if (wasReady) Machine.Abandon();
                wasReady = false;
                return;
            }

            wasReady = true;
            var suspended = agent.Session.Ambient.IsSuspended(agent.EntityId);
            Machine.SetSuspended(suspended);
            SetPriority(!suspended);
            Machine.Tick(Time.deltaTime);
        }

        /// <summary>Stops the routine for good and gives back the avoidance priority it lowered (the NPC is being turned into something else, a party member). The component is then removed by the caller.</summary>
        public void Detach()
        {
            Machine?.Abandon();
            SetPriority(false);
            Machine = null;
            enabled = false;
        }

        private void OnDisable()
        {
            Machine?.Abandon();
            wasReady = false;
        }

        private void SetPriority(bool routineActive)
        {
            if (navAgent == null || routineActive == priorityApplied) return;
            priorityApplied = routineActive;
            navAgent.avoidancePriority = routineActive ? Mathf.Max(originalPriority, RoutineAvoidancePriority) : originalPriority;
        }

        // The agent's Motor is resolved at bind time; the proxy reads it on every call so a rebind is picked up.
        private sealed class AgentMotorProxy : IMovable
        {
            private readonly CharacterAgent agent;

            public AgentMotorProxy(CharacterAgent agent) => this.agent = agent;

            public MoveResult SetDestination(Vector3 destination) =>
                agent.Motor != null ? agent.Motor.SetDestination(destination) : MoveResult.Invalid();

            public void Stop() => agent.Motor?.Stop();
            public bool Warp(Vector3 position) => agent.Motor != null && agent.Motor.Warp(position);

            public MotionSnapshot Snapshot => agent.Motor != null ? agent.Motor.Snapshot : MotionSnapshot.None;
        }
    }
}
