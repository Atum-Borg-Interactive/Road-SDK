using System;
using System.Collections.Generic;
using UnityEngine;
using RoadPro.Math;

namespace RoadPro.Generation
{
    public enum LaneKind
    {
        Walking,
        Parking,
        Driving,
        Bus,
        Biking,
        Rail,
        Median
    }

    public static class LaneKindExtensions
    {
        public static float Width(this LaneKind k)
        {
            switch (k)
            {
                case LaneKind.Walking: return 3.0f;
                case LaneKind.Parking: return 2.5f;
                case LaneKind.Driving: return 4.0f;
                case LaneKind.Bus:     return 4.0f;
                case LaneKind.Biking:  return 4.0f;
                case LaneKind.Rail:    return 5.3f;
                case LaneKind.Median:  return 2.0f;
                default: return 4.0f;
            }
        }

        public static bool NeedsArrows(this LaneKind k)
        {
            return k == LaneKind.Driving || k == LaneKind.Bus || k == LaneKind.Biking || k == LaneKind.Rail;
        }

        public static bool IsRail(this LaneKind k) => k == LaneKind.Rail;
    }

    public enum LaneDirection
    {
        Forward,
        Backward
    }

    public class LaneData
    {
        public LaneKind Kind;
        public LaneDirection Direction;
        public float DistFromBottom;
    }

    public class LanePattern
    {
        public List<(LaneKind kind, LaneDirection dir)> Lanes = new List<(LaneKind, LaneDirection)>();

        public float Width()
        {
            float w = 0f;
            foreach (var (k, _) in Lanes) w += k.Width();
            return w;
        }

        public static LanePattern OneLaneStreet()
        {
            return new LanePattern
            {
                Lanes = new List<(LaneKind, LaneDirection)>
                {
                    (LaneKind.Walking, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Walking, LaneDirection.Forward)
                }
            };
        }

        public static LanePattern OneWayStreet()
        {
            return new LanePattern
            {
                Lanes = new List<(LaneKind, LaneDirection)>
                {
                    (LaneKind.Walking, LaneDirection.Forward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Walking, LaneDirection.Forward)
                }
            };
        }

        public static LanePattern TwoLaneStreet()
        {
            return new LanePattern
            {
                Lanes = new List<(LaneKind, LaneDirection)>
                {
                    (LaneKind.Walking, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Walking, LaneDirection.Forward)
                }
            };
        }

        public static LanePattern FourLaneStreet()
        {
            return new LanePattern
            {
                Lanes = new List<(LaneKind, LaneDirection)>
                {
                    (LaneKind.Walking, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Walking, LaneDirection.Forward)
                }
            };
        }

        public static LanePattern SixLaneStreet()
        {
            return new LanePattern
            {
                Lanes = new List<(LaneKind, LaneDirection)>
                {
                    (LaneKind.Walking, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Walking, LaneDirection.Forward)
                }
            };
        }

        public static LanePattern Highway()
        {
            return new LanePattern
            {
                Lanes = new List<(LaneKind, LaneDirection)>
                {
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Driving, LaneDirection.Backward),
                    (LaneKind.Median, LaneDirection.Forward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Driving, LaneDirection.Forward),
                    (LaneKind.Driving, LaneDirection.Forward)
                }
            };
        }
    }

    public class RoadData
    {
        public string Id;
        public string SrcIntersectionId;
        public string DstIntersectionId;
        public PolyLine3 Points;
        public PolyLine3 InterfacedPoints;
        public List<LaneData> Lanes = new List<LaneData>();
        public float SrcInterface = 9f;
        public float DstInterface = 9f;
        public float Width;
        public bool IsOneWay;

        public Vector3[] SrcCrossSection;
        public Vector3[] DstCrossSection;

        public Vector3[] GetCrossSectionForNode(string intersectionId)
        {
            if (SrcIntersectionId == intersectionId) return SrcCrossSection;
            if (DstIntersectionId == intersectionId) return DstCrossSection;
            return null;
        }
    }

    public static class RoadGenerator
    {
        public const float EMPTY_INTERFACE_MIN = 9.0f;
        public const float MAX_SLOPE = 0.25f;

        public static RoadData Make(
            string srcIntersectionId,
            string dstIntersectionId,
            PolyLine3 points2D,
            LanePattern pattern,
            LayerMask terrainMask)
        {
            var (interfacedPoints, hfError) = Heightfinder.Run(
                points2D, 0f, 0f, MAX_SLOPE, terrainMask);

            var lanes = new List<LaneData>();
            float distFromBottom = 0f;
            foreach (var (kind, dir) in pattern.Lanes)
            {
                lanes.Add(new LaneData
                {
                    Kind = kind,
                    Direction = dir,
                    DistFromBottom = distFromBottom
                });
                distFromBottom += kind.Width();
            }

            bool hasForward = false;
            bool hasBackward = false;
            foreach (var l in lanes)
            {
                if (l.Direction == LaneDirection.Forward) hasForward = true;
                else hasBackward = true;
            }
            bool isOneWay = !(hasForward && hasBackward);

            float width = distFromBottom;
            float emptyInterface = Mathf.Max(width * 0.8f, EMPTY_INTERFACE_MIN);

            return new RoadData
            {
                Id = Guid.NewGuid().ToString(),
                SrcIntersectionId = srcIntersectionId,
                DstIntersectionId = dstIntersectionId,
                Points = points2D,
                InterfacedPoints = interfacedPoints,
                Lanes = lanes,
                Width = width,
                IsOneWay = isOneWay,
                SrcInterface = emptyInterface,
                DstInterface = emptyInterface
            };
        }
    }
}
