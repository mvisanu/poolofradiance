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
        /// <summary>The thin XP track immediately above the action-bar chrome.</summary>
        public static Rect XpStripRect { get; private set; }

        private const float MaxSlot = 46f;   // design size
        private const float MinSlot = 34f;   // still an easy click target
        private const float HideW = 24f;     // the slim "stow the bar" button on the end

        private float _slot = MaxSlot;
        private Texture2D _hideIcon;
        private GUIStyle _keyTag;

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
            if (Ui.PanelOpen) { BarRect = default; XpStripRect = default; return; }

            var holder = CombatClientUI.LocalPlayerHolder();
            var director = GameDirector.Instance;
            if (holder == null || director == null)
            { BarRect = default; XpStripRect = default; return; }

            if (Ui.BarCollapsed) { DrawHandle(holder); return; }

            var combat = CombatManager.Instance;
            bool inCombat = combat != null && combat.InCombat.Value;
            bool myTurn = inCombat && combat.CanAcceptPlayerInput;
            var cls = (CharacterClass)Mathf.Max(0, holder.ClassIndex.Value);
            string[] spells = inCombat ? CombatClientUI.KnownSpells(cls)
                : System.Array.Empty<string>();

            int combatSlots = inCombat ? 3 + spells.Length : 0;   // attack, dodge, end + spells
            const int utilSlots = 5;                    // potion, bag, journal, session, cog
            int slots = combatSlots + utilSlots;

            // The purse rides on the bar. Gold used to exist only inside the bags and the
            // shops, so it never visibly MOVED — loot landed, the number changed behind a
            // closed panel, and the total read like a placeholder someone had typed in.
            string gold = director.PartyGold.Value.ToString("N0");   // 1,234 — not "1234"
            var coin = Theme.CurrencyGoldTex;                        // licensed gold glyph
            float goldW = Mathf.Min(112f, Mathf.Max(64f, 26f + gold.Length * 9f))
                          + (coin != null ? 18f : 0f);

            // Fit the bar to the window. If even comfortable minimum-size slots would not
            // fit, combat actions become a first row and utilities a second row.
            float gap = inCombat ? 14f : 0f;
            float chrome = gap + 28f + goldW + HideW + 6f;
            float avail = Ui.W - 24f;
            float oneRowFit = (avail - chrome) / slots - 4f;
            bool wrapCombat = inCombat && oneRowFit < MinSlot;
            if (wrapCombat)
            {
                float combatFit = (avail - 28f) / combatSlots - 4f;
                float utilityFit = (avail - (28f + goldW + HideW + 6f)) / utilSlots - 4f;
                _slot = Mathf.Clamp(Mathf.Min(combatFit, utilityFit), 26f, MaxSlot);
            }
            else
                _slot = Mathf.Clamp(oneRowFit, 26f, MaxSlot);

            float combatWidth = combatSlots * (_slot + 4f) + 28f;
            float utilityWidth = utilSlots * (_slot + 4f) + 28f + goldW + HideW + 6f;
            float w = wrapCombat ? Mathf.Min(avail, Mathf.Max(combatWidth, utilityWidth))
                : Mathf.Min(avail, slots * (_slot + 4f) + chrome);
            float barHeight = wrapCombat ? _slot * 2f + 24f : _slot + 16f;
            var rect = new Rect(Ui.W / 2f - w / 2f, Ui.H - barHeight - 22f, w, barHeight);
            BarRect = WithXpStrip(holder, rect);

            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginVertical();
            if (wrapCombat)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                DrawCombatActions(combat, myTurn, spells);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();

            if (inCombat && !wrapCombat)
            {
                DrawCombatActions(combat, myTurn, spells);
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
                Theme.SlotStyle,
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

            if (Btn("bag", "Inventory (I)", "I")) Ui.Toggle(Ui.Panel.Inventory);
            if (Btn("journal", "Journal (J)", "J")) Ui.Toggle(Ui.Panel.Journal);
            // Session: the invite code and who you are connected to, on demand instead of
            // parked in the corner of the screen for the whole campaign.
            if (GUILayout.Button(new GUIContent(SessionPanel.Icon, "Session / invite code"),
                    Theme.SlotStyle,
                    GUILayout.Width(_slot), GUILayout.Height(_slot)))
                Ui.Toggle(Ui.Panel.Session);
            if (Btn("settings", "Settings (Esc)", "ESC")) Ui.Toggle(Ui.Panel.Settings);

            // Compact purse chip at the right end, after every action and utility slot.
            GUILayout.Label(new GUIContent(
                    coin != null ? $"<color=#f2ca50><b>{gold}</b></color>"
                                 : $"<color=#f2ca50><b>{gold}</b>g</color>",
                    $"{gold} gold — the party's purse"),
                new GUIStyle(Theme.Body)
                {
                    richText = true, fontSize = 13, wordWrap = false,
                    alignment = TextAnchor.MiddleCenter,
                    padding = coin != null ? new RectOffset(16, 0, 0, 0) : new RectOffset()
                },
                GUILayout.Width(goldW), GUILayout.Height(_slot));
            if (coin != null)
            {
                var gr = GUILayoutUtility.GetLastRect();
                Theme.CurrencyGlyph(gr.x + 2f, gr.center.y, 16f);
            }

            // Stow the whole bar. A generated chevron, not a glyph: the body font has no "▼"
            // and a missing glyph renders as a tofu box. The icon is DRAWN OVER the button
            // rather than handed to it as content — the skin's padding leaves a 24 px-wide
            // button almost no content area, and the icon came out invisible.
            GUILayout.Space(6);
            if (_hideIcon == null) _hideIcon = MakeHideIcon();
            bool stow = GUILayout.Button(new GUIContent("", "Hide the bar (H)"),
                Theme.SlotStyle,
                GUILayout.Width(HideW), GUILayout.Height(_slot));
            var stowRect = GUILayoutUtility.GetLastRect();
            GUI.DrawTexture(new Rect(stowRect.center.x - 8f, stowRect.center.y - 8f, 16f, 16f),
                _hideIcon);
            if (stow) Ui.BarCollapsed = true;

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndArea();

            if (potions > 0)
                GUI.Label(new Rect(rect.x + potionRect.xMax - 18f,
                        rect.y + potionRect.yMax - 16f, 24f, 16f),
                    $"<b><color=#f2ca50>{potions}</color></b>", Theme.Caps);

            // Only the BAR's own hints, and only above the health strip. GUI.tooltip is global
            // to the frame — the bar was printing the MINIMAP's "Show map (M)" whenever that
            // was the last control hovered, straight across the health readout.
            if (GUI.tooltip.Length > 0 && rect.Contains(Ui.Mouse))
            {
                var tipStyle = new GUIStyle(Theme.Caps)
                    { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                GUI.Label(new Rect(BarRect.x, BarRect.y - 20f, BarRect.width, 18f),
                    GUI.tooltip, tipStyle);
            }
        }

        private void DrawCombatActions(CombatManager combat, bool myTurn, string[] spells)
        {
            GUI.enabled = myTurn && combat.ActionLeft;
            if (Btn("attack", "Attack (A)", "A"))
            {
                var ui = CombatClientUI.Instance;
                if (ui != null) ui.PickAttack();
            }
            if (Btn("dodge", "Dodge")) combat.CmdDodge();

            foreach (var id in spells)
            {
                var spell = SpellLibrary.Get(id);
                bool usable = myTurn && (spell.Level == 0
                    ? combat.ActionLeft
                    : (spell.IsBonusAction ? combat.BonusLeft : combat.ActionLeft)
                      && combat.MySlots.Skip(Mathf.Max(0, spell.Level - 1)).Any(s => s > 0));
                GUI.enabled = usable;
                if (Btn(id, spell.Name))
                {
                    var ui = CombatClientUI.Instance;
                    if (ui != null) ui.PickSpell(id);
                }
            }

            GUI.enabled = myTurn;
            if (Btn("end_turn", "End Turn (Space)", "SPC")) combat.CmdEndTurn();
            GUI.enabled = true;
        }

        private bool Btn(string icon, string tooltip, string key = "")
        {
            bool pressed = GUILayout.Button(IconOnly(icon, tooltip), Theme.SlotStyle,
                GUILayout.Width(_slot), GUILayout.Height(_slot));
            if (key.Length > 0) DrawKeyTag(GUILayoutUtility.GetLastRect(), key);
            return pressed;
        }

        private void DrawKeyTag(Rect slot, string key)
        {
            if (_keyTag == null)
            {
                _keyTag = new GUIStyle(Theme.Caps)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 8,
                    wordWrap = false,
                    padding = new RectOffset(2, 2, 0, 0)
                };
                _keyTag.normal.textColor = Theme.OnSurface;
            }
            float w = Mathf.Max(12f, key.Length * 6f + 4f);
            var tag = new Rect(slot.xMax - w - 2f, slot.y + 2f, w, 11f);
            Color old = GUI.color;
            GUI.color = new Color(0.09f, 0.07f, 0.05f, 0.92f);
            GUI.DrawTexture(tag, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(tag, key, _keyTag);
        }

        /// <summary>Add the shared thin XP track above the bar and publish both as BarRect,
        /// so the log, target picker, steering arrow, and battlefield hit-test all dock from
        /// the complete bottom-centre HUD footprint.</summary>
        private static Rect WithXpStrip(PlayerCharacterHolder holder, Rect bar)
        {
            const float h = 8f;
            XpStripRect = new Rect(bar.x, bar.y - h - 4f, bar.width, h);
            ProgressUI.DrawXpTrack(XpStripRect, holder, true);
            return Rect.MinMaxRect(bar.x, XpStripRect.y, bar.xMax, bar.yMax);
        }

        /// <summary>Stowed: one slim handle where the bar was, so hiding it is never a one-way
        /// door — the same rule the minimap's collapsed pill follows. BarRect still reports the
        /// handle, so combat click-to-move can't fire through it, and the steering arrow keeps
        /// docking off the bar's top edge (it just gets more room). The XP track remains above
        /// the handle; health lives in the top-left player frame.</summary>
        private void DrawHandle(PlayerCharacterHolder holder)
        {
            var rect = new Rect(Ui.W / 2f - 62f, Ui.H - 34f, 124f, 26f);
            BarRect = WithXpStrip(holder, rect);
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
