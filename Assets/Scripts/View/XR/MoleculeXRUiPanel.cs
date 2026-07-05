// World-space uGUI panel of buttons for the VR scene. Replaces the desktop MoleculeUiController's
// OnGUI panel (which doesn't render in VR headsets at all).
//
// Builds the panel programmatically so there's no scene authoring or prefab wiring - drop the
// component on a GameObject and a Canvas with all the buttons appears at runtime. The panel
// lives at the scene root in world space (not parented to the molecule), so it doesn't move
// when the user grabs and rotates the molecule.
//
// Pointer interaction comes for free from the XR Ray Interactor on each controller: the
// TrackedDeviceGraphicRaycaster on the Canvas lets the controller's ray click buttons by pulling
// the index trigger (the same trigger the atom drag uses - on a UI element, the trigger means
// "click" and the atom-drag controller's TryBeginDrag finds no atom to grab).

using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(MoleculeController))]
    public class MoleculeXRUiPanel : MonoBehaviour
    {
        [Header("Placement (world space)")]
        [Tooltip("Where the panel sits relative to the molecule's initial position.")]
        [SerializeField] private Vector3 panelWorldOffset = new Vector3(0.35f, 0f, 0f);
        [Tooltip("Panel physical size in meters. Height must be tall enough to fit all buttons + padding.")]
        [SerializeField] private Vector2 panelSizeMeters = new Vector2(0.32f, 0.65f);
        [Tooltip("1 UI unit = canvasScale meters (smaller -> sharper text but smaller font visually).")]
        [SerializeField] private float canvasScale = 0.001f;

        [Header("Style")]
        [SerializeField] private Color panelBackground = new Color(0.05f, 0.07f, 0.10f, 0.92f);
        [SerializeField] private Color buttonNormal = new Color(0.20f, 0.25f, 0.32f, 1f);
        [SerializeField] private Color buttonHover = new Color(0.30f, 0.40f, 0.55f, 1f);
        [SerializeField] private Color buttonPressed = new Color(0.45f, 0.60f, 0.85f, 1f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int titleFontSize = 28;
        [SerializeField] private int sectionFontSize = 20;
        [SerializeField] private int buttonFontSize = 20;
        [SerializeField] private int presetButtonFontSize = 22;

        [Header("Preset count buttons (1-6)")]
        [Tooltip("Show the '1-6 set bonded count' shortcut row at all.")]
        [SerializeField] private bool showPresetButtons = true;
        [Tooltip("Hide the preset shortcut row while a Build challenge is active, so the AXE number " +
                 "can't be used as a one-press shortcut - the player must add/remove atoms deliberately.")]
        [SerializeField] private bool hidePresetsDuringChallenge = true;

        private MoleculeController _controller;
        private BondAngleOverlay _overlay;
        private GameSessionController _gameController;
        private Font _font;
        private GameObject _canvasGO;
        private GameObject _presetRow;
        private GameObject _presetSectionLabel;
        private bool _gameSubscribed;

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            _overlay = GetComponent<BondAngleOverlay>();
            _gameController = GetComponent<GameSessionController>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildPanel();
        }

        private void Start()
        {
            SubscribeToGame();          // GameSessionController.Session exists after its Awake
            UpdatePresetVisibility();
        }

        private void OnDestroy()
        {
            UnsubscribeFromGame();
            if (_canvasGO != null) Destroy(_canvasGO);
        }

        // --- Preset-row visibility (anti-shortcut) --------------------------------------------------

        private GameSession Game => _gameController != null ? _gameController.Session : null;

        private void SubscribeToGame()
        {
            if (_gameSubscribed || Game == null) return;
            Game.ChallengeStarted += _ => UpdatePresetVisibility();
            Game.ChallengeSolved += (_, __) => UpdatePresetVisibility();
            Game.ChallengeTimedOut += _ => UpdatePresetVisibility();
            Game.StateChanged += UpdatePresetVisibility;
            _gameSubscribed = true;
        }

        private void UnsubscribeFromGame()
        {
            // Lambdas above aren't individually removable; the whole panel is destroyed with the scene,
            // so there's nothing to leak. Kept as a hook for symmetry / future explicit handlers.
        }

        private void UpdatePresetVisibility()
        {
            bool activeBuild = Game != null
                               && Game.Mode == GameMode.Challenge
                               && Game.Current is { Task: TaskMode.Build }
                               && !Game.CurrentSolved;

            bool visible = showPresetButtons && !(hidePresetsDuringChallenge && activeBuild);
            if (_presetRow != null) _presetRow.SetActive(visible);
            if (_presetSectionLabel != null) _presetSectionLabel.SetActive(visible);
        }

        private void BuildPanel()
        {
            // Canvas at scene root, world-space, oriented to face -Z (the direction the user faces).
            _canvasGO = new GameObject("MoleculeXRUiPanel Canvas");
            _canvasGO.transform.position = transform.position + panelWorldOffset;
            // World-Space Canvas UI renders on the local -Z face; with identity rotation that -Z
            // face naturally points toward the user (who's at world -Z relative to the molecule).
            _canvasGO.transform.rotation = Quaternion.identity;
            _canvasGO.transform.localScale = Vector3.one * canvasScale;

            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rt = _canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSizeMeters.x / canvasScale, panelSizeMeters.y / canvasScale);

            AddBackground(_canvasGO.transform);
            BuildButtons(_canvasGO.transform);
        }

        private void AddBackground(Transform parent)
        {
            var bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(parent, false);
            RectTransform r = bg.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            Image img = bg.AddComponent<Image>();
            img.color = panelBackground;
            img.raycastTarget = false;
        }

        private void BuildButtons(Transform parent)
        {
            var container = new GameObject("Buttons", typeof(RectTransform));
            container.transform.SetParent(parent, false);
            RectTransform r = container.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(20, 20);
            r.offsetMax = new Vector2(-20, -20);

            VerticalLayoutGroup layout = container.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            CreateLabel(container.transform, "Molecule Controls", titleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, height: 48);

            CreateButton(container.transform, "+ Atom",        () => _controller.AddBondedAtom());
            CreateButton(container.transform, "− Atom",        () => _controller.RemoveLastAtom());
            CreateButton(container.transform, "+ Lone Pair",   () => _controller.AddLonePair());
            CreateButton(container.transform, "− Lone Pair",   () => _controller.RemoveLastLonePair());

            Text presetLabel = CreateLabel(container.transform, "Set bonded count:", sectionFontSize, FontStyle.Normal, TextAnchor.MiddleLeft, height: 32);
            _presetSectionLabel = presetLabel.gameObject;

            // Horizontal row of 1..6
            var row = new GameObject("Presets", typeof(RectTransform));
            row.transform.SetParent(container.transform, false);
            _presetRow = row;
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = true;
            LayoutElement rowEl = row.AddComponent<LayoutElement>();
            rowEl.minHeight = 56;
            rowEl.preferredHeight = 56;
            for (int n = 1; n <= 6; n++)
            {
                int target = n;
                CreatePresetButton(row.transform, target.ToString(), () => _controller.SetBondedAtomCount(target));
            }

            CreateButton(container.transform, "Reset", () => _controller.ResetMolecule());

            if (_overlay != null)
            {
                CreateButton(container.transform, "Toggle Angles", () => _overlay.ToggleVisible());
            }
        }

        private Text CreateLabel(Transform parent, string text, int fontSize, FontStyle style, TextAnchor alignment, float height)
        {
            var go = new GameObject(text + " Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = alignment;
            t.color = textColor;
            t.raycastTarget = false;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return t;
        }

        private Button CreateButton(Transform parent, string label, Action onClick)
        {
            var go = new GameObject(label + " Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image img = go.AddComponent<Image>();
            img.color = buttonNormal;

            Button btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = buttonNormal;
            colors.highlightedColor = buttonHover;
            colors.pressedColor = buttonPressed;
            colors.selectedColor = buttonHover;
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = 50;
            le.preferredHeight = 50;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            RectTransform tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            Text t = textGO.AddComponent<Text>();
            t.font = _font;
            t.text = label;
            t.fontSize = buttonFontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = textColor;
            t.raycastTarget = false;

            return btn;
        }

        private Button CreatePresetButton(Transform parent, string label, Action onClick)
        {
            Button btn = CreateButton(parent, label, onClick);
            // Override font size on the inner text for the preset row
            Text t = btn.GetComponentInChildren<Text>();
            if (t != null) t.fontSize = presetButtonFontSize;
            // Square-ish buttons in the preset row
            LayoutElement le = btn.GetComponent<LayoutElement>();
            le.preferredWidth = 50;
            le.minWidth = 50;
            return btn;
        }
    }
}
