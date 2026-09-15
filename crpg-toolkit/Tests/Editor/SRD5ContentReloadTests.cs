using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEditor;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// Task 4.1's "serialized-asset reload fixture": proves the SRD5 policy-class migration (CRPG.Actors/
    /// CRPG.Combat -> CRPG.Rules.SRD5, namespace only, same class names) didn't silently disturb the ~200
    /// already-authored SRD5 content assets under Rules/SRD5e2014/Content/ -- loads real assets straight off
    /// disk via AssetDatabase (not a fixture built in memory) and confirms they still resolve correctly.
    /// Modeled on CharacterWrapperBuilderTests' GUID-identity-preservation pattern (the closest existing
    /// precedent for "prove a real asset survives a move/rebuild"), adapted for a ScriptableObject definition
    /// asset rather than a prefab.
    /// </summary>
    public class SRD5ContentReloadTests
    {
        private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

        [Test]
        public void RealStrengthStatDefinitionAssetLoadsAndResolvesAfterTheSRD5Move()
        {
            var strength = AssetDatabase.LoadAssetAtPath<StatDefinition>($"{ContentFolder}/StatDefinition_Strength.asset");
            Assert.IsNotNull(strength, "StatDefinition_Strength.asset failed to load -- did the SRD5 migration touch a definition type by mistake?");

            var database = new RulesDatabase();
            database.Register(strength);

            Assert.IsTrue(database.TryResolve<StatDefinition>(strength.Id, out var resolved));
            Assert.AreSame(strength, resolved);
        }

        /// <summary>Every real top-level content asset (excluding Content/Oakhaven/, which is fixture-specific
        /// content for one demo, not core SRD5 rules content, and duplicates at least one top-level id) --
        /// loads and registers without throwing, and every DefinitionId is unique.</summary>
        [Test]
        public void EveryRealSRD5ContentAssetLoadsAndRegistersWithoutError()
        {
            var paths = AssetDatabase.FindAssets("t:DefinitionAsset", new[] { ContentFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !path.Contains("/Oakhaven/"))
                .ToList();
            Assert.Greater(paths.Count, 100, "Expected the full SRD5 content set to still be discoverable under its Content folder.");

            var database = new RulesDatabase();
            foreach (var path in paths)
            {
                var definition = AssetDatabase.LoadAssetAtPath<DefinitionAsset>(path);
                Assert.IsNotNull(definition, $"Failed to load DefinitionAsset at '{path}'.");
                Assert.DoesNotThrow(() => database.Register(definition), $"Failed to register '{path}'.");
            }
        }

        /// <summary>Task 4.5: the first real content asset authored with a non-None DurationBoundary (Task
        /// 4.4's own field) -- verifies the real, saved asset on disk, not a hand-built fixture, so this
        /// regresses loudly if the asset is ever re-authored and the field silently drops again.</summary>
        [Test]
        public void CommandsOnFailureConditionCarriesARealNonNoneBoundary()
        {
            var command = AssetDatabase.LoadAssetAtPath<CRPG.Abilities.AbilityDefinition>($"{ContentFolder}/AbilityDefinition_Command.asset");
            Assert.IsNotNull(command, "AbilityDefinition_Command.asset failed to load.");

            var savingThrow = command.Effects[0];
            var onFailure = savingThrow.GetChildEffects().FirstOrDefault();
            Assert.IsNotNull(onFailure, "Command's SavingThrowEffect must still have an OnFailure branch.");

            var boundaryField = onFailure.GetType().GetField("boundary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(boundaryField, "ApplyConditionEffect.boundary field not found -- did the field get renamed?");
            var boundary = (CRPG.Actors.DurationBoundary)boundaryField.GetValue(onFailure);

            Assert.AreEqual(CRPG.Actors.DurationBoundary.EndOfTargetTurn, boundary,
                "Command's own description says the effect lasts through the target's next turn -- must not silently regress to None (never expiring on its own).");
        }
    }
}
