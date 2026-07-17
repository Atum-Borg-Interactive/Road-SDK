using UnityEngine;
using RoadPro.Generation;

namespace RoadPro.Unity
{
    public class RoadToolbarUI : MonoBehaviour
    {
        private RoadBuilder builder;
        private Rect toolbarRect = new Rect(10, 10, 520, 32);
        private bool showLanePicker;

        void Start()
        {
            builder = GetComponent<RoadBuilder>();
            if (builder == null)
                builder = FindFirstObjectByType<RoadBuilder>();
        }

        void OnGUI()
        {
            DrawToolbar();

            if (showLanePicker)
                DrawLanePicker();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginArea(toolbarRect);
            GUILayout.BeginHorizontal(GUI.skin.box);

            var active = builder != null ? builder.ActiveTool : RoadBuilder.ToolType.None;

            if (ToolButton("Road", active == RoadBuilder.ToolType.Road, KeyCode.R))
            {
                if (active == RoadBuilder.ToolType.Road)
                {
                    builder?.SetTool(RoadBuilder.ToolType.None);
                    showLanePicker = false;
                }
                else
                {
                    builder?.SetTool(RoadBuilder.ToolType.Road);
                    showLanePicker = true;
                }
            }

            GUILayout.Space(10);

            if (ToolButton("Bulldoze", active == RoadBuilder.ToolType.Bulldoze, KeyCode.X))
            {
                builder?.SetTool(RoadBuilder.ToolType.Bulldoze);
                showLanePicker = false;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLanePicker()
        {
            float totalWidth = 540f;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = Screen.height - 50f;
            var rect = new Rect(left, top, totalWidth, 36f);

            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal(GUI.skin.box);

            if (GUILayout.Button("1 Lane (One-Way)", GUILayout.Width(110), GUILayout.Height(26)))
                builder?.SetLanePattern(LanePattern.OneWayStreet());
            if (GUILayout.Button("2 Lane", GUILayout.Width(80), GUILayout.Height(26)))
                builder?.SetLanePattern(LanePattern.TwoLaneStreet());
            if (GUILayout.Button("4 Lane", GUILayout.Width(80), GUILayout.Height(26)))
                builder?.SetLanePattern(LanePattern.FourLaneStreet());
            if (GUILayout.Button("6 Lane", GUILayout.Width(80), GUILayout.Height(26)))
                builder?.SetLanePattern(LanePattern.SixLaneStreet());
            if (GUILayout.Button("Highway", GUILayout.Width(90), GUILayout.Height(26)))
                builder?.SetLanePattern(LanePattern.Highway());

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private bool ToolButton(string label, bool isActive, KeyCode hotkey)
        {
            Color prev = GUI.backgroundColor;
            if (isActive)
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 1f);

            bool clicked = GUILayout.Button(label + " [" + hotkey + "]", GUILayout.Width(90), GUILayout.Height(24));

            GUI.backgroundColor = prev;
            return clicked;
        }

        void Update()
        {
            if (builder == null) return;

            if (Input.GetKeyDown(KeyCode.R))
            {
                bool wasRoad = builder.ActiveTool == RoadBuilder.ToolType.Road;
                builder.SetTool(wasRoad ? RoadBuilder.ToolType.None : RoadBuilder.ToolType.Road);
                showLanePicker = !wasRoad;
            }
            else if (Input.GetKeyDown(KeyCode.X))
            {
                builder.SetTool(RoadBuilder.ToolType.Bulldoze);
                showLanePicker = false;
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (builder.ActiveTool != RoadBuilder.ToolType.None)
                {
                    builder.SetTool(RoadBuilder.ToolType.None);
                    showLanePicker = false;
                }
            }
        }
    }
}
