using System;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>A pick-up-once world node (a patch of giant mushrooms). Interacting records a per-node Timeline key (so a
    /// reload or a revisit never offers it again) and a shared key every node counts toward (what a Collect objective
    /// tracks), then hides the node. The node id is serialized, not derived: <c>WorldInteractable.EntityId</c> is a fresh id every run.</summary>
    [RequireComponent(typeof(WorldInteractable))]
    public sealed class HarvestInteractionAdapter : MonoBehaviour, IInteractionAdapter
    {
        public const string SharedKey = "mushroom-harvested";

        [SerializeField] private string nodeId;
        [SerializeField] private string sharedKey = SharedKey;
        [SerializeField] private string displayName = "Giant mushrooms";

        public GameSession Session { get; set; }
        public Action RequestQuestRefresh { get; set; }
        public Action<string> Notify { get; set; }

        public string NodeKey => $"mushroom-node-{nodeId}";

        public DefinitionId ActionId { get; } = new("interact-harvest");

        public bool IsAvailable(RuntimeEntityId performer) => Session != null && !Session.Timeline.HasOccurred(NodeKey);

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            if (!IsAvailable(performer))
            {
                onFinished(InteractionExecutionResult.Failed);
                return null;
            }

            Session.Timeline.Record(NodeKey, TimelineValue.Flag);
            Session.Timeline.Record(sharedKey, TimelineValue.Flag);
            Notify?.Invoke($"Harvested {displayName.ToLowerInvariant()} ({Session.Timeline.CountOccurrences(sharedKey)} so far)");
            onFinished(InteractionExecutionResult.Succeeded);
            gameObject.SetActive(false);
            RequestQuestRefresh?.Invoke();
            return null;
        }

        /// <summary>Hides an already-harvested node. Called by the shell once a session is wired (boot, scene load, after
        /// a save load) -- a harvested node's persisted key outlives the scene object.</summary>
        public void RefreshState()
        {
            if (Session == null) return;
            var harvested = Session.Timeline.HasOccurred(NodeKey);
            if (gameObject.activeSelf == harvested) gameObject.SetActive(!harvested);
        }
    }
}
