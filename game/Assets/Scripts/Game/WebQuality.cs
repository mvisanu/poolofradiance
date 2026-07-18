using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace RadiantPool.Game
{
    /// <summary>Web-only runtime trims: cheaper camera AA and no film grain. The web
    /// URP pipeline itself is baked in at BUILD time by HeadlessBuild.WebGL (never
    /// swapped at runtime - see the comment inside). Desktop and editor builds compile
    /// this to an empty method, so the desktop pipeline is untouched there.</summary>
    public static class WebQuality
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Apply()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                // NO pipeline swap here: HeadlessBuild.WebGL bakes URP_Web in as the
                // build's active pipeline. Swapping at runtime asks for shader variants
                // the build stripped against the desktop asset, and every imported mesh
                // silently disappears (no error, no magenta - just nothing drawn).

                // SMAA High stacked on a web GPU is too costly - FXAA reads fine at
                // the game's UI scale. HDR stays on (bloom needs it; LDR washes the
                // ACES grade out).
                var cam = Camera.main;
                if (cam != null)
                {
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
