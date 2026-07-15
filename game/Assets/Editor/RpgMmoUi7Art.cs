using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RadiantPool.Game;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Discovery-first adapter for Evil's licensed RPG & MMO UI 7 package.
    /// It consumes only sprite/texture art, crops atlas sprites into runtime IMGUI textures,
    /// and never depends on the package's legacy scripts, prefabs, or folder layout.</summary>
    public static class RpgMmoUi7Art
    {
        private const string OutputDir = "Assets/Resources/UI/RpgMmoUi7";

        private sealed class Candidate
        {
            public string Label;
            public string Path;
            public Texture2D Texture;
            public Rect Rect;
            public float Area => Rect.width * Rect.height;
            public float Aspect => Rect.height > 0f ? Rect.width / Rect.height : 1f;
        }

        private static readonly Dictionary<string, string> FallbackRole =
            new Dictionary<string, string>
            {
                { "panel_plain", "panel" },
                { "button_hover", "button" },
                { "button_pressed", "button_hover" },
                { "primary", "button" },
                { "primary_hover", "button_hover" },
                { "field", "button" },
                { "slot", "button" },
                { "slider", "bar" },
                { "thumb", "slot" },
                { "toggle_off", "slot" },
                { "toggle_on", "primary" },
                { "tooltip", "panel_plain" },
                { "scroll_track", "bar" },
                { "scroll_thumb", "thumb" },
                { "tab", "button" },
                { "tab_active", "primary" },
                { "statbar_overlay", "bar" },
                { "xpbar", "bar" },
                { "xpbar_fill", "primary" },
                { "currency_gold", "slot" },
                { "divider", "panel_plain" },
                { "decoration", "panel_plain" },
                { "glow", "panel_plain" }
            };

        // UI 7 ships its controls as composable primitives rather than state-complete IMGUI
        // skins. Prefer these stable semantic paths; scoring remains a version-tolerant fallback.
        private static readonly Dictionary<string, string> PreferredSuffix =
            new Dictionary<string, string>
            {
                { "panel", "/textures/miscellaneous/general/general_container_2.png" },
                { "panel_plain", "/textures/miscellaneous/general/general_container_3.png" },
                { "button", "/textures/controls/buttons/rectangular/button_rect_foreground.png" },
                { "button_hover", "/textures/controls/buttons/rectangular/button_rect_foreground.png" },
                { "button_pressed", "/textures/controls/buttons/rectangular/button_rect_foreground.png" },
                { "primary", "/textures/controls/buttons/rectangular/button_rect_foreground.png" },
                { "primary_hover", "/textures/controls/buttons/rectangular/button_rect_foreground.png" },
                { "field", "/textures/controls/select fields/selectfield_background.png" },
                { "slot", "/textures/miscellaneous/icon slots/iconslot_background.png" },
                { "bar", "/textures/miscellaneous/loading bar/loadingbar_background.png" },
                { "slider", "/textures/miscellaneous/loading bar/loadingbar_background.png" },
                { "thumb", "/textures/controls/sliders/slider_horizontal_handle.png" },
                { "toggle_off", "/textures/controls/buttons/rectangular/button_rect_foreground.png" },
                { "toggle_on", "/textures/controls/buttons/rectangular/button_rect_foreground.png" },
                { "tooltip", "/textures/hud/tooltip/tooltip_background.png" },
                { "scroll_track", "/textures/miscellaneous/loading bar/loadingbar_background.png" },
                { "scroll_thumb", "/textures/controls/scroll bars/scrollbar_handle.png" },
                { "tab", "/textures/hud/chat/tabs/chat_tab_background.png" },
                { "tab_active", "/textures/hud/chat/tabs/chat_tab_active.png" },
                { "statbar_overlay", "/textures/hud/action bar/stat bar/statbar_overlay.png" },
                { "xpbar", "/textures/hud/action bar/xp bar/xpbar_background.png" },
                { "xpbar_fill", "/textures/hud/action bar/xp bar/xpbar_fill.png" },
                { "currency_gold", "/textures/miscellaneous/currencies/currency_gold.png" },
                { "divider", "/textures/controls/separators/separator_horizontal.png" },
                { "decoration", "/textures/miscellaneous/general/general_decoration.png" },
                { "glow", "/textures/miscellaneous/general/general_glow.png" }
            };

        [MenuItem("RadiantPool/Art/Rebuild RPG MMO UI 7 Skin")]
        public static void Bake()
        {
            var candidates = Discover().ToArray();
            if (candidates.Length == 0)
            {
                Debug.Log("[RpgMmoUi7] package art not imported; keeping generated UI fallback.");
                return;
            }

            var chosen = new Dictionary<string, Candidate>();
            foreach (string role in RpgMmoUi7Skin.Roles)
            {
                Candidate best = Preferred(role, candidates) ?? candidates
                    .Select(c => new { Candidate = c, Score = Score(role, c) })
                    .Where(x => x.Score >= 45)
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Candidate.Area)
                    .Select(x => x.Candidate)
                    .FirstOrDefault();
                if (best != null) chosen[role] = best;
            }

            foreach (string role in RpgMmoUi7Skin.Roles)
                if (!chosen.ContainsKey(role) && FallbackRole.TryGetValue(role, out string fallback)
                    && chosen.TryGetValue(fallback, out Candidate candidate))
                    chosen[role] = candidate;

            if (!chosen.ContainsKey("panel") || !chosen.ContainsKey("button"))
            {
                Debug.LogError("[RpgMmoUi7] licensed images were found, but no panel/button " +
                               "skin could be identified. Check the role-scoring log and package version.");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absoluteOutput = Path.Combine(projectRoot,
                OutputDir.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(absoluteOutput);

            int baked = 0;
            foreach (var pair in chosen)
            {
                string assetPath = OutputDir + "/" + pair.Key + ".png";
                if (!BakeCrop(pair.Value, assetPath, pair.Key,
                        pair.Key == "panel" || pair.Key == "panel_plain"
                        || pair.Key == "tooltip" ? 512 : 256))
                    continue;
                baked++;
                Debug.Log($"[RpgMmoUi7] {pair.Key} <- {pair.Value.Label}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RpgMmoUi7Skin.Invalidate();
            Debug.Log($"[RpgMmoUi7] READY - baked {baked}/{RpgMmoUi7Skin.Roles.Length} " +
                      $"runtime roles from {candidates.Length} licensed art candidates.");
        }

        private static IEnumerable<Candidate> Discover()
        {
            string[] imagePaths = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith(OutputDir, StringComparison.OrdinalIgnoreCase)
                            && LooksLikePackPath(p) && IsImagePath(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            bool hasCuratedLocalCopy = imagePaths.Any(p => p.StartsWith(
                "Assets/LocalLicensed/RpgMmoUi7/", StringComparison.OrdinalIgnoreCase));
            if (hasCuratedLocalCopy)
                imagePaths = imagePaths.Where(p => p.StartsWith(
                    "Assets/LocalLicensed/RpgMmoUi7/", StringComparison.OrdinalIgnoreCase)).ToArray();

            foreach (string path in imagePaths)
            {
                var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
                if (sprites.Length > 0)
                {
                    foreach (Sprite sprite in sprites)
                    {
                        Rect rect;
                        try { rect = sprite.textureRect; }
                        catch { rect = sprite.rect; }
                        if (sprite.texture == null || rect.width < 8f || rect.height < 8f) continue;
                        yield return new Candidate
                        {
                            Label = path + " :: " + sprite.name,
                            Path = path,
                            Texture = sprite.texture,
                            Rect = rect
                        };
                    }
                    continue;
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null || texture.width < 8 || texture.height < 8) continue;
                yield return new Candidate
                {
                    Label = path + " :: " + Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    Texture = texture,
                    Rect = new Rect(0f, 0f, texture.width, texture.height)
                };
            }
        }

        private static Candidate Preferred(string role, IEnumerable<Candidate> candidates)
        {
            if (!PreferredSuffix.TryGetValue(role, out string suffix)) return null;
            return candidates.FirstOrDefault(c => c.Path.Replace('\\', '/').ToLowerInvariant()
                .EndsWith(suffix, StringComparison.Ordinal));
        }

        private static bool LooksLikePackPath(string path)
        {
            string compact = new string(path.ToLowerInvariant()
                .Where(char.IsLetterOrDigit).ToArray());
            return compact.Contains("rpg") && compact.Contains("mmo")
                   && (compact.Contains("ui7") || compact.Contains("userinterface7"));
        }

        private static bool IsImagePath(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".tga" || extension == ".psd"
                   || extension == ".jpg" || extension == ".jpeg" || extension == ".tif"
                   || extension == ".tiff";
        }

        private static int Score(string role, Candidate candidate)
        {
            string text = candidate.Label.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
            int score = 0;
            bool any(params string[] words) => words.Any(text.Contains);
            int add(int points, params string[] words) => any(words) ? points : 0;

            if (any("demo", "example", "preview", "screenshot", "logo")) score -= 180;
            if (any("portrait", "avatar", "character", "skill icon", "item icon")) score -= 90;
            if (candidate.Rect.width >= 32f && candidate.Rect.height >= 16f) score += 8;

            switch (role)
            {
                case "panel":
                case "panel_plain":
                    score += add(100, "window") + add(90, "panel") + add(65, "frame")
                             + add(35, "background", "back");
                    if (candidate.Area > 16000f) score += 35;
                    if (candidate.Aspect > 0.55f && candidate.Aspect < 2.4f) score += 20;
                    if (any("button", "slot", "bar", "tab")) score -= 90;
                    if (role == "panel_plain") score += add(35, "inner", "simple", "small");
                    break;
                case "button":
                    score += add(125, "button") + add(30, "normal", "default", "idle");
                    if (candidate.Aspect > 1.45f) score += 30;
                    if (any("hover", "over", "pressed", "down", "active", "selected")) score -= 80;
                    break;
                case "button_hover":
                    score += add(100, "button") + add(115, "hover", "over", "highlight");
                    if (candidate.Aspect > 1.45f) score += 25;
                    break;
                case "button_pressed":
                    score += add(100, "button") + add(115, "pressed", "down", "click");
                    break;
                case "primary":
                case "primary_hover":
                    score += add(95, "button") + add(65, "primary", "active", "selected", "gold", "blue");
                    if (role == "primary_hover") score += add(75, "hover", "over", "highlight");
                    if (candidate.Aspect > 1.45f) score += 25;
                    break;
                case "field":
                    score += add(150, "input", "textfield", "text field", "search", "editbox", "edit box")
                             + add(35, "field");
                    if (candidate.Aspect > 2f) score += 35;
                    break;
                case "slot":
                    score += add(135, "slot", "cell", "item frame", "icon frame") + add(25, "square");
                    if (candidate.Aspect > 0.7f && candidate.Aspect < 1.35f) score += 35;
                    break;
                case "bar":
                    score += add(120, "progress", "health", "experience", "loading") + add(65, "bar")
                             + add(35, "back", "background", "empty", "frame");
                    if (candidate.Aspect > 2.2f) score += 35;
                    if (any("fill", "front")) score -= 45;
                    break;
                case "slider":
                    score += add(150, "slider") + add(55, "bar", "track", "back");
                    if (candidate.Aspect > 2.2f) score += 35;
                    if (any("thumb", "handle", "knob")) score -= 75;
                    break;
                case "thumb":
                case "scroll_thumb":
                    score += add(145, "thumb", "handle", "knob") + add(50, "scroll");
                    if (candidate.Aspect > 0.65f && candidate.Aspect < 1.5f) score += 25;
                    break;
                case "toggle_off":
                    score += add(130, "checkbox", "check box", "toggle") + add(65, "off", "unchecked", "normal");
                    if (any("on", "checked", "active")) score -= 50;
                    break;
                case "toggle_on":
                    score += add(130, "checkbox", "check box", "toggle") + add(75, "on", "checked", "active");
                    break;
                case "tooltip":
                    score += add(145, "tooltip", "tool tip") + add(75, "popup", "dialog") + add(45, "panel");
                    if (candidate.Area > 4000f) score += 25;
                    break;
                case "scroll_track":
                    score += add(125, "scroll") + add(80, "track", "background", "back", "bar");
                    if (any("thumb", "handle")) score -= 75;
                    break;
                case "tab":
                    score += add(145, "tab") + add(45, "normal", "idle");
                    if (any("active", "selected", "hover")) score -= 50;
                    break;
                case "tab_active":
                    score += add(145, "tab") + add(85, "active", "selected", "hover");
                    break;
            }
            return score;
        }

        private static bool BakeCrop(Candidate candidate, string assetPath, string role, int maxSize)
        {
            int width = Mathf.Max(8, Mathf.RoundToInt(candidate.Rect.width));
            int height = Mathf.Max(8, Mathf.RoundToInt(candidate.Rect.height));
            float downscale = Mathf.Min(1f, maxSize / (float)Mathf.Max(width, height));
            width = Mathf.Max(8, Mathf.RoundToInt(width * downscale));
            height = Mathf.Max(8, Mathf.RoundToInt(height * downscale));

            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Vector2 scale = new Vector2(candidate.Rect.width / candidate.Texture.width,
                    candidate.Rect.height / candidate.Texture.height);
                Vector2 offset = new Vector2(candidate.Rect.x / candidate.Texture.width,
                    candidate.Rect.y / candidate.Texture.height);
                Graphics.Blit(candidate.Texture, rt, scale, offset);
                RenderTexture.active = rt;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                Color tint = RoleTint(role);
                if (tint != Color.white)
                {
                    Color[] pixels = copy.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color p = pixels[i];
                        pixels[i] = new Color(p.r * tint.r, p.g * tint.g, p.b * tint.b, p.a);
                    }
                    copy.SetPixels(pixels);
                }
                copy.Apply();

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absolute = Path.Combine(projectRoot,
                    assetPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(absolute));
                File.WriteAllBytes(absolute, copy.EncodeToPNG());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RpgMmoUi7] could not bake {assetPath} from " +
                                 $"{candidate.Label}: {ex.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return true;
        }

        private static Color RoleTint(string role)
        {
            switch (role)
            {
                case "button": return new Color(0.58f, 0.50f, 0.42f, 1f);
                case "button_hover": return new Color(0.92f, 0.72f, 0.30f, 1f);
                case "button_pressed": return new Color(1f, 0.80f, 0.28f, 1f);
                case "primary": return new Color(0.32f, 0.52f, 1f, 1f);
                case "primary_hover": return new Color(0.46f, 0.66f, 1f, 1f);
                case "toggle_off": return new Color(0.46f, 0.42f, 0.38f, 1f);
                case "toggle_on": return new Color(0.30f, 0.62f, 0.46f, 1f);
                case "tab_active": return new Color(0.42f, 0.60f, 1f, 1f);
                default: return Color.white;
            }
        }
    }
}
