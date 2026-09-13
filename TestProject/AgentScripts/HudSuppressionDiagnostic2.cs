// Tests suppression at the CENTER of each of the four HudRoot regions (Party/Inventory/QuestLog/Save) --
// general panel area, not specifically a button -- plus one point known to be empty ground (screen center,
// away from any panel) as a negative control.
var hudRoot = UnityEngine.Object.FindAnyObjectByType<CRPG.UI.HudRoot>();
if (hudRoot == null) return new { error = "HudRoot not found." };

var document = hudRoot.GetComponent<UnityEngine.UIElements.UIDocument>();
var root = document.rootVisualElement;

var inputSource = UnityEngine.Object.FindAnyObjectByType<CRPG.InputSystem.InputSystemCommandSource>();
var routerProp = typeof(CRPG.InputSystem.InputSystemCommandSource).GetProperty("Router", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var router = (CRPG.Gameplay.PlayerCommandRouter)routerProp.GetValue(inputSource);
var camera = UnityEngine.Camera.main;

var results = new System.Collections.Generic.List<object>();

bool TestAt(UnityEngine.Vector2 screenPos)
{
    var moveFired = false;
    var selectFired = false;
    System.Action<CRPG.Core.MoveCommand> onMove = _ => moveFired = true;
    System.Action<CRPG.Core.SelectCommand> onSelect = _ => selectFired = true;
    router.Move += onMove;
    router.Select += onSelect;
    var ray = camera.ScreenPointToRay(screenPos);
    router.HandlePointerDown(screenPos, ray);
    router.HandlePointerUp(screenPos, ray, camera, false);
    router.Move -= onMove;
    router.Select -= onSelect;
    return moveFired || selectFired;
}

for (var i = 0; i < root.childCount; i++)
{
    var region = root[i];
    var center = region.worldBound.center;
    var suppressed = !TestAt(center);
    results.Add(new
    {
        regionIndex = i,
        regionName = region.name,
        childCount = region.childCount,
        worldBound = region.worldBound.ToString(),
        center = center.ToString(),
        suppressed,
    });
}

var controlPos = new UnityEngine.Vector2(UnityEngine.Screen.width / 2f, UnityEngine.Screen.height / 2f);
var controlSuppressed = !TestAt(controlPos);

return new
{
    regionCount = root.childCount,
    results,
    negativeControl = new { pos = controlPos.ToString(), suppressed = controlSuppressed },
};
