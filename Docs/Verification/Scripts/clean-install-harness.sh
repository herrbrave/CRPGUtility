#!/usr/bin/env bash
# Task 5.3 clean-install verification harness.
#
# Proves the package (and its samples) actually work OUTSIDE the dev environment -- a genuinely fresh Unity
# project that never had TestProject/ConsumerGame's own Library/ProjectSettings history, with crpg-toolkit
# added the same way a real consumer would (a `file:` package reference), samples imported through Unity's
# real Package Manager Sample-import codepath (not a hand copy), and behavioral fixture tests
# (crpg-toolkit/Tests/Fixtures/<SampleName>Verification/) run against the imported result. Built alongside
# BasicCRPG (Task 5.3 step 3); reused unchanged for CharacterCreator/TacticalEncounter.
#
# Usage:
#   clean-install-harness.sh package-only                 # steps 1-2: package alone, no samples
#   clean-install-harness.sh import "Basic CRPG"           # steps 3-5: import one sample, compile, add scenes to Build Settings
#   clean-install-harness.sh test <TestFilter>             # step 5: run one filtered PlayMode fixture
#   clean-install-harness.sh full-suite <EditMode|PlayMode> # run crpg-toolkit's own full suite standalone
#
# Every step is independently re-runnable; nothing here mutates the package itself.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe"
SCRATCH="$REPO_ROOT/_scratch/CleanInstallHarness"
LOGS="$SCRATCH/Logs"

ensure_project() {
  # A caller whose result depends on "no samples imported yet" (package-only) must pass fresh=1 -- reusing a
  # scratch project that a previous `import` run already populated would silently invalidate that claim.
  local fresh="${1:-0}"
  if [ "$fresh" = "1" ] && [ -d "$SCRATCH" ]; then
    echo "== Deleting existing scratch project so the package-only claim is actually sample-free =="
    rm -rf "$SCRATCH"
  fi
  if [ ! -d "$SCRATCH" ]; then
    echo "== Creating a genuinely fresh scratch project (not copied from TestProject/ConsumerGame) =="
    "$UNITY" -batchmode -nographics -quit -createProject "$SCRATCH" -logFile "$REPO_ROOT/_scratch/create.log"
    sed -i 's/activeInputHandler: 0/activeInputHandler: 1/' "$SCRATCH/ProjectSettings/ProjectSettings.asset"
  fi
  mkdir -p "$LOGS" "$SCRATCH/Assets/HarnessEditor"

  cat > "$SCRATCH/Packages/manifest.json" << 'EOF'
{
  "dependencies": {
    "com.crpgtoolkit.core": "file:../../../crpg-toolkit",
    "com.unity.ai.navigation": "2.0.14",
    "com.unity.ide.rider": "3.0.38",
    "com.unity.ide.visualstudio": "2.0.26",
    "com.unity.inputsystem": "1.20.0",
    "com.unity.render-pipelines.universal": "17.6.0",
    "com.unity.test-framework": "1.4.5",
    "com.unity.modules.accessibility": "1.0.0",
    "com.unity.modules.adaptiveperformance": "1.0.0",
    "com.unity.modules.ai": "1.0.0",
    "com.unity.modules.androidjni": "1.0.0",
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.assetbundle": "1.0.0",
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.cloth": "1.0.0",
    "com.unity.modules.director": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.particlesystem": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.modules.physicscore2d": "1.0.0",
    "com.unity.modules.screencapture": "1.0.0",
    "com.unity.modules.terrain": "1.0.0",
    "com.unity.modules.terrainphysics": "1.0.0",
    "com.unity.modules.tetgen": "1.0.0",
    "com.unity.modules.tilemap": "1.0.0",
    "com.unity.modules.timelinefoundation": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.umbra": "1.0.0",
    "com.unity.modules.unityanalytics": "1.0.0",
    "com.unity.modules.unitywebrequest": "1.0.0",
    "com.unity.modules.unitywebrequestassetbundle": "1.0.0",
    "com.unity.modules.unitywebrequestaudio": "1.0.0",
    "com.unity.modules.unitywebrequesttexture": "1.0.0",
    "com.unity.modules.unitywebrequestwww": "1.0.0",
    "com.unity.modules.vectorgraphics": "1.0.0",
    "com.unity.modules.vehicles": "1.0.0",
    "com.unity.modules.video": "1.0.0",
    "com.unity.modules.wind": "1.0.0",
    "com.unity.modules.xr": "1.0.0"
  },
  "testables": ["com.crpgtoolkit.core"]
}
EOF

  cat > "$SCRATCH/Assets/HarnessEditor/SampleImportHelper.cs" << 'EOF'
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

internal static class SampleImportHelper
{
    public static void ListSamples()
    {
        foreach (var s in Sample.FindByPackage("com.crpgtoolkit.core", null))
            Debug.Log($"[SampleImportHelper] Found sample: {s.displayName} isImported={s.isImported} path={s.resolvedPath}");
    }

    public static void ImportSample()
    {
        var sampleName = System.Environment.GetEnvironmentVariable("HARNESS_SAMPLE_NAME");
        var samples = Sample.FindByPackage("com.crpgtoolkit.core", null).ToList();
        var target = samples.FirstOrDefault(s => s.displayName == sampleName);
        if (target.displayName == null)
        {
            Debug.LogError($"[SampleImportHelper] No sample named '{sampleName}' found among {samples.Count} discovered samples.");
            return;
        }

        var ok = target.Import(Sample.ImportOptions.OverridePreviousImports);
        Debug.Log($"[SampleImportHelper] Import('{sampleName}') returned {ok}, isImported={target.isImported}, importPath={target.importPath}");
    }
}
EOF

  cat > "$SCRATCH/Assets/HarnessEditor/AddImportedScenesToBuildSettings.cs" << 'EOF'
using System.Linq;
using UnityEditor;

internal static class AddImportedScenesToBuildSettings
{
    public static void Run()
    {
        var scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Samples" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();

        EditorBuildSettings.scenes = scenePaths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        foreach (var p in scenePaths) UnityEngine.Debug.Log($"[AddImportedScenesToBuildSettings] Added {p}");
    }
}
EOF
}

