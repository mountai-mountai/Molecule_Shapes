// "Real Molecules" panel - the Model vs Real comparison, in the same window as the DIY sandbox rather
// than a separate screen.
//
// Pick a real molecule by formula, Load it into the sim, then flip between:
//   MODEL - the ideal VSEPR angles the simulation actually forms (109.5° for water)
//   REAL  - the measured angles from the actual molecule (104.5° for water)
//
// This is the "lighter angle-comparison pass": the simulation keeps forming the ideal VSEPR geometry
// (which is what the model layer is for) and the panel reports the measured values beside it, plus WHY
// they differ. That directly targets the unit's most-missed question - which of two molecules has the
// smaller bond angle, and why - without needing a second physics path.
//
// Loading a molecule is a sandbox action; it's ignored while a scored challenge is running so it can't
// be used to skip a Build question.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Molecule_Shapes.Game;
using Molecule_Shapes.Model;   // VsepRMolecule, RealGeometry

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(MoleculeController))]
    public class MoleculeXRRealPanel : MonoBehaviour
    {
        [Header("Theme")]
        [SerializeField] private PanelStyle style;

        [Header("Placement / axis (world space)")]
        [Tooltip("Offset from the molecule's initial position (default: below it).")]
        [SerializeField] private Vector3 panelWorldOffset = new Vector3(0f, -0.42f, 0f);
        [SerializeField] private Vector3 panelEulerAngles = Vector3.zero;
        [SerializeField] private Vector2 panelSizeMeters = new Vector2(0.40f, 0.40f);
        [SerializeField] private float canvasScale = 0.001f;

        [Header("Movable")]
        [SerializeField] private bool movable = true;
        [SerializeField] private float handleThickness = 0.02f;
        [Range(0.05f, 1f)] [SerializeField] private float handleWidthFraction = 1f;
        [SerializeField] private Color handleColor = new Color(0.30f, 0.40f, 0.55f, 1f);

        [Header("Layout (UI units)")]
        [SerializeField] private float titleHeight = 44f;
        [SerializeField] private float subtitleHeight = 22f;
        [SerializeField] private float selectorHeight = 44f;
        [SerializeField] private float selectorArrowWidth = 44f;
        [SerializeField] private float selectorValueWidth = 190f;
        [SerializeField] private int selectorMinFontSize = 11;
        [SerializeField] private float buttonHeight = 46f;
        [SerializeField] private float readoutHeight = 120f;

        private MoleculeController _controller;
        private GameSessionController _game;
        private PanelStyle _style;
        private GameObject _panelRoot;

        private Text _moleculeText, _readoutText;
        private Button _modelButton, _realButton;

        [Header("Real view")]
        [Tooltip("Bond length used in Real view, in model units (the ideal model uses 10). PhET's Real " +
                 "screen holds each molecule's true bond length; switching to Model normalises it again.")]
        [SerializeField] private float realBondLength = 12f;

        private int _index;
        private bool _showReal;    // false = Model view, true = Real view
        private Text _infoText;
        private bool _showInfo;

        private RealMoleculeSpec Current => RealMoleculeSpec.All[
            Mathf.Clamp(_index, 0, RealMoleculeSpec.All.Count - 1)];

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            _game = GetComponent<GameSessionController>();
            BuildPanel();
        }

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

            _panelRoot = new GameObject("MoleculeXRRealPanel Root");
            _panelRoot.transform.SetPositionAndRotation(transform.position + panelWorldOffset,
                                                        Quaternion.Euler(panelEulerAngles));

            var canvasGO = new GameObject("MoleculeXRRealPanel Canvas");
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

            PanelUi.MakeLabel(col, _style, "Real Molecules", _style.titleFontSize, FontStyle.Bold,
                              TextAnchor.MiddleCenter, _style.textColor, titleHeight);

            PanelUi.MakeLabel(col, _style, "Molecule", _style.subtitleFontSize, FontStyle.Normal,
                              TextAnchor.MiddleCenter, _style.subtitleColor, subtitleHeight);
            _moleculeText = PanelUi.MakeSelectorRow(col, _style, selectorHeight, selectorArrowWidth,
                selectorValueWidth, selectorMinFontSize, () => Step(-1), () => Step(+1));

            // Load + a "?" that reveals the molecule's full name and where it turns up in the world.
            Transform loadRow = PanelUi.MakeRow(col, buttonHeight, _style.buttonSpacing, forceExpandWidth: false);
            PanelUi.MakeButton(loadRow, _style, "Load Molecule", LoadCurrent, fixedWidth: selectorValueWidth);
            PanelUi.MakeButton(loadRow, _style, "?", () => { _showInfo = !_showInfo; Refresh(); },
                               fixedWidth: selectorArrowWidth);

            _infoText = PanelUi.MakeLabel(col, _style, "", _style.subtitleFontSize, FontStyle.Normal,
                                          TextAnchor.UpperLeft, _style.subtitleColor, readoutHeight);
            _infoText.gameObject.SetActive(false);

            // Model / Real view toggle - the two are mutually exclusive, and the active one is shown as
            // non-interactable so it reads as "you are here".
            Transform viewRow = PanelUi.MakeRow(col, buttonHeight, _style.buttonSpacing);
            _modelButton = PanelUi.MakeButton(viewRow, _style, "Model", () => SetView(false));
            _realButton = PanelUi.MakeButton(viewRow, _style, "Real", () => SetView(true));

            // The readout is the ONLY flexible element, so it soaks up whatever space the fixed-height
            // rows leave. That's what makes the height fields behave like the other panels': buttons stay
            // exactly at their set heights and the (variable-length) text takes the remainder, instead of
            // a trailing spacer competing with it and the buttons stretching to fill.
            _readoutText = PanelUi.MakeLabel(col, _style, "", _style.bodyFontSize, FontStyle.Normal,
                                             TextAnchor.UpperLeft, _style.textColor, readoutHeight);
            LayoutElement readoutLayout = _readoutText.GetComponent<LayoutElement>();
            readoutLayout.minHeight = readoutHeight;
            readoutLayout.flexibleHeight = 1f;

            if (movable) PanelUi.AddHandle(_panelRoot.transform, panelSizeMeters, handleThickness,
                                           handleColor, handleWidthFraction);

            Refresh();
            PanelUi.ForceRebuild(col);
        }

        private void Step(int dir)
        {
            int n = RealMoleculeSpec.All.Count;
            _index = ((_index + dir) % n + n) % n;
            Refresh();
        }

        // Switching views re-aims the simulation itself: Real mode feeds the attractor the measured
        // orientations (so water actually settles at 104.5°) and holds the molecule's true bond length;
        // Model mode clears both overrides and the textbook ideals take over again.
        private void SetView(bool real)
        {
            _showReal = real;
            ApplyViewToSimulation();
            Refresh();
        }

        private void ApplyViewToSimulation()
        {
            VsepRMolecule molecule = _controller != null ? _controller.Molecule : null;
            if (molecule == null) return;

            if (_showReal)
            {
                RealMoleculeSpec m = Current;
                molecule.SetRealOrientations(
                    RealGeometry.Build(m.X, m.E, m.RealAngle, m.RealSecondaryAngle), m.X, m.E);
                molecule.BondLengthOverride = realBondLength;
            }
            else
            {
                molecule.ClearRealOrientations();
                molecule.BondLengthOverride = null;
            }
        }

        // Builds the selected molecule in the sim. Suppressed during a scored challenge so it can't be
        // used to auto-answer a Build question.
        private void LoadCurrent()
        {
            if (_game != null && _game.Session != null && _game.Session.Mode == GameMode.Challenge)
            {
                _readoutText.text = "Stop the game first to load a molecule.";
                return;
            }
            RealMoleculeSpec m = Current;
            _controller.SetConfiguration(m.X, m.E, m.BondOrder);   // draws CO2/SO2 with double bonds
            ApplyViewToSimulation();     // the new molecule needs its own real orientations
            Refresh();
        }

        private void Refresh()
        {
            RealMoleculeSpec m = Current;
            // Formula only - the full name lives behind the "?" so the selector stays readable.
            if (_moleculeText != null) _moleculeText.text = m.Formula;

            if (_infoText != null)
            {
                _infoText.gameObject.SetActive(_showInfo);
                if (_showInfo) _infoText.text = $"{m.Name}\n\n{m.FoundIn}";
            }

            // The active view's button is disabled, so it looks selected rather than pressable.
            if (_modelButton != null) _modelButton.interactable = _showReal;
            if (_realButton != null) _realButton.interactable = !_showReal;

            if (_readoutText == null) return;

            string good = Hex(_style.goodColor);
            string sub = Hex(_style.subtitleColor);

            if (_showReal)
            {
                // Real view: lead with the measured value, keep the ideal beside it for comparison. When
                // the two agree (no lone pairs) say so plainly rather than implying a difference.
                // Note: some molecules have lone pairs yet still measure at the ideal angle (XeF4's lone
                // pairs sit opposite each other and cancel), so key this on the measurement, not on E.
                string note = m.DiffersFromIdeal
                    ? m.WhyDiffers
                    : "Here the measured angle matches the model ideal exactly.";
                _readoutText.text =
                    $"<color={sub}>{m.GeometryName}</color>\n" +
                    $"Measured: <color={good}>{m.RealAngles}</color>\n" +
                    $"<color={sub}>Model ideal: {m.IdealAngles}</color>\n" +
                    $"<color={sub}>{note}</color>";
            }
            else
            {
                _readoutText.text =
                    $"<color={sub}>{m.GeometryName}</color>\n" +
                    $"Model: <color={good}>{m.IdealAngles}</color>\n" +
                    $"<color={sub}>Switch to Real to see the measured angles.</color>";
            }
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

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
