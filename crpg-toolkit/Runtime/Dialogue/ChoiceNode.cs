using System;
using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Dialogue
{
    [Serializable]
    public sealed class ChoiceNode : DialogueNode
    {
        [SerializeField] private List<ChoiceOption> options = new();

        public IReadOnlyList<ChoiceOption> Options => options;
    }
}
