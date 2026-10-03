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
    public sealed class PlayerCommandRouter : IPlayerCommandSource, IDragSelectionFeedback
    {
        private readonly ISelectableSource selectableSource;
        private readonly IPointerCaptureProvider pointerCapture;
        private readonly LayerMask hittableMask;
        private readonly float dragThresholdPixelsSquared;
        private readonly float maxRayDistance;

        private bool gestureActive;
        private bool gestureSuppressed;
        private bool exceededThreshold;
        private bool dragBoxShown;
        private Vector2 downScreenPosition;
        private Vector2 currentScreenPosition;

        public event Action<SelectCommand> Select;
        public event Action<MoveCommand> Move;
        public event Action<InteractCommand> Interact;
        public event Action Cancel;

        /// <summary>See <see cref="IDragSelectionFeedback"/>'s own doc comments for exactly when these fire.</summary>
        public event Action<Rect> DragBoxChanged;
        public event Action DragBoxEnded;

        /// <summary>Reserved extension point for a future ability-targeting mode -- see
        /// <see cref="IPointerTargetingGate"/>. Null (the default) skips the "targeting" priority tier
        /// entirely.</summary>
        public IPointerTargetingGate TargetingGate { get; set; }

        /// <summary>Task 3.6: while set and IsBusy, every pointer gesture is suppressed outright -- a stray
        /// click can't issue a new Move/Select/Interact order while a scene travel or cross-scene load is
        /// transitioning the world out from under whatever's currently loaded. Null (the default) skips this
        /// check entirely, matching TargetingGate's own "reserved seam ahead of its consumer" precedent.</summary>
        public CRPG.Gameplay.Scenes.IWorldTransitionGate TransitionGate { get; set; }

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
            // Defensive: a stale gesture that never got a matching Up (should not normally happen) must not
            // leave a visible drag box stuck on screen forever once a new gesture starts.
            EndDragBoxIfShown();

            gestureActive = true;
            exceededThreshold = false;
            downScreenPosition = screenPosition;
            currentScreenPosition = screenPosition;
            // Task 3.6: folded into the same suppression flag as UI/modal capture -- a gesture that starts
            // while the world is mid-transition (scene travel or a cross-scene load) must never issue a
            // Move/Select/Interact order, exactly like one that starts over UI.
            gestureSuppressed = pointerCapture.IsPointerOverUI(screenPosition) || (TransitionGate?.IsBusy ?? false);
        }

        /// <summary>Call while the pointer/tap is held down and moving. A no-op outside an active gesture.
        /// Suppression is re-checked live on every move (not just the press-time snapshot) purely for the drag
        /// box's own visibility -- a modal opening mid-drag must hide an already-visible box immediately, even
        /// though the gesture's eventual resolution still only ever consults the press-time snapshot.</summary>
        public void HandlePointerMove(Vector2 screenPosition)
        {
            if (!gestureActive) return;

            currentScreenPosition = screenPosition;
            if (!exceededThreshold && (screenPosition - downScreenPosition).sqrMagnitude >= dragThresholdPixelsSquared)
                exceededThreshold = true;

            var currentlySuppressed = gestureSuppressed || pointerCapture.IsPointerOverUI(screenPosition) || (TransitionGate?.IsBusy ?? false);
            if (exceededThreshold && !currentlySuppressed)
            {
                dragBoxShown = true;
                DragBoxChanged?.Invoke(ComputeDragRect());
            }
            else
            {
                EndDragBoxIfShown();
            }
        }

        /// <summary>Call when the pointer/tap is released. Resolves to a click (never exceeded the drag
        /// threshold) or a drag-box select (did), unless the gesture was suppressed at press time OR the
        /// pointer is over UI/a modal right now -- a gesture that started clean over the world but ends after a
        /// modal opened mid-gesture (e.g. a synchronous interaction execute triggered by a different click)
        /// must not leak a Move/Select through just because the down-time check didn't see it coming.</summary>
        public void HandlePointerUp(Vector2 screenPosition, Ray ray, UnityEngine.Camera camera, bool shiftHeld)
        {
            if (!gestureActive) return;
            gestureActive = false;
            currentScreenPosition = screenPosition;
            // Always ends any visible box before resolving anything below, so every path out of this method --
            // suppressed, click, or drag-select -- leaves no box stuck on screen.
            EndDragBoxIfShown();

            if (gestureSuppressed || pointerCapture.IsPointerOverUI(screenPosition) || (TransitionGate?.IsBusy ?? false)) return;

            if (exceededThreshold) ResolveDragSelect(camera, shiftHeld);
            else ResolveClick(ray, shiftHeld);
        }

        /// <summary>Ends the current gesture without resolving it into any command -- for a release that
        /// cannot be resolved normally (e.g. no camera available to build a ray/world position from). Still
        /// ends a visible drag box, so it can never survive this path either.</summary>
        public void AbortGesture()
        {
            gestureActive = false;
            EndDragBoxIfShown();
        }

        public void HandleCancel()
        {
            if (TransitionGate?.IsBusy ?? false) return;
            Cancel?.Invoke();
        }

        private void EndDragBoxIfShown()
        {
            if (!dragBoxShown) return;
            dragBoxShown = false;
            DragBoxEnded?.Invoke();
        }

        private Rect ComputeDragRect() => Rect.MinMaxRect(
            Mathf.Min(downScreenPosition.x, currentScreenPosition.x),
            Mathf.Min(downScreenPosition.y, currentScreenPosition.y),
            Mathf.Max(downScreenPosition.x, currentScreenPosition.x),
            Mathf.Max(downScreenPosition.y, currentScreenPosition.y));

        private void ResolveClick(Ray ray, bool shiftHeld)
        {
            if (TargetingGate != null && TargetingGate.TryConsume(ray)) return;

            // Trigger volumes (encounter / quest zones) are not clickable world: ignoring them keeps a click on the ground
            // under one a real ground click, and keeps an interactable inside one reachable.
            if (!Physics.Raycast(ray, out var hit, maxRayDistance, hittableMask, QueryTriggerInteraction.Ignore)) return;

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

        private void ResolveDragSelect(UnityEngine.Camera camera, bool shiftHeld)
        {
            if (camera == null) return;

            var rect = ComputeDragRect();

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
