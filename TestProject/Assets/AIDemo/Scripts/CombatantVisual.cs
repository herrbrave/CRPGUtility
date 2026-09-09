using UnityEngine;

// Dogfood-only: a combatant's on-screen representation for the 2D AI-combat test scene -- a colored body
// quad, an HP bar, and a name label. No art dependency (matches CampView/StoreView's "functional placeholder
// first" convention) since this scene exists to watch AI decisions, not to look good.
public sealed class CombatantVisual : MonoBehaviour
{
    private Renderer bodyRenderer;
    private Renderer hpFillRenderer;
    private Transform hpFillTransform;
    private float hpFillFullWidth;
    private bool defeated;

    public void Initialize(Color bodyColor, string label)
    {
        var body = CreateQuad("Body", transform, new Vector3(0f, 0f, 0f), new Vector3(0.9f, 0.9f, 1f), bodyColor);
        bodyRenderer = body.GetComponent<Renderer>();

        CreateQuad("HPBack", transform, new Vector3(0f, 0.65f, -0.01f), new Vector3(0.9f, 0.12f, 1f), new Color(0.15f, 0.15f, 0.15f));

        var hpFill = CreateQuad("HPFill", transform, new Vector3(0f, 0.65f, -0.02f), new Vector3(0.9f, 0.1f, 1f), Color.green);
        hpFillRenderer = hpFill.GetComponent<Renderer>();
        hpFillTransform = hpFill.transform;
        hpFillFullWidth = hpFillTransform.localScale.x;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, -0.7f, -0.01f);
        var textMesh = textGo.AddComponent<TextMesh>();
        textMesh.text = label;
        textMesh.characterSize = 0.1f;
        textMesh.fontSize = 48;
        textMesh.anchor = TextAnchor.UpperCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = Color.white;
    }

    public void SetHpFraction(float fraction)
    {
        if (defeated) return;

        fraction = Mathf.Clamp01(fraction);
        var scale = hpFillTransform.localScale;
        scale.x = hpFillFullWidth * fraction;
        hpFillTransform.localScale = scale;

        var localPosition = hpFillTransform.localPosition;
        localPosition.x = -(hpFillFullWidth - scale.x) / 2f;
        hpFillTransform.localPosition = localPosition;

        hpFillRenderer.material.color = Color.Lerp(Color.red, Color.green, fraction);
    }

    public void SetHighlighted(bool highlighted)
    {
        if (defeated) return;
        transform.localScale = highlighted ? Vector3.one * 1.2f : Vector3.one;
    }

    public void SetDefeated()
    {
        defeated = true;
        transform.localScale = Vector3.one * 0.6f;
        bodyRenderer.material.color = new Color(0.3f, 0.3f, 0.3f);
        hpFillRenderer.material.color = new Color(0.3f, 0.3f, 0.3f);
    }

    private static GameObject CreateQuad(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPosition;
        quad.transform.localScale = localScale;
        quad.GetComponent<Renderer>().material = new Material(Shader.Find("Unlit/Color")) { color = color };
        return quad;
    }
}
