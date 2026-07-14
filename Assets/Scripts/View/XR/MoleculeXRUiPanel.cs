// World-space uGUI panel of molecule-editing buttons for the VR scene (the "sandbox" controls).
// Built programmatically - drop the component on the molecule object and a Canvas with all the
// buttons appears at runtime. It sits at the scene root so it doesn't move when the molecule is grabbed.
//
// The LOOK (fonts/colours/rounded edges/button shape) comes from a shared PanelStyle theme asset,
// the same one the game panel uses - assign it in the Inspector to restyle both at once. Placement/
// axis and layout sizes stay per-panel. Editing any field while playing rebuilds the panel live
// (or right-click -> Rebuild Panel).
//
// Pointer clicks come free from the controllers' XR Ray Interactor via the TrackedDeviceGraphicRaycaster.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Molecule_Shapes.Game;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(MoleculeController))]
    public class MoleculeXRUiPanel : MonoBehaviour
    {
        [Header("Theme")]
        [Tooltip("Shared look (fonts/colours/edges). Assign the same PanelStyle used by the game panel " +
                 "to restyle both together. Empty = built-in defaults.")]
        [SerializeField] private PanelStyle style;

        [Header("Placement / axis (world space)")]
        [Tooltip("Offset from the molecule's initial position (default: right of it).")]
        [SerializeField] private Vector3 panelWorldOffset = new Vector3(0.35f, 0f, 0f);
        [Tooltip("Panel orientation in degrees. (0,0,0) faces the user standing on -Z.")]
        [SerializeField] private Vector3 panelEulerAngles = Vector3.zero;
        [SerializeField] private Vector2 panelSizeMeters = new Vector2(0.32f, 0.65f);
        [SerializeField] private float canvasScale = 0.001f;

        [Header("Movable")]
        [Tooltip("Add a grab bar on top so the panel can be picked up and moved (needs one " +
                 "PanelGrabController in the scene, wired to the controllers + grip actions).")]
        [SerializeField] private bool movable = true;
        [Tooltip("Thickness of the grab bar, metres.")]
        [SerializeField] private float handleThickness = 0.02f;
        [Tooltip("Grab-bar width as a fraction of the panel width. 1 = full bar; smaller = a centered tab.")]
        [Range(0.05f, 1f)] [SerializeField] private float handleWidthFraction = 1f;
        [SerializeField] private Color handleColor = new Color(0.30f, 0.40f, 0.55f, 1f);

        [Header("Layout (UI units)")]
        [Tooltip("Height of the 'Molecule Controls' title row.")]
        [SerializeField] private float titleHeight = 48f;
        [Tooltip("Height of each action button (+ Atom, − Atom, Reset, etc.).")]
        [SerializeField] private float buttonHeight = 50f;
        [Tooltip("Height of the 'Set bonded count:' label row.")]
        [SerializeField] private float sectionHeight = 32f;
        [Tooltip("Height of the 1-6 preset row (its buttons match this height).")]
        [SerializeField] private float presetRowHeight = 56f;
        [Tooltip("Width of each 1-6 preset button (now honoured - the row no longer force-expands them).")]
        [SerializeField] private float presetButtonWidth = 50f;
        [Tooltip("Font size of the digits on the 1-6 preset buttons.")]
        [SerializeField] private int presetButtonFontSize = 22;

        [Header("Geometry read-outs (side toggles)")]
        [Tooltip("Show Electron/Molecular geometry checkboxes beside the panel; toggle one to reveal its name.")]
        [SerializeField] private bool showGeometryToggles = true;
        [Tooltip("Strip position from the panel's top-right corner, UI units. Positive x pushes it outside, right.")]
        [SerializeField] private Vector2 geometrySideOffset = new Vector2(8f, -8f);
        [Tooltip("Strip size in UI units (x = width, y = height). Long names overflow past the width.")]
        [SerializeField] private Vector2 geometrySideSize = new Vector2(210f, 120f);
        [Tooltip("Height of each geometry toggle row (the caption/name text height).")]
        [SerializeField] private float geometryRowHeight = 48f;
        [Tooltip("Size (square) of each checkbox, UI units - independent of the row/text height.")]
        [SerializeField] private float geometryCheckboxSize = 32f;
        [Tooltip("Colour of the 'Electron' / 'Molecular' captions.")]
        [SerializeField] private Color geometryCaptionColor = Color.white;
        [Tooltip("Colour of the popped-out geometry name.")]
        [SerializeField] private Color geometryValueColor = new Color(0.62f, 0.70f, 0.80f, 1f);

        [Header("Preset count buttons (1-6)")]
        [Tooltip("Show the '1-6 set bonded count' shortcut row at all.")]
        [SerializeField] private bool showPresetButtons = true;
        [Tooltip("Hide the preset shortcut row while a Build challenge is active, so the AXE number " +
                 "can't be used as a one-press shortcut - the player must add/remove atoms deliberately.")]
        [SerializeField] private bool hidePresetsDuringChallenge = true;

        private MoleculeController _controller;
        private BondAngleOverlay _overlay;
        private GameSessionController _gameController;
        private PanelStyle _style;
        private GameObject _panelRoot;
        private GameObject _canvasGO;
        private GameObject _presetRow;
        private GameObject _presetSectionLabel;
        private GameObject _geometryStrip;
        private Text _electronValue;
        private Text _molecularValue;
        private bool _gameSubscribed;

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            _overlay = GetComponent<BondAngleOverlay>();
            _gameController = GetComponent<GameSessionController>();
            BuildPanel();
        }

        private void Update()
        {
            if (showGeometryToggles) RefreshGeometryReadouts();
        }

        private void Start()
        {
            SubscribeToGame();          // GameSessionController.Session exists after its Awake
            RefreshGameDrivenVisibility();
        }

        private void OnDestroy()
        {
            if (_panelRoot != null) Destroy(_panelRoot);
        }

        // Live restyle while playing.
        private void OnValidate()
        {
            if (Application.isPlaying && _panelRoot != null) RebuildNow();
        }

        [ContextMenu("Rebuild Panel")]
        public void RebuildNow()
        {
            if (!Application.isPlaying) return;

            // Preserve where the user moved the panel to across a live rebuild.
            Vector3? pos = null; Quaternion rot = Quaternion.identity;
            if (_panelRoot != null)
            {
                pos = _panelRoot.transform.position; rot = _panelRoot.transform.rotation;
                Destroy(_panelRoot);
            }
            BuildPanel();
            if (pos.HasValue) _panelRoot.transform.SetPositionAndRotation(pos.Value, rot);

            RefreshGameDrivenVisibility();
        }

        // Prefer an assigned theme asset (picked up even if assigned at runtime); otherwise reuse or
        // create a defaults instance.
        private void EnsureStyle() => _style = style != null ? style : (_style != null ? _style : PanelStyle.CreateDefault());

        // --- Game-driven visibility (anti-shortcut / anti-cheat) ------------------------------------

        private GameSession Game => _gameController != null ? _gameController.Session : null;

        private void SubscribeToGame()
        {
            if (_gameSubscribed || Game == null) return;
            Game.ChallengeStarted += _ => RefreshGameDrivenVisibility();
            Game.ChallengeSolved += (_, __) => RefreshGameDrivenVisibility();
            Game.ChallengeTimedOut += _ => RefreshGameDrivenVisibility();
            Game.StateChanged += RefreshGameDrivenVisibility;
            _gameSubscribed = true;
        }

        private void RefreshGameDrivenVisibility()
        {
            UpdatePresetVisibility();
            UpdateGeometryVisibility();
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

        // Hide the geometry read-outs during Identify challenges where they'd give the answer away
        // (naming the molecule, or naming both the shape and AXE).
        private void UpdateGeometryVisibility()
        {
            if (_geometryStrip == null) return;
            bool givesAnswer = Game != null && Game.Mode == GameMode.Challenge && Game.Current != null
                               && (Game.Current.Objective == LearningObjective.IdentifyName
                                   || Game.Current.Objective == LearningObjective.IdentifyBoth);
            _geometryStrip.SetActive(showGeometryToggles && !givesAnswer);
        }

        // --- Construction ---------------------------------------------------------------------------

        private void BuildPanel()
        {
            EnsureStyle();

            _panelRoot = new GameObject("MoleculeXRUiPanel Root");
            _panelRoot.transform.SetPositionAndRotation(transform.position + panelWorldOffset, Quaternion.Euler(panelEulerAngles));

            _canvasGO = new GameObject("MoleculeXRUiPanel Canvas");
            _canvasGO.transform.SetParent(_panelRoot.transform, false);
            _canvasGO.transform.localScale = Vector3.one * canvasScale;

            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rt = _canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSizeMeters.x / canvasScale, panelSizeMeters.y / canvasScale);

            AddBackground(_canvasGO.transform);
            if (showGeometryToggles) BuildGeometryStrip(_canvasGO.transform);

            Transform col = PanelUi.MakeColumn(_canvasGO.transform, _style, _style.rowSpacing, fillParent: true);

            PanelUi.MakeLabel(col, _style, "Molecule Controls", _style.titleFontSize, FontStyle.Bold,
                              TextAnchor.MiddleCenter, _style.textColor, titleHeight);

            PanelUi.MakeButton(col, _style, "+ Atom", () => _controller.AddBondedAtom(), height: buttonHeight);
            PanelUi.MakeButton(col, _style, "− Atom", () => _controller.RemoveLastAtom(), height: buttonHeight);
            PanelUi.MakeButton(col, _style, "+ Lone Pair", () => _controller.AddLonePair(), height: buttonHeight);
            PanelUi.MakeButton(col, _style, "− Lone Pair", () => _controller.RemoveLastLonePair(), height: buttonHeight);
            PanelUi.MakeButton(col, _style, "Cycle Bond Order", () => _controller.CycleBondOrder(), height: buttonHeight);

            _presetSectionLabel = PanelUi.MakeLabel(col, _style, "Set bonded count:", _style.sectionFontSize,
                FontStyle.Normal, TextAnchor.MiddleLeft, _style.textColor, sectionHeight).gameObject;

            Transform row = PanelUi.MakeRow(col, presetRowHeight, _style.buttonSpacing, forceExpandWidth: false);
            _presetRow = row.gameObject;
            for (int n = 1; n <= 6; n++)
            {
                int target = n;
                PanelUi.MakeButton(row, _style, target.ToString(), () => _controller.SetBondedAtomCount(target),
                                   fontSize: presetButtonFontSize, height: 0f, fixedWidth: presetButtonWidth);
            }

            PanelUi.MakeButton(col, _style, "Reset", () => _controller.ResetMolecule(), height: buttonHeight);
            PanelUi.MakeButton(col, _style, "Recenter", () => _controller.RecenterInFront(), height: buttonHeight);

            if (_overlay != null)
                PanelUi.MakeButton(col, _style, "Toggle Angles", () => _overlay.ToggleVisible(), height: buttonHeight);

            PanelUi.AddFlexibleSpacer(col);   // keeps every button at its set height (absorbs leftover space)

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

        // --- Geometry read-out toggles --------------------------------------------------------------

        // Two checkboxes floating beside the panel. Each has an always-visible caption (Electron /
        // Molecular) and a name that pops out only while its box is checked.
        private void BuildGeometryStrip(Transform canvasT)
        {
            var strip = new GameObject("Geometry Toggles", typeof(RectTransform));
            strip.transform.SetParent(canvasT, false);
            _geometryStrip = strip;
            RectTransform r = strip.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);   // panel's top-right corner
            r.pivot = new Vector2(0f, 1f);                     // strip's top-left pins there
            r.sizeDelta = geometrySideSize;
            r.anchoredPosition = geometrySideOffset;

            var layout = strip.AddComponent<VerticalLayoutGroup>();
            layout.spacing = _style.rowSpacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true; layout.childForceExpandWidth = true;
            layout.childControlHeight = true; layout.childForceExpandHeight = false;   // rows honor geometryRowHeight

            _electronValue = MakeGeometryRow(strip.transform, "Electron");
            _molecularValue = MakeGeometryRow(strip.transform, "Molecular");
        }

        private Text MakeGeometryRow(Transform parent, string caption)
        {
            // Left-align so the checkboxes stay in a column regardless of caption/name length (otherwise a
            // shorter caption like "Electron" gets centered and its checkbox drifts right). forceExpandHeight
            // false lets the checkbox keep its own (smaller) size inside the taller text row.
            Transform row = PanelUi.MakeRow(parent, geometryRowHeight, _style.buttonSpacing,
                                            forceExpandWidth: false, childAlignment: TextAnchor.MiddleLeft,
                                            forceExpandHeight: false);

            Toggle toggle = MakeCheckbox(row);

            // wrap: false + a reserved width so the caption ("Electron"/"Molecular") always stays on one
            // line. Without this, a long popped-out value (e.g. "Trigonal Bipyramidal") squeezes the
            // caption's cell and its last letter drops to a second line.
            Text captionText = PanelUi.MakeLabel(row, _style, caption, _style.sectionFontSize, FontStyle.Bold,
                                                 TextAnchor.MiddleLeft, geometryCaptionColor, geometryRowHeight, wrap: false);
            LayoutElement capLe = captionText.GetComponent<LayoutElement>();
            capLe.minWidth = capLe.preferredWidth = captionText.preferredWidth;

            // wrap: false so long names stay on one line; preferredWidth 0 (with flexibleWidth) means the
            // value only claims the leftover space and overflows visually instead of stealing the caption's.
            Text value = PanelUi.MakeLabel(row, _style, "", _style.sectionFontSize, FontStyle.Normal,
                                           TextAnchor.MiddleLeft, geometryValueColor, geometryRowHeight, wrap: false);
            LayoutElement valLe = value.GetComponent<LayoutElement>();
            valLe.minWidth = valLe.preferredWidth = 0f;
            valLe.flexibleWidth = 1;
            value.gameObject.SetActive(false);                 // pops out when the box is checked

            toggle.onValueChanged.AddListener(on => value.gameObject.SetActive(on));
            return value;
        }

        private Toggle MakeCheckbox(Transform parent)
        {
            var go = new GameObject("Checkbox", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image box = go.AddComponent<Image>();
            if (_style.buttonCornerRadius > 0)
            {
                box.sprite = PanelUi.RoundedSprite(Mathf.Min(_style.buttonCornerRadius, 8), 0, Color.white, Color.clear);
                box.type = Image.Type.Sliced;
            }
            box.color = _style.buttonNormal;

            Toggle toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.isOn = false;

            var check = new GameObject("Check", typeof(RectTransform));
            check.transform.SetParent(go.transform, false);
            RectTransform crt = check.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.22f, 0.22f); crt.anchorMax = new Vector2(0.78f, 0.78f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            Image checkImg = check.AddComponent<Image>();
            checkImg.color = _style.goodColor;
            toggle.graphic = checkImg;                         // Toggle shows/hides this with isOn

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = geometryCheckboxSize;
            le.minHeight = le.preferredHeight = geometryCheckboxSize;
            return toggle;
        }

        private void RefreshGeometryReadouts()
        {
            VsepRMolecule m = _controller != null ? _controller.Molecule : null;
            if (m == null) return;

            int x = m.RadialAtoms.Count;
            int e = m.RadialLonePairs.Count;

            if (_electronValue != null && _electronValue.gameObject.activeSelf)
            {
                int steric = Mathf.Clamp(x + e, 0, 6);
                _electronValue.text = ElectronGeometry.GetConfiguration(steric).DisplayName;
            }
            if (_molecularValue != null && _molecularValue.gameObject.activeSelf)
            {
                _molecularValue.text = MoleculeGoal.TryGetGeometry(x, e, out MoleculeGeometry geo)
                    ? geo.DisplayName : "-";
            }
        }
    }
}
