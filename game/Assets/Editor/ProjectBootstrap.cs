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
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (loaded != null) return loaded.GetComponent<NetworkObject>();

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

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<NetworkObject>();
        }

        private static void CreateGrayboxScene(NetworkObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting.
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.88f);
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.22f);

            // Camera.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<OrbitCamera>();
            camGo.transform.position = new Vector3(0f, 6f, -10f);

            // Gray-box geometry: 120x120 map, hub south-center, docks west,
            // Drowned Market north (gated), Glasslit Temple east (gated).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(12f, 1f, 12f); // 120x120 m
            ground.GetComponent<Renderer>().sharedMaterial = Mat("M_Ground", new Color(0.42f, 0.4f, 0.38f));

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
            gateMarket.GetComponent<Renderer>().sharedMaterial = gateMat;
            gateMarket.AddComponent<ZoneGate>().ZoneIndex = 1;

            // Temple divider (x=35, south of the market wall) with a 6 m gate gap at z=0.
            Box("TempleWall_S", new Vector3(35, 1.5f, -31.5f), new Vector3(1, 3, 57), wall);
            Box("TempleWall_N", new Vector3(35, 1.5f, 14), new Vector3(1, 3, 22), wall);
            var gateTemple = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateTemple.name = "Gate_GlasslitTemple";
            gateTemple.transform.position = new Vector3(35, 1.5f, 0);
            gateTemple.transform.localScale = new Vector3(1.2f, 3, 6);
            gateTemple.GetComponent<Renderer>().sharedMaterial = gateMat;
            gateTemple.AddComponent<ZoneGate>().ZoneIndex = 2;

            // Zone dressing.
            Box("Docks_Warehouse_A", new Vector3(-40, 3, 8), new Vector3(14, 6, 10), wall);
            Box("Docks_Warehouse_B", new Vector3(-25, 2.5f, -25), new Vector3(10, 5, 12), wall);
            Box("Docks_Crates_1", new Vector3(-32, 0.75f, -6), new Vector3(1.5f, 1.5f, 1.5f), crate);
            Box("Docks_Crates_2", new Vector3(-30.5f, 0.75f, -4.5f), new Vector3(1.5f, 1.5f, 1.5f), crate);
            Box("Market_Stalls_1", new Vector3(-12, 1f, 40), new Vector3(6, 2, 3), crate);
            Box("Market_Stalls_2", new Vector3(10, 1f, 48), new Vector3(6, 2, 3), crate);
            Box("Market_Fountain", new Vector3(0, 0.6f, 45), new Vector3(4, 1.2f, 4), wall);
            Box("Temple_Pillar_1", new Vector3(42, 2.5f, -6), new Vector3(1.5f, 5, 1.5f), wall);
            Box("Temple_Pillar_2", new Vector3(42, 2.5f, 6), new Vector3(1.5f, 5, 1.5f), wall);
            var lightwell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lightwell.name = "The Lightwell";
            lightwell.transform.position = new Vector3(52, 0.3f, 20);
            lightwell.transform.localScale = new Vector3(6, 0.3f, 6);
            lightwell.GetComponent<Renderer>().sharedMaterial =
                Mat("M_Lightwell", new Color(0.95f, 0.85f, 0.45f));

            // Spawn points (hub plaza).
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
            veresk.AddComponent<NpcInteract>();
            var plateGo = new GameObject("Nameplate");
            plateGo.transform.SetParent(veresk.transform, false);
            plateGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var plate = plateGo.AddComponent<TextMesh>();
            plate.text = "Councilor Veresk";
            plate.characterSize = 0.09f;
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

            // Vendor NPC by the council platform.
            var vendor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            vendor.name = "The Salvage Exchange";
            vendor.transform.position = new Vector3(4, 1.3f, -13);
            vendor.GetComponent<Renderer>().sharedMaterial =
                Mat("M_Vendor", new Color(0.4f, 0.75f, 0.5f));
            vendor.AddComponent<VendorInteract>();
            var vplateGo = new GameObject("Nameplate");
            vplateGo.transform.SetParent(vendor.transform, false);
            vplateGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var vplate = vplateGo.AddComponent<TextMesh>();
            vplate.text = "Salvage Exchange";
            vplate.characterSize = 0.09f;
            vplate.fontSize = 40;
            vplate.anchor = TextAnchor.LowerCenter;
            vplate.color = new Color(0.7f, 1f, 0.8f);
            vplateGo.AddComponent<Billboard>();

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
            systemsGo.AddComponent<CombatManager>();
            var director = systemsGo.AddComponent<GameDirector>();
            director.Zones = new[]
            {
                new GameDirector.ZoneConfig
                {
                    ZoneId = "old_docks", DisplayName = "The Old Docks",
                    QuestName = "Retake the Old Docks",
                    RequiredEncounters = 3, XpEach = 300, Gold = 100
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "drowned_market", DisplayName = "The Drowned Market",
                    QuestName = "Silence the Drowned Market",
                    RequiredEncounters = 4, XpEach = 900, Gold = 250
                },
                new GameDirector.ZoneConfig
                {
                    ZoneId = "glasslit_temple", DisplayName = "The Glasslit Temple",
                    QuestName = "The Fire in the Glass",
                    RequiredEncounters = 5, XpEach = 3400, Gold = 600
                }
            };
            systemsGo.AddComponent<CombatClientUI>();
            systemsGo.AddComponent<SettingsMenu>();

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
