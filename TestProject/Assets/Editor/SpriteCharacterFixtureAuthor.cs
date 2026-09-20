using CRPG.Editor.Character;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling -- not part of the shipped package.
//
// 2D campaign Task 2.5: authors the two checked-in fixtures the package's PlayMode tests load with Resources.Load (a PlayMode
// assembly cannot reference Editor code, so it cannot run CharacterWrapperBuilder itself):
//   - SpriteCharacter.prefab: a Grid + Sprite wrapper built through the real CharacterWrapperBuilder;
//   - SpriteCard.prefab: a generated card (the same hierarchy the wrapper's Model has), used as a CharacterView.SetModel swap target.
// An EditMode drift guard (SpriteFixtureDriftTests) rebuilds both and fails if the checked-in copies no longer match.
public static class SpriteCharacterFixtureAuthor
{
    public const string FixtureFolder = "Assets/Resources/Fixtures";
    public const string CharacterPath = FixtureFolder + "/SpriteCharacter.prefab";
    public const string CardPath = FixtureFolder + "/SpriteCard.prefab";

    [MenuItem("CRPG Toolkit/Characters/Author Sprite Character Test Fixtures")]
    public static void Author()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder(FixtureFolder);

        var spec = Spec();
        CharacterWrapperBuilder.BuildOrUpdate(spec, CharacterPath);

        var card = SpriteCardBuilder.Build(spec);
        try { PrefabUtility.SaveAsPrefabAsset(card, CardPath); }
        finally { Object.DestroyImmediate(card); }

        AssetDatabase.SaveAssets();
        Debug.Log($"[SpriteCharacterFixtureAuthor] Authored {CharacterPath} and {CardPath}.");
    }

    /// <summary>The spec both fixtures (and the drift guard) are built from.</summary>
    public static CharacterSetupSpec Spec() => new()
    {
        Movement = MovementBackend.Grid,
        Presentation = PresentationKind.Sprite,
    };

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
