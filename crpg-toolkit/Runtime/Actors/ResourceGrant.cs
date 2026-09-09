using System;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>A resource pool a FeatureDefinition grants (e.g. a Fighter's Second Wind granting 1 use, or a
    /// class granting its Ki pool). Direct ScriptableObject reference, not a DefinitionId -- both types live
    /// in CRPG.Actors, so there's no cross-assembly reason to indirect through an id here.</summary>
    [Serializable]
    public struct ResourceGrant
    {
        [SerializeField] private ResourceDefinition resource;
        [SerializeField] private int maxAmount;

        public readonly ResourceDefinition Resource => resource;
        public readonly int MaxAmount => maxAmount;
    }
}
