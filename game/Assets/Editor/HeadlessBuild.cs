using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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

        /// <summary>Web build for the webbase/ distribution. Run Unity with
        /// `-buildTarget WebGL` so the target switch happens before this method:
        /// Unity.exe -batchmode -quit -projectPath game -buildTarget WebGL
        ///   -executeMethod RadiantPool.EditorTools.HeadlessBuild.WebGL -logFile webgl.log</summary>
        [MenuItem("RadiantPool/Build WebGL")]
        public static void WebGL()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] No scenes in build settings — run ProjectBootstrap.Run first.");
                EditorApplication.Exit(1);
                return;
            }

            // Brotli + decompression fallback: loads correctly from ANY static host
            // (itch.io, Netlify, `python serve.py`) with no Content-Encoding config.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.template = "PROJECT:RadiantPool";
            // Newtonsoft reflects over the save models; link.xml pins them and Minimal
            // stripping keeps the linker from outsmarting it elsewhere.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);

            string outDir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "..", "webbase", "game"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Debug.Log($"[Build] WebGL {report.summary.result}, " +
                      $"size {report.summary.totalSize / (1024 * 1024)} MB -> {outDir}");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
