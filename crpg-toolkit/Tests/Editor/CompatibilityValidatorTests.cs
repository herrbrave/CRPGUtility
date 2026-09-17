using System.Collections.Generic;
using CRPG.Editor.Validation;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class CompatibilityValidatorTests
    {
        [Test]
        public void EvaluateUnityVersionReturnsNothingOnAnExactMatch()
        {
            var results = CompatibilityValidator.EvaluateUnityVersion("package.json", "6000.6", "0f1", "6000.6.0f1");

            Assert.IsEmpty(results);
        }

        [Test]
        public void EvaluateUnityVersionWarnsOnASameMajorMinorDifferentRelease()
        {
            var results = CompatibilityValidator.EvaluateUnityVersion("package.json", "6000.6", "0f1", "6000.6.1f1");

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Warning, results[0].Severity);
        }

        [Test]
        public void EvaluateUnityVersionErrorsOnADifferentMajorMinor()
        {
            var results = CompatibilityValidator.EvaluateUnityVersion("package.json", "6000.6", "0f1", "2022.3.0f1");

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Error, results[0].Severity);
        }

        [Test]
        public void EvaluateDependenciesReturnsNothingWhenEveryDeclaredVersionIsInstalledExactly()
        {
            var declared = new List<(string Name, string Version)> { ("com.unity.inputsystem", "1.20.0") };
            var installed = new Dictionary<string, string> { ["com.unity.inputsystem"] = "1.20.0" };

            var results = CompatibilityValidator.EvaluateDependencies(declared, installed);

            Assert.IsEmpty(results);
        }

        [Test]
        public void EvaluateDependenciesErrorsWhenADeclaredDependencyIsNotInstalledAtAll()
        {
            var declared = new List<(string Name, string Version)> { ("com.unity.render-pipelines.universal", "17.6.0") };
            var installed = new Dictionary<string, string>();

            var results = CompatibilityValidator.EvaluateDependencies(declared, installed);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Error, results[0].Severity);
            Assert.AreEqual("com.unity.render-pipelines.universal", results[0].Field);
        }

        [Test]
        public void EvaluateDependenciesWarnsWhenAnInstalledVersionDiffersFromWhatsDeclared()
        {
            var declared = new List<(string Name, string Version)> { ("com.unity.ai.navigation", "2.0.14") };
            var installed = new Dictionary<string, string> { ["com.unity.ai.navigation"] = "2.0.5" };

            var results = CompatibilityValidator.EvaluateDependencies(declared, installed);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Warning, results[0].Severity);
        }

        [Test]
        public void ExtractJsonStringFieldFindsAKnownFieldAndIgnoresOthers()
        {
            const string json = "{\n  \"name\": \"com.crpgtoolkit.core\",\n  \"unity\": \"6000.6\",\n  \"unityRelease\": \"0f1\"\n}";

            Assert.AreEqual("6000.6", CompatibilityValidator.ExtractJsonStringField(json, "unity"));
            Assert.AreEqual("0f1", CompatibilityValidator.ExtractJsonStringField(json, "unityRelease"));
            Assert.IsNull(CompatibilityValidator.ExtractJsonStringField(json, "notAField"));
        }

        /// <summary>The one real, non-synthetic check: run the actual validator against THIS dev project.
        /// TestProject's Packages/manifest.json is expected to declare the exact same Unity version and
        /// dependency versions crpg-toolkit's own package.json requires -- if this ever goes red, either the
        /// package's requirements or TestProject's manifest have drifted out of sync for real, which is exactly
        /// the class of gap this validator exists to catch (see Docs/Verification/Task-5.3-BasicCRPG.md's URP
        /// finding).</summary>
        [Test]
        public void ValidateReturnsNoIssuesAgainstThisRealDevProject()
        {
            var results = CompatibilityValidator.Validate();

            CollectionAssert.IsEmpty(results, string.Join("\n", results));
        }
    }
}
