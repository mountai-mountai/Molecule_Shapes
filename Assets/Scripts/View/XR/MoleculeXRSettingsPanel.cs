// World-space settings panel for the VR scene. Binds checkboxes to AppSettings and persists on change
// (PlayerPrefs via SettingsStore), so choices carry across sessions. Same theme/placement/movable/live-
// rebuild pattern as the other panels.
//
// Phase 1 rows:
//   Panels stay upright   - LIVE (PanelGrabController reads it)
//   Terminal lone pairs   - saved now; feature wired next phase
//   Sound                 - saved now; audio wired next phase
//   [Advanced] Show AXE modes - LIVE (the game panel's Mode list respects it)

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    public class MoleculeXRSettingsPanel : MonoBehaviour
    {
        [Header("Theme")]
        [SerializeField] private PanelStyle style;

        [Header("Placement / axis (world space)")]
        [Tooltip("Offset from this object's position (default: further right, past the controls panel).")]
        [SerializeField] private Vector3 panelWorldOffset = new Vector3(0.72f, 0f, 0f);
        [SerializeField] private Vector3 panelEulerAngles = Vector3.zero;
        [SerializeField] private Vector2 panelSizeMeters = new Vector2(0.34f, 0.42f);
        [SerializeField] private float canvasScale = 0.001f;

        [Header("Movable")]
        [SerializeField] private bool movable = true;
        [SerializeField] private float handleThickness = 0.02f;
        [Range(0.05f, 1f)] [SerializeField] private float handleWidthFraction = 1f;
        [SerializeField] private Color handleColor = new Color(0.30f, 0.40f, 0.55f, 1f);

        [Header("Layout (UI units)")]
        [SerializeField] private float titleHeight = 48f;
        [SerializeField] private float sectionHeight = 30f;
        [SerializeField] private float rowHeight = 46f;
        [SerializeField] private float checkboxSize = 32f;

        private PanelStyle _style;
        private GameObject _panelRoot;

        private void Awake() => BuildPanel();
        private void OnDestroy() { if (_panelRoot != null) Destroy(_panelRoot); }

        private void OnValidate()
        {
            if (Application.isPlaying && _panelRoot != null) RebuildNow();
        }

        [ContextMenu("Rebuild Panel")]
        public void RebuildNow()
        {
            if (!Application.isPlaying) return;
            Vector3? pos = null; Quaternion rot = Quaternion.identity;
            if (_panelRoot != null) { pos = _panelRoot.transform.position; rot = _panelRoot.transform.rotation; Destroy(_panelRoot); }
            BuildPanel();
            if (pos.HasValue) _panelRoot.transform.SetPositionAndRotation(pos.Value, rot);
        }

        private void EnsureStyle() => _style = style != null ? style : (_style != null ? _style : PanelStyle.CreateDefault());

        private void BuildPanel()
        {
            EnsureStyle();
            AppSettings s = AppSettings.Current;

            _panelRoot = new GameObject("MoleculeXRSettingsPanel Root");
            _panelRoot.transform.SetPositionAndRotation(transform.position + panelWorldOffset, Quaternion.Euler(panelEulerAngles));

            var canvasGO = new GameObject("MoleculeXRSettingsPanel Canvas");
            canvasGO.transform.SetParent(_panelRoot.transform, false);
            canvasGO.transform.localScale = Vector3.one * canvasScale;

            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSizeMeters.x / canvasScale, panelSizeMeters.y / canvasScale);

            AddBackground(canvasGO.transform);

            Transform col = PanelUi.MakeColumn(canvasGO.transform, _style, _style.rowSpacing, fillParent: true);

            PanelUi.MakeLabel(col, _style, "Settings", _style.titleFontSize, FontStyle.Bold,
                              TextAnchor.MiddleCenter, _style.textColor, titleHeight);

            PanelUi.MakeToggleRow(col, _style, "Panels stay upright", s.PanelsUpright,
                v => { s.PanelsUpright = v; s.Save(); }, rowHeight, checkboxSize, _style.textColor);

            PanelUi.MakeToggleRow(col, _style, "Terminal lone pairs", s.AllowTerminalLonePairs,
                v => { s.AllowTerminalLonePairs = v; s.Save(); }, rowHeight, checkboxSize, _style.textColor);

            PanelUi.MakeToggleRow(col, _style, "Sound", s.SoundEnabled,
                v => { s.SoundEnabled = v; s.Save(); }, rowHeight, checkboxSize, _style.textColor);

            PanelUi.MakeLabel(col, _style, "Advanced", _style.sectionFontSize, FontStyle.Bold,
                              TextAnchor.MiddleLeft, _style.subtitleColor, sectionHeight);

            PanelUi.MakeToggleRow(col, _style, "Show AXE modes", s.ShowAxeModes,
                v => { s.ShowAxeModes = v; s.Save(); }, rowHeight, checkboxSize, _style.textColor);

            PanelUi.AddFlexibleSpacer(col);

            if (movable) PanelUi.AddHandle(_panelRoot.transform, panelSizeMeters, handleThickness, handleColor, handleWidthFraction);

            PanelUi.ForceRebuild(col);
        }

        private void AddBackground(Transform parent)
        {
            var bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(parent, false);
            RectTransform r = bg.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            Image img = bg.AddComponent<Image>();
            img.raycastTarget = false;
            PanelUi.StyleAsPanel(img, _style);
        }
    }
}
