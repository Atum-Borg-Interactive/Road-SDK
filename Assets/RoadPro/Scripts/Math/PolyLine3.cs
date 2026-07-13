using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadPro.Math
{
    public class PolyLine3
    {
        public List<Vector3> Points;

        public PolyLine3()
        {
            Points = new List<Vector3>();
        }

        public PolyLine3(List<Vector3> points)
        {
            Points = points ?? new List<Vector3>();
        }

        public PolyLine3(IEnumerable<Vector3> points)
        {
            Points = new List<Vector3>(points);
        }

        public int Count => Points.Count;

        public Vector3 First()
        {
            if (Points.Count == 0) return Vector3.zero;
            return Points[0];
        }

        public Vector3 Last()
        {
            if (Points.Count == 0) return Vector3.zero;
            return Points[Points.Count - 1];
        }

        public float Length()
        {
            float l = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                l += Vector3.Distance(Points[i - 1], Points[i]);
            }
            return l;
        }

        public Vector2 FirstDir()
        {
            if (Points.Count < 2) return Vector2.right;
            float dx = Points[1].x - Points[0].x;
            float dz = Points[1].z - Points[0].z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-8f) return Vector2.right;
            return new Vector2(dx / len, dz / len);
        }

        public Vector2 LastDir()
        {
            if (Points.Count < 2) return -Vector2.right;
            float dx = Points[Points.Count - 1].x - Points[Points.Count - 2].x;
            float dz = Points[Points.Count - 1].z - Points[Points.Count - 2].z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-8f) return -Vector2.right;
            return new Vector2(dx / len, dz / len);
        }

        public Vector3 PointAlong(float t)
        {
            if (Points.Count == 0) return Vector3.zero;
            if (Points.Count == 1) return Points[0];
            float dist = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                Vector3 a = Points[i - 1];
                Vector3 b = Points[i];
                float seg = Vector3.Distance(a, b);
                if (dist + seg >= t)
                {
                    float u = seg < 1e-6f ? 0f : (t - dist) / seg;
                    return Vector3.Lerp(a, b, u);
                }
                dist += seg;
            }
            return Points[Points.Count - 1];
        }

        public (Vector3 pos, Vector2 dir) PointDirAlong(float t)
        {
            Vector3 pos = PointAlong(t);
            if (Points.Count < 2) return (pos, Vector2.right);
            float dist = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                Vector3 a = Points[i - 1];
                Vector3 b = Points[i];
                float dx = b.x - a.x;
                float dz = b.z - a.z;
                float segLen = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist + segLen >= t || i == Points.Count - 1)
                {
                    if (segLen < 1e-8f) return (pos, Vector2.right);
                    return (pos, new Vector2(dx / segLen, dz / segLen));
                }
                dist += segLen;
            }
            return (pos, Vector2.right);
        }

        public List<Vector3> PointsAlong(IEnumerable<float> distances)
        {
            var result = new List<Vector3>();
            foreach (var d in distances) result.Add(PointAlong(d));
            return result;
        }

        public List<(Vector3 pos, Vector2 dir)> PointsDirsAlong(IEnumerable<float> distances)
        {
            var result = new List<(Vector3, Vector2)>();
            foreach (var d in distances) result.Add(PointDirAlong(d));
            return result;
        }

        public List<(Vector3 pos, Vector2 dir)> EquipointsDir(float dx, bool includeStart)
        {
            var result = new List<(Vector3, Vector2)>();
            float l = Length();
            if (dx < 0.01f) throw new ArgumentException($"dx too small: {dx}");
            if (Points.Count < 2) return result;
            int n = Mathf.Max(1, Mathf.FloorToInt(l / dx));
            float adjDx = l / (n + 1);
            int start = includeStart ? 0 : 1;
            int end = n + 2;
            for (int i = start; i < end; i++)
            {
                float p = Mathf.Clamp(i * adjDx, 0f, l);
                result.Add(PointDirAlong(p));
            }
            return result;
        }

        public PolyLine3 Cut(float fromStart, float fromEnd)
        {
            float l = Length();
            if (l < 0.001f) return new PolyLine3(new List<Vector3>(Points));
            if (Points.Count < 2) return new PolyLine3(new List<Vector3>(Points));
            float start = Mathf.Clamp(fromStart, 0f, l);
            float end = Mathf.Clamp(l - fromEnd, 0f, l);
            if (start >= end - 0.001f) return new PolyLine3();

            var result = new List<Vector3>();
            float dist = 0f;
            bool started = false;

            for (int i = 1; i < Points.Count; i++)
            {
                Vector3 a = Points[i - 1];
                Vector3 b = Points[i];
                float seg = Vector3.Distance(a, b);
                float segStart = dist;
                float segEnd = dist + seg;

                Vector3 AddIfNew(Vector3 p)
                {
                    if (!started || result.Count == 0 || Vector3.Distance(result[result.Count - 1], p) > 0.005f)
                    {
                        result.Add(p);
                    }
                    started = true;
                    return p;
                }

                if (segStart >= start && segEnd <= end)
                {
                    if (!started) AddIfNew(a);
                    AddIfNew(b);
                }
                else
                {
                    if (segStart < start && segEnd > start)
                    {
                        float u = seg < 1e-6f ? 0f : (start - segStart) / seg;
                        AddIfNew(Vector3.Lerp(a, b, u));
                    }
                    if (segStart < end && segEnd > end)
                    {
                        float u = seg < 1e-6f ? 0f : (end - segStart) / seg;
                        AddIfNew(Vector3.Lerp(a, b, u));
                    }
                }
                dist += seg;
            }
            return new PolyLine3(result);
        }

        public void Simplify(float angleDeg, float eps, float maxDist)
        {
            if (Points.Count < 3) return;
            float cosThreshold = Mathf.Cos(angleDeg * Mathf.Deg2Rad);
            var keep = new bool[Points.Count];
            keep[0] = true;
            keep[Points.Count - 1] = true;
            SimplifyRecursive(0, Points.Count - 1, keep, cosThreshold, eps);
            var newPoints = new List<Vector3>();
            for (int i = 0; i < Points.Count; i++)
            {
                if (keep[i]) newPoints.Add(Points[i]);
            }
            if (newPoints.Count >= 2) Points = newPoints;
        }

        private void SimplifyRecursive(int start, int end, bool[] keep, float cosThreshold, float eps)
        {
            if (end - start < 2) return;
            Vector3 a = Points[start];
            Vector3 b = Points[end];
            Vector3 ab = b - a;
            float abLen = ab.magnitude;
            if (abLen < 1e-4f) return;

            int maxIdx = -1;
            float maxDistSq = 0f;
            for (int i = start + 1; i < end; i++)
            {
                Vector3 p = Points[i];
                Vector3 ap = p - a;
                float t = Vector3.Dot(ap, ab) / (abLen * abLen);
                Vector3 proj = a + ab * Mathf.Clamp01(t);
                float distSq = (p - proj).sqrMagnitude;
                if (distSq > maxDistSq)
                {
                    maxDistSq = distSq;
                    maxIdx = i;
                }
            }
            if (maxIdx < 0) return;

            float maxDistActual = Mathf.Sqrt(maxDistSq);
            if (maxDistActual < eps) return;

            if (maxIdx > start && maxIdx < end - 1)
            {
                Vector3 prev = Points[maxIdx - 1];
                Vector3 curr = Points[maxIdx];
                Vector3 next = Points[maxIdx + 1];
                Vector3 d1 = (prev - curr);
                Vector3 d2 = (next - curr);
                if (d1.sqrMagnitude > 1e-8f && d2.sqrMagnitude > 1e-8f)
                {
                    d1.Normalize();
                    d2.Normalize();
                    float dot = Vector3.Dot(d1, d2);
                    if (dot < cosThreshold)
                    {
                        keep[maxIdx] = true;
                        SimplifyRecursive(start, maxIdx, keep, cosThreshold, eps);
                        SimplifyRecursive(maxIdx, end, keep, cosThreshold, eps);
                    }
                }
            }
        }

        public Vector3[] ToArray() => Points.ToArray();
    }
}
