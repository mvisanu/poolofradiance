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

            // URP's Render Graph path breaks on WebGL2 (Hidden/CoreSRP/CoreCopy is
            // "not supported on this GPU" in-browser). Ship the web player on the proven
            // compatibility path; desktop keeps Render Graph.
            var rgs = UnityEngine.Rendering.GraphicsSettings
                .GetRenderPipelineSettings<UnityEngine.Rendering.Universal.RenderGraphSettings>();
            bool prevCompat = rgs != null && rgs.enableRenderCompatibilityMode;
            if (rgs != null)
            {
                rgs.enableRenderCompatibilityMode = true;
                AssetDatabase.SaveAssets();
            }

            // The web pipeline asset must be the ACTIVE pipeline while building, never
            // swapped in at runtime: shader-variant stripping prefilters against the
            // pipeline assets in Graphics/Quality settings, and a runtime swap to an
            // asset with a different prefiltering profile asks for variants the build
            // no longer contains — objects then silently draw nothing (the web build
            // shipped with every imported mesh invisible; ground/text/UI survived).
            var webPipe = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(
                "Assets/Resources/URP/URP_Web.asset");
            var prevDefault = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            var prevQuality = new UnityEngine.Rendering.RenderPipelineAsset[QualitySettings.names.Length];
            int prevLevel = QualitySettings.GetQualityLevel();
            if (webPipe != null)
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = webPipe;
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    prevQuality[i] = QualitySettings.renderPipeline;
                    QualitySettings.renderPipeline = webPipe;
                }
                QualitySettings.SetQualityLevel(prevLevel, false);
                AssetDatabase.SaveAssets();
            }
            else
            {
                Debug.LogWarning("[Build] URP_Web asset missing — building with the desktop pipeline.");
            }

            bool ok;
            string outDir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "..", "webbase", "game"));
            try
            {
                // RP_WEBGL_DEV=1 → development player with readable console errors,
                // for diagnosing web-only rendering faults from the browser console.
                bool dev = System.Environment.GetEnvironmentVariable("RP_WEBGL_DEV") == "1";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outDir,
                    target = BuildTarget.WebGL,
                    options = dev ? BuildOptions.Development : BuildOptions.None
                });

                ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
                Debug.Log($"[Build] WebGL {report.summary.result}, " +
                          $"size {report.summary.totalSize / (1024 * 1024)} MB -> {outDir}");
            }
            finally
            {
                if (webPipe != null)
                {
                    UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = prevDefault;
                    for (int i = 0; i < QualitySettings.names.Length; i++)
                    {
                        QualitySettings.SetQualityLevel(i, false);
                        QualitySettings.renderPipeline = prevQuality[i];
                    }
                    QualitySettings.SetQualityLevel(prevLevel, false);
                }
                if (rgs != null) rgs.enableRenderCompatibilityMode = prevCompat;
                AssetDatabase.SaveAssets();
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
