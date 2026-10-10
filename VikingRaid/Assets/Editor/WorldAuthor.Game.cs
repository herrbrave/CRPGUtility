using CRPG.InputSystem;
using CRPG.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VikingRaid.Game;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Stage E (Task 4): the playable layer of the scene. A <c>Game</c> object carrying the real input source (hittable mask = <see cref="WorldLayers.Hittable"/>, NOT the default
    /// "everything" -- the router captures it once, so a wrong mask means clicks on trunks and rocks are swallowed or goblins cannot be clicked), the toolkit HUD components on a
    /// UIDocument, and <see cref="VikingGame"/> wired to the anchors, the ruleset and the camera. The Task 3 placeholder camera becomes the <see cref="VikingCamera"/>.
    /// </summary>
    public static partial class WorldAuthor
    {
        public const string RulesetPath = "Assets/Content/Ruleset/VikingRaidRuleset.asset";
        private static Camera mainCamera;

        private static void BuildGame()
        {
            var panelSettingsPath = $"{GeneratedDir}/HudPanelSettings.asset";
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);

            var cameraRig = mainCamera.gameObject.AddComponent<VikingCamera>();

            var gameObject = new GameObject("Game");
            var input = gameObject.AddComponent<InputSystemCommandSource>();
            var serializedInput = new SerializedObject(input);
            serializedInput.FindProperty("raycastCamera").objectReferenceValue = mainCamera;
            serializedInput.FindProperty("hittableMask").intValue = WorldLayers.Hittable;
            serializedInput.ApplyModifiedPropertiesWithoutUndo();

            var hud = new GameObject("Hud");
            hud.transform.SetParent(gameObject.transform, false);
            var document = hud.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
            var hudRoot = hud.AddComponent<HudRoot>();
            var abilityBar = hud.AddComponent<AbilityBarView>();
            var turnOrder = hud.AddComponent<TurnOrderView>();
            var partyView = hud.AddComponent<PartyView>();

            var combatFx = gameObject.AddComponent<CombatFx>();
            var serializedFx = new SerializedObject(combatFx);
            void Fx(string field, string path) => serializedFx.FindProperty(field).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Fx("arrowPrefab", WorldAssets.Arrow); Fx("boltPrefab", WorldAssets.FireSmall); Fx("impactSmallPrefab", WorldAssets.ImpactSmall); Fx("impactLargePrefab", WorldAssets.ImpactLarge);
            Fx("healPrefab", WorldAssets.Heal); Fx("missPrefab", WorldAssets.MissDust); Fx("boltImpactPrefab", WorldAssets.BoltImpact);
            serializedFx.ApplyModifiedPropertiesWithoutUndo();

            var game = gameObject.AddComponent<VikingGame>();
            var serializedGame = new SerializedObject(game);
            void Set(string name, Object value) => serializedGame.FindProperty(name).objectReferenceValue = value;
            Set("ruleset", AssetDatabase.LoadAssetAtPath<VikingRaidRulesetProvider>(RulesetPath));
            Set("anchors", anchorsRoot.GetComponent<WorldAnchors>());
            Set("inputSource", input);
            Set("hudRoot", hudRoot); Set("abilityBarView", abilityBar); Set("turnOrderView", turnOrder); Set("partyView", partyView);
            Set("cameraRig", cameraRig); Set("fx", combatFx);
            serializedGame.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
