using UnityEngine;
using RoadPro.Generation;

namespace RoadPro.Unity
{
    public class RoadRenderer : MonoBehaviour
    {
        [SerializeField] private Material roadMaterial;
        public RoadData RoadData;
        public LayerMask terrainLayerMask;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
            if (meshCollider == null) meshCollider = gameObject.AddComponent<MeshCollider>();
            if (roadMaterial != null)
            {
                meshRenderer.sharedMaterial = roadMaterial;
            }
        }

        public void Rebuild()
        {
            Rebuild(RoadData);
        }

        public void Rebuild(RoadData road)
        {
            RoadData = road;
            if (meshFilter == null) Awake();
            if (road == null) return;

            LayerMask mask = terrainLayerMask;
            if (mask.value == 0 || mask.value == -1)
                mask = LayerMask.GetMask("Default", "Ground", "Terrain");

            var mesh = RoadMeshBuilder.Build(road, mask);
            meshFilter.mesh = mesh;
            if (meshCollider != null) meshCollider.sharedMesh = mesh;
            if (roadMaterial != null) meshRenderer.sharedMaterial = roadMaterial;
        }

        public Material RoadMaterial
        {
            get => roadMaterial;
            set
            {
                roadMaterial = value;
                if (meshRenderer != null) meshRenderer.sharedMaterial = value;
            }
        }
    }
}
