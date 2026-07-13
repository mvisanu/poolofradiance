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
        private string _error = "";
        private bool _sessionStarted;

        /// <summary>Session state, owned here and DRAWN elsewhere: SessionPanel (the hotbar's
        /// session icon) shows it in-session, the title screen shows it before one starts. It
        /// no longer squats in the corner of the play screen for the whole campaign.</summary>
        public static string Status { get; private set; } = "Not connected";

        public static string HostCode { get; private set; } = "";

        public static string LocalDisplayName { get; private set; } = "Adventurer";

        private void Awake()
        {
            _network = GetComponent<NetworkManager>();
            _tugboat = GetComponent<Tugboat>();
            _displayName = PlayerPrefs.GetString("displayName", "Adventurer");

            _network.ServerManager.OnServerConnectionState += OnServerState;
            _network.ClientManager.OnClientConnectionState += OnClientState;

            // Diagnostic breadcrumbs for the join pipeline (visible in Player.log).
            _network.ServerManager.OnRemoteConnectionState += (conn, args) =>
                Debug.Log($"[RadiantPool] remote connection {conn.ClientId}: {args.ConnectionState}");
            _network.SceneManager.OnClientLoadedStartScenes += (conn, asServer) =>
                Debug.Log($"[RadiantPool] client {conn.ClientId} loaded start scenes (asServer={asServer})");
            var spawner = GetComponent<FishNet.Component.Spawning.PlayerSpawner>();
            if (spawner != null)
                spawner.OnSpawned += nob =>
                    Debug.Log($"[RadiantPool] player object spawned for owner {nob.OwnerId}");
        }

        /// <summary>Unattended smoke-testing hooks:
        ///   RadiantPool.exe -name Anna -autohost
        ///   RadiantPool.exe -name Ben -autojoin <code|localhost></summary>
        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-name" && i + 1 < args.Length)
                    _displayName = args[i + 1];
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autohost")
                {
                    Debug.Log("[RadiantPool] autohost");
                    Host();
                }
                else if (args[i] == "-autojoin" && i + 1 < args.Length)
                {
                    string code = args[i + 1];
                    Debug.Log($"[RadiantPool] autojoin {code}");
                    if (code.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        LocalDisplayName = Sanitize(_displayName);
                        _tugboat.SetClientAddress("127.0.0.1");
                        _tugboat.SetPort(DefaultPort);
                        _network.ClientManager.StartConnection();
                    }
                    else
                    {
                        Join(code);
                    }
                }
            }
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
                HostCode = InviteCode.Encode(ip, _hostPort);
                Status = $"Hosting — invite code: {HostCode}";
                Debug.Log($"[RadiantPool] server started, invite code {HostCode}");
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                Status = "Server stopped";
                _sessionStarted = false;
            }
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            Debug.Log($"[RadiantPool] client state: {args.ConnectionState}");
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Starting:
                    Status = "Connecting…";
                    break;
                case LocalConnectionState.Started:
                    Status = HostCode.Length > 0
                        ? $"Hosting — invite code: {HostCode}"
                        : "Connected";
                    _sessionStarted = true;
                    _error = "";
                    break;
                case LocalConnectionState.Stopped:
                    if (_sessionStarted) Status = "Disconnected from host";
                    else if (_error.Length == 0) _error = "Could not reach host. Check the code and that the host is running.";
                    _sessionStarted = false;
                    break;
            }
        }

        private ushort _hostPort = DefaultPort;

        /// <summary>First free UDP port in 7770-7779 (a second instance on the same
        /// machine — or a crashed one holding the socket — must not block hosting).
        /// The invite code carries the port, so joiners never notice.</summary>
        private static ushort FirstFreePort()
        {
            var inUse = System.Net.NetworkInformation.IPGlobalProperties
                .GetIPGlobalProperties().GetActiveUdpListeners();
            for (ushort port = DefaultPort; port < DefaultPort + 10; port++)
            {
                bool taken = false;
                foreach (var listener in inUse)
                    if (listener.Port == port) { taken = true; break; }
                if (!taken) return port;
            }
            return DefaultPort;
        }

        public void Host()
        {
            LocalDisplayName = Sanitize(_displayName);
            PlayerPrefs.SetString("displayName", LocalDisplayName);
            _error = "";
            _hostPort = FirstFreePort();
            _tugboat.SetPort(_hostPort);
            if (!_network.ServerManager.StartConnection())
            {
                _error = "Failed to start server (is another host using the port?).";
                return;
            }
            _tugboat.SetClientAddress("127.0.0.1");   // explicit IPv4: "localhost" can hit ::1
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

        /// <summary>Only the title screen draws here now. Once a session is running, the
        /// status and the invite code live behind the hotbar's session icon (SessionPanel) —
        /// they are worth ten seconds when a friend joins, not a permanent box on the city.</summary>
        private void OnGUI()
        {
            Ui.Begin();
            if (!_sessionStarted) DrawTitleScreen();
        }

        private Vector2 _titleScroll;

        /// <summary>Centered title/creation screen. The play/join buttons are pinned to
        /// the bottom of the panel — always visible; the creation section scrolls if the
        /// window is short.</summary>
        private void DrawTitleScreen()
        {
            float w = Mathf.Min(540f, Ui.W - 20f);
            float h = Mathf.Min(600f, Ui.H - 16f);
            var rect = new Rect((Ui.W - w) / 2f, (Ui.H - h) / 2f, w, h);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginVertical();

            var title = new GUIStyle(Theme.HeaderBig) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label("Radiant Pool", title);
            var subtitle = new GUIStyle(Theme.Caps) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label("SELECT A CLASS TO BEGIN YOUR JOURNEY", subtitle);
            var statusStyle = new GUIStyle(Theme.Body) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label($"<color=#d0c5af>{Status}</color>", statusStyle);
            if (_error.Length > 0)
                GUILayout.Label($"<color=#ff7a6b>{_error}</color>", statusStyle);

            // Scrollable middle: name + character creation.
            _titleScroll = GUILayout.BeginScrollView(_titleScroll, GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", Theme.Body, GUILayout.Width(52));
            _displayName = GUILayout.TextField(_displayName, 20);
            GUILayout.EndHorizontal();
            DrawCharacterCreation();
            GUILayout.EndScrollView();

            // Pinned bottom: always-reachable play/join controls.
            bool valid = CharacterBuild.Local.Validate(out string buildError);
            if (!valid)
                GUILayout.Label($"<color=#f2ca50>{buildError}</color>", Theme.Body);
            GUI.enabled = valid;
            var big = new GUIStyle(Theme.BtnPrimary) { fontSize = 16, fixedHeight = 42 };
            if (GUILayout.Button("HOST A CAMPAIGN  (solo or with friends)", big))
                Host();
            GUILayout.BeginHorizontal();
            _joinCode = GUILayout.TextField(_joinCode, 12);
            if (GUILayout.Button("Join with invite code", GUILayout.Width(170))) Join(_joinCode);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>3c: class/race selection + SRD 27-point buy. The server re-validates
        /// everything on spawn, so this panel is pure convenience.</summary>
        private void DrawCharacterCreation()
        {
            var b = CharacterBuild.Local;

            GUILayout.Space(4);
            GUILayout.Label("CLASS", Theme.Caps);
            int newClass = GUILayout.SelectionGrid(b.ClassIndex, ClassNames, 4);
            if (newClass != b.ClassIndex)
            {
                b = CharacterBuild.Default(newClass);   // sensible preset per class
            }
            GUILayout.Label("RACE", Theme.Caps);
            b.RaceIndex = GUILayout.SelectionGrid(b.RaceIndex, RaceNames, 4);

            int cost = 0;
            try { cost = RadiantPool.Rules.PointBuy.TotalCost(b.Str, b.Dex, b.Con, b.Int, b.Wis, b.Cha); }
            catch { /* out-of-range shown by Validate */ }
            GUILayout.Label($"ABILITIES — POINTS {cost}/{RadiantPool.Rules.PointBuy.Budget}",
                Theme.Caps);

            void Row(string label, ref int score)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(34));
                // ASCII hyphen, not U+2212: the theme fonts are not guaranteed to carry
                // the typographic minus, and a missing glyph draws as a tofu box.
                if (GUILayout.Button("-", GUILayout.Width(30), GUILayout.Height(24))
                    && score > 8) score--;
                GUILayout.Label(score.ToString(),
                    new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter },
                    GUILayout.Width(28));
                if (GUILayout.Button("+", GUILayout.Width(30), GUILayout.Height(24))
                    && score < 15) score++;
                int mod = (score - 10) >= 0 ? (score - 10) / 2 : (score - 11) / 2;
                GUILayout.Label($"({(mod >= 0 ? "+" : "")}{mod})", GUILayout.Width(36));
                GUILayout.EndHorizontal();
            }

            // Two columns keep the panel short enough for small windows.
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            Row("STR", ref b.Str);
            Row("DEX", ref b.Dex);
            Row("CON", ref b.Con);
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            Row("INT", ref b.Int);
            Row("WIS", ref b.Wis);
            Row("CHA", ref b.Cha);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            CharacterBuild.Local = b;
        }
    }
}
