using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>CI/CLI build entry points. Usage:
    /// Unity.exe -batchmode -quit -projectPath game -executeMethod
    ///   RadiantPool.EditorTools.HeadlessBuild.Win64 -logFile build.log</summary>
    public static class HeadlessBuild
    {
        [MenuItem("RadiantPool/Build Win64")]
        public static void Win64()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] No scenes in build settings — run ProjectBootstrap.Run first.");
                EditorApplication.Exit(1);
                return;
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/Win64/RadiantPool.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Debug.Log($"[Build] {report.summary.result}, size {report.summary.totalSize / (1024 * 1024)} MB");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
