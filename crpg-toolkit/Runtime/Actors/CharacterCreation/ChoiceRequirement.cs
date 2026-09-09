using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>
    /// One thing the player must pick before a character build is complete -- e.g. High Elf's "choose 1 Wizard
    /// cantrip". <see cref="Options"/> is the explicit candidate pool when the author knows it upfront; when
    /// it's empty, a presenter resolves candidates dynamically against <see cref="FilterTag"/> (e.g. querying
    /// every AbilityDefinition tagged "WizardCantrip" once tagging exists) rather than this class needing to
    /// know how to query anything itself.
    /// </summary>
    [Serializable]
    public sealed class ChoiceRequirement
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private ChoiceType type;
        [SerializeField] private List<DefinitionId> options = new();
        [SerializeField] private string filterTag;
        [SerializeField] private int count = 1;

        /// <summary>Stable key used to match a resolved choice back to this requirement.</summary>
        public string Id => id;
        public string DisplayName => displayName;
        public ChoiceType Type => type;
        public IReadOnlyList<DefinitionId> Options => options;
        public string FilterTag => filterTag;
        public int Count => count;
    }
}
