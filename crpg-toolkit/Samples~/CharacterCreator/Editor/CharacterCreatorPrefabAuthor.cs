using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Samples.CharacterCreator.Editor
{
    /// <summary>
    /// Authors the sample's one party-member prefab -- a plain Unity primitive, mirroring BasicCRPG's
    /// BasicCrpgCharacterAuthor exactly (same capsule/collider/rigidbody/NavMeshAgent/CharacterView shape).
    /// One prefab is enough here: CharacterCreator always spawns exactly one created character, never a party.
    /// </summary>
    internal static class CharacterCreatorPrefabAuthor
    {
        private const string PrefabFolder = "Packages/com.crpgtoolkit.core/Samples/CharacterCreator/Content/Prefabs";

        [MenuItem("CRPG Toolkit/Samples/Character Creator/1. Author Character Prefab")]
        public static void AuthorPrefab()
        {
            EnsureFolder(PrefabFolder);

            var root = new GameObject("Hero");
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
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.6f, 0.4f, 0.85f) };
                renderer.sharedMaterial = material;

                visualRoot.AddComponent<CharacterView>();

                var path = $"{PrefabFolder}/Hero.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CharacterCreatorPrefabAuthor] Authored Hero under {PrefabFolder}.");
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
