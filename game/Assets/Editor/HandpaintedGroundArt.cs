using System.IO;
using RadiantPool.Game;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Creates local, gitignored URP materials from the owned hand-painted grass
    /// pack. Only the selected seamless textures are installed, keeping import/build cost
    /// low while giving campaign cells distinct natural and corrupted ground.</summary>
    public static class HandpaintedGroundArt
    {
        private const string Root = "Assets/Handpainted_Grass_and_Ground_Textures";
        private const string Generated = Root + "/RadiantPoolGenerated";

        public static bool Available => AssetDatabase.IsValidFolder(Root)
                                        && AssetDatabase.FindAssets("t:Texture2D", new[] { Root }).Length > 0;

        public static Material ForTheme(CampaignSiteTheme theme, Color tint)
        {
            string textureName;
            switch (theme)
            {
                case CampaignSiteTheme.Wilds: textureName = "Grass_normal_up"; break;
                case CampaignSiteTheme.Marsh: textureName = "Grass_swamp_dark_up"; break;
                case CampaignSiteTheme.Camp: textureName = "Grass_normal_up"; break;
                case CampaignSiteTheme.Observatory: textureName = "Grass_bluetint_up"; break;
                case CampaignSiteTheme.Enclave:
                case CampaignSiteTheme.Manor:
                case CampaignSiteTheme.Quarter: textureName = "dirt_claydarked_up"; break;
                case CampaignSiteTheme.Caves:
                case CampaignSiteTheme.Crypt:
                case CampaignSiteTheme.Necropolis: textureName = "Grass_overcorrupted_up"; break;
                default: textureName = "Grass_corrupted_up"; break;
            }

            string[] found = AssetDatabase.FindAssets(textureName + " t:Texture2D", new[] { Root });
            if (found.Length == 0) return null;
            string texturePath = AssetDatabase.GUIDToAssetPath(found[0]);
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null && importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }

            if (!AssetDatabase.IsValidFolder(Generated))
            {
                Directory.CreateDirectory(Generated);
                AssetDatabase.Refresh();
            }
            string materialPath = $"{Generated}/M_{textureName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", new Vector2(4.5f, 4.5f));
            var normal = NormalFor(texturePath, textureName);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetTextureScale("_BumpMap", new Vector2(4.5f, 4.5f));
                material.SetFloat("_BumpScale", 0.55f);
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetColor("_BaseColor", Color.Lerp(Color.white, tint, 0.18f));
            material.SetFloat("_Smoothness", 0.02f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D NormalFor(string texturePath, string textureName)
        {
            string sourceDir = Path.GetDirectoryName(texturePath).Replace('\\', '/');
            string stem = textureName.ToLowerInvariant().Replace("_up", "");
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { sourceDir }))
            {
                string candidatePath = AssetDatabase.GUIDToAssetPath(guid);
                if (candidatePath == texturePath || candidatePath.StartsWith(Generated)) continue;
                string candidate = Path.GetFileNameWithoutExtension(candidatePath).ToLowerInvariant();
                bool namedNormal = candidate.Contains("normalmap") || candidate.EndsWith("_n")
                                   || candidate.Contains("_nrm") || candidate.Contains("_bump");
                if (!namedNormal || !candidate.Contains(stem)) continue;
                ConfigureNormal(candidatePath);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(candidatePath);
            }

            string normalPath = $"{Generated}/N_{textureName}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (existing != null)
            {
                ConfigureNormal(normalPath);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            }

            string absolute = Path.Combine(Path.GetDirectoryName(Application.dataPath), texturePath);
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(File.ReadAllBytes(absolute)))
            {
                Object.DestroyImmediate(source);
                return null;
            }
            int width = source.width;
            int height = source.height;
            var pixels = source.GetPixels();
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            Color Sample(int x, int y) => pixels[(y + height) % height * width + (x + width) % width];
            float HeightAt(int x, int y)
            {
                Color c = Sample(x, y);
                return c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = (HeightAt(x + 1, y) - HeightAt(x - 1, y)) * 0.8f;
                float dy = (HeightAt(x, y + 1) - HeightAt(x, y - 1)) * 0.8f;
                Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                output.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f,
                    n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
            }
            output.Apply();
            File.WriteAllBytes(normalPath, output.EncodeToPNG());
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(output);
            AssetDatabase.ImportAsset(normalPath);
            ConfigureNormal(normalPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        }

        private static void ConfigureNormal(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            bool changed = importer.textureType != TextureImporterType.NormalMap
                           || importer.wrapMode != TextureWrapMode.Repeat;
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            if (changed) importer.SaveAndReimport();
        }
    }
}
