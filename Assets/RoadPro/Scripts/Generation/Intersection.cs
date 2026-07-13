using System.Collections.Generic;
using UnityEngine;
using RoadPro.Math;

namespace RoadPro.Generation
{
    public static class Intersection
    {
        public const float MIN_INTERFACE = 9f;

        public static float EmptyInterface(float width)
        {
            return Mathf.Max(width * 0.8f, MIN_INTERFACE);
        }

        public static float PseudoAngle(Vector2 v)
        {
            float a = Mathf.Abs(v.x) + Mathf.Abs(v.y);
            if (a < 1e-6f) return 0f;
            float r = v.x / a;
            return v.y >= 0f ? r : 2f - r;
        }

        public static float CCWAngle(Vector2 v)
        {
            float a = Mathf.Atan2(v.y, v.x);
            return a < 0f ? a + Mathf.PI * 2f : a;
        }

        public static Vector2 DirFrom(RoadData road, string interId)
        {
            if (road.SrcIntersectionId == interId)
                return road.Points.FirstDir();
            else
                return -road.Points.LastDir();
        }

        private static void SetInterface(RoadData road, string interId, float value)
        {
            if (road.SrcIntersectionId == interId) road.SrcInterface = value;
            else road.DstInterface = value;
        }

        private static void MaxInterface(RoadData road, string interId, float value)
        {
            if (road.SrcIntersectionId == interId)
                road.SrcInterface = Mathf.Max(road.SrcInterface, value);
            else
                road.DstInterface = Mathf.Max(road.DstInterface, value);
        }

        private static float InterfaceFrom(RoadData road, string interId)
        {
            return road.SrcIntersectionId == interId ? road.SrcInterface : road.DstInterface;
        }

        public static float InterfaceCalcFormula(float w1, float w2, Vector2 dir1, Vector2 dir2)
        {
            float hw1 = w1 * 0.5f;
            float hw2 = w2 * 0.5f;
            float w = Mathf.Sqrt(hw1 * hw1 + hw2 * hw2);
            float d = Mathf.Clamp(Vector2.Dot(dir1, dir2), 0f, 1f);
            float sin = Mathf.Sqrt(1f - d * d);
            return Mathf.Min(w * 1.1f / Mathf.Max(sin, 0.01f), 50f);
        }

        public static float InterfaceCalcNumerically(RoadData r1, RoadData r2, string intersectionId)
        {
            float threshold = (r1.Width + r2.Width) * 0.8f;
            var pts1 = new List<Vector3>(r1.Points.Points);
            var pts2 = new List<Vector3>(r2.Points.Points);
            if (r1.SrcIntersectionId != intersectionId) pts1.Reverse();
            if (r2.SrcIntersectionId != intersectionId) pts2.Reverse();

            var im = IntersectionManager.Instance;
            if (im == null || !im.Intersections.TryGetValue(intersectionId, out var inter))
                return EmptyInterface(r1.Width);

            Vector2 interPos = new Vector2(inter.Position.x, inter.Position.z);
            int minLen = Mathf.Min(pts1.Count, pts2.Count);

            for (int i = 0; i < minLen; i++)
            {
                Vector2 p1 = new Vector2(pts1[i].x, pts1[i].z);
                Vector2 p2 = new Vector2(pts2[i].x, pts2[i].z);
                if (Vector2.Distance(p1, p2) > threshold)
                {
                    return (Vector2.Distance(interPos, p1) + Vector2.Distance(interPos, p2)) * 0.5f;
                }
            }
            return 50f;
        }

        private static bool RayIntersection(Vector2 o1, Vector2 d1, Vector2 o2, Vector2 d2, out float t1, out float t2)
        {
            return Geometry.Intersect.RayRay(o1, d1, o2, d2, out t1, out t2);
        }

