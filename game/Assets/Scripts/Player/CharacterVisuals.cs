using System.Linq;
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
            visual.transform.localScale *= scale;

            // Preserve the prefab's authored root correction. Generated Blender beasts
            // carry a required -90 degree FBX conversion here; replacing it with identity
            // rotated the bear and rat into the ground. Remember it so the fallback death
            // pose can be applied relative to the authored orientation as well.
            var pose = visual.GetComponent<AuthoredVisualPose>();
            if (pose == null) pose = visual.AddComponent<AuthoredVisualPose>();
            pose.BaseLocalRotation = visual.transform.localRotation;

            if (tint.HasValue)
                foreach (var r in visual.GetComponentsInChildren<Renderer>())
                {
                    // Tint is a multiplier: white means "keep the source art". Assigning
                    // white directly erased the flat brown/grey materials on generated
                    // beasts, while multiplication still colours textured stand-ins.
                    Color source = r.material.color;
                    Color t = tint.Value;
                    r.material.color = new Color(source.r * t.r, source.g * t.g,
                        source.b * t.b, source.a * t.a);
                }

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

        /// <summary>Stronger whole-creature visual audit: proves there is an enabled real
        /// renderer, supported opaque material, non-degenerate world bounds, and geometry
        /// above the unit's ground plane. Used by -creaturetest for every monster id.</summary>
        public static bool TryGetVisibleCharacterBounds(Transform unitRoot,
            out Bounds bounds, out string issue)
        {
            bounds = default;
            issue = "";
            if (unitRoot == null) { issue = "missing unit root"; return false; }
            var visual = unitRoot.Find(VisualName);
            if (visual == null) { issue = "capsule fallback/no character model"; return false; }

            var renderers = visual.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) { issue = "no enabled renderer"; return false; }

            bool first = true;
            bool hasGraphicsDevice = SystemInfo.graphicsDeviceType
                != UnityEngine.Rendering.GraphicsDeviceType.Null;
            foreach (var r in renderers)
            {
                foreach (var material in r.sharedMaterials)
                {
                    // Shader.isSupported is always false under -nographics because Unity
                    // deliberately owns no graphics device there. Visible capture sessions
                    // still enforce support; headless smoke enforces a real non-null shader.
                    if (material == null || material.shader == null
                        || (hasGraphicsDevice && !material.shader.isSupported))
                    { issue = $"unsupported material on {r.name}"; return false; }
                    if (hasGraphicsDevice && material.HasProperty("_BaseColor")
                        && material.GetColor("_BaseColor").a < 0.1f)
                    { issue = $"transparent material on {r.name}"; return false; }
                }
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }

            Vector3 size = bounds.size;
            if (size.x < 0.08f || size.y < 0.08f || size.z < 0.08f)
            { issue = $"degenerate bounds {size}"; return false; }
            float ground = unitRoot.position.y;
            if (bounds.max.y < ground + 0.08f)
            { issue = $"entire model below ground (top {bounds.max.y - ground:0.00}m)"; return false; }
            if (bounds.min.y < ground - 0.45f)
            { issue = $"model buried {ground - bounds.min.y:0.00}m below ground"; return false; }
            return true;
        }

        public static void Trigger(Transform unitRoot, string trigger)
        {
            var animator = AnimatorOf(unitRoot);
            if (animator == null || animator.runtimeAnimatorController == null) return;
            bool found = false;
            foreach (var parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Trigger
                    && parameter.name == trigger)
                { found = true; break; }
            // Native creature rigs and clean clones without the licensed Warrior Pack
            // may not expose the weapon-specific triggers. Their authored Attack state
            // remains the safe fallback and avoids Animator missing-parameter errors.
            if (!found && (trigger == "Cast" || trigger == "Victory"
                           || trigger == "Attack1H" || trigger == "Attack2H"
                           || trigger == "AttackRanged"))
            {
                trigger = "Attack";
                foreach (var parameter in animator.parameters)
                    if (parameter.type == AnimatorControllerParameterType.Trigger
                        && parameter.name == trigger)
                    { found = true; break; }
            }
            if (found) animator.SetTrigger(trigger);
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
                {
                    var pose = visual.GetComponent<AuthoredVisualPose>();
                    Quaternion authored = pose != null
                        ? pose.BaseLocalRotation : Quaternion.identity;
                    visual.localRotation = dead
                        ? authored * Quaternion.Euler(-90f, 0f, 0f) : authored;
                }
            }
        }
    }

    /// <summary>Runtime memory of a prefab's imported root correction.</summary>
    public sealed class AuthoredVisualPose : MonoBehaviour
    {
        public Quaternion BaseLocalRotation { get; set; } = Quaternion.identity;
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
