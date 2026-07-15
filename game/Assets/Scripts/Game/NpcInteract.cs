using System.Linq;
using RadiantPool.Rules;
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
        private Vector2 _scroll;

        /// <summary>How many companions Veresk can add right now. This is independent of
        /// quest state: new hires and saved former companions remain available at any point.</summary>
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

        /// <summary>The single client-side path behind every new-hire button. Built-player
        /// tests call this too, so the displayed choice and the RPC cannot drift apart.</summary>
        public void ChooseHire(CharacterClass choice)
        {
            if (!PartyComposition.HireChoices.Contains(choice)) return;
            GameDirector.Instance?.CmdHireCompanionClass((int)choice);
        }

        public void ChooseRehire(string companionName) =>
            GameDirector.Instance?.CmdRehireCompanion(companionName);

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

            GUILayout.BeginArea(Ui.Fit(600f, 520f), Theme.PanelStyle);
            GUILayout.Label(NpcName, Theme.Header);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            void FinishPanel()
            {
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            // One explicit role/class choice per open slot; released named companions are
            // listed below the new-hire choices and retain their prior sheet and equipment.
            int free = AvailableRecruitSlots();
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            GUILayout.Label($"PARTY ROSTER - {free} OPEN SLOT{(free == 1 ? "" : "S")}", Theme.Caps);
            if (free > 0)
            {
                GUILayout.Label("Choose the class and job you want to add:", Theme.BodyInk);
                for (int i = 0; i < PartyComposition.HireChoices.Length; i += 2)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = i; j < i + 2 && j < PartyComposition.HireChoices.Length; j++)
                    {
                        var choice = PartyComposition.HireChoices[j];
                        string role = PartyComposition.RoleOf(choice).ToString().ToUpperInvariant();
                        string detail = choice switch
                        {
                            CharacterClass.Fighter => "armored front line",
                            CharacterClass.Cleric => "healing and support",
                            CharacterClass.Rogue => "precise weapon damage",
                            _ => "ranged spell damage"
                        };
                        if (GUILayout.Button($"{role}: {choice}\n{detail}",
                                GUILayout.Height(43f), GUILayout.ExpandWidth(true)))
                            ChooseHire(choice);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else GUILayout.Label(
                "The active party is full. Release a companion from Inventory (I) to open a slot.",
                Theme.BodyInk);

            var waiting = new System.Collections.Generic.List<(string name, CharacterClass cls)>();
            foreach (string summary in director.CompanionRoster)
                if (GameDirector.TryParseCompanionSummary(summary, out string name,
                        out CharacterClass cls, out bool active) && !active)
                    waiting.Add((name, cls));
            if (waiting.Count > 0)
            {
                GUILayout.Space(5);
                GUILayout.Label("REHIRE FORMER COMPANIONS", Theme.Caps);
                foreach (var former in waiting)
                {
                    GUI.enabled = free > 0;
                    string role = PartyComposition.RoleOf(former.cls).ToString();
                    if (GUILayout.Button($"Rehire {former.name} - {former.cls} ({role})"))
                        ChooseRehire(former.name);
                    GUI.enabled = true;
                }
            }
            GUILayout.EndVertical();
            GUILayout.Space(7);

            // Recruitment stays above the quest branches, so it remains available before
            // muster, between turn-ins, and after the campaign finale.
            if ((QuestState)director.MusterState.Value == QuestState.Active)
            {
                Say("\"So the Exchange found us another company willing to brave the " +
                    "old quarters. Aldenmere was a hundred lamplit streets once — we hold six. " +
                    "Prove yourselves at the Old Docks and the Council will pay in gold and gratitude.\"");
                if (GUILayout.Button("We'll clear the docks.", Theme.BtnPrimary))
                { director.CmdDialogueChoice("muster_accept"); _open = false; }
                FinishPanel();
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
                    FinishPanel();
                    return;
                }
                if (state == QuestState.ObjectivesMet)
                {
                    Say(i < ZoneTurnins.Length ? ZoneTurnins[i]
                        : $"\"{director.Zones[i].DisplayName} is clear. Well done.\"");
                    if (GUILayout.Button($"Turn in at Council Hall: {director.Zones[i].DisplayName}",
                            Theme.BtnPrimary))
                    { director.CmdDialogueChoice($"turnin_{i}"); _open = false; }
                    FinishPanel();
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
            FinishPanel();
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
