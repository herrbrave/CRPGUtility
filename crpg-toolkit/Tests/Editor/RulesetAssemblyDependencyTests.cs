using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// Task 4.1's "dependency test": proves the generic engine doesn't secretly depend on the SRD5 ruleset --
    /// no assembly except CRPG.Rules.SRD5 itself (and the dev-time CRPG.Editor/CRPG.Tests.EditMode/
    /// CRPG.Tests.PlayMode, which need it for authoring/testing the SRD5 content, Task 4.5 extending this to
    /// PlayMode once a real end-to-end tactical-encounter test needed to construct real SRD5 services;
    /// CRPG.Tests.Fixtures.TacticalEncounterVerification for the same reason, Task 5.3) references
    /// CRPG.Rules.SRD5. Removing the SRD5 assembly wholesale would therefore leave every shipped runtime
    /// assembly still compilable. No prior test asserted assembly-reference structure in this repo; this is
    /// new infrastructure, not an extension of an existing pattern.
    /// </summary>
    public class RulesetAssemblyDependencyTests
    {
        private const string Srd5AssemblyName = "CRPG.Rules.SRD5";

        // "Assembly-CSharp"/"Assembly-CSharp-Editor" are TestProject's dogfood composition roots (e.g.
        // AICombatDemoBootstrap, AdventureSliceBootstrap) -- a consuming project choosing to reference a
        // concrete ruleset it has decided to use is exactly the intended shape, not a violation. This test is
        // about the crpg-toolkit PACKAGE's own generic assemblies never depending on SRD5, not about every
        // consumer everywhere.
        private static readonly string[] Whitelist =
        {
            Srd5AssemblyName, "CRPG.Editor", "CRPG.Tests.EditMode", "CRPG.Tests.PlayMode",
            "CRPG.Tests.Fixtures.TacticalEncounterVerification",
            "Assembly-CSharp", "Assembly-CSharp-Editor",
        };

        [Test]
        public void NoGenericRuntimeAssemblyReferencesTheSRD5Assembly()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);

            var offendingAssemblies = assemblies
                .Where(assembly => !Whitelist.Contains(assembly.name))
                .Where(assembly => assembly.assemblyReferences.Any(reference => reference.name == Srd5AssemblyName))
                .Select(assembly => assembly.name)
                .ToList();

            Assert.IsEmpty(offendingAssemblies,
                $"These assemblies reference {Srd5AssemblyName} and shouldn't: {string.Join(", ", offendingAssemblies)}.");
        }

        /// <summary>Names the specific assemblies the engine's own generic layers live in, so a future
        /// accidental reference from any of them fails this test by name instead of only being caught by the
        /// broader "nothing outside the whitelist" check above.</summary>
        [Test]
        public void GenericEngineAssembliesSpecificallyDoNotReferenceSRD5()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .ToDictionary(assembly => assembly.name);

            foreach (var name in new[] { "CRPG.Dialogue", "CRPG.Gameplay", "CRPG.Actors", "CRPG.Combat", "CRPG.Core" })
            {
                if (!assemblies.TryGetValue(name, out var assembly)) continue; // not part of this build target; nothing to assert

                var referencesSrd5 = assembly.assemblyReferences.Any(reference => reference.name == Srd5AssemblyName);
                Assert.IsFalse(referencesSrd5, $"{name} must never reference {Srd5AssemblyName}.");
            }
        }
    }
}
