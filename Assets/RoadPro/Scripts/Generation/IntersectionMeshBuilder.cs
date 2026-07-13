using System;
using System.Collections.Generic;
using UnityEngine;
using RoadPro.Math;

namespace RoadPro.Generation
{
    public static class IntersectionMeshBuilder
    {
        private const float Z_OFFSET = Heightfinder.ROAD_Z_OFFSET;
        private const float WALK_STRIP_Y_OFFSET = 0.02f;
        private const int MIN_ROADS = 2;

        private const float TURN_ANG_ADD = 0.29f;
        private const float TURN_ANG_MUL = 0.36f;
        private const float TURN_MUL = 0.46f;
        private const int SPLINE_MAX_PTS = 16;

        private class ConnectedRoadMeta
        {
            public string RoadId;
            public float Angle;
            public RoadData Road;
            public Vector2 TowardInter;
            public Vector2 AwayInter;
        }

        public static Mesh Build(IntersectionData inter, RoadRegistry registry)
        {
            var tess = new Tessellator();
            if (!TryPrepare(inter, registry, out var connected, out float flatY))
                return tess.ToMesh();

            var boundary2D = BuildBoundaryPolygon(connected, inter.Id);
            if (boundary2D.Count < 3)
                return tess.ToMesh();

            tess.SetNormal(Vector3.up);
            tess.SetColor(RoadColors.RoadMid);
            tess.DrawFilledPolygonEarClip(boundary2D, flatY);
            return tess.ToMesh();
        }

        public static Mesh BuildWalkingCorners(IntersectionData inter, RoadRegistry registry)
        {
            var tess = new Tessellator();
            if (!TryPrepare(inter, registry, out var connected, out float flatY))
                return tess.ToMesh();

            float walkStripY = flatY + WALK_STRIP_Y_OFFSET;
            float walkW = LaneKind.Walking.Width();
            tess.SetNormal(Vector3.up);

            int n = connected.Count;
            float halfWalk = walkW * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var roadCur = connected[i];
                var roadNext = connected[(i + 1) % n];

                Vector2 outerCur = GetOuterEdge(roadCur.Road, inter.Id, true);
                Vector2 outerNext = GetOuterEdge(roadNext.Road, inter.Id, false);

                Vector2 centerCur = outerCur + Perp(roadCur.TowardInter) * halfWalk;
                Vector2 centerNext = outerNext + Perp(roadNext.AwayInter) * halfWalk;

                var centerSpline = GenerateSpline(centerCur, centerNext, roadCur.TowardInter, roadNext.AwayInter);
                DrawWalkStripCentered(tess, centerSpline, walkW, walkStripY);
            }
            return tess.ToMesh();
        }

        private static bool TryPrepare(IntersectionData inter, RoadRegistry registry,
            out List<ConnectedRoadMeta> connected, out float flatY)
        {
            connected = null;
            flatY = 0f;
            if (inter == null || registry == null)
                return false;
            connected = CollectConnectedRoads(inter, registry);
            if (connected.Count < MIN_ROADS)
                return false;
            flatY = ComputeFlatY(inter, connected);
            SortRoadsCCW(inter, connected);
            return true;
        }

        private static List<ConnectedRoadMeta> CollectConnectedRoads(IntersectionData inter, RoadRegistry registry)
        {
            var connected = new List<ConnectedRoadMeta>();
            foreach (string roadId in inter.RoadIds)
            {
                var road = registry.GetById(roadId);
                if (road == null) continue;
                connected.Add(new ConnectedRoadMeta { RoadId = roadId, Road = road });
            }
            return connected;
        }

        private static float ComputeFlatY(IntersectionData inter, List<ConnectedRoadMeta> connected)
        {
            return inter.Position.y + Z_OFFSET;
        }

        private static void SortRoadsCCW(IntersectionData inter, List<ConnectedRoadMeta> connected)
        {
            Vector2 interPos2 = new Vector2(inter.Position.x, inter.Position.z);
            foreach (var c in connected)
            {
                GetRoadOrientations(c.Road, inter.Id, out c.TowardInter, out c.AwayInter);
                Vector2 roadEnd = GetRoadEndpointAtNode(c.Road, inter.Id);
                Vector2 toRoad = roadEnd - interPos2;
                c.Angle = toRoad.sqrMagnitude < 0.001f ? 0f : CCWAngle(toRoad.normalized);
            }
            connected.Sort((a, b) => a.Angle.CompareTo(b.Angle));
        }

