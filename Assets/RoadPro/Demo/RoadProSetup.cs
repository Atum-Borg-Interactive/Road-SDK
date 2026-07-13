using UnityEngine;
using RoadPro.Generation;
using RoadPro.Unity;

namespace RoadPro.Demo
{
    [RequireComponent(typeof(RoadBuilder))]
    public class RoadProSetup : MonoBehaviour
    {
        [Header("Layer Settings")]
        public LayerMask groundLayer;
        public LayerMask roadLayer;

        [Header("Road Material")]
        public Material roadMaterial;

        [Header("Auto-Setup")]
        public bool createTerrainIfMissing = true;
        public bool addCameraController = true;
        public bool addStreetlights = true;
        public bool addTrafficLights = true;

        void Awake()
        {
            ShaderCache.WarmUp();

            if (roadMaterial == null)
            {
                roadMaterial = ShaderCache.CreateLitNoReflection(Color.white);
                roadMaterial.name = "RoadPro Road Material (Auto)";
            }

            SetupLayers();

            var builder = GetComponent<RoadBuilder>();
            builder.roadMaterial = roadMaterial;
            builder.SetGroundLayer(groundLayer);
            builder.SetRoadLayer(roadLayer);
            builder.SetLanePattern(LanePattern.TwoLaneStreet());

            if (createTerrainIfMissing)
                CreateSimpleTerrain();

            if (addCameraController)
                SetupCamera();

            if (addStreetlights)
                gameObject.AddComponent<StreetlightSpawner>();

            if (addTrafficLights)
                gameObject.AddComponent<TrafficLightSpawner>();
        }

        void Start()
        {
            Debug.Log("[RoadPro] Road generation tool initialized.");
            Debug.Log("[RoadPro] Controls:");
            Debug.Log("  [R] - Enter road placement mode");
            Debug.Log("  [X] - Enter bulldoze mode");
            Debug.Log("  [Esc] - Cancel placement");
            Debug.Log("  Click & drag - Place road connection");
            Debug.Log("  Right-click drag - Orbit camera");
            Debug.Log("  Scroll wheel - Zoom");
            Debug.Log("  WASD - Move camera");
        }

        private void SetupLayers()
        {
            int gLayer = LayerMask.NameToLayer("Ground");
            int rLayer = LayerMask.NameToLayer("Road");

            if (groundLayer.value == 0)
            {
                if (gLayer >= 0)
                    groundLayer = 1 << gLayer;
                else
                    groundLayer = LayerMask.GetMask("Default");
            }

            if (roadLayer.value == 0)
            {
                if (rLayer >= 0)
                    roadLayer = 1 << rLayer;
                else
                    roadLayer = LayerMask.GetMask("Default");
            }

            Debug.Log($"[RoadPro] Using layers - Ground: {groundLayer.value}, Road: {roadLayer.value}");
        }

        private void CreateSimpleTerrain()
        {
            if (FindFirstObjectByType<Terrain>() != null) return;

            var terrainGO = new GameObject("Terrain");
            var terrain = terrainGO.AddComponent<Terrain>();
            var terrainData = new TerrainData
            {
                heightmapResolution = 129,
                size = new Vector3(500, 50, 500)
            };

            terrain.terrainData = terrainData;
            terrainGO.AddComponent<TerrainCollider>().terrainData = terrainData;
            terrainGO.transform.position = new Vector3(-250f, 0f, -250f);

            int gLayer = LayerMask.NameToLayer("Ground");
            if (gLayer >= 0)
                terrainGO.layer = gLayer;

            Debug.Log("[RoadPro] Created flat terrain (500x500 at Y=0)");
        }

        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGO = new GameObject("Main Camera");
                cam = camGO.AddComponent<Camera>();
                cam.tag = "MainCamera";
                camGO.AddComponent<AudioListener>();
            }

            cam.transform.position = new Vector3(0, 60, -70);
            cam.transform.rotation = Quaternion.Euler(40, 0, 0);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;

            if (!cam.TryGetComponent<CameraController>(out _))
                cam.gameObject.AddComponent<CameraController>();
        }
    }

    public class CameraController : MonoBehaviour
    {
        public float moveSpeed = 80f;
        public float scrollSpeed = 40f;
        public float rotationSpeed = 100f;

        void Update()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            Vector3 move = (transform.right * h + transform.forward * v) * moveSpeed * Time.deltaTime;
            move.y = 0;
            transform.position += move;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            transform.position += transform.forward * scroll * scrollSpeed;

            if (Input.GetMouseButton(1))
            {
                float rotX = Input.GetAxis("Mouse X") * rotationSpeed * Time.deltaTime;
                float rotY = Input.GetAxis("Mouse Y") * rotationSpeed * Time.deltaTime;

                transform.Rotate(Vector3.up, rotX, Space.World);
                transform.Rotate(transform.right, -rotY, Space.World);

                Vector3 euler = transform.eulerAngles;
                euler.z = 0;
                transform.eulerAngles = euler;
            }
        }
    }
}
