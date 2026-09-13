using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay.Interactions;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Resolves raw pointer gestures (press/move/release screen position + camera ray) into typed player
    /// commands, in one documented priority order (Docs/Design/Production-Foundation.md sec. "Input, camera,
    /// party"): captured UI/modal -&gt; targeting -&gt; interactable/selectable -&gt; ground. Deliberately plain C#,
    /// not a MonoBehaviour or anything Input-System-specific -- an adapter (mouse, touch, controller cursor,
    /// ...) only needs to feed it screen positions/rays; every decision (click-vs-drag threshold, UI/modal
    /// suppression, shift-toggle, drag-box hit testing) is unit-testable without a live input device or a real
    /// UI.
    /// </summary>
    /// <remarks>
    /// Implements <see cref="IPlayerCommandSource"/> itself so it's a drop-in source for
    /// <see cref="CRPG.Exploration.PartyExplorationController"/> and any future consumer --
    /// InputSystemCommandSource only reads raw Input System events and forwards them here.
    /// </remarks>
    public sealed class PlayerCommandRouter : IPlayerCommandSource
    {
        private readonly ISelectableSource selectableSource;
        private readonly IPointerCaptureProvider pointerCapture;
        private readonly LayerMask hittableMask;
        private readonly float dragThresholdPixelsSquared;
        private readonly float maxRayDistance;

        private bool gestureActive;
        private bool gestureSuppressed;
        private bool exceededThreshold;
        private Vector2 downScreenPosition;
        private Vector2 currentScreenPosition;

        public event Action<SelectCommand> Select;
        public event Action<MoveCommand> Move;
        public event Action<InteractCommand> Interact;
        public event Action Cancel;

        /// <summary>Reserved extension point for a future ability-targeting mode -- see
        /// <see cref="IPointerTargetingGate"/>. Null (the default) skips the "targeting" priority tier
        /// entirely.</summary>
        public IPointerTargetingGate TargetingGate { get; set; }

        public PlayerCommandRouter(
            ISelectableSource selectableSource,
            IPointerCaptureProvider pointerCapture,
            LayerMask hittableMask,
            float dragThresholdPixels = 10f,
            float maxRayDistance = 500f)
        {
            this.selectableSource = selectableSource ?? throw new ArgumentNullException(nameof(selectableSource));
            this.pointerCapture = pointerCapture ?? throw new ArgumentNullException(nameof(pointerCapture));
            this.hittableMask = hittableMask;
            dragThresholdPixelsSquared = dragThresholdPixels * dragThresholdPixels;
            this.maxRayDistance = maxRayDistance;
        }

        /// <summary>Call on the frame a pointer/tap begins. Checks UI/modal capture NOW, at press time -- a
        /// drag that starts over a UI element must never be reinterpreted as a world gesture just because the
        /// release happens to land over the game view ("suppress release-through after UI drags").</summary>
        public void HandlePointerDown(Vector2 screenPosition, Ray ray)
        {
            gestureActive = true;
            exceededThreshold = false;
            downScreenPosition = screenPosition;
            currentScreenPosition = screenPosition;
            gestureSuppressed = pointerCapture.IsPointerOverUI(screenPosition);
        }

        /// <summary>Call while the pointer/tap is held down and moving. A no-op outside an active gesture.</summary>
        public void HandlePointerMove(Vector2 screenPosition)
        {
            if (!gestureActive) return;

            currentScreenPosition = screenPosition;
            if (!exceededThreshold && (screenPosition - downScreenPosition).sqrMagnitude >= dragThresholdPixelsSquared)
                exceededThreshold = true;
        }

        /// <summary>Call when the pointer/tap is released. Resolves to a click (never exceeded the drag
        /// threshold) or a drag-box select (did), unless the gesture was suppressed at press time OR the
        /// pointer is over UI/a modal right now -- a gesture that started clean over the world but ends after a
        /// modal opened mid-gesture (e.g. a synchronous interaction execute triggered by a different click)
        /// must not leak a Move/Select through just because the down-time check didn't see it coming.</summary>
        public void HandlePointerUp(Vector2 screenPosition, Ray ray, Camera camera, bool shiftHeld)
        {
            if (!gestureActive) return;
            gestureActive = false;
            currentScreenPosition = screenPosition;

            if (gestureSuppressed || pointerCapture.IsPointerOverUI(screenPosition)) return;

            if (exceededThreshold) ResolveDragSelect(camera, shiftHeld);
            else ResolveClick(ray, shiftHeld);
        }

        public void HandleCancel() => Cancel?.Invoke();

        private void ResolveClick(Ray ray, bool shiftHeld)
        {
            if (TargetingGate != null && TargetingGate.TryConsume(ray)) return;

            if (!Physics.Raycast(ray, out var hit, maxRayDistance, hittableMask)) return;

            // Interactable/selectable tier. A CharacterAgent is checked first -- a selectable character always
            // resolves as Select, never Interact, even if a WorldInteractable also happened to sit on the same
            // object (this phase deliberately keeps the two apart: interactable targets are static world
            // objects, not characters).
            var agent = hit.collider.GetComponentInParent<CharacterAgent>();
            if (agent != null)
            {
                var mode = shiftHeld ? SelectionMode.Toggle : SelectionMode.Replace;
                Select?.Invoke(new SelectCommand(new[] { agent.EntityId }, mode));
                return;
            }

            var interactable = hit.collider.GetComponentInParent<WorldInteractable>();
            if (interactable != null)
            {
                Interact?.Invoke(new InteractCommand(interactable.EntityId));
                return;
            }

            // Ground tier. Facing left as Vector3.forward, matching MouseClickCommandSource's own existing
            // convention -- whatever consumes MoveCommand.Facing derives real facing itself.
            Move?.Invoke(new MoveCommand(hit.point, Vector3.forward));
        }

        private void ResolveDragSelect(Camera camera, bool shiftHeld)
        {
            if (camera == null) return;

            var rect = Rect.MinMaxRect(
                Mathf.Min(downScreenPosition.x, currentScreenPosition.x),
                Mathf.Min(downScreenPosition.y, currentScreenPosition.y),
                Mathf.Max(downScreenPosition.x, currentScreenPosition.x),
                Mathf.Max(downScreenPosition.y, currentScreenPosition.y));

            List<RuntimeEntityId> hits = null;
            foreach (var selectable in selectableSource.GetSelectables())
            {
                var screenPoint = camera.WorldToScreenPoint(selectable.WorldPosition);
                if (screenPoint.z < 0f) continue; // behind the camera -- not actually visible in the drag rect
                if (!rect.Contains(screenPoint)) continue;

                hits ??= new List<RuntimeEntityId>();
                hits.Add(selectable.Id);
            }

            // No hits is a legal "dragged over nothing" outcome -- PartyExplorationController.HandleSelect
            // already guards an empty Characters list, but not firing the event at all keeps a no-op gesture
            // silent rather than routing an empty command through every listener.
            if (hits == null) return;

            var mode = shiftHeld ? SelectionMode.Toggle : SelectionMode.Replace;
            Select?.Invoke(new SelectCommand(hits, mode));
        }
    }
}