        private static List<Vector2> BuildBoundaryPolygon(List<ConnectedRoadMeta> connected, string interId)
        {
            var boundary2D = new List<Vector2>();
            int n = connected.Count;

            for (int i = 0; i < n; i++)
            {
                var roadCur = connected[i];
                var roadNext = connected[(i + 1) % n];

                Vector2 leftPt = GetDrivingEdge(roadCur.Road, interId, true);
                Vector2 rightPt = GetDrivingEdge(roadNext.Road, interId, false);

                var splinePoints = GenerateSpline(leftPt, rightPt, roadCur.TowardInter, roadNext.AwayInter);
                foreach (var p in splinePoints)
                    boundary2D.Add(p);
            }

            SimplifyPolygon(boundary2D);
            EnsureCCW(boundary2D);
            return boundary2D;
        }

        private static float GetDrivingHalfWidth(RoadData road)
        {
            bool hasWalking = false;
            foreach (var lane in road.Lanes)
            {
                if (lane.Kind == LaneKind.Walking)
                {
                    hasWalking = true;
                    break;
                }
            }
            return hasWalking
                ? road.Width * 0.5f - LaneKind.Walking.Width()
                : road.Width * 0.5f;
        }

        private static Vector2 GetDrivingEdge(RoadData road, string interId, bool isLeft)
        {
            Vector2 endpoint = GetRoadEndpointAtNode(road, interId);
            GetRoadOrientations(road, interId, out Vector2 towardInter, out Vector2 awayInter);
            Vector2 orient = isLeft ? towardInter : awayInter;
            Vector2 perp = -Perp(orient);
            return endpoint + perp * GetDrivingHalfWidth(road);
        }

        private static Vector2 GetOuterEdge(RoadData road, string interId, bool isLeft)
        {
            Vector3[] crossSection = road.GetCrossSectionForNode(interId);
            if (crossSection != null && crossSection.Length >= 3)
            {
                bool isSrc = road.SrcIntersectionId == interId;
                bool useLeft = isSrc ? isLeft : !isLeft;
                Vector3 edge = useLeft ? crossSection[0] : crossSection[2];
                return new Vector2(edge.x, edge.z);
            }

            Vector2 endpoint = GetRoadEndpointAtNode(road, interId);
            GetRoadOrientations(road, interId, out Vector2 towardInter, out Vector2 awayInter);
            Vector2 orient = isLeft ? towardInter : awayInter;
            return endpoint + -Perp(orient) * (road.Width * 0.5f);
        }

        private static Vector2 Perp(Vector2 v) => new Vector2(-v.y, v.x);

        private static void GetRoadOrientations(RoadData road, string interId,
            out Vector2 towardInter, out Vector2 awayInter)
        {
            bool isSrc = road.SrcIntersectionId == interId;
            var ip = road.InterfacedPoints;

            if (isSrc)
            {
                Vector2 firstDir = (ip != null && ip.Count >= 2)
                    ? ip.FirstDir()
                    : road.Points.FirstDir();
                awayInter = firstDir;
                towardInter = -firstDir;
            }
            else
            {
                Vector2 lastDir = (ip != null && ip.Count >= 2)
                    ? ip.LastDir()
                    : road.Points.LastDir();
                towardInter = lastDir;
                awayInter = -lastDir;
            }
        }

        private static Vector2 GetRoadEndpointAtNode(RoadData road, string interId)
        {
            var ip = road.InterfacedPoints;
            if (ip != null && ip.Count >= 2)
            {
                Vector3 ep = road.SrcIntersectionId == interId ? ip.Points[0] : ip.Points[ip.Count - 1];
                return new Vector2(ep.x, ep.z);
            }
            var pts = road.Points.Points;
            Vector3 fallback = road.SrcIntersectionId == interId ? pts[0] : pts[pts.Count - 1];
            return new Vector2(fallback.x, fallback.z);
        }

