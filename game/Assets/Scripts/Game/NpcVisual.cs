using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Replaces an NPC's placeholder primitive with a KayKit character model
    /// at runtime (works in builds — models load from Resources/Characters).</summary>
    public class NpcVisual : MonoBehaviour
    {
        public string Model = "Mage";
        public Color Tint = Color.white;

        private void Start()
        {
            var attached = CharacterVisuals.Attach(transform, Model, Tint);
            if (attached != null)
            {
                var own = GetComponent<MeshRenderer>();
                if (own != null) own.enabled = false;
                var collider = GetComponent<CapsuleCollider>();
                if (collider != null) collider.enabled = false;
            }
        }
    }
}
