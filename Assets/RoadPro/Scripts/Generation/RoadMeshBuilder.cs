using System.Collections.Generic;
using UnityEngine;
using RoadPro.Math;

namespace RoadPro.Generation
{
    public static class RoadColors
    {
        public static readonly Color RoadLow   = new Color(0.216f, 0.216f, 0.216f, 1f);
        public static readonly Color RoadMid   = new Color(0.302f, 0.302f, 0.302f, 1f);
        public static readonly Color RoadHig   = new Color(0.42f,  0.42f,  0.42f,  1f);
        public static readonly Color RoadLine  = new Color(0.51f,  0.51f,  0.51f,  1f);
        public static readonly Color RoadPylon = new Color(0.49f,  0.49f,  0.49f,  1f);
    }

    public static class RoadMeshBuilder
    {
        public const float LANE_LINE_WIDTH    = 0.25f;
        public const float PYLON_SPACING      = 80.0f;
        public const float PYLON_VISIBLE_DIFF = 2.0f;
        public const float PYLON_DEPTH        = 20.0f;
        public const float PYLON_DROP         = 0.2f;
        public const float BELOW_STRIP_Z      = 0.3f;

        public static Mesh Build(RoadData road, LayerMask terrainMask, bool includePylons = true)
        {
            var tess = new Tessellator();
            var polyline = road.InterfacedPoints;

            if (polyline == null || polyline.Count < 2)
                return tess.ToMesh();

            Vector2 firstDir = polyline.FirstDir();
            Vector2 lastDir  = polyline.LastDir();
            var points = polyline.Points;

            tess.SetNormal(Vector3.down);
            tess.SetColor(RoadColors.RoadMid);
            var belowPoints = new List<Vector3>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                p.y -= BELOW_STRIP_Z;
                belowPoints.Add(p);
            }
            tess.DrawPolylineFull(belowPoints, firstDir, lastDir, road.Width, 0f);

            tess.SetNormal(Vector3.up);

            bool isFirstLane = true;
            foreach (var lane in road.Lanes)
            {
                if (lane.Kind.IsRail())
                {
                    float railOff = lane.DistFromBottom - road.Width * 0.5f + LaneKind.Rail.Width() * 0.5f;
                    tess.SetColor(RoadColors.RoadMid);
                    tess.DrawPolylineFull(points, firstDir, lastDir, LaneKind.Rail.Width(), railOff);
                    isFirstLane = true;
                    continue;
                }

                if (isFirstLane)
                {
                    float outerLineOff = lane.DistFromBottom - road.Width * 0.5f;
                    tess.SetColor(RoadColors.RoadLine);
                    tess.DrawPolylineFull(points, firstDir, lastDir, LANE_LINE_WIDTH, outerLineOff);
                    isFirstLane = false;
                }

                Color laneColor;
                switch (lane.Kind)
                {
                    case LaneKind.Walking: laneColor = RoadColors.RoadHig; break;
                    case LaneKind.Parking: laneColor = RoadColors.RoadLow; break;
                    case LaneKind.Median:  laneColor = new Color(0.25f, 0.55f, 0.2f, 1f); break;
                    default:               laneColor = RoadColors.RoadMid; break;
                }
                tess.SetColor(laneColor);
                float laneCenterOff = lane.DistFromBottom - road.Width * 0.5f + lane.Kind.Width() * 0.5f;
                tess.DrawPolylineFull(points, firstDir, lastDir, lane.Kind.Width() - LANE_LINE_WIDTH, laneCenterOff);

                float innerLineOff = lane.DistFromBottom - road.Width * 0.5f + lane.Kind.Width();
                tess.SetColor(RoadColors.RoadLine);
                tess.DrawPolylineFull(points, firstDir, lastDir, LANE_LINE_WIDTH, innerLineOff);
            }

            if (includePylons)
            {
                DrawPylons(tess, polyline, road.Width, terrainMask);
            }

