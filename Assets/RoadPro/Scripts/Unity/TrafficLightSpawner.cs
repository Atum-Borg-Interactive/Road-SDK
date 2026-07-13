using System.Collections.Generic;
using UnityEngine;
using RoadPro.Generation;

namespace RoadPro.Unity
{
    public class TrafficLightSpawner : MonoBehaviour
    {
        [Header("Terrain")]
        public LayerMask terrainLayerMask;

        [Header("Placement")]
        public float offsetFromEdge = 0.5f;
        public float offsetBackFromStop = 3f;

        [Header("Pole")]
        public float poleHeight = 4f;
        public float poleWidth = 0.15f;
        public float poleDepth = 0.15f;

        [Header("Signal Housing")]
        public float housingWidth = 0.6f;
        public float housingHeight = 1.4f;
        public float housingDepth = 0.35f;
        public float signalRadius = 0.15f;

        [Header("Materials")]
        public Material poleMaterial;
        public Material housingMaterial;
        public Material redLightMaterial;
        public Material yellowLightMaterial;
        public Material greenLightMaterial;

        private RoadBuilder builder;
        private readonly Dictionary<string, List<GameObject>> trafficLights = new Dictionary<string, List<GameObject>>();

        void OnEnable()
        {
            builder = GetComponent<RoadBuilder>();
            if (builder == null)
                builder = FindFirstObjectByType<RoadBuilder>();
            if (builder != null)
            {
                builder.OnRoadPlaced += OnRoadPlaced;
                builder.OnRoadRemoved += OnRoadRemoved;
            }

            if (terrainLayerMask.value == 0)
                terrainLayerMask = LayerMask.GetMask("Default", "Ground");

            if (poleMaterial == null)
                poleMaterial = ShaderCache.CreateLitNoReflection(new Color(0.3f, 0.3f, 0.3f, 1f));
            if (housingMaterial == null)
                housingMaterial = ShaderCache.CreateLitNoReflection(new Color(0.15f, 0.15f, 0.15f, 1f));
            if (redLightMaterial == null)
                redLightMaterial = ShaderCache.CreateLitNoReflection(new Color(1f, 0.1f, 0.1f, 1f));
            if (yellowLightMaterial == null)
                yellowLightMaterial = ShaderCache.CreateLitNoReflection(new Color(1f, 0.8f, 0.05f, 1f));
            if (greenLightMaterial == null)
                greenLightMaterial = ShaderCache.CreateLitNoReflection(new Color(0.1f, 0.9f, 0.1f, 1f));
        }

        void OnDisable()
        {
            if (builder != null)
            {
                builder.OnRoadPlaced -= OnRoadPlaced;
                builder.OnRoadRemoved -= OnRoadRemoved;
            }
        }

        void Start()
        {
            RebuildAll();
        }

        private void OnRoadPlaced(RoadData road)
        {
            UpdateIntersectionLights(road.SrcIntersectionId);
            UpdateIntersectionLights(road.DstIntersectionId);
        }

        private void OnRoadRemoved(string roadId)
        {
            var road = RoadRegistry.Instance?.GetById(roadId);
            if (road != null)
            {
                string src = road.SrcIntersectionId;
                string dst = road.DstIntersectionId;
                RemoveForIntersection(src);
                RemoveForIntersection(dst);
                UpdateIntersectionLights(src);
                UpdateIntersectionLights(dst);
            }
        }

        public void UpdateIntersectionLights(string interId)
        {
            if (string.IsNullOrEmpty(interId)) return;
            RemoveForIntersection(interId);

            if (IntersectionManager.Instance == null) return;
            if (!IntersectionManager.Instance.Intersections.TryGetValue(interId, out var inter))
                return;
            if (inter.RoadIds.Count < 3) return;

            var registry = RoadRegistry.Instance;
            if (registry == null) return;

            var spawned = new List<GameObject>();

            foreach (string roadId in inter.RoadIds)
            {
                var road = registry.GetById(roadId);
                if (road == null) continue;

                var roadPoints = road.InterfacedPoints ?? road.Points;
                if (roadPoints == null || roadPoints.Count < 2) continue;

                bool isSrc = road.SrcIntersectionId == interId;
                Vector3 roadEnd;
                Vector3 roadDir;
                if (isSrc)
                {
                    roadEnd = roadPoints.Points[0];
                    roadDir = roadEnd - roadPoints.Points[1];
                }
                else
                {
                    roadEnd = roadPoints.Points[roadPoints.Count - 1];
                    roadDir = roadEnd - roadPoints.Points[roadPoints.Count - 2];
                }
                roadDir.y = 0f;
                float dirLen = roadDir.magnitude;
                if (dirLen < 0.001f) continue;
                roadDir /= dirLen;

                float halfW = road.Width * 0.5f;
                var cs = road.GetCrossSectionForNode(interId);

                Vector3 backwardDir = -roadDir;
                Vector3 rightPerp = -Vector3.Cross(roadDir, Vector3.up).normalized;

                Vector3 placePos;
                if (cs != null && cs.Length >= 3)
                {
                    Vector3 rightEdge = isSrc ? cs[0] : cs[2];
                    placePos = rightEdge + backwardDir * offsetBackFromStop;
                }
                else
                {
                    placePos = roadEnd + rightPerp * halfW + backwardDir * offsetBackFromStop;
                }

                float groundY = Heightfinder.SampleTerrainHeight(placePos, terrainLayerMask);
                if (!float.IsNaN(groundY))
                    placePos.y = groundY;

                var lightGO = BuildTrafficLight(placePos, backwardDir);
                spawned.Add(lightGO);
            }

            trafficLights[interId] = spawned;
        }

