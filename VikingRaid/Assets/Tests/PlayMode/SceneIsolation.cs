using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace VikingRaid.Tests
{
    /// <summary>
    /// Leaves an EMPTY scene behind a test class that loaded the game world. PlayMode tests share one session, and a test that does not load a scene runs in whatever the previous one left:
    /// the older tests (the roster, the cues, the animation spikes) build their own NavMesh over every loaded scene, and the world's static-batched meshes are not readable at runtime
    /// ("RuntimeNavMeshBuilder: Source mesh Combined Mesh ... does not allow read access"). Found only when the whole suite ran together.
    /// </summary>
    internal static class SceneIsolation
    {
        public static IEnumerator UnloadAll()
        {
            var empty = SceneManager.CreateScene("EmptyBetweenTests");
            SceneManager.SetActiveScene(empty);
            var others = new List<Scene>();
            for (var i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s != empty && s.isLoaded) others.Add(s); }
            foreach (var s in others) yield return SceneManager.UnloadSceneAsync(s);
            yield return null;
        }
    }
}
