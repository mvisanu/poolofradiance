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

        // There are objective anchors at every campaign site, but they all share one
        // SiteAction panel. Only the anchor that opened it may close it; otherwise every
        // distant anchor closes the local anchor's panel later in the same frame.
        private static CampaignObjectiveInteract _panelOwner;

        public bool OwnsOpenPanel => _panelOwner == this && Ui.IsOpen(Ui.Panel.SiteAction);

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
                if (_panelOwner == this) CloseOwnedPanel();
                return;
            }
            if (!Input.GetKeyDown(KeyCode.E) || Ui.Typing) return;
            TryInteract();
        }

        /// <summary>The one definition of pressing E at a campaign objective. Runtime
        /// input and the built-player regression both use this method.</summary>
        public bool TryInteract()
        {
            var director = GameDirector.Instance;
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (combat || !InRange() || !Ready(director)) return false;
            if (Ui.PanelOpen && !Ui.IsOpen(Ui.Panel.SiteAction)) return false;

            if (OwnsOpenPanel)
                CloseOwnedPanel();
            else
            {
                _panelOwner = this;
                Ui.Show(Ui.Panel.SiteAction);
            }
            return true;
        }

        private void CloseOwnedPanel()
        {
            if (_panelOwner != this) return;
            Ui.Close(Ui.Panel.SiteAction);
            _panelOwner = null;
        }

        private void OnDisable()
        {
            if (_panelOwner == this) CloseOwnedPanel();
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
            if (_panelOwner != this) return;

            var rect = Ui.Fit(520f, 320f);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(cfg.QuestName, Theme.Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(30f), GUILayout.Height(24f)))
                CloseOwnedPanel();
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
            CloseOwnedPanel();
        }
    }
}