        public void RemoveForIntersection(string interId)
        {
            if (trafficLights.TryGetValue(interId, out var list))
            {
                foreach (var go in list)
                    if (go != null) Destroy(go);
                trafficLights.Remove(interId);
            }
        }

        public void ClearAll()
        {
            var keys = new List<string>(trafficLights.Keys);
            foreach (var k in keys)
                RemoveForIntersection(k);
        }

        public void RebuildAll()
        {
            ClearAll();
            if (IntersectionManager.Instance == null) return;
            foreach (var kvp in IntersectionManager.Instance.Intersections)
                UpdateIntersectionLights(kvp.Key);
        }

        private GameObject BuildTrafficLight(Vector3 basePos, Vector3 facing)
        {
            var go = new GameObject("TrafficLight");
            go.transform.SetParent(transform);
            go.transform.position = basePos;
            go.transform.forward = facing;

            var combineInstances = new List<CombineInstance>();

            combineInstances.Add(new CombineInstance
            {
                mesh = MakeBoxMesh(poleWidth, poleHeight, poleDepth),
                transform = Matrix4x4.Translate(new Vector3(0f, poleHeight * 0.5f, 0f))
            });

            float housingY = poleHeight + housingHeight * 0.5f;
            combineInstances.Add(new CombineInstance
            {
                mesh = MakeBoxMesh(housingWidth, housingHeight, housingDepth),
                transform = Matrix4x4.Translate(new Vector3(0f, housingY, 0f))
            });

            float faceZ = housingDepth * 0.5f + 0.01f;
            float ySpacing = housingHeight / 4f;
            float startY = poleHeight + housingHeight * 0.5f - ySpacing * 0.5f;

            Color[] signalColors = {
                new Color(1f, 0.1f, 0.1f),
                new Color(1f, 0.8f, 0.05f),
                new Color(0.1f, 0.9f, 0.1f)
            };

            for (int si = 0; si < 3; si++)
            {
                float sy = startY - si * ySpacing;
                int circSegs = 12;
                float r = signalRadius;

                var verts = new List<Vector3>();
                var tris = new List<int>();
                var norms = new List<Vector3>();
                var cols = new List<Color>();

                verts.Add(new Vector3(0f, sy, faceZ));
                norms.Add(Vector3.forward);
                cols.Add(signalColors[si]);

                for (int j = 0; j <= circSegs; j++)
                {
                    float a = (float)j / circSegs * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, sy + Mathf.Sin(a) * r, faceZ));
                    norms.Add(Vector3.forward);
                    cols.Add(signalColors[si]);
                }

                for (int j = 0; j < circSegs; j++)
                {
                    tris.Add(0);
                    tris.Add(j + 1);
                    tris.Add(j + 2);
                }

                var circleMesh = new Mesh();
                circleMesh.SetVertices(verts);
                circleMesh.SetNormals(norms);
                circleMesh.SetColors(cols);
                circleMesh.SetTriangles(tris.ToArray(), 0);

                combineInstances.Add(new CombineInstance
                {
                    mesh = circleMesh,
                    transform = Matrix4x4.Translate(new Vector3(0f, 0f, si * 0.001f))
                });
            }

            var combined = new Mesh();
            combined.CombineMeshes(combineInstances.ToArray(), false);
            combined.RecalculateNormals();
            combined.RecalculateBounds();

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = combined;

            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[combineInstances.Count];
            for (int i = 0; i < combineInstances.Count; i++)
            {
                if (i == 0)
                    mats[i] = poleMaterial;
                else if (i == 1)
                    mats[i] = housingMaterial;
                else if (i == 2)
                    mats[i] = redLightMaterial;
                else if (i == 3)
                    mats[i] = yellowLightMaterial;
                else
                    mats[i] = greenLightMaterial;
            }
            mr.materials = mats;

            return go;
        }

        private static Mesh MakeBoxMesh(float w, float h, float d)
        {
            float hw = w * 0.5f, hh = h * 0.5f, hd = d * 0.5f;

            var verts = new Vector3[]
            {
                new Vector3(-hw, -hh, -hd), new Vector3( hw, -hh, -hd), new Vector3( hw,  hh, -hd), new Vector3(-hw,  hh, -hd),
                new Vector3(-hw, -hh,  hd), new Vector3( hw, -hh,  hd), new Vector3( hw,  hh,  hd), new Vector3(-hw,  hh,  hd),
                new Vector3(-hw,  hh, -hd), new Vector3(-hw,  hh,  hd), new Vector3( hw,  hh,  hd), new Vector3( hw,  hh, -hd),
                new Vector3(-hw, -hh, -hd), new Vector3( hw, -hh, -hd), new Vector3( hw, -hh,  hd), new Vector3(-hw, -hh,  hd),
                new Vector3( hw, -hh, -hd), new Vector3( hw, -hh,  hd), new Vector3( hw,  hh,  hd), new Vector3( hw,  hh, -hd),
                new Vector3(-hw, -hh, -hd), new Vector3(-hw, -hh,  hd), new Vector3(-hw,  hh,  hd), new Vector3(-hw,  hh, -hd),
            };

            var norms = new Vector3[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
            };

            var tris = new int[]
            {
                0,2,1, 0,3,2,
                4,5,6, 4,6,7,
                8,10,9, 8,11,10,
                12,14,13, 12,15,14,
                16,18,17, 16,19,18,
                20,22,23, 20,21,22,
            };

            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            return mesh;
        }
    }
}
