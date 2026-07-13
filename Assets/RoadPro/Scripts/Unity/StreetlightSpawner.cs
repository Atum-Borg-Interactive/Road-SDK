using System.Collections.Generic;
using UnityEngine;
using RoadPro.Generation;

namespace RoadPro.Unity
{
    public class StreetlightSpawner : MonoBehaviour
    {
        [Header("Terrain")]
        public LayerMask terrainLayerMask;

        [Header("Placement")]
        public float spacing = 35f;
        public float offsetFromEdge = 2f;

        [Header("Materials")]
        public Material poleMaterial;
        public Material lampMaterial;

        private RoadBuilder builder;
        private readonly Dictionary<string, List<GameObject>> streetlights = new Dictionary<string, List<GameObject>>();
        private readonly HashSet<string> knownRoads = new HashSet<string>();

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
                poleMaterial = ShaderCache.CreateLitNoReflection(new Color(0.3f, 0.3f, 0.32f, 1f));
            if (lampMaterial == null)
                lampMaterial = ShaderCache.CreateLitNoReflection(new Color(1f, 0.92f, 0.7f, 1f));
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

        void LateUpdate()
        {
            var registry = RoadRegistry.Instance;
            if (registry == null) return;

            var allIds = new List<string>(registry.Roads.Keys);

            foreach (var rid in allIds)
            {
                if (!knownRoads.Contains(rid))
                {
                    var road = registry.GetById(rid);
                    if (road != null) SpawnForRoad(road);
                }
            }

            var removed = new List<string>();
            foreach (var rid in knownRoads)
            {
                if (!registry.Roads.ContainsKey(rid))
                    removed.Add(rid);
            }
            foreach (var rid in removed)
            {
                RemoveForRoad(rid);
                knownRoads.Remove(rid);
            }
        }

        private void OnRoadPlaced(RoadData road)
        {
            if (road == null) return;
            RefreshIntersectionRoads(road.SrcIntersectionId);
            RefreshIntersectionRoads(road.DstIntersectionId);
        }

        private void RefreshIntersectionRoads(string interId)
        {
            if (string.IsNullOrEmpty(interId)) return;
            if (IntersectionManager.Instance == null) return;
            if (!IntersectionManager.Instance.Intersections.TryGetValue(interId, out var inter))
                return;
            var registry = RoadRegistry.Instance;
            if (registry == null) return;
            foreach (var rid in inter.RoadIds)
            {
                var r = registry.GetById(rid);
                if (r != null) SpawnForRoad(r);
            }
        }

        private void OnRoadRemoved(string roadId)
        {
            RemoveForRoad(roadId);
            knownRoads.Remove(roadId);
        }

        public void SpawnForRoad(RoadData road)
        {
            RemoveForRoad(road.Id);
            var poly = road.InterfacedPoints ?? road.Points;
            if (poly == null || poly.Count < 2)
                return;

            float roadLen = poly.Length();
            float halfW = road.Width * 0.5f;

            var spawned = new List<GameObject>();

            float walkOffset = -1f;
            foreach (var lane in road.Lanes)
            {
                if (lane.Kind == LaneKind.Walking)
                {
                    walkOffset = halfW - (lane.DistFromBottom + LaneKind.Walking.Width() * 0.5f);
                    break;
                }
            }
            if (walkOffset < 0f)
                walkOffset = halfW + offsetFromEdge * 0.5f;

            float margin = spacing * 0.3f;
            float t = margin;
            while (t <= roadLen - margin)
            {
                var (pos, dir) = poly.PointDirAlong(t);
                Vector3 fwd = new Vector3(dir.x, 0f, dir.y);

                float groundY = Heightfinder.SampleTerrainHeight(pos, terrainLayerMask);
                if (float.IsNaN(groundY))
                    groundY = pos.y;

                Vector3 basePos = new Vector3(pos.x, groundY, pos.z);
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

                Vector3 leftSidePos = basePos - right * walkOffset;
                Vector3 rightSidePos = basePos + right * walkOffset;

                spawned.Add(BuildStreetlight(leftSidePos, right));
                spawned.Add(BuildStreetlight(rightSidePos, -right));
                t += spacing;
            }

            if (spawned.Count == 0)
            {
                float mid = roadLen * 0.5f;
                var (pos, dir) = poly.PointDirAlong(mid);
                Vector3 fwd = new Vector3(dir.x, 0f, dir.y);

                float groundY = Heightfinder.SampleTerrainHeight(pos, terrainLayerMask);
                if (float.IsNaN(groundY))
                    groundY = pos.y;

                Vector3 basePos = new Vector3(pos.x, groundY, pos.z);
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

                spawned.Add(BuildStreetlight(basePos - right * walkOffset, right));
                spawned.Add(BuildStreetlight(basePos + right * walkOffset, -right));
            }

            streetlights[road.Id] = spawned;
            knownRoads.Add(road.Id);
        }

