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
            "\"An orc warband under Karg Splitjaw has dug into the sunken quarter south of " +
            "the walls. Break his pickets and his war-tent — the Council will not assault " +
            "the Temple with raiders at its back. Mind the wilds on the road: spiders, " +
            "bears, goblin ambushes.\"",
            "\"Only the Temple remains. The cult there serves something that wears our own " +
            "Warden like a cloak. Break their five circles and end it.\"",
            "\"Sorrel was right: the Flame fled through the Lightwell gate into the ward " +
            "beyond. Seal its three breaches before it can kindle the city again. Follow " +
            "the gold waypoint northeast from the Lightwell.\""
        };

        private static readonly string[] ZoneTurnins =
        {
            "\"The lamps are lit on the waterfront tonight — first time in nine years. The " +
            "Council is in your debt.\"",
            "\"You gave the drowned their rest. The market bells will ring again because of you.\"",
            "\"Splitjaw's warband is broken and the south road is ours again. Nothing stands " +
            "between us and the Temple now.\"",
            "\"Sorrel lives — freed after nine years in that thing's grip. The Flame has fled " +
            "deep into the Lightwell. The old gate is open; finish this beyond it.\"",
            "\"The breaches are cold and the ward is ours. This time the Hollow Flame has " +
            "nowhere left to run.\""
        };

        private bool _open;

        /// <summary>How many sellswords Veresk can add to the party right now. This is
        /// deliberately independent of quest/muster state: companions are session-owned,
        /// so a solo player loading any campaign state must be able to refill the party.</summary>
        public static int AvailableRecruitSlots()
        {
            int partySize = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Count(p => p.ClassIndex.Value >= 0);
            return Mathf.Max(0,
                RadiantPool.Rules.PartyComposition.MaxPartySize - partySize);
        }

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
            if (Input.GetKeyDown(KeyCode.E) && InRange() && !Ui.PanelOpen) _open = !_open;
            if (_open && !InRange()) _open = false;
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;
            if (Ui.PanelOpen) return;   // a screen is up: Veresk waits behind it

            if (!_open && InRange())
            {
                // Under the quest banner, not over the hotbar: the bottom of the screen is
                // hotbar + steering arrow, and the prompt used to land on top of them.
                Theme.DrawToast(Ui.W / 2f, 102f, $"[E] Talk to {NpcName}");
                return;
            }
            if (!_open) return;

            var director = GameDirector.Instance;
            if (director == null || director.Zones.Length == 0) return;

            GUILayout.BeginArea(Ui.Fit(520f, 280f), Theme.PanelStyle);
            GUILayout.Label(NpcName, Theme.Header);

            // Sellswords: offered whenever the party is short of four. The roster they
            // bring is PartyComposition's (a healer, then two damage classes) — say so, so
            // the player knows the hire covers what the party is missing.
            int free = AvailableRecruitSlots();
            bool RecruitButton()
            {
                if (free > 0 && GUILayout.Button(
                        $"We could use sellswords. (hire {free}: support and damage)"))
                {
                    director.CmdRecruitCompanions();
                    _open = false;
                    return true;
                }
                return false;
            }

            // Always available while there is room — before the quest-state branches.
            // Previously this was only called from Muster and Active, so it vanished at
            // turn-in and forever after campaign completion even though companions vanish
            // between sessions and the player was solo again.
            if (RecruitButton()) { GUILayout.EndArea(); return; }

            if ((QuestState)director.MusterState.Value == QuestState.Active)
            {
                Say("\"So the Exchange found us another company willing to brave the " +
                    "old quarters. Aldenmere was a hundred lamplit streets once — we hold six. " +
                    "Prove yourselves at the Old Docks and the Council will pay in gold and gratitude.\"");
                if (GUILayout.Button("We'll clear the docks.", Theme.BtnPrimary))
                { director.CmdDialogueChoice("muster_accept"); _open = false; }
                GUILayout.EndArea();
                return;
            }

            // Find the first zone that still needs attention.
            for (int i = 0; i < director.Zones.Length; i++)
            {
                var state = director.GetZoneState(i);
                if (state == QuestState.Active)
                {
                    Say(i < ZoneBriefs.Length ? ZoneBriefs[i]
                        : $"\"Clear {director.Zones[i].DisplayName}, then follow the gold " +
                          "turn-in marker back to me at Council Hall.\"");
                    if (GUILayout.Button("We're on it.")) _open = false;
                    GUILayout.EndArea();
                    return;
                }
                if (state == QuestState.ObjectivesMet)
                {
                    Say(i < ZoneTurnins.Length ? ZoneTurnins[i]
                        : $"\"{director.Zones[i].DisplayName} is clear. Well done.\"");
                    if (GUILayout.Button($"Turn in at Council Hall: {director.Zones[i].DisplayName}",
                            Theme.BtnPrimary))
                    { director.CmdDialogueChoice($"turnin_{i}"); _open = false; }
                    GUILayout.EndArea();
                    return;
                }
            }

            if (director.CampaignComplete.Value)
            {
                Say("\"Whatever you ask of this Council, heroes — it is yours. " +
                    "Aldenmere stands free.\"");
                if (GUILayout.Button("It was our honor.")) _open = false;
            }
            else
            {
                Say("\"The Council sits day and night until every quarter is reclaimed.\"");
                if (GUILayout.Button("Farewell.")) _open = false;
            }
            GUILayout.EndArea();
        }

        /// <summary>NPC speech on a parchment card, per the theme mockups.</summary>
        private static void Say(string text)
        {
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            GUILayout.Label($"<i>{text}</i>", Theme.BodyInk);
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }
    }
}
