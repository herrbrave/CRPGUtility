using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CRPG.Consumer.Game.Editor
{
    /// <summary>
    /// One-off standalone build trigger for Gate D's verification -- direct <see cref="BuildPipeline.BuildPlayer"/>
    /// call via <c>-executeMethod</c>, since ConsumerGame deliberately doesn't include <c>com.unity.pipeline</c>
    /// (kept out of the "clean consumer" manifest; see Docs/Verification/Consumer-Game.md).
    /// </summary>
    internal static class ConsumerBuildScript
    {
        [MenuItem("Consumer Game/Build Standalone Development Player")]
        public static void Build()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/ConsumerGame.unity" },
                locationPathName = "Builds/ConsumerGame/ConsumerGame.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };

            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[ConsumerBuildScript] Build result: {report.summary.result}, errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}, size={report.summary.totalSize} bytes, time={report.summary.totalTime}");
        }
    }
}
