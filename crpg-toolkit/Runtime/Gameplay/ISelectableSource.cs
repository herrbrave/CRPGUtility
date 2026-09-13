using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>A live, currently-selectable entity and where it is right now -- enough for drag-box hit
    /// testing (screen-space containment), nothing more.</summary>
    public readonly struct SelectableEntity
    {
        public SelectableEntity(RuntimeEntityId id, Vector3 worldPosition)
        {
            Id = id;
            WorldPosition = worldPosition;
        }

        public RuntimeEntityId Id { get; }
        public Vector3 WorldPosition { get; }
    }

    /// <summary>What <see cref="PlayerCommandRouter"/> enumerates to resolve a drag-box selection. Kept
    /// deliberately minimal (id + position only) so it's trivial to fake in tests -- the production
    /// implementation (<see cref="SceneSelectableSource"/>) is the only place that needs to know these come
    /// from live <see cref="CharacterAgent"/> instances.</summary>
    public interface ISelectableSource
    {
        IEnumerable<SelectableEntity> GetSelectables();
    }
}
