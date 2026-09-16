using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Consumer.Game.Editor
{
    /// <summary>
    /// Gate D's "distinct art": every character in this project is a plain Unity primitive, never an imported
    /// model -- proves <see cref="CharacterView"/>/<see cref="CharacterAgent"/>'s binding contract is genuinely
    /// rig-agnostic (confirmed by reading the package source, not assumed: <c>CharacterView.BindModel</c> only
    /// touches <c>Animator</c> when one exists, and <c>CharacterAgent.Bind</c> only needs <c>IMovable</c>/
    /// <c>CharacterView</c>/<c>FacingController</c> on the root). Mirrors
    /// <c>TestProject/Assets/Editor/BaseHumanMaleCharacterAuthor.cs</c>'s exact component order, minus the
    /// equipment/animation components this compact game doesn't need.
    /// </summary>
    internal static class ConsumerCharacterAuthor
    {
        private const string PrefabFolder = "Assets/Game/Prefabs";

        [MenuItem("Consumer Game/Author Character Prefabs")]
        public static void AuthorPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Game")) AssetDatabase.CreateFolder("Assets", "Game");
                AssetDatabase.CreateFolder("Assets/Game", "Prefabs");
            }

            AuthorPrefab("PartyCapsule", new Color(0.2f, 0.45f, 0.95f));
            AuthorPrefab("HostileCapsule", new Color(0.85f, 0.2f, 0.2f));
            AuthorPrefab("NpcCapsule", new Color(0.25f, 0.75f, 0.35f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ConsumerCharacterAuthor] Authored PartyCapsule/HostileCapsule/NpcCapsule under " + PrefabFolder);
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

                // GameObject.CreatePrimitive auto-attaches its own CapsuleCollider -- CharacterRoot already
                // owns the one real gameplay collider, so this one must go or the two would silently double up.
                Object.DestroyImmediate(model.GetComponent<Collider>());

                var renderer = model.GetComponent<MeshRenderer>();
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
                renderer.sharedMaterial = material;

                // Added last, so its Awake() finds Model already parented -- same ordering rule
                // BaseHumanMaleCharacterAuthor.cs documents for the exact same reason.
                visualRoot.AddComponent<CharacterView>();

                var path = $"{PrefabFolder}/{name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
