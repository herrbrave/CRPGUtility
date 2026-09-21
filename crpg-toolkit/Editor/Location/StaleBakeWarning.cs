using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// When a scene that holds an already-baked location is saved, warns if its painted tiles changed since the bake (the same check
    /// <c>BakeFingerprint</c> makes when the location loads). A location that was never baked stays silent: it is still being built.
    /// </summary>
    [InitializeOnLoad]
    public static class StaleBakeWarning
    {
        static StaleBakeWarning() => EditorSceneManager.sceneSaved += OnSceneSaved;

        private static void OnSceneSaved(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.transform.Find(LocationBaker.BakedRootName) == null) continue;
                if (root.GetComponentsInChildren<Grid>(true).FirstOrDefault() == null) continue;
                if (LocationBaker.IsStale(root, out var message)) Debug.LogWarning($"[LocationBaker] {message}", root);
            }
        }
    }
}
