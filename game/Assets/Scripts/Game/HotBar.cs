using System.Linq;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Persistent bottom action bar (classic MMO hotbar): attack/dodge/spell
    /// slots while in combat, plus potion, inventory, journal, and settings buttons at
    /// all times. Icon slots use Resources/SpellIcons; combat clicks delegate to
    /// CombatClientUI's target pickers.
    ///
    /// The bar sizes itself to the window: a cleric in combat needs twelve slots, which
    /// at the design size is wider than a small window — the slots shrink to fit rather
    /// than running off both edges (they never shrink below a comfortable click target).</summary>
    public class HotBar : MonoBehaviour
    {
        /// <summary>This frame's bar rect in Ui space — CombatClientUI excludes it from
        /// click-to-move.</summary>
        public static Rect BarRect { get; private set; }

        private const float MaxSlot = 46f;   // design size
        private const float MinSlot = 34f;   // still an easy click target
        private const float HideW = 24f;     // the slim "stow the bar" button on the end

        private float _slot = MaxSlot;
        private Texture2D _hideIcon;

        private static GUIContent IconOnly(string id, string fallback)
        {
            var tex = CombatClientUI.Icon(id);
            return tex != null ? new GUIContent(tex, fallback) : new GUIContent(fallback);
        }

        private void Update()
        {
            // Not while a screen is up: the bar is not on display then, and stowing something
            // the player cannot see is a change they would only find later.
            if (Input.GetKeyDown(KeyCode.H) && !Ui.Typing && !Ui.PanelOpen)
                Ui.BarCollapsed = !Ui.BarCollapsed;
        }

        private void OnGUI()
        {
            Ui.Begin();
            // A screen is up (bags, journal, settings, level-up): it owns the display, and the
            // bar is not allowed to show through it.
            if (Ui.PanelOpen) { BarRect = default; return; }

            var holder = CombatClientUI.LocalPlayerHolder();
            var director = GameDirector.Instance;
            if (holder == null || director == null) { BarRect = default; return; }

            if (Ui.BarCollapsed) { DrawHandle(); return; }

            var combat = CombatManager.Instance;
            bool inCombat = combat != null && combat.InCombat.Value;
            bool myTurn = inCombat && combat.IsMyTurn;
            var cls = (CharacterClass)Mathf.Max(0, holder.ClassIndex.Value);
            string[] spells = inCombat ? CombatClientUI.KnownSpells(cls)
                : System.Array.Empty<string>();

            int combatSlots = inCombat ? 3 + spells.Length : 0;   // attack, dodge, end
            const int utilSlots = 5;                    // potion, bag, journal, session, cog
            int slots = combatSlots + utilSlots;

            // The purse rides on the bar. Gold used to exist only inside the bags and the
            // shops, so it never visibly MOVED — loot landed, the number changed behind a
            // closed panel, and the total read like a placeholder someone had typed in.
            string gold = director.PartyGold.Value.ToString("N0");   // 1,234 — not "1234"
            float goldW = Mathf.Min(96f, Mathf.Max(64f, 26f + gold.Length * 9f));

            // Fit the bar to the window: shrink the slots before ever overflowing the edge.
            float gap = inCombat ? 14f : 0f;
            float chrome = gap + 28f + goldW + HideW + 6f;
            float avail = Ui.W - 24f;
            _slot = Mathf.Clamp((avail - chrome) / slots - 4f, MinSlot, MaxSlot);

            float w = slots * (_slot + 4f) + chrome;
            var rect = new Rect(Ui.W / 2f - w / 2f, Ui.H - _slot - 38f, w, _slot + 16f);
            BarRect = rect;

            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();

            GUILayout.Label(new GUIContent($"<color=#f2ca50><b>{gold}</b>g</color>",
                    $"{gold} gold — the party's purse"),
                new GUIStyle(Theme.Body)
                {
                    richText = true, fontSize = 13, wordWrap = false,
                    alignment = TextAnchor.MiddleCenter
                },
                GUILayout.Width(goldW), GUILayout.Height(_slot));

            if (inCombat)
            {
                GUI.enabled = myTurn && combat.ActionLeft;
                if (Btn("attack", "Attack")) CombatClientUI.Instance?.PickAttack();
                if (Btn("dodge", "Dodge")) combat.CmdDodge();

                foreach (var id in spells)
                {
                    var spell = SpellLibrary.Get(id);
                    bool usable = myTurn && (spell.Level == 0
                        ? combat.ActionLeft
                        : (spell.IsBonusAction ? combat.BonusLeft : combat.ActionLeft)
                          && combat.MySlots.Sum() > 0);
                    GUI.enabled = usable;
                    if (Btn(id, spell.Name)) CombatClientUI.Instance?.PickSpell(id);
                }

                GUI.enabled = myTurn;
                if (Btn("end_turn", "End Turn (Space)")) combat.CmdEndTurn();
                GUI.enabled = true;
                GUILayout.Space(gap);
            }

            // Potions work IN COMBAT: drinking one is an Action (SRD), so it is live only
            // on your turn while you still have an action — and the tooltip says the price
            // out loud, because spending your attack on a drink is a real decision.
            int potions = director.Stash.Count(s => s == "potion_healing");
            bool canDrink = potions > 0 && (!inCombat || (myTurn && combat.ActionLeft));
            string potionTip = potions == 0
                ? "No potions — buy one at the Salvage Exchange"
                : inCombat
                    ? $"Drink Potion of Healing — costs your ACTION, heals 2d4+2 ({potions} left)"
                    : $"Drink Potion of Healing — heals 2d4+2 ({potions} left)";

            GUI.enabled = canDrink;
            bool drink = GUILayout.Button(
                new GUIContent(CombatClientUI.Icon("potion"), potionTip),
                GUILayout.Width(_slot), GUILayout.Height(_slot));
            // The badge hangs off the button GUILayout actually placed, so it can never
            // drift out of register with the bar the way hand-computed offsets did.
            var potionRect = GUILayoutUtility.GetLastRect();
            if (drink)
            {
                if (inCombat) combat.CmdDrinkPotion();
                else director.CmdUsePotion();
            }
            GUI.enabled = true;

            if (Btn("bag", "Inventory (I)")) Ui.Toggle(Ui.Panel.Inventory);
            if (Btn("journal", "Journal (J)")) Ui.Toggle(Ui.Panel.Journal);
            // Session: the invite code and who you are connected to, on demand instead of
            // parked in the corner of the screen for the whole campaign.
            if (GUILayout.Button(new GUIContent(SessionPanel.Icon, "Session / invite code"),
                    GUILayout.Width(_slot), GUILayout.Height(_slot)))
                Ui.Toggle(Ui.Panel.Session);
            if (Btn("settings", "Settings (Esc)")) Ui.Toggle(Ui.Panel.Settings);

            // Stow the whole bar. A generated chevron, not a glyph: the body font has no "▼"
            // and a missing glyph renders as a tofu box. The icon is DRAWN OVER the button
            // rather than handed to it as content — the skin's padding leaves a 24 px-wide
            // button almost no content area, and the icon came out invisible.
            GUILayout.Space(6);
            if (_hideIcon == null) _hideIcon = MakeHideIcon();
            bool stow = GUILayout.Button(new GUIContent("", "Hide the bar (H)"),
                GUILayout.Width(HideW), GUILayout.Height(_slot));
            var stowRect = GUILayoutUtility.GetLastRect();
            GUI.DrawTexture(new Rect(stowRect.center.x - 8f, stowRect.center.y - 8f, 16f, 16f),
                _hideIcon);
            if (stow) Ui.BarCollapsed = true;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (potions > 0)
                GUI.Label(new Rect(rect.x + potionRect.xMax - 18f,
                        rect.y + potionRect.yMax - 16f, 24f, 16f),
                    $"<b><color=#f2ca50>{potions}</color></b>", Theme.Caps);

            if (GUI.tooltip.Length > 0)
            {
                var tipStyle = new GUIStyle(Theme.Caps)
                    { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                GUI.Label(new Rect(rect.x, rect.y - 20f, rect.width, 18f),
                    GUI.tooltip, tipStyle);
            }
        }

        private bool Btn(string icon, string tooltip) =>
            GUILayout.Button(IconOnly(icon, tooltip),
                GUILayout.Width(_slot), GUILayout.Height(_slot));

        /// <summary>Stowed: one slim handle where the bar was, so hiding it is never a one-way
        /// door — the same rule the minimap's collapsed pill follows. BarRect still reports the
        /// handle, so combat click-to-move can't fire through it, and the steering arrow keeps
        /// docking off the bar's top edge (it just gets more room).</summary>
        private void DrawHandle()
        {
            var rect = new Rect(Ui.W / 2f - 62f, Ui.H - 34f, 124f, 26f);
            BarRect = rect;
            var style = new GUIStyle(GUI.skin.button)
                { fontSize = 11, wordWrap = false, clipping = TextClipping.Overflow };
            if (GUI.Button(rect, "SHOW BAR (H)", style)) Ui.BarCollapsed = false;
        }

        /// <summary>Down chevron over a bar — "put this away". Generated, because the fonts
        /// carry no arrow glyphs and tint follows GUI.color like the minimap's icons.</summary>
        private static Texture2D MakeHideIcon()
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = Mathf.Abs(x - (s - 1) / 2f);
                    // "v": arms wide at the top, meeting at a point above the floor bar the
                    // bar tucks into. Texture rows run bottom-up.
                    bool chevron = y >= 12 && y <= 26 && Mathf.Abs(fx - (y - 12)) <= 2.4f
                                   && fx <= 11f;
                    bool floorBar = y >= 4 && y <= 7 && fx <= 11f;
                    tex.SetPixel(x, y,
                        chevron || floorBar ? Theme.OnSurface : Color.clear);
                }
            tex.Apply();
            return tex;
        }
    }
}
