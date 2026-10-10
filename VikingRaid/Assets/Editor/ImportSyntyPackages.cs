using System.IO;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools
{
    /// <summary>Imports the Synty packages this game uses from the repo's untracked <c>thirdpartyassets/</c> folder, so the
    /// project is reproducible from the owner's own copies (Synty licences forbid committing the raw assets -- Assets/Synty is
    /// gitignored). Order is oldest build first: PolygonGeneric and SyntyPackageHelper are duplicated, same GUIDs, across the
    /// 2021.3 / 2022 / 2022.3 builds. The Apocalypse HUD is deliberately NOT imported (toolkit default UI was chosen).</summary>
    public static class ImportSyntyPackages
    {
        private static readonly string[] Packages =
        {
            "SIDEKICK_Goblin_Fighters_Unity_2021_3_v1_0_4.unitypackage",
            "POLYGON_Goblin_War_Camp_Unity_2022_v1_1_1.unitypackage",
            "POLYGON_Viking_Realm_Unity_2022_3_v1_1_1.unitypackage",
            "POLYGON_NatureBiomes_AlpineMountain_Unity_2022_3_v1_5_5.unitypackage",
            "POLYGON_Particle_FX_Unity_2022_3_v1_4_1.unitypackage",
            "ANIMATION_Goblin_Locomotion_Unity_2022_3_v1_0_1.unitypackage",
        };

        /// <summary>Batch entry point: imports exactly one package (<c>-pkgIndex N</c>) and exits from the completion callback.
        /// ImportPackage is deferred, so -quit would exit before it runs, and the script compile that follows an import
        /// reloads the domain and would drop any static chaining state -- hence one package per Unity launch.</summary>
        public static void RunBatch()
        {
            var args = System.Environment.GetCommandLineArgs();
            var index = -1;
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-pkgIndex") int.TryParse(args[i + 1], out index);
            if (index < 0 || index >= Packages.Length) { Debug.LogError("[ImportSyntyPackages] bad -pkgIndex"); EditorApplication.Exit(2); return; }

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "thirdpartyassets", Packages[index]));
            AssetDatabase.importPackageFailed += (name, error) => { Debug.LogError($"[ImportSyntyPackages] FAILED {name}: {error}"); EditorApplication.Exit(1); };
            AssetDatabase.importPackageCancelled += name => { Debug.LogError($"[ImportSyntyPackages] cancelled {name}"); EditorApplication.Exit(1); };
            SessionState.SetBool(WatchKey, true);
            Debug.Log($"[ImportSyntyPackages] importing {Packages[index]}");
            AssetDatabase.ImportPackage(path, false);
            StartIdleWatcher();
        }

        // An imported package that ships Editor scripts forces a recompile and domain reload, which drops any managed
        // callback registered before it (the importPackageCompleted handler never fired and the batch hung forever).
        // SessionState survives the reload, so this watcher is re-armed by InitializeOnLoadMethod and exits once the
        // editor has been idle (not compiling, not importing) for a while after the import began.
        private const string WatchKey = "VikingRaid.ImportBatchWatch";
        private const double IdleSecondsBeforeExit = 45;
        private static double idleSince = -1;
        private static double startedAt;

        [InitializeOnLoadMethod]
        private static void ResumeWatcher()
        {
            if (SessionState.GetBool(WatchKey, false)) StartIdleWatcher();
        }

        private static void StartIdleWatcher()
        {
            startedAt = EditorApplication.timeSinceStartup;
            idleSince = -1;
            EditorApplication.update -= WatchForIdle;
            EditorApplication.update += WatchForIdle;
        }

        private static void WatchForIdle()
        {
            var now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || now - startedAt < 20) { idleSince = -1; return; }
            if (idleSince < 0) { idleSince = now; return; }
            if (now - idleSince < IdleSecondsBeforeExit) return;

            SessionState.SetBool(WatchKey, false);
            Debug.Log("[ImportSyntyPackages] editor idle after import -- exiting");
            EditorApplication.Exit(0);
        }

        [MenuItem("Viking Raid/0. Import Synty Packages")]
        public static void Run()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "thirdpartyassets"));
            foreach (var name in Packages)
            {
                var path = Path.Combine(folder, name);
                if (!File.Exists(path))
                {
                    Debug.LogError($"[ImportSyntyPackages] missing {path}");
                    continue;
                }

                Debug.Log($"[ImportSyntyPackages] importing {name}");
                AssetDatabase.ImportPackage(path, false);
            }
        }
    }
}
