using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>The features a class/subclass grants at a specific level (spec §55's level-progression table).</summary>
    [Serializable]
    public sealed class ClassLevelGrant
    {
        [SerializeField] private int level = 1;
        [SerializeField] private List<DefinitionId> featureIds = new();

        public int Level => level;
        public IReadOnlyList<DefinitionId> FeatureIds => featureIds;
    }
}
