using System;
using CRPG.Actors;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// A generic resource cost. Action, Bonus Action, Reaction, Movement, a spell slot, hit dice, a class
    /// resource, an item charge -- the engine treats all of them identically as "spend N of ResourceDefinition
    /// X"; a ruleset decides what to call each ResourceDefinition and what having none left means.
    /// </summary>
    [Serializable]
    public struct AbilityCost
    {
        [SerializeField] private ResourceDefinition resource;
        [SerializeField] private int amount;

        public readonly ResourceDefinition Resource => resource;
        public readonly int Amount => amount;
    }
}