        private static List<Vector2> GenerateSpline(Vector2 from, Vector2 to, Vector2 fromDir, Vector2 toDir, bool isWalkStrip = false)
        {
            var points = new List<Vector2>();
            float dist = Vector2.Distance(from, to);
            if (dist < 0.5f)
            {
                points.Add(from);
                points.Add(to);
                return points;
            }

            float ang = Vector2.Angle(fromDir, toDir) * Mathf.Deg2Rad;

            float tangentLen;
            if (isWalkStrip)
            {
                tangentLen = Mathf.Min(dist * 0.552f, dist * 0.5f);
            }
            else
            {
                tangentLen = dist * (TURN_ANG_ADD + Mathf.Abs(ang) * TURN_ANG_MUL) * TURN_MUL;
                if (tangentLen < 0.5f) tangentLen = 0.5f;
            }

            Vector2 p0 = from;
            Vector2 p1 = from + fromDir * tangentLen;
            Vector2 p2 = to - toDir * tangentLen;
            Vector2 p3 = to;

            int segments = Mathf.Clamp(Mathf.CeilToInt(dist * 0.3f), 4, SPLINE_MAX_PTS);
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float u = 1f - t;
                points.Add(
                    u * u * u * p0
                    + 3f * u * u * t * p1
                    + 3f * u * t * t * p2
                    + t * t * t * p3);
            }
            return points;
        }

        private static void DrawWalkStripCentered(Tessellator tess, List<Vector2> spline, float walkW, float y)
        {
            if (spline == null || spline.Count < 2) return;

            float halfW = walkW * 0.5f;
            int n = spline.Count;

            var perps = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 tangent;
                if (i == 0)
                    tangent = spline[1] - spline[0];
                else if (i == n - 1)
                    tangent = spline[i] - spline[i - 1];
                else
                    tangent = spline[i + 1] - spline[i - 1];

                if (tangent.sqrMagnitude < 1e-8f)
                    tangent = Vector2.right;
                else
                    tangent.Normalize();

                perps[i] = new Vector2(-tangent.y, tangent.x);
            }

            for (int si = 0; si < n - 1; si++)
            {
                Vector2 o0 = spline[si];
                Vector2 o1 = spline[si + 1];

                Vector2 m0 = MiterOffset(perps[Mathf.Max(0, si - 1)], perps[si], halfW);
                Vector2 m1 = MiterOffset(perps[si], perps[Mathf.Min(n - 1, si + 1)], halfW);

                Vector3 inner0 = new Vector3(o0.x - m0.x, y, o0.y - m0.y);
                Vector3 inner1 = new Vector3(o1.x - m1.x, y, o1.y - m1.y);
                Vector3 outer0 = new Vector3(o0.x + m0.x, y, o0.y + m0.y);
                Vector3 outer1 = new Vector3(o1.x + m1.x, y, o1.y + m1.y);

                tess.SetColor(RoadColors.RoadHig);
                int baseIdx = tess.Vertices.Count;
                tess.Vertices.Add(inner0); tess.Normals.Add(Vector3.up); tess.Colors.Add(tess.Color); tess.UVs.Add(Vector2.zero);
                tess.Vertices.Add(inner1); tess.Normals.Add(Vector3.up); tess.Colors.Add(tess.Color); tess.UVs.Add(Vector2.zero);
                tess.Vertices.Add(outer1); tess.Normals.Add(Vector3.up); tess.Colors.Add(tess.Color); tess.UVs.Add(Vector2.zero);
                tess.Vertices.Add(outer0); tess.Normals.Add(Vector3.up); tess.Colors.Add(tess.Color); tess.UVs.Add(Vector2.zero);

                tess.Indices.Add(baseIdx);
                tess.Indices.Add(baseIdx + 1);
                tess.Indices.Add(baseIdx + 2);
                tess.Indices.Add(baseIdx);
                tess.Indices.Add(baseIdx + 2);
                tess.Indices.Add(baseIdx + 3);
            }
        }

        private static Vector2 MiterOffset(Vector2 perpA, Vector2 perpB, float width)
        {
            Vector2 a = perpA.sqrMagnitude > 1e-8f ? perpA.normalized : perpB.normalized;
            Vector2 b = perpB.sqrMagnitude > 1e-8f ? perpB.normalized : a;
            Vector2 miter = a + b;
            if (miter.sqrMagnitude < 1e-8f)
                return b * width;

            miter.Normalize();
            float dot = Vector2.Dot(a, miter);
            if (Mathf.Abs(dot) < 0.05f)
                return b * width;
            return miter * (width / dot);
        }

        private static void SimplifyPolygon(List<Vector2> poly, float eps = 0.05f)
        {
            if (poly.Count < 2) return;

            var result = new List<Vector2> { poly[0] };
            float eps2 = eps * eps;
            for (int i = 1; i < poly.Count; i++)
            {
                if ((poly[i] - result[result.Count - 1]).sqrMagnitude > eps2)
                    result.Add(poly[i]);
            }
            if (result.Count > 1 && (result[0] - result[result.Count - 1]).sqrMagnitude <= eps2)
                result.RemoveAt(result.Count - 1);

            poly.Clear();
            poly.AddRange(result);
        }

        private static void EnsureCCW(List<Vector2> poly)
        {
            if (SignedArea(poly) < 0f)
                poly.Reverse();
        }

        private static float SignedArea(List<Vector2> poly)
        {
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                int j = (i + 1) % poly.Count;
                area += poly[i].x * poly[j].y - poly[j].x * poly[i].y;
            }
            return area * 0.5f;
        }

        private static float CCWAngle(Vector2 v)
        {
            float a = Mathf.Atan2(v.y, v.x);
            return a < 0f ? a + Mathf.PI * 2f : a;
        }
    }
}
