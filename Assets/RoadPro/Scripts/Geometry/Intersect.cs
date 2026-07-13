using UnityEngine;

namespace RoadPro.Geometry
{
    public static class Intersect
    {
        public static bool SegmentSegment(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point, out float t, out float u)
        {
            point = Vector2.zero;
            t = u = -1f;

            Vector2 ab = b - a;
            Vector2 cd = d - c;
            float denom = ab.x * cd.y - ab.y * cd.x;
            if (Mathf.Abs(denom) < 1e-10f) return false;

            Vector2 ac = c - a;
            t = (ac.x * cd.y - ac.y * cd.x) / denom;
            u = (ac.x * ab.y - ac.y * ab.x) / denom;

            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;

            point = a + ab * t;
            return true;
        }

        public static bool RayRay(Vector2 originA, Vector2 dirA, Vector2 originB, Vector2 dirB, out float tA, out float tB)
        {
            tA = tB = 0f;
            float det = dirA.x * dirB.y - dirA.y * dirB.x;
            if (Mathf.Abs(det) < 1e-10f) return false;

            Vector2 delta = originB - originA;
            tA = (delta.x * dirB.y - delta.y * dirB.x) / det;
            tB = (delta.x * dirA.y - delta.y * dirA.x) / det;
            return tA >= 0f && tB >= 0f;
        }
    }
}
