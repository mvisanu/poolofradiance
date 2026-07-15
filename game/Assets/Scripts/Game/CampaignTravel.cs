using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>A hub directory or return waystone. Hub stones open the unlocked-location
    /// list; site stones return the whole co-op party to Council Hall.</summary>
    public class CampaignTravel : MonoBehaviour
    {
        public bool IsDirectory;
        public bool ReturnsToCouncil;
        public float InteractRange = 4.5f;
        public string DisplayName = "Council Waystone";

        /// <summary>Visual-QA evidence from the most recent open-directory frame.</summary>
        public int LastRecommendedZoneDrawn { get; private set; } = -1;

        private Vector2 _scroll;

        private void Start()
        {
            if (!IsDirectory) return;
            string[] args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-waystonehighlighttest") >= 0)
                StartCoroutine(WaystoneHighlightSelfTest());
            int capture = System.Array.IndexOf(args, "-waystonecapture");
            if (capture >= 0 && capture + 1 < args.Length)
                StartCoroutine(WaystoneHighlightCapture(args[capture + 1]));
        }

        private PlayerCharacterHolder LocalHolder() =>
            FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);

        private bool InRange()
        {
            var holder = LocalHolder();
            return holder != null
                && Vector3.Distance(holder.transform.position, transform.position) <= InteractRange;
        }

        /// <summary>Server-side mirror of the UI's proximity rule. A client cannot invoke
        /// fast travel from the wilderness by calling the RPC directly.</summary>
        public bool Allows(Vector3 playerPosition, int destinationZone)
        {
            if (Vector3.Distance(playerPosition, transform.position) > InteractRange + 0.75f)
                return false;
            return destinationZone < 0 ? ReturnsToCouncil : IsDirectory;
        }

        private void Update()
        {
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (combat || !InRange())
            {
                if (IsDirectory) Ui.Close(Ui.Panel.Travel);
                return;
            }
            if (!Input.GetKeyDown(KeyCode.E) || Ui.Typing) return;

            if (IsDirectory)
            {
                if (Ui.PanelOpen && !Ui.IsOpen(Ui.Panel.Travel)) return;
                Ui.Toggle(Ui.Panel.Travel);
            }
            else if (ReturnsToCouncil && !Ui.PanelOpen)
            {
                var director = GameDirector.Instance;
                if (director != null) director.CmdTravelTo(-1);
            }
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (!InRange()) return;
            if (!Ui.IsOpen(Ui.Panel.Travel))
            {
                if (!Ui.PanelOpen)
                    Theme.DrawToast(Ui.W / 2f, 102f,
                        IsDirectory ? $"[E] Open {DisplayName}" : "[E] Return to Council Hall");
                return;
            }
            if (!IsDirectory) return;

            var director = GameDirector.Instance;
            if (director == null) return;
            var rect = Ui.Fit(560f, 500f);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Council Waystone Network", Theme.Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(30f), GUILayout.Height(24f)))
                Ui.Close(Ui.Panel.Travel);
            GUILayout.EndHorizontal();
            GUILayout.Label("Travel together to any commissioned or reclaimed location. " +
                            "Locked sites appear when the Council authorizes them. " +
                            "Green marks your tracked quest destination.", Theme.Body);

            int recommended = QuestTracker.RecommendedTravelZoneIndex(director);
            LastRecommendedZoneDrawn = recommended;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < director.Zones.Length; i++)
            {
                var state = director.GetZoneState(i);
                if (state == QuestState.Locked) continue;
                var cfg = director.Zones[i];
                string status = state == QuestState.Active ? "ACTIVE"
                    : state == QuestState.ObjectivesMet ? "TURN IN" : "RECLAIMED";
                bool questRoute = i == recommended;
                GUILayout.BeginVertical(questRoute
                    ? Theme.QuestRouteStyle : Theme.ParchmentStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{cfg.DisplayName}</b>\n{status}" +
                                (questRoute
                                    ? "\n<color=#2e7d32><b>QUEST DESTINATION</b></color>"
                                    : ""), Theme.BodyInk);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(questRoute ? "Travel now" : "Travel",
                        questRoute ? Theme.BtnRoute : Theme.BtnPrimary,
                        GUILayout.Width(92f), GUILayout.Height(34f)))
                {
                    director.CmdTravelTo(i);
                    Ui.Close(Ui.Panel.Travel);
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(3f);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void RestoreQuestState(GameDirector director, int oldMuster,
            int[] oldStates, bool hadTrackedPreference, string oldTrackedZone)
        {
            director.MusterState.Value = oldMuster;
            for (int i = 0; i < oldStates.Length; i++)
                director.ZoneStates[i] = oldStates[i];
            if (hadTrackedPreference)
                PlayerPrefs.SetString(QuestTracker.TrackedQuestPreference, oldTrackedZone);
            else
                PlayerPrefs.DeleteKey(QuestTracker.TrackedQuestPreference);
            PlayerPrefs.Save();
        }

        private System.Collections.IEnumerator WaystoneHighlightSelfTest()
        {
            yield return new WaitForSeconds(8f);
            var director = GameDirector.Instance;
            var tracker = QuestTracker.Instance;
            if (director == null || !director.IsServerStarted || tracker == null
                || director.ZoneStates.Count < 2)
            {
                Debug.Log("[WaystoneHighlightTest] FAIL - server, tracker, or zones missing");
                yield break;
            }

            int oldMuster = director.MusterState.Value;
            int[] oldStates = director.ZoneStates.ToArray();
            bool hadTracked = PlayerPrefs.HasKey(QuestTracker.TrackedQuestPreference);
            string oldTracked = PlayerPrefs.GetString(QuestTracker.TrackedQuestPreference, "");
            director.MusterState.Value = (int)QuestState.Completed;
            for (int i = 0; i < director.ZoneStates.Count; i++)
                director.ZoneStates[i] = (int)QuestState.Locked;
            director.ZoneStates[0] = (int)QuestState.Active;
            director.ZoneStates[1] = (int)QuestState.Active;

            tracker.TrackZone(1);
            int trackedRoute = QuestTracker.RecommendedTravelZoneIndex(director);
            director.ZoneStates[1] = (int)QuestState.ObjectivesMet;
            int turnInRoute = QuestTracker.RecommendedTravelZoneIndex(director);
            director.ZoneStates[1] = (int)QuestState.Completed;
            tracker.TrackZone(0);
            int fallbackRoute = QuestTracker.RecommendedTravelZoneIndex(director);

            Color card = Theme.QuestRouteTex.GetPixel(16, 16);
            Color button = Theme.BtnRouteTex.GetPixel(16, 16);
            bool greenAssets = card.g > card.r && card.g > card.b
                               && button.g > button.r && button.g > button.b;
            bool pass = trackedRoute == 1 && turnInRoute == -1
                        && fallbackRoute == 0 && greenAssets;
            RestoreQuestState(director, oldMuster, oldStates, hadTracked, oldTracked);
            Debug.Log($"[WaystoneHighlightTest] {(pass ? "PASS" : "FAIL")} - " +
                      $"tracked quest route {trackedRoute}, turn-in route {turnInRoute}, " +
                      $"active fallback {fallbackRoute}, green card/button {greenAssets}");
        }

        private System.Collections.IEnumerator WaystoneHighlightCapture(string path)
        {
            yield return new WaitForSeconds(8f);
            var director = GameDirector.Instance;
            var holder = LocalHolder();
            if (director == null || !director.IsServerStarted || holder == null
                || director.ZoneStates.Count == 0)
            {
                Debug.Log("[WaystoneCapture] FAIL - server, player, or zones missing");
                yield break;
            }

            int oldMuster = director.MusterState.Value;
            int[] oldStates = director.ZoneStates.ToArray();
            bool hadTracked = PlayerPrefs.HasKey(QuestTracker.TrackedQuestPreference);
            string oldTracked = PlayerPrefs.GetString(QuestTracker.TrackedQuestPreference, "");
            director.MusterState.Value = (int)QuestState.Completed;
            for (int i = 0; i < director.ZoneStates.Count; i++)
                director.ZoneStates[i] = (int)QuestState.Locked;
            director.ZoneStates[0] = (int)QuestState.Active;
            PlayerPrefs.SetString(QuestTracker.TrackedQuestPreference,
                director.Zones[0].ZoneId);
            PlayerPrefs.Save();

            var controller = holder.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            holder.transform.position = transform.position + Vector3.right * 1.5f;
            if (controller != null) controller.enabled = true;
            Ui.Show(Ui.Panel.Travel);
            LastRecommendedZoneDrawn = -1;
            yield return new WaitForSeconds(2f);
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(2f);
            bool drewGreenRoute = LastRecommendedZoneDrawn == 0;

            Ui.Close(Ui.Panel.Travel);
            RestoreQuestState(director, oldMuster, oldStates, hadTracked, oldTracked);
            Debug.Log($"[WaystoneCapture] {(drewGreenRoute ? "PASS" : "FAIL")} - " +
                      $"green tracked quest row {(drewGreenRoute ? "drawn" : "MISSING")} to {path}");
        }
    }
}
