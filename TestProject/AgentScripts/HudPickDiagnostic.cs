// Read-only diagnostic (no scene/asset mutation). Must be run while AdventureSlice_Core is playing and booted.
var hudRoot = UnityEngine.Object.FindAnyObjectByType<CRPG.UI.HudRoot>();
if (hudRoot == null) return new { error = "HudRoot not found -- is AdventureSlice playing and booted?" };

var document = hudRoot.GetComponent<UnityEngine.UIElements.UIDocument>();
var root = document.rootVisualElement;
var panel = root != null ? root.panel : null;

var registeredField = typeof(CRPG.UI.UIToolkitPointerCapture).GetField("registered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var registeredSet = (System.Collections.Generic.HashSet<UnityEngine.UIElements.UIDocument>)registeredField.GetValue(null);

var buttons = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
void Collect(UnityEngine.UIElements.VisualElement ve)
{
    if (ve is UnityEngine.UIElements.Button) buttons.Add(ve);
    for (var i = 0; i < ve.childCount; i++) Collect(ve[i]);
}
Collect(root);

var probes = new System.Collections.Generic.List<object>();
foreach (var b in buttons)
{
    var panelCenter = b.worldBound.center;
    var pickedAtOwnCenter = panel.Pick(panelCenter);
    string pickedTypeName = pickedAtOwnCenter == null ? null : pickedAtOwnCenter.GetType().Name;
    string pickedElementName = pickedAtOwnCenter == null ? null : pickedAtOwnCenter.name;
    var buttonAsButton = (UnityEngine.UIElements.Button)b;
    probes.Add(new
    {
        text = buttonAsButton.text,
        panelWorldBound = b.worldBound.ToString(),
        panelCenter = panelCenter.ToString(),
        pickedType = pickedTypeName,
        pickedName = pickedElementName,
        pickedIsThisButton = System.Object.ReferenceEquals(pickedAtOwnCenter, b),
    });
}

var screenPos = new UnityEngine.Vector2(UnityEngine.Screen.width / 2f, UnityEngine.Screen.height / 2f);
var panelPosFromScreen = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(panel, screenPos);

return new
{
    screenSize = new { w = UnityEngine.Screen.width, h = UnityEngine.Screen.height },
    panelNotNull = panel != null,
    rootPickingMode = root.pickingMode.ToString(),
    rootResolvedStyle = new { left = root.resolvedStyle.left, top = root.resolvedStyle.top, width = root.resolvedStyle.width, height = root.resolvedStyle.height, position = root.resolvedStyle.position.ToString() },
    rootLayout = root.layout.ToString(),
    registeredCount = registeredSet.Count,
    registeredContainsThisDocument = registeredSet.Contains(document),
    buttonCount = buttons.Count,
    screenCenterToPanelCenter = new { screenPos = screenPos.ToString(), panelPos = panelPosFromScreen.ToString() },
    probes,
};
