using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Integration for the Asset Store "RPG Poly Pack - Lite" (and any similar
    /// low-poly environment pack dropped under Assets/Art/AssetStore).
    ///
    /// It is DISCOVERY-BASED on purpose: nothing here hard-codes a prefab name, because
    /// the pack's exact contents can't be read until it is imported (Asset Store packs
    /// need an editor sign-in and can't be fetched headlessly). Instead it finds the pack
    /// wherever it landed, sorts every prefab into buckets by the words in its name
    /// (tree / rock / house / …), and the bootstrap dresses the world out of those
    /// buckets. Import the pack anywhere under Assets and the world re-dresses itself;
    /// with no pack present, Available is false and the bootstrap falls back to the CC0
    /// Kenney kits, so the build is never broken by a missing purchase.</summary>
    public static class PolyPackArt
    {
        /// <summary>Where the pack is looked for. Any folder whose name contains one of
        /// these is treated as the pack root, wherever it sits under Assets/.</summary>
        private static readonly string[] RootHints =
            { "RPG Poly Pack", "RPGPolyPack", "PolyPack", "AssetStore/PolyPack" };

        public enum Kind
        {
            Tree, Pine, Bush, Rock, Cliff, Grass, Flower, Mushroom, Log,
            House, Ruin, Fence, Prop, Tent
        }

        /// <summary>Name fragments that sort a prefab into a bucket. First match wins, so
        /// the order matters: "pine tree" must hit Pine before Tree.</summary>
        private static readonly (Kind kind, string[] words)[] Rules =
        {
            (Kind.Pine,     new[] { "pine", "fir", "spruce", "conifer" }),
            (Kind.Tree,     new[] { "tree", "oak", "birch", "willow", "maple", "poplar" }),
            (Kind.Bush,     new[] { "bush", "shrub", "hedge", "fern" }),
            (Kind.Cliff,    new[] { "cliff", "boulder", "mountain", "formation" }),
            (Kind.Rock,     new[] { "rock", "stone", "pebble" }),
            (Kind.Grass,    new[] { "grass", "reed", "wheat", "plant" }),
            (Kind.Flower,   new[] { "flower", "clover", "lily", "tulip" }),
            (Kind.Mushroom, new[] { "mushroom", "shroom", "fungus" }),
            (Kind.Log,      new[] { "log", "stump", "branch", "trunk", "wood" }),
            (Kind.Ruin,     new[] { "ruin", "wreck", "broken", "pillar", "column", "grave" }),
            (Kind.House,    new[] { "house", "hut", "cottage", "building", "barn", "mill",
                                    "tower", "church", "shed", "cabin", "home", "shop",
                                    "tavern", "roof", "wall_" }),
            (Kind.Fence,    new[] { "fence", "gate", "railing", "post" }),
            (Kind.Tent,     new[] { "tent", "camp", "canvas" }),
            (Kind.Prop,     new[] { "barrel", "crate", "box", "cart", "wagon", "well",
                                    "bench", "sign", "lamp", "lantern", "torch", "bucket",
                                    "anvil", "chest", "sack", "ladder", "bridge" }),
        };

        private static string _root;
        private static Dictionary<Kind, List<GameObject>> _buckets;

        /// <summary>The pack is present and has usable prefabs.</summary>
        public static bool Available
        {
            get
            {
                Scan();
                return _root != null && _buckets.Values.Any(v => v.Count > 0);
            }
        }

        public static string RootPath { get { Scan(); return _root; } }

        public static int Count(Kind kind)
        {
            Scan();
            return _root == null ? 0 : _buckets[kind].Count;
        }

        /// <summary>Re-scan on the next call (after an import).</summary>
        public static void Invalidate() { _root = null; _buckets = null; }

        private static void Scan()
        {
            if (_buckets != null) return;
            _buckets = new Dictionary<Kind, List<GameObject>>();
            foreach (Kind k in System.Enum.GetValues(typeof(Kind)))
                _buckets[k] = new List<GameObject>();

            _root = FindRoot();
            if (_root == null) return;

            // Prefabs first — they carry the pack's materials and LODs. A pack that ships
            // only FBX still works: models are picked up the same way.
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { _root })
                .Concat(AssetDatabase.FindAssets("t:Model", new[] { _root }))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p)                      // deterministic: same world every run
                .ToList();

            foreach (string path in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (go.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                if (Classify(System.IO.Path.GetFileNameWithoutExtension(path), out var kind))
                    _buckets[kind].Add(go);
            }

            Debug.Log($"[PolyPack] {_root}: " + string.Join(", ",
                _buckets.Where(b => b.Value.Count > 0)
                        .Select(b => $"{b.Key} x{b.Value.Count}")));
        }

        private static string FindRoot()
        {
            foreach (string dir in AssetDatabase.GetSubFolders("Assets")
                .SelectMany(Descend).Distinct())
            {
                string name = dir.Replace('\\', '/');
                foreach (string hint in RootHints)
                    if (name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return name;
            }
            return null;
        }

        /// <summary>Every folder under root, two levels deep — enough to find the pack
        /// whether it imported to Assets/RPG Poly Pack or Assets/Art/AssetStore/….</summary>
        private static IEnumerable<string> Descend(string folder)
        {
            yield return folder;
            foreach (string child in AssetDatabase.GetSubFolders(folder))
            {
                yield return child;
                foreach (string grand in AssetDatabase.GetSubFolders(child))
                    yield return grand;
            }
        }

        private static bool Classify(string fileName, out Kind kind)
        {
            string n = fileName.ToLowerInvariant();
            foreach (var (k, words) in Rules)
                foreach (string w in words)
                    if (n.Contains(w)) { kind = k; return true; }
            kind = Kind.Prop;
            return false;   // unrecognised: don't scatter mystery meshes around the map
        }

        // ---------- URP conversion ----------

        /// <summary>Asset Store packs ship built-in-pipeline (Standard) materials, which
        /// render MAGENTA under URP. Convert them in place: same textures and colours, URP
        /// Lit shader. Runs once per bootstrap; already-URP materials are left alone.</summary>
        public static void SetupMaterials()
        {
            Scan();
            if (_root == null) return;

            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null) { Debug.LogWarning("[PolyPack] URP Lit shader not found."); return; }

            int converted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { _root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name.StartsWith("Universal Render Pipeline")) continue;

                // Carry the look across before swapping the shader (the properties are
                // named differently, and the values are lost once the shader changes).
                Texture main = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

                mat.shader = urp;
                if (main != null) mat.SetTexture("_BaseMap", main);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", 0.08f);   // matte, like the rest of the world
                EditorUtility.SetDirty(mat);
                converted++;
            }

            if (converted > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[PolyPack] Converted {converted} materials to URP.");
            }
        }

        // ---------- placement ----------

        /// <summary>Deterministic pick from a bucket (index wraps), so the same bootstrap
        /// seed always builds the same world.</summary>
        public static GameObject Pick(Kind kind, int index)
        {
            Scan();
            var list = _root == null ? null : _buckets[kind];
            if (list == null || list.Count == 0) return null;
            return list[Mathf.Abs(index) % list.Count];
        }

        /// <summary>Places one pack prefab, scaled so its bounds match targetSize metres
        /// (height or footprint) and seated on the ground at pos — same contract as
        /// KenneyArt.Place, so bootstrap layout code reads identically for either pack.
        /// Returns null when the bucket is empty, and every caller must cope with that.</summary>
        public static GameObject Place(Kind kind, int index, Vector3 pos, float yRot = 0f,
            float targetSize = 0f, bool byHeight = true)
        {
            var prefab = Pick(kind, index);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) return null;
            go.name = $"Poly_{kind}_{prefab.name}";
            go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

            if (targetSize > 0f)
            {
                var b = Bounds(go);
                float current = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
                if (current > 0.0001f)
                    go.transform.localScale = Vector3.one * (targetSize / current);
            }

            // Seat the mesh base on the requested point (pack pivots are not consistent).
            var bounds = Bounds(go);
            var baseCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            go.transform.position += pos - baseCenter;
            return go;
        }

        private static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
