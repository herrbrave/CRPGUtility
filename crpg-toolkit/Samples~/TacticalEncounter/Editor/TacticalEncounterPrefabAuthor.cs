using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Samples.TacticalEncounter.Editor
{
    /// <summary>Authors the sample's two combatant prefabs -- plain Unity primitives, same shape as
    /// BasicCrpgCharacterAuthor/CharacterCreatorPrefabAuthor.</summary>
    internal static class TacticalEncounterPrefabAuthor
    {
        private const string PrefabFolder = "Packages/com.crpgtoolkit.core/Samples/TacticalEncounter/Content/Prefabs";

        [MenuItem("CRPG Toolkit/Samples/Tactical Encounter/2. Author Combatant Prefabs")]
        public static void AuthorPrefabs()
        {
            EnsureFolder(PrefabFolder);

            AuthorPrefab("Hero", new Color(0.2f, 0.45f, 0.95f));
            AuthorPrefab("Foe", new Color(0.85f, 0.25f, 0.2f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TacticalEncounterPrefabAuthor] Authored Hero/Foe under {PrefabFolder}.");
        }

        private static void AuthorPrefab(string name, Color color)
        {
            var root = new GameObject(name);
            try
            {
                var capsuleCollider = root.AddComponent<CapsuleCollider>();
                capsuleCollider.height = 2f;
                capsuleCollider.radius = 0.4f;
                capsuleCollider.center = new Vector3(0f, 1f, 0f);

                var rigidbody = root.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;

                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.speed = 4f;

                root.AddComponent<NavMeshCharacterMotor>();
                root.AddComponent<FacingController>();
                root.AddComponent<CharacterAgent>();

                var visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(root.transform, false);

                var model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                model.name = "Model";
                model.transform.SetParent(visualRoot.transform, false);
                model.transform.localPosition = new Vector3(0f, 1f, 0f);

                Object.DestroyImmediate(model.GetComponent<Collider>());

                var renderer = model.GetComponent<MeshRenderer>();
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
                renderer.sharedMaterial = material;

                visualRoot.AddComponent<CharacterView>();

                var path = $"{PrefabFolder}/{name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var folderName = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, folderName);
        }
    }
}
