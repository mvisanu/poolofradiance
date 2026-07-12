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

        public static void Trigger(Transform unitRoot, string trigger)
        {
            var animator = AnimatorOf(unitRoot);
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetTrigger(trigger);
        }

        /// <summary>Puts a weapon/shield model into the character's hand slot at runtime
        /// (prefabs live under Resources/Weapons). Empty model name clears the hand.</summary>
        public static void SetHandItem(Transform unitRoot, string side, string modelName)
        {
            var visual = unitRoot != null ? unitRoot.Find(VisualName) : null;
            if (visual == null) return;
            Transform slot = null;
            foreach (var t in visual.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("handslot") && n.Contains(side)) { slot = t; break; }
            }
            if (slot == null)
                foreach (var t in visual.GetComponentsInChildren<Transform>())
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("hand") && n.Contains(side)) { slot = t; break; }
                }
            if (slot == null) return;

            for (int i = slot.childCount - 1; i >= 0; i--)
                Object.Destroy(slot.GetChild(i).gameObject);
            if (string.IsNullOrEmpty(modelName)) return;
            var prefab = Resources.Load<GameObject>($"Weapons/{modelName}");
            if (prefab == null) return;
            var item = Object.Instantiate(prefab, slot);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
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
