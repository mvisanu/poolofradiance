using System.Collections.Generic;
using System.IO;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using RadiantPool.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RadiantPool.EditorTools
{
    /// <summary>One-shot project bootstrap: URP setup, player prefab, gray-box zone scene,
    /// network manager wiring, player settings, build list. Idempotent — safe to re-run.
    /// Invoked headless via: Unity.exe -batchmode -quit -executeMethod
    /// RadiantPool.EditorTools.ProjectBootstrap.Run</summary>
    public static class ProjectBootstrap
    {
        private const string SettingsDir = "Assets/Settings";
        private const string PrefabDir = "Assets/Prefabs";
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = SceneDir + "/Zone_OldDocks_Graybox.unity";

        [MenuItem("RadiantPool/Bootstrap Project")]
        public static void Run()
        {
            EnsureFolders();
            SetupPlayerSettings();
            SetupUrp();
            PolyPackArt.Invalidate();      // re-scan: the pack may have just been imported
            PolyPackArt.SetupMaterials();  // Asset Store packs ship Standard mats: magenta in URP
            KenneyArt.SetupMaterials();
            KayKitArt.Setup();
            QuaterniusArt.Setup();
            GeneratedArt.Setup();   // bear + rat: the CC0 packs have neither
            var playerPrefab = CreatePlayerPrefab();
            CreateGrayboxScene(playerPrefab);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Bootstrap] Complete.");
        }

        private static void EnsureFolders()
        {
            foreach (var dir in new[] { SettingsDir, PrefabDir, SceneDir })
                if (!AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(dir).Replace('\\', '/'),
                        Path.GetFileName(dir));
        }

        private static void SetupPlayerSettings()
        {
            PlayerSettings.productName = "Radiant Pool";
            PlayerSettings.companyName = "RadiantPool";
            PlayerSettings.runInBackground = true;   // multiple local instances must keep simulating
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        private static void SetupUrp()
        {
            var pipelinePath = SettingsDir + "/URP_Asset.asset";
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (existing == null)
            {
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, SettingsDir + "/URP_Renderer.asset");
                var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
                existing = pipeline;
            }
            GraphicsSettings.defaultRenderPipeline = existing;
            QualitySettings.renderPipeline = existing;
        }

        /// <summary>Procedural cobblestone-ish tile so the ground reads as a surface,
        /// not a void. Replaced by real textures at the 3f asset pass.</summary>
        /// <summary>The ground is GRASS, not pavement. It used to be a 4x4 cobblestone tile
        /// repeated across the whole 120 m map — which reads as one enormous stone floor
        /// with a forest growing out of it, and left the dirt roads with nothing to be a
        /// road THROUGH. Soft green noise instead: no grid, no mortar lines, just enough
        /// variation to stop it looking like flat paint.</summary>
        private static Texture2D GroundTexture()
        {
            string path = SettingsDir + "/T_GroundGrass.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var rand = new System.Random(7);

            // Two octaves of value noise: broad patches + a fine speckle, so the tiling
            // never resolves into a visible grid the way the stones did.
            float Noise(int x, int y, int cells)
            {
                int c = size / cells;
                int gx = x / c, gy = y / c;
                var r = new System.Random(gx * 73856093 ^ gy * 19349663 ^ cells);
                return (float)r.NextDouble();
            }

            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    float broad = Noise(x, y, 8) * 0.10f;          // patchiness
                    float fine = (float)rand.NextDouble() * 0.06f;  // blade speckle
                    float v = 0.90f + broad + fine;
                    tex.SetPixel(x, y, new Color(0.42f * v, 0.60f * v, 0.32f * v));
                }
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Mat(string name, Color color)
        {
            string path = $"{SettingsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        private static NetworkObject CreatePlayerPrefab()
        {
            string path = PrefabDir + "/Player.prefab";
            AssetDatabase.DeleteAsset(path);   // always regenerate (components evolve)

            var root = new GameObject("Player");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.center = new Vector3(0f, 1f, 0f);
            cc.radius = 0.4f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(body.GetComponent<Collider>()); // controller handles collision
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.GetComponent<Renderer>().sharedMaterial = Mat("M_Player", new Color(0.35f, 0.55f, 0.9f));

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube); // facing indicator
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.5f, 0.45f);
            nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.3f);
            nose.GetComponent<Renderer>().sharedMaterial = Mat("M_PlayerNose", new Color(0.9f, 0.8f, 0.3f));

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            var so = new SerializedObject(nt);
            var clientAuth = so.FindProperty("_clientAuthoritative");
            if (clientAuth != null) { clientAuth.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            root.AddComponent<PlayerMotor>();
            root.AddComponent<PlayerIdentity>();
            root.AddComponent<PlayerCharacterHolder>();
            root.AddComponent<CompanionFollower>();   // inert unless spawned as companion

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<NetworkObject>();
        }

        /// <summary>Vibrant open-world dressing: forest bands around the map, scatter
        /// (grass/flowers/stones), themed wilds encounter sites, and a proper orc warcamp.
        /// Deterministic (fixed seed) so every bootstrap produces the same world.
        ///
        /// Art comes from the Asset Store RPG Poly Pack when it is imported, and the CC0
        /// Kenney kits when it is not — the LAYOUT below is the same either way, so the
        /// world reads the same and only the models change. Composition rules that survive
        /// both: big silhouettes ring the map and frame the play space, density falls off
        /// toward the quarters so fights stay readable, and nothing is planted inside the
        /// hub or on a grid where combat happens.</summary>
        private static void DressWorld()
        {
            var rnd = new System.Random(1177);
            float Jit(float range) => (float)(rnd.NextDouble() * 2.0 - 1.0) * range;
            float Rot() => (float)(rnd.NextDouble() * 360.0);

            void Nature(string model, Vector3 pos, float size, bool byHeight = true) =>
                KenneyArt.Place("Nature", model, pos, Rot(), size, byHeight);

            // --- pack-or-fallback dispatchers -------------------------------------------
            // Each takes the ROLE the object plays in the composition, not a model name.

            string[] bigTrees = { "tree_default", "tree_oak", "tree_pineTallA",
                "tree_pineRoundB", "tree_detailed", "tree_fat", "tree_tall" };
            string[] darkTrees = { "tree_default_dark", "tree_thin_dark", "tree_small_dark" };
            string[] scatter = { "grass_large", "grass", "flower_redA", "flower_yellowA",
                "flower_purpleA", "plant_bush", "plant_bushLarge", "stone_smallA",
                "stone_smallD", "rock_smallB", "stump_round", "log" };

            int pick = 0;   // walks the pack buckets so a stand of trees isn't all one model

            void Tree(Vector3 pos, float size)
            {
                pick++;
                // Mix conifers and broadleaf when the pack has both — a stand of one model
                // repeated reads as wallpaper.
                var kind = (pick % 3 == 0 && PolyPackArt.Count(PolyPackArt.Kind.Pine) > 0)
                    ? PolyPackArt.Kind.Pine : PolyPackArt.Kind.Tree;
                if (PolyPackArt.Place(kind, pick, pos, Rot(), size) != null) return;
                Nature(bigTrees[rnd.Next(bigTrees.Length)], pos, size);
            }

            void DeadTree(Vector3 pos, float size)
            {
                pick++;
                if (PolyPackArt.Place(PolyPackArt.Kind.Pine, pick, pos, Rot(), size) != null) return;
                Nature(darkTrees[rnd.Next(darkTrees.Length)], pos, size);
            }

            void Rock(Vector3 pos, float size, bool big = false)
            {
                pick++;
                var kind = big ? PolyPackArt.Kind.Cliff : PolyPackArt.Kind.Rock;
                if (PolyPackArt.Place(kind, pick, pos, Rot(), size, byHeight: false) != null) return;
                Nature(big ? "cliff_block_rock" : "rock_largeA", pos, size, false);
            }

            void Ground(Vector3 pos, float size)
            {
                pick++;
                // Grass / flowers / bushes / mushrooms — whatever the pack actually has.
                var kinds = new[] { PolyPackArt.Kind.Grass, PolyPackArt.Kind.Flower,
                    PolyPackArt.Kind.Bush, PolyPackArt.Kind.Rock, PolyPackArt.Kind.Mushroom,
                    PolyPackArt.Kind.Log };
                for (int i = 0; i < kinds.Length; i++)
                {
                    var kind = kinds[(pick + i) % kinds.Length];
                    if (PolyPackArt.Place(kind, pick, pos, Rot(), size) != null) return;
                }
                Nature(scatter[rnd.Next(scatter.Length)], pos, size);
            }

            // Forest bands along the map edges (leave zone interiors playable).
            foreach (var (cx, cz, count) in new (float, float, int)[]
            {
                (-52, 30, 8), (-52, -12, 7), (-30, 52, 7), (8, 55, 6),
                (55, 40, 6), (58, -38, 7), (28, -55, 7), (-14, -52, 7), (-55, -44, 6)
            })
                for (int i = 0; i < count; i++)
                    Tree(new Vector3(cx + Jit(7f), 0, cz + Jit(7f)),
                        4.2f + (float)rnd.NextDouble() * 2.6f);

            // Mid-map accent trees along the roads between quarters.
            foreach (var (x, z) in new (float, float)[]
            {
                (-16, 14), (-20, -6), (14, 16), (22, 10), (-8, 26),
                (18, -22), (-26, -18), (34, -6), (-34, 24), (12, 34)
            })
                Tree(new Vector3(x + Jit(2f), 0, z + Jit(2f)),
                    3.6f + (float)rnd.NextDouble() * 1.8f);

            // Ground scatter: grass, flowers, small stones, bushes, stumps.
            for (int i = 0; i < 90; i++)
            {
                var pos = new Vector3(Jit(56f), 0, Jit(56f));
                if (Mathf.Abs(pos.x) < 14f && Mathf.Abs(pos.z) < 18f) continue;   // hub
                Ground(pos, 0.5f + (float)rnd.NextDouble() * 0.9f);
            }

            // Wilds sites. Spider hollows: dead trees + moss. Bear dens: cliff + logs.
            foreach (var (x, z) in new (float, float)[] { (-12, 24), (30, 26) })
            {
                DeadTree(new Vector3(x - 3 + Jit(1f), 0, z + Jit(1f)), 4.5f);
                DeadTree(new Vector3(x + 3 + Jit(1f), 0, z - 2 + Jit(1f)), 4f);
                DeadTree(new Vector3(x + Jit(2f), 0, z + 3 + Jit(1f)), 3f);
                Ground(new Vector3(x + Jit(3f), 0, z + Jit(3f)), 0.7f);
            }
            foreach (var (x, z) in new (float, float)[] { (-24, -28), (26, -12) })
            {
                Rock(new Vector3(x - 2, 0, z - 2), 3.5f, big: true);
                Rock(new Vector3(x + 3 + Jit(1f), 0, z + Jit(1f)), 2.2f);
                Ground(new Vector3(x + Jit(3f), 0, z + 3 + Jit(1f)), 1.2f);
                Ground(new Vector3(x + Jit(3f), 0, z + Jit(3f)), 0.6f);
            }

            // Goblin ambush spots: small raider tents + a stone campfire.
            foreach (var (x, z) in new (float, float)[] { (-16, 8), (34, 4) })
            {
                pick++;
                if (PolyPackArt.Place(PolyPackArt.Kind.Tent, pick,
                        new Vector3(x - 2, 0, z + 2), Rot(), 2.4f) == null)
                    Nature("tent_smallOpen", new Vector3(x - 2, 0, z + 2), 1.6f);
                Nature("campfire_stones", new Vector3(x + 1, 0, z), 0.9f, false);
                Ground(new Vector3(x + 2.5f + Jit(0.5f), 0, z + 1.5f), 1.2f);
            }

            // --- Roads. Wayfinding is the whole point: the quest arrow says "that way",
            // and a road on the ground going the same way says it again, continuously, in
            // the world itself. One out of the hub to each quarter.
            //
            // Drawn as flat strips, NOT out of the pack's path pieces. Those are terrain
            // chunks sized for the demo scene's own layout: laid end to end they came out
            // as giant disconnected sand slabs floating on the grass. A road has to be
            // continuous or it isn't a road, and a strip I generate is continuous by
            // construction — at any length, on any bearing.
            var roadMat = Mat("M_Road", new Color(0.62f, 0.51f, 0.35f));   // packed dirt

            void Road(Vector3 from, Vector3 to, float width = 3.2f)
            {
                float len = Vector3.Distance(from, to);
                if (len < 0.5f) return;
                var mid = (from + to) * 0.5f;
                var dir = (to - from).normalized;

                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Road";
                Object.DestroyImmediate(strip.GetComponent<Collider>());   // walk over it
                // Sits just proud of the ground plane: exactly co-planar, the two surfaces
                // z-fight and the road flickers as the camera moves.
                strip.transform.position = new Vector3(mid.x, 0.02f, mid.z);
                strip.transform.rotation = Quaternion.Euler(
                    0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
                strip.transform.localScale = new Vector3(width, 0.04f, len);
                strip.GetComponent<Renderer>().sharedMaterial = roadMat;
            }

            var hubCentre = new Vector3(0, 0, -8);
            Road(hubCentre, new Vector3(-30, 0, -2));                 // west, the Old Docks
            Road(new Vector3(0, 0, 2), new Vector3(0, 0, 24));        // north, the Market gate
            Road(new Vector3(3, 0, -12), new Vector3(12, 0, -30));    // south, the warcamp
            Road(new Vector3(8, 0, -6), new Vector3(34, 0, 0));       // east, the Temple

            // Horizon: hills and a mountain OUTSIDE the perimeter walls. The map is a
            // 120 m box with a hard edge; a ring of big silhouettes past the wall gives it
            // a beyond, and gives the eye something to measure the world against.
            foreach (var (x, z, size) in new (float, float, float)[]
            {
                (-78, 40, 26f), (-72, -30, 22f), (0, 84, 30f), (70, 60, 24f),
                (86, -10, 34f), (30, -84, 26f), (-40, -80, 22f), (78, 30, 20f),
            })
                PolyPackArt.Place(PolyPackArt.Kind.Cliff, pick++,
                    new Vector3(x, -1f, z), (x * 53f) % 360f, size, byHeight: false);

            // Lived-in detail: the pack's well, wagon, benches, crates and sacks, placed
            // where people would actually put them — around the hub, along the docks, in
            // the market. Kept clear of the plaza centre, the shrine and the council
            // platform so nothing blocks the paths the player walks every session.
            // Sized by HEIGHT, at human scale. Sizing props by footprint (byHeight: false)
            // is what turned a vase into a 3 m urn: a narrow object gets scaled up until
            // its BASE is that wide. A barrel is knee-high; a well is chest-high.
            if (PolyPackArt.Count(PolyPackArt.Kind.Prop) > 0)
                foreach (var (x, z, rot, size) in new (float, float, float, float)[]
                {
                    (7, -12, 20f, 1.1f), (-12, -3, 0f, 1.0f), (10, 3, 200f, 0.9f),
                    (-7, 5, 90f, 0.8f), (6, 8, 130f, 0.9f),          // hub
                    (-30, -2, 40f, 0.9f), (-34, 6, 310f, 1.0f),      // docks quayside
                    (-27, 12, 15f, 0.8f), (-42, -14, 75f, 0.9f),
                    (-5, 42, 0f, 1.0f), (5, 38, 180f, 0.9f),         // market
                    (14, 44, 250f, 0.8f), (-16, 50, 60f, 1.0f),
                })
                    PolyPackArt.Place(PolyPackArt.Kind.Prop, pick++,
                        new Vector3(x, 0f, z), rot, size, byHeight: true);

            // Fences: a paddock behind the hub farm, and rails along the market road.
            if (PolyPackArt.Count(PolyPackArt.Kind.Fence) > 0)
                foreach (var (x, z, rot) in new (float, float, float)[]
                {
                    (-16, -4, 0f), (-16, -7, 0f), (-11, -9, 90f), (-15, -9, 90f),
                    (-9, 20, 90f), (-9, 24, 90f), (9, 20, 90f), (9, 24, 90f),
                })
                    PolyPackArt.Place(PolyPackArt.Kind.Fence, pick++,
                        new Vector3(x, 0f, z), rot, 3f, byHeight: false);

            // The Sunken Warcamp (south): fortified orc camp from the Survival kit.
            void Camp(string model, Vector3 pos, float size, float rot = -1f) =>
                KenneyArt.Place("Survival", model, pos, rot < 0 ? Rot() : rot, size, false);

            Camp("tent-canvas", new Vector3(10, 0, -36), 3.2f);
            Camp("tent", new Vector3(14, 0, -34), 3f);
            Camp("tent-canvas-half", new Vector3(24, 0, -40), 3f);
            Camp("structure-canvas", new Vector3(14, 0, -48), 4f, 180f);   // war-tent
            Camp("campfire-pit", new Vector3(12, 0, -40), 1.2f);
            Camp("campfire-stand", new Vector3(21, 0, -37), 1.3f);
            Camp("box-large", new Vector3(23, 0, -36.5f), 1.1f);
            Camp("barrel", new Vector3(24.2f, 0, -37.5f), 0.9f);
            Camp("chest", new Vector3(15.5f, 0, -47), 1f, 20f);
            Camp("resource-wood", new Vector3(9, 0, -33), 1.4f);
            foreach (var (x, z, r) in new (float, float, float)[]
            {
                (7, -30, 0), (10, -29.5f, 0), (13, -29.5f, 0), (16, -30, 0),
                (6, -33, 90), (6, -36, 90), (25.5f, -33, 90), (26, -36, 90)
            })
                Camp("fence-fortified", new Vector3(x, 0, z), 2.4f, r);

            // A little farm color by the hub (crops + fence, pure charm).
            Nature("crops_wheatStageB", new Vector3(-13, 0, -6), 1f);
            Nature("crops_wheatStageB", new Vector3(-14.2f, 0, -6), 1f);
            Nature("crops_cornStageC", new Vector3(-13, 0, -7.4f), 1.2f);
            Nature("crop_pumpkin", new Vector3(-14.2f, 0, -7.4f), 0.7f);
            Nature("fence_simple", new Vector3(-13.6f, 0, -5f), 1.6f);
        }

        private static void CreateGrayboxScene(NetworkObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting: bright sunny day — saturated, cheerful open-world palette.
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.55f;
            light.color = new Color(1f, 0.97f, 0.88f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.83f, 0.98f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.65f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.37f, 0.32f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.73f, 0.81f, 0.92f);
            RenderSettings.fogDensity = 0.006f;

            // Camera with URP post-processing (bloom + vignette + slight warmth).
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<OrbitCamera>();
            camGo.transform.position = new Vector3(0f, 6f, -10f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            cam.allowHDR = true;

            var profilePath = SettingsDir + "/PostFX.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
                var bloom = profile.Add<Bloom>();
                bloom.intensity.Override(0.65f);
                bloom.threshold.Override(1.05f);
                var vignette = profile.Add<Vignette>();
                vignette.intensity.Override(0.22f);
                var colors = profile.Add<ColorAdjustments>();
                colors.postExposure.Override(0.2f);
                colors.saturation.Override(16f);   // hand-painted warmth, Albion-leaning
                AssetDatabase.SaveAssets();
            }
            var volumeGo = new GameObject("Global PostFX");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = profile;

            // Grid overlay material lives in Resources so its shader ships in builds
            // (Shader.Find at runtime returns null for stripped shaders — kick bug).
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Fx"))
                AssetDatabase.CreateFolder("Assets/Resources", "Fx");
            if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Fx/M_GridOverlay.mat") == null)
            {
                var grid = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                grid.SetFloat("_Surface", 1f);   // transparent
                grid.SetFloat("_Blend", 0f);     // alpha blend
                grid.SetOverrideTag("RenderType", "Transparent");
                grid.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                grid.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                grid.SetInt("_ZWrite", 0);
                grid.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                grid.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                grid.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.14f));
                AssetDatabase.CreateAsset(grid, "Assets/Resources/Fx/M_GridOverlay.mat");
            }

            // Opaque twin of the above, for everything built at runtime (RuntimeArt.Lit):
            // primitives are born with the built-in Standard "Default-Material", which URP
            // renders MAGENTA in a build. Same reason this lives in Resources: an
            // unreferenced shader is stripped and Shader.Find comes back null.
            if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Fx/M_Solid.mat") == null)
            {
                var solid = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                solid.SetColor("_BaseColor", Color.white);
                solid.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(solid, "Assets/Resources/Fx/M_Solid.mat");
            }

            // Gray-box geometry: 120x120 map, hub south-center, docks west,
            // Drowned Market north (gated), Glasslit Temple east (gated).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(12f, 1f, 12f); // 120x120 m
            var groundMat = Mat("M_Ground", new Color(0.52f, 0.66f, 0.42f));   // sunny grass
            groundMat.mainTexture = GroundTexture();
            groundMat.mainTextureScale = new Vector2(48f, 48f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            void Box(string name, Vector3 pos, Vector3 scale, Material mat)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = name;
                b.transform.position = pos;
                b.transform.localScale = scale;
                b.GetComponent<Renderer>().sharedMaterial = mat;
            }

            var wall = Mat("M_Wall", new Color(0.5f, 0.47f, 0.44f));
            var crate = Mat("M_Crate", new Color(0.55f, 0.42f, 0.28f));
            var water = Mat("M_Water", new Color(0.2f, 0.35f, 0.5f));
            var gateMat = Mat("M_Gate", new Color(0.3f, 0.28f, 0.34f));

            // Perimeter walls.
            Box("Wall_N", new Vector3(0, 1.5f, 60), new Vector3(120, 3, 1), wall);
            Box("Wall_S", new Vector3(0, 1.5f, -60), new Vector3(120, 3, 1), wall);
            Box("Wall_E", new Vector3(60, 1.5f, 0), new Vector3(1, 3, 120), wall);
            Box("Wall_W", new Vector3(-60, 1.5f, 0), new Vector3(1, 3, 120), wall);
            // Waterfront along the west edge (docks fiction, visual only).
            Box("Water", new Vector3(-56, 0.05f, 0), new Vector3(7, 0.1f, 118), water);

            // Market divider (z=25) with a 6 m gate gap at x=0.
            Box("MarketWall_W", new Vector3(-31.5f, 1.5f, 25), new Vector3(57, 3, 1), wall);
            Box("MarketWall_E", new Vector3(31.5f, 1.5f, 25), new Vector3(57, 3, 1), wall);
            var gateMarket = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateMarket.name = "Gate_DrownedMarket";
            gateMarket.transform.position = new Vector3(0, 1.5f, 25);
            gateMarket.transform.localScale = new Vector3(6, 3, 1.2f);
            gateMarket.GetComponent<Renderer>().enabled = false;   // collider only
            gateMarket.AddComponent<ZoneGate>().ZoneIndex = 1;
            var gateMarketVisual = KenneyArt.Place("Pirate", "castle-gate",
                new Vector3(0, 0, 25), 0, 7f);
            if (gateMarketVisual != null)
                gateMarketVisual.transform.SetParent(gateMarket.transform, true);

            // Temple divider (x=35, south of the market wall) with a 6 m gate gap at z=0.
            Box("TempleWall_S", new Vector3(35, 1.5f, -31.5f), new Vector3(1, 3, 57), wall);
            Box("TempleWall_N", new Vector3(35, 1.5f, 14), new Vector3(1, 3, 22), wall);
            var gateTemple = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateTemple.name = "Gate_GlasslitTemple";
            gateTemple.transform.position = new Vector3(35, 1.5f, 0);
            gateTemple.transform.localScale = new Vector3(1.2f, 3, 6);
            gateTemple.GetComponent<Renderer>().enabled = false;   // collider only
            gateTemple.AddComponent<ZoneGate>().ZoneIndex = 2;
            var gateTempleVisual = KenneyArt.Place("Pirate", "castle-gate",
                new Vector3(35, 0, 0), 90f, 7f);
            if (gateTempleVisual != null)
                gateTempleVisual.transform.SetParent(gateTemple.transform, true);

            // Zone dressing — Albion-style district color zoning: each quarter has its
            // own saturated palette so you always know where you are at a glance.
            var docksWood = Mat("M_DocksWood", new Color(0.45f, 0.33f, 0.22f));   // tarred timber
            var docksRoof = Mat("M_DocksRoof", new Color(0.25f, 0.42f, 0.45f));   // teal slate
            var marketWood = Mat("M_MarketWood", new Color(0.72f, 0.6f, 0.4f));   // pale timber
            var marketCloth = Mat("M_MarketCloth", new Color(0.55f, 0.68f, 0.3f)); // awning green
            var templeStone = Mat("M_TempleStone", new Color(0.78f, 0.68f, 0.5f)); // warm sandstone
            var templeEmber = Mat("M_TempleEmber", new Color(0.75f, 0.3f, 0.18f)); // cult ember

            // A building is a COLLIDER plus a look. The collider is gameplay (it blocks
            // movement, and the combat x-ray fades whatever hides a creature), so it is
            // always a box; only the look changes when the pack is present. Same trick the
            // zone gates use: box with its renderer off, real model parented inside it.
            int houseSeed = 0;

            void Roofed(string name, Vector3 pos, Vector3 scale, Material body, Material roof)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = name;
                b.transform.position = pos;
                b.transform.localScale = scale;

                var visual = PolyPackArt.Place(PolyPackArt.Kind.House, houseSeed++,
                    new Vector3(pos.x, 0f, pos.z), 0f,
                    Mathf.Max(scale.x, scale.z), byHeight: false);
                if (visual != null)
                {
                    b.GetComponent<Renderer>().enabled = false;   // collider only
                    visual.transform.SetParent(b.transform, worldPositionStays: true);
                    return;
                }

                b.GetComponent<Renderer>().sharedMaterial = body;
                Box(name + "_Roof",
                    new Vector3(pos.x, pos.y + scale.y / 2f + 0.35f, pos.z),
                    new Vector3(scale.x + 1.2f, 0.7f, scale.z + 1.2f), roof);
            }

            Roofed("Docks_Warehouse_A", new Vector3(-40, 3, 8), new Vector3(14, 6, 10), docksWood, docksRoof);
            Roofed("Docks_Warehouse_B", new Vector3(-25, 2.5f, -25), new Vector3(10, 5, 12), docksWood, docksRoof);

            // The town itself: houses ringing the hub plaza and the market road, so the
            // quarters read as a settlement instead of props on a lawn. Only when the pack
            // actually ships buildings — the Kenney kits have none, and a row of grey
            // boxes here would look worse than the open plaza does.
            if (PolyPackArt.Count(PolyPackArt.Kind.House) > 0)
                foreach (var (x, z, rot, size) in new (float, float, float, float)[]
                {
                    (-18, -4, 90f, 8f), (-18, 6, 90f, 7f),          // west side of the hub
                    (16, -6, 270f, 8f), (17, 5, 270f, 7f),          // east side
                    (-14, 30, 160f, 7f), (13, 30, 200f, 7f),        // the road up to the market
                    (-6, 56, 180f, 8f), (9, 58, 180f, 7f),          // market far side
                })
                    PolyPackArt.Place(PolyPackArt.Kind.House, houseSeed++,
                        new Vector3(x, 0f, z), rot, size, byHeight: false);

            // --- CC0 Kenney set dressing (see Assets/Art/Kenney; CREDITS in README) ---
            void Lantern(Vector3 pos)
            {
                var post = KenneyArt.Place("FantasyTown", "lantern", pos,
                    targetSize: 2.6f, byHeight: true);
                if (post == null) return;
                var glow = new GameObject("GlowLight").AddComponent<Light>();
                glow.transform.SetParent(post.transform, false);
                glow.transform.position = pos + Vector3.up * 2.3f;
                glow.type = LightType.Point;
                glow.color = new Color(1f, 0.82f, 0.5f);
                glow.intensity = 1.6f;
                glow.range = 9f;
            }

            // Shrine of the Dawnmother — where a defeated party wakes
            // (position must match CombatManager.RespawnPoint).
            Box("Shrine_Base", new Vector3(-9, 0.1f, -14), new Vector3(5, 0.2f, 5), templeStone);
            KenneyArt.Place("FantasyTown", "pillar-stone", new Vector3(-11, 0.2f, -16), 0, 3.4f, true);
            KenneyArt.Place("FantasyTown", "banner-red", new Vector3(-7, 0.2f, -16), 0, 2.6f, true);
            var shrineLabelGo = new GameObject("ShrineLabel");
            shrineLabelGo.transform.position = new Vector3(-9, 3.1f, -14.5f);
            var shrineLabel = shrineLabelGo.AddComponent<TextMesh>();
            shrineLabel.text = "Shrine of the Dawnmother";
            shrineLabel.characterSize = 0.07f;
            shrineLabel.fontSize = 40;
            shrineLabel.anchor = TextAnchor.LowerCenter;
            shrineLabel.color = new Color(1f, 0.93f, 0.7f);
            shrineLabelGo.AddComponent<Billboard>();
            var shrineLight = new GameObject("ShrineGlow").AddComponent<Light>();
            shrineLight.transform.position = new Vector3(-9, 2.2f, -14);
            shrineLight.type = LightType.Point;
            shrineLight.color = new Color(1f, 0.9f, 0.6f);
            shrineLight.intensity = 2.2f;
            shrineLight.range = 11f;

            // Hub plaza: fountain, lanterns, banners, trees.
            KenneyArt.Place("FantasyTown", "fountain-round-detail", new Vector3(0, 0, -2), 0, 5f);
            Lantern(new Vector3(-5, 0, -10));
            Lantern(new Vector3(5, 0, -10));
            Lantern(new Vector3(-5, 0, 2));
            Lantern(new Vector3(5, 0, 2));
            KenneyArt.Place("FantasyTown", "banner-red", new Vector3(-3.5f, 0.3f, -15), 0, 2.6f, true);
            KenneyArt.Place("FantasyTown", "banner-green", new Vector3(3.5f, 0.3f, -15), 0, 2.6f, true);
            KenneyArt.Place("FantasyTown", "cart-high", new Vector3(8, 0, -18), 35f, 3f);
            KenneyArt.Place("FantasyTown", "tree-high", new Vector3(-10, 0, -20), 0, 5.5f, true);
            KenneyArt.Place("FantasyTown", "tree", new Vector3(12, 0, -22), 70f, 4f, true);

            // Docks: crates, barrels, boats on the water, mooring props.
            KenneyArt.Place("Pirate", "crate", new Vector3(-32, 0, -6), 15f, 1.4f);
            KenneyArt.Place("Pirate", "crate-bottles", new Vector3(-30.5f, 0, -4.5f), 40f, 1.3f);
            KenneyArt.Place("Pirate", "barrel", new Vector3(-33.5f, 0, -4f), 0, 1.1f);
            KenneyArt.Place("Pirate", "barrel", new Vector3(-44, 0, 10), 0, 1.1f);
            KenneyArt.Place("Pirate", "chest", new Vector3(-23, 0, -30), 200f, 1.3f);
            KenneyArt.Place("Pirate", "boat-row-large", new Vector3(-54, 0.15f, -8), 100f, 6f);
            KenneyArt.Place("Pirate", "boat-row-small", new Vector3(-53, 0.15f, 16), 80f, 4f);
            KenneyArt.Place("Pirate", "flag-high", new Vector3(-36, 0, 18), 0, 4.5f, true);
            Lantern(new Vector3(-28, 0, 4));

            // Market: real stalls, fountain, carts, hedges.
            KenneyArt.Place("FantasyTown", "stall-green", new Vector3(-12, 0, 40), 160f, 4f);
            KenneyArt.Place("FantasyTown", "stall-red", new Vector3(10, 0, 48), 20f, 4f);
            KenneyArt.Place("FantasyTown", "stall-bench", new Vector3(-8, 0, 44), 90f, 3f);
            KenneyArt.Place("FantasyTown", "fountain-round", new Vector3(0, 0, 45), 0, 5f);
            KenneyArt.Place("FantasyTown", "cart", new Vector3(16, 0, 38), 120f, 2.8f);
            KenneyArt.Place("FantasyTown", "hedge-large", new Vector3(-24, 0, 46), 0, 4f);
            Lantern(new Vector3(-6, 0, 36));
            Lantern(new Vector3(8, 0, 52));

            // Temple: stone pillars, ruined walls, ember lanterns, the cult's banners.
            KenneyArt.Place("FantasyTown", "pillar-stone", new Vector3(42, 0, -6), 0, 5f, true);
            KenneyArt.Place("FantasyTown", "pillar-stone", new Vector3(42, 0, 6), 0, 5f, true);
            KenneyArt.Place("FantasyTown", "wall-broken", new Vector3(48, 0, -22), 30f, 4f);
            KenneyArt.Place("FantasyTown", "wall-broken", new Vector3(55, 0, 8), 290f, 4f);
            KenneyArt.Place("Pirate", "flag-pirate-high", new Vector3(45, 0, 15), 0, 5f, true);
            Box("Temple_Brazier_1", new Vector3(46, 1f, -18), new Vector3(1, 2, 1), templeEmber);
            Box("Temple_Brazier_2", new Vector3(54, 1f, 4), new Vector3(1, 2, 1), templeEmber);

            // Broken stone around the temple approach — the quarter should look like
            // something that FELL, not a clean park with braziers in it.
            foreach (var (x, z, size) in new (float, float, float)[]
            {
                (44, -14, 3f), (50, -3, 2.6f), (57, -9, 3.2f), (47, 8, 2.8f),
            })
                PolyPackArt.Place(PolyPackArt.Kind.Ruin, houseSeed++,
                    new Vector3(x, 0f, z), (x * 37f) % 360f, size, byHeight: false);

            // Nature + survival dressing (Kenney Nature/Survival kits, CC0): forests,
            // scatter, dressed wilds encounter sites, and the orc warcamp.
            DressWorld();
            var lightwell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lightwell.name = "The Lightwell";
            lightwell.transform.position = new Vector3(52, 0.3f, 20);
            lightwell.transform.localScale = new Vector3(6, 0.3f, 6);
            var wellMat = Mat("M_Lightwell", new Color(0.95f, 0.85f, 0.45f));
            wellMat.EnableKeyword("_EMISSION");
            wellMat.SetColor("_EmissionColor", new Color(1.6f, 1.2f, 0.45f)); // HDR glow → bloom
            lightwell.GetComponent<Renderer>().sharedMaterial = wellMat;
            var wellLight = new GameObject("Lightwell Glow").AddComponent<Light>();
            wellLight.transform.position = new Vector3(52, 2.5f, 20);
            wellLight.type = LightType.Point;
            wellLight.color = new Color(1f, 0.85f, 0.45f);
            wellLight.intensity = 3.5f;
            wellLight.range = 18f;

            // Spawn points (hub plaza) — DressWorld() lives below in this class.
            var spawnParent = new GameObject("SpawnPoints");
            var spawns = new List<Transform>();
            for (int i = 0; i < 4; i++)
            {
                var sp = new GameObject($"Spawn_{i}");
                sp.transform.SetParent(spawnParent.transform);
                sp.transform.position = new Vector3(-2f + i * 1.6f, 0.1f, -8f);
                spawns.Add(sp.transform);
            }

            // Council corner (hub stand-in): a raised platform + Veresk.
            Box("CouncilPlatform", new Vector3(0, 0.15f, -15), new Vector3(8, 0.3f, 6), crate);
            var veresk = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            veresk.name = "Councilor Veresk";
            veresk.transform.position = new Vector3(0, 1.3f, -16);
            veresk.GetComponent<Renderer>().sharedMaterial =
                Mat("M_Npc", new Color(0.85f, 0.75f, 0.35f));
            veresk.AddComponent<NpcVisual>().Model = "Mage";   // robed councilor
            veresk.AddComponent<NpcInteract>();
            var plateGo = new GameObject("Nameplate");
            plateGo.transform.SetParent(veresk.transform, false);
            plateGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var plate = plateGo.AddComponent<TextMesh>();
            plate.text = "Councilor Veresk";
            plate.characterSize = 0.055f;
            plate.fontSize = 40;
            plate.anchor = TextAnchor.LowerCenter;
            plate.color = new Color(1f, 0.92f, 0.6f);
            plateGo.AddComponent<Billboard>();

            // Encounter blocks from content/zones/*.json.
            void Encounter(string id, string zoneId, string display, Vector3 pos, Vector3 size,
                bool required, string[] monsters, string bonusLoot = "")
            {
                var go = new GameObject($"Encounter_{id}");
                go.transform.position = pos;
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = size;
                var trig = go.AddComponent<EncounterTrigger>();
                trig.EncounterId = id;
                trig.ZoneId = zoneId;
                trig.DisplayName = display;
                trig.RequiredForClear = required;
                trig.MonsterIds = monsters;
                trig.BonusLootTable = bonusLoot;
            }

            // Zone 0 — The Old Docks (west).
            Encounter("enc_docks_01", "old_docks", "the waterfront yard",
                new Vector3(-30, 1.5f, 0), new Vector3(10, 3, 10), true,
                new[] { "marsh_skulker", "marsh_skulker", "dock_rat" });
            Encounter("enc_docks_02", "old_docks", "the rat nests",
                new Vector3(-45, 1.5f, 12), new Vector3(10, 3, 10), true,
                new[] { "dock_rat", "dock_rat", "dock_rat" });
            Encounter("enc_docks_03", "old_docks", "the smugglers' pier",
                new Vector3(-40, 1.5f, -20), new Vector3(12, 3, 12), true,
                new[] { "marsh_skulker", "marsh_skulker", "marsh_skulker", "marsh_skulker" });
            Encounter("enc_docks_optional_warehouse", "old_docks", "the locked warehouse",
                new Vector3(-22, 1.5f, -32), new Vector3(8, 3, 10), false,
                new[] { "marsh_skulker", "marsh_skulker", "dock_rat", "dock_rat" },
                "lt_warehouse_cache");

            // Zone 1 — The Drowned Market (north, gated).
            Encounter("enc_market_01", "drowned_market", "the flooded arcade",
                new Vector3(-20, 1.5f, 38), new Vector3(10, 3, 10), true,
                new[] { "risen_drowned", "risen_drowned", "bonewalker" });
            Encounter("enc_market_02", "drowned_market", "the fountain square",
                new Vector3(0, 1.5f, 40), new Vector3(10, 3, 8), true,
                new[] { "bonewalker", "bonewalker", "risen_drowned" });
            Encounter("enc_market_03", "drowned_market", "the sunken rows",
                new Vector3(20, 1.5f, 35), new Vector3(10, 3, 10), true,
                new[] { "risen_drowned", "risen_drowned", "risen_drowned", "bonewalker" });
            Encounter("enc_market_04_tollkeeper", "drowned_market", "the Toll-Keeper's gate",
                new Vector3(0, 1.5f, 54), new Vector3(12, 3, 8), true,
                new[] { "bonewalker", "bonewalker", "bonewalker", "bonewalker" });
            Encounter("enc_market_optional_vault", "drowned_market", "the sunken vault",
                new Vector3(28, 1.5f, 52), new Vector3(8, 3, 8), false,
                new[] { "risen_drowned", "risen_drowned", "bonewalker", "bonewalker" },
                "lt_sunken_vault");

            // Zone 2 — The Glasslit Temple (east, gated).
            Encounter("enc_temple_01", "glasslit_temple", "the shattered nave",
                new Vector3(42, 1.5f, -25), new Vector3(10, 3, 10), true,
                new[] { "kindled_zealot", "kindled_zealot", "kindled_zealot" });
            Encounter("enc_temple_02", "glasslit_temple", "the ember cloister",
                new Vector3(52, 1.5f, -12), new Vector3(10, 3, 10), true,
                new[] { "kindled_zealot", "kindled_zealot", "kindled_zealot", "kindled_zealot" });
            Encounter("enc_temple_03", "glasslit_temple", "the processional",
                new Vector3(44, 1.5f, 0), new Vector3(10, 3, 10), true,
                new[] { "kindled_zealot", "kindled_zealot", "kindled_zealot",
                        "kindled_zealot", "kindled_zealot" });
            Encounter("enc_temple_04", "glasslit_temple", "the glass gallery",
                new Vector3(52, 1.5f, 10), new Vector3(10, 3, 8), true,
                new[] { "kindled_zealot", "kindled_zealot", "kindled_zealot", "kindled_zealot" });
            Encounter("enc_temple_05_sorrel", "glasslit_temple", "the Lightwell",
                new Vector3(50, 1.5f, 19), new Vector3(12, 3, 10), true,
                new[] { "hollow_warden", "kindled_zealot" });

            // Zone 2 — The Sunken Warcamp (south, gated): orc warband + boss fight.
            Encounter("enc_warcamp_01", "sunken_warcamp", "the picket line",
                new Vector3(10, 1.5f, -32), new Vector3(10, 3, 10), true,
                new[] { "orc", "orc" });
            Encounter("enc_warcamp_02", "sunken_warcamp", "the loot pens",
                new Vector3(22, 1.5f, -38), new Vector3(10, 3, 10), true,
                new[] { "orc", "orc", "orc" });
            Encounter("enc_warcamp_03_karg", "sunken_warcamp", "the war-tent",
                new Vector3(14, 1.5f, -46), new Vector3(12, 3, 10), true,
                new[] { "orc_warchief", "orc", "orc" }, "lt_sunken_vault");

            // Wilds — optional world fights on the roads between quarters (no quest,
            // just XP and loot): spiders, bears, goblin ambushes.
            Encounter("enc_wild_spiders_01", "wilds", "the webbed alley",
                new Vector3(-12, 1.5f, 24), new Vector3(9, 3, 9), false,
                new[] { "giant_spider", "giant_spider" });
            Encounter("enc_wild_spiders_02", "wilds", "the silk hollow",
                new Vector3(30, 1.5f, 26), new Vector3(9, 3, 9), false,
                new[] { "giant_spider", "giant_spider", "giant_spider" });
            Encounter("enc_wild_bear_01", "wilds", "a bear den",
                new Vector3(-24, 1.5f, -28), new Vector3(9, 3, 9), false,
                new[] { "brown_bear" });
            Encounter("enc_wild_bears_02", "wilds", "the briar thicket",
                new Vector3(26, 1.5f, -12), new Vector3(9, 3, 9), false,
                new[] { "brown_bear", "brown_bear" });
            Encounter("enc_wild_goblins_01", "wilds", "a goblin ambush",
                new Vector3(-16, 1.5f, 8), new Vector3(9, 3, 9), false,
                new[] { "goblin", "goblin", "goblin" });
            Encounter("enc_wild_goblins_02", "wilds", "the toll bridge",
                new Vector3(34, 1.5f, 4), new Vector3(9, 3, 9), false,
                new[] { "goblin", "goblin", "goblin", "goblin" });

            // District signs: a quest that says "retake the Old Docks" is useless if
            // nothing in the world is labelled "the Old Docks". Each quarter gets a name
            // floating over it (billboarded, high enough to clear the rooftops) on a lit
            // post, so the destination is visible from the hub. Positions are the centre
            // of each quarter's required encounters.
            void District(string label, Vector3 groundPos, Color colour)
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = $"DistrictSign_{label}";
                post.transform.position = groundPos + new Vector3(0f, 3f, 0f);
                post.transform.localScale = new Vector3(0.25f, 3f, 0.25f);
                Object.DestroyImmediate(post.GetComponent<Collider>());
                post.GetComponent<Renderer>().sharedMaterial =
                    Mat("M_SignPost", new Color(0.32f, 0.22f, 0.14f));

                var textGo = new GameObject("DistrictLabel");
                textGo.transform.SetParent(post.transform, false);
                textGo.transform.position = groundPos + new Vector3(0f, 8.2f, 0f);
                var text = textGo.AddComponent<TextMesh>();
                text.text = label;
                text.characterSize = 0.34f;      // readable clear across the map
                text.fontSize = 52;
                text.anchor = TextAnchor.LowerCenter;
                text.color = colour;
                textGo.AddComponent<Billboard>();

                var glow = new GameObject($"DistrictGlow_{label}").AddComponent<Light>();
                glow.transform.position = groundPos + new Vector3(0f, 6.5f, 0f);
                glow.type = LightType.Point;
                glow.range = 16f;
                glow.intensity = 2.2f;
                glow.color = colour;
            }

            var signGold = new Color(1f, 0.87f, 0.55f);
            District("The Old Docks", new Vector3(-38f, 0f, -3f), signGold);
            District("The Drowned Market", new Vector3(0f, 0f, 42f), signGold);
            District("The Sunken Warcamp", new Vector3(15f, 0f, -39f), signGold);
            District("The Glasslit Temple", new Vector3(48f, 0f, -2f), signGold);

            // Vendor NPC by the council platform.
            var vendor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vendor.name = "The Salvage Exchange";
            vendor.transform.position = new Vector3(4, 1.3f, -13);
            vendor.GetComponent<Renderer>().sharedMaterial =
                Mat("M_Vendor", new Color(0.4f, 0.75f, 0.5f));
            vendor.AddComponent<NpcVisual>().Model = "Barbarian";  // burly trader
            vendor.AddComponent<VendorInteract>();
            var vplateGo = new GameObject("Nameplate");
            vplateGo.transform.SetParent(vendor.transform, false);
            vplateGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var vplate = vplateGo.AddComponent<TextMesh>();
            vplate.text = "Salvage Exchange";
            vplate.characterSize = 0.055f;
            vplate.fontSize = 40;
            vplate.anchor = TextAnchor.LowerCenter;
            vplate.color = new Color(0.7f, 1f, 0.8f);
            vplateGo.AddComponent<Billboard>();

            // Smith NPC on the other side of the council platform: buy weapon/armor
            // upgrades (SmithInteract reads stock from GameDirector.SmithStock).
            var smith = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            smith.name = "The Broken Anvil";
            smith.transform.position = new Vector3(-4, 1.3f, -13);
            smith.GetComponent<Renderer>().sharedMaterial =
                Mat("M_Smith", new Color(0.75f, 0.45f, 0.3f));
            smith.AddComponent<NpcVisual>().Model = "Knight";   // armored smith
            smith.AddComponent<SmithInteract>();
            var splateGo = new GameObject("Nameplate");
            splateGo.transform.SetParent(smith.transform, false);
            splateGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var splate = splateGo.AddComponent<TextMesh>();
            splate.text = "The Broken Anvil (smith)";
            splate.characterSize = 0.055f;
            splate.fontSize = 40;
            splate.anchor = TextAnchor.LowerCenter;
            splate.color = new Color(1f, 0.8f, 0.6f);
            splateGo.AddComponent<Billboard>();

            // Networking + game systems.
            var netGo = new GameObject("NetworkManager");
            netGo.AddComponent<NetworkManager>();
            netGo.AddComponent<Tugboat>();
            var spawner = netGo.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(playerPrefab);   // public API (verified vs FishNet source)
            spawner.Spawns = spawns.ToArray();       // public field, not "_spawns"
            netGo.AddComponent<SessionLauncher>();

            var systemsGo = new GameObject("GameSystems");
            systemsGo.AddComponent<NetworkObject>();
            systemsGo.AddComponent<GameAudio>();
            systemsGo.AddComponent<CombatFx>();
            systemsGo.AddComponent<CombatManager>();
            var director = systemsGo.AddComponent<GameDirector>();
            director.Zones = new[]
            {
                new GameDirector.ZoneConfig
                {
                    ZoneId = "old_docks", DisplayName = "The Old Docks",
                    QuestName = "Retake the Old Docks",
                    Description = "Squatter gangs hold three yards along the waterfront " +
                        "WEST of the hub. Break all three, then return to Veresk.",
                    RequiredEncounters = 3, XpEach = 300, Gold = 100
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "drowned_market", DisplayName = "The Drowned Market",
                    QuestName = "Silence the Drowned Market",
                    Description = "The drowned dead haunt the flooded market NORTH of the " +
                        "hub. Lay all four hauntings to rest — mind the Toll-Keeper.",
                    RequiredEncounters = 4, XpEach = 900, Gold = 250
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "sunken_warcamp", DisplayName = "The Sunken Warcamp",
                    QuestName = "The Warband Below",
                    Description = "Karg Splitjaw's orc warband holds the sunken quarter " +
                        "SOUTH of the walls. Break both picket bands, then storm the " +
                        "war-tent and slay the Warchief.",
                    RequiredEncounters = 3, XpEach = 1200, Gold = 400
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "glasslit_temple", DisplayName = "The Glasslit Temple",
                    QuestName = "The Fire in the Glass",
                    Description = "The Kindled cult holds the Glasslit Temple EAST of the " +
                        "hub. Break their five circles and face what wears the Warden.",
                    RequiredEncounters = 5, XpEach = 3400, Gold = 600
                }
            };
            director.CompanionPrefab = playerPrefab;
            systemsGo.AddComponent<CombatClientUI>();
            systemsGo.AddComponent<SettingsMenu>();
            systemsGo.AddComponent<MiniMap>();
            systemsGo.AddComponent<QuestTracker>();
            systemsGo.AddComponent<InventoryUI>();
            systemsGo.AddComponent<HotBar>();

            // FishNet scene NetworkObjects need SceneIds; the editor UI stamps them via
            // its own hooks, but a batchmode-generated scene must do it explicitly.
            // CreateSceneId(Scene, bool force, out int changed) is internal — reflection.
            var createSceneId = typeof(NetworkObject).GetMethod("CreateSceneId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (createSceneId != null)
            {
                object[] sceneIdArgs = { scene, true, 0 };
                createSceneId.Invoke(null, sceneIdArgs);
                Debug.Log($"[Bootstrap] FishNet SceneIds stamped ({sceneIdArgs[2]} changed).");
            }
            else
            {
                Debug.LogWarning("[Bootstrap] Could not find NetworkObject.CreateSceneId — " +
                    "run Tools > Fish-Networking > Utility > Reserialize NetworkObjects manually.");
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Bootstrap] Scene saved to {ScenePath}");
        }
    }
}