run_unity() {
  local logname="$1"; shift
  "$UNITY" -batchmode -nographics -quit -projectPath "$SCRATCH" -logFile "$LOGS/$logname" "$@"
  if grep -qi "error CS" "$LOGS/$logname"; then
    echo "COMPILE ERRORS in $logname:"; grep -i "error CS" "$LOGS/$logname" | sort -u
    exit 1
  fi
}

# Prints the total/passed/failed summary line, or a clear diagnostic if the results file is missing/malformed.
# Does not exit -- used where a nonzero `failed` count can be an already-understood, pre-existing condition
# (e.g. the full PlayMode suite's 10 TestProject-fixture-dependent failures documented in
# Docs/Verification/Task-5.3-BasicCRPG.md) rather than something this specific invocation is trying to prove.
report_results() {
  local xml="$1"
  if [ ! -f "$xml" ]; then
    echo "WARNING: no results file at $xml -- Unity likely crashed or never reached the test runner."
    return
  fi
  grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$xml" | head -1
}

# Fails loudly on a nonzero `failed` count OR a zero `total` -- a swallowed Unity exit code (every -runTests
# invocation below ends in `|| true` since Unity's own exit status isn't a reliable pass/fail signal) previously
# meant a silently-ignored or zero-matched -testFilter still printed a harmless-looking `total="0"` line and the
# script exited 0 regardless. Used for a specific, scoped -testFilter run, where "it actually ran with zero
# failures" is the whole point of the call -- NOT for a full-suite run, which can carry already-documented,
# pre-existing failures unrelated to whatever this harness invocation is verifying (see report_results above).
check_results() {
  local xml="$1"
  if [ ! -f "$xml" ]; then
    echo "FAILED: no results file at $xml -- Unity likely crashed or never reached the test runner."
    exit 1
  fi

  local line
  line="$(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$xml" | head -1)"
  if [ -z "$line" ]; then
    echo "FAILED: could not find a total/passed/failed summary line in $xml."
    exit 1
  fi
  echo "$line"

  local total passed failed
  total="$(echo "$line" | grep -o 'total="[0-9]*"' | grep -o '[0-9]*')"
  passed="$(echo "$line" | grep -o 'passed="[0-9]*"' | grep -o '[0-9]*')"
  failed="$(echo "$line" | grep -o 'failed="[0-9]*"' | grep -o '[0-9]*')"

  if [ "$failed" != "0" ]; then
    echo "FAILED: $failed test(s) failed in $xml."
    grep -o 'result="Failed"[^>]*name="[^"]*"' "$xml" | sort -u || true
    exit 1
  fi
  if [ "$total" = "0" ]; then
    echo "FAILED: total=\"0\" in $xml -- nothing actually ran (check the test filter/testables wiring)."
    exit 1
  fi
  # A wholly Ignored/Skipped test reports failed="0" and a nonzero total without a single test actually
  # passing -- exactly the "silently skipped fixture reads as success" gap this whole function exists to
  # close, so passed="0" must fail loudly too, not just failed!=0/total=0.
  if [ "$passed" = "0" ]; then
    echo "FAILED: passed=\"0\" in $xml -- every matched test was skipped/ignored, nothing actually ran to completion."
    exit 1
  fi
}

