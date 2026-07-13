using System.Collections.Generic;
using UnityEngine;

namespace RoadPro.Generation
{
    public class IntersectionData
    {
        public string Id;
        public Vector3 Position;
        public float Radius = 0f;
        public List<string> RoadIds = new List<string>();
    }
}