            float emptyInter = Intersection.EmptyInterface(road.Width);
            if (Mathf.Abs(road.SrcInterface - emptyInter) < 0.1f && IsTrueDeadEnd(road.SrcIntersectionId))
                DrawEndCap(tess, points[0], -firstDir, road);
            if (Mathf.Abs(road.DstInterface - emptyInter) < 0.1f && IsTrueDeadEnd(road.DstIntersectionId))
                DrawEndCap(tess, points[points.Count - 1], lastDir, road);

            float hw = road.Width * 0.5f;
            {
                Vector3 srcEndpoint = points[0];
                Vector3 srcNor = new Vector3(firstDir.y, 0f, -firstDir.x);
                road.SrcCrossSection = new Vector3[]
                {
                    srcEndpoint - srcNor * hw,
                    srcEndpoint,
                    srcEndpoint + srcNor * hw
                };

                Vector3 dstEndpoint = points[points.Count - 1];
                Vector3 dstNor = new Vector3(lastDir.y, 0f, -lastDir.x);
                road.DstCrossSection = new Vector3[]
                {
                    dstEndpoint - dstNor * hw,
                    dstEndpoint,
                    dstEndpoint + dstNor * hw
                };
            }

            return tess.ToMesh();
        }

        private static void DrawEndCap(Tessellator tess, Vector3 center, Vector2 forward, RoadData road)
        {
            Color prevColor = tess.Color;
            Vector3 prevNormal = tess.Normal;
            float halfWidth = road.Width * 0.5f;
            int seg = 24;

            tess.SetNormal(Vector3.up);

            var slices = new List<(float inner, float outer, Color color, bool isLine)>();
            for (int li = 0; li < road.Lanes.Count; li++)
            {
                var lane = road.Lanes[li];
                float leftEdge = lane.DistFromBottom - halfWidth;
                float rightEdge = leftEdge + lane.Kind.Width();
                float absInner, absOuter;
                bool spansCenter = leftEdge < 0f && rightEdge > 0f;

                if (leftEdge >= 0f)
                {
                    absInner = leftEdge + LANE_LINE_WIDTH * 0.5f;
                    absOuter = rightEdge - LANE_LINE_WIDTH * 0.5f;
                }
                else if (rightEdge <= 0f)
                {
                    absInner = -rightEdge + LANE_LINE_WIDTH * 0.5f;
                    absOuter = -leftEdge - LANE_LINE_WIDTH * 0.5f;
                }
                else
                {
                    absInner = 0f;
                    absOuter = Mathf.Max(-leftEdge, rightEdge) - LANE_LINE_WIDTH * 0.5f;
                }

                if (absOuter > absInner + 0.001f)
                {
                    Color laneColor;
                    switch (lane.Kind)
                    {
                        case LaneKind.Walking: laneColor = RoadColors.RoadHig; break;
                        case LaneKind.Parking: laneColor = RoadColors.RoadLow; break;
                        case LaneKind.Median:  laneColor = new Color(0.25f, 0.55f, 0.2f, 1f); break;
                        case LaneKind.Rail:    laneColor = RoadColors.RoadMid; break;
                        default:               laneColor = RoadColors.RoadMid; break;
                    }
                    slices.Add((absInner, absOuter, laneColor, false));
                }

                if (!spansCenter && absOuter < halfWidth)
                {
                    float lineInner = absOuter;
                    float lineOuter = Mathf.Min(absOuter + LANE_LINE_WIDTH, halfWidth);
                    if (lineOuter > lineInner + 0.001f)
                        slices.Add((lineInner, lineOuter, RoadColors.RoadLine, true));
                }
            }

            slices.Sort((a, b) => a.inner.CompareTo(b.inner));

            float prevR = 0f;
            foreach (var (sliceInner, sliceOuter, color, _) in slices)
            {
                if (sliceInner > prevR + 0.001f)
                {
                    tess.SetColor(RoadColors.RoadLine);
                    tess.DrawSemicircleStrip(center, forward, prevR, sliceInner, seg);
                }

                float drawInner = Mathf.Max(sliceInner, prevR);
                if (sliceOuter > drawInner + 0.001f)
                {
                    tess.SetColor(color);
                    tess.DrawSemicircleStrip(center, forward, drawInner, sliceOuter, seg);
                }
                prevR = Mathf.Max(prevR, sliceOuter);
                if (prevR >= halfWidth) break;
            }

            if (prevR < halfWidth - 0.01f)
            {
                tess.SetColor(RoadColors.RoadLine);
                tess.DrawSemicircleStrip(center, forward, prevR, halfWidth, seg);
            }

            tess.SetColor(prevColor);
            tess.SetNormal(prevNormal);
        }

