using System.Collections.Generic;
using UnityEngine;
using RoadPro.Generation;
using RoadPro.Math;

namespace RoadPro.Geometry
{
    public readonly struct CrossingResult
    {
        public readonly Vector2 Point;
        public readonly string ExistingRoadId;
        public readonly float DistFromNewRoadStart;

        public CrossingResult(Vector2 point, string existingRoadId, float distFromNewRoadStart)
        {
            Point = point;
            ExistingRoadId = existingRoadId;
            DistFromNewRoadStart = distFromNewRoadStart;
        }
    }

    public static class CrossingDetect
    {
        public static List<CrossingResult> FindAll(
            Vector2 newStart, Vector2 newEnd,
            RoadRegistry registry,
            string excludeSrcInterId, string excludeDstInterId)
        {
            var results = new List<CrossingResult>();

            foreach (var kvp in registry.Roads)
            {
                var existing = kvp.Value;
                var pts = existing.Points.Points;
                if (pts.Count < 2) continue;

                if (SharesEndpoint(existing, excludeSrcInterId, excludeDstInterId))
                    continue;

                Vector2 exStart = new Vector2(pts[0].x, pts[0].z);
                Vector2 exEnd = new Vector2(pts[pts.Count - 1].x, pts[pts.Count - 1].z);

                if (!Intersect.SegmentSegment(newStart, newEnd, exStart, exEnd,
                    out Vector2 crossPt, out float t, out float _))
                    continue;

                float dist = Vector2.Distance(newStart, crossPt);
                results.Add(new CrossingResult(crossPt, kvp.Key, dist));
            }

            results.Sort((a, b) => a.DistFromNewRoadStart.CompareTo(b.DistFromNewRoadStart));
            return results;
        }

        private static bool SharesEndpoint(RoadData road, string srcInterId, string dstInterId)
        {
            if (string.IsNullOrEmpty(road.SrcIntersectionId) || string.IsNullOrEmpty(road.DstIntersectionId))
                return false;
            return road.SrcIntersectionId == srcInterId || road.SrcIntersectionId == dstInterId ||
                   road.DstIntersectionId == srcInterId || road.DstIntersectionId == dstInterId;
        }
    }
}
