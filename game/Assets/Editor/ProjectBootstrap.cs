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

            // Gray-box geometry: dockyard floor, water edge, crates/warehouses, perimeter.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8f, 1f, 8f); // 80x80 m
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

            // Perimeter walls.
            Box("Wall_N", new Vector3(0, 1.5f, 40), new Vector3(80, 3, 1), wall);
            Box("Wall_S", new Vector3(0, 1.5f, -40), new Vector3(80, 3, 1), wall);
            Box("Wall_E", new Vector3(40, 1.5f, 0), new Vector3(1, 3, 80), wall);
            Box("Wall_W", new Vector3(-40, 1.5f, 0), new Vector3(1, 3, 80), wall);
            // "Water" strip along the east edge (visual only).
            Box("Water", new Vector3(34, 0.05f, 0), new Vector3(11, 0.1f, 78), water);
            // Warehouses.
            Box("Warehouse_A", new Vector3(-18, 3, 14), new Vector3(14, 6, 10), wall);
            Box("Warehouse_B", new Vector3(-15, 2.5f, -16), new Vector3(10, 5, 12), wall);
            Box("Warehouse_C", new Vector3(12, 3, 22), new Vector3(12, 6, 8), wall);
            // Crate clusters (future encounter sites).
            Box("Crates_1", new Vector3(6, 0.75f, 2), new Vector3(1.5f, 1.5f, 1.5f), crate);
            Box("Crates_2", new Vector3(8, 0.75f, 3.5f), new Vector3(1.5f, 1.5f, 1.5f), crate);
            Box("Crates_3", new Vector3(7, 2f, 2.7f), new Vector3(1.4f, 1.0f, 1.4f), crate);
            Box("Crates_4", new Vector3(-4, 0.75f, -24), new Vector3(1.5f, 1.5f, 1.5f), crate);
            Box("Crates_5", new Vector3(18, 0.75f, -8), new Vector3(1.5f, 1.5f, 1.5f), crate);

            // Spawn points.
            var spawnParent = new GameObject("SpawnPoints");
            var spawns = new List<Transform>();
            for (int i = 0; i < 4; i++)
            {
                var sp = new GameObject($"Spawn_{i}");
                sp.transform.SetParent(spawnParent.transform);
                sp.transform.position = new Vector3(-2f + i * 1.6f, 0.1f, -4f);
                spawns.Add(sp.transform);
            }

            // Council corner (hub stand-in): a raised platform + Veresk.
            Box("CouncilPlatform", new Vector3(0, 0.15f, -12), new Vector3(8, 0.3f, 6), crate);
            var veresk = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            veresk.name = "Councilor Veresk";
            veresk.transform.position = new Vector3(0, 1.3f, -13);
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

            // Encounter blocks from content/zones/old_docks.json.
            void Encounter(string id, string display, Vector3 pos, Vector3 size,
                bool required, string[] monsters)
            {
                var go = new GameObject($"Encounter_{id}");
                go.transform.position = pos;
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = size;
                var trig = go.AddComponent<EncounterTrigger>();
                trig.EncounterId = id;
                trig.ZoneId = "old_docks";
                trig.DisplayName = display;
                trig.RequiredForClear = required;
                trig.MonsterIds = monsters;
            }

            Encounter("enc_docks_01", "the waterfront yard", new Vector3(-18, 1.5f, 2),
                new Vector3(10, 3, 10), true,
                new[] { "marsh_skulker", "marsh_skulker", "dock_rat" });
            Encounter("enc_docks_02", "the rat nests", new Vector3(6, 1.5f, 14),
                new Vector3(10, 3, 10), true,
                new[] { "dock_rat", "dock_rat", "dock_rat" });
            Encounter("enc_docks_03", "the smugglers' pier", new Vector3(22, 1.5f, -14),
                new Vector3(12, 3, 12), true,
                new[] { "marsh_skulker", "marsh_skulker", "marsh_skulker", "marsh_skulker" });
            Encounter("enc_docks_optional_warehouse", "the locked warehouse",
                new Vector3(-15, 1.5f, -16), new Vector3(8, 3, 10), false,
                new[] { "marsh_skulker", "marsh_skulker", "dock_rat", "dock_rat" });
            GameObject.Find("Encounter_enc_docks_optional_warehouse")
                .GetComponent<EncounterTrigger>().BonusLootTable = "lt_warehouse_cache";

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
            var so = new SerializedObject(spawner);
            so.FindProperty("_playerPrefab").objectReferenceValue = playerPrefab;
            var spawnsProp = so.FindProperty("_spawns");
            spawnsProp.arraySize = spawns.Count;
            for (int i = 0; i < spawns.Count; i++)
                spawnsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawns[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            netGo.AddComponent<SessionLauncher>();

            var systemsGo = new GameObject("GameSystems");
            systemsGo.AddComponent<NetworkObject>();
            systemsGo.AddComponent<CombatManager>();
            var director = systemsGo.AddComponent<GameDirector>();
            director.ZoneId = "old_docks";
            director.ZoneDisplayName = "The Old Docks";
            director.RequiredEncounters = 3;
            director.QuestXpEach = 300;
            director.QuestGold = 100;
            systemsGo.AddComponent<CombatClientUI>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Bootstrap] Scene saved to {ScenePath}");
        }
    }
}
