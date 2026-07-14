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

        private Vector2 _scroll;

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
                            "Locked sites appear when the Council authorizes them.", Theme.Body);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < director.Zones.Length; i++)
            {
                var state = director.GetZoneState(i);
                if (state == QuestState.Locked) continue;
                var cfg = director.Zones[i];
                string status = state == QuestState.Active ? "ACTIVE"
                    : state == QuestState.ObjectivesMet ? "TURN IN" : "RECLAIMED";
                GUILayout.BeginVertical(Theme.ParchmentStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{cfg.DisplayName}</b>\n{status}", Theme.BodyInk);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Travel", Theme.BtnPrimary,
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
    }
}
