using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// Task 5.3: the validator entrypoint half of the package's supported-version/API/migration policy (see
    /// <c>COMPATIBILITY.md</c> at the package root for the policy text itself). Checks the CURRENT project's
    /// actually-resolved Unity version and package dependency versions against what this package's own
    /// <c>package.json</c> declares -- the exact class of gap that let BasicCRPG's authored materials render
    /// pink in a genuinely fresh project (URP never declared as a dependency at all; see
    /// <c>Docs/Verification/Task-5.3-BasicCRPG.md</c>). Deliberately reads the live, resolved
    /// <see cref="PackageInfo"/> for this package (via <see cref="PackageInfo.FindForAssembly"/>) rather than
    /// re-parsing package.json's <c>dependencies</c> object by hand -- <see cref="PackageInfo.dependencies"/>
    /// already mirrors it exactly. The Unity editor/release fields aren't exposed on <see cref="PackageInfo"/>,
    /// so those two are read directly from package.json's own text (a small, targeted string scan, not a
    /// general-purpose JSON parser -- package.json's shape is controlled by this project, not external input).
    /// <para>
    /// The actual comparison logic (<see cref="EvaluateUnityVersion"/>/<see cref="EvaluateDependencies"/>) is
    /// pure -- plain strings/dictionaries in, <see cref="ValidationResult"/>s out -- specifically so it's
    /// unit-testable with synthetic version combinations without needing a second real Unity install or a
    /// different project's manifest.json to exercise the mismatch/missing branches. <see cref="Validate"/>
    /// itself just gathers the real environment's data and hands it to those.
    /// </para>
    /// </summary>
    public static class CompatibilityValidator
    {
        public static List<ValidationResult> Validate()
        {
            var results = new List<ValidationResult>();

            var packageInfo = PackageInfo.FindForAssembly(typeof(CompatibilityValidator).Assembly);
            if (packageInfo == null)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, "package.json", "Resolve",
                    "Could not resolve this package's own PackageInfo -- is it installed as a real UPM package (not loose files under Assets/)?",
                    "Reference com.crpgtoolkit.core via Packages/manifest.json, not by copying its files into Assets/."));
                return results;
            }

            var packageJsonPath = Path.Combine(packageInfo.assetPath, "package.json").Replace('\\', '/');

            string requiredUnity = null, requiredRelease = null;
            try
            {
                var text = File.ReadAllText(packageJsonPath);
                requiredUnity = ExtractJsonStringField(text, "unity");
                requiredRelease = ExtractJsonStringField(text, "unityRelease");
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(ValidationSeverity.Warning, packageJsonPath, "unity",
                    $"Could not read package.json to check the required Unity version: {ex.Message}",
                    "Confirm the package was installed normally (file:/git/registry), not with package.json missing or unreadable."));
            }

            if (!string.IsNullOrEmpty(requiredUnity))
                results.AddRange(EvaluateUnityVersion(packageJsonPath, requiredUnity, requiredRelease, Application.unityVersion));

            var installed = new Dictionary<string, string>();
            foreach (var resolved in PackageInfo.GetAllRegisteredPackages())
                installed[resolved.name] = resolved.version;

            var declared = new List<(string Name, string Version)>();
            foreach (var dependency in packageInfo.dependencies)
                declared.Add((dependency.name, dependency.version));

            results.AddRange(EvaluateDependencies(declared, installed));

            return results;
        }

        /// <summary>Pure: given what package.json declares and what Unity reports as actually running, returns
        /// zero results on an exact match, a Warning on a same-major.minor patch/release mismatch (likely fine,
        /// just not independently verified), or an Error on a different major.minor entirely.</summary>
        public static List<ValidationResult> EvaluateUnityVersion(string assetPath, string requiredUnity, string requiredRelease, string actualVersion)
        {
            var results = new List<ValidationResult>();

            // Application.unityVersion is e.g. "6000.6.0f1" -- the "unity"/"unityRelease" split in package.json
            // is "6000.6" / "0f1" (Unity's own package.json convention), so re-join with the same separator
            // this project's own package.json already uses ("." between unity and unityRelease) to compare.
            var declaredFull = string.IsNullOrEmpty(requiredRelease) ? requiredUnity : $"{requiredUnity}.{requiredRelease}";
            if (actualVersion == declaredFull) return results;

            if (actualVersion.StartsWith(requiredUnity, StringComparison.Ordinal))
            {
                results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "unity",
                    $"Declares Unity {declaredFull}; this project is on {actualVersion} -- same major.minor, different patch/release. Likely fine, not independently verified against this exact release.",
                    "Re-run the package's own test suite on this Unity release before shipping; update package.json's \"unityRelease\" once confirmed."));
            }
            else
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "unity",
                    $"Declares Unity {declaredFull}; this project is on {actualVersion} -- different major.minor version, not the one this package is verified against.",
                    $"Use Unity {requiredUnity}.x, or verify the package on {actualVersion} yourself and update package.json's \"unity\"/\"unityRelease\" once confirmed."));
            }

            return results;
        }

        /// <summary>Pure: an Error for a declared dependency missing entirely from <paramref name="installed"/>,
        /// a Warning for one present but at a different version, nothing for an exact match.</summary>
        public static List<ValidationResult> EvaluateDependencies(IReadOnlyList<(string Name, string Version)> declared, IReadOnlyDictionary<string, string> installed)
        {
            var results = new List<ValidationResult>();
            const string path = "Packages/manifest.json";

            foreach (var dependency in declared)
            {
                if (!installed.TryGetValue(dependency.Name, out var installedVersion))
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, path, dependency.Name,
                        $"Declared as a dependency (version {dependency.Version}) but is not installed in this project at all.",
                        $"Add \"{dependency.Name}\": \"{dependency.Version}\" to Packages/manifest.json."));
                    continue;
                }

                if (installedVersion == dependency.Version) continue;

                results.Add(new ValidationResult(ValidationSeverity.Warning, path, dependency.Name,
                    $"Declares {dependency.Version}; this project has {installedVersion} installed -- this exact combination isn't independently verified.",
                    $"Match the declared version ({dependency.Version}) unless you've already confirmed {installedVersion} works, in which case update package.json to match."));
            }

            return results;
        }

        public static string ExtractJsonStringField(string json, string fieldName)
        {
            var key = $"\"{fieldName}\"";
            var keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0) return null;

            var colonIndex = json.IndexOf(':', keyIndex + key.Length);
            if (colonIndex < 0) return null;

            var valueStart = json.IndexOf('"', colonIndex + 1);
            if (valueStart < 0) return null;
            var valueEnd = json.IndexOf('"', valueStart + 1);
            if (valueEnd < 0) return null;

            return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        }
    }
}
