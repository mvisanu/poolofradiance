using System;
using System.Collections.Generic;
using RadiantPool.Game;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    public static class OpenWorld
    {
        public const float MinX = -400f;
        public const float MaxX = 560f;
        public const float MinZ = -480f;
        public const float MaxZ = 410f;

        private const float ChunkSize = 120f;
        private const float VertexSpacing = 4f;
        private const int VerticesPerSide = 31;
        private const float FlatHeight = -0.04f;
        private const string MeshAssetPath = "Assets/Settings/OpenWorldMeshes.asset";

        public static readonly string[][] Regions =
        {
            new[] { "drowned_bastion", "cinderwell_yard", "cinderwell_undercroft", "ember_archive", "loomhouse_enclave", "blackbriar_manor", "gilded_quarter" },
            new[] { "emberwild_expanse", "wild_lairs", "reedwind_encampment", "goblin_delves" },
            new[] { "drowned_observatory_approach", "drowned_observatory_underworks", "drowned_observatory_crown" },
            new[] { "mirewatch_citadel", "tidebreaker_anchorage", "iron_concord_redoubt", "lanternfall_necropolis" },
            new[] { "cinder_gate", "crownless_citadel", "thornmaze", "ember_crown_spire" },
            new[] { "duskmire_crossing", "whispervault", "stormglass_foundry" },
            new[] { "frostvein_pass", "hoarfire_halls", "winter_crown_vault" },
            new[] { "shattered_coast", "colossus_road", "titan_foundry" },
            new[] { "veil_threshold", "hollow_star_depths", "dawnspire_nexus" }
        };

        public static void Build(Transform worldRoot, Material groundMat)
        {
            var existing = GameObject.Find("Wilderness");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var wilderness = new GameObject("Wilderness");
            if (worldRoot != null) wilderness.transform.SetParent(worldRoot, false);
            wilderness.isStatic = true;

            var roads = RoadPolylines();
            AssetDatabase.DeleteAsset(MeshAssetPath);
            bool firstMesh = true;
            int chunksX = Mathf.CeilToInt((MaxX - MinX) / ChunkSize);
            int chunksZ = Mathf.CeilToInt((MaxZ - MinZ) / ChunkSize);

            for (int cz = 0; cz < chunksZ; cz++)
            {
                for (int cx = 0; cx < chunksX; cx++)
                {
                    float startX = MinX + cx * ChunkSize;
                    float startZ = MinZ + cz * ChunkSize;
                    var chunk = new GameObject($"Wilderness_{cx}_{cz}");
                    chunk.transform.SetParent(wilderness.transform, false);
                    chunk.isStatic = true;

                    Mesh mesh = BuildChunkMesh(chunk.name, startX, startZ, roads);
                    if (firstMesh)
                    {
                        AssetDatabase.CreateAsset(mesh, MeshAssetPath);
                        firstMesh = false;
                    }
                    else
                    {
                        AssetDatabase.AddObjectToAsset(mesh, MeshAssetPath);
                    }

                    var filter = chunk.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = chunk.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = groundMat;
                    var collider = chunk.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                }
            }

            AddWorldEdge(wilderness.transform, "WorldEdge_N",
                new Vector3((MinX + MaxX) * 0.5f, 4f, MaxZ),
                new Vector3(MaxX - MinX, 8f, 2f));
            AddWorldEdge(wilderness.transform, "WorldEdge_S",
                new Vector3((MinX + MaxX) * 0.5f, 4f, MinZ),
                new Vector3(MaxX - MinX, 8f, 2f));
            AddWorldEdge(wilderness.transform, "WorldEdge_E",
                new Vector3(MaxX, 4f, (MinZ + MaxZ) * 0.5f),
                new Vector3(2f, 8f, MaxZ - MinZ));
            AddWorldEdge(wilderness.transform, "WorldEdge_W",
                new Vector3(MinX, 4f, (MinZ + MaxZ) * 0.5f),
                new Vector3(2f, 8f, MaxZ - MinZ));

            AssetDatabase.ImportAsset(MeshAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
        }

        public static List<List<Vector3>> RoadPolylines()
        {
            var centers = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (var site in CampaignExpansionContent.Sites)
                centers.Add(site.ZoneId, site.Center);

            var roads = new List<List<Vector3>>();
            foreach (string[] region in Regions)
            {
                var ordered = new List<string>(region);
                ordered.Sort(StringComparer.Ordinal);
                var visited = new HashSet<string>(StringComparer.Ordinal) { ordered[0] };

                while (visited.Count < ordered.Count)
                {
                    string bestFrom = null;
                    string bestTo = null;
                    float bestDistance = float.PositiveInfinity;
                    string bestKey = null;

                    foreach (string from in ordered)
                    {
                        if (!visited.Contains(from)) continue;
                        foreach (string to in ordered)
                        {
                            if (visited.Contains(to)) continue;
                            float distance = (centers[from] - centers[to]).sqrMagnitude;
                            string key = from + ":" + to;
                            if (distance < bestDistance - 0.0001f
                                || (Mathf.Abs(distance - bestDistance) <= 0.0001f
                                    && string.CompareOrdinal(key, bestKey) < 0))
                            {
                                bestDistance = distance;
                                bestFrom = from;
                                bestTo = to;
                                bestKey = key;
                            }
                        }
                    }

                    Vector3 fromEdge = centers[bestFrom] + new Vector3(0f, 0f, -24f);
                    Vector3 toEdge = centers[bestTo] + new Vector3(0f, 0f, -24f);
                    roads.Add(Wind(fromEdge, toEdge));
                    visited.Add(bestTo);
                }

                string entryId = ordered[0];
                float entryDistance = centers[entryId].sqrMagnitude;
                foreach (string id in ordered)
                {
                    float distance = centers[id].sqrMagnitude;
                    if (distance < entryDistance - 0.0001f
                        || (Mathf.Abs(distance - entryDistance) <= 0.0001f
                            && string.CompareOrdinal(id, entryId) < 0))
                    {
                        entryId = id;
                        entryDistance = distance;
                    }
                }

                Vector3 entry = centers[entryId];
                // Start town roads on the town SQUARE's boundary (Chebyshev 66, just past
                // the old wall line) — a Euclidean-58 start lands inside the built-up
                // district blocks and the validator flags their rooftops as blockers.
                float maxComponent = Mathf.Max(Mathf.Abs(entry.x), Mathf.Abs(entry.z));
                Vector3 townEdge = entry * (66f / maxComponent);
                Vector3 siteEdge = entry + new Vector3(0f, 0f, -24f);
                roads.Add(Wind(townEdge, siteEdge));
            }

            return roads;
        }

        public static void Validate()
        {
            var failures = new List<string>();
            var sites = CampaignExpansionContent.Sites;
            int siteFailures = 0;
            if (sites.Length != 34)
            {
                failures.Add($"sites expected 34 got {sites.Length}");
                siteFailures++;
            }

            for (int i = 0; i < sites.Length; i++)
            {
                if (sites[i].Center.magnitude < 150f)
                {
                    siteFailures++;
                    failures.Add($"site {sites[i].ZoneId} is {sites[i].Center.magnitude:F1} from origin");
                }

                for (int j = i + 1; j < sites.Length; j++)
                {
                    float distance = Vector3.Distance(sites[i].Center, sites[j].Center);
                    if (distance >= 50f) continue;
                    siteFailures++;
                    failures.Add($"sites {sites[i].ZoneId}/{sites[j].ZoneId} are {distance:F1} apart");
                }
            }

            Physics.SyncTransforms();
            int samples = 0;
            int blocked = 0;
            string firstRoadFailure = null;
            foreach (var road in RoadPolylines())
            {
                for (int segment = 0; segment < road.Count - 1; segment++)
                {
                    Vector3 a = road[segment];
                    Vector3 b = road[segment + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 4f));
                    for (int step = 0; step < steps; step++)
                        ValidateRoadSample(Vector3.Lerp(a, b, step / (float)steps),
                            ref samples, ref blocked, ref firstRoadFailure);
                }

                ValidateRoadSample(road[road.Count - 1],
                    ref samples, ref blocked, ref firstRoadFailure);
            }

            if (blocked > 0)
                failures.Add($"walkable samples {samples - blocked}/{samples}, first {firstRoadFailure}");

            if (failures.Count == 0)
                Debug.Log($"[OpenWorld] PASS walkable samples {samples}/{samples}, sites 34");
            else
                Debug.Log($"[OpenWorld] FAIL {string.Join("; ", failures)}");
        }

        private static Mesh BuildChunkMesh(string name, float startX, float startZ,
            List<List<Vector3>> roads)
        {
            var vertices = new Vector3[VerticesPerSide * VerticesPerSide];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(VerticesPerSide - 1) * (VerticesPerSide - 1) * 6];

            for (int z = 0; z < VerticesPerSide; z++)
            {
                for (int x = 0; x < VerticesPerSide; x++)
                {
                    float worldX = startX + x * VertexSpacing;
                    float worldZ = startZ + z * VertexSpacing;
                    int index = z * VerticesPerSide + x;
                    vertices[index] = new Vector3(worldX, HeightAt(worldX, worldZ, roads), worldZ);
                    uvs[index] = new Vector2(worldX / 8f, worldZ / 8f);
                }
            }

            int triangle = 0;
            for (int z = 0; z < VerticesPerSide - 1; z++)
            {
                for (int x = 0; x < VerticesPerSide - 1; x++)
                {
                    int index = z * VerticesPerSide + x;
                    triangles[triangle++] = index;
                    triangles[triangle++] = index + VerticesPerSide;
                    triangles[triangle++] = index + 1;
                    triangles[triangle++] = index + 1;
                    triangles[triangle++] = index + VerticesPerSide;
                    triangles[triangle++] = index + VerticesPerSide + 1;
                }
            }

            var mesh = new Mesh { name = name + "_Mesh" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float HeightAt(float x, float z, List<List<Vector3>> roads)
        {
            float height = Mathf.PerlinNoise(x * 0.013f + 7.3f, z * 0.013f + 2.1f) * 3.4f
                + Mathf.PerlinNoise(x * 0.045f + 11.7f, z * 0.045f + 5.9f) * 0.9f - 1.6f;
            float flatten = BlendWeight(Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)), 66f, 100f);

            foreach (var site in CampaignExpansionContent.Sites)
            {
                float chebyshev = Mathf.Max(Mathf.Abs(x - site.Center.x), Mathf.Abs(z - site.Center.z));
                flatten = Mathf.Max(flatten, BlendWeight(chebyshev, 34f, 55f));
            }

            flatten = Mathf.Max(flatten, BlendWeight(DistanceToRoads(x, z, roads), 7f, 15f));
            return Mathf.Lerp(height, FlatHeight, flatten);
        }

        private static float BlendWeight(float distance, float inner, float outer)
        {
            if (distance <= inner) return 1f;
            if (distance >= outer) return 0f;
            return 1f - Mathf.SmoothStep(0f, 1f, (distance - inner) / (outer - inner));
        }

        private static float DistanceToRoads(float x, float z, List<List<Vector3>> roads)
        {
            var point = new Vector2(x, z);
            float best = float.PositiveInfinity;
            foreach (var road in roads)
            {
                for (int i = 0; i < road.Count - 1; i++)
                {
                    var a = new Vector2(road[i].x, road[i].z);
                    var b = new Vector2(road[i + 1].x, road[i + 1].z);
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude > 0f
                        ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
                    best = Mathf.Min(best, Vector2.Distance(point, a + ab * t));
                }
            }

            return best;
        }

        private static List<Vector3> Wind(Vector3 start, Vector3 end)
        {
            float length = Vector3.Distance(start, end);
            int segments = Mathf.Max(1, Mathf.CeilToInt(length / 24f));
            var points = new List<Vector3>(segments + 1);
            Vector3 direction = end - start;
            Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x).normalized;
            float seed = Mathf.Abs(start.x * 0.017f + start.z * 0.031f
                + end.x * 0.043f + end.z * 0.059f);

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = Vector3.Lerp(start, end, t);
                float endpointDistance = Mathf.Min(length * t, length * (1f - t));
                float taper = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(endpointDistance / 30f));
                float offset = (Mathf.PerlinNoise(t * 1.7f + seed, 0.5f) - 0.5f) * 12f * taper;
                points.Add(point + perpendicular * offset);
            }

            return points;
        }

        private static void AddWorldEdge(Transform parent, string name, Vector3 position,
            Vector3 scale)
        {
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = name;
            edge.transform.SetParent(parent, false);
            edge.transform.position = position;
            edge.transform.localScale = scale;
            edge.GetComponent<Renderer>().enabled = false;
            edge.isStatic = true;
        }

        private static void ValidateRoadSample(Vector3 point, ref int samples, ref int blocked,
            ref string firstFailure)
        {
            samples++;
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(point.x, 30f, point.z), Vector3.down,
                    out hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                blocked++;
                if (firstFailure == null) firstFailure = $"miss at ({point.x:F1},{point.z:F1})";
                return;
            }

            if (hit.point.y < -1.5f || hit.point.y > 3f)
            {
                blocked++;
                if (firstFailure == null)
                    firstFailure = $"height {hit.point.y:F2} at ({point.x:F1},{point.z:F1})";
                return;
            }

            Vector3 clearanceCenter = hit.point + Vector3.up * 1.1f;
            if (!Physics.CheckSphere(clearanceCenter, 0.8f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;

            Collider[] overlaps = Physics.OverlapSphere(clearanceCenter, 0.8f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
            {
                if (overlap == hit.collider) continue;
                blocked++;
                if (firstFailure == null)
                    firstFailure = $"blocked by {overlap.name} at ({point.x:F1},{point.z:F1})";
                return;
            }
        }
    }
}
