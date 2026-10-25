using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>The second way to qualify for Sneak Attack: another enemy of the target (an ally of the attacker) is next to it. By geometry, like flanking: any conscious ally of the attacker within reach of the target counts, wherever the attacker itself stands (a bow works from afar).</summary>
    public class PositionalAllyAdjacencyQueryTests
    {
        private sealed class Spots : IPositionProvider
        {
            public readonly Dictionary<RuntimeEntityId, Vector3> At = new();
            public Vector3 GetPosition(RuntimeEntityId entity) => At[entity];
        }

        private static (PositionalAllyAdjacencyQuery Query, Spots Spots, RuntimeEntityId Attacker, RuntimeEntityId Ally, RuntimeEntityId Target, HashSet<RuntimeEntityId> Down) Setup()
        {
            var spots = new Spots(); var attacker = RuntimeEntityId.NewId(); var ally = RuntimeEntityId.NewId(); var target = RuntimeEntityId.NewId(); var down = new HashSet<RuntimeEntityId>();
            spots.At[target] = new Vector3(10f, 0f, 10f); spots.At[attacker] = new Vector3(10f, 0f, 20f); spots.At[ally] = new Vector3(11f, 0f, 10f);
            return (new PositionalAllyAdjacencyQuery(spots, id => new[] { attacker, ally }.Where(a => a != id || id == attacker), id => !down.Contains(id)), spots, attacker, ally, target, down);
        }

        [Test]
        public void AnAllyBesideTheTargetCounts_EvenWhenTheAttackerShootsFromFarAway()
        {
            var (query, _, attacker, _, target, _) = Setup();
            Assert.IsTrue(query.IsAllyAdjacentToTarget(attacker, target));
        }

        [Test]
        public void AnAllyFarFromTheTargetDoesNot()
        {
            var (query, spots, attacker, ally, target, _) = Setup();
            spots.At[ally] = new Vector3(10f, 0f, 16f);
            Assert.IsFalse(query.IsAllyAdjacentToTarget(attacker, target));
        }

        [Test]
        public void ADownedAllyDoesNotThreatenAndTheAttackerItselfIsNeverItsOwnAlly()
        {
            var (query, spots, attacker, ally, target, down) = Setup();
            down.Add(ally); Assert.IsFalse(query.IsAllyAdjacentToTarget(attacker, target), "a downed ally");
            down.Clear(); spots.At[ally] = new Vector3(10f, 0f, 20f); spots.At[attacker] = new Vector3(10.5f, 0f, 10f);
            Assert.IsFalse(query.IsAllyAdjacentToTarget(attacker, target), "the attacker standing beside the target is not an ally of itself");
        }
    }
}
