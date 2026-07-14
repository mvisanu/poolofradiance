using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Swaps a unit's placeholder capsule for a KayKit character prefab from
    /// Resources/Characters, with optional tint and scale. Also drives the shared
    /// animator: MotionAnimator feeds Speed from world movement (works for remote
    /// players via their interpolated NetworkTransform — no extra networking).</summary>
    public static class CharacterVisuals
    {
        public const string VisualName = "CharacterModel";
        private const string EquipmentSocketPrefix = "EquipmentSocket_";

        public static GameObject Attach(Transform parent, string prefabName,
            Color? tint = null, float scale = 1f)
        {
            var prefab = Resources.Load<GameObject>($"Characters/{prefabName}");
            if (prefab == null) return null;

            // Hide placeholder primitives (capsule body, facing nose).
            foreach (var r in parent.GetComponentsInChildren<MeshRenderer>())
                if (r.gameObject.name is "Capsule" or "Cube" or "nose"
                    || r.gameObject.name.StartsWith("Monster_"))
                    r.enabled = false;

            var old = parent.Find(VisualName);
            if (old != null) Object.Destroy(old.gameObject);

            var visual = Object.Instantiate(prefab, parent);
            visual.name = VisualName;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale *= scale;

            if (tint.HasValue)
                foreach (var r in visual.GetComponentsInChildren<Renderer>())
                    r.material.color = tint.Value;

            if (visual.GetComponent<MotionAnimator>() == null)
                visual.AddComponent<MotionAnimator>();
            return visual;
        }

        public static Animator AnimatorOf(Transform unitRoot)
        {
            var visual = unitRoot != null ? unitRoot.Find(VisualName) : null;
            return visual != null ? visual.GetComponent<Animator>() : null;
        }

        /// <summary>True only when a real rendered character has replaced the primitive
        /// network placeholder. Used by unattended recruitment coverage so companions
        /// cannot silently regress to visible capsules.</summary>
        public static bool HasVisibleCharacterModel(Transform unitRoot)
        {
            if (unitRoot == null) return false;
            var visual = unitRoot.Find(VisualName);
            if (visual == null) return false;

            bool hasVisibleRenderer = false;
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { hasVisibleRenderer = true; break; }
            if (!hasVisibleRenderer) return false;

            foreach (var r in unitRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform.IsChildOf(visual)) continue;
                if ((r.gameObject.name is "Capsule" or "Cube" or "nose"
                     || r.gameObject.name.StartsWith("Monster_")) && r.enabled)
                    return false;
            }
            return true;
        }

        public static void Trigger(Transform unitRoot, string trigger)
        {
            var animator = AnimatorOf(unitRoot);
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetTrigger(trigger);
        }

        /// <summary>Puts a weapon/shield model into the character's hand slot at runtime
        /// (prefabs live under Resources/Weapons). Empty model name clears the hand.</summary>
        public static bool SetHandItem(Transform unitRoot, string side, string modelName)
        {
            var visual = unitRoot != null ? unitRoot.Find(VisualName) : null;
            if (visual == null) return false;
            side = side != null && side.ToLowerInvariant().StartsWith("l") ? "L" : "R";

            var socket = FindOrCreateEquipmentSocket(visual, side);
            if (socket == null) return false;
            for (int i = socket.childCount - 1; i >= 0; i--)
                Object.Destroy(socket.GetChild(i).gameObject);
            if (string.IsNullOrEmpty(modelName)) return true;

            var prefab = Resources.Load<GameObject>($"Weapons/{modelName}");
            if (prefab == null)
            {
                Debug.LogWarning($"[RadiantPool] missing hand model Resources/Weapons/{modelName}");
                return false;
            }

            // Keep the imported prefab's local rotation and scale. KayKit weapon FBXes
            // carry a required -90 degree root correction; resetting rotation to identity
            // made equipped items lie inside the hand and appear to be missing.
            var item = Object.Instantiate(prefab, socket, false);
            item.name = $"Equipped_{side}_{modelName}";
            return true;
        }

        /// <summary>True when the named model is mounted in the requested hand. Used by
        /// the unattended weapon self-test, so visual equipment cannot silently regress.</summary>
        public static bool HasHandItem(Transform unitRoot, string side, string modelName)
        {
            var visual = unitRoot != null ? unitRoot.Find(VisualName) : null;
            if (visual == null) return false;
            side = side != null && side.ToLowerInvariant().StartsWith("l") ? "L" : "R";
            var socket = FindEquipmentSocket(visual, side);
            if (socket == null) return false;
            string expected = $"Equipped_{side}_{modelName}";
            for (int i = 0; i < socket.childCount; i++)
                if (socket.GetChild(i).name == expected) return true;
            return false;
        }

        private static Transform FindEquipmentSocket(Transform visual, string side)
        {
            string name = EquipmentSocketPrefix + side;
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Transform FindOrCreateEquipmentSocket(Transform visual, string side)
        {
            var existing = FindEquipmentSocket(visual, side);
            if (existing != null) return existing;

            var all = visual.GetComponentsInChildren<Transform>(true);
            Transform hand = null;
            foreach (var t in all)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("handslot") && MatchesSide(n, side)) { hand = t; break; }
            }
            if (hand == null)
                foreach (var t in all)
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("hand") && MatchesSide(n, side)) { hand = t; break; }
                }

            // Non-humanoid/fallback rigs may not expose named hand bones. A model-root
            // socket still keeps the item visible; humanoid KayKit rigs take the animated
            // hand path above.
            bool fallback = hand == null;
            if (fallback) hand = visual;
            var socket = new GameObject(EquipmentSocketPrefix + side).transform;
            socket.SetParent(hand, false);
            if (fallback)
                socket.localPosition = side == "R"
                    ? new Vector3(0.48f, 1.05f, 0.08f)
                    : new Vector3(-0.48f, 1.05f, 0.08f);
            return socket;
        }

        private static bool MatchesSide(string name, string side)
        {
            if (side == "R")
                return name.Contains("right") || name.EndsWith("r")
                    || name.Contains(".r") || name.Contains("_r") || name.Contains("-r");
            return name.Contains("left") || name.EndsWith("l")
                || name.Contains(".l") || name.Contains("_l") || name.Contains("-l");
        }

        public static void SetDead(Transform unitRoot, bool dead)
        {
            var animator = AnimatorOf(unitRoot);
            bool hasDeathState = false;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (var p in animator.parameters)
                    if (p.name == "Dead") { hasDeathState = true; break; }
                if (hasDeathState) animator.SetBool("Dead", dead);
            }

            // Guaranteed lay-down: if no death animation is available, tip the model
            // onto its back so a corpse never stands.
            if (!hasDeathState)
            {
                var visual = unitRoot != null ? unitRoot.Find(VisualName) : null;
                if (visual != null)
                    visual.localRotation = dead
                        ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
            }
        }
    }

    /// <summary>Feeds the animator's Speed parameter from actual world displacement, so
    /// walk cycles play for both the local player and network-interpolated remotes.</summary>
    public class MotionAnimator : MonoBehaviour
    {
        private Animator _animator;
        private Vector3 _lastPos;

        private void Start()
        {
            _animator = GetComponent<Animator>();
            _lastPos = transform.position;
        }

        private void Update()
        {
            if (_animator == null) return;
            Vector3 delta = transform.position - _lastPos;
            delta.y = 0f;
            _lastPos = transform.position;
            float speed = Time.deltaTime > 0.0001f ? delta.magnitude / Time.deltaTime : 0f;
            _animator.SetFloat("Speed",
                Mathf.Lerp(_animator.GetFloat("Speed"), speed, 12f * Time.deltaTime));
        }
    }
}
