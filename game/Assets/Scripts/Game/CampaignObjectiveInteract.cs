using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Client prompt for one server-authoritative non-combat quest objective.
    /// Recoveries, rescues, controls, evidence, and alliance decisions share this path,
    /// so mouse/UI input and automated tests reach the same public server method.</summary>
    public class CampaignObjectiveInteract : MonoBehaviour
    {
        public int ZoneIndex = -1;
        public float InteractRange = 4.5f;

        private PlayerCharacterHolder LocalHolder() =>
            FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);

        private bool InRange()
        {
            var holder = LocalHolder();
            return holder != null
                   && Vector3.Distance(holder.transform.position, transform.position) <= InteractRange;
        }

        private bool Ready(GameDirector director) => director != null
            && ZoneIndex >= 0 && ZoneIndex < director.Zones.Length
            && director.GetZoneState(ZoneIndex) == QuestState.Active
            && ZoneIndex < director.ZoneClearedCounts.Count
            && director.ZoneClearedCounts[ZoneIndex]
               >= director.Zones[ZoneIndex].RequiredEncounters
            && !director.IsSiteActionComplete(ZoneIndex);

        private void Update()
        {
            var director = GameDirector.Instance;
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (combat || !InRange() || !Ready(director))
            {
                Ui.Close(Ui.Panel.SiteAction);
                return;
            }
            if (!Input.GetKeyDown(KeyCode.E) || Ui.Typing) return;
            if (Ui.PanelOpen && !Ui.IsOpen(Ui.Panel.SiteAction)) return;
            Ui.Toggle(Ui.Panel.SiteAction);
        }

        private void OnGUI()
        {
            Ui.Begin();
            var director = GameDirector.Instance;
            if (director == null || ZoneIndex < 0 || ZoneIndex >= director.Zones.Length
                || !InRange() || director.IsSiteActionComplete(ZoneIndex)) return;
            var cfg = director.Zones[ZoneIndex];
            if (director.GetZoneState(ZoneIndex) != QuestState.Active) return;

            if (!Ui.IsOpen(Ui.Panel.SiteAction))
            {
                if (!Ui.PanelOpen)
                {
                    bool ready = Ready(director);
                    Theme.DrawToast(Ui.W / 2f, 102f, ready
                        ? $"[E] {cfg.SiteAction}"
                        : $"Secure {cfg.DisplayName} first");
                }
                return;
            }

            var rect = Ui.Fit(520f, 320f);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(cfg.QuestName, Theme.Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(30f), GUILayout.Height(24f)))
                Ui.Close(Ui.Panel.SiteAction);
            GUILayout.EndHorizontal();
            GUILayout.Space(12f);
            GUILayout.Label(cfg.SiteAction, Theme.Body);
            GUILayout.FlexibleSpace();

            bool hasChoice = !string.IsNullOrEmpty(cfg.ChoiceA)
                             && !string.IsNullOrEmpty(cfg.ChoiceB);
            if (hasChoice)
            {
                if (GUILayout.Button(cfg.ChoiceA, Theme.BtnPrimary, GUILayout.Height(42f)))
                    Resolve(director, 0);
                GUILayout.Space(7f);
                if (GUILayout.Button(cfg.ChoiceB, Theme.BtnPrimary, GUILayout.Height(42f)))
                    Resolve(director, 1);
            }
            else if (GUILayout.Button("Complete objective", Theme.BtnPrimary,
                         GUILayout.Height(42f)))
                Resolve(director, 0);
            GUILayout.EndArea();
        }

        private void Resolve(GameDirector director, int choice)
        {
            director.CmdResolveSiteAction(ZoneIndex, choice);
            Ui.Close(Ui.Panel.SiteAction);
        }
    }
}
