using System;
using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

// Dogfood-only demo script. The only concrete IPlayerCommandSource in the project -- the interface exists
// specifically so a real Input System adapter can replace this later without touching PartyExplorationController.
// Only Move is ever raised (a single-character party never needs Select); left-click raycasts against the
// ground and sends a MoveCommand to whatever's currently selected in PartyState.
public sealed class MouseClickCommandSource : MonoBehaviour, IPlayerCommandSource
{
    [SerializeField] private Camera raycastCamera;
    [SerializeField] private LayerMask groundMask = ~0;

    public event Action<SelectCommand> Select;
    public event Action<MoveCommand> Move;
    public event Action<InteractCommand> Interact;
    public event Action Cancel;

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (IsPointerOverUI()) return;

        var activeCamera = raycastCamera != null ? raycastCamera : Camera.main;
        if (activeCamera == null) return;

        var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 500f, groundMask))
            Move?.Invoke(new MoveCommand(hit.point, Vector3.forward));
    }

    // Clicking a DialogueView button (Continue/choice/Roll) was also firing a world-space move command
    // underneath it -- there's no EventSystem in this scene, and runtime UI Toolkit panels don't route through
    // one on their own, so nothing was stopping a raw Input.GetMouseButtonDown click from being read twice by
    // two unrelated systems. Ask every currently-open UIDocument's panel directly whether the pointer is over
    // one of its elements (UI Toolkit's own picking, independent of EventSystem) before treating a click as a
    // world click.
    private static bool IsPointerOverUI()
    {
        var screenPosition = (Vector2)Input.mousePosition;
        foreach (var document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        {
            var panel = document.rootVisualElement?.panel;
            if (panel == null) continue;

            var panelPosition = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
            if (panel.Pick(panelPosition) != null) return true;
        }

        return false;
    }
}
