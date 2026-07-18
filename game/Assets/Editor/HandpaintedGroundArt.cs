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
            material.SetColor("_BaseColor", Color.Lerp(Color.white, tint, 0.18f));
            material.SetFloat("_Smoothness", 0.02f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
