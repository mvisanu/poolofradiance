using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Runtime side of the licensed RPG & MMO UI 7 adapter. Editor bootstrap bakes
    /// selected package sprites into ignored Resources/UI/RpgMmoUi7 PNGs. Public builds and
    /// contributors without the licensed pack transparently keep the generated fallback.</summary>
    public static class RpgMmoUi7Skin
    {
        public const string ResourceRoot = "UI/RpgMmoUi7/";

        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>();

        public static readonly string[] Roles =
        {
            "panel", "panel_plain", "button", "button_hover", "button_pressed",
            "primary", "primary_hover", "field", "slot", "bar", "slider",
            "thumb", "toggle_off", "toggle_on", "tooltip", "scroll_track",
            "scroll_thumb", "tab", "tab_active"
        };

        public static Texture2D Get(string role)
        {
            if (string.IsNullOrEmpty(role)) return null;
            if (!Cache.TryGetValue(role, out var texture))
            {
                texture = Resources.Load<Texture2D>(ResourceRoot + role);
                Cache[role] = texture; // cache missing roles too
            }
            return texture;
        }

        public static Texture2D Get(string role, Texture2D fallback)
        {
            Texture2D texture = Get(role);
            return texture == null ? fallback : texture;
        }

        public static bool Available => Get("panel") != null && Get("button") != null;

        public static int LoadedRoleCount
        {
            get
            {
                int count = 0;
                foreach (string role in Roles)
                    if (Get(role) != null) count++;
                return count;
            }
        }

        public static void Invalidate() => Cache.Clear();
    }
}
