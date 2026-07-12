using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Replicated display name + floating nameplate. The owning client submits
    /// its chosen name once on spawn; the server accepts a sanitized version and the
    /// SyncVar replicates it to everyone.</summary>
    public class PlayerIdentity : NetworkBehaviour
    {
        private readonly SyncVar<string> _displayName = new SyncVar<string>("");

        private TextMesh _nameplate;

        private void Awake()
        {
            _displayName.OnChange += (_, next, _) => ApplyName(next);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            EnsureNameplate();
            if (IsOwner)
                SubmitNameServerRpc(SessionLauncher.LocalDisplayName);
            ApplyName(_displayName.Value);
        }

        /// <summary>Server-side name set for AI companions (no owner to submit one).</summary>
        public void ServerSetName(string name) => _displayName.Value = name;

        [ServerRpc]
        private void SubmitNameServerRpc(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = $"Adventurer {OwnerId}";
            if (name.Length > 20) name = name.Substring(0, 20);
            _displayName.Value = name;
        }

        private void ApplyName(string name)
        {
            if (_nameplate != null) _nameplate.text = name ?? "";
        }

        private void EnsureNameplate()
        {
            if (_nameplate != null) return;
            var go = new GameObject("Nameplate");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            _nameplate = go.AddComponent<TextMesh>();
            _nameplate.characterSize = 0.08f;
            _nameplate.fontSize = 48;
            _nameplate.anchor = TextAnchor.LowerCenter;
            _nameplate.alignment = TextAlignment.Center;
            _nameplate.color = Color.white;
            go.AddComponent<Billboard>();
        }
    }

    /// <summary>Keeps a transform facing the camera.</summary>
    public class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(
                transform.position - cam.transform.position);
        }
    }
}
