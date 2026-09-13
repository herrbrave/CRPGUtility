using System.Collections;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Navigation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>Task 3.3: proves UseDoorInteractionAdapter changes REAL NavMesh traversability (a carving
    /// NavMeshObstacle sized across a doorway gap), not just a bool flag -- a bare Collider toggle wouldn't
    /// prove this, which is exactly why the adapter uses NavMeshObstacle instead.</summary>
    public class UseDoorInteractionAdapterTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            NavMesh.RemoveAllNavMeshData();
        }

        private GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }

        private static GameObject CreateGroundPlane(Vector3 center, float halfSize)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = center;
            plane.transform.localScale = new Vector3(halfSize / 5f, 1f, halfSize / 5f);
            plane.isStatic = true;
            return plane;
        }

        private static GameObject CreateStaticWall(Vector3 center, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = center;
            wall.transform.localScale = size;
            wall.isStatic = true;
            return wall;
        }

        [UnityTest]
        public IEnumerator ClosingTheDoorBlocksThePathAndOpeningItRestoresPath()
        {
            // A unique, far-away origin -- confirmed live that this test passes in isolation but fails when run
            // as part of the full suite at Vector3.zero, the same class of cross-test NavMesh-at-the-same-
            // coordinates contamination PointerCommandTests' own "every test its own far-apart origin" comment
            // already documents for Physics/colliders. NavMesh.RemoveAllNavMeshData() between tests evidently
            // isn't a strong enough guarantee against another test's bake at the identical world location.
            var origin = new Vector3(4000f, 0f, 4000f);

            // Ground is exactly as wide as the walls' combined span (x = +/-10) -- there is deliberately no
            // ground beyond the walls' outer edges, so a path can never go AROUND them; the only way through
            // is the doorway gap itself (x in roughly [-1, 1] at z = 0).
            Track(CreateGroundPlane(origin, 10f));
            Track(CreateStaticWall(origin + new Vector3(-5.5f, 1f, 0f), new Vector3(9f, 2f, 1f)));
            Track(CreateStaticWall(origin + new Vector3(5.5f, 1f, 0f), new Vector3(9f, 2f, 1f)));

            var surface = Track(new GameObject("NavMeshSurface")).AddComponent<NavMeshSurface>();
            surface.BuildNavMesh();

            var doorGo = Track(new GameObject("Door"));
            doorGo.SetActive(false); // defer Awake -- UseDoorInteractionAdapter's [RequireComponent] auto-adds
                                      // WorldInteractable, which requires its anchor configured before Awake runs
            doorGo.transform.position = origin + new Vector3(0f, 1f, 0f);
            var obstacle = doorGo.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false; // carve immediately -- don't wait for Unity's "has been
                                                   // stationary for a while" heuristic, irrelevant for a door
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(2.4f, 2f, 1.2f);
            var adapter = doorGo.AddComponent<UseDoorInteractionAdapter>();
            typeof(UseDoorInteractionAdapter)
                .GetField("obstacle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(adapter, obstacle);

            var anchorGo = Track(new GameObject("DoorAnchor"));
            anchorGo.transform.position = doorGo.transform.position + new Vector3(0f, 0f, -3f);
            doorGo.GetComponent<WorldInteractable>().Configure(anchorGo.transform, 1.5f);
            doorGo.SetActive(true);

            var from = origin + new Vector3(0f, 0f, -8f);
            var to = origin + new Vector3(0f, 0f, 8f);
            var navMeshService = new NavMeshService();

            // Carving updates over real elapsed time, not a fixed frame count (the same "budget by wall-clock
            // time, not frame count" lesson CharacterMotionTests already learned for pathfinding resolution).
            var blocked = false;
            var elapsed = 0f;
            while (elapsed < 30f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (!navMeshService.TryCalculatePathLength(from, to, out _)) { blocked = true; break; }
            }

            Assert.IsTrue(blocked, "A closed door's carving obstacle must genuinely block the only path through the gap.");

            InteractionExecutionResult? result = null;
            adapter.BeginExecute(RuntimeEntityId.NewId(), r => result = r);
            Assert.AreEqual(InteractionExecutionResult.Succeeded, result);
            Assert.IsTrue(adapter.IsOpen);

            var reopened = false;
            elapsed = 0f;
            while (elapsed < 5f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (navMeshService.TryCalculatePathLength(from, to, out _)) { reopened = true; break; }
            }

            Assert.IsTrue(reopened, "Opening the door must remove the carve and restore a real path through the gap.");
        }
    }
}