        public static void UpdateInterfaceRadius(IntersectionData inter, RoadRegistry registry)
        {
            if (registry == null) return;

            switch (inter.RoadIds.Count)
            {
                case 0:
                    return;

                case 1:
                {
                    var road = registry.GetById(inter.RoadIds[0]);
                    if (road != null) SetInterface(road, inter.Id, EmptyInterface(road.Width));
                    return;
                }

                case 2:
                {
                    var r1 = registry.GetById(inter.RoadIds[0]);
                    var r2 = registry.GetById(inter.RoadIds[1]);
                    if (r1 == null || r2 == null) return;

                    Vector2 dir1 = DirFrom(r1, inter.Id);
                    Vector2 dir2 = DirFrom(r2, inter.Id);

                    float angleDeg = Vector2.Angle(dir1, dir2);

                    if (angleDeg < 15f)
                    {
                        float dist = Mathf.Max(
                            InterfaceCalcNumerically(r1, r2, inter.Id),
                            EmptyInterface(r1.Width));
                        SetInterface(r1, inter.Id, dist);
                        SetInterface(r2, inter.Id, dist);
                        return;
                    }

                    Vector2 elbow = (dir1 + dir2) * 0.5f;

                    if (elbow.sqrMagnitude < 1e-6f)
                    {
                        float dist = EmptyInterface(r1.Width);
                        SetInterface(r1, inter.Id, dist);
                        SetInterface(r2, inter.Id, dist);
                        return;
                    }

                    Vector2 perp1 = dir1.Perpendicular();
                    float sign1 = Mathf.Sign(Vector2.Dot(perp1, elbow));
                    Vector2 pos = new Vector2(inter.Position.x, inter.Position.z);
                    Vector2 rayOrigin1 = pos + perp1 * sign1 * r1.Width * 0.5f;

                    Vector2 perp2 = dir2.Perpendicular();
                    float sign2 = Mathf.Sign(Vector2.Dot(perp2, elbow));
                    Vector2 rayOrigin2 = pos + perp2 * sign2 * r2.Width * 0.5f;

                    if (RayIntersection(rayOrigin1, dir1, rayOrigin2, dir2, out float d1, out float d2))
                    {
                        SetInterface(r1, inter.Id, Mathf.Max(d1, EmptyInterface(r1.Width)));
                        SetInterface(r2, inter.Id, Mathf.Max(d2, EmptyInterface(r2.Width)));
                    }
                    else
                    {
                        SetInterface(r1, inter.Id, EmptyInterface(r1.Width));
                        SetInterface(r2, inter.Id, EmptyInterface(r2.Width));
                    }
                    return;
                }

                default:
                {
                    foreach (var rid in inter.RoadIds)
                    {
                        var road = registry.GetById(rid);
                        if (road != null) SetInterface(road, inter.Id, EmptyInterface(road.Width));
                    }

                    for (int i = 0; i < inter.RoadIds.Count; i++)
                    {
                        string r1id = inter.RoadIds[i];
                        string r2id = inter.RoadIds[(i + 1) % inter.RoadIds.Count];
                        var r1 = registry.GetById(r1id);
                        var r2 = registry.GetById(r2id);
                        if (r1 == null || r2 == null) continue;

                        Vector2 dir1 = DirFrom(r1, inter.Id);
                        Vector2 dir2 = DirFrom(r2, inter.Id);
                        float angleDeg = Vector2.SignedAngle(dir1, dir2);
                        float angleRad = Mathf.Abs(angleDeg * Mathf.Deg2Rad);

                        float minDist;
                        if (angleRad < 0.17453292f)
                            minDist = InterfaceCalcNumerically(r1, r2, inter.Id);
                        else
                            minDist = InterfaceCalcFormula(r1.Width, r2.Width, dir1, dir2);

                        MaxInterface(r1, inter.Id, minDist);
                        MaxInterface(r2, inter.Id, minDist);
                    }

                    float maxInter = 0f;
                    foreach (var rid in inter.RoadIds)
                    {
                        var road = registry.GetById(rid);
                        if (road != null)
                        {
                            float v = InterfaceFrom(road, inter.Id);
                            if (v > maxInter) maxInter = v;
                        }
                    }
                    inter.Radius = maxInter;
                    break;
                }
            }
        }
    }
}
