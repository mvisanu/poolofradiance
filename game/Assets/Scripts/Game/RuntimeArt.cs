using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Materials for objects built at RUNTIME (primitives, FX, markers).
    ///
    /// `GameObject.CreatePrimitive` hands you Unity's built-in **Default-Material**, which
    /// is a *Standard* (built-in pipeline) shader. URP cannot render it, so in a PLAYER
    /// BUILD every such primitive comes out **magenta** — the quest beacon, the blood, the
    /// spell bolts, the turn ring, the hover square, the monster capsule. It looks fine in
    /// the editor, which is exactly why it survived this long.
    ///
    /// So: never keep the material a primitive was born with. Repaint it from here. The
    /// source materials are ASSETS under Resources/Fx (created by the bootstrap), because
    /// `Shader.Find` alone is not enough either — a shader nothing references is stripped
    /// out of the build and Find returns null (see M_GridOverlay).</summary>
    public static class RuntimeArt
    {
        private const string LitPath = "Fx/M_Solid";          // URP Lit, opaque
        private const string GlowPath = "Fx/M_GridOverlay";   // URP Unlit, transparent

        private static Material Clone(string resourcePath, string fallbackShader)
        {
            var src = Resources.Load<Material>(resourcePath);
            if (src != null) return new Material(src);
            var shader = Shader.Find(fallbackShader);
            return shader != null ? new Material(shader) : null;
        }

        /// <summary>Opaque lit material — blood, bolts, capsules, the quest beacon.</summary>
        public static Material Lit(Color color, float emission = 0f)
        {
            var m = Clone(LitPath, "Universal Render Pipeline/Lit");
            if (m == null) return null;
            Tint(m, color);
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * emission);
            }
            return m;
        }

        /// <summary>Transparent unlit material — ground rings, hover squares, flares.
        /// Alpha in the colour is honoured, so these can fade out.</summary>
        public static Material Glow(Color color, float emission = 0f)
        {
            var m = Clone(GlowPath, "Universal Render Pipeline/Unlit");
            if (m == null) return null;
            Tint(m, color);
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * emission);
            }
            return m;
        }

        /// <summary>Repaints a primitive and hands back its material for later tweaking.</summary>
        public static Material Paint(GameObject go, Color color, float emission = 0f,
            bool glow = false)
        {
            var m = glow ? Glow(color, emission) : Lit(color, emission);
            if (m == null) return null;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = m;
            return m;
        }

        /// <summary>URP's colour lives in _BaseColor; `material.color` alone misses it on
        /// some shader variants.</summary>
        public static void Tint(Material m, Color color)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }
    }
}
