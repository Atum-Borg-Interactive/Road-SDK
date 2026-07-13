using System.Collections.Generic;
using UnityEngine;
using RoadPro.Math;

namespace RoadPro.Generation
{
    public class IntersectionManager : MonoBehaviour
    {
        public static IntersectionManager Instance { get; private set; }

        public Dictionary<string, IntersectionData> Intersections = new Dictionary<string, IntersectionData>();

        private Dictionary<string, GameObject> intersectionMeshObjs = new Dictionary<string, GameObject>();
        private Dictionary<string, MeshFilter> intersectionMeshFilters = new Dictionary<string, MeshFilter>();

        private Dictionary<string, GameObject> walkMeshObjs = new Dictionary<string, GameObject>();
        private Dictionary<string, MeshFilter> walkMeshFilters = new Dictionary<string, MeshFilter>();

        [SerializeField] private Material intersectionMaterial;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var kvp in intersectionMeshObjs)
                if (kvp.Value != null) Destroy(kvp.Value);
            foreach (var kvp in walkMeshObjs)
                if (kvp.Value != null) Destroy(kvp.Value);
        }

        public string CreateIntersection(Vector3 position, float snapRadius = 8f)
        {
            foreach (var kvp in Intersections)
            {
                float dx = position.x - kvp.Value.Position.x;
                float dz = position.z - kvp.Value.Position.z;
                if (dx * dx + dz * dz < snapRadius * snapRadius)
                    return kvp.Key;
            }
            var id = System.Guid.NewGuid().ToString();
            Intersections[id] = new IntersectionData
            {
                Id = id,
                Position = position
            };
            return id;
        }

        public string FindIntersection(string id)
        {
            return Intersections.ContainsKey(id) ? id : null;
        }

        public void AddRoadToIntersection(string interId, string roadId)
        {
            if (!Intersections.TryGetValue(interId, out var inter)) return;
            if (inter.RoadIds.Contains(roadId)) return;
            inter.RoadIds.Add(roadId);
            SortRoadsByAngle(inter);
            Intersection.UpdateInterfaceRadius(inter, RoadRegistry.Instance);
        }

        public void RemoveRoadFromIntersection(string interId, string roadId)
        {
            if (!Intersections.TryGetValue(interId, out var inter)) return;
            inter.RoadIds.Remove(roadId);
        }

        private void SortRoadsByAngle(IntersectionData inter)
        {
            var registry = RoadRegistry.Instance;
            if (registry == null) return;
            inter.RoadIds.Sort((a, b) =>
            {
                var r1 = registry.GetById(a);
                var r2 = registry.GetById(b);
                var d1 = r1 != null ? Intersection.DirFrom(r1, inter.Id) : Vector2.right;
                var d2 = r2 != null ? Intersection.DirFrom(r2, inter.Id) : Vector2.right;
                return Intersection.CCWAngle(d1).CompareTo(Intersection.CCWAngle(d2));
            });
        }

        public void ReapplyAllInterfaces()
        {
            foreach (var kvp in Intersections)
                Intersection.UpdateInterfaceRadius(kvp.Value, RoadRegistry.Instance);
        }

        public void RebuildIntersectionMesh(string interId)
        {
            if (!Intersections.TryGetValue(interId, out var inter)) return;
            if (inter.RoadIds.Count < 2)
            {
                RemoveIntersectionMesh(interId);
                return;
            }

            var registry = RoadRegistry.Instance;
            if (registry == null) return;

            var mesh = IntersectionMeshBuilder.Build(inter, registry);
            if (mesh == null || mesh.vertexCount == 0)
            {
                RemoveIntersectionMesh(interId);
                return;
            }

            if (!intersectionMeshObjs.TryGetValue(interId, out var obj) || obj == null)
            {
                obj = new GameObject("Intersection " + interId);
                obj.transform.SetParent(transform);
                intersectionMeshObjs[interId] = obj;

                var mf = obj.AddComponent<MeshFilter>();
                intersectionMeshFilters[interId] = mf;

                var mr = obj.AddComponent<MeshRenderer>();
                Material mat = intersectionMaterial;
                if (mat == null && RoadRegistry.Instance != null)
                    mat = RoadRegistry.Instance.RoadMaterial;
                if (mat != null)
                    mr.sharedMaterial = mat;
            }

            if (intersectionMeshFilters.TryGetValue(interId, out var meshFilter))
                meshFilter.mesh = mesh;

            var walkMesh = IntersectionMeshBuilder.BuildWalkingCorners(inter, registry);
            if (walkMesh != null && walkMesh.vertexCount > 0)
            {
                if (!walkMeshObjs.TryGetValue(interId, out var wObj) || wObj == null)
                {
                    wObj = new GameObject("WalkCorners " + interId);
                    wObj.transform.SetParent(transform);
                    walkMeshObjs[interId] = wObj;

                    var wmf = wObj.AddComponent<MeshFilter>();
                    walkMeshFilters[interId] = wmf;

                    var wmr = wObj.AddComponent<MeshRenderer>();
                    Material mat = intersectionMaterial;
                    if (mat == null && RoadRegistry.Instance != null)
                        mat = RoadRegistry.Instance.RoadMaterial;
                    if (mat != null)
                        wmr.sharedMaterial = mat;
                }

                if (walkMeshFilters.TryGetValue(interId, out var wFilter))
                    wFilter.mesh = walkMesh;
            }
            else
            {
                RemoveWalkMesh(interId);
            }
        }

        public void RebuildAllIntersectionMeshes()
        {
            foreach (var kvp in Intersections)
                RebuildIntersectionMesh(kvp.Key);
        }

        public void RemoveIntersectionMesh(string interId)
        {
            if (intersectionMeshObjs.TryGetValue(interId, out var obj))
            {
                if (obj != null) Destroy(obj);
                intersectionMeshObjs.Remove(interId);
            }
            intersectionMeshFilters.Remove(interId);
            RemoveWalkMesh(interId);
        }

        public void RemoveIntersection(string interId)
        {
            RemoveIntersectionMesh(interId);
            Intersections.Remove(interId);
        }

        private void RemoveWalkMesh(string interId)
        {
            if (walkMeshObjs.TryGetValue(interId, out var wObj))
            {
                if (wObj != null) Destroy(wObj);
                walkMeshObjs.Remove(interId);
            }
            walkMeshFilters.Remove(interId);
        }
    }
}
