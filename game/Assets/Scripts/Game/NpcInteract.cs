using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Walk up to the NPC and press E; dialogue reflects the shared quest state
    /// (lines abridged from content/dialogue/npc_council_veresk.json). Choices go to the
    /// server via GameDirector, so both players see quest state advance together.</summary>
    public class NpcInteract : MonoBehaviour
    {
        public string NpcName = "Councilor Veresk";
        public float InteractRange = 3.5f;

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
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;

            if (!_open && InRange())
            {
                var hint = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
                GUI.Box(new Rect(Screen.width / 2f - 130, Screen.height - 60, 260, 28),
                    $"[E] Talk to {NpcName}", hint);
                return;
            }
            if (!_open) return;

            var director = GameDirector.Instance;
            if (director == null) return;

            GUILayout.BeginArea(new Rect(Screen.width / 2f - 260, Screen.height / 2f - 120, 520, 240),
                GUI.skin.box);
            GUILayout.Label($"<b>{NpcName}</b>", new GUIStyle(GUI.skin.label) { richText = true });

            var muster = (QuestState)director.MusterState.Value;
            var clear = (QuestState)director.ClearQuestState.Value;

            if (muster == QuestState.Active)
            {
                GUILayout.Label("\"So the Exchange found us another company willing to brave the " +
                    "old quarters. Aldenmere was a hundred lamplit streets once — we hold six. " +
                    "Prove yourselves at the Old Docks and the Council will pay in gold and gratitude.\"");
                if (GUILayout.Button("We'll clear the docks."))
                { director.CmdDialogueChoice("muster_accept"); _open = false; }
            }
            else if (clear == QuestState.Active)
            {
                GUILayout.Label("\"The squatter gangs hold three yards along the waterfront. Break " +
                    "them all and the docks are ours again. Watch for the rats — they hunt in packs.\"");
                if (GUILayout.Button("We're on it.")) _open = false;
            }
            else if (clear == QuestState.ObjectivesMet)
            {
                GUILayout.Label("\"The lamps are lit on the waterfront tonight — first time in nine " +
                    "years. The Council is in your debt.\"");
                if (GUILayout.Button("The docks are clear. (Turn in)"))
                { director.CmdDialogueChoice("docks_turnin"); _open = false; }
            }
            else if (clear == QuestState.Completed)
            {
                GUILayout.Label("\"Rest and resupply, heroes. The Drowned Market comes next — in the " +
                    "full campaign.\"");
                if (GUILayout.Button("Farewell.")) _open = false;
            }
            GUILayout.EndArea();
        }
    }
}
