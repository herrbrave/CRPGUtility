using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace CRPG.Gameplay
{
    /// <summary>
    /// An <see cref="ISelectableSource"/> over <see cref="CharacterAgent.AllLive"/> restricted to one scene.
    /// The global <see cref="SceneSelectableSource"/> yields every live character in every loaded scene, which is
    /// right for drag-select but wrong for a location map: a party member (or NPC) standing in an additive
    /// background location must never show up on this location's map. LocationInitializer moves each location's
    /// characters into that location's scene, so the agent's own scene is the filter.
    /// </summary>
    public sealed class SceneLocalSelectableSource : ISelectableSource
    {
        private readonly Scene scene;

        public SceneLocalSelectableSource(Scene scene) => this.scene = scene;

        public IEnumerable<SelectableEntity> GetSelectables()
        {
            if (!scene.IsValid() || !scene.isLoaded) yield break;

            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent == null || agent.gameObject.scene != scene) continue;
                yield return new SelectableEntity(agent.EntityId, agent.transform.position);
            }
        }
    }
}
