using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Marker and presentation driver for a physical town lamp post. WorldAtmosphere
    /// owns the point-light intensity so all lamps obey real time; this component gives the
    /// emissive flame a small independent lick and exposes structural health to self-tests.</summary>
    public sealed class FlameLampPost : MonoBehaviour
    {
        public const string QuestCenterArea = "quest_center";

        public string AreaId = "district";
        public Transform FlameVisual;
        public Light Glow;

        private Vector3 _baseScale = Vector3.one;
        private float _seed;

        public bool HasVisibleFlame
        {
            get
            {
                if (FlameVisual == null) return false;
                return FlameVisual.GetComponentsInChildren<Renderer>(true).Any(r =>
                    r.enabled && r.gameObject.activeInHierarchy
                    && r.sharedMaterials.Length > 0
                    && r.sharedMaterials.All(m => m != null));
            }
        }

        public bool HasWorkingLight => Glow != null && Glow.type == LightType.Point
                                       && Glow.gameObject.name == "GlowLight"
                                       && Glow.range >= 8f;

        private void Awake()
        {
            if (FlameVisual != null) _baseScale = FlameVisual.localScale;
            _seed = Mathf.Abs(transform.position.x * 0.173f
                              + transform.position.z * 0.317f) + 1f;
        }

        private void Update()
        {
            if (FlameVisual == null) return;
            float lick = 0.90f + Mathf.PerlinNoise(
                _seed, Time.unscaledTime * 7.4f) * 0.22f;
            FlameVisual.localScale = new Vector3(_baseScale.x / Mathf.Sqrt(lick),
                _baseScale.y * lick, _baseScale.z / Mathf.Sqrt(lick));
            FlameVisual.localRotation = Quaternion.Euler(0f,
                Time.unscaledTime * 24f + _seed * 31f,
                (lick - 1f) * 11f);
        }
    }
}
