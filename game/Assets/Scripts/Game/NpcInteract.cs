using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Walk up to the NPC and press E; dialogue reflects the shared quest state
    /// across the whole zone chain (lines abridged from content/dialogue JSON). Choices
    /// go to the server via GameDirector, so all players see quest state advance.</summary>
    public class NpcInteract : MonoBehaviour
    {
        public string NpcName = "Councilor Veresk";
        public float InteractRange = 3.5f;

        private static readonly string[] ZoneBriefs =
        {
            "\"The squatter gangs hold three yards along the waterfront. Break them all and " +
            "the docks are ours again. Watch for the rats — they hunt in packs.\"",
            "\"The flooded Market is worse — the dead of the eruption never left. Lay them " +
            "to rest, all four hauntings, and mind the Toll-Keeper.\"",
            "\"Only the Temple remains. The cult there serves something that wears our own " +
            "Warden like a cloak. Break their five circles and end it.\""
        };

        private static readonly string[] ZoneTurnins =
        {
            "\"The lamps are lit on the waterfront tonight — first time in nine years. The " +
            "Council is in your debt.\"",
            "\"You gave the drowned their rest. The market bells will ring again because of you.\"",
            "\"Sorrel lives — freed after nine years in that thing's grip. The Flame has fled " +
            "deep into the Lightwell, and Aldenmere is ours to the last stone.\""
        };

        private bool _open;

        private Transform LocalPlayer()
        {
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);
            return holder != null ? holder.transform : null;
        }

        private bool InRange()
        {
            var player = LocalPlayer();
            return player != null
                && Vector3.Distance(player.position, transform.position) <= InteractRange;
        }

        private void Update()
        {
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (combat) { _open = false; return; }
            if (Input.GetKeyDown(KeyCode.E) && InRange()) _open = !_open;
            if (_open && !InRange()) _open = false;
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;

            if (!_open && InRange())
            {
                var hint = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
                GUI.Box(new Rect(Ui.W / 2f - 130, Ui.H - 60, 260, 28),
                    $"[E] Talk to {NpcName}", hint);
                return;
            }
            if (!_open) return;

            var director = GameDirector.Instance;
            if (director == null || director.Zones.Length == 0) return;

            GUILayout.BeginArea(new Rect(Ui.W / 2f - 260, Ui.H / 2f - 130, 520, 260),
                GUI.skin.box);
            GUILayout.Label($"<b>{NpcName}</b>", new GUIStyle(GUI.skin.label) { richText = true });

            // Sellswords: offered whenever the party is short of four.
            int partySize = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Count(p => p.ClassIndex.Value >= 0);
            void RecruitButton()
            {
                if (partySize < 4 && GUILayout.Button(
                        $"We could use sellswords. (hire {4 - partySize} companions)"))
                { director.CmdRecruitCompanions(); _open = false; }
            }

            if ((QuestState)director.MusterState.Value == QuestState.Active)
            {
                GUILayout.Label("\"So the Exchange found us another company willing to brave the " +
                    "old quarters. Aldenmere was a hundred lamplit streets once — we hold six. " +
                    "Prove yourselves at the Old Docks and the Council will pay in gold and gratitude.\"");
                if (GUILayout.Button("We'll clear the docks."))
                { director.CmdDialogueChoice("muster_accept"); _open = false; }
                RecruitButton();
                GUILayout.EndArea();
                return;
            }

            // Find the first zone that still needs attention.
            for (int i = 0; i < director.Zones.Length; i++)
            {
                var state = director.GetZoneState(i);
                if (state == QuestState.Active)
                {
                    GUILayout.Label(i < ZoneBriefs.Length ? ZoneBriefs[i]
                        : $"\"Clear {director.Zones[i].DisplayName}, then return to me.\"");
                    if (GUILayout.Button("We're on it.")) _open = false;
                    RecruitButton();
                    GUILayout.EndArea();
                    return;
                }
                if (state == QuestState.ObjectivesMet)
                {
                    GUILayout.Label(i < ZoneTurnins.Length ? ZoneTurnins[i]
                        : $"\"{director.Zones[i].DisplayName} is clear. Well done.\"");
                    if (GUILayout.Button($"{director.Zones[i].DisplayName} is clear. (Turn in)"))
                    { director.CmdDialogueChoice($"turnin_{i}"); _open = false; }
                    GUILayout.EndArea();
                    return;
                }
            }

            if (director.CampaignComplete.Value)
            {
                GUILayout.Label("\"Whatever you ask of this Council, heroes — it is yours. " +
                    "Aldenmere stands free.\"");
                if (GUILayout.Button("It was our honor.")) _open = false;
            }
            else
            {
                GUILayout.Label("\"The Council sits day and night until every quarter is reclaimed.\"");
                if (GUILayout.Button("Farewell.")) _open = false;
            }
            GUILayout.EndArea();
        }
    }
}
