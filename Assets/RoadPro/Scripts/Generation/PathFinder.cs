using System.Collections.Generic;
using UnityEngine;

namespace RoadPro.Generation
{
    public static class PathFinder
    {
        public struct PathResult
        {
            public bool Found;
            public List<RoadSegment> Segments;
        }

        public struct RoadSegment
        {
            public RoadData Road;
            public bool Forward;
            public string FromIntersectionId;
            public string ToIntersectionId;
        }

        public static PathResult FindPath(string fromIntersectionId, string toIntersectionId)
        {
            if (RoadRegistry.Instance == null || IntersectionManager.Instance == null)
                return new PathResult { Found = false };

            if (fromIntersectionId == toIntersectionId)
                return new PathResult { Found = false };

            var intersections = IntersectionManager.Instance.Intersections;
            var registry = RoadRegistry.Instance;

            if (!intersections.ContainsKey(fromIntersectionId) || !intersections.ContainsKey(toIntersectionId))
                return new PathResult { Found = false };

            var cameFrom = new Dictionary<string, string>();
            var roadUsed = new Dictionary<string, RoadSegment>();
            var openSet = new Queue<string>();
            var visited = new HashSet<string>();

            openSet.Enqueue(fromIntersectionId);
            visited.Add(fromIntersectionId);

            while (openSet.Count > 0)
            {
                string current = openSet.Dequeue();

                if (current == toIntersectionId)
                {
                    var segments = ReconstructPath(cameFrom, roadUsed, current, fromIntersectionId);
                    return new PathResult { Found = true, Segments = segments };
                }

                if (!intersections.TryGetValue(current, out var inter)) continue;

                foreach (string roadId in inter.RoadIds)
                {
                    var road = registry.GetById(roadId);
                    if (road == null) continue;

                    string neighbor;
                    bool forward;

                    if (road.SrcIntersectionId == current)
                    {
                        neighbor = road.DstIntersectionId;
                        forward = true;
                    }
                    else if (road.DstIntersectionId == current)
                    {
                        neighbor = road.SrcIntersectionId;
                        forward = false;
                    }
                    else
                        continue;

                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);

                    cameFrom[neighbor] = current;
                    roadUsed[neighbor] = new RoadSegment
                    {
                        Road = road,
                        Forward = forward,
                        FromIntersectionId = current,
                        ToIntersectionId = neighbor
                    };
                    openSet.Enqueue(neighbor);
                }
            }

            return new PathResult { Found = false };
        }

        private static List<RoadSegment> ReconstructPath(
            Dictionary<string, string> cameFrom,
            Dictionary<string, RoadSegment> roadUsed,
            string current,
            string fromIntersectionId)
        {
            var segments = new List<RoadSegment>();
            while (current != fromIntersectionId && cameFrom.ContainsKey(current))
            {
                segments.Add(roadUsed[current]);
                current = cameFrom[current];
            }
            segments.Reverse();
            return segments;
        }
    }
}
