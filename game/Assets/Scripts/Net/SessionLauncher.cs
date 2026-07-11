using System;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Phase 2 host/join flow with invite codes, rendered as a simple IMGUI panel
    /// (replaced by the themed UI at 3f). Host = listen server. Errors are surfaced
    /// on-screen — never silent (ARCHITECTURE risk #1).</summary>
    [RequireComponent(typeof(NetworkManager))]
    public class SessionLauncher : MonoBehaviour
    {
        public const ushort DefaultPort = 7770;

        private NetworkManager _network;
        private Tugboat _tugboat;
        private string _joinCode = "";
        private string _displayName = "";
        private string _hostCode = "";
        private string _status = "Not connected";
        private string _error = "";
        private bool _sessionStarted;

        public static string LocalDisplayName { get; private set; } = "Adventurer";

        private void Awake()
        {
            _network = GetComponent<NetworkManager>();
            _tugboat = GetComponent<Tugboat>();
            _displayName = PlayerPrefs.GetString("displayName", "Adventurer");

            _network.ServerManager.OnServerConnectionState += OnServerState;
            _network.ClientManager.OnClientConnectionState += OnClientState;
        }

        private void OnDestroy()
        {
            if (_network == null) return;
            _network.ServerManager.OnServerConnectionState -= OnServerState;
            _network.ClientManager.OnClientConnectionState -= OnClientState;
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                var ip = InviteCode.LocalAddress();
                _hostCode = InviteCode.Encode(ip, DefaultPort);
                _status = $"Hosting — invite code: {_hostCode}";
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                _status = "Server stopped";
                _sessionStarted = false;
            }
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Starting:
                    _status = "Connecting…";
                    break;
                case LocalConnectionState.Started:
                    _status = _hostCode.Length > 0
                        ? $"Hosting — invite code: {_hostCode}"
                        : "Connected";
                    _sessionStarted = true;
                    _error = "";
                    break;
                case LocalConnectionState.Stopped:
                    if (_sessionStarted) _status = "Disconnected from host";
                    else if (_error.Length == 0) _error = "Could not reach host. Check the code and that the host is running.";
                    _sessionStarted = false;
                    break;
            }
        }

        public void Host()
        {
            LocalDisplayName = Sanitize(_displayName);
            PlayerPrefs.SetString("displayName", LocalDisplayName);
            _error = "";
            _tugboat.SetPort(DefaultPort);
            if (!_network.ServerManager.StartConnection())
            {
                _error = "Failed to start server (is another host using the port?).";
                return;
            }
            _tugboat.SetClientAddress("localhost");
            _network.ClientManager.StartConnection();
        }

        public void Join(string code)
        {
            LocalDisplayName = Sanitize(_displayName);
            PlayerPrefs.SetString("displayName", LocalDisplayName);
            _error = "";
            if (!InviteCode.TryDecode(code, out string address, out ushort port))
            {
                _error = "That invite code is not valid.";
                return;
            }
            _tugboat.SetClientAddress(address);
            _tugboat.SetPort(port);
            _network.ClientManager.StartConnection();
        }

        private static string Sanitize(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "Adventurer";
            return name.Length > 20 ? name.Substring(0, 20) : name;
        }

        private void OnGUI()
        {
            const int w = 340;
            GUILayout.BeginArea(new Rect(12, 12, w, 240), GUI.skin.box);
            GUILayout.Label("<b>Radiant Pool — session</b>",
                new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label(_status);
            if (_error.Length > 0)
            {
                var errStyle = new GUIStyle(GUI.skin.label) { richText = true };
                GUILayout.Label($"<color=#ff6666>{_error}</color>", errStyle);
            }

            if (!_sessionStarted)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", GUILayout.Width(45));
                _displayName = GUILayout.TextField(_displayName, 20);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Host a campaign")) Host();

                GUILayout.BeginHorizontal();
                _joinCode = GUILayout.TextField(_joinCode, 12);
                if (GUILayout.Button("Join", GUILayout.Width(70))) Join(_joinCode);
                GUILayout.EndHorizontal();
            }
            else if (_hostCode.Length > 0)
            {
                GUILayout.Label($"Share this code with friends: {_hostCode}");
                if (GUILayout.Button("Copy code")) GUIUtility.systemCopyBuffer = _hostCode;
            }
            GUILayout.EndArea();
        }
    }
}
