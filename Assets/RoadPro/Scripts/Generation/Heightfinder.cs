using System.Collections.Generic;
using UnityEngine;
using RoadPro.Math;

namespace RoadPro.Generation
{
    public enum HeightError
    {
        OutsideOfMap,
        TooSteep
    }

    public class Spline1
    {
        public float From;
        public float To;
        public float FromDerivative;
        public float ToDerivative;

        public Spline1(float from, float to, float fromDerivative, float toDerivative)
        {
            From = from;
            To = to;
            FromDerivative = fromDerivative;
            ToDerivative = toDerivative;
        }

        public List<float> Points(int n)
        {
            var result = new List<float>(n);
            if (n <= 0) return result;
            if (n == 1) { result.Add(From); return result; }
            float p0 = From;
            float p1 = To;
            float m0 = FromDerivative;
            float m1 = ToDerivative;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1);
                float t2 = t * t;
                float t3 = t2 * t;
                float h00 = 2f * t3 - 3f * t2 + 1f;
                float h10 = t3 - 2f * t2 + t;
                float h01 = -2f * t3 + 3f * t2;
                float h11 = t3 - t2;
                result.Add(h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1);
            }
            return result;
        }
    }

    public static class Heightfinder
    {
        public const float ROAD_Z_OFFSET = 0.3f;

        public static (PolyLine3 points, HeightError? error) Run(
            PolyLine3 polyline,
            float startHeight,
            float endHeight,
            float maxSlope,
            LayerMask terrainMask,
            float raycastHeight = 200f
        )
        {
            if (polyline == null || polyline.Count == 0)
                return (new PolyLine3(), (HeightError?)HeightError.OutsideOfMap);

            if (polyline.Count == 1)
            {
                Vector3 p = polyline.First();
                p.y = startHeight + ROAD_Z_OFFSET;
                return (new PolyLine3(new List<Vector3> { p }), null);
            }

            float length = polyline.Length();
            int n = Mathf.Max(2, Mathf.FloorToInt(length) + 1);

            var contour = new float[n];
            var samplePoints = new List<Vector3>(n);
            bool heightError = false;

            contour[0] = startHeight;
            samplePoints.Add(polyline.First());

            for (int i = 1; i < n - 1; i++)
            {
                float t = (float)i;
                Vector3 pos = polyline.PointAlong(t);
                float h = SampleTerrainHeight(pos, terrainMask, raycastHeight);
                if (float.IsNaN(h)) heightError = true;
                contour[i] = h;
                samplePoints.Add(pos);
            }

            contour[n - 1] = endHeight;
            samplePoints.Add(polyline.Last());

            var airborn = new bool[n];
            float curHeight = contour[0];
            for (int i = 0; i < n; i++)
            {
                float diff = curHeight - contour[i];
                airborn[i] = diff > maxSlope;
                curHeight -= Mathf.Min(diff, maxSlope);
            }

            curHeight = contour[n - 1];
            for (int i = n - 1; i >= 0; i--)
            {
                float diff = curHeight - contour[i];
                airborn[i] |= diff > maxSlope;
                curHeight -= Mathf.Min(diff, maxSlope);
            }

            airborn[0] = false;
            airborn[n - 1] = false;

            var interfacePoints = new List<int>();
            for (int i = 1; i < n - 1; i++)
            {
                if (airborn[i] && !airborn[i - 1])
                    interfacePoints.Add(Mathf.Max(0, i - 3));
                if (airborn[i] && !airborn[i + 1])
                    interfacePoints.Add(Mathf.Min(n - 1, i + 3));
            }

            int ii = 0;
            while (ii + 3 < interfacePoints.Count)
            {
                if (interfacePoints[ii + 1] >= interfacePoints[ii + 2])
                {
                    interfacePoints.RemoveAt(ii + 2);
                    interfacePoints.RemoveAt(ii + 1);
                }
                else
                {
                    ii += 2;
                }
            }

            bool slopeTooSteep = false;

            for (int w = 0; w + 1 < interfacePoints.Count; w += 2)
            {
                int i1 = interfacePoints[w];
                int i2 = interfacePoints[w + 1];
                float h1 = contour[i1];
                float h2 = contour[i2];
                float deriv1 = h1 - (i1 > 0 ? contour[i1 - 1] : h1);
                float deriv2 = (i2 < n - 1 ? contour[i2 + 1] : h2) - h2;
                float d = Mathf.Abs(h2 - h1);
                float span = Mathf.Max(1, i2 - i1);
                float slope = d / span;
                if (slope > maxSlope) slopeTooSteep = true;

                var s = new Spline1(h1, h2, deriv1 * d, deriv2 * d);
                var pts = s.Points(i2 - i1 + 1);
                for (int j = 0; j < pts.Count; j++)
                {
                    contour[i1 + j] = pts[j];
                }
            }

            for (int i = 0; i < n; i++)
            {
                Vector3 p = samplePoints[i];
                p.y = contour[i] + ROAD_Z_OFFSET;
                samplePoints[i] = p;
            }

            var result = new PolyLine3(samplePoints);
            result.Simplify(1.0f, 1.0f, 100.0f);

            HeightError? err = null;
            if (heightError) err = HeightError.OutsideOfMap;
            else if (slopeTooSteep) err = HeightError.TooSteep;

            return (result, err);
        }

        public static float SampleTerrainHeight(Vector3 pos, LayerMask terrainMask, float raycastHeight = 200f)
        {
            Vector3 origin = pos + Vector3.up * raycastHeight;
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, raycastHeight * 2f, terrainMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }
            return float.NaN;
        }
    }
}
