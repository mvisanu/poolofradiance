using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Integration for the beasts we generate ourselves (scripts/make_beasts.py,
    /// headless Blender): the CC0 packs ship no bear and no rat, so those two monsters
    /// used to fall through to the red-capsule fallback. Original geometry, so no licence
    /// attaches — see IP-CHECKLIST.md.
    ///
    /// Unlike QuaterniusArt/KayKitArt these are NOT normalised to human height: they are
    /// modelled at true scale (a bear is long and low, not 1.85 m tall), so the prefab
    /// keeps the mesh's own size. They are static — CharacterVisuals tolerates a missing
    /// Animator, so no controller is built.</summary>
    public static class GeneratedArt
    {
        private const string Root = "Assets/Art/Generated";
        private const string PrefabDir = "Assets/Resources/Characters";

        /// <summary>Flat colours matching what the generator baked in (the FBX material
        /// survives import, but we want a URP Lit material Unity is happy to batch).</summary>
        private static readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>
        {
            { "Bear", new Color(0.28f, 0.17f, 0.09f) },
            { "Rat", new Color(0.34f, 0.30f, 0.27f) },
        };

        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                Debug.Log("[Bootstrap] No generated beasts; skipping.");
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                SetupModel(path, name);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Bootstrap] Generated beasts ready (Bear, Rat).");
        }

        private static void SetupModel(string path, string name)
        {
            var colour = Colours.TryGetValue(name, out var c) ? c : Color.gray;
            string matPath = $"{Root}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.color = colour;
            mat.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(mat);

            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            var map = importer.GetExternalObjectMap();
            foreach (string embedded in AssetDatabase.LoadAllAssetsAtPath(path)
                         .OfType<Material>().Select(m => m.name).Distinct())
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded);
                if (map.ContainsKey(id)) continue;
                importer.AddRemap(id, mat);
                changed = true;
            }
            if (changed) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            BuildPrefab(path, name);
        }

        private static void BuildPrefab(string path, string name)
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/Resources", "Characters");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.localScale = Vector3.one;   // already true-to-life metres
            PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(instance);
        }
    }
}
