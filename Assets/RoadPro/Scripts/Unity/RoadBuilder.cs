using System;
using System.Collections.Generic;
using UnityEngine;
using RoadPro.Math;
using RoadPro.Generation;
using RoadPro.Geometry;

namespace RoadPro.Unity
{
    public class RoadBuilder : MonoBehaviour
    {
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private LayerMask roadLayer;

        public Material roadMaterial;

        public enum PlaceState { Idle, Placing }
        public PlaceState CurrentState { get; private set; } = PlaceState.Idle;

        public enum ToolType { None, Road, Bulldoze }
        public ToolType ActiveTool { get; private set; } = ToolType.None;
        public bool RoadMode { get; private set; }
        public bool BulldozeMode { get; private set; }

        private const float SNAP_RADIUS = 8f;
        private const float ANGLE_STEP = 45f;
        private const int RING_SEGMENTS = 32;
        private const float MIN_ROAD_LENGTH = 5f;

        private string startIntersectionId;
        private Vector3 startHitPoint;

        private GameObject previewRoadObj;
        private MeshFilter previewMeshFilter;
        private MeshRenderer previewMeshRenderer;
        private Material previewMaterial;

        private GameObject cursorRingObj;
        private LineRenderer cursorRingLine;
        private GameObject cursorFillObj;
        private MeshFilter cursorFillMesh;
        private MeshRenderer cursorFillRenderer;
        private Material cursorFillMaterial;

        private LanePattern cachedPattern;
        private string snappedIntersectionId;
        private string hoveredRoadId;
        private Vector3 hoveredRoadSplitPoint;
        private bool mouseWasHeld;
        private float dragStartTime;

        private static readonly Color ColorDefault = new Color(0.29f, 0.40f, 1.0f, 0.8f);
        private static readonly Color ColorSnapped = new Color(0.2f, 0.9f, 0.3f, 0.9f);
        private static readonly Color ColorTJunction = new Color(1.0f, 0.6f, 0.1f, 0.9f);

        private const float NODE_SPACING = 10f;
        private const float NODE_RADIUS = 0.6f;

        private readonly Dictionary<string, List<GameObject>> roadNodes = new Dictionary<string, List<GameObject>>();
        private readonly Dictionary<string, Vector3[]> roadNodePositions = new Dictionary<string, Vector3[]>();
        private string hoveredNodeKey;
        private Material nodeMaterial;

        public event Action<RoadData> OnRoadPlaced;
        public event Action<string> OnRoadRemoved;

        public void SetGroundLayer(LayerMask layer) { groundLayer = layer; }
        public void SetRoadLayer(LayerMask layer) { roadLayer = layer; }

        void Awake()
        {
            ShaderCache.WarmUp();

            if (RoadRegistry.Instance == null)
                gameObject.AddComponent<RoadRegistry>();
            if (IntersectionManager.Instance == null)
                gameObject.AddComponent<IntersectionManager>();

            CreatePreviewRoad();
            CreateCursorRing();
            nodeMaterial = ShaderCache.CreateLitNoReflection(new Color(0.29f, 0.40f, 1.0f, 0.5f));
            nodeMaterial.name = "RoadNode Material";
        }

        void Start()
        {
            if (roadMaterial != null)
                RoadRegistry.Instance.RoadMaterial = roadMaterial;
            RebuildAllNodes();
        }

        void OnDestroy()
        {
            ClearAllNodes();
            if (previewRoadObj != null) Destroy(previewRoadObj);
            if (cursorRingObj != null) Destroy(cursorRingObj);
            if (cursorFillObj != null) Destroy(cursorFillObj);
            if (previewMaterial != null) Destroy(previewMaterial);
            if (nodeMaterial != null) Destroy(nodeMaterial);
        }

        void Update()
        {
            if (BulldozeMode)
            {
                UpdateBulldozeVisuals();
                if (Input.GetMouseButtonDown(0))
                    HandleBulldozeClick();
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    SetTool(ToolType.None);
                }
            }

            if (!RoadMode)
            {
                SetVisualsActive(false);
                return;
            }

            if (!ValidateLayers())
            {
                SetVisualsActive(false);
                return;
            }

            Ray mouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (cachedPattern == null)
            {
                SetVisualsActive(false);
                previewRoadObj.SetActive(false);
                return;
            }

