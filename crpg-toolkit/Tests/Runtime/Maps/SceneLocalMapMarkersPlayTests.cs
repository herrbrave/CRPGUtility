using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Maps;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>Real CharacterAgents in two additive scenes: a location's map must only ever see the actors in
    /// its own scene, never those of a background location, even though CharacterAgent.AllLive holds both.</summary>
    public class SceneLocalMapMarkersPlayTests
    {
        private readonly List<Scene> scenes = new();
        private GameSession session;

        [SetUp]
        public void SetUp() => session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));

        [TearDown]
        public void TearDown()
        {
            foreach (var scene in scenes)
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            scenes.Clear();
        }

        private Scene NewScene(string name)
        {
            var scene = SceneManager.CreateScene(name + "_" + System.Guid.NewGuid().ToString("N"));
            scenes.Add(scene);
            return scene;
        }

        private (GameObject go, RuntimeEntityId id) Actor(Scene scene, Vector3 position)
        {
            var go = new GameObject("Actor") { transform = { position = position } };
            SceneManager.MoveGameObjectToScene(go, scene);
            var agent = go.AddComponent<CharacterAgent>();
            var id = RuntimeEntityId.NewId();
            agent.Bind(id, session);
            return (go, id);
        }

        [UnityTest]
        public IEnumerator OnlyActorsInTheBoundSceneAreVisible()
        {
            var village = NewScene("village");
            var cave = NewScene("cave");
            var (_, inVillage) = Actor(village, new Vector3(5f, 0f, 6f));
            var (_, inCave) = Actor(cave, new Vector3(50f, 0f, 60f));
            yield return null;

            var ids = new SceneLocalSelectableSource(village).GetSelectables().Select(e => e.Id).ToList();

            CollectionAssert.Contains(ids, inVillage);
            CollectionAssert.DoesNotContain(ids, inCave);
        }

        [UnityTest]
        public IEnumerator APartyMemberInTheOtherSceneHasNoMarkerAndNoFocus()
        {
            var village = NewScene("village");
            var cave = NewScene("cave");
            var (_, here) = Actor(village, new Vector3(5f, 0f, 6f));
            var (_, away) = Actor(cave, new Vector3(50f, 0f, 60f));
            var party = new PartyState(new[] { here, away });
            party.Select(away); // the active character is the one standing in the other location
            yield return null;

            using var source = new PartyMapMarkerSource(party, new SceneLocalSelectableSource(village), null);
            var markers = new List<MapMarkerSnapshot>();
            source.Collect(markers);

            Assert.AreEqual(1, markers.Count);
            Assert.AreEqual(MapMarkerKeys.Party(here), markers[0].Key);
            Assert.IsFalse(source.TryGetFocusXZ(out _), "the active actor is elsewhere, so there is no honest focus");
        }

        [UnityTest]
        public IEnumerator ADestroyedActorDropsOutOfTheSourceWithoutThrowing()
        {
            var village = NewScene("village");
            var (go, id) = Actor(village, new Vector3(1f, 0f, 1f));
            yield return null;

            Object.Destroy(go);
            yield return null;

            var ids = new SceneLocalSelectableSource(village).GetSelectables().Select(e => e.Id).ToList();
            CollectionAssert.DoesNotContain(ids, id);
        }

        [UnityTest]
        public IEnumerator AnUnloadedSceneYieldsNothing()
        {
            var village = NewScene("village");
            Actor(village, Vector3.zero);
            yield return null;
            var source = new SceneLocalSelectableSource(village);

            yield return SceneManager.UnloadSceneAsync(village);

            Assert.IsEmpty(source.GetSelectables().ToList());
        }
    }
}
