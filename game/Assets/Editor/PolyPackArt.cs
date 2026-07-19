using System.Collections.Generic;
using System.Linq;
using RadiantPool.Game;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Discovery-first integration for the locally owned low-poly environment
    /// packs. Licensed assets remain gitignored; importing any subset still produces a
    /// valid world because every placement call can fall back to the CC0 kits.</summary>
    public static class PolyPackArt
    {
        public enum Source { RpgPoly, SimpleNature, GraveyardNature, Dungeon }

        public enum Kind
        {
            Tree, Pine, Bush, Rock, Cliff, Grass, Flower, Mushroom, Log,
            House, Ruin, Grave, Fence, Prop, Tent, Path
        }

        private sealed class PackSpec
        {
            public Source Source;
            public string[] ExactRoots;
            public string[] Hints;
        }

        private static readonly PackSpec[] Specs =
        {
            new PackSpec
            {
                Source = Source.RpgPoly,
                ExactRoots = new[] { "Assets/RPGPP_LT" },
                Hints = new[] { "RPG Poly Pack", "RPGPolyPack", "RPGPP", "PolyPack" }
            },
            new PackSpec
            {
                Source = Source.SimpleNature,
                ExactRoots = new[] { "Assets/SimpleNaturePack" },
                Hints = new[] { "SimpleNaturePack", "Low-Poly Simple Nature", "Simple Nature Pack" }
            },
            new PackSpec
            {
                Source = Source.GraveyardNature,
                ExactRoots = new[] { "Assets/NatureManufacture Assets/PBR Graveyard" },
                Hints = new[] { "PBR Graveyard", "Graveyard and Nature Set" }
            },
            new PackSpec
            {
                Source = Source.Dungeon,
                ExactRoots = new[] { "Assets/LowPolyDungeonsLite" },
                Hints = new[] { "LowPolyDungeonsLite", "Low Poly Dungeons Lite" }
            }
        };

        private static readonly string[] Skip =
            { "sky", "cloud", "terrain_grass", "terrain_sand", "preset", "_lod", "collider" };

        // First match wins. Specific natural features precede generic props/buildings.
        private static readonly (Kind kind, string[] words)[] Rules =
        {
            (Kind.Path,     new[] { "ground", "mud", "path", "road", "cobble" }),
            (Kind.Pine,     new[] { "pine", "fir", "spruce", "conifer" }),
            (Kind.Tree,     new[] { "tree", "oak", "birch", "willow", "maple", "poplar" }),
            (Kind.Bush,     new[] { "bush", "shrub", "hedge", "fern", "ivy", "rose" }),
            (Kind.Flower,   new[] { "flower", "clover", "lily", "tulip" }),
            (Kind.Grass,    new[] { "grass", "plant", "reed", "wheat" }),
            (Kind.Mushroom, new[] { "mushroom", "shroom", "fungus" }),
            (Kind.Cliff,    new[] { "cliff", "mountain", "hill", "boulder", "formation" }),
            (Kind.Grave,    new[] { "grave", "coffin", "sarcophagus", "tomb", "stone_cross",
                                    "death_statue", "catafalque" }),
            (Kind.Rock,     new[] { "rock", "stone", "pebble" }),
            (Kind.Log,      new[] { "log", "stump", "branch", "trunk", "root", "wood_02" }),
            (Kind.Ruin,     new[] { "wall", "ruin", "wreck", "broken", "pillar", "column", "grave" }),
            (Kind.Tent,     new[] { "tent", "canvas", "awning" }),
            (Kind.Prop,     new[] { "barrel", "crate", "box", "basket", "sack", "bucket",
                                    "jug", "bottle", "pot", "book", "vase", "bowl", "bench",
                                    "chair", "table", "ladder", "broom", "rake", "wagon", "cart",
                                    "well", "banner", "sign", "trough", "bathtub", "hanger",
                                    "package", "shield", "bird", "lamp", "lantern", "light",
                                    "candle", "torch", "anvil", "chest", "stones", "walldecor" }),
            (Kind.Fence,    new[] { "fence", "railing", "picket" }),
            (Kind.House,    new[] { "building", "house", "hut", "cottage", "shed", "barn",
                                    "mill", "tower", "church", "cabin", "tavern", "shop" }),
        };

        private static Dictionary<Source, string> _roots;
        private static Dictionary<Source, Dictionary<Kind, List<GameObject>>> _buckets;

        public static bool Available { get { Scan(); return _roots.Count > 0; } }
        public static string RootPath { get { Scan(); return string.Join(";", _roots.Values); } }

        public static bool Has(Source source) { Scan(); return _roots.ContainsKey(source); }

        public static int Count(Kind kind)
        {
            Scan();
            return _buckets.Values.Sum(b => b[kind].Count);
        }

        public static int Count(Source source, Kind kind)
        {
            Scan();
            return _buckets.TryGetValue(source, out var b) ? b[kind].Count : 0;
        }

        public static void Invalidate() { _roots = null; _buckets = null; }

        private static void Scan()
        {
            if (_buckets != null) return;
            _roots = new Dictionary<Source, string>();
            _buckets = new Dictionary<Source, Dictionary<Kind, List<GameObject>>>();

            foreach (var spec in Specs)
            {
                string root = FindRoot(spec);
                if (root == null) continue;
                _roots[spec.Source] = root;
                var buckets = new Dictionary<Kind, List<GameObject>>();
                foreach (Kind kind in System.Enum.GetValues(typeof(Kind)))
                    buckets[kind] = new List<GameObject>();
                _buckets[spec.Source] = buckets;

                var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { root })
                    .Select(AssetDatabase.GUIDToAssetPath).ToList();
                var prefabNames = new HashSet<string>(
                    prefabs.Select(System.IO.Path.GetFileNameWithoutExtension));
                // NatureManufacture's FBX files expose many internal submeshes (church
                // corners, raw portal pieces, cross meshes) that are not standalone props.
                // Use its authored prefabs so pivots, material assignments, and hierarchy
                // remain intact. The older low-poly packs still need model discovery because
                // several of them ship useful FBX objects without prefab wrappers.
                var paths = spec.Source == Source.GraveyardNature
                    ? prefabs.Distinct().OrderBy(p => p).ToList()
                    : prefabs.Concat(AssetDatabase.FindAssets("t:Model", new[] { root })
                            .Select(AssetDatabase.GUIDToAssetPath)
                            .Where(p => !prefabNames.Contains(System.IO.Path.GetFileNameWithoutExtension(p))))
                        .Distinct().OrderBy(p => p).ToList();

                foreach (string path in paths)
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null || go.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                    if (Classify(System.IO.Path.GetFileNameWithoutExtension(path), out var kind))
                        buckets[kind].Add(go);
                }

                Debug.Log($"[EnvironmentArt] {spec.Source} at {root}: " + string.Join(", ",
                    buckets.Where(b => b.Value.Count > 0).Select(b => $"{b.Key} x{b.Value.Count}")));
            }
        }

        private static string FindRoot(PackSpec spec)
        {
            foreach (string exact in spec.ExactRoots)
                if (AssetDatabase.IsValidFolder(exact)) return exact;
            foreach (string dir in AssetDatabase.GetSubFolders("Assets").SelectMany(Descend).Distinct())
                foreach (string hint in spec.Hints)
                    if (dir.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return dir.Replace('\\', '/');
            return null;
        }

        private static IEnumerable<string> Descend(string folder)
        {
            yield return folder;
            foreach (string child in AssetDatabase.GetSubFolders(folder))
            {
                yield return child;
                foreach (string grand in AssetDatabase.GetSubFolders(child)) yield return grand;
            }
        }

        private static bool Classify(string fileName, out Kind kind)
        {
            kind = Kind.Prop;
            string n = fileName.ToLowerInvariant();
            foreach (string s in Skip) if (n.Contains(s)) return false;
            foreach (var rule in Rules)
                foreach (string word in rule.words)
                    if (n.Contains(word)) { kind = rule.kind; return true; }
            return false;
        }

        public static void SetupMaterials()
        {
            Scan();
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null) { Debug.LogWarning("[EnvironmentArt] URP Lit shader not found."); return; }
            int converted = 0;
            foreach (var sourceRoot in _roots)
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { sourceRoot.Value }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (mat == null || (mat.shader != null
                    && mat.shader.name.StartsWith("Universal Render Pipeline")))
                    continue;
                // Asset Store packs often reference a proprietary shader that is outside
                // the selected prefab dependency closure. Unity then exposes the material
                // through its error shader, whose HasProperty calls all return false even
                // though the original texture GUIDs remain serialized. Read those saved
                // slots directly before switching shaders so the PBR maps are never lost.
                Texture main = SavedTexture(mat, "_MainTex");
                Color color = SavedColor(mat, "_Color", Color.white);
                Texture normal = SavedTexture(mat, "_BumpMap");
                Texture metallic = SavedTexture(mat, "_MetallicGlossMap");
                if (metallic == null)
                    metallic = SavedTexture(mat, "_AmbientOcclusionGSmoothnessA");
                Texture occlusion = SavedTexture(mat, "_OcclusionMap");
                if (occlusion == null)
                    occlusion = SavedTexture(mat, "_AmbientOcclusionGSmoothnessA");
                float sourceSmoothness = SavedFloat(mat, "_Glossiness", 0.2f);
                mat.shader = urp;
                if (main != null) mat.SetTexture("_BaseMap", main);
                mat.SetColor("_BaseColor", color);
                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }
                if (metallic != null)
                {
                    mat.SetTexture("_MetallicGlossMap", metallic);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                if (occlusion != null) mat.SetTexture("_OcclusionMap", occlusion);
                // Preserve each pack's authored response instead of flattening three
                // sources to chalk. The clamp keeps legacy glossy values plausible.
                mat.SetFloat("_Smoothness", Mathf.Clamp(sourceSmoothness, 0.08f, 0.65f));

                string lower = assetPath.ToLowerInvariant();
                bool cutout = sourceRoot.Key == Source.GraveyardNature
                              && (lower.Contains("leaf") || lower.Contains("grass")
                                  || lower.Contains("ivy") || lower.Contains("fern")
                                  || lower.Contains("tree") || lower.Contains("rose"));
                if (cutout)
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.SetFloat("_Cutoff", 0.35f);
                    mat.SetFloat("_Cull", 0f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }
                EditorUtility.SetDirty(mat);
                converted++;
            }
            if (converted > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[EnvironmentArt] Converted {converted} materials to URP.");
            }
        }

        private static Texture SavedTexture(Material mat, string property)
        {
            var saved = new SerializedObject(mat)
                .FindProperty("m_SavedProperties.m_TexEnvs");
            if (saved == null) return null;
            for (int i = 0; i < saved.arraySize; i++)
            {
                var entry = saved.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue != property) continue;
                return entry.FindPropertyRelative("second")
                    .FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
            }
            return null;
        }

        private static Color SavedColor(Material mat, string property, Color fallback)
        {
            var saved = new SerializedObject(mat)
                .FindProperty("m_SavedProperties.m_Colors");
            if (saved == null) return fallback;
            for (int i = 0; i < saved.arraySize; i++)
            {
                var entry = saved.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == property)
                    return entry.FindPropertyRelative("second").colorValue;
            }
            return fallback;
        }

        private static float SavedFloat(Material mat, string property, float fallback)
        {
            var saved = new SerializedObject(mat)
                .FindProperty("m_SavedProperties.m_Floats");
            if (saved == null) return fallback;
            for (int i = 0; i < saved.arraySize; i++)
            {
                var entry = saved.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == property)
                    return entry.FindPropertyRelative("second").floatValue;
            }
            return fallback;
        }

        private static List<(Source source, GameObject prefab)> Choices(Kind kind)
        {
            Scan();
            var choices = new List<(Source, GameObject)>();
            foreach (var source in Specs.Select(s => s.Source))
                if (_buckets.TryGetValue(source, out var b))
                    choices.AddRange(b[kind].Select(p => (source, p)));
            return choices;
        }

        public static GameObject Pick(Kind kind, int index)
        {
            var choices = Choices(kind);
            return choices.Count == 0 ? null : choices[Mathf.Abs(index) % choices.Count].prefab;
        }

        public static GameObject Pick(Source source, Kind kind, int index)
        {
            Scan();
            if (!_buckets.TryGetValue(source, out var b) || b[kind].Count == 0) return null;
            return b[kind][Mathf.Abs(index) % b[kind].Count];
        }

        public static int IndexOf(Kind kind, string nameLike, int fallback = 0)
        {
            var choices = Choices(kind);
            for (int i = 0; i < choices.Count; i++)
                if (choices[i].prefab.name.IndexOf(nameLike, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            return fallback;
        }

        public static int IndexOf(Source source, Kind kind, string nameLike, int fallback = 0)
        {
            Scan();
            if (!_buckets.TryGetValue(source, out var buckets)) return fallback;
            var list = buckets[kind];
            for (int i = 0; i < list.Count; i++)
                if (list[i].name.IndexOf(nameLike, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            return fallback;
        }

        public static Vector3 NaturalSize(Kind kind, int index)
        {
            var prefab = Pick(kind, index);
            if (prefab == null) return Vector3.one;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var size = Bounds(go).size;
            Object.DestroyImmediate(go);
            return size;
        }

        public static GameObject Place(Kind kind, int index, Vector3 pos, float yRot = 0f,
            float targetSize = 0f, bool byHeight = true)
        {
            var choices = Choices(kind);
            if (choices.Count == 0) return null;
            var choice = choices[Mathf.Abs(index) % choices.Count];
            return PlacePrefab(choice.source, kind, choice.prefab, pos, yRot, targetSize, byHeight);
        }

        public static GameObject Place(Source source, Kind kind, int index, Vector3 pos,
            float yRot = 0f, float targetSize = 0f, bool byHeight = true)
        {
            var prefab = Pick(source, kind, index);
            return prefab == null ? null : PlacePrefab(source, kind, prefab, pos, yRot, targetSize, byHeight);
        }

        private static GameObject PlacePrefab(Source source, Kind kind, GameObject prefab,
            Vector3 pos, float yRot, float targetSize, bool byHeight)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) return null;
            go.name = $"Env_{source}_{kind}_{prefab.name}";
            go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
            if (targetSize > 0f)
            {
                var b = Bounds(go);
                float current = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
                if (current > 0.0001f) go.transform.localScale = Vector3.one * (targetSize / current);
            }
            var bounds = Bounds(go);
            go.transform.position += pos - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var tag = go.AddComponent<EnvironmentArtTag>();
            tag.SourcePack = source.ToString();
            tag.Role = kind.ToString();
            return go;
        }

        private static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
