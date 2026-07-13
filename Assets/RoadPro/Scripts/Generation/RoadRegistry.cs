using System;
using System.Collections.Generic;
using UnityEngine;
using RoadPro.Unity;

namespace RoadPro.Generation
{
    public class RoadRegistry : MonoBehaviour
    {
        public static RoadRegistry Instance { get; private set; }

        public Dictionary<string, RoadData> Roads = new Dictionary<string, RoadData>();
        public Dictionary<string, RoadRenderer> Renderers = new Dictionary<string, RoadRenderer>();

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
        }

        public void Register(RoadData road, RoadRenderer renderer)
        {
            Roads[road.Id] = road;
            if (renderer != null) Renderers[road.Id] = renderer;
        }

        public void Unregister(string id)
        {
            Roads.Remove(id);
            Renderers.Remove(id);
        }

        public RoadData GetById(string id)
        {
            return Roads.ContainsKey(id) ? Roads[id] : null;
        }

        public RoadRenderer GetRenderer(string id)
        {
            return Renderers.ContainsKey(id) ? Renderers[id] : null;
        }

        public IEnumerable<RoadData> AllRoads()
        {
            return Roads.Values;
        }

        public Material RoadMaterial;

        public (RoadData roadA, RoadData roadB) SplitRoad(
            string roadId, string newIntersectionId, Vector3 splitPoint, LayerMask terrainMask)
        {
            var original = GetById(roadId);
            if (original == null) return (null, null);

            var originalRenderer = GetRenderer(roadId);

            const float maxPerpDist = 5f;
            float bestPerpDist = float.MaxValue;
            int splitIndex = 1;
            Vector3 bestPoint = splitPoint;

            for (int i = 1; i < original.Points.Count; i++)
            {
                Vector3 a = original.Points.Points[i - 1];
                Vector3 b = original.Points.Points[i];
                Vector3 ab = b - a;
                float abLen2 = ab.sqrMagnitude;
                if (abLen2 < 1e-8f) continue;

                float t = Mathf.Clamp01(Vector3.Dot(splitPoint - a, ab) / abLen2);
                Vector3 proj = a + ab * t;
                Vector3 delta = splitPoint - proj;
                delta.y = 0f;
                float perpDist = delta.magnitude;
                if (perpDist < bestPerpDist)
                {
                    bestPerpDist = perpDist;
                    bestPoint = proj;
                    splitIndex = i;
                }
            }

            if (bestPerpDist > maxPerpDist)
                return (null, null);

            splitPoint = bestPoint;
            splitPoint.y = Heightfinder.SampleTerrainHeight(splitPoint, terrainMask);

            var ptsA = new List<Vector3>();
            for (int j = 0; j < splitIndex; j++)
                ptsA.Add(original.Points.Points[j]);
            if (ptsA.Count == 0 || Vector3.Distance(ptsA[ptsA.Count - 1], splitPoint) > 0.01f)
                ptsA.Add(splitPoint);

            var ptsB = new List<Vector3> { splitPoint };
            for (int j = splitIndex; j < original.Points.Count; j++)
                ptsB.Add(original.Points.Points[j]);
            if (ptsB.Count > 1 && Vector3.Distance(ptsB[0], ptsB[1]) < 0.01f)
                ptsB.RemoveAt(1);

            float startH = original.Points.Points[0].y;
            float endH = original.Points.Points[original.Points.Count - 1].y;
            float splitH = splitPoint.y;

            var polyA = new Math.PolyLine3(ptsA);
            var (interfacedA, _) = Heightfinder.Run(polyA, startH, splitH, 0.25f, terrainMask);

            var polyB = new Math.PolyLine3(ptsB);
            var (interfacedB, _) = Heightfinder.Run(polyB, splitH, endH, 0.25f, terrainMask);

            var roadA = new RoadData
            {
                Id = Guid.NewGuid().ToString(),
                SrcIntersectionId = original.SrcIntersectionId,
                DstIntersectionId = newIntersectionId,
                Points = polyA,
                InterfacedPoints = interfacedA,
                Width = original.Width,
                IsOneWay = original.IsOneWay,
                SrcInterface = original.SrcInterface,
                DstInterface = Mathf.Max(original.Width * 0.8f, Intersection.MIN_INTERFACE)
            };

            var roadB = new RoadData
            {
                Id = Guid.NewGuid().ToString(),
                SrcIntersectionId = newIntersectionId,
                DstIntersectionId = original.DstIntersectionId,
                Points = polyB,
                InterfacedPoints = interfacedB,
                Width = original.Width,
                IsOneWay = original.IsOneWay,
                SrcInterface = Mathf.Max(original.Width * 0.8f, Intersection.MIN_INTERFACE),
                DstInterface = original.DstInterface
            };

            foreach (var lane in original.Lanes)
            {
                roadA.Lanes.Add(new LaneData
                {
                    Kind = lane.Kind,
                    Direction = lane.Direction,
                    DistFromBottom = lane.DistFromBottom
                });
                roadB.Lanes.Add(new LaneData
                {
                    Kind = lane.Kind,
                    Direction = lane.Direction,
                    DistFromBottom = lane.DistFromBottom
                });
            }

            Unregister(roadId);
            if (originalRenderer != null)
            {
                Destroy(originalRenderer.gameObject);
            }

            var goA = new GameObject("Road " + roadA.Id);
            goA.transform.SetParent(transform);
            var rendererA = goA.AddComponent<RoadRenderer>();
            rendererA.RoadMaterial = RoadMaterial;
            Register(roadA, rendererA);

            var goB = new GameObject("Road " + roadB.Id);
            goB.transform.SetParent(transform);
            var rendererB = goB.AddComponent<RoadRenderer>();
            rendererB.RoadMaterial = RoadMaterial;
            Register(roadB, rendererB);

            IntersectionManager.Instance.RemoveRoadFromIntersection(original.SrcIntersectionId, roadId);
            IntersectionManager.Instance.RemoveRoadFromIntersection(original.DstIntersectionId, roadId);

            IntersectionManager.Instance.AddRoadToIntersection(original.SrcIntersectionId, roadA.Id);
            IntersectionManager.Instance.AddRoadToIntersection(newIntersectionId, roadA.Id);
            IntersectionManager.Instance.AddRoadToIntersection(newIntersectionId, roadB.Id);
            IntersectionManager.Instance.AddRoadToIntersection(original.DstIntersectionId, roadB.Id);

            IntersectionManager.Instance.ReapplyAllInterfaces();

            var affected = new HashSet<string> {
                original.SrcIntersectionId,
                newIntersectionId,
                original.DstIntersectionId
            };
            foreach (var interId in affected)
            {
                if (!IntersectionManager.Instance.Intersections.TryGetValue(interId, out var inter))
                    continue;
                foreach (var rid in inter.RoadIds)
                {
                    var r = GetById(rid);
                    var ren = GetRenderer(rid);
                    if (r == null || ren == null) continue;

                    float roadLen = r.Points.Length();
                    float safeSrc = Mathf.Min(r.SrcInterface, roadLen * 0.4f);
                    float safeDst = Mathf.Min(r.DstInterface, roadLen * 0.4f);
                    var trimmed = r.Points.Cut(safeSrc, safeDst);
                    if (trimmed.Count >= 2)
                    {
                        var im = IntersectionManager.Instance.Intersections;
                        float sH = 0f, eH = 0f;
                        if (im.TryGetValue(r.SrcIntersectionId, out var si)) sH = si.Position.y;
                        if (im.TryGetValue(r.DstIntersectionId, out var di)) eH = di.Position.y;
                        var (ip, _) = Heightfinder.Run(trimmed, sH, eH, 0.25f, terrainMask);
                        r.InterfacedPoints = ip;
                    }
                    ren.RoadData = r;
                    ren.Rebuild();
                }
            }
            IntersectionManager.Instance.RebuildAllIntersectionMeshes();

            return (roadA, roadB);
        }
    }
}
