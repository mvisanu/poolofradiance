using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using RadiantPool.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RadiantPool.EditorTools
{
    /// <summary>WebGL builds cannot open sockets, so the scene's Tugboat (UDP) transport
    /// is swapped for the in-process LoopbackTransport while the build pipeline copies the
    /// scene. The scene asset on disk is untouched — desktop builds keep Tugboat, and the
    /// bootstrap never needs to know two platforms exist.</summary>
    public sealed class WebGLBuildSupport : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // report is null when entering play mode in the editor — leave that alone.
            if (report == null || report.summary.platform != BuildTarget.WebGL) return;

            foreach (var root in scene.GetRootGameObjects())
            {
                var network = root.GetComponentInChildren<NetworkManager>(true);
                if (network == null) continue;

                var go = network.gameObject;
                var tugboat = go.GetComponent<Tugboat>();
                if (tugboat != null) Object.DestroyImmediate(tugboat);
                if (go.GetComponent<LoopbackTransport>() == null)
                    go.AddComponent<LoopbackTransport>();
                Debug.Log($"[WebGLBuild] {scene.name}: Tugboat -> LoopbackTransport on '{go.name}'.");
            }
        }
    }
}
