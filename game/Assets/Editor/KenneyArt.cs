using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Integration for the CC0 Kenney kits under Assets/Art/Kenney: remaps every
    /// FBX's embedded material to one shared URP atlas material per kit (they import as
    /// Standard-shader and would render magenta in URP), and places models with
    /// bounds-normalized scale so layout code can think in meters.</summary>
    public static class KenneyArt
    {
        private const string Root = "Assets/Art/Kenney";
        private enum SurfaceKind { Default, Water, Metal, Cloth, Skin, WoodStone }
        private static readonly string[] WaterWords = { "water", "fountain" };

        private static readonly string[] MetalWords =
            { "metal", "iron", "steel", "blade", "sword", "axe", "weapon", "lantern" };
        private static readonly string[] ClothWords =
            { "cloth", "canvas", "banner", "flag", "tent", "sail" };
        private static readonly string[] SkinWords =
            { "skin", "fur", "hide", "leather", "animal" };
        private static readonly string[] WoodStoneWords =
            { "wood", "plank", "log", "tree", "rock", "stone", "road", "wall", "brick" };

        public static void SetupMaterials()
        {
            foreach (string kit in new[] { "FantasyTown", "Pirate", "Nature", "Survival" })
            {
                string kitPath = $"{Root}/{kit}";
                if (!AssetDatabase.IsValidFolder(kitPath)) continue;

                // Atlas kits get one URP material pointing at their colormap. Kits
                // without an atlas (Nature ships plain solid-color materials) get one
                // URP material per embedded material name, copying its color.
                var atlasTex = AssetDatabase.FindAssets("colormap t:Texture2D", new[] { kitPath })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                    .FirstOrDefault();
                Material atlasMat = null;
                if (atlasTex != null)
                {
                    string matPath = $"{kitPath}/M_{kit}.mat";
                    atlasMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (atlasMat == null)
                    {
                        atlasMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        atlasMat.SetTexture("_BaseMap", atlasTex);
                        AssetDatabase.CreateAsset(atlasMat, matPath);
                    }
                    // Outside the creation guard so a retune reaches already-baked mats.
                    atlasMat.SetFloat("_Smoothness", SmoothnessFor(kit, atlasTex.name));
                }
                else if (!AssetDatabase.IsValidFolder($"{kitPath}/Mats"))
                {
                    AssetDatabase.CreateFolder(kitPath, "Mats");
                }

                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { kitPath }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null) continue;
                    bool changed = false;
                    var embedded = AssetDatabase.LoadAllAssetsAtPath(path)
                        .OfType<Material>().Distinct().ToList();
                    var existing = importer.GetExternalObjectMap();
                    foreach (var src in embedded)
                    {
                        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name);
                        var target = atlasMat != null
                            ? AtlasMat(kitPath, kit, atlasTex, path, src.name)
                            : ColorMat(kitPath, src);
                        if (existing.TryGetValue(id, out var current) && current == target) continue;
                        importer.AddRemap(id, target);
                        changed = true;
                    }
                    if (changed)
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Bootstrap] Kenney materials remapped to URP.");
        }

        private static float SmoothnessFor(params string[] names)
        {
            switch (SurfaceFor(names))
            {
                case SurfaceKind.Water: return 0.68f;
                case SurfaceKind.Metal: return 0.58f;
                case SurfaceKind.Cloth: return 0.11f;
                case SurfaceKind.Skin: return 0.25f;
                case SurfaceKind.WoodStone: return 0.20f;
                default: return 0.18f;
            }
        }

        private static SurfaceKind SurfaceFor(params string[] names)
        {
            string value = string.Join(" ", names).ToLowerInvariant();
            if (WaterWords.Any(word => value.Contains(word))) return SurfaceKind.Water;
            if (MetalWords.Any(word => value.Contains(word))) return SurfaceKind.Metal;
            if (ClothWords.Any(word => value.Contains(word))) return SurfaceKind.Cloth;
            if (SkinWords.Any(word => value.Contains(word))) return SurfaceKind.Skin;
            if (WoodStoneWords.Any(word => value.Contains(word))) return SurfaceKind.WoodStone;
            return SurfaceKind.Default;
        }

        private static Material AtlasMat(string kitPath, string kit, Texture2D atlas,
            params string[] names)
        {
            SurfaceKind kind = SurfaceFor(names);
            string suffix = kind == SurfaceKind.Default ? "" : "_" + kind;
            string path = $"{kitPath}/M_{kit}{suffix}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", atlas);
            material.SetFloat("_Smoothness", SmoothnessFor(names));
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>URP material per embedded-material name, copying its base color —
        /// shared across every model in the kit that uses the same material name.</summary>
        private static Material ColorMat(string kitPath, Material source)
        {
            string safe = string.Concat(source.name.Split(System.IO.Path.GetInvalidFileNameChars()));
            string matPath = $"{kitPath}/Mats/M_{safe}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null)
            {
                mat.SetFloat("_Smoothness", SmoothnessFor(source.name));
                return mat;
            }
            Color color = Color.white;
            if (source.HasProperty("_BaseColor")) color = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color")) color = source.GetColor("_Color");
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", SmoothnessFor(source.name));
            AssetDatabase.CreateAsset(mat, matPath);
            return mat;
        }

        /// <summary>Instantiates a kit model. targetSize > 0 uniformly rescales so the
        /// chosen dimension (horizontal footprint or height) equals targetSize meters —
        /// deterministic layout regardless of the FBX's native units.</summary>
        public static GameObject Place(string kit, string model, Vector3 pos,
            float yRot = 0f, float targetSize = 0f, bool byHeight = false)
        {
            string path = $"{Root}/{kit}/{model}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[Bootstrap] Missing Kenney model {path}");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = $"{kit}_{model}";
            go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

            if (targetSize > 0f)
            {
                var bounds = CombinedBounds(go);
                float current = byHeight
                    ? bounds.size.y
                    : Mathf.Max(bounds.size.x, bounds.size.z);
                if (current > 0.0001f)
                    go.transform.localScale = Vector3.one * (targetSize / current);
            }

            // Sit the model's renderer base centered on the requested position.
            var b = CombinedBounds(go);
            var baseCenter = new Vector3(b.center.x, b.min.y, b.center.z);
            go.transform.position += pos - baseCenter;
            return go;
        }

        private static Bounds CombinedBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            return bounds;
        }
    }
}