            if (CurrentState == PlaceState.Idle)
            {
                FindSnapTarget(mouseRay);
                UpdateCursorRingVisual();
                UpdateCursorRingPosition(mouseRay);
                SetVisualsActive(true);
                previewRoadObj.SetActive(false);

                if (Input.GetMouseButtonDown(0))
                {
                    HandleIdleClick(mouseRay);
                }

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    SetTool(ToolType.None);
                }
            }
            else if (CurrentState == PlaceState.Placing)
            {
                FindSnapTarget(mouseRay);
                UpdateCursorRingVisual();
                UpdateCursorRingPosition(mouseRay);
                SetVisualsActive(true);

                Vector3 endPoint = GetSnappedEndPoint(mouseRay, out bool hasValidEndpoint);
                UpdatePreviewMesh(endPoint);
                previewRoadObj.SetActive(true);

                if (Input.GetMouseButtonUp(0))
                {
                    if (hasValidEndpoint)
                    {
                        string endInterId = GetOrCreateEndIntersection(endPoint);
                        ConfirmRoad(endInterId);
                    }
                    else
                    {
                        CancelPlacement();
                    }
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelPlacement();
                }
            }
        }

        private bool ValidateLayers()
        {
            if (groundLayer.value == 0)
            {
                groundLayer = LayerMask.GetMask("Default", "Ground");
            }
            if (roadLayer.value == 0)
            {
                roadLayer = LayerMask.GetMask("Default", "Road");
            }
            return groundLayer.value != 0;
        }

        private void ResolveLayers()
        {
            if (groundLayer.value == 0)
                groundLayer = LayerMask.GetMask("Default", "Ground");
            if (roadLayer.value == 0)
                roadLayer = LayerMask.GetMask("Default", "Road");
        }

        public void SetTool(ToolType tool)
        {
            if (ActiveTool == tool) return;

            bool wasBulldoze = BulldozeMode;
            ActiveTool = tool;

            RoadMode = tool == ToolType.Road;
            BulldozeMode = tool == ToolType.Bulldoze;

            if (tool == ToolType.Bulldoze)
            {
                CancelPlacement();
                previewRoadObj.SetActive(false);
                bulldozeHighlightProps = new MaterialPropertyBlock();
                ClearBulldozeHighlight();
            }
            else if (tool == ToolType.Road)
            {
                CancelPlacement();
                if (wasBulldoze) ClearBulldozeHighlight();
            }
            else
            {
                CancelPlacement();
                SetVisualsActive(false);
                previewRoadObj.SetActive(false);
                ClearBulldozeHighlight();
            }
        }

        public void ToggleRoadMode()
        {
            SetTool(ActiveTool == ToolType.Road ? ToolType.None : ToolType.Road);
        }

        public void SetRoadMode(bool active)
        {
            SetTool(active ? ToolType.Road : ToolType.None);
        }

        private void SetVisualsActive(bool active)
        {
            cursorRingObj.SetActive(active);
            cursorFillObj.SetActive(active);
        }

        private string hoveredBulldozeRoadId;
        private MaterialPropertyBlock bulldozeHighlightProps;

        public void SetBulldozeMode(bool active)
        {
            SetTool(active ? ToolType.Bulldoze : ToolType.None);
        }

        public void ToggleBulldozeMode()
        {
            SetTool(ActiveTool == ToolType.Bulldoze ? ToolType.None : ToolType.Bulldoze);
        }

        private void UpdateBulldozeVisuals()
        {
            cursorRingLine.startColor = Color.red;
            cursorRingLine.endColor = Color.red;
            cursorFillMaterial.SetColor("_BaseColor", new Color(1f, 0f, 0f, 0.2f));
            SetVisualsActive(true);
            Ray mouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            UpdateCursorRingPosition(mouseRay);

            if (TryFindRoadHit(mouseRay, out string roadId, out _))
                HighlightBulldozeRoad(roadId);
            else
                ClearBulldozeHighlight();
        }

        private void HighlightBulldozeRoad(string roadId)
        {
            if (hoveredBulldozeRoadId == roadId) return;
            ClearBulldozeHighlight();

            var roadRenderer = RoadRegistry.Instance.GetRenderer(roadId);
            if (roadRenderer != null)
            {
                var meshRenderer = roadRenderer.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.GetPropertyBlock(bulldozeHighlightProps);
                    bulldozeHighlightProps.SetColor("_BaseColor", Color.red);
                    meshRenderer.SetPropertyBlock(bulldozeHighlightProps);
                    hoveredBulldozeRoadId = roadId;
                }
            }
        }

        private void ClearBulldozeHighlight()
        {
            if (hoveredBulldozeRoadId != null)
            {
                var roadRenderer = RoadRegistry.Instance.GetRenderer(hoveredBulldozeRoadId);
                if (roadRenderer != null)
                {
                    var meshRenderer = roadRenderer.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        meshRenderer.GetPropertyBlock(bulldozeHighlightProps);
                        bulldozeHighlightProps.SetColor("_BaseColor", Color.white);
                        meshRenderer.SetPropertyBlock(bulldozeHighlightProps);
                    }
                }
                hoveredBulldozeRoadId = null;
            }
        }

        private void HandleBulldozeClick()
        {
            Ray mouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!TryFindRoadHit(mouseRay, out string roadId, out _)) return;

            var visited = new HashSet<string> { roadId };

            foreach (var rid in visited)
            {
                var rd = RoadRegistry.Instance.GetById(rid);
                if (rd == null) continue;

                IntersectionManager.Instance.RemoveRoadFromIntersection(rd.SrcIntersectionId, rid);
                IntersectionManager.Instance.RemoveRoadFromIntersection(rd.DstIntersectionId, rid);

                var ren = RoadRegistry.Instance.GetRenderer(rid);
                if (ren != null && ren.gameObject != null)
                    Destroy(ren.gameObject);

                RoadRegistry.Instance.Unregister(rid);
                OnRoadRemoved?.Invoke(rid);
                ClearNodesForRoad(rid);
            }

            var deadInters = new List<string>();
            foreach (var kvp in IntersectionManager.Instance.Intersections)
                if (kvp.Value.RoadIds.Count == 0)
                    deadInters.Add(kvp.Key);
            foreach (var id in deadInters)
            {
                IntersectionManager.Instance.RemoveIntersectionMesh(id);
                IntersectionManager.Instance.Intersections.Remove(id);
            }

            IntersectionManager.Instance.ReapplyAllInterfaces();

            var affectedInters = new HashSet<string>();
            var visitedRoads = new HashSet<string>(RoadRegistry.Instance.Roads.Keys);
            foreach (var rid in visitedRoads)
            {
                var r = RoadRegistry.Instance.GetById(rid);
                if (r == null) continue;
                affectedInters.Add(r.SrcIntersectionId);
                affectedInters.Add(r.DstIntersectionId);
            }
            RebuildAffectedRoads(affectedInters);
            IntersectionManager.Instance.RebuildAllIntersectionMeshes();
        }

        private void HandleIdleClick(Ray mouseRay)
        {
            if (cachedPattern == null) return;
            ResolveLayers();

            if (hoveredRoadId != null)
            {
                string origRoadId = hoveredRoadId;
                string newInterId = IntersectionManager.Instance.CreateIntersection(hoveredRoadSplitPoint, 0.1f);
                var splitInter = IntersectionManager.Instance.Intersections[newInterId];
                splitInter.Position = hoveredRoadSplitPoint;

                var (_, roadB) = RoadRegistry.Instance.SplitRoad(origRoadId, newInterId, hoveredRoadSplitPoint, groundLayer);
                if (roadB != null)
                {
                    ClearNodesForRoad(origRoadId);
                    startIntersectionId = newInterId;
                    startHitPoint = splitInter.Position;
                    CurrentState = PlaceState.Placing;

                    IntersectionManager.Instance.RebuildIntersectionMesh(newInterId);
                    IntersectionManager.Instance.RebuildIntersectionMesh(roadB.SrcIntersectionId);
                    IntersectionManager.Instance.RebuildIntersectionMesh(roadB.DstIntersectionId);
                }
                return;
            }

            if (TryFindRoadHit(mouseRay, out string roadId, out Vector3 splitPoint))
            {
                string origRoadId = roadId;
                string newInterId = IntersectionManager.Instance.CreateIntersection(splitPoint, SNAP_RADIUS);
                var (_, roadB) = RoadRegistry.Instance.SplitRoad(origRoadId, newInterId, splitPoint, groundLayer);
                if (roadB != null)
                {
                    ClearNodesForRoad(origRoadId);
                    if (IntersectionManager.Instance.Intersections.TryGetValue(newInterId, out var splitInter))
                    {
                        var ip = roadB.InterfacedPoints ?? roadB.Points;
                        if (ip != null && ip.Count > 0)
                            splitInter.Position = ip.Points[0];
                    }

                    startIntersectionId = newInterId;
                    startHitPoint = IntersectionManager.Instance.Intersections[newInterId].Position;
                    CurrentState = PlaceState.Placing;

                    IntersectionManager.Instance.RebuildIntersectionMesh(newInterId);
                    IntersectionManager.Instance.RebuildIntersectionMesh(roadB.SrcIntersectionId);
                    IntersectionManager.Instance.RebuildIntersectionMesh(roadB.DstIntersectionId);
                }
                return;
            }

            if (Physics.Raycast(mouseRay, out RaycastHit hit, 1000f, groundLayer))
            {
                Vector3 point = hit.point;
                if (snappedIntersectionId != null)
                {
                    var inter = IntersectionManager.Instance.Intersections[snappedIntersectionId];
                    point = inter.Position;
                    startIntersectionId = snappedIntersectionId;
                }
                else
                {
                    startIntersectionId = IntersectionManager.Instance.CreateIntersection(point);
                    Debug.Log($"[RoadPro] Created intersection at {point}");
                }
                startHitPoint = point;
                CurrentState = PlaceState.Placing;
            }
            else
            {
                Debug.LogWarning("[RoadPro] No ground hit - check groundLayer mask");
            }
        }

        private bool TryFindRoadHit(Ray mouseRay, out string roadId, out Vector3 splitPoint)
        {
            roadId = null;
            splitPoint = Vector3.zero;

            ResolveLayers();

            if (!Physics.Raycast(mouseRay, out RaycastHit hit, 1000f, roadLayer))
                return false;

            var renderer = hit.collider.GetComponent<RoadRenderer>();
            if (renderer == null || renderer.RoadData == null)
                return false;

            roadId = renderer.RoadData.Id;
            splitPoint = hit.point;
            return true;
        }

        private void FindSnapTarget(Ray mouseRay)
        {
            snappedIntersectionId = null;
            hoveredRoadId = null;
            hoveredRoadSplitPoint = Vector3.zero;

            ResolveLayers();

            Vector3 mouseWorld = mouseRay.origin + mouseRay.direction * 200f;
            if (Physics.Raycast(mouseRay, out RaycastHit groundHit, 1000f, groundLayer))
                mouseWorld = groundHit.point;

            float bestDist = SNAP_RADIUS;

            foreach (var kvp in IntersectionManager.Instance.Intersections)
            {
                float dx = mouseWorld.x - kvp.Value.Position.x;
                float dz = mouseWorld.z - kvp.Value.Position.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    snappedIntersectionId = kvp.Key;
                }
            }

            FindNearestNode(out string nodeRoadId, out int nodeIdx, out Vector3 nodePos, out float nodeDist);
            if (nodeRoadId != null && nodeDist < bestDist)
            {
                snappedIntersectionId = null;
                hoveredRoadId = nodeRoadId;
                hoveredRoadSplitPoint = nodePos;
                bestDist = nodeDist;
                UpdateNodeHighlight();
                return;
            }

            if (Physics.Raycast(mouseRay, out RaycastHit roadHit, 1000f, roadLayer))
            {
                var renderer = roadHit.collider.GetComponent<RoadRenderer>();
                if (renderer != null && renderer.RoadData != null)
                {
                    var road = renderer.RoadData;
                    var pts = road.InterfacedPoints ?? road.Points;
                    if (pts != null && pts.Count >= 2)
                    {
                        Vector3 nearest = NearestPointOnPolyLine(pts, roadHit.point);
                        float len = pts.Length();
                        float distAlong = DistanceAlongPolyLine(pts, nearest);
                        float minEndDist = road.Width * 0.5f;

                        if (distAlong > minEndDist && distAlong < len - minEndDist)
                        {
                            float dx = mouseWorld.x - nearest.x;
                            float dz = mouseWorld.z - nearest.z;
                            float dist = Mathf.Sqrt(dx * dx + dz * dz);

                            if (dist < bestDist)
                            {
                                snappedIntersectionId = null;
                                hoveredRoadId = road.Id;
                                hoveredRoadSplitPoint = nearest;
                            }
                        }
                    }
                }
            }

            UpdateNodeHighlight();
        }

        private Vector3 NearestPointOnPolyLine(PolyLine3 poly, Vector3 point)
        {
            float bestDist = float.MaxValue;
            Vector3 bestPoint = poly.First();

            for (int i = 1; i < poly.Count; i++)
            {
                Vector3 a = poly.Points[i - 1];
                Vector3 b = poly.Points[i];
                Vector3 ab = b - a;
                float segLenSq = ab.sqrMagnitude;
                if (segLenSq < 1e-8f) continue;

                float t = Vector3.Dot(point - a, ab) / segLenSq;
                t = Mathf.Clamp01(t);
                Vector3 proj = a + ab * t;

                float dist = Vector3.Distance(point, proj);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPoint = proj;
                }
            }

            return bestPoint;
        }

        private float DistanceAlongPolyLine(PolyLine3 poly, Vector3 point)
        {
            float dist = 0f;
            for (int i = 1; i < poly.Count; i++)
            {
                Vector3 a = poly.Points[i - 1];
                Vector3 b = poly.Points[i];
                Vector3 ab = b - a;
                float segLen = ab.magnitude;
                if (segLen < 1e-8f) continue;

                float t = Vector3.Dot(point - a, ab) / (segLen * segLen);
                if (t >= 0f && t <= 1f)
                    return dist + t * segLen;
                dist += segLen;
            }
            return dist;
        }

        private Vector3 GetSnappedEndPoint(Ray mouseRay, out bool valid)
        {
            valid = false;

            if (snappedIntersectionId != null)
            {
                valid = true;
                return IntersectionManager.Instance.Intersections[snappedIntersectionId].Position;
            }

            if (hoveredRoadId != null)
            {
                valid = true;
                return hoveredRoadSplitPoint;
            }

            if (Physics.Raycast(mouseRay, out RaycastHit hit, 1000f, groundLayer))
            {
                valid = true;
                Vector3 point = hit.point;
                point = SnapAngle(startHitPoint, point);
                return point;
            }

            return startHitPoint + Vector3.forward * 10f;
        }

        private string GetOrCreateEndIntersection(Vector3 endPoint)
        {
            if (snappedIntersectionId != null)
                return snappedIntersectionId;

            if (hoveredRoadId != null)
            {
                string origRoadId = hoveredRoadId;
                string interId = IntersectionManager.Instance.CreateIntersection(hoveredRoadSplitPoint, 0.1f);
                var inter = IntersectionManager.Instance.Intersections[interId];
                inter.Position = hoveredRoadSplitPoint;

                var (_, roadB) = RoadRegistry.Instance.SplitRoad(origRoadId, interId, hoveredRoadSplitPoint, groundLayer);
                if (roadB != null)
                {
                    ClearNodesForRoad(origRoadId);
                    IntersectionManager.Instance.RebuildIntersectionMesh(interId);
                    return interId;
                }
            }

            return IntersectionManager.Instance.CreateIntersection(endPoint);
        }

        private Vector3 SnapAngle(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            dir.y = 0f;
            float dist = dir.magnitude;
            if (dist < 0.1f) return to;

            float angle = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(angle / ANGLE_STEP) * ANGLE_STEP;
            float rad = snapped * Mathf.Deg2Rad;
            return from + new Vector3(Mathf.Cos(rad) * dist, 0f, Mathf.Sin(rad) * dist);
        }

        private void UpdateCursorRingVisual()
        {
            Color c;
            if (snappedIntersectionId != null)
                c = ColorSnapped;
            else if (hoveredRoadId != null)
                c = ColorTJunction;
            else
                c = ColorDefault;

            cursorRingLine.startColor = c;
            cursorRingLine.endColor = c;
            cursorFillMaterial.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1f));
            cursorFillMaterial.SetFloat("_Alpha", 0.15f);
        }

        private void UpdateCursorRingPosition(Ray mouseRay)
        {
            Vector3 center;
            if (snappedIntersectionId != null)
                center = IntersectionManager.Instance.Intersections[snappedIntersectionId].Position;
            else if (hoveredRoadId != null)
                center = hoveredRoadSplitPoint;
            else if (Physics.Raycast(mouseRay, out RaycastHit hit, 1000f, groundLayer))
                center = hit.point;
            else
                return;

            float radius = Mathf.Max(pattern().Width() * 0.5f, Intersection.MIN_INTERFACE);
            float innerRadius = radius * 0.75f;

            for (int i = 0; i < RING_SEGMENTS; i++)
            {
                float angle = (float)i / RING_SEGMENTS * Mathf.PI * 2f;
                float x = center.x + Mathf.Cos(angle) * radius;
                float z = center.z + Mathf.Sin(angle) * radius;
                cursorRingLine.SetPosition(i, new Vector3(x, center.y + 0.05f, z));
            }

            BuildCursorFillMesh(center, innerRadius);
        }

        private void CreatePreviewRoad()
        {
            previewRoadObj = new GameObject("RoadPreview");
            previewRoadObj.transform.SetParent(transform);
            previewMeshFilter = previewRoadObj.AddComponent<MeshFilter>();
            previewMeshRenderer = previewRoadObj.AddComponent<MeshRenderer>();
            previewMaterial = new Material(ShaderCache.RoadPreview);
            previewMaterial.SetColor("_BaseColor", new Color(0.29f, 0.40f, 1.0f, 0.45f));
            previewMeshRenderer.material = previewMaterial;
        }

        private void CreateCursorRing()
        {
            cursorRingObj = new GameObject("CursorRing");
            cursorRingObj.transform.SetParent(transform);
            cursorRingLine = cursorRingObj.AddComponent<LineRenderer>();
            cursorRingLine.useWorldSpace = true;
            cursorRingLine.loop = true;
            cursorRingLine.positionCount = RING_SEGMENTS;
            cursorRingLine.widthMultiplier = 0.3f;
            var ringMat = ShaderCache.CreateUnlitMaterial(ColorDefault);
            ringMat.color = ColorDefault;
            cursorRingLine.material = ringMat;
            cursorRingLine.startColor = ColorDefault;
            cursorRingLine.endColor = ColorDefault;

            cursorFillObj = new GameObject("CursorFill");
            cursorFillObj.transform.SetParent(transform);
            cursorFillMesh = cursorFillObj.AddComponent<MeshFilter>();
            cursorFillRenderer = cursorFillObj.AddComponent<MeshRenderer>();
            cursorFillMaterial = new Material(ShaderCache.RoadPreview);
            cursorFillMaterial.SetColor("_BaseColor", new Color(0.29f, 0.40f, 1.0f, 0.15f));
            cursorFillRenderer.material = cursorFillMaterial;
        }

        private void BuildCursorFillMesh(Vector3 center, float innerRadius)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();

            Vector3 up = Vector3.up;
            Color col = Color.white;
            float y = center.y + 0.03f;

            vertices.Add(new Vector3(center.x, y, center.z));
            normals.Add(up);
            colors.Add(col);

            for (int i = 0; i <= RING_SEGMENTS; i++)
            {
                float angle = (float)i / RING_SEGMENTS * Mathf.PI * 2f;
                float x = center.x + Mathf.Cos(angle) * innerRadius;
                float z = center.z + Mathf.Sin(angle) * innerRadius;
                vertices.Add(new Vector3(x, y, z));
                normals.Add(up);
                colors.Add(col);
            }

            for (int i = 1; i <= RING_SEGMENTS; i++)
            {
                indices.Add(0);
                indices.Add(i);
                indices.Add(i + 1);
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            cursorFillMesh.mesh = mesh;
        }

        private void UpdatePreviewMesh(Vector3 endHitPoint)
        {
            if (cachedPattern == null) return;
            var startInter = IntersectionManager.Instance.Intersections[startIntersectionId];
            if (startInter == null) return;

            Vector3 s = startInter.Position;
            Vector3 e = endHitPoint;
            Vector2 dir2 = new Vector2(e.x - s.x, e.z - s.z);
            float len = dir2.magnitude;
            if (len < 0.1f) { previewMeshFilter.mesh = null; return; }
            dir2 /= len;

            float halfW = pattern().Width() * 0.5f;
            Vector2 perp2 = new Vector2(-dir2.y, dir2.x);
            float previewY = startInter.Position.y + Heightfinder.ROAD_Z_OFFSET + 0.04f;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var cols = new List<Color>();
            var inds = new List<int>();

            Vector3 sL = new Vector3(s.x - perp2.x * halfW, previewY, s.z - perp2.y * halfW);
            Vector3 sR = new Vector3(s.x + perp2.x * halfW, previewY, s.z + perp2.y * halfW);
            Vector3 eL = new Vector3(e.x - perp2.x * halfW, previewY, e.z - perp2.y * halfW);
            Vector3 eR = new Vector3(e.x + perp2.x * halfW, previewY, e.z + perp2.y * halfW);

            verts.Add(sL); norms.Add(Vector3.up); cols.Add(Color.white);
            verts.Add(sR); norms.Add(Vector3.up); cols.Add(Color.white);
            verts.Add(eR); norms.Add(Vector3.up); cols.Add(Color.white);
            verts.Add(eL); norms.Add(Vector3.up); cols.Add(Color.white);
            inds.Add(0); inds.Add(1); inds.Add(2);
            inds.Add(0); inds.Add(2); inds.Add(3);

            int capSeg = 12;

            int capBase = verts.Count;
            verts.Add(new Vector3(s.x, previewY, s.z));
            norms.Add(Vector3.up);
            cols.Add(Color.white);
            for (int i = 0; i <= capSeg; i++)
            {
                float t = (float)i / capSeg;
                float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, t);
                float ca = Mathf.Cos(a);
                float sa = Mathf.Sin(a);
                Vector2 capDir = new Vector2(-dir2.x * ca + perp2.x * sa, -dir2.y * ca + perp2.y * sa);
                verts.Add(new Vector3(s.x + capDir.x * halfW, previewY, s.z + capDir.y * halfW));
                norms.Add(Vector3.up);
                cols.Add(Color.white);
            }
            for (int i = 0; i < capSeg; i++)
            {
                inds.Add(capBase);
                inds.Add(capBase + 1 + i);
                inds.Add(capBase + 2 + i);
            }

            int capBase2 = verts.Count;
            verts.Add(new Vector3(e.x, previewY, e.z));
            norms.Add(Vector3.up);
            cols.Add(Color.white);
            for (int i = 0; i <= capSeg; i++)
            {
                float t = (float)i / capSeg;
                float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, t);
                float ca = Mathf.Cos(a);
                float sa = Mathf.Sin(a);
                Vector2 capDir = new Vector2(dir2.x * ca + perp2.x * sa, dir2.y * ca + perp2.y * sa);
                verts.Add(new Vector3(e.x + capDir.x * halfW, previewY, e.z + capDir.y * halfW));
                norms.Add(Vector3.up);
                cols.Add(Color.white);
            }
            for (int i = 0; i < capSeg; i++)
            {
                inds.Add(capBase2);
                inds.Add(capBase2 + 1 + i);
                inds.Add(capBase2 + 2 + i);
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetTriangles(inds, 0);
            mesh.RecalculateBounds();
            previewMeshFilter.mesh = mesh;
        }

        private LanePattern pattern()
        {
            return cachedPattern;
        }

        public void SetLanePattern(LanePattern pattern)
        {
            if (pattern == null) return;
            cachedPattern = pattern;
        }

        void ConfirmRoad(string endIntersectionId)
        {
            if (cachedPattern == null)
            {
                CancelPlacement();
                return;
            }

            if (startIntersectionId == endIntersectionId)
            {
                CancelPlacement();
                return;
            }

            var inter1 = IntersectionManager.Instance.Intersections[startIntersectionId];
            var inter2 = IntersectionManager.Instance.Intersections[endIntersectionId];
            if (inter1 == null || inter2 == null)
            {
                Debug.LogWarning("[RoadPro] Invalid intersections for road placement");
                CancelPlacement();
                return;
            }

            var pts2D = new PolyLine3(new List<Vector3>
            {
                new Vector3(inter1.Position.x, 0f, inter1.Position.z),
                new Vector3(inter2.Position.x, 0f, inter2.Position.z)
            });

            if (TryHandleRoadCrossing(pts2D, startIntersectionId, endIntersectionId))
                return;

            var pat = pattern();
            float minRoadLen = Mathf.Max(Intersection.EmptyInterface(pat.Width()) * 2f + 1f, MIN_ROAD_LENGTH);
            if (pts2D.Length() < minRoadLen)
            {
                Debug.Log($"[RoadPro] Road too short ({pts2D.Length():F1}m < {minRoadLen:F1}m)");
                CancelPlacement();
                return;
            }

            float startH = inter1.Position.y;
            float endH = inter2.Position.y;

            ResolveLayers();
            var (initial3D, heightError) = Heightfinder.Run(pts2D, startH, endH, 0.25f, groundLayer);

            if (heightError != null)
            {
                Debug.LogWarning($"[RoadPro] Height error: {heightError}");
            }

            var road = new RoadData
            {
                Id = Guid.NewGuid().ToString(),
                SrcIntersectionId = startIntersectionId,
                DstIntersectionId = endIntersectionId,
                Points = pts2D,
                InterfacedPoints = initial3D,
                Width = pat.Width(),
                IsOneWay = false,
                SrcInterface = Intersection.EmptyInterface(pat.Width()),
                DstInterface = Intersection.EmptyInterface(pat.Width())
            };

            float distFromBottom = 0f;
            foreach (var (kind, dir) in pat.Lanes)
            {
                road.Lanes.Add(new LaneData
                {
                    Kind = kind,
                    Direction = dir,
                    DistFromBottom = distFromBottom
                });
                distFromBottom += kind.Width();
            }

            var go = new GameObject("Road " + road.Id);
            go.transform.SetParent(transform);
            go.layer = LayerMask.NameToLayer("Default");
            if (go.layer == -1) go.layer = 0;

            var renderer = go.AddComponent<RoadRenderer>();
            if (roadMaterial != null)
            {
                renderer.RoadMaterial = roadMaterial;
                RoadRegistry.Instance.RoadMaterial = roadMaterial;
            }

            renderer.terrainLayerMask = groundLayer;

            RoadRegistry.Instance.Register(road, renderer);
            IntersectionManager.Instance.AddRoadToIntersection(startIntersectionId, road.Id);
            IntersectionManager.Instance.AddRoadToIntersection(endIntersectionId, road.Id);
            IntersectionManager.Instance.ReapplyAllInterfaces();

            RebuildAffectedRoads(new HashSet<string> { startIntersectionId, endIntersectionId });
            IntersectionManager.Instance.RebuildAllIntersectionMeshes();

            Debug.Log($"[RoadPro] Road placed: {pts2D.Length():F1}m");
            OnRoadPlaced?.Invoke(road);

            CurrentState = PlaceState.Idle;
            startIntersectionId = null;
            previewRoadObj.SetActive(false);
            previewMeshFilter.mesh = null;
            cachedPattern = null;
        }

        void CancelPlacement()
        {
            CurrentState = PlaceState.Idle;
            startIntersectionId = null;
            previewRoadObj.SetActive(false);
            previewMeshFilter.mesh = null;
        }

        private bool TryHandleRoadCrossing(PolyLine3 newRoadPts, string srcInterId, string dstInterId)
        {
            if (cachedPattern == null) return false;

            Vector2 newStart = new Vector2(newRoadPts.Points[0].x, newRoadPts.Points[0].z);
            Vector2 newEnd = new Vector2(newRoadPts.Points[newRoadPts.Points.Count - 1].x, newRoadPts.Points[newRoadPts.Points.Count - 1].z);

            var crossings = CrossingDetect.FindAll(newStart, newEnd, RoadRegistry.Instance, srcInterId, dstInterId);
            if (crossings.Count == 0) return false;

            float patWidth = pattern().Width();
            float minSegLen = Intersection.EmptyInterface(patWidth) * 2f;

            var orderedInters = new List<(string interId, Vector3 position)>();
            Vector3 prevPos = Vector3.zero;
            bool first = true;

            foreach (var cr in crossings)
            {
                Vector3 cross3D = new Vector3(cr.Point.x, 0f, cr.Point.y);
                cross3D.y = Heightfinder.SampleTerrainHeight(cross3D, groundLayer);

                if (!first && Vector3.Distance(cross3D, prevPos) < minSegLen)
                    continue;
                first = false;
                prevPos = cross3D;

                string crossInterId = IntersectionManager.Instance.CreateIntersection(cross3D, 0.1f);
                IntersectionManager.Instance.Intersections[crossInterId].Position = cross3D;

                ClearNodesForRoad(cr.ExistingRoadId);
                var (splitA, splitB) = RoadRegistry.Instance.SplitRoad(cr.ExistingRoadId, crossInterId, cross3D, groundLayer);
                if (splitA == null || splitB == null)
                    continue;

                orderedInters.Add((crossInterId, cross3D));
            }

            if (orderedInters.Count == 0) return false;

            string prev = srcInterId;
            var createdRoads = new List<RoadData>();
            foreach (var (interId, _) in orderedInters)
            {
                createdRoads.Add(CreateRoadBetween(prev, interId));
                prev = interId;
            }
            createdRoads.Add(CreateRoadBetween(prev, dstInterId));

            IntersectionManager.Instance.ReapplyAllInterfaces();

            var affectedInters = new HashSet<string> { srcInterId, dstInterId };
            foreach (var (interId, _) in orderedInters)
                affectedInters.Add(interId);
            RebuildAffectedRoads(affectedInters);
            IntersectionManager.Instance.RebuildAllIntersectionMeshes();

            foreach (var created in createdRoads)
                OnRoadPlaced?.Invoke(created);

            CurrentState = PlaceState.Idle;
            startIntersectionId = null;
            previewRoadObj.SetActive(false);
            previewMeshFilter.mesh = null;
            cachedPattern = null;
            return true;
        }

        private RoadData CreateRoadBetween(string srcId, string dstId)
        {
            if (cachedPattern == null) return null;

            var src = IntersectionManager.Instance.Intersections[srcId];
            var dst = IntersectionManager.Instance.Intersections[dstId];

            var pts2D = new PolyLine3(new List<Vector3>
            {
                new Vector3(src.Position.x, 0f, src.Position.z),
                new Vector3(dst.Position.x, 0f, dst.Position.z)
            });

            var pat = pattern();
            float startH = src.Position.y;
            float endH = dst.Position.y;
            var (initial3D, _) = Heightfinder.Run(pts2D, startH, endH, 0.25f, groundLayer);

            var road = new RoadData
            {
                Id = Guid.NewGuid().ToString(),
                SrcIntersectionId = srcId,
                DstIntersectionId = dstId,
                Points = pts2D,
                InterfacedPoints = initial3D,
                Width = pat.Width(),
                IsOneWay = false,
                SrcInterface = Intersection.EmptyInterface(pat.Width()),
                DstInterface = Intersection.EmptyInterface(pat.Width())
            };

            float distFromBottom = 0f;
            foreach (var (kind, dir) in pat.Lanes)
            {
                road.Lanes.Add(new LaneData
                {
                    Kind = kind,
                    Direction = dir,
                    DistFromBottom = distFromBottom
                });
                distFromBottom += kind.Width();
            }

            var go = new GameObject("Road " + road.Id);
            go.transform.SetParent(transform);
            var renderer = go.AddComponent<RoadRenderer>();
            renderer.RoadMaterial = roadMaterial;
            renderer.terrainLayerMask = groundLayer;

            RoadRegistry.Instance.Register(road, renderer);
            IntersectionManager.Instance.AddRoadToIntersection(srcId, road.Id);
            IntersectionManager.Instance.AddRoadToIntersection(dstId, road.Id);
            return road;
        }

        private void GenerateNodesForRoad(string roadId)
        {
            ClearNodesForRoad(roadId);
            var road = RoadRegistry.Instance?.GetById(roadId);
            if (road == null) return;

            var poly = road.InterfacedPoints ?? road.Points;
            if (poly == null || poly.Count < 2) return;

            float len = poly.Length();
            if (len < NODE_SPACING * 1.5f) return;

            int count = Mathf.Max(1, Mathf.FloorToInt(len / NODE_SPACING));
            float step = len / (count + 1);

            var positions = new List<Vector3>();
            var objs = new List<GameObject>();

            for (int i = 0; i < count; i++)
            {
                float t = (i + 1) * step;
                Vector3 pos = poly.PointAlong(t);
                float groundY = Heightfinder.SampleTerrainHeight(pos, groundLayer);
                if (!float.IsNaN(groundY))
                    pos.y = groundY;
                pos.y += 0.04f;

                positions.Add(pos);
                objs.Add(BuildNodeSphere(pos));
            }

            roadNodePositions[roadId] = positions.ToArray();
            roadNodes[roadId] = objs;
        }

        private GameObject BuildNodeSphere(Vector3 position)
        {
            var go = new GameObject("RoadNode");
            go.transform.SetParent(transform);
            go.transform.position = position;

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = MakeNodeMesh();

            var mr = go.AddComponent<MeshRenderer>();
            mr.material = nodeMaterial;
            return go;
        }

        private static Mesh nodeMesh;

        private static Mesh MakeNodeMesh()
        {
            if (nodeMesh != null) return nodeMesh;
            nodeMesh = new Mesh();

            int segs = 12;
            float r = NODE_RADIUS;
            var verts = new Vector3[segs + 1];
            var tris = new int[segs * 3];
            var norms = new Vector3[segs + 1];
            var cols = new Color[segs + 1];

            verts[0] = Vector3.zero;
            norms[0] = Vector3.up;
            cols[0] = Color.white;

            for (int i = 0; i < segs; i++)
            {
                float a = (float)i / segs * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                norms[i + 1] = Vector3.up;
                cols[i + 1] = Color.white;
            }

            for (int i = 0; i < segs; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segs + 1;
            }

            nodeMesh.vertices = verts;
            nodeMesh.normals = norms;
            nodeMesh.colors = cols;
            nodeMesh.triangles = tris;
            nodeMesh.RecalculateBounds();
            return nodeMesh;
        }

        private void ClearNodesForRoad(string roadId)
        {
            if (roadNodes.TryGetValue(roadId, out var objs))
            {
                foreach (var o in objs)
                    if (o != null) Destroy(o);
                roadNodes.Remove(roadId);
            }
            roadNodePositions.Remove(roadId);
        }

        private void ClearAllNodes()
        {
            var keys = new List<string>(roadNodes.Keys);
            foreach (var k in keys)
                ClearNodesForRoad(k);
        }

        private void RebuildAllNodes()
        {
            ClearAllNodes();
            if (RoadRegistry.Instance == null) return;
            foreach (var kvp in RoadRegistry.Instance.Roads)
                GenerateNodesForRoad(kvp.Key);
        }

        private void FindNearestNode(out string roadId, out int nodeIndex, out Vector3 nodePos, out float nodeDist)
        {
            roadId = null;
            nodeIndex = -1;
            nodePos = Vector3.zero;
            nodeDist = SNAP_RADIUS;

            Vector3 mouseWorld = GetMouseWorldPos();
            if (float.IsNaN(mouseWorld.x)) return;

            foreach (var kvp in roadNodePositions)
            {
                var positions = kvp.Value;
                for (int i = 0; i < positions.Length; i++)
                {
                    float dx = mouseWorld.x - positions[i].x;
                    float dz = mouseWorld.z - positions[i].z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < nodeDist)
                    {
                        nodeDist = d;
                        roadId = kvp.Key;
                        nodeIndex = i;
                        nodePos = positions[i];
                    }
                }
            }
        }

        private Vector3 GetMouseWorldPos()
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
                return hit.point;
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private void UpdateNodeHighlight()
        {
            if (hoveredNodeKey != null)
            {
                Color c = ColorDefault;
                c.a = 0.5f;
                nodeMaterial.SetColor("_BaseColor", c);
                hoveredNodeKey = null;
            }

            if (hoveredRoadId != null && roadNodePositions.TryGetValue(hoveredRoadId, out _))
            {
                Color c = ColorTJunction;
                c.a = 0.8f;
                nodeMaterial.SetColor("_BaseColor", c);
                hoveredNodeKey = hoveredRoadId;
            }
        }

        private void RebuildAffectedRoads(HashSet<string> affectedIntersectionIds)
        {
            var rebuilt = new HashSet<string>();
            foreach (var interId in affectedIntersectionIds)
            {
                if (!IntersectionManager.Instance.Intersections.TryGetValue(interId, out var inter))
                    continue;
                foreach (var rid in inter.RoadIds)
                {
                    if (rebuilt.Contains(rid)) continue;
                    rebuilt.Add(rid);

                    var r = RoadRegistry.Instance.GetById(rid);
                    var ren = RoadRegistry.Instance.GetRenderer(rid);
                    if (r == null || ren == null) continue;

                    float roadLen = r.Points.Length();
                    float safeSrc = Mathf.Min(r.SrcInterface, roadLen * 0.4f);
                    float safeDst = Mathf.Min(r.DstInterface, roadLen * 0.4f);
                    var trimmed = r.Points.Cut(safeSrc, safeDst);
                    if (trimmed.Count >= 2)
                    {
                        var im = IntersectionManager.Instance.Intersections;
                        float sH = 0f, eH = 0f;
                        if (im.TryGetValue(r.SrcIntersectionId, out var si)) sH = si.Position.y;
                        if (im.TryGetValue(r.DstIntersectionId, out var di)) eH = di.Position.y;
                        var (ip, _) = Heightfinder.Run(trimmed, sH, eH, 0.25f, groundLayer);
                        r.InterfacedPoints = ip;
                    }
                    ren.RoadData = r;
                    ren.Rebuild();
                    GenerateNodesForRoad(rid);
                }
            }
        }

        private static bool TryFindRoadCrossing(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 intersection, out float t)
        {
            return Intersect.SegmentSegment(a, b, c, d, out intersection, out t, out _);
        }
    }
}
