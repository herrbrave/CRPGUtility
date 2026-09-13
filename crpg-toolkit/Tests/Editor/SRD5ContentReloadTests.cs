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
    }
}
