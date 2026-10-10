using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Gameplay.Ambient
{
    /// <summary>What an NPC does when nothing is asking it to do anything else. <see cref="Stand"/> (0) is the
    /// default, so every spawn point authored before routines existed deserializes as the old behaviour.</summary>
    public enum NpcRoutineKind
    {
        Stand = 0,
        Wander = 1,
        WorkLoop = 2,
    }

    public enum WorkStopAction
    {
        /// <summary>Walk here, wait <see cref="WorkStop.Seconds"/>, move on.</summary>
        None = 0,

        /// <summary>Take <see cref="WorkStop.Prop"/> in the hands (halfway through the action).</summary>
        PickUp = 1,

        /// <summary>Put the carried prop down (it is removed halfway through the action).</summary>
        Drop = 2,

        /// <summary>Do <see cref="WorkStop.CueId"/> for <see cref="WorkStop.Seconds"/> (hammering, kneeling...).</summary>
        Work = 3,
    }

    /// <summary>One place a work loop visits. The cue is looked up in the NPC's animation profile: a looping cue is
    /// held as a stance for <see cref="Seconds"/> (it needs a StanceLocomotionSet of the same id), a one-shot cue is
    /// played once. A cue the profile does not have is silently skipped (the validator warns).</summary>
    [Serializable]
    public sealed class WorkStop
    {
        [Tooltip("Where to stand. Its forward direction is the way the NPC faces while it works.")]
        public Transform Point;
        public WorkStopAction Action = WorkStopAction.None;
        [Tooltip("How long the stop takes. A one-shot cue never takes less than its own length.")]
        public float Seconds = 2f;
        public DefinitionId CueId;
        [Tooltip("PickUp only: what ends up in the NPC's hands.")]
        public CarriedPropDefinition Prop;
    }

    /// <summary>
    /// Authored on an <see cref="Scenes.NpcSpawnPoint"/>: what the NPC does between orders. Plain data; the behaviour
    /// is <see cref="AmbientRoutineMachine"/>, driven by <see cref="NpcRoutineRunner"/>.
    /// </summary>
    [Serializable]
    public sealed class NpcRoutineSpec
    {
        public NpcRoutineKind Kind = NpcRoutineKind.Stand;

        [Header("Wander")]
        [Tooltip("Centre of the wander circle. Empty = the NPC's own spawn anchor.")]
        public Transform AreaCentre;
        public float AreaRadius = 6f;
        public float PauseMin = 2f;
        public float PauseMax = 6f;

        [Header("Work loop")]
        public List<WorkStop> Stops = new();

        [Tooltip("0 = derived from the spawn id, so the same NPC always wanders the same way.")]
        public int Seed;

        public bool IsActive => Kind != NpcRoutineKind.Stand;

        /// <summary>Back to the default (Stand, nothing referenced). Used when a new spawn point is added next to
        /// an existing one, so it never inherits that NPC's stops.</summary>
        public void Reset()
        {
            Kind = NpcRoutineKind.Stand;
            AreaCentre = null;
            AreaRadius = 6f;
            PauseMin = 2f;
            PauseMax = 6f;
            Stops = new List<WorkStop>();
            Seed = 0;
        }

        /// <summary>Resolves transforms into world positions once, at attach time. <paramref name="anchor"/> stands in
        /// for an empty <see cref="AreaCentre"/>.</summary>
        public RoutinePlan Resolve(Transform anchor)
        {
            var plan = new RoutinePlan
            {
                Kind = Kind,
                Radius = Mathf.Max(0f, AreaRadius),
                PauseMin = Mathf.Max(0f, PauseMin),
                PauseMax = Mathf.Max(Mathf.Max(0f, PauseMin), PauseMax),
                Seed = Seed,
            };

            var centre = AreaCentre != null ? AreaCentre : anchor;
            if (centre != null) plan.Centre = centre.position;

            if (Stops != null)
            {
                foreach (var stop in Stops)
                {
                    if (stop == null || stop.Point == null) continue;
                    var forward = stop.Point.forward;
                    forward.y = 0f;
                    plan.Stops.Add(new RoutineStopPlan
                    {
                        Position = stop.Point.position,
                        Facing = forward,
                        Action = stop.Action,
                        Seconds = Mathf.Max(0f, stop.Seconds),
                        Cue = stop.CueId,
                        Prop = stop.Prop,
                        Label = stop.Point.name,
                    });
                }
            }

            return plan;
        }
    }

    /// <summary>A routine with every scene reference resolved to plain values, so the state machine needs no
    /// Unity objects to be tested.</summary>
    public sealed class RoutinePlan
    {
        public NpcRoutineKind Kind;
        public Vector3 Centre;
        public float Radius = 6f;
        public float PauseMin = 2f;
        public float PauseMax = 6f;
        public int Seed;
        public List<RoutineStopPlan> Stops = new();
    }

    public sealed class RoutineStopPlan
    {
        public Vector3 Position;
        public Vector3 Facing;
        public WorkStopAction Action;
        public float Seconds;
        public DefinitionId Cue;
        public CarriedPropDefinition Prop;
        public string Label;
    }
}
