using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace RadiantPool.Game
{
    /// <summary>Swaps a WebGL player onto the lightweight URP variant at startup:
    /// the bootstrap bakes URP_Desktop (default) and URP_Web side by side under
    /// Resources/URP, and this picks the web one plus cheaper camera AA and no film
    /// grain. Desktop and editor builds compile this to an empty method, so the
    /// desktop pipeline is untouched there.</summary>
    public static class WebQuality
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Apply()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                var web = Resources.Load<UniversalRenderPipelineAsset>("URP/URP_Web");
                if (web != null)
                {
                    QualitySettings.renderPipeline = web;
                    Debug.Log("[WebQuality] URP_Web pipeline active.");
                }
                else
                {
                    Debug.Log("[WebQuality] URP/URP_Web asset missing; keeping the default pipeline.");
                }

                // SMAA High stacked on a web GPU is too costly - FXAA reads fine at
                // the game's UI scale. HDR is off in the web pipeline asset already;
                // the camera flag must agree or it forces an HDR target anyway.
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.allowHDR = false;
                    var camData = cam.GetComponent<UniversalAdditionalCameraData>();
                    if (camData != null)
                        camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                }

                // Film grain is a full extra fullscreen pass - not worth it on web.
                var volume = Object.FindFirstObjectByType<Volume>();
                if (volume != null && volume.profile != null
                    && volume.profile.TryGet(out FilmGrain grain))
                {
                    grain.active = false;
                }

                Debug.Log("[WebQuality] Web quality preset applied.");
            }
            catch (System.Exception e)
            {
                Debug.Log($"[WebQuality] Failed to apply web preset: {e}");
            }
#endif
        }
    }
}
