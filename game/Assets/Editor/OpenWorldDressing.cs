using System;
using System.Collections.Generic;
using System.IO;
using RadiantPool.Game;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    public static class OpenWorldDressing
    {
        private const int Seed = 481516;
        private const int PropCap = 1600;
        private const float RoadWidth = 5f;
        private const string RoadMeshPath = "Assets/Settings/OpenWorldRoadMeshes.asset";
        private const string RoadTexturePath = "Assets/Settings/T_WildRoad.png";
        private const string RoadMaterialPath = "Assets/Settings/M_WildRoad.mat";

        private enum RegionFlavor
        {
            Lush,
            Mirewatch,
            Pine,
            Burnt
        }

        private sealed class Candidate
        {
            public Vector3 Position;
            public float Mask;
            public float Acceptance;
            public float Choice;
            public float MeadowRoll;
            public float KindRoll;
            public float Scale;
            public float Yaw;
            public RegionFlavor Flavor;
            public bool NearNecropolis;
            public int Pick;
        }

        private static readonly string[] FallbackTrees =
        {
            "tree_default", "tree_oak", "tree_pineTallA", "tree_pineRoundB",
            "tree_detailed", "tree_fat", "tree_tall"
        };

        private static readonly string[] FallbackPines =
        {
            "tree_pineTallA", "tree_pineRoundB", "tree_default_dark", "tree_thin_dark"
        };

        private static readonly string[] FallbackScatter =
        {
            "grass_large", "grass", "flower_redA", "flower_yellowA", "flower_purpleA",
            "plant_bush", "plant_bushLarge", "stone_smallA", "stone_smallD",
            "rock_smallB", "stump_round", "log"
        };

        public static void Dress()
        {
            try
            {
                Transform world = FindWorld();
                List<List<Vector3>> roads = OpenWorld.RoadPolylines();
                BuildRoads(world, roads);
                int props = ScatterWilderness(world, roads);
                BuildWorldRim(world);
                AssetDatabase.SaveAssets();
                Physics.SyncTransforms();
                Debug.Log($"[OpenWorldDressing] PASS props {props}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[OpenWorldDressing] FAIL {ex.Message}");
                throw;
            }
        }

        private static Transform FindWorld()
        {
            var world = GameObject.Find("World");
            if (world == null) throw new InvalidOperationException("World root missing");
            return world.transform;
        }

        private static void BuildRoads(Transform world, List<List<Vector3>> roads)
        {
            DestroyNamed("WildRoads");
            var root = new GameObject("WildRoads");
            root.transform.SetParent(world, false);
            MarkStatic(root);

            Material material = RoadMaterial();
            AssetDatabase.DeleteAsset(RoadMeshPath);
            bool first = true;
            for (int i = 0; i < roads.Count; i++)
            {
                Mesh mesh = RoadMesh(roads[i], i);
                if (first)
                {
                    AssetDatabase.CreateAsset(mesh, RoadMeshPath);
                    first = false;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(mesh, RoadMeshPath);
                }

                var road = new GameObject($"WildRoad_{i:00}");
                road.transform.SetParent(root.transform, false);
                road.AddComponent<MeshFilter>().sharedMesh = mesh;
                road.AddComponent<MeshRenderer>().sharedMaterial = material;
                MarkStatic(road);
            }

            if (first) throw new InvalidOperationException("road graph is empty");
            AssetDatabase.ImportAsset(RoadMeshPath, ImportAssetOptions.ForceUpdate);
        }

        private static Mesh RoadMesh(List<Vector3> points, int index)
        {
            if (points == null || points.Count < 2)
                throw new InvalidOperationException($"road {index} has fewer than two points");

            var vertices = new Vector3[points.Count * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(points.Count - 1) * 6];
            float length = 0f;

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 before = points[Mathf.Max(0, i - 1)];
                Vector3 after = points[Mathf.Min(points.Count - 1, i + 1)];
                Vector3 tangent = after - before;
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
                tangent.Normalize();
                Vector3 side = new Vector3(-tangent.z, 0f, tangent.x) * (RoadWidth * 0.5f);
                Vector3 center = new Vector3(points[i].x, -0.02f, points[i].z);
                vertices[i * 2] = center - side;
                vertices[i * 2 + 1] = center + side;
                if (i > 0) length += Vector3.Distance(points[i - 1], points[i]);
                uvs[i * 2] = new Vector2(0f, length / 6f);
                uvs[i * 2 + 1] = new Vector2(1f, length / 6f);
            }

            int triangle = 0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                int vertex = i * 2;
                triangles[triangle++] = vertex;
                triangles[triangle++] = vertex + 1;
                triangles[triangle++] = vertex + 2;
                triangles[triangle++] = vertex + 2;
                triangles[triangle++] = vertex + 1;
                triangles[triangle++] = vertex + 3;
            }

            var mesh = new Mesh { name = $"WildRoad_{index:00}_Mesh" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material RoadMaterial()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float broad = Mathf.PerlinNoise(x * 0.027f + 5.1f, y * 0.027f + 8.6f);
                float fine = Mathf.PerlinNoise(x * 0.091f + 17.3f, y * 0.091f + 2.4f);
                float value = 0.84f + broad * 0.22f + fine * 0.08f;
                texture.SetPixel(x, y, new Color(0.42f * value, 0.33f * value, 0.24f * value));
            }
            texture.Apply();
            File.WriteAllBytes(RoadTexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(RoadTexturePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(RoadTexturePath);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader missing");
            var material = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_WildRoad" };
                AssetDatabase.CreateAsset(material, RoadMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(RoadTexturePath);
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.14f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static int ScatterWilderness(Transform world, List<List<Vector3>> roads)
        {
            DestroyNamed("WildernessDressing");
            var root = new GameObject("WildernessDressing");
            root.transform.SetParent(world, false);
            MarkStatic(root);

            var random = new System.Random(Seed);
            var candidates = new List<Candidate>();
            int pick = 0;
            for (float z = OpenWorld.MinZ; z <= OpenWorld.MaxZ; z += 8f)
            for (float x = OpenWorld.MinX; x <= OpenWorld.MaxX; x += 8f)
            {
                float px = x + Jitter(random, 3f);
                float pz = z + Jitter(random, 3f);
                float choice = (float)random.NextDouble();
                float acceptance = (float)random.NextDouble();
                float meadowRoll = (float)random.NextDouble();
                float kindRoll = (float)random.NextDouble();
                float scaleRoll = (float)random.NextDouble();
                float yaw = (float)random.NextDouble() * 360f;
                if (!ScatterAllowed(px, pz, roads)) continue;

                RegionFlavor flavor = FlavorAt(px, pz);
                float mask = Mathf.PerlinNoise(px * 0.008f + 3.7f, pz * 0.008f + 9.2f);
                float treeAcceptance = flavor == RegionFlavor.Burnt ? 0.55f
                    : flavor == RegionFlavor.Mirewatch ? 0.70f : 1f;
                candidates.Add(new Candidate
                {
                    Position = new Vector3(px, 0f, pz), Mask = mask,
                    Acceptance = acceptance / treeAcceptance, Choice = choice,
                    MeadowRoll = meadowRoll, KindRoll = kindRoll,
                    Scale = scaleRoll, Yaw = yaw, Flavor = flavor,
                    NearNecropolis = NearSite("lanternfall_necropolis", px, pz, 120f),
                    Pick = pick++
                });
            }

            float threshold = 0.56f;
            int initialCount = CountAccepted(candidates, threshold);
            int count = initialCount;
            while (count > PropCap && threshold < 0.96f)
            {
                threshold += 0.01f;
                count = CountAccepted(candidates, threshold);
            }
            if (count > PropCap)
                throw new InvalidOperationException($"prop cap cannot converge from {count}");
            if (count < initialCount)
                Debug.Log($"[OpenWorldScatter] cap reduced {initialCount} to {count} "
                    + $"at tree threshold {threshold:F2}");

            int placed = 0;
            int trees = 0;
            int scatter = 0;
            int graves = 0;
            foreach (Candidate candidate in candidates)
            {
                bool tree = candidate.Mask > threshold && candidate.Acceptance < 1f;
                bool meadow = candidate.Mask < 0.35f && candidate.MeadowRoll < 0.30f;
                if (!tree && !meadow) continue;
                if (!Ground(candidate.Position, out Vector3 position)) continue;

                bool grave = false;
                GameObject go = tree
                    ? PlaceTree(candidate, position)
                    : PlaceScatter(candidate, position, out grave);
                if (go == null) continue;
                FinishProp(go, root.transform);
                placed++;
                if (tree) trees++;
                else if (grave) graves++;
                else scatter++;
            }

            if (placed > PropCap)
                throw new InvalidOperationException($"placed {placed} props over cap {PropCap}");
            Debug.Log($"[OpenWorldScatter] categories trees {trees}, scatter {scatter}, "
                + $"graves {graves}");
            return placed;
        }

        private static int CountAccepted(List<Candidate> candidates, float threshold)
        {
            int count = 0;
            foreach (Candidate candidate in candidates)
                if ((candidate.Mask > threshold && candidate.Acceptance < 1f)
                    || (candidate.Mask < 0.35f && candidate.MeadowRoll < 0.30f))
                    count++;
            return count;
        }

        private static bool ScatterAllowed(float x, float z, List<List<Vector3>> roads)
        {
            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 62f) return false;
            if (x < OpenWorld.MinX + 14f || x > OpenWorld.MaxX - 14f
                || z < OpenWorld.MinZ + 14f || z > OpenWorld.MaxZ - 14f) return false;
            foreach (CampaignSitePlan site in CampaignExpansionContent.Sites)
                if (Mathf.Max(Mathf.Abs(x - site.Center.x), Mathf.Abs(z - site.Center.z)) < 40f)
                    return false;
            return DistanceToRoads(x, z, roads) >= 9f;
        }

        private static float DistanceToRoads(float x, float z, List<List<Vector3>> roads)
        {
            Vector2 point = new Vector2(x, z);
            float best = float.PositiveInfinity;
            foreach (List<Vector3> road in roads)
            for (int i = 0; i < road.Count - 1; i++)
            {
                Vector2 a = new Vector2(road[i].x, road[i].z);
                Vector2 b = new Vector2(road[i + 1].x, road[i + 1].z);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 0f
                    ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(point, a + ab * t));
            }
            return best;
        }

        private static RegionFlavor FlavorAt(float x, float z)
        {
            if (new Vector2(x, z).magnitude <= 160f) return RegionFlavor.Lush;
            int bestRegion = 0;
            float bestDistance = float.PositiveInfinity;
            for (int region = 0; region < OpenWorld.Regions.Length; region++)
            foreach (string zoneId in OpenWorld.Regions[region])
            {
                CampaignSitePlan site = Site(zoneId);
                float distance = (new Vector2(x, z)
                    - new Vector2(site.Center.x, site.Center.z)).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestRegion = region;
                }
            }

            if (bestRegion == 3) return RegionFlavor.Mirewatch;
            if (bestRegion == 5 || bestRegion == 6) return RegionFlavor.Pine;
            if (bestRegion == 4 || bestRegion == 7 || bestRegion == 8) return RegionFlavor.Burnt;
            return RegionFlavor.Lush;
        }

        private static GameObject PlaceTree(Candidate candidate, Vector3 position)
        {
            PolyPackArt.Kind kind = PolyPackArt.Kind.Tree;
            if (candidate.Flavor == RegionFlavor.Pine && candidate.Choice < 0.75f)
                kind = PolyPackArt.Kind.Pine;
            else if (candidate.Flavor == RegionFlavor.Mirewatch && candidate.Choice < 0.25f)
                kind = PolyPackArt.Kind.Pine;

            float size = Mathf.Lerp(3.6f, 6.4f, candidate.Scale);
            GameObject go = PolyPackArt.Place(kind, candidate.Pick, position,
                candidate.Yaw, size, byHeight: true);
            if (go == null && kind != PolyPackArt.Kind.Tree)
                go = PolyPackArt.Place(PolyPackArt.Kind.Tree, candidate.Pick,
                    position, candidate.Yaw, size, byHeight: true);
            if (go != null) return go;

            string[] fallback = kind == PolyPackArt.Kind.Pine ? FallbackPines : FallbackTrees;
            return KenneyArt.Place("Nature", fallback[candidate.Pick % fallback.Length],
                position, candidate.Yaw, size, byHeight: true);
        }

        private static GameObject PlaceScatter(Candidate candidate, Vector3 position,
            out bool grave)
        {
            PolyPackArt.Kind kind;
            if (candidate.NearNecropolis && candidate.KindRoll < 0.08f)
                kind = PolyPackArt.Kind.Grave;
            else if (candidate.NearNecropolis && candidate.KindRoll < 0.16f)
                kind = PolyPackArt.Kind.Bush;
            else if (candidate.Flavor == RegionFlavor.Pine)
                kind = Weighted(candidate.KindRoll, PolyPackArt.Kind.Rock, PolyPackArt.Kind.Rock,
                    PolyPackArt.Kind.Grass, PolyPackArt.Kind.Flower, PolyPackArt.Kind.Bush,
                    PolyPackArt.Kind.Mushroom, PolyPackArt.Kind.Log);
            else if (candidate.Flavor == RegionFlavor.Mirewatch)
                kind = Weighted(candidate.KindRoll, PolyPackArt.Kind.Bush, PolyPackArt.Kind.Bush,
                    PolyPackArt.Kind.Log, PolyPackArt.Kind.Log, PolyPackArt.Kind.Grass,
                    PolyPackArt.Kind.Mushroom, PolyPackArt.Kind.Rock);
            else if (candidate.Flavor == RegionFlavor.Burnt)
                kind = Weighted(candidate.KindRoll, PolyPackArt.Kind.Rock, PolyPackArt.Kind.Rock,
                    PolyPackArt.Kind.Cliff, PolyPackArt.Kind.Grass, PolyPackArt.Kind.Log);
            else
                kind = Weighted(candidate.KindRoll, PolyPackArt.Kind.Grass, PolyPackArt.Kind.Flower,
                    PolyPackArt.Kind.Bush, PolyPackArt.Kind.Rock, PolyPackArt.Kind.Mushroom,
                    PolyPackArt.Kind.Log);

            grave = kind == PolyPackArt.Kind.Grave;
            float size = Mathf.Lerp(0.8f, 2f, candidate.Scale);
            bool byHeight = kind != PolyPackArt.Kind.Cliff && kind != PolyPackArt.Kind.Rock;
            GameObject go = null;
            if (candidate.NearNecropolis
                && (kind == PolyPackArt.Kind.Grave || kind == PolyPackArt.Kind.Bush))
                go = PolyPackArt.Place(PolyPackArt.Source.GraveyardNature, kind,
                    candidate.Pick, position, candidate.Yaw, size, byHeight);
            if (go == null)
                go = PolyPackArt.Place(kind, candidate.Pick, position,
                    candidate.Yaw, size, byHeight);
            if (go == null && kind == PolyPackArt.Kind.Grave)
            {
                grave = false;
                go = PolyPackArt.Place(PolyPackArt.Kind.Bush, candidate.Pick,
                    position, candidate.Yaw, size, byHeight: true);
            }
            if (go != null) return go;

            return KenneyArt.Place("Nature", FallbackScatter[candidate.Pick % FallbackScatter.Length],
                position, candidate.Yaw, size, byHeight: true);
        }

        private static PolyPackArt.Kind Weighted(float roll, params PolyPackArt.Kind[] kinds)
        {
            int index = Mathf.Min(kinds.Length - 1, Mathf.FloorToInt(roll * kinds.Length));
            return kinds[index];
        }

        private static void BuildWorldRim(Transform world)
        {
            DestroyNamed("WorldRim");
            var root = new GameObject("WorldRim");
            root.transform.SetParent(world, false);
            MarkStatic(root);
            var random = new System.Random(Seed + 1);
            int pick = 100000;

            for (float x = OpenWorld.MinX + 16f; x <= OpenWorld.MaxX - 16f; x += 26f)
            {
                PlaceRim(root.transform, random, ref pick,
                    new Vector3(x + Jitter(random, 8f), 0f, OpenWorld.MinZ + 8f), 0f);
                PlaceRim(root.transform, random, ref pick,
                    new Vector3(x + Jitter(random, 8f), 0f, OpenWorld.MaxZ - 8f), 180f);
            }
            for (float z = OpenWorld.MinZ + 16f; z <= OpenWorld.MaxZ - 16f; z += 26f)
            {
                PlaceRim(root.transform, random, ref pick,
                    new Vector3(OpenWorld.MinX + 8f, 0f, z + Jitter(random, 8f)), 90f);
                PlaceRim(root.transform, random, ref pick,
                    new Vector3(OpenWorld.MaxX - 8f, 0f, z + Jitter(random, 8f)), 270f);
            }

            foreach (Vector3 corner in new[]
            {
                new Vector3(OpenWorld.MinX + 10f, 0f, OpenWorld.MinZ + 10f),
                new Vector3(OpenWorld.MinX + 10f, 0f, OpenWorld.MaxZ - 10f),
                new Vector3(OpenWorld.MaxX - 10f, 0f, OpenWorld.MinZ + 10f),
                new Vector3(OpenWorld.MaxX - 10f, 0f, OpenWorld.MaxZ - 10f)
            })
            {
                int extras = 3 + random.Next(2);
                for (int i = 0; i < extras; i++)
                    PlaceRim(root.transform, random, ref pick,
                        corner + new Vector3(Jitter(random, 10f), 0f, Jitter(random, 10f)),
                        (float)random.NextDouble() * 360f);
            }
        }

        private static void PlaceRim(Transform root, System.Random random, ref int pick,
            Vector3 position, float inwardYaw)
        {
            if (!Ground(position, out Vector3 grounded)) return;
            PolyPackArt.Kind kind = random.NextDouble() < 0.62
                ? PolyPackArt.Kind.Cliff : PolyPackArt.Kind.Rock;
            float size = Mathf.Lerp(2.2f, 3.4f, (float)random.NextDouble());
            float yaw = inwardYaw + Jitter(random, 28f);
            GameObject go = PolyPackArt.Place(kind, pick++, grounded, yaw, size, byHeight: false);
            if (go == null)
            {
                string fallback = kind == PolyPackArt.Kind.Cliff ? "cliff_block_rock" : "rock_largeA";
                go = KenneyArt.Place("Nature", fallback, grounded, yaw, size, byHeight: false);
            }
            if (go != null) FinishProp(go, root);
        }

        private static bool Ground(Vector3 position, out Vector3 grounded)
        {
            if (Physics.Raycast(new Vector3(position.x, 30f, position.z), Vector3.down,
                    out RaycastHit hit, 60f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                grounded = hit.point;
                return true;
            }
            grounded = position;
            return false;
        }

        private static void FinishProp(GameObject go, Transform parent)
        {
            go.transform.SetParent(parent, true);
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            MarkStatic(go);
        }

        private static void MarkStatic(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.isStatic = true;
        }

        private static float Jitter(System.Random random, float range)
        {
            return ((float)random.NextDouble() * 2f - 1f) * range;
        }

        private static bool NearSite(string zoneId, float x, float z, float distance)
        {
            CampaignSitePlan site = Site(zoneId);
            return Vector2.Distance(new Vector2(x, z),
                new Vector2(site.Center.x, site.Center.z)) <= distance;
        }

        private static CampaignSitePlan Site(string zoneId)
        {
            foreach (CampaignSitePlan site in CampaignExpansionContent.Sites)
                if (site.ZoneId == zoneId) return site;
            throw new InvalidOperationException($"site {zoneId} missing");
        }

        private static void DestroyNamed(string name)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
        }
    }
}
