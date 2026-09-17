using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Samples.BasicCRPG.Editor
{
    /// <summary>
    /// Authors the sample's two party-member prefabs -- plain Unity primitives, mirroring ConsumerGame's
    /// (Gate D) ConsumerCharacterAuthor exactly: a CapsuleCollider is the one real gameplay collider, the
    /// visual capsule underneath is purely cosmetic and has its own auto-attached collider removed so the two
    /// never double up.
    /// </summary>
    internal static class BasicCrpgCharacterAuthor
    {
        private const string PrefabFolder = "Packages/com.crpgtoolkit.core/Samples/BasicCRPG/Content/Prefabs";

        [MenuItem("CRPG Toolkit/Samples/Basic CRPG/1. Author Character Prefabs")]
        public static void AuthorPrefabs()
        {
            EnsureFolder(PrefabFolder);

            AuthorPrefab("Hero1", new Color(0.2f, 0.45f, 0.95f));
            AuthorPrefab("Hero2", new Color(0.25f, 0.75f, 0.35f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BasicCrpgCharacterAuthor] Authored Hero1/Hero2 under {PrefabFolder}.");
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
