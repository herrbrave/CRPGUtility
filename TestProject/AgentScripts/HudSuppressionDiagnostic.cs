// Drives the real PlayerCommandRouter with a real Save button's screen coordinates and observes whether
// Move/Select fire -- tests the FULL production suppression path (IsPointerOverUI + the router's own logic),
// not just panel.Pick() in isolation. No actual button click is simulated (bypasses InputSystemUIInputModule
// entirely), so any state change is exactly what a genuine world click at that screen position would cause.
var hudRoot = UnityEngine.Object.FindAnyObjectByType<CRPG.UI.HudRoot>();
if (hudRoot == null) return new { error = "HudRoot not found -- is AdventureSlice playing and booted?" };

var document = hudRoot.GetComponent<UnityEngine.UIElements.UIDocument>();
var root = document.rootVisualElement;

UnityEngine.UIElements.VisualElement saveButton = null;
void Find(UnityEngine.UIElements.VisualElement ve)
{
    if (saveButton != null) return;
    if (ve is UnityEngine.UIElements.Button btn && btn.text == "Save") { saveButton = ve; return; }
    for (var i = 0; i < ve.childCount; i++) Find(ve[i]);
}
Find(root);
if (saveButton == null) return new { error = "No Save button found." };

var screenPos = saveButton.worldBound.center; // panel space == screen space here, confirmed by the prior diagnostic

var inputSource = UnityEngine.Object.FindAnyObjectByType<CRPG.InputSystem.InputSystemCommandSource>();
if (inputSource == null) return new { error = "InputSystemCommandSource not found." };

var routerProp = typeof(CRPG.InputSystem.InputSystemCommandSource).GetProperty("Router", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var router = (CRPG.Gameplay.PlayerCommandRouter)routerProp.GetValue(inputSource);

var moveFired = false;
var selectFired = false;
System.Action<CRPG.Core.MoveCommand> onMove = _ => moveFired = true;
System.Action<CRPG.Core.SelectCommand> onSelect = _ => selectFired = true;
router.Move += onMove;
router.Select += onSelect;

var camera = UnityEngine.Camera.main;
var ray = camera.ScreenPointToRay(screenPos);

router.HandlePointerDown(screenPos, ray);
router.HandlePointerUp(screenPos, ray, camera, false);

router.Move -= onMove;
router.Select -= onSelect;

return new
{
    screenPos = screenPos.ToString(),
    moveFired,
    selectFired,
};
