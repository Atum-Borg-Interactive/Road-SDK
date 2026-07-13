using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadPro.Math
{
    public static class Vec2Extensions
    {
        public static Vector2 Perpendicular(this Vector2 v) => new Vector2(-v.y, v.x);
        public static float PerpDot(this Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public static Vector2 ToVector2XZ(this Vector3 v) => new Vector2(v.x, v.z);
    }

    public class Tessellator
    {
        public Color Color = Color.black;
        public Vector3 Normal = Vector3.up;
        public float Zoom = 1.0f;

        public List<Vector3> Vertices = new List<Vector3>();
        public List<Vector3> Normals = new List<Vector3>();
        public List<Color> Colors = new List<Color>();
        public List<Vector2> UVs = new List<Vector2>();
        public List<int> Indices = new List<int>();

        public void Clear()
        {
            Vertices.Clear();
            Normals.Clear();
            Colors.Clear();
            UVs.Clear();
            Indices.Clear();
        }

        public Mesh ToMesh()
        {
            var m = new Mesh();
            m.vertices = Vertices.ToArray();
            m.normals = Normals.ToArray();
            m.colors = Colors.ToArray();
            m.uv = UVs.ToArray();
            m.triangles = Indices.ToArray();
            m.RecalculateBounds();
            return m;
        }

        public void SetColor(Color c) { Color = c; }
        public void SetNormal(Vector3 n) { Normal = n; }

        private void PushVertex(Vector3 pos)
        {
            Vertices.Add(pos);
            Normals.Add(Normal);
            Colors.Add(Color);
            UVs.Add(Vector2.zero);
        }

        public bool DrawCircle(Vector3 p, float r)
        {
            if (r <= 0f) return false;
            int n = Mathf.Max(4, Mathf.FloorToInt(6.0f * Mathf.Pow(r * Zoom, 1.0f / 3.0f)));
            return DrawRegularPolygon(p, r, n, 0f);
        }

        public bool DrawRegularPolygon(Vector3 p, float r, int nPoints, float startAngle)
        {
            if (r <= 0f || nPoints < 3) return false;
            int offset = Vertices.Count;
            PushVertex(p);
            for (int i = 0; i < nPoints; i++)
            {
                float v = Mathf.PI * 2.0f * i / nPoints + startAngle;
                Vector3 trans = p + r * new Vector3(Mathf.Cos(v), 0, Mathf.Sin(v));
                PushVertex(trans);
            }
            for (int i = 0; i < nPoints; i++)
            {
                Indices.Add(offset + 0);
                Indices.Add(offset + i + 1);
                if (i == nPoints - 1) Indices.Add(offset + 1);
                else Indices.Add(offset + i + 2);
            }
            return true;
        }

        public bool DrawStrokeFull(Vector3 p1, Vector3 p2, Vector2 dir, float thickness)
        {
            if (thickness <= 0f) return true;
            Vector3 d3 = new Vector3(dir.x, 0, dir.y) * (thickness * 0.5f);
            int offset = Vertices.Count;
            PushVertex(p1 - d3);
            PushVertex(p1 + d3);
            PushVertex(p2 + d3);
            PushVertex(p2 - d3);
            bool nInv = Normal.y < 0f;
            if (nInv)
            {
                Indices.Add(offset + 2); Indices.Add(offset + 1); Indices.Add(offset + 0);
                Indices.Add(offset + 3); Indices.Add(offset + 2); Indices.Add(offset + 0);
            }
            else
            {
                Indices.Add(offset + 0); Indices.Add(offset + 1); Indices.Add(offset + 2);
                Indices.Add(offset + 0); Indices.Add(offset + 2); Indices.Add(offset + 3);
            }
            return true;
        }

        public bool DrawStroke(Vector3 p1, Vector3 p2, float thickness)
        {
            if (thickness <= 0f) return true;
            Vector2 dXZ = new Vector2(p2.x - p1.x, p2.z - p1.z);
            Vector2 perp;
            if (dXZ.sqrMagnitude < 1e-8f) perp = Vector2.right;
            else { dXZ.Normalize(); perp = dXZ.Perpendicular(); }
            return DrawStrokeFull(p1, p2, perp, thickness);
        }

        public bool DrawPolylineFull(IList<Vector3> points, Vector2 firstDir, Vector2 lastDir, float thickness, float offset)
        {
            int n = points.Count;
            if (n < 2 || thickness <= 0f) return true;
            if (n == 2)
            {
                Vector3 d = points[0] - points[1];
                Vector2 dXZ = new Vector2(d.x, d.z);
                Vector2 dir;
                if (dXZ.sqrMagnitude < 1e-8f) dir = Vector2.up;
                else dir = dXZ.normalized;
                dir = dir.Perpendicular();
                Vector3 p1off = points[0] + new Vector3(dir.x, 0, dir.y) * offset;
                Vector3 p2off = points[1] + new Vector3(dir.x, 0, dir.y) * offset;
                return DrawStrokeFull(p1off, p2off, firstDir.Perpendicular(), thickness);
            }

            float halfThick = thickness * 0.5f;
            int swap = Normal.y < 0f ? 2 : 0;
            int globalOffset = Vertices.Count;

            int index = 0;
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 a = points[i - 1];
                Vector3 elbow = points[i];
                Vector3 c = points[i + 1];

                if (index == 0)
                {
                    Vector2 perp = firstDir.Perpendicular();
                    Vector3 nor = new Vector3(-perp.x, 0, -perp.y);
                    PushVertex(a + nor * (offset + halfThick));
                    PushVertex(a + nor * (offset - halfThick));
                }

                Vector2 aeRaw = new Vector2(elbow.x - a.x, elbow.z - a.z);
                Vector2 ceRaw = new Vector2(elbow.x - c.x, elbow.z - c.z);
                if (aeRaw.sqrMagnitude < 1e-8f) continue;
                if (ceRaw.sqrMagnitude < 1e-8f) continue;
                Vector2 ae = aeRaw.normalized;
                Vector2 ce = ceRaw.normalized;

                Vector2 bisector = ae + ce;
                Vector2 dir;
                if (bisector.sqrMagnitude > 1e-8f)
                {
                    bisector.Normalize();
                    float d = ae.PerpDot(ce);
                    if (Mathf.Abs(d) < 0.01f) dir = -ae.Perpendicular();
                    else if (d < 0f) dir = -bisector;
                    else dir = bisector;
                }
                else
                {
                    dir = -ae.Perpendicular();
                }

                float sinTheta = ae.PerpDot(dir);
                if (sinTheta < 0.1f) sinTheta = 0.1f;
                float mul = 1.0f / sinTheta;

                Vector3 dir3 = new Vector3(dir.x, 0, dir.y);
                Vector3 p1v = elbow + mul * dir3 * (offset + halfThick);
                Vector3 p2v = elbow + mul * dir3 * (offset - halfThick);
                PushVertex(p1v);
                PushVertex(p2v);

                int v = globalOffset + index * 2;
                Indices.Add(v + swap);
                Indices.Add(v + 1);
                Indices.Add(v + 2 - swap);
                Indices.Add(v + 3 - swap);
                Indices.Add(v + 2);
                Indices.Add(v + 1 + swap);

                index++;
                if (index == n - 2)
                {
                    Vector2 perp2 = lastDir.Perpendicular();
                    Vector3 nor2 = new Vector3(-perp2.x, 0, -perp2.y);
                    Vector3 p1End = c + nor2 * (offset + halfThick);
                    Vector3 p2End = c + nor2 * (offset - halfThick);
                    PushVertex(p1End);
                    PushVertex(p2End);
                    int v2 = globalOffset + index * 2;
                    Indices.Add(v2 + swap);
                    Indices.Add(v2 + 1);
                    Indices.Add(v2 + 2 - swap);
                    Indices.Add(v2 + 3 - swap);
                    Indices.Add(v2 + 2);
                    Indices.Add(v2 + 1 + swap);
                }
            }
            return true;
        }

        public bool DrawPolyline(IList<Vector3> points, float thickness, bool loops)
        {
            int n = points.Count;
            if (n < 2 || thickness <= 0f) return true;
            if (n == 2 || (loops && n == 3)) return DrawStroke(points[0], points[1], thickness);
            Vector2 firstDir, lastDir;
            if (loops)
            {
                Vector3 elbow = points[0];
                Vector3 a = points[1];
                Vector3 c = points[n - 2];
                Vector2 ae = new Vector2(elbow.x - a.x, elbow.z - a.z).normalized;
                Vector2 ce = new Vector2(elbow.x - c.x, elbow.z - c.z).normalized;
                Vector2 sum = ae + ce;
                if (sum.sqrMagnitude < 1e-8f) firstDir = -ae.Perpendicular();
                else { sum.Normalize(); firstDir = -sum.Perpendicular(); }
                lastDir = firstDir;
            }
            else
            {
                float dx1 = points[1].x - points[0].x;
                float dz1 = points[1].z - points[0].z;
                float len1 = Mathf.Sqrt(dx1 * dx1 + dz1 * dz1);
                firstDir = len1 < 1e-8f ? Vector2.right : new Vector2(dx1 / len1, dz1 / len1);
                float dx2 = points[n - 1].x - points[n - 2].x;
                float dz2 = points[n - 1].z - points[n - 2].z;
                float len2 = Mathf.Sqrt(dx2 * dx2 + dz2 * dz2);
                lastDir = len2 < 1e-8f ? Vector2.right : new Vector2(dx2 / len2, dz2 / len2);
            }
            return DrawPolylineFull(points, firstDir, lastDir, thickness, 0f);
        }

        public bool DrawFilledPolygon(IList<Vector2> points, float z)
        {
            if (points.Count < 3) return false;
            int offset = Vertices.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 v = new Vector3(points[i].x, z, points[i].y);
                PushVertex(v);
            }
            for (int i = 1; i < points.Count - 1; i++)
            {
                Indices.Add(offset + 0);
                Indices.Add(offset + i);
                Indices.Add(offset + i + 1);
                Indices.Add(offset + 0);
                Indices.Add(offset + i + 1);
                Indices.Add(offset + i);
            }
            return true;
        }

        public bool DrawFilledPolygonEarClip(IList<Vector2> points, float z)
        {
            if (points.Count < 3) return false;

            int offset = Vertices.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 v = new Vector3(points[i].x, z, points[i].y);
                PushVertex(v);
            }

            var tris = EarClipTriangulate(points);
            if (tris == null || tris.Count == 0)
            {
                for (int i = 1; i < points.Count - 1; i++)
                {
                    Indices.Add(offset + 0);
                    Indices.Add(offset + i);
                    Indices.Add(offset + i + 1);
                    Indices.Add(offset + 0);
                    Indices.Add(offset + i + 1);
                    Indices.Add(offset + i);
                }
                return true;
            }

            foreach (int idx in tris)
            {
                Indices.Add(offset + idx);
            }
            return true;
        }

        private static List<int> EarClipTriangulate(IList<Vector2> poly)
        {
            int n = poly.Count;
            if (n < 3) return null;

            var indices = new List<int>(n);
            for (int i = 0; i < n; i++)
                indices.Add(i);

            var triangles = new List<int>();
            int guard = 0;
            int maxGuard = n * n;

            while (indices.Count > 3 && guard++ < maxGuard)
            {
                bool earFound = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int prev = indices[(i - 1 + indices.Count) % indices.Count];
                    int curr = indices[i];
                    int next = indices[(i + 1) % indices.Count];

                    if (!IsEar(poly, indices, prev, curr, next))
                        continue;

                    triangles.Add(prev);
                    triangles.Add(curr);
                    triangles.Add(next);
                    triangles.Add(next);
                    triangles.Add(curr);
                    triangles.Add(prev);
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
                if (!earFound)
                    return null;
            }

            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
                triangles.Add(indices[2]);
                triangles.Add(indices[1]);
                triangles.Add(indices[0]);
            }
            return triangles;
        }

        private static bool IsEar(IList<Vector2> poly, List<int> ring, int prev, int curr, int next)
        {
            Vector2 a = poly[prev];
            Vector2 b = poly[curr];
            Vector2 c = poly[next];

            if (Cross2D(b - a, c - b) <= 1e-6f)
                return false;

            for (int i = 0; i < ring.Count; i++)
            {
                int idx = ring[i];
                if (idx == prev || idx == curr || idx == next)
                    continue;
                if (PointStrictlyInTriangle(poly[idx], a, b, c))
                    return false;
            }
            return true;
        }

        private static bool PointStrictlyInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            const float eps = 1e-6f;
            float d1 = Cross2D(b - a, p - a);
            float d2 = Cross2D(c - b, p - b);
            float d3 = Cross2D(a - c, p - c);
            if (d1 <= eps || d2 <= eps || d3 <= eps)
                return false;
            return true;
        }

        private static float Cross2D(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        public void DrawSemicircleFan(Vector3 center, Vector2 forward, float radius, int segments = 16)
        {
            if (radius <= 0f || segments < 3) return;
            Vector2 perp = new Vector2(-forward.y, forward.x);
            int offset = Vertices.Count;
            PushVertex(center);
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, (float)i / segments);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                Vector3 p = center + new Vector3(
                    forward.x * cos + perp.x * sin,
                    0f,
                    forward.y * cos + perp.y * sin
                ) * radius;
                PushVertex(p);
            }
            for (int i = 0; i < segments; i++)
            {
                bool nInv = Normal.y < 0f;
                if (nInv)
                {
                    Indices.Add(offset + 0);
                    Indices.Add(offset + i + 2);
                    Indices.Add(offset + i + 1);
                }
                else
                {
                    Indices.Add(offset + 0);
                    Indices.Add(offset + i + 1);
                    Indices.Add(offset + i + 2);
                }
            }
        }

        public void DrawSemicircleStrip(Vector3 center, Vector2 forward, float innerR, float outerR, int segments = 16)
        {
            if (outerR <= 0f || segments < 3) return;
            if (innerR < 0f) innerR = 0f;
            Vector2 perp = new Vector2(-forward.y, forward.x);
            bool nInv = Normal.y < 0f;

            if (innerR < 0.001f)
            {
                DrawSemicircleFan(center, forward, outerR, segments);
                return;
            }

            int offset = Vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, (float)i / segments);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                Vector2 dir = new Vector2(
                    forward.x * cos + perp.x * sin,
                    forward.y * cos + perp.y * sin
                );
                PushVertex(center + new Vector3(dir.x, 0f, dir.y) * outerR);
            }
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, (float)i / segments);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                Vector2 dir = new Vector2(
                    forward.x * cos + perp.x * sin,
                    forward.y * cos + perp.y * sin
                );
                PushVertex(center + new Vector3(dir.x, 0f, dir.y) * innerR);
            }
            for (int i = 0; i < segments; i++)
            {
                int o = i;
                int i1 = i + 1;
                int o2 = (segments + 1) + i;
                int i2 = (segments + 1) + i + 1;
                if (nInv)
                {
                    Indices.Add(offset + o);
                    Indices.Add(offset + i2);
                    Indices.Add(offset + i1);
                    Indices.Add(offset + o);
                    Indices.Add(offset + o2);
                    Indices.Add(offset + i2);
                }
                else
                {
                    Indices.Add(offset + o);
                    Indices.Add(offset + i1);
                    Indices.Add(offset + i2);
                    Indices.Add(offset + o);
                    Indices.Add(offset + i2);
                    Indices.Add(offset + o2);
                }
            }
        }
    }
}
