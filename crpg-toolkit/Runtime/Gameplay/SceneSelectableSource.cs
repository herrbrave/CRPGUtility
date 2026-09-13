using System.Collections.Generic;

namespace CRPG.Gameplay
{
    /// <summary>Production <see cref="ISelectableSource"/>, backed by <see cref="CharacterAgent.AllLive"/>.
    /// Filters out any entry that's been destroyed since it registered -- Unity's overridden null-check makes
    /// a destroyed MonoBehaviour compare equal to null, so this is a real, not merely defensive, check: a
    /// character destroyed mid-drag is silently excluded rather than throwing.</summary>
    public sealed class SceneSelectableSource : ISelectableSource
    {
        public IEnumerable<SelectableEntity> GetSelectables()
        {
            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent == null) continue;
                yield return new SelectableEntity(agent.EntityId, agent.transform.position);
            }
        }
    }
}
