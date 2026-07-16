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
            RpgMmoUi7Art.Bake();       // licensed UI art -> ignored runtime skin textures
            PolyPackArt.Invalidate();      // re-scan: the pack may have just been imported
            PolyPackArt.SetupMaterials();  // Asset Store packs ship Standard mats: magenta in URP
            KenneyArt.SetupMaterials();
            WarriorPackArt.Setup();    // licensed humanoid attacks, if selectively installed
            KayKitArt.Setup();
            QuaterniusArt.Setup();
            GeneratedArt.Setup();   // bear + rat: the CC0 packs have neither
            ItemIconBaker.Bake();   // bag icons, shot from the very models the hands hold
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
            // Desktop-first MMORPG presentation: stable 4x MSAA, HDR colour and enough
            // shadow reach that the district silhouettes do not visibly pop in.
            existing.msaaSampleCount = 4;
            existing.renderScale = 1f;
            existing.supportsHDR = true;
            existing.shadowDistance = 70f;
            EditorUtility.SetDirty(existing);
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
                    tex.SetPixel(x, y, new Color(0.22f * v, 0.30f * v, 0.20f * v));
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
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", name == "M_Water" ? 0.62f : 0.16f);
            EditorUtility.SetDirty(m);
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
            light.intensity = 0.35f;
            light.color = new Color(1f, 0.55f, 0.34f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.12f, 0.14f, 0.19f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.09f, 0.10f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.035f, 0.04f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.09f, 0.075f, 0.085f);
            RenderSettings.fogDensity = 0.022f;

            // A procedural sky gives the bright stylized world a real horizon and sun
            // disc. It is generated as an asset so player builds retain the shader.
            var skyPath = SettingsDir + "/M_SunnySky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.075f, 0.085f, 0.14f));
            sky.SetColor("_GroundColor", new Color(0.035f, 0.03f, 0.04f));
            sky.SetFloat("_Exposure", 0.62f);
            RenderSettings.skybox = sky;

            // Camera with URP post-processing (bloom + vignette + slight warmth).
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<OrbitCamera>();
            camGo.transform.position = new Vector3(0f, 6f, -10f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            cam.allowHDR = true;

            var profilePath = SettingsDir + "/PostFX.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            // VolumeProfile.Add updates the in-memory list but does not persist a newly
            // created component inside an existing asset by itself. The old profile thus
            // serialized six {fileID: 0} entries and shipped none of its grading. Remove
            // those ghosts and embed every component as a real sub-asset.
            profile.components.RemoveAll(c => c == null);
            T ProfileEffect<T>() where T : VolumeComponent
            {
                if (!profile.TryGet(out T effect)) effect = profile.Add<T>();
                if (!AssetDatabase.Contains(effect)) AssetDatabase.AddObjectToAsset(effect, profile);
                EditorUtility.SetDirty(effect);
                return effect;
            }

            var bloom = ProfileEffect<Bloom>();
            bloom.active = true;
            bloom.intensity.Override(0.42f);
            bloom.threshold.Override(0.86f);
            bloom.scatter.Override(0.72f);
            var vignette = ProfileEffect<Vignette>();
            vignette.active = true;
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.72f);
            var colors = ProfileEffect<ColorAdjustments>();
            colors.active = true;
            colors.postExposure.Override(-0.22f);
            colors.contrast.Override(18f);
            colors.saturation.Override(-14f);
            var tonemapping = ProfileEffect<Tonemapping>();
            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);
            var whiteBalance = ProfileEffect<WhiteBalance>();
            whiteBalance.active = true;
            whiteBalance.temperature.Override(-8f);
            whiteBalance.tint.Override(-4f);
            var grain = ProfileEffect<FilmGrain>();
            grain.active = true;
            grain.intensity.Override(0.14f);
            grain.response.Override(0.72f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
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

            // Slow, sparse motes catch the sunlight and add depth without fogging the
            // battlefield. The camera carries the emission volume; particles simulate in
            // world space, so they drift past instead of sticking to the screen.
            var motePath = "Assets/Resources/Fx/M_AmbientMotes.mat";
            var moteMat = AssetDatabase.LoadAssetAtPath<Material>(motePath);
            if (moteMat == null)
            {
                moteMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                moteMat.SetColor("_BaseColor", new Color(1f, 0.88f, 0.58f, 0.42f));
                AssetDatabase.CreateAsset(moteMat, motePath);
            }
            var motesGo = new GameObject("Ambient Sun Motes");
            motesGo.transform.SetParent(camGo.transform, false);
            motesGo.transform.localPosition = new Vector3(0f, 2f, 10f);
            var motes = motesGo.AddComponent<ParticleSystem>();
            var main = motes.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.085f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.84f, 0.5f, 0.12f), new Color(1f, 0.96f, 0.78f, 0.42f));
            main.maxParticles = 90;
            var emission = motes.emission;
            emission.rateOverTime = 7f;
            var shape = motes.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(28f, 10f, 28f);
            var velocity = motes.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.02f, 0.09f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            motesGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = moteMat;

            // Gray-box geometry: 120x120 map, hub south-center, docks west,
            // Drowned Market north (gated), Glasslit Temple east (gated).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(12f, 1f, 12f); // 120x120 m
            var groundMat = Mat("M_Ground", new Color(0.24f, 0.31f, 0.22f));
            groundMat.mainTexture = GroundTexture();
            groundMat.mainTextureScale = new Vector2(48f, 48f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = name;
                b.transform.position = pos;
                b.transform.localScale = scale;
                b.GetComponent<Renderer>().sharedMaterial = mat;
                return b;
            }

            var wall = Mat("M_Wall", new Color(0.32f, 0.31f, 0.32f));
            var crate = Mat("M_Crate", new Color(0.35f, 0.25f, 0.18f));
            var water = Mat("M_Water", new Color(0.07f, 0.15f, 0.20f));
            var gateMat = Mat("M_Gate", new Color(0.15f, 0.14f, 0.18f));

            // Perimeter walls.
            Box("Wall_N", new Vector3(0, 1.5f, 60), new Vector3(120, 3, 1), wall);
            Box("Wall_S", new Vector3(0, 1.5f, -60), new Vector3(120, 3, 1), wall);
            Box("Wall_E", new Vector3(60, 1.5f, 0), new Vector3(1, 3, 120), wall);
            Box("Wall_W", new Vector3(-60, 1.5f, 0), new Vector3(1, 3, 120), wall);
            // Waterfront along the west edge (docks fiction, visual only).
            Box("Water", new Vector3(-56, 0.05f, 0), new Vector3(7, 0.1f, 118), water);

            // Market divider (z=25) with a 6 m gate gap at x=0.
            Box("MarketWall_W", new Vector3(-31.5f, 1.5f, 25), new Vector3(57, 3, 1), wall);
            // Leave a second postern at x=48. It stays sealed until the appended
            // Lightwell commission is active, then opens into the Ashen Ward.
            Box("MarketWall_E_A", new Vector3(24f, 1.5f, 25), new Vector3(42, 3, 1), wall);
            Box("MarketWall_E_B", new Vector3(55.5f, 1.5f, 25), new Vector3(9, 3, 1), wall);
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

            var gateAshen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateAshen.name = "Gate_AshenWard";
            gateAshen.transform.position = new Vector3(48, 1.5f, 25);
            gateAshen.transform.localScale = new Vector3(6, 3, 1.2f);
            gateAshen.GetComponent<Renderer>().enabled = false;
            gateAshen.AddComponent<ZoneGate>().ZoneIndex = 4;
            var gateAshenVisual = KenneyArt.Place("Pirate", "castle-gate",
                new Vector3(48, 0, 25), 0f, 7f);
            if (gateAshenVisual != null)
                gateAshenVisual.transform.SetParent(gateAshen.transform, true);

            // Temple divider (x=35, south of the market wall) with a 6 m gate gap at z=0.
            Box("TempleWall_S", new Vector3(35, 1.5f, -31.5f), new Vector3(1, 3, 57), wall);
            Box("TempleWall_N", new Vector3(35, 1.5f, 14), new Vector3(1, 3, 22), wall);
            var gateTemple = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateTemple.name = "Gate_GlasslitTemple";
            gateTemple.transform.position = new Vector3(35, 1.5f, 0);
            gateTemple.transform.localScale = new Vector3(1.2f, 3, 6);
            gateTemple.GetComponent<Renderer>().enabled = false;   // collider only
            gateTemple.AddComponent<ZoneGate>().ZoneIndex = 3;
            var gateTempleVisual = KenneyArt.Place("Pirate", "castle-gate",
                new Vector3(35, 0, 0), 90f, 7f);
            if (gateTempleVisual != null)
                gateTempleVisual.transform.SetParent(gateTemple.transform, true);

            // Zone dressing — Albion-style district color zoning: each quarter has its
            // own saturated palette so you always know where you are at a glance.
            var docksWood = Mat("M_DocksWood", new Color(0.26f, 0.20f, 0.16f));
            var docksRoof = Mat("M_DocksRoof", new Color(0.12f, 0.24f, 0.27f));
            var marketWood = Mat("M_MarketWood", new Color(0.38f, 0.33f, 0.26f));
            var marketCloth = Mat("M_MarketCloth", new Color(0.24f, 0.33f, 0.18f));
            var templeStone = Mat("M_TempleStone", new Color(0.43f, 0.37f, 0.31f));
            var templeEmber = Mat("M_TempleEmber", new Color(0.52f, 0.16f, 0.08f));
            var councilWall = Mat("M_CouncilWall", new Color(0.42f, 0.30f, 0.20f));
            var councilRoof = Mat("M_CouncilRoof", new Color(0.18f, 0.12f, 0.10f));

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

            // A real civic landmark behind Veresk, rather than a quest giver standing on
            // an anonymous crate platform. The collider-backed hall always exists; an
            // imported low-poly house becomes its visual when available.
            Roofed("Council Hall", new Vector3(0f, 3f, -25f),
                new Vector3(14f, 6f, 8f), councilWall, councilRoof);
            KenneyArt.Place("FantasyTown", "wall-doorway-round", new Vector3(0f, 0f, -20.8f),
                0f, 3.4f, true);
            KenneyArt.Place("FantasyTown", "pillar-stone", new Vector3(-5.2f, 0f, -20.8f),
                0f, 4.8f, true);
            KenneyArt.Place("FantasyTown", "pillar-stone", new Vector3(5.2f, 0f, -20.8f),
                0f, 4.8f, true);
            KenneyArt.Place("FantasyTown", "banner-red", new Vector3(-3.8f, 0.5f, -20.7f),
                0f, 3.1f, true);
            KenneyArt.Place("FantasyTown", "banner-green", new Vector3(3.8f, 0.5f, -20.7f),
                0f, 3.1f, true);

            var hallLabelGo = new GameObject("Council Hall Sign");
            hallLabelGo.transform.position = new Vector3(0f, 5.4f, -20.6f);
            var hallLabel = hallLabelGo.AddComponent<TextMesh>();
            hallLabel.text = "Council Hall";
            hallLabel.characterSize = 0.09f;
            hallLabel.fontSize = 46;
            hallLabel.anchor = TextAnchor.LowerCenter;
            hallLabel.color = new Color(1f, 0.86f, 0.45f);
            hallLabelGo.AddComponent<Billboard>();

            // The atmosphere self-test samples this point on the front elevation. Point
            // lights are omnidirectional, so being within both marked lamp ranges proves
            // the actual hall facade receives their warm night light.
            var hallLightTarget = new GameObject("Council Hall Facade Light Target");
            hallLightTarget.transform.position = new Vector3(0f, 3.2f, -20.7f);

            // --- CC0 Kenney set dressing (see Assets/Art/Kenney; CREDITS in README) ---
            var lampIron = Mat("M_LampIron", new Color(0.075f, 0.060f, 0.052f));
            var flameOrange = Mat("M_FlameOrange", new Color(1f, 0.22f, 0.035f));
            flameOrange.EnableKeyword("_EMISSION");
            flameOrange.SetColor("_EmissionColor", new Color(4.2f, 0.55f, 0.06f));
            EditorUtility.SetDirty(flameOrange);
            var flameGold = Mat("M_FlameGold", new Color(1f, 0.72f, 0.16f));
            flameGold.EnableKeyword("_EMISSION");
            flameGold.SetColor("_EmissionColor", new Color(5.2f, 2.2f, 0.22f));
            EditorUtility.SetDirty(flameGold);

            void FlamePiece(Transform parent, string name, Vector3 localPosition,
                Vector3 localScale, Material material)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                piece.name = name;
                Object.DestroyImmediate(piece.GetComponent<Collider>());
                piece.transform.SetParent(parent, false);
                piece.transform.localPosition = localPosition;
                piece.transform.localScale = localScale;
                piece.GetComponent<Renderer>().sharedMaterial = material;
            }

            void Lantern(Vector3 pos, string areaId = "district",
                bool illuminatesCouncilHall = false)
            {
                var root = new GameObject($"Flame Lamp Post [{areaId}]");
                root.transform.position = pos;
                var post = KenneyArt.Place("FantasyTown", "lantern", pos,
                    targetSize: 2.6f, byHeight: true);
                if (post != null)
                    post.transform.SetParent(root.transform, true);
                else
                {
                    // Art-pack-independent fallback: gameplay and a visible silhouette
                    // survive even when the Kenney lantern model is not installed.
                    var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    shaft.name = "Lamp Post";
                    Object.DestroyImmediate(shaft.GetComponent<Collider>());
                    shaft.transform.SetParent(root.transform, false);
                    shaft.transform.localPosition = Vector3.up * 1.1f;
                    shaft.transform.localScale = new Vector3(0.075f, 1.1f, 0.075f);
                    shaft.GetComponent<Renderer>().sharedMaterial = lampIron;
                }

                var basket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                basket.name = "Flame Basket";
                Object.DestroyImmediate(basket.GetComponent<Collider>());
                basket.transform.SetParent(root.transform, false);
                basket.transform.localPosition = Vector3.up * 2.18f;
                basket.transform.localScale = new Vector3(0.20f, 0.055f, 0.20f);
                basket.GetComponent<Renderer>().sharedMaterial = lampIron;

                // Three faceted emissive lobes read as fire from every camera bearing.
                // Their parent licks and leans in FlameLampPost.Update.
                var flame = new GameObject("Visible Flame");
                flame.transform.SetParent(root.transform, false);
                flame.transform.localPosition = Vector3.up * 2.22f;
                FlamePiece(flame.transform, "Flame Orange",
                    new Vector3(0f, 0.15f, 0f), new Vector3(0.11f, 0.17f, 0.11f), flameOrange);
                FlamePiece(flame.transform, "Flame Gold",
                    new Vector3(0f, 0.11f, -0.075f), new Vector3(0.065f, 0.105f, 0.065f), flameGold);
                FlamePiece(flame.transform, "Flame Tip",
                    new Vector3(0.035f, 0.39f, 0f), new Vector3(0.052f, 0.13f, 0.052f), flameOrange);

                var glowObject = new GameObject("GlowLight");
                glowObject.transform.SetParent(root.transform, false);
                glowObject.transform.localPosition = Vector3.up * 2.48f;
                var glow = glowObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = new Color(1f, 0.58f, 0.22f);
                glow.intensity = illuminatesCouncilHall ? 3.25f : 2.25f;
                glow.range = illuminatesCouncilHall ? 14f : 10f;
                glow.shadows = LightShadows.Soft;
                glow.shadowStrength = 0.35f;

                var marker = root.AddComponent<FlameLampPost>();
                marker.AreaId = areaId;
                marker.FlameVisual = flame.transform;
                marker.Glow = glow;
                marker.IlluminatesCouncilHall = illuminatesCouncilHall;
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
            // Four flames surround the quest giver's platform (front pair + back pair),
            // while two more carry that warm path through the fountain plaza.
            Lantern(new Vector3(-5, 0, -10), FlameLampPost.QuestCenterArea);
            Lantern(new Vector3(5, 0, -10), FlameLampPost.QuestCenterArea);
            Lantern(new Vector3(-4, 0, -20), FlameLampPost.QuestCenterArea, true);
            Lantern(new Vector3(4, 0, -20), FlameLampPost.QuestCenterArea, true);
            Lantern(new Vector3(-5, 0, 2), "town_center");
            Lantern(new Vector3(5, 0, 2), "town_center");
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

            // The newly opened ward reads as a distinct endgame space even from the
            // Lightwell: three low, emissive breach seals identify the actual quest sites.
            var breachMat = Mat("M_AshenBreach", new Color(0.22f, 0.07f, 0.035f));
            breachMat.EnableKeyword("_EMISSION");
            breachMat.SetColor("_EmissionColor", new Color(2.2f, 0.42f, 0.08f));
            void BreachSeal(string name, Vector3 pos)
            {
                var seal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seal.name = name;
                seal.transform.position = new Vector3(pos.x, 0.08f, pos.z);
                seal.transform.localScale = new Vector3(2.4f, 0.05f, 2.4f);
                Object.DestroyImmediate(seal.GetComponent<Collider>());
                seal.GetComponent<Renderer>().sharedMaterial = breachMat;
                var glow = new GameObject(name + " Glow").AddComponent<Light>();
                glow.transform.position = pos + Vector3.up * 1.2f;
                glow.type = LightType.Point;
                glow.color = new Color(1f, 0.24f, 0.06f);
                glow.intensity = 2.4f;
                glow.range = 9f;
            }
            BreachSeal("Ashen Breach I", new Vector3(42, 0, 34));
            BreachSeal("Ashen Breach II", new Vector3(53, 0, 43));
            BreachSeal("Ashen Breach III", new Vector3(42, 0, 52));

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

            // Council forecourt: a raised speaking platform in front of the lit hall.
            Box("CouncilPlatform", new Vector3(0, 0.15f, -15), new Vector3(8, 0.3f, 6), crate);
            var veresk = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            veresk.name = "Councilor Veresk";
            veresk.transform.position = new Vector3(0, 1.3f, -16);
            veresk.GetComponent<Renderer>().sharedMaterial =
                Mat("M_Npc", new Color(0.85f, 0.75f, 0.35f));
            var vereskVisual = veresk.AddComponent<NpcVisual>();
            vereskVisual.Model = "Mage";   // robed councilor
            vereskVisual.WeaponId = "quarterstaff";
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

            // Zone 4 — The Ashen Ward (northeast, beyond the Lightwell postern).
            Encounter("enc_ashen_01", "ashen_ward", "the breached postern",
                new Vector3(42, 1.5f, 34), new Vector3(9, 3, 9), true,
                new[] { "kindled_zealot", "kindled_zealot", "kindled_zealot", "bonewalker" });
            Encounter("enc_ashen_02", "ashen_ward", "the cinder court",
                new Vector3(53, 1.5f, 43), new Vector3(9, 3, 9), true,
                new[] { "kindled_zealot", "kindled_zealot", "kindled_zealot",
                        "kindled_zealot", "bonewalker" });
            Encounter("enc_ashen_03", "ashen_ward", "the hollow shrine",
                new Vector3(42, 1.5f, 52), new Vector3(10, 3, 9), true,
                new[] { "iron_sentinel", "giant_spider", "kindled_zealot", "kindled_zealot" });

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
                // Do not parent this under the post: the post is deliberately scaled
                // (thin in X/Z, tall in Y), which stretches child TextMesh glyphs into
                // unreadable vertical streaks. Position is enough; the scene owns both.
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
            District("The Ashen Ward", new Vector3(46f, 0f, 43f), signGold);

            // Council waystones are the scalable route to the larger campaign. The hub
            // stone opens a directory; each site stone is both its arrival anchor and the
            // return route. Future remote regions only add another entry here.
            var waystoneMat = Mat("M_Waystone", new Color(0.10f, 0.34f, 0.38f));
            waystoneMat.EnableKeyword("_EMISSION");
            waystoneMat.SetColor("_EmissionColor", new Color(0.12f, 1.1f, 1.25f));
            void Waystone(string label, Vector3 pos, int zoneIndex, bool directory)
            {
                var stone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stone.name = $"Waystone_{label}";
                stone.transform.position = pos + Vector3.up * 0.8f;
                stone.transform.localScale = new Vector3(0.65f, 0.8f, 0.65f);
                stone.GetComponent<Renderer>().sharedMaterial = waystoneMat;
                var travel = stone.AddComponent<CampaignTravel>();
                travel.IsDirectory = directory;
                travel.ReturnsToCouncil = !directory;
                travel.DisplayName = directory ? "Council Waystone Network" : label;
                stone.AddComponent<CampaignDestination>().ZoneIndex = zoneIndex;

                var glow = new GameObject($"WaystoneGlow_{label}").AddComponent<Light>();
                glow.transform.position = pos + Vector3.up * 1.8f;
                glow.type = LightType.Point;
                glow.color = new Color(0.2f, 0.9f, 1f);
                glow.range = 8f;
                glow.intensity = 1.8f;
            }
            Waystone("Council Quarter", new Vector3(8f, 0f, -8f), -1, true);
            Waystone("Old Docks", new Vector3(-21f, 0f, -2f), 0, false);
            Waystone("Drowned Market", new Vector3(0f, 0f, 29f), 1, false);
            Waystone("Sunken Warcamp", new Vector3(2f, 0f, -35f), 2, false);
            Waystone("Glasslit Temple", new Vector3(38f, 0f, -13f), 3, false);
            Waystone("Ashen Ward", new Vector3(48f, 0f, 28f), 4, false);

            // The expanded campaign lives in separated encounter cells beyond the city.
            // They remain in one scene (cheap networking and save compatibility), while
            // waystones make the separation feel like world travel rather than a long,
            // empty walk. Every cell has its own palette, silhouette, label, arrival
            // anchor, and three authored encounter spaces.
            Color SiteColor(CampaignSiteTheme theme)
            {
                switch (theme)
                {
                    case CampaignSiteTheme.Wilds: return new Color(0.12f, 0.25f, 0.16f);
                    case CampaignSiteTheme.Marsh: return new Color(0.10f, 0.23f, 0.22f);
                    case CampaignSiteTheme.Anchorage: return new Color(0.09f, 0.19f, 0.28f);
                    case CampaignSiteTheme.Crypt:
                    case CampaignSiteTheme.Necropolis: return new Color(0.17f, 0.17f, 0.22f);
                    case CampaignSiteTheme.Caves: return new Color(0.22f, 0.17f, 0.13f);
                    case CampaignSiteTheme.Archive:
                    case CampaignSiteTheme.Manor:
                    case CampaignSiteTheme.Quarter: return new Color(0.30f, 0.21f, 0.17f);
                    case CampaignSiteTheme.Observatory: return new Color(0.10f, 0.22f, 0.29f);
                    case CampaignSiteTheme.Spire: return new Color(0.28f, 0.09f, 0.055f);
                    default: return new Color(0.23f, 0.24f, 0.27f);
                }
            }

            Color SiteAccent(CampaignSiteTheme theme)
            {
                switch (theme)
                {
                    case CampaignSiteTheme.Wilds: return new Color(0.32f, 0.86f, 0.43f);
                    case CampaignSiteTheme.Marsh: return new Color(0.25f, 0.90f, 0.78f);
                    case CampaignSiteTheme.Anchorage:
                    case CampaignSiteTheme.Observatory: return new Color(0.22f, 0.72f, 1f);
                    case CampaignSiteTheme.Crypt:
                    case CampaignSiteTheme.Necropolis: return new Color(0.64f, 0.48f, 1f);
                    case CampaignSiteTheme.Caves: return new Color(1f, 0.58f, 0.20f);
                    case CampaignSiteTheme.Spire: return new Color(1f, 0.25f, 0.06f);
                    default: return new Color(0.95f, 0.72f, 0.28f);
                }
            }

            bool DungeonSite(CampaignSiteTheme theme)
            {
                switch (theme)
                {
                    case CampaignSiteTheme.Keep:
                    case CampaignSiteTheme.Ruins:
                    case CampaignSiteTheme.Crypt:
                    case CampaignSiteTheme.Archive:
                    case CampaignSiteTheme.Caves:
                    case CampaignSiteTheme.Observatory:
                    case CampaignSiteTheme.Redoubt:
                    case CampaignSiteTheme.Necropolis:
                    case CampaignSiteTheme.Gate:
                    case CampaignSiteTheme.Citadel:
                    case CampaignSiteTheme.Maze:
                    case CampaignSiteTheme.Spire: return true;
                    default: return false;
                }
            }

            bool SettlementSite(CampaignSiteTheme theme)
            {
                return theme == CampaignSiteTheme.Camp || theme == CampaignSiteTheme.Anchorage
                    || theme == CampaignSiteTheme.Enclave || theme == CampaignSiteTheme.Manor
                    || theme == CampaignSiteTheme.Quarter;
            }

            string SitePalette(CampaignSiteTheme theme)
            {
                switch (theme)
                {
                    case CampaignSiteTheme.Wilds: return "wild";
                    case CampaignSiteTheme.Marsh: return "marsh";
                    case CampaignSiteTheme.Anchorage:
                    case CampaignSiteTheme.Observatory: return "aether";
                    case CampaignSiteTheme.Crypt:
                    case CampaignSiteTheme.Necropolis: return "grave";
                    case CampaignSiteTheme.Caves: return "cave";
                    case CampaignSiteTheme.Archive:
                    case CampaignSiteTheme.Enclave:
                    case CampaignSiteTheme.Manor:
                    case CampaignSiteTheme.Quarter: return "civic";
                    case CampaignSiteTheme.Spire: return "ember";
                    default: return "stone";
                }
            }

            void FallbackSiteArt(CampaignSitePlan site, int index, Vector3 pos)
            {
                float rot = index * 57f;
                switch (site.Theme)
                {
                    case CampaignSiteTheme.Wilds:
                        KenneyArt.Place("Nature", index % 2 == 0 ? "tree_oak" : "tree_pineTallA",
                            pos, rot, 5.8f, true);
                        break;
                    case CampaignSiteTheme.Marsh:
                        KenneyArt.Place("Nature", index % 2 == 0
                            ? "tree_default_dark" : "plant_bushLarge", pos, rot,
                            index % 2 == 0 ? 5f : 2.4f, true);
                        break;
                    case CampaignSiteTheme.Camp:
                        KenneyArt.Place("Survival", index % 2 == 0 ? "tent-canvas" : "tent",
                            pos, rot, 3.4f, false);
                        break;
                    case CampaignSiteTheme.Anchorage:
                        KenneyArt.Place("Pirate", index % 3 == 0 ? "boat-row-small"
                            : index % 2 == 0 ? "crate" : "barrel", pos, rot,
                            index % 3 == 0 ? 4.5f : 1.4f, index % 3 != 0);
                        break;
                    case CampaignSiteTheme.Caves:
                        KenneyArt.Place("Nature", index % 2 == 0
                            ? "cliff_block_rock" : "rock_largeA", pos, rot,
                            index % 2 == 0 ? 4.5f : 3f, false);
                        break;
                    case CampaignSiteTheme.Enclave:
                    case CampaignSiteTheme.Manor:
                    case CampaignSiteTheme.Quarter:
                        KenneyArt.Place("FantasyTown", index % 2 == 0
                            ? "wall-broken" : "pillar-stone", pos, rot,
                            index % 2 == 0 ? 5f : 4.5f, index % 2 != 0);
                        break;
                    case CampaignSiteTheme.Necropolis:
                    case CampaignSiteTheme.Crypt:
                        KenneyArt.Place("FantasyTown", index % 2 == 0
                            ? "pillar-stone" : "wall-broken", pos, rot, 4.5f,
                            index % 2 == 0);
                        KenneyArt.Place("Nature", "tree_thin_dark", pos + new Vector3(1.5f, 0f, 1f),
                            rot + 31f, 3.8f, true);
                        break;
                    default:
                        KenneyArt.Place("FantasyTown", index % 3 == 0
                            ? "wall-doorway-round" : index % 2 == 0
                                ? "wall-broken" : "pillar-stone", pos, rot,
                            index % 2 == 0 ? 5f : 4.5f, index % 2 != 0);
                        break;
                }
            }

            GameObject SiteVisual(PolyPackArt.Source source, PolyPackArt.Kind kind, int index,
                CampaignSitePlan site, Vector3 pos, float rotation, float size, bool byHeight)
            {
                var visual = PolyPackArt.Place(source, kind, index, pos, rotation, size, byHeight);
                if (visual == null) return null;
                foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);
                var tag = visual.GetComponent<EnvironmentArtTag>();
                if (tag != null) tag.ZoneId = site.ZoneId;
                return visual;
            }

            void SitePointLight(CampaignSitePlan site, int index, Vector3 pos, Color color)
            {
                var light = new GameObject($"SiteLantern_{site.ZoneId}_{index}").AddComponent<Light>();
                light.transform.position = pos + Vector3.up * 2.2f;
                light.type = LightType.Point;
                light.color = color;
                light.intensity = 1.25f;
                light.range = 8f;
            }

            void RemoteSite(CampaignSitePlan site, int zoneIndex, int siteIndex)
            {
                var baseColor = SiteColor(site.Theme);
                var accent = SiteAccent(site.Theme);
                string palette = SitePalette(site.Theme);
                // Palette sharing keeps these repeated modular cells in the same render
                // batches instead of manufacturing three materials per destination.
                var groundSite = Mat($"M_Site_{palette}", baseColor);
                var edgeSite = Mat($"M_SiteEdge_{palette}", Color.Lerp(baseColor, Color.black, 0.35f));
                var glowSite = Mat($"M_SiteGlow_{palette}", accent * 0.45f);
                glowSite.EnableKeyword("_EMISSION");
                glowSite.SetColor("_EmissionColor", accent * 1.7f);

                // Arena footprint. Enlarged from the original 44x44 so each destination is a
                // roomier field to fight across; S scales every perimeter placement below so
                // the dressing, walls and backdrops grow with the boundary instead of leaving
                // props stranded in the middle of a bigger empty slab.
                // Sites sit on a 50-unit grid, so the arena stays under that spacing (edges +
                // dressing must not bleed into the neighbouring cell). 48 is as large as fits
                // with a margin; the per-site randomized fight spread below is what really
                // makes each destination feel different, not raw size alone.
                const float ArenaSize = 48f;              // was 44
                const float S = ArenaSize / 44f;          // ~1.09 perimeter scale factor
                const float ArenaHalf = ArenaSize / 2f;   // 24 — invisible boundary half-extent

                // Gameplay remains a simple, reliable collider slab. Its renderer is hidden
                // beneath a hand-painted plane so combat navigation never depends on prop mesh.
                var floorCollider = Box($"SiteGround_{site.ZoneId}", site.Center + Vector3.down * 0.18f,
                    new Vector3(ArenaSize, 0.35f, ArenaSize), groundSite);
                floorCollider.GetComponent<Renderer>().enabled = false;
                var paintedGround = GameObject.CreatePrimitive(PrimitiveType.Plane);
                paintedGround.name = $"Environment_HandpaintedGrass_{site.ZoneId}";
                paintedGround.transform.position = site.Center + Vector3.down * 0.005f;
                paintedGround.transform.localScale = new Vector3(ArenaSize / 10f, 1f, ArenaSize / 10f);
                Object.DestroyImmediate(paintedGround.GetComponent<Collider>());
                var paintedMaterial = HandpaintedGroundArt.ForTheme(site.Theme, baseColor);
                paintedGround.GetComponent<Renderer>().sharedMaterial = paintedMaterial ?? groundSite;
                var groundTag = paintedGround.AddComponent<EnvironmentArtTag>();
                groundTag.SourcePack = paintedMaterial != null ? "HandpaintedGrass" : "Fallback";
                groundTag.Role = "Ground";
                groundTag.ZoneId = site.ZoneId;

                // Invisible boundary colliders retain the old safe cell dimensions. Low-poly
                // walls, rocks, trees and fences now carry the visible silhouette.
                foreach (var edge in new[]
                {
                    Box($"SiteEdgeN_{site.ZoneId}", site.Center + new Vector3(0f, 0.55f, ArenaHalf), new Vector3(ArenaSize, 1.1f, 0.8f), edgeSite),
                    Box($"SiteEdgeS_{site.ZoneId}", site.Center + new Vector3(0f, 0.55f, -ArenaHalf), new Vector3(ArenaSize, 1.1f, 0.8f), edgeSite),
                    Box($"SiteEdgeE_{site.ZoneId}", site.Center + new Vector3(ArenaHalf, 0.55f, 0f), new Vector3(0.8f, 1.1f, ArenaSize), edgeSite),
                    Box($"SiteEdgeW_{site.ZoneId}", site.Center + new Vector3(-ArenaHalf, 0.55f, 0f), new Vector3(0.8f, 1.1f, ArenaSize), edgeSite)
                }) edge.GetComponent<Renderer>().enabled = false;

                var titleGo = new GameObject($"SiteLabel_{site.ZoneId}");
                titleGo.transform.position = site.Center + new Vector3(0f, 4.2f, 19f * S);
                var title = titleGo.AddComponent<TextMesh>();
                title.text = site.DisplayName;
                title.characterSize = 0.075f;
                title.fontSize = 48;
                title.anchor = TextAnchor.LowerCenter;
                title.color = accent;
                titleGo.AddComponent<Billboard>();

                var random = new System.Random(siteIndex * 7919 + 173);

                // PBR Graveyard + Nature Set 2.0 supplies the dominant perimeter at every
                // destination. If the locally licensed pack is absent, Simple Nature keeps
                // the same deterministic composition and the world still builds.
                var surroundSource = PolyPackArt.Has(PolyPackArt.Source.GraveyardNature)
                    ? PolyPackArt.Source.GraveyardNature : PolyPackArt.Source.SimpleNature;
                // Necropolis/crypt sites read as a walled cemetery, not a generic clearing:
                // the perimeter becomes graveyard walls (with rocky gaps for sight-lines)
                // instead of logs and bushes.
                bool graveyard = (site.Theme == CampaignSiteTheme.Necropolis
                                  || site.Theme == CampaignSiteTheme.Crypt)
                                 && PolyPackArt.Has(PolyPackArt.Source.GraveyardNature);
                for (int i = 0; i < 14; i++)
                {
                    float angle = (i / 14f) * Mathf.PI * 2f + (float)(random.NextDouble() - 0.5) * 0.24f;
                    float radius = (18f + (float)random.NextDouble() * 2.5f) * S;
                    var pos = site.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    PolyPackArt.Kind kind;
                    if (site.Theme == CampaignSiteTheme.Wilds) kind = i % 4 == 0 ? PolyPackArt.Kind.Bush : PolyPackArt.Kind.Tree;
                    else if (site.Theme == CampaignSiteTheme.Marsh) kind = i % 3 == 0 ? PolyPackArt.Kind.Tree : PolyPackArt.Kind.Bush;
                    // Two openings in the wall ring: the gate lane on the front (-Z, i≈10/11)
                    // and a rear frame for the mausoleum (+Z, i≈3/4). Rocks fill the gaps.
                    else if (graveyard) kind = (i == 3 || i == 4 || i == 10 || i == 11)
                        ? PolyPackArt.Kind.Rock
                        : i % 4 == 0 ? PolyPackArt.Kind.Rock : PolyPackArt.Kind.Ruin;
                    else kind = i % 5 == 0 ? PolyPackArt.Kind.Log : i % 2 == 0 ? PolyPackArt.Kind.Rock : PolyPackArt.Kind.Bush;
                    float size = kind == PolyPackArt.Kind.Tree ? 4.5f + (float)random.NextDouble() * 1.8f
                        : kind == PolyPackArt.Kind.Bush ? 0.65f + (float)random.NextDouble() * 0.65f
                        : kind == PolyPackArt.Kind.Ruin ? 4.0f + (float)random.NextDouble() * 1.1f
                        : 0.9f + (float)random.NextDouble() * 1.1f;
                    // Walls face tangent to the ring so they line up into a cemetery wall
                    // instead of scattering at random bearings.
                    float rot = kind == PolyPackArt.Kind.Ruin
                        ? -angle * Mathf.Rad2Deg + 90f
                        : (float)random.NextDouble() * 360f;
                    if (SiteVisual(surroundSource, kind, siteIndex * 31 + i,
                            site, pos, rot, size,
                            kind == PolyPackArt.Kind.Tree || kind == PolyPackArt.Kind.Bush) == null)
                        FallbackSiteArt(site, i, pos);
                }

                // A designed cemetery rather than a uniform headstone ring: a gate on the
                // entrance axis, a mausoleum landmark at the back lit warm, flanking death
                // statues, clustered graves varied in rotation and scale, and abandoned
                // coffins, skull-stones and corrupted ferns that tell the story of the seal
                // the quest sends you to close. Every piece is collider-free, so grid combat
                // is governed only by the invisible arena boundaries.
                if (graveyard)
                {
                    var gsrc = PolyPackArt.Source.GraveyardNature;

                    // Cemetery gate on the front (-Z) axis, in the gap the perimeter left.
                    var gatePos = site.Center + new Vector3(0f, 0f, -20f * S);
                    if (SiteVisual(gsrc, PolyPackArt.Kind.Ruin,
                            PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Ruin, "Gate"),
                            site, gatePos, 0f, 5.0f, false) != null)
                        SitePointLight(site, 90, gatePos + new Vector3(0f, 0f, 1.6f),
                            new Color(1f, 0.62f, 0.28f));   // warm lantern over the entrance

                    // The "black mausoleum" the Necropolis quest names — a church tower framed
                    // in the rear wall gap. It sits just OUTSIDE the arena as a backdrop
                    // landmark, never between the camera and the fighting centre, so the
                    // combat x-ray occlusion can't fade it into a translucent blob.
                    var mausPos = site.Center + new Vector3(0f, 0f, 23f * S);
                    var maus = SiteVisual(gsrc, PolyPackArt.Kind.House,
                        PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.House, "tower"),
                        site, mausPos, 180f, 9f, true);
                    if (maus != null)
                    {
                        SitePointLight(site, 91, mausPos + new Vector3(0f, 1f, -3f),
                            new Color(0.62f, 0.44f, 1f));   // violet ritual glow at the door
                        int statue = PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Grave, "statue");
                        SiteVisual(gsrc, PolyPackArt.Kind.Grave, statue, site,
                            site.Center + new Vector3(-5f * S, 0f, 17f * S), 150f, 2.4f, false);
                        SiteVisual(gsrc, PolyPackArt.Kind.Grave, statue, site,
                            site.Center + new Vector3(5f * S, 0f, 17f * S), 210f, 2.4f, false);
                    }

                    // Clustered graves — small groups at varied angle/radius/rotation/scale,
                    // never a perfect ring, kept off the front lane and the combat centre.
                    var graveRng = new System.Random(siteIndex * 6151 + 29);
                    float[] clusterAngles = { 0.9f, 2.1f, 3.5f, 4.3f, 5.4f };
                    int graveNo = 0;
                    foreach (float ca in clusterAngles)
                    {
                        int inCluster = 2 + graveRng.Next(0, 2);
                        float cr = 9.5f + (float)graveRng.NextDouble() * 4.5f;
                        for (int j = 0; j < inCluster; j++)
                        {
                            float a = ca + (float)(graveRng.NextDouble() - 0.5) * 0.4f;
                            float r = cr + (float)(graveRng.NextDouble() - 0.5) * 2.4f;
                            var gp = site.Center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                            SiteVisual(gsrc, PolyPackArt.Kind.Grave, siteIndex * 43 + graveNo,
                                site, gp, (float)graveRng.NextDouble() * 360f,
                                2.0f + (float)graveRng.NextDouble() * 0.7f, false);
                            graveNo++;
                        }
                    }

                    // Environmental storytelling: a disturbed grave spilling an open coffin,
                    // skull-stones, and corrupted ferns and ivy creeping over the plots.
                    SiteVisual(gsrc, PolyPackArt.Kind.Grave,
                        PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Grave, "coffin"), site,
                        site.Center + new Vector3(-8.5f, 0f, -6f), 40f, 2.3f, false);
                    int skull = PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Rock, "skull");
                    SiteVisual(gsrc, PolyPackArt.Kind.Rock, skull, site,
                        site.Center + new Vector3(7.5f, 0f, -3f), 120f, 1.4f, false);
                    SiteVisual(gsrc, PolyPackArt.Kind.Rock, skull, site,
                        site.Center + new Vector3(-6f, 0f, 8.5f), 250f, 1.2f, false);
                    int fern = PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Bush, "fern");
                    int ivy = PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Bush, "ivy");
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 1.256f + 0.5f;
                        float r = 9f + (i % 3) * 3.5f;
                        SiteVisual(gsrc, PolyPackArt.Kind.Bush, i == 2 ? ivy : fern, site,
                            site.Center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                            i * 63f, 0.9f + (i % 2) * 0.4f, true);
                    }
                }

                // Haunted (non-cemetery) sites — the drowned Keep, Blackbriar Manor, and the
                // Observatory trio — get graveyard ACCENTS layered over their own dressing, not
                // a walled necropolis: a couple of grave clusters, a watching statue, a
                // skull-stone, creeping dead ferns and one violet ritual candle. Small props
                // only, so the combat x-ray can fade an individual one without a looming blob.
                bool haunted = (site.Theme == CampaignSiteTheme.Keep
                                || site.Theme == CampaignSiteTheme.Manor
                                || site.Theme == CampaignSiteTheme.Observatory)
                               && PolyPackArt.Has(PolyPackArt.Source.GraveyardNature);
                if (haunted)
                {
                    var gsrc = PolyPackArt.Source.GraveyardNature;
                    var hRng = new System.Random(siteIndex * 5077 + 61);
                    int gno = 0;
                    foreach (float ca in new[] { 2.4f, 4.0f })   // tucked to the back corners
                    {
                        int n = 2 + hRng.Next(0, 2);
                        float cr = 10.5f + (float)hRng.NextDouble() * 4f;
                        for (int j = 0; j < n; j++)
                        {
                            float a = ca + (float)(hRng.NextDouble() - 0.5) * 0.5f;
                            float r = cr + (float)(hRng.NextDouble() - 0.5) * 2f;
                            SiteVisual(gsrc, PolyPackArt.Kind.Grave, siteIndex * 47 + gno, site,
                                site.Center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                                (float)hRng.NextDouble() * 360f,
                                1.9f + (float)hRng.NextDouble() * 0.6f, false);
                            gno++;
                        }
                    }
                    var statuePos = site.Center + new Vector3(-9f, 0f, 10f);
                    SiteVisual(gsrc, PolyPackArt.Kind.Grave,
                        PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Grave, "statue"), site,
                        statuePos, 130f, 2.3f, false);
                    SiteVisual(gsrc, PolyPackArt.Kind.Rock,
                        PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Rock, "skull"), site,
                        site.Center + new Vector3(8.5f, 0f, 9f), 200f, 1.3f, false);
                    int hfern = PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Bush, "fern");
                    int hivy = PolyPackArt.IndexOf(gsrc, PolyPackArt.Kind.Bush, "ivy");
                    for (int i = 0; i < 3; i++)
                    {
                        float a = 1.9f + i * 1.7f;
                        float r = 11f + i * 2f;
                        SiteVisual(gsrc, PolyPackArt.Kind.Bush, i == 1 ? hivy : hfern, site,
                            site.Center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                            i * 77f, 0.85f + (i % 2) * 0.35f, true);
                    }
                    SitePointLight(site, 92, statuePos + Vector3.up * 0.5f,
                        new Color(0.55f, 0.4f, 0.95f));   // one violet ritual candle
                }

                if (DungeonSite(site.Theme))
                {
                    // Broken wall arc, columns and clutter from Low Poly Dungeons Lite.
                    // Gaps are intentional sight-lines into the arena, not a continuous box.
                    for (int i = 0; i < 10; i++)
                    {
                        float angle = (i / 10f) * Mathf.PI * 2f + 0.18f;
                        float radius = (i % 3 == 0 ? 18.2f : 20.4f) * S;
                        var pos = site.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                        SiteVisual(PolyPackArt.Source.Dungeon, PolyPackArt.Kind.Ruin,
                            siteIndex * 19 + i, site, pos, -angle * Mathf.Rad2Deg + 90f,
                            i % 3 == 0 ? 4.5f : 6.2f, false);
                    }
                    int lightIndex = PolyPackArt.IndexOf(PolyPackArt.Source.Dungeon,
                        PolyPackArt.Kind.Prop, "Light_08");
                    for (int i = 0; i < 4; i++)
                    {
                        float angle = i * Mathf.PI * 0.5f + 0.7f;
                        var pos = site.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 16.8f * S;
                        SiteVisual(PolyPackArt.Source.Dungeon, PolyPackArt.Kind.Prop,
                            i == 0 ? lightIndex : siteIndex * 13 + i, site, pos,
                            i * 83f, i == 0 ? 2.2f : 1.45f, true);
                        if (i % 2 == 0) SitePointLight(site, i, pos, accent);
                    }
                }

                if (SettlementSite(site.Theme))
                {
                    // RPG Poly Pack supplies the larger silhouettes; props/fences vary with
                    // the seed so enclaves and camps no longer repeat the same six objects.
                    for (int i = 0; i < 4; i++)
                    {
                        float angle = i * Mathf.PI * 0.5f + 0.75f;
                        var pos = site.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 19f * S;
                        var kind = site.Theme == CampaignSiteTheme.Camp && i % 2 == 0
                            ? PolyPackArt.Kind.Tent : PolyPackArt.Kind.House;
                        if (SiteVisual(PolyPackArt.Source.RpgPoly, kind, siteIndex * 7 + i,
                                site, pos, -angle * Mathf.Rad2Deg + 180f,
                                kind == PolyPackArt.Kind.House ? 7.5f : 4.4f, false) == null)
                            SiteVisual(PolyPackArt.Source.RpgPoly, PolyPackArt.Kind.Prop,
                                siteIndex * 7 + i, site, pos, i * 71f, 2f, true);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI * 0.25f;
                        var pos = site.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 16.5f * S;
                        SiteVisual(PolyPackArt.Source.RpgPoly,
                            i % 3 == 0 ? PolyPackArt.Kind.Fence : PolyPackArt.Kind.Prop,
                            siteIndex * 23 + i, site, pos, i * 47f,
                            i % 3 == 0 ? 4.2f : 1.6f, i % 3 != 0);
                    }
                }
                else
                {
                    // Even hostile wilderness and ruins get a few abandoned RPG-pack props,
                    // making the locations feel inhabited and using every imported poly pack.
                    for (int i = 0; i < 3; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 3f + 0.4f;
                        var pos = site.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 15.8f * S;
                        SiteVisual(PolyPackArt.Source.RpgPoly, PolyPackArt.Kind.Prop,
                            siteIndex * 17 + i, site, pos, i * 113f, 1.5f, true);
                    }
                }

                // Per-site randomized fight layout. Instead of the same three fixed offsets at
                // every destination, the encounters spread across the (now larger) northern
                // field in front of the south waystone arrival — seeded per site so no two
                // locations share a formation, spaced so fights never merge, and kept clear of
                // the arrival point so nothing triggers the instant you step off the waystone.
                var layoutRng = new System.Random(siteIndex * 2749 + 41);
                float xLimit = ArenaHalf - 8f;                       // 16
                var offsets = new Vector3[site.Encounters.Length];
                var placedXZ = new System.Collections.Generic.List<Vector2>();
                for (int fi = 0; fi < offsets.Length; fi++)
                {
                    Vector2 p = Vector2.zero;
                    for (int attempt = 0; attempt < 48; attempt++)
                    {
                        float px = (float)(layoutRng.NextDouble() * 2.0 - 1.0) * xLimit;
                        float pz = -3f + (float)layoutRng.NextDouble() * (ArenaHalf - 4f); // -3..17
                        p = new Vector2(px, pz);
                        bool clear = true;
                        foreach (var q in placedXZ)
                            if (Vector2.Distance(q, p) < 12f) { clear = false; break; }
                        if (clear) break;
                    }
                    placedXZ.Add(p);
                    offsets[fi] = new Vector3(p.x, 1.5f, p.y);
                }
                for (int i = 0; i < site.Encounters.Length; i++)
                {
                    var fight = site.Encounters[i];
                    var seal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    seal.name = $"EncounterSeal_{site.ZoneId}_{fight.Suffix}";
                    seal.transform.position = site.Center + offsets[i] + Vector3.down * 1.42f;
                    seal.transform.localScale = new Vector3(3.4f, 0.04f, 3.4f);
                    Object.DestroyImmediate(seal.GetComponent<Collider>());
                    seal.GetComponent<Renderer>().sharedMaterial = glowSite;
                    seal.GetComponent<Renderer>().enabled = false;
                    // A broken circle of real stones reads as an ominous encounter place
                    // without the old giant neon primitive stamped across the terrain.
                    for (int stone = 0; stone < 5; stone++)
                    {
                        float angle = stone * Mathf.PI * 2f / 5f + i * 0.37f;
                        var encounterCenter = site.Center + new Vector3(offsets[i].x, 0f, offsets[i].z);
                        var stonePos = encounterCenter + new Vector3(
                            Mathf.Cos(angle) * 2.8f, 0f, Mathf.Sin(angle) * 2.8f);
                        SiteVisual(surroundSource, PolyPackArt.Kind.Rock,
                            siteIndex * 41 + i * 5 + stone, site, stonePos,
                            stone * 73f, 0.48f + stone * 0.035f, true);
                    }
                    Encounter($"enc_{site.ZoneId}_{fight.Suffix}", site.ZoneId,
                        fight.DisplayName, site.Center + offsets[i], new Vector3(7f, 3f, 7f),
                        true, fight.MonsterIds);
                }

                Waystone(site.DisplayName, site.Center + new Vector3(0f, 0f, -14f * S),
                    zoneIndex, false);
                var actionConfig = site.ToZoneConfig();
                if (!string.IsNullOrEmpty(actionConfig.SiteAction))
                {
                    var objective = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    objective.name = $"QuestObjective_{site.ZoneId}";
                    objective.transform.position = site.Center + new Vector3(0f, 0.7f, 17f * S);
                    objective.transform.localScale = new Vector3(0.8f, 0.7f, 0.8f);
                    objective.GetComponent<Renderer>().sharedMaterial = glowSite;
                    objective.GetComponent<Renderer>().enabled = false;
                    objective.AddComponent<CampaignObjectiveInteract>().ZoneIndex = zoneIndex;
                    int objectiveLantern = PolyPackArt.IndexOf(PolyPackArt.Source.Dungeon,
                        PolyPackArt.Kind.Prop, "Light_08");
                    var objectivePos = site.Center + new Vector3(0f, 0f, 17f * S);
                    SiteVisual(PolyPackArt.Source.Dungeon, PolyPackArt.Kind.Prop,
                        objectiveLantern, site, objectivePos, siteIndex * 29f, 2.4f, true);
                    SitePointLight(site, 20, objectivePos, accent);
                }
                var siteLight = new GameObject($"SiteLight_{site.ZoneId}").AddComponent<Light>();
                siteLight.transform.position = site.Center + new Vector3(0f, 7f, 0f);
                siteLight.type = LightType.Point;
                siteLight.color = accent;
                siteLight.intensity = 1.7f;
                siteLight.range = 30f * S;
            }

            for (int i = 0; i < CampaignExpansionContent.Sites.Length; i++)
                RemoteSite(CampaignExpansionContent.Sites[i], 5 + i, i);

            // The market's second commission is an event rather than another map. Fold
            // its Brass Auction decision into the reclaimed plaza after the hauntings.
            var auction = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            auction.name = "QuestObjective_drowned_market_auction";
            auction.transform.position = new Vector3(0f, 0.7f, 50f);
            auction.transform.localScale = new Vector3(0.8f, 0.7f, 0.8f);
            auction.GetComponent<Renderer>().sharedMaterial = waystoneMat;
            auction.AddComponent<CampaignObjectiveInteract>().ZoneIndex = 1;

            var delivery = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            delivery.name = "QuestObjective_old_docks_delivery";
            delivery.transform.position = new Vector3(-48f, 0.7f, 18f);
            delivery.transform.localScale = new Vector3(0.8f, 0.7f, 0.8f);
            delivery.GetComponent<Renderer>().sharedMaterial = waystoneMat;
            delivery.AddComponent<CampaignObjectiveInteract>().ZoneIndex = 0;

            // Vendor NPC by the council platform.
            var vendor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vendor.name = "The Salvage Exchange";
            vendor.transform.position = new Vector3(4, 1.3f, -13);
            vendor.GetComponent<Renderer>().sharedMaterial =
                Mat("M_Vendor", new Color(0.4f, 0.75f, 0.5f));
            var vendorVisual = vendor.AddComponent<NpcVisual>();
            vendorVisual.Model = "Barbarian";  // burly trader
            vendorVisual.WeaponId = "greataxe";
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
            var smithVisual = smith.AddComponent<NpcVisual>();
            smithVisual.Model = "Knight";   // armored smith
            smithVisual.WeaponId = "warhammer";
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

            // Local side-quest contact. The delivery resolves at the dock marker after
            // the waterfront is secured; its journal objective is part of zone 0 so it
            // shares save/progression truth rather than maintaining a duplicate counter.
            var apothecary = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            apothecary.name = "Emberleaf Apothecary";
            apothecary.transform.position = new Vector3(9f, 1.3f, -13f);
            apothecary.GetComponent<Renderer>().sharedMaterial =
                Mat("M_Apothecary", new Color(0.24f, 0.58f, 0.42f));
            var apothecaryVisual = apothecary.AddComponent<NpcVisual>();
            apothecaryVisual.Model = "Ranger";
            var aplateGo = new GameObject("Nameplate");
            aplateGo.transform.SetParent(apothecary.transform, false);
            aplateGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var aplate = aplateGo.AddComponent<TextMesh>();
            aplate.text = "Emberleaf Apothecary";
            aplate.characterSize = 0.055f;
            aplate.fontSize = 40;
            aplate.anchor = TextAnchor.LowerCenter;
            aplate.color = new Color(0.64f, 1f, 0.76f);
            aplateGo.AddComponent<Billboard>();

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
            var zoneConfigs = new List<GameDirector.ZoneConfig>
            {
                new GameDirector.ZoneConfig
                {
                    ZoneId = "old_docks", DisplayName = "The Old Docks",
                    QuestName = "Retake the Old Docks / A Bitter Draught",
                    Description = "Squatter gangs hold three yards along the waterfront " +
                        "WEST of the hub. Break all three, deliver the Emberleaf " +
                        "Apothecary's sealed draught at the marked dock contact, then " +
                        "follow the gold marker back to Council Hall.",
                    RequiredEncounters = 3,
                    StartsAvailable = true,
                    SiteAction = "Deliver the apothecary's sealed draught to the dock contact."
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "drowned_market", DisplayName = "The Drowned Market",
                    QuestName = "Silence the Drowned Market / The Brass Auction",
                    Description = "The drowned dead haunt the flooded market NORTH of the " +
                        "hub. Lay all four hauntings to rest, then return SOUTH to Council Hall.",
                    RequiredEncounters = 4,
                    PrerequisiteZoneIds = new[] { "old_docks" },
                    SiteAction = "Witness the Brass Auction and identify its winning faction.",
                    ChoiceA = "Expose the ash cartel", ChoiceB = "Follow the masked buyer"
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "sunken_warcamp", DisplayName = "The Sunken Warcamp",
                    QuestName = "The Warband Below",
                    Description = "Karg Splitjaw's orc warband holds the sunken quarter " +
                        "SOUTH of the walls. Break both picket bands, then storm the " +
                        "war-tent and slay the Warchief before returning to Council Hall.",
                    RequiredEncounters = 3,
                    PrerequisiteZoneIds = new[] { "drowned_market" }
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "glasslit_temple", DisplayName = "The Glasslit Temple",
                    QuestName = "The Fire in the Glass",
                    Description = "The Kindled cult holds the Glasslit Temple EAST of the " +
                        "hub. Break their five circles, face what wears the Warden, then " +
                        "return WEST to Council Hall.",
                    RequiredEncounters = 5,
                    PrerequisiteZoneIds = new[] { "sunken_warcamp" }
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "ashen_ward", DisplayName = "The Ashen Ward",
                    QuestName = "Beyond the Lightwell",
                    Description = "The Hollow Flame fled through the sealed postern " +
                        "NORTHEAST of the Lightwell. Follow the gold waypoint through the " +
                        "newly opened gate, seal all three breaches, then return SOUTHWEST " +
                        "to Council Hall.",
                    RequiredEncounters = 3,
                    PrerequisiteZoneIds = new[] { "glasslit_temple" }
                }
            };
            foreach (var site in CampaignExpansionContent.Sites)
                zoneConfigs.Add(site.ToZoneConfig());
            foreach (var config in zoneConfigs)
                config.ApplyCampaignReward();
            director.Zones = zoneConfigs.ToArray();
            director.CompanionPrefab = playerPrefab;
            systemsGo.AddComponent<CombatClientUI>();
            systemsGo.AddComponent<SettingsMenu>();
            systemsGo.AddComponent<MiniMap>();
            systemsGo.AddComponent<QuestTracker>();
            systemsGo.AddComponent<InventoryUI>();
            systemsGo.AddComponent<HotBar>();
            systemsGo.AddComponent<ProgressUI>();    // level-up screen (L)
            systemsGo.AddComponent<SessionPanel>();  // invite code, behind the hotbar icon

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
