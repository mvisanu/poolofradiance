using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Item art for the UI: Resources/ItemIcons/&lt;id&gt;.png, baked from each item's
    /// own model by the editor (EditorTools.ItemIconBaker) so the icon and the thing in the
    /// character's hand are the same object. Cached on first use.
    ///
    /// A missing icon is never fatal: Get returns null and every caller keeps its text row,
    /// so an item added without art still buys, sells and equips.</summary>
    public static class ItemIcon
    {
        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>();

        public static Texture2D Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (!Cache.TryGetValue(itemId, out var tex))
            {
                tex = Resources.Load<Texture2D>($"ItemIcons/{itemId}");
                // An enchanted variant has no art of its own — a Longsword +1 is drawn with
                // the Longsword's icon (same object, plus a glow the name carries).
                if (tex == null)
                {
                    string baseId = GameItem.Get(itemId)?.BaseId;
                    if (!string.IsNullOrEmpty(baseId))
                        tex = Resources.Load<Texture2D>($"ItemIcons/{baseId}");
                }
                Cache[itemId] = tex;   // null is cached too: don't hit Resources every frame
            }
            return tex;
        }

        /// <summary>Lays out an icon of the given size, or an equally sized gap when the item
        /// has no art — so rows line up either way.</summary>
        public static void Draw(string itemId, float size)
        {
            var tex = Get(itemId);
            var rect = GUILayoutUtility.GetRect(size, size,
                GUILayout.Width(size), GUILayout.Height(size));
            if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
        }
    }
}