        public void RemoveForRoad(string roadId)
        {
            if (streetlights.TryGetValue(roadId, out var list))
            {
                foreach (var go in list)
                    if (go != null) Destroy(go);
                streetlights.Remove(roadId);
            }
        }

        public void ClearAll()
        {
            var keys = new List<string>(streetlights.Keys);
            foreach (var k in keys)
                RemoveForRoad(k);
            knownRoads.Clear();
        }

        public void RebuildAll()
        {
            ClearAll();
            if (RoadRegistry.Instance == null) return;
            foreach (var road in RoadRegistry.Instance.AllRoads())
                SpawnForRoad(road);
        }

        private GameObject BuildStreetlight(Vector3 basePos, Vector3 armDir)
        {
            var go = new GameObject("Streetlight");
            go.transform.SetParent(transform);
            go.transform.position = basePos;

            var structInstances = new List<CombineInstance>();
            var panelInstances = new List<CombineInstance>();
            float poleH = 6.5f;

            structInstances.Add(CreateBoxMI(0.3f, 0.06f, 0.3f, new Vector3(0f, 0.03f, 0f)));

            structInstances.Add(CreateBoxMI(0.1f, poleH, 0.1f, new Vector3(0f, poleH * 0.5f, 0f)));

            float armLen = 1.6f;
            float armW = 0.04f;
            Vector3 armCenter = new Vector3(armDir.x * armLen * 0.5f, poleH, armDir.z * armLen * 0.5f);
            structInstances.Add(new CombineInstance
            {
                mesh = MakeBoxMesh(armW, armW, armLen),
                transform = Matrix4x4.TRS(armCenter, Quaternion.LookRotation(armDir), Vector3.one)
            });

            Vector3 armEnd = new Vector3(armDir.x * armLen, poleH, armDir.z * armLen);
            structInstances.Add(CreateBoxMI(0.04f, 0.25f, 0.04f,
                armEnd + new Vector3(0f, -0.125f, 0f)));

            float housingW = 0.4f;
            float housingH = 0.1f;
            float housingD = 0.18f;
            Vector3 housingCenter = armEnd + new Vector3(0f, -0.25f - housingH * 0.5f, 0f);
            structInstances.Add(CreateBoxMI(housingW, housingH, housingD, housingCenter));

            structInstances.Add(CreateBoxMI(housingW + 0.04f, 0.02f, housingD + 0.04f,
                housingCenter + new Vector3(0f, housingH * 0.5f, 0f)));

            float panelW = housingW - 0.06f;
            float panelD = housingD - 0.04f;
            panelInstances.Add(CreateBoxMI(panelW, 0.02f, panelD,
                housingCenter + new Vector3(0f, -housingH * 0.5f, 0f)));

            var combinedStruct = new Mesh();
            if (structInstances.Count > 0)
            {
                combinedStruct.CombineMeshes(structInstances.ToArray(), true);
                combinedStruct.RecalculateNormals();
                combinedStruct.RecalculateBounds();
            }

            var combinedPanel = new Mesh();
            if (panelInstances.Count > 0)
            {
                combinedPanel.CombineMeshes(panelInstances.ToArray(), true);
                combinedPanel.RecalculateNormals();
                combinedPanel.RecalculateBounds();
            }

            var mf = go.AddComponent<MeshFilter>();
            bool hasPanel = combinedPanel.vertexCount > 0;
            bool hasStruct = combinedStruct.vertexCount > 0;

            if (hasStruct && hasPanel)
            {
                var finalMesh = new Mesh();
                finalMesh.CombineMeshes(new CombineInstance[]
                {
                    new CombineInstance { mesh = combinedStruct, transform = Matrix4x4.identity },
                    new CombineInstance { mesh = combinedPanel, transform = Matrix4x4.identity },
                }, false);
                finalMesh.RecalculateNormals();
                finalMesh.RecalculateBounds();
                mf.mesh = finalMesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.materials = new Material[] { poleMaterial, lampMaterial };
            }
            else if (hasStruct)
            {
                mf.mesh = combinedStruct;
                var mr = go.AddComponent<MeshRenderer>();
                mr.material = poleMaterial;
            }

            return go;
        }

        private static CombineInstance CreateBoxMI(float w, float h, float d, Vector3 center)
        {
            return new CombineInstance
            {
                mesh = MakeBoxMesh(w, h, d),
                transform = Matrix4x4.Translate(center)
            };
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