case "${1:-}" in
  package-only)
    ensure_project 1
    echo "== Step 1: package-only compile check =="
    run_unity "step1_compile.log"
    echo "== Step 2: package-only full test suite =="
    "$UNITY" -batchmode -nographics -runTests -projectPath "$SCRATCH" -testPlatform EditMode -testResults "$LOGS/step2_editmode.xml" -logFile "$LOGS/step2_editmode.log" || true
    "$UNITY" -batchmode -nographics -runTests -projectPath "$SCRATCH" -testPlatform PlayMode -testResults "$LOGS/step2_playmode.xml" -logFile "$LOGS/step2_playmode.log" || true
    report_results "$LOGS/step2_editmode.xml"
    report_results "$LOGS/step2_playmode.xml"
    ;;
  import)
    ensure_project 0
    sample="${2:?Usage: clean-install-harness.sh import \"<Sample Display Name>\"}"
    echo "== Step 3: importing sample '$sample' via the real Package Manager Sample API =="
    HARNESS_SAMPLE_NAME="$sample" "$UNITY" -batchmode -nographics -quit -projectPath "$SCRATCH" -executeMethod SampleImportHelper.ImportSample -logFile "$LOGS/import_${sample// /_}.log"
    grep "SampleImportHelper" "$LOGS/import_${sample// /_}.log"
    echo "== Step 4: compile check with sample imported =="
    run_unity "compile_after_${sample// /_}.log"
    echo "== Step 5: adding imported scenes to Build Settings =="
    "$UNITY" -batchmode -nographics -quit -projectPath "$SCRATCH" -executeMethod AddImportedScenesToBuildSettings.Run -logFile "$LOGS/add_scenes_${sample// /_}.log"
    grep "AddImportedScenesToBuildSettings" "$LOGS/add_scenes_${sample// /_}.log"
    ;;
  test)
    filter="${2:?Usage: clean-install-harness.sh test <NUnit test filter>}"
    echo "== Running behavioral fixture: $filter =="
    "$UNITY" -batchmode -nographics -runTests -projectPath "$SCRATCH" -testPlatform PlayMode -testFilter "$filter" -testResults "$LOGS/behavioral_$$.xml" -logFile "$LOGS/behavioral_$$.log" || true
    check_results "$LOGS/behavioral_$$.xml"
    ;;
  full-suite)
    platform="${2:?Usage: clean-install-harness.sh full-suite <EditMode|PlayMode>}"
    "$UNITY" -batchmode -nographics -runTests -projectPath "$SCRATCH" -testPlatform "$platform" -testResults "$LOGS/fullsuite_${platform}.xml" -logFile "$LOGS/fullsuite_${platform}.log" || true
    report_results "$LOGS/fullsuite_${platform}.xml"
    ;;
  *)
    echo "Usage: $0 {package-only|import <sample>|test <filter>|full-suite <EditMode|PlayMode>}"
    exit 1
    ;;
esac