        private static bool IsTrueDeadEnd(string intersectionId)
        {
            if (string.IsNullOrEmpty(intersectionId)) return true;
            var im = IntersectionManager.Instance;
            if (im == null) return true;
            if (!im.Intersections.TryGetValue(intersectionId, out var inter)) return true;
            return inter.RoadIds.Count <= 1;
        }

        private static void DrawPylons(Tessellator tess, PolyLine3 polyline, float roadWidth, LayerMask terrainMask, float raycastHeight = 200f)
        {
            var samples = polyline.EquipointsDir(PYLON_SPACING, true);
            foreach (var (pos, dir) in samples)
            {
                float terrainH = Heightfinder.SampleTerrainHeight(pos, terrainMask, raycastHeight);
                if (float.IsNaN(terrainH)) continue;
                if (Mathf.Abs(terrainH - pos.y) <= PYLON_VISIBLE_DIFF) continue;

                DrawPylonBox(tess, pos, dir, terrainH, roadWidth);
            }
        }

        private static void DrawPylonBox(Tessellator tess, Vector3 pos, Vector2 dir, float terrainH, float roadWidth)
        {
            Color prevColor = tess.Color;
            Vector3 prevNormal = tess.Normal;
            tess.SetColor(RoadColors.RoadPylon);

            float w = roadWidth * 0.5f;
            float hUp   = pos.y - PYLON_DROP;
            float hDown = terrainH - PYLON_DEPTH;
            Vector3 d2  = new Vector3(dir.x, 0f, dir.y) * w;
            Vector3 d2p = new Vector3(-dir.y, 0f, dir.x) * w;

            Vector3 down = new Vector3(pos.x, hDown, pos.z);
            Vector3 up   = new Vector3(pos.x, hUp,   pos.z);

            Vector3 v0 = down + d2 + d2p;
            Vector3 v1 = down + d2 - d2p;
            Vector3 v2 = down - d2 - d2p;
            Vector3 v3 = down - d2 + d2p;
            Vector3 v4 = up   + d2 + d2p;
            Vector3 v5 = up   + d2 - d2p;
            Vector3 v6 = up   - d2 - d2p;
            Vector3 v7 = up   - d2 + d2p;

            Vector3 d2n  = d2.normalized;
            Vector3 d2pn = d2p.normalized;

            DrawPylonSide(tess, v0, v1, v5, v4,  d2n);
            DrawPylonSide(tess, v2, v3, v7, v6, -d2n);
            DrawPylonSide(tess, v3, v0, v4, v7,  d2pn);
            DrawPylonSide(tess, v1, v2, v6, v5, -d2pn);

            tess.SetColor(prevColor);
            tess.SetNormal(prevNormal);
        }

        private static void DrawPylonSide(Tessellator tess, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            Vector3 prevNormal = tess.Normal;
            tess.SetNormal(normal);
            int offset = tess.Vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                tess.Vertices.Add(i == 0 ? a : i == 1 ? b : i == 2 ? c : d);
                tess.Normals.Add(normal);
                tess.Colors.Add(tess.Color);
                tess.UVs.Add(Vector2.zero);
            }
            tess.Indices.Add(offset + 0); tess.Indices.Add(offset + 1); tess.Indices.Add(offset + 2);
            tess.Indices.Add(offset + 0); tess.Indices.Add(offset + 2); tess.Indices.Add(offset + 3);
            tess.SetNormal(prevNormal);
        }
    }
}
