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

        private static readonly string[] ClassNames = { "Fighter", "Cleric", "Wizard", "Rogue" };
        private static readonly string[] RaceNames = { "Human", "Dwarf", "Elf", "Halfling" };

        private void OnGUI()
        {
            const int w = 360;
            GUILayout.BeginArea(new Rect(12, 12, w, _sessionStarted ? 150 : 470), GUI.skin.box);
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

                DrawCharacterCreation();

                bool valid = CharacterBuild.Local.Validate(out string buildError);
                if (!valid)
                {
                    var errStyle = new GUIStyle(GUI.skin.label) { richText = true };
                    GUILayout.Label($"<color=#ffcc66>{buildError}</color>", errStyle);
                }
                GUI.enabled = valid;
                if (GUILayout.Button("Host a campaign")) Host();
                GUILayout.BeginHorizontal();
                _joinCode = GUILayout.TextField(_joinCode, 12);
                if (GUILayout.Button("Join", GUILayout.Width(70))) Join(_joinCode);
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }
            else if (_hostCode.Length > 0)
            {
                GUILayout.Label($"Share this code with friends: {_hostCode}");
                if (GUILayout.Button("Copy code")) GUIUtility.systemCopyBuffer = _hostCode;
            }
            GUILayout.EndArea();
        }

        /// <summary>3c: class/race selection + SRD 27-point buy. The server re-validates
        /// everything on spawn, so this panel is pure convenience.</summary>
        private void DrawCharacterCreation()
        {
            var b = CharacterBuild.Local;

            GUILayout.Space(4);
            GUILayout.Label("Class:");
            int newClass = GUILayout.SelectionGrid(b.ClassIndex, ClassNames, 4);
            if (newClass != b.ClassIndex)
            {
                b = CharacterBuild.Default(newClass);   // sensible preset per class
            }
            GUILayout.Label("Race:");
            b.RaceIndex = GUILayout.SelectionGrid(b.RaceIndex, RaceNames, 4);

            int cost = 0;
            try { cost = RadiantPool.Rules.PointBuy.TotalCost(b.Str, b.Dex, b.Con, b.Int, b.Wis, b.Cha); }
            catch { /* out-of-range shown by Validate */ }
            GUILayout.Label($"Abilities — points {cost}/{RadiantPool.Rules.PointBuy.Budget}:");

            void Row(string label, ref int score)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(34));
                if (GUILayout.Button("−", GUILayout.Width(26)) && score > 8) score--;
                GUILayout.Label(score.ToString(),
                    new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter },
                    GUILayout.Width(28));
                if (GUILayout.Button("+", GUILayout.Width(26)) && score < 15) score++;
                int mod = (score - 10) >= 0 ? (score - 10) / 2 : (score - 11) / 2;
                GUILayout.Label($"({(mod >= 0 ? "+" : "")}{mod})", GUILayout.Width(36));
                GUILayout.EndHorizontal();
            }

            Row("STR", ref b.Str);
            Row("DEX", ref b.Dex);
            Row("CON", ref b.Con);
            Row("INT", ref b.Int);
            Row("WIS", ref b.Wis);
            Row("CHA", ref b.Cha);

            CharacterBuild.Local = b;
        }
    }
}
